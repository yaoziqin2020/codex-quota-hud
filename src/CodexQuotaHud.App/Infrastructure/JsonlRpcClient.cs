using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;

namespace CodexQuotaHud.App.Infrastructure;

public sealed class JsonlRpcClient
{
    private readonly TextWriter _standardInput;
    private readonly TextReader _standardOutput;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _readerLock = new();
    private readonly object _terminalLock = new();
    private long _nextId;
    private Task? _readerLoop;
    private Exception? _terminalException;

    public JsonlRpcClient(TextWriter standardInput, TextReader standardOutput)
    {
        _standardInput = standardInput;
        _standardOutput = standardOutput;
    }

    public async Task<JsonElement> RequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        RegisterPending(id, completion);

        using var registration = cancellationToken.Register(() =>
        {
            CancelPending(id, cancellationToken);
        });

        EnsureReaderLoop();
        try
        {
            await WriteMessageAsync(method, id, parameters, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CancelPending(id, cancellationToken);
        }
        catch (Exception exception)
        {
            FailPendingRequest(id, exception);
        }

        return await completion.Task.ConfigureAwait(false);
    }

    public Task NotifyAsync(string method, object? parameters, CancellationToken cancellationToken) =>
        WriteMessageAsync(method, null, parameters, cancellationToken);

    private void EnsureReaderLoop()
    {
        lock (_readerLock)
        {
            _readerLoop ??= ReadLoopAsync();
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (true)
            {
                var line = await _standardOutput.ReadLineAsync().ConfigureAwait(false);
                if (line is null)
                {
                    SetTerminalException(new EndOfStreamException("Codex app-server closed its output stream."));
                    return;
                }

                using var document = JsonDocument.Parse(line);
                var message = document.RootElement;
                if (message.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("JSON-RPC message must be an object.");
                }

                if (!message.TryGetProperty("id", out var idElement))
                {
                    continue;
                }

                if (!idElement.TryGetInt64(out var id))
                {
                    throw new InvalidDataException("JSON-RPC response id must be an integer.");
                }

                if (!TryTakePending(id, out var completion))
                {
                    continue;
                }

                if (message.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind != JsonValueKind.Object)
                    {
                        completion.TrySetException(new InvalidDataException(
                            "JSON-RPC error must be an object."));
                        continue;
                    }

                    completion.TrySetException(JsonlRpcException.From(error));
                    continue;
                }

                if (!message.TryGetProperty("result", out var result))
                {
                    completion.TrySetException(new InvalidDataException(
                        "JSON-RPC response did not contain result or error."));
                    continue;
                }

                completion.TrySetResult(result.Clone());
            }
        }
        catch (JsonException exception)
        {
            SetTerminalException(new InvalidDataException("Malformed JSON-RPC line.", exception));
        }
        catch (Exception exception)
        {
            SetTerminalException(exception);
        }
    }

    private async Task WriteMessageAsync(
        string method,
        long? id,
        object? parameters,
        CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("method", method);
                if (id is not null)
                {
                    writer.WriteNumber("id", id.Value);
                }

                if (parameters is not null)
                {
                    writer.WritePropertyName("params");
                    JsonSerializer.Serialize(writer, parameters);
                }

                writer.WriteEndObject();
            }

            await _standardInput.WriteLineAsync(Encoding.UTF8.GetString(buffer.WrittenSpan)).ConfigureAwait(false);
            await _standardInput.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void RegisterPending(long id, TaskCompletionSource<JsonElement> completion)
    {
        lock (_terminalLock)
        {
            if (_terminalException is not null)
            {
                throw _terminalException;
            }

            if (!_pending.TryAdd(id, completion))
            {
                throw new InvalidOperationException("Could not register JSON-RPC request.");
            }
        }
    }

    private void CancelPending(long id, CancellationToken cancellationToken)
    {
        lock (_terminalLock)
        {
            if (_pending.TryRemove(id, out var completion))
            {
                completion.TrySetCanceled(cancellationToken);
            }
        }
    }

    private void FailPendingRequest(long id, Exception exception)
    {
        lock (_terminalLock)
        {
            if (_pending.TryRemove(id, out var completion))
            {
                completion.TrySetException(exception);
            }
        }
    }

    private bool TryTakePending(long id, out TaskCompletionSource<JsonElement> completion)
    {
        lock (_terminalLock)
        {
            return _pending.TryRemove(id, out completion!);
        }
    }

    private void SetTerminalException(Exception exception)
    {
        List<TaskCompletionSource<JsonElement>> pending;
        lock (_terminalLock)
        {
            if (_terminalException is not null)
            {
                return;
            }

            _terminalException = exception;
            pending = _pending.ToArray()
                .Where(pair => _pending.TryRemove(pair.Key, out _))
                .Select(pair => pair.Value)
                .ToList();
        }

        foreach (var completion in pending)
        {
            completion.TrySetException(exception);
        }
    }
}

public sealed class JsonlRpcException : Exception
{
    public JsonlRpcException(int code, string message)
        : base(message)
    {
        Code = code;
    }

    public int Code { get; }

    internal static JsonlRpcException From(JsonElement error)
    {
        if (error.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("JSON-RPC error must be an object.");
        }

        var code = error.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var value)
            ? value
            : 0;
        var message = error.TryGetProperty("message", out var messageElement) &&
                      messageElement.ValueKind == JsonValueKind.String
            ? messageElement.GetString() ?? "JSON-RPC request failed."
            : "JSON-RPC request failed.";
        return new JsonlRpcException(code, message);
    }
}

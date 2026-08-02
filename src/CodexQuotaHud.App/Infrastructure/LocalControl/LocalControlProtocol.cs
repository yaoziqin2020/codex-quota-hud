using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.App.Infrastructure.LocalControl;

internal static class LocalControlTimeBudget
{
    // The client starts its response timer before writing the request. Stop all
    // commit-capable server work at 1500 ms, allow 50 ms to arbitrate a success
    // committed just before cancellation, reserve 200 ms for the response
    // write, and leave 250 ms for pipe scheduling, client reads, and the final
    // pending-message probe.
    internal static readonly TimeSpan EndToEndResponse =
        TimeSpan.FromMilliseconds(2000);
    internal static readonly TimeSpan HandlerCommitWindow =
        TimeSpan.FromMilliseconds(1500);
    internal static readonly TimeSpan CommitOutcomeArbitration =
        TimeSpan.FromMilliseconds(50);
    internal static readonly TimeSpan ResponseWriteWindow =
        TimeSpan.FromMilliseconds(200);
    internal static readonly TimeSpan ClientCompletionReserve =
        TimeSpan.FromMilliseconds(250);
    internal static readonly TimeSpan PendingMessageProbe =
        TimeSpan.FromMilliseconds(25);
}

public static class LocalControlProtocol
{
    public const string PipeName = "CodexQuotaHud.LocalControl.v1";
    public const int ProtocolVersion = 1;
    public const int MaximumPayloadBytes = 4096;
    public const string ProtocolInvalidErrorCode = "control.protocol.invalid";
    public const string RequestInvalidErrorCode = "control.request.invalid";

    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan ResponseTimeout =
        LocalControlTimeBudget.EndToEndResponse;
    private static readonly TimeSpan PendingMessageProbeTimeout =
        LocalControlTimeBudget.PendingMessageProbe;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static Task WriteRequestAsync(
        Stream stream,
        LocalControlRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        var wire = new RequestWire
        {
            ProtocolVersion = request.ProtocolVersion,
            Command = "activateSkin",
            SelectionKey = request.SelectionKey
        };
        return WriteFrameAsync(stream, wire, cancellationToken);
    }

    public static async Task<LocalControlRequest> ReadRequestAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var payload = await ReadFrameAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        RequestWire wire;
        try
        {
            wire = JsonSerializer.Deserialize<RequestWire>(payload, SerializerOptions)
                ?? throw ProtocolInvalid();
        }
        catch (LocalControlProtocolException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or DecoderFallbackException or NotSupportedException)
        {
            throw ProtocolInvalid();
        }

        if (wire.ProtocolVersion != ProtocolVersion)
        {
            throw ProtocolInvalid();
        }

        if (!string.Equals(
                wire.Command,
                "activateSkin",
                StringComparison.Ordinal))
        {
            throw RequestInvalid();
        }

        var request = new LocalControlRequest(
            wire.ProtocolVersion,
            LocalControlCommandKind.ActivateSkin,
            wire.SelectionKey);
        ValidateRequest(request);
        return request;
    }

    public static Task WriteResponseAsync(
        Stream stream,
        LocalControlResponse response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        return WriteFrameAsync(stream, response, cancellationToken);
    }

    public static async Task<LocalControlResponse> ReadResponseAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var payload = await ReadFrameAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var wire = JsonSerializer.Deserialize<ResponseWire>(
                payload,
                SerializerOptions) ?? throw ProtocolInvalid();
            return new LocalControlResponse(
                wire.Succeeded,
                wire.ErrorCode,
                wire.Message);
        }
        catch (LocalControlProtocolException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or DecoderFallbackException or NotSupportedException)
        {
            throw ProtocolInvalid();
        }
    }

    private static void ValidateRequest(LocalControlRequest request)
    {
        if (request.ProtocolVersion != ProtocolVersion)
        {
            throw ProtocolInvalid();
        }

        if (request.Command != LocalControlCommandKind.ActivateSkin ||
            !SkinSelectionKey.TryGetCustomId(request.SelectionKey, out _))
        {
            throw RequestInvalid();
        }
    }

    private static async Task WriteFrameAsync<T>(
        Stream stream,
        T value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] payload;
        try
        {
            payload = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
            _ = StrictUtf8.GetCharCount(payload);
        }
        catch (Exception exception) when (
            exception is JsonException or EncoderFallbackException or NotSupportedException)
        {
            throw ProtocolInvalid();
        }

        if (payload.Length is < 1 or > MaximumPayloadBytes)
        {
            throw ProtocolInvalid();
        }

        var frame = new byte[sizeof(int) + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame, sizeof(int));
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var prefix = new byte[sizeof(int)];
        await ReadExactlyAsync(
                stream,
                prefix,
                requirePipeMessageComplete: false,
                cancellationToken)
            .ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is < 1 or > MaximumPayloadBytes)
        {
            throw ProtocolInvalid();
        }

        var payload = new byte[length];
        await ReadExactlyAsync(
                stream,
                payload,
                requirePipeMessageComplete: true,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureNoTrailingBytesAsync(stream, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            _ = StrictUtf8.GetCharCount(payload);
        }
        catch (DecoderFallbackException)
        {
            throw ProtocolInvalid();
        }

        return payload;
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        bool requirePipeMessageComplete,
        CancellationToken cancellationToken)
    {
        try
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(
                        buffer[offset..],
                        cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException();
                }

                offset += read;
                if (stream is not PipeStream pipe)
                {
                    continue;
                }

                EnsureMessageMode(pipe);
                var messageComplete = IsMessageComplete(pipe);
                if (messageComplete && offset < buffer.Length)
                {
                    throw ProtocolInvalid();
                }

                if (offset == buffer.Length &&
                    messageComplete != requirePipeMessageComplete)
                {
                    if (!messageComplete)
                    {
                        await DrainCurrentMessageAsync(pipe, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    throw ProtocolInvalid();
                }
            }
        }
        catch (EndOfStreamException)
        {
            throw ProtocolInvalid();
        }
    }

    private static async Task EnsureNoTrailingBytesAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        if (stream.CanSeek && stream.Position != stream.Length)
        {
            throw ProtocolInvalid();
        }

        if (stream is not PipeStream pipe)
        {
            return;
        }

        EnsureMessageMode(pipe);
        if (!IsMessageComplete(pipe))
        {
            await DrainCurrentMessageAsync(pipe, cancellationToken)
                .ConfigureAwait(false);
            throw ProtocolInvalid();
        }

        using var probeDeadline = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        probeDeadline.CancelAfter(PendingMessageProbeTimeout);
        try
        {
            var probe = new byte[1];
            var read = await pipe.ReadAsync(probe, probeDeadline.Token)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }

            await DrainCurrentMessageAsync(pipe, cancellationToken)
                .ConfigureAwait(false);
            throw ProtocolInvalid();
        }
        catch (OperationCanceledException) when (
            probeDeadline.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task DrainCurrentMessageAsync(
        PipeStream pipe,
        CancellationToken cancellationToken)
    {
        var drained = 0;
        var buffer = new byte[512];
        while (!IsMessageComplete(pipe) &&
               drained <= MaximumPayloadBytes + sizeof(int))
        {
            var read = await pipe.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            drained += read;
        }
    }

    private static void EnsureMessageMode(PipeStream pipe)
    {
        try
        {
            if (pipe.ReadMode != PipeTransmissionMode.Message)
            {
                throw ProtocolInvalid();
            }
        }
        catch (LocalControlProtocolException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException)
        {
            throw ProtocolInvalid();
        }
    }

    private static bool IsMessageComplete(PipeStream pipe)
    {
        try
        {
            return pipe.IsMessageComplete;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException)
        {
            throw ProtocolInvalid();
        }
    }

    private static LocalControlProtocolException ProtocolInvalid() =>
        new(ProtocolInvalidErrorCode);

    private static LocalControlProtocolException RequestInvalid() =>
        new(RequestInvalidErrorCode);

    private sealed class RequestWire
    {
        public required int ProtocolVersion { get; init; }
        public required string Command { get; init; }
        public required string SelectionKey { get; init; }
    }

    private sealed class ResponseWire
    {
        public required bool Succeeded { get; init; }
        public required string? ErrorCode { get; init; }
        public required string? Message { get; init; }
    }
}

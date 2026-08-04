using System.Buffers.Binary;
using System.IO.Pipes;
using CodexQuotaHud.App.Infrastructure.LocalControl;

namespace CodexQuotaHud.App.Tests.Infrastructure.LocalControl;

public sealed class LocalControlServerTests
{
    private const string SelectionKey =
        "custom:11111111-1111-1111-1111-111111111111";
    private static readonly LocalControlRequest Request = new(
        LocalControlProtocol.ProtocolVersion,
        LocalControlCommandKind.ActivateSkin,
        SelectionKey);

    [Fact]
    public async Task CurrentUserFactory_CreatesConnectedAsynchronousPipeEnds()
    {
        var pipeName = UniquePipeName();
        var factory = new CurrentUserLocalControlPipeFactory();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var accept = factory.AcceptAsync(pipeName, cancellation.Token);
        var client = await factory.ConnectAsync(
            pipeName,
            LocalControlProtocol.ConnectTimeout,
            cancellation.Token);
        await using var server = await accept;
        await using var connectedClient = client;

        Assert.NotNull(connectedClient);
        var serverPipe = Assert.IsType<NamedPipeServerStream>(server);
        var clientPipe = Assert.IsType<NamedPipeClientStream>(connectedClient);
        Assert.True(serverPipe.IsAsync);
        Assert.True(clientPipe.IsAsync);
        Assert.Equal(PipeTransmissionMode.Message, serverPipe.TransmissionMode);
        Assert.Equal(PipeTransmissionMode.Message, serverPipe.ReadMode);
        Assert.Equal(PipeTransmissionMode.Message, clientPipe.ReadMode);
    }

    [Fact]
    public async Task Server_AcceptsOneRequestAndReturnsOneResponse()
    {
        var pipeName = UniquePipeName();
        var calls = 0;
        await using var server = new LocalControlServer(
            pipeName,
            (request, _) =>
            {
                Interlocked.Increment(ref calls);
                Assert.Equal(Request, request);
                return Task.FromResult(new LocalControlResponse(true, null, null));
            });
        server.Start();

        var response = await new LocalControlClient(pipeName).SendAsync(Request);

        Assert.True(response.Succeeded);
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task Server_RealPipeRejectsValidRequestWithTrailingSecondFrame()
    {
        var pipeName = UniquePipeName();
        var handlerCalls = 0;
        await using var server = new LocalControlServer(
            pipeName,
            (_, _) =>
            {
                Interlocked.Increment(ref handlerCalls);
                return Task.FromResult(new LocalControlResponse(true, null, null));
            });
        server.Start();
        await using var client = await ConnectRawAsync(pipeName);
        var frames = await EncodeRequestsAsync(Request, Request);

        await client.WriteAsync(frames);
        await client.FlushAsync();
        var response = await LocalControlProtocol.ReadResponseAsync(
            client,
            CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Equal("control.protocol.invalid", response.ErrorCode);
        Assert.Equal(0, Volatile.Read(ref handlerCalls));
    }

    [Fact]
    public async Task Client_RealPipeRejectsValidResponseWithTrailingSecondFrame()
    {
        var pipeName = UniquePipeName();
        var factory = new CurrentUserLocalControlPipeFactory();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var accept = factory.AcceptAsync(pipeName, cancellation.Token);
        var maliciousServer = Task.Run(async () =>
        {
            await using var connection = await accept;
            _ = await LocalControlProtocol.ReadRequestAsync(
                connection,
                cancellation.Token);
            var frames = await EncodeResponsesAsync(
                new LocalControlResponse(true, null, null),
                new LocalControlResponse(true, null, null));
            await connection.WriteAsync(frames, cancellation.Token);
            await connection.FlushAsync(cancellation.Token);
        }, cancellation.Token);

        var response = await new LocalControlClient(pipeName).SendAsync(
            Request,
            cancellation.Token);
        await maliciousServer;

        Assert.False(response.Succeeded);
        Assert.Equal("control.protocol.invalid", response.ErrorCode);
    }

    [Fact]
    public async Task Server_RealPipeRejectsFrameSplitAcrossPipeMessages()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var pair = await ConnectPipePairAsync(cancellation.Token);
        await using var server = pair.Server;
        await using var client = pair.Client;
        var frame = await EncodeRequestsAsync(Request);

        var reading = LocalControlProtocol.ReadRequestAsync(
            server,
            cancellation.Token);
        var firstWrite = client.WriteAsync(
            frame.AsMemory(0, 2),
            cancellation.Token).AsTask();
        var secondWrite = client.WriteAsync(
            frame.AsMemory(2),
            cancellation.Token).AsTask();
        LocalControlProtocolException exception;
        try
        {
            exception = await Assert.ThrowsAsync<LocalControlProtocolException>(
                () => reading);
        }
        finally
        {
            await server.DisposeAsync();
            try
            {
                await Task.WhenAll(firstWrite, secondWrite)
                    .WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (Exception)
            {
            }
        }

        Assert.Equal("control.protocol.invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task Client_RealPipeRejectsResponseSplitAcrossPipeMessages()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var pair = await ConnectPipePairAsync(cancellation.Token);
        await using var server = pair.Server;
        await using var client = pair.Client;
        var frame = await EncodeResponsesAsync(
            new LocalControlResponse(true, null, null));

        var reading = LocalControlProtocol.ReadResponseAsync(
            client,
            cancellation.Token);
        var firstWrite = server.WriteAsync(
            frame.AsMemory(0, 2),
            cancellation.Token).AsTask();
        var secondWrite = server.WriteAsync(
            frame.AsMemory(2),
            cancellation.Token).AsTask();
        LocalControlProtocolException exception;
        try
        {
            exception = await Assert.ThrowsAsync<LocalControlProtocolException>(
                () => reading);
        }
        finally
        {
            await client.DisposeAsync();
            try
            {
                await Task.WhenAll(firstWrite, secondWrite)
                    .WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (Exception)
            {
            }
        }

        Assert.Equal("control.protocol.invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task Server_RealPipeRejectsAlreadyQueuedSecondMessage()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var pair = await ConnectPipePairAsync(cancellation.Token);
        await using var server = pair.Server;
        await using var client = pair.Client;
        var frame = await EncodeRequestsAsync(Request);

        var firstWrite = client.WriteAsync(frame, cancellation.Token).AsTask();
        var secondWrite = client.WriteAsync(frame, cancellation.Token).AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(25), cancellation.Token);
        LocalControlProtocolException exception;
        try
        {
            exception = await Assert.ThrowsAsync<LocalControlProtocolException>(
                () => LocalControlProtocol.ReadRequestAsync(
                    server,
                    cancellation.Token));
        }
        finally
        {
            await server.DisposeAsync();
            try
            {
                await Task.WhenAll(firstWrite, secondWrite)
                    .WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (Exception)
            {
            }
        }

        Assert.Equal("control.protocol.invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task Client_RealPipeRejectsAlreadyQueuedSecondResponseMessage()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var pair = await ConnectPipePairAsync(cancellation.Token);
        await using var server = pair.Server;
        await using var client = pair.Client;
        var frame = await EncodeResponsesAsync(
            new LocalControlResponse(true, null, null));

        var firstWrite = server.WriteAsync(frame, cancellation.Token).AsTask();
        var secondWrite = server.WriteAsync(frame, cancellation.Token).AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(25), cancellation.Token);
        LocalControlProtocolException exception;
        try
        {
            exception = await Assert.ThrowsAsync<LocalControlProtocolException>(
                () => LocalControlProtocol.ReadResponseAsync(
                    client,
                    cancellation.Token));
        }
        finally
        {
            await client.DisposeAsync();
            try
            {
                await Task.WhenAll(firstWrite, secondWrite)
                    .WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (Exception)
            {
            }
        }

        Assert.Equal("control.protocol.invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task Server_ContinuesAfterMalformedDisconnectedAndThrowingClients()
    {
        var pipeName = UniquePipeName();
        var handlerCalls = 0;
        await using var server = new LocalControlServer(
            pipeName,
            (_, _) =>
            {
                var call = Interlocked.Increment(ref handlerCalls);
                if (call == 1)
                {
                    throw new InvalidOperationException("handler detail");
                }

                return Task.FromResult(new LocalControlResponse(true, null, null));
            });
        server.Start();

        await using (var malformed = await ConnectRawAsync(pipeName))
        {
            await malformed.WriteAsync(new byte[] { 1, 0, 0, 0, 0xff });
            await malformed.FlushAsync();
            var rejected = await LocalControlProtocol.ReadResponseAsync(
                malformed,
                CancellationToken.None);
            Assert.False(rejected.Succeeded);
            Assert.Equal("control.protocol.invalid", rejected.ErrorCode);
        }

        await using (var disconnected = await ConnectRawAsync(pipeName))
        {
        }

        var handlerFailed = await new LocalControlClient(pipeName).SendAsync(Request);
        Assert.False(handlerFailed.Succeeded);
        Assert.Equal("control.handler.failed", handlerFailed.ErrorCode);
        Assert.DoesNotContain(
            "handler detail",
            handlerFailed.Message ?? string.Empty,
            StringComparison.Ordinal);

        var recovered = await new LocalControlClient(pipeName).SendAsync(Request);
        Assert.True(recovered.Succeeded);
        Assert.Equal(2, Volatile.Read(ref handlerCalls));
    }

    [Fact]
    public async Task Server_TimeoutCancelsHandlerBeforeAnyLateActivation()
    {
        var pipeName = UniquePipeName();
        var lateActivations = 0;
        await using var server = new LocalControlServer(
            pipeName,
            async (_, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                Interlocked.Increment(ref lateActivations);
                return new LocalControlResponse(true, null, null);
            });
        server.Start();

        var response = await new LocalControlClient(pipeName).SendAsync(Request);
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        Assert.False(response.Succeeded);
        Assert.Equal("control.timeout", response.ErrorCode);
        Assert.Equal(0, Volatile.Read(ref lateActivations));
    }

    [Fact]
    public async Task Server_RealPipeNearCommitCutoffSucceedsBeforeClientDeadline()
    {
        var pipeName = UniquePipeName();
        var commits = 0;
        await using var server = new LocalControlServer(
            pipeName,
            async (_, cancellationToken) =>
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(1400),
                    cancellationToken);
                Interlocked.Increment(ref commits);
                return new LocalControlResponse(true, null, null);
            });
        server.Start();

        var response = await new LocalControlClient(pipeName).SendAsync(Request);

        Assert.True(response.Succeeded);
        Assert.Equal(1, Volatile.Read(ref commits));
    }

    [Fact]
    public async Task Server_RealPipePastReservedCutoffTimesOutWithoutCommit()
    {
        var pipeName = UniquePipeName();
        var commits = 0;
        await using var server = new LocalControlServer(
            pipeName,
            async (_, cancellationToken) =>
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(1600),
                    cancellationToken);
                Interlocked.Increment(ref commits);
                return new LocalControlResponse(true, null, null);
            });
        server.Start();

        var response = await new LocalControlClient(pipeName).SendAsync(Request);

        Assert.False(response.Succeeded);
        Assert.Equal("control.timeout", response.ErrorCode);
        Assert.Equal("The local-control request timed out.", response.Message);
        Assert.Equal(0, Volatile.Read(ref commits));
    }

    [Fact]
    public async Task Server_CommittedSuccessCompletingDuringDeadlineArbitrationWins()
    {
        var pipeName = UniquePipeName();
        var commits = 0;
        await using var server = new LocalControlServer(
            pipeName,
            async (_, cancellationToken) =>
            {
                Interlocked.Increment(ref commits);
                try
                {
                    await Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                }

                await Task.Yield();
                return new LocalControlResponse(true, null, null);
            });
        server.Start();

        var response = await new LocalControlClient(pipeName).SendAsync(Request);

        Assert.True(response.Succeeded);
        Assert.Equal(1, Volatile.Read(ref commits));
    }

    [Fact]
    public async Task Server_NonCooperativeHandlerIsBoundedAndDoesNotBlockNextClient()
    {
        var pipeName = UniquePipeName();
        var calls = 0;
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new LocalControlServer(
            pipeName,
            async (_, _) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    firstStarted.SetResult();
                    await releaseFirst.Task;
                }

                return new LocalControlResponse(true, null, null);
            });
        server.Start();

        var first = new LocalControlClient(pipeName).SendAsync(Request);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var firstResponse = await first;
        var secondResponse = await new LocalControlClient(pipeName).SendAsync(Request);
        releaseFirst.SetResult();

        Assert.False(firstResponse.Succeeded);
        Assert.Equal("control.timeout", firstResponse.ErrorCode);
        Assert.True(secondResponse.Succeeded);
        Assert.Equal(2, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task Server_SynchronousHandlerPrefixCannotBypassDeadlineOrBlockNextClient()
    {
        var pipeName = UniquePipeName();
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFirst = new ManualResetEventSlim();
        var calls = 0;
        var server = new LocalControlServer(
            pipeName,
            (_, _) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    firstStarted.SetResult();
                    releaseFirst.Wait();
                }

                return Task.FromResult(new LocalControlResponse(true, null, null));
            });
        server.Start();

        try
        {
            var first = new LocalControlClient(pipeName).SendAsync(Request);
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var firstResponse = await first;
            var secondResponse = await new LocalControlClient(pipeName).SendAsync(Request);

            Assert.False(firstResponse.Succeeded);
            Assert.Equal("control.timeout", firstResponse.ErrorCode);
            Assert.True(secondResponse.Succeeded);
            Assert.Equal(2, Volatile.Read(ref calls));
        }
        finally
        {
            releaseFirst.Set();
            await server.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposeAsync_CancelsAndDrainsActiveHandlerBeforeReturning()
    {
        var pipeName = UniquePipeName();
        var handlerStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowHandlerToFinish = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerFinished = 0;
        var server = new LocalControlServer(
            pipeName,
            async (_, cancellationToken) =>
            {
                handlerStarted.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    cancellationObserved.SetResult();
                    await allowHandlerToFinish.Task;
                }

                Interlocked.Exchange(ref handlerFinished, 1);
                return new LocalControlResponse(true, null, null);
            });
        server.Start();
        var clientRequest = new LocalControlClient(pipeName).SendAsync(Request);
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var disposal = server.DisposeAsync().AsTask();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        Assert.False(disposal.IsCompleted);
        Assert.Equal(0, Volatile.Read(ref handlerFinished));

        allowHandlerToFinish.SetResult();
        await disposal.WaitAsync(TimeSpan.FromSeconds(2));
        _ = await clientRequest;

        Assert.Equal(1, Volatile.Read(ref handlerFinished));
    }

    [Fact]
    public async Task DisposeAsync_PermanentlyNonCooperativeHandlerHasTwoSecondHardBound()
    {
        var pipeName = UniquePipeName();
        var handlerStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var neverCompletes = new TaskCompletionSource<LocalControlResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new LocalControlServer(
            pipeName,
            (_, _) =>
            {
                handlerStarted.SetResult();
                return neverCompletes.Task;
            });
        server.Start();
        var clientRequest = new LocalControlClient(pipeName).SendAsync(Request);
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await server.DisposeAsync().AsTask().WaitAsync(
            TimeSpan.FromMilliseconds(2300));
        stopwatch.Stop();
        _ = await clientRequest;

        Assert.True(
            stopwatch.Elapsed <= TimeSpan.FromMilliseconds(2200),
            $"Dispose took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Client_UnavailableReturnsStableResultWithoutThrowing()
    {
        var client = new LocalControlClient(
            "unused",
            new ScriptedPipeFactory(connectResult: null));

        var response = await client.SendAsync(Request);

        Assert.False(response.Succeeded);
        Assert.Equal("control.unavailable", response.ErrorCode);
    }

    [Fact]
    public async Task Client_ReturnsRejectionWithoutRetrying()
    {
        var stream = await ResponseStreamAsync(new LocalControlResponse(
            false,
            "skin.activation.failed",
            "Activation failed."));
        var factory = new ScriptedPipeFactory(stream);
        var client = new LocalControlClient("unused", factory);

        var response = await client.SendAsync(Request);

        Assert.False(response.Succeeded);
        Assert.Equal("skin.activation.failed", response.ErrorCode);
        Assert.Equal(1, factory.ConnectCalls);
    }

    [Fact]
    public async Task Client_MalformedResponseReturnsProtocolFailure()
    {
        var malformed = new byte[sizeof(int) + 1];
        BinaryPrimitives.WriteInt32LittleEndian(malformed, 1);
        malformed[^1] = 0xff;
        var client = new LocalControlClient(
            "unused",
            new ScriptedPipeFactory(new ScriptedDuplexStream(malformed)));

        var response = await client.SendAsync(Request);

        Assert.False(response.Succeeded);
        Assert.Equal("control.protocol.invalid", response.ErrorCode);
    }

    [Fact]
    public async Task Client_ConnectedResponseCannotClaimServerIsUnavailable()
    {
        var stream = await ResponseStreamAsync(new LocalControlResponse(
            false,
            "control.unavailable",
            "Untrusted unavailable claim."));
        var client = new LocalControlClient(
            "unused",
            new ScriptedPipeFactory(stream));

        var response = await client.SendAsync(Request);

        Assert.False(response.Succeeded);
        Assert.Equal("control.protocol.invalid", response.ErrorCode);
    }

    [Fact]
    public async Task Client_ConnectAccessFailureIsNotTreatedAsNoServer()
    {
        var client = new LocalControlClient(
            "unused",
            new ThrowingConnectPipeFactory());

        var response = await client.SendAsync(Request);

        Assert.False(response.Succeeded);
        Assert.Equal("control.failed", response.ErrorCode);
    }

    private static async Task<NamedPipeClientStream> ConnectRawAsync(string pipeName)
    {
        var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await client.ConnectAsync(
            (int)LocalControlProtocol.ConnectTimeout.TotalMilliseconds);
        client.ReadMode = PipeTransmissionMode.Message;
        return client;
    }

    private static async Task<(Stream Server, Stream Client)> ConnectPipePairAsync(
        CancellationToken cancellationToken)
    {
        var pipeName = UniquePipeName();
        var factory = new CurrentUserLocalControlPipeFactory();
        var accept = factory.AcceptAsync(pipeName, cancellationToken);
        var client = await factory.ConnectAsync(
            pipeName,
            LocalControlProtocol.ConnectTimeout,
            cancellationToken) ?? throw new TimeoutException(
                "The test pipe client did not connect.");
        try
        {
            var server = await accept;
            return (server, client);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    private static async Task<ScriptedDuplexStream> ResponseStreamAsync(
        LocalControlResponse response)
    {
        await using var encoded = new MemoryStream();
        await LocalControlProtocol.WriteResponseAsync(
            encoded,
            response,
            CancellationToken.None);
        return new ScriptedDuplexStream(encoded.ToArray());
    }

    private static async Task<byte[]> EncodeRequestsAsync(
        params LocalControlRequest[] requests)
    {
        await using var encoded = new MemoryStream();
        foreach (var request in requests)
        {
            await LocalControlProtocol.WriteRequestAsync(
                encoded,
                request,
                CancellationToken.None);
        }

        return encoded.ToArray();
    }

    private static async Task<byte[]> EncodeResponsesAsync(
        params LocalControlResponse[] responses)
    {
        await using var encoded = new MemoryStream();
        foreach (var response in responses)
        {
            await LocalControlProtocol.WriteResponseAsync(
                encoded,
                response,
                CancellationToken.None);
        }

        return encoded.ToArray();
    }

    private static string UniquePipeName() =>
        $"CodexQuotaHud.Tests.LocalControl.{Guid.NewGuid():N}";

    private sealed class ScriptedPipeFactory(Stream? connectResult)
        : ILocalControlPipeFactory
    {
        public int ConnectCalls { get; private set; }

        public Task<Stream> AcceptAsync(
            string pipeName,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream?> ConnectAsync(
            string pipeName,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ConnectCalls++;
            return Task.FromResult(connectResult);
        }
    }

    private sealed class ThrowingConnectPipeFactory : ILocalControlPipeFactory
    {
        public Task<Stream> AcceptAsync(
            string pipeName,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Stream?> ConnectAsync(
            string pipeName,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            throw new UnauthorizedAccessException("access detail");
    }

    private sealed class ScriptedDuplexStream(byte[] response) : Stream
    {
        private int _offset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(buffer.Length, response.Length - _offset);
            if (count == 0)
            {
                return ValueTask.FromResult(0);
            }

            response.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return ValueTask.FromResult(count);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}

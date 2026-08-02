using System.Buffers.Binary;
using System.Text;
using CodexQuotaHud.App.Infrastructure.LocalControl;

namespace CodexQuotaHud.App.Tests.Infrastructure.LocalControl;

public sealed class LocalControlProtocolTests
{
    [Fact]
    public void TimeBudget_LeavesClientCompletionReserveInsideTwoSecondDeadline()
    {
        Assert.Equal(
            LocalControlTimeBudget.EndToEndResponse,
            LocalControlTimeBudget.HandlerCommitWindow +
            LocalControlTimeBudget.CommitOutcomeArbitration +
            LocalControlTimeBudget.ResponseWriteWindow +
            LocalControlTimeBudget.ClientCompletionReserve);
        Assert.Equal(
            LocalControlProtocol.ResponseTimeout,
            LocalControlTimeBudget.EndToEndResponse);
        Assert.True(
            LocalControlTimeBudget.PendingMessageProbe <
            LocalControlTimeBudget.ClientCompletionReserve);
    }

    private const string SelectionKey =
        "custom:11111111-1111-1111-1111-111111111111";

    [Fact]
    public async Task RequestFrame_UsesLittleEndianLengthAndCanonicalJson()
    {
        var request = new LocalControlRequest(
            1,
            LocalControlCommandKind.ActivateSkin,
            SelectionKey);
        await using var stream = new MemoryStream();

        await LocalControlProtocol.WriteRequestAsync(
            stream,
            request,
            CancellationToken.None);

        var frame = stream.ToArray();
        var expectedJson = Encoding.UTF8.GetBytes(
            "{\"protocolVersion\":1,\"command\":\"activateSkin\",\"selectionKey\":\"custom:11111111-1111-1111-1111-111111111111\"}");
        Assert.Equal(expectedJson.Length, BinaryPrimitives.ReadInt32LittleEndian(frame));
        Assert.Equal(expectedJson, frame[sizeof(int)..]);
    }

    [Fact]
    public async Task Request_RoundTripsOneCanonicalActivation()
    {
        var request = new LocalControlRequest(
            1,
            LocalControlCommandKind.ActivateSkin,
            SelectionKey);

        var decoded = await RoundTripRequestAsync(request);

        Assert.Equal(request, decoded);
    }

    [Fact]
    public async Task Response_RoundTripsWithoutAddingProperties()
    {
        var response = new LocalControlResponse(
            false,
            "skin.activation.failed",
            "Activation failed.");
        await using var stream = new MemoryStream();

        await LocalControlProtocol.WriteResponseAsync(
            stream,
            response,
            CancellationToken.None);
        stream.Position = 0;
        var decoded = await LocalControlProtocol.ReadResponseAsync(
            stream,
            CancellationToken.None);

        Assert.Equal(response, decoded);
    }

    [Theory]
    [InlineData("{\"succeeded\":true}")]
    [InlineData("{\"errorCode\":null,\"message\":null}")]
    [InlineData("{\"succeeded\":false,\"message\":null}")]
    [InlineData("{\"succeeded\":false,\"errorCode\":\"skin.activation.failed\"}")]
    public async Task Response_RejectsMissingRequiredMembers(string json)
    {
        await using var stream = new MemoryStream(Frame(json), writable: false);

        var failure = await Assert.ThrowsAsync<LocalControlProtocolException>(() =>
            LocalControlProtocol.ReadResponseAsync(
                stream,
                CancellationToken.None));

        Assert.Equal(LocalControlProtocol.ProtocolInvalidErrorCode, failure.ErrorCode);
    }

    [Theory]
    [InlineData("{\"command\":\"activateSkin\",\"selectionKey\":\"custom:11111111-1111-1111-1111-111111111111\"}")]
    [InlineData("{\"protocolVersion\":1,\"selectionKey\":\"custom:11111111-1111-1111-1111-111111111111\"}")]
    [InlineData("{\"protocolVersion\":1,\"command\":\"activateSkin\"}")]
    public async Task Request_RejectsMissingRequiredMembers(string json)
    {
        await using var stream = new MemoryStream(Frame(json), writable: false);

        var failure = await Assert.ThrowsAsync<LocalControlProtocolException>(() =>
            LocalControlProtocol.ReadRequestAsync(
                stream,
                CancellationToken.None));

        Assert.Equal(LocalControlProtocol.ProtocolInvalidErrorCode, failure.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Request_RejectsUnsupportedProtocolVersion(int version)
    {
        var frame = Frame(
            $"{{\"protocolVersion\":{version},\"command\":\"activateSkin\",\"selectionKey\":\"{SelectionKey}\"}}");

        await AssertFailureAsync(
            frame,
            LocalControlProtocol.ProtocolInvalidErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4097)]
    public async Task Request_RejectsPayloadLengthsOutsideBound(int length)
    {
        var frame = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(frame, length);

        await AssertFailureAsync(
            frame,
            LocalControlProtocol.ProtocolInvalidErrorCode);
    }

    [Theory]
    [MemberData(nameof(TruncatedFrames))]
    public async Task Request_RejectsTruncatedPrefixOrPayload(byte[] frame)
    {
        await AssertFailureAsync(
            frame,
            LocalControlProtocol.ProtocolInvalidErrorCode);
    }

    public static TheoryData<byte[]> TruncatedFrames => new()
    {
        new byte[] { 1, 0, 0 },
        Frame("{\"protocolVersion\":1")
            .SkipLast(1)
            .ToArray()
    };

    [Fact]
    public async Task Request_RejectsTrailingSecondFrame()
    {
        var first = Frame(
            $"{{\"protocolVersion\":1,\"command\":\"activateSkin\",\"selectionKey\":\"{SelectionKey}\"}}");
        var second = Frame(
            $"{{\"protocolVersion\":1,\"command\":\"activateSkin\",\"selectionKey\":\"{SelectionKey}\"}}");

        await AssertFailureAsync(
            [.. first, .. second],
            LocalControlProtocol.ProtocolInvalidErrorCode);
    }

    [Fact]
    public async Task Request_RejectsInvalidUtf8()
    {
        await AssertFailureAsync(
            Frame([0xff]),
            LocalControlProtocol.ProtocolInvalidErrorCode);
    }

    [Fact]
    public async Task Request_RejectsUnknownJsonProperty()
    {
        var frame = Frame(
            $"{{\"protocolVersion\":1,\"command\":\"activateSkin\",\"selectionKey\":\"{SelectionKey}\",\"extra\":true}}");

        await AssertFailureAsync(
            frame,
            LocalControlProtocol.ProtocolInvalidErrorCode);
    }

    [Fact]
    public async Task Request_RejectsUnknownCommandWithoutEchoingIt()
    {
        const string packageControlled = "run-package-controlled-text";
        var frame = Frame(
            $"{{\"protocolVersion\":1,\"command\":\"{packageControlled}\",\"selectionKey\":\"{SelectionKey}\"}}");

        var failure = await AssertFailureAsync(
            frame,
            LocalControlProtocol.RequestInvalidErrorCode);
        Assert.DoesNotContain(packageControlled, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("builtin:HudDial")]
    [InlineData("custom:AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA")]
    [InlineData("custom:11111111111111111111111111111111")]
    public async Task Request_RejectsNoncanonicalOrBuiltinSelectionKeys(string key)
    {
        var frame = Frame(
            $"{{\"protocolVersion\":1,\"command\":\"activateSkin\",\"selectionKey\":\"{key}\"}}");

        await AssertFailureAsync(
            frame,
            LocalControlProtocol.RequestInvalidErrorCode);
    }

    [Fact]
    public async Task Request_CancellationDuringPartialReadDoesNotBecomeAProtocolResult()
    {
        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, 20);
        await using var stream = new PartialThenBlockingStream(
            [.. prefix, (byte)'{']);
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LocalControlProtocol.ReadRequestAsync(
                stream,
                cancellation.Token));
    }

    private static async Task<LocalControlRequest> RoundTripRequestAsync(
        LocalControlRequest request)
    {
        await using var stream = new MemoryStream();
        await LocalControlProtocol.WriteRequestAsync(
            stream,
            request,
            CancellationToken.None);
        stream.Position = 0;
        return await LocalControlProtocol.ReadRequestAsync(
            stream,
            CancellationToken.None);
    }

    private static async Task<LocalControlProtocolException> AssertFailureAsync(
        byte[] frame,
        string expectedErrorCode)
    {
        await using var stream = new MemoryStream(frame, writable: false);
        var failure = await Assert.ThrowsAsync<LocalControlProtocolException>(() =>
            LocalControlProtocol.ReadRequestAsync(
                stream,
                CancellationToken.None));
        Assert.Equal(expectedErrorCode, failure.ErrorCode);
        return failure;
    }

    private static byte[] Frame(string json) =>
        Frame(Encoding.UTF8.GetBytes(json));

    private static byte[] Frame(byte[] payload)
    {
        var frame = new byte[sizeof(int) + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame, sizeof(int));
        return frame;
    }

    private sealed class PartialThenBlockingStream(byte[] prefix) : Stream
    {
        private int _offset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_offset < prefix.Length)
            {
                var count = Math.Min(buffer.Length, prefix.Length - _offset);
                prefix.AsMemory(_offset, count).CopyTo(buffer);
                _offset += count;
                return count;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}

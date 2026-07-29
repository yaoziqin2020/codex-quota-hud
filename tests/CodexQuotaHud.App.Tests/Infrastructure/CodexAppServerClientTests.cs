using System.Threading.Channels;
using System.Text;
using CodexQuotaHud.App.Infrastructure;
using CodexQuotaHud.Core.RateLimits;

namespace CodexQuotaHud.App.Tests.Infrastructure;

public sealed class CodexAppServerClientTests
{
    [Fact]
    public async Task ReadAsync_InitializesOnce_ThenMapsRateLimitResponse()
    {
        var process = new FakeAppServerProcess();
        IQuotaClient client = new CodexAppServerClient(
            process,
            () => DateTimeOffset.Parse("2026-07-29T00:00:00Z"));

        var firstRead = client.ReadAsync(CancellationToken.None);
        Assert.Equal(
            "{\"method\":\"initialize\",\"id\":1,\"params\":{\"clientInfo\":{\"name\":\"codex_quota_hud\",\"title\":\"Codex Quota HUD\",\"version\":\"1.0.0\"}}}",
            await process.Input.ReadLineAsync());

        process.Output.WriteLine("""{"id":1,"result":{}}""");
        Assert.Equal("{\"method\":\"initialized\"}", await process.Input.ReadLineAsync());
        Assert.Equal("{\"method\":\"account/rateLimits/read\",\"id\":2}", await process.Input.ReadLineAsync());

        process.Output.WriteLine("""
            {"id":2,"result":{"rateLimits":{"primary":{"usedPercent":38,"windowDurationMins":300,"resetsAt":1785297600}}}}
            """);
        var first = await firstRead;

        Assert.Equal(62, first.FiveHour!.RemainingPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-07-29T00:00:00Z"), first.FetchedAt);

        var secondRead = client.ReadAsync(CancellationToken.None);
        Assert.Equal("{\"method\":\"account/rateLimits/read\",\"id\":3}", await process.Input.ReadLineAsync());
        process.Output.WriteLine("""{"id":3,"result":{"rateLimits":{}}}""");
        process.Output.Complete();

        var second = await secondRead;
        Assert.Null(second.FiveHour);
        Assert.Null(second.Weekly);
    }

    [Fact]
    public async Task ReadAsync_CompletesSharedInitialization_AfterOriginalCallerCancels()
    {
        var process = new FakeAppServerProcess();
        var client = new CodexAppServerClient(process);
        using var cancellation = new CancellationTokenSource();

        var canceledRead = client.ReadAsync(cancellation.Token);
        await process.Input.ReadLineAsync();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledRead);

        process.Output.WriteLine("""{"id":1,"result":{}}""");
        Assert.Equal("{\"method\":\"initialized\"}",
            await process.Input.ReadLineAsync().WaitAsync(TimeSpan.FromMilliseconds(200)));

        var retry = client.ReadAsync(CancellationToken.None);
        Assert.Equal("{\"method\":\"account/rateLimits/read\",\"id\":2}",
            await process.Input.ReadLineAsync().WaitAsync(TimeSpan.FromMilliseconds(200)));
        process.Output.WriteLine("""{"id":2,"result":{"rateLimits":{}}}""");
        process.Output.Complete();

        await retry;
    }

    [Fact]
    public async Task ReadAsync_DoesNotRetryInitialize_AfterInitializationError()
    {
        var process = new FakeAppServerProcess();
        var client = new CodexAppServerClient(process);

        var firstRead = client.ReadAsync(CancellationToken.None);
        await process.Input.ReadLineAsync();
        process.Output.WriteLine("""{"id":1,"error":{"code":-32000,"message":"denied"}}""");
        await Assert.ThrowsAsync<JsonlRpcException>(() => firstRead);

        await Assert.ThrowsAsync<JsonlRpcException>(() =>
            client.ReadAsync(CancellationToken.None).WaitAsync(TimeSpan.FromMilliseconds(200)));
        await Assert.ThrowsAsync<TimeoutException>(() =>
            process.Input.ReadLineAsync().WaitAsync(TimeSpan.FromMilliseconds(200)));
    }

    private sealed class FakeAppServerProcess : IAppServerProcess
    {
        public RecordingTextWriter Input { get; } = new();
        public QueueTextReader Output { get; } = new();

        public TextWriter StandardInput => Input;
        public TextReader StandardOutput => Output;
        public TextReader StandardError => TextReader.Null;
        public bool HasExited => false;

        public Task KillAsync() => Task.CompletedTask;
    }

    private sealed class RecordingTextWriter : TextWriter
    {
        private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();

        public override Encoding Encoding => Encoding.UTF8;

        public override Task WriteLineAsync(string? value)
        {
            _lines.Writer.TryWrite(value ?? string.Empty);
            return Task.CompletedTask;
        }

        public async Task<string> ReadLineAsync() => await _lines.Reader.ReadAsync();
    }

    private sealed class QueueTextReader : TextReader
    {
        private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();

        public void WriteLine(string line) => _lines.Writer.TryWrite(line);
        public void Complete() => _lines.Writer.TryComplete();

        public override async Task<string?> ReadLineAsync()
        {
            try
            {
                return await _lines.Reader.ReadAsync();
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }
    }
}

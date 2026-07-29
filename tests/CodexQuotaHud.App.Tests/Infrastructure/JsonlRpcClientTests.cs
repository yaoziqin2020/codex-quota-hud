using System.Text.Json;
using CodexQuotaHud.App.Infrastructure;

namespace CodexQuotaHud.App.Tests.Infrastructure;

public sealed class JsonlRpcClientTests
{
    [Fact]
    public async Task RequestAsync_MatchesResponseById_WhenNotificationArrivesFirst()
    {
        var output = new StringWriter();
        var input = new StringReader(
            """{"method":"account/rateLimits/updated","params":{}}""" + "\n" +
            """{"id":1,"result":{"ok":true}}""" + "\n");
        var client = new JsonlRpcClient(output, input);

        var result = await client.RequestAsync("sample/read", null, CancellationToken.None);

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal("{\"method\":\"sample/read\",\"id\":1}" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task RequestAsync_ThrowsRpcException_ForErrorResponse()
    {
        var client = new JsonlRpcClient(
            new StringWriter(),
            new StringReader("""{"id":1,"error":{"code":-32000,"message":"denied"}}""" + "\n"));

        var error = await Assert.ThrowsAsync<JsonlRpcException>(() =>
            client.RequestAsync("sample/read", null, CancellationToken.None));

        Assert.Equal(-32000, error.Code);
        Assert.Equal("denied", error.Message);
    }

    [Fact]
    public async Task RequestAsync_Cancels_WhenNoResponseArrives()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new JsonlRpcClient(new StringWriter(), new NeverEndingTextReader());

        var request = client.RequestAsync("sample/read", null, cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    [Fact]
    public async Task RequestAsync_ThrowsInvalidDataException_ForMalformedLine()
    {
        var client = new JsonlRpcClient(new StringWriter(), new StringReader("not json\n"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.RequestAsync("sample/read", null, CancellationToken.None));
    }

    [Fact]
    public async Task RequestAsync_ThrowsInvalidDataException_ForMalformedErrorResponse()
    {
        var client = new JsonlRpcClient(new StringWriter(), new StringReader("""{"id":1,"error":42}""" + "\n"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.RequestAsync("sample/read", null, CancellationToken.None)
                .WaitAsync(TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public async Task RequestAsync_ThrowsEndOfStreamException_WhenOutputEndsBeforeResponse()
    {
        var client = new JsonlRpcClient(new StringWriter(), new StringReader(string.Empty));

        await Assert.ThrowsAsync<EndOfStreamException>(() =>
            client.RequestAsync("sample/read", null, CancellationToken.None));
    }

    private sealed class NeverEndingTextReader : TextReader
    {
        public override async Task<string?> ReadLineAsync()
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
            return null;
        }
    }
}

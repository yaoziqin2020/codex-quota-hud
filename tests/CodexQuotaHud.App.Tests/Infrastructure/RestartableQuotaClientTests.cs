using CodexQuotaHud.App.Infrastructure;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.RateLimits;

namespace CodexQuotaHud.App.Tests.Infrastructure;

public sealed class RestartableQuotaClientTests
{
    [Fact]
    public async Task ReadsReuseOneSessionUntilReset()
    {
        var sessions = new List<FakeSession>();
        var client = new RestartableQuotaClient(() =>
        {
            var session = new FakeSession(sessions.Count + 1);
            sessions.Add(session);
            return session;
        });

        var first = await client.ReadAsync(CancellationToken.None);
        var second = await client.ReadAsync(CancellationToken.None);

        Assert.Single(sessions);
        Assert.Equal(1, first.FiveHour!.RemainingPercent);
        Assert.Equal(1, second.FiveHour!.RemainingPercent);
    }

    [Fact]
    public async Task ResetDisposesOldSessionAndNextReadStartsFreshSession()
    {
        var sessions = new List<FakeSession>();
        var client = new RestartableQuotaClient(() =>
        {
            var session = new FakeSession(sessions.Count + 1);
            sessions.Add(session);
            return session;
        });
        await client.ReadAsync(CancellationToken.None);

        await client.ResetAsync();
        var result = await client.ReadAsync(CancellationToken.None);

        Assert.True(sessions[0].IsDisposed);
        Assert.Equal(2, result.FiveHour!.RemainingPercent);
    }

    [Fact]
    public async Task DisposeReleasesSessionAndRejectsFurtherReads()
    {
        var session = new FakeSession(1);
        var client = new RestartableQuotaClient(() => session);
        await client.ReadAsync(CancellationToken.None);

        await client.DisposeAsync();

        Assert.True(session.IsDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => client.ReadAsync(CancellationToken.None));
    }

    private sealed class FakeSession(int value) : IQuotaClient, IAsyncDisposable
    {
        public bool IsDisposed { get; private set; }

        public Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new QuotaSnapshot(
                new QuotaWindow(QuotaWindowKind.FiveHour, value, null),
                null,
                DateTimeOffset.Parse("2026-07-29T00:00:00Z")));
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}

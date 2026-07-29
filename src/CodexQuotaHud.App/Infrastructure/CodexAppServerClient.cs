using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.RateLimits;

namespace CodexQuotaHud.App.Infrastructure;

public sealed class CodexAppServerClient : IQuotaClient
{
    private readonly JsonlRpcClient _rpc;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly object _initializationLock = new();
    private Task? _initializationTask;

    public CodexAppServerClient(IAppServerProcess process, Func<DateTimeOffset>? utcNow = null)
    {
        _rpc = new JsonlRpcClient(process.StandardInput, process.StandardOutput);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        await GetInitializationTask().WaitAsync(cancellationToken);
        var result = await _rpc.RequestAsync("account/rateLimits/read", null, cancellationToken);
        return RateLimitMapper.Map(result, _utcNow());
    }

    private Task GetInitializationTask()
    {
        lock (_initializationLock)
        {
            return _initializationTask ??= InitializeAsync();
        }
    }

    private async Task InitializeAsync()
    {
        await _rpc.RequestAsync(
            "initialize",
            new
            {
                clientInfo = new
                {
                    name = "codex_quota_hud",
                    title = "Codex Quota HUD",
                    version = "1.0.0"
                }
            },
            CancellationToken.None);
        await _rpc.NotifyAsync("initialized", null, CancellationToken.None);
    }
}

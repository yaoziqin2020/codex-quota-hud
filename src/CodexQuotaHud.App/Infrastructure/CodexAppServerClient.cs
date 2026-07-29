using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.RateLimits;

namespace CodexQuotaHud.App.Infrastructure;

public sealed class CodexAppServerClient : IQuotaClient
{
    private readonly JsonlRpcClient _rpc;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public CodexAppServerClient(IAppServerProcess process, Func<DateTimeOffset>? utcNow = null)
    {
        _rpc = new JsonlRpcClient(process.StandardInput, process.StandardOutput);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        var result = await _rpc.RequestAsync("account/rateLimits/read", null, cancellationToken);
        return RateLimitMapper.Map(result, _utcNow());
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

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
                cancellationToken);
            await _rpc.NotifyAsync("initialized", null, cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }
}

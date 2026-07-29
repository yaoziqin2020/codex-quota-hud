using System.IO;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.RateLimits;

namespace CodexQuotaHud.App.Infrastructure;

public sealed class RestartableQuotaClient :
    IQuotaClient,
    IAsyncDisposable
{
    private readonly Func<IQuotaClient> _createSession;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private IQuotaClient? _session;
    private bool _disposed;

    public RestartableQuotaClient()
        : this(CreateSession)
    {
    }

    internal RestartableQuotaClient(Func<IQuotaClient> createSession)
    {
        _createSession =
            createSession ?? throw new ArgumentNullException(nameof(createSession));
    }

    public async Task<QuotaSnapshot> ReadAsync(
        CancellationToken cancellationToken)
    {
        await _sessionGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _session ??= _createSession();
            return await _session.ReadAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async Task ResetAsync()
    {
        await _sessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var session = _session;
            _session = null;
            if (session is not null)
            {
                await DisposeSessionAsync(session).ConfigureAwait(false);
            }
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _sessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var session = _session;
            _session = null;
            if (session is not null)
            {
                await DisposeSessionAsync(session).ConfigureAwait(false);
            }
        }
        finally
        {
            _sessionGate.Release();
        }

        GC.SuppressFinalize(this);
    }

    private static IQuotaClient CreateSession()
    {
        var path = new CodexExecutableLocator().Find() ??
            throw new FileNotFoundException(
                "Could not find a Codex executable that supports app-server.");
        var process = AppServerProcess.Start(path);
        return new OwnedSession(process);
    }

    private static async ValueTask DisposeSessionAsync(IQuotaClient session)
    {
        if (session is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            return;
        }

        if (session is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private sealed class OwnedSession(
        AppServerProcess process) :
        IQuotaClient,
        IAsyncDisposable
    {
        private readonly CodexAppServerClient _client = new(process);
        private int _disposed;

        public Task<QuotaSnapshot> ReadAsync(
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                this);
            return _client.ReadAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                await process.KillAsync().ConfigureAwait(false);
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}

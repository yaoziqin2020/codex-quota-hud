using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.App.Preview;

internal sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly object _sync = new();
    private AppSettings _current;

    public InMemorySettingsStore(AppSettings initial)
    {
        _current = initial ?? throw new ArgumentNullException(nameof(initial));
    }

    public AppSettings Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public AppSettings Load() => Current;

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_sync)
        {
            _current = settings;
        }
    }
}

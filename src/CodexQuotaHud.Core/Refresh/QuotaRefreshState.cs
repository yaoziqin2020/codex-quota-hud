using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.Core.Refresh;

public sealed record QuotaRefreshState(
    bool IsCodexRunning,
    bool IsRefreshing,
    QuotaDisplayState Display,
    string? LastError);

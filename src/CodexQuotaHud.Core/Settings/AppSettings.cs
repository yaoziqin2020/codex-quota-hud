using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.Core.Settings;

public sealed record AppSettings(
    double? Left = null,
    double? Top = null,
    bool AnimationsEnabled = true,
    SkinId SelectedSkin = SkinId.HudDial,
    DateTimeOffset? LastSuccessfulRefresh = null);

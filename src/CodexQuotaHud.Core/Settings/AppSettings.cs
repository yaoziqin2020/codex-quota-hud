namespace CodexQuotaHud.Core.Settings;

public sealed record AppSettings(
    double? Left = null,
    double? Top = null,
    bool AnimationsEnabled = true,
    string SelectedSkinKey = SkinSelectionKey.HudDial,
    DateTimeOffset? LastSuccessfulRefresh = null);

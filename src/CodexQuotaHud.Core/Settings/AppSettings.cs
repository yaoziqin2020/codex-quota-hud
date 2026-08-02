using System.Text.Json.Serialization;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.Core.Settings;

public sealed record AppSettings(
    double? Left = null,
    double? Top = null,
    bool AnimationsEnabled = true,
    string SelectedSkinKey = SkinSelectionKey.HudDial,
    DateTimeOffset? LastSuccessfulRefresh = null)
{
    [JsonIgnore]
    public SkinId SelectedSkin =>
        SkinSelectionKey.TryGetBuiltIn(SelectedSkinKey, out var skin)
            ? skin
            : SkinId.HudDial;
}

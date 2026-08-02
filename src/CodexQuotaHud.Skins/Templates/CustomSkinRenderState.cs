using System.Windows.Media;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.Skins.Templates;

public sealed record CustomSkinRenderState(
    double PrimaryPercent,
    double? SecondaryPercent,
    string PrimaryLabel,
    QuotaDisplayMode Mode,
    bool IsRefreshing,
    Color PrimaryRingColor,
    Color? SecondaryRingColor);

public enum CustomSkinAnimationState
{
    Hidden,
    Idle,
    Refreshing
}

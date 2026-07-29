using System.Windows;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.UI.Skins;

public partial class EnergyRingSkin : AnimatedQuotaSkin
{
    public EnergyRingSkin()
    {
        InitializeComponent();
        ConfigureRotation(
            nameof(EnergyOrbitTransform),
            idleSeconds: 22,
            refreshingSeconds: 2.6);
    }

    public override SkinId Id => SkinId.EnergyRing;

    protected override void RenderCore(QuotaSkinState state)
    {
        PrimaryArc.Progress = state.PrimaryPercent;
        SecondaryArc.Progress = state.SecondaryPercent ?? 0;
        SecondaryArc.Visibility = state.Mode == QuotaDisplayMode.Dual
            ? Visibility.Visible
            : Visibility.Collapsed;
        PercentText.Text = $"{state.PrimaryPercent:0}%";
        LabelText.Text = state.PrimaryLabel;
        RefreshGlyph.Opacity = state.IsRefreshing ? 1 : .38;
    }
}

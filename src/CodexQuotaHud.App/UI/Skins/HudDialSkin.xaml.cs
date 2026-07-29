using System.Windows;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.UI.Skins;

public partial class HudDialSkin : AnimatedQuotaSkin
{
    public HudDialSkin()
    {
        InitializeComponent();
        ConfigureRotation(
            nameof(OuterDialTransform),
            idleSeconds: 24,
            refreshingSeconds: 2.4);
        ConfigureRotation(
            nameof(InnerDialTransform),
            idleSeconds: 31,
            refreshingSeconds: 3.1,
            clockwise: false);
    }

    public override SkinId Id => SkinId.HudDial;

    protected override void RenderCore(QuotaSkinState state)
    {
        PrimaryArc.Progress = state.PrimaryPercent;
        SecondaryArc.Progress = state.SecondaryPercent ?? 0;
        SecondaryArc.Visibility = state.Mode == QuotaDisplayMode.Dual
            ? Visibility.Visible
            : Visibility.Collapsed;
        PercentText.Text = $"{state.PrimaryPercent:0}%";
        LabelText.Text = state.PrimaryLabel;
        ModeText.Text = state.IsRefreshing ? "SYNC" : "CODΞX";
    }
}

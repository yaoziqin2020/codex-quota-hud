using System.Windows;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.UI.Skins;

public partial class AuroraSkin : AnimatedQuotaSkin
{
    public AuroraSkin()
    {
        InitializeComponent();
        ConfigureRotation(
            nameof(AuroraBandTransform),
            idleSeconds: 40,
            refreshingSeconds: 3.8);
    }

    public override SkinId Id => SkinId.Aurora;

    protected override void RenderCore(QuotaSkinState state)
    {
        PrimaryArc.Progress = state.PrimaryPercent;
        SecondaryArc.Progress = state.SecondaryPercent ?? 0;
        SecondaryArc.Visibility = state.Mode == QuotaDisplayMode.Dual
            ? Visibility.Visible
            : Visibility.Collapsed;
        PercentText.Text = $"{state.PrimaryPercent:0}%";
        LabelText.Text = state.PrimaryLabel;
    }
}

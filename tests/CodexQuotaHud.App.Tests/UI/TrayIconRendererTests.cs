using System.Drawing;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class TrayIconRendererTests
{
    [Theory]
    [InlineData(QuotaDisplayMode.Dual, 84, "84")]
    [InlineData(QuotaDisplayMode.Single, 100, "100")]
    [InlineData(QuotaDisplayMode.Hidden, 0, "—")]
    public void CreateState_MapsPrimaryQuotaAndNoData(
        QuotaDisplayMode mode,
        double percent,
        string expectedText)
    {
        var state = TrayIconRenderer.CreateState(
            mode,
            percent,
            SkinId.EnergyRing);

        Assert.Equal(expectedText, state.Text);
        Assert.Equal(
            mode == QuotaDisplayMode.Hidden ? null : percent,
            state.Percent);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(32)]
    public void Render_CreatesRequestedWindowsIconSize(int size)
    {
        using var icon = TrayIconRenderer.Render(
            TrayIconRenderer.CreateState(
                QuotaDisplayMode.Dual,
                84,
                SkinId.HudDial),
            size);

        Assert.Equal(size, icon.Width);
        Assert.Equal(size, icon.Height);
    }

    [Fact]
    public void CreateState_UsesDistinctSkinAccents()
    {
        var accents = Enum.GetValues<SkinId>()
            .Select(skin => TrayIconRenderer.CreateState(
                QuotaDisplayMode.Single,
                50,
                skin).Accent.ToArgb())
            .Distinct()
            .ToArray();

        Assert.Equal(Enum.GetValues<SkinId>().Length, accents.Length);
    }

    [Theory]
    [InlineData(20, 0xFF, 0xB5, 0x47)]
    [InlineData(10, 0xFF, 0x5A, 0x67)]
    public void CreateState_LowQuotaOverridesOnlyRingAccent(
        double percent,
        byte red,
        byte green,
        byte blue)
    {
        var state = TrayIconRenderer.CreateState(
            QuotaDisplayMode.Single,
            percent,
            SkinId.Aurora);

        Assert.Equal(Color.FromArgb(red, green, blue), state.Accent);
        Assert.Equal($"{percent:0}", state.Text);
    }

    [Fact]
    public void CreateState_HiddenKeepsNoDataDashAndNormalSkinAccent()
    {
        var state = TrayIconRenderer.CreateState(
            QuotaDisplayMode.Hidden,
            0,
            SkinId.Aurora);

        Assert.Equal("\u2014", state.Text);
        Assert.Null(state.Percent);
        Assert.Equal(Color.FromArgb(0x79, 0xF3, 0xE2), state.Accent);
        Assert.NotEqual(QuotaAlertPalette.CriticalDrawingColor, state.Accent);
    }

    [Fact]
    public void Lifetime_DisposesReplacedAndFinalIcons()
    {
        System.Drawing.Icon? assigned = null;
        using var lifetime = new TrayIconLifetime(icon => assigned = icon);
        var first = TrayIconRenderer.Render(
            TrayIconRenderer.CreateState(
                QuotaDisplayMode.Single, 10, SkinId.HudDial));
        var second = TrayIconRenderer.Render(
            TrayIconRenderer.CreateState(
                QuotaDisplayMode.Single, 20, SkinId.Aurora));

        lifetime.Replace(first);
        lifetime.Replace(second);

        Assert.Same(second, assigned);
        Assert.ThrowsAny<Exception>(() => first.ToBitmap());

        lifetime.Dispose();
        Assert.Null(assigned);
        Assert.ThrowsAny<Exception>(() => second.ToBitmap());
    }
}

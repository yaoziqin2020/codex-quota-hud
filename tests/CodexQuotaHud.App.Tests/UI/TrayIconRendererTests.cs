using System.Drawing;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class TrayIconRendererTests
{
    [Fact]
    public void TraySkinItem_InvokesInjectedWindowCoordinatorWithExactKey()
    {
        var descriptor = new SkinDescriptor(
            "builtin:Aurora",
            "Aurora",
            IsBuiltIn: true,
            BuiltInId: SkinId.Aurora,
            Installed: null);
        var calls = new List<string>();
        using var item = TrayController.CreateSkinMenuItem(
            descriptor,
            key =>
            {
                calls.Add(key);
                return true;
            });

        item.PerformClick();

        Assert.Equal(["builtin:Aurora"], calls);
    }

    [Theory]
    [InlineData(21, 0x12, 0x34, 0x56)]
    [InlineData(20, 0xFF, 0xB5, 0x47)]
    [InlineData(11, 0xFF, 0xB5, 0x47)]
    [InlineData(10, 0xFF, 0x5A, 0x67)]
    [InlineData(0, 0xFF, 0x5A, 0x67)]
    public void CustomAccent_UsesPresentationColorUntilProductAlertOverridesIt(
        double percent,
        int red,
        int green,
        int blue)
    {
        var state = TrayIconRenderer.CreateState(
            QuotaDisplayMode.Single,
            percent,
            System.Drawing.Color.FromArgb(0x12, 0x34, 0x56));

        Assert.Equal(System.Drawing.Color.FromArgb(red, green, blue), state.Accent);
    }

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
            SkinPresentation.ForBuiltIn(SkinId.EnergyRing).TrayAccent);

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
                SkinPresentation.ForBuiltIn(SkinId.HudDial).TrayAccent),
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
                SkinPresentation.ForBuiltIn(skin).TrayAccent).Accent.ToArgb())
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
            SkinPresentation.ForBuiltIn(SkinId.Aurora).TrayAccent);

        Assert.Equal(Color.FromArgb(red, green, blue), state.Accent);
        Assert.Equal($"{percent:0}", state.Text);
    }

    [Fact]
    public void CreateState_HiddenKeepsNoDataDashAndNormalSkinAccent()
    {
        var state = TrayIconRenderer.CreateState(
            QuotaDisplayMode.Hidden,
            0,
            SkinPresentation.ForBuiltIn(SkinId.Aurora).TrayAccent);

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
                QuotaDisplayMode.Single,
                10,
                SkinPresentation.ForBuiltIn(SkinId.HudDial).TrayAccent));
        var second = TrayIconRenderer.Render(
            TrayIconRenderer.CreateState(
                QuotaDisplayMode.Single,
                20,
                SkinPresentation.ForBuiltIn(SkinId.Aurora).TrayAccent));

        lifetime.Replace(first);
        lifetime.Replace(second);

        Assert.Same(second, assigned);
        Assert.ThrowsAny<Exception>(() => first.ToBitmap());

        lifetime.Dispose();
        Assert.Null(assigned);
        Assert.ThrowsAny<Exception>(() => second.ToBitmap());
    }
}

using CodexQuotaHud.App.UI;
using CodexQuotaHud.Core.Models;
using System.Windows.Media;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class PopupPresentationTests
{
    [Theory]
    [InlineData(-1920, 40, EdgeDockSide.Left, true, 142, 0)]
    [InlineData(-132, 40, EdgeDockSide.Right, false, -260, 0)]
    [InlineData(-1920, 1020, EdgeDockSide.Left, true, 142, -120)]
    public void Placement_ChoosesAwayFromEdgeAndClampsVertically(
        double left,
        double top,
        EdgeDockSide dock,
        bool opensRight,
        double offsetX,
        double offsetY)
    {
        var result = PopupPlacementCalculator.Calculate(
            left,
            top,
            132,
            132,
            250,
            180,
            new WorkArea(-1920, 40, 1920, 1040),
            dock);

        Assert.Equal(opensRight, result.OpensToRight);
        Assert.Equal(offsetX, result.OffsetX);
        Assert.Equal(offsetY, result.OffsetY);
    }

    [Fact]
    public void Placement_CentersVerticallyWhenThereIsRoom()
    {
        var result = PopupPlacementCalculator.Calculate(
            100,
            300,
            132,
            132,
            250,
            180,
            new WorkArea(0, 0, 1920, 1040),
            EdgeDockSide.None);

        Assert.Equal(-24, result.OffsetY);
    }

    [Theory]
    [InlineData(EdgeDockSide.Left, 128)]
    [InlineData(EdgeDockSide.Right, -274)]
    public void Placement_UsesOuterPopupSizeWhileKeepingInnerCardTenPixelsAway(
        EdgeDockSide side,
        double expectedOuterOffset)
    {
        var orbLeft = side == EdgeDockSide.Left ? 0 : 1788;
        var result = PopupPlacementCalculator.Calculate(
            orbLeft,
            300,
            132,
            132,
            popupWidth: 278,
            popupHeight: 208,
            new WorkArea(0, 0, 1920, 1032),
            side,
            insetLeft: 14,
            insetTop: 14,
            insetRight: 14,
            insetBottom: 14);

        Assert.Equal(expectedOuterOffset, result.OffsetX);
        var innerLeft = orbLeft + result.OffsetX + 14;
        var innerRight = orbLeft + result.OffsetX + 278 - 14;
        if (side == EdgeDockSide.Left)
        {
            Assert.Equal(orbLeft + 132 + 10, innerLeft);
        }
        else
        {
            Assert.Equal(orbLeft - 10, innerRight);
        }
    }

    [Theory]
    [InlineData(EdgeDockSide.Left)]
    [InlineData(EdgeDockSide.Right)]
    [InlineData(EdgeDockSide.Top)]
    [InlineData(EdgeDockSide.Bottom)]
    public void Placement_DefaultsRightForEveryDockSideWhenRightHasRoom(
        EdgeDockSide side)
    {
        var result = PopupPlacementCalculator.Calculate(
            orbLeft: 800,
            orbTop: 300,
            orbWidth: 132,
            orbHeight: 132,
            popupWidth: 278,
            popupHeight: 208,
            new WorkArea(0, 0, 1920, 1032),
            side,
            insetLeft: 14,
            insetTop: 14,
            insetRight: 14,
            insetBottom: 14);

        Assert.Equal(PopupOpenDirection.Right, result.Direction);
        Assert.Equal(128, result.OffsetX);
        Assert.Equal(-38, result.OffsetY);
        Assert.Equal(
            800 + 132 + 10,
            800 + result.OffsetX + 14);
    }

    [Fact]
    public void Placement_FallsBackLeftOnlyWhenRightCannotFit()
    {
        var result = PopupPlacementCalculator.Calculate(
            orbLeft: 1788,
            orbTop: 300,
            orbWidth: 132,
            orbHeight: 132,
            popupWidth: 278,
            popupHeight: 208,
            new WorkArea(0, 0, 1920, 1032),
            EdgeDockSide.Top,
            insetLeft: 14,
            insetTop: 14,
            insetRight: 14,
            insetBottom: 14);

        Assert.Equal(PopupOpenDirection.Left, result.Direction);
        Assert.Equal(-274, result.OffsetX);
    }

    [Fact]
    public void Themes_CoverEverySkinWithDistinctAccents()
    {
        var themes = Enum.GetValues<SkinId>()
            .Select(PopupThemeProvider.Get)
            .ToArray();

        Assert.Equal(Enum.GetValues<SkinId>().Length, themes.Length);
        Assert.Equal(
            themes.Length,
            themes.Select(theme => theme.Accent.ToString()).Distinct().Count());
        Assert.Equal(
            themes.Length,
            themes.Select(theme => theme.Decoration).Distinct().Count());
        Assert.All(
            themes,
            theme =>
            {
                var color = Assert.IsType<SolidColorBrush>(
                    theme.SecondaryText).Color;
                Assert.True(color.R >= 0xB8);
                Assert.True(color.G >= 0xB8);
                Assert.True(color.B >= 0xB8);
            });
    }

    [Fact]
    public void EdgeProgressThemes_HaveDistinctFillsAndInternalTextures()
    {
        var themes = Enum.GetValues<SkinId>()
            .Select(EdgeProgressThemeProvider.Get)
            .ToArray();

        Assert.Equal(
            themes.Length,
            themes
                .Select(theme => string.Join(
                    ",",
                    Assert.IsType<LinearGradientBrush>(theme.Fill)
                        .GradientStops
                        .Select(stop => stop.Color.ToString())))
                .Distinct()
                .Count());
        Assert.Equal(
            themes.Length,
            themes
                .Select(theme => Assert.IsType<DrawingBrush>(
                    theme.Texture).Viewport.Width)
                .Distinct()
                .Count());
    }

    [Fact]
    public void AuroraEdgeProgress_DoesNotStartWithADarkCap()
    {
        var theme = EdgeProgressThemeProvider.Get(SkinId.Aurora);
        var fill = Assert.IsType<LinearGradientBrush>(theme.Fill);
        var start = fill.GradientStops[0].Color;
        var middle = fill.GradientStops[1].Color;

        Assert.InRange(
            Math.Abs(start.R - middle.R) +
            Math.Abs(start.G - middle.G) +
            Math.Abs(start.B - middle.B),
            0,
            40);
    }
}

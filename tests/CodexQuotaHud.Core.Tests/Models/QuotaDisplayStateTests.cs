using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.Core.Tests.Models;

public sealed class QuotaDisplayStateTests
{
    [Fact]
    public void DefaultSkinId_IsHudDial()
    {
        Assert.Equal(SkinId.HudDial, default(SkinId));
    }

    [Theory]
    [InlineData(false, false, QuotaDisplayMode.Hidden)]
    [InlineData(true, false, QuotaDisplayMode.Single)]
    [InlineData(false, true, QuotaDisplayMode.Single)]
    [InlineData(true, true, QuotaDisplayMode.Dual)]
    public void FromSnapshot_SelectsExpectedMode(
        bool hasFiveHour,
        bool hasWeekly,
        QuotaDisplayMode expected)
    {
        var snapshot = new QuotaSnapshot(
            hasFiveHour ? Window(QuotaWindowKind.FiveHour, 62) : null,
            hasWeekly ? Window(QuotaWindowKind.Weekly, 84) : null,
            DateTimeOffset.Parse("2026-07-29T00:00:00Z"));

        Assert.Equal(expected, QuotaDisplayState.FromSnapshot(snapshot).Mode);
    }

    [Fact]
    public void WeeklyOnly_UsesWeeklyAsPrimaryValue()
    {
        var weekly = Window(QuotaWindowKind.Weekly, 84);
        var state = QuotaDisplayState.FromSnapshot(
            new QuotaSnapshot(null, weekly, DateTimeOffset.UtcNow));

        Assert.Equal(QuotaWindowKind.Weekly, state.Primary!.Kind);
        Assert.Null(state.Secondary);
    }

    private static QuotaWindow Window(QuotaWindowKind kind, double remainingPercent) =>
        new(kind, remainingPercent, null);
}

using System.Text.Json;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.RateLimits;

namespace CodexQuotaHud.Core.Tests.RateLimits;

public sealed class RateLimitMapperTests
{
    private static readonly DateTimeOffset FetchedAt =
        DateTimeOffset.Parse("2026-07-29T00:00:00Z");

    [Fact]
    public void Map_RecognizesWindowsByDuration_NotPosition()
    {
        var snapshot = RateLimitMapper.Map(ReadFixture("rate-limits-dual.json"), FetchedAt);

        Assert.Equal(QuotaWindowKind.FiveHour, snapshot.FiveHour!.Kind);
        Assert.Equal(62, snapshot.FiveHour.RemainingPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1785297600), snapshot.FiveHour.ResetsAt);
        Assert.Equal(QuotaWindowKind.Weekly, snapshot.Weekly!.Kind);
        Assert.Equal(84, snapshot.Weekly.RemainingPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1785888000), snapshot.Weekly.ResetsAt);
    }

    [Fact]
    public void Map_ConvertsUsedToRemaining()
    {
        var snapshot = RateLimitMapper.Map(Parse("""
            { "rateLimits": { "primary": {
              "usedPercent": 38, "windowDurationMins": 300, "resetsAt": 1785297600
            } } }
            """), FetchedAt);

        Assert.Equal(62, snapshot.FiveHour!.RemainingPercent);
    }

    [Theory]
    [InlineData(-20, 100)]
    [InlineData(120, 0)]
    public void Map_ClampsRemainingPercent(double used, double expected)
    {
        var snapshot = RateLimitMapper.Map(Parse($$"""
            { "rateLimits": { "primary": {
              "usedPercent": {{used}}, "windowDurationMins": 300, "resetsAt": 1785297600
            } } }
            """), FetchedAt);

        Assert.Equal(expected, snapshot.FiveHour!.RemainingPercent);
    }

    [Fact]
    public void Map_MissingFiveHour_ReturnsWeeklyOnly()
    {
        var snapshot = RateLimitMapper.Map(ReadFixture("rate-limits-weekly-only.json"), FetchedAt);

        Assert.Null(snapshot.FiveHour);
        Assert.Equal(84, snapshot.Weekly!.RemainingPercent);
    }

    [Fact]
    public void Map_UnknownDuration_IsIgnored()
    {
        var snapshot = RateLimitMapper.Map(Parse("""
            { "rateLimits": { "primary": {
              "usedPercent": 20, "windowDurationMins": 60, "resetsAt": 1785297600
            } } }
            """), FetchedAt);

        Assert.Null(snapshot.FiveHour);
        Assert.Null(snapshot.Weekly);
    }

    [Fact]
    public void Map_MissingRateLimits_ReturnsEmptySnapshot()
    {
        var snapshot = RateLimitMapper.Map(Parse("{}"), FetchedAt);

        Assert.Null(snapshot.FiveHour);
        Assert.Null(snapshot.Weekly);
        Assert.Equal(FetchedAt, snapshot.FetchedAt);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    public void Map_NonObjectRoot_ReturnsEmptySnapshot(string json)
    {
        var snapshot = RateLimitMapper.Map(Parse(json), FetchedAt);

        Assert.Null(snapshot.FiveHour);
        Assert.Null(snapshot.Weekly);
        Assert.Equal(FetchedAt, snapshot.FetchedAt);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    public void Map_NonObjectRateLimits_ReturnsEmptySnapshot(string rateLimits)
    {
        var snapshot = RateLimitMapper.Map(Parse($$"""
            { "rateLimits": {{rateLimits}} }
            """), FetchedAt);

        Assert.Null(snapshot.FiveHour);
        Assert.Null(snapshot.Weekly);
        Assert.Equal(FetchedAt, snapshot.FetchedAt);
    }

    private static JsonElement ReadFixture(string name) =>
        Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

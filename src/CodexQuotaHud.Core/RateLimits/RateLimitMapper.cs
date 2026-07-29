using System.Text.Json;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.Core.RateLimits;

public static class RateLimitMapper
{
    public const int FiveHourMinutes = 300;
    public const int WeeklyMinutes = 10_080;

    public static QuotaSnapshot Map(JsonElement result, DateTimeOffset fetchedAt)
    {
        QuotaWindow? fiveHour = null;
        QuotaWindow? weekly = null;

        if (result.ValueKind == JsonValueKind.Object &&
            result.TryGetProperty("rateLimits", out var limits) &&
            limits.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "primary", "secondary" })
            {
                if (!limits.TryGetProperty(name, out var item) ||
                    item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var mapped = MapWindow(item);
                if (mapped?.Kind == QuotaWindowKind.FiveHour) fiveHour = mapped;
                if (mapped?.Kind == QuotaWindowKind.Weekly) weekly = mapped;
            }
        }

        return new QuotaSnapshot(fiveHour, weekly, fetchedAt);
    }

    private static QuotaWindow? MapWindow(JsonElement item)
    {
        if (!item.TryGetProperty("usedPercent", out var usedElement) ||
            !usedElement.TryGetDouble(out var usedPercent) ||
            !item.TryGetProperty("windowDurationMins", out var durationElement) ||
            !durationElement.TryGetInt32(out var durationMinutes))
        {
            return null;
        }

        var kind = durationMinutes switch
        {
            FiveHourMinutes => QuotaWindowKind.FiveHour,
            WeeklyMinutes => QuotaWindowKind.Weekly,
            _ => (QuotaWindowKind?)null
        };

        if (kind is null)
        {
            return null;
        }

        DateTimeOffset? resetsAt = null;
        if (item.TryGetProperty("resetsAt", out var resetsElement) &&
            resetsElement.TryGetInt64(out var unixSeconds))
        {
            resetsAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }

        return new QuotaWindow(kind.Value, ClampRemaining(100 - usedPercent), resetsAt);
    }

    private static double ClampRemaining(double remainingPercent) =>
        Math.Clamp(remainingPercent, 0, 100);
}

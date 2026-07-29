namespace CodexQuotaHud.Core.Models;

public enum QuotaWindowKind { FiveHour, Weekly }

public enum QuotaDisplayMode { Hidden, Single, Dual }

public enum SkinId { EnergyRing, LiquidGlass, HudDial, Aurora, LiquidTank }

public sealed record QuotaWindow(
    QuotaWindowKind Kind,
    double RemainingPercent,
    DateTimeOffset? ResetsAt);

public sealed record QuotaSnapshot(
    QuotaWindow? FiveHour,
    QuotaWindow? Weekly,
    DateTimeOffset FetchedAt);

public sealed record QuotaDisplayState(
    QuotaDisplayMode Mode,
    QuotaWindow? Primary,
    QuotaWindow? Secondary,
    DateTimeOffset? FetchedAt,
    bool IsStale)
{
    public static QuotaDisplayState Hidden() =>
        new(QuotaDisplayMode.Hidden, null, null, null, false);

    public static QuotaDisplayState FromSnapshot(
        QuotaSnapshot? snapshot,
        bool isStale = false)
    {
        if (snapshot is null || (snapshot.FiveHour is null && snapshot.Weekly is null))
        {
            return Hidden();
        }

        if (snapshot.FiveHour is not null && snapshot.Weekly is not null)
        {
            return new(QuotaDisplayMode.Dual, snapshot.FiveHour, snapshot.Weekly,
                snapshot.FetchedAt, isStale);
        }

        var only = snapshot.FiveHour ?? snapshot.Weekly;
        return new(QuotaDisplayMode.Single, only, null, snapshot.FetchedAt, isStale);
    }
}

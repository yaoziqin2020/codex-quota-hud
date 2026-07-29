using System.Windows;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.UI.Skins;

public sealed record QuotaSkinState
{
    private double _primaryPercent;
    private double? _secondaryPercent;
    private QuotaDisplayMode _mode;

    public QuotaSkinState(
        double PrimaryPercent,
        double? SecondaryPercent,
        string PrimaryLabel,
        QuotaDisplayMode Mode,
        bool IsRefreshing,
        bool AnimationsEnabled)
    {
        this.PrimaryPercent = PrimaryPercent;
        this.SecondaryPercent = SecondaryPercent;
        this.PrimaryLabel = PrimaryLabel ?? string.Empty;
        this.Mode = Mode;
        this.IsRefreshing = IsRefreshing;
        this.AnimationsEnabled = AnimationsEnabled;
    }

    public double PrimaryPercent
    {
        get => _primaryPercent;
        init => _primaryPercent = Normalize(value);
    }

    public double? SecondaryPercent
    {
        get => Mode == QuotaDisplayMode.Dual ? _secondaryPercent : null;
        init => _secondaryPercent = value is null ? null : Normalize(value.Value);
    }

    public string PrimaryLabel { get; init; }

    public QuotaDisplayMode Mode
    {
        get => _mode;
        init => _mode = Enum.IsDefined(value) ? value : QuotaDisplayMode.Hidden;
    }

    public bool IsRefreshing { get; init; }

    public bool AnimationsEnabled { get; init; }

    private static double Normalize(double value) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
}

public interface IQuotaSkin
{
    SkinId Id { get; }

    FrameworkElement View { get; }

    void Render(QuotaSkinState state);
}

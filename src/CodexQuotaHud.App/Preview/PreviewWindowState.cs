namespace CodexQuotaHud.App.Preview;

internal sealed record PreviewWindowState(
    double Left,
    double Top,
    double Width,
    double Height)
{
    public static PreviewWindowState Default { get; } =
        new(double.NaN, double.NaN, 380, 650);

    public bool IsValid =>
        double.IsFinite(Left) &&
        double.IsFinite(Top) &&
        double.IsFinite(Width) &&
        double.IsFinite(Height) &&
        Width >= 340 &&
        Height >= 520;
}

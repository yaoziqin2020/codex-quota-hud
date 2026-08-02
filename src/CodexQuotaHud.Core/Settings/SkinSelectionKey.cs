using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.Core.Settings;

public static class SkinSelectionKey
{
    private const string CustomPrefix = "custom:";

    public const string HudDial = "builtin:HudDial";
    public const string EnergyRing = "builtin:EnergyRing";
    public const string LiquidGlass = "builtin:LiquidGlass";
    public const string Aurora = "builtin:Aurora";
    public const string LiquidTank = "builtin:LiquidTank";

    public static string FromBuiltIn(SkinId skin) => skin switch
    {
        SkinId.HudDial => HudDial,
        SkinId.EnergyRing => EnergyRing,
        SkinId.LiquidGlass => LiquidGlass,
        SkinId.Aurora => Aurora,
        SkinId.LiquidTank => LiquidTank,
        _ => throw new ArgumentOutOfRangeException(nameof(skin))
    };

    public static bool TryGetBuiltIn(string value, out SkinId skin)
    {
        switch (value)
        {
            case HudDial:
                skin = SkinId.HudDial;
                return true;
            case EnergyRing:
                skin = SkinId.EnergyRing;
                return true;
            case LiquidGlass:
                skin = SkinId.LiquidGlass;
                return true;
            case Aurora:
                skin = SkinId.Aurora;
                return true;
            case LiquidTank:
                skin = SkinId.LiquidTank;
                return true;
            default:
                skin = default;
                return false;
        }
    }

    public static bool TryGetCustomId(string value, out Guid id)
    {
        id = default;
        if (value is null ||
            value.Length != CustomPrefix.Length + 36 ||
            !value.StartsWith(CustomPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var canonicalId = value[CustomPrefix.Length..];
        return Guid.TryParseExact(canonicalId, "D", out id) &&
               string.Equals(canonicalId, id.ToString("D"), StringComparison.Ordinal);
    }

    public static bool IsSyntacticallyValid(string value) =>
        TryGetBuiltIn(value, out _) || TryGetCustomId(value, out _);
}

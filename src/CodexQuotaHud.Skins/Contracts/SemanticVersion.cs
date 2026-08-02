using System.Globalization;

namespace CodexQuotaHud.Skins.Contracts;

public readonly record struct SemanticVersion : IComparable<SemanticVersion>
{
    public SemanticVersion(int major, int minor, int patch)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);

        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public static SemanticVersion Parse(string value)
    {
        if (TryParse(value, out var version))
        {
            return version;
        }

        throw new FormatException(
            "Semantic versions must use canonical major.minor.patch form.");
    }

    public static bool TryParse(
        string? value,
        out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var components = value.Split('.');
        if (components.Length != 3 ||
            !TryParseComponent(components[0], out var major) ||
            !TryParseComponent(components[1], out var minor) ||
            !TryParseComponent(components[2], out var patch))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        return minor != 0
            ? minor
            : Patch.CompareTo(other.Patch);
    }

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Major}.{Minor}.{Patch}");

    private static bool TryParseComponent(
        string value,
        out int component)
    {
        component = default;
        if (value.Length == 0 ||
            (value.Length > 1 && value[0] == '0'))
        {
            return false;
        }

        return int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out component);
    }
}

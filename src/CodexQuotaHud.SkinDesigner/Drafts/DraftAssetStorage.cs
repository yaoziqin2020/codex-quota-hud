using System.IO;
using System.Security.Cryptography;

namespace CodexQuotaHud.SkinDesigner.Drafts;

public static class DraftAssetStorage
{
    private const string Prefix = "assets/sha256-";
    private const int HashLength = 64;

    public static string CreateContentRelativePath(
        string canonicalPackageRelativePath,
        ReadOnlySpan<byte> content)
    {
        var extension = CanonicalExtension(canonicalPackageRelativePath);
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        return $"{Prefix}{hash}{extension}";
    }

    public static string ResolveOwnedLeaf(DraftAssetReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return Path.GetFileName(
            reference.StorageRelativePath ?? reference.RelativePath);
    }

    public static bool IsValidContentRelativePath(
        string? storageRelativePath,
        string? canonicalPackageRelativePath)
    {
        if (storageRelativePath is null || canonicalPackageRelativePath is null)
        {
            return false;
        }

        string extension;
        try
        {
            extension = CanonicalExtension(canonicalPackageRelativePath);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!storageRelativePath.StartsWith(Prefix, StringComparison.Ordinal) ||
            !storageRelativePath.EndsWith(extension, StringComparison.Ordinal) ||
            storageRelativePath.Length != Prefix.Length + HashLength + extension.Length)
        {
            return false;
        }

        var hash = storageRelativePath.AsSpan(Prefix.Length, HashLength);
        foreach (var character in hash)
        {
            if (character is not (>= '0' and <= '9') and
                not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    public static bool MatchesContent(
        DraftAssetReference reference,
        ReadOnlySpan<byte> content)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!IsValidContentRelativePath(
                reference.StorageRelativePath,
                reference.RelativePath))
        {
            return false;
        }

        var actualHash = Convert.ToHexString(SHA256.HashData(content))
            .ToLowerInvariant();
        return string.Equals(
            reference.StorageRelativePath![Prefix.Length..(Prefix.Length + HashLength)],
            actualHash,
            StringComparison.Ordinal);
    }

    internal static bool IsValidContentLeaf(string? leafName)
    {
        if (leafName is null)
        {
            return false;
        }

        var extension = leafName.EndsWith(".png", StringComparison.Ordinal)
            ? ".png"
            : leafName.EndsWith(".jpg", StringComparison.Ordinal)
                ? ".jpg"
                : null;
        return extension is not null && IsValidContentRelativePath(
            "assets/" + leafName,
            "assets/background" + extension);
    }

    private static string CanonicalExtension(string canonicalPackageRelativePath) =>
        canonicalPackageRelativePath switch
        {
            "assets/background.png" or "assets/center.png" or
                "assets/decoration.png" => ".png",
            "assets/background.jpg" or "assets/center.jpg" => ".jpg",
            _ => throw new ArgumentException(
                "The package asset path must be a canonical schema-v1 path.",
                nameof(canonicalPackageRelativePath))
        };
}

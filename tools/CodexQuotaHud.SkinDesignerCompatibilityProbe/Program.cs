using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexQuotaHud.SkinDesigner.Documents;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.SkinDesigner.Output;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Serialization;
using CodexQuotaHud.Skins.Storage;

return await RunAsync(args);

static async Task<int> RunAsync(string[] args)
{
    var startedAtUtc = DateTimeOffset.UtcNow;
    try
    {
        var options = ProbeOptions.Parse(args);
        var summary = await ExecuteAsync(options, startedAtUtc);
        Console.WriteLine(JsonSerializer.Serialize(
            summary,
            new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(
            new ProbeFailure(
                StartedAtUtc: startedAtUtc,
                FailedAtUtc: DateTimeOffset.UtcNow,
                ErrorType: exception.GetType().FullName ??
                    exception.GetType().Name,
                Message: exception.Message),
            new JsonSerializerOptions { WriteIndented = true }));
        return 1;
    }
}

static async Task<ProbeSummary> ExecuteAsync(
    ProbeOptions options,
    DateTimeOffset startedAtUtc)
{
    const double outputTextOffsetY = 7d;
    const double outputTextLineGap = 6d;
    var v123 = SemanticVersion.Parse("1.2.3");
    var v130 = SemanticVersion.Parse("1.3.0");

    Require(File.Exists(options.LegacyPackagePath),
        "The explicit legacy package does not exist.");
    var legacyPackageSha256 = HashFile(options.LegacyPackagePath);
    Require(string.Equals(
            legacyPackageSha256,
            options.ExpectedLegacyPackageSha256,
            StringComparison.Ordinal),
        "The legacy package SHA-256 did not match the explicit expectation.");

    var reader = new SkinPackageReader();
    var legacyRead = reader.ValidateFile(
        options.LegacyPackagePath,
        v130,
        CancellationToken.None);
    Require(legacyRead.IsValid,
        "The legacy package did not validate under HUD 1.3.0.");
    Require(legacyRead.Value!.Manifest.SkinId == options.ExpectedSkinId,
        "The legacy skin ID did not match the explicit expectation.");
    Require(legacyRead.Value.Manifest.MinimumHudVersion == v123,
        "The legacy minimum HUD version was not exactly 1.2.3.");
    Require(legacyRead.Value.Manifest.Assets.Count == options.ExpectedAssetCount,
        "The legacy asset count did not match the explicit expectation.");
    Require(legacyRead.Value!.Theme.TextOffsetY == 0d,
        "The legacy text offset did not default to zero.");
    Require(legacyRead.Value.Theme.TextLineGap == 0d,
        "The legacy text line gap did not default to zero.");

    using var legacyArchive = ZipFile.OpenRead(options.LegacyPackagePath);
    var legacyThemeBytes = ReadEntry(legacyArchive, "theme.json");
    var legacyTextOffsetPropertyCount = CountProperty(
        legacyThemeBytes,
        "textOffsetY");
    var legacyTextLineGapPropertyCount = CountProperty(
        legacyThemeBytes,
        "textLineGap");
    Require(legacyTextOffsetPropertyCount == 0,
        "The selected legacy package already contains textOffsetY.");
    Require(legacyTextLineGapPropertyCount == 0,
        "The selected legacy package already contains textLineGap.");
    var legacyAssetHashes = HashAssets(
        legacyArchive,
        legacyRead.Value.Manifest.Assets);
    Require(legacyAssetHashes.Length == options.ExpectedAssetCount,
        "The legacy hashed asset count did not match the expectation.");
    Require(legacyAssetHashes.All(asset => asset.Matches),
        "At least one legacy declared asset hash did not match.");

    ValidateMutationBoundaries(options);
    Directory.CreateDirectory(options.IsolatedLocalAppDataRoot);
    Directory.CreateDirectory(options.OutputRoot);

    var paths = new SkinStoragePaths(options.IsolatedLocalAppDataRoot);
    var documents = new DesignerDocumentService(
        paths,
        new DraftStore(paths),
        new InstalledSkinCatalog(paths, v130),
        reader,
        newId: () => Guid.Parse(
            "88888888-8888-4888-8888-888888888888"),
        utcNow: () => new DateTimeOffset(
            2026,
            8,
            8,
            6,
            45,
            0,
            TimeSpan.Zero));
    var imported = await documents.ImportForEditingAsync(
        options.LegacyPackagePath,
        v130,
        CancellationToken.None);
    Require(imported.Draft is not null && imported.Errors.Count == 0,
        FormatErrors("The Designer import failed", imported.Errors));
    Require(imported.Draft!.Theme.TextOffsetY == 0d,
        "The imported draft text offset did not default to zero.");
    Require(imported.Draft.Theme.TextLineGap == 0d,
        "The imported draft text line gap did not default to zero.");
    Require(imported.Draft.MinimumHudVersion == v130,
        "The imported draft minimum HUD was not normalized to 1.3.0.");

    var nonZeroDraft = imported.Draft with
    {
        ProjectName = "Task 8 Compatibility Copy",
        DisplayName = "Task 8 Compatibility Copy",
        PackageVersion = v130,
        MinimumHudVersion = v130,
        Revision = imported.Draft.Revision + 1,
        Theme = imported.Draft.Theme with
        {
            TextOffsetY = outputTextOffsetY,
            TextLineGap = outputTextLineGap
        }
    };
    var build = new DraftPackageBuilder(v130).Build(
        nonZeroDraft,
        imported.Assets);
    Require(build.IsValid,
        FormatErrors("The non-zero draft did not build", build.Errors));
    var written = new SkinPackageWriter().WriteFile(
        options.OutputPackagePath,
        build.Value!,
        overwrite: false,
        CancellationToken.None);
    Require(written.IsValid,
        FormatErrors("The non-zero package did not export", written.Errors));

    var outputRead = reader.ValidateFile(
        options.OutputPackagePath,
        v130,
        CancellationToken.None);
    Require(outputRead.IsValid,
        FormatErrors("The exported package did not validate", outputRead.Errors));
    Require(outputRead.Value!.Manifest.MinimumHudVersion == v130,
        "The exported minimum HUD was not 1.3.0.");
    Require(outputRead.Value.Manifest.PackageVersion == v130,
        "The exported package version was not 1.3.0.");
    Require(outputRead.Value.Manifest.SkinId == options.ExpectedSkinId,
        "The exported skin ID changed unexpectedly.");
    Require(outputRead.Value.Manifest.Assets.Count == options.ExpectedAssetCount,
        "The exported asset count did not match the explicit expectation.");
    Require(outputRead.Value.Theme.TextOffsetY == outputTextOffsetY,
        "The exported text offset was not exact.");
    Require(outputRead.Value.Theme.TextLineGap == outputTextLineGap,
        "The exported text line gap was not exact.");

    using var outputArchive = ZipFile.OpenRead(options.OutputPackagePath);
    var manifestBytes = ReadEntry(outputArchive, "manifest.json");
    var themeBytes = ReadEntry(outputArchive, "theme.json");
    var textOffsetPropertyCount = CountProperty(themeBytes, "textOffsetY");
    var textLineGapPropertyCount = CountProperty(themeBytes, "textLineGap");
    var manifestCanonical = manifestBytes.AsSpan().SequenceEqual(
        SkinJsonCodec.WriteManifest(outputRead.Value.Manifest));
    var themeCanonical = themeBytes.AsSpan().SequenceEqual(
        SkinJsonCodec.WriteTheme(outputRead.Value.Theme));
    Require(textOffsetPropertyCount == 1,
        "theme.json did not contain textOffsetY exactly once.");
    Require(textLineGapPropertyCount == 1,
        "theme.json did not contain textLineGap exactly once.");
    Require(manifestCanonical, "manifest.json was not canonical.");
    Require(themeCanonical, "theme.json was not canonical.");

    var assetHashes = HashAssets(
        outputArchive,
        outputRead.Value.Manifest.Assets);
    Require(assetHashes.Length == options.ExpectedAssetCount,
        "The exported hashed asset count did not match the expectation.");
    Require(assetHashes.All(asset => asset.Matches),
        "At least one declared asset hash did not match.");

    var outputPackageSha256 = HashFile(options.OutputPackagePath);
    Require(string.Equals(
            outputPackageSha256,
            options.ExpectedOutputPackageSha256,
            StringComparison.Ordinal),
        "The exported package SHA-256 did not match the deterministic expectation.");

    var oldHudRead = reader.ValidateFile(
        options.OutputPackagePath,
        v123,
        CancellationToken.None);
    var oldHudErrors = oldHudRead.Errors
        .Select(error => new ProbeError(
            error.Code,
            error.Location,
            error.Message))
        .ToArray();
    Require(!oldHudRead.IsValid && oldHudRead.Value is null,
        "HUD 1.2.3 accepted the 1.3.0 package.");
    Require(oldHudErrors.Any(error =>
            error.Code == "version.incompatible" &&
            error.Location == "$.minimumHudVersion"),
        "The old-HUD rejection did not identify minimumHudVersion.");

    return new ProbeSummary(
        ProbeVersion: 1,
        StartedAtUtc: startedAtUtc,
        CompletedAtUtc: DateTimeOffset.UtcNow,
        LegacyPackagePath: options.LegacyPackagePath,
        LegacyPackageSha256: legacyPackageSha256,
        ExpectedSkinId: options.ExpectedSkinId,
        ExpectedAssetCount: options.ExpectedAssetCount,
        LegacyDeclaredMinimumHudVersion:
            legacyRead.Value.Manifest.MinimumHudVersion.ToString(),
        LegacyTextOffsetPropertyCount: legacyTextOffsetPropertyCount,
        LegacyTextLineGapPropertyCount: legacyTextLineGapPropertyCount,
        LegacyEffectiveTextOffsetY: legacyRead.Value.Theme.TextOffsetY,
        LegacyEffectiveTextLineGap: legacyRead.Value.Theme.TextLineGap,
        LegacyAssetHashes: legacyAssetHashes,
        ImportedDraftId: imported.Draft.DraftId,
        ImportedMinimumHudVersion: imported.Draft.MinimumHudVersion.ToString(),
        OutputPackagePath: options.OutputPackagePath,
        OutputPackageSha256: outputPackageSha256,
        OutputSkinId: outputRead.Value.Manifest.SkinId,
        OutputPackageVersion:
            outputRead.Value.Manifest.PackageVersion.ToString(),
        OutputMinimumHudVersion:
            outputRead.Value.Manifest.MinimumHudVersion.ToString(),
        OutputTextOffsetY: outputRead.Value.Theme.TextOffsetY,
        OutputTextLineGap: outputRead.Value.Theme.TextLineGap,
        TextOffsetPropertyCount: textOffsetPropertyCount,
        TextLineGapPropertyCount: textLineGapPropertyCount,
        ManifestCanonical: manifestCanonical,
        ThemeCanonical: themeCanonical,
        AssetHashes: assetHashes,
        OldHudVersion: v123.ToString(),
        OldHudValid: oldHudRead.IsValid,
        OldHudErrors: oldHudErrors);
}

static void ValidateMutationBoundaries(ProbeOptions options)
{
    RequireAbsentRoot(
        options.IsolatedLocalAppDataRoot,
        "The explicit isolated LocalAppData root");
    RequireAbsentRoot(
        options.OutputRoot,
        "The explicit output root");
    Require(!IsSameOrAncestor(
            options.IsolatedLocalAppDataRoot,
            options.OutputRoot) &&
        !IsSameOrAncestor(
            options.OutputRoot,
            options.IsolatedLocalAppDataRoot),
        "The isolated LocalAppData root and output root must be distinct and non-overlapping.");
    RequireNoReparsePointAncestors(options.IsolatedLocalAppDataRoot);
    RequireNoReparsePointAncestors(options.OutputRoot);
}

static void RequireAbsentRoot(string path, string label)
{
    Require(!Directory.Exists(path) && !File.Exists(path),
        $"{label} must not already exist: {path}");
}

static bool IsSameOrAncestor(string candidate, string path)
{
    if (string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    var prefix = Path.EndsInDirectorySeparator(candidate)
        ? candidate
        : candidate + Path.DirectorySeparatorChar;
    return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
}

static void RequireNoReparsePointAncestors(string path)
{
    for (var current = new DirectoryInfo(path);
         current is not null;
         current = current.Parent)
    {
        Require(!File.Exists(current.FullName),
            $"A mutation-path ancestor is a file: {current.FullName}");
        if (current.Exists)
        {
            Require(
                (current.Attributes & FileAttributes.ReparsePoint) == 0,
                $"A mutation-path ancestor is a reparse point: {current.FullName}");
        }
    }
}

static ProbeAssetHash[] HashAssets(
    ZipArchive archive,
    IReadOnlyList<SkinAssetReference> assets) =>
    assets
        .Select(asset =>
        {
            var actual = HashBytes(ReadEntry(archive, asset.Path));
            return new ProbeAssetHash(
                Path: asset.Path,
                DeclaredSha256: asset.Sha256,
                ActualSha256: actual,
                Matches: string.Equals(
                    actual,
                    asset.Sha256,
                    StringComparison.Ordinal));
        })
        .ToArray();

static byte[] ReadEntry(ZipArchive archive, string name)
{
    var entry = archive.GetEntry(name) ??
        throw new InvalidDataException($"Missing archive entry: {name}");
    using var stream = entry.Open();
    using var buffer = new MemoryStream();
    stream.CopyTo(buffer);
    return buffer.ToArray();
}

static int CountProperty(byte[] json, string propertyName)
{
    var text = Encoding.UTF8.GetString(json);
    var token = $"\"{propertyName}\"";
    var count = 0;
    var index = 0;
    while ((index = text.IndexOf(
               token,
               index,
               StringComparison.Ordinal)) >= 0)
    {
        count++;
        index += token.Length;
    }

    return count;
}

static string FormatErrors(
    string prefix,
    IReadOnlyList<SkinValidationError> errors) =>
    errors.Count == 0
        ? $"{prefix}."
        : $"{prefix}: {string.Join(
            "; ",
            errors.Select(error =>
                $"{error.Code}|{error.Location}|{error.Message}"))}";

static string HashFile(string path) => HashBytes(File.ReadAllBytes(path));

static string HashBytes(byte[] bytes) =>
    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed record ProbeOptions(
    string LegacyPackagePath,
    string ExpectedLegacyPackageSha256,
    Guid ExpectedSkinId,
    string ExpectedOutputPackageSha256,
    int ExpectedAssetCount,
    string IsolatedLocalAppDataRoot,
    string OutputRoot,
    string OutputPackagePath)
{
    public static ProbeOptions Parse(string[] args)
    {
        if (args.Length != 14)
        {
            throw Usage();
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            var name = args[index];
            var value = args[index + 1];
            if (string.IsNullOrWhiteSpace(value) ||
                !values.TryAdd(name, value))
            {
                throw Usage();
            }
        }

        if (values.Count != 7 ||
            !values.TryGetValue("--legacy-package", out var legacyPackage) ||
            !values.TryGetValue(
                "--expected-legacy-sha256",
                out var expectedLegacyPackageSha256) ||
            !values.TryGetValue("--expected-skin-id", out var expectedSkinId) ||
            !values.TryGetValue(
                "--expected-output-sha256",
                out var expectedOutputPackageSha256) ||
            !values.TryGetValue(
                "--expected-asset-count",
                out var expectedAssetCount) ||
            !values.TryGetValue(
                "--isolated-local-app-data",
                out var isolatedLocalAppData) ||
            !values.TryGetValue("--output-package", out var outputPackage))
        {
            throw Usage();
        }

        var normalizedLegacyPackage = Path.GetFullPath(legacyPackage);
        var normalizedIsolatedLocalAppData = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(isolatedLocalAppData));
        var normalizedOutputPackage = Path.GetFullPath(outputPackage);
        var normalizedOutputRoot = Path.TrimEndingDirectorySeparator(
            Path.GetDirectoryName(normalizedOutputPackage) ?? string.Empty);
        if (!string.Equals(
                Path.GetExtension(normalizedLegacyPackage),
                ".cqskin",
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                Path.GetExtension(normalizedOutputPackage),
                ".cqskin",
                StringComparison.OrdinalIgnoreCase) ||
            IsFileSystemRoot(normalizedIsolatedLocalAppData) ||
            IsFileSystemRoot(normalizedOutputRoot) ||
            !IsSha256(expectedLegacyPackageSha256) ||
            !IsSha256(expectedOutputPackageSha256) ||
            !Guid.TryParse(expectedSkinId, out var parsedExpectedSkinId) ||
            parsedExpectedSkinId == Guid.Empty ||
            !int.TryParse(expectedAssetCount, out var parsedExpectedAssetCount) ||
            parsedExpectedAssetCount <= 0)
        {
            throw Usage();
        }

        return new ProbeOptions(
            normalizedLegacyPackage,
            expectedLegacyPackageSha256.ToLowerInvariant(),
            parsedExpectedSkinId,
            expectedOutputPackageSha256.ToLowerInvariant(),
            parsedExpectedAssetCount,
            normalizedIsolatedLocalAppData,
            normalizedOutputRoot,
            normalizedOutputPackage);
    }

    private static bool IsFileSystemRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var root = Path.GetPathRoot(path);
        return root is not null && string.Equals(
            Path.TrimEndingDirectorySeparator(root),
            Path.TrimEndingDirectorySeparator(path),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static ArgumentException Usage() => new(
        "Usage: --legacy-package <existing .cqskin> " +
        "--expected-legacy-sha256 <64 hex characters> " +
        "--expected-skin-id <non-empty GUID> " +
        "--expected-output-sha256 <64 hex characters> " +
        "--expected-asset-count <positive integer> " +
        "--isolated-local-app-data <absent directory> " +
        "--output-package <file in an absent output directory>");
}

internal sealed record ProbeSummary(
    int ProbeVersion,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string LegacyPackagePath,
    string LegacyPackageSha256,
    Guid ExpectedSkinId,
    int ExpectedAssetCount,
    string LegacyDeclaredMinimumHudVersion,
    int LegacyTextOffsetPropertyCount,
    int LegacyTextLineGapPropertyCount,
    double LegacyEffectiveTextOffsetY,
    double LegacyEffectiveTextLineGap,
    IReadOnlyList<ProbeAssetHash> LegacyAssetHashes,
    Guid ImportedDraftId,
    string ImportedMinimumHudVersion,
    string OutputPackagePath,
    string OutputPackageSha256,
    Guid OutputSkinId,
    string OutputPackageVersion,
    string OutputMinimumHudVersion,
    double OutputTextOffsetY,
    double OutputTextLineGap,
    int TextOffsetPropertyCount,
    int TextLineGapPropertyCount,
    bool ManifestCanonical,
    bool ThemeCanonical,
    IReadOnlyList<ProbeAssetHash> AssetHashes,
    string OldHudVersion,
    bool OldHudValid,
    IReadOnlyList<ProbeError> OldHudErrors);

internal sealed record ProbeAssetHash(
    string Path,
    string DeclaredSha256,
    string ActualSha256,
    bool Matches);

internal sealed record ProbeError(
    string Code,
    string Location,
    string Message);

internal sealed record ProbeFailure(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FailedAtUtc,
    string ErrorType,
    string Message);

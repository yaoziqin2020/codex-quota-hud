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
    Require(!Directory.Exists(options.IsolatedLocalAppDataRoot),
        "The explicit isolated LocalAppData root must not already exist.");
    Require(!File.Exists(options.OutputPackagePath),
        "The explicit output package must not already exist.");

    Directory.CreateDirectory(options.IsolatedLocalAppDataRoot);
    Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPackagePath)!);

    var reader = new SkinPackageReader();
    var legacyRead = reader.ValidateFile(
        options.LegacyPackagePath,
        v130,
        CancellationToken.None);
    Require(legacyRead.IsValid,
        "The legacy package did not validate under HUD 1.3.0.");
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

    var assetHashes = outputRead.Value.Manifest.Assets
        .Select(asset =>
        {
            var actual = HashBytes(ReadEntry(outputArchive, asset.Path));
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
    Require(assetHashes.All(asset => asset.Matches),
        "At least one declared asset hash did not match.");

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
        LegacyPackageSha256: HashFile(options.LegacyPackagePath),
        LegacyDeclaredMinimumHudVersion:
            legacyRead.Value.Manifest.MinimumHudVersion.ToString(),
        LegacyTextOffsetPropertyCount: legacyTextOffsetPropertyCount,
        LegacyTextLineGapPropertyCount: legacyTextLineGapPropertyCount,
        LegacyEffectiveTextOffsetY: legacyRead.Value.Theme.TextOffsetY,
        LegacyEffectiveTextLineGap: legacyRead.Value.Theme.TextLineGap,
        ImportedDraftId: imported.Draft.DraftId,
        ImportedMinimumHudVersion: imported.Draft.MinimumHudVersion.ToString(),
        OutputPackagePath: options.OutputPackagePath,
        OutputPackageSha256: HashFile(options.OutputPackagePath),
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
    string IsolatedLocalAppDataRoot,
    string OutputPackagePath)
{
    public static ProbeOptions Parse(string[] args)
    {
        if (args.Length != 6)
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

        if (!values.TryGetValue("--legacy-package", out var legacyPackage) ||
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
        if (!string.Equals(
                Path.GetExtension(normalizedLegacyPackage),
                ".cqskin",
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                Path.GetExtension(normalizedOutputPackage),
                ".cqskin",
                StringComparison.OrdinalIgnoreCase) ||
            Path.GetPathRoot(normalizedIsolatedLocalAppData) ==
                normalizedIsolatedLocalAppData)
        {
            throw Usage();
        }

        return new ProbeOptions(
            normalizedLegacyPackage,
            normalizedIsolatedLocalAppData,
            normalizedOutputPackage);
    }

    private static ArgumentException Usage() => new(
        "Usage: --legacy-package <existing .cqskin> " +
        "--isolated-local-app-data <absent directory> " +
        "--output-package <absent .cqskin>");
}

internal sealed record ProbeSummary(
    int ProbeVersion,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string LegacyPackagePath,
    string LegacyPackageSha256,
    string LegacyDeclaredMinimumHudVersion,
    int LegacyTextOffsetPropertyCount,
    int LegacyTextLineGapPropertyCount,
    double LegacyEffectiveTextOffsetY,
    double LegacyEffectiveTextLineGap,
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

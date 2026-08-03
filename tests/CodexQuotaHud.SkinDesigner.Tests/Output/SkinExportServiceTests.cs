using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using CodexQuotaHud.SkinDesigner.Output;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;

namespace CodexQuotaHud.SkinDesigner.Tests.Output;

public sealed class SkinExportServiceTests
{
    [Fact]
    public async Task ExportAsync_WritesDeterministicRevalidatablePackageWithOwnedAssetHashes()
    {
        using var root = new TemporaryRoot();
        var assets = OutputTestFixture.Assets(
            SkinAssetSlot.Decoration,
            SkinAssetSlot.Background,
            SkinAssetSlot.Center);
        var draft = OutputTestFixture.WithReferences(
            OutputTestFixture.CompleteDraft(),
            assets);
        var first = Path.Combine(root.Path, "first.cqskin");
        var second = Path.Combine(root.Path, "second.cqskin");
        var service = new SkinExportService(
            new DraftPackageBuilder(OutputTestFixture.HudVersion),
            new SkinPackageWriter());

        var firstResult = await service.ExportAsync(
            draft,
            assets,
            first,
            overwrite: false);
        var secondResult = await service.ExportAsync(
            draft,
            assets,
            second,
            overwrite: false);

        Assert.Equal(DesignerOutputDisposition.Exported, firstResult.Disposition);
        Assert.Equal(Path.GetFullPath(first), firstResult.ExportPath);
        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        var validation = new SkinPackageReader().ValidateFile(
            first,
            OutputTestFixture.HudVersion,
            CancellationToken.None);
        Assert.True(validation.IsValid, Format(validation.Errors));
        Assert.Null(validation.Value!.Manifest.OriginSkinId);
        Assert.Equal(
            [SkinAssetSlot.Background, SkinAssetSlot.Center, SkinAssetSlot.Decoration],
            validation.Value.Manifest.Assets.Select(reference => reference.Slot).ToArray());
        Assert.All(validation.Value.Manifest.Assets, reference => Assert.Equal(
            Convert.ToHexString(SHA256.HashData(assets[reference.Slot].Content))
                .ToLowerInvariant(),
            reference.Sha256));

        using var archive = ZipFile.OpenRead(first);
        Assert.DoesNotContain(archive.Entries, entry =>
            entry.FullName.Contains(draft.ProjectName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportAsync_WithoutOverwritePreservesExistingExactBytes()
    {
        using var root = new TemporaryRoot();
        var destination = Path.Combine(root.Path, "existing.cqskin");
        var original = "old exact bytes"u8.ToArray();
        File.WriteAllBytes(destination, original);
        var service = new SkinExportService(
            new DraftPackageBuilder(OutputTestFixture.HudVersion),
            new SkinPackageWriter());

        var result = await service.ExportAsync(
            OutputTestFixture.CompleteDraft(),
            OutputTestFixture.Assets(),
            destination,
            overwrite: false);

        Assert.Equal(DesignerOutputDisposition.Failed, result.Disposition);
        Assert.Contains(result.Errors, error => error.Code == "export.destination-exists");
        Assert.Equal(original, File.ReadAllBytes(destination));
        Assert.Single(Directory.EnumerateFiles(root.Path));
    }

    [Fact]
    public async Task ExportAsync_WithConfirmedOverwriteAtomicallyReplacesAndRevalidates()
    {
        using var root = new TemporaryRoot();
        var destination = Path.Combine(root.Path, "replace.cqskin");
        var original = "old exact bytes"u8.ToArray();
        File.WriteAllBytes(destination, original);
        var service = new SkinExportService(
            new DraftPackageBuilder(OutputTestFixture.HudVersion),
            new SkinPackageWriter());

        var result = await service.ExportAsync(
            OutputTestFixture.CompleteDraft(),
            OutputTestFixture.Assets(),
            destination,
            overwrite: true);

        Assert.Equal(DesignerOutputDisposition.Exported, result.Disposition);
        Assert.Equal(Path.GetFullPath(destination), result.ExportPath);
        Assert.NotEqual(original, File.ReadAllBytes(destination));
        var validation = new SkinPackageReader().ValidateFile(
            destination,
            OutputTestFixture.HudVersion,
            CancellationToken.None);
        Assert.True(validation.IsValid, Format(validation.Errors));
        Assert.Empty(EnumerateTemporaryFiles(destination));
    }

    [Fact]
    public async Task ExportAsync_WhenFinalMoveFailsPreservesExistingExactBytes()
    {
        using var root = new TemporaryRoot();
        var destination = Path.Combine(root.Path, "move-failure.cqskin");
        var original = "old exact bytes"u8.ToArray();
        File.WriteAllBytes(destination, original);
        var writer = CreateWriterWithFinalMove((source, target, overwrite) =>
        {
            Assert.True(File.Exists(source));
            Assert.Equal(Path.GetFullPath(destination), target);
            Assert.True(overwrite);
            throw new IOException("Injected final move failure.");
        });
        var service = new SkinExportService(
            new DraftPackageBuilder(OutputTestFixture.HudVersion),
            writer);

        var result = await service.ExportAsync(
            OutputTestFixture.CompleteDraft(),
            OutputTestFixture.Assets(),
            destination,
            overwrite: true);

        Assert.Equal(DesignerOutputDisposition.Failed, result.Disposition);
        Assert.Contains(result.Errors, error => error.Code == "export.failed");
        Assert.Equal(original, File.ReadAllBytes(destination));
        Assert.Empty(EnumerateTemporaryFiles(destination));
    }

    [Fact]
    public async Task ExportAsync_WhenWriterCancelsBeforeCommitReturnsCancelledAndPreservesDestination()
    {
        using var root = new TemporaryRoot();
        var destination = Path.Combine(root.Path, "cancelled.cqskin");
        var original = "old exact bytes"u8.ToArray();
        File.WriteAllBytes(destination, original);
        var service = new SkinExportService(
            new DraftPackageBuilder(OutputTestFixture.HudVersion),
            (_, _, _, _) => throw new OperationCanceledException());

        var result = await service.ExportAsync(
            OutputTestFixture.CompleteDraft(),
            OutputTestFixture.Assets(),
            destination,
            overwrite: true);

        Assert.Equal(DesignerOutputDisposition.Cancelled, result.Disposition);
        Assert.Equal(original, File.ReadAllBytes(destination));
        Assert.Empty(EnumerateTemporaryFiles(destination));
    }

    [Fact]
    public async Task ExportAsync_WhenCancellationArrivesAfterWriterCommitReportsExported()
    {
        using var root = new TemporaryRoot();
        var destination = Path.Combine(root.Path, "committed.cqskin");
        using var cancellation = new CancellationTokenSource();
        var writer = new SkinPackageWriter();
        var service = new SkinExportService(
            new DraftPackageBuilder(OutputTestFixture.HudVersion),
            (path, request, overwrite, token) =>
            {
                var written = writer.WriteFile(path, request, overwrite, token);
                Assert.True(written.IsValid, Format(written.Errors));
                cancellation.Cancel();
                return written;
            });

        var result = await service.ExportAsync(
            OutputTestFixture.CompleteDraft(),
            OutputTestFixture.Assets(),
            destination,
            overwrite: false,
            cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(DesignerOutputDisposition.Exported, result.Disposition);
        Assert.Equal(Path.GetFullPath(destination), result.ExportPath);
        Assert.True(File.Exists(destination));
        Assert.Empty(EnumerateTemporaryFiles(destination));
    }

    private static SkinPackageWriter CreateWriterWithFinalMove(
        Action<string, string, bool> finalMove)
    {
        var constructor = typeof(SkinPackageWriter).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(Action<string, string, bool>)],
            modifiers: null);
        Assert.NotNull(constructor);
        return Assert.IsType<SkinPackageWriter>(constructor.Invoke([finalMove]));
    }

    private static string[] EnumerateTemporaryFiles(string destination) =>
        Directory.GetFiles(
            Path.GetDirectoryName(destination)!,
            $"{Path.GetFileName(destination)}.*.tmp");

    private static string Format(IReadOnlyList<SkinValidationError> errors) =>
        string.Join(Environment.NewLine, errors.Select(error =>
            $"{error.Code} {error.Location}: {error.Message}"));

    private sealed class TemporaryRoot : IDisposable
    {
        public TemporaryRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CodexQuotaHud-Task15-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

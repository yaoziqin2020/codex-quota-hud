using CodexQuotaHud.App.Infrastructure.LocalControl;
using CodexQuotaHud.SkinDesigner.Output;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Tests.Output;

public sealed class SkinApplyServiceTests
{
    [Fact]
    public async Task ApplyAsync_CleanInstallUsesStrictStagingInstallReloadActivationOrder()
    {
        using var root = new TemporaryRoot();
        var order = new List<string>();
        var dialogs = new RecordingDialogs();
        string? activatedKey = null;
        var service = CreateService(
            root.Paths,
            dialogs,
            (key, _) =>
            {
                activatedKey = key;
                return Task.FromResult(new HudActivationResult(
                    HudActivationDisposition.ActivatedLive,
                    null,
                    null));
            },
            order.Add);

        var result = await service.ApplyAsync(
            OutputTestFixture.CompleteDraft(),
            OutputTestFixture.Assets());

        Assert.True(
            result.Disposition == DesignerOutputDisposition.AppliedLive,
            $"Actual: {result.Disposition}; " + string.Join(
                " | ",
                result.Errors.Select(error => $"{error.Code}: {error.Message}")));
        var installed = Assert.IsType<InstalledSkinRecord>(result.Installed);
        Assert.Equal(installed.SelectionKey, activatedKey);
        Assert.Equal(
            [
                "build", "prewrite", "postwrite", "prevalidate",
                "postvalidate", "precollision", "postcollision",
                "postdecision", "preinstall", "postinstall",
                "prereload", "postreload", "preactivation",
                "postactivation", "report", "cleanup"
            ],
            order);
        Assert.Equal(0, dialogs.CollisionChoiceCount);
        Assert.NotNull(new InstalledSkinCatalog(root.Paths, OutputTestFixture.HudVersion)
            .TryLoadSelection(installed.SelectionKey));
        AssertNoApplyOperations(root.Paths);
    }

    [Fact]
    public async Task ApplyAsync_KeepCopyActivatesReturnedNewSelectionKey()
    {
        using var root = new TemporaryRoot();
        InstallInitial(root.Paths, "1.2.3");
        var dialogs = new RecordingDialogs
        {
            CollisionDecision = SkinCollisionDecision.KeepCopy
        };
        string? activatedKey = null;
        var service = CreateService(
            root.Paths,
            dialogs,
            (key, _) =>
            {
                activatedKey = key;
                return Task.FromResult(new HudActivationResult(
                    HudActivationDisposition.ActivatedLive,
                    null,
                    null));
            });

        var result = await service.ApplyAsync(
            OutputTestFixture.CompleteDraft(packageVersion: "1.3.0"),
            OutputTestFixture.Assets());

        Assert.Equal(DesignerOutputDisposition.AppliedLive, result.Disposition);
        var installed = Assert.IsType<InstalledSkinRecord>(result.Installed);
        Assert.Equal(SkinInstallDisposition.KeptCopy, dialogs.LastInstallDisposition);
        Assert.NotEqual(
            "custom:11111111-1111-1111-1111-111111111111",
            installed.SelectionKey);
        Assert.Equal(installed.SelectionKey, activatedKey);
        Assert.Equal(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            installed.Package.Manifest.OriginSkinId);
        AssertNoApplyOperations(root.Paths);
    }

    [Fact]
    public async Task ApplyAsync_CollisionCancelPreservesInstalledBytesAndNeverActivates()
    {
        using var root = new TemporaryRoot();
        var original = InstallInitial(root.Paths, "1.2.3");
        var oldManifest = File.ReadAllBytes(Path.Combine(
            original.DirectoryPath,
            SkinPackageLimits.ManifestFileName));
        var activations = 0;
        var service = CreateService(
            root.Paths,
            new RecordingDialogs
            {
                CollisionDecision = SkinCollisionDecision.Cancel
            },
            (_, _) =>
            {
                activations++;
                return Task.FromResult(new HudActivationResult(
                    HudActivationDisposition.ActivatedLive,
                    null,
                    null));
            });

        var result = await service.ApplyAsync(
            OutputTestFixture.CompleteDraft(packageVersion: "1.3.0"),
            OutputTestFixture.Assets());

        Assert.Equal(DesignerOutputDisposition.Cancelled, result.Disposition);
        Assert.Equal(0, activations);
        Assert.Equal(
            oldManifest,
            File.ReadAllBytes(Path.Combine(
                original.DirectoryPath,
                SkinPackageLimits.ManifestFileName)));
        AssertNoApplyOperations(root.Paths);
    }

    [Theory]
    [InlineData(HudActivationDisposition.Rejected)]
    [InlineData(HudActivationDisposition.Failed)]
    public async Task ApplyAsync_ActivationFailureKeepsInstalledAndFormalSettingsExact(
        HudActivationDisposition activationDisposition)
    {
        using var root = new TemporaryRoot();
        Directory.CreateDirectory(root.Paths.SettingsRoot);
        var settingsPath = Path.Combine(root.Paths.SettingsRoot, "settings.json");
        var settings = "{\"selectedSkinKey\":\"builtin:EnergyRing\",\"other\":17}"u8.ToArray();
        File.WriteAllBytes(settingsPath, settings);
        var service = CreateService(
            root.Paths,
            new RecordingDialogs(),
            (_, _) => Task.FromResult(new HudActivationResult(
                activationDisposition,
                "control.protocol.invalid",
                "Malformed response.")));

        var result = await service.ApplyAsync(
            OutputTestFixture.CompleteDraft(),
            OutputTestFixture.Assets());

        Assert.Equal(
            DesignerOutputDisposition.InstalledNotActivated,
            result.Disposition);
        Assert.NotNull(result.Installed);
        Assert.Equal(settings, File.ReadAllBytes(settingsPath));
        Assert.Contains("HUD", result.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.NotNull(new InstalledSkinCatalog(root.Paths, OutputTestFixture.HudVersion)
            .TryLoadSelection(result.Installed!.SelectionKey));
        AssertNoApplyOperations(root.Paths);
    }

    [Fact]
    public async Task ApplyAsync_OfflineLaunchSuccessMapsToInstalledAndHudStarted()
    {
        using var root = new TemporaryRoot();
        var service = CreateService(
            root.Paths,
            new RecordingDialogs(),
            (_, _) => Task.FromResult(new HudActivationResult(
                HudActivationDisposition.StartedHud,
                null,
                null)));

        var result = await service.ApplyAsync(
            OutputTestFixture.CompleteDraft(),
            OutputTestFixture.Assets());

        Assert.Equal(
            DesignerOutputDisposition.InstalledAndHudStarted,
            result.Disposition);
        Assert.NotNull(result.Installed);
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.3.0")]
    public async Task ApplyAsync_SameIdEqualOrNewerReplacePreservesIdentity(
        string incomingVersion)
    {
        using var root = new TemporaryRoot();
        var original = InstallInitial(root.Paths, "1.2.3");
        var dialogs = new RecordingDialogs
        {
            CollisionDecision = SkinCollisionDecision.Replace
        };
        var service = CreateService(
            root.Paths,
            dialogs,
            (_, _) => Task.FromResult(new HudActivationResult(
                HudActivationDisposition.ActivatedLive,
                null,
                null)));

        var result = await service.ApplyAsync(
            OutputTestFixture.CompleteDraft(packageVersion: incomingVersion),
            OutputTestFixture.Assets());

        Assert.Equal(DesignerOutputDisposition.AppliedLive, result.Disposition);
        Assert.Equal(original.SkinId, result.Installed?.SkinId);
        Assert.Equal(
            SemanticVersion.Parse(incomingVersion),
            result.Installed?.PackageVersion);
        Assert.Equal(1, dialogs.CollisionChoiceCount);
        AssertNoApplyOperations(root.Paths);
    }

    [Fact]
    public async Task ApplyAsync_DowngradeRejectsBeforeDecisionPromotionOrActivation()
    {
        using var root = new TemporaryRoot();
        var original = InstallInitial(root.Paths, "2.0.0");
        var oldBytes = SnapshotDirectory(original.DirectoryPath);
        var dialogs = new RecordingDialogs();
        var activations = 0;
        var service = CreateService(
            root.Paths,
            dialogs,
            (_, _) =>
            {
                activations++;
                return Task.FromResult(new HudActivationResult(
                    HudActivationDisposition.ActivatedLive,
                    null,
                    null));
            });

        var result = await service.ApplyAsync(
            OutputTestFixture.CompleteDraft(packageVersion: "1.9.9"),
            OutputTestFixture.Assets());

        Assert.Equal(DesignerOutputDisposition.Failed, result.Disposition);
        Assert.Contains(result.Errors, error => error.Code == "install.downgrade");
        Assert.Equal(0, dialogs.CollisionChoiceCount);
        Assert.Equal(0, activations);
        Assert.Equal(oldBytes, SnapshotDirectory(original.DirectoryPath));
        AssertNoApplyOperations(root.Paths);
    }

    [Fact]
    public async Task ApplyAsync_InvalidDraftAssetMatchFailsBeforeStaging()
    {
        using var root = new TemporaryRoot();
        var references = OutputTestFixture.Assets(SkinAssetSlot.Background);
        var draft = OutputTestFixture.WithReferences(
            OutputTestFixture.CompleteDraft(),
            references);
        var activations = 0;
        var service = CreateService(
            root.Paths,
            new RecordingDialogs(),
            (_, _) =>
            {
                activations++;
                return Task.FromResult(new HudActivationResult(
                    HudActivationDisposition.ActivatedLive,
                    null,
                    null));
            });

        var result = await service.ApplyAsync(
            draft,
            OutputTestFixture.Assets());

        Assert.Equal(DesignerOutputDisposition.Failed, result.Disposition);
        Assert.Contains(result.Errors, error => error.Code == "draft.asset.mismatch");
        Assert.Equal(0, activations);
        Assert.False(Directory.Exists(root.Paths.ImportsRoot));
    }

    [Fact]
    public async Task ApplyAsync_StagingWriteFailureCleansOnlyExactOperation()
    {
        using var root = new TemporaryRoot();
        var sentinel = CreateOtherOperation(root.Paths);
        var service = CreateService(
            root.Paths,
            new RecordingDialogs(),
            (_, _) => throw new InvalidOperationException("activation must not run"),
            write: (_, _, _) => throw new IOException("injected stage write"));

        var result = await service.ApplyAsync(
            OutputTestFixture.CompleteDraft(),
            OutputTestFixture.Assets());

        Assert.Equal(DesignerOutputDisposition.Failed, result.Disposition);
        AssertOtherOperationOnly(root.Paths, sentinel);
        Assert.Empty(new InstalledSkinCatalog(root.Paths, OutputTestFixture.HudVersion)
            .LoadAll().Installed);
    }

    [Fact]
    public async Task ApplyAsync_StagedValidationFailureStopsBeforeCollisionAndInstall()
    {
        using var root = new TemporaryRoot();
        var sentinel = CreateOtherOperation(root.Paths);
        var inspections = 0;
        var service = CreateService(
            root.Paths,
            new RecordingDialogs(),
            (_, _) => throw new InvalidOperationException("activation must not run"),
            validate: (_, _, _, _) => new SkinValidationResult<SkinPackageDocument>(
                null,
                [new SkinValidationError(
                    "archive.invalid",
                    "$archive",
                    "Injected staged validation failure.")]),
            inspect: (_, _, _) =>
            {
                inspections++;
                throw new InvalidOperationException("inspect must not run");
            });

        var result = await service.ApplyAsync(
            OutputTestFixture.CompleteDraft(),
            OutputTestFixture.Assets());

        Assert.Equal(DesignerOutputDisposition.Failed, result.Disposition);
        Assert.Contains(result.Errors, error => error.Code == "archive.invalid");
        Assert.Equal(0, inspections);
        AssertOtherOperationOnly(root.Paths, sentinel);
    }

    [Fact]
    public async Task ApplyAsync_InstallerRollbackFailurePreservesOldExactBytesAndNeverActivates()
    {
        using var root = new TemporaryRoot();
        var original = InstallInitial(root.Paths, "1.2.3");
        var oldBytes = SnapshotDirectory(original.DirectoryPath);
        var sentinel = CreateOtherOperation(root.Paths);
        var activations = 0;
        var service = CreateService(
            root.Paths,
            new RecordingDialogs
            {
                CollisionDecision = SkinCollisionDecision.Replace
            },
            (_, _) =>
            {
                activations++;
                return Task.FromResult(new HudActivationResult(
                    HudActivationDisposition.ActivatedLive,
                    null,
                    null));
            },
            install: (_, _, _) => new SkinInstallResult(
                SkinInstallDisposition.Cancelled,
                null,
                [new SkinValidationError(
                    "install.rollback-failed",
                    "$install",
                    "Injected Task5 rollback result.")]));

        var result = await service.ApplyAsync(
            OutputTestFixture.CompleteDraft(packageVersion: "1.3.0"),
            OutputTestFixture.Assets());

        Assert.Equal(DesignerOutputDisposition.Failed, result.Disposition);
        Assert.Contains(result.Errors, error => error.Code == "install.rollback-failed");
        Assert.Equal(0, activations);
        Assert.Equal(oldBytes, SnapshotDirectory(original.DirectoryPath));
        AssertOtherOperationOnly(root.Paths, sentinel);
    }

    [Fact]
    public async Task ApplyAsync_UnhealthyReloadNeverRequestsActivation()
    {
        using var root = new TemporaryRoot();
        var activations = 0;
        var service = CreateService(
            root.Paths,
            new RecordingDialogs(),
            (_, _) =>
            {
                activations++;
                return Task.FromResult(new HudActivationResult(
                    HudActivationDisposition.ActivatedLive,
                    null,
                    null));
            },
            reload: _ => null);

        var result = await service.ApplyAsync(
            OutputTestFixture.CompleteDraft(),
            OutputTestFixture.Assets());

        Assert.Equal(
            DesignerOutputDisposition.InstalledNotActivated,
            result.Disposition);
        Assert.Contains(result.Errors, error => error.Code == "install.reload.failed");
        Assert.Equal(0, activations);
        Assert.NotNull(result.Installed);
    }

    [Fact]
    public async Task ApplyAsync_CleanupFailurePreservesCommittedDispositionButWarnsActionably()
    {
        using var root = new TemporaryRoot();
        var staging = new CleanupFailingStagingProvider(
            PhysicalApplyStagingLeaseProvider.Instance);
        var service = CreateService(
            root.Paths,
            new RecordingDialogs(),
            (_, _) => Task.FromResult(new HudActivationResult(
                HudActivationDisposition.ActivatedLive,
                null,
                null)),
            staging: staging);

        var result = await service.ApplyAsync(
            OutputTestFixture.CompleteDraft(),
            OutputTestFixture.Assets());

        Assert.Equal(DesignerOutputDisposition.AppliedLive, result.Disposition);
        Assert.NotNull(result.Installed);
        Assert.Contains(result.Errors, error => error.Code == "apply.cleanup-failed");
        Assert.Contains(
            "could not be cleaned up",
            result.Message ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            Path.GetFileName(staging.OperationPath),
            result.Message ?? string.Empty,
            StringComparison.Ordinal);
        Assert.True(Directory.Exists(staging.OperationPath));
        Assert.NotNull(new InstalledSkinCatalog(root.Paths, OutputTestFixture.HudVersion)
            .TryLoadSelection(result.Installed!.SelectionKey));
    }

    [Theory]
    [InlineData("build", false)]
    [InlineData("prewrite", false)]
    [InlineData("postwrite", false)]
    [InlineData("prevalidate", false)]
    [InlineData("postvalidate", false)]
    [InlineData("precollision", false)]
    [InlineData("postcollision", false)]
    [InlineData("postdecision", false)]
    [InlineData("preinstall", false)]
    [InlineData("postinstall", true)]
    [InlineData("prereload", true)]
    [InlineData("postreload", true)]
    [InlineData("preactivation", true)]
    public async Task ApplyAsync_CancellationAtEveryBoundaryHasCorrectCommitSemantics(
        string boundary,
        bool installedCommitted)
    {
        using var root = new TemporaryRoot();
        var sentinel = CreateOtherOperation(root.Paths);
        using var cancellation = new CancellationTokenSource();
        var activations = 0;
        var service = CreateService(
            root.Paths,
            new RecordingDialogs(),
            (_, _) =>
            {
                activations++;
                return Task.FromResult(new HudActivationResult(
                    HudActivationDisposition.ActivatedLive,
                    null,
                    null));
            },
            observe: observed =>
            {
                if (observed == boundary)
                {
                    cancellation.Cancel();
                }
            });

        var result = await service.ApplyAsync(
            OutputTestFixture.CompleteDraft(),
            OutputTestFixture.Assets(),
            cancellation.Token);

        Assert.Equal(
            installedCommitted
                ? DesignerOutputDisposition.InstalledNotActivated
                : DesignerOutputDisposition.Cancelled,
            result.Disposition);
        Assert.Equal(0, activations);
        Assert.Equal(
            installedCommitted ? 1 : 0,
            new InstalledSkinCatalog(root.Paths, OutputTestFixture.HudVersion)
                .LoadAll().Installed.Count);
        AssertOtherOperationOnly(root.Paths, sentinel);
    }

    private static SkinApplyService CreateService(
        SkinStoragePaths paths,
        ISkinOutputDialogs dialogs,
        Func<string, CancellationToken, Task<HudActivationResult>> activate,
        Action<string>? observe = null,
        Func<
            Stream,
            SkinPackageBuildRequest,
            CancellationToken,
            SkinManifest>? write = null,
        Func<
            Stream,
            long,
            SemanticVersion,
            CancellationToken,
            SkinValidationResult<SkinPackageDocument>>? validate = null,
        Func<
            SkinPackageDocument,
            SemanticVersion,
            CancellationToken,
            SkinValidationResult<SkinInstallPreview>>? inspect = null,
        Func<
            SkinInstallPreview,
            SkinCollisionDecision,
            CancellationToken,
            SkinInstallResult>? install = null,
        Func<string, InstalledSkinRecord?>? reload = null,
        IApplyStagingLeaseProvider? staging = null)
    {
        var writer = new SkinPackageWriter();
        var reader = new SkinPackageReader();
        var installer = new SkinPackageInstaller(paths, OutputTestFixture.HudVersion);
        var catalog = new InstalledSkinCatalog(paths, OutputTestFixture.HudVersion);
        return new SkinApplyService(
            paths,
            OutputTestFixture.HudVersion,
            new DraftPackageBuilder(OutputTestFixture.HudVersion),
            staging ?? PhysicalApplyStagingLeaseProvider.Instance,
            write ?? writer.Write,
            validate ?? reader.ValidateStream,
            inspect ?? installer.Inspect,
            install ?? ((preview, decision, token) =>
            {
                var result = installer.Install(preview, decision, token);
                if (dialogs is RecordingDialogs recording)
                {
                    recording.LastInstallDisposition = result.Disposition;
                }

                return result;
            }),
            reload ?? catalog.TryLoadSelection,
            activate,
            dialogs,
            observe);
    }

    private static InstalledSkinRecord InstallInitial(
        SkinStoragePaths paths,
        string version)
    {
        var builder = new DraftPackageBuilder(OutputTestFixture.HudVersion);
        var request = builder.Build(
            OutputTestFixture.CompleteDraft(packageVersion: version),
            OutputTestFixture.Assets());
        Assert.True(request.IsValid);
        var package = Path.Combine(
            Path.GetDirectoryName(paths.SettingsRoot)!,
            $"seed-{Guid.NewGuid():N}.cqskin");
        var written = new SkinPackageWriter().WriteFile(
            package,
            request.Value!,
            overwrite: false,
            CancellationToken.None);
        Assert.True(written.IsValid);
        var installer = new SkinPackageInstaller(paths, OutputTestFixture.HudVersion);
        var preview = installer.Inspect(
            package,
            OutputTestFixture.HudVersion,
            CancellationToken.None);
        Assert.True(preview.IsValid);
        var installed = installer.Install(
            preview.Value!,
            SkinCollisionDecision.Replace,
            CancellationToken.None);
        Assert.Empty(installed.Errors);
        File.Delete(package);
        return Assert.IsType<InstalledSkinRecord>(installed.Installed);
    }

    private static void AssertNoApplyOperations(SkinStoragePaths paths) =>
        Assert.True(
            !Directory.Exists(paths.ImportsRoot) ||
            !Directory.EnumerateDirectories(paths.ImportsRoot).Any());

    private static string CreateOtherOperation(SkinStoragePaths paths)
    {
        var operation = Path.Combine(
            paths.ImportsRoot,
            "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        Directory.CreateDirectory(operation);
        File.WriteAllBytes(Path.Combine(operation, "sentinel.bin"), "other"u8.ToArray());
        return operation;
    }

    private static void AssertOtherOperationOnly(
        SkinStoragePaths paths,
        string sentinel)
    {
        Assert.Equal(
            [Path.GetFullPath(sentinel)],
            Directory.EnumerateDirectories(paths.ImportsRoot)
                .Select(Path.GetFullPath)
                .ToArray());
        Assert.Equal(
            "other"u8.ToArray(),
            File.ReadAllBytes(Path.Combine(sentinel, "sentinel.bin")));
    }

    private static IReadOnlyDictionary<string, byte[]> SnapshotDirectory(
        string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                File.ReadAllBytes,
                StringComparer.Ordinal);

    private sealed class RecordingDialogs : ISkinOutputDialogs
    {
        public SkinCollisionDecision CollisionDecision { get; init; } =
            SkinCollisionDecision.Replace;

        public int CollisionChoiceCount { get; private set; }

        public SkinInstallDisposition? LastInstallDisposition { get; set; }

        public string? ChooseExportPath(string suggestedFileName) => null;

        public bool ConfirmExportReplace(string destinationPath) => false;

        public SkinCollisionDecision ChooseApplyCollision(SkinInstallPreview preview)
        {
            CollisionChoiceCount++;
            return CollisionDecision;
        }

        public void ShowResult(DesignerOutputResult result)
        {
        }
    }

    private sealed class CleanupFailingStagingProvider(
        IApplyStagingLeaseProvider inner) : IApplyStagingLeaseProvider
    {
        public string OperationPath { get; private set; } = string.Empty;

        public IApplyStagingLease Create(SkinStoragePaths paths)
        {
            var lease = inner.Create(paths);
            OperationPath = lease.OperationPath;
            return new CleanupFailingStagingLease(lease);
        }

        private sealed class CleanupFailingStagingLease(IApplyStagingLease inner) :
            IApplyStagingLease
        {
            public string OperationPath => inner.OperationPath;

            public string PackagePath => inner.PackagePath;

            public Stream PackageStream => inner.PackageStream;

            public void FlushPackageToDisk() => inner.FlushPackageToDisk();

            public void DeleteOwnedOperation() =>
                throw new IOException("Injected cleanup failure.");

            public void Dispose() => inner.Dispose();
        }
    }

    private sealed class TemporaryRoot : IDisposable
    {
        public TemporaryRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CodexQuotaHud-Task15-apply-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            Paths = new SkinStoragePaths(Path);
        }

        public string Path { get; }

        public SkinStoragePaths Paths { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

using System.Text;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Tests.Drafts;

public sealed class DraftStoreTests
{
    private static readonly Guid DraftId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SkinId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset CreatedAt =
        DateTimeOffset.Parse("2026-08-02T00:00:00Z");

    [Fact]
    public void DraftProjectPaths_UsesOnlyALowercaseDirectGuidChild()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new DraftProjectPaths(temporary.Path, DraftId);
        var expectedRoot = System.IO.Path.Combine(
            System.IO.Path.GetFullPath(temporary.Path),
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        Assert.Equal(expectedRoot, paths.ProjectRoot);
        Assert.Equal(System.IO.Path.Combine(expectedRoot, "draft.json"), paths.NamedDraftPath);
        Assert.Equal(System.IO.Path.Combine(expectedRoot, "recovery.json"), paths.RecoveryPath);
        Assert.Equal(System.IO.Path.Combine(expectedRoot, "assets"), paths.AssetsRoot);
        Assert.ThrowsAny<ArgumentException>(() => new DraftProjectPaths("", DraftId));
        Assert.ThrowsAny<ArgumentException>(() => new DraftProjectPaths(
            System.IO.Path.GetPathRoot(temporary.Path)!,
            DraftId));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DraftProjectPaths(temporary.Path, Guid.Empty));
    }

    [Fact]
    public async Task SaveNamedAsync_UsesProjectNameOnlyAsValidatedJsonData()
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var operationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var store = new DraftStore(storage, operationId: () => operationId);
        var projectName = string.Concat(Enumerable.Repeat("😀", 72)) + "<>:/\\|?*";
        var draft = ValidDraft(DraftId, revision: 4, updatedAt: CreatedAt.AddMinutes(4)) with
        {
            ProjectName = projectName
        };

        await store.SaveNamedAsync(draft);

        var paths = new DraftProjectPaths(storage.DraftsRoot, DraftId);
        Assert.True(File.Exists(paths.NamedDraftPath));
        Assert.True(Directory.Exists(paths.AssetsRoot));
        Assert.Equal(
            ["assets", "draft.json"],
            Directory.EnumerateFileSystemEntries(paths.ProjectRoot)
                .Select(System.IO.Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal));
        var parsed = DraftJsonCodec.Parse(File.ReadAllBytes(paths.NamedDraftPath));
        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        Assert.Equal(projectName, parsed.Value!.ProjectName);
        Assert.DoesNotContain(projectName, paths.ProjectRoot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveNamedAsync_RejectsInvalidProjectNameBeforeAnyWrite()
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var files = new RecordingFileOperations();
        var store = new DraftStore(storage, files);
        var draft = ValidDraft(DraftId, revision: 1, updatedAt: CreatedAt.AddMinutes(1)) with
        {
            ProjectName = string.Concat(Enumerable.Repeat("😀", 81))
        };

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveNamedAsync(draft));

        Assert.Equal(0, files.MutationCount);
    }

    [Fact]
    public async Task SaveNamedAsync_RejectsReparseAncestorBeforeAnyWrite()
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var files = new RecordingFileOperations(storage.DraftsRoot);
        var store = new DraftStore(storage, files);

        var exception = await Assert.ThrowsAnyAsync<IOException>(
            () => store.SaveNamedAsync(
                ValidDraft(DraftId, revision: 1, updatedAt: CreatedAt.AddMinutes(1))));

        Assert.Contains("draft.path-unsafe", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, files.MutationCount);
    }

    [Fact]
    public async Task LoadAll_SortsHealthyDraftsAndPreservesCorruptDirectChild()
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var store = new DraftStore(storage);
        var firstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var tiedId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var corruptId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        await store.SaveNamedAsync(ValidDraft(firstId, 1, CreatedAt.AddHours(1)));
        await store.SaveNamedAsync(ValidDraft(secondId, 2, CreatedAt.AddHours(2)));
        await store.SaveNamedAsync(ValidDraft(tiedId, 3, CreatedAt.AddHours(2)));
        var corruptPaths = new DraftProjectPaths(storage.DraftsRoot, corruptId);
        Directory.CreateDirectory(corruptPaths.ProjectRoot);
        var corruptBytes = Encoding.UTF8.GetBytes("{ private-json");
        await File.WriteAllBytesAsync(corruptPaths.NamedDraftPath, corruptBytes);
        var ignoredNested = System.IO.Path.Combine(
            storage.DraftsRoot,
            "not-a-guid",
            "55555555-5555-5555-5555-555555555555");
        Directory.CreateDirectory(ignoredNested);
        await File.WriteAllBytesAsync(
            System.IO.Path.Combine(ignoredNested, "draft.json"),
            DraftJsonCodec.Write(ValidDraft(
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                9,
                CreatedAt.AddDays(1))));

        var before = File.ReadAllBytes(corruptPaths.NamedDraftPath);
        var snapshot = store.LoadAll();

        Assert.Equal([secondId, tiedId, firstId], snapshot.Healthy.Select(draft => draft.DraftId));
        var failure = Assert.Single(snapshot.Corrupt);
        Assert.Equal(corruptId, failure.DraftId);
        Assert.Equal("draft.json", failure.LeafName);
        Assert.Equal("draft.corrupt", failure.ErrorCode);
        Assert.DoesNotContain(storage.DraftsRoot, failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-json", failure.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(corruptPaths.NamedDraftPath));
    }

    [Theory]
    [InlineData(AtomicFault.CreateDirectory)]
    [InlineData(AtomicFault.WriteAndFlush)]
    [InlineData(AtomicFault.ReadBackCorrupt)]
    [InlineData(AtomicFault.Replace)]
    public async Task SaveNamedAsync_PreReplaceFaultPreservesExactOldTarget(
        AtomicFault fault)
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var project = new DraftProjectPaths(storage.DraftsRoot, DraftId);
        var oldBytes = Encoding.UTF8.GetBytes("old named bytes");
        var operation = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var files = new AtomicFileOperations(project, oldBytes) { Fault = fault };
        var store = new DraftStore(storage, files, () => operation);

        await Assert.ThrowsAnyAsync<Exception>(() => store.SaveNamedAsync(
            ValidDraft(DraftId, revision: 8, updatedAt: CreatedAt.AddMinutes(8))));

        Assert.Equal(oldBytes, files.GetBytes(project.NamedDraftPath));
        Assert.False(files.Contains(TemporaryPath(project.NamedDraftPath, operation)));
        Assert.Equal(fault == AtomicFault.Replace ? 1 : 0, files.ReplaceAttempts);
    }

    [Fact]
    public async Task SaveRecoveryAsync_WritesFlushesValidatesAndReplacesOnceInSameDirectory()
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var project = new DraftProjectPaths(storage.DraftsRoot, DraftId);
        var operation = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var files = new AtomicFileOperations(project, Encoding.UTF8.GetBytes("old"));
        var store = new DraftStore(storage, files, () => operation);
        var draft = ValidDraft(DraftId, revision: 9, updatedAt: CreatedAt.AddMinutes(9));

        await store.SaveRecoveryAsync(draft);

        var temporaryPath = TemporaryPath(project.RecoveryPath, operation);
        Assert.Equal(temporaryPath, files.WrittenPath);
        Assert.True(files.WriteWasFlushed);
        Assert.Equal(1, files.ReplaceAttempts);
        Assert.Equal(temporaryPath, files.ReplaceSource);
        Assert.Equal(project.RecoveryPath, files.ReplaceDestination);
        Assert.Equal(
            System.IO.Path.GetDirectoryName(files.ReplaceSource),
            System.IO.Path.GetDirectoryName(files.ReplaceDestination));
        Assert.False(files.Contains(temporaryPath));
        Assert.Equal(DraftJsonCodec.Write(draft), files.GetBytes(project.RecoveryPath));
    }

    [Fact]
    public async Task SaveNamedAsync_CancellationBeforeReplacePreservesOldTarget()
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var project = new DraftProjectPaths(storage.DraftsRoot, DraftId);
        var oldBytes = Encoding.UTF8.GetBytes("old named bytes");
        using var cancellation = new CancellationTokenSource();
        var files = new AtomicFileOperations(project, oldBytes)
        {
            Fault = AtomicFault.CancelAfterRead,
            Cancellation = cancellation
        };
        var store = new DraftStore(storage, files);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SaveNamedAsync(
                ValidDraft(DraftId, 10, CreatedAt.AddMinutes(10)),
                cancellation.Token));

        Assert.Equal(oldBytes, files.GetBytes(project.NamedDraftPath));
        Assert.Equal(0, files.ReplaceAttempts);
        Assert.DoesNotContain(files.Paths, path => path.Contains(".tmp-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveNamedAsync_CancellationAfterReplaceReportsSuccess()
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var project = new DraftProjectPaths(storage.DraftsRoot, DraftId);
        using var cancellation = new CancellationTokenSource();
        var files = new AtomicFileOperations(project, Encoding.UTF8.GetBytes("old"))
        {
            Fault = AtomicFault.CancelAfterReplace,
            Cancellation = cancellation
        };
        var store = new DraftStore(storage, files);
        var draft = ValidDraft(DraftId, 11, CreatedAt.AddMinutes(11));

        await store.SaveNamedAsync(draft, cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(DraftJsonCodec.Write(draft), files.GetBytes(project.NamedDraftPath));
        Assert.Equal(1, files.ReplaceAttempts);
    }

    [Fact]
    public async Task SaveNamedAsync_CleanupFailureReportsStableCodeAndTouchesNoOtherOperation()
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var project = new DraftProjectPaths(storage.DraftsRoot, DraftId);
        var oldBytes = Encoding.UTF8.GetBytes("old named bytes");
        var operation = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var otherOperation = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var otherTemporary = TemporaryPath(project.NamedDraftPath, otherOperation);
        var otherBytes = Encoding.UTF8.GetBytes("other operation");
        var files = new AtomicFileOperations(project, oldBytes)
        {
            Fault = AtomicFault.Cleanup,
            CleanupFailurePath = TemporaryPath(project.NamedDraftPath, operation)
        };
        files.SeedFile(otherTemporary, otherBytes);
        var store = new DraftStore(storage, files, () => operation);

        var exception = await Assert.ThrowsAnyAsync<IOException>(() =>
            store.SaveNamedAsync(
                ValidDraft(DraftId, 12, CreatedAt.AddMinutes(12))));

        Assert.Contains("draft.cleanup-failed", exception.Message, StringComparison.Ordinal);
        Assert.Equal(oldBytes, files.GetBytes(project.NamedDraftPath));
        Assert.Equal(otherBytes, files.GetBytes(otherTemporary));
        Assert.Equal([TemporaryPath(project.NamedDraftPath, operation)], files.DeleteAttempts);
    }

    [Fact]
    public async Task SaveNamedAsync_RechecksForReparseEscapeImmediatelyBeforeWriting()
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var project = new DraftProjectPaths(storage.DraftsRoot, DraftId);
        var oldBytes = Encoding.UTF8.GetBytes("old named bytes");
        var files = new AtomicFileOperations(project, oldBytes)
        {
            Fault = AtomicFault.ReparseAfterCreate
        };
        var store = new DraftStore(storage, files);

        var exception = await Assert.ThrowsAnyAsync<IOException>(() =>
            store.SaveNamedAsync(
                ValidDraft(DraftId, 13, CreatedAt.AddMinutes(13))));

        Assert.Contains("draft.path-unsafe", exception.Message, StringComparison.Ordinal);
        Assert.Null(files.WrittenPath);
        Assert.Equal(oldBytes, files.GetBytes(project.NamedDraftPath));
    }

    [Theory]
    [InlineData(LeaseTransition.AfterWrite)]
    [InlineData(LeaseTransition.AfterRead)]
    [InlineData(LeaseTransition.BeforeReplace)]
    public async Task SaveNamedAsync_ParentPathReplacementCannotRedirectLeasedTransaction(
        LeaseTransition transition)
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var operation = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var files = new LeaseAwareFileOperations(
            DraftId,
            DraftJsonCodec.Write(ValidDraft(DraftId, 3, CreatedAt.AddMinutes(3))),
            transition,
            operation);
        var externalBefore = files.ExternalSnapshot();
        var next = ValidDraft(DraftId, 14, CreatedAt.AddMinutes(14));

        await new DraftStore(storage, files, () => operation).SaveNamedAsync(next);

        Assert.True(files.ParentPathWasReplaced);
        Assert.Equal(externalBefore, files.ExternalSnapshot());
        Assert.Equal(DraftJsonCodec.Write(next), files.OriginalNamedBytes);
        Assert.False(files.OriginalContainsTemporary);
    }

    [Fact]
    public async Task SaveNamedAsync_ParentReplacementBeforeCleanupCannotDeleteExternalTree()
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var operation = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var oldBytes = DraftJsonCodec.Write(ValidDraft(DraftId, 3, CreatedAt.AddMinutes(3)));
        var files = new LeaseAwareFileOperations(
            DraftId,
            oldBytes,
            LeaseTransition.BeforeCleanup,
            operation);
        var externalBefore = files.ExternalSnapshot();

        await Assert.ThrowsAnyAsync<IOException>(() =>
            new DraftStore(storage, files, () => operation).SaveNamedAsync(
                ValidDraft(DraftId, 15, CreatedAt.AddMinutes(15))));

        Assert.True(files.ParentPathWasReplaced);
        Assert.Equal(externalBefore, files.ExternalSnapshot());
        Assert.Equal(oldBytes, files.OriginalNamedBytes);
        Assert.False(files.OriginalContainsTemporary);
    }

    [Fact]
    public void LoadForOpen_ParentReplacementAfterLeaseCannotRedirectRead()
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var original = ValidDraft(DraftId, 5, CreatedAt.AddMinutes(5));
        var files = new LeaseAwareFileOperations(
            DraftId,
            DraftJsonCodec.Write(original),
            LeaseTransition.BeforeRead,
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        var externalBefore = files.ExternalSnapshot();

        var result = new DraftStore(storage, files).LoadForOpen(DraftId);

        Assert.Equal(5, result.Document?.Revision);
        Assert.True(files.ParentPathWasReplaced);
        Assert.Equal(externalBefore, files.ExternalSnapshot());
        Assert.Equal(0, files.PathBasedOperationCount);
    }

    [Fact]
    public void LoadAll_ParentReplacementDuringEnumerationStaysOnLeasedCatalog()
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var files = new LeaseAwareFileOperations(
            DraftId,
            DraftJsonCodec.Write(ValidDraft(DraftId, 5, CreatedAt.AddMinutes(5))),
            LeaseTransition.DuringEnumeration,
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        var externalBefore = files.ExternalSnapshot();

        var snapshot = new DraftStore(storage, files).LoadAll();

        Assert.Equal([DraftId], snapshot.Healthy.Select(draft => draft.DraftId));
        Assert.Empty(snapshot.Corrupt);
        Assert.True(files.ParentPathWasReplaced);
        Assert.Equal(externalBefore, files.ExternalSnapshot());
        Assert.Equal(0, files.PathBasedOperationCount);
    }

    [Fact]
    public void PhysicalLease_HoldsProjectIdentityAgainstRenameUntilDisposed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var projectPaths = new DraftProjectPaths(storage.DraftsRoot, DraftId);
        var provider = Assert.IsAssignableFrom<IDraftStorageLeaseProvider>(
            PhysicalDraftFileOperations.Instance);
        using (var catalog = Assert.IsAssignableFrom<IDraftCatalogLease>(
                   provider.OpenCatalog(storage.DraftsRoot, create: true)))
        using (var project = Assert.IsAssignableFrom<IDraftProjectLease>(
                   catalog.OpenProject(DraftId, create: true)))
        {
            project.EnsureAssetsDirectory();
            Assert.ThrowsAny<IOException>(() => Directory.Move(
                projectPaths.ProjectRoot,
                projectPaths.ProjectRoot + "-moved"));
        }

        Directory.Move(projectPaths.ProjectRoot, projectPaths.ProjectRoot + "-moved");
        Directory.Move(projectPaths.ProjectRoot + "-moved", projectPaths.ProjectRoot);
    }

    [Fact]
    public void PhysicalLease_AllowsTwoCatalogLeasesWhileBlockingCatalogRename()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var provider = Assert.IsAssignableFrom<IDraftStorageLeaseProvider>(
            PhysicalDraftFileOperations.Instance);
        using var first = Assert.IsAssignableFrom<IDraftCatalogLease>(
            provider.OpenCatalog(storage.DraftsRoot, create: true));
        using var second = Assert.IsAssignableFrom<IDraftCatalogLease>(
            provider.OpenCatalog(storage.DraftsRoot, create: true));

        Assert.ThrowsAny<IOException>(() => Directory.Move(
            storage.DraftsRoot,
            storage.DraftsRoot + "-moved"));
    }

    [Fact]
    public void PhysicalLease_AllowsTwoProjectLeasesWhileBlockingProjectRename()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var projectPaths = new DraftProjectPaths(storage.DraftsRoot, DraftId);
        var provider = Assert.IsAssignableFrom<IDraftStorageLeaseProvider>(
            PhysicalDraftFileOperations.Instance);
        using var catalog = Assert.IsAssignableFrom<IDraftCatalogLease>(
            provider.OpenCatalog(storage.DraftsRoot, create: true));
        using var first = Assert.IsAssignableFrom<IDraftProjectLease>(
            catalog.OpenProject(DraftId, create: true));
        using var second = Assert.IsAssignableFrom<IDraftProjectLease>(
            catalog.OpenProject(DraftId, create: true));
        first.EnsureAssetsDirectory();
        second.EnsureAssetsDirectory();

        Assert.ThrowsAny<IOException>(() => Directory.Move(
            projectPaths.ProjectRoot,
            projectPaths.ProjectRoot + "-moved"));
    }

    [Fact]
    public async Task PhysicalLease_AllowsTwoReadHandlesForOneDraftDocument()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var draft = ValidDraft(DraftId, 4, CreatedAt.AddMinutes(4));
        await new DraftStore(storage).SaveNamedAsync(draft);
        var projectPaths = new DraftProjectPaths(storage.DraftsRoot, DraftId);
        using var project = WindowsDraftDirectoryLease.Open(projectPaths.ProjectRoot);
        using var first = Assert.IsType<WindowsDraftFileLease>(
            project.OpenChildFile("draft.json", create: false));
        using var second = Assert.IsType<WindowsDraftFileLease>(
            project.OpenChildFile("draft.json", create: false));

        Assert.Equal(DraftJsonCodec.Write(draft), first.ReadAllBytes());
        Assert.Equal(DraftJsonCodec.Write(draft), second.ReadAllBytes());
    }

    [Fact]
    public void PhysicalLease_CleansAnExistingOperationTempWithDeleteAccess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var projectPaths = new DraftProjectPaths(storage.DraftsRoot, DraftId);
        var provider = Assert.IsAssignableFrom<IDraftStorageLeaseProvider>(
            PhysicalDraftFileOperations.Instance);
        using var catalog = Assert.IsAssignableFrom<IDraftCatalogLease>(
            provider.OpenCatalog(storage.DraftsRoot, create: true));
        using var project = Assert.IsAssignableFrom<IDraftProjectLease>(
            catalog.OpenProject(DraftId, create: true));
        var leaf = ".draft.json.tmp-cccccccc-cccc-cccc-cccc-cccccccccccc";
        var path = System.IO.Path.Combine(projectPaths.ProjectRoot, leaf);
        File.WriteAllBytes(path, [1, 2, 3]);

        project.DeleteFile(leaf);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task LoadForOpen_DuringActiveRecoverySaveReadsLastCommittedDocument()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var initial = ValidDraft(DraftId, 1, CreatedAt.AddMinutes(1));
        await new DraftStore(storage).SaveNamedAsync(initial);
        var files = new FirstPhysicalWriteBarrier();
        var store = new DraftStore(storage, files);
        var recovery = store.SaveRecoveryAsync(
            ValidDraft(DraftId, 2, CreatedAt.AddMinutes(2)));
        await files.WaitForFirstWriteAsync();

        DraftOpenResult during;
        try
        {
            during = store.LoadForOpen(DraftId);
        }
        finally
        {
            files.ReleaseFirstWrite();
            await recovery;
        }

        Assert.NotNull(during.Document);
        Assert.Equal(1, during.Document.Revision);
        Assert.False(during.WasRecovered);
        Assert.Empty(during.Failures);

        var after = store.LoadForOpen(DraftId);
        Assert.NotNull(after.Document);
        Assert.Equal(2, after.Document.Revision);
        Assert.True(after.WasRecovered);
        Assert.Empty(after.Failures);
    }

    [Fact]
    public async Task SaveNamedAsync_DuringActiveRecoverySaveCommitsWithoutCorruption()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        await new DraftStore(storage).SaveNamedAsync(
            ValidDraft(DraftId, 1, CreatedAt.AddMinutes(1)));
        var files = new FirstPhysicalWriteBarrier();
        var store = new DraftStore(storage, files);
        var recovery = store.SaveRecoveryAsync(
            ValidDraft(DraftId, 2, CreatedAt.AddMinutes(2)));
        await files.WaitForFirstWriteAsync();

        try
        {
            await store.SaveNamedAsync(
                ValidDraft(DraftId, 3, CreatedAt.AddMinutes(3)));
        }
        finally
        {
            files.ReleaseFirstWrite();
            await recovery;
        }

        var result = store.LoadForOpen(DraftId);
        Assert.NotNull(result.Document);
        Assert.Equal(3, result.Document.Revision);
        Assert.False(result.WasRecovered);
        Assert.Empty(result.Failures);
    }

    private static SkinDraftDocument ValidDraft(
        Guid draftId,
        long revision,
        DateTimeOffset updatedAt)
    {
        var skinId = draftId == SkinId
            ? Guid.Parse("99999999-9999-9999-9999-999999999999")
            : SkinId;
        return SkinDraftFactory.CreateNew(
            draftId,
            skinId,
            CreatedAt,
            SemanticVersion.Parse("1.1.1")) with
        {
            Revision = revision,
            ProjectName = $"Project {draftId:D}",
            UpdatedAtUtc = updatedAt
        };
    }

    private static string TemporaryPath(string targetPath, Guid operationId) =>
        System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(targetPath)!,
            $".{System.IO.Path.GetFileName(targetPath)}.tmp-{operationId:D}".ToLowerInvariant());

    public enum AtomicFault
    {
        None,
        CreateDirectory,
        WriteAndFlush,
        ReadBackCorrupt,
        Replace,
        CancelAfterRead,
        CancelAfterReplace,
        Cleanup,
        ReparseAfterCreate
    }

    public enum LeaseTransition
    {
        AfterWrite,
        AfterRead,
        BeforeReplace,
        BeforeCleanup,
        BeforeRead,
        DuringEnumeration
    }

    private sealed class LeaseAwareFileOperations :
        IDraftFileOperations,
        IDraftStorageLeaseProvider
    {
        private readonly Guid _draftId;
        private readonly LeaseTransition _transition;
        private readonly Guid _operationId;
        private readonly ProjectTree _original;
        private readonly ProjectTree _external;

        public LeaseAwareFileOperations(
            Guid draftId,
            byte[] namedBytes,
            LeaseTransition transition,
            Guid operationId)
        {
            _draftId = draftId;
            _transition = transition;
            _operationId = operationId;
            _original = new ProjectTree(namedBytes);
            _external = new ProjectTree(Encoding.UTF8.GetBytes("external named bytes"));
            _external.Files[TemporaryLeaf("draft.json")] =
                Encoding.UTF8.GetBytes("external temp sentinel");
        }

        public bool ParentPathWasReplaced { get; private set; }

        public int PathBasedOperationCount { get; private set; }

        public byte[] OriginalNamedBytes => _original.Files["draft.json"].ToArray();

        public bool OriginalContainsTemporary =>
            _original.Files.ContainsKey(TemporaryLeaf("draft.json"));

        public string ExternalSnapshot() => string.Join(
            "|",
            _external.Files
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}:{Convert.ToHexString(pair.Value)}"));

        public IDraftCatalogLease? OpenCatalog(string draftsRoot, bool create) =>
            new CatalogLease(this, _original);

        public void CreateDirectory(string path) => PathBasedOperationCount++;

        public bool DirectoryExists(string path)
        {
            PathBasedOperationCount++;
            return true;
        }

        public bool FileExists(string path)
        {
            PathBasedOperationCount++;
            return Current.Files.ContainsKey(System.IO.Path.GetFileName(path));
        }

        public FileAttributes GetAttributes(string path)
        {
            PathBasedOperationCount++;
            return FileAttributes.Normal;
        }

        public IEnumerable<string> EnumerateDirectories(string path)
        {
            PathBasedOperationCount++;
            return [];
        }

        public byte[] ReadAllBytes(string path)
        {
            PathBasedOperationCount++;
            return Current.Files[System.IO.Path.GetFileName(path)].ToArray();
        }

        public Task WriteAndFlushAsync(
            string path,
            ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken)
        {
            PathBasedOperationCount++;
            Current.Files[System.IO.Path.GetFileName(path)] = bytes.ToArray();
            return Task.CompletedTask;
        }

        public void ReplaceFile(string sourcePath, string destinationPath)
        {
            PathBasedOperationCount++;
            var source = System.IO.Path.GetFileName(sourcePath);
            var destination = System.IO.Path.GetFileName(destinationPath);
            Current.Files[destination] = Current.Files[source];
            Current.Files.Remove(source);
        }

        public void DeleteFile(string path)
        {
            PathBasedOperationCount++;
            Current.Files.Remove(System.IO.Path.GetFileName(path));
        }

        private ProjectTree Current => ParentPathWasReplaced ? _external : _original;

        private void ReplaceParentPath() => ParentPathWasReplaced = true;

        private string TemporaryLeaf(string targetLeaf) =>
            $".{targetLeaf}.tmp-{_operationId:D}".ToLowerInvariant();

        private sealed class CatalogLease(
            LeaseAwareFileOperations owner,
            ProjectTree tree) : IDraftCatalogLease
        {
            public IReadOnlyList<string> EnumerateProjectNames()
            {
                if (owner._transition == LeaseTransition.DuringEnumeration)
                {
                    owner.ReplaceParentPath();
                }

                return [owner._draftId.ToString("D").ToLowerInvariant()];
            }

            public IDraftProjectLease? OpenProject(Guid draftId, bool create)
            {
                Assert.Equal(owner._draftId, draftId);
                var lease = new ProjectLease(owner, tree);
                if (owner._transition == LeaseTransition.BeforeRead)
                {
                    owner.ReplaceParentPath();
                }

                return lease;
            }

            public void Dispose()
            {
            }
        }

        private sealed class ProjectLease(
            LeaseAwareFileOperations owner,
            ProjectTree tree) : IDraftProjectLease
        {
            public void EnsureAssetsDirectory()
            {
            }

            public bool FileExists(string fixedLeafName) =>
                tree.Files.ContainsKey(fixedLeafName);

            public byte[] ReadAllBytes(string fixedLeafName)
            {
                var result = owner._transition == LeaseTransition.BeforeCleanup &&
                    fixedLeafName.Contains(".tmp-", StringComparison.Ordinal)
                    ? Encoding.UTF8.GetBytes("{ corrupt temp")
                    : tree.Files[fixedLeafName].ToArray();
                if (owner._transition == LeaseTransition.AfterRead &&
                    fixedLeafName.Contains(".tmp-", StringComparison.Ordinal))
                {
                    owner.ReplaceParentPath();
                }

                return result;
            }

            public Task WriteAndFlushAsync(
                string fixedLeafName,
                ReadOnlyMemory<byte> bytes,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                tree.Files[fixedLeafName] = bytes.ToArray();
                if (owner._transition == LeaseTransition.AfterWrite)
                {
                    owner.ReplaceParentPath();
                }

                return Task.CompletedTask;
            }

            public void ReplaceFile(string sourceLeafName, string destinationLeafName)
            {
                if (owner._transition == LeaseTransition.BeforeReplace)
                {
                    owner.ReplaceParentPath();
                }

                tree.Files[destinationLeafName] = tree.Files[sourceLeafName];
                tree.Files.Remove(sourceLeafName);
            }

            public void DeleteFile(string fixedLeafName)
            {
                if (owner._transition == LeaseTransition.BeforeCleanup)
                {
                    owner.ReplaceParentPath();
                }

                tree.Files.Remove(fixedLeafName);
            }

            public void Dispose()
            {
            }
        }

        private sealed class ProjectTree(byte[] namedBytes)
        {
            public Dictionary<string, byte[]> Files { get; } =
                new(StringComparer.Ordinal)
                {
                    ["draft.json"] = namedBytes.ToArray()
                };
        }
    }

    private sealed class AtomicFileOperations : IDraftFileOperations
    {
        private readonly Dictionary<string, byte[]> _files =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _directories =
            new(StringComparer.OrdinalIgnoreCase);

        public AtomicFileOperations(DraftProjectPaths project, byte[] oldNamedBytes)
        {
            SeedDirectory(System.IO.Path.GetDirectoryName(project.ProjectRoot)!);
            SeedDirectory(project.ProjectRoot);
            SeedDirectory(project.AssetsRoot);
            SeedFile(project.NamedDraftPath, oldNamedBytes);
        }

        public AtomicFault Fault { get; init; }

        public CancellationTokenSource? Cancellation { get; init; }

        public string? CleanupFailurePath { get; init; }

        public string? WrittenPath { get; private set; }

        public bool WriteWasFlushed { get; private set; }

        public int ReplaceAttempts { get; private set; }

        public string? ReplaceSource { get; private set; }

        public string? ReplaceDestination { get; private set; }

        public List<string> DeleteAttempts { get; } = [];

        public IEnumerable<string> Paths => _files.Keys;

        public void SeedFile(string path, byte[] bytes) =>
            _files[Normalize(path)] = bytes.ToArray();

        public void CreateDirectory(string path)
        {
            if (Fault == AtomicFault.CreateDirectory)
            {
                throw new IOException("create failed");
            }

            SeedDirectory(path);
            if (Fault == AtomicFault.ReparseAfterCreate)
            {
                HasReparsePoint = true;
            }
        }

        public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));

        public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

        public FileAttributes GetAttributes(string path) =>
            HasReparsePoint
                ? FileAttributes.Directory | FileAttributes.ReparsePoint
                : FileAttributes.Directory;

        public IEnumerable<string> EnumerateDirectories(string path) =>
            _directories.Where(directory => string.Equals(
                System.IO.Path.GetDirectoryName(directory),
                Normalize(path),
                StringComparison.OrdinalIgnoreCase));

        public byte[] ReadAllBytes(string path)
        {
            var normalized = Normalize(path);
            if (Fault == AtomicFault.ReadBackCorrupt &&
                normalized.Contains(".tmp-", StringComparison.Ordinal))
            {
                return Encoding.UTF8.GetBytes("{ invalid");
            }

            var bytes = _files[normalized].ToArray();
            if (Fault == AtomicFault.CancelAfterRead &&
                normalized.Contains(".tmp-", StringComparison.Ordinal))
            {
                Cancellation!.Cancel();
            }

            return bytes;
        }

        public Task WriteAndFlushAsync(
            string path,
            ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WrittenPath = Normalize(path);
            _files[WrittenPath] = bytes.ToArray();
            WriteWasFlushed = true;
            if (Fault is AtomicFault.WriteAndFlush or AtomicFault.Cleanup)
            {
                throw new IOException("write failed after creating temp");
            }

            return Task.CompletedTask;
        }

        public void ReplaceFile(string sourcePath, string destinationPath)
        {
            ReplaceAttempts++;
            ReplaceSource = Normalize(sourcePath);
            ReplaceDestination = Normalize(destinationPath);
            if (Fault == AtomicFault.Replace)
            {
                throw new IOException("replace failed");
            }

            _files[ReplaceDestination] = _files[ReplaceSource];
            _files.Remove(ReplaceSource);
            if (Fault == AtomicFault.CancelAfterReplace)
            {
                Cancellation!.Cancel();
            }
        }

        public void DeleteFile(string path)
        {
            var normalized = Normalize(path);
            DeleteAttempts.Add(normalized);
            if (Fault == AtomicFault.Cleanup &&
                string.Equals(
                    normalized,
                    Normalize(CleanupFailurePath!),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("cleanup failed");
            }

            _files.Remove(normalized);
        }

        public bool Contains(string path) => _files.ContainsKey(Normalize(path));

        public byte[] GetBytes(string path) => _files[Normalize(path)].ToArray();

        private void SeedDirectory(string path) => _directories.Add(Normalize(path));

        private bool HasReparsePoint { get; set; }

        private static string Normalize(string path) =>
            System.IO.Path.TrimEndingDirectorySeparator(
                System.IO.Path.GetFullPath(path));
    }

    private sealed class RecordingFileOperations : IDraftFileOperations
    {
        private readonly string? _reparsePath;

        public RecordingFileOperations(string? reparsePath = null) =>
            _reparsePath = reparsePath is null
                ? null
                : System.IO.Path.GetFullPath(reparsePath);

        public int MutationCount { get; private set; }

        public void CreateDirectory(string path) => MutationCount++;

        public bool DirectoryExists(string path) =>
            _reparsePath is not null &&
            string.Equals(
                System.IO.Path.GetFullPath(path),
                _reparsePath,
                StringComparison.OrdinalIgnoreCase);

        public bool FileExists(string path) => false;

        public FileAttributes GetAttributes(string path) => FileAttributes.ReparsePoint;

        public IEnumerable<string> EnumerateDirectories(string path) => [];

        public byte[] ReadAllBytes(string path) => throw new FileNotFoundException();

        public Task WriteAndFlushAsync(
            string path,
            ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken)
        {
            MutationCount++;
            return Task.CompletedTask;
        }

        public void ReplaceFile(string sourcePath, string destinationPath) => MutationCount++;

        public void DeleteFile(string path) => MutationCount++;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CodexQuotaHud-Task12-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class FirstPhysicalWriteBarrier :
        IDraftFileOperations,
        IDraftStorageLeaseProvider
    {
        private readonly TaskCompletionSource _firstWrite = NewSignal();
        private readonly TaskCompletionSource _release = NewSignal();
        private int _writeClaimed;

        public void CreateDirectory(string path) =>
            PhysicalDraftFileOperations.Instance.CreateDirectory(path);

        public bool DirectoryExists(string path) =>
            PhysicalDraftFileOperations.Instance.DirectoryExists(path);

        public bool FileExists(string path) =>
            PhysicalDraftFileOperations.Instance.FileExists(path);

        public FileAttributes GetAttributes(string path) =>
            PhysicalDraftFileOperations.Instance.GetAttributes(path);

        public IEnumerable<string> EnumerateDirectories(string path) =>
            PhysicalDraftFileOperations.Instance.EnumerateDirectories(path);

        public byte[] ReadAllBytes(string path) =>
            PhysicalDraftFileOperations.Instance.ReadAllBytes(path);

        public Task WriteAndFlushAsync(
            string path,
            ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken) =>
            PhysicalDraftFileOperations.Instance.WriteAndFlushAsync(
                path,
                bytes,
                cancellationToken);

        public void ReplaceFile(string sourcePath, string destinationPath) =>
            PhysicalDraftFileOperations.Instance.ReplaceFile(sourcePath, destinationPath);

        public void DeleteFile(string path) =>
            PhysicalDraftFileOperations.Instance.DeleteFile(path);

        public IDraftCatalogLease? OpenCatalog(string draftsRoot, bool create)
        {
            var provider = (IDraftStorageLeaseProvider)PhysicalDraftFileOperations.Instance;
            var inner = provider.OpenCatalog(draftsRoot, create);
            return inner is null ? null : new CatalogLease(this, inner);
        }

        public Task WaitForFirstWriteAsync() => _firstWrite.Task;

        public void ReleaseFirstWrite() => _release.TrySetResult();

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed class CatalogLease(
            FirstPhysicalWriteBarrier owner,
            IDraftCatalogLease inner) : IDraftCatalogLease
        {
            public IReadOnlyList<string> EnumerateProjectNames() =>
                inner.EnumerateProjectNames();

            public IDraftProjectLease? OpenProject(Guid draftId, bool create)
            {
                var project = inner.OpenProject(draftId, create);
                return project is null ? null : new ProjectLease(owner, project);
            }

            public void Dispose() => inner.Dispose();
        }

        private sealed class ProjectLease(
            FirstPhysicalWriteBarrier owner,
            IDraftProjectLease inner) : IDraftProjectLease
        {
            public void EnsureAssetsDirectory() => inner.EnsureAssetsDirectory();

            public bool FileExists(string fixedLeafName) =>
                inner.FileExists(fixedLeafName);

            public byte[] ReadAllBytes(string fixedLeafName) =>
                inner.ReadAllBytes(fixedLeafName);

            public async Task WriteAndFlushAsync(
                string fixedLeafName,
                ReadOnlyMemory<byte> bytes,
                CancellationToken cancellationToken)
            {
                await inner.WriteAndFlushAsync(
                    fixedLeafName,
                    bytes,
                    cancellationToken).ConfigureAwait(false);
                if (Interlocked.CompareExchange(ref owner._writeClaimed, 1, 0) == 0)
                {
                    owner._firstWrite.TrySetResult();
                    await owner._release.Task.WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            public void ReplaceFile(string sourceLeafName, string destinationLeafName) =>
                inner.ReplaceFile(sourceLeafName, destinationLeafName);

            public void DeleteFile(string fixedLeafName) =>
                inner.DeleteFile(fixedLeafName);

            public void Dispose() => inner.Dispose();
        }
    }
}

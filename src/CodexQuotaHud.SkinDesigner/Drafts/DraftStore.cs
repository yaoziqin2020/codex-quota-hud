using System.IO;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Drafts;

public sealed class DraftStore
{
    private const string NamedDraftLeaf = "draft.json";
    private const string RecoveryLeaf = "recovery.json";

    private readonly string _draftsRoot;
    private readonly IDraftFileOperations _files;
    private readonly IDraftStorageLeaseProvider? _leaseProvider;
    private readonly Func<Guid> _operationId;

    public DraftStore(
        SkinStoragePaths paths,
        IDraftFileOperations? files = null,
        Func<Guid>? operationId = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _draftsRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(paths.DraftsRoot));
        _files = files ?? PhysicalDraftFileOperations.Instance;
        _leaseProvider = _files as IDraftStorageLeaseProvider;
        _operationId = operationId ?? Guid.NewGuid;
    }

    public Task SaveNamedAsync(
        SkinDraftDocument draft,
        CancellationToken cancellationToken = default) =>
        SaveAsync(draft, NamedDraftLeaf, cancellationToken);

    public Task SaveRecoveryAsync(
        SkinDraftDocument draft,
        CancellationToken cancellationToken = default) =>
        SaveAsync(draft, RecoveryLeaf, cancellationToken);

    public Task<bool> DiscardWorkingCopyAsync(
        Guid draftId,
        long maximumRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (draftId == Guid.Empty || maximumRevision < 0)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_leaseProvider is null
            ? DiscardWorkingCopyPhysical(draftId, maximumRevision)
            : DiscardWorkingCopyLeased(draftId, maximumRevision));
    }

    public DraftOpenResult LoadForOpen(Guid draftId)
    {
        if (_leaseProvider is not null)
        {
            return LoadForOpenLeased(draftId);
        }

        DraftProjectPaths project;
        try
        {
            project = new DraftProjectPaths(_draftsRoot, draftId);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new DraftOpenResult(
                null,
                false,
                [LoadFailure(draftId, DraftIdLeaf(draftId), "draft.path-unsafe",
                    "The draft project path is not safe to read.")]);
        }

        if (!_files.DirectoryExists(project.ProjectRoot))
        {
            return NotFound(draftId);
        }

        try
        {
            EnsureNoExistingReparsePoint(project.ProjectRoot);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new DraftOpenResult(
                null,
                false,
                [LoadFailure(draftId, DraftIdLeaf(draftId), "draft.path-unsafe",
                    "The draft project path is not safe to read.")]);
        }

        var failures = new List<DraftLoadFailure>();
        var named = ReadDocument(project.NamedDraftPath, NamedDraftLeaf, draftId, failures);
        var recovery = ReadDocument(project.RecoveryPath, RecoveryLeaf, draftId, failures);
        if (named is null && recovery is null)
        {
            if (failures.Count == 0)
            {
                failures.Add(LoadFailure(
                    draftId,
                    DraftIdLeaf(draftId),
                    "draft.not-found",
                    "The draft project does not contain a saved document."));
            }

            return new DraftOpenResult(null, false, failures.ToArray());
        }

        var useRecovery = recovery is not null &&
            (named is null || recovery.Revision > named.Revision);
        return new DraftOpenResult(
            useRecovery ? recovery : named,
            useRecovery,
            failures.ToArray());
    }

    public DraftCatalogSnapshot LoadAll()
    {
        if (_leaseProvider is not null)
        {
            return LoadAllLeased();
        }

        if (!_files.DirectoryExists(_draftsRoot))
        {
            return new DraftCatalogSnapshot([], []);
        }

        try
        {
            EnsureNoExistingReparsePoint(_draftsRoot);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new DraftCatalogSnapshot(
                [],
                [LoadFailure(null, "drafts", "draft.path-unsafe",
                    "The drafts catalog path is not safe to read.")]);
        }

        string[] children;
        try
        {
            children = _files.EnumerateDirectories(_draftsRoot).ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new DraftCatalogSnapshot(
                [],
                [LoadFailure(null, "drafts", "draft.read-failed",
                    "The drafts catalog could not be read.")]);
        }

        var healthy = new List<SkinDraftDocument>();
        var corrupt = new List<DraftLoadFailure>();
        foreach (var child in children)
        {
            if (!TryReadDirectDraftId(child, out var draftId))
            {
                continue;
            }

            var result = LoadForOpen(draftId);
            if (result.Document is not null)
            {
                healthy.Add(result.Document);
            }

            corrupt.AddRange(result.Failures);
        }

        return new DraftCatalogSnapshot(
            healthy
                .OrderByDescending(draft => draft.UpdatedAtUtc)
                .ThenBy(draft => draft.DraftId)
                .ToArray(),
            corrupt.ToArray());
    }

    private async Task SaveAsync(
        SkinDraftDocument draft,
        string targetLeaf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var bytes = DraftJsonCodec.Write(draft);
        if (_leaseProvider is not null)
        {
            await SaveLeasedAsync(
                draft,
                targetLeaf,
                bytes,
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var project = new DraftProjectPaths(_draftsRoot, draft.DraftId);
        EnsureNoExistingReparsePoint(project.ProjectRoot);
        cancellationToken.ThrowIfCancellationRequested();

        var targetPath = targetLeaf == NamedDraftLeaf
            ? project.NamedDraftPath
            : project.RecoveryPath;
        var currentOperationId = _operationId();
        if (currentOperationId == Guid.Empty)
        {
            throw new DraftPersistenceException(
                "draft.operation-id.invalid",
                "The draft save operation ID is invalid.");
        }

        var temporaryPath = Path.Combine(
            project.ProjectRoot,
            $".{targetLeaf}.tmp-{currentOperationId:D}".ToLowerInvariant());
        Exception? pending = null;
        try
        {
            _files.CreateDirectory(project.ProjectRoot);
            _files.CreateDirectory(project.AssetsRoot);
            EnsureNoExistingReparsePoint(project.AssetsRoot);
            await _files.WriteAndFlushAsync(
                temporaryPath,
                bytes,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var verifiedBytes = _files.ReadAllBytes(temporaryPath);
            var parsed = DraftJsonCodec.Parse(verifiedBytes);
            if (!parsed.IsValid ||
                parsed.Value is null ||
                parsed.Value.DraftId != draft.DraftId ||
                !verifiedBytes.AsSpan().SequenceEqual(bytes))
            {
                throw new DraftPersistenceException(
                    "draft.validation-failed",
                    "The temporary draft could not be validated.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            _files.ReplaceFile(temporaryPath, targetPath);
        }
        catch (Exception exception)
        {
            pending = exception;
        }

        try
        {
            if (_files.FileExists(temporaryPath))
            {
                _files.DeleteFile(temporaryPath);
            }
        }
        catch (Exception cleanupException)
        {
            throw new DraftPersistenceException(
                "draft.cleanup-failed",
                "The draft save temporary file could not be cleaned up.",
                cleanupException);
        }

        if (pending is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(pending)
                .Throw();
        }
    }

    private bool DiscardWorkingCopyPhysical(
        Guid draftId,
        long maximumRevision)
    {
        try
        {
            var project = new DraftProjectPaths(_draftsRoot, draftId);
            if (!_files.DirectoryExists(project.ProjectRoot))
            {
                return true;
            }

            EnsureNoExistingReparsePoint(project.ProjectRoot);
            if (!_files.FileExists(project.RecoveryPath))
            {
                return true;
            }

            if ((_files.GetAttributes(project.RecoveryPath) &
                    FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            var parsed = DraftJsonCodec.Parse(
                _files.ReadAllBytes(project.RecoveryPath));
            if (!parsed.IsValid ||
                parsed.Value is null ||
                parsed.Value.DraftId != draftId ||
                parsed.Value.Revision > maximumRevision)
            {
                return false;
            }

            _files.DeleteFile(project.RecoveryPath);
            return !_files.FileExists(project.RecoveryPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                ArgumentException)
        {
            return false;
        }
    }

    private bool DiscardWorkingCopyLeased(
        Guid draftId,
        long maximumRevision)
    {
        try
        {
            _ = new DraftProjectPaths(_draftsRoot, draftId);
            using var catalog = _leaseProvider!.OpenCatalog(
                _draftsRoot,
                create: false);
            if (catalog is null)
            {
                return true;
            }

            using var project = catalog.OpenProject(draftId, create: false);
            if (project is null || !project.FileExists(RecoveryLeaf))
            {
                return true;
            }

            var parsed = DraftJsonCodec.Parse(
                project.ReadAllBytes(RecoveryLeaf));
            if (!parsed.IsValid ||
                parsed.Value is null ||
                parsed.Value.DraftId != draftId ||
                parsed.Value.Revision > maximumRevision)
            {
                return false;
            }

            project.DeleteFile(RecoveryLeaf);
            return !project.FileExists(RecoveryLeaf);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                ArgumentException)
        {
            return false;
        }
    }

    private async Task SaveLeasedAsync(
        SkinDraftDocument draft,
        string targetLeaf,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        _ = new DraftProjectPaths(_draftsRoot, draft.DraftId);
        cancellationToken.ThrowIfCancellationRequested();
        var currentOperationId = _operationId();
        if (currentOperationId == Guid.Empty)
        {
            throw new DraftPersistenceException(
                "draft.operation-id.invalid",
                "The draft save operation ID is invalid.");
        }

        var temporaryLeaf =
            $".{targetLeaf}.tmp-{currentOperationId:D}".ToLowerInvariant();
        using var catalog = _leaseProvider!.OpenCatalog(_draftsRoot, create: true) ??
            throw new DraftPersistenceException(
                "draft.path-unsafe",
                "The drafts catalog could not be leased.");
        using var project = catalog.OpenProject(draft.DraftId, create: true) ??
            throw new DraftPersistenceException(
                "draft.path-unsafe",
                "The draft project could not be leased.");
        project.EnsureAssetsDirectory();

        Exception? pending = null;
        try
        {
            await project.WriteAndFlushAsync(
                temporaryLeaf,
                bytes,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var verifiedBytes = project.ReadAllBytes(temporaryLeaf);
            ValidateTemporaryDocument(draft, bytes, verifiedBytes);
            cancellationToken.ThrowIfCancellationRequested();
            project.ReplaceFile(temporaryLeaf, targetLeaf);
        }
        catch (Exception exception)
        {
            pending = exception;
        }

        try
        {
            if (project.FileExists(temporaryLeaf))
            {
                project.DeleteFile(temporaryLeaf);
            }
        }
        catch (Exception cleanupException)
        {
            throw new DraftPersistenceException(
                "draft.cleanup-failed",
                "The draft save temporary file could not be cleaned up.",
                cleanupException);
        }

        if (pending is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(pending)
                .Throw();
        }
    }

    private DraftOpenResult LoadForOpenLeased(Guid draftId)
    {
        try
        {
            _ = new DraftProjectPaths(_draftsRoot, draftId);
            using var catalog = _leaseProvider!.OpenCatalog(_draftsRoot, create: false);
            if (catalog is null)
            {
                return NotFound(draftId);
            }

            using var project = catalog.OpenProject(draftId, create: false);
            return project is null ? NotFound(draftId) : LoadFromLease(project, draftId);
        }
        catch (Exception exception) when (
            exception is DraftUnsafePathException or ArgumentException)
        {
            return new DraftOpenResult(
                null,
                false,
                [LoadFailure(
                    draftId,
                    DraftIdLeaf(draftId),
                    "draft.path-unsafe",
                    "The draft project path is not safe to read.")]);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new DraftOpenResult(
                null,
                false,
                [LoadFailure(
                    draftId,
                    DraftIdLeaf(draftId),
                    "draft.read-failed",
                    "The draft project could not be read and was left unchanged.")]);
        }
    }

    private DraftCatalogSnapshot LoadAllLeased()
    {
        try
        {
            using var catalog = _leaseProvider!.OpenCatalog(_draftsRoot, create: false);
            if (catalog is null)
            {
                return new DraftCatalogSnapshot([], []);
            }

            var healthy = new List<SkinDraftDocument>();
            var corrupt = new List<DraftLoadFailure>();
            foreach (var name in catalog.EnumerateProjectNames())
            {
                if (!TryReadCanonicalDraftId(name, out var draftId))
                {
                    continue;
                }

                try
                {
                    using var project = catalog.OpenProject(draftId, create: false);
                    if (project is null)
                    {
                        corrupt.Add(LoadFailure(
                            draftId,
                            DraftIdLeaf(draftId),
                            "draft.not-found",
                            "The draft project was not found."));
                        continue;
                    }

                    var result = LoadFromLease(project, draftId);
                    if (result.Document is not null)
                    {
                        healthy.Add(result.Document);
                    }

                    corrupt.AddRange(result.Failures);
                }
                catch (DraftUnsafePathException)
                {
                    corrupt.Add(LoadFailure(
                        draftId,
                        DraftIdLeaf(draftId),
                        "draft.path-unsafe",
                        "The draft project path is not safe to read."));
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    corrupt.Add(LoadFailure(
                        draftId,
                        DraftIdLeaf(draftId),
                        "draft.read-failed",
                        "The draft project could not be read and was left unchanged."));
                }
            }

            return new DraftCatalogSnapshot(
                healthy
                    .OrderByDescending(draft => draft.UpdatedAtUtc)
                    .ThenBy(draft => draft.DraftId)
                    .ToArray(),
                corrupt.ToArray());
        }
        catch (DraftUnsafePathException)
        {
            return new DraftCatalogSnapshot(
                [],
                [LoadFailure(
                    null,
                    "drafts",
                    "draft.path-unsafe",
                    "The drafts catalog path is not safe to read.")]);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new DraftCatalogSnapshot(
                [],
                [LoadFailure(
                    null,
                    "drafts",
                    "draft.read-failed",
                    "The drafts catalog could not be read.")]);
        }
    }

    private DraftOpenResult LoadFromLease(IDraftProjectLease project, Guid draftId)
    {
        var failures = new List<DraftLoadFailure>();
        var named = ReadDocument(project, NamedDraftLeaf, draftId, failures);
        var recovery = ReadDocument(project, RecoveryLeaf, draftId, failures);
        if (named is null && recovery is null)
        {
            if (failures.Count == 0)
            {
                failures.Add(LoadFailure(
                    draftId,
                    DraftIdLeaf(draftId),
                    "draft.not-found",
                    "The draft project does not contain a saved document."));
            }

            return new DraftOpenResult(null, false, failures.ToArray());
        }

        var useRecovery = recovery is not null &&
            (named is null || recovery.Revision > named.Revision);
        return new DraftOpenResult(
            useRecovery ? recovery : named,
            useRecovery,
            failures.ToArray());
    }

    private static SkinDraftDocument? ReadDocument(
        IDraftProjectLease project,
        string leafName,
        Guid draftId,
        ICollection<DraftLoadFailure> failures)
    {
        try
        {
            if (!project.FileExists(leafName))
            {
                return null;
            }

            var parsed = DraftJsonCodec.Parse(project.ReadAllBytes(leafName));
            if (!parsed.IsValid || parsed.Value is null || parsed.Value.DraftId != draftId)
            {
                failures.Add(LoadFailure(
                    draftId,
                    leafName,
                    "draft.corrupt",
                    "The draft document is invalid and was left unchanged."));
                return null;
            }

            return parsed.Value;
        }
        catch (DraftUnsafePathException)
        {
            failures.Add(LoadFailure(
                draftId,
                leafName,
                "draft.path-unsafe",
                "The draft document path is not safe to read."));
            return null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            failures.Add(LoadFailure(
                draftId,
                leafName,
                "draft.read-failed",
                "The draft document could not be read and was left unchanged."));
            return null;
        }
    }

    private static void ValidateTemporaryDocument(
        SkinDraftDocument draft,
        ReadOnlySpan<byte> expected,
        ReadOnlySpan<byte> actual)
    {
        var parsed = DraftJsonCodec.Parse(actual);
        if (!parsed.IsValid ||
            parsed.Value is null ||
            parsed.Value.DraftId != draft.DraftId ||
            !actual.SequenceEqual(expected))
        {
            throw new DraftPersistenceException(
                "draft.validation-failed",
                "The temporary draft could not be validated.");
        }
    }

    private SkinDraftDocument? ReadDocument(
        string path,
        string leafName,
        Guid draftId,
        ICollection<DraftLoadFailure> failures)
    {
        try
        {
            if (!_files.FileExists(path))
            {
                return null;
            }

            if ((_files.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                failures.Add(LoadFailure(
                    draftId,
                    leafName,
                    "draft.path-unsafe",
                    "The draft document path is not safe to read."));
                return null;
            }

            var parsed = DraftJsonCodec.Parse(_files.ReadAllBytes(path));
            if (!parsed.IsValid || parsed.Value is null || parsed.Value.DraftId != draftId)
            {
                failures.Add(LoadFailure(
                    draftId,
                    leafName,
                    "draft.corrupt",
                    "The draft document is invalid and was left unchanged."));
                return null;
            }

            return parsed.Value;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            failures.Add(LoadFailure(
                draftId,
                leafName,
                "draft.read-failed",
                "The draft document could not be read and was left unchanged."));
            return null;
        }
    }

    private void EnsureNoExistingReparsePoint(string candidatePath)
    {
        var current = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(candidatePath));
        while (!string.IsNullOrEmpty(current))
        {
            if ((_files.DirectoryExists(current) || _files.FileExists(current)) &&
                (_files.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new DraftPersistenceException(
                    "draft.path-unsafe",
                    "The draft storage path contains a reparse point.");
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) ||
                string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = Path.TrimEndingDirectorySeparator(parent);
        }
    }

    private bool TryReadDirectDraftId(string candidate, out Guid draftId)
    {
        draftId = default;
        string normalized;
        try
        {
            normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            return false;
        }

        if (!string.Equals(
                Path.GetDirectoryName(normalized),
                _draftsRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var leaf = Path.GetFileName(normalized);
        return Guid.TryParseExact(leaf, "D", out draftId) &&
            string.Equals(
                leaf,
                draftId.ToString("D").ToLowerInvariant(),
                StringComparison.Ordinal);
    }

    private static bool TryReadCanonicalDraftId(string name, out Guid draftId) =>
        Guid.TryParseExact(name, "D", out draftId) &&
        string.Equals(
            name,
            draftId.ToString("D").ToLowerInvariant(),
            StringComparison.Ordinal);

    private static DraftOpenResult NotFound(Guid draftId) => new(
        null,
        false,
        [LoadFailure(
            draftId,
            DraftIdLeaf(draftId),
            "draft.not-found",
            "The draft project was not found.")]);

    private static DraftLoadFailure LoadFailure(
        Guid? draftId,
        string leafName,
        string code,
        string message) => new(draftId, leafName, code, message);

    private static string DraftIdLeaf(Guid draftId) =>
        draftId.ToString("D").ToLowerInvariant();
}

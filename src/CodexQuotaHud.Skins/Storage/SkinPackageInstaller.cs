using System.IO;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Serialization;

namespace CodexQuotaHud.Skins.Storage;

public sealed class SkinPackageInstaller
{
    private static readonly SemanticVersion CatalogValidationVersion =
        new(int.MaxValue, int.MaxValue, int.MaxValue);

    private static readonly HashSet<Guid> ReservedBuiltInIds =
    [
        Guid.Parse("10000000-0000-0000-0000-000000000001"),
        Guid.Parse("10000000-0000-0000-0000-000000000002"),
        Guid.Parse("10000000-0000-0000-0000-000000000003"),
        Guid.Parse("10000000-0000-0000-0000-000000000004"),
        Guid.Parse("10000000-0000-0000-0000-000000000005")
    ];

    private static readonly SkinCollisionDecision[] CollisionDecisions =
    [
        SkinCollisionDecision.Replace,
        SkinCollisionDecision.KeepCopy,
        SkinCollisionDecision.Cancel
    ];

    private readonly SkinStoragePaths _paths;
    private readonly ISkinFileSystem _fileSystem;

    public SkinPackageInstaller(SkinStoragePaths paths)
        : this(paths, PhysicalSkinFileSystem.Instance)
    {
    }

    internal SkinPackageInstaller(
        SkinStoragePaths paths,
        ISkinFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _paths = paths;
        _fileSystem = fileSystem;
    }

    public SkinValidationResult<SkinInstallPreview> Inspect(
        string packagePath,
        SemanticVersion hudVersion,
        CancellationToken cancellationToken)
    {
        var package = new SkinPackageReader().ValidateFile(
            packagePath,
            hudVersion,
            cancellationToken);
        if (!package.IsValid)
        {
            return new SkinValidationResult<SkinInstallPreview>(
                null,
                package.Errors);
        }

        var existing = new InstalledSkinCatalog(
            _paths,
            hudVersion,
            _fileSystem).Find(package.Value!.Manifest.SkinId);
        var isDowngrade = existing is not null &&
            package.Value.Manifest.PackageVersion.CompareTo(existing.PackageVersion) < 0;
        return new SkinValidationResult<SkinInstallPreview>(
            new SkinInstallPreview(
                package.Value,
                existing,
                isDowngrade,
                existing is null || isDowngrade ? [] : CollisionDecisions),
            []);
    }

    public SkinInstallResult Install(
        SkinInstallPreview preview,
        SkinCollisionDecision decision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        cancellationToken.ThrowIfCancellationRequested();

        if (preview.IsDowngrade)
        {
            return Error("install.downgrade", "The installed skin is newer than this package.");
        }

        if (preview.Existing is not null && decision == SkinCollisionDecision.Cancel)
        {
            return new SkinInstallResult(
                SkinInstallDisposition.Cancelled,
                null,
                []);
        }

        if (preview.Existing is not null &&
            !preview.AllowedDecisions.Contains(decision))
        {
            return Error("install.decision.invalid", "The collision decision is not allowed.");
        }

        if (preview.Existing is not null &&
            !IsValidExistingRecord(preview.Existing, preview.Package.Manifest.SkinId))
        {
            return Error(
                "install.preview.invalid",
                "The collision preview does not reference an owned installed skin.");
        }

        var existing = preview.Existing;
        if (existing is not null)
        {
            var current = new InstalledSkinCatalog(
                _paths,
                CatalogValidationVersion,
                _fileSystem).Find(preview.Package.Manifest.SkinId);
            if (current is null ||
                !string.Equals(
                    Path.GetFullPath(current.DirectoryPath),
                    Path.GetFullPath(existing.DirectoryPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return Error(
                    "install.preview.invalid",
                    "The installed skin changed after the collision preview was created.");
            }

            if (preview.Package.Manifest.PackageVersion.CompareTo(
                    current.PackageVersion) < 0)
            {
                return Error(
                    "install.downgrade",
                    "The installed skin is newer than this package.");
            }

            existing = current;
        }

        var package = preview.Package;
        var disposition = SkinInstallDisposition.Installed;
        if (existing is not null && decision == SkinCollisionDecision.KeepCopy)
        {
            var newSkinId = Guid.NewGuid();
            var manifest = package.Manifest with
            {
                SkinId = newSkinId,
                OriginSkinId = package.Manifest.SkinId
            };
            package = package with { Manifest = manifest };
            disposition = SkinInstallDisposition.KeptCopy;
        }
        else if (existing is not null)
        {
            disposition = SkinInstallDisposition.Replaced;
        }

        var operationId = Guid.NewGuid();
        var operationPath = Path.Combine(
            _paths.ImportsRoot,
            operationId.ToString("D").ToLowerInvariant());
        var candidateRoot = Path.Combine(operationPath, "candidate");
        var skinDirectoryName = package.Manifest.SkinId.ToString("D").ToLowerInvariant();
        var candidatePath = Path.Combine(candidateRoot, skinDirectoryName);
        var finalPath = Path.Combine(_paths.InstalledSkinsRoot, skinDirectoryName);
        var backupRoot = Path.Combine(operationPath, "backup");
        var backupPath = Path.Combine(backupRoot, skinDirectoryName);
        if (!SafeOwnedDirectory.IsSafeStoragePath(
                _paths,
                operationPath,
                _fileSystem) ||
            !SafeOwnedDirectory.IsSafeStoragePath(
                _paths,
                candidatePath,
                _fileSystem) ||
            !SafeOwnedDirectory.IsSafeStoragePath(
                _paths,
                finalPath,
                _fileSystem))
        {
            return Error(
                "install.path.invalid",
                "Skin installation storage contains an unsafe path or reparse point.");
        }

        var retainOperation = false;
        var backupMoved = false;
        var candidatePromoted = false;
        SkinInstallResult result;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _fileSystem.CreateDirectory(candidatePath);
            WriteCandidate(candidatePath, package, cancellationToken);

            var staged = new InstalledSkinReader(
                candidateRoot,
                package.Manifest.MinimumHudVersion,
                _fileSystem).Read(candidatePath);
            if (!staged.IsValid)
            {
                result = new SkinInstallResult(
                    SkinInstallDisposition.Cancelled,
                    null,
                    staged.Errors);
                return FinishOperation(
                    result,
                    operationPath,
                    operationId,
                    retainOperation);
            }

            cancellationToken.ThrowIfCancellationRequested();
            _fileSystem.CreateDirectory(_paths.InstalledSkinsRoot);
            if (existing is not null && decision == SkinCollisionDecision.Replace)
            {
                _fileSystem.CreateDirectory(backupRoot);
                _fileSystem.MoveDirectory(existing.DirectoryPath, backupPath);
                backupMoved = true;
                cancellationToken.ThrowIfCancellationRequested();
            }

            try
            {
                _fileSystem.MoveDirectory(candidatePath, finalPath);
                candidatePromoted = true;
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                if (backupMoved)
                {
                    try
                    {
                        RestoreBackup(finalPath, backupPath);
                        backupMoved = false;
                        candidatePromoted = false;
                    }
                    catch (Exception rollbackException) when (
                        rollbackException is IOException or UnauthorizedAccessException)
                    {
                        retainOperation = true;
                        result = Error(
                            "install.rollback-failed",
                            $"Promotion and rollback failed. Recovery operation: {operationId:D}.");
                        return FinishOperation(
                            result,
                            operationPath,
                            operationId,
                            retainOperation);
                    }
                }

                result = Error("install.io", "The skin could not be installed safely.");
                return FinishOperation(
                    result,
                    operationPath,
                    operationId,
                    retainOperation);
            }

            var installed = new InstalledSkinReader(
                _paths.InstalledSkinsRoot,
                package.Manifest.MinimumHudVersion,
                _fileSystem).Read(finalPath);
            if (!installed.IsValid)
            {
                if (backupMoved)
                {
                    RestoreBackup(finalPath, backupPath);
                    backupMoved = false;
                    candidatePromoted = false;
                }
                else if (_fileSystem.DirectoryExists(finalPath))
                {
                    _fileSystem.DeleteDirectory(finalPath, recursive: true);
                    candidatePromoted = false;
                }

                result = new SkinInstallResult(
                    SkinInstallDisposition.Cancelled,
                    null,
                    installed.Errors);
                return FinishOperation(
                    result,
                    operationPath,
                    operationId,
                    retainOperation);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (backupMoved)
            {
                try
                {
                    _fileSystem.DeleteDirectory(backupPath, recursive: true);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    retainOperation = true;
                    result = new SkinInstallResult(
                        disposition,
                        installed.Value,
                        [CleanupFailed(operationId)]);
                    return FinishOperation(
                        result,
                        operationPath,
                        operationId,
                        retainOperation);
                }

                backupMoved = false;
            }

            result = new SkinInstallResult(disposition, installed.Value, []);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (backupMoved || _fileSystem.DirectoryExists(backupPath))
                {
                    RestoreBackup(finalPath, backupPath);
                    backupMoved = false;
                    candidatePromoted = false;
                }
                else if (candidatePromoted && _fileSystem.DirectoryExists(finalPath))
                {
                    _fileSystem.DeleteDirectory(finalPath, recursive: true);
                    candidatePromoted = false;
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                retainOperation = true;
            }

            _ = FinishOperation(
                Error("install.cancelled", "The skin installation was cancelled."),
                operationPath,
                operationId,
                retainOperation);
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            try
            {
                if (backupMoved || _fileSystem.DirectoryExists(backupPath))
                {
                    RestoreBackup(finalPath, backupPath);
                    backupMoved = false;
                    candidatePromoted = false;
                }
                else if (candidatePromoted && _fileSystem.DirectoryExists(finalPath))
                {
                    _fileSystem.DeleteDirectory(finalPath, recursive: true);
                    candidatePromoted = false;
                }
            }
            catch (Exception rollbackException) when (
                rollbackException is IOException or UnauthorizedAccessException)
            {
                retainOperation = true;
            }

            result = Error("install.io", "The skin could not be installed safely.");
        }

        return FinishOperation(
            result,
            operationPath,
            operationId,
            retainOperation);
    }

    public SkinValidationResult<Guid> Remove(Guid skinId)
    {
        if (skinId == Guid.Empty)
        {
            return RemoveError(
                "remove.invalid-id",
                "The custom skin ID must not be empty.");
        }

        if (ReservedBuiltInIds.Contains(skinId))
        {
            return RemoveError(
                "remove.reserved-id",
                "Built-in skins cannot be removed from custom storage.");
        }

        if (!_fileSystem.DirectoryExists(_paths.InstalledSkinsRoot))
        {
            return RemoveError("remove.not-found", "The custom skin is not installed.");
        }

        var ownedRoot = new SafeOwnedDirectory(
            _paths.InstalledSkinsRoot,
            _fileSystem);
        try
        {
            if (!SafeOwnedDirectory.IsSafeStoragePath(
                    _paths,
                    ownedRoot.RootPath,
                    _fileSystem) ||
                ownedRoot.HasExistingReparsePoint(ownedRoot.RootPath))
            {
                return RemoveError(
                    "remove.path.invalid",
                    "Custom skin storage cannot be a reparse point.");
            }

            var matches = _fileSystem.EnumerateDirectories(ownedRoot.RootPath)
                .Where(path =>
                {
                    var name = Path.GetFileName(
                        Path.TrimEndingDirectorySeparator(path));
                    return Guid.TryParse(name, out var parsed) && parsed == skinId;
                })
                .ToArray();
            if (matches.Length == 0)
            {
                return RemoveError(
                    "remove.not-found",
                    "The custom skin is not installed.");
            }

            if (matches.Length != 1 ||
                !ownedRoot.TryResolveSkinDirectory(
                    matches[0],
                    out var directoryPath,
                    out var directorySkinId) ||
                directorySkinId != skinId)
            {
                return RemoveError(
                    "remove.path.invalid",
                    "The custom skin directory is not an owned lowercase GUID directory.");
            }

            _fileSystem.DeleteDirectory(directoryPath, recursive: true);
            return new SkinValidationResult<Guid>(skinId, []);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return RemoveError(
                "remove.io",
                "The custom skin could not be removed safely.");
        }
    }

    private SkinInstallResult FinishOperation(
        SkinInstallResult result,
        string operationPath,
        Guid operationId,
        bool retainOperation)
    {
        if (retainOperation || !_fileSystem.DirectoryExists(operationPath))
        {
            return result;
        }

        try
        {
            _fileSystem.DeleteDirectory(operationPath, recursive: true);
            return result;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            var errors = result.Errors
                .Concat([CleanupFailed(operationId)])
                .ToArray();
            return result with { Errors = errors };
        }
    }

    private bool IsValidExistingRecord(
        InstalledSkinRecord existing,
        Guid importedSkinId)
    {
        if (existing.SkinId != importedSkinId ||
            existing.Package.Manifest.SkinId != existing.SkinId ||
            !string.Equals(
                existing.SelectionKey,
                $"custom:{existing.SkinId:D}",
                StringComparison.Ordinal) ||
            !SafeOwnedDirectory.IsSafeStoragePath(
                _paths,
                existing.DirectoryPath,
                _fileSystem))
        {
            return false;
        }

        var ownedSkins = new SafeOwnedDirectory(
            _paths.InstalledSkinsRoot,
            _fileSystem);
        return ownedSkins.TryResolveSkinDirectory(
                existing.DirectoryPath,
                out var resolvedPath,
                out var directorySkinId) &&
            directorySkinId == existing.SkinId &&
            _fileSystem.DirectoryExists(resolvedPath);
    }

    private void RestoreBackup(string finalPath, string backupPath)
    {
        if (_fileSystem.DirectoryExists(finalPath))
        {
            _fileSystem.DeleteDirectory(finalPath, recursive: true);
        }

        if (_fileSystem.DirectoryExists(backupPath))
        {
            _fileSystem.MoveDirectory(backupPath, finalPath);
        }
    }

    private static SkinValidationError CleanupFailed(Guid operationId) =>
        new(
            "install.cleanup-failed",
            "$operation",
            $"The new skin was installed, but cleanup failed. Recovery operation: {operationId:D}.");

    private void WriteCandidate(
        string candidatePath,
        SkinPackageDocument package,
        CancellationToken cancellationToken)
    {
        WriteFile(
            Path.Combine(candidatePath, SkinPackageLimits.ManifestFileName),
            SkinJsonCodec.WriteManifest(package.Manifest),
            cancellationToken);
        WriteFile(
            Path.Combine(candidatePath, SkinPackageLimits.ThemeFileName),
            SkinJsonCodec.WriteTheme(package.Theme),
            cancellationToken);
        foreach (var reference in package.Manifest.Assets.OrderBy(asset => asset.Path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var asset = package.Assets[reference.Slot];
            var path = Path.Combine(
                candidatePath,
                reference.Path.Replace('/', Path.DirectorySeparatorChar));
            var parent = Path.GetDirectoryName(path)!;
            _fileSystem.CreateDirectory(parent);
            WriteFile(path, asset.Content, cancellationToken);
        }
    }

    private void WriteFile(
        string path,
        byte[] content,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _fileSystem.WriteAllBytesAndFlush(path, content);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static SkinInstallResult Error(string code, string message) =>
        new(
            SkinInstallDisposition.Cancelled,
            null,
            [new SkinValidationError(code, "$install", message)]);

    private static SkinValidationResult<Guid> RemoveError(
        string code,
        string message) =>
        new(
            default,
            [new SkinValidationError(code, "$remove", message)]);
}

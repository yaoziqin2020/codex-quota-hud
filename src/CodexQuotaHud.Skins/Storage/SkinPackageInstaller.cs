using System.IO;
using System.Security.Cryptography;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Serialization;
using CodexQuotaHud.Skins.Validation;

namespace CodexQuotaHud.Skins.Storage;

public sealed class SkinPackageInstaller
{
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
    private readonly SemanticVersion _currentHudVersion;
    private readonly ISkinFileSystem _fileSystem;
    private readonly ISkinInstallLockProvider _lockProvider;
    private readonly IDirectoryIdentityProvider _identityProvider;
    private readonly IDirectoryLeaseProvider _directoryLeaseProvider;
    private readonly IDirectoryMoveProvider _directoryMoveProvider;
    private readonly ISafeDirectoryDeleteProvider _directoryDeleteProvider;

    public SkinPackageInstaller(
        SkinStoragePaths paths,
        SemanticVersion currentHudVersion)
        : this(paths, currentHudVersion, PhysicalSkinFileSystem.Instance)
    {
    }

    internal SkinPackageInstaller(
        SkinStoragePaths paths,
        SemanticVersion currentHudVersion,
        ISkinFileSystem fileSystem,
        ISkinInstallLockProvider? lockProvider = null,
        IDirectoryIdentityProvider? identityProvider = null,
        IDirectoryLeaseProvider? directoryLeaseProvider = null,
        IDirectoryMoveProvider? directoryMoveProvider = null,
        ISafeDirectoryDeleteProvider? directoryDeleteProvider = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _paths = paths;
        _currentHudVersion = currentHudVersion;
        _fileSystem = fileSystem;
        _lockProvider = lockProvider ?? NamedSkinInstallLockProvider.Instance;
        _identityProvider = identityProvider ?? PhysicalDirectoryIdentityProvider.Instance;
        _directoryLeaseProvider = directoryLeaseProvider ??
            PhysicalDirectoryLeaseProvider.Instance;
        _directoryMoveProvider = directoryMoveProvider ??
            (ReferenceEquals(fileSystem, PhysicalSkinFileSystem.Instance)
                ? PhysicalDirectoryLeaseProvider.Instance
                : new FileSystemDirectoryMoveProvider(fileSystem));
        _directoryDeleteProvider = directoryDeleteProvider ??
            (ReferenceEquals(fileSystem, PhysicalSkinFileSystem.Instance)
                ? PhysicalDirectoryDeleteProvider.Instance
                : new FileSystemDirectoryDeleteProvider(fileSystem));
    }

    public SkinValidationResult<SkinInstallPreview> Inspect(
        string packagePath,
        SemanticVersion hudVersion,
        CancellationToken cancellationToken)
    {
        if (hudVersion != _currentHudVersion)
        {
            return new SkinValidationResult<SkinInstallPreview>(
                null,
                [new SkinValidationError(
                    "inspect.hud-version-mismatch",
                    "$hudVersion",
                    "The inspection HUD version does not match the installer.")]);
        }

        var package = new SkinPackageReader().ValidateFile(
            packagePath,
            _currentHudVersion,
            cancellationToken);
        if (!package.IsValid)
        {
            return new SkinValidationResult<SkinInstallPreview>(
                null,
                package.Errors);
        }

        var existing = new InstalledSkinCatalog(
            _paths,
            _currentHudVersion,
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

        var validatedPackage = ValidateExternalPackage(
            preview.Package,
            cancellationToken);
        if (!validatedPackage.IsValid)
        {
            return new SkinInstallResult(
                SkinInstallDisposition.Cancelled,
                null,
                validatedPackage.Errors);
        }

        var package = validatedPackage.Value!;
        if (preview.Existing is not null &&
            !IsValidExistingRecord(preview.Existing, package.Manifest.SkinId))
        {
            return Error(
                "install.preview.invalid",
                "The collision preview does not reference an owned installed skin.");
        }

        using var transactionLock = _lockProvider.Acquire(
            _paths.InstalledSkinsRoot,
            package.Manifest.SkinId,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var existing = new InstalledSkinCatalog(
            _paths,
            _currentHudVersion,
            _fileSystem).Find(package.Manifest.SkinId);
        if (!MatchesPreviewState(preview.Existing, existing))
        {
            return Error(
                "install.state-changed",
                "The installed skin state changed. Inspect the package again.");
        }

        if (existing is not null &&
            package.Manifest.PackageVersion.CompareTo(existing.PackageVersion) < 0)
        {
            return Error(
                "install.downgrade",
                "The installed skin is newer than this package.");
        }

        if (!Enum.IsDefined(decision) ||
            (existing is null && decision != SkinCollisionDecision.Replace))
        {
            return Error(
                "install.decision.invalid",
                "The collision decision is not allowed.");
        }

        if (existing is not null && decision == SkinCollisionDecision.Cancel)
        {
            return new SkinInstallResult(
                SkinInstallDisposition.Cancelled,
                null,
                []);
        }

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
        IDirectoryLease? candidateLease = null;
        IDirectoryLease? candidateParentLease = null;
        IDirectoryLease? existingLease = null;
        IDirectoryLease? backupParentLease = null;
        IDirectoryLease? installedRootLease = null;
        SkinInstallResult result;

        void DisposeTransactionLeases()
        {
            existingLease?.Dispose();
            existingLease = null;
            candidateLease?.Dispose();
            candidateLease = null;
            installedRootLease?.Dispose();
            installedRootLease = null;
            backupParentLease?.Dispose();
            backupParentLease = null;
            candidateParentLease?.Dispose();
            candidateParentLease = null;
        }

        SkinInstallResult FinishTransaction(
            SkinInstallResult transactionResult,
            bool retain)
        {
            DisposeTransactionLeases();
            return FinishOperation(
                transactionResult,
                operationPath,
                operationId,
                retain);
        }

        bool TryRollbackExactHandles()
        {
            if (candidatePromoted)
            {
                if (candidateLease is null || candidateParentLease is null)
                {
                    return false;
                }

                _directoryMoveProvider.Move(
                    candidateLease,
                    finalPath,
                    candidateParentLease,
                    candidateRoot,
                    skinDirectoryName,
                    candidatePath);
                candidatePromoted = false;
            }

            if (backupMoved)
            {
                if (existingLease is null || installedRootLease is null)
                {
                    return false;
                }

                _directoryMoveProvider.Move(
                    existingLease,
                    backupPath,
                    installedRootLease,
                    _paths.InstalledSkinsRoot,
                    skinDirectoryName,
                    finalPath);
                backupMoved = false;
            }

            return true;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _fileSystem.CreateDirectory(candidatePath);
            candidateParentLease = _directoryLeaseProvider.Lease(candidateRoot);
            candidateLease = _directoryLeaseProvider.Lease(candidatePath);
            WriteCandidate(candidatePath, package, cancellationToken);
            var staged = new InstalledSkinReader(
                candidateRoot,
                _currentHudVersion,
                _fileSystem,
                allowLocalProvenance:
                    disposition == SkinInstallDisposition.KeptCopy).Read(candidatePath);
            if (!staged.IsValid)
            {
                result = new SkinInstallResult(
                    SkinInstallDisposition.Cancelled,
                    null,
                    staged.Errors);
                return FinishTransaction(result, retainOperation);
            }

            cancellationToken.ThrowIfCancellationRequested();
            _fileSystem.CreateDirectory(_paths.InstalledSkinsRoot);
            installedRootLease = _directoryLeaseProvider.Lease(
                _paths.InstalledSkinsRoot);
            if (existing is not null && decision == SkinCollisionDecision.Replace)
            {
                _fileSystem.CreateDirectory(backupRoot);
                existingLease = _directoryLeaseProvider.Lease(
                    existing.DirectoryPath);
                backupParentLease = _directoryLeaseProvider.Lease(backupRoot);
                _directoryMoveProvider.Move(
                    existingLease,
                    existing.DirectoryPath,
                    backupParentLease,
                    backupRoot,
                    skinDirectoryName,
                    backupPath);
                backupMoved = true;
                cancellationToken.ThrowIfCancellationRequested();
            }

            try
            {
                _directoryMoveProvider.Move(
                    candidateLease,
                    candidatePath,
                    installedRootLease,
                    _paths.InstalledSkinsRoot,
                    skinDirectoryName,
                    finalPath);
                candidatePromoted = true;
                if (candidateLease is null ||
                    !_identityProvider.TryGetIdentity(
                        finalPath,
                        out var candidateIdentity) ||
                    candidateIdentity != candidateLease.Identity)
                {
                    retainOperation = true;
                    result = RollbackFailed(operationId);
                    return FinishTransaction(result, retainOperation);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                if (backupMoved)
                {
                    try
                    {
                        if (!TryRollbackExactHandles())
                        {
                            retainOperation = true;
                            result = RollbackFailed(operationId);
                            return FinishTransaction(result, retainOperation);
                        }
                    }
                    catch (Exception rollbackException) when (
                        rollbackException is IOException or UnauthorizedAccessException)
                    {
                        retainOperation = true;
                        result = RollbackFailed(operationId);
                        return FinishTransaction(result, retainOperation);
                    }
                }

                result = Error("install.io", "The skin could not be installed safely.");
                return FinishTransaction(result, retainOperation);
            }

            var installed = new InstalledSkinReader(
                _paths.InstalledSkinsRoot,
                _currentHudVersion,
                _fileSystem,
                allowLocalProvenance:
                    disposition == SkinInstallDisposition.KeptCopy).Read(finalPath);
            if (!installed.IsValid)
            {
                try
                {
                    if (!TryRollbackExactHandles())
                    {
                        retainOperation = true;
                        result = RollbackFailed(operationId);
                        return FinishTransaction(result, retainOperation);
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    retainOperation = true;
                    result = RollbackFailed(operationId);
                    return FinishTransaction(result, retainOperation);
                }

                result = new SkinInstallResult(
                    SkinInstallDisposition.Cancelled,
                    null,
                    installed.Errors);
                return FinishTransaction(result, retainOperation);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (backupMoved)
            {
                backupMoved = false;
            }

            result = new SkinInstallResult(disposition, installed.Value, []);
        }
        catch (OperationCanceledException)
        {
            var rollbackSucceeded = true;
            try
            {
                rollbackSucceeded = TryRollbackExactHandles();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                retainOperation = true;
                rollbackSucceeded = false;
            }

            if (!rollbackSucceeded)
            {
                retainOperation = true;
                return FinishTransaction(
                    RollbackFailed(operationId),
                    retainOperation);
            }

            _ = FinishTransaction(
                Error("install.cancelled", "The skin installation was cancelled."),
                retainOperation);
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            var rollbackSucceeded = true;
            try
            {
                rollbackSucceeded = TryRollbackExactHandles();
            }
            catch (Exception rollbackException) when (
                rollbackException is IOException or UnauthorizedAccessException)
            {
                retainOperation = true;
                rollbackSucceeded = false;
            }

            if (!rollbackSucceeded)
            {
                retainOperation = true;
                result = RollbackFailed(operationId);
            }
            else
            {
                result = Error("install.io", "The skin could not be installed safely.");
            }
        }
        finally
        {
            DisposeTransactionLeases();
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

        using var installLock = _lockProvider.Acquire(
            _paths.InstalledSkinsRoot,
            skinId,
            CancellationToken.None);

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

            var operationId = Guid.NewGuid();
            var operationPath = Path.Combine(
                _paths.ImportsRoot,
                operationId.ToString("D").ToLowerInvariant());
            var removeRoot = Path.Combine(operationPath, "remove");
            var directoryName = skinId.ToString("D").ToLowerInvariant();
            var quarantinePath = Path.Combine(removeRoot, directoryName);
            if (!SafeOwnedDirectory.IsSafeStoragePath(
                    _paths,
                    quarantinePath,
                    _fileSystem))
            {
                return RemoveError(
                    "remove.path.invalid",
                    "The removal quarantine path is unsafe.");
            }

            _fileSystem.CreateDirectory(removeRoot);
            using (var installedRootLease = _directoryLeaseProvider.Lease(
                       _paths.InstalledSkinsRoot))
            using (var targetLease = _directoryLeaseProvider.Lease(directoryPath))
            using (var removeParentLease = _directoryLeaseProvider.Lease(removeRoot))
            {
                _directoryMoveProvider.Move(
                    targetLease,
                    directoryPath,
                    removeParentLease,
                    removeRoot,
                    directoryName,
                    quarantinePath);
            }

            // C4 first quarantines the exact leased object. The operation tree is
            // cleaned only after every lease that prevents deletion is released.
            try
            {
                _directoryDeleteProvider.DeleteOwnedTree(
                    operationPath,
                    SkinPackageLimits.MaximumEntries + 3);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return RemoveError(
                    "remove.cleanup-failed",
                    $"The skin was removed, but cleanup failed. Recovery operation: {operationId:D}.");
            }

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

    private SkinValidationResult<SkinPackageDocument> ValidateExternalPackage(
        SkinPackageDocument? package,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (package?.Manifest is null ||
                package.Theme is null ||
                package.Assets is null ||
                package.Manifest.Assets is null)
            {
                return InvalidPreview(
                    "The collision preview does not contain a complete package.");
            }

            var manifest = package.Manifest with
            {
                Assets = package.Manifest.Assets.ToArray()
            };
            var contract = SkinContractValidator.Validate(
                manifest,
                package.Theme,
                _currentHudVersion,
                allowLocalProvenance: false);
            if (!contract.IsValid)
            {
                return new SkinValidationResult<SkinPackageDocument>(
                    null,
                    contract.Errors);
            }

            var assetSnapshot = new Dictionary<SkinAssetSlot, SkinAsset>();
            foreach (var pair in package.Assets)
            {
                assetSnapshot.Add(pair.Key, pair.Value);
            }

            if (assetSnapshot.Count != manifest.Assets.Count)
            {
                return InvalidPreview(
                    "The collision preview asset dictionary does not match its manifest.");
            }

            long totalBytes = checked(
                SkinJsonCodec.WriteManifest(manifest).LongLength +
                SkinJsonCodec.WriteTheme(contract.Value!.Theme).LongLength);
            if (totalBytes > SkinPackageLimits.MaximumExtractedBytes)
            {
                return InvalidPreview(
                    "The collision preview exceeds the extracted size limit.");
            }

            var validatedAssets = new Dictionary<SkinAssetSlot, SkinAsset>();
            long decodedPixels = 0;
            foreach (var reference in manifest.Assets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reference is null ||
                    !assetSnapshot.TryGetValue(reference.Slot, out var asset) ||
                    asset is null ||
                    asset.Slot != reference.Slot ||
                    !string.Equals(
                        asset.RelativePath,
                        reference.Path,
                        StringComparison.Ordinal) ||
                    asset.Content is null)
                {
                    return InvalidPreview(
                        "The collision preview asset dictionary does not match its manifest.");
                }

                if (asset.Content.LongLength > SkinPackageLimits.MaximumImageBytes ||
                    asset.Content.LongLength >
                        SkinPackageLimits.MaximumExtractedBytes - totalBytes)
                {
                    return InvalidPreview(
                        "The collision preview exceeds an asset size limit.");
                }

                var content = asset.Content.ToArray();
                totalBytes = checked(totalBytes + content.LongLength);
                var hash = Convert.ToHexString(SHA256.HashData(content))
                    .ToLowerInvariant();
                if (!string.Equals(hash, reference.Sha256, StringComparison.Ordinal))
                {
                    return InvalidPreview(
                        "The collision preview asset hash does not match its manifest.");
                }

                var decoded = SkinImageDecoder.Decode(
                    reference.Slot,
                    reference.Path,
                    content,
                    SkinPackageLimits.MaximumDecodedPixels - decodedPixels);
                var pixels = checked(
                    (long)decoded.PixelWidth * decoded.PixelHeight);
                decodedPixels = checked(decodedPixels + pixels);
                if (asset.PixelWidth != decoded.PixelWidth ||
                    asset.PixelHeight != decoded.PixelHeight ||
                    asset.HasAlpha != decoded.HasAlpha)
                {
                    return InvalidPreview(
                        "The collision preview asset metadata does not match its content.");
                }

                validatedAssets.Add(
                    reference.Slot,
                    new SkinAsset(
                        reference.Slot,
                        reference.Path,
                        content,
                        decoded.PixelWidth,
                        decoded.PixelHeight,
                        decoded.HasAlpha));
            }

            return new SkinValidationResult<SkinPackageDocument>(
                new SkinPackageDocument(
                    manifest,
                    contract.Value.Theme,
                    validatedAssets),
                []);
        }
        catch (SkinImageValidationException exception)
        {
            return new SkinValidationResult<SkinPackageDocument>(
                null,
                [new SkinValidationError(
                    exception.Code,
                    "$image",
                    exception.Message)]);
        }
        catch (Exception exception) when (
            exception is IOException or OverflowException or
                ArgumentException or KeyNotFoundException or
                InvalidOperationException)
        {
            return InvalidPreview(
                "The collision preview package is malformed.");
        }
    }

    private static SkinValidationResult<SkinPackageDocument> InvalidPreview(
        string message) =>
        new(
            null,
            [new SkinValidationError(
                "install.preview.invalid",
                "$install.preview",
                message)]);

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
            _directoryDeleteProvider.DeleteOwnedTree(
                operationPath,
                SkinPackageLimits.MaximumEntries + 3);
            return result;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            var cleanupError = result.Installed is null
                ? OperationCleanupFailed(operationId)
                : CleanupFailed(operationId);
            var errors = result.Errors
                .Concat([cleanupError])
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
                out _,
                out var directorySkinId) &&
            directorySkinId == existing.SkinId;
    }

    private static bool MatchesPreviewState(
        InstalledSkinRecord? previewExisting,
        InstalledSkinRecord? currentExisting)
    {
        if (previewExisting is null || currentExisting is null)
        {
            return previewExisting is null && currentExisting is null;
        }

        return previewExisting.SkinId == currentExisting.SkinId &&
            previewExisting.PackageVersion == currentExisting.PackageVersion &&
            string.Equals(
                Path.GetFullPath(previewExisting.DirectoryPath),
                Path.GetFullPath(currentExisting.DirectoryPath),
                StringComparison.OrdinalIgnoreCase);
    }

    private static SkinInstallResult RollbackFailed(Guid operationId) =>
        Error(
            "install.rollback-failed",
            $"Rollback could not safely identify the promoted skin. Recovery operation: {operationId:D}.");

    private static SkinValidationError CleanupFailed(Guid operationId) =>
        new(
            "install.cleanup-failed",
            "$operation",
            $"The new skin was installed, but cleanup failed. Recovery operation: {operationId:D}.");

    private static SkinValidationError OperationCleanupFailed(Guid operationId) =>
        new(
            "install.operation-cleanup-failed",
            "$operation",
            $"The skin was not installed, and temporary cleanup failed. Recovery operation: {operationId:D}.");

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

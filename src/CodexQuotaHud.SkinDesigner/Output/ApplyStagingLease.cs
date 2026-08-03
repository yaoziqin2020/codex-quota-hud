using System.IO;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Storage;
using Microsoft.Win32.SafeHandles;

namespace CodexQuotaHud.SkinDesigner.Output;

internal interface IApplyStagingLeaseProvider
{
    IApplyStagingLease Create(SkinStoragePaths paths);
}

internal interface IApplyStagingLease : IDisposable
{
    string OperationPath { get; }

    string PackagePath { get; }

    Stream PackageStream { get; }

    void FlushPackageToDisk();

    void DeleteOwnedOperation();
}

internal sealed class PhysicalApplyStagingLeaseProvider :
    IApplyStagingLeaseProvider
{
    private readonly Action<SafeFileHandle> _afterPackageCreated;

    internal static PhysicalApplyStagingLeaseProvider Instance { get; } = new();

    private PhysicalApplyStagingLeaseProvider() : this(_ => { })
    {
    }

    internal PhysicalApplyStagingLeaseProvider(
        Action<SafeFileHandle> afterPackageCreated)
    {
        _afterPackageCreated = afterPackageCreated ??
            throw new ArgumentNullException(nameof(afterPackageCreated));
    }

    public IApplyStagingLease Create(SkinStoragePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var settingsRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(paths.SettingsRoot));
        var localRoot = Path.GetDirectoryName(settingsRoot);
        var importsRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(paths.ImportsRoot));
        if (string.IsNullOrEmpty(localRoot) ||
            !string.Equals(
                Path.GetFileName(settingsRoot),
                "CodexQuotaHud",
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                importsRoot,
                Path.Combine(settingsRoot, "imports"),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Apply staging storage has an invalid shape.");
        }

        WindowsDraftDirectoryLease? local = null;
        WindowsDraftDirectoryLease? settings = null;
        WindowsDraftDirectoryLease? imports = null;
        WindowsDraftDirectoryLease? operation = null;
        FileStream? package = null;
        try
        {
            local = WindowsDraftDirectoryLease.Open(localRoot);
            settings = local.OpenChildDirectory(
                "CodexQuotaHud",
                settingsRoot,
                create: true) ?? throw new IOException(
                    "The apply settings directory could not be leased.");
            imports = settings.OpenChildDirectory(
                "imports",
                importsRoot,
                create: true) ?? throw new IOException(
                    "The apply imports directory could not be leased.");
            for (var attempt = 0; attempt < 16 && operation is null; attempt++)
            {
                var operationName = Guid.NewGuid().ToString("D").ToLowerInvariant();
                operation = imports.TryCreateChildDirectoryExclusive(
                    operationName,
                    Path.Combine(importsRoot, operationName));
            }

            if (operation is null)
            {
                throw new IOException(
                    "A unique apply staging operation could not be created.");
            }

            var packagePath = Path.Combine(operation.ExpectedPath, "apply.cqskin");
            var packageHandle = operation.UseValidatedHandle(parentHandle =>
                DraftNative.OpenRelativeFile(
                    parentHandle,
                    "apply.cqskin",
                    create: true));
            try
            {
                _afterPackageCreated(packageHandle);
                _ = DraftNative.ValidateFile(packageHandle, packagePath);
                package = new FileStream(packageHandle, FileAccess.ReadWrite);
            }
            catch
            {
                try
                {
                    DraftNative.DeleteFile(packageHandle);
                }
                catch
                {
                    // Cleanup is best-effort and cannot replace the primary
                    // validation/stream-construction failure.
                }

                packageHandle.Dispose();
                throw;
            }

            imports.Dispose();
            imports = null;
            settings.Dispose();
            settings = null;
            local.Dispose();
            local = null;
            var result = new PhysicalApplyStagingLease(
                operation,
                package,
                packagePath);
            operation = null;
            package = null;
            return result;
        }
        catch
        {
            package?.Dispose();
            if (operation is not null)
            {
                try
                {
                    operation.DeleteIfEmpty();
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                }
            }

            operation?.Dispose();
            imports?.Dispose();
            settings?.Dispose();
            local?.Dispose();
            throw;
        }
    }
}

internal sealed class PhysicalApplyStagingLease : IApplyStagingLease
{
    private WindowsDraftDirectoryLease? _operation;
    private FileStream? _package;
    private int _deleted;

    internal PhysicalApplyStagingLease(
        WindowsDraftDirectoryLease operation,
        FileStream package,
        string packagePath)
    {
        _operation = operation;
        _package = package;
        OperationPath = operation.ExpectedPath;
        PackagePath = Path.GetFullPath(packagePath);
    }

    public string OperationPath { get; }

    public string PackagePath { get; }

    public Stream PackageStream => _package ??
        throw new ObjectDisposedException(nameof(PhysicalApplyStagingLease));

    public void FlushPackageToDisk()
    {
        var package = _package ??
            throw new ObjectDisposedException(nameof(PhysicalApplyStagingLease));
        _ = DraftNative.ValidateFile(package.SafeFileHandle, PackagePath);
        package.Flush(flushToDisk: true);
    }

    public void DeleteOwnedOperation()
    {
        if (Interlocked.Exchange(ref _deleted, 1) != 0)
        {
            return;
        }

        var package = _package ??
            throw new ObjectDisposedException(nameof(PhysicalApplyStagingLease));
        var operation = _operation ??
            throw new ObjectDisposedException(nameof(PhysicalApplyStagingLease));
        try
        {
            _ = DraftNative.ValidateFile(package.SafeFileHandle, PackagePath);
            DraftNative.DeleteFile(package.SafeFileHandle);
        }
        catch
        {
            Volatile.Write(ref _deleted, 0);
            throw;
        }

        package.Dispose();
        _package = null;
        try
        {
            operation.DeleteIfEmpty();
            operation.Dispose();
            _operation = null;
        }
        catch
        {
            Volatile.Write(ref _deleted, 0);
            throw;
        }
    }

    public void Dispose()
    {
        _package?.Dispose();
        _package = null;
        _operation?.Dispose();
        _operation = null;
    }
}

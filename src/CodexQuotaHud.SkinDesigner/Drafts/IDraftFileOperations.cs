using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CodexQuotaHud.SkinDesigner.Drafts;

public interface IDraftFileOperations
{
    void CreateDirectory(string path);

    bool DirectoryExists(string path);

    bool FileExists(string path);

    FileAttributes GetAttributes(string path);

    IEnumerable<string> EnumerateDirectories(string path);

    byte[] ReadAllBytes(string path);

    Task WriteAndFlushAsync(
        string path,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken);

    void ReplaceFile(string sourcePath, string destinationPath);

    void DeleteFile(string path);
}

internal interface IDraftStorageLeaseProvider
{
    IDraftCatalogLease? OpenCatalog(string draftsRoot, bool create);
}

internal interface IDraftCatalogLease : IDisposable
{
    IReadOnlyList<string> EnumerateProjectNames();

    IDraftProjectLease? OpenProject(Guid draftId, bool create);
}

internal interface IDraftProjectLease : IDisposable
{
    void EnsureAssetsDirectory();

    bool FileExists(string fixedLeafName);

    byte[] ReadAllBytes(string fixedLeafName);

    Task WriteAndFlushAsync(
        string fixedLeafName,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken);

    void ReplaceFile(string sourceLeafName, string destinationLeafName);

    void DeleteFile(string fixedLeafName);
}

internal enum DesignerDraftProjectOpenMode
{
    OpenExisting,
    OpenOrCreate,
    CreateExclusive
}

internal interface IDesignerDraftStorageLeaseProvider
{
    IDesignerDraftProjectLease? OpenDesignerProject(
        string draftsRoot,
        Guid draftId,
        DesignerDraftProjectOpenMode mode);

    IDesignerSourceFileLease OpenDesignerSource(string absolutePath);
}

internal interface IDesignerDraftProjectLease : IDisposable
{
    bool WasCreated { get; }

    IDesignerDraftAssetsLease OpenAssets(bool create);

    void DeleteOwnedProjectIfEmpty();
}

internal interface IDesignerDraftAssetsLease : IDisposable
{
    bool FileExists(string canonicalLeafName);

    byte[] ReadAllBytes(string canonicalLeafName);

    void WriteAndFlushNew(
        string operationLeafName,
        ReadOnlySpan<byte> bytes,
        CancellationToken cancellationToken);

    byte[] ReadOperationBytes(string operationLeafName);

    bool MoveCanonicalToOperation(
        string canonicalLeafName,
        string operationLeafName);

    void MoveOperationToCanonical(
        string operationLeafName,
        string canonicalLeafName);

    void MoveOperationToImmutable(
        string operationLeafName,
        string contentAddressedLeafName) =>
        throw new NotSupportedException(
            "This draft assets lease does not support immutable promotion.");

    void DeleteCanonical(string canonicalLeafName);

    void DeleteOperation(string operationLeafName);

    void ReleaseOperation(string operationLeafName);

    void DeleteDirectoryIfEmpty();
}

internal interface IDesignerSourceFileLease : IDisposable
{
    long Length { get; }

    byte[] ReadAllBytes(CancellationToken cancellationToken);
}

internal sealed class PhysicalDraftFileOperations :
    IDraftFileOperations,
    IDraftStorageLeaseProvider,
    IDesignerDraftStorageLeaseProvider
{
    public static PhysicalDraftFileOperations Instance { get; } = new();

    private PhysicalDraftFileOperations()
    {
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);

    public IEnumerable<string> EnumerateDirectories(string path) =>
        Directory.EnumerateDirectories(path);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public async Task WriteAndFlushAsync(
        string path,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    public void ReplaceFile(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath, overwrite: true);

    public void DeleteFile(string path) => File.Delete(path);

    public IDraftCatalogLease? OpenCatalog(string draftsRoot, bool create) =>
        WindowsDraftCatalogLease.Open(draftsRoot, create);

    public IDesignerDraftProjectLease? OpenDesignerProject(
        string draftsRoot,
        Guid draftId,
        DesignerDraftProjectOpenMode mode) =>
        WindowsDesignerDraftProjectLease.Open(draftsRoot, draftId, mode);

    public IDesignerSourceFileLease OpenDesignerSource(string absolutePath) =>
        WindowsDesignerSourceFileLease.Open(absolutePath);
}

internal sealed class WindowsDraftCatalogLease : IDraftCatalogLease
{
    private readonly WindowsDraftDirectoryLease _localRoot;
    private readonly WindowsDraftDirectoryLease _settingsRoot;
    private readonly WindowsDraftDirectoryLease _designerRoot;
    private readonly WindowsDraftDirectoryLease _draftsRoot;

    private WindowsDraftCatalogLease(
        WindowsDraftDirectoryLease localRoot,
        WindowsDraftDirectoryLease settingsRoot,
        WindowsDraftDirectoryLease designerRoot,
        WindowsDraftDirectoryLease draftsRoot)
    {
        _localRoot = localRoot;
        _settingsRoot = settingsRoot;
        _designerRoot = designerRoot;
        _draftsRoot = draftsRoot;
    }

    internal static WindowsDraftCatalogLease? Open(
        string draftsRoot,
        bool create)
    {
        var shape = DraftStorageShape.Parse(draftsRoot);
        if (!create && !Directory.Exists(shape.DraftsRoot))
        {
            return null;
        }

        WindowsDraftDirectoryLease? local = null;
        WindowsDraftDirectoryLease? settings = null;
        WindowsDraftDirectoryLease? designer = null;
        WindowsDraftDirectoryLease? drafts = null;
        try
        {
            local = WindowsDraftDirectoryLease.Open(shape.LocalRoot);
            settings = local.OpenChildDirectory(
                "CodexQuotaHud",
                shape.SettingsRoot,
                create);
            if (settings is null)
            {
                return null;
            }

            designer = settings.OpenChildDirectory(
                "designer",
                shape.DesignerRoot,
                create);
            if (designer is null)
            {
                return null;
            }

            drafts = designer.OpenChildDirectory(
                "drafts",
                shape.DraftsRoot,
                create);
            if (drafts is null)
            {
                return null;
            }

            var result = new WindowsDraftCatalogLease(
                local,
                settings,
                designer,
                drafts);
            local = null;
            settings = null;
            designer = null;
            drafts = null;
            return result;
        }
        finally
        {
            drafts?.Dispose();
            designer?.Dispose();
            settings?.Dispose();
            local?.Dispose();
        }
    }

    public IReadOnlyList<string> EnumerateProjectNames() =>
        _draftsRoot.UseValidatedHandle(_ =>
            Directory.EnumerateDirectories(_draftsRoot.ExpectedPath)
                .Select(Path.GetFileName)
                .Where(name => name is not null)
                .Cast<string>()
                .ToArray());

    public IDraftProjectLease? OpenProject(Guid draftId, bool create)
    {
        if (draftId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(draftId));
        }

        var name = draftId.ToString("D").ToLowerInvariant();
        var expectedPath = Path.Combine(_draftsRoot.ExpectedPath, name);
        var project = _draftsRoot.OpenChildDirectory(name, expectedPath, create);
        return project is null ? null : new WindowsDraftProjectLease(project);
    }

    internal WindowsDraftProjectLease? TryCreateProjectExclusive(Guid draftId)
    {
        if (draftId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(draftId));
        }

        var name = draftId.ToString("D").ToLowerInvariant();
        var expectedPath = Path.Combine(_draftsRoot.ExpectedPath, name);
        var project = _draftsRoot.TryCreateChildDirectoryExclusive(
            name,
            expectedPath);
        return project is null ? null : new WindowsDraftProjectLease(project);
    }

    public void Dispose()
    {
        _draftsRoot.Dispose();
        _designerRoot.Dispose();
        _settingsRoot.Dispose();
        _localRoot.Dispose();
    }
}

internal sealed class WindowsDraftProjectLease : IDraftProjectLease
{
    private readonly WindowsDraftDirectoryLease _project;
    private readonly Dictionary<string, WindowsDraftFileLease> _temporaryFiles =
        new(StringComparer.Ordinal);
    private WindowsDraftDirectoryLease? _assets;

    internal WindowsDraftProjectLease(WindowsDraftDirectoryLease project) =>
        _project = project;

    public void EnsureAssetsDirectory() =>
        _assets ??= _project.OpenChildDirectory(
            "assets",
            Path.Combine(_project.ExpectedPath, "assets"),
            create: true) ?? throw new IOException(
                "The draft assets directory could not be leased.");

    internal WindowsDraftAssetsLease OpenDesignerAssets(bool create)
    {
        var expectedPath = Path.Combine(_project.ExpectedPath, "assets");
        var assets = create
            ? _project.TryCreateChildDirectoryExclusive("assets", expectedPath) ??
                _project.OpenChildDirectory("assets", expectedPath, create: false)
            : _project.OpenChildDirectory("assets", expectedPath, create: false);
        if (assets is null)
        {
            throw new DirectoryNotFoundException(
                "The draft assets directory does not exist.");
        }

        return new WindowsDraftAssetsLease(assets);
    }

    internal void DeleteOwnedProjectIfEmpty() => _project.DeleteIfEmpty();

    public bool FileExists(string fixedLeafName)
    {
        DraftStorageName.ValidateDocumentLeaf(fixedLeafName);
        if (_temporaryFiles.ContainsKey(fixedLeafName))
        {
            return true;
        }

        using var file = _project.OpenChildFile(fixedLeafName, create: false);
        return file is not null;
    }

    public byte[] ReadAllBytes(string fixedLeafName)
    {
        DraftStorageName.ValidateDocumentLeaf(fixedLeafName);
        if (_temporaryFiles.TryGetValue(fixedLeafName, out var temporary))
        {
            return temporary.ReadAllBytes();
        }

        using var file = _project.OpenChildFile(fixedLeafName, create: false) ??
            throw new FileNotFoundException(
                "The draft document does not exist.",
                fixedLeafName);
        return file.ReadAllBytes();
    }

    public Task WriteAndFlushAsync(
        string fixedLeafName,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        DraftStorageName.ValidateTemporaryLeaf(fixedLeafName);
        cancellationToken.ThrowIfCancellationRequested();
        var file = _project.OpenChildFile(fixedLeafName, create: true) ??
            throw new IOException("The draft temporary file could not be created.");
        try
        {
            file.WriteAndFlush(bytes.Span);
            cancellationToken.ThrowIfCancellationRequested();
            _temporaryFiles.Add(fixedLeafName, file);
            return Task.CompletedTask;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public void ReplaceFile(string sourceLeafName, string destinationLeafName)
    {
        DraftStorageName.ValidateTemporaryLeaf(sourceLeafName);
        DraftStorageName.ValidateTargetLeaf(destinationLeafName);
        if (!_temporaryFiles.TryGetValue(sourceLeafName, out var source))
        {
            throw new FileNotFoundException(
                "The draft temporary file does not exist.",
                sourceLeafName);
        }

        try
        {
            source.RenameTo(_project, destinationLeafName);
            _temporaryFiles.Remove(sourceLeafName);
            source.Dispose();
        }
        catch (DraftReplaceCommittedException)
        {
            _temporaryFiles.Remove(sourceLeafName);
            source.Dispose();
            throw;
        }
    }

    public void DeleteFile(string fixedLeafName)
    {
        DraftStorageName.ValidateDocumentLeaf(fixedLeafName);
        if (_temporaryFiles.Remove(fixedLeafName, out var temporary))
        {
            try
            {
                temporary.Delete();
            }
            finally
            {
                temporary.Dispose();
            }

            return;
        }

        using var file = _project.OpenChildFileForDelete(fixedLeafName);
        file?.Delete();
    }

    public void Dispose()
    {
        foreach (var temporary in _temporaryFiles.Values)
        {
            temporary.Dispose();
        }

        _temporaryFiles.Clear();
        _assets?.Dispose();
        _project.Dispose();
    }
}

internal sealed class WindowsDesignerDraftProjectLease :
    IDesignerDraftProjectLease
{
    private readonly WindowsDraftCatalogLease _catalog;
    private readonly WindowsDraftProjectLease _project;

    private WindowsDesignerDraftProjectLease(
        WindowsDraftCatalogLease catalog,
        WindowsDraftProjectLease project,
        bool wasCreated)
    {
        _catalog = catalog;
        _project = project;
        WasCreated = wasCreated;
    }

    public bool WasCreated { get; }

    internal static WindowsDesignerDraftProjectLease? Open(
        string draftsRoot,
        Guid draftId,
        DesignerDraftProjectOpenMode mode)
    {
        var catalog = WindowsDraftCatalogLease.Open(
            draftsRoot,
            create: mode != DesignerDraftProjectOpenMode.OpenExisting);
        if (catalog is null)
        {
            return null;
        }

        try
        {
            var project = mode == DesignerDraftProjectOpenMode.CreateExclusive
                ? catalog.TryCreateProjectExclusive(draftId)
                : catalog.OpenProject(
                    draftId,
                    create: mode == DesignerDraftProjectOpenMode.OpenOrCreate)
                    as WindowsDraftProjectLease;
            if (project is null)
            {
                catalog.Dispose();
                return null;
            }

            return new WindowsDesignerDraftProjectLease(
                catalog,
                project,
                wasCreated: mode == DesignerDraftProjectOpenMode.CreateExclusive);
        }
        catch
        {
            catalog.Dispose();
            throw;
        }
    }

    public IDesignerDraftAssetsLease OpenAssets(bool create) =>
        _project.OpenDesignerAssets(create);

    public void DeleteOwnedProjectIfEmpty()
    {
        if (!WasCreated)
        {
            throw new InvalidOperationException(
                "Only an exclusively claimed draft project can be deleted.");
        }

        _project.DeleteOwnedProjectIfEmpty();
    }

    public void Dispose()
    {
        _project.Dispose();
        _catalog.Dispose();
    }
}

internal sealed class WindowsDraftAssetsLease : IDesignerDraftAssetsLease
{
    private readonly WindowsDraftDirectoryLease _assets;
    private readonly Dictionary<string, WindowsDraftFileLease> _operations =
        new(StringComparer.Ordinal);

    internal WindowsDraftAssetsLease(WindowsDraftDirectoryLease assets) =>
        _assets = assets;

    public bool FileExists(string canonicalLeafName)
    {
        using var file = _assets.OpenReadableDesignerAssetFile(canonicalLeafName);
        return file is not null;
    }

    public byte[] ReadAllBytes(string canonicalLeafName)
    {
        using var file = _assets.OpenReadableDesignerAssetFile(
            canonicalLeafName) ?? throw new FileNotFoundException(
                "The draft asset does not exist.",
                canonicalLeafName);
        return file.ReadAllBytes();
    }

    public void WriteAndFlushNew(
        string operationLeafName,
        ReadOnlySpan<byte> bytes,
        CancellationToken cancellationToken)
    {
        DraftStorageName.ValidateAssetOperationLeaf(operationLeafName);
        cancellationToken.ThrowIfCancellationRequested();
        var file = _assets.OpenDesignerAssetFile(
            operationLeafName,
            create: true,
            deleteAccess: false) ?? throw new IOException(
                "The draft asset operation file could not be created.");
        try
        {
            file.WriteAndFlush(bytes);
            cancellationToken.ThrowIfCancellationRequested();
            _operations.Add(operationLeafName, file);
        }
        catch
        {
            try
            {
                file.Delete();
            }
            finally
            {
                file.Dispose();
            }

            throw;
        }
    }

    public byte[] ReadOperationBytes(string operationLeafName)
    {
        DraftStorageName.ValidateAssetOperationLeaf(operationLeafName);
        if (!_operations.TryGetValue(operationLeafName, out var file))
        {
            throw new FileNotFoundException(
                "The draft asset operation file does not exist.",
                operationLeafName);
        }

        return file.ReadAllBytes();
    }

    public bool MoveCanonicalToOperation(
        string canonicalLeafName,
        string operationLeafName)
    {
        DraftStorageName.ValidateAssetLeaf(canonicalLeafName);
        DraftStorageName.ValidateAssetOperationLeaf(operationLeafName);
        var file = _assets.OpenDesignerAssetFile(
            canonicalLeafName,
            create: false,
            deleteAccess: true);
        if (file is null)
        {
            return false;
        }

        try
        {
            file.RenameDesignerAssetTo(
                _assets,
                operationLeafName,
                operationTarget: true);
            _operations.Add(operationLeafName, file);
            return true;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    public void MoveOperationToCanonical(
        string operationLeafName,
        string canonicalLeafName)
    {
        DraftStorageName.ValidateAssetOperationLeaf(operationLeafName);
        DraftStorageName.ValidateAssetLeaf(canonicalLeafName);
        if (!_operations.TryGetValue(operationLeafName, out var file))
        {
            throw new FileNotFoundException(
                "The draft asset operation file does not exist.",
                operationLeafName);
        }

        file.RenameDesignerAssetTo(
            _assets,
            canonicalLeafName,
            operationTarget: false);
    }

    public void MoveOperationToImmutable(
        string operationLeafName,
        string contentAddressedLeafName)
    {
        DraftStorageName.ValidateAssetOperationLeaf(operationLeafName);
        DraftStorageName.ValidateImmutableAssetLeaf(contentAddressedLeafName);
        if (!_operations.TryGetValue(operationLeafName, out var file))
        {
            throw new FileNotFoundException(
                "The draft asset operation file does not exist.",
                operationLeafName);
        }

        file.RenameDesignerImmutableAssetTo(
            _assets,
            contentAddressedLeafName);
    }

    public void DeleteCanonical(string canonicalLeafName)
    {
        DraftStorageName.ValidateAssetLeaf(canonicalLeafName);
        using var file = _assets.OpenDesignerAssetFile(
            canonicalLeafName,
            create: false,
            deleteAccess: true);
        file?.Delete();
    }

    public void DeleteOperation(string operationLeafName)
    {
        DraftStorageName.ValidateAssetOperationLeaf(operationLeafName);
        if (_operations.Remove(operationLeafName, out var tracked))
        {
            try
            {
                tracked.Delete();
            }
            finally
            {
                tracked.Dispose();
            }

            return;
        }

        using var file = _assets.OpenDesignerOperationFileForDelete(
            operationLeafName);
        file?.Delete();
    }

    public void ReleaseOperation(string operationLeafName)
    {
        DraftStorageName.ValidateAssetOperationLeaf(operationLeafName);
        if (_operations.Remove(operationLeafName, out var tracked))
        {
            tracked.Dispose();
        }
    }

    public void DeleteDirectoryIfEmpty() => _assets.DeleteIfEmpty();

    public void Dispose()
    {
        foreach (var operation in _operations.Values)
        {
            operation.Dispose();
        }

        _operations.Clear();
        _assets.Dispose();
    }
}

internal sealed class WindowsDesignerSourceFileLease : IDesignerSourceFileLease
{
    private readonly FileStream _stream;

    private WindowsDesignerSourceFileLease(FileStream stream) => _stream = stream;

    public long Length => _stream.Length;

    internal static WindowsDesignerSourceFileLease Open(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        if (!Path.IsPathFullyQualified(absolutePath))
        {
            throw new ArgumentException(
                "The Designer source path must be absolute.",
                nameof(absolutePath));
        }

        var fullPath = Path.GetFullPath(absolutePath);
        var handle = DraftNative.OpenAbsoluteSourceFile(fullPath);
        try
        {
            _ = DraftNative.ValidateFile(handle, fullPath);
            return new WindowsDesignerSourceFileLease(
                new FileStream(handle, FileAccess.Read));
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public byte[] ReadAllBytes(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_stream.Length > int.MaxValue)
        {
            throw new InvalidDataException("The Designer source file is too large.");
        }

        var content = new byte[checked((int)_stream.Length)];
        _stream.Position = 0;
        _stream.ReadExactly(content);
        cancellationToken.ThrowIfCancellationRequested();
        return content;
    }

    public void Dispose() => _stream.Dispose();
}

internal sealed class WindowsDraftDirectoryLease : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly DraftDirectoryIdentity _identity;

    private WindowsDraftDirectoryLease(
        SafeFileHandle handle,
        string expectedPath,
        DraftDirectoryIdentity identity)
    {
        _handle = handle;
        ExpectedPath = expectedPath;
        _identity = identity;
    }

    internal string ExpectedPath { get; }

    internal static WindowsDraftDirectoryLease Open(string expectedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPath);
        var fullPath = Path.GetFullPath(expectedPath);
        var handle = DraftNative.OpenAbsoluteDirectory(fullPath);
        try
        {
            var identity = DraftNative.ValidateDirectory(handle, fullPath, null);
            return new WindowsDraftDirectoryLease(handle, fullPath, identity);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal WindowsDraftDirectoryLease? OpenChildDirectory(
        string fixedName,
        string expectedPath,
        bool create)
    {
        DraftStorageName.ValidateExpectedChild(ExpectedPath, fixedName, expectedPath);
        if (!create && !Directory.Exists(expectedPath))
        {
            return null;
        }

        var handle = UseValidatedHandle(parent =>
            DraftNative.OpenRelativeDirectory(parent, fixedName, create));
        try
        {
            var identity = DraftNative.ValidateDirectory(handle, expectedPath, null);
            if (identity.VolumeSerialNumber != _identity.VolumeSerialNumber)
            {
                throw new DraftUnsafePathException(
                    "The draft directory moved to a different volume.");
            }

            return new WindowsDraftDirectoryLease(
                handle,
                Path.GetFullPath(expectedPath),
                identity);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal WindowsDraftDirectoryLease? TryCreateChildDirectoryExclusive(
        string fixedName,
        string expectedPath)
    {
        DraftStorageName.ValidateExpectedChild(ExpectedPath, fixedName, expectedPath);
        var handle = UseValidatedHandle(parent =>
            DraftNative.TryCreateRelativeDirectory(parent, fixedName));
        if (handle is null)
        {
            return null;
        }

        try
        {
            var identity = DraftNative.ValidateDirectory(handle, expectedPath, null);
            if (identity.VolumeSerialNumber != _identity.VolumeSerialNumber)
            {
                throw new DraftUnsafePathException(
                    "The draft directory moved to a different volume.");
            }

            return new WindowsDraftDirectoryLease(
                handle,
                Path.GetFullPath(expectedPath),
                identity);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal WindowsDraftFileLease? OpenChildFile(string fixedName, bool create)
    {
        DraftStorageName.ValidateDocumentLeaf(fixedName);
        var expectedPath = Path.Combine(ExpectedPath, fixedName);
        if (!create && !File.Exists(expectedPath))
        {
            return null;
        }

        var handle = UseValidatedHandle(parent =>
            DraftNative.OpenRelativeFile(parent, fixedName, create));
        try
        {
            var identity = DraftNative.ValidateFile(handle, expectedPath);
            if (identity.VolumeSerialNumber != _identity.VolumeSerialNumber)
            {
                throw new DraftUnsafePathException(
                    "The draft file moved to a different volume.");
            }

            return new WindowsDraftFileLease(
                handle,
                Path.GetFullPath(expectedPath),
                create ? FileAccess.ReadWrite : FileAccess.Read);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal WindowsDraftFileLease? OpenChildFileForDelete(string fixedName)
    {
        DraftStorageName.ValidateDocumentLeaf(fixedName);
        var expectedPath = Path.Combine(ExpectedPath, fixedName);
        if (!File.Exists(expectedPath))
        {
            return null;
        }

        var handle = UseValidatedHandle(parent =>
            DraftNative.OpenRelativeFileForDelete(parent, fixedName));
        try
        {
            var identity = DraftNative.ValidateFile(handle, expectedPath);
            if (identity.VolumeSerialNumber != _identity.VolumeSerialNumber)
            {
                throw new DraftUnsafePathException(
                    "The draft file moved to a different volume.");
            }

            return new WindowsDraftFileLease(
                handle,
                Path.GetFullPath(expectedPath),
                FileAccess.Read);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal WindowsDraftFileLease? OpenDesignerAssetFile(
        string fixedName,
        bool create,
        bool deleteAccess)
    {
        if (create)
        {
            DraftStorageName.ValidateAssetOperationLeaf(fixedName);
        }
        else
        {
            DraftStorageName.ValidateAssetLeaf(fixedName);
        }

        var expectedPath = Path.Combine(ExpectedPath, fixedName);
        if (!create && !File.Exists(expectedPath))
        {
            return null;
        }

        var handle = UseValidatedHandle(parent =>
            deleteAccess
                ? DraftNative.OpenRelativeFileForDelete(parent, fixedName)
                : DraftNative.OpenRelativeFile(parent, fixedName, create));
        try
        {
            var identity = DraftNative.ValidateFile(handle, expectedPath);
            if (identity.VolumeSerialNumber != _identity.VolumeSerialNumber)
            {
                throw new DraftUnsafePathException(
                    "The draft asset moved to a different volume.");
            }

            return new WindowsDraftFileLease(
                handle,
                Path.GetFullPath(expectedPath),
                create ? FileAccess.ReadWrite : FileAccess.Read);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal WindowsDraftFileLease? OpenReadableDesignerAssetFile(string leafName)
    {
        DraftStorageName.ValidateReadableAssetLeaf(leafName);
        var expectedPath = Path.Combine(ExpectedPath, leafName);
        var handle = UseValidatedHandle(parent =>
            DraftNative.TryOpenRelativeFileReadOnly(parent, leafName));
        if (handle is null)
        {
            return null;
        }

        try
        {
            var identity = DraftNative.ValidateFile(handle, expectedPath);
            if (identity.VolumeSerialNumber != _identity.VolumeSerialNumber)
            {
                throw new DraftUnsafePathException(
                    "The readable draft asset moved to a different volume.");
            }

            return new WindowsDraftFileLease(
                handle,
                Path.GetFullPath(expectedPath),
                FileAccess.Read);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal WindowsDraftFileLease? OpenDesignerOperationFileForDelete(
        string operationLeafName)
    {
        DraftStorageName.ValidateAssetOperationLeaf(operationLeafName);
        var expectedPath = Path.Combine(ExpectedPath, operationLeafName);
        if (!File.Exists(expectedPath))
        {
            return null;
        }

        var handle = UseValidatedHandle(parent =>
            DraftNative.OpenRelativeFileForDelete(parent, operationLeafName));
        try
        {
            _ = DraftNative.ValidateFile(handle, expectedPath);
            return new WindowsDraftFileLease(
                handle,
                Path.GetFullPath(expectedPath),
                FileAccess.Read);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal void DeleteIfEmpty()
    {
        UseValidatedHandle(_ =>
        {
            if (Directory.EnumerateFileSystemEntries(ExpectedPath).Any())
            {
                throw new IOException("The owned draft directory is not empty.");
            }

            DraftNative.DeleteFile(_handle);
            return true;
        });
    }

    internal TResult UseValidatedHandle<TResult>(Func<IntPtr, TResult> action)
    {
        var addedReference = false;
        try
        {
            _handle.DangerousAddRef(ref addedReference);
            _ = DraftNative.ValidateDirectory(_handle, ExpectedPath, _identity);
            return action(_handle.DangerousGetHandle());
        }
        finally
        {
            if (addedReference)
            {
                _handle.DangerousRelease();
            }
        }
    }

    public void Dispose() => _handle.Dispose();
}

internal sealed class WindowsDraftFileLease : IDisposable
{
    private readonly FileStream _stream;
    private string _expectedPath;

    internal WindowsDraftFileLease(
        SafeFileHandle handle,
        string expectedPath,
        FileAccess access)
    {
        _expectedPath = expectedPath;
        _stream = new FileStream(handle, access);
    }

    internal void WriteAndFlush(ReadOnlySpan<byte> bytes)
    {
        _stream.Position = 0;
        _stream.SetLength(0);
        _stream.Write(bytes);
        _stream.Flush(flushToDisk: true);
    }

    internal byte[] ReadAllBytes()
    {
        _ = DraftNative.ValidateFile(_stream.SafeFileHandle, _expectedPath);
        if (_stream.Length > int.MaxValue)
        {
            throw new InvalidDataException("The draft document is too large to read.");
        }

        var bytes = new byte[checked((int)_stream.Length)];
        _stream.Position = 0;
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = _stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0)
            {
                throw new EndOfStreamException(
                    "The draft document ended before its declared length.");
            }

            offset += read;
        }

        return bytes;
    }

    internal void RenameTo(
        WindowsDraftDirectoryLease destinationParent,
        string destinationLeafName)
    {
        DraftStorageName.ValidateTargetLeaf(destinationLeafName);
        RenameToCore(
            destinationParent,
            destinationLeafName,
            replaceIfExists: true);
    }

    internal void RenameDesignerAssetTo(
        WindowsDraftDirectoryLease destinationParent,
        string destinationLeafName,
        bool operationTarget)
    {
        if (operationTarget)
        {
            DraftStorageName.ValidateAssetOperationLeaf(destinationLeafName);
        }
        else
        {
            DraftStorageName.ValidateAssetLeaf(destinationLeafName);
        }

        RenameToCore(
            destinationParent,
            destinationLeafName,
            replaceIfExists: true);
    }

    internal void RenameDesignerImmutableAssetTo(
        WindowsDraftDirectoryLease destinationParent,
        string destinationLeafName)
    {
        DraftStorageName.ValidateImmutableAssetLeaf(destinationLeafName);
        RenameToCore(
            destinationParent,
            destinationLeafName,
            replaceIfExists: false);
    }

    private void RenameToCore(
        WindowsDraftDirectoryLease destinationParent,
        string destinationLeafName,
        bool replaceIfExists)
    {
        var destinationPath = Path.Combine(
            destinationParent.ExpectedPath,
            destinationLeafName);
        _ = DraftNative.ValidateFile(_stream.SafeFileHandle, _expectedPath);
        destinationParent.UseValidatedHandle(parentHandle =>
        {
            DraftNative.RenameFile(
                _stream.SafeFileHandle,
                parentHandle,
                destinationLeafName,
                replaceIfExists);
            return true;
        });
        _expectedPath = destinationPath;
        try
        {
            _ = DraftNative.ValidateFile(_stream.SafeFileHandle, destinationPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or OverflowException)
        {
            throw new DraftReplaceCommittedException(
                "The draft replace committed but its identity post-check failed.",
                exception);
        }
    }

    internal void Delete()
    {
        _ = DraftNative.ValidateFile(_stream.SafeFileHandle, _expectedPath);
        DraftNative.DeleteFile(_stream.SafeFileHandle);
    }

    public void Dispose() => _stream.Dispose();
}

internal sealed class DraftUnsafePathException : IOException
{
    internal DraftUnsafePathException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

internal sealed class DraftReplaceCommittedException : IOException
{
    internal DraftReplaceCommittedException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

internal readonly record struct DraftDirectoryIdentity(
    uint VolumeSerialNumber,
    ulong FileIndex);

internal sealed record DraftStorageShape(
    string LocalRoot,
    string SettingsRoot,
    string DesignerRoot,
    string DraftsRoot)
{
    internal static DraftStorageShape Parse(string draftsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftsRoot);
        var drafts = Path.TrimEndingDirectorySeparator(Path.GetFullPath(draftsRoot));
        var designer = Path.GetDirectoryName(drafts);
        var settings = designer is null ? null : Path.GetDirectoryName(designer);
        var local = settings is null ? null : Path.GetDirectoryName(settings);
        if (designer is null || settings is null || string.IsNullOrEmpty(local) ||
            !string.Equals(Path.GetFileName(drafts), "drafts", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(designer), "designer", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(settings), "CodexQuotaHud", StringComparison.OrdinalIgnoreCase))
        {
            throw new DraftUnsafePathException(
                "The drafts root does not match the owned storage shape.");
        }

        return new DraftStorageShape(local, settings, designer, drafts);
    }
}

internal static class DraftStorageName
{
    internal static void ValidateExpectedChild(
        string parentPath,
        string fixedName,
        string expectedPath)
    {
        ValidateSegment(fixedName);
        if (!string.Equals(
                Path.GetFullPath(expectedPath),
                Path.Combine(parentPath, fixedName),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new DraftUnsafePathException(
                "The draft child path does not match its leased parent.");
        }
    }

    internal static void ValidateDocumentLeaf(string leafName)
    {
        if (leafName is "draft.json" or "recovery.json")
        {
            return;
        }

        ValidateTemporaryLeaf(leafName);
    }

    internal static void ValidateTargetLeaf(string leafName)
    {
        if (leafName is not ("draft.json" or "recovery.json"))
        {
            throw new DraftUnsafePathException(
                "The draft target leaf name is not fixed by the schema.");
        }
    }

    internal static void ValidateTemporaryLeaf(string leafName)
    {
        ValidateSegment(leafName);
        var target = leafName.StartsWith(".draft.json.tmp-", StringComparison.Ordinal)
            ? "draft.json"
            : leafName.StartsWith(".recovery.json.tmp-", StringComparison.Ordinal)
                ? "recovery.json"
                : null;
        if (target is null)
        {
            throw new DraftUnsafePathException(
                "The draft temporary leaf name is invalid.");
        }

        var prefix = $".{target}.tmp-";
        var operationText = leafName[prefix.Length..];
        if (!Guid.TryParseExact(operationText, "D", out var operationId) ||
            !string.Equals(
                operationText,
                operationId.ToString("D").ToLowerInvariant(),
                StringComparison.Ordinal))
        {
            throw new DraftUnsafePathException(
                "The draft temporary operation ID is invalid.");
        }
    }

    internal static void ValidateAssetLeaf(string leafName)
    {
        if (!IsFixedAssetLeaf(leafName))
        {
            throw new DraftUnsafePathException(
                "The draft asset leaf name is not fixed by the schema.");
        }
    }

    internal static void ValidateReadableAssetLeaf(string leafName)
    {
        ValidateSegment(leafName);
        if (!IsFixedAssetLeaf(leafName) &&
            !DraftAssetStorage.IsValidContentLeaf(leafName))
        {
            throw new DraftUnsafePathException(
                "The readable draft asset leaf name is invalid.");
        }
    }

    internal static void ValidateAssetOperationLeaf(string leafName)
    {
        ValidateSegment(leafName);
        foreach (var canonical in new[]
                 {
                     "background.png", "background.jpg", "center.png",
                     "center.jpg", "decoration.png"
                 })
        {
            foreach (var kind in new[] { "tmp", "tomb", "rollback", "discard" })
            {
                var prefix = $".{canonical}.{kind}-";
                if (!leafName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var operationText = leafName[prefix.Length..];
                if (Guid.TryParseExact(operationText, "D", out var operationId) &&
                    string.Equals(
                        operationText,
                        operationId.ToString("D").ToLowerInvariant(),
                        StringComparison.Ordinal))
                {
                    return;
                }
            }
        }

        throw new DraftUnsafePathException(
            "The draft asset operation leaf name is invalid.");
    }

    internal static void ValidateImmutableAssetLeaf(string leafName)
    {
        ValidateSegment(leafName);
        if (!DraftAssetStorage.IsValidContentLeaf(leafName))
        {
            throw new DraftUnsafePathException(
                "The immutable draft asset leaf name is invalid.");
        }
    }

    private static bool IsFixedAssetLeaf(string leafName) => leafName is
        "background.png" or "background.jpg" or
        "center.png" or "center.jpg" or "decoration.png";

    private static void ValidateSegment(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is "." or ".." ||
            Path.IsPathRooted(name) ||
            name.IndexOfAny(['/', '\\', ':', '\0']) >= 0 ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
        {
            throw new DraftUnsafePathException(
                "The draft storage name must be one fixed path segment.");
        }
    }
}

internal static class DraftNative
{
    private const uint DeleteAccess = 0x00010000;
    private const uint Synchronize = 0x00100000;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileListDirectory = 0x00000001;
    private const uint FileAddFile = 0x00000002;
    private const uint FileAddSubdirectory = 0x00000004;
    private const uint FileTraverse = 0x00000020;
    private const uint FileReadAttributes = 0x00000080;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint FileOpen = 1;
    private const uint FileCreate = 2;
    private const uint FileOpenIf = 3;
    private const uint OpenExisting = 3;
    private const uint FileDirectoryFile = 0x00000001;
    private const uint FileSynchronousIoNonAlert = 0x00000020;
    private const uint FileNonDirectoryFile = 0x00000040;
    private const uint FileOpenReparsePoint = 0x00200000;
    private const uint ObjCaseInsensitive = 0x00000040;
    private const int FileRenameInformation = 10;
    private const int FileDispositionInfo = 4;

    internal static SafeFileHandle OpenAbsoluteDirectory(string fullPath)
    {
        var handle = CreateFileW(
            fullPath,
            FileListDirectory | FileAddFile | FileAddSubdirectory |
                FileTraverse | FileReadAttributes,
            FileShare.Read | FileShare.Write,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint | FileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var exception = Win32Error("The draft directory lease could not be opened.");
            handle.Dispose();
            throw exception;
        }

        return handle;
    }

    internal static SafeFileHandle OpenAbsoluteSourceFile(string fullPath)
    {
        var handle = CreateFileW(
            fullPath,
            GenericRead | FileReadAttributes,
            FileShare.Read,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var exception = Win32Error(
                "The Designer source file lease could not be opened.");
            handle.Dispose();
            throw exception;
        }

        return handle;
    }

    internal static SafeFileHandle OpenRelativeDirectory(
        IntPtr parentHandle,
        string name,
        bool create) =>
        OpenRelative(
            parentHandle,
            name,
            FileListDirectory | FileAddFile | FileAddSubdirectory |
                FileTraverse | FileReadAttributes | Synchronize,
            FileShareRead | FileShareWrite,
            create ? FileOpenIf : FileOpen,
            FileDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint,
            fileAttributes: 0)!;

    internal static SafeFileHandle? TryCreateRelativeDirectory(
        IntPtr parentHandle,
        string name) =>
        OpenRelative(
            parentHandle,
            name,
            FileListDirectory | FileAddFile | FileAddSubdirectory |
                FileTraverse | FileReadAttributes | DeleteAccess | Synchronize,
            FileShareRead | FileShareWrite,
            FileCreate,
            FileDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint,
            fileAttributes: 0,
            nullOnNameCollision: true);

    internal static SafeFileHandle OpenRelativeFile(
        IntPtr parentHandle,
        string name,
        bool create) =>
        OpenRelative(
            parentHandle,
            name,
            GenericRead | (create ? GenericWrite | DeleteAccess : 0) |
                FileReadAttributes | Synchronize,
            create
                ? FileShareRead
                : FileShareRead | FileShareWrite | FileShareDelete,
            create ? FileCreate : FileOpen,
            FileNonDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint,
            FileAttributeNormal)!;

    internal static SafeFileHandle? TryOpenRelativeFileReadOnly(
        IntPtr parentHandle,
        string name) =>
        OpenRelative(
            parentHandle,
            name,
            GenericRead | FileReadAttributes | Synchronize,
            FileShareRead | FileShareWrite | FileShareDelete,
            FileOpen,
            FileNonDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint,
            FileAttributeNormal,
            nullOnNameNotFound: true);

    internal static SafeFileHandle OpenRelativeFileForDelete(
        IntPtr parentHandle,
        string name) =>
        OpenRelative(
            parentHandle,
            name,
            GenericRead | FileReadAttributes | DeleteAccess | Synchronize,
            FileShareRead,
            FileOpen,
            FileNonDirectoryFile | FileSynchronousIoNonAlert | FileOpenReparsePoint,
            FileAttributeNormal)!;

    internal static DraftDirectoryIdentity ValidateDirectory(
        SafeFileHandle handle,
        string expectedPath,
        DraftDirectoryIdentity? expectedIdentity)
    {
        var information = ReadInformation(handle, "draft directory");
        if ((information.FileAttributes & FileAttributeDirectory) == 0 ||
            (information.FileAttributes & FileAttributeReparsePoint) != 0)
        {
            throw new DraftUnsafePathException(
                "The draft directory lease target is a reparse point.");
        }

        ValidateFinalPath(handle, expectedPath, "draft directory");
        var identity = Identity(information);
        if (expectedIdentity is { } expected && identity != expected)
        {
            throw new DraftUnsafePathException(
                "The draft directory lease target changed identity.");
        }

        return identity;
    }

    internal static DraftDirectoryIdentity ValidateFile(
        SafeFileHandle handle,
        string expectedPath)
    {
        var information = ReadInformation(handle, "draft file");
        if ((information.FileAttributes &
                (FileAttributeDirectory | FileAttributeReparsePoint)) != 0)
        {
            throw new DraftUnsafePathException(
                "The draft file target is a directory or reparse point.");
        }

        ValidateFinalPath(handle, expectedPath, "draft file");
        return Identity(information);
    }

    internal static void RenameFile(
        SafeFileHandle fileHandle,
        IntPtr destinationParentHandle,
        string destinationLeafName,
        bool replaceIfExists)
    {
        var nameBytes = Encoding.Unicode.GetBytes(destinationLeafName);
        var rootOffset = IntPtr.Size == 8 ? 8 : 4;
        var lengthOffset = rootOffset + IntPtr.Size;
        var nameOffset = lengthOffset + sizeof(uint);
        var headerSize = IntPtr.Size == 8 ? 24 : 16;
        var bufferSize = checked(headerSize + nameBytes.Length);
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            for (var index = 0; index < bufferSize; index++)
            {
                Marshal.WriteByte(buffer, index, 0);
            }

            Marshal.WriteByte(buffer, 0, replaceIfExists ? (byte)1 : (byte)0);
            Marshal.WriteIntPtr(buffer, rootOffset, destinationParentHandle);
            Marshal.WriteInt32(buffer, lengthOffset, nameBytes.Length);
            Marshal.Copy(nameBytes, 0, buffer + nameOffset, nameBytes.Length);
            var status = NtSetInformationFile(
                fileHandle,
                out _,
                buffer,
                checked((uint)bufferSize),
                FileRenameInformation);
            if (status < 0)
            {
                throw NtStatusError("The draft atomic replace failed.", status);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static void DeleteFile(SafeFileHandle fileHandle)
    {
        var disposition = new FileDispositionInformation { DeleteFile = true };
        if (!SetFileInformationByHandle(
                fileHandle,
                FileDispositionInfo,
                ref disposition,
                checked((uint)Marshal.SizeOf<FileDispositionInformation>())))
        {
            throw Win32Error("The draft temporary file could not be deleted.");
        }
    }

    private static SafeFileHandle? OpenRelative(
        IntPtr parentHandle,
        string name,
        uint desiredAccess,
        uint shareAccess,
        uint createDisposition,
        uint createOptions,
        uint fileAttributes,
        bool nullOnNameCollision = false,
        bool nullOnNameNotFound = false)
    {
        var nameBuffer = Marshal.StringToHGlobalUni(name);
        var unicodeStringPointer = IntPtr.Zero;
        var rawHandle = IntPtr.Zero;
        try
        {
            var nameLength = checked((ushort)(name.Length * sizeof(char)));
            var unicodeString = new UnicodeString
            {
                Length = nameLength,
                MaximumLength = checked((ushort)(nameLength + sizeof(char))),
                Buffer = nameBuffer
            };
            unicodeStringPointer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            Marshal.StructureToPtr(unicodeString, unicodeStringPointer, false);
            var attributes = new ObjectAttributes
            {
                Length = checked((uint)Marshal.SizeOf<ObjectAttributes>()),
                RootDirectory = parentHandle,
                ObjectName = unicodeStringPointer,
                Attributes = ObjCaseInsensitive
            };
            var status = NtCreateFile(
                out rawHandle,
                desiredAccess,
                ref attributes,
                out _,
                IntPtr.Zero,
                fileAttributes,
                shareAccess,
                createDisposition,
                createOptions,
                IntPtr.Zero,
                0);
            if (status < 0)
            {
                if (rawHandle != IntPtr.Zero && rawHandle != new IntPtr(-1))
                {
                    new SafeFileHandle(rawHandle, ownsHandle: true).Dispose();
                    rawHandle = IntPtr.Zero;
                }

                if (nullOnNameCollision && status == unchecked((int)0xC0000035))
                {
                    return null!;
                }

                if (nullOnNameNotFound && status is
                    unchecked((int)0xC0000034) or unchecked((int)0xC000003A))
                {
                    return null;
                }

                throw NtStatusError(
                    "The handle-relative draft object could not be opened.",
                    status);
            }

            var result = new SafeFileHandle(rawHandle, ownsHandle: true);
            rawHandle = IntPtr.Zero;
            if (result.IsInvalid)
            {
                result.Dispose();
                throw new IOException(
                    "The handle-relative draft object returned an invalid handle.");
            }

            return result;
        }
        finally
        {
            if (rawHandle != IntPtr.Zero && rawHandle != new IntPtr(-1))
            {
                new SafeFileHandle(rawHandle, ownsHandle: true).Dispose();
            }

            if (unicodeStringPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(unicodeStringPointer);
            }

            Marshal.FreeHGlobal(nameBuffer);
        }
    }

    private static ByHandleFileInformation ReadInformation(
        SafeFileHandle handle,
        string kind)
    {
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var information))
        {
            throw Win32Error($"The {kind} identity could not be read.");
        }

        return information;
    }

    private static DraftDirectoryIdentity Identity(ByHandleFileInformation information) =>
        new(
            information.VolumeSerialNumber,
            ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow);

    private static void ValidateFinalPath(
        SafeFileHandle handle,
        string expectedPath,
        string kind)
    {
        var actual = RemoveExtendedPathPrefix(GetFinalPath(handle));
        if (!string.Equals(
                actual,
                Path.GetFullPath(expectedPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new DraftUnsafePathException(
                $"The {kind} lease target changed path.");
        }
    }

    private static string GetFinalPath(SafeFileHandle handle)
    {
        var buffer = new char[512];
        while (true)
        {
            var length = GetFinalPathNameByHandleW(
                handle,
                buffer,
                checked((uint)buffer.Length),
                0);
            if (length == 0)
            {
                throw Win32Error("The draft lease path could not be read.");
            }

            if (length < buffer.Length)
            {
                return new string(buffer, 0, checked((int)length));
            }

            buffer = new char[checked((int)length + 1)];
        }
    }

    private static string RemoveExtendedPathPrefix(string path)
    {
        const string uncPrefix = @"\\?\UNC\";
        const string localPrefix = @"\\?\";
        if (path.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + path[uncPrefix.Length..];
        }

        return path.StartsWith(localPrefix, StringComparison.OrdinalIgnoreCase)
            ? path[localPrefix.Length..]
            : path;
    }

    private static IOException Win32Error(string message)
    {
        var error = Marshal.GetLastWin32Error();
        return new IOException(
            $"{message} Win32 error {error}: {new Win32Exception(error).Message}");
    }

    private static IOException NtStatusError(string message, int status)
    {
        var error = RtlNtStatusToDosError(status);
        return new IOException(
            $"{message} NTSTATUS 0x{status:X8}, Win32 error {error}: " +
            new Win32Exception(checked((int)error)).Message);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation fileInformation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle file,
        [Out] char[] filePath,
        uint filePathLength,
        uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle file,
        int fileInformationClass,
        ref FileDispositionInformation fileInformation,
        uint bufferSize);

    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(
        out IntPtr fileHandle,
        uint desiredAccess,
        ref ObjectAttributes objectAttributes,
        out IoStatusBlock ioStatusBlock,
        IntPtr allocationSize,
        uint fileAttributes,
        uint shareAccess,
        uint createDisposition,
        uint createOptions,
        IntPtr eaBuffer,
        uint eaLength);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationFile(
        SafeFileHandle file,
        out IoStatusBlock ioStatusBlock,
        IntPtr fileInformation,
        uint length,
        int fileInformationClass);

    [DllImport("ntdll.dll")]
    private static extern uint RtlNtStatusToDosError(int status);

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        public uint Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        public IntPtr Status;
        public UIntPtr Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public FILETIME CreationTime;
        public FILETIME LastAccessTime;
        public FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInformation
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool DeleteFile;
    }
}

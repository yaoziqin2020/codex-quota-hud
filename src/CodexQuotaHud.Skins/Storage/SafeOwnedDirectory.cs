using System.IO;

namespace CodexQuotaHud.Skins.Storage;

internal interface ISkinFileSystem
{
    bool DirectoryExists(string path);

    bool FileExists(string path);

    FileAttributes GetAttributes(string path);

    IReadOnlyList<string> EnumerateDirectories(string path);

    IReadOnlyList<string> EnumerateFiles(string path, SearchOption searchOption);

    byte[] ReadAllBytes(string path, long maximumBytes);

    void CreateDirectory(string path);

    void WriteAllBytesAndFlush(string path, ReadOnlySpan<byte> content);

    void MoveDirectory(string sourcePath, string destinationPath);

    void DeleteDirectory(string path, bool recursive);
}

internal sealed class PhysicalSkinFileSystem : ISkinFileSystem
{
    public static PhysicalSkinFileSystem Instance { get; } = new();

    private PhysicalSkinFileSystem()
    {
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);

    public IReadOnlyList<string> EnumerateDirectories(string path) =>
        Directory.EnumerateDirectories(path).ToArray();

    public IReadOnlyList<string> EnumerateFiles(
        string path,
        SearchOption searchOption) =>
        Directory.EnumerateFiles(path, "*", searchOption).ToArray();

    public byte[] ReadAllBytes(string path, long maximumBytes)
    {
        using var source = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        return BoundedSkinFileReader.Read(source, maximumBytes);
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void WriteAllBytesAndFlush(string path, ReadOnlySpan<byte> content)
    {
        using var destination = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        destination.Write(content);
        destination.Flush(flushToDisk: true);
    }

    public void MoveDirectory(string sourcePath, string destinationPath) =>
        Directory.Move(sourcePath, destinationPath);

    public void DeleteDirectory(string path, bool recursive) =>
        Directory.Delete(path, recursive);
}

internal static class BoundedSkinFileReader
{
    private const int BufferSize = 64 * 1024;

    public static byte[] Read(Stream source, long maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        if (!source.CanRead)
        {
            throw new ArgumentException(
                "The source stream must be readable.",
                nameof(source));
        }

        using var destination = new MemoryStream();
        var buffer = new byte[BufferSize];
        long totalBytes = 0;
        while (true)
        {
            var allowedRead = (int)Math.Min(
                buffer.Length,
                maximumBytes - totalBytes + 1);
            var read = source.Read(buffer, 0, allowedRead);
            if (read == 0)
            {
                return destination.ToArray();
            }

            totalBytes = checked(totalBytes + read);
            if (totalBytes > maximumBytes)
            {
                throw new InvalidDataException(
                    "The installed skin file exceeds its size limit.");
            }

            destination.Write(buffer, 0, read);
        }
    }
}

internal sealed class SafeOwnedDirectory
{
    private readonly ISkinFileSystem _fileSystem;

    public SafeOwnedDirectory(string rootPath, ISkinFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(fileSystem);
        RootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        _fileSystem = fileSystem;
    }

    public string RootPath { get; }

    public static bool IsSafeStoragePath(
        SkinStoragePaths paths,
        string candidatePath,
        ISkinFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidatePath);
        ArgumentNullException.ThrowIfNull(fileSystem);

        try
        {
            var localAppDataRoot = Path.GetDirectoryName(paths.SettingsRoot);
            if (string.IsNullOrEmpty(localAppDataRoot))
            {
                return false;
            }

            var ownedLocalRoot = new SafeOwnedDirectory(
                localAppDataRoot,
                fileSystem);
            return !ownedLocalRoot.HasExistingReparsePoint(candidatePath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    public bool TryResolveSkinDirectory(
        string candidatePath,
        out string resolvedPath,
        out Guid skinId)
    {
        resolvedPath = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(candidatePath));
        skinId = default;
        var prefix = RootPath + Path.DirectorySeparatorChar;
        if (!resolvedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = Path.GetRelativePath(RootPath, resolvedPath);
        if (relative.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
            relative.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
        {
            return false;
        }

        var name = Path.GetFileName(resolvedPath);
        if (!Guid.TryParseExact(name, "D", out skinId) ||
            !string.Equals(
                name,
                skinId.ToString("D").ToLowerInvariant(),
                StringComparison.Ordinal))
        {
            skinId = default;
            return false;
        }

        return !HasExistingReparsePoint(resolvedPath);
    }

    public bool HasExistingReparsePoint(string candidatePath)
    {
        var resolved = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(candidatePath));
        var prefix = RootPath + Path.DirectorySeparatorChar;
        if (!string.Equals(resolved, RootPath, StringComparison.OrdinalIgnoreCase) &&
            !resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var current = RootPath;
        if (Exists(current) && IsReparsePoint(current))
        {
            return true;
        }

        var relative = Path.GetRelativePath(RootPath, resolved);
        if (relative == ".")
        {
            return false;
        }

        foreach (var component in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            if (Exists(current) && IsReparsePoint(current))
            {
                return true;
            }
        }

        return false;
    }

    private bool Exists(string path) =>
        _fileSystem.DirectoryExists(path) || _fileSystem.FileExists(path);

    private bool IsReparsePoint(string path) =>
        (_fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}

using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32.SafeHandles;

namespace CodexQuotaHud.Skins.Storage;

internal interface ISafeDirectoryDeleteProvider
{
    void DeleteOwnedTree(string rootPath, int maximumEntries = 64);
}

internal sealed class FileSystemDirectoryDeleteProvider(
    ISkinFileSystem fileSystem) : ISafeDirectoryDeleteProvider
{
    public void DeleteOwnedTree(string rootPath, int maximumEntries = 64) =>
        fileSystem.DeleteDirectory(rootPath, recursive: true);
}

internal sealed class PhysicalDirectoryDeleteProvider : ISafeDirectoryDeleteProvider
{
    private const uint Delete = 0x00010000;
    private const uint FileReadAttributes = 0x00000080;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint OpenExisting = 3;
    private const int FileDispositionInformation = 13;

    public static PhysicalDirectoryDeleteProvider Instance { get; } = new();

    private PhysicalDirectoryDeleteProvider()
    {
    }

    public void DeleteOwnedTree(string rootPath, int maximumEntries = 64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumEntries, 1);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var count = 0;
        using var rootEntry = OpenTree(
            root,
            root,
            expectedVolumeSerialNumber: null,
            ref count,
            maximumEntries,
            countEntry: false);
        if (!rootEntry.IsDirectory || rootEntry.IsReparsePoint)
        {
            throw new IOException("The owned cleanup root is not a regular directory.");
        }

        DeleteTree(rootEntry);
    }

    private static OwnedEntry OpenTree(
        string entryPath,
        string ownedRoot,
        uint? expectedVolumeSerialNumber,
        ref int count,
        int maximumEntries,
        bool countEntry)
    {
        if (countEntry)
        {
            count = checked(count + 1);
            if (count > maximumEntries)
            {
                throw new IOException("The owned cleanup tree exceeds its entry limit.");
            }
        }

        var entry = OpenEntry(entryPath, ownedRoot, expectedVolumeSerialNumber);
        try
        {
            if (entry.IsDirectory && !entry.IsReparsePoint)
            {
                foreach (var childPath in Directory.EnumerateFileSystemEntries(entryPath))
                {
                    entry.AddChild(OpenTree(
                        childPath,
                        ownedRoot,
                        entry.VolumeSerialNumber,
                        ref count,
                        maximumEntries,
                        countEntry: true));
                }
            }

            return entry;
        }
        catch
        {
            entry.Dispose();
            throw;
        }
    }

    private static void DeleteTree(OwnedEntry entry)
    {
        foreach (var child in entry.Children)
        {
            DeleteTree(child);
            child.CloseAfterDeletion();
        }

        entry.MarkForDeletion();
    }

    private static OwnedEntry OpenEntry(
        string path,
        string ownedRoot,
        uint? expectedVolumeSerialNumber)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsWithinRoot(fullPath, ownedRoot))
        {
            throw new IOException("The owned cleanup entry escaped its root.");
        }

        var handle = CreateFileW(
            fullPath,
            Delete | FileReadAttributes,
            FileShare.Read | FileShare.Write,
            securityAttributes: IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint | FileFlagBackupSemantics,
            templateFile: IntPtr.Zero);
        try
        {
            if (handle.IsInvalid)
            {
                throw Win32Error("The owned cleanup entry could not be opened.");
            }

            if (!GetFileInformationByHandle(handle, out var information))
            {
                throw Win32Error("The owned cleanup entry identity could not be read.");
            }

            if (expectedVolumeSerialNumber is { } expectedVolume &&
                information.VolumeSerialNumber != expectedVolume)
            {
                throw new IOException("The owned cleanup entry crossed a volume boundary.");
            }

            var finalPath = NormalizeHandlePath(GetFinalPath(handle));
            if (!string.Equals(
                    Path.TrimEndingDirectorySeparator(finalPath),
                    Path.TrimEndingDirectorySeparator(fullPath),
                    StringComparison.OrdinalIgnoreCase) ||
                !IsWithinRoot(finalPath, ownedRoot))
            {
                throw new IOException("The owned cleanup entry changed identity.");
            }

            return new OwnedEntry(
                handle,
                (information.FileAttributes & FileAttributeDirectory) != 0,
                (information.FileAttributes & FileAttributeReparsePoint) != 0,
                information.VolumeSerialNumber);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static bool IsWithinRoot(string path, string root)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(
                fullRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFinalPath(SafeFileHandle handle)
    {
        var buffer = new char[512];
        while (true)
        {
            var length = GetFinalPathNameByHandleW(
                handle,
                buffer,
                (uint)buffer.Length,
                flags: 0);
            if (length == 0)
            {
                throw Win32Error("The owned cleanup entry path could not be read.");
            }

            if (length < buffer.Length)
            {
                return new string(buffer, 0, checked((int)length));
            }

            buffer = new char[checked((int)length + 1)];
        }
    }

    private static string NormalizeHandlePath(string path)
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

    private sealed class OwnedEntry(
        SafeFileHandle handle,
        bool isDirectory,
        bool isReparsePoint,
        uint volumeSerialNumber) : IDisposable
    {
        private bool _closed;

        public bool IsDirectory { get; } = isDirectory;

        public bool IsReparsePoint { get; } = isReparsePoint;

        public uint VolumeSerialNumber { get; } = volumeSerialNumber;

        public List<OwnedEntry> Children { get; } = [];

        public void AddChild(OwnedEntry child) => Children.Add(child);

        public void MarkForDeletion()
        {
            var delete = Marshal.AllocHGlobal(1);
            try
            {
                Marshal.WriteByte(delete, 1);
                var status = NtSetInformationFile(
                    handle,
                    out _,
                    delete,
                    1,
                    FileDispositionInformation);
                if (status < 0)
                {
                    throw new IOException(
                        $"The owned cleanup entry could not be deleted (NTSTATUS 0x{status:X8}).");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(delete);
            }
        }

        public void CloseAfterDeletion()
        {
            if (_closed)
            {
                return;
            }

            handle.Dispose();
            _closed = true;
        }

        public void Dispose()
        {
            foreach (var child in Children)
            {
                child.Dispose();
            }

            CloseAfterDeletion();
        }
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

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationFile(
        SafeFileHandle fileHandle,
        out IoStatusBlock ioStatusBlock,
        IntPtr fileInformation,
        uint length,
        int fileInformationClass);

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
}

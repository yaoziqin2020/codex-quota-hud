using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CodexQuotaHud.Skins.Storage;

internal interface IDirectoryLease : IDisposable
{
    DirectoryIdentity Identity { get; }
}

internal interface IDirectoryLeaseProvider
{
    IDirectoryLease Lease(string expectedPath);
}

internal interface IDirectoryMoveProvider
{
    void Move(
        IDirectoryLease sourceLease,
        string sourcePath,
        IDirectoryLease destinationParentLease,
        string destinationParentPath,
        string destinationChildName,
        string expectedDestinationPath);
}

internal sealed class PhysicalDirectoryLeaseProvider :
    IDirectoryLeaseProvider,
    IDirectoryMoveProvider
{
    public static PhysicalDirectoryLeaseProvider Instance { get; } = new();

    private PhysicalDirectoryLeaseProvider()
    {
    }

    public IDirectoryLease Lease(string expectedPath) =>
        WindowsDirectoryLease.Open(expectedPath);

    public void Move(
        IDirectoryLease sourceLease,
        string sourcePath,
        IDirectoryLease destinationParentLease,
        string destinationParentPath,
        string destinationChildName,
        string expectedDestinationPath)
    {
        if (sourceLease is not WindowsDirectoryLease source ||
            destinationParentLease is not WindowsDirectoryLease destinationParent)
        {
            throw new IOException("The exact directory move requires Windows handle leases.");
        }

        source.RenameTo(
            sourcePath,
            destinationParent,
            destinationParentPath,
            destinationChildName,
            expectedDestinationPath);
    }
}

internal sealed class FileSystemDirectoryMoveProvider(
    ISkinFileSystem fileSystem) : IDirectoryMoveProvider
{
    public void Move(
        IDirectoryLease sourceLease,
        string sourcePath,
        IDirectoryLease destinationParentLease,
        string destinationParentPath,
        string destinationChildName,
        string expectedDestinationPath)
    {
        sourceLease.Dispose();
        fileSystem.MoveDirectory(sourcePath, expectedDestinationPath);
    }
}

internal sealed class WindowsDirectoryLease : IDirectoryLease
{
    private const uint Delete = 0x00010000;
    private const uint FileAddSubdirectory = 0x00000004;
    private const uint FileReadAttributes = 0x00000080;
    private const uint FileTraverse = 0x00000020;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint OpenExisting = 3;
    private const int FileRenameInformation = 10;

    private readonly SafeFileHandle _handle;
    private readonly DirectoryIdentity _identity;
    private string _expectedPath;

    public DirectoryIdentity Identity => _identity;

    private WindowsDirectoryLease(
        SafeFileHandle handle,
        string expectedPath,
        DirectoryIdentity identity)
    {
        _handle = handle;
        _expectedPath = expectedPath;
        _identity = identity;
    }

    internal static WindowsDirectoryLease Open(
        string expectedPath,
        bool allowDeleteSharing = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPath);
        var fullExpectedPath = Path.GetFullPath(expectedPath);
        var handle = CreateFileW(
            fullExpectedPath,
            FileReadAttributes | FileAddSubdirectory | FileTraverse | Delete,
            FileShare.Read | FileShare.Write |
                (allowDeleteSharing ? FileShare.Delete : (FileShare)0),
            securityAttributes: IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint | FileFlagBackupSemantics,
            templateFile: IntPtr.Zero);
        try
        {
            if (handle.IsInvalid)
            {
                throw Win32Error("The directory lease could not be opened.");
            }

            var identity = ReadAndValidate(
                handle,
                fullExpectedPath,
                expectedIdentity: null);
            return new WindowsDirectoryLease(
                handle,
                fullExpectedPath,
                identity);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public void RenameTo(
        string sourcePath,
        WindowsDirectoryLease destinationParent,
        string destinationParentPath,
        string destinationChildName,
        string expectedDestinationPath)
    {
        ArgumentNullException.ThrowIfNull(destinationParent);
        if (!string.Equals(
                Path.GetFullPath(sourcePath),
                _expectedPath,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                Path.GetFullPath(destinationParentPath),
                destinationParent._expectedPath,
                StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(destinationChildName, "D", out var childId) ||
            !string.Equals(
                destinationChildName,
                childId.ToString("D").ToLowerInvariant(),
                StringComparison.Ordinal) ||
            !string.Equals(
                Path.GetFullPath(expectedDestinationPath),
                Path.Combine(destinationParent._expectedPath, destinationChildName),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("The exact directory move target is invalid.");
        }

        _ = ReadAndValidate(_handle, _expectedPath, _identity);
        _ = ReadAndValidate(
            destinationParent._handle,
            destinationParent._expectedPath,
            destinationParent._identity);
        if (Directory.Exists(expectedDestinationPath) ||
            File.Exists(expectedDestinationPath))
        {
            throw new IOException("The exact directory move target already exists.");
        }

        var nameBytes = Encoding.Unicode.GetBytes(destinationChildName);
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

            Marshal.WriteIntPtr(
                buffer,
                rootOffset,
                destinationParent._handle.DangerousGetHandle());
            Marshal.WriteInt32(buffer, lengthOffset, nameBytes.Length);
            Marshal.Copy(nameBytes, 0, buffer + nameOffset, nameBytes.Length);
            var status = NtSetInformationFile(
                _handle,
                out _,
                buffer,
                checked((uint)bufferSize),
                FileRenameInformation);
            if (status < 0)
            {
                throw NtStatusError("The exact directory move failed.", status);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        var fullDestinationPath = Path.GetFullPath(expectedDestinationPath);
        _ = ReadAndValidate(_handle, fullDestinationPath, _identity);
        _expectedPath = fullDestinationPath;
    }

    public void Dispose() => _handle.Dispose();

    private static DirectoryIdentity ReadAndValidate(
        SafeFileHandle handle,
        string expectedPath,
        DirectoryIdentity? expectedIdentity)
    {
        if (!GetFileInformationByHandle(handle, out var information))
        {
            throw Win32Error("The directory lease identity could not be read.");
        }

        if ((information.FileAttributes & FileAttributeReparsePoint) != 0)
        {
            throw new IOException("The directory lease target is a reparse point.");
        }

        var finalPath = RemoveExtendedPathPrefix(GetFinalPath(handle));
        if (!string.Equals(
                finalPath,
                Path.GetFullPath(expectedPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("The directory lease target changed path.");
        }

        var identity = new DirectoryIdentity(
            information.VolumeSerialNumber,
            ((ulong)information.FileIndexHigh << 32) |
                information.FileIndexLow);
        if (expectedIdentity is { } expected && identity != expected)
        {
            throw new IOException("The directory lease target changed identity.");
        }

        return identity;
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
                throw Win32Error("The directory lease path could not be read.");
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

using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CodexQuotaHud.Skins.Storage;

internal interface IOwnedStorageWriter
{
    IDirectoryLease OpenOrCreateChildDirectory(
        IDirectoryLease parentLease,
        string fixedSingleSegmentName,
        string expectedPath);

    void CreateNewChildFileAndFlush(
        IDirectoryLease parentLease,
        string fixedSingleSegmentName,
        ReadOnlySpan<byte> content);
}

internal sealed class PhysicalOwnedStorageWriter : IOwnedStorageWriter
{
    private const uint Delete = 0x00010000;
    private const uint Synchronize = 0x00100000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileListDirectory = 0x00000001;
    private const uint FileAddFile = 0x00000002;
    private const uint FileAddSubdirectory = 0x00000004;
    private const uint FileTraverse = 0x00000020;
    private const uint FileReadAttributes = 0x00000080;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileOpenIf = 3;
    private const uint FileCreate = 2;
    private const uint FileDirectoryFile = 0x00000001;
    private const uint FileSynchronousIoNonAlert = 0x00000020;
    private const uint FileNonDirectoryFile = 0x00000040;
    private const uint FileOpenReparsePoint = 0x00200000;
    private const uint ObjCaseInsensitive = 0x00000040;

    public static PhysicalOwnedStorageWriter Instance { get; } = new();

    private PhysicalOwnedStorageWriter()
    {
    }

    public IDirectoryLease OpenOrCreateChildDirectory(
        IDirectoryLease parentLease,
        string fixedSingleSegmentName,
        string expectedPath)
    {
        ArgumentNullException.ThrowIfNull(parentLease);
        var fullExpectedPath = OwnedStorageName.ValidateExpectedPath(
            parentLease,
            fixedSingleSegmentName,
            expectedPath);
        if (parentLease is not WindowsDirectoryLease windowsParent)
        {
            throw new IOException(
                "Handle-relative directory creation requires a Windows lease.");
        }

        var handle = windowsParent.UseValidatedHandle(parentHandle =>
            CreateRelative(
                parentHandle,
                fixedSingleSegmentName,
                FileListDirectory | FileAddFile | FileAddSubdirectory |
                    FileTraverse | FileReadAttributes | Delete | Synchronize,
                FileShareRead | FileShareWrite,
                FileOpenIf,
                FileDirectoryFile | FileSynchronousIoNonAlert |
                    FileOpenReparsePoint,
                fileAttributes: 0));
        var lease = WindowsDirectoryLease.FromOwnedHandle(
            handle,
            fullExpectedPath);
        if (lease.Identity.VolumeSerialNumber !=
            parentLease.Identity.VolumeSerialNumber)
        {
            lease.Dispose();
            throw new IOException(
                "The owned directory target is on a different volume.");
        }

        return lease;
    }

    public void CreateNewChildFileAndFlush(
        IDirectoryLease parentLease,
        string fixedSingleSegmentName,
        ReadOnlySpan<byte> content)
    {
        ArgumentNullException.ThrowIfNull(parentLease);
        OwnedStorageName.ValidateSegment(fixedSingleSegmentName);
        if (parentLease is not WindowsDirectoryLease windowsParent)
        {
            throw new IOException(
                "Handle-relative file creation requires a Windows lease.");
        }

        var expectedPath = Path.Combine(
            parentLease.ExpectedPath,
            fixedSingleSegmentName);
        using var handle = windowsParent.UseValidatedHandle(parentHandle =>
            CreateRelative(
                parentHandle,
                fixedSingleSegmentName,
                GenericWrite | FileReadAttributes | Synchronize,
                FileShareRead,
                FileCreate,
                FileNonDirectoryFile | FileSynchronousIoNonAlert |
                    FileOpenReparsePoint,
                FileAttributeNormal));
        var identity = WindowsDirectoryLease.ValidateFileHandle(
            handle,
            expectedPath);
        if (identity.VolumeSerialNumber != parentLease.Identity.VolumeSerialNumber)
        {
            throw new IOException("The owned file target is on a different volume.");
        }

        using var stream = new FileStream(handle, FileAccess.Write);
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }

    private static SafeFileHandle CreateRelative(
        IntPtr parentHandle,
        string name,
        uint desiredAccess,
        uint shareAccess,
        uint createDisposition,
        uint createOptions,
        uint fileAttributes)
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
            unicodeStringPointer = Marshal.AllocHGlobal(
                Marshal.SizeOf<UnicodeString>());
            Marshal.StructureToPtr(
                unicodeString,
                unicodeStringPointer,
                fDeleteOld: false);
            var objectAttributes = new ObjectAttributes
            {
                Length = checked((uint)Marshal.SizeOf<ObjectAttributes>()),
                RootDirectory = parentHandle,
                ObjectName = unicodeStringPointer,
                Attributes = ObjCaseInsensitive
            };
            var status = NtCreateFile(
                out rawHandle,
                desiredAccess,
                ref objectAttributes,
                out _,
                allocationSize: IntPtr.Zero,
                fileAttributes,
                shareAccess,
                createDisposition,
                createOptions,
                eaBuffer: IntPtr.Zero,
                eaLength: 0);
            if (status < 0)
            {
                if (rawHandle != IntPtr.Zero && rawHandle != new IntPtr(-1))
                {
                    new SafeFileHandle(rawHandle, ownsHandle: true).Dispose();
                    rawHandle = IntPtr.Zero;
                }

                throw NtStatusError(
                    "The handle-relative storage object could not be created.",
                    status);
            }

            var result = new SafeFileHandle(rawHandle, ownsHandle: true);
            rawHandle = IntPtr.Zero;
            if (result.IsInvalid)
            {
                result.Dispose();
                throw new IOException(
                    "The handle-relative storage object returned an invalid handle.");
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

    private static IOException NtStatusError(string message, int status)
    {
        var error = RtlNtStatusToDosError(status);
        return new IOException(
            $"{message} NTSTATUS 0x{status:X8}, Win32 error {error}: " +
            new Win32Exception(checked((int)error)).Message);
    }

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
}

internal sealed class FileSystemOwnedStorageWriter(
    ISkinFileSystem fileSystem,
    IDirectoryLeaseProvider directoryLeaseProvider) : IOwnedStorageWriter
{
    public IDirectoryLease OpenOrCreateChildDirectory(
        IDirectoryLease parentLease,
        string fixedSingleSegmentName,
        string expectedPath)
    {
        var fullExpectedPath = OwnedStorageName.ValidateExpectedPath(
            parentLease,
            fixedSingleSegmentName,
            expectedPath);
        fileSystem.CreateDirectory(fullExpectedPath);
        return directoryLeaseProvider.Lease(fullExpectedPath);
    }

    public void CreateNewChildFileAndFlush(
        IDirectoryLease parentLease,
        string fixedSingleSegmentName,
        ReadOnlySpan<byte> content)
    {
        ArgumentNullException.ThrowIfNull(parentLease);
        OwnedStorageName.ValidateSegment(fixedSingleSegmentName);
        var path = Path.Combine(
            parentLease.ExpectedPath,
            fixedSingleSegmentName);
        if (fileSystem.FileExists(path) || fileSystem.DirectoryExists(path))
        {
            throw new IOException("The owned file target already exists.");
        }

        fileSystem.WriteAllBytesAndFlush(path, content);
    }
}

internal static class OwnedStorageName
{
    internal static string ValidateExpectedPath(
        IDirectoryLease parentLease,
        string fixedSingleSegmentName,
        string expectedPath)
    {
        ArgumentNullException.ThrowIfNull(parentLease);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPath);
        ValidateSegment(fixedSingleSegmentName);
        var fullExpectedPath = Path.GetFullPath(expectedPath);
        if (!string.Equals(
                fullExpectedPath,
                Path.Combine(parentLease.ExpectedPath, fixedSingleSegmentName),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException(
                "The owned storage child path does not match its leased parent.");
        }

        return fullExpectedPath;
    }

    internal static void ValidateSegment(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name is "." or ".." ||
            Path.IsPathRooted(name) ||
            name.IndexOfAny(['/', '\\', ':', '\0']) >= 0 ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
        {
            throw new IOException(
                "The owned storage child name must be one fixed path segment.");
        }

        _ = checked((ushort)(name.Length * sizeof(char) + sizeof(char)));
    }
}

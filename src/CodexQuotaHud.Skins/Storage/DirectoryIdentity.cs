using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32.SafeHandles;

namespace CodexQuotaHud.Skins.Storage;

internal readonly record struct DirectoryIdentity(
    uint VolumeSerialNumber,
    ulong FileIndex);

internal interface IDirectoryIdentityProvider
{
    bool TryGetIdentity(string directoryPath, out DirectoryIdentity identity);
}

internal sealed class PhysicalDirectoryIdentityProvider : IDirectoryIdentityProvider
{
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint OpenExisting = 3;

    public static PhysicalDirectoryIdentityProvider Instance { get; } = new();

    private PhysicalDirectoryIdentityProvider()
    {
    }

    public bool TryGetIdentity(
        string directoryPath,
        out DirectoryIdentity identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        identity = default;
        using var handle = CreateFileW(
            directoryPath,
            desiredAccess: 0,
            FileShare.Read | FileShare.Write | FileShare.Delete,
            securityAttributes: IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            templateFile: IntPtr.Zero);
        if (handle.IsInvalid ||
            !GetFileInformationByHandle(handle, out var information))
        {
            return false;
        }

        identity = new DirectoryIdentity(
            information.VolumeSerialNumber,
            ((ulong)information.FileIndexHigh << 32) |
                information.FileIndexLow);
        return true;
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

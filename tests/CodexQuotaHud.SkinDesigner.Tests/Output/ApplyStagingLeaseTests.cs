using System.ComponentModel;
using System.Runtime.InteropServices;
using CodexQuotaHud.SkinDesigner.Output;
using Microsoft.Win32.SafeHandles;

namespace CodexQuotaHud.SkinDesigner.Tests.Output;

public sealed class ApplyStagingLeaseTests
{
    [Fact]
    public void PhysicalLease_HoldsExactOperationIdentityAndDeletesOnlyOwnedOperation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TemporaryRoot();
        var otherOperation = Path.Combine(
            root.Paths.ImportsRoot,
            "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        Directory.CreateDirectory(otherOperation);
        var sentinel = Path.Combine(otherOperation, "sentinel.bin");
        File.WriteAllBytes(sentinel, "other"u8.ToArray());
        using var lease = PhysicalApplyStagingLeaseProvider.Instance.Create(
            root.Paths);

        Assert.Equal(
            Path.Combine(lease.OperationPath, "apply.cqskin"),
            lease.PackagePath);
        Assert.ThrowsAny<IOException>(() => Directory.Move(
            lease.OperationPath,
            lease.OperationPath + "-swapped"));
        lease.PackageStream.Write("package"u8);
        lease.PackageStream.Flush();
        Assert.ThrowsAny<IOException>(() => File.Move(
            lease.PackagePath,
            lease.PackagePath + ".swapped"));
        Assert.ThrowsAny<IOException>(() => File.Delete(lease.PackagePath));

        lease.DeleteOwnedOperation();

        Assert.False(Directory.Exists(lease.OperationPath));
        Assert.Equal("other"u8.ToArray(), File.ReadAllBytes(sentinel));
        Assert.Equal([Path.GetFullPath(otherOperation)], Directory
            .EnumerateDirectories(root.Paths.ImportsRoot)
            .Select(Path.GetFullPath)
            .ToArray());
    }

    [Fact]
    public void Create_PostCreateFailureDeletesExactFileAndOperationButPreservesSibling()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TemporaryRoot();
        var siblingOperation = Path.Combine(
            root.Paths.ImportsRoot,
            "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        Directory.CreateDirectory(siblingOperation);
        var sentinel = Path.Combine(siblingOperation, "sentinel.bin");
        File.WriteAllBytes(sentinel, "sibling"u8.ToArray());
        var primary = new InvalidDataException(
            "Injected failure after the exact package file was created.");
        var provider = new PhysicalApplyStagingLeaseProvider(
            _ => throw primary);

        var actual = Assert.Throws<InvalidDataException>(() =>
            provider.Create(root.Paths));

        Assert.Same(primary, actual);
        Assert.Equal("sibling"u8.ToArray(), File.ReadAllBytes(sentinel));
        Assert.Equal(
            [Path.GetFullPath(siblingOperation)],
            Directory.EnumerateDirectories(root.Paths.ImportsRoot)
                .Select(Path.GetFullPath)
                .ToArray());
        Assert.Equal(
            [Path.GetFullPath(sentinel)],
            Directory.EnumerateFiles(
                    root.Paths.ImportsRoot,
                    "*",
                    SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .ToArray());
    }

    [Fact]
    public void Create_PostCreateCleanupFailureDoesNotMaskPrimaryFailure()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TemporaryRoot();
        var siblingOperation = Path.Combine(
            root.Paths.ImportsRoot,
            "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        Directory.CreateDirectory(siblingOperation);
        var sentinel = Path.Combine(siblingOperation, "sentinel.bin");
        File.WriteAllBytes(sentinel, "sibling"u8.ToArray());
        var primary = new InvalidDataException(
            "Injected primary post-create failure.");
        var provider = new PhysicalApplyStagingLeaseProvider(handle =>
        {
            handle.Dispose();
            throw primary;
        });

        var actual = Record.Exception(() => provider.Create(root.Paths));

        Assert.Same(primary, actual);
        Assert.Equal("sibling"u8.ToArray(), File.ReadAllBytes(sentinel));
    }

    [Fact]
    public void PhysicalLease_RejectsImportsReparseWithoutTouchingTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TemporaryRoot();
        Directory.CreateDirectory(root.Paths.SettingsRoot);
        var outside = Path.Combine(root.Path, "outside");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "sentinel.bin");
        File.WriteAllBytes(sentinel, "outside"u8.ToArray());
        CreateJunction(root.Paths.ImportsRoot, outside);
        try
        {
            Assert.ThrowsAny<IOException>(() =>
                PhysicalApplyStagingLeaseProvider.Instance.Create(root.Paths));
            Assert.Equal("outside"u8.ToArray(), File.ReadAllBytes(sentinel));
            Assert.Equal(
                ["sentinel.bin"],
                Directory.EnumerateFileSystemEntries(
                        outside,
                        "*",
                        SearchOption.AllDirectories)
                    .Select(path => Path.GetRelativePath(outside, path)
                        .Replace('\\', '/'))
                    .ToArray());
        }
        finally
        {
            Assert.True(
                (File.GetAttributes(root.Paths.ImportsRoot) &
                    FileAttributes.ReparsePoint) != 0);
            Directory.Delete(root.Paths.ImportsRoot);
        }
    }

    private static void CreateJunction(string junctionPath, string targetPath)
    {
        const uint genericWrite = 0x40000000;
        const uint openExisting = 3;
        const uint openReparsePoint = 0x00200000;
        const uint backupSemantics = 0x02000000;
        const uint setReparsePoint = 0x000900A4;
        const uint mountPointTag = 0xA0000003;
        Directory.CreateDirectory(junctionPath);
        using var handle = CreateFileW(
            junctionPath,
            genericWrite,
            FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero,
            openExisting,
            openReparsePoint | backupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var substitute = System.Text.Encoding.Unicode.GetBytes(
            @"\??\" + Path.GetFullPath(targetPath));
        var print = System.Text.Encoding.Unicode.GetBytes(
            Path.GetFullPath(targetPath));
        var pathBytes = checked(substitute.Length + 2 + print.Length + 2);
        var reparseDataLength = checked((ushort)(8 + pathBytes));
        var bufferLength = checked(8 + reparseDataLength);
        var buffer = Marshal.AllocHGlobal(bufferLength);
        try
        {
            for (var index = 0; index < bufferLength; index++)
            {
                Marshal.WriteByte(buffer, index, 0);
            }

            Marshal.WriteInt32(buffer, 0, unchecked((int)mountPointTag));
            Marshal.WriteInt16(buffer, 4, unchecked((short)reparseDataLength));
            Marshal.WriteInt16(buffer, 8, 0);
            Marshal.WriteInt16(buffer, 10, checked((short)substitute.Length));
            Marshal.WriteInt16(buffer, 12, checked((short)(substitute.Length + 2)));
            Marshal.WriteInt16(buffer, 14, checked((short)print.Length));
            Marshal.Copy(substitute, 0, buffer + 16, substitute.Length);
            Marshal.Copy(print, 0, buffer + 16 + substitute.Length + 2, print.Length);
            if (!DeviceIoControl(
                    handle,
                    setReparsePoint,
                    buffer,
                    checked((uint)bufferLength),
                    IntPtr.Zero,
                    0,
                    out _,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
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
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        IntPtr inputBuffer,
        uint inputBufferSize,
        IntPtr outputBuffer,
        uint outputBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    private sealed class TemporaryRoot : IDisposable
    {
        public TemporaryRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CodexQuotaHud-Task15-staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            Paths = new CodexQuotaHud.Skins.Storage.SkinStoragePaths(Path);
        }

        public string Path { get; }

        public CodexQuotaHud.Skins.Storage.SkinStoragePaths Paths { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

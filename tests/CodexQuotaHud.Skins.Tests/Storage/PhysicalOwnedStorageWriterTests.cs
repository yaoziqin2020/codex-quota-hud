using System.Diagnostics;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.Skins.Tests.Storage;

public sealed class PhysicalOwnedStorageWriterTests
{
    [Fact]
    public void OpenOrCreateChildDirectory_RejectsExistingJunctionWithoutTouchingTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var testRoot = CreateTestRoot();
        var parentPath = Path.Combine(testRoot, "parent");
        var outsidePath = Path.Combine(testRoot, "outside");
        var childPath = Path.Combine(parentPath, "child");
        var sentinelPath = Path.Combine(outsidePath, "sentinel.bin");
        var sentinelBytes = new byte[] { 0x00, 0x19, 0x7F, 0xA5, 0xFF };
        Directory.CreateDirectory(parentPath);
        Directory.CreateDirectory(outsidePath);
        File.WriteAllBytes(sentinelPath, sentinelBytes);

        try
        {
            CreateJunction(childPath, outsidePath);

            using (var parentLease = PhysicalDirectoryLeaseProvider.Instance.Lease(
                       parentPath))
            {
                Assert.Throws<IOException>(() =>
                    PhysicalOwnedStorageWriter.Instance.OpenOrCreateChildDirectory(
                        parentLease,
                        "child",
                        childPath));
            }

            Assert.Equal(sentinelBytes, File.ReadAllBytes(sentinelPath));
            Assert.Equal(
                ["sentinel.bin"],
                Directory.EnumerateFileSystemEntries(
                        outsidePath,
                        "*",
                        SearchOption.AllDirectories)
                    .Select(path => Path.GetRelativePath(outsidePath, path)
                        .Replace('\\', '/'))
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray());
        }
        finally
        {
            DeleteKnownJunction(childPath);
            DeleteTestRoot(testRoot);
        }
    }

    [Fact]
    public void CreateNewChildFileAndFlush_RejectsExistingFileWithoutChangingBytes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var testRoot = CreateTestRoot();
        var parentPath = Path.Combine(testRoot, "parent");
        var filePath = Path.Combine(parentPath, "manifest.json");
        var originalBytes = new byte[] { 0x7B, 0x22, 0x78, 0x22, 0x3A, 0x31, 0x7D };
        Directory.CreateDirectory(parentPath);
        File.WriteAllBytes(filePath, originalBytes);

        try
        {
            using (var parentLease = PhysicalDirectoryLeaseProvider.Instance.Lease(
                       parentPath))
            {
                Assert.Throws<IOException>(() =>
                    PhysicalOwnedStorageWriter.Instance.CreateNewChildFileAndFlush(
                        parentLease,
                        "manifest.json",
                        new byte[] { 0x7B, 0x7D }));
            }

            Assert.Equal(originalBytes, File.ReadAllBytes(filePath));
        }
        finally
        {
            DeleteTestRoot(testRoot);
        }
    }

    [Fact]
    public void ChildDirectoryLease_BlocksRenameAndDeleteUntilDisposed()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var testRoot = CreateTestRoot();
        var parentPath = Path.Combine(testRoot, "parent");
        var childPath = Path.Combine(parentPath, "child");
        var renamedPath = Path.Combine(parentPath, "renamed");
        Directory.CreateDirectory(parentPath);

        try
        {
            using (var parentLease = PhysicalDirectoryLeaseProvider.Instance.Lease(
                       parentPath))
            {
                var childLease =
                    PhysicalOwnedStorageWriter.Instance.OpenOrCreateChildDirectory(
                        parentLease,
                        "child",
                        childPath);
                try
                {
                    Assert.ThrowsAny<IOException>(() =>
                        Directory.Move(childPath, renamedPath));
                    Assert.ThrowsAny<IOException>(() =>
                        Directory.Delete(childPath));
                }
                finally
                {
                    childLease.Dispose();
                }

                Directory.Move(childPath, renamedPath);
                Directory.Delete(renamedPath);
            }

            Assert.False(Directory.Exists(childPath));
            Assert.False(Directory.Exists(renamedPath));
        }
        finally
        {
            DeleteTestRoot(testRoot);
        }
    }

    private static string CreateTestRoot()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "CodexQuotaHud.PhysicalOwnedStorageWriterTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CreateJunction(string junctionPath, string targetPath)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            ArgumentList =
            {
                "/c",
                "mklink",
                "/J",
                junctionPath,
                targetPath
            },
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        Assert.NotNull(process);
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0,
            $"mklink failed: {process.StandardError.ReadToEnd()}");
    }

    private static void DeleteKnownJunction(string junctionPath)
    {
        if (!Directory.Exists(junctionPath))
        {
            return;
        }

        var attributes = File.GetAttributes(junctionPath);
        Assert.True((attributes & FileAttributes.ReparsePoint) != 0);
        Directory.Delete(junctionPath);
    }

    private static void DeleteTestRoot(string testRoot)
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }
}

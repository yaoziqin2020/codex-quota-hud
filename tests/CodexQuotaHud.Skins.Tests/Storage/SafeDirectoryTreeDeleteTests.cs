using System.Diagnostics;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.Skins.Tests.Storage;

public sealed class SafeDirectoryTreeDeleteTests
{
    [Fact]
    public void DeleteOwnedTree_EntryLimitFailsBeforeDeletingAnything()
    {
        var operationRoot = Path.Combine(
            Path.GetTempPath(),
            "CodexQuotaHud.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(operationRoot);
        var paths = Enumerable.Range(1, 3)
            .Select(index => Path.Combine(operationRoot, $"entry-{index}.txt"))
            .ToArray();
        foreach (var path in paths)
        {
            File.WriteAllText(path, Path.GetFileName(path));
        }

        try
        {
            Assert.Throws<IOException>(() =>
                PhysicalDirectoryDeleteProvider.Instance.DeleteOwnedTree(
                    operationRoot,
                    maximumEntries: 2));

            Assert.True(Directory.Exists(operationRoot));
            Assert.All(paths, path => Assert.True(File.Exists(path)));
        }
        finally
        {
            if (Directory.Exists(operationRoot))
            {
                Directory.Delete(operationRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void DeleteOwnedTree_DeletesJunctionWithoutFollowingItsTarget()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "CodexQuotaHud.Tests",
            Guid.NewGuid().ToString("N"));
        var operationRoot = Path.Combine(testRoot, "operation");
        var assetsRoot = Path.Combine(
            operationRoot,
            "remove",
            Guid.NewGuid().ToString("D").ToLowerInvariant(),
            "assets");
        var outsideRoot = Path.Combine(testRoot, "outside");
        var junctionPath = Path.Combine(assetsRoot, "linked-outside");
        var sentinelPath = Path.Combine(outsideRoot, "sentinel.txt");

        Directory.CreateDirectory(assetsRoot);
        Directory.CreateDirectory(outsideRoot);
        File.WriteAllText(sentinelPath, "must survive exact bytes");
        try
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
                    outsideRoot
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

            PhysicalDirectoryDeleteProvider.Instance.DeleteOwnedTree(
                operationRoot,
                maximumEntries: 67);

            Assert.False(Directory.Exists(operationRoot));
            Assert.True(Directory.Exists(outsideRoot));
            Assert.Equal("must survive exact bytes", File.ReadAllText(sentinelPath));
        }
        finally
        {
            if (Directory.Exists(junctionPath))
            {
                Directory.Delete(junctionPath);
            }

            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }
}

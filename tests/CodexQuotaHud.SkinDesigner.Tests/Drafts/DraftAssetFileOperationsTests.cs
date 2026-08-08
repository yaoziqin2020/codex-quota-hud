using System.ComponentModel;
using System.Runtime.InteropServices;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;
using Microsoft.Win32.SafeHandles;

namespace CodexQuotaHud.SkinDesigner.Tests.Drafts;

public sealed class DraftAssetFileOperationsTests
{
    private const string Hash =
        "8ff3052044472bb44cfea3d2f45203d1bd74bc868a8628eb1fd0ab0a1aa2e2b3";
    private const string ContentLeaf = "sha256-" + Hash + ".png";
    private const string ContentPath = "assets/" + ContentLeaf;
    private const string JpgContentLeaf = "sha256-" + Hash + ".jpg";
    private const string JpgContentPath = "assets/" + JpgContentLeaf;

    [Fact]
    public void ContentHelpers_AddressExactBytesAndResolveLegacyOrImmutableLeaf()
    {
        var bytes = "immutable-png"u8.ToArray();
        var legacy = new DraftAssetReference(
            SkinAssetSlot.Center,
            "assets/center.png",
            "center.png");
        var immutable = legacy with { StorageRelativePath = ContentPath };
        var jpg = legacy with
        {
            RelativePath = "assets/center.jpg",
            OriginalFileName = "center.jpg",
            StorageRelativePath = JpgContentPath
        };

        Assert.Equal(
            ContentPath,
            DraftAssetStorage.CreateContentRelativePath(legacy.RelativePath, bytes));
        Assert.Equal("center.png", DraftAssetStorage.ResolveOwnedLeaf(legacy));
        Assert.Equal(ContentLeaf, DraftAssetStorage.ResolveOwnedLeaf(immutable));
        Assert.True(DraftAssetStorage.IsValidContentRelativePath(
            immutable.StorageRelativePath,
            immutable.RelativePath));
        Assert.True(DraftAssetStorage.MatchesContent(immutable, bytes));
        Assert.False(DraftAssetStorage.MatchesContent(
            immutable,
            "immutable-png-changed"u8));
        Assert.Equal(
            JpgContentPath,
            DraftAssetStorage.CreateContentRelativePath(jpg.RelativePath, bytes));
        Assert.True(DraftAssetStorage.IsValidContentRelativePath(
            jpg.StorageRelativePath,
            jpg.RelativePath));
        Assert.True(DraftAssetStorage.MatchesContent(jpg, bytes));
    }

    [Fact]
    public void PhysicalLease_PromotesOnceAndNeverReplacesImmutableBytes()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TemporaryRoot();
        using var project = OpenProject(root);
        using var assets = project.OpenAssets(create: true);
        var firstOperation = OperationLeaf("background.png", "aaaaaaaa");
        var secondOperation = OperationLeaf("background.png", "bbbbbbbb");
        var firstBytes = "immutable-png"u8.ToArray();
        var secondBytes = "replacement"u8.ToArray();

        assets.WriteAndFlushNew(firstOperation, firstBytes, CancellationToken.None);
        assets.MoveOperationToImmutable(firstOperation, ContentLeaf);
        assets.ReleaseOperation(firstOperation);
        Assert.True(assets.FileExists(ContentLeaf));
        Assert.Equal(firstBytes, assets.ReadAllBytes(ContentLeaf));
        assets.WriteAndFlushNew(secondOperation, secondBytes, CancellationToken.None);

        Assert.ThrowsAny<IOException>(() =>
            assets.MoveOperationToImmutable(secondOperation, ContentLeaf));
        Assert.Equal(firstBytes, File.ReadAllBytes(Path.Combine(
            root.ProjectPaths.AssetsRoot,
            ContentLeaf)));
        Assert.Equal(secondBytes, assets.ReadOperationBytes(secondOperation));
        assets.DeleteOperation(secondOperation);
    }

    [Fact]
    public void PhysicalLease_PromotedJpgIsReadableThroughReadOnlyAssetPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TemporaryRoot();
        using var project = OpenProject(root);
        using var assets = project.OpenAssets(create: true);
        var operation = OperationLeaf("center.jpg", "aaaaaaaa");
        var bytes = "immutable-png"u8.ToArray();

        assets.WriteAndFlushNew(operation, bytes, CancellationToken.None);
        assets.MoveOperationToImmutable(operation, JpgContentLeaf);
        assets.ReleaseOperation(operation);

        Assert.True(assets.FileExists(JpgContentLeaf));
        Assert.Equal(bytes, assets.ReadAllBytes(JpgContentLeaf));
    }

    [Fact]
    public void PhysicalLease_FixedCanonicalPromotionRetainsReplacementSemantics()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TemporaryRoot();
        using var project = OpenProject(root);
        using var assets = project.OpenAssets(create: true);
        var firstOperation = OperationLeaf("background.png", "aaaaaaaa");
        var secondOperation = OperationLeaf("background.png", "bbbbbbbb");

        assets.WriteAndFlushNew(
            firstOperation,
            "first"u8,
            CancellationToken.None);
        assets.MoveOperationToCanonical(firstOperation, "background.png");
        assets.ReleaseOperation(firstOperation);
        assets.WriteAndFlushNew(
            secondOperation,
            "second"u8,
            CancellationToken.None);
        assets.MoveOperationToCanonical(secondOperation, "background.png");
        assets.ReleaseOperation(secondOperation);

        Assert.True(assets.FileExists("background.png"));
        Assert.Equal("second"u8.ToArray(), assets.ReadAllBytes("background.png"));
    }

    [Theory]
    [InlineData("../sha256-8ff3052044472bb44cfea3d2f45203d1bd74bc868a8628eb1fd0ab0a1aa2e2b3.png")]
    [InlineData("assets/sha256-8ff3052044472bb44cfea3d2f45203d1bd74bc868a8628eb1fd0ab0a1aa2e2b3.png")]
    [InlineData("sha256-8FF3052044472BB44CFEA3D2F45203D1BD74BC868A8628EB1FD0AB0A1AA2E2B3.png")]
    [InlineData("sha256-8ff3052044472bb44cfea3d2f45203d1bd74bc868a8628eb1fd0ab0a1aa2e2b3.gif")]
    public void PhysicalLease_RejectsInvalidImmutableLeafBeforeMutation(
        string invalidLeaf)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TemporaryRoot();
        using var project = OpenProject(root);
        using var assets = project.OpenAssets(create: true);
        var operation = OperationLeaf("background.png", "aaaaaaaa");
        var bytes = "operation"u8.ToArray();
        assets.WriteAndFlushNew(operation, bytes, CancellationToken.None);

        Assert.Throws<DraftUnsafePathException>(() =>
            assets.MoveOperationToImmutable(operation, invalidLeaf));
        Assert.Equal(bytes, assets.ReadOperationBytes(operation));
        Assert.Equal(
            [operation],
            Directory.EnumerateFiles(root.ProjectPaths.AssetsRoot)
                .Select(path => Path.GetFileName(path)!)
                .ToArray());
        assets.DeleteOperation(operation);
    }

    [Theory]
    [InlineData("../sha256-8ff3052044472bb44cfea3d2f45203d1bd74bc868a8628eb1fd0ab0a1aa2e2b3.png")]
    [InlineData("assets/sha256-8ff3052044472bb44cfea3d2f45203d1bd74bc868a8628eb1fd0ab0a1aa2e2b3.png")]
    [InlineData("sha256-8FF3052044472BB44CFEA3D2F45203D1BD74BC868A8628EB1FD0AB0A1AA2E2B3.png")]
    [InlineData("sha256-8ff3052044472bb44cfea3d2f45203d1bd74bc868a8628eb1fd0ab0a1aa2e2b3.gif")]
    public void PhysicalLease_ReadOnlyAssetPathRejectsTraversalOrMalformedLeaf(
        string invalidLeaf)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TemporaryRoot();
        using var project = OpenProject(root);
        using var assets = project.OpenAssets(create: true);

        Assert.Throws<DraftUnsafePathException>(() => assets.FileExists(invalidLeaf));
        Assert.Throws<DraftUnsafePathException>(() => assets.ReadAllBytes(invalidLeaf));
        Assert.Empty(Directory.EnumerateFileSystemEntries(root.ProjectPaths.AssetsRoot));
    }

    [Fact]
    public void PhysicalLease_ReadOnlyAssetPathRejectsReparseBeforeExternalAccess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TemporaryRoot();
        using var project = OpenProject(root);
        using var assets = project.OpenAssets(create: true);
        var outside = Path.Combine(root.Path, "outside-readable-leaf");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "sentinel.bin");
        File.WriteAllBytes(sentinel, "outside"u8.ToArray());
        var reparseLeaf = Path.Combine(root.ProjectPaths.AssetsRoot, ContentLeaf);
        CreateJunction(reparseLeaf, outside);
        try
        {
            Assert.ThrowsAny<IOException>(() => assets.FileExists(ContentLeaf));
            Assert.ThrowsAny<IOException>(() => assets.ReadAllBytes(ContentLeaf));
            Assert.Equal("outside"u8.ToArray(), File.ReadAllBytes(sentinel));
            Assert.Equal(["sentinel.bin"], Directory
                .EnumerateFileSystemEntries(outside)
                .Select(path => Path.GetFileName(path)!)
                .ToArray());
        }
        finally
        {
            Directory.Delete(reparseLeaf);
        }
    }

    [Fact]
    public void PhysicalLease_RejectsReparseAssetsDirectoryWithoutTouchingTarget()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var root = new TemporaryRoot();
        using var project = OpenProject(root);
        var outside = Path.Combine(root.Path, "outside");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "sentinel.bin");
        File.WriteAllBytes(sentinel, "outside"u8.ToArray());
        CreateJunction(root.ProjectPaths.AssetsRoot, outside);
        try
        {
            Assert.ThrowsAny<IOException>(() => project.OpenAssets(create: false));
            Assert.Equal("outside"u8.ToArray(), File.ReadAllBytes(sentinel));
            Assert.Equal(["sentinel.bin"], Directory
                .EnumerateFileSystemEntries(outside)
                .Select(path => Path.GetFileName(path)!)
                .ToArray());
        }
        finally
        {
            Directory.Delete(root.ProjectPaths.AssetsRoot);
        }
    }

    private static IDesignerDraftProjectLease OpenProject(TemporaryRoot root) =>
        Assert.IsAssignableFrom<IDesignerDraftProjectLease>(
            PhysicalDraftFileOperations.Instance.OpenDesignerProject(
                root.Paths.DraftsRoot,
                root.DraftId,
                DesignerDraftProjectOpenMode.CreateExclusive));

    private static string OperationLeaf(string canonicalLeaf, string prefix) =>
        $".{canonicalLeaf}.tmp-{prefix}-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

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
                "CodexQuotaHud-Task6-draft-assets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            Paths = new SkinStoragePaths(Path);
            DraftId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
            ProjectPaths = new DraftProjectPaths(Paths.DraftsRoot, DraftId);
        }

        public string Path { get; }

        public SkinStoragePaths Paths { get; }

        public Guid DraftId { get; }

        public DraftProjectPaths ProjectPaths { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

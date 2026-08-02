using System.Text;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Tests.Drafts;

public sealed class CorruptDraftTests
{
    private static readonly Guid DraftId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SkinId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset CreatedAt =
        DateTimeOffset.Parse("2026-08-02T00:00:00Z");

    public static TheoryData<DocumentState, DocumentState, long?, bool, string[]> OpenMatrix =>
        new()
        {
            { DocumentState.ValidNamedRevision5, DocumentState.Missing, 5, false, [] },
            { DocumentState.Missing, DocumentState.ValidRecoveryRevision7, 7, true, [] },
            { DocumentState.ValidNamedRevision5, DocumentState.ValidRecoveryRevision7, 7, true, [] },
            { DocumentState.ValidNamedRevision5, DocumentState.ValidRecoveryRevision5, 5, false, [] },
            { DocumentState.Corrupt, DocumentState.ValidRecoveryRevision7, 7, true, ["draft.json"] },
            { DocumentState.ValidNamedRevision5, DocumentState.Corrupt, 5, false, ["recovery.json"] },
            { DocumentState.Corrupt, DocumentState.Corrupt, null, false, ["draft.json", "recovery.json"] },
            { DocumentState.MissingProject, DocumentState.MissingProject, null, false, ["draft.not-found"] }
        };

    [Theory]
    [MemberData(nameof(OpenMatrix))]
    public void LoadForOpen_UsesRevisionMatrixAndPreservesEveryByteTimestampAndEntry(
        DocumentState namedState,
        DocumentState recoveryState,
        long? expectedRevision,
        bool expectedRecovered,
        string[] expectedFailureLeavesOrCode)
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var project = new DraftProjectPaths(storage.DraftsRoot, DraftId);
        if (namedState != DocumentState.MissingProject)
        {
            Directory.CreateDirectory(project.AssetsRoot);
            File.WriteAllBytes(
                System.IO.Path.Combine(project.AssetsRoot, "sentinel.bin"),
                [0, 1, 2, 255]);
            File.WriteAllText(
                System.IO.Path.Combine(project.ProjectRoot, ".unrelated.tmp"),
                "keep me",
                Encoding.UTF8);
            WriteState(project.NamedDraftPath, namedState);
            WriteState(project.RecoveryPath, recoveryState);
            if (File.Exists(project.NamedDraftPath))
            {
                File.SetLastWriteTimeUtc(
                    project.NamedDraftPath,
                    new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc));
            }

            if (File.Exists(project.RecoveryPath))
            {
                File.SetLastWriteTimeUtc(
                    project.RecoveryPath,
                    new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc));
            }
        }

        var before = Capture(storage.DraftsRoot);
        var result = new DraftStore(storage).LoadForOpen(DraftId);
        var after = Capture(storage.DraftsRoot);

        Assert.Equal(expectedRevision, result.Document?.Revision);
        Assert.Equal(expectedRecovered, result.WasRecovered);
        if (namedState == DocumentState.MissingProject)
        {
            var failure = Assert.Single(result.Failures);
            Assert.Equal("draft.not-found", failure.ErrorCode);
            Assert.Equal(DraftId, failure.DraftId);
        }
        else
        {
            Assert.Equal(expectedFailureLeavesOrCode, result.Failures.Select(f => f.LeafName));
            Assert.All(result.Failures, failure =>
            {
                Assert.Equal(DraftId, failure.DraftId);
                Assert.Equal("draft.corrupt", failure.ErrorCode);
                Assert.DoesNotContain(storage.DraftsRoot, failure.Message, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("secret-raw-json", failure.Message, StringComparison.OrdinalIgnoreCase);
            });
        }

        AssertSnapshotsEqual(before, after);
    }

    [Fact]
    public void LoadForOpen_RejectsReparseDocumentWithoutReadingOutsideProject()
    {
        using var temporary = new TemporaryDirectory();
        var storage = new SkinStoragePaths(temporary.Path);
        var project = new DraftProjectPaths(storage.DraftsRoot, DraftId);
        var files = new ReparseDocumentFileOperations(project);

        var result = new DraftStore(storage, files).LoadForOpen(DraftId);

        Assert.Null(result.Document);
        var failure = Assert.Single(result.Failures);
        Assert.Equal("draft.json", failure.LeafName);
        Assert.Equal("draft.path-unsafe", failure.ErrorCode);
        Assert.Equal(0, files.ReadCount);
    }

    private static void WriteState(string path, DocumentState state)
    {
        switch (state)
        {
            case DocumentState.Missing:
                return;
            case DocumentState.ValidNamedRevision5:
            case DocumentState.ValidRecoveryRevision5:
                File.WriteAllBytes(path, DraftJsonCodec.Write(Draft(5)));
                return;
            case DocumentState.ValidRecoveryRevision7:
                File.WriteAllBytes(path, DraftJsonCodec.Write(Draft(7)));
                return;
            case DocumentState.Corrupt:
                File.WriteAllText(
                    path,
                    "{ \"secret-raw-json\": \"C:\\\\private\\\\draft\"",
                    Encoding.UTF8);
                return;
            case DocumentState.MissingProject:
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(state));
        }
    }

    private static SkinDraftDocument Draft(long revision) =>
        SkinDraftFactory.CreateNew(
            DraftId,
            SkinId,
            CreatedAt,
            SemanticVersion.Parse("1.1.1")) with
        {
            Revision = revision,
            UpdatedAtUtc = CreatedAt.AddMinutes(revision)
        };

    private static StorageSnapshot Capture(string draftsRoot)
    {
        if (!Directory.Exists(draftsRoot))
        {
            return new StorageSnapshot(false, []);
        }

        var entries = Directory.EnumerateFileSystemEntries(
                draftsRoot,
                "*",
                SearchOption.AllDirectories)
            .Select(path =>
            {
                var relative = System.IO.Path.GetRelativePath(draftsRoot, path);
                return Directory.Exists(path)
                    ? new EntrySnapshot(
                        relative,
                        true,
                        Directory.GetLastWriteTimeUtc(path).Ticks,
                        [])
                    : new EntrySnapshot(
                        relative,
                        false,
                        File.GetLastWriteTimeUtc(path).Ticks,
                        File.ReadAllBytes(path));
            })
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToArray();
        return new StorageSnapshot(true, entries);
    }

    private static void AssertSnapshotsEqual(
        StorageSnapshot expected,
        StorageSnapshot actual)
    {
        Assert.Equal(expected.RootExists, actual.RootExists);
        Assert.Equal(expected.Entries.Count, actual.Entries.Count);
        for (var index = 0; index < expected.Entries.Count; index++)
        {
            Assert.Equal(expected.Entries[index].RelativePath, actual.Entries[index].RelativePath);
            Assert.Equal(expected.Entries[index].IsDirectory, actual.Entries[index].IsDirectory);
            Assert.Equal(expected.Entries[index].LastWriteTicks, actual.Entries[index].LastWriteTicks);
            Assert.Equal(expected.Entries[index].Bytes, actual.Entries[index].Bytes);
        }
    }

    public enum DocumentState
    {
        MissingProject,
        Missing,
        ValidNamedRevision5,
        ValidRecoveryRevision5,
        ValidRecoveryRevision7,
        Corrupt
    }

    private sealed record EntrySnapshot(
        string RelativePath,
        bool IsDirectory,
        long LastWriteTicks,
        byte[] Bytes);

    private sealed record StorageSnapshot(
        bool RootExists,
        IReadOnlyList<EntrySnapshot> Entries);

    private sealed class ReparseDocumentFileOperations : IDraftFileOperations
    {
        private readonly DraftProjectPaths _project;

        public ReparseDocumentFileOperations(DraftProjectPaths project) => _project = project;

        public int ReadCount { get; private set; }

        public void CreateDirectory(string path) => throw new InvalidOperationException();

        public bool DirectoryExists(string path) =>
            string.Equals(path, _project.ProjectRoot, StringComparison.OrdinalIgnoreCase);

        public bool FileExists(string path) =>
            string.Equals(path, _project.NamedDraftPath, StringComparison.OrdinalIgnoreCase);

        public FileAttributes GetAttributes(string path) =>
            string.Equals(path, _project.NamedDraftPath, StringComparison.OrdinalIgnoreCase)
                ? FileAttributes.ReparsePoint
                : FileAttributes.Directory;

        public IEnumerable<string> EnumerateDirectories(string path) => [];

        public byte[] ReadAllBytes(string path)
        {
            ReadCount++;
            throw new InvalidOperationException("A reparse document must never be read.");
        }

        public Task WriteAndFlushAsync(
            string path,
            ReadOnlyMemory<byte> bytes,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException();

        public void ReplaceFile(string sourcePath, string destinationPath) =>
            throw new InvalidOperationException();

        public void DeleteFile(string path) => throw new InvalidOperationException();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CodexQuotaHud-Task12-corrupt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

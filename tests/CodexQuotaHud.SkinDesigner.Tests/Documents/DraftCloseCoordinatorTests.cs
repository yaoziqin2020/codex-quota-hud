using CodexQuotaHud.SkinDesigner.Documents;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Tests.Documents;

public sealed class DraftCloseCoordinatorTests
{
    [Fact]
    public async Task NoUnsavedChanges_ClosesWithoutDialogOrStorageMutation()
    {
        using var temporary = new TemporaryDirectory();
        var context = CreateContext(temporary);

        var allowed = await context.Coordinator.RequestCloseAsync();

        Assert.True(allowed);
        Assert.Equal(0, context.Dialog.ShowCount);
        Assert.False(Directory.Exists(context.Paths.DraftsRoot));
        await context.Recovery.DisposeAsync();
    }

    [Fact]
    public async Task Save_WritesNamedDraftMarksBaselineAndAllowsClose()
    {
        using var temporary = new TemporaryDirectory();
        var context = CreateContext(temporary, UnsavedCloseChoice.Save);
        Assert.True(context.Session.Apply(draft => draft with
        {
            DisplayName = "Saved document"
        }));

        var allowed = await context.Coordinator.RequestCloseAsync();

        Assert.True(allowed);
        Assert.False(context.Session.HasUnsavedChanges);
        var opened = context.Store.LoadForOpen(context.Session.Current.DraftId);
        Assert.Equal("Saved document", opened.Document?.DisplayName);
        Assert.False(opened.WasRecovered);
        await context.Recovery.DisposeAsync();
    }

    [Fact]
    public async Task Save_WhenValidationFailsKeepsWindowOpenAndRecoveryEvidence()
    {
        using var temporary = new TemporaryDirectory();
        var context = CreateContext(temporary, UnsavedCloseChoice.Save);
        Assert.True(context.Session.Apply(draft => draft with
        {
            DisplayName = "Valid recovery"
        }));
        await context.Store.SaveRecoveryAsync(context.Session.Current);
        Assert.True(context.Session.Apply(draft => draft with
        {
            Theme = draft.Theme with { RingThickness = 17 }
        }));
        var recoveryPath = Project(context).RecoveryPath;
        var evidence = await File.ReadAllBytesAsync(recoveryPath);

        var allowed = await context.Coordinator.RequestCloseAsync();

        Assert.False(allowed);
        Assert.True(context.Session.HasUnsavedChanges);
        Assert.Equal(evidence, await File.ReadAllBytesAsync(recoveryPath));
        Assert.Contains(context.Coordinator.Errors,
            error => error.Code == "number.out-of-range");
        await context.Recovery.DisposeAsync();
    }

    [Fact]
    public async Task Save_WhenAtomicWriteFailsKeepsRecoveryAndUnsavedState()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temporary = new TemporaryDirectory();
        var context = CreateContext(temporary, UnsavedCloseChoice.Save);
        await context.Store.SaveNamedAsync(context.Session.Current);
        Assert.True(context.Session.Apply(draft => draft with
        {
            DisplayName = "Blocked save"
        }));
        await context.Store.SaveRecoveryAsync(context.Session.Current);
        var project = Project(context);
        var recoveryEvidence = await File.ReadAllBytesAsync(project.RecoveryPath);
        await using var locked = new FileStream(
            project.NamedDraftPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        var allowed = await context.Coordinator.RequestCloseAsync();

        Assert.False(allowed);
        Assert.True(context.Session.HasUnsavedChanges);
        Assert.Equal(recoveryEvidence,
            await File.ReadAllBytesAsync(project.RecoveryPath));
        Assert.NotEmpty(context.Coordinator.Errors);
        await context.Recovery.DisposeAsync();
    }

    [Fact]
    public async Task Discard_DeletesOnlyExpectedValidRecoveryAndKeepsNamedAndOtherDraft()
    {
        using var temporary = new TemporaryDirectory();
        var context = CreateContext(temporary, UnsavedCloseChoice.Discard);
        await context.Store.SaveNamedAsync(context.Session.Current);
        Assert.True(context.Session.Apply(draft => draft with
        {
            DisplayName = "Discard me"
        }));
        await context.Store.SaveRecoveryAsync(context.Session.Current);
        var other = SkinDraftFactory.CreateNew(
            Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
            SemanticVersion.Parse("1.1.1")) with { Revision = 7 };
        await context.Store.SaveRecoveryAsync(other);

        var allowed = await context.Coordinator.RequestCloseAsync();

        Assert.True(allowed);
        Assert.True(File.Exists(Project(context).NamedDraftPath));
        Assert.False(File.Exists(Project(context).RecoveryPath));
        Assert.True(File.Exists(new DraftProjectPaths(
            context.Paths.DraftsRoot,
            other.DraftId).RecoveryPath));
        await context.Recovery.DisposeAsync();
    }

    [Fact]
    public async Task Discard_WhenPromptCreatesNewerInMemoryRevision_PreservesRecoveryAndRefusesClose()
    {
        using var temporary = new TemporaryDirectory();
        TestContext? context = null;
        context = CreateContext(
            temporary,
            UnsavedCloseChoice.Discard,
            _ => Assert.True(context!.Session.Apply(draft => draft with
            {
                DisplayName = "Edited while prompt was open"
            })));
        Assert.True(context.Session.Apply(draft => draft with
        {
            DisplayName = "Prompt snapshot"
        }));
        await context.Store.SaveRecoveryAsync(context.Session.Current);
        var recoveryPath = Project(context).RecoveryPath;
        var evidence = await File.ReadAllBytesAsync(recoveryPath);

        var allowed = await context.Coordinator.RequestCloseAsync();

        Assert.False(allowed);
        Assert.Equal(evidence, await File.ReadAllBytesAsync(recoveryPath));
        Assert.Contains(context.Coordinator.Errors,
            error => error.Code == "draft.close-stale");
        await context.Recovery.DisposeAsync();
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("newer")]
    public async Task Discard_PreservesCorruptOrNewerRecoveryAndKeepsWindowOpen(
        string scenario)
    {
        using var temporary = new TemporaryDirectory();
        var context = CreateContext(temporary, UnsavedCloseChoice.Discard);
        Assert.True(context.Session.Apply(draft => draft with
        {
            DisplayName = "Prompt snapshot"
        }));
        var project = Project(context);
        Directory.CreateDirectory(project.ProjectRoot);
        byte[] evidence;
        if (scenario == "corrupt")
        {
            evidence = "{broken"u8.ToArray();
            await File.WriteAllBytesAsync(project.RecoveryPath, evidence);
        }
        else
        {
            var newer = context.Session.Current with
            {
                Revision = context.Session.Current.Revision + 1
            };
            await context.Store.SaveRecoveryAsync(newer);
            evidence = await File.ReadAllBytesAsync(project.RecoveryPath);
        }

        var allowed = await context.Coordinator.RequestCloseAsync();

        Assert.False(allowed);
        Assert.Equal(evidence, await File.ReadAllBytesAsync(project.RecoveryPath));
        Assert.Contains(context.Coordinator.Errors,
            error => error.Code == "draft.discard-rejected");
        await context.Recovery.DisposeAsync();
    }

    [Fact]
    public async Task Cancel_LeavesAllStateAndFilesUntouched()
    {
        using var temporary = new TemporaryDirectory();
        var context = CreateContext(temporary, UnsavedCloseChoice.Cancel);
        Assert.True(context.Session.Apply(draft => draft with
        {
            DisplayName = "Do not close"
        }));
        await context.Store.SaveRecoveryAsync(context.Session.Current);
        var recovery = await File.ReadAllBytesAsync(Project(context).RecoveryPath);

        var allowed = await context.Coordinator.RequestCloseAsync();

        Assert.False(allowed);
        Assert.True(context.Session.HasUnsavedChanges);
        Assert.Equal(recovery,
            await File.ReadAllBytesAsync(Project(context).RecoveryPath));
        await context.Recovery.DisposeAsync();
    }

    private static TestContext CreateContext(
        TemporaryDirectory temporary,
        UnsavedCloseChoice choice = UnsavedCloseChoice.Cancel,
        Action<SkinDraftDocument>? onShow = null)
    {
        var paths = new SkinStoragePaths(temporary.Path);
        var store = new DraftStore(paths);
        var recovery = new DraftRecoveryService(
            store,
            (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));
        var now = DateTimeOffset.Parse("2026-08-02T00:00:00Z");
        var session = new SkinDraftSession(
            SkinDraftFactory.CreateNew(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                now,
                SemanticVersion.Parse("1.1.1")),
            () => now = now.AddSeconds(1));
        var dialog = new RecordingDialog(choice, onShow);
        var coordinator = new DraftCloseCoordinator(
            session,
            store,
            recovery,
            dialog);
        return new TestContext(
            paths,
            store,
            recovery,
            session,
            dialog,
            coordinator);
    }

    private static DraftProjectPaths Project(TestContext context) => new(
        context.Paths.DraftsRoot,
        context.Session.Current.DraftId);

    private sealed record TestContext(
        SkinStoragePaths Paths,
        DraftStore Store,
        DraftRecoveryService Recovery,
        SkinDraftSession Session,
        RecordingDialog Dialog,
        DraftCloseCoordinator Coordinator);

    private sealed class RecordingDialog(
        UnsavedCloseChoice choice,
        Action<SkinDraftDocument>? onShow = null)
        : IUnsavedChangesDialog
    {
        public int ShowCount { get; private set; }

        public UnsavedCloseChoice Show(SkinDraftDocument draft)
        {
            ShowCount++;
            onShow?.Invoke(draft);
            return choice;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CodexQuotaHud-Task14-close-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

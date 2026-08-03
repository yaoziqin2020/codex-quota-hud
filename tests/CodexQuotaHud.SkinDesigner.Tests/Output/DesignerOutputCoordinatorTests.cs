using CodexQuotaHud.SkinDesigner.Output;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Tests.Output;

public sealed class DesignerOutputCoordinatorTests
{
    [Fact]
    public async Task ApplyCommand_DisablesBothCommandsRejectsReentryAndRestoresState()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<DesignerOutputResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var applyCalls = 0;
        var exportCalls = 0;
        var dialogs = new RecordingDialogs();
        using var sut = CreateCoordinator(
            dialogs,
            (_, _, _) =>
            {
                applyCalls++;
                entered.TrySetResult();
                return release.Task;
            },
            (_, _, _, _, _) =>
            {
                exportCalls++;
                return Task.FromResult(Exported("unused.cqskin"));
            });

        var first = sut.ApplyCommand.ExecuteAsync();
        await entered.Task;
        var applyReentry = sut.ApplyCommand.ExecuteAsync();
        var exportReentry = sut.ExportCommand.ExecuteAsync();

        Assert.True(sut.IsBusy);
        Assert.False(sut.ApplyCommand.CanExecute(null));
        Assert.False(sut.ExportCommand.CanExecute(null));
        await Task.WhenAll(applyReentry, exportReentry);
        Assert.Equal(1, applyCalls);
        Assert.Equal(0, exportCalls);

        release.SetResult(Applied());
        await first;
        Assert.False(sut.IsBusy);
        Assert.True(sut.ApplyCommand.CanExecute(null));
        Assert.True(sut.ExportCommand.CanExecute(null));
        Assert.Equal(DesignerOutputDisposition.AppliedLive, sut.LastResult?.Disposition);
        Assert.Single(dialogs.Shown);
    }

    [Fact]
    public async Task ExportCommand_CancelledPathSelectionWritesNothing()
    {
        var exportCalls = 0;
        var dialogs = new RecordingDialogs { ExportPath = null };
        using var sut = CreateCoordinator(
            dialogs,
            (_, _, _) => Task.FromResult(Applied()),
            (_, _, _, _, _) =>
            {
                exportCalls++;
                return Task.FromResult(Exported("unused.cqskin"));
            });

        await sut.ExportCommand.ExecuteAsync();

        Assert.Equal(0, exportCalls);
        Assert.Equal(DesignerOutputDisposition.Cancelled, sut.LastResult?.Disposition);
        Assert.Equal("Ocean _ Ring.cqskin", dialogs.SuggestedFileName);
    }

    [Fact]
    public async Task ExportCommand_ExistingDestinationNeedsConfirmationAndPreservesBytesOnCancel()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "CodexQuotaHud-Task15-confirm-" + Guid.NewGuid().ToString("N") +
            ".cqskin");
        var original = "preserve me"u8.ToArray();
        File.WriteAllBytes(path, original);
        try
        {
            var exportCalls = 0;
            var dialogs = new RecordingDialogs
            {
                ExportPath = path,
                ConfirmReplace = false
            };
            using var sut = CreateCoordinator(
                dialogs,
                (_, _, _) => Task.FromResult(Applied()),
                (_, _, _, _, _) =>
                {
                    exportCalls++;
                    return Task.FromResult(Exported(path));
                });

            await sut.ExportCommand.ExecuteAsync();

            Assert.Equal(0, exportCalls);
            Assert.Equal(DesignerOutputDisposition.Cancelled, sut.LastResult?.Disposition);
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ResultStateAndDialogArePublishedOnlyThroughDesignerDispatcher()
    {
        var dispatchEntered = new TaskCompletionSource<Action>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispatch = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogs = new RecordingDialogs();
        using var sut = CreateCoordinator(
            dialogs,
            (_, _, _) => Task.FromResult(Applied()),
            (_, _, _, _, _) => Task.FromResult(Exported("unused.cqskin")),
            async action =>
            {
                dispatchEntered.SetResult(action);
                await releaseDispatch.Task;
                action();
            });

        var execution = sut.ApplyCommand.ExecuteAsync();
        _ = await dispatchEntered.Task;

        Assert.True(sut.IsBusy);
        Assert.Null(sut.LastResult);
        Assert.Empty(dialogs.Shown);

        releaseDispatch.SetResult();
        await execution;
        Assert.False(sut.IsBusy);
        Assert.Equal(DesignerOutputDisposition.AppliedLive, sut.LastResult?.Disposition);
        Assert.Single(dialogs.Shown);
    }

    [Fact]
    public async Task ApplyCommand_CancelPropagatesAndRestoresCommandState()
    {
        var entered = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogs = new RecordingDialogs();
        using var sut = CreateCoordinator(
            dialogs,
            async (_, _, token) =>
            {
                entered.SetResult(token);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return Applied();
            },
            (_, _, _, _, _) => Task.FromResult(Exported("unused.cqskin")));

        var execution = sut.ApplyCommand.ExecuteAsync();
        var token = await entered.Task;
        sut.ApplyCommand.Cancel();

        await execution;
        Assert.True(token.IsCancellationRequested);
        Assert.False(sut.IsBusy);
        Assert.Equal(DesignerOutputDisposition.Cancelled, sut.LastResult?.Disposition);
        Assert.True(sut.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task ApplyCommand_WhenDispatcherFailsPreservesResultAndRestoresBothCommandStates()
    {
        var dialogs = new RecordingDialogs();
        using var sut = CreateCoordinator(
            dialogs,
            (_, _, _) => Task.FromResult(Applied()),
            (_, _, _, _, _) => Task.FromResult(Exported("unused.cqskin")),
            _ => Task.FromException(new InvalidOperationException(
                "Injected dispatcher failure.")));

        await sut.ApplyCommand.ExecuteAsync();

        Assert.False(sut.IsBusy);
        Assert.True(sut.ApplyCommand.CanExecute(null));
        Assert.True(sut.ExportCommand.CanExecute(null));
        Assert.Equal(DesignerOutputDisposition.AppliedLive, sut.LastResult?.Disposition);
        Assert.Empty(dialogs.Shown);
    }

    [Fact]
    public async Task ApplyCommand_WhenPropertyChangedObserverThrowsPreservesResultAndAvailability()
    {
        var dialogs = new RecordingDialogs();
        using var sut = CreateCoordinator(
            dialogs,
            (_, _, _) => Task.FromResult(Applied()),
            (_, _, _, _, _) => Task.FromResult(Exported("unused.cqskin")));
        sut.PropertyChanged += (_, _) =>
            throw new InvalidOperationException("Injected observer failure.");

        await sut.ApplyCommand.ExecuteAsync();

        Assert.False(sut.IsBusy);
        Assert.True(sut.ApplyCommand.CanExecute(null));
        Assert.True(sut.ExportCommand.CanExecute(null));
        Assert.Equal(DesignerOutputDisposition.AppliedLive, sut.LastResult?.Disposition);
        Assert.Single(dialogs.Shown);
    }

    [Fact]
    public async Task ApplyCommand_WhenResultDialogThrowsPreservesResultAndAvailability()
    {
        var dialogs = new RecordingDialogs { ThrowOnShow = true };
        using var sut = CreateCoordinator(
            dialogs,
            (_, _, _) => Task.FromResult(Applied()),
            (_, _, _, _, _) => Task.FromResult(Exported("unused.cqskin")));

        await sut.ApplyCommand.ExecuteAsync();

        Assert.False(sut.IsBusy);
        Assert.True(sut.ApplyCommand.CanExecute(null));
        Assert.True(sut.ExportCommand.CanExecute(null));
        Assert.Equal(DesignerOutputDisposition.AppliedLive, sut.LastResult?.Disposition);
    }

    private static DesignerOutputCoordinator CreateCoordinator(
        ISkinOutputDialogs dialogs,
        Func<
            CodexQuotaHud.SkinDesigner.Drafts.SkinDraftDocument,
            IReadOnlyDictionary<SkinAssetSlot, SkinAsset>,
            CancellationToken,
            Task<DesignerOutputResult>> apply,
        Func<
            CodexQuotaHud.SkinDesigner.Drafts.SkinDraftDocument,
            IReadOnlyDictionary<SkinAssetSlot, SkinAsset>,
            string,
            bool,
            CancellationToken,
            Task<DesignerOutputResult>> export,
        Func<Action, Task>? dispatch = null)
    {
        var draft = OutputTestFixture.CompleteDraft();
        var assets = OutputTestFixture.Assets();
        return new DesignerOutputCoordinator(
            () => draft,
            () => assets,
            apply,
            export,
            dialogs,
            dispatch);
    }

    private static DesignerOutputResult Applied() =>
        new(
            DesignerOutputDisposition.AppliedLive,
            Installed: null,
            ExportPath: null,
            Errors: [],
            Message: "Applied.");

    private static DesignerOutputResult Exported(string path) =>
        new(
            DesignerOutputDisposition.Exported,
            Installed: null,
            ExportPath: path,
            Errors: [],
            Message: "Exported.");

    private sealed class RecordingDialogs : ISkinOutputDialogs
    {
        public string? ExportPath { get; init; }

        public bool ConfirmReplace { get; init; }

        public bool ThrowOnShow { get; init; }

        public string? SuggestedFileName { get; private set; }

        public List<DesignerOutputResult> Shown { get; } = [];

        public string? ChooseExportPath(string suggestedFileName)
        {
            SuggestedFileName = suggestedFileName;
            return ExportPath;
        }

        public bool ConfirmExportReplace(string destinationPath) => ConfirmReplace;

        public SkinCollisionDecision ChooseApplyCollision(SkinInstallPreview preview) =>
            SkinCollisionDecision.Cancel;

        public void ShowResult(DesignerOutputResult result)
        {
            if (ThrowOnShow)
            {
                throw new InvalidOperationException("Injected dialog failure.");
            }

            Shown.Add(result);
        }
    }
}

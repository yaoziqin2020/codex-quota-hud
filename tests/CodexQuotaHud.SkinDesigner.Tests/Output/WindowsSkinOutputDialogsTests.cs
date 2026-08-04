using System.Windows;
using System.Windows.Threading;
using CodexQuotaHud.SkinDesigner.Output;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Tests.Output;

public sealed class WindowsSkinOutputDialogsTests
{
    [Fact]
    public async Task ChooseExportPath_RunsOnExactDesignerOwnerDispatcherFromWorkerThread()
    {
        const string suggested = "Ocean Ring.cqskin";
        var expectedSuggestion = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Codex Quota HUD Skins",
            suggested);
        var selected = expectedSuggestion;
        var ready = new TaskCompletionSource<DialogFixture>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var owner = new Window();
            var dispatcher = owner.Dispatcher;
            var actions = new WindowsSkinOutputDialogActions(
                (actualOwner, actualSuggested) =>
                {
                    Assert.Same(owner, actualOwner);
                    Assert.True(dispatcher.CheckAccess());
                    Assert.Equal(expectedSuggestion, actualSuggested);
                    return selected;
                },
                (_, _) => throw new InvalidOperationException(),
                (_, _) => throw new InvalidOperationException(),
                (_, _, _) => throw new InvalidOperationException());
            ready.SetResult(new DialogFixture(
                dispatcher,
                new WindowsSkinOutputDialogs(() => owner, actions)));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var fixture = await ready.Task.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            var actual = await Task.Run(() =>
                fixture.Dialogs.ChooseExportPath(suggested));

            Assert.Equal(selected, actual);
        }
        finally
        {
            fixture.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            Assert.True(thread.Join(TimeSpan.FromSeconds(2)));
        }
    }

    [Fact]
    public async Task ConfirmExportReplace_RunsOnExactDesignerOwnerDispatcherFromWorkerThread()
    {
        const string destination = @"C:\exports\Ocean Ring.cqskin";
        var ready = new TaskCompletionSource<DialogFixture>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var owner = new Window();
            var dispatcher = owner.Dispatcher;
            var actions = new WindowsSkinOutputDialogActions(
                (_, _) => throw new InvalidOperationException(),
                (actualOwner, actualDestination) =>
                {
                    Assert.Same(owner, actualOwner);
                    Assert.True(dispatcher.CheckAccess());
                    Assert.Equal(destination, actualDestination);
                    return true;
                },
                (_, _) => throw new InvalidOperationException(),
                (_, _, _) => throw new InvalidOperationException());
            ready.SetResult(new DialogFixture(
                dispatcher,
                new WindowsSkinOutputDialogs(() => owner, actions)));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var fixture = await ready.Task.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            var confirmed = await Task.Run(() =>
                fixture.Dialogs.ConfirmExportReplace(destination));

            Assert.True(confirmed);
        }
        finally
        {
            fixture.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            Assert.True(thread.Join(TimeSpan.FromSeconds(2)));
        }
    }

    [Fact]
    public async Task CollisionChoices_RunOnExactDesignerOwnerDispatcherFromWorkerThread()
    {
        using var root = new TemporaryRoot();
        var packagePath = Path.Combine(root.Path, "preview.cqskin");
        var request = new DraftPackageBuilder(OutputTestFixture.HudVersion).Build(
            OutputTestFixture.CompleteDraft(),
            OutputTestFixture.Assets());
        Assert.True(request.IsValid);
        Assert.True(new SkinPackageWriter().WriteFile(
            packagePath,
            request.Value!,
            overwrite: false,
            CancellationToken.None).IsValid);
        var inspected = new SkinPackageInstaller(
            root.Paths,
            OutputTestFixture.HudVersion).Inspect(
                packagePath,
                OutputTestFixture.HudVersion,
                CancellationToken.None);
        var preview = Assert.IsType<SkinInstallPreview>(inspected.Value);
        var ready = new TaskCompletionSource<DialogFixture>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var owner = new Window();
            var dispatcher = owner.Dispatcher;
            var choices = new Queue<SkinCollisionDecision>(
            [
                SkinCollisionDecision.Replace,
                SkinCollisionDecision.KeepCopy,
                SkinCollisionDecision.Cancel
            ]);
            var actions = new WindowsSkinOutputDialogActions(
                (_, _) => null,
                (_, _) => false,
                (actualOwner, _) =>
                {
                    Assert.Same(owner, actualOwner);
                    Assert.True(dispatcher.CheckAccess());
                    return choices.Dequeue();
                },
                (_, _, _) => { });
            ready.SetResult(new DialogFixture(
                dispatcher,
                new WindowsSkinOutputDialogs(() => owner, actions)));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var fixture = await ready.Task.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            var observed = new List<SkinCollisionDecision>();
            foreach (var expected in new[]
                     {
                         SkinCollisionDecision.Replace,
                         SkinCollisionDecision.KeepCopy,
                         SkinCollisionDecision.Cancel
                     })
            {
                var actual = await Task.Run(() =>
                    fixture.Dialogs.ChooseApplyCollision(preview));
                observed.Add(actual);
                Assert.Equal(expected, actual);
            }

            Assert.Equal(
                [
                    SkinCollisionDecision.Replace,
                    SkinCollisionDecision.KeepCopy,
                    SkinCollisionDecision.Cancel
                ],
                observed);
        }
        finally
        {
            fixture.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            Assert.True(thread.Join(TimeSpan.FromSeconds(2)));
        }
    }

    [Fact]
    public async Task CommittedResultWithCleanupError_UsesWarningPresentationOnOwnerDispatcher()
    {
        var ready = new TaskCompletionSource<WarningFixture>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var owner = new Window();
            var dispatcher = owner.Dispatcher;
            MessageBoxImage? observed = null;
            var actions = new WindowsSkinOutputDialogActions(
                (_, _) => null,
                (_, _) => false,
                (_, _) => SkinCollisionDecision.Cancel,
                (actualOwner, _, image) =>
                {
                    Assert.Same(owner, actualOwner);
                    Assert.True(dispatcher.CheckAccess());
                    observed = image;
                });
            ready.SetResult(new WarningFixture(
                dispatcher,
                new WindowsSkinOutputDialogs(() => owner, actions),
                () => observed));
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var fixture = await ready.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var result = new DesignerOutputResult(
            DesignerOutputDisposition.AppliedLive,
            null,
            null,
            [new CodexQuotaHud.Skins.Contracts.SkinValidationError(
                "apply.cleanup-failed",
                "$operation",
                "Cleanup failed.")],
            "Installed, but cleanup failed.");

        try
        {
            await Task.Run(() => fixture.Dialogs.ShowResult(result));
            Assert.Equal(MessageBoxImage.Warning, fixture.GetObservedImage());
        }
        finally
        {
            fixture.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            Assert.True(thread.Join(TimeSpan.FromSeconds(2)));
        }
    }

    private sealed record DialogFixture(
        Dispatcher Dispatcher,
        WindowsSkinOutputDialogs Dialogs);

    private sealed record WarningFixture(
        Dispatcher Dispatcher,
        WindowsSkinOutputDialogs Dialogs,
        Func<MessageBoxImage?> GetObservedImage);

    private sealed class TemporaryRoot : IDisposable
    {
        public TemporaryRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CodexQuotaHud-Task15-dialogs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            Paths = new SkinStoragePaths(Path);
        }

        public string Path { get; }

        public SkinStoragePaths Paths { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

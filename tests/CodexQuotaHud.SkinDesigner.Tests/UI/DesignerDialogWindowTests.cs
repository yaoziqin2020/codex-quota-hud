using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CodexQuotaHud.SkinDesigner.Tests.Preview;
using CodexQuotaHud.SkinDesigner.UI.Dialogs;

namespace CodexQuotaHud.SkinDesigner.Tests.UI;

[Collection(DesignerPreviewWpfCollection.Name)]
public sealed class DesignerDialogWindowTests
{
    [Fact]
    public void Window_RendersOneRequestedActionWithItsStableId()
    {
        RunSta(() =>
        {
            var dialog = CreateDialog(new DesignerDialogAction("ok", "OK", true));

            var action = Assert.Single(Actions(dialog));

            Assert.Equal("ok", action.Tag);
            Assert.Equal("OK", action.Content);
            Assert.True(action.IsDefault);
        });
    }

    [Fact]
    public void Window_RendersTwoRequestedActionsWithTheirStableIds()
    {
        RunSta(() =>
        {
            var dialog = CreateDialog(
                new DesignerDialogAction("replace", "Replace", true),
                new DesignerDialogAction("cancel", "Cancel", IsCancel: true));

            var actions = Actions(dialog);

            Assert.Collection(
                actions,
                action => Assert.Equal("replace", action.Tag),
                action => Assert.Equal("cancel", action.Tag));
        });
    }

    [Fact]
    public void Window_RendersThreeRequestedActionsWithTheirStableIds()
    {
        RunSta(() =>
        {
            var dialog = CreateDialog(
                new DesignerDialogAction("save", "Save", true),
                new DesignerDialogAction("discard", "Discard"),
                new DesignerDialogAction("cancel", "Keep editing", IsCancel: true));

            var actions = Actions(dialog);

            Assert.Collection(
                actions,
                action => Assert.Equal("save", action.Tag),
                action => Assert.Equal("discard", action.Tag),
                action => Assert.Equal("cancel", action.Tag));
        });
    }

    [Fact]
    public void Window_PreservesExplicitDefaultAndCancelDesignations()
    {
        RunSta(() =>
        {
            var dialog = CreateDialog(
                new DesignerDialogAction("replace", "Replace", true),
                new DesignerDialogAction("copy", "Keep copy"),
                new DesignerDialogAction("cancel", "Cancel", IsCancel: true));

            var actions = Actions(dialog);

            Assert.True(Action(actions, "replace").IsDefault);
            Assert.False(Action(actions, "copy").IsDefault);
            Assert.True(Action(actions, "cancel").IsCancel);
            Assert.False(Action(actions, "copy").IsCancel);
        });
    }

    [Fact]
    public void Window_EnterChoosesTheExplicitDefaultAction()
    {
        RunSta(() =>
        {
            var dialog = CreateDialog(
                new DesignerDialogAction("replace", "Replace", true),
                new DesignerDialogAction("cancel", "Cancel", IsCancel: true));
            dialog.Show();

            Press(dialog, Key.Enter);

            Assert.Equal("replace", dialog.SelectedActionId);
        });
    }

    [Theory]
    [InlineData("copy")]
    [InlineData("cancel")]
    public void Window_EnterChoosesEachFocusedNonDefaultAction(string actionId)
    {
        RunSta(() =>
        {
            var dialog = CreateDialog(
                new DesignerDialogAction("replace", "Replace", true),
                new DesignerDialogAction("copy", "Keep copy"),
                new DesignerDialogAction("cancel", "Cancel", IsCancel: true));
            dialog.Show();
            var focused = Action(Actions(dialog), actionId);
            Assert.True(focused.Focus());
            Assert.True(focused.IsKeyboardFocusWithin);

            Press(dialog, Key.Enter);

            Assert.Equal(actionId, dialog.SelectedActionId);
        });
    }

    [Fact]
    public void Window_EscapeChoosesTheExplicitCancelAction()
    {
        RunSta(() =>
        {
            var dialog = CreateDialog(
                new DesignerDialogAction("replace", "Replace", true),
                new DesignerDialogAction("cancel", "Cancel", IsCancel: true));
            dialog.Show();

            Press(dialog, Key.Escape);

            Assert.Equal("cancel", dialog.SelectedActionId);
        });
    }

    [Fact]
    public void Window_TitleBarCloseChoosesTheExplicitCancelAction()
    {
        RunSta(() =>
        {
            var dialog = CreateDialog(
                new DesignerDialogAction("replace", "Replace", true),
                new DesignerDialogAction("cancel", "Cancel", IsCancel: true));
            dialog.Show();

            dialog.Close();

            Assert.Equal("cancel", dialog.SelectedActionId);
        });
    }

    [Fact]
    public void Window_TitleBarCloseWithoutCancelChoosesTheLastAction()
    {
        RunSta(() =>
        {
            var dialog = CreateDialog(
                new DesignerDialogAction("replace", "Replace", true),
                new DesignerDialogAction("copy", "Keep copy"));
            dialog.Show();

            dialog.Close();

            Assert.Equal("copy", dialog.SelectedActionId);
        });
    }

    [Fact]
    public void Window_CentersOverItsOwner()
    {
        RunSta(() =>
        {
            var owner = new Window();
            owner.Show();
            try
            {
                var dialog = new DesignerDialogWindow(
                    owner,
                    new DesignerDialogRequest(
                        "Replace package",
                        "A package already exists.",
                        DesignerDialogIcon.Question,
                        [new DesignerDialogAction("cancel", "Cancel", IsCancel: true)]));

                Assert.Same(owner, dialog.Owner);
                Assert.Equal(WindowStartupLocation.CenterOwner, dialog.WindowStartupLocation);
            }
            finally
            {
                owner.Close();
            }
        });
    }

    [Fact]
    public void Window_UsesTheSharedDesignerThemeResources()
    {
        RunSta(() =>
        {
            var dialog = CreateDialog(new DesignerDialogAction("ok", "OK", true));
            var action = Assert.Single(Actions(dialog));
            action.ApplyTemplate();

            Assert.Same(dialog.FindResource("DesignerSurfaceBrush"), dialog.Background);
            Assert.Same(dialog.FindResource("DesignerTextBrush"), dialog.Foreground);
            Assert.Same(dialog.FindResource("DesignerRaisedBrush"), action.Background);
            Assert.Same(dialog.FindResource("DesignerBorderBrush"), action.BorderBrush);
            Assert.NotNull(action.Template.FindName("DesignerButtonBorder", action));
        });
    }

    [Fact]
    public void Window_ProvidesAccessibleNamesForDialogMessageAndActions()
    {
        RunSta(() =>
        {
            var dialog = CreateDialog(
                new DesignerDialogAction("replace", "Replace", true),
                new DesignerDialogAction("cancel", "Cancel", IsCancel: true));
            var message = Assert.IsType<TextBlock>(dialog.FindName("DialogMessage"));

            Assert.Equal("Replace package", AutomationProperties.GetName(dialog));
            Assert.Equal("A package already exists.", AutomationProperties.GetName(message));
            Assert.Equal("Replace", AutomationProperties.GetName(Action(Actions(dialog), "replace")));
            Assert.Equal("Cancel", AutomationProperties.GetName(Action(Actions(dialog), "cancel")));
        });
    }

    [Fact]
    public void Window_WrapsLongMessages()
    {
        RunSta(() =>
        {
            var dialog = new DesignerDialogWindow(
                owner: null,
                new DesignerDialogRequest(
                    "Validation failed",
                    new string('x', 1_024),
                    DesignerDialogIcon.Error,
                    [new DesignerDialogAction("ok", "OK", true)]));
            var message = Assert.IsType<TextBlock>(dialog.FindName("DialogMessage"));

            Assert.Equal(TextWrapping.Wrap, message.TextWrapping);
        });
    }

    [Fact]
    public void Window_RejectsRequestsWithoutActions()
    {
        RunSta(() => Assert.Throws<ArgumentException>(() => new DesignerDialogWindow(
            owner: null,
            new DesignerDialogRequest(
                "Validation failed",
                "No action was supplied.",
                DesignerDialogIcon.Error,
                Array.Empty<DesignerDialogAction>()))));
    }

    [Fact]
    public void Window_RejectsRepeatedActionIds()
    {
        RunSta(() => Assert.Throws<ArgumentException>(() => new DesignerDialogWindow(
            owner: null,
            new DesignerDialogRequest(
                "Validation failed",
                "Action identifiers must be stable and unique.",
                DesignerDialogIcon.Error,
                [
                    new DesignerDialogAction("replace", "Replace", true),
                    new DesignerDialogAction("replace", "Cancel", IsCancel: true)
                ]))));
    }

    [Fact]
    public void Window_RejectsRequestsWithMoreThanThreeActions()
    {
        RunSta(() => Assert.Throws<ArgumentException>(() => CreateDialog(
            new DesignerDialogAction("one", "One"),
            new DesignerDialogAction("two", "Two"),
            new DesignerDialogAction("three", "Three"),
            new DesignerDialogAction("four", "Four"))));
    }

    [Theory]
    [InlineData("   ", "Replace")]
    [InlineData("replace", "   ")]
    public void Window_RejectsActionsWithBlankIdsOrLabels(string id, string label)
    {
        RunSta(() => Assert.Throws<ArgumentException>(() => CreateDialog(
            new DesignerDialogAction(id, label, true))));
    }

    [Fact]
    public void Window_RejectsMultipleDefaultActions()
    {
        RunSta(() => Assert.Throws<ArgumentException>(() => CreateDialog(
            new DesignerDialogAction("replace", "Replace", true),
            new DesignerDialogAction("copy", "Keep copy", true))));
    }

    [Fact]
    public void Window_RejectsMultipleCancelActions()
    {
        RunSta(() => Assert.Throws<ArgumentException>(() => CreateDialog(
            new DesignerDialogAction("replace", "Replace", IsCancel: true),
            new DesignerDialogAction("cancel", "Cancel", IsCancel: true))));
    }

    [Fact]
    public void Service_RejectsMissingRequestsBeforeOpeningAWindow()
    {
        RunSta(() =>
        {
            IDesignerDialogService service = new DesignerDialogService();

            Assert.Throws<ArgumentNullException>(() => service.Show(null, null!));
        });
    }

    [Fact]
    public async Task Service_ShowsOnTheLiveOwnerDispatcherWhenCalledFromAnotherSta()
    {
        using var host = new OwnerWindowHost();
        var shown = new TaskCompletionSource<DesignerDialogWindow>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ScheduleOwnerDialogClose(host, shown);
        string? result = null;
        Exception? workerFailure = null;
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try
            {
                result = new DesignerDialogService().Show(
                    host.Owner,
                    new DesignerDialogRequest(
                        "Replace package",
                        "A package already exists.",
                        DesignerDialogIcon.Question,
                        [new DesignerDialogAction("replace", "Replace", true)]));
            }
            catch (Exception exception)
            {
                workerFailure = exception;
            }
            finally
            {
                completed.TrySetResult();
            }
        })
        {
            IsBackground = true
        };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(workerFailure);
        var dialog = await shown.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Same(host.Dispatcher, dialog.Dispatcher);
        Assert.Equal("replace", result);
    }

    [Fact]
    public async Task Service_UsesCenteredUnownedDialogWhenOwnerIsNoLongerLoaded()
    {
        using var host = new OwnerWindowHost();
        host.CloseOwner();
        var shown = new TaskCompletionSource<DesignerDialogWindow>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ScheduleUnownedDialogClose(host, shown);
        string? result = null;
        Exception? workerFailure = null;
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try
            {
                result = new DesignerDialogService().Show(
                    host.Owner,
                    new DesignerDialogRequest(
                        "Replace package",
                        "A package already exists.",
                        DesignerDialogIcon.Question,
                        [new DesignerDialogAction("replace", "Replace", true)]));
            }
            catch (Exception exception)
            {
                workerFailure = exception;
            }
            finally
            {
                completed.TrySetResult();
            }
        })
        {
            IsBackground = true
        };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();

        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(workerFailure);
        var dialog = await shown.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Same(host.Dispatcher, dialog.Dispatcher);
        var placement = host.Dispatcher.Invoke(
            () => (dialog.Owner, dialog.WindowStartupLocation));
        Assert.Null(placement.Owner);
        Assert.Equal(WindowStartupLocation.CenterScreen, placement.WindowStartupLocation);
        Assert.Equal("replace", result);
    }

    private static DesignerDialogWindow CreateDialog(
        params DesignerDialogAction[] actions) =>
        new(
            owner: null,
            new DesignerDialogRequest(
                "Replace package",
                "A package already exists.",
                DesignerDialogIcon.Question,
                actions));

    private static Button[] Actions(DesignerDialogWindow dialog) =>
        Assert.IsType<StackPanel>(dialog.FindName("ActionPanel"))
            .Children
            .OfType<Button>()
            .ToArray();

    private static Button Action(IEnumerable<Button> actions, string id) =>
        Assert.Single(actions, action => Equals(action.Tag, id));

    private static void ScheduleOwnerDialogClose(
        OwnerWindowHost host,
        TaskCompletionSource<DesignerDialogWindow> shown)
    {
        void CloseWhenShown()
        {
            try
            {
                var dialog = host.Owner.OwnedWindows
                    .OfType<DesignerDialogWindow>()
                    .SingleOrDefault();
                if (dialog is null)
                {
                    host.Dispatcher.BeginInvoke(
                        DispatcherPriority.ContextIdle,
                        new Action(CloseWhenShown));
                    return;
                }

                shown.TrySetResult(dialog);
                Action(Actions(dialog), "replace").RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception exception)
            {
                shown.TrySetException(exception);
            }
        }

        host.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(CloseWhenShown));
    }

    private static void ScheduleUnownedDialogClose(
        OwnerWindowHost host,
        TaskCompletionSource<DesignerDialogWindow> shown)
    {
        void CloseWhenShown()
        {
            try
            {
                var dialog = PresentationSource.CurrentSources.Cast<PresentationSource>()
                    .Select(source => source.RootVisual)
                    .OfType<DesignerDialogWindow>()
                    .SingleOrDefault();
                if (dialog is null)
                {
                    host.Dispatcher.BeginInvoke(
                        DispatcherPriority.ContextIdle,
                        new Action(CloseWhenShown));
                    return;
                }

                shown.TrySetResult(dialog);
                Action(Actions(dialog), "replace").RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception exception)
            {
                shown.TrySetException(exception);
            }
        }

        host.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(CloseWhenShown));
    }

    private static void Press(DesignerDialogWindow dialog, Key key)
    {
        var source = PresentationSource.FromVisual(dialog);
        Assert.NotNull(source);
        dialog.RaiseEvent(new KeyEventArgs(
            Keyboard.PrimaryDevice,
            source,
            timestamp: 0,
            key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }

    private sealed class OwnerWindowHost : IDisposable
    {
        private readonly ManualResetEventSlim _started = new();
        private readonly Thread _thread;
        private Exception? _startupFailure;

        public OwnerWindowHost()
        {
            _thread = new Thread(() =>
            {
                try
                {
                    Dispatcher = Dispatcher.CurrentDispatcher;
                    Owner = new Window();
                    Owner.Show();
                    _started.Set();
                    Dispatcher.Run();
                }
                catch (Exception exception)
                {
                    _startupFailure = exception;
                    _started.Set();
                }
            })
            {
                IsBackground = true
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            if (!_started.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("The owner STA did not start.");
            }

            if (_startupFailure is not null)
            {
                throw new Xunit.Sdk.XunitException(_startupFailure.ToString());
            }
        }

        public Dispatcher Dispatcher { get; private set; } = null!;

        public Window Owner { get; private set; } = null!;

        public void CloseOwner() => Dispatcher.Invoke(Owner.Close);

        public void Dispose()
        {
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
            {
                Dispatcher.Invoke(() =>
                {
                    if (Owner.IsLoaded)
                    {
                        Owner.Close();
                    }

                    Dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                });
            }

            _thread.Join(TimeSpan.FromSeconds(5));
            _started.Dispose();
        }
    }
}

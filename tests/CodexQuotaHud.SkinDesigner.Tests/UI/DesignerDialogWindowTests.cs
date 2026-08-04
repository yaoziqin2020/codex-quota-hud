using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
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
    public void Service_RejectsMissingRequestsBeforeOpeningAWindow()
    {
        RunSta(() =>
        {
            IDesignerDialogService service = new DesignerDialogService();

            Assert.Throws<ArgumentNullException>(() => service.Show(null, null!));
        });
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
}

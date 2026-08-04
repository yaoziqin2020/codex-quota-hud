using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace CodexQuotaHud.SkinDesigner.UI.Dialogs;

public partial class DesignerDialogWindow : Window
{
    private readonly IReadOnlyList<DesignerDialogAction> _actions;

    internal string? SelectedActionId { get; private set; }

    public DesignerDialogWindow(Window? owner, DesignerDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _actions = Validate(request);

        InitializeComponent();

        Owner = owner;
        WindowStartupLocation = owner is null
            ? WindowStartupLocation.CenterScreen
            : WindowStartupLocation.CenterOwner;
        Title = request.Title;
        DialogTitle.Text = request.Title;
        DialogMessage.Text = request.Message;
        DialogIcon.Text = IconGlyph(request.Icon);
        AutomationProperties.SetName(this, request.Title);
        AutomationProperties.SetName(DialogMessage, request.Message);
        AutomationProperties.SetName(DialogIcon, request.Icon.ToString());

        foreach (var action in _actions)
        {
            var button = new Button
            {
                Content = action.Label,
                Tag = action.Id,
                IsDefault = action.IsDefault,
                IsCancel = action.IsCancel,
                Margin = new Thickness(8, 0, 0, 0),
                MinWidth = 88
            };
            AutomationProperties.SetName(button, action.Label);
            button.Click += (_, _) => Choose(action);
            ActionPanel.Children.Add(button);
        }

        Loaded += (_, _) =>
        {
            var defaultButton = ActionPanel.Children.OfType<Button>()
                .FirstOrDefault(button => button.IsDefault);
            defaultButton?.Focus();
        };
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var action = key switch
        {
            Key.Enter => FocusedAction() ??
                _actions.FirstOrDefault(item => item.IsDefault),
            Key.Escape => CancelOrSafeAction(),
            _ => null
        };
        if (action is null)
        {
            return;
        }

        e.Handled = true;
        Choose(action);
    }

    private DesignerDialogAction? FocusedAction()
    {
        var focusedButton = ActionPanel.Children
            .OfType<Button>()
            .FirstOrDefault(button => button.IsKeyboardFocusWithin);
        return focusedButton?.Tag is string actionId
            ? _actions.FirstOrDefault(action => string.Equals(
                action.Id,
                actionId,
                StringComparison.Ordinal))
            : null;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SelectedActionId ??= CancelOrSafeAction().Id;
    }

    private void Choose(DesignerDialogAction action)
    {
        SelectedActionId = action.Id;
        Close();
    }

    private DesignerDialogAction CancelOrSafeAction() =>
        _actions.FirstOrDefault(action => action.IsCancel) ?? _actions[^1];

    private static IReadOnlyList<DesignerDialogAction> Validate(
        DesignerDialogRequest request)
    {
        if (request.Actions is null || request.Actions.Count is < 1 or > 3)
        {
            throw new ArgumentException(
                "Designer dialogs require one to three actions.",
                nameof(request));
        }

        if (request.Actions.Any(action =>
                action is null ||
                string.IsNullOrWhiteSpace(action.Id) ||
                string.IsNullOrWhiteSpace(action.Label)))
        {
            throw new ArgumentException(
                "Designer dialog actions require non-empty identifiers and labels.",
                nameof(request));
        }

        if (request.Actions.Select(action => action.Id)
            .Distinct(StringComparer.Ordinal).Count() != request.Actions.Count)
        {
            throw new ArgumentException(
                "Designer dialog action identifiers must be unique.",
                nameof(request));
        }

        if (request.Actions.Count(action => action.IsDefault) > 1 ||
            request.Actions.Count(action => action.IsCancel) > 1)
        {
            throw new ArgumentException(
                "Designer dialogs support one default action and one cancel action.",
                nameof(request));
        }

        return request.Actions.ToArray();
    }

    private static string IconGlyph(DesignerDialogIcon icon) => icon switch
    {
        DesignerDialogIcon.Information => "i",
        DesignerDialogIcon.Warning => "!",
        DesignerDialogIcon.Error => "\u00D7",
        DesignerDialogIcon.Question => "?",
        _ => throw new ArgumentOutOfRangeException(nameof(icon), icon, null)
    };
}

using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.App.Infrastructure;

public sealed record AppLaunchRequest(
    bool IsPreview,
    bool IsBackground,
    string? ActivationSelectionKey)
{
    private const string PreviewSwitch = "--preview";
    private const string BackgroundSwitch = "--background";
    private const string ActivateSkinSwitch = "--activate-skin";
    private const int MaximumActivationArgumentLength = 64;
    private const string InvalidArgumentsError = "Invalid launch arguments.";

    public static bool TryParse(
        IReadOnlyList<string> arguments,
        out AppLaunchRequest? request,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var isPreview = false;
        var isBackground = false;
        string? activationSelectionKey = null;
        var activationSwitchSeen = false;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (string.Equals(argument, PreviewSwitch, StringComparison.OrdinalIgnoreCase))
            {
                isPreview = true;
                continue;
            }

            if (string.Equals(argument, BackgroundSwitch, StringComparison.OrdinalIgnoreCase))
            {
                isBackground = true;
                continue;
            }

            if (!string.Equals(
                    argument,
                    ActivateSkinSwitch,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (activationSwitchSeen || ++index >= arguments.Count)
            {
                return Invalid(out request, out error);
            }

            activationSwitchSeen = true;
            var selectionKey = arguments[index];
            if (selectionKey is null ||
                selectionKey.Length > MaximumActivationArgumentLength ||
                !SkinSelectionKey.TryGetCustomId(selectionKey, out _))
            {
                return Invalid(out request, out error);
            }

            activationSelectionKey = selectionKey;
        }

        if (activationSelectionKey is not null && (isPreview || isBackground))
        {
            return Invalid(out request, out error);
        }

        request = new AppLaunchRequest(
            isPreview,
            isBackground,
            activationSelectionKey);
        error = null;
        return true;
    }

    private static bool Invalid(
        out AppLaunchRequest? request,
        out string? error)
    {
        request = null;
        error = InvalidArgumentsError;
        return false;
    }
}

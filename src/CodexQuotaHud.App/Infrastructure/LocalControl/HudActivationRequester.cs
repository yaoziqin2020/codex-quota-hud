using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.App.Infrastructure.LocalControl;

public enum HudActivationDisposition
{
    ActivatedLive,
    StartedHud,
    Rejected,
    Failed
}

public sealed record HudActivationResult(
    HudActivationDisposition Disposition,
    string? ErrorCode,
    string? Message);

public sealed class HudActivationRequester
{
    private readonly Func<LocalControlRequest, CancellationToken, Task<LocalControlResponse>> _send;
    private readonly Func<string, (bool Succeeded, string? Error)> _launch;

    public HudActivationRequester()
        : this(
            new LocalControlClient(LocalControlProtocol.PipeName).SendAsync,
            CreateInstalledLaunch())
    {
    }

    internal HudActivationRequester(
        Func<LocalControlRequest, CancellationToken, Task<LocalControlResponse>> send,
        Func<string, (bool Succeeded, string? Error)> launch)
    {
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _launch = launch ?? throw new ArgumentNullException(nameof(launch));
    }

    public async Task<HudActivationResult> ActivateAsync(
        string selectionKey,
        CancellationToken cancellationToken = default)
    {
        if (selectionKey is null ||
            selectionKey.Length > 64 ||
            !SkinSelectionKey.TryGetCustomId(selectionKey, out _))
        {
            return new HudActivationResult(
                HudActivationDisposition.Rejected,
                LocalControlProtocol.RequestInvalidErrorCode,
                "The activation request is invalid.");
        }

        LocalControlResponse response;
        try
        {
            response = await _send(
                    new LocalControlRequest(
                        LocalControlProtocol.ProtocolVersion,
                        LocalControlCommandKind.ActivateSkin,
                        selectionKey),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new HudActivationResult(
                HudActivationDisposition.Failed,
                "control.timeout",
                "The activation request timed out.");
        }
        catch (Exception)
        {
            return new HudActivationResult(
                HudActivationDisposition.Failed,
                "control.failed",
                "The activation request failed.");
        }

        if (response.Succeeded)
        {
            return new HudActivationResult(
                HudActivationDisposition.ActivatedLive,
                null,
                null);
        }

        if (string.Equals(
                response.ErrorCode,
                "control.unavailable",
                StringComparison.Ordinal))
        {
            var launch = _launch(selectionKey);
            return launch.Succeeded
                ? new HudActivationResult(
                    HudActivationDisposition.StartedHud,
                    null,
                    null)
                : new HudActivationResult(
                    HudActivationDisposition.Failed,
                    "hud.launch.failed",
                    launch.Error ?? "The installed HUD could not be started.");
        }

        var rejected = response.ErrorCode is
            "control.request.invalid" or
            "skin.selection.missing" or
            "skin.activation.failed";
        return new HudActivationResult(
            rejected
                ? HudActivationDisposition.Rejected
                : HudActivationDisposition.Failed,
            response.ErrorCode ?? "control.failed",
            response.Message);
    }

    private static Func<string, (bool Succeeded, string? Error)>
        CreateInstalledLaunch()
    {
        var launcher = new InstalledAppLauncher();
        return selectionKey =>
        {
            var succeeded = launcher.TryLaunchActivation(selectionKey, out var error);
            return (succeeded, error);
        };
    }
}

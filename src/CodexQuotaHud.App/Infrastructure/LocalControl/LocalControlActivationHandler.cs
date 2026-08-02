using CodexQuotaHud.Core.Settings;
using System.Windows.Threading;

namespace CodexQuotaHud.App.Infrastructure.LocalControl;

public sealed class LocalControlActivationHandler
{
    private readonly Func<string, bool> _selectionExists;
    private readonly Func<string, CancellationToken, Task<bool>> _activateOnUiThread;

    public LocalControlActivationHandler(
        Func<string, bool> selectionExists,
        Func<string, CancellationToken, Task<bool>> activateOnUiThread)
    {
        _selectionExists = selectionExists ?? throw new ArgumentNullException(
            nameof(selectionExists));
        _activateOnUiThread = activateOnUiThread ?? throw new ArgumentNullException(
            nameof(activateOnUiThread));
    }

    public async Task<LocalControlResponse> HandleAsync(
        LocalControlRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProtocolVersion != LocalControlProtocol.ProtocolVersion ||
            request.Command != LocalControlCommandKind.ActivateSkin ||
            !SkinSelectionKey.TryGetCustomId(request.SelectionKey, out _))
        {
            return Failure(
                LocalControlProtocol.RequestInvalidErrorCode,
                "The activation request is invalid.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_selectionExists(request.SelectionKey))
            {
                return Failure(
                    "skin.selection.missing",
                    "The requested skin is not available.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var activated = await _activateOnUiThread(
                    request.SelectionKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (activated)
            {
                return new LocalControlResponse(true, null, null);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Failure(
                "skin.activation.failed",
                "The requested skin could not be activated.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failure(
                "control.handler.failed",
                "The activation handler failed.");
        }
    }

    private static LocalControlResponse Failure(string errorCode, string message) =>
        new(false, errorCode, message);

    internal static async Task<bool> InvokeOnDispatcherAsync(
        Dispatcher dispatcher,
        Func<CancellationToken, bool> activate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(activate);
        var operation = dispatcher.InvokeAsync(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return activate(cancellationToken);
            },
            DispatcherPriority.Normal,
            cancellationToken);
        return await operation.Task.ConfigureAwait(false);
    }
}

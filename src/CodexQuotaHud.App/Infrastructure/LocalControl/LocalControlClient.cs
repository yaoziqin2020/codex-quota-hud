using System.IO;
using System.Security;

namespace CodexQuotaHud.App.Infrastructure.LocalControl;

public sealed class LocalControlClient
{
    private readonly string _pipeName;
    private readonly ILocalControlPipeFactory _pipes;

    public LocalControlClient(
        string pipeName,
        ILocalControlPipeFactory? pipes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
        _pipes = pipes ?? new CurrentUserLocalControlPipeFactory();
    }

    public async Task<LocalControlResponse> SendAsync(
        LocalControlRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Stream? connection;
        try
        {
            connection = await _pipes.ConnectAsync(
                    _pipeName,
                    LocalControlProtocol.ConnectTimeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failure("control.timeout", "The local-control connection timed out.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return Failure("control.failed", "The local-control connection failed.");
        }

        if (connection is null)
        {
            return Failure("control.unavailable", "The local-control server is unavailable.");
        }

        await using (connection)
        {
            using var responseDeadline =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            responseDeadline.CancelAfter(LocalControlProtocol.ResponseTimeout);
            try
            {
                await LocalControlProtocol.WriteRequestAsync(
                        connection,
                        request,
                        responseDeadline.Token)
                    .ConfigureAwait(false);
                var response = await LocalControlProtocol.ReadResponseAsync(
                        connection,
                        responseDeadline.Token)
                    .ConfigureAwait(false);
                return IsValidServerResponse(response)
                    ? response
                    : Failure(
                        LocalControlProtocol.ProtocolInvalidErrorCode,
                        "The local-control response is invalid.");
            }
            catch (LocalControlProtocolException exception)
            {
                return Failure(exception.ErrorCode, "The local-control response is invalid.");
            }
            catch (OperationCanceledException)
            {
                return Failure("control.timeout", "The local-control response timed out.");
            }
            catch (Exception exception) when (
                exception is IOException or ObjectDisposedException or InvalidOperationException)
            {
                return Failure("control.failed", "The local-control exchange failed.");
            }
        }
    }

    private static LocalControlResponse Failure(string errorCode, string message) =>
        new(false, errorCode, message);

    private static bool IsValidServerResponse(LocalControlResponse response)
    {
        if (response.Succeeded)
        {
            return response.ErrorCode is null && response.Message is null;
        }

        if (string.IsNullOrWhiteSpace(response.Message) ||
            response.Message.Length > 256)
        {
            return false;
        }

        return response.ErrorCode is
            LocalControlProtocol.ProtocolInvalidErrorCode or
            LocalControlProtocol.RequestInvalidErrorCode or
            "control.timeout" or
            "control.handler.failed" or
            "skin.selection.missing" or
            "skin.activation.failed";
    }
}

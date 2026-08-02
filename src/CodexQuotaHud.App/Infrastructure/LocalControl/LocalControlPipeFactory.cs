using System.IO;
using System.IO.Pipes;

namespace CodexQuotaHud.App.Infrastructure.LocalControl;

public interface ILocalControlPipeFactory
{
    Task<Stream> AcceptAsync(
        string pipeName,
        CancellationToken cancellationToken);

    Task<Stream?> ConnectAsync(
        string pipeName,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed class CurrentUserLocalControlPipeFactory : ILocalControlPipeFactory
{
    private const PipeOptions SecureAsyncOptions =
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

    public async Task<Stream> AcceptAsync(
        string pipeName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Message,
            SecureAsyncOptions);
        try
        {
            await server.WaitForConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            server.ReadMode = PipeTransmissionMode.Message;
            return server;
        }
        catch
        {
            await server.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<Stream?> ConnectAsync(
        string pipeName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMilliseconds(int.MaxValue))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            SecureAsyncOptions);
        try
        {
            await client.ConnectAsync(
                    checked((int)Math.Ceiling(timeout.TotalMilliseconds)),
                    cancellationToken)
                .ConfigureAwait(false);
            client.ReadMode = PipeTransmissionMode.Message;
            return client;
        }
        catch (TimeoutException)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

namespace CodexQuotaHud.App.Infrastructure.LocalControl;

public enum LocalControlCommandKind
{
    ActivateSkin
}

public sealed record LocalControlRequest(
    int ProtocolVersion,
    LocalControlCommandKind Command,
    string SelectionKey);

public sealed record LocalControlResponse(
    bool Succeeded,
    string? ErrorCode,
    string? Message);

public sealed class LocalControlProtocolException : Exception
{
    internal LocalControlProtocolException(string errorCode)
        : base("The local-control message is invalid.")
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

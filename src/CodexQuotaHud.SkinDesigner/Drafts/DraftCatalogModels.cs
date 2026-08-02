using System.IO;

namespace CodexQuotaHud.SkinDesigner.Drafts;

public sealed record DraftLoadFailure(
    Guid? DraftId,
    string LeafName,
    string ErrorCode,
    string Message);

public sealed record DraftPersistenceFailure(
    Guid DraftId,
    string LeafName,
    string ErrorCode,
    string Message);

public sealed record DraftOpenResult(
    SkinDraftDocument? Document,
    bool WasRecovered,
    IReadOnlyList<DraftLoadFailure> Failures);

public sealed record DraftCatalogSnapshot(
    IReadOnlyList<SkinDraftDocument> Healthy,
    IReadOnlyList<DraftLoadFailure> Corrupt);

internal sealed class DraftPersistenceException : IOException
{
    public DraftPersistenceException(
        string errorCode,
        string safeMessage,
        Exception? innerException = null)
        : base($"{errorCode}: {safeMessage}", innerException)
    {
        ErrorCode = errorCode;
        SafeMessage = safeMessage;
    }

    public string ErrorCode { get; }

    public string SafeMessage { get; }
}

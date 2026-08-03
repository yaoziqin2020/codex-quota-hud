using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Validation;

namespace CodexQuotaHud.SkinDesigner.Documents;

public sealed class DraftCloseCoordinator
{
    private readonly SkinDraftSession _session;
    private readonly DraftStore _store;
    private readonly DraftRecoveryService _recovery;
    private readonly IUnsavedChangesDialog _dialog;
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    public DraftCloseCoordinator(
        SkinDraftSession session,
        DraftStore store,
        DraftRecoveryService recovery,
        IUnsavedChangesDialog dialog)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
    }

    public IReadOnlyList<SkinValidationError> Errors { get; private set; } = [];

    public async Task<bool> RequestCloseAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await _requestGate.WaitAsync(0, cancellationToken)
                .ConfigureAwait(false))
        {
            return false;
        }

        try
        {
            Errors = [];
            if (!_session.HasUnsavedChanges)
            {
                return true;
            }

            var snapshot = _session.Current;
            var choice = _dialog.Show(snapshot);
            if (choice == UnsavedCloseChoice.Discard &&
                !IsCurrentSnapshot(snapshot))
            {
                return RejectStaleSnapshot();
            }

            return choice switch
            {
                UnsavedCloseChoice.Save => await SaveAsync(
                    snapshot,
                    cancellationToken).ConfigureAwait(false),
                UnsavedCloseChoice.Discard => await DiscardAsync(
                    snapshot,
                    cancellationToken).ConfigureAwait(false),
                _ => false
            };
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task<bool> SaveAsync(
        SkinDraftDocument snapshot,
        CancellationToken cancellationToken)
    {
        var errors = new List<SkinValidationError>();
        errors.AddRange(SkinContractValidator.ValidateTheme(snapshot.Theme).Errors);
        errors.AddRange(SkinDraftValidator.Validate(snapshot).Errors);
        if (errors.Count > 0)
        {
            Errors = errors;
            return false;
        }

        try
        {
            await _store.SaveNamedAsync(snapshot, cancellationToken)
                .ConfigureAwait(false);
            if (!IsCurrentSnapshot(snapshot))
            {
                return RejectStaleSnapshot();
            }

            _session.MarkNamedSaved();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            Errors =
            [new SkinValidationError(
                "draft.save-failed",
                "$draft",
                "The named draft could not be saved; recovery was preserved.")];
            return false;
        }
    }

    private async Task<bool> DiscardAsync(
        SkinDraftDocument snapshot,
        CancellationToken cancellationToken)
    {
        if (!IsCurrentSnapshot(snapshot))
        {
            return RejectStaleSnapshot();
        }

        try
        {
            await _recovery.DiscardAsync(
                snapshot.DraftId,
                snapshot.Revision,
                cancellationToken).ConfigureAwait(false);
            if (!IsCurrentSnapshot(snapshot))
            {
                await _store.SaveRecoveryAsync(
                    _session.Current,
                    cancellationToken).ConfigureAwait(false);
                return RejectStaleSnapshot();
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            Errors =
            [new SkinValidationError(
                "draft.discard-rejected",
                "$draft.recovery",
                "The recovery is corrupt, newer, or could not be deleted safely.")];
            return false;
        }
    }

    private bool IsCurrentSnapshot(SkinDraftDocument snapshot)
    {
        var current = _session.Current;
        return current.DraftId == snapshot.DraftId &&
            current.Revision == snapshot.Revision;
    }

    private bool RejectStaleSnapshot()
    {
        Errors =
        [new SkinValidationError(
            "draft.close-stale",
            "$draft",
            "The draft changed while the close prompt was open. Review the newer changes and try again.")];
        return false;
    }
}

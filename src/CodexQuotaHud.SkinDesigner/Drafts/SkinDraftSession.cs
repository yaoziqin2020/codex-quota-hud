namespace CodexQuotaHud.SkinDesigner.Drafts;

public sealed class SkinDraftSession
{
    private readonly DraftHistory _history;
    private readonly Func<DateTimeOffset> _utcNow;
    private SkinDraftDocument _current;
    private SkinDraftDocument _namedSavedBaseline;
    private long _nextRevision;

    public SkinDraftSession(
        SkinDraftDocument initial,
        Func<DateTimeOffset> utcNow)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(utcNow);
        _history = new DraftHistory(initial);
        _current = _history.Current;
        _namedSavedBaseline = DraftSnapshot.Clone(_current);
        _nextRevision = initial.Revision;
        _utcNow = utcNow;
    }

    public event EventHandler<SkinDraftDocument>? MeaningfulChange;

    public SkinDraftDocument Current => _current;

    public bool HasUnsavedChanges => !DraftSnapshot.StructuralEquals(
        _current,
        _namedSavedBaseline,
        ignoreBookkeeping: true);

    public bool Apply(Func<SkinDraftDocument, SkinDraftDocument> edit)
        => ApplyCore(edit, requireStructuralChange: true);

    internal bool ApplyMeaningful(
        Func<SkinDraftDocument, SkinDraftDocument> edit)
        => ApplyCore(edit, requireStructuralChange: false);

    private bool ApplyCore(
        Func<SkinDraftDocument, SkinDraftDocument> edit,
        bool requireStructuralChange)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var edited = edit(DraftSnapshot.Clone(_current)) ??
            throw new InvalidOperationException("A draft edit must return a document.");
        if (requireStructuralChange && DraftSnapshot.StructuralEquals(
                _current,
                edited,
                ignoreBookkeeping: true))
        {
            return false;
        }

        var accepted = edited with
        {
            Revision = NextRevision(),
            CreatedAtUtc = _current.CreatedAtUtc,
            UpdatedAtUtc = NextTimestamp()
        };
        if (!_history.Push(accepted))
        {
            return false;
        }

        _current = _history.Current;
        RaiseMeaningfulChange();
        return true;
    }

    public bool TryUndo()
    {
        if (!_history.Undo(out var restored))
        {
            return false;
        }

        _current = Rebase(restored);
        RaiseMeaningfulChange();
        return true;
    }

    public bool TryRedo()
    {
        if (!_history.Redo(out var restored))
        {
            return false;
        }

        _current = Rebase(restored);
        RaiseMeaningfulChange();
        return true;
    }

    public void MarkNamedSaved() =>
        _namedSavedBaseline = DraftSnapshot.Clone(_current);

    private SkinDraftDocument Rebase(SkinDraftDocument restored) =>
        DraftSnapshot.Clone(restored) with
        {
            Revision = NextRevision(),
            CreatedAtUtc = _current.CreatedAtUtc,
            UpdatedAtUtc = NextTimestamp()
        };

    private long NextRevision() => _nextRevision = checked(_nextRevision + 1);

    private DateTimeOffset NextTimestamp()
    {
        var supplied = _utcNow().ToUniversalTime();
        return supplied > _current.UpdatedAtUtc
            ? supplied
            : _current.UpdatedAtUtc.AddTicks(1);
    }

    private void RaiseMeaningfulChange() => MeaningfulChange?.Invoke(this, _current);
}

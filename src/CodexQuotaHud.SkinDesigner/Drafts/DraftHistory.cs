using System.Collections.ObjectModel;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.SkinDesigner.Drafts;

public sealed class DraftHistory
{
    private const int MaximumCapacity = 100;

    private readonly int _capacity;
    private readonly List<SkinDraftDocument> _states;
    private int _index;

    public DraftHistory(SkinDraftDocument initial, int capacity = 100)
    {
        ArgumentNullException.ThrowIfNull(initial);
        if (capacity is < 1 or > MaximumCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                "Draft history capacity must be between 1 and 100.");
        }

        _capacity = capacity;
        _states = [DraftSnapshot.Clone(initial)];
    }

    public SkinDraftDocument Current => _states[_index];

    public int Count => _states.Count;

    public bool CanUndo => _index > 0;

    public bool CanRedo => _index < _states.Count - 1;

    public bool Push(SkinDraftDocument state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (DraftSnapshot.StructuralEquals(Current, state))
        {
            return false;
        }

        if (CanRedo)
        {
            _states.RemoveRange(_index + 1, _states.Count - _index - 1);
        }

        _states.Add(DraftSnapshot.Clone(state));
        _index = _states.Count - 1;
        if (_states.Count > _capacity)
        {
            _states.RemoveAt(0);
            _index--;
        }

        return true;
    }

    public bool Undo(out SkinDraftDocument state)
    {
        if (!CanUndo)
        {
            state = Current;
            return false;
        }

        _index--;
        state = Current;
        return true;
    }

    public bool Redo(out SkinDraftDocument state)
    {
        if (!CanRedo)
        {
            state = Current;
            return false;
        }

        _index++;
        state = Current;
        return true;
    }
}

internal static class DraftSnapshot
{
    public static SkinDraftDocument Clone(SkinDraftDocument draft) =>
        draft with
        {
            Assets = new ReadOnlyDictionary<SkinAssetSlot, DraftAssetReference>(
                draft.Assets.ToDictionary(pair => pair.Key, pair => pair.Value))
        };

    public static bool StructuralEquals(
        SkinDraftDocument left,
        SkinDraftDocument right,
        bool ignoreBookkeeping = false) =>
        left.DraftSchemaVersion == right.DraftSchemaVersion &&
        left.DraftId == right.DraftId &&
        left.SkinId == right.SkinId &&
        (ignoreBookkeeping || left.Revision == right.Revision) &&
        string.Equals(left.ProjectName, right.ProjectName, StringComparison.Ordinal) &&
        string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal) &&
        string.Equals(left.Author, right.Author, StringComparison.Ordinal) &&
        left.PackageVersion == right.PackageVersion &&
        string.Equals(left.Description, right.Description, StringComparison.Ordinal) &&
        left.MinimumHudVersion == right.MinimumHudVersion &&
        left.OriginSkinId == right.OriginSkinId &&
        Equals(left.Theme, right.Theme) &&
        AssetsEqual(left.Assets, right.Assets) &&
        (ignoreBookkeeping || left.CreatedAtUtc == right.CreatedAtUtc) &&
        (ignoreBookkeeping || left.UpdatedAtUtc == right.UpdatedAtUtc);

    private static bool AssetsEqual(
        IReadOnlyDictionary<SkinAssetSlot, DraftAssetReference> left,
        IReadOnlyDictionary<SkinAssetSlot, DraftAssetReference> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) || pair.Value != value)
            {
                return false;
            }
        }

        return true;
    }
}

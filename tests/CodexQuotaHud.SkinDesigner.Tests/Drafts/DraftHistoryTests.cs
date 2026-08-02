using System.Collections.ObjectModel;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.SkinDesigner.Tests.Drafts;

public sealed class DraftHistoryTests
{
    private static readonly Guid DraftId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SkinId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset CreatedAt =
        DateTimeOffset.Parse("2026-08-02T00:00:00Z");

    [Fact]
    public void DefaultCapacity_CountsCurrentAndEvictsRevisionZeroAtOneHundredStates()
    {
        var history = new DraftHistory(Draft(0));

        for (var revision = 1; revision <= 100; revision++)
        {
            Assert.True(history.Push(Draft(revision)));
        }

        Assert.Equal(100, history.Count);
        Assert.Equal(100, history.Current.Revision);
        var undoRevisions = new List<long>();
        while (history.Undo(out var state))
        {
            undoRevisions.Add(state.Revision);
        }

        Assert.Equal(99, undoRevisions.Count);
        Assert.Equal(99, undoRevisions[0]);
        Assert.Equal(1, undoRevisions[^1]);
        Assert.False(history.CanUndo);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(101)]
    public void Constructor_RejectsCapacityOutsideOneThroughOneHundred(int capacity) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DraftHistory(Draft(0), capacity));

    [Fact]
    public void CapacityOne_AlwaysKeepsOnlyCurrentState()
    {
        var history = new DraftHistory(Draft(0), capacity: 1);

        Assert.True(history.Push(Draft(1)));

        Assert.Equal(1, history.Count);
        Assert.Equal(1, history.Current.Revision);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Push_SuppressesStructuralDuplicateAndTruncatesRedoBranch()
    {
        var history = new DraftHistory(Draft(0));
        Assert.True(history.Push(Draft(1)));
        Assert.True(history.Push(Draft(2)));
        Assert.True(history.Undo(out var revisionOne));
        Assert.Equal(1, revisionOne.Revision);
        Assert.True(history.CanRedo);

        var structuralDuplicate = revisionOne with
        {
            Assets = ReadOnly(revisionOne.Assets.ToDictionary(
                pair => pair.Key,
                pair => pair.Value with { }))
        };
        Assert.False(history.Push(structuralDuplicate));
        Assert.True(history.CanRedo);

        var branch = Draft(3) with { DisplayName = "New branch" };
        Assert.True(history.Push(branch));

        Assert.False(history.CanRedo);
        Assert.False(history.Redo(out _));
        Assert.Equal("New branch", history.Current.DisplayName);
    }

    [Fact]
    public void AcceptedSnapshots_CloneAssetDictionariesFromCallerMutation()
    {
        var initialAssets = new Dictionary<SkinAssetSlot, DraftAssetReference>
        {
            [SkinAssetSlot.Background] = new(
                SkinAssetSlot.Background,
                "assets/background.png",
                "background.png")
        };
        var history = new DraftHistory(Draft(0) with { Assets = initialAssets });
        initialAssets.Clear();
        Assert.Single(history.Current.Assets);

        var pushedAssets = new Dictionary<SkinAssetSlot, DraftAssetReference>
        {
            [SkinAssetSlot.Center] = new(
                SkinAssetSlot.Center,
                "assets/center.jpg",
                "center.jpg")
        };
        Assert.True(history.Push(Draft(1) with { Assets = pushedAssets }));
        pushedAssets.Clear();

        Assert.Single(history.Current.Assets);
        Assert.True(history.Current.Assets.ContainsKey(SkinAssetSlot.Center));
        var dictionary = Assert.IsAssignableFrom<
            IDictionary<SkinAssetSlot, DraftAssetReference>>(history.Current.Assets);
        Assert.Throws<NotSupportedException>(() => dictionary.Clear());
    }

    [Fact]
    public void SessionApply_OwnsOneRevisionTimeHistoryNodeAndEvent()
    {
        var times = new Queue<DateTimeOffset>(
        [
            CreatedAt.AddSeconds(1),
            CreatedAt.AddSeconds(2)
        ]);
        var session = new SkinDraftSession(Draft(0), () => times.Dequeue());
        var events = new List<SkinDraftDocument>();
        session.MeaningfulChange += (_, document) => events.Add(document);

        var changed = session.Apply(current => current with
        {
            DisplayName = "Edited",
            Revision = 999,
            CreatedAtUtc = CreatedAt.AddYears(1),
            UpdatedAtUtc = CreatedAt.AddYears(1)
        });
        var equal = session.Apply(current => current with
        {
            Revision = 777,
            UpdatedAtUtc = CreatedAt.AddYears(2)
        });

        Assert.True(changed);
        Assert.False(equal);
        Assert.Equal(1, session.Current.Revision);
        Assert.Equal(CreatedAt, session.Current.CreatedAtUtc);
        Assert.Equal(CreatedAt.AddSeconds(1), session.Current.UpdatedAtUtc);
        Assert.Equal("Edited", session.Current.DisplayName);
        Assert.True(session.HasUnsavedChanges);
        var meaningful = Assert.Single(events);
        Assert.Same(session.Current, meaningful);
        Assert.True(session.TryUndo());
        Assert.False(session.TryUndo());
    }

    [Fact]
    public void SessionUndoRedo_RebasesContentOnMonotonicRecoveryBookkeeping()
    {
        var returnedTime = CreatedAt;
        var session = new SkinDraftSession(Draft(0), () => returnedTime);
        var events = new List<SkinDraftDocument>();
        session.MeaningfulChange += (_, document) => events.Add(document);
        Assert.True(session.Apply(current => current with { DisplayName = "One" }));
        Assert.True(session.Apply(current => current with { DisplayName = "Two" }));

        Assert.True(session.TryUndo());
        Assert.Equal("One", session.Current.DisplayName);
        Assert.True(session.TryRedo());
        Assert.Equal("Two", session.Current.DisplayName);
        Assert.False(session.TryRedo());

        Assert.Equal([1L, 2L, 3L, 4L], events.Select(document => document.Revision));
        Assert.Equal(
            [
                CreatedAt.AddTicks(1),
                CreatedAt.AddTicks(2),
                CreatedAt.AddTicks(3),
                CreatedAt.AddTicks(4)
            ],
            events.Select(document => document.UpdatedAtUtc));
    }

    [Fact]
    public void MarkNamedSaved_UpdatesContentBaselineWithoutClearingHistory()
    {
        var next = CreatedAt;
        var session = new SkinDraftSession(
            Draft(0),
            () => next = next.AddSeconds(1));
        Assert.False(session.HasUnsavedChanges);
        Assert.True(session.Apply(current => current with { DisplayName = "Saved state" }));
        Assert.True(session.HasUnsavedChanges);

        session.MarkNamedSaved();
        Assert.False(session.HasUnsavedChanges);
        Assert.True(session.Apply(current => current with { DisplayName = "Later state" }));
        Assert.True(session.HasUnsavedChanges);

        Assert.True(session.TryUndo());
        Assert.Equal("Saved state", session.Current.DisplayName);
        Assert.False(session.HasUnsavedChanges);
        Assert.True(session.TryRedo());
        Assert.Equal("Later state", session.Current.DisplayName);
        Assert.True(session.HasUnsavedChanges);
    }

    private static SkinDraftDocument Draft(long revision) =>
        SkinDraftFactory.CreateNew(
            DraftId,
            SkinId,
            CreatedAt,
            SemanticVersion.Parse("1.1.1")) with
        {
            Revision = revision,
            UpdatedAtUtc = CreatedAt.AddMinutes(revision)
        };

    private static IReadOnlyDictionary<SkinAssetSlot, DraftAssetReference> ReadOnly(
        IDictionary<SkinAssetSlot, DraftAssetReference> assets) =>
        new ReadOnlyDictionary<SkinAssetSlot, DraftAssetReference>(assets);
}

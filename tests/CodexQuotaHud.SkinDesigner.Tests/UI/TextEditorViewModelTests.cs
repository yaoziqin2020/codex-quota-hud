using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.SkinDesigner.UI;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.SkinDesigner.Tests.UI;

public sealed class TextEditorViewModelTests
{
    private static readonly DateTimeOffset CreatedAt =
        DateTimeOffset.Parse("2026-08-02T00:00:00Z");

    [Fact]
    public void SetTextOffsetY_ChangesOnlyOffsetAndSupportsDirtyUndoRedo()
    {
        using var sut = CreateViewModel(out var session);
        var before = session.Current;

        var result = sut.Text.SetTextOffsetY(-12);

        Assert.True(result.Succeeded, Format(result.Errors));
        AssertOnlyThemeChanged(
            before,
            session.Current,
            before.Theme with { TextOffsetY = -12 });
        Assert.True(session.HasUnsavedChanges);

        Assert.True(session.TryUndo());
        Assert.Equal(before.Theme, session.Current.Theme);
        Assert.False(session.HasUnsavedChanges);

        Assert.True(session.TryRedo());
        Assert.Equal(before.Theme with { TextOffsetY = -12 }, session.Current.Theme);
        Assert.True(session.HasUnsavedChanges);
    }

    [Fact]
    public void SetTextLineGap_ChangesOnlyGapAndSupportsDirtyUndoRedo()
    {
        using var sut = CreateViewModel(out var session);
        var before = session.Current;

        var result = sut.Text.SetTextLineGap(11);

        Assert.True(result.Succeeded, Format(result.Errors));
        AssertOnlyThemeChanged(
            before,
            session.Current,
            before.Theme with { TextLineGap = 11 });
        Assert.True(session.HasUnsavedChanges);

        Assert.True(session.TryUndo());
        Assert.Equal(before.Theme, session.Current.Theme);
        Assert.False(session.HasUnsavedChanges);

        Assert.True(session.TryRedo());
        Assert.Equal(before.Theme with { TextLineGap = 11 }, session.Current.Theme);
        Assert.True(session.HasUnsavedChanges);
    }

    private static DesignerViewModel CreateViewModel(out SkinDraftSession session)
    {
        var next = CreatedAt;
        session = new SkinDraftSession(
            SkinDraftFactory.CreateNew(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CreatedAt,
                SemanticVersion.Parse("1.2.3")),
            () => next = next.AddSeconds(1));
        return new DesignerViewModel(session);
    }

    private static void AssertOnlyThemeChanged(
        SkinDraftDocument before,
        SkinDraftDocument after,
        SkinTheme expectedTheme)
    {
        Assert.Equal(
            before with
            {
                Revision = after.Revision,
                UpdatedAtUtc = after.UpdatedAtUtc,
                Theme = expectedTheme,
                Assets = after.Assets
            },
            after);
        Assert.Equal(
            before.Assets.OrderBy(pair => pair.Key),
            after.Assets.OrderBy(pair => pair.Key));
    }

    private static string Format(IReadOnlyList<SkinValidationError> errors) =>
        string.Join("; ", errors.Select(error =>
            $"{error.Code}@{error.Location}"));
}

using CodexQuotaHud.App.UI;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class DetailsPopupTogglePolicyTests
{
    [Fact]
    public void AutoCloseOverOrb_SuppressesEveryPointerDownInDoubleClickWindow()
    {
        var now = new DateTimeOffset(2026, 7, 29, 18, 30, 0, TimeSpan.Zero);
        var policy = new DetailsPopupTogglePolicy(() => now);

        policy.ObserveClosed(
            pointerOverOrb: true,
            programmaticClose: false,
            TimeSpan.FromMilliseconds(500));

        Assert.True(policy.ShouldDismissPointerDown(
            popupOpen: false,
            TimeSpan.FromMilliseconds(500)));
        now = now.AddMilliseconds(250);
        Assert.True(policy.ShouldDismissPointerDown(
            popupOpen: false,
            TimeSpan.FromMilliseconds(500)));
        Assert.True(policy.IsOpenSuppressed);
        now = now.AddMilliseconds(251);
        Assert.False(policy.ShouldDismissPointerDown(
            popupOpen: false,
            TimeSpan.FromMilliseconds(500)));
        Assert.False(policy.IsOpenSuppressed);
    }

    [Fact]
    public void VisiblePopup_ArmsDismissalWindowBeforeProgrammaticClose()
    {
        var now = new DateTimeOffset(2026, 7, 29, 18, 30, 0, TimeSpan.Zero);
        var policy = new DetailsPopupTogglePolicy(() => now);

        Assert.True(policy.ShouldDismissPointerDown(
            popupOpen: true,
            TimeSpan.FromMilliseconds(500)));
        policy.ObserveClosed(
            pointerOverOrb: true,
            programmaticClose: true,
            TimeSpan.FromMilliseconds(500));

        now = now.AddMilliseconds(300);
        Assert.True(policy.ShouldDismissPointerDown(
            popupOpen: false,
            TimeSpan.FromMilliseconds(500)));
        now = now.AddMilliseconds(201);
        Assert.False(policy.IsOpenSuppressed);
    }
}

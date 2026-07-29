using CodexQuotaHud.App.UI;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class DetailsPopupTogglePolicyTests
{
    [Fact]
    public void AutoCloseCausedByOrbClick_SuppressesReopenForSameClickOnly()
    {
        var policy = new DetailsPopupTogglePolicy();

        policy.ObserveClosed(
            pointerOverOrb: true,
            leftButtonPressed: true,
            programmaticClose: false);

        Assert.True(policy.ConsumeSuppressedOpen());
        Assert.False(policy.ConsumeSuppressedOpen());
    }

    [Fact]
    public void ProgrammaticToggleClose_DoesNotSuppressNextIndependentClick()
    {
        var policy = new DetailsPopupTogglePolicy();

        policy.ObserveClosed(
            pointerOverOrb: true,
            leftButtonPressed: true,
            programmaticClose: true);

        Assert.False(policy.ConsumeSuppressedOpen());
    }
}

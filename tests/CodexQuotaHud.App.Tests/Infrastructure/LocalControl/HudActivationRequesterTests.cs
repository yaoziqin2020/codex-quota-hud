using CodexQuotaHud.App.Infrastructure.LocalControl;

namespace CodexQuotaHud.App.Tests.Infrastructure.LocalControl;

public sealed class HudActivationRequesterTests
{
    private const string SelectionKey =
        "custom:11111111-1111-1111-1111-111111111111";

    [Fact]
    public async Task LiveSuccess_ReturnsActivatedWithoutStartingProcess()
    {
        var launches = 0;
        var requester = Requester(
            new LocalControlResponse(true, null, null),
            _ =>
            {
                launches++;
                return (true, null);
            });

        var result = await requester.ActivateAsync(SelectionKey);

        Assert.Equal(HudActivationDisposition.ActivatedLive, result.Disposition);
        Assert.Equal(0, launches);
    }

    [Fact]
    public async Task OnlyUnavailable_FallsBackToExactInstalledLauncher()
    {
        var launchedKey = string.Empty;
        var requester = Requester(
            new LocalControlResponse(false, "control.unavailable", "Unavailable."),
            key =>
            {
                launchedKey = key;
                return (true, null);
            });

        var result = await requester.ActivateAsync(SelectionKey);

        Assert.Equal(HudActivationDisposition.StartedHud, result.Disposition);
        Assert.Equal(SelectionKey, launchedKey);
    }

    [Theory]
    [InlineData("control.timeout", HudActivationDisposition.Failed)]
    [InlineData("control.protocol.invalid", HudActivationDisposition.Failed)]
    [InlineData("skin.selection.missing", HudActivationDisposition.Rejected)]
    [InlineData("skin.activation.failed", HudActivationDisposition.Rejected)]
    [InlineData("control.request.invalid", HudActivationDisposition.Rejected)]
    public async Task TimeoutMalformedOrRejectedLiveResult_NeverStartsSecondHud(
        string errorCode,
        HudActivationDisposition expectedDisposition)
    {
        var launches = 0;
        var requester = Requester(
            new LocalControlResponse(false, errorCode, "Bounded failure."),
            _ =>
            {
                launches++;
                return (true, null);
            });

        var result = await requester.ActivateAsync(SelectionKey);

        Assert.Equal(expectedDisposition, result.Disposition);
        Assert.Equal(errorCode, result.ErrorCode);
        Assert.Equal(0, launches);
    }

    [Fact]
    public async Task UnavailableButFailedProcessStart_ReturnsFailed()
    {
        var requester = Requester(
            new LocalControlResponse(false, "control.unavailable", "Unavailable."),
            _ => (false, "正式版启动失败"));

        var result = await requester.ActivateAsync(SelectionKey);

        Assert.Equal(HudActivationDisposition.Failed, result.Disposition);
        Assert.Equal("hud.launch.failed", result.ErrorCode);
        Assert.Equal("正式版启动失败", result.Message);
    }

    [Fact]
    public async Task InvalidSelection_IsRejectedBeforePipeOrProcess()
    {
        var sends = 0;
        var launches = 0;
        var requester = new HudActivationRequester(
            (_, _) =>
            {
                sends++;
                return Task.FromResult(new LocalControlResponse(true, null, null));
            },
            _ =>
            {
                launches++;
                return (true, null);
            });

        var result = await requester.ActivateAsync("builtin:HudDial");

        Assert.Equal(HudActivationDisposition.Rejected, result.Disposition);
        Assert.Equal("control.request.invalid", result.ErrorCode);
        Assert.Equal(0, sends);
        Assert.Equal(0, launches);
    }

    private static HudActivationRequester Requester(
        LocalControlResponse response,
        Func<string, (bool Succeeded, string? Error)> launch) =>
        new(
            (_, _) => Task.FromResult(response),
            launch);
}

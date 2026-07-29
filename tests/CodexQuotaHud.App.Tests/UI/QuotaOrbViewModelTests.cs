using CodexQuotaHud.App.UI;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.Refresh;
using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class QuotaOrbViewModelTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"CodexQuotaHud-VM-{Guid.NewGuid():N}");

    [Fact]
    public void HiddenState_HidesWindow()
    {
        var source = new FakeRefreshController();
        using var viewModel = CreateViewModel(source);

        source.Publish(State(QuotaDisplayState.Hidden()));

        Assert.False(viewModel.IsVisible);
        Assert.Empty(viewModel.Details);
    }

    [Fact]
    public void WeeklyOnly_ShowsWeeklyLabelAndNoSecondaryRing()
    {
        var source = new FakeRefreshController();
        using var viewModel = CreateViewModel(source);
        var fetchedAt = DateTimeOffset.Parse("2026-07-29T01:00:00Z");
        var weekly = new QuotaWindow(
            QuotaWindowKind.Weekly,
            83.6,
            DateTimeOffset.Parse("2026-08-04T02:30:00Z"));

        source.Publish(State(QuotaDisplayState.FromSnapshot(
            new QuotaSnapshot(null, weekly, fetchedAt))));

        Assert.True(viewModel.IsVisible);
        Assert.Equal(84, viewModel.PrimaryPercent);
        Assert.Equal("每周", viewModel.PrimaryLabel);
        Assert.Null(viewModel.SecondaryPercent);
        Assert.False(viewModel.HasSecondary);
        var row = Assert.Single(viewModel.Details);
        Assert.Equal("每周", row.Label);
        Assert.Equal("84%", row.Remaining);
    }

    [Fact]
    public void Dual_ShowsFiveHourInCenterAndWeeklyOutside()
    {
        var source = new FakeRefreshController();
        using var viewModel = CreateViewModel(source);
        var snapshot = new QuotaSnapshot(
            new QuotaWindow(QuotaWindowKind.FiveHour, 61.5, null),
            new QuotaWindow(QuotaWindowKind.Weekly, 84.4, null),
            DateTimeOffset.Parse("2026-07-29T01:00:00Z"));

        source.Publish(State(QuotaDisplayState.FromSnapshot(snapshot)));

        Assert.Equal(62, viewModel.PrimaryPercent);
        Assert.Equal("5 小时", viewModel.PrimaryLabel);
        Assert.Equal(84, viewModel.SecondaryPercent);
        Assert.True(viewModel.HasSecondary);
        Assert.Collection(
            viewModel.Details,
            row => Assert.Equal("5 小时", row.Label),
            row => Assert.Equal("每周", row.Label));
        Assert.Equal(QuotaDisplayMode.Dual, viewModel.DisplayMode);
        Assert.Equal(
            new QuotaSkinState(
                62,
                84,
                "5 小时",
                QuotaDisplayMode.Dual,
                IsRefreshing: false,
                AnimationsEnabled: true),
            viewModel.SkinState);
    }

    [Fact]
    public void StaleState_AddsDataMayBeStaleMessage()
    {
        var source = new FakeRefreshController();
        using var viewModel = CreateViewModel(source);
        var snapshot = new QuotaSnapshot(
            new QuotaWindow(QuotaWindowKind.FiveHour, 50, null),
            null,
            DateTimeOffset.Parse("2026-07-29T01:00:00Z"));

        source.Publish(State(QuotaDisplayState.FromSnapshot(snapshot, isStale: true)));

        Assert.True(viewModel.IsStale);
        Assert.Equal("数据可能已过期", viewModel.StaleMessage);
    }

    [Fact]
    public void SkinSelection_UpdatesImmediatelyAndPersists()
    {
        var source = new FakeRefreshController();
        var store = CreateStore();
        using var viewModel = CreateViewModel(source, store);

        viewModel.SelectSkinCommand.Execute(SkinId.LiquidTank);

        Assert.Equal(SkinId.LiquidTank, viewModel.SelectedSkin);
        Assert.Equal(SkinId.LiquidTank, store.Load().SelectedSkin);
    }

    [Fact]
    public void ServiceEvents_AreDispatchedBeforeUpdatingBindableState()
    {
        var source = new FakeRefreshController();
        var dispatcher = new QueuedDispatcher(checkAccess: false);
        using var viewModel = CreateViewModel(source, dispatcher: dispatcher);
        var snapshot = new QuotaSnapshot(
            new QuotaWindow(QuotaWindowKind.FiveHour, 74, null),
            null,
            DateTimeOffset.Parse("2026-07-29T01:00:00Z"));

        source.Publish(State(QuotaDisplayState.FromSnapshot(snapshot)));

        Assert.False(viewModel.IsVisible);
        dispatcher.Drain();
        Assert.True(viewModel.IsVisible);
        Assert.Equal(74, viewModel.PrimaryPercent);
    }

    [Fact]
    public async Task Hover_RequestsRefreshOnlyIfStale()
    {
        var source = new FakeRefreshController();
        using var viewModel = CreateViewModel(source);

        await viewModel.OnHoverAsync();

        Assert.Equal([true], source.OnlyIfStaleRequests);
    }

    [Fact]
    public void FreshSuccess_PersistsLastSuccessfulRefreshWithoutQuotaPayload()
    {
        var source = new FakeRefreshController();
        var store = CreateStore();
        using var viewModel = CreateViewModel(source, store);
        var fetchedAt = DateTimeOffset.Parse("2026-07-29T01:00:00Z");
        var snapshot = new QuotaSnapshot(
            new QuotaWindow(QuotaWindowKind.FiveHour, 74, null),
            null,
            fetchedAt);

        source.Publish(State(QuotaDisplayState.FromSnapshot(snapshot)));

        Assert.Equal(fetchedAt, store.Load().LastSuccessfulRefresh);
        var json = File.ReadAllText(store.SettingsPath);
        Assert.DoesNotContain("RemainingPercent", json, StringComparison.Ordinal);
        Assert.DoesNotContain("FiveHour", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsSaveFailure_DoesNotEscapeSkinAnimationPositionOrRefreshUpdates()
    {
        var source = new FakeRefreshController();
        var store = new ThrowingSettingsStore();
        using var viewModel = new QuotaOrbViewModel(
            source,
            store,
            new AppSettings(),
            new QueuedDispatcher(checkAccess: true),
            () => { });
        var snapshot = new QuotaSnapshot(
            new QuotaWindow(QuotaWindowKind.FiveHour, 45, null),
            null,
            DateTimeOffset.Parse("2026-07-29T01:00:00Z"));

        var exception = Record.Exception(() =>
        {
            viewModel.SelectSkinCommand.Execute(SkinId.Aurora);
            viewModel.ToggleAnimationsCommand.Execute(null);
            viewModel.SavePosition(20, 30);
            source.Publish(State(QuotaDisplayState.FromSnapshot(snapshot)));
        });

        Assert.Null(exception);
        Assert.Equal(SkinId.Aurora, viewModel.SelectedSkin);
        Assert.False(viewModel.AnimationsEnabled);
        Assert.True(viewModel.IsVisible);
        Assert.Equal(45, viewModel.PrimaryPercent);
        Assert.Equal("设置未保存", viewModel.LastSettingsError);
    }

    [Fact]
    public void DisposedViewModel_IgnoresAlreadyQueuedState()
    {
        var source = new FakeRefreshController();
        var dispatcher = new QueuedDispatcher(checkAccess: false);
        var viewModel = CreateViewModel(source, dispatcher: dispatcher);
        var snapshot = new QuotaSnapshot(
            new QuotaWindow(QuotaWindowKind.FiveHour, 74, null),
            null,
            DateTimeOffset.Parse("2026-07-29T01:00:00Z"));
        source.Publish(State(QuotaDisplayState.FromSnapshot(snapshot)));

        viewModel.Dispose();
        dispatcher.Drain();

        Assert.False(viewModel.IsVisible);
        Assert.Equal(0, viewModel.PrimaryPercent);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private QuotaOrbViewModel CreateViewModel(
        FakeRefreshController source,
        SettingsStore? store = null,
        IUiDispatcher? dispatcher = null)
    {
        store ??= CreateStore();
        return new QuotaOrbViewModel(
            source,
            store,
            store.Load(),
            dispatcher ?? new QueuedDispatcher(checkAccess: true),
            () => { });
    }

    private SettingsStore CreateStore() =>
        new(Path.Combine(_directory, "settings.json"));

    private static QuotaRefreshState State(QuotaDisplayState display) =>
        new(
            IsCodexRunning: display.Mode != QuotaDisplayMode.Hidden,
            IsRefreshing: false,
            Display: display,
            LastError: null);

    private sealed class FakeRefreshController : IQuotaRefreshController
    {
        public event Action<QuotaRefreshState>? StateChanged;

        public List<bool> OnlyIfStaleRequests { get; } = [];

        public Task RefreshNowAsync(bool onlyIfStale, CancellationToken cancellationToken)
        {
            OnlyIfStaleRequests.Add(onlyIfStale);
            return Task.CompletedTask;
        }

        public void Publish(QuotaRefreshState state) => StateChanged?.Invoke(state);
    }

    private sealed class QueuedDispatcher(bool checkAccess) : IUiDispatcher
    {
        private readonly Queue<Action> _actions = new();

        public bool CheckAccess() => checkAccess;

        public void Post(Action action) => _actions.Enqueue(action);

        public void Drain()
        {
            while (_actions.TryDequeue(out var action))
            {
                action();
            }
        }
    }

    private sealed class ThrowingSettingsStore : ISettingsStore
    {
        public AppSettings Load() => new();

        public void Save(AppSettings settings) =>
            throw new UnauthorizedAccessException("read only");
    }
}

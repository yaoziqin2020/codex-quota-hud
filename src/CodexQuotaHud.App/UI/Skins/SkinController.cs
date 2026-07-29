using System.Windows;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.UI.Skins;

public sealed class SkinController
{
    private readonly IReadOnlyDictionary<SkinId, Func<IQuotaSkin>> _factories;
    private readonly Dictionary<SkinId, IQuotaSkin> _instances = [];
    private QuotaSkinState? _lastState;

    public SkinController()
        : this(
            new Dictionary<SkinId, Func<IQuotaSkin>>
            {
                [SkinId.HudDial] = static () => new HudDialSkin(),
                [SkinId.EnergyRing] = static () => new EnergyRingSkin(),
                [SkinId.LiquidGlass] = static () => new LiquidGlassSkin(),
                [SkinId.Aurora] = static () => new AuroraSkin(),
                [SkinId.LiquidTank] = static () => new LiquidTankSkin()
            },
            SkinId.HudDial)
    {
    }

    internal SkinController(
        IReadOnlyDictionary<SkinId, Func<IQuotaSkin>> factories,
        SkinId selectedSkin)
    {
        ArgumentNullException.ThrowIfNull(factories);
        if (!Enum.GetValues<SkinId>().All(factories.ContainsKey))
        {
            var missing = Enum.GetValues<SkinId>()
                .Where(id => !factories.ContainsKey(id));
            throw new ArgumentException(
                $"Missing skin registrations: {string.Join(", ", missing)}",
                nameof(factories));
        }

        _factories = factories;
        CurrentSkin = Resolve(Normalize(selectedSkin));
    }

    public IReadOnlyCollection<SkinId> RegisteredIds =>
        _factories.Keys.ToArray();

    public IQuotaSkin CurrentSkin { get; private set; }

    public FrameworkElement CurrentView => CurrentSkin.View;

    public IQuotaSkin Select(SkinId skin)
    {
        var selected = Resolve(Normalize(skin));
        if (!ReferenceEquals(CurrentSkin, selected))
        {
            CurrentSkin = selected;
        }

        if (_lastState is not null)
        {
            CurrentSkin.Render(_lastState);
        }

        return CurrentSkin;
    }

    public void Render(QuotaSkinState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _lastState = state;
        CurrentSkin.Render(state);
    }

    private IQuotaSkin Resolve(SkinId skin)
    {
        if (_instances.TryGetValue(skin, out var existing))
        {
            return existing;
        }

        var created = _factories[skin]();
        if (created.Id != skin)
        {
            throw new InvalidOperationException(
                $"Skin factory for {skin} produced {created.Id}.");
        }

        _instances.Add(skin, created);
        return created;
    }

    private static SkinId Normalize(SkinId skin) =>
        Enum.IsDefined(skin) ? skin : SkinId.HudDial;
}

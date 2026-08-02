using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.Settings;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.App.UI.Skins;

public sealed record HudSkinCatalogSnapshot(
    IReadOnlyList<SkinDescriptor> Healthy,
    IReadOnlyList<CorruptInstalledSkin> Corrupt);

public sealed class HudSkinCatalog
{
    private readonly object _sync = new();
    private readonly InstalledSkinCatalog? _installedCatalog;
    private IReadOnlyDictionary<string, SkinDescriptor> _byKey =
        new Dictionary<string, SkinDescriptor>(StringComparer.Ordinal);
    private HudSkinCatalogSnapshot _snapshot = null!;

    public HudSkinCatalog(InstalledSkinCatalog installedCatalog)
        : this(
            (installedCatalog ?? throw new ArgumentNullException(
                nameof(installedCatalog))).LoadAll(),
            installedCatalog)
    {
    }

    internal HudSkinCatalog(InstalledSkinCatalogResult installedSnapshot)
        : this(installedSnapshot, installedCatalog: null)
    {
    }

    private HudSkinCatalog(
        InstalledSkinCatalogResult installedSnapshot,
        InstalledSkinCatalog? installedCatalog)
    {
        ArgumentNullException.ThrowIfNull(installedSnapshot);
        _installedCatalog = installedCatalog;
        Replace(installedSnapshot);
    }

    private void Replace(InstalledSkinCatalogResult installedSnapshot)
    {

        var healthy = Enum.GetValues<SkinId>()
            .Select(id => new SkinDescriptor(
                SkinSelectionKey.FromBuiltIn(id),
                DisplayNameFor(id),
                IsBuiltIn: true,
                BuiltInId: id,
                Installed: null))
            .Concat(installedSnapshot.Installed
                .OrderBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.SkinId)
                .Select(record => new SkinDescriptor(
                    record.SelectionKey,
                    record.DisplayName,
                    IsBuiltIn: false,
                    BuiltInId: null,
                    Installed: record)))
            .ToArray();
        var corrupt = installedSnapshot.Corrupt.ToArray();
        var snapshot = new HudSkinCatalogSnapshot(
            Array.AsReadOnly(healthy),
            Array.AsReadOnly(corrupt));
        var byKey = healthy.ToDictionary(
            descriptor => descriptor.SelectionKey,
            StringComparer.Ordinal);
        lock (_sync)
        {
            _snapshot = snapshot;
            _byKey = byKey;
            Generation = new object();
        }
    }

    internal object Generation { get; private set; } = new();

    public static HudSkinCatalog CreateBuiltInOnly() =>
        new(new InstalledSkinCatalogResult([], []));

    public HudSkinCatalogSnapshot Load()
    {
        lock (_sync)
        {
            return _snapshot;
        }
    }

    public HudSkinCatalogSnapshot Refresh()
    {
        if (_installedCatalog is null)
        {
            return Load();
        }

        Replace(_installedCatalog.LoadAll());
        return Load();
    }

    public bool TryGet(string selectionKey, out SkinDescriptor descriptor)
    {
        if (selectionKey is null)
        {
            descriptor = null!;
            return false;
        }

        lock (_sync)
        {
            return _byKey.TryGetValue(selectionKey, out descriptor!);
        }
    }

    private static string DisplayNameFor(SkinId id) => id switch
    {
        SkinId.HudDial => "HUD 科技仪表",
        SkinId.EnergyRing => "双彩能量环",
        SkinId.LiquidGlass => "流体玻璃球",
        SkinId.Aurora => "克制极光",
        SkinId.LiquidTank => "液位储能舱",
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };
}

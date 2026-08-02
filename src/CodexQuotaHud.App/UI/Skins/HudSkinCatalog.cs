using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.Settings;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.App.UI.Skins;

public sealed record HudSkinCatalogSnapshot(
    IReadOnlyList<SkinDescriptor> Healthy,
    IReadOnlyList<CorruptInstalledSkin> Corrupt);

public sealed class HudSkinCatalog
{
    private readonly IReadOnlyDictionary<string, SkinDescriptor> _byKey;
    private readonly HudSkinCatalogSnapshot _snapshot;

    public HudSkinCatalog(InstalledSkinCatalog installedCatalog)
        : this((installedCatalog ?? throw new ArgumentNullException(
            nameof(installedCatalog))).LoadAll())
    {
    }

    internal HudSkinCatalog(InstalledSkinCatalogResult installedSnapshot)
    {
        ArgumentNullException.ThrowIfNull(installedSnapshot);

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
        _snapshot = new HudSkinCatalogSnapshot(
            Array.AsReadOnly(healthy),
            Array.AsReadOnly(corrupt));
        _byKey = healthy.ToDictionary(
            descriptor => descriptor.SelectionKey,
            StringComparer.Ordinal);
        Generation = new object();
    }

    internal object Generation { get; }

    public static HudSkinCatalog CreateBuiltInOnly() =>
        new(new InstalledSkinCatalogResult([], []));

    public HudSkinCatalogSnapshot Load() => _snapshot;

    public bool TryGet(string selectionKey, out SkinDescriptor descriptor)
    {
        if (selectionKey is null)
        {
            descriptor = null!;
            return false;
        }

        return _byKey.TryGetValue(selectionKey, out descriptor!);
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

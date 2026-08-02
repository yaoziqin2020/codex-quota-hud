using System.IO;
using System.Windows;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.Settings;
using CodexQuotaHud.Skins.Templates;

namespace CodexQuotaHud.App.UI.Skins;

public sealed class SkinController
{
    private readonly HudSkinCatalog _catalog;
    private readonly Func<SkinDescriptor, IQuotaSkin> _factory;
    private readonly Dictionary<string, IQuotaSkin> _instances =
        new(StringComparer.Ordinal);
    private QuotaSkinState? _lastState;

    public event EventHandler? ActiveSkinChanged;

    public SkinController()
        : this(
            HudSkinCatalog.CreateBuiltInOnly(),
            SkinTemplateRegistry.CreateDefault())
    {
    }

    public SkinController(
        HudSkinCatalog catalog,
        SkinTemplateRegistry templates)
        : this(
            catalog,
            descriptor => CreateSkin(descriptor, templates),
            SkinSelectionKey.HudDial)
    {
    }

    internal SkinController(
        HudSkinCatalog catalog,
        Func<SkinDescriptor, IQuotaSkin> factory,
        string initialSelectionKey)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        if (!TryPrepare(initialSelectionKey, out var initial, out var failure))
        {
            throw new InvalidOperationException(
                $"The initial skin could not be prepared ({failure?.ErrorCode}).");
        }

        SetActive(initial!);
    }

    public IReadOnlyCollection<string> RegisteredKeys =>
        _catalog.Load().Healthy
            .Select(descriptor => descriptor.SelectionKey)
            .ToArray();

    public IReadOnlyCollection<SkinId> RegisteredIds =>
        _catalog.Load().Healthy
            .Where(descriptor => descriptor.BuiltInId is not null)
            .Select(descriptor => descriptor.BuiltInId!.Value)
            .ToArray();

    public IQuotaSkin CurrentSkin { get; private set; } = null!;

    public SkinDescriptor CurrentDescriptor { get; private set; } = null!;

    public SkinPresentation CurrentPresentation { get; private set; } = null!;

    public FrameworkElement CurrentView => CurrentSkin.View;

    public bool TryPrepare(
        string selectionKey,
        out SkinActivationCandidate? candidate,
        out SkinSelectionFailure? failure)
    {
        candidate = null;
        failure = null;
        if (!_catalog.TryGet(selectionKey, out var descriptor))
        {
            failure = new SkinSelectionFailure(
                selectionKey ?? string.Empty,
                BoundedIdentity(selectionKey),
                "skin.selection.missing");
            return false;
        }

        try
        {
            var skin = Resolve(descriptor);
            if (!string.Equals(
                    skin.SelectionKey,
                    descriptor.SelectionKey,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The skin factory returned a different selection identity.");
            }

            if (_lastState is not null)
            {
                skin.Render(_lastState);
            }

            candidate = new SkinActivationCandidate(
                descriptor,
                skin,
                descriptor.BuiltInId is { } builtInId
                    ? SkinPresentation.ForBuiltIn(builtInId)
                    : SkinPresentation.ForCustom(descriptor.Installed!.Package.Theme),
                _catalog.Generation);
            return true;
        }
        catch (Exception)
        {
            failure = new SkinSelectionFailure(
                descriptor.SelectionKey,
                BoundedIdentity(descriptor.DisplayName),
                "skin.selection.factory");
            return false;
        }
    }

    public void Activate(SkinActivationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!ReferenceEquals(candidate.CatalogGeneration, _catalog.Generation) ||
            !_catalog.TryGet(candidate.Descriptor.SelectionKey, out var current) ||
            !ReferenceEquals(current, candidate.Descriptor))
        {
            throw new InvalidOperationException(
                "The prepared skin does not belong to the current catalog generation.");
        }

        SetActive(candidate);
        ActiveSkinChanged?.Invoke(this, EventArgs.Empty);
    }

    public IQuotaSkin Select(SkinId skin)
    {
        var normalized = Enum.IsDefined(skin) ? skin : SkinId.HudDial;
        if (TryPrepare(
                SkinSelectionKey.FromBuiltIn(normalized),
                out var candidate,
                out _))
        {
            Activate(candidate!);
        }

        return CurrentSkin;
    }

    public void Render(QuotaSkinState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _lastState = state;
        CurrentSkin.Render(state);
    }

    private IQuotaSkin Resolve(SkinDescriptor descriptor)
    {
        if (_instances.TryGetValue(descriptor.SelectionKey, out var existing))
        {
            return existing;
        }

        var created = _factory(descriptor);
        _instances.Add(descriptor.SelectionKey, created);
        return created;
    }

    private void SetActive(SkinActivationCandidate candidate)
    {
        CurrentSkin = candidate.Skin;
        CurrentDescriptor = candidate.Descriptor;
        CurrentPresentation = candidate.Presentation;
    }

    private static IQuotaSkin CreateSkin(
        SkinDescriptor descriptor,
        SkinTemplateRegistry templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        if (descriptor.BuiltInId is { } builtInId)
        {
            return builtInId switch
            {
                SkinId.HudDial => new HudDialSkin(),
                SkinId.EnergyRing => new EnergyRingSkin(),
                SkinId.LiquidGlass => new LiquidGlassSkin(),
                SkinId.Aurora => new AuroraSkin(),
                SkinId.LiquidTank => new LiquidTankSkin(),
                _ => throw new ArgumentOutOfRangeException(nameof(descriptor))
            };
        }

        var installed = descriptor.Installed ?? throw new InvalidOperationException(
            "A custom descriptor has no installed package snapshot.");
        var manifest = installed.Package.Manifest;
        if (!templates.TryResolve(
                manifest.TemplateId,
                manifest.SchemaVersion,
                out var template))
        {
            throw new InvalidOperationException(
                "The installed skin template is unavailable.");
        }

        return new CustomQuotaSkin(
            descriptor.SelectionKey,
            installed.Package.Theme,
            template.CreateRenderer(installed.Package));
    }

    private static string BoundedIdentity(string? value)
    {
        var identity = string.IsNullOrWhiteSpace(value) ? "unknown skin" : value;
        if (Path.IsPathFullyQualified(identity))
        {
            identity = Path.GetFileName(identity);
        }

        const int maximumLength = 100;
        return identity.Length <= maximumLength
            ? identity
            : identity[..maximumLength];
    }
}

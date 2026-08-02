namespace CodexQuotaHud.App.UI.Skins;

public sealed record SkinActivationCandidate
{
    internal SkinActivationCandidate(
        SkinDescriptor descriptor,
        IQuotaSkin skin,
        SkinPresentation presentation,
        object catalogGeneration)
    {
        Descriptor = descriptor;
        Skin = skin;
        Presentation = presentation;
        CatalogGeneration = catalogGeneration;
    }

    public SkinDescriptor Descriptor { get; }

    public IQuotaSkin Skin { get; }

    public SkinPresentation Presentation { get; }

    internal object CatalogGeneration { get; }
}

public sealed record SkinSelectionFailure(
    string RequestedSelectionKey,
    string DisplayNameOrId,
    string ErrorCode);

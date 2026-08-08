using CodexQuotaHud.App.Preview;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.SkinDesigner.Preview;

public sealed class DesignerPreviewController
{
    private readonly Func<SkinPackageDocument,
        SkinValidationResult<SkinPackageDocument>> _setCustomPackage;
    private readonly Action<SkinTheme?, bool> _setDesignerGuides;
    private SkinPackageDocument? _lastValidPackage;
    private bool _guidesVisible;
    private DesignerAnimationAudition _animationAudition;

    public DesignerPreviewController(SyntheticPreviewComposition composition)
        : this(
            (composition ?? throw new ArgumentNullException(nameof(composition)))
                .SetCustomPackage,
            composition.SetDesignerGuides)
    {
    }

    internal DesignerPreviewController(
        Func<SkinPackageDocument, SkinValidationResult<SkinPackageDocument>>
            setCustomPackage)
        : this(setCustomPackage, (_, _) => { })
    {
    }

    private DesignerPreviewController(
        Func<SkinPackageDocument, SkinValidationResult<SkinPackageDocument>>
            setCustomPackage,
        Action<SkinTheme?, bool> setDesignerGuides)
    {
        _setCustomPackage = setCustomPackage ?? throw new ArgumentNullException(
            nameof(setCustomPackage));
        _setDesignerGuides = setDesignerGuides ?? throw new ArgumentNullException(
            nameof(setDesignerGuides));
    }

    public SkinValidationResult<SkinPackageDocument> Update(
        SkinDraftDocument draft,
        IReadOnlyDictionary<SkinAssetSlot, SkinAsset> assets)
    {
        var built = DraftPreviewDocumentBuilder.Build(draft, assets);
        if (!built.IsValid)
        {
            return built;
        }

        var original = built.Value!;
        var rendered = _setCustomPackage(Audition(original, _animationAudition));
        if (rendered.IsValid)
        {
            _lastValidPackage = original;
            _setDesignerGuides(
                original.Theme,
                _guidesVisible);
            return built;
        }

        return rendered;
    }

    public void SetGuidesVisible(bool value)
    {
        _guidesVisible = value;
        _setDesignerGuides(
            _lastValidPackage?.Theme,
            value);
    }

    public void SetAnimationAudition(DesignerAnimationAudition value)
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (_animationAudition == value)
        {
            return;
        }

        _animationAudition = value;
        if (_lastValidPackage is not null)
        {
            _ = _setCustomPackage(Audition(_lastValidPackage, value));
        }
    }

    private static SkinPackageDocument Audition(
        SkinPackageDocument saved,
        DesignerAnimationAudition mode)
    {
        var animation = Audition(saved.Theme.Animation, mode);
        return ReferenceEquals(animation, saved.Theme.Animation)
            ? saved
            : saved with
            {
                Theme = saved.Theme with { Animation = animation }
            };
    }

    private static SkinAnimationSettings Audition(
        SkinAnimationSettings saved,
        DesignerAnimationAudition mode) => mode switch
    {
        DesignerAnimationAudition.Rotation => saved with
        {
            BreathingIntensity = 0,
            GlowIntensity = 0,
            FloatingIntensity = 0
        },
        DesignerAnimationAudition.Breathing => saved with
        {
            RotationIntensity = 0,
            GlowIntensity = 0,
            FloatingIntensity = 0
        },
        DesignerAnimationAudition.Glow => saved with
        {
            RotationIntensity = 0,
            BreathingIntensity = 0,
            FloatingIntensity = 0
        },
        DesignerAnimationAudition.Floating => saved with
        {
            RotationIntensity = 0,
            BreathingIntensity = 0,
            GlowIntensity = 0
        },
        _ => saved
    };
}

using System.Windows.Controls;

namespace CodexQuotaHud.Skins.Templates;

public abstract class CustomSkinRenderer : UserControl
{
    public int? DesiredFrameRate { get; protected set; }

    public bool HasActiveAnimations { get; protected set; }

    public abstract void Render(CustomSkinRenderState state);

    public abstract void ApplyAnimationState(
        CustomSkinAnimationState state,
        bool globalAnimationsEnabled,
        double refreshSpeedMultiplier = 2d);
}

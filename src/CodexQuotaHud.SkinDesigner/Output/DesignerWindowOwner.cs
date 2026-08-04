using System.Windows;

namespace CodexQuotaHud.SkinDesigner.Output;

internal sealed class DesignerWindowOwner
{
    internal Window? Current { get; private set; }

    internal void Promote(Window window) =>
        Current = window ?? throw new ArgumentNullException(nameof(window));
}

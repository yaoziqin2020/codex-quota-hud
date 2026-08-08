using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CodexQuotaHud.SkinDesigner.Preview;

public sealed class DesignerPreviewToolsViewModel : INotifyPropertyChanged
{
    private readonly Action<bool> _setGuidesVisible;
    private bool _compositionGuidesVisible;

    public DesignerPreviewToolsViewModel(
        DesignerPreviewController controller)
        : this((controller ?? throw new ArgumentNullException(nameof(controller)))
            .SetGuidesVisible)
    {
    }

    internal DesignerPreviewToolsViewModel(Action<bool> setGuidesVisible)
    {
        _setGuidesVisible = setGuidesVisible ?? throw new ArgumentNullException(
            nameof(setGuidesVisible));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool CompositionGuidesVisible
    {
        get => _compositionGuidesVisible;
        set
        {
            if (_compositionGuidesVisible == value)
            {
                return;
            }

            _compositionGuidesVisible = value;
            _setGuidesVisible(value);
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

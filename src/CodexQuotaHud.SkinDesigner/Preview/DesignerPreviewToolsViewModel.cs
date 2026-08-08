using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodexQuotaHud.SkinDesigner.UI;

namespace CodexQuotaHud.SkinDesigner.Preview;

public sealed class DesignerPreviewToolsViewModel : INotifyPropertyChanged,
    IDisposable
{
    private readonly Action<bool> _setGuidesVisible;
    private readonly Action<DesignerAnimationAudition> _setAnimationAudition;
    private readonly SyntheticPreviewViewModel? _synthetic;
    private bool _compositionGuidesVisible;
    private DesignerAnimationAudition _animationAudition;
    private int _disposed;

    public DesignerPreviewToolsViewModel(
        DesignerPreviewController controller,
        SyntheticPreviewViewModel synthetic)
        : this(
            (controller ?? throw new ArgumentNullException(nameof(controller)))
                .SetGuidesVisible,
            controller.SetAnimationAudition,
            synthetic)
    {
    }

    internal DesignerPreviewToolsViewModel(Action<bool> setGuidesVisible)
        : this(setGuidesVisible, _ => { }, synthetic: null)
    {
    }

    internal DesignerPreviewToolsViewModel(
        Action<bool> setGuidesVisible,
        Action<DesignerAnimationAudition> setAnimationAudition,
        SyntheticPreviewViewModel? synthetic)
    {
        _setGuidesVisible = setGuidesVisible ?? throw new ArgumentNullException(
            nameof(setGuidesVisible));
        _setAnimationAudition = setAnimationAudition ??
            throw new ArgumentNullException(nameof(setAnimationAudition));
        _synthetic = synthetic;
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

    public DesignerAnimationAudition AnimationAudition
    {
        get => _animationAudition;
        set
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                this);
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (_animationAudition == value)
            {
                return;
            }

            var refreshChanged =
                (_animationAudition == DesignerAnimationAudition.Refresh) !=
                (value == DesignerAnimationAudition.Refresh);
            _animationAudition = value;
            _setAnimationAudition(value);
            _synthetic?.SetRefreshAudition(
                value == DesignerAnimationAudition.Refresh);
            OnPropertyChanged();
            if (refreshChanged)
            {
                OnPropertyChanged(nameof(CanEditRefreshCheckbox));
            }
        }
    }

    public bool CanEditRefreshCheckbox =>
        _animationAudition != DesignerAnimationAudition.Refresh;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_animationAudition != DesignerAnimationAudition.Refresh)
        {
            return;
        }

        _synthetic?.SetRefreshAudition(false);
        _animationAudition = DesignerAnimationAudition.All;
        _setAnimationAudition(DesignerAnimationAudition.All);
        OnPropertyChanged(nameof(AnimationAudition));
        OnPropertyChanged(nameof(CanEditRefreshCheckbox));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

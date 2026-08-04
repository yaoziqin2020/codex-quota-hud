using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CodexQuotaHud.App.UI.Animation;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Skins.Storage;
using CodexQuotaHud.Skins.Templates;

namespace CodexQuotaHud.App.UI.SkinManagement;

public partial class SkinImportPreviewWindow : Window
{
    private readonly CustomSkinRenderer _renderer;
    private bool _completed;

    public SkinImportPreviewWindow(SkinInstallPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        InitializeComponent();

        var package = preview.Package;
        var manifest = package.Manifest;
        var registry = SkinTemplateRegistry.CreateDefault();
        if (!registry.TryResolve(
                manifest.TemplateId,
                manifest.SchemaVersion,
                out var template))
        {
            throw new InvalidOperationException(
                "The validated skin template is unavailable.");
        }

        var minimumHudVersion = manifest.MinimumHudVersion.CompareTo(
            template.MinimumHudVersion) >= 0
            ? manifest.MinimumHudVersion
            : template.MinimumHudVersion;
        DisplayNameText.Text = manifest.DisplayName;
        AuthorLabel.Text = $"作者：{manifest.Author}";
        VersionText.Text = $"版本：{manifest.PackageVersion}";
        TemplateText.Text = $"模板：{manifest.TemplateId}";
        AssetSummaryText.Text = package.Assets.Count == 0
            ? "资源：无可选图片"
            : $"资源：{package.Assets.Count} 个已验证图片";
        DescriptionText.Text = manifest.Description;
        CompatibilityText.Text = preview.IsDowngrade
            ? "兼容性：不允许降级"
            : $"兼容性：需要 HUD {minimumHudVersion} 或更高版本";

        _renderer = template.CreateRenderer(package);
        var skin = new CustomQuotaSkin(
            $"custom:{manifest.SkinId:D}",
            package.Theme,
            _renderer);
        skin.Render(new QuotaSkinState(
            68,
            34,
            "5 小时",
            QuotaDisplayMode.Dual,
            IsRefreshing: false,
            AnimationsEnabled: true));
        skin.ApplyAnimationState(OrbAnimationState.Idle, animationsEnabled: true);
        PreviewHost.Content = _renderer;

        if (preview.Existing is not null)
        {
            InstallButton.Visibility = Visibility.Collapsed;
            ReplaceButton.Visibility = Visibility.Visible;
            KeepCopyButton.Visibility = Visibility.Visible;
        }

        if (preview.IsDowngrade)
        {
            InstallButton.IsEnabled = false;
            ReplaceButton.IsEnabled = false;
            KeepCopyButton.IsEnabled = false;
            DowngradeReason.Visibility = Visibility.Visible;
        }
    }

    public SkinCollisionDecision Decision { get; private set; } =
        SkinCollisionDecision.Cancel;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_completed)
        {
            Decision = SkinCollisionDecision.Cancel;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _renderer.ApplyAnimationState(
            CustomSkinAnimationState.Hidden,
            globalAnimationsEnabled: false);
        base.OnClosed(e);
    }

    private void OnInstallClick(object sender, RoutedEventArgs e) =>
        Complete(SkinCollisionDecision.Replace);

    private void OnReplaceClick(object sender, RoutedEventArgs e) =>
        Complete(SkinCollisionDecision.Replace);

    private void OnKeepCopyClick(object sender, RoutedEventArgs e) =>
        Complete(SkinCollisionDecision.KeepCopy);

    private void OnCancelClick(object sender, RoutedEventArgs e) =>
        Complete(SkinCollisionDecision.Cancel);

    private void OnPreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Complete(SkinCollisionDecision.Cancel);
    }

    private void Complete(SkinCollisionDecision decision)
    {
        Decision = decision;
        _completed = true;
        if (IsVisible)
        {
            Close();
        }
    }
}

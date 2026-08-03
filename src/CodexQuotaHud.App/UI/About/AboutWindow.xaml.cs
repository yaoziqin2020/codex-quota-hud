using System.Windows;
using System.Windows.Navigation;

namespace CodexQuotaHud.App.UI.About;

public partial class AboutWindow : Window, IAboutWindow
{
    private readonly AboutInformation _information;

    internal AboutWindow(AboutInformation information)
    {
        _information = information ?? throw new ArgumentNullException(
            nameof(information));
        InitializeComponent();
        ProductNameText.Text = information.ProductName;
        VersionValueText.Text = $"版本 {information.VersionText}";
        AuthorText.Text = $"作者：{information.Author}";
        RepositoryLabelText.Text = information.RepositoryLabel;
        RepositoryLink.NavigateUri = new Uri(information.RepositoryUrl);
        LicenseText.Text = $"许可证：{information.LicenseName}";
    }

    private void OnRepositoryRequestNavigate(
        object sender,
        RequestNavigateEventArgs e)
    {
        e.Handled = true;
        if (!AboutLinkLauncher.TryOpen(_information.RepositoryUrl, out var error))
        {
            _ = System.Windows.MessageBox.Show(
                this,
                error,
                "Codex Quota HUD",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}

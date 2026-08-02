using CodexQuotaHud.SkinDesigner.Drafts;

namespace CodexQuotaHud.SkinDesigner;

public partial class MainWindow : System.Windows.Window, IDesignerWindow
{
    internal MainWindow(SkinDraftDocument draft)
    {
        Draft = draft ?? throw new ArgumentNullException(nameof(draft));
        InitializeComponent();
    }

    internal SkinDraftDocument Draft { get; }
}

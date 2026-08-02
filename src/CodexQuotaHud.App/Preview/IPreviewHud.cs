using CodexQuotaHud.App.UI;

namespace CodexQuotaHud.App.Preview;

public interface IPreviewHud
{
    bool TryActivateSkinKey(string selectionKey);

    void SetDetailsOpen(bool isOpen);
    void PreviewEdge(EdgeDockSide side);
    void ForceExpanded();
}

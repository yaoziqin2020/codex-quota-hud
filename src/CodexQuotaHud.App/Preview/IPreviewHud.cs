using CodexQuotaHud.App.UI;

namespace CodexQuotaHud.App.Preview;

internal interface IPreviewHud
{
    void SetDetailsOpen(bool isOpen);
    void PreviewEdge(EdgeDockSide side);
    void ForceExpanded();
}

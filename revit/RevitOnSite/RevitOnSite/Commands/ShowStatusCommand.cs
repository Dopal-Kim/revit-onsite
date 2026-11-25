// [판정 · 표시: 상태 표시] 판정 상태 색으로 되돌림 (요소별 색 재지정을 지움) — 활성 뷰의 요소별 색 재지정만, 구역 값은 그대로 (ZoneOverlay)
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    [Transaction(TransactionMode.Manual)]
    public class ShowStatusCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) => ZoneOverlay.Run(data, ZoneOverlay.Mode.Status, "상태 표시");
    }
}

// [판정 · 표시: 일정 표시] 구역을 예측 해체일까지 남은 날 5단계 색으로 — 활성 뷰의 요소별 색 재지정만, 구역 값은 그대로 (ZoneOverlay)
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    [Transaction(TransactionMode.Manual)]
    public class ShowScheduleCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) => ZoneOverlay.Run(data, ZoneOverlay.Mode.Schedule, "일정 표시");
    }
}

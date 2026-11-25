// [판정 · 표시: 강도 표시] 구역을 추정 강도 ÷ fck 5단계 파랑으로 — 활성 뷰의 요소별 색 재지정만, 구역 값은 그대로 (ZoneOverlay)
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    [Transaction(TransactionMode.Manual)]
    public class ShowStrengthCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) => ZoneOverlay.Run(data, ZoneOverlay.Mode.Strength, "강도 표시");
    }
}

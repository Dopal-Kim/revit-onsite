// [판정 · 판정 근거] (리본 슬라이드아웃·상황별 구역 탭·우클릭) — 고른 구역의 판정 근거 창: 대시보드 칸을 두 번 누를 때와 같은 창 (Dashboard.ReasonNow).
// 33차: PendingCommand(안내만)라 상황별 탭에서 눌러도 창이 안 떴음. 모델은 바꾸지 않는다.
using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    [Transaction(TransactionMode.ReadOnly)]
    public class ReasonCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            if (App.ProjectDoc(data) == null) return Result.Cancelled;
            try
            {
                var zone = ZoneFocus.SelectedZones(data.Application.ActiveUIDocument).FirstOrDefault();
                if (zone == null) { App.Message("구역을 하나 고르세요.", "검측 뷰에서 구역(또는 그 동바리)을 고르거나, 대시보드 칸을 두 번 누르면 판정 근거가 보입니다."); return Result.Cancelled; }
                Dashboard.ReasonNow(data.Application, Dashboard.ReadZone(zone));
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("판정 근거", ex); }
        }
    }
}

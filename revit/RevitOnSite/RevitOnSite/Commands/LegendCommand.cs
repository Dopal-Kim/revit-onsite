// [판정 · 색 범례] 상태 색 범례 제도 뷰를 만들거나 다시 그린다 (ReportViews.Legend). 활성 뷰가 시트면 그 시트에 놓고, 아니면 범례 뷰를 연다
using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    [Transaction(TransactionMode.Manual)]
    public class LegendCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            var ui = data.Application.ActiveUIDocument;
            try
            {
                View legend = null;
                bool placed = false;
                if (!App.Write(doc, "색 범례", () =>
                {
                    legend = ReportViews.Legend(doc);
                    // 3D·평면 뷰에는 채우기 영역을 놓을 수 없어 범례는 제도 뷰로 만들고, 시트가 열려 있으면 거기에 붙인다
                    if (ui.ActiveView is ViewSheet sheet && Viewport.CanAddViewToSheet(doc, sheet.Id, legend.Id))
                    {
                        var o = sheet.Outline;
                        Viewport.Create(doc, sheet.Id, legend.Id, new XYZ((o.Min.U + o.Max.U) / 2, (o.Min.V + o.Max.V) / 2, 0));
                        placed = true;
                    }
                }, out var err))
                    return App.WriteFailed("색 범례", err);
                // 알림 = 저장 뒤 범례 뷰 안의 색 칸 수
                int boxes = new FilteredElementCollector(doc, legend.Id).OfClass(typeof(FilledRegion)).GetElementCount();
                if (placed) App.Message("이 시트에 상태 색 범례를 놓았습니다.", "색 칸 " + boxes + "개. 위치는 끌어서 옮기면 됩니다." + App.WarningNote());
                else
                {
                    if (!(ui.ActiveView is ViewSheet)) ui.ActiveView = legend;
                    App.Message("상태 색 범례 제도 뷰를 만들었습니다.", "색 칸 " + boxes + "개. 프로젝트 탐색기의 제도 뷰에서 시트로 끌어다 놓으면 됩니다. 다시 누르면 색과 이름을 새로 그립니다." + App.WarningNote());
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                return App.Fail("색 범례", ex);
            }
        }
    }
}

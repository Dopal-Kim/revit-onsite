// [준비 · 뷰·필터 준비] 3D 뷰 "Revit On-Site 검측" 생성·갱신 + 상태 색 필터 + 일람표 2개 (ReportViews.Schedules). 계산 없음
// 쓰기 = App.Write, 알림 = 저장 뒤 모델에서 뷰·필터·일람표를 다시 찾아 센 값
using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    [Transaction(TransactionMode.Manual)]
    public class PrepareViewsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try
            {
                if (!App.Write(doc, "뷰·필터 준비", () =>
                {
                    var v = ZoneEvaluation.StatusView(doc);
                    ZoneEvaluation.ApplyFilters(doc, v);
                    ReportViews.Schedules(doc);   // 구역 판정표 · 층별 존치 현황표 (이름으로 찾아 다시 채움)
                }, out var err))
                    return App.WriteFailed("뷰·필터 준비", err);
                var view = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().First(x => !x.IsTemplate && x.Name == ZoneEvaluation.StatusViewName);
                int n = view.GetFilters().Count(id => doc.GetElement(id)?.Name.StartsWith(ZoneEvaluation.FilterPrefix) == true);
                int tables = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                    .Count(x => !x.IsTemplate && (x.Name == ReportViews.ZoneTable || x.Name == ReportViews.FloorTable));
                data.Application.ActiveUIDocument.ActiveView = view;
                App.Message("검측 3D 뷰와 일람표 " + tables + "개를 준비했습니다.",
                    "상태 색 필터 " + n + "개가 이 뷰에 적용돼 있습니다. 벽·창·계단은 흐리게, 슬래브는 반투명으로 보입니다.\n" +
                    "일람표: 구역 판정표, 층별 존치 현황표 (프로젝트 탐색기 > 일람표/수량)\n[해체 판정]을 누르면 이 뷰와 일람표 값이 바뀝니다." + App.WarningNote());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                return App.Fail("뷰·필터 준비", ex);
            }
        }
    }
}

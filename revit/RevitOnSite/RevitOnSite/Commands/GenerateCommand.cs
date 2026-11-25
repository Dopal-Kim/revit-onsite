// [준비] 구역·동바리 생성 — 창 = Wpf\GenerateWindow, 계산 = ZoneGenerator (그대로)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    [Transaction(TransactionMode.Manual)]
    public class GenerateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try
            {
                var floors = ZoneGenerator.DetectFloors(doc);
                if (floors.Count == 0)
                {
                    App.Message("현장타설 구조 슬래브를 찾지 못했습니다. 동바리를 만들지 않습니다.",
                        "(찾는 조건: 바닥·지붕 중 두께 120–600 mm, 윗면이 레벨 ±300 mm)\n" +
                        "프리캐스트 슬래브(중공 슬래브 등)는 동바리 검측 대상이 아닙니다. IFC 라면 바닥이 '바닥' 카테고리로 들어왔는지 확인하세요.");
                    return Result.Cancelled;
                }
                var o = new GenerateOptions { FamilyPath = Path.Combine(App.Dir, "Families", ZoneGenerator.FamilyName + ".rfa") };
                var axes = ZoneGenerator.ZoneAxes(doc, floors, o);
                var have = ShoringModel.Shores(doc).GroupBy(e => e.LookupParameter("SHR_Floor") is Parameter p && p.HasValue ? p.AsInteger() : int.MinValue).ToDictionary(g => g.Key, g => g.Count());
                var labels = floors.Select(f => f.Label + " — " + f.Level.Name + " (윗면 EL " +
                    UnitUtils.ConvertFromInternalUnits(f.Top, UnitTypeId.Meters).ToString("0.000") + " m, 두께 " +
                    Math.Round(UnitUtils.ConvertFromInternalUnits(f.Top - f.Bottom, UnitTypeId.Millimeters)) + " mm)"
                    + (have.TryGetValue(f.FloorNo, out var n) ? " · 지금 동바리 " + n + "본" : "")).ToList();
                if (!App.ShowDialog(new GenerateWindow(labels, !axes.source.StartsWith("그리드 없음"), axes.source, o), data.Application))
                    return Result.Cancelled;
                List<string> report = null;
                // 쓰기 = App.Write: Revit 오류로 되돌려지면 false (4차 점검: 되돌려졌는데 "만들었습니다" 라고 하던 문제)
                if (!App.Write(doc, "구역·동바리 생성", () => { report = ZoneGenerator.Run(doc, o); }, out var err))
                    return App.WriteFailed("구역·동바리 생성", err);
                // 알림의 수 = 저장 뒤 모델에 실제로 있는 생성분 (계획한 수를 그대로 옮기지 않음)
                var made = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_GenericModel).WhereElementIsNotElementType()
                    .Where(e => e.LookupParameter("SHR_GenBy")?.AsString() == ZoneGenerator.GenBy).ToList();
                int nz = made.Count(e => e is DirectShape), ns = made.Count(e => e is FamilyInstance);
                var perFloor = made.OfType<FamilyInstance>().GroupBy(e => e.LookupParameter("SHR_ZoneID")?.AsString()?.Split('-')[0] ?? "")
                    .Select(g => g.Key + " " + g.Count() + "본");
                Dashboard.Refresh(doc);            // 새 구역 요소로 대시보드 칸을 다시 읽음 (12차 N11 — 지워진 구역 Id 를 들고 있지 않게)
                ZoneLabels.Update(doc);
                string head = o.ShoreFloors.Count == 0 && nz > 0 ? $"구역만 다시 만듦 (동바리는 그대로 {ns}본) · 구역 {nz}개"   // 층을 안 고름 = 구역만 (15차 점검)
                    : "구역 " + nz + "개 · 동바리 " + ns + "본이 모델에 있습니다.";
                string body = (ns > 0 ? "동바리: " + string.Join(", ", perFloor) + "\n\n" : "") + string.Join("\n", report) + App.WarningNote();
                if (nz == 0) App.Message(head, body, TaskDialogIcon.TaskDialogIconWarning);   // 구역 0개 = 놓치면 안 되는 경고 → 모달
                else
                {
                    var made3 = floors.Where(f => o.ShoreFloors.Contains(f.Number)).Select(f => f.FloorNo).Distinct().ToList();   // 이번에 동바리를 만든 층 번호 = SHR_Floor (V1 — 그 층만 보는 3D 뷰)
                    App.ShowResult(data.Application, "구역·동바리 생성", head, body,
                        ns > 0 && made3.Count > 0 ? ("생성한 동바리 보기", () => Dashboard.Request("생성한 동바리 보기", app => ShoreView.Show(app, made3))) : null);
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                return App.Fail("구역·동바리 생성", ex);
            }
        }
    }
}

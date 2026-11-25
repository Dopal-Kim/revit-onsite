// [준비 · 모델 진단] 층·구조 슬래브를 어떻게 인식했는지, 빠진 바닥·지붕과 이유를 보여 준다. 모델은 바꾸지 않음
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    [Transaction(TransactionMode.ReadOnly)]
    public class DiagnoseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try
            {
                var (head, body) = Report(doc);
                App.Message(head, body);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                return App.Fail("모델 진단", ex);
            }
        }

        /// <summary>진단 (제목, 본문) — [모델 진단] 창과 v6.1 열기 안내('동바리 대상 구조 슬래브 없음')가 같은 글</summary>
        internal static (string Head, string Body) Report(Document doc)
        {
                var excluded = new List<(Element El, string Reason)>();
                var floors = ZoneGenerator.DetectFloors(doc, excluded);
                var axes = ZoneGenerator.ZoneAxes(doc, floors, new GenerateOptions());
                int Count(BuiltInCategory c) => new FilteredElementCollector(doc).OfCategory(c).WhereElementIsNotElementType().GetElementCount();
                var lines = new List<string>
                {
                    "레벨 " + new FilteredElementCollector(doc).OfClass(typeof(Level)).GetElementCount() + "개 · 바닥 " + Count(BuiltInCategory.OST_Floors) +
                    " · 지붕 " + Count(BuiltInCategory.OST_Roofs) + " · 벽 " + Count(BuiltInCategory.OST_Walls) +
                    " · 구조 기둥 " + Count(BuiltInCategory.OST_StructuralColumns) + " · 그리드 " + new FilteredElementCollector(doc).OfClass(typeof(Grid)).GetElementCount(),
                    "구역 나누기: " + (floors.Count > 0 ? axes.source : "-"),
                    "",
                    "타설층 " + floors.Count + "개 (구조 슬래브가 있는 레벨):",
                };
                foreach (var f in floors)
                    lines.Add("  " + f.Label + " = " + f.Level.Name + " · 윗면 " + Math.Round(UnitUtils.ConvertFromInternalUnits(f.Top, UnitTypeId.Meters), 3) + " m · 두께 " +
                              Math.Round(UnitUtils.ConvertFromInternalUnits(f.Top - f.Bottom, UnitTypeId.Millimeters)) + " mm · 슬래브 " + f.Slabs.Count + "개" +
                              (f.Unsure.Count > 0 ? " (재료 불명 " + f.Unsure.Count + " — 콘크리트로 가정 [가정])" : "") + ShoreHeight(floors, f));
                if (floors.Count == 0) lines.Add("  없음 — 현장타설 구조 슬래브를 찾지 못했습니다. 동바리 검측 대상이 아닙니다.");
                lines.Add("");
                lines.Add(excluded.Count == 0 ? "대상에서 뺀 바닥·지붕 없음" : "대상에서 뺀 바닥·지붕 " + excluded.Count + "개:");
                foreach (var g in excluded.GroupBy(x => Short(x.Reason)).OrderByDescending(g => g.Count()))
                    lines.Add("  " + g.Key + " — " + g.Count() + "개 (예: " + string.Join(", ", g.Take(2).Select(x => Trim(x.El.Name))) + ")");
                if (excluded.Count > 0) lines.Add("자동 판별이 틀렸으면 그 바닥의 '현장타설 여부'(SHR_CastInPlace)를 예/아니오로 지정하세요.");
                // 예전 빌드로 만든 구역·동바리(형식 값 없음)는 일람표에 섞일 수 있음 → 다시 생성 안내 (4차 점검)
                int oldGen = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_GenericModel).WhereElementIsNotElementType()
                    .Count(e => e.LookupParameter("SHR_GenBy")?.AsString() == ZoneGenerator.GenBy && string.IsNullOrEmpty(e.LookupParameter("SHR_Type")?.AsString()));
                if (oldGen > 0) lines.Add("예전 방식으로 만든 구역·동바리 " + oldGen + "개가 있습니다. [구역·동바리 생성]을 다시 누르면 일람표가 정리됩니다.");
                return (floors.Count > 0 ? "타설층 " + floors.Count + "개를 인식했습니다." : "현장타설 구조 슬래브가 없습니다.", string.Join("\n", lines));
        }

        /// <summary>층 줄 끝: 동바리 높이 (그 층 슬래브 밑면 − 아래 구조 슬래브 윗면) + 경고 (3.5 m 초과·1.5 m 미만 [가정])</summary>
        static string ShoreHeight(List<SlabLevel> floors, SlabLevel f)
        {
            var (_, _, h, err) = ZoneGenerator.ShoreBase(f.Level.Document, floors, f);
            if (err != null) return "";
            double mm = UnitUtils.ConvertFromInternalUnits(h, UnitTypeId.Millimeters);
            return " · 동바리 높이 " + Math.Round(mm) + " mm" + ZoneGenerator.HeightFlag(mm);
        }

        /// <summary>이유 문구에서 요소마다 다른 부분(두께 값·재료 이름)을 떼어 묶음 이름으로</summary>
        static string Short(string r)
        {
            int i = r.IndexOfAny(new[] { ':', '(' });
            string head = i > 0 ? r.Substring(0, i).Trim() : r;
            return head.StartsWith("두께 ") ? "두께가 120–600 mm 밖 (마감·토핑·매트기초)" : head.StartsWith("윗면이") ? "윗면이 레벨에서 300 mm 넘게 떨어짐 (계단참·캐노피)" : head;
        }

        static string Trim(string s) => s.Length > 40 ? s.Substring(0, 40) + "…" : s;
    }
}

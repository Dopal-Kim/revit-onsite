// 일람표 2개(뷰·필터 준비) + 상태 색 범례(색 범례) — ui_spec §2 ①·④. 계산 없음, 판정이 적어 둔 구역 값을 보여 주기만.
// 이름으로 찾아 내용만 다시 채우므로 몇 번 눌러도 하나씩만 남고, 시트에 놓인 자리도 그대로다. 모두 열린 트랜잭션 안에서 호출
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Color = Autodesk.Revit.DB.Color;

namespace RevitOnSite
{
    public static class ReportViews
    {
        public const string ZoneTable = "Revit On-Site 구역 판정표", FloorTable = "Revit On-Site 층별 존치 현황표", LegendName = "Revit On-Site 상태 색 범례";

        /// <summary>솔리드 채우기 (이름 말고 IsSolidFill — 한글판, CLAUDE.md)</summary>
        public static ElementId SolidFill(Document doc) => new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .First(f => f.GetFillPattern().IsSolidFill && f.GetFillPattern().Target == FillPatternTarget.Drafting).Id;

        /// <summary>구역 판정표(구역마다 한 줄) + 층별 존치 현황표(층 × 판정별 구역 수). 대상 = 구역·동바리 생성이 만든 구역 상자만</summary>
        public static List<ViewSchedule> Schedules(Document doc)
        {
            var zone = Table(doc, ZoneTable, true,
                new[] { ("SHR_ZoneID", "구역"), ("SHR_Floor", "층"), ("SHR_PourDate", "타설일"), ("SHR_fck", "fck (MPa)"), ("SHR_fCyl", "공시체 강도 (MPa)"),
                        ("SHR_fEst", "추정 강도 (MPa)"), ("SHR_Decision", "판정"), ("SHR_DueDate", "예측 해체일"), ("SHR_Reason", "사유") },
                "SHR_Floor", "SHR_ZoneID");
            var floor = Table(doc, FloorTable, false, new[] { ("SHR_Floor", "층"), ("SHR_Decision", "판정") }, "SHR_Floor", "SHR_Decision");
            floor.Definition.AddField(ScheduleFieldType.Count).ColumnHeading = "구역 수";
            floor.Definition.ShowGrandTotal = true;
            floor.Definition.ShowGrandTotalCount = true;
            return new List<ViewSchedule> { zone, floor };
        }

        static ViewSchedule Table(Document doc, string name, bool itemized, (string Param, string Heading)[] cols, params string[] sortBy)
        {
            var vs = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>().FirstOrDefault(v => !v.IsTemplate && v.Name == name);
            if (vs == null)
            {
                vs = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                vs.Name = name;
            }
            var def = vs.Definition;
            def.ClearFilters();
            def.ClearSortGroupFields();
            while (def.GetFieldCount() > 0) def.RemoveField(0);
            def.IsItemized = itemized;
            var all = def.GetSchedulableFields();
            SchedulableField Find(string p) => all.FirstOrDefault(f => f.GetName(doc) == p)
                ?? throw new InvalidOperationException("검측용 매개변수가 모델에 없습니다. [매개변수 연결]을 먼저 누르세요.");
            var ids = new Dictionary<string, ScheduleFieldId>();
            foreach (var (p, h) in cols)
            {
                var f = def.AddField(Find(p));
                f.ColumnHeading = h;
                ids[p] = f.FieldId;
            }
            // 구역·동바리 생성이 만든 요소만 (숨은 열로 거름)
            var gen = def.AddField(Find("SHR_GenBy"));
            gen.IsHidden = true;
            def.AddFilter(new ScheduleFilter(gen.FieldId, ScheduleFilterType.Equal, ZoneGenerator.GenBy));
            // 구역 상자만: 생성 때 구역은 SHR_Type = "구역", 동바리는 "파이프서포트" → 긍정 조건 숨은 필터.
            // (예전 "패밀리 ≠ 동바리 패밀리" 필터는 DirectShape 에서 Revit 일람표 계산 예외 → 일람표가 열린 채 생성하면 Revit 심각한 오류 — 4차 점검 P0-1)
            var type = def.AddField(Find("SHR_Type"));
            type.IsHidden = true;
            def.AddFilter(new ScheduleFilter(type.FieldId, ScheduleFilterType.Equal, ZoneGenerator.ZoneType));
            foreach (var p in sortBy)
                def.AddSortGroupField(new ScheduleSortGroupField(ids[p], ScheduleSortOrder.Ascending));
            return vs;
        }

        /// <summary>
        /// 범례 제도 뷰: ① 상태 색 10가지 (ui_spec §6) ② 강도 표시 5단계 ③ 일정 표시 5단계 (+ 값 없음 회색) — 칸(채우기 영역) + 이름(문자).
        /// 뷰는 그대로 두고 안의 칸·문자만 다시 그림. 색이 대시보드와 같게: 솔리드 전경 + 정확한 RGB, 배경 패턴 없음, 하프톤·투명 없음 (5차 점검 L1)
        /// </summary>
        public static ViewDrafting Legend(Document doc)
        {
            var v = new FilteredElementCollector(doc).OfClass(typeof(ViewDrafting)).Cast<ViewDrafting>().FirstOrDefault(x => !x.IsTemplate && x.Name == LegendName);
            if (v == null)
            {
                var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.Drafting);
                v = ViewDrafting.Create(doc, vft.Id);
                v.Name = LegendName;
                v.Scale = 50;
            }
            else
            {
                var old = new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType().Where(e => e is FilledRegion || e is TextNote).Select(e => e.Id).ToList();
                if (old.Count > 0) doc.Delete(old);
            }
            if (v.ViewTemplateId != ElementId.InvalidElementId) v.ViewTemplateId = ElementId.InvalidElementId;   // 템플릿이 하프톤·색을 바꾸지 않게
            double Mm(double paperMm) => UnitUtils.ConvertToInternalUnits(paperMm * v.Scale, UnitTypeId.Millimeters);   // 종이 mm → 모델 길이
            var text = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            if (text == ElementId.InvalidElementId) text = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).FirstElementId();
            var solid = SolidFill(doc);
            var types = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().ToList();
            var plain = new OverrideGraphicSettings().SetHalftone(false);

            void Group(double x, string title, IEnumerable<(string Type, string Label, Color C)> items)
            {
                TextNote.Create(doc, v.Id, new XYZ(x, Mm(9), 0), title, text);
                int i = 0;
                foreach (var (typeName, label, c) in items)
                {
                    var ft = types.FirstOrDefault(t => t.Name == typeName);
                    if (ft == null) { ft = (FilledRegionType)types[0].Duplicate(typeName); types.Add(ft); }
                    ft.ForegroundPatternId = solid;
                    ft.ForegroundPatternColor = c;
                    ft.BackgroundPatternId = solid;                       // 복제 원본의 배경 무늬·색이 섞이지 않게 배경도 같은 색 솔리드
                    ft.BackgroundPatternColor = c;
                    ft.IsMasking = false;
                    double y = -i++ * Mm(7);
                    var p = new[] { new XYZ(x, y, 0), new XYZ(x + Mm(12), y, 0), new XYZ(x + Mm(12), y + Mm(5), 0), new XYZ(x, y + Mm(5), 0) };
                    var loop = new CurveLoop();
                    for (int k = 0; k < 4; k++) loop.Append(Line.CreateBound(p[k], p[(k + 1) % 4]));
                    var fr = FilledRegion.Create(doc, ft.Id, v.Id, new List<CurveLoop> { loop });
                    v.SetElementOverrides(fr.Id, plain);
                    TextNote.Create(doc, v.Id, new XYZ(x + Mm(15), y + Mm(4), 0), label, text);
                }
            }
            Group(0, "상태 색 (구역 판정)",
                ZoneEvaluation.Styles.Select(s => (ZoneEvaluation.FilterPrefix + s.Name, s.Name, new Color(s.R, s.G, s.B))));
            Group(Mm(70), "강도 표시 (추정 강도 ÷ fck)",
                ZoneOverlay.StrengthSteps.Select((s, i) => ("Revit On-Site 강도 - " + (i + 1), s.Label, s.C)).Append(("Revit On-Site 표시 - 값 없음", "값 없음", ZoneOverlay.NoData)));
            Group(Mm(130), "일정 표시 (예측 해체일까지)",
                ZoneOverlay.DueSteps.Select((s, i) => ("Revit On-Site 일정 - " + (i + 1), s.Label, s.C)).Append(("Revit On-Site 표시 - 값 없음", "날짜 없음", ZoneOverlay.NoData)));
            return v;
        }
    }
}

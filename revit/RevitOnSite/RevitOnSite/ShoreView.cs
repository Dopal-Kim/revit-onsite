// V1 [생성한 동바리 보기] (사용자 요청 11-05, docs/shore_view_spec.md §V1) — 동바리를 만든 직후 그 층 동바리만 보는 3D 뷰 "Revit On-Site 동바리".
//   단면 상자 = 고른 층 동바리 + 1 m, 다른 층 동바리·구역 상자 숨김, 슬래브·벽·기둥·보 회색 반투명 60 % [가정], 동바리는 층마다 색 하나(요소 재지정),
//   층마다 "9F 416" 글자(선 글꼴 덧그림 ZoneLabels — 결과 창 층별 본수와 같은 수). 뷰 설정만 바꿈(트랜잭션 하나 "동바리 보기") — 동바리·구역 값은 그대로
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.UI;
using Color = Autodesk.Revit.DB.Color;

namespace RevitOnSite
{
    public static class ShoreView
    {
        public const string ViewName = "Revit On-Site 동바리";
        /// <summary>[가정] 층 색 — 상태 색(ZoneEvaluation.Styles: 회·초록·빨강·파랑·노랑·연두)과 겹치지 않게 보라·청록·갈색·자주·남색·올리브 차례 (밝은 캔버스 기준)</summary>
        internal static readonly Color[] FloorColors = { new(112, 48, 160), new(0, 128, 128), new(150, 90, 30), new(190, 30, 120), new(30, 60, 150), new(110, 120, 20) };

        static int? Floor(Element e) => e.LookupParameter("SHR_Floor") is Parameter p && p.HasValue ? p.AsInteger() : null;
        static double M(double m) => UnitUtils.ConvertToInternalUnits(m, UnitTypeId.Meters);

        /// <summary>API 문맥 (결과 창 버튼 → Dashboard.Request): floors = 이번에 동바리를 만든 층 (비면 동바리가 있는 모든 층)</summary>
        internal static void Show(UIApplication app, IReadOnlyCollection<int> floors)
        {
            var ui = app.ActiveUIDocument;
            var doc = ui?.Document;
            if (doc == null || doc.IsFamilyDocument) return;
            var shores = ShoringModel.Shores(doc).ToList();
            var pick = (floors.Count > 0 ? floors : shores.Select(Floor).Where(f => f != null).Select(f => f.Value)).ToHashSet();
            var mine = shores.Where(s => Floor(s) is int f && pick.Contains(f)).ToList();
            if (mine.Count == 0) { App.Message("보여 줄 동바리가 없습니다.", "이번에 동바리를 만든 층이 없습니다 (구역만 다시 만들었으면 [구역·동바리 생성]에서 층을 고르세요)."); return; }
            var byFloor = mine.GroupBy(s => Floor(s).Value).OrderBy(g => g.Key).ToList();
            var boxes = mine.Select(s => s.get_BoundingBox(null)).Where(b => b != null).ToList();
            if (boxes.Count == 0) { App.Message("보여 줄 동바리가 없습니다.", "동바리 크기를 읽지 못했습니다 — [구역·동바리 생성]을 다시 실행하세요."); return; }   // 코드 검토 2차
            // 단면 상자 위 = 그 층 슬래브 윗면 + 1 m (32차: 동바리 위 + 1 m 라 슬래브 위로는 0.79 m 였음) — 슬래브 윗면 = 그 층 구역 상자 윗면 (구역 상자 = 슬래브 자리)
            var slabTop = ShoringModel.Zones(doc).Where(z => Floor(z) is int f && pick.Contains(f)).Select(z => z.get_BoundingBox(null)).Where(b => b != null).Select(b => b.Max.Z)
                .DefaultIfEmpty(boxes.Max(b => b.Max.Z)).Max();
            View3D v = null;
            if (!App.Write(doc, "동바리 보기", () =>
            {
                v = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(x => !x.IsTemplate && x.Name == ViewName);
                if (v == null)
                {
                    var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.ThreeDimensional);
                    v = View3D.CreateIsometric(doc, vft.Id);
                    v.Name = ViewName;
                }
                if (v.ViewTemplateId != ElementId.InvalidElementId) v.ViewTemplateId = ElementId.InvalidElementId;
                v.DisplayStyle = DisplayStyle.FlatColors;   // 검측 뷰와 같게 — 조명 때문에 층 색이 옅어지지 않게
                var gray = new Color(128, 128, 128);
                foreach (var c in new[] { BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs, BuiltInCategory.OST_Walls, BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralColumns,
                                          BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_Stairs, BuiltInCategory.OST_Windows, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Ceilings })
                {
                    if (Category.GetCategory(doc, c) == null) continue;
                    var id = new ElementId(c);
                    if (v.CanCategoryBeHidden(id)) v.SetCategoryHidden(id, false);
                    if (v.IsCategoryOverridable(id))
                        v.SetCategoryOverrides(id, new OverrideGraphicSettings().SetSurfaceTransparency(60).SetProjectionLineColor(gray).SetSurfaceForegroundPatternColor(gray));
                }
                var hide = ShoringModel.Zones(doc).Concat(shores.Where(s => !(Floor(s) is int f && pick.Contains(f))))
                    .Where(e => !e.IsHidden(v) && e.CanBeHidden(v)).Select(e => e.Id).ToList();
                if (hide.Count > 0) v.HideElements(hide);
                var show = mine.Where(e => e.IsHidden(v)).Select(e => e.Id).ToList();
                if (show.Count > 0) v.UnhideElements(show);
                var solid = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                    .FirstOrDefault(f => f.GetFillPattern().IsSolidFill && f.GetFillPattern().Target == FillPatternTarget.Drafting);   // 이름 말고 IsSolidFill (한글판)
                for (int i = 0; i < byFloor.Count; i++)
                {
                    var col = FloorColors[i % FloorColors.Length];
                    var o = new OverrideGraphicSettings().SetProjectionLineColor(col).SetSurfaceForegroundPatternColor(col).SetProjectionLineWeight(4);
                    if (solid != null) o.SetSurfaceForegroundPatternId(solid.Id);
                    foreach (var s in byFloor[i]) v.SetElementOverrides(s.Id, o);
                }
                double side = M(1.0);
                v.SetSectionBox(new BoundingBoxXYZ
                {
                    Min = new XYZ(boxes.Min(b => b.Min.X) - side, boxes.Min(b => b.Min.Y) - side, boxes.Min(b => b.Min.Z) - side),
                    Max = new XYZ(boxes.Max(b => b.Max.X) + side, boxes.Max(b => b.Max.Y) + side, Math.Max(slabTop, boxes.Max(b => b.Max.Z)) + side),
                });
                v.IsSectionBoxActive = true;
            }, out var err)) { App.WriteFailed("동바리 보기", err); return; }
            if (ui.ActiveView.Id != v.Id) ui.ActiveView = v;
            // 층마다 "9F 416" — 층 이름 = 그 층 동바리 구역 ID 의 '-' 앞 (결과 창 층별 본수와 같은 묶음).
            // 32차: 북쪽 바깥(+1.6 m)이라 단면 상자(+1 m) 밖에서 잘려 안 보였음 → 단면 상자 안. 34차: 북쪽 줄 안쪽은 계단실 판·동바리와 같은 보라라 안 읽힘
            //   → 동바리 남쪽 줄 앞 0.5 m (카메라 쪽, 단면 상자 1 m 안), 굵은 검정 글자 + 흰 바탕 판, 높이 = 동바리 높이의 절반 이하
            var text = new ZoneLabels.Set();
            // 60차 R22: 동바리가 회색 선으로만 보임(가는 파이프) → 동바리마다 채운 막대 덧그림 (가로 +30 mm). 색 = 마지막 판정 기준 동바리 상태 색, 상태가 없는 구역은 층 색
            var state = new Dictionary<string, string>();
            try
            {
                if (ZoneEvaluation.LastEvalAt(doc) is DateTime at && ProjectSettings.Read(doc) is var ps && !string.IsNullOrWhiteSpace(ps.CsvFolder))
                    foreach (var x in Engine.ShoreStatus.Status(ps.CsvFolder, at, ps.CureMin)) state[x.ZoneId] = x.State;
            }
            catch (Exception ex) { Dashboard.Log("동바리 보기: 상태 색 계산 못 함 — 층 색으로 (" + ex.Message + ")"); }
            var grow = new XYZ(M(0.03), M(0.03), 0);
            int byState = 0;
            for (int i = 0; i < byFloor.Count; i++)
            {
                var fc = FloorColors[i % FloorColors.Length];
                foreach (var s in byFloor[i])
                {
                    if (s.get_BoundingBox(null) is not BoundingBoxXYZ b) continue;
                    var c = new ColorWithTransparency(fc.Red, fc.Green, fc.Blue, 0);
                    if (state.TryGetValue(ZoneFocus.Zid(s), out var stName)) { var (r, g, bl) = Engine.ShoreStatus.ColorOf(stName); c = new ColorWithTransparency(r, g, bl, 0); byState++; }
                    text.Add(ZoneLabels.Box(b.Min - grow, b.Max + grow), c);
                }
            }
            for (int i = 0; i < byFloor.Count; i++)
            {
                var g = byFloor[i];
                var name = g.Select(ZoneFocus.Zid).Select(z => z.Split('-')[0]).FirstOrDefault(z => z.Length > 0) ?? (g.Key < 0 ? "B" + -g.Key : g.Key + "F");
                var bb = g.Select(s => s.get_BoundingBox(null)).Where(b => b != null).ToList();
                if (bb.Count == 0) continue;
                var label = $"{name} {g.Count()}";
                double z0 = bb.Min(b => b.Min.Z), z1 = bb.Max(b => b.Max.Z), x0 = bb.Min(b => b.Min.X), x1 = bb.Max(b => b.Max.X);
                double h = Math.Min(M(1.2), (z1 - z0) * 0.5), w = StrokeFont.Width(label) * h / 6;   // 글자 높이 [가정] 1.2 m, 동바리 높이의 절반 이하
                if (w > (x1 - x0) * 0.8) h *= (x1 - x0) * 0.8 / w;
                // 세운 글자판: 줄 방향 +X, 위 +Z → 앞면이 남쪽(−Y) — 기본 3D(남동쪽에서 봄)에서 바로 읽힘. 바탕 판은 글자 바로 뒤(북쪽)
                double cx = (x0 + x1) / 2, cz = (z0 + z1) / 2, y = bb.Min(b => b.Min.Y) - M(0.5), tw = StrokeFont.Width(label) * h / 6, pad = h * 0.25;
                text.Add(StrokeFont.Triangles(label, new XYZ(cx, y, cz), h, XYZ.BasisX, XYZ.BasisZ, weight: 0.75), new ColorWithTransparency(0, 0, 0, 0));
                text.Add(ZoneLabels.Box(new XYZ(cx - tw / 2 - pad, y + M(0.03), cz - h / 2 - pad), new XYZ(cx + tw / 2 + pad, y + M(0.05), cz + h / 2 + pad)),
                    new ColorWithTransparency(255, 255, 255, 0));
            }
            ZoneLabels.SetShoreText(doc, text);
            ui.RefreshActiveView();
            Dashboard.Log($"동바리 보기: {string.Join(", ", byFloor.Select(g => g.Key + "층 " + g.Count() + "본"))} · 채운 막대 상태 색 {byState} / 층 색 {mine.Count - byState}");
        }
    }
}

// 구역 선택 조회 (P8, 2026-11-04 사용자 요구 "구역을 선택하면 강조 표시해 선택적으로 조회") — "Revit On-Site 검측" 3D 뷰.
//   ① 클릭(모델 선택 / 대시보드 칸)만으로는 모델·뷰 설정을 바꾸지 않는다 (총괄 결정: 실행 취소 목록에 쌓이면 사용자의 진짜 되돌리기가 묻힘)
//      → 강조는 DirectContext3D 덧그림(ZoneLabels): 고른 구역 상자 굵은 모서리 + 상태색 반투명 덮개, 그 구역 동바리·센서를 진하게.
//   ② 사용자가 버튼으로 고를 때만 뷰 재지정 (트랜잭션 하나): 동바리 표시(대시보드 콤보) / 같은 층 보기(그 층 단면 상자 + 같은 층 다른 구역 하프톤) / 전체 층.
//      동바리 표시: 선택 구역만(기본 — 뷰에는 판정 대상 층 동바리, 고른 구역 동바리는 덧그림으로) / 판정 대상 층만 / 전부 / 숨김.
//      판정 대상 층 = 상태 4·5·9(조치 필요) 구역이 있는 최상층, 없으면 3(양생 중) 최상층, 없으면 전부.
//   해체 판정 직후에는 그 판정 트랜잭션 안에서 동바리 표시를 맞춘다 (ApplyShores — 실행 취소 항목 하나 그대로)
//   ③ V2 동바리 상태 (docs/shore_view_spec.md §V2, 대시보드 [색] '동바리 상태'): 검측 뷰에 보이는 동바리를 기록 기준 상태색으로 덧그림 (Engine ShoreStatus — 판정 시각 기준),
//      구역 단위로 설치 검측 테두리·최근 24 h 확인 없음 점선, 무단 해체 의심은 빨강 굵게 + 1 Hz 깜빡임. 화면에만 — 모델·뷰 설정·실행 취소 목록 그대로
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    public static class ZoneFocus
    {
        public enum ShoreMode { Selected, JudgeFloor, All, None }
        public static readonly string[] ShoreModeNames = { "선택 구역만", "판정 대상 층만", "전부", "숨김" };

        static ShoreMode mode = ShoreMode.Selected;
        static string highlighted;   // 마지막 덧그림의 (문서, 선택 구역) — 같으면 다시 만들지 않음
        public static ShoreMode Mode => mode;

        static double Mm(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);

        static View3D StatusView(Document doc) => new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
            .FirstOrDefault(x => !x.IsTemplate && x.Name == ZoneEvaluation.StatusViewName);

        internal static string Zid(Element e) => e.LookupParameter("SHR_ZoneID")?.AsString() ?? "";
        static int? Floor(Element e) => e.LookupParameter("SHR_Floor") is Parameter p && p.HasValue ? p.AsInteger() : null;
        static int Status(Element e) => e.LookupParameter("SHR_Status") is Parameter p && p.HasValue ? p.AsInteger() : -1;

        /// <summary>고른 요소 → 구역 ID: 구역 상자 그대로 + 동바리는 그 동바리의 구역 (구역 상자와 슬래브가 같은 자리라 클릭이 동바리에 먼저 걸릴 때도 구역이 강조되게)</summary>
        static HashSet<string> ZoneIdsOf(Document doc, IEnumerable<ElementId> ids) =>
            ids.Select(doc.GetElement).Where(e => e != null && (ShoringModel.IsZone(e) || ShoringModel.IsShore(e))).Select(Zid).Where(z => z.Length > 0).ToHashSet();

        internal static List<Element> SelectedZones(UIDocument ui)
        {
            var ids = ZoneIdsOf(ui.Document, ui.Selection.GetElementIds());
            return ids.Count == 0 ? new List<Element>() : ShoringModel.Zones(ui.Document).Where(z => ids.Contains(Zid(z))).ToList();
        }

        /// <summary>선택이 바뀌었을 때 (App.OnSelectionChanged): 검측 뷰가 활성이면 덧그림 갱신 요청. 무엇이 골라졌는지 대시보드 기록에 (6차 점검 G1 진단)</summary>
        internal static void SelectionChanged(Document doc, IEnumerable<ElementId> selected)
        {
            if (doc?.ActiveView?.Name != ZoneEvaluation.StatusViewName) return;
            var els = selected.Select(doc.GetElement).Where(e => e != null).ToList();
            Dashboard.Log("검측 뷰 선택: " + (els.Count == 0 ? "없음" : string.Join(", ", els.GroupBy(e => ShoringModel.IsZone(e) ? "구역" : ShoringModel.IsShore(e) ? "동바리" : e.Category?.Name ?? e.GetType().Name).Select(g => g.Key + " " + g.Count()))));
            var key = Key(doc, ZoneIdsOf(doc, els.Select(e => e.Id)));
            if (key == highlighted) return;
            Dashboard.Request("구역 강조", Highlight);
        }

        static string Key(Document doc, IEnumerable<string> ids) => Dashboard.Key(doc) + "|" + string.Join(",", ids.OrderBy(s => s, StringComparer.Ordinal));

        /// <summary>판정 값이 바뀐 뒤 (해체 판정·현장 입력 반영): 덮개 색을 새 상태로 (동바리 상태 보기가 켜져 있으면 그것도 다시 계산)</summary>
        internal static void Rehighlight(UIApplication app)
        {
            highlighted = null;
            Highlight(app);
            if (shoreStatusOn) ApplyShoreStatus(app, true, openView: false);
        }

        // ── V2 동바리 상태 보기
        static bool shoreStatusOn, blinkPending;
        static System.Windows.Threading.DispatcherTimer blinkTimer;

        /// <summary>Revit 종료·문서 닫기 (Dashboard.Closing): 깜빡임 타이머를 멈춤 — 종료 뒤에도 0.5 초마다 요청이 걸리던 것 (코드 검토 2차)</summary>
        internal static void StopBlink() { blinkTimer?.Stop(); blinkPending = false; }

        /// <summary>켜기 실패 (판정 전·검측 뷰 없음·계산 오류): 사용자가 켠 경우만 알리고 모드를 끔 (대시보드 [색] 콤보도 되돌림). 자동 갱신(휴대폰 저장 등)은 기록만.
        /// 알림 = 대시보드 안내 줄 (회귀 v6-1 R25: 외부 이벤트 안의 모달 창이 이벤트를 막아, 콤보를 다시 고를 때마다 창이 쌓이고 요청이 10분 넘게 대기)</summary>
        static void Fail(bool user, string title, string body)
        {
            Dashboard.Log("동바리 상태 " + (user ? "켜지 못함: " : "갱신 건너뜀: ") + title + " " + body);
            if (!user) return;
            shoreStatusOn = false;
            StopBlink();
            Dashboard.ResetColorMode();
            Dashboard.TimelineNote("⚠ " + title + " " + body);
        }

        /// <summary>대시보드 [색] 콤보 (UI 스레드): 켜면 API 문맥에서 상태 계산·덧그림 + 검측 뷰 열기, 끄면 덧그림 지움</summary>
        internal static void SetShoreStatus(bool on)
        {
            if (on == shoreStatusOn) return;
            shoreStatusOn = on;
            Dashboard.Request(on ? "동바리 상태 보기" : "동바리 상태 끔", app => ApplyShoreStatus(app, on));
        }

        /// <summary>API 문맥, 트랜잭션 없음: on = 마지막 판정 시각의 ShoreStatus 로 덧그림 (openView 면 검측 뷰를 엶), off = 지움</summary>
        internal static void ApplyShoreStatus(UIApplication app, bool on, bool openView = true)
        {
            var ui = app.ActiveUIDocument;
            var doc = ui?.Document;
            if (!on) StopBlink();   // 문서가 없어도 타이머는 멈춤 (코드 검토 2차)
            if (doc == null || doc.IsFamilyDocument) return;
            if (!on)
            {
                ZoneLabels.SetShoreStatus(doc, new ZoneLabels.Set(), new ZoneLabels.Set());
                RestoreJudgeColors(doc);
                if (ui.ActiveView?.Name == ZoneEvaluation.StatusViewName) ui.RefreshActiveView();
                return;
            }
            var v = StatusView(doc);
            if (v == null || ZoneEvaluation.LastEvalAt(doc) is not DateTime at)
            {
                Fail(openView, "동바리 상태를 보여 줄 수 없습니다.", "[판정]을 먼저 하세요 — 검측 뷰와 판정 기준 시각이 필요합니다.");
                return;
            }
            var s = ProjectSettings.Read(doc);
            List<Engine.ShoreState> st;
            Dictionary<string, List<(string Item, string Shore, DateTime T)>> ng;
            HashSet<string> insNg;
            try
            {
                st = Engine.ShoreStatus.Status(s.CsvFolder, at, s.CureMin);
                ng = Engine.ShoreInspect.OpenNg(s.CsvFolder, at);
                insNg = Engine.ShoreInspect.OpenNg(s.CsvFolder, at, Engine.ShoreInspect.Install).Keys.ToHashSet();   // 판정 '존치 (설치 결함 기록)' 과 같은 계산
            }
            catch (Exception ex) { Fail(openView, "동바리 상태를 계산하지 못했습니다.", ex.Message); return; }
            if (openView && ui.ActiveView.Id != v.Id) ui.ActiveView = v;
            if (!ZoneEvaluation.FiltersDimmed(doc, v) && !App.Write(doc, "동바리 상태 보기 — 판정 색 흐리게", () => ZoneEvaluation.DimFilters(doc, v, true), out var derr))
                Dashboard.Log("판정 색을 흐리게 하지 못함 (덧그림은 그대로): " + derr);   // 35차: 판정 색에 묻혀 상태 색이 안 보였음
            bool blink = BuildShoreStatus(doc, v, st, ng, insNg);
            if (blink)
            {
                if (blinkTimer == null)
                {
                    blinkTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };   // 1 Hz 깜빡임 = 0.5 초마다 켜짐·꺼짐
                    blinkTimer.Tick += (_, _) =>
                    {
                        ZoneLabels.BlinkOn = !ZoneLabels.BlinkOn;
                        if (blinkPending) return;                                        // Revit 이 바쁘면 프레임을 쌓지 않음
                        blinkPending = true;
                        Dashboard.Request("동바리 상태 깜빡임", BlinkFrame, quiet: true);
                    };
                }
                blinkTimer.Start();
            }
            else blinkTimer?.Stop();
            if (ui.ActiveView?.Name == ZoneEvaluation.StatusViewName) ui.RefreshActiveView();
            Dashboard.Log($"동바리 상태 {at:yyyy-MM-dd HH:mm}: " + string.Join(", ", st.GroupBy(x => x.State).Select(g => g.Key + " " + g.Count()))
                + $" · 설치 검측 필요/NG {st.Count(x => x.Install.Length > 0)} · 24 h 확인 없음 {st.Count(x => x.Stale.Length > 0)}");
        }

        /// <summary>깜빡임 한 프레임 (API 문맥): 검측 뷰가 열려 있고 4D 재생 중이 아닐 때만 다시 그림 — 4D 재생 중엔 4D 색이 우선</summary>
        static void BlinkFrame(UIApplication app)
        {
            blinkPending = false;
            var ui = app.ActiveUIDocument;
            if (shoreStatusOn && !Dashboard.Playing && ui?.ActiveView?.Name == ZoneEvaluation.StatusViewName) ui.RefreshActiveView();
        }

        /// <summary>덧그림 만들기 → 깜빡일 것이 있는지. 동바리마다(뷰에서 숨기지 않은 것, 단면 상자 높이 안): 채움 = 상태색 상자, 해체됨 = 윤곽만,
        /// 무단 해체 의심 = 빨강 굵은 윤곽 + 깜빡이는 상자. 구역마다: 설치 검측 필요/NG 테두리, 최근 24 h 확인 없음 점선, 무단 해체 의심은 구역 테두리도 깜빡임 (상세 동바리가 없는 층도 보이게)</summary>
        static bool BuildShoreStatus(Document doc, View3D v, List<Engine.ShoreState> st, Dictionary<string, List<(string Item, string Shore, DateTime T)>> ng, HashSet<string> insNg)
        {
            int insDrawn = 0;
            string insRing = null;
            var insSkip = new List<string>();
            var by = st.ToDictionary(x => x.ZoneId);
            var bad = ng.SelectMany(kv => kv.Value.Select(x => (kv.Key, x.Shore))).ToHashSet();   // F16: 풀리지 않은 점검 NG (모든 단계) — (구역, 동바리 ID 또는 '*')
            var sid = bad.Count > 0 ? ShoringModel.ShoreIds(doc) : new();
            int ngN = 0;
            var (bottom, top) = ZoneLabels.SectionZ(v);
            double tol = Mm(100);
            bool Inside(BoundingBoxXYZ b) => b.Max.Z >= bottom - tol && b.Min.Z <= top + tol;
            static ColorWithTransparency C(string name) { var (r, g, b) = Engine.ShoreStatus.ColorOf(name); return new ColorWithTransparency(r, g, b, 0); }
            var fill = new ZoneLabels.Set();
            var blink = new ZoneLabels.Set();
            var grow = new XYZ(Mm(20), Mm(20), 0);
            int drawn = 0, hiddenN = 0, outN = 0, suspect = 0;
            foreach (var s in ShoringModel.Shores(doc))
            {
                if (!by.TryGetValue(Zid(s), out var x) || s.get_BoundingBox(null) is not BoundingBoxXYZ b) continue;
                if (s.IsHidden(v)) { hiddenN++; continue; }
                if (!Inside(b)) { outN++; continue; }
                drawn++;
                if (x.State == Engine.ShoreStatus.Removed) fill.Add(ZoneLabels.Edges(b, Mm(12)), C(x.State));
                else if (x.State == Engine.ShoreStatus.Suspect) { fill.Add(ZoneLabels.Edges(b, Mm(45)), C(x.State)); blink.Add(ZoneLabels.Box(b.Min - grow, b.Max + grow), C(x.State)); }
                else fill.Add(ZoneLabels.Box(b.Min - grow, b.Max + grow), C(x.State));
                if (sid.TryGetValue(s.Id, out var me) && bad.Contains((me.ZoneId, me.Id))) { fill.Add(ZoneLabels.Edges(b, Mm(45)), C(Engine.ShoreStatus.ShoreNg)); ngN++; }
            }
            foreach (var z in ShoringModel.Zones(doc))
            {
                var zid = Zid(z);
                bool isNg = insNg.Contains(zid);
                if (!by.TryGetValue(zid, out var x) || z.get_BoundingBox(null) is not BoundingBoxXYZ bb) { if (isNg) insSkip.Add(zid + " 상태·상자 없음"); continue; }
                if (!Inside(bb)) { if (isNg) insSkip.Add(zid + " 단면 상자 밖"); continue; }
                // 37차: 테가 안 보였음 — ShoreState.Install 은 '해체됨'이면 비워져(협력업체 '뺐음' 등) 테가 빠졌고, 54차의 150 mm 넓힌 테는 무단 해체 의심 테와 다른 경로.
                // → 풀리지 않은 설치 검측 NG 구역이면 상태와 상관없이, 보이는 것이 확인된 무단 해체 의심 테와 같은 Edges(bb, 160 mm)
                // 60차: 그래도 안 보임 — 구역 판정 색 '설치 결함'(215,48,39)이 테 빨강(215,25,25)과 같아 묻히고, 테 윗절반은 단면 상자(구역 윗면 +50 mm)에 잘림.
                // → 구역 윗면 바로 위(단면 상자 안) 납작한 띠: 바깥 300 mm 빨강 + 안쪽 80 mm 진회색 (빨강 구역 위에서도 보이게)
                if (isNg)
                {
                    double z0 = bb.Max.Z + Mm(5), z1 = Math.Max(z0 + Mm(10), Math.Min(bb.Max.Z + Mm(40), top - Mm(5)));
                    fill.Add(ZoneLabels.Ring(bb, z0, z1, 0, Mm(300)), C(Engine.ShoreStatus.InstallNg));
                    fill.Add(ZoneLabels.Ring(bb, z0, z1 + Mm(2), Mm(80), 0), new ColorWithTransparency(40, 40, 40, 0));
                    insDrawn++;
                    insRing ??= $"테 Z = 구역 윗면 +{(z0 - bb.Max.Z) * 304.8:0}~+{(z1 - bb.Max.Z) * 304.8:0} mm (단면 상자 윗면 {(top >= double.MaxValue / 2 ? "없음" : $"+{(top - bb.Max.Z) * 304.8:0} mm")}) · 두께 {(z1 - z0) * 304.8:0} mm · 폭 바깥 300 + 안 80 mm";
                }
                else if (x.Install.Length > 0) fill.Add(ZoneLabels.Edges(bb, Mm(90)), C(x.Install));
                if (bad.Contains((x.ZoneId, "*"))) fill.Add(ZoneLabels.Edges(bb, Mm(120)), C(Engine.ShoreStatus.ShoreNg));   // 구역 전체 NG (동바리를 고르지 않은 기록)
                if (x.Stale.Length > 0)
                {
                    var e = new XYZ(Mm(150), Mm(150), Mm(150));
                    fill.Add(ZoneLabels.DashedEdges(new BoundingBoxXYZ { Min = bb.Min - e, Max = bb.Max + e }, Mm(60), Mm(400)), C(Engine.ShoreStatus.Stale));
                }
                if (x.State == Engine.ShoreStatus.Suspect)   // 34차 ⓒ: 판정 색 그대로 보였음 → 늘 빨간 굵은 테 + 깜빡일 때 구역 상자를 빨갛게 덮음
                {
                    var e2 = new XYZ(Mm(30), Mm(30), Mm(30));
                    fill.Add(ZoneLabels.Edges(bb, Mm(160)), C(x.State));
                    blink.Add(ZoneLabels.Box(bb.Min - e2, bb.Max + e2), C(x.State));
                    suspect++;
                }
            }
            ZoneLabels.SetShoreStatus(doc, fill, blink);
            Dashboard.Log($"동바리 상태 덧그림: 동바리 {drawn}개 (뷰에서 숨김 {hiddenN} · 단면 상자 밖 {outN}) · NG 동바리 {ngN} · 설치 결함 구역 테 {insDrawn}/{insNg.Count}"
                + (insRing != null ? $" ({insRing})" : "") + (insSkip.Count > 0 ? " (안 그림: " + string.Join(", ", insSkip) + ")" : "") + $" · 무단 해체 의심 구역 {suspect} · 삼각형 채움 {fill.Tris.Count} · 깜빡 {blink.Tris.Count}");
            return blink.Tris.Count > 0;
        }

        /// <summary>DocumentSaving(As): 흐린 판정 색이 .rvt 에 저장되지 않게 저장 직전 원래 색 (Revit 이 이 이벤트 안 수정을 허용 — RevitAPI.xml). 실패해도 저장은 그대로</summary>
        internal static void BeforeSave(Document doc)
        {
            try { if (doc != null && !doc.IsFamilyDocument && !doc.IsReadOnly) RestoreJudgeColors(doc); }
            catch (Exception ex) { Dashboard.Log("저장 전 판정 색 되돌림 실패: " + ex.Message); }
        }

        /// <summary>동바리 상태 모드를 끄면(또는 모드가 꺼진 채 흐린 색이 남아 있으면 — Revit 을 다시 켠 경우) 검측 뷰 판정 색을 되돌림 (트랜잭션 하나, 필요할 때만)</summary>
        static void RestoreJudgeColors(Document doc)
        {
            var v = StatusView(doc);
            if (v != null && ZoneEvaluation.FiltersDimmed(doc, v) && !App.Write(doc, "판정 색 되돌림", () => ZoneEvaluation.DimFilters(doc, v, false), out var err))
                Dashboard.Log("판정 색을 되돌리지 못함: " + err);
        }

        /// <summary>API 문맥: 지금 선택으로 강조 덧그림을 만들고 화면만 다시 그림 (동바리 상태 모드가 꺼졌는데 흐린 판정 색이 남아 있으면 그것만 트랜잭션으로 되돌림)</summary>
        internal static void Highlight(UIApplication app)
        {
            var ui = app.ActiveUIDocument;
            var doc = ui?.Document;
            if (doc != null && !doc.IsFamilyDocument && !shoreStatusOn) RestoreJudgeColors(doc);
            if (doc == null || doc.IsFamilyDocument) return;
            var sel = SelectedZones(ui);
            var key = Key(doc, sel.Select(Zid));
            if (key == highlighted) return;
            highlighted = key;
            var hi = new ZoneLabels.Set();
            var cover = new ZoneLabels.Set();
            if (sel.Count > 0)
            {
                var dark = new ColorWithTransparency(20, 20, 20, 0);
                var ids = sel.Select(Zid).ToHashSet();
                foreach (var z in sel)
                {
                    if (z.get_BoundingBox(null) is not BoundingBoxXYZ bb) continue;
                    var st = ZoneEvaluation.Styles.FirstOrDefault(s => s.Code == Status(z));
                    var col = st.Name == null ? new ColorWithTransparency(120, 120, 120, 0) : new ColorWithTransparency(st.R, st.G, st.B, 0);   // 덮개 색 (반투명은 덧그림 효과의 투명도 55 %)
                    hi.Add(ZoneLabels.Edges(bb, Mm(120)), dark);
                    var e = new XYZ(Mm(40), Mm(40), Mm(40));
                    cover.Add(ZoneLabels.Box(bb.Min - e, bb.Max + e), col);
                }
                // 그 구역 동바리: 실제 형상을 감싸는 진한 상자 (뷰에서 숨겨져 있어도 보이게 — 동바리 표시 '선택 구역만')
                var grow = new XYZ(Mm(15), Mm(15), 0);
                foreach (var s in ShoringModel.Shores(doc))
                    if (ids.Contains(Zid(s)) && s.get_BoundingBox(null) is BoundingBoxXYZ b) hi.Add(ZoneLabels.Box(b.Min - grow, b.Max + grow), new ColorWithTransparency(40, 40, 40, 0));
                // 그 구역 센서 표식 (현장 입력이 놓은 SNS_Sensor): 같은 층·구역 평면 안
                var boxes = sel.Select(z => (F: Floor(z), B: z.get_BoundingBox(null))).Where(x => x.B != null).ToList();
                foreach (var s in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().Where(f => f.Symbol?.FamilyName == "SNS_Sensor"))
                {
                    if ((s.Location as LocationPoint)?.Point is not XYZ p || s.get_BoundingBox(null) is not BoundingBoxXYZ b) continue;
                    if (boxes.Any(x => x.F == Floor(s) && p.X >= x.B.Min.X && p.X <= x.B.Max.X && p.Y >= x.B.Min.Y && p.Y <= x.B.Max.Y))
                        hi.Add(ZoneLabels.Edges(b, Mm(40)), new ColorWithTransparency(230, 60, 150, 0));
                }
            }
            ZoneLabels.SetHighlight(doc, hi, cover);
            if (ui.ActiveView?.Name == ZoneEvaluation.StatusViewName) ui.RefreshActiveView();
        }

        /// <summary>대시보드 동바리 표시 고르기 → 검측 뷰 숨김·보임 (트랜잭션 하나)</summary>
        internal static void SetMode(ShoreMode m)
        {
            if (m == mode) return;
            mode = m;
            Dashboard.Request("동바리 표시 " + ShoreModeNames[(int)m], app => ViewAndWrite(app, "동바리 표시", (doc, v, sel) => ApplyShores(doc, v, sel)));
        }

        /// <summary>같은 층 보기 (상황별 탭·대시보드): 선택 구역(없으면 판정 대상 층)의 층만 단면 상자로, 같은 층 다른 구역은 하프톤</summary>
        internal static void SameFloor(UIApplication app)
        {
            var ui = app.ActiveUIDocument;
            var doc = ui.Document;
            var zones = ShoringModel.Zones(doc).ToList();
            var sel = SelectedZones(ui);
            int? floor = sel.Count > 0 ? Floor(sel[0]) : JudgeFloor(zones);
            if (floor == null) { App.Message("보여 줄 층이 없습니다.", "구역을 하나 고르거나 해체 판정을 먼저 하세요."); return; }
            ViewAndWrite(app, "같은 층 보기", (d, v, s) =>
            {
                var fz = zones.Where(z => Floor(z) == floor).ToList();
                var fb = fz.Select(z => z.get_BoundingBox(null)).Where(b => b != null).ToList();
                var fs = ShoringModel.Shores(doc).Where(x => Floor(x) == floor).Select(x => x.get_BoundingBox(null)).Where(b => b != null).ToList();
                var all = zones.Select(z => z.get_BoundingBox(null)).Where(b => b != null).ToList();
                double side = Mm(2000);
                v.SetSectionBox(new BoundingBoxXYZ
                {
                    Min = new XYZ(all.Min(b => b.Min.X) - side, all.Min(b => b.Min.Y) - side, fb.Concat(fs).Min(b => b.Min.Z) - Mm(50)),
                    Max = new XYZ(all.Max(b => b.Max.X) + side, all.Max(b => b.Max.Y) + side, fb.Max(b => b.Max.Z) + Mm(50)),
                });
                v.IsSectionBoxActive = true;
                var keep = s.Select(Zid).ToHashSet();
                var ghost = new OverrideGraphicSettings().SetHalftone(true);
                foreach (var z in zones) v.SetElementOverrides(z.Id, Floor(z) == floor && keep.Count > 0 && !keep.Contains(Zid(z)) ? ghost : new OverrideGraphicSettings());
                ApplyShores(d, v, s);
            });
        }

        /// <summary>전체 층 (대시보드): 판정 직후와 같은 단면 상자 (판정 대상 층 슬래브 윗면까지) + 구역 하프톤 지움. 판정 전이면 단면 상자 끔</summary>
        internal static void AllFloors(UIApplication app)
        {
            var doc = app.ActiveUIDocument.Document;
            var zones = ShoringModel.Zones(doc).ToList();
            var judged = zones.Where(z => !string.IsNullOrEmpty(z.LookupParameter("SHR_Decision")?.AsString())).Select(Zid).ToList();
            ViewAndWrite(app, "전체 층 보기", (d, v, s) =>
            {
                if (!ZoneEvaluation.FocusOnEvaluated(d, v, judged)) v.IsSectionBoxActive = false;
                foreach (var z in zones) v.SetElementOverrides(z.Id, new OverrideGraphicSettings());
                ApplyShores(d, v, s);
            });
        }

        /// <summary>검측 뷰를 (없으면 만들고) 쓰기 한 번 → 그 뷰를 열고 덧그림 갱신</summary>
        static void ViewAndWrite(UIApplication app, string what, Action<Document, View3D, List<Element>> body)
        {
            var ui = app.ActiveUIDocument;
            var doc = ui?.Document;
            if (doc == null || doc.IsFamilyDocument) return;
            var sel = SelectedZones(ui);
            View3D v = null;
            if (!App.Write(doc, what, () =>
            {
                v = StatusView(doc);
                if (v == null) { v = ZoneEvaluation.StatusView(doc); ZoneEvaluation.ApplyFilters(doc, v); }
                body(doc, v, sel);
            }, out var err)) { App.WriteFailed(what, err); return; }
            if (ui.ActiveView.Id != v.Id) ui.ActiveView = v;
            highlighted = null;
            Highlight(app);
        }

        /// <summary>판정 대상 층 = 상태 4·5·9(조치 필요) 구역이 있는 최상층, 없으면 3(양생 중) 최상층, 없으면 null (= 전부)</summary>
        static int? JudgeFloor(List<Element> zones)
        {
            foreach (var codes in new[] { new[] { 4, 5, 9 }, new[] { 3 } })
            {
                var f = zones.Where(z => codes.Contains(Status(z))).Select(Floor).Where(x => x != null).ToList();
                if (f.Count > 0) return f.Max();
            }
            return null;
        }

        /// <summary>열린 트랜잭션 안에서: 검측 뷰 v 의 동바리 숨김·보임을 표시 방식에 맞춤. sel = 지금 고른 구역 ('판정 대상 층만' 은 고른 구역의 층 우선)</summary>
        internal static void ApplyShores(Document doc, View3D v, List<Element> sel)
        {
            var zones = ShoringModel.Zones(doc).ToList();
            var selFloors = sel.Select(Floor).Where(f => f != null).ToHashSet();
            int? jf = JudgeFloor(zones);
            bool Show(Element s) => mode switch
            {
                ShoreMode.All => true,
                ShoreMode.None => false,
                ShoreMode.JudgeFloor when selFloors.Count > 0 => selFloors.Contains(Floor(s)),
                _ => jf == null || Floor(s) == jf,   // 선택 구역만: 뷰에는 판정 대상 층, 고른 구역 동바리는 덧그림
            };
            var hide = new List<ElementId>();
            var unhide = new List<ElementId>();
            foreach (var s in ShoringModel.Shores(doc))
            {
                bool show = Show(s), hidden = s.IsHidden(v);
                if (show && hidden) unhide.Add(s.Id);
                else if (!show && !hidden && s.CanBeHidden(v)) hide.Add(s.Id);
            }
            if (hide.Count > 0) v.HideElements(hide);
            if (unhide.Count > 0) v.UnhideElements(unhide);
        }
    }
}

// 해체 판정 (P2): CSV 폴더 → RevitOnSite.Engine.Evaluator (engine.py/evaluate.py 이식, 일치 시험 통과) → 구역·동바리 SHR_* 기입 + 상태 색 뷰 필터
// 계산은 엔진 한 곳에서만 (CLAUDE.md). 여기서는 결과를 모델에 옮기고 색만 칠한다.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using RevitOnSite.Engine;
using Color = Autodesk.Revit.DB.Color;

namespace RevitOnSite
{
    public static class ZoneEvaluation
    {
        /// <summary>상태 코드 = 정의서 §3.5 (SHR_Status), 색 = ui_spec §6</summary>
        public static readonly (int Code, string Name, byte R, byte G, byte B)[] Styles =
        {
            (0, "미검측", 160, 160, 160), (1, "설치 검측 합격", 171, 190, 209), (2, "설치 결함", 215, 48, 39), (3, "양생 중", 69, 117, 180),
            (4, "공시체 시험 필요", 254, 224, 139), (5, "해체 승인 요청 가능", 145, 207, 96),
            (6, "감리 승인", 26, 152, 80), (7, "해체 완료", 115, 115, 115), (8, "재받침", 128, 100, 162), (9, "존치 연장 경고", 244, 109, 67),
        };

        public const string FilterPrefix = "Revit On-Site 상태 - ";   // 필터 이름에 ':' 등 금지 문자 불가

        /// <summary>엔진 판정 → 상태 코드. 존치는 기준일이 예측 해체일 전이면 정상 양생 중(3), 지났는데도 존치면 존치 연장 경고(9) — 층수 게이트 NG 도 같은 규칙</summary>
        public static int StatusOf(ZoneRow r, DateTime at) =>
            r.Decision == "해체 승인 요청 가능" ? 5 :
            r.Decision == "공시체 시험 필요" ? 4 :
            r.Decision == "존치 (설치 결함 기록)" ? 2 :   // F16 규칙 ⑦: 설치 검측 NG 미해결
            DateOnly.FromDateTime(at) < r.PredictedDue ? 3 : 9;

        /// <summary>
        /// 데이터 폴더 zones.csv 에 없는 모델 구역(구역 상자 SHR_ZoneID·SHR_Floor·평면 범위 m)을 끝에 덧붙인다 — 10차 점검 N5: 데모 데이터는 9–12F 24구역뿐인데
        /// 모델은 15F 까지라 타설 전 확인·현장 입력이 위층 구역을 몰랐음. 기존 줄과 다른 파일은 그대로 (판정 결과 불변: Python 으로 샘플 3날짜 확인).
        /// 안전장치: shoring\sample(회귀 기준) 은 안 바꿈, 둘 다 있는 구역의 좌표가 모델과 1 mm 넘게 다르면(좌표계가 다름) 안 바꿈. 반환 = 안내 한 줄(바꿀 것 없으면 null)
        /// </summary>
        public static string SyncZonesCsv(Document doc, string folder)
        {
            // zones.csv 를 Excel 등으로 열어 두면 쓰기가 막힘 → 판정·현장 입력은 파일 그대로 계속하고 안내만 (오류 점검 ②)
            try { return SyncZonesCsvCore(doc, folder); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                return "zones.csv 가 다른 프로그램(Excel 등)에서 열려 있거나 쓸 수 없어 모델 구역·slab_mm 을 쓰지 못했습니다 — 파일 그대로 계속합니다 (" + ex.Message + ")";
            }
        }

        static string SyncZonesCsvCore(Document doc, string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !System.IO.Directory.Exists(folder)) return null;
            if (Engine.Evaluator.IsSample(folder)) return null;
            var model = new List<(string Id, int Floor, double[] R)>();
            foreach (var z in ShoringModel.Zones(doc))
            {
                var id = z.LookupParameter("SHR_ZoneID")?.AsString();
                if (string.IsNullOrEmpty(id) || z.LookupParameter("SHR_Floor") is not Parameter fp || !fp.HasValue || z.get_BoundingBox(null) is not BoundingBoxXYZ bb) continue;
                double M(double f) => Math.Round(UnitUtils.ConvertFromInternalUnits(f, UnitTypeId.Meters), 3) + 0.0;   // + 0.0: -0 → 0
                model.Add((id, fp.AsInteger(), new[] { M(bb.Min.X), M(bb.Min.Y), M(bb.Max.X), M(bb.Max.Y) }));
            }
            if (model.Count == 0) return null;
            var p = System.IO.Path.Combine(folder, "zones.csv");
            var have = Evaluator.Read(folder, "zones.csv");
            var byId = model.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
            foreach (var r in have)
                if (r.GetValueOrDefault("zone_id") is string zid && byId.TryGetValue(zid, out var m))
                {
                    var c = new[] { "x0", "y0", "x1", "y1" }.Select(k => double.TryParse(r.GetValueOrDefault(k), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN).ToArray();
                    if (r.GetValueOrDefault("floor") != m.Floor.ToString(CultureInfo.InvariantCulture) || c.Zip(m.R).Any(q => !(Math.Abs(q.First - q.Second) <= 0.001)))
                        return $"zones.csv 의 {zid} 위치·층이 모델과 달라(좌표 기준이 다른 듯) 모델 구역을 더하지 않았습니다";
                }
            var known = have.Select(r => r.GetValueOrDefault("zone_id")).ToHashSet();
            var add = byId.Values.Where(m => !known.Contains(m.Id)).OrderBy(m => m.Floor).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();
            // D1: slab_mm = 구역 평면과 겹치는 그 층 구조 슬래브 두께 최댓값 (하중비교, 보수 측). 못 찾으면 빈칸 → evaluate 가 210 mm [가정]
            var slabs = ZoneGenerator.SlabsByFloor(doc);
            string SlabMm((string Id, int Floor, double[] R) m)
            {
                var hit = slabs.TryGetValue(m.Floor, out var l)
                    ? l.Where(q => Math.Min(q.X1, m.R[2]) > Math.Max(q.X0, m.R[0]) && Math.Min(q.Y1, m.R[3]) > Math.Max(q.Y0, m.R[1])).Select(q => q.Mm).ToList() : new List<double>();
                return hit.Count > 0 ? hit.Max().ToString("0", CultureInfo.InvariantCulture) : "";
            }
            string msg = null;
            if (add.Count > 0)
            {
                string text = System.IO.File.Exists(p) ? System.IO.File.ReadAllText(p) : "";
                string nl = text.Contains("\r\n") ? "\r\n" : "\n";
                var head = text.Length > 0 ? text.Split('\n')[0].TrimEnd('\r').TrimStart('﻿').Split(',') : new[] { "zone_id", "floor", "x0", "y0", "x1", "y1", "slab_mm" };
                static string F(double v) { var s = v.ToString("0.###", CultureInfo.InvariantCulture); return s.Contains('.') ? s : s + ".0"; }   // Python float 표기처럼 10 → 10.0
                var sb = new System.Text.StringBuilder();
                if (text.Length == 0) sb.Append(string.Join(",", head)).Append(nl);
                else if (!text.EndsWith("\n")) sb.Append(nl);
                foreach (var m in add)
                    sb.Append(string.Join(",", head.Select(h => h switch
                    {
                        "zone_id" => m.Id, "floor" => m.Floor.ToString(CultureInfo.InvariantCulture),
                        "x0" => F(m.R[0]), "y0" => F(m.R[1]), "x1" => F(m.R[2]), "y1" => F(m.R[3]), "slab_mm" => SlabMm(m), _ => "",
                    }))).Append(nl);
                System.IO.File.AppendAllText(p, sb.ToString(), new System.Text.UTF8Encoding(false));
                msg = $"zones.csv 에 모델 구역 {add.Count}개를 {(have.Count == 0 ? "썼습니다" : "더했습니다")} ({add[0].Id} …)";   // 비었거나 머리만 있던 파일 = 썼습니다 (N13)
            }
            // 총괄 11-05 (b): 열 없는 zones.csv 엔 slab_mm 열을 더해 모델 값으로, 있으면 빈칸만 (다른 칸·줄 순서 그대로, Engine.ZonesCsv.FillSlab)
            var (added, filled) = Engine.ZonesCsv.FillSlab(p, byId.Values.ToDictionary(m => m.Id, m => SlabMm(m)));
            string slabMsg = added ? $"zones.csv 에 slab_mm 열을 더함 (모델 슬래브 두께, {filled}구역)"
                : filled > 0 ? $"zones.csv slab_mm 빈칸 {filled}구역을 모델 슬래브 두께로 채움" : null;
            // v6.2 W11: shore_mm = 그 층 동바리 높이 (ShoreBase — 아래 슬래브 윗면 ~ 이 슬래브 밑면), 범위 밖 알림만 씀. 같은 방식으로 빈칸만 채움
            var floors = ZoneGenerator.DetectFloors(doc);
            var shoreH = new Dictionary<int, string>();
            foreach (var f in floors)
                if (ZoneGenerator.ShoreBase(doc, floors, f) is var (_, _, h, err) && err == null && h > 0)
                    shoreH[f.FloorNo] = Math.Round(UnitUtils.ConvertFromInternalUnits(h, UnitTypeId.Millimeters)).ToString("0", CultureInfo.InvariantCulture);
            var (hAdded, hFilled) = Engine.ZonesCsv.FillSlab(p, byId.Values.ToDictionary(m => m.Id, m => shoreH.GetValueOrDefault(m.Floor, "")), "shore_mm");
            if (hAdded || hFilled > 0) slabMsg = (slabMsg == null ? "" : slabMsg + " · ") + $"zones.csv shore_mm (동바리 높이) {hFilled}구역";
            return msg == null ? slabMsg : slabMsg == null ? msg : msg + " · " + slabMsg;
        }

        /// <summary>마지막 판정 기준일(PRJ_LastEvalDate) 09:00 — 대시보드·승인 기록·보고서·한중 경고가 같은 시각을 쓴다. 판정 전이면 null</summary>
        public static DateTime? LastEvalAt(Document doc)
        {
            int last = doc.ProjectInformation?.LookupParameter("PRJ_LastEvalDate") is Parameter p && p.HasValue ? p.AsInteger() : 0;
            return last > 19000101 && DateTime.TryParseExact(last.ToString(CultureInfo.InvariantCulture), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.AddHours(9) : null;
        }

        /// <summary>기록을 바꾼 뒤: 마지막 판정 기준일로 다시 판정(실행 취소 한 번) → 대시보드·강조 새로 고침. 판정 기준일이 없으면 null, 실패면 false + err (창·명령 공통)</summary>
        internal static bool? Reapply(Autodesk.Revit.UI.UIApplication app, Document doc, string folder, double cureMin, string title, out string err)
        {
            err = null;
            if (LastEvalAt(doc) is not DateTime at) return null;
            bool ok;
            try { ok = App.Write(doc, title, () => Apply(doc, folder, at, cureMin), out err); }
            catch (Exception ex) { ok = false; err = ex.Message; }
            if (ok) { Dashboard.Refresh(doc); ZoneFocus.Rehighlight(app); }
            return ok;
        }

        /// <summary>열린 트랜잭션 안에서 호출. 반환 = (판정 행, 결과 문장)</summary>
        public static (List<ZoneRow> Rows, List<string> Report) Apply(Document doc, string csvFolder, DateTime at, double cureMin)
        {
            var rows = Evaluator.Evaluate(csvFolder, at, cureMin);   // 입력 오류는 InvalidDataException(한국어 문구)
            var done = Approval.StatusOverride(csvFolder, at);        // approvals.csv 의 사람 기록 6 감리 승인 기록 · 7 해체 완료 · 8 재받침이 판정 상태보다 우선 (field_server 와 같게) — 판정일까지의 기록만
            var later = Approval.StatusOverride(csvFolder);           // 기록은 있지만 판정일보다 뒤 날짜인 구역 → 그 판정에선 판정 상태로 (20차)
            var byZone = rows.GroupBy(r => r.ZoneId).ToDictionary(g => g.Key, g => g.First());
            var els = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_GenericModel).WhereElementIsNotElementType()
                .Select(e => (E: e, Id: e.LookupParameter("SHR_ZoneID")?.AsString()))
                .Where(x => !string.IsNullOrEmpty(x.Id) && byZone.ContainsKey(x.Id)).ToList();
            string shareMsg = null;
            if (doc.IsWorkshared)   // v6.2 W7: 다른 사용자가 가진 요소는 건너뜀 (Engine.Worksharing.Split — 순수 함수 시험)
            {
                var (write, _, msg) = Engine.Worksharing.Split(els.Select(x =>
                {
                    var st = WorksharingUtils.GetCheckoutStatus(doc, x.E.Id, out string owner);
                    return new Engine.Worksharing.Item(x.E.Id.ToString(), x.Id, st == CheckoutStatus.OwnedByOtherUser, owner ?? "");
                }));
                els = els.Where(x => write.Contains(x.E.Id.ToString())).ToList();
                shareMsg = msg;
            }
            int nz = 0, ns = 0, kept = 0;
            foreach (var (e, id) in els)
            {
                var r = byZone[id];
                int cur = e.LookupParameter("SHR_Status") is Parameter sp && sp.HasValue ? sp.AsInteger() : -1;
                if (done.TryGetValue(id, out var code)) Set(e, "SHR_Status", code);
                else if ((cur == 6 || cur == 7 || cur == 8) && !later.ContainsKey(id)) { kept++; continue; }   // approvals.csv 밖에서 적힌 6·7·8 은 판정으로 덮지 않음
                else Set(e, "SHR_Status", StatusOf(r, at));
                if (e is DirectShape)
                {
                    Set(e, "SHR_Decision", r.Decision);
                    Set(e, "SHR_Reason", r.Reason);
                    Set(e, "SHR_DueDate", Ymd(r.PredictedDue.ToDateTime(TimeOnly.MinValue)));
                    if (DateTime.TryParse(r.PourDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var pd)) Set(e, "SHR_PourDate", Ymd(pd));
                    Set(e, "SHR_Mix", r.Mix);
                    Set(e, "SHR_fck", r.Fck);
                    if (r.FCyl.HasValue) Set(e, "SHR_fCyl", r.FCyl.Value);
                    Set(e, "SHR_fEst", r.FEstMin);
                    Set(e, "SHR_LevelsReq", ShoringEngine.MinLevels);
                    nz++;
                }
                else ns++;
            }
            // 11-12 2-1: 판정에서 빠진 구역(타설 기록 취소·판정일 뒤 타설) — 전에 적힌 판정 상태(3·4·5·9)·결론을 지움 (옛 결론이 모델에 남지 않게). 사람 기록 6·7·8·설치 0·1·2 는 그대로
            int cleared = 0;
            foreach (var e in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_GenericModel).WhereElementIsNotElementType())
            {
                var id = e.LookupParameter("SHR_ZoneID")?.AsString();
                if (string.IsNullOrEmpty(id) || byZone.ContainsKey(id) || e.LookupParameter("SHR_Status") is not Parameter sp2 || !sp2.HasValue || sp2.AsInteger() is not (3 or 4 or 5 or 9)) continue;
                if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, e.Id) == CheckoutStatus.OwnedByOtherUser) continue;
                Set(e, "SHR_Status", 0);
                if (e is DirectShape)   // 결론·타설·강도 값도 (일람표·IFC 에 옛 값이 남지 않게 — 91차 revit-ui #2). SHR_DueDate·SHR_PourDate = 정수 YYYYMMDD
                {
                    foreach (var k in new[] { "SHR_Decision", "SHR_Reason", "SHR_Mix" }) Set(e, k, "");
                    foreach (var k in new[] { "SHR_DueDate", "SHR_PourDate" }) Set(e, k, 0);
                    foreach (var k in new[] { "SHR_fCyl", "SHR_fEst", "SHR_fck" }) Set(e, k, 0.0);
                    cleared++;
                }
            }
            var report = new List<string> { "판정 기준일 " + at.ToString("yyyy-MM-dd HH:mm") + " · 판정 " + rows.Count + "구역 → 모델 구역 " + nz + "개, 동바리 " + ns + "본 기입" };
            if (cleared > 0) report.Add("타설 기록이 없는 구역 " + cleared + "개는 옛 판정을 지움 (미검측)");
            // 결과 목록 = 모델에 적힌 상태와 같게: 승인 기록(6·7·8)이 있으면 그 상태로 (11차 점검 N8: 제목은 3, 본문은 요청 가능 4 로 어긋났음)
            foreach (var g in rows.GroupBy(r => done.TryGetValue(r.ZoneId, out var c) ? c : StatusOf(r, at)).OrderByDescending(g => g.Key))
                report.Add((g.Key == 6 ? "감리 승인 기록" : Styles.First(s => s.Code == g.Key).Name) + " " + g.Count() + ": " + string.Join(", ", g.Select(r => r.ZoneId)));
            if (kept > 0) report.Add("감리 승인·해체 완료·재받침으로 기록된 요소 " + kept + "개는 그대로 둠");
            if (shareMsg != null) report.Add(shareMsg);   // W7
            var noModel = byZone.Keys.Except(els.Where(x => x.E is DirectShape).Select(x => x.Id)).ToList();
            if (noModel.Count > 0) report.Add("모델에 없는 구역(구역·동바리 생성을 먼저 실행): " + string.Join(", ", noModel));
            return (rows, report);
        }

        const int DimTransparency = 80;                       // [가정] 동바리 상태 모드의 판정 색 = 회색 20 % (투명 80 %)
        static readonly Color DimGray = new(150, 150, 150);

        static FillPatternElement Solid(Document doc) => new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .First(f => f.GetFillPattern().IsSolidFill && f.GetFillPattern().Target == FillPatternTarget.Drafting);   // 이름 말고 IsSolidFill (한글판)

        static IEnumerable<(ParameterFilterElement F, (int Code, string Name, byte R, byte G, byte B) S)> StatusFilters(Document doc, View view)
        {
            var byName = new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>().GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First());
            var on = view.GetFilters().ToHashSet();
            foreach (var s in Styles)
                if (byName.TryGetValue(FilterPrefix + s.Name, out var f) && on.Contains(f.Id)) yield return (f, s);
        }

        /// <summary>검측 뷰 판정 색이 흐려져 있나 (동바리 상태 모드, 35차)</summary>
        public static bool FiltersDimmed(Document doc, View view) => view != null && StatusFilters(doc, view).Any(x => view.GetFilterOverrides(x.F.Id).Transparency == DimTransparency);

        /// <summary>35차: 동바리 상태 모드에선 판정 색 필터를 회색·투명 80 % 로 (상태 색 덧그림이 보이게), 끄면 ApplyFilters 와 같은 색으로 되돌림. 열린 트랜잭션 안에서</summary>
        public static void DimFilters(Document doc, View view, bool dim)
        {
            var solid = Solid(doc);
            foreach (var (f, s) in StatusFilters(doc, view).ToList())
            {
                var c = dim ? DimGray : new Color(s.R, s.G, s.B);
                view.SetFilterOverrides(f.Id, new OverrideGraphicSettings().SetSurfaceForegroundPatternId(solid.Id).SetSurfaceForegroundPatternColor(c)
                    .SetCutForegroundPatternId(solid.Id).SetCutForegroundPatternColor(c).SetSurfaceTransparency(dim ? DimTransparency : 0));
            }
        }

        /// <summary>상태별 뷰 필터를 만들고(있으면 재사용) 그 뷰에 솔리드 채우기 색으로 적용. 열린 트랜잭션 안에서 호출</summary>
        public static int ApplyFilters(Document doc, View view)
        {
            if (!view.AreGraphicsOverridesAllowed()) throw new InvalidOperationException("이 뷰(" + view.Name + ")는 색 재지정을 할 수 없습니다. 3D 뷰나 평면 뷰에서 다시 누르세요.");
            ElementId statusId = null;
            var it = doc.ParameterBindings.ForwardIterator();
            while (it.MoveNext()) if (it.Key.Name == "SHR_Status" && it.Key is InternalDefinition d) statusId = d.Id;
            if (statusId == null) throw new InvalidOperationException("판정 상태를 적을 매개변수가 없습니다. [매개변수 연결]을 먼저 누르세요.");
            var solid = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .First(f => f.GetFillPattern().IsSolidFill && f.GetFillPattern().Target == FillPatternTarget.Drafting);   // 이름 말고 IsSolidFill (한글판)
            var cats = new List<ElementId> { new ElementId(BuiltInCategory.OST_GenericModel) };
            var existing = new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>().ToDictionary(f => f.Name);
            int n = 0;
            foreach (var s in Styles)
            {
                string name = FilterPrefix + s.Name;
                if (!existing.TryGetValue(name, out var f))
                    f = ParameterFilterElement.Create(doc, name, cats, new ElementParameterFilter(ParameterFilterRuleFactory.CreateEqualsRule(statusId, s.Code)));
                if (!view.GetFilters().Contains(f.Id)) view.AddFilter(f.Id);
                var c = new Color(s.R, s.G, s.B);
                view.SetFilterOverrides(f.Id, new OverrideGraphicSettings()
                    .SetSurfaceForegroundPatternId(solid.Id).SetSurfaceForegroundPatternColor(c)
                    .SetCutForegroundPatternId(solid.Id).SetCutForegroundPatternColor(c));
                view.SetFilterVisibility(f.Id, true);
                n++;
            }
            // 판정 전(상태값 없음) 구역·동바리는 거의 투명하게 — 위층의 회색 상자가 판정층 색을 덮지 않게 (3차 점검)
            string pending = FilterPrefix + "판정 전";
            if (!existing.TryGetValue(pending, out var pf))
                pf = ParameterFilterElement.Create(doc, pending, cats, new ElementParameterFilter(ParameterFilterRuleFactory.CreateHasNoValueParameterRule(statusId)));
            if (!view.GetFilters().Contains(pf.Id)) view.AddFilter(pf.Id);
            view.SetFilterOverrides(pf.Id, new OverrideGraphicSettings().SetSurfaceTransparency(85).SetHalftone(true));
            view.SetFilterVisibility(pf.Id, true);
            // 구역 경계 (P8): 구역 상자(SHR_Type = "구역") 외곽선을 늘 진한 회색 실선으로 — 같은 상태색 구역끼리도 경계가 보이게. 선이라 상태색(면) 필터와 겹치지 않음
            ElementId typeId = null;
            it.Reset();
            while (it.MoveNext()) if (it.Key.Name == "SHR_Type" && it.Key is InternalDefinition td) typeId = td.Id;
            if (typeId != null)
            {
                string edge = FilterPrefix + "구역 경계";
                if (!existing.TryGetValue(edge, out var ef))
                    ef = ParameterFilterElement.Create(doc, edge, cats, new ElementParameterFilter(ParameterFilterRuleFactory.CreateEqualsRule(typeId, ZoneGenerator.ZoneType)));
                if (!view.GetFilters().Contains(ef.Id)) view.AddFilter(ef.Id);
                view.SetFilterOverrides(ef.Id, new OverrideGraphicSettings().SetProjectionLineWeight(3).SetProjectionLineColor(new Color(50, 50, 50)));
                view.SetFilterVisibility(ef.Id, true);
            }
            return n;
        }

        /// <summary>
        /// 3D 뷰 단면 상자 윗면 = 판정 결과가 요청 가능(5)·존치 연장(9)·공시체 시험 필요(4)인 구역 중 최고 높이 + 50 mm (없으면 판정한 구역 최고 높이 + 50 mm).
        /// 타설 최상층(양생 중 파랑)까지 남기면 그 아래 판정층 색이 덮이므로 "조치가 필요한 최상층" 기준 (3차 점검). 열린 트랜잭션 안에서 호출
        /// </summary>
        public static bool FocusOnEvaluated(Document doc, View3D v, IEnumerable<string> zoneIds)
        {
            var ids = new HashSet<string>(zoneIds);
            var all = new FilteredElementCollector(doc).OfClass(typeof(DirectShape)).Cast<Element>()
                .Where(e => e.LookupParameter("SHR_GenBy")?.AsString() == ZoneGenerator.GenBy).Select(e => (e, id: e.LookupParameter("SHR_ZoneID")?.AsString(), bb: e.get_BoundingBox(null)))
                .Where(x => x.bb != null).ToList();
            var hit = all.Where(x => ids.Contains(x.id)).ToList();
            var act = hit.Where(x => x.e.LookupParameter("SHR_Status") is Parameter p && p.HasValue && new[] { 4, 5, 9 }.Contains(p.AsInteger())).ToList();
            if (act.Count > 0) hit = act;
            if (hit.Count == 0) return false;
            double m = UnitUtils.ConvertToInternalUnits(1000, UnitTypeId.Millimeters), side = 2 * m;
            var box = new BoundingBoxXYZ
            {
                Min = new XYZ(all.Min(x => x.bb.Min.X) - side, all.Min(x => x.bb.Min.Y) - side, all.Min(x => x.bb.Min.Z) - 10 * m),
                // 윗면 = 그 슬래브 윗면 + 50 mm. 예전 + 1 m 는 위층 동바리(이 슬래브 위에 선 것)를 1 m 토막으로 남겨 "슬래브 위로 솟아" 보였음 (6차 화면 ui6_01)
                Max = new XYZ(all.Max(x => x.bb.Max.X) + side, all.Max(x => x.bb.Max.Y) + side, hit.Max(x => x.bb.Max.Z) + UnitUtils.ConvertToInternalUnits(50, UnitTypeId.Millimeters)),
            };
            v.SetSectionBox(box);
            v.IsSectionBoxActive = true;
            return true;
        }

        public const string StatusViewName = "Revit On-Site 검측";

        /// <summary>
        /// 검측 3D 뷰 (없으면 만들고 있으면 갱신, ui_spec "뷰·필터 준비"): 음영, 벽·창·문·계단·기둥·보 투명도 80 % + 하프톤, 슬래브 하프톤 + 투명도 50 %
        /// (구역 상자가 슬래브와 같은 자리라 하프톤만으로는 상태색이 가려짐), 구역·동바리(일반 모델)는 그대로 → 상태 색 필터는 ApplyFilters. 열린 트랜잭션 안에서 호출
        /// </summary>
        public static View3D StatusView(Document doc)
        {
            var v = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(x => !x.IsTemplate && x.Name == StatusViewName);
            if (v == null)
            {
                var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.ThreeDimensional);
                v = View3D.CreateIsometric(doc, vft.Id);
                v.Name = StatusViewName;
            }
            if (v.ViewTemplateId != ElementId.InvalidElementId) v.ViewTemplateId = ElementId.InvalidElementId;   // 템플릿이 필터·표시를 잠그지 않게
            v.DisplayStyle = DisplayStyle.FlatColors;   // 일관된 색상: 음영 조명 때문에 상태색(파랑 등)이 옅어지지 않게 (3차 점검)
            void Ghost(BuiltInCategory c, int transparency, bool halftone)
            {
                if (Category.GetCategory(doc, c) == null) return;
                var id = new ElementId(c);
                if (v.CanCategoryBeHidden(id)) v.SetCategoryHidden(id, false);
                if (v.IsCategoryOverridable(id)) v.SetCategoryOverrides(id, new OverrideGraphicSettings().SetSurfaceTransparency(transparency).SetHalftone(halftone));
            }
            foreach (var c in new[] { BuiltInCategory.OST_Walls, BuiltInCategory.OST_Windows, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Stairs,
                                      BuiltInCategory.OST_StairsRailing, BuiltInCategory.OST_CurtainWallPanels, BuiltInCategory.OST_CurtainWallMullions,
                                      BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_Ceilings })
                Ghost(c, 80, true);
            // 슬래브는 구역 상자와 같은 자리 → 하프톤(바탕색과 섞임)·반투명이면 상태색이 탁해짐(5차 점검: 노랑→올리브). 하프톤 없이 투명도 85 % 로 상태색이 비치게
            Ghost(BuiltInCategory.OST_Floors, 85, false);
            Ghost(BuiltInCategory.OST_Roofs, 85, false);
            return v;
        }

        static int Ymd(DateTime d) => d.Year * 10000 + d.Month * 100 + d.Day;

        static void Set(Element e, string name, object v)
        {
            var p = e.LookupParameter(name);
            if (p == null || p.IsReadOnly) return;
            switch (v) { case string s: p.Set(s); break; case int i: p.Set(i); break; default: p.Set(Convert.ToDouble(v)); break; }
        }
    }
}

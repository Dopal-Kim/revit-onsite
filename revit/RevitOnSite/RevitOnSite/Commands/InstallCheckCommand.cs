// [설치 검측] 구역마다 동바리 높이·이음·수평연결재·형식·1본 하중 검사 — 계산 = RevitOnSite.Engine.InstallRules (install_rules.py 이식, 일치 시험 통과)
// 기록: 동바리 SHR_Load_kN·SHR_Allowable_kN(비어 있을 때)·SHR_InspectDate·SHR_InspectResult [가정] 1 합격 / 2 불합격 / 3 확인 필요(구조계산·시스템 동바리 등 서류)
//       구역 SHR_Status: 합격 1, 불합격 2, 확인 필요 0(미검측) + SHR_Reason 에 사유 (이미 타설 이후 상태 3 이상이면 그대로)
// 1본 하중 = 그 구역 동바리 격자 간격(X·Y 각각 서로 다른 좌표 사이 가장 작은 간격) × 설계 하중 = 안쪽 동바리 기준 (가장자리는 더 적게 받음, 4차 점검)
// 쓰기 = App.Write, 결과 알림 = 저장 뒤 모델의 오늘 검측 기록을 다시 센 값
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitOnSite.Engine;

namespace RevitOnSite
{
    [Transaction(TransactionMode.Manual)]
    public class InstallCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try
            {
                var shores = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                    .Where(f => f.Symbol?.Family?.Name == ZoneGenerator.FamilyName && !string.IsNullOrEmpty(f.LookupParameter("SHR_ZoneID")?.AsString())).ToList();
                var zones = new FilteredElementCollector(doc).OfClass(typeof(DirectShape)).Cast<Element>()
                    .Where(e => !string.IsNullOrEmpty(e.LookupParameter("SHR_ZoneID")?.AsString())).GroupBy(e => e.LookupParameter("SHR_ZoneID").AsString()).ToDictionary(g => g.Key, g => g.First());
                // v6.2 W6 구역 본수 모드: 모델 동바리 0·해체 기록 없음·사양 X·Y 간격 있는 구역 (ShoreInput.SpecCount)
                var folder0 = ProjectSettings.Read(doc).CsvFolder;
                var specZones = new List<(string Zid, int Floor, double X, double Y, int N)>();
                if (!string.IsNullOrWhiteSpace(folder0) && System.IO.Directory.Exists(folder0))
                {
                    var withModel = shores.Select(f => f.LookupParameter("SHR_ZoneID").AsString()).ToHashSet();
                    var gone = Evaluator.Read(folder0, ShoreInput.Removed).Select(r => (r.GetValueOrDefault("zone_id") ?? "").Trim()).ToHashSet();
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    foreach (var r in Evaluator.Read(folder0, "zones.csv"))
                    {
                        var zid = (r.GetValueOrDefault("zone_id") ?? "").Trim();
                        if (zid.Length == 0 || withModel.Contains(zid) || gone.Contains(zid) || !int.TryParse(r.GetValueOrDefault("floor"), System.Globalization.NumberStyles.AllowLeadingSign, inv, out var fl)) continue;
                        var rect = (double.Parse(r["x0"]!, inv), double.Parse(r["y0"]!, inv), double.Parse(r["x1"]!, inv), double.Parse(r["y1"]!, inv));
                        if (ShoreInput.SpecCount(folder0, zid, fl, rect) is int n && n > 0 && ShoreInput.SpecOf(folder0, fl, zid) is { } sp)
                            specZones.Add((zid, fl, double.Parse(sp["spacing_x_mm"]!.Trim(), inv) / 1000, double.Parse(sp["spacing_y_mm"]!.Trim(), inv) / 1000, n));
                    }
                }
                if (shores.Count == 0 && specZones.Count == 0) { App.Message("검사할 동바리가 없습니다.", "[구역·동바리 생성]으로 동바리를 먼저 배치하거나 [동바리 사양]에 X·Y 간격을 넣으세요 (구역 본수 모드)."); return Result.Cancelled; }
                int today = DateTime.Today.Year * 10000 + DateTime.Today.Month * 100 + DateTime.Today.Day;
                // 타설 두께 = 그 층 대표 슬래브 두께 (구역 상자 높이는 윗면을 20 mm 올려 슬래브와 다름 — ZoneGenerator.TopPlateMm). 층을 못 찾으면 상자 높이
                var slabT = ZoneGenerator.DetectFloors(doc).GroupBy(s => s.FloorNo).ToDictionary(g => g.Key, g => g.First().Top - g.First().Bottom);
                var issues = new List<(string Floor, string Item, string Res, string Reason, string Src)>();
                var plumb = new List<string>();   // 구역마다 연직도 결과 (OK·NG·필요) — 결과 창에 합계 한 줄 (OK 도 보이게, 21차 점검)
                // 연직도 = 모델로 못 재는 수동 항목 → 데이터 폴더 install_inspection.csv (SHR-V) 의 그 구역 마지막 기록, 없으면 확인 필요 (install_rules.plumb_check)
                var folder = ProjectSettings.Read(doc).CsvFolder;
                var insRows = !string.IsNullOrWhiteSpace(folder) && System.IO.Directory.Exists(folder) ? Evaluator.InstallRows(folder) : new();
                // 설치 서류 4열은 선택 — 한 열도 없으면 서류 항목을 세지 않고 안내만 (안 쓰는 현장이 전부 '확인 필요'로 내려가지 않게, F5 입회와 같은 방식)
                string[] docCols = { "assembly_dwg_no", "struct_review_no", "expert_confirm", "support_method" };
                bool useDocs = insRows.Count > 0 && docCols.Any(insRows[0].ContainsKey);
                if (!App.Write(doc, "설치 검측", () =>
                {
                    foreach (var zg in shores.GroupBy(f => f.LookupParameter("SHR_ZoneID").AsString()).OrderBy(z => z.First().LookupParameter("SHR_Floor")?.AsInteger() ?? 0).ThenBy(z => z.Key, StringComparer.Ordinal))
                    {
                        var list = zg.ToList(); var s0 = list[0];
                        double h = Mm(s0.LookupParameter("높이")?.AsDouble() ?? 0);
                        // 이음: 기록 없는(매개변수 없음·값 없음) 본이 있으면 null(확인 필요, 감사 A-14) — 단 기록된 값이 이미 기준을 넘으면 그 값(불합격)
                        var sv = list.Select(f => f.LookupParameter("SHR_Splices")).Select(p => p != null && p.HasValue ? p.AsInteger() : (int?)null).ToList();
                        int? splices = sv.Contains(null) && !(sv.Max() > InstallRules.MaxSplices) ? null : sv.Max();
                        // 수평연결재: 한 본이라도 "아니오" 기록 → false(불합격), 전부 "예" → true, 그 밖(기록 없음 섞임) → null(확인 필요) — install_rules.py 와 같은 3가지
                        var bv = list.Select(f => f.LookupParameter("SHR_HorizBrace")).Select(p => p != null && p.HasValue ? p.AsInteger() : -1).ToList();
                        bool? brace = bv.Contains(0) ? false : bv.All(x => x == 1) ? true : (bool?)null;
                        string type = s0.LookupParameter("SHR_Type")?.AsString();
                        if (string.IsNullOrWhiteSpace(type)) type = "파이프서포트";
                        double allow = s0.LookupParameter("SHR_Allowable_kN") is Parameter ap && ap.HasValue && ap.AsDouble() > 0 ? ap.AsDouble() : InstallRules.PipeAllowableKn;
                        double tMm = 0, load = 0;
                        if (zones.TryGetValue(zg.Key, out var z) && z.get_BoundingBox(null) is BoundingBoxXYZ bb)
                        {
                            tMm = Mm(z.LookupParameter("SHR_Floor") is Parameter fp && fp.HasValue && slabT.TryGetValue(fp.AsInteger(), out var st) ? st : bb.Max.Z - bb.Min.Z);
                            load = InstallRules.PostLoad(Spacing(list, p => p.X), Spacing(list, p => p.Y), InstallRules.DesignLoad(tMm));
                        }
                        var checks = InstallRules.CheckShore(h, splices, brace, type, tMm, tMm > 0 ? load : (double?)null, allow);
                        checks.Add(InstallRules.PlumbCheck(insRows, zg.Key));
                        plumb.Add(checks[^1].Result);
                        // 설치 서류 (F4, install_rules.install_docs_check): install_inspection.csv 선택 열 — 열마다 그 구역의 날짜 순 마지막 빈칸 아닌 값
                        // shortcut: 여러 줄을 열별로 합침 (Python 은 한 줄을 받음) — 구역 서류 표가 따로 생기면 그 파일로
                        var docRow = new Dictionary<string, string>();
                        foreach (var r in insRows.Where(r => r.GetValueOrDefault("zone_id") == zg.Key).OrderBy(r => r.GetValueOrDefault("date") ?? "", StringComparer.Ordinal))
                            foreach (var k in docCols)
                                if (!string.IsNullOrWhiteSpace(r.GetValueOrDefault(k))) docRow[k] = r[k];
                        if (useDocs) checks.AddRange(InstallRules.InstallDocsCheck(docRow, h));
                        int res = checks.Any(c => c.Result == "NG") ? 2 : checks.Any(c => c.Result != "OK") ? 3 : 1;
                        foreach (var f in list)
                        {
                            if (tMm > 0) Set(f, "SHR_Load_kN", Math.Round(load, 2));
                            if (!(f.LookupParameter("SHR_Allowable_kN")?.HasValue ?? false)) Set(f, "SHR_Allowable_kN", allow);
                            Set(f, "SHR_InspectDate", today);
                            Set(f, "SHR_InspectResult", res);
                        }
                        if (z != null)
                        {
                            var sp = z.LookupParameter("SHR_Status");
                            int cur = sp != null && sp.HasValue ? sp.AsInteger() : 0;
                            // 총괄 결정 (5차 점검 B3): 합격 1 · 불합격 2(설치 결함) · 확인 필요 0(미검측, 검측이 안 끝남) + 사유. 타설 이후(3 이상)는 그대로
                            if (cur <= 2)
                            {
                                Set(z, "SHR_Status", res == 1 ? 1 : res == 2 ? 2 : 0);
                                var why = string.Join("; ", checks.Where(c => c.Result != "OK").Select(c => c.Item + " " + c.Result + (string.IsNullOrEmpty(c.Reason) ? "" : " (" + c.Reason + ")")));
                                Set(z, "SHR_Reason", res == 1 ? "" : (res == 2 ? "설치 결함: " : "확인 필요: ") + why);
                            }
                        }
                        foreach (var c in checks.Where(c => c.Result != "OK"))
                            issues.Add((zg.Key.Split('-')[0], c.Item, c.Result, c.Reason, c.Source));
                    }
                }, out var err))
                    return App.WriteFailed("설치 검측", err);
                foreach (var (zid, fl, x, y, n) in specZones)   // W6: 본 단위 모델 없음 — 1본 하중만 (부담 면적 = 사양 X×Y), 나머지는 현장 확인
                {
                    string sp = $"사양 계산 {n}본 · 부담 면적 {x:0.##}×{y:0.##} m";
                    if (slabT.TryGetValue(fl, out var st))
                    {
                        double load = InstallRules.PostLoad(x, y, InstallRules.DesignLoad(Mm(st)));
                        if (load > InstallRules.PipeAllowableKn) issues.Add((zid.Split('-')[0], "1본 하중", "NG", $"{load:0.0} kN > 허용 {InstallRules.PipeAllowableKn:0.#} kN ({sp})", "KDS 21 50 00 표 2.6-1 ÷ 표 1.8-1"));
                    }
                    issues.Add((zid.Split('-')[0], "본 단위 검사", "필요", $"본 단위 모델 없음 — 현장 확인 (높이·이음·수평연결재, {sp})", "v6.2 구역 본수 모드"));
                }

                // 결과 수 = 저장 뒤 모델에서: 오늘 검측한 동바리를 구역별로 (구역 안 동바리는 같은 결과)
                var done = shores.Where(f => f.IsValidObject && f.LookupParameter("SHR_InspectDate")?.AsInteger() == today)
                    .GroupBy(f => f.LookupParameter("SHR_ZoneID").AsString()).Select(g => g.First().LookupParameter("SHR_InspectResult")?.AsInteger() ?? 0).ToList();
                int ok = done.Count(r => r == 1), ng = done.Count(r => r == 2), need = done.Count(r => r == 3);
                // 큰 글자는 짧게, 본문은 층·항목별로 묶어서 (화면 점검 L5·L6)
                // 상하 정렬 (가이드라인 4.3): 층마다 바로 아래층 동바리까지 수평 거리 — 허용값 [입력] 이 없어 편차만 (install_rules.align_check)
                var pts = shores.Where(f => f.IsValidObject && f.Location is LocationPoint).GroupBy(f => f.LookupParameter("SHR_Floor")?.AsInteger() ?? 0)
                    .ToDictionary(g => g.Key, g => g.Select(f => ((LocationPoint)f.Location).Point).Select(p => (Mm(p.X), Mm(p.Y))).ToList());
                var align = InstallRules.AlignCheck(pts, ProjectSettings.Read(doc).AlignTolMm);   // 허용값 = 프로젝트 설정 [입력], 없으면 편차만 (F9)
                string alignText = $"\n\n연직도 (install_inspection.csv SHR-V 수동 항목, {(insRows.Count > 0 ? "데이터 폴더" : "파일 없음")}): OK {plumb.Count(r => r == "OK")}구역 · NG {plumb.Count(r => r == "NG")} · 기록 없음 {plumb.Count(r => r == "필요")}"
                    + (align.Count == 0 ? "" : "\n\n상하 정렬 (" + InstallRules.AlignSource + "):\n" + string.Join("\n", align.Select(a => a.Reason)))
                    + (useDocs ? "" : "\n\n설치 서류: install_inspection.csv 에 assembly_dwg_no·struct_review_no·expert_confirm·support_method 열을 넣으면 조립도·구조검토서·관계전문가 확인·지지 방식을 점검합니다");
                var groups = issues.GroupBy(i => (i.Floor, i.Item, i.Res)).Select(g => g.Key.Floor + " " + g.Key.Item + " " + g.Key.Res + " (" + g.Count() + "구역): " + g.First().Reason + " — " + g.First().Src.Replace(" 제", " 제")).ToList();   // 조항 번호가 줄바꿈되지 않게
                App.Message("합격 " + ok + " / 불합격 " + ng + " / 확인 필요 " + need + (specZones.Count > 0 ? $" · 사양 계산 구역 {specZones.Count}" : ""),
                    "구역 " + done.Count + "개를 검사했습니다." + (specZones.Count > 0 ? $" 모델 동바리 없는 구역 {specZones.Count}개는 사양 간격으로 1본 하중만 셈 (본 단위 모델 없음 — 현장 확인)." : "") + "\n" + (groups.Count == 0 ? "모든 구역이 높이·이음·수평연결재·형식·1본 하중·연직도 기준을 만족합니다." :
                    string.Join("\n", groups.Take(8)) + (groups.Count > 8 ? "\n… 외 " + (groups.Count - 8) + "건 — 동바리 '검측 결과' 값으로 확인" : "")) + alignText + App.WarningNote());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                return App.Fail("설치 검측", ex);
            }
        }

        /// <summary>
        /// 구역 동바리 격자 간격 (m): 서로 다른 좌표(1 mm 반올림) 사이 가장 작은 양의 간격. 한 줄뿐이면 1.2 m [가정] (생성 기본 간격).
        /// 구역 면적 ÷ 본수는 기둥·코어·개구부 면적까지 안쪽 동바리에 얹어 과대(KBIM 5F 15.4 kN, 4차 점검)
        /// </summary>
        static double Spacing(List<FamilyInstance> list, Func<XYZ, double> axis)
        {
            var v = list.Select(f => (f.Location as LocationPoint)?.Point).Where(p => p != null)
                .Select(p => Math.Round(Mm(axis(p)))).Distinct().OrderBy(x => x).ToList();
            var gaps = v.Zip(v.Skip(1), (a, b) => b - a).Where(d => d > 0).ToList();
            return (gaps.Count > 0 ? gaps.Min() : 1200) / 1000;
        }

        static double Mm(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);

        static void Set(Element e, string name, object v)
        {
            var p = e.LookupParameter(name);
            if (p == null || p.IsReadOnly) return;
            if (v is string s) p.Set(s); else if (v is int i) p.Set(i); else p.Set(Convert.ToDouble(v));
        }
    }
}

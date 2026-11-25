// [보고·공유 · 웹 공유판] 4D JSON (web/onsite_4d_schema.md v1.0) — shoring/export_4d.py build() 이식 + Revit 형상 추출.
//   (a) Revit 없는 부분: 입력(층·구역·부재 형상·설정·데이터 폴더) → JSON v2.0 (v6 3단계). 강도 예측 = Engine.StrengthTimeline, 날짜별 구역 판정 = Engine.Evaluator.Evaluate (첫 타설 ~ 마지막 기록일, 기록만),
//       강도 곡선 = ShoringEngine.Predict — 계산은 엔진 한 곳 (CLAUDE.md). export_4d.py 처럼 1차 계획 → 매일 09:00 예측 판정 → 판정이 늦은 구역을 미룬 2차 계획.
//       일치 시험: RevitOnSite.WebExport.Check 가 이 부분만 컴파일(WEBEXPORT_CHECK)해 export_4d.py 결과와 대조.
//   (b) Revit 부분 (#if !WEBEXPORT_CHECK): 슬래브·벽·창호·계단·구역·동바리 묶음 → m 단위 평면 다각형 + z 범위. 읽기만 (모델 안 바꿈)
// 결론 문구는 "해체 승인 요청 가능" / "존치" 까지 — 금지 문구가 들어가면 내보내지 않음.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using RevitOnSite.Engine;
#if !WEBEXPORT_CHECK
using Autodesk.Revit.DB;
#endif

namespace RevitOnSite
{
    /// <summary>부재 하나 (스키마 elements[]). Floor·Zone 이 null 이면 WebExport.Assign 이 채운다</summary>
    public sealed class WebElement
    {
        public string Id, Category, Zone, Name;
        public int? Floor;
        public List<double[]> Footprint;   // [[x, y], …] m, 반시계, 닫지 않음
        public double Z0, Z1;              // m
        public bool Synthetic;             // 모델에 없는 부재를 지어 넣었으면 true
        public double[] Spacing;           // shore_zone 만: 동바리 간격 [x, y] m
    }

    /// <summary>층 k = k층 슬래브(윗면 Z). Thickness = 슬래브 두께 m</summary>
    public sealed record WebLevel(int Floor, string Name, double Z, double Thickness);

    /// <summary>구역 사각형 (m) — 부재 구역 배정과 판정용 zones.csv</summary>
    public sealed record WebZone(string Id, int Floor, double X0, double Y0, double X1, double Y1);

    public sealed class WebInput
    {
        public string ProjectName, ModelFile, Source = "revit", Origin;
        public int MinLevels = ShoringEngine.MinLevels;
        public double Fck = 24, CureMin = 5;
        public string DataFolder;                          // 타설·센서·공시체·기상 기록 CSV (타설 기록이 있어야 내보냄)
        public List<WebLevel> Levels = new();
        public List<WebZone> Zones = new();
        public List<WebElement> Elements = new();
        public List<(string Id, string Text, string Basis)> Assumptions = new();   // 형상 쪽 가정 (호출하는 쪽이 앎)
    }

    public static class WebExport
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly JsonSerializerOptions Opts = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };   // 한글 그대로 (ensure_ascii=False)
        public const string Ready = "해체 승인 요청 가능";

        static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", Inv);
        static string G(double x) => x.ToString("G6", Inv);                                    // Python f"{x:g}"
        /// <summary>Python round(x, n) — "F" 서식은 정확한 2진값 기준이라 Python 과 같다 (Engine Py.Round 와 같은 방법)</summary>
        public static double Rn(double x, int n) => double.Parse(x.ToString("F" + n, Inv), Inv) + 0.0;
        internal static string Suffix(string id) => id.Split('-')[^1];

        /// <summary>구역 순서: 접미사 "A1" = 열 글자 + 줄 번호 → 줄, 열 순 (export_4d.py ZONE_ORDER = A1 B1 C1 A2 B2 C2)</summary>
        internal static IEnumerable<string> ByZoneOrder(IEnumerable<string> ids) => ids
            .Select(id => (id, m: Regex.Match(Suffix(id), @"^([A-Z]+)(\d+)$")))
            .OrderBy(q => q.m.Success ? int.Parse(q.m.Groups[2].Value, Inv) : int.MaxValue)
            .ThenBy(q => q.m.Groups[1].Length).ThenBy(q => q.m.Groups[1].Value, StringComparer.Ordinal)
            .ThenBy(q => q.id, StringComparer.Ordinal).Select(q => q.id);

        // ───────────── 평면 형상 (Revit 없음) ─────────────

        public static double Area(List<double[]> p)
        {
            double a = 0;
            for (int i = 0; i < p.Count; i++)
                a += p[i][0] * p[(i + 1) % p.Count][1] - p[(i + 1) % p.Count][0] * p[i][1];
            return a / 2;
        }

        /// <summary>mm 반올림, 연속 중복 점 제거, 반시계. 넓이가 없으면 null</summary>
        public static List<double[]> Clean(IEnumerable<double[]> pts)
        {
            var o = new List<double[]>();
            foreach (var p in pts)
            {
                var q = new[] { Rn(p[0], 3), Rn(p[1], 3) };
                if (o.Count == 0 || o[^1][0] != q[0] || o[^1][1] != q[1]) o.Add(q);
            }
            while (o.Count > 1 && o[0][0] == o[^1][0] && o[0][1] == o[^1][1]) o.RemoveAt(o.Count - 1);
            if (o.Count < 3 || Math.Abs(Area(o)) < 1e-6) return null;
            if (Area(o) < 0) o.Reverse();
            return o;
        }

        public static List<double[]> Rect(double x0, double y0, double x1, double y1) =>
            Clean(new[] { new[] { x0, y0 }, new[] { x1, y0 }, new[] { x1, y1 }, new[] { x0, y1 } });

        /// <summary>점들을 직선(p0, 단위 방향 u)에 투영한 범위. across = 법선 v = (−u.y, u.x) 쪽</summary>
        public static (double Min, double Max) Along(IEnumerable<double[]> pts, double[] p0, double[] u, bool across = false)
        {
            var v = pts.Select(p => across ? -(p[0] - p0[0]) * u[1] + (p[1] - p0[1]) * u[0] : (p[0] - p0[0]) * u[0] + (p[1] - p0[1]) * u[1]).ToList();
            return (v.Min(), v.Max());
        }

        /// <summary>직선 방향 a..b, 법선 방향 n0..n1 직사각형</summary>
        public static List<double[]> Strip(double[] p0, double[] u, double a, double b, double n0, double n1)
        {
            double[] P(double s, double t) => new[] { p0[0] + u[0] * s - u[1] * t, p0[1] + u[1] * s + u[0] * t };
            return Clean(new[] { P(a, n0), P(b, n0), P(b, n1), P(a, n1) });
        }

        /// <summary>곧은 벽을 개구부(a..b, 창대 sill, 인방 head)로 나눈 토막 (스키마: 개구부 사이 벽, 창대 아래 벽, 인방 위 벽). 벽 = 방향 a0..a1, 두께 n0..n1, 높이 z0..z1</summary>
        public static List<(List<double[]> Fp, double Z0, double Z1)> SplitWall(double[] p0, double[] u, double a0, double a1, double n0, double n1,
            double z0, double z1, IEnumerable<(double A, double B, double Sill, double Head)> openings)
        {
            var o = new List<(List<double[]>, double, double)>();
            void Add(double a, double b, double lo, double hi)
            {
                if (b - a > 0.01 && hi - lo > 0.01 && Strip(p0, u, a, b, n0, n1) is { } fp) o.Add((fp, lo, hi));
            }
            double x = a0;
            foreach (var (a, b, sill, head) in openings.Select(q => (A: Math.Clamp(q.A, a0, a1), B: Math.Clamp(q.B, a0, a1), S: Math.Clamp(q.Sill, z0, z1), H: Math.Clamp(q.Head, z0, z1)))
                         .Where(q => q.B > q.A).OrderBy(q => q.A))
            {
                Add(x, a, z0, z1);
                Add(Math.Max(a, x), b, z0, sill);
                Add(Math.Max(a, x), b, head, z1);
                x = Math.Max(x, b);
            }
            Add(x, a1, z0, z1);
            return o;
        }

        /// <summary>export_4d.py zone_of: 꼭짓점 평균이 든 구역 (x0 ≤ x &lt; x1). 없으면 그 층에서 가장 가까운 구역, 층에 구역이 없으면 null</summary>
        public static string ZoneOf(int floor, List<double[]> poly, IEnumerable<WebZone> zones)
        {
            double cx = poly.Average(p => p[0]), cy = poly.Average(p => p[1]);
            var fz = zones.Where(z => z.Floor == floor).ToList();
            static double D(double v, double lo, double hi) => Math.Max(Math.Max(lo - v, v - hi), 0);
            return fz.FirstOrDefault(z => z.X0 <= cx && cx < z.X1 && z.Y0 <= cy && cy < z.Y1)?.Id
                ?? fz.OrderBy(z => Math.Pow(D(cx, z.X0, z.X1), 2) + Math.Pow(D(cy, z.Y0, z.Y1), 2)).FirstOrDefault()?.Id;
        }

        /// <summary>층이 없는 부재 = 높이 가운데보다 윗면이 높은 첫 층 (k층 슬래브 아래 부재 = k층), 구역이 없는 부재 = ZoneOf. 넣지 못한 부재(지붕 위·구역 없는 층)는 빼고 그 수를 돌려줌</summary>
        public static int Assign(List<WebElement> els, IReadOnlyList<WebLevel> levels, IReadOnlyList<WebZone> zones)
        {
            var lv = levels.OrderBy(l => l.Z).ToList();
            return els.RemoveAll(e =>
            {
                if (e.Floor is null)
                {
                    double zc = (e.Z0 + e.Z1) / 2;
                    var l = lv.FirstOrDefault(q => q.Z >= zc - 1e-6);
                    if (l is null) return true;
                    e.Floor = l.Floor;
                }
                if (e.Zone is null && e.Category != "slab")
                    e.Zone = ZoneOf(e.Floor.Value, e.Footprint, zones);
                return e.Zone is null && e.Category != "slab";
            });
        }

        // ───────────── 공정·판정 → JSON (Revit 없음) ─────────────

        /// <summary>export_4d.py build(): 반환 (JSON, 한 줄 요약). 입력이 형식에 맞지 않거나 타설 기록이 없으면 InvalidOperationException, 데이터 오류는 엔진의 InvalidDataException</summary>
        public static (string Json, string Summary) Build(WebInput x, DateTime now)
        {
            var floors = x.Zones.Select(z => z.Floor).Distinct().OrderBy(f => f).ToList();
            if (floors.Count == 0) throw new InvalidOperationException("구역이 없습니다. [구역·동바리 생성]을 먼저 누르세요.");
            if (string.IsNullOrEmpty(x.DataFolder) || !Directory.Exists(x.DataFolder)) throw new InvalidOperationException("데이터 폴더가 없습니다. [프로젝트 설정]에서 데이터 폴더를 고르세요.");

            // 스키마 최소 검사 (export_4d.py check)
            var cats = new HashSet<string> { "slab", "wall", "stair", "window", "shore_zone", "zone" };
            var zoneIds = x.Zones.Select(z => z.Id).ToHashSet();
            var ids = new HashSet<string>();
            foreach (var e in x.Elements)
                if (!cats.Contains(e.Category) || e.Footprint is null || e.Footprint.Count < 3 || !(Rn(e.Z1, 3) > Rn(e.Z0, 3)) || e.Floor is null
                    || !ids.Add(e.Id) || (e.Zone is null ? e.Category != "slab" : !zoneIds.Contains(e.Zone)))
                    throw new InvalidOperationException($"부재 '{e.Id}' ({e.Category}) 가 웹 공유판 형식에 맞지 않습니다 (형상·층·구역·중복 ID).");

            string until = ActualUntil(x.DataFolder);
            StrengthTimelineResult tl;
            List<(DateOnly Day, ZoneRow R)> decisions;
            var tmp = Directory.CreateTempSubdirectory("onsite4d_").FullName;
            try
            {
                CopyData(x, tmp);
                tl = StrengthTimeline.Timeline(tmp, cureMin: x.CureMin);
                if (tl.Dates.Count == 0) throw new InvalidOperationException("타설 기록이 없습니다 (pours.csv) — 강도 타임라인을 만들 수 없습니다. 타설일을 먼저 넣으세요.");
                var d1 = until is null ? tl.Start!.Value : new[] { tl.End!.Value, DateOnly.ParseExact(until, "yyyy-MM-dd", Inv) }.Min();
                decisions = Decisions(x, tmp, tl.Start!.Value, d1, floors.Max());
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch (IOException) { }
            }

            var zoneOrder = ByZoneOrder(x.Zones.Select(z => Suffix(z.Id)).Distinct()).ToList();
            var measurements = Measurements(x);
            var th = ShoringEngine.Thresholds(x.Fck);
            var root = new Dictionary<string, object>
            {
                ["schema"] = "revit-onsite-4d",
                ["version"] = "2.0",   // 2.0 = 강도 예측 타임라인 (v6 3단계, onsite_4d_schema.md)
                ["project"] = new
                {
                    name = x.ProjectName, source = x.Source, model_file = x.ModelFile, generated_at = now.ToString("yyyy-MM-dd'T'HH:mm:ss", Inv),
                    units = "m", origin = x.Origin, fck_mpa = x.Fck, min_levels = x.MinLevels, cure_min_c = x.CureMin,
                    date_range = new[] { Iso(tl.Start!.Value), Iso(tl.End!.Value) }, judged_until = until, zone_order = zoneOrder,
                },
                ["levels"] = x.Levels.OrderBy(l => l.Floor).Select(l => new { floor = l.Floor, name = l.Name, z = Rn(l.Z, 3), slab_thickness = Rn(l.Thickness, 3) }),
                ["thresholds"] = new { side_form_mpa = th["측면"], remove_mpa = th["다층 동바리 해체"], support_slab_mpa = th["지지 슬래브"] },
                ["elements"] = x.Elements.Select(e =>
                {
                    var d = new Dictionary<string, object>
                    {
                        ["id"] = e.Id, ["category"] = e.Category, ["floor"] = e.Floor, ["zone"] = e.Zone, ["name"] = e.Name,
                        ["geometry"] = new { footprint = e.Footprint, z0 = Rn(e.Z0, 3), z1 = Rn(e.Z1, 3) }, ["synthetic"] = e.Synthetic,
                    };
                    if (e.Spacing != null) d["spacing_m"] = e.Spacing;
                    return d;
                }),
                ["strength_timeline"] = new
                {
                    start = Iso(tl.Start!.Value), end = Iso(tl.End!.Value),
                    floors = tl.Floors.Select(f => new { floor = f.Floor, label = f.Label, fck = f.Fck, mpa = f.Values }),
                    zones = tl.Zones.Select(z => new { zone = z.ZoneId, mpa = z.Values }),
                    cylinders = tl.Cylinders.Select(c => new { date = Iso(c.Date), zone = c.ZoneId, floor = c.Floor, mpa = c.Mpa, curing = c.Curing }),
                    lines = tl.Lines.Select(l => new { name = l.Name, mpa = l.Mpa }),
                },
                ["decisions"] = decisions.Select(d => new
                {
                    at = Iso(d.Day) + "T09:00", zone = d.R.ZoneId, decision = d.R.Decision, reason = d.R.Reason, predicted_due = Iso(d.R.PredictedDue),
                    f_cyl = d.R.FCyl, f_est_min = d.R.FEstMin, est_source = d.R.EstSource, level_gate = d.R.LevelGate, age_days = d.R.AgeDays,
                    f_est_xy = d.R.FEstXy is { } xy ? new[] { xy.X, xy.Y } : null,   // 구역 최소 추정 강도 지점 (센서 "판정을 좌우" 표시)
                }),
                ["measurements"] = measurements,
                ["approvals"] = Approvals(x),
                ["evidence"] = EvidenceRows(x),
                ["assumptions"] = x.Assumptions.Concat(DataAssumptions(x, measurements)).Select(a => new { id = a.Id, text = a.Text, basis = a.Basis }),
            };
            var json = JsonSerializer.Serialize(root, Opts);
            if (json.Contains("해체하세요") || json.Contains("승인됨"))
                throw new InvalidOperationException("내보낼 글에 승인·해체 지시 문구가 있어 멈췄습니다 (결론은 '해체 승인 요청 가능' / '존치' 까지).");

            var label = new[] { ("slab", "슬래브"), ("zone", "구역"), ("shore_zone", "동바리 묶음"), ("wall", "벽"), ("window", "창호"), ("stair", "계단") };
            var byCat = label.Select(c => (c.Item2, n: x.Elements.Count(e => e.Category == c.Item1))).Where(c => c.n > 0).Select(c => c.Item1 + " " + c.n);
            return (json, $"부재 {x.Elements.Count} ({string.Join(" · ", byCat)}) · 강도 곡선 {tl.Floors.Count}개층 · 판정 {decisions.Count} (마지막 기록일 {until ?? "없음"}까지)");
        }

        static string Csv(params object[] v) => string.Join(",", v.Select(o =>
        {
            var s = Convert.ToString(o, Inv) ?? "";
            return s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }));

        /// <summary>export_4d.py copy_data: 계산용 사본 — zones.csv = 모델 구역, 나머지 CSV·options.json 은 데이터 폴더 그대로 (pours.csv 포함)</summary>
        static void CopyData(WebInput x, string tmp)
        {
            File.WriteAllLines(Path.Combine(tmp, "zones.csv"), x.Zones.Select(z => Csv(z.Id, z.Floor, z.X0, z.Y0, z.X1, z.Y1)).Prepend("zone_id,floor,x0,y0,x1,y1"));
            foreach (var f in Directory.GetFiles(x.DataFolder))
            {
                var n = Path.GetFileName(f);
                if ((n.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || n == Evaluator.OptionsFile) && n != "zones.csv") File.Copy(f, Path.Combine(tmp, n));
            }
        }

        /// <summary>export_4d.py actual_until: 실적(타설·공시체·센서 기록)의 마지막 날 'yyyy-MM-dd' — 없으면 null (감사 A-27)</summary>
        public static string ActualUntil(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
            var days = new[] { ("pours.csv", "pour_date"), ("cylinders.csv", "test_date"), ("sensors.csv", "timestamp"), ("sensor_strength.csv", "timestamp") }
                .SelectMany(f => Evaluator.Read(folder, f.Item1).Select(r => r.GetValueOrDefault(f.Item2)))
                .Where(v => !string.IsNullOrEmpty(v)).Select(v => v.Length > 10 ? v[..10] : v);
            return days.Max(StringComparer.Ordinal);
        }

        /// <summary>export_4d.py run_decisions: d0~d1 매일 09:00 에 Evaluate — 그날까지 기록상 타설된 구역만 (pours.csv 기록 줄 그대로, 계획 타설 없음)</summary>
        static List<(DateOnly Day, ZoneRow R)> Decisions(WebInput x, string tmp, DateOnly d0, DateOnly d1, int top)
        {
            var pours = Evaluator.Read(tmp, "pours.csv");
            var head = new[] { "zone_id", "pour_date", "pour_time", "mix", "fck" };
            var outp = new List<(DateOnly, ZoneRow)>();
            for (var d = d0; d <= d1; d = d.AddDays(1))
            {
                var day = Iso(d);
                var done = pours.Where(r => string.CompareOrdinal((r.GetValueOrDefault("pour_date") ?? "")[..Math.Min(10, (r.GetValueOrDefault("pour_date") ?? "").Length)], day) <= 0)
                    .OrderBy(r => r.GetValueOrDefault("zone_id"), StringComparer.Ordinal).ToList();
                if (done.Count == 0) continue;
                File.WriteAllLines(Path.Combine(tmp, "pours.csv"), done.Select(r => Csv(head.Select(k => (object)(r.GetValueOrDefault(k) ?? "")).ToArray())).Prepend(string.Join(",", head)));
                foreach (var r in Evaluator.Evaluate(tmp, d.ToDateTime(new TimeOnly(9, 0)), x.CureMin, top))
                    outp.Add((d, r));
            }
            return outp;
        }

        const int TempStepH = 6;                                  // 온도 기록 줄이기 간격 (시간) — export_4d.py TEMP_STEP_H (판정 계산은 원본 전체)

        /// <summary>export_4d.py measurements(): 데이터 폴더 실측값 → measurements[] (field_input_spec §4). 구역 = 그 점이 든 구역 상자.
        /// 입력자 열이 없으면 출처 이름 (Python 판은 "(샘플)" 을 붙임)</summary>
        /// <summary>approvals.csv (요청 제출·감리 승인 기록·해체 완료·재받침) → 4D 에서 그 시각부터 상태 6·7·8 — export_4d.py approvals_rows.
        /// 사슬이 깨졌으면 넣지 않음 (고친 기록을 믿을 만한 것처럼 보이지 않게). 스키마 v1.1 선택 필드.
        /// shortcut: Python 은 "approvals.csv 무시: …" 를 출력만 함 — 여기선 조용히 빈 목록 (WebExport 는 Revit 없이도 돌아 대시보드 기록을 안 씀)</summary>
        public static List<Dictionary<string, object>> Approvals(WebInput x)
        {
            var outp = new List<Dictionary<string, object>>();
            if (string.IsNullOrEmpty(x.DataFolder) || !Directory.Exists(x.DataFolder) || !Approval.Verify(x.DataFolder).Ok) return outp;
            foreach (var r in Approval.ReadLog(x.DataFolder))
            {
                var ts = r.GetValueOrDefault("timestamp") ?? "";
                outp.Add(new()
                {
                    ["zone"] = r.GetValueOrDefault("zone_id"), ["at"] = ts.Length > 16 ? ts[..16] : ts, ["action"] = r.GetValueOrDefault("action"),
                    ["status"] = Approval.CodeOf(r.GetValueOrDefault("action")), ["actor"] = r.GetValueOrDefault("actor"),
                });
            }
            return outp;
        }

        /// <summary>근거 원장 (Engine.Evidence.Entries = evidence.py entries 순서 그대로) → 4D '근거 단계' 색 — export_4d.py "evidence". 스키마 v1.1 선택 필드</summary>
        public static List<object> EvidenceRows(WebInput x) =>
            string.IsNullOrEmpty(x.DataFolder) || !Directory.Exists(x.DataFolder) ? new List<object>()
                : Evidence.Entries(x.DataFolder).Select(e => (object)new { zone = e.ZoneId, date = e.Date[..Math.Min(10, e.Date.Length)], stage = e.Stage, source = e.Source }).ToList();

        static List<Dictionary<string, object>> Measurements(WebInput x)
        {
            var outp = new List<Dictionary<string, object>>();
            if (string.IsNullOrEmpty(x.DataFolder) || !Directory.Exists(x.DataFolder)) return outp;
            double F(string s) => double.Parse(s, NumberStyles.Float, Inv);
            string By(Dictionary<string, string> r, string dflt) => string.IsNullOrEmpty(r.GetValueOrDefault("entered_by")) ? dflt : r["entered_by"];
            Dictionary<string, object> M(string kind, string id, int floor, string zone, double px, double py, string ts, double v, string unit, string by) => new()
            {
                ["kind"] = kind, ["point_id"] = id, ["floor"] = floor, ["zone"] = zone, ["x"] = px, ["y"] = py,
                ["timestamp"] = ts, ["value"] = v, ["unit"] = unit, ["entered_by"] = by,
            };
            string ZoneAt(int k, double px, double py) => ZoneOf(k, new List<double[]> { new[] { px, py } }, x.Zones);
            foreach (var g in Evaluator.Read(x.DataFolder, "sensors.csv").GroupBy(r => r["sensor_id"]).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var rs = g.OrderBy(r => r["timestamp"], StringComparer.Ordinal).ToList();
                var t0 = DateTime.Parse(rs[0]["timestamp"], Inv);
                var keep = rs.Where(r => (DateTime.Parse(r["timestamp"], Inv) - t0).TotalSeconds % (TempStepH * 3600) == 0).ToList();
                if (!ReferenceEquals(keep[^1], rs[^1])) keep.Add(rs[^1]);
                foreach (var r in keep)
                {
                    int k = int.Parse(r["floor"], Inv);
                    double px = F(r["x"]), py = F(r["y"]);
                    outp.Add(M("sensor_temp", g.Key, k, ZoneAt(k, px, py), px, py, r["timestamp"][..Math.Min(16, r["timestamp"].Length)], F(r["temp_c"]), "°C", By(r, "센서 로거")));
                }
            }
            foreach (var r in Evaluator.Read(x.DataFolder, "sensor_strength.csv"))
            {
                int k = int.Parse(r["floor"], Inv);
                double px = F(r["x"]), py = F(r["y"]);
                outp.Add(M("sensor_strength", r["sensor_id"], k, ZoneAt(k, px, py), px, py, r["timestamp"][..Math.Min(16, r["timestamp"].Length)], F(r["mpa"]), "MPa", By(r, "강도 센서")));
            }
            var zones = x.Zones.GroupBy(z => z.Id).ToDictionary(g => g.Key, g => g.First());
            foreach (var r in Evaluator.Read(x.DataFolder, "cylinders.csv"))
            {
                if (!zones.TryGetValue(r["zone_id"], out var z)) continue;   // 모델에 없는 구역의 공시체는 표식 자리가 없음
                var f = new[] { F(r["f1"]), F(r["f2"]), F(r["f3"]) };
                var m = M("cylinder", "CYL-" + z.Id, z.Floor, z.Id, Rn((z.X0 + z.X1) / 2, 2), Rn((z.Y0 + z.Y1) / 2, 2), r["test_date"] + "T09:00", Rn(f.Sum() / 3, 2), "MPa", By(r, "시험 성적서"));
                (m["f"], m["curing"]) = (f, r.GetValueOrDefault("curing") ?? "");
                outp.Add(m);
            }
            foreach (var r in Evaluator.Read(x.DataFolder, "rebound.csv"))   // 반발경도 — 조회만, 판정 미사용
            {
                if (!zones.TryGetValue(r["zone_id"], out var z)) continue;
                outp.Add(M("rebound", $"RB-{z.Id}-{r["x"]}-{r["y"]}", z.Floor, z.Id, F(r["x"]), F(r["y"]), r["test_date"][..Math.Min(10, r["test_date"].Length)] + "T09:00", F(r["r_value"]), "R", By(r, "")));
            }
            return outp;
        }

        /// <summary>데이터 쪽 가정 (export_4d.py ASSUMPTIONS 의 data-·tl-·view- 항목, 데이터 폴더에 맞춰 문장만 바꿈)</summary>
        static IEnumerable<(string Id, string Text, string Basis)> DataAssumptions(WebInput x, List<Dictionary<string, object>> meas)
        {
            if (meas.Count > 0)
            {
                string N(string k, string label) => meas.Count(m => (string)m["kind"] == k) is int n and > 0 ? $"{label} {n}건" : null;
                yield return ("data-meas", "실측값 표식: " + string.Join(" · ", new[] { N("sensor_temp", $"온도 센서 기록({TempStepH}시간 간격으로 줄여 표시, 판정은 원본)"), N("sensor_strength", "강도 센서"),
                    N("cylinder", "공시체"), N("rebound", "반발경도(판정 미사용)") }.Where(s => s != null)) + ".", "[입력]");
            }
            var floorOf = x.Zones.GroupBy(z => z.Id).ToDictionary(g => g.Key, g => g.First().Floor);
            string Floors(IEnumerable<int> fl) => string.Join("·", fl.Distinct().Order()) + "층";
            var sensorFloors = Evaluator.Read(x.DataFolder, "sensors.csv")
                .Select(r => int.TryParse(r.GetValueOrDefault("floor"), NumberStyles.Integer, Inv, out var f) ? f : (int?)null).Where(f => f != null).Select(f => f.Value).ToList();
            var cylFloors = Evaluator.Read(x.DataFolder, "cylinders.csv").Select(r => r.GetValueOrDefault("zone_id")).Where(z => z != null && floorOf.ContainsKey(z)).Select(z => floorOf[z]).ToList();
            int wx = Evaluator.Read(x.DataFolder, "weather.csv").Count;
            yield return ("data-weather", (wx > 0 ? $"기온은 데이터 폴더 기상 자료({wx}일)와, 자료가 없는 날은 서울 평년값입니다. " : "기온은 서울 평년값입니다 (기상청 실측 미반영). ")
                + (sensorFloors.Count > 0 ? $"센서 기록은 {Floors(sensorFloors)}만 있습니다." : "센서 기록이 없습니다."), wx > 0 ? "[입력]" : "[가정]");
            yield return ("data-cyl", cylFloors.Count > 0 ? $"현장양생 공시체 결과는 {Floors(cylFloors)}만 있습니다." : "현장양생 공시체 결과가 없습니다 — 해체 승인 요청은 공시체 시험 뒤에만 가능합니다.", "[입력]");
            yield return ("tl-estimate", $"강도 곡선은 타설일·기온으로 계산한 추정(참고, 판정에 안 씀)입니다. 층 곡선 = 그날까지 타설된 구역 중 가장 낮은 값, 그림 기간 = 첫 타설 ~ 마지막 타설 + {StrengthTimeline.SpanDays}일.", "[가정]");
            yield return ("data-decision", "구역 판정은 첫 타설일부터 마지막 기록일까지 날마다 09:00 에 기록(공시체·3 m 안 센서)만으로 다시 계산한 값입니다. 그 뒤 날짜는 판정 없음.", "[가정]");
            yield return ("view-color", "구역 색은 그날 추정 강도 ÷ fck (40·60·80·100 % 구간, 화면 표시용)이고, 판정 근거 문구는 그날의 구역 판정에서 가져옵니다.", "[가정]");
        }

        /// <summary>뷰어(web/onsite_4d.html) 사본의 &lt;script type="application/json" id="data"&gt; 안에 JSON 을 넣음 (export_4d.py --html 과 같게 "&lt;/" → "&lt;\/")</summary>
        public static string Html(string viewer, string json)
        {
            var m = Regex.Match(viewer, "<script type=\"application/json\" id=\"data\">(.*?)</script>", RegexOptions.Singleline);
            if (!m.Success) throw new InvalidDataException("웹 뷰어 파일에 <script type=\"application/json\" id=\"data\"> 블록이 없습니다.");
            var g = m.Groups[1];
            return viewer[..g.Index] + json.Replace("</", "<\\/") + viewer[(g.Index + g.Length)..];
        }

#if !WEBEXPORT_CHECK
        // ───────────── Revit 형상 → 입력 ─────────────

        static double M(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Meters);
        static double[] Xy(XYZ p) => new[] { M(p.X), M(p.Y) };
        static List<double[]> Box(BoundingBoxXYZ b) => Rect(M(b.Min.X), M(b.Min.Y), M(b.Max.X), M(b.Max.Y));
        static IEnumerable<double[]> Corners(BoundingBoxXYZ b) => new[] { Xy(b.Min), Xy(new XYZ(b.Max.X, b.Min.Y, 0)), Xy(b.Max), Xy(new XYZ(b.Min.X, b.Max.Y, 0)) };
        static string TypeName(Element e) => e.Document.GetElement(e.GetTypeId())?.Name ?? e.Name;

        static IEnumerable<Solid> Solids(Element e)
        {
            var geo = e.get_Geometry(new Options());
            if (geo == null) yield break;
            foreach (var g in geo)
                foreach (var o in g is GeometryInstance gi ? gi.GetInstanceGeometry().Cast<GeometryObject>() : new[] { g })
                    if (o is Solid s && s.Faces.Size > 0) yield return s;
        }

        /// <summary>아래를 보는 평면마다 (외곽 루프 m, 넓이, 높이 m). 외곽 = 넓이가 가장 큰 루프 (구멍 루프는 버림, 스키마 v1)</summary>
        static List<(List<double[]> Fp, double Area, double Z)> DownFaces(Element e)
        {
            var o = new List<(List<double[]>, double, double)>();
            foreach (var s in Solids(e))
                foreach (Face f in s.Faces)
                    if (f is PlanarFace pf && pf.FaceNormal.Z < -0.9 && pf.GetEdgesAsCurveLoops()
                            .Select(l => Clean(l.SelectMany(c => c.Tessellate().SkipLast(1)).Select(Xy)))
                            .Where(p => p != null).OrderByDescending(p => Math.Abs(Area(p))).FirstOrDefault() is { } fp)
                        o.Add((fp, pf.Area, M(pf.Origin.Z)));
            return o;
        }

        /// <summary>열린 문서 → 입력. notes = 알림에 덧붙일 문장 (동바리 묶음 출처, 뺀 부재 수)</summary>
        public static WebInput Collect(Document doc, List<string> notes)
        {
            var s = ProjectSettings.Read(doc);
            string file = string.IsNullOrEmpty(doc.PathName) ? doc.Title : Path.GetFileName(doc.PathName);
            var x = new WebInput
            {
                ProjectName = string.IsNullOrWhiteSpace(doc.ProjectInformation?.Name) ? file : doc.ProjectInformation.Name,
                ModelFile = file,
                Origin = "Revit 내부 원점 (데모 모델 = 그리드 X1·Y1 교점), x·y = 모델 좌표축, z = 내부 원점 고도 (m)",
                MinLevels = s.MinLevels, Fck = s.DefaultFck, CureMin = s.CureMin, DataFolder = s.CsvFolder,
            };
            var floors = ZoneGenerator.DetectFloors(doc);
            x.Levels = floors.Select(f => new WebLevel(f.FloorNo,
                f.Label == f.FloorNo + "F" ? f.FloorNo + "층" : f.Label.StartsWith("B") ? "지하 " + f.Label[1..] + "층" : f.Label,
                M(f.Level.ProjectElevation), M(f.Top - f.Bottom))).ToList();
            var els = x.Elements;

            // 구역 상자 (SHR 구역 DirectShape) + 구역마다 동바리 묶음 하나 (스키마: 416본을 다 내보내지 않음)
            var shores = ShoringModel.Shores(doc)
                .Select(e => (Zone: e.LookupParameter("SHR_ZoneID")?.AsString(), Bb: e.get_BoundingBox(null), Pt: (e.Location as LocationPoint)?.Point))
                .Where(q => !string.IsNullOrEmpty(q.Zone) && q.Bb != null && q.Pt != null).GroupBy(q => q.Zone).ToDictionary(g => g.Key, g => g.ToList());
            var levelZ = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().Select(l => l.ProjectElevation).ToList();
            static double Gap(IEnumerable<double> v)   // 동바리 줄 사이 간격의 중앙값 (m)
            {
                var a = v.Select(q => Rn(q, 3)).Distinct().Order().ToList();
                var g = a.Zip(a.Skip(1), (p, q) => q - p).Where(d => d > 0.05).Order().ToList();
                return g.Count > 0 ? Rn(g[g.Count / 2], 3) : 1.2;
            }
            int real = 0, synth = 0;
            foreach (var (e, id, floor) in Replan.ZoneList(doc))
            {
                var bb = e.get_BoundingBox(null);
                if (bb == null || x.Zones.Any(z => z.Id == id)) continue;   // 같은 ID 구역이 둘이면 앞의 것
                var z = new WebZone(id, floor, Rn(M(bb.Min.X), 3), Rn(M(bb.Min.Y), 3), Rn(M(bb.Max.X), 3), Rn(M(bb.Max.Y), 3));
                x.Zones.Add(z);
                els.Add(new WebElement { Id = id, Category = "zone", Floor = floor, Zone = id, Name = "구역 " + id, Footprint = Rect(z.X0, z.Y0, z.X1, z.Y1), Z0 = M(bb.Min.Z), Z1 = M(bb.Max.Z) });
                // 모델 동바리가 있으면 그 높이·간격, 없으면 아래 구조 슬래브 윗면(없으면 레벨) ~ 구역 밑면, 1.2 m [가정] → synthetic
                double z0, z1;
                double[] sp = { 1.2, 1.2 };
                bool has = shores.TryGetValue(id, out var sh);
                if (has)
                {
                    (z0, z1) = (sh.Min(q => q.Bb.Min.Z), sh.Max(q => q.Bb.Max.Z));
                    sp = new[] { Gap(sh.Select(q => M(q.Pt.X))), Gap(sh.Select(q => M(q.Pt.Y))) };
                }
                else
                {
                    z1 = bb.Min.Z;
                    double tol = UnitUtils.ConvertToInternalUnits(300, UnitTypeId.Millimeters);
                    double? below = floors.Select(f => (double?)f.Top).Where(t => t < z1 - tol).Max() ?? levelZ.Select(l => (double?)l).Where(l => l < z1 - tol).Max();
                    if (below == null) continue;
                    z0 = below.Value;
                }
                if (has) real++; else synth++;
                els.Add(new WebElement
                {
                    Id = id + "-SH", Category = "shore_zone", Floor = floor, Zone = id, Synthetic = !has, Spacing = sp, Z0 = M(z0), Z1 = M(z1),
                    Name = has ? $"{id} 동바리 {sh.Count}본 ({G(sp[0])} × {G(sp[1])} m 간격 묶음)" : $"{id} 동바리 (모델에 없음 — {G(sp[0])} m 간격 가정)",
                    Footprint = Rect(z.X0 + 0.15, z.Y0 + 0.15, z.X1 - 0.15, z.Y1 - 0.15),
                });
            }

            // 슬래브 = 구조 슬래브(DetectFloors 와 같은 기준)의 가장 넓은 아래 면 외곽
            foreach (var f in floors)
                foreach (var e in f.Slabs)
                {
                    var bb = e.get_BoundingBox(null);
                    els.Add(new WebElement
                    {
                        Id = e.UniqueId, Category = "slab", Floor = f.FloorNo, Name = TypeName(e), Z0 = M(bb.Min.Z), Z1 = M(bb.Max.Z),
                        Footprint = DownFaces(e).OrderByDescending(q => q.Area).Select(q => q.Fp).FirstOrDefault() ?? Box(bb),
                    });
                }

            IEnumerable<Element> Of(params BuiltInCategory[] c) => new FilteredElementCollector(doc).WherePasses(new ElementMulticategoryFilter(c))
                .WhereElementIsNotElementType().Where(e => e.get_BoundingBox(null) != null);
            void Add(Element e, string cat, List<(List<double[]> Fp, double Z0, double Z1)> parts)
            {
                for (int i = 0; i < parts.Count; i++)
                    els.Add(new WebElement { Id = parts.Count == 1 ? e.UniqueId : e.UniqueId + "-" + (i + 1), Category = cat, Name = TypeName(e), Footprint = parts[i].Fp, Z0 = parts[i].Z0, Z1 = parts[i].Z1 });
            }

            // 벽: 곧은 Wall = 위치선 방향 직사각형(형상 모서리 범위)을 개구부로 나눔. 커튼월 = 창호. 곡선 벽·IFC 벽(DirectShape) = 가장 낮은 아래 면들 (문 자리에서 끊긴 토막은 따로)
            var axis = new Dictionary<ElementId, (double[] P0, double[] U, double N0, double N1)>();
            foreach (var e in Of(BuiltInCategory.OST_Walls))
            {
                var bb = e.get_BoundingBox(null);
                double z0 = M(bb.Min.Z), z1 = M(bb.Max.Z);
                var w = e as Wall;
                if (w?.WallType?.Kind == WallKind.Curtain) { Add(e, "window", new() { (Box(bb), z0, z1) }); continue; }
                var pts = Solids(e).SelectMany(q => q.Edges.Cast<Edge>()).SelectMany(ed => ed.Tessellate()).Select(Xy).ToList();
                if (w?.Location is LocationCurve { Curve: Line ln } && Math.Abs(ln.Direction.Z) < 0.1 && pts.Count > 0)
                {
                    var p0 = Xy(ln.GetEndPoint(0));
                    double h = Math.Sqrt(ln.Direction.X * ln.Direction.X + ln.Direction.Y * ln.Direction.Y);
                    var u = new[] { ln.Direction.X / h, ln.Direction.Y / h };
                    var (a0, a1) = Along(pts, p0, u);
                    var (n0, n1) = Along(pts, p0, u, true);
                    axis[e.Id] = (p0, u, n0, n1);
                    // shortcut: 개구부 폭 = 삽입 요소 외곽 상자를 벽 방향에 투영 (사선 벽은 조금 넓게 잡힘). 정확히 하려면 패밀리 폭 매개변수
                    var ops = w.FindInserts(true, false, false, false).Select(doc.GetElement).Select(o => o?.get_BoundingBox(null)).Where(b => b != null)
                        .Select(b => { var (a, c) = Along(Corners(b), p0, u); return (a, c, M(b.Min.Z), M(b.Max.Z)); }).ToList();
                    Add(e, "wall", SplitWall(p0, u, a0, a1, n0, n1, z0, z1, ops));
                    continue;
                }
                var faces = DownFaces(e);
                Add(e, "wall", faces.Count == 0 ? new() { (Box(bb), z0, z1) }
                    : faces.Where(q => q.Z < faces.Min(r => r.Z) + 0.01).Select(q => (q.Fp, z0, z1)).ToList());
            }

            // 창호(창·문) = 곧은 벽에 있으면 벽 두께 가운데 0.1 m 판, 아니면 외곽 상자. 계단 = 외곽 상자 (뷰어가 긴 변 방향 계단으로 그림)
            foreach (var e in Of(BuiltInCategory.OST_Windows, BuiltInCategory.OST_Doors))
            {
                var bb = e.get_BoundingBox(null);
                var fp = Box(bb);
                if ((e as FamilyInstance)?.Host is Wall hw && axis.TryGetValue(hw.Id, out var w))
                {
                    var (a, b) = Along(Corners(bb), w.P0, w.U);
                    double mid = (w.N0 + w.N1) / 2;
                    fp = Strip(w.P0, w.U, a, b, mid - 0.05, mid + 0.05);
                }
                Add(e, "window", new() { (fp, M(bb.Min.Z), M(bb.Max.Z)) });
            }
            // shortcut: 여러 층을 잇는 계단은 높이 가운데 층 하나로 들어감. 층마다 나누려면 계단참·런(StairsRun) 단위로
            foreach (var e in Of(BuiltInCategory.OST_Stairs))
            {
                var bb = e.get_BoundingBox(null);
                Add(e, "stair", new() { (Box(bb), M(bb.Min.Z), M(bb.Max.Z)) });
            }

            int bad = els.RemoveAll(e => e.Footprint == null || Rn(e.Z1, 3) - Rn(e.Z0, 3) < 0.002);
            int dropped = Assign(els, x.Levels, x.Zones);
            int n(string c) => els.Count(e => e.Category == c);
            x.Assumptions.Add(("geom-model", $"슬래브 {n("slab")} · 벽 {n("wall")} · 창호 {n("window")} · 계단 {n("stair")}개는 Revit 모델 형상입니다. 슬래브·곡선 벽은 아래 면 외곽, "
                + "곧은 벽은 위치선 방향 직사각형을 창호 자리에서 나눴고(창대 아래·인방 위 벽은 따로), 창호는 벽 두께 가운데 0.1 m 판, 계단은 외곽 상자로 단순화했습니다. "
                + "층은 부재 높이 가운데 바로 위 구조 슬래브, 구역은 평면 무게중심이 든 구역입니다.", "[입력]"));
            x.Assumptions.Add(("geom-shore", $"동바리는 구역마다 상자 하나로 묶어 보여 줍니다. 모델에 동바리가 있는 {real}개 구역은 그 높이·간격이고, "
                + $"동바리를 놓지 않은 {synth}개 구역은 아래 슬래브 윗면~슬래브 밑면, 1.2 m 간격으로 가정했습니다 ('가상' 표시).", "[가정]"));
            notes.Add($"동바리 묶음: 모델 동바리 {real}개 구역, 가정(가상) {synth}개 구역");
            if (bad + dropped > 0) notes.Add($"형상이 없거나 층·구역에 넣지 못한 부재 {bad + dropped}개는 뺐습니다 (지붕 위·구역 없는 층).");
            return x;
        }
#endif
    }
}

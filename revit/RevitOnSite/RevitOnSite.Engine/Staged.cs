// shoring/staged.py 의 1:1 C# 이식 (총괄 11-05, docs/shore_view_spec.md S1–S3) — 단계 해체·조기 탈형 구조 검토 (S4 전용 계획은 v6 공정표 제거로 뺌).
//   S1 WallStrip 벽·기둥 측면 거푸집 해체일 (5 MPa 도달일 · 무시험 일수 표 3.3-2)   S2 Stages 판넬 탈형(필러 존치) 4조건 + 필러 = 기존 판정
//   S3 ReviewHtml 조기 탈형 구조 검토서(초안, 서명란 빈칸)
// 근거 값·문구·정수 일·반올림 순서·allocate(빠지는 날 ≤ 필요일, 같은 날이면 층 번호 작은 쪽)가 staged.py 와 같다. 결론은 '요청 가능 / 존치 / 필요'까지.
using System.Globalization;
using System.Text;
using Row = System.Collections.Generic.Dictionary<string, string?>;

namespace RevitOnSite.Engine;

/// <summary>staged.wall_strip 결과 (Passed = at 을 줬을 때만)</summary>
public sealed record WallStripResult(string ZoneId, DateOnly PourDate, DateOnly? DateByStrength, DateOnly? DateByDays, string Basis, (bool Strength, bool Days)? Passed);

/// <summary>staged.stages 구역 하나 — Checks = (이름, 통과?, 근거), Values = 검토 값</summary>
public sealed record StageZone(int Floor, string PourDate, string PanelDecision, List<(string Name, bool Ok, string Basis)> Checks, StageValues Values,
    string FillerDecision, string FillerReason, DateOnly? EarliestPanelDate);

public sealed record StageValues(double F, double FEst, double? FCyl, string EstSource, int AgeDays, int WarmDays, double Ratio, List<double> Supports, double Load, double? Cap, double Fck, double Live = Evaluator.LiveKnM2);   // Live = W12 시공하중

public static class Staged
{
    public static readonly double SideMpa = ShoringEngine.Thresholds(24.0)["측면"];                 // 5 [확정] KCS 14 20 12:2024 표 3.3-1
    public static readonly double PanelMpa = ShoringEngine.Thresholds(24.0)["필러 동바리 조기 탈형"];   // 14 [확정] 가이드라인 2.1.1(2)①
    public const double WarmC = 10.0, HotC = 20.0;                                                 // [확정] 3.3.1(5) · 표 3.3-2
    public const int WarmDaysNeed = 4;                                                             // [확정] 10 ℃ 이상 4일
    /// <summary>[확정] 표 3.3-2 측면 무시험 일수 (20 ℃ 이상, 10–20 ℃). 배합 → 칸 대응 [가정]: 슬래그·3성분 = 2종 혼합 칸</summary>
    public static readonly Dictionary<string, (int Hot, int Warm)> NoTestDaysTable = new()
    {
        ["조강"] = (2, 3), ["보통"] = (4, 6), ["슬래그 미분말"] = (5, 8), ["슬래그·FA 3성분"] = (5, 8),
    };
    public const int NoReach = 400;   // DaysTo 한도 (감사 A-28, staged.py NO_REACH) — 판넬 탈형 가능일을 찾는 한도
    public const int LimitDays = NoReach;

    /// <summary>staged.py days_to: 타설일부터 target MPa 에 닿는 날 수 (정수 일, 엔진 예측). NoReach 일 안에 못 닿으면 null (감사 A-28). v6: 예전 Schedule 에서 옮김</summary>
    public static int? DaysTo(string mix, DateOnly cast, double fck, double target, double cureMin, IReadOnlyList<double> temps)
        => Py.FirstDayOrNull(a => ShoringEngine.Predict(mix, cast, a, fck, cureMin, temps) >= target);
    public const string ReviewKey = "구조검토";
    public const string PanelOk = "판넬 탈형 요청 가능 (필러 존치)", PanelKeep = "판넬 존치";
    public const string Draft = "초안 — 책임기술자 검토·승인 전, 해체 지시 아님";
    public static readonly string[] Forbidden = { "하십시오", "하세요", "할 것", "하라", "승인함", "승인합니다", "지시합니다", "지시함" };
    const string SrcSide = "KCS 14 20 12:2024 표 3.3-1";
    const string SrcDays = "KCS 14 20 12:2024 표 3.3-2";
    const string SrcPanel = "해체 가이드라인 2.1.1(2)① p10 — 필러 동바리 전체 존치 + 구조검토 + 14 MPa 이상";
    const string SrcWarm = "KCS 14 20 12:2024 3.3.1(5) — 10 ℃ 이상 4일 이상 양생 + 책임기술자 사전 승인";
    const string SrcLoad = "해체 가이드라인 2.1.2(2) 하중비교 예제식 (지지층 강도 비율 √(f/fck) 감소 적용 — 보수 측)";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ── 공통 (staged.py _Site)
    sealed class Site
    {
        public readonly string Folder;
        public readonly Dictionary<string, int> FloorOf = new();
        public readonly Dictionary<string, (double, double, double, double)> RectOf = new();
        public readonly Dictionary<string, double> SlabOf = new();
        public readonly List<int> Order;
        public readonly Dictionary<string, Row> Pours = new();
        public readonly List<Row> Weather;
        readonly Dictionary<DateOnly, List<double>> t = new();
        readonly Evaluator.SensorIndex sens;   // W4 구역 온도 센서
        readonly Dictionary<string, (List<double> Temps, List<string> Src, List<List<string>> Used)> zt = new();

        public Site(string folder)
        {
            Folder = folder;
            foreach (var z in Evaluator.Read(folder, "zones.csv"))
            {
                var id = z["zone_id"]!;
                FloorOf[id] = Py.Int(z["floor"]);
                RectOf[id] = (Py.Float(z["x0"]), Py.Float(z["y0"]), Py.Float(z["x1"]), Py.Float(z["y1"]));
                if (!string.IsNullOrWhiteSpace(z.GetValueOrDefault("slab_mm"))) SlabOf[id] = Py.Float(z["slab_mm"]);   // 값 있는 줄만 (Python 딕셔너리 내포와 같음)
            }
            Order = FloorOf.Values.Distinct().Order().ToList();
            foreach (var p in Evaluator.Read(folder, "pours.csv")) Pours[p["zone_id"]!] = p;
            var fl = Pours.Keys.Where(FloorOf.ContainsKey).Select(z => FloorOf[z]).ToList();
            First = fl.Count > 0 ? fl.Min() : null;
            Weather = Evaluator.Read(folder, "weather.csv");
            sens = Evaluator.SensorsOf(folder);
        }

        /// <summary>staged.py _Site.zone_temps (W4, v6.2): 타설일부터 LimitDays 일 → (일평균 온도, 날마다 출처 '센서'·'기상'·'평년', 날마다 쓴 센서 ID).
        /// 구역 3 m 안(Evaluator.Near) 온도 센서 temp_c 일평균(여럿이면 낮은 값) 먼저, 센서 없는 날만 weather.csv → 평년</summary>
        public (List<double> Temps, List<string> Src, List<List<string>> Used) ZoneTemps(string zid)
        {
            if (zt.TryGetValue(zid, out var hit)) return hit;
            var cast = Cast(zid);
            var (pts, tail) = Evaluator.Near(sens.Floor(FloorOf[zid]).Select(kv => (kv.Key, Py.Float(kv.Value[0]["x"]), Py.Float(kv.Value[0]["y"]), kv.Value)).ToList(), RectOf[zid]);
            var daily = new Dictionary<DateOnly, Dictionary<string, List<double>>>();
            foreach (var (sid, _, _, rs) in tail.Length > 0 ? new() : pts)
                foreach (var r in rs)
                {
                    var d = DateOnly.FromDateTime(Evaluator.Ts(r["timestamp"]));
                    if (d < cast) continue;
                    if (!daily.TryGetValue(d, out var bySid)) daily[d] = bySid = new();
                    if (!bySid.TryGetValue(sid, out var l)) bySid[sid] = l = new();
                    l.Add(Py.Float(r["temp_c"]));
                }
            var wmap = new Dictionary<string, double>();
            foreach (var r in Weather) wmap[r["date"]!] = Py.Float(r["avgTa"]);
            var temps = new List<double>();
            var src = new List<string>();
            var used = new List<List<string>>();
            for (int i = 0; i < LimitDays; i++)
            {
                var d = cast.AddDays(i);
                var iso = d.ToString("yyyy-MM-dd", Inv);
                if (daily.TryGetValue(d, out var bySid))
                {
                    temps.Add(bySid.Values.Min(v => Py.Mean(v)));
                    src.Add("센서");
                    used.Add(bySid.Keys.Order(StringComparer.Ordinal).ToList());
                }
                else
                {
                    temps.Add(wmap.TryGetValue(iso, out var w) ? w : ShoringEngine.AirTemp(d));
                    src.Add(wmap.ContainsKey(iso) ? "기상" : "평년");
                    used.Add(new());
                }
            }
            return zt[zid] = (temps, src, used);
        }

        /// <summary>staged.py _Site.temp_label: 처음 n 일(최소 1)에 쓴 온도 출처 — '센서 S1·S2 일평균' / weather.csv 출처 이름 / '평년 [가정]', 여럿이면 ' + '</summary>
        public string TempLabel(string zid, int n)
        {
            var (_, src, used) = ZoneTemps(zid);
            n = Math.Max(n, 1);
            var head = src.Take(n).ToList();
            var sids = used.Take(n).SelectMany(u => u).Distinct().Order(StringComparer.Ordinal).ToList();
            return string.Join(" + ", new[] { head.Contains("센서") ? $"센서 {string.Join("·", sids)} 일평균" : null,
                head.Contains("기상") ? Evaluator.WeatherLabel(Weather) : null, head.Contains("평년") ? "평년 [가정]" : null }.Where(x => x != null));
        }

        /// <summary>타설 기록이 있는 가장 아래층</summary>
        public readonly int? First;
        /// <summary>타설 기록은 없지만 첫 기록 층보다 아래 = 이미 굳은 층 [가정] (staged.py _Site.hard, 34차)</summary>
        public bool Hard(string zid) => !Pours.ContainsKey(zid) && First is int f && FloorOf.GetValueOrDefault(zid, f) < f;

        /// <summary>타설일부터 400일 일평균 기온 (evaluate.daily_temps 그대로)</summary>
        public List<double> Temps(DateOnly cast)
        {
            if (!t.TryGetValue(cast, out var v)) t[cast] = v = Evaluator.DailyTemps(Weather, cast, LimitDays);
            return v;
        }

        public DateOnly Cast(string zid) => DateOnly.ParseExact(Pours[zid]["pour_date"]!, "yyyy-MM-dd", Inv);
        public DateTime Start(string zid) => Evaluator.FromIso($"{Pours[zid]["pour_date"]}T{(string.IsNullOrEmpty(Pours[zid].GetValueOrDefault("pour_time")) ? "23:59" : Pours[zid]["pour_time"])}");
        public (string Mix, double Fck) MixFck(string zid)
        {
            var p = Pours.TryGetValue(zid, out var x) ? x : Pours.Values.First();
            return (p["mix"]!, Py.Float(p["fck"]));
        }

        /// <summary>zid 아래층들(가까운 층부터 MinLevels − 1 개)의 평면 겹침 구역 — 데이터에 없는 층은 null</summary>
        public List<List<string>?> Below(string zid)
        {
            int fl = FloorOf[zid];
            var lower = Order.Take(Order.IndexOf(fl)).Reverse().Take(ShoringEngine.MinLevels - 1).ToList();
            var outp = lower.Select(g => FloorOf.Where(kv => kv.Value == g && Evaluator.Overlaps(RectOf[kv.Key], RectOf[zid])).Select(kv => kv.Key).ToList() is { Count: > 0 } l ? l : null).ToList();
            while (outp.Count < ShoringEngine.MinLevels - 1) outp.Add(null);
            return outp;
        }
    }

    /// <summary>첫 n 일 중 양생 온도 ≥ 10 ℃ 인 날 수 (누적, [가정] 해석). 센서 날(src '센서')은 실측 그대로, 기상·평년 날은 max(일평균 기온, 양생 하한) (W4)</summary>
    public static int WarmDays(IReadOnlyList<double> temps, int n, double cureMin = 5.0, IReadOnlyList<string>? src = null) =>
        temps.Take(Math.Max(n, 0)).Select((T, i) => (src != null && src[i] == "센서" ? T : Math.Max(T, cureMin)) >= WarmC).Count(x => x);

    /// <summary>표 3.3-2 무시험 일수 — 평균 ≥ 20 → 20 ℃ 칸, ≥ 10 → 10–20 ℃ 칸, 아니면 null (statistics.mean 과 같은 정확한 평균)</summary>
    public static int? NoTestDays(IReadOnlyList<double> temps, string mix)
    {
        var (hot, warm) = NoTestDaysTable[mix];
        if (Py.Mean(temps.Take(hot)) >= HotC) return hot;
        return Py.Mean(temps.Take(warm)) >= WarmC ? warm : null;
    }

    /// <summary>③ 위층 타설 하중비교 — (하중비, 지지 강도, 하중 kN/m², 여유 kN/m² 또는 null, fck 가정 썼나).
    /// strengthOf = 자료 없으면 null → 첫 타설 기록 층보다 아래면 fck [가정], 아니면 0 (staged.py panel_load)</summary>
    static (double Ratio, List<double> Sup, double Load, double? Cap, bool Assumed) PanelLoad(Site site, string zid, Func<string, double?> strengthOf, double fck)
    {
        double S(string z) => strengthOf(z) ?? (site.Hard(z) ? fck : 0.0);
        var below = site.Below(zid);
        var sup = new List<double> { S(zid) };
        foreach (var ov in below) sup.Add(ov is null ? fck : ov.Min(S));
        bool assumed = below.Any(ov => ov is null || ov.Any(site.Hard));
        bool hasT = site.SlabOf.TryGetValue(zid, out var mm);
        double live = Evaluator.LiveLoad(site.Folder);   // W12 시공하중 (options.json, 기본·최소 2.5)
        var (sdl, ll, _) = Evaluator.SupportLoads(site.Folder);   // 11-11 G1
        var ratio = hasT ? ShoringEngine.LoadCheck(sup.Select(x => (double?)x).ToList(), fck, tSlab: mm / 1000, live: live, sdl: sdl, ll: ll).Ratio : ShoringEngine.LoadCheck(sup.Select(x => (double?)x).ToList(), fck, live: live, sdl: sdl, ll: ll).Ratio;
        var load = hasT ? ShoringEngine.PourLoad(tSlab: mm / 1000, live: live, nSupport: sup.Count) : ShoringEngine.PourLoad(live: live, nSupport: sup.Count);
        return (ratio, sup, load, ratio != 0 ? load / ratio : null, assumed);
    }

    /// <summary>①②③ 이 처음 모두 맞는 날 (타설일부터, 엔진 예측, 400일 한도 → null). ④ 구조검토서는 날짜와 무관이라 뺌</summary>
    static DateOnly? EarliestPanel(Site site, string zid, double cureMin, Func<string, DateOnly?>? castOf = null, Func<string, (string, double)>? mixFck = null)
    {
        castOf ??= z => site.Pours.ContainsKey(z) ? site.Cast(z) : null;
        mixFck ??= site.MixFck;
        var c0 = castOf(zid)!.Value;
        var (mix, fck) = mixFck(zid);
        var t0 = site.Temps(c0);
        var (zt, zs) = site.Pours.ContainsKey(zid) && site.Cast(zid) == c0 ? (site.ZoneTemps(zid).Temps, (List<string>?)site.ZoneTemps(zid).Src) : (t0, (List<string>?)null);   // W4: 실제 타설이면 ② 는 구역 온도
        for (int a = 1; a < LimitDays; a++)
        {
            var d = c0.AddDays(a);
            if (ShoringEngine.Predict(mix, c0, a, fck, cureMin, t0) < PanelMpa || WarmDays(zt, a, cureMin, zs) < WarmDaysNeed) continue;
            double? FAt(string z)
            {
                var c = castOf(z);
                if (c is null) return null;                  // 타설 기록 없음 → PanelLoad 가 정함 (첫 기록 층 아래면 fck [가정])
                if (c.Value > d) return 0.0;                 // 아직 안 친 아래층은 못 받침
                var (m, k) = mixFck(z);
                return ShoringEngine.Predict(m, c.Value, d.DayNumber - c.Value.DayNumber, k, cureMin, site.Temps(c.Value));
            }
            if (PanelLoad(site, zid, FAt, fck).Ratio <= 1.0) return d;
        }
        return null;
    }

    // ── S1
    /// <summary>staged.wall_strip: 벽·기둥 측면 거푸집 해체일 두 가지 (강도 기준일은 공시체 시험 확인 시에만 근거)</summary>
    public static WallStripResult WallStrip(string folder, string zoneId, DateTime? at = null, double cureMin = 5.0)
    {
        var site = new Site(folder);
        if (!site.Pours.ContainsKey(zoneId)) throw new InvalidDataException($"{zoneId}: pours.csv 에 타설 기록 없음");
        var cast = site.Cast(zoneId);
        var (mix, fck) = site.MixFck(zoneId);
        var t = site.Temps(cast);
        int? n = DaysTo(mix, cast, fck, SideMpa, cureMin, t);
        DateOnly? byStrength = n is int nn && nn != 0 ? cast.AddDays(nn) : null;
        var zt = site.ZoneTemps(zoneId).Temps;   // W4: 무시험 일수 평균기온 = 구역 온도 (센서 우선 → 기상 → 평년)
        int? k = NoTestDays(zt, mix);
        DateOnly? byDays = k is int kk && kk != 0 ? cast.AddDays(kk) : null;
        var (_, warm) = NoTestDaysTable[mix];
        string daysTxt = k is int k2 && k2 != 0 ? $"무시험 {k2}일 — 첫 {k2}일 평균기온 {Py.F(Py.Mean(zt.Take(k2)), 1)} ℃ ({site.TempLabel(zoneId, k2)}), {mix} 칸 [확정] {SrcDays}"
            : $"무시험 일수 표 범위 밖 — 시험 필요 (첫 {warm}일 평균기온 {Py.F(Py.Mean(zt.Take(warm)), 1)} ℃ < {Py.G(WarmC)} ℃, {site.TempLabel(zoneId, warm)})";
        string strTxt = n is int n2 && n2 != 0 ? $"측면 {Py.G(SideMpa)} MPa 도달 {n2}일 (엔진 예측, 양생 하한 {Py.G(cureMin)} ℃) [확정 기준] {SrcSide}"
            : $"{LimitDays}일 안에 측면 {Py.G(SideMpa)} MPa 도달 안 함";
        string note = byStrength is { } bs && (byDays is null || bs < byDays.Value) ? "강도 기준일이 이름 — 강도 기준일은 공시체 시험 확인 시" : "";
        (bool, bool)? passed = at is { } a ? (byStrength is { } s1 && s1 <= DateOnly.FromDateTime(a), byDays is { } s2 && s2 <= DateOnly.FromDateTime(a)) : null;
        return new(zoneId, cast, byStrength, byDays, $"{strTxt} / {daysTxt}" + (note.Length > 0 ? $" / {note}" : ""), passed);
    }

    // ── S2
    /// <summary>④ 근거 원장의 구조검토서 기록 (판정 시각까지, 단계 무관) — 첫 기록 또는 null</summary>
    static Row? ReviewRecord(Site site, string zid, DateTime at)
    {
        var day = at.ToString("yyyy-MM-dd", Inv);
        return Evaluator.Read(site.Folder, Evidence.Ledger).FirstOrDefault(r => r["zone_id"] == zid && string.CompareOrdinal(r["date"], day) <= 0
            && ((r.GetValueOrDefault("doc_no") ?? "").Contains(ReviewKey) || (r.GetValueOrDefault("doc_type") ?? "").Contains(ReviewKey)));
    }

    /// <summary>staged.stages: 판정 시각까지 타설된 구역마다 판넬 탈형(필러 존치) 4조건 · 필러 = 기존 판정 · 판넬 탈형 가능일 (evaluate 순서)</summary>
    /// only = 그 구역만 (대시보드 판정 근거 한 구역 — 같은 계산의 부분, 판넬 탈형 가능일 400일 탐색을 구역마다 다 하지 않게)
    public static Dictionary<string, StageZone> Stages(string folder, DateTime at, double cureMin = 5.0, string? only = null)
    {
        var site = new Site(folder);
        var rows = Evaluator.Evaluate(folder, at, cureMin);
        var by = rows.ToDictionary(r => r.ZoneId);
        double? FNow(string z) => by.TryGetValue(z, out var r) ? (r.FCyl is double fc ? Math.Min(r.FEstMin, fc) : r.FEstMin) : null;   // 타설 기록 없음 → PanelLoad (34차)
        var outp = new Dictionary<string, StageZone>();
        foreach (var r in rows)
        {
            var zid = r.ZoneId;
            if (only != null && zid != only) continue;
            double fck = r.Fck, f = FNow(zid) ?? 0.0;
            int n = (int)((at - site.Start(zid)).TotalSeconds / 86400);   // evaluate 와 같은 정수 일 (버림)
            var (zTemps, zSrc, _) = site.ZoneTemps(zid);
            int w = WarmDays(zTemps, n, cureMin, zSrc);   // W4 센서 우선
            var (ratio, sup, load, cap, assumed) = PanelLoad(site, zid, FNow, fck);
            var rec = ReviewRecord(site, zid, at);
            string basis1 = r.FCyl is not null ? "공시체·추정 중 낮은 값" : "추정만 — 공시체 없음";
            var checks = new List<(string, bool, string)>
            {
                ("① 강도 ≥ 14 MPa", f >= PanelMpa, $"{Py.F(f, 1)} {(f >= PanelMpa ? "≥" : "<")} {Py.G(PanelMpa)} MPa ({basis1}) — {SrcPanel}"),
                ("② 10 ℃ 이상 4일", w >= WarmDaysNeed, $"양생 {n}일 중 10 ℃ 이상 {w}일 {(w >= WarmDaysNeed ? "≥" : "<")} {WarmDaysNeed}일 (온도: {site.TempLabel(zid, n)}) — {SrcWarm}"),
                ("③ 위층 하중비 ≤ 1.0", ratio <= 1.0, $"하중비 {Py.F(ratio, 2)} (지지 {string.Join(", ", sup.Select(x => Py.F(x, 1)))} MPa{(assumed ? " · 타설 기록 없는 아래층 = fck [가정]" : "")}) — {SrcLoad}"),
                ("④ 구조검토서 기록", rec is not null, rec is not null ? $"{rec["doc_no"]} ({rec["date"]})" : "구조검토서·책임기술자 승인 기록 필요"),
            };
            outp[zid] = new StageZone(r.Floor, r.PourDate, checks.All(c => c.Item2) ? PanelOk : PanelKeep, checks,
                new StageValues(f, r.FEstMin, r.FCyl, r.EstSource, n, w, ratio, sup, load, cap, fck, Evaluator.LiveLoad(folder)), r.Decision, r.Reason, EarliestPanel(site, zid, cureMin));
        }
        return outp;
    }

    static string E(string? s) => Py.Html(s);   // html.escape
    static string K(DateOnly d) => Py.KDate(d.ToString("yyyy-MM-dd", Inv));

    // ── S3
    /// <summary>staged.review_html: 조기 탈형 구조 검토서(초안) A4 한 장. shores = 그 구역 모델 동바리 본수 (null 이면 '모델 값' 칸)</summary>
    public static string ReviewHtml(string folder, DateTime at, string zoneId, int? shores = null, string project = "", double cureMin = 5.0)
        => ReviewHtml(Stages(folder, at, cureMin), at, zoneId, shores, project, folder);

    /// <summary>미리 구한 Stages 로 (여러 구역 검토서를 쓸 때 Stages 를 한 번만) — 결과는 위와 같음</summary>
    public static string ReviewHtml(Dictionary<string, StageZone> st, DateTime at, string zoneId, int? shores, string project, string folder)
    {
        if (!st.TryGetValue(zoneId, out var s)) throw new InvalidDataException($"{zoneId}: 판정 시각까지 타설된 구역이 아님");
        var x = s.Values;
        var rows = string.Concat(s.Checks.Select(c => $"<tr><td>{E(c.Name)}</td><td>{(c.Ok ? "통과" : !c.Name.StartsWith("④", StringComparison.Ordinal) ? "미충족" : "필요")}</td><td>{E(c.Basis)}</td></tr>"));
        var cap = x.Cap is double cv ? Py.F(cv, 2) : "—";
        var ep = s.EarliestPanelDate;
        string con = Evaluator.TextOption(folder, "contractor"), sup = Evaluator.TextOption(folder, "supervisor_firm");   // 11-12 서류 머리 자동 입력 ([프로젝트 설정])
        return $"""
<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>조기 탈형 구조 검토서 (초안) {E(zoneId)} {at.ToString("yyyy-MM-dd", Inv)}</title>
<style>@page{"{"}size:A4;margin:12mm{"}"}body{"{"}font:10pt 'Malgun Gothic',sans-serif;color:#111;background:#fff{"}"}h1{"{"}font-size:16pt;margin:0 0 4px{"}"}h2{"{"}font-size:12pt;margin:8px 0 3px{"}"}
table{"{"}border-collapse:collapse;width:100%;margin:6px 0{"}"}th,td{"{"}border:1px solid #888;padding:4px 6px;text-align:left;vertical-align:top{"}"}th{"{"}background:#eee{"}"}
.sign{"{"}break-inside:avoid{"}"}.sign td{"{"}height:40px{"}"}.note{"{"}color:#444;font-size:9.5pt{"}"}.draft{"{"}color:#a00;font-weight:600{"}"}{Provenance.Css}.annex{"{"}break-before:page{"}"}{ReportStyle.Css}</style></head><body>
<h1>조기 탈형(판넬 탈형·필러 존치) 구조 검토서 (초안)</h1>
<div class="draft">{E(Draft)}</div>
<div>{E(project)} · 구역 {E(zoneId)} ({s.Floor}층) · 타설 {Py.KDate(s.PourDate)} · 검토 기준 시각 {Py.KStamp(at)} · 작성 Revit On-Site{(con.Length > 0 ? " · 시공사 " + E(con) : "")}{(sup.Length > 0 ? " · 감리단 " + E(sup) : "")}</div>
<h2>1. 강도 근거 (그날)</h2>
<table><tr><th>구역 최소 추정</th><td>{Py.Repr(x.FEst)} MPa — {E(x.EstSource)}</td></tr>
<tr><th>현장양생 공시체 평균</th><td>{(x.FCyl is double fc ? E(Py.Repr(fc)) + " MPa" + (Evaluator.LotLabel(s.FillerReason) is { Length: > 0 } lot ? " (" + E(lot) + ")" : "") : "없음 — 공시체 시험 필요")}</td></tr>
<tr><th>검토에 쓴 값</th><td>{Py.F(x.F, 1)} MPa (둘 중 낮은 값)</td></tr></table>
<h2>2. 판넬 탈형 조건</h2>
<table><tr><th>조건</th><th>결과</th><th>계산 값·출처</th></tr>{rows}</table>
<p class="note">③ 식: 1.2(24·t + 0.4 + 0.5·n) + 1.6·{Py.G(x.Live)} = {Py.F(x.Load, 2)} kN/m² ≤ Σ(1.2·1.64 + 1.6·2.0)·√(f/fck) = {cap} kN/m²,
n = {x.Supports.Count}개층 (이 구역 + 아래층, 데이터에 없는 아래층 = fck {Py.G(x.Fck)} MPa [가정]), fck {Py.G(x.Fck)} MPa. 시공하중 {Py.G(x.Live)} kN/m² [가정] — 자재 적재·장비 집중하중 미반영.
판넬 탈형 가능일(엔진 예측, ①②③): {(ep is { } e1 ? K(e1) : $"{LimitDays}일 안에 없음")}</p>
<h2>3. 필러 동바리</h2>
<table><tr><th>배치</th><td>모델 동바리 전체 존치 (판넬만 탈형) — {SrcPanel}</td></tr>
<tr><th>본수</th><td>{(shores is int sh ? sh.ToString("N0", Inv) + "본 (모델)" : "모델 값: ____ 본")}</td></tr>
<tr><th>필러 해체 판정 (기존)</th><td>{E(s.FillerDecision)} — {E(s.FillerReason)}</td></tr></table>
<h2>4. 결론 (프로그램)</h2>
<p><b>{E(s.PanelDecision)}</b> — 조건 ①–④ {(s.PanelDecision == PanelOk ? "모두 충족" : "중 미충족·필요 항목 있음")}. 최종 판단은 책임기술자 검토·서명.</p>
<table class="sign"><tr><th>구분</th><th>소속</th><th>성명</th><th>서명</th><th>일시</th></tr>
<tr><td>작성 (시공사)</td><td>{E(con)}</td><td></td><td></td><td></td></tr>
<tr><td>검토 (책임기술자)</td><td></td><td></td><td></td><td></td></tr></table>
<p class="note">ACI PRC-347.2 (Shoring/Reshoring) 은 본문 미확인 — 개념만 참고, 수치는 위 국내 기준. {E(Draft)}.</p>
<div class="annex">{Provenance.TableHtml(Provenance.Facts(folder, zoneId, at))}</div>
</body></html>
""".Replace("\r\n", "\n").TrimEnd('\n');
    }
}

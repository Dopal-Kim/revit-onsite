// shoring/kpi.py 의 1:1 C# 이식 — 파장 지표 5개 (docs/impact_map.md 1절 '지표' 열). 새 판단 없이 기존 기록(승인·동바리 확인·근거 원장·알림·판정표)을 세기만 한다.
// 대시보드 맨 아래 '지표' 한 줄 (Lines 를 " · " 로 잇고, 툴팁에 다섯 줄).
using System.Globalization;

namespace RevitOnSite.Engine;

/// <summary>kpi.py kpis() 결과 — ReviewHours = 구역별 요청 → 감리 승인(서명 일시 우선) 시간 (승인 기록 순서)</summary>
public sealed record KpiResult(List<(string Zone, double Hours)> ReviewHours, double? ReviewMedian, int Checked, int Of, int RemovalSuspect, double? EvidenceFull, int Assumed, int AssumedOf);

public static class Kpi
{
    /// <summary>kpi.py _t: 빈칸 → null, 날짜만 → 그날 23:59 (보수 측, signature_watch 와 같음), 그 밖 ISO (T 또는 공백)</summary>
    static DateTime? T(string? s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return null;
        if (!s.Contains('T') && s.Length <= 10) s += "T23:59";
        return DateTime.ParseExact(s, new[] { "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF" },
            CultureInfo.InvariantCulture, DateTimeStyles.None);
    }

    /// <summary>kpi.py kpis(folder, at) — 판정은 cureMin (Python 기본 5.0)</summary>
    public static KpiResult Of(string folder, DateTime at, double cureMin = 5.0) => Evaluator.Checked(() =>
    {
        var rows = Evaluator.Evaluate(folder, at, cureMin);
        var day = DateOnly.FromDateTime(at);
        var iso = Py.Iso(day);
        var (ok, _) = Approval.Verify(folder);
        var log = ok ? Approval.ReadLog(folder).Where(r => string.CompareOrdinal((r["timestamp"] ?? "").Length > 10 ? r["timestamp"]![..10] : r["timestamp"] ?? "", iso) <= 0).ToList() : new();
        var req = new Dictionary<string, DateTime?>();
        var hours = new List<(string Zone, double Hours)>();
        foreach (var r in log)
        {
            var z = r["zone_id"]!;
            if (r["action"] == "요청 제출") req.TryAdd(z, T(r["timestamp"]));
            else if (r["action"] == "감리 승인 기록" && req.ContainsKey(z) && !hours.Any(h => h.Zone == z))
            {
                var done = T(r.GetValueOrDefault("signed_at")) ?? T(r["timestamp"]);
                if (done!.Value >= req[z]!.Value) hours.Add((z, Py.Round((done.Value - req[z]!.Value).TotalSeconds / 3600, 1)));   // 91차: 서명 < 요청이면 뺌 (kpi.py 같음)
            }
        }
        var k = Alerts.Of(folder, at);
        var judged = rows.Select(r => r.ZoneId).ToHashSet();
        int? top = rows.Count > 0 ? rows.Max(r => r.Floor) : null;
        var zones = Evaluator.Read(folder, "zones.csv");
        int? nxt = zones.Where(z => !judged.Contains(z["zone_id"]!) && top is not null && Py.Int(z["floor"]) > top).Select(z => (int?)Py.Int(z["floor"])).Min();
        var seen = new Dictionary<string, bool>();   // 같은 아래층 구역이 여러 번 나오면 한 번만 (뒤 값)
        foreach (var z in zones.Where(z => nxt is not null && Py.Int(z["floor"]) == nxt))
            foreach (var c in PourGate.Evaluate(folder, z["zone_id"]!, at).Checks) seen[c.ZoneId] = c.Ok;
        var led = Evidence.LedgerAt(folder, day);
        var reqZones = rows.Where(r => r.Decision == "해체 승인 요청 가능").Select(r => r.ZoneId).ToList();
        var hv = hours.Select(h => h.Hours).OrderBy(x => x).ToList();
        double? median = hv.Count == 0 ? null : hv.Count % 2 == 1 ? hv[hv.Count / 2] : (hv[hv.Count / 2 - 1] + hv[hv.Count / 2]) / 2;   // statistics.median
        hours = hours.OrderBy(h => h.Zone, Evaluator.FloorOrder(folder)).ToList();   // 구역별 시간은 층 순 → 구역 ID (kpi.py, 총괄 11-05)
        return new KpiResult(hours, median, seen.Values.Count(v => v), seen.Count, k.Count(a => a.Kind == "무단 해체 의심"),
            reqZones.Count > 0 ? Py.Round((double)reqZones.Sum(z => led.TryGetValue(z, out var l) ? l.Stage : 0) / reqZones.Count, 1) : null,
            rows.Count(r => (r.EstSource + r.Reason).Contains("[가정]")), rows.Count);
    });

    /// <summary>kpi.py lines: 다섯 줄 (Python 과 같은 글자)</summary>
    public static List<string> Lines(KpiResult k)
    {
        var review = k.ReviewMedian is not { } m ? "기록 없음"
            : $"중앙값 {Py.G(m)} h · " + string.Join(" · ", k.ReviewHours.Select(h => $"{h.Zone} {Py.G(h.Hours)} h"));
        return new()
        {
            $"감리 확인 시간 (요청 → 감리 승인): {review}",
            $"타설 전 동바리 확인: {k.Checked}/{k.Of} 구역 (최근 24 h '예')",
            $"무단 해체 의심: {k.RemovalSuspect} 구역",
            $"요청 가능 구역 근거 원장 평균: {(k.EvidenceFull is { } e ? Py.G(e) + "/8" : "—")}",
            $"판정 근거에 [가정] 이 든 구역: {k.Assumed}/{k.AssumedOf}",
        };
    }
}

/// <summary>kpi_trend.py 한 줄 (eval_at, review_median_h, shore_checked, shore_of, removal_suspect, evidence_avg, assumed, judged)</summary>
public sealed record KpiTrendRow(string EvalAt, double? ReviewMedian, int Checked, int Of, int RemovalSuspect, double? EvidenceAvg, int Assumed, int Judged);

/// <summary>shoring/kpi_trend.py 의 1:1 C# 이식 (방향성 5판 F13) — 판정 이력(eval_log.csv)의 판정 시각마다 지표를 다시 잼. 같은 시각은 한 번, 시각 순. 새 판단 없음</summary>
public static class KpiTrend
{
    public static readonly string[] Head = { "eval_at", "review_median_h", "shore_checked", "shore_of", "removal_suspect", "evidence_avg", "assumed", "judged" };

    /// <summary>kpi_trend.py trend. last 를 주면 시각 순 마지막 last 개만 잼 (= trend(folder)[-last:], 대시보드 툴팁이 판정 이력이 길어도 느려지지 않게)</summary>
    public static List<KpiTrendRow> Trend(string folder, int? last = null, double cureMin = 5.0)
    {
        var ats = new List<string>();
        foreach (var h in Evaluator.Read(folder, Alerts.EvalLog))
        {
            var at = (h.GetValueOrDefault("eval_at") ?? "").Trim();
            if (at.Length > 0 && !ats.Contains(at)) ats.Add(at);
        }
        var pick = ats.OrderBy(a => a, StringComparer.Ordinal).ToList();
        if (last is int n && pick.Count > n) pick = pick.Skip(pick.Count - n).ToList();
        return pick.Select(a =>
        {
            var k = Kpi.Of(folder, DateTime.ParseExact(a, new[] { "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss" }, Py.Inv, System.Globalization.DateTimeStyles.None), cureMin);
            return new KpiTrendRow(a, k.ReviewMedian, k.Checked, k.Of, k.RemovalSuspect, k.EvidenceFull, k.Assumed, k.AssumedOf);
        }).ToList();
    }

    /// <summary>kpi_trend.py --csv 와 같은 바이트 (UTF-8 BOM, csv.writer — 실수는 Python repr, 없으면 빈칸)</summary>
    public static void WriteCsv(IEnumerable<KpiTrendRow> rows, string path)
    {
        static string R(double? v) => v is { } x ? Py.Repr(x) : "";
        Py.WriteCsv(path, new[] { Head }.Concat(rows.Select(r => new[] { r.EvalAt, R(r.ReviewMedian), r.Checked.ToString(Py.Inv), r.Of.ToString(Py.Inv),
            r.RemovalSuspect.ToString(Py.Inv), R(r.EvidenceAvg), r.Assumed.ToString(Py.Inv), r.Judged.ToString(Py.Inv) })));
    }

    /// <summary>대시보드 '지표' 툴팁 아래 추이 두 줄 (최근 판정들, " → ") — 화면 전용 글자 (Python 대응 없음)</summary>
    public static List<string> Lines(IReadOnlyList<KpiTrendRow> rows)
    {
        if (rows.Count < 2) return new();
        string A(Func<KpiTrendRow, string> f) => string.Join(" → ", rows.Select(f));
        static string G(double? v, string unit) => v is { } x ? Py.G(x) + unit : "—";
        return new()
        {
            $"추이 (최근 {rows.Count}회 판정): {A(r => r.EvalAt.Length >= 10 ? r.EvalAt[5..10] : r.EvalAt)}",
            $"타설 전 확인 {A(r => $"{r.Checked}/{r.Of}")} · 무단 해체 의심 {A(r => r.RemovalSuspect.ToString(Py.Inv))} · [가정] {A(r => $"{r.Assumed}/{r.Judged}")} · 근거 평균 {A(r => G(r.EvidenceAvg, "/8"))} · 감리 확인 {A(r => G(r.ReviewMedian, " h"))}",
        };
    }
}

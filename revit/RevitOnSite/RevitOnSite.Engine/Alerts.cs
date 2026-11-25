// shoring/alerts.py 의 1:1 C# 이식 — 오늘 볼 것 (알림): 휴대폰 맨 위 배지·대시보드 한 줄 (docs/direction_log.md 3판 D4).
// 판정·결론은 바꾸지 않고, 이미 있는 계산(Evaluator · PourGate · Evidence.RemovalWatch)을 '사람이 지금 할 일' 순서로 모은다.
// 종류 (위에서부터 급한 순서): 무단 해체 의심 → 타설 보류(판정된 최상층 바로 위 층 구역) → 시험 필요(공시체 없음·예측 도달일 ≤ 판정일) → 상태 바뀜(prevAt 대비)
//   → 입회 없음(판정일까지 타설했는데 pours.csv 입회 기록이 빈 구역, 열이 없으면 안 셈 — Quality.WatchMissing, F5)
namespace RevitOnSite.Engine;

/// <summary>alerts.py 한 줄 {kind, zone_id, text}</summary>
public sealed record Alert(string Kind, string ZoneId, string Text);

public static class Alerts
{
    public static readonly string[] Order = { "무단 해체 의심", "타설 보류", "순찰 확인 없음", "감리 확인 못 넣음", "공시체 미입력 (예측일 지남)", "상태 바뀜", "입회 없음" };   // 순찰 = v6.2 W9, 감리 확인 못 넣음 = W-A
    /// <summary>[가정] 순찰 확인 간격 (시간) — options.json patrol_hours 로 바꿈 (alerts.py PATROL_H, v6.2 W9)</summary>
    public const double PatrolH = 24;

    /// <summary>alerts.py patrol_missing (W9): 판정 구역 중 동바리 '해체됨'(ShoreStatus) 이 아닌 곳에 판정 시각 전 patrol_hours 안 동바리 확인 줄이 없음 → 구역 → 마지막 확인 시각 (없으면 null).
    /// 확인 = shore_checks.csv 줄 (예/아니오 모두), 협력업체 신고는 뺌, 판정 시각 뒤 줄은 안 셈</summary>
    public static Dictionary<string, DateTime?> PatrolMissing(string folder, DateTime at, List<ZoneRow>? rows = null, double cureMin = 5.0)
    {
        double h = Evaluator.NumOption(folder, "patrol_hours", PatrolH);
        rows ??= Evaluator.Evaluate(folder, at, cureMin);
        var gone = ShoreStatus.Status(folder, at, cureMin).Where(s => s.State == ShoreStatus.Removed).Select(s => s.ZoneId).ToHashSet();
        var last = new Dictionary<string, DateTime>();
        foreach (var r in Evaluator.Read(folder, "shore_checks.csv"))
        {
            if ((r.GetValueOrDefault("source") ?? "").Trim() == Evidence.ReportSource || string.IsNullOrWhiteSpace(r.GetValueOrDefault("timestamp"))) continue;
            var t = Evaluator.Ts(r["timestamp"]);
            var z = (r.GetValueOrDefault("zone_id") ?? "").Trim();
            if (t <= at && (!last.TryGetValue(z, out var o) || t > o)) last[z] = t;
        }
        var outp = new Dictionary<string, DateTime?>();
        foreach (var r in rows)
        {
            var z = r.ZoneId;
            if (gone.Contains(z) || (last.TryGetValue(z, out var lt) && (at - lt).TotalSeconds <= h * 3600)) continue;
            outp[z] = last.TryGetValue(z, out var l2) ? l2 : null;
        }
        return outp;
    }

    /// <summary>alerts.py patrol_floors (W9, 총괄 11-09 소음 줄이기): 오늘 볼 것 층마다 한 줄 → (첫 구역 ID, 문구) — 펼치면(문구 끝) 구역 목록.
    /// '마지막 확인' = 그 층 빠진 구역 중 가장 최근 확인 (없으면 '확인 기록 없음')</summary>
    public static List<(string ZoneId, string Text)> PatrolFloors(string folder, DateTime at, List<ZoneRow>? rows = null, double cureMin = 5.0)
    {
        double h = Evaluator.NumOption(folder, "patrol_hours", PatrolH);
        var miss = PatrolMissing(folder, at, rows, cureMin);
        var floorOf = new Dictionary<string, int>();
        foreach (var r in Evaluator.Read(folder, "zones.csv")) floorOf[r["zone_id"]!] = Py.Int(r["floor"]);
        var by = new List<(int Fl, List<string> Zs)>();   // 층이 처음 나온 순서 (Python dict)
        foreach (var z in miss.Keys.OrderBy(z => z, Evaluator.FloorOrder(folder)))
        {
            int fl = floorOf[z];
            var g = by.FirstOrDefault(x => x.Fl == fl);
            if (g.Zs is null) by.Add((fl, new List<string> { z })); else g.Zs.Add(z);
        }
        var outp = new List<(string, string)>();
        foreach (var (fl, zs) in by)
        {
            var lasts = zs.Where(z => miss[z] is not null).Select(z => miss[z]!.Value).ToList();
            var since = lasts.Count > 0 ? $"마지막 확인 {Py.F((at - lasts.Max()).TotalSeconds / 3600, 0)}시간 전" : "확인 기록 없음";
            var name = StrengthTimeline.LabelOf(fl, floorOf.Where(kv => kv.Value == fl).Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList());
            outp.Add((zs[0], $"{name} 순찰 확인 없음 {zs.Count}구역 (기준 {Py.G(h)}시간 [가정] · {since}) — {string.Join(", ", zs)} (순찰 때 휴대폰 [동바리 확인])"));
        }
        return outp;
    }

    /// <summary>alerts.py alerts(): Order 순, 같은 종류 안에서는 구역 ID 순 (안정 정렬). 입력 오류는 Evaluator 의 InvalidDataException</summary>
    public static List<Alert> Of(string folder, DateTime at, DateTime? prevAt = null, double cureMin = 5.0)
    {
        var rows = Evaluator.Evaluate(folder, at, cureMin);
        var outp = Evidence.RemovalWatch(folder, DateOnly.FromDateTime(at)).Select(kv => new Alert("무단 해체 의심", kv.Key, kv.Value)).ToList();
        var judged = rows.Select(r => r.ZoneId).ToHashSet();
        int? top = rows.Count > 0 ? rows.Max(r => r.Floor) : null;
        var zones = Evaluator.Read(folder, "zones.csv");
        int? nxt = zones.Where(z => !judged.Contains(z["zone_id"]!) && top is not null && Py.Int(z["floor"]) > top).Select(z => (int?)Py.Int(z["floor"])).Min();
        foreach (var z in zones.Where(z => nxt is not null && Py.Int(z["floor"]) == nxt).Select(z => z["zone_id"]!).OrderBy(z => z, StringComparer.Ordinal))
        {
            var g = PourGate.Evaluate(folder, z, at);
            if (g.Decision == "타설 보류") outp.Add(new("타설 보류", z, $"{z} 타설 전 — {g.Reason}"));
        }
        outp.AddRange(PatrolFloors(folder, at, rows, cureMin).Select(x => new Alert("순찰 확인 없음", x.ZoneId, x.Text)));   // W9 층 단위 한 줄
        foreach (var (z, v) in Approval.SuperviseRejectList(folder, at))   // W-A: 서버 감리 직접 확인을 동기화 때 넣지 못함 — 이유 그대로
            outp.Add(new("감리 확인 못 넣음", z, v.Reason.StartsWith(FieldServer.RejectTag + " — ", StringComparison.Ordinal)   // 1-2 보완 요청 (alerts.py 와 같음)
                ? $"{z} {FieldServer.RejectTag} ({v.Name}, {v.At.Replace('T', ' ')}) — {v.Reason[(FieldServer.RejectTag.Length + 3)..]}"
                : $"{z} 감리 직접 확인({v.Name}, {v.At.Replace('T', ' ')})을 넣지 못함 — {v.Reason}"));
        var day = DateOnly.FromDateTime(at);
        foreach (var r in rows)
            if (r.FCyl is null && r.Decision != "해체 승인 요청 가능" && r.PredictedDue <= day)
                outp.Add(new("공시체 미입력 (예측일 지남)", r.ZoneId, $"{r.ZoneId} 예측 도달일 {Py.Iso(r.PredictedDue)} 지남 — 현장양생 공시체 시험 결과 입력 필요"));
        if (prevAt is { } pa)
        {
            var before = Evaluator.Evaluate(folder, pa, cureMin).GroupBy(r => r.ZoneId).ToDictionary(g => g.Key, g => g.Last().Decision);
            foreach (var r in rows)
                if (before.TryGetValue(r.ZoneId, out var b) && b.Length > 0 && b != r.Decision)
                    outp.Add(new("상태 바뀜", r.ZoneId, $"{r.ZoneId}: {b} → {r.Decision} ({pa:MM-dd} → {at:MM-dd})"));
        }
        var iso = Py.Iso(day);
        var poured = Evaluator.Read(folder, "pours.csv").Where(p => string.CompareOrdinal(p["pour_date"], iso) <= 0).Select(p => p["zone_id"]).ToHashSet();
        outp.AddRange((Quality.WatchMissing(folder) ?? new()).Where(kv => poured.Contains(kv.Key)).Select(kv => new Alert("입회 없음", kv.Key, kv.Value)));
        return outp.OrderBy(a => Array.IndexOf(Order, a.Kind)).ThenBy(a => a.ZoneId, Evaluator.FloorOrder(folder)).ToList();   // 같은 종류 안: 층 순 → 구역 ID (총괄 11-05)
    }

    /// <summary>판정 이력 — 애드인 [판정] 이 한 줄씩 추가만. '상태 바뀜' 비교 기준 + 근거 원장 보조 자료 (alerts.py EVAL_LOG)</summary>
    public const string EvalLog = "eval_log.csv";

    /// <summary>alerts.py eval_summary: 판정 결론 기준 요약 한 줄 (승인 기록 6·7·8 은 세지 않음)</summary>
    public static string EvalSummary(IReadOnlyCollection<ZoneRow> rows, string? folder = null, DateTime? at = null)
    {
        var left = folder != null ? Approval.RequestRows(rows.ToList(), folder, at) : rows.ToList();   // 11-12: 사람 기록(6·7·8) 구역은 '이미 기록 n' 으로 (alerts.py 와 같음)
        int done = rows.Count - left.Count;
        return Summary(left) + (done > 0 ? $" · 이미 기록 {done} (감리 승인·해체 완료·재받침)" : "");
    }

    static string Summary(IReadOnlyCollection<ZoneRow> rows) =>
        $"해체 승인 요청 가능 {rows.Count(r => r.Decision == "해체 승인 요청 가능")} · 존치 {rows.Count(r => r.Decision.StartsWith("존치", StringComparison.Ordinal))}"
        + (rows.Count(r => r.Decision == "존치 (설치 결함 기록)") is int d2 && d2 > 0 ? $" (설치 결함 {d2})" : "")   // F16 규칙 ⑦ (alerts.py 와 같음, 37차)
        + $" · 공시체 시험 필요 {rows.Count(r => r.Decision == "공시체 시험 필요")}";

    /// <summary>alerts.py log_eval: 판정 한 번 → eval_log.csv 한 줄 추가 (Python csv 와 같은 바이트: 머리는 새 파일일 때만, CRLF, 필요할 때만 따옴표)</summary>
    public static void LogEval(string folder, DateTime at, string summary, DateTime? now = null)
    {
        static string Q(string v) => v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        var p = Path.Combine(folder, EvalLog);
        string line = string.Join(",", at.ToString("yyyy-MM-dd'T'HH:mm", Py.Inv), (now ?? DateTime.Now).ToString("yyyy-MM-dd'T'HH:mm:ss", Py.Inv), Q(summary)) + "\r\n";
        File.AppendAllText(p, (File.Exists(p) ? "" : "eval_at,logged_at,summary\r\n") + line, new System.Text.UTF8Encoding(false));
    }

    /// <summary>alerts.py prev_eval: eval_log.csv 에서 at 과 다른 판정 시각 중 마지막 줄 (없으면 null) — '상태 바뀜' 비교 기준</summary>
    public static DateTime? PrevEval(string folder, DateTime at)
    {
        string me = at.ToString("yyyy-MM-dd'T'HH:mm", Py.Inv);
        var last = Evaluator.Read(folder, EvalLog).Select(r => r.GetValueOrDefault("eval_at")).LastOrDefault(v => !string.IsNullOrEmpty(v) && v != me);
        return last is null ? null : DateTime.ParseExact(last, "yyyy-MM-dd'T'HH:mm", Py.Inv);
    }

    /// <summary>대시보드·휴대폰 머리 한 줄: "오늘 볼 것 n — 무단 해체 의심 a · 타설 보류 b · …" (없는 종류는 뺌, 0건이면 빈 글)</summary>
    public static string Summary(IReadOnlyCollection<Alert> alerts) => alerts.Count == 0 ? ""
        : $"오늘 볼 것 {alerts.Count} — " + string.Join(" · ", Order.Select(k => (k, n: alerts.Count(a => a.Kind == k))).Where(q => q.n > 0).Select(q => $"{q.k} {q.n}"));
}

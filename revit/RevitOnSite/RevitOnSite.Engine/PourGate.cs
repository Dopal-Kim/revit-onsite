// shoring/pour_gate.py 의 1:1 C# 이식: 타설 전 게이트 — 다음 층 타설 요청 전에 아래 지지층 동바리가 실제로 서 있는지.
// 화정 2022 사조위 [확정]: 결정적 단계는 해체 승인이 아니라 "하부 3개층 동바리가 이미 빠진 상태에서 상층 타설".
// '없다는 기록이 없음'이 아니라 '있다는 최근 확인'을 요구한다.
// 입력 (데이터 폴더): zones.csv, shore_checks.csv (zone_id, timestamp, present 예/아니오, actor, photo), approvals.csv (7 해체 완료 · 8 재받침)
// 결론: "타설 요청 가능" / "타설 보류" (+ 사유). 프로그램은 타설을 승인하거나 지시하지 않는다.
using System.Globalization;

namespace RevitOnSite.Engine;

public sealed record PourCheck(int Floor, string ZoneId, bool Ok, string Reason);

public sealed record PourGateResult(string ZoneId, string Decision, string Reason, List<PourCheck> Checks);

public static class PourGate
{
    public const string ChecksFile = "shore_checks.csv";
    /// <summary>[가정] 이 시간 안의 '있음' 확인만 인정 (매일 확인 운영 기준) — pour_gate.py CHECK_FRESH_H</summary>
    public const double CheckFreshH = 24.0;

    static string Md(DateTime t) => t.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>zoneId = 지금 타설하려는 구역. 그 아래로 평면이 겹치는 nLevels 개 층(층 순서)의 구역마다 동바리 확인.
    /// zones.csv 에 없으면 InvalidDataException (Python SystemExit 와 같은 문구)</summary>
    public static PourGateResult Evaluate(string folder, string zoneId, DateTime at, int nLevels = ShoringEngine.MinLevels)
    {
        var zones = new Dictionary<string, Dictionary<string, string?>>();
        foreach (var r in Evaluator.Read(folder, "zones.csv")) zones[r["zone_id"]!] = r;
        if (!zones.ContainsKey(zoneId))
            throw new InvalidDataException($"zones.csv 에 {zoneId} 없음");
        var floorOf = zones.ToDictionary(kv => kv.Key, kv => Py.Int(kv.Value["floor"]));
        var rectOf = zones.ToDictionary(kv => kv.Key, kv => (Py.Float(kv.Value["x0"]), Py.Float(kv.Value["y0"]), Py.Float(kv.Value["x1"]), Py.Float(kv.Value["y1"])));
        var order = floorOf.Values.Distinct().Order().ToList();
        var below = order.Where(f => f < floorOf[zoneId]).Reverse().Take(nLevels).ToList();          // 바로 아래층부터
        var status = Approval.StatusOverride(folder, at);                                                  // 7 해체 완료 · 8 재받침
        var last = new Dictionary<string, (DateTime T, string Present, string Actor)>();
        foreach (var r in Evaluator.Read(folder, ChecksFile))
        {
            var t = Evaluator.Ts(r["timestamp"]);
            if (t <= at && (!last.TryGetValue(r["zone_id"]!, out var prev) || t >= prev.T))
                last[r["zone_id"]!] = (t, (r.GetValueOrDefault("present") ?? "").Trim(), r.GetValueOrDefault("actor") ?? "");
        }
        var rows = new List<PourCheck>();
        var hold = new List<string>();
        if (ShoreInspect.OpenNg(folder, at, ShoreInspect.Install).TryGetValue(zoneId, out var ng))   // F16 ⑦: 타설하려는 구역의 설치 검측 NG 가 풀리지 않으면 보류
            hold.Add($"{zoneId}: 설치 검측 결함 기록 미해결 — {ShoreInspect.NgText(ng)}");
        foreach (var f in below)
            foreach (var z in zones.Keys.Where(z => floorOf[z] == f && Evaluator.Overlaps(rectOf[z], rectOf[zoneId])).OrderBy(z => z, StringComparer.Ordinal))
            {
                bool has = last.TryGetValue(z, out var c);
                string msg;
                if (status.GetValueOrDefault(z) == 7)
                    msg = "해체 완료 기록 있음 — 재받침 기록 없이는 지지 안 됨";
                else if (has && c.Present == "아니오")
                    msg = $"{Md(c.T)} {c.Actor} 확인: 동바리 없음";
                else if (!has || (at - c.T).TotalSeconds / 3600 > CheckFreshH)
                    msg = "최근 24시간 동바리 확인 없음" + (has ? $" (마지막 {Md(c.T)})" : "");
                else
                    msg = "";
                rows.Add(new PourCheck(f, z, msg.Length == 0, msg.Length > 0 ? msg : $"{Md(c.T)} {c.Actor} 확인: 동바리 있음"));
                if (msg.Length > 0)
                    hold.Add($"{z}: {msg}");
            }
        if (below.Count < nLevels && below.Count > 0 && below[^1] != order[0])
            hold.Add($"아래 층 수 {below.Count} < {nLevels}");
        return new PourGateResult(zoneId, hold.Count > 0 ? "타설 보류" : "타설 요청 가능",
            hold.Count > 0 ? string.Join("; ", hold) : below.Count > 0 ? $"아래 {below.Count}개층 동바리 최근 확인 있음" : "맨 아래층 — 확인할 아래 동바리 없음", rows);
    }
}

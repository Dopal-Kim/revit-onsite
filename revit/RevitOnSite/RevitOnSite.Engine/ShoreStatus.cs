// shoring/shore_status.py 의 1:1 C# 이식: 동바리 상태 (V2 동바리 보기 — docs/shore_view_spec.md §V2, 총괄 11-05) — 구역마다 '기록 기준' 상태, 판정 시각 기준.
// 기존 함수 조합만: 판정 Evaluator.Evaluate · 승인 기록 Approval.StatusOverride(6/7/8) · 무단 해체 의심 Evidence.RemovalWatch
// · 타설 전 아래층 24 h 확인 PourGate (다음 타설 층 = alerts 와 같은 규칙) · 설치 검측 install_inspection.csv · 협력업체 해체 신고 '뺐음'.
// 결과 = 채움 상태 하나 + 테두리(설치 검측 필요/NG) + 점선(최근 24 h 확인 없음). 화면 표시용 — 판정·결론은 바꾸지 않음. Dump 는 Python dump 와 바이트 동일.
using System.Globalization;
using Row = System.Collections.Generic.IReadOnlyDictionary<string, string?>;

namespace RevitOnSite.Engine;

public sealed record ShoreState(string ZoneId, int Floor, string State, string Install, string Stale, string Reason);

public static class ShoreStatus
{
    const string ReadyDecision = "해체 승인 요청 가능";
    public const string Suspect = "무단 해체 의심", Removed = "해체됨", Approved = "감리 확인", Ready = "해체 승인 요청 가능", Standing = "서 있어야 함";
    public const string InstallNeed = "설치 검측 필요", InstallNg = "설치 검측 NG", Stale = "최근 24 h 확인 없음";
    public const string ShoreNg = "NG 동바리";   // F16: 풀리지 않은 점검 NG 가 있는 동바리 (모든 단계) — Revit 동바리 빨강 테두리
    /// <summary>범례 칩 = (이름, R, G, B, 그리기 채움/윤곽/깜빡임/테두리/점선) — 색 [가정], shore_status.py LEGEND 와 같은 순서</summary>
    public static readonly (string Name, byte R, byte G, byte B, string Draw)[] Legend =
    {
        (Standing, 90, 90, 90, "채움"), (Ready, 235, 190, 0, "채움"), (Approved, 30, 150, 60, "채움"),
        (Removed, 170, 170, 170, "윤곽"), (Suspect, 215, 25, 25, "깜빡임"),
        (InstallNeed, 240, 130, 0, "테두리"), (InstallNg, 215, 25, 25, "테두리"), (Stale, 40, 40, 40, "점선"),
        (ShoreNg, 215, 25, 25, "테두리"),
    };

    public static (byte R, byte G, byte B) ColorOf(string name)
    {
        var l = Legend.First(x => x.Name == name);
        return (l.R, l.G, l.B);
    }

    static string V(Row r, string k) => r.TryGetValue(k, out var v) ? v ?? "" : "";
    static string Head10(string s) => s.Length > 10 ? s[..10] : s;

    /// <summary>zones.csv 의 모든 구역, 층 순 → 구역 ID 순 — shore_status.py status</summary>
    public static List<ShoreState> Status(string folder, DateTime at, double cureMin = 5.0)
    {
        string day = at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var zones = new Dictionary<string, int>();
        foreach (var r in Evaluator.Read(folder, "zones.csv")) zones[r["zone_id"] ?? ""] = Py.Int(r["floor"]);
        var rows = Evaluator.Evaluate(folder, at, cureMin);
        var dec = new Dictionary<string, string>();
        foreach (var r in rows) dec[r.ZoneId] = r.Decision;
        var code = Approval.StatusOverride(folder, at);
        var watch = Evidence.RemovalWatch(folder, DateOnly.FromDateTime(at));
        var last = new Dictionary<string, (DateTime T, string K)>();   // 마지막 해체/재받침 사건 — "7" 해체 완료 · "8" 재받침 · "신고"
        foreach (var r in Approval.ReadLog(folder))
        {
            var k = Approval.CodeOf(r.GetValueOrDefault("action"));
            var ts = r.GetValueOrDefault("timestamp") ?? "";
            if (k is not (7 or 8) || string.CompareOrdinal(Head10(ts), day) > 0) continue;
            var t = DateTime.ParseExact(ts, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
            var z = r.GetValueOrDefault("zone_id") ?? "";
            if (!last.TryGetValue(z, out var l) || t >= l.T) last[z] = (t, k.Value.ToString(CultureInfo.InvariantCulture));
        }
        foreach (var r in Evaluator.Read(folder, PourGate.ChecksFile))
        {
            if (V(r, "source").Trim() != Evidence.ReportSource || V(r, "present").Trim() != "아니오") continue;
            var t = Evaluator.Ts(r["timestamp"]);
            var z = r["zone_id"] ?? "";
            if (string.CompareOrdinal(t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), day) <= 0 && (!last.TryGetValue(z, out var l) || t >= l.T)) last[z] = (t, "신고");
        }
        var insNg = ShoreInspect.OpenNg(folder, at, ShoreInspect.Install);   // F16: 판정 시각까지 풀리지 않은 설치 NG (다시 점검해 OK 면 풀림 — 규칙 ⑦ 과 같음)
        var ins = new Dictionary<string, bool>();                              // 구역 → NG 있음
        foreach (var r in ShoreInspect.Records(folder, at).Where(r => r.Stage == ShoreInspect.Install)) ins[r.ZoneId] = insNg.ContainsKey(r.ZoneId);
        int? top = rows.Count > 0 ? rows.Max(r => r.Floor) : null;   // 다음 타설 층 (alerts 와 같음) 아래 확인 대상의 '최근 24시간 확인 없음'
        var later = zones.Where(kv => !dec.ContainsKey(kv.Key) && top != null && kv.Value > top).Select(kv => kv.Value).ToList();
        int? nxt = later.Count > 0 ? later.Min() : null;
        var stale = new Dictionary<string, string>();
        foreach (var z in zones.Where(kv => kv.Value == nxt).Select(kv => kv.Key).OrderBy(z => z, StringComparer.Ordinal))
            foreach (var c in PourGate.Evaluate(folder, z, at).Checks)
                if (!c.Ok && c.Reason.StartsWith("최근 24시간", StringComparison.Ordinal)) stale.TryAdd(c.ZoneId, $"{z} 타설 전 확인 대상 — {c.Reason}");
        var outp = new List<ShoreState>();
        foreach (var zid in zones.Keys.OrderBy(z => z, Evaluator.FloorOrder(folder)))
        {
            string state, reason;
            last.TryGetValue(zid, out var e);
            if (watch.TryGetValue(zid, out var w)) (state, reason) = (Suspect, w);
            else if (e.K != null && e.K != "8") (state, reason) = (Removed, (e.K == "7" ? "해체 완료 기록" : "협력업체 해체 신고") + " " + e.T.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture));
            else if (code.GetValueOrDefault(zid) == 6) (state, reason) = (Approved, "감리 승인 기록");
            else if (dec.GetValueOrDefault(zid) == ReadyDecision) (state, reason) = (Ready, ReadyDecision);
            else (state, reason) = (Standing, dec.GetValueOrDefault(zid, "타설 전") + (code.GetValueOrDefault(zid) == 8 ? " · 재받침 기록" : ""));
            string install = state == Removed ? "" : !ins.TryGetValue(zid, out var ng) ? InstallNeed : ng ? InstallNg : "";
            outp.Add(new ShoreState(zid, zones[zid], state, install, stale.GetValueOrDefault(zid, ""), reason));
        }
        return outp;
    }

    /// <summary>일치 시험용 글자 (탭 구분, 줄마다 \n) — shore_status.py dump</summary>
    public static string Dump(IEnumerable<ShoreState> rows) =>
        string.Concat(rows.Select(r => $"{r.ZoneId}\t{r.Floor.ToString(CultureInfo.InvariantCulture)}\t{r.State}\t{r.Install}\t{r.Stale}\t{r.Reason}\n"));
}

// shoring/after_check.py 의 1:1 C# 이식 (방향성 4판 F3) — 해체 후 점검: 거푸집 및 동바리 해체 가이드라인(국토교통부 2023.1) p51 4.3 [확정].
// after_check.csv  zone_id, date, item_code(AC-1..AC-5), result(OK/NG/빈칸), by, photo. 판정은 바꾸지 않고 경고만 (근거 원장 8 결과보고와 묶음).
namespace RevitOnSite.Engine;

public sealed record AfterStatus(string Date, IReadOnlyList<(string Code, string Result)> Items, List<string> Missing, bool Ok);

public static class AfterCheck
{
    public const string File = "after_check.csv";
    public static readonly (string Code, string Name)[] Items =
    {
        ("AC-1", "필러·지지 동바리 타설층 포함 하부 3개층 설치"), ("AC-2", "각 층 같은 위치(상하 정렬)"), ("AC-3", "연직도"),
        ("AC-4", "자재 변형·침하"), ("AC-5", "콘크리트 면 결함"),
    };
    const string ReportStage = "8";   // evidence.py STAGES 8 결과보고
    /// <summary>침하 계측 줄 item_code 머리 (11-12 G9, 가이드라인 3.2.12①·3.3.3⑥) — SET-&lt;지점&gt;, result = 침하량 mm</summary>
    public const string Settle = "SET-";

    /// <summary>after_check.py settlements: day 까지의 침하 계측 → 구역: (지점 처음 나온 순, 날짜 → 지점 → mm 글자). 같은 날 같은 지점은 뒤 행</summary>
    public static Dictionary<string, (List<string> Points, Dictionary<string, Dictionary<string, string>> Days)> Settlements(string folder, string day)
    {
        var outp = new Dictionary<string, (List<string> Points, Dictionary<string, Dictionary<string, string>> Days)>();
        foreach (var r in Evaluator.Read(folder, File))
        {
            string code = (r.GetValueOrDefault("item_code") ?? "").Trim().ToUpperInvariant(), d = (r.GetValueOrDefault("date") ?? "").Trim();
            if (!code.StartsWith(Settle, StringComparison.Ordinal) || code.Length == Settle.Length || d.Length == 0 || string.CompareOrdinal(d, day) > 0) continue;
            var zid = (r.GetValueOrDefault("zone_id") ?? "").Trim();
            if (!outp.TryGetValue(zid, out var z)) outp[zid] = z = (new(), new());
            var pt = code[Settle.Length..];
            if (!z.Points.Contains(pt)) z.Points.Add(pt);
            if (!z.Days.TryGetValue(d, out var m)) z.Days[d] = m = new();
            m[pt] = (r.GetValueOrDefault("result") ?? "").Trim();
        }
        return outp;
    }

    /// <summary>after_check.py same_points: 날마다 처음 측정한 날과 같은 지점을 쟀는지 → (일치, 다른 날)</summary>
    public static (bool Ok, List<string> Bad) SamePoints(Dictionary<string, Dictionary<string, string>> days)
    {
        var ds = days.Keys.OrderBy(d => d, StringComparer.Ordinal).ToList();
        var first = days[ds[0]].Keys.ToHashSet();
        var bad = ds.Skip(1).Where(d => !days[d].Keys.ToHashSet().SetEquals(first)).ToList();
        return (bad.Count == 0, bad);
    }

    /// <summary>after_check.py after_status: day(yyyy-MM-dd)까지의 기록 → 구역별 상태. 항목마다 마지막 날짜 행(같은 날이면 뒤 행).
    /// Missing = OK/NG 기록이 없는 항목, Ok = 5개 전부 OK. 순서 = 파일에 처음 나온 구역 순 (Python dict)</summary>
    public static Dictionary<string, AfterStatus> Status(string folder, string day)
    {
        var acc = new Dictionary<string, (string Date, Dictionary<string, string> Items, Dictionary<string, string> D)>();
        foreach (var r in Evaluator.Read(folder, File))
        {
            string code = (r.GetValueOrDefault("item_code") ?? "").Trim().ToUpperInvariant(), d = (r.GetValueOrDefault("date") ?? "").Trim();
            if (!Items.Any(i => i.Code == code) || d.Length == 0 || string.CompareOrdinal(d, day) > 0) continue;
            var zid = (r.GetValueOrDefault("zone_id") ?? "").Trim();
            if (!acc.TryGetValue(zid, out var z)) acc[zid] = z = ("", new(), new());
            if (string.CompareOrdinal(d, z.D.GetValueOrDefault(code, "")) >= 0) { z.D[code] = d; z.Items[code] = (r.GetValueOrDefault("result") ?? "").Trim().ToUpperInvariant(); }
            acc[zid] = (string.CompareOrdinal(d, z.Date) > 0 ? d : z.Date, z.Items, z.D);
        }
        return acc.ToDictionary(kv => kv.Key, kv =>
        {
            var items = Items.Select(i => (i.Code, kv.Value.Items.GetValueOrDefault(i.Code, ""))).ToList();
            return new AfterStatus(kv.Value.Date, items, items.Where(i => i.Item2 is not ("OK" or "NG")).Select(i => i.Code).ToList(), items.All(i => i.Item2 == "OK"));
        });
    }

    /// <summary>after_check.py warnings: 원장 8 결과보고가 day 이하인 구역인데 해체 후 점검이 없거나 OK 가 아닌 항목 → (구역, "해체 후 점검 미완 — {zone}: {항목}") 구역 순</summary>
    public static List<KeyValuePair<string, string>> Warnings(string folder, string day)
    {
        // 23차 E13: 원장 7 해체 완료·8 결과보고 또는 승인 기록 '해체 완료'(사슬 정상일 때만) — after_check.py DONE_STAGES 와 같음
        var st = Status(folder, day);
        var done = Evaluator.Read(folder, "evidence.csv")
            .Where(r => (r.GetValueOrDefault("stage") ?? "").Trim() is "7" or ReportStage && string.CompareOrdinal(r.GetValueOrDefault("date") ?? "", day) <= 0)
            .Select(r => (r.GetValueOrDefault("zone_id") ?? "").Trim()).ToHashSet();
        if (Approval.Verify(folder).Ok)
            foreach (var r in Approval.ReadLog(folder))
                if (r.GetValueOrDefault("action") == "해체 완료" && string.CompareOrdinal((r.GetValueOrDefault("timestamp") ?? "")[..Math.Min(10, (r.GetValueOrDefault("timestamp") ?? "").Length)], day) <= 0)
                    done.Add((r.GetValueOrDefault("zone_id") ?? "").Trim());
        var zones = done.OrderBy(z => z, StringComparer.Ordinal);
        var outp = new List<KeyValuePair<string, string>>();
        foreach (var z in zones)
        {
            var items = st.TryGetValue(z, out var s) ? s.Items : Items.Select(i => (i.Code, "")).ToList();
            var bad = items.Where(i => i.Result != "OK").Select(i => $"{i.Code} {Items.First(x => x.Code == i.Code).Name}" + (i.Result == "NG" ? " NG" : "")).ToList();
            if (bad.Count > 0) outp.Add(new(z, $"해체 후 점검 미완 — {z}: {string.Join(", ", bad)}"));
        }
        return outp;
    }
}

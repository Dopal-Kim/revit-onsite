// shoring/quality.py 의 C# 이식 — 해체 판정(Evaluator)과 분리, 판정 결과를 바꾸지 않는다.
// F5 타설 입회: pours.csv 선택 열 supervisor_present(예/아니오)·watcher(이름). 열이 둘 다 없으면 안내 한 줄만.
// F7 28일 품질 대장: cylinders.csv 중 curing = "표준양생"·재령 28일 줄만, 1줄 = 1회 시험(공시체 3개 평균), 구역(pours.csv 한 줄) 단위. 참고용 [부분 확인] — 판정·대시보드에 안 올림.
namespace RevitOnSite.Engine;

/// <summary>quality.py quality_ledger 한 줄 — Mean·Min = 1회 평균들의 평균·최솟값 (MPa, 시험 없으면 null)</summary>
public sealed record QualityRow(string ZoneId, string PourDate, int NSpecimens, double? Mean, double? Min, string Result, string Basis);

public static class Quality
{
    // 기준 — quality.py 와 같은 값·출처 (docs/direction_review_20261104.md N4·N5·N6, field_process_research.md S12)
    const string StdCuring = "표준양생";     // [입력]
    const int AgeDays = 28;                  // [부분 확인, N5]
    const int PerTest = 3;                   // [부분 확인, S12]
    const int Run = 3;                       // [부분 확인, N5]
    const int MinSpecimens = 9;              // [확정, N5·N6]
    const double OneMargin = 3.5;            // [부분 확인, N5] Fcq ≤ 35 일 때
    const double OneLimitFcq = 35.0;         // [부분 확인, N5]
    const string Basis = "KCS 14 20 10 표 3.5-7 · 최소 9개 (연속 3회 × 3개)";

    static DateOnly Day(string? s) => DateOnly.ParseExact(s!, "yyyy-MM-dd", Py.Inv);

    /// <summary>quality.py quality_ledger (pours.csv 순서). 같은 시험일 줄은 파일 순서, 평균은 Python sum 과 같은 왼쪽부터 더하기</summary>
    public static List<QualityRow> Ledger(string folder) => Evaluator.Checked(() => LedgerCore(folder));   // quality.py @checked

    static List<QualityRow> LedgerCore(string folder)
    {
        var pours = Evaluator.Read(folder, "pours.csv");
        var pd = new Dictionary<string, DateOnly>();
        foreach (var p in pours) pd[p["zone_id"]!] = Day(p["pour_date"]);
        var tests = new Dictionary<string, List<(string Date, double[] F)>>();
        foreach (var r in Evaluator.CylRows(folder))   // W1: 타설 묶음 공통 시험 → 묶음의 구역마다
        {
            var z = r["zone_id"]!;
            if (r.GetValueOrDefault("curing") != StdCuring || !pd.ContainsKey(z)) continue;
            if (Day(r["test_date"]).DayNumber - pd[z].DayNumber != AgeDays) continue;
            if (!tests.TryGetValue(z, out var l)) tests[z] = l = new();
            l.Add((r["test_date"]!, new[] { Py.Float(r["f1"]), Py.Float(r["f2"]), Py.Float(r["f3"]) }));
        }
        static double Sum(IEnumerable<double> v) => Py.Sum(v);   // Python 3.12 sum() 은 보정 합산 — 단순 더하기면 마지막 자리가 다름
        var rows = new List<QualityRow>();
        foreach (var p in pours)
        {
            string z = p["zone_id"]!; double fcq = Py.Float(p["fck"]);
            var ts = (tests.GetValueOrDefault(z) ?? new()).OrderBy(t => t.Date, StringComparer.Ordinal).Select(t => Sum(t.F) / PerTest).ToList();
            string res, why;
            if (ts.Count == 0) (res, why) = ("시험 없음", $"표준양생 {AgeDays}일 시험 없음");
            else if (fcq > OneLimitFcq) (res, why) = ("확인 필요", $"Fcq {Py.G(fcq)} > {Py.G(OneLimitFcq)} MPa 1회 기준 [미확인]");
            else if (ts.Min() < fcq - OneMargin) (res, why) = ("불합격", $"1회 {Py.F(ts.Min(), 1)} < Fcq − {Py.G(OneMargin)} = {Py.F(fcq - OneMargin, 1)} MPa");
            else if (PerTest * ts.Count < MinSpecimens) (res, why) = ("확인 필요", $"공시체 {PerTest * ts.Count}개 < 최소 {MinSpecimens}개 (연속 {Run}회 미만)");
            else
            {
                var low = Enumerable.Range(0, ts.Count - Run + 1).Select(i => Sum(ts.Skip(i).Take(Run)) / Run).Min();
                (res, why) = low >= fcq ? ("합격", $"연속 {Run}회 평균 최저 {Py.F(low, 1)} ≥ Fcq {Py.G(fcq)}") : ("불합격", $"연속 {Run}회 평균 {Py.F(low, 1)} < Fcq {Py.G(fcq)}");
            }
            rows.Add(new(z, p["pour_date"]!, PerTest * ts.Count, ts.Count > 0 ? Sum(ts) / ts.Count : null, ts.Count > 0 ? ts.Min() : null, res, $"{why} — {Basis}"));
        }
        return rows;
    }

    static string E(string s) => Py.Html(s);

    /// <summary>quality.py quality_html: 인쇄용 표 한 장 (표 머리 "참고 — 기준 적용 [부분 확인], 품질관리자 확인용")</summary>
    public static string Html(IEnumerable<QualityRow> rows)
    {
        static string F(double? v) => v is { } x ? Py.F(x, 1) : "";
        var tr = string.Concat(rows.Select(r => $"<tr><td>{E(r.ZoneId)}</td><td>{Py.KDate(r.PourDate)}</td><td>{r.NSpecimens}</td>"
            + $"<td>{F(r.Mean)}</td><td>{F(r.Min)}</td><td><b>{r.Result}</b></td><td>{E(r.Basis)}</td></tr>"));
        return "<!doctype html><html lang='ko'><head><meta charset='utf-8'><title>품질 대장 (28일 · 참고)</title>"
            + "<style>@page{size:A4;margin:14mm}body{font:12px sans-serif}table{border-collapse:collapse}td,th{border:1px solid #000;padding:3px 6px}" + ReportStyle.Css + "</style>"
            + "</head><body><h1>품질 대장 (28일 · 참고) — 표준양생</h1><p>해체 판정과 별개인 품질검사 기록입니다. "
            + "Fcq = 타설 fck [가정]. 평균·최소 = 1회(공시체 3개 평균) 기준, MPa.</p><table><caption>참고 — 기준 적용 [부분 확인], 품질관리자 확인용</caption><tr><th>구역</th><th>타설일</th>"
            + "<th>공시체 수</th><th>평균</th><th>최소</th><th>결과</th><th>근거</th></tr>" + tr + "</table></body></html>";
    }

    public const string WatchBasis = "KCS 14 20 12 3.2(11) 타설 중 감시자 배치 [확정, N4]";
    public const string WatchHint = "pours.csv 에 supervisor_present·watcher 열을 넣으면 점검합니다";

    /// <summary>quality.py watch_missing: {구역: "타설 입회 기록 없음 — …"} (pours.csv 순서). 선택 열이 둘 다 없으면 null</summary>
    public static Dictionary<string, string>? WatchMissing(string folder)
    {
        var pours = Evaluator.Read(folder, "pours.csv");
        if (pours.Count == 0 || !(pours[0].ContainsKey("supervisor_present") || pours[0].ContainsKey("watcher"))) return null;
        var outp = new Dictionary<string, string>();
        foreach (var p in pours)
            if ((p.GetValueOrDefault("supervisor_present") ?? "").Trim() != "예" || (p.GetValueOrDefault("watcher") ?? "").Trim().Length == 0)
                outp[p["zone_id"]!] = $"타설 입회 기록 없음 — {p["zone_id"]} {p["pour_date"]} ({WatchBasis})";
        return outp;
    }

    /// <summary>quality.py pour_watch: [문구], 선택 열이 없으면 [안내 한 줄]</summary>
    public static List<string> PourWatch(string folder) => WatchMissing(folder)?.Values.ToList() ?? new() { WatchHint };
}

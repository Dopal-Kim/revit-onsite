// shoring/result_report.py 의 C# 이식 (11-12 2-6 G10, 가이드라인 3.2.13) — 해체가 끝난 구역마다 A4 한 장 해체 결과보고 HTML + 근거 원장 8 한 줄.
// 새 판단 없음: 근거 원장 단계 날짜·해체 후 점검 5항목(4.3)·침하 계측(3.2.12①·3.3.3⑥)을 옮김. 조건: 원장 7 해체 + 5항목 모두 기록(OK/NG).
// 파일 <데이터 폴더>/해체결과보고_<구역>_YYYYMMDD.html (같은 날 다시면 _2, _3 … — 원장 SHA 가 가리키는 파일을 덮지 않음). Python 과 바이트 같음.
using System.Text;

namespace RevitOnSite.Engine;

public static class ResultReport
{
    public const string Note = "이 보고서는 프로그램에 남은 기록을 옮긴 것입니다. 프로그램은 승인하지 않으며, 결과 확인은 현장 책임자와 감리(책임기술인)가 서명으로 합니다.";

    static string E(string? s) => Py.Html(s);

    /// <summary>result_report.py items: 5항목 → (코드, 이름, 결과, 점검일, 점검자) — 항목마다 day 까지 마지막 날짜 행 (같은 날이면 뒤 행)</summary>
    public static List<(string Code, string Name, string Result, string Date, string By)> Items(string folder, string zone, string day)
    {
        var last = new Dictionary<string, (string D, string R, string By)>();
        foreach (var r in Evaluator.Read(folder, AfterCheck.File))
        {
            string code = (r.GetValueOrDefault("item_code") ?? "").Trim().ToUpperInvariant(), d = (r.GetValueOrDefault("date") ?? "").Trim();
            if ((r.GetValueOrDefault("zone_id") ?? "").Trim() == zone && AfterCheck.Items.Any(i => i.Code == code) && d.Length > 0 && string.CompareOrdinal(d, day) <= 0
                && string.CompareOrdinal(d, last.TryGetValue(code, out var p) ? p.D : "") >= 0)
                last[code] = (d, (r.GetValueOrDefault("result") ?? "").Trim().ToUpperInvariant(), (r.GetValueOrDefault("by") ?? "").Trim());
        }
        return AfterCheck.Items.Select(i => last.TryGetValue(i.Code, out var x) ? (i.Code, i.Name, x.R, x.D, x.By) : (i.Code, i.Name, "", "", "")).ToList();
    }

    public static string Page(string folder, string zone, DateTime at, string project = "")
    {
        var day = at.ToString("yyyy-MM-dd", Py.Inv);
        var zr = Evaluator.Read(folder, "zones.csv").FirstOrDefault(r => r.GetValueOrDefault("zone_id") == zone);
        var pour = Evaluator.Read(folder, "pours.csv").Where(r => r.GetValueOrDefault("zone_id") == zone).Select(r => r.GetValueOrDefault("pour_date") ?? "").FirstOrDefault() ?? "";
        var L = Evidence.LedgerAt(folder, DateOnly.FromDateTime(at)).GetValueOrDefault(zone) ?? new ZoneLedger();
        var steps = string.Concat(new[] { 2, 4, 5, 6, 7 }.Select(s => $"<tr><td>{s} {Evidence.StageName(s)}</td><td>{(L.Dates.TryGetValue(s, out var d) ? Py.KDate(d) : "기록 없음")}</td></tr>"));
        var its = string.Concat(Items(folder, zone, day).Select(x => $"<tr><td>{x.Code}</td><td>{E(x.Name)}</td><td>{(x.Result.Length > 0 ? E(x.Result) : "기록 없음")}</td><td>{Py.KDate(x.Date)}</td><td>{E(x.By)}</td></tr>"));
        string st;
        if (AfterCheck.Settlements(folder, day).TryGetValue(zone, out var se))
        {
            var days = se.Days.Keys.OrderBy(d => d, StringComparer.Ordinal).ToList();
            st = "<table><tr><th>측정일</th>" + string.Concat(se.Points.Select(p => $"<th>{E(p)} (mm)</th>")) + "</tr>"
                + string.Concat(days.Select(d => $"<tr><td>{Py.KDate(d)}</td>" + string.Concat(se.Points.Select(p => $"<td>{E(se.Days[d].GetValueOrDefault(p, "—"))}</td>")) + "</tr>")) + "</table>";
            var (ok, bad) = AfterCheck.SamePoints(se.Days);
            var first = se.Days[days[0]].Keys.OrderBy(p => p, StringComparer.Ordinal);
            st += $"<p>측정 위치 일치: <b>{(ok ? "예" : "아니오")}</b> — 처음 측정({Py.KDate(days[0])}) 지점 {E(string.Join(", ", first))}"
                + (ok ? "" : $" · 다른 지점을 잰 날 {string.Join(", ", bad.Select(d => Py.KDate(d)))}") + "</p>";
        }
        else st = "<p>침하 계측 기록 없음 (해체 후 점검 화면에서 지점·침하량 mm 를 넣습니다)</p>";
        var wl = string.Concat(L.Warnings.Where(w => w.Contains(zone, StringComparison.Ordinal)).Select(w => $"<li>{E(w)}</li>"));
        if (wl.Length == 0) wl = "<li>없음</li>";
        return "<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>해체 결과보고 " + E(zone) + " " + day + "</title>\n"
            + "<style>@page{size:A4;margin:12mm}body{font:10pt 'Malgun Gothic',sans-serif;color:#111;background:#fff}h1{font-size:16pt;margin:0 0 4px}h2{font-size:12pt;margin:8px 0 3px}\n"
            + "table{border-collapse:collapse;width:100%;margin:6px 0}th,td{border:1px solid #888;padding:4px 6px;text-align:left;vertical-align:top}th{background:#eee}\n"
            + ".sign{break-inside:avoid}.sign td{height:40px}.note{color:#444;font-size:9.5pt}ul{margin:3px 0;padding-left:18px}" + ReportStyle.Css + "</style></head><body>\n"
            + "<h1>동바리 해체 결과보고</h1>\n"
            + $"<div>{E(project)} · 구역 {E(zone)} ({E(zr?.GetValueOrDefault("floor") ?? "")}층) · 타설 {Py.KDate(pour)} · 작성 기준일 {Py.KDate(day)} · 작성 Revit On-Site</div>\n"
            + "<h2>1. 해체 경과 (근거 원장)</h2>\n"
            + $"<table><tr><th>단계</th><th>날짜</th></tr>{steps}</table>\n"
            + "<h2>2. 해체 후 점검 (가이드라인 4.3)</h2>\n"
            + $"<table><tr><th>코드</th><th>항목</th><th>결과</th><th>점검일</th><th>점검자</th></tr>{its}</table>\n"
            + "<h2>3. 침하 계측 (가이드라인 3.2.12① · 3.3.3⑥)</h2>\n"
            + st + "\n"
            + "<h2>4. 확인할 점</h2>\n"
            + $"<ul>{wl}</ul>\n"
            + "<table class=\"sign\"><tr><th>구분</th><th>소속</th><th>성명</th><th>서명</th><th>일시</th></tr>\n"
            + "<tr><td>작성 (현장 책임자)</td><td>" + E(Evaluator.TextOption(folder, "contractor")) + "</td><td></td><td></td><td></td></tr>\n"
            + "<tr><td>확인 (감리)</td><td></td><td></td><td></td><td></td></tr></table>\n"
            + $"<p class=\"note\">{E(Note)} 결과보고와 다음 해체 계획 반영은 가이드라인 3.2.13.</p>\n"
            + "</body></html>";
    }

    /// <summary>만들 수 있는지 (result_report.py make 의 검사와 같음) → null = 가능, 아니면 사유</summary>
    public static string? WhyNot(string folder, string zone, DateTime at)
    {
        var day = at.ToString("yyyy-MM-dd", Py.Inv);
        if (!(Evidence.LedgerAt(folder, DateOnly.FromDateTime(at)).GetValueOrDefault(zone)?.Dates.ContainsKey(7) ?? false))
            return $"{zone}: 해체(원장 7) 기록이 없어 결과보고를 만들지 않음";
        var miss = AfterCheck.Status(folder, day).TryGetValue(zone, out var st) ? st.Missing : AfterCheck.Items.Select(i => i.Code).ToList();
        return miss.Count > 0 ? $"{zone}: 해체 후 점검 기록 없는 항목 {string.Join(", ", miss)} — 5항목을 넣은 뒤 만듭니다" : null;
    }

    /// <summary>result_report.py make: 검사 → 보고서 파일 → 근거 원장 8 한 줄 (파일 SHA-256). → 파일 경로. 조건이 안 되면 InvalidOperationException</summary>
    public static string Make(string folder, string zone, DateTime at, string by, string project = "")
    {
        if (string.IsNullOrWhiteSpace(by)) throw new InvalidOperationException("입력자가 필요합니다");
        if (WhyNot(folder, zone, at) is string why) throw new InvalidOperationException(why);
        var b = Path.Combine(Path.GetFullPath(folder), $"해체결과보고_{zone}_{at.ToString("yyyyMMdd", Py.Inv)}");
        string path = b + ".html";
        for (int k = 2; File.Exists(path); k++) path = $"{b}_{k}.html";
        File.WriteAllText(path, Page(folder, zone, at, project), new UTF8Encoding(false));
        Evidence.Add(folder, zone, at.ToString("yyyy-MM-dd", Py.Inv), 8, by, $"RR-{zone}-{at.ToString("yyyyMMdd", Py.Inv)}", "해체 결과보고", path);
        return path;
    }
}

// shoring/package.py 의 C# 이식 (방향성 5판 F11) — 검측 패키지: 감리에게 '한 묶음'으로 넘기는 폴더. 새 계산 없음(기존 결과물 모음).
// <데이터 폴더>/검측패키지_YYYYMMDD/ (같은 날 다시 만들면 지우고 새로 — 쌓지 않음):
//   검측요청서.html · 슬래브이력서.html(요청 가능 구역만) · 감사기록.csv · 근거원장.csv(요청 구역, 층 순) · 첨부/(근거 원장 file 칸 사본 {구역}_{단계}_{이름})
//   · RevitOnSite_YYYYMMDD.ifc 사본(있으면) · 목록.csv(file, bytes, sha256 — 목록·index 자신은 빼고) · index.html
using System.Security.Cryptography;
using System.Text;

namespace RevitOnSite.Engine;

public static class Package
{
    public const string Note = "이 묶음은 감리 검측요청에 붙이는 근거 자료입니다. 프로그램은 승인하지 않으며, 해체 여부는 감리(책임기술인)가 확인·서명으로 결정합니다.";

    static string Sha(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }

    static string E(string s) => Py.Html(s);

    /// <summary>package.py _table_html: 근거원장.html·감사기록.html (11-12 2-6 Revit #20) — 엑셀 없이 읽는 표, 요청 구역 줄만 (CSV 는 그대로)</summary>
    static string TableHtml(string title, string project, DateTime at, List<string> req, string[] head, List<string[]> rows) =>
        $"<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>{E(title)} {E(project)} {at.ToString("yyyy-MM-dd", Py.Inv)}</title>\n"
        + "<style>@page{size:A4 landscape;margin:12mm}body{font:9pt 'Malgun Gothic',sans-serif;color:#111;margin:16px}table{border-collapse:collapse;width:100%}"
        + "th,td{border:1px solid #999;padding:3px 5px;text-align:left;vertical-align:top;word-break:break-all}th{background:#eee}tr{break-inside:avoid}" + ReportStyle.Css + "</style></head><body>\n"
        + $"<h1>{E(title)}</h1><p>{E(project)} · 판정 기준 {Py.KStamp(at)} · 요청 구역 {req.Count}: {E(string.Join(", ", req))} · 줄 {rows.Count}</p>\n"
        + "<table><thead><tr>" + string.Concat(head.Select(h => $"<th>{E(h)}</th>")) + "</tr></thead><tbody>"
        + string.Concat(rows.Select(r => "<tr>" + string.Concat(r.Select(c => $"<td>{E(c)}</td>")) + "</tr>")) + "</tbody></table></body></html>";

    /// <summary>package.py build →(패키지 폴더, [(상대 경로, 바이트, sha256)] 경로 순, 요청 구역). 요청 가능 구역이 없으면 InvalidDataException</summary>
    public static (string Out, List<(string Name, long Bytes, string Sha)> Files, List<string> Req) Build(string folder, DateTime at, string? project = null, double cureMin = 5.0)
    {
        project ??= Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)));
        var rows = Evaluator.Evaluate(folder, at, cureMin);
        var req = Approval.RequestRows(rows, folder, at).Where(r => r.Decision == "해체 승인 요청 가능").Select(r => r.ZoneId).ToList();   // 이미 기록(6·7·8)된 구역 뺌
        if (req.Count == 0) throw new InvalidDataException("해체 승인 요청 가능 구역이 없어 검측 패키지를 만들지 않음");
        var ymd = at.ToString("yyyyMMdd", Py.Inv);
        var outDir = Path.Combine(folder, $"검측패키지_{ymd}");
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);   // 같은 날 다시 만들면 바꿔 씀 (쌓지 않기)
        Directory.CreateDirectory(Path.Combine(outDir, "첨부"));
        void W(string name, string text) => File.WriteAllText(Path.Combine(outDir, name), text, new UTF8Encoding(false));
        W("검측요청서.html", Approval.RequestHtml(rows, project, at, folder, Evaluator.GateDetails(folder, at, cureMin)));
        W("슬래브이력서.html", SlabRecord.Page(SlabRecord.Records(folder, at, cureMin: cureMin).Where(r => req.Contains((string)r["zone_id"]!)).ToList(), project, at));
        var audit = AuditExport.Rows(folder);
        AuditExport.WriteCsv(audit, Path.Combine(outDir, "감사기록.csv"));
        W("감사기록.html", TableHtml("감사 기록 (요청 구역)", project, at, req, new[] { "시각", "출처", "구역", "행위", "입력자", "내용", "줄 SHA" },
            audit.Where(r => req.Contains(r.ZoneId)).Select(r => new[] { r.When, r.Source, r.ZoneId, r.Action, r.Actor, r.Detail, r.LineSha }).ToList()));

        // 근거 원장 (evidence.entries 와 같은 줄: 원장 줄은 doc_no·file·sha256 까지, 나머지 출처는 빈칸) — 요청 구역·판정일까지, 층 순 → 날짜 → 단계
        string V(Dictionary<string, string?> r, string k) => r.GetValueOrDefault(k) ?? "";
        var led = Evaluator.Read(folder, Evidence.Ledger).Select(r => (Zone: V(r, "zone_id"), Date: V(r, "date"), Stage: Py.Int(r["stage"]), Source: Evidence.Ledger,
                By: V(r, "by"), Doc: V(r, "doc_no"), Result: V(r, "result"), File: V(r, "file"), Sha: V(r, "sha256")))
            .Concat(Evidence.Entries(folder).Where(x => x.Source != Evidence.Ledger).Select(x => (Zone: x.ZoneId, x.Date, x.Stage, x.Source, x.By, Doc: "", x.Result, File: "", x.Sha)))
            .Where(x => req.Contains(x.Zone) && string.CompareOrdinal(x.Date.Length > 10 ? x.Date[..10] : x.Date, at.ToString("yyyy-MM-dd", Py.Inv)) <= 0)
            .OrderBy(x => x.Zone, Evaluator.FloorOrder(folder)).ThenBy(x => x.Date, StringComparer.Ordinal).ThenBy(x => x.Stage).ToList();
        var sb = new StringBuilder(Py.CsvLine(new[] { "zone_id", "date", "stage", "source", "by", "doc_no", "result", "file", "sha256" }));
        var hrows = new List<string[]>();
        foreach (var x in led)
        {
            var src = x.File.Trim();
            string name = "", chk = "";
            var p = src.Length == 0 ? "" : Path.IsPathRooted(src) ? src : Path.Combine(folder, src);
            if (p.Length > 0 && File.Exists(p))
            {
                name = $"첨부/{x.Zone}_{x.Stage.ToString(Py.Inv)}_{Path.GetFileName(p)}";
                var dst = Path.Combine(outDir, name.Replace('/', Path.DirectorySeparatorChar));
                File.Copy(p, dst, true);
                // 원장에 적은 SHA-256 과 넣은 사본 대조 (package.py 와 같음) — 원장 기록 뒤 파일이 바뀌면 드러남
                chk = x.Sha.Length > 0 ? (Sha(dst) == x.Sha ? "일치" : "다름 — 원장 기록 뒤 파일이 바뀜") : "원장에 해시 없음";
            }
            else if (src.Length > 0) chk = "파일 없음";
            sb.Append(Py.CsvLine(new[] { x.Zone, x.Date, x.Stage.ToString(Py.Inv), x.Source, x.By, x.Doc, x.Result, name, x.Sha }));
            hrows.Add(new[] { x.Zone, x.Date, $"{x.Stage.ToString(Py.Inv)} {Evidence.StageName(x.Stage)}", x.Source, x.By, x.Doc, x.Result, name, chk });
        }
        File.WriteAllText(Path.Combine(outDir, "근거원장.csv"), sb.ToString(), new UTF8Encoding(true));
        W("근거원장.html", TableHtml("근거 원장 (요청 구역)", project, at, req, new[] { "구역", "날짜", "단계", "출처", "입력자", "문서 번호", "결과", "첨부", "첨부 해시 대조" }, hrows));
        var day = at.ToString("yyyy-MM-dd", Py.Inv);
        var reqSet = req.ToHashSet();
        var need = req.SelectMany(z => Approval.CheckTargets(folder, z)).ToHashSet();   // 요청서 현장 확인 ① 대상 (위 3개층·평면 겹침)
        need.ExceptWith(reqSet);
        var ph = Photos.Rows(folder, reqSet, day).Concat(Photos.Rows(folder, need, day)   // + ① 대상의 최근 24 h 동바리 확인 사진 (총괄 29차, package.py 와 같음)
            .Where(x => x.Kind is "동바리확인" or "해체신고" && (at - Evaluator.Ts(x.When)).TotalSeconds / 3600 is >= 0 and <= Approval.CheckFreshH));
        foreach (var x in ph)   // 요청 구역 현장 사진 사본 (F15) — 원본 그대로, 이름 = 서버가 정한 이름
        {
            var p = Path.Combine(new[] { folder }.Concat(x.Path.Split('/')).ToArray());
            if (!File.Exists(p)) continue;
            Directory.CreateDirectory(Path.Combine(outDir, "첨부", "사진"));
            File.Copy(p, Path.Combine(outDir, "첨부", "사진", Path.GetFileName(p)), true);
        }
        var ifc = Path.Combine(folder, $"RevitOnSite_{ymd}.ifc");
        if (File.Exists(ifc)) File.Copy(ifc, Path.Combine(outDir, Path.GetFileName(ifc)), true);

        var files = Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories)
            .Select(f => (Name: Path.GetRelativePath(outDir, f).Replace(Path.DirectorySeparatorChar, '/'), Bytes: new FileInfo(f).Length, Sha: Sha(f)))
            .OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
        Py.WriteCsv(Path.Combine(outDir, "목록.csv"), new[] { new[] { "file", "bytes", "sha256" } }
            .Concat(files.Select(f => new[] { f.Name, f.Bytes.ToString(Py.Inv), f.Sha })));
        var lst = string.Concat(files.Select(f => $"<tr><td><a href=\"{E(f.Name)}\">{E(f.Name)}</a></td><td>{f.Bytes.ToString("N0", Py.Inv)}</td><td><small>{f.Sha}</small></td></tr>"));
        W("index.html", $"<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>검측 패키지 {E(project)} {at.ToString("yyyy-MM-dd", Py.Inv)}</title>\n"
            + "<style>@page{size:A4;margin:14mm}body{font:11pt 'Malgun Gothic',sans-serif;color:#111;margin:16px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #999;padding:4px 6px;text-align:left}th{background:#eee}.note{color:#444;font-size:10pt}" + ReportStyle.Css + "</style></head><body>\n"
            + $"<h1>검측 패키지</h1><p>{E(project)} · 판정 기준 {Py.KStamp(at)} · 요청 구역 {req.Count}: {E(string.Join(", ", req))}</p>\n"
            + $"<p class=\"note\">{E(Note)} 아래 파일마다 SHA-256 — 받은 파일을 다시 계산해 같으면 보낸 그대로입니다. 목록은 목록.csv 와 같습니다.</p>\n"
            + $"<table><tr><th>파일</th><th>바이트</th><th>SHA-256</th></tr>{lst}</table></body></html>");
        return (outDir, files, req);
    }
}

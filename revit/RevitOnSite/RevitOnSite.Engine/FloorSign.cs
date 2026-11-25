// [층 표지] (현장 조사 제안 2) — 마지막 판정 기준으로 층마다 A4 한 장 HTML (데이터 폴더 층표지_YYYYMMDD.html, 한 파일·층마다 쪽 나눔).
// 기존 결과만 옮긴다 (판정 행 · approvals.csv 승인 기록 · 근거 원장 · shore_checks.csv 동바리 확인) — 새 계산 없음, Python 기준본 없음.
// 프로그램은 해체를 지시하지 않는다: 층 상태 줄은 "동바리 해체 금지 — …" / "해체 금지 — 감리 확인 전 (해체 승인 요청 가능)" / "감리 확인 기록 있음 — …" / "해체 완료 기록 있음 — …" 까지.
using System.Globalization;
using System.Text;

namespace RevitOnSite.Engine;

public static class FloorSign
{
    const string Ready = "해체 승인 요청 가능";
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public sealed record ZoneLine(string ZoneId, string Conclusion, string Due, int Evidence, string Shore, int Status = 0);   // Status = 휴대폰·서버와 같은 상태 코드 (W10 QR pz)
    /// <summary>[가정] 층 표지 유효 시간 (시간) — options.json sign_valid_hours (v6.2 W10)</summary>
    public const double SignValidH = 24;
    /// <summary>Kind: suspect(무단 해체 의심 — 진한 빨강·맨 위 굵은 줄, 60차) · hold(해체 금지) · ready(요청 가능, 감리 확인 전) · checked(감리 확인 기록·해체 완료 기록)</summary>
    public sealed record FloorLine(int Floor, string Name, string Status, string Kind, List<ZoneLine> Zones);

    /// <summary>층 이름 = 그 층 첫 구역 ID 의 '-' 앞 (B1·PH), 없으면 음수 "B{-fl}" 그 밖 "{fl}F" — field_server.py floor_name 과 같은 규칙</summary>
    public static string FloorName(int fl, string? firstZoneId)
    {
        var head = firstZoneId != null && firstZoneId.Contains('-') ? firstZoneId.Split('-')[0] : "";
        return head.Length > 0 ? head : fl < 0 ? "B" + (-fl).ToString(Inv) : fl.ToString(Inv) + "F";
    }

    static string Md(string ts) => ts.Length >= 10 ? ts[5..10] : ts;   // "2026-12-31T…" → "12-31"

    /// <summary>zones.csv 의 층마다 (낮은 층부터) 구역 줄과 상태 줄. rows = 판정 시각 at 의 Evaluator.Evaluate 결과</summary>
    public static List<FloorLine> Floors(string folder, IReadOnlyList<ZoneRow> rows, DateTime at)
    {
        var zones = new List<(string Id, int Floor)>();
        var seen = new HashSet<string>();
        foreach (var r in Evaluator.Read(folder, "zones.csv"))
            if (r.GetValueOrDefault("zone_id") is { Length: > 0 } z && seen.Add(z)) zones.Add((z, Py.Int(r["floor"])));
        var dec = rows.GroupBy(r => r.ZoneId).ToDictionary(g => g.Key, g => g.Last());
        var appr = Approval.StatusEvents(folder, at);   // 구역별 마지막 상태 사건 + 시각 (승인 기록 + 협력업체 해체 신고 '뺐음' = 7, 66차) — 판정일까지
        var led = Evidence.LedgerAt(folder, DateOnly.FromDateTime(at));
        var watch = Evidence.RemovalWatch(folder, DateOnly.FromDateTime(at));   // 무단 해체 관측 (총괄 P1-2) — 그 층 상태 줄을 빨강 "무단 해체 의심 — 확인 필요"
        var shore = new Dictionary<string, (DateTime T, string Present, string Actor)>();   // 판정 시각까지의 마지막 동바리 확인 (PourGate 와 같은 규칙)
        foreach (var r in Evaluator.Read(folder, PourGate.ChecksFile))
        {
            var t = Evaluator.Ts(r["timestamp"]);
            var z = r.GetValueOrDefault("zone_id") ?? "";
            if (t <= at && (!shore.TryGetValue(z, out var p) || t >= p.T))
                shore[z] = (t, (r.GetValueOrDefault("present") ?? "").Trim(), (r.GetValueOrDefault("actor") ?? "").Trim());
        }
        var outp = new List<FloorLine>();
        foreach (var g in zones.GroupBy(z => z.Floor).OrderBy(g => g.Key))
        {
            var lines = new List<ZoneLine>();
            int keep = 0, cyl = 0, pre = 0, re = 0, ok6 = 0, done7 = 0;
            foreach (var (id, _) in g)
            {
                dec.TryGetValue(id, out var d);
                bool has = appr.TryGetValue(id, out var a);
                string concl = has && a.Code == 6 ? $"감리 확인 기록 있음 ({Md(a.Ts)})"
                    : has && a.Code == 7 ? $"해체 완료 기록 ({Md(a.Ts)})"
                    : has && a.Code == 8 ? $"재받침 기록 ({Md(a.Ts)})"
                    : d == null ? "타설 전" : d.Decision;
                if (has && a.Code == 6) ok6++;
                else if (has && a.Code == 7) done7++;
                else if (has && a.Code == 8) re++;
                else if (d == null) pre++;
                else if (d.Decision.StartsWith("존치", StringComparison.Ordinal)) keep++;
                else if (d.Decision != Ready) cyl++;   // 공시체 시험 필요 등 — 요청 가능이 아니면 해체 금지 쪽
                string sh = shore.TryGetValue(id, out var c) ? $"{c.T.ToString("MM-dd HH:mm", Inv)} {c.Present}" + (c.Actor.Length > 0 ? $" ({c.Actor})" : "") : "—";
                int code = has ? a.Code : d == null ? 0 : FieldServer.StatusOf(d.Decision, at, d.PredictedDue.ToString("yyyy-MM-dd", Inv));   // FieldServer.Model 과 같은 규칙
                lines.Add(new ZoneLine(id, concl, d == null ? "—" : d.PredictedDue.ToString("yyyy-MM-dd", Inv), led.TryGetValue(id, out var l) ? l.Stage : 0, sh, code));
            }
            var hold = new List<string>();
            if (keep > 0) hold.Add($"존치 {keep}구역");
            if (cyl > 0) hold.Add($"공시체 시험 필요 {cyl}구역");
            if (pre > 0) hold.Add($"타설 전 {pre}구역");
            if (re > 0) hold.Add($"재받침 {re}구역");
            int n = lines.Count;
            var sus = g.Select(z => z.Id).Where(watch.ContainsKey).ToList();
            var (status, kind) = sus.Count > 0 ? ("무단 해체 의심 — 확인 필요: " + string.Join(", ", sus), "suspect")
                : hold.Count > 0 ? ("동바리 해체 금지 — " + string.Join(" · ", hold), "hold")
                : done7 == n ? ($"해체 완료 기록 있음 — {n}구역", "checked")
                : ok6 + done7 == n ? ($"감리 확인 기록 있음 — {ok6}구역" + (done7 > 0 ? $" · 해체 완료 기록 {done7}구역" : ""), "checked")
                : ("해체 금지 — 감리 확인 전 (해체 승인 요청 가능)", "ready");
            outp.Add(new FloorLine(g.Key, FloorName(g.Key, g.First().Id), status, kind, lines));
        }
        return outp;
    }

    /// <summary>11-12 2-6 Revit #21: 층 표지 기본 = 동바리가 서 있는 층만 — 무단 해체 의심 층, 또는 타설됐고 해체 완료 기록(7)이 아닌 구역이 하나라도 있는 층 (재받침 8·감리 확인 6 은 서 있음)</summary>
    public static List<FloorLine> Standing(IEnumerable<FloorLine> floors) =>
        floors.Where(f => f.Kind == "suspect" || f.Zones.Any(z => z.Conclusion != "타설 전" && z.Status != 7)).ToList();

    /// <summary>층 → 그 층으로 바로 가는 휴대폰 주소 (QR). 현장 입력 서버가 켜져 있고 같은 데이터 폴더일 때만 — 아니면 늘 null.
    /// 주소 = 서버 PrimaryUrl + "#floor=층 번호" (zones.csv floor, B1 = -1; 웹 field/index.html 이 이 해시로 층을 고름)</summary>
    public static Func<int, string?> PhoneUrl(FieldServer? server, string folder)
    {
        if (server is not { Running: true } || !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(server.Folder)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), StringComparison.OrdinalIgnoreCase))
            return _ => null;
        return f => server.ViewUrl(f);   // 읽기 전용 층 화면 (F12) — 입력 주소는 현장 입력 창 QR 만
    }

    /// <summary>[서버 연동]이 켜져 있을 때: 층 → Vercel 서버의 읽기 전용 층 화면 주소 (FieldServer.CloudViewUrl). 주소·보기 토큰이 없으면 늘 null</summary>
    public static Func<int, string?> CloudPhoneUrl(string? baseUrl, string? viewToken) =>
        string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(viewToken) ? _ => null : f => FieldServer.CloudViewUrl(baseUrl, viewToken, f);

    /// <summary>W10: 층 보기 주소에 인쇄 시각 ps=YYYYMMDDHHmm · 그 층 구역 상태 코드 pz (구역 순서 = zones.csv) — '#' 앞에. 서버 층 화면이 '표지 인쇄 후 n시간 · 표지와 지금 상태 다른 구역 m개' 띠</summary>
    public static string WithPrint(string url, DateTime printed, IEnumerable<int> codes)
    {
        int h = url.IndexOf('#');
        string head = h >= 0 ? url[..h] : url, tail = h >= 0 ? url[h..] : "";
        return head + (head.Contains('?') ? "&" : "?") + "ps=" + printed.ToString("yyyyMMddHHmm", Inv) + "&pz=" + string.Concat(codes.Select(c => c.ToString(Inv))) + tail;
    }

    static string E(string? s) => Py.Html(s);

    /// <summary>QR (Engine.Qr, 모듈 [y, x]) → SVG 사각형 (둘레 여백 4칸)</summary>
    public static string QrSvg(string text)
    {
        var m = Qr.Encode(text);
        int n = m.GetLength(0), w = n + 8;
        var d = new StringBuilder();
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                if (m[y, x]) d.Append($"M{x + 4},{y + 4}h1v1h-1z");
        return $"<svg viewBox=\"0 0 {w} {w}\" xmlns=\"http://www.w3.org/2000/svg\" shape-rendering=\"crispEdges\" role=\"img\" aria-label=\"층 보기 QR (읽기 전용)\"><rect width=\"{w}\" height=\"{w}\" fill=\"#fff\"/><path d=\"{d}\" fill=\"#000\"/></svg>";
    }

    /// <summary>층 표지 HTML. phoneUrl(층 번호) = 그 층으로 바로 가는 현장 입력 주소 (현장 입력이 꺼져 있으면 null), seal = 마감 값 8자 (요청 가능 구역이 없으면 null)</summary>
    /// printedAt (W10) = 인쇄 시각 — 주면 머리에 크게 + 유효 시간 안내, QR 주소에 ps·pz. validHours = options.json sign_valid_hours (기본 24 [가정])
    public static string Page(string project, DateTime at, IReadOnlyList<FloorLine> floors, Func<int, string?> phoneUrl, string? seal, DateTime? printedAt = null, double validHours = SignValidH)
    {
        string stamp = Py.KStamp(at);   // 인쇄 표기 "2026. 12. 31. 09:00" (행정업무규정 제7조, ui_reference_research 7절 #22)
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
          .Append($"<title>층 표지 {E(project)} {at.ToString("yyyy-MM-dd", Inv)}</title>\n<style>")
          .Append("@page{size:A4;margin:12mm}body{font-family:'Malgun Gothic',sans-serif;color:#111;margin:0;background:#fff}")
          .Append("section{box-sizing:border-box;min-height:270mm;padding:4mm 2mm;display:flex;flex-direction:column;break-after:page;page-break-after:always}")
          .Append("section:last-of-type{break-after:auto;page-break-after:auto}")
          .Append(".top{font-size:10pt;color:#444}.name{font-size:120pt;font-weight:800;line-height:1.05;margin:4mm 0}")
          .Append(".status{font-size:24pt;font-weight:700;padding:4mm 6mm;border:3px solid;border-radius:3mm}")
          .Append("*{-webkit-print-color-adjust:exact;print-color-adjust:exact}.alarm{font-size:30pt;font-weight:900;color:#fff;background:#5c0000;padding:3mm 6mm;margin-bottom:3mm}.suspect{color:#fff;border-color:#5c0000;background:#7a0000}")
          .Append(".hold{color:#a40000;border-color:#a40000;background:#fdeaea}.ready{color:#7a4f00;border-color:#b07a00;background:#fff4dc}.checked{color:#1d5c2e;border-color:#1d5c2e;background:#e8f4ec}")
          .Append("table{border-collapse:collapse;width:100%;margin-top:6mm;font-size:12.5pt}th,td{border:1px solid #888;padding:2mm 3mm;text-align:left;vertical-align:top}th{background:#eee}td:first-child{white-space:nowrap}")   // 11-12: 9F-A1 같은 구역 ID 가 줄바꿈되던 것
          .Append(".foot{margin-top:auto;padding-top:6mm;display:flex;gap:6mm;align-items:flex-end;font-size:10pt;color:#333}.qr svg{width:40mm;height:40mm;display:block}.qr p{margin:1mm 0 0;font-size:9pt;word-break:break-all;max-width:60mm}.qr p.url{font:7pt monospace;color:#555}")
          .Append(".note{font-size:9.5pt;color:#444;margin:1mm 0}.printed{font-size:20pt;font-weight:800;color:#111;border-bottom:2px solid #111;padding:1mm 0}.printed small{font-size:11pt;font-weight:600;color:#444;display:block}" + ReportStyle.Css + "</style></head><body>\n");
        foreach (var f in floors)
        {
            sb.Append("<section>")
              .Append(f.Kind == "suspect" ? "<div class=\"alarm\">무단 해체 의심 — 확인 필요</div>" : "")   // 60차: 존치 층(연한 빨강)과 구별되게 맨 위 굵은 줄
              .Append(printedAt is DateTime pa ? $"<div class=\"printed\">인쇄 {Py.KStamp(pa)}<small>인쇄 후 {Py.G(validHours)}시간 지나면 QR 화면으로 현재 상태를 확인하세요 (표지는 인쇄 때 기록)</small></div>" : "")   // W10
              .Append($"<div class=\"top\">{E(project)} · 동바리 층 표지 · 판정 기준 시각 {stamp}</div>")
              .Append($"<div class=\"name\">{E(f.Name)}</div>")
              .Append($"<div class=\"status {f.Kind}\">{E(f.Status)}</div>")
              .Append($"<p class=\"note\">{E(Approval.DuringNote)}</p>")   // 해체 중 안내 (총괄 P1, 요청서와 같은 문구)
              .Append("<table><tr><th>구역</th><th>결론</th><th>예측 해체 가능일</th><th>근거 원장</th><th>최근 동바리 확인</th></tr>");
            foreach (var z in f.Zones)
                sb.Append($"<tr><td>{E(z.ZoneId)}</td><td>{E(z.Conclusion)}</td><td>{E(Py.KDate(z.Due))}</td><td>{z.Evidence}/8</td><td>{E(z.Shore)}</td></tr>");
            sb.Append("</table><div class=\"foot\"><div class=\"qr\">");
            var url = phoneUrl(f.Floor);
            if (url != null && printedAt is DateTime pq) url = WithPrint(url, pq, f.Zones.Select(z => z.Status));   // W10 인쇄 시각·상태 → 서버 띠
            if (url != null)
                sb.Append(QrSvg(url)).Append($"<p>휴대폰으로 층 보기 (읽기 전용) — {E(f.Name)}</p><p class=\"url\">{E(url)}</p>");   // QR 이 안 찍힐 때 손으로 입력·점검 때 #floor=N 확인
            else
                sb.Append("<p>현장 입력을 켜면 QR 이 나옵니다 (타설·데이터 › 현장 입력).</p>");
            sb.Append("</div><div>")
              .Append($"<p class=\"note\">판정 기준 시각 {stamp} · 작성 Revit On-Site</p>")
              .Append(seal != null ? $"<p class=\"note\">마감 값 <b>{E(seal)}</b> (해체 검측요청서와 같은 값 — 요청 가능 구역의 판정 근거·근거 원장·승인 기록)</p>" : "")
              .Append("<p class=\"note\">이 표지는 프로그램 판정 기록을 옮긴 것입니다. 해체 여부는 감리(책임기술인) 확인을 거쳐 현장 책임자가 정합니다.</p>")
              .Append("</div></div></section>\n");
        }
        sb.Append("</body></html>");
        return sb.ToString();
    }
}

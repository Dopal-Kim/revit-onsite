// shoring/provenance.py 의 1:1 C# 이식 (11-12) — 구역 서류의 '기본 사항·출처': 자동으로 넣는 값마다 누가(역할)·언제·어떤 경로로 넣었는지.
// 계산·판정 없음: 데이터 폴더 기록 그대로. 글자는 provenance.py 와 같다 (Engine.Check 가 검토서 HTML 바이트로 비교).
using System.Globalization;
using Row = System.Collections.Generic.Dictionary<string, string?>;

namespace RevitOnSite.Engine;

public static class Provenance
{
    public static readonly string[] Head = { "항목", "값", "입력 — 누가 (역할) · 언제 · 경로" };   // 한 장 검토서: 출처 세 칸을 한 칸으로
    public const string Note = "자동 입력 — 데이터 폴더 기록 그대로 (계산 없음). 입력자 '—' = 이름 없이 들어온 기록(옛 기록·시연 자료) — 원본 서류로 확인.";
    public const string Css = ".prov th,.prov td{font-size:8pt;padding:1px 4px}.prov td:first-child{white-space:nowrap}";

    static string V(IReadOnlyDictionary<string, string?> r, string k) => (r.GetValueOrDefault(k) ?? "").Trim();
    static string Raw(IReadOnlyDictionary<string, string?> r, string k) => r.GetValueOrDefault(k) ?? "";
    static string When(string? s) { var t = (s ?? "").Trim().Replace("T", " "); return t.Length > 16 ? t[..16] : t; }

    /// <summary>provenance.who_fn: 이름 → '이름 (역할)' (역할 없으면 이름, 빈칸 그대로) — options.json actor_roles</summary>
    public static Func<string?, string> WhoFn(string folder)
    {
        var roles = Evaluator.Roles(folder);
        return n => (n ?? "").Trim() is var s && roles.TryGetValue(s, out var r) ? $"{s} ({r})" : (n ?? "").Trim();
    }

    /// <summary>provenance.cyl_meta: (구역 또는 'lot:'+타설 묶음, 시험일) → (입력자 (역할), 입력 시각 YYYY-MM-DDTHH:MM), 같은 키는 뒤 줄 — 스냅숏 cyl_entered_*</summary>
    public static Dictionary<(string, string), (string By, string At)> CylMeta(string folder)
    {
        var who = WhoFn(folder);
        var d = new Dictionary<(string, string), (string, string)>();
        foreach (var c in Evaluator.Read(folder, "cylinders.csv"))
        {
            var at = V(c, "entered_at");
            d[(V(c, "zone_id").Length > 0 ? V(c, "zone_id") : "lot:" + V(c, "pour_id"), Raw(c, "test_date"))] = (who(c.GetValueOrDefault("entered_by")), at.Length > 16 ? at[..16] : at);
        }
        return d;
    }

    /// <summary>provenance.evidence_meta: {구역: {"단계": {"by", "at"}}} 근거 원장(evidence.csv) 단계마다 day 까지 마지막 줄 — 스냅숏 check.evidence_meta</summary>
    public static System.Text.Json.Nodes.JsonObject EvidenceMeta(string folder, string day)
    {
        var who = WhoFn(folder);
        var outp = new System.Text.Json.Nodes.JsonObject();
        foreach (var r in Evaluator.Read(folder, Evidence.Ledger))
            if (string.CompareOrdinal(Raw(r, "date"), day) <= 0)
            {
                var z = Raw(r, "zone_id");
                var o = outp[z] as System.Text.Json.Nodes.JsonObject ?? (System.Text.Json.Nodes.JsonObject)(outp[z] = new System.Text.Json.Nodes.JsonObject())!;
                var st = (int.TryParse(V(r, "stage"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0).ToString(CultureInfo.InvariantCulture);
                o[st] = new System.Text.Json.Nodes.JsonObject { ["by"] = who(r.GetValueOrDefault("by")), ["at"] = Raw(r, "date") };
            }
        return outp;
    }

    /// <summary>provenance.via: Revit 창(client_id revit-) · 현장 입력(기기) · 파일 기록(직접·시연 자료)</summary>
    public static string Via(IReadOnlyDictionary<string, string?> r)
    {
        string cid = V(r, "client_id"), dev = V(r, "device");
        if (cid.StartsWith("revit-", StringComparison.Ordinal)) return "Revit 창";
        if (dev.Length > 0) return $"현장 입력 ({dev})";
        return cid.Length > 0 ? "현장 입력" : "파일 기록";
    }

    /// <summary>provenance.facts → (항목, 값, 입력자 (역할), 입력 시각, 경로) 판정 시각 at 날짜까지, 타설 → 공시체 → 사양 → 설치 검측 → 근거 원장 → 승인 기록</summary>
    public static List<string[]> Facts(string folder, string zoneId, DateTime at)
    {
        var day = at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var who = WhoFn(folder);
        var outp = new List<string[]>();
        void Add(string item, string value, string? name, string? when, string how)
        {
            var w = who(name);
            outp.Add(new[] { item, value, w.Length > 0 ? w : "—", When(when), how });
        }

        var zone = Evaluator.Read(folder, "zones.csv").FirstOrDefault(r => r.GetValueOrDefault("zone_id") == zoneId);
        var pours = Evaluator.Read(folder, "pours.csv").Where(r => r.GetValueOrDefault("zone_id") == zoneId).ToList();
        var lot = pours.Count > 0 ? V(pours[^1], "pour_id") : "";
        if (pours.Count > 0)
        {
            var p = pours[^1];
            var wit = string.Join(" · ", new[] { V(p, "supervisor_present").Length > 0 ? "입회 감리 " + Raw(p, "supervisor_present") : "", V(p, "watcher").Length > 0 ? "감시자 " + Raw(p, "watcher") : "" }.Where(x => x.Length > 0));
            Add("타설", $"{Raw(p, "pour_date")} {Raw(p, "pour_time")} · {Raw(p, "mix")} · fck {Raw(p, "fck")} MPa" + (wit.Length > 0 ? " · " + wit : ""), p.GetValueOrDefault("entered_by"), p.GetValueOrDefault("entered_at"), Via(p));
        }
        foreach (var c in Evaluator.Read(folder, "cylinders.csv"))
        {
            bool own = c.GetValueOrDefault("zone_id") == zoneId;
            if ((own || (V(c, "zone_id").Length == 0 && lot.Length > 0 && V(c, "pour_id") == lot)) && string.CompareOrdinal(Raw(c, "test_date"), day) <= 0)
                Add("공시체", $"{Raw(c, "test_date")} {Raw(c, "curing")} {Raw(c, "f1")}·{Raw(c, "f2")}·{Raw(c, "f3")} MPa" + (own ? "" : $" (타설 {lot} 공통)"), c.GetValueOrDefault("entered_by"), c.GetValueOrDefault("entered_at"), Via(c));
        }
        if (zone != null && ShoreInput.SpecOf(folder, Py.Int(zone["floor"]), zoneId) is { } sp)
            Add("동바리 사양", $"{Raw(sp, "type")} {Raw(sp, "spec")} · {Raw(sp, "spacing_x_mm")}×{Raw(sp, "spacing_y_mm")} mm · 설치 {Raw(sp, "install_date")}" + (V(sp, "zone_id").Length > 0 ? "" : " (층 전체)"),
                sp.GetValueOrDefault("entered_by"), sp.GetValueOrDefault("entered_at"), Via(sp));
        var ins = Evaluator.Read(folder, "install_inspection.csv").Where(r => r.GetValueOrDefault("zone_id") == zoneId && string.CompareOrdinal(Raw(r, "date"), day) <= 0).ToList();
        if (ins.Count > 0)
        {
            var r = ins[^1];
            Add("설치 검측", $"{ins.Count}줄 · 마지막 {Raw(r, "date")} {Raw(r, "item_code")} {Raw(r, "result")}", Or(r, "entered_by", "inspector"), Or(r, "entered_at", "date"), Via(r));
        }
        foreach (var r in Evaluator.Read(folder, Evidence.Ledger))
            if (r.GetValueOrDefault("zone_id") == zoneId && string.CompareOrdinal(Raw(r, "date"), day) <= 0)
            {
                int st = int.TryParse(V(r, "stage"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
                var name = Evidence.Stages.FirstOrDefault(x => x.Stage == st).Name ?? "";
                Add($"근거 원장 {st} {name}", string.Join(" ", new[] { V(r, "doc_no"), V(r, "result"), V(r, "sha256").Length > 0 ? "파일 SHA-256 " + Raw(r, "sha256")[..Math.Min(12, Raw(r, "sha256").Length)] : "" }.Where(x => x.Length > 0)),
                    r.GetValueOrDefault("by"), r.GetValueOrDefault("date"), "근거 원장");
            }
        foreach (var r in Approval.ReadLog(folder))
            if (r.GetValueOrDefault("zone_id") == zoneId && string.CompareOrdinal(Raw(r, "timestamp").Length > 10 ? Raw(r, "timestamp")[..10] : Raw(r, "timestamp"), day) <= 0)
            {
                var sign = V(r, "approver");
                Add(Raw(r, "action"), (V(r, "note") + (sign.Length > 0 ? $" · 서명 감리 {sign} {When(r.GetValueOrDefault("signed_at"))}" : "")).Trim(' ', '·'),
                    r.GetValueOrDefault("actor"), r.GetValueOrDefault("timestamp"), "승인 기록 (해시 사슬)" + (V(r, "source").Length > 0 ? " · " + Raw(r, "source") : ""));
            }
        return outp;
    }

    // Python: r.get(a) or r.get(b) — 빈 글자면 다음 열
    static string? Or(Row r, string a, string b) => (r.GetValueOrDefault(a) ?? "").Length > 0 ? r[a] : r.GetValueOrDefault(b);

    /// <summary>provenance.table_html — '기본 사항·출처' 절</summary>
    public static string TableHtml(IReadOnlyList<string[]> rows)
    {
        var body = rows.Count > 0 ? string.Concat(rows.Select(r => $"<tr><td>{Py.Html(r[0])}</td><td>{Py.Html(r[1])}</td><td>{Py.Html(string.Join(" · ", r.Skip(2).Where(c => c.Length > 0)))}</td></tr>")) : $"<tr><td colspan=\"{Head.Length}\">기록 없음</td></tr>";
        return $"<h2>붙임. 기본 사항·출처</h2><table class=\"prov\"><tr>{string.Concat(Head.Select(h => $"<th>{h}</th>"))}</tr>{body}</table><p class=\"note\">{Py.Html(Note)}</p>";
    }
}

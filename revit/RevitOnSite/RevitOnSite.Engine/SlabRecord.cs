// shoring/slab_record.py 의 1:1 C# 이식 (F10, impact_map #17) — 슬래브 이력서: 구역마다 한 장 (타설·기온 근거·공시체·판정·근거 원장·승인 기록·판정 이력·지문).
// 새 계산 없음: Evaluator·Evidence.LedgerAt·Approval(사슬)·eval_log·WeatherLabel 을 모은다. 품질 기록이며 해체·사용 승인이 아니다.
// 기록은 Python dict 모양 그대로 (Dictionary<string, object?>) — 지문 sha = json.dumps(rec, ensure_ascii=False, sort_keys=True) 의 SHA-256 앞 12자라 같은 글자여야 함.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RevitOnSite.Engine;

public static class SlabRecord
{
    static readonly string[] ApprovalShown = { "요청 제출", "감리 승인 기록", "해체 완료", "재받침" };
    static readonly string[] CylKeys = { "test_date", "curing", "f1", "f2", "f3", "pour_id", "sampled_at", "supplier", "truck_no", "storage_zone", "cyl_lot" };
    static readonly string[] AprKeys = { "timestamp", "action", "actor", "approver", "signed_at", "sha", "source" };

    /// <summary>Python dict.get(k, "") — 열이 없으면 "", 열은 있는데 칸이 모자라면 null (DictReader 의 None)</summary>
    static string? G(IReadOnlyDictionary<string, string?> r, string k) => r.TryGetValue(k, out var v) ? v : "";
    static Dictionary<string, object?> Pick(IReadOnlyDictionary<string, string?> r, IEnumerable<string> keys) => keys.ToDictionary(k => k, k => (object?)G(r, k));

    /// <summary>slab_record.py records: → 구역 ID 순 기록 [{zone_id, floor, pour, weather, cylinders, decision, evidence, approvals, chain, history, sha}].
    /// zone 을 주면 그 구역만 (pours.csv 에 없으면 InvalidDataException)</summary>
    public static List<Dictionary<string, object?>> Records(string folder, DateTime at, string? zone = null, double cureMin = 5.0)
        => Evaluator.Checked(() => RecordsCore(folder, at, zone, cureMin));   // 빈 칸·열 없음 → 한국어 입력 오류 문구

    static List<Dictionary<string, object?>> RecordsCore(string folder, DateTime at, string? zone, double cureMin)
    {
        var rows = new Dictionary<string, ZoneRow>();
        foreach (var r in Evaluator.Evaluate(folder, at, cureMin)) rows[r.ZoneId] = r;
        var pours = new Dictionary<string, Dictionary<string, string?>>();
        foreach (var p in Evaluator.Read(folder, "pours.csv")) pours[p["zone_id"]!] = p;
        var cyl = Evaluator.CylRows(folder);   // W1: 타설 묶음 공통 시험 → 묶음의 구역마다
        var wlabel = Evaluator.WeatherLabel(Evaluator.Read(folder, "weather.csv"));
        var led = Evidence.LedgerAt(folder, DateOnly.FromDateTime(at));
        var (chainOk, chain) = Approval.Verify(folder);
        var iso = at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var log = chainOk ? Approval.ReadLog(folder).Where(r => string.CompareOrdinal(Head(r["timestamp"] ?? "", 10), iso) <= 0).ToList() : new();
        var cut = at.ToString("yyyy-MM-ddTHH:mm", Py.Inv);
        var histBy = new Dictionary<string, Dictionary<string, string?>>();   // 11-11: 판정 시각까지, 같은 시각은 마지막 한 줄 (slab_record.py 와 같음 — 첫 자리·마지막 값)
        foreach (var h in Evaluator.Read(folder, "eval_log.csv"))
        {
            var k = h.GetValueOrDefault("eval_at") ?? "";
            if (string.CompareOrdinal(k.Length > 16 ? k[..16] : k, cut) <= 0) histBy[k] = h;
        }
        var hist = histBy.Values.ToList();
        var ins = Evaluator.InstallRows(folder);   // 설치 검측 기록 (총괄 11-05: 설치 검측 결과의 전달 경로)
        var outp = new List<Dictionary<string, object?>>();
        foreach (var zid in zone is null ? pours.Keys.OrderBy(z => z, Evaluator.FloorOrder(folder)).ToList() : new List<string> { zone })
        {
            if (!pours.TryGetValue(zid, out var p)) throw new InvalidDataException($"{zid}: pours.csv 에 없음");
            rows.TryGetValue(zid, out var d);
            led.TryGetValue(zid, out var z);
            var last = new Dictionary<string, (string Date, string Res)>();   // 항목마다 마지막 날짜 줄 (같은 날이면 뒤 줄), 순서 = 처음 나온 항목 순
            foreach (var r in ins)
            {
                string k = (r.GetValueOrDefault("item_code") ?? "").Trim(), dd = (r.GetValueOrDefault("date") ?? "").Trim();
                if ((r.GetValueOrDefault("zone_id") ?? "").Trim() != zid || k.Length == 0) continue;
                if (string.CompareOrdinal(dd, last.TryGetValue(k, out var lv) ? lv.Date : "") >= 0) last[k] = (dd, (r.GetValueOrDefault("result") ?? "").Trim().ToUpperInvariant());
            }
            var rec = new Dictionary<string, object?>
            {
                ["zone_id"] = zid,
                ["install"] = new Dictionary<string, object?>
                {
                    ["date"] = last.Count == 0 ? "" : last.Values.Select(v => v.Date).Aggregate((a, b) => string.CompareOrdinal(b, a) > 0 ? b : a),
                    ["not_ok"] = last.Where(kv => kv.Value.Res != "OK").Select(kv => (object?)kv.Key).ToList(),
                },
                ["floor"] = d is null ? "" : (object?)d.Floor,
                ["pour"] = Pick(p, new[] { "pour_date", "pour_time", "mix", "fck" }),
                ["weather"] = d is not null && !d.EstSource.Contains("센서 없음") ? wlabel : d?.EstSource ?? "",
                ["cylinders"] = cyl.Where(r => r["zone_id"] == zid).Select(r => (object?)Pick(r, CylKeys)).ToList(),
                ["decision"] = d is null ? null : new Dictionary<string, object?>
                {
                    ["decision"] = d.Decision, ["reason"] = d.Reason, ["f_cyl"] = d.FCyl is { } fc ? (object?)fc : "", ["f_est_min"] = d.FEstMin,
                    ["level_gate"] = d.LevelGate, ["predicted_due"] = Py.Iso(d.PredictedDue),
                },
                ["evidence"] = new Dictionary<string, object?>
                {
                    ["stage"] = z?.Stage ?? 0,
                    ["dates"] = (z?.Dates ?? new()).OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key.ToString(CultureInfo.InvariantCulture), kv => (object?)kv.Value),
                    ["warnings"] = (z?.Warnings ?? new()).Select(w => (object?)w).ToList(),
                },
                ["approvals"] = log.Where(r => r["zone_id"] == zid && ApprovalShown.Contains(r["action"])).Select(r => (object?)Pick(r, AprKeys)).ToList(),
                ["chain"] = chainOk ? chain : $"승인 기록 사슬 손상 — {chain}",
                ["history"] = hist.Select(h => (object?)new Dictionary<string, object?> { ["eval_at"] = G(h, "eval_at"), ["summary"] = G(h, "summary") }).TakeLast(5).ToList(),
            };
            var ph = Photos.Rows(folder, new HashSet<string> { zid }, iso);   // 현장 사진 (F15) — 있을 때만 키를 둠 (사진 없는 이력서의 지문은 그대로)
            if (ph.Count > 0)
                rec["photos"] = ph.Select(x => (object?)new Dictionary<string, object?> { ["when"] = x.When, ["kind"] = x.Kind, ["path"] = x.Path, ["sha256"] = x.Sha256 }).ToList();
            rec["sha"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Dumps(rec)))).ToLowerInvariant()[..12];   // 이 이력서 내용의 지문
            outp.Add(rec);
        }
        return outp;
    }

    static string Head(string s, int n) => s.Length > n ? s[..n] : s;

    /// <summary>Python json.dumps(v, ensure_ascii=False, sort_keys=True) — dict·list·str·int·float·bool·None 만</summary>
    internal static string Dumps(object? v)
    {
        var b = new StringBuilder();
        void W(object? x)
        {
            switch (x)
            {
                case null: b.Append("null"); break;
                case string s: Str(s); break;
                case bool t: b.Append(t ? "true" : "false"); break;
                case int i: b.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case double f: b.Append(Py.Repr(f)); break;
                case IDictionary<string, object?> m:
                    b.Append('{');
                    var first = true;
                    foreach (var kv in m.OrderBy(kv => kv.Key, StringComparer.Ordinal)) { if (!first) b.Append(", "); first = false; Str(kv.Key); b.Append(": "); W(kv.Value); }
                    b.Append('}'); break;
                case System.Collections.IEnumerable l:
                    b.Append('[');
                    var f1 = true;
                    foreach (var y in l) { if (!f1) b.Append(", "); f1 = false; W(y); }
                    b.Append(']'); break;
                default: throw new ArgumentException("json 으로 못 씀: " + x.GetType());
            }
        }
        void Str(string s)
        {
            b.Append('"');
            foreach (var c in s)
                b.Append(c switch
                {
                    '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", '\b' => "\\b", '\f' => "\\f",
                    < ' ' => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
                    _ => c.ToString(),
                });
            b.Append('"');
        }
        W(v);
        return b.ToString();
    }

    static string E(string? s) => Py.Html(s);
    /// <summary>Python str(x): None → "None", float → repr</summary>
    static string S(object? x) => x switch { null => "None", double f => Py.Repr(f), int i => i.ToString(CultureInfo.InvariantCulture), _ => x.ToString() ?? "" };
    static Dictionary<string, object?> D(object? x) => x as Dictionary<string, object?> ?? new();
    static string? T(Dictionary<string, object?> m, string k) => m.TryGetValue(k, out var v) ? v as string : null;
    static IEnumerable<Dictionary<string, object?>> L(object? x) => ((IEnumerable<object?>)x!).Cast<Dictionary<string, object?>>();
    static List<string> L2(object? x) => ((IEnumerable<object?>)x!).Select(v => (string)v!).ToList();

    /// <summary>slab_record.py page: A4 인쇄용 HTML (구역마다 한 쪽, 머리에 "품질 기록이며 해체·사용 승인이 아닙니다")</summary>
    public static string Page(IReadOnlyList<Dictionary<string, object?>> recs, string project, DateTime at)
    {
        var inv = CultureInfo.InvariantCulture;
        string One(Dictionary<string, object?> r)
        {
            var d = D(r["decision"]);
            var pour = D(r["pour"]);
            var ev = D(r["evidence"]);
            var cyl = string.Concat(L(r["cylinders"]).Select(c => $"<tr><td>{E(Py.KDate(T(c, "test_date")))}</td><td>{E(T(c, "curing"))}</td><td>{E(T(c, "f1"))} · {E(T(c, "f2"))} · {E(T(c, "f3"))}</td>"
                + $"<td>{(E(string.Join(" / ", new[] { "pour_id", "sampled_at", "supplier", "storage_zone" }.Select(k => k == "sampled_at" ? Py.KStamp(T(c, k)) : k == "pour_id" && T(c, "cyl_lot") != "" ? "타설 " + T(c, "cyl_lot") : T(c, k)).Where(x => !string.IsNullOrEmpty(x)))) is { Length: > 0 } ci ? ci : "—")}</td></tr>"));
            var apr = string.Concat(L(r["approvals"]).Select(a => $"<tr><td>{E(Py.KStamp(T(a, "timestamp")))}</td><td>{E(T(a, "action"))}</td><td>{E(T(a, "actor"))}</td><td>{E(string.Join(" · ", new[] { T(a, "approver") ?? "", Approval.SourceLabel(new Dictionary<string, string?> { ["source"] = T(a, "source"), ["action"] = T(a, "action") }) }.Where(x => x.Length > 0)))}</td>"
                + $"<td>{E(Py.KStamp(T(a, "signed_at") ?? ""))}</td><td><small>{E(Head(T(a, "sha") ?? "", 12))}</small></td></tr>"));
            var ledger = string.Join(" · ", D(ev["dates"]).Select(kv => $"{kv.Key} {Py.KDate(kv.Value as string)}"));
            if (ledger.Length == 0) ledger = "없음";
            var warn = string.Concat(((IEnumerable<object?>)ev["warnings"]!).Select(w => $"<li>{E((string?)w)}</li>"));
            string Dg(string k, string dflt) => d.TryGetValue(k, out var v) ? S(v) : dflt;
            var photo = r.TryGetValue("photos", out var phs) ? "<tr><th>현장 사진</th><td>" + string.Join("<br>", L(phs).Select(x =>
                $"{E(Py.KStamp(T(x, "when")))} {E(T(x, "kind"))} — {E(T(x, "path"))} · <small>{Head(T(x, "sha256") ?? "", 12)}</small>")) + "</td></tr>" : "";   // 경로·해시 (F15)
            return $"<section><h2>{E((string)r["zone_id"]!)} <small>슬래브 이력서 · 지문 {r["sha"]}</small></h2>\n"
                + $"<table><tr><th>타설</th><td>{E(Py.KDate(T(pour, "pour_date")))} {E(T(pour, "pour_time"))} · {E(T(pour, "mix"))} · fck {E(S(pour["fck"]))} MPa</td></tr>\n"
                + $"<tr><th>설치 검측</th><td>{(T(D(r["install"]), "date") is { Length: > 0 } idt ? E(Py.KDate(idt)) : "기록 없음")}"
                + $"{(L2(D(r["install"])["not_ok"]) is { Count: > 0 } nok ? " · 확인 필요: " + E(string.Join(", ", nok)) : "")}</td></tr>\n"
                + $"<tr><th>기온 근거</th><td>{E((string?)r["weather"])}</td></tr>\n"
                + $"<tr><th>판정 ({Py.KStamp(at)})</th><td>{E(Dg("decision", "판정 없음"))} — {E(Dg("reason", ""))}<br><small>공시체 {E(Dg("f_cyl", ""))} · 구역 최소 추정 {E(Dg("f_est_min", ""))} MPa · 규칙 게이트 {E(Dg("level_gate", ""))} · 예측 도달 {E(Py.KDate(Dg("predicted_due", "")))}</small></td></tr>\n"
                + $"<tr><th>근거 원장</th><td>{S(ev["stage"])}/8 — {E(ledger)}{(warn.Length > 0 ? "<ul class=\"warn\">" + warn + "</ul>" : "")}</td></tr>{photo}</table>\n"
                + $"<h3>공시체</h3><table><tr><th>시험일</th><th>양생</th><th>MPa (3개)</th><th>채취 정보</th></tr>{(cyl.Length > 0 ? cyl : "<tr><td colspan=4>없음</td></tr>")}</table>\n"
                + $"<h3>승인·해체 기록</h3><table><tr><th>기록 시각</th><th>종류</th><th>입력자</th><th>감리</th><th>서명 일시</th><th>줄 해시</th></tr>{(apr.Length > 0 ? apr : "<tr><td colspan=6>없음</td></tr>")}</table>\n"
                + "</section>";
        }
        var hist = string.Concat((recs.Count > 0 ? L(recs[0]["history"]) : Enumerable.Empty<Dictionary<string, object?>>()).Select(h => $"<li>{E(Py.KStamp(T(h, "eval_at")))} — {E(T(h, "summary"))}</li>"));
        return $"<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>슬래브 이력서 {E(project)} {at.ToString("yyyy-MM-dd", inv)}</title>\n"
            + "<style>@page{size:A4;margin:14mm}body{font:10.5pt 'Malgun Gothic',sans-serif;color:#111}section{page-break-after:always}h2{font-size:15pt;margin:0 0 6px}h2 small{font-size:9pt;color:#555;font-weight:400}\n"
            + "h3{font-size:11pt;margin:10px 0 4px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #999;padding:4px 6px;text-align:left;vertical-align:top}th{background:#eee;white-space:nowrap}.warn{color:#a00;margin:4px 0 0 16px}.note{color:#444;font-size:9pt}" + ReportStyle.Css + "</style></head><body>\n"
            + $"<p class=\"note\">{E(project)} · 기준 {Py.KStamp(at)} · Revit On-Site 데이터 폴더 기록을 모은 것 (새 계산 없음). 이 문서는 품질 기록이며 해체·사용 승인이 아닙니다. 최근 판정 이력: <ul class=\"note\">{(hist.Length > 0 ? hist : "<li>없음</li>")}</ul></p>\n"
            + string.Concat(recs.Select(One)) + "</body></html>";
    }

    /// <summary>slab_record.py write_csv: UTF-8 BOM, csv.writer (CRLF·필요할 때만 따옴표)</summary>
    public static void WriteCsv(IReadOnlyList<Dictionary<string, object?>> recs, string path)
    {
        var sb = new StringBuilder(Py.CsvLine(new[] { "zone_id", "pour_date", "decision", "evidence_stage", "approvals", "cylinders", "warnings", "sha" }));
        foreach (var r in recs)
        {
            var ev = D(r["evidence"]);
            sb.Append(Py.CsvLine(new[]
            {
                (string)r["zone_id"]!, T(D(r["pour"]), "pour_date") ?? "", T(D(r["decision"]), "decision") ?? "", $"{S(ev["stage"])}/8",
                string.Join(" · ", L(r["approvals"]).Select(a => $"{Head(T(a, "timestamp") ?? "", 10)} {T(a, "action")}")), L(r["cylinders"]).Count().ToString(CultureInfo.InvariantCulture),
                string.Join(" · ", ((IEnumerable<object?>)ev["warnings"]!).Cast<string>()), (string)r["sha"]!,
            }));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }
}

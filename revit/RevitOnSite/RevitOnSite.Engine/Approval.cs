// shoring/approval.py 의 1:1 C# 이식: 해체 검측요청서 초안(감리 검측요청 서식 준용) + 승인 기록(바꾸면 드러나는 기록, 해시 사슬).
//
// 프로그램은 승인하지 않는다. 결론은 evaluate 의 "해체 승인 요청 가능"/"존치" 뿐이고, 승인은 감리가 한다.
// ① 요청 가능 구역의 근거를 모은 검측요청서 초안(HTML A4 4쪽 서식, 인쇄 → PDF, 서명·결과·합불 칸 빈칸) — 법령에 '해체 승인 요청서' 조문은 없음, 감리 검측요청 서식 준용
// ② 사람이 입력한 행위(요청 제출·감리 승인 기록·해체 완료·재받침)를 approvals.csv 에 이어 쓰며 각 줄에 앞 줄 해시를 묶어
//    줄을 고치거나 지우면 Verify() 가 찾는다. evidence_sha = 그때 판정 행(근거)의 해시.
// 해시·CSV·HTML 은 Python 과 바이트 동일 (Engine.Check 에서 대조).
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Row = System.Collections.Generic.IReadOnlyDictionary<string, string?>;

namespace RevitOnSite.Engine;

public static class Approval
{
    public const string Log = "approvals.csv";
    /// <summary>approval.py DURING_NOTE — 고정 문구 (총괄 P1): 요청서 '함께 낼 것' 아래·층 표지 상태 줄 아래, 마감 값에는 안 넣음</summary>
    public const string DuringNote = "해체 중: 가이드라인 4.2 체크리스트(해체 순서·추락 방지·보호구) — 관리감독자 현장 확인";
    public static readonly string[] FieldsV1 = { "seq", "timestamp", "zone_id", "action", "actor", "note", "evidence_sha", "prev_sha", "sha" };
    /// <summary>approval.py FIELDS — 뒤에 감리 성명·서명 일시 (총괄 11-05, direction_review 10). 옛 줄·옛 해시는 그대로</summary>
    public static readonly string[] Fields = FieldsV1.Concat(new[] { "approver", "signed_at", "source" }).ToArray();   // + 출처 (v6.2 W-A)
    /// <summary>approval.py SOURCES — 감리 승인 기록 출처: 서버 감리 확인(S3) / 종이 서명을 [승인 기록]으로 옮김</summary>
    public static readonly string[] Sources = { "감리 직접", "종이 옮김" };

    /// <summary>approval.py source_label — source 값, 없으면(옛 줄·[승인 기록] 종이 서명) '종이 옮김'. 다른 행위는 빈칸</summary>
    public static string SourceLabel(IReadOnlyDictionary<string, string?> r) =>
        (r.GetValueOrDefault("source") ?? "").Trim() is { Length: > 0 } s ? s : r.GetValueOrDefault("action") == "감리 승인 기록" ? "종이 옮김" : "";
    /// <summary>행위 → 상태 코드 (정의서 §3.5), 요청 제출은 상태 안 바꿈. 순서 = Python dict 순서</summary>
    public static readonly IReadOnlyList<(string Action, int? Code)> Actions = new (string, int?)[] { ("요청 제출", null), ("감리 승인 기록", 6), ("해체 완료", 7), ("재받침", 8) };
    public const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";
    static readonly string[] EvidenceKeys = { "zone_id", "decision", "reason", "threshold", "f_cyl", "cyl_test_date", "cyl_curing", "f_est_min", "est_source", "level_gate" };

    public static int? CodeOf(string? action) => Actions.FirstOrDefault(a => a.Action == action).Code;
    static bool IsAction(string action) => Actions.Any(a => a.Action == action);

    /// <summary>판정 행(열 이름 → Python str() 값) → 근거 해시 (열 순서·실수 표기와 무관하게 같은 값)</summary>
    public static string EvidenceSha(IReadOnlyDictionary<string, string> row)
    {
        var parts = EvidenceKeys.OrderBy(k => k, StringComparer.Ordinal).Select(k => Json(k) + ": " + Json(row.GetValueOrDefault(k) ?? ""));
        return Sha("{" + string.Join(", ", parts) + "}");
    }

    public static string EvidenceSha(ZoneRow r) => EvidenceSha(RowOf(r));

    /// <summary>판정표 한 행 → evaluate.py 출력 dict 의 str() 값 (CSV 글자와 같음)</summary>
    public static Dictionary<string, string> RowOf(ZoneRow r) => ZoneRow.CsvHeader.Zip(r.CsvValues()).ToDictionary(p => p.First, p => p.Second);

    /// <summary>Python json.dumps(s, ensure_ascii=False) 의 문자열 하나</summary>
    static string Json(string s)
    {
        var b = new StringBuilder("\"");
        foreach (char c in s)
            b.Append(c switch
            {
                '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", '\b' => "\\b", '\f' => "\\f",
                < ' ' => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
                _ => c.ToString(),
            });
        return b.Append('"').ToString();
    }

    static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    /// <summary>approval.py line_sha: 옛 9열(sha 앞까지) + approver·signed_at 은 값이 있을 때만 뒤에 — 옛 사슬은 그대로, 새 줄의 성명·서명 일시를 지우거나 옛 줄에 쓰면 드러남</summary>
    public static string LineSha(Row r)
    {
        var ext = new[] { r.GetValueOrDefault("approver") ?? "", r.GetValueOrDefault("signed_at") ?? "" }
            .Concat(string.IsNullOrEmpty(r.GetValueOrDefault("source")) ? Array.Empty<string>() : new[] { r["source"]! }).ToArray();   // W-A 출처도 값이 있을 때만
        return Sha(string.Join("|", FieldsV1[..^1].Select(k => Py.Str(r.GetValueOrDefault(k))).Concat(ext.Any(x => x.Length > 0) ? ext : Array.Empty<string>())));
    }

    public static List<Dictionary<string, string?>> ReadLog(string folder) => Evaluator.Read(folder, Log);

    /// <summary>해시 사슬 검사 → (정상 여부, 문구). 줄을 고치거나 지우거나 순서를 바꾸면 그 줄 번호를 알려 준다.</summary>
    public static (bool Ok, string Message) Verify(string folder)
    {
        string prev = Genesis;
        var rows = ReadLog(folder);
        for (int i = 1; i <= rows.Count; i++)
        {
            var r = rows[i - 1];
            if (r["seq"] != i.ToString(CultureInfo.InvariantCulture) || r["prev_sha"] != prev)
                return (false, $"{Log} {i}번째 기록: 앞 기록과 이어지지 않음 (지워지거나 순서가 바뀜)");
            if (LineSha(r) != r["sha"])
                return (false, $"{Log} {i}번째 기록 ({Py.Str(r["zone_id"])} {Py.Str(r["action"])}): 내용이 기록 뒤에 바뀜");
            prev = r["sha"]!;
        }
        return (true, $"기록 {rows.Count}건, 사슬 정상");
    }

    public const string DemoNote = "(시연 시각 — 판정 기준일 고정)";

    /// <summary>approval.py record_clock: [승인 기록]·[근거 원장] 기본 시각 — 현장 입력 서버(FieldInput.SyncDate)와 같은 규칙.
    /// 마지막 판정 시각(판정일 09:00)이 지금보다 미래면 시연 자료로 보고 그 시각으로 고정 (Demo = true), 아니면 실제 시계.
    /// 실제 프로젝트(판정일 ≤ 오늘)는 늘 실제 시계 — 기록의 핵심은 '언제 실제로 넣었나' (총괄 11-04)</summary>
    public static (DateTime At, bool Demo) RecordClock(DateTime? evalAt, DateTime? now = null)
    {
        var n = now ?? DateTime.Now;
        return evalAt is { } e && e > n ? (e, true) : (n, false);
    }

    /// <summary>사람이 한 행위 한 줄을 이어 쓴다. 검사: 행위 이름, 입력자, 사슬 정상, 요청 제출·감리 승인 기록은 그 시점 판정이 '해체 승인 요청 가능'일 때만.
    /// now 를 안 주면 RecordClock(evalAt): 판정 기준 시각이 미래(시연 자료)면 그 시각 + note 끝에 DemoNote (해시 사슬 형식은 그대로).
    /// approver·signedAt(yyyy-MM-dd 또는 yyyy-MM-dd HH:mm) = 종이에 서명한 감리 성명·일시 — '감리 승인 기록'에만 (서명 스캔 SHA 는 근거 원장 6).
    /// 거부는 InvalidOperationException (Python ValueError 와 같은 문구)</summary>
    public static Dictionary<string, string> Record(string folder, string zoneId, string action, string actor, IReadOnlyDictionary<string, string> decisionRow,
        string note = "", DateTime? now = null, DateTime? evalAt = null, string approver = "", string signedAt = "", string source = "")
    {
        if (!IsAction(action))
            throw new InvalidOperationException($"행위 '{action}' 은(는) 없음 — {Py.ReprList(Actions.Select(a => a.Action))}");
        if (actor.Trim().Length == 0)
            throw new InvalidOperationException("입력자 이름이 필요합니다");
        var (ok, msg) = Verify(folder);
        if (!ok)
            throw new InvalidOperationException("승인 기록이 손상돼 이어 쓰지 않음 — " + msg);
        var decision = decisionRow.GetValueOrDefault("decision");
        if (action is "요청 제출" or "감리 승인 기록" && decision != "해체 승인 요청 가능")
            throw new InvalidOperationException($"{zoneId} 판정이 '{Py.Str(decision)}' — 해체 승인 요청 가능일 때만 {action}");
        approver = (approver ?? "").Trim();
        signedAt = (signedAt ?? "").Trim().Replace(" ", "T");
        if ((approver.Length > 0 || signedAt.Length > 0) && action != "감리 승인 기록")
            throw new InvalidOperationException("감리 성명·서명 일시는 '감리 승인 기록'에만 적습니다");
        source = (source ?? "").Trim();
        if (source.Length > 0 && (action != "감리 승인 기록" || !Sources.Contains(source)))
            throw new InvalidOperationException($"출처 '{source}' — '감리 승인 기록'에만 {string.Join(" · ", Sources)} 중 하나");
        if (signedAt.Length > 0 && !DateTime.TryParseExact(signedAt, new[] { "yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new InvalidOperationException($"서명 일시 '{signedAt}' — YYYY-MM-DD 또는 YYYY-MM-DD HH:MM 로 적어 주세요");
        if (now is null)
        {
            (now, bool demo) = RecordClock(evalAt);
            if (demo) note = note.Trim().Length > 0 ? note + " " + DemoNote : DemoNote;
        }
        var rows = ReadLog(folder);
        var r = new Dictionary<string, string>
        {
            ["seq"] = (rows.Count + 1).ToString(CultureInfo.InvariantCulture),
            ["timestamp"] = now.Value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            ["zone_id"] = zoneId, ["action"] = action, ["actor"] = actor.Trim(), ["note"] = note.Replace("\n", " "),
            ["evidence_sha"] = EvidenceSha(decisionRow), ["prev_sha"] = rows.Count > 0 ? rows[^1]["sha"] ?? "None" : Genesis,
            ["approver"] = approver, ["signed_at"] = signedAt, ["source"] = source,
        };
        r["sha"] = LineSha(r.ToDictionary(kv => kv.Key, kv => (string?)kv.Value));
        var p = Path.Combine(folder, Log);
        if (File.Exists(p))   // 옛 9·11열 머리 → 12열 머리로 (줄은 그대로, 빈 칸은 값 없음으로 읽힘 — 해시 무관), approval.py 와 같은 바이트
        {
            var all = Py.ReadText(p, new UTF8Encoding(false));
            int nl = all.IndexOf('\n');
            string head = nl >= 0 ? all[..nl] : all;
            if (!head.Contains("source"))
                File.WriteAllText(p, string.Join(",", Fields) + (head.EndsWith('\r') ? "\r" : "") + (nl >= 0 ? all[nl..] : ""), new UTF8Encoding(false));
        }
        var text = new StringBuilder();
        if (!File.Exists(p)) text.Append(Py.CsvLine(Fields));
        text.Append(Py.CsvLine(Fields.Select(k => r[k])));
        File.AppendAllText(p, text.ToString(), new UTF8Encoding(false));   // Python open("a", encoding="utf-8") — BOM 없음
        return r;
    }

    /// <summary>approval.py SUPERVISE_REJECTS (W-A): 동기화 때 넣지 못한 감리 직접 확인 (승인 기록 아님) — '오늘 볼 것' 이유</summary>
    public const string SuperviseRejects = "supervise_rejects.csv";
    static readonly string[] RejectFields = { "zone_id", "received_at", "name", "client_id", "reason" };

    /// <summary>approval.py log_supervise_reject: 감리 직접 확인(서버)을 Revit 검사에서 넣지 못함 → 한 줄 추가. 판정·승인 기록은 그대로</summary>
    public static void LogSuperviseReject(string folder, string zoneId, string name, string clientId, string receivedAt, string reason)
    {
        var p = Path.Combine(folder, SuperviseRejects);
        var text = new StringBuilder();
        if (!File.Exists(p)) text.Append(Py.CsvLine(RejectFields));
        receivedAt ??= "";
        text.Append(Py.CsvLine(new[] { zoneId, receivedAt.Length > 16 ? receivedAt[..16] : receivedAt, name, clientId,
            Py.Squash(reason ?? "") }));
        File.AppendAllText(p, text.ToString(), new UTF8Encoding(false));   // Python open("a", encoding="utf-8") + csv.writer
    }

    /// <summary>approval.py supervise_rejects: 구역마다 마지막 거절 (at 날짜까지) → (받은 시각, 감리 이름, 이유), 그 뒤 감리 승인·해체·재받침 기록이 생긴 구역은 뺌</summary>
    public static Dictionary<string, (string At, string Name, string Reason)> SuperviseRejectList(string folder, DateTime? at = null)
    {
        var outp = new Dictionary<string, (string, string, string)>();
        var day = at?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var r in Evaluator.Read(folder, SuperviseRejects))
        {
            var z = (r.GetValueOrDefault("zone_id") ?? "").Trim();
            var ts = (r.GetValueOrDefault("received_at") ?? "").Trim();
            if (z.Length == 0) continue;
            if (day is null || string.CompareOrdinal(ts.Length > 10 ? ts[..10] : ts, day) <= 0)
                outp[z] = (ts, (r.GetValueOrDefault("name") ?? "").Trim(), (r.GetValueOrDefault("reason") ?? "").Trim());
        }
        var done = StatusOverride(folder, at);
        return outp.Where(kv => !(done.TryGetValue(kv.Key, out var c) && c is 6 or 7 or 8)).ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>구역별 마지막 상태 행위 → 상태 코드 {zone_id: 6|7|8} (요청 제출은 제외). 판정 화면이 이 코드를 우선 표시.
    /// at 을 주면 그 판정일까지의 기록만 (뒤 날짜 승인 기록이 그 판정에 적용되지 않게 — 20차 점검, approval.py 와 같음).
    /// 날짜 단위: 판정 시각은 늘 판정일 09:00 이라 시각으로 자르면 같은 날 오후 감리 승인 기록이 그날 다시 한 판정에서 빠짐</summary>
    public static Dictionary<string, int> StatusOverride(string folder, DateTime? at = null) =>
        StatusEvents(folder, at).ToDictionary(kv => kv.Key, kv => kv.Value.Code);

    /// <summary>approval.py request_rows: 검측요청서·검측 패키지에 넣을 판정 줄 — 사람 기록(6 감리 승인 기록 · 7 해체 완료 · 8 재받침)이 있는 구역은 뺌
    /// (11-09 시뮬레이션: 해체 완료·감리 승인된 구역이 '요청 가능'으로 다시 요청서에 들어가던 것)</summary>
    public static IReadOnlyList<ZoneRow> RequestRows(IReadOnlyList<ZoneRow> rows, string? folder, DateTime? at = null)
    {
        if (string.IsNullOrEmpty(folder)) return rows.ToList();
        var done = StatusOverride(folder, at);
        return rows.Where(r => !done.ContainsKey(r.ZoneId)).ToList();
    }

    /// <summary>approval.py zone_counts: 요약 숫자 한 곳 (11-11 Revit #1) — 사람 기록(6·7·8) 구역은 Recorded 로만 셈</summary>
    public static (int Ready, int Keep, int Defect, int Cyl, int Recorded) Counts(IReadOnlyList<ZoneRow> rows, string? folder, DateTime? at = null)
    {
        var left = RequestRows(rows, folder, at);
        int N(Func<string, bool> f) => left.Count(r => f(r.Decision));
        return (N(d => d == "해체 승인 요청 가능"), N(d => d.StartsWith("존치", StringComparison.Ordinal)), N(d => d == "존치 (설치 결함 기록)"),
                N(d => d == "공시체 시험 필요"), rows.Count - left.Count);
    }

    /// <summary>approval.py status_events: 구역별 마지막 상태 사건 (코드 6|7|8, 시각 글자) — 승인 기록(요청 제출 제외) + 협력업체 해체 신고 '뺐음'
    /// (shore_checks.csv source = 협력업체 신고, present = 아니오 → 7, 66차). 시각 순 (같은 시각이면 승인 기록 다음 신고), 날짜 단위로 판정일까지</summary>
    public static Dictionary<string, (int Code, string Ts)> StatusEvents(string folder, DateTime? at = null)
    {
        var evs = new List<(string Ts, string Zone, int Code)>();
        foreach (var r in ReadLog(folder))
            if (CodeOf(r.GetValueOrDefault("action")) is int code) evs.Add((Py.Str(r.GetValueOrDefault("timestamp")), Py.Str(r.GetValueOrDefault("zone_id")), code));
        foreach (var r in Evaluator.Read(folder, PourGate.ChecksFile))
            if ((r.GetValueOrDefault("source") ?? "").Trim() == Evidence.ReportSource && (r.GetValueOrDefault("present") ?? "").Trim() == "아니오")
                evs.Add((r.GetValueOrDefault("timestamp") ?? "", r.GetValueOrDefault("zone_id") ?? "", 7));
        static string Head(string s, int n) => s.Length > n ? s[..n] : s;
        var day = at?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var outp = new Dictionary<string, (int, string)>();
        foreach (var e in evs.OrderBy(e => Head(e.Ts, 16).Replace(' ', 'T'), StringComparer.Ordinal))
            if (day is null || string.CompareOrdinal(Head(e.Ts, 10), day) <= 0) outp[e.Zone] = (e.Code, e.Ts);
        return outp;
    }

    /// <summary>Python html.escape(s, quote=True)</summary>
    static string E(string? s) => Py.Html(s);

    // ---- 검측요청서 서식 (approval.py FORM_* 와 같은 글자 — 총괄 11-05: docs/templates/해체검측요청서_템플릿.docx 배치) ----
    // A4 4쪽: ① 검측요청서 + 결과 통보(별지 제6호 준용) ② 판정 근거표·존치 사유·감리 확인 3가지·현장 경고·마감 값
    //         ③ 해체 검측 체크리스트(별지 제5호 준용, 가이드라인 4.1·4.2·4.3) ④ 공사 참여자 실명부(별지 제7호 준용)
    public const string FormName = "동바리 해체 검측요청서 (감리 검측요청 서식 준용)";
    public const string FormFoot = "본 문서는 계산 도구가 만든 초안이며 해체 승인이 아닙니다. 해체 승인은 감리 검측 결과로만 정합니다.";
    const string FormCss = "@page{size:A4;margin:12mm 13mm}body{font:9.5pt 'Malgun Gothic',sans-serif;color:#111;background:#fff;margin:0}\n"
        + ".page{break-after:page;page-break-after:always}.page:last-child{break-after:auto;page-break-after:auto}\n"
        + ".hd{text-align:right;color:#777;font-size:7.5pt}.form{display:flex;justify-content:space-between;color:#555;font-size:8pt;margin-top:2px}.re{color:#b00020;font-weight:700}\n"
        + ".title{border-top:2px solid #222;border-bottom:2px solid #222;text-align:center;padding:4px 0;margin:2px 0 4px}.title b{font-size:16pt;display:block}.title span{color:#555;font-size:8pt}\n"
        + "h2{font-size:10.5pt;margin:6px 0 2px}h2 i{color:#1f3a68;font-style:normal;margin-right:6px}h3{font-size:10pt;margin:6px 0 3px}\n"
        + "table{border-collapse:collapse;width:100%;margin:0 0 4px;font-size:9pt}th,td{border:1px solid #444;padding:2px 4px;text-align:left;vertical-align:middle}\n"
        + "th{background:#e7e9ee;text-align:center;font-weight:700}td.in{background:#eef4fb}.hint{color:#8a93a3}.c{text-align:center}.n{white-space:nowrap}.s{font-size:8pt}\n"
        + ".lead{text-align:center;margin:4px 0}.note{color:#555;font-size:8pt;margin:4px 0}.warn{color:#a00}.sign td{height:26px}\n"
        + ".ft{border-top:1px solid #aaa;color:#555;font-size:7.5pt;text-align:center;margin-top:8px;padding-top:2px}\n"
        + "tr,.sign{break-inside:avoid;page-break-inside:avoid}td.in,th{-webkit-print-color-adjust:exact;print-color-adjust:exact}";   // 11-11: 붙임 1 이 여러 장이어도 줄·서명란이 안 갈리고 입력란 색이 인쇄됨
    const string FormChecks = "<td class=\"in\">□ 해체 기준 강도 충족 — 현장양생공시체 평균 ≥ 해체 기준 강도 (2쪽 근거표)<br>□ 센서 구역 최소 추정 강도 확인 (센서 위치별)<br>"
        + "□ 타설층 포함 하부 3개 층 동바리 존치<br>□ 해체 후 하중 재하 검토 (하중비교 · 지지층 14 MPa 이상)<br>□ 해체 작업계획서 승인 · 해체 전 체크리스트 (3쪽)</td>";
    const string FormAttach = "<td>□ 시공자 검측 체크리스트 (3쪽) &nbsp; □ 시험성과 (현장양생공시체 압축강도 시험)<br>□ 판정 근거표 · 근거 원장 (2쪽) &nbsp; □ 야장 및 도면 (동바리 배치도 · 센서 위치도)<br>"
        + "□ 해체 작업계획서 &nbsp; □ 공사 참여자(기능공 포함) 실명부 (4쪽)</td>";
    const string FormResult = "<h2><i>②</i>검 측 결 과 통 보</h2>\n"
        + "<table><tr><th style=\"width:13%\">번호</th><td class=\"in\" style=\"width:37%\"><span class=\"hint\">20 &nbsp;. &nbsp;. &nbsp;. - &nbsp;호</span></td><th style=\"width:13%\">받음</th><td class=\"in\"><span class=\"hint\">○○공사 현장대리인 ○○○</span></td></tr></table>\n"
        + "<p class=\"lead\">문서번호 <span class=\"hint\">「 &nbsp; &nbsp; &nbsp; &nbsp; 」</span> 로 검측요청 한 건에 대하여 20 &nbsp; . &nbsp; . &nbsp; . 검측한 결과를 다음과 같이 통보합니다.</p>\n"
        + "<table><tr><th style=\"width:18%\">1. 검측결과</th><td class=\"in\">□ 합격 — 해체 승인 구역 : <br>□ 불합격 — 존치 구역 : <br>□ 재검측 필요 — 재검측 예정일 : 20 &nbsp; . &nbsp; . &nbsp; .</td></tr>\n"
        + "<tr><th>2. 지시사항</th><td class=\"in\" style=\"height:46px\"></td></tr><tr><th>붙임</th><td>□ 감리원의 검측 점검표 (3쪽 감리 확인란)</td></tr></table>\n"
        + "<table class=\"sign\"><tr><th style=\"width:18%\">감리</th><th style=\"width:16%\">검측감리원</th><td class=\"in\" style=\"text-align:right\">(인)</td><th style=\"width:16%\">총괄감리원</th><td class=\"in\" style=\"text-align:right\">(인)</td></tr>\n"
        + "<tr><th>서명 일시</th><td class=\"in\" colspan=\"2\"><span class=\"hint\">20 &nbsp;. &nbsp;. &nbsp;. &nbsp; : </span></td><th>감리 성명</th><td class=\"in\"></td></tr></table>\n"
        + "<p class=\"note\"><b>※ 작성요령</b> 1. 재검측 시에는 붉은 글씨로 \"(재)\"를 우측 상단에 표시합니다. 2. 시공자가 재검측을 요청할 때에는 잘못 시공한 기능공의 명단을 첨부합니다. "
        + "3. 2부 작성하여 시공자, 감리원이 각 1부를 보관합니다. 4. 옅은 파란 칸이 입력란입니다.</p>\n";
    /// <summary>체크리스트 행 (구분 칸, 검사항목, 검사기준) — approval.py FORM_CHECKLIST 와 같은 순서</summary>
    static readonly (string Grp, string Item, string Std)[] FormItems =
    {
        ("<th rowspan=\"7\">4.1<br>해체 전</th>", "해체 작업계획서 작성 · 검토", "계획서 승인 (해체 순서 · 범위 · 일정)"),
        ("", "존치기간 준수 · 강도 발현 확인", "현장양생공시체 ≥ 해체 기준 강도 (가이드라인 표 2.1.1)"),
        ("", "압축강도 확인 후 해체작업 허가", "시공사 해체 허가 기록"),
        ("", "해체 후 하중 재하 시 필러 동바리 존치 · 재설치 구조검토", "하중비교 (가이드라인 2.1.2 계산 예)"),
        ("", "하부 지지층 강도", "최소 14 MPa (가이드라인 2.1.2)"),
        ("", "작업 준비 · 근로자 교육 · 출입 통제", "관리감독자 확인"),
        ("", "센서 위치별 추정 강도 (구역 최소)", "센서 구역 최소 ≥ 해체 기준 강도"),
        ("<th>4.2<br>해체 중</th>", "해체 순서 (벽 → 기둥 → 보 → 슬래브) · 추락 방지 · 보호구 · 기상", "가이드라인 4.2 체크리스트 — 관리감독자 현장 확인"),
        ("<th rowspan=\"5\">4.3<br>해체 후</th>", "필러 · 지지용 동바리 타설층 포함 하부 3개 층 설치", "KCS 14 20 12:2024 3.3.2(2) · 가이드라인 2.1.2(1)④"),
        ("", "각 층 같은 위치에 설치 (상하 정렬)", "층 간 편차 허용값 이내 (설치 검측)"),
        ("", "동바리 연직도", "설치 검측 기준"),
        ("", "자재 변형 · 침하", "육안 · 측정"),
        ("", "콘크리트 면 결함 (돌기물 등)", "가이드라인 4.3"),
        ("<th>기타</th>", "", ""),
    };
    static readonly string FormChecklist =
        "<table><tr><th rowspan=\"2\" style=\"width:9%\">구분</th><th rowspan=\"2\" style=\"width:33%\">검사항목</th><th rowspan=\"2\">검사기준<br><span class=\"s\">(시방서 · 가이드라인 · 도면)</span></th>"
        + "<th colspan=\"2\">검사결과</th><th rowspan=\"2\" style=\"width:12%\">조치사항</th></tr><tr><th style=\"width:9%\">시공자</th><th style=\"width:9%\">감리</th></tr>\n"
        + string.Concat(FormItems.Select(t => $"<tr>{t.Grp}<td>{t.Item}</td><td class=\"s\">{t.Std}</td><td class=\"in c s\">합 · 불</td><td class=\"in c s\">합 · 불</td><td class=\"in\"></td></tr>\n"))
        + "</table>\n"
        + "<table class=\"sign\"><tr><th style=\"width:18%\">시공자</th><th style=\"width:13%\">점검일자</th><td class=\"in\"><span class=\"hint\">20 &nbsp; . &nbsp; . &nbsp; .</span></td><th style=\"width:13%\">점검직원</th><td class=\"in\" style=\"text-align:right\">(인)</td></tr>\n"
        + "<tr><th>감리원</th><th>검측일자</th><td class=\"in\"><span class=\"hint\">20 &nbsp; . &nbsp; . &nbsp; .</span></td><th>검측감리원</th><td class=\"in\" style=\"text-align:right\">(인)</td></tr></table>\n";
    static readonly string FormRoster =
        "<table><tr><th>작업일</th><th>작업위치<br>및 공종</th><th>소속</th><th>직위</th><th>성명</th><th>생년월일<br>(성별)</th><th>공사한 내용</th><th>서명</th></tr>\n"
        + string.Concat(Enumerable.Repeat("<tr>" + string.Concat(Enumerable.Repeat("<td class=\"in\" style=\"height:22px\"></td>", 8)) + "</tr>\n", 18)) + "</table>\n"
        + "<p class=\"note\"><b>※</b> 실명부는 시공자가 작성하여 감리원에게 제출하고, 감리원은 확인 후 유지·관리합니다. 개인정보는 공사 관리 목적으로만 쓰고 보관 기간이 지나면 파기합니다.</p>\n";

    /// <summary>서식 한 쪽: 머리 · (재)검측 · 제목 띠 · 본문 · 바닥글 (n / 4) — approval.py _form_page</summary>
    static string FormPage(int n, string formNo, string title, string sub, string body) =>
        $"<section class=\"page\"><div class=\"hd\">{FormName}</div>\n<div class=\"form\"><span>{formNo}</span><span class=\"re\">(재)검측 □</span></div>\n"
        + $"<div class=\"title\"><b>{title}</b><span>{sub}</span></div>\n{body}<div class=\"ft\">{FormFoot} &nbsp; 서식 {n} / 4</div></section>\n";

    /// <summary>동바리 해체 검측요청서 (감리 검측요청 서식 준용) 초안 HTML — A4 4쪽. 요청 가능 구역은 근거와 함께, 존치 구역은 사유만(층·판정·사유·예측일이 같으면 한 줄).
    /// 서명·결과·합불 칸은 빈칸. 데이터·마감 값 계산은 그대로. — approval.py request_html</summary>
    public static string RequestHtml(IReadOnlyList<ZoneRow> decisions, string project, DateTime at, string? folder = null, IReadOnlyDictionary<string, GateDetail>? gate = null)
    {
        const string Ready = "해체 승인 요청 가능";
        int nAll = decisions.Count;
        decisions = RequestRows(decisions, folder, at);   // 11-09: 이미 사람 기록(6·7·8)이 있는 구역은 요청서에서 뺌 — 대시보드와 같은 상태 우선 (approval.py 와 같음)
        int skip = nAll - decisions.Count;
        var req = decisions.Where(d => d.Decision == Ready).ToList();
        var keep = decisions.Where(d => d.Decision != Ready).ToList();
        var (ok, chain) = folder != null ? Verify(folder) : (true, "");
        var led = !string.IsNullOrEmpty(folder) ? Evidence.LedgerAt(folder, DateOnly.FromDateTime(at)) : new Dictionary<string, ZoneLedger>();
        var unready = req.Where(d => !string.IsNullOrEmpty(folder) && !new[] { 2, 4 }.All(k => led.TryGetValue(d.ZoneId, out var zl) && zl.Dates.ContainsKey(k))).Select(d => d.ZoneId).ToList();   // 11-11 1-1: 가이드라인 표 3.2.1 2·4단계
        string site = "", sealV = "";
        if (!string.IsNullOrEmpty(folder) && req.Count > 0) (site, _) = SiteChecks(req, folder, at, out sealV);
        string R(double x) => Py.Repr(x);
        static string Hint(string t) => $"<span class=\"hint\">{t}</span>";
        var ids = req.Select(d => d.ZoneId).ToList();
        var fl = req.Select(d => d.ZoneId.Contains('-') ? d.ZoneId.Split('-')[0] : FloorSign.FloorName(d.Floor, null)).Distinct().ToList();
        string day = at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var p1 = "<h2><i>①</i>검 측 요 청 서</h2>\n"
            + $"<table><tr><th style=\"width:13%\">번호</th><td class=\"in\" style=\"width:37%\">{Hint("20 &nbsp;. &nbsp;. &nbsp;. - &nbsp;호")}</td><th style=\"width:13%\">받음</th><td class=\"in\">{Hint("○○공사 총괄감리원 ○○○")}</td></tr>\n"
            + $"<tr><th>공사명</th><td class=\"in\">{E(project)}</td><th>요청일</th><td class=\"in\">{Py.KDate(day)}</td></tr></table>\n"
            + "<p class=\"lead\">다음과 같은 세부공종에 대하여 검측요청 하오니 검사 후 승인하여 주시기 바랍니다.</p>\n"
            + $"<table><tr><th style=\"width:18%\">위치 및 공종</th><td class=\"in\">{(req.Count > 0 ? E(string.Join(", ", ids)) + " / 거푸집·동바리 해체" : Hint("해체 승인 요청 가능 구역 없음"))}</td></tr>\n"
            + $"<tr><th>검측부위</th><td class=\"in\">{(req.Count > 0 ? E(string.Join("·", fl)) + " 슬래브 동바리 해체 · 위 3개층 존치 동바리 확인" : "")}</td></tr>\n"
            + $"<tr><th>검측요구일시</th><td class=\"in\">{Hint("20 &nbsp;. &nbsp;. &nbsp;. ( &nbsp;) &nbsp; : ")}</td></tr>\n"
            + $"<tr><th>검측사항</th>{FormChecks}</tr>\n"
            + $"<tr><th>도구 판정</th><td class=\"in\">해체 승인 요청 가능 <b>{req.Count}</b> 구역 &nbsp;/&nbsp; 존치 <b>{keep.Count(d => d.Decision.StartsWith("존치", StringComparison.Ordinal))}</b> 구역 &nbsp;/&nbsp; 공시체 시험 필요 <b>{keep.Count(d => d.Decision == "공시체 시험 필요")}</b> 구역 &nbsp; &nbsp; 마감 값 <b>{(sealV.Length > 0 ? sealV : "—")}</b>"
            + (skip > 0 ? $" &nbsp; <span class=\"s\">이미 기록된 {skip}구역 제외 (감리 승인 기록·해체 완료·재받침)</span>" : "")
            + (unready.Count > 0 ? $"<br><b class=\"warn\">현장 확인 미비 {unready.Count}구역 — 작업계획서·해체 전 점검 근거 원장 기록 없음: {E(string.Join(", ", unready))}</b>" : "")
            + $" &nbsp; <span class=\"s\">(판정 기준 시각 {Py.KStamp(at)})</span><br><span class=\"note\">도구는 \"요청 가능 / 존치\"만 표시합니다. 해체 승인은 감리 검측으로만 정합니다.</span></td></tr>\n"
            + $"<tr><th>붙임</th>{FormAttach}</tr></table>\n"
            + "<table class=\"sign\"><tr><th style=\"width:18%\">시공자</th><th style=\"width:16%\">점검직원</th><td class=\"in\" style=\"text-align:right\">(인)</td><th style=\"width:16%\">현장대리인</th><td class=\"in\" style=\"text-align:right\">(인)</td></tr></table>\n"
            + FormResult;
        static bool NoSensor(ZoneRow d) => d.EstSource.StartsWith("기상 예측", StringComparison.Ordinal);
        string EstCell(ZoneRow d) => NoSensor(d) ? "<td class=\"c\">센서 없음</td>"   // 11-11 Revit #17: 센서가 없으면 칸엔 '센서 없음'만 (설명은 표 아래 한 줄)
            : $"<td>{R(d.FEstMin)}{(d.FEstXy != null ? " / " + E(Evaluator.XyText(d.FEstXy)) : "")}<br><span class=\"s\">{E(d.EstSource)}</span></td>";
        string GateCell(string zid)   // 11-11 G7: 위로 타설된 층 수 · 가장 약한 지지층 · 하중비 (approval.py gate_cell)
        {
            if (gate == null || !gate.TryGetValue(zid, out var g)) return "";
            var parts = new List<string> { $"위 {g.Above}층" };
            if (g.Supports.Count > 0) { var low = g.Supports.MinBy(x => x.Mpa); parts.Add($"지지층 최소 {low.Zone} {Py.F(low.Mpa, 1)}"); }
            if (g.Ratio is double r) parts.Add($"하중비 {Py.F(r, 2)}");
            return "<br><span class=\"s\">" + E(string.Join(" · ", parts)) + "</span>";
        }
        var rows = string.Concat(req.Select(d =>
            $"<tr><td class=\"c n\">{E(d.ZoneId)}</td><td class=\"c\">{d.Floor.ToString(Py.Inv)}</td><td class=\"c n\">{Py.KDate(d.PourDate)}</td><td class=\"c\">{R(d.AgeDays)}</td><td class=\"c\">{Py.G(d.Fck)}</td>"
            + $"<td class=\"n\">{(d.FCyl is { } c ? R(c) : "")} <span class=\"s\">{Py.KDate(d.CylTestDate)}</span><br><span class=\"s\">{E(d.CylCuring)}</span>{(Evaluator.LotLabel(d.Reason) is { Length: > 0 } lot ? $"<br><span class=\"s\">{E(lot)}</span>" : "")}</td>"
            + EstCell(d)
            + $"<td class=\"c\">{E(d.LevelGate)}{GateCell(d.ZoneId)}</td><td class=\"c\">{(led.TryGetValue(d.ZoneId, out var L) ? L.Stage : 0)}/8<br><span class=\"s\">{EvidenceSha(d)[..12]}</span></td><td class=\"c n\">요청 가능</td></tr>\n"));
        // 존치: 층·판정·사유·예측일이 같으면 한 줄 (GroupBy = 처음 나온 순서, Python dict 와 같음)
        var held = string.Concat(keep.GroupBy(d => (d.Floor, d.Decision, d.Reason, d.PredictedDue)).Select(g =>
            $"<tr><td>{E(string.Join(", ", g.Select(d => d.ZoneId)))}</td><td class=\"c\">{g.Key.Floor.ToString(Py.Inv)}</td><td>{E(g.Key.Decision)}: {E(g.Key.Reason)}</td>"
            + $"<td class=\"c n\">{Py.KDate(g.Key.PredictedDue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}</td><td></td></tr>\n"));
        var p2 = "<h2><i>③</i>요청 구역 근거표</h2>\n"
            + "<table><thead><tr><th>구역</th><th>층</th><th>타설일</th><th class=\"n\">재령<br>(일)</th><th class=\"n\">fck<br>(MPa)</th><th>공시체 평균 (MPa)<br>· 시험일</th><th>센서 구역 최소 (MPa)<br>/ 위치</th>"
            + "<th class=\"s n\">존치 층수<br>(규칙 게이트)</th><th class=\"n\">근거<br>n/8 · 해시</th><th class=\"n\">도구 판정</th></tr></thead>\n"
            + (rows.Length > 0 ? rows : "<tr><td colspan=\"10\" class=\"c\">없음</td></tr>\n") + "</table>\n"
            + "<p class=\"note\"><b>판정 근거</b> 거푸집 및 동바리 해체 가이드라인(국토교통부, 2023.1) 표 2.1.1 · KCS 14 20 12:2024 3.3.1·3.3.2 — 해체 기준 강도 ≥ 설계기준압축강도"
            + "(판넬 조기 탈형에 한해 필러 동바리 존치·구조계산 시 최소 14 MPa), 타설층 포함 3개 층 존치, 같은 위치 현장양생 공시체 시험(가이드라인 2.1.2(3)④·고시 제2024-879호). "
            + "우선순위 규칙 게이트 → 공시체 → 센서 구역 최소. 근거 해시 = 판정 행 해시 (approvals.csv 의 evidence_sha 와 대조)."
            + (folder != null ? " · 승인 기록: " + E(chain) : "")
            + (req.FirstOrDefault(NoSensor) is { } ns ? " · 센서 없음 = " + E(ns.EstSource) : "")
            + (!string.IsNullOrEmpty(folder) ? " · " + E(Evaluator.SupportNote(folder)) : "") + "</p>\n"
            + (ok ? "" : "<p class=\"warn\">승인 기록 사슬 손상: " + E(chain) + "</p>\n")
            + "<h2><i>④</i>존치 구역 및 사유</h2>\n"
            + "<table><thead><tr><th style=\"width:21%\">구역</th><th style=\"width:4%\">층</th><th>존치 사유</th><th style=\"width:12%\">예측 해체일</th><th style=\"width:7%\">비고</th></tr></thead>\n"
            + (held.Length > 0 ? held : "<tr><td colspan=\"5\" class=\"c\">없음</td></tr>\n") + "</table>\n"
            + (site.Length > 0 ? site : "<h2><i>⑤</i>감리가 현장에서 확인할 3가지</h2><p class=\"note\">요청 가능 구역이 없어 표가 없습니다.</p>") + "\n"
            + $"<table><tr><th style=\"width:12%\">마감 값</th><td class=\"c\" style=\"width:12%\"><b>{(sealV.Length > 0 ? sealV : "—")}</b></td><td class=\"s\">판정 시점 근거를 고정한 값(SHA 앞 8자리) — 앱 화면 값과 같아야 합니다. "
            + "서명할 때 종이에 함께 적어 두면 나중에 근거 묶음(판정 근거·원장·승인 기록)이 바뀌었는지 대조됩니다 (바꾸기를 막지는 못하고 드러낼 뿐).</td></tr></table>\n";
        var za = !string.IsNullOrEmpty(folder) ? Evaluator.Read(folder, "zones.csv").ToDictionary(r => r["zone_id"] ?? "", r => (Py.Float(r["x1"]) - Py.Float(r["x0"])) * (Py.Float(r["y1"]) - Py.Float(r["y0"]))) : new();
        string area = ids.Count > 0 && ids.All(za.ContainsKey) ? Py.F(ids.Sum(z => za[z]), 1) : "&nbsp; &nbsp;";   // 11-11 Revit #16: 구역 면적 합 (approval.py 와 같음)
        var p3 = "<table><tr><th style=\"width:9%\">№</th><th style=\"width:14%\">공종 CODE</th><th>위치 및 부위</th><th style=\"width:24%\">공종(세부공종)</th><th style=\"width:20%\">공사량</th></tr>\n"
            + $"<tr><td class=\"in\"></td><td class=\"in\"></td><td class=\"in\">{E(string.Join(", ", ids))}</td><td class=\"in\">거푸집·동바리 해체</td><td class=\"in\">해체 면적 {area} m²</td></tr></table>\n"
            + FormChecklist + $"<p class=\"note\">{E(DuringNote)}. ※ 작성요령 1. \"시공자\" 칸은 시공자 담당기술자가 먼저 점검하여 합격으로 확인한 뒤 검측요청서와 함께 제출합니다. "
            + "2. \"감리\" 칸은 감리원이 현장 확인 후 기록합니다. 3. 불합격 항목은 조치사항을 적고 보완 시공 후 재검측을 받습니다. 4. 4.2 해체 중 항목은 도구가 관리하지 않으며 관리감독자가 현장에서 확인합니다.</p>\n";
        var p4 = $"<table><tr><th style=\"width:18%\">공사명</th><td class=\"in\">{E(project)}</td></tr></table>\n" + FormRoster;
        return $"<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>{FormName} {E(project)} {day}</title>\n"
            + $"<style>{FormCss}{ReportStyle.Css}</style></head><body>\n"
            + FormPage(1, "[별지 제6호서식 준용] 주택건설공사 감리업무 세부기준", "동바리 해체 검측요청서", "거푸집·동바리 해체 — 감리 검측요청 서식 준용 (검측요청 · 결과 통보)", p1)
            + FormPage(2, "[붙임 1] 판정 근거표 — 근거 원장 요약", "해체 요청 구역 판정 근거", "도구 계산값 · 현장양생공시체 시험 · 센서 위치별 추정 강도", p2)
            + FormPage(3, "[별지 제5호서식 준용] 검측 점검표", "거푸집·동바리 해체 검측 체크리스트", "거푸집 및 동바리 해체 가이드라인(2023.1) 4.1 해체 전 · 4.2 해체 중 · 4.3 해체 후", p3)
            + FormPage(4, "[별지 제7호서식 준용] 공사 참여자(기능공 포함) 실명부", "공사 참여자 실명부", "거푸집·동바리 해체 작업 참여자", p4)
            + "</body></html>";
    }

    /// <summary>[가정] 위층 동바리 존치 확인으로 인정하는 시간 (PourGate.CheckFreshH 와 같은 값) — approval.py CHECK_FRESH_H</summary>
    public const double CheckFreshH = 24.0;

    /// <summary>approval.py site_checks: 감리가 현장에서 확인할 3가지 표 + 마감 값 문단.
    /// ① 위 3개층(평면 겹침·층 순서) 구역마다 최근 24 h 동바리 '예' 확인 ② 작업계획서(원장 2) ③ 해체 전 체크리스트(원장 4).
    /// 마감 값 = sha256(json.dumps([[구역, 근거 해시, [[단계, 날짜]…]]…, 승인 기록 끝 해시], ensure_ascii=False)) 앞 8자 — 동바리 확인은 안 들어감</summary>
    public static (string Site, string Seal) SiteChecks(IReadOnlyList<ZoneRow> req, string folder, DateTime at) => SiteChecks(req, folder, at, out _);

    /// <summary>approval.py check_targets: 요청서 현장 확인 ① 대상 — zid 위 3개층(층 순서)에서 평면이 겹치는 구역, zones.csv 순서 (SiteChecks 의 need 와 같은 규칙).
    /// 검측 패키지가 이 구역들의 최근 24 h 동바리 확인 사진을 첨부/사진/ 에 넣을 때 씀 (총괄 29차)</summary>
    public static List<string> CheckTargets(string folder, string zid)
    {
        var ids = new List<string>();
        var zrow = new Dictionary<string, Dictionary<string, string?>>();
        foreach (var r in Evaluator.Read(folder, "zones.csv"))
        {
            var z = r["zone_id"] ?? "";
            if (!zrow.ContainsKey(z)) ids.Add(z);
            zrow[z] = r;
        }
        if (!zrow.ContainsKey(zid)) return new();
        var floorOf = ids.ToDictionary(z => z, z => Py.Int(zrow[z]["floor"]));
        var rectOf = ids.ToDictionary(z => z, z => (Py.Float(zrow[z]["x0"]), Py.Float(zrow[z]["y0"]), Py.Float(zrow[z]["x1"]), Py.Float(zrow[z]["y1"])));
        var above = floorOf.Values.Distinct().OrderBy(f => f).Where(f => f > floorOf[zid]).Take(3).ToHashSet();
        return ids.Where(z => above.Contains(floorOf[z]) && Evaluator.Overlaps(rectOf[z], rectOf[zid])).ToList();
    }

    /// <summary>마감 값 8자만 (층 표지 — 검측요청서와 같은 식, 요청 가능 구역 req 가 있을 때만 의미 있음)</summary>
    public static string SealValue(IReadOnlyList<ZoneRow> req, string folder, DateTime at)
    {
        SiteChecks(req, folder, at, out var v);
        return v;
    }

    static (string Site, string Seal) SiteChecks(IReadOnlyList<ZoneRow> req, string folder, DateTime at, out string sealValue)
    {
        // zones.csv → dict (Python: 같은 구역 ID 가 또 나오면 값은 뒤 줄, 순서는 처음 나온 자리)
        var ids = new List<string>();
        var zrow = new Dictionary<string, Dictionary<string, string?>>();
        foreach (var r in Evaluator.Read(folder, "zones.csv"))
        {
            var z = r["zone_id"] ?? "";
            if (!zrow.ContainsKey(z)) ids.Add(z);
            zrow[z] = r;
        }
        var floorOf = ids.ToDictionary(z => z, z => Py.Int(zrow[z]["floor"]));
        var rectOf = ids.ToDictionary(z => z, z => (Py.Float(zrow[z]["x0"]), Py.Float(zrow[z]["y0"]), Py.Float(zrow[z]["x1"]), Py.Float(zrow[z]["y1"])));
        var order = floorOf.Values.Distinct().OrderBy(f => f).ToList();
        var last = new Dictionary<string, (DateTime T, string Present)>();
        foreach (var r in Evaluator.Read(folder, "shore_checks.csv"))
        {
            var t = Evaluator.Ts(r["timestamp"]);
            var z = r["zone_id"] ?? "";
            if (t <= at && (!last.TryGetValue(z, out var l) || t >= l.T))
                last[z] = (t, (r.GetValueOrDefault("present") ?? "").Trim());
        }
        var led = Evidence.LedgerAt(folder, DateOnly.FromDateTime(at));
        var key = Evaluator.FloorOrder(folder);   // 구역 목록은 층 순 → 구역 ID (approval.py floor_order, 총괄 11-05)
        var watch = Evidence.RemovalWatch(folder, DateOnly.FromDateTime(at));   // 무단 해체 관측 (총괄 P1-2, 경고만)
        var rows = new StringBuilder();
        var sealSrc = new List<string>();
        var near = new HashSet<int>();
        foreach (var d in req)
        {
            var zid = d.ZoneId;
            if (!floorOf.TryGetValue(zid, out var fl)) continue;
            var above = order.Where(f => f > fl).Take(3).ToHashSet();
            near.Add(fl); near.UnionWith(above);
            var need = ids.Where(z => above.Contains(floorOf[z]) && Evaluator.Overlaps(rectOf[z], rectOf[zid])).ToList();
            var miss = need.Where(z => !(last.TryGetValue(z, out var l) && l.Present == "예" && (at - l.T).TotalSeconds / 3600 <= CheckFreshH)).ToList();
            var dates = led.TryGetValue(zid, out var L) ? L.Dates : new Dictionary<int, string>();
            int stage = L?.Stage ?? 0;
            var sortedMiss = miss.OrderBy(z => z, key).ToList();
            string c1 = need.Count == 0 ? "— 위층 없음"
                : miss.Count == 0 ? "✓ 확인 있음"
                : "✕ 확인 없음: " + string.Join(", ", sortedMiss.Take(6)) + (miss.Count > 6 ? " …" : "");
            var sus = need.Append(zid).Where(watch.ContainsKey).OrderBy(z => z, key).ToList();
            if (sus.Count > 0) c1 = $"⚠ 무단 해체 의심 — 확인 필요: {string.Join(", ", sus)} · {c1}";
            string c2 = dates.TryGetValue(2, out var d2) ? "✓ " + Py.KDate(d2) : "✕ 원장 기록 없음";
            string c3 = dates.TryGetValue(4, out var d4) ? "✓ " + Py.KDate(d4) : "✕ 원장 기록 없음";
            rows.Append($"<tr><td class=\"n\">{E(zid)}</td><td>{stage}/8</td><td>{E(c1)}</td><td>{E(c2)}</td><td>{E(c3)}</td><td class=\"in c n\">□ 적합</td><td class=\"in c n\">□ 적합</td></tr>");
            var items = string.Join(", ", dates.OrderBy(kv => kv.Key).Select(kv => "[" + kv.Key.ToString(CultureInfo.InvariantCulture) + ", " + Json(kv.Value) + "]"));
            sealSrc.Add("[" + Json(zid) + ", " + Json(EvidenceSha(d)) + ", [" + items + "]]");
        }
        // 이 층·바로 위 3개층 현장 경고 (총괄 적합성 ⓐ-1, approval.py 와 같은 글자): 층 단위 — 무단 해체 관측·서명 전 해체 관측·해체 후 점검 미완. 마감 값에는 안 넣음
        var day = DateOnly.FromDateTime(at);
        var scope = req.Where(d => Evaluator.ScopeNote(d.Reason).Length > 0).ToDictionary(d => d.ZoneId, d => $"{d.ZoneId} {Evaluator.ScopeNote(d.Reason)}");   // v6.2 W11 범위 밖 (요청 구역)
        var nearW = watch.Concat(Evidence.SignatureWatch(folder, day)).Concat(AfterCheck.Warnings(folder, Py.Iso(day))).Concat(scope)
            .Where(kv => floorOf.TryGetValue(kv.Key, out var f) && near.Contains(f)).OrderBy(kv => kv.Key, key).ToList();
        var lis = string.Concat(nearW.Select(kv => $"<li>{E(kv.Value)}</li>"));
        var warn = "<h2><i>⑥</i>이 층·바로 위 3개층 현장 경고</h2>" + (lis.Length > 0 ? $"<ul class=\"warn\">{lis}</ul>" : "<p class=\"note\">없음</p>")
            + "<p class=\"note\">같은 층·위 3개층의 무단 해체 관측·서명 전 해체 관측·해체 후 점검 미완·범위 밖 가능(두꺼운 슬래브·지하층·높은 동바리) — 경고만 (판정·마감 값에 넣지 않음), 현장에서 확인하세요.</p>";
        var log = ReadLog(folder);
        string head = log.Count > 0 ? log[^1].GetValueOrDefault("sha") ?? "" : Genesis;
        string seal = Sha("[[" + string.Join(", ", sealSrc) + "], " + Json(head) + "]")[..8];
        sealValue = seal;
        string site = "<h2><i>⑤</i>감리가 현장에서 확인할 3가지</h2><table class=\"s\"><tr><th>구역</th><th>근거 원장</th><th>① 위 3개층 동바리 존치 (최근 24 h)</th>"
            + "<th>② 작업계획서</th><th>③ 해체 전 체크리스트 (가이드라인 4.1)</th><th>시공자</th><th>감리</th></tr>" + rows + "</table>"
            + "<p class=\"note\">✕ 는 프로그램 기록에 없다는 뜻입니다 — 현장에서 직접 확인하고 서류를 받으세요. 동바리 확인은 휴대폰 [동바리 확인], 서류는 [근거 원장].</p>" + warn;
        return (site, "<p class=\"note\">마감 값 <b>" + seal + "</b> — 서명할 때 이 값을 종이에 함께 적어 두면, 나중에 이 근거 묶음(판정 근거·원장·승인 기록)이 바뀌었는지 "
            + "대조할 수 있습니다. 바꾸기를 막지는 못하고 드러낼 뿐입니다.</p>");
    }
}

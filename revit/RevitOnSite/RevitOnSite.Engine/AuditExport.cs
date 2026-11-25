// shoring/audit_export.py 의 1:1 C# 이식 — 감사 기록 (impact_map #4 책임 추적, 총괄 결정 11-05 ⑩).
// 승인 기록·근거 원장·동바리 확인·동바리 점검(F16)·판정 이력·해체 후 점검을 시각 순으로 한 표에. 새 판단 없음, 원본 파일은 읽기만.
// 열: when, source, zone_id, action, actor, detail, line_sha (원본 줄 값을 "|" 로 이은 글자의 SHA-256 앞 16자)
using System.Security.Cryptography;
using System.Text;
using Row = System.Collections.Generic.Dictionary<string, string?>;

namespace RevitOnSite.Engine;

public sealed record AuditRow(string When, string Source, string ZoneId, string Action, string Actor, string Detail, string LineSha);

public static class AuditExport
{
    public static readonly string[] Cols = { "when", "source", "zone_id", "action", "actor", "detail", "line_sha" };

    static string G(Row r, string k) => (r.GetValueOrDefault(k) ?? "").Trim();
    static string Join(params string[] xs) => string.Join(" · ", xs.Where(x => x.Length > 0));
    static string Stage(string s) => int.TryParse(s, System.Globalization.NumberStyles.None, Py.Inv, out var n) && Evidence.Stages.Any(x => x.Stage == n) ? $"{s} {Evidence.StageName(n)}" : s;
    static string Pre(string p, string v) => v.Length > 0 ? p + v : "";
    /// <summary>사진 줄 detail = 경로 · sha 앞 12자 (F15, 총괄 29차 — audit_export.py _photo). line_sha 는 그대로</summary>
    static string Photo(Row r) => Join(G(r, "photo"), Pre("sha ", G(r, Photos.ShaCol).Length > 12 ? G(r, Photos.ShaCol)[..12] : G(r, Photos.ShaCol)));

    static readonly (string File, string Label, Func<Row, (string When, string Zone, string Action, string Actor, string Detail)> F)[] Sources =
    {
        ("approvals.csv", "승인 기록", r => (G(r, "timestamp"), G(r, "zone_id"), G(r, "action"), G(r, "actor"),
            Join(G(r, "note"), Pre("감리 ", G(r, "approver")), Pre("서명 ", G(r, "signed_at")), Pre("출처 ", Approval.SourceLabel(r)), Pre("사슬 ", G(r, "sha").Length > 12 ? G(r, "sha")[..12] : G(r, "sha"))))),
        ("evidence.csv", "근거 원장", r => (G(r, "date"), G(r, "zone_id"), Stage(G(r, "stage")), G(r, "by"),
            Join(G(r, "doc_no"), G(r, "result"), G(r, "file"), Pre("파일 ", G(r, "sha256").Length > 12 ? G(r, "sha256")[..12] : G(r, "sha256"))))),
        ("shore_checks.csv", "동바리 확인", r => (G(r, "timestamp"), G(r, "zone_id"), "동바리 " + G(r, "present"), G(r, "actor"), Photo(r))),
        // F16 동바리 점검 (install_inspection.csv, 총괄 60차 — audit_export.py 와 같음): 시각 = timestamp (옛 설치 검측 줄은 date), 단계 빈칸 = 설치 검측
        ("install_inspection.csv", "동바리 점검", r => (G(r, "timestamp") is { Length: > 0 } ts ? ts : G(r, "date"), G(r, "zone_id"),
            Join(G(r, "stage") is { Length: > 0 } st ? st : "설치 검측", G(r, "item_code"), G(r, "result")), G(r, "inspector"),
            Join(Pre("동바리 ", G(r, "shore_ids")), G(r, "source"), G(r, "photo_path")))),
        // 판정 이력: 시각 = 판정 기준 시각(eval_at), 실제로 적은 시각은 detail "기록 …" (audit_export.py 와 같음, 총괄 11-05 25차)
        ("eval_log.csv", "판정 이력", r => (G(r, "eval_at"), "", "판정", "", Join(G(r, "summary"), Pre("기록 ", (G(r, "logged_at").Length > 16 ? G(r, "logged_at")[..16] : G(r, "logged_at")).Replace('T', ' '))))),
        ("after_check.csv", "해체 후 점검", r => (G(r, "date"), G(r, "zone_id"), Join(G(r, "item_code"), G(r, "result")), G(r, "by"), Photo(r))),
    };

    /// <summary>audit_export.py rows: 시각 순 (같은 시각은 Sources 순서·파일 순서 — 안정 정렬, 글자 순서는 Ordinal)</summary>
    public static List<AuditRow> Rows(string folder)
    {
        var outp = new List<AuditRow>();
        foreach (var (file, label, f) in Sources)
            foreach (var r in Evaluator.Read(folder, file))
            {
                var (when, zone, action, actor, detail) = f(r);
                var line = string.Join("|", r.Values.Select(v => v ?? ""));
                outp.Add(new(when, label, zone, action, actor, detail, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(line))).ToLowerInvariant()[..16]));
            }
        return outp.OrderBy(x => x.When, StringComparer.Ordinal).ToList();
    }

    /// <summary>audit_export.py write_csv: UTF-8 BOM, csv.writer (CRLF·필요할 때만 따옴표)</summary>
    public static void WriteCsv(IEnumerable<AuditRow> rows, string path) =>
        Py.WriteCsv(path, new[] { Cols }.Concat(rows.Select(r => new[] { r.When, r.Source, r.ZoneId, r.Action, r.Actor, r.Detail, r.LineSha })));
}

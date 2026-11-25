// shoring/shore_inspect.py 의 1:1 C# 이식: 동바리 단위 현장 점검 (F16 — 사용자 확정 11-05 "구역 체크리스트와 문제 동바리 표시", 사진 없음).
// 동바리 ID = 구역-R행C열 (좌표로 정함, 0.1 m 로 묶음) · shores.csv · install_inspection.csv 넓힌 기록 · 풀리지 않은 NG (규칙 ⑦ — 설치 검측 NG 면 해체 요청 불가·타설 보류).
using System.Globalization;

namespace RevitOnSite.Engine;

public sealed record InspectRecord(string ZoneId, string Stage, string ItemCode, string Result, List<string> ShoreIds, DateTime T, int I, string Inspector, string Note);

public static class ShoreInspect
{
    public const string File = "install_inspection.csv", Shores = "shores.csv", Install = "설치 검측";
    public static readonly string[] BaseCols = { "zone_id", "item_code", "result", "inspector", "date", "photo_path" };
    public static readonly string[] ExtraCols = { "stage", "shore_ids", "timestamp", "source" };
    public const double GridM = 0.1;   // [가정] 동바리 좌표를 이 간격으로 묶어 행·열을 셈

    /// <summary>단계 → (항목 코드, 항목, 기준, 출처 표시) — shore_inspect.py STAGES 와 같은 순서</summary>
    public static readonly (string Stage, (string Code, string Label, string Criterion, string Source)[] Items)[] Stages =
    {
        (Install, new[]
        {
            ("SHR-B", "수평연결재", "높이 3.5 m 초과 → 2 m 이내마다 2방향", "[확정] 안전보건규칙 제332조의2"),
            ("SHR-S", "이음", "이음 1개 이하 (3개 이상 이어 쓰지 않음)", "[확정] 안전보건규칙 제332조의2"),
            ("SHR-A", "상하 정렬", "아래층 동바리와 같은 위치", "[확정] 해체 가이드라인 4.3"),
            ("SHR-V", "연직도", "시공계획서 허용값 이내", "[입력]"),
            ("SHR-H", "높이·받침(잭) 조정", "조립도 높이·받침 길이", "[입력]"),
            ("SHR-P", "깔판·하부 고정", "조립도대로 고정", "[입력]"),
        }),
        ("일상 점검", new[]
        {
            ("DLY-D", "변형·침하", "작업 전 변형·침하 없음", "[확정] 안전보건규칙 제334조"),
            ("DLY-B", "수평연결재·체결 탈락", "탈락·풀림 없음", "[입력]"),
            ("DLY-L", "하부 지지 상태", "깔판·하층 슬래브 이상 없음", "[입력]"),
            ("DLY-W", "자재 적치", "슬래브 위 과적재 없음", "[입력]"),
        }),
        ("해체 전", new[]
        {
            ("PRE-C", "하부 3개층 동바리 존치", "타설층 포함 3개층 존치", "[확정] KCS 14 20 12:2024 3.3.2(2)"),
            ("PRE-A", "작업 준비·출입 통제", "관리감독자 확인", "[확정] 해체 가이드라인 4.1"),
            ("PRE-F", "필러 동바리 표시", "존치할 동바리 표시", "[입력]"),
        }),
        ("해체 후", new[]
        {
            ("AC-1", "필러·지지 동바리 타설층 포함 하부 3개층 설치", "가이드라인 4.3", "[확정] 해체 가이드라인 4.3"),
            ("AC-2", "각 층 같은 위치(상하 정렬)", "가이드라인 4.3", "[확정] 해체 가이드라인 4.3"),
            ("AC-3", "연직도", "가이드라인 4.3", "[확정] 해체 가이드라인 4.3"),
            ("AC-4", "자재 변형·침하", "가이드라인 4.3", "[확정] 해체 가이드라인 4.3"),
            ("AC-5", "콘크리트 면 결함", "가이드라인 4.3", "[확정] 해체 가이드라인 4.3"),
        }),
    };

    public static string ItemLabel(string code) => Stages.SelectMany(s => s.Items).FirstOrDefault(i => i.Code == code).Label ?? code;

    /// <summary>구역 안 동바리 점 (x, y) m → 같은 순서의 ID `구역-R행C열` (행 = y 남→북, 열 = x 서→동, GridM 로 묶은 고유 좌표 순) — shore_ids</summary>
    public static List<string> ShoreIds(string zoneId, IReadOnlyList<(double X, double Y)> pts)
    {
        var kx = pts.Select(p => Math.Round(p.X / GridM)).ToList();   // Python round() = 짝수 반올림 (Math.Round 기본)
        var ky = pts.Select(p => Math.Round(p.Y / GridM)).ToList();
        var cols = kx.Distinct().Order().ToList();
        var rows = ky.Distinct().Order().ToList();
        return kx.Zip(ky, (a, b) => $"{zoneId}-R{(rows.IndexOf(b) + 1).ToString("00", CultureInfo.InvariantCulture)}C{(cols.IndexOf(a) + 1).ToString("00", CultureInfo.InvariantCulture)}").ToList();
    }

    /// <summary>shores.csv → id → (zone_id, x, y, field_label)</summary>
    public static Dictionary<string, (string ZoneId, double X, double Y, string FieldLabel)> ReadShores(string folder)
    {
        var outp = new Dictionary<string, (string, double, double, string)>();
        foreach (var r in Evaluator.Read(folder, Shores))
            if (!string.IsNullOrWhiteSpace(r.GetValueOrDefault("id"))
                && double.TryParse(r.GetValueOrDefault("x"), NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                && double.TryParse(r.GetValueOrDefault("y"), NumberStyles.Float, CultureInfo.InvariantCulture, out var y))   // x·y 빈칸 줄은 그 동바리만 건너뜀 (코드 검토 2차, shore_inspect.py 같음)
                outp[r["id"]!] = (r.GetValueOrDefault("zone_id") ?? "", x, y, (r.GetValueOrDefault("field_label") ?? "").Trim());
        return outp;
    }

    static string V(IReadOnlyDictionary<string, string?> r, string k) => r.TryGetValue(k, out var v) ? v ?? "" : "";

    /// <summary>install_inspection.csv → 기록 시각 순 (같으면 파일 순). stage 빈칸 = 설치 검측, 시각 = timestamp 또는 date 00:00 — shore_inspect.py records</summary>
    public static List<InspectRecord> Records(string folder, DateTime? at = null)
    {
        var outp = new List<InspectRecord>();
        int i = -1;
        foreach (var r in Evaluator.Read(folder, File))
        {
            i++;
            var ts = V(r, "timestamp").Trim();
            var d = V(r, "date").Trim();
            if (d.Length > 10) d = d[..10];
            if (ts.Length == 0 && d.Length == 0) continue;
            var t = ts.Length > 0 ? Evaluator.FromIso(ts) : DateTime.ParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (at is DateTime a && t.Date > a.Date) continue;   // 61차 ②: 날짜 단위 (판정일까지 — shore_inspect.py records)
            var st = V(r, "stage").Trim();
            outp.Add(new InspectRecord(r["zone_id"] ?? "", st.Length > 0 ? st : Install, V(r, "item_code").Trim(),
                V(r, "result").Trim().ToUpperInvariant() == "OK" ? "OK" : "NG",
                V(r, "shore_ids").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToList(), t, i, V(r, "inspector").Trim(), V(r, "note").Trim()));   // 'A; B' 도 같은 동바리 (코드 검토 2차)
        }
        return outp.OrderBy(x => x.T).ThenBy(x => x.I).ToList();
    }

    /// <summary>풀리지 않은 NG → 구역 → [(항목, 동바리 또는 '*', 처음 NG 시각)] — shore_inspect.py open_ng (같은 항목: ID 없는 OK = 전부 풀림, 동바리별 OK = 그 동바리만)</summary>
    public static Dictionary<string, List<(string Item, string Shore, DateTime T)>> OpenNg(string folder, DateTime? at = null, string? stage = null)
    {
        var st = new Dictionary<string, Dictionary<(string Item, string Shore), DateTime>>();
        var order = new List<string>();
        foreach (var r in Records(folder, at))
        {
            if (stage != null && r.Stage != stage) continue;
            if (!st.TryGetValue(r.ZoneId, out var z)) { st[r.ZoneId] = z = new(); order.Add(r.ZoneId); }
            if (r.Result == "OK")
            {
                foreach (var k in z.Keys.Where(k => k.Item == r.ItemCode && (r.ShoreIds.Count == 0 || r.ShoreIds.Contains(k.Shore))).ToList()) z.Remove(k);
            }
            else
                foreach (var sid in r.ShoreIds.Count > 0 ? r.ShoreIds : new List<string> { "*" })
                    z.TryAdd((r.ItemCode, sid), r.T);
        }
        var outp = new Dictionary<string, List<(string, string, DateTime)>>();
        foreach (var zid in order)
            if (st[zid].Count > 0)
                outp[zid] = st[zid].Select(kv => (kv.Key.Item, kv.Key.Shore, kv.Value)).OrderBy(x => x.Value).ThenBy(x => x.Item, StringComparer.Ordinal).ThenBy(x => x.Shore, StringComparer.Ordinal).ToList();
        return outp;
    }

    /// <summary>'수평연결재 9F-A2-R03C05 외 2 · 이음 (구역 전체)' — shore_inspect.py ng_text</summary>
    public static string NgText(IEnumerable<(string Item, string Shore, DateTime T)> items)
    {
        var by = new List<(string Item, List<string> Shores)>();
        foreach (var (c, s, _) in items)
        {
            var g = by.FirstOrDefault(x => x.Item == c);
            if (g.Item == null) by.Add(g = (c, new List<string>()));
            g.Shores.Add(s);
        }
        return string.Join(" · ", by.Select(g => ItemLabel(g.Item) + " " + (g.Shores.Contains("*") ? "(구역 전체)" : g.Shores[0] + (g.Shores.Count > 1 ? $" 외 {g.Shores.Count - 1}" : ""))));
    }
}

// shoring/evidence.py 의 1:1 C# 이식: 4D 근거 원장 — 해체 절차 단계 서류를 구역·날짜에 묶어 "날짜 D 에 어느 구역이 어느 근거까지 갖췄나".
// 모델은 서명 원본이 아니라 위치·시점 색인이다. 원본은 서명된 서류이고 원장에는 파일 경로와 SHA-256 만 남긴다.
//   evidence.csv           (추가만 하는 원장) ev_id, zone_id, date, stage, doc_no, result, by, x, y, file, sha256 — 2 작업계획서 · 4 해체전점검 · 8 결과보고 등
//   cylinders.csv          → 3 공시체 (현장양생만, test_date)
//   approvals.csv          → 5 검측요청(요청 제출) · 6 감리확인(감리 승인 기록) · 7 해체(해체 완료) — 사슬이 깨졌으면 쓰지 않음
//   install_inspection.csv → 1 설치검측 (항목마다 마지막 결과에 풀리지 않은 NG 없음 — ShoreInspect.OpenNg 날짜 단위, 날짜 = 마지막 기록일, 61차 ①)
//   after_check.csv → 8 결과보고 (해체 후 점검 5항목 모두 OK 인 날, 61차 ⑥)
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RevitOnSite.Engine;

public sealed record EvidenceEntry(string ZoneId, string Date, int Stage, string Result, string By, string Source, string Sha = "");   // Sha = 협력업체 신고 사진 SHA-256 (F15, evidence.py "sha256")

public sealed class ZoneLedger
{
    /// <summary>단계 → 첫 날짜 (YYYY-MM-DD). 넣은 순서 = Python dict 순서</summary>
    public Dictionary<int, string> Dates { get; } = new();
    /// <summary>갖춘 단계 = 1 부터 빈칸 없이 이어진 마지막 단계</summary>
    public int Stage { get; set; }
    public List<string> Warnings { get; } = new();
}

public static class Evidence
{
    public const string Ledger = "evidence.csv";
    public static readonly string[] Fields = { "ev_id", "zone_id", "date", "stage", "doc_no", "result", "by", "x", "y", "file", "sha256" };
    public static readonly (int Stage, string Name)[] Stages =
        { (1, "설치검측"), (2, "작업계획서"), (3, "공시체"), (4, "해체전점검"), (5, "검측요청"), (6, "감리확인"), (7, "해체"), (8, "결과보고") };
    /// <summary>shore_checks.csv source 열 — 협력업체 반장이 층 화면 신고 QR 로 남긴 줄 (F14, evidence.py REPORT_SOURCE · FieldServer /api/report)</summary>
    public const string ReportSource = "협력업체 신고";
    static readonly (string Action, int Stage)[] FromApproval = { ("요청 제출", 5), ("감리 승인 기록", 6), ("해체 완료", 7) };
    /// <summary>[가정] 이 단계 전에 있어야 할 단계 (가이드라인 3.2·4.1·4.3 순서) — evidence.py NEEDS</summary>
    static readonly (int Stage, int[] Needs)[] Needs = { (5, new[] { 2, 3, 4 }), (6, new[] { 5 }), (7, new[] { 6 }), (8, new[] { 7 }) };   // 11-11 G5: 검측요청 전 작업계획서·공시체·해체 전 점검
    /// <summary>1-2: 감리가 서버에서 작업계획서를 확인하면 원장 2 단계에 이 결과로 한 줄 — evidence.py PLAN_OK</summary>
    public const string PlanOk = "감리 계획서 확인 (서버)";

    public static string StageName(int s) => Stages.First(x => x.Stage == s).Name;

    /// <summary>첨부 서류 SHA-256 (없으면 빈 값 — 원장엔 경로만)</summary>
    public static string FileSha(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
        using var f = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
    }

    /// <summary>원장에 한 줄 추가 (고치지 않고 추가만). 단계 번호·입력자 검사 — 거부는 InvalidOperationException (Python ValueError 와 같은 문구)</summary>
    public static Dictionary<string, string> Add(string folder, string zoneId, string day, int stage, string by, string docNo = "", string result = "", string file = "", string x = "", string y = "")
    {
        if (!Stages.Any(s => s.Stage == stage))
            throw new InvalidOperationException($"단계 {stage} 없음 — {{" + string.Join(", ", Stages.Select(s => $"{s.Stage}: '{s.Name}'")) + "}");
        if (by.Trim().Length == 0)
            throw new InvalidOperationException("입력자가 필요합니다");
        var rows = Evaluator.Read(folder, Ledger);
        var r = new Dictionary<string, string>
        {
            ["ev_id"] = "E" + (rows.Count + 1).ToString("D4", CultureInfo.InvariantCulture), ["zone_id"] = zoneId,
            ["date"] = DateOnly.ParseExact(day.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["stage"] = stage.ToString(CultureInfo.InvariantCulture), ["doc_no"] = docNo, ["result"] = result, ["by"] = by.Trim(),
            ["x"] = x, ["y"] = y, ["file"] = file, ["sha256"] = FileSha(file),
        };
        var p = Path.Combine(folder, Ledger);
        var text = new StringBuilder();
        if (!File.Exists(p)) text.Append(Py.CsvLine(Fields));
        text.Append(Py.CsvLine(Fields.Select(k => r[k])));
        File.AppendAllText(p, text.ToString(), new UTF8Encoding(false));   // Python open("a", encoding="utf-8")
        return r;
    }

    /// <summary>모든 출처를 원장 형식으로 (원장 · 설치검측 · 현장양생 공시체 · 승인 기록)</summary>
    public static List<EvidenceEntry> Entries(string folder, DateOnly? day = null)
    {
        string V(Dictionary<string, string?> r, string k) => r.GetValueOrDefault(k) ?? "";
        var outp = Evaluator.Read(folder, Ledger).Select(r => new EvidenceEntry(V(r, "zone_id"), V(r, "date"), Py.Int(r["stage"]), V(r, "result"), V(r, "by"), Ledger)).ToList();
        var cut = day ?? DateOnly.MaxValue;
        var cutT = cut.ToDateTime(TimeOnly.MinValue);
        var ins = new Dictionary<string, List<InspectRecord>>();   // 1 설치검측 (61차 ①): 항목마다 마지막 결과 — NG 뒤 OK 면 풀림 (evidence.py, 다른 단계 줄은 뺌)
        foreach (var r in ShoreInspect.Records(folder, cutT))
            if (r.Stage == ShoreInspect.Install) { if (!ins.TryGetValue(r.ZoneId, out var l)) ins[r.ZoneId] = l = new(); l.Add(r); }
        var ng = ShoreInspect.OpenNg(folder, cutT, ShoreInspect.Install);
        foreach (var zid in ins.Keys.OrderBy(z => ins[z].Min(r => r.I)))   // 구역 = 파일에 처음 나온 순
            if (!ng.ContainsKey(zid))
            {
                var last = ins[zid][^1];
                outp.Add(new(zid, last.T.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 1, "OK", last.Inspector, "install_inspection.csv"));
            }
        // 8 결과보고 = 원장 줄만 (ResultReport 가 결과보고서를 만들 때 한 줄, 11-12 G10 — 해체 후 점검 OK 만으로 자동 8 이던 것 뺌, evidence.py 와 같음)
        foreach (var r in Evaluator.CylRows(folder))   // W1: 타설 묶음 공통 시험 → 묶음의 구역마다
            if (ShoringEngine.FieldCurings.Contains(r.GetValueOrDefault("curing")))
                outp.Add(new(V(r, "zone_id"), V(r, "test_date"), 3, V(r, "curing"), V(r, "entered_by"), "cylinders.csv"));
        if (Approval.Verify(folder).Ok)            // 사슬이 깨진 승인 기록은 근거로 쓰지 않음
            foreach (var r in Approval.ReadLog(folder))
                if (FromApproval.FirstOrDefault(a => a.Action == r.GetValueOrDefault("action")) is { Action: not null } m)
                    outp.Add(new(V(r, "zone_id"), V(r, "timestamp")[..Math.Min(10, V(r, "timestamp").Length)], m.Stage, V(r, "evidence_sha")[..Math.Min(12, V(r, "evidence_sha").Length)], V(r, "actor"), Approval.Log));
        foreach (var r in Evaluator.Read(folder, PourGate.ChecksFile))   // 협력업체 해체 신고 '뺐음' (F14) → 7 해체, 결과 = 실제 시각 (안전관리자 확인 '아니오'는 관측이라 안 셈)
            if (V(r, "source").Trim() == ReportSource && V(r, "present").Trim() == "아니오")
                outp.Add(new(V(r, "zone_id"), V(r, "timestamp")[..Math.Min(10, V(r, "timestamp").Length)], 7, V(r, "timestamp"), V(r, "actor").Trim(), PourGate.ChecksFile,
                    V(r, Photos.ShaCol).Trim()));
        return outp;
    }

    /// <summary>날짜 day 까지의 근거 → {구역: 단계별 첫 날짜·갖춘 단계·순서 누락 경고} (구역 = 처음 나온 순서)</summary>
    /// <summary>evidence.py removal_watch: 무단 해체 관측 경고 (총괄 P1-2 — 화정 2022 1심: 원인 = 하부 3개층 동바리 무단 해체) → {구역: 문구}.
    /// 구역에 '동바리 없음' 관측(휴대폰 동바리 확인 '아니오' 또는 승인 기록 '해체 완료')이 있는데 그 시각까지 그 구역 '감리 승인 기록'이 없으면
    /// 그 첫 관측을 경고. 그 뒤 '재받침' 기록 다음에 '예' 확인이 오면 해제. day 날짜까지의 기록만, 사슬이 깨진 승인 기록은 안 씀(Entries 와 같음).
    /// 판정·결론은 바꾸지 않음. 문구는 '의심'·'확인 필요'까지</summary>
    public static SortedDictionary<string, string> RemovalWatch(string folder, DateOnly day)
    {
        var by = new Dictionary<string, List<(DateTime T, int Order, string Kind, string Actor)>>();   // 종류 "6"·"7"·"8"·"예"·"아니오"
        void Put(string z, (DateTime, int, string, string) e) { if (!by.TryGetValue(z, out var l)) by[z] = l = new(); l.Add(e); }
        if (Approval.Verify(folder).Ok)
            foreach (var r in Approval.ReadLog(folder))
                if (Approval.CodeOf(r.GetValueOrDefault("action")) is int k)
                    Put(r.GetValueOrDefault("zone_id") ?? "", (DateTime.ParseExact(r["timestamp"]!, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                        k == 6 ? 0 : 1, k.ToString(CultureInfo.InvariantCulture), r.GetValueOrDefault("actor") ?? ""));
        foreach (var r in Evaluator.Read(folder, PourGate.ChecksFile))
        {
            var p = (r.GetValueOrDefault("present") ?? "").Trim();
            if (p is "예" or "아니오")
                Put(r.GetValueOrDefault("zone_id") ?? "", (Evaluator.Ts(r["timestamp"]), 1, p, (r.GetValueOrDefault("actor") ?? "").Trim()));
        }
        var outp = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (zid, es) in by)
        {
            bool approved = false, reshored = false;
            (DateTime T, string Actor)? flag = null;
            foreach (var (t, _, k, actor) in es.OrderBy(e => e.T).ThenBy(e => e.Order))   // 같은 시각이면 감리 승인 기록 먼저 (안정 정렬)
            {
                if (DateOnly.FromDateTime(t) > day) break;
                if (k == "6") approved = true;
                else if (k == "8") reshored = flag is not null;
                else if (k is "7" or "아니오")
                {
                    if (!approved && flag is null) flag = (t, actor);
                    reshored = false;
                }
                else if (flag is not null && reshored) (flag, reshored) = (null, false);   // 재받침 기록 뒤 '예' 확인 → 해제
            }
            if (flag is { } f)
                outp[zid] = $"승인 기록 없이 동바리 없음 관측 — {zid} {f.T.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)} {f.Actor}".TrimEnd();
        }
        return outp;
    }

    /// <summary>evidence.py removal_order (35차 총괄): 구역의 해체 기록(근거 7)이 바로 아래층(층 순서)에서 평면이 겹치고 타설된 구역의 해체 기록보다 먼저면
    /// (그 날짜까지 아래층 해체 기록 없음) 경고 — 층 순서. 해체는 아래층부터 [가정]. 판정은 그대로</summary>
    public static List<KeyValuePair<string, string>> RemovalOrder(string folder, DateOnly day, List<EvidenceEntry>? ents = null)
    {
        var d = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var first7 = new Dictionary<string, string>();
        foreach (var x in ents ?? Entries(folder))
        {
            var xd = x.Date[..Math.Min(10, x.Date.Length)];
            if (x.Stage == 7 && string.CompareOrdinal(xd, d) <= 0 && (!first7.TryGetValue(x.ZoneId, out var have) || string.CompareOrdinal(xd, have) < 0)) first7[x.ZoneId] = xd;
        }
        var outp = new List<KeyValuePair<string, string>>();
        if (first7.Count == 0) return outp;
        var zones = Evaluator.Read(folder, "zones.csv").GroupBy(r => r["zone_id"]!).ToDictionary(g => g.Key, g => g.Last());   // Python dict: 같은 ID 는 마지막 줄
        var floorOf = zones.ToDictionary(kv => kv.Key, kv => Py.Int(kv.Value["floor"]));
        var rect = zones.ToDictionary(kv => kv.Key, kv => new[] { "x0", "y0", "x1", "y1" }.Select(k => Py.Float(kv.Value[k])).ToArray());
        static string H10(string s) => s.Length > 10 ? s[..10] : s;
        var poured = Evaluator.Read(folder, "pours.csv").Where(p => string.CompareOrdinal(H10(p.GetValueOrDefault("pour_date") ?? ""), d) <= 0).Select(p => p["zone_id"]!).ToHashSet();
        var order = floorOf.Values.Distinct().Order().ToList();
        static bool Over(double[] a, double[] b) => Math.Min(a[2], b[2]) > Math.Max(a[0], b[0]) && Math.Min(a[3], b[3]) > Math.Max(a[1], b[1]);   // evaluate.overlaps
        foreach (var z in first7.Keys.Where(floorOf.ContainsKey).OrderBy(z => z, Evaluator.FloorOrder(folder)))
        {
            int i = order.IndexOf(floorOf[z]);
            var t = first7[z];
            var low = zones.Keys.Where(y => i > 0 && floorOf[y] == order[i - 1] && poured.Contains(y) && Over(rect[y], rect[z])
                && !(first7.TryGetValue(y, out var ty) && string.CompareOrdinal(ty, t) <= 0)).OrderBy(y => y, StringComparer.Ordinal).ToList();
            if (low.Count > 0)
                outp.Add(new(z, $"{z}: 위층 동바리가 아래층보다 먼저 해체 — 확인 필요 ({t[5..]} 해체, 아래층 {string.Join(", ", low.Take(3))}{(low.Count > 3 ? " 외" : "")} 해체 기록 없음)"));
        }
        return outp;
    }

    public static Dictionary<string, ZoneLedger> LedgerAt(string folder, DateOnly day)
    {
        var res = new Dictionary<string, ZoneLedger>();
        var ents = Entries(folder, day);
        foreach (var x in ents)
        {
            if (DateOnly.ParseExact(x.Date[..Math.Min(10, x.Date.Length)], "yyyy-MM-dd", CultureInfo.InvariantCulture) > day)
                continue;
            if (!res.TryGetValue(x.ZoneId, out var z)) res[x.ZoneId] = z = new ZoneLedger();
            if (!z.Dates.TryGetValue(x.Stage, out var have) || string.CompareOrdinal(x.Date, have) < 0)
                z.Dates[x.Stage] = x.Date[..Math.Min(10, x.Date.Length)];
        }
        foreach (var (zid, z) in res)
        {
            int k = 0;
            while (z.Dates.ContainsKey(k + 1)) k++;
            z.Stage = k;
            foreach (var (s, need) in Needs)
                if (z.Dates.TryGetValue(s, out var ds))
                    foreach (var n in need)
                        if (!z.Dates.TryGetValue(n, out var dn) || string.CompareOrdinal(dn, ds) > 0)
                            z.Warnings.Add($"{zid}: {StageName(s)}({ds}) 전에 {StageName(n)} 기록 없음");
        }
        foreach (var (zid, w) in RemovalWatch(folder, day).Concat(SignatureWatch(folder, day))
                     .Concat(AfterCheck.Warnings(folder, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                     .Concat(InstallAfterPour(folder, day))
                     .Concat(RemovalOrder(folder, day, ents)))   // 무단 해체 관측·서명 전 해체 관측·해체 후 점검 미완 (F3)·설치 검측이 타설 뒤·위층 먼저 해체 (경고만)
        {
            if (!res.TryGetValue(zid, out var z)) res[zid] = z = new ZoneLedger();
            z.Warnings.Add(w);
        }
        return res;
    }

    /// <summary>evidence.py install_after_pour: 설치 검측 마지막 날짜가 그 구역 타설일 뒤면 경고 (총괄 11-05 25차, 판정은 안 바꿈).
    /// day 날짜까지의 타설·설치 검측 기록만. 순서 = install_inspection.csv 에 처음 나온 구역 순 (Python dict)</summary>
    public static List<KeyValuePair<string, string>> InstallAfterPour(string folder, DateOnly day)
    {
        var iso = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var pour = new Dictionary<string, string>();
        foreach (var p in Evaluator.Read(folder, "pours.csv"))
            if (string.CompareOrdinal(p.GetValueOrDefault("pour_date") ?? "", iso) <= 0) pour[p["zone_id"]!] = p["pour_date"]!;
        var last = new Dictionary<string, string>();
        foreach (var r in Evaluator.InstallRows(folder))
        {
            var z = (r.GetValueOrDefault("zone_id") ?? "").Trim();
            var d = (r.GetValueOrDefault("date") ?? "").Trim();
            if (d.Length > 10) d = d[..10];
            if (pour.ContainsKey(z) && d.Length > 0 && string.CompareOrdinal(d, iso) <= 0 && string.CompareOrdinal(d, last.GetValueOrDefault(z, "")) > 0) last[z] = d;
        }
        static string Md(string s) => s.Length > 5 ? s[5..] : "";
        return last.Where(kv => string.CompareOrdinal(kv.Value, pour[kv.Key]) > 0)
            .Select(kv => new KeyValuePair<string, string>(kv.Key, $"{kv.Key}: 설치 검측({Md(kv.Value)})이 타설({Md(pour[kv.Key])}) 뒤 — 기록 확인 필요")).ToList();
    }

    /// <summary>evidence.py signature_watch: 서명 전 해체 관측 (총괄 11-05) — '감리 승인 기록'의 서명 일시(signed_at)보다 앞서 같은 구역에 동바리 확인 '아니오'가 있으면 경고.
    /// 구역마다 가장 이른 서명과 그 전 가장 이른 '아니오'. 서명 일시가 날짜만이면 그날 23:59 로 봄. day 날짜까지의 기록만, 사슬이 깨진 승인 기록은 안 씀. 순서 = 관측 시각 순</summary>
    public static List<KeyValuePair<string, string>> SignatureWatch(string folder, DateOnly day)
    {
        var outp = new List<KeyValuePair<string, string>>();
        if (!Approval.Verify(folder).Ok) return outp;
        string ds = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var signed = new Dictionary<string, DateTime>();
        foreach (var r in Approval.ReadLog(folder))
        {
            var s = (r.GetValueOrDefault("signed_at") ?? "").Trim();
            var ts = r.GetValueOrDefault("timestamp") ?? "";
            if (r.GetValueOrDefault("action") != "감리 승인 기록" || s.Length == 0 || string.CompareOrdinal(ts[..Math.Min(10, ts.Length)], ds) > 0) continue;
            var t = DateTime.ParseExact(s.Contains('T') ? s : s + "T23:59", new[] { "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None);
            var z = r.GetValueOrDefault("zone_id") ?? "";
            signed[z] = signed.TryGetValue(z, out var have) && have < t ? have : t;
        }
        var obs = Evaluator.Read(folder, PourGate.ChecksFile)
            .Where(r => (r.GetValueOrDefault("present") ?? "").Trim() == "아니오" && signed.ContainsKey(r.GetValueOrDefault("zone_id") ?? ""))
            .Select(r => (T: Evaluator.Ts(r["timestamp"]), Z: r["zone_id"]!, A: (r.GetValueOrDefault("actor") ?? "").Trim()))
            .OrderBy(o => o.T).ThenBy(o => o.Z, StringComparer.Ordinal).ThenBy(o => o.A, StringComparer.Ordinal);
        var seen = new HashSet<string>();
        foreach (var (t, z, a) in obs)
            if (DateOnly.FromDateTime(t) <= day && t < signed[z] && seen.Add(z))
                outp.Add(new(z, $"서명 전 해체 관측 — {z} 동바리 없음 {t.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)} {a} · 감리 서명 {signed[z].ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)}".Replace("  ", " ")));
        return outp;
    }
}

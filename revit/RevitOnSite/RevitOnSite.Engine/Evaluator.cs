// shoring/evaluate.py 의 1:1 C# 이식: CSV → 구역별 동바리 해체 판정표.
//
// 입력 폴더 (UTF-8, 첫 줄 열 이름):
//   zones.csv      zone_id, floor, x0, y0, x1, y1
//   pours.csv      zone_id, pour_date(YYYY-MM-DD), pour_time(HH:MM — 비면 23:59 로 보아 재령을 짧게, 보수 측), mix, fck  (구역마다 한 줄) [, pour_id 타설 묶음 — v6.2 W1]
//   sensors.csv    sensor_id, floor, x, y, timestamp(YYYY-MM-DDTHH:MM), temp_c     (없어도 됨 → 기상 예측)
//   cylinders.csv  zone_id, test_date, curing, f1, f2, f3                          (없어도 됨 → "공시체 시험 필요") [, pour_id — zone_id 가 비면 묶음 공통, CylRows]
//   weather.csv    date, avgTa                                                     (없어도 됨 → 평년 기온)
//   sensor_strength.csv sensor_id, floor, x, y, timestamp, mpa  무선 강도 센서(장비가 성숙도로 계산한 MPa) (없어도 됨)
// 위치 근거 = 온도 센서 IDW 구역 최소와 강도 센서 IDW 구역 최소 중 낮은 값 (둘 다 없으면 기상 예측).
using System.Globalization;
using System.Text;
using Row = System.Collections.Generic.Dictionary<string, string?>;

namespace RevitOnSite.Engine;

/// <summary>evaluate.py gate_details 한 구역: 위로 연속 타설된 층 수, 최상층까지 타설, 맨 위 슬래브가 24 h 안(지지층 검토 때만),
/// 지지층 (층, 그 층 최소 구역, 강도 MPa — 반올림 안 함), 하중비, 지지 슬래브 기준 (34차 A — 판정 근거 창 비교 줄)</summary>
public sealed record GateDetail(int Above, bool Topped, bool? Fresh, List<(int Floor, string Zone, double Mpa)> Supports, double? Ratio, double? Lim);

/// <summary>판정표 한 행 = evaluate.py 출력 dict (숫자는 Python 과 같은 자리에서 반올림된 값).</summary>
public sealed class ZoneRow
{
    public required string ZoneId { get; init; }
    public int Floor { get; init; }
    public required string PourDate { get; init; }           // pours.csv 원문
    public double AgeDays { get; init; }                     // 소수 1자리
    public required string Mix { get; init; }
    public double Fck { get; init; }
    public double Threshold { get; init; }
    public double? FCyl { get; init; }                       // 소수 1자리, 공시체 없으면 null
    public string CylTestDate { get; init; } = "";
    public string CylCuring { get; init; } = "";
    public double FEstMin { get; init; }                     // 소수 1자리
    public (double X, double Y)? FEstXy { get; init; }       // 센서 IDW 최소 위치 (m), 기상 예측이면 null
    public required string EstSource { get; init; }
    public required string LevelGate { get; init; }          // "OK" / "OK(최상부)" / "NG" / "NG(지지층)"
    public required string Decision { get; init; }           // "해체 승인 요청 가능" / "존치 …" / "공시체 시험 필요"
    public required string Reason { get; init; }
    public DateOnly PredictedDue { get; init; }
    public string EarlyForm { get; init; } = "";             // 판넬 조기 탈형 (evaluate.py early_form) — 동바리 해체 판정과 별개

    public static readonly string[] CsvHeader =
    {
        "zone_id", "floor", "pour_date", "age_days", "mix", "fck", "threshold", "f_cyl", "cyl_test_date", "cyl_curing",
        "f_est_min", "f_est_xy", "est_source", "level_gate", "decision", "reason", "predicted_due", "early_form",
    };

    /// <summary>Python csv 모듈이 쓰는 글자 그대로 (float → repr, None → "", tuple → "(x, y)").</summary>
    public string[] CsvValues() => new[]
    {
        ZoneId, Floor.ToString(Py.Inv), PourDate, Py.Repr(AgeDays), Mix, Py.Repr(Fck), Py.Repr(Threshold),
        FCyl is { } c ? Py.Repr(c) : "", CylTestDate, CylCuring, Py.Repr(FEstMin), Evaluator.XyText(FEstXy), EstSource,
        LevelGate, Decision, Reason, PredictedDue.ToString("yyyy-MM-dd", Py.Inv), EarlyForm,
    };
}

/// <summary>한중 초기양생 경고 한 건 = evaluate.py cold_checks() 의 dict (판정은 바꾸지 않음)</summary>
public sealed record ColdWarning(string ZoneId, int Floor, string Kind, string At, string SensorId, double TempC, string Text);

/// <summary>공시체–센서 교차 검사 경고 한 건 = evaluate.py cross_checks() 의 dict (판정은 바꾸지 않음)</summary>
public sealed record CrossWarning(string ZoneId, int Floor, string Kind, double FCyl, double FEst, double Diff, string Text);

/// <summary>evaluate.py input_checks() 한 줄 (감사 A-29) — kind "fck 확인" / "공시체 편차". 판정은 안 바꿈</summary>
public sealed record InputWarning(string ZoneId, int Floor, string Kind, string Text);

public static class Evaluator
{
    static readonly string[] IsoFormats =   // shortcut: Python fromisoformat 의 흔한 형태만 (시간대·기본형 20261126 제외)
    {
        "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd'T'HH",
        "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd",
    };

    internal static DateTime FromIso(string? s) => DateTime.ParseExact(s!, IsoFormats, Py.Inv, DateTimeStyles.None);

    static readonly string[] ZonedFormats = IsoFormats.Where(f => f.Contains('H')).Select(f => f + "K").ToArray();

    /// <summary>evaluate.py ts(): 센서 시각 → 현지(KST) 시각. 로거가 '2026-12-31T10:00+09:00'·'…Z' 처럼 내보내도 읽는다 (감사 A-15)</summary>
    internal static DateTime Ts(string? s)
    {
        s = s!.Trim();
        if (DateTime.TryParseExact(s, IsoFormats, Py.Inv, DateTimeStyles.None, out var t)) return t;
        return DateTimeOffset.ParseExact(s, ZonedFormats, Py.Inv, DateTimeStyles.None).ToOffset(TimeSpan.FromHours(9)).DateTime;
    }

    /// <summary>evaluate.py @checked: CSV 값 오류(빈 숫자 칸 등)를 입력 오류 문구로 — 현장 입력 서버가 통째로 500 이 되지 않게 (감사 A-15)</summary>
    /// <summary>evaluate.py floor_order: 사람이 읽는 결과물의 구역 순서 (총괄 11-05) — 층 번호 → 구역 ID(Ordinal), zones.csv 에 없는 구역은 맨 뒤 (ID 순)</summary>
    public static IComparer<string> FloorOrder(string folder)
    {
        var fl = new Dictionary<string, int>();
        foreach (var z in Read(folder, "zones.csv"))
            if (z.GetValueOrDefault("zone_id") is string id && int.TryParse(z.GetValueOrDefault("floor"), NumberStyles.Integer, Py.Inv, out var f)) fl[id] = f;
        return Comparer<string>.Create((a, b) =>
        {
            bool ha = fl.TryGetValue(a, out var fa), hb = fl.TryGetValue(b, out var fb);
            if (ha != hb) return ha ? -1 : 1;
            if (ha && fa != fb) return fa.CompareTo(fb);
            return string.CompareOrdinal(a, b);
        });
    }


    internal static T Checked<T>(Func<T> f)
    {
        try { return f(); }
        catch (Exception x) when (x is FormatException or KeyNotFoundException or InvalidCastException or ArgumentNullException or OverflowException)
        {
            throw new InvalidDataException($"입력 CSV 값을 읽을 수 없음 ({x.GetType().Name}: {x.Message}) — 빈 숫자 칸·글자·날짜 형식을 확인하세요");
        }
    }

    internal static string XyText((double X, double Y)? xy) => xy is { } v ? $"({Py.Repr(v.X)}, {Py.Repr(v.Y)})" : "";

    /// <summary>폴더의 CSV 하나 → 행 목록 (csv.DictReader 와 같게: 빈 줄 건너뜀, 모자란 칸은 null). 파일 없으면 빈 목록.</summary>
    /// <summary>evaluate.py install_rows: install_inspection.csv 중 설치 검측 줄만 (stage 빈칸 = 예전 줄). F16 일상 점검·해체 전·해체 후 줄은 뺌</summary>
    public static List<Row> InstallRows(string folder) =>
        Read(folder, "install_inspection.csv").Where(r => (r.GetValueOrDefault("stage") ?? "").Trim() is "" or "설치 검측").ToList();

    static readonly System.Text.RegularExpressions.Regex LotTag = new(@"공시체 \(타설 (.+?) · (\d+)구역 공통\)");   // W1 사유 태그 (evaluate.py LOT_TAG)

    /// <summary>evaluate.py cyl_rows (v6.2 W1 공시체 타설 로트): zone_id 가 비고 pour_id 가 있는 줄 = pours.csv 에서 그 pour_id 를 가진 모든 구역에 같은 시험
    /// (줄마다 cyl_lot = "P-9F-1 · 6구역 공통"). 같은 구역·같은 시험일에 구역 자기 줄이 있으면 자기 줄만. 자기 줄은 cyl_lot 빈칸. 없는 묶음·빈 줄은 뺌</summary>
    public static List<Row> CylRows(string folder)
    {
        var rows = Read(folder, "cylinders.csv");
        var lots = new Dictionary<string, List<string>>();
        foreach (var p in Read(folder, "pours.csv"))
        {
            var lot = (p.GetValueOrDefault("pour_id") ?? "").Trim();
            if (lot.Length == 0) continue;
            if (!lots.TryGetValue(lot, out var l)) lots[lot] = l = new();
            l.Add(p["zone_id"]!);
        }
        static bool Has(Row r, string k) => !string.IsNullOrWhiteSpace(r.GetValueOrDefault(k));
        var outp = rows.Where(r => Has(r, "zone_id")).Select(r => new Row(r) { ["cyl_lot"] = "" }).ToList();
        var have = outp.Select(r => (r["zone_id"], r.GetValueOrDefault("test_date"))).ToHashSet();
        foreach (var r in rows)
        {
            var lot = (r.GetValueOrDefault("pour_id") ?? "").Trim();
            if (Has(r, "zone_id") || lot.Length == 0) continue;
            var zs = lots.GetValueOrDefault(lot) ?? new();
            foreach (var z in zs)
                if (!have.Contains((z, r.GetValueOrDefault("test_date")))) outp.Add(new Row(r) { ["zone_id"] = z, ["cyl_lot"] = $"{lot} · {zs.Count}구역 공통" });
        }
        return outp;
    }

    /// <summary>evaluate.py lot_label: 판정 사유 → '타설 P-9F-1 · 6구역 공통' (공시체가 타설 묶음 공통 시험일 때, 아니면 빈칸)</summary>
    public static string LotLabel(string? reason) => LotTag.Match(reason ?? "") is { Success: true } m ? $"타설 {m.Groups[1].Value} · {m.Groups[2].Value}구역 공통" : "";

    public static List<Row> Read(string folder, string name)
    {
        var p = Path.Combine(folder, name);
        if (!File.Exists(p))
            return new();
        List<List<string>> recs;
        try { recs = ParseCsv(Py.ReadText(p, Py.Utf8Strict)); }   // 공유 읽기 — 현장에서 Excel 로 열어 둔 CSV 도 판정
        catch (System.Text.DecoderFallbackException)   // 코드 검토 2차: 영문 "Unable to translate bytes…" 대신
        { throw new InvalidDataException($"{name} 가 UTF-8 이 아닙니다 — Excel 이면 'CSV UTF-8(쉼표로 분리)' 형식으로 다시 저장하세요"); }
        if (recs.Count == 0)
            return new();
        var head = recs[0];
        var rows = recs.Skip(1).Where(rec => rec.Count > 0).Select(rec =>
        {
            var r = new Row();
            for (int i = 0; i < head.Count; i++)
                r[head[i]] = i < rec.Count ? rec[i] : null;
            return r;
        }).ToList();
        if (!Voidable.Contains(name)) return rows;
        var outp = new List<Row>();
        foreach (var r in rows)
        {
            if (!(r.GetValueOrDefault("client_id") ?? "").StartsWith(Void, StringComparison.Ordinal)) { outp.Add(r); continue; }
            var k = DataKey(r);
            for (int i = outp.Count - 1; i >= 0; i--)   // 가장 최근의 같은 줄 하나
                if (DataKey(outp[i]) == k) { outp.RemoveAt(i); break; }
        }
        return outp;
    }

    // evaluate.py VOID·read·void_row (11-12 2-1 기록 취소): client_id 가 void: 로 시작하는 줄 = 앞의 같은 자료 줄 하나를 지움 (추가만 원칙)
    public const string Void = "void:";
    public static readonly string[] Voidable = { "pours.csv", "cylinders.csv" };
    static readonly string[] Meta = { "entered_by", "entered_at", "device", "note", "client_id" };
    static string DataKey(Row r) => string.Join("\u001f", r.Where(kv => !Meta.Contains(kv.Key)).Select(kv => kv.Key + "\u001e" + (kv.Value ?? "")));

    /// <summary>evaluate.void_row: 기록 취소 줄을 이어 쓴다 (원래 줄 그대로) — 자료 열 = 원래 줄, note '[취소] 사유', client_id void:시각. 타설은 요청·승인 기록 있는 구역이면 거부</summary>
    public static void VoidRow(string folder, string name, Row row, string reason, string actor, DateTime now)
    {
        reason = (reason ?? "").Trim(); actor = (actor ?? "").Trim();
        if (!Voidable.Contains(name)) throw new ArgumentException($"{name} 은(는) 취소할 수 없습니다 — {string.Join(" · ", Voidable)} 만");
        if (reason.Length is < 1 or > 100 || actor.Length == 0) throw new ArgumentException("취소 사유(1–100자)와 입력자를 넣으세요");
        var k = DataKey(row);
        if (!Read(folder, name).Any(r => DataKey(r) == k)) throw new ArgumentException("취소할 기록이 없습니다 — 이미 취소했거나 파일이 바뀌었습니다");
        var zid = row.GetValueOrDefault("zone_id") ?? "";
        if (name == "pours.csv" && Read(folder, "approvals.csv").Any(r => r.GetValueOrDefault("zone_id") == zid))
            throw new ArgumentException($"{zid} 는 요청·승인 기록이 있어 타설 기록을 취소할 수 없습니다 — 감리와 협의해 정정 문서로");
        var p = Path.Combine(folder, name);
        var head = ParseCsv(Py.ReadText(p, Py.Utf8Strict)).FirstOrDefault() ?? new();
        if (!head.Contains("note") || !head.Contains("client_id")) throw new ArgumentException($"{name} 첫 줄에 note·client_id 열이 없습니다 — 현장 입력 한 번 뒤 다시");
        var meta = new Dictionary<string, string> { ["entered_by"] = actor, ["entered_at"] = now.ToString("yyyy-MM-dd'T'HH:mm:ss", Py.Inv), ["device"] = "",
            ["note"] = "[취소] " + reason, ["client_id"] = Void + now.ToString("yyyyMMddHHmmss", Py.Inv) };
        var data = head.Where(c => !Meta.Contains(c)).ToArray();
        var line = head.ToDictionary(c => c, c => meta.TryGetValue(c, out var m) ? m : row.GetValueOrDefault(c) ?? "");
        FieldServer.AppendRows(p, data, new() { line });
    }

    /// <summary>csv.reader (excel 방언): 쉼표, 큰따옴표("" = "), 따옴표 안 줄바꿈 허용, 빈 줄 = 빈 행.</summary>
    internal static List<List<string>> ParseCsv(string text)
    {
        var recs = new List<List<string>>();
        var rec = new List<string>();
        var f = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c != '"') f.Append(c);
                else if (i + 1 < text.Length && text[i + 1] == '"') { f.Append('"'); i++; }
                else quoted = false;
            }
            else if (c == '"' && f.Length == 0) quoted = any = true;
            else if (c == ',') { rec.Add(f.ToString()); f.Clear(); any = true; }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                if (any || f.Length > 0) rec.Add(f.ToString());
                recs.Add(rec);
                rec = new(); f.Clear(); any = false;
            }
            else { f.Append(c); any = true; }
        }
        if (any || f.Length > 0) { rec.Add(f.ToString()); recs.Add(rec); }
        return recs;
    }

    // ── 큰 자료 캐시 (감사 A-20, evaluate.py memo·_Sensors 와 같은 규칙: 센서 26만 줄에서 휴대폰 저장 1건 10 s)
    // 입력 파일 열쇠 = (크기, 수정 시각) + 1 MB 미만 파일은 내용 SHA-1. 결과는 그대로 (판정 규칙 안 바꿈) — ZoneRow 는 init 전용이라 목록만 새로.
    // shortcut: 1 MB 이상 파일을 같은 크기로 같은 시각 틱 안에 고쳐 쓰면 못 알아챔 (evaluate.py 와 같음)
    static readonly string[] Inputs = { "zones.csv", "pours.csv", "sensors.csv", "weather.csv", "cylinders.csv", "sensor_strength.csv", "options.json", "install_inspection.csv", "approvals.csv", "shore_checks.csv" };   // 승인·신고 = 11-11 재받침 표시   // 설치 검측 = F16 규칙 ⑦
    const long SmallFile = 1 << 20;
    static readonly Dictionary<string, object> MemoCache = new();
    static readonly Dictionary<string, SensorIndex> SensorCache = new();
    static readonly object MemoLock = new();

    /// <summary>evaluate.py file_key: 입력 파일 열쇠 (없으면 "-")</summary>
    public static string FileKey(string folder, string name)
    {
        var p = Path.Combine(folder, name);
        var fi = new FileInfo(p);
        if (!fi.Exists) return "-";
        if (fi.Length >= SmallFile) return $"{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
        try { return $"{fi.Length}|{fi.LastWriteTimeUtc.Ticks}|{Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Py.ReadBytes(p)))}"; }
        catch (IOException) { return "?" + Guid.NewGuid().ToString("N"); }   // 못 읽으면 캐시 안 씀 (읽기가 다시 실패해 오류 문구)
    }

    /// <summary>evaluate.py memo: 같은 인자·같은 입력 파일이면 앞 계산 결과 (오류는 캐시 안 함)</summary>
    static T Memo<T>(string fn, string folder, string args, IEnumerable<string> files, Func<T> compute) where T : class
    {
        var key = string.Join("\u0001", new[] { fn, Path.GetFullPath(folder), args }.Concat(files.Select(f => FileKey(folder, f))));
        lock (MemoLock) if (MemoCache.TryGetValue(key, out var hit)) return (T)hit;
        var v = compute();
        lock (MemoLock)
        {
            if (MemoCache.Count >= 256) MemoCache.Clear();
            MemoCache[key] = v;
        }
        return v;
    }

    /// <summary>evaluate.py _Sensors: sensors.csv 를 한 번만 읽고 층·센서별 묶음·시각 순 (시각, 온도)·등가재령을 한 번만 — 파일이 같으면 다음 판정·경고도 다시 씀.
    /// 묶음 순서·값·계산 순서는 구역마다 다시 읽던 것과 같음 (판정표 바이트 그대로)</summary>
    internal sealed class SensorIndex(string folder)
    {
        readonly List<Row> rows = Read(folder, "sensors.csv");
        Dictionary<int, Dictionary<string, List<Row>>>? byFloor;
        Dictionary<string, List<Row>>? bySid;
        readonly Dictionary<string, List<(DateTime T, double V)>> pts = new();
        readonly Dictionary<(int, string, DateTime, DateTime, double, string), double> te = new();
        readonly object gate = new();

        /// <summary>{센서 ID: 그 층 줄들} — 파일에 처음 나온 순서</summary>
        public Dictionary<string, List<Row>> Floor(int fl)
        {
            lock (gate)
            {
                if (byFloor is null)
                {
                    byFloor = new();
                    foreach (var r in rows)
                    {
                        int f = Py.Int(r["floor"]);
                        if (!byFloor.TryGetValue(f, out var d)) byFloor[f] = d = new();
                        if (!d.TryGetValue(r["sensor_id"]!, out var l)) d[r["sensor_id"]!] = l = new();
                        l.Add(r);
                    }
                }
                return byFloor.TryGetValue(fl, out var x) ? x : new();
            }
        }

        /// <summary>{센서 ID: 모든 줄} (층 무관, ColdChecks)</summary>
        public Dictionary<string, List<Row>> BySid()
        {
            lock (gate)
            {
                if (bySid is null)
                {
                    bySid = new();
                    foreach (var r in rows)
                    {
                        if (!bySid.TryGetValue(r["sensor_id"]!, out var l)) bySid[r["sensor_id"]!] = l = new();
                        l.Add(r);
                    }
                }
                return bySid;
            }
        }

        public List<(DateTime T, double V)> Pts(string key, List<Row> rs)
        {
            lock (gate)
            {
                if (!pts.TryGetValue(key, out var p))
                    pts[key] = p = rs.Select(r => (T: Ts(r["timestamp"]), V: Py.Float(r["temp_c"]))).OrderBy(q => q.T).ThenBy(q => q.V).ToList();
                return p;
            }
        }

        public double Te(int fl, string sid, DateTime start, DateTime at, double ea, Func<DateOnly, double> air, string airKey)
        {
            var k = (fl, sid, start, at, ea, airKey);
            lock (gate) if (te.TryGetValue(k, out var v)) return v;
            var r = TeFromPoints(Pts($"f|{fl}|{sid}", Floor(fl)[sid]), start, at, ea, air);
            lock (gate) te[k] = r;
            return r;
        }

        public double Stale(int fl, string sid, DateTime at)
        {
            double d = (at - Pts($"f|{fl}|{sid}", Floor(fl)[sid])[^1].T).TotalSeconds / 86400;
            return d * 24 > MaxGapH ? d : 0.0;
        }
    }

    internal static SensorIndex SensorsOf(string folder)
    {
        var key = Path.GetFullPath(folder) + "|" + FileKey(folder, "sensors.csv");
        lock (MemoLock)
        {
            if (!SensorCache.TryGetValue(key, out var s))
            {
                if (SensorCache.Count >= 4) SensorCache.Clear();
                SensorCache[key] = s = new SensorIndex(folder);
            }
            return s;
        }
    }

    /// <summary>[가정] 센서 기록 사이가 이보다 길면 끊김으로 보고 그 뒤는 기상 온도로 채움 (A-04: 수화열 35 ℃ 가 몇 주 이어지는 거짓 안전) — evaluate.py MAX_GAP_H</summary>
    public const double MaxGapH = 6.0;

    /// <summary>센서 온도 기록(시각 순) → start~at 등가재령(일). 구간 온도 = 앞 측정값, 단 MaxGapH 넘는 공백은
    /// MaxGapH 까지만 앞 측정값, 나머지는 air(날짜) 기상 온도(양생 하한 없음). air 없으면 평년 기온.</summary>
    public static double SensorTe(IEnumerable<Row> rows, DateTime start, DateTime at, double ea, Func<DateOnly, double>? air = null)
    {
        return TeFromPoints(rows.Select(r => (T: Ts(r["timestamp"]), V: Py.Float(r["temp_c"]))).OrderBy(q => q.T).ThenBy(q => q.V).ToList(), start, at, ea, air);
    }

    /// <summary>evaluate.py te_from_points: SensorTe 의 계산 (시각 순 (시각, 온도) 를 받음 — SensorIndex 가 한 번 푼 것을 다시 씀)</summary>
    static double TeFromPoints(List<(DateTime T, double V)> sorted, DateTime start, DateTime at, double ea, Func<DateOnly, double>? air = null)
    {
        air ??= ShoringEngine.AirTemp;
        var pts = sorted.Where(q => q.T <= at).ToList();
        double te = 0.0;
        var gap = TimeSpan.FromHours(MaxGapH);
        for (int i = 0; i < pts.Count; i++)
        {
            var (t0, T0) = pts[i];
            var t1 = i + 1 < pts.Count ? pts[i + 1].T : at;
            DateTime a = t0 > start ? t0 : start, b = t1 < at ? t1 : at;
            if (b <= a)
                continue;
            var held = t0 + gap > a ? t0 + gap : a;
            if (held > b) held = b;
            te += ShoringEngine.AgeFactor(T0, ea) * (held - a).TotalSeconds / 86400;
            while (held < b)                                   // 끊긴 구간: 하루 단위로 기상 온도
            {
                var nxt = held.Date.AddDays(1) < b ? held.Date.AddDays(1) : b;
                te += ShoringEngine.AgeFactor(air(DateOnly.FromDateTime(held)), ea) * (nxt - held).TotalSeconds / 86400;
                held = nxt;
            }
        }
        return te;
    }

    /// <summary>마지막 기록 뒤 판정 시각까지 공백(일). MaxGapH 이하면 0. — evaluate.py stale_days</summary>
    public static double StaleDays(IEnumerable<Row> rows, DateTime at)
    {
        double d = (at - rows.Max(r => Ts(r["timestamp"]))).TotalSeconds / 86400;
        return d * 24 > MaxGapH ? d : 0.0;
    }

    /// <summary>[가정] 타설 후 이 시간 안 슬래브는 지지층에 넣지 않고 하중으로 봄 (가이드라인 하중 경우 = 타설 순간) — evaluate.py FRESH_H</summary>
    public const double FreshH = 24.0;

    /// <summary>두 구역 rect (x0, y0, x1, y1) 가 면적 > 0 으로 겹치나 — evaluate.py overlaps</summary>
    /// <summary>[확정 — 정의서 §3.2, 가이드라인: 구조계산 + 14 MPa 이상] 판넬 조기 탈형 기준 키 — evaluate.py EARLY_FORM_MPA_KEY</summary>
    public const string EarlyFormKey = "필러 동바리 조기 탈형";

    /// <summary>evaluate.py early_form: 판넬(슬래브 거푸집)만 먼저 떼고 필러 동바리는 남기는 단계. 구역 최소 추정과 현장양생 공시체(있으면) 중 낮은 값 ≥ 14 MPa.
    /// 강도 ①만 → "14 MPa 도달 — 판넬 조기 탈형 4조건 검토 대상" / "14 MPa 미달 — 판넬 존치". '판넬 탈형 요청 가능'은 Staged 4조건 결과만 (33차) — 동바리 해체 판정과 별개</summary>
    public static string EarlyForm(double fEst, double? fCyl, double fck)
    {
        double lim = ShoringEngine.Thresholds(fck)[EarlyFormKey];
        double f = fCyl is { } c ? Math.Min(fEst, c) : fEst;
        string basis = fCyl is not null ? "공시체·추정 중 낮은 값" : "추정만 — 공시체 없음";
        return f >= lim ? $"{Py.G(lim)} MPa 도달 — 판넬 조기 탈형 4조건 검토 대상 ({Py.F(f, 1)} ≥ {Py.G(lim)} MPa, {basis})"
            : $"{Py.G(lim)} MPa 미달 — 판넬 존치 ({Py.F(f, 1)} < {Py.G(lim)} MPa, {basis})";
    }

    internal static bool Overlaps((double X0, double Y0, double X1, double Y1) a, (double X0, double Y0, double X1, double Y1) b)
        => Math.Min(a.X1, b.X1) > Math.Max(a.X0, b.X0) && Math.Min(a.Y1, b.Y1) > Math.Max(a.Y0, b.Y0);

    /// <summary>[가정] 구역 경계에서 이 거리 안 센서만 그 구역 IDW 에 씀 (동바리 간격 1.2 m 의 약 2.5배) — evaluate.py SENSOR_MARGIN_M</summary>
    public const double SensorMarginM = 3.0;

    /// <summary>points [(id, x, y, 값)] 중 구역 rect 를 margin 만큼 넓힌 범위 안의 것. 하나도 없으면 층 전체를 쓰고 표시를 붙인다
    /// (멀리 떨어진 센서 하나가 층 전체 구역을 정하지 않게). evaluate.py near()</summary>
    internal static (List<(string Id, double X, double Y, T V)> Points, string Tail) Near<T>(List<(string Id, double X, double Y, T V)> points,
        (double X0, double Y0, double X1, double Y1) r, double margin = SensorMarginM)
    {
        var inside = points.Where(p => r.X0 - margin <= p.X && p.X <= r.X1 + margin && r.Y0 - margin <= p.Y && p.Y <= r.Y1 + margin).ToList();
        return inside.Count > 0 ? (inside, "") : (points, " (가까운 센서 없음 — 층 전체 센서)");
    }

    public static List<double> DailyTemps(IEnumerable<Row> weather, DateOnly start, int days)
    {
        var w = new Dictionary<string, double>();
        foreach (var r in weather)
            w[r["date"]!] = Py.Float(r["avgTa"]);
        return Enumerable.Range(0, Math.Max(days, 0)).Select(i => start.AddDays(i))
            .Select(d => w.TryGetValue(d.ToString("yyyy-MM-dd", Py.Inv), out var v) ? v : ShoringEngine.AirTemp(d)).ToList();
    }

    /// <summary>evaluate.py weather_tag(): DailyTemps 가 실제로 쓴 기온의 출처 (감사 A-26) — weather.csv 기록 [입력] / 평년 기온 [가정] / 섞임</summary>
    public static string WeatherTag(IReadOnlyDictionary<string, double> wmap, DateOnly start, int days, string label = "기상 기록")
    {
        int n = Enumerable.Range(0, Math.Max(days, 0)).Count(i => wmap.ContainsKey(start.AddDays(i).ToString("yyyy-MM-dd", Py.Inv)));
        if (n > 0 && n == days) return $"{label} [입력]";
        return n > 0 ? $"{label} {n}일 [입력]·평년 기온 {days - n}일 [가정]" : "평년 기온 [가정]";
    }

    /// <summary>evaluate.py weather_label: weather.csv 행들의 source 가 모두 같은 비지 않은 값이면 그 값(끝 ' [입력]' 뗌), 아니면 '기상 기록' — source 열 없는 옛 파일은 그대로 (F8)</summary>
    public static string WeatherLabel(IEnumerable<Row> weather)
    {
        var src = weather.Select(r => (r.GetValueOrDefault("source") ?? "").Trim()).Distinct().ToList();
        string one = src.Count == 1 ? src[0] : "";
        return one.Length > 0 ? (one.EndsWith(" [입력]", StringComparison.Ordinal) ? one[..^" [입력]".Length] : one) : "기상 기록";
    }

    /// <summary>폴더의 CSV 들로 판정 시각 at 의 구역별 판정표 (층·구역 순). 입력 오류는 InvalidDataException (한국어 문구).
    /// topFloor: 건물 최상층(지붕 슬래브) 번호. 그 층까지 타설이 끝나면 위에서 내려오는 타설 하중이 없으므로
    /// 존치 층수 게이트를 통과로 본다 [가정 — 팀 해석: KCS 14 20 12 3.3.2(2) 의 3개층 존치를 상부 타설 하중 분담 목적으로 읽음,
    /// 원문에 최상층 예외 문구는 없음]. 강도·공시체 기준은 그대로 적용. null 이면 데이터 폴더 options.json top_floor
    /// (애드인 [구역 나누기] 기본값 = 인식된 맨 위 구조 슬래브 층, 총괄 78차), 그것도 없으면 게이트 항상 적용.
    /// 규칙 게이트 (구역마다, 2026-11-04 내부 감사 A-01·02·03·06):
    ///   ① 판정 시각 이전 타설만 셈 (pours.csv 의 계획 행은 판정 행에서 뺌)
    ///   ② 이 구역과 평면이 겹치는 위층 구역이 모두 타설된 층을, 층 번호 산술이 아니라 층 순서로 셈 (지하 −1 → 지상 1 경계)
    ///   ③ 그 층들이 다음 타설을 받칠 때 각 ≥ 14 MPa, 하중비 ≤ 1.0 — 강도는 판정 시각 구역 최소 추정 (안전 측) [가정]</summary>
    /// calibrate: 예측 해체 가능일에 현장 보정 곡선 (Calibration, G2) — null 이면 데이터 폴더 options.json (기본 꺼짐). 켜면 바뀌는 것은 PredictedDue 와 사유 끝 '예측일: …' 태그뿐
    public static List<ZoneRow> Evaluate(string folder, DateTime at, double cureMin = 5.0, int? topFloor = null, bool? calibrate = null)
        => Full(folder, at, cureMin, topFloor, calibrate).Rows.ToList();   // 같은 입력이면 앞 결과 (A-20), 목록은 새로

    /// <summary>evaluate.py gate_details: 구역 → 규칙 게이트 세부값 (위로 타설된 층 수·지지층별 최소 구역·강도·하중비) — 판정 근거 창 비교 줄 (34차 A). 판정표와 같은 계산·같은 캐시</summary>
    public static Dictionary<string, GateDetail> GateDetails(string folder, DateTime at, double cureMin = 5.0, int? topFloor = null, bool? calibrate = null)
        => new(Full(folder, at, cureMin, topFloor, calibrate).Gate);

    sealed class EvalFull(List<ZoneRow> rows, Dictionary<string, GateDetail> gate)
    {
        public readonly List<ZoneRow> Rows = rows;
        public readonly Dictionary<string, GateDetail> Gate = gate;
    }

    static EvalFull Full(string folder, DateTime at, double cureMin, int? topFloor, bool? calibrate)
        => Checked(() => Memo("evaluate", folder, $"{at.Ticks}|{Py.Repr(cureMin)}|{topFloor}|{calibrate}", Inputs,
            () =>
            {
                int? top = topFloor ?? TopOption(folder);
                return EvaluateCore(folder, at, cureMin, top, calibrate ?? CalibrateDueOn(folder), topFloor is null && top is not null && TopAuto(folder));
            }));

    /// <summary>데이터 폴더의 판정 선택 파일 (evaluate.py OPTIONS) — {"calibrate_due": true} 이면 예측일에 현장 보정 곡선. 애드인 [프로젝트 설정] 이 씀</summary>
    public const string OptionsFile = "options.json";
    /// <summary>[가정] 현장 보정에 필요한 현장양생 공시체 시험 수 (evaluate.py CAL_MIN, 총괄 11-05 G2)</summary>
    public const int CalMin = 3;

    /// <summary>evaluate.py options: options.json 전체 (없음·못 읽음·객체 아님 = null) — 아래 *Option·SpecialZones·Roles 가 모두 이것으로 읽음</summary>
    public static System.Text.Json.JsonElement? Options(string folder)
    {
        try
        {
            var p = Path.Combine(folder, OptionsFile);
            if (!File.Exists(p)) return null;
            using var d = System.Text.Json.JsonDocument.Parse(Py.ReadText(p, new UTF8Encoding(false)).TrimStart('\uFEFF'));
            return d.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object ? d.RootElement.Clone() : null;
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return null; }
    }

    /// <summary>evaluate.py option: 값 하나 (없으면 null)</summary>
    public static System.Text.Json.JsonElement? Option(string folder, string key) => Options(folder) is { } o && o.TryGetProperty(key, out var v) ? v : null;
    static bool IsTrue(string folder, string key) => Option(folder, key)?.ValueKind == System.Text.Json.JsonValueKind.True;

    /// <summary>evaluate.py num_option: options.json 의 양수 숫자 하나 (v6.2 [가정] 값 — patrol_hours·scope_slab_max_mm 등). 참거짓·글자·0 이하·없음·못 읽음 = def</summary>
    public static double NumOption(string folder, string key, double def)
        => Option(folder, key) is { ValueKind: System.Text.Json.JsonValueKind.Number } v && v.GetDouble() is var x && x > 0 ? x : def;

    /// <summary>evaluate.py text_option: 글자 하나 (11-12 시공사·감리단 — 서류 머리 자동 입력), 글자가 아니면 빈칸</summary>
    public static string TextOption(string folder, string key)
        => Option(folder, key) is { ValueKind: System.Text.Json.JsonValueKind.String } v ? Py.Squash(v.GetString()!) : "";

    /// <summary>evaluate.py LIVE_KN_M2 — 시공하중 kN/m² (가이드라인 하중비교 예제 · KDS 21 50 00 §1.6 작업하중 최소), 이보다 크게만 바꿈 (W12)</summary>
    public const double LiveKnM2 = 2.5;
    /// <summary>evaluate.py live_load: options.json live_load_kn_m2 (자재 적재 등 [가정]), 없거나 2.5 미만이면 2.5 (보수 측)</summary>
    public static double LiveLoad(string folder) => Math.Max(LiveKnM2, NumOption(folder, "live_load_kn_m2", LiveKnM2));
    /// <summary>evaluate.py SDL_KN_M2·LL_KN_M2 — 가이드라인 2.1.2(2) 예제 "설계하중은 가정치 적용" 지지 슬래브 마감하중·설계활하중</summary>
    public const double SdlKnM2 = 1.64, LlKnM2 = 2.0;
    /// <summary>evaluate.py support_loads: options.json support_sdl_kn_m2 · support_ll_kn_m2 (구조계산서), 없으면 예제값 (11-11 G1)</summary>
    public static (double Sdl, double Ll, bool Example) SupportLoads(string folder)
    {
        double sdl = NumOption(folder, "support_sdl_kn_m2", SdlKnM2), ll = NumOption(folder, "support_ll_kn_m2", LlKnM2);
        return (sdl, ll, sdl == SdlKnM2 && ll == LlKnM2);
    }
    /// <summary>evaluate.py support_note — 판정 근거 창·요청서 근거 줄</summary>
    public static string SupportNote(string folder)
    {
        var (sdl, ll, ex) = SupportLoads(folder);
        return $"지지층 여유 1.2×{Py.G(sdl)} + 1.6×{Py.G(ll)} kN/m²" + (ex ? " — 가이드라인 예제값 [가정] (구조계산서 값을 [프로젝트 설정]에)" : " (구조계산서)");
    }
    /// <summary>evaluate.py live_note: 하중 줄 꼬리표 — 판정 근거 창·조기 탈형 검토서</summary>
    public static string LiveNote(string folder) => $"시공하중 {Py.G(LiveLoad(folder))} kN/m² [가정] — 자재 적재·장비 집중하중 미반영";

    /// <summary>v6.2 [프로젝트 설정] '현장 기준 [가정]': options.json 숫자 하나 (null = 기본값으로 — JSON null). 같으면·샘플 폴더면 안 씀</summary>
    public static void SetNumOption(string folder, string key, double? v)
    {
        if (!Writable(folder)) return;
        double cur = NumOption(folder, key, double.NaN);
        if (v is null ? double.IsNaN(cur) : cur == v) return;
        SetOption(folder, key, v is double x ? x : null);
    }

    /// <summary>11-12 [프로젝트 설정] 시공사·감리단 (글자, 빈칸 = 지움). 같으면·샘플 폴더면 안 씀</summary>
    public static void SetTextOption(string folder, string key, string v)
    {
        v = Py.Squash(v ?? "");
        if (Writable(folder) && TextOption(folder, key) != v) SetOption(folder, key, v.Length > 0 ? v : null);
    }

    /// <summary>evaluate.py top_option: options.json top_floor (정수만 — 참거짓·소수·글자는 null). [구역 나누기]가 인식된 맨 위 구조 슬래브 층으로 씀 (총괄 78차 ④).
    /// 없거나 못 읽으면 null (게이트 항상 적용)</summary>
    public static int? TopOption(string folder) => Option(folder, "top_floor") is { ValueKind: System.Text.Json.JsonValueKind.Number } v && v.TryGetInt32(out var n) ? n : null;

    /// <summary>evaluate.py TOP_AUTO_TAG — 사람이 확인하지 않은 최상층(자동값)으로 게이트를 넘은 구역 사유 끝 (총괄 83차, 결론은 그대로)</summary>
    public const string TopAutoTag = "최상층 자동 판단 [가정] — 확인 필요";
    /// <summary>evaluate.py SCOPE_* (v6.2 W11 범위 밖 알림 [가정 — 팀 결정, 규정 값 아님]): 두꺼운 슬래브·지하층·높은 동바리 → 구조기술자 검토 안내 (결론 그대로). options.json scope_slab_max_mm · scope_shore_h_m</summary>
    public const double ScopeSlabMm = 300;   // [가정] 슬래브만 — 동바리 높이 = InstallRules.PeHMm (건진법 시행령 제101조의2, 기준 한 곳)
    public const string ShoreHLaw = "건설기술진흥법 시행령 제101조의2";
    public const string ScopeTag = "⚠ 범위 밖 가능", ScopeTail = "구조기술자 검토 (재받침·층간 하중 분배 미계산)";
    static readonly System.Text.RegularExpressions.Regex ScopeRe = new(System.Text.RegularExpressions.Regex.Escape(ScopeTag) + @" \(.*?\) — " + System.Text.RegularExpressions.Regex.Escape(ScopeTail));
    /// <summary>evaluate.py scope_note: 사유 속 '⚠ 범위 밖 가능 (…) — 구조기술자 검토 (…)' 한 토막 (없으면 빈칸)</summary>
    public static string ScopeNote(string? reason) => ScopeRe.Match(reason ?? "") is { Success: true } m ? m.Value : "";

    /// <summary>evaluate.py top_auto: options.json top_floor_auto 가 true (= [구역 나누기] 자동값 그대로, 사람이 확인 안 함). 없거나 못 읽으면 false</summary>
    public static bool TopAuto(string folder) => IsTrue(folder, "top_floor_auto");

    /// <summary>최상층 → options.json top_floor·top_floor_auto (다른 키는 그대로). floor null = 최상층 예외 안 씀. auto = [구역 나누기] 자동값 그대로(사람이 확인 안 함).
    /// [구역 나누기] 확인 창·[프로젝트 설정] 이 씀. 같으면 안 씀, shoring\sample 폴더에는 쓰지 않음</summary>
    public static void SetTopFloor(string folder, int? floor, bool auto)
    {
        auto = auto && floor is not null;
        if (!Writable(folder) || (TopOption(folder) == floor && TopAuto(folder) == auto)) return;
        SetOption(folder, "top_floor", floor is int f ? f : null);
        SetOption(folder, "top_floor_auto", auto);
    }

    /// <summary>evaluate.py calibrate_due_on: options.json 의 calibrate_due 가 true 일 때만 (없거나 못 읽으면 false — 기본 꺼짐)</summary>
    public static bool CalibrateDueOn(string folder) => IsTrue(folder, "calibrate_due");

    /// <summary>[프로젝트 설정] '예측일에 현장 보정 쓰기' → options.json (다른 키는 그대로). shoring\sample 폴더에는 쓰지 않음</summary>
    public static void SetCalibrateDue(string folder, bool on)
    {
        if (Writable(folder) && CalibrateDueOn(folder) != on) SetOption(folder, "calibrate_due", on);
    }

    /// <summary>[가정] 기온감률 0.65 ℃/100 m (v6 사용자 확정 — 층 높이 보정, evaluate.py LAPSE_C_PER_M) · 층고 3 m (options.json story_m)</summary>
    public const double LapseCPerM = 0.0065, StoryM = 3.0;

    /// <summary>evaluate.py height_option: options.json {"height_correct": true, "story_m": 3.0, "exposure_c": 0.0} → (켬, 층고 m, 노출 보정 ℃). 없거나 못 읽으면 꺼짐</summary>
    public static (bool On, double Story, double Expo) HeightOption(string folder)
    {
        if (Options(folder) is not { } r) return (false, StoryM, 0.0);
        static double Num(System.Text.Json.JsonElement e) => e.ValueKind switch   // Python float(): 숫자·글자 숫자·참거짓(1·0)
        {
            System.Text.Json.JsonValueKind.Number => e.GetDouble(),
            System.Text.Json.JsonValueKind.True => 1.0,
            System.Text.Json.JsonValueKind.False => 0.0,
            System.Text.Json.JsonValueKind.String => double.Parse(e.GetString()!.Trim(), NumberStyles.Float, Py.Inv),
            _ => throw new FormatException("숫자 아님"),
        };
        try
        {
            return (r.TryGetProperty("height_correct", out var h) && h.ValueKind == System.Text.Json.JsonValueKind.True,
                r.TryGetProperty("story_m", out var s) ? Num(s) : StoryM, r.TryGetProperty("exposure_c", out var x) ? Num(x) : 0.0);
        }
        catch (FormatException) { return (false, StoryM, 0.0); }
    }

    /// <summary>evaluate.py height_dt: 층 fl 슬래브 기온 보정 ℃ (≤ 0) = −0.65 ℃/100 m × (층 × 층고, 지하 0) − 노출 보정(지상 층). 꺼짐이면 0</summary>
    public static double HeightDt(int fl, (bool On, double Story, double Expo) o) => !o.On ? 0.0 : -LapseCPerM * Math.Max(0, fl) * o.Story - (fl >= 1 ? o.Expo : 0.0);

    /// <summary>evaluate.py height_tag — Python f"{dt:+.2f}" (−0.0 도 '-')</summary>
    public static string HeightTag(double dt) => $" · 층 높이 보정 {(double.IsNegative(dt) ? "-" : "+")}{Py.F(Math.Abs(dt), 2)} °C [가정]";

    /// <summary>[프로젝트 설정] '층 높이 보정' → options.json height_correct (다른 키는 그대로). shoring\sample 폴더에는 쓰지 않음</summary>
    public static void SetHeightCorrect(string folder, bool on)
    {
        if (Writable(folder) && HeightOption(folder).On != on) SetOption(folder, "height_correct", on);
    }

    /// <summary>evaluate.py SPECIAL_KINDS — 가이드라인 3.2.11(2)⑥ fck 100% 확인 전 해체 금지 부위 (프로그램이 자동으로 못 가려냄)</summary>
    public static readonly string[] SpecialKinds = { "캔틸레버", "차양", "지하주차장" };

    /// <summary>evaluate.py special_zones: options.json special_zones {구역: 부위} (11-11 G4·Revit #24). 모르는 부위·형식은 무시</summary>
    public static Dictionary<string, string> SpecialZones(string folder) => StrMap(folder, "special_zones", SpecialKinds.Contains);

    /// <summary>evaluate.py roles (11-12 정보 출처): options.json actor_roles {이름: 역할} — 서류 '기본 사항·출처' 표의 입력자 옆 역할 (빈 역할은 뺌)</summary>
    public static Dictionary<string, string> Roles(string folder) => StrMap(folder, "actor_roles", v => v.Trim().Length > 0);

    /// <summary>{글자: 글자} 객체 하나 (값이 글자이고 keep 을 통과한 것만, 없으면 빈 사전)</summary>
    static Dictionary<string, string> StrMap(string folder, string key, Func<string, bool> keep)
    {
        var res = new Dictionary<string, string>();
        if (Option(folder, key) is { ValueKind: System.Text.Json.JsonValueKind.Object } v)
            foreach (var kv in v.EnumerateObject())
                if (kv.Value.ValueKind == System.Text.Json.JsonValueKind.String && keep(kv.Value.GetString()!)) res[kv.Name] = kv.Value.GetString()!;
        return res;
    }

    /// <summary>[구역 정보] 특수 부위 고르기: kind = null 이면 뺌. 샘플 폴더면 안 씀</summary>
    public static void SetSpecialZone(string folder, string zoneId, string? kind)
    {
        if (!Writable(folder)) return;
        var cur = SpecialZones(folder);
        if (kind == null ? !cur.Remove(zoneId) : cur.TryGetValue(zoneId, out var k) && k == kind) return;
        if (kind != null) cur[zoneId] = kind;
        SetMap(folder, "special_zones", cur);
    }

    /// <summary>[프로젝트 설정] '관리자 역할' (이름=역할). 같으면·샘플 폴더면 안 씀</summary>
    public static void SetRoles(string folder, IReadOnlyDictionary<string, string> roles)
    {
        var cur = Writable(folder) ? Roles(folder) : null;
        if (cur != null && !(cur.Count == roles.Count && cur.All(kv => roles.TryGetValue(kv.Key, out var r) && r == kv.Value))) SetMap(folder, "actor_roles", roles);
    }

    static void SetMap(string folder, string key, IEnumerable<KeyValuePair<string, string>> m)
    {
        var o = new System.Text.Json.Nodes.JsonObject();
        foreach (var (k, v) in m) o[k] = v;
        SetOption(folder, key, o);
    }

    /// <summary>쓸 수 있는 데이터 폴더 (있고, shoring\sample 기대값 폴더가 아님)</summary>
    static bool Writable(string folder) => !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) && !IsSample(folder);

    /// <summary>shoring\sample 회귀 기준 폴더인가 (거기엔 쓰지 않음 — 애드인·현장 입력 공통 한 곳). 상대·반쯤 친 경로도 안전하게</summary>
    public static bool IsSample(string? folder)
    {
        var f = (folder ?? "").Replace('/', '\\');
        try { f = Path.GetFullPath(f); } catch (Exception x) when (x is ArgumentException or NotSupportedException or PathTooLongException) { }
        return f.Contains(@"shoring\sample", StringComparison.OrdinalIgnoreCase);
    }

    static void SetOption(string folder, string key, System.Text.Json.Nodes.JsonNode? on)
    {
        var p = Path.Combine(folder, OptionsFile);
        System.Text.Json.Nodes.JsonObject o;
        try { o = File.Exists(p) ? System.Text.Json.Nodes.JsonNode.Parse(Py.ReadText(p, new UTF8Encoding(false)).TrimStart('\uFEFF')) as System.Text.Json.Nodes.JsonObject ?? new() : new(); }
        catch (System.Text.Json.JsonException) { o = new(); }
        o[key] = on;
        File.WriteAllText(p, o.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n", new UTF8Encoding(false));
    }

    /// <summary>evaluate.py calibration: 현장양생 공시체로 배합별 강도 곡선 s 보정 (G2 — 예측 해체 가능일에만) → {배합: (s 중앙값 또는 null, 쓴 시험 수)}.
    /// 판정 시각 이전 현장양생 시험마다 te = 타설일~시험일 일수만큼 Predict 와 같은 기온 모델(양생 하한 cureMin), 강도 = 3개 평균 → CalibrateS (0 &lt; te &lt; 28),
    /// s ≤ 0 은 뺌 [가정]. CalMin 건 미만이면 null (기본 곡선 [가정])</summary>
    public static Dictionary<string, (double? S, int N)> Calibration(string folder, DateTime at, double cureMin = 5.0)
    {
        var pours = new Dictionary<string, Row>();
        foreach (var r in Read(folder, "pours.csv")) pours[r["zone_id"]!] = r;
        var weather = Read(folder, "weather.csv");
        var hopt = HeightOption(folder);                                            // v6 층 높이 보정 (꺼지면 0)
        var zfl = new Dictionary<string, int>();
        foreach (var r in Read(folder, "zones.csv")) zfl[r["zone_id"]!] = Py.Int(r["floor"]);
        var pts = new Dictionary<string, List<double>>();
        var seen = new HashSet<string>();
        foreach (var r in CylRows(folder))
        {
            if (!pours.TryGetValue(r["zone_id"] ?? "", out var p) || !ShoringEngine.FieldCurings.Contains(r.GetValueOrDefault("curing"))
                || DateOnly.ParseExact(r["test_date"]!, "yyyy-MM-dd", Py.Inv) > DateOnly.FromDateTime(at)) continue;
            if (r["cyl_lot"] != "" && !seen.Add(string.Join("\u0001", r["cyl_lot"], r["test_date"], r["curing"], r["f1"], r["f2"], r["f3"]))) continue;   // W1: 묶음 공통 시험은 보정 자료 1건
            var cast = DateOnly.ParseExact(p["pour_date"]!, "yyyy-MM-dd", Py.Inv);
            int days = DateOnly.ParseExact(r["test_date"]!, "yyyy-MM-dd", Py.Inv).DayNumber - cast.DayNumber;
            double f = Py.Sum(new[] { "f1", "f2", "f3" }.Select(k => Py.Float(r[k]))) / 3;
            if (days <= 0 || f <= 0) continue;
            var (_, ea) = ShoringEngine.Mixes[p["mix"]!];
            double dt = HeightDt(zfl.GetValueOrDefault(r["zone_id"] ?? "", 0), hopt);
            var te = ShoringEngine.EquivalentAge(DailyTemps(weather, cast, days).Select(T => Math.Max(T + dt, cureMin)), ea);
            if (ShoringEngine.CalibrateS(f, te, Py.Float(p["fck"])) is double s && s > 0)
            {
                if (!pts.TryGetValue(p["mix"]!, out var l)) pts[p["mix"]!] = l = new();
                l.Add(s);
            }
        }
        double Median(List<double> v) { var o = v.OrderBy(x => x).ToList(); int m = o.Count / 2; return o.Count % 2 == 1 ? o[m] : (o[m - 1] + o[m]) / 2; }   // statistics.median
        return pts.ToDictionary(kv => kv.Key, kv => (kv.Value.Count >= CalMin ? Median(kv.Value) : (double?)null, kv.Value.Count));
    }

    static EvalFull EvaluateCore(string folder, DateTime at, double cureMin, int? topFloor, bool calibrate, bool autoTop)
    {
        var gateD = new Dictionary<string, GateDetail>();
        var zones = new Dictionary<string, Row>();
        foreach (var r in Read(folder, "zones.csv")) zones[r["zone_id"]!] = r;
        var pourRows = Read(folder, "pours.csv");
        var pours = new Dictionary<string, Row>();
        foreach (var r in pourRows) pours[r["zone_id"]!] = r;
        var dup = pourRows.GroupBy(r => r["zone_id"]!).Where(g => g.Count() > 1).Select(g => g.Key).OrderBy(z => z, StringComparer.Ordinal).ToList();
        if (dup.Count > 0)                                                        // A-24: 말없이 마지막 행을 쓰지 않는다
            throw new InvalidDataException($"pours.csv 에 같은 구역이 두 번 있음: {Py.ReprList(dup)} — 구역마다 타설 한 줄 (재타설이면 옛 줄을 지우세요)");
        List<Row> weather = Read(folder, "weather.csv"), strength = Read(folder, "sensor_strength.csv");
        var sens = SensorsOf(folder);                         // 센서는 한 번 읽어 둔 것 (A-20)
        var airKey = FileKey(folder, "weather.csv");
        // 입력 점검: 사람이 엑셀로 만든 CSV라서 흔한 실수를 먼저 알려 준다
        if (zones.Count == 0 || pours.Count == 0)
            throw new InvalidDataException($"{folder} 에 zones.csv 와 pours.csv 가 있어야 합니다");
        var bad = pours.Keys.Where(z => !zones.ContainsKey(z)).OrderBy(z => z, StringComparer.Ordinal).ToList();
        if (bad.Count > 0)
            throw new InvalidDataException($"pours.csv 의 zone_id 가 zones.csv 에 없음: {Py.ReprList(bad)}");
        bad = pours.Values.Select(p => p["mix"]!).Distinct().Where(m => !ShoringEngine.Mixes.ContainsKey(m)).OrderBy(m => m, StringComparer.Ordinal).ToList();
        if (bad.Count > 0)
            throw new InvalidDataException($"pours.csv 의 mix 값 {Py.ReprList(bad)} 은 쓸 수 없음. 가능한 값: {Py.ReprList(ShoringEngine.Mixes.Keys)}");
        var wmap = new Dictionary<string, double>();
        foreach (var r in weather) wmap[r["date"]!] = Py.Float(r["avgTa"]);
        string wlabel = WeatherLabel(weather);   // weather.csv source 가 한 값이면 그 출처 이름 (F8)
        var hopt = HeightOption(folder);         // v6 층 높이 보정 — 기상 기온으로 셈하는 강도(참고)·예측일만, 센서는 그대로
        double Air(DateOnly d) => wmap.TryGetValue(d.ToString("yyyy-MM-dd", Py.Inv), out var v) ? v : ShoringEngine.AirTemp(d);
        var cyl = new Dictionary<string, Row>();
        var ignored = new Dictionary<string, int>();
        foreach (var r in CylRows(folder))   // W1: 타설 묶음 공통 시험 → 묶음의 구역마다
        {
            if (DateOnly.ParseExact(r["test_date"]!, "yyyy-MM-dd", Py.Inv) > DateOnly.FromDateTime(at))
                continue;
            if (!ShoringEngine.FieldCurings.Contains(r.GetValueOrDefault("curing")))   // A-05: 표준양생 등은 기준 근거로 안 씀
            {
                ignored[r["zone_id"]!] = ignored.GetValueOrDefault(r["zone_id"]!) + 1;
                continue;
            }
            cyl.TryGetValue(r["zone_id"]!, out var prev);
            static double Avg(Row x) => Py.Sum(new[] { "f1", "f2", "f3" }.Select(k => Py.Float(x[k]))) / 3;
            // 최신 시험, 같은 날 두 건이면 낮은 쪽 (A-24: 행 순서로 높은 값이 이기지 않게)
            int cmp = prev is null ? 1 : string.CompareOrdinal(r["test_date"], prev["test_date"]);
            if (cmp > 0 || (cmp == 0 && Avg(r) < Avg(prev!)))
                cyl[r["zone_id"]!] = r;
        }
        var startOf = pours.ToDictionary(kv => kv.Key, kv => FromIso($"{kv.Value["pour_date"]}T{(string.IsNullOrEmpty(kv.Value.GetValueOrDefault("pour_time")) ? "23:59" : kv.Value["pour_time"])}"));
        var done = startOf.Where(kv => kv.Value <= at).Select(kv => kv.Key).ToHashSet();
        var rectOf = zones.ToDictionary(kv => kv.Key, kv => (Py.Float(kv.Value["x0"]), Py.Float(kv.Value["y0"]), Py.Float(kv.Value["x1"]), Py.Float(kv.Value["y1"])));
        var floorOf = zones.ToDictionary(kv => kv.Key, kv => Py.Int(kv.Value["floor"]));
        var order = floorOf.Values.Distinct().Order().ToList();                    // 층 순서 (0 없는 번호도 순번으로)
        bool hasSlab = zones.Values.Any(r => r.ContainsKey("slab_mm"));            // D1: 애드인이 모델 슬래브 두께를 쓴 zones.csv (선택 열)
        var slabOf = zones.Where(kv => !string.IsNullOrWhiteSpace(kv.Value.GetValueOrDefault("slab_mm"))).ToDictionary(kv => kv.Key, kv => Py.Float(kv.Value["slab_mm"]));
        var shoreOf = zones.Where(kv => !string.IsNullOrWhiteSpace(kv.Value.GetValueOrDefault("shore_mm"))).ToDictionary(kv => kv.Key, kv => Py.Float(kv.Value["shore_mm"]));   // W11 동바리 높이 (선택 열)
        double smax = NumOption(folder, "scope_slab_max_mm", ScopeSlabMm);
        double live = LiveLoad(folder);                                                                  // W12 시공하중 (기본 2.5)
        var (sdl, ll, _) = SupportLoads(folder);                                                         // 11-11 G1 지지층 여유 하중 (기본 = 예제값)
        var doneSorted = done.OrderBy(z => floorOf[z]).ThenBy(z => z, StringComparer.Ordinal).ToList();

        // 1단계: 타설된 구역마다 위치 근거(구역 최소 추정)
        var est = new Dictionary<string, (double F, (double X, double Y)? Xy, string Src, double Age, bool Near)>();
        foreach (var zid in doneSorted)
        {
            var p = pours[zid];
            int fl = floorOf[zid];
            var rect = rectOf[zid];
            var start = startOf[zid];
            double fck = Py.Float(p["fck"]);
            string mix = p["mix"]!;
            var (s, ea) = ShoringEngine.Mixes[mix];
            double age = (at - start).TotalSeconds / 86400;
            var byId = sens.Floor(fl);
            var ests = new List<(double F, (double X, double Y)? Xy, string Src)>();   // 위치 근거 후보 → 가장 낮은 값
            if (byId.Count > 0)
            {
                var (pts, tail) = Near(byId.Select(kv => (kv.Key, Py.Float(kv.Value[0]["x"]), Py.Float(kv.Value[0]["y"]), sens.Te(fl, kv.Key, start, at, ea, Air, airKey))).ToList(), rect);
                var gaps = pts.Select(q => (q.Id, D: sens.Stale(fl, q.Id, at))).Where(q => q.D != 0).Select(q => $"{q.Id} {Py.F(q.D, 0)}일").ToList();
                if (gaps.Count > 0)
                    tail += $" · 기록 끊김 {string.Join(", ", gaps)} → 기상 온도";
                var best = ShoringEngine.ZoneMinStrength(pts, new Dictionary<string, (double, double, double, double)> { [zid] = rect }, fck, mix)[zid]
                    ?? throw new InvalidDataException($"{zid} 구역이 0.25 m 보다 좁음 (Python: TypeError)");
                ests.Add((best.F, best.Xy, $"센서 {pts.Count}개 IDW{tail}"));
            }
            // 강도 센서: 이 타설 이후 ~ 판정 시각까지 센서마다 마지막 값 → IDW (Dictionary 는 지우지 않으면 넣은 순서 = Python dict 순서)
            var last = new Dictionary<string, (DateTime T, double X, double Y, double Mpa)>();
            foreach (var r in strength)
            {
                var t = Ts(r["timestamp"]);
                if (Py.Int(r["floor"]) == fl && start <= t && t <= at && (!last.TryGetValue(r["sensor_id"]!, out var prev) || t >= prev.T))
                    last[r["sensor_id"]!] = (t, Py.Float(r["x"]), Py.Float(r["y"]), Py.Float(r["mpa"]));
            }
            if (last.Count > 0)
            {
                var (v, tail) = Near(last.Select(kv => (kv.Key, kv.Value.X, kv.Value.Y, kv.Value.Mpa)).ToList(), rect);
                var best = ShoringEngine.ZoneMinFromValues(v.Select(a => (a.X, a.Y)).ToList(), v.Select(a => a.V).ToList(), rect)
                    ?? throw new InvalidDataException($"{zid} 구역이 0.25 m 보다 좁음 (Python: TypeError)");
                ests.Add((best.F, best.Xy, $"강도 센서 {v.Count}개 IDW{tail}"));
            }
            bool nearOk = ests.Any(a => !a.Src.Contains("가까운 센서 없음"));   // 구역 3 m 안 센서 = 위치 근거 (없으면 추정은 참고, 34차)
            double dt = HeightDt(fl, hopt);
            bool wx = ests.Count == 0 || ests.All(a => a.Src.Contains("가까운 센서 없음"));   // 기상 기온을 씀
            if (ests.Count > 0 && wx)
            {
                // A-10: 3 m 안 센서가 없으면 먼 센서 IDW 만 믿지 않고 기상 예측(보온 하한 없이)과 비교해 낮은 값
                var temps = DailyTemps(weather, DateOnly.FromDateTime(start), (int)age).Select(T => T + dt).ToList();
                ests.Add((ShoringEngine.StrengthAt(ShoringEngine.EquivalentAge(temps, ea), fck, s), null, "기상 예측(하한 없음)"));
            }
            (double F, (double X, double Y)? Xy, string Src) e;
            if (ests.Count > 0)
            {
                e = ests.Aggregate((a, b) => b.F < a.F ? b : a);   // Python min(): 같으면 앞의 것
                if (ests.Count > 1)
                    e.Src = string.Join("·", ests.Select(a => (a.Src.StartsWith("센서") ? "온도 " : "") + $"{a.Src} {Py.F(a.F, 1)}")) + " 중 최소";
            }
            else
            {
                var temps = DailyTemps(weather, DateOnly.FromDateTime(start), (int)age).Select(T => Math.Max(T + dt, cureMin));
                e = (ShoringEngine.StrengthAt(ShoringEngine.EquivalentAge(temps, ea), fck, s), null,
                     $"기상 예측(센서 없음) — {WeatherTag(wmap, DateOnly.FromDateTime(start), (int)age, wlabel)}, 양생 하한 {Py.G(cureMin)} °C [가정]");   // A-26 출처 태그
            }
            if (hopt.On && wx) e.Src += HeightTag(dt);
            if (!nearOk) e.Src += " · 참고 (판정에 안 씀)";
            est[zid] = (e.F, e.Xy, e.Src, age, nearOk);
        }

        // 2단계: 구역마다 규칙 게이트 (A-01·02·03·06) → 판정
        var cal = calibrate ? Calibration(folder, at, cureMin) : new();   // G2 현장 보정 곡선 (꺼져 있으면 빈 → 기본 곡선, 예전과 같음)
        var insNg = ShoreInspect.OpenNg(folder, at, ShoreInspect.Install);   // F16 규칙 ⑦: 풀리지 않은 설치 검측 NG
        var reshored = Approval.StatusOverride(folder, at).Where(kv => kv.Value == 8).Select(kv => kv.Key).ToHashSet();   // 11-11 G3: 재받침 기록 구역
        var special = SpecialZones(folder);
        var outp = new List<ZoneRow>();
        foreach (var zid in doneSorted)
        {
            var p = pours[zid];
            int fl = floorOf[zid];
            var rect = rectOf[zid];
            var start = startOf[zid];
            double fck = Py.Float(p["fck"]);
            string mix = p["mix"]!;
            var (fEst, xy, src, age, nearOk) = est[zid];
            double thr = ShoringEngine.Thresholds(fck)["다층 동바리 해체"];
            var stack = new List<(int F, List<string> Ov)>();                         // 이 구역 위로 연속 타설된 층 (층, 겹치는 구역들)
            foreach (var f in order.Skip(order.IndexOf(fl) + 1))
            {
                var ov = zones.Keys.Where(z => floorOf[z] == f && Overlaps(rectOf[z], rect)).ToList();
                if (ov.Count == 0 || !ov.All(done.Contains))
                    break;
                stack.Add((f, ov));
            }
            int highest = stack.Count > 0 ? stack[^1].F : fl;
            bool toppedOut = topFloor is not null && highest >= topFloor;
            bool levelOk = stack.Count >= ShoringEngine.MinLevels || toppedOut;
            var c = cyl.GetValueOrDefault(zid);
            double? fCyl = c is null ? null : Py.Sum(new[] { "f1", "f2", "f3" }.Select(k => Py.Float(c[k]))) / 3;
            var temps400 = DailyTemps(weather, DateOnly.FromDateTime(start), 400).Select(T => T + HeightDt(fl, hopt)).ToList();      // 한 번만 만든다 · v6 층 높이 보정
            var (sCal, nCal) = cal.TryGetValue(mix, out var cv) ? cv : ((double?)null, 0);
            int need = Py.FirstDayOrNull(a => ShoringEngine.Predict(mix, DateOnly.FromDateTime(start), a, fck, cureMin, temps400, sCal) >= thr)
                ?? throw new InvalidDataException($"{zid}: 400일 안에 기준 {Py.G(thr)} MPa 에 도달 안 함 (강도 예측) — weather.csv 기온·양생 최저 온도·fck 입력을 확인하세요");   // A-28
            string gate = levelOk ? (toppedOut && stack.Count < ShoringEngine.MinLevels ? "OK(최상부)" : "OK") : "NG", supMsg = "";
            bool? gFresh = null;
            double? gRatio = null, gLim = null;
            var gSup = new List<(int Floor, string Zone, double Mpa)>();
            if (levelOk && !toppedOut)                                                 // ③ 지지층 검토 (다음 타설 하중)
            {
                // 맨 위 슬래브가 타설 후 FreshH 안이면 그 슬래브는 지지층이 아니라 하중 → 그 아래층들이 받치는지,
                // 아니면 맨 위까지 다음 타설을 받치는 지지층 (강도는 판정 시각 값, 보수 측)
                bool fresh = (at - stack[^1].Ov.Max(z => startOf[z])).TotalSeconds / 3600 < FreshH;
                var sup = (fresh ? stack.SkipLast(1) : stack).Select(q => (double?)q.Ov.Min(z => est[z].F)).ToList();
                // D1: 하중 슬래브 두께 = 이 구역과 평면이 겹치는 이 층·위층 구역의 zones.csv slab_mm 최댓값 (보수 측), 없으면 210 mm [가정]
                // — slab_mm 열이 없는 zones.csv 는 예전과 바이트 같게 (문구 태그 없음)
                var ts = zones.Keys.Where(z => slabOf.ContainsKey(z) && floorOf[z] >= fl && Overlaps(rectOf[z], rect)).Select(z => slabOf[z]).ToList();
                var (ratio, fmin) = ts.Count > 0 ? ShoringEngine.LoadCheck(sup, fck, tSlab: ts.Max() / 1000, live: live, sdl: sdl, ll: ll) : ShoringEngine.LoadCheck(sup, fck, live: live, sdl: sdl, ll: ll);
                double lim = ShoringEngine.Thresholds(fck)["지지 슬래브"];
                (gFresh, gRatio, gLim) = (fresh, ratio, lim);
                foreach (var q in fresh ? stack.SkipLast(1) : stack)
                {
                    var zmin = q.Ov.Aggregate((a, b) => est[b].F < est[a].F ? b : a);   // Python min(key) = 같으면 앞 구역
                    gSup.Add((q.F, zmin, est[zmin].F));
                }
                if (fmin < lim || ratio > 1)
                {
                    gate = "NG(지지층)";
                    var m = new List<string>();
                    if (fmin < lim) m.Add($"다음 타설 때 지지층 최소 {Py.F(fmin!.Value, 1)} < {Py.F(lim, 0)} MPa");
                    if (ratio > 1) m.Add($"하중비 {Py.F(ratio, 2)} > 1.0");
                    supMsg = string.Join("; ", m);
                    if (hasSlab) supMsg += ts.Count > 0 ? $" — 하중 슬래브 {Py.G(ts.Max())} mm (모델)" : " — 하중 슬래브 210 mm [가정]";
                }
            }
            string decision, reason;
            if (gate == "NG")
                (decision, reason) = ("존치", $"위로 {stack.Count}개층만 타설됨 (타설층 포함 {ShoringEngine.MinLevels}개층 존치 필요)");
            else if (supMsg != "")
                (decision, reason) = ("존치", supMsg + " (KCS 14 20 12 지지 슬래브)");
            else
            {
                decision = ShoringEngine.ZoneDecision(nearOk ? fEst : null, fCyl, thr);   // 3 m 안 센서가 없으면 공시체만 (34차)
                var parts = new List<string>();
                if (!(fCyl is null || fCyl >= thr)) parts.Add($"공시체 평균 {Py.F(fCyl!.Value, 2)} < {Py.F(thr, 0)} MPa");
                if (nearOk && !(fEst >= thr)) parts.Add($"구역 최소 추정 {Py.F(fEst, 2)} < {Py.F(thr, 0)} MPa" + (xy is null ? "" : $" (좌표 {XyText(xy)})"));   // 센서 없으면 좌표 없음 (20차)
                if (fCyl is null) parts.Add("공시체 시험 결과 없음" + (ignored.TryGetValue(zid, out var n) ? $" (현장양생 아님 {n}건 무시)" : ""));
                reason = parts.Count > 0 ? string.Join("; ", parts) : "기준 충족";
                if (c is not null && c["cyl_lot"] != "") reason += $"; 공시체 (타설 {c["cyl_lot"]}) {Py.F(fCyl!.Value, 1)} MPa";   // W1 근거: 타설 묶음 공통 시험
            }
            if (decision == "해체 승인 요청 가능" && insNg.TryGetValue(zid, out var ng))   // F16 ⑦: 설치 검측 NG 가 풀리지 않으면 해체 요청 불가 (결론은 '결함 기록'까지)
                (decision, reason) = ("존치 (설치 결함 기록)", "설치 검측 결함 기록 미해결 — " + ShoreInspect.NgText(ng));
            if (gate == "OK(최상부)" && autoTop) reason += "; " + TopAutoTag;            // 83차: 자동 최상층으로만 게이트를 넘은 구역 — 확인 필요 (결론 그대로)
            var outOf = new[]                                                          // W11 범위 밖 (결론 그대로)
            {
                slabOf.TryGetValue(zid, out var sm) && sm > smax ? $"슬래브 {Py.G(sm)} mm > {Py.G(smax)} mm [가정]" : null,
                fl < 0 ? "지하층" : null,
                shoreOf.TryGetValue(zid, out var hm) && hm >= InstallRules.PeHMm ? $"동바리 높이 {Py.F(hm / 1000, 2)} m ≥ {Py.G(InstallRules.PeHMm / 1000.0)} m ({ShoreHLaw})" : null,
                special.TryGetValue(zid, out var sk) ? $"{sk} — fck 100% 확인 (가이드라인 3.2.11)" : null,
                reshored.Contains(zid) || stack.Any(st => st.Ov.Any(reshored.Contains)) ? "재받침 기록 — 단순 하중비교 범위 밖 (가이드라인 2.1.2(2)①)" : null,
            }.Where(x => x != null).ToList();
            if (outOf.Count > 0) reason += $"; {ScopeTag} ({string.Join(" · ", outOf)}) — {ScopeTail}";
            if (calibrate)                                                             // 예측일 근거 태그 (G2) — 꺼져 있으면 사유 그대로
                reason += sCal is double sc ? $"; 예측일: 현장 보정 곡선 (공시체 {nCal}건, s={Py.F(sc, 3)}) [보정]"
                    : $"; 예측일: 기본 곡선 [가정] (보정 자료 부족 — 공시체 {nCal}건 < {CalMin})";
            outp.Add(new ZoneRow
            {
                ZoneId = zid, Floor = fl, PourDate = p["pour_date"]!, AgeDays = Py.Round(age, 1), Mix = mix,
                Fck = fck, Threshold = thr, FCyl = fCyl is null ? null : Py.Round(fCyl.Value, 1),
                CylTestDate = c is null ? "" : c["test_date"] ?? "", CylCuring = c is null ? "" : c["curing"] ?? "",
                FEstMin = Py.Round(fEst, 1), FEstXy = xy, EstSource = src,
                LevelGate = gate, Decision = decision, Reason = reason,
                PredictedDue = DateOnly.FromDateTime(start).AddDays(need),
                EarlyForm = EarlyForm(fEst, fCyl, fck),
            });
            gateD[zid] = new GateDetail(stack.Count, toppedOut, gFresh, gSup, gRatio, gLim);
        }
        return new EvalFull(outp, gateD);
    }

    /// <summary>[확정] KCS 14 20 40:2024 1.1(2) 일평균기온 4 ℃ 이하 → 한중 콘크리트</summary>
    public const double ColdMeanC = 4.0;
    /// <summary>[확정] 같은 기준 3.4.1(4)·표 3.4-1(보통 노출): 5 MPa 까지 콘크리트 온도 5 ℃ 이상, 5 MPa 도달 뒤 2일간 0 ℃ 이상</summary>
    public const double InitMpa = 5.0, InitMinC = 5.0, AfterMinC = 0.0;
    public const int AfterDays = 2;

    /// <summary>evaluate.py cold_checks(): 한중 콘크리트 초기양생 점검 → 경고 목록. 판정(Evaluate)은 바꾸지 않는 경고만 —
    /// 초기 동해가 의심되면 적산온도 추정 강도를 믿기 어려우므로 공시체·코어로 확인하라는 뜻.
    /// 센서마다 그 센서 온도로 등가재령을 쌓아 5 MPa 도달 시각을 구하고, 그 전 5 ℃ 미만 / 그 뒤 2일 0 ℃ 미만을 찾는다.</summary>
    public static List<ColdWarning> ColdChecks(string folder, DateTime at)
        => Checked(() => Memo("cold_checks", folder, at.Ticks.ToString(Py.Inv), new[] { "zones.csv", "pours.csv", "weather.csv", "sensors.csv" }, () => ColdChecksCore(folder, at))).ToList();   // A-20

    static List<ColdWarning> ColdChecksCore(string folder, DateTime at)
    {
        var zones = new Dictionary<string, Row>();
        foreach (var r in Read(folder, "zones.csv")) zones[r["zone_id"]!] = r;
        var pours = new Dictionary<string, Row>();
        foreach (var r in Read(folder, "pours.csv")) pours[r["zone_id"]!] = r;
        var weather = new Dictionary<string, double>();
        foreach (var r in Read(folder, "weather.csv")) weather[r["date"]!] = Py.Float(r["avgTa"]);
        var sens = SensorsOf(folder);                         // 센서는 한 번 읽어 둔 것 (A-20)
        var byId = sens.BySid();
        static string Min(DateTime t) => t.ToString("yyyy-MM-dd'T'HH:mm", Py.Inv);
        static string G(double x) => x.ToString(Py.Inv);   // Python {x:g} (정수값 상수만)
        var outp = new List<ColdWarning>();
        foreach (var (zid, p) in pours.OrderBy(kv => Py.Int(zones[kv.Key]["floor"])).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var z = zones[zid];
            int fl = Py.Int(z["floor"]);
            var start = FromIso($"{p["pour_date"]}T{(string.IsNullOrEmpty(p.GetValueOrDefault("pour_time")) ? "23:59" : p["pour_time"])}");
            if (start > at)
                continue;
            var (s, ea) = ShoringEngine.Mixes[p["mix"]!];
            double fck = Py.Float(p["fck"]);
            var d0 = DateOnly.FromDateTime(start);
            double mean = weather.TryGetValue(d0.ToString("yyyy-MM-dd", Py.Inv), out var w) ? w : ShoringEngine.AirTemp(d0);
            if (mean <= ColdMeanC)
                outp.Add(new(zid, fl, "한중", Min(start), "", Py.Round(mean, 1),
                    $"{zid}: 타설일 일평균기온 {Py.F(mean, 1)} ℃ ≤ {G(ColdMeanC)} ℃ → 한중 콘크리트 대상 (KCS 14 20 40 1.1(2))"));
            var rect = (Py.Float(z["x0"]), Py.Float(z["y0"]), Py.Float(z["x1"]), Py.Float(z["y1"]));
            var (pts, _) = Near(byId.Where(kv => Py.Int(kv.Value[0]["floor"]) == fl)
                .Select(kv => (kv.Key, Py.Float(kv.Value[0]["x"]), Py.Float(kv.Value[0]["y"]), kv.Value)).ToList(), rect);
            foreach (var (sid, _, _, rows) in pts)
            {
                var recs = sens.Pts("s|" + sid, rows).Where(q => start <= q.T && q.T <= at).ToList();   // 센서마다 한 번만 풀어 둠
                double te = 0.0;
                DateTime? t5 = null;
                (DateTime T, double V)? low = null, frost = null;
                for (int i = 0; i < recs.Count; i++)
                {
                    var (t0, T0) = recs[i];
                    var t1 = i + 1 < recs.Count ? recs[i + 1].T : at;
                    if (t5 is null)
                    {
                        if (T0 < InitMinC && low is null) low = (t0, T0);
                        te += ShoringEngine.AgeFactor(T0, ea) * Math.Max((t1 - t0).TotalSeconds, 0) / 86400;
                        if (ShoringEngine.StrengthAt(te, fck, s) >= InitMpa) t5 = t1;
                    }
                    else if (t0 <= t5.Value.AddDays(AfterDays) && T0 < AfterMinC && frost is null)
                        frost = (t0, T0);
                }
                if (low is { } lo)
                    outp.Add(new(zid, fl, "초기양생 5℃", Min(lo.T), sid, lo.V,
                        $"{zid}: {G(InitMpa)} MPa 도달 전 센서 {sid} {Py.F(lo.V, 1)} ℃ < {G(InitMinC)} ℃ ({lo.T.ToString("MM-dd HH:mm", Py.Inv)}) — 초기 동해 의심, 공시체로 확인 (KCS 14 20 40 3.4.1(4))"));
                if (frost is { } fr)
                    outp.Add(new(zid, fl, "도달 후 2일 0℃", Min(fr.T), sid, fr.V,
                        $"{zid}: {G(InitMpa)} MPa 도달 후 {AfterDays}일 안 센서 {sid} {Py.F(fr.V, 1)} ℃ < {G(AfterMinC)} ℃ ({fr.T.ToString("MM-dd HH:mm", Py.Inv)}) — KCS 14 20 40 3.4.1(4)"));
            }
        }
        return outp;
    }

    /// <summary>[가정] 공시체 평균과 센서 구역 최소 추정이 이 비율 넘게 다르면 경고 — evaluate.py CROSS_TOL</summary>
    public const double CrossTol = 0.25;

    /// <summary>evaluate.py cross_checks(): 현장양생 공시체(기준 근거)와 센서 위치 근거가 크게 어긋나면 경고 (시편 바꿔치기·보관 위치·온도 차·센서 이상).
    /// 센서가 없는 구역(기상 예측만)은 비교하지 않음. 판정은 바꾸지 않음</summary>
    public static List<CrossWarning> CrossChecks(string folder, DateTime at, double tol = CrossTol)
    {
        static string Pct(double x) { var s = Py.F(x * 100, 0); return (s.StartsWith('-') ? s : "+" + s) + "%"; }   // Python {x:+.0%}
        var outp = new List<CrossWarning>();
        foreach (var r in Evaluate(folder, at))
        {
            if (r.FCyl is null || r.EstSource.StartsWith("기상"))
                continue;
            double a = r.FCyl.Value, b = r.FEstMin;
            if (b <= 0)
                continue;
            double d = (a - b) / b;
            if (Math.Abs(d) > tol)
                outp.Add(new(r.ZoneId, r.Floor, "공시체-센서 불일치", a, b, Py.Round(d, 3),
                    $"{r.ZoneId}: 공시체 평균 {Py.F(a, 1)} MPa ↔ 센서 구역 최소 {Py.F(b, 1)} MPa ({Pct(d)}) — "
                    + $"시편 보관 위치·온도·시험 기록과 센서를 확인 (차 {Py.F(tol * 100, 0)}% 넘음 [가정])"));
        }
        return outp;
    }

    /// <summary>evaluate.py RULE_BASIS: 고정 근거 조항 한 줄 (대시보드 판정 근거 창·휴대폰 구역 카드, 총괄 P2) — 판정 사유 문자열에는 안 넣음</summary>
    public const string RuleBasis = "판정 규칙 근거: 해체 가이드라인 3.2 (국토교통부 2023.1) · KCS 14 20 12:2024 3.3.1·3.3.2 · 지지층 14 MPa·하중비 ≤ 1.0";

    /// <summary>[가정] 공시체 3개 (최대 − 최소) 가 평균의 이 비율을 넘으면 확인 문구 (감사 A-29, 규정 값 아님) — evaluate.py CYL_RANGE</summary>
    public const double CylRange = 0.15;

    /// <summary>evaluate.py input_checks(): 입력값 개연성 점검 (감사 A-29) → 경고 목록. 판정은 바꾸지 않는다.
    /// ① pours.csv fck 가 프로젝트 기본 fck(PRJ_DefaultFck, 주면)와 다름 — 오기면 해체 기준(= fck)이 같이 바뀐다
    /// ② 판정에 쓰는 공시체 시험(현장양생, 판정 시각까지 최신 날짜)의 3개 범위 &gt; 평균 × rng [가정]</summary>
    public static List<InputWarning> InputChecks(string folder, DateTime at, double? defaultFck = null, double rng = CylRange)
        => Checked(() =>
        {
            var zones = new Dictionary<string, Row>();
            foreach (var r in Read(folder, "zones.csv")) zones[r["zone_id"]!] = r;
            var day = DateOnly.FromDateTime(at);
            var cyl = CylRows(folder).Where(r => ShoringEngine.FieldCurings.Contains(r.GetValueOrDefault("curing"))
                && DateOnly.ParseExact(r["test_date"]!, "yyyy-MM-dd", Py.Inv) <= day).ToList();
            var outp = new List<InputWarning>();
            foreach (var p in Read(folder, "pours.csv").OrderBy(r => Py.Int(zones[r["zone_id"]!]["floor"])).ThenBy(r => r["zone_id"], StringComparer.Ordinal))
            {
                string zid = p["zone_id"]!;
                int fl = Py.Int(zones[zid]["floor"]);
                double fck = Py.Float(p["fck"]);
                if (defaultFck is { } df && fck != df)
                    outp.Add(new(zid, fl, "fck 확인", $"{zid}: pours.csv fck {Py.G(fck)} MPa ≠ 프로젝트 기본 fck {Py.G(df)} MPa — 설계 강도를 확인하세요 (해체 기준은 pours.csv 값)"));
                var mine = cyl.Where(r => r["zone_id"] == zid).ToList();
                string? last = mine.Select(r => r["test_date"]!).Max(StringComparer.Ordinal);
                foreach (var r in mine)
                {
                    var v = new[] { "f1", "f2", "f3" }.Select(k => Py.Float(r[k])).ToList();
                    double avg = Py.Sum(v) / 3, spread = v.Max() - v.Min();
                    if (r["test_date"] == last && spread > rng * avg)
                        outp.Add(new(zid, fl, "공시체 편차", $"{zid}: 공시체 {last} 3개 범위 {Py.F(spread, 1)} MPa > 평균 {Py.F(avg, 1)} 의 {Py.F(rng * 100, 0)}% [가정] — 시편·시험 기록을 확인하세요 (판정은 그대로)"));
                }
            }
            return outp;
        });

    /// <summary>evaluate.py write(): UTF-8 BOM, CRLF, 필요할 때만 따옴표 (Python csv 와 같은 바이트).</summary>
    public static void Write(IReadOnlyList<ZoneRow> rows, string path)
        => Py.WriteCsv(path, rows.Select(r => r.CsvValues()).Prepend(ZoneRow.CsvHeader));
}

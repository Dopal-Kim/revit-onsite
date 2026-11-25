// shoring/sensor_import.py 의 1:1 C# 이식 (benchmark_gap G5, 총괄 11-05) — 센서 업체 내보내기 CSV 를 손으로 옮기지 않게.
// 업체 CSV(CP949·UTF-8, 열 이름이 업체마다 다름) → 데이터 폴더 sensors.csv(온도)·sensor_strength.csv(강도 열이 있으면).
// 열 찾기·시각 읽기·위치(파일에 없으면 기존 같은 ID 위치, 처음 보는 ID 는 목록)·같은 (센서, 시각) 한 줄 — sensor_import.py 와 같음. 판정·계산 없음.
using System.Globalization;
using System.Text.RegularExpressions;

namespace RevitOnSite.Engine;

/// <summary>sensor_import.py import_csv 결과 — Temp·Mpa = (새 줄, 바뀐 줄), 그 열이 파일에 없으면 null</summary>
public sealed record SensorImportResult(int Rows, (int New, int Changed)? Temp, (int New, int Changed)? Mpa, List<string> NoPosition, int Bad, Dictionary<string, string> Columns);

public static class SensorImport
{
    public static readonly string[] Roles = { "sensor_id", "timestamp", "temp_c", "mpa", "floor", "x", "y" };
    static readonly (string Role, string File)[] Files = { ("temp_c", "sensors.csv"), ("mpa", "sensor_strength.csv") };
    public const string Source = "업체 내보내기 CSV";
    static readonly Regex Ts = new(@"^([0-9]{4})[-/.]([0-9]{1,2})[-/.]([0-9]{1,2})[ T]([0-9]{1,2}):([0-9]{2})(?::[0-9]{2}(?:\.[0-9]+)?)?$", RegexOptions.CultureInvariant);
    static readonly Regex Split = new(@"[^0-9a-z가-힣]+", RegexOptions.CultureInvariant);

    /// <summary>sensor_import.py role_of: 열 이름 → 역할 (없으면 null). 값 역할(강도·온도)을 먼저</summary>
    public static string? RoleOf(string h)
    {
        var l = h.Trim().ToLowerInvariant();
        var toks = Split.Split(l).Where(t => t.Length > 0).ToList();
        if (l is "x" or "x(m)" or "x[m]" or "x_m") return "x";
        if (l is "y" or "y(m)" or "y[m]" or "y_m") return "y";
        if (l.Contains("강도") || l.Contains("mpa") || l.Contains("strength")) return "mpa";
        if (l.Contains("온도") || l.Contains("temp") || l.Contains("°c") || l.Contains("℃")) return "temp_c";
        if (new[] { "시각", "일시", "날짜", "시간" }.Any(l.Contains) || toks.Any(t => t is "time" or "date" or "datetime" or "timestamp")) return "timestamp";
        if (l.Contains('층') || toks.Contains("floor")) return "floor";
        if (l.Contains("센서") || toks.Any(t => t is "sensor" or "serial" or "device" or "id" or "sn")) return "sensor_id";
        return null;
    }

    /// <summary>sensor_import.py columns: {역할: 열 번호} — mapping {열 이름: 역할} 이 먼저, 나머지는 왼쪽부터 처음 맞는 열</summary>
    public static Dictionary<string, int> Columns(List<string> head, IReadOnlyDictionary<string, string>? mapping = null)
    {
        var roles = new Dictionary<string, int>();
        foreach (var (name, r) in mapping ?? new Dictionary<string, string>())
        {
            if (!Roles.Contains(r)) throw new InvalidDataException($"역할 '{r}' 은 없습니다 — {string.Join(", ", Roles)} 중에서");
            int i = head.FindIndex(h => h.Trim() == name.Trim());
            if (i < 0) throw new InvalidDataException($"열 '{name}' 이 파일에 없습니다 — 열: {string.Join(", ", head)}");
            roles[r] = i;
        }
        for (int i = 0; i < head.Count; i++)
        {
            if (roles.ContainsValue(i)) continue;
            var r = RoleOf(head[i]);
            if (r != null && !roles.ContainsKey(r)) roles[r] = i;
        }
        if (!roles.ContainsKey("sensor_id") || !roles.ContainsKey("timestamp") || !(roles.ContainsKey("temp_c") || roles.ContainsKey("mpa")))
            throw new InvalidDataException("센서·시각·온도(또는 강도) 열을 찾지 못함 — --map \"열 이름=역할\" 로 지정하세요. 파일의 열: " + string.Join(", ", head));
        return roles;
    }

    /// <summary>sensor_import.py stamp: 업체 시각 글자 → yyyy-MM-ddTHH:mm (못 읽으면 null)</summary>
    public static string? Stamp(string? s)
    {
        var m = Ts.Match((s ?? "").Trim());
        if (!m.Success) return null;
        int[] g = Enumerable.Range(1, 5).Select(i => int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture)).ToArray();
        try { _ = new DateTime(g[0], g[1], g[2], g[3], g[4], 0); } catch (ArgumentOutOfRangeException) { return null; }
        return $"{g[0]:D4}-{g[1]:D2}-{g[2]:D2}T{g[3]:D2}:{g[4]:D2}";
    }

    static double? Num(string? s) => double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;

    static (List<string>? Head, List<List<string>> Rows) ReadCsv(string path)
    {
        if (!File.Exists(path)) return (null, new());
        var rows = Evaluator.ParseCsv(Py.ReadText(path, Py.Utf8Strict)).Where(r => r.Count > 0).ToList();
        return rows.Count > 0 ? (rows[0], rows.Skip(1).ToList()) : (null, new());
    }

    /// <summary>sensor_import.py import_csv: 업체 CSV → 데이터 폴더 sensors.csv / sensor_strength.csv 합치기</summary>
    public static SensorImportResult Import(string path, string folder, IReadOnlyDictionary<string, string>? mapping = null)
    {
        var recs = Evaluator.ParseCsv(WeatherImport.Text(path)).Where(r => r.Count > 0).ToList();
        if (recs.Count == 0) throw new InvalidDataException($"{path}: 빈 파일");
        var head = recs[0];
        var body = recs.Skip(1).ToList();
        var cols = Columns(head, mapping);
        var known = new Dictionary<string, (string Fl, string X, string Y)>();   // 기존 위치: 두 파일의 sensor_id 마지막 줄
        foreach (var (_, name) in Files)
        {
            var (h, rows) = ReadCsv(Path.Combine(folder, name));
            if (h == null || !new[] { "sensor_id", "floor", "x", "y" }.All(h.Contains)) continue;
            int Ix(string c) => h.IndexOf(c);
            foreach (var r in rows)
            {
                string G(string c) => Ix(c) < r.Count ? r[Ix(c)] : "";
                if (G("sensor_id").Trim().Length > 0) known[G("sensor_id").Trim()] = (G("floor"), G("x"), G("y"));
            }
        }
        string Cell(List<string> r, string k) => cols.TryGetValue(k, out var i) && i < r.Count ? r[i].Trim() : "";
        var outp = Files.ToDictionary(f => f.Role, _ => new List<(string Sid, string Ts, (string, string, string) Pos, string V)>());
        var missing = new List<string>();
        int bad = 0;
        foreach (var r in body)
        {
            string sid = Cell(r, "sensor_id");
            var ts = Stamp(Cell(r, "timestamp"));
            var vals = Files.Where(f => cols.ContainsKey(f.Role)).Select(f => (f.Role, V: Num(Cell(r, f.Role)))).ToList();
            if (sid.Length == 0 || ts is null || vals.All(v => v.V is null)) { bad++; continue; }
            string fl = Cell(r, "floor");
            double? x = Num(Cell(r, "x")), y = Num(Cell(r, "y"));
            (string, string, string) pos;
            if (Regex.IsMatch(fl, "^-?[0-9]+$") && x is { } xv && y is { } yv) pos = (int.Parse(fl, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture), Py.Repr(xv), Py.Repr(yv));
            else if (known.TryGetValue(sid, out var kp)) pos = kp;
            else { if (!missing.Contains(sid)) missing.Add(sid); continue; }
            foreach (var (role, v) in vals)
                if (v is { } vv)
                {   // 파일 안 같은 (센서, 시각) 은 마지막 값 — 자리는 처음 (Python dict)
                    var l = outp[role];
                    int at = l.FindIndex(e => e.Sid == sid && e.Ts == ts);
                    if (at >= 0) l[at] = (sid, ts, pos, Py.Repr(vv)); else l.Add((sid, ts, pos, Py.Repr(vv)));
                }
        }
        (int, int)? Res(string role, string file) => cols.ContainsKey(role) ? Merge(Path.Combine(folder, file), role, outp[role]) : null;
        return new SensorImportResult(body.Count, Res("temp_c", "sensors.csv"), Res("mpa", "sensor_strength.csv"), missing, bad,
            cols.ToDictionary(kv => kv.Key, kv => head[kv.Value]));   // 순서 = 찾은 순서 (Python dict)
    }

    /// <summary>sensor_import.py merge: (sensor_id, timestamp) 한 줄 — 있으면 값이 다를 때만 새 값, 없으면 끝에. 다른 열·줄 순서는 그대로 (UTF-8 BOM·CRLF)</summary>
    static (int New, int Changed) Merge(string path, string valueCol, List<(string Sid, string Ts, (string Fl, string X, string Y) Pos, string V)> recs)
    {
        var (h0, rows0) = ReadCsv(path);
        var head = h0 ?? new List<string> { "sensor_id", "floor", "x", "y", "timestamp", valueCol };
        foreach (var c in new[] { "sensor_id", "floor", "x", "y", "timestamp", valueCol })
            if (!head.Contains(c)) throw new InvalidDataException($"{Path.GetFileName(path)} 의 첫 줄에 '{c}' 열이 없습니다");
        var rows = rows0.Select(r => r.Concat(Enumerable.Repeat("", Math.Max(0, head.Count - r.Count))).ToList()).ToList();
        int Ix(string c) => head.IndexOf(c);
        var at = new Dictionary<(string, string), int>();
        for (int n = 0; n < rows.Count; n++) at[(rows[n][Ix("sensor_id")], rows[n][Ix("timestamp")])] = n;
        int added = 0, changed = 0;
        foreach (var (sid, ts, (fl, x, y), v) in recs)
        {
            if (!at.TryGetValue((sid, ts), out var n))
            {
                var row = Enumerable.Repeat("", head.Count).ToList();
                foreach (var (c, val) in new[] { ("sensor_id", sid), ("floor", fl), ("x", x), ("y", y), ("timestamp", ts), (valueCol, v) }) row[Ix(c)] = val;
                if (head.Contains("entered_by")) row[Ix("entered_by")] = Source;
                rows.Add(row);
                at[(sid, ts)] = rows.Count - 1;
                added++;
            }
            else if (Num(rows[n][Ix(valueCol)]) != Py.Float(v))
            {
                rows[n][Ix(valueCol)] = v;
                changed++;
            }
        }
        if (added > 0 || changed > 0) Py.WriteCsv(path, new[] { head.ToArray() }.Concat(rows.Select(r => r.ToArray())));
        return (added, changed);
    }
}

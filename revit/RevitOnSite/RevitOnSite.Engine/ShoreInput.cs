// shoring/shore_input.py 의 1:1 C# 이식 (v6 2단계 동바리 입력, docs/redesign_v6.md §2 · '자료 형식 확정' 11-08):
//   shore_spec.csv (층·구역 사양·설치일 — 쓰기는 FieldServer 종류 shore_spec), shore_removed.csv (구역 해체 때 지운 동바리 위치), 3개층 확인 표.
//   표 = 층마다 지금 서 있는 본수(shores.csv = 모델 동바리)·해체 본수·설치 누적, 타설된 맨 위층부터 아래로 3개층 [확정] 안인데 0 이면 빨강. 판정은 안 바꿈.
using System.Text;

namespace RevitOnSite.Engine;

public sealed record ShoreTableRow(int Floor, string Label, int Installed, int Removed, int Standing, bool Red, int Spec = 0);   // Spec = W6 사양 계산 본수 (Standing 에 포함)

public static class ShoreInput
{
    public const string Spec = "shore_spec.csv", Removed = "shore_removed.csv", Shores = "shores.csv";
    public static readonly string[] SpecCols = { "floor", "zone_id", "type", "spec", "spacing_x_mm", "spacing_y_mm", "splices", "install_date" };
    public static readonly string[] RemovedCols = { "id", "zone_id", "x", "y", "removed_date", "by", "note" };
    /// <summary>화면 이름 → SHR_Type (install_rules shore_type 값)</summary>
    public static readonly (string Name, string ShrType)[] Types = { ("파이프 서포트", "파이프서포트"), ("시스템 동바리", "시스템") };
    public const int SpacingLo = 200, SpacingHi = 3000;   // [가정] 입력 범위 — 오타 거르기 (값은 조립도에서 [입력])
    public const int SplicesLo = 0, SplicesHi = 9;         // [가정] 입력 범위 — 이음 1개 이하 규칙은 설치 검측이 봄

    /// <summary>실수 → Python str() 글자 (해체 기록 x·y — shore_input.write_removed 와 같은 칸)</summary>
    public static string Repr(double v) => Py.Repr(v);

    static string V(Dictionary<string, string?> r, string k) => (r.GetValueOrDefault(k) ?? "").Trim();

    /// <summary>shore_input.spec_of: 그 층(·구역)에 쓰는 사양 줄 — 구역 줄이 있으면 그 줄, 없으면 층 전체 줄 (같은 키는 뒤 줄). 없으면 null</summary>
    public static Dictionary<string, string?>? SpecOf(string folder, int floor, string zoneId = "")
    {
        var rows = Evaluator.Read(folder, Spec).Where(r => V(r, "floor") == floor.ToString(Py.Inv)).ToList();
        var zone = rows.Where(r => zoneId.Length > 0 && V(r, "zone_id") == zoneId).ToList();
        var whole = rows.Where(r => V(r, "zone_id").Length == 0).ToList();
        return zone.Count > 0 ? zone[^1] : whole.Count > 0 ? whole[^1] : null;
    }

    /// <summary>shore_input.spec_count (W6 구역 본수 모드): 사양 X·Y 간격이 있으면 ⌊구역 평면 면적 ÷ (X×Y)⌋, 없거나 못 읽으면 null</summary>
    public static int? SpecCount(string folder, string zoneId, int floor, (double X0, double Y0, double X1, double Y1) rect)
    {
        var sp = SpecOf(folder, floor, zoneId);
        if (sp is null || !double.TryParse(V(sp, "spacing_x_mm"), System.Globalization.NumberStyles.Float, Py.Inv, out var x) || !double.TryParse(V(sp, "spacing_y_mm"), System.Globalization.NumberStyles.Float, Py.Inv, out var y)) return null;
        x /= 1000; y /= 1000;
        if (x <= 0 || y <= 0) return null;
        return (int)Math.Floor((rect.X1 - rect.X0) * (rect.Y1 - rect.Y0) / (x * y) + 1e-9);   // 1e-9: 나누어떨어질 때 61.9999… → 62
    }

    /// <summary>shore_input.write_removed: 해체 기록 줄 추가 (머리 없으면 만듦, UTF-8·CRLF·필요할 때만 따옴표 = csv.DictWriter). 값은 부르는 쪽이 글자로 (실수는 Py.Repr = Python str)</summary>
    public static int WriteRemoved(string folder, IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        var p = Path.Combine(folder, Removed);
        var sb = new StringBuilder();
        if (!File.Exists(p) || new FileInfo(p).Length == 0) sb.Append(Py.CsvLine(RemovedCols));
        foreach (var r in rows) sb.Append(Py.CsvLine(RemovedCols.Select(k => r.GetValueOrDefault(k, ""))));
        File.AppendAllText(p, sb.ToString(), new UTF8Encoding(false));
        return rows.Count;
    }

    /// <summary>shore_input.table: 3개층 확인 표 (위층부터)</summary>
    public static List<ShoreTableRow> Table(string folder)
    {
        var floorOf = new Dictionary<string, int>();
        var rectOf = new Dictionary<string, (double, double, double, double)>();
        foreach (var r in Evaluator.Read(folder, "zones.csv"))
        {
            floorOf[r["zone_id"] ?? ""] = Py.Int(r["floor"]);
            rectOf[r["zone_id"] ?? ""] = (Py.Float(r["x0"]), Py.Float(r["y0"]), Py.Float(r["x1"]), Py.Float(r["y1"]));
        }
        var order = floorOf.Values.Distinct().Order().ToList();
        var standing = order.ToDictionary(f => f, _ => 0);
        var removed = order.ToDictionary(f => f, _ => 0);
        var model = new Dictionary<string, int>();
        var gone = new HashSet<string>();
        foreach (var r in Evaluator.Read(folder, Shores)) if (floorOf.TryGetValue(V(r, "zone_id"), out var f)) { standing[f]++; model[V(r, "zone_id")] = model.GetValueOrDefault(V(r, "zone_id")) + 1; }
        foreach (var r in Evaluator.Read(folder, Removed)) if (floorOf.TryGetValue(V(r, "zone_id"), out var f)) { removed[f]++; gone.Add(V(r, "zone_id")); }
        var spec = order.ToDictionary(f => f, _ => 0);   // W6 구역 본수 모드 (사양 계산)
        foreach (var (z, f) in floorOf)
            if (model.GetValueOrDefault(z) == 0 && !gone.Contains(z) && SpecCount(folder, z, f, rectOf[z]) is int n && n > 0) { spec[f] += n; standing[f] += n; }
        var poured = Evaluator.Read(folder, "pours.csv").Where(p => p.GetValueOrDefault("zone_id") is string z && floorOf.ContainsKey(z)).Select(p => floorOf[p["zone_id"]!]).ToHashSet();
        var need = new HashSet<int>();
        if (poured.Count > 0)
        {
            int i = poured.Max(f => order.IndexOf(f));
            for (int k = Math.Max(0, i - (ShoringEngine.MinLevels - 1)); k <= i; k++) need.Add(order[k]);
        }
        return order.AsEnumerable().Reverse().Select(f => new ShoreTableRow(f,
            FloorSign.FloorName(f, floorOf.Where(kv => kv.Value == f).Select(kv => kv.Key).Order(StringComparer.Ordinal).FirstOrDefault()),
            standing[f] + removed[f], removed[f], standing[f], need.Contains(f) && standing[f] == 0, spec[f])).ToList();
    }

    /// <summary>shore_input.table_text</summary>
    public static List<string> TableText(IEnumerable<ShoreTableRow> rows) => rows.Select(r => $"{r.Label} 설치 {r.Installed} · 해체 {r.Removed} · 지금 {r.Standing}" + (r.Spec > 0 ? $" (사양 계산 {r.Spec})" : "")
        + (r.Red ? $" — 빨강: 타설층 포함 {ShoringEngine.MinLevels}개층 존치 필요인데 동바리 없음" : "")).ToList();
}

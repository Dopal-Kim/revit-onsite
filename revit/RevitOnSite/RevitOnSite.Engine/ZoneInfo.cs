// shoring/zone_info.py 의 1:1 C# 이식 (v6.1 [구역 정보] 창 — 구역 하나 모아 보기, docs/redesign_v6.md v6.1 §3).
//   데이터 폴더 CSV 만 읽고 계산·판정은 하지 않는다: 구역(zones.csv·shores.csv) · 사양(구역 줄 > 층 줄) · 타설(뒤 줄) · 공시체(시험일 순, 현장양생 = 기준 근거)
//   · 센서(같은 층·구역 3 m 안, 위치 = 첫 줄) · 설치 검측(기록 수·마지막·풀리지 않은 NG) · 해체 기록. 입력은 FieldServer.SubmitLocal (휴대폰과 같은 검사).
//   Engine.Check 가 Dump 글자를 Python 과 비교.
using System.Text;
using Row = System.Collections.Generic.Dictionary<string, string?>;

namespace RevitOnSite.Engine;

public sealed record ZoneCylinder(string TestDate, string Curing, string[] F, double? Avg, string Basis, string Lot = "");   // Lot = W1 타설 묶음 공통 시험 ("P-9F-1 · 6구역 공통")
public sealed record ZoneSensor(string Kind, string SensorId, string X, string Y, bool Inside, int Count, string LastTs, string LastValue);
public sealed record ZoneInfoResult(string ZoneId, int Floor, string Label, double AreaM2, string SlabMm, int Shores,
    Dictionary<string, string>? Spec, Dictionary<string, string>? Pour, List<ZoneCylinder> Cylinders, List<ZoneSensor> Sensors,
    int InstallRecords, string InstallLast, string OpenNg, int RemovedCount, string RemovedLast);

public static class ZoneInfo
{
    static readonly (string File, string Kind, string Col)[] SensorFiles = { ("sensors.csv", "온도", "temp_c"), ("sensor_strength.csv", "강도", "mpa") };

    static string V(Row r, string k) => (r.GetValueOrDefault(k) ?? "").Trim();

    /// <summary>zone_info.zone_info — at 을 주면 설치 검측·해체 기록은 그날까지. 구역이 zones.csv 에 없으면 ArgumentException</summary>
    public static ZoneInfoResult Of(string folder, string zoneId, DateOnly? at = null)
    {
        var zones = Evaluator.Read(folder, "zones.csv");
        var z = zones.FirstOrDefault(r => V(r, "zone_id") == zoneId) ?? throw new ArgumentException($"구역 '{zoneId}' 이(가) zones.csv 에 없습니다");
        int fl = Py.Int(z["floor"]);
        double x0 = Py.Float(z["x0"]), y0 = Py.Float(z["y0"]), x1 = Py.Float(z["x1"]), y1 = Py.Float(z["y1"]);
        var onFloor = zones.Where(r => Py.Int(r["floor"]) == fl).Select(r => V(r, "zone_id")).Order(StringComparer.Ordinal).ToList();
        int shores = Evaluator.Read(folder, ShoreInput.Shores).Count(r => V(r, "zone_id") == zoneId);

        var sp = ShoreInput.SpecOf(folder, fl, zoneId);
        Dictionary<string, string>? spec = null;
        if (sp != null)
        {
            spec = ShoreInput.SpecCols.ToDictionary(k => k, k => V(sp, k));
            spec["source"] = V(sp, "zone_id").Length > 0 ? "구역" : "층 전체";
        }

        var pours = Evaluator.Read(folder, "pours.csv").Where(r => V(r, "zone_id") == zoneId).ToList();
        var pour = pours.Count == 0 ? null : new[] { "pour_date", "pour_time", "mix", "fck", "pour_id" }.ToDictionary(k => k, k => V(pours[^1], k));   // pour_id = W1 타설 묶음

        var cyl = new List<(ZoneCylinder C, int I)>();
        int i = -1;
        foreach (var r in Evaluator.CylRows(folder))   // W1: 타설 묶음 공통 시험도 이 구역 줄로
        {
            i++;
            if (V(r, "zone_id") != zoneId) continue;
            var fs = new[] { V(r, "f1"), V(r, "f2"), V(r, "f3") };
            double? avg = fs.All(f => double.TryParse(f, System.Globalization.NumberStyles.Float, Py.Inv, out _)) ? Py.Sum(fs.Select(Py.Float)) / 3 : null;
            var td = V(r, "test_date");
            cyl.Add((new ZoneCylinder(td[..Math.Min(10, td.Length)], V(r, "curing"), fs, avg, ShoringEngine.FieldCurings.Contains(V(r, "curing")) ? "기준 근거" : "현장양생 아님", V(r, "cyl_lot")), i));
        }
        var cylinders = cyl.OrderBy(c => c.C.TestDate, StringComparer.Ordinal).ThenBy(c => c.I).Select(c => c.C).ToList();

        var sensors = new List<ZoneSensor>();
        foreach (var (file, kind, col) in SensorFiles)
        {
            var by = new Dictionary<string, List<Row>>();
            foreach (var r in Evaluator.Read(folder, file))
                if (V(r, "sensor_id") is { Length: > 0 } sid) (by.TryGetValue(sid, out var l) ? l : by[sid] = new List<Row>()).Add(r);
            foreach (var (sid, rs) in by)
            {
                double x, y;
                try
                {
                    if (Py.Int(rs[0]["floor"]) != fl) continue;
                    x = Py.Float(rs[0]["x"]); y = Py.Float(rs[0]["y"]);
                }
                catch (Exception ex) when (ex is FormatException or ArgumentNullException or KeyNotFoundException or OverflowException) { continue; }   // 손으로 고친 줄 — 그 센서만 빼고
                double m = Evaluator.SensorMarginM;
                if (!(x0 - m <= x && x <= x1 + m && y0 - m <= y && y <= y1 + m)) continue;   // 3 m 밖 (판정에 안 쓰는 센서, evaluate.near)
                var last = rs[0];
                foreach (var r in rs) if (string.CompareOrdinal(V(r, "timestamp"), V(last, "timestamp")) > 0) last = r;   // Python max = 첫 최댓값
                var ts = V(last, "timestamp");
                sensors.Add(new ZoneSensor(kind, sid, V(rs[0], "x"), V(rs[0], "y"), x0 <= x && x <= x1 && y0 <= y && y <= y1, rs.Count, ts[..Math.Min(16, ts.Length)], V(last, col)));
            }
        }
        sensors = sensors.OrderBy(s => s.Kind, StringComparer.Ordinal).ThenBy(s => s.SensorId, StringComparer.Ordinal).ToList();

        DateTime? cut = at?.ToDateTime(TimeOnly.MinValue);
        var recs = ShoreInspect.Records(folder, cut).Where(r => r.ZoneId == zoneId).ToList();
        string openNg = ShoreInspect.NgText(ShoreInspect.OpenNg(folder, cut).GetValueOrDefault(zoneId) ?? new());

        string atIso = at is DateOnly a ? Py.Iso(a) : "";
        var rem = Evaluator.Read(folder, ShoreInput.Removed).Where(r => V(r, "zone_id") == zoneId)
            .Select(r => V(r, "removed_date")).Select(d => d[..Math.Min(10, d.Length)]).Where(d => at == null || string.CompareOrdinal(d, atIso) <= 0).ToList();

        return new ZoneInfoResult(zoneId, fl, FloorSign.FloorName(fl, onFloor.FirstOrDefault()), (x1 - x0) * (y1 - y0), V(z, "slab_mm"), shores, spec, pour, cylinders, sensors,
            recs.Count, recs.Count > 0 ? recs[^1].T.ToString("yyyy-MM-dd", Py.Inv) + " " + recs[^1].Stage : "", openNg,
            rem.Count, rem.Count > 0 ? rem.Max(StringComparer.Ordinal)! : "");
    }

    static string N(double? v) => v is double d ? d.ToString("0.00", Py.Inv) : "-";

    /// <summary>zone_info.dump — 창·비교용 글자</summary>
    public static string Dump(ZoneInfoResult z)
    {
        var sb = new StringBuilder();
        sb.Append($"zone|{z.ZoneId}|{z.Floor.ToString(Py.Inv)}|{z.Label}|{N(z.AreaM2)}|{z.SlabMm}|{z.Shores}\n");
        sb.Append(z.Spec == null ? "spec|-\n" : "spec|" + string.Join("|", new[] { z.Spec["source"] }.Concat(ShoreInput.SpecCols.Select(k => z.Spec[k]))) + "\n");
        sb.Append(z.Pour == null ? "pour|-\n" : $"pour|{z.Pour["pour_date"]}|{z.Pour["pour_time"]}|{z.Pour["mix"]}|{z.Pour["fck"]}|{z.Pour["pour_id"]}\n");
        foreach (var c in z.Cylinders) sb.Append($"cyl|{c.TestDate}|{c.Curing}|{string.Join("|", c.F)}|{N(c.Avg)}|{c.Basis}|{c.Lot}\n");
        foreach (var s in z.Sensors) sb.Append($"sensor|{s.Kind}|{s.SensorId}|{s.X}|{s.Y}|{(s.Inside ? "안" : "3 m 안")}|{s.Count}|{s.LastTs}|{s.LastValue}\n");
        sb.Append($"install|{z.InstallRecords}|{z.InstallLast}|{z.OpenNg}\n");
        sb.Append($"removed|{z.RemovedCount}|{z.RemovedLast}\n");
        return sb.ToString();
    }
}

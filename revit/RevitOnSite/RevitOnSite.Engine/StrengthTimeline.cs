// shoring/strength_timeline.py 의 1:1 C# 이식 (v6 3단계 강도 예측 타임라인, docs/redesign_v6.md §4):
//   날짜마다 층별 추정 강도(그 층에서 그날까지 타설된 구역 중 최소 — 보수 측), 구역별 추정, 현장양생 공시체 실측 점, 기준선 5·14·fck.
//   추정 = 판정의 예측 해체 가능일과 같은 식 (ShoringEngine.Predict, 기온 = weather.csv 또는 평년, 층 높이 보정) — 화면엔 '참고 (판정에 안 씀)'.
//   Dump 는 Python dump() 와 바이트 동일 (Engine.Check).
using System.Text;

namespace RevitOnSite.Engine;

public sealed record StrengthFloor(int Floor, string Label, double Fck, List<double?> Values);
public sealed record StrengthCylinder(DateOnly Date, string ZoneId, int Floor, double Mpa, string Curing);
public sealed record StrengthTimelineResult(DateOnly? Start, DateOnly? End, List<DateOnly> Dates, List<StrengthFloor> Floors,
    List<(string ZoneId, List<double?> Values)> Zones, List<StrengthCylinder> Cylinders, List<(string Name, double Mpa)> Lines);

public static class StrengthTimeline
{
    public static readonly double[] Steps = { 0.4, 0.6, 0.8, 1.0 };   // [가정] 화면 색 구간 (추정 ÷ fck)
    public const int SpanDays = 56;                                    // [가정] 끝 날짜 기본 = 마지막 타설 + 56일
    const int MaxAge = 399;                                            // 기온 목록 400일 (evaluate 예측일과 같은 한도)

    static string V(Dictionary<string, string?> r, string k) => r.GetValueOrDefault(k) ?? "";

    /// <summary>strength_timeline.label_of: 첫 구역 ID 의 '-' 앞, 없으면 번호 (음수 = B)</summary>
    public static string LabelOf(int fl, IReadOnlyList<string> zoneIds)
    {
        var head = zoneIds.Count > 0 && zoneIds[0].Contains('-') ? zoneIds[0].Split('-')[0] : "";
        return head.Length > 0 ? head : fl < 0 ? $"B{-fl}" : $"{fl}F";
    }

    /// <summary>strength_timeline.step_of: 색 구간 0–4 (null = 값 없음)</summary>
    public static int? StepOf(double? mpa, double fck)
    {
        if (mpa is not { } m) return null;
        var r = m / fck;
        for (int i = 0; i < Steps.Length; i++) if (r < Steps[i]) return i;
        return Steps.Length;
    }

    /// <summary>strength_timeline.timeline</summary>
    public static StrengthTimelineResult Timeline(string folder, DateOnly? start = null, DateOnly? end = null, double cureMin = 5.0)
    {
        var floorOf = new Dictionary<string, int>();
        foreach (var z in Evaluator.Read(folder, "zones.csv")) floorOf[V(z, "zone_id")] = Py.Int(z["floor"]);
        var pours = new Dictionary<string, Dictionary<string, string?>>();
        foreach (var p in Evaluator.Read(folder, "pours.csv")) if (floorOf.ContainsKey(V(p, "zone_id"))) pours[V(p, "zone_id")] = p;
        var weather = Evaluator.Read(folder, "weather.csv");
        var hopt = Evaluator.HeightOption(folder);
        if (pours.Count == 0) return new(null, null, new(), new(), new(), new(), new());
        var cast = pours.ToDictionary(kv => kv.Key, kv => DateOnly.ParseExact(V(kv.Value, "pour_date"), "yyyy-MM-dd", Py.Inv));
        var d0 = start ?? cast.Values.Min();
        var d1 = end ?? cast.Values.Max().AddDays(SpanDays);
        var dates = Enumerable.Range(0, Math.Max(0, d1.DayNumber - d0.DayNumber + 1)).Select(i => d0.AddDays(i)).ToList();
        var zv = new List<(string ZoneId, List<double?> Values)>();
        foreach (var z in pours.Keys.OrderBy(z => floorOf[z]).ThenBy(z => z, StringComparer.Ordinal))
        {
            var p = pours[z];
            var c = cast[z];
            var dt = Evaluator.HeightDt(floorOf[z], hopt);
            var t400 = Evaluator.DailyTemps(weather, c, 400).Select(T => T + dt).ToList();
            double fck = Py.Float(p["fck"]);
            zv.Add((z, dates.Select(d => d < c ? (double?)null
                : Py.Round(ShoringEngine.Predict(V(p, "mix"), c, Math.Min(d.DayNumber - c.DayNumber, MaxAge), fck, cureMin, t400), 1)).ToList()));
        }
        var byZone = zv.ToDictionary(x => x.ZoneId, x => x.Values);
        var floors = new List<StrengthFloor>();
        foreach (var fl in pours.Keys.Select(z => floorOf[z]).Distinct().Order())
        {
            var zs = pours.Keys.Where(z => floorOf[z] == fl).Order(StringComparer.Ordinal).ToList();
            var vals = dates.Select((_, i) => zs.Select(z => byZone[z][i]).Min()).ToList();   // Nullable Min: null 건너뜀, 모두 null 이면 null
            var all = floorOf.Where(kv => kv.Value == fl).Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList();
            floors.Add(new(fl, LabelOf(fl, all), zs.Min(z => Py.Float(pours[z]["fck"])), vals));
        }
        var cyl = new List<StrengthCylinder>();
        foreach (var r in Evaluator.CylRows(folder))   // W1: 타설 묶음 공통 시험 → 묶음의 구역마다
        {
            var zid = r.GetValueOrDefault("zone_id");
            if (r.GetValueOrDefault("curing") is string cu && ShoringEngine.FieldCurings.Contains(cu) && zid is not null && floorOf.TryGetValue(zid, out var f))
                cyl.Add(new(DateOnly.ParseExact(V(r, "test_date"), "yyyy-MM-dd", Py.Inv), zid, f,
                    Py.Round(Py.Sum(new[] { "f1", "f2", "f3" }.Select(k => Py.Float(r[k]))) / 3, 1), cu));
        }
        var fcks = pours.Values.Select(p => Py.Float(p["fck"])).Distinct().Order().ToList();
        var th = ShoringEngine.Thresholds(fcks[0]);
        var lines = new List<(string, double)> { ("측면 거푸집", th["측면"]), ("지지 슬래브", th["지지 슬래브"]) };
        lines.AddRange(fcks.Select(f => ($"fck {Py.G(f)}", f)));
        return new(d0, d1, dates, floors, zv, cyl.OrderBy(c => c.Date).ThenBy(c => c.ZoneId, StringComparer.Ordinal).ToList(), lines);
    }

    /// <summary>strength_timeline.dump: 대조용 글 (Python 과 바이트 동일)</summary>
    public static string Dump(StrengthTimelineResult res)
    {
        static string Vs(double? x) => x is { } v ? Py.G(v) : "-";
        static string Ds(DateOnly? d) => d is { } v ? Py.Iso(v) : "None";
        var o = new List<string> { $"range|{Ds(res.Start)}|{Ds(res.End)}|{res.Dates.Count}" };
        o.AddRange(res.Lines.Select(l => $"line|{l.Name}|{Py.G(l.Mpa)}"));
        o.AddRange(res.Floors.Select(f => $"floor|{f.Floor}|{f.Label}|{Py.G(f.Fck)}|" + string.Join(",", f.Values.Select(Vs))));
        o.AddRange(res.Zones.Select(z => $"zone|{z.ZoneId}|" + string.Join(",", z.Values.Select(Vs))));
        o.AddRange(res.Cylinders.Select(c => $"cyl|{Py.Iso(c.Date)}|{c.ZoneId}|{c.Floor}|{Py.G(c.Mpa)}|{c.Curing}"));
        return string.Join("\n", o) + "\n";
    }
}

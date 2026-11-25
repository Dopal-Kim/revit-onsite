// shoring/engine.py 의 1:1 C# 이식. 계산 기준(정답)은 Python 쪽이다.
// 어느 한쪽을 고치면 다른 쪽도 같이 고치고 RevitOnSite.Engine.Check 로 일치 시험을 다시 돌린다.
//
// 1) 강도 예측 : KDS 14 20 10  f_cm(t) = β(t)·(fck+Δf), β = exp{s[1 − (28/t)^0.5]},
//               t = 온도 보정 재령 Σ Δt·exp[Ea/R·(1/293 − 1/(273+T))] (fib MC2010)
// 2) 보정      : 현장양생 공시체 실측으로 s 역산
// 3) 해체 기준 : 「거푸집 및 동바리 해체 가이드라인」(국토교통부 2023.1, 작성 한국콘크리트학회·국가건설기준센터)
// 4) 하중비교  : 같은 가이드라인 2.1.2(2) 예제식, 지지 슬래브 여유 × √(f/fck) (ACI SP-4 C법)
// 5) 구역 매핑 : 센서 좌표의 등가재령을 IDW(p=2, Utepov 외 2019)로 보간 → 구역 최소 강도로 판정
namespace RevitOnSite.Engine;

/// <summary>floor_plan 한 행 (Python dict 키를 주석으로).</summary>
public sealed record FloorPlanRow(
    int Floor,                  // 층
    DateOnly Pour,              // 타설
    DateOnly? FckReached,       // fck 도달 (400일 안에 못 닿으면 null — 감사 A-28)
    int? RequiredLevels,        // 필요 존치 층수 (타설 1건·사이클 모름이면 null)
    DateOnly? PlannedRemoval,   // 예정 해체
    double? StrengthAtRemoval,  // 예정 해체 시 강도
    double? MinSupportStrength, // 지지 최소 강도
    double LoadRatio,           // 하중비
    string Verdict);            // 판정 "적합" / "위반"

public static class ShoringEngine
{
    public const double RGas = 8.314;

    // 배합: (s, Ea kJ/mol). s: KDS 1종 0.35·3종 0.25, 3성분 0.54는 Jeong 외 2024 3일 강도비 0.33에 맞춤,
    // 슬래그 미분말 혼합은 보간 (shortcut: 현장 1–3일 공시체로 calibrate_s 해서 교체). Ea: Vollpracht 외 2018
    public static readonly IReadOnlyDictionary<string, (double S, double Ea)> Mixes = new Dictionary<string, (double S, double Ea)>
    {
        ["조강"] = (0.25, 33.3),
        ["보통"] = (0.35, 33.3),
        ["슬래그 미분말"] = (0.45, 38.0),
        ["슬래그·FA 3성분"] = (0.54, 41.6),
    };

    public static readonly double[] SeoulT = { -1.9, 0.7, 6.1, 12.6, 18.2, 22.7, 25.3, 26.1, 21.6, 15.0, 7.5, 0.2 };   // 서울 월평균 °C (평년값 근사)

    /// <summary>기상 자료가 없을 때 쓰는 평년 기온 (월 중순 값 선형 보간). shortcut: 기상청 ASOS 일평균으로 교체.</summary>
    public static double AirTemp(DateOnly d)
    {
        double m = d.Month - 1 + (d.Day - 15) / 30.0;
        int i = (int)Math.Floor(m);
        return SeoulT[Py.Mod(i, 12)] + (m - i) * (SeoulT[Py.Mod(i + 1, 12)] - SeoulT[Py.Mod(i, 12)]);
    }

    /// <summary>KDS 14 20 10: fck ≤ 40 → 4, ≥ 60 → 6, 사이 보간.</summary>
    public static double DeltaF(double fck) => fck <= 40 ? 4.0 : fck >= 60 ? 6.0 : 4.0 + (fck - 40) / 10;

    /// <summary>1 시간(또는 1 일) 동안 온도 T 의 20 °C 환산 계수.</summary>
    public static double AgeFactor(double T, double ea) => Math.Exp(ea * 1000 / RGas * (1.0 / 293 - 1 / (273 + T)));

    /// <summary>온도 기록(같은 간격) → 20 °C 등가재령(일).</summary>
    public static double EquivalentAge(IEnumerable<double> temps, double ea, double stepDays = 1.0)
        => Py.Sum(temps.Select(T => AgeFactor(T, ea))) * stepDays;

    public static double StrengthAt(double te, double fck, double s)
        => (fck + DeltaF(fck)) * Math.Exp(s * (1 - Math.Sqrt(28 / Math.Max(te, 0.05))));

    /// <summary>타설일 cast부터 ageDays 일 강도 MPa. temps 가 없으면 평년 기온, 양생 온도 = max(기온, cureMin).</summary>
    /// s = 현장 보정 곡선 값 (Evaluator.Calibration, G2 — 예측일에만), null 이면 배합 기본값 (engine.py predict s)
    public static double Predict(string mix, DateOnly cast, double ageDays, double fck, double cureMin = 5.0, IReadOnlyList<double>? temps = null, double? sCal = null)
    {
        var (s0, ea) = Mixes[mix];
        var s = sCal ?? s0;
        int days = (int)ageDays;
        IEnumerable<double> tl = temps is { Count: > 0 }
            ? temps.Take(days >= 0 ? days : Math.Max(0, temps.Count + days))   // Python temps[:days] (음수면 뒤에서 자름)
            : Enumerable.Range(0, Math.Max(days, 0)).Select(i => AirTemp(cast.AddDays(i)));
        return StrengthAt(EquivalentAge(tl.Select(T => Math.Max(T, cureMin)), ea), fck, s);
    }

    /// <summary>공시체 실측 강도와 그때 등가재령으로 s 역산. 0 &lt; te &lt; 28 일 때만 의미 있음 — 아니면 null (감사 A-28).</summary>
    public static double? CalibrateS(double fMeasured, double te, double fck)
        => te is > 0 and < 28 ? Math.Log(fMeasured / (fck + DeltaF(fck))) / (1 - Math.Sqrt(28 / te)) : null;

    /// <summary>가이드라인 표 2.1.1·2.1.2(2)⑧ 해체 기준 MPa.</summary>
    // KCS 14 20 12:2024 표 3.3-1 (측면은 내구성 중요 시 10), 가이드라인 2.1.2(2)⑧ [확정]
    public static Dictionary<string, double> Thresholds(double fck) => new()
    {
        ["측면"] = 5.0, ["슬래브 밑면(단층)"] = Math.Max(2.0 / 3 * fck, 14.0), ["다층 동바리 해체"] = fck,
        ["필러 동바리 조기 탈형"] = 14.0, ["지지 슬래브"] = 14.0,
    };

    public const int MinLevels = 3;   // 가이드라인 2.1.2(1)④: 타설층 포함 3개층 이상 존치(또는 재받침)
    /// <summary>해체 판단에 쓰는 현장양생 공시체 (표준양생은 기준 근거 아님 — 검단 2023) — engine.py FIELD_CURINGS</summary>
    public static readonly string[] FieldCurings = { "현장봉함", "현장수중", "현장기건" };

    public const double ShoreSelf = 0.5;   // kN/m² 지지층 1개층분 동바리 자중 (가이드라인 예제 ALU 서포트 가정치, regulation_check.md)

    /// <summary>가이드라인 예제 1.2(5.44 + 0.5·n) + 1.6·2.5 의 일반형 kN/m². extra 를 안 주면 동바리 자중 0.5 × 지지 층수 n
    /// (예제 n = 3 → 1.5).</summary>
    public static double PourLoad(double tSlab = 0.21, double form = 0.4, double? extra = null, double live = 2.5, int nSupport = MinLevels)
        => 1.2 * (24 * tSlab + form + (extra ?? ShoreSelf * nSupport)) + 1.6 * live;

    /// <summary>지지 슬래브 1개층 여유 내력 kN/m² (아직 안 올라간 마감 하중 + 설계 활하중).</summary>
    public static double SlabSpare(double sdl = 1.64, double ll = 2.0) => 1.2 * sdl + 1.6 * ll;

    /// <summary>supportStrengths: 동바리가 딛고 있는 아래 슬래브들의 현재 강도. null = 기초가 받침.
    /// 반환 (하중/여유 비, 최소 지지 강도). × √(f/fck) 감소는 가이드라인 예제에 없는 보수 측 추가 [가정].
    /// nSupport 를 안 주면 지지 층수 = supportStrengths 개수 (Python kw.setdefault).</summary>
    public static (double Ratio, double? FSup) LoadCheck(IReadOnlyList<double?> supportStrengths, double fck,
        double tSlab = 0.21, double form = 0.4, double? extra = null, double live = 2.5, int? nSupport = null, double sdl = 1.64, double ll = 2.0)
    {
        if (supportStrengths.Any(f => f is null))
            return (0.0, null);
        double cap = Py.Sum(supportStrengths.Select(f => SlabSpare(sdl, ll) * Math.Sqrt(Math.Min(f!.Value / fck, 1.0))));   // sdl·ll = 구조계산서 값 (11-11 G1, 기본 = 가이드라인 예제)
        if (cap == 0) throw new DivideByZeroException("지지 슬래브가 없음 (Python: ZeroDivisionError)");
        return (PourLoad(tSlab, form, extra, live, nSupport ?? supportStrengths.Count) / cap, supportStrengths.Min());
    }

    /// <summary>pours: [(층, 타설일, 배합)] 아래층부터. 층별 해체 판정 표.
    /// nLevels 개 층 존치 → 층 k 아래 동바리는 k+n 층 타설 직전에 해체.
    /// cycle: 골조 사이클(일) — null 이면 첫 두 타설 간격, 타설이 1건뿐이면 필요 존치 층수 null (감사 A-28).
    /// 400일 안에 fck 에 못 닿으면 FckReached·RequiredLevels null, 판정 "위반".</summary>
    public static List<FloorPlanRow> FloorPlan(IReadOnlyList<(int Floor, DateOnly Date, string Mix)> pours, double fck, int nLevels,
        double cureMin = 5.0, IReadOnlyList<double>? temps = null, int? cycle = null)
    {
        if (cycle is null && pours.Count > 1) cycle = pours[1].Date.DayNumber - pours[0].Date.DayNumber;
        var rows = new List<FloorPlanRow>();
        for (int k = 0; k < pours.Count; k++)
        {
            var (fl, d, mix) = pours[k];
            int? need = Py.FirstDayOrNull(a => Predict(mix, d, a, fck, cureMin, temps) >= fck);
            DateOnly? rm = k + nLevels < pours.Count ? pours[k + nLevels].Date : null;
            double? fRm = rm is { } r ? Predict(mix, d, r.DayNumber - d.DayNumber, fck, cureMin, temps) : null;
            var sup = new List<double?>();
            for (int j = k - 1; j > k - 1 - nLevels; j--)
                sup.Add(j < 0 ? null : Predict(pours[j].Mix, pours[j].Date, d.DayNumber - pours[j].Date.DayNumber, fck, cureMin, temps));
            var (ratio, fSup) = LoadCheck(sup, fck);
            rows.Add(new FloorPlanRow(fl, d, need is { } n ? d.AddDays(n) : null,
                need is { } n2 && cycle is { } c ? Math.Max(MinLevels, (int)Math.Ceiling((double)n2 / Math.Max(c, 1))) : null,
                rm, fRm, fSup, ratio,
                nLevels < MinLevels || need is null || (fRm is not null && fRm < fck) || (fSup is not null && fSup < 14.0) || ratio > 1 ? "위반" : "적합"));
        }
        return rows;
    }

    /// <summary>points [(x,y)], values [v] → (x,y) 보간값. 센서 위치에서는 그 값.</summary>
    public static double Idw(IReadOnlyList<(double X, double Y)> points, IReadOnlyList<double> values, double x, double y, double p = 2)
    {
        double num = 0.0, den = 0.0;
        for (int i = 0; i < Math.Min(points.Count, values.Count); i++)
        {
            double d = double.Hypot(x - points[i].X, y - points[i].Y);
            if (d < 1e-9)
                return values[i];
            double w = Math.Pow(d, -p);
            (num, den) = (num + w * values[i], den + w);
        }
        if (den == 0) throw new DivideByZeroException("센서가 없음 (Python: ZeroDivisionError)");
        return num / den;
    }

    /// <summary>sensors: [(id, x, y, 등가재령)], zones: {이름: (x0,y0,x1,y1)} → {이름: (최소 강도, 위치)}.
    /// 구역이 step/2 보다 좁으면 값은 null (Python None).</summary>
    public static Dictionary<string, (double F, (double X, double Y) Xy)?> ZoneMinStrength(
        IReadOnlyList<(string Id, double X, double Y, double Te)> sensors,
        IReadOnlyDictionary<string, (double X0, double Y0, double X1, double Y1)> zones, double fck, string mix, double step = 0.5)
    {
        double s = Mixes[mix].S;
        var pts = sensors.Select(a => (a.X, a.Y)).ToList();
        var tes = sensors.Select(a => a.Te).ToList();
        return zones.ToDictionary(kv => kv.Key, kv => ZoneMinFromValues(pts, tes, kv.Value, step, te => StrengthAt(te, fck, s)));
    }

    /// <summary>구역 rect (x0,y0,x1,y1) 의 step 격자 가운데점마다 IDW(values) → f → (최소, 위치). 구역이 step/2 보다 좁으면 null (Python None).
    /// 강도 센서는 values = MPa 그대로(f 없음), 온도 센서는 values = 등가재령, f = 강도 곡선.</summary>
    public static (double F, (double X, double Y) Xy)? ZoneMinFromValues(IReadOnlyList<(double X, double Y)> points, IReadOnlyList<double> values,
        (double X0, double Y0, double X1, double Y1) rect, double step = 0.5, Func<double, double>? f = null)
    {
        (double F, (double X, double Y) Xy)? best = null;
        double x = rect.X0 + step / 2;   // 누적 덧셈 그대로 (x0 + i·step 로 바꾸면 경계·반올림이 달라질 수 있음)
        while (x < rect.X1)
        {
            double y = rect.Y0 + step / 2;
            while (y < rect.Y1)
            {
                double v = Idw(points, values, x, y);
                if (f is not null) v = f(v);
                if (best is null || v < best.Value.F)
                    best = (v, (Py.Round(x, 2), Py.Round(y, 2)));
                y += step;
            }
            x += step;
        }
        return best;
    }

    /// <summary>판정 우선순위: 공시체(기준 근거)와 센서 구역 최소(위치 근거) 둘 다 기준 이상일 때만 승인 요청.
    /// fZoneMin = null → 구역 3 m 안 센서가 없음: 공시체만 (먼 센서·기상 예측 추정은 참고 — engine.py zone_decision, 정의서 §3.4-3, 34차)</summary>
    public static string ZoneDecision(double? fZoneMin, double? fCylinder, double threshold)
    {
        if (fCylinder is null)
            return "공시체 시험 필요";
        if (fCylinder >= threshold && (fZoneMin is null || fZoneMin >= threshold))
            return "해체 승인 요청 가능";
        return fCylinder < threshold ? "존치 (공시체 미달)" : "존치 (구역 최소 강도 미달)";
    }
}

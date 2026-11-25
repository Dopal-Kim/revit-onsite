// shoring/install_rules.py 의 1:1 C# 이식: 동바리 설치 검측 규칙 (타설 전) — 정의서 §3.2 '설계 하중'·'설치 검측' 표.
// ShoringEngine(engine.py) 에 없는 설치 단계 규칙만 여기 둔다 (해체·강도는 ShoringEngine).
namespace RevitOnSite.Engine;

/// <summary>check_shore 결과 한 항목. Result: "OK" / "NG"(고쳐야 함) / "필요"(서류·검토 필요).</summary>
public sealed record InstallCheck(string Item, string Result, string Reason, string Source);

/// <summary>align_check 결과 한 층 쌍 (install_rules.py dict 키: upper, lower, n, max_mm, p95_mm, result, reason, source)</summary>
public sealed record AlignRow(int Upper, int Lower, int N, double MaxMm, double P95Mm, string Result, string Reason, string Source);

public static class InstallRules
{
    // §3.2 설계 하중 행 — KDS 21 50 00:2024 §1.6 [확정]
    public const double ConcreteKnM3 = 24.0;          // 콘크리트 단위중량 (철근 포함)
    public const double FormKnM2 = 0.4;               // 거푸집 하중 최소
    public const double MinVerticalKnM2 = 5.0;        // 고정 + 작업 하중 최소

    // §3.2 1본당 하중 행 — KDS 21 50 00 표 2.6-1 압축성능 40 kN ÷ 표 1.8-1 안전율 3 (파이프서포트) [확정].
    // 제품 성적서 값이 더 작으면 그 값 (KCS 14 20 12 1.6.3(9)) [입력]
    public const double PipeAllowableKn = 40.0 / 3;

    // §3.2 설치 검측 행 [확정]
    public const int BraceHMm = 3500;              // 안전보건규칙 제332조의2: 높이 3.5 m 초과 → 수평연결재
    public const int BraceIntervalMm = 2000;       // 〃 2 m 이내마다 2방향
    public const int SystemHMm = 4000;             // 고시 2025-78 제9조: 높이 4 m 초과 → 시스템 동바리
    public const int SystemTMm = 1000;             // 〃 타설 두께 1 m 초과 → 시스템 동바리
    public const int PeHMm = 5000;                 // 건설기술진흥법 시행령 제101조의2: 높이 5 m 이상 → 기술사 구조계산
    public const int MaxSplices = 1;               // 제332조의2 "3개 이상 이어 사용 금지" → 이음 ≤ 1 (정의서 표: 이음 수 매개변수 ≤ 1)

    /// <summary>KDS 21 50 00 §1.6 작업하중: 타설 높이 &lt; 0.5 m 2.5 / 0.5–1.0 m 3.5 / ≥ 1.0 m 5.0 kN/m².</summary>
    public static double LiveLoad(double pourHeightM) => pourHeightM < 0.5 ? 2.5 : pourHeightM < 1.0 ? 3.5 : 5.0;

    /// <summary>설계 연직하중 kN/m² = max(D + L, 5.0). D = 24·t + 거푸집, 타설 높이 기본값 = 슬래브 두께.</summary>
    public static double DesignLoad(double tSlabMm, double form = FormKnM2, double? pourHeightM = null)
    {
        double t = tSlabMm / 1000;
        return Math.Max(ConcreteKnM3 * t + form + LiveLoad(pourHeightM ?? t), MinVerticalKnM2);
    }

    /// <summary>1본당 하중 kN = 지지면적 × w.</summary>
    public static double PostLoad(double spacingXM, double spacingYM, double wKnM2) => spacingXM * spacingYM * wKnM2;

    /// <summary>동바리 1본(또는 같은 높이 묶음) 설치 검사 → [{item, result, reason, source}].
    /// result: OK / NG (모델에 기록된 값이 규칙을 어김 → 불합격) / 필요 (규칙이 요구하는 것이 모델에 기록돼 있지 않음 → 확인 필요).
    /// horizBrace: true 설치 기록 / false "없음"으로 기록 / null 기록 없음. 모델 매개변수 SHR_Height·SHR_Splices·SHR_HorizBrace·SHR_Type.
    /// splices·loadKn 도 null = 기록 없음 → "필요" (감사 A-14: 기록이 없는데 합격으로 보이던 것).</summary>
    public static List<InstallCheck> CheckShore(double heightMm, int? splices = null, bool? horizBrace = null, string shoreType = "파이프서포트",
        double pourThicknessMm = 0.0, double? loadKn = null, double allowableKn = PipeAllowableKn)
    {
        var outp = new List<InstallCheck>();
        void Add(string item, bool ok, string bad, string reason, string source) => outp.Add(new(item, ok ? "OK" : bad, reason, source));

        Add("이음", splices is not null && splices <= MaxSplices, splices is null ? "필요" : "NG",
            splices is null ? "이음 수 기록 없음, 현장 확인"
            : $"이음 {splices}개" + (splices == 1 ? " (원칙 금지 — 불가피하면 볼트 4개 이상, 체크리스트 확인)" : ""),
            "안전보건규칙 제332조의2, 고시 2025-78");
        if (heightMm > BraceHMm)
        {
            int n = (int)Math.Ceiling(heightMm / BraceIntervalMm) - 1;     // 간격이 2 m 이내가 되는 최소 단수 [가정: 바닥·윗면 끝에서 셈]
            Add("수평연결재", horizBrace == true, horizBrace is null ? "필요" : "NG",
                $"높이 {Py.F(heightMm, 0)} mm > {BraceHMm} mm → 2방향 {BraceIntervalMm} mm 이내마다 (최소 {n}단)"
                + (horizBrace switch { true => "", false => ", 수평연결재 없음으로 기록됨", null => " — 수평연결재 설치 기록 없음, 현장 확인" }),
                "안전보건규칙 제332조의2");
        }
        if (heightMm > SystemHMm || pourThicknessMm > SystemTMm)
            Add("동바리 형식", shoreType == "시스템", "NG",
                heightMm > SystemHMm ? $"높이 {Py.F(heightMm, 0)} mm > {SystemHMm} mm"
                : $"타설 두께 {Py.F(pourThicknessMm, 0)} mm > {SystemTMm} mm", "고시 2025-78 제9조 (시스템 동바리 사용)");
        if (heightMm >= PeHMm)
            Add("구조계산", false, "필요", $"높이 {Py.F(heightMm, 0)} mm ≥ {PeHMm} mm → 기술사 구조계산서 필요",
                "건설기술진흥법 시행령 제101조의2");
        if (loadKn is not { } load)
            Add("1본 하중", false, "필요", "1본 하중 산정 없음 (슬래브 두께·간격 기록 필요)", "KDS 21 50 00 표 2.6-1 ÷ 표 1.8-1");
        else
            Add("1본 하중", load <= allowableKn, "NG", $"{Py.F(load, 1)} kN {(load <= allowableKn ? "≤" : ">")} " +
                $"허용 {Py.F(allowableKn, 1)} kN", "KDS 21 50 00 표 2.6-1 ÷ 표 1.8-1");
        return outp;
    }

    public const string AlignSource = "해체 가이드라인 4.3 (각 층 같은 위치 설치)";
    /// <summary>install_inspection.csv 수동 항목 코드 — 연직도는 모델로 못 잰다 (install_rules.py PLUMB_CODES)</summary>
    public static readonly string[] PlumbCodes = { "SHR-V", "연직도" };

    /// <summary>install_rules.py align_check: 상하 정렬 (총괄 P2) — 층마다 동바리(점, mm)에서 바로 아래층 동바리 중 가장 가까운 것까지 수평 거리,
    /// 층 쌍마다 최대·95% 값 (가까운 순위). 바로 아래층 = 동바리가 있는 층을 정렬한 앞 층.
    /// tolMm [입력] 이 있을 때만 OK/NG, 없으면 '필요'(편차만 표시) — 허용값은 지어내지 않는다.
    /// shortcut: 전수 비교 O(n·m) — 층당 수천 본이면 격자로 나눌 것</summary>
    public static List<AlignRow> AlignCheck(IReadOnlyDictionary<int, List<(double X, double Y)>> pointsByFloor, double? tolMm = null)
    {
        var fl = pointsByFloor.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).Order().ToList();
        var outp = new List<AlignRow>();
        for (int i = 1; i < fl.Count; i++)
        {
            int lo = fl[i - 1], up = fl[i];
            var below = pointsByFloor[lo];
            var d = pointsByFloor[up].Select(p => below.Min(b => double.Hypot(p.X - b.X, p.Y - b.Y))).Order().ToList();
            double mx = d[^1], p95 = d[Math.Max(0, (int)Math.Ceiling(0.95 * d.Count) - 1)];
            string head = $"{up}층 ↔ {lo}층: 바로 아래 동바리까지 수평 거리 최대 {Py.F(mx, 0)} mm · 95% {Py.F(p95, 0)} mm ({d.Count}본)";
            string res, reason;
            if (tolMm is not { } tol) (res, reason) = ("필요", head + " — 편차만 표시, 허용값 입력 필요");
            else
            {
                res = mx <= tol ? "OK" : "NG";
                reason = head + $" {(res == "OK" ? "≤" : ">")} 허용 {Py.G(tol)} mm [입력]";
            }
            outp.Add(new AlignRow(up, lo, d.Count, Py.Round(mx, 1), Py.Round(p95, 1), res, reason, AlignSource));
        }
        return outp;
    }

    /// <summary>install_rules.py plumb_check: 연직도 — install_inspection.csv 수동 항목(SHR-V 또는 연직도)의 그 구역 마지막 날짜 행 (같은 날이면 뒤 행).
    /// OK/NG 그대로, 빈칸·기록 없음 → '필요'</summary>
    public static InstallCheck PlumbCheck(IEnumerable<IReadOnlyDictionary<string, string?>> inspectionRows, string zoneId)
    {
        IReadOnlyDictionary<string, string?>? last = null;
        foreach (var r in inspectionRows)
            if (r.GetValueOrDefault("zone_id") == zoneId && PlumbCodes.Contains((r.GetValueOrDefault("item_code") ?? "").Trim())
                && (last is null || string.CompareOrdinal(r.GetValueOrDefault("date") ?? "", last.GetValueOrDefault("date") ?? "") >= 0))
                last = r;
        string res = (last?.GetValueOrDefault("result") ?? "").Trim().ToUpperInvariant();
        if (last is null || res is not ("OK" or "NG"))
            return new("연직도", "필요", "연직도 기록 없음 — 현장 확인 (모델로 못 잼, install_inspection.csv SHR-V)", "설치 검측 수동 항목");
        return new("연직도", res, $"연직도 {res} ({last.GetValueOrDefault("date") ?? ""} {(last.GetValueOrDefault("inspector") ?? "").Trim()})".TrimEnd(), "설치 검측 수동 항목");
    }

    /// <summary>install_rules.py DOCS_SOURCE — 설치 서류 항목 출처 태그 (F4)</summary>
    public static readonly Dictionary<string, string> DocsSource = new()
    {
        ["조립도"] = "안전보건규칙 제331조 (조립도) [미확인 — FPR S19 2차 인용, 조문 미대조]",
        ["구조검토서"] = "주택건설공사 감리업무 세부기준 제15조 · 건진법 시행령 제101조의2 [확정, FPR S17·S18]",
        ["관계전문가 확인"] = "건진법 시행령 제101조의2 (높이 5 m 이상 관계전문가 확인) [확정, FPR S17 · N12]",
        ["지지 방식"] = "기록 항목 — 지지 방식 임의 변경(화정 S01) 추적용 [입력]",
    };

    /// <summary>install_rules.py install_docs_check: 설치 단계 서류 속성 (direction_review 13) → CheckShore 와 같은 항목 목록.
    /// row 열: assembly_dwg_no · struct_review_no · expert_confirm(예/아니오/빈칸) · support_method (· height_mm). 빈칸 → '필요'.
    /// 관계전문가 확인은 높이 ≥ PeHMm 일 때만, '예' 가 아니면 '필요' (높이 기록 없으면 '필요')</summary>
    public static List<InstallCheck> InstallDocsCheck(IReadOnlyDictionary<string, string?> row, double? heightMm = null)
    {
        string G(string k) => (row.GetValueOrDefault(k) ?? "").Trim();
        var outp = new List<InstallCheck>();
        void Add(string item, bool v, string ok, string bad) => outp.Add(new(item, v ? "OK" : "필요", v ? ok : bad, DocsSource[item]));
        Add("조립도", G("assembly_dwg_no").Length > 0, $"조립도 {G("assembly_dwg_no")}", "조립도 번호 기록 없음");
        Add("구조검토서", G("struct_review_no").Length > 0, $"구조검토서 {G("struct_review_no")}", "구조검토서 번호 기록 없음");
        double? h = heightMm ?? (G("height_mm").Length > 0 ? Py.Float(G("height_mm")) : null);
        if (h is null || h >= PeHMm)
            Add("관계전문가 확인", G("expert_confirm") == "예", "관계전문가 확인 예",
                (h is { } hv ? $"높이 {Py.F(hv, 0)} mm ≥ {PeHMm} mm" : $"높이 기록 없음 ({PeHMm} mm 이상이면 대상)")
                + $" — 관계전문가 확인 {(G("expert_confirm").Length > 0 ? G("expert_confirm") : "기록 없음")}");
        Add("지지 방식", G("support_method").Length > 0, $"지지 방식 {G("support_method")} (기록)", "지지 방식 기록 없음");
        return outp;
    }

    /// <summary>OK 가 아닌 항목 이름 목록.</summary>
    public static List<string> Flags(IEnumerable<InstallCheck> results) => results.Where(r => r.Result != "OK").Select(r => r.Item).ToList();
}

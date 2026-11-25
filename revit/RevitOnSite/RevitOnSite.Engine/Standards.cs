// 적용 기준 한 곳 (34차 사용자 요청 B: "출처는 매번 표시하지 말고 프로젝트 개요에서 어느 표준값·각 값·단위를 한 번 명시").
// [프로젝트 설정] › 적용 기준 접는 칸과 도움말 표가 이것을 보여 주고, 판정 근거 창 바닥글이 이곳을 가리킨다. 값은 엔진 상수에서 읽음 — 새 수치 없음.
// 출처 표시 = 정의서 §3.2 ([확정] 원문 확인 / [가정] 팀 결정). 서류(검측요청서·검토서)의 근거 줄은 서류라 그대로 둔다.
namespace RevitOnSite.Engine;

public static class Standards
{
    public sealed record Item(string What, string Value, string Source, string Tag);

    /// <summary>적용 기준 이름 (판정·서류가 쓰는 것만)</summary>
    public static readonly string[] Names =
    {
        "KCS 14 20 12:2024 거푸집 및 동바리", "거푸집 및 동바리 해체 가이드라인 (국토교통부, 2023.1)", "KDS 21 50 00:2024", "고시 제2024-879호",
    };

    /// <summary>항목 · 값·단위 · 출처 · 표시 — fck = 프로젝트 설계기준강도 (PRJ_DefaultFck)</summary>
    public static List<Item> Rows(double fck)
    {
        var t = ShoringEngine.Thresholds(fck);
        string G(double v) => Py.G(v);
        return new()
        {
            new("다층 동바리 해체 (구역 강도)", $"공시체·센서 ≥ fck = {G(t["다층 동바리 해체"])} MPa", "KCS 14 20 12:2024 표 3.3-1 · 해체 가이드라인 표 2.1.1", "[확정]"),
            new("존치 층수", $"타설층 포함 {ShoringEngine.MinLevels}개층 이상", "KCS 14 20 12:2024 3.3.2(2) · 해체 가이드라인 2.1.2(1)④", "[확정]"),
            new("지지 슬래브 (다음 타설을 받치는 층)", $"≥ {G(t["지지 슬래브"])} MPa", "해체 가이드라인 2.1.2(2)⑧", "[확정]"),
            new("하중비 (위층 타설 하중 ÷ 지지층 여유)", "≤ 1.00", "해체 가이드라인 2.1.2(2) 하중비교 예제식 (× √(f/fck) 감소는 보수 측 [가정])", "[확정]"),
            new("측면 거푸집 (벽·기둥)", $"≥ {G(t["측면"])} MPa", "KCS 14 20 12:2024 표 3.3-1", "[확정]"),
            new("판넬 조기 탈형 (필러 존치)", $"≥ {G(t["필러 동바리 조기 탈형"])} MPa · 10 ℃ 이상 {Staged.WarmDaysNeed}일 · 구조검토서", "해체 가이드라인 2.1.1(2)① · KCS 14 20 12:2024 3.3.1(5)", "[확정]"),
            new("강도 확인 (기준 근거)", $"현장양생 공시체 3개 평균 ({string.Join("·", ShoringEngine.FieldCurings)})", "KCS 14 20 12:2024 · 고시 제2024-879호", "[확정]"),
            new("센서 (위치 근거)", "구역(평면 + 3 m) 안 센서가 있을 때만 센서 구역 최소 추정 ≥ 해체 기준 · 3 m 안 센서가 없는 구역의 먼 센서·기상 예측 = 예상 (참고, 판정에 안 씀)",
                "사용자 결정 11-05 (판정은 실측 — 센서는 3 m 안일 때만)", "[가정]"),
            new("동바리 1본 허용 하중", $"≤ {Py.F(InstallRules.PipeAllowableKn, 1)} kN/본 (압축 성능 40 kN ÷ 안전율 3)", "KDS 21 50 00:2024 표 2.6-1 · 표 1.8-1", "[확정]"),
        };
    }
}

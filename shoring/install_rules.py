"""동바리 설치 검측 규칙 (타설 전) — 정의서 §3.2 '설계 하중'·'설치 검측' 표. 표준 라이브러리만 (C# 이식 기준본).

engine.py 에 없는 설치 단계 규칙만 여기 둔다 (해체·강도는 engine.py).
실행: python install_rules.py  (자체 검사)
"""
import math

# §3.2 설계 하중 행 — KDS 21 50 00:2024 §1.6 [확정]
CONCRETE_KN_M3 = 24.0          # 콘크리트 단위중량 (철근 포함)
FORM_KN_M2 = 0.4               # 거푸집 하중 최소
MIN_VERTICAL_KN_M2 = 5.0       # 고정 + 작업 하중 최소

# §3.2 1본당 하중 행 — KDS 21 50 00 표 2.6-1 압축성능 40 kN ÷ 표 1.8-1 안전율 3 (파이프서포트) [확정].
# 제품 성적서 값이 더 작으면 그 값 (KCS 14 20 12 1.6.3(9)) [입력]
PIPE_ALLOWABLE_KN = 40.0 / 3

# §3.2 설치 검측 행 [확정]
BRACE_H_MM = 3500              # 안전보건규칙 제332조의2: 높이 3.5 m 초과 → 수평연결재
BRACE_INTERVAL_MM = 2000       # 〃 2 m 이내마다 2방향
SYSTEM_H_MM = 4000             # 고시 2025-78 제9조: 높이 4 m 초과 → 시스템 동바리
SYSTEM_T_MM = 1000             # 〃 타설 두께 1 m 초과 → 시스템 동바리
PE_H_MM = 5000                 # 건설기술진흥법 시행령 제101조의2: 높이 5 m 이상 → 기술사 구조계산
MAX_SPLICES = 1                # 제332조의2 "3개 이상 이어 사용 금지" → 이음 ≤ 1 (정의서 표: 이음 수 매개변수 ≤ 1)


def live_load(pour_height_m):
    """KDS 21 50 00 §1.6 작업하중: 타설 높이 < 0.5 m 2.5 / 0.5–1.0 m 3.5 / ≥ 1.0 m 5.0 kN/m²."""
    return 2.5 if pour_height_m < 0.5 else 3.5 if pour_height_m < 1.0 else 5.0


def design_load(t_slab_mm, form=FORM_KN_M2, pour_height_m=None):
    """설계 연직하중 kN/m² = max(D + L, 5.0). D = 24·t + 거푸집, 타설 높이 기본값 = 슬래브 두께."""
    t = t_slab_mm / 1000
    return max(CONCRETE_KN_M3 * t + form + live_load(t if pour_height_m is None else pour_height_m), MIN_VERTICAL_KN_M2)


def post_load(spacing_x_m, spacing_y_m, w_kn_m2):
    """1본당 하중 kN = 지지면적 × w."""
    return spacing_x_m * spacing_y_m * w_kn_m2


def check_shore(height_mm, splices=None, horiz_brace=None, shore_type="파이프서포트", pour_thickness_mm=0.0,
                load_kn=None, allowable_kn=PIPE_ALLOWABLE_KN):
    """동바리 1본(또는 같은 높이 묶음) 설치 검사 → [{item, result, reason, source}].
    result: OK / NG (모델에 기록된 값이 규칙을 어김 → 불합격) / 필요 (규칙이 요구하는 것이 모델에 기록돼 있지 않음 → 확인 필요).
    horiz_brace: True 설치 기록 / False "없음"으로 기록 / None 기록 없음. 모델 매개변수 SHR_Height·SHR_Splices·SHR_HorizBrace·SHR_Type.
    splices·load_kn 도 None = 기록 없음 → "필요" (감사 A-14: 기록이 없는데 합격으로 보이던 것)."""
    out = []
    horiz_brace = None if horiz_brace is None else bool(horiz_brace)   # Revit 예/아니오 AsInteger() 1/0 도 받음

    def add(item, ok, bad, reason, source):
        out.append({"item": item, "result": "OK" if ok else bad, "reason": reason, "source": source})

    add("이음", splices is not None and splices <= MAX_SPLICES, "필요" if splices is None else "NG",
        "이음 수 기록 없음, 현장 확인" if splices is None else
        f"이음 {splices}개" + (" (원칙 금지 — 불가피하면 볼트 4개 이상, 체크리스트 확인)" if splices == 1 else ""),
        "안전보건규칙 제332조의2, 고시 2025-78")
    if height_mm > BRACE_H_MM:
        n = math.ceil(height_mm / BRACE_INTERVAL_MM) - 1     # 간격이 2 m 이내가 되는 최소 단수 [가정: 바닥·윗면 끝에서 셈]
        add("수평연결재", horiz_brace is True, "필요" if horiz_brace is None else "NG",
            f"높이 {height_mm:.0f} mm > {BRACE_H_MM} mm → 2방향 {BRACE_INTERVAL_MM} mm 이내마다 (최소 {n}단)"
            + {True: "", False: ", 수평연결재 없음으로 기록됨",
               None: " — 수평연결재 설치 기록 없음, 현장 확인"}[horiz_brace], "안전보건규칙 제332조의2")
    if height_mm > SYSTEM_H_MM or pour_thickness_mm > SYSTEM_T_MM:
        add("동바리 형식", shore_type == "시스템", "NG",
            f"높이 {height_mm:.0f} mm > {SYSTEM_H_MM} mm" if height_mm > SYSTEM_H_MM
            else f"타설 두께 {pour_thickness_mm:.0f} mm > {SYSTEM_T_MM} mm", "고시 2025-78 제9조 (시스템 동바리 사용)")
    if height_mm >= PE_H_MM:
        add("구조계산", False, "필요", f"높이 {height_mm:.0f} mm ≥ {PE_H_MM} mm → 기술사 구조계산서 필요",
            "건설기술진흥법 시행령 제101조의2")
    if load_kn is None:
        add("1본 하중", False, "필요", "1본 하중 산정 없음 (슬래브 두께·간격 기록 필요)", "KDS 21 50 00 표 2.6-1 ÷ 표 1.8-1")
    else:
        add("1본 하중", load_kn <= allowable_kn, "NG", f"{load_kn:.1f} kN {'≤' if load_kn <= allowable_kn else '>'} "
            f"허용 {allowable_kn:.1f} kN", "KDS 21 50 00 표 2.6-1 ÷ 표 1.8-1")
    return out


ALIGN_SOURCE = "해체 가이드라인 4.3 (각 층 같은 위치 설치)"
PLUMB_CODES = ("SHR-V", "연직도")   # install_inspection.csv 수동 항목 — 연직도는 모델로 못 잰다


def align_check(points_by_floor, tol_mm=None):
    """상하 정렬 (총괄 P2) — 층마다 동바리(점, mm)에서 바로 아래층 동바리 중 가장 가까운 것까지 수평 거리, 층 쌍마다 최대·95% 값.
    points_by_floor {층: [(x_mm, y_mm)]}, 바로 아래층 = 동바리가 있는 층을 정렬한 앞 층. 95% = 가까운 순위(nearest-rank).
    tol_mm [입력] 이 있을 때만 OK/NG, 없으면 '필요'(편차만 표시) — 허용값은 지어내지 않는다 (규정 수치 없음).
    shortcut: 전수 비교 O(n·m) — 층당 수천 본이면 격자로 나눌 것."""
    fl = sorted(f for f, p in points_by_floor.items() if p)
    out = []
    for lo, up in zip(fl, fl[1:]):
        below = points_by_floor[lo]
        d = sorted(min(math.hypot(x - a, y - b) for a, b in below) for x, y in points_by_floor[up])
        mx, p95 = d[-1], d[max(0, math.ceil(0.95 * len(d)) - 1)]
        head = f"{up}층 ↔ {lo}층: 바로 아래 동바리까지 수평 거리 최대 {mx:.0f} mm · 95% {p95:.0f} mm ({len(d)}본)"
        if tol_mm is None:
            res, reason = "필요", head + " — 편차만 표시, 허용값 입력 필요"
        else:
            res = "OK" if mx <= tol_mm else "NG"
            reason = head + f" {'≤' if res == 'OK' else '>'} 허용 {tol_mm:g} mm [입력]"
        out.append({"item": "상하 정렬", "upper": up, "lower": lo, "n": len(d), "max_mm": round(mx, 1), "p95_mm": round(p95, 1),
                    "result": res, "reason": reason, "source": ALIGN_SOURCE})
    return out


def plumb_check(inspection_rows, zone_id):
    """연직도 — install_inspection.csv 수동 항목(item_code SHR-V 또는 연직도)의 그 구역 마지막 날짜 행 (같은 날이면 뒤 행).
    OK/NG 그대로, 빈칸·기록 없음 → '필요'. check_shore 와 같은 형식 한 줄."""
    last = None
    for r in inspection_rows:
        if r.get("zone_id") == zone_id and (r.get("item_code") or "").strip() in PLUMB_CODES and (last is None or r.get("date", "") >= last.get("date", "")):
            last = r
    res = (last.get("result") or "").strip().upper() if last else ""
    if res not in ("OK", "NG"):
        return {"item": "연직도", "result": "필요", "reason": "연직도 기록 없음 — 현장 확인 (모델로 못 잼, install_inspection.csv SHR-V)",
                "source": "설치 검측 수동 항목"}
    return {"item": "연직도", "result": res, "reason": f"연직도 {res} ({last.get('date', '')} {(last.get('inspector') or '').strip()})".rstrip(),
            "source": "설치 검측 수동 항목"}


DOCS_SOURCE = {"조립도": "안전보건규칙 제331조 (조립도) [미확인 — FPR S19 2차 인용, 조문 미대조]",
               "구조검토서": "주택건설공사 감리업무 세부기준 제15조 · 건진법 시행령 제101조의2 [확정, FPR S17·S18]",
               "관계전문가 확인": "건진법 시행령 제101조의2 (높이 5 m 이상 관계전문가 확인) [확정, FPR S17 · N12]",
               "지지 방식": "기록 항목 — 지지 방식 임의 변경(화정 S01) 추적용 [입력]"}


def install_docs_check(row, height_mm=None):
    """설치 단계 서류 속성 (direction_review 13) → check_shore 와 같은 [{item, result, reason, source}].
    row 열: assembly_dwg_no · struct_review_no · expert_confirm(예/아니오/빈칸) · support_method (· height_mm).
    빈칸 → '필요'. 관계전문가 확인은 높이 ≥ PE_H_MM 일 때만, '예' 가 아니면 '필요' (높이 기록 없으면 '필요')."""
    g = lambda k: str(row.get(k) or "").strip()
    out = []

    def add(item, v, reason_ok, reason_bad):
        out.append({"item": item, "result": "OK" if v else "필요", "reason": reason_ok if v else reason_bad, "source": DOCS_SOURCE[item]})

    add("조립도", g("assembly_dwg_no"), f"조립도 {g('assembly_dwg_no')}", "조립도 번호 기록 없음")
    add("구조검토서", g("struct_review_no"), f"구조검토서 {g('struct_review_no')}", "구조검토서 번호 기록 없음")
    h = height_mm if height_mm is not None else (float(g("height_mm")) if g("height_mm") else None)
    if h is None or h >= PE_H_MM:
        add("관계전문가 확인", g("expert_confirm") == "예", "관계전문가 확인 예",
            (f"높이 {h:.0f} mm ≥ {PE_H_MM} mm" if h is not None else f"높이 기록 없음 ({PE_H_MM} mm 이상이면 대상)")
            + f" — 관계전문가 확인 {g('expert_confirm') or '기록 없음'}")
    add("지지 방식", g("support_method"), f"지지 방식 {g('support_method')} (기록)", "지지 방식 기록 없음")
    return out


def flags(results):
    """OK 가 아닌 항목 이름 목록."""
    return [r["item"] for r in results if r["result"] != "OK"]


if __name__ == "__main__":
    w = design_load(210)
    assert abs(w - 7.94) < 1e-9                                         # 정의서 §3.2 산정 (210 mm)
    assert abs(post_load(1.2, 1.2, w) - 11.4336) < 1e-9 and post_load(1.2, 1.2, w) <= PIPE_ALLOWABLE_KN
    assert abs(post_load(1.5, 1.5, w) - 17.865) < 1e-9 and post_load(1.5, 1.5, w) > PIPE_ALLOWABLE_KN   # §4.1 17.9 kN
    assert design_load(50) == MIN_VERTICAL_KN_M2                        # 얇아도 5.0 최소
    assert flags(check_shore(2790)) == ["이음", "1본 하중"]               # 기록 없음 → 확인 필요 (A-14)
    assert {r["result"] for r in check_shore(2790) if r["item"] in ("이음", "1본 하중")} == {"필요"}
    ok = dict(splices=0, load_kn=11.4)                                  # 기록 있음 (이음 0, 1.2 m 간격 하중)
    assert flags(check_shore(2790, **ok)) == []                         # 데모 모델 층고 3.0 m
    assert flags(check_shore(3950, **ok)) == ["수평연결재"]
    res = lambda rs, item: next(r["result"] for r in rs if r["item"] == item)
    assert res(check_shore(3950, **ok), "수평연결재") == "필요"                 # 기록 없음 → 확인 필요 (불합격 아님)
    assert res(check_shore(3950, **ok, horiz_brace=False), "수평연결재") == "NG"  # "없음" 기록 → 불합격
    assert flags(check_shore(3950, **ok, horiz_brace=True)) == [] and flags(check_shore(3950, **ok, horiz_brace=1)) == []
    assert res(check_shore(3950, **ok, horiz_brace=0), "수평연결재") == "NG"
    assert flags(check_shore(4650, **ok)) == ["수평연결재", "동바리 형식"]
    assert res(check_shore(4650, **ok), "동바리 형식") == "NG"                  # 4 m 초과 파이프 서포트 → 불합격
    assert flags(check_shore(5900, **ok, horiz_brace=True, shore_type="시스템")) == ["구조계산"]
    assert flags(check_shore(2790, splices=2, load_kn=11.4)) == ["이음"] and flags(check_shore(2790, splices=1, load_kn=11.4)) == []
    assert flags(check_shore(2790, **ok, pour_thickness_mm=1200)) == ["동바리 형식"]
    # 상하 정렬: 3×3 격자 1.2 m, 1·2층 같은 위치(0 mm), 3층만 x 로 300 mm 밀림 / 허용값 없으면 '필요'(편차만), 있으면 OK·NG
    grid = [(1200.0 * i, 1200.0 * j) for i in range(3) for j in range(3)]
    al = align_check({1: grid, 2: grid, 3: [(x + 300, y) for x, y in grid]})
    assert [(a["upper"], a["lower"], a["max_mm"], a["p95_mm"], a["result"]) for a in al] == [(2, 1, 0.0, 0.0, "필요"), (3, 2, 300.0, 300.0, "필요")], al
    assert al[1]["reason"] == "3층 ↔ 2층: 바로 아래 동바리까지 수평 거리 최대 300 mm · 95% 300 mm (9본) — 편차만 표시, 허용값 입력 필요", al[1]
    assert [a["result"] for a in align_check({1: grid, 2: grid, 3: [(x + 300, y) for x, y in grid]}, tol_mm=50)] == ["OK", "NG"]
    assert align_check({1: grid, 2: []}) == [] and align_check({5: grid}) == []
    # 연직도: 수동 항목 — 기록 없음·빈칸 → 필요, 마지막 행 OK/NG
    ins = [{"zone_id": "9F-A1", "item_code": "SHR-V", "result": "NG", "inspector": "박안전", "date": "2026-11-23"},
           {"zone_id": "9F-A1", "item_code": "SHR-V", "result": "ok", "inspector": "박안전", "date": "2026-11-24"},
           {"zone_id": "9F-B1", "item_code": "연직도", "result": "", "inspector": "박안전", "date": "2026-11-24"}]
    assert plumb_check(ins, "9F-A1") == {"item": "연직도", "result": "OK", "reason": "연직도 OK (2026-11-24 박안전)", "source": "설치 검측 수동 항목"}
    assert plumb_check(ins, "9F-B1")["result"] == "필요" and plumb_check(ins, "9F-C1")["result"] == "필요" and plumb_check(ins[:1], "9F-A1")["result"] == "NG"
    # 설치 서류 속성: 빈칸 → 필요, 관계전문가는 5 m 이상만
    full = {"assembly_dwg_no": "SD-101", "struct_review_no": "SR-07", "expert_confirm": "", "support_method": "파이프서포트"}
    assert flags(install_docs_check(full, 2790)) == [] and [r["item"] for r in install_docs_check(full, 2790)] == ["조립도", "구조검토서", "지지 방식"]
    assert flags(install_docs_check({}, 2790)) == ["조립도", "구조검토서", "지지 방식"]
    assert {r["result"] for r in install_docs_check({}, 2790)} == {"필요"}
    assert flags(install_docs_check(full, 5000)) == ["관계전문가 확인"] and flags(install_docs_check({**full, "expert_confirm": "아니오"}, 5900)) == ["관계전문가 확인"]
    assert flags(install_docs_check({**full, "expert_confirm": "예"}, 5900)) == []
    assert flags(install_docs_check({**full, "height_mm": "5200"})) == ["관계전문가 확인"] and flags(install_docs_check(full)) == ["관계전문가 확인"]
    assert next(r for r in install_docs_check(full, 5900) if r["item"] == "관계전문가 확인")["reason"] == "높이 5900 mm ≥ 5000 mm — 관계전문가 확인 기록 없음"
    print("install_rules self-check OK")

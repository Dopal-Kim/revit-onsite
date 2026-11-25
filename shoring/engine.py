"""동바리 타설·해체 검측 계산 엔진 — 표준 라이브러리만 사용 (Dynamo Python 노드에 그대로 붙여 쓸 수 있게).

1) 강도 예측 : KDS 14 20 10  f_cm(t) = β(t)·(fck+Δf), β = exp{s[1 − (28/t)^0.5]},
              t = 온도 보정 재령 Σ Δt·exp[Ea/R·(1/293 − 1/(273+T))] (fib MC2010)
2) 보정      : 현장양생 공시체 실측으로 s 역산
3) 해체 기준 : 「거푸집 및 동바리 해체 가이드라인」(국토부·건설연 2023)
4) 하중비교  : 같은 가이드라인 2.1.2(2) 예제식, 지지 슬래브 여유 × √(f/fck) (ACI SP-4 C법)
5) 구역 매핑 : 센서 좌표의 등가재령을 IDW(p=2, Utepov 외 2019)로 보간 → 구역 최소 강도로 판정
실행: python engine.py  (자체 검사)
"""
import math
from datetime import date, timedelta

R_GAS = 8.314
# 배합: (s, Ea kJ/mol). s: KDS 1종 0.35·3종 0.25, 3성분 0.54는 Jeong 외 2024 3일 강도비 0.33에 맞춤,
# 슬래그 미분말 혼합은 보간 (shortcut: 현장 1–3일 공시체로 calibrate_s 해서 교체). Ea: Vollpracht 외 2018
MIXES = {
    "조강": (0.25, 33.3),
    "보통": (0.35, 33.3),
    "슬래그 미분말": (0.45, 38.0),
    "슬래그·FA 3성분": (0.54, 41.6),
}
SEOUL_T = [-1.9, 0.7, 6.1, 12.6, 18.2, 22.7, 25.3, 26.1, 21.6, 15.0, 7.5, 0.2]   # 서울 월평균 °C (평년값 근사)


def air_temp(d):
    """기상 자료가 없을 때 쓰는 평년 기온 (월 중순 값 선형 보간). shortcut: 기상청 ASOS 일평균으로 교체."""
    m = d.month - 1 + (d.day - 15) / 30
    i = math.floor(m)
    return SEOUL_T[i % 12] + (m - i) * (SEOUL_T[(i + 1) % 12] - SEOUL_T[i % 12])


def delta_f(fck):
    """KDS 14 20 10: fck ≤ 40 → 4, ≥ 60 → 6, 사이 보간."""
    return 4.0 if fck <= 40 else 6.0 if fck >= 60 else 4.0 + (fck - 40) / 10


def age_factor(T, ea):
    """1 시간(또는 1 일) 동안 온도 T 의 20 °C 환산 계수."""
    return math.exp(ea * 1000 / R_GAS * (1 / 293 - 1 / (273 + T)))


def equivalent_age(temps, ea, step_days=1.0):
    """온도 기록(같은 간격) → 20 °C 등가재령(일)."""
    return sum(age_factor(T, ea) for T in temps) * step_days


def strength_at(te, fck, s):
    return (fck + delta_f(fck)) * math.exp(s * (1 - math.sqrt(28 / max(te, 0.05))))


def predict(mix, cast, age_days, fck, cure_min=5.0, temps=None, s=None):
    """타설일 cast부터 age_days 일 강도 MPa. temps 가 없으면 평년 기온, 양생 온도 = max(기온, cure_min).
    s = 현장 보정 곡선 값 (evaluate.calibration, G2 — 예측일에만), 없으면 배합 기본값."""
    s0, ea = MIXES[mix]
    s = s0 if s is None else s
    days = int(age_days)
    tl = temps[:days] if temps else [air_temp(cast + timedelta(i)) for i in range(days)]
    return strength_at(equivalent_age([max(T, cure_min) for T in tl], ea), fck, s)


def calibrate_s(f_measured, te, fck):
    """공시체 실측 강도와 그때 등가재령으로 s 역산. 0 < te < 28 일 때만 의미 있음 — 아니면 None (감사 A-28: 28 에서 0 나눔, 넘으면 부호 뒤집힘)."""
    if not 0 < te < 28:
        return None
    return math.log(f_measured / (fck + delta_f(fck))) / (1 - math.sqrt(28 / te))


def thresholds(fck):
    """가이드라인 표 2.1.1·2.1.2(2)⑧ 해체 기준 MPa."""
    # KCS 14 20 12:2024 표 3.3-1 (측면은 내구성 중요 시 10), 가이드라인 2.1.2(2)⑧ [확정]
    return {"측면": 5.0, "슬래브 밑면(단층)": max(2 / 3 * fck, 14.0), "다층 동바리 해체": fck,
            "필러 동바리 조기 탈형": 14.0, "지지 슬래브": 14.0}


MIN_LEVELS = 3   # 가이드라인 2.1.2(1)④: 타설층 포함 3개층 이상 존치(또는 재받침)
FIELD_CURINGS = ["현장봉함", "현장수중", "현장기건"]   # 해체 판단에 쓰는 현장양생 공시체 (표준양생은 기준 근거 아님 — 검단 2023)


SHORE_SELF = 0.5   # kN/m² 지지층 1개층분 동바리 자중 (가이드라인 예제 ALU 서포트 가정치, regulation_check.md)


def pour_load(t_slab=0.21, form=0.4, extra=None, live=2.5, n_support=MIN_LEVELS):
    """가이드라인 예제 1.2(5.44 + 0.5·n) + 1.6·2.5 의 일반형 kN/m². extra 를 안 주면 동바리 자중 0.5 × 지지 층수 n
    (예제 n = 3 → 1.5)."""
    extra = SHORE_SELF * n_support if extra is None else extra
    return 1.2 * (24 * t_slab + form + extra) + 1.6 * live


def slab_spare(sdl=1.64, ll=2.0):
    """지지 슬래브 1개층 여유 내력 kN/m² (아직 안 올라간 마감 하중 + 설계 활하중)."""
    return 1.2 * sdl + 1.6 * ll


def load_check(support_strengths, fck, sdl=1.64, ll=2.0, **kw):
    """support_strengths: 동바리가 딛고 있는 아래 슬래브들의 현재 강도. None = 기초가 받침.
    반환 (하중/여유 비, 최소 지지 강도). × √(f/fck) 감소는 가이드라인 예제에 없는 보수 측 추가 [가정]."""
    if any(f is None for f in support_strengths):
        return 0.0, None
    cap = sum(slab_spare(sdl, ll) * math.sqrt(min(f / fck, 1.0)) for f in support_strengths)   # sdl·ll = 구조계산서 값 (11-11 G1, 기본 = 가이드라인 예제)
    kw.setdefault("n_support", len(support_strengths))
    return pour_load(**kw) / cap, min(support_strengths)


def floor_plan(pours, fck, n_levels, cure_min=5.0, temps=None, cycle=None):
    """pours: [(층, 타설일, 배합)] 아래층부터. 층별 해체 판정 표.
    n_levels 개 층 존치 → 층 k 아래 동바리는 k+n 층 타설 직전에 해체.
    cycle: 골조 사이클(일) — None 이면 첫 두 타설 간격, 타설이 1건뿐이면 필요 존치 층수 None (감사 A-28).
    400일 안에 fck 에 못 닿으면 "fck 도달"·"필요 존치 층수" None, 판정 "위반"."""
    if cycle is None and len(pours) > 1:
        cycle = (pours[1][1] - pours[0][1]).days
    rows = []
    for k, (fl, d, mix) in enumerate(pours):
        need = next((a for a in range(1, 400) if predict(mix, d, a, fck, cure_min, temps) >= fck), None)
        rm = pours[k + n_levels][1] if k + n_levels < len(pours) else None
        f_rm = predict(mix, d, (rm - d).days, fck, cure_min, temps) if rm else None
        sup = [None if j < 0 else predict(pours[j][2], pours[j][1], (d - pours[j][1]).days, fck, cure_min, temps)
               for j in range(k - 1, k - 1 - n_levels, -1)]
        ratio, f_sup = load_check(sup, fck)
        rows.append({"층": fl, "타설": d, "fck 도달": d + timedelta(need) if need else None,
                     "필요 존치 층수": max(MIN_LEVELS, math.ceil(need / max(cycle, 1))) if need and cycle is not None else None,
                     "예정 해체": rm, "예정 해체 시 강도": f_rm, "지지 최소 강도": f_sup, "하중비": ratio,
                     "판정": "위반" if (n_levels < MIN_LEVELS or need is None or (f_rm is not None and f_rm < fck)
                                       or (f_sup is not None and f_sup < 14.0) or ratio > 1) else "적합"})
    return rows


def idw(points, values, x, y, p=2):
    """points [(x,y)], values [v] → (x,y) 보간값. 센서 위치에서는 그 값."""
    num = den = 0.0
    for (px, py), v in zip(points, values):
        d = math.hypot(x - px, y - py)
        if d < 1e-9:
            return v
        w = d ** -p
        num, den = num + w * v, den + w
    return num / den


def zone_min_from_values(points, values, rect, step=0.5, f=lambda v: v):
    """구역 rect (x0,y0,x1,y1) 의 step 격자 가운데점마다 IDW(values) → f → (최소, 위치).
    강도 센서는 values = MPa 그대로, 온도 센서는 values = 등가재령, f = 강도 곡선."""
    x0, y0, x1, y1 = rect
    best = None
    x = x0 + step / 2
    while x < x1:
        y = y0 + step / 2
        while y < y1:
            v = f(idw(points, values, x, y))
            if best is None or v < best[0]:
                best = (v, (round(x, 2), round(y, 2)))
            y += step
        x += step
    return best


def zone_min_strength(sensors, zones, fck, mix, step=0.5):
    """sensors: [(id, x, y, 등가재령)], zones: {이름: (x0,y0,x1,y1)} → {이름: (최소 강도, 위치)}."""
    s = MIXES[mix][0]
    pts, tes = [(a[1], a[2]) for a in sensors], [a[3] for a in sensors]
    return {name: zone_min_from_values(pts, tes, rect, step, lambda te: strength_at(te, fck, s)) for name, rect in zones.items()}


def zone_decision(f_zone_min, f_cylinder, threshold):
    """판정 우선순위: 공시체(기준 근거)와 센서 구역 최소(위치 근거) 둘 다 기준 이상일 때만 승인 요청.
    f_zone_min = None → 구역 3 m 안 센서가 없음: 위치 근거 없이 공시체만 (먼 센서·기상 예측 추정은 참고 — 정의서 §3.4-3, 34차)."""
    if f_cylinder is None:
        return "공시체 시험 필요"
    if f_cylinder >= threshold and (f_zone_min is None or f_zone_min >= threshold):
        return "해체 승인 요청 가능"
    return "존치 (공시체 미달)" if f_cylinder < threshold else "존치 (구역 최소 강도 미달)"


if __name__ == "__main__":
    fck = 24.0
    # 1) 20 °C 28일 = fck+4 (KDS 평균강도)
    assert abs(predict("보통", date(2026, 4, 1), 28, fck, cure_min=20, temps=[20.0] * 28) - 28.0) < 1e-6
    # 2) 가이드라인 예제: 12.33 ≤ 3개층 28일 여유 15.50
    assert abs(pour_load() - 12.33) < .01 and abs(3 * slab_spare() - 15.50) < .01
    # 3) s 역산 왕복
    te = equivalent_age([10.0] * 7, 33.3)
    assert abs(calibrate_s(strength_at(te, fck, 0.35), te, fck) - 0.35) < 1e-9
    # 4) IDW: 센서 위치에서 그 값
    assert idw([(0, 0), (10, 0)], [1.0, 5.0], 0, 0) == 1.0
    # 4-1) 강도 센서 IDW 구역 최소: 낮은 센서 쪽 첫 격자점, 값은 두 센서 사이
    v, xy = zone_min_from_values([(0, 0), (10, 0)], [20.0, 26.0], (0, 0, 10, 1))
    assert xy == (0.25, 0.25) and 20.0 < v < 26.0, (v, xy)
    # 5) 화정형: 동바리 1개층만 → 위반
    pours = [(f + 1, date(2026, 11, 1) + timedelta(7 * f), "보통") for f in range(6)]
    assert all(r["판정"] == "위반" for r in floor_plan(pours, fck, 1)[1:4])
    # 6) 경계 (감사 A-28): te ≥ 28 → None, 타설 1건 → 필요 존치 층수 None, 400일 안에 못 닿음 → None·위반
    assert calibrate_s(30, 28, fck) is None and calibrate_s(30, 40, fck) is None and calibrate_s(30, 0, fck) is None
    one = floor_plan(pours[:1], fck, 3)[0]
    assert one["필요 존치 층수"] is None and one["fck 도달"] is not None and floor_plan(pours[:1], fck, 3, cycle=7)[0]["필요 존치 층수"] == floor_plan(pours, fck, 3)[0]["필요 존치 층수"]
    cold = floor_plan(pours[:2], fck, 3, cure_min=-60, temps=[-60.0] * 400)[0]
    assert cold["fck 도달"] is None and cold["필요 존치 층수"] is None and cold["판정"] == "위반", cold
    print("self-check OK")
    for r in floor_plan(pours, fck, 3):
        print({k: (round(v, 2) if isinstance(v, float) else v) for k, v in r.items()})

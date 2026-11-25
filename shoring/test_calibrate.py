"""G2 현장 보정 곡선 시험 (예측 해체 가능일만 — 판정 게이트·결론·f_est_min 은 그대로). 실행: python test_calibrate.py
① 기본 꺼짐 = 예전 바이트 ② 자료 부족(현장양생 2건, 표준양생은 안 셈) → 기본 곡선 [가정] 태그, 예측일 그대로
③ 충분(3건) → s 중앙값, 예측일 앞당김, [보정] 태그, 다른 열 그대로 ④ te ≥ 28 시험 뺌 ⑤ s ≤ 0 뺌 ⑥ options.json 켬/깨짐
⑦ v6 층 높이 보정 (options.json height_correct, 기본 꺼짐): 켜면 기상 예측 구역에 태그·강도 같거나 낮음·예측일 같거나 늦음, 센서 구역 강도 그대로
⑧ 78차 최상층 기본값 (options.json top_floor — 애드인 [구역 나누기]가 씀): 정수면 top_floor 인자와 같은 결과, 없거나 참거짓이면 예전 그대로
   83차: top_floor_auto true(자동값 그대로)면 게이트 OK(최상부) 구역 사유 끝에 '최상층 자동 판단 [가정] — 확인 필요', 다른 열·다른 구역 그대로
"""
import json
import os
import shutil
import tempfile
from datetime import date, datetime

import engine as e
import evaluate as ev

SAMPLE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sample", "data")
AT = datetime(2026, 12, 31, 9)
SAME = ("predicted_due", "reason")


def cyl(d, rows):
    with open(os.path.join(d, "cylinders.csv"), "w", encoding="utf-8", newline="") as f:
        f.write("zone_id,test_date,curing,f1,f2,f3\n" + "".join(",".join(map(str, r)) + "\n" for r in rows))


def rest(rows):
    return [{k: v for k, v in r.items() if k not in SAME} for r in rows]


t = tempfile.mkdtemp()
try:
    d = os.path.join(t, "data")
    shutil.copytree(SAMPLE, d)
    assert ev.evaluate(d, AT) == ev.evaluate(d, AT, calibrate=False)                  # ① 기본 꺼짐 (options.json 없음)
    assert ev.calibration(d, AT)["보통"][1] == 6                                         # 샘플 공시체 6건 (기본 곡선으로 만든 값이라 s ≈ 0.35)

    cyl(d, [("9F-A1", "2026-12-03", "현장봉함", 15, 15.2, 15.4), ("9F-B1", "2026-12-03", "현장수중", 15.1, 15.3, 15.5),
            ("9F-C1", "2026-12-03", "표준양생", 20, 20, 20)])                             # ② 현장양생 2건 + 표준양생 1건(안 셈)
    assert ev.calibration(d, AT) == {"보통": (None, 2)}, ev.calibration(d, AT)
    r0, r1 = ev.evaluate(d, AT, calibrate=False), ev.evaluate(d, AT, calibrate=True)
    assert [a["predicted_due"] for a in r0] == [b["predicted_due"] for b in r1]
    assert all(b["reason"] == a["reason"] + "; 예측일: 기본 곡선 [가정] (보정 자료 부족 — 공시체 2건 < 3)" for a, b in zip(r0, r1)), r1[0]["reason"]

    early = [("9F-A1", 16.0), ("9F-B1", 17.2), ("9F-C1", 18.4)]                          # ③ 7일차 강도가 기본 곡선(약 13 MPa)보다 높음
    cyl(d, [(z, "2026-12-03", "현장봉함", f, f, f) for z, f in early])
    s, n = ev.calibration(d, AT)["보통"]
    te = e.equivalent_age([max(T, 5.0) for T in ev.daily_temps([], date(2026, 11, 26), 7)], e.MIXES["보통"][1])
    assert n == 3 and abs(s - e.calibrate_s(17.2, te, 24.0)) < 1e-12 and 0 < s < e.MIXES["보통"][0], (s, n, te)   # 중앙값 = 가운데 시험
    r0, r1 = ev.evaluate(d, AT, calibrate=False), ev.evaluate(d, AT, calibrate=True)
    assert rest(r0) == rest(r1)                                                          # 판정·게이트·추정 강도 그대로
    assert all(b["predicted_due"] <= a["predicted_due"] for a, b in zip(r0, r1)) and any(b["predicted_due"] < a["predicted_due"] for a, b in zip(r0, r1))
    assert all(b["reason"].endswith(f"; 예측일: 현장 보정 곡선 (공시체 3건, s={s:.3f}) [보정]") for b in r1), r1[0]["reason"]

    cyl(d, [(z, "2026-12-03", "현장봉함", f, f, f) for z, f in early] + [("9F-A2", "2026-12-30", "현장봉함", 25, 25, 25)])   # ④ 34일차
    assert ev.calibration(d, AT)["보통"][1] == 4                                          # 양생 5 ℃: te < 28 → 들어감
    assert ev.calibration(d, AT, cure_min=25.0)["보통"][1] == 3                           # 양생 25 ℃: 34일차 te ≥ 28 → 뺌 (7일차는 들어감)

    cyl(d, [(z, "2026-12-03", "현장봉함", f, f, f) for z, f in early] + [("9F-A2", "2026-12-03", "현장봉함", 30, 30, 30)])   # ⑤ 조기 강도 ≥ fck+Δf
    assert ev.calibration(d, AT)["보통"] == (s, 3)                                         # s ≤ 0 은 뺌 → 앞 3건 그대로

    with open(os.path.join(d, ev.OPTIONS), "w", encoding="utf-8") as f:                  # ⑥ 데이터 폴더 설정
        json.dump({"calibrate_due": True}, f)
    assert ev.evaluate(d, AT) == ev.evaluate(d, AT, calibrate=True) and ev.evaluate(d, AT, calibrate=False) != ev.evaluate(d, AT)
    with open(os.path.join(d, ev.OPTIONS), "w", encoding="utf-8") as f:
        f.write("{not json")
    assert ev.evaluate(d, AT) == ev.evaluate(d, AT, calibrate=False)                     # 못 읽으면 꺼짐

    h = os.path.join(t, "height")                                                       # ⑦ v6 층 높이 보정
    shutil.copytree(SAMPLE, h)
    base = {r["zone_id"]: r for r in ev.evaluate(h, AT)}
    assert ev.height_dt(12, ev.height_option(h)) == 0.0                                  # 기본 꺼짐
    with open(os.path.join(h, ev.OPTIONS), "w", encoding="utf-8") as f:
        json.dump({"height_correct": True, "story_m": 3.0}, f)
    assert abs(ev.height_dt(12, ev.height_option(h)) + 0.0065 * 36) < 1e-12 and ev.height_dt(-1, ev.height_option(h)) == 0.0
    on = {r["zone_id"]: r for r in ev.evaluate(h, AT)}
    wx = [z for z, r in base.items() if "기상 예측" in r["est_source"]]
    assert wx and all(ev.height_tag(ev.height_dt(on[z]["floor"], ev.height_option(h))) in on[z]["est_source"] for z in wx), [on[z]["est_source"] for z in wx]
    assert all(on[z]["f_est_min"] <= base[z]["f_est_min"] and on[z]["predicted_due"] >= base[z]["predicted_due"] for z in base)
    assert all(on[z]["f_est_min"] == base[z]["f_est_min"] for z in base if z not in wx)   # 센서 구역 강도 그대로
    k = os.path.join(t, "top")                                                          # ⑧ 최상층 기본값
    shutil.copytree(SAMPLE, k)
    plain = ev.evaluate(k, AT)
    with open(os.path.join(k, ev.OPTIONS), "w", encoding="utf-8") as f:
        json.dump({"top_floor": 12}, f)
    assert ev.top_option(k) == 12 and ev.evaluate(k, AT) == ev.evaluate(k, AT, top_floor=12) != plain
    with open(os.path.join(k, ev.OPTIONS), "w", encoding="utf-8") as f:
        json.dump({"top_floor": 12, "top_floor_auto": True}, f)
    a12, u12 = ev.evaluate(k, AT), ev.evaluate(k, AT, top_floor=12)
    tagged = [r["zone_id"] for r in a12 if r["reason"].endswith("; " + ev.TOP_AUTO_TAG)]
    assert tagged and tagged == [r["zone_id"] for r in u12 if r["level_gate"] == "OK(최상부)"], tagged
    assert [{**r, "reason": r["reason"].replace("; " + ev.TOP_AUTO_TAG, "")} for r in a12] == u12
    with open(os.path.join(k, ev.OPTIONS), "w", encoding="utf-8") as f:
        json.dump({"top_floor": True, "x": 1}, f)
    assert ev.top_option(k) is None and ev.evaluate(k, AT) == plain
    print("calibrate OK")
finally:
    shutil.rmtree(t, ignore_errors=True)

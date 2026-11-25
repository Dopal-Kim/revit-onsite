"""센서 없는 기본형 회귀 (기획 세션 원칙 3): 공시체 + 기온만으로 판정·한중 경고·요청서·승인 기록·타설 전 게이트·공정 계획이 돈다.
센서는 위치 근거를 더하는 선택 기능이어야 한다. 실행: python test_no_sensor.py"""
import os
import shutil
import tempfile
from datetime import date, datetime

import approval as ap
import evaluate as ev
import pour_gate as pg

HERE = os.path.dirname(os.path.abspath(__file__))
SAMPLE = os.path.join(HERE, "sample", "data")
AT = datetime(2026, 12, 31, 9)

t = tempfile.mkdtemp()
try:
    for n in ("zones.csv", "pours.csv", "cylinders.csv"):          # sensors.csv·sensor_strength.csv·weather.csv 없음
        shutil.copy(os.path.join(SAMPLE, n), t)
    rows = ev.evaluate(t, AT)
    assert rows and all(r["est_source"].startswith("기상 예측(센서 없음) — 평년 기온 [가정]") for r in rows), {r["est_source"] for r in rows}
    nine = {r["zone_id"]: r["decision"] for r in rows if r["floor"] == 9}
    assert set(nine.values()) <= {"해체 승인 요청 가능", "존치 (공시체 미달)"}, nine   # 34차 C: 센서 없음 = 공시체만, 기상 예측은 참고
    assert all(r["est_source"].endswith("· 참고 (판정에 안 씀)") and "구역 최소 추정" not in r["reason"] for r in rows), {r["reason"] for r in rows}
    cyl9 = {r["zone_id"]: r for r in rows if r["floor"] == 9}
    assert all((d == "해체 승인 요청 가능") == (cyl9[z]["f_cyl"] != "" and cyl9[z]["f_cyl"] >= cyl9[z]["threshold"]) for z, d in nine.items()), nine
    assert all(r["level_gate"] == "NG" for r in rows if r["floor"] >= 10)   # 규칙 게이트는 센서와 무관
    cold = ev.cold_checks(t, AT)
    assert all(w["kind"] == "한중" for w in cold)                           # 센서가 없으면 초기양생 온도 경고는 못 냄 (한중 여부만)
    html = ap.request_html(rows, "센서 없음 시험", AT, t)
    assert "기상 예측(센서 없음)" in html and "사슬 정상" in html
    ok = next(r for r in rows if r["decision"] == "해체 승인 요청 가능")
    ap.record(t, ok["zone_id"], "요청 제출", "시험", ok)
    assert ap.verify(t)[0]
    g = pg.pour_gate(t, "12F-A1", AT)
    assert g["decision"] == "타설 보류" and "동바리 확인 없음" in g["reason"]   # 동바리 확인 기록도 센서와 무관
finally:
    shutil.rmtree(t)
print("no-sensor self-check OK")

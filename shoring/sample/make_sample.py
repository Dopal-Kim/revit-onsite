"""가상 현장 데이터 생성 (9–12층, 층당 6구역, 9층 센서 6개·공시체) → sample/data/*.csv, 그리고 evaluate.py로 판정까지 확인.

센서 온도는 가상값: 평년 기온 + 일교차 ±4 °C + 수화열(중앙 18 °C, 단부 0.55배, 모서리 0.4배),
단부·모서리는 보온막 밖 외기, 북측은 그늘 −1.5 °C. 실측 CSV로 바꾸면 같은 형식으로 동작한다.
실행: python sample/make_sample.py
"""
import csv
import math
import os
import shutil
import sys
from datetime import date, datetime, timedelta

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))
import engine as e          # noqa: E402
import evaluate as ev       # noqa: E402

OUT = os.path.join(HERE, "data")
OUT_STRENGTH = os.path.join(HERE, "data_strength")   # 같은 데이터 + 무선 강도 센서 (기존 data 정답은 그대로 둔다)
# 강도 센서 (ID, x, y, 기준 온도 센서, 장비 차이 MPa) — 가상: 기준 온도 센서 기록으로 엔진 강도를 내고 차이를 더한 값을 6시간마다 보고
STRENGTH_SENSORS = [("W1", 25.0, 4.5, "S5", -0.6), ("W2", 5.0, 4.5, "S1", 0.0)]
X0, Y0 = 0.0, 0.0                           # Revit 데모 모델과 같은 좌표: 30 × 18 m 평면, 원점 = 남서 모서리, 구역 10 × 9 m
FIRST = date(2026, 10, 1)                   # 1층 타설, 7일 사이클
FLOORS = range(9, 13)
AT = datetime(2026, 12, 31, 9, 0)           # 판정 시각
SENSORS = [("S1", 15.0, 9.0, 1.0, False, 0.0), ("S2", 15.0, 17.5, 0.55, True, 1.5), ("S3", 15.0, 0.5, 0.55, True, 0.0),
           ("S4", 0.5, 17.5, 0.4, True, 1.5), ("S5", 29.5, 0.5, 0.4, True, 0.0), ("S6", 29.5, 9.0, 0.55, True, 0.0)]


def pour_date(fl):
    return FIRST + timedelta(7 * (fl - 1))


def write(name, rows, folder=OUT):
    with open(os.path.join(folder, name), "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0]))
        w.writeheader()
        w.writerows(rows)


def main():
    os.makedirs(OUT, exist_ok=True)
    zones, pours, cyl = [], [], []
    for fl in FLOORS:
        for j, row in enumerate("12"):
            for i, col in enumerate("ABC"):
                zid = f"{fl}F-{col}{row}"
                zones.append({"zone_id": zid, "floor": fl, "x0": X0 + 10 * i, "y0": Y0 + 9 * j, "x1": X0 + 10 * (i + 1), "y1": Y0 + 9 * (j + 1)})
                pours.append({"zone_id": zid, "pour_date": pour_date(fl).isoformat(), "pour_time": "08:00", "mix": "보통", "fck": 24})
    # 9층 현장양생 공시체: 판정 전날 시험, 수화열 없이 보온 양생 조건 (구역마다 ±0.3 MPa 흩어짐)
    d9 = pour_date(9)
    base = e.predict("보통", d9, (AT.date() - timedelta(1) - d9).days, 24.0)
    for k, z in enumerate(z for z in zones if z["floor"] == 9):
        v = base + (k % 3 - 1) * 0.3
        cyl.append({"zone_id": z["zone_id"], "test_date": (AT.date() - timedelta(1)).isoformat(), "curing": "현장봉함",
                    "f1": round(v - 0.4, 1), "f2": round(v, 1), "f3": round(v + 0.4, 1)})
    sensors = []
    start = datetime.combine(d9, datetime.min.time()) + timedelta(hours=8)
    for h in range(int((AT - start).total_seconds() // 3600) + 1):
        t = start + timedelta(hours=h)
        air = e.air_temp(t.date()) + 4 * math.sin((t.hour - 9) / 24 * 2 * math.pi)
        for sid, x, y, k, exposed, shade in SENSORS:
            temp = (air if exposed else max(air, 5.0)) + 18 * k * (h / 20) * math.exp(1 - h / 20) - shade
            sensors.append({"sensor_id": sid, "floor": 9, "x": x, "y": y, "timestamp": t.strftime("%Y-%m-%dT%H:%M"), "temp_c": round(temp, 2)})
    write("zones.csv", zones)
    write("pours.csv", pours)
    write("cylinders.csv", cyl)
    write("sensors.csv", sensors)
    make_strength(sensors, start)
    rows = ev.evaluate(OUT, AT)
    ev.write(rows, os.path.join(HERE, "zone_decisions_sample.csv"))
    nine = {r["zone_id"]: r["decision"] for r in rows if r["floor"] == 9}
    # self-check: 9층은 구역별로 결과가 갈리고, 10층 이상은 존치 층수 게이트에 걸린다
    assert "해체 승인 요청 가능" in nine.values() and any(v.startswith("존치") for v in nine.values()), nine
    assert all(r["level_gate"] == "NG" for r in rows if r["floor"] >= 10)
    for r in rows:
        if r["floor"] == 9:
            print(r["zone_id"], r["f_cyl"], r["f_est_min"], r["decision"], "|", r["reason"])
    print("sample OK →", OUT)


def make_strength(sensors, start):
    """data_strength/: data/ 사본 + sensor_strength.csv. 9F-C1 가운데 강도 센서가 온도 센서보다 낮게 보고 → C1 존치로 바뀌는지 확인."""
    os.makedirs(OUT_STRENGTH, exist_ok=True)
    for name in ("zones.csv", "pours.csv", "cylinders.csv", "sensors.csv"):
        shutil.copyfile(os.path.join(OUT, name), os.path.join(OUT_STRENGTH, name))
    ea = e.MIXES["보통"][1]
    rows = []
    for sid, x, y, ref, bias in STRENGTH_SENSORS:
        rs = [r for r in sensors if r["sensor_id"] == ref]
        for h in range(6, int((AT - start).total_seconds() // 3600) + 1, 6):
            t = start + timedelta(hours=h)
            mpa = e.strength_at(ev.sensor_te(rs, start, t, ea), 24.0, e.MIXES["보통"][0]) + bias
            rows.append({"sensor_id": sid, "floor": 9, "x": x, "y": y, "timestamp": t.strftime("%Y-%m-%dT%H:%M"), "mpa": round(max(mpa, 0.0), 1)})
    write("sensor_strength.csv", rows, OUT_STRENGTH)
    got = {r["zone_id"]: r for r in ev.evaluate(OUT_STRENGTH, AT) if r["floor"] == 9}
    assert got["9F-C1"]["decision"] == "존치 (구역 최소 강도 미달)" and "강도 센서" in got["9F-C1"]["est_source"], got["9F-C1"]
    for r in got.values():
        print("  [강도 센서]", r["zone_id"], r["f_est_min"], r["decision"], "|", r["est_source"])


if __name__ == "__main__":
    main()

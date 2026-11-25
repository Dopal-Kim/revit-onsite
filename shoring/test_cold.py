"""evaluate.cold_checks 자체 검사 (KCS 14 20 40 한중 콘크리트 초기양생): python test_cold.py"""
import os
import shutil
import tempfile
from datetime import datetime, timedelta

import evaluate as ev

HERE = os.path.dirname(os.path.abspath(__file__))
SAMPLE = os.path.join(HERE, "sample", "data")
AT = datetime(2026, 12, 31, 9)


def run(temps_by_hour):
    """9F-A1 가운데 센서 T1 하나, 타설 11-26 08:00 부터 시간별 온도 목록."""
    tmp = tempfile.mkdtemp()
    try:
        for n in ("zones.csv", "pours.csv"):
            shutil.copy(os.path.join(SAMPLE, n), tmp)
        t0 = datetime(2026, 11, 26, 8)
        with open(os.path.join(tmp, "sensors.csv"), "w", encoding="utf-8", newline="") as f:
            f.write("sensor_id,floor,x,y,timestamp,temp_c\n")
            for h, T in enumerate(temps_by_hour):
                f.write(f"T1,9,5,4.5,{(t0 + timedelta(hours=h)).isoformat(timespec='minutes')},{T}\n")
        return [x["kind"] for x in ev.cold_checks(tmp, AT) if x["zone_id"] == "9F-A1"]
    finally:
        shutil.rmtree(tmp)


warm = [15.0] * (24 * 10)
assert run(warm) == [], run(warm)                                   # 따뜻하면 경고 없음
assert run([3.0] * 12 + [15.0] * (24 * 10)) == ["초기양생 5℃"]       # 5 MPa 전 5 ℃ 미만
k = run([15.0] * (24 * 3) + [-2.0] * 24 + [15.0] * 24)             # 5 MPa 도달 뒤 2일 안 영하
assert k == ["도달 후 2일 0℃"], k
assert run([15.0] * (24 * 9) + [-2.0] * 24) == []                    # 도달 후 2일 지나 영하 → 이 규정 밖
sample = ev.cold_checks(SAMPLE, AT)
assert any(x["kind"] == "한중" and x["floor"] == 12 for x in sample)  # 12F 12-17 타설: 일평균 ≤ 4 ℃
print("cold self-check OK")

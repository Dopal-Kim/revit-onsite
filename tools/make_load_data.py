"""부하 시험 자료 (감사 A-20, 총괄 11-05) — shoring/sample/data 사본 + 합성 sensors.csv (센서 n개 × step 분 간격 × days 일).
크기 시험용이다: 온도는 일교차 모양의 결정적 합성 값(판정 규칙 시험 아님). 결과 폴더는 부른 쪽이 지운다(체크용 파일 쌓지 않기).
실행: python tools/make_load_data.py <새 폴더> [--sensors 30] [--days 60] [--step 10]   (기본 ≈ 26만 줄)
"""
import argparse
import csv
import math
import os
import shutil
from datetime import datetime, timedelta

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SAMPLE = os.path.join(ROOT, "shoring", "sample", "data")
END = datetime(2026, 12, 31, 9, 0)   # 샘플 판정 시각


def make(folder, sensors=30, days=60, step=10, end=END):
    """folder(없어야 함)에 샘플 사본 + 합성 sensors.csv → 줄 수. 센서는 9–12F 에 번갈아, 층 평면(30 × 18 m) 안에 고르게."""
    shutil.copytree(SAMPLE, folder)
    n = days * 24 * 60 // step
    t0 = end - timedelta(days=days)
    rows = 0
    with open(os.path.join(folder, "sensors.csv"), "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        w.writerow(["sensor_id", "floor", "x", "y", "timestamp", "temp_c"])
        for i in range(sensors):
            fl, k = 9 + i % 4, i // 4
            x, y = 2.5 + (k % 6) * 5.0, 4.5 if k < 6 else 13.5
            for j in range(n + 1):
                t = t0 + timedelta(minutes=step * j)
                temp = 8 + 6 * math.sin(2 * math.pi * (t.hour * 60 + t.minute) / 1440) + (i % 5) * 0.3
                w.writerow([f"L{i + 1}", fl, x, y, t.strftime("%Y-%m-%dT%H:%M"), f"{temp:.2f}"])
                rows += 1
    return rows


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description="부하 시험 자료 (합성 센서)")
    ap.add_argument("folder")
    ap.add_argument("--sensors", type=int, default=30)
    ap.add_argument("--days", type=int, default=60)
    ap.add_argument("--step", type=int, default=10)
    a = ap.parse_args()
    print(make(a.folder, a.sensors, a.days, a.step))

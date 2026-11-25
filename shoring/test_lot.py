"""v6.2 W1 공시체 타설 로트 회귀: python test_lot.py
pours.csv pour_id(같이 친 구역 묶음) + cylinders.csv 줄의 pour_id(zone_id 빈칸) → 묶음의 모든 구역에 같은 시험, 구역 자기 줄(같은 시험일) 우선.
샘플 9F 6구역을 한 묶음 P-9F-1 로 묶고 개별 공시체를 묶음 공시체 한 줄로 바꿔도 판정은 샘플 정답과 같아야 함 (C1 만 자기 줄 23.0 → 공시체 미달)."""
import csv
import os
import shutil
import tempfile
from datetime import datetime

import approval
import evaluate as ev
import quality
import strength_timeline as st
import zone_info as zi

HERE = os.path.dirname(os.path.abspath(__file__))
SAMPLE = os.path.join(HERE, "sample", "data")
AT = datetime(2026, 12, 31, 9)
LOT = "P-9F-1"

t = tempfile.mkdtemp()
try:
    for n in os.listdir(SAMPLE):
        if n.endswith(".csv"):
            shutil.copy(os.path.join(SAMPLE, n), t)
    pours = ev.read(t, "pours.csv")
    with open(os.path.join(t, "pours.csv"), "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(pours[0]) + ["pour_id"])
        w.writeheader()
        w.writerows({**p, "pour_id": LOT if p["zone_id"].startswith("9F-") else ""} for p in pours)
    with open(os.path.join(t, "cylinders.csv"), "w", encoding="utf-8", newline="") as f:
        f.write("zone_id,test_date,curing,f1,f2,f3,pour_id\n"
                ",2026-12-30,현장봉함,24.4,24.8,25.2,P-9F-1\n"     # 묶음 공통 (6구역)
                ",2026-12-10,현장봉함,18,18,18,P-9F-1\n"          # 앞 시험 — 보정 자료 1건으로만
                "9F-C1,2026-12-30,현장봉함,23.0,23.0,23.0,\n"     # 자기 줄 우선 (같은 시험일)
                ",2026-12-30,현장봉함,30,30,30,P-없음\n"          # pours.csv 에 없는 묶음 → 뺌
                ",2026-12-30,현장봉함,30,30,30,\n")               # 구역·묶음 다 빈 줄 → 뺌

    rows = ev.cyl_rows(t)
    by = {}
    for r in rows:
        by.setdefault(r["zone_id"], []).append(r)
    assert sorted(by) == ["9F-A1", "9F-A2", "9F-B1", "9F-B2", "9F-C1", "9F-C2"], sorted(by)
    assert [r["cyl_lot"] for r in by["9F-C1"]] == ["", "P-9F-1 · 6구역 공통"], by["9F-C1"]   # 12-30 은 자기 줄, 12-10 은 묶음
    assert all(len(v) == 2 and all(r["cyl_lot"] == "P-9F-1 · 6구역 공통" for r in v) for z, v in by.items() if z != "9F-C1")

    base = {r["zone_id"]: r for r in ev.evaluate(SAMPLE, AT)}
    got = {r["zone_id"]: r for r in ev.evaluate(t, AT)}
    a1 = got["9F-A1"]
    assert a1["decision"] == "해체 승인 요청 가능" and a1["f_cyl"] == 24.8 and a1["cyl_test_date"] == "2026-12-30", a1
    assert a1["reason"] == "기준 충족; 공시체 (타설 P-9F-1 · 6구역 공통) 24.8 MPa", a1["reason"]
    assert ev.lot_label(a1) == "타설 P-9F-1 · 6구역 공통" and ev.lot_label(base["9F-A1"]) == ""
    c1 = got["9F-C1"]
    assert c1["f_cyl"] == 23.0 and c1["decision"] == "존치 (공시체 미달)" and ev.lot_label(c1) == "", c1
    for z in ("9F-B1", "9F-C2", "9F-A2", "9F-B2"):      # 나머지는 샘플 정답과 같은 결론 (A2·B2 = 센서 존치)
        assert got[z]["decision"] == base[z]["decision"], (z, got[z]["decision"], base[z]["decision"])
    assert all(got[z]["reason"] == base[z]["reason"] for z in got if not z.startswith("9F-")), "10F↑ 사유 그대로"

    # 보정: 묶음 시험은 구역 수만큼 세지 않음 — 묶음 12-10 1건 + 묶음 12-30 1건 + C1 자기 줄 1건 = 3 (안 묶으면 6 + 5 + 1 = 12)
    assert ev.calibration(t, AT).get("보통", (None, 0))[1] == 3, ev.calibration(t, AT)
    # 구역 정보·강도 그림·품질 대장·요청서 칸
    zc = zi.zone_info(t, "9F-A1", AT)["cylinders"]
    assert [c["lot"] for c in zc] == ["P-9F-1 · 6구역 공통"] * 2, zc
    assert sum(c["zone_id"].startswith("9F-") for c in st.timeline(t)["cylinders"]) == 12
    assert len(quality.quality_ledger(t)) == len(pours)
    assert "타설 P-9F-1 · 6구역 공통" in approval.lot_cell(a1) and approval.lot_cell(base["9F-A1"]) == ""
    assert ev.input_checks(t, AT) is not None
    print("공시체 타설 로트 시험 통과 (묶음 6구역 · 자기 줄 우선 · 없는 묶음 무시 · 보정은 묶음 1건씩 · 사유·요청서 표기)")
finally:
    shutil.rmtree(t)

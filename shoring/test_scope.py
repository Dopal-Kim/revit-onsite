"""v6.2 W11 범위 밖 알림 회귀: python test_scope.py
샘플에 slab_mm·shore_mm 열을 더하고 지하층 구역 하나 → 사유 끝 '⚠ 범위 밖 가능 (…) — 구조기술자 검토 …', 결론은 그대로, options.json 으로 기준 바꿈, 요청서 ⑥ 현장 경고."""
import csv
import json
import os
import shutil
import tempfile
from datetime import datetime

import approval
import evaluate as ev

HERE = os.path.dirname(os.path.abspath(__file__))
SAMPLE = os.path.join(HERE, "sample", "data")
AT = datetime(2026, 12, 31, 9)

t = tempfile.mkdtemp()
try:
    for n in os.listdir(SAMPLE):
        if n.endswith(".csv"):
            shutil.copy(os.path.join(SAMPLE, n), t)
    base = {r["zone_id"]: r for r in ev.evaluate(t, AT)}
    zones = ev.read(t, "zones.csv")
    with open(os.path.join(t, "zones.csv"), "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(zones[0]) + ["slab_mm", "shore_mm"])
        w.writeheader()
        for z in zones:
            w.writerow({**z, "slab_mm": "350" if z["zone_id"] == "12F-C2" else "", "shore_mm": "5200" if z["zone_id"] == "9F-A1" else "3000"})
        w.writerow({"zone_id": "B1-A1", "floor": "-1", "x0": "0.0", "y0": "0.0", "x1": "10.0", "y1": "9.0", "slab_mm": "", "shore_mm": ""})
    with open(os.path.join(t, "pours.csv"), "a", encoding="utf-8") as f:
        f.write("B1-A1,2026-11-12,08:00,보통,24\n")
    got = {r["zone_id"]: r for r in ev.evaluate(t, AT)}
    tail = " — " + ev.SCOPE_TAIL
    assert got["9F-A1"]["decision"] == base["9F-A1"]["decision"] == "해체 승인 요청 가능", got["9F-A1"]
    assert got["9F-A1"]["reason"] == "기준 충족; ⚠ 범위 밖 가능 (동바리 높이 5.20 m ≥ 5 m (건설기술진흥법 시행령 제101조의2))" + tail, got["9F-A1"]["reason"]
    assert got["12F-C2"]["decision"] == base["12F-C2"]["decision"] and got["12F-C2"]["reason"].endswith("(슬래브 350 mm > 300 mm [가정])" + tail)
    assert "(지하층)" + tail in got["B1-A1"]["reason"], got["B1-A1"]["reason"]
    assert ev.scope_note(got["9F-B1"]) == "" and ev.scope_note(got["9F-A1"]).startswith("⚠ 범위 밖 가능 (동바리")
    with open(os.path.join(t, "options.json"), "w", encoding="utf-8") as f:
        json.dump({"scope_slab_max_mm": 400, "scope_shore_h_m": 99}, f)   # 동바리 높이 기준은 법령 값 (options 로 안 바뀜)
    got2 = {r["zone_id"]: r for r in ev.evaluate(t, AT)}
    assert ev.scope_note(got2["9F-A1"]) != "" and ev.scope_note(got2["12F-C2"]) == "" and ev.scope_note(got2["B1-A1"]) != ""
    os.remove(os.path.join(t, "options.json"))
    rows = ev.evaluate(t, AT)
    full = [{**r} for r in rows if r["zone_id"].startswith("9F-")]
    h = approval.request_html(full, "시험", AT, t)
    assert "9F-A1 ⚠ 범위 밖 가능 (동바리 높이 5.20 m ≥ 5 m (건설기술진흥법 시행령 제101조의2))" in h, "요청서 ⑥ 현장 경고"
    # W12 시공하중: 2.5 미만은 2.5 (낮추지 않음), 3.5 면 하중비가 커지고 검토서 ③ 식·꼬리표가 3.5
    import staged
    gd0 = ev.gate_details(t, AT)
    for v, want in ((1.0, 2.5), (3.5, 3.5)):
        with open(os.path.join(t, "options.json"), "w", encoding="utf-8") as f:
            json.dump({"live_load_kn_m2": v}, f)
        assert ev.live_load(t) == want and ev.live_note(t) == f"시공하중 {want:g} kN/m² [가정] — 자재 적재·장비 집중하중 미반영"
    gd = ev.gate_details(t, AT)
    assert gd["9F-A1"]["ratio"] > gd0["9F-A1"]["ratio"], (gd["9F-A1"], gd0["9F-A1"])
    rv = staged.review_html(t, AT, "9F-A1")
    assert "+ 1.6·3.5 =" in rv and "시공하중 3.5 kN/m² [가정] — 자재 적재·장비 집중하중 미반영." in rv
    os.remove(os.path.join(t, "options.json"))
    print("범위 밖 알림 시험 통과 (동바리 높이·슬래브·지하층 → 사유 ⚠, 결론 그대로, options.json, 요청서 ⑥) · 시공하중 W12")
finally:
    shutil.rmtree(t)

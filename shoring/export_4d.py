"""웹 공유판 JSON 내보내기 — 데모 모델용. 스키마: web/onsite_4d_schema.md v2.0 (C# 애드인 "웹 공유판" 버튼도 같은 JSON 을 만든다).

v2.0 (v6 3단계, docs/redesign_v6.md §4): 시공 순서(공정 계획) 대신 강도 예측 타임라인 —
strength_timeline.timeline() 의 층·구역 추정 곡선(참고)·현장양생 공시체 점·기준선 + evaluate.evaluate() 의 날짜별 구역 판정
(첫 타설 ~ 마지막 기록일, 기록만 — 계획 타설 없음) + 데이터 폴더 실측값(measurements[], field_input_spec §4) → 한 JSON.
데모 Revit 모델에는 슬래브·구역·동바리만 있으므로 벽·계단·창호는 벽식 공동주택 배치를 지어내고 "synthetic": true 로 표시한다.
표준 라이브러리만.

실행:
  python shoring/export_4d.py --out web/demo_4d.json [--data <데이터 폴더>]
  python shoring/export_4d.py --out web/demo_4d.json --html web/onsite_4d_demo.html   (뷰어 web/onsite_4d.html 사본에 JSON 을 넣음)
"""
import argparse
import csv
import json
import os
import re
import shutil
import sys
import tempfile
from datetime import date, datetime, timedelta

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import engine as e        # noqa: E402
import evaluate as ev     # noqa: E402
import strength_timeline as stl   # noqa: E402
import approval as apv    # noqa: E402
import evidence as evd    # noqa: E402

SAMPLE = os.path.join(HERE, "sample", "data")
FLOORS = list(range(1, 16))
H, T_SLAB, W, D = 3.0, 0.21, 30.0, 18.0          # 층고, 슬래브 두께, 평면 (m) — 정의서 §3.1
ZONE_COLS = [("A", 0.0, 10.0), ("B", 10.0, 20.0), ("C", 20.0, 30.0)]
ZONE_ROWS = [("1", 0.0, 9.0), ("2", 9.0, 18.0)]   # 1 = 남측 줄, 2 = 북측 줄
ZONE_ORDER = ["A1", "B1", "C1", "A2", "B2", "C2"]
MIX, FCK, CURE_MIN = "보통", 24.0, 5.0
WT = 0.2                                           # 벽 두께 [가정]


def rect(x0, y0, x1, y1):
    r = lambda v: round(v, 3)
    return [[r(x0), r(y0)], [r(x1), r(y0)], [r(x1), r(y1)], [r(x0), r(y1)]]


def zone_rects(k):
    return {f"{k}F-{c}{r}": (x0, y0, x1, y1) for r, y0, y1 in ZONE_ROWS for c, x0, x1 in ZONE_COLS}


def zone_of(k, poly):
    """부재 평면 무게중심이 든 구역 (경계는 x0 ≤ x < x1 쪽)."""
    cx = sum(p[0] for p in poly) / len(poly)
    cy = sum(p[1] for p in poly) / len(poly)
    for zid, (x0, y0, x1, y1) in zone_rects(k).items():
        if x0 <= cx < x1 and y0 <= cy < y1:
            return zid
    return f"{k}F-C2"


# ---------- 가상 벽·창호·계단 배치 (벽식 3세대/층) ----------
# 세대 폭 10 m. (x0, x1, 창대 높이) — 남측 = 거실 발코니창 + 침실창, 북측 = 침실·주방창. B세대 북측 가운데는 계단실.
SOUTH_OPEN = [(o + a, o + b, sill) for o in (0, 10, 20) for a, b, sill in ((1.2, 5.2, 0.0), (6.2, 8.6, 0.9))]
NORTH_OPEN = [(1.5, 3.0, 0.9), (6.0, 8.0, 0.9), (11.5, 13.0, 0.9), (17.2, 18.8, 0.9), (21.5, 23.0, 0.9), (26.0, 28.0, 0.9)]
CORE = (13.6, 12.1, 16.4, 17.8)                    # 계단 평면 (계단실 안쪽)


def facade(y0, y1, openings):
    """외벽 한 줄을 개구부로 나눈 벽 토막 [(x0, x1)]."""
    segs, x = [], 0.0
    for a, b, _ in sorted(openings):
        if a > x:
            segs.append((x, a))
        x = b
    if x < W:
        segs.append((x, W))
    return [rect(a, y0, b, y1) for a, b in segs]


def story_layout():
    """한 층(L(k-1)–Lk) 안 부재: [(category, footprint, dz0, dz1 or None)]. dz 는 층 바닥에서 높이, None = 슬래브 밑면까지."""
    walls = facade(0.0, WT, SOUTH_OPEN) + facade(D - WT, D, NORTH_OPEN)
    walls += [rect(0, WT, WT, 9), rect(0, 9, WT, D - WT), rect(W - WT, WT, W, 9), rect(W - WT, 9, W, D - WT)]  # 측벽
    for x in (10.0, 20.0):                                                                                   # 세대 경계벽
        walls += [rect(x - WT / 2, WT, x + WT / 2, 9), rect(x - WT / 2, 9, x + WT / 2, D - WT)]
    walls += [rect(o + 5 - WT / 2, WT, o + 5 + WT / 2, 6.0) for o in (0, 10, 20)]                            # 세대 안 칸막이벽
    walls += [rect(13.4, 11.9, 13.6, D - WT), rect(16.4, 11.9, 16.6, D - WT),                                # 계단실 벽
              rect(13.6, 11.9, 14.2, 12.1), rect(15.8, 11.9, 16.4, 12.1)]
    out = [("wall", p, 0.0, None) for p in walls]
    for y0, y1, ops in ((0.0, WT, SOUTH_OPEN), (D - WT, D, NORTH_OPEN)):
        for a, b, sill in ops:
            if sill:
                out.append(("wall", rect(a, y0, b, y1), 0.0, sill))                                          # 창대 아래 벽
            out.append(("window", rect(a, y0 + 0.05, b, y1 - 0.05), sill, None))
    out.append(("stair", rect(*CORE), 0.0, None))
    return out


def build_elements():
    els = []
    layout = story_layout()
    for k in FLOORS:
        zt, zb, base = round(k * H, 3), round(k * H - T_SLAB, 3), round((k - 1) * H, 3)
        els.append({"id": f"{k}F-SLAB", "category": "slab", "floor": k, "zone": None, "name": f"{k}층 슬래브 210",
                    "geometry": {"footprint": rect(0, 0, W, D), "z0": zb, "z1": zt}, "synthetic": False})
        for zid, (x0, y0, x1, y1) in zone_rects(k).items():
            els.append({"id": zid, "category": "zone", "floor": k, "zone": zid, "name": f"구역 {zid}",
                        "geometry": {"footprint": rect(x0, y0, x1, y1), "z0": zb, "z1": zt}, "synthetic": False})
            els.append({"id": f"{zid}-SH", "category": "shore_zone", "floor": k, "zone": zid,
                        "name": f"{zid} 동바리 (1.2 m 간격 파이프서포트 묶음)",
                        "geometry": {"footprint": rect(x0 + 0.15, y0 + 0.15, x1 - 0.15, y1 - 0.15), "z0": base, "z1": zb},
                        "spacing_m": [1.2, 1.2], "synthetic": False})
        n = {"wall": 0, "window": 0, "stair": 0}
        for cat, poly, dz0, dz1 in layout:
            n[cat] += 1
            tag = {"wall": "W", "window": "WIN", "stair": "ST"}[cat]
            els.append({"id": f"{k}F-{tag}{n[cat]:02d}", "category": cat, "floor": k, "zone": zone_of(k, poly),
                        "name": {"wall": "벽 200", "window": "창호", "stair": "계단"}[cat],
                        "geometry": {"footprint": poly, "z0": round(base + dz0, 3),
                                     "z1": zb if dz1 is None else round(base + dz1, 3)}, "synthetic": True})
    return els


# ---------- 공정·판정 ----------
def read_csv(name, folder=None):
    with open(os.path.join(folder or SAMPLE, name), encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def copy_data(folder, tmp, zones):
    """계산용 사본: zones.csv = 내보낼 구역 (zones [(id, floor, x0, y0, x1, y1)]), 나머지 CSV·options.json 은 데이터 폴더 그대로 (pours.csv 포함)."""
    with open(os.path.join(tmp, "zones.csv"), "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(["zone_id", "floor", "x0", "y0", "x1", "y1"])
        w.writerows(zones)
    for name in os.listdir(folder):
        if (name.endswith(".csv") or name == ev.OPTIONS) and name != "zones.csv" and os.path.isfile(os.path.join(folder, name)):
            shutil.copy(os.path.join(folder, name), tmp)


def run_decisions(tmp, d0, d1, step, top):
    """d0..d1 매 step 일 09:00 에 evaluate() — 그날까지 기록상 타설된 구역만 (pours.csv 기록 줄 그대로, 계획 타설 없음)."""
    pours = read_csv("pours.csv", tmp)
    head = ["zone_id", "pour_date", "pour_time", "mix", "fck"]
    out, d = [], d0
    try:
        while d <= d1:
            at = datetime(d.year, d.month, d.day, 9)
            done = sorted((r for r in pours if r["pour_date"][:10] <= d.isoformat()), key=lambda r: r["zone_id"])
            if done:
                with open(os.path.join(tmp, "pours.csv"), "w", encoding="utf-8-sig", newline="") as f:
                    w = csv.writer(f)
                    w.writerow(head)
                    w.writerows([r.get(k) or "" for k in head] for r in done)
                for r in ev.evaluate(tmp, at, CURE_MIN, top_floor=top):
                    out.append({"at": at.isoformat(timespec="minutes"), "zone": r["zone_id"],
                                "decision": r["decision"], "reason": r["reason"], "predicted_due": r["predicted_due"],
                                "f_cyl": r["f_cyl"] if r["f_cyl"] != "" else None, "f_est_min": r["f_est_min"],
                                "est_source": r["est_source"], "level_gate": r["level_gate"], "age_days": r["age_days"],
                                "f_est_xy": list(r["f_est_xy"]) if r["f_est_xy"] else None})
            d += timedelta(step)
        return out
    finally:
        with open(os.path.join(tmp, "pours.csv"), "w", encoding="utf-8-sig", newline="") as f:   # 사본을 원래 타설 기록으로 되돌림
            w = csv.writer(f)
            w.writerow(head)
            w.writerows([r.get(k) or "" for k in head] for r in pours)


def actual_until(folder):
    """실적(타설·공시체·센서 기록)의 마지막 날 'YYYY-MM-DD' — 없으면 None (감사 A-27)."""
    days = [r[col][:10] for name, col in (("pours.csv", "pour_date"), ("cylinders.csv", "test_date"),
                                          ("sensors.csv", "timestamp"), ("sensor_strength.csv", "timestamp"))
            for r in ev.read(folder, name) if r.get(col)]
    return max(days, default=None)


def timeline_json(tl):
    """strength_timeline.timeline 결과 → JSON (스키마 v2.0 strength_timeline)."""
    return {"start": tl["start"].isoformat(), "end": tl["end"].isoformat(),
            "floors": [{"floor": f["floor"], "label": f["label"], "fck": f["fck"], "mpa": f["values"]} for f in tl["floors"]],
            "zones": [{"zone": z, "mpa": v} for z, v in tl["zones"].items()],
            "cylinders": [{"date": c["date"].isoformat(), "zone": c["zone_id"], "floor": c["floor"], "mpa": c["mpa"], "curing": c["curing"]}
                          for c in tl["cylinders"]],
            "lines": [{"name": n, "mpa": m} for n, m in tl["lines"]]}


def zone_center(zid):
    k = int(zid.split("F-")[0])
    x0, y0, x1, y1 = zone_rects(k)[zid]
    return round((x0 + x1) / 2, 2), round((y0 + y1) / 2, 2)


TEMP_STEP_H = 6                                     # 온도 기록 줄이기 간격 (시간)


def approvals_rows(folder):
    """approvals.csv (사람이 입력한 요청 제출·감리 승인 기록·해체 완료·재받침) → 4D 에서 그 시각부터 상태 6·7·8 로 표시.
    사슬이 깨졌으면 넣지 않고 경고만 (고친 기록을 화면에 믿을 만한 것처럼 보이지 않게). 스키마 v1.1 의 선택 필드."""
    ok, msg = apv.verify(folder)
    if not ok:
        print("approvals.csv 무시:", msg)
        return []
    return [{"zone": r["zone_id"], "at": r["timestamp"][:16], "action": r["action"], "status": apv.ACTIONS[r["action"]],
             "actor": r["actor"]} for r in apv.read_log(folder)]


def measurements(folder=SAMPLE):
    """field_input_spec §2·§4 — 데이터 폴더의 실측값 → measurements[]. 온도는 TEMP_STEP_H 시간 간격으로 줄인다 (판정 계산은 원본 전체)."""
    out = []
    by_id = {}
    for r in (read_csv("sensors.csv", folder) if os.path.exists(os.path.join(folder, "sensors.csv")) else []):
        by_id.setdefault(r["sensor_id"], []).append(r)
    for sid, rs in sorted(by_id.items()):
        rs.sort(key=lambda r: r["timestamp"])
        t0 = datetime.fromisoformat(rs[0]["timestamp"])
        keep = [r for r in rs if (datetime.fromisoformat(r["timestamp"]) - t0).total_seconds() % (TEMP_STEP_H * 3600) == 0]
        if keep[-1] is not rs[-1]:
            keep.append(rs[-1])
        for r in keep:
            k, x, y = int(r["floor"]), float(r["x"]), float(r["y"])
            out.append({"kind": "sensor_temp", "point_id": sid, "floor": k, "zone": zone_of(k, [[x, y]]), "x": x, "y": y,
                        "timestamp": r["timestamp"][:16], "value": float(r["temp_c"]), "unit": "°C",
                        "entered_by": r.get("entered_by") or "센서 로거(샘플)"})
    if os.path.exists(os.path.join(folder, "sensor_strength.csv")):       # 무선 강도 센서 (있을 때만)
        for r in read_csv("sensor_strength.csv", folder):
            k, x, y = int(r["floor"]), float(r["x"]), float(r["y"])
            out.append({"kind": "sensor_strength", "point_id": r["sensor_id"], "floor": k, "zone": zone_of(k, [[x, y]]),
                        "x": x, "y": y, "timestamp": r["timestamp"][:16], "value": float(r["mpa"]), "unit": "MPa",
                        "entered_by": r.get("entered_by") or "강도 센서(샘플)"})
    for r in (read_csv("cylinders.csv", folder) if os.path.exists(os.path.join(folder, "cylinders.csv")) else []):
        z, f = r["zone_id"], [float(r[k]) for k in ("f1", "f2", "f3")]
        x, y = zone_center(z)
        out.append({"kind": "cylinder", "point_id": f"CYL-{z}", "floor": int(z.split("F-")[0]), "zone": z, "x": x, "y": y,
                    "timestamp": r["test_date"] + "T09:00", "value": round(sum(f) / 3, 2), "unit": "MPa",
                    "entered_by": r.get("entered_by") or "시험 성적서(샘플)", "f": f, "curing": r.get("curing", "")})
    if os.path.exists(os.path.join(folder, "rebound.csv")):               # 반발경도 — 조회만, 판정 미사용
        for r in read_csv("rebound.csv", folder):
            z = r["zone_id"]
            out.append({"kind": "rebound", "point_id": f"RB-{z}-{r['x']}-{r['y']}", "floor": int(z.split("F-")[0]), "zone": z,
                        "x": float(r["x"]), "y": float(r["y"]), "timestamp": r["test_date"][:10] + "T09:00",
                        "value": float(r["r_value"]), "unit": "R", "entered_by": r.get("entered_by") or "샘플"})
    return out


ASSUMPTIONS = [
    ("geom-walls", "벽·계단·창호는 데모 Revit 모델에 없어서 벽식 3세대/층 배치를 지어 넣었습니다 (외벽·세대 경계벽·칸막이벽·계단실, "
                   "벽 두께 200 mm). 화면에서 '가상' 표시가 붙은 부재입니다.", "[가정]"),
    ("geom-shore", "동바리는 구역마다 상자 하나로 묶어 보여 줍니다 (실제는 1.2 m × 1.2 m 간격 파이프서포트, 층당 약 416본).", "[가정]"),
    ("data-weather", "기온은 서울 평년값입니다 (기상청 실측 미반영). 센서 기록은 9층(샘플)만 있습니다.", "[가정]"),
    ("data-cyl", "현장양생 공시체 결과는 9층 샘플 가상 데이터(make_sample.py)만 있습니다. 타설 기록은 9–12층(샘플)만 있어 1–8·13–15층은 '타설 전'으로 보입니다.",
     "[가정]"),
    ("tl-estimate", f"강도 곡선은 타설일·기온으로 계산한 추정(참고, 판정에 안 씀)입니다. 층 곡선 = 그날까지 타설된 구역 중 가장 낮은 값, "
                    f"그림 기간 = 첫 타설 ~ 마지막 타설 + {stl.SPAN_DAYS}일.", "[가정]"),
    ("data-decision", "구역 판정은 첫 타설일부터 마지막 기록일까지 날마다 09:00 에 기록(공시체·3 m 안 센서)만으로 다시 계산한 값입니다. 그 뒤 날짜는 판정 없음.", "[가정]"),
    ("data-meas", f"측정값 표식(샘플 가상 데이터): 9층 온도 센서 6개(기록을 {TEMP_STEP_H}시간 간격으로 줄여 표시, 판정은 1시간 원본), 공시체 6건. "
                  "강도 센서(sensor_strength.csv)·반발경도(rebound.csv)는 데이터 폴더에 있을 때만 들어갑니다.", "[가정]"),
    ("view-color", "구역 색은 그날 추정 강도 ÷ fck (40·60·80·100 % 구간, 화면 표시용)이고, 판정 근거 문구는 그날의 구역 판정에서 가져옵니다.", "[가정]"),
]


def build(step, folder=SAMPLE):
    zones = [(zid, k, *r) for k in FLOORS for zid, r in zone_rects(k).items()]
    tmp = tempfile.mkdtemp(prefix="onsite4d_")
    try:
        copy_data(folder, tmp, zones)
        tl = stl.timeline(tmp, cure_min=CURE_MIN)
        if not tl["dates"]:
            raise SystemExit("타설 기록이 없습니다 (pours.csv) — 강도 타임라인을 만들 수 없습니다.")
        until = actual_until(folder)
        d1 = min(tl["end"], date.fromisoformat(until)) if until else tl["start"]
        decisions = run_decisions(tmp, tl["start"], d1, step, max(FLOORS))
        evid = [{"zone": x["zone_id"], "date": x["date"][:10], "stage": x["stage"], "source": x["source"]} for x in evd.entries(folder)]
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    return {
        "schema": "revit-onsite-4d",
        "version": "2.0",
        "project": {"name": "Revit On-Site 데모 — RC 공동주택 15층 (30 × 18 m)", "source": "demo-python",
                    "model_file": "revit/shoring_demo.rvt", "generated_at": datetime.now().isoformat(timespec="seconds"),
                    "units": "m", "origin": "건물 남서 모서리 (그리드 X1·Y1 교점), x = 동, y = 북, z = 1층 바닥 0",
                    "fck_mpa": FCK, "min_levels": e.MIN_LEVELS, "cure_min_c": CURE_MIN, "measurements_synthetic": True,   # 9층 측정값 = make_sample.py 가상값 (감사 A-17)
                    "date_range": [tl["start"].isoformat(), tl["end"].isoformat()], "judged_until": until, "zone_order": ZONE_ORDER},
        "levels": [{"floor": k, "name": f"{k}층", "z": round(k * H, 3), "slab_thickness": T_SLAB} for k in FLOORS],
        "thresholds": {"side_form_mpa": e.thresholds(FCK)["측면"], "remove_mpa": e.thresholds(FCK)["다층 동바리 해체"],
                       "support_slab_mpa": e.thresholds(FCK)["지지 슬래브"]},
        "elements": build_elements(),
        "strength_timeline": timeline_json(tl),
        "decisions": decisions,
        "measurements": measurements(folder),
        "approvals": approvals_rows(folder),
        "evidence": evid,   # 근거 원장 (evidence.py) — '근거 단계' 색
        "assumptions": [{"id": i, "text": t, "basis": b} for i, t, b in ASSUMPTIONS],
    }


def check(data):
    """스키마 최소 검사 — 뷰어가 믿고 쓰는 필드."""
    cats = {"slab", "wall", "stair", "window", "shore_zone", "zone"}
    for el in data["elements"]:
        g = el["geometry"]
        assert el["category"] in cats and len(g["footprint"]) >= 3 and g["z1"] > g["z0"], el["id"]
    assert len({el["id"] for el in data["elements"]}) == len(data["elements"]), "id 중복"
    zones = {el["zone"] for el in data["elements"] if el["category"] == "zone"}
    assert all(el["zone"] in zones for el in data["elements"] if el["zone"]), "부재 구역이 구역 상자에 없음"
    tl = data["strength_timeline"]
    n = (date.fromisoformat(tl["end"]) - date.fromisoformat(tl["start"])).days + 1
    assert all(len(f["mpa"]) == n for f in tl["floors"]) and all(len(z["mpa"]) == n for z in tl["zones"]), "강도 곡선 길이"
    assert all(z["zone"] in zones for z in tl["zones"]) and all(c["zone"] in zones for c in tl["cylinders"]), "강도 곡선 구역"
    until = data["project"]["judged_until"]
    assert all(x["at"][:10] <= (until or "") for x in data["decisions"]), "판정은 마지막 기록일까지"
    kinds = {"sensor_temp", "sensor_strength", "cylinder", "rebound"}
    for m in data.get("measurements", []):
        assert m["kind"] in kinds and isinstance(m["value"], float) and len(m["timestamp"]) == 16, m
        assert m["zone"] in {el["zone"] for el in data["elements"] if el["category"] == "zone"}, m
    words = json.dumps(data, ensure_ascii=False)
    assert "해체하세요" not in words and "승인됨" not in words, "금지 문구"


def main():
    ap = argparse.ArgumentParser(description="Revit On-Site 웹 공유판 JSON (데모 모델)")
    ap.add_argument("--out", default=os.path.join("web", "demo_4d.json"))
    ap.add_argument("--html", help="이 경로에 뷰어(web/onsite_4d.html) 사본을 만들고 JSON 을 넣음")
    ap.add_argument("--viewer", default=os.path.join(os.path.dirname(HERE), "web", "onsite_4d.html"))
    ap.add_argument("--step", type=int, default=1, help="구역 판정 간격 (일)")
    ap.add_argument("--data", default=SAMPLE, help="데이터 폴더 (pours·cylinders·sensors·weather CSV — 기본 shoring/sample/data)")
    a = ap.parse_args()
    data = build(a.step, a.data)
    check(data)
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    with open(a.out, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
    tl = data["strength_timeline"]
    print(f"{a.out}: 부재 {len(data['elements'])} · 강도 곡선 {len(tl['floors'])}개층·{len(tl['zones'])}구역 · 판정 {len(data['decisions'])} · "
          f"실측 {len(data['measurements'])}")
    if a.html:
        with open(a.viewer, encoding="utf-8") as f:
            html = f.read()
        blob = json.dumps(data, ensure_ascii=False, separators=(",", ":")).replace("</", "<\\/")
        html, n = re.subn(r'(<script type="application/json" id="data">)(.*?)(</script>)',
                          lambda m: m.group(1) + blob + m.group(3), html, count=1, flags=re.S)
        if n != 1:
            raise SystemExit(f'{a.viewer} 에 <script type="application/json" id="data"> 블록이 없습니다')
        with open(a.html, "w", encoding="utf-8") as f:
            f.write(html)
        print(f"{a.html}: 뷰어 + 데이터 {len(blob) // 1024} KB")


if __name__ == "__main__":
    import sys as _sys; [_s.reconfigure(errors="replace") for _s in (_sys.stdout, _sys.stderr) if hasattr(_s, "reconfigure")]   # cp949 콘솔에서 — 등으로 죽지 않게
    main()

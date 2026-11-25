"""v6 3단계 강도 예측 타임라인 (docs/redesign_v6.md §4) — 4D 시공 순서 대신: 날짜를 움직이면 층별 추정 강도 곡선 위 점,
그날까지 현장양생 공시체 실측 점, 5·14·fck MPa 기준선. 3D 는 구역을 그날 추정 강도 색으로 (동바리는 그대로 — 해체 애니메이션 없음).

추정 = 판정의 예측 해체 가능일과 같은 식 (engine.predict: 타설일부터 일 단위, 기온 = weather.csv 또는 평년 [가정], 양생 하한 cure_min,
층 높이 보정 = options.json, evaluate.height_dt) — 화면엔 '참고 (판정에 안 씀)'. 판정은 evaluate.py (현장양생 공시체·3 m 안 센서).
층 곡선 = 그 층에서 그날까지 타설된 구역 중 가장 낮은 값 (보수 측). 기준선 = engine.thresholds: 측면 5 · 지지 슬래브 14 · 다층 해체 fck [확정].
색 구간 = 추정 ÷ fck < 40·60·80·100 % · 이상 [가정 — 화면 표시용, 애드인 강도 표시와 같은 구간].
표준 라이브러리만 — C# Engine StrengthTimeline.cs 와 Dump 바이트 동일 (Engine.Check).
실행: python strength_timeline.py <데이터 폴더> [--cure-min 5] [--start YYYY-MM-DD --end YYYY-MM-DD] · 자체 검사: --self-test
"""
import os
import sys
from datetime import date, timedelta

import engine as e
import evaluate as ev

STEPS = (0.4, 0.6, 0.8, 1.0)   # [가정] 화면 색 구간 (추정 ÷ fck)
SPAN_DAYS = 56                 # [가정] 끝 날짜 기본 = 마지막 타설 + 56일 (그림 범위만)
MAX_AGE = 399                  # 기온 목록 400일 (evaluate 예측일과 같은 한도)


def label_of(fl, zone_ids):
    """층 이름 = 첫 구역 ID 의 '-' 앞, 없으면 번호 (음수 = B) — shore_input.floor_label 과 같은 규칙."""
    head = zone_ids[0].split("-")[0] if zone_ids and "-" in zone_ids[0] else ""
    return head or (f"B{-fl}" if fl < 0 else f"{fl}F")


def step_of(mpa, fck):
    """색 구간 0–4 (None = 값 없음)."""
    if mpa is None:
        return None
    r = mpa / fck
    return next((i for i, s in enumerate(STEPS) if r < s), len(STEPS))


def timeline(folder, start=None, end=None, cure_min=5.0):
    """→ {start, end, dates[], floors[{floor, label, fck, values[]}], zones{zone_id: values[]}, cylinders[{date, zone_id, floor, mpa, curing}], lines[(이름, MPa)]}.
    values[i] = dates[i] 의 추정 MPa (소수 1자리) 또는 None (타설 전). 타설 기록이 없으면 dates 가 빈 목록."""
    zones = ev.read(folder, "zones.csv")
    floor_of = {z["zone_id"]: int(z["floor"]) for z in zones}
    pours = {p["zone_id"]: p for p in ev.read(folder, "pours.csv") if p["zone_id"] in floor_of}
    weather = ev.read(folder, "weather.csv")
    hopt = ev.height_option(folder)
    if not pours:
        return {"start": None, "end": None, "dates": [], "floors": [], "zones": {}, "cylinders": [], "lines": []}
    cast = {z: date.fromisoformat(p["pour_date"]) for z, p in pours.items()}
    d0 = start or min(cast.values())
    d1 = end or max(cast.values()) + timedelta(SPAN_DAYS)
    dates = [d0 + timedelta(i) for i in range((d1 - d0).days + 1)]
    zv = {}
    for z in sorted(pours, key=lambda z: (floor_of[z], z)):
        p, c = pours[z], cast[z]
        t400 = [T + ev.height_dt(floor_of[z], hopt) for T in ev.daily_temps(weather, c, 400)]
        zv[z] = [None if d < c else round(e.predict(p["mix"], c, min((d - c).days, MAX_AGE), float(p["fck"]), cure_min, t400), 1) for d in dates]
    floors = []
    for fl in sorted({floor_of[z] for z in pours}):
        zs = sorted(z for z in pours if floor_of[z] == fl)
        vals = [min((zv[z][i] for z in zs if zv[z][i] is not None), default=None) for i in range(len(dates))]
        floors.append({"floor": fl, "label": label_of(fl, sorted(z for z, f in floor_of.items() if f == fl)), "fck": min(float(pours[z]["fck"]) for z in zs), "values": vals})
    cyl = []
    for r in ev.cyl_rows(folder):               # W1: 타설 묶음 공통 시험 → 묶음의 구역마다
        if r.get("curing") in e.FIELD_CURINGS and r["zone_id"] in floor_of:
            cyl.append({"date": date.fromisoformat(r["test_date"]), "zone_id": r["zone_id"], "floor": floor_of[r["zone_id"]],
                        "mpa": round(sum(float(r[k]) for k in ("f1", "f2", "f3")) / 3, 1), "curing": r["curing"]})
    fcks = sorted({float(p["fck"]) for p in pours.values()})
    th = e.thresholds(fcks[0])
    lines = [("측면 거푸집", th["측면"]), ("지지 슬래브", th["지지 슬래브"])] + [(f"fck {f:g}", f) for f in fcks]
    return {"start": d0, "end": d1, "dates": dates, "floors": floors, "zones": zv, "cylinders": sorted(cyl, key=lambda c: (c["date"], c["zone_id"])), "lines": lines}


def dump(res):
    """대조용 글 (C# Dump 와 바이트 동일): 범위·기준선·층·구역 곡선·공시체 점."""
    def v(x):
        return "-" if x is None else f"{x:g}"
    out = [f"range|{res['start']}|{res['end']}|{len(res['dates'])}"]
    out += [f"line|{n}|{m:g}" for n, m in res["lines"]]
    out += [f"floor|{f['floor']}|{f['label']}|{f['fck']:g}|" + ",".join(v(x) for x in f["values"]) for f in res["floors"]]
    out += [f"zone|{z}|" + ",".join(v(x) for x in vals) for z, vals in res["zones"].items()]
    out += [f"cyl|{c['date']}|{c['zone_id']}|{c['floor']}|{c['mpa']:g}|{c['curing']}" for c in res["cylinders"]]
    return "\n".join(out) + "\n"


if __name__ == "__main__":
    [s.reconfigure(errors="replace") for s in (sys.stdout, sys.stderr) if hasattr(s, "reconfigure")]
    if "--self-test" in sys.argv:
        import shutil
        import tempfile
        import json
        sample = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sample", "data")
        r = timeline(sample)
        assert r["start"] == date(2026, 11, 26) and r["end"] == date(2027, 2, 11) and len(r["dates"]) == 78, (r["start"], r["end"])
        nine = next(f for f in r["floors"] if f["floor"] == 9)
        i30 = r["dates"].index(date(2026, 12, 30))
        assert nine["values"][0] is not None and nine["values"][i30] > nine["values"][7], nine["values"][:10]
        twelve = next(f for f in r["floors"] if f["floor"] == 12)
        assert twelve["values"][0] is None and twelve["values"][-1] is not None                   # 12F 는 12-17 타설 — 그 전엔 값 없음
        assert [c["zone_id"] for c in r["cylinders"]][:1] and all(c["curing"] in e.FIELD_CURINGS for c in r["cylinders"])
        assert [n for n, _ in r["lines"]] == ["측면 거푸집", "지지 슬래브", "fck 24"] and r["lines"][1][1] == 14.0
        # 판정의 예측 해체 가능일과 같은 식: 9F-A1 곡선이 fck 에 처음 닿는 날 = predicted_due (판정 시각 전후 무관)
        due = next(x["predicted_due"] for x in ev.evaluate(sample, ev.datetime(2026, 12, 31, 9)) if x["zone_id"] == "9F-A1")
        za = r["zones"]["9F-A1"]
        hit = next(r["dates"][i] for i, x in enumerate(za) if x is not None and x >= 24)
        assert hit.isoformat() <= due, (hit, due)                                                # 반올림(0.1) 때문에 하루 이르게 닿을 수 있음
        assert step_of(None, 24) is None and step_of(9.5, 24) == 0 and step_of(23.9, 24) == 3 and step_of(24, 24) == 4
        t = tempfile.mkdtemp()
        try:
            for n in ("zones.csv", "pours.csv", "cylinders.csv"):
                shutil.copy(os.path.join(sample, n), t)
            base = timeline(t)
            with open(os.path.join(t, ev.OPTIONS), "w", encoding="utf-8") as f:                 # 층 높이 보정 켬 → 위층일수록 같거나 낮음
                json.dump({"height_correct": True, "story_m": 3.0}, f)
            hi = timeline(t)
            assert all(all(b is None or h <= b for h, b in zip(fh["values"], fb["values"])) for fh, fb in zip(hi["floors"], base["floors"]))
            assert timeline(t, cure_min=15.0)["floors"][0]["values"][10] >= base["floors"][0]["values"][10]   # 가열 양생이면 같거나 높음
            os.remove(os.path.join(t, "pours.csv"))
            open(os.path.join(t, "pours.csv"), "w", encoding="utf-8").write("zone_id,pour_date,pour_time,mix,fck\n")
            assert timeline(t)["dates"] == [] and dump(timeline(t)) == "range|None|None|0\n"
        finally:
            shutil.rmtree(t, ignore_errors=True)
        print("strength_timeline self-check OK")
        raise SystemExit(0)
    import argparse
    a = argparse.ArgumentParser()
    a.add_argument("folder")
    a.add_argument("--cure-min", type=float, default=5.0)
    a.add_argument("--start")
    a.add_argument("--end")
    o = a.parse_args()
    sys.stdout.write(dump(timeline(o.folder, date.fromisoformat(o.start) if o.start else None, date.fromisoformat(o.end) if o.end else None, o.cure_min)))

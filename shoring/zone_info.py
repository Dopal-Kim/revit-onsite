"""v6.1 [구역 정보] 창의 '구역 하나 모아 보기' (docs/redesign_v6.md v6.1 §3) — 데이터 폴더 CSV 만 읽고, 계산·판정은 하지 않는다.

  구역      zones.csv 한 줄: 층·층 이름·평면 면적 m²·slab_mm, shores.csv 의 이 구역 동바리 수 (모델 동바리를 애드인이 씀)
  사양      shore_spec.csv — 구역 줄이 있으면 그 줄, 없으면 층 전체 줄 (shore_input.spec_of 그대로)
  타설      pours.csv 이 구역 줄 (같은 구역이 여럿이면 뒤 줄 — field_server·evaluate 와 같음)
  공시체    cylinders.csv 이 구역 줄, 시험일 순. 기준 근거 = 현장양생(현장봉함·현장수중·현장기건)만 (engine.FIELD_CURINGS)
  센서      sensors.csv(온도)·sensor_strength.csv(강도) 중 같은 층·구역 3 m 안 (evaluate.SENSOR_MARGIN_M — 판정에 쓰는 위치 근거) — 위치 = 첫 줄
  설치 검측 install_inspection.csv 이 구역 기록 수·마지막 기록·풀리지 않은 NG 한 줄 (shore_inspect.open_ng·ng_text)
  해체 기록 shore_removed.csv 이 구역 줄 수·마지막 해체일
입력(쓰기)은 여기서 하지 않는다 — 창의 [기록]은 현장 입력 서버와 같은 검사·같은 문구 (field_server.validate · C# FieldServer.SubmitLocal).
판정 한 줄은 모델의 마지막 판정 값(SHR_Decision)을 창이 그대로 보여 준다 (계산은 evaluate.py 한 곳).
표준 라이브러리만 — C# Engine ZoneInfo.cs 와 같은 값 (Engine.Check 가 dump 글자를 비교).
실행: python zone_info.py <데이터 폴더> [구역 ID … | --all] [--at YYYY-MM-DD]   ·   자체 검사: --self-test
"""
import os
import sys
from datetime import date

import engine as e
import evaluate as ev
import shore_input as sin
import shore_inspect as si

KIND_LABEL = {"sensors.csv": ("온도", "temp_c"), "sensor_strength.csv": ("강도", "mpa")}


def _v(r, k):
    return (r.get(k) or "").strip()


def zone_info(folder, zone_id, at=None):
    """구역 하나 → dict. at(date) 를 주면 설치 검측·해체 기록은 그날까지 (공시체·타설·센서는 다 보여 줌). 구역이 zones.csv 에 없으면 ValueError."""
    zones = ev.read(folder, "zones.csv")
    z = next((r for r in zones if _v(r, "zone_id") == zone_id), None)
    if z is None:
        raise ValueError(f"구역 '{zone_id}' 이(가) zones.csv 에 없습니다")
    fl = int(z["floor"])
    x0, y0, x1, y1 = (float(z[k]) for k in ("x0", "y0", "x1", "y1"))
    on_floor = sorted(_v(r, "zone_id") for r in zones if int(r["floor"]) == fl)
    info = {"zone_id": zone_id, "floor": fl, "label": sin.floor_label(fl, on_floor), "area_m2": (x1 - x0) * (y1 - y0),
            "slab_mm": _v(z, "slab_mm"), "shores": sum(_v(r, "zone_id") == zone_id for r in ev.read(folder, sin.SHORES))}

    sp = sin.spec_of(folder, fl, zone_id)
    info["spec"] = None if sp is None else {**{k: _v(sp, k) for k in sin.SPEC_COLS}, "source": "구역" if _v(sp, "zone_id") else "층 전체"}

    pours = [r for r in ev.read(folder, "pours.csv") if _v(r, "zone_id") == zone_id]
    info["pour"] = None if not pours else {k: _v(pours[-1], k) for k in ("pour_date", "pour_time", "mix", "fck", "pour_id")}   # pour_id = W1 타설 묶음

    cyl = []
    for i, r in enumerate(ev.cyl_rows(folder)):   # W1: 타설 묶음 공통 시험도 이 구역 줄로
        if _v(r, "zone_id") != zone_id:
            continue
        fs = [_v(r, k) for k in ("f1", "f2", "f3")]
        try:
            avg = sum(float(f) for f in fs) / 3
        except ValueError:
            avg = None
        cyl.append({"test_date": _v(r, "test_date")[:10], "curing": _v(r, "curing"), "f": fs, "avg": avg,
                    "basis": "기준 근거" if _v(r, "curing") in e.FIELD_CURINGS else "현장양생 아님", "lot": r["cyl_lot"], "i": i})
    info["cylinders"] = [{k: c[k] for k in c if k != "i"} for c in sorted(cyl, key=lambda c: (c["test_date"], c["i"]))]

    sens = []
    for name, (kind, vcol) in KIND_LABEL.items():
        by = {}
        for r in ev.read(folder, name):
            if _v(r, "sensor_id"):
                by.setdefault(_v(r, "sensor_id"), []).append(r)
        for sid, rs in by.items():
            try:
                if int(rs[0]["floor"]) != fl:
                    continue
                x, y = float(rs[0]["x"]), float(rs[0]["y"])
            except (TypeError, ValueError, KeyError):   # 손으로 고친 줄 — 그 센서만 빼고
                continue
            if ev.near([(sid, x, y, None)], (x0, y0, x1, y1))[1]:   # 3 m 밖 (판정에 안 쓰는 센서)
                continue
            last = max(rs, key=lambda r: _v(r, "timestamp"))
            sens.append({"kind": kind, "sensor_id": sid, "x": _v(rs[0], "x"), "y": _v(rs[0], "y"), "inside": x0 <= x <= x1 and y0 <= y <= y1,
                         "count": len(rs), "last_ts": _v(last, "timestamp")[:16], "last_value": _v(last, vcol)})
    info["sensors"] = sorted(sens, key=lambda s: (s["kind"], s["sensor_id"]))

    recs = [r for r in si.records(folder, at) if r["zone_id"] == zone_id]
    info["install"] = {"records": len(recs), "last": f"{recs[-1]['t']:%Y-%m-%d} {recs[-1]['stage']}" if recs else "",
                       "open_ng": si.ng_text(si.open_ng(folder, at).get(zone_id, []))}

    rem = [r for r in ev.read(folder, sin.REMOVED) if _v(r, "zone_id") == zone_id and (at is None or _v(r, "removed_date")[:10] <= at.isoformat())]
    info["removed"] = {"count": len(rem), "last": max((_v(r, "removed_date")[:10] for r in rem), default="")}
    return info


def dump(info):
    """창·비교용 글자 (C# ZoneInfo.Dump 와 바이트 같음). 수는 소수 둘째 자리."""
    n = lambda v: "-" if v is None else f"{v:.2f}"
    out = [f"zone|{info['zone_id']}|{info['floor']}|{info['label']}|{n(info['area_m2'])}|{info['slab_mm']}|{info['shores']}"]
    sp = info["spec"]
    out.append("spec|-" if sp is None else "spec|" + "|".join([sp["source"]] + [sp[k] for k in sin.SPEC_COLS]))
    p = info["pour"]
    out.append("pour|-" if p is None else f"pour|{p['pour_date']}|{p['pour_time']}|{p['mix']}|{p['fck']}|{p['pour_id']}")
    out += [f"cyl|{c['test_date']}|{c['curing']}|{'|'.join(c['f'])}|{n(c['avg'])}|{c['basis']}|{c['lot']}" for c in info["cylinders"]]
    out += [f"sensor|{s['kind']}|{s['sensor_id']}|{s['x']}|{s['y']}|{'안' if s['inside'] else '3 m 안'}|{s['count']}|{s['last_ts']}|{s['last_value']}" for s in info["sensors"]]
    i = info["install"]
    out.append(f"install|{i['records']}|{i['last']}|{i['open_ng']}")
    out.append(f"removed|{info['removed']['count']}|{info['removed']['last']}")
    return "\n".join(out) + "\n"


def _self_test():
    import shutil
    import tempfile
    t = tempfile.mkdtemp(prefix="zone_info_")
    try:
        w = lambda name, text: open(os.path.join(t, name), "w", encoding="utf-8", newline="").write(text)
        w("zones.csv", "zone_id,floor,x0,y0,x1,y1,slab_mm\n1F-A1,1,0.0,0.0,10.0,9.0,210\n1F-B1,1,10.0,0.0,20.0,9.0,\n2F-A1,2,0.0,0.0,10.0,9.0,210\n")
        w("pours.csv", "zone_id,pour_date,pour_time,mix,fck\n1F-A1,2026-11-01,08:00,보통,24\n")
        w("cylinders.csv", "zone_id,test_date,curing,f1,f2,f3\n1F-A1,2026-11-08,표준양생,20,21,22\n1F-A1,2026-11-05,현장봉함,15.0,15.5,16\n1F-B1,2026-11-05,현장봉함,9,9,9\n")
        w("sensors.csv", "sensor_id,floor,x,y,timestamp,temp_c\nS1,1,5,4,2026-11-01T10:00,20\nS1,1,5,4,2026-11-02T10:00,18.5\nS2,1,12.5,4,2026-11-01T10:00,19\n"
          "S3,1,15,4,2026-11-01T10:00,19\nS4,2,5,4,2026-11-01T10:00,19\n")
        w(sin.SPEC, "floor,zone_id,type,spec,spacing_x_mm,spacing_y_mm,splices,install_date\n1,,파이프 서포트,V2,1200,1200,1,2026-10-28\n1,1F-B1,시스템 동바리,S,1500,1500,0,2026-10-29\n")
        w(si.FILE, "zone_id,item_code,result,inspector,date,photo_path,stage,shore_ids,timestamp,source\n1F-A1,SHR-S,NG,김,2026-10-30,,설치 검측,,2026-10-30T09:00,현장 점검\n"
          "1F-A1,SHR-B,NG,김,2026-10-30,,설치 검측,1F-A1-R01C01,2026-10-30T09:00,현장 점검\n1F-A1,SHR-S,OK,김,2026-11-03,,설치 검측,,2026-11-03T09:00,현장 점검\n")
        w(sin.REMOVED, "id,zone_id,x,y,removed_date,by,note\n1F-A1-R01C01,1F-A1,1.0,1.0,2026-11-20,김,구역 해체 (Revit)\n")
        a = zone_info(t, "1F-A1")
        assert a["label"] == "1F" and abs(a["area_m2"] - 90) < 1e-9 and a["slab_mm"] == "210" and a["shores"] == 0, a
        assert a["spec"]["source"] == "층 전체" and a["spec"]["spec"] == "V2", a["spec"]                  # 구역 줄 없음 → 층 줄
        assert zone_info(t, "1F-B1")["spec"]["source"] == "구역"                                          # 구역 줄 우선
        assert a["pour"] == {"pour_date": "2026-11-01", "pour_time": "08:00", "mix": "보통", "fck": "24", "pour_id": ""}   # pour_id 열 없음 → 빈칸 (W1)
        assert [c["test_date"] for c in a["cylinders"]] == ["2026-11-05", "2026-11-08"]                    # 시험일 순
        assert [c["basis"] for c in a["cylinders"]] == ["기준 근거", "현장양생 아님"] and abs(a["cylinders"][0]["avg"] - 15.5) < 1e-9
        assert [s["sensor_id"] for s in a["sensors"]] == ["S1", "S2"], a["sensors"]                          # S2 = 경계 밖 2.5 m (3 m 안), S3 5 m 밖, S4 다른 층
        assert a["sensors"][0]["inside"] and not a["sensors"][1]["inside"] and a["sensors"][0]["last_value"] == "18.5" and a["sensors"][0]["count"] == 2
        assert a["install"]["records"] == 3 and a["install"]["open_ng"] == "수평연결재 1F-A1-R01C01", a["install"]   # 이음 NG 는 11-03 OK 로 풀림
        assert zone_info(t, "1F-A1", date(2026, 11, 1))["install"]["open_ng"] == "수평연결재 1F-A1-R01C01 · 이음 (구역 전체)"   # 그날까지면 이음도 남음 (같은 시각은 항목 코드 순)
        assert a["removed"] == {"count": 1, "last": "2026-11-20"} and zone_info(t, "1F-A1", date(2026, 11, 8))["removed"]["count"] == 0
        b = zone_info(t, "2F-A1")
        assert b["spec"] is None and b["pour"] is None and b["cylinders"] == [] and [s["sensor_id"] for s in b["sensors"]] == ["S4"]
        assert dump(b).splitlines()[:3] == ["zone|2F-A1|2|2F|90.00|210|0", "spec|-", "pour|-"], dump(b)
        try:
            zone_info(t, "9F-Z9")
            raise AssertionError("없는 구역")
        except ValueError:
            pass
    finally:
        shutil.rmtree(t)
    print("zone_info 자체 검사 통과")


if __name__ == "__main__":
    args = sys.argv[1:]
    if "--self-test" in args:
        _self_test()
        sys.exit(0)
    at = None
    if "--at" in args:
        k = args.index("--at")
        at = date.fromisoformat(args[k + 1])
        del args[k:k + 2]
    folder, ids = args[0], args[1:]
    if ids == ["--all"] or not ids:
        ids = [_v(r, "zone_id") for r in ev.read(folder, "zones.csv")]
    sys.stdout.reconfigure(encoding="utf-8")
    for zid in ids:
        sys.stdout.write(dump(zone_info(folder, zid, at)))

"""파장 지표 (docs/impact_map.md 1절 '지표' 열을 데이터 폴더 기록에서 바로 잼) — 적합성 검사 ⓐ 의 "잴 수 있는 것".

새 판단은 없다: 이미 있는 기록(approvals·shore_checks·evidence·alerts·판정표)을 세기만 한다.
지표:
  review_hours      요청 제출 → 감리 승인 기록(서명 일시가 있으면 그 시각) 걸린 시간 — 구역별·중앙값 (감리 확인 시간, impact_map #5)
  shore_check_rate  다음 층 타설 전 확인 대상 구역 중 최근 24 h '예' 확인이 있는 비율 (#2)
  removal_suspect   무단 해체 의심 구역 수 (#3)
  evidence_full     요청 가능 구역의 근거 원장 평균 단계 n/8 (#4)
  assumed_share     판정 근거에 [가정] 이 들어간 구역 비율 — 낮을수록 모델·실측 근거 (#12·#13)
실행: python kpi.py <데이터 폴더> --at 2026-12-31T09:00   ·   자체 검사: --self-test
"""
import argparse
import os
import statistics
from datetime import datetime

import alerts as al
import approval as ap
import evaluate as ev
import evidence
import pour_gate as pg


def _t(s):
    s = (s or "").strip()
    return datetime.fromisoformat(s if "T" in s or len(s) > 10 else s + "T23:59") if s else None   # 날짜만 = 그날 23:59 (보수 측, signature_watch 와 같음)


def kpis(folder, at):
    rows = ev.evaluate(folder, at)
    day = at.date()
    ok, _ = ap.verify(folder)
    log = [r for r in ap.read_log(folder) if r["timestamp"][:10] <= day.isoformat()] if ok else []
    req = {}
    hours = {}
    for r in log:
        if r["action"] == "요청 제출":
            req.setdefault(r["zone_id"], _t(r["timestamp"]))
        elif r["action"] == "감리 승인 기록" and r["zone_id"] in req and r["zone_id"] not in hours:
            done = _t(r.get("signed_at")) or _t(r["timestamp"])
            if done >= req[r["zone_id"]]:                                   # 91차: 서명 시각이 요청보다 앞(기록 시각 고정 등)이면 지표에서 뺌
                hours[r["zone_id"]] = round((done - req[r["zone_id"]]).total_seconds() / 3600, 1)
    k = al.alerts(folder, at)
    judged = {r["zone_id"] for r in rows}
    top = max((r["floor"] for r in rows), default=None)
    zones = ev.read(folder, "zones.csv")
    nxt = min((int(z["floor"]) for z in zones if z["zone_id"] not in judged and top is not None and int(z["floor"]) > top), default=None)
    checks = [c for z in zones if nxt is not None and int(z["floor"]) == nxt for c in pg.pour_gate(folder, z["zone_id"], at)["checks"]]
    seen = {c["zone_id"]: c["ok"] for c in checks}                       # 같은 아래층 구역이 여러 번 나오면 한 번만
    led = evidence.ledger_at(folder, day)
    req_zones = [r["zone_id"] for r in rows if r["decision"] == "해체 승인 요청 가능"]
    key = ev.floor_order(folder)                                       # 구역별 시간은 층 순 → 구역 ID (총괄 11-05)
    hours = {z: hours[z] for z in sorted(hours, key=key)}
    return {
        "review_hours": {"zones": hours, "median": statistics.median(hours.values()) if hours else None},
        "shore_check_rate": {"checked": sum(seen.values()), "of": len(seen)},
        "removal_suspect": sum(x["kind"] == "무단 해체 의심" for x in k),
        "evidence_full": round(sum(led.get(z, {}).get("stage", 0) for z in req_zones) / len(req_zones), 1) if req_zones else None,
        "assumed_share": {"assumed": sum("[가정]" in (r["est_source"] + r["reason"]) for r in rows), "of": len(rows)},
    }


def lines(k):
    rh, sc, a, ef = k["review_hours"], k["shore_check_rate"], k["assumed_share"], k["evidence_full"]
    review = "기록 없음" if rh["median"] is None else (
        f"중앙값 {rh['median']:g} h · " + " · ".join(f"{z} {h:g} h" for z, h in rh["zones"].items()))
    return [f"감리 확인 시간 (요청 → 감리 승인): {review}",
            f"타설 전 동바리 확인: {sc['checked']}/{sc['of']} 구역 (최근 24 h '예')",
            f"무단 해체 의심: {k['removal_suspect']} 구역",
            f"요청 가능 구역 근거 원장 평균: {'—' if ef is None else f'{ef:g}/8'}",
            f"판정 근거에 [가정] 이 든 구역: {a['assumed']}/{a['of']}"]


if __name__ == "__main__":
    import sys
    [s.reconfigure(errors="replace") for s in (sys.stdout, sys.stderr) if hasattr(s, "reconfigure")]
    p = argparse.ArgumentParser()
    p.add_argument("folder", nargs="?")
    p.add_argument("--at")
    p.add_argument("--self-test", action="store_true")
    o = p.parse_args()
    if o.self_test:
        import shutil
        import tempfile
        sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tools"))
        import make_demo_data as md
        t = tempfile.mkdtemp()
        try:
            md.make(t)
            at = datetime(2026, 12, 31, 9)
            k0 = kpis(t, at)
            assert k0["review_hours"]["median"] is None and k0["removal_suspect"] == 0 and k0["shore_check_rate"]["of"] == 0   # 13F 구역 없음 → 대상 0
            zs = ev.read(t, "zones.csv")
            with open(os.path.join(t, "zones.csv"), "a", encoding="utf-8") as f:
                f.write("".join(f"13F-{z['zone_id'].split('-', 1)[1]},13,{z['x0']},{z['y0']},{z['x1']},{z['y1']}\n" for z in zs if z["floor"] == "12"))
            with open(os.path.join(t, "shore_checks.csv"), "w", encoding="utf-8") as f:
                f.write("zone_id,timestamp,present,actor,photo\n" + "".join(f"{z},2026-12-31T08:30,예,시험,\n" for z in ("12F-B1", "11F-B1", "10F-B1"))
                        + "9F-A2,2026-12-31T08:40,아니오,시험,\n")
            b1 = next(r for r in ev.evaluate(t, at) if r["zone_id"] == "9F-B1")
            ap.record(t, "9F-B1", "요청 제출", "시험", b1, now=datetime(2026, 12, 30, 9))
            ap.record(t, "9F-B1", "감리 승인 기록", "시험", b1, now=at, approver="감리", signed_at="2026-12-31 15:00")
            k = kpis(t, at)
            assert k["review_hours"]["zones"] == {"9F-B1": 30.0}, k["review_hours"]          # 12-30 09:00 → 12-31 15:00 서명
            assert k["removal_suspect"] == 1 and k["shore_check_rate"]["checked"] == 3 and k["shore_check_rate"]["of"] >= 3
            assert k["assumed_share"]["of"] == 24 and k["evidence_full"] is not None
            assert len(lines(k)) == 5 and "30 h" in lines(k)[0]
        finally:
            shutil.rmtree(t, ignore_errors=True)
        print("kpi self-check OK")
    else:
        if not (o.folder and o.at):
            p.error("데이터 폴더와 --at (또는 --self-test)")
        print("\n".join(lines(kpis(o.folder, datetime.fromisoformat(o.at)))))

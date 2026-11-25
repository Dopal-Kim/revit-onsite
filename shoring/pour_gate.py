"""타설 전 게이트 — 다음 층 타설 요청 전에 아래 지지층 동바리가 실제로 서 있는지 (docs/field_process_research.md 제안 1순위).

화정 2022 사조위 [확정]: 결정적 단계는 해체 승인이 아니라 "하부 3개층 동바리가 이미 빠진 상태에서 상층 타설". 현장 기록은
원청·감리만 남기고 해체는 협력업체가 하므로, 기록이 없으면 동바리가 '있는' 것으로 보이는 거짓 안전이 생긴다.
그래서 이 게이트는 '없다는 기록이 없음'이 아니라 '있다는 최근 확인'을 요구한다.

입력 (데이터 폴더):
  zones.csv, pours.csv            evaluate 와 같음
  shore_checks.csv                zone_id, timestamp, present(예/아니오), actor, photo   — 층 QR 로 안전관리자가 매일 확인 [제안]
  approvals.csv                   approval.py — "해체 완료"(7) 는 동바리 없음, "재받침"(8) 은 다시 있음
결론: "타설 요청 가능" / "타설 보류" (+ 사유). 프로그램은 타설을 승인하거나 지시하지 않는다.
실행: python pour_gate.py  (자체 검사)
"""
import os
from datetime import datetime, timedelta

import approval as ap
import engine as e
import evaluate as ev
import shore_inspect as si

CHECK_FRESH_H = 24.0   # [가정] 이 시간 안의 '있음' 확인만 인정 (매일 확인 운영 기준)


def pour_gate(folder, zone_id, at, n_levels=e.MIN_LEVELS):
    """zone_id = 지금 타설하려는 구역. 그 아래로 평면이 겹치는 n_levels 개 층(층 순서)의 구역마다 동바리 확인."""
    zones = {r["zone_id"]: r for r in ev.read(folder, "zones.csv")}
    if zone_id not in zones:
        raise SystemExit(f"zones.csv 에 {zone_id} 없음")
    floor_of = {z: int(r["floor"]) for z, r in zones.items()}
    rect_of = {z: tuple(float(r[k]) for k in ("x0", "y0", "x1", "y1")) for z, r in zones.items()}
    order = sorted(set(floor_of.values()))
    below = [f for f in order if f < floor_of[zone_id]][::-1][:n_levels]          # 바로 아래층부터
    status = ap.status_override(folder, at)                                       # 7 해체 완료 · 8 재받침 (판정일까지의 기록)
    last = {}
    for r in ev.read(folder, "shore_checks.csv"):
        t = ev.ts(r["timestamp"])
        if t <= at and (r["zone_id"] not in last or t >= last[r["zone_id"]][0]):
            last[r["zone_id"]] = (t, r.get("present", "").strip(), r.get("actor", ""))
    rows, hold = [], []
    ng = si.open_ng(folder, at, si.INSTALL).get(zone_id)    # F16 ⑦: 타설하려는 구역의 설치 검측 NG 가 풀리지 않으면 보류
    if ng:
        hold.append(f"{zone_id}: 설치 검측 결함 기록 미해결 — {si.ng_text(ng)}")
    for f in below:
        for z in sorted(z for z in zones if floor_of[z] == f and ev.overlaps(rect_of[z], rect_of[zone_id])):
            c = last.get(z)
            if status.get(z) == 7:
                msg = "해체 완료 기록 있음 — 재받침 기록 없이는 지지 안 됨"
            elif c and c[1] == "아니오":
                msg = f"{c[0]:%m-%d %H:%M} {c[2]} 확인: 동바리 없음"
            elif not c or (at - c[0]).total_seconds() / 3600 > CHECK_FRESH_H:
                msg = "최근 24시간 동바리 확인 없음" + (f" (마지막 {c[0]:%m-%d %H:%M})" if c else "")
            else:
                msg = ""
            rows.append({"floor": f, "zone_id": z, "ok": not msg, "reason": msg or f"{c[0]:%m-%d %H:%M} {c[2]} 확인: 동바리 있음"})
            if msg:
                hold.append(f"{z}: {msg}")
    if len(below) < n_levels and below and below[-1] != order[0]:
        hold.append(f"아래 층 수 {len(below)} < {n_levels}")
    decision = "타설 보류" if hold else "타설 요청 가능"
    return {"zone_id": zone_id, "decision": decision, "reason": "; ".join(hold) or (f"아래 {len(below)}개층 동바리 최근 확인 있음" if below else "맨 아래층 — 확인할 아래 동바리 없음"),
            "checks": rows}


if __name__ == "__main__":
    import shutil
    import tempfile
    t = tempfile.mkdtemp()
    try:
        box = "0,0,10,10"
        open(os.path.join(t, "zones.csv"), "w", encoding="utf-8").write(
            "zone_id,floor,x0,y0,x1,y1\n" + "".join(f"{k}F-A,{k},{box}\n" for k in range(1, 6)))
        at = datetime(2026, 6, 1, 9)
        def checks(rows):
            open(os.path.join(t, "shore_checks.csv"), "w", encoding="utf-8").write(
                "zone_id,timestamp,present,actor,photo\n" + "".join(",".join(r) + "\n" for r in rows))
        ok = [(f"{k}F-A", "2026-06-01T07:30", "예", "박안전", f"{k}F.jpg") for k in (2, 3, 4)]
        checks(ok)
        g = pour_gate(t, "5F-A", at)
        assert g["decision"] == "타설 요청 가능" and [c["floor"] for c in g["checks"]] == [4, 3, 2], g
        checks(ok[:2] + [("2F-A", "2026-05-30T07:30", "예", "박안전", "2F.jpg")])           # 2F 확인이 오래됨
        assert "최근 24시간" in pour_gate(t, "5F-A", at)["reason"]
        checks(ok + [("3F-A", "2026-06-01T08:10", "아니오", "박안전", "3F.jpg")])          # 화정형: 아래층 동바리 빠짐
        g = pour_gate(t, "5F-A", at)
        assert g["decision"] == "타설 보류" and "3F-A" in g["reason"] and "동바리 없음" in g["reason"], g
        checks(ok)
        row = {"zone_id": "4F-A", "decision": "해체 승인 요청 가능"}
        ap.record(t, "4F-A", "감리 승인 기록", "이감리", row, now=datetime(2026, 5, 31, 10))
        ap.record(t, "4F-A", "해체 완료", "김현장", row, now=datetime(2026, 5, 31, 16))      # 확인보다 해체 기록이 우선
        assert "해체 완료 기록" in pour_gate(t, "5F-A", at)["reason"]
        g = pour_gate(t, "2F-A", at)                                                         # 아래가 1층뿐 → 그 층만, 확인 없으면 보류
        assert g["decision"] == "타설 보류" and [c["zone_id"] for c in g["checks"]] == ["1F-A"], g
        checks(ok + [("1F-A", "2026-06-01T07:30", "예", "박안전", "1F.jpg")])
        assert pour_gate(t, "2F-A", at)["decision"] == "타설 요청 가능"
        assert pour_gate(t, "1F-A", at)["reason"] == "맨 아래층 — 확인할 아래 동바리 없음"
    finally:
        shutil.rmtree(t)
    print("pour_gate self-check OK")

"""동바리 상태 (V2 동바리 보기 — docs/shore_view_spec.md §V2, 총괄 11-05): 구역마다 '기록 기준' 동바리 상태, 판정 시각 기준.

기존 함수 조합만 — 판정 evaluate · 승인 기록 approval.status_override(6 감리 확인 / 7 해체 완료 / 8 재받침)
· 무단 해체 의심 evidence.removal_watch · 타설 전 아래층 24 h 확인 pour_gate (다음 타설 층 = alerts 와 같은 규칙)
· 설치 검측 install_inspection.csv (evidence.entries 와 같은 규칙: 한 항목이라도 OK 아니면 NG) · 협력업체 해체 신고 '뺐음' shore_checks.csv.
결과 = 구역마다 채움 상태 하나 + 테두리(설치 검측 필요/NG) + 점선(최근 24 h 확인 없음). 화면 표시용이며 판정·결론은 바꾸지 않는다.
표준 라이브러리만 — C# ShoreStatus.cs 와 바이트 동일 (Engine.Check). 실행: python shore_status.py 폴더 --at 2026-12-31T09:00 / --self-test
"""
import argparse
import os
import sys
from datetime import datetime

import approval as ap
import evaluate as ev
import evidence
import pour_gate as pg
import shore_inspect as si

READY_DECISION = "해체 승인 요청 가능"
# 채움 상태 — 우선순위: 무단 해체 의심 > 해체됨 > 감리 확인 > 해체 승인 요청 가능 > 서 있어야 함 (존치·공시체 시험 필요·타설 전)
SUSPECT, REMOVED, APPROVED, READY, STANDING = "무단 해체 의심", "해체됨", "감리 확인", "해체 승인 요청 가능", "서 있어야 함"
INSTALL_NEED, INSTALL_NG, STALE = "설치 검측 필요", "설치 검측 NG", "최근 24 h 확인 없음"
SHORE_NG = "NG 동바리"                                             # F16: 풀리지 않은 점검 NG 가 있는 동바리 (모든 단계) — Revit 동바리 빨강 테두리
# 범례 칩 = (이름, (R, G, B), 그리기) — 색 [가정] (명세 §V2 표: 진회색·노랑·초록·윤곽·빨강 깜빡임·주황/빨강 테두리·점선)
LEGEND = [(STANDING, (90, 90, 90), "채움"), (READY, (235, 190, 0), "채움"), (APPROVED, (30, 150, 60), "채움"),
          (REMOVED, (170, 170, 170), "윤곽"), (SUSPECT, (215, 25, 25), "깜빡임"),
          (INSTALL_NEED, (240, 130, 0), "테두리"), (INSTALL_NG, (215, 25, 25), "테두리"), (STALE, (40, 40, 40), "점선"),
          (SHORE_NG, (215, 25, 25), "테두리")]


def status(folder, at, cure_min=5.0):
    """→ [{zone_id, floor, state, install, stale, reason}] zones.csv 의 모든 구역, 층 순 → 구역 ID 순.
    state = 채움 상태, install = "" / 설치 검측 필요 / 설치 검측 NG (해체됨이면 ""), stale = "" 또는 '최근 24 h 확인 없음' 사유, reason = state 근거."""
    day = at.date().isoformat()
    zones = {r["zone_id"]: int(r["floor"]) for r in ev.read(folder, "zones.csv")}
    rows = ev.evaluate(folder, at, cure_min)
    dec = {r["zone_id"]: r["decision"] for r in rows}
    code = ap.status_override(folder, at)                          # 판정일까지의 마지막 6·7·8
    watch = evidence.removal_watch(folder, at.date())
    last = {}                                                      # 구역 → 마지막 해체/재받침 사건 (시각, 종류) — 해체 완료 7 · 재받침 8 · 신고 '뺐음'
    for r in ap.read_log(folder):
        k = ap.ACTIONS.get(r["action"])
        if k in (7, 8) and r["timestamp"][:10] <= day:
            t = datetime.fromisoformat(r["timestamp"])
            if r["zone_id"] not in last or t >= last[r["zone_id"]][0]:
                last[r["zone_id"]] = (t, k)
    for r in ev.read(folder, "shore_checks.csv"):
        if (r.get("source") or "").strip() == evidence.REPORT_SOURCE and (r.get("present") or "").strip() == "아니오":   # 짧은 줄 = None (C# V() 와 같음)
            t = ev.ts(r["timestamp"])
            if t.date().isoformat() <= day and (r["zone_id"] not in last or t >= last[r["zone_id"]][0]):
                last[r["zone_id"]] = (t, "신고")
    ins_ng = si.open_ng(folder, at, si.INSTALL)                    # F16: 판정 시각까지 풀리지 않은 설치 NG (다시 점검해 OK 면 풀림 — 규칙 ⑦ 과 같음)
    ins = {r["zone_id"]: r["zone_id"] in ins_ng for r in si.records(folder, at) if r["stage"] == si.INSTALL}   # 구역 → NG 있음
    top = max((r["floor"] for r in rows), default=None)            # 다음 타설 층 (alerts 와 같음) 아래 확인 대상의 '최근 24시간 확인 없음'
    nxt = min((f for z, f in zones.items() if z not in dec and top is not None and f > top), default=None)
    stale = {}
    for z in sorted(z for z, f in zones.items() if f == nxt):
        for c in pg.pour_gate(folder, z, at)["checks"]:
            if not c["ok"] and c["reason"].startswith("최근 24시간"):
                stale.setdefault(c["zone_id"], f"{z} 타설 전 확인 대상 — {c['reason']}")
    out = []
    for zid in sorted(zones, key=ev.floor_order(folder)):
        e = last.get(zid)
        if zid in watch:
            state, reason = SUSPECT, watch[zid]
        elif e and e[1] != 8:
            state, reason = REMOVED, ("해체 완료 기록" if e[1] == 7 else "협력업체 해체 신고") + f" {e[0]:%m-%d %H:%M}"
        elif code.get(zid) == 6:
            state, reason = APPROVED, "감리 승인 기록"
        elif dec.get(zid) == READY_DECISION:
            state, reason = READY, READY_DECISION
        else:
            state, reason = STANDING, dec.get(zid, "타설 전") + (" · 재받침 기록" if code.get(zid) == 8 else "")
        install = "" if state == REMOVED else INSTALL_NEED if zid not in ins else INSTALL_NG if ins[zid] else ""
        out.append({"zone_id": zid, "floor": zones[zid], "state": state, "install": install, "stale": stale.get(zid, ""), "reason": reason})
    return out


def dump(rows):
    """일치 시험용 글자 (탭 구분, 줄마다 \\n)."""
    return "".join(f"{r['zone_id']}\t{r['floor']}\t{r['state']}\t{r['install']}\t{r['stale']}\t{r['reason']}\n" for r in rows)


if __name__ == "__main__":
    [s.reconfigure(errors="replace") for s in (sys.stdout, sys.stderr) if hasattr(s, "reconfigure")]
    p = argparse.ArgumentParser()
    p.add_argument("folder", nargs="?")
    p.add_argument("--at")
    p.add_argument("--out")
    p.add_argument("--self-test", action="store_true")
    a = p.parse_args()
    if not a.self_test:
        text = dump(status(a.folder, datetime.fromisoformat(a.at)))
        if a.out:
            open(a.out, "w", encoding="utf-8", newline="").write(text)
        else:
            print(text, end="")
        sys.exit(0)
    import shutil
    import tempfile
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tools"))
    import make_demo_data as md
    t = tempfile.mkdtemp()
    try:
        md.make(t)                                                   # 시연 자료 (9–12F 설치 검측 OK) + 모델처럼 13F 구역 (타설 전)
        zs = ev.read(t, "zones.csv")
        with open(os.path.join(t, "zones.csv"), "a", encoding="utf-8") as f:
            f.write("".join(f"13F-{z['zone_id'].split('-', 1)[1]},13,{z['x0']},{z['y0']},{z['x1']},{z['y1']}\n" for z in zs if z["floor"] == "12"))
        at = datetime(2026, 12, 31, 9)
        s = {r["zone_id"]: r for r in status(t, at)}
        assert [r["zone_id"] for r in status(t, at)][:2] == ["9F-A1", "9F-A2"] and len(s) == 30
        assert {z for z, r in s.items() if r["state"] == READY} == {"9F-A1", "9F-B1", "9F-C1", "9F-C2"}
        assert s["9F-A2"]["state"] == STANDING and s["9F-A2"]["reason"].startswith("존치") and s["9F-A2"]["install"] == ""
        assert s["13F-A1"] == {"zone_id": "13F-A1", "floor": 13, "state": STANDING, "install": INSTALL_NEED, "stale": "", "reason": "타설 전"}
        assert s["12F-A1"]["stale"].startswith("13F-A1 타설 전 확인 대상 — 최근 24시간") and s["9F-A1"]["stale"] == ""
        rows9 = {r["zone_id"]: r for r in ev.evaluate(t, at)}
        ap.record(t, "9F-B1", "감리 승인 기록", "공무", rows9["9F-B1"], now=datetime(2026, 12, 30, 15))
        ap.record(t, "9F-C1", "감리 승인 기록", "공무", rows9["9F-C1"], now=datetime(2026, 12, 30, 15))
        ap.record(t, "9F-C1", "해체 완료", "공무", rows9["9F-C1"], now=datetime(2026, 12, 30, 16))
        ap.record(t, "9F-C2", "감리 승인 기록", "공무", rows9["9F-C2"], now=datetime(2026, 12, 30, 15))
        with open(os.path.join(t, "shore_checks.csv"), "w", encoding="utf-8") as f:
            f.write("zone_id,timestamp,present,actor,photo,source\n" + "".join(f"{z},2026-12-31T08:30,예,안전,,\n" for z in ("12F-A1", "11F-A1", "10F-A1"))
                    + "9F-A2,2026-12-31T08:40,아니오,안전,,\n" + f"9F-C2,2026-12-30T17:00,아니오,반장,,{evidence.REPORT_SOURCE}\n")
        with open(os.path.join(t, "install_inspection.csv"), "a", encoding="utf-8") as f:
            f.write("10F-A2,SHR-S,NG,검측,2026-12-01,\n13F-B1,SHR-H,OK,검측,2027-01-02,\n")   # 13F-B1 기록은 판정일 뒤 → 아직 필요
        s = {r["zone_id"]: r for r in status(t, at)}
        assert s["9F-A2"]["state"] == SUSPECT and "승인 기록 없이 동바리 없음" in s["9F-A2"]["reason"]
        assert s["9F-B1"]["state"] == APPROVED and s["9F-C1"] == {**s["9F-C1"], "state": REMOVED, "install": "", "reason": "해체 완료 기록 12-30 16:00"}
        assert s["9F-C2"]["state"] == REMOVED and s["9F-C2"]["reason"] == "협력업체 해체 신고 12-30 17:00"
        assert s["10F-A2"]["install"] == INSTALL_NG and s["13F-B1"]["install"] == INSTALL_NEED
        assert s["12F-A1"]["stale"] == "" and s["12F-B1"]["stale"]               # 08:30 '예' 확인한 줄만 풀림
        ap.record(t, "9F-C1", "재받침", "공무", rows9["9F-C1"], now=datetime(2026, 12, 30, 18))
        s = {r["zone_id"]: r for r in status(t, at)}
        assert s["9F-C1"]["state"] == READY                                         # 재받침 뒤 = 판정대로 (요청 가능)
        assert {r["zone_id"]: r for r in status(t, datetime(2026, 12, 30, 9))}["9F-A2"]["state"] != SUSPECT   # 12-30 판정엔 12-31 관측이 안 들어감
        assert [n for n, _, _ in LEGEND] == [STANDING, READY, APPROVED, REMOVED, SUSPECT, INSTALL_NEED, INSTALL_NG, STALE, SHORE_NG]
        assert dump(status(t, at)).count("\n") == 30
    finally:
        shutil.rmtree(t, ignore_errors=True)
    print("shore_status self-check OK")

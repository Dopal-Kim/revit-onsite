"""오늘 볼 것 (알림) — 휴대폰 맨 위 배지·대시보드용 (docs/direction_log.md 3판 D4, direction_review 14: 공시체 시험 예정일·상태 변화를 놓침).

판정·결론은 바꾸지 않고, 이미 있는 계산(evaluate · pour_gate · evidence.removal_watch)을 '사람이 지금 할 일' 순서로 모은다.
종류 (위에서부터 급한 순서):
  1 무단 해체 의심   evidence.removal_watch — 승인 기록 없이 동바리 없음 관측 (화정형)
  2 타설 보류        다음 층(판정된 최상층 바로 위) 구역의 pour_gate 가 보류 — 아래층 동바리 확인이 필요
  · 순찰 확인 없음    동바리가 서 있는 판정 구역에 patrol_hours(options.json, 기본 24 [가정]) 안 동바리 확인이 없음 (협력업체 신고는 안 셈, v6.2 W9)
  · 감리 확인 못 넣음  서버 감리 직접 확인(S3)을 동기화 때 Revit 검사에서 넣지 못함 (approval.supervise_rejects, v6.2 W-A) — 이유 그대로
  3 공시체 미입력 (예측일 지남) 공시체 결과 없음 + 예측 도달일(predicted_due)이 판정일 이하 — 공시체를 깨 볼 때
  4 상태 바뀜        prev_at 판정 대비 결론이 바뀐 구역 (prev_at 을 줄 때만)
  5 입회 없음        quality.watch_missing — 판정일까지 타설했는데 pours.csv 입회 기록(supervisor_present·watcher)이 빈 구역 (열이 없으면 안 셈, F5)
실행: python alerts.py <데이터 폴더> --at 2026-12-31T09:00 [--prev 2026-12-30T09:00]   ·   자체 검사: python alerts.py --self-test
"""
import argparse
import csv
import os
from datetime import datetime

import approval
import evaluate as ev
import evidence
import pour_gate as pg
import quality
import shore_input as sin
import shore_status as ss

ORDER = ("무단 해체 의심", "타설 보류", "순찰 확인 없음", "감리 확인 못 넣음", "공시체 미입력 (예측일 지남)", "상태 바뀜", "입회 없음")
PATROL_H = 24      # [가정] 순찰 확인 간격 (시간) — options.json patrol_hours 로 바꿈 (v6.2 W9)
EVAL_LOG = "eval_log.csv"   # 판정 이력 — 애드인 [판정] 이 한 줄씩 추가만. '상태 바뀜' 비교 기준 + 근거 원장 보조 자료 (총괄 11-05)
EVAL_FIELDS = ["eval_at", "logged_at", "summary"]


def eval_summary(rows, folder=None, at=None):
    """판정 행 → 요약 한 줄 (판정 결론 기준). folder 를 주면 사람 기록(6·7·8) 구역은 빼고 '이미 기록 n' 으로 — 대시보드 칩·요청서와 같은 수 (11-12, approval.zone_counts)."""
    left = approval.request_rows(rows, folder, at) if folder else rows
    done, rows = len(rows) - len(left), left
    n = lambda f: sum(1 for r in rows if f(r["decision"]))   # noqa: E731
    d2 = n(lambda d: d == "존치 (설치 결함 기록)")          # F16 규칙 ⑦ (상태 2) — 존치에 들어 있고 몇 개인지 따로 (37차)
    return (f"해체 승인 요청 가능 {n(lambda d: d == '해체 승인 요청 가능')} · 존치 {n(lambda d: d.startswith('존치'))}"
            + (f" (설치 결함 {d2})" if d2 else "") + f" · 공시체 시험 필요 {n(lambda d: d == '공시체 시험 필요')}"
            + (f" · 이미 기록 {done} (감리 승인·해체 완료·재받침)" if done else ""))


def log_eval(folder, at, summary, now=None):
    """판정 한 번 → eval_log.csv 한 줄 추가 (고치지 않고 추가만). 반환 = 쓴 줄."""
    p = os.path.join(folder, EVAL_LOG)
    r = {"eval_at": at.strftime("%Y-%m-%dT%H:%M"), "logged_at": (now or datetime.now()).strftime("%Y-%m-%dT%H:%M:%S"), "summary": summary}
    new = not os.path.exists(p)
    with open(p, "a", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=EVAL_FIELDS)
        if new:
            w.writeheader()
        w.writerow(r)
    return r


def prev_eval(folder, at):
    """eval_log.csv 에서 at 과 다른 판정 시각 중 마지막 줄 → datetime (없으면 None) — '상태 바뀜' 비교 기준."""
    me = at.strftime("%Y-%m-%dT%H:%M")
    rows = [r["eval_at"] for r in ev.read(folder, EVAL_LOG) if r.get("eval_at") and r["eval_at"] != me]
    return datetime.fromisoformat(rows[-1]) if rows else None


def patrol_missing(folder, at, rows=None, cure_min=5.0):
    """W9 → {zone_id: 마지막 동바리 확인 시각 또는 None}: 판정 구역 중 동바리 '해체됨'(shore_status) 이 아닌 곳에 판정 시각 전 patrol_hours 안 동바리 확인 줄이 없음.
    확인 = shore_checks.csv 줄 (서 있음 예/아니오 모두 — 순찰했다는 기록), 협력업체 신고는 뺌. 판정 시각 뒤 줄은 안 셈"""
    h = ev.num_option(folder, "patrol_hours", PATROL_H)
    rows = ev.evaluate(folder, at, cure_min) if rows is None else rows
    gone = {s["zone_id"] for s in ss.status(folder, at, cure_min) if s["state"] == ss.REMOVED}
    last = {}
    for r in ev.read(folder, "shore_checks.csv"):
        if (r.get("source") or "").strip() == evidence.REPORT_SOURCE or not (r.get("timestamp") or "").strip():
            continue
        t = ev.ts(r["timestamp"])
        z = (r.get("zone_id") or "").strip()
        if t <= at and (z not in last or t > last[z]):
            last[z] = t
    out = {}
    for r in rows:
        z = r["zone_id"]
        if z in gone or (z in last and (at - last[z]).total_seconds() <= h * 3600):
            continue
        out[z] = last.get(z)
    return out


def patrol_floors(folder, at, rows=None, cure_min=5.0):
    """W9 오늘 볼 것 → [(첫 구역 ID, 문구)] 층마다 한 줄 (구역마다 띄우면 무단 해체 의심 같은 경고가 묻힘 — 총괄 11-09). 펼치면(문구 끝) 구역 목록.
    '마지막 확인' = 그 층 빠진 구역 중 가장 최근 확인 (없으면 '확인 기록 없음')"""
    h = ev.num_option(folder, "patrol_hours", PATROL_H)
    miss = patrol_missing(folder, at, rows, cure_min)
    floor_of = {r["zone_id"]: int(r["floor"]) for r in ev.read(folder, "zones.csv")}
    by = {}
    for z in sorted(miss, key=ev.floor_order(folder)):
        by.setdefault(floor_of[z], []).append(z)
    out = []
    for fl, zs in by.items():
        lasts = [miss[z] for z in zs if miss[z] is not None]
        since = f"마지막 확인 {(at - max(lasts)).total_seconds() / 3600:.0f}시간 전" if lasts else "확인 기록 없음"
        name = sin.floor_label(fl, sorted(z for z, f in floor_of.items() if f == fl))
        out.append((zs[0], f"{name} 순찰 확인 없음 {len(zs)}구역 (기준 {h:g}시간 [가정] · {since}) — {', '.join(zs)} (순찰 때 휴대폰 [동바리 확인])"))
    return out


def alerts(folder, at, prev_at=None, cure_min=5.0):
    """→ [{kind, zone_id, text}] ORDER 순, 같은 종류 안에서는 층 순 → 구역 ID 순."""
    rows = ev.evaluate(folder, at, cure_min)
    out = [{"kind": "무단 해체 의심", "zone_id": z, "text": t} for z, t in evidence.removal_watch(folder, at.date()).items()]
    judged = {r["zone_id"] for r in rows}
    top = max((r["floor"] for r in rows), default=None)
    zones = ev.read(folder, "zones.csv")
    nxt = min((int(z["floor"]) for z in zones if z["zone_id"] not in judged and top is not None and int(z["floor"]) > top), default=None)
    for z in sorted(z["zone_id"] for z in zones if nxt is not None and int(z["floor"]) == nxt):
        g = pg.pour_gate(folder, z, at)
        if g["decision"] == "타설 보류":
            out.append({"kind": "타설 보류", "zone_id": z, "text": f"{z} 타설 전 — {g['reason']}"})
    out += [{"kind": "순찰 확인 없음", "zone_id": z, "text": x} for z, x in patrol_floors(folder, at, rows, cure_min)]   # W9 층 단위 한 줄
    for z, (ts, name, why) in approval.supervise_rejects(folder, at).items():   # W-A
        out.append({"kind": "감리 확인 못 넣음", "zone_id": z,
                    "text": f"{z} {why.split(' — ')[0]} ({name}, {ts.replace('T', ' ')}) — {why.split(' — ', 1)[1]}" if why.startswith("감리 보완 요청 — ")   # 1-2 보완 요청
                    else f"{z} 감리 직접 확인({name}, {ts.replace('T', ' ')})을 넣지 못함 — {why}"})
    for r in rows:
        if r["f_cyl"] == "" and r["decision"] != "해체 승인 요청 가능" and r["predicted_due"] <= at.date().isoformat():
            out.append({"kind": "공시체 미입력 (예측일 지남)", "zone_id": r["zone_id"],
                        "text": f"{r['zone_id']} 예측 도달일 {r['predicted_due']} 지남 — 현장양생 공시체 시험 결과 입력 필요"})
    if prev_at:
        before = {r["zone_id"]: r["decision"] for r in ev.evaluate(folder, prev_at, cure_min)}
        for r in rows:
            b = before.get(r["zone_id"])
            if b and b != r["decision"]:
                out.append({"kind": "상태 바뀜", "zone_id": r["zone_id"], "text": f"{r['zone_id']}: {b} → {r['decision']} ({prev_at:%m-%d} → {at:%m-%d})"})
    poured = {p["zone_id"] for p in ev.read(folder, "pours.csv") if p["pour_date"] <= at.date().isoformat()}
    out += [{"kind": "입회 없음", "zone_id": z, "text": t} for z, t in (quality.watch_missing(folder) or {}).items() if z in poured]
    key = ev.floor_order(folder)   # 같은 종류 안에서는 층 순 → 구역 ID (총괄 11-05)
    return sorted(out, key=lambda a: (ORDER.index(a["kind"]), key(a["zone_id"])))


if __name__ == "__main__":
    import sys
    [s.reconfigure(errors="replace") for s in (sys.stdout, sys.stderr) if hasattr(s, "reconfigure")]
    ap = argparse.ArgumentParser()
    ap.add_argument("folder", nargs="?")
    ap.add_argument("--at")
    ap.add_argument("--prev")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()
    if a.self_test:
        import shutil
        import tempfile
        sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tools"))
        import make_demo_data as md
        t = tempfile.mkdtemp()
        try:
            md.make(t)                                                   # 시연 자료 (샘플 + 시연용 설치검측·작업계획서)
            zs = ev.read(t, "zones.csv")                                 # 샘플엔 판정층(9–12F)만 — Revit 은 모델 구역 13F 를 더함(N5), 시험도 같게
            with open(os.path.join(t, "zones.csv"), "a", encoding="utf-8") as f:
                f.write("".join(f"13F-{z['zone_id'].split('-', 1)[1]},13,{z['x0']},{z['y0']},{z['x1']},{z['y1']}\n" for z in zs if z["floor"] == "12"))
            at = datetime(2026, 12, 31, 9)
            k = alerts(t, at)
            kinds = [x["kind"] for x in k]
            assert kinds == sorted(kinds, key=ORDER.index), kinds
            assert "무단 해체 의심" not in kinds                          # 동바리 확인 기록이 없으면 무단 해체 경고도 없음
            hold = [x for x in k if x["kind"] == "타설 보류"]
            assert hold and all(x["zone_id"].startswith("13F-") for x in hold), hold      # 데모: 판정 최상층 12F → 다음 13F
            # 13F-B1 아래 3개층 '예' → 13F-B1 은 보류에서 빠짐, 9F-A2 '아니오'(승인 없음) → 무단 해체 의심 맨 위
            with open(os.path.join(t, "shore_checks.csv"), "w", encoding="utf-8") as f:
                f.write("zone_id,timestamp,present,actor,photo\n" + "".join(f"{z},2026-12-31T08:30,예,시험,\n" for z in ("12F-B1", "11F-B1", "10F-B1"))
                        + "9F-A2,2026-12-31T08:40,아니오,시험,\n")
            k2 = alerts(t, at)
            assert k2[0]["kind"] == "무단 해체 의심" and k2[0]["zone_id"] == "9F-A2", k2[0]
            assert "13F-B1" not in {x["zone_id"] for x in k2 if x["kind"] == "타설 보류"}
            # 상태 바뀜: 하루 전 판정과 비교하면 적어도 구역 하나는 결론이 다름 (데모 9F 는 12-30 → 12-31 에 바뀜이 있음 — 없으면 빈 목록이어도 형식만 확인)
            k3 = alerts(t, at, prev_at=datetime(2026, 12, 20, 9))
            assert all(" → " in x["text"] for x in k3 if x["kind"] == "상태 바뀜")
            assert any(x["kind"] == "상태 바뀜" for x in k3), "12-20 → 12-31 사이 결론이 바뀐 구역이 있어야 함"
            # 판정 이력: 12-20 → 12-31 → 12-31(다시) 순으로 기록하면 12-31 의 비교 기준은 12-20, 기록 없으면 None
            # W-A: 넣지 못한 감리 직접 확인 → '감리 확인 못 넣음' (그 뒤 감리 승인 기록이 생기면 사라짐)
            approval.log_supervise_reject(t, "9F-A2", "이감리", "s-9", "2026-12-31T08:52:10", "9F-A2 판정이 '존치 (구역 최소 강도 미달)' —\n해체 승인 요청 가능일 때만 감리 승인 기록")
            k4 = [x for x in alerts(t, at) if x["kind"] == "감리 확인 못 넣음"]
            assert [x["zone_id"] for x in k4] == ["9F-A2"] and "이감리, 2026-12-31 08:52" in k4[0]["text"] and "—\n" not in k4[0]["text"], k4
            assert approval.supervise_rejects(t, datetime(2026, 12, 30, 9)) == {}
            # W9 순찰 확인: 위에서 12F·11F·10F-B1 '예'(08:30) → 그 셋은 빠지고, 기록 없는 9F-A1 은 '확인 기록 없음', patrol_hours 1 → 08:30 줄도 지남
            pm = patrol_missing(t, at)
            assert "10F-B1" not in pm and pm["9F-A1"] is None and "9F-A2" not in pm, pm   # 9F-A2 = '아니오'(08:40) 도 순찰 기록
            pf = dict(patrol_floors(t, at))
            assert pf["9F-A1"] == "9F 순찰 확인 없음 5구역 (기준 24시간 [가정] · 확인 기록 없음) — 9F-A1, 9F-B1, 9F-B2, 9F-C1, 9F-C2 (순찰 때 휴대폰 [동바리 확인])", pf
            assert len(pf) == len({z.split("-")[0] for z in pm}) and "10F 순찰 확인 없음 5구역" in pf["10F-A1"], pf   # 층마다 한 줄
            with open(os.path.join(t, "options.json"), "w", encoding="utf-8") as f:
                f.write('{"patrol_hours": 0.25}')
            assert patrol_missing(t, at)["10F-B1"] == datetime(2026, 12, 31, 8, 30)
            assert "10F 순찰 확인 없음 6구역 (기준 0.25시간 [가정] · 마지막 확인 0시간 전)" in dict(patrol_floors(t, at))["10F-A1"]
            os.remove(os.path.join(t, "options.json"))
            assert [x["kind"] for x in alerts(t, at)].index("순찰 확인 없음") > [x["kind"] for x in alerts(t, at)].index("무단 해체 의심")
            assert prev_eval(t, at) is None
            for d in (datetime(2026, 12, 20, 9), at, at):
                log_eval(t, d, eval_summary(ev.evaluate(t, d)), now=datetime(2026, 11, 5, 1))
            assert prev_eval(t, at) == datetime(2026, 12, 20, 9) and prev_eval(t, datetime(2026, 12, 20, 9)) == at
            assert ev.read(t, EVAL_LOG)[1]["summary"].startswith("해체 승인 요청 가능 4 · 존치 ")
            assert eval_summary([{"decision": "존치 (설치 결함 기록)"}, {"decision": "존치"}, {"decision": "해체 승인 요청 가능"}]) \
                == "해체 승인 요청 가능 1 · 존치 2 (설치 결함 1) · 공시체 시험 필요 0"   # 37차: 설치 결함도 존치에 세고 따로 보임
            demo = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "revit", "onsite_data_demo")
            if os.path.isdir(demo):   # 11-12: folder 를 주면 사람 기록 구역을 빼고 '이미 기록 n' — approval.zone_counts 와 같은 수
                dt = datetime(2026, 12, 31, 9)
                c = approval.zone_counts(ev.evaluate(demo, dt), demo, dt)
                assert eval_summary(ev.evaluate(demo, dt), demo, dt) == (f"해체 승인 요청 가능 {c['ready']} · 존치 {c['keep']}" + (f" (설치 결함 {c['defect']})" if c["defect"] else "")
                                                                       + f" · 공시체 시험 필요 {c['cyl']}" + (f" · 이미 기록 {c['recorded']} (감리 승인·해체 완료·재받침)" if c["recorded"] else ""))
            # 입회 없음 (F5): 열이 없으면 안 셈 → 열을 넣고 9F-A1 만 '예·홍길동' → 나머지 타설 구역이 맨 아래 '입회 없음'
            assert "입회 없음" not in {x["kind"] for x in k3}
            ps = ev.read(t, "pours.csv")
            with open(os.path.join(t, "pours.csv"), "w", encoding="utf-8", newline="") as f:
                w = csv.DictWriter(f, fieldnames=[*ps[0], "supervisor_present", "watcher"])
                w.writeheader()
                w.writerows({**p, "supervisor_present": "예" if p["zone_id"] == "9F-A1" else "", "watcher": "홍길동" if p["zone_id"] == "9F-A1" else ""} for p in ps)
            k5 = alerts(t, at)
            iw = [x for x in k5 if x["kind"] == "입회 없음"]
            assert iw and k5[-len(iw):] == iw and "9F-A1" not in {x["zone_id"] for x in iw}, iw
            assert all(p["pour_date"] <= "2026-12-31" for p in ps if p["zone_id"] in {x["zone_id"] for x in iw})
        finally:
            shutil.rmtree(t, ignore_errors=True)
        print("alerts self-check OK")
    else:
        if not (a.folder and a.at):
            ap.error("데이터 폴더와 --at 이 필요합니다 (또는 --self-test)")
        for x in alerts(a.folder, datetime.fromisoformat(a.at), datetime.fromisoformat(a.prev) if a.prev else None):
            print(f"[{x['kind']}] {x['text']}")

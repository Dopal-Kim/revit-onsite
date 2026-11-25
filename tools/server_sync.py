"""Vercel 현장 서버 ↔ 데이터 폴더 동기화 (docs/redesign_v6.md §3). 표준 라이브러리만. C# 이식 기준본.

push = field_server.Store 로 판정 스냅샷(model + 층별 floor_view + recent + 입력 검사 자료)을 만들어 POST /api/sync/push.
pull = GET /api/sync/pull 로 휴대폰 입력을 받아 Store.measure/report 로 그대로 다시 넣음 (LAN 서버와 같은 검사·열·사진 저장·client_id 중복 방지)
       → POST /api/sync/ack. 서버는 판정하지 않으므로 pull 뒤 push 해야 휴대폰에 새 판정이 보인다 (sync = pull → push).

실행: python tools/server_sync.py sync --data <데이터 폴더> [--at 2026-12-31T09:00] [--url https://….vercel.app]
  서버 주소 = --url 또는 환경 변수 FIELD_SERVER_URL, Revit 쓰기 키 = 환경 변수 FIELD_SYNC_KEY (파일·명령행에 쓰지 않음).
"""
import argparse
import gzip
import inspect
import json
import os
import sys
import urllib.error
import urllib.request
from datetime import datetime

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import field_server as fs   # noqa: E402  (shoring 경로도 여기서 잡힘)
import evaluate as ev        # noqa: E402
import approval              # noqa: E402
import after_check as ac     # noqa: E402
import shore_inspect as si   # noqa: E402
import strength_timeline as stl   # noqa: E402
import provenance as pv          # noqa: E402  (11-12 근거 원장 누가·언제)
import evidence as evd           # noqa: E402  (1-2 감리 두 단계)
from export_4d import timeline_json   # noqa: E402  (웹 공유판과 같은 타임라인 모양)

RECENT_N = 200   # /api/recent 최대 n 과 같음
BUSY = "다른 PC·프로그램이 쓰는 중입니다"   # field_server.file_lock 잠금 실패 문구 (shortcut: 문구로 구분 — field_server 에 전용 예외가 생기면 그것으로)
CURE_MIN = 5.0   # 타임라인 양생 하한 °C — strength_timeline 기본값 (Revit [프로젝트 설정] 값이 있으면 C# 이 그 값)


def check_data(store, m):
    """서버가 판정 없이 입력 검사(field_server.validate 의 JS 판)를 하는 데 필요한 자료 — 판정 결과는 없음."""
    folder = store.folder
    ts = {k: {} for k in fs.SENSOR_VALUE}
    for k in fs.SENSOR_VALUE:
        for r in fs.read_rows(store.path(k)):
            ts[k].setdefault(r["sensor_id"], []).append(r["timestamp"])
    ok, _ = approval.verify(folder)
    appr, req = {}, {}
    for r in approval.read_log(folder) if ok else []:
        t = datetime.fromisoformat(r["timestamp"]).isoformat(timespec="seconds")
        if r["action"] == "감리 승인 기록":     # report() 문구용 — 구역별 가장 이른 승인 기록 시각
            appr[r["zone_id"]] = min(appr.get(r["zone_id"], t), t)
        elif r["action"] == "요청 제출":        # 감리 직접 확인(S3) 대상 — 구역별 마지막 요청 제출
            req[r["zone_id"]] = {"at": t, "actor": r["actor"]}
    pours = {r["zone_id"]: r.get("pour_date", "") for r in ev.read(folder, "pours.csv")}
    led = evd.ledger_at(folder, store.now().date())
    plan = {r["zone_id"]: r["date"] for r in ev.read(folder, evd.LEDGER) if r.get("stage") == "2" and r.get("result") == evd.PLAN_OK}
    return {"zones": {r["zone_id"]: {"floor": int(r["floor"]), **{k: float(r[k]) for k in ("x0", "y0", "x1", "y1")}} for r in ev.read(folder, "zones.csv")},
            "pours": pours,
            "cylinders": [[r.get(k) or "" for k in ("zone_id", "test_date", "curing", "f1", "f2", "f3", "pour_id")] for r in ev.read(folder, "cylinders.csv")],   # pour_id = W1 타설 묶음 줄 구분
            "sensor_ts": ts,
            "shores": {zid: sorted(k for k, v in si.read_shores(folder).items() if v["zone_id"] == zid) for zid in {v["zone_id"] for v in si.read_shores(folder).values()}},
            "stages": {st: [[c, n] for c, n, _, _ in its] for st, its in si.STAGES.items()},
            "after_items": [[c, n] for c, n in ac.ITEMS.items()],
            "mixes": list(ev.e.MIXES), "approved_at": appr, "requested": req, "seen": sorted(store.seen),
            "evidence": {z: {str(k): v["dates"].get(k, "") for k in evd.STAGES} for z, v in led.items()},   # 1-2 감리 화면 단계별 ✓/✕ (첫 날짜)
            "plan_confirmed": plan,
            "evidence_meta": pv.evidence_meta(folder, store.now().date().isoformat())}   # 11-12 근거 원장 단계마다 누가·언제 (API.md §10.5)


SPEC_COLS = ("zone_id", "type", "spec", "spacing_x_mm", "spacing_y_mm", "splices", "install_date")


def shore_info(folder):
    """층 → {shore_spec: shore_spec.csv 줄들, shore_removed: [{date, zone_id, count}] 최근 날짜부터} (docs/redesign_v6.md '자료 형식 확정'). 파일 없으면 빈 값."""
    zfloor = {r["zone_id"]: int(r["floor"]) for r in ev.read(folder, "zones.csv")}
    out = {}
    for r in ev.read(folder, "shore_spec.csv"):
        try:
            fl = int(str(r.get("floor") or "").strip())
        except ValueError:
            continue
        out.setdefault(fl, {"shore_spec": [], "shore_removed": []})["shore_spec"].append({k: (r.get(k) or "").strip() for k in SPEC_COLS})
    cnt = {}
    for r in ev.read(folder, "shore_removed.csv"):
        zid = (r.get("zone_id") or "").strip()
        if zid in zfloor:
            key = (zfloor[zid], (r.get("removed_date") or "").strip()[:10], zid)
            cnt[key] = cnt.get(key, 0) + 1
    for (fl, d, zid), n in sorted(cnt.items(), key=lambda kv: (kv[0][1], kv[0][2]), reverse=True):
        out.setdefault(fl, {"shore_spec": [], "shore_removed": []})["shore_removed"].append({"date": d, "zone_id": zid, "count": n})
    return out


def snapshot(store):
    m = store.model()
    store.model = lambda: m      # shortcut: floor_view 가 층마다 model() 을 다시 계산하지 않게 이번 스냅샷 동안만 고정 (인스턴스 속성)
    try:
        floors = {str(f["floor"]): store.floor_view(f["floor"]) for f in m["floors"]}
    finally:
        del store.model
    info = shore_info(store.folder)
    for f in m["floors"]:        # 휴대폰 입력 화면(model)·층 화면(floors) 둘 다 같은 값
        extra = info.get(f["floor"], {"shore_spec": [], "shore_removed": []})
        f.update(extra)
        floors[str(f["floor"])].update(extra)
    tl = stl.timeline(store.folder, cure_min=CURE_MIN)
    return {"version": 1, "pushed_at": datetime.now().isoformat(timespec="seconds"), "model": m, "floors": floors,
            "recent": store.recent(RECENT_N)["items"], "check": check_data(store, m),
            "timeline": {**timeline_json(tl), "steps": list(stl.STEPS)} if tl["dates"] else None}   # 강도 타임라인 웹(S1) — 그림만, 판정 아님


SUPERVISE_NOTE = "감리 직접 확인 (서버) — 현장 확인 ①②③"
REJECT_TAG = "감리 보완 요청"   # supervise_rejects.csv 이유 앞머리 — 오늘 볼 것에서 '보완 요청'으로 보임


def supervise(store, b, received_at):
    """감리 직접 확인(S3) → approvals.csv '감리 승인 기록' (approver = 감리 이름, signed_at = 서버가 받은 시각).
    그 시각 판정이 '해체 승인 요청 가능' + 그 구역 '요청 제출' 기록 + 아직 승인·해체 기록 없음일 때만. 아니면 fs.Bad (이유 = ack error).
    client_id 는 note 끝에 남겨 같은 확인을 두 번 넣지 않음."""
    if not isinstance(b, dict):
        raise fs.Bad("요청 형식이 틀렸습니다 (JSON 객체가 아님)")
    zid, cid = str(b.get("zone_id") or "").strip(), str(b.get("client_id") or "")
    if not fs.CLIENT_ID.match(cid):
        raise fs.Bad("client_id 가 없거나 형식이 틀렸습니다")
    name = fs.text(b.get("name"), "감리 이름", 20, required=True)
    action = b.get("action") or "final"   # 1-2 (11-11): plan = 작업계획서 확인 · final = 최종 확인(지금과 같음) · reject = 보완 요청
    if action not in ("plan", "final", "reject"):
        raise fs.Bad(f"action '{action}' 을 모릅니다 (plan · final · reject)")
    if not any(r["zone_id"] == zid for r in ev.read(store.folder, "zones.csv")):
        raise fs.Bad(f"구역 '{zid}' 이 없습니다")
    if action == "reject":                # 보완 요청 → 오늘 볼 것 (승인 기록 아님)
        why = fs.text(b.get("reason"), "보완 요청 사유", 100, required=True)
        approval.log_supervise_reject(store.folder, zid, name, cid, received_at, f"{REJECT_TAG} — {why}")
        return {"duplicate": False}
    if action == "plan":                  # 작업계획서 확인 → 근거 원장 2 단계 (doc_no = client_id, 두 번 안 넣음)
        if any(r.get("doc_no") == cid for r in ev.read(store.folder, evd.LEDGER)):
            return {"duplicate": True}
        evd.add(store.folder, zid, store.now().date(), 2, name, doc_no=cid, result=evd.PLAN_OK)
        return {"duplicate": False}
    checks = b.get("checks")
    if not (isinstance(checks, list) and len(checks) == 3 and all(c is True for c in checks)):
        raise fs.Bad("현장 확인 3가지를 모두 체크해야 승인 기록을 남깁니다")
    folder, at = store.folder, store.now()
    log = approval.read_log(folder)
    if any(cid in (r.get("note") or "") for r in log):
        return {"duplicate": True}
    if not any(r["zone_id"] == zid and r["action"] == "요청 제출" and datetime.fromisoformat(r["timestamp"]) <= at for r in log):
        raise fs.Bad(f"{zid} 는 요청 제출 기록이 없습니다 — 시공자가 [요청서]로 요청을 제출한 구역만 감리 확인을 받습니다")
    if approval.status_override(folder, at).get(zid) in (6, 7, 8):
        raise fs.Bad(f"{zid} 는 이미 감리 승인·해체·재받침 기록이 있습니다")
    dates = evd.ledger_at(folder, at.date()).get(zid, {}).get("dates", {})
    miss = [evd.STAGES[k] for k in (2, 4) if k not in dates]
    if miss:                              # 1-1 (11-11): 최종 확인 전 작업계획서·해체 전 점검 (가이드라인 표 3.2.1)
        raise fs.Bad(f"{zid} 는 {'·'.join(miss)} 근거 원장 기록이 없습니다 — 계획서 확인([계획서 확인])과 해체 전 점검을 먼저")
    row = next((r for r in ev.evaluate(folder, at) if r["zone_id"] == zid), None)
    if row is None:
        raise fs.Bad(f"구역 '{zid}' 판정이 없습니다")
    try:
        # shortcut: source 인자는 작업 세션이 approval.record 에 더하는 중 — 생기면 '감리 직접'으로 넘김 (다 바뀌면 조건 없이)
        src = {"source": "감리 직접"} if "source" in inspect.signature(approval.record).parameters else {}
        approval.record(folder, zid, "감리 승인 기록", name, row, note=f"{SUPERVISE_NOTE} · {cid}", now=at,
                        approver=name, signed_at=received_at[:16], **src)
    except ValueError as x:       # 판정이 '해체 승인 요청 가능'이 아님 · 사슬 손상 등 — approval.record 의 문구 그대로
        raise fs.Bad(str(x))
    return {"duplicate": False}


def call(url, key, path, body=None, gz=False):
    data = None if body is None else json.dumps(body, ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(url.rstrip("/") + path, method="POST" if body is not None else "GET", data=gzip.compress(data) if gz else data,
                                 headers={"Authorization": "Bearer " + key, "Content-Type": "application/gzip" if gz else "application/json",
                                          "User-Agent": "RevitOnSite-sync/1"})   # 스냅샷은 gzip (센서 시각 목록이 커서 — Vercel 요청 4.5 MB 상한)
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return json.loads(r.read().decode("utf-8"))
    except urllib.error.HTTPError as x:
        try:
            msg = json.loads(x.read().decode("utf-8")).get("error", "")
        except ValueError:
            msg = ""
        raise SystemExit(f"서버 {x.code}: {msg or x.reason} ({path})")
    except urllib.error.URLError as x:
        raise SystemExit(f"서버에 연결하지 못했습니다 ({x.reason}) — 인터넷과 서버 주소를 확인하세요")


def push(store, url, key):
    snap = snapshot(store)
    res = call(url, key, "/api/sync/push", snap, gz=True)
    return {"floors": len(snap["floors"]), "bytes": res.get("bytes"), "pushed_at": snap["pushed_at"]}


def pull(store, url, key, fixed_at=None):
    """서버 입력 → Store.measure/report (LAN 서버와 같은 길). 판정 시각 = fixed_at(데모 --at) 또는 서버가 받은 시각(한국 시각)."""
    saved = dup = 0
    errors = {}
    while True:
        res = call(url, key, "/api/sync/pull")
        items = res.get("items", [])
        acks = []
        for it in items:
            cid = it["client_id"]
            store.at = fixed_at or datetime.fromisoformat(it["received_at"][:16])
            try:
                route = it["route"]
                r = (supervise(store, it["body"], it["received_at"]) if route == "supervise" else
                     (store.report if route == "report" else store.measure)(it["body"], it.get("ua", "")))
                dup += bool(r.get("duplicate"))
                saved += not r.get("duplicate")
                acks.append({"client_id": cid})
            except fs.Bad as x:       # 서버 검사는 통과했는데 그 사이 데이터가 바뀜 — 저장 안 하고 사유를 서버에 남김 (다시 받지 않음)
                if BUSY in str(x):    # W7 잠금을 못 잡음 = 일시적 → 파일 잠김처럼 ack 안 함, 다음 동기화 때 다시 (C# Pull 도 같은 규칙)
                    errors[cid] = f"{x} — 다음 동기화 때 다시"
                    continue
                errors[cid] = str(x)
                acks.append({"client_id": cid, "error": str(x)})
                if it.get("route") == "supervise" and isinstance(it.get("body"), dict):   # 감리 확인 못 넣음 → supervise_rejects.csv → 오늘 볼 것 (C# FieldServer.Pull 과 같음)
                    approval.log_supervise_reject(store.folder, str(it["body"].get("zone_id") or "").strip(), str(it["body"].get("name") or "").strip(),
                                                  cid, it["received_at"], str(x))
            # 그 밖의 오류(파일 잠김 등)는 ack 하지 않음 → 다음 pull 때 다시 받음
            except PermissionError as x:
                errors[cid] = f"파일 잠김 — 다음 동기화 때 다시: {x}"
        if acks:
            call(url, key, "/api/sync/ack", {"items": acks})
        if not res.get("more") or not acks:
            break
    store.at = fixed_at
    return {"saved": saved, "duplicate": dup, "errors": errors}


def main(argv=None):
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(errors="replace")
        except (AttributeError, ValueError):
            pass
    ap = argparse.ArgumentParser(description="Revit On-Site Vercel 현장 서버 동기화")
    ap.add_argument("cmd", choices=("push", "pull", "sync"))
    ap.add_argument("--data", required=True, help="데이터 폴더 (zones.csv …)")
    ap.add_argument("--at", help="이 시각을 '지금'으로 (데모) YYYY-MM-DDTHH:MM")
    ap.add_argument("--url", default=os.environ.get("FIELD_SERVER_URL", ""), help="서버 주소 (기본: 환경 변수 FIELD_SERVER_URL)")
    a = ap.parse_args(argv)
    key = os.environ.get("FIELD_SYNC_KEY", "")
    if not a.url or not key:
        raise SystemExit("서버 주소(--url 또는 FIELD_SERVER_URL)와 환경 변수 FIELD_SYNC_KEY 가 필요합니다")
    if not os.path.exists(os.path.join(a.data, "zones.csv")):
        raise SystemExit(f"{a.data} 에 zones.csv 가 없습니다")
    at = datetime.fromisoformat(a.at) if a.at else None
    store = fs.Store(a.data, at)
    if a.cmd in ("pull", "sync"):
        r = pull(store, a.url, key, at)
        print(f"받음: 저장 {r['saved']}건, 이미 있음 {r['duplicate']}건, 거부 {len(r['errors'])}건")
        for cid, msg in r["errors"].items():
            print(f"  {cid}: {msg}")
    if a.cmd in ("push", "sync"):
        r = push(store, a.url, key)
        print(f"올림: {r['floors']}개 층 판정 ({r['pushed_at']} 기준, {r['bytes']} 바이트)")


if __name__ == "__main__":
    main()

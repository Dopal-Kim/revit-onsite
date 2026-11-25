"""동바리 단위 현장 점검 (F16 — 사용자 확정 11-05: "구역 체크리스트와 문제 동바리 표시", "사진은 날리고").

흐름: 층 QR → 층 평면(구역 칸 + 동바리 점) → 구역·단계 고름 → 항목 '전부 OK' 미리 채움, 문제 항목만 NG → NG 면 동바리 점 여러 개 고름 → 저장
→ Revit 그 동바리 빨강(덧그림)·구역 상태·알림. 사진 없음.
- 동바리 ID = 구역-행-열 `9F-A2-R03C05` — 구역 안 동바리 좌표로 정함 (행 = y 남→북, 열 = x 서→동, 0.1 m 로 묶음). 다시 생성해도 같은 자리면 같은 ID.
  `shores.csv` (id, zone_id, x, y, field_label) = 애드인이 모델 동바리로 씀 (field_label = 현장 스프레이 표기, 선택). 새 공유 매개변수 없음.
- 기록 = `install_inspection.csv` 를 넓힘: 기존 열(zone_id, item_code, result, inspector, date, photo_path) 뒤에 stage, shore_ids(; 구분), timestamp, source
  + 현장 입력 공통 열. stage 가 비었으면 설치 검측(예전 줄). 기존 열·기존 판정 바이트 그대로.
- 규칙 ⑦ (Python 먼저): 설치 검측 NG 가 풀리지 않은 구역 → 타설 전 확인 '보류' + 해체 판정 '해체 승인 요청 가능' 대신 '존치 (설치 결함 기록)'.
  풀림 = 같은 항목의 뒤 기록이 OK (동바리별, 동바리 ID 없는 줄은 구역 전체). 결론 문구는 '결함 기록'까지 — 지시 없음.
표준 라이브러리만 — C# ShoreInspect.cs 와 같은 값. 실행: python shore_inspect.py (자체 검사)
"""
import os
from datetime import datetime

import evaluate as ev

FILE, SHORES = "install_inspection.csv", "shores.csv"
BASE_COLS = ["zone_id", "item_code", "result", "inspector", "date", "photo_path"]   # 기존 열 (make_demo_data · 설치 검측 서류)
EXTRA_COLS = ["stage", "shore_ids", "timestamp", "source"]                          # F16 (사진 열 없음)
INSTALL = "설치 검측"
GRID_M = 0.1          # [가정] 동바리 좌표를 이 간격으로 묶어 행·열을 셈 (모델 좌표 오차 흡수)
# 단계 → [(항목 코드, 항목, 기준, 출처 표시)] — 수치는 regulation_check·install_rules 에 출처 있는 것만, 나머지 [입력] (현장 계획서 값)
STAGES = {
    INSTALL: [
        ("SHR-B", "수평연결재", "높이 3.5 m 초과 → 2 m 이내마다 2방향", "[확정] 안전보건규칙 제332조의2"),
        ("SHR-S", "이음", "이음 1개 이하 (3개 이상 이어 쓰지 않음)", "[확정] 안전보건규칙 제332조의2"),
        ("SHR-A", "상하 정렬", "아래층 동바리와 같은 위치", "[확정] 해체 가이드라인 4.3"),
        ("SHR-V", "연직도", "시공계획서 허용값 이내", "[입력]"),
        ("SHR-H", "높이·받침(잭) 조정", "조립도 높이·받침 길이", "[입력]"),
        ("SHR-P", "깔판·하부 고정", "조립도대로 고정", "[입력]"),
    ],
    "일상 점검": [
        ("DLY-D", "변형·침하", "작업 전 변형·침하 없음", "[확정] 안전보건규칙 제334조"),
        ("DLY-B", "수평연결재·체결 탈락", "탈락·풀림 없음", "[입력]"),
        ("DLY-L", "하부 지지 상태", "깔판·하층 슬래브 이상 없음", "[입력]"),
        ("DLY-W", "자재 적치", "슬래브 위 과적재 없음", "[입력]"),
    ],
    "해체 전": [
        ("PRE-C", "하부 3개층 동바리 존치", "타설층 포함 3개층 존치", "[확정] KCS 14 20 12:2024 3.3.2(2)"),
        ("PRE-A", "작업 준비·출입 통제", "관리감독자 확인", "[확정] 해체 가이드라인 4.1"),
        ("PRE-F", "필러 동바리 표시", "존치할 동바리 표시", "[입력]"),
    ],
    "해체 후": [
        ("AC-1", "필러·지지 동바리 타설층 포함 하부 3개층 설치", "가이드라인 4.3", "[확정] 해체 가이드라인 4.3"),
        ("AC-2", "각 층 같은 위치(상하 정렬)", "가이드라인 4.3", "[확정] 해체 가이드라인 4.3"),
        ("AC-3", "연직도", "가이드라인 4.3", "[확정] 해체 가이드라인 4.3"),
        ("AC-4", "자재 변형·침하", "가이드라인 4.3", "[확정] 해체 가이드라인 4.3"),
        ("AC-5", "콘크리트 면 결함", "가이드라인 4.3", "[확정] 해체 가이드라인 4.3"),
    ],
}
ITEM_LABEL = {c: n for items in STAGES.values() for c, n, _, _ in items}


def shore_ids(zone_id, pts):
    """구역 안 동바리 점 [(x, y)] m → 같은 순서의 ID 목록 `구역-R행C열` (행 = y 남→북, 열 = x 서→동, GRID_M 로 묶은 고유 좌표 순, 1부터 두 자리)."""
    kx = [round(x / GRID_M) for x, _ in pts]
    ky = [round(y / GRID_M) for _, y in pts]
    cols, rows = sorted(set(kx)), sorted(set(ky))
    return [f"{zone_id}-R{rows.index(b) + 1:02d}C{cols.index(a) + 1:02d}" for a, b in zip(kx, ky)]


def read_shores(folder):
    """shores.csv → {id: {zone_id, x, y, field_label}} (없으면 빈 dict)."""
    out = {}
    for r in ev.read(folder, SHORES):
        if not (r.get("id") or "").strip():
            continue
        try:
            x, y = float(r["x"]), float(r["y"])
        except (TypeError, ValueError, KeyError):   # 손으로 고친 줄 (x·y 빈칸 등) — 그 동바리만 빼고 나머지는 그대로 (코드 검토 2차)
            continue
        out[r["id"]] = {"zone_id": r.get("zone_id") or "", "x": x, "y": y, "field_label": (r.get("field_label") or "").strip()}
    return out


def records(folder, at=None):
    """install_inspection.csv → [{zone_id, stage, item_code, result(OK/NG), shore_ids[], t(datetime), inspector, note}] 시각 순 (같으면 파일 순).
    stage 빈칸 = 설치 검측 (예전 줄), t = timestamp 또는 date 00:00. at(datetime·date)을 주면 그 날짜까지 — 날짜 단위
    (총괄 61차 ②: 판정 09:00 뒤 같은 날 10:00 입력이 빠져 OK 가 안 풀렸음 → 승인 기록과 같은 판정일까지 규칙)."""
    cut = at.date() if isinstance(at, datetime) else at
    out = []
    for i, r in enumerate(ev.read(folder, FILE)):
        ts = (r.get("timestamp") or "").strip()
        d = (r.get("date") or "").strip()[:10]
        if not ts and not d:
            continue
        t = datetime.fromisoformat(ts) if ts else datetime.fromisoformat(d)
        if cut is not None and t.date() > cut:
            continue
        out.append({"zone_id": r["zone_id"], "stage": (r.get("stage") or "").strip() or INSTALL, "item_code": (r.get("item_code") or "").strip(),
                    "result": "OK" if (r.get("result") or "").strip().upper() == "OK" else "NG",
                    "shore_ids": [s.strip() for s in (r.get("shore_ids") or "").split(";") if s.strip()],   # 손으로 'A; B' 라고 적어도 (코드 검토 2차)
                    "t": t, "i": i, "inspector": (r.get("inspector") or "").strip(), "note": (r.get("note") or "").strip()})
    return sorted(out, key=lambda x: (x["t"], x["i"]))


def open_ng(folder, at=None, stage=None):
    """풀리지 않은 NG → {zone_id: [(item_code, shore_id 또는 '*', t)]} (구역 안 첫 NG 시각 순). stage 를 주면 그 단계만.
    같은 (구역, 항목)에서: 동바리 ID 없는 OK = 그 항목 전부 풀림, 동바리별 OK = 그 동바리만, NG = 그 동바리(없으면 '*')."""
    st = {}
    for r in records(folder, at):
        if stage is not None and r["stage"] != stage:
            continue
        z = st.setdefault(r["zone_id"], {})
        if r["result"] == "OK":
            for k in [k for k in z if k[0] == r["item_code"] and (not r["shore_ids"] or k[1] in r["shore_ids"])]:
                del z[k]
        else:
            for sid in r["shore_ids"] or ["*"]:
                z.setdefault((r["item_code"], sid), r["t"])
    return {zid: sorted(((k[0], k[1], t) for k, t in z.items()), key=lambda x: (x[2], x[0], x[1])) for zid, z in st.items() if z}


def ng_text(items):
    """[(항목, 동바리, t)] → '수평연결재 9F-A2-R03C05 외 2 · 이음 (구역 전체)' — 판정 사유·경고 글."""
    by = {}
    for c, s, _ in items:
        by.setdefault(c, []).append(s)
    return " · ".join(f"{ITEM_LABEL.get(c, c)} " + ("(구역 전체)" if "*" in ss else ss[0] + (f" 외 {len(ss) - 1}" if len(ss) > 1 else "")) for c, ss in by.items())


if __name__ == "__main__":
    import shutil
    import sys
    import tempfile
    [s.reconfigure(errors="replace") for s in (sys.stdout, sys.stderr) if hasattr(s, "reconfigure")]
    ids = shore_ids("9F-A2", [(0.6, 9.6), (1.8, 9.6), (0.6, 10.8), (1.80001, 10.79999), (3.0, 9.6)])
    assert ids == ["9F-A2-R01C01", "9F-A2-R01C02", "9F-A2-R02C01", "9F-A2-R02C02", "9F-A2-R01C03"], ids
    assert shore_ids("Z", [(5.0, 5.0)]) == ["Z-R01C01"]
    t = tempfile.mkdtemp()
    try:
        w = lambda s: open(os.path.join(t, FILE), "w", encoding="utf-8").write(",".join(BASE_COLS + EXTRA_COLS) + "\n" + s)   # noqa: E731
        w("9F-A1,SHR-H,OK,검측,2026-11-24,,,,,\n"                                     # 예전 줄 (stage 없음) = 설치 검측 OK
          "9F-A2,SHR-B,NG,김,2026-11-24,,설치 검측,9F-A2-R03C05;9F-A2-R03C06,2026-11-24T10:00,현장 점검\n"
          "9F-A2,SHR-S,NG,김,2026-11-24,,설치 검측,,2026-11-24T10:05,현장 점검\n"
          "9F-A2,DLY-D,NG,박,2026-11-30,,일상 점검,9F-A2-R01C01,2026-11-30T08:00,현장 점검\n")
        o = open_ng(t, stage=INSTALL)
        assert list(o) == ["9F-A2"] and [(c, s) for c, s, _ in o["9F-A2"]] == [("SHR-B", "9F-A2-R03C05"), ("SHR-B", "9F-A2-R03C06"), ("SHR-S", "*")], o
        assert ng_text(o["9F-A2"]) == "수평연결재 9F-A2-R03C05 외 1 · 이음 (구역 전체)"
        assert open_ng(t, datetime(2026, 11, 23, 9)) == {} and "DLY-D" in {c for c, _, _ in open_ng(t)["9F-A2"]}
        assert len(open_ng(t, datetime(2026, 11, 24, 9), INSTALL)["9F-A2"]) == 3                 # 61차 ②: 날짜 단위 — 09:00 판정에 같은 날 10:00 기록도 셈
        with open(os.path.join(t, FILE), "a", encoding="utf-8") as f:   # 다시 점검: R03C05 만 OK, 이음은 구역 전체 OK
            f.write("9F-A2,SHR-B,OK,김,2026-11-25,,설치 검측,9F-A2-R03C05,2026-11-25T09:00,현장 점검\n9F-A2,SHR-S,OK,김,2026-11-25,,설치 검측,,2026-11-25T09:00,현장 점검\n")
        o = open_ng(t, stage=INSTALL)
        assert [(c, s) for c, s, _ in o["9F-A2"]] == [("SHR-B", "9F-A2-R03C06")], o
        with open(os.path.join(t, FILE), "a", encoding="utf-8") as f:   # 구역 전체 OK → 그 항목 동바리별 NG 도 풀림
            f.write("9F-A2,SHR-B,OK,김,2026-11-25,,설치 검측,,2026-11-25T11:00,현장 점검\n")
        assert open_ng(t, stage=INSTALL) == {} and open_ng(t, datetime(2026, 11, 25, 10), INSTALL) == {}   # 같은 날 11:00 OK 도 셈 (61차 ②)
        assert open_ng(t, datetime(2026, 11, 24, 23), INSTALL)["9F-A2"][0][1] == "9F-A2-R03C05"
        open(os.path.join(t, SHORES), "w", encoding="utf-8").write("id,zone_id,x,y,field_label\n9F-A2-R01C01,9F-A2,0.6,9.6,A-1\n9F-A2-R09C09,9F-A2,,9.6,\n")
        assert read_shores(t) == {"9F-A2-R01C01": {"zone_id": "9F-A2", "x": 0.6, "y": 9.6, "field_label": "A-1"}}   # x 빈칸 줄은 건너뜀 (코드 검토 2차)
        with open(os.path.join(t, FILE), "a", encoding="utf-8") as f:   # 손으로 'A; B' 처럼 띄어 적은 동바리 목록도 같은 동바리 (코드 검토 2차)
            f.write("9F-C1,SHR-B,NG,김,2026-11-25,,설치 검측,9F-C1-R01C01; 9F-C1-R01C02,2026-11-25T12:00,현장 점검\n"
                    "9F-C1,SHR-B,OK,김,2026-11-25,,설치 검측,9F-C1-R01C02,2026-11-25T13:00,현장 점검\n")
        assert [s for _, s, _ in open_ng(t, stage=INSTALL)["9F-C1"]] == ["9F-C1-R01C01"]
        assert all(len(v) >= 3 for v in STAGES.values()) and len(ITEM_LABEL) == sum(len(v) for v in STAGES.values())
    finally:
        shutil.rmtree(t, ignore_errors=True)
    # 규칙 ⑦ (시연 자료 + 13F 구역): 9F-B1 설치 NG → 해체 요청 불가 '존치 (설치 결함 기록)', 같은 항목 OK 로 다시 점검 → 요청 가능,
    # 13F-B1(다음 타설) 설치 NG → 타설 전 확인 보류. 다른 단계(일상 점검) NG 는 판정·근거 원장 1 에 안 들어감
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tools"))
    import evidence
    import make_demo_data as md
    import pour_gate as pg
    t = tempfile.mkdtemp()
    try:
        md.make(t)
        zs = ev.read(t, "zones.csv")
        with open(os.path.join(t, "zones.csv"), "a", encoding="utf-8") as f:
            f.write("".join(f"13F-{z['zone_id'].split('-', 1)[1]},13,{z['x0']},{z['y0']},{z['x1']},{z['y1']}\n" for z in zs if z["floor"] == "12"))
        at = datetime(2026, 12, 31, 9)
        dec = lambda: {r["zone_id"]: r for r in ev.evaluate(t, at)}   # noqa: E731
        assert dec()["9F-B1"]["decision"] == "해체 승인 요청 가능"
        led0 = evidence.ledger_at(t, at.date())["9F-B1"]["dates"].get(1)
        add = lambda s: open(os.path.join(t, FILE), "a", encoding="utf-8").write(s)   # noqa: E731
        old = open(os.path.join(t, FILE), encoding="utf-8").read().splitlines()      # 시연 설치 검측 머리글 뒤에 F16 열을 더해 다시 씀 (서버가 하는 것과 같게)
        open(os.path.join(t, FILE), "w", encoding="utf-8").write("\n".join([old[0] + "," + ",".join(EXTRA_COLS)] + [x + ",,,," for x in old[1:]]) + "\n")
        add("9F-B1,DLY-D,NG,박,2026-12-30,,일상 점검,9F-B1-R01C01,2026-12-30T08:00,현장 점검\n")
        assert dec()["9F-B1"]["decision"] == "해체 승인 요청 가능"                             # 일상 점검 NG 는 판정에 안 들어감
        assert evidence.ledger_at(t, at.date())["9F-B1"]["dates"].get(1) == led0                # 근거 원장 1 그대로
        add("9F-B1,SHR-B,NG,김,2026-12-30,,설치 검측,9F-B1-R02C03,2026-12-30T10:00,현장 점검\n")
        d = dec()["9F-B1"]
        assert d["decision"] == "존치 (설치 결함 기록)" and d["reason"] == "설치 검측 결함 기록 미해결 — 수평연결재 9F-B1-R02C03", d
        add("9F-B1,SHR-B,OK,김,2026-12-30,,설치 검측,9F-B1-R02C03,2026-12-30T15:00,현장 점검\n")
        assert dec()["9F-B1"]["decision"] == "해체 승인 요청 가능"                             # 다시 점검 OK → 풀림
        assert pg.pour_gate(t, "13F-B1", at)["reason"].count("설치 검측") == 0
        add("13F-B1,SHR-S,NG,김,2026-12-30,,설치 검측,,2026-12-30T16:00,현장 점검\n")
        g = pg.pour_gate(t, "13F-B1", at)
        assert g["decision"] == "타설 보류" and "13F-B1: 설치 검측 결함 기록 미해결 — 이음 (구역 전체)" in g["reason"], g
    finally:
        shutil.rmtree(t, ignore_errors=True)
    print("shore_inspect self-check OK")

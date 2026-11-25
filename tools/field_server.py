"""현장 실측값 입력 서버 (시험·기준본) — docs/field_input_spec.md 의 API 를 그대로 구현. C# 애드인 내장 서버가 이것을 이식한다.

휴대폰 브라우저(web/field/index.html) → POST /api/measure → 데이터 폴더 CSV 에 한 줄 추가 → evaluate.py 로 그 구역 판정 다시 계산.
표준 라이브러리만. 같은 LAN 안에서만 쓴다 (토큰은 켤 때마다 새로 만든다).

실행: python tools/field_server.py --data shoring/sample/data [--at 2026-12-31T09:00] [--port 8765] [--host 0.0.0.0]
  --at 을 주면 그 시각을 "지금"으로 보고 판정·날짜 검사를 한다 (데모용). 없으면 PC 시계.
"""
import argparse
import copy
import csv
import hashlib
import hmac
import json
import os
import re
import secrets
import socket
import sys
import tempfile
import threading
import time
from contextlib import contextmanager
from datetime import date, datetime, timedelta
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "shoring"))
import evaluate as ev     # noqa: E402
import approval     # noqa: E402
import evidence as evd     # noqa: E402
import after_check as ac     # noqa: E402  (해체 후 점검 — F3)
import alerts as al     # noqa: E402  (오늘 볼 것 — D4)
import photos as ph     # noqa: E402
import provenance as pv  # noqa: E402  (11-12 누가 넣었나 — 감리 화면)  (현장 사진 근거 — F15)
import pour_gate as pg     # noqa: E402
import shore_input as sin
import shore_inspect as si     # noqa: E402  (동바리 단위 현장 점검 — F16)  (ap 로 줄이지 않음 — __main__ 의 argparse ap 와 겹쳐 명령행 실행이 전부 500 이었음)

HTML = os.path.join(ROOT, "web", "field", "index.html")
VIEW_HTML = os.path.join(ROOT, "web", "field", "view.html")   # 반장·협력업체용 읽기 전용 층 화면 (F12)
REPORT_FILE = "report_token.txt"   # 협력업체 해체 신고 토큰 (F14) — 보기·입력 토큰과 다름. 현장 입력 창에서 반장에게 따로 주는 QR (만료 없음, 데이터 폴더에 고정)
REPORT_COLS = ("source", "company", ph.SHA_COL)   # 신고 줄에만 값이 있는 shore_checks.csv 선택 열 (출처 = evidence.REPORT_SOURCE, 소속, 사진 SHA-256)
PHOTO_COLS = (ph.SHA_COL,)   # 사진을 올린 줄에만 값이 있는 선택 열 (동바리 확인·해체 후 점검, F15) — photo 칸 = 사진/YYYYMMDD/… 상대 경로
VIEW_FILE = "view_token.txt"   # 읽기 전용 보기 토큰 — 데이터 폴더에 한 번 만들어 두고 계속 씀(인쇄해 붙인 층 표지 QR 이 서버를 다시 켜도 열리게), 입력 토큰과 다름
# 구역 상태 → 반장용 한 줄 (지시 문구 없음 — '금지'·'확인 전'까지, F12). 0·1·2(타설 전·설치)는 '해체 금지 — 타설 전'
VIEW_LINE = {7: ("해체 완료 기록 있음", "done"), 6: ("감리 확인 기록 있음 — 현장 책임자 지시 후 해체", "ok"), 5: ("해체 금지 — 감리 확인 전 (해체 승인 요청 가능)", "ready"),
             8: ("해체 금지 — 재받침", "hold"), 4: ("해체 금지 — 공시체 시험 필요", "hold"), 3: ("해체 금지 — 존치", "hold"), 9: ("해체 금지 — 존치", "hold"),
             2: ("해체 금지 — 설치 결함 기록", "hold")}   # F16 규칙 ⑦ (36차: 전엔 '타설 전'으로 나옴)
COMMON = ["entered_by", "entered_at", "device", "note", "client_id"]   # 모든 파일 기존 열 뒤에 붙는 공통 열
KINDS = {   # kind → (화면 이름, 파일, 기존 열, 판정에서의 역할)
    "cylinder": ("공시체", "cylinders.csv", ["zone_id", "test_date", "curing", "f1", "f2", "f3"], "기준 근거"),
    "sensor_temp": ("온도 센서", "sensors.csv", ["sensor_id", "floor", "x", "y", "timestamp", "temp_c"], "위치 근거 (등가재령 → 강도 IDW)"),
    "sensor_strength": ("강도 센서", "sensor_strength.csv", ["sensor_id", "floor", "x", "y", "timestamp", "mpa"], "위치 근거 (강도 IDW)"),
    "rebound": ("반발경도", "rebound.csv", ["zone_id", "x", "y", "test_date", "r_value"], "참고 (판정 미사용)"),
    "shore_check": ("동바리 확인", "shore_checks.csv", ["zone_id", "timestamp", "present", "actor", "photo"],
                    "타설 전 게이트 (아래층 동바리가 서 있는지, pour_gate.py)"),
    "after_check": ("해체 후 점검", "after_check.csv", ["zone_id", "date", "item_code", "result", "by", "photo"],
                    "참고 — 해체 후 점검 (가이드라인 4.3, 판정 미사용, 근거 원장 8 결과보고와 묶음)"),
    "pour": ("타설", "pours.csv", ["zone_id", "pour_date", "pour_time", "mix", "fck"],
             "타설일·배합·fck = 강도 계산 시작 (해체 판정·공정) — Revit [타설 등록]"),
    "shore_spec": ("동바리 사양", sin.SPEC, sin.SPEC_COLS,
                   "참고 — 동바리 종류·규격·간격·이음·설치일 (v6: 공정표 대신 직접 입력, 판정 미사용) — Revit [동바리 사양]"),
    "shore_inspect": ("동바리 점검", si.FILE, si.BASE_COLS,
                      "설치 검측 NG 가 풀리지 않으면 해체 요청 불가·타설 보류 (F16 규칙 ⑦), 다른 단계는 기록·Revit 빨강 표시"),
}
SAMPLE_COLS = ["pour_id", "sampled_at", "supplier", "truck_no", "storage_zone", "photo", ph.SHA_COL]   # 공시체 채취 정보 (선택, F2) — 값이 있을 때만 열을 더함. pour_id 는 W1 타설 묶음 (zone_id 가 비면 판정에 씀)
POUR_COLS = ["pour_id", "supervisor_present", "watcher"]   # 11-12 Revit #7 타설 입회 감리(예/아니오)·감시자 (선택, quality.watch_missing) · v6.2 W1 타설 묶음 (선택) — 같이 친 구역 묶음, 공시체 한 번으로 묶음 전체 (evaluate.cyl_rows)
SENSOR_VALUE = {"sensor_temp": "temp_c", "sensor_strength": "mpa"}
CURINGS = ev.e.FIELD_CURINGS
LIMITS = {"mpa": [0, 100], "temp_c": [-30, 90], "r_value": [10, 70]}   # 강도·온도 [확정 spec §3], 반발경도 R 10–70 [가정]
MAX_BODY, MAX_READINGS = 12_000_000, 5000   # 본문 상한 = 사진 8 MB [가정] 의 base64(×4/3) + JSON
TOKEN_TTL_H = 12.0   # [가정] 접속 주소(토큰) 유효 시간 — 지나면 PC 현장 입력 창에서 [새 주소] (감사 A-16, F6). 실제 시계 기준 (데모 고정 시각과 무관)
TS = re.compile(r"^\s*([0-9]{4})[-/.]([0-9]{1,2})[-/.]([0-9]{1,2})[T ]([0-9]{1,2}):([0-9]{2})(?::[0-9]{2}(?:\.[0-9]+)?)?\s*$")   # ASCII 숫자만 (전각 ０８ 거부 — 코드 검토 2차)
DAY = re.compile(r"^\s*([0-9]{4})[-/.]([0-9]{1,2})[-/.]([0-9]{1,2})\s*$")
ID = re.compile(r"^[0-9A-Za-z가-힣_\-]{1,20}$")
CLIENT_ID = re.compile(r"^[0-9A-Za-z_\-]{1,80}$")


class Bad(ValueError):
    """사용자에게 그대로 보여 줄 한국어 오류 (무엇을 어떻게 고칠지)."""


# ---------- 값 검사 ----------
def num(b, key, label, unit, lo, hi, lo_open=False):
    v = b.get(key)
    if v is None or str(v).strip() == "":
        raise Bad(f"{label} 값이 비어 있습니다")
    try:
        f = float(str(v).strip().replace(",", "."))     # 휴대폰 키패드의 쉼표 소수점도 받는다
    except ValueError:
        raise Bad(f"{label} '{v}' 은(는) 숫자가 아닙니다 — 숫자만 넣으세요 (예: 24.5)")
    if f != f or f < lo or f > hi or (lo_open and f <= lo):
        hint = " (kgf/cm² 값이면 10.2 로 나누어 MPa 로)" if unit == "MPa" and f > hi else ""
        raise Bad(f"{label} {f:g}{' ' + unit if unit else ''} 은(는) 범위 밖입니다 — {lo}{'초과' if lo_open else ''}~{hi} {unit} 로 넣으세요{hint}")
    return f


def day(v, label):
    m = DAY.match(str(v or ""))
    try:
        return date(*map(int, m.groups()))
    except (AttributeError, ValueError):
        raise Bad(f"{label} '{v or ''}' 형식이 틀렸습니다 — 2026-12-30 처럼 넣으세요")


def stamp(v, label="시각"):
    m = TS.match(str(v or ""))
    try:
        return datetime(*map(int, m.groups()))
    except (AttributeError, ValueError):
        raise Bad(f"{label} '{v or ''}' 형식이 틀렸습니다 — 2026-12-30 09:00 처럼 날짜와 시각을 넣으세요")


def text(v, label, maxlen, required=False):
    s = " ".join(str(v or "").split())
    if required and not s:
        raise Bad(f"{label} 칸이 비어 있습니다 — 적어 주세요 (기록에 남습니다)")
    if len(s) > maxlen:
        raise Bad(f"{label}: {maxlen}자 이내로 줄여 주세요 (지금 {len(s)}자)")
    return "'" + s if s and s[0] in "=+-@" else s       # 엑셀에서 수식으로 실행되지 않게


def fmt(f):
    return f"{f:g}"


def device_of(ua):
    """User-Agent → 짧은 기기 요약 (예: 'Android Chrome')."""
    os_ = next((n for k, n in (("iPhone", "iPhone"), ("iPad", "iPad"), ("Android", "Android"), ("Windows", "Windows"),
                               ("Mac OS", "Mac")) if k in ua), "기타")
    br = next((n for k, n in (("SamsungBrowser", "Samsung"), ("NAVER", "Naver"), ("KAKAOTALK", "KakaoTalk"), ("Edg/", "Edge"),
                              ("CriOS", "Chrome"), ("Chrome", "Chrome"), ("Firefox", "Firefox"), ("Safari", "Safari")) if k in ua), "")
    return f"{os_} {br}".strip()


# ---------- CSV ----------
def read_rows(path):
    if not os.path.exists(path):
        return []
    with open(path, encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


LOCK_TRIES, LOCK_WAIT_S, LOCK_STALE_S = 50, 0.1, 30   # [가정] W7 잠금: 0.1 초씩 50번(5 초) 기다림, 30 초 넘은 잠금 파일은 멈춘 프로그램이 남긴 것


@contextmanager
def file_lock(path):
    """W7 (v6.2): 데이터 폴더 CSV 이어 쓰기 잠금 — '파일.lock' 을 배타 생성 (다른 PC·Revit C# 와 같은 이름 — 네트워크 데이터 폴더도), 잡힐 때까지 짧게 다시.
    못 잡으면 Bad — 이번 입력은 쓰지 않음 (자료 손실·두 줄 섞임 없음)."""
    lk = path + ".lock"
    fd = None
    for _ in range(LOCK_TRIES):
        try:
            fd = os.open(lk, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            break
        except (FileExistsError, PermissionError):            # Windows: 지워지는 중인 파일은 PermissionError
            try:
                if time.time() - os.path.getmtime(lk) > LOCK_STALE_S:
                    os.remove(lk)
                    continue
            except OSError:
                pass
            time.sleep(LOCK_WAIT_S)
    if fd is None:
        raise Bad(f"{os.path.basename(path)} 를 다른 PC·프로그램이 쓰는 중입니다 — 잠시 뒤 다시 저장하세요 (이번 입력은 저장하지 않았습니다)")
    try:
        yield
    finally:
        os.close(fd)
        try:
            os.remove(lk)
        except OSError:
            pass


def append_rows(path, cols, rows, opt=()):
    """_append_rows 를 파일 잠금 안에서 (W7 — 두 곳에서 같은 CSV 에 동시에 이어 써도 줄 손실 없음)"""
    with file_lock(path):
        return _append_rows(path, cols, rows, opt)


def _append_rows(path, cols, rows, opt=()):
    """header 가 없으면 만들고, 공통 열이 없으면 header 뒤에 붙여 다시 쓴 다음 줄을 추가한다. 기존 열 순서는 그대로.
    opt = 선택 열 (공시체 채취 정보) — 이번 줄에 값이 있을 때만 머리에 더함 (값이 없으면 파일을 다시 쓰지 않음)."""
    used = [c for c in opt if any(r.get(c) for r in rows)]
    want = cols + COMMON + used
    if not os.path.exists(path) or os.path.getsize(path) == 0:
        header, old = want, []
    else:
        with open(path, encoding="utf-8-sig", newline="") as f:
            r = csv.reader(f)
            header = next(r, [])
            missing = [c for c in cols if c not in header]
            if missing:
                raise Bad(f"{os.path.basename(path)} 의 첫 줄에 {missing} 열이 없습니다 — PC 에서 파일 형식을 확인하세요")
            extra = [c for c in COMMON + used if c not in header]
            old = [row for row in r if any(c.strip() for c in row)] if extra else None   # 빈 줄은 버림 (",,,,," 로 바뀌면 evaluate 가 멈춤 — C# 이식 지적)
        header = header + extra
    if old is not None:     # 새 파일이거나 header 를 늘려야 함 → 임시 파일에 다 쓰고 바꿔 끼운다 (도중 실패 시 원본 보존)
        fd, tmp = tempfile.mkstemp(dir=os.path.dirname(path), suffix=".tmp")
        try:
            with os.fdopen(fd, "w", encoding="utf-8-sig", newline="") as f:
                w = csv.writer(f)
                w.writerow(header)
                w.writerows(row + [""] * (len(header) - len(row)) for row in old)     # 기존 줄은 공통 열 빈칸
            os.replace(tmp, path)
        finally:
            if os.path.exists(tmp):     # 바꿔 끼우기 실패(Excel 잠김) — 임시 파일이 쌓이지 않게 (코드 검토 2차)
                os.remove(tmp)
    with open(path, "rb") as f:
        needs_nl = f.seek(0, 2) > 0 and f.seek(-1, 2) >= 0 and f.read(1) not in (b"\n", b"\r")   # 엑셀 저장 파일은 끝 줄바꿈이 없을 수 있음
    with open(path, "a", encoding="utf-8", newline="") as f:     # 이어 쓰기는 BOM 없이
        if needs_nl:
            f.write("\r\n")
        csv.DictWriter(f, fieldnames=header, extrasaction="ignore").writerows(rows)


# ---------- 상태 ----------
def floor_name(fl, zones):
    """층 이름 = 그 층 첫 구역 ID 의 '-' 앞 (KBIM B1·PH 처럼 번호와 이름이 다른 층), 없으면 번호로 (음수 = B)."""
    head = zones[0]["zone_id"].split("-")[0] if zones and "-" in zones[0]["zone_id"] else ""
    return head or (f"B{-fl}" if fl < 0 else f"{fl}F")


def status_of(decision, at, due):
    """design/ui_spec.md §6: 요청 가능 5, 공시체 시험 필요 4, 설치 결함 기록 2 (F16 규칙 ⑦), 존치 → 예측 해체일 전 3 양생 중 / 이후 9 존치 연장 경고."""
    if not decision:
        return 0
    if decision == "해체 승인 요청 가능":
        return 5
    if decision == "존치 (설치 결함 기록)":
        return 2
    if decision == "공시체 시험 필요":
        return 4
    return 3 if at.date() < date.fromisoformat(due) else 9


def view_token(folder, name=VIEW_FILE):
    """데이터 폴더의 고정 토큰 — 보기(view_token.txt)·신고(report_token.txt) (없거나 이상하면 새로 만들어 저장). C# FieldServer.ViewTokenOf 와 같은 파일."""
    p = os.path.join(folder, name)
    try:
        with open(p, encoding="utf-8-sig") as f:
            t = f.read().strip()
    except OSError:
        t = ""
    if not re.fullmatch(r"[0-9A-Za-z_\-]{12,64}", t):
        t = secrets.token_urlsafe(12)
        if os.path.join("shoring", "sample") not in os.path.normcase(os.path.abspath(folder)):   # 샘플 폴더에는 쓰지 않음 (그때는 이번 실행만)
            with open(p, "w", encoding="utf-8", newline="") as f:
                f.write(t + "\n")
    return t


class Store:
    def __init__(self, folder, at=None, default_fck=None, prev_at=None):
        self.folder, self.at, self.lock = folder, at, threading.Lock()
        self.prev_at = prev_at              # 그 전 판정 시각 — 주면 알림 '상태 바뀜' (D4)
        self.default_fck = default_fck      # PRJ_DefaultFck — 주면 pours.csv fck 와 다를 때 경고 (감사 A-29)
        self.seen = {r.get("client_id") for k in KINDS for r in read_rows(self.path(k)) if r.get("client_id")}

    def path(self, kind):
        return os.path.join(self.folder, KINDS[kind][1])

    def now(self):
        return self.at or datetime.now().replace(second=0, microsecond=0)

    def sensors(self):
        """{(kind, id): {floor, x, y, last, count}} — 센서 파일 두 개가 그대로면 앞 결과를 다시 씀 (A-20, 깊은 사본)."""
        key = tuple(ev.file_key(self.folder, KINDS[k][1]) for k in SENSOR_VALUE)
        if getattr(self, "_sens_key", None) != key:
            self._sens, self._sens_key = self._read_sensors(), key
        return copy.deepcopy(self._sens)

    def _read_sensors(self):
        """같은 ID 의 첫 줄 위치 (evaluate 온도 센서와 같은 규칙)."""
        out = {}
        for kind, vcol in SENSOR_VALUE.items():
            for r in read_rows(self.path(kind)):
                s = out.get((kind, r["sensor_id"]))
                if s is None:
                    s = out[(kind, r["sensor_id"])] = {"sensor_id": r["sensor_id"], "kind": kind, "floor": int(r["floor"]),
                                                       "x": float(r["x"]), "y": float(r["y"]), "count": 0, "last": None}
                s["count"] += 1
                if s["last"] is None or r["timestamp"] >= s["last"]["timestamp"]:
                    s["last"] = {"timestamp": r["timestamp"], vcol: float(r[vcol])}
        return out

    def model(self):
        at = self.now()
        zones = ev.read(self.folder, "zones.csv")
        pours = {r["zone_id"]: r for r in ev.read(self.folder, "pours.csv")}
        try:
            dec, err = {r["zone_id"]: r for r in ev.evaluate(self.folder, at)}, ""
        except SystemExit as x:          # evaluate 의 입력 점검 문구 (CSV 를 사람이 고쳐야 함)
            dec, err = {}, str(x)
        cold = {}
        if not err:
            try:
                for w in ev.cold_checks(self.folder, at):     # 한중 초기양생 경고 (판정은 안 바꿈)
                    cold.setdefault(w["zone_id"], []).append(w["text"])
                for w in ev.cross_checks(self.folder, at):    # 공시체–센서 교차 검사 (판정은 안 바꿈)
                    cold.setdefault(w["zone_id"], []).append(w["text"])
                for w in ev.input_checks(self.folder, at, self.default_fck):   # fck·공시체 편차 점검 (판정은 안 바꿈, A-29)
                    cold.setdefault(w["zone_id"], []).append(w["text"])
            except SystemExit as x:
                err = str(x)
        alerts = []
        if not err:
            try:
                alerts = al.alerts(self.folder, at, self.prev_at or al.prev_eval(self.folder, at))   # 오늘 볼 것 (D4) — 비교 기준 = --prev, 없으면 eval_log.csv
            except SystemExit as x:
                err = str(x)
        done = approval.status_override(self.folder, at)           # 감리 승인 기록 6 · 해체 완료 7 · 재받침 8 (판정일까지) (approvals.csv, 사람이 입력)
        try:
            led = evd.ledger_at(self.folder, at.date())             # 근거 원장 n/8 (evidence.py)
        except (SystemExit, ValueError, KeyError) as x:
            led, err = {}, err or f"근거 원장을 읽을 수 없음: {x}"
        floors, cm = {}, pv.cyl_meta(self.folder)   # cm = 공시체 줄 누가·언제 (감리 화면, 11-12)
        top = max((int(z["floor"]) for z in zones if z["zone_id"] in dec), default=None)   # 타설된 최상층 — 그 위층만 타설 전 확인
        for z in zones:
            d, p = dec.get(z["zone_id"], {}), pours.get(z["zone_id"], {})
            fl = int(z["floor"])
            floors.setdefault(fl, []).append({
                "zone_id": z["zone_id"], **{k: float(z[k]) for k in ("x0", "y0", "x1", "y1")},
                "status": done.get(z["zone_id"]) or status_of(d.get("decision", ""), at, d.get("predicted_due", "")),
                "decision": d.get("decision", ""), "reason": d.get("reason", "타설 예정 (판정 시각 뒤)" if p else "타설 기록 없음"),
                "predicted_due": d.get("predicted_due", ""), "pour_date": p.get("pour_date", ""), "pour_id": (p.get("pour_id") or "").strip(),   # pour_id = W1 타설 묶음 (휴대폰 공시체 묶음 고르기)
                **{k: d.get(k, "") for k in ("threshold", "f_cyl", "cyl_test_date", "f_est_min", "est_source", "level_gate", "age_days", "early_form")},
                **dict(zip(("cyl_entered_by", "cyl_entered_at"), cm.get((z["zone_id"], d.get("cyl_test_date", "")))
                           or cm.get(("lot:" + (p.get("pour_id") or "").strip(), d.get("cyl_test_date", ""))) or ("", ""))),
                "warnings": cold.get(z["zone_id"], []),
                "evidence_stage": led.get(z["zone_id"], {}).get("stage", 0),
                "evidence_warnings": led.get(z["zone_id"], {}).get("warnings", []),
                "pour_gate": self.gate(z["zone_id"], at) if not d and top is not None and fl > top else None})   # 아직 타설 전 구역만 — 현장에서 타설 직전에 확인
        return {"ok": True, "project": os.path.basename(os.path.abspath(self.folder)), "project_id": self.project_id(), "now": at.strftime("%Y-%m-%dT%H:%M"),
                "fixed_now": self.at is not None, "error": err,
                "floors": [{"floor": fl, "name": floor_name(fl, zs), "zones": zs} for fl, zs in sorted(floors.items())],
                "sensors": sorted(self.sensors().values(), key=lambda s: (s["floor"], s["kind"], s["sensor_id"])),
                "kinds": [{"kind": k, "label": v[0], "file": v[1], "role": v[3]} for k, v in KINDS.items()],
                "curings": CURINGS, "limits": LIMITS, "rule_basis": ev.RULE_BASIS, "alerts": alerts,
                "inspect": [{"stage": st, "items": [{"code": c, "label": n, "criterion": k, "source": src} for c, n, k, src in its]} for st, its in si.STAGES.items()]}

    # ---- GET /api/floor (읽기 전용, F12) ----
    def floor_view(self, fl, report=False):
        """반장·협력업체용 층 화면 자료: 구역마다 큰 글자 한 줄 + 무단 해체 의심, 층 상태 한 줄. 수치 없음. report = 신고 토큰으로 열림 (구역마다 신고 버튼, F14)."""
        m = self.model()
        f = next((x for x in m["floors"] if x["floor"] == fl), None)
        if f is None:
            return {"ok": False, "error": f"{fl}층 구역이 없습니다 — 층 표지 QR 을 다시 찍으세요"}
        watch = {} if m["error"] else evd.removal_watch(self.folder, self.now().date())
        zs = []
        for z in f["zones"]:
            line, kind = VIEW_LINE.get(z["status"], ("해체 금지 — 타설 전", "hold"))
            zs.append({"zone_id": z["zone_id"], "line": line, "kind": kind, "suspect": watch.get(z["zone_id"], ""), "status": z["status"]})   # status = W10 층 표지 pz 비교
        try:                    # F16: 풀리지 않은 NG (모든 단계) — 동바리 점 빨강·구역 한 줄. 판정 오류(첫 타설 전 등)와 상관없이 (코드 검토 2차)
            ng = si.open_ng(self.folder, self.now())
        except (ValueError, KeyError, OSError):
            ng = {}
        for z in zs:
            z["ng"] = si.ng_text(ng.get(z["zone_id"], []))
        zone_ids = {z["zone_id"] for z in zs}
        bad = {(zid, sid) for zid, v in ng.items() for _, sid, _ in v}
        shores = [{"id": k, "zone_id": v["zone_id"], "x": v["x"], "y": v["y"], "label": v["field_label"],
                   "ng": (v["zone_id"], k) in bad or (v["zone_id"], "*") in bad}
                  for k, v in si.read_shores(self.folder).items() if v["zone_id"] in zone_ids]
        sus = [z["zone_id"] for z in zs if z["suspect"]]
        hold = sum(z["kind"] == "hold" for z in zs)
        if sus:
            st = ("무단 해체 의심 — 확인 필요: " + ", ".join(sus), "hold")
        elif hold:
            st = (f"동바리 해체 금지 — {hold}구역", "hold")
        elif any(z["kind"] == "ready" for z in zs):
            st = ("해체 금지 — 감리 확인 전 (해체 승인 요청 가능)", "ready")
        elif all(z["kind"] == "done" for z in zs):
            st = ("해체 완료 기록 있음", "done")
        else:
            st = (f"감리 확인 기록 있음 — {sum(z['kind'] in ('ok', 'done') for z in zs)}구역", "ok")
        return {"ok": True, "project": m["project"], "now": m["now"], "fixed_now": m["fixed_now"], "error": m["error"],
                "floor": fl, "name": f["name"], "status": st[0], "kind": st[1], "zones": zs, "sign_valid_hours": ev.num_option(self.folder, "sign_valid_hours", 24),   # W10 [가정]
                "floors": [{"floor": x["floor"], "name": x["name"]} for x in m["floors"]], "can_report": report,
                "zone_rects": [{k: z[k] for k in ("zone_id", "x0", "y0", "x1", "y1")} for z in f["zones"]], "shores": shores}

    # ---- POST /api/report (협력업체 해체 신고, F14) ----
    def report(self, b, ua):
        """반장이 층 화면에서 구역마다 '뺐음'(아니오)·'다시 세움'(예)을 한 번 누름 → shore_checks.csv 한 줄 (source = 협력업체 신고, 시각 = 서버 지금).
        판정·결론은 안 바꿈 — 무단 해체 의심(removal_watch)·타설 전 확인(pour_gate)·근거 원장 7 해체가 기존 규칙대로 받음. 답은 신고 확인 문구까지.
        device_at = 휴대폰이 오프라인으로 저장했다가 다시 보낸 신고의 누른 시각(기기 시계) — timestamp 는 그래도 서버가 받은 시각(위조 방지), 기기 시각은 note 에만."""
        if not isinstance(b, dict):
            raise Bad("요청 형식이 틀렸습니다 (JSON 객체가 아님)")
        zid = str(b.get("zone_id") or "").strip()
        if zid not in {r["zone_id"] for r in ev.read(self.folder, "zones.csv")}:
            raise Bad(f"구역 '{zid}' 이(가) 모델에 없습니다 — 층 화면을 새로 고치세요")
        present = str(b.get("present") or "").strip()
        if present not in ("예", "아니오"):
            raise Bad("'동바리를 뺐음' 또는 '다시 세움'을 누르세요")
        cid = str(b.get("client_id") or "")
        if not CLIENT_ID.match(cid):
            raise Bad("client_id 가 없거나 형식이 틀렸습니다 (영문·숫자·-·_ 80자 이내)")
        name, company = text(b.get("name"), "이름", 20, required=True), text(b.get("company"), "소속", 40)
        dev = text(b.get("device_at"), "기기 시각", 30)
        photo, sha = "", ""
        note = f"기기 시각 {dev} (오프라인 저장 뒤 전송)" if dev else ""
        now = self.now()
        ts = now.strftime("%Y-%m-%dT%H:%M")
        with self.lock:
            dup = cid in self.seen
            if not dup:
                photo, sha = self.photo(b, zid, "report", now)
                append_rows(self.path("shore_check"), KINDS["shore_check"][2],
                            [{"zone_id": zid, "timestamp": ts, "present": present, "actor": name, "photo": photo, "source": evd.REPORT_SOURCE, "company": company, ph.SHA_COL: sha,
                              "entered_by": name, "entered_at": datetime.now().strftime("%Y-%m-%dT%H:%M:%S"), "device": device_of(ua), "note": note, "client_id": cid}],
                            REPORT_COLS)
                self.seen.add(cid)
            approved = approval.verify(self.folder)[0] and any(
                r["zone_id"] == zid and r["action"] == "감리 승인 기록" and datetime.fromisoformat(r["timestamp"]) <= now for r in approval.read_log(self.folder))
        msg = ("신고 저장됨 — 동바리 다시 세움" if present == "예" else
               "신고 저장됨 — 감리 승인 기록 있음: 해체 시각 기록됨" if approved else "신고 저장됨 — 감리 승인 기록 없음: 현장 확인 요청됨")
        return {"ok": True, "saved": KINDS["shore_check"][1], "duplicate": dup, "zone_id": zid, "present": present, "timestamp": ts, "message": msg,
                **({"photo": photo} if photo else {})}

    def photo(self, b, zid, kind, at):
        """b['photo_data'] (base64·data URL, 휴대폰 사진 1장) → 사진/YYYYMMDD/… 원본 저장 → (상대 경로, sha256). 없으면 ("", ""). 검사 실패 → Bad (F15).
        다른 검사를 모두 통과한 뒤에만 부른다 (틀린 요청이 사진만 남기지 않게).
        at = 그 기록의 시각 → 파일 이름의 날짜 폴더·HHMMSS (총괄 29차: 기록 08:40 인데 이름이 서버 '지금' 09:00 이던 불일치).
        동바리 확인 = 확인 시각, 신고 = 서버가 받은 시각(= timestamp), 해체 후 점검 = 점검일 + 받은 시각(기록에 날짜만 있음)."""
        if not b.get("photo_data"):
            return "", ""
        try:
            return ph.save(self.folder, ph.decode(b["photo_data"]), zid, kind, at)
        except ValueError as x:
            raise Bad(str(x))

    # ---- POST /api/measure ----
    def gate(self, zid, at):
        """타설 전 게이트 (pour_gate.py) — 판정할 수 없으면 None (게이트는 경고가 아니라 확인 목록이라 실패를 숨기지 않고 사유로)."""
        try:
            g = pg.pour_gate(self.folder, zid, at)
            return {"decision": g["decision"], "reason": g["reason"]}
        except SystemExit as x:
            return {"decision": "확인 불가", "reason": str(x)}

    def project_id(self):
        """데이터 폴더 절대 경로로 정한 프로젝트 표시 — 휴대폰 오프라인 큐가 다른 프로젝트로 가지 않게 (감사 A-07)."""
        return hashlib.sha1(os.path.normcase(os.path.abspath(self.folder)).encode("utf-8")).hexdigest()[:10]

    def measure(self, b, ua):
        if not isinstance(b, dict):
            raise Bad("요청 형식이 틀렸습니다 (JSON 객체가 아님)")
        if b.get("project_id") not in (None, "", self.project_id()):
            raise Bad("다른 프로젝트(다른 데이터 폴더)에서 입력한 기록이라 받지 않습니다 — 그 프로젝트의 현장 입력을 켜고 보내거나, 이 기기에서 지우세요")
        kind = b.get("kind")
        if kind not in KINDS:
            raise Bad(f"측정 종류 '{kind}' 은(는) 없습니다 — {list(KINDS)} 중 하나")
        cid = str(b.get("client_id") or "")
        if not CLIENT_ID.match(cid):
            raise Bad("client_id 가 없거나 형식이 틀렸습니다 (영문·숫자·-·_ 80자 이내)")
        common = {"entered_by": text(b.get("entered_by"), "입력자 이름", 20, required=True), "note": text(b.get("note"), "메모", 200),
                  "entered_at": datetime.now().strftime("%Y-%m-%dT%H:%M:%S"), "device": device_of(ua), "client_id": cid}
        with self.lock:
            if cid in self.seen:      # 오프라인 큐 재전송 → 저장하지 않고 성공으로 답한다
                return self.answer(kind, b, {"duplicate": True, "rows": 0})
            rows, info = self.validate(kind, b)
            if rows:
                append_rows(self.path(kind), KINDS[kind][2], [{**r, **common} for r in rows],
                            SAMPLE_COLS if kind == "cylinder" else POUR_COLS if kind == "pour" else PHOTO_COLS if kind in ("shore_check", "after_check") else si.EXTRA_COLS if kind == "shore_inspect" else ())
            self.seen.add(cid)
            return self.answer(kind, b, {**info, "rows": len(rows)})

    def validate(self, kind, b):
        now = self.now()
        tomorrow = datetime.combine(now.date() + timedelta(1), datetime.min.time())
        zones = {r["zone_id"]: r for r in ev.read(self.folder, "zones.csv")}
        pours = {r["zone_id"]: r for r in ev.read(self.folder, "pours.csv")}
        info = {}
        if kind == "shore_check":        # 안전관리자가 층 QR 로 '지금 동바리가 서 있는가' 확인 (해체는 협력업체가 해서 기록이 비기 쉬움)
            zid = str(b.get("zone_id") or "").strip()
            if zid not in zones:
                raise Bad(f"구역 '{zid}' 이(가) 모델에 없습니다 — 구역 타일에서 다시 고르세요")
            t = stamp(b.get("timestamp"), "확인 시각")
            if t >= tomorrow:
                raise Bad(f"확인 시각 {t:%Y-%m-%d %H:%M} 은(는) 아직 오지 않은 시각입니다")
            present = str(b.get("present") or "").strip()
            if present not in ("예", "아니오"):
                raise Bad("동바리가 서 있는지 '예' 또는 '아니오'를 고르세요")
            actor, pname = text(b.get("entered_by"), "입력자 이름", 20, required=True), text(b.get("photo"), "사진 파일 이름", 80)
            photo, sha = self.photo(b, zid, "shore_check", t)       # 사진을 올렸으면 서버 저장 경로 (F15), 아니면 예전처럼 파일 이름 글자
            if photo:
                info["photo"] = photo
            return [{"zone_id": zid, "timestamp": t.strftime("%Y-%m-%dT%H:%M"), "present": present, "actor": actor, "photo": photo or pname, ph.SHA_COL: sha}], info
        if kind == "shore_inspect":     # F16 동바리 단위 현장 점검 — 구역·단계·항목마다 OK/NG, NG 면 문제 동바리 ID (사진 없음, 사용자 11-05)
            zid = str(b.get("zone_id") or "").strip()
            if zid not in zones:
                raise Bad(f"구역 '{zid}' 이(가) 모델에 없습니다 — 평면에서 구역을 다시 고르세요")
            stage = str(b.get("stage") or "").strip()
            if stage not in si.STAGES:
                raise Bad(f"점검 단계 '{stage}' 은(는) 없습니다 — {', '.join(si.STAGES)} 중 하나")
            t = stamp(b.get("timestamp"), "점검 시각") if b.get("timestamp") else now.replace(second=0, microsecond=0)
            if t >= tomorrow:
                raise Bad(f"점검 시각 {t:%Y-%m-%d %H:%M} 은(는) 아직 오지 않은 시각입니다")
            items = b.get("items")
            if not isinstance(items, list) or not items:
                raise Bad("점검 항목을 하나 이상 보내세요 (항목마다 OK 또는 NG)")
            label = {c: n for c, n, _, _ in si.STAGES[stage]}
            shores = {k for k, v in si.read_shores(self.folder).items() if v["zone_id"] == zid}
            by, rows, seen = text(b.get("entered_by"), "입력자 이름", 20, required=True), [], set()
            for it in items:
                if not isinstance(it, dict):
                    raise Bad("점검 항목 형식이 틀렸습니다 (item_code·result·shore_ids)")
                c = str(it.get("item_code") or "").strip()
                if c not in label:
                    raise Bad(f"{stage} 항목 '{c}' 은(는) 없습니다 — 화면을 새로 고치세요")
                if c in seen:
                    raise Bad(f"{c} {label[c]} 이(가) 두 번 있습니다")
                seen.add(c)
                res = str(it.get("result") or "").strip().upper()
                if res not in ("OK", "NG"):
                    raise Bad(f"{c} {label[c]}: OK 또는 NG 를 고르세요")
                ids = it.get("shore_ids")
                ids = [] if ids is None else ids
                if not isinstance(ids, list) or len(ids) > 500:
                    raise Bad(f"{c} {label[c]}: 동바리 목록 형식이 틀렸습니다 (500개 이내)")
                ids = list(dict.fromkeys(str(x).strip() for x in ids if str(x).strip()))
                bad = [x for x in ids if x not in shores]
                if bad:
                    raise Bad(f"동바리 {', '.join(bad[:3])} 은(는) 구역 {zid} 에 없습니다 — 평면에서 다시 고르세요")
                if res == "NG" and not ids and shores:
                    raise Bad(f"{c} {label[c]} NG — 문제 동바리를 평면에서 하나 이상 고르세요")
                rows.append({"zone_id": zid, "item_code": c, "result": res, "inspector": by, "date": t.date().isoformat(), "photo_path": "",
                             "stage": stage, "shore_ids": ";".join(ids), "timestamp": t.strftime("%Y-%m-%dT%H:%M"), "source": "현장 점검"})
            return rows, info
        if kind == "pour":              # 타설 등록 (Revit [타설 등록] 창 — 휴대폰 화면엔 없음): 구역 한 줄, 이미 타설 기록이 있으면 거부 (고침은 [타설 기록 취소] 뒤 다시 — 11-12 2-1)
            zid = str(b.get("zone_id") or "").strip()
            if zid not in zones:
                raise Bad(f"구역 '{zid}' 이(가) 모델에 없습니다 — 구역을 다시 고르세요")
            if zid in pours:
                raise Bad(f"{zid} 는 이미 타설 기록이 있습니다 ({pours[zid].get('pour_date') or ''}) — 고치려면 구역 정보 창 [타설 기록 취소] 뒤 다시 등록")
            d = day(b.get("pour_date"), "타설일")
            if d >= tomorrow.date():
                raise Bad(f"타설일 {d} 은(는) 아직 오지 않은 날입니다 — 오늘({now.date()}) 이전으로")
            m = re.fullmatch(r"\s*([01]?[0-9]|2[0-3]):([0-5][0-9])\s*", str(b.get("pour_time") or ""))
            if not m:
                raise Bad(f"타설 시각 '{b.get('pour_time') or ''}' 형식이 틀렸습니다 — 08:00 처럼 넣으세요")
            mix = str(b.get("mix") or "").strip()
            if mix not in ev.e.MIXES:
                raise Bad(f"배합을 고르세요 ({' · '.join(ev.e.MIXES)})")
            fck = num(b, "fck", "설계기준강도 fck", "MPa", 18, 60)   # [프로젝트 설정] fck 칸과 같은 범위
            lot = text(b.get("pour_id"), "타설 묶음", 40)
            sup = str(b.get("supervisor_present") or "").strip()
            if sup not in ("", "예", "아니오"):
                raise Bad("타설 입회 감리는 예·아니오 중 하나입니다 (모르면 비움)")
            watcher = text(b.get("watcher"), "타설 감시자", 20)
            same = [r for r in pours.values() if (r.get("pour_id") or "").strip() == lot] if lot else []
            if same and (same[0]["pour_date"] != d.isoformat() or same[0]["mix"] != mix or float(same[0]["fck"]) != fck):   # W1: 공시체 하나로 묶음 전체를 대표 → 같은 콘크리트만
                raise Bad(f"타설 묶음 {lot} 은(는) {same[0]['pour_date']} · {same[0]['mix']} · fck {fmt(float(same[0]['fck']))} 입니다 — 같은 날·같은 배합·같은 fck 만 묶습니다 (다르면 새 묶음 번호)")
            return [{"zone_id": zid, "pour_date": d.isoformat(), "pour_time": f"{int(m.group(1)):02d}:{m.group(2)}", "mix": mix, "fck": fmt(fck), "pour_id": lot,
                     "supervisor_present": sup, "watcher": watcher}], info
        if kind == "shore_spec":        # v6 동바리 사양·설치일 (Revit [동바리 사양] 창 — 휴대폰 화면엔 없음): 층(+ 구역) 한 줄, 같은 층·구역이 이미 있으면 거부 (고침은 shore_spec.csv 직접)
            try:
                fl = int(str(b.get("floor") if b.get("floor") is not None else "").strip())
            except ValueError:
                raise Bad("층 번호가 틀렸습니다 — 층을 다시 고르세요")
            fz = sorted(z for z, r in zones.items() if int(r["floor"]) == fl)
            if not fz:
                raise Bad(f"{fl}층이 모델에 없습니다 — 층을 다시 고르세요")
            name = floor_name(fl, [zones[z] for z in fz])
            zid = str(b.get("zone_id") or "").strip()
            if zid and zid not in fz:
                raise Bad(f"구역 '{zid}' 이(가) {name} 에 없습니다 — 구역을 다시 고르세요 (비우면 층 전체)")
            have = [r for r in ev.read(self.folder, KINDS[kind][1]) if (r.get("floor") or "").strip() == str(fl) and (r.get("zone_id") or "").strip() == zid]
            typ = str(b.get("type") or "").strip()
            if typ not in sin.TYPES:
                raise Bad(f"동바리 종류를 고르세요 ({' · '.join(sin.TYPES)})")
            spec = text(b.get("spec"), "규격", 40, required=True)
            sx = num(b, "spacing_x_mm", "X 간격", "mm", *sin.SPACING_MM)
            sy = num(b, "spacing_y_mm", "Y 간격", "mm", *sin.SPACING_MM)
            n = num(b, "splices", "이음 수", "개", *sin.SPLICES)
            if n != int(n):
                raise Bad("이음 수는 0 이상 정수로 적어 주세요")
            d = day(b.get("install_date"), "동바리 설치일")
            if d >= tomorrow.date():
                raise Bad(f"동바리 설치일 {d} 은(는) 아직 오지 않은 날입니다 — 오늘({now.date()}) 이전으로")
            pdays = sorted(date.fromisoformat(pours[z]["pour_date"]) for z in ([zid] if zid else fz) if z in pours)
            if pdays and d > pdays[0]:
                raise Bad(f"동바리 설치일 {d} 이 타설일 {pdays[0]} 보다 뒤입니다 — 설치일을 확인하세요")
            if have and d.isoformat() < (have[-1].get("install_date") or ""):    # 11-12 2-1: 다시 넣기 허용 (마지막 줄을 씀) — 옛 설치일보다 앞이면 거부
                raise Bad(f"{name} {'구역 ' + zid if zid else '층 전체'} 사양 기록이 있습니다 (설치일 {have[-1].get('install_date') or ''}) — 다시 넣으려면 그 날 이후 설치일로 (마지막 줄을 씀)")
            return [{"floor": str(fl), "zone_id": zid, "type": typ, "spec": spec, "spacing_x_mm": fmt(sx), "spacing_y_mm": fmt(sy),
                     "splices": str(int(n)), "install_date": d.isoformat()}], info
        if kind == "after_check":       # 해체 후 점검 5항목 (가이드라인 4.3, F3) — 예 → OK, 아니오 → NG, 고르지 않은 항목은 줄 없음
            zid = str(b.get("zone_id") or "").strip()
            if zid not in zones:
                raise Bad(f"구역 '{zid}' 이(가) 모델에 없습니다 — 구역 타일에서 다시 고르세요")
            d = day(b.get("date"), "점검일")
            if d >= tomorrow.date():
                raise Bad(f"점검일 {d} 은(는) 아직 오지 않은 날입니다 — 오늘({now.date()}) 이전으로")
            items = b.get("items") or {}
            if not isinstance(items, dict):
                raise Bad("점검 항목 형식이 틀렸습니다 (항목 → 예/아니오)")
            by, photo = text(b.get("entered_by"), "입력자 이름", 20, required=True), text(b.get("photo"), "사진 파일 이름", 80)
            rows = []
            for code, name in ac.ITEMS.items():
                v = str(items.get(code) or "").strip()
                if not v:
                    continue
                if v not in ("예", "아니오"):
                    raise Bad(f"{code} {name}: '예' 또는 '아니오'를 고르세요")
                rows.append({"zone_id": zid, "date": d.isoformat(), "item_code": code, "result": "OK" if v == "예" else "NG", "by": by, "photo": photo})
            settle = b.get("settle") or {}       # 11-12 G9 침하 계측 {지점: mm} → item_code SET-지점, result = mm (날짜마다 반복해 넣음)
            if not isinstance(settle, dict) or len(settle) > 20:
                raise Bad("침하 계측 형식이 틀렸습니다 (지점 → mm, 20지점까지)")
            for pt in settle:
                p = str(pt).strip().upper()
                if not re.fullmatch(r"[A-Z0-9]{1,10}", p):
                    raise Bad(f"침하 측정 지점 '{pt}' — 영문·숫자 10자까지 (예: P1)")
                rows.append({"zone_id": zid, "date": d.isoformat(), "item_code": ac.SETTLE + p, "result": fmt(num(settle, pt, f"침하량 {p}", "mm", -100, 100)), "by": by, "photo": photo})
            if not rows:
                raise Bad("점검 항목을 하나 이상 고르세요 (예/아니오)")
            path, sha = self.photo(b, zid, "after_check", datetime.combine(d, self.now().time()))        # 사진 1장 → 항목 줄마다 같은 경로 (F15)
            if path:
                info["photo"] = path
                for r in rows:
                    r.update({"photo": path, ph.SHA_COL: sha})
            return rows, info
        if kind in ("cylinder", "rebound"):
            zid = str(b.get("zone_id") or "").strip()
            lot = text(b.get("pour_id"), "타설 묶음", 40) if kind == "cylinder" and not zid else ""
            if lot:                     # W1: 구역 대신 타설 묶음 — 묶음의 모든 구역 공통 시험
                lz = sorted(z for z, r in pours.items() if (r.get("pour_id") or "").strip() == lot)
                if not lz:
                    raise Bad(f"타설 묶음 '{lot}' 이(가) 타설 기록(pours.csv)에 없습니다 — [타설 등록]에서 묶음 번호를 넣거나 구역을 고르세요")
                info["lot_zones"] = lz
            elif zid not in zones:
                raise Bad(f"구역 '{zid}' 이(가) 모델에 없습니다 — 구역 타일에서 다시 고르세요")
            elif zid not in pours:
                raise Bad(f"{zid} 는 타설 기록(pours.csv)이 없습니다 — 타설일을 먼저 등록하세요")
            d = day(b.get("test_date"), "시험일")
            pd = max(date.fromisoformat(pours[z]["pour_date"]) for z in info["lot_zones"]) if lot else date.fromisoformat(pours[zid]["pour_date"])
            if d < pd:
                raise Bad(f"시험일 {d} 이 타설일 {pd} 보다 앞입니다 — 시험일을 확인하세요")
            if d >= tomorrow.date():
                raise Bad(f"시험일 {d} 은(는) 아직 오지 않은 날입니다 — 오늘({now.date()}) 이전으로")
            if kind == "cylinder":
                curing = str(b.get("curing") or "").strip()
                if curing not in CURINGS:
                    raise Bad(f"양생 방법을 고르세요 ({' · '.join(CURINGS)}) — 해체 판단 근거는 현장양생 공시체")
                lo, hi = LIMITS["mpa"]
                fs = [num(b, k, f"공시체 {i} 강도", "MPa", lo, hi, lo_open=True) for i, k in enumerate(("f1", "f2", "f3"), 1)]
                for r in ev.read(self.folder, "cylinders.csv"):   # 61차 ⑤: 같은 공시체(구역·시험일·양생·f1–f3)를 다시 넣으면 경고 없이 두 줄 → 거부 (client_id 재전송과 별개)
                    try:
                        same = [float(r.get(k) or "nan") for k in ("f1", "f2", "f3")] == fs
                    except ValueError:
                        same = False
                    if same and (r.get("zone_id") or "").strip() == zid and (zid or (r.get("pour_id") or "").strip() == lot)                             and (r.get("test_date") or "").strip()[:10] == d.isoformat() and (r.get("curing") or "").strip() == curing:
                        raise Bad(f"같은 공시체 기록이 이미 있습니다 ({zid or '타설 ' + lot} {d} {curing} {' · '.join(fmt(f) for f in fs)} MPa) — 다시 넣지 않아도 됩니다")
                # 채취 정보 (선택, F2): 타설 번호·채취 시각·레미콘 회사·차량 번호·보관 위치·사진 파일 이름 — 판정에는 안 씀
                samp = {"pour_id": lot or text(b.get("pour_id"), "타설 번호", 40), "sampled_at": "", "supplier": text(b.get("supplier"), "레미콘 회사", 40),
                        "truck_no": text(b.get("truck_no"), "차량 번호", 20), "storage_zone": text(b.get("storage_zone"), "보관 위치", 40),
                        "photo": text(b.get("photo"), "사진 파일 이름", 80)}
                if str(b.get("sampled_at") or "").strip():
                    st = stamp(b.get("sampled_at"), "채취 시각")
                    if st.date() < pd:
                        raise Bad(f"채취 시각 {st:%Y-%m-%d %H:%M} 이 타설일 {pd} 보다 앞입니다 — 공시체는 타설 때 채취합니다")
                    if st.date() > d:
                        raise Bad(f"채취 시각 {st:%Y-%m-%d %H:%M} 이 시험일 {d} 보다 뒤입니다 — 채취 시각을 확인하세요")
                    samp["sampled_at"] = st.strftime("%Y-%m-%dT%H:%M")
                path, sha = self.photo(b, zid or "타설 " + lot, "cylinder", datetime.combine(d, self.now().time()))   # 11-12 2-4 웹 #7 성적서·파단면 사진 1장 (API.md §10.2)
                if path:
                    samp.update({"photo": path, ph.SHA_COL: sha})
                    info["photo"] = path
                return [{"zone_id": zid, "test_date": d.isoformat(), "curing": curing, **{k: fmt(f) for k, f in zip(("f1", "f2", "f3"), fs)}, **samp}], info
            z = zones[zid]
            x0, y0, x1, y1 = (float(z[k]) for k in ("x0", "y0", "x1", "y1"))
            x = (x0 + x1) / 2 if b.get("x") in (None, "") else num(b, "x", "x 좌표", "m", x0, x1)
            y = (y0 + y1) / 2 if b.get("y") in (None, "") else num(b, "y", "y 좌표", "m", y0, y1)
            r = num(b, "r_value", "반발경도 R", "", *LIMITS["r_value"])
            return [{"zone_id": zid, "x": fmt(round(x, 2)), "y": fmt(round(y, 2)), "test_date": d.isoformat(), "r_value": fmt(r)}], info
        # 센서 (온도·강도)
        vcol = SENSOR_VALUE[kind]
        sid = str(b.get("sensor_id") or "").strip()
        if not ID.match(sid):
            raise Bad("센서 ID 를 넣으세요 (영문·숫자·한글·-·_ 20자 이내, 예: S7)")
        try:
            fl = int(str(b.get("floor")).strip())
        except ValueError:
            raise Bad("층을 고르세요")
        fz = [z for z in zones.values() if int(z["floor"]) == fl]
        if not fz:
            raise Bad(f"{fl}층 구역이 모델에 없습니다 — 층을 다시 고르세요")
        pdays = [date.fromisoformat(pours[z["zone_id"]]["pour_date"]) for z in fz if z["zone_id"] in pours]
        if not pdays:
            raise Bad(f"{fl}층은 타설 기록(pours.csv)이 없습니다 — 타설 후에 기록을 넣으세요")
        known = self.sensors()
        other = next((k for k in SENSOR_VALUE if k != kind and (k, sid) in known), None)
        if other:
            raise Bad(f"{sid} 는 이미 {KINDS[other][0]} ID 입니다 — 다른 ID 를 쓰거나 측정 종류를 바꾸세요")
        s = known.get((kind, sid))
        if s:
            if s["floor"] != fl:
                raise Bad(f"{sid} 는 {s['floor']}층 센서입니다 — 층을 확인하거나 새 ID 를 쓰세요")
            x, y = s["x"], s["y"]
            for k, v in (("x", x), ("y", y)):
                if b.get(k) not in (None, "") and abs(num(b, k, f"{k} 좌표", "m", -1e6, 1e6) - v) > 0.05:
                    raise Bad(f"{sid} 의 위치는 ({fmt(x)}, {fmt(y)}) m 로 등록돼 있습니다 — 센서를 옮겼으면 새 ID 로 넣으세요")
        else:
            bx = (min(float(z["x0"]) for z in fz), max(float(z["x1"]) for z in fz))
            by = (min(float(z["y0"]) for z in fz), max(float(z["y1"]) for z in fz))
            x = num(b, "x", "x 좌표", "m", *bx)
            y = num(b, "y", "y 좌표", "m", *by)
            info["new_sensor"] = True
        lo, hi = LIMITS[vcol]
        readings = b.get("readings")
        if readings is None:
            readings = [{"timestamp": b.get("timestamp"), vcol: b.get(vcol)}]
        if not isinstance(readings, list) or not readings:
            raise Bad("기록이 없습니다 — 시각과 값을 넣으세요")
        if len(readings) > MAX_READINGS:
            raise Bad(f"한 번에 {MAX_READINGS}줄까지 보낼 수 있습니다 — 나누어 붙여 넣으세요")
        label = "온도" if kind == "sensor_temp" else "추정 강도"
        unit = "°C" if kind == "sensor_temp" else "MPa"
        have = {r["timestamp"] for r in read_rows(self.path(kind)) if r["sensor_id"] == sid}
        rows, skipped, first = [], 0, min(pdays)
        for i, rd in enumerate(readings, 1):
            at = f"{i}번째 줄 " if len(readings) > 1 else ""
            if not isinstance(rd, dict):
                raise Bad(f"{at}형식이 틀렸습니다")
            t = stamp(rd.get("timestamp"), f"{at}시각")
            if t.date() < first:
                raise Bad(f"{at}시각 {t:%Y-%m-%d %H:%M} 이 {fl}층 타설일 {first} 보다 앞입니다 — 타설 전 기록은 빼고 넣으세요")
            if t >= tomorrow:
                raise Bad(f"{at}시각 {t:%Y-%m-%d %H:%M} 은(는) 아직 오지 않은 시각입니다 — 휴대폰 시계를 확인하세요")
            v = num(rd, vcol, f"{at}{label}", unit, lo, hi)
            ts = t.strftime("%Y-%m-%dT%H:%M")
            if ts in have and b.get("readings") is None:
                raise Bad(f"{sid} 는 {t:%Y-%m-%d %H:%M} 기록이 이미 있습니다 — 측정 시각을 확인하세요")
            if ts in have:            # 센서 앱의 누적 기록을 통째로 다시 붙여 넣어도 겹치지 않게
                skipped += 1
                continue
            have.add(ts)
            rows.append({"sensor_id": sid, "floor": fl, "x": fmt(x), "y": fmt(y), "timestamp": ts, vcol: fmt(v)})
        info["skipped"] = skipped
        return rows, info

    def answer(self, kind, b, info):
        """저장 후 판정 다시 계산 → 그 구역(센서는 센서가 든 구역)과 그 층 구역들."""
        m = self.model()
        fl_of = {z["zone_id"]: f for f in m["floors"] for z in f["zones"]}
        zid = str(b.get("zone_id") or "")
        if kind in SENSOR_VALUE:
            s = next((s for s in m["sensors"] if s["kind"] == kind and s["sensor_id"] == str(b.get("sensor_id", "")).strip()), None)
            f = next((f for f in m["floors"] if s and f["floor"] == s["floor"]), None)
            zid = next((z["zone_id"] for z in (f["zones"] if f else []) if z["x0"] <= s["x"] <= z["x1"] and z["y0"] <= s["y"] <= z["y1"]), "")
        f = fl_of.get(zid)
        zone = next((z for z in f["zones"] if z["zone_id"] == zid), None) if f else None
        if kind == "shore_inspect":       # F16: 저장 뒤 그 구역의 풀리지 않은 NG (모든 단계) 한 줄
            try:
                ong = si.ng_text(si.open_ng(self.folder, self.now()).get(zid, []))
            except (ValueError, KeyError, OSError):   # 손으로 고친 점검 줄 등 — 저장은 이미 됨, 이 한 줄만 비움 (코드 검토 2차)
                ong = ""
            info = {**info, "open_ng": ong}
        return {"ok": True, "saved": KINDS[kind][1], "used_in_decision": kind not in ("rebound", "shore_check", "after_check", "shore_spec"), **info, "now": m["now"],
                "zone": zone, "floor_zones": f["zones"] if f else [], "warning": m["error"]}

    # ---- GET /api/recent ----
    def recent(self, n):
        items = {}
        for kind, (label, _, cols, _) in KINDS.items():
            for r in read_rows(self.path(kind)):
                cid = r.get("client_id")
                if not cid:
                    continue
                it = items.get(cid)
                if it is None:
                    point = r.get("zone_id") or r.get("sensor_id") or ("타설 " + r["pour_id"] if r.get("pour_id") else "")   # W1 타설 묶음 공시체
                    if kind == "cylinder":
                        val = f'{r["f1"]}·{r["f2"]}·{r["f3"]} MPa ({r["curing"]})'
                    elif kind == "rebound":
                        val = f'R {r["r_value"]}'
                    else:
                        val = ""
                    it = items[cid] = {"client_id": cid, "kind": kind, "label": label, "point": point, "floor": r.get("floor", ""),
                                       "when": r.get("test_date") or r.get("timestamp") or r.get("date") or r.get("pour_date") or r.get("install_date"), "value": val, "count": 0,
                                       **{k: r.get(k, "") for k in ("entered_by", "entered_at", "note")}}
                it["count"] += 1
                if kind in SENSOR_VALUE:
                    unit = "°C" if kind == "sensor_temp" else "MPa"
                    it["when"], it["value"] = r["timestamp"], f'{r[SENSOR_VALUE[kind]]} {unit}' + (f' 외 {it["count"] - 1}줄' if it["count"] > 1 else "")
        out = sorted(items.values(), key=lambda it: it["entered_at"], reverse=True)[:n]
        return {"ok": True, "items": out}


# ---------- HTTP ----------
def make_handler(store, token):
    class H(BaseHTTPRequestHandler):
        server_version = "RevitOnSiteField/1"
        timeout = 15          # 소켓 읽기 시간 제한 s — 느린 연결이 스레드를 잡지 않게 (감사 A-20, C# FieldServer ReceiveTimeout 15000 과 같음)

        def send(self, code, body, ctype="application/json; charset=utf-8"):
            data = body if isinstance(body, bytes) else json.dumps(body, ensure_ascii=False).encode("utf-8")
            self.send_response(code)
            self.send_header("Content-Type", ctype)
            self.send_header("Content-Length", str(len(data)))
            self.send_header("Cache-Control", "no-store")
            self.send_header("X-Content-Type-Options", "nosniff")
            self.end_headers()
            self.wfile.write(data)

        def authed(self, q):
            srv = self.server                          # 토큰은 서버에 (새 주소로 바꿀 수 있게, F6)
            if hmac.compare_digest((q.get("t") or [""])[0].encode(), srv.token.encode()):
                if (datetime.now() - srv.token_issued).total_seconds() / 3600 < srv.ttl_h:
                    return True
                self.send(401, {"ok": False, "error": f"접속 주소가 만료됐습니다 ({srv.ttl_h:g}시간) — PC 의 현장 입력 창에서 [새 주소]를 눌러 새 QR 로 여세요"})
                return False
            self.send(403, {"ok": False, "error": "접속 코드가 맞지 않습니다 — PC 화면에 나온 주소(QR)로 다시 여세요"})
            return False

        def do_GET(self):
            u = urlparse(self.path)
            q = parse_qs(u.query)
            if u.path == "/favicon.ico":
                return self.send(404, b"", "text/plain")
            if u.path in ("/", "/index.html", "/view"):   # 11-12 0-7: 화면 껍데기(자료 없음)는 열쇠 없이 — 화면이 열쇠를 기기에 저장하고 주소창에서 지움 (Vercel 과 같음). 자료(/api/*)는 그대로 열쇠
                try:
                    with open(VIEW_HTML if u.path == "/view" else HTML, "rb") as f:
                        return self.send(200, f.read(), "text/html; charset=utf-8")
                except OSError as x:
                    return self.send(500, {"ok": False, "error": f"서버 오류: {x}"})
            if u.path == "/api/floor" and (q.get("t") or [""])[0]:   # F16: 휴대폰 동바리 점검 화면이 입력 토큰으로 층 평면·동바리 점을 읽음
                if not self.authed(q):
                    return
                try:
                    fl = int((q.get("f") or [""])[0])
                except ValueError:
                    return self.send(400, {"ok": False, "error": "층 번호(f)가 필요합니다"})
                try:
                    with store.lock:
                        res = store.floor_view(fl, False)
                except Exception as x:       # noqa: BLE001  (C# FieldServer 와 같은 500 문구 — 코드 검토 2차)
                    return self.send(500, {"ok": False, "error": f"서버 오류: {x}"})
                return self.send(200 if res["ok"] else 404, res)
            if u.path == "/api/floor":      # 층 화면 (F12): 보기 토큰 v (읽기 전용) 또는 신고 토큰 r (F14, 구역마다 신고 버튼) — 입력 토큰과 다름
                r = (q.get("r") or [""])[0]
                report = hmac.compare_digest(r.encode(), self.server.report_token.encode())
                if not report and not hmac.compare_digest((q.get("v") or [""])[0].encode(), self.server.view_token.encode()):
                    return self.send(403, {"ok": False, "error": "신고 주소가 맞지 않습니다 — 현장 담당자에게 신고 QR 을 다시 받으세요" if r
                                           else "보기 주소가 맞지 않습니다 — 층 표지 QR 을 다시 찍으세요"})
                try:
                    try:
                        fl = int((q.get("f") or [""])[0])
                    except ValueError:
                        return self.send(400, {"ok": False, "error": "층 번호(f)가 필요합니다 — 층 표지 QR 을 다시 찍으세요"})
                    with store.lock:
                        res = store.floor_view(fl, report)
                    return self.send(200 if res["ok"] else 404, res)
                except Exception as x:       # noqa: BLE001
                    return self.send(500, {"ok": False, "error": f"서버 오류: {x}"})
            if not self.authed(q):
                return
            try:
                if u.path == "/api/model":
                    with store.lock:
                        return self.send(200, store.model())
                if u.path == "/api/recent":
                    try:
                        n = max(1, min(int((q.get("n") or ["20"])[0]), 200))
                    except ValueError:
                        n = 20
                    with store.lock:
                        return self.send(200, store.recent(n))
                self.send(404, {"ok": False, "error": "없는 주소입니다"})
            except Exception as x:       # noqa: BLE001 — 휴대폰에 원인을 보여 준다
                self.send(500, {"ok": False, "error": f"서버 오류: {x}"})

        def do_POST(self):
            u = urlparse(self.path)
            q = parse_qs(u.query)
            try:
                n = int(self.headers.get("Content-Length") or 0)
            except ValueError:
                n = -1
            raw = self.rfile.read(n) if 0 < n <= MAX_BODY else b""   # R7 (회귀 2회차): 거부(403·401·404)도 본문을 다 읽은 뒤 답 — 안 읽고 닫으면 보낸 쪽은 403 대신 연결 끊김
            if u.path == "/api/report":         # 협력업체 해체 신고 (F14): 신고 토큰 r 만 — 보기·입력 토큰으로는 403
                if not hmac.compare_digest((q.get("r") or [""])[0].encode(), self.server.report_token.encode()):
                    return self.send(403, {"ok": False, "error": "신고 주소가 맞지 않습니다 — 현장 담당자에게 신고 QR 을 다시 받으세요"})
            elif not self.authed(q):
                return
            elif u.path != "/api/measure":
                return self.send(404, {"ok": False, "error": "없는 주소입니다"})
            if n <= 0 or n > MAX_BODY:
                return self.send(413 if n > MAX_BODY else 400, {"ok": False, "error": "보낸 내용이 없거나 너무 큽니다"})
            try:
                b = json.loads(raw.decode("utf-8"))
            except (UnicodeDecodeError, json.JSONDecodeError):
                return self.send(400, {"ok": False, "error": "요청 형식이 틀렸습니다 (JSON 아님)"})
            try:
                ua = self.headers.get("User-Agent", "")
                self.send(200, store.report(b, ua) if u.path == "/api/report" else store.measure(b, ua))
            except Bad as x:
                self.send(400, {"ok": False, "error": str(x)})
            except PermissionError as x:   # Windows: Excel 이 연 CSV (공유 위반) · 읽기 전용 — C# FieldServer 와 같은 문구
                sys.stderr.write(f"POST 오류(파일 잠김): {x}\n")
                self.send(500, {"ok": False, "error": "서버 오류 — 저장되지 않았습니다: 데이터 파일이 다른 프로그램(Excel 등)에서 열려 있거나 쓸 수 없습니다 — PC 에서 그 파일을 닫고 다시 보내세요"})
            except Exception as x:       # noqa: BLE001
                self.send(500, {"ok": False, "error": f"서버 오류 — 저장되지 않았을 수 있습니다: {x}"})

        def log_message(self, fmt_, *args):      # 토큰이 든 주소를 로그에 남기지 않는다
            sys.stderr.write(f"{self.address_string()} {self.command} {urlparse(self.path).path} {args[1] if len(args) > 1 else ''}\n")
    return H


def lan_ip():
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        s.connect(("10.255.255.255", 1))     # 패킷은 나가지 않음 — 기본 경로의 내 주소만 얻는다
        return s.getsockname()[0]
    except OSError:
        return "127.0.0.1"
    finally:
        s.close()


def serve(folder, at=None, host=None, port=8765, token=None, default_fck=None, prev_at=None, ttl_h=TOKEN_TTL_H):
    """서버를 만들어 돌려준다 (serve_forever 는 부른 쪽이). 시험에서 port=0 → 빈 포트.
    host 기본 = 이 PC 의 첫 LAN 주소 하나 (공용망·테더링에 같이 열리지 않게, 감사 A-19 · F6). 모든 망에 열려면 "0.0.0.0".
    접속 주소(토큰)는 ttl_h 시간 뒤 만료 — new_token(httpd) 로 새 주소."""
    httpd = ThreadingHTTPServer((host or lan_ip(), port), make_handler(Store(folder, at, default_fck, prev_at), None))
    httpd.ttl_h = ttl_h
    new_token(httpd, token)
    httpd.view_token = view_token(folder)        # 읽기 전용 보기 토큰 (F12) — 만료 없음, 데이터 폴더에 고정
    httpd.report_token = view_token(folder, REPORT_FILE)   # 협력업체 해체 신고 토큰 (F14) — 같은 방식, 보기 토큰과 다름
    return httpd


def new_token(httpd, token=None):
    """접속 주소(토큰)를 새로 — 만료 시각도 지금부터 다시. 옛 QR 은 403."""
    httpd.token, httpd.token_issued = token or secrets.token_urlsafe(12), datetime.now()
    return httpd.token


if __name__ == "__main__":
    for _s in (sys.stdout, sys.stderr):   # 한국어 Windows 콘솔(cp949)엔 — 같은 글자가 없어 시작하자마자 죽던 것 (11-04)
        try:
            _s.reconfigure(errors="replace")
        except (AttributeError, ValueError):
            pass
    ap = argparse.ArgumentParser(description="Revit On-Site 현장 입력 서버")
    ap.add_argument("--data", required=True, help="데이터 폴더 (zones.csv, pours.csv …)")
    ap.add_argument("--at", help="이 시각을 '지금'으로 (데모) YYYY-MM-DDTHH:MM")
    ap.add_argument("--host", help="열 주소 — 기본 = 이 PC 첫 LAN 주소 하나, 모든 망에 열려면 0.0.0.0")
    ap.add_argument("--port", type=int, default=8765)
    ap.add_argument("--prev", help="그 전 판정 시각 YYYY-MM-DDTHH:MM — 알림 '상태 바뀜' 기준 (없으면 데이터 폴더 eval_log.csv 마지막 판정)")
    ap.add_argument("--fck", type=float, help="프로젝트 기본 fck MPa (PRJ_DefaultFck) — pours.csv 값과 다르면 구역 경고")
    a = ap.parse_args()
    if not os.path.exists(os.path.join(a.data, "zones.csv")):
        raise SystemExit(f"{a.data} 에 zones.csv 가 없습니다")
    srv = serve(a.data, datetime.fromisoformat(a.at) if a.at else None, a.host, a.port, default_fck=a.fck, prev_at=datetime.fromisoformat(a.prev) if a.prev else None)
    ip = lan_ip() if a.host in (None, "0.0.0.0") else a.host
    print(f"현장 입력 서버 — 데이터 {os.path.abspath(a.data)}" + (f", 기준 시각 {a.at}" if a.at else ""))
    print(f"휴대폰(같은 Wi-Fi)에서 열기:  http://{ip}:{srv.server_address[1]}/?t={srv.token}")
    print(f"층 보기(읽기 전용, 반장·협력업체):  http://{ip}:{srv.server_address[1]}/view?v={srv.view_token}&f=<층 번호>")
    print(f"협력업체 해체 신고(반장에게 따로):  http://{ip}:{srv.server_address[1]}/view?r={srv.report_token}&f=<층 번호>")
    print("Windows 방화벽 창이 뜨면 '개인 네트워크' 허용을 눌러 주세요. 끄려면 Ctrl+C.")
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass

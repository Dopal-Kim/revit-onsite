"""CSV → 구역별 동바리 해체 판정표. Dynamo 05_Evaluate와 Claude 에이전트가 같은 계산을 쓰도록 엔진을 감싼 명령행 도구.

입력 폴더 (UTF-8, 첫 줄 열 이름):
  zones.csv      zone_id, floor, x0, y0, x1, y1 [, slab_mm — 모델 슬래브 두께, 선택 열 (D1 하중비교, 없으면 210 [가정])]
                 [, shore_mm — 동바리 높이 (애드인이 모델에서 잼, 선택 열 — v6.2 W11 범위 밖 알림만)]
  pours.csv      zone_id, pour_date(YYYY-MM-DD), pour_time(HH:MM — 비면 23:59 로 보아 재령을 짧게, 보수 측), mix, fck  (구역마다 한 줄)
                 [, pour_id — 같이 친 구역 묶음 (선택, v6.2 W1)]
  sensors.csv    sensor_id, floor, x, y, timestamp(YYYY-MM-DDTHH:MM), temp_c     (없어도 됨 → 기상 예측)
  cylinders.csv  zone_id, test_date, curing, f1, f2, f3                          (없어도 됨 → "공시체 시험 필요")
                 [, pour_id — zone_id 가 비면 그 타설 묶음의 모든 구역 공통 시험 (W1, cyl_rows)]
  weather.csv    date, avgTa                                                     (없어도 됨 → 평년 기온)
  sensor_strength.csv sensor_id, floor, x, y, timestamp, mpa  무선 강도 센서(장비가 성숙도로 계산한 MPa) (없어도 됨)
  rebound.csv    zone_id, x, y, test_date, r_value  반발경도 — 저장·조회만, 판정에 쓰지 않음 [가정]
위치 근거 = 온도 센서 IDW 구역 최소와 강도 센서 IDW 구역 최소 중 낮은 값 (둘 다 없으면 기상 예측).
실행: python evaluate.py <입력 폴더> --at 2026-12-08T09:00 [--out zone_decisions.csv] [--cure-min 5]
"""
import argparse
import copy
import csv
import hashlib
import json
import os
import re
import statistics
from datetime import date, datetime, timedelta, timezone
from functools import wraps

import engine as e
import install_rules as ir


KST = timezone(timedelta(hours=9))


def floor_order(folder):
    """사람이 읽는 결과물의 구역 순서 키 (총괄 11-05): 층 번호 → 구역 ID (B1 → 1F … 9F → 10F … PH, 글자 순 '10F < 9F' 를 막음).
    zones.csv 에 없는 구역은 맨 뒤 (ID 순)."""
    fl = {}
    for z in read(folder, "zones.csv"):
        try:
            fl[z["zone_id"]] = int(z["floor"])
        except (KeyError, TypeError, ValueError):
            pass
    return lambda zid: (0, fl[zid], zid) if zid in fl else (1, 0, zid)


def kdate(s):
    """인쇄물 날짜 = 행정업무규정 제7조 표기 "2026. 12. 31." (월·일 앞 0 없음). ISO 가 아니면 그대로 — 데이터·CSV 는 ISO 그대로 (ui_reference_research 7절 #22)."""
    t = str(s or "")[:10]
    p = t.split("-")
    if len(p) != 3 or not all(x.isdigit() for x in p):
        return str(s or "")
    return f"{int(p[0])}. {int(p[1])}. {int(p[2])}."


def kstamp(s):
    """인쇄물 일시 "2026. 12. 31. 09:00" — 날짜만이면 kdate."""
    t = str(s or "")
    return kdate(t[:10]) + " " + t[11:16] if len(t) >= 16 and t[10] in "T " else kdate(t)


def ts(s):
    """센서 시각 → 현지(KST) 시각, 시간대 없는 값. 로거가 '2026-12-31T10:00+09:00' 처럼 내보내도 읽는다 (감사 A-15)."""
    t = datetime.fromisoformat(s.strip())
    return t.astimezone(KST).replace(tzinfo=None) if t.tzinfo else t


def checked(fn):
    """CSV 값 오류(빈 숫자 칸 등)를 SystemExit 문구로 — 현장 입력 서버가 통째로 500 이 되지 않게 (감사 A-15).
    shortcut: 행 번호는 안 알려 줌, 현장에서 자주 틀리는 열이 보이면 그 열만 행 번호 점검 추가."""
    @wraps(fn)
    def run(*a, **kw):
        try:
            return fn(*a, **kw)
        except (ValueError, TypeError, KeyError) as x:
            raise SystemExit(f"입력 CSV 값을 읽을 수 없음 ({type(x).__name__}: {x}) — 빈 숫자 칸·글자·날짜 형식을 확인하세요")
    return run


def install_rows(folder):
    """install_inspection.csv 중 설치 검측 줄만 (stage 빈칸 = 예전 줄 = 설치 검측). F16 일상 점검·해체 전·해체 후 줄은 뺌 —
    근거 원장 1 설치검측·설치 검측 타설 뒤 경고·이력서·동바리 상태·연직도가 씀 (shore_inspect.py)."""
    return [r for r in read(folder, "install_inspection.csv") if (r.get("stage") or "").strip() in ("", "설치 검측")]


VOID, VOIDABLE = "void:", ("pours.csv", "cylinders.csv")   # 11-12 2-1 기록 취소: client_id 가 void: 로 시작하는 줄 = 앞의 같은 자료 줄 하나를 지움 (추가만 원칙)
META = ("entered_by", "entered_at", "device", "note", "client_id")


def _data(r):
    return tuple((k, v or "") for k, v in r.items() if k is not None and k not in META)


def read(folder, name):
    p = os.path.join(folder, name)
    if not os.path.exists(p):
        return []
    with open(p, encoding="utf-8-sig", newline="") as f:
        rows = list(csv.DictReader(f))
    if name not in VOIDABLE:
        return rows
    out = []
    for r in rows:
        if not (r.get("client_id") or "").startswith(VOID):
            out.append(r)
            continue
        k = _data(r)
        for i in range(len(out) - 1, -1, -1):     # 가장 최근의 같은 줄 하나
            if _data(out[i]) == k:
                del out[i]
                break
    return out


def void_row(folder, name, row, reason, actor, now):
    """기록 취소 줄을 이어 쓴다 (원래 줄은 그대로): 자료 열은 원래 줄 그대로, note = '[취소] 사유', client_id = void:시각.
    타설은 요청·승인 기록(approvals.csv)이 있는 구역이면 거부 — 판정 사슬이 이미 그 타설을 근거로 씀."""
    reason, actor = (reason or "").strip(), (actor or "").strip()
    if name not in VOIDABLE:
        raise ValueError(f"{name} 은(는) 취소할 수 없습니다 — {' · '.join(VOIDABLE)} 만")
    if not 1 <= len(reason) <= 100 or not actor:
        raise ValueError("취소 사유(1–100자)와 입력자를 넣으세요")
    if _data(row) not in {_data(r) for r in read(folder, name)}:
        raise ValueError("취소할 기록이 없습니다 — 이미 취소했거나 파일이 바뀌었습니다")
    zid = row.get("zone_id") or ""
    if name == "pours.csv" and any(r.get("zone_id") == zid for r in read(folder, "approvals.csv")):
        raise ValueError(f"{zid} 는 요청·승인 기록이 있어 타설 기록을 취소할 수 없습니다 — 감리와 협의해 정정 문서로")
    p = os.path.join(folder, name)
    with open(p, encoding="utf-8-sig", newline="") as f:
        head = next(csv.reader(f))
    if "note" not in head or "client_id" not in head:
        raise ValueError(f"{name} 첫 줄에 note·client_id 열이 없습니다 — 현장 입력 한 번 뒤 다시")
    meta = {"entered_by": actor, "entered_at": now.strftime("%Y-%m-%dT%H:%M:%S"), "device": "", "note": "[취소] " + reason, "client_id": VOID + now.strftime("%Y%m%d%H%M%S")}
    vals = [meta[c] if c in META else (row.get(c) or "") for c in head]
    with open(p, "rb") as f:
        f.seek(0, 2)
        nl = f.tell() > 0 and (f.seek(-1, 2) or True) and f.read(1) not in (b"\n", b"\r")   # 엑셀 저장 파일은 끝 줄바꿈이 없을 수 있음
    with open(p, "a", encoding="utf-8", newline="") as f:
        if nl:
            f.write("\r\n")
        csv.writer(f).writerow(vals)
    return vals


LOT_TAG = re.compile(r"공시체 \(타설 (.+?) · (\d+)구역 공통\)")   # W1 사유 태그 → 요청서·검토서·이력서 표기 (lot_label)


def cyl_rows(folder):
    """cylinders.csv → 구역 줄 (v6.2 W1 공시체 타설 로트). zone_id 가 비고 pour_id 가 있는 줄 = pours.csv 에서 그 pour_id 를 가진
    모든 구역에 같은 시험 (줄마다 cyl_lot = "P-9F-1 · 6구역 공통"). 같은 구역·같은 시험일에 구역 자기 줄이 있으면 자기 줄만 (자기 줄 우선).
    자기 줄은 cyl_lot 빈칸. pours.csv 에 없는 pour_id·zone_id 와 pour_id 가 다 빈 줄은 뺌 (판정엔 '공시체 시험 결과 없음' — 보수 측)."""
    rows = read(folder, "cylinders.csv")
    lots = {}
    for p in read(folder, "pours.csv"):
        if (p.get("pour_id") or "").strip():
            lots.setdefault(p["pour_id"].strip(), []).append(p["zone_id"])
    own = [dict(r, cyl_lot="") for r in rows if (r.get("zone_id") or "").strip()]
    have = {(r["zone_id"], r["test_date"]) for r in own}
    out = list(own)
    for r in rows:
        lot = (r.get("pour_id") or "").strip()
        if (r.get("zone_id") or "").strip() or not lot:
            continue
        zs = lots.get(lot, [])
        out += [dict(r, zone_id=z, cyl_lot=f"{lot} · {len(zs)}구역 공통") for z in zs if (z, r["test_date"]) not in have]
    return out


def lot_label(row):
    """판정표 행 → '타설 P-9F-1 · 6구역 공통' (공시체가 타설 묶음 공통 시험일 때, 아니면 빈칸) — 사유 태그에서 (W1)"""
    m = LOT_TAG.search(row.get("reason") or "")
    return f"타설 {m.group(1)} · {m.group(2)}구역 공통" if m else ""


# ---- 큰 자료 캐시 (감사 A-20: 센서 26만 줄에서 휴대폰 저장 1건 13 s — 저장마다 evaluate 를 몇 번씩 다시 돌림) ----
# 입력 파일 열쇠 = (크기, 수정 시각 ns) + 1 MB 미만 파일은 내용 SHA-1 (같은 크기로 빨리 고쳐 써도 알아챔).
# shortcut: 1 MB 이상 파일을 같은 크기로 같은 시각 틱 안에 고쳐 쓰면 못 알아챔 — 그런 일이 생기면 큰 파일도 해시로.
INPUTS = ("zones.csv", "pours.csv", "sensors.csv", "weather.csv", "cylinders.csv", "sensor_strength.csv", "options.json", "install_inspection.csv", "approvals.csv", "shore_checks.csv")   # 승인·신고 = 11-11 재받침 표시   # 설치 검측 = F16 규칙 ⑦
SMALL_FILE = 1 << 20
_MEMO = {}           # (함수, 폴더, 인자, 입력 파일 열쇠) → 결과 (돌려줄 땐 깊은 사본 — 부른 쪽이 고쳐도 캐시는 그대로)
_SENSORS = {}        # (폴더, sensors.csv 열쇠) → _Sensors


def file_key(folder, name):
    p = os.path.join(folder, name)
    try:
        st = os.stat(p)
    except OSError:
        return None
    if st.st_size < SMALL_FILE:
        with open(p, "rb") as f:
            return st.st_size, st.st_mtime_ns, hashlib.sha1(f.read()).hexdigest()
    return st.st_size, st.st_mtime_ns


def memo(*names):
    """같은 인자·같은 입력 파일(names)이면 앞 계산 결과를 다시 씀 — 결과 바이트 그대로 (판정 규칙 안 바꿈, A-20)."""
    def deco(fn):
        @wraps(fn)
        def run(folder, *a, **kw):
            key = (fn.__qualname__, os.path.abspath(folder), a, tuple(sorted(kw.items())), tuple(file_key(folder, n) for n in names))
            if key not in _MEMO:
                if len(_MEMO) >= 256:
                    _MEMO.clear()
                _MEMO[key] = fn(folder, *a, **kw)
            return copy.deepcopy(_MEMO[key])
        return run
    return deco


class _Sensors:
    """sensors.csv 를 한 번만 읽고, 층·센서별 묶음과 시각 순 (시각, 온도) 를 한 번만 만들어 둠 — 파일이 같으면 다음 판정·경고도 다시 씀.
    묶음 순서·값·계산 순서는 예전 구역마다 다시 읽던 것과 같음 (판정표 바이트 그대로)."""
    def __init__(self, folder):
        self.rows = read(folder, "sensors.csv")
        self._floor, self._sid, self._pts, self._te = None, None, {}, {}

    def floor(self, fl):
        """{센서 ID: 그 층 줄들} — 파일에 처음 나온 순서 (예전 by_id 와 같음)"""
        if self._floor is None:
            g = {}
            for r in self.rows:
                g.setdefault(int(r["floor"]), {}).setdefault(r["sensor_id"], []).append(r)
            self._floor = g
        return self._floor.get(fl, {})

    def by_sid(self):
        """{센서 ID: 모든 줄} (층 무관, cold_checks)"""
        if self._sid is None:
            g = {}
            for r in self.rows:
                g.setdefault(r["sensor_id"], []).append(r)
            self._sid = g
        return self._sid

    def pts(self, key, rows):
        if key not in self._pts:
            self._pts[key] = sorted((ts(r["timestamp"]), float(r["temp_c"])) for r in rows)
        return self._pts[key]

    def te(self, fl, sid, start, at, ea, air, air_key):
        k = (fl, sid, start, at, ea, air_key)
        if k not in self._te:
            self._te[k] = te_from_points(self.pts(("f", fl, sid), self._floor[fl][sid]), start, at, ea, air)
        return self._te[k]

    def stale(self, fl, sid, at):
        d = (at - self.pts(("f", fl, sid), self._floor[fl][sid])[-1][0]).total_seconds() / 86400
        return d if d * 24 > MAX_GAP_H else 0.0


def sensors_of(folder):
    key = (os.path.abspath(folder), file_key(folder, "sensors.csv"))
    if key not in _SENSORS:
        if len(_SENSORS) >= 4:
            _SENSORS.clear()
        _SENSORS[key] = _Sensors(folder)
    return _SENSORS[key]


MAX_GAP_H = 6.0   # [가정] 센서 기록 사이가 이보다 길면 끊김으로 보고 그 뒤는 기상 온도로 채움 (A-04: 수화열 35 ℃ 가 몇 주 이어지는 거짓 안전)


def sensor_te(rows, start, at, ea, air=None):
    """센서 온도 기록(시각 순) → start~at 등가재령(일). 구간 온도 = 앞 측정값, 단 MAX_GAP_H 넘는 공백은
    MAX_GAP_H 까지만 앞 측정값, 나머지는 air(날짜) 기상 온도(양생 하한 없음). air 없으면 평년 기온."""
    return te_from_points(sorted((ts(r["timestamp"]), float(r["temp_c"])) for r in rows), start, at, ea, air)


def te_from_points(pts, start, at, ea, air=None):
    """sensor_te 의 계산 (시각 순 (시각, 온도) 를 받음 — _Sensors 가 한 번 푼 것을 다시 씀)."""
    air = air or e.air_temp
    pts = [p for p in pts if p[0] <= at]
    te, gap = 0.0, timedelta(hours=MAX_GAP_H)
    for (t0, T0), (t1, _) in zip(pts, pts[1:] + [(at, None)]):
        a, b = max(t0, start), min(t1, at)
        if b <= a:
            continue
        held = min(b, max(a, t0 + gap))
        te += e.age_factor(T0, ea) * (held - a).total_seconds() / 86400
        while held < b:                                   # 끊긴 구간: 하루 단위로 기상 온도
            nxt = min(b, datetime.combine(held.date() + timedelta(1), datetime.min.time()))
            te += e.age_factor(air(held.date()), ea) * (nxt - held).total_seconds() / 86400
            held = nxt
    return te


def stale_days(rows, at):
    """마지막 기록 뒤 판정 시각까지 공백(일). MAX_GAP_H 이하면 0."""
    last = max(ts(r["timestamp"]) for r in rows)
    d = (at - last).total_seconds() / 86400
    return d if d * 24 > MAX_GAP_H else 0.0


def daily_temps(weather, start, days):
    w = {r["date"]: float(r["avgTa"]) for r in weather}
    return [w.get((start + timedelta(i)).isoformat(), e.air_temp(start + timedelta(i))) for i in range(days)]


def weather_tag(wmap, start, days, label="기상 기록"):
    """daily_temps 가 실제로 쓴 기온의 출처 (감사 A-26): weather.csv 기록 [입력] / 평년 기온 [가정] / 섞임.
    label = weather.csv source 열이 한 값이면 그 출처(예: 기상청 ASOS 108 서울 — weather_import.py), 아니면 '기상 기록'."""
    n = sum((start + timedelta(i)).isoformat() in wmap for i in range(days))
    if n and n == days:
        return f"{label} [입력]"
    return f"{label} {n}일 [입력]·평년 기온 {days - n}일 [가정]" if n else "평년 기온 [가정]"


def weather_label(weather):
    """weather.csv 행들의 source 가 모두 같은 비지 않은 값이면 그 값(끝 ' [입력]' 뗌), 아니면 '기상 기록' — source 열 없는 옛 파일은 그대로."""
    src = {(r.get("source") or "").strip() for r in weather}
    one = src.pop() if len(src) == 1 else ""
    return one.removesuffix(" [입력]") if one else "기상 기록"


SENSOR_MARGIN_M = 3.0   # [가정] 구역 경계에서 이 거리 안 센서만 그 구역 IDW 에 씀 (동바리 간격 1.2 m 의 약 2.5배)


def near(points, rect, margin=SENSOR_MARGIN_M):
    """points [(id, x, y, 값)] 중 구역 rect 를 margin 만큼 넓힌 범위 안의 것. 하나도 없으면 층 전체를 쓰고 표시를 붙인다
    (멀리 떨어진 센서 하나가 층 전체 구역을 정하지 않게 — 2026-11-04 모바일 입력 시험에서 발견)."""
    x0, y0, x1, y1 = rect
    inside = [p for p in points if x0 - margin <= p[1] <= x1 + margin and y0 - margin <= p[2] <= y1 + margin]
    return (inside, "") if inside else (points, " (가까운 센서 없음 — 층 전체 센서)")


RULE_BASIS = "판정 규칙 근거: 해체 가이드라인 3.2 (국토교통부 2023.1) · KCS 14 20 12:2024 3.3.1·3.3.2 · 지지층 14 MPa·하중비 ≤ 1.0"   # 고정 근거 조항 한 줄 (대시보드 판정 근거 창·휴대폰 구역 카드, 총괄 P2) — 판정 사유 문자열에는 안 넣음

FRESH_H = 24.0   # [가정] 타설 후 이 시간 안 슬래브는 지지층에 넣지 않고 하중으로 봄 (가이드라인 하중 경우 = 타설 순간)


EARLY_FORM_MPA_KEY = "필러 동바리 조기 탈형"   # engine.thresholds: 14 MPa [확정 — 정의서 §3.2, 가이드라인: 구조계산 + 14 MPa 이상]


def early_form(f_est, f_cyl, fck):
    """판넬(슬래브 거푸집) 조기 탈형 — 필러 동바리를 남긴 채 판넬만 먼저 떼는 단계 (매 사이클 D+3–4 결정, field_process_research §1 #9).
    강도 ①(구역 최소 추정과 현장양생 공시체(있으면) 중 낮은 값 ≥ 14 MPa)만 본다 → "14 MPa 도달 — 판넬 조기 탈형 4조건 검토 대상" / "14 MPa 미달 — 판넬 존치".
    '판넬 탈형 요청 가능'은 staged.stages 4조건(① 14 MPa ② 10 ℃ 이상 4일 ③ 위층 하중비 ④ 구조검토서) 결과만 쓴다 (33차: 판정 근거 창 두 줄이 서로 달랐음). 동바리 해체 판정과 별개."""
    lim = e.thresholds(fck)[EARLY_FORM_MPA_KEY]
    f = min(f_est, f_cyl) if f_cyl is not None else f_est
    basis = "공시체·추정 중 낮은 값" if f_cyl is not None else "추정만 — 공시체 없음"
    if f >= lim:
        return f"{lim:g} MPa 도달 — 판넬 조기 탈형 4조건 검토 대상 ({f:.1f} ≥ {lim:g} MPa, {basis})"
    return f"{lim:g} MPa 미달 — 판넬 존치 ({f:.1f} < {lim:g} MPa, {basis})"


def overlaps(a, b):
    """두 구역 rect (x0, y0, x1, y1) 가 면적 > 0 으로 겹치나."""
    return min(a[2], b[2]) > max(a[0], b[0]) and min(a[3], b[3]) > max(a[1], b[1])


OPTIONS = "options.json"   # 데이터 폴더의 판정 선택 (애드인 [프로젝트 설정] 이 씀) — {"calibrate_due": true} 이면 예측일에 현장 보정 곡선 (G2)
CAL_MIN = 3                # [가정] 현장 보정에 필요한 현장양생 공시체 시험 수 (총괄 11-05 G2) — 모자라면 기본 곡선


def options(folder):
    """options.json 전체 (없음·못 읽음·객체가 아님 = None) — 아래 option·*_option 이 모두 이것으로 읽음"""
    try:
        with open(os.path.join(folder, OPTIONS), encoding="utf-8-sig") as f:
            o = json.load(f)
    except (OSError, ValueError):
        return None
    return o if isinstance(o, dict) else None


def option(folder, key):
    """options.json 의 값 하나 (없으면 None)"""
    return (options(folder) or {}).get(key)


def num_option(folder, key, default):
    """options.json 의 양수 숫자 하나 (v6.2 [가정] 값 — patrol_hours·scope_slab_max_mm 등, [프로젝트 설정]이 씀). 참거짓·글자·0 이하·없음·못 읽음 = default"""
    v = option(folder, key)
    return v if isinstance(v, (int, float)) and not isinstance(v, bool) and v > 0 else default


def text_option(folder, key):
    """options.json 글자 하나 (11-12 시공사·감리단 — 서류 머리 자동 입력), 글자가 아니면 빈칸"""
    v = option(folder, key)
    return " ".join(v.split()) if isinstance(v, str) else ""


LIVE_KN_M2 = 2.5   # 시공하중 kN/m² — 가이드라인 하중비교 예제 1.6·2.5 · KDS 21 50 00 §1.6 작업하중 최소 (install_rules.live_load) — 이보다 크게만 바꿈


def live_load(folder):
    """하중비교 식의 시공하중 (v6.2 W12) — options.json live_load_kn_m2 (자재 적재 등 [가정]), 없거나 2.5 미만이면 2.5 (낮추지 않음 — 보수 측)"""
    return max(LIVE_KN_M2, num_option(folder, "live_load_kn_m2", LIVE_KN_M2))


SPECIAL_KINDS = ("캔틸레버", "차양", "지하주차장")   # 가이드라인 3.2.11(2)⑥: fck 100% 확인 전 해체 금지 부위 — 프로그램이 자동으로 못 가려냄


def special_zones(folder):
    """options.json special_zones {구역: 부위} (11-11 G4·Revit #24, [구역 정보]에서 사람이 고름). 모르는 부위·형식은 무시"""
    v = option(folder, "special_zones")
    return {str(z): k for z, k in v.items() if k in SPECIAL_KINDS} if isinstance(v, dict) else {}


def roles(folder):
    """options.json actor_roles {이름: 역할} (11-12 정보 출처 — 서류 '기본 사항·출처' 표의 입력자 옆 역할, [프로젝트 설정]이 씀). 빈 역할은 뺌"""
    v = option(folder, "actor_roles")
    return {str(k): r for k, r in v.items() if isinstance(r, str) and r.strip()} if isinstance(v, dict) else {}


SDL_KN_M2, LL_KN_M2 = 1.64, 2.0   # 가이드라인 2.1.2(2) 예제 "설계하중은 가정치 적용" — 지지 슬래브 마감하중·설계활하중


def support_loads(folder):
    """지지층 여유 하중 (11-11 G1): options.json support_sdl_kn_m2 · support_ll_kn_m2 (구조계산서 값), 없으면 예제값 → (sdl, ll, 예제값인가)"""
    sdl, ll = num_option(folder, "support_sdl_kn_m2", SDL_KN_M2), num_option(folder, "support_ll_kn_m2", LL_KN_M2)
    return sdl, ll, (sdl, ll) == (SDL_KN_M2, LL_KN_M2)


def support_note(folder):
    """판정 근거 창·요청서 근거 줄 — 지지층 여유 하중의 출처"""
    sdl, ll, ex = support_loads(folder)
    return f"지지층 여유 1.2×{sdl:g} + 1.6×{ll:g} kN/m²" + (" — 가이드라인 예제값 [가정] (구조계산서 값을 [프로젝트 설정]에)" if ex else " (구조계산서)")


def live_note(folder):
    """하중 줄 꼬리표 (W12) — 판정 근거 창·조기 탈형 검토서"""
    return f"시공하중 {live_load(folder):g} kN/m² [가정] — 자재 적재·장비 집중하중 미반영"


def top_option(folder):
    """options.json top_floor (정수만 — 참거짓·소수·글자는 없음으로) — 애드인 [구역 나누기]가 인식된 맨 위 구조 슬래브 층으로 씀 (총괄 78차 ④).
    없거나 읽지 못하면 None (게이트 항상 적용 — 예전 결과 바이트 그대로)."""
    v = option(folder, "top_floor")
    return v if isinstance(v, int) and not isinstance(v, bool) else None


TOP_AUTO_TAG = "최상층 자동 판단 [가정] — 확인 필요"
# v6.2 W11 범위 밖 알림: 두꺼운 슬래브·지하층·높은 동바리는 재받침·층간 하중 분배를 이 도구가 계산하지 않음 → 구조기술자 검토 안내 (결론은 그대로)
SCOPE_SLAB_MM = 300   # [가정 — 팀 결정, 규정 값 아님] options.json scope_slab_max_mm 로 바꿈. 동바리 높이 = install_rules.PE_H_MM (5 m, 건진법 시행령 제101조의2 — 기준 한 곳)
SHORE_H_LAW = "건설기술진흥법 시행령 제101조의2"
SCOPE_TAG = "⚠ 범위 밖 가능"
SCOPE_TAIL = "구조기술자 검토 (재받침·층간 하중 분배 미계산)"
SCOPE_RE = re.compile(re.escape(SCOPE_TAG) + r" \(.*?\) — " + re.escape(SCOPE_TAIL))


def scope_note(row):
    """판정표 행 → 사유 속 '⚠ 범위 밖 가능 (…) — 구조기술자 검토 (…)' 한 토막 (없으면 빈칸) — 요청서 현장 경고·판정 근거 창·구역 정보"""
    m = SCOPE_RE.search(row.get("reason") or "")
    return m.group(0) if m else ""   # 총괄 83차: 사람이 확인하지 않은 최상층(자동값)으로 게이트를 통과한 구역 사유 끝 (결론은 그대로)


def top_auto(folder):
    """options.json top_floor_auto 가 true (= [구역 나누기]가 넣은 자동값 그대로, 사람이 확인 안 함). 없거나 읽지 못하면 False."""
    return option(folder, "top_floor_auto") is True


def calibrate_due_on(folder):
    """데이터 폴더 options.json 의 calibrate_due (없거나 읽지 못하면 False — 기본 꺼짐, 예전 결과 바이트 그대로)."""
    return option(folder, "calibrate_due") is True


LAPSE_C_PER_M = 0.0065   # [가정] 기온감률 0.65 ℃/100 m (v6 사용자 확정 — 층 높이 보정, 표준대기 값)
STORY_M = 3.0            # [가정] 층고 m — options.json "story_m" 으로 바꿈


def height_option(folder):
    """options.json 층 높이 보정 (v6) → (켬, 층고 m, 노출 보정 ℃). {"height_correct": true, "story_m": 3.0, "exposure_c": 0.0}
    없거나 읽지 못하면 꺼짐 — 예전 결과 바이트 그대로. 노출 보정 기본 0 [가정, 값 결정 대기]."""
    o = options(folder)
    try:
        return (o.get("height_correct") is True, float(o.get("story_m", STORY_M)), float(o.get("exposure_c", 0.0)))
    except (ValueError, AttributeError, TypeError):   # 파일 없음(o = None)·글자 값 → 꺼짐
        return (False, STORY_M, 0.0)


def height_dt(fl, opt):
    """층 fl 슬래브의 기온 보정 ℃ (기상 기온에 더함, ≤ 0): 높이 = 층 × 층고 (지하 0) → −0.65 ℃/100 m × 높이 − 노출 보정(지상 층). 꺼짐이면 0."""
    on, story, expo = opt
    return 0.0 if not on else -LAPSE_C_PER_M * max(0, fl) * story - (expo if fl >= 1 else 0.0)


def height_tag(dt):
    return f" · 층 높이 보정 {dt:+.2f} °C [가정]"


def calibration(folder, at, cure_min=5.0):
    """현장양생 공시체로 배합별 강도 곡선 s 보정 (benchmark G2 — 예측 해체 가능일에만 씀, 판정 게이트·결론·f_est_min 은 그대로)
    → {mix: (s 중앙값 또는 None, 쓴 시험 수)}. 같은 데이터 폴더(= 같은 현장)·같은 배합의 판정 시각 이전 현장양생 공시체 시험마다:
    등가재령 te = 타설일~시험일 일수만큼 predict 와 같은 기온 모델(weather.csv / 평년, 양생 하한 cure_min), 강도 = 3개 평균
    → engine.calibrate_s (0 < te < 28 만 — 28 이상은 식이 맞지 않음), s ≤ 0(조기 강도가 28일 값 이상 — 곡선 모양이 안 맞음)은 뺌 [가정].
    쓴 시험이 CAL_MIN 건 미만이면 s = None (기본 곡선 [가정])."""
    pours = {r["zone_id"]: r for r in read(folder, "pours.csv")}
    weather = read(folder, "weather.csv")
    hopt, floor_of = height_option(folder), {r["zone_id"]: int(r["floor"]) for r in read(folder, "zones.csv")}   # v6 층 높이 보정 (꺼지면 0)
    pts, seen = {}, set()
    for r in cyl_rows(folder):
        p = pours.get(r["zone_id"])
        if p is None or r.get("curing") not in e.FIELD_CURINGS or date.fromisoformat(r["test_date"]) > at.date():
            continue
        if r["cyl_lot"]:                                  # W1: 타설 묶음 공통 시험은 보정 자료 1건 (구역 수만큼 세지 않음)
            k = (r["cyl_lot"], r["test_date"], r["curing"], r["f1"], r["f2"], r["f3"])
            if k in seen:
                continue
            seen.add(k)
        cast = date.fromisoformat(p["pour_date"])
        days = (date.fromisoformat(r["test_date"]) - cast).days
        f = sum(float(r[k]) for k in ("f1", "f2", "f3")) / 3
        if days <= 0 or f <= 0:
            continue
        ea = e.MIXES[p["mix"]][1]
        dt = height_dt(floor_of.get(r["zone_id"], 0), hopt)
        te = e.equivalent_age([max(T + dt, cure_min) for T in daily_temps(weather, cast, days)], ea)
        s = e.calibrate_s(f, te, float(p["fck"]))
        if s is not None and s > 0:
            pts.setdefault(p["mix"], []).append(s)
    return {mix: (statistics.median(ss) if len(ss) >= CAL_MIN else None, len(ss)) for mix, ss in pts.items()}


def evaluate(folder, at, cure_min=5.0, top_floor=None, calibrate=None):
    """구역별 판정표 행 (본체 = _evaluate_full, 같은 입력이면 캐시)."""
    return _evaluate_full(folder, at, cure_min, top_floor, calibrate)["rows"]


def gate_details(folder, at, cure_min=5.0, top_floor=None, calibrate=None):
    """구역 → 규칙 게이트 세부값 {above: 위로 연속 타설된 층 수, topped: 최상층까지 타설, fresh: 맨 위 슬래브가 24 h 안,
    supports: [(층, 그 층 최소 구역, 강도 MPa)], ratio: 하중비, lim: 지지 슬래브 기준} — 판정 근거 창 비교 줄용 (34차 A). 판정표와 같은 계산·같은 캐시."""
    return _evaluate_full(folder, at, cure_min, top_floor, calibrate)["gate"]


@memo(*INPUTS)
@checked
def _evaluate_full(folder, at, cure_min=5.0, top_floor=None, calibrate=None):
    """evaluate 본체 → {"rows": 판정표 행, "gate": gate_details}.
    top_floor: 건물 최상층(지붕 슬래브) 번호. 그 층까지 타설이 끝나면 위에서 내려오는 타설 하중이 없으므로
    존치 층수 게이트를 통과로 본다 [가정 — 팀 해석: KCS 14 20 12 3.3.2(2) 의 3개층 존치를 상부 타설 하중 분담 목적으로 읽음,
    원문에 최상층 예외 문구는 없음]. 강도·공시체 기준은 그대로 적용. None 이면 데이터 폴더 options.json top_floor
    (애드인 [구역 나누기] 기본값 = 인식된 맨 위 구조 슬래브 층, 총괄 78차), 그것도 없으면 게이트 항상 적용.
    규칙 게이트 (구역마다, 2026-11-04 내부 감사 A-01·02·03·06):
      ① 판정 시각 이전 타설만 셈 (pours.csv 의 계획 행은 판정에서 뺌)
      ② 이 구역과 평면이 겹치는 위층 구역이 모두 타설된 층을, 층 번호 산술이 아니라 층 순서로 셈 (지하 −1 → 지상 1 경계)
      ③ 그 층들이 다음 타설을 받칠 때 각 ≥ 14 MPa, 하중비 ≤ 1.0 — 강도는 판정 시각 구역 최소 추정 (실제 타설 땐 더 큼, 안전 측) [가정]
    calibrate: 예측 해체 가능일(predicted_due)에 현장 보정 곡선(calibration) — None 이면 데이터 폴더 options.json (기본 꺼짐).
      켜면 바뀌는 것은 predicted_due 와 사유 끝 '예측일: …' 태그뿐 (G2)."""
    if calibrate is None:
        calibrate = calibrate_due_on(folder)
    auto_top = False
    if top_floor is None:
        top_floor = top_option(folder)
        auto_top = top_floor is not None and top_auto(folder)
    zones = {r["zone_id"]: r for r in read(folder, "zones.csv")}
    pour_rows = read(folder, "pours.csv")
    pours = {r["zone_id"]: r for r in pour_rows}
    dup = sorted({r["zone_id"] for r in pour_rows if sum(x["zone_id"] == r["zone_id"] for x in pour_rows) > 1})
    if dup:                                                   # A-24: 말없이 마지막 행을 쓰지 않는다
        raise SystemExit(f"pours.csv 에 같은 구역이 두 번 있음: {dup} — 구역마다 타설 한 줄 (재타설이면 옛 줄을 지우세요)")
    weather = read(folder, "weather.csv")
    sens, air_key = sensors_of(folder), file_key(folder, "weather.csv")   # 센서는 한 번 읽어 둔 것 (A-20)
    strength = read(folder, "sensor_strength.csv")
    # 입력 점검: 사람이 엑셀로 만든 CSV라서 흔한 실수를 먼저 알려 준다
    if not zones or not pours:
        raise SystemExit(f"{folder} 에 zones.csv 와 pours.csv 가 있어야 합니다")
    bad = sorted(z for z in pours if z not in zones)
    if bad:
        raise SystemExit(f"pours.csv 의 zone_id 가 zones.csv 에 없음: {bad}")
    bad = sorted({p["mix"] for p in pours.values()} - set(e.MIXES))
    if bad:
        raise SystemExit(f"pours.csv 의 mix 값 {bad} 은 쓸 수 없음. 가능한 값: {list(e.MIXES)}")
    wmap = {r["date"]: float(r["avgTa"]) for r in weather}
    wlabel = weather_label(weather)
    hopt = height_option(folder)                             # v6 층 높이 보정 — 기상 기온으로 셈하는 강도(참고)·예측일만, 센서는 그대로
    air = lambda d: wmap.get(d.isoformat(), e.air_temp(d))  # noqa: E731
    cyl, ignored = {}, {}
    for r in cyl_rows(folder):                               # W1: 타설 묶음 공통 시험 → 묶음의 구역마다
        if date.fromisoformat(r["test_date"]) > at.date():
            continue
        if r.get("curing") not in e.FIELD_CURINGS:          # A-05: 표준양생 등은 기준 근거로 안 씀
            ignored[r["zone_id"]] = ignored.get(r["zone_id"], 0) + 1
            continue
        prev = cyl.get(r["zone_id"])
        avg = lambda x: sum(float(x[k]) for k in ("f1", "f2", "f3")) / 3  # noqa: E731
        # 최신 시험, 같은 날 두 건이면 낮은 쪽 (A-24: 행 순서로 높은 값이 이기지 않게)
        if prev is None or r["test_date"] > prev["test_date"] or (r["test_date"] == prev["test_date"] and avg(r) < avg(prev)):
            cyl[r["zone_id"]] = r
    start_of = {z: datetime.fromisoformat(f'{p["pour_date"]}T{p.get("pour_time") or "23:59"}') for z, p in pours.items()}
    done = {z for z, t in start_of.items() if t <= at}
    rect_of = {z: tuple(float(r[k]) for k in ("x0", "y0", "x1", "y1")) for z, r in zones.items()}
    floor_of = {z: int(r["floor"]) for z, r in zones.items()}
    order = sorted(set(floor_of.values()))                  # 층 순서 (0 없는 번호도 순번으로)
    has_slab = any("slab_mm" in r for r in zones.values())  # D1: 애드인이 모델 슬래브 두께를 쓴 zones.csv (선택 열)
    slab_of = {z: float(r["slab_mm"]) for z, r in zones.items() if (r.get("slab_mm") or "").strip()}
    shore_of = {z: float(r["shore_mm"]) for z, r in zones.items() if (r.get("shore_mm") or "").strip()}   # W11 동바리 높이 (선택 열)
    smax = num_option(folder, "scope_slab_max_mm", SCOPE_SLAB_MM)
    live = live_load(folder)                                 # W12 시공하중 (기본 2.5 — 예전 결과 그대로)
    sdl, ll, _ = support_loads(folder)                       # 11-11 G1 지지층 여유 하중 (기본 = 예제값, 예전 결과 그대로)

    # 1단계: 타설된 구역마다 위치 근거(구역 최소 추정)
    est = {}
    for zid in sorted(done, key=lambda z: (floor_of[z], z)):
        p, fl, rect, start = pours[zid], floor_of[zid], rect_of[zid], start_of[zid]
        fck, mix = float(p["fck"]), p["mix"]
        s, ea = e.MIXES[mix]
        age = (at - start).total_seconds() / 86400
        by_id = sens.floor(fl)
        ests = []                                            # 위치 근거 후보 (강도, 위치, 출처) → 가장 낮은 값
        if by_id:
            pts, tail = near([(sid, float(rs[0]["x"]), float(rs[0]["y"]), sens.te(fl, sid, start, at, ea, air, air_key))
                              for sid, rs in by_id.items()], rect)
            gaps = [f"{sid} {sens.stale(fl, sid, at):.0f}일" for sid, *_ in pts if sens.stale(fl, sid, at)]
            if gaps:
                tail += f" · 기록 끊김 {', '.join(gaps)} → 기상 온도"
            ests.append(e.zone_min_strength(pts, {zid: rect}, fck, mix)[zid] + (f"센서 {len(pts)}개 IDW{tail}",))
        # 강도 센서: 이 타설 이후 ~ 판정 시각까지 센서마다 마지막 값 → IDW
        last = {}
        for r in strength:
            t = ts(r["timestamp"])
            if int(r["floor"]) == fl and start <= t <= at and (r["sensor_id"] not in last or t >= last[r["sensor_id"]][0]):
                last[r["sensor_id"]] = (t, float(r["x"]), float(r["y"]), float(r["mpa"]))
        if last:
            v, tail = near([(sid, a[1], a[2], a[3]) for sid, a in last.items()], rect)
            ests.append(e.zone_min_from_values([(a[1], a[2]) for a in v], [a[3] for a in v], rect)
                        + (f"강도 센서 {len(v)}개 IDW{tail}",))
        near_ok = any("가까운 센서 없음" not in a[2] for a in ests)   # 구역 3 m 안 센서 = 위치 근거 (없으면 추정은 참고, 정의서 §3.4-3 · 34차)
        dt, wx = height_dt(fl, hopt), not ests or all("가까운 센서 없음" in a[2] for a in ests)   # wx = 기상 기온을 씀
        if ests and wx:
            # A-10: 3 m 안 센서가 없으면 먼 센서 IDW 만 믿지 않고 기상 예측(보온 하한 없이)과 비교해 낮은 값
            temps = [T + dt for T in daily_temps(weather, start.date(), int(age))]
            ests.append((e.strength_at(e.equivalent_age(temps, ea), fck, s), "", "기상 예측(하한 없음)"))
        if ests:
            f_est, xy, src = min(ests, key=lambda a: a[0])
            if len(ests) > 1:
                src = "·".join(("온도 " if a[2].startswith("센서") else "") + f"{a[2]} {a[0]:.1f}" for a in ests) + " 중 최소"
        else:
            temps = [max(T + dt, cure_min) for T in daily_temps(weather, start.date(), int(age))]
            f_est, xy = e.strength_at(e.equivalent_age(temps, ea), fck, s), ""
            src = f"기상 예측(센서 없음) — {weather_tag(wmap, start.date(), int(age), wlabel)}, 양생 하한 {cure_min:g} °C [가정]"   # A-26 출처 태그
        if hopt[0] and wx:   # 기상 기온을 썼을 때만
            src += height_tag(dt)
        if not near_ok:
            src += " · 참고 (판정에 안 씀)"
        est[zid] = (f_est, xy, src, age, near_ok)

    # 2단계: 구역마다 규칙 게이트 → 판정
    cal = calibration(folder, at, cure_min) if calibrate else {}
    out, gate_d = [], {}
    import shore_inspect as si                              # 늦은 import (shore_inspect 가 evaluate 를 씀)
    ins_ng = si.open_ng(folder, at, si.INSTALL)              # F16 규칙 ⑦: 풀리지 않은 설치 검측 NG
    import approval                                          # 늦은 import (approval 이 evaluate 를 늦게 import)
    reshored = {z for z, c in approval.status_override(folder, at).items() if c == 8}   # 11-11 G3: 재받침 기록 구역
    special = special_zones(folder)
    for zid in sorted(done, key=lambda z: (floor_of[z], z)):
        p, fl, rect, start = pours[zid], floor_of[zid], rect_of[zid], start_of[zid]
        fck, mix = float(p["fck"]), p["mix"]
        f_est, xy, src, age, near_ok = est[zid]
        thr = e.thresholds(fck)["다층 동바리 해체"]
        stack = []                                           # 이 구역 위로 연속 타설된 층 [(층, 겹치는 구역들)]
        for f in order[order.index(fl) + 1:]:
            ov = [z for z in zones if floor_of[z] == f and overlaps(rect_of[z], rect)]
            if not ov or not all(z in done for z in ov):
                break
            stack.append((f, ov))
        highest = stack[-1][0] if stack else fl
        topped_out = top_floor is not None and highest >= top_floor
        level_ok = len(stack) >= e.MIN_LEVELS or topped_out
        c = cyl.get(zid)
        f_cyl = sum(float(c[k]) for k in ("f1", "f2", "f3")) / 3 if c else None
        t400 = [T + height_dt(fl, hopt) for T in daily_temps(weather, start.date(), 400)]   # 한 번만 만든다 · v6 층 높이 보정 (꺼지면 0)
        s_cal, n_cal = cal.get(mix, (None, 0))               # G2 현장 보정 곡선 (꺼져 있으면 cal = {} → 기본 곡선, 예전과 같음)
        need = next((a for a in range(1, 400) if e.predict(mix, start.date(), a, fck, cure_min, t400, s_cal) >= thr), None)
        if need is None:                                    # A-28: StopIteration 대신 고칠 곳을 알려 준다
            raise SystemExit(f"{zid}: 400일 안에 기준 {thr:g} MPa 에 도달 안 함 (강도 예측) — weather.csv 기온·양생 최저 온도·fck 입력을 확인하세요")
        gate, sup_msg = ("OK(최상부)" if topped_out and len(stack) < e.MIN_LEVELS else "OK") if level_ok else "NG", ""
        gd = {"above": len(stack), "topped": topped_out, "fresh": None, "supports": [], "ratio": None, "lim": None}
        if level_ok and not topped_out:                     # ③ 지지층 검토
            # 맨 위 슬래브가 타설 후 FRESH_H 안이면 그 슬래브는 지지층이 아니라 하중 → 그 아래층들이 받치는지,
            # 아니면 맨 위까지 다음 타설을 받치는 지지층 (강도는 판정 시각 값, 보수 측)
            fresh = (at - max(start_of[z] for z in stack[-1][1])).total_seconds() / 3600 < FRESH_H
            sup = [min(est[z][0] for z in ov) for _f, ov in (stack[:-1] if fresh else stack)]
            # D1: 하중 슬래브 두께 = 이 구역과 평면이 겹치는 이 층·위층 구역의 zones.csv slab_mm 최댓값 (두꺼울수록 하중이 커서 보수 측),
            # 없으면 가이드라인 예제 210 mm [가정]. slab_mm 열이 없는 zones.csv 는 예전과 바이트 같게 (문구 태그 없음)
            t_mm = [slab_of[z] for z in zones if z in slab_of and floor_of[z] >= fl and overlaps(rect_of[z], rect)]
            ratio, fmin = e.load_check(sup, fck, sdl, ll, t_slab=max(t_mm) / 1000, live=live) if t_mm else e.load_check(sup, fck, sdl, ll, live=live)
            lim = e.thresholds(fck)["지지 슬래브"]
            gd.update(fresh=fresh, ratio=ratio, lim=lim, supports=[(sf, min(ov, key=lambda z: est[z][0]), min(est[z][0] for z in ov))
                                                                   for sf, ov in (stack[:-1] if fresh else stack)])
            if fmin < lim or ratio > 1:
                gate = "NG(지지층)"
                sup_msg = "; ".join(x for x in (
                    f"다음 타설 때 지지층 최소 {fmin:.1f} < {lim:.0f} MPa" if fmin < lim else None,
                    f"하중비 {ratio:.2f} > 1.0" if ratio > 1 else None) if x)
                if has_slab:
                    sup_msg += f" — 하중 슬래브 {max(t_mm):g} mm (모델)" if t_mm else " — 하중 슬래브 210 mm [가정]"
        if gate == "NG":
            decision, reason = "존치", f"위로 {len(stack)}개층만 타설됨 (타설층 포함 {e.MIN_LEVELS}개층 존치 필요)"
        elif sup_msg:
            decision, reason = "존치", sup_msg + " (KCS 14 20 12 지지 슬래브)"
        else:
            decision = e.zone_decision(f_est if near_ok else None, f_cyl, thr)     # 3 m 안 센서가 없으면 공시체만 (34차)
            reason = "; ".join(x for x in (
                None if f_cyl is None or f_cyl >= thr else f"공시체 평균 {f_cyl:.2f} < {thr:.0f} MPa",
                None if not near_ok or f_est >= thr else f"구역 최소 추정 {f_est:.2f} < {thr:.0f} MPa" + (f" (좌표 {xy})" if xy else ""),   # 센서 없으면 좌표 없음 (20차)
                ("공시체 시험 결과 없음" + (f" (현장양생 아님 {ignored[zid]}건 무시)" if zid in ignored else ""))
                if f_cyl is None else None) if x) or "기준 충족"
            if c and c["cyl_lot"]:                          # W1 근거: 이 구역 공시체 = 타설 묶음 공통 시험
                reason += f"; 공시체 (타설 {c['cyl_lot']}) {f_cyl:.1f} MPa"
        if decision == "해체 승인 요청 가능" and zid in ins_ng:   # F16 ⑦: 설치 검측 NG 가 풀리지 않으면 해체 요청 불가 (결론은 '결함 기록'까지)
            decision, reason = "존치 (설치 결함 기록)", "설치 검측 결함 기록 미해결 — " + si.ng_text(ins_ng[zid])
        if gate == "OK(최상부)" and auto_top:                # 83차: 자동 최상층으로만 게이트를 넘은 구역 — 확인 필요 (결론 그대로)
            reason += "; " + TOP_AUTO_TAG
        out_of = [x for x in (                              # W11 범위 밖 (결론 그대로)
            f"슬래브 {slab_of[zid]:g} mm > {smax:g} mm [가정]" if zid in slab_of and slab_of[zid] > smax else None,
            "지하층" if fl < 0 else None,
            f"동바리 높이 {shore_of[zid] / 1000:.2f} m ≥ {ir.PE_H_MM / 1000:g} m ({SHORE_H_LAW})" if zid in shore_of and shore_of[zid] >= ir.PE_H_MM else None,
            f"{special[zid]} — fck 100% 확인 (가이드라인 3.2.11)" if zid in special else None,
            "재받침 기록 — 단순 하중비교 범위 밖 (가이드라인 2.1.2(2)①)" if zid in reshored or any(z in reshored for _f, ov in stack for z in ov) else None) if x]
        if out_of:
            reason += f"; {SCOPE_TAG} ({' · '.join(out_of)}) — {SCOPE_TAIL}"
        if calibrate:                                       # 예측일 근거 태그 (G2) — 꺼져 있으면 사유 그대로
            reason += ("; " + f"예측일: 현장 보정 곡선 (공시체 {n_cal}건, s={s_cal:.3f}) [보정]" if s_cal is not None
                       else f"; 예측일: 기본 곡선 [가정] (보정 자료 부족 — 공시체 {n_cal}건 < {CAL_MIN})")
        out.append({"zone_id": zid, "floor": fl, "pour_date": p["pour_date"], "age_days": round(age, 1), "mix": mix,
                    "fck": fck, "threshold": thr, "f_cyl": "" if f_cyl is None else round(f_cyl, 1),
                    "cyl_test_date": c["test_date"] if c else "", "cyl_curing": c["curing"] if c else "",
                    "f_est_min": round(f_est, 1), "f_est_xy": xy, "est_source": src,
                    "level_gate": gate, "decision": decision, "reason": reason,
                    "predicted_due": (start.date() + timedelta(need)).isoformat(),
                    "early_form": early_form(f_est, f_cyl, fck)})
        gate_d[zid] = gd
    return {"rows": out, "gate": gate_d}


COLD_MEAN_C = 4.0        # [확정] KCS 14 20 40:2024 1.1(2) 일평균기온 4 ℃ 이하 → 한중 콘크리트
INIT_MPA, INIT_MIN_C = 5.0, 5.0   # [확정] 같은 기준 3.4.1(4)·표 3.4-1(보통 노출): 5 MPa 까지 콘크리트 온도 5 ℃ 이상
AFTER_DAYS, AFTER_MIN_C = 2, 0.0  # [확정] 3.4.1(4): 5 MPa 도달 뒤 2일간 0 ℃ 이상


@memo("zones.csv", "pours.csv", "weather.csv", "sensors.csv")
@checked
def cold_checks(folder, at):
    """한중 콘크리트 초기양생 점검 → 경고 목록 [{zone_id, floor, kind, at, sensor_id, temp_c, text}].
    판정(evaluate)은 바꾸지 않는 경고만 낸다 — 초기 동해가 의심되면 적산온도 추정 강도를 믿기 어려우므로
    공시체·코어로 확인하라는 뜻(KCS 14 20 40 3.5(2) 해체 시기는 현장 공시체 또는 적산온도 추정 강도).
    센서마다 그 센서 온도로 등가재령을 쌓아 5 MPa 도달 시각을 구하고, 그 전 5 ℃ 미만 / 그 뒤 2일 0 ℃ 미만을 찾는다."""
    zones = {r["zone_id"]: r for r in read(folder, "zones.csv")}
    pours = {r["zone_id"]: r for r in read(folder, "pours.csv")}
    weather = {r["date"]: float(r["avgTa"]) for r in read(folder, "weather.csv")}
    sens = sensors_of(folder)                       # 센서는 한 번 읽어 둔 것 (A-20)
    by_id = sens.by_sid()
    out = []
    for zid, p in sorted(pours.items(), key=lambda kv: (int(zones[kv[0]]["floor"]), kv[0])):
        z, fl = zones[zid], int(zones[zid]["floor"])
        start = datetime.fromisoformat(f'{p["pour_date"]}T{p.get("pour_time") or "23:59"}')
        if start > at:
            continue
        s, ea = e.MIXES[p["mix"]]
        fck = float(p["fck"])
        d0 = start.date()
        mean = weather.get(d0.isoformat(), e.air_temp(d0))
        if mean <= COLD_MEAN_C:
            out.append({"zone_id": zid, "floor": fl, "kind": "한중", "at": start.isoformat(timespec="minutes"),
                        "sensor_id": "", "temp_c": round(mean, 1),
                        "text": f"{zid}: 타설일 일평균기온 {mean:.1f} ℃ ≤ {COLD_MEAN_C:g} ℃ → 한중 콘크리트 대상 (KCS 14 20 40 1.1(2))"})
        rect = tuple(float(z[k]) for k in ("x0", "y0", "x1", "y1"))
        pts, _ = near([(sid, float(rs[0]["x"]), float(rs[0]["y"]), rs) for sid, rs in by_id.items()
                       if int(rs[0]["floor"]) == fl], rect)
        for sid, _x, _y, rows in pts:
            recs = [(t, T) for t, T in sens.pts(("s", sid), rows) if start <= t <= at]   # 센서마다 한 번만 풀어 둠
            te, t5, low, frost = 0.0, None, None, None
            for (t0, T0), (t1, _) in zip(recs, recs[1:] + [(at, None)]):
                if t5 is None:
                    if T0 < INIT_MIN_C and low is None:
                        low = (t0, T0)
                    te += e.age_factor(T0, ea) * max((t1 - t0).total_seconds(), 0) / 86400
                    if e.strength_at(te, fck, s) >= INIT_MPA:
                        t5 = t1
                elif t0 <= t5 + timedelta(AFTER_DAYS) and T0 < AFTER_MIN_C and frost is None:
                    frost = (t0, T0)
            if low:
                out.append({"zone_id": zid, "floor": fl, "kind": "초기양생 5℃", "at": low[0].isoformat(timespec="minutes"),
                            "sensor_id": sid, "temp_c": low[1],
                            "text": f"{zid}: {INIT_MPA:g} MPa 도달 전 센서 {sid} {low[1]:.1f} ℃ < {INIT_MIN_C:g} ℃ "
                                    f"({low[0]:%m-%d %H:%M}) — 초기 동해 의심, 공시체로 확인 (KCS 14 20 40 3.4.1(4))"})
            if frost:
                out.append({"zone_id": zid, "floor": fl, "kind": "도달 후 2일 0℃", "at": frost[0].isoformat(timespec="minutes"),
                            "sensor_id": sid, "temp_c": frost[1],
                            "text": f"{zid}: {INIT_MPA:g} MPa 도달 후 {AFTER_DAYS}일 안 센서 {sid} {frost[1]:.1f} ℃ < {AFTER_MIN_C:g} ℃ "
                                    f"({frost[0]:%m-%d %H:%M}) — KCS 14 20 40 3.4.1(4)"})
    return out


CROSS_TOL = 0.25   # [가정] 공시체 평균과 센서 구역 최소 추정이 이 비율 넘게 다르면 경고 (docs/field_process_research.md 공시체 위조·지연)


def cross_checks(folder, at, tol=CROSS_TOL):
    """공시체–센서 교차 검사 → 경고 목록 [{zone_id, floor, kind, f_cyl, f_est, diff, text}]. 판정은 바꾸지 않는다.
    현장양생 공시체(기준 근거)와 센서 위치 근거가 크게 어긋나면 시편 바꿔치기·보관 위치·온도 차·센서 이상 중 하나 —
    사진·해시로 못 잡는 '같은 사람이 시편을 바꾸고 사진도 찍는' 경우의 유일한 신호라서 감리에게 보여 준다.
    센서가 없는 구역(기상 예측만)은 비교하지 않는다 (센서 없는 기본형에서는 이 경고가 없음)."""
    out = []
    for r in evaluate(folder, at):
        if r["f_cyl"] == "" or r["est_source"].startswith("기상"):
            continue
        a, b = float(r["f_cyl"]), float(r["f_est_min"])
        if b <= 0:
            continue
        d = (a - b) / b
        if abs(d) > tol:
            out.append({"zone_id": r["zone_id"], "floor": r["floor"], "kind": "공시체-센서 불일치", "f_cyl": a, "f_est": b,
                        "diff": round(d, 3),
                        "text": f"{r['zone_id']}: 공시체 평균 {a:.1f} MPa ↔ 센서 구역 최소 {b:.1f} MPa ({d:+.0%}) — "
                                f"시편 보관 위치·온도·시험 기록과 센서를 확인 (차 {tol:.0%} 넘음 [가정])"})
    return out


CYL_RANGE = 0.15   # [가정] 공시체 3개 (최대 − 최소) 가 평균의 이 비율을 넘으면 확인 문구 (감사 A-29, 규정 값 아님)


@checked
def input_checks(folder, at, default_fck=None, rng=CYL_RANGE):
    """입력값 개연성 점검 (감사 A-29) → 경고 목록 [{zone_id, floor, kind, text}]. 판정은 바꾸지 않는다.
    ① pours.csv fck 가 프로젝트 기본 fck(PRJ_DefaultFck, 주면)와 다름 — 오기면 해체 기준(= fck)이 같이 바뀐다
    ② 판정에 쓰는 공시체 시험(현장양생, 판정 시각까지 최신 날짜)의 3개 범위 > 평균 × rng [가정]"""
    zones = {r["zone_id"]: r for r in read(folder, "zones.csv")}
    cyl = [r for r in cyl_rows(folder)
           if r.get("curing") in e.FIELD_CURINGS and date.fromisoformat(r["test_date"]) <= at.date()]
    out = []
    for p in sorted(read(folder, "pours.csv"), key=lambda r: (int(zones[r["zone_id"]]["floor"]), r["zone_id"])):
        zid, fl, fck = p["zone_id"], int(zones[p["zone_id"]]["floor"]), float(p["fck"])
        if default_fck is not None and fck != default_fck:
            out.append({"zone_id": zid, "floor": fl, "kind": "fck 확인",
                        "text": f"{zid}: pours.csv fck {fck:g} MPa ≠ 프로젝트 기본 fck {default_fck:g} MPa — "
                                f"설계 강도를 확인하세요 (해체 기준은 pours.csv 값)"})
        mine = [r for r in cyl if r["zone_id"] == zid]
        last = max((r["test_date"] for r in mine), default=None)
        for r in mine:
            v = [float(r[k]) for k in ("f1", "f2", "f3")]
            avg, spread = sum(v) / 3, max(v) - min(v)
            if r["test_date"] == last and spread > rng * avg:
                out.append({"zone_id": zid, "floor": fl, "kind": "공시체 편차",
                            "text": f"{zid}: 공시체 {last} 3개 범위 {spread:.1f} MPa > 평균 {avg:.1f} 의 {rng:.0%} [가정] — "
                                    f"시편·시험 기록을 확인하세요 (판정은 그대로)"})
    return out


def write(rows, path):
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0]))
        w.writeheader()
        w.writerows(rows)


if __name__ == "__main__":
    import sys as _sys; [_s.reconfigure(errors="replace") for _s in (_sys.stdout, _sys.stderr) if hasattr(_s, "reconfigure")]   # cp949 콘솔에서 — 등으로 죽지 않게
    ap = argparse.ArgumentParser()
    ap.add_argument("folder")
    ap.add_argument("--at", required=True, help="판정 시각 YYYY-MM-DDTHH:MM")
    ap.add_argument("--out", default="zone_decisions.csv")
    ap.add_argument("--cure-min", type=float, default=5.0)
    ap.add_argument("--top-floor", type=int, help="건물 최상층 번호 (그 층까지 타설되면 존치 층수 게이트 통과)")
    ap.add_argument("--calibrate", action="store_true", help="예측 해체 가능일에 현장양생 공시체 보정 곡선 (없으면 데이터 폴더 options.json, 기본 꺼짐)")
    a = ap.parse_args()
    rows = evaluate(a.folder, datetime.fromisoformat(a.at), a.cure_min, a.top_floor, True if a.calibrate else None)
    write(rows, a.out)
    for r in rows:
        print(r["zone_id"], r["decision"], "|", r["reason"])

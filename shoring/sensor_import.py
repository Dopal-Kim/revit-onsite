"""센서 업체 내보내기 CSV 가져오기 (benchmark_gap G5, 총괄 11-05) — 현장에서 센서 기록을 손으로 옮기지 않게.

업체 내보내기 CSV (CP949·UTF-8, 열 이름이 업체마다 다름) → 데이터 폴더 sensors.csv(온도)·sensor_strength.csv(강도 열이 있으면).
열 찾기 (이름 일부, 왼쪽부터 처음 맞는 열):
  강도 = 강도·mpa·strength · 온도 = 온도·temp·°c·℃ · 시각 = 시각·일시·날짜·시간 또는 낱말 time·date·datetime·timestamp ·
  층 = 층 또는 낱말 floor · 센서 = 센서 또는 낱말 sensor·serial·device·id·sn · x·y = 열 이름이 그대로 x / y (x(m)·x[m]·x_m 도)
  --map "열 이름=역할" (역할: sensor_id·timestamp·temp_c·mpa·floor·x·y) 로 직접 지정하면 그 열이 먼저.
시각: 2026-11-26 8:00 · 2026/11/26 08:00:00 · 2026.11.26T08:00 → 2026-11-26T08:00 (초는 버림). 못 읽으면 그 줄은 건너뜀(셈).
위치(floor·x·y)가 파일에 없으면 기존 sensors.csv·sensor_strength.csv 의 같은 sensor_id 마지막 위치 — 처음 보는 ID 는 가져오지 않고 목록("위치 없음").
같은 (sensor_id, timestamp) 는 한 줄 (값이 다르면 새 값으로). 판정·계산 없음 (판정은 다음 [판정] 때).
실행: python sensor_import.py <업체 CSV> <데이터 폴더> [--map 열=역할 …]   ·   자체 검사: --self-test
"""
import argparse
import csv
import io
import math
import os
import re
from datetime import datetime

import weather_import as wi

ROLES = ("sensor_id", "timestamp", "temp_c", "mpa", "floor", "x", "y")
FILES = (("temp_c", "sensors.csv"), ("mpa", "sensor_strength.csv"))
SOURCE = "업체 내보내기 CSV"
TS = re.compile(r"^(\d{4})[-/.](\d{1,2})[-/.](\d{1,2})[ T](\d{1,2}):(\d{2})(?::\d{2}(?:\.\d+)?)?$")


def role_of(h):
    """열 이름 → 역할 (없으면 None). 값 역할(강도·온도)을 먼저 봐서 '센서 온도' 같은 이름은 온도로."""
    l = h.strip().lower()
    toks = [t for t in re.split(r"[^0-9a-z가-힣]+", l) if t]
    if l in ("x", "x(m)", "x[m]", "x_m"):
        return "x"
    if l in ("y", "y(m)", "y[m]", "y_m"):
        return "y"
    if "강도" in l or "mpa" in l or "strength" in l:
        return "mpa"
    if "온도" in l or "temp" in l or "°c" in l or "℃" in l:
        return "temp_c"
    if any(k in l for k in ("시각", "일시", "날짜", "시간")) or any(t in ("time", "date", "datetime", "timestamp") for t in toks):
        return "timestamp"
    if "층" in l or "floor" in toks:
        return "floor"
    if "센서" in l or any(t in ("sensor", "serial", "device", "id", "sn") for t in toks):
        return "sensor_id"
    return None


def columns(head, mapping=None):
    """→ {역할: 열 번호}. mapping {열 이름: 역할} 이 먼저, 나머지는 role_of 로 왼쪽부터 처음 맞는 열."""
    roles = {}
    for name, r in (mapping or {}).items():
        if r not in ROLES:
            raise ValueError(f"역할 '{r}' 은 없습니다 — {', '.join(ROLES)} 중에서")
        hs = [i for i, h in enumerate(head) if h.strip() == name.strip()]
        if not hs:
            raise ValueError(f"열 '{name}' 이 파일에 없습니다 — 열: {', '.join(head)}")
        roles[r] = hs[0]
    for i, h in enumerate(head):
        if i in roles.values():
            continue
        r = role_of(h)
        if r and r not in roles:
            roles[r] = i
    if "sensor_id" not in roles or "timestamp" not in roles or not ("temp_c" in roles or "mpa" in roles):
        raise ValueError("센서·시각·온도(또는 강도) 열을 찾지 못함 — --map \"열 이름=역할\" 로 지정하세요. 파일의 열: " + ", ".join(head))
    return roles


def stamp(s):
    """업체 시각 글자 → YYYY-MM-DDTHH:MM (못 읽으면 None)."""
    m = TS.match((s or "").strip())
    if not m:
        return None
    y, mo, d, h, mi = (int(g) for g in m.groups())
    try:
        datetime(y, mo, d, h, mi)
    except ValueError:
        return None
    return f"{y:04d}-{mo:02d}-{d:02d}T{h:02d}:{mi:02d}"


def _num(s):
    try:
        v = float((s or "").strip())
    except ValueError:
        return None
    return v if math.isfinite(v) else None


def _read(path):
    if not os.path.exists(path):
        return None, []
    with open(path, encoding="utf-8-sig", newline="") as f:
        rd = csv.reader(f)
        rows = [r for r in rd if r]
    return (rows[0], rows[1:]) if rows else (None, [])


def import_csv(path, folder, mapping=None):
    """업체 CSV → 데이터 폴더 sensors.csv / sensor_strength.csv 합치기.
    → {"rows": 데이터 줄 수, "temp_c": (새, 바뀜) 또는 None, "mpa": (새, 바뀜) 또는 None, "no_position": [처음 보는 ID…], "bad": 건너뛴 줄, "columns": {역할: 열 이름}}"""
    recs = [r for r in csv.reader(io.StringIO(wi._text(path))) if r]
    if not recs:
        raise ValueError(f"{path}: 빈 파일")
    head, body = recs[0], recs[1:]
    cols = columns(head, mapping)
    known = {}                                                       # 기존 위치: 두 파일의 sensor_id 마지막 줄
    for _, name in FILES:
        h, rows = _read(os.path.join(folder, name))
        if h:
            ix = {c: h.index(c) for c in ("sensor_id", "floor", "x", "y") if c in h}
            if len(ix) == 4:
                for r in rows:
                    g = lambda c: r[ix[c]] if ix[c] < len(r) else ""   # noqa: E731
                    if g("sensor_id").strip():
                        known[g("sensor_id").strip()] = (g("floor"), g("x"), g("y"))
    cell = lambda r, k: r[cols[k]].strip() if k in cols and cols[k] < len(r) else ""   # noqa: E731
    out = {k: {} for k, _ in FILES}                                  # (sensor_id, timestamp) → (위치, 값): 파일 안 같은 시각은 마지막 값 (자리는 처음)
    missing, bad = [], 0
    for r in body:
        sid, ts = cell(r, "sensor_id"), stamp(cell(r, "timestamp"))
        vals = {k: _num(cell(r, k)) for k, _ in FILES if k in cols}
        if not sid or ts is None or all(v is None for v in vals.values()):
            bad += 1
            continue
        fl, x, y = cell(r, "floor"), _num(cell(r, "x")), _num(cell(r, "y"))
        if re.fullmatch(r"-?\d+", fl) and x is not None and y is not None:
            pos = (str(int(fl)), repr(x), repr(y))
        elif sid in known:
            pos = known[sid]
        else:
            if sid not in missing:
                missing.append(sid)
            continue
        for k, v in vals.items():
            if v is not None:
                out[k][(sid, ts)] = (pos, repr(v))
    res = {"rows": len(body), "no_position": missing, "bad": bad, "columns": {k: head[i] for k, i in cols.items()}}
    for k, name in FILES:
        res[k] = merge(os.path.join(folder, name), k, [(s, t, p, v) for (s, t), (p, v) in out[k].items()]) if k in cols else None
    return res


def merge(path, value_col, recs):
    """(sensor_id, timestamp) 한 줄 — 있으면 값이 다를 때만 새 값, 없으면 끝에 더함. 다른 열·줄 순서는 그대로. → (새, 바뀜)"""
    head, rows = _read(path)
    head = head or ["sensor_id", "floor", "x", "y", "timestamp", value_col]
    for c in ("sensor_id", "floor", "x", "y", "timestamp", value_col):
        if c not in head:
            raise ValueError(f"{os.path.basename(path)} 의 첫 줄에 '{c}' 열이 없습니다")
    rows = [r + [""] * (len(head) - len(r)) for r in rows]
    ix = {c: i for i, c in enumerate(head)}
    at = {(r[ix["sensor_id"]], r[ix["timestamp"]]): n for n, r in enumerate(rows)}
    new = changed = 0
    for sid, ts, (fl, x, y), v in recs:
        n = at.get((sid, ts))
        if n is None:
            row = [""] * len(head)
            for c, val in (("sensor_id", sid), ("floor", fl), ("x", x), ("y", y), ("timestamp", ts), (value_col, v)):
                row[ix[c]] = val
            if "entered_by" in ix:
                row[ix["entered_by"]] = SOURCE
            rows.append(row)
            at[(sid, ts)] = len(rows) - 1
            new += 1
        elif _num(rows[n][ix[value_col]]) != float(v):
            rows[n][ix[value_col]] = v
            changed += 1
    if new or changed:
        with open(path, "w", encoding="utf-8-sig", newline="") as f:
            csv.writer(f).writerows([head] + rows)
    return new, changed


if __name__ == "__main__":
    import sys
    [s.reconfigure(errors="replace") for s in (sys.stdout, sys.stderr) if hasattr(s, "reconfigure")]
    ap = argparse.ArgumentParser(description="센서 업체 내보내기 CSV → sensors.csv / sensor_strength.csv")
    ap.add_argument("file", nargs="?")
    ap.add_argument("folder", nargs="?")
    ap.add_argument("--map", action="append", default=[], help='"열 이름=역할" (역할: ' + ", ".join(ROLES) + ")")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()
    if a.self_test:
        import shutil
        import tempfile
        t = tempfile.mkdtemp()
        try:
            data = os.path.join(t, "data")
            shutil.copytree(os.path.join(os.path.dirname(os.path.abspath(__file__)), "sample", "data"), data)
            n0 = len(_read(os.path.join(data, "sensors.csv"))[1])
            # 모양 1: 한국어 머리·CP949 — 센서번호·측정일시·온도(°C), 결측·못 읽는 시각·처음 보는 ID·같은 시각 두 번
            k = os.path.join(t, "a.csv")
            open(k, "w", encoding="cp949", newline="").write(
                "센서번호,측정일시,온도(°C)\r\nS1,2027/01/01 8:00,7.5\r\nS1,2027-01-01 09:00:30,\r\nS2,어제,3\r\nNEW9,2027-01-01 08:00,4\r\n"
                "S1,2027-01-01 10:00,6\r\nS1,2027-01-01 10:00,6.5\r\nS2,2026-11-26 08:00,2.29\r\n")   # 샘플은 12-31 09:00 까지 → 01-01 은 새 줄
            r = import_csv(k, data)
            assert r["columns"] == {"sensor_id": "센서번호", "timestamp": "측정일시", "temp_c": "온도(°C)"}, r
            assert (r["rows"], r["bad"], r["no_position"], r["temp_c"], r["mpa"]) == (7, 2, ["NEW9"], (2, 0), None), r
            h, rows = _read(os.path.join(data, "sensors.csv"))
            assert len(rows) == n0 + 2 and rows[-2][:6] == ["S1", "9", "15.0", "9.0", "2027-01-01T08:00", "7.5"] and rows[-1][5] == "6.5", rows[-2:]
            assert import_csv(k, data)["temp_c"] == (0, 0)                                    # 다시 가져와도 그대로
            # 모양 2: 영어 머리·UTF-8 BOM — 강도 열 → sensor_strength.csv (위치는 sensors.csv 의 같은 ID), 위치 열이 있으면 그 값
            e = os.path.join(t, "b.csv")
            open(e, "w", encoding="utf-8-sig", newline="").write(
                "Serial No.,Date/Time,Temperature (C),Strength (MPa),Floor,X,Y\nS3,2027-01-01T09:00,8,21.5,,,\nW9,2027-01-01T09:00,,18,10,5,4\n")
            r2 = import_csv(e, data)
            assert r2["columns"] == {"sensor_id": "Serial No.", "timestamp": "Date/Time", "temp_c": "Temperature (C)", "mpa": "Strength (MPa)",
                                     "floor": "Floor", "x": "X", "y": "Y"}, r2["columns"]
            assert (r2["temp_c"], r2["mpa"], r2["no_position"]) == ((1, 0), (2, 0), []), r2
            hs, srows = _read(os.path.join(data, "sensor_strength.csv"))
            assert hs == ["sensor_id", "floor", "x", "y", "timestamp", "mpa"] and srows[-1] == ["W9", "10", "5.0", "4.0", "2027-01-01T09:00", "18.0"], srows
            # --map: 이름으로 못 찾는 열
            m = os.path.join(t, "c.csv")
            open(m, "w", encoding="utf-8", newline="").write("ch,when,val\nS4,2027-01-01 11:00,9.5\n")
            try:
                import_csv(m, data)
                raise AssertionError("열을 못 찾았는데 가져옴")
            except ValueError as x:
                assert "--map" in str(x)
            assert import_csv(m, data, {"ch": "sensor_id", "when": "timestamp", "val": "temp_c"})["temp_c"] == (1, 0)
            assert stamp("2026-2-30 8:00") is None and stamp("2026.1.5T7:05") == "2026-01-05T07:05"
        finally:
            shutil.rmtree(t, ignore_errors=True)
        print("sensor_import self-check OK")
    else:
        if not (a.file and a.folder):
            ap.error("업체 CSV 와 데이터 폴더 (또는 --self-test)")
        mp = dict(x.split("=", 1) for x in a.map)
        r = import_csv(a.file, a.folder, mp)
        f = lambda v: "열 없음" if v is None else f"{v[0]}줄 새로 · {v[1]}줄 바뀜"   # noqa: E731
        print(f"{SOURCE}: {r['rows']}줄 — 온도 {f(r['temp_c'])} · 강도 {f(r['mpa'])} · 건너뜀 {r['bad']}"
              + (f" · 위치 없음 {len(r['no_position'])} ({', '.join(r['no_position'])}) — 현장 입력에서 위치 먼저" if r["no_position"] else ""))

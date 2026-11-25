"""기상청 ASOS 자료 파일 → weather.csv (docs/direction_log.md 4판 F8). 인증키 없이: 기상자료개방포털에서 내려받은 CSV 를 읽는다.

입력: 종관기상관측(ASOS) 일자료 CSV(일시 = 날짜, '평균기온' 열) 또는 시간자료 CSV(일시 = 날짜 시각, '기온' 열 → 날짜별 평균).
      인코딩은 CP949(포털 기본) 또는 UTF-8. 열은 이름 일부로 찾는다(일시 / 평균기온·기온 / 지점·지점명) — 포털 열 이름이 조금 달라도 됨 [미확인: 포털 형식이 바뀌면 열 찾기 실패 → 오류 문구].
출력: 데이터 폴더 weather.csv (date, avgTa, source) — evaluate 는 date·avgTa 만 읽음(source 는 출처 기록). 기존 줄은 남기고 같은 날짜는 새 파일 값으로 바꿈.
실행: python weather_import.py <ASOS.csv> <데이터 폴더>   ·   자체 검사: python weather_import.py --self-test
"""
import csv
import io
import os
import sys
from collections import defaultdict


def _text(path):
    raw = open(path, "rb").read()
    for enc in ("utf-8-sig", "cp949"):
        try:
            return raw.decode(enc)
        except UnicodeDecodeError:
            pass
    raise SystemExit(f"{path}: 인코딩을 읽을 수 없음 (CP949·UTF-8 아님)")


def _col(head, *keys, exclude=()):
    for i, h in enumerate(head):
        if any(k in h for k in keys) and not any(x in h for x in exclude):
            return i
    return None


def read_asos(path):
    """→ ({날짜: 평균 기온}, 출처 문구). 시간자료면 날짜별 평균(그날 기록 개수 그대로)."""
    rows = list(csv.reader(io.StringIO(_text(path))))
    if not rows:
        raise SystemExit(f"{path}: 빈 파일")
    head = [h.strip() for h in rows[0]]
    it = _col(head, "일시", "date", "tm")
    ta = _col(head, "평균기온", exclude=("최저", "최고", "지면")) if _col(head, "평균기온") is not None else _col(head, "기온", "avgTa", "ta", exclude=("최저", "최고", "지면", "이슬"))
    if it is None or ta is None:
        raise SystemExit(f"{path}: '일시'·'기온' 열을 찾지 못함 — 머리 줄: {head[:8]}")
    st = _col(head, "지점명")
    stn = _col(head, "지점", exclude=("지점명",))
    acc = defaultdict(list)
    for r in rows[1:]:
        if len(r) <= max(it, ta) or not r[ta].strip():
            continue                                  # 결측(빈칸)은 건너뜀 — 그날이 비면 evaluate 가 평년 기온으로 채움
        acc[r[it].strip()[:10]].append(float(r[ta]))
    if not acc:
        raise SystemExit(f"{path}: 기온 값이 있는 줄이 없음")
    first = rows[1]
    where = " ".join(x for x in ((first[stn].strip() if stn is not None else ""), (first[st].strip() if st is not None else "")) if x)
    return {d: round(sum(v) / len(v), 1) for d, v in acc.items()}, f"기상청 ASOS {where}".strip() + " [입력]"


def merge(folder, temps, source):
    """weather.csv 에 합침 → (새 날짜 수, 바뀐 날짜 수). 날짜 순 정렬, 머리 date,avgTa,source."""
    p = os.path.join(folder, "weather.csv")
    old = {}
    if os.path.exists(p):
        with open(p, encoding="utf-8-sig", newline="") as f:
            for r in csv.DictReader(f):
                old[r["date"]] = (r["avgTa"], r.get("source", "") or "weather.csv 기록 [입력]")
    new = sum(1 for d in temps if d not in old)
    changed = sum(1 for d, t in temps.items() if d in old and float(old[d][0]) != t)
    for d, t in temps.items():
        old[d] = (f"{t:g}", source)
    with open(p, "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        w.writerow(["date", "avgTa", "source"])
        w.writerows([d, t, s] for d, (t, s) in sorted(old.items()))
    return new, changed


if __name__ == "__main__":
    [s.reconfigure(errors="replace") for s in (sys.stdout, sys.stderr) if hasattr(s, "reconfigure")]
    if sys.argv[1:] == ["--self-test"]:
        import shutil
        import tempfile
        from datetime import datetime
        t = tempfile.mkdtemp()
        try:
            day = os.path.join(t, "asos_day.csv")       # 포털 일자료 모양 (CP949)
            open(day, "w", encoding="cp949", newline="").write(
                "지점,지점명,일시,평균기온(°C),최저기온(°C),최고기온(°C)\n108,서울,2026-12-01,-1.5,-5.0,3.2\n108,서울,2026-12-02,,-6.0,1.0\n108,서울,2026-12-03,2.0,-1.0,5.0\n")
            temps, src = read_asos(day)
            assert temps == {"2026-12-01": -1.5, "2026-12-03": 2.0} and src == "기상청 ASOS 108 서울 [입력]", (temps, src)
            hour = os.path.join(t, "asos_hour.csv")      # 시간자료 → 날짜 평균
            open(hour, "w", encoding="utf-8", newline="").write(
                "지점,지점명,일시,기온(°C),강수량(mm)\n" + "".join(f"108,서울,2026-12-0{d} {h:02d}:00,{v},\n" for d, v in ((4, 1.0), (4, 3.0), (5, -2.0)) for h in (1,)))
            assert read_asos(hour)[0] == {"2026-12-04": 2.0, "2026-12-05": -2.0}
            folder = os.path.join(t, "data")
            os.makedirs(folder)
            open(os.path.join(folder, "weather.csv"), "w", encoding="utf-8").write("date,avgTa\n2026-11-30,4.0\n2026-12-01,0.0\n")
            assert merge(folder, temps, src) == (1, 1)
            rows = list(csv.DictReader(open(os.path.join(folder, "weather.csv"), encoding="utf-8")))
            assert [(r["date"], r["avgTa"]) for r in rows] == [("2026-11-30", "4.0"), ("2026-12-01", "-1.5"), ("2026-12-03", "2")]
            assert rows[0]["source"] == "weather.csv 기록 [입력]" and rows[1]["source"] == src
            # evaluate 가 source 열이 더해진 weather.csv 를 그대로 읽는지 (date·avgTa 만 씀)
            sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
            import evaluate as ev
            assert {r["date"]: float(r["avgTa"]) for r in ev.read(folder, "weather.csv")}["2026-12-01"] == -1.5
            assert ev.weather_label(ev.read(folder, "weather.csv")) == "기상 기록"                      # 출처 섞임 → 일반 이름
            assert ev.weather_label([{"source": src}, {"source": src}]) == "기상청 ASOS 108 서울"     # 한 출처 → 판정 근거에 그 이름
            assert ev.weather_label([{"date": "x", "avgTa": "1"}]) == "기상 기록"                     # source 열 없는 옛 파일 그대로
            bad = os.path.join(t, "bad.csv")
            open(bad, "w", encoding="utf-8").write("a,b\n1,2\n")
            try:
                read_asos(bad)
                raise AssertionError("열 없는 파일이 통과함")
            except SystemExit as x:
                assert "열을 찾지 못함" in str(x)
        finally:
            shutil.rmtree(t, ignore_errors=True)
        print("weather_import self-check OK")
    elif len(sys.argv) == 3:
        temps, src = read_asos(sys.argv[1])
        n, c = merge(sys.argv[2], temps, src)
        print(f"{src}: {len(temps)}일 ({min(temps)} – {max(temps)}) → weather.csv 새 날짜 {n} · 바뀐 날짜 {c}")
    else:
        raise SystemExit("사용법: python weather_import.py <ASOS.csv> <데이터 폴더>  |  --self-test")

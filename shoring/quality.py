"""28일 품질 대장 (F7) · 타설 입회 점검 (F5) — 해체 판정(evaluate.py)과 분리. 판정 결과를 바꾸지 않는다.

F7  cylinders.csv(zone_id, test_date, curing, f1, f2, f3) 중 curing = "표준양생"·재령 28일(test_date − pour_date) 줄만.
    1줄 = 1회 시험(공시체 3개 평균). 구역(= pours.csv 한 줄 = 타설) 단위로 아래 기준.
F5  pours.csv 선택 열 supervisor_present(예/아니오)·watcher(이름). 열이 없으면 안내 한 줄만.
실행: python quality.py <폴더> [--html 대장.html]  ·  python quality.py --self-test
"""
from report_style import REPORT_CSS
import html
import os
import sys
from datetime import date

from evaluate import checked, cyl_rows, kdate, read

# 기준 — 출처: docs/direction_review_20261104.md (N4·N5·N6), docs/field_process_research.md (S12)
STD_CURING = "표준양생"     # [입력] cylinders.csv curing 값 (test_gate.py 와 같은 표기)
AGE_DAYS = 28               # [부분 확인, N5] "28일 품질검사(KCS 14 20 10 표 3.5-7)" — direction_review 개선 8
PER_TEST = 3                # [부분 확인, S12] 1회 = 공시체 3개 평균 (FPR 단계 3)
RUN = 3                     # [부분 확인, N5] 연속 3회 평균 ≥ Fcq
MIN_SPECIMENS = 9           # [확정, N5·N6] 연속 3회 → 최소 9개 (direction_review 단계 5)
ONE_MARGIN = 3.5            # [부분 확인, N5] 1회 ≥ Fcq − 3.5 MPa, Fcq ≤ 35 일 때
ONE_LIMIT_FCQ = 35.0        # [부분 확인, N5] 위 1회 기준의 적용 범위
# Fcq > 35 의 1회 기준 [미확인] — 저장소 문서에 없음 → '확인 필요'
# Fcq = pours.csv fck [가정] — 품질기준강도 정의(내구성 기준강도 등)는 문서에 없음, 입력 열도 없음
BASIS = "KCS 14 20 10 표 3.5-7 · 최소 9개 (연속 3회 × 3개)"
WATCH_BASIS = "KCS 14 20 12 3.2(11) 타설 중 감시자 배치 [확정, N4]"
WATCH_HINT = "pours.csv 에 supervisor_present·watcher 열을 넣으면 점검합니다"


@checked
def quality_ledger(folder):
    """→ [{zone_id, pour_date, n_specimens, mean, min, result, basis}] (pours.csv 순서).
    mean = 1회 평균들의 평균, min = 1회 평균의 최솟값 (MPa, 시험 없으면 None)."""
    tests = {}
    pours = read(folder, "pours.csv")
    pd = {p["zone_id"]: date.fromisoformat(p["pour_date"]) for p in pours}
    for r in cyl_rows(folder):                  # W1: 타설 묶음 공통 시험 → 묶음의 구역마다
        z = r["zone_id"]
        if r.get("curing") != STD_CURING or z not in pd:
            continue
        if (date.fromisoformat(r["test_date"]) - pd[z]).days != AGE_DAYS:
            continue
        tests.setdefault(z, []).append((r["test_date"], [float(r[k]) for k in ("f1", "f2", "f3")]))
    rows = []
    for p in pours:
        z, fcq = p["zone_id"], float(p["fck"])
        ts = [sum(v) / PER_TEST for _, v in sorted(tests.get(z, []), key=lambda t: t[0])]  # 같은 날은 파일 순서
        row = {"zone_id": z, "pour_date": p["pour_date"], "n_specimens": PER_TEST * len(ts),
               "mean": sum(ts) / len(ts) if ts else None, "min": min(ts) if ts else None}
        if not ts:
            res, why = "시험 없음", f"표준양생 {AGE_DAYS}일 시험 없음"
        elif fcq > ONE_LIMIT_FCQ:
            res, why = "확인 필요", f"Fcq {fcq:g} > {ONE_LIMIT_FCQ:g} MPa 1회 기준 [미확인]"
        elif min(ts) < fcq - ONE_MARGIN:
            res, why = "불합격", f"1회 {min(ts):.1f} < Fcq − {ONE_MARGIN:g} = {fcq - ONE_MARGIN:.1f} MPa"
        elif PER_TEST * len(ts) < MIN_SPECIMENS:
            res, why = "확인 필요", f"공시체 {PER_TEST * len(ts)}개 < 최소 {MIN_SPECIMENS}개 (연속 {RUN}회 미만)"
        else:
            low = min(sum(ts[i:i + RUN]) / RUN for i in range(len(ts) - RUN + 1))
            res, why = (("합격", f"연속 {RUN}회 평균 최저 {low:.1f} ≥ Fcq {fcq:g}") if low >= fcq
                        else ("불합격", f"연속 {RUN}회 평균 {low:.1f} < Fcq {fcq:g}"))
        row.update(result=res, basis=f"{why} — {BASIS}")
        rows.append(row)
    return rows


def watch_missing(folder):
    """→ {구역: 타설 입회 기록 없음 문구} (pours.csv 순서). 선택 열이 둘 다 없으면 None. 판정은 바꾸지 않는다."""
    pours = read(folder, "pours.csv")
    if not pours or not ({"supervisor_present", "watcher"} & set(pours[0])):
        return None
    return {p["zone_id"]: f"타설 입회 기록 없음 — {p['zone_id']} {p['pour_date']} ({WATCH_BASIS})" for p in pours
            if (p.get("supervisor_present") or "").strip() != "예" or not (p.get("watcher") or "").strip()}


def pour_watch(folder):
    """→ [문구]. 선택 열이 둘 다 없으면 [안내 한 줄]. 판정은 바꾸지 않는다."""
    m = watch_missing(folder)
    return [WATCH_HINT] if m is None else list(m.values())


def quality_html(rows):
    """인쇄용 표 한 장."""
    f = lambda v: "" if v is None else f"{v:.1f}"  # noqa: E731
    tr = "".join(f"<tr><td>{html.escape(r['zone_id'])}</td><td>{kdate(r['pour_date'])}</td><td>{r['n_specimens']}</td>"
                 f"<td>{f(r['mean'])}</td><td>{f(r['min'])}</td><td><b>{r['result']}</b></td>"
                 f"<td>{html.escape(r['basis'])}</td></tr>" for r in rows)
    return ("<!doctype html><html lang='ko'><head><meta charset='utf-8'><title>품질 대장 (28일 · 참고)</title>"
            "<style>@page{size:A4;margin:14mm}body{font:12px sans-serif}table{border-collapse:collapse}td,th{border:1px solid #000;padding:3px 6px}" + REPORT_CSS + "</style>"
            "</head><body><h1>품질 대장 (28일 · 참고) — 표준양생</h1><p>해체 판정과 별개인 품질검사 기록입니다. "
            "Fcq = 타설 fck [가정]. 평균·최소 = 1회(공시체 3개 평균) 기준, MPa.</p><table><caption>참고 — 기준 적용 [부분 확인], 품질관리자 확인용</caption><tr><th>구역</th><th>타설일</th>"
            "<th>공시체 수</th><th>평균</th><th>최소</th><th>결과</th><th>근거</th></tr>" + tr + "</table></body></html>")


def self_test():
    import shutil
    import tempfile
    t = tempfile.mkdtemp()
    try:
        def w(name, text):
            with open(os.path.join(t, name), "w", encoding="utf-8", newline="") as fh:
                fh.write(text)
        w("pours.csv", "zone_id,pour_date,pour_time,mix,fck,supervisor_present,watcher\n"
          "P,2026-10-01,08:00,보통,24,예,홍길동\nF,2026-10-01,08:00,보통,24,아니오,\n"
          "N,2026-10-01,08:00,보통,24,,\nC,2026-10-01,08:00,보통,24,예,김감시\nH,2026-10-01,08:00,보통,40,예,이감시\n")
        w("cylinders.csv", "zone_id,test_date,curing,f1,f2,f3\n"
          + "".join(f"P,2026-10-29,표준양생,{a},{a},{a}\n" for a in (25, 26, 24.5))
          + "F,2026-10-29,표준양생,26,26,26\nF,2026-10-29,표준양생,20,20,20\nF,2026-10-29,표준양생,26,26,26\n"
          "N,2026-10-29,현장봉함,30,30,30\nN,2026-10-15,표준양생,30,30,30\n"   # 현장양생·14일 → 시험 없음
          "C,2026-10-29,표준양생,30,30,30\nH,2026-10-29,표준양생,45,45,45\n")
        got = {r["zone_id"]: r for r in quality_ledger(t)}
        assert got["P"]["result"] == "합격" and got["P"]["n_specimens"] == 9, got["P"]
        assert got["F"]["result"] == "불합격" and got["F"]["min"] == 20, got["F"]
        assert got["N"]["result"] == "시험 없음" and got["N"]["mean"] is None, got["N"]
        assert got["C"]["result"] == "확인 필요" and got["H"]["result"] == "확인 필요", (got["C"], got["H"])
        warns = pour_watch(t)
        assert len(warns) == 2 and warns[0].startswith("타설 입회 기록 없음 — F 2026-10-01"), warns
        assert "<table><caption>참고 — 기준 적용 [부분 확인], 품질관리자 확인용</caption>" in quality_html(list(got.values()))
        print("자체 검사 1 합격·불합격·시험 없음·확인 필요·입회 경고 2건 OK")
        shutil.rmtree(t)
        os.makedirs(t)
        sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "tools"))
        import make_demo_data
        make_demo_data.make(t)
        rows = quality_ledger(t)
        assert rows and all(r["result"] == "시험 없음" for r in rows), rows   # 시연 공시체는 현장봉함뿐
        assert pour_watch(t) == [WATCH_HINT]
        quality_html(rows)
        print(f"자체 검사 2 시연 자료 {len(rows)}구역 시험 없음 · 입회 열 없음 안내 OK")
    finally:
        shutil.rmtree(t, ignore_errors=True)


if __name__ == "__main__":
    import argparse
    [s.reconfigure(errors="replace") for s in (sys.stdout, sys.stderr) if hasattr(s, "reconfigure")]  # cp949 콘솔
    ap = argparse.ArgumentParser(description="28일 품질 대장·타설 입회 점검 (해체 판정과 별개)")
    ap.add_argument("folder", nargs="?")
    ap.add_argument("--html", help="인쇄용 HTML 저장 경로")
    ap.add_argument("--self-test", action="store_true")
    a = ap.parse_args()
    if a.self_test:
        self_test()
        raise SystemExit(0)
    if not a.folder:
        ap.error("폴더를 주세요")
    rows = quality_ledger(a.folder)
    for r in rows:
        m = "" if r["mean"] is None else f" 평균 {r['mean']:.1f} 최소 {r['min']:.1f}"
        print(f"{r['zone_id']} {r['pour_date']} 공시체 {r['n_specimens']}{m} → {r['result']} ({r['basis']})")
    for s in pour_watch(a.folder):
        print(s)
    if a.html:
        with open(a.html, "w", encoding="utf-8") as fh:
            fh.write(quality_html(rows))
        print(f"HTML: {a.html}")

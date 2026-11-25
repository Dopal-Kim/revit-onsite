"""해체 결과보고 (11-12 2-6 G10, 가이드라인 3.2.13) — 해체가 끝난 구역마다 A4 한 장 HTML + 근거 원장 8 결과보고 한 줄.
새 판단은 없다: 근거 원장 단계 날짜·해체 후 점검 5항목(가이드라인 4.3)·침하 계측(3.2.12①·3.3.3⑥, after_check.settlements)을 옮긴다.
만드는 조건: 그 구역 원장 7 해체 기록이 있고, 해체 후 점검 5항목이 모두 기록(OK/NG)돼 있을 것. NG 가 있어도 만든다 (그대로 적음).
파일: <데이터 폴더>/해체결과보고_<구역>_YYYYMMDD.html (같은 구역·같은 날 다시 만들면 _2, _3 … — 원장 줄의 SHA-256 이 가리키는 파일을 덮지 않음).
C# RevitOnSite.Engine/ResultReport.cs 와 같은 글자 (바이트 대조).
실행: python result_report.py --self-test
"""
import html
import os

import after_check as ac
import evaluate as ev
import evidence as evd
from report_style import REPORT_CSS

NOTE = "이 보고서는 프로그램에 남은 기록을 옮긴 것입니다. 프로그램은 승인하지 않으며, 결과 확인은 현장 책임자와 감리(책임기술인)가 서명으로 합니다."


def items(folder, zone, day):
    """해체 후 점검 5항목 → [(코드, 이름, 결과, 점검일, 점검자)] — 항목마다 day 까지 마지막 날짜 행 (같은 날이면 뒤 행, after_status 와 같음)."""
    last = {}
    for r in ev.read(folder, ac.FILE):
        code, d = (r.get("item_code") or "").strip().upper(), (r.get("date") or "").strip()
        if (r.get("zone_id") or "").strip() == zone and code in ac.ITEMS and d and d <= day and d >= last.get(code, ("",))[0]:
            last[code] = (d, (r.get("result") or "").strip().upper(), (r.get("by") or "").strip())
    return [(c, n, last[c][1], last[c][0], last[c][2]) if c in last else (c, n, "", "", "") for c, n in ac.ITEMS.items()]


def page(folder, zone, at, project=""):
    day = at.date().isoformat()
    q = html.escape
    zr = next((r for r in ev.read(folder, "zones.csv") if r["zone_id"] == zone), {})
    pour = next((r.get("pour_date") or "" for r in ev.read(folder, "pours.csv") if r["zone_id"] == zone), "")
    L = evd.ledger_at(folder, day).get(zone, {"dates": {}, "warnings": []})
    steps = "".join(f"<tr><td>{s} {evd.STAGES[s]}</td><td>{ev.kdate(L['dates'][s]) if s in L['dates'] else '기록 없음'}</td></tr>" for s in (2, 4, 5, 6, 7))
    its = "".join(f"<tr><td>{c}</td><td>{q(n)}</td><td>{q(res) if res else '기록 없음'}</td><td>{ev.kdate(d)}</td><td>{q(by)}</td></tr>" for c, n, res, d, by in items(folder, zone, day))
    se = ac.settlements(folder, day).get(zone)
    if se:
        days = sorted(se["days"])
        st = ("<table><tr><th>측정일</th>" + "".join(f"<th>{q(p)} (mm)</th>" for p in se["points"]) + "</tr>"
              + "".join(f"<tr><td>{ev.kdate(d)}</td>" + "".join(f"<td>{q(se['days'][d].get(p, '—'))}</td>" for p in se["points"]) + "</tr>" for d in days) + "</table>")
        ok, bad = ac.same_points(se)
        first = sorted(se["days"][days[0]])
        st += (f"<p>측정 위치 일치: <b>{'예' if ok else '아니오'}</b> — 처음 측정({ev.kdate(days[0])}) 지점 {q(', '.join(first))}"
               + ("" if ok else f" · 다른 지점을 잰 날 {', '.join(ev.kdate(d) for d in bad)}") + "</p>")
    else:
        st = "<p>침하 계측 기록 없음 (해체 후 점검 화면에서 지점·침하량 mm 를 넣습니다)</p>"
    warn = [w for w in L["warnings"] if zone in w]
    wl = "".join(f"<li>{q(w)}</li>" for w in warn) or "<li>없음</li>"
    return f"""<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>해체 결과보고 {q(zone)} {day}</title>
<style>@page{{size:A4;margin:12mm}}body{{font:10pt 'Malgun Gothic',sans-serif;color:#111;background:#fff}}h1{{font-size:16pt;margin:0 0 4px}}h2{{font-size:12pt;margin:8px 0 3px}}
table{{border-collapse:collapse;width:100%;margin:6px 0}}th,td{{border:1px solid #888;padding:4px 6px;text-align:left;vertical-align:top}}th{{background:#eee}}
.sign{{break-inside:avoid}}.sign td{{height:40px}}.note{{color:#444;font-size:9.5pt}}ul{{margin:3px 0;padding-left:18px}}{REPORT_CSS}</style></head><body>
<h1>동바리 해체 결과보고</h1>
<div>{q(project)} · 구역 {q(zone)} ({q(zr.get('floor', ''))}층) · 타설 {ev.kdate(pour)} · 작성 기준일 {ev.kdate(day)} · 작성 Revit On-Site</div>
<h2>1. 해체 경과 (근거 원장)</h2>
<table><tr><th>단계</th><th>날짜</th></tr>{steps}</table>
<h2>2. 해체 후 점검 (가이드라인 4.3)</h2>
<table><tr><th>코드</th><th>항목</th><th>결과</th><th>점검일</th><th>점검자</th></tr>{its}</table>
<h2>3. 침하 계측 (가이드라인 3.2.12① · 3.3.3⑥)</h2>
{st}
<h2>4. 확인할 점</h2>
<ul>{wl}</ul>
<table class="sign"><tr><th>구분</th><th>소속</th><th>성명</th><th>서명</th><th>일시</th></tr>
<tr><td>작성 (현장 책임자)</td><td>{q(ev.text_option(folder, 'contractor'))}</td><td></td><td></td><td></td></tr>
<tr><td>확인 (감리)</td><td></td><td></td><td></td><td></td></tr></table>
<p class="note">{q(NOTE)} 결과보고와 다음 해체 계획 반영은 가이드라인 3.2.13.</p>
</body></html>"""


def make(folder, zone, at, by, project=""):
    """조건 검사 → 보고서 파일 쓰기 → 근거 원장 8 한 줄 (파일 SHA-256). → 파일 경로. 조건이 안 되면 ValueError."""
    day = at.date().isoformat()
    if not str(by).strip():
        raise ValueError("입력자가 필요합니다")
    if 7 not in evd.ledger_at(folder, day).get(zone, {"dates": {}})["dates"]:
        raise ValueError(f"{zone}: 해체(원장 7) 기록이 없어 결과보고를 만들지 않음")
    st = ac.after_status(folder, day).get(zone)
    miss = st["missing"] if st else list(ac.ITEMS)
    if miss:
        raise ValueError(f"{zone}: 해체 후 점검 기록 없는 항목 {', '.join(miss)} — 5항목을 넣은 뒤 만듭니다")
    base = os.path.join(os.path.abspath(folder), f"해체결과보고_{zone}_{at:%Y%m%d}")
    path, k = base + ".html", 1
    while os.path.exists(path):
        k += 1
        path = f"{base}_{k}.html"
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write(page(folder, zone, at, project))
    evd.add(folder, zone, day, 8, by, doc_no=f"RR-{zone}-{at:%Y%m%d}", result="해체 결과보고", file=path)
    return path


if __name__ == "__main__":
    import shutil
    import sys
    import tempfile
    from datetime import datetime
    [s.reconfigure(errors="replace") for s in (sys.stdout, sys.stderr) if hasattr(s, "reconfigure")]
    sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tools"))
    import make_demo_data as md
    t = tempfile.mkdtemp()
    try:
        md.sim(t)                                                       # 1–4F 해체·해체 후 점검 5항목 OK·원장 8 (시연)
        at = datetime(2026, 11, 24, 9)
        z = "1F-A1"
        try:
            make(t, "9F-A1", at, "시험")
            raise AssertionError("해체 기록 없는 구역인데 만들어짐")
        except ValueError as x:
            assert "해체(원장 7) 기록이 없어" in str(x), x
        with open(os.path.join(t, ac.FILE), "a", encoding="utf-8", newline="") as f:
            f.write(f"{z},2026-11-20,SET-P1,1.2,박안전,\n{z},2026-11-20,SET-P2,0.4,박안전,\n{z},2026-11-22,SET-P1,1.5,박안전,\n")
        n8 = sum(1 for r in ev.read(t, evd.LEDGER) if r["zone_id"] == z and r["stage"] == "8")
        p = make(t, z, at, "시험", "시험")
        p2 = make(t, z, at, "시험", "시험")
        assert p.endswith(f"해체결과보고_{z}_20261124.html") and p2.endswith("_2.html"), (p, p2)
        led = [r for r in ev.read(t, evd.LEDGER) if r["zone_id"] == z and r["stage"] == "8"]
        assert len(led) == n8 + 2 and led[-2]["file"] == p and led[-2]["sha256"] == evd.file_sha(p), led[-2]
        h = open(p, encoding="utf-8").read()
        assert "측정 위치 일치: <b>아니오</b>" in h and "P2 (mm)" in h and "<td>—</td>" in h and "1F-A1" in h, h
        assert all(f"<td>{c}</td>" in h for c in ac.ITEMS) and "기록 없음</td></tr><tr><td>4 해체전점검" not in h
        print("result_report self-check OK")
    finally:
        shutil.rmtree(t, ignore_errors=True)

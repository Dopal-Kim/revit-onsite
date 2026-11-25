"""인쇄 쪽수 시험 (11-11 1-9): 서류를 Chrome 으로 A4 PDF 인쇄해 쪽수를 센다 — 화면 스타일이 인쇄로 새거나 서명란이 갈리는 회귀를 잡음.
데모 자료(revit/onsite_data_demo) 사본 → 임시 폴더, 끝나면 지움. Chrome 이 없으면 건너뜀(통과로 보지 않고 '건너뜀' 출력, 종료 0).
실행: python tools/print_pages_check.py
"""
import os
import re
import shutil
import subprocess
import sys
import tempfile
from datetime import datetime

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "shoring"))
import approval as ap   # noqa: E402
import evidence as evd  # noqa: E402
import result_report as rr   # noqa: E402
import evaluate as ev   # noqa: E402
import staged           # noqa: E402

AT = datetime(2026, 12, 31, 9)


def pages(html_path):
    """make_pdf.ps1 로 인쇄 → 쪽수 (Chrome 없으면 None)"""
    pdf = html_path[:-5] + ".pdf"
    r = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", os.path.join(ROOT, "tools", "make_pdf.ps1"), html_path, pdf],
                       capture_output=True, timeout=120)
    if r.returncode != 0:
        return None
    m = re.search(rb"\((\d+)", r.stdout)
    return int(m.group(1)) if m else None


def main():
    t = tempfile.mkdtemp(prefix="print_pages_")
    try:
        f = os.path.join(t, "data")
        shutil.copytree(os.path.join(ROOT, "revit", "onsite_data_demo"), f)
        rows = ev.evaluate(f, AT)
        req = os.path.join(t, "req.html")
        open(req, "w", encoding="utf-8").write(ap.request_html(rows, "시험", AT, f, ev.gate_details(f, AT)))
        rev = os.path.join(t, "staged.html")
        z = next(r["zone_id"] for r in rows if r["floor"] == 8)
        html_rev = staged.review_html(f, AT, z, project="시험")
        open(rev, "w", encoding="utf-8").write(html_rev)
        rev1 = os.path.join(t, "staged_body.html")   # 11-12: 본문(서명란까지)만 — 붙임(기본 사항·출처)을 뺀 사본이 한 장이어야
        open(rev1, "w", encoding="utf-8").write(re.sub(r'<div class="annex">.*?</div>\n', "", html_rev, flags=re.S))
        rrp = os.path.join(t, "result.html")       # 11-12 2-6 G10 해체 결과보고 = A4 한 장
        z7 = next(z for z, v in sorted(evd.ledger_at(f, AT.date().isoformat()).items()) if 7 in v["dates"])
        open(rrp, "w", encoding="utf-8").write(rr.page(f, z7, AT, "시험"))
        n_req, n_rev, n_rev1, n_rr = pages(req), pages(rev), pages(rev1), pages(rrp)
        if None in (n_req, n_rev, n_rev1, n_rr):
            print("인쇄 쪽수 시험 건너뜀 (Chrome 인쇄 실패)")
            return 0
        body = open(req, encoding="utf-8").read()
        assert "<thead>" in body and "서식 1 / 4" in body, "요청서 표 머리 반복·서식 번호"
        assert 4 <= n_req <= 6, f"검측요청서 {n_req}쪽 (서식 4가지, 붙임 1 은 구역 수만큼 — 데모 5쪽)"
        assert n_rev1 == 1 and n_rev == 2, f"조기 탈형 검토서 본문 {n_rev1}쪽 (A4 한 장 — 서명란까지) · 붙임 포함 {n_rev}쪽 (2쪽)"
        assert n_rr == 1, f"해체 결과보고 {n_rr}쪽 (A4 한 장이어야 함)"
        print(f"인쇄 쪽수 시험 통과 (검측요청서 {n_req}쪽 · 조기 탈형 검토서 {n_rev}쪽 · 해체 결과보고 {n_rr}쪽)")
        return 0
    finally:
        shutil.rmtree(t, ignore_errors=True)


if __name__ == "__main__":
    sys.exit(main())

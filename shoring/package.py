"""검측 패키지 (docs/direction_log.md 5판 F11) — 감리에게 '한 묶음'으로 넘기는 폴더.

왜: 감리는 검측요청서·이력서·IFC·근거 파일을 따로 받으면 대조에 시간이 들고 빠지는 서류가 생긴다(impact_map #5 '감리 확인 시간').
묶음 하나 + 목록(index.html) + 파일마다 SHA-256 → 받은 쪽이 빠짐·고침을 바로 확인. 새 계산은 없다(기존 결과물 모음).
출력: <데이터 폴더>/검측패키지_YYYYMMDD/ (같은 날 다시 만들면 덮어씀 — 점검 파일 쌓지 않기)
  검측요청서.html · 슬래브이력서.html(요청 가능 구역만) · 감사기록.csv/.html(html 은 요청 구역 줄만) · 근거원장.csv/.html(요청 구역 근거 줄, html 에 첨부 해시 대조)
  · 첨부/(근거 원장 file 칸의 파일 사본)
  · IFC(데이터 폴더에 RevitOnSite_YYYYMMDD.ifc 가 있으면 사본 — 애드인 [검측 패키지]가 먼저 IFC 를 내보냄, 11-12 2-6) · 목록.csv · index.html
실행: python package.py <데이터 폴더> --at 2026-12-31T09:00   ·   자체 검사: --self-test
"""
from report_style import REPORT_CSS
import argparse
import csv
import hashlib
import html
import os
import shutil
from datetime import datetime

import approval as ap
import audit_export as ax
import evaluate as ev
import evidence
import photos
import slab_record as sr

NOTE = "이 묶음은 감리 검측요청에 붙이는 근거 자료입니다. 프로그램은 승인하지 않으며, 해체 여부는 감리(책임기술인)가 확인·서명으로 결정합니다."


def _sha(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for b in iter(lambda: f.read(1 << 16), b""):
            h.update(b)
    return h.hexdigest()


def _table_html(title, project, at, req, head, rows):
    """근거원장.html·감사기록.html (11-12 2-6 Revit #20): 엑셀 없이 읽는 표 — 요청 구역 줄만, CSV 는 그대로 둠."""
    e = html.escape
    return (f"<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>{e(title)} {e(project)} {at:%Y-%m-%d}</title>\n"
            f"<style>@page{{size:A4 landscape;margin:12mm}}body{{font:9pt 'Malgun Gothic',sans-serif;color:#111;margin:16px}}table{{border-collapse:collapse;width:100%}}"
            f"th,td{{border:1px solid #999;padding:3px 5px;text-align:left;vertical-align:top;word-break:break-all}}th{{background:#eee}}tr{{break-inside:avoid}}{REPORT_CSS}</style></head><body>\n"
            f"<h1>{e(title)}</h1><p>{e(project)} · 판정 기준 {ev.kstamp(at)} · 요청 구역 {len(req)}: {e(', '.join(req))} · 줄 {len(rows)}</p>\n"
            "<table><thead><tr>" + "".join(f"<th>{e(h)}</th>" for h in head) + "</tr></thead><tbody>"
            + "".join("<tr>" + "".join(f"<td>{e(c)}</td>" for c in r) + "</tr>" for r in rows) + "</tbody></table></body></html>")


def build(folder, at, project=None):
    """→ (패키지 폴더, [(상대 경로, 바이트, sha256)], 요청 구역 목록). 요청 가능 구역이 없으면 SystemExit."""
    project = project or os.path.basename(os.path.abspath(folder))
    rows = ev.evaluate(folder, at)
    req = [r["zone_id"] for r in ap.request_rows(rows, folder, at) if r["decision"] == "해체 승인 요청 가능"]   # 이미 기록(6·7·8)된 구역 뺌
    if not req:
        raise SystemExit("해체 승인 요청 가능 구역이 없어 검측 패키지를 만들지 않음")
    out = os.path.join(folder, f"검측패키지_{at:%Y%m%d}")
    if os.path.isdir(out):
        shutil.rmtree(out)                                   # 같은 날 다시 만들면 바꿔 씀 (쌓지 않기)
    os.makedirs(os.path.join(out, "첨부"))
    def w(name, text):
        open(os.path.join(out, name), "w", encoding="utf-8", newline="").write(text)
    w("검측요청서.html", ap.request_html(rows, project, at, folder, ev.gate_details(folder, at)))
    w("슬래브이력서.html", sr.page([r for r in sr.records(folder, at) if r["zone_id"] in req], project, at))
    audit = ax.rows(folder)
    ax.write_csv(audit, os.path.join(out, "감사기록.csv"))
    w("감사기록.html", _table_html("감사 기록 (요청 구역)", project, at, req, ["시각", "출처", "구역", "행위", "입력자", "내용", "줄 SHA"],
                               [[r[k] for k in ax.COLS] for r in audit if r["zone_id"] in req]))
    led = [x for x in evidence.entries(folder) if x["zone_id"] in req and x["date"][:10] <= at.date().isoformat()]
    hrows = []
    with open(os.path.join(out, "근거원장.csv"), "w", encoding="utf-8-sig", newline="") as f:
        c = csv.writer(f)
        c.writerow(["zone_id", "date", "stage", "source", "by", "doc_no", "result", "file", "sha256"])
        key = ev.floor_order(folder)
        for x in sorted(led, key=lambda x: (key(x["zone_id"]), x["date"], x["stage"])):
            src = (x.get("file") or "").strip()
            name, chk = "", ""
            if src and os.path.isfile(src if os.path.isabs(src) else os.path.join(folder, src)):
                p = src if os.path.isabs(src) else os.path.join(folder, src)
                name = f"첨부/{x['zone_id']}_{x['stage']}_{os.path.basename(p)}"
                shutil.copy2(p, os.path.join(out, name))
                sha = x.get("sha256", "")      # 원장에 적은 SHA-256 과 넣은 사본 대조 — 원장 기록 뒤 파일이 바뀌면 드러남
                chk = ("일치" if _sha(os.path.join(out, name)) == sha else "다름 — 원장 기록 뒤 파일이 바뀜") if sha else "원장에 해시 없음"
            elif src:
                chk = "파일 없음"
            c.writerow([x["zone_id"], x["date"], x["stage"], x["source"], x.get("by", ""), x.get("doc_no", ""), x.get("result", ""), name, x.get("sha256", "")])
            hrows.append([x["zone_id"], x["date"], f"{x['stage']} {evidence.STAGES[x['stage']]}", x["source"], x.get("by", ""), x.get("doc_no", ""), x.get("result", ""), name, chk])
    w("근거원장.html", _table_html("근거 원장 (요청 구역)", project, at, req, ["구역", "날짜", "단계", "출처", "입력자", "문서 번호", "결과", "첨부", "첨부 해시 대조"], hrows))
    day = at.date().isoformat()
    need = {z for zid in req for z in ap.check_targets(folder, zid)}      # 요청서 현장 확인 ① 대상 (위 3개층·평면 겹침)
    ph = photos.rows(folder, set(req), day) + [x for x in photos.rows(folder, need - set(req), day)   # + ① 대상의 최근 24 h 동바리 확인 사진 (총괄 29차)
                                               if x["kind"] in ("동바리확인", "해체신고") and 0 <= (at - ev.ts(x["when"])).total_seconds() / 3600 <= ap.CHECK_FRESH_H]
    for x in ph:      # 요청 구역 현장 사진 사본 (F15) — 원본 그대로, 이름 = 서버가 정한 이름
        p = os.path.join(folder, *x["path"].split("/"))
        if os.path.isfile(p):
            os.makedirs(os.path.join(out, "첨부", "사진"), exist_ok=True)
            shutil.copy2(p, os.path.join(out, "첨부", "사진", os.path.basename(p)))
    ifc = os.path.join(folder, f"RevitOnSite_{at:%Y%m%d}.ifc")
    if os.path.exists(ifc):
        shutil.copy2(ifc, os.path.join(out, os.path.basename(ifc)))
    files = []
    for root, _, names in os.walk(out):
        for n in sorted(names):
            p = os.path.join(root, n)
            files.append((os.path.relpath(p, out).replace(os.sep, "/"), os.path.getsize(p), _sha(p)))
    files.sort()
    with open(os.path.join(out, "목록.csv"), "w", encoding="utf-8-sig", newline="") as f:
        c = csv.writer(f)
        c.writerow(["file", "bytes", "sha256"])
        c.writerows(files)
    e = html.escape
    lst = "".join(f"<tr><td><a href=\"{e(n)}\">{e(n)}</a></td><td>{b:,}</td><td><small>{s}</small></td></tr>" for n, b, s in files)
    w("index.html", f"""<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>검측 패키지 {e(project)} {at:%Y-%m-%d}</title>
<style>@page{{size:A4;margin:14mm}}body{{font:11pt 'Malgun Gothic',sans-serif;color:#111;margin:16px}}table{{border-collapse:collapse;width:100%}}th,td{{border:1px solid #999;padding:4px 6px;text-align:left}}th{{background:#eee}}.note{{color:#444;font-size:10pt}}{REPORT_CSS}</style></head><body>
<h1>검측 패키지</h1><p>{e(project)} · 판정 기준 {ev.kstamp(at)} · 요청 구역 {len(req)}: {e(', '.join(req))}</p>
<p class="note">{e(NOTE)} 아래 파일마다 SHA-256 — 받은 파일을 다시 계산해 같으면 보낸 그대로입니다. 목록은 목록.csv 와 같습니다.</p>
<table><tr><th>파일</th><th>바이트</th><th>SHA-256</th></tr>{lst}</table></body></html>""")
    return out, files, req


if __name__ == "__main__":
    import sys
    [s.reconfigure(errors="replace") for s in (sys.stdout, sys.stderr) if hasattr(s, "reconfigure")]
    p = argparse.ArgumentParser()
    p.add_argument("folder", nargs="?")
    p.add_argument("--at")
    p.add_argument("--self-test", action="store_true")
    o = p.parse_args()
    if o.self_test:
        import tempfile
        sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tools"))
        import make_demo_data as md
        t = tempfile.mkdtemp()
        try:
            md.make(t)
            at = datetime(2026, 12, 31, 9)
            att = os.path.join(t, "작업계획서_9F.pdf")
            open(att, "wb").write(b"%PDF-1.4 test")
            evidence.add(t, "9F-B1", "2026-12-31", 4, "시험", file=att)
            out, files, req = build(t, at, "시험")
            names = [n for n, _, _ in files]
            assert set(req) == {"9F-A1", "9F-B1", "9F-C1", "9F-C2"}, req
            assert {"검측요청서.html", "슬래브이력서.html", "감사기록.csv", "근거원장.csv", "감사기록.html", "근거원장.html"} <= set(names), names
            lh = open(os.path.join(out, "근거원장.html"), encoding="utf-8").read()
            assert "<td>첨부/9F-B1_4_작업계획서_9F.pdf</td><td>일치</td>" in lh and "<td>4 해체전점검</td>" in lh, lh       # 첨부 해시 대조 (2-6)
            assert "<td>9F-D1</td>" not in open(os.path.join(out, "감사기록.html"), encoding="utf-8").read()                # 요청 구역 줄만
            assert any(n.startswith("첨부/9F-B1_4_") for n in names), names                          # 근거 파일 사본
            assert all(_sha(os.path.join(out, n)) == s for n, _, s in files)                          # 목록 해시 = 실제 파일
            assert open(os.path.join(out, "슬래브이력서.html"), encoding="utf-8").read().count("<section>") == 4   # 요청 구역만
            ix = open(os.path.join(out, "index.html"), encoding="utf-8").read()
            assert "승인하지 않으며" in ix and all(n in ix for n in names)
            open(att, "ab").write(b" changed")                                                       # 원장 기록 뒤 파일이 바뀌면 '다름'
            build(t, at, "시험")
            assert "다름 — 원장 기록 뒤 파일이 바뀜" in open(os.path.join(out, "근거원장.html"), encoding="utf-8").read()
            out2, files2, _ = build(t, at, "시험")                                                     # 다시 만들면 바꿔 씀 (쌓이지 않음)
            assert out2 == out and len(os.listdir(t)) == len(set(os.listdir(t)))
            assert sum(1 for d in os.listdir(t) if d.startswith("검측패키지_")) == 1
            try:
                build(t, datetime(2026, 12, 1, 9), "시험")
                raise AssertionError("요청 가능 구역 없는데 만들어짐")
            except SystemExit as x:
                assert "요청 가능 구역이 없어" in str(x)
        finally:
            shutil.rmtree(t, ignore_errors=True)
        print("package self-check OK")
    else:
        if not (o.folder and o.at):
            p.error("데이터 폴더와 --at (또는 --self-test)")
        out, files, req = build(o.folder, datetime.fromisoformat(o.at))
        print(f"검측 패키지 {out} — 요청 구역 {len(req)} · 파일 {len(files)}")

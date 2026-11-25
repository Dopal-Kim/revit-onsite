"""슬래브 이력서 (docs/impact_map.md #17 F10) — 구역마다 한 장: 타설·기상·공시체·판정·해체·감리 서명·근거 해시.

왜: 양생·해체 기록이 공정 끝나면 흩어져 사라진다. 준공 때 발주처·LH·조합에 넘기는 **구역별 슬래브 품질 기록**이 되어
층간소음·하자 분쟁 때 "그 슬래브가 언제 타설돼 어떤 근거로 동바리를 뺐나"를 보여 준다(인과 단정 아님 — 기록만).
새 계산은 없다: evaluate(판정)·evidence(원장)·approval(승인 사슬)·eval_log(판정 이력)·weather_label 을 모은다.
실행: python slab_record.py <데이터 폴더> --at 2026-12-31T09:00 [--zone 9F-B1] [--html 파일] [--csv 파일]   ·   자체 검사: --self-test
"""
from report_style import REPORT_CSS
import argparse
import csv
import hashlib
import html
import json
import os
from datetime import datetime

import approval as ap
import evaluate as ev
import evidence
import photos

APPROVAL_SHOWN = ("요청 제출", "감리 승인 기록", "해체 완료", "재받침")


def records(folder, at, zone=None):
    """→ [{zone_id, floor, pour, weather, cylinders, decision, evidence, approvals, history, sha}] 층 순 → 구역 ID 순."""
    rows = {r["zone_id"]: r for r in ev.evaluate(folder, at)}
    pours = {r["zone_id"]: r for r in ev.read(folder, "pours.csv")}
    cyl = ev.cyl_rows(folder)                   # W1: 타설 묶음 공통 시험 → 묶음의 구역마다
    wlabel = ev.weather_label(ev.read(folder, "weather.csv"))
    led = evidence.ledger_at(folder, at.date())
    chain_ok, chain = ap.verify(folder)
    log = [r for r in ap.read_log(folder) if r["timestamp"][:10] <= at.date().isoformat()] if chain_ok else []
    cut = at.isoformat()[:16]
    hist = list({h.get("eval_at", ""): h for h in ev.read(folder, "eval_log.csv") if h.get("eval_at", "")[:16] <= cut}.values())   # 11-11: 판정 시각까지, 같은 시각은 마지막 한 줄
    ins = ev.install_rows(folder)   # 설치 검측 기록 (총괄 11-05: 설치 검측 결과의 전달 경로) — F16 다른 단계 줄은 뺌
    out = []
    for zid in sorted(pours if zone is None else [zone], key=ev.floor_order(folder)):   # 층 순 → 구역 ID (총괄 11-05)
        if zid not in pours:
            raise SystemExit(f"{zid}: pours.csv 에 없음")
        p, d = pours[zid], rows.get(zid)
        c = [{k: r.get(k, "") for k in ("test_date", "curing", "f1", "f2", "f3", "pour_id", "sampled_at", "supplier", "truck_no", "storage_zone", "cyl_lot")}
             for r in cyl if r["zone_id"] == zid]
        last = {}                                         # 항목마다 마지막 날짜 줄 (같은 날이면 뒤 줄)
        for r in ins:
            if (r.get("zone_id") or "").strip() == zid and (r.get("item_code") or "").strip():
                k, dd = r["item_code"].strip(), (r.get("date") or "").strip()
                if dd >= last.get(k, ("", ""))[0]:
                    last[k] = (dd, (r.get("result") or "").strip().upper())
        rec = {"zone_id": zid, "floor": d["floor"] if d else "",
               "install": {"date": max((v[0] for v in last.values()), default=""), "not_ok": [k for k, v in last.items() if v[1] != "OK"]}, "pour": {k: p.get(k, "") for k in ("pour_date", "pour_time", "mix", "fck")},
               "weather": wlabel if d and "센서 없음" not in d["est_source"] else (d["est_source"] if d else ""),
               "cylinders": c,
               "decision": {k: d[k] for k in ("decision", "reason", "f_cyl", "f_est_min", "level_gate", "predicted_due")} if d else None,
               "evidence": {"stage": led.get(zid, {}).get("stage", 0), "dates": {str(k): v for k, v in sorted(led.get(zid, {}).get("dates", {}).items())},
                            "warnings": led.get(zid, {}).get("warnings", [])},
               "approvals": [{k: r.get(k, "") for k in ("timestamp", "action", "actor", "approver", "signed_at", "sha", "source")}
                             for r in log if r["zone_id"] == zid and r["action"] in APPROVAL_SHOWN],
               "chain": chain if chain_ok else f"승인 기록 사슬 손상 — {chain}",
               "history": [{"eval_at": h.get("eval_at", ""), "summary": h.get("summary", "")} for h in hist][-5:]}
        ph = photos.rows(folder, {zid}, at.date().isoformat())      # 현장 사진 (F15) — 있을 때만 키를 둠 (사진 없는 이력서의 지문은 그대로)
        if ph:
            rec["photos"] = [{k: x[k] for k in ("when", "kind", "path", "sha256")} for x in ph]
        rec["sha"] = hashlib.sha256(json.dumps(rec, ensure_ascii=False, sort_keys=True).encode("utf-8")).hexdigest()[:12]   # 이 이력서 내용의 지문
        out.append(rec)
    return out


def page(recs, project, at):
    e = html.escape
    def one(r):
        d = r["decision"] or {}
        cyl = "".join(f"<tr><td>{e(ev.kdate(c['test_date']))}</td><td>{e(c['curing'])}</td><td>{e(c['f1'])} · {e(c['f2'])} · {e(c['f3'])}</td>"
                      f"<td>{e(' / '.join(x for x in ('타설 ' + c['cyl_lot'] if c['cyl_lot'] else c['pour_id'], ev.kstamp(c['sampled_at']), c['supplier'], c['storage_zone']) if x)) or '—'}</td></tr>" for c in r["cylinders"])
        apr = "".join(f"<tr><td>{e(ev.kstamp(a['timestamp']))}</td><td>{e(a['action'])}</td><td>{e(a['actor'])}</td><td>{e(' · '.join(x for x in (a['approver'], ap.source_label(a)) if x))}</td>"
                      f"<td>{e(ev.kstamp(a['signed_at'] or ''))}</td><td><small>{e(a['sha'][:12])}</small></td></tr>" for a in r["approvals"])
        ledger = " · ".join(f"{k} {ev.kdate(v)}" for k, v in r["evidence"]["dates"].items()) or "없음"
        warn = "".join(f"<li>{e(w)}</li>" for w in r["evidence"]["warnings"])
        photo = ("<tr><th>현장 사진</th><td>" + "<br>".join(f"{e(ev.kstamp(x['when']))} {e(x['kind'])} — {e(x['path'])} · <small>{x['sha256'][:12]}</small>"
                                                         for x in r["photos"]) + "</td></tr>") if r.get("photos") else ""   # 경로·해시 (F15)
        return f"""<section><h2>{e(r['zone_id'])} <small>슬래브 이력서 · 지문 {r['sha']}</small></h2>
<table><tr><th>타설</th><td>{e(ev.kdate(r['pour']['pour_date']))} {e(r['pour']['pour_time'])} · {e(r['pour']['mix'])} · fck {e(str(r['pour']['fck']))} MPa</td></tr>
<tr><th>설치 검측</th><td>{e(ev.kdate(r['install']['date'])) if r['install']['date'] else '기록 없음'}{' · 확인 필요: ' + e(', '.join(r['install']['not_ok'])) if r['install']['not_ok'] else ''}</td></tr>
<tr><th>기온 근거</th><td>{e(r['weather'])}</td></tr>
<tr><th>판정 ({ev.kstamp(at.isoformat())})</th><td>{e(d.get('decision', '판정 없음'))} — {e(d.get('reason', ''))}<br><small>공시체 {e(str(d.get('f_cyl', '')))} · 구역 최소 추정 {e(str(d.get('f_est_min', '')))} MPa · 규칙 게이트 {e(str(d.get('level_gate', '')))} · 예측 도달 {e(ev.kdate(d.get('predicted_due', '')))}</small></td></tr>
<tr><th>근거 원장</th><td>{r['evidence']['stage']}/8 — {e(ledger)}{'<ul class="warn">' + warn + '</ul>' if warn else ''}</td></tr>{photo}</table>
<h3>공시체</h3><table><tr><th>시험일</th><th>양생</th><th>MPa (3개)</th><th>채취 정보</th></tr>{cyl or '<tr><td colspan=4>없음</td></tr>'}</table>
<h3>승인·해체 기록</h3><table><tr><th>기록 시각</th><th>종류</th><th>입력자</th><th>감리</th><th>서명 일시</th><th>줄 해시</th></tr>{apr or '<tr><td colspan=6>없음</td></tr>'}</table>
</section>"""
    hist = "".join(f"<li>{e(ev.kstamp(h['eval_at']))} — {e(h['summary'])}</li>" for h in (recs[0]["history"] if recs else []))
    return f"""<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>슬래브 이력서 {e(project)} {at:%Y-%m-%d}</title>
<style>@page{{size:A4;margin:14mm}}body{{font:10.5pt 'Malgun Gothic',sans-serif;color:#111}}section{{page-break-after:always}}h2{{font-size:15pt;margin:0 0 6px}}h2 small{{font-size:9pt;color:#555;font-weight:400}}
h3{{font-size:11pt;margin:10px 0 4px}}table{{border-collapse:collapse;width:100%}}th,td{{border:1px solid #999;padding:4px 6px;text-align:left;vertical-align:top}}th{{background:#eee;white-space:nowrap}}.warn{{color:#a00;margin:4px 0 0 16px}}.note{{color:#444;font-size:9pt}}{REPORT_CSS}</style></head><body>
<p class="note">{e(project)} · 기준 {ev.kstamp(at.isoformat())} · Revit On-Site 데이터 폴더 기록을 모은 것 (새 계산 없음). 이 문서는 품질 기록이며 해체·사용 승인이 아닙니다. 최근 판정 이력: <ul class="note">{hist or '<li>없음</li>'}</ul></p>
{''.join(one(r) for r in recs)}</body></html>"""


def write_csv(recs, path):
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(["zone_id", "pour_date", "decision", "evidence_stage", "approvals", "cylinders", "warnings", "sha"])
        for r in recs:
            w.writerow([r["zone_id"], r["pour"]["pour_date"], (r["decision"] or {}).get("decision", ""), f"{r['evidence']['stage']}/8",
                        " · ".join(f"{a['timestamp'][:10]} {a['action']}" for a in r["approvals"]), len(r["cylinders"]),
                        " · ".join(r["evidence"]["warnings"]), r["sha"]])


if __name__ == "__main__":
    import sys
    [s.reconfigure(errors="replace") for s in (sys.stdout, sys.stderr) if hasattr(s, "reconfigure")]
    a = argparse.ArgumentParser()
    a.add_argument("folder", nargs="?")
    a.add_argument("--at")
    a.add_argument("--zone")
    a.add_argument("--html")
    a.add_argument("--csv")
    a.add_argument("--self-test", action="store_true")
    o = a.parse_args()
    if o.self_test:
        import shutil
        import tempfile
        sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tools"))
        import make_demo_data as md
        t = tempfile.mkdtemp()
        try:
            md.make(t)
            at = datetime(2026, 12, 31, 9)
            rs = records(t, at)
            assert len(rs) == len(ev.read(t, "pours.csv")) and all(len(r["sha"]) == 12 for r in rs)
            b1 = records(t, at, "9F-B1")[0]
            assert b1["decision"]["decision"] == "해체 승인 요청 가능" and b1["evidence"]["stage"] >= 3 and b1["cylinders"], b1["evidence"]
            ap.record(t, "9F-B1", "요청 제출", "시험", next(r for r in ev.evaluate(t, at) if r["zone_id"] == "9F-B1"), now=at)
            b2 = records(t, at, "9F-B1")[0]
            assert [x["action"] for x in b2["approvals"]] == ["요청 제출"] and b2["sha"] != b1["sha"]       # 기록이 늘면 지문이 바뀜
            assert records(t, datetime(2026, 12, 30, 9), "9F-B1")[0]["approvals"] == []                    # 기준일 뒤 기록은 안 보임
            h = page(rs, "시연", at)
            assert h.count("<section>") == len(rs) and "슬래브 이력서" in h and "승인이 아닙니다" in h
            write_csv(rs, os.path.join(t, "x.csv"))
            assert sum(1 for _ in open(os.path.join(t, "x.csv"), encoding="utf-8-sig")) == len(rs) + 1
        finally:
            shutil.rmtree(t, ignore_errors=True)
        print("slab_record self-check OK")
    else:
        if not (o.folder and o.at):
            a.error("데이터 폴더와 --at 이 필요합니다 (또는 --self-test)")
        at = datetime.fromisoformat(o.at)
        rs = records(o.folder, at, o.zone)
        if o.html:
            open(o.html, "w", encoding="utf-8").write(page(rs, os.path.basename(os.path.abspath(o.folder)), at))
        if o.csv:
            write_csv(rs, o.csv)
        for r in rs:
            print(r["zone_id"], (r["decision"] or {}).get("decision", "판정 없음"), f"근거 {r['evidence']['stage']}/8", f"승인 {len(r['approvals'])}", r["sha"])

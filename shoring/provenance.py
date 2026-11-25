"""11-12 구역 서류의 '기본 사항·출처' — 검토서에 자동으로 넣는 값마다 누가(역할)·언제·어떤 경로로 넣었는지 (정보 신뢰성).
계산·판정 없음: 데이터 폴더 기록(pours·cylinders·shore_spec·install_inspection·evidence·approvals)을 그대로 옮긴다.
역할 = options.json actor_roles {이름: 역할} ([프로젝트 설정] '관리자 역할'). C# Engine Provenance.cs 와 같은 글자 (Engine.Check 비교).
실행: python provenance.py <데이터 폴더> <구역> [--at YYYY-MM-DDTHH:MM]   ·   자체 검사: --self-test
"""
import html
import sys
from datetime import datetime

import approval as ap
import evaluate as ev
import evidence as evd
import shore_input as sin

HEAD = ("항목", "값", "입력 — 누가 (역할) · 언제 · 경로")   # 한 장 검토서: 출처 세 칸을 한 칸으로 (줄바꿈 줄임)
NOTE = "자동 입력 — 데이터 폴더 기록 그대로 (계산 없음). 입력자 '—' = 이름 없이 들어온 기록(옛 기록·시연 자료) — 원본 서류로 확인."


def via(r):
    """기록 경로: Revit 창(client_id revit-) · 휴대폰 현장 입력(기기) · 파일 기록(직접·시연 자료)"""
    cid, dev = (r.get("client_id") or "").strip(), (r.get("device") or "").strip()
    if cid.startswith("revit-"):
        return "Revit 창"
    if dev:
        return f"현장 입력 ({dev})"
    return "현장 입력" if cid else "파일 기록"


def who_fn(folder):
    """이름 → '이름 (역할)' (역할 없으면 이름, 빈칸 그대로) — options.json actor_roles"""
    roles = ev.roles(folder)
    return lambda n: (lambda s: f"{s} ({roles[s]})" if s in roles else s)((n or "").strip())


def cyl_meta(folder):
    """(구역 또는 'lot:'+타설 묶음, 시험일) → (입력자 (역할), 입력 시각 YYYY-MM-DDTHH:MM), 같은 키는 뒤 줄 — 스냅숏 cyl_entered_* (API.md §10.5)"""
    who = who_fn(folder)
    return {((c.get("zone_id") or "").strip() or "lot:" + (c.get("pour_id") or "").strip(), c.get("test_date") or ""):
            (who(c.get("entered_by")), (c.get("entered_at") or "").strip()[:16]) for c in ev.read(folder, "cylinders.csv")}


def evidence_meta(folder, day):
    """→ {구역: {"단계": {"by": 입력자 (역할), "at": 날짜}}} 근거 원장(evidence.csv) 단계마다 day 까지 마지막 줄 — 스냅숏 check.evidence_meta"""
    who, out = who_fn(folder), {}
    for r in ev.read(folder, evd.LEDGER):
        if (r.get("date") or "") <= day:
            out.setdefault(r.get("zone_id") or "", {})[str(int(r.get("stage") or 0))] = {"by": who(r.get("by")), "at": r.get("date") or ""}
    return out


def _when(s):
    return (s or "").strip().replace("T", " ")[:16]


def facts(folder, zone_id, at):
    """→ [(항목, 값, 입력자 (역할), 입력 시각, 경로)] 판정 시각 at 날짜까지의 기록, 정해진 순서 (타설 → 공시체 → 사양 → 설치 검측 → 근거 원장 → 승인 기록)"""
    day, who, out = at.date().isoformat(), who_fn(folder), []

    def add(item, value, name, when, how):
        out.append((item, value, who(name) or "—", _when(when), how))

    zone = next((r for r in ev.read(folder, "zones.csv") if r.get("zone_id") == zone_id), None)
    pours = [r for r in ev.read(folder, "pours.csv") if r.get("zone_id") == zone_id]
    lot = (pours[-1].get("pour_id") or "").strip() if pours else ""
    if pours:
        p = pours[-1]
        wit = " · ".join(x for x in (f"입회 감리 {p['supervisor_present']}" if (p.get("supervisor_present") or "").strip() else "",
                                    f"감시자 {p['watcher']}" if (p.get("watcher") or "").strip() else "") if x)
        add("타설", f"{p['pour_date']} {p.get('pour_time') or ''} · {p.get('mix') or ''} · fck {p.get('fck') or ''} MPa" + (f" · {wit}" if wit else ""),
            p.get("entered_by"), p.get("entered_at"), via(p))
    for c in ev.read(folder, "cylinders.csv"):
        own = c.get("zone_id") == zone_id
        if (own or (not (c.get("zone_id") or "").strip() and lot and (c.get("pour_id") or "").strip() == lot)) and (c.get("test_date") or "") <= day:
            add("공시체", f"{c['test_date']} {c.get('curing') or ''} {c.get('f1') or ''}·{c.get('f2') or ''}·{c.get('f3') or ''} MPa" + ("" if own else f" (타설 {lot} 공통)"),
                c.get("entered_by"), c.get("entered_at"), via(c))
    sp = sin.spec_of(folder, int(zone["floor"]), zone_id) if zone else None
    if sp:
        add("동바리 사양", f"{sp.get('type') or ''} {sp.get('spec') or ''} · {sp.get('spacing_x_mm') or ''}×{sp.get('spacing_y_mm') or ''} mm · 설치 {sp.get('install_date') or ''}"
            + ("" if (sp.get("zone_id") or "").strip() else " (층 전체)"), sp.get("entered_by"), sp.get("entered_at"), via(sp))
    ins = [r for r in ev.read(folder, "install_inspection.csv") if r.get("zone_id") == zone_id and (r.get("date") or "") <= day]
    if ins:
        r = ins[-1]
        add("설치 검측", f"{len(ins)}줄 · 마지막 {r.get('date') or ''} {r.get('item_code') or ''} {r.get('result') or ''}",
            r.get("entered_by") or r.get("inspector"), r.get("entered_at") or r.get("date"), via(r))
    for r in ev.read(folder, evd.LEDGER):
        if r.get("zone_id") == zone_id and (r.get("date") or "") <= day:
            st = int(r.get("stage") or 0)
            add(f"근거 원장 {st} {evd.STAGES.get(st, '')}", " ".join(x for x in ((r.get("doc_no") or "").strip(), (r.get("result") or "").strip(),
                "파일 SHA-256 " + r["sha256"][:12] if (r.get("sha256") or "").strip() else "") if x), r.get("by"), r.get("date"), "근거 원장")
    for r in ap.read_log(folder):
        if r.get("zone_id") == zone_id and (r.get("timestamp") or "")[:10] <= day:
            sign = (r.get("approver") or "").strip()
            add(r.get("action") or "", ((r.get("note") or "").strip() + (f" · 서명 감리 {sign} {_when(r.get('signed_at'))}" if sign else "")).strip(" ·"),
                r.get("actor"), r.get("timestamp"), "승인 기록 (해시 사슬)" + (f" · {r['source']}" if (r.get("source") or "").strip() else ""))
    return out


def table_html(rows):
    """facts → '기본 사항·출처' 절 (검토서에 그대로 넣음)"""
    q = html.escape
    body = "".join(f"<tr><td>{q(r[0])}</td><td>{q(r[1])}</td><td>{q(' · '.join(c for c in r[2:] if c))}</td></tr>" for r in rows) or f"<tr><td colspan=\"{len(HEAD)}\">기록 없음</td></tr>"
    return (f"<h2>붙임. 기본 사항·출처</h2><table class=\"prov\"><tr>{''.join(f'<th>{h}</th>' for h in HEAD)}</tr>{body}</table>"
            f"<p class=\"note\">{q(NOTE)}</p>")


CSS = ".prov th,.prov td{font-size:8pt;padding:1px 4px}.prov td:first-child{white-space:nowrap}"


def _self_test():
    import os
    import shutil
    import tempfile
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    t = tempfile.mkdtemp(prefix="prov_")
    try:
        f = os.path.join(t, "d")
        shutil.copytree(os.path.join(root, "revit", "onsite_data_demo"), f)
        with open(os.path.join(f, ev.OPTIONS), "w", encoding="utf-8") as o:
            o.write('{"actor_roles": {"시연용": "공무", "빈칸": " "}}')
        rows = facts(f, "9F-B1", datetime(2026, 12, 31, 9))
        items = [r[0] for r in rows]
        assert items[0] == "타설" and "공시체" in items and any(i.startswith("근거 원장") for i in items), items
        assert all(r[2] in ("—", "시연용 (공무)") or "(" not in r[2] for r in rows), rows
        assert ev.roles(f) == {"시연용": "공무"}
        assert "<table class=\"prov\">" in table_html(rows) and "기록 없음" in table_html([])
        assert via({"client_id": "revit-ab"}) == "Revit 창" and via({"device": "Android Chrome", "client_id": "x"}) == "현장 입력 (Android Chrome)" and via({}) == "파일 기록"
    finally:
        shutil.rmtree(t, ignore_errors=True)
    print("provenance self-check OK")


if __name__ == "__main__":
    if "--self-test" in sys.argv:
        _self_test()
    else:
        a = sys.argv[1:]
        at = datetime.fromisoformat(a[a.index("--at") + 1]) if "--at" in a else datetime.now()
        for r in facts(a[0], a[1], at):
            print(" | ".join(r))

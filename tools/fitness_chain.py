"""ⓐ 기능 적합성 검사 1층 — 시연 흐름을 Python 기준으로 끝까지 돌려 impact_map 의 결과물이 실제로 나오는지 한 줄씩 (docs/impact_map.md 2절).

임시 폴더(끝나면 지움)에서: 시연 자료 → 판정 → 9F-B1 요청 제출 → 13F 타설 전 확인('예' 3건) → 9F-A2 '아니오' → 근거 원장 4 → 감리 승인(성명·서명 일시)
→ 검측요청서 · 오늘 볼 것 · 슬래브 이력서 · 품질 대장
→ 단계 해체(보온 5 ℃ / 가열 15 ℃ — 양생 하한 인자만, 폴더는 그대로) · 조기 탈형 검토서 · 동바리 타임라인 · 동바리 상태 · 웹 4D 시공 순서(cycle).
결과물마다 "있음/없음 · 크기 · 핵심 한 줄".
실행: python tools\\fitness_chain.py   (Revit 없음 — 층 표지·IFC 는 C# 전용이라 ② 오류 검토 E2·E20 에서)
"""
import os
import shutil
import sys
import tempfile
from datetime import date, datetime

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path[:0] = [os.path.join(ROOT, "shoring"), os.path.join(ROOT, "tools")]
import alerts as al          # noqa: E402
import approval as ap        # noqa: E402
import evaluate as ev        # noqa: E402
import evidence              # noqa: E402
import make_demo_data as md  # noqa: E402
import pour_gate as pg       # noqa: E402
import quality               # noqa: E402
import shore_status as ss    # noqa: E402
import slab_record as sr     # noqa: E402
import staged                # noqa: E402


def line(name, path_or_text, key):
    if path_or_text is None:
        print(f"없음 {name}")
        return
    size = os.path.getsize(path_or_text) if os.path.exists(str(path_or_text)) else len(str(path_or_text))
    print(f"있음 {name:14s} {size:>7,}B  {key}")


if __name__ == "__main__":
    [s.reconfigure(errors="replace") for s in (sys.stdout, sys.stderr) if hasattr(s, "reconfigure")]
    t = tempfile.mkdtemp()
    try:
        md.make(t)
        zs = ev.read(t, "zones.csv")       # Revit 이 더하는 13F 구역 흉내
        with open(os.path.join(t, "zones.csv"), "a", encoding="utf-8") as f:
            f.write("".join(f"13F-{z['zone_id'].split('-', 1)[1]},13,{z['x0']},{z['y0']},{z['x1']},{z['y1']}\n" for z in zs if z["floor"] == "12"))
        at = datetime(2026, 12, 31, 9)
        rows = ev.evaluate(t, at)
        ev.write(rows, os.path.join(t, "zone_decisions.csv"))
        n_req = sum(r["decision"] == "해체 승인 요청 가능" for r in rows)
        line("판정표", os.path.join(t, "zone_decisions.csv"), f"요청 가능 {n_req} · 존치 {len(rows) - n_req}")
        b1 = next(r for r in rows if r["zone_id"] == "9F-B1")
        ap.record(t, "9F-B1", "요청 제출", "적합성 시험", b1, now=at)
        print(f"     13F-B1 타설 전 (확인 전): {pg.pour_gate(t, '13F-B1', at)['decision']}")
        with open(os.path.join(t, "shore_checks.csv"), "w", encoding="utf-8") as f:
            f.write("zone_id,timestamp,present,actor,photo\n" + "".join(f"{z},2026-12-31T08:30,예,안전관리자,\n" for z in ("12F-B1", "11F-B1", "10F-B1"))
                    + "9F-A2,2026-12-31T08:40,아니오,안전관리자,\n")
        print(f"     13F-B1 타설 전 (확인 3건 뒤): {pg.pour_gate(t, '13F-B1', at)['decision']}")
        evidence.add(t, "9F-B1", "2026-12-31", 4, "안전관리자")
        ap.record(t, "9F-B1", "감리 승인 기록", "공무", b1, now=at, approver="감리(시험)", signed_at="2026-12-31 10:00")
        led = evidence.ledger_at(t, date(2026, 12, 31))
        print(f"     근거 원장 9F-B1 {led['9F-B1']['stage']}/8 · 승인 사슬 {ap.verify(t)}")
        req = ap.request_html(ev.evaluate(t, at), "적합성 시험", at, t)
        p = os.path.join(t, "해체검측요청서.html")
        open(p, "w", encoding="utf-8").write(req)
        seal = req.split("마감 값")[1][:40] if "마감 값" in req else "마감 값 없음"
        line("검측요청서", p, f"현장 확인 3가지 {'있음' if '현장에서 확인' in req else '없음'} · 4.2 {'있음' if '4.2' in req else '없음'} · 무단 해체 {'있음' if '무단 해체' in req else '없음'}")
        k = al.alerts(t, at)
        line("오늘 볼 것", "\n".join(x["text"] for x in k), " · ".join(f"{n} {sum(x['kind'] == n for x in k)}" for n in al.ORDER if any(x['kind'] == n for x in k)))
        recs = sr.records(t, at)
        p = os.path.join(t, "슬래브이력서.html")
        open(p, "w", encoding="utf-8").write(sr.page(recs, "적합성 시험", at))
        b = next(r for r in recs if r["zone_id"] == "9F-B1")
        line("슬래브 이력서", p, f"{len(recs)}구역 · 9F-B1 근거 {b['evidence']['stage']}/8 · 승인 {len(b['approvals'])}건 · 감리 {b['approvals'][-1]['approver'] if b['approvals'] else '-'} · 지문 {b['sha']}")
        q = quality.quality_ledger(t)
        line("품질 대장(참고)", quality.quality_html(q), f"{len(q)}구역 · " + " · ".join(f"{v} {sum(r['result'] == v for r in q)}" for v in sorted({r['result'] for r in q})))
        # 단계 해체 (staged): 9F 판넬 결론·9F-A1 벽 거푸집 해체일 — 보온 5 ℃ / 가열 15 ℃ (양생 하한 인자만, 폴더는 그대로)
        for cm in (5.0, 15.0):
            stg = staged.stages(t, at, cm)
            nine = [v["panel"]["decision"] for z, v in stg.items() if z.startswith("9F-")]
            w = staged.wall_strip(t, "9F-A1", at, cm)
            line(f"단계 해체 {cm:g} ℃", None if not stg else str(stg),
                 "9F 판넬 결론: " + " · ".join(f"{d} {nine.count(d)}" for d in dict.fromkeys(nine)) + f" · 9F-A1 벽 거푸집 해체 {w['date_by_strength']} (무시험 {w['date_by_days'] or '표 범위 밖'})")
        p = os.path.join(t, "조기탈형검토서_9F-A1.html")
        open(p, "w", encoding="utf-8").write(staged.review_html(t, at, "9F-A1", project="적합성 시험"))
        a1 = staged.stages(t, at, 5.0)["9F-A1"]
        line("조기 탈형 검토서", p, f"9F-A1 {a1['panel']['decision']} — " + " ".join(c[0][:1] + ("✓" if c[1] else "✕") for c in a1["panel"]["checks"]) + f" · 필러 {a1['filler']['decision']}")
        sts = ss.status(t, at)
        line("동바리 상태", ss.dump(sts), " · ".join(f"{k} {sum(x['state'] == k for x in sts)}" for k, *_ in ss.LEGEND if sum(x['state'] == k for x in sts))
             + f" · 설치 검측 필요/NG {sum(bool(x['install']) for x in sts)} · 24 h 확인 없음 {sum(bool(x['stale']) for x in sts)}")
        import strength_timeline as stt                               # 강도 예측 타임라인 (v6 3단계, 애드인·웹 공유판 공통): 이 시험 폴더 기록
        st = stt.timeline(t)
        i31 = st["dates"].index(at.date()) if at.date() in st["dates"] else None
        line("강도 예측 타임라인", stt.dump(st), f"{st['start']}~{st['end']} · 12-31 추정(참고) "
             + " · ".join(f"{f['label']} {f['values'][i31]:g}" for f in st["floors"] if i31 is not None and f["values"][i31] is not None)
             + f" MPa · 공시체 점 {len(st['cylinders'])}")
        print("없음 층 표지·IFC — C# 전용 (② E2·E20)")
    finally:
        shutil.rmtree(t, ignore_errors=True)

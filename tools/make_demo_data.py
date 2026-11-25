r"""시연 데이터 폴더 만들기 — 샘플(shoring/sample/data) + "시연용" 가상 근거 기록.
아무것도 지우지 않는다: 대상 폴더에 이미 파일이 있으면 `_보관_YYYYMMDD_HHMMSS\` 로 옮긴 뒤 새로 만든다.
가상 기록은 모두 입력자/문서번호에 "시연용" 을 붙인다 (실측·실제 서류로 보이지 않게 — make_sample.py 가상값과 같은 원칙).

만드는 것:
  zones·pours·sensors·cylinders.csv   샘플 그대로 (9–12F)
  install_inspection.csv              9–12F 구역마다 설치검측 3항목(높이·이음·연직도) OK (타설 2일 전, 입력자 "시연용")  → 근거 1
  evidence.csv                        9F 구역 작업계획서(12-20, "시연용-WP-9F")                          → 근거 2
                                      (근거 3 = 샘플 공시체 12-30. 4 해체전점검·5 검측요청·6 감리확인은 시연 중 직접 입력)
  approvals.csv·shore_checks.csv      만들지 않음 — 시연에서 [승인 기록]·휴대폰 동바리 확인으로 넣음
실행: python make_demo_data.py <대상 폴더>     (예: revit\\onsite_data_demo)
      python make_demo_data.py <대상 폴더> --sim   (시나리오 시뮬레이션 시작 상태 11-24 아침 — sim() 설명, 기존 make() 결과는 그대로)
"""
import csv
import os
import shutil
import sys
from datetime import date, datetime, timedelta

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SAMPLE = os.path.join(ROOT, "shoring", "sample", "data")
sys.path.insert(0, os.path.join(ROOT, "shoring"))
import evidence as evd   # noqa: E402

TAG = "시연용"


def make(target):
    os.makedirs(target, exist_ok=True)
    old = [n for n in os.listdir(target) if not n.startswith("_보관_")]
    if old:
        keep = os.path.join(target, f"_보관_{datetime.now():%Y%m%d_%H%M%S}")
        os.makedirs(keep, exist_ok=True)
        for n in old:
            shutil.move(os.path.join(target, n), os.path.join(keep, n))
    for n in ("zones.csv", "pours.csv", "sensors.csv", "cylinders.csv"):
        shutil.copy(os.path.join(SAMPLE, n), target)
    with open(os.path.join(target, "pours.csv"), encoding="utf-8-sig", newline="") as f:
        pours = list(csv.DictReader(f))
    with open(os.path.join(target, "install_inspection.csv"), "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        w.writerow(["zone_id", "item_code", "result", "inspector", "date", "photo_path"])
        for p in pours:
            d = (date.fromisoformat(p["pour_date"]) - timedelta(2)).isoformat()
            for item in ("SHR-H", "SHR-S", "SHR-V"):   # 높이·이음·연직도 (설치 검측 항목 예, 연직도 = install_rules.plumb_check 수동 항목)
                w.writerow([p["zone_id"], item, "OK", TAG, d, ""])
    for p in pours:
        if p["zone_id"].startswith("9F-"):
            evd.add(target, p["zone_id"], "2026-12-20", 2, TAG, doc_no=f"{TAG}-WP-9F", result="작성")
            # 시연용 조기 탈형 구조검토서(staged ④) — 같은 날짜라 근거 단계·첫 날짜·마감 값은 그대로. 겨울 보온 5 ℃ 에선 ② 로 여전히 '판넬 존치',
            # [프로젝트 설정] 양생 하한 15 ℃(가열) 로 바꾸면 판넬 탈형 → 필러만 단계가 4D 에 보임 (총괄 11-05, 시연 비교용)
            evd.add(target, p["zone_id"], "2026-12-20", 2, TAG, doc_no=f"{TAG}-구조검토-9F", result="작성")
    return old


def from_zones(target, start, cycle=7, at=None, mix="보통", fck=24.0):
    """다른 시험 모델용 (KBIM 등): Revit 이 쓴 target\\zones.csv 를 그대로 두고, 층 순서대로 "시연용" 기록을 만든다.
    pours = 첫 타설일 + 사이클 × 층 순번 (판정 시각 at 이전만, 08:00) · cylinders = 엔진 예측으로 fck 도달일(평년 기온, 보온 5 ℃)에
    현장봉함 fck+0.4·+0.8·+1.2 (at 이전만) · install_inspection = 타설 2일 전 3항목 OK. 다른 파일은 _보관_ 으로 옮김(지우지 않음).
    가상 기록이라 폴더에 `시연용_가상데이터.txt` 를 남긴다 (cylinders.csv 엔 표시할 열이 없어서)."""
    import engine as e
    zp = os.path.join(target, "zones.csv")
    if not os.path.exists(zp):
        raise SystemExit(f"{zp} 없음 — Revit 에서 [구역·동바리 생성] 뒤 [판정] 을 한 번 눌러 zones.csv 를 만드세요")
    old = [n for n in os.listdir(target) if not n.startswith("_보관_") and n != "zones.csv"]
    if old:
        keep = os.path.join(target, f"_보관_{datetime.now():%Y%m%d_%H%M%S}")
        os.makedirs(keep, exist_ok=True)
        for n in old:
            shutil.move(os.path.join(target, n), os.path.join(keep, n))
    with open(zp, encoding="utf-8-sig", newline="") as f:
        zones = list(csv.DictReader(f))
    floors = sorted({int(z["floor"]) for z in zones})
    at = at or datetime(start.year, start.month, start.day) + timedelta(days=cycle * len(floors))
    pours, cyl, ins = [], [], []
    for i, fl in enumerate(floors):
        d = start + timedelta(cycle * i)
        if datetime(d.year, d.month, d.day, 8) > at:
            break
        need = next(a for a in range(1, 400) if e.predict(mix, d, a, fck, 5.0) >= fck)
        for z in (z for z in zones if int(z["floor"]) == fl):
            pours.append([z["zone_id"], d.isoformat(), "08:00", mix, f"{fck:g}"])
            ins += [[z["zone_id"], item, "OK", TAG, (d - timedelta(2)).isoformat(), ""] for item in ("SHR-H", "SHR-S", "SHR-V")]
            t = d + timedelta(need)
            if t <= at.date():
                cyl.append([z["zone_id"], t.isoformat(), "현장봉함", f"{fck + 0.4:g}", f"{fck + 0.8:g}", f"{fck + 1.2:g}"])
    for name, head, rows in (("pours.csv", "zone_id,pour_date,pour_time,mix,fck", pours),
                             ("cylinders.csv", "zone_id,test_date,curing,f1,f2,f3", cyl),
                             ("install_inspection.csv", "zone_id,item_code,result,inspector,date,photo_path", ins)):
        with open(os.path.join(target, name), "w", encoding="utf-8", newline="") as f:
            f.write(head + "\n")
            csv.writer(f).writerows(rows)
    with open(os.path.join(target, "시연용_가상데이터.txt"), "w", encoding="utf-8") as f:
        f.write(f"이 폴더의 pours·cylinders·install_inspection 은 make_demo_data.py --from-zones 가 만든 가상(시연용) 기록입니다.\n"
                f"첫 타설 {start} · {cycle}일 사이클 · {mix} fck {fck:g} · 판정 시각 {at:%Y-%m-%d %H:%M} · 만든 시각 {datetime.now():%Y-%m-%d %H:%M}\n")
    return {"floors": len(floors), "poured": len({p[0].split('-')[0] for p in pours}), "pours": len(pours), "cylinders": len(cyl), "moved": old}


def sim(target, at=datetime(2026, 11, 24, 8), start=date(2026, 10, 1), cycle=7, top=15, mix="보통", fck=24.0):
    """시나리오 시뮬레이션 시작 상태 (총괄 61차, docs/simulation/scenario.md) — 기본 11-24 아침. 기록은 모두 '시연용'(가상).
    zones = 샘플 9F 평면을 1–top F 에 (모델 구역 이름 nF-A1…) · 타설 = 첫 타설 start + cycle × (층−1), at 전 층만 (1–8F) ·
    설치 검측 = 타설 2일 전 3항목 OK · 현장봉함 공시체 = 엔진 예측 fck 도달일(평년 기온, 보온 5 ℃)에 fck+0.4·+0.8·+1.2 (at 전만) ·
    동바리 확인 '예' = 다음 층 타설 전날 16:00 아래 3개층 · 해체 = 엔진이 그 층 전부 '해체 승인 요청 가능'으로 본 첫날 09:00 판정으로
    작업계획서(2일 전)·해체전점검·요청 제출·감리 승인(서명)·다음 날 해체 완료·해체 후 점검 5항목 OK (→ 원장 1–8).
    9F 이상은 타설 전 (pours·cylinders·install 없음 — 시나리오 2·4·6단계에서 넣음). 센서 기록은 샘플 그대로 (9F 타설 등록 뒤 쓰임)."""
    import approval as ap
    import engine as e
    import evaluate as ev
    os.makedirs(target, exist_ok=True)
    old = [n for n in os.listdir(target) if not n.startswith("_보관_")]
    if old:
        keep = os.path.join(target, f"_보관_{datetime.now():%Y%m%d_%H%M%S}")
        os.makedirs(keep, exist_ok=True)
        for n in old:
            shutil.move(os.path.join(target, n), os.path.join(keep, n))
    shutil.copy(os.path.join(SAMPLE, "sensors.csv"), target)
    with open(os.path.join(SAMPLE, "zones.csv"), encoding="utf-8-sig", newline="") as f:
        plan = [z for z in csv.DictReader(f) if z["floor"] == "9"]
    zones = {fl: [dict(z, zone_id=f"{fl}F-{z['zone_id'].split('-', 1)[1]}", floor=str(fl)) for z in plan] for fl in range(1, top + 1)}
    pour_day = {fl: start + timedelta(cycle * (fl - 1)) for fl in zones if datetime.combine(start + timedelta(cycle * (fl - 1)), datetime.min.time()) + timedelta(hours=8) < at}
    pours, cyl, ins, chk = [], [], [], []
    for fl, d in pour_day.items():
        need = next(a for a in range(1, 400) if e.predict(mix, d, a, fck, 5.0) >= fck)
        for z in zones[fl]:
            pours.append([z["zone_id"], d.isoformat(), "08:00", mix, f"{fck:g}"])
            ins += [[z["zone_id"], item, "OK", TAG, (d - timedelta(2)).isoformat(), ""] for item in ("SHR-H", "SHR-S", "SHR-V")]
            if d + timedelta(need) < at.date():
                cyl.append([z["zone_id"], (d + timedelta(need)).isoformat(), "현장봉함", f"{fck + 0.4:g}", f"{fck + 0.8:g}", f"{fck + 1.2:g}"])
        chk += [[z["zone_id"], f"{d - timedelta(1)}T16:00", "예", TAG, ""] for q in (fl - 1, fl - 2, fl - 3) if q in zones and q >= 1 for z in zones[q]]
    for name, head, rows in (("zones.csv", "zone_id,floor,x0,y0,x1,y1", [[z[k] for k in ("zone_id", "floor", "x0", "y0", "x1", "y1")] for fl in zones for z in zones[fl]]),
                             ("pours.csv", "zone_id,pour_date,pour_time,mix,fck", pours),
                             ("cylinders.csv", "zone_id,test_date,curing,f1,f2,f3", cyl),
                             ("install_inspection.csv", "zone_id,item_code,result,inspector,date,photo_path", ins),
                             ("shore_checks.csv", "zone_id,timestamp,present,actor,photo", chk)):
        with open(os.path.join(target, name), "w", encoding="utf-8", newline="") as f:
            f.write(head + "\n")
            csv.writer(f, lineterminator="\n").writerows(rows)
    removed, after = {}, []
    for fl in sorted(pour_day):
        ids = [z["zone_id"] for z in zones[fl]]
        d = pour_day.get(fl + 3)                                    # 타설층 포함 3개층 존치 → 3개층 위가 타설된 다음 날부터 찾음
        while d is not None and d + timedelta(2) < at.date():
            d += timedelta(1)
            rows = {r["zone_id"]: r for r in ev.evaluate(target, datetime.combine(d, datetime.min.time()) + timedelta(hours=9))}
            if all(rows.get(z, {}).get("decision") == "해체 승인 요청 가능" for z in ids):
                break
        else:
            continue
        for z in ids:
            evd.add(target, z, d - timedelta(2), 2, TAG, doc_no=f"{TAG}-WP-{fl}F", result="작성")
            evd.add(target, z, d, 4, TAG, doc_no=f"{TAG}-PRE-{fl}F", result="적합")
        t0 = datetime.combine(d, datetime.min.time())
        for z in ids:
            ap.record(target, z, "요청 제출", TAG, rows[z], note="검측요청서 제출", now=t0 + timedelta(hours=9, minutes=30))
        for z in ids:
            ap.record(target, z, "감리 승인 기록", TAG, rows[z], now=t0 + timedelta(hours=14), approver=f"{TAG} 감리", signed_at=f"{d}T13:50")
        for z in ids:
            ap.record(target, z, "해체 완료", TAG, rows[z], now=t0 + timedelta(days=1, hours=10))
        after += [[z, (d + timedelta(1)).isoformat(), c, "OK", TAG, ""] for z in ids for c in ("AC-1", "AC-2", "AC-3", "AC-4", "AC-5")]
        for z in ids:                                                # 11-12 G10: 원장 8 은 결과보고 줄만 (해체 후 점검 OK 로 자동 8 이 아님)
            evd.add(target, z, d + timedelta(1), 8, TAG, doc_no=f"{TAG}-RR-{fl}F", result="결과보고서")
        removed[fl] = d + timedelta(1)
    with open(os.path.join(target, "after_check.csv"), "w", encoding="utf-8", newline="") as f:
        f.write("zone_id,date,item_code,result,by,photo\n")
        csv.writer(f, lineterminator="\n").writerows(after)
    with open(os.path.join(target, "시연용_가상데이터.txt"), "w", encoding="utf-8") as f:
        f.write(f"이 폴더의 기록은 make_demo_data.py --sim 이 만든 가상(시연용) 기록입니다 (시나리오 시뮬레이션 시작 상태).\n"
                f"시작 시각 {at:%Y-%m-%d %H:%M} · 첫 타설 {start} · {cycle}일 사이클 · {mix} fck {fck:g} · 만든 시각 {datetime.now():%Y-%m-%d %H:%M}\n")
    return {"poured": sorted(pour_day), "removed": removed, "standing": [fl for fl in sorted(pour_day) if fl not in removed],
            "cylinders": len(cyl), "checks": len(chk), "moved": old}


if __name__ == "__main__":
    import sys as _sys; [_s.reconfigure(errors="replace") for _s in (_sys.stdout, _sys.stderr) if hasattr(_s, "reconfigure")]   # cp949 콘솔에서 — 등으로 죽지 않게
    import argparse
    ap_ = argparse.ArgumentParser(description="시연 데이터 폴더 만들기")
    ap_.add_argument("folder")
    ap_.add_argument("--from-zones", action="store_true", help="폴더의 zones.csv(Revit 이 씀)로 층 순서 가상 기록 — KBIM 등 다른 모델")
    ap_.add_argument("--sim", action="store_true", help="시나리오 시뮬레이션 시작 상태 (기본 11-24 아침: 1–8F 기록, 9F 타설 전) — docs/simulation/scenario.md")
    ap_.add_argument("--start", default="2026-10-01")
    ap_.add_argument("--cycle", type=int, default=7)
    ap_.add_argument("--at", help="판정 시각 YYYY-MM-DDTHH:MM (기본: 마지막 층 타설 뒤)")
    a = ap_.parse_args()
    tgt = os.path.abspath(a.folder)
    if os.path.commonpath([tgt, os.path.abspath(SAMPLE)]) == os.path.abspath(SAMPLE):
        raise SystemExit("시험용 샘플 폴더에는 만들지 않음 — 다른 폴더를 주세요")
    if a.sim:
        r = sim(tgt, datetime.fromisoformat(a.at) if a.at else datetime(2026, 11, 24, 8), date.fromisoformat(a.start), a.cycle)
        print(f"시뮬레이션 시작 폴더 {tgt}: 타설 {r['poured'][0]}–{r['poured'][-1]}F · 해체 기록 {', '.join(f'{k}F {v:%m-%d}' for k, v in r['removed'].items()) or '없음'}"
              f" · 동바리 서 있음 {', '.join(f'{k}F' for k in r['standing'])} · 공시체 {r['cylinders']}줄 · 동바리 확인 {r['checks']}줄"
              + (f" (기존 {len(r['moved'])}개는 _보관_ 폴더로)" if r["moved"] else ""))
        raise SystemExit(0)
    if a.from_zones:
        r = from_zones(tgt, date.fromisoformat(a.start), a.cycle, datetime.fromisoformat(a.at) if a.at else None)
        print(f"시연 폴더 {tgt}: 층 {r['floors']} 중 {r['poured']}개 타설 · 타설 {r['pours']}줄 · 공시체 {r['cylinders']}줄"
              + (f" (기존 {len(r['moved'])}개는 _보관_ 폴더로)" if r["moved"] else ""))
        raise SystemExit(0)
    moved = make(tgt)
    L = evd.ledger_at(tgt, "2026-12-31")
    print(f"시연 폴더 {tgt}" + (f" (기존 {len(moved)}개는 _보관_ 폴더로)" if moved else ""))
    print("12-31 근거:", {z: f"{v['stage']}/8" for z, v in sorted(L.items()) if z.startswith("9F-")})

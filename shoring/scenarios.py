"""Revit On-Site 시나리오 검증 — 정의서 §5 '시나리오 검사'. 표준 라이브러리만.

모든 수치는 engine.py / evaluate.py / install_rules.py 를 그대로 불러 계산한다 (이 파일은 값을 따로 계산하지 않음).
실행: python shoring/scenarios.py  → docs/scenario_validation.md 작성, 실행 가능한 시나리오 중 FAIL 이 있으면 종료 코드 1.
"""
import csv
import os
import shutil
import sys
import tempfile
from datetime import date, datetime, timedelta

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import engine as e            # noqa: E402
import evaluate as ev         # noqa: E402
import install_rules as ir    # noqa: E402

SAMPLE = os.path.join(HERE, "sample", "data")
OUT_MD = os.path.join(ROOT, "docs", "scenario_validation.md")
AT = datetime(2026, 12, 31, 9, 0)        # 샘플 판정 시각 (make_sample.py 와 같음)
START, CYCLE, FCK = date(2026, 10, 1), 7, 24.0
FLOORS = list(range(1, 16))              # 데모 모델 15층 (정의서 §3.1)
REQ = "해체 승인 요청 가능"


def scenario(sid, title, inp, expected, source):
    """결과 dict 틀. ok: True PASS / False FAIL / None 여기서 실행 불가."""
    return {"id": sid, "title": title, "input": inp, "expected": expected, "source": source, "actual": "", "ok": None,
            "note": ""}


def sample_copy(tmp, drop=(), cylinders=None):
    """샘플 폴더를 tmp 에 복사. drop: 뺄 파일, cylinders: cylinders.csv 를 이 행들로 바꿈."""
    for name in os.listdir(SAMPLE):
        if name not in drop:
            shutil.copy(os.path.join(SAMPLE, name), tmp)
    if cylinders is not None:
        with open(os.path.join(tmp, "cylinders.csv"), "w", encoding="utf-8", newline="") as f:
            w = csv.DictWriter(f, fieldnames=list(cylinders[0]))
            w.writeheader()
            w.writerows(cylinders)
    return tmp


def violated(start, n_levels, floors=FLOORS):
    pours = [(f, start + timedelta(CYCLE * i), "보통") for i, f in enumerate(floors)]
    return [r["층"] for r in e.floor_plan(pours, FCK, n_levels) if r["판정"] == "위반"], e.floor_plan(pours, FCK, n_levels)


def span(xs):
    return f"{xs[0]}–{xs[-1]}층" if xs else "없음"


# ---------------- 시나리오 ----------------
def s0():
    r = scenario("S0", "정의서 §4.2 계절별 fck 도달 일수 재현",
                 "보통 시멘트 fck 24, 서울 평년 기온, 타설 5/1·8/1·11/1·1/5, 양생 하한 5 °C / 15 °C",
                 "5/1 16·16일, 8/1 11·11일, 11/1 24·18일, 1/5 29·18일", "정의서 §4.2 표 (KDS 14 20 10 강도식 + 온도 보정 재령)")
    want = {date(2026, 5, 1): (16, 16), date(2026, 8, 1): (11, 11), date(2026, 11, 1): (24, 18), date(2027, 1, 5): (29, 18)}
    got = {d: tuple(next(a for a in range(1, 400) if e.predict("보통", d, a, FCK, cm) >= FCK) for cm in (5.0, 15.0))
           for d in want}
    r["actual"] = ", ".join(f"{d.month}/{d.day} {a}·{b}일" for d, (a, b) in got.items())
    r["ok"] = got == want
    r["note"] = "11월·1월 타설은 보온(5 °C)만으로 fck 도달이 24–29일 → 7일 사이클 3개층(21일)로는 모자람. 가열 양생(15 °C)이면 18일."
    return r


def s1():
    r = scenario("S1", "데모 기준 — 샘플 판정",
                 "sample/data (9–12층 24구역, 9층 센서 6개·공시체 12/30), 첫 타설 10/1·7일 사이클·보통·fck 24·존치 3개층, "
                 "판정 2026-12-31 09:00",
                 "9F A1·B1·C1·C2 해체 승인 요청 가능, A2·B2 존치 (북측 그늘 구역 최소 강도 미달), 10F↑ 층수 게이트 NG",
                 "가이드라인 2.1.2 다층 해체 ≥ fck, KCS 14 20 12 3.3.2(2) 3개층 존치, CLAUDE.md 판정 우선순위")
    rows = ev.evaluate(SAMPLE, AT)
    nine = {x["zone_id"]: x for x in rows if x["floor"] == 9}
    ok_z = sorted(z for z, x in nine.items() if x["decision"] == REQ)
    hold = sorted(z for z, x in nine.items() if x["decision"].startswith("존치"))
    ng = [x for x in rows if x["floor"] >= 10]
    r["actual"] = (f"요청 가능 {'·'.join(z[3:] for z in ok_z)} / 존치 {'·'.join(z[3:] for z in hold)} "
                   f"({'; '.join(nine[z]['reason'] for z in hold)}) / 10–12F {sum(x['level_gate'] == 'NG' for x in ng)}"
                   f"/{len(ng)}구역 게이트 NG")
    r["ok"] = (ok_z == ["9F-A1", "9F-B1", "9F-C1", "9F-C2"] and hold == ["9F-A2", "9F-B2"]
               and all(x["level_gate"] == "NG" and x["decision"] == "존치" for x in ng))
    r["note"] = (f"9층 공시체 평균은 6구역 모두 기준 이상({min(float(x['f_cyl']) for x in nine.values()):.1f} MPa 이상)이지만, "
                 f"센서 IDW 구역 최소 강도가 북측 그늘(최소 위치 {nine['9F-A2']['f_est_xy']}) A2·B2 에서 {FCK:g} MPa 미만 → 공시체 한 장으로 층 전체를 해체하면 "
                 "놓치는 구역을 위치 근거로 잡아냄.")
    return r


def s2():
    r = scenario("S2", "화정형 — 1개층만 존치",
                 "15층, 첫 타설 10/1·7일 사이클·보통, 존치 1개층 (engine.floor_plan n_levels=1)",
                 "모든 층 위반 (존치 층수 < 3), 2층↑ 하중비 > 1", "KCS 14 20 12 3.3.2(2) 타설층 포함 3개층, 가이드라인 2.1.2(2) 하중비교")
    bad, rows = violated(START, 1)
    ratios = [x["하중비"] for x in rows[1:]]
    r["actual"] = f"위반 {len(bad)}/{len(rows)}층, 2–15층 하중비 {min(ratios):.2f}–{max(ratios):.2f}"
    r["ok"] = len(bad) == len(rows) and min(ratios) > 1
    r["note"] = (f"지지층이 1개면 타설 하중 1.2(5.44 + 0.5) + 1.6·2.5 = {e.pour_load(n_support=1):.2f} kN/m² 를 여유 "
                 f"{e.slab_spare():.2f} kN/m²·√(f/fck) 한 장이 받아야 해서 "
                 "하중비가 2배를 넘는다. 1층은 기초가 받쳐 하중비 0 이지만 층수 규칙만으로도 위반.")
    return r


def s3():
    r = scenario("S3", "겨울 첫 타설 — 가이드라인 최소 3개층으로 부족",
                 "15층·7일 사이클·보통·fck 24·존치 3개층, 첫 타설 12/1 (비교: 5/1 여름, 10/1 데모)",
                 "12/1: 필요 존치 층수 > 3, 3개층 존치 시 위반 층 발생 / "
                 "5/1: 3개층 위반 없음 / 10/1: 3개층 5–12층·4개층 9–11층·5개층 없음",
                 "정의서 §4.3·§5 ①②, KCS 14 20 12 표 3.3-1 (다층 ≥ fck), engine.floor_plan")
    res = {}
    for d in (date(2026, 5, 1), START, date(2026, 12, 1)):
        bad3, rows3 = violated(d, 3)
        res[d] = {"need": max(x["필요 존치 층수"] for x in rows3), "bad3": bad3, "bad5": violated(d, 5)[0]}
    w, su, base = res[date(2026, 12, 1)], res[date(2026, 5, 1)], res[START]
    bad4 = violated(START, 4)[0]
    r["actual"] = (f"12/1: 필요 {w['need']}개층, 3개층 위반 {span(w['bad3'])}, 5개층 위반 {span(w['bad5'])} / 5/1: 필요 {su['need']}개층, "
                   f"3개층 위반 {span(su['bad3'])} / 10/1: 3개층 {span(base['bad3'])}·4개층 {span(bad4)}·5개층 {span(base['bad5'])}")
    r["ok"] = (w["need"] > e.MIN_LEVELS and w["bad3"] and not su["bad3"] and base["bad3"] == list(range(5, 13)) and bad4 == [9, 10, 11]
               and not base["bad5"])
    r["note"] = (f"겨울(12/1)은 위반 층이 {span(w['bad3'])}으로 10/1 시작({span(base['bad3'])})보다 넓다 "
                 "→ '가이드라인 최소 3개층만 지키면 겨울에 위반'(정의서 §1)을 엔진이 재현. (v6: 공정 계획의 동시 세트 수 비교는 공정 계산과 함께 뺌)")
    return r


def s4():
    r = scenario("S4", "공시체 미달",
                 "S1 샘플에서 9F-A1 공시체만 21.6/22.0/22.4 MPa (평균 22.0, 12/30 시험) [입력 가정]",
                 "evaluate: 9F-A1 '존치 (공시체 미달)', 승인 요청 불가",
                 "CLAUDE.md 판정 우선순위 (공시체 = 기준 근거), KCS 14 20 12 3.3.1(3)(4) 현장양생 공시체")
    cyl = ev.read(SAMPLE, "cylinders.csv")
    for c in cyl:
        if c["zone_id"] == "9F-A1":
            c.update(f1="21.6", f2="22.0", f3="22.4")
    with tempfile.TemporaryDirectory() as tmp:
        a1 = next(x for x in ev.evaluate(sample_copy(tmp, cylinders=cyl), AT) if x["zone_id"] == "9F-A1")
    r["actual"] = f"evaluate '{a1['decision']}' ({a1['reason']})"
    r["ok"] = a1["decision"] == "존치 (공시체 미달)"
    r["note"] = f"센서 추정(구역 최소 {a1['f_est_min']} MPa)이 기준을 넘어도 공시체가 미달이면 승인 요청을 막는다."
    return r


def s6():
    r = scenario("S6", "공시체 없음",
                 "S1 샘플에서 cylinders.csv 제거 (센서·타설 기록은 그대로)",
                 "9F 6구역 모두 '공시체 시험 필요', 승인 요청 가능 0", "정의서 §3.4-6, KCS 14 20 12 3.3.1(3)(4), engine.zone_decision")
    with tempfile.TemporaryDirectory() as tmp:
        rows = ev.evaluate(sample_copy(tmp, drop=("cylinders.csv",)), AT)
    nine = [x["decision"] for x in rows if x["floor"] == 9]
    r["actual"] = f"9F {nine.count('공시체 시험 필요')}/{len(nine)}구역 '공시체 시험 필요', 요청 가능 {nine.count(REQ)}"
    r["ok"] = nine.count("공시체 시험 필요") == 6 and REQ not in nine
    r["note"] = "센서만으로는 승인 요청이 나가지 않는다 (국내 기준상 강도 확인 근거는 현장양생 공시체). Revit 색은 노랑(상태 4)."
    return r


def s7():
    r = scenario("S7", "최상층 토핑아웃 — 층수 게이트",
                 "S1 샘플, evaluate(top_floor=12) (12층이 지붕 슬래브라고 가정) / 비교: top_floor 없음",
                 "10–12F 게이트 'OK(최상부)' → 판정이 공시체·센서 단계로 넘어감 (공시체 없으니 '공시체 시험 필요'); top_floor 없으면 NG",
                 "evaluate.py top_floor 해석 [가정]: 3개층 존치는 상부 타설 하중 분담 목적 (KCS 14 20 12 3.3.2(2))")
    top = [x for x in ev.evaluate(SAMPLE, AT, top_floor=12) if x["floor"] >= 10]
    plain = [x for x in ev.evaluate(SAMPLE, AT) if x["floor"] >= 10]
    gates = sorted({x["level_gate"] for x in top})
    decs = sorted({x["decision"] for x in top})
    r["actual"] = (f"top_floor=12: 게이트 {gates}, 판정 {decs} / 없음: 게이트 {sorted({x['level_gate'] for x in plain})}")
    r["ok"] = gates == ["OK(최상부)"] and decs == ["공시체 시험 필요"] and all(x["level_gate"] == "NG" for x in plain)
    r["note"] = ("최상부 층은 위로 3개층이 영원히 생기지 않으므로 게이트를 통과시키되, 강도 근거(공시체)는 그대로 요구한다. "
                 "이 해석은 규정 문구가 아니라 팀 해석 [가정] — 발표에서 그렇게 밝힐 것.")
    return r


def s8():
    r = scenario("S8", "KBIM 실측 동바리 높이 — 설치 검측",
                 "KBIM 10층 IFC 동바리 높이 (design/kbim_analysis.md §8.2): 기준층 3,950 / 2층 4,650 / 1층 5,900 / 옥탑 4,850 mm, "
                 "대조 데모 2,790 mm. 파이프서포트·수평연결재 없음·이음 0 (모델 기본값)",
                 "2,790: 없음 / 3,950: 수평연결재 / 4,650·4,850: 수평연결재 + 시스템 동바리 / 5,900: + 기술사 구조계산",
                 "안전보건규칙 제332조의2 (3.5 m 초과 수평연결재), 고시 2025-78 제9조 (4 m 초과 시스템 동바리), "
                 "건진법 시행령 제101조의2 (5 m 이상 구조계산) — 정의서 §3.2 설치 검측")
    want = {2790: [], 3950: ["수평연결재"], 4650: ["수평연결재", "동바리 형식"], 4850: ["수평연결재", "동바리 형식"],
            5900: ["수평연결재", "동바리 형식", "구조계산"]}
    got = {h: ir.flags(ir.check_shore(h, splices=0, load_kn=11.4)) for h in want}   # 이음·하중 기록 있음 (S9 1.2 m)
    r["actual"] = " / ".join(f"{h:,}: {'·'.join(f) or '없음'}" for h, f in got.items())
    r["ok"] = got == want
    brace = {h: next(x["reason"] for x in ir.check_shore(h, splices=0, load_kn=11.4) if x["item"] == "수평연결재") for h in (3950, 5900)}
    r["note"] = ("데모 모델(층고 3.0 m)에서는 아무 플래그도 안 나오지만, 실제 IFC(업무시설 층고 4.1 m, 1층 아래 6.1 m)에서는 기준층부터 "
                 f"수평연결재, 1층 하부는 3가지가 모두 걸린다. 예: {brace[5900]}.")
    return r


def s9():
    w = ir.design_load(210)
    r = scenario("S9", "1본당 하중",
                 f"슬래브 210 mm → 설계 연직하중 w = 24·0.21 + 0.4 + 2.5 = {w:.2f} kN/m², 간격 1.2 × 1.2 m / 1.5 × 1.5 m, 파이프서포트",
                 "1.2 m: 11.4 kN ≤ 13.3 kN OK / 1.5 m: 17.9 kN > 13.3 kN NG",
                 "KDS 21 50 00 §1.6 (하중), 표 2.6-1 압축성능 40 kN ÷ 표 1.8-1 안전율 3 = 13.3 kN, 정의서 §3.2·§4.1")
    res = {}
    for a in (1.2, 1.5):
        p = ir.post_load(a, a, w)
        res[a] = (p, ir.flags(ir.check_shore(2790, splices=0, load_kn=p)))
    r["actual"] = " / ".join(f"{a} m: {p:.1f} kN → {'NG' if f else 'OK'}" for a, (p, f) in res.items()) + \
                  f" (허용 {ir.PIPE_ALLOWABLE_KN:.1f} kN)"
    r["ok"] = (round(res[1.2][0], 1) == 11.4 and not res[1.2][1] and round(res[1.5][0], 1) == 17.9
               and res[1.5][1] == ["1본 하중"])
    r["note"] = (f"1.2 m 간격은 여유 {1 - res[1.2][0] / ir.PIPE_ALLOWABLE_KN:.0%}. 1.5 m 로 넓히면 허용하중을 "
                 f"{res[1.5][0] / ir.PIPE_ALLOWABLE_KN - 1:.0%} 넘는다. 40 kN 은 극한값이라 허용하중으로 쓰면 안 되며, "
                 "제품 성적서 값이 더 작으면 그 값을 넣는다 [입력].")
    return r


def s10():
    r = scenario("S10", "RAVA3Pro 프리캐스트 중공 슬래브 — 부정 시험",
                 "revit/test_models/rava3pro/ (핀란드 소형 공동주택, 바닥 = 프리캐스트 중공 슬래브 ONTELOLAATTA, IFC 에서 IfcBeam)",
                 "현장타설 구조 슬래브 0 → '동바리 대상 없음' (구역·동바리 생성 0, 오류 없이 안내)",
                 "정의서 §3.7-1 시험 모델 역할 ③, 범용 생성 규칙 (구조 슬래브 = FLOOR/ROOF + 콘크리트 ≥ 120 mm)")
    r["actual"] = "미실행 — Revit 시나리오, 애드인 '구역·동바리 생성'에서 확인 예정"
    r["note"] = ("판정 엔진이 아니라 Revit 요소 인식(ZoneGenerator) 단계의 시험이라 이 스크립트로는 돌릴 수 없다. "
                 "합격 기준: 생성 0, 한국어 안내('구조 바닥이 있는 레벨을 찾지 못했습니다…').")
    return r


def s11():
    r = scenario("S11", "센서 구역 최소가 날짜 사이클을 덮어씀",
                 "샘플 실제 기록(9–12층 타설, 9층 공시체 12/30) — evaluate 가 처음 '요청 가능'을 낸 날 (12/30–1/10 매일 09:00 판정)",
                 "9F-A1 은 공시체 시험일 12/30, 북측 그늘 9F-A2·B2 는 그보다 늦음 (같은 층·같은 타설일이어도 구역 최소 강도가 늦게 닿음)",
                 "CLAUDE.md 판정 우선순위 (센서 구역 최소 = 위치 근거)")
    first = {}
    for k in range(12):
        at = datetime(2026, 12, 30, 9) + timedelta(k)
        for x in ev.evaluate(SAMPLE, at):
            if x["floor"] == 9 and x["decision"] == REQ:
                first.setdefault(x["zone_id"], at.date())
    late = date(2100, 1, 1)
    a1, a2, b2 = (first.get(z, late) for z in ("9F-A1", "9F-A2", "9F-B2"))
    r["actual"] = "처음 '요청 가능': " + " · ".join(f"{z[3:]} {d:%m-%d}" for z, d in sorted(first.items())) + (" (A2·B2 1/10 까지 없음)" if late in (a2, b2) else "")
    r["ok"] = a1 == date(2026, 12, 30) and a2 > a1 and b2 > a1
    r["note"] = ("날짜 사이클만 보면 9층은 12/30 에 끝나지만, 북측 그늘 구역은 센서 구역 최소 강도가 기준에 닿는 날까지 존치다 "
                 "(v6: 실제 타설·공시체·센서 기록만으로 판정 — 공정 계획 반영은 공정 계산과 함께 뺌).")
    return r


SCENARIOS = [s0, s1, s2, s3, s4, s6, s7, s8, s9, s10, s11]   # S5 공정표 파일 — v6 에서 뺌


def cell(t):
    return str(t).replace("|", "\\|").replace("\n", "<br>")


def verdict(ok):
    return "PASS" if ok else "미실행" if ok is None else "**FAIL**"


def write_md(results):
    n_pass = sum(r["ok"] is True for r in results)
    n_fail = sum(r["ok"] is False for r in results)
    n_skip = sum(r["ok"] is None for r in results)
    lines = ["# Revit On-Site 시나리오 검증표", "",
             f"- 생성: `python shoring\\scenarios.py` ({date.today().isoformat()}). 이 문서는 스크립트가 덮어쓴다 — 손으로 고치지 말 것.",
             "- 계산: `engine.py`·`evaluate.py`(판정 기준본) + `install_rules.py`(설치 검측). 기준값 출처는 정의서 §3.2, "
             "`shoring/regulation_check.md`.",
             "- 데이터: 샘플(`shoring/sample/data`)은 가상값이다 (센서 온도 = 평년 기온 + 수화열 모형, 공시체 = 엔진 예측 ± 0.4 MPa). "
             "KBIM 높이는 IFC 실측(`design/kbim_analysis.md`).",
             f"- 결과: **PASS {n_pass} · FAIL {n_fail} · 미실행 {n_skip}** (미실행 = Revit 안에서만 확인 가능)", "",
             "| ID | 시나리오 | 입력 | 기대 결과 (근거) | 실제 결과 | 판정 |", "|---|---|---|---|---|---|"]
    for r in results:
        lines.append(f"| {r['id']} | {cell(r['title'])} | {cell(r['input'])} | {cell(r['expected'])}<br>*근거: "
                     f"{cell(r['source'])}* | {cell(r['actual'])} | {verdict(r['ok'])} |")
    lines += ["", "## 시나리오별 설명", ""]
    for r in results:
        lines += [f"### {r['id']} {r['title']} — {verdict(r['ok'])}", "", r["note"], ""]
    lines += ["## 다시 돌리기", "", "```", r".venv\Scripts\python.exe shoring\scenarios.py", "```", "",
              "실행 가능한 시나리오 중 하나라도 FAIL 이면 종료 코드 1. 엔진·공정 코드가 바뀌면 이 스크립트와 "
              "`python shoring\\engine.py`, `python shoring\\install_rules.py` 를 함께 돌린다.", ""]
    os.makedirs(os.path.dirname(OUT_MD), exist_ok=True)
    with open(OUT_MD, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))


def main():
    sys.stdout.reconfigure(errors="replace")        # cp949 콘솔에서 ≤·— 같은 글자 때문에 멈추지 않게
    results = []
    for fn in SCENARIOS:
        try:
            results.append(fn())
        except Exception as ex:                      # 한 시나리오가 터져도 표는 만든다 (FAIL 로 기록)
            r = scenario(fn.__name__.upper(), fn.__name__, "", "", "")
            r.update(actual=f"예외: {type(ex).__name__}: {ex}", ok=False)
            results.append(r)
    write_md(results)
    for r in results:
        print(f"{r['id']:>4} {verdict(r['ok']).strip('*'):<4} {r['title']} | {r['actual']}")
    print("→", OUT_MD)
    sys.exit(1 if any(r["ok"] is False for r in results) else 0)


if __name__ == "__main__":
    main()

"""규칙 게이트·근거 거짓 안전 회귀 (내부 감사 A-01–A-05, docs/internal_audit.md): python test_gate.py
A-06 (지지층 14 MPa) 은 tools/test_field_server.py test_strength_only_latest_before_at 에서 본다."""
import csv
import os
import shutil
import tempfile
from datetime import date, datetime, timedelta

import engine as e
import evaluate as ev

HERE = os.path.dirname(os.path.abspath(__file__))
SAMPLE = os.path.join(HERE, "sample", "data")
AT = datetime(2026, 12, 31, 9)


def folder(zones=None, pours=None, cylinders=None, copy=()):
    t = tempfile.mkdtemp()
    for n in copy:
        shutil.copy(os.path.join(SAMPLE, n), t)
    for name, head, rows in (("zones.csv", "zone_id,floor,x0,y0,x1,y1", zones),
                             ("pours.csv", "zone_id,pour_date,pour_time,mix,fck", pours),
                             ("cylinders.csv", "zone_id,test_date,curing,f1,f2,f3", cylinders)):
        if rows is not None:
            with open(os.path.join(t, name), "w", encoding="utf-8", newline="") as f:
                f.write(head + "\n" + "\n".join(",".join(map(str, r)) for r in rows) + "\n")
    return t


def run(t, at, **kw):
    try:
        return {r["zone_id"]: r for r in ev.evaluate(t, at, **kw)}
    finally:
        shutil.rmtree(t)


box = (0, 0, 10, 10)
# A-01: 4F 는 계획 행(6/30) → 1F 위로 2개층뿐 → 존치, 4F 는 판정에서 빠짐
r = run(folder([(f"{k}F-A", k, *box) for k in (1, 2, 3, 4)],
               [("1F-A", "2026-05-01", "08:00", "보통", 24), ("2F-A", "2026-05-08", "08:00", "보통", 24),
                ("3F-A", "2026-05-15", "08:00", "보통", 24), ("4F-A", "2026-06-30", "08:00", "보통", 24)],
               [("1F-A", "2026-05-31", "현장봉함", 25, 25, 25)]), datetime(2026, 6, 1, 9))
assert r["1F-A"]["decision"] == "존치" and r["1F-A"]["level_gate"] == "NG", r["1F-A"]
assert "4F-A" not in r

# A-03: 지하 −1 → 지상 1 (0 없음) 도 층 순서로 셈 → B1 위로 2개층뿐 → 존치
r = run(folder([("B1-A", -1, *box), ("1F-A", 1, *box), ("2F-A", 2, *box)],
               [("B1-A", "2026-05-01", "08:00", "보통", 24), ("1F-A", "2026-05-08", "08:00", "보통", 24),
                ("2F-A", "2026-05-15", "08:00", "보통", 24)],
               [("B1-A", "2026-06-01", "현장봉함", 26, 26, 26)]), datetime(2026, 6, 2, 9))
assert r["B1-A"]["decision"] == "존치" and "위로 2개층" in r["B1-A"]["reason"], r["B1-A"]

# A-02: 12F 는 A1 만 타설 → A1 위치만 3개층, B1·C2 위치는 2개층 → 존치
t = folder(copy=("zones.csv", "sensors.csv", "cylinders.csv"))
with open(os.path.join(SAMPLE, "pours.csv"), encoding="utf-8-sig") as f:
    rows = [x for x in csv.DictReader(f) if not x["zone_id"].startswith("12F-") or x["zone_id"] == "12F-A1"]
with open(os.path.join(t, "pours.csv"), "w", encoding="utf-8", newline="") as f:
    w = csv.DictWriter(f, fieldnames=list(rows[0]))
    w.writeheader()
    w.writerows(rows)
r = run(t, AT)
for z in ("9F-B1", "9F-C2"):
    assert r[z]["decision"] == "존치" and r[z]["level_gate"] == "NG", r[z]
assert r["9F-A1"]["level_gate"] != "NG", r["9F-A1"]

# A-04: 첫날 35 ℃ 기록뿐인 센서 → 끊긴 뒤는 기상 온도 (계속 35 ℃ 로 치면 22일 만에 fck 훨씬 넘음)
start, at = datetime(2026, 12, 1, 8), datetime(2026, 12, 23, 9)
rows = [{"timestamp": (start + timedelta(hours=h)).isoformat(), "temp_c": "35"} for h in range(24)]
s, ea = e.MIXES["보통"]
te = ev.sensor_te(rows, start, at, ea, air=lambda d: 0.0)
hot = (23 + ev.MAX_GAP_H) / 24                         # 마지막 기록 23시 + 끊김 허용 6 h
held = e.age_factor(35, ea) * hot + e.age_factor(0.0, ea) * ((at - start).total_seconds() / 86400 - hot)
assert abs(te - held) < 1e-6, (te, held)
assert e.strength_at(te, 24, s) < 24 and ev.stale_days(rows, at) > 20

# A-05: 표준양생 공시체는 기준 근거 아님 → 공시체 시험 필요 (무시 건수 표시)
t = folder(copy=("zones.csv", "pours.csv", "sensors.csv"),
           cylinders=[("9F-A1", "2026-12-30", "표준양생", 30, 30, 30), ("9F-B1", "2026-12-30", "현장봉함", 25, 25, 25)])
r = run(t, AT)
assert r["9F-A1"]["decision"] == "공시체 시험 필요" and "현장양생 아님 1건" in r["9F-A1"]["reason"], r["9F-A1"]
assert r["9F-B1"]["f_cyl"] == 25.0
# A-23: 타설 시각 비면 23:59 (00:00 이면 재령이 최대 하루 길어짐)
r = run(folder([("1F-A", 1, *box)], [("1F-A", "2026-05-01", "", "보통", 24)]), datetime(2026, 5, 2, 9))
assert r["1F-A"]["age_days"] == round((datetime(2026, 5, 2, 9) - datetime(2026, 5, 1, 23, 59)).total_seconds() / 86400, 1)

# A-24: 같은 구역 타설 두 줄 → 입력 오류, 같은 날 공시체 두 건 → 낮은 쪽
try:
    run(folder([("1F-A", 1, *box)], [("1F-A", "2026-05-01", "08:00", "보통", 24), ("1F-A", "2026-05-20", "08:00", "보통", 24)]),
        datetime(2026, 6, 1, 9))
    raise AssertionError("중복 타설 행을 받음")
except SystemExit as x:
    assert "두 번" in str(x)
t = folder(copy=("zones.csv", "pours.csv", "sensors.csv"),
           cylinders=[("9F-A1", "2026-12-30", "현장봉함", 22, 22, 22), ("9F-A1", "2026-12-30", "현장봉함", 26, 26, 26)])
assert run(t, AT)["9F-A1"]["f_cyl"] == 22.0
# 막 친 맨 위 슬래브(24 h 안)는 지지층이 아니라 하중: 1F 위 2·3·4F, 4F 가 판정 1 h 전 타설 → 2·3F 가 받침
zs = [(f"{k}F-A", k, *box) for k in (1, 2, 3, 4)]
ps = [("1F-A", "2026-05-01", "08:00", "보통", 24), ("2F-A", "2026-05-08", "08:00", "보통", 24),
      ("3F-A", "2026-05-15", "08:00", "보통", 24), ("4F-A", "2026-06-01", "08:00", "보통", 24)]
r = run(folder(zs, ps, [("1F-A", "2026-05-31", "현장봉함", 25, 25, 25)]), datetime(2026, 6, 1, 9))
assert "지지층 최소 0." not in r["1F-A"]["reason"], r["1F-A"]
r2 = run(folder(zs, ps, [("1F-A", "2026-05-31", "현장봉함", 25, 25, 25)]), datetime(2026, 6, 2, 9))   # 25 h 뒤 → 4F 도 지지층
assert r2["1F-A"]["level_gate"] == "NG(지지층)", r2["1F-A"]
# D1: zones.csv slab_mm (모델 슬래브 두께) → 하중비교 두께. 열 없으면 문구 그대로, 빈칸이면 210 [가정], 값이 있으면 그 최댓값 (모델)
zs_mm = lambda v: [z + (v,) for z in zs]                                        # noqa: E731
t = folder(None, ps, [("1F-A", "2026-05-31", "현장봉함", 25, 25, 25)])
with open(os.path.join(t, "zones.csv"), "w", encoding="utf-8", newline="") as f:
    f.write("zone_id,floor,x0,y0,x1,y1,slab_mm\n" + "".join(",".join(map(str, z)) + "\n" for z in zs_mm(300)))
r3 = run(t, datetime(2026, 6, 1, 9))
assert r3["1F-A"]["reason"] == "하중비 1.39 > 1.0 — 하중 슬래브 300 mm (모델) (KCS 14 20 12 지지 슬래브)", r3["1F-A"]["reason"]
r4 = run(folder(zs, ps, [("1F-A", "2026-05-31", "현장봉함", 25, 25, 25)]), datetime(2026, 6, 1, 9))
assert r4["1F-A"]["reason"] == "하중비 1.13 > 1.0 (KCS 14 20 12 지지 슬래브)", r4["1F-A"]["reason"]           # 열 없음 → 예전 그대로
t = folder(None, ps, [("1F-A", "2026-05-31", "현장봉함", 25, 25, 25)])
with open(os.path.join(t, "zones.csv"), "w", encoding="utf-8", newline="") as f:
    f.write("zone_id,floor,x0,y0,x1,y1,slab_mm\n" + "".join(",".join(map(str, z)) + "\n" for z in zs_mm("")))
assert run(t, datetime(2026, 6, 1, 9))["1F-A"]["reason"].endswith("— 하중 슬래브 210 mm [가정] (KCS 14 20 12 지지 슬래브)")
# 공시체–센서 교차 검사: 샘플은 어긋남 없음, 9F-A1 공시체를 40 MPa 로 바꾸면 경고 (판정은 공시체·센서 규칙 그대로)
assert ev.cross_checks(SAMPLE, AT) == []
t = folder(copy=("zones.csv", "pours.csv", "sensors.csv"), cylinders=[("9F-A1", "2026-12-30", "현장봉함", 40, 40, 40)])
w = ev.cross_checks(t, AT)
shutil.rmtree(t)
assert [x["zone_id"] for x in w] == ["9F-A1"] and w[0]["diff"] > 0.25, w
t = folder(copy=("zones.csv", "pours.csv"), cylinders=[("9F-A1", "2026-12-30", "현장봉함", 40, 40, 40)])   # 센서 없음 → 비교 안 함
assert ev.cross_checks(t, AT) == []
shutil.rmtree(t)
# 판넬 조기 탈형 (필러 존치): 14 MPa, 공시체 있으면 낮은 쪽
assert ev.early_form(15.0, None, 24) == "14 MPa 도달 — 판넬 조기 탈형 4조건 검토 대상 (15.0 ≥ 14 MPa, 추정만 — 공시체 없음)"
assert ev.early_form(15.0, 13.0, 24) == "14 MPa 미달 — 판넬 존치 (13.0 < 14 MPa, 공시체·추정 중 낮은 값)"
r = {x["zone_id"]: x for x in ev.evaluate(SAMPLE, AT)}
assert r["9F-A1"]["early_form"].startswith("14 MPa 도달 — 판넬 조기 탈형 4조건 검토 대상") and r["12F-A1"]["early_form"].startswith("14 MPa ")
assert not any("판넬 탈형 요청 가능" in x["early_form"] for x in r.values())   # 요청 가능 문구는 staged 4조건 결과만 (33차)

# A-26: 센서 없는 구역 근거 = 실제 쓴 기온 출처 + 양생 하한 태그
assert r["10F-A1"]["est_source"] == "기상 예측(센서 없음) — 평년 기온 [가정], 양생 하한 5 °C [가정] · 참고 (판정에 안 씀)", r["10F-A1"]["est_source"]
t = folder(copy=("zones.csv", "pours.csv"))
with open(os.path.join(t, "weather.csv"), "w", encoding="utf-8", newline="") as f:
    f.write("date,avgTa\n" + "".join(f"{date(2026, 12, 3) + timedelta(i)},1.0\n" for i in range(8)))
r = run(t, AT, cure_min=3)
assert r["10F-A1"]["est_source"] == "기상 예측(센서 없음) — 기상 기록 8일 [입력]·평년 기온 20일 [가정], 양생 하한 3 °C [가정] · 참고 (판정에 안 씀)", r["10F-A1"]
# A-28: 400일 안에 기준 강도에 못 닿으면 StopIteration 이 아니라 고칠 곳을 알려 주는 입력 오류
t = folder([("1F-A", 1, *box)], [("1F-A", "2026-05-01", "08:00", "보통", 24)])
with open(os.path.join(t, "weather.csv"), "w", encoding="utf-8", newline="") as f:
    f.write("date,avgTa\n" + "".join(f"{date(2026, 5, 1) + timedelta(i)},-60\n" for i in range(420)))
try:
    run(t, datetime(2026, 6, 1, 9), cure_min=-60)
    raise AssertionError("400일 안에 못 닿는데 판정함")
except SystemExit as x:
    assert "400일 안에 기준 24 MPa 에 도달 안 함" in str(x), x
# A-29: fck 가 프로젝트 기본값과 다름 / 공시체 3개 범위 > 평균 15 % → 경고만 (판정 함수는 그대로)
assert ev.input_checks(SAMPLE, AT, 24.0) == []
t = folder([("1F-A", 1, *box), ("2F-A", 2, *box)],
           [("1F-A", "2026-05-01", "08:00", "보통", 18), ("2F-A", "2026-05-08", "08:00", "보통", 24)],
           [("2F-A", "2026-05-20", "현장봉함", 30, 30, 30), ("2F-A", "2026-06-01", "현장봉함", 20, 24, 28),
            ("1F-A", "2026-06-01", "표준양생", 10, 24, 40)])
w = ev.input_checks(t, datetime(2026, 6, 1, 9), 24.0)
shutil.rmtree(t)
assert [(x["zone_id"], x["kind"]) for x in w] == [("1F-A", "fck 확인"), ("2F-A", "공시체 편차")], w
assert w[0]["text"] == "1F-A: pours.csv fck 18 MPa ≠ 프로젝트 기본 fck 24 MPa — 설계 강도를 확인하세요 (해체 기준은 pours.csv 값)", w[0]
assert w[1]["text"] == "2F-A: 공시체 2026-06-01 3개 범위 8.0 MPa > 평균 24.0 의 15% [가정] — 시편·시험 기록을 확인하세요 (판정은 그대로)", w[1]
print("gate self-check OK")

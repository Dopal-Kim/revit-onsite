"""S 단계 해체·조기 탈형 구조 검토 — docs/shore_view_spec.md S1–S3 (S4 전용 계획은 v6 공정표 제거로 뺌). 표준 라이브러리만 (C# 이식 기준본).

S1 wall_strip   벽·기둥 측면 거푸집 해체일: 5 MPa 도달일(엔진 예측) · 무시험 일수(타설 뒤 평균기온 → 표 3.3-2)
S2 stages       구역마다 ① 판넬 탈형(필러 존치) 4조건 ② 필러 = 기존 판정 결론 그대로 + 판넬 탈형 가능일(예측)
S3 review_html  조기 탈형 구조 검토서(초안) A4 한 장 — 서명란 빈칸, 승인·지시 아님
근거 값은 shoring/regulation_check.md §7 [확정] 만. ACI 347.2 는 본문 미확인 → 개념(재받침·전용 일정)만, 숫자는 우리 계산.
결론 문구는 '요청 가능 / 존치 / 필요' 까지 — 승인·해체 지시 문구 없음.
실행: python staged.py <데이터 폴더> --at 2026-12-31T09:00 [--out 폴더]   /   python staged.py --self-test
"""
from report_style import REPORT_CSS
import html
import os
import statistics
import sys
from datetime import date, datetime, timedelta

import engine as e
import evaluate as ev
import provenance as pv

SIDE_MPA = e.thresholds(24.0)["측면"]                        # 5 [확정] KCS 14 20 12:2024 표 3.3-1 (fck 무관)
PANEL_MPA = e.thresholds(24.0)[ev.EARLY_FORM_MPA_KEY]        # 14 [확정] 가이드라인 2.1.1(2)① p10 (fck 무관)
WARM_C, WARM_DAYS = 10.0, 4                                  # [확정] KCS 14 20 12:2024 3.3.1(5) 10 ℃ 이상 4일 이상 양생
HOT_C = 20.0                                                 # [확정] 표 3.3-2 평균기온 20 ℃ 이상 칸
# [확정] 표 3.3-2 측면 무시험 일수 (20 ℃ 이상, 10–20 ℃): 조강 2/3 · 보통·1종 혼합 4/6 · 2종 혼합 5/8
# 배합 → 시멘트 칸 대응은 [가정]: 슬래그 미분말·3성분 = 2종 혼합 칸 (보수 측)
NO_TEST_DAYS = {"조강": (2, 3), "보통": (4, 6), "슬래그 미분말": (5, 8), "슬래그·FA 3성분": (5, 8)}
NO_REACH = 400                                               # days_to 한도 (감사 A-28) — 판넬 탈형 가능일을 찾는 한도
LIMIT_DAYS = NO_REACH
REVIEW_KEY = "구조검토"  # 근거 원장(evidence.csv) doc_no 또는 선택 열 doc_type 에 이 글자가 있으면 구조검토서 기록
PANEL_OK, PANEL_KEEP = "판넬 탈형 요청 가능 (필러 존치)", "판넬 존치"
DRAFT = "초안 — 책임기술자 검토·승인 전, 해체 지시 아님"
FORBIDDEN = ("하십시오", "하세요", "할 것", "하라", "승인함", "승인합니다", "지시합니다", "지시함")   # 자체 검사: 지시 문구

SRC_SIDE = "KCS 14 20 12:2024 표 3.3-1"
SRC_DAYS = "KCS 14 20 12:2024 표 3.3-2"
SRC_PANEL = "해체 가이드라인 2.1.1(2)① p10 — 필러 동바리 전체 존치 + 구조검토 + 14 MPa 이상"
SRC_WARM = "KCS 14 20 12:2024 3.3.1(5) — 10 ℃ 이상 4일 이상 양생 + 책임기술자 사전 승인"
SRC_LOAD = "해체 가이드라인 2.1.2(2) 하중비교 예제식 (지지층 강도 비율 √(f/fck) 감소 적용 — 보수 측)"


# ---------- 공통 ----------
class _Site:
    """데이터 폴더 한 번 읽기: 구역·타설·기온 (evaluate 와 같은 파일·같은 기온 모델)."""
    def __init__(self, folder):
        self.folder = folder
        zones = ev.read(folder, "zones.csv")
        self.floor_of = {z["zone_id"]: int(z["floor"]) for z in zones}
        self.rect_of = {z["zone_id"]: tuple(float(z[k]) for k in ("x0", "y0", "x1", "y1")) for z in zones}
        self.slab_of = {z["zone_id"]: float(z["slab_mm"]) for z in zones if (z.get("slab_mm") or "").strip()}
        self.order = sorted(set(self.floor_of.values()))
        self.pours = {p["zone_id"]: p for p in ev.read(folder, "pours.csv")}
        self.first = min((self.floor_of[z] for z in self.pours if z in self.floor_of), default=None)   # 타설 기록이 있는 가장 아래층
        self.weather = ev.read(folder, "weather.csv")
        self.sens = ev.sensors_of(folder)                  # W4 구역 온도 센서
        self._t, self._zt = {}, {}

    def hard(self, zid):
        """타설 기록은 없지만 첫 기록 층보다 아래 = 이미 굳은 층으로 봄 [가정] (34차: Revit 이 zones.csv 에 모델 1–8F 를 더함)."""
        return zid not in self.pours and self.first is not None and self.floor_of.get(zid, self.first) < self.first

    def temps(self, cast):
        """타설일부터 400일 일평균 기온 (weather.csv, 없으면 평년) — evaluate.daily_temps 그대로."""
        if cast not in self._t:
            self._t[cast] = ev.daily_temps(self.weather, cast, LIMIT_DAYS)
        return self._t[cast]

    def zone_temps(self, zid):
        """W4 (v6.2): 타설일부터 LIMIT_DAYS 일 → (일평균 온도, 날마다 출처 '센서'·'기상'·'평년', 날마다 쓴 센서 ID). 구역 3 m 안(evaluate.near) 온도 센서
        temp_c 일평균(센서 여럿이면 낮은 값)을 먼저, 센서 없는 날만 weather.csv → 평년. 먼 센서(3 m 밖)는 안 씀 (판정과 같은 위치 규칙)."""
        if zid not in self._zt:
            cast = self.cast(zid)
            pts, tail = ev.near([(sid, float(rs[0]["x"]), float(rs[0]["y"]), rs) for sid, rs in self.sens.floor(self.floor_of[zid]).items()], self.rect_of[zid])
            daily = {}
            for sid, _x, _y, rs in ([] if tail else pts):
                for r in rs:
                    d = ev.ts(r["timestamp"]).date()
                    if d >= cast:
                        daily.setdefault(d, {}).setdefault(sid, []).append(float(r["temp_c"]))
            wmap = {r["date"]: float(r["avgTa"]) for r in self.weather}
            temps, src, used = [], [], []
            for i in range(LIMIT_DAYS):
                d = cast + timedelta(i)
                if d in daily:
                    temps.append(min(statistics.mean(v) for v in daily[d].values()))
                    src.append("센서")
                    used.append(sorted(daily[d]))
                else:
                    temps.append(wmap[d.isoformat()] if d.isoformat() in wmap else e.air_temp(d))
                    src.append("기상" if d.isoformat() in wmap else "평년")
                    used.append([])
            self._zt[zid] = (temps, src, used)
        return self._zt[zid]

    def temp_label(self, zid, n):
        """처음 n 일(최소 1)에 쓴 온도 출처 — '센서 S1·S2 일평균' / weather.csv 출처 이름(예: 기상 ASOS) / '평년 [가정]', 여럿이면 ' + '"""
        _, src, used = self.zone_temps(zid)
        n = max(n, 1)
        sids = sorted({s for u in used[:n] for s in u})
        return " + ".join(x for x in (f"센서 {'·'.join(sids)} 일평균" if "센서" in src[:n] else None,
                                       ev.weather_label(self.weather) if "기상" in src[:n] else None,
                                       "평년 [가정]" if "평년" in src[:n] else None) if x)

    def cast(self, zid):
        return date.fromisoformat(self.pours[zid]["pour_date"])

    def start(self, zid):
        p = self.pours[zid]
        return datetime.fromisoformat(f'{p["pour_date"]}T{p.get("pour_time") or "23:59"}')   # evaluate 와 같음 (비면 23:59)

    def mix_fck(self, zid):
        p = self.pours.get(zid) or next(iter(self.pours.values()))
        return p["mix"], float(p["fck"])

    def below(self, zid):
        """zid 아래층들(층 순서, 가까운 층부터 MIN_LEVELS − 1 개)의 평면 겹침 구역 목록. 데이터에 없는 층은 None."""
        fl = self.floor_of[zid]
        lower = self.order[:self.order.index(fl)][::-1][:e.MIN_LEVELS - 1]
        out = [[z for z, f in self.floor_of.items() if f == g and ev.overlaps(self.rect_of[z], self.rect_of[zid])] or None for g in lower]
        return out + [None] * (e.MIN_LEVELS - 1 - len(out))


def warm_days(temps, n, cure_min=5.0, src=None):
    """첫 n 일 중 양생 온도 ≥ 10 ℃ 인 날 수 (누적) — [가정] 3.3.1(5) '10 ℃ 이상 4일' 해석. 양생 온도 = 센서 날(src '센서')은 실측 그대로,
    기상·평년 날은 max(일평균 기온, 양생 하한) (W4: 센서 우선 — _Site.zone_temps)."""
    return sum((T if src and src[i] == "센서" else max(T, cure_min)) >= WARM_C for i, T in enumerate(temps[:max(n, 0)]))


def no_test_days(temps, mix):
    """표 3.3-2 무시험 일수: 첫 (20 ℃ 칸 일수) 평균 ≥ 20 → 그 일수, 아니면 첫 (10–20 ℃ 칸 일수) 평균 ≥ 10 → 그 일수, 아니면 None.
    평균 기간 = 그 칸 일수 동안의 일평균 기온 [가정 — 표의 '평균기온' 기간 해석]."""
    hot, warm = NO_TEST_DAYS[mix]
    if statistics.mean(temps[:hot]) >= HOT_C:
        return hot
    return warm if statistics.mean(temps[:warm]) >= WARM_C else None


def panel_load(site, zid, strength_of, fck):
    """③ 위층 타설 하중비교: 이 구역 슬래브 + 아래 MIN_LEVELS − 1 개층(겹침 구역 최소)이 받침 — engine.load_check 그대로.
    strength_of(z) = 그 구역 강도, 자료가 없으면 None → 첫 타설 기록 층보다 아래면 굳은 층 fck [가정], 아니면(아직 타설 전) 0.
    데이터(zones.csv)에 없는 아래층도 fck [가정]. 하중 슬래브 두께 = zones.csv slab_mm (없으면 엔진 기본 210 mm).
    반환 (하중비, 지지 강도 목록, 하중 kN/m², 여유 kN/m², fck 가정 썼나)."""
    def s(z):
        v = strength_of(z)
        return v if v is not None else fck if site.hard(z) else 0.0
    below = site.below(zid)
    sup = [s(zid)] + [fck if ov is None else min(s(z) for z in ov) for ov in below]
    assumed = any(ov is None or any(site.hard(z) for z in ov) for ov in below)
    kw = {"t_slab": site.slab_of[zid] / 1000} if zid in site.slab_of else {}
    kw["live"] = ev.live_load(site.folder)                  # W12 시공하중 (options.json, 기본·최소 2.5)
    ratio, _ = e.load_check(sup, fck, *ev.support_loads(site.folder)[:2], **kw)   # 11-11 G1
    load = e.pour_load(n_support=len(sup), **kw)
    return ratio, sup, load, (load / ratio if ratio else None), assumed


def earliest_panel(site, zid, cure_min=5.0, cast_of=None, mix_fck=None):
    """①②③ 이 처음 모두 맞는 날 (타설일부터 앞으로, 엔진 예측 강도, 400일 한도 → None). ④ 구조검토서는 날짜와 무관이라 빼고 셈.
    cast_of: 구역 → 타설일 (기본 pours.csv)."""
    cast_of = cast_of or (lambda z: site.cast(z) if z in site.pours else None)
    mix_fck = mix_fck or site.mix_fck
    c0 = cast_of(zid)
    mix, fck = mix_fck(zid)
    t0 = site.temps(c0)
    zt, zs = site.zone_temps(zid)[:2] if zid in site.pours and site.cast(zid) == c0 else (t0, None)   # W4: 실제 타설이면 ② 는 구역 온도 (센서 우선)
    for a in range(1, LIMIT_DAYS):
        d = c0 + timedelta(a)
        if e.predict(mix, c0, a, fck, cure_min, t0) < PANEL_MPA or warm_days(zt, a, cure_min, zs) < WARM_DAYS:
            continue

        def f_at(z):
            c = cast_of(z)
            if c is None:
                return None                                  # 타설 기록 없음 → panel_load 가 정함 (첫 기록 층 아래면 fck [가정])
            if c > d:
                return 0.0                                   # 아직 안 친 아래층은 못 받침
            m, k = mix_fck(z)
            return e.predict(m, c, (d - c).days, k, cure_min, site.temps(c))
        if panel_load(site, zid, f_at, fck)[0] <= 1.0:
            return d
    return None


def days_to(mix, cast, fck, target, cure_min, temps):
    """타설일부터 target MPa 에 닿는 날 수 (정수 일, 엔진 예측). NO_REACH 일 안에 못 닿으면 None (감사 A-28). v6: 예전 schedule.py 에서 옮김."""
    return next((a for a in range(1, NO_REACH) if e.predict(mix, cast, a, fck, cure_min, temps) >= target), None)


# ---------- S1 ----------
def wall_strip(folder, zone_id, at=None, cure_min=5.0):
    """벽·기둥 측면 거푸집 해체일 두 가지 — 판정 근거 있는 쪽을 고르는 건 사람: 강도 기준일은 공시체 시험 확인 시에만 근거."""
    site = _Site(folder)
    if zone_id not in site.pours:
        raise SystemExit(f"{zone_id}: pours.csv 에 타설 기록 없음")
    cast, (mix, fck) = site.cast(zone_id), site.mix_fck(zone_id)
    t = site.temps(cast)
    n = days_to(mix, cast, fck, SIDE_MPA, cure_min, t)
    by_strength = cast + timedelta(n) if n else None
    zt = site.zone_temps(zone_id)[0]                         # W4: 무시험 일수 평균기온 = 구역 온도 (센서 우선 → 기상 → 평년)
    k = no_test_days(zt, mix)
    by_days = cast + timedelta(k) if k else None
    hot, warm = NO_TEST_DAYS[mix]
    days_txt = (f"무시험 {k}일 — 첫 {k}일 평균기온 {statistics.mean(zt[:k]):.1f} ℃ ({site.temp_label(zone_id, k)}), {mix} 칸 [확정] {SRC_DAYS}" if k
                else f"무시험 일수 표 범위 밖 — 시험 필요 (첫 {warm}일 평균기온 {statistics.mean(zt[:warm]):.1f} ℃ < {WARM_C:g} ℃, {site.temp_label(zone_id, warm)})")
    str_txt = (f"측면 {SIDE_MPA:g} MPa 도달 {n}일 (엔진 예측, 양생 하한 {cure_min:g} ℃) [확정 기준] {SRC_SIDE}" if n
               else f"{LIMIT_DAYS}일 안에 측면 {SIDE_MPA:g} MPa 도달 안 함")
    note = ""
    if by_strength and (by_days is None or by_strength < by_days):
        note = "강도 기준일이 이름 — 강도 기준일은 공시체 시험 확인 시"
    out = {"zone_id": zone_id, "pour_date": cast, "date_by_strength": by_strength, "date_by_days": by_days,
           "basis": f"{str_txt} / {days_txt}" + (f" / {note}" if note else "")}
    if at is not None:
        out["passed"] = {"강도": bool(by_strength and by_strength <= at.date()), "일수": bool(by_days and by_days <= at.date())}
    return out


# ---------- S2 ----------
def review_record(site, zid, at):
    """④ 근거 원장의 구조검토서 기록 (판정 시각까지, 단계 번호 무관) — 첫 기록 또는 None."""
    for r in ev.read(site.folder, "evidence.csv"):
        if r["zone_id"] == zid and r["date"] <= at.date().isoformat() and (
                REVIEW_KEY in (r.get("doc_no") or "") or REVIEW_KEY in (r.get("doc_type") or "")):
            return r
    return None


def stages(folder, at, cure_min=5.0):
    """{zone_id: {floor, panel: {decision, checks: [(이름, 통과?, 근거)], values}, filler: {decision, reason}, earliest_panel_date}}
    — 판정 시각까지 타설된 구역 (evaluate 와 같은 구역·같은 강도)."""
    site = _Site(folder)
    rows = ev.evaluate(folder, at, cure_min)
    by = {r["zone_id"]: r for r in rows}

    def f_now(z):
        if z in by:
            return by[z]["f_est_min"] if by[z]["f_cyl"] == "" else min(by[z]["f_est_min"], by[z]["f_cyl"])
        return None                                          # zones.csv 에 있지만 타설 기록 없음 → panel_load 가 정함 (34차)
    out = {}
    for r in rows:
        zid, fck = r["zone_id"], float(r["fck"])
        f = f_now(zid)
        n = int((at - site.start(zid)).total_seconds() / 86400)   # evaluate 와 같은 정수 일
        zt, zs, _ = site.zone_temps(zid)
        w = warm_days(zt, n, cure_min, zs)                   # W4 센서 우선
        ratio, sup, load, cap, assumed = panel_load(site, zid, f_now, fck)
        rec = review_record(site, zid, at)
        basis1 = "공시체·추정 중 낮은 값" if r["f_cyl"] != "" else "추정만 — 공시체 없음"
        checks = [
            ("① 강도 ≥ 14 MPa", f >= PANEL_MPA, f"{f:.1f} {'≥' if f >= PANEL_MPA else '<'} {PANEL_MPA:g} MPa ({basis1}) — {SRC_PANEL}"),
            ("② 10 ℃ 이상 4일", w >= WARM_DAYS, f"양생 {n}일 중 10 ℃ 이상 {w}일 {'≥' if w >= WARM_DAYS else '<'} {WARM_DAYS}일 (온도: {site.temp_label(zid, n)}) — {SRC_WARM}"),
            ("③ 위층 하중비 ≤ 1.0", ratio <= 1.0, f"하중비 {ratio:.2f} (지지 {', '.join(f'{x:.1f}' for x in sup)} MPa"
             + (" · 타설 기록 없는 아래층 = fck [가정]" if assumed else "") + f") — {SRC_LOAD}"),
            ("④ 구조검토서 기록", rec is not None, f"{rec['doc_no']} ({rec['date']})" if rec else "구조검토서·책임기술자 승인 기록 필요"),
        ]
        out[zid] = {"floor": r["floor"], "pour_date": r["pour_date"],
                    "panel": {"decision": PANEL_OK if all(c[1] for c in checks) else PANEL_KEEP, "checks": checks,
                              "values": {"f": f, "f_est": r["f_est_min"], "f_cyl": r["f_cyl"], "est_source": r["est_source"], "age_days": n,
                                         "warm_days": w, "ratio": ratio, "supports": sup, "load": load, "cap": cap, "fck": fck, "live": ev.live_load(folder)}},
                    "filler": {"decision": r["decision"], "reason": r["reason"]},
                    "earliest_panel_date": earliest_panel(site, zid, cure_min)}
    return out


# ---------- S3 ----------
def review_html(folder, at, zone_id, shores=None, project="", cure_min=5.0):
    """조기 탈형 구조 검토서(초안) A4 한 장. shores = 그 구역 모델 동바리 본수 (None 이면 '모델 값' 칸)."""
    st = stages(folder, at, cure_min)
    if zone_id not in st:
        raise SystemExit(f"{zone_id}: 판정 시각까지 타설된 구역이 아님")
    s, x = st[zone_id], st[zone_id]["panel"]["values"]
    q = html.escape
    rows = "".join(f"<tr><td>{q(n)}</td><td>{'통과' if ok else '미충족' if not n.startswith('④') else '필요'}</td><td>{q(b)}</td></tr>"
                   for n, ok, b in s["panel"]["checks"])
    cap = "—" if x["cap"] is None else f"{x['cap']:.2f}"
    ep = s["earliest_panel_date"]
    con, sup = ev.text_option(folder, "contractor"), ev.text_option(folder, "supervisor_firm")   # 11-12 서류 머리 자동 입력 ([프로젝트 설정])
    return f"""<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>조기 탈형 구조 검토서 (초안) {q(zone_id)} {at:%Y-%m-%d}</title>
<style>@page{{size:A4;margin:12mm}}body{{font:10pt 'Malgun Gothic',sans-serif;color:#111;background:#fff}}h1{{font-size:16pt;margin:0 0 4px}}h2{{font-size:12pt;margin:8px 0 3px}}
table{{border-collapse:collapse;width:100%;margin:6px 0}}th,td{{border:1px solid #888;padding:4px 6px;text-align:left;vertical-align:top}}th{{background:#eee}}
.sign{{break-inside:avoid}}.sign td{{height:40px}}.note{{color:#444;font-size:9.5pt}}.draft{{color:#a00;font-weight:600}}{pv.CSS}.annex{{break-before:page}}{REPORT_CSS}</style></head><body>
<h1>조기 탈형(판넬 탈형·필러 존치) 구조 검토서 (초안)</h1>
<div class="draft">{q(DRAFT)}</div>
<div>{q(project)} · 구역 {q(zone_id)} ({s['floor']}층) · 타설 {ev.kdate(s['pour_date'])} · 검토 기준 시각 {ev.kstamp(at.isoformat())} · 작성 Revit On-Site{' · 시공사 ' + q(con) if con else ''}{' · 감리단 ' + q(sup) if sup else ''}</div>
<h2>1. 강도 근거 (그날)</h2>
<table><tr><th>구역 최소 추정</th><td>{x['f_est']} MPa — {q(x['est_source'])}</td></tr>
<tr><th>현장양생 공시체 평균</th><td>{q(str(x['f_cyl'])) + ' MPa' + (' (' + q(ev.lot_label(s['filler'])) + ')' if ev.lot_label(s['filler']) else '') if x['f_cyl'] != '' else '없음 — 공시체 시험 필요'}</td></tr>
<tr><th>검토에 쓴 값</th><td>{x['f']:.1f} MPa (둘 중 낮은 값)</td></tr></table>
<h2>2. 판넬 탈형 조건</h2>
<table><tr><th>조건</th><th>결과</th><th>계산 값·출처</th></tr>{rows}</table>
<p class="note">③ 식: 1.2(24·t + 0.4 + 0.5·n) + 1.6·{x['live']:g} = {x['load']:.2f} kN/m² ≤ Σ(1.2·1.64 + 1.6·2.0)·√(f/fck) = {cap} kN/m²,
n = {len(x['supports'])}개층 (이 구역 + 아래층, 데이터에 없는 아래층 = fck {x['fck']:g} MPa [가정]), fck {x['fck']:g} MPa. 시공하중 {x['live']:g} kN/m² [가정] — 자재 적재·장비 집중하중 미반영.
판넬 탈형 가능일(엔진 예측, ①②③): {ev.kdate(ep.isoformat()) if ep else f'{LIMIT_DAYS}일 안에 없음'}</p>
<h2>3. 필러 동바리</h2>
<table><tr><th>배치</th><td>모델 동바리 전체 존치 (판넬만 탈형) — {SRC_PANEL}</td></tr>
<tr><th>본수</th><td>{f'{shores:,}본 (모델)' if shores is not None else '모델 값: ____ 본'}</td></tr>
<tr><th>필러 해체 판정 (기존)</th><td>{q(s['filler']['decision'])} — {q(s['filler']['reason'])}</td></tr></table>
<h2>4. 결론 (프로그램)</h2>
<p><b>{q(s['panel']['decision'])}</b> — 조건 ①–④ {'모두 충족' if s['panel']['decision'] == PANEL_OK else '중 미충족·필요 항목 있음'}. 최종 판단은 책임기술자 검토·서명.</p>
<table class="sign"><tr><th>구분</th><th>소속</th><th>성명</th><th>서명</th><th>일시</th></tr>
<tr><td>작성 (시공사)</td><td>{q(con)}</td><td></td><td></td><td></td></tr>
<tr><td>검토 (책임기술자)</td><td></td><td></td><td></td><td></td></tr></table>
<p class="note">ACI PRC-347.2 (Shoring/Reshoring) 은 본문 미확인 — 개념만 참고, 수치는 위 국내 기준. {q(DRAFT)}.</p>
<div class="annex">{pv.table_html(pv.facts(folder, zone_id, at))}</div>
</body></html>"""


# ---------- 자체 검사 ----------
def _self_test():
    import shutil
    import tempfile
    import evidence as evd
    tmp = tempfile.mkdtemp(prefix="staged_")

    def folder(name, temp, cyl_low=False):
        p = os.path.join(tmp, name)
        os.makedirs(p)
        d0 = date(2026, 5, 1)
        w = lambda f, s: open(os.path.join(p, f), "w", encoding="utf-8", newline="").write(s)   # noqa: E731
        w("zones.csv", "zone_id,floor,x0,y0,x1,y1,slab_mm\n1F-A,1,0,0,40,10,210\n2F-A,2,0,0,40,10,210\n"
                       "3F-A,3,0,0,10,10,210\n3F-B,3,10,0,20,10,210\n3F-C,3,20,0,30,10,600\n3F-D,3,30,0,40,10,210\n")
        w("pours.csv", "zone_id,pour_date,pour_time,mix,fck\n1F-A,2026-04-01,08:00,보통,24\n2F-A,2026-04-15,08:00,보통,24\n"
          + "".join(f"3F-{c},{d0},08:00,보통,24\n" for c in "ABCD"))
        w("weather.csv", "date,avgTa\n" + "".join(f"{date(2026, 3, 1) + timedelta(i)},{temp}\n" for i in range(900)))
        if cyl_low:
            w("cylinders.csv", "zone_id,test_date,curing,f1,f2,f3\n3F-B,2026-05-05,현장봉함,10,10,10\n")
        for z in ("3F-A", "3F-B", "3F-C"):
            evd.add(p, z, "2026-05-03", 2, "시험", doc_no=f"{REVIEW_KEY}-{z}")
        evd.add(p, "3F-D", "2026-05-03", 2, "시험", doc_no="WP-3F")          # 작업계획서만 — 구조검토서 아님
        return p
    try:
        at = datetime(2026, 5, 6, 9, 0)
        warm = folder("warm", 20.0, cyl_low=True)
        st = stages(warm, at)
        ok = lambda z: [c[1] for c in st[z]["panel"]["checks"]]   # noqa: E731
        assert ok("3F-A") == [True] * 4 and st["3F-A"]["panel"]["decision"] == PANEL_OK, st["3F-A"]["panel"]["checks"]
        assert ok("3F-B") == [False, True, True, True] and st["3F-B"]["panel"]["decision"] == PANEL_KEEP        # ① 공시체 10 MPa
        assert ok("3F-C") == [True, True, False, True] and st["3F-C"]["panel"]["decision"] == PANEL_KEEP        # ③ 600 mm 슬래브
        assert ok("3F-D") == [True, True, True, False] and "필요" in st["3F-D"]["panel"]["checks"][3][2]        # ④ 구조검토서 없음
        assert st["3F-A"]["earliest_panel_date"] == date(2026, 5, 5)          # 20 ℃: 4일차 15.7 MPa·10 ℃ 이상 4일 (3일차 13.6)
        assert st["3F-C"]["earliest_panel_date"] is None                      # 하중비는 강도가 커도 600 mm 에선 안 맞음
        cold = folder("cold", 8.0)
        sc_ = stages(cold, datetime(2026, 6, 1, 9, 0))
        assert [c[1] for c in sc_["3F-A"]["panel"]["checks"]] == [True, False, True, True]                     # ② 8 ℃ < 10 ℃
        # S1: 10 ℃ 미만 → 무시험 None·'시험 필요', 20 ℃ → 보통 4일
        w8, w20 = wall_strip(cold, "3F-A"), wall_strip(warm, "3F-A", at)
        assert w8["date_by_days"] is None and "무시험 일수 표 범위 밖 — 시험 필요" in w8["basis"] and w8["date_by_strength"]
        assert w20["date_by_days"] == date(2026, 5, 5) and w20["passed"]["일수"]
        assert no_test_days([20.0] * 8, "보통") == 4 and no_test_days([15.0] * 8, "보통") == 6 and no_test_days([9.9] * 8, "보통") is None
        # W4 센서 우선: 센서 날은 실측 그대로(양생 하한 안 씀), 기상·평년 날은 하한 적용 / 샘플 9F-B2 = 구역 3 m 안 센서 S1·S2 일평균, 9F-A1 = 센서 없음 → 평년
        assert warm_days([8.0, 8.0], 2, 15.0, ["센서", "평년"]) == 1 and warm_days([8.0, 8.0], 2, 15.0) == 2
        sw = _Site(os.path.join(os.path.dirname(os.path.abspath(__file__)), "sample", "data"))
        assert sw.temp_label("9F-B2", 30) == "센서 S1·S2 일평균" and sw.zone_temps("9F-B2")[1][0] == "센서", sw.temp_label("9F-B2", 30)
        assert sw.temp_label("9F-A1", 30) == "평년 [가정]" and set(sw.zone_temps("9F-B2")[1][60:]) == {"평년"}
        assert no_test_days([25.0] * 8, "슬래그 미분말") == 5 and no_test_days([12.0] * 8, "조강") == 3
        # 33차: 모델 층 1–15F + 타설 기록 9–12F (기록 없는 아래층 = 굳은 층)
        demo = os.path.join(tmp, "demo15")
        sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tools"))
        import make_demo_data as md
        md.make(demo)
        zl = open(os.path.join(demo, "zones.csv"), encoding="utf-8").read().splitlines()
        nine = [x for x in zl if x.startswith("9F-")]
        open(os.path.join(demo, "zones.csv"), "w", encoding="utf-8").write("\n".join(
            zl + [f"{k}F-{x.split(',', 1)[0].split('-', 1)[1]},{k},{x.split(',', 2)[2]}" for k in [*range(1, 9), 13, 14, 15] for x in nine]) + "\n")
        t31 = datetime(2026, 12, 31, 9)
        # 34차: 타설 기록 없는 1–8F 는 9F 아래 = 굳은 층 fck [가정] → 가열 15 ℃ 에서 9F 판넬 탈형 요청 가능 · ⓑ 세트 감소 (0 MPa 로 넣어 ③ 늘 ✕ 였음)
        h15 = stages(demo, t31, 15.0)
        c3 = h15["9F-A1"]["panel"]["checks"][2]
        assert c3[1] and "타설 기록 없는 아래층 = fck [가정]" in c3[2] and h15["9F-A1"]["panel"]["values"]["supports"][1:] == [24.0, 24.0], c3
        assert h15["9F-A1"]["panel"]["decision"] == PANEL_OK, [c[:2] for c in h15["9F-A1"]["panel"]["checks"]]
        assert stages(demo, t31, 15.0)["12F-A1"]["panel"]["values"]["supports"][0] > 0                      # 위층(13F↑ 타설 전)은 지지로 안 셈
        # HTML: 지시 문구 없음, 초안 문구·빈 서명란
        h1 = review_html(warm, at, "3F-D")
        assert not [x for x in FORBIDDEN if x in h1], [x for x in FORBIDDEN if x in h1]
        assert DRAFT in h1 and "모델 값: ____ 본" in h1 and "<td>검토 (책임기술자)</td><td></td><td></td><td></td><td></td>" in h1
        assert "408본 (모델)" in review_html(warm, at, "3F-A", shores=408)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    assert days_to("보통", date(2026, 1, 1), 24, 24, -60, [-60.0] * 400) is None   # A-28: 400일 안에 못 닿으면 None (예전 test_schedule)
    print("staged self-test OK")


if __name__ == "__main__":
    [s.reconfigure(errors="replace") for s in (sys.stdout, sys.stderr) if hasattr(s, "reconfigure")]   # cp949 콘솔
    if "--self-test" in sys.argv:
        _self_test()
        raise SystemExit(0)
    import argparse
    ap_ = argparse.ArgumentParser(description="단계 해체·조기 탈형")
    ap_.add_argument("folder")
    ap_.add_argument("--at", required=True)
    ap_.add_argument("--cure-min", type=float, default=5.0)
    a = ap_.parse_args()
    t = datetime.fromisoformat(a.at)
    for z, s in stages(a.folder, t, a.cure_min).items():
        print(z, s["panel"]["decision"], [c[1] for c in s["panel"]["checks"]], "판넬 가능일", s["earliest_panel_date"],
              "벽", wall_strip(a.folder, z, t, a.cure_min)["date_by_strength"], "| 필러", s["filler"]["decision"])

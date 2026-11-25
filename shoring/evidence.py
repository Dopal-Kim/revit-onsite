"""4D 근거 원장 — 해체 절차 단계 서류를 구역·날짜에 묶어 "날짜 D 에 어느 구역이 어느 근거까지 갖췄나"를 낸다.
(클라우드 기획 세션 방향, 사용자 결정 11-04. 표준 라이브러리만 — C# 이식 기준본.)

모델은 서명 원본이 아니라 위치·시점 색인이다. 원본은 서명된 서류이고 원장에는 파일 경로와 SHA-256 만 남긴다.
기존 구조는 그대로 두고 합친다:
  evidence.csv   (추가만 하는 원장) ev_id, zone_id, date, stage, doc_no, result, by, x, y, file, sha256
                 — 사람이 넣는 서류: 2 작업계획서 · 4 해체전점검 · 8 결과보고 (다른 단계도 넣을 수 있음)
  cylinders.csv  → 3 공시체 (현장양생만, test_date)
  approvals.csv  → 5 검측요청(요청 제출) · 6 감리확인(감리 승인 기록) · 7 해체(해체 완료)  — approval.py, 해시 사슬
  install_inspection.csv (있으면, 정의서 §3.6) → 1 설치검측 — 항목마다 마지막 결과(shore_inspect.open_ng, 날짜 단위)에 풀리지 않은 NG 가 없으면, 날짜 = 마지막 기록일 (61차 ①)
  8 결과보고 = 원장 줄만 — result_report.py 가 구역별 해체 결과보고서를 만들 때 한 줄 (11-12 G10: 해체 후 점검 OK 만으로 자동 8 이던 것 뺌)
결과: 구역마다 단계별 첫 날짜, 갖춘 단계(1 부터 빈칸 없이 이어진 마지막 단계), 순서 누락 경고(예: 공시체 없이 검측요청).
실행: python evidence.py (자체 검사)
"""
import csv
import hashlib
import os
from datetime import date, datetime

import after_check as ac
import approval as ap
import engine as e
import evaluate as ev
import shore_inspect as si

STAGES = {1: "설치검측", 2: "작업계획서", 3: "공시체", 4: "해체전점검", 5: "검측요청", 6: "감리확인", 7: "해체", 8: "결과보고"}
LEDGER = "evidence.csv"
FIELDS = ["ev_id", "zone_id", "date", "stage", "doc_no", "result", "by", "x", "y", "file", "sha256"]
FROM_APPROVAL = {"요청 제출": 5, "감리 승인 기록": 6, "해체 완료": 7}
REPORT_SOURCE = "협력업체 신고"   # shore_checks.csv source 열 — 협력업체 반장이 층 화면 신고 QR 로 남긴 줄 (F14, field_server.py /api/report)
NEEDS = {5: (2, 3, 4), 6: (5,), 7: (6,), 8: (7,)}   # 이 단계 전에 있어야 할 단계 — 검측요청 전 작업계획서·공시체·해체 전 점검 (가이드라인 표 3.2.1 2·4단계, 11-11 G5)
PLAN_OK = "감리 계획서 확인 (서버)"   # 1-2: 감리가 서버에서 작업계획서를 확인하면 원장 2 단계에 이 결과로 한 줄


def file_sha(path):
    """첨부 서류 SHA-256 (없으면 빈 값 — 원장엔 경로만)."""
    if not path or not os.path.exists(path):
        return ""
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for b in iter(lambda: f.read(1 << 16), b""):
            h.update(b)
    return h.hexdigest()


def add(folder, zone_id, day, stage, by, doc_no="", result="", file="", x="", y=""):
    """원장에 한 줄 추가 (고치지 않고 추가만). 단계 번호·입력자 검사."""
    if int(stage) not in STAGES:
        raise ValueError(f"단계 {stage} 없음 — {STAGES}")
    if not str(by).strip():
        raise ValueError("입력자가 필요합니다")
    p = os.path.join(folder, LEDGER)
    rows = ev.read(folder, LEDGER)
    r = {"ev_id": f"E{len(rows) + 1:04d}", "zone_id": zone_id, "date": date.fromisoformat(str(day)).isoformat(), "stage": str(int(stage)),
         "doc_no": doc_no, "result": result, "by": str(by).strip(), "x": x, "y": y, "file": file, "sha256": file_sha(file)}
    new = not os.path.exists(p)
    with open(p, "a", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=FIELDS)
        if new:
            w.writeheader()
        w.writerow(r)
    return r


def entries(folder, day=None):
    """모든 출처를 원장 형식 [{zone_id, date, stage, source, by, ...}] 으로. day 를 주면 1 설치검측·8 결과보고(해체 후 점검)는 그 날짜까지의 기록으로 셈."""
    out = [{**r, "stage": int(r["stage"]), "source": LEDGER} for r in ev.read(folder, LEDGER)]
    cut = date.max if day is None else date.fromisoformat(str(day)[:10])
    ins = {}
    for r in si.records(folder, cut):                            # 1 설치검측 (61차 ①): 항목마다 마지막 결과 — NG 뒤 OK 면 풀림 (F16 open_ng 와 같은 규칙, 다른 단계 줄은 뺌)
        if r["stage"] == si.INSTALL:
            ins.setdefault(r["zone_id"], []).append(r)
    ng = si.open_ng(folder, cut, si.INSTALL)
    for zid in sorted(ins, key=lambda z: min(r["i"] for r in ins[z])):   # 구역 = 파일에 처음 나온 순
        if zid not in ng:
            last = ins[zid][-1]
            out.append({"zone_id": zid, "date": last["t"].date().isoformat(), "stage": 1, "result": "OK", "by": last["inspector"], "source": "install_inspection.csv"})
    for r in ev.cyl_rows(folder):               # W1: 타설 묶음 공통 시험 → 묶음의 구역마다
        if r.get("curing") in e.FIELD_CURINGS:
            out.append({"zone_id": r["zone_id"], "date": r["test_date"], "stage": 3, "result": r.get("curing", ""), "by": r.get("entered_by", ""),
                        "source": "cylinders.csv"})
    if ap.verify(folder)[0]:                    # 사슬이 깨진 승인 기록은 근거로 쓰지 않음
        for r in ap.read_log(folder):
            if r["action"] in FROM_APPROVAL:
                out.append({"zone_id": r["zone_id"], "date": r["timestamp"][:10], "stage": FROM_APPROVAL[r["action"]], "by": r["actor"],
                            "result": r["evidence_sha"][:12], "source": ap.LOG})
    for r in ev.read(folder, "shore_checks.csv"):              # 협력업체 해체 신고 '뺐음' (F14) → 7 해체, 결과 = 실제 시각 (안전관리자 확인 '아니오'는 관측이라 안 셈)
        if r.get("source", "").strip() == REPORT_SOURCE and r.get("present", "").strip() == "아니오":
            out.append({"zone_id": r["zone_id"], "date": r["timestamp"][:10], "stage": 7, "result": r["timestamp"], "by": r.get("actor", "").strip(),
                        "source": "shore_checks.csv", "sha256": (r.get("photo_sha256") or "").strip()})   # 신고 사진 SHA-256 (F15, 없으면 빈칸)
    return out


def removal_watch(folder, day):
    """무단 해체 관측 경고 (총괄 P1-2 — 화정 2022 1심: 원인 = 하부 3개층 동바리 무단 해체) → {zone_id: 문구}. 판정·결론은 안 바꿈.
    구역에 '동바리 없음' 관측(휴대폰 동바리 확인 '아니오' 또는 승인 기록 '해체 완료')이 있는데 그 시각까지 그 구역 '감리 승인 기록'이
    없으면 그 첫 관측을 경고. 그 뒤 '재받침' 기록 다음에 '예' 확인이 오면 해제. day 날짜까지의 기록만, 사슬이 깨진 승인 기록은 안 씀(entries 와 같음).
    문구는 '의심'·'확인 필요'까지 — 단정·처벌 문구 금지."""
    d = date.fromisoformat(str(day))
    by = {}                                                      # 구역 → [(시각, 같은 시각 순서, 종류, 행위자)] 종류 6·7·8·"예"·"아니오"
    if ap.verify(folder)[0]:
        for r in ap.read_log(folder):
            k = ap.ACTIONS.get(r["action"])
            if k:
                by.setdefault(r["zone_id"], []).append((datetime.fromisoformat(r["timestamp"]), 0 if k == 6 else 1, k, r["actor"]))
    for r in ev.read(folder, "shore_checks.csv"):
        p = r.get("present", "").strip()
        if p in ("예", "아니오"):
            by.setdefault(r["zone_id"], []).append((ev.ts(r["timestamp"]), 1, p, r.get("actor", "").strip()))
    out = {}
    for zid, es in sorted(by.items()):
        approved, flag, reshored = False, None, False
        for t, _, k, actor in sorted(es, key=lambda x: (x[0], x[1])):   # 같은 시각이면 감리 승인 기록 먼저
            if t.date() > d:
                break
            if k == 6:
                approved = True
            elif k == 8:
                reshored = flag is not None
            elif k in (7, "아니오"):
                if not approved and flag is None:
                    flag = (t, actor)
                reshored = False
            elif flag and reshored:                                  # 재받침 기록 뒤 '예' 확인 → 해제
                flag, reshored = None, False
        if flag:
            out[zid] = f"승인 기록 없이 동바리 없음 관측 — {zid} {flag[0]:%m-%d %H:%M} {flag[1]}".rstrip()
    return out


def signature_watch(folder, day):
    """서명 전 해체 관측 (총괄 11-05): '감리 승인 기록'의 서명 일시(signed_at)보다 앞서 같은 구역에 동바리 확인 '아니오'가 있으면 경고 → {zone_id: 문구}.
    구역마다 가장 이른 서명과 그 전 가장 이른 '아니오'. 서명 일시가 날짜만이면 그날 23:59 로 봄(확인 필요 쪽). day 날짜까지의 기록만,
    사슬이 깨진 승인 기록은 안 씀. 판정·결론은 안 바꿈."""
    d = date.fromisoformat(str(day))
    if not ap.verify(folder)[0]:
        return {}
    signed = {}
    for r in ap.read_log(folder):
        s = (r.get("signed_at") or "").strip()
        if r["action"] == "감리 승인 기록" and s and r["timestamp"][:10] <= d.isoformat():
            t = datetime.fromisoformat(s if "T" in s else s + "T23:59")
            signed[r["zone_id"]] = min(signed.get(r["zone_id"], t), t)
    obs = sorted((ev.ts(r["timestamp"]), r["zone_id"], r.get("actor", "").strip()) for r in ev.read(folder, "shore_checks.csv")
                 if r.get("present", "").strip() == "아니오" and r["zone_id"] in signed)
    out = {}
    for t, z, actor in obs:
        if t.date() <= d and t < signed[z] and z not in out:
            out[z] = f"서명 전 해체 관측 — {z} 동바리 없음 {t:%m-%d %H:%M} {actor} · 감리 서명 {signed[z]:%m-%d %H:%M}".replace("  ", " ")
    return out


def install_after_pour(folder, day):
    """설치 검측 마지막 날짜가 그 구역 타설일 뒤면 경고 (총괄 11-05 25차 — 기록 날짜 실수를 실자료에서도 잡게, 판정은 안 바꿈) → {zone_id: 문구}.
    day 날짜까지의 타설·설치 검측 기록만. 순서 = install_inspection.csv 에 처음 나온 구역 순."""
    day = str(day)
    pour = {p["zone_id"]: p["pour_date"] for p in ev.read(folder, "pours.csv") if (p.get("pour_date") or "") <= day}
    last = {}
    for r in ev.install_rows(folder):                            # F16: 일상 점검·해체 전·해체 후 줄은 타설 뒤가 정상
        z, d = (r.get("zone_id") or "").strip(), (r.get("date") or "").strip()[:10]
        if z in pour and d and d <= day and d > last.get(z, ""):
            last[z] = d
    return {z: f"{z}: 설치 검측({d[5:]})이 타설({pour[z][5:]}) 뒤 — 기록 확인 필요" for z, d in last.items() if d > pour[z]}


def removal_order(folder, day, ents=None):
    """해체 순서 (35차 총괄): 구역의 해체 기록(근거 7 — 승인 기록 '해체 완료'·협력업체 신고 '뺐음')이 바로 아래층(층 순서)에서 평면이 겹치고
    타설된 구역의 해체 기록보다 먼저(그 날짜까지 아래층 해체 기록 없음)면 경고 → {zone_id: 문구} (층 순서). 해체는 아래층부터 [가정].
    day 날짜까지의 기록만. 판정·결론은 안 바꿈 — '확인 필요'까지."""
    d = str(day)
    first7 = {}
    for x in entries(folder) if ents is None else ents:
        if x["stage"] == 7 and x["date"][:10] <= d:
            first7[x["zone_id"]] = min(first7.get(x["zone_id"], x["date"][:10]), x["date"][:10])
    if not first7:
        return {}
    zones = {r["zone_id"]: r for r in ev.read(folder, "zones.csv")}
    floor_of = {z: int(r["floor"]) for z, r in zones.items()}
    rect = {z: tuple(float(r[k]) for k in ("x0", "y0", "x1", "y1")) for z, r in zones.items()}
    poured = {p["zone_id"] for p in ev.read(folder, "pours.csv") if (p.get("pour_date") or "")[:10] <= d}
    order = sorted(set(floor_of.values()))
    out = {}
    for z in sorted((z for z in first7 if z in floor_of), key=ev.floor_order(folder)):
        i, t = order.index(floor_of[z]), first7[z]
        low = sorted(y for y in zones if i > 0 and floor_of[y] == order[i - 1] and y in poured and ev.overlaps(rect[y], rect[z])
                     and not (y in first7 and first7[y] <= t))
        if low:
            out[z] = f"{z}: 위층 동바리가 아래층보다 먼저 해체 — 확인 필요 ({t[5:]} 해체, 아래층 {', '.join(low[:3])}{' 외' if len(low) > 3 else ''} 해체 기록 없음)"
    return out


def ledger_at(folder, day):
    """날짜 day 까지의 근거 → {zone_id: {"dates": {단계: 첫 날짜}, "stage": 갖춘 단계, "warnings": [...]}}."""
    d = date.fromisoformat(str(day))
    res = {}
    ents = entries(folder, d)
    for x in ents:
        if date.fromisoformat(x["date"][:10]) > d:
            continue
        z = res.setdefault(x["zone_id"], {"dates": {}, "stage": 0, "warnings": []})
        s = x["stage"]
        if s not in z["dates"] or x["date"] < z["dates"][s]:
            z["dates"][s] = x["date"][:10]
    for zid, z in res.items():
        k = 0
        while k + 1 in z["dates"]:
            k += 1
        z["stage"] = k
        for s, need in NEEDS.items():
            if s in z["dates"]:
                for n in need:
                    if n not in z["dates"] or z["dates"][n] > z["dates"][s]:
                        z["warnings"].append(f"{zid}: {STAGES[s]}({z['dates'][s]}) 전에 {STAGES[n]} 기록 없음")
    for zid, w in [*removal_watch(folder, d).items(), *signature_watch(folder, d).items(),
                   *ac.warnings(folder, d.isoformat()).items(), *install_after_pour(folder, d.isoformat()).items(),
                   *removal_order(folder, d.isoformat(), ents).items()]:   # 무단 해체 관측·서명 전 해체 관측·해체 후 점검 미완 (F3)·설치 검측이 타설 뒤·위층 먼저 해체 (경고만)
        res.setdefault(zid, {"dates": {}, "stage": 0, "warnings": []})["warnings"].append(w)
    return res


if __name__ == "__main__":
    import shutil
    import tempfile
    from datetime import datetime
    t = tempfile.mkdtemp()
    try:
        SAMPLE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "sample", "data")
        for n in ("zones.csv", "pours.csv", "cylinders.csv"):
            shutil.copy(os.path.join(SAMPLE, n), t)
        ok_row = {"zone_id": "9F-A1", "decision": "해체 승인 요청 가능"}
        add(t, "9F-A1", "2026-11-23", 1, "박안전", "IC-9F-01", "합격")
        add(t, "9F-A1", "2026-12-20", 2, "김현장", "WP-9F", "", file=os.path.abspath(__file__))
        ap.record(t, "9F-A1", "요청 제출", "김현장", ok_row, now=datetime(2026, 12, 31, 10))
        L = ledger_at(t, "2026-12-29")                               # 공시체(12-30) 전 → 1·2 단계까지
        assert L["9F-A1"]["stage"] == 2 and 3 not in L["9F-A1"]["dates"], L["9F-A1"]
        L = ledger_at(t, "2026-12-31")                               # 공시체 3, 검측요청 5, 해체전점검 4 없음 → 갖춘 단계 3
        assert L["9F-A1"]["stage"] == 3 and L["9F-A1"]["dates"][5] == "2026-12-31" and L["9F-A1"]["warnings"] == ["9F-A1: 검측요청(2026-12-31) 전에 해체전점검 기록 없음"], L["9F-A1"]   # 11-11 G5: 요청 전 해체 전 점검 필요
        ap.record(t, "9F-A2", "해체 완료", "김현장", ok_row, now=datetime(2026, 12, 31, 11))   # 감리확인 없이 해체 → 경고
        assert any("감리확인" in w for w in ledger_at(t, "2026-12-31")["9F-A2"]["warnings"])
        # 무단 해체 관측: 9F-A2 는 감리 승인 기록 없이 해체 완료 → 경고 / 9F-A1 은 감리 승인 기록 뒤 '아니오' → 경고 없음 /
        # 9F-B1 은 승인 없이 '아니오' → 경고, 재받침 뒤 '예' → 해제
        assert removal_watch(t, "2026-12-31") == {"9F-A2": "승인 기록 없이 동바리 없음 관측 — 9F-A2 12-31 11:00 김현장"}, removal_watch(t, "2026-12-31")
        assert "승인 기록 없이 동바리 없음 관측 — 9F-A2 12-31 11:00 김현장" in ledger_at(t, "2026-12-31")["9F-A2"]["warnings"]
        ap.record(t, "9F-A1", "감리 승인 기록", "이감리", ok_row, now=datetime(2026, 12, 31, 12))
        with open(os.path.join(t, "shore_checks.csv"), "w", encoding="utf-8", newline="") as f:
            f.write("zone_id,timestamp,present,actor,photo\n9F-A1,2026-12-31T13:00,아니오,박안전,\n9F-B1,2026-12-31T08:00,아니오,박안전,\n")
        w = removal_watch(t, "2026-12-31")
        assert "9F-A1" not in w and w["9F-B1"] == "승인 기록 없이 동바리 없음 관측 — 9F-B1 12-31 08:00 박안전", w
        assert removal_watch(t, "2026-12-30") == {}                    # 그 날짜 뒤 기록은 안 봄
        ap.record(t, "9F-B1", "재받침", "김현장", ok_row, now=datetime(2026, 12, 31, 15))
        assert "9F-B1" in removal_watch(t, "2026-12-31")              # 재받침 기록만으로는 해제 안 됨
        with open(os.path.join(t, "shore_checks.csv"), "a", encoding="utf-8", newline="") as f:
            f.write("9F-B1,2026-12-31T16:00,예,박안전,\n")
        assert "9F-B1" not in removal_watch(t, "2026-12-31") and "9F-A2" in removal_watch(t, "2026-12-31")
        # 서명 전 해체 관측: 9F-C1 감리 승인 기록(서명 12-31 15:00) 전에 14:00 '아니오' → 경고, 서명 뒤 '아니오'(9F-C2) → 없음
        ap.record(t, "9F-C1", "감리 승인 기록", "이감리", ok_row, now=datetime(2026, 12, 31, 16), approver="이감리", signed_at="2026-12-31 15:00")
        ap.record(t, "9F-C2", "감리 승인 기록", "이감리", ok_row, now=datetime(2026, 12, 31, 16), approver="이감리", signed_at="2026-12-31")
        with open(os.path.join(t, "shore_checks.csv"), "a", encoding="utf-8", newline="") as f:
            f.write("9F-C1,2026-12-31T14:00,아니오,박안전,\n9F-C2,2027-01-01T08:00,아니오,박안전,\n")
        sw = signature_watch(t, "2027-01-01")
        assert sw == {"9F-C1": "서명 전 해체 관측 — 9F-C1 동바리 없음 12-31 14:00 박안전 · 감리 서명 12-31 15:00"}, sw
        assert sw["9F-C1"] in ledger_at(t, "2027-01-01")["9F-C1"]["warnings"] and signature_watch(t, "2026-12-30") == {}
        r = [x for x in ev.read(t, LEDGER) if x["stage"] == "2"][0]
        assert len(r["sha256"]) == 64 and r["ev_id"] == "E0002"
        with open(os.path.join(t, "install_inspection.csv"), "w", encoding="utf-8", newline="") as f:   # 1 설치검측: 전 항목 OK 인 구역만
            f.write("zone_id,item_code,result,inspector,date,photo_path\n9F-B1,H,OK,박안전,2026-11-23,\n9F-B1,S,OK,박안전,2026-11-24,\n"
                    "9F-C1,H,OK,박안전,2026-11-23,\n9F-C1,S,NG,박안전,2026-11-23,\n")
        L = ledger_at(t, "2026-12-31")
        assert L["9F-B1"]["dates"][1] == "2026-11-24" and 1 not in L.get("9F-C1", {}).get("dates", {}), (L.get("9F-B1"), L.get("9F-C1"))
        with open(os.path.join(t, "install_inspection.csv"), "a", encoding="utf-8", newline="") as f:   # 61차 ①: 9F-C1 이음 NG 뒤 OK → 1 설치검측 (날짜 = 마지막 기록일)
            f.write("9F-C1,S,OK,박안전,2026-11-25,\n")
        assert ledger_at(t, "2026-12-31")["9F-C1"]["dates"][1] == "2026-11-25" and 1 not in ledger_at(t, "2026-11-24").get("9F-C1", {}).get("dates", {})
        assert install_after_pour(t, "2026-12-31") == {}                                          # 9F 타설 11-26 전 기록
        with open(os.path.join(t, "install_inspection.csv"), "a", encoding="utf-8", newline="") as f:   # 타설 뒤 날짜로 잘못 적은 줄
            f.write("9F-B1,V,OK,박안전,2026-12-01,\n")
        w = install_after_pour(t, "2026-12-31")
        assert w == {"9F-B1": "9F-B1: 설치 검측(12-01)이 타설(11-26) 뒤 — 기록 확인 필요"}, w
        assert w["9F-B1"] in ledger_at(t, "2026-12-31")["9F-B1"]["warnings"] and install_after_pour(t, "2026-11-30") == {}
        # 협력업체 해체 신고 (F14): 감리 승인 기록(12-31 12:00) 뒤 '뺐음' → 근거 7 해체(결과 = 실제 시각), 출처 없는 '아니오'(안전관리자 확인)는 7 아님
        with open(os.path.join(t, "shore_checks.csv"), "w", encoding="utf-8", newline="") as f:
            f.write("zone_id,timestamp,present,actor,photo,source\n9F-A1,2026-12-31T13:00,아니오,박안전,,\n9F-A1,2026-12-31T14:30,아니오,최반장,,협력업체 신고\n")
        s7 = [x for x in entries(t) if x["stage"] == 7 and x["zone_id"] == "9F-A1"]
        assert [(x["date"], x["result"], x["by"], x["source"]) for x in s7] == [("2026-12-31", "2026-12-31T14:30", "최반장", "shore_checks.csv")], s7
        assert ledger_at(t, "2026-12-31")["9F-A1"]["dates"][7] == "2026-12-31"
        # 해체 순서 (35차): 10F-A1 해체(12-31) — 아래 9F-A1 같은 날 해체 → 경고 없음 / 10F-B1 해체 — 아래 9F-B1 해체 기록 없음 → 경고
        ap.record(t, "10F-A1", "해체 완료", "김현장", ok_row, now=datetime(2026, 12, 31, 17))
        ap.record(t, "10F-B1", "해체 완료", "김현장", ok_row, now=datetime(2026, 12, 31, 17))
        ro = removal_order(t, "2026-12-31")
        assert ro == {"10F-B1": "10F-B1: 위층 동바리가 아래층보다 먼저 해체 — 확인 필요 (12-31 해체, 아래층 9F-B1 해체 기록 없음)"}, ro
        assert ro["10F-B1"] in ledger_at(t, "2026-12-31")["10F-B1"]["warnings"] and removal_order(t, "2026-12-30") == {}
        with open(os.path.join(t, ac.FILE), "w", encoding="utf-8", newline="") as f:      # 11-12 G10: 해체 후 점검 5항목 OK 만으로는 8 아님 — 원장 8 줄(결과보고서)이 있어야
            f.write("zone_id,date,item_code,result,by,photo\n" + "".join(f"9F-A1,2027-01-02,AC-{i},OK,박안전,\n" for i in range(1, 6)))
        assert 8 not in ledger_at(t, "2027-01-02")["9F-A1"]["dates"]
        add(t, "9F-A1", "2027-01-02", 8, "박안전", "RR-9F-A1", "해체 결과보고")
        L = ledger_at(t, "2027-01-02")
        assert L["9F-A1"]["dates"][8] == "2027-01-02" and 8 not in ledger_at(t, "2027-01-01")["9F-A1"]["dates"], L["9F-A1"]
        try:
            add(t, "9F-A1", "2026-12-31", 9, "누구")
            raise AssertionError("없는 단계를 받음")
        except ValueError:
            pass
    finally:
        shutil.rmtree(t)
    print("evidence self-check OK")

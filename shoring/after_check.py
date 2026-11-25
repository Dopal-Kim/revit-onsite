"""해체 후 점검 — 거푸집 및 동바리 해체 가이드라인(국토교통부 2023.1) p51 4.3 [확정, docs/참고자료_방향성.md 공식본 대조].
표준 라이브러리만 (C# 이식 기준본). 판정은 바꾸지 않고 경고만 낸다 (근거 원장 8 결과보고와 묶음).
  after_check.csv  zone_id, date, item_code(AC-1..AC-5), result(OK/NG/빈칸), by, photo
                   침하 계측 (11-12 G9, 가이드라인 3.2.12①·3.3.3⑥): item_code = SET-<지점>, result = 침하량 mm — 날짜마다 반복해 넣음
실행: python after_check.py (자체 검사)
"""
import evaluate as ev

FILE = "after_check.csv"
ITEMS = {"AC-1": "필러·지지 동바리 타설층 포함 하부 3개층 설치",
         "AC-2": "각 층 같은 위치(상하 정렬)",
         "AC-3": "연직도",
         "AC-4": "자재 변형·침하",
         "AC-5": "콘크리트 면 결함"}
REPORT_STAGE = "8"   # evidence.py STAGES 8 결과보고
SETTLE = "SET-"   # 침하 계측 줄 item_code 머리 (지점 이름이 뒤에)
DONE_STAGES = ("7", "8")   # 7 해체 완료 · 8 결과보고 — 둘 다 해체가 끝났다는 기록


def after_status(folder, day):
    """날짜 day(YYYY-MM-DD) 까지의 기록 → {구역: {date, items: {code: OK/NG/""}, ok, missing}}.
    항목마다 마지막 날짜 행(같은 날이면 뒤 행). missing = OK/NG 기록이 없는 항목, ok = 5개 전부 OK."""
    day = str(day)
    out = {}
    for r in ev.read(folder, FILE):
        code, d = (r.get("item_code") or "").strip().upper(), (r.get("date") or "").strip()
        if code not in ITEMS or not d or d > day:
            continue
        z = out.setdefault(r["zone_id"].strip(), {"date": "", "items": {}, "_d": {}})
        if d >= z["_d"].get(code, ""):
            z["_d"][code], z["items"][code] = d, (r.get("result") or "").strip().upper()
        z["date"] = max(z["date"], d)
    for z in out.values():
        del z["_d"]
        z["items"] = {c: z["items"].get(c, "") for c in ITEMS}
        z["missing"] = [c for c, v in z["items"].items() if v not in ("OK", "NG")]
        z["ok"] = all(v == "OK" for v in z["items"].values())
    return out


def settlements(folder, day):
    """day 까지의 침하 계측 → {구역: {"points": [지점 (파일에 처음 나온 순)], "days": {날짜: {지점: mm 글자}}}} — 같은 날 같은 지점은 뒤 행."""
    day = str(day)
    out = {}
    for r in ev.read(folder, FILE):
        code, d = (r.get("item_code") or "").strip().upper(), (r.get("date") or "").strip()
        if not code.startswith(SETTLE) or len(code) == len(SETTLE) or not d or d > day:
            continue
        z = out.setdefault(r["zone_id"].strip(), {"points": [], "days": {}})
        pt = code[len(SETTLE):]
        if pt not in z["points"]:
            z["points"].append(pt)
        z["days"].setdefault(d, {})[pt] = (r.get("result") or "").strip()
    return out


def same_points(s):
    """침하 측정 위치 일치 — 날마다 처음 측정한 날과 같은 지점을 쟀는지 → (일치, 다른 날 목록)."""
    days = sorted(s["days"])
    first = set(s["days"][days[0]])
    bad = [d for d in days[1:] if set(s["days"][d]) != first]
    return not bad, bad


def warnings(folder, day):
    """해체가 끝난 구역(원장 7 해체 완료·8 결과보고, 또는 승인 기록 '해체 완료')인데 해체 후 점검이 없거나 OK 가 아닌 항목 → {구역: "해체 후 점검 미완 — {zone}: {항목}"}.
    23차 E13: '해체 완료' 뒤 NG 가 경고 없이 지나감 → 7 도 셈 (가이드라인 4.3 = 해체 직후 점검). 사슬이 깨진 승인 기록은 안 씀."""
    import approval as ap        # evidence → after_check 순환 import 를 피해 여기서
    day = str(day)
    st = after_status(folder, day)
    done = {r["zone_id"].strip() for r in ev.read(folder, "evidence.csv")
            if (r.get("stage") or "").strip() in DONE_STAGES and (r.get("date") or "") <= day}
    if ap.verify(folder)[0]:
        done |= {r["zone_id"].strip() for r in ap.read_log(folder) if r["action"] == "해체 완료" and r["timestamp"][:10] <= day}
    zones = sorted(done)
    out = {}
    for z in zones:
        items = st.get(z, {"items": {c: "" for c in ITEMS}})["items"]
        bad = [f"{c} {ITEMS[c]}" + (" NG" if v == "NG" else "") for c, v in items.items() if v != "OK"]
        if bad:
            out[z] = f"해체 후 점검 미완 — {z}: {', '.join(bad)}"
    return out


if __name__ == "__main__":
    import os
    import shutil
    import tempfile
    t = tempfile.mkdtemp()
    try:
        with open(os.path.join(t, "evidence.csv"), "w", encoding="utf-8", newline="") as f:
            f.write("ev_id,zone_id,date,stage,doc_no,result,by,x,y,file,sha256\n"
                    "E0001,9F-A1,2027-01-05,8,R-1,,박안전,,,,\nE0002,9F-B1,2027-01-05,8,R-2,,박안전,,,,\n"
                    "E0003,9F-C1,2027-01-05,8,R-3,,박안전,,,,\nE0004,9F-D1,2027-01-05,7,,,박안전,,,,\n")
        rows = ["zone_id,date,item_code,result,by,photo"]
        rows += [f"9F-A1,2027-01-05,AC-{i},OK,박안전," for i in range(1, 6)]                     # 모두 OK
        rows += [f"9F-B1,2027-01-05,AC-{i},{'NG' if i == 4 else 'ok'},박안전," for i in range(1, 6)]  # AC-4 NG
        rows += ["9F-A1,2027-01-09,AC-3,NG,박안전,"]                                              # day 뒤 기록은 무시
        with open(os.path.join(t, FILE), "w", encoding="utf-8", newline="") as f:
            f.write("\n".join(rows) + "\n")
        s = after_status(t, "2027-01-06")
        assert s["9F-A1"] == {"date": "2027-01-05", "items": {c: "OK" for c in ITEMS}, "missing": [], "ok": True}, s["9F-A1"]
        assert s["9F-B1"]["ok"] is False and s["9F-B1"]["items"]["AC-4"] == "NG" and s["9F-B1"]["missing"] == []
        assert "9F-C1" not in s and after_status(t, "2027-01-09")["9F-A1"]["ok"] is False
        w = warnings(t, "2027-01-06")
        assert set(w) == {"9F-B1", "9F-C1", "9F-D1"}, w                                          # D1 = 원장 7 해체 완료 → 점검 없음 경고 (23차 E13)
        assert w["9F-B1"] == "해체 후 점검 미완 — 9F-B1: AC-4 자재 변형·침하 NG", w
        assert w["9F-C1"] == "해체 후 점검 미완 — 9F-C1: " + ", ".join(f"{c} {n}" for c, n in ITEMS.items()), w
        assert warnings(t, "2027-01-04") == {}                                                   # 결과보고 전
        with open(os.path.join(t, FILE), "a", encoding="utf-8", newline="") as f:               # 빈칸 → missing
            f.write("9F-C1,2027-01-05,AC-1,,박안전,\n")
        assert after_status(t, "2027-01-06")["9F-C1"]["missing"] == list(ITEMS)
        with open(os.path.join(t, FILE), "a", encoding="utf-8", newline="") as f:               # G9 침하 계측: 반복·같은 날 뒤 행·지점 다름
            f.write("9F-A1,2027-01-05,SET-P1,1.0,박안전,\n9F-A1,2027-01-05,set-p2,0.5,박안전,\n9F-A1,2027-01-06,SET-P1,1.5,박안전,\n"
                    "9F-A1,2027-01-06,SET-P2,0.8,박안전,\n9F-A1,2027-01-06,SET-P2,0.7,박안전,\n9F-A1,2027-01-08,SET-P1,2.0,박안전,\n9F-A1,2027-01-05,SET-,9,박안전,\n")
        se = settlements(t, "2027-01-07")["9F-A1"]
        assert se == {"points": ["P1", "P2"], "days": {"2027-01-05": {"P1": "1.0", "P2": "0.5"}, "2027-01-06": {"P1": "1.5", "P2": "0.7"}}}, se
        assert same_points(se) == (True, []) and same_points(settlements(t, "2027-01-08")["9F-A1"]) == (False, ["2027-01-08"])
        assert after_status(t, "2027-01-07")["9F-A1"]["ok"] is True                              # 침하 줄은 5항목에 안 섞임
        print("after_check self-check OK")
    finally:
        shutil.rmtree(t, ignore_errors=True)

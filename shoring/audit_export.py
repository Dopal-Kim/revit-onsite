"""감사 기록 (docs/impact_map.md #4 책임 추적, 총괄 결정 11-05 ⑩) — 승인 기록·근거 원장·동바리 확인·동바리 점검(F16)·판정 이력·해체 후 점검을 시각 순으로 한 표에.

새 판단은 없다: 데이터 폴더의 여섯 파일을 읽기만 한다 (원본은 그대로).
열: when, source, zone_id, action, actor, detail, line_sha
  line_sha = 원본 줄 값(그 파일의 열 순서)을 "|" 로 이은 글자의 SHA-256 앞 16자 — 원본 줄을 고치면 달라진다.
  승인 기록의 해시 사슬 끝은 detail 의 "사슬 …" (approval.verify 로 따로 검사).
실행: python audit_export.py <데이터 폴더> [--out 감사기록.csv]   ·   자체 검사: --self-test
"""
import argparse
import csv
import hashlib
import os

import approval as ap
import evaluate as ev
import evidence as evd

COLS = ["when", "source", "zone_id", "action", "actor", "detail", "line_sha"]


def _g(r, k):
    return (r.get(k) or "").strip()


def _join(*xs):
    return " · ".join(x for x in xs if x)


def _photo(r):
    return _join(_g(r, "photo"), _g(r, "photo_sha256") and "sha " + _g(r, "photo_sha256")[:12])


def _stage(s):
    return f"{s} {evd.STAGES[int(s)]}" if s.isdigit() and int(s) in evd.STAGES else s


# (파일, 출처 이름, 줄 → (when, zone_id, action, actor, detail))
SOURCES = [
    ("approvals.csv", "승인 기록", lambda r: (_g(r, "timestamp"), _g(r, "zone_id"), _g(r, "action"), _g(r, "actor"),
                                          _join(_g(r, "note"), _g(r, "approver") and "감리 " + _g(r, "approver"),
                                                _g(r, "signed_at") and "서명 " + _g(r, "signed_at"), ap.source_label(r) and "출처 " + ap.source_label(r),   # W-A
                                                _g(r, "sha") and "사슬 " + _g(r, "sha")[:12]))),
    ("evidence.csv", "근거 원장", lambda r: (_g(r, "date"), _g(r, "zone_id"), _stage(_g(r, "stage")), _g(r, "by"),
                                          _join(_g(r, "doc_no"), _g(r, "result"), _g(r, "file"), _g(r, "sha256") and "파일 " + _g(r, "sha256")[:12]))),
    # 사진 줄: detail = 경로 · sha 앞 12자 (F15, 총괄 29차) — line_sha 는 그대로 (원본 줄 값 전체)
    ("shore_checks.csv", "동바리 확인", lambda r: (_g(r, "timestamp"), _g(r, "zone_id"), "동바리 " + _g(r, "present"), _g(r, "actor"), _photo(r))),
    # F16 동바리 점검 (install_inspection.csv, 총괄 60차): 시각 = timestamp (옛 설치 검측 줄은 date), 단계 빈칸 = 설치 검측, detail = 동바리 ID · 입력 경로 · 사진
    ("install_inspection.csv", "동바리 점검", lambda r: (_g(r, "timestamp") or _g(r, "date"), _g(r, "zone_id"), _join(_g(r, "stage") or "설치 검측", _g(r, "item_code"), _g(r, "result")),
                                                     _g(r, "inspector"), _join(_g(r, "shore_ids") and "동바리 " + _g(r, "shore_ids"), _g(r, "source"), _g(r, "photo_path")))),
    # 판정 이력: 시각 = 판정 기준 시각(eval_at) — 다른 줄(승인 기록 = 시연 시각 고정 규칙)과 같은 시간축, 실제로 적은 시각은 detail "기록 …" (총괄 11-05 25차)
    ("eval_log.csv", "판정 이력", lambda r: (_g(r, "eval_at"), "", "판정", "", _join(_g(r, "summary"), _g(r, "logged_at") and "기록 " + _g(r, "logged_at")[:16].replace("T", " ")))),
    ("after_check.csv", "해체 후 점검", lambda r: (_g(r, "date"), _g(r, "zone_id"), _join(_g(r, "item_code"), _g(r, "result")), _g(r, "by"), _photo(r))),
]


def rows(folder):
    """→ [{when, source, zone_id, action, actor, detail, line_sha}] 시각 순 (같은 시각은 위 SOURCES 순서·파일 순서)."""
    out = []
    for name, label, f in SOURCES:
        for r in ev.read(folder, name):
            when, zone, action, actor, detail = f(r)
            line = "|".join(r[c] or "" for c in r if c is not None)
            out.append({"when": when, "source": label, "zone_id": zone, "action": action, "actor": actor, "detail": detail,
                        "line_sha": hashlib.sha256(line.encode("utf-8")).hexdigest()[:16]})
    return sorted(out, key=lambda x: x["when"])


def write_csv(rs, path):
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(COLS)
        w.writerows([r[c] for c in COLS] for r in rs)


if __name__ == "__main__":
    import sys
    [s.reconfigure(errors="replace") for s in (sys.stdout, sys.stderr) if hasattr(s, "reconfigure")]
    a = argparse.ArgumentParser()
    a.add_argument("folder", nargs="?")
    a.add_argument("--out")
    a.add_argument("--self-test", action="store_true")
    o = a.parse_args()
    if o.self_test:
        import shutil
        import tempfile
        from datetime import datetime
        import alerts as al
        import approval as ap
        sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tools"))
        import make_demo_data as md
        t = tempfile.mkdtemp()
        try:
            md.make(t)
            at = datetime(2026, 12, 31, 9)
            n0 = len(rows(t))
            assert n0 == len(ev.read(t, "evidence.csv")) + len(ev.read(t, "install_inspection.csv")), n0   # 시연 자료 = 원장 + 설치 검측
            assert next(r for r in rows(t) if r["source"] == "동바리 점검")["action"] == "설치 검측 · SHR-H · OK"
            old = open(os.path.join(t, "install_inspection.csv"), encoding="utf-8").read().splitlines()   # F16 열을 더하고 타설 전 점검 NG 한 줄 (서버가 쓰는 것과 같게)
            open(os.path.join(t, "install_inspection.csv"), "w", encoding="utf-8").write("\n".join([old[0] + ",stage,shore_ids,timestamp,source"] + [x + ",,,," for x in old[1:]]
                + ["9F-A2,SHR-B,NG,박안전,2026-12-31,,타설 전,9F-A2-R01C01;9F-A2-R01C02,2026-12-31T08:30,휴대폰"]) + "\n")
            with open(os.path.join(t, "shore_checks.csv"), "w", encoding="utf-8") as f:
                f.write("zone_id,timestamp,present,actor,photo\n9F-A2,2026-12-31T08:40,아니오,박안전,a.jpg\n")
            with open(os.path.join(t, "after_check.csv"), "w", encoding="utf-8") as f:
                f.write("zone_id,date,item_code,result,by,photo\n9F-A1,2026-12-31,AC-1,OK,박안전,\n")
            b1 = next(r for r in ev.evaluate(t, at) if r["zone_id"] == "9F-B1")
            ap.record(t, "9F-B1", "요청 제출", "김현장", b1, note="요청서 제출", now=datetime(2026, 12, 30, 9))
            ap.record(t, "9F-B1", "감리 승인 기록", "김현장", b1, now=at, approver="이감리", signed_at="2026-12-31 15:00")
            al.log_eval(t, at, "해체 승인 요청 가능 4 · 존치 2 · 공시체 시험 필요 0", now=datetime(2026, 12, 31, 9, 5))
            rs = rows(t)
            assert len(rs) == n0 + 6 and [r["when"] for r in rs] == sorted(r["when"] for r in rs)
            sc = next(r for r in rs if r["source"] == "동바리 확인")
            assert (sc["zone_id"], sc["action"], sc["actor"], sc["detail"]) == ("9F-A2", "동바리 아니오", "박안전", "a.jpg"), sc
            apr = [r for r in rs if r["source"] == "승인 기록"]
            assert apr[1]["action"] == "감리 승인 기록" and "감리 이감리" in apr[1]["detail"] and "서명 2026-12-31T15:00" in apr[1]["detail"], apr
            ev_line = next(r for r in rs if r["source"] == "판정 이력")
            assert (ev_line["when"], ev_line["detail"]) == ("2026-12-31T09:00", "해체 승인 요청 가능 4 · 존치 2 · 공시체 시험 필요 0 · 기록 2026-12-31 09:05"), ev_line
            f16 = next(r for r in rs if r["source"] == "동바리 점검" and r["when"] == "2026-12-31T08:30")
            assert (f16["zone_id"], f16["action"], f16["actor"], f16["detail"]) == ("9F-A2", "타설 전 · SHR-B · NG", "박안전", "동바리 9F-A2-R01C01;9F-A2-R01C02 · 휴대폰"), f16
            assert all(len(r["line_sha"]) == 16 for r in rs)
            p = os.path.join(t, "감사기록.csv")
            write_csv(rs, p)
            assert sum(1 for _ in open(p, encoding="utf-8-sig")) == len(rs) + 1
            assert [r["line_sha"] for r in rows(t)] == [r["line_sha"] for r in rs]                   # 같은 원본이면 같은 해시
            with open(os.path.join(t, "shore_checks.csv"), "w", encoding="utf-8") as f:            # 뒤에 '아니오' → '예' 로 고치면 그 줄 해시가 바뀜
                f.write("zone_id,timestamp,present,actor,photo\n9F-A2,2026-12-31T08:40,예,박안전,a.jpg\n")
            assert next(r for r in rows(t) if r["source"] == "동바리 확인")["line_sha"] != sc["line_sha"]
        finally:
            shutil.rmtree(t, ignore_errors=True)
        print("audit_export self-check OK")
    else:
        if not o.folder:
            a.error("데이터 폴더가 필요합니다 (또는 --self-test)")
        rs = rows(o.folder)
        if o.out:
            write_csv(rs, o.out)
        print(f"감사 기록 {len(rs)}줄" + (f" → {o.out}" if o.out else ""))

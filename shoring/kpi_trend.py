"""지표 추이 (docs/direction_log.md 5판 F13) — 판정 이력(eval_log.csv)의 판정 시각마다 kpi 를 다시 재서 표 한 장.

왜: 도입 효과는 한 시점 값이 아니라 '시간이 지나며 바뀌나'(타설 전 확인 비율↑, [가정] 비율↓, 무단 해체 의심 0)로 보인다(impact_map 지표 열).
새 판단 없음 — kpi.kpis 를 판정 시각마다 부름. 같은 판정 시각이 여러 줄이면 한 번만.
실행: python kpi_trend.py <데이터 폴더> [--csv 파일]   ·   자체 검사: --self-test
"""
import argparse
import csv
import os
from datetime import datetime

import evaluate as ev
import kpi

HEAD = ["eval_at", "review_median_h", "shore_checked", "shore_of", "removal_suspect", "evidence_avg", "assumed", "judged"]


def trend(folder):
    seen, out = set(), []
    for h in ev.read(folder, "eval_log.csv"):
        at = (h.get("eval_at") or "").strip()
        if not at or at in seen:
            continue
        seen.add(at)
        k = kpi.kpis(folder, datetime.fromisoformat(at))
        out.append([at, "" if k["review_hours"]["median"] is None else k["review_hours"]["median"], k["shore_check_rate"]["checked"],
                    k["shore_check_rate"]["of"], k["removal_suspect"], "" if k["evidence_full"] is None else k["evidence_full"],
                    k["assumed_share"]["assumed"], k["assumed_share"]["of"]])
    return sorted(out)


if __name__ == "__main__":
    import sys
    [s.reconfigure(errors="replace") for s in (sys.stdout, sys.stderr) if hasattr(s, "reconfigure")]
    p = argparse.ArgumentParser()
    p.add_argument("folder", nargs="?")
    p.add_argument("--csv")
    p.add_argument("--self-test", action="store_true")
    o = p.parse_args()
    if o.self_test:
        import shutil
        import tempfile
        sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tools"))
        import make_demo_data as md
        t = tempfile.mkdtemp()
        try:
            md.make(t)
            with open(os.path.join(t, "eval_log.csv"), "w", encoding="utf-8") as f:
                f.write("eval_at,logged_at,summary\n2026-12-20T09:00,x,a\n2026-12-31T09:00,x,b\n2026-12-20T09:00,y,a\n")
            r = trend(t)
            assert [x[0] for x in r] == ["2026-12-20T09:00", "2026-12-31T09:00"], r          # 같은 시각 한 번, 시각 순
            assert all(len(x) == len(HEAD) for x in r) and r[1][7] == 24
        finally:
            shutil.rmtree(t, ignore_errors=True)
        print("kpi_trend self-check OK")
    else:
        if not o.folder:
            p.error("데이터 폴더 (또는 --self-test)")
        r = trend(o.folder)
        if o.csv:
            with open(o.csv, "w", encoding="utf-8-sig", newline="") as f:
                csv.writer(f).writerows([HEAD] + r)
        for x in r:
            print(" · ".join(f"{h} {v}" for h, v in zip(HEAD, x)))

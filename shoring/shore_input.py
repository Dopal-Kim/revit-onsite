"""v6 2단계 동바리 입력 (docs/redesign_v6.md §2 · '자료 형식 확정' 11-08) — 사양·설치일, 구역 해체 기록, 3개층 확인 표.

  shore_spec.csv    floor, zone_id, type, spec, spacing_x_mm, spacing_y_mm, splices, install_date (+ 현장 입력 공통 열)
                    zone_id 비우면 그 층 전체, 구역 줄이 있으면 그 구역이 우선. 쓰기 = 현장 입력 서버 종류 shore_spec (Revit [사양] 창, 휴대폰과 같은 검사)
  shore_removed.csv id, zone_id, x, y, removed_date, by, note — Revit [구역 해체]가 모델에서 지운 동바리 위치 (휴대폰·보고서 이력)
  3개층 확인 표   층마다 지금 서 있는 본수(shores.csv = 모델 동바리, 애드인이 씀)·해체 본수(shore_removed.csv)·설치 누적 = 둘의 합.
                    타설된 맨 위층과 그 아래 2개층(층 순서, 타설층 포함 3개층 [확정] 가이드라인 2023)에 서 있는 동바리가 0 이면 빨강.
판정은 바꾸지 않는다 (판정은 evaluate.py). 표준 라이브러리만 — C# Engine ShoreInput.cs 와 같은 값 (Engine.Check).
실행: python shore_input.py <데이터 폴더>   ·   자체 검사: --self-test
"""
import csv
import math
import os

import engine as e
import evaluate as ev

SPEC, REMOVED, SHORES = "shore_spec.csv", "shore_removed.csv", "shores.csv"
SPEC_COLS = ["floor", "zone_id", "type", "spec", "spacing_x_mm", "spacing_y_mm", "splices", "install_date"]
REMOVED_COLS = ["id", "zone_id", "x", "y", "removed_date", "by", "note"]
TYPES = {"파이프 서포트": "파이프서포트", "시스템 동바리": "시스템"}   # 화면 이름 → SHR_Type (install_rules shore_type 값)
SPACING_MM = (200, 3000)   # [가정] 입력 범위 — 오타 거르기 (값은 조립도에서 [입력])
SPLICES = (0, 9)           # [가정] 입력 범위 — 이음 1개 이하 규칙은 설치 검측이 봄 (shore_inspect SHR-S)


def floor_label(fl, zone_ids):
    """층 이름 = 그 층 첫 구역 ID 의 '-' 앞 (B1·PH), 없으면 번호로 (음수 = B) — field_server.floor_name 과 같은 규칙."""
    head = zone_ids[0].split("-")[0] if zone_ids and "-" in zone_ids[0] else ""
    return head or (f"B{-fl}" if fl < 0 else f"{fl}F")


def spec_of(folder, floor, zone_id=""):
    """그 층(·구역)에 쓰는 사양 줄 — 구역 줄이 있으면 그 줄, 없으면 층 전체 줄 (같은 키가 여럿이면 뒤 줄). 없으면 None."""
    rows = [r for r in ev.read(folder, SPEC) if (r.get("floor") or "").strip() == str(floor)]
    zone = [r for r in rows if zone_id and (r.get("zone_id") or "").strip() == zone_id]
    whole = [r for r in rows if not (r.get("zone_id") or "").strip()]
    return (zone or whole or [None])[-1]


def spec_count(folder, zone_id, floor, rect):
    """W6 구역 본수 모드: 그 구역 사양(spec_of)에 X·Y 간격이 있으면 ⌊구역 평면 면적 ÷ (X×Y)⌋, 없거나 못 읽으면 None (사양 계산 — 모델 동바리가 없을 때만 씀)"""
    sp = spec_of(folder, floor, zone_id)
    try:
        x, y = float(sp["spacing_x_mm"]) / 1000, float(sp["spacing_y_mm"]) / 1000
    except (TypeError, KeyError, ValueError):
        return None
    if x <= 0 or y <= 0:
        return None
    return math.floor((rect[2] - rect[0]) * (rect[3] - rect[1]) / (x * y) + 1e-9)   # 1e-9: 나누어떨어질 때 61.9999… → 62


def write_removed(folder, rows):
    """해체 기록 줄 추가 (추가만, 머리 없으면 만듦) — rows = [{id, zone_id, x, y, removed_date, by, note}]. 반환 = 줄 수."""
    p = os.path.join(folder, REMOVED)
    new = not os.path.exists(p) or os.path.getsize(p) == 0
    with open(p, "a", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=REMOVED_COLS)
        if new:
            w.writeheader()
        for r in rows:
            w.writerow({k: r.get(k, "") for k in REMOVED_COLS})
    return len(rows)


def table(folder):
    """3개층 확인 표 → [{floor, label, installed, removed, standing, red, spec}] 위층부터.
    standing = shores.csv 의 그 층 구역 동바리 수 + spec (W6: 모델 동바리 0·해체 기록 없는 구역의 사양 계산 본수), removed = shore_removed.csv 의 그 층 구역 줄 수, installed = 둘의 합.
    red = 타설된 맨 위층(층 순서)부터 아래로 3개층 [확정] 안인데 standing 이 0."""
    zrows = ev.read(folder, "zones.csv")
    floor_of = {r["zone_id"]: int(r["floor"]) for r in zrows}
    rect_of = {r["zone_id"]: tuple(float(r[k]) for k in ("x0", "y0", "x1", "y1")) for r in zrows}
    order = sorted(set(floor_of.values()))
    zones_of = {f: sorted(z for z, g in floor_of.items() if g == f) for f in order}
    standing, model = {f: 0 for f in order}, {}
    for r in ev.read(folder, SHORES):
        z = (r.get("zone_id") or "").strip()
        f = floor_of.get(z)
        if f is not None:
            standing[f] += 1
            model[z] = model.get(z, 0) + 1
    removed, gone = {f: 0 for f in order}, set()
    for r in ev.read(folder, REMOVED):
        z = (r.get("zone_id") or "").strip()
        f = floor_of.get(z)
        if f is not None:
            removed[f] += 1
            gone.add(z)
    spec = {f: 0 for f in order}                             # W6 구역 본수 모드 (사양 계산)
    for z, f in floor_of.items():
        if not model.get(z) and z not in gone and (n := spec_count(folder, z, f, rect_of[z])):
            spec[f] += n
            standing[f] += n
    poured = {floor_of[p["zone_id"]] for p in ev.read(folder, "pours.csv") if p.get("zone_id") in floor_of}
    need = []
    if poured:
        i = order.index(max(poured, key=order.index))
        need = order[max(0, i - (e.MIN_LEVELS - 1)):i + 1]
    return [{"floor": f, "label": floor_label(f, zones_of[f]), "installed": standing[f] + removed[f], "removed": removed[f],
             "standing": standing[f], "red": f in need and standing[f] == 0, "spec": spec[f]} for f in reversed(order)]


def table_text(rows):
    """표를 글 줄로 (애드인 [3개층 확인] 창·Engine.Check 대조)."""
    return [f"{r['label']} 설치 {r['installed']} · 해체 {r['removed']} · 지금 {r['standing']}" + (f" (사양 계산 {r['spec']})" if r.get("spec") else "")
            + (f" — 빨강: 타설층 포함 {e.MIN_LEVELS}개층 존치 필요인데 동바리 없음" if r["red"] else "") for r in rows]


if __name__ == "__main__":
    import sys
    [s.reconfigure(errors="replace") for s in (sys.stdout, sys.stderr) if hasattr(s, "reconfigure")]
    if "--self-test" in sys.argv:
        import shutil
        import tempfile
        t = tempfile.mkdtemp()
        try:
            w = lambda n, s: open(os.path.join(t, n), "w", encoding="utf-8", newline="").write(s)   # noqa: E731
            w("zones.csv", "zone_id,floor,x0,y0,x1,y1\nB1-A1,-1,0,0,10,9\n1F-A1,1,0,0,10,9\n2F-A1,2,0,0,10,9\n3F-A1,3,0,0,10,9\n3F-B1,3,10,0,20,9\n4F-A1,4,0,0,10,9\n")
            w("pours.csv", "zone_id,pour_date,pour_time,mix,fck\nB1-A1,2026-10-01,08:00,보통,24\n1F-A1,2026-10-08,08:00,보통,24\n2F-A1,2026-10-15,08:00,보통,24\n3F-A1,2026-10-22,08:00,보통,24\n")
            w("shores.csv", "id,zone_id,x,y,field_label\n" + "".join(f"3F-A1-R01C0{i},3F-A1,{i},1,\n" for i in range(1, 4)) + "3F-B1-R01C01,3F-B1,11,1,\n1F-A1-R01C01,1F-A1,1,1,\n")
            assert table(t)[0]["floor"] == 4 and [r["red"] for r in table(t)] == [False, False, True, False, False], table(t)   # 3F·2F·1F 중 2F 비어 빨강
            assert write_removed(t, [{"id": "2F-A1-R01C01", "zone_id": "2F-A1", "x": 1.0, "y": 1, "removed_date": "2026-11-01", "by": "박", "note": "구역 해체, 쉼표"}]) == 1
            write_removed(t, [{"id": "2F-A1-R01C02", "zone_id": "2F-A1", "x": 2, "y": 1, "removed_date": "2026-11-01", "by": "박"}])
            assert open(os.path.join(t, REMOVED), encoding="utf-8", newline="").read() == ("id,zone_id,x,y,removed_date,by,note\r\n2F-A1-R01C01,2F-A1,1.0,1,2026-11-01,박,\"구역 해체, 쉼표\"\r\n"
                                                                              "2F-A1-R01C02,2F-A1,2,1,2026-11-01,박,\r\n")
            r2 = next(r for r in table(t) if r["floor"] == 2)
            assert (r2["installed"], r2["removed"], r2["standing"], r2["red"]) == (2, 2, 0, True), r2
            assert table_text(table(t))[1] == "3F 설치 4 · 해체 0 · 지금 4" and "빨강" in table_text(table(t))[2], table_text(table(t))
            w(SPEC, ",".join(SPEC_COLS) + "\n3,,파이프 서포트,V4,1200,1200,0,2026-10-20\n3,3F-B1,시스템 동바리,SY-1,1500,1500,0,2026-10-20\n")
            assert spec_of(t, 3, "3F-B1")["spec"] == "SY-1" and spec_of(t, 3, "3F-A1")["spec"] == "V4" and spec_of(t, 3)["type"] == "파이프 서포트" and spec_of(t, 2) is None
            # W6: 모델 동바리 없는 4F-A1 (10 × 9 m, 3층 줄 없음 → None) · 3F 는 모델 있음 → 사양 계산 안 함. 4F 사양 1.2 × 1.5 → ⌊90 ÷ 1.8⌋ = 50
            assert spec_count(t, "4F-A1", 4, (0, 0, 10, 9)) is None and table(t)[0]["spec"] == 0
            w(SPEC, ",".join(SPEC_COLS) + "\n3,,파이프 서포트,V4,1200,1200,0,2026-10-20\n3,3F-B1,시스템 동바리,SY-1,1500,1500,0,2026-10-20\n4,,파이프 서포트,V4,1200,1500,0,2026-10-27\n2,,파이프 서포트,V4,1200,1200,0,2026-10-13\n")
            r4 = table(t)[0]
            assert (r4["standing"], r4["spec"], r4["installed"]) == (50, 50, 50) and table_text([r4])[0] == "4F 설치 50 · 해체 0 · 지금 50 (사양 계산 50)", r4
            assert next(r for r in table(t) if r["floor"] == 2)["spec"] == 0                    # 2F-A1 은 해체 기록이 있어 사양 계산 안 함 (지운 동바리)
            assert next(r for r in table(t) if r["floor"] == 3)["spec"] == 0                    # 3F 는 모델 동바리가 있음
            os.remove(os.path.join(t, "pours.csv"))
            assert not any(r["red"] for r in table(t))                                          # 타설 전이면 빨강 없음
        finally:
            shutil.rmtree(t, ignore_errors=True)
        print("shore_input self-check OK")
        raise SystemExit(0)
    print("\n".join(table_text(table(sys.argv[1]))))

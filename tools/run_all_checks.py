"""자동 점검 한 번에 (오류 점검 ② 단계 1층 — docs/error_review_plan.md). Revit 없이 돌고, 점검마다 결과 한 줄만 찍는다(토큰 절약).

실행: python tools\\run_all_checks.py [--no-dotnet] [--only 이름,이름]
끝 줄: "합계 통과 n / 실패 m" — 실패가 있으면 종료 코드 1, 실패한 점검만 마지막 출력 12줄을 덧붙인다.
Engine.Check 는 shoring\\sample 기대값이 옛 문구(사용자 승인 전 재생성 금지)라 새 Python 결과를 --expected 로 넘긴다.
"""
import os
import subprocess
import sys
import tempfile
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SH, TL = os.path.join(ROOT, "shoring"), os.path.join(ROOT, "tools")
PY = sys.executable
RS = os.path.join(ROOT, "revit", "RevitOnSite")


def checks(dotnet=True):
    py = [(n, [PY, f], SH) for n, f in (
        ("gate", "test_gate.py"), ("calibrate", "test_calibrate.py"), ("no_sensor", "test_no_sensor.py"),
        ("cold", "test_cold.py"), ("engine", "engine.py"), ("evidence", "evidence.py"), ("approval", "approval.py"), ("pour_gate", "pour_gate.py"),
        ("install_rules", "install_rules.py"), ("after_check", "after_check.py"), ("lot", "test_lot.py"), ("scope", "test_scope.py"))]   # lot = v6.2 W1 공시체 타설 로트, scope = W11 범위 밖
    py += [("alerts", [PY, "alerts.py", "--self-test"], SH), ("quality", [PY, "quality.py", "--self-test"], SH),
           ("weather_import", [PY, "weather_import.py", "--self-test"], SH), ("scenarios", [PY, "scenarios.py"], SH),
           ("field_server", [PY, "test_field_server.py"], TL), ("server_sync", [PY, "test_server_sync.py"], TL), ("check_ifc", [PY, "check_ifc.py", "--self-test"], TL),   # server_sync = vercel 세션 시험 (W-A 출처 단언 포함)
           ("slab_record", [PY, "slab_record.py", "--self-test"], SH),
           ("audit_export", [PY, "audit_export.py", "--self-test"], SH),
           ("sensor_import", [PY, "sensor_import.py", "--self-test"], SH), ("photos", [PY, "photos.py", "--self-test"], SH), ("kpi", [PY, "kpi.py", "--self-test"], SH), ("package", [PY, "package.py", "--self-test"], SH), ("kpi_trend", [PY, "kpi_trend.py", "--self-test"], SH), ("staged", [PY, "staged.py", "--self-test"], SH), ("shore_status", [PY, "shore_status.py", "--self-test"], SH), ("shore_inspect", [PY, "shore_inspect.py"], SH), ("shore_input", [PY, "shore_input.py", "--self-test"], SH), ("strength_timeline", [PY, "strength_timeline.py", "--self-test"], SH), ("zone_info", [PY, "zone_info.py", "--self-test"], SH),
           ("ifc_add_evidence", [PY, "ifc_add_evidence.py", "--self-test"], TL),
           ("result_report", [PY, "result_report.py"], SH),   # 11-12 2-6 G10 해체 결과보고·원장 8
           ("print_pages", [PY, "print_pages_check.py"], TL)]   # 11-11 1-9 인쇄 쪽수 (요청서·조기 탈형 검토서)
    if dotnet:
        exp = os.path.join(tempfile.gettempdir(), "RevitOnSite_expected_1231.csv")
        py += [("expected(py)", [PY, "evaluate.py", os.path.join("sample", "data"), "--at", "2026-12-31T09:00", "--out", exp], SH),
               ("Engine.Check", ["dotnet", "run", "-c", "Release", "--project", os.path.join(RS, "RevitOnSite.Engine.Check"), "--", "--expected", exp], ROOT),
               ("Field.Check", ["dotnet", "run", "-c", "Release", "--project", os.path.join(RS, "RevitOnSite.Field.Check")], ROOT),
               ("WebExport.Check", ["dotnet", "run", "-c", "Release", "--project", os.path.join(RS, "RevitOnSite.WebExport.Check")], ROOT),
               # 85차 (총괄 R21): 대시보드 XAML 을 창 400–1080 px × 폭 2 × 색 3 × 펼침/접힘으로 띄워 강도 그림이 첫 화면 안·160 px 그대로인지
               ("Layout.Check", ["dotnet", "run", "-c", "Release", "--project", os.path.join(RS, "RevitOnSite.Layout.Check")], ROOT)]
    return py


def ok_line(name, code, out):
    lines = [l for l in out.splitlines() if l.strip()]
    last = lines[-1] if lines else ""
    if name == "scenarios":       # PASS 개수와 FAIL 유무
        n_pass, n_fail = sum("PASS" in l for l in lines), sum("FAIL" in l and "FAIL 0" not in l for l in lines)
        return code == 0 and n_fail == 0, f"PASS {n_pass} · FAIL {n_fail}"
    return code == 0 and "Traceback" not in out and "MISMATCH" not in out, last[:140]


if __name__ == "__main__":
    [s.reconfigure(errors="replace") for s in (sys.stdout, sys.stderr) if hasattr(s, "reconfigure")]
    args = sys.argv[1:]
    only = set(args[args.index("--only") + 1].split(",")) if "--only" in args else None
    env = {**os.environ, "PYTHONIOENCODING": "utf-8"}
    passed, failed = 0, []
    for name, cmd, cwd in checks("--no-dotnet" not in args):
        if only and name not in only:
            continue
        t0 = time.time()
        try:
            p = subprocess.run(cmd, cwd=cwd, env=env, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=900)
            out, code = p.stdout + p.stderr, p.returncode
        except (subprocess.TimeoutExpired, FileNotFoundError) as x:
            out, code = str(x), -1
        good, msg = ok_line(name, code, out)
        print(f"{'통과' if good else '실패'} {name:16s} {time.time() - t0:5.0f}s  {msg}")
        if good:
            passed += 1
        else:
            failed.append((name, out))
    for name, out in failed:
        print(f"\n--- {name} 마지막 12줄 ---\n" + "\n".join(out.splitlines()[-12:]))
    print(f"\n합계 통과 {passed} / 실패 {len(failed)}")
    sys.exit(1 if failed else 0)

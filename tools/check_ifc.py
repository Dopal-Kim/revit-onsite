r"""IFC 속성 세트 검사 — [IFC 내보내기](D3) 결과 파일에서 Pset_RevitOnSite_Zone 이 있고 구역마다 결론(Decision)이 들었는지 본다.
표준 라이브러리만. IFC(STEP) 문자열의 한글은 \X2\...\X0\ 로 들어 있어 grep 대신 이 스크립트로 풀어서 본다.
실행: python tools\check_ifc.py <ifc 파일> [구역 ID] [--zones 모델 구역 수] [--shores 동바리 수]   (구역 ID 를 주면 그 구역 속성 전부)
      python tools\check_ifc.py --self-test
종료 코드: Pset 이 없거나 Decision 이 하나도 없거나, 기대 수와 다르거나, 고른 구역이 없으면 1.
"""
import re
import sys

PSET = "Pset_RevitOnSite_Zone"
SHORE = "Pset_RevitOnSite_Shore"


def decode(s):
    r"""STEP 문자열 → 글자: '' → ', \X2\hhhh…\X0\ (UTF-16), \X4\hhhhhhhh…\X0\ (UTF-32)."""
    s = re.sub(r"\\X2\\((?:[0-9A-Fa-f]{4})+)\\X0\\", lambda m: bytes.fromhex(m.group(1)).decode("utf-16-be"), s)
    s = re.sub(r"\\X4\\((?:[0-9A-Fa-f]{8})+)\\X0\\", lambda m: bytes.fromhex(m.group(1)).decode("utf-32-be"), s)
    return s.replace("''", "'")


def zone_psets(text, pset=PSET):
    """→ [{속성 이름: 값}] 속성 세트 하나에 하나 (같은 이름 세트가 여러 요소에 붙음)."""
    ent = {}
    for m in re.finditer(r"#(\d+)\s*=\s*([A-Z0-9_]+)\((.*?)\);\s*$", text, re.M | re.S):
        ent[m.group(1)] = (m.group(2), m.group(3))
    out = []
    for kind, args in ent.values():
        if kind != "IFCPROPERTYSET":
            continue
        name = re.findall(r"'((?:[^']|'')*)'", args)
        if len(name) < 2 or decode(name[1]) != pset:
            continue
        props = {}
        refs = re.search(r"\(([^()]*)\)\s*$", args)
        for ref in re.findall(r"#(\d+)", refs.group(1) if refs else ""):
            k, a = ent.get(ref, ("", ""))
            if k != "IFCPROPERTYSINGLEVALUE":
                continue
            pn = re.match(r"\s*'((?:[^']|'')*)'", a)
            val = re.search(r"IFC[A-Z]+\(\s*('((?:[^']|'')*)'|[^)]*)\)", a)
            if pn:
                props[decode(pn.group(1))] = decode(val.group(2)) if val and val.group(2) is not None else (val.group(1).strip() if val else "")
        out.append(props)
    return out


def main(argv):
    if argv and argv[0] == "--self-test":
        sample = ("#10=IFCPROPERTYSINGLEVALUE('ZoneID',$,IFCTEXT('9F-A1'),$);\n"
                  "#11=IFCPROPERTYSINGLEVALUE('Decision',$,IFCTEXT('\\X2\\D574CCB4\\X0\\ \\X2\\C2B9C778\\X0\\ \\X2\\C694CCAD\\X0\\ \\X2\\AC00B2A5\\X0\\'),$);\n"
                  "#12=IFCPROPERTYSINGLEVALUE('Status',$,IFCINTEGER(5),$);\n"
                  "#13=IFCPROPERTYSET('2abc',#5,'Pset_RevitOnSite_Zone',$,(#10,#11,#12));\n"
                  "#14=IFCPROPERTYSET('2abd',#5,'Pset_RevitOnSite_Shore',$,(#12));\n")
        ps = zone_psets(sample)
        assert ps == [{"ZoneID": "9F-A1", "Decision": "해체 승인 요청 가능", "Status": "5"}], ps
        assert len(zone_psets(sample, SHORE)) == 1
        p = "check_ifc_selftest.ifc"
        import os
        import tempfile
        p = os.path.join(tempfile.mkdtemp(), p)
        open(p, "w", encoding="utf-8").write(sample)
        assert main([p, "9F-A1", "--zones", "1", "--shores", "1"]) == 0 and main([p, "--zones", "2"]) == 1   # 수가 다르면 1
        p2 = p.replace(".ifc", "_empty.ifc")   # ZoneID 없는 빈 구역 세트가 섞이면 1 (60차 R17: 96 = 90 + 6)
        open(p2, "w", encoding="utf-8").write(sample + "#15=IFCPROPERTYSINGLEVALUE('Floor',$,IFCINTEGER(9),$);\n#16=IFCPROPERTYSET('2abe',#5,'Pset_RevitOnSite_Zone',$,(#15));\n")
        assert main([p2, "--zones", "1"]) == 1
        print("check_ifc self-check OK")
        return 0
    want = {}
    for k in ("--zones", "--shores"):            # 기대 수 (모델 구역 수·동바리 수) — [IFC 내보내기] 결과 창에 이 명령이 그대로 나옴
        if k in argv:
            i = argv.index(k)
            want[k] = int(argv[i + 1])
            del argv[i:i + 2]
    if not argv:
        print(__doc__)
        return 2
    text = open(argv[0], encoding="utf-8", errors="replace").read()
    ps, shores = zone_psets(text), zone_psets(text, SHORE)
    zones = [p for p in ps if p.get("ZoneID")]
    print(f"{PSET}: {len(ps)}개 (ZoneID 있는 것 {len(zones)}개) · {SHORE}: {len(shores)}개")
    ok = bool(zones) and any(p.get("Decision") for p in zones)
    if len(ps) > len(zones):   # 60차 R17: ZoneID 없는 프록시(센서 표식 등)에 붙은 빈 구역 세트 — [IFC 내보내기]가 센서를 IfcSensor 로 내보내 막음
        print(f"  ZoneID 없는 {PSET} {len(ps) - len(zones)}개 — 구역이 아닌 프록시에 붙음 (센서 표식 등)")
        ok = False
    for k, n, label in (("--zones", len(zones), "구역 세트(ZoneID 있는 것)"), ("--shores", len(shores), "동바리 세트")):
        if k in want:
            same = n == want[k]
            ok = ok and same
            print(f"  {label} {n} {'=' if same else '≠'} 기대 {want[k]}")
    pick = [p for p in sorted(zones, key=lambda p: p["ZoneID"]) if len(argv) < 2 or p["ZoneID"] == argv[1]]   # 구역을 먼저 고르고 그다음 개수 제한 (22차: 9F-B1 이 안 찍힘)
    if len(argv) > 1 and not pick:
        print(f"  {argv[1]}: {PSET} 없음")
        ok = False
    for p in pick[:200]:
        print(f"  {p['ZoneID']}: {p.get('Decision', '(Decision 없음)')}" + ("" if len(argv) < 2 else "\n    " + "\n    ".join(f"{k} = {v}" for k, v in p.items())))
    return 0 if ok else 1


if __name__ == "__main__":
    for _s in (sys.stdout, sys.stderr):
        try:
            _s.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    sys.exit(main(sys.argv[1:]))

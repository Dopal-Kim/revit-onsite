r"""IFC 에 근거 속성 덧붙이기 — [IFC 내보내기] 결과 IFC 와 곁 CSV 를 읽어, 구역마다 Pset_RevitOnSite_Evidence
(EvidenceStage·Seal·EvalAt·Warnings, 값 없는 속성은 뺌)를 붙인 새 파일 <이름>_evidence.ifc 를 쓴다. 원본은 그대로.
공유 매개변수 보류로 근거 n/8·마감 값·판정 시각·경고가 IFC 안에 없어서(곁 CSV 에만) 만든 임시 다리.
표준 라이브러리만. STEP 줄을 정규식으로 읽는다(디코딩은 check_ifc.decode 와 같음).
실행: python tools\ifc_add_evidence.py <ifc> [<csv>]   (csv 생략 시 같은 이름 .csv)
      python tools\ifc_add_evidence.py --self-test
"""
import csv
import os
import re
import sys
import uuid

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from check_ifc import PSET, decode, zone_psets  # noqa: E402

EVIDENCE = "Pset_RevitOnSite_Evidence"
FIELDS = (("evidence_stage", "EvidenceStage"), ("seal", "Seal"), ("eval_at", "EvalAt"), ("warnings", "Warnings"))
_B64 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$"
_ENT = re.compile(r"#(\d+)\s*=\s*([A-Z0-9_]+)\((.*?)\);\s*$", re.M | re.S)   # check_ifc.zone_psets 와 같은 식
_STR = r"'((?:[^']|'')*)'"


def ifc_guid(u=None):
    """uuid(128비트) → IFC 압축 GlobalId 22자: 첫 글자 2비트 + 21×6비트."""
    n = (u or uuid.uuid4()).int
    return _B64[n >> 126] + "".join(_B64[(n >> (6 * i)) & 63] for i in range(20, -1, -1))


def encode(s):
    r"""글자 → STEP 문자열 본문: ' → '', \ → \\, 인쇄 가능 ASCII 밖은 \X2\UTF-16BE\X0\ (BMP 밖은 \X4\)."""
    out = []
    for m in re.finditer(r"[ -~]+|[^ -~]+", s):
        t = m.group(0)
        if t[0] >= " " and t[0] <= "~":
            out.append(t.replace("\\", "\\\\").replace("'", "''"))
        elif all(ord(c) <= 0xFFFF for c in t):
            out.append("\\X2\\" + t.encode("utf-16-be").hex().upper() + "\\X0\\")
        else:
            out.append("\\X4\\" + t.encode("utf-32-be").hex().upper() + "\\X0\\")
    return "".join(out)


def zone_elements(text):
    """→ {ZoneID: [요소 #id…]} — Pset_RevitOnSite_Zone 의 ZoneID → 그 세트 → 그것을 쓰는 IFCRELDEFINESBYPROPERTIES 의 대상."""
    ent = {m.group(1): (m.group(2), m.group(3)) for m in _ENT.finditer(text)}
    pset_zone = {}
    for i, (kind, args) in ent.items():
        if kind != "IFCPROPERTYSET":
            continue
        names = re.findall(_STR, args)
        if len(names) < 2 or decode(names[1]) != PSET:
            continue
        refs = re.search(r"\(([^()]*)\)\s*$", args)
        for ref in re.findall(r"#(\d+)", refs.group(1) if refs else ""):
            k, a = ent.get(ref, ("", ""))
            m = re.match(r"\s*'ZoneID'\s*,[^,]*,\s*IFC[A-Z]+\(\s*" + _STR, a) if k == "IFCPROPERTYSINGLEVALUE" else None
            if m:
                pset_zone[i] = decode(m.group(1))
    out = {}
    for kind, args in ent.values():
        if kind != "IFCRELDEFINESBYPROPERTIES":
            continue
        m = re.search(r"\(([^()]*)\)\s*,\s*#(\d+)\s*$", args)   # (RelatedObjects), #RelatingPropertyDefinition
        if m and m.group(2) in pset_zone:
            lst = out.setdefault(pset_zone[m.group(2)], [])
            lst += [r for r in re.findall(r"#(\d+)", m.group(1)) if r not in lst]
    return out


def add_evidence(ifc_path, csv_path=None):
    """→ (새 파일 경로, 붙인 구역 수, CSV 에만 있는 구역 목록). 원본은 읽기만."""
    base = os.path.splitext(ifc_path)[0]                    # 곁 CSV: <이름>_근거.csv (총괄 11-05 ⑤), 없으면 옛 이름 <이름>.csv
    csv_path = csv_path or (base + "_근거.csv" if os.path.exists(base + "_근거.csv") else base + ".csv")
    raw = open(ifc_path, "rb").read()
    text = raw.decode("utf-8", "surrogateescape")          # 그대로 되돌려 쓸 수 있게 (원본 바이트 보존)
    nl = "\r\n" if "\r\n" in text else "\n"
    ends = list(re.finditer(r"^ENDSEC;", text, re.M))
    if len(ends) < 2:
        raise ValueError(f"DATA 끝 ENDSEC 없음: {ifc_path}")
    at = ends[-1].start()                                   # 마지막 ENDSEC = DATA 끝
    oh = re.search(r"^#(\d+)\s*=\s*IFCOWNERHISTORY\(", text, re.M)
    oh = f"#{oh.group(1)}" if oh else "$"
    nid = max(int(x) for x in re.findall(r"^#(\d+)\s*=", text, re.M)) + 1
    zones = zone_elements(text)
    rows = list(csv.DictReader(open(csv_path, encoding="utf-8-sig", newline="")))
    lines, done, missing = [], 0, []
    for row in rows:
        zid = (row.get("zone_id") or "").strip()
        if not zid:
            continue
        if zid not in zones:
            missing.append(zid)
            continue
        props = []
        for col, name in FIELDS:
            v = (row.get(col) or "").strip()
            if v:
                lines.append(f"#{nid}=IFCPROPERTYSINGLEVALUE('{name}',$,IFCTEXT('{encode(v)}'),$);")
                props.append(f"#{nid}")
                nid += 1
        if not props:
            continue
        lines.append(f"#{nid}=IFCPROPERTYSET('{ifc_guid()}',{oh},'{EVIDENCE}',$,({','.join(props)}));")
        lines.append(f"#{nid + 1}=IFCRELDEFINESBYPROPERTIES('{ifc_guid()}',{oh},$,$,({','.join('#' + e for e in zones[zid])}),#{nid});")
        nid += 2
        done += 1
    out = os.path.splitext(ifc_path)[0] + "_evidence.ifc"
    new = text[:at] + "".join(l + nl for l in lines) + text[at:]
    open(out, "wb").write(new.encode("utf-8", "surrogateescape"))
    return out, done, missing


def self_test():
    import hashlib
    import shutil
    import tempfile
    d = tempfile.mkdtemp()
    try:
        ifc = os.path.join(d, "RevitOnSite_20991231.ifc")
        body = ("ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION(('ViewDefinition [ReferenceView_V1.2]'),'2;1');\n"
                "FILE_NAME('t.ifc','2099-12-31T00:00:00',(''),(''),'','','');\nFILE_SCHEMA(('IFC4'));\nENDSEC;\n\nDATA;\n"
                "#5=IFCPERSONANDORGANIZATION($,$,$);\n#6=IFCOWNERHISTORY(#5,$,$,.NOCHANGE.,$,$,$,0);\n"
                "#20=IFCBUILDINGELEMENTPROXY('1aaaaaaaaaaaaaaaaaaaaa',#6,'Z1',$,$,$,$,$,$);\n"
                "#21=IFCBUILDINGELEMENTPROXY('1aaaaaaaaaaaaaaaaaaaab',#6,'Z2',$,$,$,$,$,$);\n"
                "#30=IFCPROPERTYSINGLEVALUE('ZoneID',$,IFCTEXT('9F-A1'),$);\n"
                "#31=IFCPROPERTYSINGLEVALUE('ZoneID',$,IFCTEXT('9F-A2'),$);\n"
                "#32=IFCPROPERTYSET('2aaaaaaaaaaaaaaaaaaaaa',#6,'Pset_RevitOnSite_Zone','',(#30));\n"
                "#33=IFCPROPERTYSET('2aaaaaaaaaaaaaaaaaaaab',#6,'Pset_RevitOnSite_Zone','',(#31));\n"
                "#34=IFCRELDEFINESBYPROPERTIES('3aaaaaaaaaaaaaaaaaaaaa',#6,$,$,(#20),#32);\n"
                "#35=IFCRELDEFINESBYPROPERTIES('3aaaaaaaaaaaaaaaaaaaab',#6,$,$,(#21),#33);\n"
                "ENDSEC;\n\nEND-ISO-10303-21;\n").replace("\n", "\r\n")
        open(ifc, "wb").write(body.encode("ascii"))
        warn = "승인 기록 없이 동바리 없음 관측 — 9F-A2 'O'Neil' 확인"
        with open(ifc[:-4] + ".csv", "w", encoding="utf-8-sig", newline="") as f:
            w = csv.writer(f)
            w.writerow(["zone_id", "decision", "evidence_stage", "seal", "eval_at", "warnings"])
            w.writerow(["9F-A1", "해체 승인 요청 가능", "3/8", "880afa40", "2099-12-31T09:00", ""])
            w.writerow(["9F-A2", "존치", "3/8", "", "2099-12-31T09:00", warn])
            w.writerow(["9F-ZZ", "존치", "1/8", "", "", ""])
        before = hashlib.sha256(open(ifc, "rb").read()).hexdigest()
        out, n, missing = add_evidence(ifc)
        assert hashlib.sha256(open(ifc, "rb").read()).hexdigest() == before, "원본이 바뀜"
        assert (n, missing) == (2, ["9F-ZZ"]), (n, missing)
        text = open(out, encoding="utf-8", newline="").read()
        assert text.startswith(body[:body.rindex("ENDSEC;")]) and text.endswith("ENDSEC;\r\n\r\nEND-ISO-10303-21;\r\n")
        assert "\r\n" in text and "\n" not in text.replace("\r\n", "")   # 줄바꿈 방식 유지
        ids = re.findall(r"^#(\d+)\s*=", text, re.M)
        assert len(ids) == len(set(ids)) and min(int(i) for i in ids[-10:]) == 36, ids   # 겹침 없음, 최대+1 부터
        ev = zone_psets(text, EVIDENCE)
        assert ev == [{"EvidenceStage": "3/8", "Seal": "880afa40", "EvalAt": "2099-12-31T09:00"},
                      {"EvidenceStage": "3/8", "EvalAt": "2099-12-31T09:00", "Warnings": warn}], ev
        assert len(zone_psets(text)) == 2 and "#40=IFCRELDEFINESBYPROPERTIES(" in text and ",(#20),#39);" in text
        assert ",#6,'Pset_RevitOnSite_Evidence'," in text
        g = ifc_guid(uuid.UUID(int=0)), ifc_guid(uuid.UUID(int=(1 << 128) - 1))
        assert g == ("0" * 22, "3" + "$" * 21), g
        assert all(len(x) == 22 and set(x) <= set(_B64) for x in re.findall(r"IFCPROPERTYSET\('([^']*)'", text))
        assert decode(encode("a'b 한글 😀")) == "a'b 한글 😀"
        print("ifc_add_evidence self-check OK")
        return 0
    finally:
        shutil.rmtree(d, ignore_errors=True)


def main(argv):
    if argv and argv[0] == "--self-test":
        return self_test()
    if not argv:
        print(__doc__)
        return 2
    out, n, missing = add_evidence(argv[0], argv[1] if len(argv) > 1 else None)
    print(f"구역 {n}개에 {EVIDENCE} 추가 → {out}")
    if missing:
        print(f"  경고: CSV 에 있는데 IFC 에 없는 구역 {len(missing)}개: {', '.join(missing)}")
    return 0 if n else 1


if __name__ == "__main__":
    for _s in (sys.stdout, sys.stderr):
        try:
            _s.reconfigure(encoding="utf-8", errors="replace")
        except (AttributeError, ValueError):
            pass
    sys.exit(main(sys.argv[1:]))

"""IFC 축소: 동바리 검토에 필요한 구조 부재(층·슬래브·벽·기둥·보)만 남긴 IFC 를 만든다. 표준 라이브러리만.

Revit 이 큰 IFC(예: KBIM 126 MB, 엔티티 200만 개)를 가져오다 멈출 때 쓰는 우회 수단.
남김: 공간 구조(프로젝트·대지·건물·층), KEEP 부재, 그것들이 참조하는 모든 엔티티(형상·배치·재료·속성), 관계(목록에서 버린 부재만 뺌).
버림: 문·창·가구·설비·공간·개구부·난간·계단(계단참 슬래브는 남김) 등 KEEP 밖의 부재와 그 개구부 관계.
실행: python -I tools/ifc_filter.py <입력.ifc> <출력.ifc>
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ifc_survey import statements   # noqa: E402  (같은 폴더의 STEP 문 분리기 재사용)

KEEP = {"IFCSLAB", "IFCWALL", "IFCWALLSTANDARDCASE", "IFCCOLUMN", "IFCBEAM", "IFCFOOTING"}
SPATIAL = {"IFCPROJECT", "IFCSITE", "IFCBUILDING", "IFCBUILDINGSTOREY"}
DROP_RELS = {"IFCRELVOIDSELEMENT", "IFCRELFILLSELEMENT", "IFCRELSPACEBOUNDARY", "IFCRELCONNECTSPATHELEMENTS",
             "IFCRELCONNECTSELEMENTS", "IFCRELCONNECTSPORTS", "IFCRELCONNECTSPORTTOELEMENT", "IFCRELSERVICESBUILDINGS"}
HEAD = re.compile(r"#(\d+)\s*=\s*([A-Z0-9_]+)\s*\(", re.I)
REF = re.compile(r"#(\d+)")
LIST = re.compile(r"\(((?:\s*#\d+\s*,)*\s*#\d+\s*)\)")


def load(path):
    ents = {}
    for st in statements(path):
        m = HEAD.match(st)
        if m:
            ents[int(m.group(1))] = (m.group(2).upper(), st)
    return ents


def is_product(typ, text):
    """IfcProduct 계열: 7번째 인자가 배치(#..)인 IFC 객체. 관계·형상은 제외 (이름 규칙 + 대표 패턴)."""
    return typ.startswith("IFC") and not typ.startswith("IFCREL") and typ not in SPATIAL and \
        re.match(r"#\d+\s*=\s*\w+\s*\(\s*'[^']*'\s*,\s*(#\d+|\$)\s*,", text) is not None


def is_product_type(text):
    """IfcTypeProduct 계열: ('GUID',#소유이력, ...) 로 시작 (IfcProfileDef 같은 '…TYPE' 이 아닌 값 엔티티와 구분)."""
    return re.match(r"#\d+\s*=\s*\w+\s*\(\s*'[^']{22}'\s*,", text) is not None


def filter_ifc(src, dst):
    ents = load(src)
    # 1) 부재로 쓰이는 id = 공간 포함·집합 관계에 등장하는 것
    member_ids = set()
    for typ, text in ents.values():
        if typ in ("IFCRELCONTAINEDINSPATIALSTRUCTURE", "IFCRELAGGREGATES"):
            member_ids.update(int(x) for x in REF.findall(text))
    drop = {i for i in member_ids if i in ents and ents[i][0] not in KEEP | SPATIAL and is_product(*ents[i])}
    drop |= {i for i, (t, _) in ents.items() if t in ("IFCOPENINGELEMENT", "IFCSPACE", "IFCDOOR", "IFCWINDOW")}
    # 버린 부재의 유형(IfcFurnitureType 등)도 버림 — 재료 관계가 유형을 직접 가리켜 형상(BREP)을 끌고 오기 때문 (실측: 가구 유형이 용량 대부분)
    keep_types = {k + "TYPE" for k in KEEP} | {"IFCWALLTYPE"}
    drop |= {i for i, (t, _) in ents.items() if t.endswith("TYPE") and t.startswith("IFC") and t not in keep_types
             and not t.startswith("IFCREL") and is_product_type(ents[i][1])}
    # 2) 관계: 목록에서 버린 id 를 빼고, 비면 관계도 버림
    keep_text = {}
    for i, (typ, text) in ents.items():
        if i in drop:
            continue
        if typ.startswith("IFCREL"):
            if typ in DROP_RELS:
                continue
            refs = [int(x) for x in REF.findall(text)]
            if any(r in drop for r in refs):
                def strip(m):
                    kept = [x for x in m.group(1).split(",") if int(x.strip()[1:]) not in drop]
                    return "(" + ",".join(kept) + ")" if kept else "()"
                text = LIST.sub(strip, text)
                if "()" in text:        # 관련 대상 목록이 비었음
                    continue
                # 단일 참조 자리(예: RelatingElement)에 버린 부재가 남았으면 관계 버림
                if any(int(x) in drop for x in REF.findall(text)):
                    continue
            keep_text[i] = text
    # 3) 뿌리 = 공간 구조 + KEEP 부재 + 남은 관계 → 참조를 따라 닫힘
    roots = [i for i, (t, _) in ents.items() if (t in SPATIAL or t in KEEP) and i not in drop] + list(keep_text)
    out, stack = set(), roots
    while stack:
        i = stack.pop()
        if i in out or i not in ents or i in drop:
            continue
        out.add(i)
        text = keep_text.get(i, ents[i][1])
        stack.extend(int(x) for x in REF.findall(text))
    # 4) 쓰기: 원본 헤더 + 남은 엔티티 (id 순)
    with open(src, "r", encoding="latin-1", newline="") as f:
        header = f.read(200000).split("DATA;")[0]
    with open(dst, "w", encoding="latin-1", newline="\n") as f:
        f.write(header + "DATA;\n")
        for i in sorted(out):
            f.write(keep_text.get(i, ents[i][1]) + ";\n")
        f.write("ENDSEC;\nEND-ISO-10303-21;\n")
    counts = {}
    for i in out:
        counts[ents[i][0]] = counts.get(ents[i][0], 0) + 1
    return len(ents), len(out), {k: counts.get(k, 0) for k in sorted(KEEP | SPATIAL)}


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("사용법: python -I tools/ifc_filter.py <입력.ifc> <출력.ifc>")
    n_in, n_out, c = filter_ifc(sys.argv[1], sys.argv[2])
    print(f"엔티티 {n_in:,} → {n_out:,}  ({os.path.getsize(sys.argv[2]) / 1e6:.1f} MB)")
    print("  " + ", ".join(f"{k} {v}" for k, v in c.items()))

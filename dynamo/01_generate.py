# 01_Generate — Dynamo "Python Script" 노드에 그대로 붙여 넣는 코드 (Revit 2026 CPython3 / 2027 PythonNet3 공통)
# 입력:
#   IN[0] Boolean  — 실행 여부 (True 일 때만 모델 변경)
#   IN[1] Number   — 동바리 간격 X (m), 예 1.2  [입력] 조립도 값
#   IN[2] Number   — 동바리 간격 Y (m), 예 1.2  [입력]
#   IN[3] String   — 동바리를 배치할 층 (예 "9,10,11,12"). 빈 문자열이면 구역 상자만 만든다
# 하는 일 (모델 변경):
#   1) SHR_GenBy = "01_Generate" 인 일반 모델을 모두 지운다 (재실행 안전)
#   2) 레벨 L01–Lnn 마다, 그리드 X1..·Y1.. 칸마다 구역 상자(DirectShape, 슬래브 두께 210) 생성
#      → SHR_ZoneID("9F-A1"), SHR_Floor(9), SHR_GenBy 기입. A=X1–X2, B=X2–X3 … / 1=Y1–Y2, 2=Y2–Y3
#   3) IN[3] 층마다 SHR_PipeSupport 를 X1·Y1 교점 원점 격자(간격 IN[1]×IN[2])로 배치
#      k층 슬래브를 받치는 동바리 = L(k-1) 위에 서고, 높이 = Lk − 210 mm − L(k-1)
#      → SHR_ZoneID, SHR_Floor, SHR_GenBy 기입
# 출력: 문자열 목록 ["삭제 n개", "구역 n개", "9F 동바리 416본", ..., 경고들] 또는 오류 메시지
# 재실행 안전: 예 (같은 태그 요소를 지우고 새로 만든다). 손으로 고친 구역·동바리도 지워지니 주의.
import clr
clr.AddReference("RevitServices")
from RevitServices.Persistence import DocumentManager
from RevitServices.Transactions import TransactionManager
clr.AddReference("RevitAPI")
from Autodesk.Revit.DB import (BuiltInCategory, BuiltInParameter, Category, CurveLoop, DirectShape,
                               ElementId, FamilySymbol, FilteredElementCollector, GeometryCreationUtilities,
                               GeometryObject, Grid, Level, Line, UnitTypeId, UnitUtils, XYZ)
from Autodesk.Revit.DB.Structure import StructuralType
from System.Collections.Generic import List

GEN_BY = "01_Generate"
FAMILY = "SHR_PipeSupport"
SLAB_T_MM = 210.0          # [가정] 데모 모델 슬래브 두께 (매뉴얼 2-4)


# --- 순수 계산 (Revit 없이 검사 가능) ---
def axis_points(a0, a1, s):
    """a0~a1(mm) 구간에 s 간격으로 놓이는 좌표. 양 끝 포함 (30 m / 1.2 m → 26점)."""
    n = int((a1 - a0) / s + 1e-6)
    return [a0 + i * s for i in range(n + 1)]


def cell_index(v, bounds):
    """v 가 들어가는 칸 번호. 경계 위 점은 다음 칸, 마지막 경계는 마지막 칸."""
    for i in range(len(bounds) - 2):
        if v < bounds[i + 1]:
            return i
    return len(bounds) - 2


def parse_floors(text):
    return sorted(set(int(t) for t in str(text or "").replace(" ", "").split(",") if t))


assert len(axis_points(0, 30000, 1200)) * len(axis_points(0, 18000, 1200)) == 416   # 정의서 §4 층당 416본
assert cell_index(9600, [0, 10000, 20000, 30000]) == 0 and cell_index(30000, [0, 10000, 20000, 30000]) == 2


# --- Revit ---
doc = DocumentManager.Instance.CurrentDBDocument


def mm(v):
    return UnitUtils.ConvertToInternalUnits(v, UnitTypeId.Millimeters)


def to_mm(v):
    return UnitUtils.ConvertFromInternalUnits(v, UnitTypeId.Millimeters)


def set_param(el, name, value):
    p = el.LookupParameter(name)
    if p is None or p.IsReadOnly:
        return False
    p.Set(value)
    return True


def grid_axis(prefix, use_x):
    """이름이 X1, X2 … 인 그리드의 좌표(mm), 번호순."""
    found = {}
    for g in FilteredElementCollector(doc).OfClass(Grid):
        name = g.Name.strip().upper()
        if name.startswith(prefix) and name[len(prefix):].isdigit():
            pt = g.Curve.GetEndPoint(0)
            found[int(name[len(prefix):])] = to_mm(pt.X if use_x else pt.Y)
    return [found[k] for k in sorted(found)]


def floor_levels():
    """{층 번호: Level}  — 이름 L00, L01 … 만."""
    out = {}
    for lv in FilteredElementCollector(doc).OfClass(Level):
        name = lv.Name.strip().upper()
        if name.startswith("L") and name[1:].isdigit():
            out[int(name[1:])] = lv
    return out


def box(x0, y0, x1, y1, z0, h):
    """mm 좌표 → 상자 Solid."""
    p = [XYZ(mm(x0), mm(y0), z0), XYZ(mm(x1), mm(y0), z0), XYZ(mm(x1), mm(y1), z0), XYZ(mm(x0), mm(y1), z0)]
    loop = CurveLoop()
    for i in range(4):
        loop.Append(Line.CreateBound(p[i], p[(i + 1) % 4]))
    loops = List[CurveLoop]()
    loops.Add(loop)
    return GeometryCreationUtilities.CreateExtrusionGeometry(loops, XYZ.BasisZ, h)


def zone_id(floor, ix, iy):
    return "%dF-%s%d" % (floor, chr(65 + ix), iy + 1)


run = IN[0]
sx_mm = float(IN[1] or 0) * 1000.0
sy_mm = float(IN[2] or 0) * 1000.0
shore_floors = parse_floors(IN[3])

xs, ys, levels = grid_axis("X", True), grid_axis("Y", False), floor_levels()
errors = []
if not run:
    errors.append("Run 을 True 로 바꾸고 다시 실행하세요")
if len(xs) < 2 or len(ys) < 2:
    errors.append("그리드 X1,X2… 와 Y1,Y2… 가 각각 2개 이상 있어야 합니다 (찾음: X %d개, Y %d개)" % (len(xs), len(ys)))
if not [k for k in levels if k >= 1]:
    errors.append("이름이 L01, L02 … 인 레벨이 없습니다")
if shore_floors and (sx_mm <= 0 or sy_mm <= 0):
    errors.append("동바리 간격 IN[1]·IN[2] 는 0보다 커야 합니다 (m 단위, 예 1.2)")
symbol = None
if shore_floors:
    symbol = next((s for s in FilteredElementCollector(doc).OfClass(FamilySymbol) if s.Family.Name == FAMILY), None)
    if symbol is None:
        errors.append("패밀리 %s 가 로드되지 않았습니다 (삽입 탭 → 패밀리 로드)" % FAMILY)

if errors:
    OUT = errors
else:
    report, warnings = [], []
    try:
        TransactionManager.Instance.EnsureInTransaction(doc)

        # 1) 이전 실행 결과 삭제
        old = List[ElementId]()
        for el in FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_GenericModel).WhereElementIsNotElementType():
            p = el.LookupParameter("SHR_GenBy")
            if p is not None and p.AsString() == GEN_BY:
                old.Add(el.Id)
        if old.Count:
            doc.Delete(old)
        report.append("삭제 %d개 (이전 실행분)" % old.Count)

        # 2) 구역 상자: 슬래브 윗면 Lk 에서 아래로 210 mm
        gm_cat = Category.GetCategory(doc, BuiltInCategory.OST_GenericModel).Id
        n_zone = 0
        for k in sorted(levels):
            if k < 1:
                continue
            top = levels[k].ProjectElevation           # 내부 원점 기준 (피트)
            for ix in range(len(xs) - 1):
                for iy in range(len(ys) - 1):
                    ds = DirectShape.CreateElement(doc, gm_cat)
                    geo = List[GeometryObject]()
                    geo.Add(box(xs[ix], ys[iy], xs[ix + 1], ys[iy + 1], top - mm(SLAB_T_MM), mm(SLAB_T_MM)))
                    ds.SetShape(geo)
                    ok = set_param(ds, "SHR_ZoneID", zone_id(k, ix, iy))
                    set_param(ds, "SHR_Floor", k)
                    set_param(ds, "SHR_GenBy", GEN_BY)
                    if not ok and not warnings:
                        warnings.append("SHR_ZoneID 를 쓸 수 없습니다 → 00_Setup 을 먼저 실행하세요")
                    n_zone += 1
        report.append("구역 %d개 (%d개층 × %d칸)" % (n_zone, len([k for k in levels if k >= 1]),
                                               (len(xs) - 1) * (len(ys) - 1)))

        # 3) 동바리 격자 — 원점 = X1·Y1 교점
        if symbol is not None and not symbol.IsActive:
            symbol.Activate()
            doc.Regenerate()
        px, py = axis_points(xs[0], xs[-1], sx_mm), axis_points(ys[0], ys[-1], sy_mm)
        for k in shore_floors:
            if k not in levels or (k - 1) not in levels:
                warnings.append("%dF: 레벨 L%02d 또는 L%02d 가 없어 건너뜀" % (k, k, k - 1))
                continue
            base = levels[k - 1]
            height = levels[k].ProjectElevation - mm(SLAB_T_MM) - base.ProjectElevation
            n = 0
            for x in px:
                for y in py:
                    fi = doc.Create.NewFamilyInstance(XYZ(mm(x), mm(y), base.ProjectElevation), symbol, base,
                                                      StructuralType.NonStructural)
                    # Revit 2026은 위치 Z를 레벨 기준 간격띄우기로 해석함(한 층 위에 섬) → 0 으로 되돌림. MCP 사본에서 확인
                    for bip in (BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM, BuiltInParameter.INSTANCE_ELEVATION_PARAM):
                        p = fi.get_Parameter(bip)
                        if p is not None and not p.IsReadOnly:
                            p.Set(0.0)
                    if not set_param(fi, "높이", height) and n == 0:
                        warnings.append("%dF: 패밀리에 인스턴스 매개변수 '높이' 가 없어 높이를 못 바꿈" % k)
                    set_param(fi, "SHR_ZoneID", zone_id(k, cell_index(x, xs), cell_index(y, ys)))
                    set_param(fi, "SHR_Floor", k)
                    set_param(fi, "SHR_GenBy", GEN_BY)
                    n += 1
            report.append("%dF 동바리 %d본 (높이 %.0f mm, L%02d 위)" % (k, n, to_mm(height), k - 1))

        TransactionManager.Instance.TransactionTaskDone()
        OUT = report + warnings
    except Exception as e:
        TransactionManager.Instance.ForceCloseTransaction()   # 열린 트랜잭션 정리. 일부 생성분이 남아도 재실행 때 태그로 지워짐
        OUT = ["오류: " + str(e)] + report

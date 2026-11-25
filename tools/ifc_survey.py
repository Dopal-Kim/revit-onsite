# ifc_survey.py — IFC(STEP, IFC2X3/IFC4) 모델 조사 도구. 표준 라이브러리만, Revit 없이 실행.
# 사용:  python -I tools\ifc_survey.py <model.ifc> [--json out.json] [--spacing 1.2] [--verify]
#        python -I tools\ifc_survey.py --selftest
# 하는 일: 파일을 ';' 문 단위로 스트리밍 분해(따옴표 안 ';' 무시, 여러 줄 문 지원) → 단위·프로젝트·층·
#   슬래브(두께·재료·평면 범위)·벽·기둥·보·계단·창호·그리드·기둥 좌표 군집·동바리 격자 본수 추정을 JSON으로.
# 읽기 전용: 입력 파일을 바꾸지 않는다. 결과 JSON 은 --json 경로에만 쓴다.
import json
import math
import re
import sys
from collections import Counter, defaultdict
from functools import lru_cache

# ---------------- STEP 파서 ----------------
STMT_SPLIT = re.compile(r"(?:'[^']*'|[^';])*;")
STMT = re.compile(r"\s*#(\d+)\s*=\s*([A-Za-z0-9_]+)\s*\((.*)\)\s*$", re.S)
TOK = re.compile(r"'((?:[^']|'')*)'|#(\d+)|\.([A-Za-z0-9_]+)\.|([A-Za-z][A-Za-z0-9_]*)\s*\(|"
                 r"([+-]?\d+(?:\.\d*)?(?:[Ee][+-]?\d+)?)|(\$)|(\*)|(\()|(\))")
COMMENT = re.compile(r"/\*.*?\*/", re.S)


class Ref(int):
    pass


class Enum(str):
    pass


def decode(s):
    """STEP 문자열 → 유니코드. '' , \\X2\\…\\X0\\ (UTF-16 hex), \\X\\hh (ISO-8859-1), \\S\\c 처리."""
    s = s.replace("''", "'")
    if "\\" not in s:
        return s
    s = re.sub(r"\\X2\\((?:[0-9A-Fa-f]{4})+)\\X0\\",
               lambda m: bytes.fromhex(m.group(1)).decode("utf-16-be", "replace"), s)
    s = re.sub(r"\\X4\\((?:[0-9A-Fa-f]{8})+)\\X0\\",
               lambda m: bytes.fromhex(m.group(1)).decode("utf-32-be", "replace"), s)
    s = re.sub(r"\\X\\([0-9A-Fa-f]{2})", lambda m: chr(int(m.group(1), 16)), s)
    s = re.sub(r"\\S\\(.)", lambda m: chr(ord(m.group(1)) + 128), s)
    return s.replace("\\\\", "\\")


def statements(path):
    """DATA 절의 문을 하나씩 낸다. 여러 줄에 걸친 문, 한 줄에 여러 문, 따옴표 안 ';' 를 처리."""
    buf, in_data = "", False
    with open(path, "r", encoding="latin-1", newline="") as f:
        for line in f:
            if not in_data:
                if line.strip().upper() == "DATA;":
                    in_data = True
                continue
            buf += line
            if ";" not in line or buf.count("'") % 2:      # 문이 아직 안 끝남 / 문자열이 열려 있음
                continue
            if "/*" in buf:
                buf = COMMENT.sub("", buf)
            end = 0
            while True:                                    # 앞에서부터 이어서만 자른다 (search 는 문자열 중간 ';' 에서 잘림)
                m = STMT_SPLIT.match(buf, end)
                if not m:
                    break
                st = m.group(0)[:-1].strip()
                end = m.end()
                if st.upper() == "ENDSEC":
                    return
                if st:
                    yield st
            buf = buf[end:]


def parse_args(s):
    stack = [[]]
    for m in TOK.finditer(s):
        g = m.lastindex
        if g == 1:
            stack[-1].append(decode(m.group(1)))
        elif g == 2:
            stack[-1].append(Ref(m.group(2)))
        elif g == 3:
            stack[-1].append(Enum(m.group(3).upper()))
        elif g == 4:                                  # 형식 값 IFCLABEL('x') → ("IFCLABEL", [..])
            new = []
            stack[-1].append((m.group(4).upper(), new))
            stack.append(new)
        elif g == 5:
            stack[-1].append(float(m.group(5)))
        elif g == 6:
            stack[-1].append(None)
        elif g == 7:
            stack[-1].append("*")
        elif g == 8:
            new = []
            stack[-1].append(new)
            stack.append(new)
        elif g == 9 and len(stack) > 1:
            stack.pop()
    return stack[0]


class Model:
    def __init__(self, path):
        self.raw = {}                      # id -> (TYPE, argstr) ; 파싱은 필요할 때만
        self.by_type = defaultdict(list)
        self.n_statements = 0
        for st in statements(path):
            self.n_statements += 1
            m = STMT.match(st)
            if not m:
                continue
            i, t = int(m.group(1)), m.group(2).upper()
            self.raw[i] = (t, m.group(3))
            self.by_type[t].append(i)
        self.a = lru_cache(maxsize=None)(self._args)

    def _args(self, i):
        return parse_args(self.raw[i][1])

    def t(self, i):
        r = self.raw.get(i)
        return r[0] if r else None

    def ids(self, *types):
        out = []
        for t in types:
            out += self.by_type.get(t, [])
        return out


def val(x):
    """형식 값 (IFCLABEL, ['x']) → 'x'."""
    while isinstance(x, tuple):
        x = x[1][0] if x[1] else None
    return x


# ---------------- 좌표 변환 ----------------
def norm(v):
    l = math.sqrt(sum(c * c for c in v)) or 1.0
    return tuple(c / l for c in v)


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


IDENT = ((0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0))   # (원점, x축, y축, z축)


def apply(T, p):
    o, x, y, z = T
    p = tuple(p) + (0.0,) * (3 - len(p))
    return tuple(o[k] + x[k] * p[0] + y[k] * p[1] + z[k] * p[2] for k in range(3))


def rot(T, v):
    return apply((IDENT[0],) + T[1:], v)


def compose(A, B):
    """A∘B : B 좌표계를 A 안에 놓는다."""
    return (apply(A, B[0]), rot(A, B[1]), rot(A, B[2]), rot(A, B[3]))


class Geo:
    def __init__(self, m):
        self.m = m
        self.world = lru_cache(maxsize=None)(self._world)

    def pt(self, i):
        c = self.m.a(i)[0]
        return tuple(float(v) for v in c)

    def dirv(self, i, default):
        if i is None:
            return default
        return norm(tuple(float(v) for v in self.m.a(i)[0]) + (0.0,) * (3 - len(self.m.a(i)[0])))

    def axis2(self, i):
        """IfcAxis2Placement3D/2D → 변환."""
        if i is None:
            return IDENT
        t, a = self.m.t(i), self.m.a(i)
        o = self.pt(a[0]) + (0.0,) * (3 - len(self.pt(a[0])))
        if t == "IFCAXIS2PLACEMENT2D":
            x = self.dirv(a[1], (1.0, 0.0, 0.0))
            z = (0.0, 0.0, 1.0)
        else:
            z = self.dirv(a[1], (0.0, 0.0, 1.0))
            x = self.dirv(a[2], (1.0, 0.0, 0.0))
        d = sum(x[k] * z[k] for k in range(3))
        x = norm(tuple(x[k] - d * z[k] for k in range(3)))
        return (o, x, cross(z, x), z)

    def _world(self, lp):
        """IfcLocalPlacement 사슬 → 세계 변환."""
        if lp is None or self.m.t(lp) != "IFCLOCALPLACEMENT":
            return IDENT
        a = self.m.a(lp)
        parent = self.world(a[0]) if a[0] is not None else IDENT
        return compose(parent, self.axis2(a[1]))

    # --- 2D 곡선/단면 → 점 목록 (단면 좌표계) ---
    def curve_pts(self, i):
        t, a = self.m.t(i), self.m.a(i)
        if t == "IFCPOLYLINE":
            return [self.pt(p)[:2] for p in a[0]]
        if t == "IFCCOMPOSITECURVE":
            out = []
            for seg in a[0]:
                out += self.curve_pts(self.m.a(seg)[2])
            return out
        if t == "IFCTRIMMEDCURVE":
            return self.trimmed_pts(a)
        if t == "IFCCIRCLE":
            T = self.axis2(a[0])
            r = float(a[1])
            return [apply(T, (r * math.cos(k * math.pi / 8), r * math.sin(k * math.pi / 8)))[:2] for k in range(16)]
        return []

    def trimmed_pts(self, a):
        basis, tr1, tr2, sense = a[0], a[1], a[2], a[3]
        pts = [self.pt(x)[:2] for x in list(tr1) + list(tr2) if isinstance(x, Ref)]
        if self.m.t(basis) == "IFCCIRCLE":
            par = [float(val(x)) for x in list(tr1) + list(tr2) if isinstance(x, tuple) and x[0] == "IFCPARAMETERVALUE"]
            if len(par) == 2:
                ca = self.m.a(basis)
                T, r = self.axis2(ca[0]), float(ca[1])
                t1, t2 = math.radians(par[0] * DEG), math.radians(par[1] * DEG)
                if sense == "T":
                    t2 += 2 * math.pi if t2 < t1 else 0.0
                else:
                    t2 -= 2 * math.pi if t2 > t1 else 0.0
                return [apply(T, (r * math.cos(t1 + (t2 - t1) * k / 8), r * math.sin(t1 + (t2 - t1) * k / 8)))[:2]
                        for k in range(9)]
        if not pts:
            return self.curve_pts(basis)
        return pts

    def profile(self, i):
        """단면 → (외곽 점, [내부 구멍 점 목록])."""
        t, a = self.m.t(i), self.m.a(i)
        if t == "IFCRECTANGLEPROFILEDEF" or t == "IFCISHAPEPROFILEDEF" or t.endswith("SHAPEPROFILEDEF"):
            T = self.axis2(a[2])
            hx, hy = float(a[3]) / 2, float(a[4]) / 2
            return [apply(T, c)[:2] for c in ((-hx, -hy), (hx, -hy), (hx, hy), (-hx, hy))], []
        if t == "IFCCIRCLEPROFILEDEF":
            T, r = self.axis2(a[2]), float(a[3])
            return [apply(T, (r * math.cos(k * math.pi / 8), r * math.sin(k * math.pi / 8)))[:2] for k in range(16)], []
        if t == "IFCARBITRARYCLOSEDPROFILEDEF":
            return self.curve_pts(a[2]), []
        if t == "IFCARBITRARYPROFILEDEFWITHVOIDS":
            return self.curve_pts(a[2]), [self.curve_pts(c) for c in a[3]]
        return [], []

    # --- 형상 항목 → 세계 좌표 점들 + (수직 돌출이면) 평면 다각형 ---
    def item(self, i, T, acc):
        t, a = self.m.t(i), self.m.a(i)
        if t == "IFCEXTRUDEDAREASOLID":
            T2 = compose(T, self.axis2(a[1]))
            outer, holes = self.profile(a[0])
            d = self.dirv(a[2], (0.0, 0.0, 1.0))
            depth = float(a[3])
            ext = tuple(c * depth for c in d)
            for p in outer:
                acc["pts"].append(apply(T2, p))
                acc["pts"].append(apply(T2, (p[0] + ext[0], p[1] + ext[1], ext[2])))
            wd = rot(T2, d)
            acc["depths"].append(depth)
            if abs(abs(wd[2]) - 1.0) < 1e-3 and outer:      # 수직 돌출 → 평면 다각형 유효
                acc["polys"].append(([apply(T2, p)[:2] for p in outer], [[apply(T2, p)[:2] for p in h] for h in holes]))
            else:
                acc["nonvertical"] += 1
        elif t in ("IFCBOOLEANCLIPPINGRESULT", "IFCBOOLEANRESULT"):
            if a[0] == "DIFFERENCE" or t == "IFCBOOLEANCLIPPINGRESULT":
                self.item(a[1], T, acc)              # 잘린 결과 ⊂ 첫 피연산자 → 범위는 상한값
                acc["clipped"] += 1
            else:
                self.item(a[1], T, acc)
                self.item(a[2], T, acc)
        elif t == "IFCMAPPEDITEM":
            rm = self.m.a(a[0])
            T2 = compose(compose(T, self.transform_op(a[1])), self.axis2(rm[0]))
            for it in self.m.a(rm[1])[3]:
                self.item(it, T2, acc)
        elif t in ("IFCFACETEDBREP", "IFCFACETEDBREPWITHVOIDS"):
            self.faces(self.m.a(a[0])[0], T, acc)
            acc["brep"] += 1
        elif t in ("IFCFACEBASEDSURFACEMODEL", "IFCSHELLBASEDSURFACEMODEL"):
            for fs in a[0]:
                self.faces(self.m.a(fs)[0], T, acc)
            acc["brep"] += 1
        else:
            acc["other"][t] += 1

    def faces(self, face_ids, T, acc):
        for f in face_ids:
            for b in self.m.a(f)[0]:
                loop = self.m.a(b)[0]
                if self.m.t(loop) == "IFCPOLYLOOP":
                    for p in self.m.a(loop)[0]:
                        acc["pts"].append(apply(T, self.pt(p)))

    def transform_op(self, i):
        if i is None or self.m.t(i) != "IFCCARTESIANTRANSFORMATIONOPERATOR3D":
            return IDENT
        a = self.m.a(i)          # (Axis1, Axis2, LocalOrigin, Scale, Axis3)
        s = float(a[3]) if isinstance(a[3], float) else 1.0
        x = self.dirv(a[0], (1.0, 0.0, 0.0))
        y = self.dirv(a[1], (0.0, 1.0, 0.0))
        z = self.dirv(a[4] if len(a) > 4 else None, (0.0, 0.0, 1.0))
        return (self.pt(a[2]), tuple(c * s for c in x), tuple(c * s for c in y), tuple(c * s for c in z))

    def product(self, i, rep_ids=("Body",)):
        """IfcProduct → 세계 좌표 형상 요약."""
        a = self.m.a(i)
        T = self.world(a[5])
        acc = {"pts": [], "polys": [], "depths": [], "nonvertical": 0, "clipped": 0, "brep": 0, "other": Counter()}
        if a[6] is not None:
            for rep in self.m.a(a[6])[2]:
                ra = self.m.a(rep)
                if ra[1] in rep_ids:
                    for it in ra[3]:
                        self.item(it, T, acc)
        acc["origin"] = T[0]
        return acc


MIN_COLS_PER_LINE = 4   # shortcut: 구역 절단선 = 기둥 4개 이상 늘어선 열(이 모델 기준 경험값), 다른 모델에선 조정
DEG = 1.0      # 평면각 단위 → 도. 파일 단위가 라디안이면 main 에서 180/pi 로 바꿈


# ---------------- 다각형 ----------------
def area(poly):
    return abs(sum(poly[k][0] * poly[k - 1][1] - poly[k - 1][0] * poly[k][1] for k in range(len(poly)))) / 2.0


def inside(x, y, poly):
    c = False
    j = len(poly) - 1
    for k in range(len(poly)):
        xi, yi = poly[k]
        xj, yj = poly[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            c = not c
        j = k
    return c


def bbox(pts):
    if not pts:
        return None
    xs, ys, zs = [p[0] for p in pts], [p[1] for p in pts], [p[2] for p in pts]
    return [min(xs), min(ys), min(zs), max(xs), max(ys), max(zs)]


def cluster(values, tol):
    """1차원 좌표 군집 → [(중심, 개수)] (좌표순)."""
    out = []
    for v in sorted(values):
        if out and v - out[-1][2] <= tol:
            c = out[-1]
            c[0] += v
            c[1] += 1
            c[2] = v
        else:
            out.append([v, 1, v])
    return [(c[0] / c[1], c[1]) for c in out]


# ---------------- 조사 ----------------
CONCRETE = re.compile(r"콘크리트|concrete|\bRC\b|철근", re.I)
TARGETS = ["IFCSLAB", "IFCWALL", "IFCWALLSTANDARDCASE", "IFCCOLUMN", "IFCBEAM", "IFCSTAIR", "IFCSTAIRFLIGHT",
           "IFCRAMP", "IFCRAMPFLIGHT", "IFCDOOR", "IFCWINDOW", "IFCRAILING", "IFCROOF", "IFCCURTAINWALL", "IFCPLATE",
           "IFCMEMBER", "IFCCOVERING", "IFCFOOTING", "IFCPILE", "IFCBUILDINGELEMENTPROXY", "IFCSPACE",
           "IFCFURNISHINGELEMENT", "IFCFLOWTERMINAL", "IFCOPENINGELEMENT"]


def survey(path, spacing_m=1.2):
    global DEG
    m = Model(path)
    g = Geo(m)
    R = {"file": path, "statements": m.n_statements, "entities": len(m.raw),
         "type_counts": {t: len(v) for t, v in sorted(m.by_type.items(), key=lambda kv: -len(kv[1]))}}

    # 단위
    units, length_scale = [], 1.0
    for ua in m.ids("IFCUNITASSIGNMENT"):
        for u in m.a(ua)[0]:
            t, a = m.t(u), m.a(u)
            if t == "IFCSIUNIT":
                units.append({"type": a[1], "prefix": a[2], "name": a[3]})
                if a[1] == "LENGTHUNIT":
                    length_scale = {"MILLI": 0.001, "CENTI": 0.01, "DECI": 0.1, "KILO": 1000.0}.get(a[2], 1.0)
                if a[1] == "PLANEANGLEUNIT" and a[3] == "RADIAN":
                    DEG = 180.0 / math.pi
            elif t == "IFCCONVERSIONBASEDUNIT":
                mw = m.a(a[3])
                units.append({"type": a[1], "name": a[2], "factor": val(mw[0]), "of": m.a(mw[1])[3]})
                if a[1] == "PLANEANGLEUNIT":
                    DEG = 1.0 if "DEG" in str(a[2]).upper() else 180.0 / math.pi
            else:
                units.append({"type": t, "kind": a[1] if len(a) > 1 else None})
    R["units"] = {"assigned": units, "length_to_m": length_scale, "angle_parameters_in_degrees": DEG == 1.0}
    mm = length_scale * 1000.0              # 파일 길이 → mm

    # 프로젝트·대지·건물
    def names(t, k_long):
        out = []
        for i in m.ids(t):
            a = m.a(i)
            out.append({"id": i, "name": a[2], "description": a[3], "long_name": a[k_long] if k_long < len(a) else None})
        return out
    R["project"] = [dict(x, phase=m.a(x["id"])[6]) for x in names("IFCPROJECT", 5)]
    sites = []
    for x in names("IFCSITE", 7):
        a = m.a(x["id"])
        sites.append(dict(x, ref_lat=a[9], ref_lon=a[10], ref_elev=a[11], placement_world=g.world(a[5])[0]))
    R["site"] = sites
    R["building"] = [dict(x, placement_world=g.world(m.a(x["id"])[5])[0]) for x in names("IFCBUILDING", 7)]
    ctx = []
    for c in m.ids("IFCGEOMETRICREPRESENTATIONCONTEXT"):
        a = m.a(c)
        tn = g.dirv(a[5], None) if len(a) > 5 and a[5] is not None else None
        ctx.append({"id": c, "type": a[1], "true_north": tn})
    R["contexts"] = ctx

    # 층
    storeys = []
    for i in m.ids("IFCBUILDINGSTOREY"):
        a = m.a(i)
        storeys.append({"id": i, "name": a[2], "long_name": a[7], "elevation_mm": (a[9] or 0.0) * mm,
                        "placement_z_mm": g.world(a[5])[0][2] * mm})
    storeys.sort(key=lambda s: s["elevation_mm"])
    for k, s in enumerate(storeys):
        s["order"] = k
        s["height_to_next_mm"] = round(storeys[k + 1]["elevation_mm"] - s["elevation_mm"], 1) if k + 1 < len(storeys) else None
    sid = {s["id"]: s for s in storeys}

    # 관계: 포함, 집합, 타입, 재료, 속성
    container, parent = {}, {}
    for r in m.ids("IFCRELCONTAINEDINSPATIALSTRUCTURE"):
        a = m.a(r)
        for e in a[4]:
            container[e] = a[5]
    for r in m.ids("IFCRELAGGREGATES"):
        a = m.a(r)
        for e in a[5]:
            parent[e] = a[4]

    def storey_of(e):
        seen = 0
        while e is not None and seen < 20:
            if e in sid:
                return e
            e = container.get(e, parent.get(e))
            seen += 1
        return None

    target_ids = set(m.ids(*TARGETS))
    type_of = {}
    for r in m.ids("IFCRELDEFINESBYTYPE"):
        a = m.a(r)
        for e in a[4]:
            if e in target_ids:
                type_of[e] = a[5]

    def mat_desc(mid):
        t, a = m.t(mid), m.a(mid)
        if t == "IFCMATERIAL":
            return {"names": [a[0]], "layers_mm": None}
        if t == "IFCMATERIALLIST":
            return {"names": [m.a(x)[0] for x in a[0]], "layers_mm": None}
        if t == "IFCMATERIALLAYERSETUSAGE":
            d = mat_desc(a[0])
            d["usage"] = {"dir": a[1], "sense": a[2], "offset_mm": float(a[3]) * mm}
            return d
        if t == "IFCMATERIALLAYERSET":
            lay = [m.a(x) for x in a[0]]
            return {"names": [m.a(l[0])[0] if l[0] is not None else None for l in lay], "set_name": a[1] if len(a) > 1 else None,
                    "layers_mm": [round(float(l[1]) * mm, 1) for l in lay]}
        if t == "IFCMATERIALLAYER":
            return {"names": [m.a(a[0])[0] if a[0] is not None else None], "layers_mm": [round(float(a[1]) * mm, 1)]}
        return {"names": [t], "layers_mm": None}

    material = {}
    for r in m.ids("IFCRELASSOCIATESMATERIAL"):
        a = m.a(r)
        for e in a[4]:
            if e in target_ids:
                material[e] = mat_desc(a[5])

    props, quants = defaultdict(dict), defaultdict(dict)
    want_ps = re.compile(r"Common$|^BaseQuantities$|^Qto_", re.I)
    for r in m.ids("IFCRELDEFINESBYPROPERTIES"):
        a = m.a(r)
        objs = [e for e in a[4] if e in target_ids]
        if not objs:
            continue
        ps, pt = a[5], m.t(a[5])
        pa = m.a(ps)
        if not want_ps.search(str(pa[2] or "")):
            continue
        if pt == "IFCPROPERTYSET":
            d = {}
            for p in pa[4]:
                if m.t(p) == "IFCPROPERTYSINGLEVALUE":
                    q = m.a(p)
                    d[q[0]] = val(q[2])
            for e in objs:
                props[e][pa[2]] = d
        elif pt == "IFCELEMENTQUANTITY":
            d = {}
            for q in pa[5]:
                qa = m.a(q)
                v = qa[3]
                if m.t(q) == "IFCQUANTITYLENGTH":
                    v = float(v) * mm
                d[qa[0]] = v
            for e in objs:
                quants[e].update(d)

    def is_concrete(e):
        names_ = " ".join(str(x) for x in (material.get(e) or {}).get("names", []) if x)
        return bool(CONCRETE.search(names_ + " " + str(m.a(e)[2]) + " " + str(m.a(e)[4])))

    def load_bearing(e):
        for k, d in props.get(e, {}).items():
            if "LoadBearing" in d:
                return d["LoadBearing"]
        return None

    def tname(e):
        return m.a(type_of[e])[2] if e in type_of else None

    # 층별 개수
    per = defaultdict(lambda: Counter())
    unassigned = Counter()
    for t in TARGETS:
        for e in m.ids(t):
            s = storey_of(e)
            if s is None:
                unassigned[t] += 1
            else:
                per[s][t] += 1
    for s in storeys:
        s["counts"] = dict(per[s["id"]])
    R["unassigned_to_storey"] = dict(unassigned)

    def core_layer(md):
        """가장 두꺼운 재료층 (구조체 후보) → [재료명, 두께 mm, 콘크리트 여부]. 층 정보가 없으면 None."""
        if not md.get("layers_mm"):
            return None
        k = max(range(len(md["layers_mm"])), key=lambda j: md["layers_mm"][j])
        nm = str(md["names"][k])
        return [nm, md["layers_mm"][k], bool(CONCRETE.search(nm))]

    # 벽: 층별 콘크리트 여부·하중 지지·두께
    walls = []
    for e in m.ids("IFCWALL", "IFCWALLSTANDARDCASE"):
        md = material.get(e) or {}
        cl = core_layer(md)                         # 마감벽("콘크리트면정리" 등) 오탐 방지: 가장 두꺼운 층 기준
        walls.append({"storey": storey_of(e), "concrete": cl[2] if cl else is_concrete(e), "load_bearing": load_bearing(e),
                      "core_mm": cl[1] if cl else None,
                      "thickness_mm": round(sum(md["layers_mm"]), 1) if md.get("layers_mm") else None,
                      "type": tname(e) or m.a(e)[4], "materials": md.get("names")})
    for s in storeys:
        ws = [w for w in walls if w["storey"] == s["id"]]
        s["walls"] = {"total": len(ws), "concrete": sum(w["concrete"] for w in ws),
                      "load_bearing_true": sum(1 for w in ws if w["load_bearing"] in ("T", True)),
                      "concrete_thickness_mm": dict(Counter(w["thickness_mm"] for w in ws if w["concrete"]).most_common(6))}
    R["wall_types_top"] = Counter("%s | conc=%s" % (w["type"], w["concrete"]) for w in walls).most_common(25)
    R["wall_materials_top"] = Counter(" / ".join(str(x) for x in (w["materials"] or ["(없음)"])) for w in walls).most_common(15)

    # 기둥·보 재료
    for cls, key in (("IFCCOLUMN", "columns"), ("IFCBEAM", "beams")):
        lst = [(storey_of(e), is_concrete(e), tname(e) or m.a(e)[4]) for e in m.ids(cls)]
        R[key + "_types_top"] = Counter("%s | conc=%s" % (x[2], x[1]) for x in lst).most_common(15)
        for s in storeys:
            sel = [x for x in lst if x[0] == s["id"]]
            s[key] = {"total": len(sel), "concrete": sum(x[1] for x in sel)}

    # 슬래브 상세 + 형상
    slabs = []
    opening_of = defaultdict(list)
    for r in m.ids("IFCRELVOIDSELEMENT"):
        a = m.a(r)
        opening_of[a[4]].append(a[5])
    geo_stats = Counter()
    for e in m.ids("IFCSLAB"):
        a = m.a(e)
        md = material.get(e) or {}
        G = g.product(e)
        bb = bbox(G["pts"])
        polys = G["polys"]
        holes = [h for _, hs in polys for h in hs]
        for op in opening_of.get(e, []):
            OG = g.product(op)
            holes += [o for o, _ in OG["polys"]]
        a_out = sum(area(o) for o, _ in polys)
        a_net = a_out - sum(area(h) for h in holes)
        geo_stats["brep" if G["brep"] else ("extrusion" if polys else "other")] += 1
        if G["clipped"]:
            geo_stats["clipped"] += 1
        thk_q = None
        for k in ("Width", "Depth", "Thickness", "NominalThickness"):
            if k in quants.get(e, {}):
                thk_q = round(quants[e][k], 1)
                break
        thk_name = re.search(r"THK\s*(\d+)|(\d{2,4})\s*mm", str(a[2]), re.I)
        st = storey_of(e)
        slabs.append({
            "id": e, "guid": a[0], "name": a[2], "type": tname(e), "object_type": a[4], "predefined": a[8] if len(a) > 8 else None,
            "storey": sid[st]["name"] if st else None, "storey_id": st, "parent": m.t(parent[e]) if e in parent else None,
            "materials": md.get("names"), "layers_mm": md.get("layers_mm"),
            "thk_layers_mm": round(sum(md["layers_mm"]), 1) if md.get("layers_mm") else None,
            "thk_quantity_mm": thk_q,
            "thk_extrusion_mm": round(max(G["depths"]) * mm, 1) if G["depths"] else None,
            "thk_bbox_mm": round((bb[5] - bb[2]) * mm, 1) if bb else None,
            "thk_name_mm": float(thk_name.group(1) or thk_name.group(2)) if thk_name else None,
            "concrete": is_concrete(e), "load_bearing": load_bearing(e),
            "core_layer": core_layer(md),
            "bbox_mm": [round(v * mm) for v in bb] if bb else None,
            "area_m2_profile": round(a_net * (length_scale ** 2), 2) if polys else None,
            "area_m2_quantity": quants.get(e, {}).get("GrossArea"),
            "geometry": "brep" if G["brep"] else ("extrusion" if polys else "other"),
            "clipped": bool(G["clipped"]), "n_openings": len(opening_of.get(e, [])),
            "_polys": [o for o, _ in polys], "_holes": holes,
        })
    R["slab_geometry_methods"] = dict(geo_stats)

    def slab_thk(s):
        return s["thk_layers_mm"] or s["thk_quantity_mm"] or s["thk_extrusion_mm"] or s["thk_bbox_mm"]

    # 구조 바닥 슬래브 후보 판정 (이름 비의존): FLOOR/ROOF 형식 + 콘크리트 + 두께 ≥ 120 mm + 상단이 층 고도 ± 300 mm
    elev = [(s["elevation_mm"], s) for s in storeys]
    for s in slabs:
        top = s["bbox_mm"][5] if s["bbox_mm"] else None
        near = min(elev, key=lambda es: abs(es[0] - top)) if top is not None else None
        s["nearest_storey_by_top"] = near[1]["name"] if near else None
        s["top_minus_storey_elev_mm"] = round(top - sid[s["storey_id"]]["elevation_mm"]) if (top is not None and s["storey_id"]) else None
        # 재료층이 있으면 가장 두꺼운 층이 콘크리트이고 ≥120 mm (마감 복합층·무근 토핑 배제), 없으면 전체 두께
        cl = s["core_layer"]
        conc, th = (cl[2], cl[1]) if cl else (s["concrete"], slab_thk(s) or 0)
        s["structural_candidate"] = bool(s["predefined"] in ("FLOOR", "ROOF") and conc and th >= 120
                                         and s["top_minus_storey_elev_mm"] is not None and abs(s["top_minus_storey_elev_mm"]) <= 300)

    # 층별 슬래브 요약 + 범위 + 동바리 격자 본수
    step = spacing_m / length_scale
    for st in storeys:
        ss = [s for s in slabs if s["storey_id"] == st["id"]]
        st["slabs"] = {
            "total": len(ss), "by_predefined": dict(Counter(str(s["predefined"]) for s in ss)),
            "thickness_mm": dict(Counter(slab_thk(s) for s in ss).most_common()),
            "types": dict(Counter(s["type"] or s["object_type"] for s in ss).most_common(12)),
            "structural_candidates": sum(s["structural_candidate"] for s in ss),
        }
        cand = [s for s in ss if s["structural_candidate"]]
        bbs = [s["bbox_mm"] for s in cand if s["bbox_mm"]]
        if bbs:
            ext = [min(b[0] for b in bbs), min(b[1] for b in bbs), max(b[3] for b in bbs), max(b[4] for b in bbs)]
            st["structural_extent_mm"] = ext
            st["structural_extent_size_m"] = [round((ext[2] - ext[0]) / 1000, 2), round((ext[3] - ext[1]) / 1000, 2)]
            st["structural_area_m2_profile"] = round(sum(s["area_m2_profile"] or 0 for s in cand), 1)
            polys = [p for s in cand for p in s["_polys"]]
            holes = [h for s in cand for h in s["_holes"]]
            x0, y0 = ext[0] / mm + step / 2, ext[1] / mm + step / 2
            pts = []
            x = x0
            while x < ext[2] / mm:
                y = y0
                while y < ext[3] / mm:
                    if any(inside(x, y, p) for p in polys) and not any(inside(x, y, h) for h in holes):
                        pts.append((x * mm, y * mm))
                    y += step
                x += step
            st["shore_points_%gm" % spacing_m] = len(pts)
            st["_shore_pts"] = pts
        ab = [s["bbox_mm"] for s in ss if s["bbox_mm"]]
        if ab:
            st["all_slab_extent_mm"] = [min(b[0] for b in ab), min(b[1] for b in ab), max(b[3] for b in ab), max(b[4] for b in ab)]
    R["slab_types_top"] = Counter("%s | %s | thk=%s | conc=%s" % (s["predefined"], s["type"] or s["object_type"], slab_thk(s), s["concrete"])
                                  for s in slabs).most_common(40)
    R["thickness_source_agreement"] = {
        "layers_vs_extrusion_equal": sum(1 for s in slabs if s["thk_layers_mm"] and s["thk_extrusion_mm"] and abs(s["thk_layers_mm"] - s["thk_extrusion_mm"]) < 1),
        "layers_vs_extrusion_differ": sum(1 for s in slabs if s["thk_layers_mm"] and s["thk_extrusion_mm"] and abs(s["thk_layers_mm"] - s["thk_extrusion_mm"]) >= 1),
        "has_layers": sum(1 for s in slabs if s["thk_layers_mm"]),
        "has_quantity": sum(1 for s in slabs if s["thk_quantity_mm"]),
        "has_extrusion": sum(1 for s in slabs if s["thk_extrusion_mm"]),
    }
    R["slab_storey_mismatch_by_top"] = sum(1 for s in slabs if s["storey"] and s["nearest_storey_by_top"] != s["storey"])
    for s in slabs:
        s.pop("_polys")
        s.pop("_holes")
    R["slabs"] = slabs

    # 그리드
    grids = []
    for gi in m.ids("IFCGRID"):
        a = m.a(gi)
        axes = {k: [m.a(x)[0] for x in (a[7 + j] or [])] for j, k in enumerate(("U", "V", "W")) if 7 + j < len(a)}
        grids.append({"id": gi, "name": a[2], "storey": sid.get(storey_of(gi), {}).get("name"), "axes": axes})
    R["grids"] = grids
    R["grid_axis_count"] = len(m.ids("IFCGRIDAXIS"))

    # 기둥 위치 군집 (그리드 대용): 기둥 Body 형상 범위 중심의 세계 XY, 100 mm 허용
    # (Revit 2017 내보내기는 기둥 배치 원점을 건물 원점에 두고 형상을 띄우는 경우가 있어 배치 원점은 못 씀)
    cols = []
    for e in m.ids("IFCCOLUMN"):
        b = bbox(g.product(e)["pts"]) or list(g.world(m.a(e)[5])[0]) * 2
        cols.append((storey_of(e), (b[0] + b[3]) / 2 * mm, (b[1] + b[4]) / 2 * mm, is_concrete(e)))
    R["column_clusters"] = {}
    for s in storeys:
        cs = [c for c in cols if c[0] == s["id"] and c[3]]       # 콘크리트 기둥만 (철골 H형강 제외)
        if not cs:
            continue
        cx = [(round(v), n) for v, n in cluster([c[1] for c in cs], 100)]
        cy = [(round(v), n) for v, n in cluster([c[2] for c in cs], 100)]
        R["column_clusters"][s["name"]] = {"n": len(cs), "x_lines": cx, "y_lines": cy}

    # 구역 제안: 아래층(없으면 같은 층) 콘크리트 기둥 열(군집 ≥ MIN_COLS_PER_LINE) 을 절단선으로 쓰는 칸 나누기, 칸별 동바리 본수
    for k, s in enumerate(storeys):
        pts = s.pop("_shore_pts", None)
        if not pts:
            continue
        ext = s["structural_extent_mm"]
        below = storeys[k - 1]["id"] if k else None             # k층 슬래브를 받치는 기둥 = 아래층에 속한 기둥
        cs = [c for c in cols if c[3] and c[0] == below] or [c for c in cols if c[3] and c[0] == s["id"]]

        def cuts(vals, lo, hi):
            return [round(v) for v, n in cluster(vals, 300) if n >= MIN_COLS_PER_LINE and lo + 1500 < v < hi - 1500]
        xs = [ext[0]] + cuts([c[1] for c in cs], ext[0], ext[2]) + [ext[2]]
        ys = [ext[1]] + cuts([c[2] for c in cs], ext[1], ext[3]) + [ext[3]]
        cnt = Counter()
        for x, y in pts:
            ix = max(i for i in range(len(xs) - 1) if x >= xs[i])
            iy = max(i for i in range(len(ys) - 1) if y >= ys[i])
            cnt[(ix, iy)] += 1
        s["zones_by_column_lines"] = {
            "x_cuts_mm": xs, "y_cuts_mm": ys,
            "cells": [{"zone": "%s%d" % (chr(65 + ix), iy + 1), "x_mm": [xs[ix], xs[ix + 1]], "y_mm": [ys[iy], ys[iy + 1]],
                       "shores": cnt[(ix, iy)]} for ix in range(len(xs) - 1) for iy in range(len(ys) - 1) if cnt[(ix, iy)]]}

    # 공간 이름 (세대 구분 가능성)
    sp = Counter()
    for e in m.ids("IFCSPACE"):
        a = m.a(e)
        sp[a[7] or a[2]] += 1                      # LongName(실 이름) 우선, 없으면 Name(실 번호)
    R["space_names_top"] = sp.most_common(40)
    R["storeys"] = storeys
    return R


def verify(path, R):
    """단순 정규식 줄 단위 개수와 파서 개수 비교 (한 줄 한 문 파일에서 일치해야 함)."""
    rx = re.compile(r"^#\d+\s*=\s*([A-Z0-9_]+)\s*\(")
    c = Counter()
    with open(path, "r", encoding="latin-1") as f:
        for line in f:
            mm_ = rx.match(line)
            if mm_:
                c[mm_.group(1)] += 1
    diff = {t: (c.get(t, 0), R["type_counts"].get(t, 0)) for t in set(c) | set(R["type_counts"]) if c.get(t, 0) != R["type_counts"].get(t, 0)}
    return {"regex_total": sum(c.values()), "parser_total": R["entities"], "mismatched_types": diff}


def selftest():
    import os
    import tempfile
    txt = ("ISO-10303-21;\nHEADER;\nENDSEC;\nDATA;\n#1= IFCCARTESIANPOINT((0.,0.,0.));\n"
           "#2= IFCLABELTEST('a;b''c',\n  #1, .T., 1.5E-3, $, *, IFCLABEL('\\X2\\C9C0C0C1\\X0\\ 1'));#3= IFCDIRECTION((0.,0.,1.));\n"
           "/* note */\n#4= IFCAXIS2PLACEMENT3D(#1,#3,$);\nENDSEC;\nEND-ISO-10303-21;\n")
    fd, p = tempfile.mkstemp(suffix=".ifc")
    os.write(fd, txt.encode("latin-1"))
    os.close(fd)
    try:
        m = Model(p)
    finally:
        os.remove(p)
    assert sorted(m.raw) == [1, 2, 3, 4], m.raw
    a = m.a(2)
    assert a[0] == "a;b'c" and a[1] == 1 and isinstance(a[1], Ref) and a[2] == "T" and abs(a[3] - 0.0015) < 1e-12
    assert a[4] is None and a[5] == "*" and val(a[6]) == "지상 1", a
    assert area([(0, 0), (2, 0), (2, 3), (0, 3)]) == 6 and inside(1, 1, [(0, 0), (2, 0), (2, 3), (0, 3)])
    T = (( 10.0, 0.0, 0.0), (0.0, 1.0, 0.0), (-1.0, 0.0, 0.0), (0.0, 0.0, 1.0))      # 90° 회전 + 이동
    assert apply(compose(T, T), (1, 0, 0)) == (9.0, 10.0, 0.0)
    assert [n for _, n in cluster([0, 50, 1000, 1040, 5000], 100)] == [2, 2, 1]
    print("selftest ok")


def main(argv):
    sys.stdout.reconfigure(errors="replace")        # -I 는 PYTHONIOENCODING 을 무시 → cp949 콘솔에서 한글 외 문자 대비
    if "--selftest" in argv:
        selftest()
        return
    args = [x for x in argv if not x.startswith("--")]
    out = argv[argv.index("--json") + 1] if "--json" in argv else None
    spacing = float(argv[argv.index("--spacing") + 1]) if "--spacing" in argv else 1.2
    if out in args:
        args.remove(out)
    if "--spacing" in argv:
        args.remove(argv[argv.index("--spacing") + 1])
    R = survey(args[0], spacing)
    if "--verify" in argv:
        R["verify"] = verify(args[0], R)
    for s in R["storeys"]:
        print("%2d %-10s elev %9.0f  h %7s  slab %3d (cand %2d)  wall %4d  col %3d  beam %3d  shores %s  size %s" % (
            s["order"], s["name"], s["elevation_mm"], s["height_to_next_mm"], s["slabs"]["total"], s["slabs"]["structural_candidates"],
            s["walls"]["total"], s["columns"]["total"], s["beams"]["total"], s.get("shore_points_%gm" % spacing), s.get("structural_extent_size_m")))
    if "verify" in R:
        print("verify:", R["verify"]["regex_total"], R["verify"]["parser_total"], R["verify"]["mismatched_types"])
    if out:
        with open(out, "w", encoding="utf-8") as f:
            json.dump(R, f, ensure_ascii=False, indent=1, default=str)
        print("wrote", out)


if __name__ == "__main__":
    main(sys.argv[1:])

// 구역·동바리 생성 — dynamo/01_generate.py 이식 + 레벨·그리드 이름 비의존 (정의서 §3.7-1, design/kbim_analysis.md §8)
//   구조 슬래브 = 바닥·지붕 중 두께 120–600 mm, 윗면이 어느 레벨 ±300 mm 안 (마감·토핑·매트기초·계단참·캐노피·경사 지붕 제외). 층 배정 = 윗면에 가장 가까운 레벨
//   층 이름 = 지상 첫 슬래브층이 1F, 그 아래 B1·B2…, 맨 위 작은 층은 PH
//   구역 = 직교 그리드 칸 → (그리드 없으면) 그 층을 받치는 기둥열(4개 이상) → 고정 크기 타일. 동바리 30본 미만 칸은 이웃과 합침
//   k층 동바리 = 바로 아래 구조 슬래브(슬래브 없는 레벨은 건너뜀) 윗면 위, 높이 = k층 슬래브 밑면 − 아래 슬래브 윗면. 슬래브 두께는 요소에서 읽음
// 재실행 안전: SHR_GenBy = "01_Generate" 인 일반 모델을 지우고 새로 만든다 (Dynamo 01_Generate 와 같은 태그)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace RevitOnSite
{
    public class GenerateOptions
    {
        public double SpacingX = 1200, SpacingY = 1200;   // mm [입력] 조립도 값
        public List<int> ShoreFloors = new List<int>();   // 동바리를 놓을 층의 목록 순서 (SlabLevel.Number)
        public double CellX = 10000, CellY = 9000;        // mm, 그리드·기둥열이 없을 때 구역 크기 [가정] 데모 구역 10 × 9 m
        public int MinShoresPerZone = 30;                 // [가정] 이보다 적은 칸은 이웃 칸과 합침 (kbim_analysis §8.3)
        public string FamilyPath;                         // 모델에 SHR_PipeSupport 가 없을 때 로드할 .rfa
        public bool KeepShores;                           // v6.1 [구역 나누기]: 구역 번호가 없어진 동바리도 지우지 않음 (위치로 다시 매기는 건 ZoneSplit)
    }

    /// <summary>한 층의 구역 계획 (모델은 안 바꿈): 경계 xs·ys(작은 칸 합친 뒤), 예시 동바리 점, 만들 구역 칸(칸 ∩ 슬래브 외곽)</summary>
    public class FloorPlan
    {
        public SlabLevel S;
        public List<double> Xs, Ys;
        public List<(double X, double Y)> Pts;
        public List<(int Ix, int Iy, double Ax, double Ay, double Bx, double By)> Cells = new();
        public string How;                                // 그리드 · 기둥열 · 타일
        public int Merged;
    }

    /// <summary>구조 슬래브가 있는 층 하나</summary>
    public class SlabLevel
    {
        public int Number;                                // 목록 순서 1..n (낮은 층부터)
        public int FloorNo;                               // 층 번호: 지하 −1, −2 … / 지상 1, 2 … / PH 는 지상 최상층 다음 번호
        public string Label;                              // "B1", "1F", "PH"
        public Level Level;
        public double Top, Bottom;                        // 대표 슬래브 윗면·밑면 Z (내부 단위 ft)
        public double MinX = double.MaxValue, MinY = double.MaxValue, MaxX = double.MinValue, MaxY = double.MinValue;
        public List<Element> Slabs = new List<Element>();
        public List<Face> TopFaces = new List<Face>();
        public List<Element> Unsure = new List<Element>();   // 재료 불명 → 콘크리트로 가정한 바닥·지붕 [가정] (확인 창·모델 진단에 개수)
        public double Area => (MaxX - MinX) * (MaxY - MinY);
    }

    public static class ZoneGenerator
    {
        public const string GenBy = "01_Generate";
        public const string FamilyName = "SHR_PipeSupport";
        public const string ZoneType = "구역";   // 구역 상자의 SHR_Type
        /// <summary>구역 상자 윗면을 슬래브 윗면보다 이만큼 올림 (mm) — 8차 점검 N4: 윗면이 슬래브(검측 뷰 투명 85 %) 윗면과 같은 높이라
        /// 안쪽을 눌러도 구역이 골라지지 않았음. 사용자의 "면으로 요소 선택" 설정과 무관하게 구역 면이 먼저 맞도록 [가정 20 mm]</summary>
        public const double TopPlateMm = 20;

        static double Mm(double v) => UnitUtils.ConvertToInternalUnits(v, UnitTypeId.Millimeters);
        static double ToMm(double v) => UnitUtils.ConvertFromInternalUnits(v, UnitTypeId.Millimeters);

        /// <summary>슬래브 두께 = 바닥·지붕 두께 매개변수(구조층 합) → IFC 재료층 합(IfcMaterial "이름: 두께 mm;…") → 상자 높이.
        /// 총괄 78차 ③: 상자 높이는 경사·Brep 요소에서 크게 틀림 (주택 모델.ifc slab_mm 549, kbim_analysis §8.1)</summary>
        static double Thickness(Element e, BoundingBoxXYZ bb)
        {
            var p = (e as Floor)?.get_Parameter(BuiltInParameter.FLOOR_ATTR_THICKNESS_PARAM)
                    ?? (e as RoofBase)?.get_Parameter(BuiltInParameter.ROOF_ATTR_THICKNESS_PARAM);
            if (p != null && p.AsDouble() > 0) return p.AsDouble();
            double ifc = IfcLayers(e).Sum();
            return ifc > 0 ? Mm(ifc) : bb.Max.Z - bb.Min.Z;
        }

        /// <summary>IFC 재료층 두께 mm 목록 (IfcMaterial 매개변수 "이름: 두께 mm;…", 두께 없는 층은 뺌)</summary>
        static IEnumerable<double> IfcLayers(Element e) => (e.LookupParameter("IfcMaterial")?.AsString() ?? "").Split(';')
            .Select(s => System.Text.RegularExpressions.Regex.Match(s, @":\s*([\d.]+)\s*mm")).Where(m => m.Success)
            .Select(m => double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0);

        /// <summary>D1 하중비교: 층 번호 → 그 층 구조 슬래브들 (평면 상자 m, 두께 mm 반올림). zones.csv slab_mm (구역과 겹치는 것 중 최댓값)</summary>
        public static Dictionary<int, List<(double X0, double Y0, double X1, double Y1, double Mm)>> SlabsByFloor(Document doc)
        {
            static double M(double f) => UnitUtils.ConvertFromInternalUnits(f, UnitTypeId.Meters);
            return DetectFloors(doc).GroupBy(s => s.FloorNo).ToDictionary(g => g.Key, g => g.SelectMany(s => s.Slabs)
                .Select(e => { var bb = e.get_BoundingBox(null); return (M(bb.Min.X), M(bb.Min.Y), M(bb.Max.X), M(bb.Max.Y), Math.Round(ToMm(Thickness(e, bb)))); }).ToList());
        }

        /// <summary>구조 슬래브가 있는 층 목록. excluded 를 주면 빠진 바닥·지붕과 이유를 담는다 (모델 진단)</summary>
        public static List<SlabLevel> DetectFloors(Document doc) => DetectFloors(doc, null);

        public static List<SlabLevel> DetectFloors(Document doc, List<(Element El, string Reason)> excluded)
        {
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.ProjectElevation).ToList();
            var cats = new ElementMulticategoryFilter(new[] { BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs });
            var slabs = new FilteredElementCollector(doc).WherePasses(cats).WhereElementIsNotElementType().Where(e => e.get_BoundingBox(null) != null).ToList();
            if (slabs.OfType<Floor>().Any(IsStructural))   // 네이티브 바닥은 구조 바닥만 (SHR_CastInPlace = 예 는 예외)
            {
                foreach (var f in slabs.OfType<Floor>().Where(f => !IsStructural(f) && f.LookupParameter("SHR_CastInPlace")?.AsInteger() != 1)) excluded?.Add((f, "바닥 '구조' 체크 없음 (마감 바닥)"));
                slabs = slabs.Where(e => !(e is Floor f) || IsStructural(f) || f.LookupParameter("SHR_CastInPlace")?.AsInteger() == 1).ToList();
            }
            if (levels.Count == 0 || slabs.Count == 0) return new List<SlabLevel>();

            var byLevel = new Dictionary<ElementId, SlabLevel>();
            foreach (var e in slabs)
            {
                var bb = e.get_BoundingBox(null);
                // 사용자 지정 SHR_CastInPlace: 아니오 = 제외, 예 = 자동 판별(프리캐스트·IFC 정보) 건너뜀, 비어 있음 = 자동
                var cip = e.LookupParameter("SHR_CastInPlace");
                bool? forced = cip != null && cip.HasValue ? cip.AsInteger() == 1 : (bool?)null;
                string why = forced == false ? "SHR_CastInPlace = 아니오" : null;
                double t = Thickness(e, bb);
                if (why == null && (t < Mm(120) || t > Mm(600))) why = "두께 " + Math.Round(ToMm(t)) + " mm (120–600 밖: 마감·토핑·매트기초)";
                if (why == null && forced != true && e.Category?.BuiltInCategory == BuiltInCategory.OST_Roofs) why = Pitched(doc, e, bb, t);
                if (why == null && e is Floor fl && (doc.GetElement(fl.GetTypeId()) as FloorType)?.IsFoundationSlab == true) why = "기초 슬래브 유형";
                if (why == null && forced != true) why = NotStructuralByIfc(e);
                bool unsure = false;
                if (why == null && forced != true && (e is Floor || e is RoofBase)) why = NotCastInPlace(doc, e, out unsure);
                var lv = levels.OrderBy(l => Math.Abs(l.ProjectElevation - bb.Max.Z)).First();      // 윗면에 가장 가까운 레벨
                if (why == null && Math.Abs(lv.ProjectElevation - bb.Max.Z) > Mm(300))
                    why = "윗면이 가장 가까운 레벨(" + lv.Name + ")에서 " + Math.Round(ToMm(Math.Abs(lv.ProjectElevation - bb.Max.Z))) + " mm (계단참·캐노피)";
                if (why != null) { excluded?.Add((e, why)); continue; }
                if (!byLevel.TryGetValue(lv.Id, out var s)) byLevel[lv.Id] = s = new SlabLevel { Level = lv };
                s.Slabs.Add(e);
                if (unsure) s.Unsure.Add(e);
                s.MinX = Math.Min(s.MinX, bb.Min.X); s.MinY = Math.Min(s.MinY, bb.Min.Y);
                s.MaxX = Math.Max(s.MaxX, bb.Max.X); s.MaxY = Math.Max(s.MaxY, bb.Max.Y);
            }
            if (byLevel.Count == 0) return new List<SlabLevel>();
            // shortcut: 외곽 상자 면적이 최대층의 5 % 미만인 레벨은 층에서 뺌(작은 램프·피트). 옥탑 지붕(PH)은 남김. 실제 면적 합 비교가 필요하면 바꿀 것
            double maxArea = byLevel.Values.Max(s => s.Area);
            var floors = byLevel.Values.Where(s => s.Area >= 0.05 * maxArea).OrderBy(s => s.Level.ProjectElevation).ToList();
            for (int i = 0; i < floors.Count; i++)
            {
                var s = floors[i];
                s.Number = i + 1;
                var main = s.Slabs.OrderByDescending(e => { var b = e.get_BoundingBox(null); return (b.Max.X - b.Min.X) * (b.Max.Y - b.Min.Y); }).First();
                var bb = main.get_BoundingBox(null);
                s.Top = bb.Max.Z;
                s.Bottom = s.Top - Thickness(main, bb);
                foreach (var e in s.Slabs) s.TopFaces.AddRange(TopFaces(e));
            }
            NameFloors(floors);
            return floors;
        }

        static bool IsStructural(Floor f) => f.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL)?.AsInteger() == 1;

        /// <summary>[가정] 프리캐스트 표시어 (재료·유형 이름, 소문자). RAVA3Pro "Runkobetonielementit"(구체 콘크리트 부재) 등</summary>
        static readonly string[] PrecastWords = { "element", "precast", "pre-cast", "prefab", "hollow", "ontelo", "fertigteil", "kanalplatte", "hcs", "프리캐스트", "중공", "pc슬래브", "pc 슬래브", "하프", "조립식" };

        static bool IsPrecastName(string s) { s = (s ?? "").ToLowerInvariant(); return PrecastWords.Any(w => s.Contains(w)); }

        /// <summary>네이티브 바닥·지붕: 가장 두꺼운 구조층이 프리캐스트 / 확실히 다른 구조재(목재·금속) / 그 아래에 단열층(지반 위 슬래브)이면 이유 [가정].
        /// unsure = 재료 불명(회반죽·페인트·기본값·에셋 없음) → 콘크리트로 가정 [가정] (총괄 78차 ② — smiley-west.ifc 'Verputz, weiß' 한 겹 바닥이 다 빠졌음)</summary>
        static string NotCastInPlace(Document doc, Element e, out bool unsure)
        {
            unsure = false;
            var ht = doc.GetElement(e.GetTypeId()) as HostObjAttributes;
            var cs = ht?.GetCompoundStructure();
            if (IsPrecastName(ht?.Name) || IsPrecastName((ht as ElementType)?.FamilyName)) return "프리캐스트 유형 이름: " + ht.Name;
            if (cs == null) return null;
            var layers = cs.GetLayers();
            var core = layers.Where(l => l.Function == MaterialFunctionAssignment.Structure).OrderByDescending(l => l.Width).FirstOrDefault();
            if (core == null) return null;
            var m = doc.GetElement(core.MaterialId) as Material;
            if (IsPrecastName(m?.Name)) return "가장 두꺼운 구조층이 프리캐스트 재료: " + m.Name;
            var (unknown, other) = StructMaterial(doc, e, m);
            if (other != null) return "가장 두꺼운 구조층이 다른 구조재: " + other;
            unsure = unknown;
            int ci = layers.IndexOf(core);   // 재료층은 위(0)에서 아래 순
            return layers.Skip(ci + 1).Any(l => l.Function == MaterialFunctionAssignment.Insulation) ? "구조층 아래 단열층 (지반 위 슬래브)" : null;
        }

        static readonly string[] ConcreteWords = { "concrete", "콘크리트", "betoni", "beton", "コンクリート" };
        /// <summary>[가정] 확실히 다른 구조재 표시어 (소문자) — 목재·금속. 핀란드어(RAVA3Pro)·독일어·일본어 포함</summary>
        static readonly string[] OtherWords = { "wood", "timber", "lumber", "glulam", "holz", "puu", "kerto", "steel", "stahl", "teräs", "metal", "alumin",
                                                "목재", "목조", "철골", "강재", "금속", "木材", "木造", "鉄骨", "鋼", "金属" };

        /// <summary>구조층 재료 → (불명, 다른 구조재 설명). 콘크리트 = (false, null), 목재·금속 = (false, "이름 (근거)"), 그 밖 = (true, null) [가정].
        /// 순서: 구조 에셋 분류(콘크리트·목재·금속) → 재료 이름·분류 글자 → IFC 재료 이름(IfcMaterial — IFC 열기 바닥은 Revit 재료가 표면 스타일 이름일 수 있음)</summary>
        static (bool Unknown, string Other) StructMaterial(Document doc, Element e, Material m)
        {
            if (m != null && doc.GetElement(m.StructuralAssetId) is PropertySetElement ps && ps.GetStructuralAsset() is StructuralAsset a)
            {
                if (a.StructuralAssetClass == StructuralAssetClass.Concrete) return (false, null);
                if (a.StructuralAssetClass is StructuralAssetClass.Wood or StructuralAssetClass.Metal)
                    return (false, m.Name + " (구조 에셋 " + (a.StructuralAssetClass == StructuralAssetClass.Wood ? "목재" : "금속") + ")");
            }
            var ifc = e.LookupParameter("IfcMaterial")?.AsString();
            foreach (var (s, shown) in new[] { (m == null ? null : m.Name + " " + m.MaterialClass + " " + m.MaterialCategory, m?.Name), (ifc, "IFC 재료 " + ifc) })
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                var low = s.ToLowerInvariant();
                if (ConcreteWords.Any(low.Contains)) return (false, null);
                if (OtherWords.FirstOrDefault(low.Contains) is string w) return (false, shown + " (이름 '" + w + "')");
            }
            return (true, null);
        }

        /// <summary>지붕 범주: 경사 지붕이면 이유 [가정] (총괄 78차 ③ — 주택 모델.ifc 'Roof_Pitched…' 윗면이 레벨 300 mm 안이라 2F 슬래브에 섞였음).
        /// 이름·IFC 유형에 PITCH / 윗면이 수평 아님(수평 윗면 &lt; 위로 향한 면 평면 투영의 50 %) / 상자 높이 ≥ 두께 × 2</summary>
        static string Pitched(Document doc, Element e, BoundingBoxXYZ bb, double t)
        {
            var type = doc.GetElement(e.GetTypeId()) as ElementType;
            var name = (e.Name + " " + type?.FamilyName + " " + type?.Name + " " + e.get_Parameter(BuiltInParameter.IFC_EXPORT_PREDEFINEDTYPE)?.AsString()).ToUpperInvariant();
            if (name.Contains("PITCH")) return "경사 지붕 (이름·IFC 유형 PITCHED)";
            double flat = 0, all = 0;
            foreach (PlanarFace f in TopFaces(e, 0.1)) { all += f.Area * f.FaceNormal.Z; if (f.FaceNormal.Z > 0.99) flat += f.Area; }
            if (all > 0 && flat < 0.5 * all) return "경사 지붕 (수평 윗면 " + Math.Round(100 * flat / all) + " %)";
            double h = bb.Max.Z - bb.Min.Z;
            return h >= 2 * t ? "경사 지붕 (상자 높이 " + Math.Round(ToMm(h)) + " mm ≥ 두께 " + Math.Round(ToMm(t)) + " mm × 2)" : null;
        }

        /// <summary>IFC 에서 온 요소의 표준 정보로 구조 슬래브가 아닌 것: Pset_SlabCommon.LoadBearing = 아니오, 미리 정의된 유형이 FLOOR·ROOF 외(LANDING·BASESLAB 등). 정보가 없으면 false</summary>
        static string NotStructuralByIfc(Element e)
        {
            var lb = e.LookupParameter("Pset_SlabCommon.LoadBearing");
            if (lb != null && lb.HasValue && lb.StorageType == StorageType.Integer && lb.AsInteger() == 0) return "IFC LoadBearing = 아니오";
            foreach (Parameter p in e.Parameters)   // IFC Pset_PrecastConcrete* 속성이 있으면 조립식
                if (p.HasValue && p.Definition.Name.IndexOf("PrecastConcrete", StringComparison.OrdinalIgnoreCase) >= 0) return "IFC " + p.Definition.Name + " (조립식)";
            if (IsPrecastName(e.Name)) return "프리캐스트 이름: " + e.Name;
            // 재료층 목록 "이름: 두께 mm;…" — 가장 두꺼운 층이 120 mm 미만이면 마감·토핑 복합층 (kbim_analysis §8.1-2)
            var mat = e.LookupParameter("IfcMaterial")?.AsString();
            if (!string.IsNullOrEmpty(mat))
            {
                if (IsPrecastName(mat)) return "프리캐스트 재료: " + mat;
                double thickest = IfcLayers(e).DefaultIfEmpty(0).Max();
                if (thickest > 0 && thickest < 120) return "가장 두꺼운 재료층 " + thickest + " mm < 120 (마감·토핑)";
            }
            var pt = e.get_Parameter(BuiltInParameter.IFC_EXPORT_PREDEFINEDTYPE)?.AsString()?.Trim().ToUpperInvariant();
            return !string.IsNullOrEmpty(pt) && pt != "FLOOR" && pt != "ROOF" && pt != "NOTDEFINED" && pt != "USERDEFINED" ? "IFC 유형 " + pt + " (계단참·기초 등)" : null;
        }

        /// <summary>지상 첫 슬래브층(레벨 고도 ≥ −300 mm) = 1F, 아래로 B1·B2…, 맨 위에서 면적이 지상 최대층의 절반 미만인 층들 = PH</summary>
        static void NameFloors(List<SlabLevel> floors)
        {
            int g = floors.FindIndex(f => f.Level.ProjectElevation >= -Mm(300));
            if (g < 0) g = floors.Count;
            for (int i = 0; i < g; i++) { floors[i].FloorNo = -(g - i); floors[i].Label = "B" + (g - i); }
            if (g == floors.Count) return;
            double maxAbove = floors.Skip(g).Max(f => f.Area);
            int ph = 0;   // shortcut: 외곽 상자 면적 50 % 미만으로 PH 판단
            for (int i = floors.Count - 1; i > g && floors[i].Area < 0.5 * maxAbove; i--) ph++;
            for (int i = g; i < floors.Count; i++)
            {
                floors[i].FloorNo = i - g + 1;
                int k = i - (floors.Count - ph) + 1;
                floors[i].Label = k >= 1 ? (ph == 1 ? "PH" : "PH" + k) : (i - g + 1) + "F";
            }
        }

        /// <summary>윗면 = 평면 면 중 법선 Z &gt; minNz (기본 0.9 — 동바리 놓을 면, 0.1 = 경사 지붕 판별용 위로 향한 면 전부)</summary>
        static IEnumerable<Face> TopFaces(Element e, double minNz = 0.9)
        {
            var geo = e.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
            if (geo == null) yield break;
            foreach (var g in geo)
            {
                var items = g is GeometryInstance gi ? gi.GetInstanceGeometry().Cast<GeometryObject>() : new[] { g };
                foreach (var solid in items.OfType<Solid>())
                    foreach (Face f in solid.Faces)
                        if (f is PlanarFace pf && pf.FaceNormal.Z > minNz) yield return f;
            }
        }

        /// <summary>점 (x, y) 가 그 층 슬래브 위인지. 면을 못 읽은 요소(메시 등)뿐이면 외곽 상자로 판단</summary>
        internal static bool OnSlab(SlabLevel s, double x, double y)
        {
            if (x < s.MinX - 1e-6 || x > s.MaxX + 1e-6 || y < s.MinY - 1e-6 || y > s.MaxY + 1e-6) return false;
            if (s.TopFaces.Count == 0) return true;
            double cx = (s.MinX + s.MaxX) / 2, cy = (s.MinY + s.MaxY) / 2, d = Mm(5);
            var nudged = new XYZ(x + Math.Sign(cx - x) * d, y + Math.Sign(cy - y) * d, s.Top);   // 슬래브 가장자리 점 허용
            return s.TopFaces.Any(f => f.Project(new XYZ(x, y, s.Top)) != null || f.Project(nudged) != null);
        }

        /// <summary>모델 전체 기준: 직교 그리드 2개 이상씩이면 그리드 칸(gx, gy), 아니면 null (층마다 기둥열 → 타일)</summary>
        public static (List<double> xs, List<double> ys, string source) ZoneAxes(Document doc, List<SlabLevel> floors, GenerateOptions o)
        {
            var gx = new List<double>(); var gy = new List<double>();
            foreach (Grid g in new FilteredElementCollector(doc).OfClass(typeof(Grid)))
            {
                if (!(g.Curve is Line ln)) continue;
                if (Math.Abs(ln.Direction.X) < 1e-3) gx.Add(ln.Origin.X);        // 세로선 → X 경계
                else if (Math.Abs(ln.Direction.Y) < 1e-3) gy.Add(ln.Origin.Y);   // 가로선 → Y 경계
            }
            gx = Dedupe(gx, Mm(1)); gy = Dedupe(gy, Mm(1));
            if (gx.Count >= 2 && gy.Count >= 2) return (gx, gy, "그리드 X " + gx.Count + "개 · Y " + gy.Count + "개");
            return (null, null, "그리드 없음 → 층마다 기둥열(4개 이상), 없으면 " + (o.CellX / 1000).ToString("0.#") + " × " + (o.CellY / 1000).ToString("0.#") + " m 분할");
        }

        /// <summary>한 층의 구역 경계: 그리드 → 그 슬래브를 받치는 기둥열 → 타일</summary>
        static (List<double> xs, List<double> ys, string how) FloorAxes(SlabLevel s, List<double> gx, List<double> gy, List<(double X, double Y, double Z0, double Z1)> cols, GenerateOptions o)
        {
            if (gx != null) return (new List<double>(gx), new List<double>(gy), "그리드");
            var here = cols.Where(c => c.Z0 < s.Bottom - Mm(300) && c.Z1 > s.Bottom - Mm(300)).ToList();   // 아래층에서 이 슬래브까지 올라오는 기둥
            var lx = Rows(here.Select(c => c.X), s.MinX, s.MaxX);
            var ly = Rows(here.Select(c => c.Y), s.MinY, s.MaxY);
            if (lx.Count + ly.Count > 0)
                return (new[] { s.MinX }.Concat(lx).Append(s.MaxX).ToList(), new[] { s.MinY }.Concat(ly).Append(s.MaxY).ToList(), "기둥열");
            return (Steps(s.MinX, s.MaxX, Mm(o.CellX)), Steps(s.MinY, s.MaxY, Mm(o.CellY)), "타일");
        }

        /// <summary>기둥 중심 좌표를 300 mm 로 묶어 4개 이상인 열의 평균. 슬래브 외곽에서 1.5 m 안쪽만 절단선</summary>
        static List<double> Rows(IEnumerable<double> v, double lo, double hi)
        {
            var r = new List<double>(); var cur = new List<double>();
            void Close() { if (cur.Count >= 4) { double m = cur.Average(); if (m - lo > Mm(1500) && hi - m > Mm(1500)) r.Add(m); } cur.Clear(); }
            foreach (var x in v.OrderBy(a => a)) { if (cur.Count > 0 && x - cur[0] > Mm(300)) Close(); cur.Add(x); }
            Close();
            return r;
        }

        /// <summary>동바리 점이 min 본 미만인 칸이 없어질 때까지, 그 칸과 가장 작은 이웃 사이 경계선을 지움 (칸은 계속 직사각형)</summary>
        static int MergeSmall(List<double> xs, List<double> ys, List<(double X, double Y)> pts, int min)
        {
            int removed = 0;
            while (true)
            {
                int nx = xs.Count - 1, ny = ys.Count - 1;
                var cnt = new int[nx, ny];
                foreach (var p in pts) cnt[CellIndex(p.X, xs), CellIndex(p.Y, ys)]++;
                (int ix, int iy, int n) small = (-1, -1, int.MaxValue);
                for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++) if (cnt[i, j] > 0 && cnt[i, j] < min && cnt[i, j] < small.n) small = (i, j, cnt[i, j]);
                if (small.ix < 0) return removed;
                var cand = new List<(bool isX, int line, int n)>();
                if (small.ix > 0) cand.Add((true, small.ix, cnt[small.ix - 1, small.iy]));
                if (small.ix < nx - 1) cand.Add((true, small.ix + 1, cnt[small.ix + 1, small.iy]));
                if (small.iy > 0) cand.Add((false, small.iy, cnt[small.ix, small.iy - 1]));
                if (small.iy < ny - 1) cand.Add((false, small.iy + 1, cnt[small.ix, small.iy + 1]));
                if (cand.Count == 0) return removed;
                var c = cand.OrderBy(k => k.n).First();
                (c.isX ? xs : ys).RemoveAt(c.line);
                removed++;
            }
        }

        static List<double> Dedupe(List<double> v, double tol)
        {
            var r = new List<double>();
            foreach (var x in v.OrderBy(a => a)) if (r.Count == 0 || x - r[r.Count - 1] > tol) r.Add(x);
            return r;
        }

        static List<double> Steps(double a0, double a1, double step)
        {
            var r = new List<double>();
            for (double a = a0; a < a1 - Mm(1); a += step) r.Add(a);
            r.Add(a1);
            return r;
        }

        /// <summary>a0~a1 구간에 s 간격으로 놓이는 좌표, 양 끝 포함 (01_generate.py axis_points)</summary>
        internal static List<double> AxisPoints(double a0, double a1, double s)
        {
            int n = (int)((a1 - a0) / s + 1e-6);
            return Enumerable.Range(0, n + 1).Select(i => a0 + i * s).ToList();
        }

        /// <summary>v 가 들어가는 칸 번호. 경계 위 점은 다음 칸, 마지막 경계는 마지막 칸 (01_generate.py cell_index)</summary>
        static int CellIndex(double v, List<double> b)
        {
            for (int i = 0; i < b.Count - 2; i++) if (v < b[i + 1]) return i;
            return b.Count - 2;
        }

        static string Letters(int i)
        {
            var s = "";
            for (i++; i > 0; i /= 26) { i--; s = (char)('A' + i % 26) + s; }
            return s;
        }

        public static string ZoneId(string label, int ix, int iy) => label + "-" + Letters(ix) + (iy + 1);

        static void Set(Element e, string name, object v)
        {
            var p = e.LookupParameter(name);
            if (p == null || p.IsReadOnly) return;
            if (v is string str) p.Set(str); else if (v is int n) p.Set(n); else p.Set((double)v);
        }

        /// <summary>동바리 높이 경고 (정의서 §3.2: 3.5 m 초과 수평연결재, 4 m 초과 시스템 동바리, 5 m 이상 구조계산) + 1.5 m 미만 안내 [가정] (총괄 78차 —
        /// Snowdon 3F 635 mm 처럼 중간층 위일 수 있음, 판정은 그대로)</summary>
        internal static string HeightFlag(double hMm) =>
            hMm >= 5000 ? " ⚠ 5 m 이상: 기술사 구조계산 대상·시스템 동바리" :
            hMm > 4000 ? " ⚠ 4 m 초과: 시스템 동바리 전환 검토" :
            hMm > 3500 ? " ⚠ 3.5 m 초과: 수평연결재 필요" :
            hMm < 1500 ? " ⚠ 1.5 m 미만 — 중간층(메자닌) 위인지 확인 [가정]" : "";

        /// <summary>동바리 패밀리 기호 (없으면 familyPath 에서 로드, 꺼져 있으면 켬) — 열린 트랜잭션 안. 못 찾으면 InvalidOperationException</summary>
        internal static FamilySymbol ShoreSymbol(Document doc, string familyPath)
        {
            var symbol = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault(s => s.Family.Name == FamilyName);
            if (symbol == null && familyPath != null && File.Exists(familyPath) && doc.LoadFamily(familyPath, out var fam))
                symbol = doc.GetElement(fam.GetFamilySymbolIds().First()) as FamilySymbol;
            if (symbol == null) throw new InvalidOperationException("동바리 패밀리 " + FamilyName + " 를 찾지도 로드하지도 못했습니다.");
            if (!symbol.IsActive) { symbol.Activate(); doc.Regenerate(); }
            return symbol;
        }

        /// <summary>층 s 동바리가 설 곳 = 바로 아래 구조 슬래브 윗면 (없으면 슬래브 밑면 아래 가장 가까운 레벨) → (레벨, 바닥 Z, 높이, 오류 문장 또는 null)</summary>
        internal static (Level Base, double BaseZ, double H, string Error) ShoreBase(Document doc, List<SlabLevel> floors, SlabLevel s)
        {
            var below = floors.FirstOrDefault(f => f.Number == s.Number - 1);
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.ProjectElevation).ToList();
            Level baseLevel = below?.Level ?? levels.LastOrDefault(l => l.ProjectElevation < s.Bottom - Mm(1));
            if (baseLevel == null) return (null, 0, 0, s.Label + ": 아래에 설 레벨이 없어 건너뜀");
            double baseZ = below?.Top ?? baseLevel.ProjectElevation;
            double h = s.Bottom - baseZ;
            return (baseLevel, baseZ, h, h < Mm(300) ? s.Label + ": 동바리 높이 " + Math.Round(ToMm(h)) + " mm 가 너무 작아 건너뜀" : null);
        }

        /// <summary>동바리 한 본 (열린 트랜잭션 안): 바닥 Z 에 높이 h, 구역·층·만든 곳·형식 매개변수</summary>
        internal static FamilyInstance NewShore(Document doc, FamilySymbol symbol, Level baseLevel, double baseZ, double h, double x, double y, string zoneId, int floorNo, string genBy, string type)
        {
            var fi = doc.Create.NewFamilyInstance(new XYZ(x, y, 0), symbol, baseLevel, StructuralType.NonStructural);
            // Revit 2026 은 위치 Z 를 레벨 기준 간격띄우기로 해석 → 간격띄우기를 직접 지정
            var off = fi.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM) ?? fi.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM);
            if (off != null && !off.IsReadOnly) off.Set(baseZ - baseLevel.ProjectElevation);
            Set(fi, "높이", h);
            Set(fi, "SHR_ZoneID", zoneId);
            Set(fi, "SHR_Floor", floorNo);
            Set(fi, "SHR_GenBy", genBy);
            Set(fi, "SHR_Type", type);   // 동바리 형식(설치 검측) + 일람표에서 구역과 동바리를 가르는 값
            return fi;
        }

        /// <summary>층마다 구역 칸 계획 — 그리드 → 기둥열 → 타일, 동바리 점이 MinShoresPerZone 미만인 칸은 이웃과 합침, 칸 안 3×3 점 중 하나라도 슬래브 위인 칸만.
        /// 모델은 안 바꿈 (v6.1 열기 확인 창의 '예상 구역 m개' 와 Run 이 같은 계획을 씀)</summary>
        public static List<FloorPlan> Plan(Document doc, List<SlabLevel> floors, GenerateOptions o)
        {
            var (gx, gy, _) = ZoneAxes(doc, floors, o);
            var cols = new FilteredElementCollector(doc).WherePasses(new ElementMulticategoryFilter(new[] { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns }))
                .WhereElementIsNotElementType().Select(e => e.get_BoundingBox(null)).Where(b => b != null)
                .Select(b => ((b.Min.X + b.Max.X) / 2, (b.Min.Y + b.Max.Y) / 2, b.Min.Z, b.Max.Z)).ToList();   // shortcut: 콘크리트 여부는 안 봄 (IFC 재료 정보 확인 후)
            var plans = new List<FloorPlan>();
            foreach (var s in floors)
            {
                var (xs, ys, h0) = FloorAxes(s, gx, gy, cols, o);
                var px = AxisPoints(xs[0], xs[xs.Count - 1], Mm(o.SpacingX));
                var py = AxisPoints(ys[0], ys[ys.Count - 1], Mm(o.SpacingY));
                var pts = new List<(double X, double Y)>();
                foreach (var x in px) foreach (var y in py) if (OnSlab(s, x, y)) pts.Add((x, y));
                var fp = new FloorPlan { S = s, Xs = xs, Ys = ys, Pts = pts, How = h0, Merged = MergeSmall(xs, ys, pts, o.MinShoresPerZone) };
                for (int ix = 0; ix < xs.Count - 1; ix++)
                    for (int iy = 0; iy < ys.Count - 1; iy++)
                    {
                        double ax = Math.Max(xs[ix], s.MinX), bx = Math.Min(xs[ix + 1], s.MaxX);
                        double ay = Math.Max(ys[iy], s.MinY), by = Math.Min(ys[iy + 1], s.MaxY);
                        if (bx - ax < Mm(500) || by - ay < Mm(500)) continue;
                        bool any = false;
                        for (int i = 1; i <= 3 && !any; i++)
                            for (int j = 1; j <= 3 && !any; j++)
                                any = OnSlab(s, ax + (bx - ax) * i / 4, ay + (by - ay) * j / 4);
                        if (any) fp.Cells.Add((ix, iy, ax, ay, bx, by));
                    }
                plans.Add(fp);
            }
            return plans;
        }

        /// <summary>열린 트랜잭션 안에서 호출. 반환 = 결과 문장 목록</summary>
        public static List<string> Run(Document doc, GenerateOptions o)
        {
            var report = new List<string>();
            var floors = DetectFloors(doc);
            if (floors.Count == 0) throw new InvalidOperationException("구조 바닥이 있는 레벨을 찾지 못했습니다(두께 120–600 mm, 윗면이 레벨 ±300 mm). 바닥을 먼저 모델링하거나, IFC 라면 바닥이 '바닥' 카테고리로 들어왔는지 확인하세요.");
            var (gx, gy, source) = ZoneAxes(doc, floors, o);

            FamilySymbol symbol = o.ShoreFloors.Count > 0 ? ShoreSymbol(doc, o.FamilyPath) : null;

            // 1) 이전 생성분 삭제: 구역은 전부(모든 층을 다시 만듦), 동바리는 이번에 고른 층만 — 12차 점검 N10: 9F 만 골랐는데 10–12F 동바리까지 지웠음
            var shoreSet = new HashSet<int>(o.ShoreFloors);
            var pickedFloorNos = floors.Where(f => shoreSet.Contains(f.Number)).Select(f => f.FloorNo).ToHashSet();
            var oldAll = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_GenericModel).WhereElementIsNotElementType()
                .Where(e => e.LookupParameter("SHR_GenBy")?.AsString() == GenBy).ToList();
            var keptShores = oldAll.Where(e => e is FamilyInstance && !(e.LookupParameter("SHR_Floor") is Parameter fp && fp.HasValue && pickedFloorNos.Contains(fp.AsInteger()))).ToList();
            var old = oldAll.Except(keptShores).Select(e => e.Id).ToList();
            if (old.Count > 0) doc.Delete(old);
            report.Add("삭제 " + old.Count + "개 (이전 생성분: 구역 전부 + 고른 층 동바리)" + (keptShores.Count > 0 ? " · 고르지 않은 층 동바리 " + keptShores.Count + "본은 그대로" : ""));
            var newZoneIds = new HashSet<string>();
            report.Add("층 " + floors.Count + "개: " + floors[0].Label + " = " + floors[0].Level.Name + " … " + floors[floors.Count - 1].Label + " = " + floors[floors.Count - 1].Level.Name + " / 구역 기준: " + source);

            var gm = Category.GetCategory(doc, BuiltInCategory.OST_GenericModel).Id;
            int nz = 0, merged = 0;
            Element lastZone = null;
            var how = new Dictionary<string, int>();
            foreach (var fp in Plan(doc, floors, o))
            {
                var s = fp.S;
                var below = floors.FirstOrDefault(f => f.Number == s.Number - 1);
                var (xs, ys, pts) = (fp.Xs, fp.Ys, fp.Pts);
                how[fp.How] = how.TryGetValue(fp.How, out var c0) ? c0 + 1 : 1;
                merged += fp.Merged;

                // 2) 구역 상자: 칸 ∩ 슬래브 외곽, 슬래브 두께만큼 (칸 안 3×3 점 중 하나라도 슬래브 위인 칸만 — Plan)
                foreach (var (ix, iy, ax, ay, bx, by) in fp.Cells)
                    {
                        var p = new[] { new XYZ(ax, ay, s.Bottom), new XYZ(bx, ay, s.Bottom), new XYZ(bx, by, s.Bottom), new XYZ(ax, by, s.Bottom) };
                        var loop = new CurveLoop();
                        for (int i = 0; i < 4; i++) loop.Append(Line.CreateBound(p[i], p[(i + 1) % 4]));
                        var ds = DirectShape.CreateElement(doc, gm);
                        ds.SetShape(new List<GeometryObject> { GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, s.Top - s.Bottom + Mm(TopPlateMm)) });
                        Set(ds, "SHR_ZoneID", ZoneId(s.Label, ix, iy));
                        newZoneIds.Add(ZoneId(s.Label, ix, iy));
                        Set(ds, "SHR_Floor", s.FloorNo);
                        Set(ds, "SHR_GenBy", GenBy);
                        Set(ds, "SHR_Type", ZoneType);   // 일람표가 구역만 고르는 긍정 조건 (동바리는 "파이프서포트")
                        lastZone = ds;
                        nz++;
                    }

                // 3) 동바리: 바로 아래 구조 슬래브 윗면 위 (없으면 슬래브 밑면 아래 가장 가까운 레벨)
                if (!shoreSet.Contains(s.Number)) continue;
                var (baseLevel, baseZ, h, baseErr) = ShoreBase(doc, floors, s);
                if (baseErr != null) { report.Add(baseErr); continue; }
                foreach (var (x, y) in pts)
                    NewShore(doc, symbol, baseLevel, baseZ, h, x, y, ZoneId(s.Label, CellIndex(x, xs), CellIndex(y, ys)), s.FloorNo, GenBy, "파이프서포트");
                report.Add(s.Label + " 동바리 " + pts.Count + "본 (높이 " + Math.Round(ToMm(h)) + " mm, " + (below != null ? below.Label + " 슬래브" : baseLevel.Name) + " 위)" + HeightFlag(ToMm(h)));
            }
            // 남겨 둔 동바리 중 구역 번호가 새 구역에 없는 것(칸 크기·그리드가 바뀜)은 어느 구역 것인지 모르므로 지움
            var orphan = o.KeepShores ? new List<ElementId>() : keptShores.Where(e => e.IsValidObject && !newZoneIds.Contains(e.LookupParameter("SHR_ZoneID")?.AsString() ?? "")).Select(e => e.Id).ToList();
            if (orphan.Count > 0) { doc.Delete(orphan); report.Add("고르지 않은 층 동바리 중 구역이 바뀌어 번호가 없어진 " + orphan.Count + "본은 지움"); }
            report.Insert(2, "구역 " + nz + "개 (층별 방식: " + string.Join(", ", how.Select(k => k.Key + " " + k.Value + "개층")) + (merged > 0 ? ", 작은 칸 경계 " + merged + "개 합침" : "") + ")");
            if (lastZone != null && lastZone.LookupParameter("SHR_ZoneID") == null)
                report.Add("주의: 구역 번호를 적을 매개변수가 없어 구역 번호를 못 썼습니다. [매개변수 연결]을 먼저 누르세요.");
            return report;
        }
    }
}

// dynamo/01_generate.py 와 같은 로직 (C#) — 사본 모델을 백그라운드로 열어 실행·검사·저장
var app = document.Application;
string rvt = (string)parameters[0], floorsText = (string)parameters[1];
double sx = Convert.ToDouble(parameters[2]) * 1000, sy = Convert.ToDouble(parameters[3]) * 1000;
const string GEN_BY = "01_Generate"; const double SLAB_T = 210;
Func<double, double> mm = v => UnitUtils.ConvertToInternalUnits(v, UnitTypeId.Millimeters);
Func<double, double> tomm = v => UnitUtils.ConvertFromInternalUnits(v, UnitTypeId.Millimeters);
Func<double, double, double, List<double>> axis = (a0, a1, s) => { int n = (int)((a1 - a0) / s + 1e-6); return Enumerable.Range(0, n + 1).Select(i => a0 + i * s).ToList(); };
Func<double, List<double>, int> cell = (v, b) => { for (int i = 0; i < b.Count - 2; i++) if (v < b[i + 1]) return i; return b.Count - 2; };
Func<int, int, int, string> zid = (f, ix, iy) => f + "F-" + (char)(65 + ix) + (iy + 1);
Func<Element, string, object, bool> setp = (el, n, v) => { var p = el.LookupParameter(n); if (p == null || p.IsReadOnly) return false;
    if (v is string) return p.Set((string)v); if (v is int) return p.Set((int)v); return p.Set((double)v); };
var sw = System.Diagnostics.Stopwatch.StartNew();
var report = new List<string>(); var warn = new List<string>(); var check = new List<string>(); Document pd = null;
try {
    pd = app.OpenDocumentFile(rvt);
    Func<string, bool, List<double>> grid = (pre, useX) => new FilteredElementCollector(pd).OfClass(typeof(Grid)).Cast<Grid>()
        .Where(g => g.Name.Trim().ToUpper().StartsWith(pre) && g.Name.Trim().Substring(pre.Length).All(char.IsDigit))
        .OrderBy(g => int.Parse(g.Name.Trim().Substring(pre.Length))).Select(g => tomm(useX ? g.Curve.GetEndPoint(0).X : g.Curve.GetEndPoint(0).Y)).ToList();
    var xs = grid("X", true); var ys = grid("Y", false);
    var levels = new FilteredElementCollector(pd).OfClass(typeof(Level)).Cast<Level>()
        .Where(l => l.Name.StartsWith("L") && l.Name.Substring(1).All(char.IsDigit)).ToDictionary(l => int.Parse(l.Name.Substring(1)));
    var floors = floorsText.Split(',').Where(s => s.Trim() != "").Select(s => int.Parse(s.Trim())).Distinct().OrderBy(k => k).ToList();
    var sym = new FilteredElementCollector(pd).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().First(s => s.Family.Name == "SHR_PipeSupport");
    using (var t = new Transaction(pd, "01_Generate")) {
        t.Start();
        var old = new FilteredElementCollector(pd).OfCategory(BuiltInCategory.OST_GenericModel).WhereElementIsNotElementType()
            .Where(e => { var p = e.LookupParameter("SHR_GenBy"); return p != null && p.AsString() == GEN_BY; }).Select(e => e.Id).ToList();
        if (old.Count > 0) pd.Delete(old);
        report.Add("삭제 " + old.Count + "개 (이전 실행분)");
        var gm = Category.GetCategory(pd, BuiltInCategory.OST_GenericModel).Id; int nz = 0;
        foreach (var k in levels.Keys.Where(k => k >= 1).OrderBy(k => k)) {
            double top = levels[k].ProjectElevation;
            for (int ix = 0; ix < xs.Count - 1; ix++) for (int iy = 0; iy < ys.Count - 1; iy++) {
                var p = new[] { new XYZ(mm(xs[ix]), mm(ys[iy]), top - mm(SLAB_T)), new XYZ(mm(xs[ix + 1]), mm(ys[iy]), top - mm(SLAB_T)),
                                new XYZ(mm(xs[ix + 1]), mm(ys[iy + 1]), top - mm(SLAB_T)), new XYZ(mm(xs[ix]), mm(ys[iy + 1]), top - mm(SLAB_T)) };
                var loop = new CurveLoop(); for (int i = 0; i < 4; i++) loop.Append(Line.CreateBound(p[i], p[(i + 1) % 4]));
                var ds = DirectShape.CreateElement(pd, gm);
                ds.SetShape(new List<GeometryObject> { GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, mm(SLAB_T)) });
                if (!setp(ds, "SHR_ZoneID", zid(k, ix, iy)) && warn.Count == 0) warn.Add("SHR_ZoneID 를 쓸 수 없음");
                setp(ds, "SHR_Floor", k); setp(ds, "SHR_GenBy", GEN_BY); nz++;
            }
        }
        report.Add("구역 " + nz + "개");
        if (!sym.IsActive) { sym.Activate(); pd.Regenerate(); }
        var px = axis(xs[0], xs[xs.Count - 1], sx); var py = axis(ys[0], ys[ys.Count - 1], sy);
        var made = new Dictionary<int, List<FamilyInstance>>();
        foreach (var k in floors) {
            if (!levels.ContainsKey(k) || !levels.ContainsKey(k - 1)) { warn.Add(k + "F 레벨 없음"); continue; }
            var b = levels[k - 1]; double h = levels[k].ProjectElevation - mm(SLAB_T) - b.ProjectElevation;
            made[k] = new List<FamilyInstance>();
            foreach (var x in px) foreach (var y in py) {
                var fi = pd.Create.NewFamilyInstance(new XYZ(mm(x), mm(y), b.ProjectElevation), sym, b, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                foreach (var bip in new[] { BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM, BuiltInParameter.INSTANCE_ELEVATION_PARAM }) {
                    var q = fi.get_Parameter(bip); if (q != null && !q.IsReadOnly) q.Set(0.0);
                }
                if (!setp(fi, "높이", h) && made[k].Count == 0) warn.Add(k + "F '높이' 못 씀");
                setp(fi, "SHR_ZoneID", zid(k, cell(x, xs), cell(y, ys))); setp(fi, "SHR_Floor", k); setp(fi, "SHR_GenBy", GEN_BY);
                made[k].Add(fi);
            }
            report.Add(k + "F 동바리 " + made[k].Count + "본 (높이 " + Math.Round(tomm(h)) + " mm, L" + (k - 1).ToString("00") + " 위)");
        }
        pd.Regenerate();
        // 검사: 위치·높이·구역 분포 (python 코드에는 없음, MCP 검증용)
        foreach (var k in made.Keys) {
            var bb = made[k][0].get_BoundingBox(null);
            check.Add(k + "F 첫 동바리 바닥 " + Math.Round(tomm(bb.Min.Z)) + " (L" + (k - 1).ToString("00") + "=" + Math.Round(tomm(levels[k - 1].ProjectElevation)) + ") 윗면 " + Math.Round(tomm(bb.Max.Z)) + " (슬래브 밑면 " + Math.Round(tomm(levels[k].ProjectElevation) - SLAB_T) + ")");
            check.Add(k + "F 구역별 본수 " + string.Join(" ", made[k].GroupBy(f => f.LookupParameter("SHR_ZoneID").AsString()).OrderBy(g => g.Key).Select(g => g.Key.Split('-')[1] + "=" + g.Count())));
        }
        var z9 = new FilteredElementCollector(pd).OfClass(typeof(DirectShape)).Cast<Element>().FirstOrDefault(e => e.LookupParameter("SHR_ZoneID") != null && e.LookupParameter("SHR_ZoneID").AsString() == "9F-B2");
        if (z9 != null) { var bb = z9.get_BoundingBox(null); check.Add("9F-B2 구역 상자 X " + Math.Round(tomm(bb.Min.X)) + "–" + Math.Round(tomm(bb.Max.X)) + " Y " + Math.Round(tomm(bb.Min.Y)) + "–" + Math.Round(tomm(bb.Max.Y)) + " Z " + Math.Round(tomm(bb.Min.Z)) + "–" + Math.Round(tomm(bb.Max.Z)) + ", SHR_Floor=" + z9.LookupParameter("SHR_Floor").AsValueString()); }
        t.Commit();
    }
    pd.Save();
    report.Add("저장, " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s");
} catch (Exception ex) { report.Add("ERR " + (ex.InnerException ?? ex).ToString()); }
finally { if (pd != null && pd.IsValidObject) pd.Close(false); }
return new { report, warn, check };

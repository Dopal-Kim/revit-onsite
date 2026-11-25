// KBIM IFC 를 백그라운드로 열어 .rvt 저장 + 인식 결과 조사 → JSON 파일 (MCP 60 s 제한을 넘어도 결과가 남게)
var app = document.Application;
string ifc = (string)parameters[0], rvt = (string)parameters[1], outJson = (string)parameters[2];
Func<double, double> tomm = v => Math.Round(UnitUtils.ConvertFromInternalUnits(v, UnitTypeId.Millimeters));
var sw = System.Diagnostics.Stopwatch.StartNew();
var res = new Dictionary<string, object>(); Document d = null;
try {
    var opt = new Autodesk.Revit.DB.IFC.IFCImportOptions { Intent = Autodesk.Revit.DB.IFC.IFCImportIntent.Reference, AutoJoin = false, Action = Autodesk.Revit.DB.IFC.IFCImportAction.Open };   // 결합 오류·시간 방지
    d = app.OpenIFCDocument(ifc, opt);
    res["open_s"] = sw.Elapsed.TotalSeconds;
    d.SaveAs(rvt, new SaveAsOptions { OverwriteExistingFile = true });
    res["levels"] = new FilteredElementCollector(d).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.ProjectElevation).Select(l => l.Name + " @" + tomm(l.ProjectElevation)).ToList();
    res["grids"] = new FilteredElementCollector(d).OfClass(typeof(Grid)).GetElementCount();
    var all = new FilteredElementCollector(d).WhereElementIsNotElementType().Where(e => e.Category != null && e.Category.CategoryType == CategoryType.Model).ToList();
    res["by_category"] = all.GroupBy(e => e.Category.Name + " / " + e.GetType().Name).OrderByDescending(g => g.Count()).Select(g => g.Key + " = " + g.Count()).Take(40).ToList();
    var floors = all.Where(e => e.Category.Id.Value == (long)BuiltInCategory.OST_Floors).ToList();
    var f0 = floors.OrderByDescending(e => { var b = e.get_BoundingBox(null); return b == null ? 0 : (b.Max.X - b.Min.X) * (b.Max.Y - b.Min.Y); }).FirstOrDefault();
    if (f0 != null) {
        var bb = f0.get_BoundingBox(null);
        res["floor_sample"] = new { cls = f0.GetType().Name, name = f0.Name, bbox_z = tomm(bb.Min.Z) + "~" + tomm(bb.Max.Z),
            parameters = f0.Parameters.Cast<Parameter>().Where(p => p.HasValue).Select(p => p.Definition.Name + " = " + (p.AsValueString() ?? p.AsString())).OrderBy(s => s).Take(80).ToList() };
    }
    res["floor_name_counts"] = floors.GroupBy(e => e.Name).OrderByDescending(g => g.Count()).Select(g => g.Key + " = " + g.Count()).Take(25).ToList();
    var cols = all.Where(e => e.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns || e.Category.Id.Value == (long)BuiltInCategory.OST_Columns).ToList();
    if (cols.Count > 0) res["column_sample_params"] = cols[0].Parameters.Cast<Parameter>().Where(p => p.HasValue).Select(p => p.Definition.Name + " = " + (p.AsValueString() ?? p.AsString())).OrderBy(s => s).Take(50).ToList();
    res["total_s"] = sw.Elapsed.TotalSeconds;
} catch (Exception ex) { res["error"] = (ex.InnerException ?? ex).ToString(); }
finally { if (d != null && d.IsValidObject) d.Close(false); }
System.IO.File.WriteAllText(outJson, Newtonsoft.Json.JsonConvert.SerializeObject(res, Newtonsoft.Json.Formatting.Indented));
return "done " + sw.Elapsed.TotalSeconds.ToString("0") + " s";

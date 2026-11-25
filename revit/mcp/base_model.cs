// 새 프로젝트 + 레벨 L00–L15 + 구조평면 + 그리드 X1–X4·Y1–Y3 + RC 슬래브 210 × 15 → SaveAs → 화면에 열기
var app = document.Application;
string tpl = (string)parameters[0], outPath = (string)parameters[1];
Func<double, double> mm = v => UnitUtils.ConvertToInternalUnits(v, UnitTypeId.Millimeters);
Func<double, double> tomm = v => UnitUtils.ConvertFromInternalUnits(v, UnitTypeId.Millimeters);
var log = new List<string>();
// 이전 실패로 남은 이름 없는 문서(화면에 안 보임) 닫기
foreach (var d in app.Documents.Cast<Document>().Where(d => d.PathName == "" && !d.Equals(document)).ToList()) { log.Add("남은 문서 닫음: " + d.Title); d.Close(false); }
Document nd = null;
try {
nd = app.NewProjectDocument(tpl);
using (var t = new Transaction(nd, "동바리 데모 기본 모델"))
{
    t.Start();
    // 1) 레벨: 기존 레벨은 높이로 이름 정리, 없는 층 생성
    var levels = new Dictionary<int, Level>();
    var vft = new FilteredElementCollector(nd).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
        .First(v => v.ViewFamily == ViewFamily.StructuralPlan);
    var plans = new FilteredElementCollector(nd).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Where(v => !v.IsTemplate).ToList();
    foreach (var l in new FilteredElementCollector(nd).OfClass(typeof(Level)).Cast<Level>().ToList())
    {
        double e = tomm(l.Elevation);
        int k = (int)Math.Round(e / 3000.0);
        if (Math.Abs(e - k * 3000) > 1 || k < 0 || k > 15 || levels.ContainsKey(k)) { log.Add("기존 레벨 유지: " + l.Name + " @" + e); continue; }
        string old = l.Name, nn = "L" + k.ToString("00");
        l.Name = nn;
        foreach (var v in plans.Where(p => p.GenLevel != null && p.GenLevel.Id == l.Id && p.Name == old))
            try { v.Name = nn; } catch (Exception ex) { log.Add("뷰 이름 실패 " + old + ": " + ex.Message); }
        levels[k] = l;
    }
    for (int k = 0; k <= 15; k++)
    {
        if (levels.ContainsKey(k)) continue;
        var l = Level.Create(nd, mm(k * 3000));
        l.Name = "L" + k.ToString("00");
        var v = ViewPlan.Create(nd, vft.Id, l.Id);
        try { v.Name = l.Name; } catch (Exception ex) { log.Add("뷰 이름 실패 " + l.Name + ": " + ex.Message); }
        levels[k] = l;
    }
    // 2) 그리드 (원점 = X1·Y1 교점 = 내부 원점)
    double[] xv = { 0, 10000, 20000, 30000 }, yv = { 0, 9000, 18000 };
    for (int i = 0; i < xv.Length; i++)
        Grid.Create(nd, Line.CreateBound(new XYZ(mm(xv[i]), mm(-3000), 0), new XYZ(mm(xv[i]), mm(21000), 0))).Name = "X" + (i + 1);
    for (int i = 0; i < yv.Length; i++)
        Grid.Create(nd, Line.CreateBound(new XYZ(mm(-3000), mm(yv[i]), 0), new XYZ(mm(33000), mm(yv[i]), 0))).Name = "Y" + (i + 1);
    // 3) 바닥 유형 RC 슬래브 210 (단일 구조층)
    var fts = new FilteredElementCollector(nd).OfClass(typeof(FloorType)).Cast<FloorType>().ToList();
    var ft = fts.FirstOrDefault(f => f.Name == "RC 슬래브 210");
    if (ft == null)
    {
        var src = fts.Where(f => !f.IsFoundationSlab && f.GetCompoundStructure() != null)
                     .OrderByDescending(f => f.Name.Contains("콘크리트")).First();
        ft = (FloorType)src.Duplicate("RC 슬래브 210");
        var cs0 = ft.GetCompoundStructure();
        int si = cs0.StructuralMaterialIndex >= 0 ? cs0.StructuralMaterialIndex : 0;
        var cs1 = CompoundStructure.CreateSingleLayerCompoundStructure(MaterialFunctionAssignment.Structure, mm(210), cs0.GetMaterialId(si));
        cs1.EndCap = cs0.EndCap;   // 바닥 유형은 원본 단부 조건을 그대로 써야 함
        ft.SetCompoundStructure(cs1);
        log.Add("바닥 유형 복제 원본: " + src.Name);
    }
    // 4) 슬래브 L01–L15 (윗면 = 레벨)
    for (int k = 1; k <= 15; k++)
    {
        var loop = new CurveLoop();
        var p = new[] { new XYZ(0, 0, 0), new XYZ(mm(30000), 0, 0), new XYZ(mm(30000), mm(18000), 0), new XYZ(0, mm(18000), 0) };
        for (int i = 0; i < 4; i++) loop.Append(Line.CreateBound(p[i], p[(i + 1) % 4]));
        Floor.Create(nd, new List<CurveLoop> { loop }, ft.Id, levels[k].Id, true, null, 0.0);
    }
    t.Commit();
}
nd.SaveAs(outPath, new SaveAsOptions { OverwriteExistingFile = false });
var summary = new {
    levels = new FilteredElementCollector(nd).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).Select(l => l.Name + "@" + Math.Round(tomm(l.Elevation))).ToList(),
    grids = new FilteredElementCollector(nd).OfClass(typeof(Grid)).Cast<Grid>().Select(g => g.Name).OrderBy(n => n).ToList(),
    floors = new FilteredElementCollector(nd).OfClass(typeof(Floor)).Cast<Floor>().Select(f => f.Name + "/" + nd.GetElement(f.LevelId).Name).ToList(),
    plans = new FilteredElementCollector(nd).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Where(v => !v.IsTemplate).Select(v => v.ViewType + ":" + v.Name).ToList(),
    log };
nd.Close(false);
try { new UIApplication(app).OpenAndActivateDocument(outPath); log.Add("화면에 열기 성공"); }
catch (Exception ex) { log.Add("화면에 열기 실패: " + ex.Message); }
return summary;
} catch (Exception ex) {
    if (nd != null && nd.IsValidObject) nd.Close(false);
    return new { error = (ex.InnerException ?? ex).ToString(), log };
}

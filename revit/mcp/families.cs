// 패밀리 2개 생성·저장 → shoring_demo.rvt 에 백그라운드로 로드 → L01 시험 배치로 높이 확인 후 삭제 → 저장
var app = document.Application;
string rvt = (string)parameters[0], famDir = (string)parameters[1];
string rft = System.IO.Path.Combine(app.FamilyTemplatePath, "미터법 일반 모델.rft");
Func<double, double> mm = v => UnitUtils.ConvertToInternalUnits(v, UnitTypeId.Millimeters);
Func<double, double> tomm = v => UnitUtils.ConvertFromInternalUnits(v, UnitTypeId.Millimeters);
var log = new List<string>();
var opened = new List<Document>();
try {
    // 프로필을 원점 평면(참조 레벨)에 그리고 위로 돌출
    Func<Document, CurveArray, double, Extrusion> extrude = (fd, ca, h) => {
        var arr = new CurveArrArray(); arr.Append(ca);
        var sp = SketchPlane.Create(fd, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
        return fd.FamilyCreate.NewExtrusion(true, arr, sp, h);
    };
    // 1) SHR_PipeSupport: 원통 r=30, 돌출 끝 = 인스턴스 매개변수 '높이'
    string pipePath = System.IO.Path.Combine(famDir, "SHR_PipeSupport.rfa");
    var fd1 = app.NewFamilyDocument(rft); opened.Add(fd1);
    using (var t = new Transaction(fd1, "SHR_PipeSupport")) {
        t.Start();
        var fm = fd1.FamilyManager;
        if (fm.CurrentType == null) fm.NewType("기본");
        var hp = fm.AddParameter("높이", GroupTypeId.Constraints, SpecTypeId.Length, true);
        fm.Set(hp, mm(2790));
        var pl = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero);
        var ca = new CurveArray();
        ca.Append(Arc.Create(pl, mm(30), 0, Math.PI)); ca.Append(Arc.Create(pl, mm(30), Math.PI, 2 * Math.PI));
        var ext = extrude(fd1, ca, mm(2790));
        fm.AssociateElementParameterToFamilyParameter(ext.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), hp);
        t.Commit();
        // 매뉴얼 2-6 ⑧: 높이를 바꾸면 원통이 따라오는지
        foreach (var hv in new[] { 2000.0, 3000.0, 2790.0 }) {
            t.Start(); fm.Set(hp, mm(hv)); t.Commit();
            log.Add("높이 " + hv + " → 원통 윗면 " + Math.Round(tomm(ext.get_BoundingBox(null).Max.Z)));
        }
    }
    fd1.SaveAs(pipePath, new SaveAsOptions { OverwriteExistingFile = true });
    // 2) SNS_Sensor: 100×100 상자, 높이 50
    string snsPath = System.IO.Path.Combine(famDir, "SNS_Sensor.rfa");
    var fd2 = app.NewFamilyDocument(rft); opened.Add(fd2);
    using (var t = new Transaction(fd2, "SNS_Sensor")) {
        t.Start();
        if (fd2.FamilyManager.CurrentType == null) fd2.FamilyManager.NewType("기본");
        var p = new[] { new XYZ(-mm(50), -mm(50), 0), new XYZ(mm(50), -mm(50), 0), new XYZ(mm(50), mm(50), 0), new XYZ(-mm(50), mm(50), 0) };
        var ca = new CurveArray();
        for (int i = 0; i < 4; i++) ca.Append(Line.CreateBound(p[i], p[(i + 1) % 4]));
        extrude(fd2, ca, mm(50));
        t.Commit();
    }
    fd2.SaveAs(snsPath, new SaveAsOptions { OverwriteExistingFile = true });
    // 3) 프로젝트에 로드 + 시험 배치
    var pd = app.OpenDocumentFile(rvt); opened.Add(pd);
    using (var t = new Transaction(pd, "패밀리 로드")) {
        t.Start();
        foreach (var fp in new[] { pipePath, snsPath }) {
            Family fam; bool ok = pd.LoadFamily(fp, out fam);
            log.Add("로드 " + System.IO.Path.GetFileName(fp) + ": " + ok);
        }
        // 해석 평면 뷰 이름도 L00/L01 로 정리
        foreach (var v in new FilteredElementCollector(pd).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().Where(v => v.Name.StartsWith("레벨 ")).ToList())
            try { v.Name = v.Name.Replace("레벨 1", "L00").Replace("레벨 2", "L01"); } catch (Exception ex) { log.Add("뷰 이름 " + v.Name + ": " + ex.Message); }
        var sym = new FilteredElementCollector(pd).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().First(s => s.Family.Name == "SHR_PipeSupport");
        if (!sym.IsActive) { sym.Activate(); pd.Regenerate(); }
        var l1 = new FilteredElementCollector(pd).OfClass(typeof(Level)).Cast<Level>().First(l => l.Name == "L01");
        var fi = pd.Create.NewFamilyInstance(new XYZ(mm(5000), mm(4500), l1.Elevation), sym, l1, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
        pd.Regenerate();
        var bb = fi.get_BoundingBox(null);
        log.Add("시험 배치 L01: 바닥 " + Math.Round(tomm(bb.Min.Z)) + " / 윗면 " + Math.Round(tomm(bb.Max.Z)) + " (기대 3000 / 5790 = L02 슬래브 밑면), 높이 매개변수 " + Math.Round(tomm(fi.LookupParameter("높이").AsDouble())));
        pd.Delete(fi.Id);
        t.Commit();
    }
    pd.Save();
    log.Add("저장 완료");
} catch (Exception ex) { log.Add("ERR " + (ex.InnerException ?? ex).ToString()); }
finally { foreach (var d in opened) if (d.IsValidObject) d.Close(false); }
return log;

// N3 진단 (사본에서만, 화면만 — 모델 안 바꿈): 4D 미리보기 뷰 창을 닫았다가 다시 열어 Revit 이 그 뷰를 새로 그리게 함
// 다른 열린 뷰(없으면 검측 뷰)를 먼저 활성으로 → 미리보기 UIView.Close() → 다시 ActiveView = 미리보기 → RefreshActiveView
var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "RevitOnSite");
var F = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
Action<UIApplication> act = app =>
{
    var ui = app.ActiveUIDocument;
    var doc = ui.Document;
    var pv = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().First(v => !v.IsTemplate && v.Name == "Revit On-Site 4D 미리보기");
    var other = ui.GetOpenUIViews().Select(u => doc.GetElement(u.ViewId) as View).FirstOrDefault(v => v != null && v.Id != pv.Id)
        ?? new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().First(v => !v.IsTemplate && v.Id != pv.Id);
    if (ui.ActiveView.Id == pv.Id) ui.ActiveView = other;
    ui.GetOpenUIViews().FirstOrDefault(u => u.ViewId == pv.Id)?.Close();
    ui.ActiveView = pv;
    ui.RefreshActiveView();
};
asm.GetType("RevitOnSite.Dashboard").GetMethod("Request", F).Invoke(null, new object[] { "4D 뷰 다시 열기 시험", act });
return "예약: 다시 열기";

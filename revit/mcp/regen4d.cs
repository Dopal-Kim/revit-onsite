// N3 진단 (사본에서만): parameters[0] = shutdown → 4D 그리기 서버를 뺌 (Timeline.Shutdown, 다음 프레임에서 새로 등록)
//                                    = rollback → 미리보기 뷰 표시 방식을 바꿨다가 트랜잭션 그룹을 되돌림 (실행 취소 기록 없음) — 뷰를 다시 계산하게 하는지 시험
var arg = parameters.Length > 0 ? (string)parameters[0] : "rollback";
var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "RevitOnSite");
var F = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
Action<UIApplication> act = app =>
{
    if (arg == "shutdown") { asm.GetType("RevitOnSite.Timeline").GetMethod("Shutdown", F).Invoke(null, new object[] { null }); return; }
    var ui = app.ActiveUIDocument;
    var doc = ui.Document;
    var pv = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().First(v => !v.IsTemplate && v.Name == "Revit On-Site 4D 미리보기");
    using (var tg = new TransactionGroup(doc, "4D 다시 그리기 시험"))
    {
        tg.Start();
        using (var t = new Transaction(doc, "표시 방식 잠깐"))
        {
            t.Start();
            pv.DisplayStyle = pv.DisplayStyle == DisplayStyle.Shading ? DisplayStyle.Realistic : DisplayStyle.Shading;
            t.Commit();
        }
        tg.RollBack();
    }
    ui.RefreshActiveView();
};
asm.GetType("RevitOnSite.Dashboard").GetMethod("Request", F).Invoke(null, new object[] { "4D 시험 " + arg, act });
return "예약: " + arg;

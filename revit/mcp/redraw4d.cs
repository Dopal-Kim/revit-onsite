// 4D 미리보기 다시 그리기 방법 시험 (화면만, 모델 안 바꿈 — N3 진단용, 사본에서만): parameters[0] = refresh | update | toggle
// ExternalEvent(Dashboard.Request) 안에서 실행 → 그 뒤 capture.ps1 로 화면 확인
var how = parameters.Length > 0 ? (string)parameters[0] : "refresh";
var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "RevitOnSite");
var F = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
Action<UIApplication> act = app =>
{
    var ui = app.ActiveUIDocument;
    if (how == "update") ui.UpdateAllOpenViews();
    else if (how == "toggle")
    {
        var svc = (Autodesk.Revit.DB.ExternalService.MultiServerService)Autodesk.Revit.DB.ExternalService.ExternalServiceRegistry.GetService(Autodesk.Revit.DB.ExternalService.ExternalServices.BuiltInExternalServices.DirectContext3DService);
        var sid = new Guid("3C1B8E47-6D2A-4F59-9B0E-7A41C5D2E8F3");
        var ids = svc.GetActiveServerIds();
        ids.Remove(sid);
        svc.SetActiveServers(ids);
        ids.Add(sid);
        svc.SetActiveServers(ids);
        ui.RefreshActiveView();
    }
    else ui.RefreshActiveView();
};
asm.GetType("RevitOnSite.Dashboard").GetMethod("Request", F).Invoke(null, new object[] { "4D 다시 그리기 시험 " + how, act });
return "예약: " + how;

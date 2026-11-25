// 4D 미리보기 그리기 서버 상태 (읽기만 — N3 "재생 중 건물 안 보임" 진단용): 서버 등록·활성, 상태 수, 실패 표시, 묶음·버퍼, 미리보기 뷰 표시 방식·단면 상자
var outp = new List<string>();
try
{
    var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "RevitOnSite");
    var F = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
    var gT = asm.GetType("RevitOnSite.FourDGraphics");
    var g = gT.GetField("Instance", F).GetValue(null);
    var states = gT.GetField("states", F).GetValue(g) as System.Collections.IDictionary;
    var meshes = gT.GetField("meshes", F).GetValue(g) as System.Collections.IDictionary;
    outp.Add("states " + (states == null ? "null" : states.Count.ToString()) + " · meshes " + (meshes == null ? "null" : meshes.Count.ToString())
        + " · failed " + gT.GetField("failed", F).GetValue(g) + " · viewId " + gT.GetField("viewId", F).GetValue(g) + " · registered " + gT.GetField("registered", F).GetValue(null));
    int vis = 0, withMesh = 0, built = 0, chunks = 0;
    if (states != null)
        foreach (System.Collections.DictionaryEntry kv in states)
        {
            var s = kv.Value;
            var vp = s.GetType().GetProperty("Visible");
            var vfi = s.GetType().GetField("Visible");
            object vv = vp != null ? vp.GetValue(s) : vfi?.GetValue(s);
            if (vv is bool b && b) vis++;
            if (meshes != null && meshes.Contains(kv.Key)) withMesh++;
        }
    outp.Add("보임 " + vis + " · 형상 있는 상태 " + withMesh);
    if (meshes != null)
        foreach (System.Collections.DictionaryEntry kv in meshes)
            foreach (var c in (System.Collections.IEnumerable)kv.Value)
            {
                chunks++;
                var vb = c.GetType().GetField("Vb").GetValue(c) as Autodesk.Revit.DB.DirectContext3D.VertexBuffer;
                if (vb != null && vb.IsValid()) built++;
            }
    outp.Add("chunks " + chunks + " · 버퍼 만든 것 " + built);
    var svc = (Autodesk.Revit.DB.ExternalService.MultiServerService)Autodesk.Revit.DB.ExternalService.ExternalServiceRegistry.GetService(Autodesk.Revit.DB.ExternalService.ExternalServices.BuiltInExternalServices.DirectContext3DService);
    var sid = new Guid("3C1B8E47-6D2A-4F59-9B0E-7A41C5D2E8F3");
    outp.Add("서비스 등록 " + svc.IsRegisteredServerId(sid) + " · 활성 " + svc.GetActiveServerIds().Contains(sid));
    var pv = new FilteredElementCollector(document).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(v => !v.IsTemplate && v.Name == "Revit On-Site 4D 미리보기");
    outp.Add("미리보기 뷰 " + (pv == null ? "없음" : pv.Id + " · " + pv.DisplayStyle + " · 단면 상자 " + pv.IsSectionBoxActive));
    var dash = asm.GetType("RevitOnSite.Dashboard");
    outp.Add("재생 중 " + dash.GetField("playing", F).GetValue(null) + " · frameBusy " + dash.GetField("frameBusy", F).GetValue(null));
}
catch (Exception ex) { outp.Add("오류: " + (ex.InnerException ?? ex)); }
return outp;

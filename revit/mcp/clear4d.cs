// N3 진단 (사본에서만, 화면만): parameters[0] = clear → 4D 그림 상태를 비움(멈춤과 같음) / style:<FlatColors|Shading|Realistic> → 미리보기 뷰 표시 방식만 바꿈(뷰 다시 계산)
var arg = parameters.Length > 0 ? (string)parameters[0] : "clear";
if (arg.StartsWith("style:"))
{
    var pv = new FilteredElementCollector(document).OfClass(typeof(View3D)).Cast<View3D>().First(v => !v.IsTemplate && v.Name == "Revit On-Site 4D 미리보기");
    pv.DisplayStyle = (DisplayStyle)Enum.Parse(typeof(DisplayStyle), arg.Substring(6));
    return "표시 " + pv.DisplayStyle;
}
var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "RevitOnSite");
var F = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var g = asm.GetType("RevitOnSite.FourDGraphics").GetField("Instance", F).GetValue(null);
Action<UIApplication> act = app =>
{
    g.GetType().GetMethod("Clear").Invoke(g, null);
    app.ActiveUIDocument.RefreshActiveView();
};
asm.GetType("RevitOnSite.Dashboard").GetMethod("Request", F).Invoke(null, new object[] { "4D 비우기 시험", act });
return "예약: 비우기";

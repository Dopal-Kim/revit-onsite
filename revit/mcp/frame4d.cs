// 4D 미리보기 한 프레임을 대시보드와 같은 경로(ExternalEvent → Timeline.Frame)로 그리게 예약 — 캡처로 색을 확인하는 개발용 (사본에서만).
// MCP 코드는 열린 트랜잭션 안이라 Timeline 을 직접 부르지 않고 Dashboard.Request 로 넘긴다 (MCP 가 끝난 뒤 Revit 이 한가할 때 실행).
// parameters: [0] 시각 "yyyy-MM-ddTHH:mm", [1] 미리보기 뷰 표시 방식 FlatColors|Shading|Realistic (빈 값 = 그대로)
// 매번 따라가기를 다시 켜서 첫 화면 = 건물 전체로 확대 (카메라 수정 확인 겸)
var t = DateTime.ParseExact((string)parameters[0], "yyyy-MM-dd'T'HH:mm", System.Globalization.CultureInfo.InvariantCulture);
var style = parameters.Length > 1 ? (string)parameters[1] : "";
var pv = new FilteredElementCollector(document).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(v => !v.IsTemplate && v.Name == "Revit On-Site 4D 미리보기");
string note = pv == null ? "미리보기 뷰 없음 (이 프레임에서 만듦 — 표시 방식은 다음 호출에서)" : "";
if (pv != null && style.Length > 0) { pv.DisplayStyle = (DisplayStyle)Enum.Parse(typeof(DisplayStyle), style); note = "표시 " + pv.DisplayStyle; }
var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "RevitOnSite");
var F = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var tl = asm.GetType("RevitOnSite.Timeline");
var frame = tl.GetMethod("Frame", F, null, new[] { typeof(UIApplication), typeof(DateTime), typeof(bool) }, null);
var follow = tl.GetMethod("SetFollow", F);
Action<UIApplication> act = app =>
{
    follow.Invoke(null, new object[] { true });
    frame.Invoke(null, new object[] { app, t, true });   // open = 4D 뷰를 연다
};
asm.GetType("RevitOnSite.Dashboard").GetMethod("Request", F).Invoke(null, new object[] { "4D 캡처 " + t.ToString("yyyy-MM-dd HH:mm"), act });
return "예약: " + t.ToString("yyyy-MM-dd HH:mm") + " · " + note + " · 문서 " + document.Title;

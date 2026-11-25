// 대시보드 창 상태 (읽기만 — N11 진단): 마우스 캡처·키보드 포커스·[판정] 버튼 사용 가능·대기 요청 수
var outp = new List<string>();
try
{
    var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "RevitOnSite");
    var F = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
    var dash = asm.GetType("RevitOnSite.Dashboard");
    var pane = dash.GetField("pane", F).GetValue(null) as System.Windows.FrameworkElement;
    var todo = dash.GetField("todo", F).GetValue(null) as System.Collections.ICollection;
    outp.Add("pane " + (pane == null ? "null" : $"IsEnabled {pane.IsEnabled} · IsVisible {pane.IsVisible} · IsLoaded {pane.IsLoaded}") + " · 대기 요청 " + (todo?.Count ?? -1)
        + " · evt " + (dash.GetField("evt", F).GetValue(null) == null ? "null" : "있음"));
    var cap = System.Windows.Input.Mouse.Captured as System.Windows.DependencyObject;
    outp.Add("마우스 캡처 " + (cap == null ? "없음" : cap.GetType().Name + " " + ((cap as System.Windows.FrameworkElement)?.Name ?? "") + " " + ((cap as System.Windows.Controls.ContentControl)?.Content as string ?? "")));
    var foc = System.Windows.Input.Keyboard.FocusedElement as System.Windows.DependencyObject;
    outp.Add("키보드 포커스 " + (foc == null ? "없음" : foc.GetType().Name + " " + ((foc as System.Windows.Controls.ContentControl)?.Content as string ?? "")));
    if (pane != null)
    {
        var stack = new Stack<System.Windows.DependencyObject>();
        stack.Push(pane);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (d is System.Windows.Controls.Button b && b.Content is string s && (s == "판정" || s == "오늘" || s == "같은 층만" || s == "전체 층"))
                outp.Add($"버튼 {s}: IsEnabled {b.IsEnabled} · IsHitTestVisible {b.IsHitTestVisible} · IsVisible {b.IsVisible} · IsPressed {b.IsPressed} · IsMouseCaptured {b.IsMouseCaptured}");
            if (d is System.Windows.Controls.DatePicker dp) outp.Add($"기준일 칸: SelectedDate {dp.SelectedDate:yyyy-MM-dd} · Text '{dp.Text}' · IsDropDownOpen {dp.IsDropDownOpen}");
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++) stack.Push(System.Windows.Media.VisualTreeHelper.GetChild(d, i));
        }
    }
    foreach (System.Windows.Window w in System.Windows.Application.Current?.Windows ?? new System.Windows.WindowCollection())
        outp.Add($"WPF 창: {w.Title} · IsVisible {w.IsVisible} · IsEnabled {w.IsEnabled}");
}
catch (Exception ex) { outp.Add("오류: " + (ex.InnerException ?? ex)); }
return outp;

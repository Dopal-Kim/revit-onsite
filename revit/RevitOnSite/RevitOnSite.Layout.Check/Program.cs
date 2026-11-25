// 85차 레이아웃 시험 → 11-12 2-5 (Revit #4: 첫 화면 = 오늘 볼 것 → 칩 → 층 × 구역, 강도 그림은 접힌 채): 애드인 Wpf\DashboardPane.xaml 을 그대로 읽어 (x:Class·이벤트 이름만 뺌)
// 화면 밖 창에 띄우고, 창 높이 400·598·800·1080 px × 폭 300·420 × [색] 3가지 × 오늘 볼 것 펼침/접힘에서
//   ① 순서 오늘 볼 것 < 칩 < 범례 < 층 × 구역 머리 < 강도 타임라인 ② 오늘 볼 것 접힘이면 층 × 구역 머리(창 400)·첫 줄(창 598 이상)이 첫 화면(스크롤 0) 안
//   ③ 강도 타임라인은 접힌 채 시작, 펼치면 그림 높이 160 그대로 ④ 지표 줄은 줄바꿈(잘림 없음).
// 데이터는 가짜로 가장 큰 경우를 채움: 층 줄 13개 + 펼친 층 줄 9개, 범례 칩, 그날 줄·안내 줄 두 줄, 지표·칩·안내, 격자 15층, 경고 26줄. 실패면 종료 코드 1
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;

static class Program
{
    [STAThread]
    static int Main()
    {
        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var path = Find(Path.Combine("revit", "RevitOnSite", "RevitOnSite", "Wpf", "DashboardPane.xaml"));
        var xaml = Regex.Replace(File.ReadAllText(path), @"\sx:Class=""[^""]*""", "");
        xaml = Regex.Replace(xaml, @"\s(?:Click|MouseDoubleClick|PreviewMouse\w+|SelectionChanged|SizeChanged|ValueChanged|SelectedDateChanged|TextChanged)=""[^""]*""", "");
        int fails = 0, n = 0;
        foreach (var h in new[] { 400, 598, 800, 1080 })
            foreach (var w in new[] { 300, 420 })
                foreach (var color in new[] { 0, 1, 2 })
                    foreach (var open in new[] { false, true })
                    {
                        var pane = (UserControl)XamlReader.Parse(xaml);
                        Fill(pane, color, open);
                        var win = new Window { Content = pane, Width = w, Height = h, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                                               Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false };
                        win.Show();
                        win.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);   // 배치가 끝날 때까지
                        var sv = (ScrollViewer)pane.FindName("PageScroll");
                        sv.ScrollToVerticalOffset(0);
                        pane.UpdateLayout();
                        double Y(string name) => ((FrameworkElement)pane.FindName(name)).TranslatePoint(new Point(0, 0), sv).Y;
                        var tl = (Expander)pane.FindName("TimelineBox");
                        double alert = Y("AlertBox"), chips = Y("Chips"), legend = Y("Legend"), head = Y("MatrixHeader"), grid = Y("Matrix"), timeline = Y("TimelineBox");
                        bool order = alert < chips && chips < legend && legend < head && head < grid && grid < timeline;
                        double need = h >= 598 ? grid + 30 + 36 : head + 20;   // 598 이상: 격자 머리글 + 첫 줄, 400: 층 × 구역 머리 띠
                        bool first = open || need <= sv.ViewportHeight + 0.5;
                        bool closed = !tl.IsExpanded;
                        var kpi = (TextBlock)pane.FindName("KpiLine");
                        bool wrap = kpi.TextWrapping == TextWrapping.Wrap && kpi.ActualWidth <= sv.ViewportWidth + 0.5;
                        tl.IsExpanded = true;
                        pane.UpdateLayout();
                        var chart = (FrameworkElement)pane.FindName("ProcessChart");
                        bool chartOk = Math.Abs(chart.ActualHeight - 160) < 0.5;
                        bool ok = order && first && closed && wrap && chartOk;
                        if (!ok || h == 400 || h == 598)
                            Console.WriteLine($"{(ok ? "OK" : "NG")} 창 {h}×{w} · 색 {color} · 오늘 볼 것 {(open ? "펼침" : "접힘")}: 순서 {(order ? "맞음" : "틀림")} (볼 것 {alert:0} · 칩 {chips:0} · 범례 {legend:0} · 격자 {head:0}/{grid:0} · 타임라인 {timeline:0})"
                                              + $" · 첫 화면 {sv.ViewportHeight:0} 필요 {need:0}{(first ? "" : " 넘침")} · 타임라인 {(closed ? "접힘 시작" : "펼친 채 시작")} · 지표 {(wrap ? "줄바꿈" : "잘림")} · 그림 {chart.ActualHeight:0}");
                        win.Close();
                        n++;
                        if (!ok) fails++;
                    }
        Console.WriteLine(fails == 0 ? $"레이아웃 시험 통과 ({n}가지)" : $"레이아웃 시험 실패 {fails}/{n}");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>현재 폴더·실행 파일 위로 올라가며 저장소 안의 파일을 찾음</summary>
    static string Find(string rel)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, rel))) return Path.Combine(d.FullName, rel);
        throw new FileNotFoundException(rel);
    }

    static T Get<T>(FrameworkElement root, string name) where T : class => root.FindName(name) as T ?? throw new InvalidOperationException("XAML 에 " + name + " 없음");

    /// <summary>가장 큰 경우의 가짜 내용 (DashboardPane.Load·BuildFloors·RebuildLegend 가 채우는 칸들)</summary>
    static void Fill(UserControl p, int color, bool open)
    {
        var alert = Get<Expander>(p, "AlertBox");
        alert.Visibility = Visibility.Visible;
        Get<TextBlock>(p, "AlertLine").Text = "오늘 볼 것 — ■ 무단 해체 의심 1 · ▲ 타설 보류 1 · ▲ 공시체 미입력 (예측일 지남) 6 · 요청 가능 4";
        alert.IsExpanded = open;
        Get<Border>(p, "FloorScroll").Visibility = Visibility.Visible;
        var floors = Get<StackPanel>(p, "FloorList");
        for (int f = 13; f >= 1; f--)
        {
            floors.Children.Add(new Button { Content = new TextBlock { Text = $"{f}F {(f == 9 ? "▾" : "▸")} ▲ 공시체 시험 필요 2 · 요청 가능 4 · 존치 2", TextWrapping = TextWrapping.Wrap }, HorizontalContentAlignment = HorizontalAlignment.Left });
            if (f == 9) for (int k = 0; k < 9; k++) floors.Children.Add(new Button { Content = new TextBlock { Text = $"● [요청 가능] 9F-A{k + 1} — 검측요청서 차례" }, Margin = new Thickness(14, 0, 0, 1) });
        }
        var kpi = Get<TextBlock>(p, "KpiLine");
        kpi.Text = "지표 — 해체 앞당김 3일 · 공시체 6건 · 센서 12개 · 설치 검측 100 % · 감리 확인 시간 (요청 → 감리 승인): 중앙 1.5일 · 최대 4일 (8건)";
        kpi.Visibility = Visibility.Visible;
        Get<TextBlock>(p, "ChipReady").Text = "해체 승인 요청 가능 4구역";
        Get<TextBlock>(p, "ChipKeep").Text = "존치 80구역 (설치 결함 1)";
        Get<TextBlock>(p, "ChipCyl").Text = "공시체 시험 필요 6구역";
        var info = Get<TextBlock>(p, "Info");
        info.Text = "판정 기준일 2026-12-31 — 데이터 폴더 C:\\…\\onsite_data_demo";
        info.Visibility = Visibility.Visible;
        Get<ComboBox>(p, "ColorMode").SelectedIndex = color;
        var legend = Get<WrapPanel>(p, "Legend");
        var chips = color switch
        {
            0 => new[] { "40 % 미만", "40–70 %", "70–100 %", "100 % 이상", "양생 중", "타설 전" },
            1 => Enumerable.Range(0, 9).Select(i => $"{i} " + new[] { "없음", "설치검측", "타설", "공시체", "센서", "요청", "승인", "해체", "재받침" }[i]).ToArray(),
            _ => new[] { "설치 전", "설치 검측 완료", "타설 중", "양생 중", "해체 요청 가능", "감리 승인 기록", "해체 완료", "재받침", "결함 기록", "무단 해체 의심" },
        };
        foreach (var c in chips) legend.Children.Add(new Border { Margin = new Thickness(0, 0, 6, 2), Child = new TextBlock { Text = "■ " + c } });
        Get<TextBlock>(p, "DoingText").Text = "12-31 13F 타설 보류 · 12F 추정 18.2 MPa · 11F 24.6 MPa · 10F 27.1 MPa — 공시체 시험 9F 2건 (현장양생 25.2 MPa ≥ 24)";
        var note = Get<TextBlock>(p, "TimelineNoteText");
        note.Text = "12-31 사건 — 13F 타설 · 12F 추정 14 MPa 도달 · 9F 공시체 시험 (긴 안내는 두 줄까지, 나머지는 툴팁)";
        note.Visibility = Visibility.Visible;
        Get<FrameworkElement>(p, "ProcessChart").Visibility = Visibility.Visible;
        var grid = Get<DataGrid>(p, "Matrix");
        grid.RowHeight = 36;
        grid.ItemsSource = Enumerable.Range(1, 15).Reverse().Select(f => new { 층 = $"{f}F", A1 = "존치", A2 = "요청", B1 = "공시", B2 = "양생" }).ToList();
        Get<DataGrid>(p, "Warnings").ItemsSource = Enumerable.Range(1, 26).Select(i => $"경고 {i} — 12F-A{i % 4 + 1} 존치 연장: 위로 2개층만 타설됨 (타설층 포함 3개층 존치 필요)").ToList();
    }
}

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OnSitePreview;

// 화면에 창을 남기지 않고 화면 밖에서 잠깐 띄워 RenderTargetBitmap 으로 PNG 를 만든다.
static class Program
{
    // 참고 캡처(revit/screens)가 125 % 배율이라 같은 배율로 그려 1:1 비교한다.
    const double Scale = 1.25;
    static readonly string Out = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\out"));

    [STAThread]
    static void Main()
    {
        _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Directory.CreateDirectory(Out);

        RenderDialog("dialog_setup.png", invalid: false);
        RenderDialog("dialog_setup_invalid.png", invalid: true);
        RenderDashboard("dashboard_light.png", dark: false);
        RenderDashboard("dashboard_dark.png", dark: true);
        RenderDashboard("dashboard_dark_short.png", dark: true, height: 460); // 창이 낮을 때 스크롤바 확인
        RenderStates("controls_states.png");
        Console.WriteLine("PNG → " + Out);
    }

    // 체크 상자·꺼진 컨트롤 상태 확인용 (대화상자 라이트 | 도킹 창 다크)
    static void RenderStates(string file)
    {
        FrameworkElement Sheet(string dict)
        {
            var root = new Border { Padding = new Thickness(12) };
            root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"/OnSitePreview;component/wpf/{dict}", UriKind.Relative) });
            root.SetResourceReference(Border.BackgroundProperty, "OnSite.Brush.Background");
            var p = new StackPanel();
            p.SetResourceReference(TextElement.ForegroundProperty, "OnSite.Brush.Foreground");
            p.Children.Add(new CheckBox { Content = "켜짐", IsChecked = true, Margin = new Thickness(0, 0, 0, 6) });
            p.Children.Add(new CheckBox { Content = "꺼짐", Margin = new Thickness(0, 0, 0, 6) });
            p.Children.Add(new CheckBox { Content = "사용 불가", IsEnabled = false, Margin = new Thickness(0, 0, 0, 6) });
            p.Children.Add(new TextBox { Text = "사용 불가 칸", IsEnabled = false, Width = 140, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left });
            p.Children.Add(new ComboBox { ItemsSource = new[] { "사용 불가" }, SelectedIndex = 0, IsEnabled = false, Width = 140, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Left });
            p.Children.Add(new Button { Content = "적용", IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Left });
            root.Child = p;
            return root;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, UseLayoutRounding = true };
        TextOptions.SetTextFormattingMode(row, TextFormattingMode.Display);
        row.SetValue(TextElement.FontFamilyProperty, new FontFamily("Segoe UI, Malgun Gothic"));
        row.SetValue(TextElement.FontSizeProperty, 12.0);
        row.Children.Add(Sheet("OnSiteDialog.xaml"));
        row.Children.Add(Sheet("OnSiteTheme.Dark.xaml"));
        var host = new Window { Content = row, SizeToContent = SizeToContent.WidthAndHeight, WindowStyle = WindowStyle.None,
                                Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false };
        host.Show(); Pump();
        Save(file, row, 0, (_, _) => { }, null);
        host.Close();
    }

    static void RenderDialog(string file, bool invalid)
    {
        var w = new ProjectSetupWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowActivated = false };
        if (invalid) w.ShowInvalidSample();
        w.Show(); Pump();
        var content = (FrameworkElement)w.Content;
        // 창 테두리·제목줄은 OS 가 그리므로 흉내만 낸다 (Windows 11, 03_open_dialog.png 의 #F3F3F3 제목줄)
        Save(file, content, 30, (dc, width) =>
        {
            dc.DrawRectangle(Hex("#F3F3F3"), null, new Rect(0, 0, width, 30));
            dc.DrawText(Text(w.Title, 12, "#000000"), new Point(12, 8));
            dc.DrawText(Text("✕", 12, "#000000"), new Point(width - 30, 8));
        }, Hex("#9A9A9A"));
        w.Close();
    }

    static void RenderDashboard(string file, bool dark, double height = double.NaN)
    {
        var pane = new DashboardPane { Height = height };
        pane.ApplyTheme(dark);
        pane.Load(SampleFloors(), ["A", "B", "C", "D", "E", "F"],
        [
            "2026-12-10 에 동바리 5개층분 동시 필요 (보유 4개층분)",
            "11층 D구역 설치 검측 불합격 — 수평연결재 누락",
            "7층 A·B구역 공시체 결과 없음 — 시험 필요",
        ]);
        var host = new Window { Content = pane, SizeToContent = SizeToContent.WidthAndHeight, WindowStyle = WindowStyle.None,
                                Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false };
        host.Show(); Pump();
        pane.Matrix.SelectedIndex = 5;     // 10층 선택 → 층 칸이 #2B7BB3
        pane.Warnings.SelectedIndex = 1;
        Pump();
        // 도킹 창 제목 띠("Revit On-Site" ✕)는 Revit 이 그린다 — 02_addins_tab.png 의 "특성" 띠를 흉내
        var titleBg = dark ? "#222933" : "#E5E5E5";
        var titleFg = dark ? "#FFFFFF" : "#000000";
        Save(file, pane, 20, (dc, width) =>
        {
            dc.DrawRectangle(Hex(titleBg), null, new Rect(0, 0, width, 20));
            dc.DrawText(Text("Revit On-Site", 12, titleFg), new Point(4, 2));
            dc.DrawText(Text("✕", 12, titleFg), new Point(width - 16, 2));
        }, null);
        host.Close();
    }

    // 15층 × 6구역 예시: 첫 타설 10-01, 7일 사이클, 기준일 12-03 (10층 타설일)
    static List<(string, (string, string)[])> SampleFloors()
    {
        var first = new DateTime(2026, 10, 1);
        var rows = new List<(string, (string, string)[])>();
        for (int f = 15; f >= 1; f--)
        {
            var zones = new (string, string)[6];
            for (int z = 0; z < 6; z++)
            {
                var pour = first.AddDays((f - 1) * 7 + (z >= 3 ? 1 : 0));
                string s = f switch
                {
                    >= 12 => "Unchecked",
                    11 => z == 3 ? "Defect" : "Unchecked",
                    10 or 9 => "Curing",
                    8 => z < 4 ? "Extended" : "Curing",
                    7 => z < 2 ? "NeedsCylinder" : "ReadyToRequest",
                    6 => z < 4 ? "ReadyToRequest" : z == 4 ? "NeedsCylinder" : "Extended",
                    5 => z < 4 ? "Approved" : "ReadyToRequest",
                    _ => "Approved",
                };
                var removal = pour.AddDays(s == "Extended" ? 31 : 24);
                zones[z] = (s, removal.ToString("MM-dd"));
            }
            rows.Add(($"{f}층", zones));
        }
        return rows;
    }

    static void Save(string file, FrameworkElement el, double top, Action<DrawingContext, double> chrome, Brush frame)
    {
        double w = el.ActualWidth, h = el.ActualHeight + top;
        var dv = new DrawingVisual();
        TextOptions.SetTextFormattingMode(dv, TextFormattingMode.Display);
        using (var dc = dv.RenderOpen())
        {
            chrome(dc, w);
            dc.DrawRectangle(new VisualBrush(el) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                             null, new Rect(0, top, el.ActualWidth, el.ActualHeight));
            if (frame != null) dc.DrawRectangle(null, new Pen(frame, 1 / Scale), new Rect(0.5 / Scale, 0.5 / Scale, w - 1 / Scale, h - 1 / Scale));
        }
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(w * Scale), (int)Math.Ceiling(h * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bmp.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(Out, file));
        enc.Save(fs);
        Console.WriteLine($"{file}: {bmp.PixelWidth}x{bmp.PixelHeight}");
    }

    // 같은 스레드에서 낮은 우선순위 작업을 Invoke → 레이아웃·Loaded·렌더 대기열이 먼저 처리된다
    static void Pump() => Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, () => { });

    static SolidColorBrush Hex(string c) => (SolidColorBrush)new BrushConverter().ConvertFromString(c);

    static FormattedText Text(string s, double size, string color) =>
        new(s, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI, Malgun Gothic"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size, Hex(color), null, TextFormattingMode.Display, Scale);
}

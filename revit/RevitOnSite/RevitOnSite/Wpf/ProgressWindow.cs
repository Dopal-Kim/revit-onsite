// 오래 걸리는 쓰기(날짜별 뷰 저장)의 진행 창 — 자기 STA 스레드·Dispatcher 에서 돈다.
// Revit 주 스레드는 트랜잭션을 계속하고 진행만 BeginInvoke 로 보냄 (주 스레드에서 메시지를 돌리지 않으므로 Revit 에 다시 들어올 일이 없다).
// 취소 단추는 없음: 트랜잭션 도중 멈추면 반쯤 만든 뷰가 남으므로 끝까지 가거나 Revit 오류로 통째로 되돌림 (App.Write)
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RevitOnSite
{
    sealed class ProgressWindow : IDisposable
    {
        Dispatcher ui;
        Window win;
        TextBlock text;
        ProgressBar bar;
        readonly ManualResetEventSlim ready = new();

        public ProgressWindow(string title, int total)
        {
            var t = new Thread(() =>
            {
                text = new TextBlock { Margin = new Thickness(0, 0, 0, 8), Text = "준비 중…" };
                bar = new ProgressBar { Height = 14, Minimum = 0, Maximum = Math.Max(1, total) };
                var w = win = new Window
                {
                    Title = title, Width = 360, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow,
                    Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)), FontFamily = new FontFamily("Segoe UI, Malgun Gothic"), FontSize = 12,
                    Content = new StackPanel { Margin = new Thickness(12), Children = { text, bar } },
                };
                ui = Dispatcher.CurrentDispatcher;
                w.Closed += (s, e) => ui.InvokeShutdown();
                w.Show();
                ready.Set();
                Dispatcher.Run();
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            ready.Wait(TimeSpan.FromSeconds(5));   // 창이 못 뜨면 진행 표시 없이 계속
        }

        /// <summary>진행 보고 (주 스레드에서 불러도 기다리지 않음)</summary>
        public void Report(int done, string message) =>
            ui?.BeginInvoke(() => { bar.Value = done; text.Text = message; });

        public void Dispose() => ui?.BeginInvoke(() => win?.Close());   // 창을 닫으면 그 스레드의 Dispatcher 도 끝남
    }
}

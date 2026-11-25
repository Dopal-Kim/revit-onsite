// 결과 창 (모달 아님, 하나만) — App.Result 가 부른다. 내용은 글뿐이라 Revit API 를 부르지 않음 → 모달이 아니어도 안전 (현장 입력 창과 같은 방식)
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace RevitOnSite
{
    public partial class ResultWindow : Window
    {
        static ResultWindow open;

        ResultWindow() => InitializeComponent();

        /// <summary>결과를 보여 줌: 열린 창이 있으면 내용을 바꾸고 앞으로(활성화하지 않음), 없으면 Revit 주 창 가운데에 새로. what = 어느 명령의 결과인지</summary>
        Action action;

        /// <summary>action = 결과 창 버튼 하나 (글자, 누르면 할 일 — Revit API 는 Dashboard.Request 로). null 이면 버튼 없음</summary>
        internal static void Present(IntPtr owner, string what, string summary, string body, (string Label, Action Run)? action = null)
        {
            if (open == null)
            {
                open = new ResultWindow();
                new WindowInteropHelper(open).Owner = owner;
                open.Closed += (s, e) => open = null;
            }
            open.SummaryText.Text = summary;
            open.action = action?.Run;
            open.ActionButton.Content = action?.Label ?? "";
            open.ActionButton.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;
            var (warns, rest) = Split(body ?? "");
            open.WarnList.ItemsSource = warns;
            open.WarnList.Visibility = warns.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            open.BodyText.Text = rest;
            int n = rest.Length == 0 ? 0 : rest.Split('\n').Length;
            open.Detail.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            open.Detail.Header = $"상세 ({n}줄)";
            open.Detail.IsExpanded = n <= 8;   // 짧으면 펼친 채, 길면 접어 두고 요약·경고만
            open.AtText.Text = $"{what} · {DateTime.Now:HH:mm:ss} 결과";   // 같은 내용으로 다시 와도 새 결과인지 알 수 있게
            open.BodyScroll.ScrollToTop();
            if (!open.IsVisible) open.Show();   // ShowActivated=False: 키보드는 Revit 뷰에 남음
            // 처음 띄울 때도 앞으로 (HWND_TOP) — 활성화 없이 띄우면 같은 주인의 현장 입력 창 뒤에 깔렸음 (21차 점검). z 순서만, 포커스는 그대로
            SetWindowPos(new WindowInteropHelper(open).Handle, IntPtr.Zero, 0, 0, 0, 0, NoMove | NoSize | NoActivate);
        }

        /// <summary>본문에서 경고 줄(경고·주의·⚠·▲·Revit 경고 로 시작)을 따로 빼 "▲ " 로 — 나머지는 상세. 빈 줄 겹침은 하나로</summary>
        internal static (System.Collections.Generic.List<string> Warns, string Body) Split(string body)
        {
            var warns = new System.Collections.Generic.List<string>();
            var rest = new System.Collections.Generic.List<string>();
            foreach (var line in body.Replace("\r", "").Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("경고") || t.StartsWith("주의") || t.StartsWith("⚠") || t.StartsWith("▲") || t.StartsWith("Revit 경고")
                    || t.Contains("존치 연장") || t.StartsWith("연장"))   // 24차 ⑧: 존치 연장 경고 줄도 ▲ 칸으로
                    warns.Add("▲ " + t.TrimStart('⚠', '▲', ' '));
                else if (!(t.Length == 0 && (rest.Count == 0 || rest[^1].Length == 0))) rest.Add(line);
            }
            while (rest.Count > 0 && rest[^1].Trim().Length == 0) rest.RemoveAt(rest.Count - 1);
            return (warns, string.Join("\n", rest));
        }

        /// <summary>Revit 종료 때 (Dashboard.Closing): 주인 창이 닫히기 전에 먼저 닫음</summary>
        internal static void CloseIfOpen() => open?.Close();

        const uint NoSize = 0x0001, NoMove = 0x0002, NoActivate = 0x0010;
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        void CloseClick(object sender, RoutedEventArgs e) => Close();

        void ActionClick(object sender, RoutedEventArgs e)   // 누르면 그 일 하고 창을 닫음 (32차: [생성한 동바리 보기] 뒤 결과 창이 뷰를 가림)
        {
            var a = action;
            Close();
            a?.Invoke();
        }

        // Esc: 모달이 아니라 IsCancel 만으로는 안 닫힘(DialogResult 는 모달 전용), Revit 메시지 루프에서 접근 키가 안 올 수도 있어 키를 직접 받음
        void KeyPressed(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        }
    }
}

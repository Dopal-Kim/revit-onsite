// 현장 입력 창 — 접속 주소·QR 코드·받은 기록. 창을 닫으면 서버를 끈다 (FieldInput.Stop)
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RevitOnSite.Engine;

namespace RevitOnSite
{
    public partial class FieldInputWindow : Window
    {
        static FieldInputWindow open;
        /// <summary>문서를 닫을 때 (Dashboard): 열려 있으면 닫음 → 서버도 끔 (Closed 처리)</summary>
        internal static void CloseOpen() => open?.Close();

        /// <summary>이미 열려 있으면 앞으로</summary>
        internal static bool BringToFront()
        {
            if (open == null) return false;
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return true;
        }

        readonly FieldServer srv;
        const string AllNets = "모든 망 (0.0.0.0)";

        public FieldInputWindow(FieldServer srv)
        {
            InitializeComponent();
            open = this;
            this.srv = srv;
            // 열 주소 (F6): 기본 = 첫 LAN 주소 하나, '모든 망' 은 고를 때만 (공용망·테더링에 같이 열리지 않게)
            foreach (var a in FieldServer.LanAddresses()) HostBox.Items.Add(a.ToString());
            HostBox.Items.Add(AllNets);
            HostBox.SelectedItem = srv.Host is null || srv.Host.Equals(System.Net.IPAddress.Any) ? AllNets : srv.Host.ToString();
            if (HostBox.SelectedIndex < 0) { HostBox.Items.Insert(0, srv.Host.ToString()); HostBox.SelectedIndex = 0; }
            HostBox.SelectionChanged += Rebind;
            ShowAddress(srv);
            FieldInput.Log += Add;
            IsVisibleChanged += (s, e) => Dashboard.Log("현장 입력 창 " + ((bool)e.NewValue ? "보임" : "숨겨짐 (서버는 켜진 채)"));
            Loaded += PlaceBottomRight;
            Closed += (s, e) => { FieldInput.Log -= Add; FieldInput.Stop(); open = null; };
        }

        /// <summary>주소·QR·상태 글 다시 (처음·새 주소·열 주소 바꿈)</summary>
        void ShowAddress(FieldServer s)
        {
            var urls = s.Urls();
            var url = s.PrimaryUrl();
            UrlBox.Text = url;
            QrImage.Source = QrBitmap(Qr.Encode(url));
            OtherUrls.Text = urls.Count > 1 ? "QR 이 안 열리면 휴대폰과 같은 망의 다른 주소: " + string.Join("  ·  ", urls.Skip(1).Select(u => u[..u.IndexOf("/?")])) : "";
            OtherUrls.Visibility = urls.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            State.Text = $"켜짐 · 포트 {s.Port} · 데이터 폴더 {s.Folder}\n주소 유효 ~ {s.TokenExpires:MM-dd HH:mm} ({s.TokenTtlH:0.#}시간 — 지나면 [새 주소])" +
                (FieldInput.FixedNow is DateTime f ? $"\n기준 시각 {f:yyyy-MM-dd HH:mm} (마지막 판정 기준일 — 날짜 검사·판정이 이 시각 기준)" : "");
            if (urls.Count == 0) State.Text += "\n네트워크 연결(Wi-Fi)이 없어 이 PC 에서만 열립니다.";
            if (ReportTab.IsSelected) ShowReport(null, null);   // 열 주소가 바뀌면 신고 QR 도
        }

        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        /// <summary>[가정] Revit 제목 줄 + 리본 높이 (DIP) — 창이 리본(승인 패널 등)을 가리지 않게 이 아래에만 둠</summary>
        const double RibbonDip = 180;
        Rect area;

        /// <summary>모델 뷰 영역 = 활성 뷰 창 사각형 (UIView.GetWindowRectangle, 화면 픽셀) — 리본·오른쪽 도킹 창(대시보드)을 뺀 자리. FieldInputCommand 가 넘김, 못 얻으면 null</summary>
        internal (int Left, int Top, int Right, int Bottom)? ViewArea { get; set; }

        /// <summary>처음 위치 (28·29차 총괄): 모델 뷰 영역의 오른쪽 아래 — 리본도 오른쪽 도킹 대시보드도 가리지 않게.
        /// 뷰 영역을 못 얻으면 Revit 창의 리본 아래 영역 왼쪽 아래. 높이 상한 = 그 영역 (넘치면 본문 스크롤, 아래 버튼 줄은 고정).
        /// 탭을 바꿔 높이가 늘면 아래 끝이 영역 밖으로 나가지 않게 위로 올림</summary>
        void PlaceBottomRight(object sender, RoutedEventArgs e)
        {
            var wa = SystemParameters.WorkArea;
            var src = PresentationSource.FromVisual(this);
            Rect Dip(int l, int t, int r, int b)   // 화면 픽셀 → DIP, 작업 영역 안으로
            {
                var m = src.CompositionTarget.TransformFromDevice;
                return Rect.Intersect(wa, new Rect(m.Transform(new Point(l, t)), m.Transform(new Point(r, b))));
            }
            bool inView = false;
            area = new Rect(wa.Left, wa.Top + RibbonDip, wa.Width, Math.Max(300, wa.Height - RibbonDip));
            if (src?.CompositionTarget != null && ViewArea is { } v && Dip(v.Left, v.Top, v.Right, v.Bottom) is { IsEmpty: false, Height: > 300, Width: > 420 } va)
            {
                area = va;
                inView = true;
            }
            else
            {
                var owner = new WindowInteropHelper(this).Owner;
                if (owner != IntPtr.Zero && src?.CompositionTarget != null && GetWindowRect(owner, out var r)
                    && Dip(r.Left, r.Top, r.Right, r.Bottom) is { IsEmpty: false } a && a.Height > RibbonDip + 300)
                    area = new Rect(a.Left, a.Top + RibbonDip, a.Width, a.Height - RibbonDip);   // Revit 창의 리본 아래
            }
            SizeChanged += (s, ev) => { if (Top + ActualHeight > area.Bottom) Top = Math.Max(area.Top, area.Bottom - ActualHeight - 8); };
            MaxHeight = area.Height - 8;
            UpdateLayout();
            Left = inView ? Math.Max(area.Left, area.Right - ActualWidth - 16) : area.Left + 16;   // 뷰 안이면 오른쪽 아래, 아니면 왼쪽 아래 (오른쪽 도킹 창을 피함)
            Top = Math.Max(area.Top, area.Bottom - ActualHeight - 8);
            Dashboard.Log($"현장 입력 창 위치: {(inView ? "모델 뷰 오른쪽 아래" : "Revit 창 왼쪽 아래 (뷰 영역 못 얻음)")} ({Left:0},{Top:0}) 높이 상한 {MaxHeight:0}");
        }

        void TabChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (e.OriginalSource == Tabs && ReportTab != null && ReportTab.IsSelected) ShowReport(null, null);   // 안쪽 콤보의 SelectionChanged 도 여기로 올라옴 → 탭만
        }

        /// <summary>F14 협력업체 해체 신고 QR — 고른 층의 /view?r=신고토큰&amp;f=층 (층 목록 = zones.csv, 층 이름 = 첫 구역 ID 의 '-' 앞)</summary>
        void ShowReport(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ReportFloor.Items.Count == 0)
                    foreach (var g in Evaluator.Read(srv.Folder, "zones.csv").GroupBy(r => int.Parse(r["floor"], System.Globalization.CultureInfo.InvariantCulture)).OrderBy(g => g.Key))
                    {
                        var id = g.First()["zone_id"] ?? "";
                        ReportFloor.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = id.Contains('-') ? id.Split('-')[0] : g.Key < 0 ? $"B{-g.Key}" : $"{g.Key}F", Tag = g.Key });
                    }
                if (ReportFloor.SelectedIndex < 0 && ReportFloor.Items.Count > 0) { ReportFloor.SelectedIndex = 0; return; }   // SelectionChanged 가 다시 부름
                if (!(ReportFloor.SelectedItem is System.Windows.Controls.ComboBoxItem it)) return;
                var url = srv.ReportUrl((int)it.Tag);
                ReportUrlBox.Text = url;
                ReportQr.Source = QrBitmap(Qr.Encode(url));
                Dashboard.Log($"현장 입력: 해체 신고 QR {it.Content}");
            }
            catch (Exception ex) { ReportUrlBox.Text = "층 목록을 읽지 못함 (zones.csv): " + ex.Message; ReportQr.Source = null; }
        }

        void CopyReport(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(ReportUrlBox.Text); Add($"{DateTime.Now:HH:mm:ss} 신고 주소를 클립보드에 복사함"); }
            catch (Exception) { }   // 다른 프로그램이 클립보드를 잡고 있으면 조용히 넘어감
        }

        void NewUrl(object sender, RoutedEventArgs e)
        {
            srv.NewToken();   // 옛 QR 은 이제 403 — 휴대폰은 새 QR 로 다시 엶
            ShowAddress(srv);
            Add($"{DateTime.Now:HH:mm:ss} 새 주소 — 옛 QR 은 쓸 수 없음");
            Dashboard.Log("현장 입력: 새 접속 주소 (만료 " + srv.TokenExpires.ToString("MM-dd HH:mm") + ")");
        }

        void Rebind(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var pick = HostBox.SelectedItem as string;
            var host = pick == AllNets ? System.Net.IPAddress.Any : System.Net.IPAddress.TryParse(pick, out var a) ? a : null;
            if (host == null || host.Equals(srv.Host)) return;
            int port = srv.Port;
            srv.Stop();
            try { srv.Start(port, host); }
            catch (System.Net.Sockets.SocketException ex)
            {
                Add($"{DateTime.Now:HH:mm:ss} 주소를 바꾸지 못함: {ex.Message}");
                try { srv.Start(port); }   // 원래 망으로 — 이것도 안 되면 예외를 Revit 까지 올리지 않고 안내만
                catch (System.Net.Sockets.SocketException ex2) { Add($"{DateTime.Now:HH:mm:ss} 입력 서버를 다시 켜지 못함: {ex2.Message} — 창을 닫고 [현장 입력]을 다시 누르세요"); return; }
            }
            ShowAddress(srv);
            Add($"{DateTime.Now:HH:mm:ss} 열 주소 {(host.Equals(System.Net.IPAddress.Any) ? "모든 망" : host.ToString())} · 포트 {srv.Port}");
            Dashboard.Log($"현장 입력: 열 주소 {pick} · 포트 {srv.Port}");
        }

        void Add(string line)
        {
            LogList.Items.Insert(0, line);
            while (LogList.Items.Count > 200) LogList.Items.RemoveAt(LogList.Items.Count - 1);
        }

        /// <summary>모듈 행렬 → 흑백 비트맵 (둘레 4칸 여백, 1 모듈 = 1 px — 화면에서 최근접 확대)</summary>
        internal static BitmapSource QrBitmap(bool[,] m)   // 서버 연동 QR 창도 씀
        {
            int n = m.GetLength(0), q = 4, w = n + 2 * q;
            var px = Enumerable.Repeat((byte)255, w * w).ToArray();
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (m[y, x]) px[(y + q) * w + x + q] = 0;
            var b = BitmapSource.Create(w, w, 96, 96, PixelFormats.Gray8, null, px, w);
            b.Freeze();
            return b;
        }

        void CopyUrl(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(UrlBox.Text); Add($"{DateTime.Now:HH:mm:ss} 주소를 클립보드에 복사함"); }
            catch (Exception) { }   // 다른 프로그램이 클립보드를 잡고 있으면 조용히 넘어감
        }

        void CloseClick(object sender, RoutedEventArgs e)
        {
            // 끄기 전에 확인 (N15: 사람이 누른 것이었지만, 시연·현장에서 실수로 끄면 휴대폰 입력이 말없이 끊김)
            var dev = System.Windows.Input.InputManager.Current.MostRecentInputDevice?.GetType().Name ?? "?";
            Dashboard.Log("현장 입력 [닫고 입력 끄기] 눌림 — 입력 장치: " + dev);
            if (MessageBox.Show(this, "현장 입력을 끄면 휴대폰 입력을 더 받지 않습니다.\n끌까요? (창만 치우려면 [아니요] 후 최소화)", "현장 입력 끄기",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                Dashboard.Log("현장 입력 끄기 취소 — 서버 그대로");
                return;
            }
            Close();
        }

        void Help(object sender, RoutedEventArgs e) => App.ShowHelp(typeof(FieldInputCommand));
    }
}

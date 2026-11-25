// [타설·데이터 · 서버 연동] Vercel 현장 서버와 동기화를 켜고 끔 (v6 4단계, server/vercel/API.md). 통신·검사·CSV = Engine.FieldServer.Sync (tools/server_sync.py 1:1 이식).
//   켜면 바로 한 번, 그 뒤 IntervalMin 분마다: pull → ack → push. 통신은 백그라운드 스레드 (Revit API 없음) — Revit 은 기다리지 않음.
//   받아 저장한 입력이 있으면 현장 입력(LAN)과 같은 모델 반영 (FieldInput.ApplyModel: 판정 다시 계산 · 센서 표식 · 대시보드) + 강도 타임라인 다시 만듦.
//   주소·키 = Windows 사용자 환경 변수 FIELD_SERVER_URL · FIELD_SYNC_KEY — 값은 화면·기록 어디에도 쓰지 않음. 결론은 엔진 판정 그대로 ("해체 승인 요청 가능" / "존치").
//   휴대폰 QR (총괄 11-08 "QR 을 찍으면 Vercel 주소로 열림"): 켤 때·[서버 연동] 다시 누름 → [QR 보기] 로 ServerQrWindow — 입력(?t=FIELD_TOKEN)·층 보기(?v=)·협력업체 신고(?r=).
//     토큰은 QR·이 창 글자에만 (기록·파일 이름 없음). 층 표지 QR 도 연동 중이면 서버 층 보기 주소 (FloorSignCommand).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitOnSite.Engine;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using TextBox = System.Windows.Controls.TextBox;

namespace RevitOnSite
{
    public static class ServerLink
    {
        public const int IntervalMin = 5;   // [가정] 자동 동기화 간격 (분) — 휴대폰 '판정은 동기화 뒤 반영' 대기 시간
        const string Name = "server_link";
        static DispatcherTimer timer;
        static Document doc;
        static volatile bool busy;

        public static bool On => timer != null;
        internal static bool Configured => Env() != null;   // [현장 입력]도 이 서버로 (주소·키가 있을 때)

        static (string Url, string Key)? Env() =>
            FieldServer.EnvVar(FieldServer.UrlVar) is string u && FieldServer.EnvVar(FieldServer.KeyVar) is string k ? (u, k) : null;

        /// <summary>API 문맥(명령)에서: 켜고 바로 한 번 동기화. 켜지 못하면 InvalidOperationException (한국어)</summary>
        internal static void Start(UIApplication app)
        {
            if (On) return;
            if (Env() == null)
                throw new InvalidOperationException($"서버 주소·동기화 키가 없습니다. Windows 사용자 환경 변수 {FieldServer.UrlVar} 와 {FieldServer.KeyVar} 를 넣은 뒤 다시 누르세요 (값은 서버를 만든 사람에게 받습니다 — 화면에 보이지 않음).");
            var d = app.ActiveUIDocument.Document;
            var s = ProjectSettings.Read(d);
            if (string.IsNullOrWhiteSpace(s.CsvFolder) || !File.Exists(Path.Combine(s.CsvFolder, "zones.csv")))
                throw new InvalidOperationException("데이터 폴더에 zones.csv 가 없습니다. [프로젝트 설정]에서 데이터 폴더를 고르세요.");
            var zs = ZoneEvaluation.SyncZonesCsv(d, s.CsvFolder);   // 휴대폰 구역 타일 = 모델 구역 (현장 입력과 같음)
            if (zs != null) Dashboard.Log("서버 연동: " + zs);
            var sh = ShoringModel.WriteShoresCsv(d, s.CsvFolder);   // 휴대폰 동바리 점검 평면 = 모델 동바리 위치
            if (sh != null) Dashboard.Log("서버 연동: " + sh);
            Dashboard.Ensure();
            doc = d;
            timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(IntervalMin) };
            timer.Tick += (_, _) => Dashboard.Request("서버 연동", a => Run(a, report: false), quiet: true);
            timer.Start();
            App.SetText(Name, "서버\n연동 켬");
            Dashboard.Log($"서버 연동 켬: {IntervalMin}분마다 가져오기 → 확인 → 올리기");
            ServerQrWindow.Open(app, s.CsvFolder);
            Run(app, report: true);
        }

        /// <summary>연동 중일 때 층 표지 QR 주소 (서버 층 보기) — 꺼져 있거나 주소·보기 토큰이 없으면 null (그때는 LAN 규칙)</summary>
        internal static Func<int, string> FloorSignUrl() =>
            On && FieldServer.EnvVar(FieldServer.UrlVar) is string u && FieldServer.EnvVar(FieldServer.ViewTokenVar) is string v ? FloorSign.CloudPhoneUrl(u, v) : null;

        internal static void Stop()
        {
            if (!On) return;
            timer.Stop();
            timer = null;
            doc = null;
            ServerQrWindow.CloseIfOpen();
            App.SetText(Name, "서버\n연동");
            Dashboard.Log("서버 연동 끔");
        }

        /// <summary>API 문맥: 문서·설정·'지금'(데모 규칙)을 다시 읽고 백그라운드로 pull → ack → push. 끝나면 Done (다시 API 문맥)</summary>
        static void Run(UIApplication app, bool report)
        {
            if (!On) return;
            if (doc == null || !doc.IsValidObject) { Stop(); return; }   // 문서를 닫음
            if (busy) { Dashboard.Log("서버 연동: 앞 동기화가 아직 끝나지 않아 이번은 건너뜀"); return; }
            if (Env() is not { } env) { Stop(); App.Message("서버 연동을 껐습니다.", $"환경 변수 {FieldServer.UrlVar}·{FieldServer.KeyVar} 가 없어졌습니다."); return; }
            var s = ProjectSettings.Read(doc);
            var demo = FieldInput.DemoNow(doc);   // 판정 기준일이 미래(시연 자료)면 그 09:00 을 '지금'으로 — 현장 입력과 같은 규칙
            // LAN 현장 입력 서버가 같은 폴더로 켜져 있으면 그 인스턴스 (client_id 중복 집합·잠금을 같이), 아니면 이번 한 번 (CSV 에서 중복 집합을 새로 읽음)
            var srv = FieldInput.Running && string.Equals(Path.GetFullPath(FieldInput.Server.Folder), Path.GetFullPath(s.CsvFolder), StringComparison.OrdinalIgnoreCase)
                ? FieldInput.Server : new FieldServer(s.CsvFolder) { FixedNow = () => demo, CureMin = s.CureMin, DefaultFck = s.DefaultFck };
            busy = true;
            var ui = Dispatcher.CurrentDispatcher;
            Task.Run(() =>
            {
                SyncResult r = null;
                Exception err = null;
                try { r = srv.Sync(env.Url, env.Key, demo); }
                catch (Exception ex) { err = ex; }
                finally { busy = false; }
                ui.BeginInvoke(new Action(() => Dashboard.Request("서버 연동 결과", a => Done(a, r, err, demo, report), quiet: true)));
            });
        }

        /// <summary>API 문맥: 결과 기록 · 받은 입력이 있으면 모델 반영 · 처음(켤 때)·거부가 있을 때만 결과 창</summary>
        static void Done(UIApplication app, SyncResult r, Exception err, DateTime? demo, bool report)
        {
            if (err != null)
            {
                Dashboard.Log("서버 연동 실패: " + err.Message);
                if (report) App.Message("서버와 동기화하지 못했습니다.", err.Message + $"\n\n연동은 켜 둔 채 {IntervalMin}분 뒤 다시 합니다 — 끄려면 [서버 연동]을 다시 누르세요.", TaskDialogIcon.TaskDialogIconWarning);
                return;
            }
            string line = $"서버 연동 {DateTime.Now:HH:mm} — 가져옴: 저장 {r.Saved} · 이미 있음 {r.Duplicate} · 거부 {r.Errors.Count} / 올림: {r.Floors}개 층 판정 ({r.PushedAt[11..16]} 기준)";
            Dashboard.Log(line + string.Concat(r.Errors.Select(e => $"\n  {e.Key}: {e.Value}")));
            string applied = "";
            if (r.Saved > 0 && doc != null && doc.IsValidObject)
            {
                Timeline.DataChanged();   // 강도 곡선·공시체 점은 CSV 에서 — 다음 프레임에 다시 만듦
                var (ok, text) = FieldInput.ApplyModel(app, doc, demo ?? DateTime.Now, "서버 연동 반영");
                applied = (ok ? "모델 반영 — " : "모델 반영 못 함 — Revit 오류: ") + text;
                Dashboard.Log("서버 연동: " + applied);
            }
            if (report || r.Errors.Count > 0)
                App.ShowResult(app, "서버 연동", line,
                    (applied.Length > 0 ? applied + "\n" : "")
                    + (r.Errors.Count > 0 ? "거부한 입력 (서버 검사 뒤 데이터가 바뀜 — 휴대폰엔 '저장됨'으로 남음):\n" + string.Join("\n", r.Errors.Select(e => $"  {e.Key}: {e.Value}")) + "\n" : "")
                    + (On ? $"켜 둔 동안 {IntervalMin}분마다 다시 합니다. 끄려면 [서버 연동]을 다시 누르세요." : ""));
        }
    }

    /// <summary>[서버 연동] 켜기·끄기</summary>
    [Transaction(TransactionMode.Manual)]
    public class ServerLinkCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            if (ServerLink.On)
            {
                var td = new TaskDialog(App.TabName) { TitleAutoPrefix = false, MainInstruction = "서버 연동이 켜져 있습니다", CommonButtons = TaskDialogCommonButtons.Cancel };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "휴대폰 QR 보기", "입력·층 보기·협력업체 신고 QR (서버 주소)");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "서버 연동 끄기", "휴대폰 입력은 서버에 쌓여 있다가 다시 켜면 가져옵니다.");
                var r = td.Show();
                if (r == TaskDialogResult.CommandLink1 && App.ProjectDoc(data) is Document qd) { ServerQrWindow.Open(data.Application, ProjectSettings.Read(qd).CsvFolder); return Result.Succeeded; }
                if (r != TaskDialogResult.CommandLink2) return Result.Cancelled;
                ServerLink.Stop();
                App.Message("서버 연동을 껐습니다.", "휴대폰 입력은 서버에 쌓여 있다가 다시 켜면 가져옵니다.");
                return Result.Succeeded;
            }
            return App.ProjectDoc(data) == null ? Result.Cancelled : TurnOn(data.Application);
        }

        /// <summary>켜기 ([서버 연동]·[현장 입력]·대시보드 [동바리 점검 열기] 공통, API 문맥)</summary>
        internal static Result TurnOn(UIApplication app)
        {
            try
            {
                ServerLink.Start(app);
                return Result.Succeeded;
            }
            catch (InvalidOperationException ex)
            {
                ServerLink.Stop();
                App.Message("서버 연동을 켜지 못했습니다.", ex.Message, TaskDialogIcon.TaskDialogIconWarning);
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                ServerLink.Stop();
                return App.Fail("서버 연동 켜기", ex);
            }
        }
    }

    /// <summary>서버 연동 휴대폰 QR 창 (모달 아님 — 닫아도 연동은 계속). 입력 QR + 층 고르기 → 층 보기·협력업체 신고 QR. 토큰은 QR·이 창 글자에만</summary>
    sealed class ServerQrWindow : Window
    {
        static ServerQrWindow open;

        internal static void Open(UIApplication app, string folder)
        {
            if (open != null) { open.Activate(); return; }
            var w = new ServerQrWindow(folder);
            new WindowInteropHelper(w).Owner = app.MainWindowHandle;
            w.Closed += (_, _) => open = null;
            open = w;
            w.Show();
        }

        internal static void CloseIfOpen() => open?.Close();

        static FrameworkElement Qr(string url, string missingVar, double size, out TextBox box)
        {
            box = null;
            var sp = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            if (url == null) { sp.Children.Add(new TextBlock { Text = $"환경 변수 {missingVar} 가 없어 이 QR 은 만들 수 없습니다.", TextWrapping = TextWrapping.Wrap, Width = size, Foreground = Brushes.Firebrick }); return sp; }
            var img = new Image { Source = FieldInputWindow.QrBitmap(Engine.Qr.Encode(url)), Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Left };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            sp.Children.Add(img);
            sp.Children.Add(box = new TextBox { Text = url, IsReadOnly = true, Width = size, FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            return sp;
        }

        ServerQrWindow(string folder)
        {
            Title = "서버 연동 — 휴대폰 QR";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Language = System.Windows.Markup.XmlLanguage.GetLanguage("ko-KR");
            string url = FieldServer.EnvVar(FieldServer.UrlVar) ?? "";
            string tok = FieldServer.EnvVar(FieldServer.TokenVar), vtok = FieldServer.EnvVar(FieldServer.ViewTokenVar), rtok = FieldServer.EnvVar(FieldServer.ReportTokenVar);
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock { Text = "인터넷만 되면 어디서나 찍으면 됩니다 (같은 와이파이 아니어도). 휴대폰에서 저장한 값의 판정은 Revit 동기화(" + ServerLink.IntervalMin + "분마다) 뒤에 보입니다.",
                TextWrapping = TextWrapping.Wrap, MaxWidth = 560, Margin = new Thickness(0, 0, 0, 10) });
            root.Children.Add(new TextBlock { Text = "현장 입력 (품질·안전관리자 — 공시체·센서·동바리 확인·점검)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            var inRow = new StackPanel { Orientation = Orientation.Horizontal };
            inRow.Children.Add(Qr(tok == null ? null : FieldServer.CloudInputUrl(url, tok), FieldServer.TokenVar, 200, out var inBox));
            if (inBox != null)
            {
                var copy = new Button { Content = new TextBlock { Text = "주소 복사" }, VerticalAlignment = VerticalAlignment.Top, Padding = new Thickness(8, 2, 8, 2) };
                copy.Click += (_, _) => { try { Clipboard.SetText(inBox.Text); } catch (Exception) { } };   // 다른 프로그램이 클립보드를 잡고 있으면 조용히
                inRow.Children.Add(copy);
            }
            root.Children.Add(inRow);
            // 층 고르기 → 층 보기·신고 (층 표지·층 입구 게시용과 같은 주소)
            var floors = Evaluator.Read(folder, "zones.csv").Where(r => int.TryParse(r.GetValueOrDefault("floor"), out _))
                .GroupBy(r => int.Parse(r["floor"]!)).OrderByDescending(g => g.Key)
                .Select(g => (Floor: g.Key, Label: StrengthTimeline.LabelOf(g.Key, g.Select(r => r.GetValueOrDefault("zone_id") ?? "").Order(StringComparer.Ordinal).ToList()))).ToList();
            var pick = new ComboBox { Width = 100, Margin = new Thickness(8, 0, 0, 0) };
            foreach (var f in floors) pick.Items.Add(new ComboBoxItem { Content = f.Label, Tag = f.Floor });
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 4) };
            head.Children.Add(new TextBlock { Text = "층 보기 (반장·읽기 전용) · 협력업체 해체 신고", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(pick);
            root.Children.Add(head);
            var floorRow = new StackPanel { Orientation = Orientation.Horizontal };
            root.Children.Add(floorRow);
            void ShowFloor()
            {
                floorRow.Children.Clear();
                if (pick.SelectedItem is not ComboBoxItem it) { floorRow.Children.Add(new TextBlock { Text = "zones.csv 에 층이 없습니다." }); return; }
                int fl = (int)it.Tag;
                var v = new StackPanel();
                v.Children.Add(new TextBlock { Text = "층 보기", Margin = new Thickness(0, 0, 0, 2) });
                v.Children.Add(Qr(vtok == null ? null : FieldServer.CloudViewUrl(url, vtok, fl), FieldServer.ViewTokenVar, 160, out _));
                var r = new StackPanel();
                r.Children.Add(new TextBlock { Text = "협력업체 신고", Margin = new Thickness(0, 0, 0, 2) });
                r.Children.Add(Qr(rtok == null ? null : FieldServer.CloudReportUrl(url, rtok, fl), FieldServer.ReportTokenVar, 160, out _));
                floorRow.Children.Add(v);
                floorRow.Children.Add(r);
            }
            pick.SelectionChanged += (_, _) => ShowFloor();
            if (pick.Items.Count > 0) pick.SelectedIndex = 0; else ShowFloor();
            var close = new Button { Content = new TextBlock { Text = "닫기 (연동은 계속)" }, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 12, 0, 0) };
            close.Click += (_, _) => Close();
            root.Children.Add(close);
            Content = root;
        }
    }
}

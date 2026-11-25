// 대시보드 도킹 창 (ui_spec §5) — 화면만. 데이터 읽기·모델 요청은 Dashboard.cs (모델 변경은 ExternalEvent 로만)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace RevitOnSite
{
    public partial class DashboardPane : UserControl
    {
        public DashboardPane()
        {
            InitializeComponent();
            BaseDate.SelectedDate = DateTime.Today;
            RoleBox.SelectedIndex = Math.Max(0, Array.IndexOf(RoleNames, role));   // W13 역할 보기 (RoleChanged 가 단추 보임 맞춤)
            ApplyRole();
            // 85차: 패널 전체 스크롤 하나 — 격자·경고 목록은 칸 안 스크롤이 끝이면 바깥 스크롤로 (안쪽 스크롤이 휠을 삼키지 않게)
            Matrix.PreviewMouseWheel += WheelToPage;
            Warnings.PreviewMouseWheel += WheelToPage;
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible) Dashboard.RequestRefresh();   // 창이 보일 때 새로 읽기
                else if (playStart != null && Dashboard.Playing) { StopPlayback(null); Dashboard.TimelineStop(RestoreView.IsChecked == true); }   // 창을 닫으면 4D 멈춤
            };
            timer.Tick += (s, e) => Step();
            // 칸 우클릭 (33차): 판정 근거 · 조기 탈형 검토서 — 리본 슬라이드아웃은 구역을 고르면 '수정' 탭으로 바뀌어 누르기 어려움
            var cm = new ContextMenu();
            MenuItem Item(string text, Action<Dashboard.ZoneInfo> run)
            {
                var m = new MenuItem { Header = text };
                m.Click += (s, e) => { if (rightZone != null) run(rightZone); };
                return m;
            }
            cm.Items.Add(Item("판정 근거", Dashboard.ShowReason));
            cm.Items.Add(Item("조기 탈형 검토서 (이 구역)", Dashboard.EarlyForm));
            cm.Items.Add(Item("동바리 점검 열기 (휴대폰 QR)", Dashboard.ShoreInspect));
            // 60차: Revit 도킹 창에선 WPF 가 메뉴를 스스로 안 띄움(뷰 우클릭만 됨) → 누를 때 직접 연다. 마우스 2 px 옆 = 떼는 순간 항목이 눌리지 않게
            cm.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            cm.HorizontalOffset = cm.VerticalOffset = 2;
            cm.PlacementTarget = Matrix;
            void Prepare(Dashboard.ZoneInfo z)
            {
                rightZone = z;
                bool judged = z.Decision.Length > 0;
                ((MenuItem)cm.Items[0]).IsEnabled = ((MenuItem)cm.Items[1]).IsEnabled = judged;   // 판정 전 칸은 점검만
                ((MenuItem)cm.Items[0]).Header = judged ? "판정 근거" : "판정 근거 (판정 전)";
            }
            bool mouseOpened = false;
            cm.Closed += (s, e) => mouseOpened = false;   // Revit 도킹 창에서 ContextMenuOpening 이 안 오면 다음 키보드 열기를 막지 않게
            Matrix.PreviewMouseRightButtonDown += (s, e) =>
            {
                if (CurrentZone(e) is not { } z) { rightZone = null; return; }   // 빈 칸·층 이름 칸은 메뉴 없음
                e.Handled = true;
                Prepare(z);
                cm.PlacementTarget = Matrix;
                cm.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                mouseOpened = true;
                cm.IsOpen = true;
            };
            // 62차 R4: 늘 막으면 Shift+F10·메뉴 키·화면 읽기(UIA)로는 못 열었음 → 마우스로 이미 연 것만 막고, 키보드는 지금 칸(CurrentCell)으로 열기
            Matrix.ContextMenuOpening += (s, e) =>
            {
                if (mouseOpened) { mouseOpened = false; e.Handled = true; return; }
                if (Matrix.CurrentCell is { Item: FloorRow row, Column: { DisplayIndex: >= 1 } col } && col.DisplayIndex - 1 < row.Cells.Length && row.Cells[col.DisplayIndex - 1].Zone is { } kz)
                {
                    Prepare(kz);
                    cm.PlacementTarget = col.GetCellContent(row) as UIElement ?? Matrix;   // 키보드: 그 칸 아래 (칸이 화면에 없으면 표 아래)
                    cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                }
                else e.Handled = true;
            };
            RebuildLegend();   // 처음 = 상태 (활성 뷰를 모름 → Load 가 맞춤)
            // 근거 단계 색 견본 0–8 (강도 타임라인과 같은 색 식)
            for (int k = 0; k <= 8; k++)
            {
                var c = FourD.EvidenceColor(k);
                EvSwatches.Children.Add(new Border
                {
                    Width = 12, Height = 12, Margin = new Thickness(0, 0, 1, 0), Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)),
                    ToolTip = k == 0 ? "0 근거 없음" : $"{k} {Engine.Evidence.StageName(k)}",
                });
            }
        }

        // ── 강도 예측 타임라인 막대 (Dashboard.Timeline*). 슬라이더 값 = 시작일부터 날짜 수 (정수). 재생: 1초에 하루 (또는 한 주)
        DateOnly? playStart;   // null = 아직 세션 없음 (▶ 를 누르면 준비)
        DateTime pos;          // 지금 그리는 날짜 (09:00)
        bool quiet;            // 슬라이더를 코드로 옮길 때 SliderMoved 가 다시 요청하지 않게
        Engine.StrengthTimelineResult line;
        SortedDictionary<DateOnly, List<string>> events = new();
        readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
        readonly List<UIElement> nowMarks = new();   // 지금 날짜에 따라 바뀌는 것: 세로선·층 점·그날까지 공시체

        /// <summary>Dashboard 가 세션 준비 뒤 부름: 기간·사건·강도 곡선을 넣고 current 를 그림</summary>
        public void SetTimeline(DateOnly start, DateOnly end, DateOnly current, bool play, SortedDictionary<DateOnly, List<string>> evs)
        {
            playStart = start;
            TimelineBox.IsExpanded = true;   // 11-12 2-5: 접힌 채 시작 — 세션을 열면(▶·사건·[강도 타임라인]) 그림이 보이게
            line = Timeline.Line;
            chartStart = start;
            chartSpan = Math.Max(1, end.DayNumber - start.DayNumber) + 1;
            events = evs ?? new();
            TimelineSlider.Maximum = Math.Max(1, end.DayNumber - start.DayNumber);
            TimelineSlider.IsEnabled = true;
            pos = current.ToDateTime(new TimeOnly(9, 0));
            MoveSlider();
            DrawChart();
            Want();
            if (play) timer.Start();
        }

        DateOnly? chartStart;   // 그림 가로축 시작 (세션 시작, 세션이 없으면 그림 자료 시작 — 86차)
        double chartSpan = 1;   // 그림 가로축 날 수

        /// <summary>86차 (85차 점검 R21: 처음 열 때·판정 뒤·문서 전환 뒤 그림이 빈칸 — ▶·사건을 눌러 세션을 준비해야 그려졌음): 세션 없이도 그림.
        /// res = 세션과 같은 계산 (StrengthTimeline, 데이터 폴더), 지금 선 = 기준일. 세션이 있으면 세션 그림 그대로, ▶·사건을 누르면 그때 세션 준비 (기존대로). res null = 비움</summary>
        public void ShowChart(Engine.StrengthTimelineResult res, DateTime at)
        {
            if (playStart != null) return;
            line = res != null && res.Dates.Count > 0 ? res : null;
            chartStart = line?.Start;
            chartSpan = line == null ? 1 : Math.Max(1, line.End!.Value.DayNumber - line.Start!.Value.DayNumber) + 1;
            pos = at.Date.AddHours(9);
            DrawChart();
        }

        /// <summary>61차: [프로젝트 설정] 저장 뒤 — 4D 세션이 있고 멈춰 있으면 지금 날짜를 다시 그림 (양생 하한 등 → 4D 다시 만듦·판넬 문구, 뷰는 안 바꿈)</summary>
        public void Redraw() { if (playStart != null && !Dashboard.Playing) Want(); }

        /// <summary>4D 모델을 다시 만듦 (휴대폰 저장 등) — 세션이 있으면 사건·곡선을 새 값으로 다시 그림 (기간은 그대로)</summary>
        public void ModelRebuilt()
        {
            if (playStart == null) return;
            events = Timeline.Events;
            line = Timeline.Line;
            DrawChart();
            if (pendingStep is { } t) { pendingStep = null; Dispatcher.BeginInvoke(new Action(() => DoStep(t))); }   // 다시 만든 뒤 [사건 ▶] 이어서 (35차)
        }

        string pendingStep;   // 4D 를 다시 만드는 동안 기다리는 [사건 ◀/▶] (Tag)

        /// <summary>Dashboard 가 4D 준비에 실패했을 때</summary>
        public void StopPlay() => StopPlayback(null);

        /// <summary>재생을 멈추는 단 한 곳 (❚❚·끝 날짜·창 닫기·뷰를 떠남·준비 실패): 타이머 멈춤 + 재생 상태 끔(대기 중 그리기 버림) + 상태 줄</summary>
        public void StopPlayback(string note)
        {
            timer.Stop();
            bool was = Dashboard.Playing;
            Dashboard.TimelinePlaying(false);
            if (note != null) TimelineNote(note);
            else if (was && playStart != null) TimelineNote("멈춤 " + SliderDate.Text + " — ▶ 를 누르면 이어서 재생");
        }

        /// <summary>세션을 버렸을 때 (문서 닫기·미리보기 뷰 삭제·끄개): 막대를 처음 상태로, ▶ 를 누르면 다시 준비</summary>
        public void ResetTimeline(string note)
        {
            timer.Stop();
            playStart = null;
            chartStart = null;   // 86차: 그림도 비움 — 다음 새로 고침이 그 문서 그림을 다시 넣음
            line = null;
            pendingStep = null;
            TimelineSlider.IsEnabled = false;
            SliderDate.Text = "";
            ProcessChart.Visibility = Visibility.Hidden;   // 자리는 남김 (N2)
            SetDoing(null);
            TimelineNote(note);
        }

        /// <summary>[색] = 판정 상태 (동바리 상태 켜기 실패 때 — 콤보가 켜진 채 남지 않게)</summary>
        public void ResetColorMode() { if (ColorMode != null && ColorMode.SelectedIndex != 0) ColorMode.SelectedIndex = 0; }

        /// <summary>그날 위층 추정 강도·사건 (Dashboard.TimelineDoing)</summary>
        public void SetDoing(string line)
        {
            DoingText.Text = line ?? "";
            DoingText.ToolTip = string.IsNullOrEmpty(line) ? null : line;
        }

        // ── 하루 한 칸 / 사건 건너뛰기 (Tag: 0, -1, 1, -e, e)
        void StepUp(object sender, MouseButtonEventArgs e)
        {
            if (!Over(sender, e)) { Dashboard.Log("버튼 4D 한 칸: 버튼 밖에서 놓임 — 무시"); return; }
            ReleaseButton(e);
            MarkMouse(sender);
            var tag = (string)((FrameworkElement)sender).Tag;
            Act("4D 한 칸 " + tag, "마우스", () => DoStep(tag));
        }

        void StepClick(object sender, RoutedEventArgs e)
        {
            if (FromMouse(sender)) return;   // (N7)
            var tag = (string)((FrameworkElement)sender).Tag;
            Act("4D 한 칸 " + tag, "키보드·화면 읽기", () => DoStep(tag));
        }

        void DoStep(string tag)
        {
            if (playStart is not { } s0) { Dashboard.TimelinePrepare(play: false); return; }   // 처음이면 준비만 (오늘을 그림)
            if (Dashboard.Playing) StopPlayback(null);
            if (tag.EndsWith("e") && Timeline.Stale)   // 35차: 휴대폰 저장 직후 첫 [사건 ▶] 가 옛 목록으로 12-22 를 건너뛰던 것 → 지금 날을 다시 그려(= 4D 다시 만듦) 새 사건으로
            {
                pendingStep = tag;
                TimelineNote("기록이 바뀌어 4D 를 다시 만드는 중 — 끝나면 사건으로 이동합니다");
                Want();
                return;
            }
            var d = DateOnly.FromDateTime(pos);
            var end = s0.AddDays((int)TimelineSlider.Maximum);
            DateOnly? to = tag switch
            {
                "0" => s0,   // [처음] (61차: 타임라인은 마지막 판정일에서 시작)
                "-1" => d.AddDays(-1),
                "1" => d.AddDays(1),
                "-e" => events.Keys.Where(k => k < d).Select(k => (DateOnly?)k).LastOrDefault(),
                _ => events.Keys.Where(k => k > d).Select(k => (DateOnly?)k).FirstOrDefault(),
            };
            if (to is not { } t || t < s0 || t > end) { TimelineNote(tag.EndsWith("e") ? "그 쪽으로 더 갈 사건이 없습니다." : "기간 끝입니다."); return; }
            pos = t.ToDateTime(new TimeOnly(9, 0));
            MoveSlider();
            Dashboard.TimelineShow();
            Want();
            TimelineNote(events.TryGetValue(t, out var ev) ? t.ToString("MM-dd") + " 사건 — " + string.Join(" · ", ev) : null);
        }

        /// <summary>그림 아래 안내 (null = 비움). 두 줄까지 — 길면 … 로 줄이고 전체는 툴팁. 비면 접음 (84차 — 그림 아래라 버튼 위치는 안 바뀜, N2)</summary>
        public void TimelineNote(string note)
        {
            TimelineNoteText.Text = note ?? "";
            TimelineNoteText.ToolTip = string.IsNullOrEmpty(note) ? null : note;
            TimelineNoteText.Visibility = string.IsNullOrEmpty(note) ? Visibility.Collapsed : Visibility.Visible;
        }

        void MoveSlider()
        {
            quiet = true;
            TimelineSlider.Value = DateOnly.FromDateTime(pos).DayNumber - playStart!.Value.DayNumber;
            quiet = false;
        }

        /// <summary>pos 를 그려 달라고 요청 + 날짜 글자·그림의 지금 표시</summary>
        void Want()
        {
            SliderDate.Text = pos.ToString("yyyy-MM-dd");
            MoveNowLine();
            Dashboard.TimelineWant(pos);
        }

        void SliderMoved(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (quiet || playStart is not { } s0) return;
            pos = s0.AddDays((int)TimelineSlider.Value).ToDateTime(new TimeOnly(9, 0));   // 손으로 옮기면 그날 09:00
            if (Dashboard.Playing) StopPlayback(null);   // 재생 중에 막대를 잡으면 멈추고 그 날짜를 봄
            Dashboard.TimelineShow();
            Want();
        }

        /// <summary>재생 한 걸음. 앞 프레임을 아직 그리는 중이면 이번 걸음은 건너뜀 → 느린 모델에서도 요청이 쌓이지 않음</summary>
        void Step()
        {
            if (!Dashboard.Playing || playStart is not { } s0) { timer.Stop(); return; }   // 멈춘 뒤 남은 틱
            if (Dashboard.FrameBusy) return;
            var next = DateOnly.FromDateTime(pos).AddDays(Speed.SelectedIndex == 1 ? 7 : 1);
            if (next.DayNumber - s0.DayNumber > TimelineSlider.Maximum) { StopPlayback("끝 날짜 " + SliderDate.Text + " 에서 멈춤 — ▶ 를 누르면 처음부터"); return; }   // 반복하지 않음 (뷰는 그대로)
            pos = next.ToDateTime(new TimeOnly(9, 0));
            MoveSlider();
            Want();
        }

        void PlayUp(object sender, MouseButtonEventArgs e)
        {
            if (!Over(sender, e)) { Dashboard.Log("버튼 재생: 버튼 밖에서 놓임 — 무시"); return; }
            ReleaseButton(e);
            MarkMouse(sender);
            Act("재생", "마우스", DoPlay);
        }

        void PlayClick(object sender, RoutedEventArgs e) { if (!FromMouse(sender)) Act("재생", "키보드·화면 읽기", DoPlay); }   // (N7)

        void DoPlay()
        {
            if (Dashboard.Playing) return;   // 이미 재생 중 (▶ 를 여러 번 눌러도 처음부터 다시 돌지 않음)
            Dashboard.TimelinePlaying(true);
            TimelineNote("재생 중 — ❚❚ 로 멈춤 (강도 타임라인 뷰를 닫거나 다른 뷰로 가도 멈춥니다)");
            if (playStart is not { } s0) { Dashboard.TimelinePrepare(play: true); return; }
            if (TimelineSlider.Value >= TimelineSlider.Maximum) pos = s0.ToDateTime(new TimeOnly(9, 0));   // 끝에서 누르면 처음부터
            MoveSlider();
            Want();   // 멈춘 뒤 다시 누르면 그 시각부터 다시 그림
            timer.Start();
        }

        /// <summary>❚❚: 누를 때와 뗄 때 둘 다 받음 — 재생 중 Revit 이 다시 그리며 마우스를 가져가 뗌이 안 올 수 있음 (6차 점검 ui7_06, 기록에 멈춤 요청이 없었음). 두 번 와도 같음</summary>
        void PauseUp(object sender, MouseButtonEventArgs e)
        {
            if (!Over(sender, e)) { Dashboard.Log("버튼 멈춤: 버튼 밖에서 놓임 — 무시"); return; }
            ReleaseButton(e);
            MarkMouse(sender);
            Act("멈춤", "마우스", DoPause);
        }

        void PauseClick(object sender, RoutedEventArgs e) { if (!FromMouse(sender)) Act("멈춤", "키보드·화면 읽기", DoPause); }   // (N7)

        void DoPause()
        {
            if (!Dashboard.Playing && !timer.IsEnabled) return;
            StopPlayback(null);
            if (playStart != null) Dashboard.TimelineStop(RestoreView.IsChecked == true);
        }

        // ── 강도 예측 그림 (v6 3단계): 층별 추정 곡선(회색) · 기준선(5·14·fck 파선) — 날짜가 바뀌면 MoveNowLine 이 지금 선·층 점·그날까지 공시체 ◆ 만 다시
        void ChartResized(object sender, SizeChangedEventArgs e) => DrawChart();

        double X(DateTime t)
        {
            double left = 30, w = Math.Max(10, ProcessChart.ActualWidth - left - 4);
            return left + (t - chartStart!.Value.ToDateTime(TimeOnly.MinValue)).TotalDays / chartSpan * w;
        }

        double yMax = 30;
        double Y(double mpa) => 6 + (ProcessChart.Height - 16) * (1 - Math.Clamp(mpa / yMax, 0, 1));   // 65차 점검: 96 px (1080 화면, 60차 R22) · 아래 10 px 여백 — 5 MPa 선이 잘리지 않게

        void DrawChart()
        {
            ProcessChart.Children.Clear();
            nowMarks.Clear();
            if (chartStart == null || line == null || line.Floors.Count == 0) { ProcessChart.Visibility = Visibility.Hidden; return; }
            ProcessChart.Visibility = Visibility.Visible;
            if (ProcessChart.ActualWidth <= 40) return;   // 처음 보일 때는 SizeChanged 에서 다시 그림
            var ink = TryFindResource("OnSite.Brush.Foreground") as Brush ?? Brushes.Gray;
            var all = line.Floors.SelectMany(f => f.Values).Where(v => v != null).Select(v => v!.Value).Concat(line.Cylinders.Select(c => c.Mpa)).Concat(line.Lines.Select(l => l.Mpa));
            yMax = Math.Ceiling(Math.Max(line.Floors.Max(f => f.Fck) * 1.25, all.DefaultIfEmpty(0).Max()) / 5) * 5;
            foreach (var (name, mpa) in line.Lines)   // 기준선
            {
                double y = Y(mpa);
                ProcessChart.Children.Add(new System.Windows.Shapes.Line { X1 = 30, X2 = ProcessChart.ActualWidth - 4, Y1 = y, Y2 = y, Stroke = Brushes.Gray, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }, ToolTip = $"{name} {mpa:0.#} MPa [확정]" });
                var lab = new TextBlock { Text = $"{mpa:0.#}", FontSize = 9, Foreground = ink, ToolTip = name };
                Canvas.SetLeft(lab, 2); Canvas.SetTop(lab, y - 7);
                ProcessChart.Children.Add(lab);
            }
            var unit = new TextBlock { Text = "MPa", FontSize = 8, Foreground = ink };
            Canvas.SetLeft(unit, 2); Canvas.SetTop(unit, -2);   // 위쪽 (회귀 v6-1: 범례가 두 줄이면 그림 아래가 패널 밖으로 밀려 'MPa' 가 안 보였음)
            ProcessChart.Children.Add(unit);
            var curve = new SolidColorBrush(Color.FromRgb(150, 150, 150));
            foreach (var f in line.Floors)   // 층별 추정 곡선 (타설 전 날은 건너뜀)
            {
                var pts = new PointCollection(line.Dates.Select((d, k) => (d, v: f.Values[k])).Where(x => x.v != null).Select(x => new Point(X(x.d.ToDateTime(TimeOnly.MinValue)), Y(x.v!.Value))));
                if (pts.Count > 1) ProcessChart.Children.Add(new System.Windows.Shapes.Polyline { Points = pts, Stroke = curve, StrokeThickness = 1, ToolTip = f.Label + " 추정 강도 (참고)" });
            }
            MoveNowLine();
        }

        /// <summary>그날로 (재생 중이면 멈춤)</summary>
        void JumpTo(DateOnly d, string note)
        {
            if (playStart is not { } s0 || d < s0 || d > s0.AddDays((int)TimelineSlider.Maximum)) return;
            if (Dashboard.Playing) StopPlayback(null);
            pos = d.ToDateTime(new TimeOnly(9, 0));
            MoveSlider();
            Dashboard.TimelineShow();
            Want();
            TimelineNote(note);
        }

        /// <summary>지금 날짜 표시: 빨간 세로선 · 위 4개층 점(강도 색)과 값 · 그날까지 현장양생 공시체 ◆ (초록 = 그 층 fck 이상)</summary>
        void MoveNowLine()
        {
            if (chartStart == null || line == null || ProcessChart.Visibility != Visibility.Visible || ProcessChart.ActualWidth <= 40) return;
            foreach (var m in nowMarks) ProcessChart.Children.Remove(m);
            nowMarks.Clear();
            void Add(UIElement e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); ProcessChart.Children.Add(e); nowMarks.Add(e); }
            var d = DateOnly.FromDateTime(pos);
            double xn = X(pos.Date);
            Add(new System.Windows.Shapes.Line { Y1 = 0, Y2 = ProcessChart.Height, Stroke = new SolidColorBrush(Color.FromRgb(215, 48, 39)), StrokeThickness = 1.5, IsHitTestVisible = false }, xn, 0);
            var fckOf = line.Floors.ToDictionary(f => f.Floor, f => f.Fck);
            var green = new SolidColorBrush(Color.FromRgb(26, 152, 80));
            var ink = TryFindResource("OnSite.Brush.Foreground") as Brush ?? Brushes.Gray;
            foreach (var c in line.Cylinders.Where(c => c.Date <= d && c.Date >= chartStart.Value))
            {
                bool ok = c.Mpa >= fckOf.GetValueOrDefault(c.Floor, double.MaxValue);
                var dm = new System.Windows.Shapes.Polygon { Points = new PointCollection { new(4, 0), new(8, 4), new(4, 8), new(0, 4) }, Fill = ok ? green : ink, Cursor = Cursors.Hand,
                    ToolTip = $"{c.ZoneId} 공시체 {c.Mpa:0.0} MPa ({c.Curing}, {c.Date:MM-dd}) — 실측 · 누르면 그날로" };
                var day = c.Date;
                dm.PreviewMouseLeftButtonUp += (s, e) => { e.Handled = true; JumpTo(day, $"{day:MM-dd} {c.ZoneId} 공시체 {c.Mpa:0.0} MPa"); };
                Add(dm, X(c.Date.ToDateTime(TimeOnly.MinValue)) - 4, Y(c.Mpa) - 4);
            }
            int i = d.DayNumber - line.Dates[0].DayNumber;
            if (i < 0 || i >= line.Dates.Count) return;
            double lastY = double.MinValue;
            foreach (var f in line.Floors.Where(f => f.Values[i] != null).OrderByDescending(f => f.Floor).Take(4).OrderBy(f => Y(f.Values[i]!.Value)))
            {
                double v = f.Values[i]!.Value, y = Y(v);
                var c = FourD.StrengthColor(v, f.Fck);
                Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)), Stroke = ink, StrokeThickness = 0.8, ToolTip = $"{f.Label} 추정 {v:0.0} MPa (참고)" }, xn - 3.5, y - 3.5);
                double ly = Math.Max(y, lastY + 10);   // 글자가 겹치면 아래로 밀기 (65차 점검: 9F·11F 라벨)
                lastY = ly;
                Add(new TextBlock { Text = $"{f.Label} {v:0.0}", FontSize = 9, Foreground = ink, IsHitTestVisible = false }, xn + 5, ly - 7);
            }
        }

        // UIControlledApplication.ThemeChanged 에서 호출 (UIThemeManager.CurrentTheme == UITheme.Dark)
        public void ApplyTheme(bool dark) =>
            Resources.MergedDictionaries[1] = new ResourceDictionary
            {
                Source = new Uri($"/RevitOnSite;component/Wpf/OnSiteTheme.{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative)
            };

        /// <summary>격자 한 칸: 글자(예측 해체일 MM-DD), 색, 그 구역</summary>
        /// <summary>칸 = 위 상태 약자(굵게) + 아래 예측일 MM-dd (24차 ui24_01: 한 줄이면 칸 폭에서 잘림)</summary>
        public sealed record Cell(string Text, Brush Bg, Brush Fg, Dashboard.ZoneInfo Zone, string Due = "");
        public sealed record FloorRow(string Floor, Cell[] Cells);

        DateTime? appliedEval;   // 마지막으로 기준일 칸에 넣은 "마지막 판정일" — 바뀔 때만 다시 넣어 사용자가 고른 날짜를 덮지 않음

        /// <param name="mode">활성 뷰의 표시 (상태/강도/일정)</param>
        /// <param name="lastEval">마지막 판정 기준일 (없으면 null → 오늘 그대로)</param>
        public void Load(IReadOnlyList<string> columns, IReadOnlyList<FloorRow> rows, int ready, int keep, int cyl, IReadOnlyList<string> warnings, string info,
                         string mode = "상태", DateTime? lastEval = null, int approved = 0, int removed = 0, int reshored = 0, string alertLine = null, IReadOnlyList<Engine.Alert> alerts = null,
                         IReadOnlyList<string> kpi = null, IReadOnlyList<string> trend = null, int defect = 0)
        {
            zoneById = rows.SelectMany(r => r.Cells).Where(c => c.Zone != null).GroupBy(c => c.Zone.ZoneId).ToDictionary(g => g.Key, g => g.First().Zone);
            alertsNow = (alerts ?? Array.Empty<Engine.Alert>()).Where(a => RoleShows(a.Kind)).ToList();   // W13 역할 보기 — 그 역할 줄만
            if (role != "전체" && role != "공무" && alertsNow.Count < (alerts?.Count ?? 0)) alertLine = alertsNow.Count > 0 ? Engine.Alerts.Summary(alertsNow) : null;
            // 심각한 것(무단 해체 의심)은 처음 나온 구역일 때만 펼침 — 날짜를 옮기거나 새로 고쳐도 사용자가 고른 펼침/접힘 유지 (회귀 v6 R21: 날짜 이동마다 다시 펼쳐져 강도 그림을 가렸음)
            if (rows.Count == 0) { seenSerious.Clear(); seenFlag.Clear(); openFloor = null; }   // 프로젝트를 닫음
            int seen = seenSerious.Count;
            seenSerious.UnionWith(alertsNow.Where(a => a.Kind == "무단 해체 의심").Select(a => a.ZoneId));
            if (seenSerious.Count > seen) AlertBox.IsExpanded = true;
            // 안전 신호(무단 해체 의심·타설 보류) 층은 처음 나올 때 그 층을 펼침 (새로 나온 것 중 가장 위층 하나) — 뒤로는 사용자가 고른 펼침/접힘 그대로
            var fresh = alertsNow.Where(a => Flag(a.Kind) && seenFlag.Add(a.Kind + "|" + a.ZoneId)).Select(a => FloorOf(a.ZoneId)).ToList();
            if (fresh.Count > 0) openFloor = fresh.Max();   // 85차: 새로 고침 때는 굴리지 않음 (강도 그림이 첫 화면에 남게)
            KpiLine.Text = kpi == null || kpi.Count == 0 ? "" : "지표 — " + string.Join(" · ", kpi);
            KpiLine.ToolTip = kpi == null || kpi.Count == 0 ? null : "지표 (기존 기록을 센 것 — 판정과 무관, docs/impact_map.md)\n" + string.Join("\n", kpi)
                + (trend is { Count: > 0 } ? "\n\n" + string.Join("\n", trend) : "");   // F13 추이 (최근 5회 판정)
            KpiLine.Visibility = kpi == null || kpi.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            // 61차 (리허설 7단계: 요청 가능이 오늘 볼 것에 안 나옴) — 맨 아래 '요청 가능 n — 검측요청서 차례' + 구역 칩. 오늘 볼 것이 없으면 이 줄이 머리
            readyZones = zoneById.Values.Where(z => z.Status == 5).OrderBy(z => z.Floor).ThenBy(z => z.ZoneId, StringComparer.Ordinal).ToList();
            string readyText = readyZones.Count > 0 ? $"요청 가능 {readyZones.Count} — 검측요청서 차례" : null;
            BuildFloors();
            AlertLine.Text = !string.IsNullOrEmpty(alertLine) ? alertLine : readyText ?? "";
            AlertLine.SetResourceReference(TextBlock.ForegroundProperty, string.IsNullOrEmpty(alertLine) ? "OnSite.Brush.Foreground" : "OnSite.Brush.Error");
            AlertBox.Visibility = string.IsNullOrEmpty(AlertLine.Text) ? Visibility.Collapsed : Visibility.Visible;
            if (lastEval != null && lastEval != appliedEval) { BaseDate.SelectedDate = lastEval; appliedEval = lastEval; }   // 5차 점검 L3
            MatrixHeader.Text = "층 × 구역 (칸 = 상태 약자 · 예측 해체일) · 뷰 표시: " + mode;
            if (mode != viewMode) { viewMode = mode; RebuildLegend(); }   // 11-12 2-5: 범례가 활성 뷰 색 기준을 따라감   // 5차 점검 L2 — 활성 뷰의 3D 색 (상태/강도/일정)
            MatrixHeader.ToolTip = "칸 색과 앞 글자는 늘 판정 상태입니다: 미검 = 미검측 · 합격 = 설치 합격 · !결함 = 설치 결함 · 양생 = 양생 중 · 공시 = 공시체 시험 필요 · "
                + "요청 = 해체 승인 요청 가능 · 감리 = 감리 승인 기록 · 완료 = 해체 완료 · 재받침 · 존치 = 존치 연장.\n'뷰 표시'는 활성 뷰에서 [표시] 토글로 고른 색 기준 (범례 뷰 참고).";
            Matrix.Columns.Clear();
            // 층 이름 열은 글자 폭에 맞춤 (1F·10F·PH 가 잘리던 문제, 5차 점검 B3)
            Matrix.Columns.Add(new DataGridTextColumn { Header = "층", Binding = new Binding("Floor"), Width = DataGridLength.SizeToCells, MinWidth = 36 });
            var statusCell = (Style)FindResource("OnSite.StatusCell");
            for (int i = 0; i < columns.Count; i++)
            {
                var style = new Style(typeof(DataGridCell), statusCell);
                style.Setters.Add(new Setter(BackgroundProperty, new Binding($"Cells[{i}].Bg")));
                style.Setters.Add(new Setter(ForegroundProperty, new Binding($"Cells[{i}].Fg")));
                style.Setters.Add(new Setter(ToolTipProperty, new Binding($"Cells[{i}].Zone.Tip")));
                // 두 줄 칸: 열이 많으면(KBIM 12열) 글자·최소 폭을 줄여 가로 스크롤 없이 — 넘치는 글자는 … (전체는 칸 툴팁)
                bool many = columns.Count > 8;
                var panel = new FrameworkElementFactory(typeof(StackPanel));
                panel.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);
                foreach (var (path, bold) in new[] { ($"Cells[{i}].Text", true), ($"Cells[{i}].Due", false) })
                {
                    var tb = new FrameworkElementFactory(typeof(TextBlock));
                    tb.SetBinding(TextBlock.TextProperty, new Binding(path));
                    tb.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                    tb.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
                    tb.SetValue(TextBlock.FontSizeProperty, many ? (bold ? 12.0 : 11.0) : (bold ? 13.0 : 12.0));
                    if (bold) tb.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
                    panel.AppendChild(tb);
                }
                Matrix.Columns.Add(new DataGridTemplateColumn
                {
                    Header = new TextBlock { Text = columns[i] }, CellTemplate = new DataTemplate { VisualTree = panel },
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = many ? 34 : 40, CellStyle = style
                });
            }
            Matrix.RowHeight = columns.Count > 8 ? 32 : 36;   // 24차 S1: 칸 두 줄(약자 + 날짜)이 기본 줄 높이 20 에 잘림
            Matrix.ItemsSource = rows;
            ScrollMatrixToJudged(rows, lastEval);
            ChipReady.Text = $"해체 승인 요청 가능 {ready}구역";
            ChipKeep.Text = $"존치 {keep}구역" + (defect > 0 ? $" (설치 결함 {defect})" : "");   // 36차: 상태 2 '존치 (설치 결함 기록)'도 존치 — 칩 합계 = 구역 수
            ChipCyl.Text = $"공시체 시험 필요 {cyl}구역";
            void Done(FrameworkElement box, TextBlock t, string name, int n) { t.Text = $"{name} {n}구역"; box.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed; }
            Done(ChipApprovedBox, ChipApproved, "감리 승인 기록", approved);
            Done(ChipRemovedBox, ChipRemoved, "해체 완료", removed);
            Done(ChipReshoredBox, ChipReshored, "재받침", reshored);
            Chips.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            Info.Text = info ?? "";
            Info.Visibility = string.IsNullOrEmpty(info) ? Visibility.Collapsed : Visibility.Visible;
            Warnings.ItemsSource = warnings;
            WarnHeader.Text = $"경고 ({warnings.Count})";
        }

        /// <summary>마우스가 누른 칸의 구역 (머리글·스크롤바·빈 칸·층 열이면 null)</summary>
        Dashboard.ZoneInfo rightZone;   // 마지막으로 우클릭한 칸

        Dashboard.ZoneInfo CurrentZone(MouseButtonEventArgs e)
        {
            // 누른 칸 자체에서 열·행을 읽음 — 창에 포커스가 없어 첫 누름이 활성화에 쓰이면 CurrentCell 이 아직 이전 칸이므로 (5차 점검 B1)
            var d = e.OriginalSource as DependencyObject;
            while (d != null && d is not DataGridCell) d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            if (d is not DataGridCell cell || cell.Column == null || cell.DataContext is not FloorRow row || cell.Column.DisplayIndex < 1) return null;
            int i = cell.Column.DisplayIndex - 1;
            return i < row.Cells.Length ? row.Cells[i].Zone : null;
        }

        /// <summary>11-12 2-5 (Revit #5): 활성 뷰 표시 (Dashboard 가 Load 로 넘김) — "상태"·"강도"·"일정" (ZoneOverlay) 또는 TimelineMode (강도 타임라인 뷰)</summary>
        public const string TimelineMode = "강도 타임라인";
        string viewMode = "상태";

        /// <summary>범례 칩 줄 = 지금 활성 뷰의 실제 색 기준 (11-12 2-5): 강도 타임라인 뷰 = [색] 콤보(동바리 상태여도 타임라인은 추정 강도 색) ·
        /// [색: 동바리 상태] = 검측 뷰 동바리 상태 (V2) · [표시: 강도] = 추정 강도 5단계 · [표시: 일정] = 남은 날 5단계 · 그 밖 = 구역 상태 0–9 (뷰 필터·격자 칸 색)</summary>
        void RebuildLegend()
        {
            if (ColorMode == null || Legend == null) return;
            if (viewMode == TimelineMode) BuildLegend(ColorMode.SelectedIndex == 1);
            else if (ColorMode.SelectedIndex == 2) BuildShoreLegend();
            else if (viewMode == "강도") Fill(ZoneOverlay.StrengthSteps.Select(t => (B(t.C.Red, t.C.Green, t.C.Blue), t.Label, (string)null)).Append(NoData()).ToList(), "구역 색 범례 ([표시: 강도])",
                "[표시: 강도] — 추정 강도 ÷ fck (참고 · 판정에 안 씀), 짙을수록 강함. 구간은 화면 표시용. 판정 상태로 되돌리려면 [표시: 상태].");
            else if (viewMode == "일정") Fill(ZoneOverlay.DueSteps.Select(t => (B(t.C.Red, t.C.Green, t.C.Blue), t.Label, (string)null)).Append(NoData()).ToList(), "구역 색 범례 ([표시: 일정])",
                "[표시: 일정] — 오늘부터 예측 해체일까지 남은 날. 판정 상태로 되돌리려면 [표시: 상태].");
            else Fill(ZoneEvaluation.Styles.Select(t => (B(t.R, t.G, t.B), Dashboard.StatusShort.GetValueOrDefault(t.Code, t.Name), $"{t.Code} {t.Name}")).ToList(), "구역 상태 범례 (검측 뷰·격자 칸)",
                "구역 상태 색 (검측 뷰 필터 = 격자 칸 색): " + string.Join(" · ", ZoneEvaluation.Styles.Select(t => $"{Dashboard.StatusShort.GetValueOrDefault(t.Code, "")} = {t.Name}")));
        }

        static Brush B(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));
        static (Brush, string, string) NoData() => (B(ZoneOverlay.NoData.Red, ZoneOverlay.NoData.Green, ZoneOverlay.NoData.Blue), "값 없음", null);

        /// <summary>범례 칩 줄을 채움: 색 견본 + 글자(툴팁 = 긴 이름). UIA 이름 = 보이는 글자 전체</summary>
        void Fill(List<(Brush Fill, string Text, string Tip)> items, string name, string tip)
        {
            Legend.Children.Clear();
            foreach (var (fill, text, itemTip) in items)
            {
                var sw = new Border { Width = 11, Height = 11, Margin = new Thickness(0, 0, 3, 0), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, Background = fill };
                var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 8, 1), ToolTip = itemTip };
                chip.Children.Add(sw);
                chip.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
                Legend.Children.Add(chip);
            }
            System.Windows.Automation.AutomationProperties.SetName(Legend, name + ": " + string.Join(" · ", items.Select(i => i.Tip ?? i.Text)));
            Legend.ToolTip = tip;
        }

        /// <summary>동바리 상태 범례 칩 (Engine ShoreStatus.Legend — 이름·색·그리기 방식, 검측 뷰 V2)</summary>
        void BuildShoreLegend()
        {
            Legend.Children.Clear();
            var items = Engine.ShoreStatus.Legend.ToList();
            foreach (var (name, r, g, b, draw) in items)
            {
                var c = new SolidColorBrush(Color.FromRgb(r, g, b));
                bool solid = draw is "채움" or "깜빡임";
                var sw = new Border { Width = 11, Height = 11, Margin = new Thickness(0, 0, 3, 0), BorderThickness = new Thickness(solid ? 1 : 2), BorderBrush = solid ? Brushes.Gray : c,
                    Background = solid ? c : Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
                if (draw == "점선") sw = new Border { Width = 11, Height = 11, Margin = sw.Margin, VerticalAlignment = VerticalAlignment.Center,
                    Child = new System.Windows.Shapes.Rectangle { Stroke = c, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 2, 1.5 } } };
                var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 8, 1) };
                chip.Children.Add(sw);
                chip.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
                Legend.Children.Add(chip);
            }
            System.Windows.Automation.AutomationProperties.SetName(Legend, "동바리 상태 범례: " + string.Join(" · ", items.Select(l => l.Name + " " + l.Draw)));
            Legend.ToolTip = "동바리 상태 (검측 뷰, 마지막 판정 시각 기준 · 기록 기준): 채움 = 서 있어야 함(존치·공시체 시험 필요·타설 전) · 해체 승인 요청 가능 · 감리 확인 기록, "
                + "윤곽 = 해체됨(해체 완료·협력업체 신고), 빨강 깜빡임 = 무단 해체 의심(승인 기록 없이 동바리 없음 관측), 구역 테두리 = 설치 검측 필요(주황)·NG(빨강), 점선 = 다음 타설 전 확인 대상인데 최근 24 h 동바리 확인 없음, "
                + "NG 동바리 = 휴대폰 동바리 점검의 풀리지 않은 NG (빨강 테두리 — 같은 항목을 다시 점검해 OK 로 기록하면 사라짐). 판정은 바꾸지 않습니다.";
        }

        /// <summary>강도 타임라인 뷰 범례: 추정 강도 (참고) / 근거 단계 0–8</summary>
        void BuildLegend(bool ev)
        {
            var items = new List<(Brush, string, string)>();
            if (ev)
                for (int k = 0; k <= 8; k++)
                {
                    var c = FourD.EvidenceColor(k);
                    items.Add((B(c.R, c.G, c.B), k == 0 ? "0 없음" : $"{k} {Engine.Evidence.StageName(k)}", null));
                }
            else
            {
                foreach (var (c, label) in ZoneOverlay.StrengthSteps) items.Add((B(c.Red, c.Green, c.Blue), label, null));
                var g = FourD.StrengthColor(null, 1);
                items.Add((B(g.R, g.G, g.B), "타설 전", null));
            }
            Fill(items, ev ? "강도 타임라인 색 범례 (근거 단계)" : "강도 타임라인 색 범례 (추정 강도 ÷ fck, 참고)",
                ev ? "강도 타임라인 뷰 — 근거 단계: 1 설치검측부터 빈칸 없이 갖춘 마지막 단계, 회색 0 → 진녹색 8. 그날 넣은 서류는 다음 날부터 셉니다. 서류는 [근거 원장]."
                   : "강도 타임라인 뷰 — 추정 강도 ÷ 그 층 fck (참고 · 판정에 안 씀): 짙을수록 강함, 구간은 화면 표시용. 판정은 공시체·3 m 안 센서 실측으로만 — 대시보드 칸 색이 판정 상태입니다.");
        }

        /// <summary>26차: 격자를 처음 볼 때(판정·층 수가 바뀔 때만) 판정된 최상층 바로 위 층이 맨 위에 오게 — 타설 전 빈 층만 보이며 시작하지 않게. 사용자가 굴린 위치는 그 뒤로 건드리지 않음</summary>
        object matrixScrollKey;
        void ScrollMatrixToJudged(IReadOnlyList<FloorRow> rows, DateTime? lastEval)
        {
            int top = -1;
            for (int i = 0; i < rows.Count && top < 0; i++)
                if (rows[i].Cells.Any(c => c.Zone != null && !string.IsNullOrEmpty(c.Zone.Decision))) top = i;
            int target = Math.Max(0, top - 1);
            var key = (lastEval, rows.Count, target);
            if (Equals(key, matrixScrollKey)) return;
            matrixScrollKey = key;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                DependencyObject d = Matrix;
                while (d != null && d is not ScrollViewer) d = VisualTreeHelper.GetChildrenCount(d) > 0 ? VisualTreeHelper.GetChild(d, 0) : null;
                (d as ScrollViewer)?.ScrollToVerticalOffset(target);   // DataGrid 는 줄 단위 스크롤 — 오프셋 = 줄 번호
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        readonly HashSet<string> seenSerious = new();   // 이미 펼쳐 보인 무단 해체 의심 구역 (회귀 v6 R21)
        // 79차 (사용자 11-08 "요청 가능한 구역 … 중구난방으로 뜨지 말고 토글 사용해서 몇 층 열람 이후 해당 층 어느 구역이 존재하는지 조회" · 총괄): '오늘 볼 것' 펼친 칸 = 층별 토글.
        //   층 줄 "13F ▸ ■ 무단 해체 의심 1 · 요청 가능 3 · 존치 2" → 누르면 그 층의 알림 줄 + 요청 가능 구역 줄, 줄을 누르면 그 구역 선택·확대 (기존 동작).
        //   층 순서 = 위층부터, ■·▲ 알림이 있는 층은 맨 위 (처음 나올 때 펼침). 펼침은 사용자가 고른 대로 — 새로 고침·날짜 이동에도 유지. 계산 없음 (알림·상태를 층으로 묶기만)
        const int NoFloor = int.MinValue;   // 모델에 없는 구역의 알림
        List<Engine.Alert> alertsNow = new();
        List<Dashboard.ZoneInfo> readyZones = new();
        int? openFloor;   // 펼친 층 하나 (80차 점검: 여러 층이 함께 펼쳐져 고른 층 줄이 칸 밖으로 밀렸음 → 한 번에 한 층)
        readonly HashSet<string> seenFlag = new();   // 펼쳐 보인 안전 신호 알림 (종류|구역)

        int FloorOf(string zid) => zoneById.TryGetValue(zid, out var z) ? z.Floor : NoFloor;
        /// <summary>안전 신호 = 무단 해체 의심(■)·타설 보류(▲ 아래층 동바리 확인) — 그 층을 맨 위에, 처음 나올 때 펼침. 시험 필요(▲)는 할 일이라 층 순서대로 (80차: 4개 층이 한꺼번에 펼쳐졌음)</summary>
        static bool Flag(string kind) => kind is "무단 해체 의심" or "타설 보류";

        void BuildFloors(int? focus = null)
        {
            FloorList.Children.Clear();
            var floors = alertsNow.Select(a => FloorOf(a.ZoneId)).Concat(readyZones.Select(z => z.Floor)).Distinct()
                .OrderByDescending(f => alertsNow.Any(a => Flag(a.Kind) && FloorOf(a.ZoneId) == f)).ThenBy(f => f == NoFloor).ThenByDescending(f => f).ToList();
            FloorScroll.Visibility = floors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            Button keep = null;
            foreach (var fl in floors)
            {
                var al = alertsNow.Where(a => FloorOf(a.ZoneId) == fl).ToList();
                var rz = readyZones.Where(z => z.Floor == fl).ToList();
                string label = fl == NoFloor ? "모델에 없는 구역" : zoneById.Values.FirstOrDefault(z => z.Floor == fl)?.Label ?? Engine.FloorSign.FloorName(fl, null);
                int keepN = fl == NoFloor ? 0 : zoneById.Values.Count(z => z.Floor == fl && z.Status is 2 or 3 or 9);   // 대시보드 '존치' 칩과 같은 셈
                var parts = Engine.Alerts.Order.Select(k => (k, n: al.Count(a => a.Kind == k))).Where(q => q.n > 0).Select(q => $"{Sym(q.k)} {q.k} {q.n}").ToList();
                if (rz.Count > 0) parts.Add($"요청 가능 {rz.Count}");
                if (keepN > 0) parts.Add($"존치 {keepN}");
                bool open = openFloor == fl;
                var head = new Button
                {
                    Tag = fl, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 1), Padding = new Thickness(4, 1, 4, 1),
                    Content = new TextBlock { Text = $"{label} {(open ? "▾" : "▸")} {string.Join(" · ", parts)}", TextWrapping = TextWrapping.Wrap, FontWeight = open ? FontWeights.SemiBold : FontWeights.Normal },
                    ToolTip = open ? $"{label} 줄 접기" : $"{label} 펼치기 — 알림·요청 가능 구역 줄 (줄을 누르면 그 구역 선택·확대)",
                };
                System.Windows.Automation.AutomationProperties.SetName(head, $"{label} {string.Join(" · ", parts)} {(open ? "접기" : "펼치기")}");
                head.PreviewMouseLeftButtonUp += FloorUp;
                head.Click += FloorClick;
                FloorList.Children.Add(head);
                if (fl == focus) keep = head;
                if (!open) continue;
                foreach (var a in al) FloorList.Children.Add(ZoneLine(Sym(a.Kind) + " [" + a.Kind + "] " + a.Text, a.ZoneId));
                foreach (var z in rz) FloorList.Children.Add(ZoneLine("● [요청 가능] " + z.ZoneId + " — 검측요청서 차례", z.ZoneId));
            }
            keep?.Focus();   // 키보드로 펼쳤으면 그 층 줄에 포커스 그대로
        }

        /// <summary>층 아래 한 줄 (알림·요청 가능 구역) — 누르면 그 구역 선택·확대, 모델에 없는 구역이면 꺼진 줄</summary>
        Button ZoneLine(string text, string zid)
        {
            bool here = zoneById.TryGetValue(zid, out var z);
            var b = new Button
            {
                Tag = z, IsEnabled = here, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(14, 0, 0, 1), Padding = new Thickness(4, 0, 4, 0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 36 },
                ToolTip = here ? "누르면 " + zid + " 선택·확대" : zid + " — 이 모델 구역에 없음",
            };
            ToolTipService.SetShowOnDisabled(b, true);
            System.Windows.Automation.AutomationProperties.SetName(b, text);
            b.PreviewMouseLeftButtonUp += ReadyUp;
            b.Click += ReadyClick;
            return b;
        }

        /// <summary>펼친 층 줄이 보이게 (사용자가 펼쳤을 때만 — 바깥 스크롤): 마지막 줄 → 머리 순서로 (머리가 위에 오도록)</summary>
        void RevealOpen()
        {
            FrameworkElement head = null, last = null;
            bool inBlock = false;
            foreach (FrameworkElement c in FloorList.Children)
            {
                if (c.Tag is int f) { if (inBlock) break; inBlock = f == openFloor; if (inBlock) head = c; }
                if (inBlock) last = c;
            }
            last?.BringIntoView();
            head?.BringIntoView();
        }

        void FloorToggle(Button t)
        {
            int fl = (int)t.Tag;
            openFloor = openFloor == fl ? null : fl;   // 다른 층을 누르면 그 층만 펼침 (앞 층은 접힘)
            BuildFloors(t.IsKeyboardFocused ? fl : null);
            if (openFloor != null) Dispatcher.BeginInvoke(new Action(RevealOpen), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>85차: 칸 안 스크롤(격자·경고)이 그 방향 끝이면 휠을 바깥(패널) 스크롤로</summary>
        void WheelToPage(object sender, MouseWheelEventArgs e)
        {
            DependencyObject d = (DependencyObject)sender;
            while (d != null && d is not ScrollViewer) d = VisualTreeHelper.GetChildrenCount(d) > 0 ? VisualTreeHelper.GetChild(d, 0) : null;
            if (d is ScrollViewer inner && (e.Delta > 0 ? inner.VerticalOffset > 0 : inner.VerticalOffset < inner.ScrollableHeight)) return;
            e.Handled = true;
            PageScroll.ScrollToVerticalOffset(PageScroll.VerticalOffset - e.Delta / 3.0);
        }

        void FloorUp(object sender, MouseButtonEventArgs e)
        {
            if (!Over(sender, e)) return;
            ReleaseButton(e);
            MarkMouse(sender);
            Act("오늘 볼 것 층 " + ((FrameworkElement)sender).Tag, "마우스", () => FloorToggle((Button)sender));
        }
        void FloorClick(object sender, RoutedEventArgs e) { if (!FromMouse(sender)) Act("오늘 볼 것 층 " + ((FrameworkElement)sender).Tag, "키보드·화면 읽기", () => FloorToggle((Button)sender)); }

        static string Sym(string kind) => kind switch { "무단 해체 의심" => "■", "타설 보류" or "순찰 확인 없음" or "감리 확인 못 넣음" or "공시체 미입력 (예측일 지남)" => "▲", _ => "●" };   // 경고 목록과 같은 심각도 기호
        Dictionary<string, Dashboard.ZoneInfo> zoneById = new();

        void ReadyUp(object sender, MouseButtonEventArgs e)
        {
            if (!Over(sender, e) || ((FrameworkElement)sender).Tag is not Dashboard.ZoneInfo z) return;
            ReleaseButton(e);
            MarkMouse(sender);
            Act("오늘 볼 것 줄 " + z.ZoneId, "마우스", () => Dashboard.SelectZone(z));
        }
        void ReadyClick(object sender, RoutedEventArgs e) { if (!FromMouse(sender) && ((FrameworkElement)sender).Tag is Dashboard.ZoneInfo z) Act("오늘 볼 것 줄 " + z.ZoneId, "키보드·화면 읽기", () => Dashboard.SelectZone(z)); }

        void CellClick(object sender, MouseButtonEventArgs e)
        {
            var z = CurrentZone(e);
            if (z != null) Dashboard.SelectZone(z);
        }

        void CellDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var z = CurrentZone(e);
            if (z != null) Dashboard.ShowReason(z);
        }

        /// <summary>
        /// 버튼은 놓을 때(PreviewMouseLeftButtonUp) 실행 — 도킹 창 첫 클릭의 누름이 창 활성화에 쓰여 Click 이 안 생기는 문제 (5차 점검 B1).
        /// 이 이벤트를 Handled 로 막으면 Button 이 누를 때 잡은 마우스 캡처를 놓지 못해, 그 뒤 클릭이 전부 그 버튼으로 가고 Revit 3D 뷰 선택·실행 취소도 먹지 않았음
        /// (6차 점검 ui7_06·08·12: ❚❚ 가 ▶ 로, [전체 층] 이 [같은 층만] 으로 — 기록에 '같은 층 보기' 7번·'4D 멈춤' 0번). → 뗌은 막지 않고 캡처를 바로 놓는다.
        /// 누름(Down, ❚❚)은 Handled 로 막아 Button 이 캡처를 아예 잡지 않게
        /// </summary>
        /// <summary>마우스가 그 버튼 위에서 놓였는가 — 이벤트 좌표로 판정. IsMouseOver 는 모달 창(생성 결과 등)이 닫힌 직후처럼
        /// 마우스 이동이 없으면 낡은 값일 수 있어 첫 클릭을 버릴 수 있었음 (12차 점검 N11 의심: 재생성 뒤 [판정] 무반응, 기록에 요청 없음)</summary>
        static bool Over(object sender, MouseButtonEventArgs e)
        {
            var b = (FrameworkElement)sender;
            var p = e.GetPosition(b);
            return p.X >= 0 && p.Y >= 0 && p.X <= b.ActualWidth && p.Y <= b.ActualHeight;
        }

        /// <summary>버튼 처리: 무엇이 어떻게 눌렸는지 대시보드 기록에 남기고(다음에 무반응이면 처리기까지 왔는지 바로 앎), 예외는 삼키지 않고 알림</summary>
        static void Act(string name, string via, Action a)
        {
            Dashboard.Log("버튼 " + name + " (" + via + ")");
            try { a(); }
            catch (Exception ex)
            {
                Dashboard.Log("버튼 " + name + " 예외 — " + ex);
                MessageBox.Show("대시보드 [" + name + "] 를 처리하지 못했습니다.\n" + ex.Message, App.TabName);
            }
        }

        // 마우스로 누르면 놓기 처리(…Up) 뒤 Button 의 Click 도 올 수 있음 → 같은 버튼의 놓기 직후 Click 은 건너뜀 (11차 점검 N7)
        object lastMouseButton;
        long lastMouseTick;
        void MarkMouse(object sender)
        {
            lastMouseButton = sender;
            lastMouseTick = Environment.TickCount64;
            // 마우스로 누른 경우 처리 뒤 키보드 포커스를 Revit 활성 뷰로 (12차 점검 N9: 남아 있으면 다음 Space/Enter 가 판정을 다시 돌리고 VV 같은 단축키가 안 먹음).
            // Tab 으로 온 키보드 포커스(Click 경로)는 그대로. Click 이 끝난 뒤 하도록 Background 로 미룸
            Dispatcher.BeginInvoke(new Action(FocusRevitView), System.Windows.Threading.DispatcherPriority.Background);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);

        static string Win(IntPtr h, bool cls) { var b = new System.Text.StringBuilder(512); _ = cls ? GetClassName(h, b, 512) : GetWindowText(h, b, 512); return b.ToString(); }

        /// <summary>Revit 활성 뷰 캔버스(보이는 AfxFrameOrView… 창, 부모 제목 "3D 뷰: 이름 - 문서.rvt" = 메인 창 제목 "[문서.rvt - 3D 뷰: 이름]")에 키보드 포커스.
        /// Revit 2026 은 MDIClient 가 없음 (창 구조는 MCP 밖에서 읽기만으로 확인). 못 찾으면 보이는 첫 뷰, 그것도 없으면 메인 창</summary>
        static void FocusRevitView()
        {
            try
            {
                var main = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (main == IntPtr.Zero) return;
                var t = Win(main, false);
                int a = t.IndexOf('['), b = t.LastIndexOf(']');
                string active = a >= 0 && b > a ? t[(a + 1)..b] : "";
                IntPtr target = IntPtr.Zero, first = IntPtr.Zero;
                EnumChildWindows(main, (h, l) =>
                {
                    if (!IsWindowVisible(h) || !Win(h, true).StartsWith("AfxFrameOrView")) return true;
                    if (first == IntPtr.Zero) first = h;
                    var pt = Win(GetParent(h), false);
                    int i = pt.LastIndexOf(" - ");
                    if (active.Length > 0 && i > 0 && active == pt[(i + 3)..] + " - " + pt[..i]) { target = h; return false; }
                    return true;
                }, IntPtr.Zero);
                Keyboard.ClearFocus();
                SetFocus(target != IntPtr.Zero ? target : first != IntPtr.Zero ? first : main);
            }
            catch (Exception ex) { Dashboard.Log("키보드 포커스 되돌리기 실패: " + ex.Message); }
        }
        bool FromMouse(object sender) => ReferenceEquals(sender, lastMouseButton) && Environment.TickCount64 - lastMouseTick < 700;

        static void ReleaseButton(MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) e.Handled = true;   // 누름: 캡처를 잡지 않게
            else Mouse.Capture(null);                                           // 뗌: 잡힌 캡처를 놓음 (Click 은 연결된 처리기가 없어 아무 일 없음)
        }

        void TodayUp(object sender, MouseButtonEventArgs e)
        {
            if (!Over(sender, e)) { Dashboard.Log("버튼 오늘: 버튼 밖에서 놓임 — 무시"); return; }
            ReleaseButton(e);
            MarkMouse(sender);
            Act("오늘", "마우스", DoToday);
        }

        void TodayClick(object sender, RoutedEventArgs e) { if (!FromMouse(sender)) Act("오늘", "키보드·화면 읽기", DoToday); }   // (N7)

        void DoToday()
        {
            BaseDate.SelectedDate = DateTime.Today;
        }

        // 구역 보기 (ZoneFocus)
        /// <summary>구역 색: 추정 강도 / 근거 단계 / 동바리 상태(검측 뷰). 멈춰 있으면 지금 날짜를 새 색으로 다시 그림 (재생 중이면 다음 프레임부터)</summary>
        void ColorModeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsInitialized || ColorMode == null || Legend == null || EvSwatches == null || ColorMode.SelectedIndex < 0) return;   // InitializeComponent 중 (기본 = 판정 상태) — 91차: 순서 바꾼 뒤 EvSwatches 가 ColorMode 뒤라 Revit 시작 때 null
            bool ev = ColorMode.SelectedIndex == 1, shore = ColorMode.SelectedIndex == 2;
            Timeline.SetEvidenceColors(ev);                 // 동바리 상태일 때 타임라인은 추정 강도 색 (재생 중엔 타임라인 색 우선 — 총괄 11-05)
            ZoneFocus.SetShoreStatus(shore);                // V2: 검측 뷰 동바리 덧그림 켬·끔
            Dashboard.Log("4D 색: " + (ev ? "근거 단계" : shore ? "동바리 상태 (검측 뷰)" : "추정 강도"));
            EvSwatches.Visibility = Visibility.Collapsed;   // 범례 칩 줄(Legend)이 대신 보여 줌
            RebuildLegend();
            if (!shore && playStart != null && !Dashboard.Playing) { Dashboard.TimelineShow(); Want(); }   // 동바리 상태는 검측 뷰 덧그림 — 타임라인 뷰로 바꾸지 않음 (34차 V2 ⓑ)
        }

        void ShoreModeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ShoreMode != null && ShoreMode.SelectedIndex >= 0) Dashboard.ShoreMode(ShoreMode.SelectedIndex);   // 처음(InitializeComponent) 0 = 기본값이라 요청 없음
        }

        void SameFloorUp(object sender, MouseButtonEventArgs e)
        {
            if (!Over(sender, e)) { Dashboard.Log("버튼 같은 층만: 버튼 밖에서 놓임 — 무시"); return; }
            ReleaseButton(e);
            MarkMouse(sender);
            Act("같은 층만", "마우스", DoSameFloor);
        }

        void SameFloorClick(object sender, RoutedEventArgs e) { if (!FromMouse(sender)) Act("같은 층만", "키보드·화면 읽기", DoSameFloor); }   // (N7)

        void DoSameFloor()
        {
            Dashboard.SameFloor();
        }

        void AllFloorsUp(object sender, MouseButtonEventArgs e)
        {
            if (!Over(sender, e)) { Dashboard.Log("버튼 전체 층: 버튼 밖에서 놓임 — 무시"); return; }
            ReleaseButton(e);
            MarkMouse(sender);
            Act("전체 층", "마우스", DoAllFloors);
        }

        void AllFloorsClick(object sender, RoutedEventArgs e) { if (!FromMouse(sender)) Act("전체 층", "키보드·화면 읽기", DoAllFloors); }   // (N7)

        void DoAllFloors()
        {
            Dashboard.AllFloors();
        }

        // ── v6.2 W13 오늘 할 일·역할 보기 (기능은 그대로 — 보이는 단추·오늘 볼 것 줄만)
        static readonly string[] RoleNames = { "전체", "공무", "안전", "품질" };
        static readonly Dictionary<string, (string[] Kinds, bool Judge, bool Request, bool Sign)> Roles = new()
        {
            ["안전"] = (new[] { "무단 해체 의심", "타설 보류", "순찰 확인 없음", "감리 확인 못 넣음" }, false, false, true),
            ["품질"] = (new[] { "공시체 미입력 (예측일 지남)", "입회 없음", "상태 바뀜", "감리 확인 못 넣음" }, true, true, false),
        };
        static string role = ReadRole();
        static string RoleFile => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RevitOnSite", "dashboard.json");

        static string ReadRole()
        {
            try
            {
                using var d = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(RoleFile));
                return d.RootElement.TryGetProperty("role", out var v) && v.GetString() is string r && RoleNames.Contains(r) ? r : "전체";
            }
            catch (Exception x) when (x is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { return "전체"; }
        }

        static bool RoleShows(string kind) => !Roles.TryGetValue(role, out var r) || r.Kinds.Contains(kind);

        void ApplyRole()
        {
            var (_, judge, request, sign) = Roles.TryGetValue(role, out var r) ? r : (null, true, true, true);
            StepJudge.Visibility = judge ? Visibility.Visible : Visibility.Collapsed;
            StepRequest.Visibility = request ? Visibility.Visible : Visibility.Collapsed;
            StepSign.Visibility = sign ? Visibility.Visible : Visibility.Collapsed;
        }

        void RoleChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RoleBox.SelectedItem is not ComboBoxItem it || (string)it.Content == role) return;
            role = (string)it.Content;
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RoleFile)!);
                System.IO.File.WriteAllText(RoleFile, System.Text.Json.JsonSerializer.Serialize(new { role }));
            }
            catch (Exception x) when (x is System.IO.IOException or UnauthorizedAccessException) { Dashboard.Log("역할 보기를 기억하지 못함: " + x.Message); }
            ApplyRole();
            Dashboard.RequestRefresh();   // 오늘 볼 것 줄을 그 역할로
        }

        void TodayUp2(object sender, MouseButtonEventArgs e)   // '오늘 할 일' 단추 (타임라인 StepUp 과 다름)
        {
            if (!Over(sender, e)) return;
            ReleaseButton(e);
            MarkMouse(sender);
            StepRun(sender, "마우스");
        }

        void TodayClick2(object sender, RoutedEventArgs e) { if (!FromMouse(sender)) StepRun(sender, "키보드·화면 읽기"); }

        void StepRun(object sender, string via)
        {
            if (sender == StepJudge) Act("판정", via, DoJudge);
            else if (sender == StepAlerts)
            {
                if (AlertBox.Visibility != Visibility.Visible) { Dashboard.Log("오늘 볼 것 없음"); return; }
                AlertBox.IsExpanded = true;
                AlertBox.BringIntoView();
            }
            else if (sender == StepRequest) Dashboard.Request("검측요청서", app => ReportCommand.Run(app));
            else if (sender == StepSign) Dashboard.Request("층 표지", app => FloorSignCommand.Run(app));
        }

        void JudgeUp(object sender, MouseButtonEventArgs e)
        {
            if (!Over(sender, e)) { Dashboard.Log("버튼 판정: 버튼 밖에서 놓임 — 무시"); return; }
            ReleaseButton(e);
            MarkMouse(sender);
            Act("판정", "마우스", DoJudge);
        }

        void JudgeClick(object sender, RoutedEventArgs e) { if (!FromMouse(sender)) Act("판정", "키보드·화면 읽기", DoJudge); }   // (N7)

        /// <summary>대시보드 기준일 칸의 날짜 — 리본 [해체 판정] 창의 기본값 (ui_reference_research 7절 #5)</summary>
        public DateTime? BaseDay => BaseDate.SelectedDate?.Date;

        void DoJudge()
        {
            // 날짜 칸에 입력만 하고 바로 [판정]을 누르면 SelectedDate 가 아직 안 바뀌었을 수 있음 → 입력 글자를 먼저 확정 (4차 재점검)
            var d = BaseDate.SelectedDate;
            if (DateTime.TryParse(BaseDate.Text, System.Globalization.CultureInfo.GetCultureInfo("ko-KR"), System.Globalization.DateTimeStyles.None, out var typed)) d = typed;
            Dashboard.Judge((d ?? DateTime.Today).Date);
        }
    }
}

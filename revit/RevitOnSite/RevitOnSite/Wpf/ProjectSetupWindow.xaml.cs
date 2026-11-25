// 프로젝트 설정 창 — ProjectSettings 를 채워 보여 주고, [확인] 때 그대로 되돌려 쓴다. 저장(트랜잭션)은 SettingsCommand
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RevitOnSite.Engine;

namespace RevitOnSite
{
    public partial class ProjectSetupWindow : Window
    {
        readonly ProjectSettings s;
        bool topLoading;   // 데이터 폴더에서 최상층을 읽어 칸에 넣는 동안 (그때는 '확인함'을 켜지 않음)

        public ProjectSetupWindow(ProjectSettings settings)
        {
            InitializeComponent();
            s = settings;
            FirstPour.SelectedDate = s.StartDate;
            Cycle.Text = s.CycleDays.ToString();
            Keep.Text = s.MinLevels.ToString();
            // 배합 = 엔진이 계산할 수 있는 것만 (조강·보통·슬래그 미분말·슬래그·FA 3성분). 모르는 값이면 보통
            foreach (var m in ShoringEngine.Mixes.Keys) Mix.Items.Add(m);
            Mix.SelectedItem = ShoringEngine.Mixes.ContainsKey(s.DefaultMix ?? "") ? s.DefaultMix : "보통";
            Fck.Text = s.DefaultFck.ToString("0.#", CultureInfo.InvariantCulture);
            // 적용 기준 (34차 B): 설계기준강도 칸 값으로 — 칸을 바꾸면 해체 기준(fck) 줄도 따라 바뀜
            StandardNames.Text = "기준: " + string.Join(" · ", Engine.Standards.Names);
            StandardsGrid.ItemsSource = Engine.Standards.Rows(s.DefaultFck);
            Fck.TextChanged += (_, _) => { if (double.TryParse(Fck.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f >= 18 && f <= 60) StandardsGrid.ItemsSource = Engine.Standards.Rows(f); };
            MinTemp.Text = s.CureMin.ToString("0.##", CultureInfo.InvariantCulture);
            AlignTol.Text = s.AlignTolMm?.ToString("0.#", CultureInfo.InvariantCulture) ?? "";
            // 목록 고르기 전용 ComboBox(OnSiteStyles) → 목록에 없는 관측소 번호는 맨 앞에 넣어 그대로 보이게
            var item = Station.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Code(i) == s.StationId);
            if (item == null) Station.Items.Insert(0, item = new ComboBoxItem { Content = s.StationId });
            Station.SelectedItem = item;
            Folder.Text = (s.CsvFolder ?? "").Replace('/', '\\');   // C:/… 도 C:\… 로 보이게 (ux_review L3)
            defaultFolder = s.CsvFolderSet ? null : Folder.Text;   // 87차: 기본값이면 안내 한 줄
            FolderDefaultNote.Visibility = defaultFolder != null ? Visibility.Visible : Visibility.Collapsed;
            // 35차: 창 높이 ≤ 작업 영역 (작업 표시줄 위) — 넘치면 본문만 스크롤, 버튼 줄은 늘 보임. 위쪽이 화면 밖이면 끌어올림
            var wa = SystemParameters.WorkArea;
            MaxHeight = System.Math.Min(wa.Height, 760);   // 11-12 2-5 (Revit #11): 1020 px → 접힘 칸으로 줄이고, 펼치면 본문만 스크롤
            DataBox.IsExpanded = defaultFolder != null;    // 기본 폴더를 넣었으면 안내 줄이 보이게
            // 36차: '적용 기준'을 펼치면 창이 아래로 자라 [확인]이 화면 밖 → 크기가 바뀔 때마다 아래가 작업 영역 안이 되게 위로 올림
            SizeChanged += (_, _) => { if (double.IsNaN(Top)) return; if (Top + ActualHeight > wa.Bottom) Top = wa.Bottom - ActualHeight; if (Top < wa.Top) Top = wa.Top; };
            // 표 위에서 마우스 휠 = 본문 스크롤 (DataGrid 가 휠을 먹어 바깥 스크롤이 안 움직이는 것 방지)
            StandardsGrid.PreviewMouseWheel += (_, e) =>
            {
                e.Handled = true;
                Body.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = StandardsGrid });
            };
        }

        string defaultFolder;   // 87차: 창을 열 때 넣은 기본 폴더 (저장한 폴더가 있으면 null)

        static string Code(ComboBoxItem i) => ((string)i.Content).Split(' ')[0];
        static bool Num(string text, out double v) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        /// <summary>칸이 바뀔 때마다: 잘못된 칸 아래 빨간 한 줄, 하나라도 있으면 [확인] 비활성 (ui_spec §7)</summary>
        void Changed(object sender, RoutedEventArgs e)
        {
            OkButton.IsEnabled = Show(FirstPourError, FirstPour.SelectedDate != null)
                & Show(CycleError, int.TryParse(Cycle.Text, out int c) && c >= 1 && c <= 30)
                & Show(KeepError, int.TryParse(Keep.Text, out int k) && k >= 3 && k <= 10)
                & Show(FckError, Num(Fck.Text, out double f) && f >= 18 && f <= 60)
                & Show(MinTempError, Num(MinTemp.Text, out double t) && t >= -10 && t <= 30)
                & Show(AlignTolError, AlignTol.Text.Trim().Length == 0 || Num(AlignTol.Text, out double a) && a >= 1 && a <= 1000)
                & (LiveLoad == null || Show(SiteRuleError, SiteOk(LiveLoad, Engine.Evaluator.LiveKnM2) && SiteOk(SupportSdl, 0) && SiteOk(SupportLl, 0) && SiteOk(PatrolHours, 0) && SiteOk(ScopeSlab, 0) && SiteOk(SignHours, 0)))
                & (TopFloor == null || Show(TopFloorError, TopFloor.Text.Trim().Length == 0 || int.TryParse(TopFloor.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int tf) && tf >= 1 && tf <= 200));
            if (SiteBox != null && SiteRuleError != null && SiteRuleError.Visibility == Visibility.Visible) SiteBox.IsExpanded = true;   // 접힌 칸의 오류로 [확인]이 꺼진 이유가 안 보이지 않게
        }

        /// <summary>v6.2 현장 기준 칸: 비움 또는 min 이상(0 이면 0 초과) 숫자</summary>
        static bool SiteOk(TextBox b, double min) => b.Text.Trim().Length == 0 || Num(b.Text, out double v) && (min > 0 ? v >= min : v > 0) && v < 100000;
        static double? SiteVal(TextBox b) => b.Text.Trim().Length == 0 ? null : Num(b.Text, out double v) ? v : null;
        static string SiteText(string folder, string key) => Engine.Evaluator.NumOption(folder, key, double.NaN) is var v && !double.IsNaN(v) ? v.ToString("0.###", CultureInfo.InvariantCulture) : "";

        /// <summary>최상층 칸을 사람이 고치면 '실제 최상층으로 확인함'을 켬</summary>
        void TopChanged(object sender, TextChangedEventArgs e)
        {
            if (!topLoading && TopConfirmed != null) TopConfirmed.IsChecked = true;
            Changed(sender, e);
        }

        static bool Show(TextBlock error, bool valid)
        {
            error.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
            return valid;
        }

        void Ok(object sender, RoutedEventArgs e)
        {
            s.StartDate = FirstPour.SelectedDate.Value.Date;
            s.CycleDays = int.Parse(Cycle.Text);
            s.MinLevels = int.Parse(Keep.Text);
            s.DefaultMix = (string)Mix.SelectedItem;
            Num(Fck.Text, out s.DefaultFck);
            Num(MinTemp.Text, out s.CureMin);
            s.AlignTolMm = Num(AlignTol.Text, out double tol) && tol > 0 ? tol : null;
            s.StationId = Code((ComboBoxItem)Station.SelectedItem);
            s.CsvFolder = Folder.Text.Trim();
            if (defaultFolder != null && s.CsvFolder == defaultFolder) s.EnsureFolder();   // 87차: 기본 폴더 그대로면 없을 때 만듦 (options.json 을 쓰기 전에)
            int? top = TopFloor.Text.Trim().Length == 0 ? null : int.Parse(TopFloor.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
            try
            {   // 데이터 폴더 options.json (G2·v6 층 높이·83차 최상층) — 바뀔 때만 씀
                Engine.Evaluator.SetCalibrateDue(s.CsvFolder, CalibrateDue.IsChecked == true);
                Engine.Evaluator.SetHeightCorrect(s.CsvFolder, HeightCorrect.IsChecked == true);
                Engine.Evaluator.SetTopFloor(s.CsvFolder, top, TopConfirmed.IsChecked != true);
                Engine.Evaluator.SetNumOption(s.CsvFolder, "live_load_kn_m2", SiteVal(LiveLoad));   // v6.2 현장 기준 [가정]
                Engine.Evaluator.SetNumOption(s.CsvFolder, "support_sdl_kn_m2", SiteVal(SupportSdl));   // 11-11 G1 구조계산서 값
                Engine.Evaluator.SetNumOption(s.CsvFolder, "support_ll_kn_m2", SiteVal(SupportLl));
                Engine.Evaluator.SetNumOption(s.CsvFolder, "patrol_hours", SiteVal(PatrolHours));
                Engine.Evaluator.SetNumOption(s.CsvFolder, "scope_slab_max_mm", SiteVal(ScopeSlab));
                Engine.Evaluator.SetNumOption(s.CsvFolder, "sign_valid_hours", SiteVal(SignHours));
                Engine.Evaluator.SetTextOption(s.CsvFolder, "contractor", Contractor.Text);   // 11-12 서류 머리 자동 입력
                Engine.Evaluator.SetTextOption(s.CsvFolder, "supervisor_firm", SupFirm.Text);
                Engine.Evaluator.SetRoles(s.CsvFolder, ParseRoles(Roles.Text));
            }
            catch (System.Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException)
            { MessageBox.Show(this, "예측일 현장 보정·층 높이 보정·최상층·현장 기준 설정을 데이터 폴더에 저장하지 못했습니다: " + ex.Message, "프로젝트 설정", MessageBoxButton.OK, MessageBoxImage.Warning); }
            DialogResult = true;
        }

        /// <summary>'이름=역할' 줄들 → 사전 (줄·쉼표로 나눔, '=' 없거나 한쪽이 빈 줄은 버림, 같은 이름은 뒤 줄)</summary>
        static Dictionary<string, string> ParseRoles(string text)
        {
            var d = new Dictionary<string, string>();
            foreach (var part in text.Split(new[] { '\r', '\n', ',' }, System.StringSplitOptions.RemoveEmptyEntries))
                if (part.Split('=', 2) is [var n, var r] && n.Trim().Length > 0 && r.Trim().Length > 0) d[n.Trim()] = r.Trim();
            return d;
        }

        /// <summary>데이터 폴더가 shoring\sample 아래면 경고 한 줄 (막지는 않음) — 현장 입력·판정이 회귀 기준 CSV 를 고치지 않게 (ux_review_7 N1)</summary>
        void FolderChanged(object sender, TextChangedEventArgs e)
        {
            SampleWarn.Visibility = Engine.Evaluator.IsSample(Folder.Text) ? Visibility.Visible : Visibility.Collapsed;
            if (SampleWarn.Visibility == Visibility.Visible && DataBox != null) DataBox.IsExpanded = true;
            var (n, bytes) = Engine.Photos.Usage(Folder.Text.Trim());   // 사진 폴더 용량 — 보기만, 지우지 않음 (근거 자료)
            PhotoUsage.Text = n == 0 ? "사진 폴더: 아직 없음 (현장 입력 사진은 데이터 폴더\\사진 에 쌓임)"
                : $"사진 폴더 용량: {bytes / 1048576.0:0.0} MB · {n}장 (자동 삭제 없음 — 근거 자료)";
            if (CalibrateDue != null) CalibrateDue.IsChecked = Engine.Evaluator.CalibrateDueOn(Folder.Text.Trim());   // 설정은 데이터 폴더에 따라감 (G2)
            if (HeightCorrect != null) HeightCorrect.IsChecked = Engine.Evaluator.HeightOption(Folder.Text.Trim()).On;   // v6 층 높이 보정
            if (LiveLoad != null)   // v6.2 현장 기준 [가정] (데이터 폴더 options.json — 비어 있으면 기본값)
            {
                var fo = Folder.Text.Trim();
                LiveLoad.Text = SiteText(fo, "live_load_kn_m2");
                SupportSdl.Text = SiteText(fo, "support_sdl_kn_m2");
                SupportLl.Text = SiteText(fo, "support_ll_kn_m2");
                PatrolHours.Text = SiteText(fo, "patrol_hours");
                ScopeSlab.Text = SiteText(fo, "scope_slab_max_mm");
                SignHours.Text = SiteText(fo, "sign_valid_hours");
                Contractor.Text = Engine.Evaluator.TextOption(fo, "contractor");
                SupFirm.Text = Engine.Evaluator.TextOption(fo, "supervisor_firm");
                Roles.Text = string.Join("\r\n", Engine.Evaluator.Roles(fo).Select(kv => kv.Key + "=" + kv.Value));
            }
            if (TopFloor != null && TopConfirmed != null)   // 83차 최상층 (데이터 폴더 options.json)
            {
                topLoading = true;
                var tf = Engine.Evaluator.TopOption(Folder.Text.Trim());
                TopFloor.Text = tf?.ToString(CultureInfo.InvariantCulture) ?? "";
                TopConfirmed.IsChecked = tf != null && !Engine.Evaluator.TopAuto(Folder.Text.Trim());
                topLoading = false;
            }
        }

        void Browse(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "데이터 폴더 (타설 기록·센서·공시체·기상 자료)", InitialDirectory = Folder.Text };
            if (dlg.ShowDialog(this) == true) Folder.Text = dlg.FolderName;
        }

        void Help(object sender, RoutedEventArgs e) => App.ShowHelp(typeof(SettingsCommand));
    }
}

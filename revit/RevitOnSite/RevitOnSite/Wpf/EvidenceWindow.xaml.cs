// 근거 원장 추가 창 — 단계·날짜·문서번호·결과·입력자·파일을 고른다. 원장 기록(evidence.csv)은 EvidenceCommand → Engine.Evidence.Add
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace RevitOnSite
{
    public partial class EvidenceWindow : Window
    {
        public int StageNo => Engine.Evidence.Stages[Stage.SelectedIndex].Stage;
        public string DayText => Day.SelectedDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "";
        public string DocNoText => DocNo.Text.Trim();
        public string ResultText => ResultBox.Text.Trim();
        public string ActorName => Actor.Text.Trim();
        public string FilePath => FileBox.Text.Trim().Trim('"');
        /// <summary>11-12 2-4: 함께 넣을 다른 단계 (같은 날짜·입력자·결과, 문서번호·파일 없음)</summary>
        public IReadOnlyList<int> AlsoStages => AlsoPanel.Children.OfType<System.Windows.Controls.CheckBox>().Where(c => c.IsChecked == true && c.Visibility == Visibility.Visible).Select(c => (int)c.Tag).ToList();
        static readonly int[] AlsoCandidates = { 2, 4 };   // 사람이 넣는 서류 단계 (8 결과보고는 해체 결과보고서가 만듦)

        // 자동으로 들어오는 단계는 그 출처를 알려 줌 (여기서 또 넣으면 같은 단계가 두 번 — 첫 날짜만 씀)
        static readonly Dictionary<int, string> Hints = new()
        {
            [1] = "데이터 폴더에 설치검측표(install_inspection.csv)가 있으면 거기서 읽습니다. 없을 때 여기서 넣으세요.",
            [2] = "해체 작업계획서.",
            [3] = "현장양생 공시체 결과(공시체 입력·현장 입력)에서 자동으로 읽습니다.",
            [4] = "해체 전 점검표.",
            [5] = "[승인 기록]의 '요청 제출'에서 자동으로 읽습니다.",
            [6] = "[승인 기록]의 '감리 승인 기록'에서 자동으로 읽습니다.",
            [7] = "[승인 기록]의 '해체 완료'에서 자동으로 읽습니다.",
            [8] = "해체 결과 보고.",
        };

        /// <summary>zones = 구역마다 "9F-A1 — 근거 연속 3/8 · 갖춘 단계 1·2·3·5" 같은 한 줄. stage = 처음 고를 단계 번호, day = 처음 날짜 (대시보드 기준일 = 마지막 판정일)</summary>
        public EvidenceWindow(IEnumerable<string> zones, int stage, string lastActor, DateOnly day)
        {
            InitializeComponent();
            ZonesText.Text = string.Join("\n", zones);
            foreach (var (s, name) in Engine.Evidence.Stages) Stage.Items.Add($"{s} {name}");
            Stage.SelectedIndex = Math.Max(0, Array.FindIndex(Engine.Evidence.Stages, x => x.Stage == stage));
            Day.SelectedDate = day.ToDateTime(TimeOnly.MinValue);
            Actor.Text = lastActor ?? "";
            foreach (var s in AlsoCandidates)
            {
                var cb = new System.Windows.Controls.CheckBox { Content = $"{s} {Engine.Evidence.StageName(s)}도 함께", Tag = s, Margin = new Thickness(0, 0, 12, 0) };
                AlsoPanel.Children.Add(cb);
            }
            Changed(null, null);
        }

        void Changed(object sender, RoutedEventArgs e)
        {
            if (OkButton == null) return;   // InitializeComponent 중
            StageHint.Text = Stage.SelectedIndex >= 0 ? Hints.GetValueOrDefault(StageNo, "") : "";
            foreach (var cb in AlsoPanel.Children.OfType<System.Windows.Controls.CheckBox>())
                cb.Visibility = Stage.SelectedIndex >= 0 && (int)cb.Tag == StageNo ? Visibility.Collapsed : Visibility.Visible;
            bool fileOk = FilePath.Length == 0 || File.Exists(FilePath);
            ActorError.Visibility = ActorName.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
            DayError.Visibility = DayText.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
            FileError.Visibility = fileOk ? Visibility.Collapsed : Visibility.Visible;
            OkButton.IsEnabled = ActorName.Length > 0 && DayText.Length > 0 && fileOk && Stage.SelectedIndex >= 0;
        }

        void Browse(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "근거 서류 파일", Filter = "서류 (*.pdf;*.jpg;*.jpeg;*.png;*.hwp;*.hwpx;*.docx;*.xlsx)|*.pdf;*.jpg;*.jpeg;*.png;*.hwp;*.hwpx;*.docx;*.xlsx|모든 파일 (*.*)|*.*" };
            if (dlg.ShowDialog(this) == true) FileBox.Text = dlg.FileName;
        }

        void Ok(object sender, RoutedEventArgs e)
        {
            if (!OkButton.IsEnabled) return;
            DialogResult = true;
        }

        void Help(object sender, RoutedEventArgs e) => App.ShowHelp(typeof(EvidenceCommand));
    }
}

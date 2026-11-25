// 승인 기록 창 — 행위·입력자·메모를 고른다. 기록(approvals.csv)·상태 기입은 ApproveCommand
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace RevitOnSite
{
    public partial class ApproveWindow : Window
    {
        public string ActionName => (string)Action.SelectedItem;
        public string ActorName => Actor.Text.Trim();
        public string NoteText => Note.Text;
        bool IsSign => Action.SelectedItem as string == "감리 승인 기록";
        /// <summary>감리 승인 기록일 때만 — 다른 행위면 빈 값 (Approval.Record 가 거부하므로)</summary>
        public string ApproverName => IsSign ? Approver.Text.Trim() : "";
        public string SignedAtText => IsSign ? SignedAt.Text.Trim() : "";
        bool SignedOk => SignedAtText.Length == 0 || DateTime.TryParseExact(SignedAtText.Replace(" ", "T"), new[] { "yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm" },
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);

        static readonly Dictionary<string, string> Hints = new()
        {
            ["요청 제출"] = "감리에게 해체 검측요청서(근거 자료)를 냈음을 기록합니다. 판정이 '해체 승인 요청 가능'인 구역만.",
            ["감리 승인 기록"] = "감리가 승인했음을 기록합니다 (상태 6). 판정이 '해체 승인 요청 가능'인 구역만.",
            ["해체 완료"] = "동바리를 실제로 해체했음을 기록합니다 (상태 7).",
            ["재받침"] = "해체 뒤 다시 받쳤음을 기록합니다 (상태 8).",
        };

        readonly string folder;
        readonly List<string> zoneIds;
        readonly DateOnly basis;

        /// <summary>zones = (구역 ID, 판정 결론 — 판정 행이 없으면 빈 값). folder·basis = 근거 원장 (감리 승인 기록의 서명 스캔을 6 감리확인으로)</summary>
        public ApproveWindow(IReadOnlyList<(string Zone, string Decision)> zones, string atText, string lastActor, string folder, DateOnly basis)
        {
            InitializeComponent();
            this.folder = folder;
            this.basis = basis;
            zoneIds = zones.Select(z => z.Zone).ToList();
            ZonesText.Text = string.Join("\n", zones.Select(z => z.Zone + " — " + (z.Decision.Length > 0 ? z.Decision : "판정 없음 (타설 전이거나 판정 전)")));
            AtText.Text = atText;
            foreach (var (a, _) in Engine.Approval.Actions) Action.Items.Add(a);
            // 기본 행위: 모두 요청 가능이면 요청 제출, 아니면 해체 완료 (요청·승인은 존치 구역에서 거부되므로)
            Action.SelectedIndex = zones.All(z => z.Decision == "해체 승인 요청 가능") ? 0 : 2;
            Actor.Text = lastActor ?? "";
            Changed(null, null);
        }

        void Changed(object sender, RoutedEventArgs e)
        {
            if (OkButton == null) return;   // InitializeComponent 중
            ActionHint.Text = Action.SelectedItem is string a && Hints.TryGetValue(a, out var h) ? h : "";
            SignPanel.Visibility = Action.SelectedItem as string == "감리 승인 기록" ? Visibility.Visible : Visibility.Collapsed;
            bool ok = ActorName.Length > 0 && Action.SelectedItem != null && SignedOk;
            ActorError.Visibility = ActorName.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
            SignedError.Visibility = SignedOk ? Visibility.Collapsed : Visibility.Visible;
            OkButton.IsEnabled = ok;
        }

        void Ok(object sender, RoutedEventArgs e)
        {
            if (!OkButton.IsEnabled) return;
            DialogResult = true;
        }

        /// <summary>근거 원장 창 (같은 구역·단계 6 감리확인·판정 기준일) — 이 창 위에 띄우고, 추가한 결과는 이 창 안에 한 줄로 (CSV 만 씀, 모델 안 바꿈)</summary>
        void OpenEvidence(object sender, RoutedEventArgs e)
        {
            try
            {
                var r = EvidenceCommand.Record(folder, zoneIds, basis, 6, ActorName, w => { w.Owner = this; return w.ShowDialog() == true; });
                if (r is not { } res) return;
                SignResult.Text = "✓ " + res.Head;
                SignResult.ToolTip = res.Body;
            }
            catch (Exception ex) { SignResult.Text = "근거 원장에 쓰지 못함: " + ex.Message; }
            SignResult.Visibility = Visibility.Visible;
        }

        void Help(object sender, RoutedEventArgs e) => App.ShowHelp(typeof(ApproveCommand));
    }
}

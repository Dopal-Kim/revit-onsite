// 해체 판정 창 — 기준일·시각을 고른다. 판정(트랜잭션)은 EvaluateCommand
using System;
using System.Globalization;
using System.Windows;

namespace RevitOnSite
{
    public partial class EvaluateWindow : Window
    {
        public DateTime At { get; private set; }
        public bool ApplyColors => Colors.IsChecked == true;

        readonly DateTime? baseDay;

        public EvaluateWindow(ProjectSettings s, DateTime? baseDay = null)
        {
            InitializeComponent();
            this.baseDay = baseDay;
            Day.SelectedDate = baseDay ?? DateTime.Today;
            Time.Text = "09:00";
            Folder.Text = string.IsNullOrEmpty(s.CsvFolder) ? "(지정 안 됨)" : s.CsvFolder;
            Folder.ToolTip = s.CsvFolder;
            Cure.Text = s.CureMin.ToString("0.#", CultureInfo.InvariantCulture) + " °C";
        }

        bool TryAt(out DateTime at)
        {
            at = default;
            if (Day.SelectedDate is not DateTime d) return false;
            if (!TimeSpan.TryParseExact(Time.Text.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out var t) || t >= TimeSpan.FromDays(1)) return false;
            at = d.Date + t;
            return true;
        }

        void Changed(object sender, RoutedEventArgs e)
        {
            if (OkButton == null) return;   // InitializeComponent 중
            bool ok = TryAt(out _);
            DayError.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
            BaseHint.Visibility = baseDay != null && Day.SelectedDate != null ? Visibility.Visible : Visibility.Collapsed;
            if (baseDay is DateTime b && Day.SelectedDate is DateTime d)
                BaseHint.Text = d.Date == b.Date ? "대시보드 기준일과 같음" : $"대시보드 기준일({b:yyyy-MM-dd})과 다름";
            OkButton.IsEnabled = ok;
        }

        void Ok(object sender, RoutedEventArgs e)
        {
            if (!TryAt(out var at)) return;
            At = at;
            DialogResult = true;
        }

        void Help(object sender, RoutedEventArgs e) => App.ShowHelp(typeof(EvaluateCommand));
    }
}

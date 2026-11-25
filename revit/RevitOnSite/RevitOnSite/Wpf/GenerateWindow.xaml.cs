// 구역·동바리 생성 창 — 고른 값을 GenerateOptions 에 직접 쓴다 (창 = m, 옵션 = mm). 생성(트랜잭션)은 GenerateCommand
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace RevitOnSite
{
    public partial class GenerateWindow : Window
    {
        readonly GenerateOptions o;
        readonly bool hasGrid;

        public GenerateWindow(List<string> floorLabels, bool hasGrid, string zoneSource, GenerateOptions options)
        {
            InitializeComponent();
            o = options;
            this.hasGrid = hasGrid;
            FloorSummary.Text = floorLabels.Count + "개 (" + floorLabels[0].Split(' ')[0] + " ~ " + floorLabels[floorLabels.Count - 1].Split(' ')[0] + ")";
            ZoneSource.Text = zoneSource;
            CellX.Text = M(o.CellX); CellY.Text = M(o.CellY);
            CellX.IsEnabled = CellY.IsEnabled = !hasGrid;
            CellHint.Text = hasGrid ? "(그리드가 있어 그리드 칸을 씁니다)" : "(그리드·기둥열이 없을 때만 씁니다)";
            SpacingX.Text = M(o.SpacingX); SpacingY.Text = M(o.SpacingY);
            foreach (var l in floorLabels) Floors.Children.Add(new CheckBox { Content = new TextBlock { Text = l }, Margin = new Thickness(0, 2, 0, 2) });
        }

        static string M(double mm) => (mm / 1000).ToString("0.###", CultureInfo.InvariantCulture);
        static bool Num(TextBox b, out double v) => double.TryParse(b.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        static bool Between(TextBox b, double lo, double hi) => Num(b, out var v) && v >= lo && v <= hi;
        static double Mm(TextBox b) { Num(b, out var v); return v * 1000; }

        /// <summary>잘못된 칸 아래 빨간 한 줄, 하나라도 있으면 [확인] 비활성 (ui_spec §7)</summary>
        void Changed(object sender, RoutedEventArgs e)
        {
            OkButton.IsEnabled = Show(CellError, hasGrid || Between(CellX, 2, 50) && Between(CellY, 2, 50))
                & Show(SpacingError, Between(SpacingX, 0.3, 3) && Between(SpacingY, 0.3, 3));
        }

        static bool Show(TextBlock error, bool valid)
        {
            error.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
            return valid;
        }

        void Ok(object sender, RoutedEventArgs e)
        {
            if (!hasGrid) { o.CellX = Mm(CellX); o.CellY = Mm(CellY); }
            o.SpacingX = Mm(SpacingX); o.SpacingY = Mm(SpacingY);
            o.ShoreFloors = Floors.Children.OfType<CheckBox>().Select((c, i) => c.IsChecked == true ? i + 1 : 0).Where(n => n > 0).ToList();   // 목록 순서 = 층 번호
            DialogResult = true;
        }

        void Help(object sender, RoutedEventArgs e) => App.ShowHelp(typeof(GenerateCommand));
    }
}

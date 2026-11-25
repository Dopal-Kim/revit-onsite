using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace OnSitePreview;

public partial class DashboardPane : UserControl
{
    public DashboardPane() => InitializeComponent();

    // add-in 에서는 UIApplication.ThemeChanged 에서 호출 (UIThemeManager.CurrentTheme == UITheme.Dark)
    public void ApplyTheme(bool dark) =>
        Resources.MergedDictionaries[1] = new ResourceDictionary
        {
            Source = new Uri($"/OnSitePreview;component/wpf/OnSiteTheme.{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative)
        };

    public sealed record Cell(string Text, Brush Bg, Brush Fg);
    public sealed record FloorRow(string Floor, Cell[] Cells);

    // statuses[f][z] = StatusColors 키 이름, dates[f][z] = MM-DD
    public void Load(IList<(string floor, (string status, string date)[] zones)> floors, string[] zones, string[] warnings)
    {
        Matrix.Columns.Clear();
        Matrix.Columns.Add(new DataGridTextColumn { Header = "층", Binding = new Binding("Floor"), Width = 34 });
        var statusCell = (Style)FindResource("OnSite.StatusCell");
        for (int i = 0; i < zones.Length; i++)
        {
            var style = new Style(typeof(DataGridCell), statusCell);
            style.Setters.Add(new Setter(BackgroundProperty, new Binding($"Cells[{i}].Bg")));
            style.Setters.Add(new Setter(ForegroundProperty, new Binding($"Cells[{i}].Fg")));
            Matrix.Columns.Add(new DataGridTextColumn
            {
                Header = zones[i], Binding = new Binding($"Cells[{i}].Text"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star), CellStyle = style
            });
        }
        Matrix.ItemsSource = floors.Select(f => new FloorRow(f.floor, f.zones.Select(z =>
            new Cell(z.date, (Brush)FindResource($"OnSite.Status.{z.status}"), (Brush)FindResource($"OnSite.Status.{z.status}.Fg"))).ToArray())).ToList();

        int Count(string s) => floors.Sum(f => f.zones.Count(z => z.status == s));
        ChipReady.Text = $"해체 승인 요청 가능 {Count("ReadyToRequest")}구역";
        ChipKeep.Text = $"존치 {Count("Extended") + Count("Curing")}구역";
        ChipCyl.Text = $"공시체 시험 필요 {Count("NeedsCylinder")}구역";

        Warnings.ItemsSource = warnings;
        WarnHeader.Text = $"경고 ({warnings.Length})";
    }
}

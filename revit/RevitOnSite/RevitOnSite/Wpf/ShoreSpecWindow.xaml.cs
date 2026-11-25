// [동바리 사양] 창 (v6 2단계) — 값을 모아 submit(= FieldServer.SubmitLocal 종류 shore_spec, 휴대폰과 같은 검사)에 넘기고, 틀리면 그 문구를 창에 보여 준 채 둔다
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;

namespace RevitOnSite
{
    public partial class ShoreSpecWindow : Window
    {
        const string Whole = "층 전체";
        readonly int floorNo;
        readonly Func<List<JsonObject>, (bool Ok, string Error, int Rows)> submit;
        public string ZoneId { get; private set; } = "";
        public string ActorName => Actor.Text.Trim();
        /// <summary>제품 성적서 허용하중 kN/본 (11-11 Revit #10) — 비우면 null (모델 값 그대로·기본 13.3)</summary>
        public double? AllowableKn { get; private set; }

        /// <param name="existing">이 층에 이미 넣은 사양 줄 (조회용 글)</param>
        public ShoreSpecWindow(int floorNo, string label, IReadOnlyList<string> zones, string zoneId, IReadOnlyList<string> existing, DateTime today, string actor,
                               Func<List<JsonObject>, (bool Ok, string Error, int Rows)> submit)
        {
            InitializeComponent();
            this.floorNo = floorNo;
            this.submit = submit;
            Title = "동바리 사양 — " + label;
            FloorText.Text = label;
            Have.Text = existing.Count > 0 ? string.Join("\n", existing) : "아직 없습니다.";
            Zone.Items.Add(Whole);
            foreach (var z in zones) Zone.Items.Add(z);
            Zone.SelectedItem = zoneId.Length > 0 && zones.Contains(zoneId) ? zoneId : Whole;
            foreach (var (name, _) in Engine.ShoreInput.Types) Kind.Items.Add(name);
            Kind.SelectedIndex = 0;
            SpacingX.Text = SpacingY.Text = "1200";
            Splices.Text = "0";
            InstallDate.SelectedDate = today.Date;
            Actor.Text = actor;
        }

        void Ok(object sender, RoutedEventArgs e)
        {
            var zid = (string)Zone.SelectedItem == Whole ? "" : (string)Zone.SelectedItem;
            double? allow = null;
            if (Allowable.Text.Trim().Length > 0)
            {
                if (!double.TryParse(Allowable.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var a) || a < 1 || a > 500)
                {
                    ErrorText.Text = "허용하중은 1–500 kN 사이 숫자로 넣으세요 (제품 성적서 값, 비우면 기본 13.3).";
                    ErrorText.Visibility = Visibility.Visible;
                    return;
                }
                allow = a;
            }
            var body = new JsonObject
            {
                ["kind"] = "shore_spec", ["floor"] = floorNo, ["zone_id"] = zid, ["type"] = (string)Kind.SelectedItem, ["spec"] = Spec.Text,
                ["spacing_x_mm"] = SpacingX.Text, ["spacing_y_mm"] = SpacingY.Text, ["splices"] = Splices.Text,
                ["install_date"] = InstallDate.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                ["entered_by"] = Actor.Text, ["note"] = allow is double av ? $"허용하중 {av.ToString("0.###", CultureInfo.InvariantCulture)} kN (성적서) {Note.Text}".Trim() : Note.Text,
            };
            var (ok, error, _) = submit(new List<JsonObject> { body });
            if (!ok)
            {
                ErrorText.Text = error;
                ErrorText.Visibility = Visibility.Visible;
                return;
            }
            ZoneId = zid;
            AllowableKn = allow;
            DialogResult = true;
        }

        void Help(object sender, RoutedEventArgs e) => App.ShowHelp(typeof(ShoreSpecCommand));
    }
}

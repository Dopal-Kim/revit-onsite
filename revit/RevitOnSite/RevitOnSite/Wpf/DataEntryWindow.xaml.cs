// [타설 등록]·[공시체 결과] 입력 창 — 값을 모아 submit(= FieldServer.SubmitLocal, 휴대폰과 같은 검사) 에 넘기고, 틀리면 창에 그 문구를 보여 준 채 그대로 둔다
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using RevitOnSite.Engine;

namespace RevitOnSite
{
    public partial class DataEntryWindow : Window
    {
        readonly string kind;
        readonly Func<List<JsonObject>, (bool Ok, string Error, int Rows)> submit;
        public List<string> ZoneIds { get; private set; } = new();
        public int SavedRows { get; private set; }
        public string ActorName => Actor.Text.Trim();
        /// <summary>11-12 Revit #9 시험성적서 파일 (공시체, 선택) — 저장 뒤 부르는 쪽이 근거 원장 3 에 경로·SHA-256 으로 남김</summary>
        public string ReportPath => Report.Text.Trim().Trim('"');
        public string TestDay => Day(TestDate);

        /// <param name="kind">pour = 타설 등록 (여러 구역) · cylinder = 공시체 결과 (한 구역)</param>
        /// <param name="zones">고를 수 있는 구역 (층 순) — (ID, 목록 글)</param>
        /// <param name="lots">v6.2 W1 타설 묶음 (pours.csv pour_id) — (묶음, 목록 글, 구역들). 공시체 결과에서 고르면 묶음의 모든 구역 공통 시험 한 줄</param>
        public DataEntryWindow(string kind, IReadOnlyList<(string Id, string Label)> zones, ISet<string> picked, DateTime today, ProjectSettings s, string actor,
                               Func<List<JsonObject>, (bool Ok, string Error, int Rows)> submit, IReadOnlyList<(string Lot, string Label, List<string> Zones)> lots = null)
        {
            InitializeComponent();
            this.kind = kind;
            this.submit = submit;
            bool pour = kind == "pour", many = kind != "cylinder";
            Title = pour ? "타설 등록" : "공시체 결과";
            PourBox.Visibility = pour ? Visibility.Visible : Visibility.Collapsed;
            CylBox.Visibility = kind == "cylinder" ? Visibility.Visible : Visibility.Collapsed;
            Zones.SelectionMode = many ? SelectionMode.Extended : SelectionMode.Single;
            ZoneHint.Text = pour ? "타설 기록이 없는 구역 — 한 번 타설한 구역들을 함께 고릅니다 (Ctrl·Shift)" : "타설 기록이 있는 구역 하나";
            foreach (var (id, label) in zones)
            {
                var it = new ListBoxItem { Content = label, Tag = id };
                Zones.Items.Add(it);
                if (picked.Contains(id) && (many || Zones.SelectedItems.Count == 0)) it.IsSelected = true;
            }
            if (Zones.SelectedItem is ListBoxItem first) Loaded += (_, _) => first.BringIntoView();
            PourDate.SelectedDate = today.Date;
            PourTime.Text = "08:00";
            foreach (var m in ShoringEngine.Mixes.Keys) Mix.Items.Add(m);
            Mix.SelectedItem = ShoringEngine.Mixes.ContainsKey(s.DefaultMix ?? "") ? s.DefaultMix : "보통";
            Fck.Text = s.DefaultFck.ToString("0.#", CultureInfo.InvariantCulture);
            TestDate.SelectedDate = today.Date;
            foreach (var c in ShoringEngine.FieldCurings) Curing.Items.Add(c);
            Curing.SelectedIndex = 0;
            Actor.Text = actor;
            if (kind == "cylinder")
            {
                CylLot.Items.Add(OneZone);
                foreach (var (lot, label, zs) in lots ?? Array.Empty<(string, string, List<string>)>()) CylLot.Items.Add(new ComboBoxItem { Content = label, Tag = (lot, zs) });
                CylLot.SelectedIndex = 0;
                CylLot.IsEnabled = CylLot.Items.Count > 1;   // 묶음이 없으면 구역 하나만
            }
        }

        const string OneZone = "구역 하나 (위 목록에서 고름)";

        /// <summary>W1: 묶음을 고르면 구역 목록을 끔 (묶음의 모든 구역 공통 시험)</summary>
        void LotChanged(object sender, SelectionChangedEventArgs e)
        {
            bool byLot = CylLot.SelectedItem is ComboBoxItem;
            Zones.IsEnabled = !byLot;
            ZoneHint.Text = byLot ? "타설 묶음의 모든 구역에 같은 시험 결과를 기록합니다 — 같은 시험일에 구역 자기 기록이 있으면 그 기록이 우선" : "타설 기록이 있는 구역 하나";
        }

        static string Day(DatePicker d) => d.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

        void Ok(object sender, RoutedEventArgs e)
        {
            var lot = kind == "cylinder" && CylLot.SelectedItem is ComboBoxItem li ? ((string Lot, List<string> Zones))li.Tag : default;   // W1 타설 묶음
            var ids = lot.Lot != null ? new List<string> { "" } : Zones.SelectedItems.Cast<ListBoxItem>().Select(i => (string)i.Tag).ToList();
            if (ids.Count == 0) { Show("구역을 고르세요."); return; }
            if (kind == "cylinder" && ReportPath.Length > 0 && !System.IO.File.Exists(ReportPath)) { Show("시험성적서 파일이 없습니다 — 비우거나 있는 파일을 고르세요."); return; }
            var bodies = ids.Select(z => kind == "pour"
                ? new JsonObject
                {
                    ["kind"] = "pour", ["zone_id"] = z, ["pour_date"] = Day(PourDate), ["pour_time"] = PourTime.Text, ["mix"] = (string)Mix.SelectedItem, ["fck"] = Fck.Text,
                    ["pour_id"] = PourLot.Text, ["supervisor_present"] = (Witness.SelectedItem as ComboBoxItem)?.Content as string ?? "", ["watcher"] = Watcher.Text,
                    ["entered_by"] = Actor.Text, ["note"] = Note.Text,
                }
                : new JsonObject
                {
                    ["kind"] = "cylinder", ["zone_id"] = z, ["test_date"] = Day(TestDate), ["curing"] = (string)Curing.SelectedItem, ["f1"] = F1.Text, ["f2"] = F2.Text, ["f3"] = F3.Text,
                    ["pour_id"] = lot.Lot ?? PourId.Text, ["sampled_at"] = SampledAt.Text, ["supplier"] = Supplier.Text, ["truck_no"] = TruckNo.Text, ["storage_zone"] = Storage.Text,
                    ["entered_by"] = Actor.Text, ["note"] = Note.Text,
                }).ToList();
            var (ok, error, rows) = submit(bodies);
            if (!ok) { Show(error); return; }   // 하나라도 틀리면 아무것도 쓰지 않음 (SubmitLocal) — 고쳐서 다시 [기록]
            ZoneIds = lot.Lot != null ? lot.Zones : ids;
            SavedRows = rows;
            DialogResult = true;
        }

        void BrowseReport(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "공시체 시험성적서", Filter = "성적서 (*.pdf;*.jpg;*.jpeg;*.png)|*.pdf;*.jpg;*.jpeg;*.png|모든 파일 (*.*)|*.*" };
            if (dlg.ShowDialog(this) == true) Report.Text = dlg.FileName;
        }

        void Show(string msg)
        {
            ErrorText.Text = msg;
            ErrorText.Visibility = Visibility.Visible;
        }

        void Help(object sender, RoutedEventArgs e) => App.ShowHelp(kind == "pour" ? typeof(PourCommand) : typeof(ImportCylindersCommand));
    }
}

// v6.1 §3 [구역 정보] 창 (상황별 구역 탭 첫 버튼 · 우클릭) — 고른 구역 하나의 보기 + 입력 (docs/redesign_v6.md v6.1 §3).
//   보기 = Engine.ZoneInfo.Of (= shoring/zone_info.py, Engine.Check 일치) + 모델 동바리 본수·높이 + 마지막 판정 한 줄(SHR_Decision, 판정 근거 창과 같은 제목).
//   입력 = FieldServer.SubmitLocal (휴대폰·리본 창과 같은 검사·같은 문구, 새 검사 규칙 없음): 사양(shore_spec) · 타설(pour) · 공시체(cylinder) · 센서(sensor_temp) · 설치 검측·메모(shore_inspect).
//   [기록]이 되면 판정 기준일이 있을 때 같은 시각으로 판정을 다시 계산(사양은 판정에 안 써서 제외)하고 창을 다시 그림. 결론 문구는 판정 값 그대로 — 지시·승인 문구 없음.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitOnSite.Engine;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace RevitOnSite
{
    [Transaction(TransactionMode.Manual)]
    public class ZoneInfoCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try
            {
                var zone = ZoneFocus.SelectedZones(data.Application.ActiveUIDocument).FirstOrDefault();
                if (zone == null) { App.Message("구역을 하나 고르세요.", "검측 뷰·층 입력 뷰에서 구역 상자를 고른 뒤 [구역 정보]를 누르세요."); return Result.Cancelled; }
                var folder = ShoreInputTools.Folder(doc);
                if (folder == null) return Result.Cancelled;
                var sync = ZoneEvaluation.SyncZonesCsv(doc, folder);   // 새 모델 구역도 구역 표에 (없는 것만)
                if (sync != null) Dashboard.Log("구역 정보: " + sync);
                var zid = ZoneFocus.Zid(zone);
                if (!Evaluator.Read(folder, "zones.csv").Any(r => (r.GetValueOrDefault("zone_id") ?? "").Trim() == zid))
                { App.Message($"구역 표(zones.csv)에 {zid} 가 없습니다.", (sync ?? "") + "\n데이터 폴더: " + folder); return Result.Cancelled; }
                var w = new ZoneInfoWindow(data.Application, zone, folder);
                App.ShowDialog(w, data.Application);
                return w.Saved > 0 ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception ex) { return App.Fail("구역 정보", ex); }
        }
    }

    public class ZoneInfoWindow : Window
    {
        readonly UIApplication app;
        readonly Document doc;
        readonly Element zone;
        readonly string folder, zid;
        readonly int floorNo;
        readonly ProjectSettings st;
        readonly FieldServer srv;
        readonly DateTime today;
        readonly TextBox actor = new() { MinWidth = 140 };
        readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        readonly StackPanel body = new();
        public int Saved { get; private set; }
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public ZoneInfoWindow(UIApplication app, Element zone, string folder)
        {
            this.app = app;
            doc = app.ActiveUIDocument.Document;
            this.zone = zone;
            this.folder = folder;
            zid = ZoneFocus.Zid(zone);
            floorNo = zone.LookupParameter("SHR_Floor") is Parameter p && p.HasValue ? p.AsInteger() : 0;
            st = ProjectSettings.Read(doc);
            today = ShoreInputTools.Today(doc);
            DateTime? demo = today > DateTime.Today ? today.AddHours(9) : null;   // 판정 기준일이 미래(시연 자료)면 그날을 '지금'으로 — DataEntry 와 같은 규칙
            srv = FieldInput.Running && string.Equals(Path.GetFullPath(FieldInput.Server.Folder), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)
                ? FieldInput.Server : new FieldServer(folder) { FixedNow = () => demo, DefaultFck = st.DefaultFck };
            Title = "구역 정보 — " + zid;
            Width = 600;
            MaxHeight = SystemParameters.WorkArea.Height;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Language = System.Windows.Markup.XmlLanguage.GetLanguage("ko-KR");
            actor.Text = App.Actor;
            var close = new Button { Content = "닫기", IsCancel = true, MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            close.Click += (_, _) => { App.Actor = actor.Text.Trim(); Close(); };
            var top = Row("입력자", actor);
            var dock = new DockPanel { Margin = new Thickness(12) };
            var foot = new StackPanel();
            foot.Children.Add(status);
            foot.Children.Add(close);
            DockPanel.SetDock(foot, Dock.Bottom);
            DockPanel.SetDock(top, Dock.Top);
            dock.Children.Add(foot);
            dock.Children.Add(top);
            dock.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 0) });
            Content = dock;
            Fill();
        }

        // ── 화면 조각
        static StackPanel Row(string label, params UIElement[] els)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            sp.Children.Add(new TextBlock { Text = label, Width = 92, VerticalAlignment = VerticalAlignment.Center });
            foreach (var e in els) { if (e is FrameworkElement f) f.Margin = new Thickness(0, 0, 6, 0); sp.Children.Add(e); }
            return sp;
        }

        static TextBlock Unit(string u) => new() { Text = u, VerticalAlignment = VerticalAlignment.Center };   // 단위는 칸 바로 뒤 (XAML 창 OnSite.UnitText 와 같게)
        static TextBox Box(string text, double w = 90) => new() { Text = text, Width = w, VerticalContentAlignment = VerticalAlignment.Center };
        static DatePicker Day(DateTime d) => new() { SelectedDate = d.Date, Width = 130 };
        static string Iso(DatePicker d) => d.SelectedDate?.ToString("yyyy-MM-dd", Inv) ?? "";
        static TextBlock Line(string t, bool hint = false) { var b = new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) }; if (hint) b.Foreground = Brushes.Gray; return b; }

        static ComboBox Combo(IEnumerable<string> items, string sel)
        {
            var c = new ComboBox { MinWidth = 110 };
            foreach (var i in items) c.Items.Add(i);
            c.SelectedItem = items.Contains(sel) ? sel : items.FirstOrDefault();
            return c;
        }

        StackPanel Section(string title)
        {
            var g = new GroupBox { Header = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold }, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(6, 4, 6, 4) };
            var sp = new StackPanel();
            g.Content = sp;
            body.Children.Add(g);
            return sp;
        }

        Button Record(string label, string kind, Func<JsonObject> make)
        {
            var b = new Button { Content = new TextBlock { Text = label + " 기록" }, MinWidth = 70,   // 11-11 Revit #23: 어느 칸을 저장하는지 버튼에
                 Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = label + " — 휴대폰 입력과 같은 검사" };
            b.Click += (_, _) => Submit(label, kind, make());
            return b;
        }

        /// <summary>11-12 2-1 [… 기록 취소]: 사유를 받아 취소 줄을 덧붙임 (원래 줄 그대로 — Evaluator.VoidRow) → 판정 다시 → 창 다시 그림</summary>
        StackPanel VoidRow(string label, string file, Func<Dictionary<string, string>> row, ComboBox pick = null)
        {
            var why = Box("", 180);
            var b = new Button { Content = new TextBlock { Text = label + " 기록 취소" }, MinWidth = 100, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "잘못 넣은 기록을 취소 줄로 지웁니다 (원래 줄은 파일에 남음 — 추가만). 고친 값은 취소 뒤 다시 기록" };
            b.Click += (_, _) =>
            {
                try
                {
                    if (MessageBox.Show(this, $"{zid} {label} 기록을 취소합니다 (사유: {why.Text.Trim()}).\n원래 줄은 파일에 남고 판정에서만 빠집니다.", "기록 취소", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
                    Evaluator.VoidRow(folder, file, row(), why.Text, actor.Text, DateTime.Now);   // 입력 시각 = 실제 시각 (다른 [기록]과 같게 — 91차 #3)
                    App.Actor = actor.Text.Trim();
                    Saved++;
                    string note = ZoneEvaluation.Reapply(app, doc, folder, st.CureMin, "구역 정보 " + label + " 기록 취소", out _) == true ? " — 판정 다시 계산" : "";
                    Timeline.DataChanged();
                    Dashboard.Log($"구역 정보 {zid}: {label} 기록 취소 ({why.Text.Trim()}){note}");
                    Fill();
                    Say($"{label} 기록 취소 줄을 더함{note} — 고친 값은 다시 기록하세요", false);
                }
                catch (Exception ex) { Say($"{label} 기록을 취소하지 못했습니다 — " + ex.Message, true); }
            };
            return pick == null ? Row("잘못 넣었으면", Unit("사유"), why, b) : Row("잘못 넣었으면", pick, Unit("사유"), why, b);
        }

        // ── 보기 + 입력
        void Fill()
        {
            body.Children.Clear();
            var z = Engine.ZoneInfo.Of(folder, zid, DateOnly.FromDateTime(today));
            var dz = Dashboard.ReadZone(doc.GetElement(zone.Id) ?? zone);
            var shores = ShoreInputTools.ShoresIn(doc, floorNo, zid);
            var hs = shores.Select(e => e.LookupParameter("높이") is Parameter h && h.HasValue ? Math.Round(UnitUtils.ConvertFromInternalUnits(h.AsDouble(), UnitTypeId.Millimeters)) : double.NaN)
                .Where(v => !double.IsNaN(v)).Distinct().Order().ToList();

            // 판정 한 줄 (판정 근거 창 제목과 같은 규칙: 사람 기록 6·7·8 이면 그 상태, 아니면 마지막 판정 값)
            var head = Section("검측 결과");
            string recorded = dz.Status is 6 or 7 or 8 ? ZoneEvaluation.Styles.First(x => x.Code == dz.Status).Name + " (기록)" : null;
            var at = ZoneEvaluation.LastEvalAt(doc);
            head.Children.Add(new TextBlock { Text = $"{zid} 검측 결과 — {recorded ?? (dz.Decision.Length > 0 ? dz.Decision : "판정 전")}", FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap });
            head.Children.Add(Line(at is DateTime a ? $"마지막 판정 {a:yyyy-MM-dd HH:mm} 기준" + (dz.DueText.Length > 0 && !dz.Decision.StartsWith("해체 승인 요청 가능") ? $" · 예측 해체 가능일 {dz.DueText} (참고)" : "") : "아직 판정 전 — 타설·공시체를 넣고 [판정]을 누르세요", true));
            var reason = new Button { Content = new TextBlock { Text = "판정 근거" }, MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            reason.Click += (_, _) =>
            {
                try { Dashboard.ReasonNow(app, Dashboard.ReadZone(doc.GetElement(zone.Id) ?? zone)); }
                catch (Exception ex) { Say("판정 근거를 열지 못했습니다 — " + ex.Message, true); }
            };
            head.Children.Add(reason);
            if (Engine.Evaluator.ScopeNote(dz.Reason) is { Length: > 0 } scope)   // v6.2 W11 범위 밖 가능 — 결론 그대로, 구조기술자 검토 안내
                head.Children.Add(new TextBlock { Text = scope, Foreground = Brushes.DarkOrange, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });

            var basic = Section("기본 (읽기)");
            basic.Children.Add(Line($"구역 {z.ZoneId} · {z.Label} (층 번호 {z.Floor}) · 면적 {z.AreaM2:0.0} m² · 슬래브 두께 {(z.SlabMm.Length > 0 ? z.SlabMm + " mm" : "모름")}"));
            int? specN = shores.Count == 0 && z.RemovedCount == 0 && zone.get_BoundingBox(null) is BoundingBoxXYZ zbb   // v6.2 W6 구역 본수 모드 (모델 동바리 0·해체 기록 없음)
                ? ShoreInput.SpecCount(folder, zid, floorNo, (UnitUtils.ConvertFromInternalUnits(zbb.Min.X, UnitTypeId.Meters), UnitUtils.ConvertFromInternalUnits(zbb.Min.Y, UnitTypeId.Meters),
                    UnitUtils.ConvertFromInternalUnits(zbb.Max.X, UnitTypeId.Meters), UnitUtils.ConvertFromInternalUnits(zbb.Max.Y, UnitTypeId.Meters))) : null;
            basic.Children.Add(Line((specN is int specCnt && specCnt > 0 ? $"동바리 {specCnt}본 (사양 계산 — 본 단위 모델 없음, 이음·높이는 현장 확인)" : $"동바리 {shores.Count}본") + (hs.Count == 0 ? "" : " · 높이 " + (hs.Count == 1 ? $"{hs[0]:#,0} mm" : $"{hs[0]:#,0}–{hs[^1]:#,0} mm"))
                + (z.RemovedCount > 0 ? $" · 해체 기록 {z.RemovedCount}본 (마지막 {z.RemovedLast})" : "")));

            // 11-11 G4·Revit #24: 특수 부위 (캔틸레버·차양·지하주차장 — fck 100% 확인 전 해체 금지, 가이드라인 3.2.11) → options.json special_zones
            var spk = Combo(new[] { "없음" }.Concat(Engine.Evaluator.SpecialKinds), Engine.Evaluator.SpecialZones(folder).GetValueOrDefault(zid, "없음"));
            var spBtn = new Button { Content = new TextBlock { Text = "특수 부위 기록" }, MinWidth = 100, VerticalAlignment = VerticalAlignment.Center };
            spBtn.Click += (_, _) =>
            {
                try
                {
                    Engine.Evaluator.SetSpecialZone(folder, zid, (string)spk.SelectedItem == "없음" ? null : (string)spk.SelectedItem);
                    string note = ZoneEvaluation.Reapply(app, doc, folder, st.CureMin, "구역 정보 특수 부위", out _) == true ? " — 판정 다시 계산 (결론 그대로, 사유·요청서에 'fck 100% 확인')" : "";
                    Fill();
                    Say($"특수 부위 = {spk.SelectedItem}{note}", false);
                }
                catch (Exception ex) { Say("특수 부위를 저장하지 못했습니다 — " + ex.Message, true); }
            };
            basic.Children.Add(Row("특수 부위", spk, spBtn));

            // 동바리 사양 (이 구역 줄, 없으면 층 줄) → shore_spec.csv
            var sp = Section("동바리 사양");
            sp.Children.Add(Line(z.Spec is { } s ? $"{(s["source"] == "구역" ? "이 구역" : "층 전체")}: {s["type"]} {s["spec"]} · {s["spacing_x_mm"]}×{s["spacing_y_mm"]} mm · 이음 {s["splices"]} · 설치 {s["install_date"]}" : "사양 기록 없음", z.Spec == null));
            var typ = Combo(Engine.ShoreInput.Types.Select(t => t.Name), z.Spec?["type"] ?? "파이프 서포트");
            var spec = Box(z.Spec?["spec"] ?? "", 120);
            var sx = Box(z.Spec?["spacing_x_mm"] ?? "1200", 60);
            var sy = Box(z.Spec?["spacing_y_mm"] ?? "1200", 60);
            var spl = Box(z.Spec?["splices"] ?? "0", 40);
            var inst = Day(DateTime.TryParseExact(z.Spec?["install_date"], "yyyy-MM-dd", Inv, DateTimeStyles.None, out var sd) ? sd : today);   // 91차 #8: 기록된 설치일 그대로
            sp.Children.Add(Row("종류·규격", typ, spec));
            sp.Children.Add(Row("간격 X·Y", sx, sy, Unit("mm"), Unit("이음"), spl, Unit("개")));
            sp.Children.Add(Row("설치일", inst));
            sp.Children.Add(Record("이 구역 사양", "shore_spec", () => new JsonObject
            {
                ["kind"] = "shore_spec", ["floor"] = floorNo.ToString(Inv), ["zone_id"] = zid, ["type"] = (string)typ.SelectedItem, ["spec"] = spec.Text,
                ["spacing_x_mm"] = sx.Text, ["spacing_y_mm"] = sy.Text, ["splices"] = spl.Text, ["install_date"] = Iso(inst),
            }));

            // 타설 → pours.csv
            var po = Section("타설");
            if (z.Pour is { } pr)
            {
                po.Children.Add(Line($"타설 {pr["pour_date"]} {pr["pour_time"]} · {pr["mix"]} · fck {pr["fck"]} MPa" + (pr["pour_id"].Length > 0 ? $" · 타설 묶음 {pr["pour_id"]}" : "")));
                var raw = Evaluator.Read(folder, "pours.csv").LastOrDefault(r => r.GetValueOrDefault("zone_id") == zid);
                if (raw != null)
                {
                    var vr = VoidRow("타설", "pours.csv", () => raw);
                    if (Engine.Approval.ReadLog(folder).Any(r => r.GetValueOrDefault("zone_id") == zid))   // 91차 #4: 확인 창 전에 막음 (VoidRow 도 거부)
                    {
                        vr.IsEnabled = false;
                        vr.ToolTip = "요청·승인 기록이 있는 구역이라 타설 기록을 취소할 수 없습니다 — 감리와 협의해 정정 문서로";
                        ToolTipService.SetShowOnDisabled(vr, true);
                    }
                    po.Children.Add(vr);
                }
            }
            else
            {
                po.Children.Add(Line("타설 기록 없음", true));
                var pd = Day(today);
                var pt = Box("08:00", 60);
                var mix = Combo(ShoringEngine.Mixes.Keys, st.DefaultMix);
                var fck = Box(st.DefaultFck.ToString("0.#", Inv), 50);
                po.Children.Add(Row("타설일·시각", pd, pt));
                po.Children.Add(Row("배합·fck", mix, fck, Unit("MPa")));
                var plot = Box("", 90);   // v6.2 W1 같이 친 구역 묶음 (선택)
                po.Children.Add(Row("타설 묶음", plot, new TextBlock { Text = "(선택 — 예: P-9F-1, 공시체 한 번으로 묶음 전체)", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center }));
                var wit = Combo(new[] { "", "예", "아니오" }, "");   // 11-12 Revit #7 타설 입회 (선택)
                var watcher = Box("", 90);
                po.Children.Add(Row("입회 감리·감시자", wit, watcher, new TextBlock { Text = "(선택)", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center }));
                po.Children.Add(Record("타설", "pour", () => new JsonObject { ["kind"] = "pour", ["zone_id"] = zid, ["pour_date"] = Iso(pd), ["pour_time"] = pt.Text, ["mix"] = (string)mix.SelectedItem, ["fck"] = fck.Text, ["pour_id"] = plot.Text,
                    ["supervisor_present"] = (string)wit.SelectedItem, ["watcher"] = watcher.Text }));
            }

            // 공시체 (현장양생) → cylinders.csv
            var cy = Section("공시체 (현장양생 = 기준 근거)");
            if (z.Cylinders.Count == 0) cy.Children.Add(Line("공시체 기록 없음", true));
            foreach (var c in z.Cylinders) cy.Children.Add(Line($"{c.TestDate} {c.Curing} {string.Join(" · ", c.F)} MPa → 평균 {(c.Avg is double v ? v.ToString("0.0", Inv) : "-")} ({c.Basis})"
                + (c.Lot.Length > 0 ? $" — 타설 {c.Lot}" : "")));   // v6.2 W1
            var ownCyl = Evaluator.Read(folder, "cylinders.csv").Where(r => r.GetValueOrDefault("zone_id") == zid).ToList();   // 11-12 2-1: 이 구역 자기 줄만 취소 (묶음 공통 줄은 묶음 첫 구역에서도 같은 줄)
            if (ownCyl.Count > 0)
            {
                var pick = Combo(ownCyl.Select(r => $"{r.GetValueOrDefault("test_date")} {r.GetValueOrDefault("curing")} {r.GetValueOrDefault("f1")}·{r.GetValueOrDefault("f2")}·{r.GetValueOrDefault("f3")}"), "");
                pick.SelectedIndex = ownCyl.Count - 1;
                cy.Children.Add(VoidRow("공시체", "cylinders.csv", () => ownCyl[Math.Max(0, pick.SelectedIndex)], pick));
            }
            var td = Day(today);
            var cur = Combo(ShoringEngine.FieldCurings, ShoringEngine.FieldCurings[0]);
            var f1 = Box("", 50); var f2 = Box("", 50); var f3 = Box("", 50);
            cy.Children.Add(Row("시험일·양생", td, cur));
            cy.Children.Add(Row("강도 3개", f1, f2, f3, Unit("MPa")));
            var lotId = z.Pour?["pour_id"] ?? "";
            var byLot = new CheckBox { Content = $"타설 묶음 {lotId} 전체에 (같은 시험 — 묶음의 구역마다 적용)", Visibility = lotId.Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 2) };   // v6.2 W1
            cy.Children.Add(byLot);
            cy.Children.Add(Record("공시체", "cylinder", () => new JsonObject { ["kind"] = "cylinder", ["zone_id"] = byLot.IsChecked == true ? "" : zid, ["pour_id"] = byLot.IsChecked == true ? lotId : "",
                ["test_date"] = Iso(td), ["curing"] = (string)cur.SelectedItem, ["f1"] = f1.Text, ["f2"] = f2.Text, ["f3"] = f3.Text }));

            // 센서 연결 (구역 3 m 안 = 위치 근거) → sensors.csv
            var se = Section($"센서 (구역 {Evaluator.SensorMarginM:0} m 안 = 위치 근거)");
            if (z.Sensors.Count == 0) se.Children.Add(Line("구역 3 m 안 센서 없음 — 판정은 공시체만", true));
            foreach (var sn in z.Sensors) se.Children.Add(Line($"{sn.Kind} {sn.SensorId} ({sn.X}, {sn.Y}) m · {(sn.Inside ? "구역 안" : "구역 3 m 안")} · 기록 {sn.Count}줄 · 마지막 {sn.LastTs.Replace('T', ' ')} {sn.LastValue}{(sn.Kind == "온도" ? " °C" : " MPa")}"));
            var bb = zone.get_BoundingBox(null);
            string M(double ft) => Math.Round(UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Meters), 2).ToString("0.##", Inv);
            var sid = Box("", 70);
            var ts = Box(today.ToString("yyyy-MM-dd", Inv) + " 09:00", 130);
            var temp = Box("", 50);
            var x = Box(bb != null ? M((bb.Min.X + bb.Max.X) / 2) : "", 60);
            var y = Box(bb != null ? M((bb.Min.Y + bb.Max.Y) / 2) : "", 60);
            se.Children.Add(Row("센서 ID·시각", sid, ts));
            se.Children.Add(Row("온도", temp, Unit("°C")));
            se.Children.Add(Row("위치 x·y", x, y, Unit("m"), new TextBlock { Text = "(새 센서만 — 기본 = 구역 가운데)", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center }));
            se.Children.Add(Record("온도 센서", "sensor_temp", () => new JsonObject { ["kind"] = "sensor_temp", ["sensor_id"] = sid.Text, ["floor"] = floorNo.ToString(Inv), ["x"] = x.Text, ["y"] = y.Text, ["timestamp"] = ts.Text, ["temp_c"] = temp.Text }));

            // 설치 검측·메모 → install_inspection.csv (shore_inspect, 단계 '설치 검측')
            var ins = Section("설치 검측·메모");
            ins.Children.Add(Line(z.InstallRecords == 0 ? "점검 기록 없음" : $"기록 {z.InstallRecords}줄 · 마지막 {z.InstallLast}", z.InstallRecords == 0));
            if (z.OpenNg.Length > 0) ins.Children.Add(new TextBlock { Text = "풀리지 않은 NG: " + z.OpenNg, Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap });
            var items = new List<(string Code, ComboBox Res, TextBox Ids)>();
            foreach (var (code, label, crit, _) in ShoreInspect.Stages.First(g => g.Stage == ShoreInspect.Install).Items)
            {
                var res = Combo(new[] { "—", "OK", "NG" }, "—");
                res.MinWidth = 60;
                var ids = Box("", 170);
                ids.ToolTip = "NG 면 문제 동바리 ID (쉼표로, 예: " + zid + "-R01C02) — 동바리가 없는 구역만 비워도 됨 (구역 전체)";
                items.Add((code, res, ids));
                var r = Row(label, res, ids);
                r.ToolTip = crit;
                ins.Children.Add(r);
            }
            var note = Box("", 380);
            ins.Children.Add(Row("메모", note));
            ins.Children.Add(Line("고른 항목만 기록합니다 (— = 이번에 안 봄). NG 는 문제 동바리 ID, 같은 항목을 뒤에 OK 로 기록하면 풀립니다.", true));
            ins.Children.Add(Record("설치 검측", "shore_inspect", () => new JsonObject
            {
                ["kind"] = "shore_inspect", ["zone_id"] = zid, ["stage"] = ShoreInspect.Install, ["note"] = note.Text,
                ["items"] = new JsonArray(items.Where(i => (string)i.Res.SelectedItem != "—").Select(i => (JsonNode)new JsonObject
                {
                    ["item_code"] = i.Code, ["result"] = (string)i.Res.SelectedItem,
                    ["shore_ids"] = new JsonArray(i.Ids.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(v => (JsonNode)v.Trim()).ToArray()),
                }).ToArray()),
            }));
        }

        /// <summary>[기록]: 같은 검사로 저장 → 판정 다시(판정 기준일이 있고 판정에 쓰는 자료일 때) → 창 다시 그림. 틀리면 그 문구를 아래 줄에</summary>
        void Submit(string label, string kind, JsonObject b)
        {
            try { SubmitCore(label, kind, b); }
            catch (Exception ex) { Say("저장·다시 그리기 중 오류 — " + ex.Message, true); Dashboard.Log($"구역 정보 {zid} {label} 오류: {ex}"); }   // 창 클릭 처리에서 예외가 Revit 으로 새지 않게
        }

        void SubmitCore(string label, string kind, JsonObject b)
        {
            b["entered_by"] = actor.Text;
            if (!b.ContainsKey("note")) b["note"] = "";
            if (kind == "shore_inspect") ShoringModel.WriteShoresCsv(doc, folder);   // NG 동바리 ID 검사 = 모델 동바리 위치 (shores.csv)
            var (ok, error, rows) = srv.SubmitLocal(new List<JsonObject> { b });
            if (!ok) { Say(error, true); return; }
            Saved += rows;
            App.Actor = actor.Text.Trim();
            string note = "";
            if (kind != "shore_spec")
                note = ZoneEvaluation.Reapply(app, doc, folder, st.CureMin, "구역 정보 " + label, out var err) switch
                {
                    true => $" — 판정 다시 계산 ({ZoneEvaluation.LastEvalAt(doc):MM-dd HH:mm} 기준)",
                    false => $" — 기록은 저장됐지만 판정을 다시 계산하지 못했습니다 ([판정]을 다시 누르세요: {err})",
                    null => " — 판정 기준일이 아직 없습니다 ([판정]을 누르면 반영)",
                };
            Timeline.DataChanged();
            Dashboard.Log($"구역 정보 {zid}: {label} {rows}줄 기록{note}");
            Fill();
            Say($"{label} {rows}줄 기록 (휴대폰 입력과 같은 검사){note}", false);
        }

        void Say(string msg, bool error)
        {
            status.Text = msg;
            status.Foreground = error ? Brushes.Firebrick : Brushes.DarkGreen;
        }
    }
}

// v6.1 §1 열면 바로 구역 (docs/redesign_v6.md v6.1 — 사용자 11-08 "어느 모델 파일이든 입력과 동시에 구역을 나누고…")
//   모델을 처음 활성화할 때(Idling — 열기·IFC 열기·Revit On-Site 버튼 전 어느 쪽이든 한 번) 구조 슬래브가 있고 구역이 0개면 확인 창 한 번 [총괄 결정: 모델을 바꾸므로 묻는다].
//   [구역 나누기] = 한 트랜잭션(실행 취소 한 번): ① SHR_·SNS_·PRJ_ 매개변수 연결(새 공유 매개변수 없음) ② 프로젝트 설정 기본값(데이터 폴더 = rvt 옆 onsite_data_<이름>, 이미 있으면 그대로)
//   ③ 구역만(동바리 0, ZoneGenerator.Run) ④ 기존 동바리는 지우지 않고 위치로 SHR_ZoneID 를 새 구역에 다시 매김 — 어느 구역에도 안 들어가면 목록.
//   데이터 폴더 CSV 의 옛 구역 ID 가 새 구역에 없으면 경고만(자동 변경 금지). 대상 슬래브 없음 = 창 대신 안내 한 번(모델 진단 글). 저장은 하지 않음.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace RevitOnSite
{
    static class ZoneSplit
    {
        const string What = "구역 나누기";
        static readonly HashSet<string> asked = new(StringComparer.OrdinalIgnoreCase);   // 이번 Revit 실행에서 이미 본 문서 (닫으면 지움 — [나중에] = 닫을 때까지 안 물음)

        internal static void Register(UIControlledApplication app)
        {
            app.Idling += OnIdling;
            app.ControlledApplication.DocumentClosing += (s, e) => asked.Remove(Dashboard.Key(e.Document));
        }

        static void OnIdling(object sender, IdlingEventArgs e)
        {
            if (sender is not UIApplication app || app.ActiveUIDocument?.Document is not Document doc || doc.IsFamilyDocument || doc.IsLinked || doc.IsReadOnly) return;
            if (!asked.Add(Dashboard.Key(doc))) return;
            try { Offer(app, doc); }
            catch (Exception ex) { Dashboard.Log("구역 나누기 확인 건너뜀 (" + doc.Title + "): " + ex.Message); }   // 열기 흐름을 막지 않음
        }

        static GenerateOptions Options() => new() { FamilyPath = Path.Combine(App.Dir, "Families", ZoneGenerator.FamilyName + ".rfa"), KeepShores = true };

        /// <summary>층 n개 · 구역 기준 · 예상 구역 m개 (모델은 안 바꿈)</summary>
        static string Summary(Document doc, List<SlabLevel> floors, out int zones)
        {
            var plan = ZoneGenerator.Plan(doc, floors, Options());
            zones = plan.Sum(p => p.Cells.Count);
            string basis = string.Join(" · ", plan.GroupBy(p => p.How).Select(g => g.Key + (plan.Count > 1 ? " " + g.Count() + "개층" : "")));
            int unsure = floors.Sum(f => f.Unsure.Count);
            return $"층 {floors.Count}개 ({floors[0].Label}–{floors[^1].Label}) · 구역 기준 {basis} · 예상 구역 {zones}개"
                + (unsure > 0 ? $"\n⚠ 재료 불명 {unsure}개 — 콘크리트로 가정 [가정] (회반죽·기본값 등 재료로 판단 못 함). 현장타설이 아니면 나눈 뒤 그 바닥의 '현장타설 여부'(SHR_CastInPlace)를 아니오로 하고 [구역 나누기]를 다시" : "");
        }

        /// <summary>열 때 한 번: 구역이 이미 있으면 아무것도 안 함, 대상 슬래브 없음 = 안내(모델 진단 글), 있으면 확인 창</summary>
        static void Offer(UIApplication app, Document doc)
        {
            if (ShoringModel.Zones(doc).Any()) return;
            var floors = ZoneGenerator.DetectFloors(doc);
            if (floors.Count == 0)
            {
                var (head, body) = DiagnoseCommand.Report(doc);
                Dashboard.Log($"{doc.Title}: 동바리 대상 구조 슬래브 없음 — {head}");
                App.ShowResult(app, "모델 진단", "동바리 대상 구조 슬래브 없음 — [모델 진단]에서 이유 보기", doc.Title + "\n" + head + "\n\n" + body);
                return;
            }
            var sum = Summary(doc, floors, out _);
            var (top, confirmed) = TopDefault(doc, floors);
            var w = new SplitDialog("이 모델에 동바리 구역을 나눌까요?",
                doc.Title + "\n" + sum + "\n\n[구역 나누기] = 검측용 매개변수 연결 + 프로젝트 설정 기본값 + 구역만 만듭니다 (동바리 0 — [층 입력 뷰]에서 놓음). 모델을 바꿉니다 (실행 취소 한 번, 저장은 하지 않음).",
                "데이터 폴더 기본값: " + DefaultFolder(doc) + "\n[나중에] = 이 파일을 닫을 때까지 다시 묻지 않습니다. 언제든 리본 [준비] › [구역 나누기].",
                What, "나중에", floors[^1], top, confirmed);
            if (App.ShowDialog(w, app)) Run(app, doc, floors, w.TopFloor, !w.Confirmed);
            else Dashboard.Log($"{doc.Title}: 구역 나누기 나중에 ({sum})");
        }

        /// <summary>데이터 폴더 = 프로젝트 설정 값, 비었으면 기본 폴더 (87차: ProjectSettings.Read 가 같은 기본값)</summary>
        static string FolderOf(Document doc) => ProjectSettings.Read(doc).CsvFolder;

        /// <summary>확인 창 최상층 칸 처음 값: 사람이 확인한 값(options.json, 모델 맨 위 이상)이 있으면 그것(확인함), 아니면 모델 맨 위 슬래브 층(자동)</summary>
        static (int Top, bool Confirmed) TopDefault(Document doc, List<SlabLevel> floors)
        {
            var folder = FolderOf(doc);
            return Engine.Evaluator.TopOption(folder) is int v && !Engine.Evaluator.TopAuto(folder) && v >= floors[^1].FloorNo ? (v, true) : (floors[^1].FloorNo, false);
        }

        /// <summary>데이터 폴더 기본값 = rvt 옆 onsite_data_&lt;파일 이름&gt; (저장 안 한 모델·클라우드 모델은 문서\Revit On-Site 아래). 회귀 기준 shoring\sample 아래는 피함</summary>
        internal static string DefaultFolder(Document doc)
        {
            var path = doc.PathName ?? "";
            bool local = path.Length > 0 && !path.Contains("://") && Path.IsPathFullyQualified(path);
            var name = Path.GetFileNameWithoutExtension(local ? path : doc.Title);
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            var dir = local ? Path.GetDirectoryName(path) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Revit On-Site");
            if (Engine.Evaluator.IsSample(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Revit On-Site");
            return Path.Combine(dir, "onsite_data_" + name);
        }

        /// <summary>열린 트랜잭션 안: 프로젝트 설정 기본값 — 데이터 폴더가 비었을 때만 기본 폴더, 나머지는 있는 값 그대로(없으면 기본값). 반환 = (폴더, 새로 정했나, 못 쓴 항목)</summary>
        static (string Folder, bool New, List<string> Missing) Defaults(Document doc)
        {
            var s = ProjectSettings.Read(doc);   // 비었으면 CsvFolder = DefaultFolder (87차)
            return (s.CsvFolder, !s.CsvFolderSet, s.Write(doc));
        }

        /// <summary>열린 트랜잭션 안: 모든 동바리의 SHR_ZoneID 를 같은 층(SHR_Floor) 구역 상자 위치로 다시. 반환 = (바뀐 수, 구역 밖 목록)</summary>
        static (int Changed, List<string> Outside) Rezone(Document doc)
        {
            int changed = 0;
            var outside = new List<string>();
            var byFloor = new Dictionary<int, List<(string Id, BoundingBoxXYZ B)>>();
            foreach (var e in ShoringModel.Shores(doc))
            {
                var zp = e.LookupParameter("SHR_ZoneID");
                var pt = (e.Location as LocationPoint)?.Point;
                if (zp == null || zp.IsReadOnly || pt == null || e.LookupParameter("SHR_Floor") is not Parameter fp || !fp.HasValue)
                {
                    outside.Add($"{e.Id} (층·위치 없음)");
                    continue;
                }
                int fl = fp.AsInteger();
                if (!byFloor.TryGetValue(fl, out var zs)) byFloor[fl] = zs = ShoreInputTools.ZonesOf(doc, fl);
                var zid = ShoreInputTools.ZoneAt(zs, pt.X, pt.Y);
                if (zid.Length == 0) outside.Add($"{e.Id} ({fl}층 x {ShoreInputTools.Coord(pt.X)} · y {ShoreInputTools.Coord(pt.Y)} m)");
                if ((zp.AsString() ?? "") != zid) { zp.Set(zid); changed++; }
            }
            return (changed, outside);
        }

        /// <summary>데이터 폴더 CSV(모델에서 쓰는 zones.csv·shores.csv 빼고)의 zone_id 중 새 구역에 없는 것 → "파일: ID …" (고치지 않음)</summary>
        static List<string> StaleIds(string folder, HashSet<string> now)
        {
            var outp = new List<string>();
            if (!Directory.Exists(folder)) return outp;
            foreach (var path in Directory.EnumerateFiles(folder, "*.csv").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(path);
                if (name is "zones.csv" or "shores.csv") continue;
                try
                {
                    var old = Engine.Evaluator.Read(folder, name).Select(r => (r.GetValueOrDefault("zone_id") ?? "").Trim()).Where(z => z.Length > 0 && !now.Contains(z)).Distinct().Order(StringComparer.Ordinal).ToList();
                    if (old.Count > 0) outp.Add($"{name}: {string.Join(", ", old.Take(6))}{(old.Count > 6 ? $" 외 {old.Count - 6}" : "")}");
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException) { outp.Add($"{name}: 읽지 못함 — {ex.Message}"); }
            }
            return outp;
        }

        /// <summary>[구역 나누기] 실행 (API 문맥). floors 를 주면 다시 찾지 않음. top = 최상층 층 번호(없으면 모델 맨 위 슬래브), topAuto = 사람이 확인 안 한 자동값</summary>
        internal static Result Run(UIApplication app, Document doc, List<SlabLevel> floors = null, int? top = null, bool topAuto = true)
        {
            floors ??= ZoneGenerator.DetectFloors(doc);
            if (floors.Count == 0) { App.Message("동바리 대상 구조 슬래브가 없습니다.", "[준비] › [모델 진단]에서 이유를 보세요. 모델은 바꾸지 않았습니다."); return Result.Cancelled; }
            int topNo = top ?? floors[^1].FloorNo;
            List<string> added = null, report = null;
            (string Folder, bool New, List<string> Missing) set = default;
            (int Changed, List<string> Outside) rz = default;
            if (!App.Write(doc, What, () =>
            {
                added = ParameterSetup.Ensure(doc);
                if (added.Count > 0) doc.Regenerate();   // 방금 연결한 매개변수를 같은 트랜잭션에서 읽고 씀
                set = Defaults(doc);
                report = ZoneGenerator.Run(doc, Options());
                doc.Regenerate();                          // 새 구역 상자의 범위·값으로 동바리 구역 번호를 매김
                rz = Rezone(doc);
            }, out var err)) return App.WriteFailed(What, err);

            var lines = new List<string>();
            try { Directory.CreateDirectory(set.Folder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { lines.Add("⚠ 데이터 폴더를 만들지 못했습니다 — [프로젝트 설정]에서 고르세요: " + ex.Message); }
            // 총괄 78차 ④·83차: 최상층 = 확인 창 칸 값 → options.json top_floor·top_floor_auto (판정·대시보드·휴대폰이 같은 값). 자동값 그대로면 판정 근거에 '확인 필요'
            try
            {
                Engine.Evaluator.SetTopFloor(set.Folder, topNo, topAuto);
                string topLabel = floors.FirstOrDefault(f => f.FloorNo == topNo)?.Label ?? topNo + "층";
                lines.Add(topAuto ? $"⚠ 최상층 = {topLabel} (모델 맨 위 슬래브, 자동 [가정] — 확인 필요) — 이 층까지 타설되면 '3개층 존치' 대신 '최상층까지 타설'로 봅니다. 건물 일부만 모델링했다면 [프로젝트 설정] › 공정 › 최상층에서 고치세요"
                    : $"최상층 = {topLabel} (확인함) — 이 층까지 타설되면 '3개층 존치' 대신 '최상층까지 타설'로 봅니다");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { lines.Add("⚠ 최상층 기본값을 쓰지 못했습니다 (options.json): " + ex.Message); }
            var zones = ShoringModel.Zones(doc).ToList();
            var ids = zones.Select(ZoneFocus.Zid).Where(z => z.Length > 0).ToHashSet();
            int nShores = ShoringModel.Shores(doc).Count();
            string head = $"구역 {ids.Count}개 · 층 {floors.Count}개 — 이제 [층 입력 뷰]에서 동바리를 놓으세요";
            lines.Add($"데이터 폴더 {(set.New ? "(새로 정함)" : "(그대로)")}: {set.Folder}");
            lines.Add(added.Count > 0 ? $"검측용 매개변수 {added.Count}개 연결" : "검측용 매개변수는 이미 연결돼 있음");
            if (set.Missing.Count > 0) lines.Add("⚠ 저장하지 못한 설정: " + string.Join(", ", set.Missing));
            lines.AddRange(report);
            if (nShores > 0) lines.Add($"기존 동바리 {nShores}본은 지우지 않음 — 구역 번호를 위치로 다시 적음 {rz.Changed}본");
            if (rz.Outside.Count > 0) lines.Add($"⚠ 어느 구역에도 안 들어간 동바리 {rz.Outside.Count}본 (구역 번호 빈칸 — 3개층 표·휴대폰에 세지 않음): {string.Join(", ", rz.Outside.Take(8))}{(rz.Outside.Count > 8 ? " …" : "")}");
            if (Directory.Exists(set.Folder) && ZoneEvaluation.SyncZonesCsv(doc, set.Folder) is string sync) lines.Add(sync);
            var stale = StaleIds(set.Folder, ids);
            if (stale.Count > 0) lines.Add("⚠ 데이터 폴더 기록의 옛 구역 ID 가 새 구역에 없습니다 (자동으로 바꾸지 않음 — 그 기록은 판정·휴대폰에서 빠짐):\n   " + string.Join("\n   ", stale));
            Dashboard.Log($"{What}: {doc.Title} — {head} · {set.Folder}");
            Dashboard.Refresh(doc);
            ZoneLabels.Update(doc);
            Timeline.DataChanged();
            App.ShowResult(app, What, head, string.Join("\n", lines) + App.WarningNote(),
                ("층 입력 뷰 열기", () => Dashboard.Request("층 입력 뷰", a =>
                {
                    var fl = ZoneGenerator.DetectFloors(a.ActiveUIDocument.Document);
                    if (ShoreInputTools.PickFloor(a, fl, "층 입력 뷰") is SlabLevel s) ShoreInputTools.OpenView(a, s, fl);
                })));
            return Result.Succeeded;
        }

        internal static Result Command(UIApplication app, Document doc)
        {
            var floors = ZoneGenerator.DetectFloors(doc);
            if (floors.Count == 0)
            {
                var (h, body) = DiagnoseCommand.Report(doc);
                App.Message("동바리 대상 구조 슬래브가 없습니다.", h + "\n\n" + body);
                return Result.Cancelled;
            }
            int now = ShoringModel.Zones(doc).Count(), shores = ShoringModel.Shores(doc).Count();
            var (top, confirmed) = TopDefault(doc, floors);
            var w = new SplitDialog(now > 0 ? $"구역 {now}개를 지우고 다시 나눌까요?" : "이 모델에 동바리 구역을 나눌까요?",
                Summary(doc, floors, out _) + (shores > 0 ? $"\n동바리 {shores}본은 지우지 않고 위치로 구역 번호를 다시 적습니다." : "")
                    + "\n데이터 폴더의 기록은 바꾸지 않습니다 — 옛 구역 ID 가 새 구역에 없으면 알려 줍니다.",
                "모델을 바꿉니다 (실행 취소 한 번, 저장은 하지 않음).", What, "취소", floors[^1], top, confirmed);
            return App.ShowDialog(w, app) ? Run(app, doc, floors, w.TopFloor, !w.Confirmed) : Result.Cancelled;
        }
    }

    /// <summary>[구역 나누기] 확인 창 (열 때·리본 같은 창): 요약 + 최상층 칸 — 총괄 83차: 최상층 자동값 [가정]을 그 자리에서 고치거나 확인.
    /// 칸을 고치면 '확인함'이 켜짐. TopFloor = 층 번호(모델 맨 위 이상 200 이하), Confirmed = 사람이 확인한 값</summary>
    sealed class SplitDialog : Window
    {
        public int TopFloor { get; private set; }
        public bool Confirmed { get; private set; }

        public SplitDialog(string ask, string summary, string footer, string okText, string cancelText, SlabLevel auto, int top, bool confirmed)
        {
            Title = App.TabName + " — 구역 나누기";
            Width = 580;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Language = System.Windows.Markup.XmlLanguage.GetLanguage("ko-KR");
            var gray = System.Windows.Media.Brushes.DimGray;
            var box = new System.Windows.Controls.TextBox { Text = top.ToString(CultureInfo.InvariantCulture), Width = 60, VerticalContentAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(box, "최상층 층 번호");
            var check = new System.Windows.Controls.CheckBox { Content = "실제 최상층으로 확인함", IsChecked = confirmed, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            var ok = new System.Windows.Controls.Button { Content = okText, IsDefault = true, MinWidth = 96, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = cancelText, IsCancel = true, MinWidth = 96, Padding = new Thickness(8, 3, 8, 3) };
            bool loading = true;
            void Update()
            {
                bool valid = int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= auto.FloorNo && n <= 200;
                ok.IsEnabled = valid;
                hint.Foreground = valid ? gray : System.Windows.Media.Brushes.Firebrick;
                hint.Text = !valid ? $"최상층은 모델 맨 위 슬래브 층 번호({auto.FloorNo}) 이상 200 이하의 정수로 입력하세요."
                    : n == auto.FloorNo ? $"최상층 = {auto.Label} (모델 맨 위 슬래브) [가정] — 건물 일부만 모델링했다면 실제 최상층으로 고치세요."
                    : $"최상층 = {n}층 (모델 맨 위 {auto.Label} 보다 위 — 그 층까지 타설돼야 '최상층까지 타설'로 봅니다).";
            }
            box.TextChanged += (_, _) => { if (!loading) check.IsChecked = true; Update(); };
            ok.Click += (_, _) => { TopFloor = int.Parse(box.Text.Trim(), CultureInfo.InvariantCulture); Confirmed = check.IsChecked == true; DialogResult = true; };
            var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            row.Children.Add(new System.Windows.Controls.Label { Content = "최상층(_T):", Target = box, Padding = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(box);
            row.Children.Add(new TextBlock { Text = "층", Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(check);
            var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            var body = new StackPanel { Margin = new Thickness(14) };
            body.Children.Add(new TextBlock { Text = ask, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            body.Children.Add(row);
            body.Children.Add(hint);
            body.Children.Add(new TextBlock { Text = footer, TextWrapping = TextWrapping.Wrap, Foreground = gray, FontSize = 11, Margin = new Thickness(0, 10, 0, 0) });
            body.Children.Add(buttons);
            Content = body;
            loading = false;
            Update();
            Loaded += (_, _) => ok.Focus();
        }
    }

    /// <summary>[준비 · 구역 나누기] — 처음 나누기·다시 나누기 (열 때 확인 창과 같은 일)</summary>
    [Transaction(TransactionMode.Manual)]
    public class ZoneSplitCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try { return ZoneSplit.Command(data.Application, doc); }
            catch (Exception ex) { return App.Fail("구역 나누기", ex); }
        }
    }
}

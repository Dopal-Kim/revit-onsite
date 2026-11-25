// v6 2단계 [동바리] 패널 (docs/redesign_v6.md §2, 자료 형식 확정 11-08) — 층마다 평면 입력 뷰에서 동바리를 직접 놓고 고친다.
//   층 입력 뷰 'Revit On-Site 동바리 입력 – nF' = 그 층 슬래브 레벨 평면, 자름 상자 = 그 층 슬래브 + 2 m, 다른 층 동바리·구역은 뷰 필터로 숨김, 바닥은 숨김(동바리가 가려지지 않게).
//   사양 = shore_spec.csv (Engine.FieldServer 종류 shore_spec — 휴대폰과 같은 검사), 놓기 간격·형식은 그 층(·구역) 사양에서. 사양이 없으면 사양 창부터.
//   놓기: 한 본씩(Esc 로 끝) · 줄로(두 점 + X 간격) · 구역 채우기(X·Y 간격, 슬래브 밖·기존 동바리 옆은 건너뜀) · 위층으로 복사.
//   구역 이동(두 점) · 구역 해체(모델에서 지움 + shore_removed.csv 기록) · 3개층 확인 표 (Engine.ShoreInput.Table — shores.csv 를 모델로 다시 쓴 뒤).
//   만든 동바리 SHR_GenBy = "입력" (새 공유 매개변수 없음). 도면(CAD)에서·기존 부재 등록은 ShoreCad.cs (같은 Place).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitOnSite.Engine;

namespace RevitOnSite
{
    static class ShoreInputTools
    {
        public const string ViewPrefix = "Revit On-Site 동바리 입력 – ";
        public const string GenBy = "입력";
        static double Mm(double v) => UnitUtils.ConvertToInternalUnits(v, UnitTypeId.Millimeters);
        static double M(double ft) => Math.Round(UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Meters), 3) + 0.0;

        /// <summary>활성 뷰가 층 입력 뷰면 그 층 (구조 슬래브 층 목록에서), 아니면 null</summary>
        internal static (SlabLevel S, List<SlabLevel> Floors)? FloorOf(Document doc, View v)
        {
            if (v is not ViewPlan || !v.Name.StartsWith(ViewPrefix, StringComparison.Ordinal)) return null;
            var label = v.Name[ViewPrefix.Length..];
            var floors = ZoneGenerator.DetectFloors(doc);
            var s = floors.FirstOrDefault(f => f.Label == label);
            return s == null ? null : (s, floors);
        }

        internal static int Floor(Element e) => e.LookupParameter("SHR_Floor") is Parameter p && p.HasValue ? p.AsInteger() : int.MinValue;

        /// <summary>층 고르기 창 (코드로 만든 작은 목록) → 고른 층 또는 null</summary>
        internal static SlabLevel PickFloor(UIApplication app, List<SlabLevel> floors, string title)
        {
            var list = new System.Windows.Controls.ListBox { Height = 260, Margin = new System.Windows.Thickness(0, 0, 0, 8) };
            foreach (var f in floors.OrderByDescending(f => f.Number)) list.Items.Add(new System.Windows.Controls.ListBoxItem { Content = $"{f.Label}   (레벨 {f.Level.Name})", Tag = f });
            list.SelectedIndex = 0;
            var ok = new System.Windows.Controls.Button { Content = "열기", IsDefault = true, MinWidth = 80, Margin = new System.Windows.Thickness(0, 0, 6, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "취소", IsCancel = true, MinWidth = 80 };
            var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(12), Width = 300 };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "동바리를 넣을 층 (그 층 슬래브를 받치는 동바리)", Margin = new System.Windows.Thickness(0, 0, 0, 6) });
            panel.Children.Add(list); panel.Children.Add(buttons);
            var w = new System.Windows.Window { Title = title, Content = panel, SizeToContent = System.Windows.SizeToContent.WidthAndHeight, ResizeMode = System.Windows.ResizeMode.NoResize,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner, Language = System.Windows.Markup.XmlLanguage.GetLanguage("ko-KR") };
            ok.Click += (_, _) => w.DialogResult = list.SelectedItem != null;
            list.MouseDoubleClick += (_, _) => w.DialogResult = list.SelectedItem != null;
            return App.ShowDialog(w, app) && list.SelectedItem is System.Windows.Controls.ListBoxItem it ? (SlabLevel)it.Tag : null;
        }

        /// <summary>층 입력 뷰를 찾거나 만들고 (App.Write) 연다. 실패하면 null</summary>
        internal static ViewPlan OpenView(UIApplication app, SlabLevel s, List<SlabLevel> floors)
        {
            var doc = app.ActiveUIDocument.Document;
            string name = ViewPrefix + s.Label;
            var v = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(x => !x.IsTemplate && x.Name == name);
            // 66차 (64차 점검): ① 숨은선 표시에서는 구역 상자의 자름면 아래 반이 바로 밑 동바리를 가림 → 와이어프레임 (모든 모서리)
            //                   ② API 로 만든 평면은 작업 기준면이 없을 수 있음 → PickPoint 가 그 층 레벨 위 점을 잡도록 기준면 = 그 층 레벨. 이미 있는 뷰도 한 번 고침
            if ((v == null || v.DisplayStyle != DisplayStyle.Wireframe || v.SketchPlane == null) && !App.Write(doc, "동바리 입력 뷰 " + s.Label, () =>
            {
                if (v != null) { Fix(v); return; }
                var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(t => t.ViewFamily == ViewFamily.FloorPlan)
                    ?? throw new InvalidOperationException("이 모델에 평면도 뷰 유형이 없어 층 입력 뷰를 만들 수 없습니다 — [뷰] › [평면도]로 평면을 하나 만든 뒤 다시 누르세요.");   // v6.1 §2 (IFC 열기 모델 등)
                v = ViewPlan.Create(doc, vft.Id, s.Level.Id);
                v.Name = name;
                v.Scale = 100;
                v.DetailLevel = ViewDetailLevel.Medium;
                var (baseLevel, _, _, _) = ZoneGenerator.ShoreBase(doc, floors, s);
                try   // 자름면 = 슬래브 두께 가운데 (구역 상자가 잘려 보임), 아래 = 동바리가 서는 레벨 — 안 되면 기본 보기 범위
                {
                    var r = v.GetViewRange();
                    r.SetLevelId(PlanViewPlane.TopClipPlane, s.Level.Id); r.SetOffset(PlanViewPlane.TopClipPlane, Mm(1000));
                    r.SetLevelId(PlanViewPlane.CutPlane, s.Level.Id); r.SetOffset(PlanViewPlane.CutPlane, (s.Bottom + s.Top) / 2 - s.Level.ProjectElevation);
                    if (baseLevel != null)
                    {
                        r.SetLevelId(PlanViewPlane.BottomClipPlane, baseLevel.Id); r.SetOffset(PlanViewPlane.BottomClipPlane, 0);
                        r.SetLevelId(PlanViewPlane.ViewDepthPlane, baseLevel.Id); r.SetOffset(PlanViewPlane.ViewDepthPlane, 0);
                    }
                    v.SetViewRange(r);
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentException or Autodesk.Revit.Exceptions.InvalidOperationException) { Dashboard.Log("동바리 입력 뷰 보기 범위: 기본 그대로 — " + ex.Message); }
                var floorsCat = new ElementId(BuiltInCategory.OST_Floors);
                if (v.CanCategoryBeHidden(floorsCat)) v.SetCategoryHidden(floorsCat, true);   // 잘린 바닥 채움이 동바리를 가리지 않게
                // 다른 층 동바리·구역 숨김: 일반 모델 중 SHR_Floor ≠ 이 층 (매개변수가 없는 일반 모델은 그대로 보임)
                var pid = ShoringModel.Zones(doc).Select(z => z.LookupParameter("SHR_Floor")?.Id).FirstOrDefault(id => id != null);
                if (pid != null)
                {
                    string fname = "Revit On-Site 다른 층 숨김 " + s.Label;
                    var f = new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement)).Cast<ParameterFilterElement>().FirstOrDefault(x => x.Name == fname)
                        ?? ParameterFilterElement.Create(doc, fname, new List<ElementId> { new ElementId(BuiltInCategory.OST_GenericModel) },
                            new ElementParameterFilter(ParameterFilterRuleFactory.CreateNotEqualsRule(pid, s.FloorNo)));
                    v.AddFilter(f.Id);
                    v.SetFilterVisibility(f.Id, false);
                }
                var cb = v.CropBox;
                double m = Mm(2000);
                cb.Min = new XYZ(s.MinX - m, s.MinY - m, cb.Min.Z);
                cb.Max = new XYZ(s.MaxX + m, s.MaxY + m, cb.Max.Z);
                v.CropBox = cb;
                v.CropBoxActive = true;
                v.CropBoxVisible = true;
                Fix(v);
            }, out var err)) { App.WriteFailed("동바리 입력 뷰", err); return null; }
            app.ActiveUIDocument.ActiveView = v;
            return v;

            void Fix(ViewPlan pv)   // 열린 트랜잭션 안
            {
                try { pv.DisplayStyle = DisplayStyle.Wireframe; }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentException or Autodesk.Revit.Exceptions.InvalidOperationException) { Dashboard.Log("동바리 입력 뷰 와이어프레임 못 함 (뷰 템플릿?) — " + ex.Message); }
                if (pv.SketchPlane == null) pv.SketchPlane = SketchPlane.Create(doc, s.Level.Id);
            }
        }

        /// <summary>점이 들어가는 그 층 구역 ID (구역 상자 평면 안, 없으면 "")</summary>
        internal static string ZoneAt(List<(string Id, BoundingBoxXYZ B)> zones, double x, double y) =>
            zones.FirstOrDefault(z => x >= z.B.Min.X - 1e-6 && x <= z.B.Max.X + 1e-6 && y >= z.B.Min.Y - 1e-6 && y <= z.B.Max.Y + 1e-6).Id ?? "";

        internal static List<(string Id, BoundingBoxXYZ B)> ZonesOf(Document doc, int floorNo) => ShoringModel.Zones(doc).Where(z => Floor(z) == floorNo)
            .Select(z => (ZoneFocus.Zid(z), z.get_BoundingBox(null))).Where(z => z.Item2 != null).ToList();

        /// <summary>그 층(·구역) 사양 — 없으면 사양 창을 먼저 띄우고 다시 읽음 (취소하면 null)</summary>
        internal static Dictionary<string, string> SpecOrAsk(UIApplication app, SlabLevel s, string zoneId = "")
        {
            var doc = app.ActiveUIDocument.Document;
            var set = ProjectSettings.Read(doc);
            set.EnsureFolder();   // 87차: 기본 폴더가 없으면 만듦 (동바리 사양·입력 기록)
            var folder = set.CsvFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) { App.Message("데이터 폴더가 없습니다.", "[프로젝트 설정]에서 데이터 폴더를 고르세요."); return null; }
            var sp = Engine.ShoreInput.SpecOf(folder, s.FloorNo, zoneId);
            if (sp == null && ShoreSpecCommand.Run(app, s, zoneId) == Result.Succeeded) sp = Engine.ShoreInput.SpecOf(folder, s.FloorNo, zoneId);
            return sp?.ToDictionary(kv => kv.Key, kv => kv.Value ?? "");
        }

        static string ShrType(Dictionary<string, string> sp) => Engine.ShoreInput.Types.FirstOrDefault(t => t.Name == sp.GetValueOrDefault("type")).ShrType ?? "파이프서포트";

        /// <summary>점들에 동바리 (한 트랜잭션, 실행 취소 한 번). 슬래브 밖·같은 층 기존 동바리 tol 안 점은 건너뜀.
        /// 반환 = (놓은 수, 건너뛴 수, 구역 밖 수 — 어느 구역 상자에도 안 들어가 구역 번호가 빈 것, 막지 않고 알림만: 총괄 11-08).
        /// after = 같은 트랜잭션 안에서 놓은 점 번호(pts 순서)로 할 일 (기존 부재 등록의 원래 부재 지우기 — 실행 취소 한 번)</summary>
        internal static (int Made, int Skipped, int Outside) Place(UIApplication app, SlabLevel s, List<SlabLevel> floors, IEnumerable<(double X, double Y)> pts, Dictionary<string, string> sp, string title, double tol,
            Action<Document, List<int>> after = null)
        {
            var doc = app.ActiveUIDocument.Document;
            int made = 0, skipped = 0, outside = 0, offSlab = 0;
            var zones = ZonesOf(doc, s.FloorNo);
            var have = ShoringModel.Shores(doc).Where(e => Floor(e) == s.FloorNo).Select(e => (e.Location as LocationPoint)?.Point).Where(p => p != null).Select(p => (p.X, p.Y)).ToList();
            if (!App.Write(doc, title, () =>
            {
                var (baseLevel, baseZ, h, bErr) = ZoneGenerator.ShoreBase(doc, floors, s);
                if (bErr != null) throw new InvalidOperationException(bErr);
                var symbol = ZoneGenerator.ShoreSymbol(doc, Path.Combine(App.Dir, "Families", ZoneGenerator.FamilyName + ".rfa"));
                var placed = new List<int>();
                int i = -1;
                foreach (var (x, y) in pts)
                {
                    i++;
                    if (!ZoneGenerator.OnSlab(s, x, y)) { skipped++; offSlab++; continue; }
                    if (have.Any(q => Math.Abs(q.X - x) < tol && Math.Abs(q.Y - y) < tol)) { skipped++; continue; }
                    var zid = ZoneAt(zones, x, y);
                    if (zid.Length == 0) outside++;
                    ZoneGenerator.NewShore(doc, symbol, baseLevel, baseZ, h, x, y, zid, s.FloorNo, GenBy, ShrType(sp));
                    have.Add((x, y));
                    placed.Add(i);
                    made++;
                }
                after?.Invoke(doc, placed);
                return made > 0;
            }, out var err)) { App.WriteFailed(title, err); return (0, skipped, 0); }
            if (outside > 0) Dashboard.Log($"{title}: 구역 밖 {outside}본 — 3개층 표·휴대폰에 세지 않음");
            if (skipped > 0) Dashboard.Log($"{title}: 건너뜀 {skipped} (슬래브 밖 {offSlab} · 기존 동바리 옆 {skipped - offSlab})");   // 64차 점검 ②: '0본' 의 까닭이 기록에 남게
            return (made, skipped, outside);
        }

        /// <summary>결과 글 끝: 그 층 동바리 높이가 경고 범위(3.5 m 초과·1.5 m 미만 [가정])면 한 줄</summary>
        internal static string HeightNote(SlabLevel s, List<SlabLevel> floors)
        {
            var (_, _, h, err) = ZoneGenerator.ShoreBase(s.Level.Document, floors, s);
            double mm = UnitUtils.ConvertFromInternalUnits(h, UnitTypeId.Millimeters);
            var flag = err == null ? ZoneGenerator.HeightFlag(mm) : "";
            return flag.Length > 0 ? $"\n{s.Label} 동바리 높이 {Math.Round(mm)} mm{flag}" : "";
        }

        /// <summary>결과 글 끝: 구역 밖이 있으면 알림 한 줄</summary>
        internal static string OutsideNote(int outside) => outside > 0 ? $"\n구역 밖 {outside}본 — 어느 구역 상자에도 안 들어가 3개층 표·휴대폰에 세지 않습니다 (구역 상자 안으로 옮기거나 [예시로 채우기]로 구역을 다시 만드세요)." : "";

        /// <summary>층 입력 뷰가 아니면 안내 (null)</summary>
        internal static (SlabLevel S, List<SlabLevel> Floors)? NeedView(UIApplication app)
        {
            var ui = app.ActiveUIDocument;
            var f = ui?.Document is Document d && !d.IsFamilyDocument ? FloorOf(d, ui.ActiveView) : null;
            if (f == null) App.Message("층 입력 뷰에서 눌러 주세요.", "[동바리] › [층 입력 뷰]로 층을 연 뒤 다시 누르세요.");
            return f;
        }

        internal sealed class ZonePick : ISelectionFilter
        {
            readonly int floorNo;
            public ZonePick(int floorNo) => this.floorNo = floorNo;
            public bool AllowElement(Element e) => ShoringModel.IsZone(e) && Floor(e) == floorNo;
            public bool AllowReference(Reference r, XYZ p) => false;
        }

        /// <summary>구역 하나 고르기 (Esc → null)</summary>
        internal static Element PickZone(UIDocument ui, int floorNo, string prompt)
        {
            try { return ui.Document.GetElement(ui.Selection.PickObject(ObjectType.Element, new ZonePick(floorNo), prompt)); }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return null; }
        }

        internal static XYZ PickPoint(UIDocument ui, string prompt)
        {
            try { return ui.Selection.PickPoint(ObjectSnapTypes.Endpoints | ObjectSnapTypes.Intersections | ObjectSnapTypes.Nearest, prompt); }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return null; }
        }

        internal static List<Element> ShoresIn(Document doc, int floorNo, string zoneId) =>
            ShoringModel.Shores(doc).Where(e => Floor(e) == floorNo && ZoneFocus.Zid(e) == zoneId).ToList();

        /// <summary>구역 동바리 옮긴 뒤 구역 번호를 위치로 다시 (열린 트랜잭션 안)</summary>
        internal static void Rezone(Document doc, int floorNo, IEnumerable<Element> shores)
        {
            var zones = ZonesOf(doc, floorNo);
            foreach (var e in shores)
                if ((e.Location as LocationPoint)?.Point is XYZ p && e.LookupParameter("SHR_ZoneID") is Parameter zp && !zp.IsReadOnly)
                    zp.Set(ZoneAt(zones, p.X, p.Y));
        }

        internal static string Folder(Document doc)
        {
            var set = ProjectSettings.Read(doc);
            set.EnsureFolder();   // 87차: 기본 폴더가 없으면 만듦 (동바리 사양·입력 기록)
            var folder = set.CsvFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) { App.Message("데이터 폴더가 없습니다.", "[프로젝트 설정]에서 데이터 폴더를 고르세요."); return null; }
            if (Engine.Evaluator.IsSample(folder))
            { App.Message("시연 샘플 폴더에는 기록하지 않습니다.", "회귀 시험 기준 자료입니다. [프로젝트 설정]에서 사본 폴더(예: revit\\onsite_data_demo)를 고르세요."); return null; }
            return folder;
        }

        /// <summary>판정 기준일이 미래(시연 자료)면 그날, 아니면 오늘 — DataEntry 와 같은 규칙</summary>
        internal static DateTime Today(Document doc) => ZoneEvaluation.LastEvalAt(doc) is DateTime at && at > DateTime.Now ? at.Date : DateTime.Today;

        internal static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        internal static string Coord(double ft) => Engine.ShoreInput.Repr(M(ft));
    }

    // ── 명령
    [Transaction(TransactionMode.Manual)]
    public class ShoreFloorViewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try
            {
                var floors = ZoneGenerator.DetectFloors(doc);
                // v6.1 §2: 구조 슬래브 층이 하나뿐인 모델(주택 등)도 — 가장 아래층 동바리는 슬래브 밑면 아래 가장 가까운 레벨 위 (ZoneGenerator.ShoreBase, 없으면 놓을 때 이유 문장)
                if (floors.Count == 0) { App.Message("동바리를 넣을 층이 없습니다.", "현장타설 구조 슬래브를 찾지 못했습니다 — [준비] › [모델 진단]에서 이유를 보세요."); return Result.Cancelled; }
                var s = ShoreInputTools.PickFloor(data.Application, floors, "층 입력 뷰");
                if (s == null) return Result.Cancelled;
                if (ShoreInputTools.OpenView(data.Application, s, floors) == null) return Result.Failed;
                var folder = ProjectSettings.Read(doc).CsvFolder;
                if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) && Engine.ShoreInput.SpecOf(folder, s.FloorNo) == null)
                    ShoreSpecCommand.Run(data.Application, s, "");   // 그 층 현장 정보(사양)가 없으면 먼저
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("층 입력 뷰", ex); }
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class ShoreSpecCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try
            {
                var f = ShoreInputTools.FloorOf(doc, doc.ActiveView);
                var floors = f?.Floors ?? ZoneGenerator.DetectFloors(doc);
                var s = f?.S ?? ShoreInputTools.PickFloor(data.Application, floors, "동바리 사양");
                return s == null ? Result.Cancelled : Run(data.Application, s, "");
            }
            catch (Exception ex) { return App.Fail("동바리 사양", ex); }
        }


        /// <summary>사양 창 (그 층 기록을 위에 보여 줌) → FieldServer.SubmitLocal(shore_spec) — 휴대폰과 같은 검사·문구</summary>
        internal static Result Run(UIApplication app, SlabLevel s, string zoneId)
        {
            var doc = app.ActiveUIDocument.Document;
            var folder = ShoreInputTools.Folder(doc);
            if (folder == null) return Result.Cancelled;
            var st = ProjectSettings.Read(doc);
            var sync = ZoneEvaluation.SyncZonesCsv(doc, folder);
            if (sync != null) Dashboard.Log("동바리 사양: " + sync);
            var zones = Evaluator.Read(folder, "zones.csv").Where(r => int.Parse(r["floor"] ?? "", CultureInfo.InvariantCulture) == s.FloorNo).Select(r => r["zone_id"] ?? "").Where(z => z.Length > 0).Distinct().Order(StringComparer.Ordinal).ToList();
            var existing = Evaluator.Read(folder, Engine.ShoreInput.Spec).Where(r => (r.GetValueOrDefault("floor") ?? "").Trim() == s.FloorNo.ToString(CultureInfo.InvariantCulture))
                .Select(r => $"{((r.GetValueOrDefault("zone_id") ?? "").Trim() is { Length: > 0 } z ? z : "층 전체")}: {r.GetValueOrDefault("type")} {r.GetValueOrDefault("spec")} · {r.GetValueOrDefault("spacing_x_mm")}×{r.GetValueOrDefault("spacing_y_mm")} mm · 이음 {r.GetValueOrDefault("splices")} · 설치 {r.GetValueOrDefault("install_date")}").ToList();
            var today = ShoreInputTools.Today(doc);
            DateTime? demo = today > DateTime.Today ? today.AddHours(9) : null;
            var srv = FieldInput.Running && string.Equals(Path.GetFullPath(FieldInput.Server.Folder), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)
                ? FieldInput.Server : new FieldServer(folder) { FixedNow = () => demo, DefaultFck = st.DefaultFck };
            var w = new ShoreSpecWindow(s.FloorNo, s.Label, zones, zoneId, existing, today, App.Actor, b => srv.SubmitLocal(b));
            if (!App.ShowDialog(w, app)) return Result.Cancelled;
            App.Actor = w.ActorName;
            string allowNote = "";
            if (w.AllowableKn is double ak)   // 11-11 Revit #10: 성적서 허용하중 → 그 층(·구역) 동바리 SHR_Allowable_kN — 설치 검측 1본 하중이 이 값을 씀
            {
                var targets = ShoringModel.Shores(doc).Where(e => ShoreInputTools.Floor(e) == s.FloorNo && (w.ZoneId.Length == 0 || ZoneFocus.Zid(e) == w.ZoneId)).ToList();
                if (App.Write(doc, "동바리 허용하중", () => { foreach (var e in targets) e.LookupParameter("SHR_Allowable_kN")?.Set(ak); }, out var werr))
                    allowNote = $"\n허용하중 {ak:0.###} kN → 동바리 {targets.Count}본 (설치 검측을 다시 누르면 반영)";
                else allowNote = "\n허용하중을 모델에 쓰지 못함: " + werr;
            }
            Timeline.DataChanged();
            App.ShowResult(app, "동바리 사양", $"{s.Label} {(w.ZoneId.Length > 0 ? w.ZoneId : "층 전체")} 사양 기록", $"데이터 폴더 shore_spec.csv 에 한 줄 더함 (휴대폰 입력과 같은 검사)\n{Path.Combine(folder, Engine.ShoreInput.Spec)}\n판정에는 쓰지 않습니다 — 놓기 간격·형식과 3개층 확인 표·휴대폰 층 화면에서 씁니다." + allowNote);
            return Result.Succeeded;
        }
    }

    /// <summary>[놓기 ▾ · 한 본씩]: 점을 누를 때마다 한 본 (Esc 로 끝)</summary>
    [Transaction(TransactionMode.Manual)]
    public class ShorePlaceOneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            if (ShoreInputTools.NeedView(data.Application) is not { } f) return Result.Cancelled;
            try
            {
                var sp = ShoreInputTools.SpecOrAsk(data.Application, f.S);
                if (sp == null) return Result.Cancelled;
                int n = 0, outside = 0, offSlab = 0, near = 0;
                string last = "";   // 66차 점검 ⑪: 방금 누른 점을 건너뛰었으면 그 까닭을 다음 안내 줄(상태 줄)에 바로
                while (ShoreInputTools.PickPoint(data.Application.ActiveUIDocument, $"{f.S.Label} 동바리 놓을 점 (Esc = 끝, 지금 {n}본{(outside > 0 ? $" · 구역 밖 {outside}본 — 3개층 표·휴대폰에 세지 않음" : "")}){last}") is XYZ p)
                {
                    var r = ShoreInputTools.Place(data.Application, f.S, f.Floors, new[] { (p.X, p.Y) }, sp, "동바리 한 본", UnitUtils.ConvertToInternalUnits(50, UnitTypeId.Millimeters));
                    n += r.Made;
                    outside += r.Outside;
                    bool off = r.Made == 0 && !ZoneGenerator.OnSlab(f.S, p.X, p.Y);
                    if (r.Made == 0) { if (off) offSlab++; else near++; }
                    last = r.Made > 0 ? "" : off ? $" — 방금 점 건너뜀: {f.S.Label} 슬래브 밖 (슬래브 윤곽 안을 누르세요)" : " — 방금 점 건너뜀: 기존 동바리 50 mm 안";
                }
                Dashboard.Log($"동바리 한 본씩: {f.S.Label} {n}본" + (outside > 0 ? $" (구역 밖 {outside})" : "") + (offSlab + near > 0 ? $" · 건너뜀 {offSlab + near} (슬래브 밖 {offSlab} · 기존 동바리 옆 {near})" : ""));
                if (outside > 0 || offSlab + near > 0)   // 0본으로 끝나도 까닭이 화면에 남게 (66차 점검 ⑪)
                    App.ShowResult(data.Application, "동바리 한 본씩", $"{f.S.Label} {n}본 놓음" + (offSlab + near > 0 ? $" · 건너뜀 {offSlab + near}" : "") + (outside > 0 ? $" · 구역 밖 {outside}본" : ""),
                        (offSlab + near > 0 ? $"건너뛴 점: {f.S.Label} 슬래브 밖 {offSlab} · 기존 동바리 50 mm 안 {near} — 슬래브 밖 점은 그 층 슬래브 윤곽 안을 눌러야 놓입니다." : "")
                        + ShoreInputTools.OutsideNote(outside) + ShoreInputTools.HeightNote(f.S, f.Floors));
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("동바리 한 본씩", ex); }
        }
    }

    /// <summary>[놓기 ▾ · 줄로]: 두 점 사이를 사양 X 간격으로 (두 끝 포함)</summary>
    [Transaction(TransactionMode.Manual)]
    public class ShorePlaceLineCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            if (ShoreInputTools.NeedView(data.Application) is not { } f) return Result.Cancelled;
            try
            {
                var sp = ShoreInputTools.SpecOrAsk(data.Application, f.S);
                if (sp == null) return Result.Cancelled;
                var ui = data.Application.ActiveUIDocument;
                double step = UnitUtils.ConvertToInternalUnits(double.Parse(sp["spacing_x_mm"], CultureInfo.InvariantCulture), UnitTypeId.Millimeters);
                if (ShoreInputTools.PickPoint(ui, "줄 시작점") is not XYZ a || ShoreInputTools.PickPoint(ui, $"줄 끝점 (간격 {sp["spacing_x_mm"]} mm)") is not XYZ b) return Result.Cancelled;
                double len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                var pts = ZoneGenerator.AxisPoints(0, len, step).Select(d => len < 1e-9 ? (a.X, a.Y) : (a.X + (b.X - a.X) * d / len, a.Y + (b.Y - a.Y) * d / len)).ToList();
                var (made, skipped, outside) = ShoreInputTools.Place(data.Application, f.S, f.Floors, pts, sp, "동바리 줄로", step / 4);
                App.ShowResult(data.Application, "동바리 줄로", $"{f.S.Label} {made}본 놓음", $"간격 {sp["spacing_x_mm"]} mm (사양) · 건너뜀 {skipped} (슬래브 밖·기존 동바리 옆)" + ShoreInputTools.OutsideNote(outside) + ShoreInputTools.HeightNote(f.S, f.Floors));
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("동바리 줄로", ex); }
        }
    }

    /// <summary>[놓기 ▾ · 구역 채우기]: 고른 구역 상자 안을 사양 X·Y 간격 격자로 (예시 생성과 같은 점 규칙)</summary>
    [Transaction(TransactionMode.Manual)]
    public class ShoreFillZoneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            if (ShoreInputTools.NeedView(data.Application) is not { } f) return Result.Cancelled;
            try
            {
                var ui = data.Application.ActiveUIDocument;
                var z = ShoreInputTools.PickZone(ui, f.S.FloorNo, $"{f.S.Label} 채울 구역을 고르세요");
                if (z?.get_BoundingBox(null) is not BoundingBoxXYZ bb) return Result.Cancelled;
                var zid = ZoneFocus.Zid(z);
                var sp = ShoreInputTools.SpecOrAsk(data.Application, f.S, zid);
                if (sp == null) return Result.Cancelled;
                double sx = UnitUtils.ConvertToInternalUnits(double.Parse(sp["spacing_x_mm"], CultureInfo.InvariantCulture), UnitTypeId.Millimeters);
                double sy = UnitUtils.ConvertToInternalUnits(double.Parse(sp["spacing_y_mm"], CultureInfo.InvariantCulture), UnitTypeId.Millimeters);
                var pts = ZoneGenerator.AxisPoints(bb.Min.X + sx / 2, bb.Max.X - sx / 2 + 1e-6, sx).SelectMany(x => ZoneGenerator.AxisPoints(bb.Min.Y + sy / 2, bb.Max.Y - sy / 2 + 1e-6, sy).Select(y => (x, y))).ToList();
                var (made, skipped, outside) = ShoreInputTools.Place(data.Application, f.S, f.Floors, pts, sp, "구역 채우기 " + zid, Math.Min(sx, sy) / 4);
                App.ShowResult(data.Application, "구역 채우기", $"{zid} {made}본 놓음", $"간격 {sp["spacing_x_mm"]} × {sp["spacing_y_mm"]} mm ({(sp.GetValueOrDefault("zone_id") is { Length: > 0 } ? "구역" : "층 전체")} 사양) · 건너뜀 {skipped} (슬래브 밖·기존 동바리 옆)" + ShoreInputTools.OutsideNote(outside) + ShoreInputTools.HeightNote(f.S, f.Floors));
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("구역 채우기", ex); }
        }
    }

    /// <summary>[놓기 ▾ · 위층으로 복사]: 이 층 동바리 위치를 바로 위 구조 슬래브 층에 (그 층 높이·구역·사양 형식)</summary>
    [Transaction(TransactionMode.Manual)]
    public class ShoreCopyUpCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            if (ShoreInputTools.NeedView(data.Application) is not { } f) return Result.Cancelled;
            try
            {
                var doc = data.Application.ActiveUIDocument.Document;
                var up = f.Floors.FirstOrDefault(x => x.Number == f.S.Number + 1);
                if (up == null) { App.Message("위층이 없습니다.", f.S.Label + " 이 맨 위 구조 슬래브 층입니다."); return Result.Cancelled; }
                var pts = ShoringModel.Shores(doc).Where(e => e.LookupParameter("SHR_Floor") is Parameter p && p.HasValue && p.AsInteger() == f.S.FloorNo)
                    .Select(e => (e.Location as LocationPoint)?.Point).Where(p => p != null).Select(p => (p.X, p.Y)).ToList();
                if (pts.Count == 0) { App.Message("복사할 동바리가 없습니다.", f.S.Label + " 에 동바리를 먼저 놓으세요."); return Result.Cancelled; }
                var sp = ShoreInputTools.SpecOrAsk(data.Application, up);
                if (sp == null) return Result.Cancelled;
                var (made, skipped, outside) = ShoreInputTools.Place(data.Application, up, f.Floors, pts, sp, $"동바리 위층으로 복사 {f.S.Label}→{up.Label}", UnitUtils.ConvertToInternalUnits(50, UnitTypeId.Millimeters));
                App.ShowResult(data.Application, "위층으로 복사", $"{f.S.Label} → {up.Label} {made}본", $"건너뜀 {skipped} (위층 슬래브 밖·이미 있는 자리). 높이는 {up.Label} 슬래브 밑면까지, 형식은 {up.Label} 사양." + ShoreInputTools.OutsideNote(outside) + ShoreInputTools.HeightNote(up, f.Floors),
                    ("위층 입력 뷰 열기", () => Dashboard.Request("동바리 입력 뷰", a => ShoreInputTools.OpenView(a, up, f.Floors))));
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("위층으로 복사", ex); }
        }
    }

    /// <summary>[구역 이동]: 구역을 고르고 두 점(기준 → 놓을 곳)으로 그 구역 동바리를 함께 옮김, 구역 번호는 새 위치로</summary>
    [Transaction(TransactionMode.Manual)]
    public class ShoreZoneMoveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            if (ShoreInputTools.NeedView(data.Application) is not { } f) return Result.Cancelled;
            try
            {
                var ui = data.Application.ActiveUIDocument;
                var z = ShoreInputTools.PickZone(ui, f.S.FloorNo, "옮길 동바리가 있는 구역을 고르세요");
                if (z == null) return Result.Cancelled;
                var zid = ZoneFocus.Zid(z);
                var shores = ShoreInputTools.ShoresIn(ui.Document, f.S.FloorNo, zid);
                if (shores.Count == 0) { App.Message("그 구역에 동바리가 없습니다.", zid); return Result.Cancelled; }
                if (ShoreInputTools.PickPoint(ui, "기준점") is not XYZ a || ShoreInputTools.PickPoint(ui, "놓을 곳") is not XYZ b) return Result.Cancelled;
                var mv = new XYZ(b.X - a.X, b.Y - a.Y, 0);
                if (!App.Write(ui.Document, "구역 동바리 이동 " + zid, () =>
                {
                    ElementTransformUtils.MoveElements(ui.Document, shores.Select(e => e.Id).ToList(), mv);
                    ShoreInputTools.Rezone(ui.Document, f.S.FloorNo, shores);
                }, out var err)) return App.WriteFailed("구역 이동", err);
                App.ShowResult(data.Application, "구역 이동", $"{zid} 동바리 {shores.Count}본 옮김", $"이동 {UnitUtils.ConvertFromInternalUnits(mv.GetLength(), UnitTypeId.Millimeters):0} mm · 구역 번호는 옮긴 위치의 구역으로 다시 적음");
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("구역 이동", ex); }
        }
    }

    /// <summary>[구역 해체]: 고른 구역 동바리를 모델에서 지우고 위치·날짜를 shore_removed.csv 에 (휴대폰·보고서 이력). 해체 지시·승인이 아니라 모델 정리 기록</summary>
    [Transaction(TransactionMode.Manual)]
    public class ShoreZoneRemoveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            if (ShoreInputTools.NeedView(data.Application) is not { } f) return Result.Cancelled;
            try
            {
                var ui = data.Application.ActiveUIDocument;
                var doc = ui.Document;
                var folder = ShoreInputTools.Folder(doc);
                if (folder == null) return Result.Cancelled;
                var sync = ZoneEvaluation.SyncZonesCsv(doc, folder);   // 해체 기록의 zone_id 로 휴대폰·서버가 층을 찾음 → zones.csv 에 모델 구역이 있어야 함 (vercel 11-08)
                if (sync != null) Dashboard.Log("구역 해체: " + sync);
                var z = ShoreInputTools.PickZone(ui, f.S.FloorNo, "동바리를 해체(모델에서 지움)할 구역을 고르세요");
                if (z == null) return Result.Cancelled;
                var zid = ZoneFocus.Zid(z);
                var shores = ShoreInputTools.ShoresIn(doc, f.S.FloorNo, zid);
                if (shores.Count == 0) { App.Message("그 구역에 동바리가 없습니다.", zid); return Result.Cancelled; }
                var day = ShoreInputTools.Today(doc);
                var td = new TaskDialog("구역 해체") { MainInstruction = $"{zid} 동바리 {shores.Count}본을 모델에서 지우고 해체 기록을 남길까요?",
                    MainContent = $"해체일 {ShoreInputTools.Iso(day)} · 위치는 데이터 폴더 shore_removed.csv 에 남습니다 (휴대폰·보고서 이력). 실행 취소하면 모델은 돌아오지만 기록 줄은 남습니다 — 그때는 파일에서 지우세요.",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No, DefaultButton = TaskDialogResult.No };
                if (td.Show() != TaskDialogResult.Yes) return Result.Cancelled;
                var ids = ShoringModel.ShoreIds(doc);
                var rows = shores.Select(e => ids.TryGetValue(e.Id, out var s) ? s : (Id: e.Id.ToString(), ZoneId: zid, X: double.NaN, Y: double.NaN)).ToList();
                if (!App.Write(doc, "구역 해체 " + zid, () => { doc.Delete(shores.Select(e => e.Id).ToList()); }, out var err)) return App.WriteFailed("구역 해체", err);
                try
                {
                    Engine.ShoreInput.WriteRemoved(folder, rows.Select(r => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
                    {
                        ["id"] = r.Id, ["zone_id"] = r.ZoneId, ["x"] = double.IsNaN(r.X) ? "" : Engine.ShoreInput.Repr(r.X), ["y"] = double.IsNaN(r.Y) ? "" : Engine.ShoreInput.Repr(r.Y),
                        ["removed_date"] = ShoreInputTools.Iso(day), ["by"] = App.Actor.Length > 0 ? App.Actor : Environment.UserName, ["note"] = "구역 해체 (Revit)",
                    }).ToList());
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { App.Message("모델에서는 지웠지만 해체 기록을 쓰지 못했습니다.", "데이터 파일이 Excel 등에서 열려 있거나 쓸 수 없습니다 — 실행 취소(Ctrl+Z)로 되돌린 뒤 파일을 닫고 다시 하세요.\n" + ex.Message, TaskDialogIcon.TaskDialogIconWarning); return Result.Failed; }
                Timeline.DataChanged();
                App.ShowResult(data.Application, "구역 해체", $"{zid} 동바리 {shores.Count}본 지움 · 기록 {rows.Count}줄", $"{Path.Combine(folder, Engine.ShoreInput.Removed)}\n해체일 {ShoreInputTools.Iso(day)} — 승인 기록(해체 완료)은 [승인 기록]에서 따로 남깁니다.");
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("구역 해체", ex); }
        }
    }

    /// <summary>[3개층 확인]: 모델 동바리를 shores.csv 로 다시 쓴 뒤 Engine.ShoreInput.Table (= shore_input.py table)</summary>
    [Transaction(TransactionMode.Manual)]
    public class ShoreTableCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try
            {
                var folder = ShoreInputTools.Folder(doc);
                if (folder == null) return Result.Cancelled;
                var sync = ZoneEvaluation.SyncZonesCsv(doc, folder);
                if (sync != null) Dashboard.Log("3개층 확인: " + sync);
                ShoringModel.WriteShoresCsv(doc, folder);
                var rows = Engine.ShoreInput.Table(folder);
                var lines = Engine.ShoreInput.TableText(rows);
                int red = rows.Count(r => r.Red);
                App.ShowResult(data.Application, "3개층 확인", red > 0 ? $"빨강 {red}개층 — 타설층 포함 {ShoringEngine.MinLevels}개층에 동바리 없음" : $"타설층 포함 {ShoringEngine.MinLevels}개층 동바리 있음",
                    string.Join("\n", lines.Select((l, i) => (rows[i].Red ? "■ " : "   ") + l)) + "\n\n설치 = 지금 + 해체 기록(shore_removed.csv) · 지금 = 모델 동바리. 판정은 바꾸지 않습니다 [확정 3개층 규칙은 판정 게이트가 봄].");
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("3개층 확인", ex); }
        }
    }
}

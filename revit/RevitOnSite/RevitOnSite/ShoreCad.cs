// v6 §2 [놓기 ▾] 맨 뒤 두 가지 (총괄 11-08 "사용자가 시공사 도면으로 입력하는 것을 원했으니 CAD 는 빼지 말고"):
//   ① 도면(CAD)에서: 층 입력 뷰에 링크한 시공사 동바리 배치도(DWG)의 원 중심·블록 중심 → 고른 묶음(종류·레이어·크기, Engine.CadShores)마다 한 본씩.
//   ② 기존 부재 등록: 모델에 이미 그린 파이프·기둥·일반 모델(세운 부재)을 골라 그 평면 가운데에 SHR_PipeSupport 를 놓음 — 원래 부재는 그대로 두거나 지움(같은 실행 취소 한 번).
//   둘 다 높이·구역·형식·건너뛰기는 다른 놓기와 같음 (ShoreInputTools.Place: 그 층 슬래브 밑면까지, 슬래브 밖·기존 동바리 50 mm 안 건너뜀, 구역 밖 알림). 새 공유 매개변수 없음.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using RevitOnSite.Engine;

namespace RevitOnSite
{
    static class ShoreCadTools
    {
        static double ToMm(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);
        internal static double Mm(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);

        /// <summary>CAD 링크(가져오기) 형상의 원·블록 → 표식 (모델 좌표, 내부 단위 피트). 원 = 닫힌 호 또는 같은 중심·반지름 호들이 한 바퀴, 블록 = 중첩 인스턴스의 평면 상자 가운데</summary>
        internal static List<CadMark> Marks(Document doc, ImportInstance imp, View view)
        {
            var outp = new List<CadMark>();
            var ge = imp.get_Geometry(new Options { View = view }) ?? imp.get_Geometry(new Options());
            if (ge == null) return outp;
            string Layer(GeometryObject g) => doc.GetElement(g.GraphicsStyleId) is GraphicsStyle gs ? gs.GraphicsStyleCategory?.Name ?? gs.Name : "";
            var halves = new Dictionary<(long, long, long, string), (XYZ C, double R, double Sweep)>();   // 반원 둘로 들어온 원 (중심·반지름 0.1 mm 반올림, 레이어)
            void Walk(GeometryElement g, Transform t, bool top)
            {
                foreach (var o in g)
                {
                    if (o is GeometryInstance gi)
                    {
                        var tt = t.Multiply(gi.Transform);
                        if (top) { Walk(gi.GetSymbolGeometry(), tt, false); continue; }   // 도면 전체 한 덩어리
                        if (Box(gi.GetSymbolGeometry(), tt) is { } b)
                            outp.Add(new CadMark(CadShores.Block, Layer(gi), ToMm(Math.Max(b.MaxX - b.MinX, b.MaxY - b.MinY)), (b.MinX + b.MaxX) / 2, (b.MinY + b.MaxY) / 2));
                    }
                    else if (o is Arc a)
                    {
                        var c = t.OfPoint(a.Center);
                        double r = a.Radius * t.Scale;
                        if (!a.IsBound) { outp.Add(new CadMark(CadShores.Circle, Layer(a), ToMm(2 * r), c.X, c.Y)); continue; }
                        var key = ((long)Math.Round(ToMm(c.X) * 10), (long)Math.Round(ToMm(c.Y) * 10), (long)Math.Round(ToMm(r) * 10), Layer(a));
                        var sw = a.Length / a.Radius + (halves.TryGetValue(key, out var h) ? h.Sweep : 0);
                        halves[key] = (c, r, sw);
                    }
                }
            }
            Walk(ge, Transform.Identity, true);
            foreach (var ((_, _, _, layer), (c, r, sw)) in halves)
                if (sw >= 2 * Math.PI - 1e-3) outp.Add(new CadMark(CadShores.Circle, layer, ToMm(2 * r), c.X, c.Y));
            return outp;
        }

        /// <summary>블록 형상의 평면 상자 (모델 좌표). 형상이 없으면 null</summary>
        static (double MinX, double MinY, double MaxX, double MaxY)? Box(GeometryElement g, Transform t)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            void Add(XYZ p) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
            void W(GeometryElement ge, Transform tt)
            {
                foreach (var o in ge)
                    if (o is GeometryInstance gi) W(gi.GetSymbolGeometry(), tt.Multiply(gi.Transform));
                    else if (o is Curve c) foreach (var p in c.Tessellate()) Add(tt.OfPoint(p));
                    else if (o is PolyLine pl) foreach (var p in pl.GetCoordinates()) Add(tt.OfPoint(p));
            }
            W(g, t);
            return x0 <= x1 ? (x0, y0, x1, y1) : null;
        }

        /// <summary>묶음 고르기 창: 동바리 같아 보이는 크기(Engine.CadShores.ShoreMinMm–ShoreMaxMm [가정])는 처음부터 체크. 취소 = null</summary>
        internal static List<CadGroup> PickGroups(UIApplication app, List<CadGroup> groups, string cadName)
        {
            var boxes = groups.Select(g => new System.Windows.Controls.CheckBox { Content = new System.Windows.Controls.TextBlock { Text = g.Label }, IsChecked = g.LikelyShore, Tag = g, Margin = new System.Windows.Thickness(0, 2, 0, 2) }).ToList();
            var list = new System.Windows.Controls.StackPanel();
            foreach (var b in boxes) list.Children.Add(b);
            var ok = new System.Windows.Controls.Button { Content = "놓기", IsDefault = true, MinWidth = 80, Margin = new System.Windows.Thickness(0, 0, 6, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "취소", IsCancel = true, MinWidth = 80 };
            var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new System.Windows.Thickness(0, 8, 0, 0) };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(12), Width = 460 };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = $"도면 '{cadName}' 의 원·블록 — 동바리 기호인 묶음을 고르세요. 고른 기호의 중심마다 한 본씩 놓습니다 (슬래브 밖·기존 동바리 50 mm 안은 건너뜀).", TextWrapping = System.Windows.TextWrapping.Wrap, Margin = new System.Windows.Thickness(0, 0, 0, 6) });
            panel.Children.Add(new System.Windows.Controls.ScrollViewer { Content = list, MaxHeight = 320, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto });
            panel.Children.Add(buttons);
            var w = new System.Windows.Window { Title = "도면에서 동바리 놓기", Content = panel, SizeToContent = System.Windows.SizeToContent.WidthAndHeight, ResizeMode = System.Windows.ResizeMode.NoResize,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner, Language = System.Windows.Markup.XmlLanguage.GetLanguage("ko-KR") };
            ok.Click += (_, _) => w.DialogResult = boxes.Any(b => b.IsChecked == true);
            return App.ShowDialog(w, app) ? boxes.Where(b => b.IsChecked == true).Select(b => (CadGroup)b.Tag).ToList() : null;
        }

        internal sealed class ImportPick : ISelectionFilter
        {
            public bool AllowElement(Element e) => e is ImportInstance;
            public bool AllowReference(Reference r, XYZ p) => false;
        }

        static readonly BuiltInCategory[] MemberCats = { BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns, BuiltInCategory.OST_GenericModel };

        /// <summary>등록할 수 있는 부재: 파이프·구조 기둥·기둥·일반 모델 (구역 상자·이미 동바리인 것 빼고)</summary>
        internal static bool Member(Element e) => e?.Category?.BuiltInCategory is BuiltInCategory c && MemberCats.Contains(c) && !ShoringModel.IsZone(e) && !ShoringModel.IsShore(e) && e.get_BoundingBox(null) != null;

        internal sealed class MemberPick : ISelectionFilter
        {
            public bool AllowElement(Element e) => Member(e);
            public bool AllowReference(Reference r, XYZ p) => false;
        }
    }

    /// <summary>[놓기 ▾ · 도면(CAD)에서]: 층 입력 뷰의 CAD 링크에서 원·블록 묶음을 골라 중심마다 한 본</summary>
    [Transaction(TransactionMode.Manual)]
    public class ShorePlaceCadCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            if (ShoreInputTools.NeedView(data.Application) is not { } f) return Result.Cancelled;
            try
            {
                var ui = data.Application.ActiveUIDocument;
                var doc = ui.Document;
                var view = doc.ActiveView;
                var cads = new FilteredElementCollector(doc, view.Id).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToList();
                if (cads.Count == 0)
                {
                    App.Message("이 뷰에 CAD 도면이 없습니다.", "[삽입(Insert)] › [CAD 링크(Link CAD)]로 시공사 동바리 배치도(DWG)를 이 층 입력 뷰에 링크하세요 — '현재 뷰만(Current view only)' 체크, 위치는 원점 대 원점(맞지 않으면 링크 뒤 [이동]으로 그리드에 맞춤). 그다음 다시 누르세요.");
                    return Result.Cancelled;
                }
                ImportInstance cad = cads[0];
                if (cads.Count > 1)
                {
                    try { cad = doc.GetElement(ui.Selection.PickObject(Autodesk.Revit.UI.Selection.ObjectType.Element, new ShoreCadTools.ImportPick(), "동바리 배치도 CAD 를 누르세요")) as ImportInstance; }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
                }
                string name = doc.GetElement(cad.GetTypeId())?.Name ?? cad.Name;
                var groups = CadShores.Groups(ShoreCadTools.Marks(doc, cad, view), ShoreCadTools.Mm(1));
                if (groups.Count == 0)
                {
                    App.Message("도면에 원·블록이 없습니다.", $"'{name}' 은 선만 있는 도면이라 동바리 자리를 읽지 못했습니다. [한 본씩]·[줄로]로 도면을 보며 놓으세요.");
                    return Result.Cancelled;
                }
                var chosen = ShoreCadTools.PickGroups(data.Application, groups, name);
                if (chosen == null || chosen.Count == 0) return Result.Cancelled;
                var sp = ShoreInputTools.SpecOrAsk(data.Application, f.S);
                if (sp == null) return Result.Cancelled;
                var pts = chosen.SelectMany(g => g.Points).ToList();
                var (made, skipped, outside) = ShoreInputTools.Place(data.Application, f.S, f.Floors, pts, sp, "도면에서 동바리 " + f.S.Label, ShoreCadTools.Mm(50));
                App.ShowResult(data.Application, "도면에서 놓기", $"{f.S.Label} {made}본 놓음 (기호 {pts.Count}개)",
                    "고른 묶음: " + string.Join(" · ", chosen.Select(g => g.Label)) + $"\n건너뜀 {skipped} (슬래브 밖·기존 동바리 50 mm 안)" + ShoreInputTools.OutsideNote(outside) + ShoreInputTools.HeightNote(f.S, f.Floors)
                    + "\n도면 위치가 모델과 어긋나면 [실행 취소] 뒤 CAD 링크를 그리드에 맞게 옮기고 다시 누르세요.");
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("도면에서 놓기", ex); }
        }
    }

    /// <summary>[놓기 ▾ · 기존 부재 등록]: 고른 파이프·기둥·일반 모델(세운 부재) 가운데에 동바리 — 원래 부재는 그대로 두거나 지움</summary>
    [Transaction(TransactionMode.Manual)]
    public class ShoreRegisterCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            if (ShoreInputTools.NeedView(data.Application) is not { } f) return Result.Cancelled;
            try
            {
                var ui = data.Application.ActiveUIDocument;
                var doc = ui.Document;
                var picked = ui.Selection.GetElementIds().Select(doc.GetElement).Where(ShoreCadTools.Member).ToList();
                if (picked.Count == 0)
                {
                    try { picked = ui.Selection.PickObjects(Autodesk.Revit.UI.Selection.ObjectType.Element, new ShoreCadTools.MemberPick(), "동바리로 등록할 파이프·기둥을 고르고 [마침(Finish)]").Select(r => doc.GetElement(r)).ToList(); }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
                }
                if (picked.Count == 0) return Result.Cancelled;
                var (_, baseZ, h, bErr) = ZoneGenerator.ShoreBase(doc, f.Floors, f.S);
                if (bErr != null) { App.Message("동바리 높이를 정하지 못했습니다.", bErr); return Result.Cancelled; }
                double tol = ShoreCadTools.Mm(300);   // [가정] 부재 가운데 높이가 이 층 동바리 높이 범위 ± 300 mm 안이어야 이 층 부재로 봄
                var pts = new List<(double X, double Y)>();
                var ids = new List<ElementId>();
                int otherFloor = 0, lying = 0;
                foreach (var e in picked)
                {
                    var bb = e.get_BoundingBox(null);
                    double w = Math.Max(bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y), ht = bb.Max.Z - bb.Min.Z, zc = (bb.Min.Z + bb.Max.Z) / 2;
                    if (ht < 2 * w) { lying++; continue; }                                        // 세운 부재만 (높이 ≥ 평면 폭 × 2 [가정])
                    if (zc < baseZ - tol || zc > baseZ + h + tol) { otherFloor++; continue; }
                    pts.Add(((bb.Min.X + bb.Max.X) / 2, (bb.Min.Y + bb.Max.Y) / 2));
                    ids.Add(e.Id);
                }
                if (pts.Count == 0)
                {
                    App.Message("등록할 부재가 없습니다.", $"고른 {picked.Count}개 중 세운 부재가 아닌 것 {lying} · {f.S.Label} 동바리 높이 밖 {otherFloor}. {f.S.Label} 입력 뷰에서 그 층 슬래브를 받치는 세운 부재를 고르세요.");
                    return Result.Cancelled;
                }
                var td = new TaskDialog(App.TabName)
                {
                    TitleAutoPrefix = false, MainInstruction = $"{pts.Count}개를 {f.S.Label} 동바리로 등록합니다",
                    MainContent = "부재 가운데에 동바리(SHR_PipeSupport)를 놓습니다 — 높이·구역·형식은 다른 놓기와 같습니다. 원래 부재는 어떻게 할까요?"
                        + (lying + otherFloor > 0 ? $"\n(건너뜀: 세운 부재 아님 {lying} · 이 층 높이 밖 {otherFloor})" : ""),
                    CommonButtons = TaskDialogCommonButtons.Cancel,
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "원래 부재 그대로 두기", "같은 자리에 겹쳐 보입니다. 판정·3개층 표·휴대폰은 동바리만 셉니다.");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "원래 부재 지우기", "동바리를 놓은 부재만 지웁니다 (실행 취소 한 번에 둘 다 되돌림).");
                var pick = td.Show();
                if (pick is not (TaskDialogResult.CommandLink1 or TaskDialogResult.CommandLink2)) return Result.Cancelled;
                bool remove = pick == TaskDialogResult.CommandLink2;
                var sp = ShoreInputTools.SpecOrAsk(data.Application, f.S);
                if (sp == null) return Result.Cancelled;
                int removed = 0;
                var (made, skipped, outside) = ShoreInputTools.Place(data.Application, f.S, f.Floors, pts, sp, "기존 부재 등록 " + f.S.Label, ShoreCadTools.Mm(50),
                    remove ? (d, placed) => { var del = placed.Select(i => ids[i]).ToList(); if (del.Count > 0) d.Delete(del); removed = del.Count; } : null);
                App.ShowResult(data.Application, "기존 부재 등록", $"{f.S.Label} {made}본 등록" + (remove ? $" · 원래 부재 {removed}개 지움" : ""),
                    $"건너뜀 {skipped} (슬래브 밖·기존 동바리 50 mm 안 — 그 부재는 지우지 않음)" + (lying + otherFloor > 0 ? $" · 세운 부재 아님 {lying} · 이 층 높이 밖 {otherFloor}" : "") + ShoreInputTools.OutsideNote(outside) + ShoreInputTools.HeightNote(f.S, f.Floors));
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("기존 부재 등록", ex); }
        }
    }
}

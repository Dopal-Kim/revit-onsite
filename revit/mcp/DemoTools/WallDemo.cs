// P4: 데모 모델을 벽식 공동주택으로 보강 — shoring/export_4d.py story_layout() 과 같은 배치를 실제 Revit 벽·창·계단으로 만든다.
// 평면 30 × 18 m, 층고 3 m, 슬래브 210, 벽 200 [가정], 3세대/층, 계단실 = CORE. 재실행 안전: 주석이 "P4 벽식 보강" 인 요소를 지우고 다시 만든다.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;

namespace DemoTools
{
    public static class WallDemo
    {
        const string Tag = "P4 벽식 보강";   // 각 요소의 주석(ALL_MODEL_INSTANCE_COMMENTS)
        static double Mm(double v) => UnitUtils.ConvertToInternalUnits(v, UnitTypeId.Millimeters);

        // export_4d.py 와 같은 수 (m)
        const double W = 30.0, D = 18.0, WT = 0.2, H = 3.0, TS = 0.21;
        static readonly (double x0, double x1, double sill)[] South =
            new[] { 0.0, 10.0, 20.0 }.SelectMany(o => new[] { (o + 1.2, o + 5.2, 0.0), (o + 6.2, o + 8.6, 0.9) }).ToArray();
        static readonly (double x0, double x1, double sill)[] North =
            { (1.5, 3.0, 0.9), (6.0, 8.0, 0.9), (11.5, 13.0, 0.9), (17.2, 18.8, 0.9), (21.5, 23.0, 0.9), (26.0, 28.0, 0.9) };
        static readonly (double x0, double y0, double x1, double y1) Core = (13.6, 12.1, 16.4, 17.8);

        /// <summary>층 안 벽 중심선 (m): 외벽 남·북은 통으로(창을 끼움), 나머지는 story_layout 의 rect 를 중심선으로</summary>
        static List<(double ax, double ay, double bx, double by, string what)> WallLines()
        {
            var r = new List<(double, double, double, double, string)>
            {
                (0, WT / 2, W, WT / 2, "외벽 남"), (0, D - WT / 2, W, D - WT / 2, "외벽 북"),
                (WT / 2, WT, WT / 2, 9, "측벽"), (WT / 2, 9, WT / 2, D - WT, "측벽"), (W - WT / 2, WT, W - WT / 2, 9, "측벽"), (W - WT / 2, 9, W - WT / 2, D - WT, "측벽"),
                (13.5, 11.9, 13.5, D - WT, "계단실 벽"), (16.5, 11.9, 16.5, D - WT, "계단실 벽"), (13.6, 12.0, 14.2, 12.0, "계단실 벽"), (15.8, 12.0, 16.4, 12.0, "계단실 벽"),
            };
            foreach (var x in new[] { 10.0, 20.0 }) { r.Add((x, WT, x, 9, "세대 경계벽")); r.Add((x, 9, x, D - WT, "세대 경계벽")); }
            foreach (var o in new[] { 0.0, 10.0, 20.0 }) r.Add((o + 5, WT, o + 5, 6.0, "칸막이벽"));
            return r;
        }

        class Quiet : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
            {
                foreach (var f in a.GetFailureMessages()) if (f.GetSeverity() == FailureSeverity.Warning) a.DeleteWarning(f);
                return FailureProcessingResult.Continue;
            }
        }

        static void Note(Element e) => e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(Tag);

        /// <summary>doc = 데모 사본(트랜잭션 없이 호출). windowRfa = 라이브러리 "고정됨.rfa". 반환 = 결과 문장</summary>
        public static List<string> Build(Document doc, string windowRfa)
        {
            var log = new List<string>();
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.ProjectElevation).ToList();
            Level At(double m) => levels.FirstOrDefault(l => Math.Abs(l.ProjectElevation - Mm(m * 1000)) < Mm(1));
            var stories = Enumerable.Range(1, 15).Where(k => At((k - 1) * H) != null && At(k * H) != null).ToList();

            using (var t = new Transaction(doc, "P4 벽·창·샤프트"))
            {
                var fho = t.GetFailureHandlingOptions();
                t.SetFailureHandlingOptions(fho.SetFailuresPreprocessor(new Quiet()));   // 경고 창(창–벽 결합 등) 없이
                t.Start();
                var old = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                    .Where(e => e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() == Tag).Select(e => e.Id).ToList();
                if (old.Count > 0) doc.Delete(old);
                log.Add("이전 P4 요소 삭제 " + old.Count + "개");

                // 벽 유형 RC 벽 200 (단일 구조층, 슬래브와 같은 재료)
                var wts = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().ToList();
                var wt = wts.FirstOrDefault(w => w.Name == "RC 벽 200");
                if (wt == null)
                {
                    var src = wts.First(w => w.Kind == WallKind.Basic && w.GetCompoundStructure() != null);
                    wt = (WallType)src.Duplicate("RC 벽 200");
                    var slabType = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().FirstOrDefault(f => f.Name == "RC 슬래브 210");
                    var mat = slabType?.GetCompoundStructure()?.GetMaterialId(0) ?? ElementId.InvalidElementId;
                    wt.SetCompoundStructure(CompoundStructure.CreateSingleLayerCompoundStructure(MaterialFunctionAssignment.Structure, Mm(200), mat));
                }

                // 창 유형: 고정됨.rfa 를 크기별로 복제
                var winFam = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().FirstOrDefault(f => f.FamilyCategory?.Id.Value == (long)BuiltInCategory.OST_Windows);
                if (winFam == null && !doc.LoadFamily(windowRfa, out winFam)) throw new InvalidOperationException("창 패밀리를 로드하지 못함: " + windowRfa);
                var baseSym = (FamilySymbol)doc.GetElement(winFam.GetFamilySymbolIds().First());
                var syms = new Dictionary<string, FamilySymbol>();
                FamilySymbol Sym(double wm, double hm)
                {
                    string name = "P4 " + (int)Math.Round(wm * 1000) + "x" + (int)Math.Round(hm * 1000);
                    if (syms.TryGetValue(name, out var s)) return s;
                    s = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault(x => x.Family.Id == winFam.Id && x.Name == name)
                        ?? (FamilySymbol)baseSym.Duplicate(name);
                    (s.get_Parameter(BuiltInParameter.WINDOW_WIDTH) ?? s.get_Parameter(BuiltInParameter.FAMILY_WIDTH_PARAM))?.Set(Mm(wm * 1000));
                    (s.get_Parameter(BuiltInParameter.WINDOW_HEIGHT) ?? s.get_Parameter(BuiltInParameter.FAMILY_HEIGHT_PARAM))?.Set(Mm(hm * 1000));
                    if (!s.IsActive) s.Activate();
                    return syms[name] = s;
                }

                int nw = 0, nwin = 0;
                foreach (var k in stories)
                {
                    Level b = At((k - 1) * H), top = At(k * H);
                    Wall south = null, north = null;
                    foreach (var (ax, ay, bx, by, what) in WallLines())
                    {
                        var w = Wall.Create(doc, Line.CreateBound(new XYZ(Mm(ax * 1000), Mm(ay * 1000), b.ProjectElevation), new XYZ(Mm(bx * 1000), Mm(by * 1000), b.ProjectElevation)),
                                            wt.Id, b.Id, Mm((H - TS) * 1000), 0, false, true);
                        w.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.Set(top.Id);            // 윗면 = 위층 슬래브 밑면
                        w.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET)?.Set(-Mm(TS * 1000));
                        Note(w); nw++;
                        if (what == "외벽 남") south = w; else if (what == "외벽 북") north = w;
                    }
                    doc.Regenerate();
                    foreach (var (host, y, ops) in new[] { (south, WT / 2, South), (north, D - WT / 2, North) })
                        foreach (var (x0, x1, sill) in ops)
                        {
                            double hgt = sill == 0 ? 2.2 : 1.5;   // [가정] 거실 발코니창 2.2 m, 그 외 1.5 m
                            var fi = doc.Create.NewFamilyInstance(new XYZ(Mm((x0 + x1) / 2 * 1000), Mm(y * 1000), b.ProjectElevation), Sym(x1 - x0, hgt), host, b, StructuralType.NonStructural);
                            fi.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM)?.Set(Mm(sill * 1000));
                            Note(fi); nwin++;
                        }
                }
                // 계단실 샤프트 개구부 (전 층 슬래브 관통)
                var prof = new CurveArray();
                var c = new[] { new XYZ(Mm(Core.x0 * 1000), Mm(Core.y0 * 1000), 0), new XYZ(Mm(Core.x1 * 1000), Mm(Core.y0 * 1000), 0), new XYZ(Mm(Core.x1 * 1000), Mm(Core.y1 * 1000), 0), new XYZ(Mm(Core.x0 * 1000), Mm(Core.y1 * 1000), 0) };
                for (int i = 0; i < 4; i++) prof.Append(Line.CreateBound(c[i], c[(i + 1) % 4]));
                Note(doc.Create.NewOpening(At(0), At(15 * H), prof));
                t.Commit();
                log.Add("벽 " + nw + "개, 창 " + nwin + "개 (" + stories.Count + "개층), 계단실 샤프트 1개");
            }

            // 계단: 층마다 직선 1런, 폭 1.2 m, 계단실 가운데 (x = 15.0 m), y 12.6 → 17.1 m
            int ns = 0; var bad = new List<string>();
            foreach (var k in stories)
            {
                Level b = At((k - 1) * H), top = At(k * H);
                using (var scope = new StairsEditScope(doc, "P4 계단 " + k))
                {
                    var sid = scope.Start(b.Id, top.Id);
                    var stairs0 = (Stairs)doc.GetElement(sid);
                    double len = (stairs0.DesiredRisersNumber - 1) * stairs0.ActualTreadDepth;   // 필요한 디딤판 수 × 유형 디딤판 깊이
                    double yEnd = Mm(17700), yStart = Math.Max(Mm(12200), yEnd - len);
                    using (var t = new Transaction(doc, "계단 런"))
                    {
                        t.Start();
                        var run = StairsRun.CreateStraightRun(doc, sid, Line.CreateBound(new XYZ(Mm(15000), yStart, b.ProjectElevation), new XYZ(Mm(15000), yStart + len, b.ProjectElevation)), StairsRunJustification.Center);
                        run.ActualRunWidth = Mm(1200);
                        t.Commit();
                    }
                    scope.Commit(new Quiet());
                    var st = (Stairs)doc.GetElement(sid);
                    using (var t = new Transaction(doc, "계단 주석")) { t.Start(); Note(st); t.Commit(); }
                    if (st.ActualRisersNumber != st.DesiredRisersNumber) bad.Add(k + "층 " + st.ActualRisersNumber + "/" + st.DesiredRisersNumber);
                    ns++;
                }
            }
            log.Add("계단 " + ns + "개" + (bad.Count > 0 ? " (챌판 수 부족: " + string.Join(", ", bad) + ")" : ""));
            return log;
        }
    }
}

// 강도 예측 타임라인 (v6 3단계, docs/redesign_v6.md §4 — 예전 4D 시공 순서 애니메이션을 대신함). 계산 = Engine.StrengthTimeline (strength_timeline.py 이식) 한 곳.
//   3D: 전용 뷰 "Revit On-Site 강도 타임라인" 에서 구역 상자만 DirectContext3D 로 그 날짜 추정 강도 색 (추정 ÷ 그 층 fck, ZoneOverlay.StrengthSteps [가정]),
//       타설 전 구역은 흐린 회색. 그 층 실제 슬래브는 숨김(구역 상자가 대신). 동바리·벽 등 나머지 부재는 모델 그대로 (해체 애니메이션 없음).
//   추정은 '참고 (판정에 안 씀)' — 판정은 현장양생 공시체·3 m 안 센서 (Evaluator). 대시보드 곡선·공시체 점·기준선은 DashboardPane.DrawChart.
//   사건 (대시보드 |◀ 사건 ▶|): 층 첫 타설 · 층 추정이 14 MPa·fck 에 처음 닿는 날 (그 층 마지막 타설 뒤, 추정) · 공시체 시험일.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitOnSite.Engine;

namespace RevitOnSite
{
    public sealed class FourD
    {
        public enum Kind { Zone, Slab }

        public sealed class Group
        {
            public string Key;
            public Kind Kind;
            public int Floor;
            public string Zone;
            public readonly List<ElementId> Ids = new();
            public List<XYZ[]> Tris;        // FourDGraphics 가 읽음 — 여기서는 늘 null (부재 형상)
        }

        /// <summary>그날 한 묶음의 모습. Transparency 0–100 %</summary>
        public readonly record struct State(bool Visible, byte R, byte G, byte B, int Transparency)
        {
            public static readonly State Hidden = new(false, 0, 0, 0, 0);
        }

        static readonly State Ghost = new(true, 205, 205, 205, 82);   // 타설 전 구역 [가정] (흐린 회색)

        public readonly Document Doc;
        public readonly List<Group> Groups = new();
        public readonly DateOnly Start, End;
        /// <summary>층별·구역별 추정 곡선, 공시체 실측 점, 기준선 (대시보드 그림)</summary>
        public readonly StrengthTimelineResult Line;
        /// <summary>사건 날짜 → 글 (대시보드 사건 건너뛰기)</summary>
        public readonly SortedDictionary<DateOnly, List<string>> Events = new();
        readonly string folder;
        readonly Dictionary<string, List<double?>> zoneValues;
        readonly Dictionary<int, StrengthFloor> floorOf;

        /// <summary>API 문맥에서. 설정·구역·타설 기록이 없으면 InvalidOperationException (한국어)</summary>
        public FourD(Document doc)
        {
            Doc = doc;
            if (!ShoringModel.SettingsDone(doc)) throw new InvalidOperationException("첫 타설일이 없습니다. [프로젝트 설정]을 먼저 하세요.");
            var s = ProjectSettings.Read(doc);
            var zones = Replan.ZoneList(doc);
            if (zones.Count == 0) throw new InvalidOperationException("구역이 없습니다. [구역·동바리 생성]을 먼저 누르세요.");
            folder = s.CsvFolder;
            if (string.IsNullOrWhiteSpace(folder) || !System.IO.Directory.Exists(folder)) throw new InvalidOperationException("데이터 폴더가 없습니다. [프로젝트 설정]에서 데이터 폴더를 고르세요.");
            Line = StrengthTimeline.Timeline(folder, cureMin: s.CureMin);
            if (Line.Dates.Count == 0) throw new InvalidOperationException("타설 기록이 없습니다 (pours.csv). 타설일을 넣으면 그날부터 강도 예측이 보입니다.");
            Start = Line.Start!.Value;
            End = Line.End!.Value;
            zoneValues = Line.Zones.ToDictionary(z => z.ZoneId, z => z.Values);
            floorOf = Line.Floors.ToDictionary(f => f.Floor);

            foreach (var z in zones)
            {
                var g = new Group { Key = "Z|" + z.Id, Kind = Kind.Zone, Floor = z.Floor, Zone = z.Id };
                g.Ids.Add(z.E.Id);
                Groups.Add(g);
            }
            var withZones = zones.Select(z => z.Floor).ToHashSet();
            foreach (var f in ZoneGenerator.DetectFloors(doc).Where(f => withZones.Contains(f.FloorNo)))
            {
                var g = new Group { Key = "F|" + f.FloorNo, Kind = Kind.Slab, Floor = f.FloorNo };
                g.Ids.AddRange(f.Slabs.Select(e => e.Id));
                Groups.Add(g);
            }
            BuildEvents(zones.GroupBy(z => z.Floor).ToDictionary(g => g.Key, g => g.Select(z => z.Id).ToList()));
        }

        void AddEvent(DateOnly d, string s) => (Events.TryGetValue(d, out var l) ? l : Events[d] = new()).Add(s);

        void BuildEvents(Dictionary<int, List<string>> zonesOfFloor)
        {
            foreach (var f in Line.Floors)
            {
                int first = f.Values.FindIndex(v => v != null);
                if (first >= 0) AddEvent(Line.Dates[first], f.Label + " 타설");
                // 층의 (타설 기록이 있는) 구역이 다 타설된 뒤에만 셈 — 앞 구역만 있을 때 닿았다가 다음 구역 타설로 다시 내려가는 것을 사건으로 안 셈
                var zs = (zonesOfFloor.GetValueOrDefault(f.Floor) ?? new()).Where(zoneValues.ContainsKey).ToList();
                int all = Enumerable.Range(0, Line.Dates.Count).FirstOrDefault(i => zs.All(z => zoneValues[z][i] != null), -1);
                if (all < 0) continue;
                foreach (var (mpa, name) in new[] { (Line.Lines.First(l => l.Name == "지지 슬래브").Mpa, "지지 슬래브 기준"), (f.Fck, "fck") })
                {
                    int hit = Enumerable.Range(all, Line.Dates.Count - all).FirstOrDefault(i => f.Values[i] >= mpa, -1);
                    if (hit >= 0) AddEvent(Line.Dates[hit], $"{f.Label} 추정 {mpa:0.#} MPa ({name}) 도달 — 참고");
                }
            }
            foreach (var c in Line.Cylinders.Where(c => c.Date >= Start && c.Date <= End))
                AddEvent(c.Date, $"{c.ZoneId} 공시체 {c.Mpa:0.0} MPa");
        }

        int Index(DateOnly d) => Math.Clamp(d.DayNumber - Start.DayNumber, 0, Line.Dates.Count - 1);

        /// <summary>대시보드 한 줄: 위 4개층 추정 강도 (참고) + 그날 사건</summary>
        public string DoingLine(DateTime t)
        {
            var d = DateOnly.FromDateTime(t);
            int i = Index(d);
            var parts = d < Start ? new List<string>() : Line.Floors.Where(f => f.Values[i] != null).OrderByDescending(f => f.Floor).Take(4)
                .Select(f => $"{f.Label} {f.Values[i]:0.0}").ToList();
            var line = parts.Count == 0 ? "타설 전" : "추정 강도 (참고 · 판정에 안 씀) " + string.Join(" · ", parts) + " MPa";
            return Events.TryGetValue(d, out var ev) ? line + " — " + string.Join(" · ", ev) : line;
        }

        /// <summary>4D 미리보기 [색: 근거 단계] (Timeline 이 프레임마다 넣음) — 타설된 구역 상자를 근거 단계 색으로, 웹 공유판과 같음</summary>
        public bool EvidenceColors;
        readonly Dictionary<DateOnly, Dictionary<string, int>> evCache = new();

        /// <summary>웹 EVC (총괄 확정 11-04, 15차 점검: 1단계 옅은 색이 흰 슬래브와 안 갈림): 0 = 회색(196,200,204),
        /// 1–8 = 연녹(200,230,200) → 진녹(34,118,64) 선형 u = (k−1)/7, 반올림 (JS Math.round = 양수에서 AwayFromZero)</summary>
        public static State EvidenceColor(int k)
        {
            if (k <= 0) return new State(true, 196, 200, 204, 0);
            double u = (Math.Min(k, 8) - 1) / 7.0;
            byte L(int a, int b) => (byte)Math.Round(a + (b - a) * u, MidpointRounding.AwayFromZero);
            return new State(true, L(200, 34), L(230, 118), L(200, 64), 0);
        }

        /// <summary>날짜 upTo 까지의 근거 원장 → 구역별 갖춘 단계 (Engine.Evidence.LedgerAt, 날짜별 캐시). 읽기 오류면 빈 표 (모두 0)</summary>
        Dictionary<string, int> EvStages(DateOnly upTo)
        {
            if (evCache.TryGetValue(upTo, out var hit)) return hit;
            var map = new Dictionary<string, int>();
            try { foreach (var (z, l) in Evidence.LedgerAt(folder, upTo)) map[z] = l.Stage; }
            catch (Exception ex) { Dashboard.Log("4D 근거 단계 " + upTo + " 건너뜀: " + ex.Message); }
            return evCache[upTo] = map;
        }

        /// <summary>추정 강도 색 (ZoneOverlay.StrengthSteps — 애드인 [표시: 강도]와 같은 구간 [가정]). null = 타설 전</summary>
        public static State StrengthColor(double? mpa, double fck)
        {
            if (StrengthTimeline.StepOf(mpa, fck) is not int k) return Ghost;
            var c = ZoneOverlay.StrengthSteps[k].C;
            return new State(true, c.Red, c.Green, c.Blue, 0);
        }

        /// <summary>시각 t 의 묶음별 모습 (날짜 단위)</summary>
        public Dictionary<string, State> States(DateTime t)
        {
            var d = DateOnly.FromDateTime(t);
            int i = Index(d);
            var evs = EvidenceColors ? EvStages(d.AddDays(-1)) : null;   // 근거 단계: 그날 서류는 그날이 끝나야 (웹: n = 날짜 + 1)
            var outp = new Dictionary<string, State>();
            foreach (var g in Groups)
            {
                double? v = d < Start || g.Zone == null || !zoneValues.TryGetValue(g.Zone, out var vals) ? null : vals[i];
                outp[g.Key] = g.Kind switch
                {
                    Kind.Zone when v == null => Ghost,
                    Kind.Zone when evs != null => EvidenceColor(evs.GetValueOrDefault(g.Zone)),
                    Kind.Zone => StrengthColor(v, floorOf.TryGetValue(g.Floor, out var f) ? f.Fck : Line.Floors.Min(x => x.Fck)),
                    _ => State.Hidden,   // 그 층 실제 슬래브 — 구역 상자가 대신
                };
            }
            return outp;
        }

        /// <summary>앞 프레임보다 색이 바뀐 구역 (타설·강도 구간이 바뀜) — 대시보드 강조</summary>
        public HashSet<string> Changed(Dictionary<string, State> prev, Dictionary<string, State> cur)
        {
            var outp = new HashSet<string>();
            if (prev == null) return outp;
            foreach (var g in Groups)
                if (g.Kind == Kind.Zone && cur.TryGetValue(g.Key, out var b) && prev.TryGetValue(g.Key, out var a) && a != b) outp.Add(g.Key);
            return outp;
        }

        // ── 전용 뷰 (구역 상자·그 층 슬래브는 숨기고 DirectContext3D 가 그 자리를 그림)
        public const string PreviewViewName = "Revit On-Site 강도 타임라인";
        /// <summary>v6 전 4D 시공 순서 뷰 — 벽·동바리·센서까지 숨겨 둔 뷰라 새 뷰를 만들고 지움 (활성 뷰면 다음에)</summary>
        const string OldViewName = "Revit On-Site 4D 미리보기";

        public IEnumerable<ElementId> ManagedIds => Groups.SelectMany(g => g.Ids);

        /// <summary>검측 3D 뷰 (없으면 만든다). 열린 트랜잭션 안에서</summary>
        static View3D StatusView(Document doc)
        {
            var v = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(x => !x.IsTemplate && x.Name == ZoneEvaluation.StatusViewName);
            if (v != null) return v;
            v = ZoneEvaluation.StatusView(doc);
            ZoneEvaluation.ApplyFilters(doc, v);
            return v;
        }

        View3D Find(string name) => new FilteredElementCollector(Doc).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(x => !x.IsTemplate && x.Name == name);
        public View3D FindPreviewView() => Find(PreviewViewName);
        View3D OldView() => Find(OldViewName) is View3D o && Doc.ActiveView?.Id != o.Id ? o : null;

        /// <summary>DirectContext3D 덧그림이 보이는 표시 방식. 사실적(Realistic) 등에선 Revit 이 그리지 않음 (9차 점검 N3) — 확인한 방식만 허용, 나머지는 음영으로</summary>
        public static bool Drawable(DisplayStyle s) => s is DisplayStyle.FlatColors or DisplayStyle.Shading or DisplayStyle.ShadingWithEdges;

        /// <summary>전용 뷰가 없거나, 관리 부재 중 그 뷰에서 아직 안 숨긴 것이 있거나, 예전 4D 뷰가 남았으면 true (→ 한 번의 App.Write 가 필요)</summary>
        public bool PreviewNeedsWrite()
        {
            var v = FindPreviewView();
            return v == null || !Drawable(v.DisplayStyle) || OldView() != null || ManagedIds.Select(Doc.GetElement).Any(e => e != null && !e.IsHidden(v) && e.CanBeHidden(v));
        }

        /// <summary>열린 트랜잭션 안에서: 전용 뷰 = 검측 뷰 복제(단면 상자 끔) + 관리 부재 숨김 (뷰 표시만, 부재는 그대로) · 예전 4D 뷰 지움</summary>
        public View3D EnsurePreviewView()
        {
            if (OldView() is View3D old) Doc.Delete(old.Id);
            var v = FindPreviewView();
            if (v == null)
            {
                v = (View3D)Doc.GetElement(StatusView(Doc).Duplicate(ViewDuplicateOption.Duplicate));
                v.Name = PreviewViewName;
                v.IsSectionBoxActive = false;
            }
            if (!Drawable(v.DisplayStyle)) v.DisplayStyle = DisplayStyle.Shading;
            var hide = ManagedIds.Select(Doc.GetElement).Where(e => e != null && !e.IsHidden(v) && e.CanBeHidden(v)).Select(e => e.Id).ToList();
            if (hide.Count > 0) v.HideElements(hide);
            return v;
        }
    }
}

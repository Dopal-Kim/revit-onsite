// 검측 3D 덧그림 (P8, 사용자 요구 "구역을 선택하면 강조 표시해 선택적으로 조회") — "Revit On-Site 검측" 3D 뷰에서만, DirectContext3D (화면에만, 모델·뷰 설정·실행 취소 목록 그대로).
//   ① 구역 ID 글자: 구역 상자 윗면 위, 선 글꼴(StrokeFont). 3D 뷰에는 주석 문자를 둘 수 없어 (잠긴 뷰만) 직접 그린다. 단면 상자 위로 잘린 구역은 생략.
//   ② 선택 강조: 고른 구역 상자 모서리 굵은 선 + 상태색 반투명 덮개, 그 구역 동바리·센서를 진하게 (ZoneFocus.Highlight 가 채움).
//   자료는 API 문맥(뷰 바뀜·문서 바뀜·선택)에서 만들고, RenderScene 은 그리기만 (예외는 삼키고 덧그림을 끔)
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;

namespace RevitOnSite
{
    public sealed class ZoneLabels : IDirectContext3DServer
    {
        static readonly Guid ServerId = new("8A4D2C71-5E3B-4F08-A6C9-1B7E9D3F4A52");
        public static readonly ZoneLabels Instance = new();
        static readonly ColorWithTransparency Ink = new(25, 25, 25, 0);

        /// <summary>색 있는 삼각형 묶음 + GPU 버퍼 (처음 그릴 때 한 번 만듦)</summary>
        public sealed class Set
        {
            public readonly List<(XYZ[] T, ColorWithTransparency C)> Tris = new();
            public List<(VertexBuffer Vb, IndexBuffer Ib, int Verts, int Tris)> Bufs;   // 21 845 삼각형(65 535 꼭짓점)마다 하나 — 색인이 16비트 (코드 검토 2차)
            public void Add(IEnumerable<XYZ[]> ts, ColorWithTransparency c) { foreach (var t in ts) Tris.Add((t, c)); }
            public void Release() { if (Bufs != null) foreach (var b in Bufs) { b.Vb?.Dispose(); b.Ib?.Dispose(); } Bufs = null; }
        }

        sealed class Layers
        {
            public Set Labels = new(), Hi = new(), Cover = new();   // 불투명: 글자·강조선 / 반투명: 덮개
            public Set ShoreText = new();                            // 동바리 뷰(V1) 층별 "9F 416" 글자
            public Set Status = new(), Blink = new();                // V2 동바리 상태: 불투명 / 깜빡임(BlinkOn 일 때만)
            public Outline Bounds, ShoreBounds;
            public void Release() { Labels.Release(); Hi.Release(); Cover.Release(); ShoreText.Release(); Status.Release(); Blink.Release(); }
        }
        readonly Dictionary<string, Layers> docs = new();   // 문서 키 → 덧그림
        VertexFormat format;
        EffectInstance effect, coverEffect;   // 덮개는 효과의 투명도로 (정점 색은 불투명)
        bool failed;
        static bool registered, closed;
        /// <summary>V2 깜빡임 위상 (ZoneFocus 타이머가 0.5 초마다 바꿈)</summary>
        internal static bool BlinkOn;
        /// <summary>33차 진단: 동바리 글자를 바꾼 뒤 CanExecute·첫 그림을 대시보드 기록에 한 번씩 (34차: 동바리 상태 덧그림도)</summary>
        bool logCan, logDraw, logStatCan, logStatDraw;

        /// <summary>API 문맥에서 한 번 (OnStartup): DirectContext3D 서비스에 넣고 켬. 실패하면 덧그림 없이 계속</summary>
        public static void Register()
        {
            try
            {
                var svc = (MultiServerService)ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService);
                if (!svc.IsRegisteredServerId(ServerId)) svc.AddServer(Instance);
                var ids = svc.GetActiveServerIds();
                if (!ids.Contains(ServerId)) { ids.Add(ServerId); svc.SetActiveServers(ids); }
                registered = true;
                closed = false;
            }
            catch (Exception ex) { Dashboard.Log("검측 덧그림 서버 등록 실패: " + ex.Message); }
        }

        /// <summary>Revit 종료 (Dashboard.Closing, N12): 서버를 끄고 빼고 GPU 버퍼·효과를 놓음 — 그래픽이 살아 있을 때. 여러 번 불러도 됨.
        /// shortcut: 종료가 취소되면 Revit 을 다시 켤 때까지 구역 글자 없음 — 종료 중 문서 닫기 이벤트가 서버를 다시 넣지 않게 자동 재등록은 안 함</summary>
        public static void Unregister()
        {
            var z = Instance;
            foreach (var l in z.docs.Values) l.Release();
            z.docs.Clear();
            z.effect?.Dispose(); z.coverEffect?.Dispose(); z.format?.Dispose();
            (z.effect, z.coverEffect, z.format) = (null, null, null);
            closed = true;
            if (!registered) return;
            registered = false;
            var svc = (MultiServerService)ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService);
            var ids = svc.GetActiveServerIds();
            if (ids.Remove(ServerId)) svc.SetActiveServers(ids);
            if (svc.IsRegisteredServerId(ServerId)) svc.RemoveServer(ServerId);
        }

        /// <summary>문서를 닫을 때 (DocumentClosing): 그 문서의 덧그림·GPU 버퍼를 놓음 — 같은 파일을 다시 열면 옛 덧그림이 남던 것 (코드 검토 2차)</summary>
        public static void Forget(Document doc)
        {
            var key = Dashboard.Key(doc);
            if (Instance.docs.Remove(key, out var l)) l.Release();
        }

        Layers For(Document doc)
        {
            var key = Dashboard.Key(doc);
            if (!docs.TryGetValue(key, out var l)) docs[key] = l = new Layers();
            return l;
        }

        static bool IsStatusView(View v) => v is View3D && v.Name == ZoneEvaluation.StatusViewName;
        static bool IsShoreView(View v) => v is View3D && v.Name == ShoreView.ViewName;

        /// <summary>API 문맥: 동바리 뷰(V1) 층별 글자 바꿈</summary>
        public static void SetShoreText(Document doc, Set text)
        {
            var l = Instance.For(doc);
            l.ShoreText.Release();
            l.ShoreText = text;
            var pts = text.Tris.SelectMany(t => t.T).ToList();
            l.ShoreBounds = Span(pts);
            Instance.logCan = Instance.logDraw = true;
            Dashboard.Log($"동바리 글자: 삼각형 {text.Tris.Count} · 덧그림 서버 등록 {registered} · 종료 정리 {closed} · 예외로 꺼짐 {Instance.failed} · 범위 "
                + (l.ShoreBounds == null ? "없음" : $"{l.ShoreBounds.MinimumPoint} ~ {l.ShoreBounds.MaximumPoint}"));
        }

        /// <summary>점들의 범위 + 사방 0.5 m (33차: 세운 글자판은 두께 0 이라 범위 한 변이 0 → Revit 이 빈 범위로 보고 안 그릴 수 있음)</summary>
        static Outline Span(List<XYZ> pts)
        {
            if (pts.Count == 0) return null;
            var pad = new XYZ(M(0.5), M(0.5), M(0.5));
            return new Outline(new XYZ(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Min(p => p.Z)) - pad, new XYZ(pts.Max(p => p.X), pts.Max(p => p.Y), pts.Max(p => p.Z)) + pad);
        }

        /// <summary>API 문맥: 문서의 활성 뷰가 검측 뷰면 구역 ID 글자를 다시 만듦 (뷰 바뀜·문서 바뀜 때)</summary>
        public static void Update(Document doc)
        {
            try
            {
                if (closed || doc == null || !doc.IsValidObject || doc.IsFamilyDocument || !IsStatusView(doc.ActiveView)) return;   // 종료 정리 뒤엔 아무것도 안 만듦
                var (bottom, top) = SectionZ((View3D)doc.ActiveView);
                var set = new Set();
                double tol = M(0.1);
                foreach (var z in ShoringModel.Zones(doc))
                {
                    var bb = z.get_BoundingBox(null);
                    var id = z.LookupParameter("SHR_ZoneID")?.AsString();
                    if (bb == null || string.IsNullOrEmpty(id) || bb.Max.Z > top + tol || bb.Max.Z < bottom) continue;
                    double w = bb.Max.X - bb.Min.X, d = bb.Max.Y - bb.Min.Y;
                    double h = Math.Clamp(Math.Min(w, d) * 0.12, M(0.35), M(1.0));                 // 글자 높이 [가정] 구역 짧은 변의 12 %
                    double width = StrokeFont.Width(id) * h / 6;
                    if (width > w * 0.9) h *= w * 0.9 / width;
                    set.Add(StrokeFont.Triangles(id, new XYZ((bb.Min.X + bb.Max.X) / 2, (bb.Min.Y + bb.Max.Y) / 2, bb.Max.Z + M(0.03)), h), Ink);
                }
                var l = Instance.For(doc);
                l.Labels.Release();
                l.Labels = set;
                l.Bounds = Bounds(l);
            }
            catch (Exception ex) { Dashboard.Log("구역 ID 글자 만들기 실패: " + ex.Message); }
        }

        /// <summary>단면 상자 아래·위 높이 (켜져 있고 수평일 때만, 아니면 끝없이)</summary>
        internal static (double Bottom, double Top) SectionZ(View3D v)
        {
            if (!v.IsSectionBoxActive) return (double.MinValue, double.MaxValue);
            var sb = v.GetSectionBox();
            return sb.Transform.BasisZ.IsAlmostEqualTo(XYZ.BasisZ) ? (sb.Transform.OfPoint(sb.Min).Z, sb.Transform.OfPoint(sb.Max).Z) : (double.MinValue, double.MaxValue);
        }

        /// <summary>API 문맥: V2 동바리 상태 덧그림 바꿈 (status = 늘 그림, blink = BlinkOn 일 때만). 둘 다 빈 묶음이면 없음</summary>
        public static void SetShoreStatus(Document doc, Set status, Set blink)
        {
            var l = Instance.For(doc);
            l.Status.Release(); l.Blink.Release();
            (l.Status, l.Blink) = (status, blink);
            l.Bounds = Bounds(l);
            Instance.logStatCan = Instance.logStatDraw = status.Tris.Count + blink.Tris.Count > 0;
        }

        /// <summary>API 문맥: 선택 강조 덧그림 바꿈 (hi = 불투명, cover = 반투명). 둘 다 빈 묶음이면 강조 없음</summary>
        public static void SetHighlight(Document doc, Set hi, Set cover)
        {
            var l = Instance.For(doc);
            l.Hi.Release(); l.Cover.Release();
            (l.Hi, l.Cover) = (hi, cover);
            l.Bounds = Bounds(l);
        }

        static Outline Bounds(Layers l)
        {
            return Span(l.Labels.Tris.Concat(l.Hi.Tris).Concat(l.Cover.Tris).Concat(l.Status.Tris).Concat(l.Blink.Tris).SelectMany(t => t.T).ToList());
        }

        static double M(double m) => UnitUtils.ConvertToInternalUnits(m, UnitTypeId.Meters);

        /// <summary>축 정렬 상자 (바깥 법선 방향 감김, 양면은 그리는 쪽이 신경 쓰지 않도록 두 번)</summary>
        public static List<XYZ[]> Box(XYZ a, XYZ b)
        {
            XYZ P(int i) => new((i & 1) == 0 ? a.X : b.X, (i & 2) == 0 ? a.Y : b.Y, (i & 4) == 0 ? a.Z : b.Z);
            var faces = new[] { (0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5) };
            var t = new List<XYZ[]>();
            foreach (var (i, j, k, m) in faces)
            {
                t.Add(new[] { P(i), P(j), P(k) }); t.Add(new[] { P(i), P(k), P(m) });
                t.Add(new[] { P(i), P(k), P(j) }); t.Add(new[] { P(i), P(m), P(k) });
            }
            return t;
        }

        /// <summary>상자 bb 의 모서리 12개를 굵기 w, 길이 dash 토막·같은 길이 빈칸으로 (점선 윤곽 — V2 '최근 24 h 확인 없음')</summary>
        public static List<XYZ[]> DashedEdges(BoundingBoxXYZ bb, double w, double dash)
        {
            double h = w / 2;
            XYZ lo = bb.Min, hi = bb.Max;
            var t = new List<XYZ[]>();
            void Line(XYZ a, XYZ b)
            {
                var d = b - a;
                double len = d.GetLength();
                if (len < 1e-9) return;
                var u = d / len;
                for (double s = 0; s < len; s += 2 * dash)
                {
                    XYZ p = a + u * s, q = a + u * Math.Min(len, s + dash);
                    t.AddRange(Box(new XYZ(Math.Min(p.X, q.X) - h, Math.Min(p.Y, q.Y) - h, Math.Min(p.Z, q.Z) - h), new XYZ(Math.Max(p.X, q.X) + h, Math.Max(p.Y, q.Y) + h, Math.Max(p.Z, q.Z) + h)));
                }
            }
            foreach (var y in new[] { lo.Y, hi.Y }) foreach (var z in new[] { lo.Z, hi.Z }) Line(new XYZ(lo.X, y, z), new XYZ(hi.X, y, z));
            foreach (var x in new[] { lo.X, hi.X }) foreach (var z in new[] { lo.Z, hi.Z }) Line(new XYZ(x, lo.Y, z), new XYZ(x, hi.Y, z));
            foreach (var x in new[] { lo.X, hi.X }) foreach (var y in new[] { lo.Y, hi.Y }) Line(new XYZ(x, y, lo.Z), new XYZ(x, y, hi.Z));
            return t;
        }

        /// <summary>bb 평면 둘레 납작한 띠 (높이 z0~z1): 둘레에서 안쪽 inner, 바깥쪽 outer 만큼 — 네 토막 상자</summary>
        public static List<XYZ[]> Ring(BoundingBoxXYZ bb, double z0, double z1, double inner, double outer)
        {
            double x0 = bb.Min.X - outer, x1 = bb.Max.X + outer, y0 = bb.Min.Y - outer, y1 = bb.Max.Y + outer;
            double xi0 = bb.Min.X + inner, xi1 = bb.Max.X - inner, yi0 = bb.Min.Y + inner, yi1 = bb.Max.Y - inner;
            var t = new List<XYZ[]>();
            t.AddRange(Box(new XYZ(x0, y0, z0), new XYZ(x1, yi0, z1)));     // 남
            t.AddRange(Box(new XYZ(x0, yi1, z0), new XYZ(x1, y1, z1)));     // 북
            t.AddRange(Box(new XYZ(x0, yi0, z0), new XYZ(xi0, yi1, z1)));   // 서
            t.AddRange(Box(new XYZ(xi1, yi0, z0), new XYZ(x1, yi1, z1)));   // 동
            return t;
        }

        /// <summary>상자 bb 의 모서리 12개를 굵기 w 인 각기둥으로 (굵은 외곽선)</summary>
        public static List<XYZ[]> Edges(BoundingBoxXYZ bb, double w)
        {
            double h = w / 2;
            XYZ lo = bb.Min, hi = bb.Max;
            var t = new List<XYZ[]>();
            foreach (var y in new[] { lo.Y, hi.Y }) foreach (var z in new[] { lo.Z, hi.Z }) t.AddRange(Box(new XYZ(lo.X - h, y - h, z - h), new XYZ(hi.X + h, y + h, z + h)));
            foreach (var x in new[] { lo.X, hi.X }) foreach (var z in new[] { lo.Z, hi.Z }) t.AddRange(Box(new XYZ(x - h, lo.Y - h, z - h), new XYZ(x + h, hi.Y + h, z + h)));
            foreach (var x in new[] { lo.X, hi.X }) foreach (var y in new[] { lo.Y, hi.Y }) t.AddRange(Box(new XYZ(x - h, y - h, lo.Z - h), new XYZ(x + h, y + h, hi.Z + h)));
            return t;
        }

        // ── DirectContext3D 서버
        public Guid GetServerId() => ServerId;
        public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;
        public string GetName() => "Revit On-Site 검측 덧그림";
        public string GetVendorId() => "KHU.BuildUp2";
        public string GetDescription() => "검측 3D 뷰에 구역 ID·선택 구역 강조·동바리 상태 색을 화면에만 그립니다 (모델은 바꾸지 않음).";
        public string GetApplicationId() => "";
        public string GetSourceId() => "";
        public bool UsesHandles() => false;
        Layers Of(View view) => view?.Document is Document d && docs.TryGetValue(Dashboard.Key(d), out var l) ? l : null;
        public bool CanExecute(View view)
        {
            bool ok = !failed && (IsStatusView(view) && Of(view) is { Bounds: not null } || IsShoreView(view) && Of(view) is { ShoreBounds: not null });
            if (logCan && IsShoreView(view)) { logCan = false; Dashboard.Log($"동바리 글자 CanExecute: {ok} (뷰 {view.Id} · 문서 덧그림 {(Of(view) == null ? "없음" : "있음")} · 꺼짐 {failed})"); }
            if (logStatCan && IsStatusView(view)) { logStatCan = false; Dashboard.Log($"동바리 상태 덧그림 CanExecute: {ok} (검측 뷰 {view.Id} · 꺼짐 {failed})"); }
            return ok;
        }
        public Outline GetBoundingBox(View view) => IsShoreView(view) ? Of(view)?.ShoreBounds : Of(view)?.Bounds;
        public bool UseInTransparentPass(View view) => true;

        public void RenderScene(View view, DisplayStyle displayStyle)
        {
            try
            {
                var l = Of(view);
                if (l == null) return;
                format ??= new VertexFormat(VertexFormatBits.PositionColored);
                if (effect == null || !effect.IsValid()) effect = new EffectInstance(VertexFormatBits.PositionColored);
                if (coverEffect == null || !coverEffect.IsValid()) { coverEffect = new EffectInstance(VertexFormatBits.PositionColored); coverEffect.SetTransparency(0.55); }
                if (IsShoreView(view))
                {
                    if (DrawContext.IsTransparentPass()) return;
                    Draw(l.ShoreText, effect);
                    if (logDraw) { logDraw = false; Dashboard.Log($"동바리 글자 그림: 삼각형 {l.ShoreText.Tris.Count} (뷰 {view.Id})"); }
                }
                else if (DrawContext.IsTransparentPass()) Draw(l.Cover, coverEffect);
                else
                {
                    Draw(l.Labels, effect); Draw(l.Status, effect); if (BlinkOn) Draw(l.Blink, effect); Draw(l.Hi, effect);
                    if (logStatDraw && l.Status.Tris.Count > 0) { logStatDraw = false; Dashboard.Log($"동바리 상태 덧그림 그림: 채움 {l.Status.Tris.Count} · 깜빡 {l.Blink.Tris.Count} 삼각형 (뷰 {view.Id}, 깜빡 켜짐 {BlinkOn})"); }
                }
            }
            catch (Exception ex)
            {
                failed = true;   // 예외를 Revit 그리기로 내보내지 않음 — 덧그림만 끔 (Revit 을 다시 켜면 다시 시도)
                Dashboard.Log("검측 덧그림 예외 — 끔: " + ex);
            }
        }

        void Draw(Set s, EffectInstance fx)
        {
            if (s.Tris.Count == 0) return;
            const int MaxTris = 65535 / 3;   // 색인 버퍼가 16비트 — KBIM 처럼 큰 모델에서 한 버퍼에 넣으면 넘침 (코드 검토 2차)
            if (s.Bufs == null || s.Bufs.Any(b => !b.Vb.IsValid() || !b.Ib.IsValid()))
            {
                s.Release();
                s.Bufs = new();
                for (int k = 0; k < s.Tris.Count; k += MaxTris)
                {
                    int n = Math.Min(MaxTris, s.Tris.Count - k), nv = n * 3;
                    int vf = nv * VertexPositionColored.GetSizeInFloats();
                    var vb = new VertexBuffer(vf);
                    vb.Map(vf);
                    var vs = vb.GetVertexStreamPositionColored();
                    for (int i = k; i < k + n; i++) { var (t, c) = s.Tris[i]; foreach (var p in t) vs.AddVertex(new VertexPositionColored(p, c)); }
                    vb.Unmap();
                    int ix = n * IndexTriangle.GetSizeInShortInts();
                    var ib = new IndexBuffer(ix);
                    ib.Map(ix);
                    var ts = ib.GetIndexStreamTriangle();
                    for (int i = 0; i < nv; i += 3) ts.AddTriangle(new IndexTriangle(i, i + 1, i + 2));
                    ib.Unmap();
                    s.Bufs.Add((vb, ib, nv, n));
                }
            }
            foreach (var (vb, ib, nv, n) in s.Bufs) DrawContext.FlushBuffer(vb, nv, ib, nv, format, fx, PrimitiveType.TriangleList, 0, n);
        }
    }

    /// <summary>선 글꼴: 글자마다 4×6 격자 위 꺾은선 ("x,y x,y|…"). 대문자·숫자·-·% 만 (구역 ID: 9F-A1, B1-C2, PH-A1 / 타설 진행 45%)</summary>
    public static class StrokeFont
    {
        public static readonly Dictionary<char, string> Glyphs = new()
        {
            ['0'] = "0,0 4,0 4,6 0,6 0,0|0,1 4,5", ['1'] = "1,5 2,6 2,0|1,0 3,0", ['2'] = "0,5 1,6 3,6 4,5 4,4 0,0 4,0", ['3'] = "0,6 4,6 4,0 0,0|1,3 4,3",
            ['4'] = "3,0 3,6 0,2 4,2", ['5'] = "4,6 0,6 0,3 3,3 4,2 4,1 3,0 0,0", ['6'] = "4,6 1,6 0,5 0,0 4,0 4,3 0,3", ['7'] = "0,6 4,6 1,0",
            ['8'] = "0,0 4,0 4,6 0,6 0,0|0,3 4,3", ['9'] = "4,3 0,3 0,6 4,6 4,1 3,0 0,0",
            ['A'] = "0,0 0,4 2,6 4,4 4,0|0,3 4,3", ['B'] = "0,0 0,6 3,6 4,5 4,4 3,3 0,3|3,3 4,2 4,1 3,0 0,0", ['C'] = "4,6 0,6 0,0 4,0",
            ['D'] = "0,0 0,6 3,6 4,5 4,1 3,0 0,0", ['E'] = "4,6 0,6 0,0 4,0|0,3 3,3", ['F'] = "4,6 0,6 0,0|0,3 3,3", ['G'] = "4,6 0,6 0,0 4,0 4,3 2,3",
            ['H'] = "0,6 0,0|4,6 4,0|0,3 4,3", ['I'] = "1,6 3,6|2,6 2,0|1,0 3,0", ['J'] = "4,6 4,0 0,0 0,2", ['K'] = "0,6 0,0|4,6 0,3 4,0",
            ['L'] = "0,6 0,0 4,0", ['M'] = "0,0 0,6 2,3 4,6 4,0", ['N'] = "0,0 0,6 4,0 4,6", ['O'] = "0,0 4,0 4,6 0,6 0,0", ['P'] = "0,0 0,6 4,6 4,3 0,3",
            ['Q'] = "0,0 4,0 4,6 0,6 0,0|2,2 4,0", ['R'] = "0,0 0,6 4,6 4,3 0,3 4,0", ['S'] = "4,6 0,6 0,3 4,3 4,0 0,0", ['T'] = "0,6 4,6|2,6 2,0",
            ['U'] = "0,6 0,0 4,0 4,6", ['V'] = "0,6 2,0 4,6", ['W'] = "0,6 1,0 2,3 3,0 4,6", ['X'] = "0,6 4,0|4,6 0,0", ['Y'] = "0,6 2,3 4,6|2,3 2,0",
            ['Z'] = "0,6 4,6 0,0 4,0", ['-'] = "1,3 3,3", ['%'] = "0,0 4,6|0,5 1,6 0,6 0,5|3,0 4,1 4,0 3,0", ['('] = "3,6 1,4 1,2 3,0", [')'] = "1,6 3,4 3,2 1,0",
        };

        /// <summary>글자 폭 (격자 단위, 글자 간격 1 포함)</summary>
        public static double Width(string s) => Math.Max(0, s.Length * 5 - 1);

        /// <summary>문자열 → 수평면(z 고정) 삼각형. c = 가운데, h = 글자 높이 (내부 단위). 획 굵기 = 높이의 15 %. 양면</summary>
        public static List<XYZ[]> Triangles(string s, XYZ c, double h) => Triangles(s, c, h, XYZ.BasisX, XYZ.BasisY);

        /// <summary>글자 줄 방향 right, 글자 위 방향 up 인 평면에 (세로 글자판 = up 에 BasisZ). weight = 획 반 굵기 ÷ 격자 (0.45 보통, 0.75 굵게)</summary>
        public static List<XYZ[]> Triangles(string s, XYZ c, double h, XYZ right, XYZ up, double weight = 0.45)
        {
            double u = h / 6, half = u * weight;
            var n0 = right.CrossProduct(up).Normalize();
            var o = c - right * (Width(s) * u / 2) - up * (h / 2);
            var t = new List<XYZ[]>();
            for (int i = 0; i < s.Length; i++)
            {
                if (!Glyphs.TryGetValue(char.ToUpperInvariant(s[i]), out var g)) continue;
                foreach (var line in g.Split('|'))
                {
                    var p = line.Split(' ').Select(q => q.Split(',')).Select(q => o
                        + right * ((i * 5 + double.Parse(q[0], System.Globalization.CultureInfo.InvariantCulture)) * u)
                        + up * (double.Parse(q[1], System.Globalization.CultureInfo.InvariantCulture) * u)).ToList();
                    for (int k = 0; k + 1 < p.Count; k++)
                    {
                        var d = p[k + 1] - p[k];
                        if (d.GetLength() < 1e-9) continue;
                        d = d.Normalize() * half;
                        var n = n0.CrossProduct(d);   // 획 진행 방향에 수직, 글자판 안에서
                        XYZ a = p[k] - d + n, b = p[k + 1] + d + n, e = p[k + 1] + d - n, f = p[k] - d - n;   // 양 끝을 반 굵기만큼 늘려 꺾인 곳이 비지 않게
                        t.Add(new[] { a, f, e }); t.Add(new[] { a, e, b });
                        t.Add(new[] { a, e, f }); t.Add(new[] { a, b, e });     // 뒷면 (뒷면 제거가 있어도 보이게)
                    }
                }
            }
            return t;
        }
    }
}

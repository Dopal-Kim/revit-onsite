// 강도 타임라인 그림 — DirectContext3D 서버 (Revit 2018+ 공개 API). 트랜잭션 없이 화면에만 그리므로 모델·실행 취소 목록이 안 바뀐다.
//   "Revit On-Site 강도 타임라인" 뷰에서는 관리 부재(구역 상자·그 층 슬래브)가 숨겨져 있고 (FourD.EnsurePreviewView),
//   이 서버가 그 부재의 실제 형상(면 삼각분할, 세션 시작 때 한 번)을 날짜별 상태 색으로 그린다. 프레임마다 바뀌는 것은 묶음별 색·보임뿐.
//   그리기 버퍼(GPU)는 RenderScene 안에서 처음 쓸 때 한 번 만들고, 색은 EffectInstance 로만 바꾼다 (버퍼를 다시 만들지 않음).
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;

namespace RevitOnSite
{
    public sealed class FourDGraphics : IDirectContext3DServer
    {
        static readonly Guid ServerId = new("3C1B8E47-6D2A-4F59-9B0E-7A41C5D2E8F3");
        public static readonly FourDGraphics Instance = new();
        static bool registered;

        /// <summary>API 문맥에서 한 번: DirectContext3D 서비스에 이 서버를 넣고 켠다</summary>
        public static void Register()
        {
            if (registered) return;
            var svc = (MultiServerService)ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService);
            svc.AddServer(Instance);
            var ids = svc.GetActiveServerIds();
            if (!ids.Contains(ServerId)) { ids.Add(ServerId); svc.SetActiveServers(ids); }
            registered = true;
        }

        /// <summary>
        /// API 문맥에서: 서버를 끄고 빼고 GPU 버퍼·효과를 놓는다. 미리보기 문서를 닫을 때, 미리보기 뷰가 지워질 때, Revit 을 끌 때, 4D 미리보기를 끈 설정일 때.
        /// 여러 번 불러도 됨. 다음 ▶ 에서 Register·Load 로 다시 시작
        /// </summary>
        public static void Unregister()
        {
            Instance.Release();
            if (!registered) return;
            registered = false;
            try
            {
                var svc = (MultiServerService)ExternalServiceRegistry.GetService(ExternalServices.BuiltInExternalServices.DirectContext3DService);
                var ids = svc.GetActiveServerIds();
                if (ids.Remove(ServerId)) svc.SetActiveServers(ids);
                if (svc.IsRegisteredServerId(ServerId)) svc.RemoveServer(ServerId);
            }
            catch (Exception ex) { Dashboard.Log("4D 그리기 서버 빼기 실패: " + ex.Message); }
        }

        void Release()
        {
            states = null;
            viewId = ElementId.InvalidElementId;
            OwnerKey = null;
            foreach (var c in meshes.Values.SelectMany(x => x)) { c.Vb?.Dispose(); c.Ib?.Dispose(); c.Vb = null; c.Ib = null; }
            meshes = new();
            foreach (var e in effects.Values) e.Dispose();
            effects.Clear();
            format?.Dispose();
            format = null;
            bounds = null;
            foreach (var l in outlines.Values) { l.Vb?.Dispose(); l.Ib?.Dispose(); }
            outlines.Clear();
            boxes.Clear();
            lineFx?.Dispose();
            lineFx = null;
            lineFormat?.Dispose();
            lineFormat = null;
            flash = null;
        }

        /// <summary>그리고 있는 미리보기의 문서 (Dashboard.Key) 와 뷰 — 닫힘·삭제 감지용</summary>
        public string OwnerKey { get; private set; }
        public ElementId ViewId => viewId;

        /// <summary>한 묶음의 삼각형: 꼭짓점마다 float 6개 (위치 xyz, 법선 xyz), 삼각형마다 꼭짓점 3개.
        /// 65 535 꼭짓점마다 나눔 (색인 버퍼가 16비트). 들고 있는 것은 float 목록, 버퍼에는 문서화된 스트림 API 로만 씀 (Buffers)</summary>
        sealed class Chunk
        {
            public readonly List<float> F = new();
            public VertexBuffer Vb;
            public IndexBuffer Ib;
            public int Verts => F.Count / 6;
            public int Tris => Verts / 3;
        }

        ElementId viewId = ElementId.InvalidElementId;
        Dictionary<string, List<Chunk>> meshes = new();
        Dictionary<string, FourD.State> states;
        Outline bounds;
        VertexFormat format;
        readonly Dictionary<(FourD.State, bool Lit), EffectInstance> effects = new();
        // V5 강조: 앞 프레임보다 단계가 바뀐 묶음의 상자 테두리(주황, 세 겹)를 FlashMs 동안만 그림 — 그 뒤 다시 그리기는 Timeline 이 요청
        public const int FlashMs = 500;
        sealed class Lines { public VertexBuffer Vb; public IndexBuffer Ib; public int Verts, Count; }
        readonly Dictionary<string, (XYZ Min, XYZ Max)> boxes = new();
        readonly Dictionary<string, Lines> outlines = new();
        HashSet<string> flash;
        DateTime flashUntil;
        VertexFormat lineFormat;
        EffectInstance lineFx;
        bool failed;   // RenderScene 에서 예외가 한 번이라도 나면 다음 세션(Load)까지 그리지 않음 — 예외가 Revit 그리기로 새면 Revit 이 멈출 수 있음

        // ── API 문맥에서 부름
        /// <summary>세션 시작: 묶음별 실제 형상을 삼각형으로 (이미 같은 부재로 만들었으면 다시 안 함)</summary>
        public void Load(FourD model, ElementId previewViewId)
        {
            Release();   // 앞 세션의 GPU 버퍼를 놓고 새로
            viewId = previewViewId;
            OwnerKey = Dashboard.Key(model.Doc);
            var mn = new double[] { double.MaxValue, double.MaxValue, double.MaxValue };
            var mx = new double[] { double.MinValue, double.MinValue, double.MinValue };
            var opt = new Options { DetailLevel = ViewDetailLevel.Medium, ComputeReferences = false };
            foreach (var g in model.Groups)
            {
                var chunks = new List<Chunk> { new() };
                if (g.Tris != null) foreach (var t in g.Tris) Tri(t[0], t[1], t[2], chunks);   // 측정 지점 표식 (부재 없음 — FourD.Markers)
                else
                    foreach (var id in g.Ids)
                        if (model.Doc.GetElement(id)?.get_Geometry(opt) is GeometryElement ge) Add(ge, chunks);
                if (chunks[0].Verts == 0) continue;
                meshes[g.Key] = chunks;
                double[] a = { double.MaxValue, double.MaxValue, double.MaxValue }, b = { double.MinValue, double.MinValue, double.MinValue };
                foreach (var c in chunks)
                    for (int i = 0; i < c.F.Count; i += 6)
                        for (int k = 0; k < 3; k++) { a[k] = Math.Min(a[k], c.F[i + k]); b[k] = Math.Max(b[k], c.F[i + k]); }
                boxes[g.Key] = (new XYZ(a[0], a[1], a[2]), new XYZ(b[0], b[1], b[2]));
                for (int k = 0; k < 3; k++) { mn[k] = Math.Min(mn[k], a[k]); mx[k] = Math.Max(mx[k], b[k]); }
            }
            bounds = mn[0] > mx[0] ? null : new Outline(new XYZ(mn[0], mn[1], mn[2]), new XYZ(mx[0], mx[1], mx[2]));
            states = null;
            failed = false;
        }

        static void Add(GeometryElement ge, List<Chunk> chunks)
        {
            foreach (var go in ge)
            {
                if (go is GeometryInstance gi) Add(gi.GetInstanceGeometry(), chunks);
                else if (go is Solid s && s.Faces.Size > 0) foreach (Face f in s.Faces) Add(f.Triangulate(), chunks);
                else if (go is Mesh m) Add(m, chunks);   // IFC 변환 DirectShape 등
            }
        }

        static void Add(Mesh m, List<Chunk> chunks)
        {
            if (m == null) return;
            for (int i = 0; i < m.NumTriangles; i++)
            {
                var t = m.get_Triangle(i);
                Tri(t.get_Vertex(0), t.get_Vertex(1), t.get_Vertex(2), chunks);
            }
        }

        static void Tri(XYZ a, XYZ b, XYZ c, List<Chunk> chunks)
        {
            var n = (b - a).CrossProduct(c - a);
            if (n.GetLength() < 1e-12) return;
            n = n.Normalize();
            var ch = chunks[^1];
            if (ch.Verts + 3 > 65535) chunks.Add(ch = new Chunk());
            foreach (var p in new[] { a, b, c })
            {
                ch.F.Add((float)p.X); ch.F.Add((float)p.Y); ch.F.Add((float)p.Z);
                ch.F.Add((float)n.X); ch.F.Add((float)n.Y); ch.F.Add((float)n.Z);
            }
        }

        /// <summary>프레임: 묶음별 모습만 바꿈 (다시 그리기는 호출자가 RefreshActiveView)</summary>
        public void SetStates(Dictionary<string, FourD.State> s) => states = s;

        /// <summary>프레임: 단계가 바뀐 묶음을 지금부터 FlashMs 동안 테두리로 강조</summary>
        public void SetFlash(HashSet<string> keys) { flash = keys; flashUntil = DateTime.UtcNow.AddMilliseconds(FlashMs); }

        /// <summary>멈춤: 그리지 않음</summary>
        public void Clear() => states = null;   // 형상·뷰는 그대로 (다시 ▶ 하면 바로 그림)

        public bool Active => states != null;

        // ── DirectContext3D 서버
        public Guid GetServerId() => ServerId;
        public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;
        public string GetName() => "Revit On-Site 강도 타임라인";
        public string GetVendorId() => "KHU.BuildUp2";
        public string GetDescription() => "날짜별 시공 상태를 화면에만 그립니다 (모델은 바꾸지 않음).";
        public string GetApplicationId() => "";
        public string GetSourceId() => "";
        public bool UsesHandles() => false;
        // 미리보기 뷰면 늘 true — 그릴 것(states)이 없으면 RenderScene 이 그냥 돌아간다. (사실적 표시에선 Revit 이 이 서버를 그리지 않음 → FourD.Drawable)
        public bool CanExecute(View view) => !failed && view != null && view.Id == viewId;
        public Outline GetBoundingBox(View view) => bounds;
        public bool UseInTransparentPass(View view) => true;

        // 62차 KBIM 성능: 프레임 코드는 ~75 ms 인데 다음 요청까지 2–5 s (Revit 이 미리보기 뷰를 다시 그리는 시간) → 그리기 시간·횟수를 대시보드 기록에 (Timeline.Frame 이 다음 프레임 때 꺼냄)
        static long rsTicks;
        static int rsCalls, rsFlush;
        internal static (int Calls, int Flush, double Ms) TakeRenderStats()
        {
            var r = (rsCalls, rsFlush, rsTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            (rsCalls, rsFlush, rsTicks) = (0, 0, 0);
            return r;
        }

        public void RenderScene(View view, DisplayStyle displayStyle)
        {
            var st = states;
            if (st == null || failed) return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            rsCalls++;
            try
            {
                format ??= new VertexFormat(VertexFormatBits.PositionNormal);
                bool transparentPass = DrawContext.IsTransparentPass();
                bool lit = displayStyle is DisplayStyle.Shading or DisplayStyle.ShadingWithEdges or DisplayStyle.Realistic or DisplayStyle.RealisticWithEdges
                    or DisplayStyle.Rendering or DisplayStyle.Textures;
                foreach (var (key, s) in st)
                {
                    if (!s.Visible || (s.Transparency > 0) != transparentPass || !meshes.TryGetValue(key, out var chunks)) continue;
                    var fx = Effect(s, lit);
                    foreach (var c in chunks)
                    {
                        if (c.Tris == 0) continue;
                        Buffers(c);
                        DrawContext.FlushBuffer(c.Vb, c.Verts, c.Ib, c.Tris * 3, format, fx, PrimitiveType.TriangleList, 0, c.Tris);
                        rsFlush++;
                        if (DrawContext.IsInterrupted()) return;   // 사용자가 화면을 움직이면 Revit 이 그리기를 끊음
                    }
                }
                if (!transparentPass && flash is { Count: > 0 } fl && DateTime.UtcNow < flashUntil)
                {
                    lineFormat ??= new VertexFormat(VertexFormatBits.PositionColored);
                    if (lineFx == null || !lineFx.IsValid()) lineFx = new EffectInstance(VertexFormatBits.PositionColored);
                    foreach (var key in fl)
                    {
                        if (!boxes.TryGetValue(key, out var bx)) continue;
                        if (!outlines.TryGetValue(key, out var l) || l.Vb == null || !l.Vb.IsValid() || !l.Ib.IsValid()) outlines[key] = l = Outline(bx.Min, bx.Max);
                        DrawContext.FlushBuffer(l.Vb, l.Verts, l.Ib, l.Count * IndexLine.GetSizeInShortInts(), lineFormat, lineFx, PrimitiveType.LineList, 0, l.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                // 예외를 Revit 그리기로 내보내지 않음: 기록하고 이 세션은 그리기를 끔 (다시 [4D 타임라인]·▶ 로 새 세션)
                failed = true;
                Dashboard.Log("4D 그리기 예외 — 그리기를 끔: " + ex);
            }
            finally { rsTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0; }
        }

        /// <summary>상자 a–b 의 모서리 12개를 세 겹(0·30·60 mm 바깥)으로 — 1 px 선이 굵게 보이게. 색 = 밝은 주황 (밝은 캔버스, 판정 색표와 안 겹침)</summary>
        static Lines Outline(XYZ a, XYZ b)
        {
            var pts = new List<XYZ>();
            var idx = new List<(int, int)>();
            foreach (double g in new[] { 0.0, 0.1, 0.2 })   // 피트
            {
                XYZ p = a - new XYZ(g, g, g), q = b + new XYZ(g, g, g);
                int s = pts.Count;
                for (int i = 0; i < 8; i++) pts.Add(new XYZ((i & 1) == 0 ? p.X : q.X, (i & 2) == 0 ? p.Y : q.Y, (i & 4) == 0 ? p.Z : q.Z));
                foreach (var (i, j) in new[] { (0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7) }) idx.Add((s + i, s + j));
            }
            var l = new Lines { Verts = pts.Count, Count = idx.Count };
            int vf = pts.Count * VertexPositionColored.GetSizeInFloats();
            l.Vb = new VertexBuffer(vf);
            l.Vb.Map(vf);
            var vs = l.Vb.GetVertexStreamPositionColored();
            var col = new ColorWithTransparency(255, 120, 0, 0);
            foreach (var p in pts) vs.AddVertex(new VertexPositionColored(p, col));
            l.Vb.Unmap();
            int ix = idx.Count * IndexLine.GetSizeInShortInts();
            l.Ib = new IndexBuffer(ix);
            l.Ib.Map(ix);
            var ls = l.Ib.GetIndexStreamLine();
            foreach (var (i, j) in idx) ls.AddLine(new IndexLine(i, j));
            l.Ib.Unmap();
            return l;
        }

        /// <summary>GPU 버퍼 (묶음마다 한 번): 문서화된 스트림 API 로만 씀 — 크기는 GetSizeInFloats / GetSizeInShortInts</summary>
        static void Buffers(Chunk c)
        {
            if (c.Vb != null && c.Vb.IsValid() && c.Ib != null && c.Ib.IsValid()) return;
            int vf = c.Verts * VertexPositionNormal.GetSizeInFloats();
            c.Vb = new VertexBuffer(vf);
            c.Vb.Map(vf);
            var vs = c.Vb.GetVertexStreamPositionNormal();
            for (int i = 0; i < c.F.Count; i += 6)
                vs.AddVertex(new VertexPositionNormal(new XYZ(c.F[i], c.F[i + 1], c.F[i + 2]), new XYZ(c.F[i + 3], c.F[i + 4], c.F[i + 5])));
            c.Vb.Unmap();
            int ix = c.Tris * IndexTriangle.GetSizeInShortInts();
            c.Ib = new IndexBuffer(ix);
            c.Ib.Map(ix);
            var ts = c.Ib.GetIndexStreamTriangle();
            for (int i = 0; i < c.Verts; i += 3) ts.AddTriangle(new IndexTriangle(i, i + 1, i + 2));   // 삼각형 i/3 = 꼭짓점 i, i+1, i+2
            c.Ib.Unmap();
        }

        /// <summary>
        /// 법선이 있는 정점 형식은 주변광·확산·반사·광택·발광색·투명도를 모두 지정해야 한다 (RevitAPI.xml EffectInstance).
        /// 6차 점검 ui7_03: 발광색을 빼서 조명이 없는 일관된 색상(FlatColors)·은선에서 전부 검게 나왔음 → 조명 없는 표시 방식은 발광 = 그 색,
        /// 음영·사실적 표시는 확산 = 그 색 + 발광 35 % (어두운 면도 색이 보이게)
        /// </summary>
        EffectInstance Effect(FourD.State s, bool lit)
        {
            var key = (s, lit);
            if (effects.TryGetValue(key, out var e) && e.IsValid()) return e;
            var col = new Color(s.R, s.G, s.B);
            e = new EffectInstance(VertexFormatBits.PositionNormal);
            e.SetColor(col);
            e.SetDiffuseColor(col);
            e.SetAmbientColor(col);
            e.SetSpecularColor(new Color(0, 0, 0));
            e.SetGlossiness(1);
            e.SetEmissiveColor(lit ? new Color((byte)(s.R * 0.35), (byte)(s.G * 0.35), (byte)(s.B * 0.35)) : col);
            e.SetTransparency(s.Transparency / 100.0);
            return effects[key] = e;
        }
    }
}

// 대시보드 도킹 창 (ui_spec §5) — 등록, 새로 고침, 창에서 온 요청 처리.
//   창은 모달이 아니므로 선택·확대·판정·알림처럼 Revit API 가 필요한 일은 전부 ExternalEvent 로 넘긴다 (Revit 이 한가할 때 API 문맥에서 실행).
//   자료 = 판정이 구역에 적어 둔 값 (구역 ID·층·상태·예측 해체일·판정·사유) → 창을 열거나 뷰를 바꿀 때 엔진을 다시 돌리지 않는다.
//   경고 = 존치 구역의 사유 + 마지막 일정 다시 계산의 경고 (문서별, Revit 을 끄면 사라짐)
//   한중 초기양생 경고 = 데이터 폴더 CSV 로 Engine.Evaluator.ColdChecks (마지막 판정일 09:00, 판정은 안 바꿈) → 구역 칸 툴팁·[판정 근거]
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using WColor = System.Windows.Media.Color;

namespace RevitOnSite
{
    public static class Dashboard
    {
        public static readonly DockablePaneId PaneId = new(new Guid("6E0F2A61-3B7C-4C0D-9E55-1A2B3C4D5E6F"));

        /// <summary>격자 한 칸의 구역</summary>
        public sealed class ZoneInfo
        {
            public ElementId Id;
            public string ZoneId, Label, Col, Decision, Reason;
            public List<string> Warnings = new();   // 한중 초기양생 경고 (KCS 14 20 40)
            public int Floor, Status, Due;
            public int? EvStage;   // 근거 원장 갖춘 단계 (데이터 폴더가 없으면 null)
            public string DueText => Due > 19000101 ? $"{Due / 10000}-{Due / 100 % 100:00}-{Due % 100:00}" : "";
            // 칸 툴팁: 판정(없으면 상태 이름) · 예측 해체일 · 근거 n/8 (웹 4D 칸 제목과 같음) + 사유 (설치 검측 "확인 필요: …" 도 여기 보임, 5차 점검 B3)
            public string Tip => ZoneId + " · " + (Decision.Length > 0 ? Decision : ZoneEvaluation.Styles.FirstOrDefault(s => s.Code == Status).Name ?? "미검측")
                + (DueText.Length > 0 ? " · 예측 해체일 " + DueText : "") + (EvStage is int k ? $" · 근거 {k}/8" : "") + (Reason.Length > 0 ? "\n" + Reason : "")
                + (Warnings.Count > 0 ? "\n경고 " + Warnings.Count + "건 (칸을 두 번 눌러 판정 근거에서 보기)" : "");
        }

        static DashboardPane pane;
        /// <summary>대시보드가 열려 있으면 그 기준일 (없으면 null)</summary>
        internal static DateTime? BaseDay => pane?.BaseDay;
        static ExternalEvent evt;
        static readonly Queue<(string Name, Action<UIApplication> Run, bool Quiet)> todo = new();
        static Document shown;

        internal static string Key(Document doc) => string.IsNullOrEmpty(doc.PathName) ? doc.Title : doc.PathName;

        /// <summary>OnStartup 에서: 창 등록(오른쪽, 처음엔 숨김) + 새로 고침·테마 이벤트</summary>
        public static void Register(UIControlledApplication app)
        {
            pane = new DashboardPane();
            pane.ApplyTheme(App.Dark);
            app.RegisterDockablePane(PaneId, App.TabName, new Provider());
            app.ThemeChanged += (s, e) => pane.ApplyTheme(App.Dark);
            app.ViewActivated += (s, e) =>
            {
                Ensure();
                // 재생 중에 다른 뷰로 가면 (4D 탭을 닫았을 때 포함) 재생을 멈춤 — 프레임이 4D 뷰를 다시 열지 않게 (6차 점검 ui7_06)
                if (playing && e.CurrentActiveView?.Name != FourD.PreviewViewName)
                {
                    pane?.StopPlayback("다른 뷰로 바뀌어 4D 재생을 멈췄습니다 — ▶ 를 누르면 그 날짜부터 다시.");
                    Timeline.Hide();
                }
                Timeline.CheckSwitch(s as UIApplication);   // 비상 정지(끄개)가 켜졌으면 남은 4D 그림을 지움 (6차 점검 ui7_08)
                Timeline.DocSwitched(e.Document);           // 86차: 다른 문서로 바뀌면 강도 타임라인 비움
                ZoneLabels.Update(e.Document);
                if (e.CurrentActiveView?.Name == ZoneEvaluation.StatusViewName) Request("구역 강조", ZoneFocus.Highlight);   // 검측 뷰로 오면 선택 강조 덧그림 (트랜잭션 없음)
                if (pane.IsVisible) Refresh(e.Document);
            };
            app.ControlledApplication.DocumentChanged += (s, e) => { Timeline.Changed(e); ZoneLabels.Update(e.GetDocument()); if (pane.IsVisible && e.GetDocument().Equals(shown)) Refresh(shown); };
            app.ControlledApplication.DocumentClosing += (s, e) =>
            {
                Timeline.Closing(e.Document);
                if (e.Document.Equals(shown)) Clear();
                ZoneLabels.Forget(e.Document);   // 그 문서의 덧그림·GPU 버퍼 (코드 검토 2차)
                if (FieldInput.Running && FieldInput.Doc != null && FieldInput.Doc.Equals(e.Document)) FieldInputWindow.CloseOpen();   // 모델 반영할 문서가 없어짐 → 입력 서버도 끔 (창을 닫으면 끔)
            };
            // 동바리 상태 모드의 회색 판정 색(뷰 필터 재지정)이 .rvt 에 저장되지 않게 — 저장 직전 원래 색 (다시 흐리게는 다음 갱신 때, 저장 뒤 '수정됨'이 되지 않게)
            app.ControlledApplication.DocumentSaving += (s, e) => ZoneFocus.BeforeSave(e.Document);
            app.ControlledApplication.DocumentSavingAs += (s, e) => ZoneFocus.BeforeSave(e.Document);
            app.ApplicationClosing += (s, e) => Closing();   // Revit 이 그래픽·창을 정리하기 전에 (OnShutdown 에서도 한 번 더 — 두 번째는 아무 일 없음)
            try { Ensure(); } catch (Exception) { }   // 시작 때 못 만들면 첫 뷰 활성화(API 문맥)에서 만든다
        }

        /// <summary>API 문맥(명령·Revit 이벤트) 안에서만 부를 것</summary>
        internal static void Ensure() => evt ??= ExternalEvent.Create(new Handler());

        class Provider : IDockablePaneProvider
        {
            public void SetupDockablePane(DockablePaneProviderData data)
            {
                data.FrameworkElement = pane;
                data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right, MinimumWidth = 280 };
                data.VisibleByDefault = false;
            }
        }

        class Handler : IExternalEventHandler
        {
            public void Execute(UIApplication app)
            {
                while (todo.Count > 0)
                {
                    var (name, a, quiet) = todo.Dequeue();
                    if (!quiet) Log("실행 시작: " + name + " (활성 문서 " + (app.ActiveUIDocument?.Document?.Title ?? "없음") + ")");
                    try { a(app); if (!quiet) Log("실행 끝: " + name); }
                    catch (Exception ex) { Log("실행 예외: " + name + " — " + ex); App.Message("대시보드 요청을 처리하지 못했습니다.", ex.Message, TaskDialogIcon.TaskDialogIconWarning); }
                }
            }
            public string GetName() => "Revit On-Site 대시보드";
        }

        // 진단 기록: 모델리스 창 요청이 실행됐는지 사용자 화면 없이 확인 (4차 재점검: [판정] 무반응)
        // shortcut: %TEMP% 텍스트 기록. 원인이 밝혀지면 줄이거나 지울 것
        /// <summary>Revit 종료 정리 (ApplicationClosing, N12 점검): 4D 재생 타이머 → 결과 창 → 4D 그리기 서버 → 구역 글자 덧그림 서버 순서.
        /// 타이머를 먼저 멈춰 종료 중에 ExternalEvent 요청이 새로 생기지 않게 한다. 단계마다 기록 (다음에 종료가 이상하면 어디까지 왔는지 봄)</summary>
        internal static void Closing()
        {
            void Step(string name, Action a)
            {
                try { a(); Log("종료 정리: " + name); }
                catch (Exception ex) { Log("종료 정리 실패: " + name + " — " + ex.Message); }
            }
            Step("4D 재생 멈춤", () => { TimelinePlaying(false); pane?.StopPlay(); });
            Step("동바리 상태 깜빡임 멈춤", ZoneFocus.StopBlink);
            Step("결과 창 닫음", ResultWindow.CloseIfOpen);
            Step("4D 그리기 서버 뺌", () => Timeline.Shutdown());
            Step("구역 글자 서버 뺌", ZoneLabels.Unregister);
        }

        /// <summary>OnShutdown: Closing 을 한 번 더 (ApplicationClosing 이 안 온 경우) + ExternalEvent 놓기</summary>
        internal static void Shutdown()
        {
            Closing();
            try { evt?.Dispose(); evt = null; Log("종료 정리: 요청 이벤트 놓음"); }
            catch (Exception ex) { Log("종료 정리 실패: 요청 이벤트 — " + ex.Message); }
        }

        internal static void Log(string s)
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RevitOnSite_dashboard.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + Environment.NewLine); }
            catch { }
        }

        /// <summary>quiet = 로그 안 남김 (V2 깜빡임 프레임처럼 자주 오는 요청)</summary>
        internal static void Request(string name, Action<UIApplication> a, bool quiet = false)
        {
            todo.Enqueue((name, a, quiet));
            if (evt == null)
            {
                Log("요청 " + name + ": ExternalEvent 없음 — 실행 못 함");
                if (!quiet) System.Windows.MessageBox.Show("대시보드를 아직 준비하지 못했습니다. 리본의 [대시보드] 버튼을 한 번 눌렀다가 다시 시도하세요.", App.TabName);   // 깜빡임 같은 잦은 요청은 창 없이 (코드 검토 2차)
                return;
            }
            var r = evt.Raise();
            if (!quiet) Log("요청 " + name + ": Raise = " + r + ", 대기 " + todo.Count);
        }

        // ── 창에서 오는 요청
        internal static void RequestRefresh() => Request("새로 고침", app => Refresh(app.ActiveUIDocument?.Document));

        internal static void SelectZone(ZoneInfo z) => Request("구역 선택 " + z.ZoneId, app =>
        {
            var ui = app.ActiveUIDocument;
            var el = ui?.Document.GetElement(z.Id);
            if (el == null || el.LookupParameter("SHR_ZoneID")?.AsString() != z.ZoneId)
            {
                App.Message("이 구역을 활성 모델에서 찾지 못했습니다.", "대시보드가 보여 주는 프로젝트의 창으로 바꾼 뒤 다시 누르세요.");
                return;
            }
            // 검측 3D 뷰에서 고르고 확대 (5차 점검 B2: ShowElements 가 '남쪽' 입면으로 넘어가던 문제). 뷰가 없으면 만든다 — 뷰 전환은 트랜잭션 밖에서
            var doc = ui.Document;
            var view = StatusViewOf(doc);
            if (view == null)
            {
                if (!App.Write(doc, "검측 뷰 준비", () => { var v = ZoneEvaluation.StatusView(doc); ZoneEvaluation.ApplyFilters(doc, v); }, out var err))
                {
                    App.WriteFailed("검측 뷰 준비", err);
                    return;
                }
                view = StatusViewOf(doc);
            }
            if (ui.ActiveView.Id != view.Id) ui.ActiveView = view;
            var ids = new List<ElementId> { z.Id };
            ui.Selection.SetElementIds(ids);
            // ShowElements 는 보이는 다른 뷰를 찾아 넘어갈 수 있어, 이 뷰 안에서 구역 상자 둘레(가로 약 5 m, 높이 약 3 m 여유)로 확대만 한다
            var bb = el.get_BoundingBox(null);
            var uiv = ui.GetOpenUIViews().FirstOrDefault(v => v.ViewId == view.Id);
            if (bb != null && uiv != null)
            {
                var pad = new XYZ(16, 16, 10);   // ft
                uiv.ZoomAndCenterRectangle(bb.Min - pad, bb.Max + pad);
            }
            ZoneFocus.Highlight(app);   // 선택 구역 강조 덧그림 (트랜잭션 없음, 선택 이벤트가 API 선택에도 오는지와 상관없이)
        });

        static View3D StatusViewOf(Document doc) => new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
            .FirstOrDefault(x => !x.IsTemplate && x.Name == ZoneEvaluation.StatusViewName);

        internal static void ShowReason(ZoneInfo z) => Request("판정 근거 " + z.ZoneId, app => ReasonNow(app, z));

        /// <summary>대시보드 칸 우클릭 [조기 탈형 검토서] — 그 구역 한 장</summary>
        /// <summary>칸 우클릭 [동바리 점검 열기]: [현장 입력]과 같은 QR (서버 연동 또는 Wi-Fi) — 휴대폰 [동바리 점검]에서 이 구역을 고름 (60차 메모 4)</summary>
        internal static void ShoreInspect(ZoneInfo z) => Request("동바리 점검 " + z.ZoneId, app => { Log($"동바리 점검 열기: {z.ZoneId} — 휴대폰 QR"); FieldInputCommand.Open(app); });
        internal static void EarlyForm(ZoneInfo z) => Request("조기 탈형 검토서 " + z.ZoneId, app => EarlyFormReviewCommand.Make(app, new[] { z.ZoneId }));

        /// <summary>API 문맥: 판정 근거 창 (대시보드 칸 두 번 누름·우클릭, 리본·상황별 탭 [판정 근거] — 모두 이 창). 34차 사용자 요청: 비교 줄만, 출처·조문 문구 없음
        /// (기준값·출처는 [프로젝트 설정] › 적용 기준 한 곳). 아래 [조기 탈형 검토서] 로 그 구역 검토서 초안 (33차)</summary>
        internal static void ReasonNow(UIApplication app, ZoneInfo z)
        {
            var doc = app.ActiveUIDocument?.Document;
            string tag = "";
            var lines = z.Decision.Length > 0 ? CompareLines(doc, z.ZoneId, out tag) : new List<string>();
            var warns = ReasonWarnings(doc, z.ZoneId);   // 35차: 리본·상황별 탭에서 열면 z.Warnings 가 비어 있었음 → 여기서 다시 읽음
            // 66차 (63차 점검 ②): 사람 기록 상태(6 감리 승인 기록 · 7 해체 완료 · 8 재받침) 또는 근거 원장 7·8 이면 제목 = 그 상태 — 판정 글('해체 승인 요청 가능')이 해체 뒤에도 제목에 남던 것
            string recorded = z.Status is 6 or 7 or 8 ? ZoneEvaluation.Styles.First(x => x.Code == z.Status).Name : LedgerStage(doc, z.ZoneId) >= 7 ? "해체 완료" : null;
            var d = new TaskDialog(App.TabName)
            {
                TitleAutoPrefix = false, MainInstruction = $"{z.ZoneId} 검측 결과 — {(recorded != null ? recorded + " (기록)" : z.Decision.Length > 0 ? z.Decision : "판정 전")}",
                MainContent = string.Join("\n", lines) + PanelStageText(doc, z.ZoneId)
                    + (z.Decision.Length > 0 && z.DueText.Length > 0 && !z.Decision.StartsWith("해체 승인 요청 가능") ? $"\n예측 해체 가능일 {z.DueText} (참고){tag}" : "")
                    + (z.Decision.Length == 0 ? PourGateText(doc, z.ZoneId) : "")
                    + (warns.Count > 0 ? "\n\n" + string.Join("\n", warns.Select(w => "⚠ " + w)) : ""),
                FooterText = "기준값·단위·출처: [프로젝트 설정] › 적용 기준",
                CommonButtons = TaskDialogCommonButtons.Close, DefaultButton = TaskDialogResult.Close,
            };
            if (z.Decision.Length > 0)
                d.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "조기 탈형 검토서 만들기", "이 구역 판넬 조기 탈형(필러 존치) 구조 검토서 초안 — 판넬 4조건·판넬 탈형 가능일(예측), 서명란 빈칸 (초안이며 해체 지시 아님)");
            if (d.Show() == TaskDialogResult.CommandLink1) EarlyFormReviewCommand.Make(app, new[] { z.ZoneId });
        }

        /// <summary>판정 근거 창 맨 아래 ⚠ 줄 (35차): 대시보드 칸 경고와 같은 묶음(한중 초기 동해·공시체–센서 교차·입력 점검·무단 해체 관측)
        /// + 근거 원장 경고(순서 누락·서명 전 해체·해체 후 점검 미완 등) + 타설 입회 — 판정은 그대로, 구역 ID 머리는 뗌</summary>
        static List<string> ReasonWarnings(Document doc, string zid)
        {
            var outp = new List<string>();
            if (doc == null) return outp;
            string Strip(string w) => w.StartsWith(zid + ": ", StringComparison.Ordinal) ? w[(zid.Length + 2)..] : w;
            var last = ZoneEvaluation.LastEvalAt(doc);
            outp.AddRange((ColdWarnings(doc, last?.Date).GetValueOrDefault(zid) ?? new List<string>()).Select(Strip));
            var folder = ProjectSettings.Read(doc).CsvFolder;
            if (string.IsNullOrWhiteSpace(folder) || !System.IO.Directory.Exists(folder)) return outp;
            try
            {
                if (Engine.Evidence.LedgerAt(folder, DateOnly.FromDateTime(last ?? DateTime.Today)).TryGetValue(zid, out var l)) outp.AddRange(l.Warnings.Select(Strip));
                if (Engine.Quality.WatchMissing(folder) is { } m && m.TryGetValue(zid, out var t)) outp.Add("타설 입회: " + t);
            }
            catch (Exception ex) { outp.Add("경고를 읽지 못함 — " + ex.Message); }
            return outp.Distinct().ToList();
        }

        /// <summary>마지막 판정일(없으면 오늘)까지 그 구역 근거 원장의 연속 단계 n (0 = 없음·읽기 오류)</summary>
        static int LedgerStage(Document doc, string zid)
        {
            var folder = doc == null ? null : ProjectSettings.Read(doc).CsvFolder;
            if (string.IsNullOrWhiteSpace(folder) || !System.IO.Directory.Exists(folder)) return 0;
            try { return Engine.Evidence.LedgerAt(folder, DateOnly.FromDateTime(ZoneEvaluation.LastEvalAt(doc) ?? DateTime.Today)).TryGetValue(zid, out var l) ? l.Stage : 0; }
            catch (Exception ex) { Log("근거 원장 단계 " + zid + " 건너뜀: " + ex.Message); return 0; }
        }

        /// <summary>구역 요소 → 대시보드 칸 정보 (리본 [판정 근거] 가 고른 구역으로 창을 열 때)</summary>
        internal static ZoneInfo ReadZone(Element e) => Read(e);

        /// <summary>판정 근거 창 비교 줄 (34차 A): 측정값 ≥ 기준 ✓/✕ — 마지막 판정 시각의 Engine.Evaluator 행·GateDetails (판정과 같은 계산·같은 캐시).
        /// 공시체 없으면 측정 칸 비우고 '예상 … (양생 추정, 참고)', 센서 줄은 센서 추정일 때만 (기상 예측은 참고).
        /// tag = 추정에 층 높이 보정을 썼으면 " · 층 높이 보정 −x °C [가정]" (66차, 63차 점검 ①) — 추정(참고) 줄 끝에만 붙임, 실측 비교 줄은 그대로</summary>
        static List<string> CompareLines(Document doc, string zid, out string tag)
        {
            tag = "";
            var outp = new List<string>();
            if (doc == null || ZoneEvaluation.LastEvalAt(doc) is not DateTime at) return outp;
            var s = ProjectSettings.Read(doc);
            if (string.IsNullOrWhiteSpace(s.CsvFolder) || !System.IO.File.Exists(System.IO.Path.Combine(s.CsvFolder, "pours.csv"))) return outp;
            try
            {
                var row = Engine.Evaluator.Evaluate(s.CsvFolder, at, s.CureMin).FirstOrDefault(r => r.ZoneId == zid);
                if (row == null) { outp.Add("타설 전 — 비교할 측정값 없음"); return outp; }
                tag = System.Text.RegularExpressions.Regex.Match(row.EstSource, @" · 층 높이 보정 [+-]\d+\.\d{2} °C \[가정\]").Value;
                static string Ok(bool b) => b ? "✓" : "✕";
                static string V(double v, double lim) => v.ToString(v < lim && Math.Round(v, 1) >= lim ? "0.00" : "0.0", System.Globalization.CultureInfo.InvariantCulture);   // 반올림으로 '24.0 ≥ 24.0 ✕' 가 안 되게
                string L(double lim) => lim.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                double thr = row.Threshold;
                outp.Add(row.FCyl is double fc ? $"공시체 {V(fc, thr)} MPa ≥ {L(thr)} MPa {Ok(fc >= thr)}"
                    : $"공시체 ( 시험 없음 ) ≥ {L(thr)} MPa — 예상 {V(row.FEstMin, thr)} MPa (양생 추정, 참고){tag}");
                bool sensor = !row.EstSource.StartsWith("기상 예측", StringComparison.Ordinal) && !row.EstSource.Contains("참고");
                if (sensor) outp.Add($"센서 구역 최소 {V(row.FEstMin, thr)} MPa ≥ {L(thr)} MPa {Ok(row.FEstMin >= thr)}");
                if (Engine.Evaluator.ScopeNote(row.Reason) is { Length: > 0 } scope) outp.Add(scope + " ([프로젝트 설정] 기준은 데이터 폴더 options.json)");   // v6.2 W11 결론 그대로
                if (Engine.Evaluator.GateDetails(s.CsvFolder, at, s.CureMin).TryGetValue(zid, out var g))
                {
                    int need = Engine.ShoringEngine.MinLevels;
                    outp.Add(g.Topped && g.Above < need ? $"존치 층수: 최상층({Engine.Evaluator.TopOption(s.CsvFolder)}층)까지 타설 ✓ [가정]" : $"존치 층수 (위로 타설된 층) {g.Above} ≥ {need} {Ok(g.Above >= need)}");
                    if (g.Topped && g.Above < need && Engine.Evaluator.TopAuto(s.CsvFolder))   // 총괄 83차: 사람이 확인 안 한 자동 최상층 — 결론은 그대로
                        outp.Add("⚠ " + Engine.Evaluator.TopAutoTag + " ([프로젝트 설정] › 공정 › 최상층)");
                    foreach (var (_, sz, mpa) in g.Supports) outp.Add($"지지층 {sz} {V(mpa, g.Lim ?? 14)} MPa ≥ {L(g.Lim ?? 14)} MPa {Ok(mpa >= (g.Lim ?? 14))}");
                    if (g.Ratio is double r) outp.Add($"하중비 {r.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} ≤ 1.00 {Ok(r <= 1)} ({Engine.Evaluator.LiveNote(s.CsvFolder)} · {Engine.Evaluator.SupportNote(s.CsvFolder)})");   // W12 · 11-11 G1
                }
            }
            catch (Exception ex) { outp.Add("비교 값을 읽지 못함 — " + ex.Message); }
            return outp;
        }

        /// <summary>단계 해체 (S2, Engine.Staged = staged.py stages): 판넬 탈형(필러 존치) 4조건 ①–④ 와 판넬 탈형 가능일(예측) 한 줄 — 판정표에는 안 넣음.
        /// 마지막 판정 시각·같은 입력이면 다시 계산하지 않음</summary>
        static readonly Dictionary<string, string> panelCache = new();
        static string PanelStageText(Document doc, string zoneId)
        {
            if (doc == null || ZoneEvaluation.LastEvalAt(doc) is not DateTime at) return "";
            var s = ProjectSettings.Read(doc);
            var folder = s.CsvFolder;
            if (string.IsNullOrWhiteSpace(folder) || !System.IO.File.Exists(System.IO.Path.Combine(folder, "pours.csv"))) return "";
            var key = folder + "|" + zoneId + "|" + at.ToString("s") + "|" + s.CureMin + string.Concat(new[] { "zones.csv", "pours.csv", "sensors.csv", "weather.csv", "cylinders.csv", "sensor_strength.csv", Engine.Evidence.Ledger }
                .Select(n => "|" + System.IO.File.GetLastWriteTimeUtc(System.IO.Path.Combine(folder, n)).Ticks));
            if (!panelCache.TryGetValue(key, out var text))
            {
                try
                {
                    var st = Engine.Staged.Stages(folder, at, s.CureMin, zoneId);
                    text = st.TryGetValue(zoneId, out var v)   // 34차 A: 비교 줄과 같은 꼴 — ①–④ 와 결론만
                        ? $"\n판넬 단계 {string.Join(" ", v.Checks.Select(c => c.Name.Substring(0, 1) + (c.Ok ? "✓" : "✕")))} → {v.PanelDecision}"
                          + (v.PanelDecision != Engine.Staged.PanelOk && v.EarliestPanelDate is { } d ? $" (가능일 예측 {d:MM-dd}, 참고)" : "")
                        : "";
                }
                catch (Exception ex) { text = "\n판넬 단계: 읽지 못함 — " + ex.Message; }
                if (panelCache.Count > 64) panelCache.Clear();
                panelCache[key] = text;
            }
            return text;
        }

        /// <summary>구역 상세의 "타설 전 확인" 칸 (Engine.PourGate = pour_gate.py): 이 구역을 타설하려 할 때 아래 3개층 겹치는 구역의 동바리 확인(shore_checks.csv)·해체 완료 기록.
        /// 기준 시각 = 마지막 판정일 09:00 (없으면 지금). 이미 타설된 구역은 '이미 타설됨'만. 결론은 "타설 요청 가능"/"타설 보류" — 타설을 지시하지 않음</summary>
        static string PourGateText(Document doc, string zoneId)
        {
            if (doc == null) return "";
            var folder = ProjectSettings.Read(doc).CsvFolder;
            if (string.IsNullOrWhiteSpace(folder) || !System.IO.File.Exists(System.IO.Path.Combine(folder, "zones.csv"))) return "";
            var at = ZoneEvaluation.LastEvalAt(doc) ?? DateTime.Now;
            try
            {
                if (ZoneEvaluation.SyncZonesCsv(doc, folder) is string sync) Log("타설 전 확인: " + sync);   // 아직 판정 전이라도 모델 구역을 알도록 (N5)
                // shortcut: 타설 여부는 날짜로만 (같은 날 타설 시각 전후는 구분 안 함)
                var pour = Engine.Evaluator.Read(folder, "pours.csv").LastOrDefault(r => r.GetValueOrDefault("zone_id") == zoneId)?.GetValueOrDefault("pour_date");
                if (DateOnly.TryParseExact(pour, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var pd) && pd <= DateOnly.FromDateTime(at))
                    return $"\n\n타설 전 확인: 이미 타설됨 ({pour})";
                var g = Engine.PourGate.Evaluate(folder, zoneId, at);
                var lines = g.Checks.Select(c => $"{(c.Ok ? "○" : "✕")} {c.ZoneId}: {c.Reason}").ToList();
                if (g.Checks.Count == 0 || g.Reason.Contains("아래 층 수")) lines.Add(g.Reason);
                return $"\n\n타설 전 확인 ({at:MM-dd HH:mm} 기준, 아래 {Engine.ShoringEngine.MinLevels}개층 동바리): {g.Decision}\n" + string.Join("\n", lines);
            }
            catch (Exception ex) { return "\n\n타설 전 확인: 읽지 못함 — " + ex.Message; }
        }

        // 구역 보기 (ZoneFocus): 동바리 표시 방식·같은 층만·전체 층
        internal static void ShoreMode(int i) => ZoneFocus.SetMode((ZoneFocus.ShoreMode)i);
        internal static void SameFloor() => Request("같은 층 보기", ZoneFocus.SameFloor);
        internal static void AllFloors() => Request("전체 층 보기", ZoneFocus.AllFloors);

        /// <summary>[판정]: 해체 판정 버튼과 같은 계산 (기준 시각 09:00 = 판정 창 기본값), 끝나면 새로 고침</summary>
        internal static void Judge(DateTime day) => Request("판정 " + day.ToString("yyyy-MM-dd"), app =>
        {
            EvaluateCommand.Run(app, day.Date.AddHours(9), applyColors: true);
            Refresh(app.ActiveUIDocument?.Document);
        });

        // ── 자료

        // ── 4D 타임라인 (Timeline.cs). 프레임 요청은 하나만 걸어 두고, 그동안 슬라이더가 더 움직이면 마지막 날짜만 다시 그림 (재생이 밀리지 않게)
        //    재생 상태 = playing 하나 + 세대 번호 gen (6차 점검 ui7_06: ❚❚·탭 닫기가 안 먹고 계속 재생) — 멈출 때 gen 을 올려 대기 중이던 그리기 요청을 버리고,
        //    재생 중 프레임은 4D 미리보기 뷰를 다시 열지 않는다 (사용자가 탭을 닫거나 다른 뷰로 가면 재생을 멈춤)
        static DateTime? want;
        static bool frameBusy, playing, activateNext;
        static int gen;
        internal static bool FrameBusy => frameBusy;
        internal static bool Playing => playing;

        /// <summary>▶ 또는 [4D 타임라인]: 세션 준비 → 막대에 기간을 넣고 (그 값 변경이 첫 프레임 요청), play 면 재생 시작</summary>
        internal static void TimelinePrepare(bool play) => Request("4D 준비", app => PrepareNow(app, play));

        /// <summary>API 문맥에서 바로 (TimelineCommand)</summary>
        internal static void PrepareNow(UIApplication app, bool play)
        {
            if (!Timeline.Begin(app) || Timeline.Range is not { } r) { pane?.StopPlayback(null); return; }   // 준비 실패면 ▶ 로 켠 재생 상태도 끔
            // 61차 (리허설 5단계: 11-05 에서 시작해 9F 까지 사건 15번): 시작 = 마지막 판정일 (없으면 오늘), 앞은 ◀·[처음]
            var cur = app.ActiveUIDocument?.Document is Document d && ZoneEvaluation.LastEvalAt(d) is DateTime le ? DateOnly.FromDateTime(le) : DateOnly.FromDateTime(DateTime.Today);
            activateNext = true;
            pane?.SetTimeline(r.Start, r.End, cur < r.Start ? r.Start : cur > r.End ? r.End : cur, play, Timeline.Events);
        }

        /// <summary>재생 시작·멈춤 (대시보드). 멈추면 대기 중인 그리기를 버림. 시작하면 다음 프레임이 4D 뷰를 연다</summary>
        internal static void TimelinePlaying(bool on)
        {
            playing = on;
            if (on) activateNext = true;
            else { gen++; want = null; }
        }

        /// <summary>손으로 날짜를 고름 (슬라이더) — 그 프레임은 4D 뷰를 연다</summary>
        internal static void TimelineShow() => activateNext = true;
        /// <summary>61차: [프로젝트 설정] 저장 뒤 (API 문맥) — 대시보드 새로 고침 + 4D 다시 만들어 지금 날짜 다시 그림 (양생 하한 15 → 5 ℃ 복구 뒤 '판넬 탈형' 줄이 남던 것)</summary>
        internal static void SettingsSaved(Document doc) { Timeline.DataChanged(); Refresh(doc); pane?.Redraw(); }

        static long raisedAt;
        /// <summary>62차: 마지막 4D 프레임의 요청 → 실행 시작 (Revit 이 앞 화면을 다시 그리느라 기다린 시간). 재생이 타설일 시간 걸음을 줄이는 데 씀</summary>
        internal static double LastWaitMs { get; private set; }

        internal static void TimelineWant(DateTime d)
        {
            want = d;
            if (frameBusy) return;
            frameBusy = true;
            int g = gen;
            raisedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            Request("4D " + d.ToString("yyyy-MM-dd HH:mm"), app => Draw(app, g));
        }

        static void Draw(UIApplication app, int g)
        {
            LastWaitMs = System.Diagnostics.Stopwatch.GetElapsedTime(raisedAt).TotalMilliseconds;
            var d = want;
            try
            {
                if (g != gen || d == null) return;   // 멈춘 뒤 남은 요청
                bool open = activateNext;
                activateNext = false;
                var r = Timeline.Frame(app, d.Value, open);
                if (r == Timeline.FrameResult.Failed) { want = null; pane?.StopPlayback(null); }   // 준비 실패(알림은 이미 띄움) → 재생 멈춤, 알림이 되풀이되지 않게
                else if (r == Timeline.FrameResult.ViewLeft) { want = null; Timeline.Hide(); pane?.StopPlayback("강도 타임라인 뷰가 닫혀 재생을 멈췄습니다 — ▶ 를 누르면 그 날짜부터 다시."); }
            }
            catch (Exception ex)   // 코드 검토 2차: 프레임 예외가 틱마다 오류 창을 띄웠음 → 재생을 멈추고 한 번만
            {
                want = null;
                Log("4D 프레임 예외 — 재생 멈춤: " + ex);
                pane?.StopPlayback("4D 를 그리지 못해 재생을 멈췄습니다 — 기록: %TEMP%\\RevitOnSite_dashboard.log");
            }
            finally
            {
                if (g == gen && want is { } w && w != d)   // 그리는 사이 더 움직였으면 마지막 날짜만 한 번 더
                {
                    raisedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                    Request("4D " + w.ToString("yyyy-MM-dd HH:mm"), a => Draw(a, g));
                }
                else frameBusy = false;
            }
        }

        /// <summary>세션을 버렸을 때 막대를 처음 상태로 (▶ 를 누르면 다시 준비). note = 막대 아래 안내 (끄개 등)</summary>
        internal static void TimelineReset(string note) { TimelinePlaying(false); pane?.ResetTimeline(note); }
        internal static void TimelineNote(string note) => pane?.TimelineNote(note);
        /// <summary>그날 위층 추정 강도·사건 한 줄 (FourD.DoingLine, 프레임마다)</summary>
        internal static void TimelineDoing(string line) => pane?.SetDoing(line);
        /// <summary>4D 모델을 다시 만든 뒤 (Timeline.Begin): 대시보드 사건 목록·강도 곡선을 새 값으로</summary>
        internal static void TimelineRebuilt() => pane?.ModelRebuilt();

        /// <summary>[색] 콤보를 '판정 상태'로 (동바리 상태 켜기 실패 — ZoneFocus.Fail)</summary>
        internal static void ResetColorMode() => pane?.ResetColorMode();

        /// <summary>❚❚ 등: restore 면 그림을 지우고 재생 전 뷰로</summary>
        internal static void TimelineStop(bool restore)
        {
            TimelinePlaying(false);
            Request("4D 멈춤", app => Timeline.Stop(app, restore));
        }

        static void Clear()
        {
            shown = null;
            pane?.Load(Array.Empty<string>(), Array.Empty<DashboardPane.FloorRow>(), 0, 0, 0, Array.Empty<string>(), "프로젝트 파일을 열면 층·구역 현황이 보입니다.");
            pane?.ShowChart(null, DateTime.Today);
        }

        static (string Key, Engine.StrengthTimelineResult Line) chartCache;

        /// <summary>86차: 대시보드 강도 그림 (4D 세션 없이) — 세션과 같은 계산 StrengthTimeline.Timeline(데이터 폴더, 양생 하한). 데이터 폴더 파일이 같으면 다시 계산 안 함. 못 하면 null (그림 비움)</summary>
        static Engine.StrengthTimelineResult ChartLine(Document doc)
        {
            try
            {
                var s = ProjectSettings.Read(doc);
                if (string.IsNullOrWhiteSpace(s.CsvFolder) || !System.IO.Directory.Exists(s.CsvFolder)) return null;
                var files = new System.IO.DirectoryInfo(s.CsvFolder).GetFiles().Where(f => f.Extension is ".csv" or ".json").ToList();
                string key = s.CsvFolder + "|" + s.CureMin + "|" + files.Count + "|" + (files.Count == 0 ? 0 : files.Max(f => f.LastWriteTimeUtc.Ticks));
                if (chartCache.Key != key) chartCache = (key, Engine.StrengthTimeline.Timeline(s.CsvFolder, cureMin: s.CureMin));
                return chartCache.Line;
            }
            catch (Exception ex)   // 그림만 못 그림 — 판정·격자는 그대로
            {
                Log("대시보드 강도 그림을 못 그림 (▶ 를 누르면 다시 준비): " + ex.Message);
                return null;
            }
        }

        /// <summary>API 문맥에서 호출. 문서의 구역 값으로 창을 다시 그림</summary>
        public static void Refresh(Document doc)
        {
            if (pane == null) return;
            if (doc == null || !doc.IsValidObject || doc.IsFamilyDocument) { Clear(); return; }
            shown = doc;
            var zones = ShoringModel.Zones(doc).Select(Read).ToList();
            int last = doc.ProjectInformation?.LookupParameter("PRJ_LastEvalDate") is Parameter lp && lp.HasValue ? lp.AsInteger() : 0;
            DateTime? lastEval = last > 19000101 && DateTime.TryParseExact(last.ToString(System.Globalization.CultureInfo.InvariantCulture), "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var le) ? le : null;
            var cold = ColdWarnings(doc, lastEval);
            var alerts = AlertList(doc, lastEval);
            var ev = EvidenceStages(doc, lastEval);
            foreach (var z in zones)
            {
                z.Warnings = cold.GetValueOrDefault(z.ZoneId) ?? new List<string>();
                z.EvStage = ev == null ? null : ev.GetValueOrDefault(z.ZoneId);
            }
            var cols = zones.Select(z => z.Col).Distinct()
                .OrderBy(c => c.TrimEnd("0123456789".ToCharArray()).Length).ThenBy(c => c.TrimEnd("0123456789".ToCharArray()), StringComparer.Ordinal)
                .ThenBy(c => int.TryParse(c.TrimStart("ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray()), out var n) ? n : 0).ToList();
            var rows = zones.GroupBy(z => z.Floor).OrderByDescending(g => g.Key)
                .Select(g => new DashboardPane.FloorRow(g.First().Label, cols.Select(c => Cell(g.FirstOrDefault(z => z.Col == c))).ToArray())).ToList();
            // 경고 목록 (ui_reference_research 7절 #6): 심각도 기호 + 순서 — ■ 무단 해체 의심 → ▲ 타설 보류 → ▲ 존치 → ● 그 밖(재계획·시험 필요·입회 등)
            var warns = alerts.Where(a => a.Kind == "무단 해체 의심").Select(a => "■ " + a.Text)
                .Concat(alerts.Where(a => a.Kind == "타설 보류").Select(a => "▲ " + a.Text))
                .Concat(zones.Where(z => z.Decision.StartsWith("존치")).OrderByDescending(z => z.Floor).ThenBy(z => z.ZoneId, StringComparer.Ordinal)
                    .Select(z => "▲ " + z.ZoneId + " 존치: " + z.Reason)).ToList();
            string info = zones.Count == 0 ? "구역이 없습니다. [구역·동바리 생성]을 먼저 누르세요."
                : zones.All(z => z.Decision.Length == 0) ? "아직 판정 전입니다. 기준일을 고르고 [판정]을 누르세요." : null;
            // 11-11 Revit #1: 칩 = 판정표와 같은 계산(사람 기록 6·7·8 우선, Approval.Counts) — 모델 값이 예전 판정으로 남아 있어도 요청서·패키지와 같은 수. 판정 전이면 모델 값
            var cs = ProjectSettings.Read(doc);
            var cnt = lastEval is DateTime evAt && !string.IsNullOrWhiteSpace(cs.CsvFolder) && System.IO.File.Exists(System.IO.Path.Combine(cs.CsvFolder, "pours.csv"))
                ? Engine.Approval.Counts(Engine.Evaluator.Evaluate(cs.CsvFolder, evAt, cs.CureMin), cs.CsvFolder, evAt) : ((int, int, int, int, int)?)null;
            var ov = cnt != null ? Engine.Approval.StatusOverride(cs.CsvFolder, lastEval) : null;
            int St(int code) => ov != null ? ov.Values.Count(v => v == code) : zones.Count(z => z.Status == code);
            pane.Load(cols, rows, cnt?.Item1 ?? zones.Count(z => z.Status == 5), cnt?.Item2 ?? zones.Count(z => z.Status is 2 or 3 or 9), cnt?.Item4 ?? zones.Count(z => z.Status == 4), warns, info,
                doc.ActiveView?.Name == FourD.PreviewViewName ? DashboardPane.TimelineMode : ZoneOverlay.ModeName(doc, doc.ActiveView), lastEval, St(6), St(7), St(8),   // 11-12 2-5: 뷰 표시 = 범례 기준 (활성 뷰 색)
                Engine.Alerts.Summary(alerts), alerts, KpiLines(doc, lastEval) is var kt ? kt.Kpi : null, kt.Trend, cnt?.Item3 ?? zones.Count(z => z.Status == 2));
            pane.ShowChart(zones.Count == 0 ? null : ChartLine(doc), lastEval ?? DateTime.Today);   // 86차: 처음 열 때·판정 뒤·문서 전환 뒤에도 그림 (세션이 있으면 세션 그림 그대로)
        }

        // 파장 지표 (Engine.Kpi = kpi.py) — 오늘 볼 것과 같은 기준 시각·같은 파일 묶음(+ 근거 원장), 파일이 같으면 다시 계산 안 함
        static (string Key, (List<string> Kpi, List<string> Trend) Lines) kpiCache;

        /// <summary>지표 다섯 줄 + 추이 두 줄 (Engine.KpiTrend = kpi_trend.py, 최근 5회 판정 — F13). 판정 전이면 (null, null)</summary>
        static (List<string> Kpi, List<string> Trend) KpiLines(Document doc, DateTime? lastEval)
        {
            var s = ProjectSettings.Read(doc);
            var folder = s.CsvFolder;
            if (lastEval == null || string.IsNullOrWhiteSpace(folder) || !System.IO.File.Exists(System.IO.Path.Combine(folder, "pours.csv"))) return (null, null);
            var at = lastEval.Value.Date.AddHours(9);
            var key = folder + "|" + at.ToString("s") + "|" + s.CureMin + string.Concat(new[] { "zones.csv", "pours.csv", "sensors.csv", "weather.csv", "cylinders.csv",
                "sensor_strength.csv", "approvals.csv", "shore_checks.csv", "evidence.csv", "after_check.csv", Engine.Alerts.EvalLog }.Select(n => "|" + System.IO.File.GetLastWriteTimeUtc(System.IO.Path.Combine(folder, n)).Ticks));
            if (kpiCache.Key == key) return kpiCache.Lines;
            List<string> lines, trend;
            try { lines = Engine.Kpi.Lines(Engine.Kpi.Of(folder, at, s.CureMin)); }
            catch (Exception ex) { Log("지표 건너뜀: " + ex.Message); lines = null; }
            try { trend = Engine.KpiTrend.Lines(Engine.KpiTrend.Trend(folder, last: 5, cureMin: s.CureMin)); }
            catch (Exception ex) { Log("지표 추이 건너뜀: " + ex.Message); trend = null; }
            kpiCache = (key, (lines, trend));
            return (lines, trend);
        }

        // 오늘 볼 것 (D4) — 마지막 판정일 09:00 기준, 데이터 폴더 eval_log.csv 에 다른 판정이 있으면 그 마지막 것 대비 '상태 바뀜'까지. 파일이 같으면 다시 계산 안 함
        static (string Key, List<Engine.Alert> List) alertCache;

        static List<Engine.Alert> AlertList(Document doc, DateTime? lastEval)
        {
            var s = ProjectSettings.Read(doc);
            var folder = s.CsvFolder;
            if (lastEval == null || string.IsNullOrWhiteSpace(folder) || !System.IO.File.Exists(System.IO.Path.Combine(folder, "pours.csv"))) return new();
            var at = lastEval.Value.Date.AddHours(9);
            var key = folder + "|" + at.ToString("s") + "|" + s.CureMin + string.Concat(new[] { "zones.csv", "pours.csv", "sensors.csv", "weather.csv", "cylinders.csv",
                "sensor_strength.csv", "approvals.csv", "shore_checks.csv", Engine.Alerts.EvalLog }.Select(n => "|" + System.IO.File.GetLastWriteTimeUtc(System.IO.Path.Combine(folder, n)).Ticks));
            if (alertCache.Key == key) return alertCache.List;
            List<Engine.Alert> list;
            try { list = Engine.Alerts.Of(folder, at, Engine.Alerts.PrevEval(folder, at), s.CureMin); }
            catch (Exception ex) { Log("오늘 볼 것 건너뜀: " + ex.Message); list = new(); }
            alertCache = (key, list);
            return list;
        }

        // 한중 경고는 CSV 를 읽으므로 폴더·판정일·파일 수정 시각이 같으면 다시 계산하지 않음 (새로 고침이 잦음)
        static (string Key, Dictionary<string, List<string>> Map) coldCache;

        static Dictionary<string, List<string>> ColdWarnings(Document doc, DateTime? lastEval)
        {
            var ps = ProjectSettings.Read(doc);
            var folder = ps.CsvFolder;
            if (lastEval == null || string.IsNullOrWhiteSpace(folder) || !System.IO.File.Exists(System.IO.Path.Combine(folder, "pours.csv"))) return new();
            var at = lastEval.Value.Date.AddHours(9);
            var key = folder + "|" + at.ToString("s") + "|" + ps.DefaultFck.ToString(System.Globalization.CultureInfo.InvariantCulture) + string.Concat(new[] { "zones.csv", "pours.csv", "sensors.csv", "weather.csv", "cylinders.csv", "sensor_strength.csv", "approvals.csv", "shore_checks.csv" }
                .Select(n => "|" + System.IO.File.GetLastWriteTimeUtc(System.IO.Path.Combine(folder, n)).Ticks));
            if (coldCache.Key == key) return coldCache.Map;
            var map = new Dictionary<string, List<string>>();
            try
            {
                foreach (var w in Engine.Evaluator.ColdChecks(folder, at))
                    (map.TryGetValue(w.ZoneId, out var l) ? l : map[w.ZoneId] = new List<string>()).Add(w.Text);
                foreach (var w in Engine.Evaluator.CrossChecks(folder, at))   // 공시체–센서 교차 검사 (판정은 그대로)
                    (map.TryGetValue(w.ZoneId, out var l) ? l : map[w.ZoneId] = new List<string>()).Add(w.Text);
                foreach (var w in Engine.Evaluator.InputChecks(folder, at, ps.DefaultFck))   // fck·공시체 편차 점검 (A-29, 판정은 그대로)
                    (map.TryGetValue(w.ZoneId, out var l) ? l : map[w.ZoneId] = new List<string>()).Add(w.Text);
                foreach (var (z, w) in Engine.Evidence.RemovalWatch(folder, DateOnly.FromDateTime(at)))   // 무단 해체 관측 (총괄 P1-2, 경고만)
                    (map.TryGetValue(z, out var l) ? l : map[z] = new List<string>()).Add(w);
            }
            catch (Exception ex) { Log("한중 경고 건너뜀: " + ex.Message); }
            coldCache = (key, map);
            return map;
        }

        // 근거 원장 단계 (칸 툴팁 "근거 n/8") — 구역 상세 줄과 같은 기준일(마지막 판정일, 없으면 오늘). 폴더·기준일·출처 파일 수정 시각이 같으면 다시 읽지 않음
        static (string Key, Dictionary<string, int> Map) evCache;

        static Dictionary<string, int> EvidenceStages(Document doc, DateTime? lastEval)
        {
            var folder = ProjectSettings.Read(doc).CsvFolder;
            if (string.IsNullOrWhiteSpace(folder) || !System.IO.Directory.Exists(folder)) return null;
            var day = DateOnly.FromDateTime(lastEval ?? DateTime.Today);
            var key = folder + "|" + day + string.Concat(new[] { Engine.Evidence.Ledger, "install_inspection.csv", "cylinders.csv", Engine.Approval.Log }
                .Select(n => "|" + System.IO.File.GetLastWriteTimeUtc(System.IO.Path.Combine(folder, n)).Ticks));
            if (evCache.Key == key) return evCache.Map;
            var map = new Dictionary<string, int>();
            try { foreach (var (z, l) in Engine.Evidence.LedgerAt(folder, day)) map[z] = l.Stage; }
            catch (Exception ex) { Log("근거 단계 건너뜀: " + ex.Message); }
            evCache = (key, map);
            return map;
        }

        static ZoneInfo Read(Element e)
        {
            string S(string n) => e.LookupParameter(n)?.AsString() ?? "";
            int I(string n, int none) => e.LookupParameter(n) is Parameter p && p.HasValue ? p.AsInteger() : none;
            var id = S("SHR_ZoneID");
            int dash = id.LastIndexOf('-');
            int floor = I("SHR_Floor", 0);
            return new ZoneInfo
            {
                Id = e.Id, ZoneId = id, Floor = floor,
                Label = dash > 0 ? id[..dash] : Engine.FloorSign.FloorName(floor, null), Col = dash > 0 ? id[(dash + 1)..] : id,
                Status = I("SHR_Status", 0), Due = I("SHR_DueDate", 0), Decision = S("SHR_Decision"), Reason = S("SHR_Reason"),
            };
        }

        static readonly Dictionary<int, (Brush Bg, Brush Fg)> brushes = ZoneEvaluation.Styles.ToDictionary(s => s.Code, s =>
        {
            var bg = new SolidColorBrush(WColor.FromRgb(s.R, s.G, s.B));
            bg.Freeze();
            return ((Brush)bg, 0.299 * s.R + 0.587 * s.G + 0.114 * s.B > 150 ? (Brush)Brushes.Black : Brushes.White);   // 글자 = 대비가 큰 쪽
        });

        /// <summary>칸 앞 상태 약자 — 색만으로 구분하지 않게 (색각 이상·흑백 인쇄, ui_reference_research 7절 #1). 지시가 아니라 상태 이름 줄임</summary>
        internal static readonly Dictionary<int, string> StatusShort = new()
        {
            [0] = "미검", [1] = "합격", [2] = "!결함", [3] = "양생", [4] = "공시", [5] = "요청", [6] = "감리", [7] = "완료", [8] = "재받침", [9] = "존치",
        };

        static DashboardPane.Cell Cell(ZoneInfo z)
        {
            if (z == null) return new DashboardPane.Cell("", Brushes.Transparent, Brushes.Transparent, null);
            var (bg, fg) = brushes.TryGetValue(z.Status, out var b) ? b : brushes[0];
            var tag = StatusShort.GetValueOrDefault(z.Status, "");
            var due = z.DueText.Length > 0 ? z.DueText[5..] : "";
            return new DashboardPane.Cell(tag, bg, fg, z, due);
        }
    }
}

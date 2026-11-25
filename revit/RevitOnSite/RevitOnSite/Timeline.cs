// 강도 예측 타임라인 (v6 3단계, CmdTimeline) — 대시보드 아래 막대(날짜 슬라이더 · ▶ · ❚❚ · 속도)가 ExternalEvent 로 부른다.
// 방식 (총괄 기준 1–3, 보고서 비교표): 실제 부재·검측 뷰는 바꾸지 않고, 전용 뷰 "Revit On-Site 강도 타임라인" 에서 DirectContext3D 로 화면에만 그린다.
//   실행 취소 목록: 전용 뷰를 처음 만들거나 새로 생긴 관리 부재를 그 뷰에서 숨겨야 할 때만 "Revit On-Site: 4D 표시" 1건 (보통 0건). 프레임은 0건.
//   [멈추면 원래 뷰로] 를 켜 두면 ❚❚·창 닫기 때 재생 전 뷰(검측 뷰 등)로 돌아가고 그림을 지운다 — 검측 뷰는 처음부터 손대지 않았으므로 그대로.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    static class Timeline
    {
        static FourD model;
        static bool dirty = true;
        static ElementId previous;   // 재생 전 활성 뷰

        /// <summary>모델이 바뀌면 (DocumentChanged) 다음 프레임에 일정·형상을 다시 만든다. 미리보기 뷰가 지워졌으면 그리기 서버를 뺀다</summary>
        internal static void Changed(Autodesk.Revit.DB.Events.DocumentChangedEventArgs e)
        {
            dirty = true;
            var g = FourDGraphics.Instance;
            if (g.OwnerKey != null && g.OwnerKey == Dashboard.Key(e.GetDocument()) && e.GetDeletedElementIds().Contains(g.ViewId)) Shutdown();
        }

        /// <summary>미리보기 문서를 닫을 때</summary>
        internal static void Closing(Document doc)
        {
            if (FourDGraphics.Instance.OwnerKey != null && FourDGraphics.Instance.OwnerKey == Dashboard.Key(doc)) Shutdown();
        }

        /// <summary>활성 문서가 세션 문서와 다르면 세션을 버림 (86차 — 85차 점검: IFC 문서로 바꿔도 대시보드에 check8 그림·그날 줄이 남았음). 같은 문서의 다른 뷰는 그대로</summary>
        internal static void DocSwitched(Document doc)
        {
            if (model != null && doc != null && Dashboard.Key(model.Doc) != Dashboard.Key(doc))
                Shutdown("다른 문서로 바뀌어 강도 타임라인을 비웠습니다 — ▶ 나 사건 단추를 누르면 이 문서로 다시 준비합니다.");
        }

        /// <summary>그리기 서버를 빼고 세션을 버림 (문서 닫기·미리보기 뷰 삭제·Revit 끄기·설정으로 끔). 다음 ▶ 에서 처음부터</summary>
        internal static void Shutdown(string note = null)
        {
            FourDGraphics.Unregister();
            model = null;
            last = null;
            dirty = true;
            previous = null;
            Dashboard.TimelineReset(note);
        }

        internal static (DateOnly Start, DateOnly End)? Range => model == null ? null : (model.Start, model.End);
        internal static SortedDictionary<DateOnly, List<string>> Events => model?.Events ?? new();
        /// <summary>층별 추정 곡선·공시체 점·기준선 (대시보드 그림)</summary>
        internal static Engine.StrengthTimelineResult Line => model?.Line;

        // V5 강조: 앞 프레임 모습과 비교해 단계가 바뀐 묶음만 FourDGraphics.FlashMs 동안 테두리 → 끝나면 한 번 더 그려 지움
        static Dictionary<string, FourD.State> last;
        static System.Windows.Threading.DispatcherTimer flashTimer;
        static void FlashEndLater()
        {
            if (flashTimer == null)
            {
                flashTimer = new() { Interval = TimeSpan.FromMilliseconds(FourDGraphics.FlashMs + 50) };
                flashTimer.Tick += (s, e) =>
                {
                    flashTimer.Stop();
                    Dashboard.Request("4D 강조 끝", a => { var ui = a.ActiveUIDocument; if (FourDGraphics.Instance.Active && ui?.ActiveView?.Id == FourDGraphics.Instance.ViewId) ui.RefreshActiveView(); });
                };
            }
            flashTimer.Stop();
            flashTimer.Start();
        }

        // 대시보드 [색: 근거 단계] — 미리보기에만 (날짜별 뷰 저장은 늘 판정 상태 색)
        static bool evColors;
        internal static void SetEvidenceColors(bool on) => evColors = on;
        /// <summary>데이터 폴더 CSV 가 바뀜 (휴대폰 저장) — 강도 곡선·사건을 다음 프레임에 다시 만듦 (모델이 안 바뀌어 DocumentChanged 가 안 옴, 34차)</summary>
        internal static void DataChanged() => dirty = true;
        /// <summary>다음 프레임에 4D 를 다시 만들 차례 (휴대폰 저장·모델 변경 뒤) — 대시보드 [사건 ▶] 가 옛 사건 목록으로 건너뛰지 않게 (35차)</summary>
        internal static bool Stale => dirty || model == null;
        /// <summary>
        /// 4D 미리보기 끄개 (배포 조건): 애드인 DLL 옆 onsite.settings.json 의 {"preview4d": false} 면 끔. 파일이 없으면 켜짐, 읽지 못하면 안전하게 끔.
        /// ▶·프레임마다 읽으므로 Revit 을 다시 켜지 않고 끌 수 있다
        /// </summary>
        static bool PreviewEnabled()
        {
            var p = System.IO.Path.Combine(App.Dir, "onsite.settings.json");
            if (!System.IO.File.Exists(p)) return true;
            try
            {
                using var j = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(p));
                return !(j.RootElement.TryGetProperty("preview4d", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.False);
            }
            catch (Exception ex) { Dashboard.Log("onsite.settings.json 읽기 실패 → 4D 미리보기 끔: " + ex.Message); return false; }
        }

        /// <summary>API 문맥에서. 세션 준비: 일정·판정 모델, 전용 뷰(필요할 때만 App.Write 1건), 형상 삼각분할. 실패하면 알림 후 false</summary>
        internal static bool Begin(UIApplication app)
        {
            if (!PreviewEnabled())
            {
                if (model != null || FourDGraphics.Instance.OwnerKey != null) Shutdown("4D 미리보기가 꺼져 있습니다 (onsite.settings.json)");
                else Dashboard.TimelineReset("4D 미리보기가 꺼져 있습니다 (onsite.settings.json)");
                return false;
            }
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument) { App.Message("프로젝트 파일을 연 상태에서 눌러 주세요."); return false; }
            if (model != null && !dirty && model.Doc.Equals(doc) && model.FindPreviewView() != null) return true;
            var sw = Stopwatch.StartNew();
            last = null;
            try { model = new FourD(doc); }
            catch (Exception ex) when (ex is InvalidOperationException or System.IO.InvalidDataException or FormatException)   // 설정·구역·타설 없음, CSV 날짜·숫자 오류
            { model = null; App.Message("강도 타임라인을 시작하지 못했습니다.", (ex is FormatException ? "데이터 폴더 CSV 의 날짜·숫자를 읽지 못했습니다: " : "") + ex.Message); return false; }
            long tPlan = sw.ElapsedMilliseconds;
            bool restyle = model.FindPreviewView() is View3D pv0 && !FourD.Drawable(pv0.DisplayStyle);
            if (model.PreviewNeedsWrite() && !App.Write(doc, "4D 표시", () => { model.EnsurePreviewView(); }, out var err))
            {
                model = null;
                App.WriteFailed("4D 표시", err);
                return false;
            }
            FourDGraphics.Register();
            FourDGraphics.Instance.Load(model, model.FindPreviewView().Id);
            dirty = false;   // 위 App.Write 로 생긴 DocumentChanged 는 무시
            Dashboard.TimelineRebuilt();   // 사건·강도 곡선을 새 모델로 (휴대폰 저장 뒤 다시 만든 경우, 34차)
            Dashboard.TimelineNote(restyle ? "사실적 표시에서는 4D 그림이 보이지 않아 미리보기 뷰를 음영으로 바꿨습니다." : null);
            Dashboard.Log($"4D 준비: 강도 예측·묶음 {tPlan} ms, 전체 {sw.ElapsedMilliseconds} ms · 묶음 {model.Groups.Count}개 · {model.Start:yyyy-MM-dd}~{model.End:yyyy-MM-dd}");
            return true;
        }

        /// <summary>뷰가 바뀔 때 (Dashboard.ViewActivated): 끄개(onsite.settings.json "preview4d": false)가 켜졌으면 그리기 서버를 빼고 화면을 다시 그림
        /// — 6차 점검 ui7_08: 비상 정지 뒤에도 ▶ 를 누르기 전까지 옛 그림이 남았음</summary>
        internal static void CheckSwitch(UIApplication app)
        {
            if ((model == null && FourDGraphics.Instance.OwnerKey == null) || PreviewEnabled()) return;
            Shutdown("4D 미리보기가 꺼져 있습니다 (onsite.settings.json)");
            app?.ActiveUIDocument?.RefreshActiveView();
        }

        /// <summary>재생 중 사용자가 4D 뷰를 떠났을 때: 그림만 지움 (형상·세션은 그대로, ▶ 하면 그 날짜부터 다시)</summary>
        internal static void Hide() => FourDGraphics.Instance.Clear();

        internal enum FrameResult { Drawn, Failed, ViewLeft }

        /// <summary>API 문맥에서 한 프레임: 날짜 D 의 모습을 그린다 (트랜잭션 없음). 시간은 대시보드 기록에.
        /// open = 전용 뷰가 활성이 아니면 연다 (▶·슬라이더를 처음 움직일 때만). 재생 중 프레임(open = false)은 사용자가 뷰를 떠났으면 그리지 않고 ViewLeft</summary>
        internal static FrameResult Frame(UIApplication app, DateTime d, bool open)
        {
            if (!Begin(app)) return FrameResult.Failed;
            var ui = app.ActiveUIDocument;
            var view = model.FindPreviewView();
            bool switchView = ui.ActiveView.Id != view.Id;
            if (switchView && !open) return FrameResult.ViewLeft;
            var sw = Stopwatch.StartNew();
            var rs = FourDGraphics.TakeRenderStats();   // 앞 프레임 뒤 Revit 다시 그리기 동안의 4D 덧그림 (62차 KBIM 성능)
            model.EvidenceColors = evColors;
            var st = model.States(d);
            long tState = sw.ElapsedMilliseconds;
            var changed = model.Changed(last, st);
            last = st;
            FourDGraphics.Instance.SetStates(st);   // 뷰를 열기 전에 (열자마자 그릴 것이 있게)
            FourDGraphics.Instance.SetFlash(changed);
            if (switchView)
            {
                previous ??= ui.ActiveView.Id;
                ui.ActiveView = view;
            }
            ui.RefreshActiveView();
            if (changed.Count > 0 && !Dashboard.Playing) FlashEndLater();   // 62차: 재생 중엔 다음 프레임이 강조를 지움 — '강조 끝' 다시 그리기가 KBIM 에서 프레임마다 2–5 s 를 한 번 더 썼음
            Dashboard.TimelineDoing(model.DoingLine(d));
            long t = sw.ElapsedMilliseconds;
            Dashboard.Log($"4D 프레임 {d:yyyy-MM-dd HH:mm}: 상태 {tState} ms + 다시 그리기 = {t} ms" + (t > 500 ? " (목표 0.5 s 넘음)" : "")
                + $" · 요청→실행 대기 {Dashboard.LastWaitMs:0} ms · 앞 화면 4D 덧그림 RenderScene {rs.Calls}번 · FlushBuffer {rs.Flush} · {rs.Ms:0} ms");
            return FrameResult.Drawn;
        }

        /// <summary>API 문맥에서: restore 면 그림을 지우고 재생 전 뷰로, 아니면 그 날짜 모습을 그대로 둠</summary>
        internal static void Stop(UIApplication app, bool restore)
        {
            if (!restore) return;
            FourDGraphics.Instance.Clear();
            last = null;
            var ui = app.ActiveUIDocument;
            if (restore && previous != null && ui?.Document.GetElement(previous) is View v && !v.IsTemplate) ui.ActiveView = v;
            previous = null;
            ui?.RefreshActiveView();
        }
    }
}

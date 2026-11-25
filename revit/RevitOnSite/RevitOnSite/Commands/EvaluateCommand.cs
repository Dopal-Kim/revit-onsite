// [판정] 해체 판정 — 창 = Wpf\EvaluateWindow, 계산 = RevitOnSite.Engine.Evaluator (Python 일치 시험 통과), 기입·색 = ZoneEvaluation
// Run = 창 없이 같은 판정 (대시보드 [판정] 이 ExternalEvent 안에서 부름)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    [Transaction(TransactionMode.Manual)]
    public class EvaluateCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            var s = ProjectSettings.Read(doc);
            s.EnsureFolder();   // 87차: 기본 폴더가 없으면 만듦 (구역 목록 zones.csv 를 거기에)
            if (!HasPours(s, ZoneEvaluation.SyncZonesCsv(doc, s.CsvFolder))) return Result.Cancelled;   // 구역 더하기를 먼저 (N13: 새 모델은 구역 목록이 있어야 타설 기록을 만듦)
            var w = new EvaluateWindow(s, Dashboard.BaseDay ?? ZoneEvaluation.LastEvalAt(doc)?.Date);   // 기본값 = 대시보드 기준일 (없으면 마지막 판정일, 그것도 없으면 오늘)
            if (!App.ShowDialog(w, data.Application)) return Result.Cancelled;
            return Run(data.Application, w.At, w.ApplyColors);
        }

        /// <param name="sync">방금 SyncZonesCsv 가 돌려준 문구 (구역을 썼으면 "zones.csv 에 모델 구역 n개를 더했습니다 …") — 타설 기록이 없을 때 다음 할 일 안내에 씀</param>
        internal static bool HasPours(ProjectSettings s, string sync = null)
        {
            if (!string.IsNullOrEmpty(s.CsvFolder) && File.Exists(Path.Combine(s.CsvFolder, "pours.csv"))) return true;
            bool wrote = sync?.StartsWith("zones.csv 에 모델 구역") == true;
            App.Message("데이터 폴더에서 타설 기록을 찾지 못했습니다.",
                string.IsNullOrEmpty(s.CsvFolder) ? "데이터 폴더가 지정되지 않았습니다.\n[프로젝트 설정]에서 데이터 폴더를 고르세요."
                : !Directory.Exists(s.CsvFolder) ? s.CsvFolder + " 폴더가 없습니다.\n[프로젝트 설정]에서 데이터 폴더를 고르세요."
                : (wrote ? sync + " — 데이터 폴더 " + s.CsvFolder + " 에 이 구역 ID 로 pours.csv 를 만드세요 (공정표 파일 또는 tools\\make_demo_data.py --from-zones)."
                         : sync != null ? sync + "\n" : "")
                  + (wrote ? "" : "데이터 폴더(" + s.CsvFolder + ")의 zones.csv 구역 ID 로 pours.csv 를 만들거나, [프로젝트 설정]에서 타설 기록이 있는 폴더를 고르세요."));
            return false;
        }

        /// <summary>기준 시각 at 으로 판정해 구역·동바리에 적고, applyColors 면 검측 3D 뷰에 상태 색을 입혀 그 뷰를 연다. 트랜잭션 그룹 하나</summary>
        internal static Result Run(UIApplication app, DateTime at, bool applyColors)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument) { App.Message("프로젝트 파일을 연 상태에서 눌러 주세요."); return Result.Cancelled; }
            try
            {
                var s = ProjectSettings.Read(doc);
                var folderLine = s.EnsureFolder();   // 87차: 기본 폴더면 결과 창에 경로 한 줄
                var sync = ZoneEvaluation.SyncZonesCsv(doc, s.CsvFolder);   // 모델 구역을 zones.csv 에 (없는 것만, N5) — 타설 기록 확인보다 먼저 (N13)
                if (sync != null) Dashboard.Log("판정: " + sync);   // 대시보드 기록 (slab_mm 열 추가 등 — 결과 창에도 같은 줄)
                if (!HasPours(s, sync)) return Result.Cancelled;
                View view = null;
                (List<Engine.ZoneRow> rows, List<string> report) r = default;
                // 쓰기 = App.Write (실행 취소 한 번 "Revit On-Site: 해체 판정", Revit 이 되돌리면 성공이라 하지 않음)
                if (!App.Write(doc, "해체 판정", () =>
                {
                    r = ZoneEvaluation.Apply(doc, s.CsvFolder, at, s.CureMin);
                    // 마지막 판정 기준일 → 대시보드 기준일 초기값 (5차 점검 L3). 매개변수가 아직 없으면 건너뜀 ([매개변수 연결]로 생김)
                    if (doc.ProjectInformation?.LookupParameter("PRJ_LastEvalDate") is Parameter lp && !lp.IsReadOnly)
                        lp.Set(at.Year * 10000 + at.Month * 100 + at.Day);
                    if (applyColors)
                    {
                        try
                        {
                            view = ZoneEvaluation.StatusView(doc);
                            ZoneEvaluation.ApplyFilters(doc, view);
                            bool cut = ZoneEvaluation.FocusOnEvaluated(doc, (View3D)view, r.rows.Select(x => x.ZoneId));
                            ZoneFocus.ApplyShores(doc, (View3D)view, new List<Element>());   // 동바리 표시(기본: 판정 대상 층)도 같은 판정 트랜잭션 안에서
                            r.report.Add("상태 색은 검측 3D 뷰에 보입니다." + (cut ? " (판정한 층 위는 단면 상자로 잘라 냄)" : ""));
                        }
                        catch (InvalidOperationException ex) { view = null; r.report.Add("색 적용 안 함: " + ex.Message); }
                    }
                }, out var err))
                    return App.WriteFailed("해체 판정", err);
                // 판정 이력 (eval_log.csv, 추가만) — '상태 바뀜' 비교 기준·근거 원장 보조 자료. 못 쓰면 판정은 그대로 두고 기록만
                try { Engine.Alerts.LogEval(s.CsvFolder, at, Engine.Alerts.EvalSummary(r.rows, s.CsvFolder, at)); }
                catch (Exception ex) { Dashboard.Log("판정 이력(eval_log.csv)을 쓰지 못함: " + ex.Message); }
                // 판정표 파일 (총괄 11-05 ②, impact_map #1): evaluate.py 판정표와 같은 열·바이트. 시연 샘플 폴더(shoring\sample)에는 쓰지 않음
                string tablePath = null;
                if (!Engine.Evaluator.IsSample(s.CsvFolder))
                    try
                    {
                        tablePath = Path.Combine(s.CsvFolder, $"판정표_{at.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)}.csv");
                        Engine.Evaluator.Write(r.rows, tablePath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Dashboard.Log("판정표를 쓰지 못함 (Excel 등에서 열려 있음?): " + ex.Message); tablePath = null; }
                if (view != null) app.ActiveUIDocument.ActiveView = view;   // 판정 색 뷰로 전환
                Dashboard.Refresh(doc);
                if (FieldInput.Running) FieldInput.SyncDate(doc);   // 현장 입력 서버의 "지금"(데모 기준일)도 새 기준일로
                ZoneFocus.Rehighlight(app);   // 선택 강조 덮개 색을 새 판정 상태로 (덧그림, 트랜잭션 없음)
                // 큰 글자 = 저장 뒤 모델의 구역 상태를 다시 센 값
                var st = ShoringModel.Zones(doc).Select(z => z.LookupParameter("SHR_Status") is Parameter p && p.HasValue ? p.AsInteger() : -1).ToList();
                string Rec(string name, int code) => st.Count(x => x == code) is int n && n > 0 ? " · " + name + " " + n : "";   // 승인 기록 상태는 있을 때만 (N8)
                App.ShowResult(app, "해체 판정 " + at.ToString("yyyy-MM-dd HH:mm"), "해체 승인 요청 가능 " + st.Count(x => x == 5) + " · 존치 " + st.Count(x => x is 2 or 3 or 9) + (st.Count(x => x == 2) is int d2 && d2 > 0 ? $" (설치 결함 {d2})" : "") + " · 공시체 시험 필요 " + st.Count(x => x == 4)
                    + Rec("감리 승인 기록", 6) + Rec("해체 완료", 7) + Rec("재받침", 8),
                    "해체 판정을 마쳤습니다.\n" + string.Join("\n", r.report) + (sync != null ? "\n" + sync : "")
                    + (tablePath != null ? "\n판정표: " + tablePath : "") + (folderLine != null ? "\n" + folderLine : "") + App.WarningNote());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                return App.Fail("해체 판정", ex);
            }
        }
    }
}

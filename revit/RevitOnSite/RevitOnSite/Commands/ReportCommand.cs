// [보고·공유 · 검측요청서] 동바리 해체 검측요청서 초안 HTML (감리 검측요청 서식 준용) (approval.py request_html 과 바이트 동일)을 데이터 폴더에 쓰고 브라우저로 연다 — 인쇄하면 PDF.
// 판정 = 마지막 판정 기준일 09:00 (대시보드와 같은 시각). 서명란은 비워 둔다. 모델은 바꾸지 않는다.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitOnSite.Engine;

namespace RevitOnSite
{
    [Transaction(TransactionMode.ReadOnly)]
    public class ReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) => Run(data.Application);

        /// <summary>리본·대시보드 '오늘 할 일'(v6.2 W13) 같은 길 — API 문맥에서</summary>
        internal static Result Run(UIApplication app)
        {
            var doc = App.ProjectDoc(app);
            if (doc == null) return Result.Cancelled;
            try
            {
                var s = ProjectSettings.Read(doc);
                if (!EvaluateCommand.HasPours(s)) return Result.Cancelled;
                if (ZoneEvaluation.LastEvalAt(doc) is not DateTime at) { App.Message("판정 기록이 없습니다.", "[판정]을 먼저 하세요."); return Result.Cancelled; }
                var rows = Evaluator.Evaluate(s.CsvFolder, at, s.CureMin);
                var led = Evidence.LedgerAt(s.CsvFolder, DateOnly.FromDateTime(at));   // 11-11 1-1: 작업계획서(2)·해체 전 점검(4) 없이 요청서를 만들면 확인 (가이드라인 표 3.2.1)
                var unready = Approval.RequestRows(rows, s.CsvFolder, at).Where(r => r.Decision == "해체 승인 요청 가능"
                    && !new[] { 2, 4 }.All(k => led.TryGetValue(r.ZoneId, out var zl) && zl.Dates.ContainsKey(k))).Select(r => r.ZoneId).ToList();
                if (unready.Count > 0 && new TaskDialog("Revit On-Site — 검측요청서")
                    {
                        MainInstruction = $"현장 확인 미비 {unready.Count}구역",
                        MainContent = "작업계획서·해체 전 점검 근거 원장 기록이 없습니다: " + string.Join(", ", unready.Take(12)) + (unready.Count > 12 ? " …" : "")
                            + "\n\n가이드라인 순서는 계획서 승인 → 해체 전 점검 → 검측요청입니다. 요청서 1쪽에 '현장 확인 미비'로 표시하고 만들까요?",
                        CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No, DefaultButton = TaskDialogResult.No,
                    }.Show() != TaskDialogResult.Yes) return Result.Cancelled;
                var path = Path.GetFullPath(Path.Combine(s.CsvFolder, $"해체검측요청서_{at:yyyyMMdd}.html"));
                File.WriteAllText(path, Approval.RequestHtml(rows, App.ProjectName(doc), at, s.CsvFolder, Evaluator.GateDetails(s.CsvFolder, at, s.CureMin)), new UTF8Encoding(false));
                App.OpenInBrowser(path);   // Chrome (사용자 지시 11-08)
                var c = Approval.Counts(rows, s.CsvFolder, at);   // 요청서·대시보드와 같은 수 (11-11)
                Dashboard.Log($"검측요청서: {path} (요청 가능 {c.Ready}, 존치 {c.Keep}, 공시체 시험 필요 {c.Cyl}, 이미 기록 {c.Recorded})");
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("검측요청서", ex); }
        }
    }
}

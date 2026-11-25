// [보고·공유 · 해체 결과보고] (11-12 2-6 G10, 가이드라인 3.2.13) — 해체(원장 7)가 끝나고 해체 후 점검 5항목이 모두 기록된 구역마다 A4 한 장
// 해체결과보고_<구역>_YYYYMMDD.html 을 데이터 폴더에 쓰고 근거 원장 8 결과보고 한 줄(파일 SHA-256). 아직 8 이 없는 구역만. 계산은 Engine.ResultReport (= shoring/result_report.py).
// 리본 버튼은 총괄이 App.cs 에 붙임. 모델은 바꾸지 않는다. 프로그램은 승인하지 않는다.
using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitOnSite.Engine;

namespace RevitOnSite
{
    [Transaction(TransactionMode.ReadOnly)]
    public class ResultReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try
            {
                var s = ProjectSettings.Read(doc);
                if (!EvaluateCommand.HasPours(s)) return Result.Cancelled;
                var at = DateTime.Now;   // 결과보고 작성 기준 = 지금 (해체 후 점검·침하 계측은 판정과 별개로 쌓임)
                var led = Evidence.LedgerAt(s.CsvFolder, DateOnly.FromDateTime(at));
                var done = led.Where(kv => kv.Value.Dates.ContainsKey(7) && !kv.Value.Dates.ContainsKey(8)).Select(kv => kv.Key).OrderBy(z => z, Evaluator.FloorOrder(s.CsvFolder)).ToList();
                var ready = done.Where(z => ResultReport.WhyNot(s.CsvFolder, z, at) == null).ToList();
                var wait = done.Except(ready).ToList();
                if (ready.Count == 0)
                {
                    App.Message("결과보고를 만들 구역이 없습니다.", done.Count == 0 ? "해체(원장 7) 기록이 있고 결과보고(8)가 아직 없는 구역이 없습니다."
                        : "해체 후 점검 5항목을 다 넣은 구역이 없습니다 — " + string.Join(" / ", wait.Take(6).Select(z => ResultReport.WhyNot(s.CsvFolder, z, at))));
                    return Result.Cancelled;
                }
                if (string.IsNullOrWhiteSpace(App.Actor)) { App.Message("입력자 이름이 없습니다.", "다른 기록 창(구역 정보·근거 원장 등)에서 입력자 이름을 한 번 넣은 뒤 다시 하세요."); return Result.Cancelled; }
                var td = new TaskDialog("해체 결과보고") { MainInstruction = $"결과보고 {ready.Count}구역 만들기 — 입력자 {App.Actor}",
                    MainContent = string.Join(", ", ready) + (wait.Count > 0 ? $"\n\n점검 항목이 비어 뺀 구역 {wait.Count}: {string.Join(", ", wait.Take(10))}" : "")
                        + "\n\n구역마다 A4 한 장 HTML + 근거 원장 8 결과보고 한 줄 (파일 SHA-256). 서명란은 비어 있습니다.",
                    CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel };
                if (td.Show() != TaskDialogResult.Ok) return Result.Cancelled;
                var paths = ready.Select(z => ResultReport.Make(s.CsvFolder, z, at, App.Actor, App.ProjectName(doc))).ToList();
                App.ShowResult(data.Application, "해체 결과보고", $"해체 결과보고 {paths.Count}구역 — 근거 원장 8 기록",
                    string.Join("\n", paths) + "\n\n인쇄해 현장 책임자·감리가 서명합니다. 프로그램은 승인하지 않습니다.", FolderOpen.Button(s.CsvFolder));
                Dashboard.Log($"해체 결과보고: {paths.Count}구역 ({string.Join(", ", ready)})");
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("해체 결과보고", ex); }
        }
    }
}

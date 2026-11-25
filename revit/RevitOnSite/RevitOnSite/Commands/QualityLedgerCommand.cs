// [보고·공유 · 품질 대장] 28일 품질 대장(표준양생 공시체, KCS 14 20 10 표 3.5-7 [부분 확인]) HTML 한 장을 데이터 폴더(품질대장_YYYYMMDD.html)에 쓰고 브라우저로 연다 (F7).
// 계산은 Engine.Quality (= shoring/quality.py). 해체 판정·대시보드와 별개인 참고 기록 — 품질관리자 확인용. 모델은 바꾸지 않는다.
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
    public class QualityLedgerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try
            {
                var s = ProjectSettings.Read(doc);
                if (!EvaluateCommand.HasPours(s)) return Result.Cancelled;
                var rows = Quality.Ledger(s.CsvFolder);
                // 23차 E14: 다른 결과물(요청서·이력서)처럼 마지막 판정일로 파일 이름 — 판정 전이면 오늘
                var day = ZoneEvaluation.LastEvalAt(doc) ?? DateTime.Today;
                var path = Path.GetFullPath(Path.Combine(s.CsvFolder, $"품질대장_{day.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)}.html"));
                File.WriteAllText(path, Quality.Html(rows), new UTF8Encoding(false));
                App.OpenInBrowser(path);   // Chrome (사용자 지시 11-08)
                Dashboard.Log($"품질 대장: {path} ({rows.Count}구역 — " + string.Join(" · ", rows.GroupBy(r => r.Result).Select(g => $"{g.Key} {g.Count()}")) + ")");
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("품질 대장", ex); }
        }
    }
}

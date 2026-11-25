// [승인 ▾ · 감사 기록] 승인 기록·근거 원장·동바리 확인·판정 이력·해체 후 점검을 시각 순으로 한 CSV(데이터 폴더 감사기록_YYYYMMDD.csv)로 — 책임 추적 (impact_map #4, 총괄 11-05 ⑩).
// 계산은 Engine.AuditExport (= shoring/audit_export.py, 새 판단 없음). 원본 파일은 읽기만, 모델은 바꾸지 않는다.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitOnSite.Engine;

namespace RevitOnSite
{
    [Transaction(TransactionMode.ReadOnly)]
    public class AuditLogCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try
            {
                var s = ProjectSettings.Read(doc);
                if (string.IsNullOrWhiteSpace(s.CsvFolder) || !Directory.Exists(s.CsvFolder))
                { App.Message("데이터 폴더가 없습니다.", "[프로젝트 설정]에서 데이터 폴더를 고르세요."); return Result.Cancelled; }
                var rows = AuditExport.Rows(s.CsvFolder);
                var day = ZoneEvaluation.LastEvalAt(doc) ?? DateTime.Today;
                var path = Path.GetFullPath(Path.Combine(s.CsvFolder, $"감사기록_{day.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)}.csv"));
                AuditExport.WriteCsv(rows, path);
                var by = string.Join(" · ", rows.GroupBy(r => r.Source).Select(g => $"{g.Key} {g.Count()}"));
                App.ShowResult(data.Application, "감사 기록", $"감사 기록 {rows.Count}줄" + (by.Length > 0 ? " — " + by : ""),
                    $"파일: {path}\n열: 시각 · 출처 · 구역 · 행위 · 입력자 · 세부 · 줄 해시(원본 줄의 SHA-256 앞 16자 — 원본을 고치면 달라짐).\n"
                    + "승인 기록의 해시 사슬은 세부의 '사슬 …' 로, 사슬 검사는 [검측요청서] 아래 승인 기록 줄에서 봅니다. 원본 파일과 모델은 바뀌지 않았습니다.",
                    FolderOpen.Button(s.CsvFolder, path));   // 탐색기는 누를 때만 (32차: 실행마다 새 창이 쌓임)
                Dashboard.Log($"감사 기록: {path} ({rows.Count}줄)");
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("감사 기록", ex); }
        }
    }
}

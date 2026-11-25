// [보고·공유 · 슬래브 이력서] 마지막 판정 기준으로 구역마다 A4 한 장 HTML(데이터 폴더 슬래브이력서_YYYYMMDD.html) + 요약 CSV 를 쓰고 브라우저로 연다 (F10, impact_map #17).
// 계산은 Engine.SlabRecord (= shoring/slab_record.py, 새 계산 없음 — 판정·근거 원장·승인 사슬·판정 이력을 모음). 품질 기록이며 해체·사용 승인이 아니다. 모델은 바꾸지 않는다.
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitOnSite.Engine;

namespace RevitOnSite
{
    [Transaction(TransactionMode.ReadOnly)]
    public class SlabRecordCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try
            {
                var s = ProjectSettings.Read(doc);
                if (!EvaluateCommand.HasPours(s)) return Result.Cancelled;
                if (ZoneEvaluation.LastEvalAt(doc) is not DateTime at) { App.Message("판정 기록이 없습니다.", "[판정]을 먼저 하세요 — 슬래브 이력서는 마지막 판정 결과를 옮깁니다."); return Result.Cancelled; }
                var recs = SlabRecord.Records(s.CsvFolder, at, cureMin: s.CureMin);
                var path = Path.GetFullPath(Path.Combine(s.CsvFolder, $"슬래브이력서_{at:yyyyMMdd}.html"));
                File.WriteAllText(path, SlabRecord.Page(recs, App.ProjectName(doc), at), new UTF8Encoding(false));
                SlabRecord.WriteCsv(recs, Path.ChangeExtension(path, ".csv"));
                App.OpenInBrowser(path);   // Chrome (사용자 지시 11-08)
                Dashboard.Log($"슬래브 이력서: {path} ({recs.Count}구역, 요약 CSV 같은 이름)");
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("슬래브 이력서", ex); }
        }
    }
}

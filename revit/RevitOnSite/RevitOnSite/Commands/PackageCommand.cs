// [보고·공유 · 검측 패키지] 감리에게 넘길 한 묶음 폴더(데이터 폴더 검측패키지_YYYYMMDD) — 검측요청서·슬래브 이력서(요청 구역)·감사 기록·근거 원장(CSV+HTML)·첨부 사본·IFC(이 명령이 먼저 내보냄, 11-12 2-6)·목록 해시 (F11).
// 계산은 Engine.Package (= shoring/package.py, 새 계산 없음). 같은 날 다시 만들면 그 폴더를 지우고 새로. 프로그램은 승인하지 않는다. 모델은 바꾸지 않는다.
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
    [Transaction(TransactionMode.Manual)]   // 11-12 2-6: IFC 를 먼저 내보냄 (내보내기는 트랜잭션 안에서 하고 되돌림 — 모델은 그대로)
    public class PackageCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try
            {
                var s = ProjectSettings.Read(doc);
                if (!EvaluateCommand.HasPours(s)) return Result.Cancelled;
                if (ZoneEvaluation.LastEvalAt(doc) is not DateTime at) { App.Message("판정 기록이 없습니다.", "[판정]을 먼저 하세요 — 검측 패키지는 마지막 판정 결과를 묶습니다."); return Result.Cancelled; }
                // 11-12 2-6 Revit #20: 패키지가 IFC 를 같이 만든다 (요청 가능 구역이 있을 때만). IFC 가 실패해도 패키지는 만들고 사유를 적음
                string ifcNote = "";
                if (Approval.RequestRows(Evaluator.Evaluate(s.CsvFolder, at, s.CureMin), s.CsvFolder, at).Any(x => x.Decision == "해체 승인 요청 가능"))
                    try { ExportIfcCommand.Export(doc, s, at); }
                    catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException) { ifcNote = "IFC 를 만들지 못했습니다 — " + ex.Message.Split('\n')[0] + "\n"; }
                (string Out, System.Collections.Generic.List<(string Name, long Bytes, string Sha)> Files, System.Collections.Generic.List<string> Req) r;
                try { r = Package.Build(s.CsvFolder, at, App.ProjectName(doc), s.CureMin); }
                catch (InvalidDataException ex) when (ex.Message.Contains("요청 가능 구역이 없어"))
                { App.Message("해체 승인 요청 가능 구역이 없습니다.", "검측 패키지는 요청 가능 구역이 있을 때만 만듭니다 (판정 기준 " + at.ToString("yyyy-MM-dd") + ")."); return Result.Cancelled; }
                bool ifc = r.Files.Any(f => f.Name.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase));
                int phc = r.Files.Count(f => f.Name.StartsWith("첨부/사진/", StringComparison.Ordinal));
                int att = r.Files.Count(f => f.Name.StartsWith("첨부/", StringComparison.Ordinal)) - phc;
                // 파일 수 = 목록.csv 줄 수 (첨부 서류·현장 사진 포함, 목록.csv·index.html 자신은 빼고 — 총괄 29차에 '사진을 안 세는 것'처럼 보여 밝혀 씀)
                App.ShowResult(data.Application, "검측 패키지", $"검측 패키지 — 요청 구역 {r.Req.Count} · 파일 {r.Files.Count}" + (phc > 0 ? $" (현장 사진 {phc}장 포함)" : ""),
                    $"폴더: {r.Out}\n요청 구역: {string.Join(", ", r.Req)}\n"
                    + $"묶은 것: 검측요청서 · 슬래브 이력서(요청 구역) · 감사 기록·근거 원장 (CSV + 요청 구역 HTML 표, 첨부 해시 대조){(att > 0 ? $" · 첨부 서류 {att}개" : "")}{(phc > 0 ? $" · 현장 사진 {phc}장(요청 구역 + 현장 확인 ① 위층 최근 24 h)" : "")}{(ifc ? " · IFC" : "")}\n"
                    + $"+ 목록.csv·index.html (파일마다 SHA-256 — 파일 {r.Files.Count}개 = 목록.csv 줄 수)\n"
                    + ifcNote
                    + "같은 날 다시 만들면 이 폴더를 새로 씁니다. 프로그램은 승인하지 않습니다 — 해체 여부는 감리 확인·서명으로 정합니다.",
                    FolderOpen.Button(r.Out));   // 탐색기는 [폴더 열기] 를 누를 때만, 이미 열린 창이 있으면 그 창을 앞으로 (32차: 실행마다 새 창이 쌓임)
                Dashboard.Log($"검측 패키지: {r.Out} (요청 구역 {r.Req.Count}, 파일 {r.Files.Count}{(ifc ? ", IFC" : "")})");
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("검측 패키지", ex); }
        }
    }
}

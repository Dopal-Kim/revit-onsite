// [보고·공유 · 층 표지] 마지막 판정 기준으로 층마다 A4 한 장 HTML(데이터 폴더 층표지_YYYYMMDD.html)을 쓰고 브라우저로 연다 — 인쇄해 층 입구에 붙임.
// 계산은 Engine.FloorSign (기존 판정·승인 기록·근거 원장·동바리 확인만 옮김). 현장 입력이 켜져 있으면 층마다 그 층으로 바로 가는 QR (주소 끝 #floor=층 번호).
// 마감 값 = 검측요청서와 같은 식 (요청 가능 구역이 있을 때만). 모델은 바꾸지 않는다. 프로그램은 해체를 지시하지 않는다.
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
    public class FloorSignCommand : IExternalCommand
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
                if (ZoneEvaluation.LastEvalAt(doc) is not DateTime at) { App.Message("판정 기록이 없습니다.", "[판정]을 먼저 하세요 — 층 표지는 마지막 판정 결과를 옮깁니다."); return Result.Cancelled; }
                var rows = Evaluator.Evaluate(s.CsvFolder, at, s.CureMin);
                var floors = FloorSign.Floors(s.CsvFolder, rows, at);
                if (floors.Count == 0) { App.Message("데이터 폴더의 zones.csv 에 구역이 없습니다.", "[판정]을 한 번 하면 모델 구역을 zones.csv 에 씁니다."); return Result.Cancelled; }
                // 11-12 2-6 Revit #21: 기본 = 동바리 서 있는 층만 (해체 완료·타설 전 층은 안 뽑음), 전체는 고를 때만
                var standing = FloorSign.Standing(floors);
                if (standing.Count < floors.Count)
                {
                    var td = new TaskDialog("층 표지") { MainInstruction = $"동바리 서 있는 층 {standing.Count}개 / 전체 {floors.Count}개층", MainContent = "해체 완료·타설 전 층은 붙일 필요가 없어 기본으로 뺍니다." };
                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"동바리 서 있는 층만 ({standing.Count}장)");
                    td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, $"전체 층 ({floors.Count}장)");
                    td.CommonButtons = TaskDialogCommonButtons.Cancel;
                    var pick = td.Show();
                    if (pick == TaskDialogResult.Cancel) return Result.Cancelled;
                    if (pick == TaskDialogResult.CommandLink1)
                    {
                        if (standing.Count == 0) { App.Message("동바리 서 있는 층이 없습니다.", "모든 층이 해체 완료 기록이거나 타설 전입니다 — 전체 층을 고르면 모두 뽑습니다."); return Result.Cancelled; }
                        floors = standing;
                    }
                }
                var req = Approval.RequestRows(rows, s.CsvFolder, at).Where(r => r.Decision == "해체 승인 요청 가능").ToList();   // 검측요청서와 같은 구역 → 같은 마감 값
                string seal = req.Count > 0 ? Approval.SealValue(req, s.CsvFolder, at) : null;
                // QR: [서버 연동] 중이면 서버 층 보기 주소 (어디서나 열림), 아니면 현장 입력이 이 데이터 폴더로 켜져 있을 때만 LAN 주소 — 다른 폴더 서버의 주소를 붙이지 않게
                var phone = ServerLink.FloorSignUrl() ?? FloorSign.PhoneUrl(FieldInput.Running ? FieldInput.Server : null, s.CsvFolder);
                var path = Path.GetFullPath(Path.Combine(s.CsvFolder, $"층표지_{at:yyyyMMdd}.html"));
                File.WriteAllText(path, FloorSign.Page(App.ProjectName(doc), at, floors, phone, seal, DateTime.Now, Evaluator.NumOption(s.CsvFolder, "sign_valid_hours", FloorSign.SignValidH)), new UTF8Encoding(false));   // W10 인쇄 시각
                App.OpenInBrowser(path);   // Chrome (사용자 지시 11-08)
                Dashboard.Log($"층 표지: {path} ({floors.Count}개층, 해체 금지 {floors.Count(f => f.Kind == "hold")}층, 무단 해체 의심 {floors.Count(f => f.Kind == "suspect")}층, QR {(phone(floors[0].Floor) != null ? "있음" : "없음")})");
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("층 표지", ex); }
        }
    }
}

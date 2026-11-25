// [타설·데이터 · 타설 등록] (+ 상황별 탭 [타설 등록]) · [데이터 가져오기 ▾ · 공시체 결과] (+ 상황별 탭 [공시체 입력]) — 입력 창 하나(DataEntryWindow). v6 동바리 설치일은 [동바리 사양](ShoreInput.cs)
// 저장 = Engine.FieldServer.SubmitLocal: 휴대폰 [현장 입력]과 같은 검사·문구·CSV 쓰기 (field_server.py 종류 pour·cylinder 와 바이트 동일, Field.Check).
// 판정 기준일이 있으면 같은 시각으로 판정을 다시 계산해 구역·동바리 값에 적는다 (쓰기 한 번, 실행 취소 "Revit On-Site: 타설 등록"/"…: 공시체 결과"). shoring\sample 에는 쓰지 않음
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitOnSite.Engine;

namespace RevitOnSite
{
    [Transaction(TransactionMode.Manual)]
    public class PourCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) => DataEntry.Run(data, "pour");
    }

    static class DataEntry
    {

        /// <param name="kind">pour · cylinder (FieldServer 종류)</param>
        internal static Result Run(ExternalCommandData data, string kind)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            bool pour = kind == "pour";
            string what = pour ? "타설 등록" : "공시체 결과";
            try
            {
                var s = ProjectSettings.Read(doc);
                s.EnsureFolder();   // 87차: 기본 폴더가 없으면 만듦
                var folder = s.CsvFolder;
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) { App.Message("데이터 폴더가 없습니다.", "[프로젝트 설정]에서 데이터 폴더를 고르세요."); return Result.Cancelled; }
                if (Engine.Evaluator.IsSample(folder))
                { App.Message("시연 샘플 폴더에는 기록하지 않습니다.", "회귀 시험 기준 자료입니다. [프로젝트 설정]에서 사본 폴더(예: revit\\onsite_data_demo)를 고르세요."); return Result.Cancelled; }
                var sync = ZoneEvaluation.SyncZonesCsv(doc, folder);   // 모델 구역을 구역 표에 (없는 것만) — 고를 구역 = 모델 구역
                if (sync != null) Dashboard.Log(what + ": " + sync);
                if (!File.Exists(Path.Combine(folder, "zones.csv"))) { App.Message("구역 표가 없습니다.", "[구역·동바리 생성]을 먼저 하세요."); return Result.Cancelled; }
                var done = Evaluator.Read(folder, "pours.csv").Select(r => r.GetValueOrDefault("zone_id") ?? "").ToHashSet();
                var cand = Evaluator.Read(folder, "zones.csv").Select(r => r.GetValueOrDefault("zone_id") ?? "").Where(z => z.Length > 0 && done.Contains(z) == (kind == "cylinder"))
                    .Distinct().OrderBy(z => z, Evaluator.FloorOrder(folder)).ToList();
                if (!pour && cand.Count > 1)   // 11-11 Revit #8: 공시체 없는 구역 → 최근 타설 순이 위로 (지금 시험할 구역이 맨 아래에 있던 것)
                {
                    var hasCyl = Evaluator.Read(folder, "cylinders.csv").Select(r => r.GetValueOrDefault("zone_id") ?? "").ToHashSet();
                    var pd = new Dictionary<string, string>();
                    foreach (var r in Evaluator.Read(folder, "pours.csv")) pd[r.GetValueOrDefault("zone_id") ?? ""] = r.GetValueOrDefault("pour_date") ?? "";
                    cand = cand.OrderBy(z => hasCyl.Contains(z)).ThenByDescending(z => pd.GetValueOrDefault(z, ""), StringComparer.Ordinal).ToList();   // 안정 정렬 — 같은 날은 층 순
                }
                if (cand.Count == 0)
                {
                    App.Message(pour ? "타설 기록이 없는 구역이 없습니다." : "타설 기록이 있는 구역이 없습니다.",
                        pour ? "모든 구역에 타설 기록이 있습니다. 잘못 넣은 타설은 구역 정보 창 [타설 기록 취소] 뒤 다시 등록합니다." : "[타설 등록]을 먼저 하세요 — 공시체 시험일은 타설일 뒤여야 합니다.");
                    return Result.Cancelled;
                }
                var picked = ZoneFocus.SelectedZones(data.Application.ActiveUIDocument).Select(ZoneFocus.Zid).ToHashSet();
                var at = ZoneEvaluation.LastEvalAt(doc);
                DateTime? demo = at > DateTime.Now ? at : null;   // 판정 기준일이 미래(시연 자료)면 그 시각을 '지금'으로 — FieldInput.SyncDate 와 같은 규칙
                var srv = FieldInput.Running && string.Equals(Path.GetFullPath(FieldInput.Server.Folder), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)
                    ? FieldInput.Server : new FieldServer(folder) { FixedNow = () => demo, DefaultFck = s.DefaultFck };
                var lots = Evaluator.Read(folder, "pours.csv").Where(r => !string.IsNullOrWhiteSpace(r.GetValueOrDefault("pour_id")))   // v6.2 W1 타설 묶음 (공시체 결과에서 고름)
                    .GroupBy(r => r["pour_id"]!.Trim()).Select(g => (Lot: g.Key, Zones: g.Select(r => r["zone_id"] ?? "").OrderBy(z => z, Evaluator.FloorOrder(folder)).ToList()))
                    .Select(x => (x.Lot, $"{x.Lot} — {x.Zones[0]}" + (x.Zones.Count > 1 ? $" 외 {x.Zones.Count - 1}구역" : ""), x.Zones)).ToList();
                var w = new DataEntryWindow(kind, cand.Select(z => (z, z + (picked.Contains(z) ? "   (고른 구역)" : ""))).ToList(), picked, demo ?? DateTime.Now, s,
                    App.Actor, b => srv.SubmitLocal(b), lots);
                if (!App.ShowDialog(w, data.Application)) return Result.Cancelled;
                App.Actor = w.ActorName;
                string rep = "";
                if (!pour && w.ReportPath.Length > 0)   // 11-12 Revit #9: 시험성적서 → 근거 원장 3 (경로·SHA-256, 검측 패키지 첨부)
                {
                    try
                    {
                        foreach (var z in w.ZoneIds) Evidence.Add(folder, z, w.TestDay, 3, w.ActorName, "공시체 시험성적서", "성적서", w.ReportPath);
                        rep = $"\n시험성적서 → 근거 원장 3 공시체 {w.ZoneIds.Count}줄 ({Path.GetFileName(w.ReportPath)})";
                    }
                    catch (Exception ex) { rep = "\n시험성적서를 근거 원장에 남기지 못했습니다 — [근거 원장]에서 3 공시체로 다시 넣으세요 (" + ex.Message + ")"; }
                }
                string note = "판정 기준일이 아직 없습니다 — [해체 판정]을 누르면 반영됩니다.";
                if (ZoneEvaluation.Reapply(data.Application, doc, folder, s.CureMin, what, out var err) is bool ok)
                    note = ok ? $"판정 다시 계산 ({ZoneEvaluation.LastEvalAt(doc):MM-dd HH:mm} 기준) — 구역·동바리 판정 값을 고쳤습니다."
                        : $"기록은 저장됐지만 판정을 다시 계산하지 못했습니다 — [해체 판정]을 다시 누르세요 ({err}). 같은 기록을 다시 넣지 마세요.";   // 코드 검토 2차
                Timeline.DataChanged();   // 4D 사건·본수 곡선도 새 기록으로
                App.ShowResult(data.Application, what, App.Recorded(what, w.ZoneIds.Count) + " — " + string.Join(", ", w.ZoneIds),
                    $"데이터 폴더 {(pour ? "타설 기록" : "공시체 기록")} 파일에 {w.SavedRows}줄 더함 (휴대폰 입력과 같은 검사)\n{Path.Combine(folder, FieldServer.Kinds[kind].File)}\n{note}{rep}");
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail(what, ex); }
        }
    }
}

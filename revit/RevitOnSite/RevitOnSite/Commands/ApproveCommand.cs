// [승인 · 승인 기록] 고른 구역에 사람이 한 행위(요청 제출·감리 승인 기록·해체 완료·재받침)를 데이터 폴더 approvals.csv 에 기록 — Engine.Approval (approval.py 와 바이트 동일).
// 근거 = 마지막 판정 기준일 09:00 의 판정 행 (대시보드와 같은 시각). 요청 제출·감리 승인 기록은 '해체 승인 요청 가능' 구역만 (거부 문구는 approval.py 그대로).
// 상태 6·7·8 은 구역·동바리 SHR_Status 에도 적는다 (쓰기 한 번, 실행 취소 "Revit On-Site: 승인 기록"). 프로그램은 승인하지 않는다.
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
    public class ApproveCommand : IExternalCommand
    {

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try
            {
                var ui = data.Application.ActiveUIDocument;
                var s = ProjectSettings.Read(doc);
                if (!EvaluateCommand.HasPours(s)) return Result.Cancelled;
                var zones = ZoneFocus.SelectedZones(ui).Select(ZoneFocus.Zid).Distinct().OrderBy(z => z, StringComparer.Ordinal).ToList();
                if (zones.Count == 0) { App.Message("기록할 구역을 먼저 고르세요.", "검측 3D 뷰에서 구역(또는 그 동바리)을 고르거나 대시보드 칸을 누른 뒤 다시 누르세요."); return Result.Cancelled; }
                if (ZoneEvaluation.LastEvalAt(doc) is not DateTime at) { App.Message("판정 기록이 없습니다.", "[판정]을 먼저 하세요 — 승인 기록에는 그때 판정 근거(해시)가 함께 남습니다."); return Result.Cancelled; }
                var rows = Evaluator.Evaluate(s.CsvFolder, at, s.CureMin).ToDictionary(r => r.ZoneId);
                var (chainOk, chain) = Approval.Verify(s.CsvFolder);
                var (clock, demo) = Approval.RecordClock(at);   // 판정 기준일이 미래(시연 자료)면 그 시각으로 고정 — FieldInput.SyncDate 와 같은 규칙
                var w = new ApproveWindow(zones.Select(z => (z, rows.TryGetValue(z, out var r) ? r.Decision : "")).ToList(),
                    $"판정 기준 시각 {at:yyyy-MM-dd HH:mm} · 승인 {chain}" + (demo ? $"\n기록 시각 {clock:yyyy-MM-dd HH:mm} {Approval.DemoNote}" : ""),
                    App.Actor, s.CsvFolder, DateOnly.FromDateTime(at));
                if (!App.ShowDialog(w, data.Application)) return Result.Cancelled;
                App.Actor = w.ActorName;

                var done = new List<string>();
                var refused = new List<string>();
                foreach (var z in zones)
                {
                    if (!rows.TryGetValue(z, out var r)) { refused.Add(z + ": 판정 행이 없습니다 (타설 전이거나 판정 전)"); continue; }
                    try { Approval.Record(s.CsvFolder, z, w.ActionName, w.ActorName, Approval.RowOf(r), w.NoteText, evalAt: at, approver: w.ApproverName, source: w.ActionName == "감리 승인 기록" ? "종이 옮김" : "", signedAt: w.SignedAtText); done.Add(z); }
                    catch (InvalidOperationException ex) { refused.Add(ex.Message); }
                }
                // 상태 6·7·8 을 모델에도 (요청 제출은 상태 안 바꿈)
                int? code = Approval.Actions.First(a => a.Action == w.ActionName).Code;
                if (code is int c && done.Count > 0)
                {
                    var set = done.ToHashSet();
                    var els = ShoringModel.Zones(doc).Concat(ShoringModel.Shores(doc)).Where(e => set.Contains(ZoneFocus.Zid(e))).ToList();
                    if (!App.Write(doc, "승인 기록", () =>
                    {
                        foreach (var e in els) if (e.LookupParameter("SHR_Status") is Parameter p && !p.IsReadOnly) p.Set(c);
                    }, out var err)) App.WriteFailed("승인 기록 (모델 상태)", err);
                }
                Dashboard.Refresh(doc);
                string body = (refused.Count > 0 ? "기록하지 않은 구역:\n" + string.Join("\n", refused) + "\n\n" : "")
                    + "기록 파일: " + Path.GetFullPath(Path.Combine(s.CsvFolder, Approval.Log)) + "\n" + Approval.Verify(s.CsvFolder).Message
                    + (chainOk ? "" : "\n(기록 전에도 사슬이 손상돼 있었습니다 — 파일을 손대지 말고 관리자에게 알리세요)");
                if (done.Count > 0) App.ShowResult(data.Application, "승인 기록", $"{App.Recorded(w.ActionName, done.Count)} — {string.Join(", ", done)}" + (demo ? " " + Approval.DemoNote : ""), body);
                else App.Message("기록하지 않았습니다.", body);   // 전부 거부 = 결과가 아니라 알림 → 모달
                return done.Count > 0 ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception ex) { return App.Fail("승인 기록", ex); }
        }
    }
}

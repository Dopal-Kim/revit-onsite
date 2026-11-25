// [승인 · 근거 원장] 고른 구역에 해체 절차 단계 서류 한 건을 데이터 폴더 evidence.csv 에 추가 — Engine.Evidence (evidence.py 와 바이트 동일).
// 원본은 서명된 서류, 원장에는 파일 경로와 SHA-256 만. 갖춘 단계·순서 누락 경고는 대시보드 구역 상세의 "근거 n/8" 줄.
// 모델은 바꾸지 않는다 (공유 매개변수 SHR_Ev* 는 보류 — 총괄 11-04). 프로그램은 승인·해체를 지시하지 않는다.
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
    [Transaction(TransactionMode.ReadOnly)]
    public class EvidenceCommand : IExternalCommand
    {
        static readonly int[] HumanStages = { 2, 4, 8 };   // 자동 출처가 없는 단계 — 처음 고를 단계 후보

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try
            {
                var s = ProjectSettings.Read(doc);
                s.EnsureFolder();   // 87차: 기본 폴더(rvt 옆 onsite_data_<이름>)가 없으면 만듦
                if (string.IsNullOrWhiteSpace(s.CsvFolder) || !Directory.Exists(s.CsvFolder))
                {
                    App.Message("데이터 폴더가 없습니다.", (string.IsNullOrEmpty(s.CsvFolder) ? "데이터 폴더가 지정되지 않았습니다." : s.CsvFolder) + "\n[프로젝트 설정]에서 데이터 폴더를 고르세요.");
                    return Result.Cancelled;
                }
                var zones = ZoneFocus.SelectedZones(data.Application.ActiveUIDocument).Select(ZoneFocus.Zid).Distinct().OrderBy(z => z, StringComparer.Ordinal).ToList();
                if (zones.Count == 0) { App.Message("기록할 구역을 먼저 고르세요.", "검측 3D 뷰에서 구역(또는 그 동바리)을 고르거나 대시보드 칸을 누른 뒤 다시 누르세요."); return Result.Cancelled; }

                var basis = DateOnly.FromDateTime(ZoneEvaluation.LastEvalAt(doc) ?? DateTime.Today);   // 대시보드 "근거 n/8" 와 같은 기준일
                var res = Record(s.CsvFolder, zones, basis, null, null, w => App.ShowDialog(w, data.Application));
                if (res is not { } r) return Result.Cancelled;
                App.ShowResult(data.Application, "근거 원장", r.Head, r.Body);
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("근거 원장 추가", ex); }
        }

        /// <summary>근거 원장 창을 띄워 고른 구역마다 한 줄씩 추가 (리본 [근거 원장]·[승인 기록] 창의 '서명 스캔 남기기' 공통). 취소면 null.
        /// stage·actor = 처음 고를 단계·입력자 (null 이면 첫 구역에 없는 2·4·8 중 앞 / 마지막 입력자), show = 창을 띄우는 방법(주인 창)</summary>
        internal static (string Head, string Body)? Record(string folder, IReadOnlyList<string> zones, DateOnly basis, int? stage, string actor, Func<EvidenceWindow, bool> show)
        {
            var ledger = Evidence.LedgerAt(folder, basis);
            string Line(string z) => ledger.TryGetValue(z, out var l)
                ? $"{z} — 근거 연속 {l.Stage}/8 · 갖춘 단계 {string.Join("·", l.Dates.Keys.OrderBy(k => k))}"   // 60차: "3/8 (1·2·3·5·6)" 이 3 과 5·6 을 섞어 읽힘 + (l.Warnings.Count > 0 ? " · 경고 " + l.Warnings.Count : "")
                : $"{z} — 근거 없음";
            var first = ledger.GetValueOrDefault(zones[0]);
            int st = stage ?? HumanStages.FirstOrDefault(h => first == null || !first.Dates.ContainsKey(h), 2);
            var w = new EvidenceWindow(zones.Select(Line), st, string.IsNullOrWhiteSpace(actor) ? App.Actor : actor, basis);   // 날짜 기본값 = 대시보드 기준일 (총괄 P1-2)
            if (!show(w)) return null;
            App.Actor = w.ActorName;

            var added = new List<string>();
            string sha = "";
            var stages = w.AlsoStages.Append(w.StageNo).Distinct().Order().ToList();   // 11-12 2-4: 함께 넣는 단계 — 낮은 단계부터 (순서 경고가 안 나게), 문서번호·파일은 고른 단계에만
            foreach (var z in zones)
                foreach (var stg in stages)
                {
                    bool main = stg == w.StageNo;
                    var r = Evidence.Add(folder, z, w.DayText, stg, w.ActorName, main ? w.DocNoText : "", w.ResultText, main ? w.FilePath : "");
                    added.Add(r["ev_id"] + " " + z + (stages.Count > 1 ? $" ({stg})" : ""));
                    if (main) sha = r["sha256"];
                }
            var entered = DateOnly.ParseExact(w.DayText, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            bool demo = entered == basis && Approval.RecordClock(basis.ToDateTime(new TimeOnly(9, 0))).Demo;   // 미래 판정 기준일 그대로 = 시연 자료
            if (entered > basis) basis = entered;   // 방금 넣은 서류가 보이게
            var after = Evidence.LedgerAt(folder, basis);
            var warns = zones.SelectMany(z => after.GetValueOrDefault(z)?.Warnings ?? new List<string>()).ToList();
            return ($"근거 원장 {added.Count}건 추가 — {string.Join("·", stages.Select(x => x + " " + Evidence.StageName(x)))} · {w.DayText}" + (demo ? " " + Approval.DemoNote : ""),
                string.Join(", ", added)
                + (w.FilePath.Length > 0 ? $"\n파일: {w.FilePath}\nSHA-256: {sha}" : "\n파일 없음 (경로·SHA-256 빈칸)")
                + $"\n\n갖춘 근거 ({basis:yyyy-MM-dd} 기준):\n" + string.Join("\n", zones.Select(z => after.TryGetValue(z, out var l) ? $"{z} — 연속 {l.Stage}/8 · 갖춘 단계 {string.Join("·", l.Dates.Keys.OrderBy(k => k))}" : z))
                + (warns.Count > 0 ? "\n\n순서 경고:\n· " + string.Join("\n· ", warns) : "")
                + "\n\n원장: " + Path.GetFullPath(Path.Combine(folder, Evidence.Ledger)));
        }
    }
}

// [보고·공유 ▾ · 조기 탈형 검토서] 판넬 조기 탈형(필러 존치) 구조 검토서 초안 — 고른 구역, 고른 구역이 없으면 필러(동바리) 판정 '해체 승인 요청 가능' 구역,
// 그것도 없으면 판정된 최상층 구역마다 A4 한 장 (33차: 검토서는 '검토 결과'라 판넬 존치여도 만듦). 판정 근거 창·대시보드 칸 우클릭에서도 (리본은 구역을 고르면 '수정' 탭으로 바뀜)
// (데이터 폴더 조기탈형검토서_<구역>_YYYYMMDD.html, Engine.Staged = shoring/staged.py review_html, HTML Python 과 바이트 동일). 서명란 빈칸 — 초안이며 해체 지시·승인 아님.
// 필러 본수 = 그 구역 모델 동바리 수. 판정표에는 넣지 않는다 (판정표 바이트 그대로). 모델은 바꾸지 않는다.
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
    public class EarlyFormReviewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            if (App.ProjectDoc(data) == null) return Result.Cancelled;
            var picked = ZoneFocus.SelectedZones(data.Application.ActiveUIDocument).Select(ZoneFocus.Zid).Distinct().ToList();
            return Make(data.Application, picked);
        }

        /// <summary>API 문맥: picked = 고른 구역 (비면 필러 '해체 승인 요청 가능' 구역, 없으면 판정된 최상층 구역)</summary>
        internal static Result Make(UIApplication app, System.Collections.Generic.IReadOnlyList<string> picked)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument) return Result.Cancelled;
            try
            {
                var s = ProjectSettings.Read(doc);
                if (!EvaluateCommand.HasPours(s)) return Result.Cancelled;
                if (ZoneEvaluation.LastEvalAt(doc) is not DateTime at) { App.Message("판정 기록이 없습니다.", "[판정]을 먼저 하세요 — 검토서는 마지막 판정 시각의 강도·기온으로 씁니다."); return Result.Cancelled; }
                var st = Staged.Stages(s.CsvFolder, at, s.CureMin);
                string how = "고른 구역";
                var zones = picked.Where(st.ContainsKey).ToList();
                if (picked.Count == 0)
                {
                    zones = st.Where(kv => kv.Value.FillerDecision == "해체 승인 요청 가능").Select(kv => kv.Key).ToList();
                    how = "필러 해체 승인 요청 가능 구역";
                    if (zones.Count == 0 && st.Count > 0)
                    {
                        int top = st.Max(kv => kv.Value.Floor);
                        zones = st.Where(kv => kv.Value.Floor == top).Select(kv => kv.Key).ToList();
                        how = $"요청 가능 구역이 없어 판정된 최상층({top}층) 구역";
                    }
                }
                if (zones.Count == 0)
                {
                    App.Message(picked.Count > 0 ? "고른 구역은 판정 시각까지 타설된 구역이 아닙니다." : "판정 시각까지 타설된 구역이 없습니다.",
                        "타설된 구역을 골라 누르면 그 구역 검토서 초안을 만듭니다 — 조건 ① 14 MPa ② 10 ℃ 이상 4일 ③ 위층 하중비 ④ 구조검토서 중 미충족·필요 항목이 표시됩니다.");
                    return Result.Cancelled;
                }
                var shores = ShoringModel.Shores(doc).GroupBy(ZoneFocus.Zid).ToDictionary(g => g.Key, g => g.Count());
                var ymd = at.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                var lines = new System.Collections.Generic.List<string>();
                string first = null;
                foreach (var z in zones)
                {
                    var path = Path.GetFullPath(Path.Combine(s.CsvFolder, $"조기탈형검토서_{z}_{ymd}.html"));
                    File.WriteAllText(path, Staged.ReviewHtml(st, at, z, shores.TryGetValue(z, out var n) ? n : null, App.ProjectName(doc), s.CsvFolder), new UTF8Encoding(false));
                    first ??= path;
                    var v = st[z];
                    lines.Add($"{z}: {v.PanelDecision} — {string.Join(" ", v.Checks.Select(c => c.Name.Substring(0, 1) + (c.Ok ? "✓" : "✕")))}"
                        + $" · 판넬 탈형 가능일(예측) {(v.EarliestPanelDate is { } d ? d.ToString("yyyy-MM-dd") : "400일 안에 없음")} · 필러: {v.FillerDecision}");
                }
                App.OpenInBrowser(first);   // Chrome (사용자 지시 11-08)
                App.ShowResult(app, "조기 탈형 검토서", $"조기 탈형 구조 검토서 초안 {zones.Count}장 — {string.Join(", ", zones)} ({how})",
                    string.Join("\n", lines) + $"\n\n파일 ({s.CsvFolder}): {string.Join(", ", zones.Select(z => $"조기탈형검토서_{z}_{ymd}.html"))} — 첫 장을 브라우저로 엶\n서명란은 비어 있습니다 — 초안이며 책임기술자 검토·서명 전, 해체 지시가 아닙니다.",
                    FolderOpen.Button(s.CsvFolder, first));
                Dashboard.Log($"조기 탈형 검토서: {zones.Count}장 ({string.Join(", ", zones)}) · {how}");
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("조기 탈형 검토서", ex); }
        }
    }
}

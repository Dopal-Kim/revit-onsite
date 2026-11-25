// [준비] 프로젝트 설정 — 창 = Wpf\ProjectSetupWindow, 값 = ProjectSettings(PRJ_*). 공유 매개변수도 함께 연결
// 쓰기 = App.Write (Revit 이 되돌리면 성공이라 하지 않음), 성공 알림은 저장 뒤 모델에서 다시 읽은 값
using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    [Transaction(TransactionMode.Manual)]
    public class SettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            var s = ProjectSettings.Read(doc);
            if (!App.ShowDialog(new ProjectSetupWindow(s), data.Application)) return Result.Cancelled;
            try
            {
                List<string> added = null, missing = null;
                if (!App.Write(doc, "프로젝트 설정", () => { added = ParameterSetup.Ensure(doc); missing = s.Write(doc); }, out var err))
                    return App.WriteFailed("프로젝트 설정 저장", err);
                var r = ProjectSettings.Read(doc);   // 저장된 값 그대로
                Dashboard.SettingsSaved(doc);        // 61차: 4D·대시보드 바로 다시 계산
                var msg = new List<string>
                {
                    "첫 타설일 " + r.StartDate.ToString("yyyy-MM-dd") + " · 사이클 " + r.CycleDays + "일 · 최소 존치 " + r.MinLevels + "개층",
                    r.DefaultMix + " · fck " + r.DefaultFck.ToString("0.#") + " MPa · 양생 최저 " + r.CureMin.ToString("0.#") + " °C · 관측소 " + r.StationId,
                    "데이터 폴더 " + r.CsvFolder + (System.IO.Directory.Exists(r.CsvFolder) ? "" : " (폴더 없음 — 찾아보기로 고르세요)"),   // 87차
                };
                if (added.Count > 0) msg.Add("검측용 매개변수 " + added.Count + "개를 새로 연결했습니다.");
                if (missing.Count > 0) msg.Add("저장하지 못한 항목: " + string.Join(", ", missing) + "\n매개변수 종류가 다릅니다. 공유 매개변수 파일을 확인하세요.");
                if (missing.Count > 0)   // 저장 못 한 항목은 확인이 필요해 모달 그대로
                    App.Message("프로젝트 설정을 저장했습니다.", string.Join("\n", msg) + App.WarningNote(), TaskDialogIcon.TaskDialogIconWarning);
                else                       // 34차: 모달 안내가 떠 있으면 대시보드 [판정]이 1분 넘게 대기 → 모델리스 결과 창 한 줄
                    App.ShowResult(data.Application, "프로젝트 설정", "프로젝트 설정을 저장했습니다 — " + msg[0], string.Join("\n", msg) + App.WarningNote());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                return App.Fail("프로젝트 설정 저장", ex);
            }
        }
    }
}

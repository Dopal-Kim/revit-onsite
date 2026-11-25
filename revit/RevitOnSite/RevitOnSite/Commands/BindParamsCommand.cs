// [준비] 매개변수 연결 — ParameterSetup.Ensure 만 (프로젝트 설정 창 없이). 쓰기 = App.Write, 알림 = 연결 뒤 모델에서 다시 센 값
using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    [Transaction(TransactionMode.Manual)]
    public class BindParamsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try
            {
                List<string> added = null;
                // 바뀐 게 없으면 되돌려 실행 취소 목록에 빈 항목을 남기지 않음
                if (!App.Write(doc, "매개변수 연결", () => (added = ParameterSetup.Ensure(doc)).Count > 0, out var err))
                    return App.WriteFailed("매개변수 연결", err);
                int bound = 0;
                var it = doc.ParameterBindings.ForwardIterator();
                while (it.MoveNext())
                    if (it.Key.Name.StartsWith("SHR_") || it.Key.Name.StartsWith("SNS_") || it.Key.Name.StartsWith("PRJ_")) bound++;
                if (added.Count == 0) App.Message("검측용 매개변수가 이미 모두 연결돼 있습니다.", "연결된 검측용 매개변수 " + bound + "개. 바꾼 것이 없습니다.");
                else App.Message("검측용 매개변수 " + added.Count + "개를 새로 연결했습니다.", "지금 연결된 검측용 매개변수 " + bound + "개." + App.WarningNote());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                return App.Fail("매개변수 연결", ex);
            }
        }
    }
}

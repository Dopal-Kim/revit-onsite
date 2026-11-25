// [4D · 4D 타임라인] 대시보드 창을 열고 4D 막대를 준비한다 (오늘 날짜, 일정 범위 안으로). 그림 = Timeline.cs (DirectContext3D, 모델 그대로)
using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    [Transaction(TransactionMode.Manual)]   // 전용 뷰를 처음 만들 때만 App.Write 1건
    public class TimelineCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            if (App.ProjectDoc(data) == null) return Result.Cancelled;
            try
            {
                Dashboard.Ensure();
                var p = data.Application.GetDockablePane(Dashboard.PaneId);
                if (!p.IsShown()) p.Show();
                Dashboard.PrepareNow(data.Application, play: false);   // 막대에 기간을 넣으면 첫 프레임이 요청됨
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                return App.Fail("4D 타임라인", ex);
            }
        }
    }
}

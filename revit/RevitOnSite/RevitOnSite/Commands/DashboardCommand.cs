// [판정 · 대시보드] 도킹 창을 열거나 닫는다 (등록·자료 = Dashboard.cs). 모델은 바꾸지 않음
using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    [Transaction(TransactionMode.ReadOnly)]
    public class DashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                Dashboard.Ensure();
                var p = data.Application.GetDockablePane(Dashboard.PaneId);
                if (p.IsShown()) p.Hide();
                else { p.Show(); Dashboard.Refresh(data.Application.ActiveUIDocument?.Document); }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                return App.Fail("대시보드", ex);
            }
        }
    }
}

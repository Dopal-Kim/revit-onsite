// [수정 | 동바리 구역 · 같은 층 보기] 고른 구역의 층만 검측 3D 단면 상자로 잘라 같은 층 구역끼리 비교 (ZoneFocus.SameFloor). 뷰 표시만 바뀜
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    [Transaction(TransactionMode.Manual)]
    public class SameFloorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            if (App.ProjectDoc(data) == null) return Result.Cancelled;
            ZoneFocus.SameFloor(data.Application);
            return Result.Succeeded;
        }
    }
}

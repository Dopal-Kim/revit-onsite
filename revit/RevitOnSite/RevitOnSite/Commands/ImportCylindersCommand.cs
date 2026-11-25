// [타설·데이터 · 데이터 가져오기 ▾ · 공시체 결과] / 상황별 탭 [공시체 입력] — 현장양생 공시체 입력 창 (PourCommand.cs 의 DataEntry, 휴대폰 [공시체]와 같은 검사)
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    [Transaction(TransactionMode.Manual)]
    public class ImportCylindersCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) => DataEntry.Run(data, "cylinder");
    }
}

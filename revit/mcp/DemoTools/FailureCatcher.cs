// 개발용: 트랜잭션 실패를 대화상자 없이 잡아 문구를 모으고 되돌림 (MCP 재현 시험용)
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace DemoTools
{
    public class FailureCatcher : IFailuresPreprocessor
    {
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();

        public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
        {
            foreach (var f in a.GetFailureMessages().ToList())
            {
                if (f.GetSeverity() == FailureSeverity.Warning) { Warnings.Add(f.GetDescriptionText()); a.DeleteWarning(f); }
                else Errors.Add(f.GetSeverity() + ": " + f.GetDescriptionText());
            }
            return Errors.Count > 0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}

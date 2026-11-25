// [보고·공유 · 웹 공유판] Revit 없이 브라우저로 보는 웹 파일(HTML) 하나로 내보내기 — 형상·강도 예측 타임라인·날짜별 판정 = WebExport (계산은 엔진, 스키마 v2.0),
// 뷰어 = web/onsite_4d.html 사본의 데이터 블록에 JSON 을 넣음 (export_4d.py --html 과 같은 방식). 읽기 전용: 모델은 바꾸지 않음
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    [Transaction(TransactionMode.ReadOnly)]
    public class ExportWebCommand : IExternalCommand
    {
        // shortcut: 뷰어는 개발 PC 저장소의 web/onsite_4d.html 을 직접 가리킴 (도움말과 같은 방식, 총괄 지시). 다른 PC 에 배포하면 애드인 폴더로 복사해 App.Dir 기준으로
        const string Viewer = @"C:\Users\admin\Desktop\시스템2\build-up-2\web\onsite_4d.html";

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            if (!ShoringModel.SettingsDone(doc))
            {
                App.Message("첫 타설일이 없습니다.", "[프로젝트 설정]에서 첫 타설일과 타설 사이클을 정한 뒤 다시 누르세요.");
                return Result.Cancelled;
            }
            if (!ShoringModel.Zones(doc).Any()) { App.Message("구역이 없습니다.", "[구역·동바리 생성]을 먼저 누르세요."); return Result.Cancelled; }
            if (!File.Exists(Viewer)) { App.Message("웹 뷰어 파일을 찾지 못했습니다.", Viewer, TaskDialogIcon.TaskDialogIconWarning); return Result.Cancelled; }

            bool saved = !string.IsNullOrEmpty(doc.PathName);
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "웹 공유판 저장", Filter = "웹 페이지 (*.html)|*.html", DefaultExt = ".html", AddExtension = true,
                InitialDirectory = saved ? Path.GetDirectoryName(doc.PathName) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                FileName = (saved ? Path.GetFileNameWithoutExtension(doc.PathName) : doc.Title) + "_4d.html",
            };
            if (dlg.ShowDialog() != true) return Result.Cancelled;
            try
            {
                var notes = new List<string>();
                var (json, summary) = WebExport.Build(WebExport.Collect(doc, notes), DateTime.Now);
                var utf8 = new UTF8Encoding(false);
                File.WriteAllText(dlg.FileName, WebExport.Html(File.ReadAllText(Viewer, utf8), json), utf8);
                App.OpenInBrowser(dlg.FileName);   // 66차 점검 ⑮: 저장 뒤 바로 Chrome 으로 (없으면 기본 브라우저)
                App.Message("웹 공유판을 저장하고 브라우저로 열었습니다.",
                    dlg.FileName + "\n\n" + summary + (notes.Count > 0 ? "\n" + string.Join("\n", notes) : "")
                    + "\n\n브라우저로 열면 Revit 없이 날짜별 추정 강도(참고)와 기록 판정을 볼 수 있습니다. 결론은 '해체 승인 요청 가능' / '존치' 까지입니다.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                return App.Fail("웹 공유판 내보내기", ex);
            }
        }
    }
}

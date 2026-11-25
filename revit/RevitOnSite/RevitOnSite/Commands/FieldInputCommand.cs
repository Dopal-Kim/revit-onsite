// [타설·데이터 · 현장 입력] 휴대폰 실측값 입력 서버를 켜고 창(주소·QR)을 연다. 이미 켜져 있으면 창만 앞으로. 서버·모델 반영 = FieldInput.cs
using System;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    [Transaction(TransactionMode.Manual)]   // 켜는 것만으로는 모델을 바꾸지 않음 — 입력을 받을 때 FieldInput.Apply 가 App.Write
    public class FieldInputCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            if (FieldInputWindow.BringToFront()) return Result.Succeeded;
            return App.ProjectDoc(data) == null ? Result.Cancelled : Open(data.Application);
        }

        /// <summary>서버 켜고 창 열기 (리본 버튼·대시보드 칸 우클릭 [동바리 점검 열기] 공통, API 문맥)</summary>
        internal static Result Open(UIApplication app)
        {
            if (FieldInputWindow.BringToFront()) return Result.Succeeded;
            if (app.ActiveUIDocument?.Document is not { IsFamilyDocument: false } d) { App.Message("프로젝트 파일을 연 상태에서 눌러 주세요."); return Result.Cancelled; }
            if (ServerLink.Configured)   // 11-19 총괄 "Vercel로 통일": 서버 주소·키가 있으면 서버 연동 QR (켜면 QR 창도 열림). 없을 때만 같은 Wi-Fi 서버 (인터넷 안 되는 현장 예비)
            {
                if (!ServerLink.On) return ServerLinkCommand.TurnOn(app);
                ServerQrWindow.Open(app, ProjectSettings.Read(d).CsvFolder);
                return Result.Succeeded;
            }
            try
            {
                FieldInput.Start(app);
                var w = new FieldInputWindow(FieldInput.Server) { ViewArea = ViewArea(app) };
                new WindowInteropHelper(w).Owner = app.MainWindowHandle;
                w.ShowActivated = false;   // 키보드는 Revit 에 남김 (결과 창과 같음) — Revit 단축키·Esc 가 이 창으로 가지 않게
                w.Show();   // 모달 아님: 열어 둔 채 Revit 작업 가능
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                FieldInput.Stop();
                return App.Fail("현장 입력 켜기", ex);
            }
        }

        /// <summary>활성 뷰 창의 화면 사각형 (픽셀) — 리본·도킹 창(대시보드)을 뺀 모델 뷰 영역. 창 처음 위치용 (29차 총괄), 못 얻으면 null</summary>
        static (int, int, int, int)? ViewArea(UIApplication app)
        {
            try
            {
                var uidoc = app.ActiveUIDocument;
                var views = uidoc?.GetOpenUIViews();
                var uv = views?.FirstOrDefault(v => v.ViewId == uidoc.ActiveView?.Id) ?? views?.FirstOrDefault();
                if (uv == null) return null;
                var r = uv.GetWindowRectangle();
                return (r.Left, r.Top, r.Right, r.Bottom);
            }
            catch (Exception) { return null; }   // 위치만 정하는 데 씀 — 실패하면 Revit 창 왼쪽 아래
        }
    }
}

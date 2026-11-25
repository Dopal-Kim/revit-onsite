// [타설·데이터 › 데이터 가져오기 ▾ › 센서 자료] 센서 업체 내보내기 CSV(CP949·UTF-8, 열 이름이 업체마다 다름)를 데이터 폴더 sensors.csv(온도)·sensor_strength.csv(강도)에 합친다 (benchmark_gap G5).
// 계산은 Engine.SensorImport (= shoring/sensor_import.py). 위치가 파일에 없으면 기존 같은 센서 ID 위치, 처음 보는 ID 는 가져오지 않고 목록. 판정·모델은 바꾸지 않음 (다음 [판정] 때 반영).
using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitOnSite.Engine;

namespace RevitOnSite
{
    [Transaction(TransactionMode.ReadOnly)]
    public class ImportSensorsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try
            {
                var s = ProjectSettings.Read(doc);
                s.EnsureFolder();   // 87차: 기본 폴더(rvt 옆 onsite_data_<이름>)가 없으면 만듦
                if (string.IsNullOrWhiteSpace(s.CsvFolder) || !Directory.Exists(s.CsvFolder))
                {
                    App.Message("데이터 폴더가 없습니다.", "[프로젝트 설정]에서 데이터 폴더를 고르세요.");
                    return Result.Cancelled;
                }
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "센서 업체 내보내기 CSV 고르기 — 센서 ID·측정 시각·온도(또는 강도) 열이 있는 파일",
                    Filter = "CSV 파일 (*.csv)|*.csv|모든 파일 (*.*)|*.*",
                };
                if (dlg.ShowDialog() != true) return Result.Cancelled;
                var r = SensorImport.Import(dlg.FileName, s.CsvFolder);
                static string F((int New, int Changed)? v) => v is { } x ? $"{x.New}줄 새로 · {x.Changed}줄 바뀜" : "열 없음";
                App.ShowResult(data.Application, "센서 자료",
                    $"{SensorImport.Source}: {r.Rows}줄 — 온도 {F(r.Temp)} · 강도 {F(r.Mpa)}" + (r.NoPosition.Count > 0 ? $" · 위치 없음 {r.NoPosition.Count}" : ""),
                    $"찾은 열: {string.Join(" · ", r.Columns.Select(kv => kv.Value + " → " + kv.Key))}\n파일: {dlg.FileName}\n"
                    + (r.Bad > 0 ? $"건너뛴 줄 {r.Bad} (시각을 못 읽거나 값이 빔)\n" : "")
                    + (r.NoPosition.Count > 0 ? $"경고: 위치 없음 — {string.Join(", ", r.NoPosition)} — 처음 보는 센서라 가져오지 않았습니다. 현장 입력에서 위치를 먼저 넣은 뒤 다시 가져오세요.\n" : "")
                    + "같은 센서·같은 시각은 한 줄입니다(값이 다르면 새 값). 다음 [판정]부터 반영되며, 판정·모델은 지금 바꾸지 않았습니다.");
                Dashboard.Log($"센서 자료: {dlg.FileName} → 온도 {F(r.Temp)} · 강도 {F(r.Mpa)} · 위치 없음 {r.NoPosition.Count} · 건너뜀 {r.Bad}");
                return Result.Succeeded;
            }
            catch (InvalidDataException ex)
            {
                App.Message("센서 자료를 읽지 못했습니다.", ex.Message.Replace("--map \"열 이름=역할\" 로 지정하세요.", "열 이름에 '센서·시각·온도(또는 강도)'(sensor·time·temp) 가 들어가게 고치거나, python shoring\\sensor_import.py <파일> <데이터 폴더> --map \"열 이름=역할\" 로 가져오세요."));
                return Result.Cancelled;
            }
            catch (Exception ex) { return App.Fail("센서 자료", ex); }
        }
    }
}

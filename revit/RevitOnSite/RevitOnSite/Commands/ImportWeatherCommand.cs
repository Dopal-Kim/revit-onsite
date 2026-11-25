// [타설·데이터 · 기상 자료] 기상자료개방포털에서 내려받은 ASOS CSV(일자료 '평균기온' 또는 시간자료 '기온', CP949·UTF-8)를 골라
// 데이터 폴더 weather.csv(date, avgTa, source)에 합친다 — Engine.WeatherImport = shoring/weather_import.py (F8). 인증키 없이 파일로.
// 같은 날짜는 새 값으로, 나머지 줄은 그대로. 판정은 바꾸지 않는다 (다음 [판정]부터 센서 없는 구역 근거에 "기상청 ASOS … [입력]"). 모델은 바꾸지 않는다.
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
    public class ImportWeatherCommand : IExternalCommand
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
                    Title = "기상청 ASOS 자료 (CSV) 고르기 — 기상자료개방포털 › 종관기상관측(ASOS) 일자료 또는 시간자료",
                    Filter = "CSV 파일 (*.csv)|*.csv|모든 파일 (*.*)|*.*",
                };
                if (dlg.ShowDialog() != true) return Result.Cancelled;
                var (temps, source) = WeatherImport.ReadAsos(dlg.FileName);
                var (n, c) = WeatherImport.Merge(s.CsvFolder, temps, source);
                var days = temps.Keys.OrderBy(d => d, StringComparer.Ordinal).ToList();
                var path = Path.GetFullPath(Path.Combine(s.CsvFolder, "weather.csv"));
                App.ShowResult(data.Application, "기상 자료", $"{source}: {temps.Count}일 — {n}일 새로 · {c}일 바뀜",
                    $"기간 {days[0]} – {days[^1]} (결측 날짜는 건너뜀 → 판정이 평년 기온으로 채움)\n파일: {dlg.FileName}\nweather.csv: {path}\n\n"
                    + "다음 [판정]부터 센서 없는 구역의 근거에 이 출처가 나옵니다 (출처가 섞이면 '기상 기록'). 판정·모델은 지금 바꾸지 않았습니다.");
                Dashboard.Log($"기상 자료: {source} {temps.Count}일 → weather.csv ({n} 새로 · {c} 바뀜)");
                return Result.Succeeded;
            }
            catch (InvalidDataException ex) { App.Message("기상 자료를 읽지 못했습니다.", ex.Message); return Result.Cancelled; }
            catch (Exception ex) { return App.Fail("기상 자료", ex); }
        }
    }
}

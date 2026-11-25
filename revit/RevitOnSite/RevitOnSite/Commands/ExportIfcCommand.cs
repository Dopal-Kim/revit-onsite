// [보고·공유 · IFC 내보내기] D3 (direction_log 3판) — 감리가 Revit 없이 무료 IFC 뷰어(BIMvision 등)에서 구역별 판정을 보게.
// Revit IFC 내보내기(IFC4 Reference View) + 사용자 정의 속성 세트 파일 revit\ifc\Pset_RevitOnSite.txt
//   (구역 Pset_RevitOnSite_Zone 13 속성 · 동바리 Pset_RevitOnSite_Shore 3 속성 = 이미 있는 SHR_ 공유 매개변수만, 총괄 초안).
// 매개변수가 아닌 값(근거 n/8·마감 값·판정 시각·무단 해체 의심·경고)은 IFC 옆 같은 이름 .csv 로 쓰고, 그 값을 Engine.IfcEvidence(= tools/ifc_add_evidence.py)로
// 같은 IFC 안 구역마다 Pset_RevitOnSite_Evidence 로 덧붙인다 (SHR_Ev* 매개변수 추가 보류 — 모델에는 안 씀).
// 결과: 데이터 폴더 RevitOnSite_YYYYMMDD.ifc · RevitOnSite_YYYYMMDD_근거.csv · RevitOnSite_YYYYMMDD_Pset.txt (YYYYMMDD = 마지막 판정 기준일).
// 내보내기는 트랜잭션 안에서 하고 되돌림 — 모델은 바뀌지 않는다. 검사: python tools\check_ifc.py <ifc> (Pset 이름·구역 Decision 값).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitOnSite.Engine;

namespace RevitOnSite
{
    [Transaction(TransactionMode.Manual)]
    public class ExportIfcCommand : IExternalCommand
    {
        // shortcut: 매핑 파일은 개발 PC 저장소 경로 (도움말·웹 뷰어와 같은 방식, 총괄 지시) — 다른 PC 에 배포하면 애드인 폴더로 복사해 App.Dir 기준으로
        const string PsetFile = @"C:\Users\admin\Desktop\시스템2\build-up-2\revit\ifc\Pset_RevitOnSite.txt";

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            var doc = App.ProjectDoc(data);
            if (doc == null) return Result.Cancelled;
            try
            {
                var s = ProjectSettings.Read(doc);
                if (!EvaluateCommand.HasPours(s)) return Result.Cancelled;
                if (ZoneEvaluation.LastEvalAt(doc) is not DateTime at) { App.Message("판정 기록이 없습니다.", "[판정]을 먼저 하세요 — IFC 속성은 마지막 판정이 모델에 쓴 값입니다."); return Result.Cancelled; }
                IfcOut o;
                try { o = Export(doc, s, at); }
                catch (InvalidOperationException ex) { App.Message("IFC 내보내기에 실패했습니다.", ex.Message); return Result.Failed; }
                string name = Path.GetFileNameWithoutExtension(o.Ifc), ifc = o.Ifc, csv = o.Csv, pset = o.Pset, evNote = o.EvNote;
                int n = o.Zones, shoreCount = o.Shores, asMember = o.AsMember, sensorCount = o.Sensors, asSensor = o.AsSensor;
                long kb = new FileInfo(ifc).Length / 1024;
                App.ShowResult(data.Application, "IFC 내보내기", $"{name}.ifc ({kb:N0} KB) · 구역 {n}개 — 속성 세트 Pset_RevitOnSite_Zone",
                    $"IFC: {ifc}\n곁 CSV: {csv}\n속성 세트 매핑: {pset}\n\n"
                    + "IFC 속성(모델 SHR_ 매개변수): 구역 ID·층·결론·사유·상태·예측 해체일·타설일·배합·fck·공시체·구역 최소 추정·필요 존치 층수 / 동바리: 형식·검측일·검측 결과.\n"
                    + evNote + " — 같은 값을 _근거.csv 에도 (구역 ID 로 맞춤).\n"
                    + $"동바리 {shoreCount}본 중 {asMember}본을 내보내는 동안만 IfcMember 로 (Pset_RevitOnSite_Shore), 센서 표식 {sensorCount}개 중 {asSensor}개 IfcSensor (구역 세트 안 붙음) — 구역은 IfcBuildingElementProxy (Pset_RevitOnSite_Zone).\n"
                    + $"확인: python tools\\check_ifc.py \"{ifc}\" --zones {ShoringModel.Zones(doc).Count()} --shores {shoreCount}\n"
                    + "무료 IFC 뷰어(BIMvision 등)에서 구역을 누르면 Pset_RevitOnSite_Zone 이 보입니다. 모델은 바뀌지 않았습니다.");
                Dashboard.Log($"IFC 내보내기: {ifc} ({kb} KB) · 곁 CSV {n}구역 · {evNote}");
                return Result.Succeeded;
            }
            catch (Exception ex) { return App.Fail("IFC 내보내기", ex); }
        }

        internal sealed record IfcOut(string Ifc, string Csv, string Pset, int Zones, int Shores, int AsMember, int Sensors, int AsSensor, string EvNote);

        /// <summary>IFC + 곁 CSV + 근거 속성 (데이터 폴더 RevitOnSite_YYYYMMDD.*) — [IFC 내보내기]·[검측 패키지](11-12 2-6)가 같이 씀. 트랜잭션 안에서 하고 되돌림.
        /// 실패하면 InvalidOperationException (사유 문구). ReadOnly 명령에서 부르면 안 됨 (트랜잭션 필요)</summary>
        internal static IfcOut Export(Document doc, ProjectSettings s, DateTime at)
        {
            if (!File.Exists(PsetFile)) throw new InvalidOperationException("속성 세트 파일이 없습니다: " + PsetFile);
            string name = $"RevitOnSite_{at:yyyyMMdd}", folder = s.CsvFolder;
            var pset = Path.GetFullPath(Path.Combine(folder, name + "_Pset.txt"));
            File.Copy(PsetFile, pset, true);   // 받는 쪽이 어떤 매개변수가 어떤 속성으로 갔는지 같이 보게

            var opt = new IFCExportOptions { FileVersion = IFCVersion.IFC4RV };   // IFC4 스키마 · Reference View (보기·검토용)
            opt.AddOption("ExportUserDefinedPsets", "true");
            opt.AddOption("ExportUserDefinedPsetsFileName", pset);
            bool ok;
            int asMember = 0, shoreCount = 0, asSensor = 0, sensorCount = 0;
            using (var t = new Transaction(doc, "Revit On-Site: IFC 내보내기"))
            {
                t.Start();
                // 동바리도 SHR_ZoneID 가 있어 구역 세트(프록시)가 붙던 것 (22차: 906개) → 내보내는 동안만 IfcExportAs = IfcMember, 매핑 파일 Shore 세트 = IfcMember
                foreach (var sh in ShoringModel.Shores(doc))
                {
                    shoreCount++;
                    var p = sh.get_Parameter(BuiltInParameter.IFC_EXPORT_ELEMENT_AS) ?? sh.LookupParameter("IfcExportAs");
                    if (p != null && !p.IsReadOnly && p.Set("IfcMember")) asMember++;
                }
                // 60차 R17: 센서 표식(SNS_Sensor 일반 모델)도 프록시라 ZoneID 없는 빈 구역 세트가 붙던 것 (96 = 90 + 6) → 내보내는 동안만 IfcSensor
                foreach (var sn in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().Where(f => f.Symbol?.FamilyName == "SNS_Sensor"))
                {
                    sensorCount++;
                    var p = sn.get_Parameter(BuiltInParameter.IFC_EXPORT_ELEMENT_AS) ?? sn.LookupParameter("IfcExportAs");
                    if (p != null && !p.IsReadOnly && p.Set("IfcSensor")) asSensor++;
                }
                ok = doc.Export(folder, name + ".ifc", opt);
                t.RollBack();   // IFC 내보내기는 열린 트랜잭션이 필요하고 문서를 잠깐 바꿀 수 있음 + 위 IfcExportAs — 되돌려 모델은 그대로
            }
            var ifc = Path.GetFullPath(Path.Combine(folder, name + ".ifc"));
            if (!ok || !File.Exists(ifc)) throw new InvalidOperationException("Revit IFC 내보내기(IFC for Revit)가 설치·활성화돼 있는지 확인하세요.\n" + ifc);

            var csv = Path.GetFullPath(Path.Combine(folder, name + "_근거.csv"));   // 곁 CSV 이름에 '근거' (총괄 11-05 ⑤)
            int n = WriteSide(csv, folder, at, s.CureMin);
            // 근거 속성을 IFC 안에도 (tools/ifc_add_evidence.py 와 같은 줄 — 총괄 11-05): 구역마다 Pset_RevitOnSite_Evidence, 곁 CSV 는 그대로
            string evNote;
            try
            {
                var (ev, miss) = IfcEvidence.Add(ifc, csv, ifc);
                evNote = $"근거 속성 Pset_RevitOnSite_Evidence {ev}구역 (근거 n/8·마감 값·판정 시각·경고, 값 있는 것만)" + (miss.Count > 0 ? $" — IFC 에 없는 구역 {miss.Count}개: {string.Join(", ", miss.Take(6))}" : "");
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { evNote = "근거 속성은 넣지 못함 (곁 CSV 에만): " + ex.Message; }
            return new IfcOut(ifc, csv, pset, n, shoreCount, asMember, sensorCount, asSensor, evNote);
        }

        /// <summary>곁 CSV:zone_id, decision, evidence_stage(n/8), seal(요청 가능 구역만 — 검측요청서 마감 값과 같은 값), eval_at, warnings(무단 해체 의심·근거 순서). 반환 = 줄 수</summary>
        internal static int WriteSide(string path, string folder, DateTime at, double cureMin)
        {
            var rows = Evaluator.Evaluate(folder, at, cureMin);
            var day = DateOnly.FromDateTime(at);
            var led = Evidence.LedgerAt(folder, day);
            var req = Approval.RequestRows(rows, folder, at).Where(r => r.Decision == "해체 승인 요청 가능").ToList();   // 검측요청서와 같은 구역 → 같은 마감 값
            string seal = req.Count > 0 ? Approval.SealValue(req, folder, at) : "";
            static string Q(string v) => v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
            var ids = rows.Select(r => r.ZoneId).Concat(led.Keys).Distinct().ToList();   // 판정 행 + 경고만 있는 구역 (무단 해체 관측 등)
            var dec = rows.GroupBy(r => r.ZoneId).ToDictionary(g => g.Key, g => g.Last().Decision);
            var sb = new StringBuilder("zone_id,decision,evidence_stage,seal,eval_at,warnings\r\n");
            foreach (var z in ids)
            {
                var l = led.GetValueOrDefault(z);
                sb.Append(string.Join(",", Q(z), Q(dec.GetValueOrDefault(z, "")), (l?.Stage ?? 0) + "/8", dec.GetValueOrDefault(z) == "해체 승인 요청 가능" ? seal : "",
                    at.ToString("yyyy-MM-dd'T'HH:mm", System.Globalization.CultureInfo.InvariantCulture), Q(string.Join(" / ", l?.Warnings ?? new List<string>())))).Append("\r\n");
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));   // BOM: 엑셀에서 한글이 깨지지 않게
            return ids.Count;
        }
    }
}

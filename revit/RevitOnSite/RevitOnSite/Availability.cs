// 버튼 사용 가능 조건 (ui_spec §2 "사용 가능" 열) — 조건이 안 맞으면 Revit 이 버튼을 회색으로 그린다.
// Revit 이 리본을 갱신할 때마다 부르므로 가볍게: 수집기는 빠른 필터 + 첫 일치에서 멈춘다. 모델은 읽기만.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    /// <summary>구역·동바리 판별 (ZoneGenerator 가 만든 요소 기준)</summary>
    public static class ShoringModel
    {
        public static bool IsZone(Element e) => e is DirectShape && !string.IsNullOrEmpty(e.LookupParameter("SHR_ZoneID")?.AsString());
        public static bool IsShore(Element e) => e is FamilyInstance fi && fi.Symbol?.FamilyName == ZoneGenerator.FamilyName;

        /// <summary>SHR_ZoneID 값이 있는 DirectShape. 값 검사는 Revit 내부 필터 — IFC 모델의 일반 모델 DirectShape 가 수천 개여도 하나씩 읽지 않음</summary>
        public static IEnumerable<Element> Zones(Document doc)
        {
            var p = new FilteredElementCollector(doc).OfClass(typeof(SharedParameterElement)).FirstOrDefault(e => e.Name == "SHR_ZoneID");
            if (p == null) return Enumerable.Empty<Element>();
            return new FilteredElementCollector(doc).OfClass(typeof(DirectShape)).OfCategory(BuiltInCategory.OST_GenericModel)
                .WherePasses(new ElementParameterFilter(ParameterFilterRuleFactory.CreateHasValueParameterRule(p.Id))).Where(IsZone);
        }

        /// <summary>SHR_PipeSupport 패밀리 인스턴스 (손으로 놓은 것 포함)</summary>
        public static IEnumerable<Element> Shores(Document doc)
        {
            var fam = (Family)new FilteredElementCollector(doc).OfClass(typeof(Family)).FirstOrDefault(f => f.Name == ZoneGenerator.FamilyName);
            return fam == null ? Enumerable.Empty<Element>()
                : fam.GetFamilySymbolIds().SelectMany(id => new FilteredElementCollector(doc).WherePasses(new FamilyInstanceFilter(doc, id)));
        }

        /// <summary>F16 동바리 ID — 모델 동바리(SHR_ZoneID 있는 것) 위치 m 를 구역마다 Engine.ShoreInspect.ShoreIds 로 (구역-R행C열, 0.1 m 로 묶음).
        /// 좌표 = zones.csv 와 같은 모델 좌표 (SyncZonesCsv). 같은 자리에 겹친 동바리는 같은 ID</summary>
        public static Dictionary<ElementId, (string Id, string ZoneId, double X, double Y)> ShoreIds(Document doc)
        {
            var pts = new List<(ElementId E, string Z, double X, double Y)>();
            static double M(double f) => Math.Round(UnitUtils.ConvertFromInternalUnits(f, UnitTypeId.Meters), 3) + 0.0;   // + 0.0: -0 → 0
            foreach (var e in Shores(doc))
                if (e.Location is LocationPoint lp && e.LookupParameter("SHR_ZoneID")?.AsString() is string z && z.Length > 0)
                    pts.Add((e.Id, z, M(lp.Point.X), M(lp.Point.Y)));
            var res = new Dictionary<ElementId, (string, string, double, double)>();
            foreach (var g in pts.GroupBy(p => p.Z))
            {
                var l = g.ToList();
                var ids = Engine.ShoreInspect.ShoreIds(g.Key, l.Select(p => (p.X, p.Y)).ToList());
                for (int i = 0; i < l.Count; i++) res[l[i].E] = (ids[i], l[i].Z, l[i].X, l[i].Y);
            }
            return res;
        }

        /// <summary>F16: 모델 동바리 → 데이터 폴더 shores.csv (id, zone_id, x, y, field_label) — 휴대폰 동바리 점검 평면·ID 검사용.
        /// 현장 이름표(field_label)는 같은 ID 의 기존 값을 유지. 샘플 폴더는 안 씀. 반환 = 기록 문구 (동바리 없으면 null)</summary>
        public static string WriteShoresCsv(Document doc, string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder) || Engine.Evaluator.IsSample(folder)) return null;
            var ids = ShoreIds(doc);
            if (ids.Count == 0) return null;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Dictionary<string, (string ZoneId, double X, double Y, string FieldLabel)> old;
            try { old = Engine.ShoreInspect.ReadShores(folder); }
            catch (Exception ex) when (ex is InvalidDataException or IOException) { old = new(); Dashboard.Log("옛 shores.csv 를 읽지 못해 이름표 없이 새로 씀: " + ex.Message); }   // 코드 검토 2차
            var label = old.ToDictionary(kv => kv.Key, kv => kv.Value.FieldLabel);
            static string Q(string v) => v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
            var rows = ids.Values.GroupBy(v => v.Id).Select(g => g.First()).OrderBy(v => v.ZoneId, StringComparer.Ordinal).ThenBy(v => v.Id, StringComparer.Ordinal).ToList();
            var sb = new System.Text.StringBuilder("id,zone_id,x,y,field_label\n");
            foreach (var v in rows)
                sb.Append(v.Id).Append(',').Append(v.ZoneId).Append(',').Append(v.X.ToString("0.###", inv)).Append(',').Append(v.Y.ToString("0.###", inv)).Append(',')
                  .Append(Q(label.GetValueOrDefault(v.Id, ""))).Append('\n');
            try { File.WriteAllText(Path.Combine(folder, Engine.ShoreInspect.Shores), sb.ToString(), new System.Text.UTF8Encoding(false)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return "shores.csv 가 다른 프로그램(Excel 등)에서 열려 있어 쓰지 못했습니다 — 예전 동바리 위치로 계속합니다 (" + ex.Message + ")";
            }
            // ID 는 좌표로 정해져 동바리를 다시 생성·이동하면 행·열 번호가 밀릴 수 있음 → 풀리지 않은 NG 의 동바리가 0.1 m 넘게 옮겨졌거나 없어졌으면 알림 (기록은 그대로)
            var now = rows.ToDictionary(v => v.Id);
            int moved = 0;
            try
            {
                foreach (var id in Engine.ShoreInspect.OpenNg(folder).SelectMany(kv => kv.Value.Select(x => x.Shore)).Where(x => x != "*").Distinct())
                    if (old.TryGetValue(id, out var o) && !(now.TryGetValue(id, out var n) && Math.Abs(n.X - o.X) <= 0.1 && Math.Abs(n.Y - o.Y) <= 0.1)) moved++;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException) { }
            return $"동바리 위치 {rows.Count}개 → shores.csv (휴대폰 동바리 점검 평면, 구역 {rows.Select(v => v.ZoneId).Distinct().Count()}개)"
                + (moved > 0 ? $" · ⚠ 풀리지 않은 NG 동바리 {moved}개의 위치가 바뀌었거나 없어짐 (동바리를 다시 생성하면 행·열 번호가 밀림) — 그 구역을 다시 점검해 기록 확인 필요" : "");
        }

        /// <summary>프로젝트 설정을 저장했는지 = PRJ_StartDate(첫 타설일, YYYYMMDD) 가 들어 있음</summary>
        public static bool SettingsDone(Document doc) => (doc.ProjectInformation?.LookupParameter("PRJ_StartDate")?.AsInteger() ?? 0) > 19000101;
    }

    /// <summary>기본 = 프로젝트 문서가 열려 있음 (패밀리 편집기·문서 없음 → 회색)</summary>
    public abstract class Avail : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication app, CategorySet selectedCategories)
        {
            var ui = app.ActiveUIDocument;
            return ui?.Document != null && !ui.Document.IsFamilyDocument && Ok(ui, ui.Document);
        }
        protected virtual bool Ok(UIDocument ui, Document doc) => true;
    }

    public class ProjectDocAvail : Avail { }

    /// <summary>항상 (문서가 없어도) — 대시보드 창 열기·닫기</summary>
    public class AlwaysAvail : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication app, CategorySet selectedCategories) => true;
    }

    public class SettingsDoneAvail : Avail { protected override bool Ok(UIDocument ui, Document doc) => ShoringModel.SettingsDone(doc); }
    public class ParamsBoundAvail : Avail { protected override bool Ok(UIDocument ui, Document doc) => doc.ProjectInformation?.LookupParameter("PRJ_CycleDays") != null; }
    public class ShoresAvail : Avail { protected override bool Ok(UIDocument ui, Document doc) => ShoringModel.Shores(doc).Any(); }
    /// <summary>v6 2단계: 활성 뷰가 동바리 층 입력 뷰 ('Revit On-Site 동바리 입력 – 층')</summary>
    public class InputViewAvail : Avail { protected override bool Ok(UIDocument ui, Document doc) => ui.ActiveView is ViewPlan v && v.Name.StartsWith(ShoreInputTools.ViewPrefix, StringComparison.Ordinal); }
    public class ZonesAvail : Avail { protected override bool Ok(UIDocument ui, Document doc) => ShoringModel.Zones(doc).Any(); }

    /// <summary>타설 기록 = 구역 SHR_PourDate 또는 데이터 폴더의 pours.csv (Evaluator 는 CSV 를 읽음)</summary>
    public class PourAvail : Avail
    {
        protected override bool Ok(UIDocument ui, Document doc)
        {
            if (!ShoringModel.Zones(doc).Any()) return false;   // 구역(생성분) ≥ 1 그리고 데이터 폴더에 pours.csv (총괄 지시, 점검 M2)
            var folder = ProjectSettings.Read(doc).CsvFolder;   // 87차: 비었으면 기본 폴더 (다른 명령과 같게)
            return !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, "pours.csv"));
        }
    }

    public class DecisionAvail : Avail { protected override bool Ok(UIDocument ui, Document doc) => ShoringModel.Zones(doc).Any(z => !string.IsNullOrEmpty(z.LookupParameter("SHR_Decision")?.AsString())); }

    /// <summary>일정 = 프로젝트 설정(첫 타설일·사이클)으로 자동 계획 + 구역</summary>
    public class TimelineAvail : Avail { protected override bool Ok(UIDocument ui, Document doc) => ShoringModel.SettingsDone(doc) && ShoringModel.Zones(doc).Any(); }

    public class PlanOr3DAvail : Avail { protected override bool Ok(UIDocument ui, Document doc) => doc.ActiveView is ViewPlan || doc.ActiveView is View3D; }
    public class GraphicViewAvail : Avail { protected override bool Ok(UIDocument ui, Document doc) => doc.ActiveView is ViewPlan or View3D or ViewSection or ViewDrafting or ViewSheet; }
    public class ZoneSelectedAvail : Avail { protected override bool Ok(UIDocument ui, Document doc) => ui.Selection.GetElementIds().Any(id => ShoringModel.IsZone(doc.GetElement(id))); }
}

// 공유 매개변수 바인딩 (dynamo/00_setup.py 와 같은 규칙) + 프로젝트 설정(PRJ_*) 읽기·쓰기
using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitOnSite
{
    public static class ParameterSetup
    {
        /// <summary>
        /// SHR_SharedParameters.txt 의 모든 매개변수를 바인딩 (그룹 이름으로, 순서 무관): PRJ → 프로젝트 정보, SHR_Slab → 바닥·지붕, 나머지 → 일반 모델. 이미 있는 이름은 건너뜀.
        /// 열린 트랜잭션 안에서 호출. 반환 = 새로 연결한 이름 목록.
        /// </summary>
        public static List<string> Ensure(Document doc, string spFile = null)
        {
            spFile ??= Path.Combine(App.Dir, "SHR_SharedParameters.txt");
            var app = doc.Application;
            string old = app.SharedParametersFilename;
            var added = new List<string>();
            try
            {
                app.SharedParametersFilename = spFile;
                var spf = app.OpenSharedParameterFile()
                    ?? throw new InvalidOperationException("공유 매개변수 파일을 열 수 없습니다: " + spFile + "\n애드인 폴더에 파일이 있는지 확인하세요.");
                var existing = new HashSet<string>();
                var it = doc.ParameterBindings.ForwardIterator();
                while (it.MoveNext()) existing.Add(it.Key.Name);
                var generic = Cats(doc, BuiltInCategory.OST_GenericModel);
                var project = Cats(doc, BuiltInCategory.OST_ProjectInformation);
                var slab = Cats(doc, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs);
                foreach (DefinitionGroup g in spf.Groups)
                    foreach (Definition d in g.Definitions)
                        if (!existing.Contains(d.Name) &&
                            doc.ParameterBindings.Insert(d, app.Create.NewInstanceBinding(g.Name == "PRJ" ? project : g.Name == "SHR_Slab" ? slab : generic), GroupTypeId.Data))
                            added.Add(d.Name);
            }
            finally
            {
                app.SharedParametersFilename = old ?? "";   // 사용자 설정 원래대로
            }
            return added;
        }

        static CategorySet Cats(Document doc, params BuiltInCategory[] bics)
        {
            var cs = new CategorySet();
            foreach (var bic in bics) cs.Insert(Category.GetCategory(doc, bic));
            return cs;
        }
    }

    /// <summary>프로젝트 정보의 PRJ_* 값. 기본값은 정의서 §3.5</summary>
    public class ProjectSettings
    {
        public bool StartDateSet;                     // PRJ_StartDate 가 모델에 적혀 있나 (없으면 StartDate = 오늘 — [전용 계획]은 그때 타설 기록에서 거꾸로 셈)
        public DateTime StartDate = DateTime.Today;   // PRJ_StartDate (YYYYMMDD) = 첫 타설일: 가장 아래층(지하층이 있으면 지하) 슬래브 타설일, 공정 계획의 기준일 (착공일 아님) [입력]
        public int CycleDays = 7;                     // [입력] 층당 골조 사이클
        public int MinLevels = 3;                     // [확정] 가이드라인 2023: 타설층 포함 3개층 이상 존치
        public string StationId = "108";              // [입력] 기상청 ASOS 서울
        public double CureMin = 5.0;                  // [가정] 양생 하한 5 ℃(보온), 가열 양생이면 15
        public string CsvFolder = "";
        public bool CsvFolderSet;                     // PRJ_CSVFolder 에 값이 있나 — 없으면 CsvFolder = rvt 옆 onsite_data_<파일 이름> (87차, 모든 명령 같은 기본값)
        public string DefaultMix = "보통";            // PRJ_DefaultMix [입력] 구역 값이 없을 때 쓰는 배합 = 엔진 ShoringEngine.Mixes 키, 기본 보통 [가정]
        public double DefaultFck = 24;                // PRJ_DefaultFck [입력] 설계기준강도 MPa, 기본 24 [가정]
        public double? AlignTolMm;                    // PRJ_AlignTolMm [입력] 설치 검측 상하 정렬 허용값 mm — 없으면 편차만 (허용값은 지어내지 않음, F9)

        public static ProjectSettings Read(Document doc)
        {
            var s = new ProjectSettings();
            var pi = doc.ProjectInformation;
            int date = GetInt(pi, "PRJ_StartDate", 0);
            if (date > 19000101 && DateTime.TryParseExact(date.ToString(System.Globalization.CultureInfo.InvariantCulture), "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d))
            {
                s.StartDate = d;
                s.StartDateSet = true;
            }
            s.CycleDays = GetInt(pi, "PRJ_CycleDays", s.CycleDays);
            s.MinLevels = GetInt(pi, "PRJ_MinLevels", s.MinLevels);
            var p = pi.LookupParameter("PRJ_StationID");
            if (p != null && p.HasValue && !string.IsNullOrWhiteSpace(p.AsString())) s.StationId = p.AsString();
            p = pi.LookupParameter("PRJ_CureMin");
            if (p != null && p.HasValue) s.CureMin = p.AsDouble();
            p = pi.LookupParameter("PRJ_CSVFolder");
            if (p != null && p.HasValue && !string.IsNullOrWhiteSpace(p.AsString())) { s.CsvFolder = p.AsString(); s.CsvFolderSet = true; }
            else s.CsvFolder = ZoneSplit.DefaultFolder(doc);   // 87차 (총괄): rvt 폴더 자체는 같은 폴더의 다른 모델과 CSV 가 섞임 → [구역 나누기]와 같은 기본값
            p = pi.LookupParameter("PRJ_DefaultMix");
            if (p != null && p.HasValue && !string.IsNullOrWhiteSpace(p.AsString())) s.DefaultMix = p.AsString();
            p = pi.LookupParameter("PRJ_DefaultFck");
            if (p != null && p.HasValue && p.AsDouble() > 0) s.DefaultFck = p.AsDouble();
            p = pi.LookupParameter("PRJ_AlignTolMm");
            if (p != null && p.HasValue && p.AsDouble() > 0) s.AlignTolMm = p.AsDouble();
            return s;
        }

        /// <summary>87차: 쓰는 명령 앞 — 데이터 폴더가 기본값(저장 안 됨)이고 없으면 만듦 (만들면 대시보드 기록).
        /// 반환 = 결과 창에 붙일 폴더 한 줄, 사람이 정한 폴더면 null (없어도 만들지 않음 — 오타일 수 있음)</summary>
        public string EnsureFolder()
        {
            if (CsvFolderSet || string.IsNullOrWhiteSpace(CsvFolder)) return null;
            if (Directory.Exists(CsvFolder)) return "데이터 폴더(기본값): " + CsvFolder + " — [프로젝트 설정]에서 바꿀 수 있습니다";
            string line;
            try { Directory.CreateDirectory(CsvFolder); line = "데이터 폴더(기본값)를 만들었습니다: " + CsvFolder + " — [프로젝트 설정]에서 바꿀 수 있습니다"; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { line = "데이터 폴더(기본값)를 만들지 못했습니다: " + CsvFolder + " (" + ex.Message + ")"; }
            Dashboard.Log(line);
            return line;
        }

        /// <summary>열린 트랜잭션 안에서 호출. 매개변수가 없으면 그 이름 목록을 돌려줌</summary>
        public List<string> Write(Document doc)
        {
            var pi = doc.ProjectInformation;
            var missing = new List<string>();
            void Set(string name, Func<Parameter, bool> set)
            {
                var p = pi.LookupParameter(name);
                if (p == null || p.IsReadOnly || !set(p)) missing.Add(name);
            }
            Set("PRJ_StartDate", p => p.Set(int.Parse(StartDate.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture)));
            Set("PRJ_CycleDays", p => p.Set(CycleDays));
            Set("PRJ_MinLevels", p => p.Set(MinLevels));
            Set("PRJ_StationID", p => p.Set(StationId ?? ""));
            Set("PRJ_CureMin", p => p.Set(CureMin));
            Set("PRJ_CSVFolder", p => p.Set(CsvFolder ?? ""));
            Set("PRJ_DefaultMix", p => p.Set(DefaultMix ?? "보통"));
            Set("PRJ_DefaultFck", p => p.Set(DefaultFck));
            if (AlignTolMm != null || pi.LookupParameter("PRJ_AlignTolMm") != null)   // 값이 없고 매개변수도 없으면 '연결 필요'로 알리지 않음
                Set("PRJ_AlignTolMm", p => p.Set(AlignTolMm ?? 0));
            return missing;
        }

        static int GetInt(Element e, string name, int fallback)
        {
            var p = e.LookupParameter(name);
            return p != null && p.HasValue && p.StorageType == StorageType.Integer ? p.AsInteger() : fallback;
        }
    }
}

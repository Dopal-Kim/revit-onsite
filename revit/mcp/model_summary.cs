// 모델 요약 (읽기만): 구역 수·층 접두별, 동바리 수, 프로젝트 설정 몇 가지
string P(Element e, string n) { var p = e.LookupParameter(n); return p == null || !p.HasValue ? "" : (p.StorageType == StorageType.String ? p.AsString() : p.AsValueString()); }
var ds = new FilteredElementCollector(document).OfClass(typeof(DirectShape)).Cast<Element>().ToList();
var zones = ds.Where(e => P(e, "SHR_Type") == "구역").ToList();
var byPrefix = zones.GroupBy(z => (P(z, "SHR_ZoneID") ?? "").Split('-')[0]).OrderBy(g => g.Key).Select(g => g.Key + ":" + g.Count());
var shores = new FilteredElementCollector(document).WhereElementIsNotElementType().Where(e => P(e, "SHR_ZoneID") != "" && P(e, "SHR_Type") != "구역").Count();
var pi = document.ProjectInformation;
return new { doc = document.Title, zones = zones.Count, prefixes = string.Join(" ", byPrefix), shores,
    start = P(pi, "PRJ_StartDate"), csv = P(pi, "PRJ_CSVFolder"), cycle = P(pi, "PRJ_Cycle"), mix = P(pi, "PRJ_DefaultMix") };

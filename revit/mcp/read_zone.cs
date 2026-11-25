// 구역 DirectShape 의 판정 매개변수 읽기 (읽기만). parameters: [0] 구역 ID 접두 (예 "9F-")
var pre = (string)parameters[0];
string P(Element e, string n) { var p = e.LookupParameter(n); return p == null ? "-" : (p.StorageType == StorageType.String ? p.AsString() : p.AsValueString()); }
return new FilteredElementCollector(document).OfClass(typeof(DirectShape)).Cast<Element>()
    .Where(e => (P(e, "SHR_Type") ?? "") == "구역" && (P(e, "SHR_ZoneID") ?? "").StartsWith(pre))
    .OrderBy(e => P(e, "SHR_ZoneID"))
    .Select(e => P(e, "SHR_ZoneID") + " | " + P(e, "SHR_Status") + " | " + P(e, "SHR_Reason")).ToList();

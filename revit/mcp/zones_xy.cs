// 모델 구역 상자 평면 범위 (m, 프로젝트 좌표) — zones.csv 와 비교하는 읽기 전용 점검 (N5)
var outp = new List<string>();
foreach (var e in new FilteredElementCollector(document).OfClass(typeof(DirectShape)).Cast<Element>())
{
    var id = e.LookupParameter("SHR_ZoneID")?.AsString();
    if (string.IsNullOrEmpty(id) || e.LookupParameter("SHR_Type")?.AsString() != "구역") continue;
    var bb = e.get_BoundingBox(null);
    if (bb == null) continue;
    double M(double f) => Math.Round(UnitUtils.ConvertFromInternalUnits(f, UnitTypeId.Meters), 3);
    var fl = e.LookupParameter("SHR_Floor");
    outp.Add($"{id},{(fl != null && fl.HasValue ? fl.AsInteger() : 0)},{M(bb.Min.X)},{M(bb.Min.Y)},{M(bb.Max.X)},{M(bb.Max.Y)}");
}
outp.Sort(StringComparer.Ordinal);
return new List<string> { "구역 " + outp.Count + "개 · 문서 " + document.Title }.Concat(outp).ToList();

// 구역 한 개의 SHR_ 매개변수와 상자 (읽기만)
var z = new FilteredElementCollector(document).OfClass(typeof(DirectShape)).Cast<Element>()
    .FirstOrDefault(e => { var p = e.LookupParameter("SHR_Type"); return p != null && p.AsString() == "구역"; });
if (z == null) return "구역 없음";
var bb = z.get_BoundingBox(null);
var ps = z.Parameters.Cast<Parameter>().Where(p => p.Definition.Name.StartsWith("SHR_")).Select(p => p.Definition.Name + "=" + (p.StorageType == StorageType.String ? p.AsString() : p.AsValueString())).ToList();
return new { ps, min = bb.Min.ToString(), max = bb.Max.ToString() };

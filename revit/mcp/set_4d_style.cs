// 4D 미리보기 뷰 표시 방식 바꾸기 (사본 점검용). parameters: [0] Realistic|Shading|FlatColors|ShadingWithEdges. transactionMode auto
var name = System.IO.Path.GetFileName(document.PathName);
if (name == "shoring_demo.rvt" || name == "shoring_demo_onsite.rvt") return "거부: " + name;
var pv = new FilteredElementCollector(document).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(v => !v.IsTemplate && v.Name == "Revit On-Site 4D 미리보기");
if (pv == null) return "미리보기 뷰 없음";
var before = pv.DisplayStyle;
pv.DisplayStyle = (DisplayStyle)Enum.Parse(typeof(DisplayStyle), (string)parameters[0]);
return before + " → " + pv.DisplayStyle;

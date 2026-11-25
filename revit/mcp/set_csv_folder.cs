// 프로젝트 정보 PRJ_CSVFolder 를 바꿈 (사본에서만 — 원본 shoring_demo.rvt·shoring_demo_onsite.rvt 는 거부). transactionMode auto.
// parameters: [0] 새 폴더 절대 경로 (zones.csv 가 있어야 함)
var folder = (string)parameters[0];
var name = System.IO.Path.GetFileName(document.PathName);
if (name == "shoring_demo.rvt" || name == "shoring_demo_onsite.rvt") return "거부: 원본/시연 파일 " + name;
if (!System.IO.File.Exists(System.IO.Path.Combine(folder, "zones.csv"))) return "거부: zones.csv 없음 " + folder;
var p = document.ProjectInformation.LookupParameter("PRJ_CSVFolder");
if (p == null) return "PRJ_CSVFolder 매개변수 없음";
var before = p.AsString();
p.Set(folder);
return name + ": " + before + " → " + p.AsString();

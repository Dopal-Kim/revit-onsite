// dynamo/00_setup.py 와 같은 로직 (C#) — 사본 모델을 백그라운드로 열어 실행·저장
var app = document.Application;
string rvt = (string)parameters[0], spPath = (string)parameters[1];
var log = new List<string>(); Document pd = null;
string oldPath = app.SharedParametersFilename;
try {
    pd = app.OpenDocumentFile(rvt);
    app.SharedParametersFilename = spPath;
    var spf = app.OpenSharedParameterFile();
    if (spf == null) return "공유 매개변수 파일을 열 수 없음";
    var existing = new HashSet<string>();
    var it = pd.ParameterBindings.ForwardIterator(); while (it.MoveNext()) existing.Add(it.Key.Name);
    Func<BuiltInCategory, CategorySet> cats = bic => { var cs = new CategorySet(); cs.Insert(Category.GetCategory(pd, bic)); return cs; };
    var generic = cats(BuiltInCategory.OST_GenericModel); var project = cats(BuiltInCategory.OST_ProjectInformation);
    var added = new List<string>(); var skipped = new List<string>(); var groups = new List<string>();
    using (var t = new Transaction(pd, "00_Setup")) {
        t.Start();
        foreach (DefinitionGroup g in spf.Groups) {
            groups.Add(g.Name + ":" + g.Definitions.Size);
            foreach (Definition d in g.Definitions) {
                if (existing.Contains(d.Name)) { skipped.Add(d.Name); continue; }
                if (pd.ParameterBindings.Insert(d, app.Create.NewInstanceBinding(g.Name == "PRJ" ? project : generic), GroupTypeId.Data)) added.Add(d.Name);
                else log.Add("바인딩 실패 " + d.Name);
            }
        }
        t.Commit();
    }
    pd.Save();
    return new { added = added.Count, skipped = skipped.Count, groups, names = added, log };
} catch (Exception ex) { return "ERR " + (ex.InnerException ?? ex).ToString(); }
finally { app.SharedParametersFilename = oldPath ?? ""; if (pd != null && pd.IsValidObject) pd.Close(false); }

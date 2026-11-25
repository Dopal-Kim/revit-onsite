// 웹 공유판 일치 시험: WebExport(애드인의 Revit 없는 부분)에 export_4d.py 와 같은 데모 입력을 넣어 Python JSON 과 대조.
//   입력 = Python JSON 의 층·부재 형상(가상 배치 그대로, 벽·창호·계단은 층·구역을 비워 WebExport.Assign 이 정하게) + shoring/sample/data + 같은 설정
//   대조 = 구역 ID, 날짜별 구역 판정 전부 (2026-12-31 09:00 9층 A1·B1·C1·C2 요청 가능, A2·B2 존치), 공정 행, 강도 곡선, 층 요약, 경고,
//          부재 층·구역 배정, 벽 나누기(SplitWall = export_4d.py facade·창대 벽·창호 판), 뷰어 HTML 에 넣기. 불일치면 종료 코드 1.
// 실행: dotnet run --project revit\RevitOnSite\RevitOnSite.WebExport.Check [-- --expected <export_4d.py 결과 json> --python <exe> --html <C# 결과 html>]
//   --expected 가 없으면 .venv\Scripts\python.exe shoring\export_4d.py 를 직접 실행 (약 30초)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using RevitOnSite;
using RevitOnSite.Engine;

Console.OutputEncoding = Encoding.UTF8;
if (Array.IndexOf(args, "--culture") is var ci && ci >= 0 && ci + 1 < args.Length)   // 문화권 점검 (오류 점검 ②): --culture de-DE
    CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(args[ci + 1]);
var inv = CultureInfo.InvariantCulture;
int fails = 0;
void Check(bool ok, string what)
{
    if (!ok) { fails++; Console.WriteLine("FAIL " + what); }
}
string Arg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
string FindRoot()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "shoring", "export_4d.py")))
            return d.FullName;
    throw new DirectoryNotFoundException(@"shoring\export_4d.py 를 찾지 못함");
}
string root = FindRoot();
string expected = Arg("--expected");
if (expected is null)
{
    string python = Arg("--python") ?? Path.Combine(root, ".venv", "Scripts", "python.exe");
    expected = Path.Combine(Path.GetTempPath(), "onsite4d_expected.json");
    var psi = new ProcessStartInfo(python)
    {
        WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true,
        StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
    };
    foreach (var a in new[] { Path.Combine(root, "shoring", "export_4d.py"), "--out", expected }) psi.ArgumentList.Add(a);
    psi.Environment["PYTHONIOENCODING"] = "utf-8";
    Console.WriteLine($"Python: {python} shoring\\export_4d.py --out {expected}");
    using var p = Process.Start(psi)!;
    var err = p.StandardError.ReadToEndAsync();
    Console.Write("  " + p.StandardOutput.ReadToEnd());
    p.WaitForExit();
    if (p.ExitCode != 0) { Console.WriteLine("FAIL export_4d.py\n" + err.Result); return 1; }
}
var py = JsonNode.Parse(File.ReadAllText(expected, Encoding.UTF8))!.AsObject();

// ── 입력: export_4d.py 와 같은 데모 ──
var proj = py["project"]!;
string data = Path.Combine(root, "shoring", "sample", "data");
var x = new WebInput
{
    ProjectName = (string)proj["name"], ModelFile = (string)proj["model_file"], Source = "demo-python", Origin = (string)proj["origin"],
    Fck = (double)proj["fck_mpa"], MinLevels = (int)proj["min_levels"], CureMin = (double)proj["cure_min_c"], DataFolder = data,
};
x.Levels = py["levels"]!.AsArray().Select(l => new WebLevel((int)l["floor"], (string)l["name"], (double)l["z"], (double)l["slab_thickness"])).ToList();
static List<double[]> Fp(JsonNode g) => g.AsArray().Select(p => new[] { (double)p[0], (double)p[1] }).ToList();
foreach (var e in py["elements"]!.AsArray())
{
    string cat = (string)e["category"];
    bool free = cat is "wall" or "window" or "stair";
    var el = new WebElement
    {
        Id = (string)e["id"], Category = cat, Name = (string)e["name"], Floor = free ? null : (int)e["floor"], Zone = free ? null : (string)e["zone"],
        Footprint = Fp(e["geometry"]["footprint"]), Z0 = (double)e["geometry"]["z0"], Z1 = (double)e["geometry"]["z1"], Synthetic = (bool)e["synthetic"],
        Spacing = e["spacing_m"]?.AsArray().Select(v => (double)v).ToArray(),
    };
    x.Elements.Add(el);
    if (cat == "zone")
        x.Zones.Add(new WebZone(el.Id, el.Floor.Value, el.Footprint.Min(q => q[0]), el.Footprint.Min(q => q[1]), el.Footprint.Max(q => q[0]), el.Footprint.Max(q => q[1])));
}
int dropped = WebExport.Assign(x.Elements, x.Levels, x.Zones);
Check(dropped == 0, $"Assign 이 부재 {dropped}개를 뺌");

var sw = Stopwatch.StartNew();
var (json, summary) = WebExport.Build(x, DateTime.Now);
Console.WriteLine($"C#: {summary} ({sw.Elapsed.TotalSeconds:F1}초)");
var cs = JsonNode.Parse(json)!.AsObject();

// ── 대조 ──
static void Cmp(JsonNode a, JsonNode b, string path, List<string> diffs)
{
    if (a is null || b is null)
    {
        if (a is not null || b is not null) diffs.Add($"{path}: C# {a?.ToJsonString()} ≠ Python {b?.ToJsonString()}");
        return;
    }
    switch (a)
    {
        case JsonObject oa when b is JsonObject ob:
            foreach (var k in oa.Select(kv => kv.Key).Union(ob.Select(kv => kv.Key)))
                Cmp(oa[k], ob[k], path + "." + k, diffs);
            break;
        case JsonArray xa when b is JsonArray xb:
            if (xa.Count != xb.Count) diffs.Add($"{path}: 길이 C# {xa.Count} ≠ Python {xb.Count}");
            for (int i = 0; i < Math.Min(xa.Count, xb.Count); i++)
                Cmp(xa[i], xb[i], $"{path}[{i}]", diffs);
            break;
        case JsonValue va when b is JsonValue vb:
            bool same = va.GetValueKind() == JsonValueKind.Number && vb.GetValueKind() == JsonValueKind.Number
                ? Math.Abs(va.GetValue<double>() - vb.GetValue<double>()) <= 1e-9
                : va.ToJsonString() == vb.ToJsonString();
            if (!same) diffs.Add($"{path}: C# {va.ToJsonString()} ≠ Python {vb.ToJsonString()}");
            break;
        default:
            diffs.Add($"{path}: 형식 다름");
            break;
    }
}
void Section(string name, JsonNode a, JsonNode b)
{
    var diffs = new List<string>();
    Cmp(a, b, name, diffs);
    Check(diffs.Count == 0, $"{name}: {diffs.Count}곳 다름\n    " + string.Join("\n    ", diffs.Take(5)));
    if (diffs.Count == 0) Console.WriteLine($"OK {name}" + (a is JsonArray arr ? $" ({arr.Count})" : ""));
}
// v2.0 (v6 3단계): 강도 예측 타임라인(층·구역 곡선·공시체 점·기준선) + 기록만으로 날짜별 판정 (첫 타설 ~ 마지막 기록일)
foreach (var k in new[] { "schema", "version", "levels", "thresholds", "strength_timeline", "decisions" })
    Section(k, cs[k], py[k]);
foreach (var k in new[] { "fck_mpa", "min_levels", "cure_min_c", "date_range", "judged_until", "zone_order" })
    Section("project." + k, cs["project"][k], py["project"][k]);
Check(cs["activities"] is null && cs["cycle"] is null && cs["strength"] is null, "v2.0: 공정·시공 순서·예전 강도 곡선 없음");
// 실측값 (v1.1): 입력자 열이 없을 때의 기본 이름만 다름 (Python 데모는 "(샘플)" 을 붙임) → 빼고 비교
static JsonNode NoBy(JsonNode a)
{
    var c = a.DeepClone().AsArray();
    foreach (var m in c) m!.AsObject().Remove("entered_by");
    return c;
}
if (py["measurements"] is JsonArray pm) Section("measurements", NoBy(cs["measurements"]!), NoBy(pm));
// 근거 원장 (v1.1 선택 필드): 샘플 = 현장양생 공시체 3단계만
Section("evidence", cs["evidence"] ?? new JsonArray(), py["evidence"] ?? new JsonArray());
// 승인 기록 (v1.1 선택 필드): 샘플엔 approvals.csv 가 없어 [] — 그다음 샘플 사본에 1건·2건·사슬 손상을 만들어 export_4d.approvals_rows 와 대조
// (근거 원장도 같은 사본에서: 승인 5·6·7 단계·원장 줄 추가·사슬 손상이면 승인 단계 빠짐 — export_4d 의 evidence 식과 대조)
Section("approvals", cs["approvals"] ?? new JsonArray(), py["approvals"] ?? new JsonArray());
{
    var apDir = Directory.CreateTempSubdirectory("RevitOnSite_web_appr_").FullName;
    try
    {
        foreach (var f in Directory.GetFiles(data)) File.Copy(f, Path.Combine(apDir, Path.GetFileName(f)));
        var ready = new Dictionary<string, string> { ["zone_id"] = "9F-A1", ["decision"] = WebExport.Ready };
        JsonNode PyApprovals() => PyRows("x4.approvals_rows(sys.argv[1])");
        JsonNode PyEvidence() => PyRows("[{'zone': x['zone_id'], 'date': x['date'][:10], 'stage': x['stage'], 'source': x['source']} for x in x4.evd.entries(sys.argv[1])]");
        JsonNode PyRows(string expr)
        {
            var psi = new ProcessStartInfo(Arg("--python") ?? Path.Combine(root, ".venv", "Scripts", "python.exe")) { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
            foreach (var a in new[] { "-c", "import sys, json\nsys.stdout.reconfigure(encoding='utf-8')\nsys.path.insert(0, sys.argv[2])\nimport export_4d as x4\n"
                + "print(json.dumps(" + expr + ", ensure_ascii=False))", apDir, Path.Combine(root, "shoring") }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return JsonNode.Parse(o.Split('\n').Last(l => l.TrimStart().StartsWith("[")))!;   // 사슬 손상이면 앞줄에 "approvals.csv 무시: …" 가 찍힘
        }
        JsonNode CsApprovals() => JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(WebExport.Approvals(new WebInput { DataFolder = apDir })))!;
        JsonNode CsEvidence() => JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(WebExport.EvidenceRows(new WebInput { DataFolder = apDir })))!;
        Approval.Record(apDir, "9F-A1", "감리 승인 기록", "이감리", ready, now: new DateTime(2026, 12, 31, 14, 5, 30));
        Section("approvals 1건", CsApprovals(), PyApprovals());
        Approval.Record(apDir, "9F-A1", "해체 완료", "김현장", ready, note: "메모", now: new DateTime(2027, 1, 2, 9, 0, 0));
        Section("approvals 2건", CsApprovals(), PyApprovals());
        Check(CsApprovals().AsArray().Select(a => (int?)a!["status"]).SequenceEqual(new int?[] { 6, 7 }), "approvals 상태 6·7");
        Evidence.Add(apDir, "9F-A1", "2026-12-20", 2, "김현장", "WP-9F");
        Section("evidence 원장·승인", CsEvidence(), PyEvidence());
        Check(CsEvidence().AsArray().Select(a => (int)a!["stage"]!).OrderBy(v => v).SequenceEqual(new[] { 2, 3, 3, 3, 3, 3, 3, 6, 7 }), "evidence 단계 2·3×6·6·7");
        var lp = Path.Combine(apDir, Approval.Log);
        File.WriteAllText(lp, File.ReadAllText(lp).Replace("이감리", "박감리"));
        Section("approvals 사슬 손상 → []", CsApprovals(), PyApprovals());
        Check(CsApprovals().AsArray().Count == 0, "approvals 사슬 손상이면 빈 목록");
        Section("evidence 사슬 손상 → 승인 단계 빠짐", CsEvidence(), PyEvidence());
    }
    finally { Directory.Delete(apDir, true); }
}

// 부재: ID 별 (벽·창호·계단의 층·구역은 C# 이 정한 값)
var pe = py["elements"]!.AsArray().ToDictionary(e => (string)e["id"]);
var ce = cs["elements"]!.AsArray().ToDictionary(e => (string)e["id"]);
var zonesPy = pe.Values.Where(e => (string)e["category"] == "zone").Select(e => (string)e["id"]).ToHashSet();
var zonesCs = ce.Values.Where(e => (string)e["category"] == "zone").Select(e => (string)e["id"]).ToHashSet();
Check(zonesPy.SetEquals(zonesCs), $"구역 ID: C# {zonesCs.Count} / Python {zonesPy.Count}");
Check(pe.Keys.ToHashSet().SetEquals(ce.Keys), $"부재 ID: C# {ce.Count} / Python {pe.Count}");
var ed = new List<string>();
foreach (var id in pe.Keys.Intersect(ce.Keys))
    Cmp(ce[id], pe[id], $"elements[{id}]", ed);
Check(ed.Count == 0, $"elements: {ed.Count}곳 다름\n    " + string.Join("\n    ", ed.Take(5)));
if (ed.Count == 0 && zonesPy.SetEquals(zonesCs)) Console.WriteLine($"OK elements ({ce.Count}, 구역 {zonesCs.Count}) — 벽·창호·계단 층·구역 배정 포함");

// 판정 정답 (CLAUDE.md 샘플): 2026-12-31 09:00 9층
var at = cs["decisions"]!.AsArray().Where(d => (string)d["at"] == "2026-12-31T09:00" && ((string)d["zone"]).StartsWith("9F-"))
    .ToDictionary(d => (string)d["zone"], d => (string)d["decision"]);
foreach (var z in new[] { "A1", "B1", "C1", "C2" })
    Check(at.GetValueOrDefault("9F-" + z) == WebExport.Ready, $"9F-{z} 2026-12-31: {at.GetValueOrDefault("9F-" + z)}");
foreach (var z in new[] { "A2", "B2" })
    Check(at.GetValueOrDefault("9F-" + z)?.StartsWith("존치") == true, $"9F-{z} 2026-12-31: {at.GetValueOrDefault("9F-" + z)}");
Console.WriteLine("2026-12-31 09:00 9층: " + string.Join(", ", at.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key[3..] + " " + kv.Value)));

// 벽 나누기: export_4d.py story_layout 의 남·북 외벽(두께 0.2, 1층 z 0–2.79)을 SplitWall·Strip 으로 만들어 Python 벽·창대 벽·창호와 비교
static string Key(string cat, List<double[]> fp, double z0, double z1)
{
    int i0 = Enumerable.Range(0, fp.Count).OrderBy(i => fp[i][0]).ThenBy(i => fp[i][1]).First();
    var pts = fp.Skip(i0).Concat(fp.Take(i0)).Select(p => $"{p[0]:F3},{p[1]:F3}");
    return FormattableString.Invariant($"{cat} {string.Join(" ", pts)} z {z0:F3}-{z1:F3}");
}
var south = new[] { 0.0, 10, 20 }.SelectMany(o => new[] { (o + 1.2, o + 5.2, 0.0), (o + 6.2, o + 8.6, 0.9) }).ToList();
var north = new List<(double, double, double)> { (1.5, 3.0, 0.9), (6.0, 8.0, 0.9), (11.5, 13.0, 0.9), (17.2, 18.8, 0.9), (21.5, 23.0, 0.9), (26.0, 28.0, 0.9) };
var mine = new List<string>();
foreach (var (y0, ops) in new[] { (0.0, south), (17.8, north) })
{
    double[] p0 = { 0, y0 }, u = { 1, 0 };
    foreach (var (fp, z0, z1) in WebExport.SplitWall(p0, u, 0, 30, 0, 0.2, 0, 2.79, ops.Select(o => (o.Item1, o.Item2, o.Item3, 2.79))))
        mine.Add(Key("wall", fp, z0, z1));
    foreach (var (a, b, sill) in ops)
        mine.Add(Key("window", WebExport.Strip(p0, u, a, b, 0.05, 0.15), sill, 2.79));
}
var theirs = pe.Values.Where(e => (int)e["floor"] == 1 && (string)e["category"] is "wall" or "window")
    .Select(e => (e, fp: Fp(e["geometry"]["footprint"])))
    .Where(q => q.fp.All(p => p[1] <= 0.2 + 1e-9) || q.fp.All(p => p[1] >= 17.8 - 1e-9))
    .Select(q => Key((string)q.e["category"], q.fp, (double)q.e["geometry"]["z0"], (double)q.e["geometry"]["z1"])).ToList();
bool wallsOk = mine.Order(StringComparer.Ordinal).SequenceEqual(theirs.Order(StringComparer.Ordinal));
Check(wallsOk, $"벽 나누기: C# {mine.Count} / Python {theirs.Count}\n    C# 만: {string.Join(" | ", mine.Except(theirs).Take(3))}\n    Python 만: {string.Join(" | ", theirs.Except(mine).Take(3))}");
if (wallsOk) Console.WriteLine($"OK 벽 나누기 (1층 남·북 외벽 토막·창대 벽·창호 {mine.Count}개)");

// 뷰어 HTML 에 넣기 (export_4d.py --html 과 같은 방식)
string viewer = File.ReadAllText(Path.Combine(root, "web", "onsite_4d.html"), Encoding.UTF8);
string html = WebExport.Html(viewer, json);
var m = Regex.Match(html, "<script type=\"application/json\" id=\"data\">(.*?)</script>", RegexOptions.Singleline);
Check(m.Success && !m.Groups[1].Value.Contains("</") && m.Groups[1].Value.Replace("<\\/", "</") == json
      && html.Remove(m.Groups[1].Index, m.Groups[1].Length) == Regex.Replace(viewer, "(<script type=\"application/json\" id=\"data\">).*?(</script>)", "$1$2", RegexOptions.Singleline),
    "HTML 넣기");
if (Arg("--html") is { } outHtml)
{
    File.WriteAllText(outHtml, html, new UTF8Encoding(false));
    Console.WriteLine($"{outHtml}: 뷰어 + 데이터 {json.Length / 1024} KB");
}

Console.WriteLine(fails == 0 ? "\n전체 일치" : $"\n불일치 {fails}건");
return fails == 0 ? 0 : 1;

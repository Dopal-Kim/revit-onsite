// 일치 시험: C# 엔진(RevitOnSite.Engine)이 Python(shoring/*.py)과 같은 답을 내는지.
//   1) engine.py 자체 검사 assert 이식 + floor_plan 표를 Python 출력값과 대조
//   2) 샘플 CSV 판정표를 저장된 Python 결과 CSV 와 행·열 단위로 대조 (글자 정확히, 숫자는 0.01 이내)
//   3) (v6 에서 뺌 — 공정 계산, A-28 days_to 만 staged 로), 4) install_rules.py 자체 검사 이식
//   5) 지금 Python 을 직접 실행해 대조 (.venv\Scripts\python.exe, 없으면 건너뜀): install_rules 입력 격자,
//      evaluate.py --top-floor — 바이트 단위
//   6) 내부 감사 회귀: test_gate.py A-01–05·A-23·A-24·test_cold.py·A-15 (Python 있으면 판정표 바이트·한중 경고 줄 대조)
//   7) approval.py·pour_gate.py 자체 검사 이식 + Python 과 approvals.csv·요청서 HTML 바이트·해시·게이트 결과 대조
//   8) 센서 없는 기본형 (test_no_sensor.py): 공시체 + 기온만으로 판정(Python 과 바이트)·한중·요청서·승인 기록·타설 전 게이트
//   9) (v6 에서 뺌 — 임대 계획 비교)
//  10) 4D 근거 원장 (evidence.py): 자체 검사 이식 + evidence.csv 바이트·날짜별 원장을 Python 과 대조
//  11) 감리 현장 확인 3가지·마감 값 (approval.py site_checks): 시연 폴더 요청서 HTML 을 Python 과 바이트 대조 (그대로·동바리 확인 3줄·승인 기록 1건)
//  12) 층 표지 (FloorSign, Python 기준본 없음): 시연 폴더에서 층·상태 줄·구역 줄·마감 값 = 검측요청서 값·지시 문구 없음
//  13) 판넬 조기 탈형 (evaluate.py early_form · test_gate.py): 문구 사례 + 샘플 판정표 (열 자체는 evaluate 대조가 바이트로)
//  15) 무단 해체 관측 (evidence.py removal_watch, 총괄 P1-2): 10절 끝 사례 + Python 대조, 11절 요청서 ① ⚠, 12절 층 표지 빨강 상태
//  14) 감사 A-26·28·29 (test_gate.py·engine.py·staged.py): 센서 없는 근거 출처 태그,
//      400일·te ≥ 28·타설 1건 경계, fck·공시체 편차 경고 (Python 있으면 판정표 바이트·input_checks 줄 대조)
// 실행: dotnet run --project revit\RevitOnSite\RevitOnSite.Engine.Check [-- --data <폴더> --expected <csv> --at 2026-12-31T09:00 --python <exe>]
// 다른 판정 시각의 기대값: python shoring\evaluate.py shoring\sample\data --at <시각> --out <csv> 로 만든 뒤 --expected 로 넘긴다.
// 불일치·실패가 하나라도 있으면 종료 코드 1.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using RevitOnSite.Engine;
using static RevitOnSite.Engine.InstallRules;
using static RevitOnSite.Engine.ShoringEngine;

Console.OutputEncoding = Encoding.UTF8;
// --culture de-DE: 쉼표 소수점 문화권에서 같은 대조를 돌려 문화권에 기대는 서식·해석을 찾는다 (오류 점검 ② — Revit 사용자 PC 문화권이 달라도 같은 글자)
if (Array.IndexOf(args, "--culture") is var ci && ci >= 0 && ci + 1 < args.Length)
    CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(args[ci + 1]);
var inv = CultureInfo.InvariantCulture;
var runStart = DateTime.UtcNow.AddSeconds(-2);   // 이번 실행이 %TEMP% 에 쓴 파일 = 이 시각 뒤 (맨 끝 정리용)
int fails = 0;
void Check(bool ok, string what)
{
    if (!ok) { fails++; Console.WriteLine("FAIL " + what); }
}

// ── 1. engine.py 자체 검사 ──
const double fck = 24.0;
// 1) 20 °C 28일 = fck+4 (KDS 평균강도)
Check(Math.Abs(Predict("보통", new DateOnly(2026, 4, 1), 28, fck, cureMin: 20, temps: Enumerable.Repeat(20.0, 28).ToList()) - 28.0) < 1e-6, "self-check 1");
// 2) 가이드라인 예제: 12.33 ≤ 3개층 28일 여유 15.50
Check(Math.Abs(PourLoad() - 12.33) < .01 && Math.Abs(3 * SlabSpare() - 15.50) < .01, "self-check 2");
// 3) s 역산 왕복
double te = EquivalentAge(Enumerable.Repeat(10.0, 7), 33.3);
Check(Math.Abs((CalibrateS(StrengthAt(te, fck, 0.35), te, fck) ?? double.NaN) - 0.35) < 1e-9, "self-check 3");
// 4) IDW: 센서 위치에서 그 값
Check(Idw(new[] { (0.0, 0.0), (10.0, 0.0) }, new[] { 1.0, 5.0 }, 0, 0) == 1.0, "self-check 4");
// 5) 화정형: 동바리 1개층만 → 위반
var pours = Enumerable.Range(0, 6).Select(f => (f + 1, new DateOnly(2026, 11, 1).AddDays(7 * f), "보통")).ToList();
Check(FloorPlan(pours, fck, 1).Skip(1).Take(3).All(r => r.Verdict == "위반"), "self-check 5");
// 6) floor_plan(pours, 24, 3) — Python 3.12 실행 결과 전체 자릿수 (fck 도달, 필요 존치 층수, 예정 해체, 그때 강도, 지지 최소, 하중비, 판정)
var expectedPlan = new (string Due, int Lv, string? Rm, double? FRm, double? FSup, double Ratio, string V)[]
{
    ("2026-11-25", 4, "2026-11-22", 23.304224111146908, null, 0.0, "위반"),
    ("2026-12-05", 4, "2026-11-29", 22.77011749283623, null, 0.0, "위반"),
    ("2026-12-13", 4, "2026-12-06", 22.37896664975605, null, 0.0, "위반"),
    ("2026-12-21", 5, null, null, 15.108191964800497, 0.8835316794084102, "적합"),
    ("2026-12-28", 5, null, null, 14.534235209253936, 0.8966842956186296, "적합"),
    ("2027-01-04", 5, null, null, 14.440148201595353, 0.9029063867240527, "적합"),
};
double planDiff = 0;
foreach (var (r, x) in FloorPlan(pours, fck, 3).Zip(expectedPlan))
{
    double D(double? a, double? b) => a is null || b is null ? (a is null && b is null ? 0 : double.PositiveInfinity) : Math.Abs(a.Value - b.Value);
    planDiff = new[] { planDiff, D(r.StrengthAtRemoval, x.FRm), D(r.MinSupportStrength, x.FSup), D(r.LoadRatio, x.Ratio) }.Max();
    Check(r.FckReached?.ToString("yyyy-MM-dd", inv) == x.Due && r.RequiredLevels == x.Lv && r.PlannedRemoval?.ToString("yyyy-MM-dd", inv) == x.Rm
          && r.Verdict == x.V, $"floor_plan {r.Floor}층: {r}");
}
Check(planDiff < 1e-9, $"floor_plan 숫자 차이 {planDiff:E2}");
// 6-1) 경계 (감사 A-28): te ≥ 28 → null, 타설 1건 → 필요 존치 층수 null, 400일 안에 못 닿음 → null·위반
Check(CalibrateS(30, 28, fck) is null && CalibrateS(30, 40, fck) is null && CalibrateS(30, 0, fck) is null, "A-28 calibrate_s");
var one = FloorPlan(pours.Take(1).ToList(), fck, 3)[0];
Check(one.RequiredLevels is null && one.FckReached is not null && FloorPlan(pours.Take(1).ToList(), fck, 3, cycle: 7)[0].RequiredLevels == FloorPlan(pours, fck, 3)[0].RequiredLevels, "A-28 타설 1건");
var cold28 = FloorPlan(pours.Take(2).ToList(), fck, 3, cureMin: -60, temps: Enumerable.Repeat(-60.0, 400).ToList())[0];
Check(cold28.FckReached is null && cold28.RequiredLevels is null && cold28.Verdict == "위반", "A-28 400일 " + cold28);
Console.WriteLine(fails == 0 ? $"self-check OK (floor_plan 최대 차이 {planDiff:E1})" : "self-check FAIL");

// ── 2. 샘플 판정표 대조 ──
string? Arg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
string FindRoot()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        if (Directory.Exists(Path.Combine(d.FullName, "shoring", "sample", "data")))
            return d.FullName;
    throw new DirectoryNotFoundException(@"shoring\sample\data 를 찾지 못함 — --data 와 --expected 로 지정");
}
string data = Arg("--data") ?? Path.Combine(FindRoot(), "shoring", "sample", "data");
string expectedCsv = Path.GetFullPath(Arg("--expected") ?? Path.Combine(FindRoot(), "shoring", "sample", "zone_decisions_sample.csv"));
var at = DateTime.ParseExact(Arg("--at") ?? "2026-12-31T09:00", "yyyy-MM-dd'T'HH:mm", inv);   // 기본값 = make_sample.py AT

var py = Evaluator.Read(Path.GetDirectoryName(expectedCsv)!, Path.GetFileName(expectedCsv));
var cs = Evaluator.Evaluate(data, at);
var numeric = new HashSet<string> { "floor", "age_days", "fck", "threshold", "f_cyl", "f_est_min" };
Console.WriteLine($"\n판정 시각 {at:yyyy-MM-dd'T'HH:mm}, 기대값 {expectedCsv}");
Check(py.Count == cs.Count, $"행 수 Python {py.Count} / C# {cs.Count}");
Console.WriteLine($"{"zone",-8} {"Python",-20} {"C#",-20} {"max diff",9}  결과");
for (int i = 0; i < Math.Min(py.Count, cs.Count); i++)
{
    var vals = cs[i].CsvValues();
    var bad = new List<string>();
    double maxDiff = 0;
    for (int k = 0; k < ZoneRow.CsvHeader.Length; k++)
    {
        string h = ZoneRow.CsvHeader[k], a = py[i].GetValueOrDefault(h) ?? "(열 없음)", b = vals[k];
        if (numeric.Contains(h) && a != "" && b != "")
        {
            double d = Math.Abs(double.Parse(a, inv) - double.Parse(b, inv));
            maxDiff = Math.Max(maxDiff, d);
            if (!(d <= 0.01)) bad.Add($"{h}: {a} ≠ {b}");
        }
        else if (a != b) bad.Add($"{h}: \"{a}\" ≠ \"{b}\"");
    }
    Console.WriteLine($"{cs[i].ZoneId,-8} {py[i].GetValueOrDefault("decision"),-20} {cs[i].Decision,-20} {maxDiff,9:F4}  {(bad.Count == 0 ? "OK" : "MISMATCH")}");
    foreach (var m in bad) Console.WriteLine("    " + m);
    Check(bad.Count == 0, cs[i].ZoneId);
}
var tmp = Path.Combine(Path.GetTempPath(), "RevitOnSite_zone_decisions_cs.csv");
Evaluator.Write(cs, tmp);
Console.WriteLine($"C# Write() 결과 CSV 가 Python CSV 와 바이트 동일: {(File.ReadAllBytes(tmp).AsSpan().SequenceEqual(File.ReadAllBytes(expectedCsv)) ? "예" : "아니오")} ({tmp})");
Console.WriteLine("해체 승인 요청 가능: " + string.Join(", ", cs.Where(r => r.Decision == "해체 승인 요청 가능").Select(r => r.ZoneId)));

// ── 3. (v6 공정 계산 삭제, 총괄 11-08) 감사 A-28: 400일 안에 못 닿으면 null — staged.py days_to (단계 해체 검토가 씀)
int failsBefore = fails;
Check(Staged.DaysTo("보통", new DateOnly(2026, 1, 1), 24, 24, -60, Enumerable.Repeat(-60.0, 400).ToList()) is null, "A-28 days_to");

// ── 4. install_rules.py 자체 검사 ──
failsBefore = fails;
double w0 = DesignLoad(210);
Check(Math.Abs(w0 - 7.94) < 1e-9, "install 1");                                                   // 정의서 §3.2 산정 (210 mm)
Check(Math.Abs(PostLoad(1.2, 1.2, w0) - 11.4336) < 1e-9 && PostLoad(1.2, 1.2, w0) <= PipeAllowableKn, "install 2");
Check(Math.Abs(PostLoad(1.5, 1.5, w0) - 17.865) < 1e-9 && PostLoad(1.5, 1.5, w0) > PipeAllowableKn, "install 3");   // §4.1 17.9 kN
Check(DesignLoad(50) == MinVerticalKnM2, "install 4");                                             // 얇아도 5.0 최소
bool FlagsAre(List<InstallCheck> r, params string[] e) => Flags(r).SequenceEqual(e);
string Res(List<InstallCheck> rs, string item) => rs.First(r => r.Item == item).Result;
Check(FlagsAre(CheckShore(2790), "이음", "1본 하중"), "install 5");                                  // 기록 없음 → 확인 필요 (A-14)
Check(CheckShore(2790).Where(r => r.Item is "이음" or "1본 하중").All(r => r.Result == "필요"), "install 5b");
const int okSp = 0; const double okLd = 11.4;                                                         // 기록 있음 (이음 0, 1.2 m 간격 하중)
Check(FlagsAre(CheckShore(2790, okSp, loadKn: okLd)), "install 6");                                  // 데모 모델 층고 3.0 m
Check(FlagsAre(CheckShore(3950, okSp, loadKn: okLd), "수평연결재"), "install 7");
Check(Res(CheckShore(3950, okSp, loadKn: okLd), "수평연결재") == "필요", "install 8");               // 기록 없음 → 확인 필요 (불합격 아님)
Check(Res(CheckShore(3950, okSp, horizBrace: false, loadKn: okLd), "수평연결재") == "NG", "install 9");   // "없음" 기록 → 불합격
Check(FlagsAre(CheckShore(3950, okSp, horizBrace: true, loadKn: okLd)), "install 10");
Check(FlagsAre(CheckShore(4650, okSp, loadKn: okLd), "수평연결재", "동바리 형식"), "install 11");
Check(Res(CheckShore(4650, okSp, loadKn: okLd), "동바리 형식") == "NG", "install 12");             // 4 m 초과 파이프 서포트 → 불합격
Check(FlagsAre(CheckShore(5900, okSp, horizBrace: true, shoreType: "시스템", loadKn: okLd), "구조계산"), "install 13");
Check(FlagsAre(CheckShore(2790, 2, loadKn: okLd), "이음") && FlagsAre(CheckShore(2790, 1, loadKn: okLd)), "install 14");
Check(FlagsAre(CheckShore(2790, okSp, pourThicknessMm: 1200, loadKn: okLd), "동바리 형식"), "install 15");
Console.WriteLine(fails == failsBefore ? "install_rules self-check OK" : "install_rules self-check FAIL");

// ── 5. 지금 Python 을 직접 실행해 대조 ──
string shoringDir = Path.Combine(FindRoot(), "shoring");
string? python = Arg("--python") ?? new[] { Path.Combine(FindRoot(), ".venv", "Scripts", "python.exe") }.FirstOrDefault(File.Exists);
string RunPy(params string[] a)
{
    var psi = new ProcessStartInfo(python!) { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, WorkingDirectory = shoringDir };
    psi.Environment["PYTHONIOENCODING"] = "utf-8";
    foreach (var x in a) psi.ArgumentList.Add(x);
    using var p = Process.Start(psi)!;
    var o = p.StandardOutput.ReadToEnd();
    var err = p.StandardError.ReadToEnd();
    p.WaitForExit();
    if (p.ExitCode != 0) throw new InvalidOperationException("Python 실행 실패: " + err);
    return o.Replace("\r\n", "\n");
}
bool SameBytes(string label, string pyCsv, Action<string> writeCs)
{
    var csCsv = Path.Combine(Path.GetTempPath(), "RevitOnSite_cs_" + Path.GetFileName(pyCsv));
    writeCs(csCsv);
    bool same = File.ReadAllBytes(csCsv).AsSpan().SequenceEqual(File.ReadAllBytes(pyCsv));
    Console.WriteLine($"{label}: Python 과 바이트 동일 {(same ? "예" : "아니오")}");
    Check(same, $"{label} — Python {pyCsv} / C# {csCsv}");
    return same;
}
if (python is null)
    Console.WriteLine("\nPython 없음 (.venv\\Scripts\\python.exe 또는 --python) — Python 직접 대조 건너뜀");
else
{
    Console.WriteLine($"\nPython 직접 대조 ({python})");
    // install_rules: 같은 입력 격자를 양쪽에서 돌려 한 줄씩 비교
    var pyLines = RunPy("-c", """
        import install_rules as r
        def fmt(res): return "||".join(f"{x['item']}|{x['result']}|{x['reason']}|{x['source']}" for x in res)
        for t in (0, 50, 150, 210, 300, 499, 500, 999, 1000, 1200):
            for ph in (None, 0.3, 0.5, 0.99, 1.0):
                print("D", repr(r.design_load(t, pour_height_m=ph)))
        for sx, sy in ((1.2, 1.2), (1.5, 1.5), (0.9, 1.1)):
            print("P", repr(r.post_load(sx, sy, r.design_load(210))))
        for h in (2790, 3500, 3501, 3950, 4000, 4000.4, 4650, 4999.6, 5000, 5900, 8000.5):
            for sp in (None, 0, 1, 2):
                for hb in (None, False, True):
                    for st in ("파이프서포트", "시스템"):
                        for pt in (0.0, 1000.0, 1200.0):
                            for ld in (None, 11.4336, 13.3333, 13.34, 17.865):
                                print("C", fmt(r.check_shore(h, sp, hb, st, pt, ld)))
        for ld in (9.95, 10.0, 10.05):
            print("A", fmt(r.check_shore(3000, load_kn=ld, allowable_kn=10.0)))
        """).TrimEnd('\n').Split('\n');
    string Fmt(List<InstallCheck> res) => string.Join("||", res.Select(x => $"{x.Item}|{x.Result}|{x.Reason}|{x.Source}"));
    var csLines = new List<string>();
    foreach (var t in new double[] { 0, 50, 150, 210, 300, 499, 500, 999, 1000, 1200 })
        foreach (var ph in new double?[] { null, 0.3, 0.5, 0.99, 1.0 })
            csLines.Add("D " + RevitOnSite.Engine.Py.Repr(DesignLoad(t, pourHeightM: ph)));
    foreach (var (sx, sy) in new[] { (1.2, 1.2), (1.5, 1.5), (0.9, 1.1) })
        csLines.Add("P " + RevitOnSite.Engine.Py.Repr(PostLoad(sx, sy, DesignLoad(210))));
    foreach (var h in new[] { 2790, 3500, 3501, 3950, 4000, 4000.4, 4650, 4999.6, 5000, 5900, 8000.5 })
        foreach (var sp in new int?[] { null, 0, 1, 2 })
            foreach (var hb in new bool?[] { null, false, true })
                foreach (var st in new[] { "파이프서포트", "시스템" })
                    foreach (var pt in new[] { 0.0, 1000.0, 1200.0 })
                        foreach (var ld in new double?[] { null, 11.4336, 13.3333, 13.34, 17.865 })
                            csLines.Add("C " + Fmt(CheckShore(h, sp, hb, st, pt, ld)));
    foreach (var ld in new[] { 9.95, 10.0, 10.05 })
        csLines.Add("A " + Fmt(CheckShore(3000, loadKn: ld, allowableKn: 10.0)));
    int firstBad = Enumerable.Range(0, Math.Max(pyLines.Length, csLines.Count)).FirstOrDefault(i => i >= pyLines.Length || i >= csLines.Count || pyLines[i] != csLines[i], -1);
    Console.WriteLine($"install_rules 입력 격자 {csLines.Count}줄: {(firstBad < 0 ? "모두 일치" : $"{firstBad + 1}번째 줄부터 다름")}");
    if (firstBad >= 0)
        Console.WriteLine($"    Python: {pyLines.ElementAtOrDefault(firstBad)}\n    C#    : {csLines.ElementAtOrDefault(firstBad)}");
    Check(firstBad < 0, "install_rules Python 대조");
    // evaluate.py --top-floor 12 (12층 = 지붕 슬래브 가정)
    var pyTop = Path.Combine(Path.GetTempPath(), "RevitOnSite_py_top12.csv");
    RunPy("evaluate.py", data, "--at", at.ToString("yyyy-MM-dd'T'HH:mm", inv), "--top-floor", "12", "--out", pyTop);
    SameBytes("evaluate --top-floor 12", pyTop, p => Evaluator.Write(Evaluator.Evaluate(data, at, topFloor: 12), p));
    {   // 78차 ④: 데이터 폴더 options.json top_floor (애드인 [구역 나누기] 기본값) = --top-floor 12, 참거짓이면 없음
        var topDir = Directory.CreateTempSubdirectory("RevitOnSite_top_").FullName;
        foreach (var f in Directory.GetFiles(data)) File.Copy(f, Path.Combine(topDir, Path.GetFileName(f)));
        File.WriteAllText(Path.Combine(topDir, Evaluator.OptionsFile), "{\"top_floor\": 12}", new UTF8Encoding(false));
        SameBytes("options.json top_floor 12", pyTop, p => Evaluator.Write(Evaluator.Evaluate(topDir, at), p));
        File.WriteAllText(Path.Combine(topDir, Evaluator.OptionsFile), "{\"top_floor\": 12, \"top_floor_auto\": true}", new UTF8Encoding(false));   // 83차: 자동값 → 사유 태그
        var pyAuto = Path.Combine(Path.GetTempPath(), "RevitOnSite_py_topauto.csv");
        RunPy("evaluate.py", topDir, "--at", at.ToString("yyyy-MM-dd'T'HH:mm", inv), "--out", pyAuto);
        SameBytes("options.json top_floor 12 자동값 (확인 필요 태그)", pyAuto, p => Evaluator.Write(Evaluator.Evaluate(topDir, at), p));
        Check(File.ReadAllText(pyAuto).Contains(Evaluator.TopAutoTag), "자동 최상층 태그가 판정표에 있음");
        File.WriteAllText(Path.Combine(topDir, Evaluator.OptionsFile), "{\"top_floor\": true}", new UTF8Encoding(false));
        Check(Evaluator.TopOption(topDir) == null && Evaluator.Evaluate(topDir, at).Select(r => r.LevelGate).SequenceEqual(Evaluator.Evaluate(data, at).Select(r => r.LevelGate)), "options.json top_floor 참거짓 = 없음");
        Directory.Delete(topDir, true);
    }
    // evaluate.py 강도 센서 (data_strength = data + sensor_strength.csv, make_sample.py): 9F-C1 가운데 강도 센서가 낮게 보고 → C1 존치
    var strengthData = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(data))!, "data_strength");
    if (Directory.Exists(strengthData))
    {
        var pyStr = Path.Combine(Path.GetTempPath(), "RevitOnSite_py_strength.csv");
        RunPy("evaluate.py", strengthData, "--at", at.ToString("yyyy-MM-dd'T'HH:mm", inv), "--out", pyStr);
        SameBytes("evaluate 강도 센서 (data_strength)", pyStr, p => Evaluator.Write(Evaluator.Evaluate(strengthData, at), p));
        var c1 = Evaluator.Evaluate(strengthData, at).First(r => r.ZoneId == "9F-C1");
        Console.WriteLine($"data_strength 9F-C1: {c1.Decision} · {c1.EstSource}");
        Check(c1.Decision.StartsWith("존치") && c1.EstSource.Contains("강도 센서"), "강도 센서가 낮으면 C1 존치");
    }
}
// ── 6. 내부 감사 회귀 (docs/internal_audit.md): shoring/test_gate.py A-01–05·test_cold.py 를 같은 입력으로.
//    Python 이 있으면 판정표는 evaluate.py 결과 CSV 와 바이트, 한중 경고는 cold_checks 와 줄 단위로 대조
failsBefore = fails;
Console.WriteLine("\n내부 감사 회귀");
var qAt = new DateTime(2026, 12, 31, 9, 0, 0);   // test_gate.py AT
string Scenario(string[]? zs, string[]? ps, string[]? cs, params string[] copy)
{
    var t = Directory.CreateTempSubdirectory("RevitOnSite_gate_").FullName;
    foreach (var n in copy) File.Copy(Path.Combine(data, n), Path.Combine(t, n));
    void W(string name, string head, string[]? rows)
    {
        if (rows is not null) File.WriteAllText(Path.Combine(t, name), head + "\n" + string.Join("\n", rows) + "\n", new UTF8Encoding(false));
    }
    W("zones.csv", "zone_id,floor,x0,y0,x1,y1", zs);
    W("pours.csv", "zone_id,pour_date,pour_time,mix,fck", ps);
    W("cylinders.csv", "zone_id,test_date,curing,f1,f2,f3", cs);
    return t;
}
string Stamp(DateTime d) => d.ToString("yyyy-MM-dd'T'HH:mm", inv);
Dictionary<string, ZoneRow> Gate(string label, string folder, DateTime when)
{
    try
    {
        var rows = Evaluator.Evaluate(folder, when);
        if (python is not null)
        {
            var pyG = Path.Combine(Path.GetTempPath(), "RevitOnSite_py_gate_" + label + ".csv");
            RunPy("evaluate.py", folder, "--at", Stamp(when), "--out", pyG);
            SameBytes("evaluate " + label, pyG, p => Evaluator.Write(rows, p));
        }
        return rows.ToDictionary(r => r.ZoneId);
    }
    finally { Directory.Delete(folder, true); }
}
string[] Box(params (string Id, int Fl)[] zs) => zs.Select(z => $"{z.Id},{z.Fl},0,0,10,10").ToArray();
// A-01: 4F 는 계획 행(6/30) → 1F 위로 2개층뿐 → 존치, 4F 는 판정에서 빠짐
var qg = Gate("A-01", Scenario(Box(("1F-A", 1), ("2F-A", 2), ("3F-A", 3), ("4F-A", 4)),
    new[] { "1F-A,2026-05-01,08:00,보통,24", "2F-A,2026-05-08,08:00,보통,24", "3F-A,2026-05-15,08:00,보통,24", "4F-A,2026-06-30,08:00,보통,24" },
    new[] { "1F-A,2026-05-31,현장봉함,25,25,25" }), new DateTime(2026, 6, 1, 9, 0, 0));
Check(qg["1F-A"].Decision == "존치" && qg["1F-A"].LevelGate == "NG" && !qg.ContainsKey("4F-A"), "A-01 계획 행 제외");
// A-03: 지하 −1 → 지상 1 (0 없음) 도 층 순서로 셈 → B1 위로 2개층뿐 → 존치
qg = Gate("A-03", Scenario(Box(("B1-A", -1), ("1F-A", 1), ("2F-A", 2)),
    new[] { "B1-A,2026-05-01,08:00,보통,24", "1F-A,2026-05-08,08:00,보통,24", "2F-A,2026-05-15,08:00,보통,24" },
    new[] { "B1-A,2026-06-01,현장봉함,26,26,26" }), new DateTime(2026, 6, 2, 9, 0, 0));
Check(qg["B1-A"].Decision == "존치" && qg["B1-A"].Reason.Contains("위로 2개층"), "A-03 층 순서");
// A-02: 12F 는 A1 만 타설 → A1 위치만 3개층, B1·C2 위치는 2개층 → 존치
var qPours = File.ReadAllLines(Path.Combine(data, "pours.csv")).Skip(1).Where(l => l.Length > 0 && (!l.StartsWith("12F-") || l.StartsWith("12F-A1,"))).ToArray();
qg = Gate("A-02", Scenario(null, qPours, null, "zones.csv", "sensors.csv", "cylinders.csv"), qAt);
Check(new[] { "9F-B1", "9F-C2" }.All(z => qg[z].Decision == "존치" && qg[z].LevelGate == "NG") && qg["9F-A1"].LevelGate != "NG", "A-02 구역별 겹침");
// A-04: 첫날 35 ℃ 기록뿐인 센서 → 끊긴 뒤는 기상 온도 (계속 35 ℃ 로 치면 22일 만에 fck 훨씬 넘음)
var qStart = new DateTime(2026, 12, 1, 8, 0, 0);
var qEnd = new DateTime(2026, 12, 23, 9, 0, 0);
var qRows = Enumerable.Range(0, 24).Select(h => new Dictionary<string, string?> { ["timestamp"] = qStart.AddHours(h).ToString("yyyy-MM-dd'T'HH:mm:ss", inv), ["temp_c"] = "35" }).ToList();
var (qS, qEa) = Mixes["보통"];
double qTe = Evaluator.SensorTe(qRows, qStart, qEnd, qEa, _ => 0.0);
double qHot = (23 + Evaluator.MaxGapH) / 24;                                   // 마지막 기록 23시 + 끊김 허용 6 h
double qHeld = AgeFactor(35, qEa) * qHot + AgeFactor(0.0, qEa) * ((qEnd - qStart).TotalSeconds / 86400 - qHot);
Check(Math.Abs(qTe - qHeld) < 1e-6 && StrengthAt(qTe, 24, qS) < 24 && Evaluator.StaleDays(qRows, qEnd) > 20, $"A-04 기록 끊김 {qTe} / {qHeld}");
// A-05: 표준양생 공시체는 기준 근거 아님 → 공시체 시험 필요 (무시 건수 표시)
qg = Gate("A-05", Scenario(null, null, new[] { "9F-A1,2026-12-30,표준양생,30,30,30", "9F-B1,2026-12-30,현장봉함,25,25,25" }, "zones.csv", "pours.csv", "sensors.csv"), qAt);
Check(qg["9F-A1"].Decision == "공시체 시험 필요" && qg["9F-A1"].Reason.Contains("현장양생 아님 1건") && qg["9F-B1"].FCyl == 25.0, "A-05 현장양생만");
// A-23: 타설 시각 비면 23:59 (00:00 이면 재령이 최대 하루 길어짐)
qg = Gate("A-23", Scenario(Box(("1F-A", 1)), new[] { "1F-A,2026-05-01,,보통,24" }, null), new DateTime(2026, 5, 2, 9, 0, 0));
Check(qg["1F-A"].AgeDays == RevitOnSite.Engine.Py.Round((new DateTime(2026, 5, 2, 9, 0, 0) - new DateTime(2026, 5, 1, 23, 59, 0)).TotalSeconds / 86400, 1), $"A-23 타설 시각 빈칸 {qg["1F-A"].AgeDays}");
// A-24: 같은 구역 타설 두 줄 → 입력 오류, 같은 날 공시체 두 건 → 낮은 쪽
var qDup = Scenario(Box(("1F-A", 1)), new[] { "1F-A,2026-05-01,08:00,보통,24", "1F-A,2026-05-20,08:00,보통,24" }, null);
string qDupMsg = "";
try { Evaluator.Evaluate(qDup, new DateTime(2026, 6, 1, 9, 0, 0)); } catch (InvalidDataException x) { qDupMsg = x.Message; } finally { Directory.Delete(qDup, true); }
Check(qDupMsg.Contains("두 번"), "A-24 중복 타설 행: " + qDupMsg);
qg = Gate("A-24", Scenario(null, null, new[] { "9F-A1,2026-12-30,현장봉함,22,22,22", "9F-A1,2026-12-30,현장봉함,26,26,26" }, "zones.csv", "pours.csv", "sensors.csv"), qAt);
Check(qg["9F-A1"].FCyl == 22.0, $"A-24 같은 날 공시체 낮은 쪽 {qg["9F-A1"].FCyl}");
// A-15: 센서 시각 시간대 → KST, 빈 숫자 칸 → 입력 오류 문구 (서버 500 아님)
Check(Evaluator.Ts("2026-12-31T10:00+09:00") == new DateTime(2026, 12, 31, 10, 0, 0) && Evaluator.Ts(" 2026-12-31T01:00Z") == new DateTime(2026, 12, 31, 10, 0, 0), "A-15 시간대");
var qBad = Scenario(null, null, null, "zones.csv", "pours.csv");
File.WriteAllText(Path.Combine(qBad, "sensors.csv"), "sensor_id,floor,x,y,timestamp,temp_c\nT1,9,5,4.5,2026-11-26T08:00,\n");
string qMsg = "";
try { Evaluator.Evaluate(qBad, qAt); } catch (InvalidDataException x) { qMsg = x.Message; } finally { Directory.Delete(qBad, true); }
Check(qMsg.StartsWith("입력 CSV 값을 읽을 수 없음") && qMsg.EndsWith("빈 숫자 칸·글자·날짜 형식을 확인하세요"), "A-15 빈 칸: " + qMsg);
// 한중 초기양생 경고 (test_cold.py): 9F-A1 가운데 센서 T1 하나, 타설 11-26 08:00 부터 시간별 온도
string ColdText(IEnumerable<ColdWarning> ws) => string.Join("\n", ws.Select(w => $"{w.ZoneId}|{w.Floor}|{w.Kind}|{w.At}|{w.SensorId}|{RevitOnSite.Engine.Py.Repr(w.TempC)}|{w.Text}"));
List<string> Cold(string label, string folder, Func<ColdWarning, bool> keep)
{
    var ws = Evaluator.ColdChecks(folder, qAt);
    if (python is not null)
    {
        var pyC = RunPy("-c", "import sys, evaluate as ev\nfrom datetime import datetime\n"
            + "for w in ev.cold_checks(sys.argv[1], datetime.fromisoformat(sys.argv[2])): "
            + "print(w['zone_id'], w['floor'], w['kind'], w['at'], w['sensor_id'], repr(w['temp_c']), w['text'], sep='|')", folder, Stamp(qAt)).TrimEnd('\n');
        bool same = pyC == ColdText(ws);
        Console.WriteLine($"cold_checks {label}: {ws.Count}건, Python 과 같음 {(same ? "예" : "아니오")}");
        if (!same) Console.WriteLine($"    Python: {pyC.Replace("\n", " / ")}\n    C#    : {ColdText(ws).Replace("\n", " / ")}");
        Check(same, "cold_checks " + label + " Python 대조");
    }
    return ws.Where(keep).Select(w => w.Kind).ToList();
}
List<string> ColdRun(string label, params (double T, int Hours)[] spans)
{
    var t = Scenario(null, null, null, "zones.csv", "pours.csv");
    try
    {
        var t0 = new DateTime(2026, 11, 26, 8, 0, 0);
        var temps = spans.SelectMany(s => Enumerable.Repeat(s.T, s.Hours)).ToList();
        File.WriteAllText(Path.Combine(t, "sensors.csv"), "sensor_id,floor,x,y,timestamp,temp_c\n"
            + string.Concat(temps.Select((T, h) => $"T1,9,5,4.5,{Stamp(t0.AddHours(h))},{RevitOnSite.Engine.Py.Repr(T)}\n")));
        return Cold(label, t, w => w.ZoneId == "9F-A1");
    }
    finally { Directory.Delete(t, true); }
}
Check(ColdRun("따뜻함", (15.0, 240)).Count == 0, "cold 1 따뜻하면 경고 없음");
Check(ColdRun("5℃ 미만", (3.0, 12), (15.0, 240)).SequenceEqual(new[] { "초기양생 5℃" }), "cold 2 5 MPa 전 5 ℃ 미만");
Check(ColdRun("도달 후 영하", (15.0, 72), (-2.0, 24), (15.0, 24)).SequenceEqual(new[] { "도달 후 2일 0℃" }), "cold 3 도달 뒤 2일 안 영하");
Check(ColdRun("2일 뒤 영하", (15.0, 216), (-2.0, 24)).Count == 0, "cold 4 도달 후 2일 지나 영하");
Check(Cold("샘플", data, w => w.Kind == "한중" && w.Floor == 12).Count > 0, "cold 5 샘플 12F 한중");
// FRESH_H (test_gate): 막 친 맨 위 슬래브(24 h 안)는 지지층이 아니라 하중 — 1F 위 2·3·4F, 4F 가 판정 1 h 전 타설 → 2·3F 가 받침, 25 h 뒤엔 4F 도 지지층
var qFresh = new[] { "1F-A,2026-05-01,08:00,보통,24", "2F-A,2026-05-08,08:00,보통,24", "3F-A,2026-05-15,08:00,보통,24", "4F-A,2026-06-01,08:00,보통,24" };
qg = Gate("FRESH-1h", Scenario(Box(("1F-A", 1), ("2F-A", 2), ("3F-A", 3), ("4F-A", 4)), qFresh, new[] { "1F-A,2026-05-31,현장봉함,25,25,25" }), new DateTime(2026, 6, 1, 9, 0, 0));
Check(!qg["1F-A"].Reason.Contains("지지층 최소 0."), "FRESH 1 막 친 슬래브는 지지층 아님: " + qg["1F-A"].Reason);
qg = Gate("FRESH-25h", Scenario(Box(("1F-A", 1), ("2F-A", 2), ("3F-A", 3), ("4F-A", 4)), qFresh, new[] { "1F-A,2026-05-31,현장봉함,25,25,25" }), new DateTime(2026, 6, 2, 9, 0, 0));
Check(qg["1F-A"].LevelGate == "NG(지지층)", "FRESH 2 25 h 뒤 지지층: " + qg["1F-A"].LevelGate);
// D1 (test_gate): zones.csv slab_mm = 모델 슬래브 두께 → 하중비교 두께. 값 300 → (모델), 빈칸 → 210 [가정] — Python 판정표와 바이트 대조
foreach (var (label, mm, want) in new[] { ("D1-300", "300", "하중비 1.39 > 1.0 — 하중 슬래브 300 mm (모델) (KCS 14 20 12 지지 슬래브)"),
                                          ("D1-blank", "", "하중비 1.13 > 1.0 — 하중 슬래브 210 mm [가정] (KCS 14 20 12 지지 슬래브)") })
{
    var qD1 = Scenario(null, qFresh, new[] { "1F-A,2026-05-31,현장봉함,25,25,25" });
    File.WriteAllText(Path.Combine(qD1, "zones.csv"), "zone_id,floor,x0,y0,x1,y1,slab_mm\n"
        + string.Concat(new[] { 1, 2, 3, 4 }.Select(k => $"{k}F-A,{k},0,0,10,10,{mm}\n")), new UTF8Encoding(false));
    qg = Gate(label, qD1, new DateTime(2026, 6, 1, 9, 0, 0));
    Check(qg["1F-A"].Reason == want, label + " 사유: " + qg["1F-A"].Reason);
}
// 공시체–센서 교차 검사 (test_gate): 샘플은 어긋남 없음, 9F-A1 공시체 40 MPa 면 경고, 센서 없으면 비교 안 함 — Python cross_checks 와 줄 단위 대조
string CrossText(IEnumerable<CrossWarning> ws) => string.Join("\n", ws.Select(w => $"{w.ZoneId}|{w.Floor}|{w.Kind}|{RevitOnSite.Engine.Py.Repr(w.FCyl)}|{RevitOnSite.Engine.Py.Repr(w.FEst)}|{RevitOnSite.Engine.Py.Repr(w.Diff)}|{w.Text}"));
List<CrossWarning> Cross(string label, string folder)
{
    var ws = Evaluator.CrossChecks(folder, qAt);
    if (python is not null)
    {
        var pyX = RunPy("-c", "import sys, evaluate as ev\nfrom datetime import datetime\n"
            + "for w in ev.cross_checks(sys.argv[1], datetime.fromisoformat(sys.argv[2])): "
            + "print(w['zone_id'], w['floor'], w['kind'], repr(w['f_cyl']), repr(w['f_est']), repr(w['diff']), w['text'], sep='|')", folder, Stamp(qAt)).TrimEnd('\n');
        bool same = pyX == CrossText(ws);
        Console.WriteLine($"cross_checks {label}: {ws.Count}건, Python 과 같음 {(same ? "예" : "아니오")}");
        if (!same) Console.WriteLine($"    Python: {pyX}\n    C#    : {CrossText(ws)}");
        Check(same, "cross_checks " + label + " Python 대조");
    }
    return ws;
}
Check(Cross("샘플", data).Count == 0, "cross 1 샘플 어긋남 없음");
var qX = Scenario(null, null, new[] { "9F-A1,2026-12-30,현장봉함,40,40,40" }, "zones.csv", "pours.csv", "sensors.csv");
try { var xw = Cross("9F-A1 40 MPa", qX); Check(xw.Select(w => w.ZoneId).SequenceEqual(new[] { "9F-A1" }) && xw[0].Diff > 0.25, "cross 2 9F-A1 경고"); }
finally { Directory.Delete(qX, true); }
qX = Scenario(null, null, new[] { "9F-A1,2026-12-30,현장봉함,40,40,40" }, "zones.csv", "pours.csv");
try { Check(Cross("센서 없음", qX).Count == 0, "cross 3 센서 없으면 비교 안 함"); }
finally { Directory.Delete(qX, true); }
// A-26: 센서 없는 구역 근거 = 실제 쓴 기온 출처 + 양생 하한 태그 (weather.csv 8일만 → 섞임)
Check(Evaluator.Evaluate(data, qAt).First(r => r.ZoneId == "10F-A1").EstSource == "기상 예측(센서 없음) — 평년 기온 [가정], 양생 하한 5 °C [가정] · 참고 (판정에 안 씀)", "A-26 평년 기온 (34차 C 참고 표시)");
var q26 = Scenario(null, null, null, "zones.csv", "pours.csv");
File.WriteAllText(Path.Combine(q26, "weather.csv"), "date,avgTa\n" + string.Concat(Enumerable.Range(0, 8).Select(i => $"{new DateOnly(2026, 12, 3).AddDays(i):yyyy-MM-dd},1.0\n")), new UTF8Encoding(false));
try
{
    var r26 = Evaluator.Evaluate(q26, qAt, 3).First(r => r.ZoneId == "10F-A1");
    Check(r26.EstSource == "기상 예측(센서 없음) — 기상 기록 8일 [입력]·평년 기온 20일 [가정], 양생 하한 3 °C [가정] · 참고 (판정에 안 씀)", "A-26 섞임: " + r26.EstSource);
    if (python is not null)
    {
        var py26 = Path.Combine(Path.GetTempPath(), "RevitOnSite_py_a26.csv");
        RunPy("evaluate.py", q26, "--at", Stamp(qAt), "--cure-min", "3", "--out", py26);
        SameBytes("evaluate A-26 기상 기록 섞임", py26, p => Evaluator.Write(Evaluator.Evaluate(q26, qAt, 3), p));
    }
}
finally { Directory.Delete(q26, true); }
// A-28: 400일 안에 기준 강도에 못 닿으면 입력 오류 문구
var q28 = Scenario(Box(("1F-A", 1)), new[] { "1F-A,2026-05-01,08:00,보통,24" }, null);
File.WriteAllText(Path.Combine(q28, "weather.csv"), "date,avgTa\n" + string.Concat(Enumerable.Range(0, 420).Select(i => $"{new DateOnly(2026, 5, 1).AddDays(i):yyyy-MM-dd},-60\n")), new UTF8Encoding(false));
try { Evaluator.Evaluate(q28, new DateTime(2026, 6, 1, 9, 0, 0), -60); Check(false, "A-28 400일 — 오류가 나야 함"); }
catch (InvalidDataException x) { Check(x.Message.Contains("400일 안에 기준 24 MPa 에 도달 안 함"), "A-28 문구: " + x.Message); }
finally { Directory.Delete(q28, true); }
// A-29: fck 가 프로젝트 기본값과 다름 / 공시체 3개 범위 > 평균 15 % → 경고만 — Python input_checks 와 줄 대조
string InputText(IEnumerable<InputWarning> ws) => string.Join("\n", ws.Select(w => $"{w.ZoneId}|{w.Floor}|{w.Kind}|{w.Text}"));
List<InputWarning> Inputs(string label, string folder, DateTime when, double? dfck)
{
    var ws = Evaluator.InputChecks(folder, when, dfck);
    if (python is not null)
    {
        var pyI = RunPy("-c", "import sys, evaluate as ev\nfrom datetime import datetime\n"
            + "for w in ev.input_checks(sys.argv[1], datetime.fromisoformat(sys.argv[2]), float(sys.argv[3]) if sys.argv[3] else None): "
            + "print(w['zone_id'], w['floor'], w['kind'], w['text'], sep='|')", folder, Stamp(when), dfck?.ToString(inv) ?? "").TrimEnd('\n');
        bool same = pyI == InputText(ws);
        Console.WriteLine($"input_checks {label}: {ws.Count}건, Python 과 같음 {(same ? "예" : "아니오")}");
        if (!same) Console.WriteLine($"    Python: {pyI}\n    C#    : {InputText(ws)}");
        Check(same, "input_checks " + label + " Python 대조");
    }
    return ws;
}
Check(Inputs("샘플", data, qAt, 24).Count == 0, "A-29 샘플 경고 없음");
var q29 = Scenario(Box(("1F-A", 1), ("2F-A", 2)), new[] { "1F-A,2026-05-01,08:00,보통,18", "2F-A,2026-05-08,08:00,보통,24" },
    new[] { "2F-A,2026-05-20,현장봉함,30,30,30", "2F-A,2026-06-01,현장봉함,20,24,28", "1F-A,2026-06-01,표준양생,10,24,40" });
try
{
    var w29 = Inputs("fck 18·편차", q29, new DateTime(2026, 6, 1, 9, 0, 0), 24);
    Check(w29.Select(w => (w.ZoneId, w.Kind)).SequenceEqual(new[] { ("1F-A", "fck 확인"), ("2F-A", "공시체 편차") }), "A-29 종류 " + InputText(w29));
    Check(w29.Count == 2 && w29[1].Text == "2F-A: 공시체 2026-06-01 3개 범위 8.0 MPa > 평균 24.0 의 15% [가정] — 시편·시험 기록을 확인하세요 (판정은 그대로)", "A-29 문구");
    Check(Evaluator.InputChecks(q29, new DateTime(2026, 6, 1, 9, 0, 0)).Select(w => w.Kind).SequenceEqual(new[] { "공시체 편차" }), "A-29 기본 fck 없으면 fck 점검 안 함");
}
finally { Directory.Delete(q29, true); }
Console.WriteLine(fails == failsBefore ? "내부 감사 회귀 OK" : "내부 감사 회귀 FAIL");

// ── 7. 승인 기록·요청서 (approval.py) + 타설 전 게이트 (pour_gate.py) — 자체 검사 이식, Python 있으면 같은 입력의 approvals.csv·요청서 HTML 바이트, 해시, 게이트 결과 대조
failsBefore = fails;
Console.WriteLine("\n승인 기록·타설 전 게이트");
{
    var okRow = new Dictionary<string, string> { ["zone_id"] = "9F-A1", ["decision"] = "해체 승인 요청 가능", ["reason"] = "기준 충족", ["threshold"] = "24.0", ["f_cyl"] = "24.8",
        ["cyl_test_date"] = "2026-12-30", ["cyl_curing"] = "현장봉함", ["f_est_min"] = "24.1", ["est_source"] = "센서", ["level_gate"] = "OK", ["pour_date"] = "2026-11-26", ["age_days"] = "35.0" };
    var noRow = new Dictionary<string, string>(okRow) { ["zone_id"] = "9F-A2", ["decision"] = "존치 (구역 최소 강도 미달)", ["reason"] = "구역 최소 추정 23.90 < 24 MPa" };
    Check(Approval.EvidenceSha(okRow) == Approval.EvidenceSha(okRow.Reverse().ToDictionary(kv => kv.Key, kv => kv.Value)), "approval 1 열 순서 무관");
    var t = Directory.CreateTempSubdirectory("RevitOnSite_appr_").FullName;
    try
    {
        Approval.Record(t, "9F-A1", "요청 제출", "김현장", okRow, now: new DateTime(2026, 12, 31, 10, 0, 0));
        Approval.Record(t, "9F-A1", "감리 승인 기록", "이감리", okRow, now: new DateTime(2026, 12, 31, 14, 0, 0));
        string refused = "";
        try { Approval.Record(t, "9F-A2", "감리 승인 기록", "이감리", noRow); } catch (InvalidOperationException x) { refused = x.Message; }
        Check(refused.Contains("해체 승인 요청 가능일 때만"), "approval 2 존치 구역 승인 기록 거부: " + refused);
        Approval.Record(t, "9F-A1", "해체 완료", "김현장", okRow, now: new DateTime(2027, 1, 2, 9, 0, 0));
        var so = Approval.StatusOverride(t);
        Check(Approval.Verify(t) == (true, "기록 3건, 사슬 정상") && so.Count == 1 && so["9F-A1"] == 7, "approval 3 사슬·상태");
        // 판정일까지의 기록만 (20차, approval.py 자체 검사): 12-28 → 없음 · 12-31 09:00 → 같은 날 14:00 기록 6 · 1-2 09:00 → 7
        Check(Approval.StatusOverride(t, new DateTime(2026, 12, 28, 9, 0, 0)).Count == 0 && Approval.StatusOverride(t, new DateTime(2026, 12, 31, 9, 0, 0))["9F-A1"] == 6
              && Approval.StatusOverride(t, new DateTime(2027, 1, 2, 9, 0, 0))["9F-A1"] == 7, "approval 3-1 판정일까지의 기록만");
        // 66차: 협력업체 해체 신고 '뺐음' → 7 (안전관리자 '아니오'는 안 셈, 판정일 뒤 신고는 안 셈) — approval.py 자체 검사와 같은 자료
        Approval.Record(t, "9F-B1", "감리 승인 기록", "이감리", new Dictionary<string, string>(okRow) { ["zone_id"] = "9F-B1" }, now: new DateTime(2027, 1, 1, 14, 0, 0));
        File.WriteAllText(Path.Combine(t, "shore_checks.csv"), "timestamp,zone_id,present,actor,source\n2027-01-02T08:00,9F-B1,아니오,안전관리자,\n", new UTF8Encoding(false));
        bool rep6 = Approval.StatusOverride(t)["9F-B1"] == 6;
        File.AppendAllText(Path.Combine(t, "shore_checks.csv"), "2027-01-02 10:30,9F-B1,아니오,반장,협력업체 신고\n", new UTF8Encoding(false));
        Check(rep6 && Approval.StatusOverride(t)["9F-B1"] == 7 && Approval.StatusEvents(t)["9F-B1"] == (7, "2027-01-02 10:30")
              && Approval.StatusOverride(t, new DateTime(2027, 1, 1, 9, 0, 0))["9F-B1"] == 6, "approval 3-2 협력업체 해체 신고 → 7");
        File.Delete(Path.Combine(t, "shore_checks.csv"));
        var p = Path.Combine(t, Approval.Log);
        File.WriteAllText(p, File.ReadAllText(p).Replace("이감리", "박감리"), new UTF8Encoding(false));      // 기록 고치기 → 찾음
        var (vok, vmsg) = Approval.Verify(t);
        Check(!vok && vmsg.Contains("2번째"), "approval 4 고친 기록 찾음: " + vmsg);
        string refused2 = "";
        try { Approval.Record(t, "9F-A1", "재받침", "김현장", okRow); } catch (InvalidOperationException x) { refused2 = x.Message; }
        Check(refused2.StartsWith("승인 기록이 손상돼"), "approval 5 손상된 기록에 이어 쓰지 않음");
        var sample = Evaluator.Evaluate(data, new DateTime(2026, 12, 31, 9, 0, 0));
        var h = Approval.RequestHtml(sample, "데모", new DateTime(2026, 12, 31, 9, 0, 0), t);
        Check(h.Contains("9F-A1") && h.Contains("<td>9F-A2</td>") && h.Contains("사슬 손상") && !h.Contains("승인합니다"), "approval 6 요청서");
        Check(System.Text.RegularExpressions.Regex.Matches(h, "<section class=\"page\">").Count == 4 && h.Contains(Approval.FormFoot) && h.Contains("공사 참여자 실명부")
              && System.Text.RegularExpressions.Regex.Matches(h, "height:22px").Count == 8 * 18 && h.Contains("4.2<br>해체 중"), "approval 6-1 요청서 4쪽 서식 (총괄 11-05)");
    }
    finally { Directory.Delete(t, true); }
    // 시연 시각 (approval.py record_clock): 판정 기준 시각이 미래면 그 시각 + note 표시, 지난 판정이면 실제 시계 그대로 (사슬 형식 같음)
    var t2 = Directory.CreateTempSubdirectory("RevitOnSite_clock_").FullName;
    try
    {
        var now0 = DateTime.Now;
        var future = new DateTime(now0.Year, now0.Month, now0.Day, now0.Hour, now0.Minute, now0.Second).AddDays(400);
        var past = new DateTime(2020, 1, 1, 9, 0, 0);
        Check(Approval.RecordClock(future) == (future, true) && !Approval.RecordClock(past).Demo && !Approval.RecordClock(null).Demo, "시연 시각 1 RecordClock");
        var r1 = Approval.Record(t2, "9F-A1", "요청 제출", "김현장", okRow, "요청서 제출", evalAt: future);
        Check(r1["timestamp"] == future.ToString("yyyy-MM-dd'T'HH:mm:ss", inv) && r1["note"] == "요청서 제출 " + Approval.DemoNote, "시연 시각 2 미래 판정일 → 고정·note: " + r1["timestamp"] + " " + r1["note"]);
        var before = DateTime.Now.AddSeconds(-1);
        var r2 = Approval.Record(t2, "9F-A1", "감리 승인 기록", "이감리", okRow, evalAt: past);
        var ts2 = DateTime.ParseExact(r2["timestamp"], "yyyy-MM-dd'T'HH:mm:ss", inv);
        Check(before <= ts2 && ts2 <= DateTime.Now && r2["note"] == "" && Approval.Verify(t2).Ok, "시연 시각 3 지난 판정일 → 실제 시계: " + r2["timestamp"]);
    }
    finally { Directory.Delete(t2, true); }

    // pour_gate.py 자체 검사와 같은 사례
    var g0 = Directory.CreateTempSubdirectory("RevitOnSite_gate_pour_").FullName;
    try
    {
        File.WriteAllText(Path.Combine(g0, "zones.csv"), "zone_id,floor,x0,y0,x1,y1\n" + string.Concat(Enumerable.Range(1, 5).Select(k => $"{k}F-A,{k},0,0,10,10\n")), new UTF8Encoding(false));
        var gAt = new DateTime(2026, 6, 1, 9, 0, 0);
        void Checks(IEnumerable<string> rows) => File.WriteAllText(Path.Combine(g0, PourGate.ChecksFile), "zone_id,timestamp,present,actor,photo\n" + string.Concat(rows.Select(r => r + "\n")), new UTF8Encoding(false));
        var ok3 = new[] { 2, 3, 4 }.Select(k => $"{k}F-A,2026-06-01T07:30,예,박안전,{k}F.jpg").ToList();
        string GateText(PourGateResult g) => g.Decision + "\n" + g.Reason + string.Concat(g.Checks.Select(c => $"\n{c.Floor}|{c.ZoneId}|{(c.Ok ? "True" : "False")}|{c.Reason}"));
        PourGateResult Gate2(string label, string zone)
        {
            var g = PourGate.Evaluate(g0, zone, gAt);
            if (python is not null)
            {
                var pyG = RunPy("-c", "import sys, pour_gate as pg\nfrom datetime import datetime\n"
                    + "g = pg.pour_gate(sys.argv[1], sys.argv[2], datetime.fromisoformat(sys.argv[3]))\nprint(g['decision']); print(g['reason'])\n"
                    + "for c in g['checks']: print(c['floor'], c['zone_id'], c['ok'], c['reason'], sep='|')", g0, zone, Stamp(gAt)).TrimEnd('\n');
                bool same = pyG == GateText(g);
                if (!same) Console.WriteLine($"    Python: {pyG.Replace("\n", " / ")}\n    C#    : {GateText(g).Replace("\n", " / ")}");
                Check(same, "pour_gate " + label + " Python 대조");
            }
            return g;
        }
        Checks(ok3);
        var gg = Gate2("3개층 확인", "5F-A");
        Check(gg.Decision == "타설 요청 가능" && gg.Checks.Select(c => c.Floor).SequenceEqual(new[] { 4, 3, 2 }), "pour_gate 1");
        Checks(ok3.Take(2).Append("2F-A,2026-05-30T07:30,예,박안전,2F.jpg"));                                   // 2F 확인이 오래됨
        Check(Gate2("오래된 확인", "5F-A").Reason.Contains("최근 24시간"), "pour_gate 2");
        Checks(ok3.Append("3F-A,2026-06-01T08:10,아니오,박안전,3F.jpg"));                                       // 화정형: 아래층 동바리 빠짐
        gg = Gate2("동바리 없음", "5F-A");
        Check(gg.Decision == "타설 보류" && gg.Reason.Contains("3F-A") && gg.Reason.Contains("동바리 없음"), "pour_gate 3");
        Checks(ok3);
        var row4 = new Dictionary<string, string> { ["zone_id"] = "4F-A", ["decision"] = "해체 승인 요청 가능" };
        Approval.Record(g0, "4F-A", "감리 승인 기록", "이감리", row4, now: new DateTime(2026, 5, 31, 10, 0, 0));
        Approval.Record(g0, "4F-A", "해체 완료", "김현장", row4, now: new DateTime(2026, 5, 31, 16, 0, 0));        // 확인보다 해체 기록이 우선
        Check(Gate2("해체 완료 기록", "5F-A").Reason.Contains("해체 완료 기록"), "pour_gate 4");
        gg = Gate2("아래 1층뿐", "2F-A");                                                                          // 아래가 1층뿐 → 그 층만, 확인 없으면 보류
        Check(gg.Decision == "타설 보류" && gg.Checks.Select(c => c.ZoneId).SequenceEqual(new[] { "1F-A" }), "pour_gate 5");
        Checks(ok3.Append("1F-A,2026-06-01T07:30,예,박안전,1F.jpg"));
        Check(Gate2("1층 확인", "2F-A").Decision == "타설 요청 가능", "pour_gate 6");
        Check(Gate2("맨 아래층", "1F-A").Reason == "맨 아래층 — 확인할 아래 동바리 없음", "pour_gate 7 맨 아래층");
    }
    finally { Directory.Delete(g0, true); }

    // Python 과 같은 입력으로 approvals.csv·요청서 HTML 바이트, 판정 행 해시 대조 (메모에 쉼표·따옴표·줄바꿈, 입력자 앞뒤 공백)
    if (python is not null)
    {
        string pyDir = Directory.CreateTempSubdirectory("RevitOnSite_appr_py_").FullName, csDir = Directory.CreateTempSubdirectory("RevitOnSite_appr_cs_").FullName;
        try
        {
            var pyOut = RunPy("-c", "import sys, os, approval as ap, evaluate as ev\nfrom datetime import datetime\nt, data = sys.argv[1], sys.argv[2]\n"
                + "rows = ev.evaluate(data, datetime(2026, 12, 31, 9))\nd = {r['zone_id']: r for r in rows}\n"
                + "ap.record(t, '9F-A1', '요청 제출', '김현장', d['9F-A1'], note='메모, \"따옴표\"\\n둘째 줄', now=datetime(2026, 12, 31, 10))\n"
                + "ap.record(t, '9F-A1', '감리 승인 기록', ' 이감리 ', d['9F-A1'], now=datetime(2026, 12, 31, 14))\n"
                + "ap.record(t, '9F-B2', '재받침', '김현장', d['9F-B2'], now=datetime(2027, 1, 2, 9))\n"
                + "ok, msg = ap.verify(t)\nprint(ok, msg, sep='|')\nprint(sorted(ap.status_override(t).items()))\n"
                + "for r in rows: print(r['zone_id'], ap.evidence_sha(r))\n"
                + "open(os.path.join(t, 'req.html'), 'w', encoding='utf-8', newline='').write(ap.request_html(rows, '데모', datetime(2026, 12, 31, 9), t))",
                pyDir, data).TrimEnd('\n');
            var rows = Evaluator.Evaluate(data, new DateTime(2026, 12, 31, 9, 0, 0));
            var d = rows.ToDictionary(r => r.ZoneId, Approval.RowOf);
            Approval.Record(csDir, "9F-A1", "요청 제출", "김현장", d["9F-A1"], note: "메모, \"따옴표\"\n둘째 줄", now: new DateTime(2026, 12, 31, 10, 0, 0));
            Approval.Record(csDir, "9F-A1", "감리 승인 기록", " 이감리 ", d["9F-A1"], now: new DateTime(2026, 12, 31, 14, 0, 0));
            Approval.Record(csDir, "9F-B2", "재받침", "김현장", d["9F-B2"], now: new DateTime(2027, 1, 2, 9, 0, 0));
            var (cok, cmsg) = Approval.Verify(csDir);
            var csOut = $"{(cok ? "True" : "False")}|{cmsg}\n[" + string.Join(", ", Approval.StatusOverride(csDir).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"('{kv.Key}', {kv.Value})")) + "]"
                + string.Concat(rows.Select(r => $"\n{r.ZoneId} {Approval.EvidenceSha(r)}"));
            File.WriteAllText(Path.Combine(csDir, "req.html"), Approval.RequestHtml(rows, "데모", new DateTime(2026, 12, 31, 9, 0, 0), csDir), new UTF8Encoding(false));
            bool same = pyOut == csOut;
            Console.WriteLine($"approval 검사·상태·판정 해시 {rows.Count}개: Python 과 같음 {(same ? "예" : "아니오")}");
            if (!same) Console.WriteLine($"    Python: {pyOut.Replace("\n", " / ")[..Math.Min(400, pyOut.Length)]}\n    C#    : {csOut.Replace("\n", " / ")[..Math.Min(400, csOut.Length)]}");
            Check(same, "approval Python 대조");
            foreach (var f in new[] { Approval.Log, "req.html" })
            {
                bool sb = File.ReadAllBytes(Path.Combine(pyDir, f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(csDir, f)));
                Console.WriteLine($"{f}: Python 과 바이트 동일 {(sb ? "예" : "아니오")}");
                Check(sb, f + " 바이트 대조 (" + pyDir + " / " + csDir + ")");
            }
        }
        finally
        {
            if (fails == failsBefore) { Directory.Delete(pyDir, true); Directory.Delete(csDir, true); }   // 다르면 남겨서 비교
        }
    }
}
Console.WriteLine(fails == failsBefore ? "승인 기록·타설 전 게이트 OK" : "승인 기록·타설 전 게이트 FAIL");

// ── 8. 센서 없는 기본형 (shoring/test_no_sensor.py, 기획 원칙 3): 공시체 + 기온만으로 판정·한중 경고·요청서·승인 기록·타설 전 게이트·공정 계획이 돈다
failsBefore = fails;
Console.WriteLine("\n센서 없는 기본형");
{
    var ns = Directory.CreateTempSubdirectory("RevitOnSite_nosensor_").FullName;
    try
    {
        foreach (var n in new[] { "zones.csv", "pours.csv", "cylinders.csv" })   // sensors.csv·sensor_strength.csv·weather.csv 없음
            File.Copy(Path.Combine(data, n), Path.Combine(ns, n));
        var nrows = Evaluator.Evaluate(ns, qAt);
        if (python is not null)
        {
            var pyN = Path.Combine(Path.GetTempPath(), "RevitOnSite_py_nosensor.csv");
            RunPy("evaluate.py", ns, "--at", Stamp(qAt), "--out", pyN);
            SameBytes("evaluate 센서 없음", pyN, p => Evaluator.Write(nrows, p));
        }
        Check(nrows.Count > 0 && nrows.All(r => r.EstSource.StartsWith("기상 예측(센서 없음) — 평년 기온 [가정]")), "센서 없음 1 위치 근거 = 기상 예측");
        Check(nrows.Where(r => r.Floor == 9).All(r => r.Decision is "해체 승인 요청 가능" or "존치 (구역 최소 강도 미달)" or "존치 (공시체 미달)"), "센서 없음 2 9층 결론");
        Check(nrows.Where(r => r.Floor >= 10).All(r => r.LevelGate == "NG"), "센서 없음 3 규칙 게이트는 센서와 무관");
        Check(Cold("센서 없음", ns, _ => true).All(k => k == "한중"), "센서 없음 4 한중 여부만 (초기양생 온도 경고 없음)");
        var nh = Approval.RequestHtml(nrows, "센서 없음 시험", qAt, ns);
        Check(nh.Contains("기상 예측(센서 없음)") && nh.Contains("사슬 정상"), "센서 없음 5 요청서");
        var okZone = nrows.First(r => r.Decision == "해체 승인 요청 가능");
        Approval.Record(ns, okZone.ZoneId, "요청 제출", "시험", Approval.RowOf(okZone));
        Check(Approval.Verify(ns).Ok, "센서 없음 6 승인 기록");
        var ng = PourGate.Evaluate(ns, "12F-A1", qAt);
        Check(ng.Decision == "타설 보류" && ng.Reason.Contains("동바리 확인 없음"), "센서 없음 7 타설 전 게이트: " + ng.Reason);
    }
    finally { Directory.Delete(ns, true); }
}
Console.WriteLine(fails == failsBefore ? "센서 없는 기본형 OK" : "센서 없는 기본형 FAIL");

// ── 10. 4D 근거 원장 (evidence.py) — 자체 검사 이식 + 같은 입력의 evidence.csv 바이트·날짜별 원장(단계·첫 날짜·경고)을 Python 과 대조
failsBefore = fails;
Console.WriteLine("\n4D 근거 원장");
{
    var evFile = Path.Combine(FindRoot(), "shoring", "templates", "evidence.csv");   // 첨부 서류 대신 (SHA-256 계산용, 두 쪽 같은 파일)
    const string InstallCsv = "zone_id,item_code,result,inspector,date,photo_path\n9F-B1,H,OK,박안전,2026-11-23,\n9F-B1,S,OK,박안전,2026-11-24,\n"
        + "9F-C1,H,OK,박안전,2026-11-23,\n9F-C1,S,NG,박안전,2026-11-23,\n";
    var okZone = new Dictionary<string, string> { ["zone_id"] = "9F-A1", ["decision"] = "해체 승인 요청 가능" };
    var ed = Directory.CreateTempSubdirectory("RevitOnSite_evidence_").FullName;
    try
    {
        foreach (var n in new[] { "zones.csv", "pours.csv", "cylinders.csv" }) File.Copy(Path.Combine(data, n), Path.Combine(ed, n));
        Evidence.Add(ed, "9F-A1", "2026-11-23", 1, "박안전", "IC-9F-01", "합격");
        Evidence.Add(ed, "9F-A1", "2026-12-20", 2, "김현장", "WP-9F", "", file: evFile);
        Approval.Record(ed, "9F-A1", "요청 제출", "김현장", okZone, now: new DateTime(2026, 12, 31, 10, 0, 0));
        var L = Evidence.LedgerAt(ed, new DateOnly(2026, 12, 29));                       // 공시체(12-30) 전 → 1·2 단계까지
        Check(L["9F-A1"].Stage == 2 && !L["9F-A1"].Dates.ContainsKey(3), "evidence 1 공시체 전 2단계");
        L = Evidence.LedgerAt(ed, new DateOnly(2026, 12, 31));                           // 공시체 3, 검측요청 5, 해체전점검 4 없음 → 갖춘 단계 3
        Check(L["9F-A1"].Stage == 3 && L["9F-A1"].Dates[5] == "2026-12-31" && L["9F-A1"].Warnings.SequenceEqual(new[] { "9F-A1: 검측요청(2026-12-31) 전에 해체전점검 기록 없음" }), "evidence 2 갖춘 단계 3 (11-11 G5: 요청 전 해체 전 점검)");
        Approval.Record(ed, "9F-A2", "해체 완료", "김현장", okZone, now: new DateTime(2026, 12, 31, 11, 0, 0));   // 감리확인 없이 해체 → 경고
        Check(Evidence.LedgerAt(ed, new DateOnly(2026, 12, 31))["9F-A2"].Warnings.Any(w => w.Contains("감리확인")), "evidence 3 감리확인 없이 해체 경고");
        var e2 = Evaluator.Read(ed, Evidence.Ledger).First(x => x["stage"] == "2");
        Check(e2["sha256"]!.Length == 64 && e2["ev_id"] == "E0002", "evidence 4 SHA-256·번호");
        File.WriteAllText(Path.Combine(ed, "install_inspection.csv"), InstallCsv, new UTF8Encoding(false));   // 1 설치검측: 전 항목 OK 인 구역만
        L = Evidence.LedgerAt(ed, new DateOnly(2026, 12, 31));
        Check(L["9F-B1"].Dates[1] == "2026-11-24" && !(L.TryGetValue("9F-C1", out var c1) && c1.Dates.ContainsKey(1)), "evidence 5 설치검측 전 항목 OK");
        string refusedEv = "";
        try { Evidence.Add(ed, "9F-A1", "2026-12-31", 9, "누구"); } catch (InvalidOperationException x) { refusedEv = x.Message; }
        Check(refusedEv.StartsWith("단계 9 없음"), "evidence 6 없는 단계 거부: " + refusedEv);
        if (python is not null)
        {
            var evPy = Directory.CreateTempSubdirectory("RevitOnSite_evidence_py_").FullName;
            try
            {
                string Text(string folder) => string.Join("\n", new[] { "2026-12-29", "2026-12-31" }.SelectMany(d =>
                    Evidence.LedgerAt(folder, DateOnly.ParseExact(d, "yyyy-MM-dd", inv)).OrderBy(kv => kv.Key, StringComparer.Ordinal)
                        .Select(kv => $"{d}|{kv.Key}|{kv.Value.Stage}|{string.Join(";", kv.Value.Dates.OrderBy(q => q.Key).Select(q => $"{q.Key}:{q.Value}"))}|{string.Join(" / ", kv.Value.Warnings)}")));
                var pyOut = RunPy("-c", "import sys, os, shutil, evidence as evd, approval as ap\nfrom datetime import datetime\nt, sample, f, ins = sys.argv[1:5]\n"
                    + "for n in ('zones.csv', 'pours.csv', 'cylinders.csv'): shutil.copy(os.path.join(sample, n), t)\n"
                    + "ok = {'zone_id': '9F-A1', 'decision': '해체 승인 요청 가능'}\n"
                    + "evd.add(t, '9F-A1', '2026-11-23', 1, '박안전', 'IC-9F-01', '합격')\nevd.add(t, '9F-A1', '2026-12-20', 2, '김현장', 'WP-9F', '', file=f)\n"
                    + "ap.record(t, '9F-A1', '요청 제출', '김현장', ok, now=datetime(2026, 12, 31, 10))\nap.record(t, '9F-A2', '해체 완료', '김현장', ok, now=datetime(2026, 12, 31, 11))\n"
                    + "open(os.path.join(t, 'install_inspection.csv'), 'w', encoding='utf-8', newline='').write(ins)\n"
                    + "for d in ('2026-12-29', '2026-12-31'):\n    L = evd.ledger_at(t, d)\n"
                    + "    for z in sorted(L): print(d, z, L[z]['stage'], ';'.join(f\"{s}:{L[z]['dates'][s]}\" for s in sorted(L[z]['dates'])), ' / '.join(L[z]['warnings']), sep='|')",
                    evPy, data, evFile, InstallCsv).TrimEnd('\n');
                bool same = pyOut == Text(ed);
                Console.WriteLine($"evidence 날짜별 원장: Python 과 같음 {(same ? "예" : "아니오")}");
                if (!same) Console.WriteLine($"    Python: {pyOut.Replace("\n", " / ")}\n    C#    : {Text(ed).Replace("\n", " / ")}");
                Check(same, "evidence Python 대조");
                foreach (var f in new[] { Evidence.Ledger, Approval.Log })
                {
                    bool sb = File.ReadAllBytes(Path.Combine(evPy, f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(ed, f)));
                    Console.WriteLine($"{f} (근거 원장 시험): Python 과 바이트 동일 {(sb ? "예" : "아니오")}");
                    Check(sb, f + " 근거 원장 바이트");
                }
                // 61차: make_demo_data --sim (1–4F 해체·해체 후 점검 5항목 → 원장 8) + 9F-B1 설치 검측 NG 뒤 같은 날 OK (→ 원장 1, 날짜 단위) — Python 대조
                var simDir = Directory.CreateTempSubdirectory("RevitOnSite_sim_").FullName;
                try
                {
                    RunPy(Path.Combine(FindRoot(), "tools", "make_demo_data.py"), simDir, "--sim");
                    File.AppendAllText(Path.Combine(simDir, "install_inspection.csv"), "9F-B1,SHR-V,NG,박안전,2026-11-24,\n9F-B1,SHR-V,OK,박안전,2026-11-24,\n", new UTF8Encoding(false));
                    string Days(string folder) => string.Join("\n", new[] { "2026-11-23", "2026-11-24" }.SelectMany(d =>
                        Evidence.LedgerAt(folder, DateOnly.ParseExact(d, "yyyy-MM-dd", inv)).OrderBy(kv => kv.Key, StringComparer.Ordinal)
                            .Select(kv => $"{d}|{kv.Key}|{kv.Value.Stage}|{string.Join(";", kv.Value.Dates.OrderBy(q => q.Key).Select(q => $"{q.Key}:{q.Value}"))}|{string.Join(" / ", kv.Value.Warnings)}")));
                    var pySim = RunPy("-c", "import sys, evidence as evd\nt = sys.argv[1]\nfor d in ('2026-11-23', '2026-11-24'):\n    L = evd.ledger_at(t, d)\n"
                        + "    for z in sorted(L): print(d, z, L[z]['stage'], ';'.join(f\"{s}:{L[z]['dates'][s]}\" for s in sorted(L[z]['dates'])), ' / '.join(L[z]['warnings']), sep='|')", simDir).TrimEnd('\n');
                    bool sameSim = pySim == Days(simDir);
                    var l24 = Evidence.LedgerAt(simDir, new DateOnly(2026, 11, 24));
                    Console.WriteLine($"evidence --sim 원장 (11-23·11-24 {pySim.Split('\n').Length}줄): Python 과 같음 {(sameSim ? "예" : "아니오")} · 1F-B1 {l24["1F-B1"].Stage}/8 · 9F-B1 원장 1 {l24["9F-B1"].Dates.GetValueOrDefault(1)}");
                    if (!sameSim) Console.WriteLine($"    Python: {pySim.Replace("\n", " / ")}\n    C#    : {Days(simDir).Replace("\n", " / ")}");
                    Check(sameSim && l24["1F-B1"].Stage == 8 && l24["9F-B1"].Dates.GetValueOrDefault(1) == "2026-11-24" && !Evidence.LedgerAt(simDir, new DateOnly(2026, 11, 23)).ContainsKey("9F-B1"),
                        "evidence 61차 --sim: 원장 8(해체 후 점검)·NG 뒤 OK 원장 1·Python 대조");
                }
                finally { Directory.Delete(simDir, true); }
            }
            finally { Directory.Delete(evPy, true); }
        }
    }
    finally { Directory.Delete(ed, true); }
    // 무단 해체 관측 (evidence.py removal_watch 자체 검사 사례): 9F-A2 감리 승인 기록 없이 해체 완료 → 경고 / 9F-A1 감리 승인 기록 뒤 '아니오' → 없음 /
    // 9F-B1 승인 없이 '아니오' → 경고, 재받침만으로는 그대로, 재받침 뒤 '예' → 해제. C# 이 쓴 폴더를 Python 이 읽어 같은 결과인지 대조
    var rw = Directory.CreateTempSubdirectory("RevitOnSite_removal_").FullName;
    try
    {
        foreach (var n in new[] { "zones.csv", "pours.csv", "cylinders.csv" }) File.Copy(Path.Combine(data, n), Path.Combine(rw, n));
        string WatchText(SortedDictionary<string, string> w) => string.Join(" / ", w.Select(kv => kv.Key + "=" + kv.Value));
        void SamePy(string label, string day)
        {
            var cs = WatchText(Evidence.RemovalWatch(rw, DateOnly.ParseExact(day, "yyyy-MM-dd", inv)));
            if (python is null) return;
            var pyW = RunPy("-c", "import sys, evidence as evd" + "\n" + "w = evd.removal_watch(sys.argv[1], sys.argv[2])" + "\n"
                + "print(' / '.join(k + '=' + v for k, v in w.items()))", rw, day).TrimEnd('\n');
            Console.WriteLine($"removal_watch {label}: Python 과 같음 {(pyW == cs ? "예" : "아니오")}");
            if (pyW != cs) Console.WriteLine($"    Python: {pyW}\n    C#    : {cs}");
            Check(pyW == cs, "removal_watch " + label + " Python 대조");
        }
        Approval.Record(rw, "9F-A2", "해체 완료", "김현장", okZone, now: new DateTime(2026, 12, 31, 11, 0, 0));
        Approval.Record(rw, "9F-A1", "감리 승인 기록", "이감리", okZone, now: new DateTime(2026, 12, 31, 12, 0, 0));
        File.WriteAllText(Path.Combine(rw, PourGate.ChecksFile), "zone_id,timestamp,present,actor,photo\n9F-A1,2026-12-31T13:00,아니오,박안전,\n9F-B1,2026-12-31T08:00,아니오,박안전,\n", new UTF8Encoding(false));
        var w1 = Evidence.RemovalWatch(rw, new DateOnly(2026, 12, 31));
        Check(WatchText(w1) == "9F-A2=승인 기록 없이 동바리 없음 관측 — 9F-A2 12-31 11:00 김현장 / 9F-B1=승인 기록 없이 동바리 없음 관측 — 9F-B1 12-31 08:00 박안전", "removal 1 " + WatchText(w1));
        Check(Evidence.RemovalWatch(rw, new DateOnly(2026, 12, 30)).Count == 0, "removal 2 그 날짜 뒤 기록은 안 봄");
        Check(Evidence.LedgerAt(rw, new DateOnly(2026, 12, 31))["9F-B1"].Warnings.Contains(w1["9F-B1"]), "removal 3 근거 원장 경고에 들어감");
        SamePy("승인 없음·승인 뒤", "2026-12-31");
        Approval.Record(rw, "9F-B1", "재받침", "김현장", okZone, now: new DateTime(2026, 12, 31, 15, 0, 0));
        Check(Evidence.RemovalWatch(rw, new DateOnly(2026, 12, 31)).ContainsKey("9F-B1"), "removal 4 재받침 기록만으로는 해제 안 됨");
        File.AppendAllText(Path.Combine(rw, PourGate.ChecksFile), "9F-B1,2026-12-31T16:00,예,박안전,\n", new UTF8Encoding(false));
        var rw2 = Evidence.RemovalWatch(rw, new DateOnly(2026, 12, 31));
        Check(!rw2.ContainsKey("9F-B1") && rw2.ContainsKey("9F-A2"), "removal 5 재받침 뒤 '예' → 해제");
        SamePy("재받침 뒤 예", "2026-12-31");
        // 서명 전 해체 관측 (evidence.py signature_watch, 11-05): 9F-C1 서명 15:00 전 14:00 '아니오' → 경고 / 9F-C2 서명(날짜만) 뒤 '아니오' → 없음
        Approval.Record(rw, "9F-C1", "감리 승인 기록", "이감리", okZone, now: new DateTime(2026, 12, 31, 16, 0, 0), approver: "이감리", signedAt: "2026-12-31 15:00");
        Approval.Record(rw, "9F-C2", "감리 승인 기록", "이감리", okZone, now: new DateTime(2026, 12, 31, 16, 0, 0), approver: "이감리", signedAt: "2026-12-31");
        File.AppendAllText(Path.Combine(rw, PourGate.ChecksFile), "9F-C1,2026-12-31T14:00,아니오,박안전,\n9F-C2,2027-01-01T08:00,아니오,박안전,\n", new UTF8Encoding(false));
        var sw = Evidence.SignatureWatch(rw, new DateOnly(2027, 1, 1));
        Check(sw.Count == 1 && sw[0].Value == "서명 전 해체 관측 — 9F-C1 동바리 없음 12-31 14:00 박안전 · 감리 서명 12-31 15:00"
              && Evidence.LedgerAt(rw, new DateOnly(2027, 1, 1))["9F-C1"].Warnings.Contains(sw[0].Value) && Approval.Verify(rw).Ok, "signature 1 서명 전 해체 관측");
        if (python is not null)
        {
            var pySw = RunPy("-c", "import sys, evidence as evd\nprint(' / '.join(k + '=' + v for k, v in evd.signature_watch(sys.argv[1], '2027-01-01').items()))", rw).TrimEnd('\n');
            var csSw = string.Join(" / ", sw.Select(kv => kv.Key + "=" + kv.Value));
            Console.WriteLine($"signature_watch: Python 과 같음 {(pySw == csSw ? "예" : "아니오")}");
            Check(pySw == csSw, "signature_watch Python 대조: " + pySw);
        }
    }
    finally { Directory.Delete(rw, true); }
    // 감리 성명·서명 일시 열 (approval.py 11-05): 옛 9열 approvals.csv 에 감리 승인 기록(성명·일시) 을 이어 쓰면 머리만 11열로, 사슬 정상 — Python 과 바이트 대조, 성명 지우면 드러남
    var am = Directory.CreateTempSubdirectory("RevitOnSite_approver_").FullName;
    try
    {
        var oldRow = new Dictionary<string, string?> { ["seq"] = "1", ["timestamp"] = "2026-12-31T10:00:00", ["zone_id"] = "9F-A1", ["action"] = "요청 제출", ["actor"] = "김현장",
            ["note"] = "", ["evidence_sha"] = Approval.EvidenceSha(okZone), ["prev_sha"] = Approval.Genesis };
        oldRow["sha"] = Approval.LineSha(oldRow);
        string oldText = string.Join(",", Approval.FieldsV1) + "\r\n" + string.Join(",", Approval.FieldsV1.Select(k => oldRow[k])) + "\r\n";
        File.WriteAllText(Path.Combine(am, Approval.Log), oldText, new UTF8Encoding(false));
        Approval.Record(am, "9F-A1", "감리 승인 기록", "이감리", okZone, now: new DateTime(2026, 12, 31, 14, 0, 0), approver: "이감리", signedAt: "2026-12-31 13:30");
        Check(Approval.Verify(am) == (true, "기록 2건, 사슬 정상") && File.ReadLines(Path.Combine(am, Approval.Log)).First() == string.Join(",", Approval.Fields), "approver 1 옛 파일에 이어 쓰기");
        string refusedA = "";
        try { Approval.Record(am, "9F-A1", "해체 완료", "김현장", okZone, approver: "이감리"); } catch (InvalidOperationException x) { refusedA = x.Message; }
        Check(refusedA.Contains("감리 승인 기록'에만"), "approver 2 다른 행위엔 안 받음");
        if (python is not null)
        {
            var amPy = Directory.CreateTempSubdirectory("RevitOnSite_approver_py_").FullName;
            try
            {
                File.WriteAllText(Path.Combine(amPy, Approval.Log), oldText, new UTF8Encoding(false));
                RunPy("-c", "import sys, approval as ap\nfrom datetime import datetime\nok = {'zone_id': '9F-A1', 'decision': '해체 승인 요청 가능'}\n"
                    + "ap.record(sys.argv[1], '9F-A1', '감리 승인 기록', '이감리', ok, now=datetime(2026, 12, 31, 14), approver='이감리', signed_at='2026-12-31 13:30')", amPy);
                bool sa = File.ReadAllBytes(Path.Combine(amPy, Approval.Log)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(am, Approval.Log)));
                Console.WriteLine($"approvals.csv (옛 9열 + 감리 성명·서명 일시): Python 과 바이트 동일 {(sa ? "예" : "아니오")}");
                Check(sa, "approver Python 바이트");
            }
            finally { Directory.Delete(amPy, true); }
        }
        var ap = Path.Combine(am, Approval.Log);
        File.WriteAllText(ap, File.ReadAllText(ap).Replace(",이감리,2026-12-31T13:30", ",,2026-12-31T13:30"), new UTF8Encoding(false));
        Check(!Approval.Verify(am).Ok, "approver 3 성명 지우면 사슬 손상");
    }
    finally { Directory.Delete(am, true); }
}
Console.WriteLine(fails == failsBefore ? "4D 근거 원장 OK" : "4D 근거 원장 FAIL");

// ── 11. 감리가 현장에서 확인할 3가지·마감 값 (approval.py site_checks) — 시연 폴더(tools\make_demo_data.py)에서 요청서 HTML 을 Python 과 바이트 대조:
//        ① 그대로 ② 위층 동바리 확인 3줄 뒤 (마감 값은 그대로여야 함) ③ 승인 기록 1건 뒤 (사슬 끝 해시가 마감 값에 들어감)
failsBefore = fails;
Console.WriteLine("\n감리 현장 확인·마감 값");
if (python is not null)
{
    var dm = Directory.CreateTempSubdirectory("RevitOnSite_site_").FullName;
    var outDir = Directory.CreateTempSubdirectory("RevitOnSite_site_out_").FullName;
    try
    {
        RunPy(Path.Combine(FindRoot(), "tools", "make_demo_data.py"), dm);
        var sAt = new DateTime(2026, 12, 31, 9, 0, 0);
        string? lastSeal = null;
        void Case(string label)
        {
            var pyHtml = Path.Combine(outDir, label + "_py.html");
            RunPy("-c", "import sys, approval as ap, evaluate as ev\nfrom datetime import datetime\nf, out = sys.argv[1:3]\nat = datetime(2026, 12, 31, 9)\n"
                + "open(out, 'w', encoding='utf-8', newline='').write(ap.request_html(ev.evaluate(f, at), '데모', at, f, ev.gate_details(f, at)))", dm, pyHtml);   // 11-11: 지지층·하중비 칸까지 대조
            var cs = Approval.RequestHtml(Evaluator.Evaluate(dm, sAt), "데모", sAt, dm, Evaluator.GateDetails(dm, sAt));
            bool sb = Encoding.UTF8.GetBytes(cs).AsSpan().SequenceEqual(File.ReadAllBytes(pyHtml));
            var m = System.Text.RegularExpressions.Regex.Match(cs, "마감 값 <b>([0-9a-f]{8})</b>");
            Console.WriteLine($"요청서 ({label}): Python 과 바이트 동일 {(sb ? "예" : "아니오")} · 마감 값 {(m.Success ? m.Groups[1].Value : "없음")}");
            if (!sb) File.WriteAllText(Path.Combine(outDir, label + "_cs.html"), cs, new UTF8Encoding(false));
            Check(sb, "site_checks " + label + " 바이트 (" + outDir + ")");
            Check(m.Success && cs.Contains("감리가 현장에서 확인할 3가지"), "site_checks " + label + " 표·마감 값 있음");
            if (label == "동바리 확인 3줄") Check(m.Groups[1].Value == lastSeal, "site_checks 동바리 확인은 마감 값에 안 들어감");
            if (label == "승인 기록 1건") Check(m.Groups[1].Value != lastSeal, "site_checks 승인 기록 끝 해시가 마감 값에 들어감");
            if (label == "시연 폴더") Check(cs.Contains("<h2><i>⑥</i>이 층·바로 위 3개층 현장 경고</h2><p class=\"note\">없음</p>"), "site_checks 현장 경고 없음 (ⓐ-1)");
            if (label == "같은 층 9F-A2 아니오") Check(m.Groups[1].Value == lastSeal, "site_checks 현장 경고는 마감 값에 안 들어감 (ⓐ-1)");
            lastSeal = m.Groups[1].Value;
        }
        Case("시연 폴더");
        File.WriteAllText(Path.Combine(dm, PourGate.ChecksFile), "zone_id,timestamp,present,actor,photo\n10F-A1,2026-12-31T07:30,예,박안전,\n"
            + "11F-A1,2026-12-31T07:40,예,박안전,\n12F-A1,2026-12-30T07:00,예,박안전,\n", new UTF8Encoding(false));   // 12F 는 26 h 전 → ✕
        Case("동바리 확인 3줄");
        var ready = Evaluator.Evaluate(dm, sAt).First(r => r.Decision == "해체 승인 요청 가능");
        Approval.Record(dm, ready.ZoneId, "요청 제출", "김현장", Approval.RowOf(ready), now: new DateTime(2026, 12, 31, 10, 0, 0));
        Case("승인 기록 1건");
        File.AppendAllText(Path.Combine(dm, PourGate.ChecksFile), "10F-B1,2026-12-31T08:00,아니오,박안전,\n", new UTF8Encoding(false));   // 감리 승인 기록 없이 동바리 없음
        Case("무단 해체 관측");
        Check(Approval.RequestHtml(Evaluator.Evaluate(dm, sAt), "데모", sAt, dm).Contains("⚠ 무단 해체 의심 — 확인 필요: 10F-B1 · "), "site_checks ① 무단 해체 의심 표시");
        // ⓐ-1 (총괄 적합성 검사): 같은 층 다른 구역(존치 9F-A2)의 무단 해체 의심 — ① 칸(위층 겹침)에는 안 걸리지만 '이 층·바로 위 3개층 현장 경고'에 보여야 함
        File.AppendAllText(Path.Combine(dm, PourGate.ChecksFile), "9F-A2,2026-12-31T08:40,아니오,박안전,\n", new UTF8Encoding(false));
        Case("같은 층 9F-A2 아니오");
        var near = Approval.RequestHtml(Evaluator.Evaluate(dm, sAt), "데모", sAt, dm);
        Check(near.Contains("<h2><i>⑥</i>이 층·바로 위 3개층 현장 경고</h2><ul class=\"warn\"><li>승인 기록 없이 동바리 없음 관측 — 9F-A2 12-31 08:40") && near.Contains("<li>승인 기록 없이 동바리 없음 관측 — 10F-B1"),
            "site_checks 현장 경고 9F-A2 → 10F-B1 (ⓐ-1, 층 순 — 총괄 11-05)");
    }
    finally
    {
        Directory.Delete(dm, true);
        if (fails == failsBefore) Directory.Delete(outDir, true);   // 다르면 남겨서 비교
    }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "감리 현장 확인·마감 값 OK" : "감리 현장 확인·마감 값 FAIL");

// ── 12. 층 표지 (FloorSign, Python 기준본 없음 — 기존 결과만 옮김): 시연 폴더에서 층·상태 줄·구역 줄·마감 값(검측요청서와 같은 값)·지시 문구 없음
failsBefore = fails;
Console.WriteLine("\n층 표지");
Check(FloorSign.FloorName(-1, null) == "B1" && FloorSign.FloorName(12, "PH-A1") == "PH" && FloorSign.FloorName(3, "3F-A1") == "3F" && FloorSign.FloorName(4, "Z9") == "4F", "floor_sign 층 이름 규칙");
if (python is not null)
{
    var fd = Directory.CreateTempSubdirectory("RevitOnSite_floorsign_").FullName;
    try
    {
        RunPy(Path.Combine(FindRoot(), "tools", "make_demo_data.py"), fd);
        var fAt = new DateTime(2026, 12, 31, 9, 0, 0);
        var fRows = Evaluator.Evaluate(fd, fAt);
        var fReady = fRows.First(r => r.ZoneId == "9F-A1");
        Approval.Record(fd, "9F-A1", "요청 제출", "김현장", Approval.RowOf(fReady), now: new DateTime(2026, 12, 31, 10, 0, 0));
        Approval.Record(fd, "9F-A1", "감리 승인 기록", "이감리", Approval.RowOf(fReady), now: new DateTime(2026, 12, 31, 14, 0, 0));
        File.WriteAllText(Path.Combine(fd, PourGate.ChecksFile), "zone_id,timestamp,present,actor,photo\n9F-A1,2026-12-31T07:30,예,박안전,\n9F-A1,2027-01-01T07:30,아니오,박안전,\n", new UTF8Encoding(false));   // 판정 시각 뒤 줄은 안 셈
        var fl = FloorSign.Floors(fd, fRows, fAt);
        var f9 = fl.First(f => f.Floor == 9);
        var a1 = f9.Zones.First(z => z.ZoneId == "9F-A1");
        Check(string.Join(",", fl.Select(f => f.Name)) == "9F,10F,11F,12F", "floor_sign 1 층 " + string.Join(",", fl.Select(f => f.Name)));
        Check(f9.Status == "동바리 해체 금지 — 존치 2구역" && f9.Kind == "hold", "floor_sign 2 9F 상태: " + f9.Status);
        Check(fl.First(f => f.Floor == 10).Status.StartsWith("동바리 해체 금지 — 존치 "), "floor_sign 3 10F 상태: " + fl.First(f => f.Floor == 10).Status);
        Check(a1.Conclusion == "감리 확인 기록 있음 (12-31)" && a1.Evidence == 3 && a1.Shore == "12-31 07:30 예 (박안전)", $"floor_sign 4 9F-A1 줄: {a1}");
        var allDone = new FloorSign.FloorLine(1, "1F", "해체 완료 기록 있음 — 1구역", "checked", new() { new("1F-A1", "해체 완료 기록 (11-01)", "—", 7, "—", 7) });
        var unpoured = new FloorSign.FloorLine(13, "13F", "동바리 해체 금지 — 타설 전 1구역", "hold", new() { new("13F-A1", "타설 전", "—", 0, "—", 0) });
        Check(FloorSign.Standing(fl).Count == fl.Count && FloorSign.Standing(new[] { allDone, unpoured, f9 }).Select(f => f.Floor).SequenceEqual(new[] { 9 }),
            "floor_sign 2-6 동바리 서 있는 층만 (해체 완료·타설 전 층 뺌)");
        var fReq = Approval.RequestRows(fRows, fd, fAt).Where(r => r.Decision == "해체 승인 요청 가능").ToList();   // 요청서와 같은 구역 (9F-A1 감리 승인 기록은 빠짐)
        string fSeal = Approval.SealValue(fReq, fd, fAt);
        Check(Approval.RequestHtml(fRows, "데모", fAt, fd).Contains($"마감 값 <b>{fSeal}</b>"), "floor_sign 5 마감 값 = 검측요청서 값 " + fSeal);
        var withQr = FloorSign.Page("데모", fAt, fl, f => "http://192.168.0.10:8765/?t=abc123#floor=" + f, fSeal);
        var noQr = FloorSign.Page("데모", fAt, fl, _ => null, null);
        // v6.2 W10: 인쇄 시각 주면 머리에 크게 + 유효 시간, QR 주소에 ps·pz (구역 수만큼 상태 숫자, '#' 앞)
        var printed = FloorSign.Page("데모", fAt, fl, f => "http://192.168.0.10:8765/view?v=abc&f=" + f + "#x", null, new DateTime(2027, 1, 1, 8, 5, 0), 12);
        var url9 = FloorSign.WithPrint("http://h/view?v=abc&f=9#x", new DateTime(2027, 1, 1, 8, 5, 0), fl.First(f => f.Floor == 9).Zones.Select(z => z.Status));
        Check(printed.Contains("<div class=\"printed\">인쇄 2027. 1. 1. 08:05<small>인쇄 후 12시간 지나면 QR 화면으로 현재 상태를 확인하세요") && !noQr.Contains("class=\"printed\"")
              && System.Text.RegularExpressions.Regex.IsMatch(url9, @"^http://h/view\?v=abc&f=9&ps=202701010805&pz=\d{" + fl.First(f => f.Floor == 9).Zones.Count + "}#x$")
              && FloorSign.WithPrint("http://h/x", new DateTime(2027, 1, 1, 8, 5, 0), new[] { 5, 3 }) == "http://h/x?ps=202701010805&pz=53",
            "W10 층 표지 인쇄 시각·유효 시간·QR ps/pz: " + url9);
        int sections = System.Text.RegularExpressions.Regex.Matches(withQr, "<section>").Count;
        Check(sections == 4 && System.Text.RegularExpressions.Regex.Matches(withQr, "<svg ").Count == 4 && withQr.Contains($"마감 값 <b>{fSeal}</b>"), $"floor_sign 6 쪽 {sections} · QR · 마감 값");
        Check(noQr.Contains("현장 입력을 켜면 QR 이 나옵니다") && !noQr.Contains("<svg") && !noQr.Contains("마감 값"), "floor_sign 7 QR·마감 값 없을 때");
        Check(!new[] { "해체하세요", "해체할 것", "승인됨", "타설하세요" }.Any(w => withQr.Contains(w)), "floor_sign 8 지시 문구 없음");
        Check(System.Text.RegularExpressions.Regex.Matches(withQr, "해체 중: 가이드라인 4.2 체크리스트").Count == 4
              && Approval.RequestHtml(fRows, "데모", fAt, fd).Contains("<p class=\"note\">" + Approval.DuringNote + ". ※ 작성요령"), "floor_sign 8-1 해체 중 안내 (층마다·요청서 3쪽)");
        File.AppendAllText(Path.Combine(fd, PourGate.ChecksFile), "10F-A1,2026-12-31T08:00,아니오,박안전,\n", new UTF8Encoding(false));   // 감리 승인 기록 없이 동바리 없음
        var f10 = FloorSign.Floors(fd, fRows, fAt).First(f => f.Floor == 10);
        Check(f10.Status == "무단 해체 의심 — 확인 필요: 10F-A1" && f10.Kind == "suspect" && FloorSign.Floors(fd, fRows, fAt).First(f => f.Floor == 9).Status == f9.Status
              && FloorSign.Page("데모", fAt, new[] { f10 }, _ => null, null).Contains("<section><div class=\"alarm\">무단 해체 의심 — 확인 필요</div>") && !withQr.Contains("class=\"alarm\""),   // 60차: 진한 빨강 + 맨 위 굵은 줄 (존치 층엔 없음)
            "floor_sign 9 무단 해체 의심 빨강 상태: " + f10.Status);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "RevitOnSite_층표지_시험.html"), withQr, new UTF8Encoding(false));   // 눈으로 볼 견본
    }
    finally { Directory.Delete(fd, true); }
}
else Console.WriteLine("Python 없음 — 시연 폴더 사례 건너뜀");
Console.WriteLine(fails == failsBefore ? "층 표지 OK" : "층 표지 FAIL");

// ── 13. 판넬 조기 탈형 (evaluate.py early_form, test_gate.py 사례) — 판정표 열은 위 evaluate 대조들이 바이트로 확인
failsBefore = fails;
Console.WriteLine("\n판넬 조기 탈형");
Check(Evaluator.EarlyForm(15.0, null, 24) == "14 MPa 도달 — 판넬 조기 탈형 4조건 검토 대상 (15.0 ≥ 14 MPa, 추정만 — 공시체 없음)", "early_form 1 공시체 없음: " + Evaluator.EarlyForm(15.0, null, 24));
Check(Evaluator.EarlyForm(15.0, 13.0, 24) == "14 MPa 미달 — 판넬 존치 (13.0 < 14 MPa, 공시체·추정 중 낮은 값)", "early_form 2 공시체가 낮음: " + Evaluator.EarlyForm(15.0, 13.0, 24));
{
    var er = Evaluator.Evaluate(data, new DateTime(2026, 12, 31, 9, 0, 0)).ToDictionary(r => r.ZoneId);
    Check(er["9F-A1"].EarlyForm.StartsWith("14 MPa 도달 — 판넬 조기 탈형 4조건 검토 대상") && er["12F-A1"].EarlyForm.StartsWith("14 MPa ") && !er.Values.Any(r => r.EarlyForm.Contains("판넬 탈형 요청 가능")),
        "early_form 3 샘플 9F-A1 (요청 가능 문구는 staged 4조건만, 33차): " + er["9F-A1"].EarlyForm);
}
Console.WriteLine(fails == failsBefore ? "판넬 조기 탈형 OK" : "판넬 조기 탈형 FAIL");
// 판정 규칙 근거 한 줄 (evaluate.py RULE_BASIS, 총괄 P2) — 대시보드 판정 근거 창·휴대폰 구역 카드에 같은 글자
if (python is not null)
{
    var pyBasis = RunPy("-c", "import sys, evaluate as ev; sys.stdout.write(ev.RULE_BASIS)");
    Console.WriteLine($"판정 규칙 근거: Python 과 같음 {(pyBasis == Evaluator.RuleBasis ? "예" : "아니오")}");
    Check(pyBasis == Evaluator.RuleBasis, "RULE_BASIS Python 대조: " + pyBasis);
}

// 16) 설치 검측 상하 정렬·연직도 (install_rules.py align_check·plumb_check 자체 검사 사례, 총괄 P2) + Python 문구 대조
failsBefore = fails;
Console.WriteLine("\n상하 정렬·연직도");
{
    var grid = Enumerable.Range(0, 3).SelectMany(i => Enumerable.Range(0, 3).Select(j => (1200.0 * i, 1200.0 * j))).ToList();
    var shifted = grid.Select(q => (q.Item1 + 300, q.Item2)).ToList();
    var al = InstallRules.AlignCheck(new Dictionary<int, List<(double X, double Y)>> { [1] = grid, [2] = grid, [3] = shifted });
    Check(al.Select(a => (a.Upper, a.Lower, a.MaxMm, a.P95Mm, a.Result)).SequenceEqual(new[] { (2, 1, 0.0, 0.0, "필요"), (3, 2, 300.0, 300.0, "필요") }), "align 1 정렬 0 mm / 한 층 300 mm 밀림");
    Check(al[1].Reason == "3층 ↔ 2층: 바로 아래 동바리까지 수평 거리 최대 300 mm · 95% 300 mm (9본) — 편차만 표시, 허용값 입력 필요", "align 2 문구: " + al[1].Reason);
    var alTol = InstallRules.AlignCheck(new Dictionary<int, List<(double X, double Y)>> { [1] = grid, [2] = grid, [3] = shifted }, 50);
    Check(alTol.Select(a => a.Result).SequenceEqual(new[] { "OK", "NG" }), "align 3 허용값 [입력] 있을 때만 OK/NG");
    Check(InstallRules.AlignCheck(new Dictionary<int, List<(double X, double Y)>> { [1] = grid, [2] = new() }).Count == 0, "align 4 빈 층은 건너뜀");
    var ins = new List<Dictionary<string, string?>>
    {
        new() { ["zone_id"] = "9F-A1", ["item_code"] = "SHR-V", ["result"] = "NG", ["inspector"] = "박안전", ["date"] = "2026-11-23" },
        new() { ["zone_id"] = "9F-A1", ["item_code"] = "SHR-V", ["result"] = "ok", ["inspector"] = "박안전", ["date"] = "2026-11-24" },
        new() { ["zone_id"] = "9F-B1", ["item_code"] = "연직도", ["result"] = "", ["inspector"] = "박안전", ["date"] = "2026-11-24" },
    };
    Check(InstallRules.PlumbCheck(ins, "9F-A1") == new InstallCheck("연직도", "OK", "연직도 OK (2026-11-24 박안전)", "설치 검측 수동 항목"), "plumb 1 마지막 행 OK");
    Check(InstallRules.PlumbCheck(ins, "9F-B1").Result == "필요" && InstallRules.PlumbCheck(ins, "9F-C1").Result == "필요" && InstallRules.PlumbCheck(ins.Take(1), "9F-A1").Result == "NG", "plumb 2 빈칸·기록 없음 → 필요");
    if (python is not null)
    {
        // 어긋난 격자(층마다 다른 밀림·본수) — 95% 순위·반올림까지 같은지
        var pts = new Dictionary<int, List<(double X, double Y)>>
        {
            [-1] = grid, [1] = grid.Select(q => (q.Item1 + 12.5, q.Item2 - 7.25)).ToList(),
            [2] = grid.Take(7).Select((q, k) => (q.Item1 + 40 * k, q.Item2 + 3.5)).ToList(),
        };
        string PtArg(List<(double X, double Y)> l) => string.Join(";", l.Select(q => q.X.ToString("R", inv) + "," + q.Y.ToString("R", inv)));
        var pyA = RunPy(new[] { "-c", "import sys, install_rules as ir\n"
            + "pts = {int(a.split(':')[0]): [tuple(map(float, q.split(','))) for q in a.split(':')[1].split(';')] for a in sys.argv[1:]}\n"
            + "for t in (None, 60.0):\n    for r in ir.align_check(pts, t): print(r['upper'], r['lower'], r['n'], repr(r['max_mm']), repr(r['p95_mm']), r['result'], r['reason'], sep='|')",
            }.Concat(pts.Select(kv => kv.Key + ":" + PtArg(kv.Value))).ToArray()).TrimEnd('\n');
        var csA = string.Join("\n", new double?[] { null, 60.0 }.SelectMany(t => InstallRules.AlignCheck(pts, t))
            .Select(r => $"{r.Upper}|{r.Lower}|{r.N}|{RevitOnSite.Engine.Py.Repr(r.MaxMm)}|{RevitOnSite.Engine.Py.Repr(r.P95Mm)}|{r.Result}|{r.Reason}"));
        Console.WriteLine($"align_check: Python 과 같음 {(pyA == csA ? "예" : "아니오")}");
        if (pyA != csA) Console.WriteLine($"    Python: {pyA}\n    C#    : {csA}");
        Check(pyA == csA, "align_check Python 대조");
    }
}
Console.WriteLine(fails == failsBefore ? "상하 정렬·연직도 OK" : "상하 정렬·연직도 FAIL");

// 17) 오늘 볼 것 (alerts.py --self-test 와 같은 시나리오, D4): 시연 자료 + 13F 구역 → 타설 보류만 / 동바리 확인 3건·9F-A2 '아니오' → 무단 해체 의심 맨 위 /
//     12-20 대비 상태 바뀜 — 세 경우 모두 Python alerts() 와 줄 단위로 같아야 함
failsBefore = fails;
Console.WriteLine("\n오늘 볼 것 (알림)");
if (python is not null)
{
    var ad = Directory.CreateTempSubdirectory("RevitOnSite_alerts_").FullName;
    try
    {
        RunPy(Path.Combine(FindRoot(), "tools", "make_demo_data.py"), ad);
        var z12 = Evaluator.Read(ad, "zones.csv").Where(z => z["floor"] == "12").ToList();
        File.AppendAllText(Path.Combine(ad, "zones.csv"), string.Concat(z12.Select(z => $"13F-{z["zone_id"]!.Split('-', 2)[1]},13,{z["x0"]},{z["y0"]},{z["x1"]},{z["y1"]}\n")), new UTF8Encoding(false));
        var aAt = new DateTime(2026, 12, 31, 9, 0, 0);
        void Same(string label, DateTime? prev)
        {
            var cs = string.Join("\n", Alerts.Of(ad, aAt, prev).Select(a => $"{a.Kind}|{a.ZoneId}|{a.Text}"));
            var py = RunPy("-c", "import sys, alerts as al\nfrom datetime import datetime\n"
                + "p = datetime.fromisoformat(sys.argv[2]) if sys.argv[2] else None\n"
                + "for a in al.alerts(sys.argv[1], datetime(2026, 12, 31, 9), p): print(a['kind'], a['zone_id'], a['text'], sep='|')",
                ad, prev?.ToString("yyyy-MM-dd'T'HH:mm", inv) ?? "").TrimEnd('\n');
            Console.WriteLine($"alerts {label}: {cs.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length}건, Python 과 같음 {(py == cs ? "예" : "아니오")}");
            if (py != cs) Console.WriteLine($"    Python: {py.Replace("\n", " / ")}\n    C#    : {cs.Replace("\n", " / ")}");
            Check(py == cs, "alerts " + label + " Python 대조");
        }
        var k1 = Alerts.Of(ad, aAt);
        Check(k1.Count > 0 && k1.All(a => a.Kind == "타설 보류" ? a.ZoneId.StartsWith("13F-") : true) && k1.Any(a => a.Kind == "타설 보류") && !k1.Any(a => a.Kind == "무단 해체 의심"), "alerts 1 13F 타설 보류만 (무단 해체 없음)");
        Same("시연 자료 + 13F", null);
        File.WriteAllText(Path.Combine(ad, PourGate.ChecksFile), "zone_id,timestamp,present,actor,photo\n"
            + string.Concat(new[] { "12F-B1", "11F-B1", "10F-B1" }.Select(z => $"{z},2026-12-31T08:30,예,시험,\n")) + "9F-A2,2026-12-31T08:40,아니오,시험,\n", new UTF8Encoding(false));
        var k2 = Alerts.Of(ad, aAt);
        Check(k2[0].Kind == "무단 해체 의심" && k2[0].ZoneId == "9F-A2" && !k2.Any(a => a.Kind == "타설 보류" && a.ZoneId == "13F-B1"), "alerts 2 무단 해체 의심 맨 위·13F-B1 빠짐");
        Check(Alerts.Summary(k2).StartsWith("오늘 볼 것 " + k2.Count + " — 무단 해체 의심 1 · 타설 보류 "), "alerts 3 요약 줄: " + Alerts.Summary(k2));
        Same("동바리 확인 3건·9F-A2 아니오", null);
        var k3 = Alerts.Of(ad, aAt, new DateTime(2026, 12, 20, 9, 0, 0));
        Check(k3.Any(a => a.Kind == "상태 바뀜") && k3.Where(a => a.Kind == "상태 바뀜").All(a => a.Text.Contains(" → ")), "alerts 4 상태 바뀜");
        Same("12-20 대비", new DateTime(2026, 12, 20, 9, 0, 0));
        // 판정 이력 eval_log.csv (alerts.py log_eval·prev_eval): 12-20 → 12-31 → 12-31 순 기록 → 12-31 의 비교 기준 12-20. Python 이 같은 순서로 쓴 파일과 바이트 대조
        Check(Alerts.PrevEval(ad, aAt) is null, "eval_log 1 기록 없으면 비교 기준 없음");
        var logged = new DateTime(2026, 11, 5, 1, 0, 0);
        foreach (var d in new[] { new DateTime(2026, 12, 20, 9, 0, 0), aAt, aAt })
            Alerts.LogEval(ad, d, Alerts.EvalSummary(Evaluator.Evaluate(ad, d)), logged);
        Check(Alerts.PrevEval(ad, aAt) == new DateTime(2026, 12, 20, 9, 0, 0) && Alerts.PrevEval(ad, new DateTime(2026, 12, 20, 9, 0, 0)) == aAt, "eval_log 2 비교 기준");
        var pyLog = Directory.CreateTempSubdirectory("RevitOnSite_evallog_py_").FullName;
        try
        {
            RunPy("-c", "import sys, alerts as al, evaluate as ev\nfrom datetime import datetime\nsrc, dst = sys.argv[1:3]\n"
                + "for d in (datetime(2026, 12, 20, 9), datetime(2026, 12, 31, 9), datetime(2026, 12, 31, 9)): al.log_eval(dst, d, al.eval_summary(ev.evaluate(src, d)), now=datetime(2026, 11, 5, 1))",
                ad, pyLog);
            bool sl = File.ReadAllBytes(Path.Combine(pyLog, Alerts.EvalLog)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(ad, Alerts.EvalLog)));
            Console.WriteLine($"eval_log.csv: Python 과 바이트 동일 {(sl ? "예" : "아니오")}");
            Check(sl, "eval_log Python 바이트");
        }
        finally { Directory.Delete(pyLog, true); }
        var k4 = Alerts.Of(ad, aAt, Alerts.PrevEval(ad, aAt));
        Check(k4.Select(a => a.Text).SequenceEqual(k3.Select(a => a.Text)), "eval_log 3 기록에서 읽은 비교 기준 = 12-20 직접 준 것과 같음");
        // 입회 없음 (F5, quality.watch_missing): 열 없으면 안 셈·안내 한 줄 → 열을 넣고 9F-A1 만 '예·홍길동' → 나머지 타설 구역이 맨 아래 (Python 과 줄 단위)
        Check(!k3.Any(a => a.Kind == "입회 없음") && Quality.PourWatch(ad).SequenceEqual(new[] { Quality.WatchHint }), "alerts 5 입회 열 없음 → 안 셈·안내");
        var ps = File.ReadAllLines(Path.Combine(ad, "pours.csv"));
        File.WriteAllText(Path.Combine(ad, "pours.csv"), ps[0] + ",supervisor_present,watcher\n"
            + string.Concat(ps.Skip(1).Where(l => l.Length > 0).Select(l => l + (l.StartsWith("9F-A1,") ? ",예,홍길동\n" : ",,\n"))), new UTF8Encoding(false));
        var k5 = Alerts.Of(ad, aAt);
        var iw = k5.Where(a => a.Kind == "입회 없음").ToList();
        Check(iw.Count > 0 && k5.TakeLast(iw.Count).SequenceEqual(iw) && iw.All(a => a.ZoneId != "9F-A1") && Quality.PourWatch(ad).Count >= iw.Count, $"alerts 6 입회 없음 {iw.Count}건 맨 아래·9F-A1 빠짐");
        Same("입회 열 · 9F-A1 만 기록", null);
        // v6.2 W-A 감리 확인 못 넣음: C# 이 쓴 거절 줄 = Python log_supervise_reject 바이트, 오늘 볼 것 줄 Python 대조
        Approval.LogSuperviseReject(ad, "9F-A2", "이감리", "s-9", "2026-12-31T08:52:10", "9F-A2 판정이 '존치' —\n 해체 승인 요청 가능일 때만, \"감리 승인 기록\"");
        var rjPy = Directory.CreateTempSubdirectory("RevitOnSite_reject_py_").FullName;
        try
        {
            RunPy("-c", "import sys, approval as ap\nap.log_supervise_reject(sys.argv[1], '9F-A2', '이감리', 's-9', '2026-12-31T08:52:10', \"9F-A2 판정이 '존치' —\\n 해체 승인 요청 가능일 때만, \\\"감리 승인 기록\\\"\")", rjPy);
            Check(File.ReadAllBytes(Path.Combine(rjPy, Approval.SuperviseRejects)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(ad, Approval.SuperviseRejects))), "supervise_rejects.csv Python 바이트");
        }
        finally { Directory.Delete(rjPy, true); }
        var k6 = Alerts.Of(ad, aAt);
        Check(k6.Any(a => a.Kind == "감리 확인 못 넣음" && a.ZoneId == "9F-A2" && a.Text.Contains("이감리, 2026-12-31 08:52")), "alerts 7 감리 확인 못 넣음");
        Same("감리 확인 못 넣음", null);
        // v6.2 W9 순찰 확인: 위 동바리 확인 4줄 (08:30·08:40) — options.json patrol_hours 0.25 면 그 줄도 지남 → Python 대조
        Check(Alerts.PatrolMissing(ad, aAt).TryGetValue("9F-A1", out var pa1) && pa1 is null && !Alerts.PatrolMissing(ad, aAt).ContainsKey("10F-B1")
              && Alerts.PatrolFloors(ad, aAt).First().Text == "9F 순찰 확인 없음 5구역 (기준 24시간 [가정] · 확인 기록 없음) — 9F-A1, 9F-B1, 9F-B2, 9F-C1, 9F-C2 (순찰 때 휴대폰 [동바리 확인])",
            "alerts 8 순찰 확인 없음 층 단위 한 줄: " + Alerts.PatrolFloors(ad, aAt).First().Text);
        File.WriteAllText(Path.Combine(ad, Evaluator.OptionsFile), "{\"patrol_hours\": 0.25}", new UTF8Encoding(false));
        Check(Alerts.PatrolMissing(ad, aAt)["10F-B1"] == new DateTime(2026, 12, 31, 8, 30, 0) && Alerts.PatrolFloors(ad, aAt).Any(x => x.Text.StartsWith("10F 순찰 확인 없음 6구역 (기준 0.25시간 [가정] · 마지막 확인 0시간 전)")),
            "alerts 9 patrol_hours 0.25: " + string.Join(" / ", Alerts.PatrolFloors(ad, aAt).Select(x => x.Text[..Math.Min(40, x.Text.Length)])));
        Same("순찰 0.25 h", null);
        File.Delete(Path.Combine(ad, Evaluator.OptionsFile));
        var pyW = RunPy("-c", "import sys, quality as q\nprint(*q.pour_watch(sys.argv[1]), sep=chr(10))", ad).TrimEnd('\n').Replace("\r", "");
        Check(pyW == string.Join("\n", Quality.PourWatch(ad)), "pour_watch Python 대조");
    }
    finally { Directory.Delete(ad, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "오늘 볼 것 OK" : "오늘 볼 것 FAIL");

// v6.2 W7 워크셰어링 건너뛰기 (순수 함수 — 실기 소유 충돌은 같은 계정으로 재현 불가 [미확인])
{
    var ws = Worksharing.Split(new[]
    {
        new Worksharing.Item("1", "9F-A1", false, ""), new Worksharing.Item("2", "9F-A1", true, "박구조"), new Worksharing.Item("3", "9F-B1", true, "박구조"),
        new Worksharing.Item("4", "9F-C1", true, "  "), new Worksharing.Item("5", "9F-C2", false, "김현장"),
    });
    Check(ws.Write.SetEquals(new[] { "1", "5" }) && ws.Skipped.Count == 3
          && ws.Message == "다른 사용자가 가진 요소 3개는 매개변수를 쓰지 않았습니다 — 소유: 박구조 2개 (9F-A1, 9F-B1) · (이름 없음) 1개 (9F-C1). 판정 결론은 데이터 폴더 판정표·eval_log 에 그대로 있습니다 — 그 사용자가 동기화해 요소를 내놓은 뒤 다시 [판정]."
          && Worksharing.Split(new[] { new Worksharing.Item("1", "z", false, "") }).Message is null,
        "W7 워크셰어링 건너뛰기 목록·문장: " + ws.Message);
}

// v6.2 W11 범위 밖 알림: 샘플 + slab_mm(12F-C2 350)·shore_mm(9F-A1 5200, 나머지 3000) 을 FillSlab 로 더하고 B1 구역 → Python 판정표와 바이트 같음
failsBefore = fails;
Console.WriteLine("\n범위 밖 알림 (W11)");
if (python is not null)
{
    var sd = Directory.CreateTempSubdirectory("RevitOnSite_scope_").FullName;
    try
    {
        foreach (var f in Directory.GetFiles(data, "*.csv")) File.Copy(f, Path.Combine(sd, Path.GetFileName(f)));
        var zp = Path.Combine(sd, "zones.csv");
        var ids = Evaluator.Read(sd, "zones.csv").Select(r => r["zone_id"]!).ToList();
        var (sAdd, _) = ZonesCsv.FillSlab(zp, ids.ToDictionary(z => z, z => z == "12F-C2" ? "350" : ""));
        var (hAdd, hN) = ZonesCsv.FillSlab(zp, ids.ToDictionary(z => z, z => z == "9F-A1" ? "5200" : "3000"), "shore_mm");
        File.AppendAllText(zp, "B1-A1,-1,0.0,0.0,10.0,9.0,,\n", new UTF8Encoding(false));
        File.AppendAllText(Path.Combine(sd, "pours.csv"), "B1-A1,2026-11-12,08:00,보통,24\n", new UTF8Encoding(false));
        var sAt = new DateTime(2026, 12, 31, 9, 0, 0);
        var sPy = Path.Combine(sd, "_py.csv");
        RunPy("evaluate.py", sd, "--at", "2026-12-31T09:00", "--out", sPy);
        var sCs = Path.Combine(sd, "_cs.csv");
        var srows = Evaluator.Evaluate(sd, sAt);
        Evaluator.Write(srows, sCs);
        bool same = File.ReadAllBytes(sPy).AsSpan().SequenceEqual(File.ReadAllBytes(sCs));
        Console.WriteLine($"범위 밖 판정표 Python 과 바이트 동일: {(same ? "예" : "아니오")}");
        Check(same && sAdd && hAdd && hN == ids.Count, "scope 1 판정표 Python 바이트 · slab_mm·shore_mm 열 더함");
        var sz = srows.ToDictionary(r => r.ZoneId);
        Check(Evaluator.ScopeNote(sz["9F-A1"].Reason) == "⚠ 범위 밖 가능 (동바리 높이 5.20 m ≥ 5 m (건설기술진흥법 시행령 제101조의2)) — " + Evaluator.ScopeTail && sz["9F-A1"].Decision == "해체 승인 요청 가능"
              && sz["12F-C2"].Reason.Contains("(슬래브 350 mm > 300 mm [가정])") && sz["B1-A1"].Reason.Contains("(지하층)") && Evaluator.ScopeNote(sz["9F-B1"].Reason) == "",
            "scope 2 태그 3종 (동바리 높이·슬래브·지하층), 결론 그대로");
        // W12 시공하중 3.5 (options.json): 판정표 Python 바이트 같음, 검토서 ③ 식·꼬리표, 1.0 은 2.5 로
        File.WriteAllText(Path.Combine(sd, Evaluator.OptionsFile), "{\"live_load_kn_m2\": 3.5}", new UTF8Encoding(false));
        RunPy("evaluate.py", sd, "--at", "2026-12-31T09:00", "--out", sPy);
        Evaluator.Write(Evaluator.Evaluate(sd, sAt), sCs);
        var rv = Staged.ReviewHtml(sd, sAt, "9F-A1");
        Check(File.ReadAllBytes(sPy).AsSpan().SequenceEqual(File.ReadAllBytes(sCs)) && rv.Contains("+ 1.6·3.5 =") && rv.Contains("시공하중 3.5 kN/m² [가정] — 자재 적재·장비 집중하중 미반영."),
            "scope 3 시공하중 3.5 판정표 Python 바이트 · 검토서 식·꼬리표");
        File.WriteAllText(Path.Combine(sd, Evaluator.OptionsFile), "{\"live_load_kn_m2\": 1.0}", new UTF8Encoding(false));
        Check(Evaluator.LiveLoad(sd) == 2.5 && Evaluator.LiveNote(sd) == "시공하중 2.5 kN/m² [가정] — 자재 적재·장비 집중하중 미반영", "scope 4 시공하중 2.5 미만 → 2.5");
    }
    finally { Directory.Delete(sd, true); }
}
Console.WriteLine(fails == failsBefore ? "범위 밖 OK" : "범위 밖 FAIL");

// 18) zones.csv slab_mm 채우기 (Engine.ZonesCsv.FillSlab, 총괄 11-05 (b)): 열 없는 파일 → 맨 뒤에 열 추가·다른 칸 바이트 그대로(BOM·CRLF 포함) / 열 있는 파일 → 값 있는 칸 보존, 빈칸만
failsBefore = fails;
Console.WriteLine("\nzones.csv slab_mm 채우기");
{
    var zf = Directory.CreateTempSubdirectory("RevitOnSite_slab_").FullName;
    try
    {
        var zp = Path.Combine(zf, "zones.csv");
        string a = "zone_id,floor,x0,y0,x1,y1\r\n9F-A1,9,0.0,0.0,10.0,6.0\r\n9F-A2,9,10.0,0.0,20.0,6.0\r\n9F-B1,9,20.0,0.0,30.0\r\n";   // 마지막 줄은 짧은 줄
        File.WriteAllText(zp, a, new UTF8Encoding(true));
        var r1 = ZonesCsv.FillSlab(zp, new Dictionary<string, string> { ["9F-A1"] = "210", ["9F-A2"] = "", ["9F-B1"] = "250" });
        var b1 = File.ReadAllBytes(zp);
        string t1 = new UTF8Encoding(false).GetString(b1, 3, b1.Length - 3);
        var oldLines = a.Split("\r\n"); var newLines = t1.Split("\r\n");
        Check(r1 == (true, 2) && b1[0] == 0xEF && t1 == "zone_id,floor,x0,y0,x1,y1,slab_mm\r\n9F-A1,9,0.0,0.0,10.0,6.0,210\r\n9F-A2,9,10.0,0.0,20.0,6.0,\r\n9F-B1,9,20.0,0.0,30.0,,250\r\n"
              && oldLines.Zip(newLines).All(q => q.Second.StartsWith(q.First)), "slab 1 열 추가·다른 칸 그대로: " + t1.Replace("\r\n", " / "));
        var rows1 = Evaluator.Read(zf, "zones.csv");
        Check(rows1.Select(r => r.GetValueOrDefault("slab_mm")).SequenceEqual(new[] { "210", "", "250" }) && rows1[2].GetValueOrDefault("y1") == "", "slab 2 다시 읽기 (짧은 줄은 y1 빈칸, slab_mm 제자리)");
        string bText = "zone_id,floor,x0,y0,x1,y1,slab_mm\n9F-A1,9,0.0,0.0,10.0,6.0,250\n9F-A2,9,10.0,0.0,20.0,6.0,\n";
        File.WriteAllText(zp, bText, new UTF8Encoding(false));
        var r2 = ZonesCsv.FillSlab(zp, new Dictionary<string, string> { ["9F-A1"] = "210", ["9F-A2"] = "300" });
        Check(r2 == (false, 1) && File.ReadAllText(zp) == "zone_id,floor,x0,y0,x1,y1,slab_mm\n9F-A1,9,0.0,0.0,10.0,6.0,250\n9F-A2,9,10.0,0.0,20.0,6.0,300\n", "slab 3 값 보존·빈칸만: " + File.ReadAllText(zp));
        var before = File.ReadAllBytes(zp);
        Check(ZonesCsv.FillSlab(zp, new Dictionary<string, string> { ["9F-A1"] = "999" }) == (false, 0) && File.ReadAllBytes(zp).AsSpan().SequenceEqual(before), "slab 4 바꿀 것 없으면 파일 그대로");
    }
    finally { Directory.Delete(zf, true); }
}
Console.WriteLine(fails == failsBefore ? "zones.csv slab_mm 채우기 OK" : "zones.csv slab_mm 채우기 FAIL");

// 19) 기상 자료 가져오기 (weather_import.py 자체 검사 사례, F8): CP949 일자료·UTF-8 시간자료 읽기, weather.csv 합치기 바이트, WeatherLabel, 출처가 붙은 판정 근거 (Python 판정표 바이트)
failsBefore = fails;
Console.WriteLine("\n기상 자료 가져오기");
{
    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    var wd = Directory.CreateTempSubdirectory("RevitOnSite_weather_").FullName;
    try
    {
        var day = Path.Combine(wd, "asos_day.csv");
        File.WriteAllText(day, "지점,지점명,일시,평균기온(°C),최저기온(°C),최고기온(°C)\n108,서울,2026-12-01,-1.5,-5.0,3.2\n108,서울,2026-12-02,,-6.0,1.0\n108,서울,2026-12-03,2.0,-1.0,5.0\n", System.Text.Encoding.GetEncoding(949));
        var (temps, src) = WeatherImport.ReadAsos(day);
        Check(temps.Count == 2 && temps["2026-12-01"] == -1.5 && temps["2026-12-03"] == 2.0 && src == "기상청 ASOS 108 서울 [입력]", "weather 1 일자료 CP949: " + src);
        var hour = Path.Combine(wd, "asos_hour.csv");
        File.WriteAllText(hour, "지점,지점명,일시,기온(°C),강수량(mm)\n108,서울,2026-12-04 01:00,1.0,\n108,서울,2026-12-04 01:00,3.0,\n108,서울,2026-12-05 01:00,-2.0,\n", new UTF8Encoding(false));
        var h = WeatherImport.ReadAsos(hour).Temps;
        Check(h.Count == 2 && h["2026-12-04"] == 2.0 && h["2026-12-05"] == -2.0, "weather 2 시간자료 → 날짜 평균");
        var bad = Path.Combine(wd, "bad.csv");
        File.WriteAllText(bad, "a,b\n1,2\n", new UTF8Encoding(false));
        string badMsg = "";
        try { WeatherImport.ReadAsos(bad); } catch (InvalidDataException x) { badMsg = x.Message; }
        Check(badMsg.Contains("열을 찾지 못함"), "weather 3 열 없는 파일: " + badMsg);
        foreach (var sub in new[] { "cs", "py" })
        {
            Directory.CreateDirectory(Path.Combine(wd, sub));
            File.WriteAllText(Path.Combine(wd, sub, "weather.csv"), "date,avgTa\n2026-11-30,4.0\n2026-12-01,0.0\n", new UTF8Encoding(false));
        }
        var nc = WeatherImport.Merge(Path.Combine(wd, "cs"), temps, src);
        Check(nc == (1, 1), $"weather 4 합치기 새 1·바뀜 1 ({nc})");
        var wrows = Evaluator.Read(Path.Combine(wd, "cs"), "weather.csv");
        Check(Evaluator.WeatherLabel(wrows) == "기상 기록" && Evaluator.WeatherLabel(new[] { new Dictionary<string, string?> { ["source"] = src }, new Dictionary<string, string?> { ["source"] = src } }) == "기상청 ASOS 108 서울"
              && Evaluator.WeatherLabel(new[] { new Dictionary<string, string?> { ["date"] = "x", ["avgTa"] = "1" } }) == "기상 기록", "weather 5 WeatherLabel");
        if (python is not null)
        {
            var pyOut = RunPy("-c", "import sys, weather_import as wi\nt, s = wi.read_asos(sys.argv[1])\nprint(wi.merge(sys.argv[2], t, s), s)", day, Path.Combine(wd, "py")).TrimEnd('\n');
            bool same = File.ReadAllBytes(Path.Combine(wd, "py", "weather.csv")).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(wd, "cs", "weather.csv")));
            Console.WriteLine($"weather.csv 합치기: Python 과 바이트 동일 {(same ? "예" : "아니오")} ({pyOut})");
            Check(same && pyOut == $"(1, 1) {src}", "weather Python 대조");
            // 출처가 한 값인 weather.csv → 센서 없는 구역 근거 "기상청 ASOS 108 서울 [입력]" — Python 판정표와 바이트
            var qW = Scenario(null, null, null, "zones.csv", "pours.csv");
            File.WriteAllText(Path.Combine(qW, "weather.csv"), "date,avgTa,source\n" + string.Concat(Enumerable.Range(0, 40).Select(i => $"{new DateOnly(2026, 11, 26).AddDays(i):yyyy-MM-dd},1.0,{src}\n")), new UTF8Encoding(false));
            var r10 = Evaluator.Evaluate(qW, qAt).First(r => r.ZoneId == "10F-A1");
            Check(r10.EstSource.StartsWith("기상 예측(센서 없음) — 기상청 ASOS 108 서울 [입력]"), "weather 6 근거 출처: " + r10.EstSource);
            Gate("F8-ASOS", qW, qAt);
        }
    }
    finally { Directory.Delete(wd, true); }
}
Console.WriteLine(fails == failsBefore ? "기상 자료 가져오기 OK" : "기상 자료 가져오기 FAIL");

// 20) 해체 후 점검 (after_check.py 자체 검사 자료, F3) + 설치 서류 (install_rules.install_docs_check, F4) — Python 과 줄 단위 대조
failsBefore = fails;
Console.WriteLine("\n해체 후 점검·설치 서류");
{
    var ad = Directory.CreateTempSubdirectory("RevitOnSite_after_").FullName;
    try
    {
        File.WriteAllText(Path.Combine(ad, "evidence.csv"), "ev_id,zone_id,date,stage,doc_no,result,by,x,y,file,sha256\n"
            + "E0001,9F-A1,2027-01-05,8,R-1,,박안전,,,,\nE0002,9F-B1,2027-01-05,8,R-2,,박안전,,,,\nE0003,9F-C1,2027-01-05,8,R-3,,박안전,,,,\nE0004,9F-D1,2027-01-05,7,,,박안전,,,,\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(ad, AfterCheck.File), "zone_id,date,item_code,result,by,photo\n"
            + string.Concat(Enumerable.Range(1, 5).Select(i => $"9F-A1,2027-01-05,AC-{i},OK,박안전,\n"))
            + string.Concat(Enumerable.Range(1, 5).Select(i => $"9F-B1,2027-01-05,ac-{i},{(i == 4 ? "NG" : "ok")},박안전,\n"))
            + "9F-A1,2027-01-09,AC-3,NG,박안전,\n9F-C1,2027-01-05,AC-1,,박안전,\n9F-B1,2027-01-05,AC-4,ok,박안전,\n", new UTF8Encoding(false));
        string CsAfter(string d) => string.Join("\n", AfterCheck.Status(ad, d).Select(kv => $"{kv.Key}|{kv.Value.Date}|{string.Join(",", kv.Value.Items.Select(i => i.Code + "=" + i.Result))}|{string.Join(",", kv.Value.Missing)}|{kv.Value.Ok}")
            .Concat(AfterCheck.Warnings(ad, d).Select(kv => kv.Key + "=" + kv.Value)));
        var w6 = AfterCheck.Warnings(ad, "2027-01-06");
        Check(w6.Select(kv => kv.Key).SequenceEqual(new[] { "9F-C1", "9F-D1" }) && AfterCheck.Status(ad, "2027-01-06")["9F-B1"].Ok && AfterCheck.Warnings(ad, "2027-01-04").Count == 0,
            "after 1 같은 날 뒤 행이 이김(9F-B1 AC-4 ok)·원장 7 해체 완료 9F-D1 도 경고(23차 E13)·결과보고 전 경고 없음");
        if (python is not null)
        {
            var pyX = RunPy("-c", "import sys, after_check as ac\nfor d in sys.argv[2:]:\n"
                + "    for z, s in ac.after_status(sys.argv[1], d).items(): print(z, s['date'], ','.join(c + '=' + v for c, v in s['items'].items()), ','.join(s['missing']), s['ok'], sep='|')\n"
                + "    for z, w in ac.warnings(sys.argv[1], d).items(): print(z + '=' + w)", ad, "2027-01-04", "2027-01-06", "2027-01-09").TrimEnd('\n').Replace("\r", "");
            var csX = string.Join("\n", new[] { "2027-01-04", "2027-01-06", "2027-01-09" }.Select(CsAfter).Where(x => x.Length > 0));
            Console.WriteLine($"after_check: Python 과 같음 {(pyX == csX ? "예" : "아니오")}");
            if (pyX != csX) Console.WriteLine($"    Python: {pyX}\n    C#    : {csX}");
            Check(pyX == csX, "after_check Python 대조");
        }
    }
    finally { Directory.Delete(ad, true); }
    var full = new Dictionary<string, string?> { ["assembly_dwg_no"] = "A-12", ["struct_review_no"] = "SR-3", ["support_method"] = "파이프 서포트" };
    var docCases = new (Dictionary<string, string?> Row, double? H)[]
    {
        (full, 2790), (new(), 2790), (full, 5000), (new(full) { ["expert_confirm"] = "아니오" }, 5900), (new(full) { ["expert_confirm"] = " 예 " }, 5900),
        (new(full) { ["height_mm"] = "5200" }, null), (new(full) { ["height_mm"] = "4999.6" }, null), (full, null), (new() { ["assembly_dwg_no"] = "  " }, 5000.4),
    };
    string Docs(IEnumerable<InstallCheck> l) => string.Join("\n", l.Select(r => $"{r.Item}|{r.Result}|{r.Reason}|{r.Source}"));
    Check(InstallRules.InstallDocsCheck(full, 2790).All(r => r.Result == "OK") && InstallRules.InstallDocsCheck(full, 2790).Count == 3
          && InstallRules.InstallDocsCheck(full, 5900).First(r => r.Item == "관계전문가 확인").Reason == "높이 5900 mm ≥ 5000 mm — 관계전문가 확인 기록 없음", "docs 1 자체 검사 사례");
    if (python is not null)
    {
        var pyX = RunPy(new[] { "-c", "import sys, json, install_rules as ir\nfor a in sys.argv[1:]:\n    row, h = json.loads(a)\n"
            + "    for r in ir.install_docs_check(row, h): print(r['item'], r['result'], r['reason'], r['source'], sep='|')" }
            .Concat(docCases.Select(c => System.Text.Json.JsonSerializer.Serialize(new object?[] { c.Row, c.H }))).ToArray()).TrimEnd('\n').Replace("\r", "");
        var csX = string.Join("\n", docCases.Select(c => Docs(InstallRules.InstallDocsCheck(c.Row, c.H))));
        Console.WriteLine($"install_docs_check: Python 과 같음 {(pyX == csX ? "예" : "아니오")} ({docCases.Length}가지)");
        if (pyX != csX) Console.WriteLine($"    Python: {pyX}\n    C#    : {csX}");
        Check(pyX == csX, "install_docs_check Python 대조");
    }
}
Console.WriteLine(fails == failsBefore ? "해체 후 점검·설치 서류 OK" : "해체 후 점검·설치 서류 FAIL");

// 21) 슬래브 이력서 (slab_record.py, F10): 시연 자료 → ① 그대로 ② 요청 제출 1건·판정 이력 2줄·채취 정보 열(쉼표·따옴표) 뒤 — HTML·CSV 를 Python 과 바이트 대조
failsBefore = fails;
Console.WriteLine("\n슬래브 이력서");
if (python is not null)
{
    var sd = Directory.CreateTempSubdirectory("RevitOnSite_slab_").FullName;
    try
    {
        var dm = Path.Combine(sd, "data");
        Directory.CreateDirectory(dm);
        RunPy(Path.Combine(FindRoot(), "tools", "make_demo_data.py"), dm);
        var sAt = new DateTime(2026, 12, 31, 9, 0, 0);
        void SameSlab(string label)
        {
            var po = Path.Combine(sd, "py");
            RunPy("-c", "import sys, slab_record as sr\nfrom datetime import datetime\nf, out = sys.argv[1:3]\nat = datetime(2026, 12, 31, 9)\nrs = sr.records(f, at)\n"
                + "open(out + '.html', 'w', encoding='utf-8', newline='').write(sr.page(rs, '시연', at))\nsr.write_csv(rs, out + '.csv')", dm, po);
            var rs = SlabRecord.Records(dm, sAt);
            var pyH = File.ReadAllBytes(po + ".html");
            var csH = new UTF8Encoding(false).GetBytes(SlabRecord.Page(rs, "시연", sAt));
            SlabRecord.WriteCsv(rs, Path.Combine(sd, "cs.csv"));
            bool h = pyH.AsSpan().SequenceEqual(csH), c = File.ReadAllBytes(po + ".csv").AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(sd, "cs.csv")));
            Console.WriteLine($"slab_record {label}: {rs.Count}구역 HTML {(h ? "같음" : "다름")} · CSV {(c ? "같음" : "다름")}");
            if (!h)
            {
                string a = Encoding.UTF8.GetString(pyH), b2 = Encoding.UTF8.GetString(csH);
                int i = 0; while (i < a.Length && i < b2.Length && a[i] == b2[i]) i++;
                Console.WriteLine($"    {i}번째 글자부터 다름\n    Python: {a.Substring(Math.Max(0, i - 60), Math.Min(160, a.Length - Math.Max(0, i - 60)))}\n    C#    : {b2.Substring(Math.Max(0, i - 60), Math.Min(160, b2.Length - Math.Max(0, i - 60)))}");
            }
            if (!c) Console.WriteLine($"    Python: {File.ReadAllText(po + ".csv")}\n    C#    : {File.ReadAllText(Path.Combine(sd, "cs.csv"))}");
            Check(h && c, "slab_record " + label + " Python 바이트 대조");
        }
        SameSlab("시연 자료");
        RunPy("-c", "import sys, os, csv, approval as ap, evaluate as ev, alerts as al\nfrom datetime import datetime\nf = sys.argv[1]\nat = datetime(2026, 12, 31, 9)\n"
            + "ap.record(f, '9F-B1', '요청 제출', '시험', next(r for r in ev.evaluate(f, at) if r['zone_id'] == '9F-B1'), now=at)\n"
            + "for d in (datetime(2026, 12, 30, 9), at): al.log_eval(f, d, al.eval_summary(ev.evaluate(f, d)), now=datetime(2026, 11, 5, 1))\n"
            + "cs = ev.read(f, 'cylinders.csv')\nh = open(os.path.join(f, 'cylinders.csv'), 'w', encoding='utf-8', newline='')\n"
            + "w = csv.DictWriter(h, fieldnames=[*cs[0], 'pour_id', 'sampled_at', 'supplier', 'truck_no', 'storage_zone'])\nw.writeheader()\n"
            + "open(os.path.join(f, 'install_inspection.csv'), 'a', encoding='utf-8').write('9F-B1,SHR-V,NG,시험,2026-12-01,' + chr(10))\n"
            + "w.writerows({**r, **({'pour_id': 'P-1', 'sampled_at': '2026-11-26T08:40', 'supplier': '한빛 \"레미콘\" & <주>', 'truck_no': '12가3456', 'storage_zone': '9F-B2, 수조'} if i == 0 else {})} for i, r in enumerate(cs))\nh.close()", dm);
        SameSlab("요청 제출·판정 이력·채취 정보 뒤");
        var b1 = SlabRecord.Records(dm, sAt, "9F-B1")[0];
        Check(((List<object?>)b1["approvals"]!).Count == 1 && ((string)b1["sha"]!).Length == 12 && SlabRecord.Page(new[] { b1 }, "시연", sAt).Contains("품질 기록이며 해체·사용 승인이 아닙니다")
              && ((List<object?>)SlabRecord.Records(dm, new DateTime(2026, 12, 30, 9, 0, 0), "9F-B1")[0]["approvals"]!).Count == 0,
            "slab_record 1 요청 제출 1건·지문 12자·승인 아님 문구·기준일 뒤 기록 안 보임");
        string miss = "";
        try { SlabRecord.Records(dm, sAt, "9F-Z9"); } catch (InvalidDataException x) { miss = x.Message; }
        Check(miss == "9F-Z9: pours.csv 에 없음", "slab_record 2 없는 구역: " + miss);
    }
    finally { Directory.Delete(sd, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "슬래브 이력서 OK" : "슬래브 이력서 FAIL");

// 22) 28일 품질 대장 (quality.py quality_ledger·quality_html, F7): 자체 검사 자료 + 동률(24.25)·같은 시험일 파일 순서·더하기 순서 사례·시연 자료 → 줄·HTML 바이트를 Python 과 대조
failsBefore = fails;
Console.WriteLine("\n28일 품질 대장");
if (python is not null)
{
    var qd = Directory.CreateTempSubdirectory("RevitOnSite_quality_").FullName;
    try
    {
        var fx = Path.Combine(qd, "fx");
        Directory.CreateDirectory(fx);
        File.WriteAllText(Path.Combine(fx, "pours.csv"), "zone_id,pour_date,pour_time,mix,fck,supervisor_present,watcher\n"
            + "P,2026-10-01,08:00,보통,24,예,홍길동\nF,2026-10-01,08:00,보통,24,아니오,\nN,2026-10-01,08:00,보통,24,,\nC,2026-10-01,08:00,보통,24,예,김감시\n"
            + "H,2026-10-01,08:00,보통,40,예,이감시\nT,2026-10-02,08:00,보통,24.5,예,박\nS,2026-10-02,08:00,보통,21,예,최\nU,2026-10-02,08:00,보통,0.15,예,정\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(fx, "cylinders.csv"), "zone_id,test_date,curing,f1,f2,f3\n"
            + string.Concat(new[] { "25", "26", "24.5" }.Select(a => $"P,2026-10-29,표준양생,{a},{a},{a}\n"))
            + "F,2026-10-29,표준양생,26,26,26\nF,2026-10-29,표준양생,20,20,20\nF,2026-10-29,표준양생,26,26,26\n"
            + "N,2026-10-29,현장봉함,30,30,30\nN,2026-10-15,표준양생,30,30,30\nC,2026-10-29,표준양생,30,30,30\nH,2026-10-29,표준양생,45,45,45\n"
            + string.Concat(Enumerable.Repeat("T,2026-10-30,표준양생,24.25,24.25,24.25\n", 3))
            + "S,2026-10-30,표준양생,24.1,24.2,24.4\nS,2026-10-30,표준양생,0.1,0.2,60.3\nS,2026-10-30,표준양생,21.05,21.15,21.25\nS,2026-10-30,표준양생,17.4,17.6,17.45\n"
            + string.Concat(Enumerable.Repeat("U,2026-10-30,표준양생,0.1,0.2,0.3\n", 3)), new UTF8Encoding(false));   // U: Python 3.12 sum() 보정 합산이라야 같은 값
        var dm = Path.Combine(qd, "demo");
        Directory.CreateDirectory(dm);
        RunPy(Path.Combine(FindRoot(), "tools", "make_demo_data.py"), dm);
        foreach (var (label, dir) in new[] { ("자체 검사 자료+동률·순서", fx), ("시연 자료", dm) })
        {
            var pyQ = RunPy("-c", "import sys, quality as q\nrs = q.quality_ledger(sys.argv[1])\nopen(sys.argv[2], 'w', encoding='utf-8', newline='').write(q.quality_html(rs))\n"
                + "for r in rs: print(r['zone_id'], r['pour_date'], r['n_specimens'], repr(r['mean']), repr(r['min']), r['result'], r['basis'], sep='|')", dir, Path.Combine(qd, "py.html")).TrimEnd('\n');
            var rows = Quality.Ledger(dir);
            var csQ = string.Join("\n", rows.Select(r => $"{r.ZoneId}|{r.PourDate}|{r.NSpecimens}|{(r.Mean is { } m ? RevitOnSite.Engine.Py.Repr(m) : "None")}|{(r.Min is { } n ? RevitOnSite.Engine.Py.Repr(n) : "None")}|{r.Result}|{r.Basis}"));
            bool h = File.ReadAllBytes(Path.Combine(qd, "py.html")).AsSpan().SequenceEqual(new UTF8Encoding(false).GetBytes(Quality.Html(rows)));
            Console.WriteLine($"quality {label}: {rows.Count}구역 줄 {(pyQ == csQ ? "같음" : "다름")} · HTML {(h ? "같음" : "다름")} — {string.Join(" ", rows.GroupBy(r => r.Result).Select(g => g.Key + " " + g.Count()))}");
            if (pyQ != csQ) Console.WriteLine($"    Python: {pyQ.Replace("\n", " / ")}\n    C#    : {csQ.Replace("\n", " / ")}");
            Check(pyQ == csQ && h, "quality " + label + " Python 대조");
        }
        var got = Quality.Ledger(fx).ToDictionary(r => r.ZoneId);
        Check(got["P"].Result == "합격" && got["P"].NSpecimens == 9 && got["F"].Result == "불합격" && got["F"].Min == 20 && got["N"].Result == "시험 없음" && got["N"].Mean is null
              && got["C"].Result == "확인 필요" && got["H"].Result == "확인 필요" && Quality.Html(got.Values).Contains("<caption>참고 — 기준 적용 [부분 확인], 품질관리자 확인용</caption>"),
            "quality 1 자체 검사 사례 (합격·불합격·시험 없음·확인 필요·표 머리)");
        Check(got["U"].Mean != (0.1 + 0.2 + 0.3) / 3, "quality 2 보정 합산 — 단순 더하기 값과 다르고 위 줄이 Python 과 같음 (U 구역)");
        // 오류 점검 ②: Excel 이 연 CSV(쓰기 열림·읽기만 공유)도 읽힘 / 빈 fck → 한국어 입력 오류 문구
        using (new FileStream(Path.Combine(fx, "pours.csv"), FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            Check(Quality.Ledger(fx).Count == got.Count && Evaluator.Read(fx, "cylinders.csv").Count > 0, "quality 3 Excel 처럼 열린 pours.csv 도 읽음 (공유 읽기)");
        File.AppendAllText(Path.Combine(fx, "pours.csv"), "V,2026-10-02,08:00,보통,,예,정\n", new UTF8Encoding(false));
        string qErr = "";
        try { Quality.Ledger(fx); } catch (InvalidDataException x) { qErr = x.Message; }
        Check(qErr.StartsWith("입력 CSV 값을 읽을 수 없음 (FormatException"), "quality 4 빈 fck → 한국어 입력 오류: " + qErr);
    }
    finally { Directory.Delete(qd, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "28일 품질 대장 OK" : "28일 품질 대장 FAIL");

// 23) IFC 근거 속성 (tools/ifc_add_evidence.py): 자체 검사 IFC(CRLF) + LF·OwnerHistory 없음·한글·이모지·\·'·줄바꿈 경고 → 고정 시드 GlobalId 로 Python 결과 IFC 와 파일 전체 바이트 대조
failsBefore = fails;
Console.WriteLine("\nIFC 근거 속성");
{
    Check(IfcEvidence.IfcGuid(0) == new string('0', 22) && IfcEvidence.IfcGuid(UInt128.MaxValue) == "3" + new string('$', 21), "ifc 1 GlobalId 끝값 (0 → 0×22, 최대 → 3$×21)");
    var g = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    Check(IfcEvidence.Decode(IfcEvidence.Encode("a'b 한글 😀")) == "a'b 한글 😀" && IfcEvidence.Encode("한\n") == @"\X2\D55C000A\X0\" && IfcEvidence.Encode("😀") == @"\X4\0001F600\X0\",
        "ifc 2 STEP 문자열 인코딩 (\\X2\\ 대문자·BMP 밖 \\X4\\·줄바꿈도 \\X2\\)");
    var idir = Directory.CreateTempSubdirectory("RevitOnSite_ifcev_").FullName;
    try
    {
        string body = "ISO-10303-21;\nHEADER;\nFILE_DESCRIPTION(('ViewDefinition [ReferenceView_V1.2]'),'2;1');\nFILE_NAME('t.ifc','2099-12-31T00:00:00',(''),(''),'','','');\nFILE_SCHEMA(('IFC4'));\nENDSEC;\n\nDATA;\n"
            + "#5=IFCPERSONANDORGANIZATION($,$,$);\n#6=IFCOWNERHISTORY(#5,$,$,.NOCHANGE.,$,$,$,0);\n"
            + "#20=IFCBUILDINGELEMENTPROXY('1aaaaaaaaaaaaaaaaaaaaa',#6,'Z1',$,$,$,$,$,$);\n#21=IFCBUILDINGELEMENTPROXY('1aaaaaaaaaaaaaaaaaaaab',#6,'Z2',$,$,$,$,$,$);\n"
            + "#22=IFCMEMBER('1aaaaaaaaaaaaaaaaaaaac',#6,'S1',$,$,$,$,$,$);\n"
            + "#30=IFCPROPERTYSINGLEVALUE('ZoneID',$,IFCTEXT('9F-A1'),$);\n#31=IFCPROPERTYSINGLEVALUE('ZoneID',$,IFCTEXT('9F-A2'),$);\n"
            + "#32=IFCPROPERTYSET('2aaaaaaaaaaaaaaaaaaaaa',#6,'Pset_RevitOnSite_Zone','',(#30));\n#33=IFCPROPERTYSET('2aaaaaaaaaaaaaaaaaaaab',#6,'Pset_RevitOnSite_Zone','',(#31));\n"
            + "#34=IFCRELDEFINESBYPROPERTIES('3aaaaaaaaaaaaaaaaaaaaa',#6,$,$,(#20),#32);\n#35=IFCRELDEFINESBYPROPERTIES('3aaaaaaaaaaaaaaaaaaaab',#6,$,$,(#21,#22),#33);\n"
            + "#36=IFCRELDEFINESBYPROPERTIES('3aaaaaaaaaaaaaaaaaaaac',#6,$,$,(#22,#20),#32);\n"
            + "ENDSEC;\n\nEND-ISO-10303-21;\n";
        string warn = "승인 기록 없이 동바리 없음 관측 — 9F-A2 'O'Neil' 확인 \\ 😀 줄\n바꿈";
        string csvText = "zone_id,decision,evidence_stage,seal,eval_at,warnings\r\n9F-A1,해체 승인 요청 가능,3/8,880afa40,2099-12-31T09:00,\r\n"
            + $"9F-A2,존치,3/8,,2099-12-31T09:00,\"{warn}\"\r\n9F-ZZ,존치,1/8,,,\r\n ,존치,,,,\r\n";
        foreach (var (label, text) in new[] { ("CRLF·OwnerHistory", body.Replace("\n", "\r\n")), ("LF·OwnerHistory 없음", body.Replace("#6=IFCOWNERHISTORY(#5,$,$,.NOCHANGE.,$,$,$,0);\n", "")) })
        {
            var ifc = Path.Combine(idir, "RevitOnSite_20991231.ifc");
            File.WriteAllBytes(ifc, Encoding.ASCII.GetBytes(text));
            File.WriteAllText(Path.Combine(idir, "RevitOnSite_20991231.csv"), csvText, new UTF8Encoding(true));
            var before = File.ReadAllBytes(ifc);
            var pyI = RunPy("-c", "import sys, uuid\nsys.path.insert(0, sys.argv[1])\nimport ifc_add_evidence as ia\nc = [0]\n"
                + "def u4():\n    c[0] += 1\n    return uuid.UUID(int=c[0] * 0x9E3779B97F4A7C15)\nia.uuid.uuid4 = u4\n"
                + "out, n, miss = ia.add_evidence(sys.argv[2])\nprint(n, ','.join(miss), sep='|')", Path.Combine(FindRoot(), "tools"), ifc).TrimEnd('\n');
            ulong k = 0;
            var csOut = Path.Combine(idir, "cs.ifc");
            var (done, miss) = IfcEvidence.Add(ifc, Path.Combine(idir, "RevitOnSite_20991231.csv"), csOut, () => (UInt128)(++k) * 0x9E3779B97F4A7C15UL);
            bool same = File.ReadAllBytes(Path.Combine(idir, "RevitOnSite_20991231_evidence.ifc")).AsSpan().SequenceEqual(File.ReadAllBytes(csOut));
            Console.WriteLine($"ifc_add_evidence {label}: 구역 {done} · CSV 에만 {string.Join(",", miss)} · Python 과 바이트 동일 {(same ? "예" : "아니오")}");
            if (!same) Console.WriteLine("    Python: " + File.ReadAllText(Path.Combine(idir, "RevitOnSite_20991231_evidence.ifc"))[text.LastIndexOf("ENDSEC;", StringComparison.Ordinal)..].Replace("\r", "") + "\n    C#    : " + File.ReadAllText(csOut)[text.LastIndexOf("ENDSEC;", StringComparison.Ordinal)..].Replace("\r", ""));
            Check(same && pyI == $"{done}|{string.Join(",", miss)}" && File.ReadAllBytes(ifc).AsSpan().SequenceEqual(before), "ifc " + label + " Python 대조·원본 그대로");
            IfcEvidence.Add(ifc, Path.Combine(idir, "RevitOnSite_20991231.csv"), ifc, () => (UInt128)(++k));   // 그 자리에서 바꾸기 ([IFC 내보내기] 방식)
            var inPlace = File.ReadAllText(ifc);
            Check(inPlace.Contains("'Pset_RevitOnSite_Evidence'") && inPlace.StartsWith(text[..text.LastIndexOf("ENDSEC;", StringComparison.Ordinal)]) && !Directory.EnumerateFiles(idir, "*.tmp").Any(),
                "ifc " + label + " 그 자리 쓰기 (임시 파일 없음)");
        }
    }
    finally { Directory.Delete(idir, true); }
}
Console.WriteLine(fails == failsBefore ? "IFC 근거 속성 OK" : "IFC 근거 속성 FAIL");

// 24) 파장 지표 (kpi.py, 대시보드 '지표' 줄): kpi.py --self-test 와 같은 시나리오 — 시연 자료 그대로 / 13F 구역·동바리 확인 4줄·9F-B1 요청 제출·감리 승인(서명 일시) 뒤, 다섯 줄을 Python 과 대조
failsBefore = fails;
Console.WriteLine("\n파장 지표");
if (python is not null)
{
    var kd = Directory.CreateTempSubdirectory("RevitOnSite_kpi_").FullName;
    try
    {
        RunPy(Path.Combine(FindRoot(), "tools", "make_demo_data.py"), kd);
        var kAt = new DateTime(2026, 12, 31, 9, 0, 0);
        void SameKpi(string label)
        {
            var pyK = RunPy("-c", "import sys, kpi\nfrom datetime import datetime\nprint(*kpi.lines(kpi.kpis(sys.argv[1], datetime(2026, 12, 31, 9))), sep=chr(10))", kd).TrimEnd('\n');
            var csK = string.Join("\n", Kpi.Lines(Kpi.Of(kd, kAt)));
            Console.WriteLine($"kpi {label}: Python 과 같음 {(pyK == csK ? "예" : "아니오")}");
            if (pyK != csK) Console.WriteLine($"    Python: {pyK.Replace("\n", " / ")}\n    C#    : {csK.Replace("\n", " / ")}");
            Check(pyK == csK, "kpi " + label + " Python 대조");
        }
        var k0 = Kpi.Of(kd, kAt);
        Check(k0.ReviewMedian is null && k0.RemovalSuspect == 0 && k0.Of == 0, "kpi 1 시연 자료: 감리 확인 기록 없음·무단 해체 0·타설 전 확인 대상 0");
        SameKpi("시연 자료");
        RunPy("-c", "import sys, os, approval as ap, evaluate as ev\nfrom datetime import datetime\nt = sys.argv[1]\nat = datetime(2026, 12, 31, 9)\n"
            + "zs = ev.read(t, 'zones.csv')\nf = open(os.path.join(t, 'zones.csv'), 'a', encoding='utf-8')\n"
            + "f.write(''.join(f\"13F-{z['zone_id'].split('-', 1)[1]},13,{z['x0']},{z['y0']},{z['x1']},{z['y1']}\\n\" for z in zs if z['floor'] == '12'))\nf.close()\n"
            + "f = open(os.path.join(t, 'shore_checks.csv'), 'w', encoding='utf-8')\n"
            + "f.write('zone_id,timestamp,present,actor,photo\\n' + ''.join(f'{z},2026-12-31T08:30,예,시험,\\n' for z in ('12F-B1', '11F-B1', '10F-B1')) + '9F-A2,2026-12-31T08:40,아니오,시험,\\n')\nf.close()\n"
            + "b1 = next(r for r in ev.evaluate(t, at) if r['zone_id'] == '9F-B1')\n"
            + "ap.record(t, '9F-B1', '요청 제출', '시험', b1, now=datetime(2026, 12, 30, 9))\n"
            + "ap.record(t, '9F-B1', '감리 승인 기록', '시험', b1, now=at, approver='감리', signed_at='2026-12-31 15:00')\n"
            + "c1 = next(r for r in ev.evaluate(t, at) if r['zone_id'] == '9F-C1')\n"
            + "ap.record(t, '9F-C1', '요청 제출', '시험', c1, now=datetime(2026, 12, 29, 9, 30))\n"
            + "ap.record(t, '9F-C1', '감리 승인 기록', '시험', c1, now=datetime(2026, 12, 30, 11, 15))", kd);
        var k1 = Kpi.Of(kd, kAt);
        Check(k1.ReviewHours.Any(h => h.Zone == "9F-B1" && h.Hours == 30.0) && k1.RemovalSuspect == 1 && k1.Checked == 3 && k1.Of >= 3 && k1.AssumedOf == 24 && k1.EvidenceFull is not null,
            "kpi 2 9F-B1 30 h (서명 일시)·무단 해체 1·타설 전 확인 3");
        SameKpi("요청·승인 2구역(짝수 중앙값)·동바리 확인 뒤");
    }
    finally { Directory.Delete(kd, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "파장 지표 OK" : "파장 지표 FAIL");

// 25) 감사 기록 (audit_export.py, 총괄 11-05 ⑩): 시연 자료 + 동바리 확인·해체 후 점검·요청 제출·감리 승인(서명)·판정 이력 → 감사기록 CSV 를 Python 과 바이트 대조
failsBefore = fails;
Console.WriteLine("\n감사 기록");
if (python is not null)
{
    var ud = Directory.CreateTempSubdirectory("RevitOnSite_audit_").FullName;
    try
    {
        RunPy(Path.Combine(FindRoot(), "tools", "make_demo_data.py"), ud);
        RunPy("-c", "import sys, os, approval as ap, evaluate as ev, alerts as al\nfrom datetime import datetime\nt = sys.argv[1]\nat = datetime(2026, 12, 31, 9)\n"
            + "open(os.path.join(t, 'shore_checks.csv'), 'w', encoding='utf-8').write('zone_id,timestamp,present,actor,photo' + chr(10) + '9F-A2,2026-12-31T08:40,아니오,박안전,\"a, b.jpg\"' + chr(10))\n"
            + "open(os.path.join(t, 'after_check.csv'), 'w', encoding='utf-8').write('zone_id,date,item_code,result,by,photo' + chr(10) + '9F-A1,2026-12-31,AC-1,OK,박안전,' + chr(10) + '9F-A1,2026-12-31,AC-4,NG' + chr(10))\n"
            + "b1 = next(r for r in ev.evaluate(t, at) if r['zone_id'] == '9F-B1')\n"
            + "ap.record(t, '9F-B1', '요청 제출', '김현장', b1, note='요청서 제출', now=datetime(2026, 12, 30, 9))\n"
            + "ap.record(t, '9F-B1', '감리 승인 기록', '김현장', b1, now=at, approver='이감리', signed_at='2026-12-31 15:00')\n"
            + "al.log_eval(t, at, '해체 승인 요청 가능 4 · 존치 2 · 공시체 시험 필요 0', now=datetime(2026, 12, 31, 9, 5))\n"
            + "old = open(os.path.join(t, 'install_inspection.csv'), encoding='utf-8').read().splitlines()\nopen(os.path.join(t, 'install_inspection.csv'), 'w', encoding='utf-8').write(chr(10).join([old[0] + ',stage,shore_ids,timestamp,source'] + [x + ',,,,' for x in old[1:]] + ['9F-A2,SHR-B,NG,박안전,2026-12-31,,타설 전,9F-A2-R01C01;9F-A2-R01C02,2026-12-31T08:30,휴대폰']) + chr(10))\n", ud);   // 60차: F16 점검 줄
        RunPy("-c", "import sys, audit_export as au\nau.write_csv(au.rows(sys.argv[1]), sys.argv[2])", ud, Path.Combine(ud, "py.csv"));
        var rs = AuditExport.Rows(ud);
        AuditExport.WriteCsv(rs, Path.Combine(ud, "cs.csv"));
        bool same = File.ReadAllBytes(Path.Combine(ud, "py.csv")).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(ud, "cs.csv")));
        Console.WriteLine($"audit_export: {rs.Count}줄 · Python 과 바이트 동일 {(same ? "예" : "아니오")}");
        if (!same) Console.WriteLine($"    Python: {File.ReadAllText(Path.Combine(ud, "py.csv"))}\n    C#    : {File.ReadAllText(Path.Combine(ud, "cs.csv"))}");
        Check(same && rs.Select(r => r.When).SequenceEqual(rs.Select(r => r.When).OrderBy(w => w, StringComparer.Ordinal)) && rs.Any(r => r.Source == "판정 이력")
              && rs.First(r => r.Source == "동바리 확인").Detail == "a, b.jpg"
              && rs.Any(r => r.Source == "동바리 점검" && r.Action == "타설 전 · SHR-B · NG" && r.Detail == "동바리 9F-A2-R01C01;9F-A2-R01C02 · 휴대폰"), "audit_export Python 바이트 대조·시각 순·쉼표 칸·F16 점검 줄");
    }
    finally { Directory.Delete(ud, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "감사 기록 OK" : "감사 기록 FAIL");

// 26) 설치 검측이 타설 뒤 경고 (evidence.py install_after_pour, 총괄 11-05 25차): 타설 전·뒤·판정일 뒤 기록·타설 안 한 구역 → Python 과 줄 단위, 원장 경고에 들어가는지
failsBefore = fails;
Console.WriteLine("\n설치 검측이 타설 뒤");
if (python is not null)
{
    var idr = Directory.CreateTempSubdirectory("RevitOnSite_instpour_").FullName;
    try
    {
        File.WriteAllText(Path.Combine(idr, "pours.csv"), "zone_id,pour_date,pour_time,mix,fck\n9F-A1,2026-11-26,08:00,보통,24\n9F-B1,2026-11-26,08:00,보통,24\n10F-A1,2027-01-05,08:00,보통,24\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(idr, "install_inspection.csv"), "zone_id,item_code,result,inspector,date,photo_path\n"
            + "9F-B1,H,OK,박,2026-11-24,\n9F-A1,H,OK,박,2026-11-24,\n9F-A1,V,OK,박,2026-11-28T10:00,\n9F-B1,V,OK,박,2027-01-02,\n10F-A1,H,OK,박,2027-01-06,\n 9F-A1 ,S,OK,박,2026-12-01,\n", new UTF8Encoding(false));
        foreach (var day in new[] { new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 10), new DateOnly(2026, 11, 27) })
        {
            var iso = day.ToString("yyyy-MM-dd", inv);
            var pyP = RunPy("-c", "import sys, evidence as evd\nprint(*(z + '=' + w for z, w in evd.install_after_pour(sys.argv[1], sys.argv[2]).items()), sep=chr(10))", idr, iso).TrimEnd('\n');
            var csP = string.Join("\n", Evidence.InstallAfterPour(idr, day).Select(kv => kv.Key + "=" + kv.Value));
            Console.WriteLine($"install_after_pour {iso}: {(csP.Length == 0 ? 0 : csP.Split('\n').Length)}건, Python 과 같음 {(pyP == csP ? "예" : "아니오")}");
            if (pyP != csP) Console.WriteLine($"    Python: {pyP}\n    C#    : {csP}");
            Check(pyP == csP, "install_after_pour " + iso + " Python 대조");
        }
        Check(Evidence.LedgerAt(idr, new DateOnly(2026, 12, 31))["9F-A1"].Warnings.Contains("9F-A1: 설치 검측(12-01)이 타설(11-26) 뒤 — 기록 확인 필요"), "install_after_pour 원장 경고에 들어감");
    }
    finally { Directory.Delete(idr, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "설치 검측이 타설 뒤 OK" : "설치 검측이 타설 뒤 FAIL");

// 27) 검측 패키지 (package.py, F11): 시연 자료 + 근거 원장 첨부 1건 + 현장 사진 3장(F15: 9F-B1 동바리 확인·해체 신고, 9F-A2 해체 후 점검 PNG)을 두 사본 폴더에 → Python build / C# Build 결과의 글 파일(html·csv) 바이트 대조, 목록 해시 = 파일, 같은 날 다시 만들면 하나만
failsBefore = fails;
Console.WriteLine("\n검측 패키지");
if (python is not null)
{
    var pk = Directory.CreateTempSubdirectory("RevitOnSite_pkg_").FullName;
    try
    {
        string pa = Path.Combine(pk, "py"), pb = Path.Combine(pk, "cs");
        Directory.CreateDirectory(pa);
        RunPy(Path.Combine(FindRoot(), "tools", "make_demo_data.py"), pa);
        RunPy("-c", "import sys, os, evidence, photos\nfrom datetime import datetime\nt = sys.argv[1]\natt = os.path.join(t, '작업계획서_9F.pdf')\nopen(att, 'wb').write(b'%PDF-1.4 test')\nevidence.add(t, '9F-B1', '2026-12-31', 4, '시험', file=att)\n"
            + "at = datetime(2026, 12, 31, 8, 30)\njpg = bytes([255, 216, 255, 224]) + bytes(range(256))\n"
            + "p1, s1 = photos.save(t, jpg, '9F-B1', 'shore_check', at)\np2, s2 = photos.save(t, jpg, '9F-A2', 'report', at)\n"
            + "p3, s3 = photos.save(t, bytes([137, 80, 78, 71, 13, 10, 26, 10]) + bytes(50), '9F-A2', 'after_check', at)\nnl = chr(10)\n"
            + "p4, s4 = photos.save(t, jpg + b'up', '10F-B1', 'shore_check', datetime(2026, 12, 31, 8, 40))\np5, s5 = photos.save(t, jpg + b'old', '10F-B1', 'shore_check', datetime(2026, 12, 29, 8, 0))\n"
            + "open(os.path.join(t, 'shore_checks.csv'), 'w', encoding='utf-8', newline='').write('zone_id,timestamp,present,actor,photo,source,company,photo_sha256' + nl"
            + " + '9F-B1,2026-12-31T08:30,예,박안전,' + p1 + ',,,' + s1 + nl + '9F-A2,2026-12-31T08:45,아니오,최반장,' + p2 + ',협력업체 신고,가나,' + s2 + nl + '10F-B1,2026-12-31T08:40,예,박안전,' + p4 + ',,,' + s4 + nl + '10F-B1,2026-12-29T08:00,예,박안전,' + p5 + ',,,' + s5 + nl)\n"
            + "open(os.path.join(t, 'after_check.csv'), 'w', encoding='utf-8', newline='').write('zone_id,date,item_code,result,by,photo,photo_sha256' + nl + '9F-A2,2026-12-31,AC-1,OK,박안전,' + p3 + ',' + s3 + nl)", pa);
        foreach (var f in Directory.EnumerateFiles(pa, "*", SearchOption.AllDirectories))
        {
            var dst = Path.Combine(pb, Path.GetRelativePath(pa, f));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(f, dst);
        }
        var pkAt = new DateTime(2026, 12, 31, 9, 0, 0);
        RunPy("-c", "import sys, package\nfrom datetime import datetime\npackage.build(sys.argv[1], datetime(2026, 12, 31, 9), '시험')", pa);
        var (outB, filesB, reqB) = Package.Build(pb, pkAt, "시험");
        var outA = Path.Combine(pa, "검측패키지_20261231");
        var diff = new List<string>();
        foreach (var n in new[] { "검측요청서.html", "슬래브이력서.html", "감사기록.csv", "근거원장.csv", "목록.csv", "index.html", "감사기록.html", "근거원장.html" })
            if (!File.ReadAllBytes(Path.Combine(outA, n)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(outB, n)))) diff.Add(n);
        Console.WriteLine($"package: 요청 구역 {reqB.Count} · 파일 {filesB.Count} · Python 과 다른 글 파일 {(diff.Count == 0 ? "없음" : string.Join(", ", diff))}");
        Check(diff.Count == 0, "package 글 파일 8개 Python 바이트 대조 (2-6 근거원장·감사기록 html)");
        Check(reqB.SequenceEqual(new[] { "9F-A1", "9F-B1", "9F-C1", "9F-C2" }) && filesB.Any(f => f.Name.StartsWith("첨부/9F-B1_4_", StringComparison.Ordinal))
              && filesB.All(f => new FileInfo(Path.Combine(outB, f.Name)).Length == f.Bytes)
              && File.ReadAllText(Path.Combine(outB, "index.html")).Contains("승인하지 않으며"), "package 요청 구역 4·첨부 사본·목록 크기 = 파일·승인 아님 문구");
        var photoFiles = filesB.Where(f => f.Name.StartsWith("첨부/사진/", StringComparison.Ordinal)).Select(f => f.Name).ToList();
        var jpgB = new byte[] { 255, 216, 255, 224 }.Concat(Enumerable.Range(0, 256).Select(i => (byte)i)).ToArray();
        var jpgBSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(jpgB)).ToLowerInvariant();
        // 11-09: 해체 신고(상태 7)가 있는 구역은 요청 구역에서 빠지므로 신고 사진은 요청 구역 아닌 9F-A2 에 둠 → 패키지에 안 들어감
        Check(photoFiles.Count == 2 && photoFiles.Count(n => n.Contains("/9F-B1_")) == 1 && photoFiles.Count(n => n.Contains("/10F-B1_동바리확인_084000_")) == 1
              && !photoFiles.Any(n => n.Contains("/9F-A2_"))
              && File.ReadAllText(Path.Combine(outB, "슬래브이력서.html")).Contains("<tr><th>현장 사진</th>")
,
            $"package 현장 사진: 요청 구역(9F-B1) 확인 1장 + 현장 확인 ① 위층(10F-B1) 최근 24 h 1장 (12-29 것·9F-A2 신고 아님), 이력서 '현장 사진' 줄 — {string.Join(", ", photoFiles)}");
        int nPh = Directory.GetFiles(Path.Combine(pb, Photos.Dir), "*", SearchOption.AllDirectories).Length;
        var (cpath, csha) = Photos.Save(pb, jpgB, "9F-B1", "shore_check", new DateTime(2026, 12, 31, 10, 0, 0));
        Check(cpath == $"사진/20261231/9F-B1_동바리확인_083000_{csha[..8]}.jpg" && Directory.GetFiles(Path.Combine(pb, Photos.Dir), "*", SearchOption.AllDirectories).Length == nPh,
            "photos 이름 규칙 = Python, 같은 사진 두 번 = Python 이 만든 그 파일 (" + cpath + ")");
        var pyPhErr = RunPy("-c", "import photos\nfrom datetime import datetime\nfor d in (b'not a photo, text saved as x.jpg', bytes([255, 216, 255]) + bytes(photos.MAX_BYTES)):\n"
            + "    try: photos.save('.', d, 'X', 'report', datetime(2026, 1, 1))\n    except ValueError as x: print(x)\n"
            + "for s in ('@@@@', 'data:image/jpeg;base64', 'YWJj'):\n    try: print(len(photos.decode(s)))\n    except ValueError as x: print(x)").TrimEnd();
        var csPhErr = new List<string>();
        foreach (var d in new[] { Encoding.UTF8.GetBytes("not a photo, text saved as x.jpg"), new byte[] { 255, 216, 255 }.Concat(new byte[Photos.MaxBytes]).ToArray() })
            try { Photos.Save(".", d, "X", "report", new DateTime(2026, 1, 1)); } catch (InvalidDataException x) { csPhErr.Add(x.Message); }
        foreach (var s in new[] { "@@@@", "data:image/jpeg;base64", "YWJj" })
            try { csPhErr.Add(Photos.Decode(s).Length.ToString(CultureInfo.InvariantCulture)); } catch (InvalidDataException x) { csPhErr.Add(x.Message); }
        Check(pyPhErr.Replace("\r", "") == string.Join("\n", csPhErr), "photos 거절 문구 Python 과 같음 (텍스트·크기 초과·base64 아님)" + (pyPhErr.Replace("\r", "") == string.Join("\n", csPhErr) ? "" : $"\n    Python: {pyPhErr}\n    C#    : {string.Join(" / ", csPhErr)}"));
        Check(Photos.Usage(pb).Count == 5 && Photos.Usage(pb).Bytes == Directory.GetFiles(Path.Combine(pb, Photos.Dir), "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
              && Photos.Usage(Path.Combine(pb, "없는 폴더")) == (0, 0) && Photos.Usage(null) == (0, 0), "photos 사진 폴더 용량 (프로젝트 설정 표시) = 5장·바이트 합, 없으면 0");
        Package.Build(pb, pkAt, "시험");
        Check(Directory.GetDirectories(pb, "검측패키지_*").Length == 1, "package 같은 날 다시 만들면 하나만");
        string none = "";
        try { Package.Build(pb, new DateTime(2026, 12, 1, 9, 0, 0), "시험"); } catch (InvalidDataException x) { none = x.Message; }
        Check(none.Contains("요청 가능 구역이 없어"), "package 요청 가능 구역 없으면 안 만듦: " + none);
    }
    finally { Directory.Delete(pk, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "검측 패키지 OK" : "검측 패키지 FAIL");

// 28) 지표 추이 (kpi_trend.py, F13): 시연 자료 + 동바리 확인·요청·감리 승인 + 판정 이력 3줄(같은 시각 2번) → CSV 를 Python 과 바이트 대조, last=1 = 마지막 줄
failsBefore = fails;
Console.WriteLine("\n지표 추이");
if (python is not null)
{
    var td = Directory.CreateTempSubdirectory("RevitOnSite_trend_").FullName;
    try
    {
        RunPy(Path.Combine(FindRoot(), "tools", "make_demo_data.py"), td);
        RunPy("-c", "import sys, os, approval as ap, evaluate as ev\nfrom datetime import datetime\nt = sys.argv[1]\nat = datetime(2026, 12, 31, 9)\n"
            + "open(os.path.join(t, 'shore_checks.csv'), 'w', encoding='utf-8').write('zone_id,timestamp,present,actor,photo' + chr(10) + '9F-A2,2026-12-31T08:40,아니오,시험,' + chr(10))\n"
            + "b1 = next(r for r in ev.evaluate(t, at) if r['zone_id'] == '9F-B1')\n"
            + "ap.record(t, '9F-B1', '요청 제출', '시험', b1, now=datetime(2026, 12, 30, 9))\n"
            + "ap.record(t, '9F-B1', '감리 승인 기록', '시험', b1, now=at, approver='감리', signed_at='2026-12-31 15:00')\n"
            + "open(os.path.join(t, 'eval_log.csv'), 'w', encoding='utf-8').write('eval_at,logged_at,summary' + chr(10) + '2026-12-31T09:00,x,b' + chr(10) + '2026-12-20T09:00,x,a' + chr(10) + '2026-12-31T09:00,y,b' + chr(10))", td);
        RunPy("kpi_trend.py", td, "--csv", Path.Combine(td, "py.csv"));
        var tr = KpiTrend.Trend(td);
        KpiTrend.WriteCsv(tr, Path.Combine(td, "cs.csv"));
        bool same = File.ReadAllBytes(Path.Combine(td, "py.csv")).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(td, "cs.csv")));
        Console.WriteLine($"kpi_trend: {tr.Count}줄 · Python 과 바이트 동일 {(same ? "예" : "아니오")}");
        if (!same) Console.WriteLine($"    Python: {File.ReadAllText(Path.Combine(td, "py.csv"))}\n    C#    : {File.ReadAllText(Path.Combine(td, "cs.csv"))}");
        var oneT = KpiTrend.Trend(td, last: 1);
        Check(same && tr.Select(r => r.EvalAt).SequenceEqual(new[] { "2026-12-20T09:00", "2026-12-31T09:00" }) && oneT.Count == 1 && oneT[0] == tr[^1]
              && KpiTrend.Lines(tr).Count == 2 && KpiTrend.Lines(oneT).Count == 0, "kpi_trend Python 바이트·같은 시각 한 번·시각 순·last·추이 두 줄");
    }
    finally { Directory.Delete(td, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "지표 추이 OK" : "지표 추이 FAIL");

// 29) 센서 업체 CSV 가져오기 (sensor_import.py, benchmark G5): 샘플 사본 두 벌에 같은 업체 파일 3개(CP949 한국어 머리·UTF-8 영어 머리+강도·위치 열·--map) → 결과 요약·sensors.csv·sensor_strength.csv 바이트 대조
failsBefore = fails;
Console.WriteLine("\n센서 업체 CSV 가져오기");
if (python is not null)
{
    var sd = Directory.CreateTempSubdirectory("RevitOnSite_sensimp_").FullName;
    try
    {
        foreach (var t in new[] { "py", "cs" })
        {
            Directory.CreateDirectory(Path.Combine(sd, t));
            foreach (var f in Directory.EnumerateFiles(Path.Combine(FindRoot(), "shoring", "sample", "data"))) File.Copy(f, Path.Combine(sd, t, Path.GetFileName(f)));
        }
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        File.WriteAllText(Path.Combine(sd, "a.csv"), "센서번호,측정일시,온도(°C)\r\nS1,2027/01/01 8:00,7.5\r\nS1,2027-01-01 09:00:30,\r\nS2,어제,3\r\nNEW9,2027-01-01 08:00,4\r\n"
            + "S1,2027-01-01 10:00,6\r\nS1,2027-01-01 10:00,6.5\r\nS2,2026-11-26 08:00,2.29\r\nS3,2026-12-31 09:00,1.25\r\n", System.Text.Encoding.GetEncoding(949));
        File.WriteAllText(Path.Combine(sd, "b.csv"), "Serial No.,Date/Time,Temperature (C),Strength (MPa),Floor,X,Y\nS3,2027-01-01T09:00,8,21.5,,,\nW9,2027-01-01T09:00,,18,10,5,4\nW9,2027-01-01T10:00,,\"18,5\",10,5,4\n", new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(sd, "c.csv"), "ch,when,val\nS4,2027-01-01 11:00,9.5\n", new UTF8Encoding(false));
        string Fmt(int rows, (int, int)? t, (int, int)? m, IEnumerable<string> np, int bad, IEnumerable<KeyValuePair<string, string>> c) =>
            $"{rows}|{(t is { } a ? $"{a.Item1},{a.Item2}" : "None")}|{(m is { } b ? $"{b.Item1},{b.Item2}" : "None")}|{string.Join(",", np)}|{bad}|{string.Join(";", c.Select(kv => kv.Key + "=" + kv.Value))}";
        var pyS = RunPy("-c", "import sys, os, sensor_import as si\nd, t = sys.argv[1], sys.argv[2]\n"
            + "for f, mp in (('a.csv', None), ('b.csv', None), ('c.csv', {'ch': 'sensor_id', 'when': 'timestamp', 'val': 'temp_c'})):\n"
            + "    r = si.import_csv(os.path.join(d, f), t, mp)\n"
            + "    g = lambda v: 'None' if v is None else f'{v[0]},{v[1]}'\n"
            + "    print(r['rows'], g(r['temp_c']), g(r['mpa']), ','.join(r['no_position']), r['bad'], ';'.join(k + '=' + v for k, v in r['columns'].items()), sep='|')",
            sd, Path.Combine(sd, "py")).TrimEnd('\n');
        var csS = string.Join("\n", new[] { ("a.csv", (Dictionary<string, string>?)null), ("b.csv", null), ("c.csv", new Dictionary<string, string> { ["ch"] = "sensor_id", ["when"] = "timestamp", ["val"] = "temp_c" }) }
            .Select(x => { var r = SensorImport.Import(Path.Combine(sd, x.Item1), Path.Combine(sd, "cs"), x.Item2); return Fmt(r.Rows, r.Temp, r.Mpa, r.NoPosition, r.Bad, r.Columns); }));
        bool f1 = File.ReadAllBytes(Path.Combine(sd, "py", "sensors.csv")).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(sd, "cs", "sensors.csv")));
        bool f2 = File.ReadAllBytes(Path.Combine(sd, "py", "sensor_strength.csv")).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(sd, "cs", "sensor_strength.csv")));
        Console.WriteLine($"sensor_import: 요약 Python 과 같음 {(pyS == csS ? "예" : "아니오")} · sensors.csv {(f1 ? "같음" : "다름")} · sensor_strength.csv {(f2 ? "같음" : "다름")}");
        if (pyS != csS) Console.WriteLine($"    Python: {pyS.Replace("\n", " / ")}\n    C#    : {csS.Replace("\n", " / ")}");
        Check(pyS == csS && f1 && f2, "sensor_import Python 대조 (요약·두 파일 바이트)");
        Check(SensorImport.Stamp("2026-2-30 8:00") is null && SensorImport.Stamp("2026.1.5T7:05") == "2026-01-05T07:05" && SensorImport.RoleOf("센서 온도") == "temp_c"
              && SensorImport.RoleOf("Humidity") is null, "sensor_import 시각·열 이름 규칙");
    }
    finally { Directory.Delete(sd, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "센서 업체 CSV 가져오기 OK" : "센서 업체 CSV 가져오기 FAIL");

// 30) 현장 보정 곡선 (evaluate.py calibration, G2 — 예측일만): 샘플 사본 → 기본 꺼짐 / 자료 부족 2건 / 충분(7일차 3 + 34일차 1, 양생 5·25 ℃) / options.json 켬 — 판정표 Python 바이트 대조
failsBefore = fails;
Console.WriteLine("\n현장 보정 곡선");
if (python is not null)
{
    var cd = Directory.CreateTempSubdirectory("RevitOnSite_cal_").FullName;
    try
    {
        foreach (var f in Directory.EnumerateFiles(Path.Combine(FindRoot(), "shoring", "sample", "data"))) File.Copy(f, Path.Combine(cd, Path.GetFileName(f)));
        var calAt = new DateTime(2026, 12, 31, 9, 0, 0);
        void Cyl(params string[] rows) => File.WriteAllText(Path.Combine(cd, "cylinders.csv"), "zone_id,test_date,curing,f1,f2,f3\n" + string.Concat(rows.Select(r => r + "\n")), new UTF8Encoding(false));
        string SameCal(string label, bool? calibrate, double cure = 5.0)
        {
            string py = Path.Combine(cd, "py.csv"), cs = Path.Combine(cd, "cs.csv");
            var a = new List<string> { "evaluate.py", cd, "--at", "2026-12-31T09:00", "--cure-min", cure.ToString(inv), "--out", py };
            if (calibrate == true) a.Add("--calibrate");
            RunPy(a.ToArray());
            Evaluator.Write(Evaluator.Evaluate(cd, calAt, cure, calibrate: calibrate), cs);
            bool same = File.ReadAllBytes(py).AsSpan().SequenceEqual(File.ReadAllBytes(cs));
            Console.WriteLine($"calibrate {label}: 판정표 Python 과 바이트 동일 {(same ? "예" : "아니오")}");
            Check(same, "calibrate " + label + " 판정표 Python 대조");
            return File.ReadAllText(cs);
        }
        string PyCal(double cure) => RunPy("-c", "import sys, evaluate as ev\nfrom datetime import datetime\nc = ev.calibration(sys.argv[1], datetime(2026, 12, 31, 9), float(sys.argv[2]))\n"
            + "print(';'.join(f'{k}={v[0]!r},{v[1]}' for k, v in sorted(c.items())))", cd, cure.ToString(inv)).Trim();
        string CsCal(double cure) => string.Join(";", Evaluator.Calibration(cd, calAt, cure).OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={(kv.Value.S is double sv ? Py.Repr(sv) : "None")},{kv.Value.N}"));
        var off = SameCal("기본 꺼짐 (options.json 없음)", null);
        Check(!off.Contains("예측일:"), "calibrate 기본 꺼짐 — 사유 태그 없음 (예전 판정표 그대로)");
        Cyl("9F-A1,2026-12-03,현장봉함,15,15.2,15.4", "9F-B1,2026-12-03,현장수중,15.1,15.3,15.5", "9F-C1,2026-12-03,표준양생,20,20,20");
        Check(SameCal("자료 부족 2건 켬", true).Contains("; 예측일: 기본 곡선 [가정] (보정 자료 부족 — 공시체 2건 < 3)") && PyCal(5) == CsCal(5), "calibrate 자료 부족 → 기본 곡선 태그, 보정 값 Python 과 같음 (" + CsCal(5) + ")");
        Cyl("9F-A1,2026-12-03,현장봉함,16,16,16", "9F-B1,2026-12-03,현장봉함,17.2,17.2,17.2", "9F-C1,2026-12-03,현장봉함,18.4,18.4,18.4", "9F-A2,2026-12-30,현장봉함,25,25,25");
        var on = SameCal("충분 4건 켬", true);
        SameCal("충분 켬 양생 25 ℃ (34일차 te ≥ 28 뺌)", true, 25.0);
        Check(on.Contains("[보정]") && PyCal(5) == CsCal(5) && PyCal(25) == CsCal(25) && CsCal(5).EndsWith(",4", StringComparison.Ordinal) && CsCal(25).EndsWith(",3", StringComparison.Ordinal),
            $"calibrate 보정 값 Python 과 같음 — 양생 5 ℃ {CsCal(5)} · 25 ℃ {CsCal(25)} (te ≥ 28 뺌)");
        var offRows = Evaluator.Evaluate(cd, calAt, calibrate: false);
        var onRows = Evaluator.Evaluate(cd, calAt, calibrate: true);
        Check(offRows.Zip(onRows).All(p => p.First.Decision == p.Second.Decision && p.First.FEstMin == p.Second.FEstMin && p.First.LevelGate == p.Second.LevelGate)
              && offRows.Zip(onRows).Any(p => p.Second.PredictedDue < p.First.PredictedDue), "calibrate 판정·게이트·추정 강도 그대로, 예측일만 앞당김");
        File.WriteAllText(Path.Combine(cd, Evaluator.OptionsFile), "{\"calibrate_due\": true}", new UTF8Encoding(false));
        Check(SameCal("options.json 켬", null).Contains("[보정]"), "calibrate options.json 켬 = 켬 (Python·C# 같은 파일)");
        Evaluator.SetCalibrateDue(cd, false);
        Check(!Evaluator.CalibrateDueOn(cd) && File.ReadAllText(Path.Combine(cd, Evaluator.OptionsFile)).Contains("\"calibrate_due\": false"), "calibrate 설정 끄기 → options.json false");
        // v6 층 높이 보정 (options.json height_correct, 기본 꺼짐): 켬 · 층고 3.5 m·노출 0.3 ℃ · 보정 곡선과 함께 — 판정표 Python 바이트 대조, 끄기 → 꺼짐
        Evaluator.SetHeightCorrect(cd, true);
        var hOn = SameCal("층 높이 보정 켬", null);
        Check(Evaluator.HeightOption(cd).On && hOn.Contains("층 높이 보정 -0.2") && Evaluator.HeightTag(-0.0) == " · 층 높이 보정 -0.00 °C [가정]", "층 높이 보정 켬 — 기상 예측 구역 태그 (Python 과 같은 판정표)");
        File.WriteAllText(Path.Combine(cd, Evaluator.OptionsFile), "{\"height_correct\": true, \"story_m\": \"3.5\", \"exposure_c\": 0.3, \"calibrate_due\": true}", new UTF8Encoding(false));
        SameCal("층 높이 보정 층고 3.5·노출 0.3 + 보정 곡선", null);
        Check(Evaluator.Calibration(cd, calAt).Count > 0 && PyCal(5) == CsCal(5), "층 높이 보정 + 보정 곡선 값 Python 과 같음 (" + CsCal(5) + ")");
        Evaluator.SetHeightCorrect(cd, false);
        Check(!Evaluator.HeightOption(cd).On, "층 높이 보정 끄기 → options.json false");
    }
    finally { Directory.Delete(cd, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "현장 보정 곡선 OK" : "현장 보정 곡선 FAIL");

// 31) 단계 해체·조기 탈형 (staged.py S1–S3, 총괄 11-05 — S4 전용 계획은 v6 에서 뺌): 따뜻한(20 ℃·공시체 10 MPa)·추운(8 ℃) 시험 폴더 + 시연 자료 양생 5·15 ℃ → 판넬 4조건·판넬 탈형 가능일·벽 거푸집 해체일 줄,
//     조기 탈형 검토서 HTML 을 Python 과 바이트 대조, Py.Mean = statistics.mean (경계 평균)
failsBefore = fails;
Console.WriteLine("\n단계 해체·조기 탈형");
if (python is not null)
{
    var sg = Directory.CreateTempSubdirectory("RevitOnSite_staged_").FullName;
    try
    {
        var dump = Path.Combine(sg, "dump.py");
        File.WriteAllText(dump, """
import os, sys
from datetime import date, datetime, timedelta
sys.path.insert(0, os.getcwd())   # 스크립트 파일로 돌리면 shoring 이 경로에 없음
import staged as s, evidence as evd
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(s.__file__)), '..', 'tools'))
import make_demo_data as md
base = sys.argv[1]
def folder(name, temp, cyl_low=False):
    p = os.path.join(base, name)
    os.makedirs(p)
    d0 = date(2026, 5, 1)
    w = lambda f, t: open(os.path.join(p, f), 'w', encoding='utf-8', newline='').write(t)
    w('zones.csv', 'zone_id,floor,x0,y0,x1,y1,slab_mm\n1F-A,1,0,0,40,10,210\n2F-A,2,0,0,40,10,210\n3F-A,3,0,0,10,10,210\n3F-B,3,10,0,20,10,210\n3F-C,3,20,0,30,10,600\n3F-D,3,30,0,40,10,210\n')
    w('pours.csv', 'zone_id,pour_date,pour_time,mix,fck\n1F-A,2026-04-01,08:00,보통,24\n2F-A,2026-04-15,08:00,보통,24\n' + ''.join(f'3F-{c},{d0},08:00,보통,24\n' for c in 'ABCD'))
    w('weather.csv', 'date,avgTa\n' + ''.join(f'{date(2026, 3, 1) + timedelta(i)},{temp}\n' for i in range(900)))
    if cyl_low:
        w('cylinders.csv', 'zone_id,test_date,curing,f1,f2,f3\n3F-B,2026-05-05,현장봉함,10,10,10\n')
    for z in ('3F-A', '3F-B', '3F-C'):
        evd.add(p, z, '2026-05-03', 2, '시험', doc_no=f'{s.REVIEW_KEY}-{z}')
    evd.add(p, '3F-D', '2026-05-03', 2, '시험', doc_no='WP-3F')
    return p
demo = os.path.join(base, 'demo')
md.make(demo)
cases = [(folder('warm', 20.0, True), datetime(2026, 5, 6, 9), 5.0), (folder('cold', 8.0), datetime(2026, 6, 1, 9), 5.0),
         (demo, datetime(2026, 12, 31, 9), 5.0), (demo, datetime(2026, 12, 31, 9), 15.0)]
out = []
for i, (f, at, cm) in enumerate(cases):
    for z, v in s.stages(f, at, cm).items():
        out.append('|'.join([str(i), z, v['panel']['decision'], ''.join('1' if c[1] else '0' for c in v['panel']['checks']), ' / '.join(c[2] for c in v['panel']['checks']), str(v['earliest_panel_date'] or '-')]))
        w = s.wall_strip(f, z, at, cm)
        out.append('|'.join([str(i), z, str(w['date_by_strength'] or '-'), str(w['date_by_days'] or '-'), w['basis'], str(w['passed']['강도']) + str(w['passed']['일수'])]))
        open(os.path.join(base, f'rv_{i}_{z}.html'), 'w', encoding='utf-8', newline='').write(s.review_html(f, at, z, project='시험', cure_min=cm))
import shutil   # 33차: 모델 층 1–15F + 타설 기록 9–12F — 기록 없는 층은 가장 아래층부터 사이클 (start 없으면 거꾸로 셈 / PRJ 시작일 10-01·7일)
mdl = os.path.join(base, 'model15')
shutil.copytree(demo, mdl)
zl = open(os.path.join(mdl, 'zones.csv'), encoding='utf-8').read().splitlines()
nine = [x for x in zl if x.startswith('9F-')]
open(os.path.join(mdl, 'zones.csv'), 'w', encoding='utf-8').write('\n'.join(zl + [f"{k}F-{x.split(',', 1)[0].split('-', 1)[1]},{k},{x.split(',', 2)[2]}" for k in [*range(1, 9), 13, 14, 15] for x in nine]) + '\n')
for z, v in s.stages(mdl, datetime(2026, 12, 31, 9), 15.0).items():
    out.append('|'.join(['h15', z, v['panel']['decision'], ''.join('1' if c[1] else '0' for c in v['panel']['checks']), ' / '.join(c[2] for c in v['panel']['checks']), str(v['earliest_panel_date'] or '-')]))
print('\n'.join(out))
""", new UTF8Encoding(false));
        var pyLines = RunPy(dump, sg).TrimEnd('\n').Split('\n');
        string D(DateOnly d) => d.ToString("yyyy-MM-dd", inv);
        var demoF = Path.Combine(sg, "demo");
        var cases = new (string F, DateTime At, double Cm)[] { (Path.Combine(sg, "warm"), new DateTime(2026, 5, 6, 9, 0, 0), 5.0), (Path.Combine(sg, "cold"), new DateTime(2026, 6, 1, 9, 0, 0), 5.0),
            (demoF, new DateTime(2026, 12, 31, 9, 0, 0), 5.0), (demoF, new DateTime(2026, 12, 31, 9, 0, 0), 15.0) };
        var csLines = new List<string>();
        int htmlN = 0, htmlSame = 0;
        string? firstHtmlDiff = null;
        for (int i = 0; i < cases.Length; i++)
        {
            var (f, sat, cm) = cases[i];
            foreach (var (z, v) in Staged.Stages(f, sat, cm))
            {
                csLines.Add(string.Join("|", i, z, v.PanelDecision, string.Concat(v.Checks.Select(c => c.Ok ? "1" : "0")), string.Join(" / ", v.Checks.Select(c => c.Basis)), v.EarliestPanelDate is { } ep ? D(ep) : "-"));
                var w = Staged.WallStrip(f, z, sat, cm);
                csLines.Add(string.Join("|", i, z, w.DateByStrength is { } a1 ? D(a1) : "-", w.DateByDays is { } a2 ? D(a2) : "-", w.Basis, (w.Passed!.Value.Strength ? "True" : "False") + (w.Passed.Value.Days ? "True" : "False")));
                htmlN++;
                if (File.ReadAllText(Path.Combine(sg, $"rv_{i}_{z}.html")) == Staged.ReviewHtml(f, sat, z, project: "시험", cureMin: cm)) htmlSame++; else firstHtmlDiff ??= $"rv_{i}_{z}";
            }
        }
        foreach (var (z, v) in Staged.Stages(Path.Combine(sg, "model15"), new DateTime(2026, 12, 31, 9, 0, 0), 15.0))
            csLines.Add(string.Join("|", "h15", z, v.PanelDecision, string.Concat(v.Checks.Select(c => c.Ok ? "1" : "0")), string.Join(" / ", v.Checks.Select(c => c.Basis)), v.EarliestPanelDate is { } ep15 ? D(ep15) : "-"));
        Check(csLines.Any(l => l.StartsWith("h15|9F-A1|" + Staged.PanelOk + "|1111|", StringComparison.Ordinal)), "staged 34차 모델 1–15F 가열 15 ℃: 9F-A1 판넬 탈형 요청 가능 (기록 없는 아래층 fck [가정])");
        int bad = pyLines.Zip(csLines).Count(t => t.First != t.Second) + Math.Abs(pyLines.Length - csLines.Count);
        Console.WriteLine($"staged: 줄 {csLines.Count} (Python {pyLines.Length}) 다른 줄 {bad} · HTML {htmlSame}/{htmlN} 같음");
        if (bad > 0)
        {
            var d0 = pyLines.Zip(csLines).FirstOrDefault(t => t.First != t.Second);
            Console.WriteLine($"    Python: {d0.First}\n    C#    : {d0.Second}");
        }
        if (firstHtmlDiff != null) Console.WriteLine("    HTML 다름: " + firstHtmlDiff);
        Check(bad == 0 && htmlSame == htmlN, "staged Python 대조 (판넬 4조건·판넬 탈형일·벽 해체일 줄 + 검토서 HTML 바이트)");
        var warmB = csLines.FirstOrDefault(l => l.StartsWith("0|3F-B|", StringComparison.Ordinal)) ?? "";
        Check(warmB.Contains("|판넬 존치|0111|"), "staged 공시체 10 MPa → ① 미충족 판넬 존치 (" + warmB.Split('|').Skip(2).FirstOrDefault() + ")");
        var pyMean = RunPy("-c", "import statistics as s\nfor v in ([19.9, 20.1, 20.0, 20.0], [0.1] * 7, [12.3, 7.7, 9.9], [-1.5, 2.25, 3.1], [20.000000000000004, 19.999999999999996]): print(repr(s.mean(v)))").Trim().Replace("\r", "").Split('\n');
        var csMean = new[] { new[] { 19.9, 20.1, 20.0, 20.0 }, Enumerable.Repeat(0.1, 7).ToArray(), new[] { 12.3, 7.7, 9.9 }, new[] { -1.5, 2.25, 3.1 }, new[] { 20.000000000000004, 19.999999999999996 } }
            .Select(v => Py.Repr(Py.Mean(v))).ToArray();
        Check(pyMean.SequenceEqual(csMean), "Py.Mean = statistics.mean (" + string.Join(", ", csMean) + ")");
    }
    finally { Directory.Delete(sg, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "단계 해체·조기 탈형 OK" : "단계 해체·조기 탈형 FAIL");

// 32) 동바리 상태 (V2 동바리 보기, shore_status.py — 총괄 11-05): 시연 자료 + 13F(타설 전) 3단계 폴더 (기록 없음 / 감리 확인·해체 완료·신고·무단 해체·설치 NG·동바리 확인 / 재받침)
//     + 샘플 자료를 Python dump 와 바이트 대조, 범례 순서
failsBefore = fails;
Console.WriteLine("\n동바리 상태 (V2)");
if (python is not null)
{
    var ss = Directory.CreateTempSubdirectory("RevitOnSite_shorestat_").FullName;
    try
    {
        var dump = Path.Combine(ss, "dump.py");
        File.WriteAllText(dump, """
import os, sys, shutil
from datetime import datetime
sys.path.insert(0, os.getcwd())
import shore_status as st, approval as ap, evaluate as ev, evidence
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(st.__file__)), '..', 'tools'))
import make_demo_data as md
base = sys.argv[1]
at = datetime(2026, 12, 31, 9)
s0 = os.path.join(base, 's0')
md.make(s0)
zs = ev.read(s0, 'zones.csv')
with open(os.path.join(s0, 'zones.csv'), 'a', encoding='utf-8') as f:
    f.write(''.join(f"13F-{z['zone_id'].split('-', 1)[1]},13,{z['x0']},{z['y0']},{z['x1']},{z['y1']}\n" for z in zs if z['floor'] == '12'))
s1 = os.path.join(base, 's1')
shutil.copytree(s0, s1)
r9 = {r['zone_id']: r for r in ev.evaluate(s1, at)}
ap.record(s1, '9F-B1', '감리 승인 기록', '공무', r9['9F-B1'], now=datetime(2026, 12, 30, 15))
ap.record(s1, '9F-C1', '감리 승인 기록', '공무', r9['9F-C1'], now=datetime(2026, 12, 30, 15))
ap.record(s1, '9F-C1', '해체 완료', '공무', r9['9F-C1'], now=datetime(2026, 12, 30, 16))
ap.record(s1, '9F-C2', '감리 승인 기록', '공무', r9['9F-C2'], now=datetime(2026, 12, 30, 15))
with open(os.path.join(s1, 'shore_checks.csv'), 'w', encoding='utf-8') as f:
    f.write('zone_id,timestamp,present,actor,photo,source\n' + ''.join(f'{z},2026-12-31T08:30,예,안전,,\n' for z in ('12F-A1', '11F-A1', '10F-A1'))
            + '9F-A2,2026-12-31T08:40,아니오,안전,,\n' + f'9F-C2,2026-12-30T17:00,아니오,반장,,{evidence.REPORT_SOURCE}\n')
with open(os.path.join(s1, 'install_inspection.csv'), 'a', encoding='utf-8') as f:
    f.write('10F-A2,SHR-S,NG,검측,2026-12-01,\n13F-B1,SHR-H,OK,검측,2027-01-02,\n')
s2 = os.path.join(base, 's2')
shutil.copytree(s1, s2)
ap.record(s2, '9F-C1', '재받침', '공무', r9['9F-C1'], now=datetime(2026, 12, 30, 18))
sample = os.path.join(os.path.dirname(os.path.abspath(st.__file__)), 'sample', 'data')
for i, (f, t) in enumerate([(s0, at), (s1, at), (s2, at), (s2, datetime(2026, 12, 30, 9)), (sample, at)]):
    open(os.path.join(base, f'py_{i}.txt'), 'w', encoding='utf-8', newline='').write(st.dump(st.status(f, t)))
print('|'.join(n + ':' + ','.join(map(str, c)) + ':' + d for n, c, d in st.LEGEND))
""", new UTF8Encoding(false));
        var legendPy = RunPy(dump, ss).Trim();
        var sAt = new DateTime(2026, 12, 31, 9, 0, 0);
        var sampleF = Path.Combine(FindRoot(), "shoring", "sample", "data");
        var cases = new (string F, DateTime At)[] { (Path.Combine(ss, "s0"), sAt), (Path.Combine(ss, "s1"), sAt), (Path.Combine(ss, "s2"), sAt), (Path.Combine(ss, "s2"), new DateTime(2026, 12, 30, 9, 0, 0)), (sampleF, sAt) };
        int same = 0;
        for (int i = 0; i < cases.Length; i++)
        {
            var pyT = File.ReadAllText(Path.Combine(ss, $"py_{i}.txt"));
            var csT = ShoreStatus.Dump(ShoreStatus.Status(cases[i].F, cases[i].At));
            if (pyT == csT) { same++; continue; }
            var d0 = pyT.Split('\n').Zip(csT.Split('\n')).FirstOrDefault(t => t.First != t.Second);
            Console.WriteLine($"    경우 {i} 다름\n    Python: {d0.First}\n    C#    : {d0.Second}");
        }
        var s1 = ShoreStatus.Status(Path.Combine(ss, "s1"), sAt).ToDictionary(r => r.ZoneId);
        Console.WriteLine($"shore_status: {same}/{cases.Length} 경우 Python 과 바이트 동일 · 9F-A2 {s1["9F-A2"].State} · 9F-C1 {s1["9F-C1"].State} · 10F-A2 {s1["10F-A2"].Install} · 12F-B1 점선 {(s1["12F-B1"].Stale.Length > 0 ? "예" : "아니오")}");
        Check(same == cases.Length, "shore_status Python 대조 (5경우)");
        Check(s1["9F-A2"].State == ShoreStatus.Suspect && s1["9F-B1"].State == ShoreStatus.Approved && s1["9F-C1"].State == ShoreStatus.Removed && s1["9F-C2"].State == ShoreStatus.Removed
              && s1["10F-A2"].Install == ShoreStatus.InstallNg && s1["13F-A1"].Install == ShoreStatus.InstallNeed && s1["12F-A1"].Stale == "" && s1["12F-B1"].Stale.Length > 0, "shore_status 상태 (무단 해체·감리 확인·해체됨·신고·설치 NG/필요·24 h)");
        var legendCs = string.Join("|", ShoreStatus.Legend.Select(l => $"{l.Name}:{l.R},{l.G},{l.B}:{l.Draw}"));
        Check(legendPy == legendCs, "shore_status 범례 = Python LEGEND");
    }
    finally { Directory.Delete(ss, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "동바리 상태 (V2) OK" : "동바리 상태 (V2) FAIL");

// 34) 규칙 게이트 세부값 (evaluate.gate_details — 판정 근거 창 비교 줄, 34차 A): 샘플 12-31 · 12-20 · 시연 자료(13F 구역 더함) 12-31 을 Python 과 대조
failsBefore = fails;
Console.WriteLine("\n규칙 게이트 세부값");
if (python is not null)
{
    var gd = Directory.CreateTempSubdirectory("RevitOnSite_gate_").FullName;
    try
    {
        var dump = Path.Combine(gd, "dump.py");
        File.WriteAllText(dump, """
import os, sys
from datetime import datetime
sys.path.insert(0, os.getcwd())
import evaluate as ev
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(ev.__file__)), '..', 'tools'))
import make_demo_data as md
base = sys.argv[1]
demo = os.path.join(base, 'demo')
md.make(demo)
zs = ev.read(demo, 'zones.csv')
with open(os.path.join(demo, 'zones.csv'), 'a', encoding='utf-8') as f:
    f.write(''.join(f"13F-{z['zone_id'].split('-', 1)[1]},13,{z['x0']},{z['y0']},{z['x1']},{z['y1']}\n" for z in zs if z['floor'] == '12'))
sample = os.path.join(os.path.dirname(os.path.abspath(ev.__file__)), 'sample', 'data')
out = []
for tag, f, at in (('s31', sample, datetime(2026, 12, 31, 9)), ('s20', sample, datetime(2026, 12, 20, 9)), ('d31', demo, datetime(2026, 12, 31, 9))):
    for z, g in ev.gate_details(f, at).items():
        out.append('|'.join([tag, z, str(g['above']), str(g['topped']), str(g['fresh']), ';'.join(f'{a},{b},{c:.4f}' for a, b, c in g['supports']),
                             '' if g['ratio'] is None else f"{g['ratio']:.4f}", '' if g['lim'] is None else f"{g['lim']:g}"]))
print('\n'.join(out))
""", new UTF8Encoding(false));
        var pyG = RunPy(dump, gd).TrimEnd('\n').Replace("\r", "").Split('\n');
        var csG = new List<string>();
        string B(bool? b) => b is null ? "None" : b.Value ? "True" : "False";
        foreach (var (tag, f, gat) in new[] { ("s31", data, new DateTime(2026, 12, 31, 9, 0, 0)), ("s20", data, new DateTime(2026, 12, 20, 9, 0, 0)), ("d31", Path.Combine(gd, "demo"), new DateTime(2026, 12, 31, 9, 0, 0)) })
            foreach (var (z, g) in Evaluator.GateDetails(f, gat))
                csG.Add(string.Join("|", tag, z, g.Above.ToString(inv), B(g.Topped), B(g.Fresh), string.Join(";", g.Supports.Select(x => $"{x.Floor},{x.Zone},{Py.F(x.Mpa, 4)}")),
                    g.Ratio is double r ? Py.F(r, 4) : "", g.Lim is double l ? Py.G(l) : ""));
        int badG = pyG.Zip(csG).Count(t => t.First != t.Second) + Math.Abs(pyG.Length - csG.Count);
        Console.WriteLine($"gate_details: 줄 {csG.Count} (Python {pyG.Length}) 다른 줄 {badG}");
        if (badG > 0) { var d0 = pyG.Zip(csG).FirstOrDefault(t => t.First != t.Second); Console.WriteLine($"    Python: {d0.First}\n    C#    : {d0.Second}"); }
        Check(badG == 0, "gate_details Python 대조 (샘플 12-31·12-20, 시연 자료 12-31)");
        Check(csG.Any(l => l.StartsWith("s31|9F-B1|3|False|False|10,10F-B1,", StringComparison.Ordinal)), "gate_details 9F-B1 위로 3개층 · 지지층 10F-B1 부터");
    }
    finally { Directory.Delete(gd, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "규칙 게이트 세부값 OK" : "규칙 게이트 세부값 FAIL");

// 35) F16 동바리 단위 현장 점검 (shore_inspect.py): 동바리 ID · 풀리지 않은 NG · 규칙 ⑦ (판정 '존치 (설치 결함 기록)'·타설 보류) · 근거 원장 1·타설 뒤 경고가 설치 검측 줄만 —
//     시연 자료 + 13F 에 일상 점검 NG → 설치 NG → 다시 OK → 13F 설치 NG 단계마다 Python 과 대조
failsBefore = fails;
Console.WriteLine("\n동바리 단위 현장 점검 (F16)");
if (python is not null)
{
    var fi = Directory.CreateTempSubdirectory("RevitOnSite_f16_").FullName;
    try
    {
        var dump = Path.Combine(fi, "dump.py");
        File.WriteAllText(dump, """
import os, sys
from datetime import datetime
sys.path.insert(0, os.getcwd())
import shore_inspect as si, evaluate as ev, pour_gate as pg, evidence, alerts
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(si.__file__)), '..', 'tools'))
import make_demo_data as md
base = sys.argv[1]
t = os.path.join(base, 'd')
md.make(t)
zs = ev.read(t, 'zones.csv')
with open(os.path.join(t, 'zones.csv'), 'a', encoding='utf-8') as f:
    f.write(''.join(f"13F-{z['zone_id'].split('-', 1)[1]},13,{z['x0']},{z['y0']},{z['x1']},{z['y1']}\n" for z in zs if z['floor'] == '12'))
old = open(os.path.join(t, si.FILE), encoding='utf-8').read().splitlines()
open(os.path.join(t, si.FILE), 'w', encoding='utf-8').write('\n'.join([old[0] + ',' + ','.join(si.EXTRA_COLS)] + [x + ',,,,' for x in old[1:]]) + '\n')
at = datetime(2026, 12, 31, 9)
steps = ['9F-B1,DLY-D,NG,박,2026-12-30,,일상 점검,9F-B1-R01C01,2026-12-30T08:00,현장 점검\n',
         '9F-B1,SHR-B,NG,김,2026-12-30,,설치 검측,9F-B1-R02C03;9F-B1-R02C04,2026-12-30T10:00,현장 점검\n',
         '9F-B1,SHR-B,OK,김,2026-12-30,,설치 검측,9F-B1-R02C03,2026-12-30T15:00,현장 점검\n',
         '13F-B1,SHR-S,NG,김,2026-12-30,,설치 검측,,2026-12-30T16:00,현장 점검\n',
         '9F-B1,SHR-B,OK,김,2026-12-31,,설치 검측,,2026-12-31T08:00,현장 점검\n']
out = ['ids|' + ','.join(si.shore_ids('9F-A2', [(0.6, 9.6), (1.8, 9.6), (0.6, 10.8), (1.80001, 10.79999), (3.0, 9.6), (0.65, 9.55)]))]
for i, line in enumerate([''] + steps):
    if line:
        open(os.path.join(t, si.FILE), 'a', encoding='utf-8').write(line)
    for tag, ng in (('all', si.open_ng(t, at)), ('ins', si.open_ng(t, at, si.INSTALL))):
        out.append(f'{i}|{tag}|' + ';'.join(f"{z}:" + ','.join(f'{c}/{s}/{x:%Y-%m-%dT%H:%M}' for c, s, x in v) + '=' + si.ng_text(v) for z, v in ng.items()))
    for r in ev.evaluate(t, at):
        if r['floor'] == 9:
            out.append(f"{i}|ev|{r['zone_id']}|{r['decision']}|{r['reason']}")
    out.append(f"{i}|sum|" + alerts.eval_summary(ev.evaluate(t, at)))
    g = pg.pour_gate(t, '13F-B1', at)
    out.append(f"{i}|pg|{g['decision']}|{g['reason']}")
    led = evidence.ledger_at(t, at.date())
    out.append(f"{i}|led|" + ','.join(f"{z}:{led[z]['dates'].get(1, '-')}" for z in sorted(led)))
    out.append(f"{i}|iap|" + ','.join(f'{z}={w}' for z, w in evidence.install_after_pour(t, at.date().isoformat()).items()))
    import shutil
    shutil.copy(os.path.join(t, si.FILE), os.path.join(base, f'ins_{i}.csv'))
print('\n'.join(out))
""", new UTF8Encoding(false));
        var pyF = RunPy(dump, fi).TrimEnd('\n').Replace("\r", "").Split('\n');
        var csF = new List<string> { "ids|" + string.Join(",", ShoreInspect.ShoreIds("9F-A2", new (double, double)[] { (0.6, 9.6), (1.8, 9.6), (0.6, 10.8), (1.80001, 10.79999), (3.0, 9.6), (0.65, 9.55) })) };
        var fd = Path.Combine(fi, "d");
        var fAt = new DateTime(2026, 12, 31, 9, 0, 0);
        for (int i = 0; i < 6; i++)
        {
            File.Copy(Path.Combine(fi, $"ins_{i}.csv"), Path.Combine(fd, ShoreInspect.File), true);
            foreach (var (tag, ng) in new[] { ("all", ShoreInspect.OpenNg(fd, fAt)), ("ins", ShoreInspect.OpenNg(fd, fAt, ShoreInspect.Install)) })
                csF.Add($"{i}|{tag}|" + string.Join(";", ng.Select(kv => kv.Key + ":" + string.Join(",", kv.Value.Select(x => $"{x.Item}/{x.Shore}/{x.T:yyyy-MM-dd'T'HH:mm}")) + "=" + ShoreInspect.NgText(kv.Value))));
            foreach (var r in Evaluator.Evaluate(fd, fAt).Where(r => r.Floor == 9)) csF.Add($"{i}|ev|{r.ZoneId}|{r.Decision}|{r.Reason}");
            csF.Add($"{i}|sum|" + Alerts.EvalSummary(Evaluator.Evaluate(fd, fAt)));   // 37차: '존치 n (설치 결함 m)
            var g = PourGate.Evaluate(fd, "13F-B1", fAt);
            csF.Add($"{i}|pg|{g.Decision}|{g.Reason}");
            var led = Evidence.LedgerAt(fd, DateOnly.FromDateTime(fAt));
            csF.Add($"{i}|led|" + string.Join(",", led.Keys.OrderBy(z => z, StringComparer.Ordinal).Select(z => $"{z}:{(led[z].Dates.TryGetValue(1, out var d1) ? d1 : "-")}")));
            csF.Add($"{i}|iap|" + string.Join(",", Evidence.InstallAfterPour(fd, DateOnly.FromDateTime(fAt)).Select(kv => $"{kv.Key}={kv.Value}")));
        }
        int badF = pyF.Zip(csF).Count(q => q.First != q.Second) + Math.Abs(pyF.Length - csF.Count);
        Console.WriteLine($"shore_inspect: 줄 {csF.Count} (Python {pyF.Length}) 다른 줄 {badF}");
        if (badF > 0) { var d0 = pyF.Zip(csF).FirstOrDefault(q => q.First != q.Second); Console.WriteLine($"    Python: {d0.First}\n    C#    : {d0.Second}"); }
        Check(badF == 0, "shore_inspect Python 대조 (ID · 풀리지 않은 NG · 판정 · 타설 전 확인 · 근거 원장 1 · 타설 뒤 경고, 6단계)");
        Check(csF.Contains("2|ev|9F-B1|존치 (설치 결함 기록)|설치 검측 결함 기록 미해결 — 수평연결재 9F-B1-R02C03 외 1") && csF.Any(l => l.StartsWith("5|ev|9F-B1|해체 승인 요청 가능|", StringComparison.Ordinal))
              && csF.Any(l => l.StartsWith("4|pg|타설 보류|13F-B1: 설치 검측 결함 기록 미해결 — 이음 (구역 전체)", StringComparison.Ordinal)), "F16 규칙 ⑦ (해체 요청 불가 → 다시 점검 OK 로 풀림, 13F 타설 보류)");
    }
    finally { Directory.Delete(fi, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "동바리 단위 현장 점검 (F16) OK" : "동바리 단위 현장 점검 (F16) FAIL");

// 37) v6 동바리 입력 (shore_input.py, 2단계): shore_input.py --self-test 와 같은 자료 → 3개층 확인 표 글 줄·사양 고르기·해체 기록(shore_removed.csv) 바이트를 Python 과 대조
failsBefore = fails;
Console.WriteLine("\n동바리 입력 (3개층 확인 표)");
if (python is not null)
{
    var si2 = Directory.CreateTempSubdirectory("RevitOnSite_shinput_").FullName;
    try
    {
        void W2(string d, string n, string s) => File.WriteAllText(Path.Combine(d, n), s, new UTF8Encoding(false));
        foreach (var d in new[] { Path.Combine(si2, "py"), Path.Combine(si2, "cs") })
        {
            Directory.CreateDirectory(d);
            W2(d, "zones.csv", "zone_id,floor,x0,y0,x1,y1\nB1-A1,-1,0,0,10,9\n1F-A1,1,0,0,10,9\n2F-A1,2,0,0,10,9\n3F-A1,3,0,0,10,9\n3F-B1,3,10,0,20,9\n4F-A1,4,0,0,10,9\n");
            W2(d, "pours.csv", "zone_id,pour_date,pour_time,mix,fck\nB1-A1,2026-10-01,08:00,보통,24\n1F-A1,2026-10-08,08:00,보통,24\n2F-A1,2026-10-15,08:00,보통,24\n3F-A1,2026-10-22,08:00,보통,24\n");
            W2(d, "shores.csv", "id,zone_id,x,y,field_label\n3F-A1-R01C01,3F-A1,1,1,\n3F-A1-R01C02,3F-A1,2,1,\n3F-B1-R01C01,3F-B1,11,1,\n1F-A1-R01C01,1F-A1,1,1,\n");
            W2(d, "shore_spec.csv", "floor,zone_id,type,spec,spacing_x_mm,spacing_y_mm,splices,install_date\n3,,파이프 서포트,V4,1200,1200,0,2026-10-20\n3,3F-B1,시스템 동바리,SY-1,1500,1500,0,2026-10-20\n");
        }
        var pyT = RunPy("-c", "import sys, shore_input as s\nd = sys.argv[1]\nprint('\\n'.join(s.table_text(s.table(d))))\n"
            + "s.write_removed(d, [{'id': '2F-A1-R01C01', 'zone_id': '2F-A1', 'x': 1.0, 'y': 1, 'removed_date': '2026-11-01', 'by': '박', 'note': '구역 해체, 쉼표 \"따옴\"'}])\n"
            + "print('\\n'.join(s.table_text(s.table(d))))\nprint(s.spec_of(d, 3, '3F-B1')['spec'], s.spec_of(d, 3, '3F-A1')['spec'], s.spec_of(d, 2))", Path.Combine(si2, "py")).TrimEnd('\n').Replace("\r", "");
        var cd2 = Path.Combine(si2, "cs");
        var csT = new List<string>(ShoreInput.TableText(ShoreInput.Table(cd2)));
        ShoreInput.WriteRemoved(cd2, new[] { new Dictionary<string, string> { ["id"] = "2F-A1-R01C01", ["zone_id"] = "2F-A1", ["x"] = Py.Repr(1.0), ["y"] = "1", ["removed_date"] = "2026-11-01", ["by"] = "박", ["note"] = "구역 해체, 쉼표 \"따옴\"" } });
        csT.AddRange(ShoreInput.TableText(ShoreInput.Table(cd2)));
        csT.Add($"{ShoreInput.SpecOf(cd2, 3, "3F-B1")!["spec"]} {ShoreInput.SpecOf(cd2, 3, "3F-A1")!["spec"]} {(ShoreInput.SpecOf(cd2, 2) is null ? "None" : "?")}");
        bool sameT = pyT == string.Join("\n", csT);
        bool sameR = File.ReadAllBytes(Path.Combine(si2, "py", ShoreInput.Removed)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(cd2, ShoreInput.Removed)));
        Console.WriteLine($"shore_input 표·사양 {csT.Count}줄: Python 과 같음 {(sameT ? "예" : "아니오")} · shore_removed.csv 바이트 동일 {(sameR ? "예" : "아니오")}");
        if (!sameT) Console.WriteLine($"    Python: {pyT.Replace("\n", " / ")}\n    C#    : {string.Join(" / ", csT)}");
        Check(sameT && sameR && csT[2].Contains("빨강") && csT.Count(l => l.Contains("빨강")) == 2, "동바리 입력 Python 대조 (3개층 표 2F 빨강·해체 뒤도 빨강, 사양 구역 우선)");
    }
    finally { Directory.Delete(si2, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "동바리 입력 OK" : "동바리 입력 FAIL");

// 38) v6 강도 예측 타임라인 (strength_timeline.py, 3단계): 시연 자료 그대로 · 층 높이 보정 켬 + 기상 + 가열 양생 + 기간 지정 · 타설 없음 → Python dump 와 바이트 대조
failsBefore = fails;
Console.WriteLine("\n강도 예측 타임라인 (3단계)");
if (python is not null)
{
    var stl = Directory.CreateTempSubdirectory("RevitOnSite_stl_").FullName;
    try
    {
        var sd = Path.Combine(FindRoot(), "shoring", "sample", "data");
        var hd = Path.Combine(stl, "h");
        Directory.CreateDirectory(hd);
        foreach (var n in new[] { "zones.csv", "pours.csv", "cylinders.csv", "weather.csv" })
            if (File.Exists(Path.Combine(sd, n))) File.Copy(Path.Combine(sd, n), Path.Combine(hd, n));
        File.WriteAllText(Path.Combine(hd, "options.json"), "{\"height_correct\": true, \"story_m\": 3.2, \"exposure_c\": 0.5}", new UTF8Encoding(false));
        var ed = Path.Combine(stl, "e");
        Directory.CreateDirectory(ed);
        File.Copy(Path.Combine(sd, "zones.csv"), Path.Combine(ed, "zones.csv"));
        File.WriteAllText(Path.Combine(ed, "pours.csv"), "zone_id,pour_date,pour_time,mix,fck\n", new UTF8Encoding(false));
        var cases = new (string Name, string[] PyArgs, Func<string> Cs)[]
        {
            ("시연 자료", new[] { sd }, () => StrengthTimeline.Dump(StrengthTimeline.Timeline(sd))),
            ("층 높이 보정·가열 양생·기간", new[] { hd, "--cure-min", "15", "--start", "2026-12-01", "--end", "2027-01-10" },
                () => StrengthTimeline.Dump(StrengthTimeline.Timeline(hd, new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 10), 15.0))),
            ("타설 없음", new[] { ed }, () => StrengthTimeline.Dump(StrengthTimeline.Timeline(ed))),
        };
        foreach (var (name, pa, stCs) in cases)
        {
            var stPy = RunPy(new[] { "strength_timeline.py" }.Concat(pa).ToArray());
            var c = stCs();
            bool same = stPy == c;
            Console.WriteLine($"{name}: {c.Count(ch => ch == '\n')}줄 Python 과 같음 {(same ? "예" : "아니오")}");
            if (!same)
            {
                var pl = stPy.Split('\n');
                var cl = c.Split('\n');
                int k = Enumerable.Range(0, Math.Min(pl.Length, cl.Length)).FirstOrDefault(i => pl[i] != cl[i]);
                Console.WriteLine($"    첫 차이 {k}줄\n    Python: {pl.ElementAtOrDefault(k)}\n    C#    : {cl.ElementAtOrDefault(k)}");
            }
            Check(same, $"강도 타임라인 {name}");
        }
        Check(StrengthTimeline.StepOf(null, 24) is null && StrengthTimeline.StepOf(9.5, 24) == 0 && StrengthTimeline.StepOf(23.9, 24) == 3 && StrengthTimeline.StepOf(24, 24) == 4, "강도 색 구간");
    }
    finally { Directory.Delete(stl, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "강도 예측 타임라인 OK" : "강도 예측 타임라인 FAIL");

// 39) v6 도면(CAD)에서 놓기 묶음 (Engine.CadShores — Revit 형상 도구, Python 기준본 없음): 종류·레이어·크기(10 mm 단위)로 묶기, 같은 자리 한 번, 동바리 같아 보이는 크기 [가정]
failsBefore = fails;
Console.WriteLine("\n도면(CAD)에서 놓기 묶음");
{
    double ft = 1 / 304.8;   // mm → 피트 (형상 좌표는 내부 단위)
    var marks = new List<CadMark>
    {
        new(CadShores.Circle, "S-SHORE", 60.3, 0, 0), new(CadShores.Circle, "S-SHORE", 60.3, 1200 * ft, 0), new(CadShores.Circle, "S-SHORE", 58.0, 2400 * ft, 0),
        new(CadShores.Circle, "S-SHORE", 60.3, 0.4 * ft, 0),                                      // 같은 자리 (1 mm 안) → 한 번
        new(CadShores.Block, "", 250, 0, 1200 * ft), new(CadShores.Block, "", 249, 1200 * ft, 1200 * ft),
        new(CadShores.Circle, "A-ANNO", 1500, 0, 5000 * ft),                                       // 그리드 기호 — 동바리 아님
    };
    var gs = CadShores.Groups(marks, ft);
    Console.WriteLine("  " + string.Join(" | ", gs.Select(g => g.Label + (g.LikelyShore ? " ✓" : ""))));
    Check(gs.Count == 3 && gs[0].Label == "원 · 레이어 S-SHORE · 지름 60 mm — 3개" && gs[0].LikelyShore
          && gs[1].Label == "블록 · 레이어 (없음) · 크기 250 mm — 2개" && gs[1].LikelyShore && !gs[2].LikelyShore && gs[2].SizeMm == 1500,
        "묶음 3개: 원 Φ60 3개(58 은 같은 묶음·같은 자리 한 번) · 블록 250 2개 · 그리드 원 1500 은 체크 안 함");
}
Console.WriteLine(fails == failsBefore ? "도면(CAD)에서 놓기 묶음 OK" : "도면(CAD)에서 놓기 묶음 FAIL");

// 36) 해체 순서 (35차 총괄): 기록 위반 경고 evidence.removal_order (v6: 계획 층 해체일 쪽은 공정 계산과 함께 뺌) — 시연 자료 + 해체 기록 3가지
failsBefore = fails;
Console.WriteLine("\n해체 순서 (35차)");
if (python is not null)
{
    var ro = Directory.CreateTempSubdirectory("RevitOnSite_order_").FullName;
    try
    {
        var dump = Path.Combine(ro, "dump.py");
        File.WriteAllText(dump, """
import os, sys
from datetime import datetime
sys.path.insert(0, os.getcwd())
import evidence, approval as ap, evaluate as ev
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(evidence.__file__)), '..', 'tools'))
import make_demo_data as md
t = os.path.join(sys.argv[1], 'd')
md.make(t)
row = {"zone_id": "x", "decision": "해체 승인 요청 가능"}
ap.record(t, "9F-A1", "해체 완료", "김", row, now=datetime(2026, 12, 30, 16))
ap.record(t, "10F-A1", "해체 완료", "김", row, now=datetime(2026, 12, 31, 9))
ap.record(t, "10F-B1", "해체 완료", "김", row, now=datetime(2026, 12, 31, 10))
with open(os.path.join(t, "shore_checks.csv"), "w", encoding="utf-8") as f:
    f.write("zone_id,timestamp,present,actor,photo,source\n11F-C2,2026-12-31T11:00,아니오,최반장,,협력업체 신고\n")
out = []
for d in ("2026-12-30", "2026-12-31"):
    out += [f"{d}|{z}|{w}" for z, w in evidence.removal_order(t, d).items()]
    L = evidence.ledger_at(t, d)
    out += [f"{d}|L|{z}|{' / '.join(L[z]['warnings'])}" for z in sorted(L)]
print('\n'.join(out))
""", new UTF8Encoding(false));
        var pyO = RunPy(dump, ro).TrimEnd('\n').Replace("\r", "").Split('\n');
        var fd = Path.Combine(ro, "d");
        var csO = new List<string>();
        foreach (var d in new[] { new DateOnly(2026, 12, 30), new DateOnly(2026, 12, 31) })
        {
            var ds = d.ToString("yyyy-MM-dd");
            csO.AddRange(Evidence.RemovalOrder(fd, d).Select(kv => $"{ds}|{kv.Key}|{kv.Value}"));
            var L = Evidence.LedgerAt(fd, d);
            csO.AddRange(L.Keys.OrderBy(z => z, StringComparer.Ordinal).Select(z => $"{ds}|L|{z}|{string.Join(" / ", L[z].Warnings)}"));
        }
        int badO = pyO.Zip(csO).Count(q => q.First != q.Second) + Math.Abs(pyO.Length - csO.Count);
        Console.WriteLine($"removal_order·근거 원장 경고: 줄 {csO.Count} (Python {pyO.Length}) 다른 줄 {badO}");
        if (badO > 0) { var d0 = pyO.Zip(csO).FirstOrDefault(q => q.First != q.Second); Console.WriteLine($"    Python: {d0.First}\n    C#    : {d0.Second}"); }
        Check(badO == 0, "해체 순서 경고 Python 대조 (removal_order · 근거 원장 경고, 2날)");
        Check(csO.Contains("2026-12-31|10F-B1|10F-B1: 위층 동바리가 아래층보다 먼저 해체 — 확인 필요 (12-31 해체, 아래층 9F-B1 해체 기록 없음)")
              && !csO.Any(l => l.StartsWith("2026-12-31|10F-A1|", StringComparison.Ordinal)) && csO.Any(l => l.StartsWith("2026-12-31|11F-C2|", StringComparison.Ordinal)),
            "위층 먼저 해체 경고: 10F-B1 (아래 9F-B1 기록 없음)·11F-C2 신고 — 10F-A1 은 아래 9F-A1 전날 해체라 없음");
    }
    finally { Directory.Delete(ro, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "해체 순서 (35차) OK" : "해체 순서 (35차) FAIL");

// v6.1 구역 정보 (shoring/zone_info.py ↔ Engine ZoneInfo.cs): 샘플·데모 자료·사양/점검/해체 기록이 든 작은 폴더, 날짜 자름 있음/없음 — 구역마다 Dump 글자 비교
failsBefore = fails;
Console.WriteLine("\n구역 정보 (v6.1)");
if (python is not null)
{
    var zi = Directory.CreateTempSubdirectory("RevitOnSite_zoneinfo_").FullName;
    var zdm = Directory.CreateTempSubdirectory("RevitOnSite_zoneinfo_demo_").FullName;
    try
    {
        void ZW(string n, string text) => File.WriteAllText(Path.Combine(zi, n), text, new UTF8Encoding(false));
        ZW("zones.csv", "zone_id,floor,x0,y0,x1,y1,slab_mm\n1F-A1,1,0.0,0.0,10.0,9.0,210\n1F-B1,1,10.0,0.0,20.0,9.0,\n2F-A1,2,0.0,0.0,10.0,9.0,210\n");
        ZW("pours.csv", "zone_id,pour_date,pour_time,mix,fck\n1F-A1,2026-11-01,08:00,보통,24\n");
        ZW("cylinders.csv", "zone_id,test_date,curing,f1,f2,f3\n1F-A1,2026-11-08,표준양생,20,21,22\n1F-A1,2026-11-05,현장봉함,15.0,15.5,16\n1F-B1,2026-11-05,현장봉함,9,9,9\n1F-B1,2026-11-06,현장수중,9,,9\n");
        ZW("sensors.csv", "sensor_id,floor,x,y,timestamp,temp_c\nS1,1,5,4,2026-11-01T10:00,20\nS1,1,5,4,2026-11-02T10:00,18.5\nS2,1,12.5,4,2026-11-01T10:00,19\nS3,1,15,4,2026-11-01T10:00,19\nS4,2,5,4,2026-11-01T10:00,19\n");
        ZW("sensor_strength.csv", "sensor_id,floor,x,y,timestamp,mpa\nM1,1,-2.5,4,2026-11-03T10:00,12.5\nM1,1,-2.5,4,2026-11-03T09:00,11\n");
        ZW("shores.csv", "id,zone_id,x,y,field_label\n1F-A1-R01C01,1F-A1,1.0,1.0,\n1F-A1-R01C02,1F-A1,2.2,1.0,\n");
        ZW(ShoreInput.Spec, "floor,zone_id,type,spec,spacing_x_mm,spacing_y_mm,splices,install_date\n1,,파이프 서포트,V2,1200,1200,1,2026-10-28\n1,1F-B1,시스템 동바리,S,1500,1500,0,2026-10-29\n");
        ZW(ShoreInspect.File, "zone_id,item_code,result,inspector,date,photo_path,stage,shore_ids,timestamp,source\n1F-A1,SHR-S,NG,김,2026-10-30,,설치 검측,,2026-10-30T09:00,현장 점검\n"
            + "1F-A1,SHR-B,NG,김,2026-10-30,,설치 검측,1F-A1-R01C01,2026-10-30T09:00,현장 점검\n1F-A1,SHR-S,OK,김,2026-11-03,,설치 검측,,2026-11-03T09:00,현장 점검\n");
        ZW(ShoreInput.Removed, "id,zone_id,x,y,removed_date,by,note\n1F-A1-R01C01,1F-A1,1.0,1.0,2026-11-20,김,구역 해체 (Revit)\n");
        RunPy(Path.Combine(FindRoot(), "tools", "make_demo_data.py"), zdm);
        foreach (var (name, dir, zAt) in new (string, string, DateOnly?)[] { ("샘플", data, null), ("데모", zdm, null), ("데모 12-31", zdm, new DateOnly(2026, 12, 31)),
                     ("작은 폴더", zi, null), ("작은 폴더 11-01", zi, new DateOnly(2026, 11, 1)) })
        {
            var zpy = RunPy(new[] { "zone_info.py", dir, "--all" }.Concat(zAt is DateOnly a ? new[] { "--at", a.ToString("yyyy-MM-dd", inv) } : Array.Empty<string>()).ToArray());
            var zcs = string.Concat(Evaluator.Read(dir, "zones.csv").Select(r => ZoneInfo.Dump(ZoneInfo.Of(dir, (r["zone_id"] ?? "").Trim(), zAt))));
            bool same = zpy == zcs;
            Console.WriteLine($"{name}: {zcs.Count(ch => ch == '\n')}줄 Python 과 같음 {(same ? "예" : "아니오")}");
            if (!same)
            {
                var pl = zpy.Split('\n');
                var cl = zcs.Split('\n');
                int k = Enumerable.Range(0, Math.Min(pl.Length, cl.Length)).FirstOrDefault(i => pl[i] != cl[i]);
                Console.WriteLine($"    첫 차이 {k}줄\n    Python: {pl.ElementAtOrDefault(k)}\n    C#    : {cl.ElementAtOrDefault(k)}");
            }
            Check(same, $"구역 정보 {name}");
        }
        var za = ZoneInfo.Of(zi, "1F-A1");
        Check(za.Spec?["source"] == "층 전체" && ZoneInfo.Of(zi, "1F-B1").Spec?["source"] == "구역" && za.Shores == 2 && za.OpenNg == "수평연결재 1F-A1-R01C01"
              && za.Sensors.Select(s => s.SensorId).SequenceEqual(new[] { "M1", "S1", "S2" }) && ZoneInfo.Of(zi, "1F-B1").Cylinders.Any(c => c.Avg is null),
            "구역 정보: 사양 구역 줄 우선 · 동바리 수 · 풀린 NG · 3 m 안 센서 · 빈 강도 평균 없음");
        bool threw = false;
        try { ZoneInfo.Of(zi, "9F-Z9"); } catch (ArgumentException) { threw = true; }
        Check(threw, "구역 정보: 없는 구역은 오류");
    }
    finally { Directory.Delete(zi, true); Directory.Delete(zdm, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "구역 정보 (v6.1) OK" : "구역 정보 (v6.1) FAIL");

// 11-12 2-1 기록 취소 (evaluate.void_row ↔ Evaluator.VoidRow): 시연 폴더 사본 두 개에 같은 취소 → pours·cylinders 바이트 같음, 판정표 Python 바이트 같음, 요청 기록 구역 타설은 거부
failsBefore = fails;
Console.WriteLine("\n기록 취소 (2-1)");
if (python is not null)
{
    string vPy = Directory.CreateTempSubdirectory("RevitOnSite_void_py_").FullName, vCs = Directory.CreateTempSubdirectory("RevitOnSite_void_cs_").FullName;
    try
    {
        var demo = Path.Combine(FindRoot(), "revit", "onsite_data_demo");
        foreach (var d in new[] { vPy, vCs }) foreach (var f in Directory.GetFiles(demo, "*.csv").Concat(Directory.GetFiles(demo, "*.json"))) File.Copy(f, Path.Combine(d, Path.GetFileName(f)));
        var appr = Evaluator.Read(vCs, "approvals.csv").Select(r => r["zone_id"]).ToHashSet();
        var pz = Evaluator.Read(vCs, "pours.csv").Last(r => !appr.Contains(r["zone_id"]))["zone_id"]!;
        var cz = Evaluator.Read(vCs, "cylinders.csv").Last(r => (r.GetValueOrDefault("zone_id") ?? "").Length > 0)["zone_id"]!;
        var vNow = new DateTime(2026, 12, 31, 10, 0, 0);
        RunPy("-c", "import sys, evaluate as ev\nfrom datetime import datetime\nf, pz, cz = sys.argv[1:4]\nnow = datetime(2026, 12, 31, 10)\n"
            + "ev.void_row(f, 'pours.csv', [r for r in ev.read(f, 'pours.csv') if r['zone_id'] == pz][-1], '타설일 오타', '김현장', now)\n"
            + "ev.void_row(f, 'cylinders.csv', [r for r in ev.read(f, 'cylinders.csv') if r['zone_id'] == cz][-1], '강도 오타', '김현장', now)\n", vPy, pz, cz);
        Evaluator.VoidRow(vCs, "pours.csv", Evaluator.Read(vCs, "pours.csv").Last(r => r["zone_id"] == pz), "타설일 오타", "김현장", vNow);
        Evaluator.VoidRow(vCs, "cylinders.csv", Evaluator.Read(vCs, "cylinders.csv").Last(r => r["zone_id"] == cz), "강도 오타", "김현장", vNow);
        bool sameCsv = new[] { "pours.csv", "cylinders.csv" }.All(n => File.ReadAllBytes(Path.Combine(vPy, n)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(vCs, n))));
        var vAt = new DateTime(2026, 12, 31, 9, 0, 0);
        var vOutPy = Path.Combine(vCs, "_py.csv"); var vOutCs = Path.Combine(vCs, "_cs.csv");
        RunPy("evaluate.py", vCs, "--at", "2026-12-31T09:00", "--out", vOutPy);
        var vRows = Evaluator.Evaluate(vCs, vAt);
        Evaluator.Write(vRows, vOutCs);
        bool sameEval = File.ReadAllBytes(vOutPy).AsSpan().SequenceEqual(File.ReadAllBytes(vOutCs));
        Console.WriteLine($"취소 줄 Python 과 바이트 동일 {(sameCsv ? "예" : "아니오")} · 판정표 {(sameEval ? "예" : "아니오")} (타설 {pz}, 공시체 {cz})");
        Check(sameCsv && sameEval && !Evaluator.Read(vCs, "pours.csv").Any(r => r["zone_id"] == pz) && vRows.All(r => r.ZoneId != pz), "void 1 취소 줄·판정표 Python 바이트, 취소한 타설 구역은 판정에서 빠짐");
        bool Throws(Action a) { try { a(); return false; } catch (ArgumentException) { return true; } }
        Check(Throws(() => Evaluator.VoidRow(vCs, "pours.csv", Evaluator.Read(vCs, "pours.csv").First(r => appr.Contains(r["zone_id"])), "시험", "김현장", vNow))
              && Throws(() => Evaluator.VoidRow(vCs, "cylinders.csv", new Dictionary<string, string?> { ["zone_id"] = "없음" }, "시험", "김현장", vNow)),
            "void 2 요청·승인 기록 구역 타설 취소 거부 · 없는 줄 거부");
        // 11-12 기본 사항·출처 (provenance.py ↔ Provenance.cs): 시연 사본(입력자·기기 열 있음) + 시공사·감리단·역할 → 조기 탈형 검토서 HTML Python 바이트
        File.WriteAllText(Path.Combine(vPy, Evaluator.OptionsFile), "{\"contractor\": \" 한빛건설 \", \"supervisor_firm\": \"정도감리\", \"actor_roles\": {\"공무 담당\": \"공무\", \"품질관리자\": \"품질\", \"x\": \" \"}}", new UTF8Encoding(false));
        var pvPy = Path.Combine(vCs, "_pv_py.html");
        RunPy("-c", "import sys, staged\nfrom datetime import datetime\nopen(sys.argv[2], 'w', encoding='utf-8', newline='').write(staged.review_html(sys.argv[1], datetime(2026, 12, 31, 9), '9F-B1', project='시험'))\n", vPy, pvPy);
        var pvCs = Staged.ReviewHtml(vPy, vAt, "9F-B1", project: "시험");
        var pvPyText = File.ReadAllText(pvPy, Encoding.UTF8);
        if (pvPyText != pvCs) { int k = Enumerable.Range(0, Math.Min(pvPyText.Length, pvCs.Length)).FirstOrDefault(i => pvPyText[i] != pvCs[i]); Console.WriteLine($"  첫 차이 {k}\n  Python: {pvPyText.Substring(Math.Max(0, k - 60), Math.Min(160, pvPyText.Length - Math.Max(0, k - 60)))}\n  C#    : {pvCs.Substring(Math.Max(0, k - 60), Math.Min(160, pvCs.Length - Math.Max(0, k - 60)))}"); }
        Check(File.ReadAllText(pvPy, Encoding.UTF8) == pvCs && pvCs.Contains("<table class=\"prov\">") && pvCs.Contains("시공사 한빛건설 · 감리단 정도감리") && pvCs.Contains("공무 담당 (공무)")
              && Evaluator.Roles(vPy).Count == 2, "provenance 검토서 '기본 사항·출처' Python 바이트 · 시공사·감리단 머리 · 역할");
    }
    finally { Directory.Delete(vPy, true); Directory.Delete(vCs, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "기록 취소 OK" : "기록 취소 FAIL");

// 2-6 (11-12 G9·G10): 침하 계측·해체 결과보고 — make_demo_data --sim 에 침하 줄을 더해 Python result_report.make 와 C# ResultReport.Make 의 HTML 바이트·원장 8 줄 대조
failsBefore = fails;
Console.WriteLine("\n해체 결과보고·침하 계측");
if (python is not null)
{
    var rp = Directory.CreateTempSubdirectory("RevitOnSite_rr_py_").FullName;
    var rc = Directory.CreateTempSubdirectory("RevitOnSite_rr_cs_").FullName;
    try
    {
        RunPy(Path.Combine(FindRoot(), "tools", "make_demo_data.py"), rp, "--sim");
        File.AppendAllText(Path.Combine(rp, AfterCheck.File), "1F-A1,2026-11-20,SET-P1,1.2,박안전,\n1F-A1,2026-11-20,SET-P2,0.4,박안전,\n1F-A1,2026-11-22,SET-P1,1.5,박안전,\n", new UTF8Encoding(false));
        foreach (var f in Directory.EnumerateFiles(rp)) File.Copy(f, Path.Combine(rc, Path.GetFileName(f)));
        var rAt = new DateTime(2026, 11, 24, 9, 0, 0);
        var pyR = RunPy("-c", "import sys, after_check as ac, result_report as rr\nfrom datetime import datetime\nt = sys.argv[1]\nat = datetime(2026, 11, 24, 9)\n"
            + "for z, s in ac.settlements(t, '2026-11-24').items(): print(z, ','.join(s['points']), ';'.join(d + ':' + ','.join(p + '=' + v for p, v in m.items()) for d, m in s['days'].items()), ac.same_points(s)[0], sep='|')\n"
            + "try: rr.make(t, '9F-A1', at, '시험')\nexcept ValueError as x: print(x)\n"
            + "print(rr.make(t, '1F-A1', at, '시험', '시험'))", rp).TrimEnd('\n').Replace("\r", "").Split('\n');
        var csSet = AfterCheck.Settlements(rc, "2026-11-24").Select(kv => $"{kv.Key}|{string.Join(",", kv.Value.Points)}|{string.Join(";", kv.Value.Days.Select(d => d.Key + ":" + string.Join(",", d.Value.Select(p => p.Key + "=" + p.Value))))}|{(AfterCheck.SamePoints(kv.Value.Days).Ok ? "True" : "False")}");
        string why9 = ResultReport.WhyNot(rc, "9F-A1", rAt) ?? "";
        var csPath = ResultReport.Make(rc, "1F-A1", rAt, "시험", "시험");
        var pyPath = pyR[^1];
        bool sameHtml = File.ReadAllBytes(pyPath).AsSpan().SequenceEqual(File.ReadAllBytes(csPath)) && Path.GetFileName(pyPath) == Path.GetFileName(csPath);
        string Last8(string d) { var r = Evaluator.Read(d, Evidence.Ledger).Last(); return string.Join("|", new[] { "ev_id", "zone_id", "date", "stage", "doc_no", "result", "by", "sha256" }.Select(k => r.GetValueOrDefault(k))); }
        bool sameLed = Last8(rp) == Last8(rc) && Evaluator.Read(rc, Evidence.Ledger).Last()["sha256"] == Evidence.FileSha(csPath);
        bool sameSet = string.Join("\n", pyR[..^2]) == string.Join("\n", csSet);
        Console.WriteLine($"침하 계측 Python 과 같음 {(sameSet ? "예" : "아니오")} · 결과보고 HTML 바이트 {(sameHtml ? "예" : "아니오")} · 원장 8 줄 {(sameLed ? "예" : "아니오")} · 거절 문구 {(pyR[^2] == why9 ? "예" : "아니오")}");
        if (!sameSet) Console.WriteLine($"    Python: {string.Join(" / ", pyR[..^2])}\n    C#    : {string.Join(" / ", csSet)}");
        Check(sameSet && sameHtml && sameLed && pyR[^2] == why9 && File.ReadAllText(csPath).Contains("측정 위치 일치: <b>아니오</b>"), "result_report 침하·HTML·원장 8·거절 문구 Python 대조");
        Check(Evidence.LedgerAt(rc, new DateOnly(2026, 11, 24))["1F-A1"].Dates.ContainsKey(8) && ResultReport.Make(rc, "1F-A1", rAt, "시험", "시험").EndsWith("_2.html", StringComparison.Ordinal),
            "result_report 원장 8 생김 · 같은 날 다시 = _2 (원장 SHA 가 가리키는 파일 안 덮음)");
    }
    finally { Directory.Delete(rp, true); Directory.Delete(rc, true); }
}
else Console.WriteLine("Python 없음 — 건너뜀");
Console.WriteLine(fails == failsBefore ? "해체 결과보고 OK" : "해체 결과보고 FAIL");

Console.WriteLine(fails == 0 ? "\n일치 시험 통과" : $"\n일치 시험 실패 {fails}건");
// 시험 잔여물 (사용자 지시: 체크용 파일을 쌓아 두지 않음): 통과하면 이번 실행이 %TEMP% 에 쓴 RevitOnSite_* 파일을 지움, 실패하면 비교용으로 남김
if (fails == 0)
    foreach (var f in Directory.EnumerateFiles(Path.GetTempPath(), "RevitOnSite_*"))
        if (File.GetLastWriteTimeUtc(f) >= runStart && Path.GetFileName(f) != "RevitOnSite_dashboard.log") try { File.Delete(f); } catch (IOException) { }   // 켜져 있는 Revit 의 대시보드 기록은 시험 파일이 아님
return fails == 0 ? 0 : 1;

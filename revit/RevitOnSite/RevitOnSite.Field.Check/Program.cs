// 현장 입력 서버·QR 시험 (Revit 없이). 실행: dotnet run --project revit\RevitOnSite\RevitOnSite.Field.Check [-- --qr-html <파일>]
//   1) QR 리드-솔로몬: 알려진 값 ("HELLO WORLD" 1-M, thonky.com QR 자습서)
//   2) QR 모듈 행렬 → --qr-html 에 jsQR(독립 디코더) 확인 쪽을 씀 → 브라우저로 열면 "모두 일치" (사람·에이전트가 한 번 확인)
//   3) 서버: 같은 요청 48개를 Python 기준 서버(tools/field_server.py)와 C# 서버에 보내 상태 코드·JSON·CSV 바이트 대조 + Saved·재시작 중복
//   4) 저장한 CSV(열 늘어남)를 Python evaluate.py 가 C# 과 똑같이 읽는지 바이트 대조 (.venv 없으면 건너뜀)
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using RevitOnSite.Engine;

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "  통과 " : "  실패 ") + what); if (!ok) fails++; }
string? Arg(string n) { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
string FindRoot()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        if (Directory.Exists(Path.Combine(d.FullName, "shoring", "sample", "data_strength"))) return d.FullName;
    throw new DirectoryNotFoundException(@"shoring\sample\data_strength 를 찾지 못함");
}
if (Array.IndexOf(args, "--culture") is var ci && ci >= 0 && ci + 1 < args.Length)   // 문화권 점검 (오류 점검 ②): --culture de-DE
    CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(args[ci + 1]);
var root = FindRoot();

// ── 1. 리드-솔로몬
Console.WriteLine("1) QR 리드-솔로몬");
var hello = new byte[] { 32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236, 17, 236, 17 };
var ecc = Qr.RsRemainder(hello, Qr.RsDivisor(10));
Check(ecc.SequenceEqual(new byte[] { 196, 35, 39, 119, 235, 215, 231, 226, 93, 23 }), "HELLO WORLD 1-M 오류 정정 10바이트 = 알려진 값");

// ── 2. QR 행렬 → jsQR 확인 쪽
Console.WriteLine("2) QR 행렬");
var samples = new[]
{
    "A",
    "http://192.168.0.12:8765/?t=abcdefghjk23",
    "http://10.0.0.5:8765/?t=" + new string('x', 90),                 // 버전 7 이상 (버전 정보 영역)
    "현장 입력 서버 시험 — 공시체 24.4 MPa",                            // UTF-8 한글
    new string('9', 213),                                               // 버전 10 최대
};
var mats = samples.Select(Qr.Encode).ToList();
for (int i = 0; i < samples.Length; i++)
    Console.WriteLine($"  {Encoding.UTF8.GetByteCount(samples[i]),3}바이트 → {mats[i].GetLength(0)}×{mats[i].GetLength(0)} (버전 {(mats[i].GetLength(0) - 17) / 4})");
Check(mats[^1].GetLength(0) == 57, "213바이트 = 버전 10 (57×57)");
try { Qr.Encode(new string('9', 214)); Check(false, "214바이트는 거절"); } catch (ArgumentException) { Check(true, "214바이트는 거절"); }
var qrHtml = Arg("--qr-html") ?? Path.Combine(Path.GetTempPath(), "RevitOnSite_qr_check.html");
{
    var sb = new StringBuilder();
    sb.Append("<!doctype html><meta charset=utf-8><title>QR 확인</title><body style=font-family:sans-serif><pre id=out>읽는 중…</pre>");
    sb.Append("<script src=\"https://cdn.jsdelivr.net/npm/jsqr@1.4.0/dist/jsQR.js\"></script><script>\n");
    sb.Append("const S=").Append(JsonValue.Create(samples)!.ToJsonString()).Append(";\nconst M=");
    sb.Append(new JsonArray(mats.Select(m => (JsonNode)JsonValue.Create(string.Concat(Enumerable.Range(0, m.Length).Select(k => m[k / m.GetLength(0), k % m.GetLength(0)] ? '1' : '0')))!).ToArray()).ToJsonString());
    sb.Append(";\n").Append("""
const out=[];let ok=0;
M.forEach((bits,i)=>{const n=Math.sqrt(bits.length),q=4,sc=6,W=(n+2*q)*sc;const c=document.createElement('canvas');c.width=c.height=W;
const g=c.getContext('2d');g.fillStyle='#fff';g.fillRect(0,0,W,W);g.fillStyle='#000';
for(let y=0;y<n;y++)for(let x=0;x<n;x++)if(bits[y*n+x]==='1')g.fillRect((x+q)*sc,(y+q)*sc,sc,sc);
document.body.appendChild(c);const d=g.getImageData(0,0,W,W);const r=jsQR(d.data,W,W);
const same=r&&r.data===S[i];if(same)ok++;out.push((same?'일치':'불일치')+' #'+i+' v'+((n-17)/4)+' → '+(r?JSON.stringify(r.data).slice(0,60):'못 읽음'));});
document.getElementById('out').textContent=(ok===M.length?'모두 일치 ':'불일치 있음 ')+ok+'/'+M.length+'\n'+out.join('\n');document.title='QR '+ok+'/'+M.length;
</script>
""");
    File.WriteAllText(qrHtml, sb.ToString(), new UTF8Encoding(false));
    Console.WriteLine($"  jsQR 확인 쪽: {qrHtml} (브라우저로 열어 '모두 일치 {samples.Length}/{samples.Length}' 확인)");
}

// ── 3. 서버 — 같은 요청을 Python 기준 서버(tools/field_server.py)와 C# 서버에 보내 응답·CSV 대조
Console.WriteLine("3) 현장 입력 서버: Python 기준본과 대조 (data_strength 사본 두 벌)");
var src = Path.Combine(root, "shoring", "sample", "data_strength");
string Copy(string tag)
{
    var dir = Path.Combine(Path.GetTempPath(), "RevitOnSite_field_" + tag, "data");   // 끝 폴더 이름이 같아야 model.project 가 같음
    if (Directory.Exists(dir)) Directory.Delete(dir, true);
    Directory.CreateDirectory(dir);
    foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
    return dir;
}
string pyDir = Copy("py"), csDir = Copy("cs");
var at = new DateTime(2026, 12, 31, 9, 0, 0);
var python = Path.Combine(root, ".venv", "Scripts", "python.exe");
var pyServerFile = Path.Combine(root, "tools", "field_server.py");
Process? pyProc = null;
HttpClient? pyHttp = null;
string pyToken = "";
if (File.Exists(python) && File.Exists(pyServerFile))
{
    var psi = new ProcessStartInfo(python) { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, WorkingDirectory = root };
    foreach (var x in new[] { "-u", pyServerFile, "--data", pyDir, "--at", "2026-12-31T09:00", "--host", "127.0.0.1", "--port", "0" }) psi.ArgumentList.Add(x);
    psi.Environment["PYTHONIOENCODING"] = "utf-8";
    pyProc = Process.Start(psi)!;
    pyProc.ErrorDataReceived += (_, _) => { };
    pyProc.BeginErrorReadLine();
    var started = DateTime.Now;
    while (DateTime.Now - started < TimeSpan.FromSeconds(30) && pyProc.StandardOutput.ReadLine() is { } line)
    {
        var mm = System.Text.RegularExpressions.Regex.Match(line, @"http://127\.0\.0\.1:(\d+)/\?t=(\S+)");
        if (!mm.Success) continue;
        pyHttp = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{mm.Groups[1].Value}"), Timeout = TimeSpan.FromSeconds(120) };
        pyToken = mm.Groups[2].Value;
        break;
    }
}
Check(pyHttp != null, "Python 기준 서버 켜짐" + (pyHttp == null ? " — .venv 또는 tools/field_server.py 없음, 대조 없이 C# 만 시험" : ""));

using var server = new FieldServer(csDir) { FixedNow = () => at, PagePath = Path.Combine(root, "web", "field", "index.html") };
var saved = new List<FieldEntry>();
server.Saved += e => { lock (saved) saved.Add(e); };
server.Start(0, System.Net.IPAddress.Loopback);
var csHttp = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}"), Timeout = TimeSpan.FromSeconds(120) };
const string UA = "Mozilla/5.0 (Linux; Android 14; SM-S918N) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36";
Console.WriteLine($"  C# 포트 {server.Port}, LAN 주소 예: {FieldServer.LanAddresses().FirstOrDefault()?.ToString() ?? "(없음)"}");

async Task<(int Code, string Body)> Send(HttpClient h, string token, string method, string path, string? body, bool auth)
{
    var url = path + (auth ? (path.Contains('?') ? "&" : "?") + "t=" + token : "");
    using var req = new HttpRequestMessage(new HttpMethod(method), url);
    req.Headers.UserAgent.ParseAdd(UA);
    if (body != null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
    using var r = await h.SendAsync(req);
    return ((int)r.StatusCode, await r.Content.ReadAsStringAsync());
}

// JSON 비교: 키 집합·배열 길이·값 (숫자는 값으로 — Python 24.0 과 C# 24 는 같음). entered_at 은 받은 시각이라 건너뜀
void Diff(JsonNode? a, JsonNode? b, string path, List<string> d)
{
    if (d.Count > 5) return;
    if (a is null || b is null) { if (a is not null || b is not null) d.Add($"{path}: {a?.ToJsonString() ?? "null"} ≠ {b?.ToJsonString() ?? "null"}"); return; }
    if (a is JsonObject oa && b is JsonObject ob)
    {
        foreach (var k in oa.Select(p => p.Key).Union(ob.Select(p => p.Key)))
        {
            if (k is "entered_at" or "project_id") continue;   // project_id = 데이터 폴더 경로 해시 (두 서버 폴더가 다름) → 아래에서 따로
            if (!oa.ContainsKey(k) || !ob.ContainsKey(k)) { d.Add($"{path}.{k}: 한쪽에만 있음 (Python {oa.ContainsKey(k)}, C# {ob.ContainsKey(k)})"); continue; }
            Diff(oa[k], ob[k], path + "." + k, d);
        }
        return;
    }
    if (a is JsonArray aa && b is JsonArray ab)
    {
        if (aa.Count != ab.Count) { d.Add($"{path}: 길이 {aa.Count} ≠ {ab.Count}"); return; }
        for (int i = 0; i < aa.Count; i++) Diff(aa[i], ab[i], $"{path}[{i}]", d);
        return;
    }
    var (ka, kb) = (a.GetValueKind(), b.GetValueKind());
    bool eq = ka == System.Text.Json.JsonValueKind.Number && kb == ka ? a.GetValue<double>() == b.GetValue<double>() : a.ToJsonString() == b.ToJsonString();
    if (!eq) d.Add($"{path}: {a.ToJsonString()} ≠ {b.ToJsonString()}");
}

string J(object o) => System.Text.Json.JsonSerializer.Serialize(o);
var w1 = File.ReadAllLines(Path.Combine(src, "sensor_strength.csv"))[1].Split(',');   // W1,9,25.0,4.5,2026-11-26T14:00,0.0
const string M = "/api/measure";
var cases = new List<(string Label, string Method, string Path, string? Body, bool Auth)>
{
    ("토큰 없음", "GET", "/api/model", null, false),
    ("토큰 틀림", "GET", "/api/model?t=wrong", null, false),
    ("favicon", "GET", "/favicon.ico", null, false),
    ("입력 화면", "GET", "/", null, true),
    ("모델", "GET", "/api/model", null, true),
    ("없는 주소", "GET", "/nope", null, true),
    ("POST 다른 주소", "POST", "/api/other", "{}", true),
    ("본문 없음", "POST", M, "", true),
    ("JSON 아님", "POST", M, "{", true),
    ("JSON 배열", "POST", M, "[]", true),
    ("JSON null", "POST", M, "null", true),
    ("다른 프로젝트", "POST", M, J(new { project_id = "0123456789", kind = "cylinder", client_id = "c0" }), true),
    ("project_id 빈 값", "POST", M, J(new { project_id = "", kind = "x", client_id = "c0" }), true),
    ("종류 없음", "POST", M, J(new { kind = "x", client_id = "c0" }), true),
    ("client_id 없음", "POST", M, J(new { kind = "cylinder" }), true),
    ("client_id 형식", "POST", M, J(new { kind = "cylinder", client_id = "a b" }), true),
    ("입력자 없음", "POST", M, J(new { kind = "cylinder", client_id = "c1", entered_by = "   " }), true),
    ("입력자 21자", "POST", M, J(new { kind = "cylinder", client_id = "c1", entered_by = new string('가', 21) }), true),
    ("없는 구역", "POST", M, J(new { kind = "cylinder", client_id = "c1", entered_by = "김", zone_id = "9F-Z9" }), true),
    ("날짜 형식", "POST", M, J(new { kind = "cylinder", client_id = "c1", entered_by = "김", zone_id = "9F-A2", test_date = "2026-13-01" }), true),
    ("타설 전 시험일", "POST", M, J(new { kind = "cylinder", client_id = "c1", entered_by = "김", zone_id = "9F-A2", test_date = "2026-11-01" }), true),
    ("미래 시험일", "POST", M, J(new { kind = "cylinder", client_id = "c1", entered_by = "김", zone_id = "9F-A2", test_date = "2027-01-01" }), true),
    ("양생 방법", "POST", M, J(new { kind = "cylinder", client_id = "c1", entered_by = "김", zone_id = "9F-A2", test_date = "2026/12/30", curing = "표준" }), true),
    ("강도 숫자 아님", "POST", M, J(new { kind = "cylinder", client_id = "c1", entered_by = "김", zone_id = "9F-A2", test_date = "2026-12-30", curing = "현장봉함", f1 = 30, f2 = "abc", f3 = 31 }), true),
    ("강도 빈칸", "POST", M, J(new { kind = "cylinder", client_id = "c1", entered_by = "김", zone_id = "9F-A2", test_date = "2026-12-30", curing = "현장봉함", f1 = 30, f2 = " ", f3 = 31 }), true),
    ("강도 0 (초과 조건)", "POST", M, J(new { kind = "cylinder", client_id = "c1", entered_by = "김", zone_id = "9F-A2", test_date = "2026-12-30", curing = "현장봉함", f1 = 0, f2 = 31, f3 = 31 }), true),
    ("강도 kgf 값", "POST", M, J(new { kind = "cylinder", client_id = "c1", entered_by = "김", zone_id = "9F-A2", test_date = "2026-12-30", curing = "현장봉함", f1 = 300, f2 = 31, f3 = 31 }), true),
    ("공시체 저장", "POST", M, J(new { kind = "cylinder", client_id = "dev1-1", entered_by = "  김   시험 ", note = "=메모, 쉼표 \"따옴표\"", zone_id = "9F-A2", test_date = "2026-12-30", curing = "현장봉함", f1 = 30.5, f2 = "31,0", f3 = 32 }), true),
    ("같은 공시체 다른 client_id", "POST", M, J(new { kind = "cylinder", client_id = "dev1-1b", entered_by = "김", zone_id = "9F-A2", test_date = "2026-12-30", curing = "현장봉함", f1 = 30.5, f2 = 31, f3 = "32.0" }), true),   // 61차 ⑤
    // F2 채취 정보 (선택 열): 타설 전 채취 시각은 거부, 값이 있으면 머리에 6열 더함 (cylinders.csv 바이트 대조)
    ("채취 시각 타설 전", "POST", M, J(new { kind = "cylinder", client_id = "dev1-s0", entered_by = "김", zone_id = "9F-B2", test_date = "2026-12-30", curing = "현장봉함", f1 = 25, f2 = 25, f3 = 25, sampled_at = "2026-11-20 08:00" }), true),
    ("채취 정보 저장", "POST", M, J(new { kind = "cylinder", client_id = "dev1-s1", entered_by = "김", zone_id = "9F-B2", test_date = "2026-12-30", curing = "현장봉함", f1 = 25, f2 = 25.2, f3 = 25.4,
        pour_id = "P-9F-2", sampled_at = "2026-11-26 08:40", supplier = "=한빛레미콘", truck_no = "12가3456", storage_zone = "9F-B2, 수조", photo = "cyl.jpg" }), true),
    ("같은 client_id", "POST", M, J(new { kind = "cylinder", client_id = "dev1-1", entered_by = "김", zone_id = "9F-A2", test_date = "2026-12-30", curing = "현장봉함", f1 = 1, f2 = 1, f3 = 1 }), true),
    ("반발경도 기본 위치", "POST", M, J(new { kind = "rebound", client_id = "dev1-2", entered_by = "박", zone_id = "9F-B1", test_date = "2026-12-29", r_value = 38 }), true),
    ("반발경도 구역 밖", "POST", M, J(new { kind = "rebound", client_id = "dev1-3", entered_by = "박", zone_id = "9F-B1", test_date = "2026-12-29", r_value = 38, x = 999 }), true),
    ("반발경도 범위", "POST", M, J(new { kind = "rebound", client_id = "dev1-3", entered_by = "박", zone_id = "9F-B1", test_date = "2026-12-29", r_value = 80 }), true),
    ("강도 센서 여러 줄 (겹침 1)", "POST", M, J(new { kind = "sensor_strength", client_id = "dev1-4", entered_by = "이", sensor_id = w1[0], floor = 9,
        readings = new object[] { new { timestamp = w1[4], mpa = 0 }, new { timestamp = "2026-12-30T10:00", mpa = 25.1 }, new { timestamp = "2026-12-30 16:00", mpa = "25,6" } } }), true),
    ("강도 센서 한 줄 이미 있음", "POST", M, J(new { kind = "sensor_strength", client_id = "dev1-5", entered_by = "이", sensor_id = w1[0], floor = 9, timestamp = w1[4], mpa = 1 }), true),
    ("강도 센서 자리 다름", "POST", M, J(new { kind = "sensor_strength", client_id = "dev1-5", entered_by = "이", sensor_id = w1[0], floor = 9, x = 1, timestamp = "2026-12-30T11:00", mpa = 25 }), true),
    ("강도 센서 층 다름", "POST", M, J(new { kind = "sensor_strength", client_id = "dev1-5", entered_by = "이", sensor_id = w1[0], floor = 10, timestamp = "2026-12-30T11:00", mpa = 25 }), true),
    ("다른 종류 ID", "POST", M, J(new { kind = "sensor_temp", client_id = "dev1-5", entered_by = "이", sensor_id = w1[0], floor = 9, timestamp = "2026-12-30T11:00", temp_c = 5 }), true),
    ("센서 ID 형식", "POST", M, J(new { kind = "sensor_temp", client_id = "dev1-5", entered_by = "이", sensor_id = "S 1", floor = 9 }), true),
    ("층 9.5", "POST", M, J(new { kind = "sensor_temp", client_id = "dev1-5", entered_by = "이", sensor_id = "T1", floor = 9.5 }), true),
    ("없는 층", "POST", M, J(new { kind = "sensor_temp", client_id = "dev1-5", entered_by = "이", sensor_id = "T1", floor = 99 }), true),
    ("새 센서 구역 밖", "POST", M, J(new { kind = "sensor_temp", client_id = "dev1-5", entered_by = "이", sensor_id = "T-NEW", floor = 9, x = 500, y = 1, timestamp = "2026-12-30T10:00", temp_c = 12 }), true),
    ("온도 범위", "POST", M, J(new { kind = "sensor_temp", client_id = "dev1-5", entered_by = "이", sensor_id = "T-NEW", floor = 9, x = 5, y = 4, timestamp = "2026-12-30T10:00", temp_c = -45 }), true),
    ("둘째 줄 시각 틀림", "POST", M, J(new { kind = "sensor_temp", client_id = "dev1-5", entered_by = "이", sensor_id = "T-NEW", floor = 9, x = 5, y = 4,
        readings = new object[] { new { timestamp = "2026-12-30T10:00", temp_c = 12 }, new { timestamp = "12/30 10:00", temp_c = 12 } } }), true),
    ("타설 전 시각", "POST", M, J(new { kind = "sensor_temp", client_id = "dev1-5", entered_by = "이", sensor_id = "T-NEW", floor = 9, x = 5, y = 4, timestamp = "2026-11-01T10:00", temp_c = 12 }), true),
    ("미래 시각", "POST", M, J(new { kind = "sensor_temp", client_id = "dev1-5", entered_by = "이", sensor_id = "T-NEW", floor = 9, x = 5, y = 4, timestamp = "2027-01-01T00:00", temp_c = 12 }), true),
    ("빈 기록 목록", "POST", M, J(new { kind = "sensor_temp", client_id = "dev1-5", entered_by = "이", sensor_id = "T-NEW", floor = 9, x = 5, y = 4, readings = Array.Empty<object>() }), true),
    ("새 온도 센서 저장", "POST", M, J(new { kind = "sensor_temp", client_id = "dev1-6", entered_by = "이", sensor_id = "T-NEW", floor = "9", x = "5", y = 4, timestamp = "2026-12-30T10:00:30", temp_c = 12.5 }), true),
    ("동바리 확인 없는 구역", "POST", M, J(new { kind = "shore_check", client_id = "dev1-7", entered_by = "박안전", zone_id = "9F-Z9", timestamp = "2026-12-31T08:00", present = "예" }), true),
    ("동바리 확인 미래 시각", "POST", M, J(new { kind = "shore_check", client_id = "dev1-7", entered_by = "박안전", zone_id = "10F-A1", timestamp = "2027-01-01T08:00", present = "예" }), true),
    ("동바리 확인 값", "POST", M, J(new { kind = "shore_check", client_id = "dev1-7", entered_by = "박안전", zone_id = "10F-A1", timestamp = "2026-12-31T08:00", present = "몰라" }), true),
    ("동바리 확인 저장", "POST", M, J(new { kind = "shore_check", client_id = "dev1-7", entered_by = " 박안전 ", zone_id = "10F-A1", timestamp = "2026/12/31 08:10", present = " 아니오 ", photo = "10F-A1.jpg" }), true),
    ("해체 후 점검 값", "POST", M, J(new { kind = "after_check", client_id = "dev1-8", entered_by = "박안전", zone_id = "9F-A1", date = "2026-12-31", items = new Dictionary<string, string> { ["AC-1"] = "몰라" } }), true),
    ("해체 후 점검 빈 항목", "POST", M, J(new { kind = "after_check", client_id = "dev1-8", entered_by = "박안전", zone_id = "9F-A1", date = "2026-12-31", items = new Dictionary<string, string>() }), true),
    ("침하 지점 이름", "POST", M, J(new { kind = "after_check", client_id = "dev1-8", entered_by = "박안전", zone_id = "9F-A1", date = "2026-12-31", settle = new Dictionary<string, object> { ["P 1"] = 1 } }), true),   // 11-12 G9
    ("침하량 범위", "POST", M, J(new { kind = "after_check", client_id = "dev1-8", entered_by = "박안전", zone_id = "9F-A1", date = "2026-12-31", settle = new Dictionary<string, object> { ["P1"] = "1,5", ["P2"] = 300 } }), true),
    ("침하 형식", "POST", M, J(new { kind = "after_check", client_id = "dev1-8", entered_by = "박안전", zone_id = "9F-A1", date = "2026-12-31", settle = new[] { 1 } }), true),
    ("해체 후 점검 미래", "POST", M,J(new { kind = "after_check", client_id = "dev1-8", entered_by = "박안전", zone_id = "9F-A1", date = "2027-01-01", items = new Dictionary<string, string> { ["AC-1"] = "예" } }), true),
    ("해체 후 점검 저장", "POST", M, J(new { kind = "after_check", client_id = "dev1-8", entered_by = "박안전", zone_id = "9F-A1", date = "2026-12-31",
        items = new Dictionary<string, string> { ["AC-4"] = " 아니오 ", ["AC-1"] = "예", ["AC-2"] = "" }, photo = "9F-A1_after.jpg" }), true),
    // R7 (회귀 2회차): 거부도 본문을 다 읽은 뒤 403 — 400 KB 본문이면 안 읽고 닫을 때 보내는 쪽이 연결 끊김 (세 번 연속)
    ("신고에 입력 토큰 큰 본문 1", "POST", "/api/report", J(new { zone_id = "9F-A1", pad = new string('a', 400_000) }), true),
    ("신고에 입력 토큰 큰 본문 2", "POST", "/api/report", J(new { zone_id = "9F-A1", pad = new string('b', 400_000) }), true),
    ("신고에 입력 토큰 큰 본문 3", "POST", "/api/report", J(new { zone_id = "9F-A1", pad = new string('c', 400_000) }), true),
    ("입력 토큰 틀림 큰 본문", "POST", "/api/measure?t=wrong", J(new { kind = "cylinder", pad = new string('d', 400_000) }), false),
    // v6 동바리 사양·설치일 (Revit [동바리 사양] 창이 쓰는 종류 shore_spec): 없는 층·다른 층 구역·종류·이음 정수·타설일 뒤 거부, 층 전체·구역 저장, 같은 층 두 번째는 옛 설치일보다 앞이면 거부 (11-12 2-1: 같은 날·뒤 날은 다시 넣기 = 정정)
    ("사양 없는 층", "POST", M, J(new { kind = "shore_spec", client_id = "dev1-p0", entered_by = "박", floor = 99, zone_id = "", type = "파이프 서포트", spec = "V4", spacing_x_mm = 1200, spacing_y_mm = 1200, splices = 0, install_date = "2026-11-23" }), true),
    ("사양 층 글자", "POST", M, J(new { kind = "shore_spec", client_id = "dev1-p0", entered_by = "박", floor = "아홉", zone_id = "", type = "파이프 서포트", spec = "V4", spacing_x_mm = 1200, spacing_y_mm = 1200, splices = 0, install_date = "2026-11-23" }), true),
    ("사양 다른 층 구역", "POST", M, J(new { kind = "shore_spec", client_id = "dev1-p0", entered_by = "박", floor = 9, zone_id = "10F-A1", type = "파이프 서포트", spec = "V4", spacing_x_mm = 1200, spacing_y_mm = 1200, splices = 0, install_date = "2026-11-23" }), true),
    ("사양 종류", "POST", M, J(new { kind = "shore_spec", client_id = "dev1-p0", entered_by = "박", floor = 9, zone_id = "", type = "각재", spec = "V4", spacing_x_mm = 1200, spacing_y_mm = 1200, splices = 0, install_date = "2026-11-23" }), true),
    ("사양 간격 범위", "POST", M, J(new { kind = "shore_spec", client_id = "dev1-p0", entered_by = "박", floor = 9, zone_id = "", type = "파이프 서포트", spec = "V4", spacing_x_mm = 50, spacing_y_mm = 1200, splices = 0, install_date = "2026-11-23" }), true),
    ("사양 이음 정수", "POST", M, J(new { kind = "shore_spec", client_id = "dev1-p0", entered_by = "박", floor = 9, zone_id = "", type = "파이프 서포트", spec = "V4", spacing_x_mm = 1200, spacing_y_mm = 1200, splices = 1.5, install_date = "2026-11-23" }), true),
    ("사양 타설 뒤", "POST", M, J(new { kind = "shore_spec", client_id = "dev1-p0", entered_by = "박", floor = 9, zone_id = "", type = "파이프 서포트", spec = "V4", spacing_x_mm = 1200, spacing_y_mm = 1200, splices = 0, install_date = "2026-11-27" }), true),
    ("사양 저장 층 전체", "POST", M, J(new { kind = "shore_spec", client_id = "dev1-p1", entered_by = " 박 ", floor = "9", zone_id = "", type = "파이프 서포트", spec = "=V4", spacing_x_mm = "1200", spacing_y_mm = 1200.0, splices = 0, install_date = "2026-11-23", note = "1차" }), true),
    ("사양 저장 구역", "POST", M, J(new { kind = "shore_spec", client_id = "dev1-p2", entered_by = "박", floor = 9, zone_id = "9F-B1", type = "시스템 동바리", spec = "SY-1", spacing_x_mm = 1500, spacing_y_mm = 1500, splices = 1, install_date = "2026-11-22" }), true),
    ("사양 두 번째 옛 설치일 앞", "POST", M, J(new { kind = "shore_spec", client_id = "dev1-p3", entered_by = "박", floor = 9, zone_id = "", type = "파이프 서포트", spec = "V4", spacing_x_mm = 1200, spacing_y_mm = 1200, splices = 0, install_date = "2026-11-21" }), true),
    ("최근 입력", "GET", "/api/recent?n=10", null, true),
    ("최근 입력 n 틀림", "GET", "/api/recent?n=abc", null, true),
    ("저장 뒤 모델", "GET", "/api/model", null, true),
};
int sameCount = 0;
foreach (var (label, method, path, body, auth) in cases)
{
    var c = await Send(csHttp, server.Token, method, path, body, auth);
    if (pyHttp == null) { Console.WriteLine($"  {label}: C# {c.Code}"); continue; }
    var p = await Send(pyHttp, pyToken, method, path, body, auth);
    var d = new List<string>();
    if (p.Code != c.Code) d.Add($"상태 코드 Python {p.Code} ≠ C# {c.Code}");
    else if (path == "/") { if (p.Body != c.Body) d.Add("입력 화면 내용 다름"); }
    else if (p.Body.StartsWith('{') || c.Body.StartsWith('{'))
    {
        JsonNode? pj = JsonNode.Parse(p.Body), cj = JsonNode.Parse(c.Body);
        // 최근 목록은 받은 시각(entered_at) 순 — 두 서버가 같은 요청을 1초 어긋나게 받으면 순서가 바뀔 수 있어 client_id 순으로 맞춰 비교
        JsonNode? ByClient(JsonNode? j) => j?["items"] is JsonArray a ? new JsonArray(a.OrderBy(i => (string?)i!["client_id"], StringComparer.Ordinal).Select(i => i!.DeepClone()).ToArray()) : null;
        if (path.StartsWith("/api/recent")) (pj, cj) = (ByClient(pj), ByClient(cj));
        Diff(pj, cj, "$", d);
    }
    if (d.Count == 0) sameCount++;
    else Console.WriteLine($"  다름 [{label}] {string.Join(" / ", d)}\n      Python: {p.Body[..Math.Min(300, p.Body.Length)]}\n      C#    : {c.Body[..Math.Min(300, c.Body.Length)]}");
}
if (pyHttp != null)
{
    Check(sameCount == cases.Count, $"응답 {sameCount}/{cases.Count} 이 Python 과 같음 (상태 코드·JSON, entered_at 제외)");
    // project_id (감사 A-07): 두 서버 폴더가 달라 값은 다름 → Python 값으로 식(sha1(소문자 절대 경로) 16진 앞 10자)을 확인하고 C# 도 같은 식인지
    string Pid(string dir) => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)).ToLowerInvariant())))[..10].ToLowerInvariant();
    var pyPid = (string?)JsonNode.Parse((await Send(pyHttp, pyToken, "GET", "/api/model", null, true)).Body)!["project_id"];
    var csPid = (string?)JsonNode.Parse((await Send(csHttp, server.Token, "GET", "/api/model", null, true)).Body)!["project_id"];
    Check(pyPid == Pid(pyDir) && csPid == Pid(csDir), $"project_id: Python {pyPid} (식 {Pid(pyDir)}), C# {csPid} (식 {Pid(csDir)})");
    foreach (var f in new[] { "cylinders.csv", "sensors.csv", "sensor_strength.csv", "rebound.csv", "shore_checks.csv", "after_check.csv", "shore_spec.csv" })
    {
        string Norm(string dir) => File.Exists(Path.Combine(dir, f))
            ? System.Text.RegularExpressions.Regex.Replace(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(dir, f))), @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}", "*") : "(없음)";
        Check(Norm(pyDir) == Norm(csDir), $"{f}: Python 과 바이트 동일 (entered_at 제외)");
    }
    // 오류 점검 ②: Excel 이 연 것처럼 cylinders.csv 를 잡은 채 저장 → 두 서버 같은 상태 코드·한국어 안내 (Python PermissionError · C# IOException 공유 위반), 파일은 그대로
    {
        var lockBody = J(new { kind = "cylinder", client_id = "dev1-lock", entered_by = "김", zone_id = "9F-B2", test_date = "2026-12-30", curing = "현장봉함", f1 = 31.1, f2 = 31.2, f3 = 31.3 });
        (int Code, string Body) pl, cl;
        long pyLen = new FileInfo(Path.Combine(pyDir, "cylinders.csv")).Length, csLen = new FileInfo(Path.Combine(csDir, "cylinders.csv")).Length;
        using (new FileStream(Path.Combine(pyDir, "cylinders.csv"), FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            pl = await Send(pyHttp, pyToken, "POST", M, lockBody, true);
        using (new FileStream(Path.Combine(csDir, "cylinders.csv"), FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            cl = await Send(csHttp, server.Token, "POST", M, lockBody, true);
        string? pe = (string?)JsonNode.Parse(pl.Body)?["error"], ce = (string?)JsonNode.Parse(cl.Body)?["error"];
        Check(pl.Code == 500 && cl.Code == 500 && pe == ce && ce!.Contains("Excel") && new FileInfo(Path.Combine(pyDir, "cylinders.csv")).Length == pyLen
              && new FileInfo(Path.Combine(csDir, "cylinders.csv")).Length == csLen, $"파일 잠김 저장: Python {pl.Code} · C# {cl.Code} — {ce}" + (pe != ce ? $" / Python: {pe}" : ""));
    }
    // 근거 원장 n/8·원장 경고·타설 전 확인 (field_server.py model 의 evidence_stage·evidence_warnings·pour_gate): 두 폴더에 같은 것을 더하고 모델 대조
    //   13F 구역(타설 전, 12F 를 복사) → pour_gate, 9F-A1 원장 1·2 (+ 공시체 3) → 3/8, 9F-A2 감리확인만 → 순서 경고
    foreach (var dir in new[] { pyDir, csDir })
    {
        var zl = File.ReadAllLines(Path.Combine(dir, "zones.csv"));
        var add = zl.Where(l => l.StartsWith("12F-")).Select(l => { var f = l.Split(','); f[0] = "13F-" + f[0][4..]; f[1] = "13"; return string.Join(",", f); });
        File.WriteAllLines(Path.Combine(dir, "zones.csv"), zl.Concat(add).Concat(new[] { "B1-A1,-1,0.0,0.0,10.0,9.0", "PH-A1,14,0.0,0.0,10.0,9.0" }), new UTF8Encoding(false));   // KBIM 처럼 이름 ≠ 번호 (floor_name)
        Evidence.Add(dir, "9F-A1", "2026-11-23", 1, "박안전", "IC-9F", "합격");
        Evidence.Add(dir, "9F-A1", "2026-12-20", 2, "김현장", "WP-9F");
        Evidence.Add(dir, "9F-A2", "2026-12-30", 6, "이감리");
    }
    var pm = JsonNode.Parse((await Send(pyHttp, pyToken, "GET", "/api/model", null, true)).Body);
    var cm = JsonNode.Parse((await Send(csHttp, server.Token, "GET", "/api/model", null, true)).Body);
    var md = new List<string>();
    Diff(pm, cm, "$", md);
    var czones = cm!["floors"]!.AsArray().SelectMany(f => f!["zones"]!.AsArray()).ToList();
    JsonNode? Zone(string id) => czones.FirstOrDefault(z => (string?)z!["zone_id"] == id);
    var fnames = cm["floors"]!.AsArray().ToDictionary(f => (int)f!["floor"]!, f => (string?)f!["name"]);
    Check(fnames.GetValueOrDefault(-1) == "B1" && fnames.GetValueOrDefault(14) == "PH" && fnames.GetValueOrDefault(9) == "9F", "층 이름 = 첫 구역 ID 앞 (B1·PH·9F)");
    Check(md.Count == 0 && (int?)Zone("9F-A1")?["evidence_stage"] == 3 && Zone("9F-A2")?["evidence_warnings"]?.AsArray().Count == 1
          && Zone("13F-A1")?["pour_gate"]?["decision"] is not null && Zone("12F-A1")?["pour_gate"] is null,
        "모델 근거 원장·타설 전 확인 Python 과 같음 (9F-A1 근거 3/8, 9F-A2 순서 경고, 13F 만 pour_gate — "
        + $"13F-A1 {(string?)Zone("13F-A1")?["pour_gate"]?["decision"]})" + (md.Count > 0 ? "\n    " + string.Join("\n    ", md) : ""));
    // F12 읽기 전용 층 화면: 보기 토큰(데이터 폴더 view_token.txt, 입력 토큰과 다름)으로 GET /api/floor 가 Python 과 같은 JSON, v 로는 입력 쪽·POST 거부
    {
        string pyV = File.ReadAllText(Path.Combine(pyDir, FieldServer.ViewFile)).Trim(), csV = server.ViewToken;
        Check(csV == File.ReadAllText(Path.Combine(csDir, FieldServer.ViewFile)).Trim() && csV != server.Token && pyV != pyToken, "보기 토큰 = 데이터 폴더 파일, 입력 토큰과 다름");
        var vd = new List<string>();
        foreach (var (label, q) in new[] { ("9F", "f=9"), ("B1", "f=-1"), ("PH", "f=14"), ("없는 층", "f=99"), ("층 없음", "x=1") })
        {
            var pv = await Send(pyHttp, "", "GET", $"/api/floor?{q}&v={pyV}", null, false);
            var cv = await Send(csHttp, "", "GET", $"/api/floor?{q}&v={csV}", null, false);
            if (pv.Code != cv.Code) vd.Add($"{label}: 상태 코드 Python {pv.Code} ≠ C# {cv.Code}");
            else { var d1 = new List<string>(); Diff(JsonNode.Parse(pv.Body), JsonNode.Parse(cv.Body), "$", d1); vd.AddRange(d1.Select(x => label + " " + x)); }
        }
        var c9 = JsonNode.Parse((await Send(csHttp, "", "GET", $"/api/floor?f=9&v={csV}", null, false)).Body)!;
        var lines9 = c9["zones"]!.AsArray().ToDictionary(z => (string)z!["zone_id"]!, z => (string)z!["line"]!);
        Check(vd.Count == 0 && lines9.Values.All(l => !l.Contains("하세요")) && (string?)c9["name"] == "9F",
            "읽기 전용 층 화면 Python 과 같음 (9F·B1·PH·없는 층 404·층 없음 400), 지시 문구 없음" + (vd.Count > 0 ? "\n    " + string.Join("\n    ", vd) : ""));
        var codes = new[]
        {
            (await Send(csHttp, "", "GET", $"/api/floor?f=9&v={server.Token}", null, false)).Code,     // 입력 토큰으로는 안 열림
            (await Send(csHttp, "", "GET", $"/api/model?v={csV}", null, false)).Code,                   // 보기 토큰으로 입력 쪽 안 열림
            (await Send(csHttp, "", "POST", $"/api/measure?v={csV}", J(new { kind = "rebound" }), false)).Code,
            (await Send(pyHttp, "", "POST", $"/api/measure?v={pyV}", J(new { kind = "rebound" }), false)).Code,
            (await Send(csHttp, "", "GET", $"/view?v={csV}", null, false)).Code,
        };
        Check(codes.SequenceEqual(new[] { 403, 403, 403, 403, 200 }), "보기 토큰: 입력 토큰으로 403, 입력 쪽·POST 403, /view 200 (" + string.Join(",", codes) + ")");

        // F14 협력업체 해체 신고: 신고 토큰(report_token.txt, 보기·입력 토큰과 다름)으로만 POST /api/report — Python 과 같은 JSON·shore_checks.csv,
        // 승인 기록 없이 '뺐음' → 알림 맨 위 무단 해체 의심, 감리 승인 기록 뒤 '뺐음' → 경고 없음·근거 원장 7 해체
        string pyR = File.ReadAllText(Path.Combine(pyDir, FieldServer.ReportFile)).Trim(), csR = server.ReportToken;
        Check(csR == File.ReadAllText(Path.Combine(csDir, FieldServer.ReportFile)).Trim() && csR != csV && csR != server.Token && pyR != pyV, "신고 토큰 = 데이터 폴더 파일, 보기·입력 토큰과 다름");
        int savedBefore;
        lock (saved) savedBefore = saved.Count;
        var rd = new List<string>();
        async Task<(int Py, int Cs, JsonNode? Body)> Both(string label, string method, string pathT, string? body)
        {
            var pv = await Send(pyHttp!, "", method, pathT.Replace("{R}", pyR).Replace("{V}", pyV).Replace("{T}", pyToken), body, false);
            var cv = await Send(csHttp, "", method, pathT.Replace("{R}", csR).Replace("{V}", csV).Replace("{T}", server.Token), body, false);
            if (pv.Code != cv.Code) rd.Add($"{label}: 상태 코드 Python {pv.Code} ≠ C# {cv.Code}");
            else { var d1 = new List<string>(); Diff(JsonNode.Parse(pv.Body), JsonNode.Parse(cv.Body), "$", d1); rd.AddRange(d1.Select(x => label + " " + x)); }
            return (pv.Code, cv.Code, JsonNode.Parse(cv.Body));
        }
        string Rep(string zone, string present, string cid, string company = "가나건설", string name = "최반장") => J(new { zone_id = zone, present, name, company, client_id = cid });
        var deny = new[]
        {
            await Both("보기 토큰으로 신고", "POST", "/api/report?v={V}", Rep("9F-A2", "아니오", "rep-x")),
            await Both("입력 토큰으로 신고", "POST", "/api/report?t={T}", Rep("9F-A2", "아니오", "rep-x")),
            await Both("신고 토큰으로 입력", "POST", "/api/measure?r={R}", J(new { kind = "rebound" })),
            await Both("신고 토큰으로 모델", "GET", "/api/model?r={R}", null),
            await Both("틀린 신고 토큰", "GET", "/api/floor?f=9&r=wrong", null),
        };
        var f9 = await Both("신고 층 화면", "GET", "/api/floor?f=9&r={R}", null);
        var r1 = await Both("9F-A2 뺐음(승인 없음)", "POST", "/api/report?r={R}", Rep("9F-A2", "아니오", "rep-1"));
        var r1d = await Both("같은 client_id", "POST", "/api/report?r={R}", Rep("9F-A2", "아니오", "rep-1"));
        var bad = new[]
        {
            await Both("이름 없음", "POST", "/api/report?r={R}", Rep("9F-A2", "아니오", "rep-4", name: " ")),
            await Both("없는 구역", "POST", "/api/report?r={R}", Rep("XX", "아니오", "rep-5")),
            await Both("뺐음·다시 세움 아님", "POST", "/api/report?r={R}", Rep("9F-A2", "몰라", "rep-6")),
        };
        var f9b = await Both("신고 뒤 층 화면", "GET", "/api/floor?f=9&r={R}", null);
        foreach (var d in new[] { pyDir, csDir })
            Approval.Record(d, "9F-A1", "감리 승인 기록", "이감리", new Dictionary<string, string> { ["zone_id"] = "9F-A1", ["decision"] = "해체 승인 요청 가능" }, now: new DateTime(2026, 12, 31, 8, 0, 0));
        var r2 = await Both("9F-A1 뺐음(감리 승인 기록 뒤)", "POST", "/api/report?r={R}", Rep("9F-A1", "아니오", "rep-2", company: ""));
        var r3 = await Both("9F-A2 다시 세움", "POST", "/api/report?r={R}", Rep("9F-A2", "예", "rep-3"));
        var r4 = await Both("오프라인 재전송 (device_at)", "POST", "/api/report?r={R}",
            J(new { zone_id = "9F-B1", present = "아니오", name = "최반장", company = "", client_id = "rep-7", device_at = "2026-12-31T08:40" }));
        var pmR = JsonNode.Parse((await Send(pyHttp!, pyToken, "GET", "/api/model", null, true)).Body);
        var cmR = JsonNode.Parse((await Send(csHttp, server.Token, "GET", "/api/model", null, true)).Body)!;
        Diff(pmR, cmR, "신고 뒤 모델", rd);
        string NormSc(string dir) => System.Text.RegularExpressions.Regex.Replace(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(dir, "shore_checks.csv"))), @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}", "*");
        var scLast = File.ReadAllLines(Path.Combine(csDir, "shore_checks.csv"));
        Check(rd.Count == 0 && NormSc(pyDir) == NormSc(csDir), "신고 Python 과 같음 (거부 5·층 화면·신고 4·중복·잘못된 요청 3·모델·shore_checks.csv 바이트)" + (rd.Count > 0 ? "\n    " + string.Join("\n    ", rd) : ""));
        Check(deny.All(c => c.Py == 403 && c.Cs == 403) && bad.All(c => c.Cs == 400) && (bool?)f9.Body?["can_report"] == true,
            "신고 토큰만 신고 가능 (보기·입력 토큰 403, 신고 토큰으로 입력·모델 403, 이름·구역·종류 틀리면 400) (" + string.Join(",", deny.Select(c => c.Cs)) + ")");
        var a0 = cmR["alerts"]!.AsArray()[0]!;
        Check((string?)r1.Body?["message"] == "신고 저장됨 — 감리 승인 기록 없음: 현장 확인 요청됨" && (bool?)r1d.Body?["duplicate"] == true
              && (string?)a0["kind"] == "무단 해체 의심" && (string?)a0["zone_id"] == "9F-A2" && (string?)f9b.Body?["status"] == "무단 해체 의심 — 확인 필요: 9F-A2",
            $"승인 기록 없이 '뺐음' → 알림 맨 위 무단 해체 의심 ({(string?)a0["kind"]} {(string?)a0["zone_id"]})");
        Check((string?)r2.Body?["message"] == "신고 저장됨 — 감리 승인 기록 있음: 해체 시각 기록됨" && !Evidence.RemovalWatch(csDir, new DateOnly(2026, 12, 31)).ContainsKey("9F-A1")
              && Evidence.LedgerAt(csDir, new DateOnly(2026, 12, 31))["9F-A1"].Dates.GetValueOrDefault(7) == "2026-12-31"
              && Evidence.Entries(csDir).Any(x => x.ZoneId == "9F-A1" && x.Stage == 7 && x.Result == "2026-12-31T09:00" && x.By == "최반장" && x.Source == PourGate.ChecksFile)
              && (string?)r3.Body?["message"] == "신고 저장됨 — 동바리 다시 세움",
            "감리 승인 기록 뒤 '뺐음' → 경고 없음·근거 원장 7 해체 (결과 = 실제 시각 2026-12-31T09:00)");
        Check((string?)r4.Body?["timestamp"] == "2026-12-31T09:00" && scLast[^1].Contains(",기기 시각 2026-12-31T08:40 (오프라인 저장 뒤 전송),rep-7,"),
            "오프라인 재전송: 기록 시각 = 서버가 받은 시각, 기기 시각은 note 에만 (" + scLast[^1] + ")");
        for (int i = 0; i < 40; i++) { lock (saved) if (saved.Count >= savedBefore + 4) break; await Task.Delay(50); }
        lock (saved)
        {
            var add = saved.Skip(savedBefore).ToList();
            Check(add.Count == 4 && add.All(e => e.Kind == "shore_check" && e.Rows == 1) && add[0].Target == "9F-A2" && add[1].Target == "9F-A1" && add[3].Target == "9F-B1",
                $"신고 Saved 이벤트 {add.Count}번 (9F-A2 뺐음·9F-A1 뺐음·9F-A2 다시 세움·9F-B1 재전송 — 애드인이 대시보드 알림 갱신)");
            saved.RemoveRange(savedBefore, add.Count);   // 아래 입력 Saved 시험(7번)과 따로
        }
        Check(server.ReportUrl(9) == server.PrimaryUrl()[..server.PrimaryUrl().IndexOf("/?", StringComparison.Ordinal)] + $"/view?r={csR}&f=9" && !server.ReportUrl(9).Contains(server.Token),
            "신고 QR 주소 = /view?r=신고토큰&f=층 (입력 토큰 없음)");

        // F15 현장 사진: 같은 사진(가짜 JPEG·PNG)·틀린 사진을 두 서버에 → 같은 JSON·사진 파일(이름·바이트)·CSV, 같은 사진 두 번 = 같은 파일, 거절은 줄·파일 없음
        lock (saved) savedBefore = saved.Count;
        rd.Clear();
        var jpg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0 }.Concat(Enumerable.Range(0, 2048).Select(i => (byte)i)).ToArray();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(new byte[300]).ToArray();
        string Jpg64() => "data:image/jpeg;base64," + Convert.ToBase64String(jpg);
        string Sc(string cid, string zone, string ts, string data) => J(new { kind = "shore_check", client_id = cid, entered_by = "박안전", zone_id = zone, timestamp = ts, present = "예", photo_data = data });
        var ph1 = await Both("사진 동바리 확인", "POST", "/api/measure?t={T}", Sc("ph-1", "9F-A1", "2026-12-31T08:30", Jpg64()));
        var ph2 = await Both("같은 사진 두 번", "POST", "/api/measure?t={T}", Sc("ph-2", "9F-A1", "2026-12-31T08:40", Jpg64()));
        var phBad = new[]
        {
            await Both("텍스트(.jpg 이름)", "POST", "/api/measure?t={T}", Sc("ph-3", "9F-A2", "2026-12-31T08:30", Convert.ToBase64String(Encoding.UTF8.GetBytes("not a photo, just text saved as x.jpg")))),
            await Both("크기 초과", "POST", "/api/measure?t={T}", Sc("ph-4", "9F-A2", "2026-12-31T08:30", Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF }.Concat(new byte[Photos.MaxBytes]).ToArray()))),
            await Both("base64 아님", "POST", "/api/measure?t={T}", Sc("ph-5", "9F-A2", "2026-12-31T08:30", "@@@@")),
        };
        var ph6 = await Both("사진 해체 후 점검 (PNG)", "POST", "/api/measure?t={T}", J(new { kind = "after_check", client_id = "ph-6", entered_by = "박안전", zone_id = "9F-A2", date = "2026-12-31",
            items = new Dictionary<string, string> { ["AC-1"] = "예", ["AC-2"] = "아니오" }, photo_data = Convert.ToBase64String(png) }));
        var ph7 = await Both("사진 해체 신고", "POST", "/api/report?r={R}", J(new { zone_id = "9F-C1", present = "아니오", name = "최반장", client_id = "rep-ph", photo_data = Jpg64() }));
        string[] PhotoFiles(string dir) => Directory.Exists(Path.Combine(dir, Photos.Dir))
            ? Directory.EnumerateFiles(Path.Combine(dir, Photos.Dir), "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/') + " " + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)))).OrderBy(x => x, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
        string NormF(string dir, string f) => System.Text.RegularExpressions.Regex.Replace(Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(dir, f))), @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}", "*");
        var jpgSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(jpg)).ToLowerInvariant();
        var want1 = $"사진/20261231/9F-A1_동바리확인_083000_{jpgSha[..8]}.jpg";   // 이름 시각 = 기록 시각 08:30 (총괄 29차)
        Check(rd.Count == 0 && PhotoFiles(pyDir).SequenceEqual(PhotoFiles(csDir)) && NormF(pyDir, "shore_checks.csv") == NormF(csDir, "shore_checks.csv") && NormF(pyDir, "after_check.csv") == NormF(csDir, "after_check.csv"),
            "현장 사진 Python 과 같음 (응답 JSON·사진 파일 이름·바이트·shore_checks·after_check CSV)" + (rd.Count > 0 ? "\n    " + string.Join("\n    ", rd) : ""));
        Check((string?)ph1.Body?["photo"] == want1 && (string?)ph2.Body?["photo"] == want1 && PhotoFiles(csDir).Length == 3
              && File.ReadAllBytes(Path.Combine(csDir, want1.Replace('/', Path.DirectorySeparatorChar))).AsSpan().SequenceEqual(jpg)
              && ((string?)ph6.Body?["photo"] ?? "").EndsWith(".png", StringComparison.Ordinal) && ((string?)ph7.Body?["photo"] ?? "").Contains("_해체신고_"),
            $"사진 원본 그대로·이름 = 사진/YYYYMMDD/구역_종류_HHMMSS_sha8, 같은 사진 두 번 = 같은 파일 (파일 {PhotoFiles(csDir).Length}개: 동바리확인·해체후점검 png·해체신고)");
        Check(phBad.All(c => c.Py == 400 && c.Cs == 400) && ((string?)phBad[0].Body?["error"] ?? "").Contains("JPEG·PNG") && ((string?)phBad[1].Body?["error"] ?? "").Contains("너무 큽니다")
              && !File.ReadAllText(Path.Combine(csDir, "shore_checks.csv")).Contains("ph-3"),
            "사진 거절: 확장자만 jpg 인 텍스트·8 MB 초과·base64 아님 → 400, 줄 없음 (" + string.Join(" / ", phBad.Select(c => (string?)c.Body?["error"])) + ")");
        for (int i = 0; i < 40; i++) { lock (saved) if (saved.Count >= savedBefore + 4) break; await Task.Delay(50); }
        lock (saved) saved.RemoveRange(savedBefore, saved.Count - savedBefore);   // 아래 입력 Saved 시험(7번)과 따로

        // F16 동바리 단위 현장 점검: 두 폴더에 같은 shores.csv → 입력 토큰 층 평면(동바리 점·zone_rects)·거부 8·저장 2·중복·저장 뒤 평면 대조, install_inspection.csv 바이트
        lock (saved) savedBefore = saved.Count;
        rd.Clear();
        foreach (var dir in new[] { pyDir, csDir })
            File.WriteAllText(Path.Combine(dir, ShoreInspect.Shores), "id,zone_id,x,y,field_label\n9F-B1-R01C01,9F-B1,10.6,0.6,B1-1\n9F-B1-R01C02,9F-B1,11.8,0.6,\n9F-B1-R02C01,9F-B1,10.6,1.8,\n9F-A2-R01C01,9F-A2,0.6,9.6,\n", new UTF8Encoding(false));
        string Si(string cid, string zone, string stage, object items, string? ts = "2026-12-30T10:00") =>
            ts is null ? J(new { kind = "shore_inspect", client_id = cid, entered_by = "김점검", zone_id = zone, stage, items })
                       : J(new { kind = "shore_inspect", client_id = cid, entered_by = "김점검", zone_id = zone, stage, timestamp = ts, items });
        object It(string code, string result, params string[] ids) => new { item_code = code, result, shore_ids = ids };
        var fl0 = await Both("점검 층 평면 (입력 토큰)", "GET", "/api/floor?t={T}&f=9", null);
        var siBad = new[]
        {
            await Both("점검 토큰 틀림", "GET", "/api/floor?t=wrong&f=9", null),
            await Both("점검 층 번호 없음", "GET", "/api/floor?t={T}", null),
            await Both("점검 단계 틀림", "POST", "/api/measure?t={T}", Si("si-x", "9F-B1", "설치", new[] { It("SHR-B", "OK") })),
            await Both("점검 항목 없음", "POST", "/api/measure?t={T}", Si("si-x", "9F-B1", ShoreInspect.Install, Array.Empty<object>())),
            await Both("점검 없는 항목", "POST", "/api/measure?t={T}", Si("si-x", "9F-B1", ShoreInspect.Install, new[] { It("DLY-D", "OK") })),
            await Both("점검 항목 두 번", "POST", "/api/measure?t={T}", Si("si-x", "9F-B1", ShoreInspect.Install, new[] { It("SHR-B", "OK"), It("SHR-B", "NG", "9F-B1-R01C01") })),
            await Both("점검 결과 값", "POST", "/api/measure?t={T}", Si("si-x", "9F-B1", ShoreInspect.Install, new[] { It("SHR-B", "몰라") })),
            await Both("점검 다른 구역 동바리", "POST", "/api/measure?t={T}", Si("si-x", "9F-B1", ShoreInspect.Install, new[] { It("SHR-B", "NG", "9F-A2-R01C01") })),
            await Both("점검 NG 동바리 없음", "POST", "/api/measure?t={T}", Si("si-x", "9F-B1", ShoreInspect.Install, new[] { It("SHR-B", "NG") })),
            await Both("점검 미래 시각", "POST", "/api/measure?t={T}", Si("si-x", "9F-B1", ShoreInspect.Install, new[] { It("SHR-B", "OK") }, "2027-01-01T08:00")),
            await Both("점검 동바리 목록 형식", "POST", "/api/measure?t={T}", J(new { kind = "shore_inspect", client_id = "si-x", entered_by = "김점검", zone_id = "9F-B1", stage = ShoreInspect.Install,
                items = new[] { new { item_code = "SHR-B", result = "NG", shore_ids = "9F-B1-R01C01" } } })),
        };
        var si1 = await Both("점검 저장 (설치 NG 2개)", "POST", "/api/measure?t={T}", Si("si-1", "9F-B1", ShoreInspect.Install,
            new[] { It("SHR-B", " ng ", "9F-B1-R01C02", " 9F-B1-R01C01 ", "9F-B1-R01C02"), It("SHR-S", "OK") }));
        var si2 = await Both("점검 저장 (동바리 없는 구역, 시각 없음)", "POST", "/api/measure?t={T}", Si("si-2", "9F-C1", "일상 점검", new[] { It("DLY-D", "NG") }, null));
        var si1d = await Both("점검 같은 client_id", "POST", "/api/measure?t={T}", Si("si-1", "9F-B1", ShoreInspect.Install, new[] { It("SHR-B", "OK") }));
        var fl1 = await Both("점검 뒤 층 평면", "GET", "/api/floor?t={T}&f=9", null);
        Check(rd.Count == 0 && NormF(pyDir, ShoreInspect.File) == NormF(csDir, ShoreInspect.File),
            "동바리 점검 Python 과 같음 (층 평면·거부·저장·중복 JSON, install_inspection.csv 바이트)" + (rd.Count > 0 ? "\n    " + string.Join("\n    ", rd) : ""));
        var shoresJ = fl1.Body?["shores"]?.AsArray() ?? new JsonArray();
        bool Ng(string id) => shoresJ.Any(s => (string?)s!["id"] == id && (bool?)s["ng"] == true);
        Check(fl0.Py == 200 && fl0.Body?["shores"]?.AsArray().Count == 4 && fl0.Body?["zone_rects"]?.AsArray().Count == fl0.Body?["zones"]?.AsArray().Count
              && siBad[0].Cs == 403 && siBad[1].Cs == 400 && siBad.Skip(2).All(c => c.Py == 400 && c.Cs == 400),
            $"점검 거부: 토큰 403·층 없음 400·형식 9 → 400 ({string.Join(" / ", siBad.Skip(2).Select(c => (string?)c.Body?["error"]))})");
        Check((string?)si1.Body?["open_ng"] == "수평연결재 9F-B1-R01C01 외 1" && (int?)si1.Body?["rows"] == 2 && (string?)si2.Body?["open_ng"] == "변형·침하 (구역 전체)"
              && (bool?)si1d.Body?["duplicate"] == true && Ng("9F-B1-R01C01") && Ng("9F-B1-R01C02") && !Ng("9F-B1-R02C01")
              && Evaluator.Evaluate(csDir, at).First(r => r.ZoneId == "9F-B1").Decision != "해체 승인 요청 가능",
            $"점검 저장 → 풀리지 않은 NG 한 줄·평면 빨강 동바리 2개·9F-B1 해체 요청 불가 ({(string?)si1.Body?["open_ng"]} | {(string?)si2.Body?["open_ng"]} | {(int?)si1.Body?["rows"]} | {(bool?)si1d.Body?["duplicate"]} | {string.Join(",", shoresJ.Where(s => (bool?)s!["ng"] == true).Select(s => (string?)s!["id"]))} — 규칙 ⑦: {Evaluator.Evaluate(csDir, at).First(r => r.ZoneId == "9F-B1").Decision})");
        for (int i = 0; i < 40; i++) { lock (saved) if (saved.Count >= savedBefore + 2) break; await Task.Delay(50); }
        lock (saved) saved.RemoveRange(savedBefore, saved.Count - savedBefore);

        // 타설 등록 (종류 pour — Revit [타설 등록] 창이 SubmitLocal 로 같은 검사를 씀, 휴대폰 화면엔 없음): 13F-A1(타설 전) 거부 6·저장·같은 구역 다시 거부, pours.csv 바이트
        lock (saved) savedBefore = saved.Count;
        rd.Clear();
        string Pr(string cid, params (string K, object V)[] over)
        {
            var d = new Dictionary<string, object> { ["kind"] = "pour", ["client_id"] = cid, ["entered_by"] = "김공무", ["zone_id"] = "13F-A1", ["pour_date"] = "2026-12-30", ["pour_time"] = "7:30", ["mix"] = "보통", ["fck"] = "24" };
            foreach (var (k, v) in over) d[k] = v;
            return J(d);
        }
        var prBad = new[]
        {
            await Both("타설 없는 구역", "POST", "/api/measure?t={T}", Pr("pr-x", ("zone_id", "13F-Z9"))),
            await Both("타설 이미 있음", "POST", "/api/measure?t={T}", Pr("pr-x", ("zone_id", "9F-A1"))),
            await Both("타설 미래", "POST", "/api/measure?t={T}", Pr("pr-x", ("pour_date", "2027-01-01"))),
            await Both("타설 시각", "POST", "/api/measure?t={T}", Pr("pr-x", ("pour_time", "25:00"))),
            await Both("타설 배합", "POST", "/api/measure?t={T}", Pr("pr-x", ("mix", "몰라"))),
            await Both("타설 fck", "POST", "/api/measure?t={T}", Pr("pr-x", ("fck", 70))),
        };
        var pr1 = await Both("타설 저장", "POST", "/api/measure?t={T}", Pr("pr-1"));
        var pr2 = await Both("타설 같은 구역 다시", "POST", "/api/measure?t={T}", Pr("pr-2"));
        Check(rd.Count == 0 && NormF(pyDir, "pours.csv") == NormF(csDir, "pours.csv"),
            "타설 등록 Python 과 같음 (거부 6·저장·다시 거부 JSON, pours.csv 바이트)" + (rd.Count > 0 ? "\n    " + string.Join("\n    ", rd) : ""));
        Check(prBad.All(c => c.Py == 400 && c.Cs == 400) && pr1.Cs == 200 && (int?)pr1.Body?["rows"] == 1 && pr2.Cs == 400
              && File.ReadAllLines(Path.Combine(csDir, "pours.csv"))[^1].StartsWith("13F-A1,2026-12-30,07:30,보통,24,", StringComparison.Ordinal),
            "타설 등록: 거부 6 → 400 (" + string.Join(" / ", prBad.Select(c => (string?)c.Body?["error"])) + "), 저장 1줄 '13F-A1,2026-12-30,07:30,보통,24,…', 같은 구역 다시 400");
        for (int i = 0; i < 40; i++) { lock (saved) if (saved.Count >= savedBefore + 1) break; await Task.Delay(50); }
        lock (saved) saved.RemoveRange(savedBefore, saved.Count - savedBefore);

        // v6.2 W1 타설 묶음: 13F-B1·C1 을 묶음 P-13F-1 로 등록 (다른 날 거부) → 없는 묶음 공시체 거부 → 묶음 공시체 저장 (lot_zones 2) → 같은 공시체 거부, pours·cylinders 바이트
        lock (saved) savedBefore = saved.Count;
        rd.Clear();
        string LotPour(string cid, string zid, string day = "2026-12-29") => J(new { kind = "pour", client_id = cid, entered_by = "김공무", zone_id = zid, pour_date = day, pour_time = "08:00", mix = "보통", fck = "24", pour_id = "P-13F-1" });
        string LotCyl(string cid, string lot, object f2) => J(new { kind = "cylinder", client_id = cid, entered_by = "김", zone_id = "", pour_id = lot, test_date = "2026-12-30", curing = "현장봉함", f1 = 20, f2, f3 = 21 });
        var lt1 = await Both("타설 묶음 등록", "POST", "/api/measure?t={T}", LotPour("lot-1", "13F-B1"));
        var lt2 = await Both("타설 묶음 다른 날", "POST", "/api/measure?t={T}", LotPour("lot-2", "13F-C1", "2026-12-28"));
        var lt3 = await Both("타설 묶음 둘째 구역", "POST", "/api/measure?t={T}", LotPour("lot-3", "13F-C1"));
        var lt4 = await Both("없는 타설 묶음 공시체", "POST", "/api/measure?t={T}", LotCyl("lot-4", "P-없음", 20.5));
        var lt5 = await Both("타설 묶음 공시체", "POST", "/api/measure?t={T}", LotCyl("lot-5", "P-13F-1", 20.5));
        var lt6 = await Both("같은 타설 묶음 공시체", "POST", "/api/measure?t={T}", LotCyl("lot-6", "P-13F-1", "20.50"));
        Check(rd.Count == 0 && NormF(pyDir, "pours.csv") == NormF(csDir, "pours.csv") && NormF(pyDir, "cylinders.csv") == NormF(csDir, "cylinders.csv"),
            "타설 묶음 Python 과 같음 (등록·거부·묶음 공시체 JSON, pours·cylinders.csv 바이트)" + (rd.Count > 0 ? "\n    " + string.Join("\n    ", rd) : ""));
        Check(lt1.Cs == 200 && lt2.Cs == 400 && lt3.Cs == 200 && lt4.Cs == 400 && lt5.Cs == 200 && lt5.Body?["lot_zones"]?.AsArray().Count == 2 && lt6.Cs == 400
              && Evaluator.CylRows(csDir).Count(r => r["cyl_lot"] == "P-13F-1 · 2구역 공통") == 2,
            $"타설 묶음: 등록 → 다른 날 거부 ({(string?)lt2.Body?["error"]}) → 둘째 구역 → 묶음 공시체 2구역 공통 → 같은 공시체 거부 ({(string?)lt6.Body?["error"]})");
        for (int i = 0; i < 40; i++) { lock (saved) if (saved.Count >= savedBefore + 3) break; await Task.Delay(50); }
        lock (saved) saved.RemoveRange(savedBefore, saved.Count - savedBefore);

        // Revit 입력 창 경로 (SubmitLocal): 같은 검사 — 틀린 본문이 하나라도 있으면 아무것도 안 씀, 맞으면 공시체 1줄 (기기 = Windows)
        {
            var before = File.ReadAllText(Path.Combine(csDir, "cylinders.csv"));
            JsonObject Cyl(string zone, double f2) => new() { ["kind"] = "cylinder", ["entered_by"] = "김공무", ["zone_id"] = zone, ["test_date"] = "2026-12-31", ["curing"] = "현장봉함", ["f1"] = 30, ["f2"] = f2, ["f3"] = 30 };
            var badL = server.SubmitLocal(new[] { Cyl("9F-A1", 30), Cyl("9F-A2", 300) });
            var same = File.ReadAllText(Path.Combine(csDir, "cylinders.csv")) == before;
            var okL = server.SubmitLocal(new[] { Cyl("9F-A1", 31) });
            var lastC = File.ReadAllLines(Path.Combine(csDir, "cylinders.csv"))[^1];
            Check(!badL.Ok && badL.Error.Contains("범위 밖") && same && okL.Ok && okL.Rows == 1 && lastC.StartsWith("9F-A1,2026-12-31,현장봉함,30,31,30,", StringComparison.Ordinal) && lastC.Contains(",Windows,"),
                $"SubmitLocal (Revit 입력 창): 하나라도 틀리면 안 씀 ({badL.Error}) · 맞으면 1줄 ({lastC[..Math.Min(60, lastC.Length)]}…)");
            (bool Ok, string Error, int Rows) lockL;   // 코드 검토 2차: Excel 이 연 파일 → 예외로 창이 깨지지 않고 한국어 한 줄
            using (new FileStream(Path.Combine(csDir, "cylinders.csv"), FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                lockL = server.SubmitLocal(new[] { Cyl("9F-A1", 32) });
            Check(!lockL.Ok && lockL.Error.Contains("Excel") && File.ReadAllLines(Path.Combine(csDir, "cylinders.csv"))[^1] == lastC, $"SubmitLocal 파일 잠김 → 저장 안 함·한국어 ({lockL.Error})");
        }
    }
    pyProc!.Kill(true);
}
var cylLast = File.ReadAllLines(Path.Combine(csDir, "cylinders.csv"))[^1];
Console.WriteLine("  공시체 추가 줄: " + System.Text.RegularExpressions.Regex.Replace(cylLast, @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}", "(받은 시각)"));
lock (saved)
    Check(saved.Count == 9 && saved[0].Target == "9F-A2" && saved[1].Target == "9F-B2" && saved[2].Kind == "rebound" && saved[3].Rows == 2 && saved[4].Target == "T-NEW" && saved[5].Kind == "shore_check"
            && saved[6].Kind == "after_check" && saved[6].Rows == 2 && saved[7].Kind == "shore_spec" && saved[7].Target == "" && saved[8].Target == "9F-B1",
        $"Saved 이벤트 {saved.Count}번 (공시체·공시체(채취 정보)·반발경도·강도 센서 2줄·새 온도 센서·동바리 확인·해체 후 점검 2항목·동바리 사양 층 전체·구역(v6) — 애드인이 판정 갱신에 씀)");

// 층 표지 QR (FloorSign.PhoneUrl): 서버가 켜져 있고 같은 데이터 폴더면 현장 입력 창과 같은 주소 + #floor=N, 다른 폴더·꺼짐이면 없음
{
    var ph = FloorSign.PhoneUrl(server, csDir);
    var sign = FloorSign.Page("시험", at, FloorSign.Floors(csDir, Evaluator.Evaluate(csDir, at), at), ph, null);
    var viewRoot = server.PrimaryUrl()[..server.PrimaryUrl().IndexOf("/?", StringComparison.Ordinal)];
    Check(ph(9) == $"{viewRoot}/view?v={server.ViewToken}&f=9" && ph(-1) == $"{viewRoot}/view?v={server.ViewToken}&f=-1" && !ph(9)!.Contains(server.Token)
          && FloorSign.PhoneUrl(server, pyDir)(9) == null && FloorSign.PhoneUrl(null, csDir)(9) == null
          && sign.Contains(FloorSign.QrSvg(ph(9)!)) && !sign.Contains("현장 입력을 켜면 QR"),
        "층 표지 QR = 읽기 전용 층 화면 주소 (보기 토큰, 입력 토큰 없음 — F12) (" + ph(9) + ")");
    Check(sign.Contains("<p class=\"url\">" + System.Net.WebUtility.HtmlEncode(ph(9)!) + "</p>"), "층 표지 QR 아래 주소 글자 = 읽기 전용 주소");
}
// 서버 연동 휴대폰 주소 (API.md §4 — LAN 서버와 같은 경로, Python 기준 없음): 입력 /?t= · 층 보기 /view?v=&f= · 신고 /view?r=&f=, 끝 '/' 하나, 토큰 이스케이프
{
    const string B = "https://revit-onsite-field.vercel.app/";
    var cph = FloorSign.CloudPhoneUrl(B, "v-tok");
    var csign = FloorSign.Page("시험", at, FloorSign.Floors(csDir, Evaluator.Evaluate(csDir, at), at), cph, null);
    Check(FieldServer.CloudInputUrl(B, "t-1") == "https://revit-onsite-field.vercel.app/?t=t-1"
          && FieldServer.CloudViewUrl(B.TrimEnd('/'), "v 1&x", -1) == "https://revit-onsite-field.vercel.app/view?v=v%201%26x&f=-1"
          && FieldServer.CloudReportUrl(B, "r-1", 9) == "https://revit-onsite-field.vercel.app/view?r=r-1&f=9"
          && cph(9) == "https://revit-onsite-field.vercel.app/view?v=v-tok&f=9" && FloorSign.CloudPhoneUrl(null, "v")(9) == null && FloorSign.CloudPhoneUrl(B, " ")(9) == null
          && csign.Contains(FloorSign.QrSvg(cph(9)!)),
        "서버 휴대폰 주소: 입력·층 보기·신고 경로, 끝 '/'·토큰 이스케이프, 층 표지 QR = 서버 층 보기 (" + cph(9) + ")");
}

// F6 접속 주소 만료 (field_server.py test_token 과 같은 사례): 12 시간 지난 주소 → 401 만료 안내, [새 주소] 뒤 옛 주소 403·새 주소 200
{
    server.TokenIssued = DateTime.Now.AddHours(-FieldServer.DefaultTokenTtlH - 0.1);
    var exp = await Send(csHttp, server.Token, "GET", "/api/model", null, true);
    var oldTok = server.Token;
    server.NewToken();
    var oldR = await Send(csHttp, oldTok, "GET", "/api/model", null, true);
    var newR = await Send(csHttp, server.Token, "GET", "/api/model", null, true);
    Check(exp.Code == 401 && exp.Body.Contains("만료") && oldR.Code == 403 && newR.Code == 200 && server.Token != oldTok, $"접속 주소 만료·새 주소 ({exp.Code}·{oldR.Code}·{newR.Code})");
    Check(server.Host is not null && System.Net.IPAddress.IsLoopback(server.Host) && server.Urls().Count == 0, "묶인 주소 = 127.0.0.1 이면 휴대폰용 주소 없음 (PrimaryUrl 은 127.0.0.1)");
}

// 배포 누락 점검 (총괄 27차: view.html 이 출력 폴더에 없어 /view 500) — web\field 의 html 이 모두 애드인 csproj 복사 대상이고, Release 출력 폴더(배포 원본)에 있는지
{
    var htmls = Directory.GetFiles(Path.Combine(root, "web", "field"), "*.html").Select(Path.GetFileName).ToList();
    var addin = Path.Combine(root, "revit", "RevitOnSite", "RevitOnSite");
    var proj = File.ReadAllText(Path.Combine(addin, "RevitOnSite.csproj"));
    bool inProj = proj.Contains(@"web\field\*.html") || htmls.All(h => proj.Contains(@"web\field\" + h));
    var outDir = Path.Combine(addin, "bin", "Release", "net8.0-windows", "web", "field");
    var missing = htmls.Where(h => !File.Exists(Path.Combine(outDir, h!))).ToList();
    Check(inProj && missing.Count == 0, $"배포 폴더 html: web\\field {htmls.Count}개 ({string.Join("·", htmls)}) 모두 csproj 복사 대상·Release 출력 폴더에 있음"
        + (missing.Count > 0 ? $" — 출력 폴더에 없음: {string.Join(", ", missing)} (Release 빌드 뒤 다시)" : "") + (inProj ? "" : " — csproj 에 없음"));
}

// 감사 A-20 부하: 센서 30개 × 10분 × 60일(약 26만 줄) 합성 자료(tools/make_load_data.py)에서 C# 서버 응답 — 저장 1건 2 s 이내, 첫 /api/model 5 s 이내 [가정].
// (고치기 전 /api/model 10–12 s·저장 10 s.) 합성 자료는 끝에 지움
if (File.Exists(python))
{
    var big = Directory.CreateTempSubdirectory("RevitOnSite_load_").FullName;
    try
    {
        var bd = Path.Combine(big, "data");
        var mk = new ProcessStartInfo(python) { RedirectStandardOutput = true, WorkingDirectory = root };
        foreach (var x in new[] { Path.Combine(root, "tools", "make_load_data.py"), bd }) mk.ArgumentList.Add(x);
        using (var p = Process.Start(mk)!) { p.StandardOutput.ReadToEnd(); p.WaitForExit(); }
        using var bs = new FieldServer(bd) { FixedNow = () => at, PagePath = Path.Combine(root, "web", "field", "index.html") };
        bs.Start(0, System.Net.IPAddress.Loopback);
        var bh = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{bs.Port}"), Timeout = TimeSpan.FromSeconds(120) };
        var sw = Stopwatch.StartNew();
        var bm = await Send(bh, bs.Token, "GET", "/api/model", null, true);
        double first = sw.Elapsed.TotalSeconds;
        sw.Restart();
        var bsave = await Send(bh, bs.Token, "POST", M, J(new { kind = "cylinder", client_id = "load-1", entered_by = "부하", zone_id = "9F-A2", test_date = "2026-12-30", curing = "현장봉함", f1 = 25, f2 = 25, f3 = 25 }), true);
        double save = sw.Elapsed.TotalSeconds;
        bs.Stop();
        Check(bm.Code == 200 && bsave.Code == 200 && first < 5.0 && save < 2.0,
            $"큰 자료(센서 26만 줄) 응답: 첫 /api/model {first:F2} s (< 5) · 저장 1건 {save:F2} s (< 2)");
    }
    finally { try { Directory.Delete(big, true); } catch (IOException) { } }
}

// 다시 켜도 client_id 기억 (CSV 의 client_id 열)
server.Stop();
Check(FloorSign.PhoneUrl(server, csDir)(9) == null, "층 표지 QR: 서버를 끄면 없음");
using var again = new FieldServer(csDir) { FixedNow = () => at };
again.Start(0, System.Net.IPAddress.Loopback);
var againHttp = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{again.Port}"), Timeout = TimeSpan.FromSeconds(120) };
var dup = await Send(againHttp, again.Token, "POST", M, J(new { kind = "cylinder", client_id = "dev1-1", entered_by = "김", zone_id = "9F-A2", test_date = "2026-12-30", curing = "현장봉함", f1 = 1, f2 = 1, f3 = 1 }), true);
Check(dup.Code == 200 && JsonNode.Parse(dup.Body)!["duplicate"]?.GetValue<bool>() == true, "서버를 다시 켜도 같은 client_id 는 중복으로 봄");
again.Stop();

// ── 4. Python evaluate 가 늘어난 CSV 를 C# 과 똑같이 읽는지
Console.WriteLine("4) 저장 뒤 CSV — Python evaluate.py 대조");
if (!File.Exists(python)) Console.WriteLine("  .venv 없음 — 건너뜀");
else
{
    var py = Path.Combine(Path.GetTempPath(), "RevitOnSite_field_py.csv");
    var psi = new ProcessStartInfo(python) { RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = Path.Combine(root, "shoring") };
    foreach (var x in new[] { "evaluate.py", csDir, "--at", at.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture), "--out", py }) psi.ArgumentList.Add(x);
    psi.Environment["PYTHONIOENCODING"] = "utf-8";
    using var p = Process.Start(psi)!;
    var err = p.StandardError.ReadToEnd();
    p.StandardOutput.ReadToEnd();
    p.WaitForExit();
    var cs = Path.Combine(Path.GetTempPath(), "RevitOnSite_field_cs.csv");
    Evaluator.Write(Evaluator.Evaluate(csDir, at), cs);
    Check(p.ExitCode == 0 && File.ReadAllBytes(cs).SequenceEqual(File.ReadAllBytes(py)), "Python 과 바이트 동일" + (p.ExitCode != 0 ? " — Python 오류: " + err : ""));
    var a2 = Evaluator.Evaluate(csDir, at).First(r => r.ZoneId == "9F-A2");
    Console.WriteLine($"  9F-A2 (공시체 30.5·31·32 입력 뒤): {a2.Decision} · {a2.Reason}");
}

// ── 5. 서버 동기화 (tools/server_sync.py 이식, v6 4단계): 가짜 동기화 서버 하나에 Python server_sync 와 C# FieldServer.Sync 를 차례로 붙여
//       같은 휴대폰 입력(공시체·같은 client_id 다시·없는 구역·협력업체 신고) → 저장·중복·거부 수, ack 본문, 올린 스냅샷(gzip·Bearer), 쓴 CSV 줄, 틀린 키 문구를 대조
Console.WriteLine("\n5) 서버 동기화: Python server_sync 와 C# Sync 를 같은 가짜 서버로 대조");
if (File.Exists(python) && File.Exists(Path.Combine(root, "tools", "server_sync.py")))
{
    const string SyncKey = "k-test-sync";
    string SyncDir(string tag)
    {
        var dir = Path.Combine(Path.GetTempPath(), "RevitOnSite_sync_" + tag, "data");   // 끝 폴더 이름이 같아야 model.project 가 같음
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
        File.WriteAllText(Path.Combine(dir, "shore_spec.csv"), "floor,zone_id,type,spec,spacing_x_mm,spacing_y_mm,splices,install_date,entered_by,entered_at,device,note,client_id\n"
            + "9,,파이프 서포트,V4,1200,1200,0,2026-11-20,김,2026-11-20T08:00:00,Windows,,s1\n9,9F-B1,시스템 동바리,SY-1,1500,1500,1,2026-11-20,김,2026-11-20T08:00:00,Windows,,s2\nx,,파이프 서포트,V4,1200,1200,0,2026-11-20,김,,,,s3\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "shore_removed.csv"), "id,zone_id,x,y,removed_date,by,note\nA,9F-A1,1,1,2026-12-30,박,\nB,9F-A1,2,1,2026-12-30,박,\nC,9F-C2,25,10,2026-12-31,박,\nD,9F-Z9,1,1,2026-12-31,박,\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "shores.csv"), "id,zone_id,x,y,field_label\n9F-A1-R01C01,9F-A1,1.2,1.2,\n9F-A1-R01C02,9F-A1,2.4,1.2,\n9F-B1-R01C01,9F-B1,11.2,1.2,\n", new UTF8Encoding(false));
        // v6.2 W-A 감리 직접 확인 대상: 9F-A1 요청 제출 (12-30 판정 근거) — 두 사본에 같은 줄
        Approval.Record(dir, "9F-A1", "요청 제출", "김현장", Approval.RowOf(Evaluator.Evaluate(dir, new DateTime(2026, 12, 31, 8, 0, 0)).First(r => r.ZoneId == "9F-A1")), now: new DateTime(2026, 12, 30, 10, 0, 0));
        Evidence.Add(dir, "9F-A1", "2026-12-30", 4, "박안전", result: "OK");   // 11-11 1-1: 최종 확인 전 해체 전 점검 (계획서 확인은 p-1 이 서버로)
        return dir;
    }
    var syncItems = JsonNode.Parse("""
[{"client_id": "m-1", "route": "measure", "ua": "Mozilla/5.0 (Linux; Android 14)", "received_at": "2026-12-31T08:30:00",
  "body": {"kind": "cylinder", "client_id": "m-1", "entered_by": "김시험", "zone_id": "9F-A2", "test_date": "2026-12-31", "curing": "현장봉함", "f1": 31, "f2": 32, "f3": 33}},
 {"client_id": "m-1", "route": "measure", "ua": "Mozilla/5.0 (Linux; Android 14)", "received_at": "2026-12-31T08:31:00",
  "body": {"kind": "cylinder", "client_id": "m-1", "entered_by": "김시험", "zone_id": "9F-A2", "test_date": "2026-12-31", "curing": "현장봉함", "f1": 31, "f2": 32, "f3": 33}},
 {"client_id": "m-2", "route": "measure", "ua": "Mozilla/5.0 (iPhone)", "received_at": "2026-12-31T08:40:00",
  "body": {"kind": "cylinder", "client_id": "m-2", "entered_by": "김시험", "zone_id": "9F-Z9", "test_date": "2026-12-31", "curing": "현장봉함", "f1": 31, "f2": 32, "f3": 33}},
 {"client_id": "r-1", "route": "report", "ua": "Mozilla/5.0 (Linux; Android 13)", "received_at": "2026-12-31T08:45:00",
  "body": {"zone_id": "9F-B1", "present": "아니오", "client_id": "r-1", "name": "최반장", "company": "오오건설"}},
 {"client_id": "p-1", "route": "supervise", "ua": "Mozilla/5.0 (iPhone)", "received_at": "2026-12-31T08:49:00",
  "body": {"zone_id": "9F-A1", "client_id": "p-1", "name": "이감리", "action": "plan"}},
 {"client_id": "s-1", "route": "supervise", "ua": "Mozilla/5.0 (iPhone)", "received_at": "2026-12-31T08:50:12",
  "body": {"zone_id": "9F-A1", "client_id": "s-1", "name": "이감리", "checks": [true, true, true]}},
 {"client_id": "s-1", "route": "supervise", "ua": "Mozilla/5.0 (iPhone)", "received_at": "2026-12-31T08:51:00",
  "body": {"zone_id": "9F-A1", "client_id": "s-1", "name": "이감리", "checks": [true, true, true]}},
 {"client_id": "s-2", "route": "supervise", "ua": "Mozilla/5.0 (iPhone)", "received_at": "2026-12-31T08:52:00",
  "body": {"zone_id": "9F-C2", "client_id": "s-2", "name": "이감리", "checks": [true, true, true]}},
 {"client_id": "j-1", "route": "supervise", "ua": "Mozilla/5.0 (iPhone)", "received_at": "2026-12-31T08:53:00",
  "body": {"zone_id": "9F-A2", "client_id": "j-1", "name": "이감리", "action": "reject", "reason": "수평연결재 보강 사진 필요"}}]
""")!.AsArray();
    // 가짜 동기화 서버 (server/vercel/API.md §3): pull = 아직 ack 안 된 입력 전부(more false), ack = 그 입력 지움, push = 받은 바이트. 키가 틀리면 403
    var mockLog = new List<(string Method, string Path, string Auth, string Type, byte[] Body)>();
    var pending = new List<JsonNode>();
    var mock = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    mock.Start();
    var mockUrl = $"http://127.0.0.1:{((System.Net.IPEndPoint)mock.LocalEndpoint).Port}";
    var mockCts = new CancellationTokenSource();
    void MockServe(System.Net.Sockets.TcpClient c)
    {
        using (c)
        {
            var s = c.GetStream();
            var head = new List<byte>();
            int ch;
            while ((ch = s.ReadByte()) >= 0)
            {
                head.Add((byte)ch);
                if (head.Count >= 4 && head[^4] == 13 && head[^3] == 10 && head[^2] == 13 && head[^1] == 10) break;
            }
            var lines = Encoding.UTF8.GetString(head.ToArray()).Split("\r\n");
            var first = lines[0].Split(' ');
            var hdr = lines.Skip(1).Where(l => l.Contains(':')).GroupBy(l => l[..l.IndexOf(':')].Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Last()[(g.Last().IndexOf(':') + 1)..].Trim());
            int len = hdr.TryGetValue("content-length", out var cl) ? int.Parse(cl) : 0;
            var body = new byte[len];
            for (int got = 0; got < len;) { int n = s.Read(body, got, len - got); if (n <= 0) break; got += n; }
            var type = hdr.GetValueOrDefault("content-type", "");
            if (type.StartsWith("application/gzip"))
            {
                using var gz = new System.IO.Compression.GZipStream(new MemoryStream(body), System.IO.Compression.CompressionMode.Decompress);
                using var ms = new MemoryStream();
                gz.CopyTo(ms);
                body = ms.ToArray();
            }
            int code = 200;
            JsonNode resp;
            lock (mockLog)
            {
                mockLog.Add((first[0], first[1], hdr.GetValueOrDefault("authorization", ""), type, body));
                if (hdr.GetValueOrDefault("authorization") != "Bearer " + SyncKey) { code = 403; resp = new JsonObject { ["error"] = "동기화 키가 맞지 않습니다" }; }
                else if (first[1] == "/api/sync/pull") resp = new JsonObject { ["ok"] = true, ["items"] = new JsonArray(pending.Select(q => q.DeepClone()).ToArray()), ["more"] = false };
                else if (first[1] == "/api/sync/ack")
                {
                    var acked = JsonNode.Parse(body)!["items"]!.AsArray().Select(q => (string)q!["client_id"]!).ToHashSet();
                    int before = pending.Count;
                    pending.RemoveAll(q => acked.Contains((string)q["client_id"]!));
                    resp = new JsonObject { ["ok"] = true, ["acked"] = before - pending.Count };
                }
                else if (first[1] == "/api/sync/push") resp = new JsonObject { ["ok"] = true, ["bytes"] = len, ["floors"] = JsonNode.Parse(body)!["floors"]!.AsObject().Count };
                else { code = 404; resp = new JsonObject { ["error"] = "없는 주소" }; }
            }
            var payload = Encoding.UTF8.GetBytes(resp.ToJsonString());
            s.Write(Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {(code == 200 ? "OK" : code == 403 ? "Forbidden" : "Not Found")}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n"));
            s.Write(payload);
        }
    }
    _ = Task.Run(async () =>
    {
        while (!mockCts.IsCancellationRequested)
        {
            System.Net.Sockets.TcpClient c;
            try { c = await mock.AcceptTcpClientAsync(mockCts.Token); } catch (Exception) { break; }
            _ = Task.Run(() => { try { MockServe(c); } catch (Exception ex) { Console.WriteLine("  가짜 서버 오류: " + ex.Message); } });
        }
    });
    List<(string Method, string Path, string Auth, string Type, byte[] Body)> Run(Action client)
    {
        lock (mockLog) { mockLog.Clear(); pending.Clear(); pending.AddRange(syncItems.Select(q => q!.DeepClone())); }
        client();
        lock (mockLog) return mockLog.ToList();
    }
    string pySync = SyncDir("py"), csSync = SyncDir("cs");
    string pyOut = "";
    var pyLog = Run(() =>
    {
        var psi = new ProcessStartInfo(python) { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, WorkingDirectory = root };
        foreach (var x in new[] { "-c", "import sys, json\nsys.path.insert(0, 'tools')\nimport field_server as fs, server_sync as ss\nfrom datetime import datetime\n"
            + "d, url, key = sys.argv[1:4]\nAT = datetime(2026, 12, 31, 9)\nst = fs.Store(d, AT)\nr = ss.pull(st, url, key, None)\n"
            + "st.at = AT   # C# Pull 은 끝나면 원래 '지금'(FixedNow)으로 되돌림 — 같은 조건으로\np = ss.push(st, url, key)\n"
            + "print(json.dumps({'saved': r['saved'], 'duplicate': r['duplicate'], 'errors': r['errors'], 'floors': p['floors']}, ensure_ascii=False))",
            pySync, mockUrl, SyncKey }) psi.ArgumentList.Add(x);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        using var pp = Process.Start(psi)!;
        pyOut = pp.StandardOutput.ReadToEnd();
        var perr = pp.StandardError.ReadToEnd();
        pp.WaitForExit();
        if (pp.ExitCode != 0) Console.WriteLine("  Python 오류: " + perr);
    });
    SyncResult? csRes = null;
    var csLog = Run(() =>
    {
        using var ss = new FieldServer(csSync) { FixedNow = () => at };
        csRes = ss.Sync(mockUrl, SyncKey);
    });
    var pyRes = JsonNode.Parse(pyOut.Trim().Split('\n')[^1])!;
    bool csSnap0Requested() => JsonNode.Parse(csLog[2].Body)?["check"]?["requested"]?["9F-A1"]?["actor"]?.GetValue<string>() == "김현장";
    Check(csRes != null && csRes.Saved == (int)pyRes["saved"]! && csRes.Duplicate == (int)pyRes["duplicate"]! && csRes.Errors.Count == pyRes["errors"]!.AsObject().Count
          && csRes.Saved == 5 && csRes.Duplicate == 2 && csRes.Errors.Keys.SequenceEqual(new[] { "m-2", "s-2" }) && csRes.Floors == (int)pyRes["floors"]!,
        $"저장·중복·거부 수 (C# {csRes?.Saved}·{csRes?.Duplicate}·{csRes?.Errors.Count} / Python {pyRes["saved"]}·{pyRes["duplicate"]}·{pyRes["errors"]!.AsObject().Count}) · 층 {csRes?.Floors}");
    Check(csRes!.Errors["m-2"] == (string)pyRes["errors"]!["m-2"]! && csRes.Errors["s-2"] == (string)pyRes["errors"]!["s-2"]!, "거부 사유 같은 글: " + csRes.Errors["m-2"] + " / " + csRes.Errors["s-2"]);
    Check(new[] { pySync, csSync }.All(d => Approval.SuperviseRejectList(d).TryGetValue("9F-A2", out var rj) && rj.Reason == "감리 보완 요청 — 수평연결재 보강 사진 필요")
          && Evaluator.Read(csSync, Evidence.Ledger).Any(r => r["zone_id"] == "9F-A1" && r["stage"] == "2" && r["result"] == Evidence.PlanOk && r["doc_no"] == "p-1"),
        "1-2 감리 계획서 확인 → 원장 2 단계 · 보완 요청 → supervise_rejects (C#·Python)");
    var sup = Approval.ReadLog(csSync).Where(r => r["action"] == "감리 승인 기록").ToList();   // W-A: 감리 직접 확인 = 출처 '감리 직접', 서명 일시 = 서버가 받은 시각, 사슬 정상
    Check(sup.Count == 1 && sup[0]["zone_id"] == "9F-A1" && sup[0]["source"] == "감리 직접" && sup[0]["approver"] == "이감리" && sup[0]["signed_at"] == "2026-12-31T08:50"
          && Approval.Verify(csSync).Ok && csSnap0Requested(), $"감리 직접 확인 → 승인 기록 1줄 ({string.Join(" · ", sup.Select(r => $"{r["zone_id"]} {r["source"]} {r["approver"]} {r["signed_at"]}"))}) · 사슬 정상 · check.requested 9F-A1");
    Check(string.Join(" ", csLog.Select(l => l.Method + " " + l.Path)) == string.Join(" ", pyLog.Select(l => l.Method + " " + l.Path))
          && string.Join(" ", csLog.Select(l => l.Path)) == "/api/sync/pull /api/sync/ack /api/sync/push", "순서 pull → ack → push: " + string.Join(" ", csLog.Select(l => l.Path)));
    Check(csLog.All(l => l.Auth == "Bearer " + SyncKey) && csLog.Last().Type.StartsWith("application/gzip") && pyLog.Last().Type.StartsWith("application/gzip"), "Bearer 키 · 스냅샷 gzip");
    var ackD = new List<string>();
    Diff(JsonNode.Parse(pyLog[1].Body), JsonNode.Parse(csLog[1].Body), "ack", ackD);
    Check(ackD.Count == 0, "ack 본문 같음 (m-1·m-1·m-2 error·r-1·s-1·s-1·s-2 error)" + (ackD.Count > 0 ? ": " + string.Join(" | ", ackD) : ""));
    var pySnap = JsonNode.Parse(pyLog[2].Body)!.AsObject();
    var csSnap = JsonNode.Parse(csLog[2].Body)!.AsObject();
    Check(pySnap.Remove("pushed_at") && csSnap.Remove("pushed_at"), "스냅샷 pushed_at 있음");
    // recent 순서 = entered_at(받은 실제 시각, 초) 내림차순 — Python·C# 실행이 몇 초 차이라 같은 초 묶임이 달라질 수 있음 → client_id 순으로 맞춘 뒤 비교 (순서 규칙 자체는 3절 /api/recent 대조가 봄)
    foreach (var sn in new[] { pySnap, csSnap }) sn["recent"] = new JsonArray(sn["recent"]!.AsArray().OrderBy(r => (string?)r!["client_id"], StringComparer.Ordinal).Select(r => r!.DeepClone()).ToArray());
    var snapD = new List<string>();
    Diff(pySnap, csSnap, "snapshot", snapD);
    Check(snapD.Count == 0, $"스냅샷 같음 (model·floors·recent·check, {csLog[2].Body.Length:N0} B)" + (snapD.Count > 0 ? ": " + string.Join(" | ", snapD) : ""));
    var nine = csSnap["floors"]!["9"]!;
    Check(nine["shore_spec"]!.AsArray().Count == 2 && nine["shore_removed"]!.ToJsonString() == "[{\"date\":\"2026-12-31\",\"zone_id\":\"9F-C2\",\"count\":1},{\"date\":\"2026-12-30\",\"zone_id\":\"9F-A1\",\"count\":2}]"
          && csSnap["model"]!["floors"]!.AsArray().First(f => (int)f!["floor"]! == 9)!["shore_spec"]!.AsArray().Count == 2
          && csSnap["check"]!["seen"]!.AsArray().Any(c => (string?)c == "r-1") && csSnap["check"]!["shores"]!["9F-A1"]!.AsArray().Count == 2,
        "층마다 shore_spec·shore_removed (최근 날짜부터, 없는 구역 뺌) · check.seen·shores");
    var csvD = new List<string>();
    foreach (var f in Directory.GetFiles(pySync, "*.csv").Select(Path.GetFileName))
    {
        var a = Evaluator.Read(pySync, f!).Select(r => string.Join("|", r.Where(kv => kv.Key != "entered_at").Select(kv => kv.Key + "=" + kv.Value))).ToList();
        var c = Evaluator.Read(csSync, f!).Select(r => string.Join("|", r.Where(kv => kv.Key != "entered_at").Select(kv => kv.Key + "=" + kv.Value))).ToList();
        if (!a.SequenceEqual(c)) csvD.Add(f!);
    }
    Check(csvD.Count == 0 && Evaluator.Read(csSync, "shore_checks.csv").Any(r => r["client_id"] == "r-1" && r["timestamp"] == "2026-12-31T08:45")
          && Evaluator.Read(csSync, "cylinders.csv").Count(r => r.GetValueOrDefault("client_id") == "m-1") == 1, "CSV 줄 같음 (entered_at 뺌) · 신고 시각 = 서버가 받은 시각 · m-1 한 줄" + (csvD.Count > 0 ? ": " + string.Join(", ", csvD) : ""));
    string bad = "";
    try { FieldServer.Call(mockUrl, "wrong", "/api/sync/pull"); } catch (SyncException x) { bad = x.Message; }
    Check(bad == "서버 403: 동기화 키가 맞지 않습니다 (/api/sync/pull)", "틀린 키 → " + bad);
    string down = "";
    try { FieldServer.Call("http://127.0.0.1:9", SyncKey, "/api/sync/pull"); } catch (SyncException x) { down = x.Message; }
    Check(down.StartsWith("서버에 연결하지 못했습니다 (") && down.EndsWith(") — 인터넷과 서버 주소를 확인하세요"), "연결 안 됨 → " + down);
    mockCts.Cancel();
    mock.Stop();
    if (fails == 0)
        foreach (var tg in new[] { "py", "cs" })
            try { Directory.Delete(Path.Combine(Path.GetTempPath(), "RevitOnSite_sync_" + tg), true); } catch (IOException) { }
}
else Check(false, "서버 동기화 대조 — .venv 또는 tools/server_sync.py 없음");

// ── 6. W7 CSV 이어 쓰기 잠금: C# 두 작업 + Python 프로세스 하나가 같은 파일에 동시에 → 줄 손실 0, 남의 잠금이 안 풀리면 Bad·파일 그대로
Console.WriteLine("\n6) CSV 이어 쓰기 잠금 (W7)");
{
    var ld = Directory.CreateTempSubdirectory("RevitOnSite_lock_").FullName;
    var lp = Path.Combine(ld, "lock_test.csv");
    try
    {
        Process? pyLock = null;
        if (File.Exists(python))
        {
            var lpsi = new ProcessStartInfo(python) { RedirectStandardError = true, WorkingDirectory = root };
            foreach (var x in new[] { "-c", "import sys\nsys.path.insert(0, 'tools')\nimport field_server as fs\nfor i in range(100): fs.append_rows(sys.argv[1], ['zone_id', 'test_date'], [{'zone_id': f'p{i}', 'test_date': '2026-12-30'}])", lp }) lpsi.ArgumentList.Add(x);
            pyLock = Process.Start(lpsi);
        }
        var tasks = new[] { "a", "b" }.Select(tag => Task.Run(() => { for (int i = 0; i < 100; i++) FieldServer.AppendRows(lp, new[] { "zone_id", "test_date" }, new() { new() { ["zone_id"] = tag + i, ["test_date"] = "2026-12-30" } }); })).ToArray();
        Task.WaitAll(tasks);
        pyLock?.WaitForExit();
        var lines = Evaluator.Read(ld, "lock_test.csv").Select(r => r["zone_id"]).ToList();
        int want = 200 + (pyLock != null ? 100 : 0);
        Check(lines.Count == want && lines.Distinct().Count() == want && !File.Exists(lp + ".lock"), $"C# 2 작업{(pyLock != null ? " + Python 1 프로세스" : "")} 동시 이어 쓰기 {lines.Count}/{want}줄, 잠금 파일 남지 않음");
        File.WriteAllText(lp + ".lock", "");
        var (oldTries, oldWait) = (FieldServer.LockTries, FieldServer.LockWaitMs);
        (FieldServer.LockTries, FieldServer.LockWaitMs) = (3, 10);
        string lockMsg = "";
        try { FieldServer.AppendRows(lp, new[] { "zone_id", "test_date" }, new() { new() { ["zone_id"] = "x", ["test_date"] = "2026-12-30" } }); }
        catch (Exception x) { lockMsg = x.Message; }
        File.SetLastWriteTime(lp + ".lock", DateTime.Now.AddMinutes(-1));
        FieldServer.AppendRows(lp, new[] { "zone_id", "test_date" }, new() { new() { ["zone_id"] = "y", ["test_date"] = "2026-12-30" } });
        (FieldServer.LockTries, FieldServer.LockWaitMs) = (oldTries, oldWait);
        Check(lockMsg.Contains("다른 PC·프로그램이 쓰는 중") && Evaluator.Read(ld, "lock_test.csv").Count == want + 1, "남의 잠금 → 거부·파일 그대로, 30 초 넘은 잠금은 지우고 씀: " + lockMsg);
    }
    finally { Directory.Delete(ld, true); }
}

Console.WriteLine(fails == 0 ? "\n현장 입력 시험 통과" : $"\n현장 입력 시험 실패 {fails}건");
// 시험 잔여물 정리 (사용자 지시: 체크용 파일을 쌓아 두지 않음) — 통과하면 지우고, 실패하면 비교용으로 남김. --qr-html 로 준 파일은 그대로
if (fails == 0)
    foreach (var t in new[] { "py", "cs" })
        try
        {
            var d = Path.Combine(Path.GetTempPath(), "RevitOnSite_field_" + t);
            if (Directory.Exists(d)) Directory.Delete(d, true);
            File.Delete(d + ".csv");
            if (Arg("--qr-html") is null) File.Delete(qrHtml);
        }
        catch (IOException) { }
return fails == 0 ? 0 : 1;

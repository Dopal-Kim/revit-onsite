// 현장 실측값 입력 서버 — tools/field_server.py (기준본, 총괄 작성) 의 1:1 C# 이식. API·검사 문구·CSV 쓰기 방식이 같다 (docs/field_input_spec.md §3).
//   휴대폰(web/field/index.html) → POST /api/measure → 데이터 폴더 CSV 에 한 줄 추가 → Evaluator 로 그 구역 판정 다시 계산.
//   Revit 참조 없음: 애드인(FieldInput.cs)이 켜고 끄고, 저장 뒤 Saved 이벤트를 ExternalEvent 로 넘겨 Revit 매개변수·4D 를 갱신 (이 서버 스레드는 Revit API 를 부르지 않음).
//   HttpListener 는 관리자 URL 예약(netsh http add urlacl)이 필요해 TcpListener 위에 HTTP/1.1 최소 구현 (요청마다 연결 닫음 = Python BaseHTTPRequestHandler 와 같음).
//   Python 과 다른 점 (애드인 쪽 사정): 포트가 쓰이면 다음 번호(+9), 같은 LAN 주소를 여러 개 알려 줌, Saved 이벤트, 빈 줄은 다시 쓸 때 버림.
//   일치 시험: RevitOnSite.Field.Check (같은 요청을 Python 서버와 이 서버에 보내 응답·CSV 대조)
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Row = System.Collections.Generic.Dictionary<string, string?>;

namespace RevitOnSite.Engine;

/// <summary>저장된 입력 한 건 (애드인이 판정 갱신·센서 배치에 씀). Target = 구역 ID 또는 센서 ID, Floor = 센서 층 (공시체·반발경도는 0)</summary>
public sealed record FieldEntry(string Kind, string File, string Target, int Floor, int Rows);

public sealed partial class FieldServer : IDisposable
{
    public const int DefaultPort = 8765;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly string[] Common = { "entered_by", "entered_at", "device", "note", "client_id" };   // 모든 파일 기존 열 뒤에 붙는 공통 열
    /// <summary>공시체 채취 정보 (선택, F2) — 판정엔 안 씀, 값이 있을 때만 열을 더함 (field_server.py SAMPLE_COLS)</summary>
    public static readonly string[] SampleCols = { "pour_id", "sampled_at", "supplier", "truck_no", "storage_zone", "photo", Photos.ShaCol };
    /// <summary>v6.2 W1 타설 묶음 (선택, field_server.py POUR_COLS) — 같이 친 구역 묶음, 공시체 한 번으로 묶음 전체 (Evaluator.CylRows)</summary>
    public static readonly string[] PourCols = { "pour_id", "supervisor_present", "watcher" };   // 11-12 Revit #7 타설 입회 감리(예/아니오)·감시자 (선택, quality.watch_missing)
    /// <summary>kind → (화면 이름, 파일, 기존 열, 판정에서의 역할). 순서 = Python KINDS</summary>
    public static readonly Dictionary<string, (string Label, string File, string[] Cols, string Role)> Kinds = new()
    {
        ["cylinder"] = ("공시체", "cylinders.csv", new[] { "zone_id", "test_date", "curing", "f1", "f2", "f3" }, "기준 근거"),
        ["sensor_temp"] = ("온도 센서", "sensors.csv", new[] { "sensor_id", "floor", "x", "y", "timestamp", "temp_c" }, "위치 근거 (등가재령 → 강도 IDW)"),
        ["sensor_strength"] = ("강도 센서", "sensor_strength.csv", new[] { "sensor_id", "floor", "x", "y", "timestamp", "mpa" }, "위치 근거 (강도 IDW)"),
        ["rebound"] = ("반발경도", "rebound.csv", new[] { "zone_id", "x", "y", "test_date", "r_value" }, "참고 (판정 미사용)"),
        ["shore_check"] = ("동바리 확인", "shore_checks.csv", new[] { "zone_id", "timestamp", "present", "actor", "photo" },
            "타설 전 게이트 (아래층 동바리가 서 있는지, pour_gate.py)"),
        ["after_check"] = ("해체 후 점검", AfterCheck.File, new[] { "zone_id", "date", "item_code", "result", "by", "photo" },
            "참고 — 해체 후 점검 (가이드라인 4.3, 판정 미사용, 근거 원장 8 결과보고와 묶음)"),
        ["pour"] = ("타설", "pours.csv", new[] { "zone_id", "pour_date", "pour_time", "mix", "fck" },
            "타설일·배합·fck = 강도 계산 시작 (해체 판정·공정) — Revit [타설 등록]"),
        ["shore_spec"] = ("동바리 사양", ShoreInput.Spec, ShoreInput.SpecCols,
            "참고 — 동바리 종류·규격·간격·이음·설치일 (v6: 공정표 대신 직접 입력, 판정 미사용) — Revit [동바리 사양]"),
        ["shore_inspect"] = ("동바리 점검", ShoreInspect.File, ShoreInspect.BaseCols,
            "설치 검측 NG 가 풀리지 않으면 해체 요청 불가·타설 보류 (F16 규칙 ⑦), 다른 단계는 기록·Revit 빨강 표시"),
    };
    static readonly Dictionary<string, string> SensorValue = new() { ["sensor_temp"] = "temp_c", ["sensor_strength"] = "mpa" };
    static readonly string[] Curings = ShoringEngine.FieldCurings;
    static readonly Dictionary<string, (int Lo, int Hi)> Limits = new() { ["mpa"] = (0, 100), ["temp_c"] = (-30, 90), ["r_value"] = (10, 70) };   // 강도·온도 [확정 spec §3], 반발경도 [가정]
    const int MaxBody = 12_000_000, MaxReadings = 5000;   // 본문 상한 = 사진 8 MB [가정] 의 base64(×4/3) + JSON (field_server.py MAX_BODY)
    static readonly Regex Ts = new(@"^\s*([0-9]{4})[-/.]([0-9]{1,2})[-/.]([0-9]{1,2})[T ]([0-9]{1,2}):([0-9]{2})(?::[0-9]{2}(?:\.[0-9]+)?)?\s*$");   // ASCII 숫자만 (전각 ０８ 거부 — 코드 검토 2차, field_server.py 같음)
    static readonly Regex DayRe = new(@"^\s*([0-9]{4})[-/.]([0-9]{1,2})[-/.]([0-9]{1,2})\s*$");
    static readonly Regex IdRe = new(@"^[0-9A-Za-z가-힣_\-]{1,20}$");
    static readonly Regex ClientRe = new(@"^[0-9A-Za-z_\-]{1,80}$");
    static readonly JsonSerializerOptions JsonOut = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>사용자에게 그대로 보여 줄 한국어 오류 (무엇을 어떻게 고칠지) → 400</summary>
    sealed class Bad(string message) : Exception(message);

    public string Folder { get; }
    public string Token { get; private set; } = NewTokenText();
    /// <summary>접속 주소(토큰)를 만든 시각 — TokenTtlH 시간 뒤 만료 (실제 시계, 데모 고정 시각과 무관). 시험에서 앞당길 수 있게 공개</summary>
    public DateTime TokenIssued { get; set; } = DateTime.Now;
    /// <summary>[가정] 접속 주소 유효 시간 — 지나면 401 "만료", PC 현장 입력 창에서 [새 주소] (감사 A-16, F6, field_server.py TOKEN_TTL_H)</summary>
    public const double DefaultTokenTtlH = 12.0;
    public double TokenTtlH { get; set; } = DefaultTokenTtlH;
    /// <summary>바인딩된 주소 (Any = 모든 망). 기본 = 첫 LAN 주소 하나 (감사 A-19)</summary>
    public IPAddress? Host { get; private set; }
    public int Port { get; private set; }
    static string NewTokenText() => RandomNumberGenerator.GetString("abcdefghjkmnpqrstuvwxyz23456789", 16);
    /// <summary>접속 주소(토큰)를 새로 — 만료 시각도 지금부터 다시. 옛 QR 은 403 (field_server.py new_token)</summary>
    public void NewToken() { Token = NewTokenText(); TokenIssued = DateTime.Now; }
    public DateTime TokenExpires => TokenIssued.AddHours(TokenTtlH);
    /// <summary>읽기 전용 보기 토큰 (F12, field_server.py view_token) — 데이터 폴더 view_token.txt 에 고정(만료 없음, 인쇄한 층 표지 QR 이 서버를 다시 켜도 열림), 입력 토큰과 다름</summary>
    public string ViewToken { get; }
    public const string ViewFile = "view_token.txt";
    /// <summary>협력업체 해체 신고 토큰 (F14, field_server.py report_token) — report_token.txt 에 고정, 보기·입력 토큰과 다름. 현장 입력 창에서 반장에게 따로 주는 QR</summary>
    public string ReportToken { get; }
    public const string ReportFile = "report_token.txt";
    /// <summary>신고 줄에만 값이 있는 shore_checks.csv 선택 열 (field_server.py REPORT_COLS)</summary>
    static readonly string[] ReportCols = { "source", "company", Photos.ShaCol };
    /// <summary>사진을 올린 줄에만 값이 있는 선택 열 (동바리 확인·해체 후 점검, F15 — field_server.py PHOTO_COLS)</summary>
    static readonly string[] PhotoCols = { Photos.ShaCol };

    /// <summary>field_server.py view_token: 데이터 폴더의 고정 토큰 (보기 view_token.txt · 신고 report_token.txt) — 파일에 있으면 그 값, 없거나 이상하면 새로 만들어 저장 (shoring\sample 폴더에는 쓰지 않음)</summary>
    public static string ViewTokenOf(string folder, string file = ViewFile)
    {
        var p = Path.Combine(folder, file);
        string t = "";
        try { t = File.Exists(p) ? Py.ReadText(p, new UTF8Encoding(false)).Trim() : ""; } catch (IOException) { }
        if (!Regex.IsMatch(t, @"^[0-9A-Za-z_\-]{12,64}$"))
        {
            t = NewTokenText();
            if (!Evaluator.IsSample(folder))
                try { File.WriteAllText(p, t + "\n", new UTF8Encoding(false)); } catch (IOException) { }
        }
        return t;
    }

    /// <summary>층 표지 QR 의 읽기 전용 주소 (F12): 입력 주소의 호스트·포트 + /view?v=보기토큰&amp;f=층</summary>
    public string ViewUrl(int floor)
    {
        var u = PrimaryUrl();
        int q = u.IndexOf("/?", StringComparison.Ordinal);
        return (q >= 0 ? u[..q] : u) + $"/view?v={ViewToken}&f={floor.ToString(Inv)}";
    }

    /// <summary>협력업체 해체 신고 주소 (F14): 입력 주소의 호스트·포트 + /view?r=신고토큰&amp;f=층 — 층 화면에 구역마다 [뺐음]·[다시 세움]</summary>
    public string ReportUrl(int floor)
    {
        var u = PrimaryUrl();
        int q = u.IndexOf("/?", StringComparison.Ordinal);
        return (q >= 0 ? u[..q] : u) + $"/view?r={ReportToken}&f={floor.ToString(Inv)}";
    }

    /// <summary>GET / 가 돌려줄 입력 화면 파일 (web/field/index.html). 요청마다 읽음</summary>
    public string? PagePath { get; set; }
    /// <summary>null 이 아니면 그 시각을 "지금"으로 (Python --at, 데모). 서버 스레드에서 불리므로 Revit API 금지</summary>
    public Func<DateTime?> FixedNow { get; set; } = () => null;
    /// <summary>PRJ_DefaultFck — 주면 pours.csv fck 와 다를 때 구역 경고 (evaluate.input_checks, 감사 A-29). null = 점검 안 함 (field_server.py --fck 없음과 같음)</summary>
    public double? DefaultFck { get; set; }
    /// <summary>그 전 판정 시각 — 주면 알림 '상태 바뀜' 기준 (field_server.py --prev). 없으면 데이터 폴더 eval_log.csv 의 마지막 다른 판정</summary>
    public Func<DateTime?> PrevAt { get; set; } = () => null;
    public double CureMin { get; set; } = 5.0;
    public event Action<FieldEntry>? Saved;
    public event Action<string>? Log;

    TcpListener? listener;
    CancellationTokenSource? cts;
    readonly object gate = new();
    readonly HashSet<string> seen;

    public FieldServer(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            throw new DirectoryNotFoundException($"데이터 폴더가 없습니다: {folder} — [프로젝트 설정]에서 데이터 폴더를 고르세요.");
        Folder = folder;
        ViewToken = ViewTokenOf(folder);
        ReportToken = ViewTokenOf(folder, ReportFile);
        seen = Kinds.Values.SelectMany(k => Evaluator.Read(Folder, k.File)).Select(r => r.GetValueOrDefault("client_id")).Where(c => !string.IsNullOrEmpty(c)).ToHashSet()!;
    }

    public bool Running => listener != null;

    /// <summary>port 가 쓰이고 있으면 다음 번호 (+9 까지). port 0 = 아무 빈 번호 (시험용). host 기본 = 이 PC 첫 LAN 주소 하나 (없으면 127.0.0.1) —
    /// 공용망·테더링에 같이 열리지 않게 (감사 A-19 · F6), 모든 망은 IPAddress.Any 를 넘김. 실패하면 SocketException</summary>
    public void Start(int port = DefaultPort, IPAddress? host = null)
    {
        if (listener != null) return;
        host ??= LanAddresses().FirstOrDefault() ?? IPAddress.Loopback;
        Host = host;
        for (int p = port, last = port == 0 ? 0 : port + 9; ; p++)
        {
            var l = new TcpListener(host, p);
            try { l.Start(); listener = l; Port = ((IPEndPoint)l.LocalEndpoint).Port; break; }
            catch (SocketException) when (p < last) { }
        }
        cts = new CancellationTokenSource();
        var (lis, ct) = (listener, cts.Token);
        _ = Task.Run(() => AcceptLoop(lis, ct));
        // 미리 한 번 계산 (감사 A-20): 큰 자료(센서 26만 줄)에서 첫 /api/model 3 s → 휴대폰이 QR 을 찍기 전에 센서 색인·판정 캐시를 채워 둠. 실패는 기록만
        _ = Task.Run(() => { try { lock (gate) Model(); } catch (Exception ex) { Log?.Invoke("미리 계산 건너뜀: " + ex.Message); } });
    }

    public void Stop()
    {
        cts?.Cancel();
        listener?.Stop();
        listener = null;
    }

    public void Dispose() => Stop();

    /// <summary>휴대폰이 열 주소 (같은 LAN 의 이 PC IPv4 마다). 첫 번째 = 기본 게이트웨이가 있는 어댑터 (Wi-Fi 우선)</summary>
    public IReadOnlyList<string> Urls() => (Host is null || Host.Equals(IPAddress.Any) ? LanAddresses() : new List<IPAddress> { Host })
        .Where(a => !IPAddress.IsLoopback(a)).Select(a => $"http://{a}:{Port}/?t={Token}").ToList();   // 묶인 주소만 (모든 망이면 LAN 주소 전부)

    /// <summary>QR 에 넣는 주소 = 첫 망 주소, 망이 없으면 이 PC 주소 (현장 입력 창·층 표지가 같은 주소를 씀)</summary>
    public string PrimaryUrl() => Urls().FirstOrDefault() ?? $"http://127.0.0.1:{Port}/?t={Token}";

    public static List<IPAddress> LanAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
        .Select(n => (P: n.GetIPProperties(), Wifi: n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
        .SelectMany(n => n.P.UnicastAddresses.Select(a => (a.Address, Gw: n.P.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)), n.Wifi)))
        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address) && !a.Address.ToString().StartsWith("169.254."))
        .OrderByDescending(a => a.Gw).ThenByDescending(a => a.Wifi).Select(a => a.Address).Distinct().ToList();

    // ── HTTP
    async Task AcceptLoop(TcpListener lis, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await lis.AcceptTcpClientAsync(ct); }
            catch (Exception) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { Log?.Invoke("연결 받기 오류: " + ex.Message); await Task.Delay(200, CancellationToken.None); continue; }
            _ = Task.Run(() => Serve(c));
        }
    }

    void Serve(TcpClient c)
    {
        using (c)
        {
            try
            {
                c.ReceiveTimeout = c.SendTimeout = 15000;
                var s = c.GetStream();
                var (code, type, content, entry) = Handle(s);
                var head = $"HTTP/1.1 {code} {code switch { 200 => "OK", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found", 413 => "Payload Too Large", 501 => "Not Implemented", _ => "Internal Server Error" }}\r\n" +
                    $"Server: RevitOnSiteField/1\r\nContent-Type: {type}\r\nContent-Length: {content.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
                s.Write(Encoding.ASCII.GetBytes(head));
                s.Write(content);
                s.Flush();
                if (entry != null)
                {
                    Log?.Invoke($"저장: {Kinds[entry.Kind].Label} {entry.Target} {entry.Rows}줄");
                    try { Saved?.Invoke(entry); } catch (Exception ex) { Log?.Invoke("Saved 처리 오류: " + ex.Message); }
                }
            }
            catch (Exception ex) { Log?.Invoke("요청 처리 오류: " + ex.Message); }
        }
    }

    (int Code, string Type, byte[] Content, FieldEntry? Entry) Handle(NetworkStream s)
    {
        // 머리글 (16 KB 까지)
        var buf = new List<byte>();
        var one = new byte[8192];
        int end;
        while ((end = HeaderEnd(buf)) < 0)
        {
            if (buf.Count > 16384) return (400, "text/plain", Array.Empty<byte>(), null);
            int n = s.Read(one, 0, one.Length);
            if (n == 0) throw new IOException("연결이 끊김");
            buf.AddRange(one.AsSpan(0, n).ToArray());
        }
        var lines = Encoding.Latin1.GetString(buf.GetRange(0, end).ToArray()).Split("\r\n");
        var first = lines[0].Split(' ');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in lines.Skip(1))
            if (l.IndexOf(':') is int i and > 0) headers[l[..i].Trim()] = l[(i + 1)..].Trim();
        string method = first[0], target = first.Length > 1 ? first[1] : "/";
        int q = target.IndexOf('?');
        string path = q < 0 ? target : target[..q];
        var query = new Dictionary<string, string>();   // parse_qs: 빈 값은 버리고 같은 이름은 첫 값
        foreach (var kv in (q < 0 ? "" : target[(q + 1)..]).Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int e = kv.IndexOf('=');
            static string Dec(string x) => Uri.UnescapeDataString(x.Replace('+', ' '));
            if (e > 0 && e < kv.Length - 1) query.TryAdd(Dec(kv[..e]), Dec(kv[(e + 1)..]));
        }
        (int, string, byte[], FieldEntry?) J(int code, JsonNode n, FieldEntry? entry = null) => (code, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(n.ToJsonString(JsonOut)), entry);
        JsonObject Err(string m) => new() { ["ok"] = false, ["error"] = m };

        if (method is not ("GET" or "POST")) return (501, "text/plain", Array.Empty<byte>(), null);
        // R7 (회귀 2회차): 거부(403·401·404)도 본문을 다 읽은 뒤 답 — 안 읽고 닫으면 보낸 쪽은 403 대신 연결 끊김(RST) (field_server.py 와 같음)
        int len = headers.TryGetValue("Content-Length", out var cl) && int.TryParse(cl, NumberStyles.Integer, Inv, out var cv) ? cv : -1;
        if (!headers.ContainsKey("Content-Length")) len = 0;
        var body = Array.Empty<byte>();
        if (method == "POST" && len > 0 && len <= MaxBody)
        {
            body = new byte[len];
            int have = Math.Min(len, buf.Count - (end + 4));
            buf.CopyTo(end + 4, body, 0, have);
            while (have < len)
            {
                int n = s.Read(body, have, len - have);
                if (n == 0) throw new IOException("본문이 덜 옴");
                have += n;
            }
        }
        if (method == "GET" && path == "/favicon.ico") return (404, "text/plain", Array.Empty<byte>(), null);
        if (method == "GET" && path is "/" or "/index.html" or "/view")   // 11-12 0-7: 화면 껍데기(자료 없음)는 열쇠 없이 — 화면이 열쇠를 기기에 저장하고 주소창에서 지움 (field_server.py 와 같음). 자료(/api/*)는 그대로 열쇠
        {
            try
            {
                var page = PagePath ?? throw new FileNotFoundException("입력 화면 파일이 정해지지 않았습니다");
                return (200, "text/html; charset=utf-8", File.ReadAllBytes(path == "/view" ? Path.Combine(Path.GetDirectoryName(page)!, "view.html") : page), null);
            }
            catch (Exception x) when (x is IOException or UnauthorizedAccessException) { Log?.Invoke($"GET {path} 오류: {x}"); return J(500, Err($"서버 오류: {x.Message}")); }
        }
        bool Is(string key, string token) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(query.GetValueOrDefault(key) ?? ""), Encoding.UTF8.GetBytes(token));
        const string BadReport = "신고 주소가 맞지 않습니다 — 현장 담당자에게 신고 QR 을 다시 받으세요";
        if (method == "GET" && path == "/api/floor" && query.TryGetValue("t", out var tq) && tq.Length > 0)   // F16: 휴대폰 동바리 점검 화면이 입력 토큰으로 층 평면·동바리 점을 읽음 (field_server.py 와 같음)
        {
            if (!Is("t", Token)) return J(403, Err("접속 코드가 맞지 않습니다 — PC 화면에 나온 주소(QR)로 다시 여세요"));
            if ((DateTime.Now - TokenIssued).TotalHours >= TokenTtlH)
                return J(401, Err($"접속 주소가 만료됐습니다 ({Py.G(TokenTtlH)}시간) — PC 의 현장 입력 창에서 [새 주소]를 눌러 새 QR 로 여세요"));
            if (!query.TryGetValue("f", out var tfs) || !int.TryParse(tfs.Trim(), NumberStyles.Integer, Inv, out var tfl)) return J(400, Err("층 번호(f)가 필요합니다"));
            try { lock (gate) { var r = FloorView(tfl, false); return J((bool)r["ok"]! ? 200 : 404, r); } }
            catch (Exception x) { Log?.Invoke($"GET {path} 오류: {x}"); return J(500, Err($"서버 오류: {x.Message}")); }
        }
        if (method == "GET" && path == "/api/floor")   // 층 화면 (F12): 보기 토큰 v (읽기 전용) 또는 신고 토큰 r (F14, 구역마다 신고 버튼) — 입력 토큰과 다름 (field_server.py 와 같은 응답)
        {
            bool report = Is("r", ReportToken);
            if (!report && !Is("v", ViewToken))
                return J(403, Err(query.ContainsKey("r") ? BadReport : "보기 주소가 맞지 않습니다 — 층 표지 QR 을 다시 찍으세요"));
            try
            {
                if (!query.TryGetValue("f", out var fs) || !int.TryParse(fs.Trim(), NumberStyles.Integer, Inv, out var fl))
                    return J(400, Err("층 번호(f)가 필요합니다 — 층 표지 QR 을 다시 찍으세요"));
                lock (gate) { var r = FloorView(fl, report); return J((bool)r["ok"]! ? 200 : 404, r); }
            }
            catch (Exception x) { Log?.Invoke($"GET {path} 오류: {x}"); return J(500, Err($"서버 오류: {x.Message}")); }
        }
        bool isReport = method == "POST" && path == "/api/report";   // 협력업체 해체 신고 (F14): 신고 토큰 r 만 — 보기·입력 토큰으로는 403
        if (isReport) { if (!Is("r", ReportToken)) return J(403, Err(BadReport)); }
        else if (!Is("t", Token))
            return J(403, Err("접속 코드가 맞지 않습니다 — PC 화면에 나온 주소(QR)로 다시 여세요"));
        else if ((DateTime.Now - TokenIssued).TotalHours >= TokenTtlH)
            return J(401, Err($"접속 주소가 만료됐습니다 ({Py.G(TokenTtlH)}시간) — PC 의 현장 입력 창에서 [새 주소]를 눌러 새 QR 로 여세요"));
        if (method == "GET")
        {
            try
            {
                if (path == "/api/model")
                    lock (gate) return J(200, Model());
                if (path == "/api/recent")
                {
                    int n = query.TryGetValue("n", out var ns) && int.TryParse(ns.Trim(), NumberStyles.Integer, Inv, out var v) ? Math.Max(1, Math.Min(v, 200)) : 20;
                    lock (gate) return J(200, Recent(n));
                }
                return J(404, Err("없는 주소입니다"));
            }
            catch (Exception x) { Log?.Invoke($"GET {path} 오류: {x}"); return J(500, Err($"서버 오류: {x.Message}")); }
        }
        if (!isReport && path != "/api/measure") return J(404, Err("없는 주소입니다"));
        if (len <= 0 || len > MaxBody) return J(len > MaxBody ? 413 : 400, Err("보낸 내용이 없거나 너무 큽니다"));
        JsonNode? b;
        try { b = JsonNode.Parse(new UTF8Encoding(false, true).GetString(body)); }
        catch (Exception x) when (x is JsonException or DecoderFallbackException) { return J(400, Err("요청 형식이 틀렸습니다 (JSON 아님)")); }
        try
        {
            var uaText = headers.TryGetValue("User-Agent", out var ua) ? Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(ua)) : "";
            FieldEntry? entry;
            var res = isReport ? Report(b, uaText, out entry) : Measure(b, uaText, out entry);
            return J(200, res, entry);
        }
        catch (Bad x) { return J(400, Err(x.Message)); }
        catch (Exception x) when (x is UnauthorizedAccessException || x is IOException { HResult: unchecked((int)0x80070020) or unchecked((int)0x80070021) })
        { Log?.Invoke("POST 오류(파일 잠김): " + x.Message); return J(500, Err("서버 오류 — 저장되지 않았습니다: 데이터 파일이 다른 프로그램(Excel 등)에서 열려 있거나 쓸 수 없습니다 — PC 에서 그 파일을 닫고 다시 보내세요")); }   // field_server.py PermissionError 와 같은 문구
        catch (Exception x) { Log?.Invoke("POST 오류: " + x); return J(500, Err($"서버 오류 — 저장되지 않았을 수 있습니다: {x.Message}")); }
    }

    static int HeaderEnd(List<byte> b)
    {
        for (int i = 0; i + 3 < b.Count; i++)
            if (b[i] == '\r' && b[i + 1] == '\n' && b[i + 2] == '\r' && b[i + 3] == '\n') return i;
        return -1;
    }

    // ── Python 값 흉내 (검사 문구가 글자까지 같도록)
    /// <summary>Python str(v) (JSON 에서 온 값)</summary>
    static string PyStr(JsonNode? v) => v switch
    {
        null => "None",
        JsonValue jv when jv.TryGetValue<string>(out var s) => s,
        JsonValue jv when jv.TryGetValue<bool>(out var t) => t ? "True" : "False",
        JsonValue jv when jv.TryGetValue<JsonElement>(out var e) && e.ValueKind == JsonValueKind.Number => e.GetRawText(),
        _ => v.ToJsonString(),
    };

    /// <summary>Python str(v or "") — 거짓 값(None, "", 0, False, 빈 목록)이면 ""</summary>
    static string PyOr(JsonNode? v) => v switch
    {
        null => "",
        JsonValue jv when jv.TryGetValue<string>(out var s) => s,
        JsonValue jv when jv.TryGetValue<bool>(out var t) => t ? "True" : "",
        JsonValue jv when jv.TryGetValue<double>(out var d) => d == 0 ? "" : PyStr(v),
        JsonArray a when a.Count == 0 => "",
        JsonObject o when o.Count == 0 => "",
        _ => PyStr(v),
    };

    /// <summary>b.get(k) in (None, "")</summary>
    static bool Blank(JsonNode? v) => v is null || (v is JsonValue jv && jv.TryGetValue<string>(out var s) && s.Length == 0);

    static string PyBound(double v, bool isInt) => isInt ? ((long)v).ToString(Inv) : Py.Repr(v);

    static double Num(JsonObject b, string key, string label, string unit, double lo, double hi, bool intBounds, bool loOpen = false)
    {
        var v = b[key];
        if (v is null || PyStr(v).Trim().Length == 0) throw new Bad($"{label} 값이 비어 있습니다");
        if (!double.TryParse(PyStr(v).Trim().Replace(",", "."), NumberStyles.Float, Inv, out var f))   // 휴대폰 키패드의 쉼표 소수점도 받는다
            throw new Bad($"{label} '{PyStr(v)}' 은(는) 숫자가 아닙니다 — 숫자만 넣으세요 (예: 24.5)");
        if (double.IsNaN(f) || f < lo || f > hi || (loOpen && f <= lo))
        {
            var hint = unit == "MPa" && f > hi ? " (kgf/cm² 값이면 10.2 로 나누어 MPa 로)" : "";
            throw new Bad($"{label} {Py.G(f)}{(unit.Length > 0 ? " " + unit : "")} 은(는) 범위 밖입니다 — {PyBound(lo, intBounds)}{(loOpen ? "초과" : "")}~{PyBound(hi, intBounds)} {unit} 로 넣으세요{hint}");
        }
        return f;
    }

    static DateOnly Day(JsonNode? v, string label)
    {
        var s = PyOr(v);
        var m = DayRe.Match(s);
        try { if (m.Success) return new DateOnly(int.Parse(m.Groups[1].Value, Inv), int.Parse(m.Groups[2].Value, Inv), int.Parse(m.Groups[3].Value, Inv)); }
        catch (Exception x) when (x is ArgumentOutOfRangeException or FormatException) { }
        throw new Bad($"{label} '{s}' 형식이 틀렸습니다 — 2026-12-30 처럼 넣으세요");
    }

    static DateTime Stamp(JsonNode? v, string label)
    {
        var s = PyOr(v);
        var m = Ts.Match(s);
        try
        {
            if (m.Success)
                return new DateTime(int.Parse(m.Groups[1].Value, Inv), int.Parse(m.Groups[2].Value, Inv), int.Parse(m.Groups[3].Value, Inv),
                    int.Parse(m.Groups[4].Value, Inv), int.Parse(m.Groups[5].Value, Inv), 0);
        }
        catch (Exception x) when (x is ArgumentOutOfRangeException or FormatException) { }
        throw new Bad($"{label} '{s}' 형식이 틀렸습니다 — 2026-12-30 09:00 처럼 날짜와 시각을 넣으세요");
    }

    static string Text(JsonNode? v, string label, int maxLen, bool required = false)
    {
        var s = Py.Squash(PyOr(v));
        if (required && s.Length == 0) throw new Bad($"{label} 칸이 비어 있습니다 — 적어 주세요 (기록에 남습니다)");
        int n = s.EnumerateRunes().Count();
        if (n > maxLen) throw new Bad($"{label}: {maxLen}자 이내로 줄여 주세요 (지금 {n}자)");
        return s.Length > 0 && "=+-@".Contains(s[0]) ? "'" + s : s;   // 엑셀에서 수식으로 실행되지 않게
    }

    /// <summary>User-Agent → 짧은 기기 요약 (예: 'Android Chrome')</summary>
    static string DeviceOf(string ua)
    {
        var os = new[] { ("iPhone", "iPhone"), ("iPad", "iPad"), ("Android", "Android"), ("Windows", "Windows"), ("Mac OS", "Mac") }.FirstOrDefault(p => ua.Contains(p.Item1)).Item2 ?? "기타";
        var br = new[] { ("SamsungBrowser", "Samsung"), ("NAVER", "Naver"), ("KAKAOTALK", "KakaoTalk"), ("Edg/", "Edge"), ("CriOS", "Chrome"), ("Chrome", "Chrome"), ("Firefox", "Firefox"), ("Safari", "Safari") }
            .FirstOrDefault(p => ua.Contains(p.Item1)).Item2 ?? "";
        return $"{os} {br}".Trim();
    }

    // ── CSV
    List<Row> ReadRows(string kind) => Evaluator.Read(Folder, Kinds[kind].File);

    /// <summary>열 이름 줄이 없으면 만들고, 공통 열이 없으면 그 줄 뒤에 붙여 다시 쓴 다음 줄을 추가한다. 기존 열 순서는 그대로 (Python append_rows)</summary>
    /// opt = 선택 열 (공시체 채취 정보) — 이번 줄에 값이 있을 때만 머리에 더함 (값이 없으면 파일을 다시 쓰지 않음)</summary>
    /// <summary>field_server.py LOCK_* [가정] W7 잠금: 0.1 초씩 50번(5 초) 기다림, 30 초 넘은 잠금 파일은 멈춘 프로그램이 남긴 것</summary>
    internal static int LockTries = 50, LockWaitMs = 100;
    const double LockStaleS = 30;

    /// <summary>field_server.py file_lock (W7): '파일.lock' 배타 생성 (Python 서버·다른 PC 와 같은 이름, 네트워크 폴더도) — 잡힐 때까지 짧게 다시, 못 잡으면 Bad (아무것도 안 씀). 닫으면 지움</summary>
    internal static FileStream FileLock(string path)
    {
        var lk = path + ".lock";
        for (int i = 0; i < LockTries; i++)
        {
            try { return new FileStream(lk, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose); }
            catch (IOException) when (File.Exists(lk) || i < LockTries - 1)
            {
                try
                {
                    if ((DateTime.Now - File.GetLastWriteTime(lk)).TotalSeconds > LockStaleS) { File.Delete(lk); continue; }
                }
                catch (Exception x) when (x is IOException or UnauthorizedAccessException) { }
                Thread.Sleep(LockWaitMs);
            }
            catch (UnauthorizedAccessException) { Thread.Sleep(LockWaitMs); }   // 지워지는 중
        }
        throw new Bad($"{Path.GetFileName(path)} 를 다른 PC·프로그램이 쓰는 중입니다 — 잠시 뒤 다시 저장하세요 (이번 입력은 저장하지 않았습니다)");
    }

    /// <summary>AppendRowsCore 를 파일 잠금 안에서 (W7 — 두 곳에서 같은 CSV 에 동시에 이어 써도 줄 손실 없음)</summary>
    internal static void AppendRows(string path, string[] cols, List<Dictionary<string, string>> rows, string[]? opt = null)
    {
        using var _ = FileLock(path);
        AppendRowsCore(path, cols, rows, opt);
    }

    static void AppendRowsCore(string path, string[] cols, List<Dictionary<string, string>> rows, string[]? opt = null)
    {
        var used = (opt ?? Array.Empty<string>()).Where(c => rows.Any(r => (r.GetValueOrDefault(c) ?? "").Length > 0)).ToList();
        List<string> header;
        List<List<string>>? old;
        if (!File.Exists(path) || new FileInfo(path).Length == 0) (header, old) = (cols.Concat(Common).Concat(used).ToList(), new());
        else
        {
            var recs = Evaluator.ParseCsv(Py.ReadText(path, Py.Utf8Strict));
            header = recs.Count > 0 ? recs[0] : new();
            var missing = cols.Where(c => !header.Contains(c)).ToList();
            if (missing.Count > 0) throw new Bad($"{Path.GetFileName(path)} 의 첫 줄에 {Py.ReprList(missing)} 열이 없습니다 — PC 에서 파일 형식을 확인하세요");
            var extra = Common.Concat(used).Where(c => !header.Contains(c)).ToList();
            old = extra.Count > 0 ? recs.Skip(1).Where(r => r.Any(c => c.Trim().Length > 0)).ToList() : null;   // 빈 줄·",,,,," 줄은 버림 (field_server.py 와 같음 — 코드 검토 2차)
            header = header.Concat(extra).ToList();
        }
        if (old != null)   // 새 파일이거나 열 이름 줄을 늘려야 함 → 임시 파일에 다 쓰고 바꿔 끼운다 (도중 실패 시 원본 보존)
        {
            var sb = new StringBuilder(Line(header) + "\r\n");
            foreach (var r in old) sb.Append(Line(r.Concat(Enumerable.Repeat("", Math.Max(0, header.Count - r.Count))))).Append("\r\n");   // 기존 줄은 공통 열 빈칸
            var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            try
            {
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(true));
                File.Move(tmp, path, overwrite: true);
            }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { } }   // 바꿔 끼우기 실패(Excel 잠김) — 임시 파일이 쌓이지 않게 (코드 검토 2차)
        }
        bool needsNl;
        using (var fs = File.OpenRead(path))
        {
            needsNl = fs.Length > 0 && fs.Seek(-1, SeekOrigin.End) >= 0 && fs.ReadByte() is not (10 or 13);   // 엑셀 저장 파일은 끝 줄바꿈이 없을 수 있음
        }
        var outp = new StringBuilder(needsNl ? "\r\n" : "");
        foreach (var r in rows) outp.Append(Line(header.Select(c => r.GetValueOrDefault(c) ?? ""))).Append("\r\n");
        File.AppendAllText(path, outp.ToString(), new UTF8Encoding(false));   // 이어 쓰기는 BOM 없이
    }

    /// <summary>Python csv.writer (QUOTE_MINIMAL) 한 줄</summary>
    static string Line(IEnumerable<string> vals) => string.Join(",", vals.Select(v =>
        v.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v));

    // ── 상태·모델
    DateTime Now()
    {
        if (FixedNow() is { } f) return f;
        var n = DateTime.Now;
        return new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, 0);
    }

    /// <summary>ui_spec §6: 요청 가능 5, 공시체 시험 필요 4, 설치 결함 기록 2 (F16 규칙 ⑦), 존치 → 예측 해체일 전 3 양생 중 / 이후 9 존치 연장 경고. 판정 없음 0</summary>
    internal static int StatusOf(string decision, DateTime at, string due) =>
        decision.Length == 0 ? 0 : decision == "해체 승인 요청 가능" ? 5 : decision == "공시체 시험 필요" ? 4 : decision == "존치 (설치 결함 기록)" ? 2
        : DateOnly.FromDateTime(at) < DateOnly.ParseExact(due, "yyyy-MM-dd", Inv) ? 3 : 9;

    sealed class SensorInfo
    {
        public string Id = "", Kind = "", LastTs = "";
        public int Floor, Count;
        public double X, Y, LastValue;
        public bool HasLast;
        public SensorInfo Copy() => (SensorInfo)MemberwiseClone();
    }

    string? sensKey;
    Dictionary<(string Kind, string Id), SensorInfo>? sensCache;

    /// <summary>{(kind, id)} — 센서 파일 두 개가 그대로면 앞 결과를 다시 씀 (A-20, field_server.py sensors — 사본을 돌려줌)</summary>
    Dictionary<(string Kind, string Id), SensorInfo> Sensors()
    {
        var key = string.Join("|", SensorValue.Keys.Select(k => Evaluator.FileKey(Folder, Kinds[k].File)));
        if (sensKey != key || sensCache is null) (sensCache, sensKey) = (ReadSensors(), key);
        return sensCache.ToDictionary(kv => kv.Key, kv => kv.Value.Copy());
    }

    /// <summary>같은 ID 의 첫 줄 위치 (evaluate 온도 센서와 같은 규칙), 마지막 값</summary>
    Dictionary<(string Kind, string Id), SensorInfo> ReadSensors()
    {
        var outp = new Dictionary<(string, string), SensorInfo>();
        foreach (var (kind, vcol) in SensorValue)
            foreach (var r in ReadRows(kind))
            {
                var key = (kind, r["sensor_id"]!);
                if (!outp.TryGetValue(key, out var s))
                    outp[key] = s = new SensorInfo { Id = r["sensor_id"]!, Kind = kind, Floor = Py.Int(r["floor"]), X = Py.Float(r["x"]), Y = Py.Float(r["y"]) };
                s.Count++;
                if (!s.HasLast || string.CompareOrdinal(r["timestamp"], s.LastTs) >= 0)
                    (s.HasLast, s.LastTs, s.LastValue) = (true, r["timestamp"]!, Py.Float(r[vcol]));
            }
        return outp;
    }

    /// <summary>데이터 폴더 절대 경로로 정한 프로젝트 표시 — 휴대폰 오프라인 큐가 다른 프로젝트로 가지 않게 (감사 A-07).
    /// field_server.py: sha1(normcase(abspath(폴더))) 16진 앞 10자 (normcase = 소문자 + / → \)</summary>
    public string ProjectId => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(Folder)).ToLowerInvariant().Replace('/', '\\'))))[..10].ToLowerInvariant();

    /// <summary>층 이름 = 그 층 첫 구역 ID 의 '-' 앞 (KBIM B1·PH 처럼 번호와 이름이 다른 층), 없으면 번호로 (음수 = B) — field_server.py floor_name</summary>
    static string FloorName(int fl, JsonArray zones) => FloorSign.FloorName(fl, zones.Count > 0 ? (string?)zones[0]?["zone_id"] : null);

    /// <summary>타설 전 게이트 (PourGate = pour_gate.py) — 입력 점검에 걸리면 숨기지 않고 "확인 불가" + 문구 (field_server.py gate)</summary>
    JsonObject Gate(string zid, DateTime at)
    {
        try
        {
            var g = PourGate.Evaluate(Folder, zid, at);
            return new JsonObject { ["decision"] = g.Decision, ["reason"] = g.Reason };
        }
        catch (InvalidDataException x) { return new JsonObject { ["decision"] = "확인 불가", ["reason"] = x.Message }; }
    }

    /// <summary>field_server.py VIEW_LINE: 구역 상태 → 반장용 한 줄 (지시 문구 없음 — '금지'·'확인 전'까지). 0·1·2 = '해체 금지 — 타설 전'</summary>
    static (string Line, string Kind) ViewLine(int status) => status switch
    {
        7 => ("해체 완료 기록 있음", "done"), 6 => ("감리 확인 기록 있음 — 현장 책임자 지시 후 해체", "ok"), 5 => ("해체 금지 — 감리 확인 전 (해체 승인 요청 가능)", "ready"),
        8 => ("해체 금지 — 재받침", "hold"), 4 => ("해체 금지 — 공시체 시험 필요", "hold"), 3 or 9 => ("해체 금지 — 존치", "hold"),
        2 => ("해체 금지 — 설치 결함 기록", "hold"),   // F16 규칙 ⑦ (field_server.py 와 같음)
        _ => ("해체 금지 — 타설 전", "hold"),
    };

    /// <summary>field_server.py floor_view (F12): 반장·협력업체용 층 화면 — 구역마다 한 줄 + 무단 해체 의심, 층 상태 한 줄. 입력·수치 없음</summary>
    JsonObject FloorView(int fl, bool report = false, JsonObject? model = null)   // model = 이미 계산한 Model() (서버 동기화 스냅샷이 층마다 다시 계산하지 않게)
    {
        var m = model ?? Model();
        var floors = m["floors"]!.AsArray();
        var f = floors.FirstOrDefault(x => (int)x!["floor"]! == fl);
        if (f is null) return new() { ["ok"] = false, ["error"] = $"{fl}층 구역이 없습니다 — 층 표지 QR 을 다시 찍으세요" };
        var watch = (string)m["error"]! != "" ? new Dictionary<string, string>() : Evidence.RemovalWatch(Folder, DateOnly.FromDateTime(Now())).ToDictionary(kv => kv.Key, kv => kv.Value);
        var zs = f["zones"]!.AsArray().Select(z =>
        {
            var id = (string)z!["zone_id"]!;
            var (line, kind) = ViewLine((int)z["status"]!);
            return (Id: id, Line: line, Kind: kind, Sus: watch.GetValueOrDefault(id, ""), Status: (int)z["status"]!);   // Status = W10 층 표지 pz 비교
        }).ToList();
        Dictionary<string, List<(string Item, string Shore, DateTime T)>> ng;   // F16: 풀리지 않은 NG (모든 단계) — 판정 오류(첫 타설 전 등)와 상관없이 (코드 검토 2차, field_server.py 같음)
        try { ng = ShoreInspect.OpenNg(Folder, Now()); }
        catch (Exception x) when (x is FormatException or KeyNotFoundException or InvalidDataException or IOException) { ng = new(); }
        var zoneIds = zs.Select(z => z.Id).ToHashSet();
        var badSet = ng.SelectMany(kv => kv.Value.Select(v => (kv.Key, v.Shore))).ToHashSet();
        var shoreArr = new JsonArray(ShoreInspect.ReadShores(Folder).Where(kv => zoneIds.Contains(kv.Value.ZoneId)).Select(kv => (JsonNode)new JsonObject
        {
            ["id"] = kv.Key, ["zone_id"] = kv.Value.ZoneId, ["x"] = kv.Value.X, ["y"] = kv.Value.Y, ["label"] = kv.Value.FieldLabel,
            ["ng"] = badSet.Contains((kv.Value.ZoneId, kv.Key)) || badSet.Contains((kv.Value.ZoneId, "*")),
        }).ToArray());
        var sus = zs.Where(z => z.Sus.Length > 0).Select(z => z.Id).ToList();
        int hold = zs.Count(z => z.Kind == "hold");
        var (st, sk) = sus.Count > 0 ? ("무단 해체 의심 — 확인 필요: " + string.Join(", ", sus), "hold")
            : hold > 0 ? ($"동바리 해체 금지 — {hold}구역", "hold")
            : zs.Any(z => z.Kind == "ready") ? ("해체 금지 — 감리 확인 전 (해체 승인 요청 가능)", "ready")
            : zs.All(z => z.Kind == "done") ? ("해체 완료 기록 있음", "done")
            : ($"감리 확인 기록 있음 — {zs.Count(z => z.Kind is "ok" or "done")}구역", "ok");
        return new()
        {
            ["ok"] = true, ["project"] = m["project"]!.DeepClone(), ["now"] = m["now"]!.DeepClone(), ["fixed_now"] = m["fixed_now"]!.DeepClone(), ["error"] = m["error"]!.DeepClone(),
            ["floor"] = fl, ["name"] = f["name"]!.DeepClone(), ["status"] = st, ["kind"] = sk, ["sign_valid_hours"] = Evaluator.NumOption(Folder, "sign_valid_hours", FloorSign.SignValidH),   // W10 [가정]
            ["zones"] = new JsonArray(zs.Select(z => (JsonNode)new JsonObject { ["zone_id"] = z.Id, ["line"] = z.Line, ["kind"] = z.Kind, ["suspect"] = z.Sus, ["status"] = z.Status,
                ["ng"] = ng.TryGetValue(z.Id, out var zng) ? ShoreInspect.NgText(zng) : "" }).ToArray()),
            ["floors"] = new JsonArray(floors.Select(x => (JsonNode)new JsonObject { ["floor"] = x!["floor"]!.DeepClone(), ["name"] = x["name"]!.DeepClone() }).ToArray()),
            ["can_report"] = report,
            ["zone_rects"] = new JsonArray(f["zones"]!.AsArray().Select(z => (JsonNode)new JsonObject
                { ["zone_id"] = z!["zone_id"]!.DeepClone(), ["x0"] = z["x0"]!.DeepClone(), ["y0"] = z["y0"]!.DeepClone(), ["x1"] = z["x1"]!.DeepClone(), ["y1"] = z["y1"]!.DeepClone() }).ToArray()),
            ["shores"] = shoreArr,
        };
    }

    // ── POST /api/report (협력업체 해체 신고, F14) — field_server.py report 와 같은 검사·문구
    /// <summary>반장이 층 화면에서 구역마다 '뺐음'(아니오)·'다시 세움'(예)을 한 번 누름 → shore_checks.csv 한 줄 (source = 협력업체 신고, 시각 = 서버 지금).
    /// 판정·결론은 안 바꿈 — 무단 해체 의심(RemovalWatch)·타설 전 확인(PourGate)·근거 원장 7 해체가 기존 규칙대로 받음. 답은 신고 확인 문구까지.
    /// device_at = 휴대폰이 오프라인으로 저장했다가 다시 보낸 신고의 누른 시각(기기 시계) — timestamp 는 그래도 서버가 받은 시각(위조 방지), 기기 시각은 note 에만</summary>
    JsonObject Report(JsonNode? body, string ua, out FieldEntry? entry)
    {
        entry = null;
        if (body is not JsonObject b) throw new Bad("요청 형식이 틀렸습니다 (JSON 객체가 아님)");
        var zid = PyOr(b["zone_id"]).Trim();
        if (!Evaluator.Read(Folder, "zones.csv").Any(r => r.GetValueOrDefault("zone_id") == zid)) throw new Bad($"구역 '{zid}' 이(가) 모델에 없습니다 — 층 화면을 새로 고치세요");
        var present = PyOr(b["present"]).Trim();
        if (present is not ("예" or "아니오")) throw new Bad("'동바리를 뺐음' 또는 '다시 세움'을 누르세요");
        string cid = PyOr(b["client_id"]);
        if (!ClientRe.IsMatch(cid)) throw new Bad("client_id 가 없거나 형식이 틀렸습니다 (영문·숫자·-·_ 80자 이내)");
        string name = Text(b["name"], "이름", 20, required: true), company = Text(b["company"], "소속", 40);
        var dev = Text(b["device_at"], "기기 시각", 30);
        string photo = "", sha = "";
        var note = dev.Length > 0 ? $"기기 시각 {dev} (오프라인 저장 뒤 전송)" : "";
        var now = Now();
        var ts = now.ToString("yyyy-MM-dd'T'HH:mm", Inv);
        var file = Kinds["shore_check"].File;
        bool dup, approved;
        lock (gate)
        {
            dup = seen.Contains(cid);
            if (!dup)
            {
                (photo, sha) = Photo(b, zid, "report", now);
                AppendRows(Path.Combine(Folder, file), Kinds["shore_check"].Cols, new() { new() {
                    ["zone_id"] = zid, ["timestamp"] = ts, ["present"] = present, ["actor"] = name, ["photo"] = photo, ["source"] = Evidence.ReportSource, ["company"] = company, [Photos.ShaCol] = sha,
                    ["entered_by"] = name, ["entered_at"] = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", Inv), ["device"] = DeviceOf(ua), ["note"] = note, ["client_id"] = cid } }, ReportCols);
                seen.Add(cid);
                entry = new FieldEntry("shore_check", file, zid, 0, 1);
            }
            approved = Approval.Verify(Folder).Ok && Approval.ReadLog(Folder).Any(r => r.GetValueOrDefault("zone_id") == zid && r.GetValueOrDefault("action") == "감리 승인 기록"
                && DateTime.ParseExact(r["timestamp"]!, "yyyy-MM-dd'T'HH:mm:ss", Inv) <= now);
        }
        var msg = present == "예" ? "신고 저장됨 — 동바리 다시 세움"
            : approved ? "신고 저장됨 — 감리 승인 기록 있음: 해체 시각 기록됨" : "신고 저장됨 — 감리 승인 기록 없음: 현장 확인 요청됨";
        var res = new JsonObject { ["ok"] = true, ["saved"] = file, ["duplicate"] = dup, ["zone_id"] = zid, ["present"] = present, ["timestamp"] = ts, ["message"] = msg };
        if (photo.Length > 0) res["photo"] = photo;
        return res;
    }

    /// <summary>field_server.py photo: b["photo_data"] (base64·data URL, 휴대폰 사진 1장) → 사진/YYYYMMDD/… 원본 저장 → (상대 경로, sha256), 없으면 ("", "").
    /// 검사 실패 → Bad (F15). 다른 검사를 모두 통과한 뒤에만 부른다 (틀린 요청이 사진만 남기지 않게).
    /// at = 그 기록의 시각 → 파일 이름의 날짜 폴더·HHMMSS (총괄 29차) — 동바리 확인 = 확인 시각, 신고 = 받은 시각, 해체 후 점검 = 점검일 + 받은 시각</summary>
    (string Path, string Sha) Photo(JsonObject b, string zid, string kind, DateTime at)
    {
        var s = PyOr(b["photo_data"]);
        if (s.Length == 0) return ("", "");
        try { return Photos.Save(Folder, Photos.Decode(s), zid, kind, at); }
        catch (InvalidDataException x) { throw new Bad(x.Message); }
    }

    JsonObject Model()
    {
        var at = Now();
        var zones = Evaluator.Read(Folder, "zones.csv");
        var pours = new Dictionary<string, Row>();
        foreach (var r in Evaluator.Read(Folder, "pours.csv")) pours[r["zone_id"]!] = r;
        var dec = new Dictionary<string, ZoneRow>();
        string err = "";
        try { foreach (var r in Evaluator.Evaluate(Folder, at, CureMin)) dec[r.ZoneId] = r; }
        catch (InvalidDataException x) { dec.Clear(); err = x.Message; }   // evaluate 의 입력 점검 문구 (CSV 를 사람이 고쳐야 함)
        var cold = new Dictionary<string, List<string>>();
        if (err == "")
        {
            try
            {
                foreach (var w in Evaluator.ColdChecks(Folder, at))     // 한중 초기양생 경고 (판정은 안 바꿈)
                    (cold.TryGetValue(w.ZoneId, out var l) ? l : cold[w.ZoneId] = new()).Add(w.Text);
                foreach (var w in Evaluator.CrossChecks(Folder, at))    // 공시체–센서 교차 검사 (판정은 안 바꿈)
                    (cold.TryGetValue(w.ZoneId, out var l) ? l : cold[w.ZoneId] = new()).Add(w.Text);
                foreach (var w in Evaluator.InputChecks(Folder, at, DefaultFck))   // fck·공시체 편차 점검 (판정은 안 바꿈, A-29)
                    (cold.TryGetValue(w.ZoneId, out var l) ? l : cold[w.ZoneId] = new()).Add(w.Text);
            }
            catch (InvalidDataException x) { err = x.Message; }
        }
        var alerts = new List<Alert>();
        if (err == "")
        {
            try { alerts = Alerts.Of(Folder, at, PrevAt() ?? Alerts.PrevEval(Folder, at), CureMin); }   // 오늘 볼 것 (D4) — 비교 기준 = PrevAt, 없으면 eval_log.csv (alerts.py)
            catch (InvalidDataException x) { err = x.Message; }
        }
        var done = Approval.StatusOverride(Folder, at);               // 감리 승인 기록 6 · 해체 완료 7 · 재받침 8 (approvals.csv, 사람이 입력)
        Dictionary<string, ZoneLedger> led;
        // shortcut: Python 은 SystemExit·ValueError·KeyError 만 잡고 문구가 Python 예외 글자 — 여기선 모든 예외를 .NET 문구로 (CSV 를 사람이 고쳐야 하는 경우뿐)
        try { led = Evidence.LedgerAt(Folder, DateOnly.FromDateTime(at)); }   // 근거 원장 n/8 (evidence.py)
        catch (Exception x) when (x is InvalidDataException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            led = new();
            if (err == "") err = "근거 원장을 읽을 수 없음: " + x.Message;
        }
        int? top = zones.Where(z => dec.ContainsKey(z["zone_id"]!)).Select(z => (int?)Py.Int(z["floor"])).Max();   // 타설된 최상층 — 그 위층만 타설 전 확인
        var floors = new Dictionary<int, JsonArray>();
        var cm = Provenance.CylMeta(Folder);   // 공시체 줄 누가·언제 (감리 화면, 11-12)
        foreach (var z in zones)
        {
            var id = z["zone_id"]!;
            dec.TryGetValue(id, out var d);
            pours.TryGetValue(id, out var p);
            int fl = Py.Int(z["floor"]);
            var o = new JsonObject { ["zone_id"] = id };
            foreach (var k in new[] { "x0", "y0", "x1", "y1" }) o[k] = Py.Float(z[k]);
            o["status"] = done.TryGetValue(id, out var code) ? code : d == null ? 0 : StatusOf(d.Decision, at, d.PredictedDue.ToString("yyyy-MM-dd", Inv));
            o["decision"] = d?.Decision ?? "";
            o["reason"] = d?.Reason ?? (p is { Count: > 0 } ? "타설 예정 (판정 시각 뒤)" : "타설 기록 없음");
            o["predicted_due"] = d?.PredictedDue.ToString("yyyy-MM-dd", Inv) ?? "";
            o["pour_date"] = p?.GetValueOrDefault("pour_date") ?? "";
            o["pour_id"] = (p?.GetValueOrDefault("pour_id") ?? "").Trim();   // W1 타설 묶음 (휴대폰 공시체 묶음 고르기)
            o["threshold"] = d == null ? "" : d.Threshold;
            o["f_cyl"] = d?.FCyl is { } fc ? fc : "";
            o["cyl_test_date"] = d?.CylTestDate ?? "";
            o["f_est_min"] = d == null ? "" : d.FEstMin;
            o["est_source"] = d?.EstSource ?? "";
            o["level_gate"] = d?.LevelGate ?? "";
            o["age_days"] = d == null ? "" : d.AgeDays;
            o["early_form"] = d?.EarlyForm ?? "";
            var ctd = d?.CylTestDate ?? "";
            var (cby, cat) = cm.TryGetValue((id, ctd), out var c1) ? c1 : cm.TryGetValue(("lot:" + (p?.GetValueOrDefault("pour_id") ?? "").Trim(), ctd), out var c2) ? c2 : ("", "");
            o["cyl_entered_by"] = cby;
            o["cyl_entered_at"] = cat;
            o["warnings"] = new JsonArray((cold.GetValueOrDefault(id) ?? new()).Select(t => (JsonNode)t).ToArray());
            var zl = led.GetValueOrDefault(id);
            o["evidence_stage"] = zl?.Stage ?? 0;
            o["evidence_warnings"] = new JsonArray((zl?.Warnings ?? new()).Select(t => (JsonNode)t).ToArray());
            o["pour_gate"] = d == null && top != null && fl > top ? Gate(id, at) : null;   // 아직 타설 전 구역만 — 현장에서 타설 직전에 확인
            (floors.TryGetValue(fl, out var a) ? a : floors[fl] = new JsonArray()).Add(o);
        }
        var sensors = new JsonArray();
        foreach (var s in Sensors().Values.OrderBy(s => s.Floor).ThenBy(s => s.Kind, StringComparer.Ordinal).ThenBy(s => s.Id, StringComparer.Ordinal))
            sensors.Add(new JsonObject
            {
                ["sensor_id"] = s.Id, ["kind"] = s.Kind, ["floor"] = s.Floor, ["x"] = s.X, ["y"] = s.Y, ["count"] = s.Count,
                ["last"] = s.HasLast ? new JsonObject { ["timestamp"] = s.LastTs, [SensorValue[s.Kind]] = s.LastValue } : null,
            });
        var limits = new JsonObject();
        foreach (var (k, (lo, hi)) in Limits) limits[k] = new JsonArray(lo, hi);
        return new JsonObject
        {
            ["ok"] = true, ["project"] = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Folder))), ["project_id"] = ProjectId,
            ["now"] = at.ToString("yyyy-MM-dd'T'HH:mm", Inv), ["fixed_now"] = FixedNow() != null, ["error"] = err,
            ["floors"] = new JsonArray(floors.OrderBy(kv => kv.Key).Select(kv => (JsonNode)new JsonObject { ["floor"] = kv.Key, ["name"] = FloorName(kv.Key, kv.Value), ["zones"] = kv.Value }).ToArray()),
            ["sensors"] = sensors,
            ["kinds"] = new JsonArray(Kinds.Select(kv => (JsonNode)new JsonObject { ["kind"] = kv.Key, ["label"] = kv.Value.Label, ["file"] = kv.Value.File, ["role"] = kv.Value.Role }).ToArray()),
            ["curings"] = new JsonArray(Curings.Select(c => (JsonNode)c).ToArray()),
            ["limits"] = limits,
            ["rule_basis"] = Evaluator.RuleBasis,
            ["alerts"] = new JsonArray(alerts.Select(a => (JsonNode)new JsonObject { ["kind"] = a.Kind, ["zone_id"] = a.ZoneId, ["text"] = a.Text }).ToArray()),
            ["inspect"] = new JsonArray(ShoreInspect.Stages.Select(st => (JsonNode)new JsonObject
            {
                ["stage"] = st.Stage,
                ["items"] = new JsonArray(st.Items.Select(i => (JsonNode)new JsonObject { ["code"] = i.Code, ["label"] = i.Label, ["criterion"] = i.Criterion, ["source"] = i.Source }).ToArray()),
            }).ToArray()),
        };
    }

    // ── POST /api/measure
    /// <summary>Revit 입력 창([타설 등록]·[공시체 결과])이 휴대폰과 같은 검사·저장(Validate·AppendRows·공통 열)을 HTTP 없이 쓰게 — 본문 여럿을 모두 검사한 뒤에만 씀
    /// (하나라도 틀리면 아무것도 안 씀). 반환 = (성공, 한국어 오류, 저장한 줄 수). ua = 기기 열 ("Windows")</summary>
    public (bool Ok, string Error, int Rows) SubmitLocal(IReadOnlyList<JsonObject> bodies, string ua = "Windows")
    {
        try
        {
            lock (gate)
            {
                var all = new List<(string Kind, List<Dictionary<string, string>> Rows)>();
                foreach (var b in bodies)
                {
                    var kind = PyOr(b["kind"]);
                    if (!Kinds.ContainsKey(kind)) throw new Bad($"측정 종류 '{kind}' 은(는) 없습니다");
                    var common = new Dictionary<string, string>
                    {
                        ["entered_by"] = Text(b["entered_by"], "입력자 이름", 20, required: true), ["note"] = Text(b["note"], "메모", 200),
                        ["entered_at"] = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", Inv), ["device"] = DeviceOf(ua), ["client_id"] = "revit-" + Guid.NewGuid().ToString("N")[..12],
                    };
                    var (rows, _, _, _) = Validate(kind, b);
                    foreach (var r in rows) foreach (var (k, v) in common) r[k] = v;
                    all.Add((kind, rows));
                }
                int n = 0;
                foreach (var (kind, rows) in all.Where(x => x.Rows.Count > 0))
                {
                    AppendRows(Path.Combine(Folder, Kinds[kind].File), Kinds[kind].Cols, rows, OptCols(kind));
                    foreach (var r in rows) seen.Add(r["client_id"]);
                    n += rows.Count;
                }
                return (true, "", n);
            }
        }
        catch (Bad x) { return (false, x.Message, 0); }
        catch (Exception x) when (x is UnauthorizedAccessException || x is IOException { HResult: unchecked((int)0x80070020) or unchecked((int)0x80070021) })
        { Log?.Invoke("입력 창 저장 오류(파일 잠김): " + x.Message); return (false, "저장되지 않았습니다: 데이터 파일이 다른 프로그램(Excel 등)에서 열려 있거나 쓸 수 없습니다 — 그 파일을 닫고 다시 [기록]", 0); }
        catch (Exception x) when (x is InvalidDataException or FormatException or DecoderFallbackException or KeyNotFoundException)
        { return (false, "저장되지 않았습니다: 데이터 폴더 파일을 읽지 못했습니다 — " + x.Message, 0); }   // 코드 검토 2차: Bad 만 잡아 입력 창이 깨졌음
    }

    /// <summary>종류마다 기존 열 뒤 선택 열 (field_server.py append 의 opt 와 같음)</summary>
    static string[]? OptCols(string kind) => kind == "cylinder" ? SampleCols : kind == "pour" ? PourCols : kind is "shore_check" or "after_check" ? PhotoCols : kind == "shore_inspect" ? ShoreInspect.ExtraCols : null;

    JsonObject Measure(JsonNode? body, string ua, out FieldEntry? entry)
    {
        entry = null;
        if (body is not JsonObject b) throw new Bad("요청 형식이 틀렸습니다 (JSON 객체가 아님)");
        if (!(b["project_id"] is null || b["project_id"] is JsonValue pv && pv.TryGetValue<string>(out var ps) && (ps == "" || ps == ProjectId)))
            throw new Bad("다른 프로젝트(다른 데이터 폴더)에서 입력한 기록이라 받지 않습니다 — 그 프로젝트의 현장 입력을 켜고 보내거나, 이 기기에서 지우세요");
        string? kind = b["kind"] is JsonValue kv && kv.TryGetValue<string>(out var ks) ? ks : null;
        if (kind == null || !Kinds.ContainsKey(kind)) throw new Bad($"측정 종류 '{PyStr(b["kind"])}' 은(는) 없습니다 — {Py.ReprList(Kinds.Keys)} 중 하나");
        string cid = PyOr(b["client_id"]);
        if (!ClientRe.IsMatch(cid)) throw new Bad("client_id 가 없거나 형식이 틀렸습니다 (영문·숫자·-·_ 80자 이내)");
        var common = new Dictionary<string, string>
        {
            ["entered_by"] = Text(b["entered_by"], "입력자 이름", 20, required: true), ["note"] = Text(b["note"], "메모", 200),
            ["entered_at"] = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", Inv), ["device"] = DeviceOf(ua), ["client_id"] = cid,
        };
        lock (gate)
        {
            if (seen.Contains(cid)) return Answer(kind, b, new JsonObject { ["duplicate"] = true, ["rows"] = 0 });   // 오프라인 큐 재전송 → 저장하지 않고 성공으로
            var (rows, info, target, floor) = Validate(kind, b);
            if (rows.Count > 0)
            {
                foreach (var r in rows) foreach (var (k, v) in common) r[k] = v;
                AppendRows(Path.Combine(Folder, Kinds[kind].File), Kinds[kind].Cols, rows, OptCols(kind));
                entry = new FieldEntry(kind, Kinds[kind].File, target, floor, rows.Count);
            }
            seen.Add(cid);
            info["rows"] = rows.Count;
            return Answer(kind, b, info);
        }
    }

    (List<Dictionary<string, string>> Rows, JsonObject Info, string Target, int Floor) Validate(string kind, JsonObject b)
    {
        var now = Now();
        var tomorrow = now.Date.AddDays(1);
        var zones = new Dictionary<string, Row>();
        foreach (var r in Evaluator.Read(Folder, "zones.csv")) zones[r["zone_id"]!] = r;
        var pours = new Dictionary<string, Row>();
        foreach (var r in Evaluator.Read(Folder, "pours.csv")) pours[r["zone_id"]!] = r;
        DateOnly PourDay(string zid) => DateOnly.ParseExact(pours[zid]["pour_date"]!, "yyyy-MM-dd", Inv);
        static string Iso(DateOnly d) => Py.Iso(d);
        var info = new JsonObject();
        if (kind == "shore_check")        // 안전관리자가 층 QR 로 '지금 동바리가 서 있는가' 확인 (해체는 협력업체가 해서 기록이 비기 쉬움)
        {
            var zid = PyOr(b["zone_id"]).Trim();
            if (!zones.ContainsKey(zid)) throw new Bad($"구역 '{zid}' 이(가) 모델에 없습니다 — 구역 타일에서 다시 고르세요");
            var t = Stamp(b["timestamp"], "확인 시각");
            if (t >= tomorrow) throw new Bad($"확인 시각 {t.ToString("yyyy-MM-dd HH:mm", Inv)} 은(는) 아직 오지 않은 시각입니다");
            var present = PyOr(b["present"]).Trim();
            if (present is not ("예" or "아니오")) throw new Bad("동바리가 서 있는지 '예' 또는 '아니오'를 고르세요");
            string actor = Text(b["entered_by"], "입력자 이름", 20, required: true), pname = Text(b["photo"], "사진 파일 이름", 80);
            var (photo, sha) = Photo(b, zid, "shore_check", t);       // 사진을 올렸으면 서버 저장 경로 (F15), 아니면 예전처럼 파일 이름 글자
            if (photo.Length > 0) info["photo"] = photo;
            return (new() { new() { ["zone_id"] = zid, ["timestamp"] = t.ToString("yyyy-MM-dd'T'HH:mm", Inv), ["present"] = present,
                ["actor"] = actor, ["photo"] = photo.Length > 0 ? photo : pname, [Photos.ShaCol] = sha } }, info, zid, 0);
        }
        if (kind == "shore_inspect")     // F16 동바리 단위 현장 점검 — 구역·단계·항목마다 OK/NG, NG 면 문제 동바리 ID (사진 없음, field_server.py 와 같은 검사·문구)
        {
            var zid = PyOr(b["zone_id"]).Trim();
            if (!zones.ContainsKey(zid)) throw new Bad($"구역 '{zid}' 이(가) 모델에 없습니다 — 평면에서 구역을 다시 고르세요");
            var stage = PyOr(b["stage"]).Trim();
            var def = ShoreInspect.Stages.FirstOrDefault(x => x.Stage == stage).Items;
            if (def is null) throw new Bad($"점검 단계 '{stage}' 은(는) 없습니다 — {string.Join(", ", ShoreInspect.Stages.Select(x => x.Stage))} 중 하나");
            var t = PyOr(b["timestamp"]).Length > 0 ? Stamp(b["timestamp"], "점검 시각") : new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);
            if (t >= tomorrow) throw new Bad($"점검 시각 {t.ToString("yyyy-MM-dd HH:mm", Inv)} 은(는) 아직 오지 않은 시각입니다");
            if (b["items"] is not JsonArray items || items.Count == 0) throw new Bad("점검 항목을 하나 이상 보내세요 (항목마다 OK 또는 NG)");
            var lab = def.ToDictionary(x => x.Code, x => x.Label);
            var shores = ShoreInspect.ReadShores(Folder).Where(kv => kv.Value.ZoneId == zid).Select(kv => kv.Key).ToHashSet();
            var by = Text(b["entered_by"], "입력자 이름", 20, required: true);
            var siRows = new List<Dictionary<string, string>>();
            var seen = new HashSet<string>();
            foreach (var itn in items)
            {
                if (itn is not JsonObject it) throw new Bad("점검 항목 형식이 틀렸습니다 (item_code·result·shore_ids)");
                var c = PyOr(it["item_code"]).Trim();
                if (!lab.ContainsKey(c)) throw new Bad($"{stage} 항목 '{c}' 은(는) 없습니다 — 화면을 새로 고치세요");
                if (!seen.Add(c)) throw new Bad($"{c} {lab[c]} 이(가) 두 번 있습니다");
                var res = PyOr(it["result"]).Trim().ToUpperInvariant();
                if (res is not ("OK" or "NG")) throw new Bad($"{c} {lab[c]}: OK 또는 NG 를 고르세요");
                var idn = it["shore_ids"];
                if ((idn is not null && idn is not JsonArray) || (idn is JsonArray ia && ia.Count > 500)) throw new Bad($"{c} {lab[c]}: 동바리 목록 형식이 틀렸습니다 (500개 이내)");
                var ids = (idn as JsonArray ?? new JsonArray()).Select(x => PyStr(x).Trim()).Where(x => x.Length > 0).Distinct().ToList();
                var bad = ids.Where(x => !shores.Contains(x)).ToList();
                if (bad.Count > 0) throw new Bad($"동바리 {string.Join(", ", bad.Take(3))} 은(는) 구역 {zid} 에 없습니다 — 평면에서 다시 고르세요");
                if (res == "NG" && ids.Count == 0 && shores.Count > 0) throw new Bad($"{c} {lab[c]} NG — 문제 동바리를 평면에서 하나 이상 고르세요");
                siRows.Add(new() { ["zone_id"] = zid, ["item_code"] = c, ["result"] = res, ["inspector"] = by, ["date"] = t.ToString("yyyy-MM-dd", Inv), ["photo_path"] = "",
                    ["stage"] = stage, ["shore_ids"] = string.Join(";", ids), ["timestamp"] = t.ToString("yyyy-MM-dd'T'HH:mm", Inv), ["source"] = "현장 점검" });
            }
            return (siRows, info, zid, 0);
        }
        if (kind == "pour")              // 타설 등록 (Revit [타설 등록] 창 — 휴대폰 화면엔 없음, field_server.py 와 같은 검사·문구)
        {
            var zid = PyOr(b["zone_id"]).Trim();
            if (!zones.ContainsKey(zid)) throw new Bad($"구역 '{zid}' 이(가) 모델에 없습니다 — 구역을 다시 고르세요");
            if (pours.TryGetValue(zid, out var had)) throw new Bad($"{zid} 는 이미 타설 기록이 있습니다 ({had.GetValueOrDefault("pour_date") ?? ""}) — 고치려면 구역 정보 창 [타설 기록 취소] 뒤 다시 등록");
            var d = Day(b["pour_date"], "타설일");
            if (d >= DateOnly.FromDateTime(tomorrow)) throw new Bad($"타설일 {Iso(d)} 은(는) 아직 오지 않은 날입니다 — 오늘({Iso(DateOnly.FromDateTime(now))}) 이전으로");
            var m = Regex.Match(PyOr(b["pour_time"]), @"^\s*([01]?[0-9]|2[0-3]):([0-5][0-9])\s*$");
            if (!m.Success) throw new Bad($"타설 시각 '{PyOr(b["pour_time"])}' 형식이 틀렸습니다 — 08:00 처럼 넣으세요");
            var mix = PyOr(b["mix"]).Trim();
            if (!ShoringEngine.Mixes.ContainsKey(mix)) throw new Bad($"배합을 고르세요 ({string.Join(" · ", ShoringEngine.Mixes.Keys)})");
            var fck = Num(b, "fck", "설계기준강도 fck", "MPa", 18, 60, true);   // [프로젝트 설정] fck 칸과 같은 범위
            var lot = Text(b["pour_id"], "타설 묶음", 40);
            var sup = PyOr(b["supervisor_present"]).Trim();
            if (sup is not ("" or "예" or "아니오")) throw new Bad("타설 입회 감리는 예·아니오 중 하나입니다 (모르면 비움)");
            var watcher = Text(b["watcher"], "타설 감시자", 20);
            var same = lot.Length > 0 ? pours.Values.Where(r => (r.GetValueOrDefault("pour_id") ?? "").Trim() == lot).ToList() : new();
            if (same.Count > 0 && (same[0]["pour_date"] != Iso(d) || same[0]["mix"] != mix || Py.Float(same[0]["fck"]) != fck))   // W1: 공시체 하나로 묶음 전체를 대표 → 같은 콘크리트만
                throw new Bad($"타설 묶음 {lot} 은(는) {same[0]["pour_date"]} · {same[0]["mix"]} · fck {Py.G(Py.Float(same[0]["fck"]))} 입니다 — 같은 날·같은 배합·같은 fck 만 묶습니다 (다르면 새 묶음 번호)");
            return (new() { new() { ["zone_id"] = zid, ["pour_date"] = Iso(d), ["pour_time"] = int.Parse(m.Groups[1].Value, Inv).ToString("00", Inv) + ":" + m.Groups[2].Value,
                ["mix"] = mix, ["fck"] = Py.G(fck), ["pour_id"] = lot, ["supervisor_present"] = sup, ["watcher"] = watcher } }, info, zid, 0);
        }
        if (kind == "shore_spec")        // v6 동바리 사양·설치일 (Revit [동바리 사양] 창 — 휴대폰 화면엔 없음, field_server.py 와 같은 검사·문구)
        {
            if (!int.TryParse(PyStr(b["floor"] ?? JsonValue.Create("")).Trim(), NumberStyles.AllowLeadingSign, Inv, out var spFl) || b["floor"] is null)
                throw new Bad("층 번호가 틀렸습니다 — 층을 다시 고르세요");
            var spZones = zones.Where(kv => Py.Int(kv.Value["floor"]) == spFl).Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList();
            if (spZones.Count == 0) throw new Bad($"{spFl}층이 모델에 없습니다 — 층을 다시 고르세요");
            var spName = FloorSign.FloorName(spFl, spZones[0]);
            var zid = PyOr(b["zone_id"]).Trim();
            if (zid.Length > 0 && !spZones.Contains(zid)) throw new Bad($"구역 '{zid}' 이(가) {spName} 에 없습니다 — 구역을 다시 고르세요 (비우면 층 전체)");
            var spHave = Evaluator.Read(Folder, Kinds[kind].File).Where(r => (r.GetValueOrDefault("floor") ?? "").Trim() == spFl.ToString(Inv) && (r.GetValueOrDefault("zone_id") ?? "").Trim() == zid).ToList();
            var spType = PyOr(b["type"]).Trim();
            if (!ShoreInput.Types.Any(x => x.Name == spType)) throw new Bad($"동바리 종류를 고르세요 ({string.Join(" · ", ShoreInput.Types.Select(x => x.Name))})");
            var spSpec = Text(b["spec"], "규격", 40, true);
            var spX = Num(b, "spacing_x_mm", "X 간격", "mm", ShoreInput.SpacingLo, ShoreInput.SpacingHi, true);
            var spY = Num(b, "spacing_y_mm", "Y 간격", "mm", ShoreInput.SpacingLo, ShoreInput.SpacingHi, true);
            var spN = Num(b, "splices", "이음 수", "개", ShoreInput.SplicesLo, ShoreInput.SplicesHi, true);
            if (spN != Math.Truncate(spN)) throw new Bad("이음 수는 0 이상 정수로 적어 주세요");
            var spD = Day(b["install_date"], "동바리 설치일");
            if (spD >= DateOnly.FromDateTime(tomorrow)) throw new Bad($"동바리 설치일 {Iso(spD)} 은(는) 아직 오지 않은 날입니다 — 오늘({Iso(DateOnly.FromDateTime(now))}) 이전으로");
            var spPours = (zid.Length > 0 ? new List<string> { zid } : spZones).Where(pours.ContainsKey).Select(PourDay).Order().ToList();
            if (spPours.Count > 0 && spD > spPours[0]) throw new Bad($"동바리 설치일 {Iso(spD)} 이 타설일 {Iso(spPours[0])} 보다 뒤입니다 — 설치일을 확인하세요");
            if (spHave.Count > 0 && string.CompareOrdinal(Iso(spD), spHave[^1].GetValueOrDefault("install_date") ?? "") < 0)   // 11-12 2-1: 다시 넣기 허용 (마지막 줄을 씀)
                throw new Bad($"{spName} {(zid.Length > 0 ? "구역 " + zid : "층 전체")} 사양 기록이 있습니다 (설치일 {spHave[^1].GetValueOrDefault("install_date") ?? ""}) — 다시 넣으려면 그 날 이후 설치일로 (마지막 줄을 씀)");
            return (new() { new() { ["floor"] = spFl.ToString(Inv), ["zone_id"] = zid, ["type"] = spType, ["spec"] = spSpec, ["spacing_x_mm"] = Py.G(spX), ["spacing_y_mm"] = Py.G(spY),
                ["splices"] = ((long)spN).ToString(Inv), ["install_date"] = Iso(spD) } }, info, zid, 0);
        }
        if (kind == "after_check")       // 해체 후 점검 5항목 (가이드라인 4.3, F3) — 예 → OK, 아니오 → NG, 고르지 않은 항목은 줄 없음 (field_server.py 와 같은 문구)
        {
            var zid = PyOr(b["zone_id"]).Trim();
            if (!zones.ContainsKey(zid)) throw new Bad($"구역 '{zid}' 이(가) 모델에 없습니다 — 구역 타일에서 다시 고르세요");
            var d = Day(b["date"], "점검일");
            if (d >= DateOnly.FromDateTime(tomorrow)) throw new Bad($"점검일 {Iso(d)} 은(는) 아직 오지 않은 날입니다 — 오늘({Iso(DateOnly.FromDateTime(now))}) 이전으로");
            var items = b["items"];
            if (items is not null and not JsonObject) throw new Bad("점검 항목 형식이 틀렸습니다 (항목 → 예/아니오)");
            string by = Text(b["entered_by"], "입력자 이름", 20, required: true), photo = Text(b["photo"], "사진 파일 이름", 80);
            var acRows = new List<Dictionary<string, string>>();
            foreach (var (code, name) in AfterCheck.Items)
            {
                var v = PyOr((items as JsonObject)?[code]).Trim();
                if (v.Length == 0) continue;
                if (v is not ("예" or "아니오")) throw new Bad($"{code} {name}: '예' 또는 '아니오'를 고르세요");
                acRows.Add(new() { ["zone_id"] = zid, ["date"] = Iso(d), ["item_code"] = code, ["result"] = v == "예" ? "OK" : "NG", ["by"] = by, ["photo"] = photo });
            }
            var settle = b["settle"];       // 11-12 G9 침하 계측 {지점: mm} → item_code SET-지점, result = mm (field_server.py 와 같은 문구)
            if (settle is not null and not JsonObject && !(settle is JsonArray { Count: 0 }) || settle is JsonObject { Count: > 20 })
                throw new Bad("침하 계측 형식이 틀렸습니다 (지점 → mm, 20지점까지)");
            if (settle is JsonObject so)
                foreach (var (pt, _) in so.ToList())
                {
                    var p = pt.Trim().ToUpperInvariant();
                    if (!Regex.IsMatch(p, @"^[A-Z0-9]{1,10}\z")) throw new Bad($"침하 측정 지점 '{pt}' — 영문·숫자 10자까지 (예: P1)");
                    acRows.Add(new() { ["zone_id"] = zid, ["date"] = Iso(d), ["item_code"] = AfterCheck.Settle + p, ["result"] = Py.G(Num(so, pt, $"침하량 {p}", "mm", -100, 100, intBounds: true)), ["by"] = by, ["photo"] = photo });
                }
            if (acRows.Count == 0) throw new Bad("점검 항목을 하나 이상 고르세요 (예/아니오)");
            var (path, psha) = Photo(b, zid, "after_check", d.ToDateTime(TimeOnly.FromDateTime(Now())));          // 사진 1장 → 항목 줄마다 같은 경로 (F15)
            if (path.Length > 0)
            {
                info["photo"] = path;
                foreach (var r in acRows) { r["photo"] = path; r[Photos.ShaCol] = psha; }
            }
            return (acRows, info, zid, 0);
        }
        if (kind is "cylinder" or "rebound")
        {
            var zid = PyOr(b["zone_id"]).Trim();
            var lot = kind == "cylinder" && zid.Length == 0 ? Text(b["pour_id"], "타설 묶음", 40) : "";
            var lotZones = new List<string>();
            if (lot.Length > 0)          // W1: 구역 대신 타설 묶음 — 묶음의 모든 구역 공통 시험
            {
                lotZones = pours.Where(kv => (kv.Value.GetValueOrDefault("pour_id") ?? "").Trim() == lot).Select(kv => kv.Key).Order(StringComparer.Ordinal).ToList();
                if (lotZones.Count == 0) throw new Bad($"타설 묶음 '{lot}' 이(가) 타설 기록(pours.csv)에 없습니다 — [타설 등록]에서 묶음 번호를 넣거나 구역을 고르세요");
                info["lot_zones"] = new JsonArray(lotZones.Select(z => (JsonNode?)JsonValue.Create(z)).ToArray());
            }
            else if (!zones.ContainsKey(zid)) throw new Bad($"구역 '{zid}' 이(가) 모델에 없습니다 — 구역 타일에서 다시 고르세요");
            else if (!pours.ContainsKey(zid)) throw new Bad($"{zid} 는 타설 기록(pours.csv)이 없습니다 — 타설일을 먼저 등록하세요");
            var d = Day(b["test_date"], "시험일");
            var pd = lot.Length > 0 ? lotZones.Select(PourDay).Max() : PourDay(zid);
            if (d < pd) throw new Bad($"시험일 {Iso(d)} 이 타설일 {Iso(pd)} 보다 앞입니다 — 시험일을 확인하세요");
            if (d >= DateOnly.FromDateTime(tomorrow)) throw new Bad($"시험일 {Iso(d)} 은(는) 아직 오지 않은 날입니다 — 오늘({Iso(DateOnly.FromDateTime(now))}) 이전으로");
            if (kind == "cylinder")
            {
                var curing = PyOr(b["curing"]).Trim();
                if (!Curings.Contains(curing)) throw new Bad($"양생 방법을 고르세요 ({string.Join(" · ", Curings)}) — 해체 판단 근거는 현장양생 공시체");
                var (lo, hi) = Limits["mpa"];
                var fs = new[] { "f1", "f2", "f3" }.Select((k, i) => Num(b, k, $"공시체 {i + 1} 강도", "MPa", lo, hi, true, loOpen: true)).ToArray();
                foreach (var r in Evaluator.Read(Folder, "cylinders.csv"))   // 61차 ⑤: 같은 공시체(구역·시험일·양생·f1–f3)를 다시 넣으면 거부 (field_server.py 와 같음, client_id 재전송과 별개)
                {
                    bool same = new[] { "f1", "f2", "f3" }.Select((k, i) => double.TryParse((r.GetValueOrDefault(k) ?? "").Trim(), NumberStyles.Float, Inv, out var v) && v == fs[i]).All(x => x);
                    var td = (r.GetValueOrDefault("test_date") ?? "").Trim();
                    if (same && (r.GetValueOrDefault("zone_id") ?? "").Trim() == zid && (zid.Length > 0 || (r.GetValueOrDefault("pour_id") ?? "").Trim() == lot)
                        && (td.Length > 10 ? td[..10] : td) == Iso(d) && (r.GetValueOrDefault("curing") ?? "").Trim() == curing)
                        throw new Bad($"같은 공시체 기록이 이미 있습니다 ({(zid.Length > 0 ? zid : "타설 " + lot)} {Iso(d)} {curing} {string.Join(" · ", fs.Select(f => Py.G(f)))} MPa) — 다시 넣지 않아도 됩니다");
                }
                var row = new Dictionary<string, string> { ["zone_id"] = zid, ["test_date"] = Iso(d), ["curing"] = curing };
                for (int i = 0; i < 3; i++) row["f" + (i + 1)] = Py.G(fs[i]);
                // 채취 정보 (선택, F2): 타설 번호·채취 시각·레미콘 회사·차량 번호·보관 위치·사진 파일 이름 — 판정에는 안 씀 (field_server.py 와 같은 문구)
                row["pour_id"] = lot.Length > 0 ? lot : Text(b["pour_id"], "타설 번호", 40);   // W1: 묶음 입력이면 그 묶음
                row["sampled_at"] = "";
                row["supplier"] = Text(b["supplier"], "레미콘 회사", 40);
                row["truck_no"] = Text(b["truck_no"], "차량 번호", 20);
                row["storage_zone"] = Text(b["storage_zone"], "보관 위치", 40);
                row["photo"] = Text(b["photo"], "사진 파일 이름", 80);
                if (PyOr(b["sampled_at"]).Trim().Length > 0)
                {
                    var st = Stamp(b["sampled_at"], "채취 시각");
                    var sd = DateOnly.FromDateTime(st);
                    if (sd < pd) throw new Bad($"채취 시각 {st.ToString("yyyy-MM-dd HH:mm", Inv)} 이 타설일 {Iso(pd)} 보다 앞입니다 — 공시체는 타설 때 채취합니다");
                    if (sd > d) throw new Bad($"채취 시각 {st.ToString("yyyy-MM-dd HH:mm", Inv)} 이 시험일 {Iso(d)} 보다 뒤입니다 — 채취 시각을 확인하세요");
                    row["sampled_at"] = st.ToString("yyyy-MM-dd'T'HH:mm", Inv);
                }
                var (cph, csha) = Photo(b, zid.Length > 0 ? zid : "타설 " + lot, "cylinder", d.ToDateTime(TimeOnly.FromDateTime(Now())));   // 11-12 2-4 웹 #7 성적서·파단면 사진 1장 (API.md §10.2)
                if (cph.Length > 0) { row["photo"] = cph; row[Photos.ShaCol] = csha; info["photo"] = cph; }
                return (new() { row }, info, zid, 0);
            }
            var z = zones[zid];
            double x0 = Py.Float(z["x0"]), y0 = Py.Float(z["y0"]), x1 = Py.Float(z["x1"]), y1 = Py.Float(z["y1"]);
            double x = Blank(b["x"]) ? (x0 + x1) / 2 : Num(b, "x", "x 좌표", "m", x0, x1, false);
            double y = Blank(b["y"]) ? (y0 + y1) / 2 : Num(b, "y", "y 좌표", "m", y0, y1, false);
            double rv = Num(b, "r_value", "반발경도 R", "", Limits["r_value"].Lo, Limits["r_value"].Hi, true);
            return (new() { new() { ["zone_id"] = zid, ["x"] = Py.G(Py.Round(x, 2)), ["y"] = Py.G(Py.Round(y, 2)), ["test_date"] = Iso(d), ["r_value"] = Py.G(rv) } }, info, zid, 0);
        }
        // 센서 (온도·강도)
        var vcol = SensorValue[kind];
        var sid = PyOr(b["sensor_id"]).Trim();
        if (!IdRe.IsMatch(sid)) throw new Bad("센서 ID 를 넣으세요 (영문·숫자·한글·-·_ 20자 이내, 예: S7)");
        if (!int.TryParse(PyStr(b["floor"]).Trim(), NumberStyles.AllowLeadingSign, Inv, out var fl)) throw new Bad("층을 고르세요");
        var fz = zones.Values.Where(z => Py.Int(z["floor"]) == fl).ToList();
        if (fz.Count == 0) throw new Bad($"{fl}층 구역이 모델에 없습니다 — 층을 다시 고르세요");
        var pdays = fz.Where(z => pours.ContainsKey(z["zone_id"]!)).Select(z => PourDay(z["zone_id"]!)).ToList();
        if (pdays.Count == 0) throw new Bad($"{fl}층은 타설 기록(pours.csv)이 없습니다 — 타설 후에 기록을 넣으세요");
        var known = Sensors();
        var other = SensorValue.Keys.FirstOrDefault(k => k != kind && known.ContainsKey((k, sid)));
        if (other != null) throw new Bad($"{sid} 는 이미 {Kinds[other].Label} ID 입니다 — 다른 ID 를 쓰거나 측정 종류를 바꾸세요");
        double sx, sy;
        if (known.TryGetValue((kind, sid), out var s))
        {
            if (s.Floor != fl) throw new Bad($"{sid} 는 {s.Floor}층 센서입니다 — 층을 확인하거나 새 ID 를 쓰세요");
            (sx, sy) = (s.X, s.Y);
            foreach (var (k, v) in new[] { ("x", sx), ("y", sy) })
                if (!Blank(b[k]) && Math.Abs(Num(b, k, $"{k} 좌표", "m", -1e6, 1e6, false) - v) > 0.05)
                    throw new Bad($"{sid} 의 위치는 ({Py.G(sx)}, {Py.G(sy)}) m 로 등록돼 있습니다 — 센서를 옮겼으면 새 ID 로 넣으세요");
        }
        else
        {
            sx = Num(b, "x", "x 좌표", "m", fz.Min(z => Py.Float(z["x0"])), fz.Max(z => Py.Float(z["x1"])), false);
            sy = Num(b, "y", "y 좌표", "m", fz.Min(z => Py.Float(z["y0"])), fz.Max(z => Py.Float(z["y1"])), false);
            info["new_sensor"] = true;
        }
        var (vlo, vhi) = Limits[vcol];
        bool single = b["readings"] is null;
        List<JsonNode?> readings;
        if (single) readings = new() { b };   // {timestamp, 값} 이 요청 본문에 바로
        else if (b["readings"] is JsonArray ra && ra.Count > 0) readings = ra.ToList();
        else throw new Bad("기록이 없습니다 — 시각과 값을 넣으세요");
        if (readings.Count > MaxReadings) throw new Bad($"한 번에 {MaxReadings}줄까지 보낼 수 있습니다 — 나누어 붙여 넣으세요");
        string label = kind == "sensor_temp" ? "온도" : "추정 강도", unit = kind == "sensor_temp" ? "°C" : "MPa";
        var have = ReadRows(kind).Where(r => r["sensor_id"] == sid).Select(r => r["timestamp"]).ToHashSet();
        var rows = new List<Dictionary<string, string>>();
        int skipped = 0;
        var first = pdays.Min();
        for (int i = 0; i < readings.Count; i++)
        {
            var at = readings.Count > 1 ? $"{i + 1}번째 줄 " : "";
            if (readings[i] is not JsonObject rd) throw new Bad($"{at}형식이 틀렸습니다");
            var t = Stamp(rd["timestamp"], $"{at}시각");
            var tt = t.ToString("yyyy-MM-dd HH:mm", Inv);
            if (DateOnly.FromDateTime(t) < first) throw new Bad($"{at}시각 {tt} 이 {fl}층 타설일 {Iso(first)} 보다 앞입니다 — 타설 전 기록은 빼고 넣으세요");
            if (t >= tomorrow) throw new Bad($"{at}시각 {tt} 은(는) 아직 오지 않은 시각입니다 — 휴대폰 시계를 확인하세요");
            double v = Num(rd, vcol, $"{at}{label}", unit, vlo, vhi, true);
            var ts = t.ToString("yyyy-MM-dd'T'HH:mm", Inv);
            if (have.Contains(ts) && single) throw new Bad($"{sid} 는 {tt} 기록이 이미 있습니다 — 측정 시각을 확인하세요");
            if (have.Contains(ts)) { skipped++; continue; }   // 센서 앱의 누적 기록을 통째로 다시 붙여 넣어도 겹치지 않게
            have.Add(ts);
            rows.Add(new() { ["sensor_id"] = sid, ["floor"] = fl.ToString(Inv), ["x"] = Py.G(sx), ["y"] = Py.G(sy), ["timestamp"] = ts, [vcol] = Py.G(v) });
        }
        info["skipped"] = skipped;
        return (rows, info, sid, fl);
    }

    /// <summary>저장 후 판정 다시 계산 → 그 구역(센서는 센서가 든 구역)과 그 층 구역들</summary>
    JsonObject Answer(string kind, JsonObject b, JsonObject info)
    {
        var m = Model();
        var floors = m["floors"]!.AsArray();
        string zid = PyOr(b["zone_id"]);
        bool In(JsonNode z, double x, double y) => (double)z["x0"]! <= x && x <= (double)z["x1"]! && (double)z["y0"]! <= y && y <= (double)z["y1"]!;
        if (SensorValue.ContainsKey(kind))
        {
            var sid = b.ContainsKey("sensor_id") ? PyStr(b["sensor_id"]).Trim() : "";
            var s = m["sensors"]!.AsArray().FirstOrDefault(x => (string)x!["kind"]! == kind && (string)x["sensor_id"]! == sid);
            var f = s == null ? null : floors.FirstOrDefault(fl => (int)fl!["floor"]! == (int)s["floor"]!);
            zid = f?["zones"]!.AsArray().FirstOrDefault(z => In(z!, (double)s!["x"]!, (double)s["y"]!))?["zone_id"]?.GetValue<string>() ?? "";
        }
        var fz = floors.LastOrDefault(f => f!["zones"]!.AsArray().Any(z => (string)z!["zone_id"]! == zid));
        var zone = fz?["zones"]!.AsArray().FirstOrDefault(z => (string)z!["zone_id"]! == zid);
        if (kind == "shore_inspect")     // F16: 저장 뒤 그 구역의 풀리지 않은 NG (모든 단계) 한 줄
        {
            try { info["open_ng"] = ShoreInspect.OpenNg(Folder, Now()).TryGetValue(zid, out var ong) ? ShoreInspect.NgText(ong) : ""; }
            catch (Exception x) when (x is FormatException or KeyNotFoundException or InvalidDataException or IOException) { info["open_ng"] = ""; }   // 저장은 이미 됨 — 이 한 줄만 비움 (코드 검토 2차)
        }
        var res = new JsonObject { ["ok"] = true, ["saved"] = Kinds[kind].File, ["used_in_decision"] = kind is not ("rebound" or "shore_check" or "after_check" or "shore_spec") };
        foreach (var (k, v) in info.ToList()) { info.Remove(k); res[k] = v; }
        res["now"] = m["now"]!.GetValue<string>();
        res["zone"] = zone?.DeepClone();
        res["floor_zones"] = fz != null ? fz["zones"]!.DeepClone() : new JsonArray();
        res["warning"] = m["error"]!.GetValue<string>();
        return res;
    }

    // ── GET /api/recent: client_id 하나 = 입력 한 건 (센서 여러 줄은 묶음)
    JsonObject Recent(int n)
    {
        var items = new Dictionary<string, JsonObject>();
        foreach (var (kind, k) in Kinds)
            foreach (var r in ReadRows(kind))
            {
                var cid = r.GetValueOrDefault("client_id");
                if (string.IsNullOrEmpty(cid)) continue;
                if (!items.TryGetValue(cid, out var it))
                {
                    var point = !string.IsNullOrEmpty(r.GetValueOrDefault("zone_id")) ? r["zone_id"] : !string.IsNullOrEmpty(r.GetValueOrDefault("sensor_id")) ? r["sensor_id"]
                        : !string.IsNullOrEmpty(r.GetValueOrDefault("pour_id")) ? "타설 " + r["pour_id"] : "";   // W1 타설 묶음 공시체 (field_server.py recent)
                    var val = kind == "cylinder" ? $"{r["f1"]}·{r["f2"]}·{r["f3"]} MPa ({r["curing"]})" : kind == "rebound" ? $"R {r["r_value"]}" : "";
                    var when = !string.IsNullOrEmpty(r.GetValueOrDefault("test_date")) ? r["test_date"]
                        : !string.IsNullOrEmpty(r.GetValueOrDefault("timestamp")) ? r["timestamp"] : !string.IsNullOrEmpty(r.GetValueOrDefault("date")) ? r["date"] : !string.IsNullOrEmpty(r.GetValueOrDefault("pour_date")) ? r["pour_date"] : r.GetValueOrDefault("install_date");   // Python test_date or timestamp or date or pour_date or install_date
                    items[cid] = it = new JsonObject
                    {
                        ["client_id"] = cid, ["kind"] = kind, ["label"] = k.Label, ["point"] = point, ["floor"] = r.TryGetValue("floor", out var f) ? f : "",
                        ["when"] = when, ["value"] = val, ["count"] = 0,
                        ["entered_by"] = r.TryGetValue("entered_by", out var eb) ? eb : "", ["entered_at"] = r.TryGetValue("entered_at", out var ea) ? ea : "",
                        ["note"] = r.TryGetValue("note", out var nt) ? nt : "",
                    };
                }
                int count = (int)it["count"]! + 1;
                it["count"] = count;
                if (SensorValue.TryGetValue(kind, out var vcol))
                {
                    it["when"] = r["timestamp"];
                    it["value"] = $"{r[vcol]} {(kind == "sensor_temp" ? "°C" : "MPa")}" + (count > 1 ? $" 외 {count - 1}줄" : "");
                }
            }
        var outp = items.Values.OrderByDescending(it => (string?)it["entered_at"] ?? "", StringComparer.Ordinal).Take(n).Select(it => (JsonNode)it).ToArray();
        return new JsonObject { ["ok"] = true, ["items"] = new JsonArray(outp) };
    }
}

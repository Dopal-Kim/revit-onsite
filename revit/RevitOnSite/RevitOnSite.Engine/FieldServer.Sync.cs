// tools/server_sync.py 의 1:1 C# 이식 (v6 4단계, server/vercel/API.md): Vercel 현장 서버 ↔ 데이터 폴더 동기화.
//   push = 판정 스냅샷 (Model + 층별 FloorView + Recent + 서버 입력 검사 자료) → POST /api/sync/push (gzip).
//   pull = GET /api/sync/pull 의 휴대폰 입력 → Measure/Report 로 그대로 (LAN 서버와 같은 검사·CSV 열·사진 저장·client_id 중복 방지) → POST /api/sync/ack.
//   순서 = pull → ack → push (서버는 판정하지 않으므로 다시 판정한 스냅샷을 올려야 휴대폰에 새 판정이 보임).
//   주소·키 = 환경 변수 FIELD_SERVER_URL · FIELD_SYNC_KEY (값은 어디에도 쓰거나 출력하지 않음). Revit API 없음 — 백그라운드 스레드에서 불러도 됨.
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitOnSite.Engine;

public sealed record SyncResult(int Saved, int Duplicate, IReadOnlyDictionary<string, string> Errors, IReadOnlyList<FieldEntry> Entries, int Floors, long? Bytes, string PushedAt);

/// <summary>서버 동기화 실패 (한국어 문구 — server_sync.call 의 SystemExit 와 같은 글)</summary>
public sealed class SyncException(string message) : Exception(message);

public sealed partial class FieldServer
{
    public const int RecentN = 200;   // /api/recent 최대 n 과 같음 (server_sync.RECENT_N)
    public const string UrlVar = "FIELD_SERVER_URL", KeyVar = "FIELD_SYNC_KEY";
    public const string TokenVar = "FIELD_TOKEN", ViewTokenVar = "FIELD_VIEW_TOKEN", ReportTokenVar = "FIELD_REPORT_TOKEN";   // 휴대폰 주소 토큰 (API.md §2) — QR·화면에만

    // 휴대폰 주소 (server/vercel/API.md §4, LAN 서버와 같은 경로): 입력 /?t= · 층 보기 /view?v=&f= · 협력업체 신고 /view?r=&f=. 토큰은 주소 글자로 이스케이프
    public static string CloudInputUrl(string baseUrl, string token) => baseUrl.TrimEnd('/') + "/?t=" + Uri.EscapeDataString(token);
    public static string CloudViewUrl(string baseUrl, string viewToken, int floor) => baseUrl.TrimEnd('/') + "/view?v=" + Uri.EscapeDataString(viewToken) + "&f=" + floor.ToString(Inv);
    public static string CloudReportUrl(string baseUrl, string reportToken, int floor) => baseUrl.TrimEnd('/') + "/view?r=" + Uri.EscapeDataString(reportToken) + "&f=" + floor.ToString(Inv);
    static readonly string[] SpecCols = { "zone_id", "type", "spec", "spacing_x_mm", "spacing_y_mm", "splices", "install_date" };
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    static readonly JsonSerializerOptions SyncJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };   // 한글 그대로 (Python ensure_ascii=False)

    /// <summary>환경 변수 (Revit 을 켠 뒤 사용자 변수로 넣었으면 프로세스엔 없을 수 있어 사용자 변수도 봄). 없으면 null — 값은 출력하지 않음</summary>
    public static string? EnvVar(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v
        : Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User) is { Length: > 0 } u ? u : null;

    /// <summary>server_sync.check_data: 서버가 판정 없이 입력 검사(field_server.validate 의 JS 판)를 하는 데 필요한 자료</summary>
    JsonObject CheckData()
    {
        var ts = new JsonObject();
        foreach (var k in SensorValue.Keys)
        {
            var byId = new JsonObject();
            foreach (var r in ReadRows(k))
            {
                var sid = r.GetValueOrDefault("sensor_id") ?? "";
                if (byId[sid] is not JsonArray l) byId[sid] = l = new JsonArray();
                l.Add(r.GetValueOrDefault("timestamp"));
            }
            ts[k] = byId;
        }
        var appr = new Dictionary<string, string>();
        var req = new JsonObject();
        if (Approval.Verify(Folder).Ok)
            foreach (var r in Approval.ReadLog(Folder))
            {
                var z = r.GetValueOrDefault("zone_id") ?? "";
                var t = Evaluator.FromIso(r.GetValueOrDefault("timestamp")).ToString("yyyy-MM-dd'T'HH:mm:ss", Inv);
                if (r.GetValueOrDefault("action") == "감리 승인 기록")   // report() 문구용 — 구역별 가장 이른 승인 기록 시각
                {
                    if (!appr.TryGetValue(z, out var o) || string.CompareOrdinal(t, o) < 0) appr[z] = t;
                }
                else if (r.GetValueOrDefault("action") == "요청 제출")   // 감리 직접 확인(S3) 대상 — 구역별 마지막 요청 제출 (v6.2 W-A)
                    req[z] = new JsonObject { ["at"] = t, ["actor"] = r.GetValueOrDefault("actor") };
            }
        var zones = new JsonObject();
        foreach (var r in Evaluator.Read(Folder, "zones.csv"))
            zones[r.GetValueOrDefault("zone_id") ?? ""] = new JsonObject
            {
                ["floor"] = Py.Int(r["floor"]), ["x0"] = Py.Float(r["x0"]), ["y0"] = Py.Float(r["y0"]), ["x1"] = Py.Float(r["x1"]), ["y1"] = Py.Float(r["y1"]),
            };
        var pours = new JsonObject();
        foreach (var r in Evaluator.Read(Folder, "pours.csv"))
            pours[r.GetValueOrDefault("zone_id") ?? ""] = r.TryGetValue("pour_date", out var pd) ? pd : "";   // Python r.get("pour_date", "") — 짧은 줄은 null
        var cyl = new JsonArray();
        foreach (var r in Evaluator.Read(Folder, "cylinders.csv"))
            cyl.Add(new JsonArray(new[] { "zone_id", "test_date", "curing", "f1", "f2", "f3", "pour_id" }.Select(k => (JsonNode?)JsonValue.Create(r.GetValueOrDefault(k) ?? "")).ToArray()));
        var shoresAll = ShoreInspect.ReadShores(Folder);
        var shores = new JsonObject();
        foreach (var zid in shoresAll.Values.Select(v => v.ZoneId).Distinct())
            shores[zid] = new JsonArray(shoresAll.Where(kv => kv.Value.ZoneId == zid).Select(kv => kv.Key).Order(StringComparer.Ordinal).Select(k => (JsonNode?)JsonValue.Create(k)).ToArray());
        var stages = new JsonObject();
        foreach (var (st, its) in ShoreInspect.Stages)
            stages[st] = new JsonArray(its.Select(i => (JsonNode?)new JsonArray(i.Code, i.Label)).ToArray());
        var approved = new JsonObject();
        foreach (var (z, t) in appr) approved[z] = t;
        var evid = new JsonObject();   // 1-2 감리 화면 단계별 ✓/✕ (첫 날짜) — server_sync.check_data evidence
        foreach (var (z, l) in Evidence.LedgerAt(Folder, DateOnly.FromDateTime(Now())))
            evid[z] = new JsonObject(Evidence.Stages.Select(s => new KeyValuePair<string, JsonNode?>(s.Stage.ToString(Py.Inv), l.Dates.GetValueOrDefault(s.Stage, ""))));
        var plan = new JsonObject();
        foreach (var r in Evaluator.Read(Folder, Evidence.Ledger))
            if (r.GetValueOrDefault("stage") == "2" && r.GetValueOrDefault("result") == Evidence.PlanOk) plan[r.GetValueOrDefault("zone_id") ?? ""] = r.GetValueOrDefault("date");
        return new JsonObject
        {
            ["zones"] = zones, ["pours"] = pours, ["cylinders"] = cyl, ["sensor_ts"] = ts, ["shores"] = shores, ["stages"] = stages,
            ["after_items"] = new JsonArray(AfterCheck.Items.Select(i => (JsonNode?)new JsonArray(i.Code, i.Name)).ToArray()),
            ["mixes"] = new JsonArray(ShoringEngine.Mixes.Keys.Select(m => (JsonNode?)JsonValue.Create(m)).ToArray()),
            ["approved_at"] = approved, ["requested"] = req,
            ["seen"] = new JsonArray(seen.Order(StringComparer.Ordinal).Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()),
            ["evidence"] = evid, ["plan_confirmed"] = plan,
            ["evidence_meta"] = Provenance.EvidenceMeta(Folder, Now().ToString("yyyy-MM-dd", Py.Inv)),   // 11-12 근거 원장 단계마다 누가·언제 (API.md §10.5)
        };
    }

    /// <summary>server_sync.shore_info: 층 → (shore_spec.csv 줄들, shore_removed = (날짜, 구역) 개수 최근 날짜부터). 파일 없으면 빈 값</summary>
    static Dictionary<int, (JsonArray Spec, JsonArray Removed)> ShoreInfo(string folder)
    {
        var zfloor = new Dictionary<string, int>();
        foreach (var r in Evaluator.Read(folder, "zones.csv")) zfloor[r.GetValueOrDefault("zone_id") ?? ""] = Py.Int(r["floor"]);
        var outp = new Dictionary<int, (JsonArray, JsonArray)>();
        (JsonArray Spec, JsonArray Removed) Of(int fl) => outp.TryGetValue(fl, out var v) ? v : outp[fl] = (new JsonArray(), new JsonArray());
        foreach (var r in Evaluator.Read(folder, ShoreInput.Spec))
        {
            if (!int.TryParse((r.GetValueOrDefault("floor") ?? "").Trim(), NumberStyles.AllowLeadingSign, Inv, out var fl)) continue;   // Python int() 실패 → 건너뜀
            var o = new JsonObject();
            foreach (var k in SpecCols) o[k] = (r.GetValueOrDefault(k) ?? "").Trim();
            Of(fl).Spec.Add(o);
        }
        var cnt = new Dictionary<(int Fl, string D, string Z), int>();
        foreach (var r in Evaluator.Read(folder, ShoreInput.Removed))
        {
            var zid = (r.GetValueOrDefault("zone_id") ?? "").Trim();
            if (!zfloor.TryGetValue(zid, out var fl)) continue;
            var d = (r.GetValueOrDefault("removed_date") ?? "").Trim();
            var key = (fl, d.Length > 10 ? d[..10] : d, zid);
            cnt[key] = cnt.GetValueOrDefault(key) + 1;
        }
        foreach (var (k, n) in cnt.OrderByDescending(kv => kv.Key.D, StringComparer.Ordinal).ThenByDescending(kv => kv.Key.Z, StringComparer.Ordinal))
            Of(k.Fl).Removed.Add(new JsonObject { ["date"] = k.D, ["zone_id"] = k.Z, ["count"] = n });
        return outp;
    }

    /// <summary>server_sync.snapshot: {version, pushed_at, model, floors{"층": floor_view}, recent, check} — model.floors[i]·floors[층] 에 shore_spec·shore_removed</summary>
    public JsonObject Snapshot()
    {
        lock (gate)
        {
            var m = Model().DeepClone().AsObject();   // 층마다 Model() 을 다시 계산하지 않게 한 번 (Python store.model 고정과 같음)
            var floors = new JsonObject();
            foreach (var f in m["floors"]!.AsArray())
            {
                int fl = (int)f!["floor"]!;
                floors[fl.ToString(Inv)] = FloorView(fl, false, m);
            }
            var info = ShoreInfo(Folder);
            foreach (var f in m["floors"]!.AsArray())
            {
                int fl = (int)f!["floor"]!;
                var (spec, removed) = info.TryGetValue(fl, out var v) ? v : (new JsonArray(), new JsonArray());
                f["shore_spec"] = spec.DeepClone();
                f["shore_removed"] = removed.DeepClone();
                floors[fl.ToString(Inv)]!["shore_spec"] = spec.DeepClone();
                floors[fl.ToString(Inv)]!["shore_removed"] = removed.DeepClone();
            }
            var tl = StrengthTimeline.Timeline(Folder, cureMin: CureMin);
            return new JsonObject
            {
                ["version"] = 1, ["pushed_at"] = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", Inv), ["model"] = m, ["floors"] = floors,
                ["recent"] = Recent(RecentN)["items"]!.DeepClone(), ["check"] = CheckData(),
                ["timeline"] = tl.Dates.Count > 0 ? TimelineJson(tl) : null,   // 강도 타임라인 웹(S1) — 그림만, 판정 아님 (v6.2 W-A)
            };
        }
    }

    /// <summary>export_4d.timeline_json + steps (server_sync.snapshot 'timeline') — 웹 공유판 strength_timeline 과 같은 모양</summary>
    static JsonObject TimelineJson(StrengthTimelineResult tl)
    {
        static JsonArray Vals(IEnumerable<double?> v) => new(v.Select(x => x is double d ? (JsonNode?)JsonValue.Create(d) : null).ToArray());
        return new JsonObject
        {
            ["start"] = Py.Iso(tl.Start!.Value), ["end"] = Py.Iso(tl.End!.Value),
            ["floors"] = new JsonArray(tl.Floors.Select(f => (JsonNode?)new JsonObject { ["floor"] = f.Floor, ["label"] = f.Label, ["fck"] = f.Fck, ["mpa"] = Vals(f.Values) }).ToArray()),
            ["zones"] = new JsonArray(tl.Zones.Select(z => (JsonNode?)new JsonObject { ["zone"] = z.ZoneId, ["mpa"] = Vals(z.Values) }).ToArray()),
            ["cylinders"] = new JsonArray(tl.Cylinders.Select(c => (JsonNode?)new JsonObject { ["date"] = Py.Iso(c.Date), ["zone"] = c.ZoneId, ["floor"] = c.Floor, ["mpa"] = c.Mpa, ["curing"] = c.Curing }).ToArray()),
            ["lines"] = new JsonArray(tl.Lines.Select(l => (JsonNode?)new JsonObject { ["name"] = l.Name, ["mpa"] = l.Mpa }).ToArray()),
            ["steps"] = new JsonArray(StrengthTimeline.Steps.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()),
        };
    }

    public const string SuperviseNote = "감리 직접 확인 (서버) — 현장 확인 ①②③";
    /// <summary>supervise_rejects.csv 이유 앞머리 — 오늘 볼 것에서 '보완 요청'으로 보임 (server_sync.REJECT_TAG)</summary>
    public const string RejectTag = "감리 보완 요청";

    /// <summary>server_sync.supervise: 감리 직접 확인(S3) → approvals.csv '감리 승인 기록' (approver = 감리 이름, signed_at = 서버가 받은 시각, source = 감리 직접).
    /// 그 시각 판정이 '해체 승인 요청 가능' + 그 구역 '요청 제출' 기록 + 아직 승인·해체 기록 없음일 때만. 아니면 Bad (이유 = ack error). client_id 는 note 끝 (같은 확인 두 번 방지)</summary>
    JsonObject Supervise(JsonNode? b, string receivedAt)
    {
        if (b is not JsonObject o) throw new Bad("요청 형식이 틀렸습니다 (JSON 객체가 아님)");
        var zid = PyOr(o["zone_id"]).Trim();
        var cid = PyOr(o["client_id"]);
        if (!ClientRe.IsMatch(cid)) throw new Bad("client_id 가 없거나 형식이 틀렸습니다");
        var name = Text(o["name"], "감리 이름", 20, required: true);
        var action = PyOr(o["action"]) is { Length: > 0 } act ? act : "final";   // 1-2 (11-11): plan = 작업계획서 확인 · final = 최종 확인 · reject = 보완 요청
        if (action is not ("plan" or "final" or "reject")) throw new Bad($"action '{action}' 을 모릅니다 (plan · final · reject)");
        if (!Evaluator.Read(Folder, "zones.csv").Any(r => r.GetValueOrDefault("zone_id") == zid)) throw new Bad($"구역 '{zid}' 이 없습니다");
        if (action == "reject")   // 보완 요청 → 오늘 볼 것 (승인 기록 아님)
        {
            var why = Text(o["reason"], "보완 요청 사유", 100, required: true);
            Approval.LogSuperviseReject(Folder, zid, name, cid, receivedAt, $"{RejectTag} — {why}");
            return new JsonObject { ["duplicate"] = false };
        }
        if (action == "plan")     // 작업계획서 확인 → 근거 원장 2 단계 (doc_no = client_id, 두 번 안 넣음)
        {
            if (Evaluator.Read(Folder, Evidence.Ledger).Any(r => r.GetValueOrDefault("doc_no") == cid)) return new JsonObject { ["duplicate"] = true };
            Evidence.Add(Folder, zid, Now().ToString("yyyy-MM-dd", Py.Inv), 2, name, docNo: cid, result: Evidence.PlanOk);
            return new JsonObject { ["duplicate"] = false };
        }
        if (!(o["checks"] is JsonArray ch && ch.Count == 3 && ch.All(c => c is JsonValue v && v.GetValueKind() == JsonValueKind.True)))
            throw new Bad("현장 확인 3가지를 모두 체크해야 승인 기록을 남깁니다");
        var at = Now();
        var log = Approval.ReadLog(Folder);
        if (log.Any(r => (r.GetValueOrDefault("note") ?? "").Contains(cid, StringComparison.Ordinal))) return new JsonObject { ["duplicate"] = true };
        if (!log.Any(r => r.GetValueOrDefault("zone_id") == zid && r.GetValueOrDefault("action") == "요청 제출" && Evaluator.FromIso(r.GetValueOrDefault("timestamp")) <= at))
            throw new Bad($"{zid} 는 요청 제출 기록이 없습니다 — 시공자가 [요청서]로 요청을 제출한 구역만 감리 확인을 받습니다");
        if (Approval.StatusOverride(Folder, at).TryGetValue(zid, out var code) && code is 6 or 7 or 8)
            throw new Bad($"{zid} 는 이미 감리 승인·해체·재받침 기록이 있습니다");
        var dates = Evidence.LedgerAt(Folder, DateOnly.FromDateTime(at)).TryGetValue(zid, out var zl) ? zl.Dates : new Dictionary<int, string>();
        var miss = new[] { 2, 4 }.Where(k => !dates.ContainsKey(k)).Select(Evidence.StageName).ToList();
        if (miss.Count > 0)   // 1-1 (11-11): 최종 확인 전 작업계획서·해체 전 점검 (가이드라인 표 3.2.1)
            throw new Bad($"{zid} 는 {string.Join("·", miss)} 근거 원장 기록이 없습니다 — 계획서 확인([계획서 확인])과 해체 전 점검을 먼저");
        var row = Evaluator.Evaluate(Folder, at).FirstOrDefault(r => r.ZoneId == zid) ?? throw new Bad($"구역 '{zid}' 판정이 없습니다");   // server_sync 는 evaluate 기본 양생 하한
        try
        {
            Approval.Record(Folder, zid, "감리 승인 기록", name, Approval.RowOf(row), note: $"{SuperviseNote} · {cid}", now: at,
                approver: name, signedAt: receivedAt.Length > 16 ? receivedAt[..16] : receivedAt, source: "감리 직접");
        }
        catch (InvalidOperationException x) { throw new Bad(x.Message); }   // 판정이 '해체 승인 요청 가능'이 아님 · 사슬 손상 등 — Approval.Record 문구 그대로
        return new JsonObject { ["duplicate"] = false };
    }

    /// <summary>server_sync.call: Bearer 키, 본문 JSON (gz = gzip, Content-Type application/gzip), 답 JSON. 실패는 SyncException (한국어)</summary>
    public static JsonObject Call(string url, string key, string path, JsonNode? body = null, bool gz = false)
    {
        using var req = new HttpRequestMessage(body is null ? HttpMethod.Get : HttpMethod.Post, url.TrimEnd('/') + path);
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
        req.Headers.TryAddWithoutValidation("User-Agent", "RevitOnSite-sync/1");
        if (body is not null)
        {
            var data = Encoding.UTF8.GetBytes(body.ToJsonString(SyncJson));
            if (gz)   // 스냅샷은 gzip (센서 시각 목록이 커서 — Vercel 요청 4.5 MB 상한)
            {
                using var ms = new MemoryStream();
                using (var g = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) g.Write(data);
                data = ms.ToArray();
            }
            req.Content = new ByteArrayContent(data);
            req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(gz ? "application/gzip" : "application/json");
        }
        string text;
        System.Net.HttpStatusCode code;
        string? reason;
        try
        {
            using var res = Http.Send(req);
            code = res.StatusCode;
            reason = res.ReasonPhrase;
            using var sr = new StreamReader(res.Content.ReadAsStream(), Encoding.UTF8);
            text = sr.ReadToEnd();
        }
        catch (Exception x) when (x is HttpRequestException or TaskCanceledException or IOException)
        {
            throw new SyncException($"서버에 연결하지 못했습니다 ({(x is TaskCanceledException ? "60초 안에 답이 없음" : x.Message)}) — 인터넷과 서버 주소를 확인하세요");
        }
        if ((int)code is < 200 or > 299)
        {
            string msg = "";
            try { msg = JsonNode.Parse(text)?["error"] is JsonValue ev && ev.TryGetValue<string>(out var s) ? s : ""; } catch (JsonException) { }
            throw new SyncException($"서버 {(int)code}: {(msg.Length > 0 ? msg : reason)} ({path})");
        }
        try { return JsonNode.Parse(text) as JsonObject ?? throw new SyncException($"서버 답이 JSON 객체가 아닙니다 ({path})"); }
        catch (JsonException) { throw new SyncException($"서버 답을 읽지 못했습니다 ({path})"); }
    }

    /// <summary>server_sync.push: 스냅샷을 올림 → (층 수, 서버가 받은 바이트, 스냅샷 시각)</summary>
    public (int Floors, long? Bytes, string PushedAt) Push(string url, string key)
    {
        var snap = Snapshot();
        var res = Call(url, key, "/api/sync/push", snap, gz: true);
        return (snap["floors"]!.AsObject().Count, res["bytes"] is JsonValue b && b.TryGetValue<long>(out var n) ? n : null, (string)snap["pushed_at"]!);
    }

    /// <summary>server_sync.pull: 서버 입력 → Measure/Report (LAN 서버와 같은 길). 판정 시각 = fixedAt(데모) 또는 서버가 받은 시각(한국 시각).
    /// 성공·중복 → ack, 입력 오류 → error 붙여 ack (다시 안 받음), 파일 잠김 → ack 안 함 (다음 동기화 때 다시)</summary>
    public (int Saved, int Duplicate, Dictionary<string, string> Errors, List<FieldEntry> Entries) Pull(string url, string key, DateTime? fixedAt = null)
    {
        int saved = 0, dup = 0;
        var errors = new Dictionary<string, string>();
        var entries = new List<FieldEntry>();
        var old = FixedNow;
        try
        {
            while (true)
            {
                var res = Call(url, key, "/api/sync/pull");
                var items = res["items"] as JsonArray ?? new JsonArray();
                var acks = new JsonArray();
                foreach (var it in items)
                {
                    var cid = (string)it!["client_id"]!;
                    var at = fixedAt ?? Evaluator.FromIso(((string)it["received_at"]!)[..16]);
                    FixedNow = () => at;
                    try
                    {
                        string ua = it["ua"] is JsonValue uv && uv.TryGetValue<string>(out var us) ? us : "";
                        var body = it["body"]?.DeepClone();
                        FieldEntry? e;
                        e = null;
                        var route = (string?)it["route"];
                        var r = route == "supervise" ? Supervise(body, (string?)it["received_at"] ?? "")   // v6.2 W-A 감리 직접 확인 (S3)
                            : route == "report" ? Report(body, ua, out e) : Measure(body, ua, out e);
                        bool d = r["duplicate"] is JsonValue dv && dv.TryGetValue<bool>(out var db) && db;
                        if (d) dup++; else saved++;
                        if (e != null) entries.Add(e);
                        acks.Add(new JsonObject { ["client_id"] = cid });
                    }
                    catch (Bad x)   // 서버 검사는 통과했는데 그 사이 데이터가 바뀜 — 저장 안 하고 사유를 서버에 남김 (다시 받지 않음)
                    {
                        errors[cid] = x.Message;
                        if ((string?)it["route"] == "supervise" && it["body"] is JsonObject sb)   // W-A: 넣지 못한 감리 직접 확인 → '오늘 볼 것' 이유 (server_sync.pull 과 같게)
                            Approval.LogSuperviseReject(Folder, PyOr(sb["zone_id"]).Trim(), PyOr(sb["name"]).Trim(), cid, (string?)it["received_at"] ?? "", x.Message);
                        acks.Add(new JsonObject { ["client_id"] = cid, ["error"] = x.Message });
                    }
                    catch (Exception x) when (x is UnauthorizedAccessException or IOException)   // 파일 잠김 (Python PermissionError) → ack 안 함
                    {
                        errors[cid] = "파일 잠김 — 다음 동기화 때 다시: " + x.Message;
                    }
                }
                if (acks.Count > 0) Call(url, key, "/api/sync/ack", new JsonObject { ["items"] = acks });
                if (!(res["more"] is JsonValue mv && mv.TryGetValue<bool>(out var more) && more) || acks.Count == 0) break;
            }
        }
        finally { FixedNow = old; }
        return (saved, dup, errors, entries);
    }

    /// <summary>server_sync main 'sync': pull → ack → push</summary>
    public SyncResult Sync(string url, string key, DateTime? fixedAt = null)
    {
        var (saved, dup, errors, entries) = Pull(url, key, fixedAt);
        var (floors, bytes, at) = Push(url, key);
        return new SyncResult(saved, dup, errors, entries, floors, bytes, at);
    }
}

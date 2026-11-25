// [타설·데이터 · 현장 입력] 휴대폰 실측값 입력 서버를 켜고 끔 (docs/field_input_spec.md). 서버 = Engine.FieldServer (tools/field_server.py 이식, Revit API 없음).
//   켜는 동안 창(FieldInputWindow)이 접속 주소·QR 코드·받은 기록을 보여 주고, 창을 닫으면 서버도 끈다 (입력을 받는 때가 눈에 보이게).
//   입력이 저장되면 (서버 스레드) → Revit UI 스레드로 넘겨 대시보드 ExternalEvent 로 한 번에: 판정 다시 계산 → 구역·동바리 값, 센서 표식(SNS_Sensor) 배치·값, 대시보드.
//   계산은 엔진 한 곳 (Evaluator). 결론은 "해체 승인 요청 가능" / "존치" 까지.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using RevitOnSite.Engine;

namespace RevitOnSite
{
    public static class FieldInput
    {
        public const string GenBy = "현장 입력";              // 이 기능이 놓은 센서 표식 (구역·동바리 생성의 "01_Generate" 와 따로 — 다시 생성해도 지워지지 않음)
        const string SensorFamily = "SNS_Sensor";

        static FieldServer server;
        static Document doc;
        static Dispatcher ui;
        static DateTime? fixedNow;   // 데모: 마지막 판정 기준일이 미래면 그 09:00 을 "지금"으로 (Python --at 과 같음)
        static bool pending;         // 반영 요청이 이미 대기 중이면 또 걸지 않음 (입력이 몰려도 한 번에)

        public static bool Running => server?.Running == true;
        public static FieldServer Server => server;
        /// <summary>서버를 켠 문서 (닫히면 Dashboard 가 입력 창을 닫음 — 코드 검토 2차)</summary>
        internal static Document Doc => doc;
        public static DateTime? FixedNow => fixedNow;
        internal static event Action<string> Log;   // 창의 기록 목록 (UI 스레드에서 부름)

        /// <summary>API 문맥(명령)에서: 활성 프로젝트의 데이터 폴더로 서버를 켠다. 실패하면 예외 (한국어)</summary>
        internal static void Start(UIApplication app)
        {
            if (Running) return;
            var d = app.ActiveUIDocument.Document;
            var s = ProjectSettings.Read(d);
            if (string.IsNullOrWhiteSpace(s.CsvFolder) || !File.Exists(Path.Combine(s.CsvFolder, "zones.csv")))
                throw new InvalidOperationException("데이터 폴더에 zones.csv 가 없습니다. [프로젝트 설정]에서 데이터 폴더를 고르세요.");
            var sync = ZoneEvaluation.SyncZonesCsv(d, s.CsvFolder);   // 휴대폰 구역 타일 = 모델 구역 (zones.csv 에 없는 것만 더함, N5)
            if (sync != null) Dashboard.Log("현장 입력: " + sync);
            var shores = ShoringModel.WriteShoresCsv(d, s.CsvFolder);   // F16 휴대폰 동바리 점검 평면 = 모델 동바리 위치
            if (shores != null) Dashboard.Log("현장 입력: " + shores);
            Dashboard.Ensure();
            ui = Dispatcher.CurrentDispatcher;
            doc = d;
            SyncDate(d);
            var srv = new FieldServer(s.CsvFolder)
            {
                PagePath = Path.Combine(App.Dir, "web", "field", "index.html"),
                FixedNow = () => fixedNow,
                CureMin = s.CureMin,
                DefaultFck = s.DefaultFck,
            };
            srv.Log += m => Post(() => Log?.Invoke(m));
            srv.Saved += e => Post(() => Received(e));
            try { srv.Start(); }
            catch (System.Net.Sockets.SocketException ex)
            {
                throw new InvalidOperationException($"포트 {FieldServer.DefaultPort}–{FieldServer.DefaultPort + 9} 를 모두 다른 프로그램이 쓰고 있습니다: {ex.Message}");
            }
            server = srv;
            Dashboard.Log("현장 입력 서버 켬: 포트 " + srv.Port + ", 폴더 " + s.CsvFolder);
        }

        internal static void Stop()
        {
            if (server != null) Dashboard.Log("현장 입력 서버 끔: 포트 " + server.Port);
            server?.Stop();
            server = null;
            doc = null;
            pending = false;
        }

        /// <summary>판정 기준일이 바뀌면 (해체 판정·대시보드 [판정]) 서버의 "지금"도 맞춤. 마지막 판정일 09:00 이 미래일 때만 고정 (데모 자료)</summary>
        internal static void SyncDate(Document d) => fixedNow = DemoNow(d);

        static void Post(Action a)
        {
            try { ui?.BeginInvoke(a); } catch (Exception) { }
        }

        /// <summary>UI 스레드: 기록 한 줄 + 모델 반영 요청 (대기 중이면 합침)</summary>
        static void Received(FieldEntry e)
        {
            Log?.Invoke($"{DateTime.Now:HH:mm:ss} 저장 — {FieldServer.Kinds[e.Kind].Label} {e.Target} {(e.Rows > 1 ? e.Rows + "줄" : "")}".TrimEnd());
            if (doc == null) return;
            Timeline.DataChanged();   // 4D 사건·동시 동바리 곡선은 CSV 기록에서 — 다음 프레임에 다시 만듦 (34차)
            // 동바리 확인·협력업체 신고(shore_check)도 판정 다시 (회귀 v6-1 R26): 66차부터 신고 '뺐음' = 상태 7 해체 완료(approval.status_events)·근거 원장 7 이라
            // 대시보드 새로 고침만으로는 구역 값·근거 창 제목이 [판정] 전까지 '존치'로 남았음. 오늘 볼 것·동바리 상태 덧그림은 Apply 의 대시보드 새로 고침·Rehighlight 가 함께 함
            if (pending) return;
            pending = true;
            Dashboard.Request("현장 입력 반영", Apply);
        }

        /// <summary>API 문맥: 판정 다시 계산(서버와 같은 시각) + 센서 표식, 쓰기 한 번 (실행 취소 "Revit On-Site: 현장 입력 반영")</summary>
        static void Apply(UIApplication app)
        {
            pending = false;
            var d = doc;
            if (d == null || !d.IsValidObject || server == null) return;
            var (ok, text) = ApplyModel(app, d, fixedNow ?? DateTime.Now, "현장 입력 반영");
            Log?.Invoke($"{DateTime.Now:HH:mm:ss} " + (ok ? "모델 반영 — " : "모델 반영 못 함 — Revit 오류: ") + text);
        }

        /// <summary>API 문맥: 판정 다시 계산(at 기준) + 센서 표식, 쓰기 한 번 → 대시보드. 현장 입력(LAN)·서버 연동이 같이 씀. 반환 = (성공, 기록 문구 또는 Revit 오류)</summary>
        internal static (bool Ok, string Text) ApplyModel(UIApplication app, Document d, DateTime at, string title)
        {
            var s = ProjectSettings.Read(d);
            string note = "";
            if (!App.Write(d, title, () =>
            {
                var (rows, _) = ZoneEvaluation.Apply(d, s.CsvFolder, at, s.CureMin);
                note = $"판정 {rows.Count}구역 다시 계산 ({at:MM-dd HH:mm} 기준)" + Sensors(d, s.CsvFolder);
            }, out var err)) return (false, err);
            Dashboard.Refresh(app.ActiveUIDocument?.Document);
            ZoneFocus.Rehighlight(app);
            return (true, note);
        }

        /// <summary>데모 규칙 (SyncDate 와 같음): 마지막 판정일 09:00 이 미래면 그 시각을 '지금'으로, 아니면 null</summary>
        internal static DateTime? DemoNow(Document d)
        {
            int last = d.ProjectInformation?.LookupParameter("PRJ_LastEvalDate") is Parameter p && p.HasValue ? p.AsInteger() : 0;
            DateTime? at = last > 19000101 ? new DateTime(last / 10000, last / 100 % 100, last % 100, 9, 0, 0) : null;
            return at > DateTime.Now ? at : null;
        }

        /// <summary>열린 트랜잭션 안에서: CSV 의 센서마다 SNS_Sensor 표식이 그 층 슬래브 윗면 (x, y) 에 있게 하고 마지막 값을 적는다. 반환 = 기록 문구</summary>
        static string Sensors(Document d, string folder)
        {
            var list = new List<(string Id, string Kind, int Floor, double X, double Y, string Ts, double V)>();
            foreach (var (kind, file, col) in new[] { ("sensor_temp", "sensors.csv", "temp_c"), ("sensor_strength", "sensor_strength.csv", "mpa") })
                foreach (var g in Evaluator.Read(folder, file).GroupBy(r => r["sensor_id"]))
                {
                    var first = g.First();   // 자리 = 첫 줄 (evaluate 와 같음)
                    var last = g.OrderBy(r => r["timestamp"], StringComparer.Ordinal).Last();
                    if (double.TryParse(first["x"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x)
                        && double.TryParse(first["y"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y)
                        && int.TryParse(first["floor"], out var fl)
                        && double.TryParse(last[col], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                        list.Add((g.Key, kind, fl, x, y, last["timestamp"] ?? "", v));
                }
            if (list.Count == 0) return "";
            var symbol = new FilteredElementCollector(d).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault(f => f.Family.Name == SensorFamily);
            var path = Path.Combine(App.Dir, "Families", SensorFamily + ".rfa");
            if (symbol == null && File.Exists(path) && d.LoadFamily(path, out var fam)) symbol = d.GetElement(fam.GetFamilySymbolIds().First()) as FamilySymbol;
            if (symbol == null) return " · 센서 표식 패밀리를 찾지 못해 표식은 건너뜀";
            if (!symbol.IsActive) { symbol.Activate(); d.Regenerate(); }
            var have = new FilteredElementCollector(d).WherePasses(new FamilyInstanceFilter(d, symbol.Id)).ToElements()
                .GroupBy(e => e.LookupParameter("SNS_ID")?.AsString() ?? "").ToDictionary(g => g.Key, g => g.First());
            var floors = ZoneGenerator.DetectFloors(d).ToDictionary(f => f.FloorNo);
            int made = 0, upd = 0, skip = 0;
            foreach (var s in list)
            {
                if (!have.TryGetValue(s.Id, out var e))
                {
                    if (!floors.TryGetValue(s.Floor, out var f)) { skip++; continue; }
                    var p = new XYZ(UnitUtils.ConvertToInternalUnits(s.X, UnitTypeId.Meters), UnitUtils.ConvertToInternalUnits(s.Y, UnitTypeId.Meters), 0);
                    var fi = d.Create.NewFamilyInstance(p, symbol, f.Level, StructuralType.NonStructural);
                    // Revit 2026 은 위치 Z 를 레벨 기준 간격띄우기로 해석 → 슬래브 윗면까지 간격띄우기를 직접 (ZoneGenerator 동바리와 같음)
                    var off = fi.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM) ?? fi.get_Parameter(BuiltInParameter.INSTANCE_ELEVATION_PARAM);
                    if (off != null && !off.IsReadOnly) off.Set(f.Top - f.Level.ProjectElevation);
                    Set(fi, "SNS_ID", s.Id);
                    Set(fi, "SHR_Floor", s.Floor);
                    Set(fi, "SHR_GenBy", GenBy);
                    Set(fi, "SHR_Type", s.Kind == "sensor_temp" ? "온도 센서" : "강도 센서");
                    e = fi;
                    made++;
                }
                else upd++;
                Set(e, s.Kind == "sensor_temp" ? "SNS_LastTemp" : "SNS_fEst", s.V);
                if (DateTime.TryParse(s.Ts, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t))
                    Set(e, "SNS_LastUpdate", t.Year * 10000 + t.Month * 100 + t.Day);
            }
            return $" · 센서 표식 새로 {made} · 값 갱신 {upd}" + (skip > 0 ? $" · 층을 못 찾아 건너뜀 {skip}" : "");
        }

        static void Set(Element e, string name, object v)
        {
            var p = e.LookupParameter(name);
            if (p == null || p.IsReadOnly) return;
            if (v is string str) p.Set(str); else if (v is int n) p.Set(n); else p.Set((double)v);
        }
    }
}

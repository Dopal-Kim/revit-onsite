// Revit On-Site — 리본 (design/ui_spec.md §1–4, §8)
//   탭 "Revit On-Site" 패널 7개 · 상황별 탭 2개(AdWindows, 비공식) · 우클릭 메뉴 · 테마별 아이콘 · 툴팁 그림 · F1 도움말(docs/help)
//   버튼 정의(글자·툴팁·긴 설명·사용 조건·도움말 앵커)는 아래 Btn 표 한 곳.
//   (57차 완성도 정리: 준비 중 버튼 12개 → [타설 등록]·[공시체 결과] 구현, 나머지 10개는 겹치는 기능으로 안내하고 뺌 — 도움말 각 항목.
//    58차: 준비 중 장치(PendingCommand·NotReadyAvail)와 안 쓰는 명령 파일은 휴지통으로 — 리본 버튼은 모두 실제 명령)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using AW = Autodesk.Windows;

namespace RevitOnSite
{
    /// <summary>버튼 하나. Name = 아이콘·툴팁 그림 이름(design/icons, design/tooltips)·내부 이름, Avail = 사용 조건(구현 후), Anchor = docs/help/index.html 앵커</summary>
    public record Btn(string Name, string Text, Type Cmd, string Tip, string Long, Type Avail, string Anchor);

    public class App : IExternalApplication
    {
        public const string TabName = "Revit On-Site";
        public const string ZoneTab = "수정 | 동바리 구역", ShoreTab = "수정 | 동바리";

        /// <summary>애드인 DLL 이 있는 폴더 (공유 매개변수 파일·패밀리 포함)</summary>
        public static string Dir => Path.GetDirectoryName(typeof(App).Assembly.Location);
        static string Asm => typeof(App).Assembly.Location;
        // 도움말 = 애드인 폴더 help\ (빌드가 docs\help 를 복사 — 평가·시연 PC 에서도 F1·[?] 가 열림, 코드 검토 2차). 없으면 개발 PC 저장소의 docs\help
        static string HelpFile => File.Exists(Path.Combine(Dir, "help", "index.html")) ? Path.Combine(Dir, "help", "index.html")
            : @"C:\Users\admin\Desktop\시스템2\build-up-2\docs\help\index.html";
        // 명령별 연결 파일 docs/help/cmd/<명령>.html (열리면 ../index.html#<명령> 으로 이동) — #앵커·%인코딩 문제 없이 일반 경로로 엶 (총괄 지시)
        static string HelpPath(string anchor)
        {
            var p = Path.Combine(Path.GetDirectoryName(HelpFile), "cmd", anchor + ".html");
            return File.Exists(p) ? p : HelpFile;
        }
        internal static bool Dark => UIThemeManager.CurrentTheme == UITheme.Dark;

        /// <summary>공유 매개변수 파일의 매개변수 수 (매개변수 연결 툴팁). 파일이 없으면 0</summary>
        static int ParamCount()
        {
            try { return File.ReadLines(Path.Combine(Dir, "SHR_SharedParameters.txt")).Count(l => l.StartsWith("PARAM\t")); }
            catch (Exception) { return 0; }
        }

        // ── 버튼 표. 툴팁·긴 설명 = ui_spec §2 문구. §2 에 긴 설명이 없는 버튼은 시공자 말로 "하는 일 + 모델을 바꾸는지" (§3).
        //    화면 문구에 매개변수 이름·파일 이름·출처 태그를 쓰지 않는다 (ux_review M5) — §2 원문에 있는 PRJ_·SHR_ 표기만 예외
        const string Same = "모델은 바꾸지 않습니다.";
        static readonly Btn
            ProjectSetup = new("project_setup", "프로젝트\n설정", typeof(SettingsCommand), "첫 타설일·사이클·존치 기준을 정합니다.",
                "첫 타설일, 타설 사이클(일), 최소 존치 층수(기본 3, KCS 14 20 12 3.3.2(2)), 기상 관측소, 양생 최저 온도, 데이터 폴더. 값은 프로젝트 정보에 저장됩니다.",
                typeof(ProjectDocAvail), "CmdProjectSetup"),
            // v6.1 §1 (docs/redesign_v6.md v6.1): 열 때 확인 창(ZoneSplit.Register)과 같은 일 — 처음·다시 나누기 (아이콘 split_zone = 57차에 뺀 상황별 버튼 그림 재사용)
            SplitZones = new("split_zone", "구역\n나누기", typeof(ZoneSplitCommand), "층마다 동바리 구역만 나눕니다 (동바리 0) — 처음 나누기·다시 나누기.",
                "검측용 매개변수 연결 + 프로젝트 설정 기본값(데이터 폴더가 비었으면 모델 옆 onsite_data_<파일 이름>) + 구조 슬래브 층마다 구역(그리드 칸 → 기둥열 → 10 × 9 m 타일)을 한 번에. 동바리는 만들지 않고, 다시 나누면 있는 동바리는 지우지 않고 위치로 구역 번호를 다시 적습니다. 데이터 폴더 기록의 옛 구역 ID 는 바꾸지 않고 알려 줍니다. 모델을 바꿉니다: 매개변수·프로젝트 정보·구역 상자 (실행 취소 한 번).",
                typeof(ProjectDocAvail), "CmdZoneSplit"),
            Generate = new("generate", "예시로 채우기", typeof(GenerateCommand), "층마다 구역을 나누고 예시 동바리를 간격대로 채웁니다 (v6: 직접 입력은 [동바리] 패널).",
                "구조 바닥이 있는 레벨을 찾아 그리드 칸(없으면 지정 크기)으로 구역을 만들고, 동바리를 간격대로 배치합니다. 다시 실행하면 이전 결과를 지우고 새로 만듭니다.",
                typeof(SettingsDoneAvail), "CmdGenerate"),
            // v6 2단계 [동바리] 패널 (docs/redesign_v6.md §2): 층 입력 뷰에서 직접 놓고 고침 — 사양 = shore_spec.csv, 해체 = shore_removed.csv
            ShoreFloorView = new("shore_floor_view", "층 입력\n뷰", typeof(ShoreFloorViewCommand), "층을 골라 그 층 동바리만 보이는 평면 입력 뷰를 엽니다.",
                "'Revit On-Site 동바리 입력 – 층' 평면(그 층 슬래브 레벨, 자름 상자 = 슬래브 + 2 m, 다른 층 동바리·구역 숨김, 바닥 숨김)을 만들거나 엽니다. 그 층 사양이 없으면 [동바리 사양] 창이 먼저 뜹니다. 모델을 바꿉니다: 뷰·필터 추가 (처음 한 번).",
                typeof(ProjectDocAvail), "CmdShoreFloorView"),
            ShoreSpec = new("shore_spec", "동바리\n사양", typeof(ShoreSpecCommand), "층(·구역) 동바리 종류·규격·간격·이음·설치일을 기록하고 봅니다.",
                "층 입력 뷰에서 누르면 그 층, 아니면 층을 골라 그 층에 넣은 사양을 보고 새로 넣습니다. 데이터 폴더 shore_spec.csv 에 한 줄 — 휴대폰 입력과 같은 검사(없는 층·다른 층 구역·종류·간격·이음 정수·미래·타설일 뒤·같은 층·구역 두 번). 놓기 간격·형식이 이 값을 씁니다. 판정에는 쓰지 않고 모델은 바꾸지 않습니다.",
                typeof(ProjectDocAvail), "CmdShoreSpec"),
            ShorePlaceOne = new("shore_place_one", "한 본씩", typeof(ShorePlaceOneCommand), "누르는 점마다 동바리 한 본 (Esc 로 끝).",
                "층 입력 뷰에서 점을 누를 때마다 그 층 동바리 한 본을 놓습니다 — 높이는 아래 슬래브 윗면~이 층 슬래브 밑면, 형식은 사양, 구역 번호는 위치로. 슬래브 밖·기존 동바리 50 mm 안은 건너뜁니다. 모델을 바꿉니다: 동바리 추가 (한 본마다 실행 취소 한 번).",
                typeof(InputViewAvail), "CmdShorePlaceOne"),
            ShorePlaceLine = new("shore_place_line", "줄로", typeof(ShorePlaceLineCommand), "두 점 사이에 사양 X 간격으로 줄지어 놓습니다.",
                "시작점·끝점을 누르면 두 끝을 포함해 사양 X 간격으로 놓습니다. 슬래브 밖·기존 동바리 옆은 건너뜁니다. 모델을 바꿉니다: 동바리 추가.", typeof(InputViewAvail), "CmdShorePlaceLine"),
            ShoreFillZone = new("shore_fill_zone", "구역 채우기", typeof(ShoreFillZoneCommand), "고른 구역을 사양 X·Y 간격 격자로 채웁니다.",
                "구역을 누르면 그 구역 상자 안을 사양 간격(구역 사양이 있으면 그 값) 격자로 채웁니다. 가장자리에서 간격의 반만큼 띄움, 슬래브 밖·기존 동바리 옆은 건너뜁니다. 모델을 바꿉니다: 동바리 추가.", typeof(InputViewAvail), "CmdShoreFillZone"),
            ShoreCopyUp = new("shore_copy_up", "위층으로 복사", typeof(ShoreCopyUpCommand), "이 층 동바리 위치를 바로 위 층에 그대로 놓습니다.",
                "이 층 동바리의 평면 위치를 위 구조 슬래브 층에 놓습니다 — 높이·구역·형식은 위층 것. 위층 슬래브 밖·이미 있는 자리는 건너뜁니다. 모델을 바꿉니다: 동바리 추가.", typeof(InputViewAvail), "CmdShoreCopyUp"),
            ShorePlaceCad = new("shore_place_cad", "도면(CAD)에서", typeof(ShorePlaceCadCommand), "링크한 시공사 동바리 배치도(DWG)의 원·블록 중심마다 놓습니다.",
                "층 입력 뷰에 [삽입 › CAD 링크](현재 뷰만)로 배치도를 넣고 누르면, 도면의 원·블록을 종류·레이어·크기로 묶어 보여 줍니다. 동바리 기호 묶음을 고르면 그 중심마다 한 본 — 높이·구역·형식은 다른 놓기와 같고, 슬래브 밖·기존 동바리 50 mm 안은 건너뜁니다. 모델을 바꿉니다: 동바리 추가.", typeof(InputViewAvail), "CmdShorePlaceCad"),
            ShoreRegister = new("shore_register", "기존 부재 등록", typeof(ShoreRegisterCommand), "이미 그린 파이프·기둥을 동바리로 등록합니다.",
                "고른 파이프·구조 기둥·기둥·일반 모델(세운 부재, 이 층 높이) 가운데에 동바리를 놓습니다 — 원래 부재는 그대로 두거나 지울 수 있습니다(실행 취소 한 번). 판정·3개층 표·휴대폰은 동바리만 셉니다. 모델을 바꿉니다: 동바리 추가(·원래 부재 삭제).", typeof(InputViewAvail), "CmdShoreRegister"),
            ShoreZoneMove = new("shore_zone_move", "구역 이동", typeof(ShoreZoneMoveCommand), "고른 구역의 동바리를 함께 옮깁니다.",
                "구역을 누르고 기준점·놓을 곳을 누르면 그 구역 동바리를 한꺼번에 옮기고 구역 번호를 새 위치로 다시 적습니다. 모델을 바꿉니다: 동바리 위치.", typeof(InputViewAvail), "CmdShoreZoneMove"),
            ShoreZoneRemove = new("shore_zone_remove", "구역 해체", typeof(ShoreZoneRemoveCommand), "고른 구역 동바리를 모델에서 지우고 해체 기록을 남깁니다.",
                "구역을 누르고 확인하면 그 구역 동바리를 모델에서 지우고 위치·해체일을 데이터 폴더 shore_removed.csv 에 남깁니다(휴대폰·보고서 이력). 승인 기록(해체 완료)은 [승인 기록]에서 따로. 해체 지시·승인이 아닙니다. 모델을 바꿉니다: 동바리 삭제.", typeof(InputViewAvail), "CmdShoreZoneRemove"),
            ShoreTable = new("shore_table", "3개층 확인", typeof(ShoreTableCommand), "층마다 설치·해체·지금 본수와 3개층 존치를 봅니다.",
                "모델 동바리(지금)와 해체 기록(shore_removed.csv)으로 층마다 설치 누적·해체·지금 본수를 보여 주고, 타설된 맨 위층부터 아래 3개층(타설층 포함 [확정])에 동바리가 없으면 빨강으로 표시합니다. 판정은 바꾸지 않습니다. 모델은 바꾸지 않습니다 (데이터 폴더 shores.csv 를 모델 위치로 다시 씀).", typeof(ZonesAvail), "CmdShoreTable"),
            Diagnose = new("diagnose", "모델 진단", typeof(DiagnoseCommand), "이 모델에서 층·슬래브를 어떻게 인식했는지 보여 줍니다.",
                "레벨·슬래브·벽·기둥 개수, 인식 실패 항목(IFC 변환 모델 포함). 모델은 바꾸지 않습니다.", typeof(ProjectDocAvail), "CmdDiagnose"),
            Params = new("params", "매개변수 연결", typeof(BindParamsCommand),
                ParamCount() is int n && n > 0 ? "검측용 매개변수 " + n + "개를 연결합니다." : "검측용 매개변수를 연결합니다.",   // §2 의 "38개" 는 옛 값 → 파일에서 셈
                "공유 매개변수 파일의 SHR_·SNS_·PRJ_ 매개변수를 바인딩. 이미 있으면 건너뜀.", typeof(ProjectDocAvail), "CmdBindParams"),
            Views = new("views", "뷰·필터 준비", typeof(PrepareViewsCommand), "검측 3D 뷰와 상태 색 필터를 만듭니다.",
                "\"Revit On-Site 검측\" 3D 뷰(벽·창·계단 흐리게, 슬래브 반투명), 상태별 뷰 필터(상태 색), 일람표 2개(구역 판정표·층별 존치 현황표)를 만들거나 갱신합니다. 모델을 바꿉니다: 뷰·필터·일람표 추가.", typeof(ParamsBoundAvail), "CmdPrepareViews"),

            InstallCheck = new("install_check", "설치 검측", typeof(InstallCheckCommand), "타설 전 동바리 설치 상태를 검사합니다. (안전보건규칙 제332조의2)",
                "높이·이음·수평연결재·1본 하중을 한 번에 검사하고 결과를 동바리에 기록합니다. 모델을 바꿉니다: 동바리 검측 값.", typeof(ShoresAvail), "CmdInstallCheck"),

            Pour = new("pour", "타설 등록", typeof(PourCommand), "구역을 고르고 타설일·배합·fck 를 기록합니다.",
                "타설 기록이 없는 구역(모델에서 고른 구역이 미리 골라짐, 여러 개)을 골라 타설일·타설 시각·배합·fck 를 데이터 폴더 타설 기록에 한 줄씩 더합니다 — 휴대폰 입력과 같은 검사(미래 날짜·이미 타설한 구역·배합·fck 범위). 판정 기준일이 있으면 판정을 다시 계산합니다. 모델을 바꿉니다: 구역·동바리 판정 값.", typeof(ZonesAvail), "CmdPour"),
            FieldInput = new("field_input", "현장\n입력", typeof(FieldInputCommand), "휴대폰으로 공시체·센서 실측값을 받습니다. (인터넷 서버 QR — 서버 주소가 없으면 같은 Wi-Fi)",
                "현장 PC 에서 입력 서버를 켜고 접속 QR 코드를 보여 줍니다. 엔지니어가 휴대폰으로 구역을 골라 현장양생 공시체 강도·온도 센서·강도 센서·반발경도 값을 넣으면 데이터 폴더에 한 줄씩 더하고, 해체 판정을 다시 계산해 구역 값과 센서 표식에 적습니다. 창을 닫으면 입력을 그만 받습니다. 모델을 바꿉니다: 구역·동바리 판정 값, 센서 표식.",
                typeof(PourAvail), "CmdFieldInput"),
            ServerLink = new("server_link", "서버\n연동", typeof(ServerLinkCommand), "현장 서버(인터넷)와 휴대폰 입력·판정을 주고받기를 켜고 끕니다.",
                "켜면 바로 한 번, 그 뒤 " + RevitOnSite.ServerLink.IntervalMin + "분마다: 서버에 쌓인 휴대폰 입력을 가져와 데이터 폴더에 넣고(같은 검사·같은 열, 중복 없음) 판정을 다시 계산한 뒤, 새 판정을 서버에 올립니다(가져오기 → 확인 → 올리기). "
                + "휴대폰은 인터넷만 되면 같은 와이파이가 아니어도 됩니다. 서버 주소·키는 Windows 사용자 환경 변수 FIELD_SERVER_URL · FIELD_SYNC_KEY 로만 읽고 화면에 보이지 않습니다. 다시 누르면 끕니다. 모델을 바꿉니다: 받은 입력이 있으면 구역·동바리 판정 값, 센서 표식.",
                typeof(PourAvail), "CmdServerLink"),
            ImportSensors = new("import_sensor", "센서 자료", typeof(ImportSensorsCommand), "센서 업체 내보내기 CSV 를 읽어 데이터 폴더의 센서 기록에 합칩니다.",
                "업체 내보내기 CSV(CP949·UTF-8, 열 이름이 업체마다 달라도 됨)에서 센서 ID·측정 시각·온도(또는 강도) 열을 찾아 sensors.csv(온도)·sensor_strength.csv(강도)에 합칩니다. 위치가 파일에 없으면 이미 아는 같은 센서 위치를 쓰고, 처음 보는 센서는 가져오지 않고 알려 줍니다. 같은 센서·같은 시각은 한 줄(새 값). 판정은 다음 [판정] 때 바뀌며 모델은 바꾸지 않습니다.", typeof(ZonesAvail), "CmdImportSensors"),
            ImportCylinders = new("import_cylinder", "공시체 결과", typeof(ImportCylindersCommand), "현장양생 공시체 시험 결과를 기록합니다.",
                "타설한 구역 하나 또는 타설 묶음(같이 친 구역들)을 골라 시험일·양생 방법·강도 3개(선택: 채취 정보)를 데이터 폴더 공시체 기록에 더합니다 — 휴대폰 [공시체]와 같은 검사. 해체 판정의 기준 근거입니다. 판정 기준일이 있으면 판정을 다시 계산합니다. 모델을 바꿉니다: 구역·동바리 판정 값.", typeof(PourAvail), "CmdImportCylinders"),
            ImportWeather = new("import_weather", "기상 자료", typeof(ImportWeatherCommand), "일평균 기온(기상청 ASOS)을 읽습니다.",
                "기상자료개방포털에서 내려받은 ASOS CSV(일자료 '평균기온' 또는 시간자료 '기온', CP949·UTF-8)를 골라 데이터 폴더 weather.csv 에 합칩니다 (같은 날짜는 새 값). 센서가 없는 구역의 강도 추정에 쓰이고, 판정 근거에 출처가 붙습니다. " + Same, typeof(ZonesAvail), "CmdImportWeather"),
            EarlyForm = new("early_form", "조기 탈형 검토서", typeof(EarlyFormReviewCommand), "판넬 조기 탈형(필러 존치) 구조 검토서 초안을 구역마다 만듭니다.",
                "고른 구역(없으면 필러 '해체 승인 요청 가능' 구역, 그것도 없으면 판정된 최상층 구역 — 판넬 존치여도 검토 결과로 만듦)마다 A4 한 장: 그날 강도 근거(구역 최소 추정·현장양생 공시체 중 낮은 값), 판넬 탈형 4조건(① 14 MPa ② 10 ℃ 이상 4일 ③ 위층 타설 하중비 ≤ 1.0 ④ 근거 원장의 구조검토서 기록)과 계산 값·출처, 판넬 탈형 가능일(예측), 필러 동바리 본수(모델)와 기존 해체 판정, 빈 서명란(작성·책임기술자). 데이터 폴더 조기탈형검토서_<구역>_YYYYMMDD.html. 초안이며 해체 지시·승인이 아닙니다. 모델은 바꾸지 않습니다.",
                typeof(DecisionAvail), "CmdEarlyFormReview"),

            Evaluate = new("evaluate", "해체 판정", typeof(EvaluateCommand), "기준일에 어느 구역 동바리를 해체 승인 요청할 수 있는지 판정합니다.",
                "존치 층수·지지 슬래브 강도·하중 비교 → 현장양생 공시체 → 센서 구역 최소 강도 순으로 판정해 구역마다 '해체 승인 요청 가능' 또는 '존치'와 사유·예측 해체일을 기록하고, 'Revit On-Site 검측' 3D 뷰에 상태 색을 보여 줍니다. 근거: 거푸집 및 동바리 해체 가이드라인(2023). 승인은 감리가 합니다. 모델을 바꿉니다: 구역·동바리 판정 값, 뷰 필터.",
                typeof(PourAvail), "CmdEvaluate"),
            Dashboard = new("dashboard", "대시보드", typeof(DashboardCommand), "층·구역 현황 창을 열거나 닫습니다.",
                "오른쪽 도킹 창에 층 × 구역 상태 색과 예측 해체일, 요약(해체 승인 요청 가능·존치·공시체 시험 필요), 경고를 보여 줍니다. 칸을 누르면 그 구역을 모델에서 고르고 확대하며, 두 번 누르면 판정 근거를 보여 줍니다. 창의 [판정]은 해체 판정과 같이 모델을 바꿉니다. 창을 여닫는 것만으로는 모델을 바꾸지 않습니다.", typeof(AlwaysAvail), "CmdDashboard"),
            ShowStatus = new("show_status", "상태 표시", typeof(ShowStatusCommand), "활성 뷰의 구역 색을 바꿉니다 (임시 재지정, 모델은 그대로).",
                "강도·일정 표시로 바꾼 색을 지워 판정 상태 색(뷰 필터)으로 되돌립니다.", typeof(PlanOr3DAvail), "TglShowStatus"),
            ShowStrength = new("show_strength", "강도 표시", typeof(ShowStrengthCommand), "활성 뷰의 구역 색을 바꿉니다 (임시 재지정, 모델은 그대로).",
                "구역과 그 동바리를 추정 강도 ÷ fck 로 칠합니다: 40 % 미만부터 100 % 이상까지 5단계, 짙은 파랑일수록 강합니다. 값이 없으면 회색.", typeof(PlanOr3DAvail), "TglShowStrength"),
            ShowSchedule = new("show_schedule", "일정 표시", typeof(ShowScheduleCommand), "활성 뷰의 구역 색을 바꿉니다 (임시 재지정, 모델은 그대로).",
                "구역과 그 동바리를 오늘부터 예측 해체일까지 남은 날로 칠합니다: 지남·오늘 초록, 7일 안 연두, 14일 안 노랑, 28일 안 주황, 그 뒤 빨강. 날짜가 없으면 회색.", typeof(PlanOr3DAvail), "TglShowSchedule"),
            Reason = new("reason", "판정 근거", typeof(ReasonCommand), "선택한 구역의 공시체·센서·존치 층수 근거를 보여 줍니다.",
                "판정 사유, 현장양생 공시체 강도, 센서 추정 강도, 존치 층수 확인 결과. " + Same, typeof(ZoneSelectedAvail), "CmdReason"),
            Legend = new("legend", "색 범례", typeof(LegendCommand), "상태 색 범례를 뷰에 배치합니다.",
                "상태 색과 이름을 그린 범례 제도 뷰를 만들거나 다시 그립니다. 시트를 연 상태에서 누르면 그 시트에 놓습니다. 모델을 바꿉니다: 제도 뷰·채우기 영역 유형 추가.", typeof(GraphicViewAvail), "CmdLegend"),

            Approve = new("approve", "승인 기록", typeof(ApproveCommand), "감리 승인 결과와 해체 완료를 기록합니다. (프로그램은 승인하지 않습니다)",
                "고른 구역에 요청 제출·감리 승인 기록·해체 완료·재받침을 입력자·메모와 함께 데이터 폴더 approvals.csv 에 기록합니다. 기록마다 그때 판정 근거와 앞 기록을 묶은 해시가 남아, 고치거나 지우면 드러납니다. 요청 제출·감리 승인 기록은 '해체 승인 요청 가능' 구역만 받습니다. 프로그램의 결론은 '해체 승인 요청 가능'까지입니다. 모델을 바꿉니다: 구역·동바리 상태(감리 승인 6·해체 완료 7·재받침 8).",
                typeof(DecisionAvail), "CmdApprove"),
            Evidence = new("evidence", "근거\n원장", typeof(EvidenceCommand), "고른 구역에 해체 절차 서류(작업계획서·해체전점검·결과보고 등)를 기록합니다.",
                "단계(1 설치검측 ~ 8 결과보고)·날짜·문서번호·결과·입력자·파일을 데이터 폴더 evidence.csv 에 한 줄씩 이어 씁니다. 원본은 서명된 서류이고, 원장에는 파일 경로와 SHA-256 만 남아 파일을 바꾸면 드러납니다. 공시체·요청 제출·감리 승인 기록·해체 완료는 자동으로 읽습니다. 구역마다 갖춘 단계와 순서 누락 경고는 대시보드 칸을 두 번 누르면 보입니다. 모델은 바꾸지 않습니다.",
                typeof(ZonesAvail), "CmdEvidence"),
            Timeline = new("timeline", "강도\n타임라인", typeof(TimelineCommand), "날짜를 움직여 층별 추정 강도를 봅니다 (참고 · 판정에 안 씀).",
                "대시보드 아래 날짜 막대를 움직이거나 ▶ 로 재생하면 '강도 타임라인' 3D 뷰의 구역이 그날 추정 강도 색(추정 ÷ fck)으로 바뀌고, 그림에는 층별 추정 곡선 위 점·그날까지 현장양생 공시체 실측 ◆·기준선 5·14 MPa·fck 가 보입니다. 추정은 타설일·기온(기상 기록 또는 평년)으로 계산한 참고 값이고, 판정은 공시체·3 m 안 센서 실측으로만 합니다. 동바리 등 부재는 모델 그대로. 화면에만 그리므로 모델과 실행 취소 목록은 그대로입니다 (처음 한 번 전용 뷰를 만듭니다).", typeof(TimelineAvail), "CmdTimeline"),
            Report = new("report", "검측요청서", typeof(ReportCommand), "해체 검측요청서 초안(HTML)을 데이터 폴더에 만들고 브라우저로 엽니다.",
                "마지막 판정 기준일의 요청 가능 구역 근거(현장양생 공시체·구역 최소 추정·규칙 게이트·근거 해시)와 존치 구역 사유를 A4 한 문서로 만듭니다. 브라우저에서 인쇄하면 PDF. 서명란은 비워 둡니다. 모델은 바꾸지 않습니다.", typeof(DecisionAvail), "CmdReport"),
            FloorSign = new("floor_sign", "층 표지", typeof(FloorSignCommand), "층마다 붙일 A4 표지(HTML)를 데이터 폴더에 만들고 브라우저로 엽니다.",
                "마지막 판정 기준으로 층마다 한 장: 큰 층 이름과 상태 한 줄(동바리 해체 금지 — 존치 n구역 / 해체 금지 — 감리 확인 전 (해체 승인 요청 가능) / 감리 확인 기록 있음), 구역별 결론·예측 해체 가능일·근거 n/8·최근 동바리 확인, 판정 기준 시각, 요청 가능 구역이 있으면 마감 값. 현장 입력이 켜져 있으면 그 층으로 바로 가는 휴대폰 QR. 브라우저에서 인쇄해 층 입구에 붙입니다. 모델은 바꾸지 않습니다.",
                typeof(DecisionAvail), "CmdFloorSign"),
            Package = new("package", "검측 패키지", typeof(PackageCommand), "감리에게 넘길 검측 묶음 폴더(요청서·이력서·근거·감사 기록·파일 해시 목록)를 데이터 폴더에 만듭니다.",
                "마지막 판정 기준으로 데이터 폴더에 검측패키지_YYYYMMDD 폴더를 만들고 탐색기로 엽니다: 검측요청서 · 슬래브 이력서(요청 가능 구역만) · 감사 기록 · 근거 원장(요청 구역, 층 순) · 첨부(근거 원장에 적은 파일 사본) · 그날 IFC 가 있으면 사본 · 목록.csv 와 index.html(파일마다 SHA-256 — 받은 쪽이 빠짐·고침을 확인). 같은 날 다시 만들면 새로 씁니다. 요청 가능 구역이 없으면 만들지 않습니다. 프로그램은 승인하지 않으며, 모델은 바꾸지 않습니다.",
                typeof(DecisionAvail), "CmdPackage"),
            SlabRecord = new("slab_record", "슬래브 이력서", typeof(SlabRecordCommand), "구역마다 슬래브 품질 기록 한 장(HTML·CSV)을 데이터 폴더에 만들고 브라우저로 엽니다.",
                "마지막 판정 기준으로 구역마다 A4 한 쪽: 타설일·배합·fck, 기온 근거, 공시체(채취 정보 포함), 판정과 근거, 근거 원장 n/8 과 경고, 요청 제출·감리 승인 기록·해체 완료·재받침(감리 성명·서명 일시·줄 해시), 최근 판정 이력, 이 장의 지문. 준공 때 넘기는 품질 기록이며 해체·사용 승인이 아닙니다. 새로 계산하지 않고 모델은 바꾸지 않습니다.",
                typeof(DecisionAvail), "CmdSlabRecord"),
            ResultReport = new("result_report", "해체 결과보고", typeof(ResultReportCommand), "해체를 마친 구역마다 해체 결과보고 한 장(HTML)을 만들고 근거 원장 8단계에 남깁니다.",
                "해체 완료 기록(7)과 해체 후 점검 5항목이 있는 구역마다 A4 한 장: 해체 일시·점검 결과·침하 계측(지점별 mm, 첫날 지점과 같은지)·사진 목록. 만든 파일의 SHA-256 을 근거 원장 8 결과보고 줄로 남깁니다 (가이드라인 3.2.13). 같은 날 다시 만들면 _2 로 새 파일. 모델은 바꾸지 않습니다.",
                typeof(DecisionAvail), "CmdResultReport"),
            QualityLedger = new("quality_ledger", "품질 대장", typeof(QualityLedgerCommand), "28일 품질 대장(표준양생 공시체) HTML 한 장을 데이터 폴더에 만들고 브라우저로 엽니다.",
                "타설 구역마다 표준양생 재령 28일 공시체(1회 = 3개 평균)의 공시체 수·평균·최소와 결과(합격 / 불합격 / 확인 필요 / 시험 없음)를 KCS 14 20 10 표 3.5-7 [부분 확인] 기준으로 적습니다: 연속 3회 평균 ≥ Fcq, 1회 ≥ Fcq − 3.5 MPa (Fcq ≤ 35), 최소 9개. Fcq = 타설 fck [가정]. 해체 판정·대시보드와 별개인 참고 기록이며 품질관리자 확인용입니다. 모델은 바꾸지 않습니다.",
                typeof(PourAvail), "CmdQualityLedger"),
            ExportWeb = new("export_web", "웹 공유판", typeof(ExportWebCommand), "Revit 없이 볼 수 있는 웹 파일(HTML)로 내보냅니다 — 강도 예측 타임라인·날짜별 판정.",
                "층별 추정 강도(참고)와 날짜별 판정을 브라우저로 보는 파일 하나로 내보냅니다. " + Same, typeof(TimelineAvail), "CmdExportWeb"),
            ExportIfc = new("export_ifc", "IFC 내보내기", typeof(ExportIfcCommand), "감리가 Revit 없이 보는 IFC 파일(구역별 판정 속성 세트)을 데이터 폴더에 만듭니다.",
                "마지막 판정 기준으로 IFC4(Reference View) 파일을 데이터 폴더에 씁니다: 구역마다 속성 세트 Pset_RevitOnSite_Zone(구역 ID·층·결론·사유·상태·예측 해체일·타설일·배합·fck·공시체·구역 최소 추정·필요 존치 층수), 동바리는 형식·검측일·검측 결과. 매개변수가 아닌 값(근거 n/8·마감 값·판정 시각·무단 해체 의심)은 같은 IFC 안 Pset_RevitOnSite_Evidence 와 같은 이름 _근거.csv 에 둡니다. 무료 IFC 뷰어(BIMvision 등)에서 열어 봅니다. 모델은 바꾸지 않습니다.",
                typeof(DecisionAvail), "CmdExportIfc"),
            Audit = new("audit", "감사 기록", typeof(AuditLogCommand), "누가 언제 무엇을 바꿨는지 CSV 로 내보냅니다.",
                "승인 기록·근거 원장·동바리 확인·판정 이력·해체 후 점검을 시각 순으로 합친 감사기록_YYYYMMDD.csv 를 데이터 폴더에 씁니다(출처·입력자·세부·줄 해시). 원본 파일과 모델은 바꾸지 않습니다.", typeof(ProjectDocAvail), "CmdAuditLog"),

            // 상황별 탭 (§4) — 도움말 앵커 ctx-*
            // v6.1 §3 [구역 정보]: 상황별 구역 탭 첫 버튼 · 우클릭 첫 줄 (아이콘 inspect_record = 57차에 뺀 버튼 그림 재사용)
            ZoneInfoBtn = new("inspect_record", "구역\n정보", typeof(ZoneInfoCommand), "고른 구역을 보고 사양·타설·공시체·센서·설치 검측을 입력합니다.",
                "한 창에서 그 구역의 기본 정보(구역·층·면적·슬래브 두께·동바리 본수·높이, 읽기), 동바리 사양·타설·현장양생 공시체·센서(구역 3 m 안)·설치 검측·메모 입력(휴대폰과 같은 검사·같은 문구), 마지막 판정 한 줄과 [판정 근거]. 기록하면 판정 기준일로 판정을 다시 계산합니다(사양은 판정에 안 씀). 모델을 바꿉니다: 기록 뒤 구역·동바리 판정 값.",
                typeof(ZoneSelectedAvail), "ctx-zone_info"),
            CtxReason = Reason with { Anchor = "ctx-reason" },
            CtxPour = Pour with { Anchor = "ctx-pour", Avail = typeof(ZoneSelectedAvail) },
            CtxApprove = Approve with { Anchor = "ctx-approve" },
            SameFloor = new("same_floor", "같은 층\n보기", typeof(SameFloorCommand), "선택한 구역의 층만 검측 3D 에 남겨 같은 층 구역끼리 비교합니다.",
                "검측 3D 뷰의 단면 상자를 그 층 동바리 아래부터 슬래브 윗면까지로 자르고, 같은 층 다른 구역은 흐리게 합니다 (고른 구역은 굵은 모서리·상태색 덮개로 강조). 되돌리기는 대시보드 [전체 층]. 뷰 표시만 바뀝니다 (부재 값은 그대로).",
                typeof(ZoneSelectedAvail), "ctx-same_floor"),
            CylinderInput = new("import_cylinder", "공시체 입력", typeof(ImportCylindersCommand), "선택한 구역의 현장양생 공시체 시험 결과를 입력합니다.",
                "고른 구역의 시험일·양생 방법·강도 3개를 공시체 기록에 더합니다 — [공시체 결과]와 같은 창·검사. 모델을 바꿉니다: 구역·동바리 판정 값.", typeof(ZoneSelectedAvail), "ctx-cylinder");

        static readonly Btn[] Imports = { ImportSensors, ImportCylinders, ImportWeather };
        static readonly Btn[] Places = { ShorePlaceOne, ShorePlaceLine, ShoreFillZone, ShoreCopyUp, ShorePlaceCad, ShoreRegister };
        static readonly Btn[] Toggles = { ShowStatus, ShowStrength, ShowSchedule };
        static readonly Btn[] ZoneBtns = { ZoneInfoBtn, CtxReason, SameFloor, CtxPour, CylinderInput, CtxApprove };
        static readonly Btn[] All = new[] { ProjectSetup, SplitZones, Generate, Diagnose, Params, Views, InstallCheck, Pour, FieldInput, ServerLink,
            ShoreFloorView, ShoreSpec, ShoreZoneMove, ShoreZoneRemove, ShoreTable, EarlyForm, Evaluate, Dashboard, Reason, Legend, Approve, Evidence, Timeline, Report, FloorSign, SlabRecord, Package, ExportWeb, ExportIfc, Audit, QualityLedger, ResultReport }
            .Concat(Imports).Concat(Places).Concat(Toggles).Concat(ZoneBtns).ToArray();

        /// <summary>명령 클래스 → 주 탭 버튼 (대화상자 [?]·준비 중 안내에서 씀)</summary>
        internal static Btn Find(Type cmd) => All.FirstOrDefault(b => b.Cmd == cmd);

        static readonly List<(RibbonButton btn, string icon)> buttons = new();   // 테마가 바뀌면 아이콘 교체
        static readonly Dictionary<string, AW.RibbonTab> ctxTabs = new();
        static bool zoneSelected;

        public Result OnStartup(UIControlledApplication app)
        {
            app.CreateRibbonTab(TabName);

            // 11-12 2-5 (Revit #14: 리본 43개 중 매일 쓰는 건 6개 남짓): 큰 버튼 = 매일 쓰는 6개(판정·대시보드·현장 입력·승인 기록·근거 원장·검측요청서)만,
            // 나머지는 작은 버튼 줄 + 패널 슬라이드아웃. 패널 순서 = 하루 일 순서(판정 → 현장 입력 → 승인 → 보고) 뒤에 '가끔'(동바리·설치 검측·타임라인)·'처음 한 번'(준비). 명령은 하나도 빼지 않음
            var p = app.CreateRibbonPanel(TabName, "판정");
            Large(p, Evaluate); Large(p, Dashboard);
            var g = (RadioButtonGroup)p.AddItem(new RadioButtonGroupData("TglShow"));
            foreach (var b in Toggles) Track(g.AddItem((ToggleButtonData)Data(b, toggle: true)), b);
            p.AddSlideOut();
            Stack(p, Reason, Legend);   // [판정 기준 보기]는 57차에 뺌 — 기준값·출처는 [프로젝트 설정] › 적용 기준 한 곳

            p = app.CreateRibbonPanel(TabName, "현장 입력");
            Large(p, FieldInput);
            // 작은 버튼 하나만 따로 놓는 API 는 없어 분할 버튼과 함께 쌓는다 (AddStackedItems 는 SplitButtonData 허용, RevitAPIUI.xml)
            var st = p.AddStackedItems(Small(Pour), Small(ServerLink), new SplitButtonData("SplitImport", "데이터 가져오기"));
            Track(st[0], Pour); Track(st[1], ServerLink);
            foreach (var b in Imports) Track(((SplitButton)st[2]).AddPushButton(Data(b)), b);   // v6: 동바리 설치일은 [동바리 사양]으로 (2단계)

            // 총괄 결정 11-05 (#23, 패널 3열 이하): '승인'(시공사 기록) / '보고·공유'(받는 쪽 결과물)
            p = app.CreateRibbonPanel(TabName, "승인");
            Large(p, Approve); Large(p, Evidence);
            p.AddSlideOut();
            Large(p, Audit);

            p = app.CreateRibbonPanel(TabName, "보고·공유");
            Large(p, Report); Stack(p, FloorSign, SlabRecord, Package);
            p.AddSlideOut();                 // 가끔: 품질 대장·IFC·웹 공유판·조기 탈형 검토서
            Stack(p, QualityLedger, ExportIfc, ExportWeb); Large(p, EarlyForm); Large(p, ResultReport);

            p = app.CreateRibbonPanel(TabName, "가끔");   // 층마다 몇 번: 동바리 입력(v6 2단계)·설치 검측·강도 타임라인
            Stack(p, ShoreFloorView, ShoreSpec, InstallCheck);   // 설치 검측: 높이·이음·수평연결재·1본 하중을 한 번에 (57차)
            var sp = p.AddStackedItems(new SplitButtonData("SplitShorePlace", "놓기"), Small(ShoreZoneMove), Small(ShoreZoneRemove));
            foreach (var b in Places) Track(((SplitButton)sp[0]).AddPushButton(Data(b)), b);
            Track(sp[1], ShoreZoneMove);
            Track(sp[2], ShoreZoneRemove);
            p.AddSlideOut();
            Stack(p, ShoreTable, Timeline, Generate);   // 자동 생성은 '예시로 채우기' (v6), 강도 타임라인 = 참고 (판정에 안 씀)

            p = app.CreateRibbonPanel(TabName, "처음 한 번");
            Stack(p, ProjectSetup, SplitZones, Views);
            p.AddSlideOut();
            Stack(p, Params, Diagnose);

            ContextualTabs(app);
            RevitOnSite.Dashboard.Register(app);   // (App.Dashboard 는 버튼 표의 필드라 전체 이름으로)
            ZoneLabels.Register();                 // 검측 3D 구역 ID 글자 (화면에만)
            ZoneSplit.Register(app);               // v6.1 §1: 처음 활성화한 모델에 구조 슬래브가 있고 구역이 0개면 [구역 나누기] 확인 창 한 번
            app.SelectionChanged += OnSelectionChanged;
            app.RegisterContextMenu(typeof(App).FullName, new SelectionMenu());
            app.ThemeChanged += (s, e) =>
            {
                bool dark = Dark;
                foreach (var (b, n) in buttons) { b.Image = Icon(n, 16, dark); b.LargeImage = Icon(n, 32, dark); }
            };
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication app)
        {
            try { RevitOnSite.FieldInput.Stop(); RevitOnSite.Dashboard.Log("종료 정리: 현장 입력 서버 끔"); } catch (Exception) { }   // 현장 입력 서버 포트를 놓음
            RevitOnSite.Dashboard.Shutdown();   // 재생 타이머·결과 창·4D·구역 글자 서버 (ApplicationClosing 에서 이미 했으면 아무 일 없음) + ExternalEvent  (App.Dashboard 는 버튼 표의 필드라 전체 이름으로)
            return Result.Succeeded;
        }

        // ── 상황별 탭 (§4): 공개 API 로 탭·버튼을 만들고 AdWindows 로 contextual 표시·숨김 [비공식 API]
        static void ContextualTabs(UIControlledApplication app)
        {
            if (AW.ComponentManager.Ribbon == null) return;   // AdWindows 리본이 없으면 탭을 만들지 않고 우클릭 메뉴만
            app.CreateRibbonTab(ZoneTab);
            var p = app.CreateRibbonPanel(ZoneTab, "구역");
            Large(p, ZoneInfoBtn); Large(p, CtxReason); Large(p, CtxPour); Stack(p, SameFloor, CylinderInput, CtxApprove);   // v6.1: 첫 버튼 [구역 정보], 패널 4열
            // 57차: '편집' 패널(구역 나누기·합치기·동바리 다시 배치)과 동바리 탭(높이 맞추기·검측 결과 기록·이음 수 입력)은 준비 중이라 뺌 —
            //       [구역·동바리 생성] 다시(구역 크기·간격), 속성 창 이음 수, 휴대폰 [동바리 점검]·[설치 검측] 으로 (도움말 ctx-zone)
            try
            {
                foreach (var name in new[] { ZoneTab })
                {
                    var t = AW.ComponentManager.Ribbon.Tabs.First(x => x.Id == name || x.Title == name);
                    t.IsContextualTab = true;   // 강조색은 Revit/AdWindows 의 contextual 스타일 그대로 (색 지정 안 함)
                    t.IsVisible = false;
                    ctxTabs[name] = t;
                }
            }
            catch (Exception)
            {
                // AdWindows 가 안 되면 같은 명령은 우클릭 메뉴(SelectionMenu)로만 제공. 늘 보이는 빈 탭이 되지 않게 패널을 숨긴다
                ctxTabs.Clear();
                foreach (var panel in app.GetRibbonPanels(ZoneTab)) panel.Visible = false;
            }
        }

        static void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var doc = e.GetDocument();
            bool zone = false;
            if (doc != null)
                foreach (var id in e.GetSelectedElements())
                    if (ShoringModel.IsZone(doc.GetElement(id))) { zone = true; break; }
            zoneSelected = zone;
            ZoneFocus.SelectionChanged(doc, e.GetSelectedElements());   // 검측 뷰에서 구역을 고르면 강조 (ExternalEvent 로)
            try
            {
                if (ctxTabs.TryGetValue(ZoneTab, out var z)) z.IsVisible = zone;
            }
            catch (Exception) { ctxTabs.Clear(); }   // 비공식 API 가 깨지면 우클릭 메뉴만 남김
        }

        /// <summary>요소 우클릭 메뉴 (§1·§4 대체): 구역·동바리를 골랐을 때만 그 명령들을 붙인다. 준비 중 명령은 넣지 않음</summary>
        class SelectionMenu : IContextMenuCreator
        {
            public void BuildContextMenu(ContextMenu menu)
            {
                var bs = (zoneSelected ? ZoneBtns : Array.Empty<Btn>()).ToList();
                if (bs.Count == 0) return;
                menu.AddItem(new SeparatorItem());
                foreach (var b in bs)
                {
                    var mi = new CommandMenuItem(Plain(b.Text), b.Cmd.FullName, Asm);
                    mi.SetToolTip(b.Tip);
                    mi.SetAvailabilityClassName(b.Avail.FullName);
                    menu.AddItem(mi);
                }
            }
        }

        // ── 버튼 만들기
        static void Large(RibbonPanel p, Btn b) => Track(p.AddItem(Data(b)), b);

        static void Stack(RibbonPanel p, params Btn[] bs)
        {
            var items = bs.Length == 2 ? p.AddStackedItems(Small(bs[0]), Small(bs[1])) : p.AddStackedItems(Small(bs[0]), Small(bs[1]), Small(bs[2]));
            for (int i = 0; i < bs.Length; i++) Track(items[i], bs[i]);
        }

        /// <summary>작은(쌓은) 버튼: 큰 버튼용 두 줄 글자("층 입력 / 뷰")를 한 줄로 — 11-12 2-5 리본 정리</summary>
        static PushButtonData Small(Btn b)
        {
            var d = Data(b);
            d.Text = b.Text.Replace('\n', ' ');
            smallNames.Add(b.Name);
            return d;
        }

        static readonly HashSet<string> smallNames = new();   // 작은 버튼 이름 (SetText 가 두 줄 글자를 한 줄로)

        /// <summary>리본 버튼 글자 바꾸기 (켬·끔 상태 — [서버 연동])</summary>
        internal static void SetText(string name, string text)
        {
            foreach (var (b, n) in buttons) if (n == name) b.ItemText = smallNames.Contains(n) ? text.Replace('\n', ' ') : text;
        }

        static void Track(RibbonItem item, Btn b)
        {
            if (item is RibbonButton rb) buttons.Add((rb, b.Name));
        }

        static PushButtonData Data(Btn b, bool toggle = false)
        {
            var d = toggle ? new ToggleButtonData(b.Name, b.Text, Asm, b.Cmd.FullName) : new PushButtonData(b.Name, b.Text, Asm, b.Cmd.FullName);
            d.ToolTip = KeepArticle(b.Tip);
            d.LongDescription = KeepArticle(b.Long);
            d.AvailabilityClassName = b.Avail.FullName;
            bool dark = Dark;
            d.Image = Icon(b.Name, 16, dark);
            d.LargeImage = Icon(b.Name, 32, dark);
            d.ToolTipImage = Png("tip_" + b.Name + ".png");   // 큰 버튼 8개만 그림이 있고 나머지는 null
            d.SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, HelpUri(b.Anchor)));
            return d;
        }

        static string Plain(string text) => text.Replace("\n", " ");

        /// <summary>
        /// "(안전보건규칙 제332조의2)" 같은 조문 인용을 툴팁에서 끊기지 않게: 인용을 다음 줄로 내리고 글자 사이마다 U+2060(단어 잇기)을 넣는다.
        /// WPF 는 한글을 글자마다 줄바꿈할 수 있어 줄바꿈 없는 공백만으로는 "제332 / 조의2" 로 끊겼다 (4차 점검)
        /// </summary>
        static string KeepArticle(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s*(\(?)([^\s()]+) (제\d+조(?:의\d+)?(?:제\d+항)?)(\)?)",
            m => "\n" + string.Join("\u2060", (m.Groups[1].Value + m.Groups[2].Value + "\u00A0" + m.Groups[3].Value + m.Groups[4].Value).ToCharArray()));

        /// <summary>DLL 에 넣은 아이콘 "&lt;이름&gt;_&lt;크기&gt;[_dark].png". 없으면 null (글자만 보임)</summary>
        static BitmapSource Icon(string name, int size, bool dark) => Png($"{name}_{size}{(dark ? "_dark" : "")}.png");

        /// <summary>DLL 에 넣은 PNG (아이콘·툴팁 그림). 없으면 null</summary>
        static BitmapSource Png(string resource)
        {
            using var s = typeof(App).Assembly.GetManifestResourceStream(resource);
            if (s == null) return null;
            var f = BitmapFrame.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            // PNG 가 95.99 dpi 로 저장돼 있어 그대로 쓰면 32 px 가 32.004 DIP 로 늘어나 흐려짐 → 96 dpi 로 다시 만든다
            var px = new byte[f.PixelWidth * f.PixelHeight * 4];
            new FormatConvertedBitmap(f, PixelFormats.Bgra32, null, 0).CopyPixels(px, f.PixelWidth * 4, 0);
            var r = BitmapSource.Create(f.PixelWidth, f.PixelHeight, 96, 96, PixelFormats.Bgra32, null, px, f.PixelWidth * 4);
            r.Freeze();
            return r;
        }

        // ── F1 도움말 = docs/help/cmd/<ui_spec 명령 클래스 또는 ctx-*>.html, "file:///" + 슬래시 경로 그대로(인코딩 안 함)
        static string HelpUri(string anchor) => "file:///" + HelpPath(anchor).Replace('\\', '/');

        /// <summary>대화상자 [?] → 그 명령의 도움말 연결 파일을 일반 경로(한글 그대로)로 엶 (Chrome — OpenInBrowser)</summary>
        internal static void ShowHelp(Type cmd)
        {
            var path = HelpPath(Find(cmd).Anchor);
            try { OpenInBrowser(path); }
            catch { Message("도움말을 열지 못했습니다.", "도움말 파일: " + path, TaskDialogIcon.TaskDialogIconWarning); }
        }

        /// <summary>HTML 파일·주소를 브라우저로 — Chrome 이 있으면 Chrome (사용자 지시 11-08: 기본 브라우저 Edge 말고), 없으면 기본 브라우저.
        /// shortcut: 리본 F1(ContextualHelp)은 Revit 이 기본 브라우저로 열어 여기로 못 옴 — 대화상자 [?]·결과 HTML 만</summary>
        internal static void OpenInBrowser(string target)
        {
            var chrome = new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.LocalApplicationData }
                .Select(f => Path.Combine(Environment.GetFolderPath(f), "Google", "Chrome", "Application", "chrome.exe")).FirstOrDefault(File.Exists);
            if (chrome != null) Process.Start(new ProcessStartInfo(chrome) { ArgumentList = { target }, UseShellExecute = false });
            else Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }

        // ── 명령에서 쓰는 공통
        /// <summary>WPF 대화상자를 Revit 주 창 가운데 모달로 (ui_spec §7)</summary>
        internal static bool ShowDialog(System.Windows.Window w, UIApplication app)
        {
            new WindowInteropHelper(w).Owner = app.MainWindowHandle;
            return w.ShowDialog() == true;
        }

        /// <summary>모든 알림은 이것으로: 제목 "Revit On-Site" 한 번 (TitleAutoPrefix 를 끄지 않으면 Revit 이 애드인 이름을 앞에 또 붙임)</summary>
        internal static void Message(string instruction, string content = null, TaskDialogIcon icon = TaskDialogIcon.TaskDialogIconNone) =>
            new TaskDialog(TabName) { TitleAutoPrefix = false, MainIcon = icon, MainInstruction = instruction, MainContent = content ?? "" }.Show();

        /// <summary>결과 문구 템플릿 (ui_reference_research 7절 #10): "{행위} n건 기록" — 행위 이름이 이미 '기록'으로 끝나면 덧붙이지 않음 ("감리 승인 기록 1건")</summary>
        internal static string Recorded(string action, int n) => action.EndsWith("기록", StringComparison.Ordinal) ? $"{action} {n}건" : $"{action} {n}건 기록";

        /// <summary>성공 결과 = 모달 아닌 결과 창 하나 (ResultWindow — 열어 둔 채 Revit 작업·[판정]·Revit 종료 가능). 오류·실패·확인 질문은 Message(모달) 그대로</summary>
        internal static void ShowResult(UIApplication app, string what, string summary, string body, (string Label, Action Run)? action = null) =>
            ResultWindow.Present(app.MainWindowHandle, what, summary, body, action);

        /// <summary>오류 = TaskDialog 하나 (무엇이 실패 + 원인). 반환값을 그대로 돌려주면 Revit 이 이 명령의 변경을 되돌린다 (Write 도 이미 되돌림)</summary>
        internal static Result Fail(string what, Exception ex)
        {
            var why = ex is System.IO.IOException { HResult: unchecked((int)0x80070020) or unchecked((int)0x80070021) }
                    ? "파일이 다른 프로그램(Excel 등)에서 열려 있어 쓰지 못했습니다 — 그 파일을 닫고 다시 하세요.\n(" + ex.Message + ")"
                : ex is UnauthorizedAccessException ? "파일에 쓸 권한이 없습니다(읽기 전용 파일·보호된 폴더) — 데이터 폴더 위치를 확인하세요.\n(" + ex.Message + ")"
                : ex.Message;
            Message(what + Eul(what) + " 하지 못했습니다.", why + "\n모델은 바뀌지 않았습니다.", TaskDialogIcon.TaskDialogIconError);
            return Result.Cancelled;
        }

        /// <summary>Write 가 false 일 때: Revit 이 되돌린 이유를 알리고 Cancelled</summary>
        internal static Result WriteFailed(string what, string error)
        {
            Message(what + Eul(what) + " 하지 못했습니다.", "Revit 오류: " + error + "\n모델은 바뀌지 않았습니다.", TaskDialogIcon.TaskDialogIconError);
            return Result.Cancelled;
        }

        /// <summary>을/를 (마지막 글자 받침)</summary>
        static string Eul(string w) => w.Length > 0 && w[^1] >= '가' && w[^1] <= '힣' && (w[^1] - '가') % 28 == 0 ? "를" : "을";

        /// <summary>마지막 Write 에서 Revit 이 낸 경고 (대화상자 없이 지운 것) — 성공 알림에 덧붙일 때</summary>
        internal static List<string> LastWarnings { get; private set; } = new();
        internal static string WarningNote() => LastWarnings.Count == 0 ? "" :
            "\n\nRevit 경고 " + LastWarnings.Count + "건 (확인 창 없이 넘김): " + string.Join(" / ", LastWarnings.Distinct().Take(3)) + (LastWarnings.Distinct().Count() > 3 ? " …" : "");

        /// <summary>
        /// 모든 쓰기 명령의 공통 틀 (4차 점검 P0-2): 트랜잭션 그룹 "Revit On-Site: what" 하나 + 트랜잭션 하나.
        /// Revit 경고는 지우고(LastWarnings 에 남김), 오류가 나면 확인 창 없이 되돌린다. Commit·Assimilate 가 실제로 Committed 일 때만 true.
        /// body 가 false 를 돌려주면 (바꿀 것 없음) 그룹을 되돌리고 true — 실행 취소 목록에 빈 항목을 남기지 않음.
        /// body 의 예외는 되돌린 뒤 그대로 던진다 (호출자가 App.Fail).
        /// </summary>
        internal static bool Write(Document doc, string what, Func<bool> body, out string error)
        {
            error = null;
            var catcher = new FailureCatcher();
            LastWarnings = catcher.Warnings;
            using var g = new TransactionGroup(doc, "Revit On-Site: " + what);
            g.Start();
            bool keep;
            using (var t = new Transaction(doc, what))
            {
                var o = t.GetFailureHandlingOptions();
                o.SetFailuresPreprocessor(catcher);
                o.SetClearAfterRollback(true);
                t.SetFailureHandlingOptions(o);
                t.Start();
                try { keep = body(); }
                catch
                {
                    if (t.HasStarted() && !t.HasEnded()) t.RollBack();
                    g.RollBack();
                    throw;
                }
                if (t.Commit() != TransactionStatus.Committed)
                {
                    if (g.HasStarted() && !g.HasEnded()) g.RollBack();
                    error = catcher.Errors.Count > 0 ? string.Join("\n", catcher.Errors.Distinct()) : "Revit 이 변경을 되돌렸습니다.";
                    return false;
                }
            }
            if (!keep) { g.RollBack(); return true; }
            if (g.Assimilate() != TransactionStatus.Committed)
            {
                error = "Revit 이 변경을 되돌렸습니다.";
                return false;
            }
            return true;
        }

        internal static bool Write(Document doc, string what, Action body, out string error) => Write(doc, what, () => { body(); return true; }, out error);

        /// <summary>트랜잭션 실패 처리: 경고는 지우고 글만 모음, 오류(심각한 오류 포함)가 있으면 대화상자 없이 되돌림</summary>
        sealed class FailureCatcher : IFailuresPreprocessor
        {
            public readonly List<string> Errors = new(), Warnings = new();
            public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
            {
                bool error = false;
                foreach (var f in a.GetFailureMessages())
                {
                    if (f.GetSeverity() == FailureSeverity.Warning) { Warnings.Add(f.GetDescriptionText()); a.DeleteWarning(f); }
                    else { Errors.Add(f.GetDescriptionText()); error = true; }
                }
                return error ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }

        /// <summary>프로젝트 문서가 아니면 안내하고 null</summary>
        static string actor;
        /// <summary>입력자 실명 (11-11 Revit #13): 이 PC 에서 마지막으로 넣은 이름 — %APPDATA%\RevitOnSitector.txt. Windows 계정 이름(admin 등)은 기본값으로 안 씀</summary>
        internal static string Actor
        {
            get
            {
                if (actor != null) return actor;
                try { actor = File.ReadAllText(ActorPath).Trim(); } catch (Exception) { actor = ""; }
                return actor;
            }
            set
            {
                var v = (value ?? "").Trim();
                if (v.Length == 0 || v == actor) return;
                actor = v;
                try { Directory.CreateDirectory(Path.GetDirectoryName(ActorPath)); File.WriteAllText(ActorPath, v); } catch (Exception) { }   // 기억 못 해도 입력은 그대로
            }
        }
        static string ActorPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RevitOnSite", "actor.txt");

        /// <summary>서류 머리의 공사명: Revit [관리 › 프로젝트 정보 › 프로젝트 이름] (비었거나 기본값이면 파일 이름) — 11-11 Revit #16</summary>
        internal static string ProjectName(Document doc) => doc.ProjectInformation?.Name is { Length: > 0 } n && n != "Project Name" && n != "프로젝트 이름" ? n : doc.Title;
        internal static Document ProjectDoc(ExternalCommandData data) => ProjectDoc(data.Application);

        /// <summary>같은 규칙 — 대시보드 단추(API 문맥)에서 (v6.2 W13)</summary>
        internal static Document ProjectDoc(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc != null && !doc.IsFamilyDocument) return doc;
            Message("프로젝트 파일을 연 상태에서 눌러 주세요.");
            return null;
        }
    }
}

# Revit On-Site — 워크스트림 11개 정리 (2026-11-05 기준, 발표 2026-11-18)

- 근거: `HANDOFF.md` 0-0, `CLAUDE.md`, 정의서 `동바리검측_프로젝트구상.md`, `docs\direction_log.md`, `docs\direction_review_20261104.md`, `docs\internal_audit.md`, `docs\scenario_validation.md`, `revit\screens\ux_review*.md`(1–20차), `docs\presentation_outline.md`, `docs\presentation\demo_script.md`, `shoring\*.py`·`tools\*.py` 머리 주석, `revit\RevitOnSite\`·`web\` 폴더 구조.
- 기록에 없는 것은 "기록 없음"이라고 적었다. 숫자는 ④의 출처 파일 값 그대로다.
- 형식: ① 목표 ② 구성 방식 ③ 한 일 ④ 결과·검증 + 출처 ⑤ 산출물 ⑥ 남은 일(11-18까지) ⑦ 발표용 한 줄.

---

## 시스템 설계·규정
- ① 가이드라인·KCS 기준을 판정 규칙으로 정하고, 문서마다 표현을 맞춘다.
- ② 정의서(§3.2 기준값·§3.4 판정 우선순위·§3.5 데이터 모델) → `CLAUDE.md` 도메인 규칙 → `shoring\engine.py`·`evaluate.py` 기준본 → C# 이식. 방향 바뀜은 `docs\direction_log.md` 에 판마다(근거·방향·개발 항목·버린 것) 쌓는다.
- ③ 규정 정리(KDS 21 50 00·KCS 14 20 12·해체 가이드라인 2023.1) → 법령 조사로 서류 이름 "해체 검측요청서(감리 검측요청 서식 준용)" 확정(11-04 17:20) → 공시체 표현 "기준 근거"로 통일 → 현장 절차 대비 기능 지도·개선 20개(P1–P3) → 방향 3판 D1–D5(11-05 00:1x).
- ④ 가이드라인 공식본 p9–15 와 엔진 값 일치 [확정]: 12.33 < 15.50, 14 MPa, 3개층 (`docs\direction_log.md` 3판). 실제 절차 12단계 중 "강" 대응 6단계 (`docs\direction_review_20261104.md` §1-2).
- ⑤ `동바리검측_프로젝트구상.md`, `CLAUDE.md`, `shoring\regulation_check.md`, `docs\direction_log.md`, `docs\direction_review_20261104.md`.
- ⑥ 임대 3개층 고정 최대 1,248본 vs 1,632본 표기 — **결정 대기**. 하중비교 마감 1.64·활하중 2.0 kN/㎡ [가정] 공개(개선 6). 사용자 역할 한 장(개선 4). D3 IFC 마무리.
- ⑦ "판정 규칙은 가이드라인·KCS 원문 수치 그대로 — 수치마다 [확정]/[가정]을 붙였다."

## M1·M2 데모 모델·구역/동바리 생성
- ① 정답 대조용 15층 데모 모델과, 어느 모델에서든 구역·동바리를 자동으로 만드는 생성기.
- ② MCP 코드(`revit\mcp\base_model.cs`·`families.cs`·`setup.cs`·`generate.cs`) → `revit\shoring_demo*.rvt`. 애드인은 `ZoneGenerator.cs`(구조 슬래브 찾기 → 층 → 구역 → 동바리) + `GenerateCommand.cs`·`Wpf\GenerateWindow.xaml`. 구역 = DirectShape 상자, 동바리 = `SHR_PipeSupport.rfa`, 재실행 안전 태그 `SHR_GenBy`.
- ③ MCP로 L00–L15·그리드·슬래브 210 × 15 → 사본에 매개변수 바인딩·구역·동바리 → P3 범용 규칙(그리드 → 기둥열 → 타일) → P4 벽식 보강(벽 255·창 180·계단 15) → P5 프리캐스트 제외 → N10(고른 층 동바리만 다시 만듦)·N4(구역 윗면 +20 mm).
- ④ 구역 90, 9–12F 동바리 각 408본(계단실 8본 제외), 높이 2,790 mm, 재실행 1,754개 삭제 후 같은 재생성 6 s · RAVA3Pro 구조 슬래브 0 → 동바리 0 + 안내 · N10 통과 (`HANDOFF.md` 0-0 1주차·P4·P5·[총괄 17:25]). N4 윗면 클릭 통과 (`revit\screens\ux_review_13.md`).
- ⑤ `revit\shoring_demo.rvt`(원본), `revit\shoring_demo_onsite.rvt`(시연), `revit\shoring_demo_result.rvt`(판정 결과), `revit\families\SHR_PipeSupport.rfa`·`SNS_Sensor.rfa`, `revit\SHR_SharedParameters.txt`(43개).
- ⑥ 원본에 00_Setup·01_Generate 적용 — 사용자 결정 대기. `dynamo\01_generate.py` 는 Dynamo 안 미실행. 제출용 .rvt 확정 — 기록 없음.
- ⑦ "그리드·레벨 이름에 기대지 않고, 슬래브에서 구역과 동바리를 바로 만든다."

## M3·M4 C# 애드인·엔진 이식
- ① Python 기준 엔진을 Revit 2026 C# 애드인으로 옮기고, 바이트 일치로 증명한다.
- ② `revit\RevitOnSite\` 프로젝트 5개: `RevitOnSite`(애드인 — App·Commands 42개·Wpf xaml 16개) / `RevitOnSite.Engine`(Revit 참조 없음 — ShoringEngine·Evaluator·Schedule·InstallRules·Approval·PourGate·Evidence·FloorSign·PlanCost·FieldServer·Alerts·Qr) / `Engine.Check`·`Field.Check`·`WebExport.Check`(Python 출력과 대조). 흐름: Python 고침 → C# 1:1 → Check → 총괄 배포.
- ③ M3 리본 탭·패널 5·버튼 10 → M4 엔진 이식 → P1 화면 재작업(ui_spec) → 감사 A-01–24 이식(14:39)·FRESH_H(14:44) → 승인·게이트·원장·층 표지·임대 이식(15:57–18:55) → A-25–30(23:47) → D1·D4·D5(11-05 00:17).
- ④ M4: 24구역 판정 수치 차이 0, CSV 바이트 동일. 마지막 빌드 11-05 00:17 경고 0·오류 0, Engine "일치 시험 통과", Field 통과, WebExport 전체 일치. Field.Check 54/54 (`HANDOFF.md` 0-0 M4·13차·17차).
- ⑤ `revit\RevitOnSite\`(소스), `bin\Release\net8.0-windows\`(빌드), 배포 `%AppData%\Autodesk\Revit\Addins\2026\RevitOnSite\`.
- ⑥ 00:17 빌드 배포됨(11-05 00:20, check8 — 21차 확인 중), 00:27 빌드(IFC 포함) 배포·22차 확인 남음. `shoring\sample\zone_decisions_sample.csv` 10–12F 옛 문구 18줄 → `make_sample.py` 재생성(지금은 `--expected`). '준비 중' 회색 버튼(타설 등록·가져오기 등) 정리.
- ⑦ "Revit 안 계산은 Python 기준 엔진과 바이트 단위로 같다 — 일치 시험 세 개."

## 해체 판정·대시보드·검측요청서
- ① 구역마다 "해체 승인 요청 가능 / 존치"를 색으로 보이고, 감리에게 낼 검측요청서 초안을 뽑는다.
- ② 리본 [해체 판정](`EvaluateCommand`·`EvaluateWindow`)·도킹 대시보드(`Dashboard.cs`·`DashboardPane`: 칩·층×구역 칸·두 번 누르면 판정 근거) → `Evaluation.cs` 가 엔진 결과를 SHR_Status 등에 쓰고 필터 색 → 모델리스 `ResultWindow` → [보고서](`ReportCommand`) → `해체검측요청서_YYYYMMDD.html`. 순서: 가이드라인 규칙 게이트 → 현장양생 공시체(기준 근거) → 센서 구역 최소(위치 근거).
- ③ P2 판정·상태 코드(5·4·3/9) → 감사 게이트 이식 → N7–N9·N11 대시보드 버튼·포커스 → 결과 창 모델리스(17:35) → 요청서 "감리가 현장에서 확인할 3가지" + 마감 값(17:39) → 판넬 조기 탈형·판정 규칙 근거 한 줄.
- ④ 12-31 09:00 요청 가능 4 · 존치 20 · 공시체 0 = 정답 (`revit\screens\ux_review_7.md`·`ux_review_8.md`). 마감 값 9dea9b3a = Python (`ux_review_15.md`). 9F-B1 "5/8 · ✓·✓·✓", 서명란 빈칸 (`ux_review_19.md`). 12-28 기준 공시체 시험 필요 5 (`ux_review_20.md`).
- ⑤ `revit\onsite_data_demo\해체검측요청서_20261231.html`(점검 때 생성), `docs\sample_request_20261231.html`(옛판), `docs\help\index.html`.
- ⑥ E1 판정 사유 좌표 빈칸, E3 3D '공시체 시험 필요' 색 올리브, D4 "기록 기록" 문구 (`ux_review_19.md`·`ux_review_20.md`). 판정 사유에 KCS 14 20 12 조항 없음. D1 하중 슬래브 문구(00:17) 화면 확인.
- ⑦ "결론은 두 가지뿐 — 해체 승인 요청 가능, 존치. 서명란은 비워 둔다."

## 4D·웹 공유판
- ① 타설·양생·해체 과정을 Revit 안 4D와, Revit 없는 사람이 보는 웹 한 장으로 보여 준다.
- ② Revit: `FourD.cs`·`FourDGraphics.cs`(DirectContext3D 전용 뷰, 트랜잭션 0)·`Timeline.cs`(대시보드 ▶·❚❚, [색: 판정 상태/근거 단계]). 웹: `WebExport.cs` = `shoring\export_4d.py` → JSON(스키마 v1.1 `web\onsite_4d_schema.md`) → `web\onsite_4d.html` 한 파일. 받는 쪽 IFC: `ExportIfcCommand.cs`·`revit\ifc\Pset_RevitOnSite.txt`·`tools\check_ifc.py`(D3).
- ③ P7 4D·날짜별 뷰·웹 공유판 → 11:10 배포 → 검은 덩어리·재생 안 멈춤 수정 → N3 원인 '사실적' 표시 확정, 음영 자동 전환(15:40) → 웹에 승인 기록(16:14)·근거 원장(17:18) → 근거 단계 색 식 확정(18:16) → A-27 "(예측)" 표시(23:47).
- ④ 재생 중 건물 표시·▶❚❚ 위치 고정 통과 (`revit\screens\ux_review_10.md`). 근거 단계 색 통과(부분) — 1단계 옅은 색 구분 어려움 (`ux_review_16.md`). WebExport.Check 요소 960·활동 1,170·판정 2,560·실측 858 대조, 최신 빌드도 전체 일치 (`HANDOFF.md` 0-0).
- ⑤ `web\onsite_4d.html`, `web\onsite_4d_demo.html`(시연 대체), `web\demo_4d.json`, `web\onsite_4d_schema.md`, `revit\screens\4d_check\`.
- ⑥ D3 IFC 내보내기 진행 중 → 무료 뷰어 확인 (`docs\direction_log.md`). 4D 범례 상태 5 미확인 (`ux_review_20.md`). KBIM 4D 프레임 시간(목표 ≤ 0.5 s) 측정 — 기록 없음. A-27·A-30 웹 화면은 문법 검사만.
- ⑦ "감리·발주처는 Revit 없이 같은 판정을 웹 한 장으로 본다."

## M5 KBIM 실모델 적용
- ① 데모가 아닌 실설계 IFC(KBIM 10층)에서도 같은 기능이 도는지 보인다.
- ② `tools\ifc_survey.py`(IFC 조사)·`tools\ifc_filter.py`(축소) → `revit\mcp\ifc_open.cs`(Reference·AutoJoin 끔) → `revit\test_models\kbim10.rvt` → 사본 `kbim10_check.rvt` 에서 애드인 생성·판정 → 데이터 `revit\onsite_data_kbim`(`make_demo_data.py --from-zones`).
- ③ IFC 분석 — 공동주택이 아니라 업무시설 확인 → 기본 가져오기 75분 막힘 → 옵션 바꿔 7분 → 층 13·기준층 9구역 생성 → 16차 일반성 점검 → N13(빈 폴더 zones.csv 쓰기)·N14(첫 타설일 = 가장 아래층) 고침.
- ④ 5F 788본 h 3,950 mm(⚠3.5 m)·1F 725본 h 5,900 mm(⚠5 m) (`HANDOFF.md` 0-0 P3). 판정 54/36 = Python, 층 이름 B1·PH, 임대 A 3,152·B 3,940·C 3,152본 (`revit\screens\ux_review_17.md`). N13·N14 통과, zones 100구역 좌표 차 0 (`ux_review_18.md`). S8 높이 플래그 PASS (`docs\scenario_validation.md`).
- ⑤ `revit\test_models\kbim10.rvt`·`kbim10_check.rvt`·`kbim10.ifc`·`kbim10_structural.ifc`, `design\kbim_analysis.md`, `revit\onsite_data_kbim\`.
- ⑥ 1F·B1 작은 칸 병합(2차 과제). 근거 원장·층 표지·D1 slab_mm 의 KBIM 점검 — 기록 없음. 공동주택 대표성은 데모 모델 — 발표 한계에 적기.
- ⑦ "그리드 없는 실설계 IFC에서 1층 하부 5,900 mm 가 수평연결재·시스템 동바리·구조계산 세 규정을 한꺼번에 건다."

## 현장 입력·근거 원장·승인 기록·층 표지
- ① 휴대폰 입력과 서류 색인으로 "동바리가 있다는 확인"과 근거 사슬을 남긴다.
- ② 휴대폰 `web\field\index.html` ↔ 애드인 내장 서버 `Engine\FieldServer.cs`(= `tools\field_server.py`, 포트 8765·토큰·QR) ↔ 데이터 폴더 CSV → 판정 다시 계산. [근거 원장] `Evidence.cs`(단계 1–8, SHA-256, 추가만) · [승인 기록] `Approval.cs`(approvals.csv 해시 사슬, 행위 4가지) · [층 표지] `FloorSign.cs`(A4·QR) · 타설 전 게이트 `PourGate.cs` · 알림 `Alerts.cs`.
- ③ P8 현장 입력 서버·QR → approval.py(15:20)·pour_gate.py(15:45) → C# 이식(15:57) → 동바리 확인 입력 → 근거 원장(17:13) → 층 표지(18:55) → 무단 해체 관측·시연 시각(23:57) → "오늘 볼 것" 알림(00:17).
- ④ Field.Check 54/54, test_field_server 19 OK (`HANDOFF.md` 0-0). 동바리 확인 3건 → 13F-B1 "타설 요청 가능", 9F-B1 근거 5/8 (`revit\screens\ux_review_19.md`). Esc 닫힘 수정 확인, N15 버그 아님 (`ux_review_20.md`). 층 표지 15쪽, 마감 값 9dea9b3a = 요청서 (`ux_review_18.md`).
- ⑤ `web\field\index.html`, `tools\field_server.py`, `shoring\approval.py`·`evidence.py`·`pour_gate.py`·`alerts.py`, `shoring\templates\evidence.csv`, `docs\field_input_spec.md`, `revit\onsite_data_demo\`.
- ⑥ 무단 해체 관측·시연 시각(00:17 빌드에 포함, 배포됨) 21차 화면 확인 → 시연 장면 ② 여부. 휴대폰 실기기·Wi-Fi·방화벽 확인(사용자). 마감 값 순서(D2)·근거 원장 구역 고르기(D3). 토큰 평문 URL(A-16)은 실현장 전 과제.
- ⑦ "'없다는 기록이 없음'이 아니라 '있다는 최근 확인'을 요구한다."

## 감사 잔여 A-25–30 C# 이식
- ① 내부 감사 '낮음' 6건을 Python 기준본대로 C#에 1:1로 옮긴다.
- ② `docs\internal_audit.md` §4-3 처리 표 → Python(`schedule.py`·`evaluate.py`·`engine.py`·`export_4d.py`·`web\`) → C#(`Schedule`·`Evaluator`·`ShoringEngine`·`WebExport`·`FieldServer`) → Engine.Check 절마다 Python 대조.
- ③ A-25 공정표 해체일 그대로 하중 점검 · A-26 센서 없는 근거에 출처 태그 · A-27 웹 "(예측)" · A-28 None·"400일 안에 도달 안 함" · A-29 입력 점검 경고 · A-30 "승인 요청 가능"·큐 시각 MM-DD — 빌드 11-04 23:47.
- ④ 빌드 경고 0·오류 0, scenarios PASS 11·FAIL 0, test_field_server 19 OK, Engine 일치·Field 통과·WebExport 전체 일치 (`HANDOFF.md` 0-0 14차). 휴대폰 "승인 요청 가능"·센서 없는 구역 [가정] 태그 화면 통과 (`revit\screens\ux_review_20.md`). 감사 30건 = 높음 6·중간 12·낮음 12, 7건(A-08·09·16·19–22) 남김 (`docs\internal_audit.md`).
- ⑤ `docs\internal_audit.md` §4-3, `web\demo_4d.json`·`web\onsite_4d_demo.html`(재생성), `shoring\test_gate.py`·`test_schedule.py`.
- ⑥ `zone_decisions_sample.csv` 재생성(총괄). 4D 범례 상태 5·"(예측)" 화면 확인. 남긴 7건은 발표 한계로만 말하기.
- ⑦ "내부 감사 30건 — 거짓 안전 6건부터 고치고, 낮음까지 Python·C#을 같이 맞췄다."

## 시나리오 검증·화면 점검
- ① 엔진은 시나리오로, 애드인 화면은 실제 Revit 조작으로 확인한다.
- ② `shoring\scenarios.py`(엔진 함수를 그대로 부름) → `docs\scenario_validation.md` 자동 작성. 화면은 총괄·revit-ui 세션이 `revit\mcp\*.ps1`(캡처·클릭·UIA)과 MCP로 점검 → `revit\screens\ux_review*.md` + 캡처. 결함은 N 번호로 작업 세션에 넘긴다.
- ③ 1차(11-04 03:15) 리본·창 → 4차 생성 '심각한 오류' 발견, 5차 통과 → 7–10차 4D·대시보드(N2–N4) → 10–14차 승인·보고서·근거 원장 → 16·17차 KBIM 일반성·층 표지 → 19·20차 N15·승인 흐름.
- ④ PASS 11 · FAIL 0 · 미실행 1(S10). S1: 9F A1·B1·C1·C2 요청 가능, A2 23.90·B2 23.99 MPa 존치 (`docs\scenario_validation.md`). 화면 점검 20차까지 기록 (`revit\screens\ux_review.md`, `ux_review_2.md`–`ux_review_20.md`).
- ⑤ `docs\scenario_validation.md`, `revit\screens\ux_review*.md`, `revit\screens\ui*_*.png`, `revit\screens\4d_check\`.
- ⑥ 21차 — 23:57·00:04·00:17 빌드 화면 확인 (`docs\direction_log.md`). S10 은 MCP 확인(P5) 결과가 표에 안 들어가 '미실행' 그대로. 리허설 캡처 demo_01–07 (`docs\presentation\demo_script.md` §4).
- ⑦ "시나리오 11개 PASS·FAIL 0 — 화정형·겨울·공시체 미달·실모델 높이까지."

## 발표 준비·리허설
- ① 11-18 10분 발표(성실성 30·창의성 40·적용가능성 30)와 4분 라이브 시연을 준비한다.
- ② 10분 구성안 `docs\presentation_outline.md`(슬라이드 계획·근거 3개·예상 질문) + 4분 대본 `docs\presentation\demo_script.md`(화정형 경로 세 장면: ① 존치 확인 ② 무단 해체 의심 ③ 근거 묶음, 장면마다 대체 화면) + `qa.md`·`slides.html`. 시연 데이터 = `python tools\make_demo_data.py revit\onsite_data_demo`.
- ③ 구성안 4분 대본 개정(11-04 16:5x) → 9F-B1·13F-B1 열로 변경(18:01) → 개선 5 "세 장면" 서사 → 대본·예상 질문·슬라이드(23:35 분배) → 19차 흐름 점검(23:27–23:40).
- ④ 19차에 9F-B1·13F-B1 흐름 통과 — 휴대폰 입력은 HTTP 로, 실기기 아님 (`revit\screens\ux_review_19.md`). 진행보고서 4판 (`HANDOFF.md` 0-0 [총괄 18:45]). 발표 자료 손질은 11-05 사용자 지시로 보류 (`docs\direction_log.md`).
- ⑤ `docs\presentation_outline.md`, `docs\presentation\demo_script.md`·`qa.md`·`slides.html`·`RevitOnSite_발표_초안.pdf`, `docs\RevitOnSite_진행보고서_20261104.pdf`.
- ⑥ 임대 3개층 고정 최대 1,248 vs 1,632 — **결정 대기**(정해지기 전엔 "기준 미달 해체 11개층"만 말함). 장면 ② 배포 확인. 실기기 리허설·캡처 7장. 구성안 옛 메모·표현 정리(근거 원장 날짜 기본값, "막는", §4 근거 2 → "가이드라인 최소"). 직전 make_demo_data 재실행, 모델 저장 안 함.
- ⑦ "있다는 확인을 요구하고, 없어진 것은 드러내고, 감리가 볼 곳을 근거 묶음으로 넘긴다."

## 기온 데이터 기상청 API 연결
- ① 센서 없는 구역 강도 추정의 기온을 기상청 ASOS 관측값으로 채운다.
- ② 지금: 데이터 폴더 `weather.csv`(date, avgTa) → `evaluate.py` 일별 기온 → 없으면 `engine.py` 평년 기온 보간. 근거 문구에 "기상 기록 [입력] / 평년 기온 [가정]" 태그. [기상 자료] 버튼·설정 '기상 관측소(108 서울)' 칸은 있으나 API 를 부르는 코드는 `shoring`·`tools` 에 없다.
- ③ 정의서 §3.3 "ASOS 일평균(108 서울) → 없으면 평년값" 결정 → 평년 기온 보간 구현 → `weather.csv` 양식 → A-26 출처 태그(11-04 23:47) → 도움말 CmdImportWeather 절.
- ④ **연결 안 됨.** 도움말 [기상 자료] = "준비 중인 기능" (`docs\help\index.html` CmdImportWeather). 주석 "shortcut: 기상청 ASOS 일평균으로 교체" (`shoring\engine.py`). 시연 18구역 근거 "평년 기온 [가정]" (`revit\screens\ux_review_20.md`). 한계 문구 "기상청 API 연결 예정" (`docs\presentation_outline.md` 슬라이드 8).
- ⑤ `shoring\templates\weather.csv`(머리줄만), `docs\help\cmd\CmdImportWeather.html`(색인으로 이동). 샘플 데이터에 weather.csv 없음.
- ⑥ 연결 여부·일정 — 기록 없음(결정 필요). 기록된 안: `weather.csv` 에 source(관측/예보/평년) 열 + 단기예보 값 채우기, 응답 형식 [미확인] (`docs\benchmark_gap.md` G3). 안 하면 발표 한계 "기온은 평년값" 그대로.
- ⑦ "지금 기온은 평년값 [가정] — weather.csv 만 채우면 관측값으로 바뀐다."

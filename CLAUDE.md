# CLAUDE.md — Revit On-Site (동바리 타설·해체 검측, Revit 애드인)

이 파일은 이 저장소에서 일하는 Claude 세션이 매번 먼저 읽는 규칙이다. 현재 상태와 다음 할 일은 `HANDOFF.md` (0절 우선).

## 제품·작업 방식 (2026-11-04 사용자 결정 — 아래 Dynamo 규칙보다 우선)
- 제품 이름 **Revit On-Site**. 사용자는 현장 시공자 → 리본 탭 `Revit On-Site` 의 **C# 애드인**(Revit 2026, net8.0-windows)이 주 인터페이스. 화면은 어색함 없이: 정의서 §3.7 사용성 원칙을 지킨다. Dynamo 스크립트는 보조.
- **완전 자동화**: Claude가 Revit MCP(`mcp-server-for-revit`)로 직접 모델링·실행·확인·개선한다. 사용자에게 단계 실행을 넘기지 않는다. 사용자 몫은 서드파티 설치, Revit 대화상자, Revit 재시작 요청에 응하기.
- 계산 기준은 `engine.py`/`evaluate.py`. C# 이식본은 샘플 데이터 일치 시험을 통과해야 한다 (CLAUDE "계산은 한 곳" 규칙의 갱신).
- 애드인 코드 위치 `revit/RevitOnSite/`, 빌드 `dotnet build` (Revit API DLL은 `C:\Program Files\Autodesk\Revit 2026`, Copy Local 끔). 빌드 산출물을 애드인 폴더에 넣을 때는 Revit이 꺼져 있어야 함 → 사용자에게 재시작을 한 줄로 요청.

## 프로젝트 한 줄
Revit 모델의 슬래브·동바리·센서 위치에 타설일·기온·공시체·센서 실측값을 붙여 "어느 층 어느 구역 동바리를 언제 빼도 되는지" 판정하고, 감리 승인 서류까지 Revit 안에서 뽑는 검측 도구. 대학 BIM 과제(평가: 성실성 30 · 창의성 40 · 적용가능성 30, 10분 발표, Revit 데모 테스트, .rvt 제출).
**소음 노이즈 렌더링 주제는 폐기됨** — 다시 꺼내지 말 것.

## 저장소 지도
| 경로 | 내용 | 규칙 |
|---|---|---|
| `동바리검측_프로젝트구상.md` | 정의서: 범위, 규정 기준값, 판정 로직, 데이터 모델, 인터페이스, 일정 | 결정이 바뀌면 여기를 먼저 고친다 |
| `shoring/engine.py` | 강도 예측·해체 기준·하중비교·IDW 구역 매핑 | **표준 라이브러리만** (Dynamo Python 노드 이식용). 고치면 `python shoring/engine.py` 자체 검사 |
| `shoring/evaluate.py` | CSV → 구역별 판정표 (Dynamo 05_Evaluate와 에이전트가 같은 계산 사용) | 고치면 `python shoring/scenarios.py` (FAIL 0 확인) + `python tools/test_field_server.py` + `dotnet run --project revit\RevitOnSite\RevitOnSite.Engine.Check` |
| `shoring/sample/` | 가상 데이터(9–12층) + 생성기. 정답: 9F A1·B1·C1·C2 요청 가능, A2·B2 존치, 10F↑ 존치 층수 게이트 NG | |
| `shoring/templates/` | 빈 CSV 양식 5개 (zones·pours·sensors·cylinders·weather). 공정표 양식은 v6(11-08)에서 제거 — 일정은 동바리 설치일·타설일 직접 입력(docs/redesign_v6.md). sensor_strength·rebound 는 현장 입력 서버가 처음 저장할 때 만듦 | |
| `shoring/research_notes.md` | 사례·관행·측정법·센서 조사 근거 | |
| `revit/` | 공유 매개변수 파일(`SHR_SharedParameters.txt`, 생성기 `make_shared_params.py`), 패밀리(`families/`), 데모 모델 | 매개변수 추가는 생성기 수정 → 재생성 (GUID는 이름에서 고정) |
| `dynamo/` | Dynamo Python 노드에 붙여 넣는 코드 `NN_name.py` + 저장된 `.dyn` | 아래 Dynamo 규칙 |
| `agents/shoring-inspector/` | Claude Managed Agent (검토 묶음·대화). 설정 `agent.md`, 스킬, 채점 기준, `setup_agent.py`, `run_review.py`, `chat.py` | 사용자 등록 완료 (ID는 `HANDOFF.md`) |
| `에이전트_*.md`, `작업매뉴얼_1주차.md` | 사용자용 절차서 | 절차가 바뀌면 함께 갱신 |

## 도메인 규칙 (틀리면 안 되는 것)
- 숫자 기준에는 출처 표시를 단다: **[확정]** 원문 확인 / **[가정]** 팀 결정 / **[입력]** 현장값 / **[미확인]**. 기억으로 규정 수치를 만들지 않는다. 새 수치는 조사 후 정의서 §3.2에 출처와 함께 추가.
- 해체 판정 기준 (「거푸집 및 동바리 해체 가이드라인」 국토교통부 2023.1, 작성 한국콘크리트학회·국가건설기준센터): 다층 동바리 해체 ≥ fck, 타설층 포함 **3개층 이상 존치**, 지지 슬래브 ≥ 14 MPa, 하중비교 1.2(5.44+1.5)+1.6·2.5 = 12.33 ≤ Σ(1.2·1.64+1.6·2.0)·√(f/fck), 강도 근거는 현장양생 공시체, 감리 승인 후 해체.
- 공시체 = "기준 근거"(KCS 14 20 12·고시 제2024-879호 — 법령 조문이 아니라 표준시방서·고시라서 "법정"이라 하지 않음), 센서 = "위치 근거".
- 서류 이름은 "해체 검측요청서(감리 검측요청 서식 준용)". 법령에 해체 승인 요청서 제출 조문은 없음 — "법정 서식"·"감리 승인은 법적 의무" 표현 금지 (정의서 §3.2 승인 행).
- 판정 우선순위: 규칙 게이트 → 공시체(기준 근거) → 센서 구역 최소(위치 근거). 공시체 없으면 승인 요청 불가 (상태 4, 추정 강도와 무관).
- **판정은 실측 기준 (사용자 결정 11-05)**: 결론 = 현장양생 공시체 + 구역 3 m 안 센서(있을 때만). 먼 센서·기상 예측·시간 경과 추정은 '참고 (판정에 안 씀)' — 화면엔 회색 '예상'. 판정 근거 창은 비교 줄만("nF-XX 검측 결과 공시체 n ≥ 기준 m ✓"), 기준명·값·단위는 [프로젝트 설정] › 적용 기준 한 곳.
- 규칙 게이트 (`evaluate.py`, 구역마다): (a) 판정 시각 이전 날짜의 타설만 셈 (b) 이 구역과 평면이 겹치는 위층 구역이 모두 타설된 층을 **층 순서**로 셈(층 번호 산술 아님, B1 = −1 → 1F 도 1개층) — 타설층 포함 3개층 이상 또는 최상층까지 타설 (c) 그 지지층 각 ≥ 14 MPa·하중비 ≤ 1.0, 강도는 판정 시각 구역 최소 추정(보수 측) [가정]. 맨 위 슬래브가 타설 후 24 h [가정] 안이면 그 슬래브는 지지층이 아니라 하중으로 보고 그 아래층들로 검토. (c) 미달 → level_gate "NG(지지층)", 존치.
- 기준 근거는 양생 현장봉함·현장수중·현장기건 공시체만 (나머지는 "현장양생 아님 n건 무시"). 센서 기록 공백 > 6 h [가정] 은 기상 온도(하한 없이)로 채우고 "기록 끊김" 표시.
- 프로그램·에이전트의 결론은 **"해체 승인 요청 가능" / "존치"까지**. 승인·해체 지시 문구 금지, 서명란은 비움.
- 계산은 `engine.py`/`evaluate.py` 한 곳. Dynamo나 에이전트가 값을 따로 계산하지 않는다.

## Revit / Dynamo 코드 규칙
- 대상: Revit 2026 (Dynamo 3.x, Python 엔진 **CPython3**) / Revit 2027 (Dynamo 4.x, **PythonNet3**). 두 엔진 모두에서 도는 코드만: .NET enum 은 `int()`, IronPython 문법 금지, `DSCore.List.Flatten` 의존 금지.
- 각 `dynamo/NN_*.py` 맨 위 주석에: 입력 `IN[i]` 의 노드 종류와 의미, 출력 `OUT` 형태, 하는 일, 재실행 안전 여부.
- 쓰기 작업은 `TransactionManager.Instance.EnsureInTransaction(doc)` … `TransactionTaskDone()`. 자체 `Transaction` 열지 않기.
- **재실행 안전**: 생성 요소에 `SHR_GenBy`(값 예: `"01_Generate"`) 를 쓰고, 다시 돌리면 같은 태그 요소를 지우고 새로 만든다 (Python 노드는 요소 바인딩이 없어 중복 생성됨).
- 단위: Revit API 내부 단위는 **피트**. 좌표·길이는 `UnitUtils.ConvertToInternalUnits(값, UnitTypeId.Millimeters)` 로 변환. CSV 좌표는 m, 원점 = 건물 남서 모서리(그리드 X1·Y1 교점).
- 날짜는 공유 매개변수 Integer `YYYYMMDD` (Revit에 날짜 타입 없음). MPa·kN 값은 NUMBER.
- 요소: 구역 = Generic Model (DirectShape 상자), 동바리 = `SHR_PipeSupport.rfa` (Generic Model, 인스턴스 `높이`), 센서 = `SNS_Sensor.rfa`. 성능 때문에 상세 동바리는 데모 층만.
- 색상 필터에서 솔리드 채우기 패턴은 이름이 아니라 `IsSolidFill` 로 찾는다 (한글판 이름 다름).
- AVF는 화면 전용(.rvt에 저장 안 됨) — 제출물에 필요한 표시는 뷰 필터·일람표로.
- Dynamo Player 인터페이스: 입력 노드 "입력임(Is Input)", 결과 Watch "출력임(Is Output)". 패키지(Data-Shapes 등) 의존 금지.

## 작업 방식
- **Revit MCP 도구가 이 세션에 있으면** (로컬 Revit PC 세션): 사본 모델(`revit/shoring_demo_mcp.rvt`)에서만 실행, 실행 전에 무엇을 바꿀지 한 줄로 말하기, 확인된 로직을 `dynamo/NN_*.py` 로 옮긴다. 원본 모델은 Dynamo Player 로만 바꾼다.
- **없으면** (클라우드 세션): Revit을 실행할 수 없다. "작성함, Revit에서 미검증"이라고 정직하게 말하고 사용자에게 실행과 결과(Watch 출력, 오류 문구) 회신을 요청한다. 결과를 지어내지 않는다.
- 순수 Python 부분(엔진, CSV, 생성기)은 이 세션에서 실행·검증한 뒤 커밋.
- 커밋·푸시는 작업 단위마다. 사용자 결과물(.rvt, .rfa, .dyn)은 사용자가 Windows에서 커밋.

## 사용자와 소통 (중요)
- **한국어**로. 사용자는 건축·건설 전공 학생, Windows + PowerShell.
- **명령어는 한 번에 하나**, 각각 별도 코드 블록. 여러 줄을 한 블록에 묶지 말 것 (사용자 요청). 결과를 받은 뒤 다음 명령.
- 경로는 Windows 형식, 작업 폴더 `C:\Users\admin\Desktop\시스템2\build-up-2` (공백 없음). 매번 시작: 그 폴더에서 PowerShell → `.venv\Scripts\Activate.ps1`.
- Revit 메뉴는 한글명(영문명) 병기. 성공 여부 확인 방법을 함께 준다.
- 오류 캡처가 오면 마지막 줄부터 원인 판단 → 한 줄 원인 + 한 개 명령으로 답한다.

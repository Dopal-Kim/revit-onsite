# 화면 가독성·입력 편의 참조 조사 (2026-11-05)

- 목적: 다음 단계 '인터페이스 가독성·입력 편의성 개선' 전에 따를 기준과 사례를 모으고, 지금 화면에 적용할 후보를 고른다.
- 범위: 웹 조사(Exa) + 저장소 화면 코드 읽기(`web\field\index.html`, `revit\RevitOnSite\RevitOnSite\Wpf\*.xaml` 일부, `docs\help\index.html` 앞부분, `docs\sample_request_20261231.html`, `revit\screens\ux_review_19.md`–`22.md`). Revit은 조작하지 않았고 다른 파일도 바꾸지 않았다.
- 태그: **[확정]** 원문에서 직접 확인 · **[부분 확인]** 2차 출처나 일부만 확인 · **[미확인]** 확인 못 함(가정).
- 대비 수치는 이 조사에서 WCAG 상대 휘도 공식으로 직접 계산한 값이다.

---

## 1. 현장용 모바일 입력 UX

| 항목 | 기준·사례 | 태그 | 출처 |
|---|---|---|---|
| 글자 대비 | 본문 4.5:1, 큰 글자(24px, 굵게 18.66px) 3:1. 소수점 반올림 없이 판정 | [확정] | https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum |
| 강화 대비 | 7:1(AAA). 다만 **햇빛 아래 성능을 보장하는 기준은 아님** — 선택 목표 | [확정] | https://www.w3.org/WAI/WCAG22/quickref/ (1.4.6) |
| 야외 권고 | 업계 글은 야외에서 7:1 이상, 글자 16–18px, 터치 44→60×72px을 권함. 실험 근거는 제시하지 않음 | [부분 확인] | https://altersquare.medium.com/why-ui-ux-is-especially-hard-in-construction-tech-platforms-50fbadf341ba , https://altersquare.io/blog/mobile-first-design-for-construction-management-software-field-usability-guide |
| 비텍스트 대비 | 입력칸 테두리·초점 표시·의미 있는 그래픽 3:1 | [확정] | https://allthepossibles.com/guides/wcag-22-contrast-target-size-checker (1.4.11 요약) |
| 터치 대상 최소 | WCAG 2.5.8(AA) 24×24 CSS px(간격 예외 있음), 2.5.5(AAA) 44×44 | [확정] | https://www.w3.org/WAI/WCAG22/Understanding/target-size-minimum |
| 플랫폼 권고 | Apple 44×44pt, Material 48×48dp(약 9mm, 권장 7–10mm), 대상 사이 8dp | [확정] | https://developer.apple.com/design/tips/ , https://m3.material.io/foundations/designing/structure |
| 장갑 | 장갑 전용 수치를 정한 공식 기준은 못 찾음. 위 수치는 모두 맨손 기준. 실제 장갑으로 7분 시험(햇빛·장갑·실패한 탭 기록)을 권하는 현장 UX 글이 있음 | [부분 확인] | https://tbcbuild.net/sites/field-ui-ux/guides/seven-minute-glare-glove-jobsite-app-test |
| 한 손 | 엄지 하나로 누르는 사람 75%, 한 손으로 쥐는 사람은 50% 미만. 사람들은 **화면 가운데**를 가장 정확하게 누르고 보며, 위·아래 모서리는 보조 동작에 맞음 | [확정] | https://www.uxmatters.com/mt/archives/2017/03/design-for-fingers-touch-and-people-part-1.php , https://interactions.acm.org/archive/view/may-june-2015/fingers-thumbs-and-people |
| 오프라인 큐 (Fieldwire) | 오프라인에서 만든 작업은 번호 없이 기기에 저장되고, 연결되면 번호가 붙어 동기화를 확인할 수 있음. 동기화 중일 때 화면 위에 파란 줄이 움직임. 앱을 지우면 아직 안 보낸 기록이 사라진다고 경고함 | [확정] | https://help.fieldwire.com/hc/en-us/articles/360017420132 , https://help.fieldwire.com/hc/en-us/articles/360043052832-Field-Guide-to-Syncing , https://help.fieldwire.com/hc/en-us/articles/360000567483 |
| 오프라인 큐 (SmartQHSE) | "Offline — 3 items queued"처럼 큐 개수와 항목마다 "Saved locally"를 보여 주고, 연결되면 자동으로 보냄. 장갑용 큰 탭, 햇빛 고대비, 한 손 조작을 내세움(수치는 없음) | [부분 확인] 마케팅 페이지 | https://www.smartqhse.com/features/mobile-app |
| Procore 모바일 | 목록 상태를 색 대신 **글자가 든 알약 모양**으로 바꿔 색을 잘못 읽는 일을 줄였고, 날짜를 "Mon, 00, 0000"처럼 풀어 써서 읽는 시간을 줄였다고 함(디자이너 포트폴리오) | [부분 확인] | https://www.deniseanddesign.com/procore-mobile-redesign |
| 국내 앱 | 세이프디(SafeD)는 "오프라인 환경 대비"를 설명하고, 최근 업데이트로 화면 확대·축소를 넣었음. 안전달인·Dr.Safety는 사진·서명·자동 서식 위주로 홍보함. **공개된 화면 설계 수치는 없음** | [부분 확인] | https://play.google.com/store/apps/details?id=kr.co.yido.safeD , http://www.sgmaster.app/ , https://safe.drcts.com/ |
| Autodesk Build/PlanGrid | 이번 조사에서는 공개 디자인·도움말 근거를 찾지 못함 | [미확인] | — |

## 2. Revit 애드인 UI

| 항목 | 지침 | 태그 | 출처 |
|---|---|---|---|
| 리본 위치 | 모델을 바꾸는 앱은 Add-Ins 탭, 분석만 하는 앱은 Analyze 탭에 둠(둘 다는 금지). 패널이 여럿이면 앱 이름으로 탭을 따로 만듦 | [확정] | https://help.autodesk.com/cloudhelp/2018/ENU/Revit-API/Revit_API_Developers_Guide/Appendices/API_User_Interface_Guidelines/Ribbon_Guidelines.html , https://aps.autodesk.com/marketplace/publisher-center/revit-publisher-guidelines |
| 패널 배치 | 맨 왼쪽은 **가장 자주 쓰는 명령의 큰 버튼**(패널이 접히면 이 아이콘이 대표로 남음). 패널은 3열 이하. 작은 버튼은 2–3개씩 쌓음. 설정·도움말·정보는 슬라이드아웃으로 | [확정] | 위 Ribbon Guidelines |
| 도움말 | 툴팁 + 긴 설명(LongDescription) + F1 도움말(SetContextualHelp) 권장 | [확정] | https://help.autodesk.com/cloudhelp/2025/ENU/Revit-API/files/Revit_API_Developers_Guide/Introduction/Add_In_Integration/Revit_API_Revit_API_Developers_Guide_Introduction_Add_In_Integration_Ribbon_Panels_and_Controls_html.html |
| 대화상자 | 자주 하는 일은 모달 없이, 드문 일은 모달로. 화면 가운데에 뜨고 바로 포커스를 받음. 내용이 바뀌는 창은 크기 조절 가능하게. 사용자는 **훑어보다 찾으면 멈추고, 필요할 때만 스크롤함** → 시작–중간–끝 경로로 배치 | [확정] | https://help.autodesk.com/cloudhelp/2018/ENU/Revit-API/Revit_API_Developers_Guide/Appendices/API_User_Interface_Guidelines/Dialog_Guidelines.html |
| 도킹 창 | WPF Page 기반, 위·아래·좌·우·떠 있기·탭(프로젝트 탐색기 뒤) 가능. InitialState·MinimumWidth 지정 | [확정] | https://help.autodesk.com/cloudhelp/2024/ENU/Revit-API/files/Revit_API_Developers_Guide/Advanced_Topics/Revit_API_Revit_API_Developers_Guide_Advanced_Topics_Dockable_Dialog_Panes_html.html |
| 결과·경고 | TaskDialog = 굵은 요약 한 줄(MainInstruction) + 본문 + 접는 상세(ExpandedContent) + 바로 할 일 버튼(CommandLink, 최대 4개) + 아이콘(정보·경고·오류) | [확정] API / [부분 확인] 사용 요령 | https://www.revitapidocs.com/2026/9f1e9744-8327-83a7-54fb-0730d479fb30.htm , https://blog.triplezero.xyz/beginners-guide-revit-api-taskdialog/ |
| Revit 기본 UI 글꼴 크기 | WPF 12px이 Windows 기본(9pt)과 맞는지는 확인 못 함 | [미확인] | — |

## 3. 상태 표시 가독성

| 원칙 | 내용 | 태그 | 출처 |
|---|---|---|---|
| 색만으로 구분 금지 | 상태 표시는 색·모양·기호 중 **둘 이상**을 씀. 회색조로 바꿔도 구분돼야 함. 상태 색끼리, 그리고 배경과 3:1 | [확정] | https://www.carbondesignsystem.com/building-blocks/core/patterns/status-indicators |
| 표시 개수 | 한 화면에 상태 표시가 5–6개를 넘으면 사용자가 집중하기 어려움 | [확정] | 위 Carbon |
| 색 의미 고정 | 빨강 = 위험·오류, 주황 = 심각한 경고, 노랑 = 경고, 초록 = 정상, 파랑 = 정보, 회색 = 시작 전. 같은 상태는 제품 전체에서 같은 색 | [확정] | 위 Carbon, https://camunda.github.io/design-system/?path=%2Fdocs%2Fpatterns-status-indicators--docs |
| 밝은 색은 이상 상태에만 | 정상 화면은 차분한 색으로 두고, 밝은 빨강·노랑은 경보에만 씀. 경보는 우선순위별로 색·모양·글자를 함께 쓰고, 값 옆에 표시. 확인 전에는 깜박이고 확인 뒤에는 멈춤 | [확정] | https://www.isa.org/getmedia/06130a38-f7af-4b35-8c9c-2c34f25c1977/The-High-Performance-HMI-Overview-v2-01.pdf |
| 경고 문구 | 심각한 경고는 자동으로 사라지면 안 됨. 막다른 길을 만들지 말고 다음에 할 일을 알려 줌 | [확정] | https://atlassian.design/components/flag/usage |
| 숫자 정렬·단위 | 표에서 글자는 왼쪽, 숫자는 오른쪽(소수점 기준), 머리행은 가운데. 단위는 표 오른쪽 위나 열 머리에 "(단위: …)"로 씀 — 관행이며 규정은 아님 | [부분 확인] | https://github.com/chrisryugj/kordoc/blob/main/docs/gongmunseo-reference.md (§7.3–7.4) |

## 4. 인쇄 서류 가독성 (A4)

| 항목 | 내용 | 태그 | 출처 |
|---|---|---|---|
| 용지·날짜·시각 | A4(210×297mm). 날짜는 "2026. 12. 31."처럼 연·월·일 대신 온점을 찍음. 시각은 24시각제 "15:20"으로 씀 (행정업무규정 제7조 ④–⑥) | [확정] | https://www.goe.go.kr/resource/old/BBSMSTR_000000000028/BBS_202410150153084250.pdf |
| 본문 글자 크기 | **규정으로 정한 값은 없음.** 관행: 보고서 명조 14–15pt, 줄 간격 160% / 전자결재 맑은 고딕 11.5pt. 표 안 글자는 맑은 고딕 12–13pt, 셀 좌우 여백 3pt, 머리 칸 음영 | [부분 확인] | http://jb.nhi.go.kr/comfile/fileDownload.do?atchmnflId=F20220804133412949&fileSn=1 , 위 kordoc §2 |
| 큰글자 서식 | 시행규칙 별표5: 맑은 고딕 13pt, 줄 간격 130% 이상 (일반 서식 별표4는 돋움 10pt) | [부분 확인] 2차 정리 | 위 kordoc §2.6 |
| 원문 | 2025 행정업무운영 편람(행정안전부). 이번 조사에서 PDF 본문은 열지 않음 | [미확인] 본문 수치 | https://www.mois.go.kr/frt/bbs/type001/commonSelectBoardArticle.do?bbsId=BBSMSTR_000000000012&nttId=122878 |
| 서명란 | 서명란 크기를 정한 공개 기준은 못 찾음. 관행상 서명란은 회색으로 둠(kordoc 요약) | [미확인] 크기 / [부분 확인] 색 | 위 kordoc |
| 표로 끝날 때 | 마지막 칸까지 채웠으면 표 아래에 "끝.", 중간에서 끝나면 "이하 빈칸" | [부분 확인] | 위 kordoc §7.1 |

## 5. 데이터 입력 편의

| 원칙 | 내용 | 태그 | 출처 |
|---|---|---|---|
| 기본값·이력 | 쓸 만한 기본값이 있는지, 이력·자주 쓰는 값이 있는지, GPS·카메라로 채울 수 있는지, 다른 값으로 계산할 수 있는지 먼저 확인. 미리 채운 폼은 빈 폼보다 4배 빨랐다는 연구가 있음 | [확정] / [부분 확인] 4배 | https://www.nngroup.com/articles/mobile-input-checklist/ , https://www.lukew.com/mobilefirst/08-chapter-6/index.html |
| 필드 | 한 열로 배치. 입력칸 안 예시 글자(placeholder) 대신 칸 위에 라벨. 선택지가 2–3개면 드롭다운 대신 버튼. 지우기 버튼은 두지 않음 | [확정] | https://www.nngroup.com/articles/web-form-design/ |
| 날짜 | 예시는 질문에 맞는 실제 날짜로. 오류는 비었음 → 있을 수 없는 값 → 그 밖의 검사 순으로 하나만 보여 줌. 문구 예: "~는 오늘이거나 과거여야 합니다", "~는 [날짜] 이후여야 합니다" | [확정] | https://design-system.service.gov.uk/components/date-input/ |
| 오류 메시지 | 무엇이 잘못됐고 어떻게 고치는지를 오류 난 칸 바로 옆에 씀. 색 하나로만 알리지 않고 테두리·아이콘·굵은 글자를 함께 씀. 사용자를 탓하지 않음('잘못된', '불법' 같은 말 피함). **입력한 값은 지우지 않음.** 고치는 방법을 고를 수 있게 제시 | [확정] | https://www.nngroup.com/articles/error-message-guidelines/ , https://design-system.service.gov.uk/components/error-message/ |
| 오류 요약 | 맨 위에 오류 요약을 보여 주고 포커스를 옮김. 각 칸 옆에도 메시지 | [확정] | https://design-system.service.gov.uk/patterns/validation/ |

---

## 6. 지금 화면에서 본 것 (코드 읽기)

- **휴대폰** `web\field\index.html`: 기본 18px, 입력칸 높이 56px 이상, 칩 52px, 저장 바 60px로 하단 고정, 측정 종류 카드 96px → 터치 크기는 이미 44–48 기준을 넘는다. 상태 타일은 색 + 글자(`STATUS` 표)를 함께 쓴다. 오프라인 큐·배지·`client_id`로 중복을 막는다. 입력자·양생 방법·마지막 층을 기억한다. 오류는 칸 옆에 표시하고 첫 오류 칸으로 포커스를 옮긴다.
  - 약점: 상태 6 "감리 승인 기록"이 흰 글자 on `rgb(26,152,80)` = **3.72:1** (타일 글자 14px에서 4.5:1 미달, WPF는 검정 5.65:1). 타일 상태·날짜 글자 14px, 센서 이름표 12px. 경고 색 `#8a5a00` on 흰색 = 5.93:1 (AA 통과, 7:1 미달). 큐 상태는 헤더 배지에만 보임.
- **대시보드** `DashboardPane.xaml`: 기본 글자 12px(`OnSiteStyles.xaml` `OnSite.FontSize`). 층×구역 칸 글자 = **예측 해체일만**(`Dashboard.cs` 466행) → 칸에서는 상태를 색으로만 구분한다. '오늘 볼 것'은 빨간 한 줄이고 목록은 **툴팁에만** 있다. 경고 목록은 한 줄로 잘리고 우선순위나 아이콘이 없다.
- **결과 창** `ResultWindow.xaml`: 굵은 요약 한 줄 + 본문 TextBlock 하나(줄글). 포커스를 가져가지 않음(ShowActivated=False).
- **인쇄** `sample_request_20261231.html`: 본문 10.5pt, 제목 16pt, 여백 16mm, 서명 표(구분·소속·성명·서명·일시)가 있다.
- **점검 19–22차에서 남은 것**: F1 휴대폰 입력 뒤 '오늘 볼 것'이 바로 안 바뀜, F3 결과 창이 현장 입력 창 뒤로 숨음, D4·F4 "승인 기록 기록"처럼 말이 겹침, E3 3D 공시체 색이 올리브·갈색(대시보드는 연노랑), 리본 [해체 판정] 날짜 기본값(오늘)이 대시보드 기준일(12-31)과 다름(22차), D5 리본 첫 클릭이 활성화에 쓰임.

## 7. 개선 후보 표

크기: S = 반나절 이하, M = 1–2일, L = 3일 이상 [가정]. 우선순위: 1 = 다음 빌드, 2 = 이번 단계 안, 3 = 여유 있을 때.

| # | 화면 | 지금 문제 | 참조 근거 | 제안 | 크기 | 우선 |
|---|---|---|---|---|---|---|
| 1 | 대시보드 층×구역 칸 | 칸 글자가 예측일뿐이라 상태를 색으로만 구분(색각 이상·흑백 캡처에서 구분 안 됨) | Carbon(둘 이상), ISA-101 | 칸에 상태 약자(예: "요청 12-25", "존치", "공시", "!결함")나 앞 기호를 붙이고, 머리 띠에 범례 한 줄 | S | 1 |
| 2 | 휴대폰 타일·웹 공유판 상태 6 | 흰 글자 on #1A9850 = 3.72:1 | WCAG 1.4.3 | 상태 6 글자를 검정(5.65:1)으로 바꿔 WPF `Approved.Fg`와 맞추고, 상태 색 표를 한 곳(StatusColors)에서 생성 | S | 1 |
| 3 | 대시보드 '오늘 볼 것' | 목록이 툴팁에만 있어 마우스를 올려야 보이고 누를 수 없음 | Atlassian(심각한 경고는 사라지지 않게, 다음 할 일), Revit 대화상자 지침(훑어보기) | 펼치는 목록(Expander)으로 바꾸고, 줄을 누르면 그 구역을 선택·확대(휴대폰 `#alerts`와 같은 방식) | M | 1 |
| 4 | 대시보드 ← 휴대폰 입력 | 받은 기록이 들어와도 '오늘 볼 것'이 다른 버튼을 누를 때까지 그대로(F1) | ISA-101(경보는 바로, 값 옆에) | 현장 입력 서버가 기록을 받으면 대시보드 요약·경고를 새로 고침(ExternalEvent 한 번) | M | 1 |
| 5 | 리본 [해체 판정] | 날짜 기본값이 오늘이라 대시보드 기준일(12-31)과 다름(22차) | NN/g 기본값 | 기본값을 대시보드 기준일로, 창에 "대시보드 기준일과 같음" 표시 | S | 1 |
| 6 | 대시보드 경고 목록 | 한 줄로 잘리고 순서·심각도 구분이 없음 | ISA-101 우선순위 이중 표시, Carbon 색 의미 | 앞에 심각도 기호(■ 위험 / ▲ 경고 / ● 정보)를 붙여 무단 해체 의심 → 타설 보류 → 존치 → 한중·입력 점검 순으로 정렬, 2줄까지 줄바꿈 | M | 1 |
| 7 | 휴대폰 저장 결과·헤더 | '보낼 것 N건'이 헤더 배지에만 있어 기기에만 저장된 기록인지 놓치기 쉬움 | Fieldwire(동기화돼야 번호가 붙음), SmartQHSE "Saved locally" | 최근 입력 줄마다 "기기에만 저장 · 11-05 08:40" 표시, 보낸 뒤 "서버 저장 ✓"로 바꿈. 배지에 마지막 전송 시각 | S | 1 |
| 8 | 결과 창 | 본문 줄글 하나라 핵심(몇 건, 경고 유무)을 찾기 어려움 | TaskDialog 구조(요약·상세·할 일), Revit 대화상자 지침 | 맨 위 굵은 한 줄 + 숫자 줄 + 경고는 ▲ 기호로 따로 + 상세는 접기 + 바로 할 일 버튼(예: [근거 원장 열기]) | M | 1 |
| 9 | 결과 창 ↔ 현장 입력 창 | 결과 창이 현장 입력 창 뒤로 숨음(F3) | Revit 대화상자 지침(바로 보이고 포커스) | 결과 창 Owner를 Revit 본창으로 고정하고, 새 결과가 오면 현장 입력 창 위로 올림(포커스는 그대로) | S | 2 |
| 10 | 문구 전체 | "승인 기록 기록 1건"처럼 말이 겹침(D4·F4) | NN/g(간결·정확) | 결과 문구 템플릿을 "{행위} 1건 기록" 하나로 통일하고, 문구 목록을 grep으로 점검 | S | 2 |
| 11 | WPF 전체 글자 | 기본 12px이라 대시보드 칸·칩이 작음 | Revit 기본 크기는 [미확인], WCAG 큰 글자 정의 | `OnSite.FontSize`를 13으로 하고 칸·칩은 13–14. 옵션에 '글자 크게'(14/16) 하나 | S | 2 |
| 12 | 휴대폰 구역 타일 | 상태·날짜 14px, 센서 이름표 12px — 야외에서 읽기 어려움 | 업계 야외 권고 16–18px [부분 확인] | 타일 `.st`/`.due` 16px, `.dot b` 14px. 좁은 타일은 구역명 22px → 18px로 줄여 자리 확보 | S | 2 |
| 13 | 휴대폰 경고·보조 글자 | 경고 색 #8a5a00 5.93:1, 상태 타일 위 글자는 AA 근처 | WCAG 1.4.6(7:1)은 선택 목표 | 햇빛 모드(고대비) 토글을 추가해 글자는 #000/#fff, 경고는 #6b4500(7:1 이상) — 기본값은 그대로 | M | 3 |
| 14 | 3D 검측 뷰 색 | 공시체 시험 필요가 3D에서는 올리브·갈색, 칩은 연노랑(E3) | Carbon(같은 상태는 같은 색) | 뷰 필터를 '솔리드 채우기 + 조명 영향 없음'으로 확인하고, 안 되면 3D용 색을 따로 정해 칩에도 같은 색 사용 [부분 확인 — 원인 미검증] | M | 2 |
| 15 | 휴대폰 날짜·시각 | 기본값이 오늘인 것은 좋지만, 어제 시험한 값을 넣으려면 달력을 열어야 함 | NN/g 기본값, GOV.UK 날짜 | 날짜 칸 아래에 [오늘] [어제] 빠른 버튼, 시각은 [지금] 버튼 | S | 2 |
| 16 | 휴대폰 공시체 채취 정보 | 레미콘 회사·보관 위치를 매번 새로 입력 | NN/g 이력 재사용 | 마지막 값을 기억해 미리 채우고(입력자처럼 LS), 칸 옆에 "지난번 값" 표시 | S | 3 |
| 17 | 휴대폰 오류 문구 | "채취 시각 형식이 틀렸습니다"처럼 고치는 방법이 없고 탓하는 말투 | GOV.UK·NN/g(무엇·어떻게, 탓하지 않기) | "채취 시각은 타설일 이후 날짜와 시각으로 고르세요 (예: 2026-12-30 09:00)"처럼 바꾸고, 문구 목록을 한 번에 다듬기 | S | 2 |
| 18 | 휴대폰 폼 오류 여러 개 | 첫 칸으로 포커스만 옮기고 몇 곳인지는 안 알려 줌 | GOV.UK 오류 요약 | 폼 맨 위에 "고칠 곳 2곳: 2번 강도, 입력자" 요약을 넣고, 누르면 그 칸으로 이동 | S | 3 |
| 19 | 휴대폰 실패 기록 | 실패 기록은 지우기만 할 수 있어 입력한 값을 다시 써야 함 | NN/g(입력 보존), GOV.UK(값 유지) | 실패 기록에 [고쳐서 다시 보내기]를 넣어 그 값으로 폼을 다시 엶 | M | 2 |
| 20 | 휴대폰 층 고르기 | 15개 층이 가로 스크롤 칩이라 한 손으로 끝 층에 닿기 어려움 | Hoober(가운데가 쉬움, 모서리는 보조) | 칩 앞에 '판정 대상 층·최근 층' 2개를 고정하고, 나머지는 드롭다운(목록)으로 | S | 3 |
| 21 | 인쇄 검측요청서·층 표지 | 본문 10.5pt — 감리가 현장·회의에서 읽기에는 작음 | 공문 관행: 표 안 12–13pt, 큰글자 서식 13pt [부분 확인] | 표 11.5–12pt, 결론(요청 가능·존치)은 14pt 굵게. 층 표지는 13pt 이상. 서명 칸 높이 15mm 이상 [가정] | S | 2 |
| 22 | 인쇄 날짜·숫자 | 날짜가 "2026-12-31"·"12-31"이고 숫자 정렬은 [미확인] | 행정업무규정 제7조(날짜 온점), 표 관행(숫자 오른쪽) | 인쇄물 날짜는 "2026. 12. 31.", 시각은 "09:00". 강도·본수 열은 오른쪽 정렬 + `font-variant-numeric: tabular-nums`, 단위는 열 머리 "(MPa)" | S | 2 |
| 23 | 리본 | 패널 구성이 지침(맨 왼쪽 큰 버튼, 3열 이하, 설정·도움말은 슬라이드아웃, F1)에 맞는지 이번에 확인 안 함 | Autodesk Ribbon Guidelines | 리본 코드를 지침 체크리스트로 점검하고 F1 도움말을 `docs\help` 해당 앵커에 연결 | M | 3 |

## 8. 이번에 확인 못 한 것

- Autodesk Build·PlanGrid의 공개 디자인 근거, 국내 앱 화면 수치 — 못 찾음.
- 장갑을 낀 상태의 터치 크기 실험값 — 공식 기준 없음. 실제 장갑으로 시험해야 함(1절 7분 시험).
- 행정업무운영 편람 PDF 본문 — 열지 않음. 글자 크기 수치는 2차 정리와 기관 지침에 기댄 것.
- 리본·결과 창 코드 숨김 파일(`*.cs`)은 `Dashboard.cs` 칸 내용만 확인했고, 실제 Revit 화면은 보지 않았다.

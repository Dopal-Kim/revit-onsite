<!-- ppt-master-schema: design-spec/v1 -->
# Revit On-Site 최종 발표 - Design Spec

## I. Project Information

| Item | Value |
| --- | --- |
| Project Name | revit_onsite_final |
| Canvas Format | ppt169 (1280×720) |
| Page Count | 26 |
| Primary Language | ko-KR |
| Target Audience | 건축공학시스템설계 담당 교수님(백장운 교수님)과 수강생 — 콘크리트·거푸집 공사와 BIM(Revit) 기본 지식이 있고, 실제 현장 적용성과 근거의 정확성을 봄 |
| Communication Intent | 먼저 동바리 해체 판단이 왜 위험한 문제인지(사고 사례·기준 분산·경험 의존)를 보이고, 다음으로 Revit On-Site가 기준과 실측값으로 구역마다 판정하고 근거를 남기는 방식을 설명·시연으로 입증한 뒤, 기대효과를 확실성 순서로 제시하고 한계와 보완 계획을 정직하게 밝힌다 |
| Desired Audience Outcome | 청중이 판정 흐름(강도·존치 층수·지지층 강도·하중비 → '해체 승인 요청 가능' 또는 '존치' → 감리 최종 승인)을 설명할 수 있고, 어떤 효과가 확실하고 어떤 효과가 조건부인지 구분해 이 도구의 현장 적용 가치를 평가할 수 있다 |
| Core Message / Ask / Action | 날짜가 아닌 기준으로 판단하고, 그 근거를 기록한다 — 판정은 프로그램이, 결정은 감리가 한다 |
| Delivery Context | 주: 발표자가 진행하는 수업 프로젝트 발표(중간에 시연 영상 3분 52초 재생) 후 질의응답 / 부: 발표 뒤 평가자가 파일을 다시 읽어 보는 용도 |
| Artifact Afterlife | 과목 제출·평가 자료로 보관(참고문헌·출처 포함) |
| Reading Mode | balanced |
| Content Strategy | 기존 내용을 보완하고 수준 높게 — 원본 26장의 사실·수치·출처를 모두 유지하고, 목차 2장을 1장으로 합치며, 프로젝트 문서(sources/revit_onsite_system_facts.md)에 근거한 '시스템 구성·판정 흐름' 1장을 추가 |
| Design Style | 도면 시트 (custom) — 장마다 도면 표제란, 슬래브·동바리 단면 모티프 |
| AI Image Acquisition Path | not applicable |
| Generation Mode | continuous |
| Spec Refinement | disabled |
| Speaker Notes | enabled — final Stage-2 proactive policy |
| Custom Animations | disabled — final Stage-2 proactive policy |
| Narration Audio | disabled — workflow default |
| Created Date | 2026-11-19 |

## II. Canvas Specification

| Property | Value |
| --- | --- |
| Format | PPT 16:9 |
| Dimensions | 1280 × 720 |
| viewBox | `0 0 1280 720` |
| Margins | 56 px left/right, 40 px top, 56 px bottom (표제란 띠 포함) |
| Content Area | x 56–1224, y 40–664 |

## III. Visual Theme

### Theme Style

- **Mode**: custom
- **Mode References**: narrative, instructional
- **Mode Behavior**: 문제(사고·기준 분산) → 긴장(경험·고정 일수의 한계) → 해결(구역별 기준 판정·근거 기록)의 narrative 흐름으로 I–III장을 끌고, 판정 흐름·시스템 구성·시연은 instructional처럼 단계 순서대로 풀며, 기대효과는 확실성 순서(확실 → 조건부 → 가능성)로 정직하게 나열한 뒤 한계·결론으로 닫는다. 제목은 판단 문장, 부제는 한 줄 요지.
- **Visual style**: custom
- **Visual Style References**: blueprint, swiss-minimal
- **Visual Style Behavior**: 밝은 도면 용지 바탕에 얇은 이중 테두리와 오른쪽 아래 도면 표제란(프로젝트명·시트 번호·장 이름)을 매 장 두는 도면 시트 언어. 정보는 격자에 맞춰 정렬하고 모서리는 거의 직각(4 px), 장식 대신 치수선·지시선·단면 해치로 관계를 보인다. 슬래브=굵은 남색 막대, 동바리=세로 선, 새로 타설한 층=안전 주황, 철거된 동바리=점선이라는 단면 기호를 장 전체에서 같은 뜻으로 반복한다. 숫자·라벨·시트 번호는 고정폭 글꼴, 제목은 굵은 산세리프. 그림자 없음, 면 채움은 옅은 회청색 한 단계만.
- **Theme**: 구조 도면 시트 — 표지·목차·결론·Q&A는 진한 남색 바탕의 표지 시트, 본문은 밝은 종이색 시트
- **Tone**: 정확하고 차분한 공학 보고, 과장 없이 근거 중심

### Color Scheme

| Role | HEX | Purpose |
| --- | --- | --- |
| Background | #F6F7F5 | 본문 시트 바탕(도면 용지) |
| Secondary background | #E8EDF2 | 카드·표 머리 옅은 면, 표제란 바탕 |
| Primary | #1D3557 | 제목, 슬래브 단면, 표지 시트 바탕, 표 머리 |
| Accent | #E07A1F | 새로 타설한 층, 위험·주의, 핵심 수치 강조(한 장에 한 곳) |
| Secondary accent | #2E6DB4 | '해체 승인 요청 가능', 동바리 선, 링크·진행 표시 |
| Body text | #1E2329 | 본문 |
| Secondary text | #5B6673 | 캡션, 출처 줄, 표제란 글자 |
| Divider | #C5CDD6 | 도면 테두리 안쪽 선, 표 행 구분, 치수선 |
| Negative | #B23A2E | 사고·붕괴·'불가' 판정 |

## IV. Typography System

### Font Plan

| Role | Character (Reference) | Primary | English if non-English | Fallback tail |
| --- | --- | --- | --- | --- |
| Title | 굵은 산세리프, 판단 문장 | Malgun Gothic | Segoe UI | sans-serif |
| Body | 중립 산세리프 | Malgun Gothic | Segoe UI | sans-serif |
| Data | 고정폭, 시트 번호·수치·라벨 | Consolas | Consolas | monospace |

- **Title stack**: Malgun Gothic, Segoe UI, sans-serif
- **Body stack**: Malgun Gothic, Segoe UI, sans-serif
- **Data stack**: Consolas, Malgun Gothic, monospace
- **Role rationale**: Data — 도면 표제란 시트 번호, 치수·하중·β 수치, 연도 라벨이 매 장 반복되어 고정폭으로 도면 표기 느낌을 준다.

### Font Size Hierarchy

| Purpose | Anchor Size (px) |
| --- | ---: |
| Body | 20 |
| Title | 34 |
| Subtitle | 22 |
| Lead | 24 |
| Annotation | 15 |
| Footnote | 12 |
| Display | 60 |
| Cover title | 72 |
| Data | 15 |

## V. Layout Principles

### Deck-wide Direction

- **Hierarchy direction**: 왼쪽 위 장 번호·제목 → 한 줄 요지(부제) → 본문 근거(단면 그림·표·카드) → 아래 출처 줄 → 오른쪽 아래 표제란
- **Composition tendency**: 근거가 그림이면 그림이 화면 절반 이상, 비교는 좌우 대칭, 순서는 왼→오 흐름. 카드 격자는 실제 병렬 항목일 때만.
- **Cross-page continuity**: 이중 테두리·표제란·단면 기호(슬래브/동바리/주황 타설층/점선 철거)를 매 장 같은 뜻으로 유지. 표지의 3개 층 단면이 I장 개요, V장 안전, 결론에서 다시 나온다.
- **Spacing posture**: 본문은 dense를 기본으로 하되 시연·Q&A는 여백을 크게
- **Spacing anchors**: page margin 56 px · block gap 24 px · column gutter 32 px · corner radius 4 px · body leading 1.45

## VI. Icon Usage Specification

- **Primary bundled library**: tabler-outline
- **Stroke Width**: 2

| Icon Path | Suitable Scenarios |
| --- | --- |
| icons/tabler-outline/alert-triangle.svg | |
| icons/tabler-outline/clipboard-check.svg | |
| icons/tabler-outline/ruler-measure.svg | |
| icons/tabler-outline/building.svg | |
| icons/tabler-outline/stack-2.svg | |
| icons/tabler-outline/device-mobile.svg | |
| icons/tabler-outline/qrcode.svg | |
| icons/tabler-outline/cloud.svg | |
| icons/tabler-outline/database.svg | |
| icons/tabler-outline/file-certificate.svg | |
| icons/tabler-outline/shield-check.svg | |
| icons/tabler-outline/calendar.svg | |
| icons/tabler-outline/users.svg | |
| icons/tabler-outline/eye.svg | |
| icons/tabler-outline/lock.svg | |
| icons/tabler-outline/bolt.svg | |
| icons/tabler-outline/search.svg | |
| icons/tabler-outline/settings.svg | |
| icons/tabler-outline/tool.svg | |
| icons/tabler-outline/home.svg | |
| icons/tabler-outline/world.svg | |
| icons/tabler-outline/file-text.svg | |
| icons/tabler-outline/arrow-right.svg | |
| icons/tabler-outline/check.svg | |
| icons/tabler-outline/x.svg | |
| icons/tabler-outline/rosette-discount-check.svg | |
| icons/tabler-outline/refresh.svg | |
| icons/tabler-outline/cube.svg | |
| icons/tabler-outline/chart-bar.svg | |
| icons/tabler-outline/temperature.svg | |
| icons/tabler-outline/player-play.svg | |
| icons/tabler-outline/scale.svg | |
| icons/tabler-outline/coin.svg | |
| icons/tabler-outline/book.svg | |

## VII. Visualization Reference List

| Page | Family | Template | Usage |
| --- | --- | --- | --- |
| P06 | table | record_table | 해체 판단 기준 항목·기준·근거 조항 |
| P11 | chart | column_chart | 타설층 하중 12.33 vs 아래 3개 층 여유 15.50 kN/m² |
| P13 | table | comparison_matrix | 공정 3가지 × 존치 2·3·4개 층의 최대 β와 가능/불가 |
| P20 | table | metric_table | 4개 층 vs 3개 층 받침의 동바리·띠장·공기·총공사비 |

## VIII. Image Resource List

| Filename | Dimensions | Ratio | Purpose | Type | Image pattern | Crop Policy | Acquire Via | Status | Reference | text_policy | page_role |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| khu_crest.png | 335×255 | 1.31 | 경희대학교 문장 | Logo | 표지·Q&A 시트 상단 중앙 또는 표제란 옆 작은 표식 | no-crop | user | Existing | 원본 1장에서 잘라 배경을 투명 처리 | none | local |
| dept_logo.png | 142×160 | 0.89 | 건축공학과 로고 | Logo | 표제란 오른쪽 끝 작은 학과 표식 | no-crop | user | Existing | 원본 1장 오른쪽 아래에서 잘라 배경 투명 처리 | none | local |
| demo_revit_screen.jpg | 1205×526 | 2.29 | 시연 영상 첫 화면(Revit On-Site 탭 7개 패널, 3D 구역 색) | Screenshot | 시연 장의 주 화면, 도면 뷰포트 틀 안에 전체 표시 | no-crop | user | Existing | 원본 15장 캡처 영역 | none | local |
| inspection_request.jpg | 520×562 | 0.93 | 프로그램이 만든 검측요청서 화면 | Screenshot | 기록·책임 장 오른쪽 증거 문서, 종이 한 장처럼 | no-crop | user | Existing | 원본 18장 오른쪽 문서 영역 | none | local |

## IX. Content Outline

### Part 1: 표지·목차

#### Slide 01 - 표지

- **Audience move**: 주제를 모름 → 현장의 질문 하나와 이 도구의 정체를 안다
- **Relationships**: 질문(이 구역 동바리, 오늘 해체 승인을 요청해도 될까요?) → 도구 이름·부제; 과목·담당 교수·학과·팀명·팀원은 membership
- **Composition**: 진한 남색 표지 시트, 3개 층 슬래브·동바리 단면 선화와 맨 위 주황 타설층, 질문 문장이 훅, 큰 제목, 오른쪽 아래 표제란에 과목 정보
- **Title**: Revit On-Site
- **Core message**: 동바리 설치·해체 판정을 위한 Revit 애드인
- **Content**: 훅 "이 구역 동바리, 오늘 해체 승인을 요청해도 될까요?" · 부제 동바리 설치·해체 판정을 위한 Revit 애드인 · 과목 건축공학시스템설계 / 담당 교수 백장운 교수님 / 학과 건축공학과 / 팀명 Build Up / 팀원 김도영, 이영준, 최예준, 최영윤 · 학교 문장·학과 로고
- **Cover impact**: 현장의 질문 한 문장 + 3개 층 단면
- **Images**: khu_crest.png, dept_logo.png

#### Slide 02 - 목차

- **Audience move**: 순서를 모름 → 8개 장의 흐름(문제 → 근거 → 도구 → 효과 → 한계)을 한눈에 본다
- **Relationships**: I–VIII 장 order, 각 장의 하위 항목 parent
- **Composition**: 표지 시트 계열, 도면 목록표(Drawing List)처럼 시트 번호·장 이름 2열
- **Title**: Contents
- **Core message**: 문제에서 시작해 근거·도구·효과·한계 순으로 간다
- **Content**: I. Revit On-Site 소개 — 개요 / 시스템 구성·판정 흐름 · II. 문제인식 — 동바리 공사 과정과 해체 기준 / 현행 해체 판단 방식의 문제점 / 사고 사례 분석 · III. 기술 현황 및 적용 필요성 — 선행 연구 / 존치 층수 결정 방법 / 판정이 존치 계획을 바꾸는 이유 / 적용 가능성 · IV. 시연 — 시연 영상 · V. 기술 적용의 기대효과 — 안전 / 기록·책임 / 자재·비용 / 누가 무엇을 받나 · VI. 기술의 보완방안 — 현재 한계와 보완 계획 · VII. 결론 및 참고문헌 · VIII. Q&A

### Part 2: I. Revit On-Site 소개

#### Slide 03 - 개요

- **Audience move**: 도구가 무엇에 답하는지 모름 → 질문 → 4개 기준 판정 → 두 결론 → 감리 승인 흐름을 안다
- **Relationships**: 현장의 질문 → 판정(강도 ≥ 설계기준압축강도, 타설층 포함 3개 층 존치, 지지층 14 MPa 이상, 하중비교 적합: membership) → 결론('승인 요청 가능' 또는 '존치' + 근거 조항: contrast) → 감리 최종 승인(order); 시점 설치 → 타설 → 양생 → 해체(order), 설치 검측·해체 판정 두 시점
- **Composition**: 왼→오 3단 흐름, 아래에 공정 시간선
- **Title**: I. Revit On-Site 개요
- **Core message**: 현장의 질문에 기준·근거로 답한다
- **Content**: 질문 말풍선 · 판정 4항목 체크 · 결론 두 갈래 + 근거 조항 · 감리가 최종 승인 · 시간선 설치·타설·양생·해체, 설치 검측/해체 판정 표시

#### Slide 04 - 시스템 구성·판정 흐름

- **Audience move**: 도구 내부를 모름 → 세 역할이 어디서 무엇을 넣고, 판정·근거가 어떻게 남는지 안다
- **Relationships**: 리본 7개 패널 순서 준비 → 설치 검측 → 타설·데이터 → 판정 → 승인 → 보고·공유 → 타임라인(order); 역할 3갈래(공무·BIM 담당 = Revit, 품질·안전관리자 = 휴대폰 현장 입력, 협력업체 반장 = 휴대폰 해체 신고: membership); 휴대폰 입력 → 현장 서버 → Revit 5분마다 가져와 판정 → 결과를 서버에 올림(link); 결과물은 데이터 폴더(검측요청서·승인 기록·근거 원장)
- **Composition**: 가운데 Revit, 왼쪽 휴대폰 두 역할, 위 현장 서버(인터넷), 아래 결과물 묶음; 7개 패널 띠를 위에
- **Title**: I. 시스템 구성과 판정 흐름
- **Core message**: Revit 한 곳에서 판정하고, 현장은 휴대폰으로 실측값을 넣는다
- **Content**: 패널 7개 순서 = 현장 업무 순서 · 공무·BIM 담당: 하루 1회 아침 판정, 검측요청서·검측 패키지, 승인 기록 · 품질·안전관리자: QR → 공시체·센서·반발경도·동바리 확인·점검, 사진 1장 · 협력업체 반장: 층 QR → [이 구역 동바리를 뺐음]/[다시 세움] 신고 · 현장 서버 revit-onsite-field.vercel.app, 열쇠는 QR, 5분마다 동기화 · 결론 문구 '해체 승인 요청 가능'/'존치', 서명란은 비워 둠
- **Fact IDs**: sources/revit_onsite_system_facts.md

### Part 3: II. 문제인식

#### Slide 05 - 동바리 공사 과정

- **Audience move**: 공정 순서가 흐릿함 → 층마다 반복되는 6단계와 해체 승인 절차를 안다
- **Relationships**: 계획·설계(착공 전 1회) → 조립·설치 → 타설 전 검측 → 타설·양생 → 해체(강도 확인 후) → 재받침·전용 → 상부층으로 층마다 반복(order, cycle); 해체 = 강도 확인 → 책임기술인 검토 → 공사감독자 승인(order)
- **Composition**: 왼쪽 번호 목록, 오른쪽 순환도(해체 칸 강조)
- **Title**: II. 동바리 공사 과정
- **Core message**: 계획부터 해체·전용까지 단계별로 기준이 관리된다
- **Content**: 1 계획·설계 하중 산정, 구조 검토, 조립도 작성 · 2 조립·설치 간격·수직도 확보, 높이별 수평연결재 설치 · 3 타설 전 검측 설치 상태 점검, 감리 확인 · 4 타설·양생 타설 중 변형 감시, 현장양생 공시체 제작 · 5 해체 강도 확인 → 책임기술인 검토 → 공사감독자 승인 · 6 재받침·전용 필요 시 재받침, 상부층으로 자재 전용 · 그림: 층마다 반복되는 동바리 공사 순서

#### Slide 06 - 해체 판단 기준

- **Audience move**: 기준이 어디 있는지 모름 → 6개 항목의 기준값과 근거 조항을 한 표로 본다
- **Relationships**: 항목 × 기준 × 근거 (row records)
- **Composition**: 도면 일람표 같은 3열 표, 아래 주석 한 줄
- **Title**: II. 동바리 해체 판단 기준
- **Core message**: 해체 기준은 이미 정해져 있지만 여러 문서에 흩어져 있다
- **Content**: 다층 슬래브 동바리 해체 | 설계기준압축강도 이상 | KCS 14 20 12 3.3.1 · 필러 동바리 사용 | 구조계산 시 단축 가능(최소 14 MPa) | KCS 14 20 12 3.3.1 · 해체 후 하중 > 설계하중 | 구조계산 확인 → 승인 | KCS 14 20 12 3.3.1(6) · 존치 층수 | 타설층 포함 최소 3개 층 | 가이드라인 2.1.2(1)④ · 지지층 강도 | 최소 14 MPa | 가이드라인 2.1.2(2)⑧ · 설계 연직하중 | 최소 5.0 kN/m² | KDS 21 50 00 1.6.2(5) · ※ KCS 14 20 12 3.3.2(2): 타설층 포함 최소 3개 층 존치
- **Visualization**: criteria-table = table/record_table
- **Native-ready**: criteria-table=no

#### Slide 07 - 현행 해체 판단 방식의 문제점

- **Audience move**: 경험 판단이면 충분하다고 봄 → 이르거나 늦으면 모두 문제이고 세 가지 구조적 원인이 있음을 안다
- **Relationships**: 너무 일찍 빼면 붕괴 위험 ↔ 너무 오래 두면 후속 공정 지연(contrast, 경험에 기댄 판단이 받침점); 14일 → 9.6일 / 기준의 분산 / 강도 부족(membership)
- **Composition**: 위 저울(양쪽 위험), 아래 3개 근거 카드
- **Title**: II. 현행 해체 판단 방식의 문제점
- **Core message**: 해체 시점은 이르거나 늦으면 모두 문제다
- **Content**: 너무 일찍 빼면 붕괴 위험 · 너무 오래 두면 후속 공정 지연 · 받침점 경험에 기댄 판단 · 14일 → 9.6일: 날짜 기준 = 보수적, 강도 기준 보 해체 4.4일 단축 · 기준의 분산: 강도 KCS · 존치 층수 가이드라인 · 하중 KDS · 높이 법령 · 강도 부족: 아파트 사고 주요 원인 중 하나 → 현장양생 공시체 기준 신설

#### Slide 08 - 사고 사례: 해체 단계 (광주 화정)

- **Audience move**: 사고를 뉴스로만 앎 → 조기 철거·하중 증가·강도 미달이 겹친 구조를 단면으로 본다
- **Relationships**: 2022.1.11 39층 타설 중 연쇄 붕괴, 6명 사망·1명 부상; 38층 PIT층 아래 동바리 조기 철거 → 1차 붕괴(link); 동바리 → 콘크리트 가벽 무단 변경·하중 증가 2.24배(link); 시험체 설계기준강도 85% 미달 15/17개 층(membership)
- **Composition**: 왼쪽 수치 3개, 오른쪽 PIT층·38·37·36층 단면(점선 = 조기 철거 동바리)
- **Title**: II. 사고 사례: 해체 단계 (광주 화정)
- **Core message**: 동바리 조기 철거가 가장 큰 원인이었다
- **Content**: 2022.1.11 · 39층 타설 중 연쇄 붕괴 · 6명 사망, 1명 부상 · 38층 PIT층 아래 동바리 조기 철거 → 1차 붕괴 · 2.24배 동바리 → 콘크리트 가벽 무단 변경, 하중 증가, 사고조사위 주요 원인 · 15/17층 시험체 설계기준강도 85% 미달 층 · 그림: 건설사고조사위원회 발표를 바탕으로 한 붕괴 개념도
- **Fact IDs**: 원본 8장, 참고문헌 [11][12]

#### Slide 09 - 사고 사례: 설치 단계

- **Audience move**: 설치 단계 위험을 과소평가 → 4건 모두 타설 중 발생했고 원인이 설치 결함임을 안다
- **Relationships**: 2015.2 → 2015.3 → 2022.10 → 2023.8(order); 각 사고 = 장소 · 인명 피해 · 원인(record)
- **Composition**: 시간선 위 4개 사건 칸
- **Title**: II. 사고 사례: 설치 단계
- **Core message**: 4건 모두 콘크리트 타설 중 발생했다
- **Content**: 2015.2 서울 사당종합체육관 · 11명 매몰·부상 · 동바리 지지 가설재 없이 타설 강행 · 2015.3 용인 냉수물천교 · 1명 사망·8명 부상 · 설계보다 넓은 120 cm 간격 수평재, 옹벽·상판 동시 타설 · 2022.10 안성 저온물류창고 · 3명 사망·2명 중상 · 조립도 미작성, 층고 12.8 m에 10 m·3 m 잭서포트 볼트 연결 · 2023.8 안성 근린생활시설 · 2명 사망·4명 부상 · 거푸집·동바리 붕괴(추정)
- **Fact IDs**: 원본 9장, 참고문헌 [13]–[19]

### Part 4: III. 기술 현황 및 적용 필요성

#### Slide 10 - 기술 현황: 선행 연구

- **Audience move**: 관련 연구를 모름 → 다섯 연구의 성과와 이 과제의 차별점(측정값 → 국내 기준 구역별 해체 판정)을 안다
- **Relationships**: 2014 → 2016 → 2020 → 2024 → 2024(order); 선행 연구 = 강도·하중 측정 중심 → 본 과제 = 측정값을 국내 기준 구역별 판정으로(contrast)
- **Composition**: 시간선 5칸 + 아래 차별점 한 줄
- **Title**: III. 기술 현황: 선행 연구
- **Core message**: 선행 연구는 측정에 머물렀고, 본 과제는 국내 기준 판정까지 잇는다
- **Content**: 2014 천성철 외 · 재받침 + 10층 현장 계측 · 동바리 −40%, 거푸집 −50% · 2016 Alvarado 외 · 존치 2·3·4개 층 공정 비교 · 3개 층이 최저 비용 · 2020 Hamooni 외 · 온도센서·성숙도법 + Revit · 보 해체 9.6일 · 2024 Iqbal 외 · IoT 센서 + BIM 디지털 트윈 · 원격 실시간 강도 확인 · 2024 서형석 외 · 하중 감지 지능형 동바리 · 해체 시기 판단 보조 · 선행 연구: 강도·하중 측정 중심 → 본 과제: 측정값 → 국내 기준 구역별 해체 판정
- **Fact IDs**: 참고문헌 [5]–[9]

#### Slide 11 - 존치 층수 결정 방법

- **Audience move**: 몇 개 층을 남길지 근거를 모름 → 타설층 하중과 아래층 여유 하중 비교로 정한다는 것을 안다
- **Relationships**: 타설층 하중 12.33 < 아래 3개 층 여유 하중 15.50 kN/m²(contrast) → 3개 층 유지로 충분(link); 동바리 제거·재설치 구역 → 상세 구조 계산 필요(exception)
- **Composition**: 왼쪽 두 수치 비교와 결론, 오른쪽 막대 그래프
- **Title**: III. 동바리를 몇 개 층에 남길지 정하는 방법
- **Core message**: 타설층 하중 vs 아래층 여유 하중으로 존치 층수를 정한다
- **Content**: 타설층 하중 12.33 kN/m² < 아래 3개 층 여유 하중 15.50 kN/m² · 3개 층 유지로 충분 · 가이드라인 계산 예시, 슬래브 210 mm · 동바리 제거·재설치 구역 → 상세 구조 계산 필요 · 어느 층 동바리를 언제 뺄지 판단 근거 · 그림: 국토부 해체 가이드라인의 계산 예시를 그래프로 정리
- **Visualization**: load-compare = chart/column_chart (새로 타설한 층 12.33, 아래 3개 층의 여유 15.50, 단위 kN/m², 축 0–18)
- **Native-ready**: load-compare=no
- **Fact IDs**: 참고문헌 [2]

#### Slide 12 - 판정이 존치 계획을 바꾸는 이유 (1): 연구 설계

- **Audience move**: 해외 연구의 비교 틀을 모름 → 3가지 해체 방법 × 2·3·4개 층 = 9가지 계획과 판단 기준 β를 안다
- **Relationships**: 연구 대상(마드리드 14층 아파트, 슬래브 22 cm, 일주일에 1개 층) → 3가지 방법(한 번에 해체 / 클리어링 / 재받침: membership) × 존치 2·3·4개 층 = 9가지 계획; β = 공사 중 슬래브가 받는 하중 ÷ 설계에서 정한 하중, β ≤ 1 해체 가능 ↔ β > 1 쓸 수 없는 계획(contrast)
- **Composition**: 왼쪽 연구 대상·9가지 계획, 오른쪽 3가지 방법 목록, 아래 β 정의 띠
- **Title**: III. 판정이 존치 계획을 바꾸는 이유 (1)
- **Core message**: 같은 건물도 해체 방법과 존치 층수에 따라 9가지 계획이 나온다
- **Content**: 스페인 마드리드 아파트 공사에서 동바리를 빼는 방법 세 가지를 비교한 연구 (Alvarado 외, 2016) · 연구 대상 마드리드 14층 아파트, 슬래브 두께 22 cm, 일주일에 1개 층씩 시공 · 1 한 번에 해체: 콘크리트가 충분히 굳은 뒤 한꺼번에 뺀다 · 2 클리어링: 타설 2일 뒤 동바리 절반 이상을 먼저 빼고 나머지는 남겨 둔다(스페인식) · 3 재받침: 모두 뺀 뒤 며칠 안에 다시 받친다 · 3가지 방법 × 2·3·4개 층 = 9가지 계획 · β = 공사 중 슬래브가 받는 하중 ÷ 설계에서 정한 하중 · β ≤ 1 → 해체 가능 / β > 1 → 쓸 수 없는 계획
- **Fact IDs**: 참고문헌 [6]

#### Slide 13 - 판정이 존치 계획을 바꾸는 이유 (2): 결과

- **Audience move**: 결과를 모름 → 하중 검토를 통과한 계획은 클리어링 3·4개 층뿐이고 3개 층이 최저 비용임을 안다
- **Relationships**: 공정(설치·해체 / 클리어링 / 재받침) × 존치 2·3·4개 층의 최대 β(grid); 클리어링 3·4개 층만 0.98 가능, 나머지 불가(contrast); 3개 층 = 하중 검토 통과 + 공기 동일 + 자재 최소 → 최저 비용(link)
- **Composition**: 오른쪽 표 주인공, 왼쪽 판정 결과 요약
- **Title**: III. 판정이 존치 계획을 바꾸는 이유 (2)
- **Core message**: 하중 검토를 통과한 계획만 고를 수 있고, 그중 3개 층이 가장 싸다
- **Content**: β ≤ 1: 하중 ≤ 강도 → 해체 가능 · 한 번에 해체(설치·해체) 1.30 / 1.29 / 1.28 모두 불가 · 클리어링 1.03 불가 / 0.98 가능 / 0.98 가능 · 재받침 1.08 / 1.06 / 1.06 모두 불가 · 판정 결과: 설치·해체 2·3·4개 층 모두 β > 1 → 불가 · 재받침 모두 β > 1 → 불가(15층 타설 시 14층) · 클리어링 3·4개 층 β 0.98 → 가능, 공기 101일 동일 · 임계 시점: 위층 타설 순간, 바로 아래층 · 표 1. 공정별 최대 β(β ≤ 1 → 해체 가능) · Alvarado 외 표 6~8 · → 3개 층: 하중 검토 통과 + 공기 동일 + 자재 최소 → 최저 비용
- **Visualization**: beta-matrix = table/comparison_matrix
- **Native-ready**: beta-matrix=no
- **Fact IDs**: 참고문헌 [6]

#### Slide 14 - 적용 가능성

- **Audience move**: 도구가 사고를 다 막는다고 오해할 수 있음 → 사고 원인 중 무엇을 판정으로 대응하고 무엇이 범위 밖인지 구분한다
- **Relationships**: 사고 원인 6가지를 판정으로 대응 / 일부 대응 / 범위 밖으로 나눔(membership); 각 원인 ↔ 사고 사례 ↔ 판정 항목(link)
- **Composition**: 3열 분류(대응 3 · 일부 1 · 범위 밖 2)
- **Title**: III. 적용 가능성
- **Core message**: 사고 원인 6가지 중 3가지는 판정 항목으로 직접 대응한다
- **Content**: 판정으로 대응 — 조기 해체·존치 부족(화정 · 존치 층수, 해체 강도) / 강도 미달(화정 · 현장양생 공시체 강도) / 높이에 맞지 않는 동바리(안성 물류창고 · 높이 기준: 3.5 m 넘으면 2 m마다 수평연결재, 5 m 이상 구조 안전성 확인 대상) · 일부 대응 — 지지 가설재 미설치(사당체육관 · 검측 기록 없으면 '확인 필요') · 범위 밖 — 수평재 간격·동시 타설(냉수물천교 · 시공상세도·타설 순서) / 임의 공법 변경(화정 가벽 · 구조 검토)
- **Fact IDs**: 참고문헌 [20][21]

### Part 5: IV. 시연

#### Slide 15 - 시연 영상

- **Audience move**: 말로만 들음 → 실제 Revit 화면에서 구역 나누기부터 판정·근거까지 이어지는 것을 본다
- **Relationships**: 리본 패널 순서 = 현장 업무 순서, 왼쪽부터 오른쪽으로(order)
- **Composition**: 도면 뷰포트 틀 안에 캡처 화면 크게, 아래 재생 정보
- **Title**: IV. 시연 영상
- **Core message**: 구역 나누기 → 판정까지 실제 Revit 화면으로 보인다
- **Content**: Revit On-Site 탭 — 현장 업무 순서대로 7개 패널 · 리본 패널 = 현장 업무 순서, 왼쪽부터 오른쪽으로 진행 · 시연 모델 shoring_demo.rvt, 데이터는 시연용 가상 자료 · 영상 길이 3분 52초
- **Images**: demo_revit_screen.jpg

### Part 6: V. 기술 적용의 기대효과

#### Slide 16 - 기대효과 한눈에

- **Audience move**: 효과를 뭉뚱그려 들음 → 4가지 효과의 확실성 수준(확실·조건부·가능성)을 구분한다
- **Relationships**: 안전(확실) → 기록·책임(확실) → 자재·비용(조건부) → 공정(가능성) 확실성 순(order)
- **Composition**: 4행 목록, 오른쪽 3단 확실성 눈금
- **Title**: V. 기대효과
- **Core message**: 프로그램이 직접 만드는 효과부터, 확실한 순서로
- **Content**: 안전 · 해체 전에 강도·존치 층수를 판정하고, 승인 없이 빼면 바로 빨강 · 확실 · 기록·책임 · 누가 언제 판정·승인·해체했는지가 결과물로 남음 · 확실 · 자재·비용 · "3개 층으로 충분한가"의 근거를 줌 — 4개 층씩 두던 현장에서만 줄어듦 · 조건부 · 공정 · 강도가 난 구역을 바로 알려 주지만, 공시체를 언제 시험하느냐에 달림 · 가능성

#### Slide 17 - 안전

- **Audience move**: 3개 층 존치의 의미가 추상적 → 받침을 먼저 빼면 연달아 무너지고, 프로그램은 3개 층이 다 있기 전엔 '존치'로 막는다는 것을 그림으로 본다
- **Relationships**: 화정 아파트(2022): 타설 중 젖은 콘크리트 무게 + 아래 두 층 받침을 먼저 뺌 → 연달아 붕괴 ↔ Revit On-Site 판정: 받침 1·2·3 모두 있음 → 3개 층이 다 있기 전에는 '존치', 해체 요청이 나가지 않음(contrast)
- **Composition**: 좌우 같은 축척 단면 비교(왼쪽 점선 철거 + 빨강 주석, 오른쪽 실선 3개 층 + 파랑 주석)
- **Title**: V. 기대효과: 안전
- **Core message**: 위층을 부을 때는 아래 3개 층이 받쳐 줘야 한다
- **Content**: 화정 아파트(2022) · 타설 중 젖은 콘크리트 무게 · 받침을 먼저 뺌 ×2 · 무게를 받을 층이 없어 아래로 연달아 무너졌습니다 · Revit On-Site로 판정하면 · 받침 1 · 받침 2 · 받침 3 · 3개 층이 다 있기 전에는 '존치' — 해체 요청이 나가지 않습니다
- **Motion suggestion**: 표지의 3개 층 단면이 이 장의 오른쪽 단면으로 이어짐(Morph 후보)

#### Slide 18 - 기록·책임

- **Audience move**: 기록이 왜 필요한지 약하게 느낌 → 지금 현장과 도구 사용 후를 3가지씩 비교하고 실제 검측요청서를 본다
- **Relationships**: 지금 현장(해체 사실: 뺀 반장만 앎 / 감리 승인: 종이 서명을 옮겨 적음 / 판정 근거: 흩어지고 나중에 씀) ↔ Revit On-Site를 쓰면(빼면 바로 신고: 승인 없으면 즉시 빨강 / 감리가 직접 남김: 휴대폰으로 현장에서 / 8단계 원장: 고치면 바로 드러남) 짝 contrast; 검측요청서 = 증거
- **Composition**: 왼쪽 2열 대응 3행, 오른쪽 검측요청서 이미지
- **Title**: V. 기대효과: 기록·책임
- **Core message**: "몰랐다"는 말이 통하지 않게 기록이 남는다
- **Content**: 지금 현장 / Revit On-Site를 쓰면 · 해체 사실 — 뺀 반장만 앎 → 빼면 바로 신고 — 승인 없으면 즉시 빨강 · 감리 승인 — 종이 서명을 옮겨 적음 → 감리가 직접 남김 — 휴대폰으로 현장에서 · 판정 근거 — 흩어지고 나중에 씀 → 8단계 원장 — 고치면 바로 드러남 · 프로그램이 만든 검측요청서 — 감리 서명란은 비워 둠
- **Images**: inspection_request.jpg

#### Slide 19 - 자재·비용 (1): 원리

- **Audience move**: 동바리 비용 구조를 모름 → 빌려야 할 양 = 받치는 층 수이고 '3개 층으로 충분한가'가 핵심임을 안다
- **Relationships**: 한 층을 받치는 동바리 = 세트 하나 → 빌려야 할 양 = 받치는 층 수 → "3개 층으로 충분한가"가 핵심(order); 세트 1·2·3 + 세트 4?(점선)
- **Composition**: 왼쪽 4층 단면(세트 4 점선 주황), 오른쪽 번호 3단 설명, 아래 결론 띠
- **Title**: V. 기대효과: 자재·비용 (1)
- **Core message**: 동바리는 빌려서 층마다 옮겨 쓰는 자재다
- **Content**: 1 한 층을 받치는 동바리 = 세트 하나: 다 굳은 맨 아래층 세트를 빼서 새로 부을 층 밑으로 올립니다 · 2 빌려야 할 양 = 받치는 층 수: 4개 층을 받치면 4세트, 3개 층이면 3세트를 계속 빌려 둡니다 · 3 "3개 층으로 충분한가"가 핵심: 불안해서 4번째 층을 남기는 대신, 프로그램이 하중비(아래 3개 층이 견디는가)를 구역마다 보여 줍니다 · 세트 하나를 덜 빌리면 이론상 4분의 1, 실제 연구에서는 동바리·띠장 비용이 20% 줄었습니다

#### Slide 20 - 자재·비용 (2): 실제 숫자

- **Audience move**: 절감이 막연함 → 실제 4개 층 → 3개 층 현장 숫자와, 줄어드는 현장·안 줄어드는 현장을 구분한다
- **Relationships**: 항목(동바리·띠장·공기·총공사비) × 4개 층 받침 vs 3개 층 받침(grid); 줄어드는 현장 ↔ 안 줄어드는 현장(contrast)
- **Composition**: 위 표, 아래 좌우 조건 두 칸
- **Title**: V. 기대효과: 자재·비용 (2)
- **Core message**: 실제로 4개 층을 3개 층으로 줄인 현장의 숫자
- **Content**: 스페인 14층 아파트, 단위 유로 · 동바리 6,439 → 5,151 (−20%) · 띠장 7,083 → 5,666 (−20%) · 공기 101일 → 101일 그대로 · 총공사비 129,217 → 126,513 (−2.09%) · 줄어드는 현장: 습관적으로 4개 층 이상 받치던 현장 — 하중비로 3개 층이면 된다는 근거가 생김 · 안 줄어드는 현장: 이미 기준 최소인 3개 층 현장, 겨울에 강도가 안 나 '존치'가 길어지는 현장
- **Visualization**: cost-table = table/metric_table
- **Native-ready**: cost-table=no
- **Fact IDs**: 참고문헌 [6]

#### Slide 21 - 누가 무엇을 받나

- **Audience move**: 사용자가 공무 한 명이라고 생각 → 같은 판정 하나가 다섯 사람에게 다른 결과물로 간다는 것을 안다
- **Relationships**: 판정 하나 → 시공사 공무(판정표·대시보드) / 감리(검측요청서) / 협력업체 반장(층 표지 QR) / 발주처(슬래브 이력서) / 정부·제도(IFC·근거 원장)(membership)
- **Composition**: 가운데 판정 하나에서 5갈래로 나가는 배치 또는 5칸
- **Title**: V. 기대효과: 누가 무엇을 받나
- **Core message**: 같은 판정 하나가 다섯 사람에게 다른 결과물로 간다
- **Content**: 시공사 공무 · 판정표·대시보드 · 된 구역은 근거 있게 빨리, 안 된 구역은 존치 · 감리 · 검측요청서 · 확인할 세 가지와 근거가 한 장에 · 협력업체 반장 · 층 표지 QR · 층 입구에서 오늘 빼도 되는지 직접 봄 · 발주처 · 슬래브 이력서 · 준공 뒤 하자 분쟁에 낼 구역별 기록 · 정부·제도 · IFC·근거 원장 · 의무 공시체가 실제로 쓰였는지 위치로 확인 · 프로그램의 결론은 '해체 승인 요청 가능' 또는 '존치'까지입니다. 빼라는 결정은 감리가 합니다.

### Part 7: VI. 기술의 보완방안

#### Slide 22 - 현재 한계와 보완 계획

- **Audience move**: 한계를 모름 → 5가지 한계와 각각의 보완 계획을 짝으로 본다
- **Relationships**: 한계 5 ↔ 보완 계획 5(1:1 link)
- **Composition**: 2열 대응표, 화살표
- **Title**: VI. 현재 한계와 보완 계획
- **Core message**: 한계마다 보완 계획이 있다
- **Content**: 효과 수치가 해외 단일 사례 기반 → 국내 현장에서 존치 기간·수량·비용 기록 · 하중비교는 아래층 동바리를 그대로 둔 경우에만 적용 → 동바리 제거·재설치 구역 → '상세 구조 계산 필요' 분류 · 센서 강도 추정의 신뢰성 한계 → 법정 근거는 현장양생 공시체, 센서는 보조 · 가상 데이터로만 검증 → 실측 공시체 결과로 재검증 · 판정에 법적 효력 없음 → '승인 요청 가능'까지만 표시 · 추가 계획(대본): 승인 요청 검토서 자동 출력 기능

### Part 8: VII. 결론 및 참고문헌

#### Slide 23 - 결론

- **Audience move**: 내용이 흩어져 있음 → 핵심 메시지 한 줄과 문제·기준·이점·역할 4요소로 정리한다
- **Relationships**: 문제 → 기준 → 이점 → 역할(order), 모두 '날짜가 아닌 기준으로 판단, 근거 기록'으로 수렴(parent)
- **Composition**: 표지 시트 계열 진한 바탕, 위 핵심 문장 크게, 아래 4칸
- **Title**: VII. 결론
- **Core message**: 날짜가 아닌 기준으로 판단하고, 근거를 기록한다
- **Content**: 날짜가 아닌 기준으로 판단 · 근거 기록 · 문제: 사고는 설치·해체 단계, 모두 타설 중 · 기준: 강도 · 존치 층수 · 지지층 강도 · 하중비교 · 이점: 기준 판정 → 해체 시점 단축, 동바리·비용 절감(선행 연구) · 역할: 구역별 판정, 근거 기록, 결정은 감리
- **Closing impact**: 핵심 메시지 한 줄 + 3개 층 단면 모티프 재등장
- **Motion suggestion**: 표지의 3개 층 단면이 여기서 다시 나타남(Morph 후보)

#### Slide 24 - 참고문헌 (1/2)

- **Audience move**: 출처 확인 필요 → 기준·연구 문헌 10건을 확인한다
- **Relationships**: [1]–[10] order
- **Composition**: 2단 목록, 작은 글씨
- **Title**: VII. 참고문헌 (1/2)
- **Core message**: 기준·연구 출처
- **Content**: [1]–[10] 원본 24장 그대로 (국토교통부 KCS 14 20 12:2024 / 거푸집 및 동바리 해체 가이드라인 2023.1 / KDS 21 50 00:2024 / ACI PRC-347.2-17(25) / 천성철 외 2014 / Alvarado 외 2016 / Hamooni 외 2020 / Iqbal 외 2024 / 서형석 외 2024 / 김오형·차희성 2019)

#### Slide 25 - 참고문헌 (2/2)

- **Audience move**: 출처 확인 필요 → 사고 보도·법령 13건과 웹 자료 열람일을 확인한다
- **Relationships**: [11]–[23] order
- **Composition**: 2단 목록, 작은 글씨
- **Title**: VII. 참고문헌 (2/2)
- **Core message**: 사고 보도·법령 출처
- **Content**: [11]–[23] 원본 25장 그대로 (노컷뉴스·아시아경제 2022.3.14 / 한국일보 2015.2.11 / 쿠키뉴스 2015.8.13 / 서울신문 2015.3.26 / 시대일보 2015.4.2 / 뉴시스 2022.10.26 / 경기일보 2025.7.27 / 트윅 2023.8.16 / 산업안전보건기준에 관한 규칙 제332조의2 / 건설기술 진흥법 시행령 제101조의2 / 국토교통부 고시 제2024-879호 / KCS 21 50 05:2023) · 웹 자료 열람일: 2026.11.7.

### Part 9: VIII. Q&A

#### Slide 26 - Q&A

- **Audience move**: 발표를 들음 → 핵심 메시지를 다시 보며 질문한다
- **Relationships**: none
- **Composition**: 진한 표지 시트, 학교 문장, 큰 Q&A, 핵심 메시지 한 줄
- **Title**: Q&A
- **Core message**: 질문을 받겠습니다
- **Content**: Q&A · 질문을 받겠습니다. 감사합니다. · 판정은 프로그램이, 결정은 감리가 · 팀 Build Up
- **Images**: khu_crest.png

## X. Speaker Notes Requirements

- **Generation**: enabled
- **Filename**: match each SVG filename under `notes/`
- **Content**: 사용자가 채팅으로 준 발표 대본(2026-11-19)이 최종 대본 — preserve verbatim. 대본 1→P01, 2–3→P02, 4→P03, 5–23→P05–P23 같은 번호, 24–25→P24·P25에 같은 문장, 26→P26. P04(새로 넣은 시스템 구성 장)만 대본에 없어 화면 내용으로 같은 말투의 짧은 노트를 새로 쓰고 사용자에게 알린다. notes/total.md 에 장별로 고정.
- **Total duration**: 대본 기준 약 12–14분 + 시연 영상 3분 52초
- **Notes style**: 사용자 대본 그대로(발표자 구어체)
- **Presentation purpose**: 동바리 해체 판단 문제를 보이고 Revit On-Site의 기준 판정·근거 기록을 입증한 뒤 효과와 한계를 정직하게 제시

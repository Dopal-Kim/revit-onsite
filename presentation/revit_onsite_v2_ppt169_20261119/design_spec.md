<!-- ppt-master-schema: design-spec/v1 -->
# Revit On-Site 발표 v2 - Design Spec

## I. Project Information

| Item | Value |
| --- | --- |
| Project Name | revit_onsite_v2 |
| Canvas Format | ppt169 (1280×720) |
| Page Count | 30 |
| Primary Language | ko-KR |
| Target Audience | 건축공학시스템설계 담당 교수님(백장운 교수님)과 수강생 — 콘크리트·거푸집 공사와 BIM(Revit) 기본 지식이 있고, 실제 현장 적용성과 근거의 정확성을 봄 |
| Communication Intent | 먼저 동바리 해체 판단이 왜 위험한 문제인지(사고 사례·기준 분산·경험 의존)를 보이고, 다음으로 Revit On-Site가 기준과 실측값으로 구역마다 판정하고 근거를 남기는 방식을 설명·시연으로 입증한 뒤, 기대효과를 확실성 순서로 제시하고 한계와 보완 계획을 정직하게 밝힌다 |
| Desired Audience Outcome | 청중이 판정 흐름(강도·존치 층수·지지층 강도·하중비 → '해체 승인 요청 가능' 또는 '존치' → 감리 최종 승인)을 설명할 수 있고, 어떤 효과가 확실하고 어떤 효과가 조건부인지 구분해 이 도구의 현장 적용 가치를 평가할 수 있다 |
| Core Message / Ask / Action | 날짜가 아닌 기준으로 판단하고, 그 근거를 기록한다 — 판정은 프로그램이, 결정은 감리가 한다 |
| Delivery Context | 주: 발표자가 진행하는 수업 프로젝트 발표(중간에 시연 영상 2분 7초 재생) 후 질의응답 / 부: 발표 뒤 평가자가 파일을 다시 읽어 보는 용도 |
| Artifact Afterlife | 과목 제출·평가 자료로 보관(참고문헌·출처 포함) |
| Reading Mode | balanced |
| Content Strategy | 사용자 지시(11-19): "초안을 기반으로가 아니라 지금까지의 프로젝트 내용에 집중해서 새롭게 … 대본의 내용만 기억하고 슬라이드 구성이랑 디자인 모두 새롭게". 대본의 사실·주장은 모두 유지하고, 구성은 프로젝트(애드인 실제 화면·검증·결과물) 중심으로 새로 짠다. 사실은 sources/ 의 프로젝트 문서에서만. |
| Design Style | 근거 주석 쇼케이스 (custom) — 실제 화면 + 번호 핀·지시선 |
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
| Margins | 64 px left/right, 48 px top, 40 px bottom |
| Content Area | x 64–1216, y 48–680 |

## III. Visual Theme

### Theme Style

- **Mode**: custom
- **Mode References**: narrative, showcase
- **Mode Behavior**: 질문 → 위험의 뿌리 → 흩어진 기준(narrative 긴장)에서 Revit On-Site 본체로 넘어가면 showcase처럼 실제 화면 한 장이 페이지를 차지하고 번호 핀으로 근거를 짚는다. 효과는 확실성 순서로, 한계는 정직하게. 제목은 판단 문장, 한 장에 주장 하나.
- **Visual style**: custom
- **Visual Style References**: swiss-minimal, editorial
- **Visual Style Behavior**: 넓은 흰 여백과 엄격한 좌측 정렬 격자. 제목은 큰 굵은 판단 문장, 왼쪽 위에 작은 파란 장 태그. 실제 화면은 얇은 회색 테두리의 '증거판'으로 크게 놓고, 파란 번호 핀(원 + 숫자)과 가는 지시선으로 화면 속 근거를 짚으며 옆 주석 목록과 번호로 짝짓는다. 판정 상태는 애드인과 같은 의미 색(요청 가능 초록 · 존치 주황 · 시험 필요 노랑)으로만 표시한다. 장식 없음, 그림자 없음, 모서리 6 px, 면 채움은 옅은 청회색 한 단계. 숫자는 Segoe UI 굵게 크게.
- **Theme**: 근거 주석 쇼케이스 — 흰 바탕 본문, 장 전환·표지·결론은 진한 남색 면
- **Tone**: 정확하고 자신 있는 제품 발표, 과장 없이 근거 중심

### Color Scheme

| Role | HEX | Purpose |
| --- | --- | --- |
| Background | #FFFFFF | 본문 바탕 |
| Secondary background | #EEF3F9 | 증거판 바탕, 주석 칸, 옅은 면 |
| Primary | #1B2A41 | 제목, 진한 면(표지·장 전환·결론) |
| Accent | #1F5FA8 | 번호 핀, 장 태그, 강조 수치, 링크선 |
| Secondary accent | #E8613C | 존치·위험·무단 해체 의심 |
| Body text | #1E2633 | 본문 |
| Secondary text | #5E6B7A | 캡션, 출처, 쪽 번호 |
| Divider | #D5DCE4 | 증거판 테두리, 구분선 |
| Positive | #5FA83A | '해체 승인 요청 가능' 상태 |
| Warning | #E9B93A | '시험 필요' 상태 |

## IV. Typography System

### Font Plan

| Role | Character (Reference) | Primary | English if non-English | Fallback tail |
| --- | --- | --- | --- | --- |
| Title | 굵은 산세리프 판단 문장 | Malgun Gothic | Segoe UI | sans-serif |
| Body | 중립 산세리프 | Malgun Gothic | Segoe UI | sans-serif |
| Data | 굵은 수치 | Segoe UI | Segoe UI | sans-serif |

- **Title stack**: Malgun Gothic, Segoe UI, sans-serif
- **Body stack**: Malgun Gothic, Segoe UI, sans-serif
- **Data stack**: Segoe UI, Malgun Gothic, sans-serif
- **Role rationale**: Data — 강도·하중·β·건수 같은 근거 수치와 핀 번호가 매 장 반복된다.

### Font Size Hierarchy

| Purpose | Anchor Size (px) |
| --- | ---: |
| Body | 20 |
| Title | 36 |
| Subtitle | 22 |
| Lead | 24 |
| Annotation | 15 |
| Footnote | 12 |
| Display | 64 |
| Cover title | 80 |
| Data | 28 |

## V. Layout Principles

### Deck-wide Direction

- **Hierarchy direction**: 장 태그 → 판단 제목 → 증거(화면·도식·수치) → 핀 주석 → 출처
- **Composition tendency**: 화면이 근거인 장은 화면이 60% 이상, 주석은 오른쪽 좁은 열. 비교는 좌우, 순서는 왼→오.
- **Cross-page continuity**: 파란 번호 핀과 상태 색 칩이 모든 Revit On-Site 장에서 같은 뜻으로 반복. 장 전환 남색 면.
- **Spacing posture**: 여백 넓게, 증거 장만 dense
- **Spacing anchors**: page margin 64 px · block gap 24 px · column gutter 32 px · corner radius 6 px · body leading 1.5

## VI. Icon Usage Specification

- **Primary bundled library**: tabler-outline
- **Stroke Width**: 2

| Icon Path | Suitable Scenarios |
| --- | --- |
| icons/tabler-outline/device-mobile.svg | |
| icons/tabler-outline/qrcode.svg | |
| icons/tabler-outline/cloud.svg | |
| icons/tabler-outline/cube.svg | |
| icons/tabler-outline/file-text.svg | |
| icons/tabler-outline/lock.svg | |
| icons/tabler-outline/database.svg | |
| icons/tabler-outline/alert-triangle.svg | |
| icons/tabler-outline/shield-check.svg | |
| icons/tabler-outline/file-certificate.svg | |
| icons/tabler-outline/coin.svg | |
| icons/tabler-outline/calendar.svg | |
| icons/tabler-outline/check.svg | |
| icons/tabler-outline/player-play.svg | |
| icons/tabler-outline/users.svg | |
| icons/tabler-outline/world.svg | |
| icons/tabler-outline/home.svg | |
| icons/tabler-outline/settings.svg | |
| icons/tabler-outline/tool.svg | |
| icons/tabler-outline/rosette-discount-check.svg | |
| icons/tabler-outline/ruler-measure.svg | |
| icons/tabler-outline/temperature.svg | |
| icons/tabler-outline/clipboard-check.svg | |
| icons/tabler-outline/building.svg | |

## VII. Visualization Reference List

| Page | Family | Template | Usage |
| --- | --- | --- | --- |
| P07 | chart | column_chart | 타설층 하중 12.33 vs 아래 3개 층 여유 15.50 kN/m² |
| P20 | table | record_table | 시나리오 검증 S0–S11 결과 |
| P24 | table | metric_table | 4개 층 vs 3개 층 받침 비용·공기 |

## VIII. Image Resource List

| Filename | Dimensions | Ratio | Purpose | Type | Image pattern | Crop Policy | Acquire Via | Status | Reference | text_policy | page_role |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| shot_14_view.png | 1575×767 | 2.05 | 판정 뒤 검측 3D + 대시보드 | Screenshot | 표지 오른쪽 큰 화면, 대시보드 장 증거판 | adaptive | user | Existing | guide_shots 14, 제목 표시줄(계정명) 잘라 냄 | none | local |
| shot_01_ribbon.png | 1920×160 | 12.0 | Revit On-Site 리본 7개 패널 | Screenshot | 전체 폭 띠 + 패널별 핀 | no-crop | user | Existing | guide_shots 01 리본 부분 | none | local |
| shot_15_lines.png | 490×212 | 2.31 | 판정 근거 창 9F-B1 비교 줄 | Screenshot | 증거판 + 번호 핀 5개 | no-crop | user | Existing | guide_shots 15 의 비교 줄 부분 | none | local |
| shot_20_alerts.png | 385×248 | 1.55 | 대시보드 '오늘 볼 것' 무단 해체 의심 목록 | Screenshot | 무단 해체 장 증거 | no-crop | user | Existing | guide_shots 20 의 오른쪽 목록 | none | local |
| shot_08.png | 623×584 | 1.07 | 3개층 확인 결과 | Screenshot | 설치 단계 장 왼쪽 증거 | no-crop | user | Existing | guide_shots 08 | none | local |
| shot_09.png | 578×577 | 1.00 | 설치 검측 결과 | Screenshot | 설치 단계 장 가운데 증거 | no-crop | user | Existing | guide_shots 09 | none | local |
| shot_27.png | 1100×520 | 2.12 | 층 표지 머리 (1F 동바리 해체 금지) | Screenshot | 무단 해체 장 증거 | no-crop | user | Existing | guide_shots 27 | none | local |
| shot_20_view.png | 1575×767 | 2.05 | 강도 타임라인 + 무단 해체 의심 목록 | Screenshot | 무단 해체·시연 장 증거 | adaptive | user | Existing | guide_shots 20, 제목 표시줄 잘라 냄 | none | local |
| shot_17.png | 598×514 | 1.16 | 승인 기록 창 | Screenshot | 원장 장 증거 | no-crop | user | Existing | guide_shots 17 | none | local |
| shot_18_top.png | 1240×880 | 1.41 | 해체 검측요청서 첫 쪽 윗부분 | Screenshot | 결과물 장 왼쪽 큰 증거판 | no-crop | user | Existing | guide_shots 18 윗부분 | none | local |
| cover_model.png | 620×720 | 0.86 | 표지 오른쪽 검측 3D 화면 | Screenshot | 표지 오른쪽 면 전체 | no-crop | user | Existing | guide_shots 14 의 3D 뷰 부분 | none | local |
| shot_21.png | 1600×1000 | 1.60 | 웹 공유판 4D | Screenshot | 결과물 장 화면 | no-crop | user | Existing | guide_shots 21 | none | local |

## IX. Content Outline

### Part 1: 열기

#### Slide 01 - 표지

- **Audience move**: 주제를 모름 → 도구 이름과 한 문장 약속을 안다
- **Relationships**: 약속 문장 → 도구 이름; 과목·교수·팀 membership
- **Cover impact**: "날짜가 아닌 기준으로" + 실제 판정 화면
- **Title**: Revit On-Site
- **Core message**: 동바리 설치·해체 판정을 위한 Revit 애드인
- **Content**: 날짜가 아닌 기준으로 · 동바리 설치·해체 판정을 위한 Revit 애드인 · 건축공학시스템설계 / 백장운 교수님 / 건축공학과 / Build Up / 김도영, 이영준, 최예준, 최영윤
- **Images**: cover_model.png (shot_14_view.png 모델 부분)

#### Slide 02 - 목차

- **Audience move**: 순서를 모름 → 6장 흐름을 안다
- **Relationships**: 6개 장 order
- **Title**: 발표 순서
- **Core message**: 질문 → 기준 → 도구 → 검증 → 효과 → 한계
- **Content**: 01 질문 / 02 기준 / 03 Revit On-Site / 04 검증 / 05 기대효과 / 06 한계와 결론

#### Slide 03 - 현장의 질문

- **Audience move**: 문제를 추상적으로 앎 → 매일 나오는 한 질문과 지금 방식의 한계를 안다
- **Relationships**: 질문 → 지금(날짜 사이클·경험) contrast Revit On-Site(기준·근거)
- **Title**: 매일 나오는 질문 하나
- **Core message**: "이 구역 동바리, 오늘 해체 승인을 요청해도 될까요?"
- **Content**: 질문 · 지금: 날짜 사이클과 경험으로 판단 — 너무 일찍 빼면 붕괴, 너무 오래 두면 공정 지연 · Revit On-Site: 구역마다 기준과 실측값으로 판정하고 근거 조항을 함께 보여 줌 · 최종 승인은 감리

### Part 2: 01 질문 — 왜 위험한가

#### Slide 04 - 같은 뿌리

- **Audience move**: 사고를 개별 사건으로 봄 → 타설·양생 단계 슬래브 품질이 공통 원인이고 제도도 이 방향임을 안다
- **Relationships**: 화정·검단 사고 → 타설·양생 단계 강도 문제(parent) → 제도 변화(2025 현장양생 공시체 의무화, 검단 권고)
- **Title**: 붕괴의 뿌리는 타설·양생 단계에 있습니다
- **Core message**: 품질은 타설·양생 때 정해지지만 지금은 날짜로 관리한다
- **Content**: 화정 2022: 동바리 조기 철거, 17개 층 중 15개 층 강도 미달(최저 9.8 MPa), 동절기 양생 미흡 · 검단: 표준양생 공시체 합격인데 구간 강도 미달 · 2025-01-01 현장양생 공시체 의무화(1일 1회·층·구획) · 검단 조사위 권고 "검측 자료 디지털화·BIM 활용 기반 마련" · 국토부: 강도 부족 = 아파트 사고 주요 원인 중 하나
- **Fact IDs**: sources/문제정의_방향성.md §2, 원본 발표 7·8장

#### Slide 05 - 사고는 모두 타설 중에

- **Audience move**: 사고 단계 구분이 흐림 → 해체 1건·설치 4건 모두 타설 중 발생과 원인을 안다
- **Relationships**: 사고 5건 시간 order, 단계(설치/해체) membership
- **Title**: 다섯 건 모두 콘크리트를 타설하는 도중이었습니다
- **Content**: 2015.2 사당종합체육관(설치) 11명 매몰·부상 · 지지 가설재 없이 타설 · 2015.3 냉수물천교(설치) 1명 사망·8명 부상 · 수평재 120 cm·동시 타설 · 2022.1 광주 화정(해체) 6명 사망·1명 부상 · PIT층 아래 동바리 조기 철거, 하중 2.24배, 15/17층 강도 미달 · 2022.10 안성 저온물류창고(설치) 3명 사망·2명 중상 · 조립도 미작성, 12.8 m에 10 m·3 m 잭서포트 볼트 연결 · 2023.8 안성 근린생활시설(설치) 2명 사망·4명 부상 · 거푸집·동바리 붕괴(추정)
- **Fact IDs**: 참고문헌 [11]–[19]

### Part 3: 02 기준

#### Slide 06 - 판정 기준 네 가지

- **Audience move**: 기준이 흩어져 있음 → 도구가 확인하는 4가지 기준과 조항을 한 장에서 본다
- **Relationships**: 4가지 기준 membership, 기준 → 출처 조항 link
- **Title**: 흩어진 기준 네 가지를 한 번에 확인합니다
- **Content**: 강도 ≥ 설계기준압축강도 (KCS 14 20 12 3.3.1) · 타설층 포함 3개 층 존치 (KCS 14 20 12 3.3.2(2), 가이드라인 2.1.2(1)④) · 지지층 14 MPa 이상 (가이드라인 2.1.2(2)⑧) · 하중 비교 적합 (가이드라인 2.1.2(2), 설계 연직하중 최소 5.0 kN/m² KDS 21 50 00 1.6.2(5)) · 지금은 표준시방서·가이드라인·설계기준에 나뉘어 있음

#### Slide 07 - 몇 개 층을 남길까, 겨울에는

- **Audience move**: 존치 층수 근거를 모름 → 하중 비교 원리와 겨울에는 3개 층이 부족할 수 있음을 안다
- **Relationships**: 타설층 하중 12.33 contrast 아래 3개 층 여유 15.50 → 3개 층 충분; 계절 contrast(5/1 3개 층 vs 12/1 5개 층 필요)
- **Title**: 몇 개 층을 남길지는 하중 비교로 정합니다
- **Content**: 가이드라인 계산 예시(슬래브 210 mm): 12.33 < 15.50 kN/m² → 3개 층 유지로 충분 · 동바리를 빼거나 다시 세운 구역은 상세 구조 계산 필요 · 엔진 계산(시나리오 S3): 5/1 첫 타설 필요 3개 층, 12/1 첫 타설 필요 5개 층 — 겨울엔 최소 3개 층으로 부족
- **Visualization**: load-compare = chart/column_chart
- **Native-ready**: load-compare=no
- **Fact IDs**: 참고문헌 [2], sources/scenario_validation.md S3

### Part 4: 03 Revit On-Site

#### Slide 08 - 장 전환: 리본 7개 패널

- **Audience move**: 도구를 처음 봄 → 리본 순서가 현장 업무 순서임을 안다
- **Relationships**: 7개 패널 order
- **Title**: 리본 순서가 곧 현장 업무 순서입니다
- **Content**: 03 Revit On-Site · ① 준비 ② 동바리 ③ 설치 검측 ④ 타설·데이터 ⑤ 판정 ⑥ 승인 ⑦ 보고·공유 · 타임라인 (리본 화면)
- **Images**: shot_01_ribbon.png

#### Slide 09 - 시스템 구성

- **Audience move**: 내부 구조 모름 → Revit·현장 서버·휴대폰의 역할 분담을 안다
- **Relationships**: 휴대폰 두 역할 → 현장 서버 → Revit 5분마다 동기화 → 결과물 (link)
- **Title**: 판정은 Revit에서, 입력은 현장 휴대폰으로
- **Content**: 품질·안전관리자: QR → 공시체·센서·반발경도·동바리 확인, 사진 1장 · 협력업체 반장: 층 QR → '뺐음'·'다시 세움' 신고 · 현장 서버(인터넷) revit-onsite-field.vercel.app, 열쇠는 QR로만 · 5분마다 동기화 · 공무·BIM 담당: Revit에서 하루 1회 판정 · 결과물은 데이터 폴더에
- **Fact IDs**: sources/revit_onsite_system_facts.md

#### Slide 10 - 하루 흐름

- **Audience move**: 언제 누가 무엇을 하는지 모름 → 층마다 반복되는 7단계 하루를 안다
- **Relationships**: 7단계 order, 단계별 역할 membership
- **Title**: 층마다 반복되는 하루
- **Content**: 1 아침 판정(공무·BIM) · 2 휴대폰 확인·입력(품질·안전) · 3 타설 전 확인(안전 → 공무) · 4 검측요청서·패키지(공무·BIM) · 5 감리 확인·서명(감리) · 6 해체 신고(협력업체 반장) · 7 해체 후 점검(안전)
- **Fact IDs**: sources/revit_onsite_system_facts.md

#### Slide 11 - 판정 근거 한 장

- **Audience move**: 판정이 블랙박스라고 생각 → 비교 한 줄씩 근거가 보이는 실제 화면을 본다
- **Relationships**: 근거 5줄 membership → 결론 '해체 승인 요청 가능'
- **Title**: 판정은 비교 한 줄씩, 근거와 함께 나옵니다
- **Content**: 9F-B1 검측 결과 — 해체 승인 요청 가능 · ① 공시체 25.1 ≥ 24.0 MPa ② 센서 구역 최소 24.5 ≥ 24.0 MPa ③ 존치 층수 3 ≥ 3 ④ 지지층 10F 24.0 · 11F 22.1 · 12F 19.4 ≥ 14.0 MPa ⑤ 하중비 0.83 ≤ 1.00 · 조기 탈형 검토서로 이어짐
- **Images**: shot_15_lines.png

#### Slide 12 - 대시보드와 검측 3D

- **Audience move**: 결과를 어디서 보는지 모름 → 구역 색·오늘 할 일·상태 칩을 한 화면에서 본다
- **Relationships**: 3D 구역 색 ↔ 상태 칩 link; 오늘 할 일 order
- **Title**: 모든 구역의 상태가 한 화면에 보입니다
- **Content**: 검측 3D: 구역 색 = 판정 상태 · 오늘 할 일 ①판정 ②오늘 볼 것 ③요청서 ④층 표지 · 상태 칩: 해체 승인 요청 가능 9구역 · 존치 35구역 · 공시체 시험 필요 3구역 · 감리 승인 기록 1구역 · 해체 완료 24구역 · 강도 예측 곡선은 참고(판정에 안 씀)
- **Images**: shot_14_view.png

#### Slide 13 - 같은 층, 다른 결론

- **Audience move**: 층 단위 해체가 당연하다고 봄 → 같은 층도 구역 최소 강도가 다르다는 위치 근거를 안다
- **Relationships**: 9층 6구역 contrast(A1·B1·C1·C2 요청 가능 12-30 vs A2·B2 존치 → 01-01)
- **Title**: 같은 층이라도 그늘진 구역은 늦게 굳습니다
- **Content**: 시나리오 S1·S11 (시연 가상 자료): 9층 공시체는 6구역 모두 기준 이상 · 센서 구역 최소 강도가 북측 그늘 A2 23.90, B2 23.99 < 24 MPa → 존치 · 처음 '요청 가능': A1·B1·C1·C2 12-30, A2·B2 01-01 · 공시체 한 장으로 층 전체를 빼면 놓치는 구역을 위치로 잡아냄
- **Fact IDs**: sources/scenario_validation.md S1, S11

#### Slide 14 - 설치 단계도 검사합니다

- **Audience move**: 도구가 해체만 본다고 생각 → 3개 층 확인·설치 검측(높이·수평연결재·하중) 화면과 기준을 본다
- **Relationships**: 설치 사고 원인 → 검사 항목 link
- **Title**: 설치 단계 사고 원인도 화면에서 걸러 냅니다
- **Content**: 3개층 확인: 타설층 포함 3개층에 동바리가 없으면 빨강 · 설치 검측: 이음·수평연결재·1본 하중·상하 정렬 · 높이 3.5 m 초과 → 2 m마다 수평연결재(안전보건규칙 제332조의2), 5 m 이상 → 구조 안전성 확인(건진법 시행령 제101조의2) · 1본 하중: 1.2 m 간격 11.4 kN ≤ 13.3 kN OK / 1.5 m 17.9 kN NG (S9)
- **Images**: shot_08.png, shot_09.png
- **Fact IDs**: sources/scenario_validation.md S8 S9, 참고문헌 [20][21]

#### Slide 15 - 승인 없이 빼면 바로 빨강

- **Audience move**: 해체 사실이 반장만 아는 문제를 모름 → 신고·층 표지·무단 해체 의심 경고 흐름을 본다
- **Relationships**: 반장 '뺐음' 신고 → 승인 기록 없음 → 무단 해체 의심(link); 층 표지 = 층 입구 게시
- **Title**: 승인 없이 빼면, 그 순간 빨갛게 드러납니다
- **Content**: 협력업체 반장이 층 QR로 '뺐음' 신고 · 승인 기록이 없으면 즉시 '무단 해체 의심' — 대시보드·휴대폰 '오늘 볼 것' 맨 위 · 층 표지: 층 입구에 오늘 해체 가능/금지 · 화면: 10F-B1 승인 기록 없이 동바리 없음 관측
- **Images**: shot_27.png, shot_20_alerts.png

#### Slide 16 - 근거 원장과 감리 승인

- **Audience move**: 기록이 사후에 정리된다고 봄 → 8단계 원장·해시 사슬·감리 직접 기록을 안다
- **Relationships**: 근거 8단계 order, 승인 기록 → 해시 사슬 link
- **Title**: 모든 단계가 원장에 남고, 고치면 드러납니다
- **Content**: 근거 원장 1–8 (설치검측 · 작업계획서 · 공시체 · 해체전점검 · 검측요청 · 감리확인 · 해체 · 결과보고, shoring/evidence.py STAGES) · 승인 기록 approvals.csv 해시 사슬 · 감리가 휴대폰으로 현장에서 직접 남김 · "프로그램은 승인하지 않습니다 — 감리가 승인한 것·현장의 한 일을 기록"
- **Images**: shot_17.png

#### Slide 17 - 결과물 화면

- **Audience move**: 결과가 화면에만 있다고 봄 → 검측요청서·웹 공유판·IFC가 실제 파일로 나오는 것을 본다
- **Relationships**: 판정 → 결과물 3종 membership
- **Title**: 검측요청서와 웹 공유판도 버튼 하나로 나옵니다
- **Content**: 해체 검측요청서(A4): 판정 근거와 감리 현장 확인 항목이 한 장, 서명란 비워 둠 · 웹 공유판: Revit 없이 브라우저 4D · IFC 내보내기: 구역 판정·근거 속성, 무료 뷰어 · 결과물은 데이터 폴더에 파일로
- **Images**: shot_18_top.png, shot_21.png

#### Slide 18 - 받는 사람

- **Audience move**: 사용자가 공무 한 명이라고 봄 → 같은 판정이 다섯 사람에게 다른 결과물로 가는 것을 안다
- **Relationships**: 판정 하나 → 받는 사람 5 membership
- **Title**: 같은 판정이 다섯 사람에게 다른 결과물로 갑니다
- **Content**: 시공사 공무: 판정표·대시보드 · 감리: 검측요청서 · 협력업체 반장: 층 표지 QR · 발주처: 슬래브 이력서 · 정부·제도: IFC·근거 원장 · 결론은 '해체 승인 요청 가능' 또는 '존치'까지, 결정은 감리

#### Slide 19 - 시연 영상

- **Audience move**: 말로 들음 → 실제 화면 흐름을 영상으로 본다
- **Relationships**: 작동 흐름 order (휴대폰 입력 → 서버 → Revit 판정 → 감리 승인)
- **Title**: 시연 영상
- **Content**: 2분 7초 · 01 층 입력 뷰·사양 · 02 놓기 6가지·구역 채우기 · 03 3개층 확인 · 04 QR 휴대폰 입력 · 05 판정·무단 해체 경고 · 06 요청 제출·검측요청서 출력 · 07 감리 확인·승인 기록 · 08 층 표지 · 시연 모델·데이터는 시연용 가상 자료
- **Images**: shot_20_view.png

### Part 5: 04 검증

#### Slide 20 - 검증과 차별점

- **Audience move**: 결과를 믿을 근거가 없음 → 시나리오 검증 10건 통과와 회귀 검사, 선행 연구와의 차이를 안다
- **Relationships**: 시나리오 record; 선행 연구(측정) contrast 본 과제(판정)
- **Title**: 위험한 조건은 걸러 내고, 된 구역은 통과시켰습니다
- **Content**: 질문 1 위험 조건(S2 S3 S4 S6) · 질문 2 통과(S1 S11 S0) · 질문 3 실제 모델·경계(S8 S9 S7) · 10/10 PASS, S10 미실행 · 회귀 40 · 선행 연구 차이
- **Visualization**: scenario-table = table/record_table
- **Native-ready**: scenario-table=no
- **Fact IDs**: sources/scenario_validation.md, sources/revit_onsite_system_facts.md, 참고문헌 [5]–[9]

### Part 6: 05 기대효과

#### Slide 21 - 효과는 확실한 순서로

- **Audience move**: 효과를 뭉뚱그림 → 확실·조건부·가능성을 구분한다
- **Relationships**: 안전·기록(확실) → 자재·비용(조건부) → 공정(가능성) order
- **Title**: 효과는 프로그램이 직접 만드는 것부터
- **Content**: 안전 · 해체 전에 강도·존치 층수 판정, 승인 없이 빼면 바로 빨강 · 확실 / 기록·책임 · 누가 언제 판정·승인·해체했는지 결과물로 · 확실 / 자재·비용 · 4개 층씩 두던 현장에서만 · 조건부 / 공정 · 강도 난 구역을 바로 알림, 공시체 시험 시점에 달림 · 가능성

#### Slide 22 - ① 안전

- **Audience move**: 안전 효과가 막연함 → 화정형 붕괴 경로 3단계마다 어떤 장치가 막는지 안다
- **Relationships**: 붕괴 경로 order(아래층 조기 철거 → 상층 타설 → 강도 미달 하중) ↔ 장치 link(해체 판정·무단 해체 의심 / 타설 전 24시간 확인 / 공시체 기준·겨울 존치 층수)
- **Title**: 화정형 붕괴 경로를 단계마다 끊습니다
- **Content**: 해체 판정 게이트(3개 층·14 MPa·하중비 미달 → 존치) · 무단 해체 의심(승인 기록 없이 뺐음/없음 → 즉시 빨강) · 타설 전 게이트(아래 3개 층 최근 24시간 확인 → 타설 요청 가능, 없으면 타설 보류) · 공시체 기준(S4) · 겨울 5개 층(S3) · S2 1개 층 존치 15/15층 위반 하중비 2.43–2.78 · 지표: 기준 미달 해체 구역 0, 타설 전 24시간 확인 비율, 의심 경고 해소 시간
- **Fact IDs**: impact_map #1 #2 #3 #22, scenario S2 S3 S4

#### Slide 23 - ② 기록 · 책임

- **Audience move**: 기록 효과가 추상적 → 원장·해시 사슬·사진 해시·검측 패키지·감리 직접·슬래브 이력서를 안다
- **Relationships**: 기록 4종 membership, 기록 → 받는 사람(감리·발주처) link
- **Title**: 사고나 분쟁 때, 그때 남긴 기록을 그대로 꺼냅니다
- **Content**: 지금 → Revit On-Site 비교 4행(해체 사실 · 감리 확인 · 판정 근거 · 감리 제출) · 감리(승인 시점 판정·근거) · 발주처(슬래브 이력서) · 지표. 16장 기능 설명은 반복하지 않음
- **Fact IDs**: impact_map #4 #17 #18 #23 #30

#### Slide 24 - ③ 자재 · 비용

- **Audience move**: 절감이 막연함 → 하중 판정을 통과한 계획만 고를 수 있고 3개 층이 최저 비용임을 숫자로 본다
- **Relationships**: 9가지 계획(3방법 × 2·3·4개 층) → β ≤ 1 통과는 클리어링 3·4개 층; 4개 층 vs 3개 층 비용 contrast
- **Title**: 하중 판정을 거친 3개 층이 가장 쌌습니다
- **Content**: Alvarado 외(2016) 마드리드 14층: 한 번에 해체·재받침 모두 β > 1 불가, 클리어링 3·4개 층 β 0.98 가능, 공기 101일 동일 · 4개 → 3개 층: 동바리 6,439 → 5,151 (−20%) · 띠장 7,083 → 5,666 (−20%) · 공기 101일 그대로 · 총공사비 129,217 → 126,513 (−2.09%), 단위 유로 · 줄어드는 현장: 습관적으로 4개 층 이상 · 안 줄어드는 현장: 이미 3개 층, 겨울 존치 연장 · 동바리는 빌려 옮겨 쓰는 자재 — 빌려야 할 양 = 받치는 층 수
- **Visualization**: cost-table = table/metric_table
- **Native-ready**: cost-table=no
- **Fact IDs**: 참고문헌 [6]

### Part 7: 06 한계와 결론

#### Slide 25 - ④ 공정

- **Audience move**: 공정 효과 근거를 모름 → 구역 단위 요청·판넬 먼저 탈형·타설 묶음 공시체와 그 조건(시험 일정)을 안다
- **Relationships**: 공정 수단 3 membership, 조건(공시체 결과 필요) contrast
- **Title**: 강도가 난 구역부터, 판넬은 먼저 뗄 수 있습니다
- **Content**: S11 같은 층 12-30 / 01-01 · 판넬 단계 4조건(14 MPa, 10 °C 이상 4일, 위층 하중비 ≤ 1.0, 구조검토서) → 판넬만 탈형, 필러 존치 · 타설 묶음 공시체(impact_map #28) · 오늘 볼 것(#9) · 조건: S6 공시체 없음 → 6구역 시험 필요 · 참고: Hamooni 14 → 9.6일, S0 보온 24–29일 / 가열 18일
- **Fact IDs**: impact_map #9 #25 #28, scenario S0 S6 S11, 참고문헌 [7]

#### Slide 26 - 한계와 보완 계획

- **Audience move**: 한계를 모름 → 한계 5가지와 보완 계획을 짝으로 본다
- **Relationships**: 한계 ↔ 보완 1:1 link
- **Title**: 아직 부족한 네 가지와 보완 방법
- **Content**: 해외 단일 사례 수치 → 국내 현장 존치 기간·수량·비용 기록 · 하중 비교 적용 범위 → 제거·재설치 구역 '상세 구조 계산 필요' 분류 · 센서 신뢰성 → 법정 근거는 현장양생 공시체, 센서 보조 · 가상 데이터 검증 → 실측 공시체로 재검증 · 법적 효력 없음 → '해체 승인 요청 가능'까지만 · 추가: 승인 요청 검토서 자동 출력

#### Slide 27 - 결론

- **Audience move**: 내용이 흩어짐 → 핵심 메시지 한 줄로 정리
- **Relationships**: 문제 → 기준 → 도구 → 효과 order
- **Closing impact**: "날짜가 아닌 기준으로 판단하고, 근거를 기록합니다"
- **Title**: 결론
- **Content**: 핵심 문장 · 01 문제(기준 흩어짐) · 02 만든 것(구역 판정 + 한 흐름 기록) · 03 확인한 것(10/10, 효과 확실성) · 다음 단계

#### Slide 28 - Q&A

- **Audience move**: 발표를 들음 → 질문
- **Relationships**: none
- **Title**: Q&A
- **Content**: Q&A · 질문을 받겠습니다. 감사합니다. · 판정은 프로그램이, 결정은 감리가
- **Images**: none

#### Slide 29 - 참고문헌 (1/2)

- **Audience move**: 출처 확인 → 기준·연구 문헌
- **Relationships**: [1]–[10] order
- **Title**: 참고문헌 (1/2)
- **Content**: v1 참고문헌 [1]–[10] 그대로 (sources/original_slide_facts.md Slide 24)

#### Slide 30 - 참고문헌 (2/2)

- **Audience move**: 출처 확인 → 보도·법령 문헌
- **Relationships**: [11]–[23] order
- **Title**: 참고문헌 (2/2)
- **Content**: v1 참고문헌 [11]–[23] 그대로 + 웹 자료 열람일 2026.11.7.


## X. Speaker Notes Requirements

- **Generation**: enabled
- **Filename**: match each SVG filename under `notes/`
- **Content**: 사용자 지시(11-19 2차)로 대본을 새로 씀 — 원래 대본의 사실은 유지하고 표현·구성은 새 슬라이드에 맞춤, humanizer 기준으로 검토. 최종본은 대본_최종.md 와 notes/total.md 가 같음.
- **Total duration**: 약 15분 + 시연 영상 2분 7초
- **Notes style**: 발표자 구어체, 장마다 2–5문장
- **Presentation purpose**: 동바리 해체 판단 문제를 보이고 Revit On-Site의 기준 판정·근거 기록을 실제 화면으로 입증한 뒤 효과와 한계를 정직하게 제시

# 현장 입력 화면 UI·UX 점검 — UI UX Pro Max 규칙 (2026-11-11, vercel 세션)

대상: 현장 입력 QR(`?t=`)로 들어가는 입력 화면 `web/field/index.html` — Vercel `server/vercel/pages/index.html`, Wi-Fi 서버 `tools/field_server.py`, C# `FieldServer` 가 모두 이 한 파일을 냄(바이트 같음, `test_server_sync` 가 확인). 줄 번호는 이 파일 기준.
규칙: `tools/ui_skills/ui-ux-pro-max/quick-reference.md` 10개 범주 + `pro-rules.md` 납품 전 체크리스트 (스타일·팔레트 카탈로그는 안 씀). 판정은 현장 조건(장갑·햇빛·한 손·지하 무신호·흔들림) 기준.
방법: 코드 읽기 + 로컬 시험 서버(dev.js, 시연 자료)에서 Chrome 375 px 틀로 단계마다 실측 — 구역 → 종류 → 폼(공시체·동바리 확인·해체 후 점검·동바리 점검·온도 센서), 밝게·어둡게(OS 다크)·[햇빛] 세 가지. 터치 크기(< 44 px), 글자(< 16 px), 글자 대비(WCAG 4.5:1 / 큰 글자 3:1)를 화면에서 계산.
코드 수정·재배포 없음. 1차 점검(`field_web_audit_20261111.md`)과 겹치는 것은 그 번호(#)로 묶음.

## 요약 — 범주별 위반 수 (모두 19건: 높음 3 (그중 U15 는 1차 #8) · 중간 7 · 낮음 9)
| 범주 (우선순위) | 위반 | 통과한 주요 규칙 |
|---|---|---|
| 1 접근성 | 4 | viewport 확대 허용(5줄), 초점 테두리 4 px(43줄), 라벨-입력 연결, 오류 요약·초점 이동, 읽어 주기 영역(207줄), 색만으로 구분 안 함(타일마다 글자) |
| 2 터치 | 4 | 주요 버튼 52–60 px, 칩 52 px, 동바리 고르기 손가락 반경 24 px(≈48 px 판정) + 8 px 끌기 문턱 + 두 손가락 확대 |
| 3 성능·오프라인 | 1 (=#8) | 요청 10 초 시간 제한 → 기기 대기열, 사진 줄여 보내기(Vercel) |
| 4 스타일 | 1 | 한 화면 주 버튼 하나(하단 [저장]) |
| 5 레이아웃 | 2 | 375 px 가로 넘침 없음, 하단 고정 막대 안전 영역(37·154줄) |
| 6 글자·색 | 2 | 본문 18 px, 숫자 칸 28 px, [햇빛] 고대비 |
| 7 움직임 | 0 | 애니메이션 거의 없음 (해당 적음) |
| 8 입력·피드백 | 4 | 숫자 키패드(inputmode decimal, 579줄), 이름 자동 채움(581줄), 보내는 중 버튼 잠금, 성공 화면, 시간 초과 → 대기열 안내 |
| 9 이동 | 1 | 휴대폰 뒤로 = 앞 단계(407줄), 층 고정 주소(#floor=N) |
| 10 차트 | 해당 없음 | (입력 화면에 차트 없음) |

## 위반 표
| 번호 | 규칙 (범주·규칙명) | 파일:줄 | 지금 | 고칠 모양 (CSS·HTML 한 줄) | 심각도 | 크기 |
|---|---|---|---|---|---|---|
| U1 | 1 접근성·color-accessible-pairs (다크) | index.html:195, :38 | OS 다크 모드에서 [🔔 알림 받기] 글자 대비 **1.03:1** — 버튼 바탕이 브라우저 기본(밝음)인데 글자는 밝은 색 상속 → 안 보임 | `#notify button{background:var(--card);color:var(--ink);border:2px solid var(--line)}` | 높음 | S |
| U2 | 1 접근성·color-accessible-pairs (다크) | index.html:226 | 다크에서 [강도 타임라인 →] 링크 대비 1.89:1 (기본 파랑 링크색) | `#tlink{color:var(--acc);font-weight:700}` | 중간 | S |
| U3 | 2 터치·touch-target-size | index.html:226 | [강도 타임라인 →] 누를 곳 높이 **23 px** (장갑으로 못 누름) | `#tlink{display:inline-flex;align-items:center;min-height:48px}` | 중간 | S |
| U4 | 2 터치·touch-target-size · 5 readable-font-size | index.html:133 | 구역 카드 접힘 줄([판넬 조기 탈형 검토]·[판정 규칙 근거]) 높이 40 px, 글자 14 px | `.zcard .more summary{min-height:48px;font-size:16px}` | 중간 | S |
| U5 | 5 레이아웃·readable-font-size | index.html:84, :74, :103–107, :121, :164 | 14 px 이하 글자: 타일 센서 배지 12 px(84), 칩·종류 보조줄 14 px(74·121), 상태 흐름 막대 이름 14 px(103–107), 기록 꼬리표 14 px(164), 머리 '연결됨' 14 px — 햇빛·흔들림에서 못 읽음 | `.tile .sn i{font-size:14px}` · `.chip small,.kind small,.tag,.flow span{font-size:16px}` (배지는 2줄 허용) | 중간 | S |
| U6 | 6 글자·색·contrast-feedback | index.html:165 | 최근 입력 '서버 저장 ✓' 꼬리표 대비 4.08:1 (14 px 초록 글자 on 연초록) | `.tag.s{color:color-mix(in srgb,var(--ok) 75%,#000)}` | 낮음 | S |
| U7 | 2 터치·press-feedback | index.html (`:active` 규칙 없음) | 누를 때 눌림 표시가 없어 장갑 끼고 눌렸는지 모름 → 두 번 누름 | `button:active,.tile:active,.kind:active{filter:brightness(.85)}` | 중간 | S |
| U8 | 2 터치·haptic-feedback | index.html save()·showResult() (vibrate 없음) | 저장 성공·실패를 소리·진동 없이 화면으로만 알림 (소음·햇빛) | 성공 `navigator.vibrate?.(40)` · 실패 `navigator.vibrate?.([80,60,80])` (안드로이드만, iPhone 은 무시됨) | 낮음 | S |
| U9 | 8 입력·form-autosave · 9 이동·state-preservation | index.html:597–603 (renderForm 이 폼을 새로 그림) | 강도 3개를 넣다가 휴대폰 뒤로 제스처·[←]로 한 단계 나가면 다시 들어올 때 칸이 비어 있음 | 칸 input 마다 `sessionStorage['draft.'+zone+'.'+kind]` 에 저장·복원, 저장 성공 때 지움 | 높음 | M |
| U10 | 8 입력·required-indicators | index.html:579–581, 공시체 폼 607–630 | 꼭 넣어야 할 칸(강도 3개·양생·입력자)이 표시 없음 — 저장 눌러야 알게 됨 | `<label>강도 <b class="req" aria-hidden="true">*</b><span class="sr">필수</span></label>` + 폼 위 "* 꼭 넣을 칸" | 낮음 | S |
| U11 | 8 입력·destructive-emphasis | index.html:1045, :1054 | '보내지 못함' 기록의 [이 기록 지우기]가 [고쳐서 다시 보내기]와 같은 파란 밑줄 글자로 나란히 — 되살릴 수 없는 삭제 | `.lnk.del{color:var(--err);margin-left:24px}` + 확인 창 문구에 기록 요약 | 중간 | S |
| U12 | 8 입력·inline-validation | 공시체 폼 칸 (onblur 없음) | 범위 밖 값(예 250 MPa)은 저장을 눌러야 알림. 평균 줄(#avg)만 바로 바뀜 | 숫자 칸 `onblur` 에 기존 need() 검사 재사용해 칸 아래 오류 한 줄 | 낮음 | S |
| U13 | 1 접근성·dragging-alternative · keyboard-nav (= 1차 #6 수정) | index.html:788–835 (renderShorePlan) | 문제 동바리는 평면을 눌러야만 고름 — 손가락 반경 24 px 로 누르기는 괜찮지만(1차 #6 의 '14 px 점'은 센서 점이라 정정), 화면 읽기·키보드·행/열 입력 대안이 없음 | 평면 아래 "R01C01 처럼 넣기" 칸 + 고른 동바리 칩 목록(누르면 뺌) | 중간 | M |
| U14 | 1 접근성·reduced-motion | index.html:174 | 접힘 화살표 transition 이 prefers-reduced-motion 을 안 따름 (움직임 작음) | `@media (prefers-reduced-motion:reduce){*{transition:none!important}}` | 낮음 | S |
| U15 | 3 성능·offline-support (= 1차 #8) | sw.js (fetch 처리 없음) | 지하·무신호에서 화면을 닫으면 다시 못 엶 (입력 대기열은 있음) | 서비스 워커 fetch: index.html·마지막 /api/model 캐시 (Vercel), Wi-Fi 서버는 해당 없음 | 높음 (1차 집계) | M |
| U16 | 5 레이아웃·focus-not-obscured / fixed-element-offset | index.html:154 (.bar 고정), html 에 scroll-padding 없음 | 아래쪽 칸(메모·입력자)에 초점이 갈 때 화면 키보드 + 하단 [저장] 막대에 가려질 수 있음 | `html{scroll-padding-bottom:calc(110px + env(safe-area-inset-bottom))}` | 낮음 | S |
| U17 | 6 글자·number-tabular | index.html:142 (숫자 칸), #avg | 강도·평균 숫자가 비례 폭 숫자 — 바뀔 때 흔들림 | `input[inputmode=decimal],.avg{font-variant-numeric:tabular-nums}` | 낮음 | S |
| U18 | 9 이동·multi-step-progress | index.html 단계(구역 → 종류 → 폼) | 지금 몇 단계인지 표시 없음 (제목 "공시체 — 9F-A2"만) | 폼 제목 위 `1 구역 › 2 종류 › 3 입력` 한 줄 (현재 굵게) | 낮음 | S |
| U19 | 4 스타일·no-emoji-icons | index.html 머리 '☀'·'🔔'·'⚠'·'▲ 북' | 그림 글자를 아이콘으로 씀 — 기기마다 모양·색이 다름 (글자 라벨이 같이 있어 뜻 전달은 됨) | 인라인 SVG 4개로 교체, 또는 그대로 두고 글자 라벨 유지 (현장 영향 작음) | 낮음 | S |

## 1차 묶음에 넣을 것 (10개 이내)
1. **U1** 다크 모드 [알림 받기] 안 보임 — 한 줄 CSS
2. **U2·U3** [강도 타임라인] 링크 대비·높이 — 한 줄 CSS
3. **U9** 뒤로 가도 입력값 유지 (임시 저장)
4. **U7** 누름 표시 (`:active`)
5. **U5** 14 px 이하 글자 → 16 px (배지 14 px)
6. **U4** 구역 카드 접힘 줄 48 px·16 px
7. **U11** [이 기록 지우기] 빨강·떨어뜨리기
8. **U16** 하단 막대에 가리는 초점 (scroll-padding)
9. **U6** '서버 저장 ✓' 대비
10. **U8** 저장 진동 (안드로이드)

U13(동바리 ID 입력)·U15(오프라인 열기, 1차 #8)는 M 크기라 1차 점검의 3차 묶음과 같이.

검증 한계: 실기 휴대폰·장갑·실제 햇빛·가로 화면·iOS 큰 글자(동적 글꼴)·화면 읽기 프로그램은 시험 안 함. 다크 모드는 OS 다크 CSS 를 강제로 적용해 계산. 대비는 글자 바로 뒤 바탕색 기준(그림·그라데이션 바탕은 제외).
U1·U2·U3 은 v6.2 때 이 세션이 넣은 부분([알림 받기]·타임라인 링크)의 결함입니다.

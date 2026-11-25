# 통합 HTML 뷰 점검 — Vercel Web Interface Guidelines (11-11, 고정본 tools/ui_skills/web-design-guidelines)

범위: 서류 6종(공통 report_style), 웹 공유판(web/onsite_4d.html), Vercel 보기 화면(view·supervisor·timeline), 도움말. 현장 입력 index.html 은 field_input_uiux 쪽.
방법: 코드 읽기 + 데모 데이터로 실제 서류 출력 → 헤드리스 A4 PDF 쪽수 실측(임시 파일 삭제). 층 표지(C#)는 코드만 읽음.

## 가장 큰 문제 (실측, 총괄이 report_style.py:4 확인)
공통 스타일 `body{line-height:1.5}` 가 @media screen 밖 → 인쇄에도 적용 → 검측요청서 4장 → 5장, 3쪽엔 바닥글 "2 / 4"만. **88차 회귀**.

## 분류별 위반 수
접근성 16 · 포커스 2 · 입력 4 · 애니메이션 0 · 글꼴·표기 4 · 내용 처리 9 · 이미지 0 · 성능 1 · 이동·상태 2 · 터치 3 · 안전 영역 3 · 다크·테마 3 · 날짜·숫자 4 · hover 3 · 문구 4 · 금지 패턴 1

## 파일별 (심각한 것부터)
- report_style.py:4 (+ReportStyle.cs) — line-height·th/td 테두리·th 바탕이 인쇄에도 → @media screen 안으로
- approval.py:213 — print-color-adjust 없음(입력란 옅은 파랑 인쇄 안 됨) · table.sign break-inside 없음 · :311 근거표 <thead> 없음 · :263 제목 div → h1 · 직선 따옴표 · tabular-nums 없음
- staged.py:297 — 조기 탈형 검토서 서명표가 1·2쪽으로 갈림 (A4 1장 설명과 다름)
- FloorSign.cs:132-139 — 구역 많은 층(약 9개↑, 미검증)은 QR 이 2쪽으로 밀림 · :141 층 이름 h1
- timeline.html — .tile overflow:hidden 으로 "신고: 뺐음" 잘림 · 60–80% 칸 흰 글자 대비 2.4:1 · 슬라이더 valuetext·±1일 없음 · 불러오는 중/다시 시도 없음 · 1분마다 칩 다시 그려 포커스 사라짐 · 빈 상태를 빨강 오류로 · safe-area·theme-color
- view.html — Vercel(https)에서도 "같은 Wi-Fi 확인" 문구 · 오류에 다음 할 일 없음 · 1분마다 목록 통째 교체(포커스·aria-live 반복) · :focus-visible·touch-action·color-scheme 없음 · "보내는 중…" 없음 · 날짜 표기가 인쇄 표지와 다름
- supervisor.html — 3개 체크 전 버튼 비활성 이유 안내 없음 · 불러오는 중/다시 시도 없음 · 전송 뒤 실패 시 "보내는 중…"에서 멈춤 · synced_at 없으면 오류
- web/onsite_4d.html — file 입력 label 없음 · valuetext·role 없음 · td 버튼 역할 role 없음, 정보가 title(hover)에만 · h1→h3 · 날짜·구역이 주소에 안 남음 · 기호 aria-hidden
- package.py:91 — @page 없음(Letter 로 인쇄, 실측) · SHA 칸 줄바꿈 안 됨
- quality.py:89 — viewport·@page 없음, 글꼴 다름, 숫자 정렬 없음
- slab_record.py:89 — <p> 안 <ul> · h1 없음 · 긴 경로 줄바꿈 안 됨
- docs/sample_request_20261231.html — 예전 서식 그대로 → 다시 만들거나 지움
- docs/help/index.html — 본문 바로가기 없음, 좁은 화면에서 목차 60개가 먼저 · quickstart.html ✓

## 1차 묶음 상위 10
1 공통 스타일 화면 한정(py·C# 바이트 대조) S · 2 서명표 break-inside·근거표 thead·검토서 1장 S · 3 print-color-adjust 또는 테두리 S · 4 서류 자체 시험에 인쇄 쪽수 검사 M · 5 타임라인 잘림·대비 S · 6 오류 문구·불러오는 중·다시 시도(view·supervisor·timeline) S · 7 감리 버튼 빠진 항목 안내·전송 상태 되돌림 S · 8 1분 새로 고침 부분 교체 M · 9 Vercel 세 화면 공통 CSS(safe-area·theme-color·focus-visible·touch-action·color-scheme) S · 10 웹 공유판 접근성 + 주소에 날짜·구역 M
그 밖 S: package·quality @page A4, 예전 샘플 정리. 층 표지 쪽 넘침은 구역 수 늘린 실측 먼저 M.

## 규칙이 다루지 않는 A4 인쇄 문제
화면 스타일 인쇄 누출 · 정해진 쪽수·"n / N" 바닥글 검사(PDF 쪽수로만) · break-inside/thead/orphans · 인쇄 배경 꺼짐(색으로 뜻 전하는 칸) · @page 미지정 시 Letter. QR 여백(quiet zone)은 미확인.

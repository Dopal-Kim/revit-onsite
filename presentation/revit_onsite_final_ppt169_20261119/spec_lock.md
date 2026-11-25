<!-- ppt-master-schema: spec-lock/v1 -->
# Execution Lock

## canvas
- viewBox: 0 0 1280 720
- format: PPT 16:9

## communication
- primary_language: ko-KR
- audience: 건축공학시스템설계 담당 교수님과 수강생 (콘크리트·거푸집 공사, Revit 기본 지식)
- objective: 동바리 해체 판단의 위험을 보이고 Revit On-Site가 기준·실측으로 구역마다 판정하고 근거를 남기는 방식을 설명·시연으로 입증해, 청중이 판정 흐름을 설명하고 확실한 효과와 조건부 효과를 구분해 평가하게 한다
- core_message: 날짜가 아닌 기준으로 판단하고, 그 근거를 기록한다 — 판정은 프로그램이, 결정은 감리가 한다
- consumption_mode: balanced

## mode
- mode: custom
- mode_references: narrative, instructional
- mode_behavior: 문제 → 긴장 → 해결의 narrative 흐름으로 I–III장을, 판정 흐름·시스템 구성·시연은 instructional 단계 순서로, 기대효과는 확실성 순서로 정직하게 나열한 뒤 한계·결론으로 닫는다. 제목은 판단 문장, 부제는 한 줄 요지.

## visual_style
- visual_style: custom
- visual_style_references: blueprint, swiss-minimal
- visual_style_behavior: 밝은 도면 용지 바탕에 얇은 이중 테두리와 오른쪽 아래 도면 표제란(프로젝트명·시트 번호·장 이름)을 매 장 두는 도면 시트 언어. 격자 정렬, 모서리 4 px, 장식 대신 치수선·지시선·단면 해치. 슬래브=굵은 남색 막대, 동바리=세로 선, 새로 타설한 층=안전 주황, 철거된 동바리=점선을 장 전체에서 같은 뜻으로 반복. 숫자·라벨·시트 번호는 고정폭, 제목은 굵은 산세리프. 그림자 없음, 면 채움은 옅은 회청색 한 단계. 표지·목차·결론·Q&A는 진한 남색 표지 시트.

## colors
- background: #F6F7F5
- secondary_bg: #E8EDF2
- primary: #1D3557
- accent: #E07A1F
- secondary_accent: #2E6DB4
- body_text: #1E2329
- secondary_text: #5B6673
- divider: #C5CDD6
- negative: #B23A2E

## typography
- font_family: Malgun Gothic, Segoe UI, sans-serif
- title_family: Malgun Gothic, Segoe UI, sans-serif
- body_family: Malgun Gothic, Segoe UI, sans-serif
- data_family: Consolas, Malgun Gothic, monospace
- body: 20
- title: 34
- subtitle: 22
- lead: 24
- annotation: 15
- footnote: 12
- display: 60
- cover_title: 72
- data: 15

## icons
- library: tabler-outline
- stroke_width: 2
- inventory: tabler-outline/alert-triangle, tabler-outline/clipboard-check, tabler-outline/ruler-measure, tabler-outline/building, tabler-outline/stack-2, tabler-outline/device-mobile, tabler-outline/qrcode, tabler-outline/cloud, tabler-outline/database, tabler-outline/file-certificate, tabler-outline/shield-check, tabler-outline/calendar, tabler-outline/users, tabler-outline/eye, tabler-outline/lock, tabler-outline/bolt, tabler-outline/search, tabler-outline/settings, tabler-outline/tool, tabler-outline/home, tabler-outline/world, tabler-outline/file-text, tabler-outline/arrow-right, tabler-outline/check, tabler-outline/x, tabler-outline/rosette-discount-check, tabler-outline/refresh, tabler-outline/cube, tabler-outline/chart-bar, tabler-outline/temperature, tabler-outline/player-play, tabler-outline/scale, tabler-outline/coin, tabler-outline/book

## images
- crest: images/khu_crest.png | source=user | crop=no-crop
- dept: images/dept_logo.png | source=user | crop=no-crop
- demo: images/demo_revit_screen.jpg | source=user | crop=no-crop
- request: images/inspection_request.jpg | source=user | crop=no-crop

## page_rhythm
- P01: anchor
- P02: anchor
- P03: dense
- P04: dense
- P05: dense
- P06: dense
- P07: dense
- P08: dense
- P09: dense
- P10: dense
- P11: dense
- P12: dense
- P13: dense
- P14: dense
- P15: breathing
- P16: dense
- P17: dense
- P18: dense
- P19: dense
- P20: dense
- P21: dense
- P22: dense
- P23: anchor
- P24: dense
- P25: dense
- P26: anchor

## page_visualizations
- P06: table/record_table
- P11: chart/column_chart
- P13: table/comparison_matrix
- P20: table/metric_table

## pptx_structure
- mode: flat

## forbidden
- `mask`, `<style>`, `class`, external CSS, `<foreignObject>`, `textPath`, `@font-face`, `<animate*>`, `<set>`, `<script>` / event attributes, `<iframe>`
- HTML named entities in text; write typography as raw Unicode and escape XML reserved characters

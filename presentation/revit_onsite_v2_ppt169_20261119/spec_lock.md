<!-- ppt-master-schema: spec-lock/v1 -->
# Execution Lock

## canvas
- viewBox: 0 0 1280 720
- format: PPT 16:9

## communication
- primary_language: ko-KR
- audience: 건축공학시스템설계 담당 교수님과 수강생 (콘크리트·거푸집 공사, Revit 기본 지식)
- objective: 동바리 해체 판단의 위험을 보이고 Revit On-Site가 기준·실측으로 구역마다 판정하고 근거를 남기는 방식을 실제 화면과 시연으로 입증해, 청중이 판정 흐름을 설명하고 확실한 효과와 조건부 효과를 구분해 평가하게 한다
- core_message: 날짜가 아닌 기준으로 판단하고, 그 근거를 기록한다 — 판정은 프로그램이, 결정은 감리가 한다
- consumption_mode: balanced

## mode
- mode: custom
- mode_references: narrative, showcase
- mode_behavior: 질문 → 위험의 뿌리 → 흩어진 기준의 narrative 긴장에서 Revit On-Site 본체로 넘어가면 showcase처럼 실제 화면 한 장이 페이지를 차지하고 번호 핀으로 근거를 짚는다. 효과는 확실성 순서, 한계는 정직하게. 제목은 판단 문장, 한 장에 주장 하나.

## visual_style
- visual_style: custom
- visual_style_references: swiss-minimal, editorial
- visual_style_behavior: 넓은 흰 여백과 엄격한 좌측 정렬 격자. 큰 굵은 판단 문장 제목, 왼쪽 위 작은 파란 장 태그. 실제 화면은 얇은 회색 테두리의 증거판으로 크게 놓고 파란 번호 핀과 가는 지시선으로 근거를 짚어 옆 주석과 번호로 짝짓는다. 판정 상태는 애드인과 같은 의미 색(요청 가능 초록 · 존치 주황 · 시험 필요 노랑)으로만. 장식·그림자 없음, 모서리 6 px, 면 채움은 옅은 청회색 한 단계. 숫자는 Segoe UI 굵게 크게. 표지·장 전환·결론은 진한 남색 면.

## colors
- background: #FFFFFF
- secondary_bg: #EEF3F9
- primary: #1B2A41
- accent: #1F5FA8
- secondary_accent: #E8613C
- body_text: #1E2633
- secondary_text: #5E6B7A
- divider: #D5DCE4
- positive: #5FA83A
- warning: #E9B93A

## typography
- font_family: Malgun Gothic, Segoe UI, sans-serif
- title_family: Malgun Gothic, Segoe UI, sans-serif
- body_family: Malgun Gothic, Segoe UI, sans-serif
- data_family: Segoe UI, Malgun Gothic, sans-serif
- body: 20
- title: 36
- subtitle: 22
- lead: 24
- annotation: 15
- footnote: 12
- display: 64
- cover_title: 80
- data: 28

## icons
- library: tabler-outline
- stroke_width: 2
- inventory: tabler-outline/device-mobile, tabler-outline/qrcode, tabler-outline/cloud, tabler-outline/cube, tabler-outline/file-text, tabler-outline/lock, tabler-outline/database, tabler-outline/alert-triangle, tabler-outline/shield-check, tabler-outline/file-certificate, tabler-outline/coin, tabler-outline/calendar, tabler-outline/check, tabler-outline/player-play, tabler-outline/users, tabler-outline/world, tabler-outline/home, tabler-outline/settings, tabler-outline/tool, tabler-outline/rosette-discount-check, tabler-outline/ruler-measure, tabler-outline/temperature, tabler-outline/clipboard-check, tabler-outline/building

## images
- dash: images/shot_14_view.png | source=user | crop=adaptive
- ribbon: images/shot_01_ribbon.png | source=user | crop=no-crop
- evidence: images/shot_15_lines.png | source=user | crop=no-crop
- alerts: images/shot_20_alerts.png | source=user | crop=no-crop
- floors3: images/shot_08.png | source=user | crop=no-crop
- install: images/shot_09.png | source=user | crop=no-crop
- sign: images/shot_27.png | source=user | crop=no-crop
- timeline: images/shot_20_view.png | source=user | crop=adaptive
- approve: images/shot_17.png | source=user | crop=no-crop
- request: images/shot_18_top.png | source=user | crop=no-crop
- cover: images/cover_model.png | source=user | crop=no-crop
- web4d: images/shot_21.png | source=user | crop=no-crop

## page_rhythm
- P01: anchor
- P02: anchor
- P03: breathing
- P04: dense
- P05: dense
- P06: dense
- P07: dense
- P08: anchor
- P09: dense
- P10: dense
- P11: dense
- P12: dense
- P13: dense
- P14: dense
- P15: dense
- P16: dense
- P17: dense
- P18: dense
- P19: breathing
- P20: dense
- P21: dense
- P22: dense
- P23: dense
- P24: dense
- P25: dense
- P26: dense
- P27: anchor
- P28: anchor
- P29: dense
- P30: dense

## page_visualizations
- P07: chart/column_chart
- P20: table/record_table
- P24: table/metric_table

## pptx_structure
- mode: flat

## forbidden
- `mask`, `<style>`, `class`, external CSS, `<foreignObject>`, `textPath`, `@font-face`, `<animate*>`, `<set>`, `<script>` / event attributes, `<iframe>`
- HTML named entities in text; write typography as raw Unicode and escape XML reserved characters

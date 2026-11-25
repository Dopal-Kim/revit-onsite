# Revit On-Site 웹 공유판 — JSON 데이터 계약 (v2.0, 강도 예측 타임라인)

`web/onsite_4d.html` 이 읽는 JSON. 만드는 곳 두 군데, 형식은 하나:
- **C# 애드인** 리본 `보고·공유 › 웹 공유판` (`CmdExportWeb`) — 실제 Revit 형상 + 데이터 폴더 기록으로 강도 예측·판정
- **Python** `shoring/export_4d.py` — 데모 모델(정답 대조용). 벽·계단·창호는 지어낸 것 (`synthetic: true`)

v2.0 (v6 3단계, 2026-11-08 사용자 확정 — docs/redesign_v6.md §4): 공정 계획·시공 순서 애니메이션을 뺐다. 날짜 막대 = 층별 추정 강도 곡선 위 점(참고), 그날까지 현장양생 공시체 실측 점,
기준선 5·14·fck. 3D 구역 = 그날 추정 강도 색, 동바리는 그대로(해체 애니메이션 없음). 판정은 기록(공시체·3 m 안 센서)만으로 첫 타설일 ~ 마지막 기록일.
1.x 파일(`activities`·`strength`·`floor_summary`·`summary`·`warnings`·`cycle`)은 v2.0 뷰어가 열지 않고 다시 내보내라고 안내한다.

규칙
- 인코딩 UTF-8, 날짜 `YYYY-MM-DD`, 시각 `YYYY-MM-DDTHH:MM` (현지 시각, 시간대 없음).
- 길이 **m** (Revit 내부 피트 → `UnitUtils.ConvertFromInternalUnits(v, UnitTypeId.Meters)`), 강도 MPa.
- 좌표 원점 = 건물 남서 모서리(그리드 X1·Y1 교점) 권장, x = 동, y = 북, z = 위. 프로젝트 기준점을 쓰면 `project.origin` 에 적는다.
- 모르는 필드는 뷰어가 무시한다. 필수 표시 **(필수)**, 나머지는 없으면 뷰어가 생략.
- HTML 에 넣을 때는 `<script type="application/json" id="data">…</script>` 안에, 문자열 `</` 를 `<\/` 로 바꿔 넣는다.
- 결론 문구는 **"해체 승인 요청 가능" / "존치"** 까지. "해체하세요", "승인됨" 같은 지시·승인 문구를 어떤 필드에도 쓰지 않는다 (export_4d.py `check()` 가 검사).

## 최상위

| 필드 | 형 | 설명 |
|---|---|---|
| `schema` (필수) | `"revit-onsite-4d"` | |
| `version` (필수) | `"2.0"` | 깨지는 변경이면 주 번호를 올린다 (2.0 = 공정 → 강도 예측 타임라인) |
| `project` (필수) | object | 아래 |
| `levels` (필수) | array | 층 |
| `thresholds` | object | 기준 강도 |
| `elements` (필수) | array | 부재 형상 |
| `strength_timeline` (필수) | object | 층·구역별 추정 강도 곡선(참고) · 현장양생 공시체 점 · 기준선 — 아래 |
| `decisions` | array | 날짜별 구역 판정 (기록만, 첫 타설 ~ 마지막 기록일) |
| `assumptions` | array | "이 화면에서 가정한 부분" |
| `measurements` | array | 현장 실측값 (센서·공시체…) — 표식·그래프 (docs/field_input_spec.md §4) |
| `approvals`, `evidence` | array | 승인 기록 (상태 6·7·8) · 근거 원장 ('근거 단계' 색) |

## `project`

| 필드 | 예 | 설명 |
|---|---|---|
| `name` (필수) | `"Revit On-Site 데모 — RC 공동주택 15층"` | 화면 제목 |
| `source` | `"revit"` / `"demo-python"` | |
| `model_file` | `"shoring_demo.rvt"` | `Document.PathName` 파일명 |
| `generated_at` | `"2026-11-04T09:30:00"` | |
| `units` | `"m"` | 항상 m |
| `origin` | 문장 | 좌표 원점 설명 |
| `fck_mpa`, `min_levels`, `cure_min_c` | | 프로젝트 정보 PRJ_ 값 |
| `date_range` (필수) | `["2026-11-26","2027-02-11"]` | 슬라이더 범위 = `strength_timeline` start ~ end (첫 타설 ~ 마지막 타설 + 56일 [가정]) |
| `judged_until` | `"2026-12-31"` / null | 마지막 기록일 (타설·공시체·센서) — `decisions` 는 이 날까지만 |
| `zone_order` | `["A1","B1","C1","A2","B2","C2"]` | 격자 열 순서 (구역 ID 뒤쪽 접미사). 없으면 ID 정렬 |

## `levels[]`

| 필드 | 설명 |
|---|---|
| `floor` (필수) | 정수 층 번호. **층 k = k층 슬래브(윗면 Lk)**, 그 동바리·벽·창호는 L(k−1)–Lk 사이 |
| `name` | 화면 이름 (`"9층"`) |
| `z` (필수) | 슬래브 윗면 고도 m (Revit: 레벨 `Elevation`, 레벨 고도순으로 층 번호 매김) |
| `slab_thickness` | m |

## `thresholds`
`side_form_mpa` (5, 측면 거푸집), `remove_mpa` (= fck, 다층 동바리 해체), `support_slab_mpa` (14). 양생 진행률 = 강도 ÷ 해당 기준.

## `elements[]`

| 필드 | 형 | 설명 |
|---|---|---|
| `id` (필수) | string | 고유. Revit 은 `UniqueId` 권장 (구역은 `SHR_ZoneID` 를 써도 됨) |
| `category` (필수) | `slab` \| `wall` \| `stair` \| `window` \| `shore_zone` \| `zone` | |
| `floor` (필수) | int | 위 층 정의. 벽·계단·창호·동바리는 **그 위 슬래브의 층** |
| `zone` (필수, slab 은 null 가능) | string | 구역 ID (`"9F-A1"`) — 공정을 찾는 키. 부재 평면 무게중심이 든 구역 (경계 = x0 ≤ x < x1) |
| `name` | string | 표시용 (`"벽 200"`, Revit 타입 이름) |
| `geometry` (필수) | object | `footprint`: 평면 다각형 `[[x,y],…]` m, 외곽 루프 하나, 반시계·닫지 않음(첫 점 반복 X). `z0`, `z1`: 아래·위 고도 m. **형상 = footprint 를 z0→z1 로 밀어 올린 기둥** |
| `synthetic` (필수) | bool | 모델에 없는 부재를 지어 넣었으면 true → 화면에 "가상" 표시, 가정 목록에 이유 |
| `spacing_m` | `[1.2,1.2]` | shore_zone 만: 동바리 간격 (뷰어가 기둥 선을 이 간격으로 그림) |

카테고리별 기대
- `zone`: SHR 구역 상자 (DirectShape). z0 = Lk − 두께, z1 = Lk. 구역이 있는 층은 뷰어가 `slab` 대신 `zone` 을 그린다 (slab 은 외곽선만).
- `shore_zone`: 구역당 상자 하나 = 그 구역 동바리 묶음. footprint = 구역 (벽과 겹치지 않게 0.15 m 안쪽), z0 = L(k−1) 윗면, z1 = Lk − 두께. 416본을 다 내보내지 않는다 — 뷰어가 `spacing_m` 간격 기둥으로 그리고, 선택 밖·먼 층은 반투명 상자로 묶는다 (표시 설정 '동바리').
- `wall`: Revit 벽 → `LocationCurve` + 두께로 사각형, 또는 아래 면 외곽 루프. 개구부는 벽을 나눠서 표현 (창대 아래 벽은 별도 wall, z1 = 창대 높이).
- `stair`: 평면 범위 + z0/z1. 뷰어가 긴 변 방향으로 한 계단참 없는 단순 계단으로 그린다.
- `window`: 개구부 안 얇은 상자 (벽 두께 가운데 0.1 m).
- 곡면·구멍(슬래브 개구부) 은 v1 에서 외곽 루프만. 필요하면 v2 에 `holes` 추가.

C# 추출 방법: `FilteredElementCollector` (Floors / Walls / Stairs / Windows / Generic Models `SHR_GenBy`) → `Solid` 의 가장 아래 평면 `PlanarFace.GetEdgesAsCurveLoops()[0]` 를 테셀레이트 → footprint, `BoundingBoxXYZ.Min.Z/Max.Z` → z0/z1. IFC 변환 모델도 같은 방법.

## `strength_timeline`
`shoring/strength_timeline.py` (애드인 `Engine.StrengthTimeline`, 바이트 대조) 결과. 추정은 판정의 예측 해체일과 같은 식(engine.predict — 기온 = weather.csv 또는 평년, 양생 하한, 층 높이 보정)이고 **참고 (판정에 안 씀)**.

| 필드 | 설명 |
|---|---|
| `start`, `end` (필수) | 날짜. `mpa[i]` = start + i 일의 값 (끝 포함) |
| `floors[]` (필수) | `{floor, label, fck, mpa[]}` — 층 곡선 = 그날까지 타설된 그 층 구역 중 가장 낮은 값 (보수 측), 타설 전 = null. 소수 1자리 |
| `zones[]` | `{zone, mpa[]}` — 구역 곡선 (3D 구역 색) |
| `cylinders[]` | `{date, zone, floor, mpa, curing}` — 현장양생 공시체(현장봉함·현장수중·현장기건) 세 개 평균 (실측) |
| `lines[]` | `{name, mpa}` — 기준선: 측면 거푸집 5 · 지지 슬래브 14 · fck (다층 동바리 해체) [확정] |

뷰어 색: 구역 = 추정 ÷ 그 층 fck 40·60·80·100 % 구간 (애드인 [표시: 강도]와 같음 [가정]), 타설 전 = 흐린 회색. 동바리 묶음(`shore_zone`)은 늘 서 있음.

## `decisions[]`
날짜별 구역 판정 = evaluate.py 출력 — 첫 타설일 ~ `judged_until` 매일 09:00, 그날까지 기록된 타설·공시체·센서만 (계획 타설 없음). 뷰어는 슬라이더 날짜 이하 중 가장 최근 것을 "판정 근거" 로, `judged_until` 뒤는 "판정 없음" 으로.

| 필드 | 설명 |
|---|---|
| `at` (필수) | 판정 시각 |
| `zone` (필수) | 구역 ID |
| `decision` (필수) | `해체 승인 요청 가능` \| `공시체 시험 필요` \| `존치` \| `존치 (공시체 미달)` \| `존치 (구역 최소 강도 미달)` |
| `reason` (필수) | 사유 문장 |
| `predicted_due` | 기준 강도 예측 도달일 |
| `f_cyl` | 공시체 평균 MPa 또는 null |
| `f_est_min` | 구역 최소 추정 강도 MPa |
| `est_source` | `센서 6개 IDW` / `기상 예측(센서 없음) — 평년 기온 [가정], 양생 하한 5 °C [가정]` (기온 출처: `기상 기록 [입력]` / `기상 기록 n일 [입력]·평년 기온 m일 [가정]` / `평년 기온 [가정]`, 감사 A-26) |
| `level_gate` | `OK` / `NG` (타설층 포함 3개층 존치) |
| `age_days` | 재령 |
| `f_est_xy` | `[x, y]` 구역 최소 추정 강도 지점 (evaluate.py `f_est_xy`). 센서 표식에서 "이 센서가 판정을 좌우" 를 가리는 데 씀. 센서 없으면 null |

## `assumptions[]`
`{id, text, basis}` — basis = `[가정]`/`[입력]`/`[확정]`. 지어낸 부재, 팀 가정 기간, 빠진 데이터(센서·공시체가 없는 층)를 빠짐없이.

## `measurements[]` (v1.1)
현장 실측값 한 줄 = 측정 1회 (docs/field_input_spec.md §2·§4). 뷰어는 `kind`+`point_id` 로 묶어 지점마다 시계열을 만들고, **슬라이더 시각 이하의 측정만** 보인다.

| 필드 | 형 | 설명 |
|---|---|---|
| `kind` (필수) | `sensor_temp` \| `sensor_strength` \| `cylinder` \| `rebound` | 온도 센서 / 무선 강도 센서 / 현장양생 공시체 / 반발경도(조회만, 판정 미사용) |
| `point_id` (필수) | string | 센서 ID (`"S4"`), 공시체는 `"CYL-<구역>"` |
| `floor` (필수) | int | 층 (levels 와 같은 번호) |
| `zone` (필수) | string | 지점이 든 구역 ID (경계 x0 ≤ x < x1) |
| `x`, `y` (필수) | m | 평면 위치. 공시체는 구역 가운데 (깃발 표식) |
| `timestamp` (필수) | `YYYY-MM-DDTHH:MM` | 측정 시각. 공시체는 시험일 `T09:00` (판정 시각과 같은 기준) |
| `value` (필수) | number | 온도 °C / 강도 MPa (공시체 = f1·f2·f3 평균) / R |
| `unit` (필수) | `"°C"` \| `"MPa"` \| `"R"` | |
| `entered_by` | string | 입력자 (CSV 의 `entered_by` 열, 없으면 출처 문구) |
| `f`, `curing` | | 공시체만: `[f1,f2,f3]`, 양생 방법 |

- 표식: 센서 = 슬래브 윗면 원, 공시체 = 깃발. 누르면 그래프·마지막 값·입력자, 그리고 "판정에 쓰임" 여부:
  - 공시체: 그날 `decisions[].f_cyl` 이 이 값이면 "판정에 쓰임(기준 근거)".
  - 센서: 그날 같은 층 판정의 `est_source` 가 `센서…` 면 "판정에 쓰임", `f_est_xy` 에 가장 가까운 센서면 "이 센서가 판정을 좌우" + 해당 구역.
  - 반발경도: 항상 "판정 미사용".
- 크기: 온도 기록은 6시간 간격으로 줄여 넣는다 (판정 계산은 원본 1시간 기록). 데모 858줄 ≈ 160 KB.

## C# 내보내기가 만들어야 하는 집합 (v2.0)
`schema`, `version: "2.0"`, `project.{name, date_range, judged_until, fck_mpa, min_levels, cure_min_c, zone_order}`, `levels[]`, `thresholds`, `elements[]`, `strength_timeline`, `decisions[]`, `measurements[]`, `approvals[]`, `evidence[]`, `assumptions[]`.
`RevitOnSite.WebExport.Check` 가 export_4d.py 데모 JSON 과 같은지 대조 (`assumptions` 문장은 데이터 폴더에 맞춰 달라도 됨, 실측값 `entered_by` 기본 이름만 다름).

# 현장 실측값 입력 — 데이터 계약 (Revit On-Site, 2026-11-04 사용자 요구)

사용자 원문 요지: 엔지니어가 **휴대폰으로 부재 지점의 실측값을 입력** → 그 값으로 **해체 가능 여부 판단** → **4D 에서 실측값 조회**. 공시체 시험 + **센서 측정의 이점**을 살릴 것. 입력은 편해야 함.

## 1. 흐름
```
휴대폰 브라우저 ──(같은 Wi-Fi, QR 코드 URL + 토큰)──▶ 현장 입력 서버 ──▶ 데이터 폴더 CSV 에 한 줄 추가
   web/field/index.html                    (애드인 내장 TcpListener,                     │
   (오프라인이면 기기에 쌓아 두고            시험용은 tools/field_server.py)            ▼
    연결되면 한꺼번에 보냄)                                         evaluate(판정) 재계산 → Revit 매개변수·4D 갱신
```
- 서버 주소: `http://<현장 PC IP>:8765/?t=<토큰>`. 토큰은 서버를 켤 때마다 새로 만들고, 토큰 없는 요청은 거절(403).
- 같은 LAN 안에서만. 인터넷·클라우드 서비스 쓰지 않음.
- Windows 방화벽이 처음 한 번 허용을 묻는다 → 사용자가 "개인 네트워크 허용"을 눌러야 함(프로그램이 대신 누르지 않음).

## 2. 측정 종류와 저장 파일 (데이터 폴더 = PRJ_CSVFolder)
| 종류 `kind` | 입력 값 | 저장 파일 (기존 형식 유지) | 판정에서의 역할 |
|---|---|---|---|
| `cylinder` 현장양생 공시체 | 구역, 시험일, 양생(현장봉함·현장수중 등), f1·f2·f3 MPa | `cylinders.csv`: zone_id,test_date,curing,f1,f2,f3 | **기준 근거** (없으면 승인 요청 불가) |
| `sensor_temp` 온도(성숙도) 센서 | 센서 ID, 층, x·y(m), 시각, 온도 °C (여러 줄 한 번에 붙여넣기 가능) | `sensors.csv`: sensor_id,floor,x,y,timestamp,temp_c | 위치 근거: 등가재령 → 강도 → 구역 최소 (IDW) |
| `sensor_strength` 무선 강도 센서 | 센서 ID, 층, x·y(m), 시각, 추정 강도 MPa (장비가 성숙도로 계산한 값) | **새 파일** `sensor_strength.csv`: sensor_id,floor,x,y,timestamp,mpa | 위치 근거: 강도 값 자체를 IDW → 구역 최소 (온도 센서와 같은 자리, 같은 우선순위) [가정: 장비 보정곡선은 제조사 값] |
| `pour` 타설 (57차, **Revit [타설 등록] 창 전용** — 휴대폰 화면엔 없음) | 구역, 타설일, 타설 시각 HH:MM, 배합(엔진 배합 이름), fck 18–60 MPa | `pours.csv`: zone_id,pour_date,pour_time,mix,fck — 이미 타설 기록이 있는 구역·미래 날짜는 거부 | 강도 계산 시작 (해체 판정·공정). Revit [타설 등록]·[공시체 결과] 창은 `FieldServer.SubmitLocal` 로 HTTP 없이 같은 검사·저장 (본문 여럿 중 하나라도 틀리면 아무것도 안 씀, 기기 열 = Windows) |

- 모든 줄에 공통 열을 덧붙인다: `entered_by`(입력자 이름), `entered_at`(서버 수신 시각), `device`(브라우저 정보 요약), `note`(메모). 기존 열 순서는 그대로 두고 **뒤에** 붙인다(evaluate.py 는 열 이름으로 읽음).
- 판정 우선순위는 그대로 (CLAUDE.md): 규칙 게이트 → 공시체 → 센서 구역 최소. 센서 값이 있어도 공시체가 없으면 "공시체 시험 필요".
- 슈미트 해머(반발경도) 등 비파괴 시험은 이번 범위에서 **입력·조회만, 판정 미사용**(종류 `rebound`, 파일 `rebound.csv`: zone_id,x,y,test_date,r_value) [가정].

## 3. 서버 API (애드인과 시험용 Python 서버가 똑같이 따름)
| 요청 | 응답 |
|---|---|
| `GET /?t=` | `web/field/index.html` |
| `GET /api/model?t=` | `{"project":…, "floors":[{"floor":9,"name":"9F","zones":[{"zone_id":"9F-A1","x0":0,"y0":0,"x1":10,"y1":9,"status":5,"decision":"해체 승인 요청 가능","predicted_due":"2026-12-25","pour_date":"2026-11-26"}…]}…], "sensors":[{"sensor_id":"S1","floor":9,"x":15,"y":9,"last":{"timestamp":…,"temp_c":…}}…], "kinds":[…]}` |
| `POST /api/measure?t=` body JSON `{"kind":"cylinder", "zone_id":"9F-A1", "test_date":"2026-12-30", "curing":"현장봉함", "f1":24.4, "f2":24.8, "f3":25.2, "entered_by":"김OO", "note":"", "client_id":"<기기 고유 uuid>-<번호>"}` | `{"ok":true, "saved":"cylinders.csv", "zone":{…판정 다시 계산한 결과…}}` 또는 `{"ok":false, "error":"f2 가 숫자가 아닙니다"}` |
| `GET /api/recent?t=&n=20` | 최근 입력 목록 |
- `client_id` 로 중복 저장을 막는다(오프라인 큐를 다시 보낼 때).
- 값 검사(서버에서 다시): 강도 0–100 MPa, 온도 −30–90 °C, 날짜는 타설일 이후·내일 이전, 구역·센서가 모델에 있어야 함. 오류 문구는 한국어로 "무엇을 어떻게 고칠지".

## 4. 4D 조회 (Revit 4D 미리보기·웹 공유판 공통)
- 측정 지점은 그 층 슬래브 윗면 위의 표식(센서 = 원, 공시체 = 구역 가운데 깃발)으로 보이고, 선택하면 시간에 따른 값(그래프)과 마지막 값, 그 값이 판정에 쓰였는지를 보여 준다.
- 4D 날짜 슬라이더의 날짜까지 들어온 측정만 보인다(그날 이후 값은 아직 없던 것으로).
- 웹 공유판 JSON 에 `measurements[]`: `{kind, point_id, floor, zone, x, y, timestamp, value, unit, entered_by}` 추가 (web/onsite_4d_schema.md 갱신).

## 5. 구현 세부 — 기준본 `tools/field_server.py` (2026-11-04). C# 애드인 서버는 이것과 똑같이
화면은 `web/field/index.html` 한 파일 (외부 파일 없음). 시험 `python tools/test_field_server.py`.

### 5.1 위 계약에서 다듬은 것 (호환 유지)
- 공통 열에 **`client_id`** 추가 → 순서 `entered_by, entered_at, device, note, client_id`. 서버를 다시 켜도 파일의 client_id 로 중복을 막는다.
- 파일 쓰기: 파일이 없으면 머리글(기존 열 + 공통 열)과 함께 만든다 (UTF-8 BOM). 있는데 공통 열이 없으면 머리글 뒤에 붙여 **임시 파일에 다시 쓰고 바꿔 끼운다**(기존 줄은 빈칸). 이어 쓰기는 BOM 없이, 파일 끝에 줄바꿈이 없으면 먼저 넣는다. 기존 열이 빠진 파일은 저장 거절.
- 글자 칸(`entered_by` 20자 필수, `note` 200자)은 공백 정리, `= + - @` 로 시작하면 앞에 `'` (엑셀 수식 실행 방지). `device` = User-Agent 요약 (예 `Android Chrome`).
- "지금" = 서버 `--at` 값(데모) 또는 PC 시계. 날짜 검사·판정·화면 기본 날짜가 모두 이 값을 쓴다.

### 5.2 `GET /api/model`
`{ok, project, now:"YYYY-MM-DDTHH:MM", fixed_now, error, floors:[{floor, name, zones:[…]}], sensors:[…], kinds:[{kind,label,file,role}], curings:["현장봉함","현장수중","현장기건"], limits:{mpa:[0,100], temp_c:[-30,90], r_value:[10,70]}}`
- zone: `zone_id, x0,y0,x1,y1, status, decision, reason, predicted_due, pour_date, threshold, f_cyl, cyl_test_date, f_est_min, est_source, level_gate, age_days` (evaluate 행 값 그대로, 타설 기록 없으면 `status 0`, `reason "타설 기록 없음"`).
- `status` (ui_spec §6): 해체 승인 요청 가능 5, 공시체 시험 필요 4, 존치 → now 날짜 < predicted_due 이면 3, 아니면 9. 타설 없음 0.
- sensor: `{sensor_id, kind:"sensor_temp"|"sensor_strength", floor, x, y, count, last:{timestamp, temp_c|mpa}}`. 위치 = 그 ID 의 **첫 줄**.
- `error`: evaluate 가 입력 점검에서 멈추면 그 문구 (판정 없이 구역만 보냄).

### 5.3 `POST /api/measure` 본문
| kind | 필드 |
|---|---|
| `cylinder` | `zone_id, test_date, curing, f1, f2, f3` |
| `sensor_temp` | `sensor_id, floor, timestamp, temp_c` **또는** `readings:[{timestamp, temp_c}…]` (붙여넣기, 5000줄까지). 새 센서는 `x, y` 필수, 기존 센서는 생략 |
| `sensor_strength` | 위와 같고 값 열은 `mpa` |
| `rebound` | `zone_id, test_date, r_value`, `x, y` 생략 시 구역 가운데 |
| 공통 | `entered_by` (필수), `note`, `client_id` (`[0-9A-Za-z_-]{1,80}`, 필수) |
- 숫자는 숫자 또는 문자열(쉼표 소수점 `24,4` 허용). 시각은 `YYYY-MM-DD[T ]HH:MM[:SS]`, 구분자 `- / .` 허용 → 저장은 `YYYY-MM-DDTHH:MM`. 날짜는 `YYYY-MM-DD`.
- 응답 200: `{ok:true, saved, rows, used_in_decision, now, zone, floor_zones, warning, [duplicate], [skipped], [new_sensor]}`. `zone` = cylinder·rebound 는 그 구역, 센서는 센서 좌표가 든 첫 구역. `floor_zones` = 그 층 구역 전부(화면 타일 갱신용).
- 같은 `client_id` 다시 오면 저장 없이 `{ok:true, duplicate:true, rows:0, …}`.
- 오류: 400 값 오류 `{ok:false, error:"<무엇을 어떻게 고칠지>"}` (화면은 고치게 하고 큐에서 뺌) / 403 토큰 / 413 1 MB 초과 / 500 서버 오류 (화면은 큐에 남겨 다시 보냄).

### 5.4 값 검사 (서버, 화면도 같은 검사를 먼저 함)
- 공시체 강도 0 초과 ~100 MPa, 센서 강도 0~100 MPa, 온도 −30~90 °C, 반발경도 R 10~70 **[가정]**. 100 MPa 초과면 "kgf/cm² 이면 ÷10.2" 안내.
- 시험일·시각: 타설일(센서는 그 층 가장 이른 타설일) 이상, **내일 0시 전**. 양생은 `curings` 중 하나.
- 구역은 zones.csv 에 있고 pours.csv 에 타설 기록이 있어야 함. 센서 층에 구역·타설 기록이 있어야 함.
- 센서 ID `[0-9A-Za-z가-힣_-]{1,20}`. 같은 ID 를 다른 종류(온도↔강도)로 못 씀. 기존 센서의 층이 다르거나 위치가 0.05 m 넘게 다르면 거절 ("옮겼으면 새 ID"). 새 센서 x·y 는 그 층 구역 전체 범위 안.
- 같은 센서·같은 시각: 한 건 입력이면 **거절**, 붙여넣기면 **건너뛰고** `skipped` 로 알림 (센서 앱 누적 기록을 통째로 붙여도 됨).

### 5.5 `GET /api/recent?n=20` (1~200)
`{ok, items:[{client_id, kind, label, point, floor, when, value, count, entered_by, entered_at, note}]}` — client_id 가 있는 줄만(현장 입력), client_id 하나 = 1건 (붙여넣기 여러 줄은 count), entered_at 최신순.

### 5.6 evaluate 변경 (C# Evaluator 이식 대상)
- `sensor_strength.csv` 를 읽는다 (없으면 예전과 똑같음). 구역마다: 같은 층, `타설 시각 ≤ timestamp ≤ 판정 시각` 인 줄 중 센서별 **가장 늦은 시각**의 값·좌표 → `engine.zone_min_from_values(점, MPa, 구역 사각형)` (0.5 m 격자 가운데점 IDW p=2, 최소와 위치).
- 위치 근거 = 온도 센서 구역 최소와 강도 센서 구역 최소 중 **낮은 값** (같으면 온도 센서). 둘 다 없으면 기상 예측.
- `est_source` 글자: 온도만 `센서 6개 IDW` (예전 그대로), 강도만 `강도 센서 2개 IDW`, 둘 다 `온도 센서 6개 IDW 24.4·강도 센서 2개 IDW 23.8 중 최소` (값은 소수 1자리). 다른 열은 그대로.
- `engine.zone_min_strength` 는 `zone_min_from_values` 를 부르게 정리 (계산 순서·결과 동일, 샘플 정답 바이트 동일 확인).
- 샘플: `shoring/sample/data_strength/` (= data + W1(25, 4.5)·W2(5, 4.5) 강도 센서, `make_sample.py` 가 만듦) → 9F-C1 이 "존치 (구역 최소 강도 미달)" 로 바뀜.
- 주의: 3 m 규칙(§6) 이후 강도 센서 1개는 그 센서에서 3 m 안의 구역만 정한다. 다만 주변 3 m 안에 센서가 전혀 없는 구역은 층 전체 센서(그 1개)로 대체되므로 구역마다 센서 1개 이상 권장 [가정].
- 센서 기록 끊김 (2026-11-04): 온도 센서 기록 사이 공백이 6 h [가정, `evaluate.MAX_GAP_H`] 를 넘으면 6 h 까지만 앞 측정값을 쓰고 나머지는 기상 온도(양생 하한 없이)로 채운다. 마지막 기록 뒤 판정 시각까지 공백도 같음. `est_source` 에 `기록 끊김 S1 21일 → 기상 온도` 표시.
- 공시체 양생 종류 (2026-11-04): `curing` 이 현장봉함·현장수중·현장기건(`engine.FIELD_CURINGS`)이 아닌 줄(표준양생 등, CSV 직접 편집·가져오기로 들어온 값)은 기준 근거로 쓰지 않는다. 다른 공시체가 없으면 `reason` 에 `공시체 시험 결과 없음 (현장양생 아님 n건 무시)`.


## 6. 센서 영향 범위 (총괄 결정 2026-11-04)
- 구역 위치 근거(온도·강도 센서 모두)는 **구역 경계에서 3 m 안의 센서만** IDW 에 쓴다 (`evaluate.SENSOR_MARGIN_M = 3.0` [가정], 동바리 간격 1.2 m 의 약 2.5배). 하나도 없으면 그 층 센서 전체를 쓰고 `est_source` 끝에 ` (가까운 센서 없음 — 층 전체 센서)` 를 붙인다.
- 이유: 모바일 입력 시험에서 모서리 강도 센서 한 개(22.8 MPa)가 9층 6개 구역 전부를 '존치'로 만든 문제. 샘플 판정(9F A1·B1·C1·C2 요청 가능 / A2·B2 존치)은 바뀌지 않음, 값·좌표 일부만 바뀜(예 9F-A2 최소 지점).
- 배치 권장: 구역마다 센서 1개 이상(구역 가운데 또는 북측·단부처럼 차가운 곳). 입력 화면은 구역 안에 센서가 없으면 '가까운 센서 없음' 안내.

## 7. 동바리 확인 (2026-11-04 추가, 타설 전 게이트)

- 종류 `shore_check` → `shore_checks.csv`: `zone_id, timestamp(YYYY-MM-DDTHH:MM), present(예/아니오), actor(= 입력자), photo(파일 이름, 선택)` + 공통 열.
- 쓰임: `shoring/pour_gate.py` — 타설하려는 구역 아래 3개층(평면 겹침·층 순서) 구역마다 최근 24 h [가정] "예" 확인이 있어야 "타설 요청 가능", "아니오"·`approvals.csv` 해체 완료·확인 없음 → "타설 보류". 해체 판정(evaluate)에는 쓰지 않음 (`used_in_decision: false`).
- 이유: 해체는 협력업체가 하고 기록은 원청·감리만 남겨, 기록이 없으면 동바리가 "있는" 것으로 보이는 거짓 안전 (docs/field_process_research.md, 화정 2022 사조위).
- 한계: 사진은 파일 이름만 (업로드 없음).

## 9. 현장 사진 (2026-11-05 추가, F15 — 기준본 `shoring/photos.py`, C# `Photos.cs`)

- 받는 곳: `shore_check`·`after_check`(`POST /api/measure`)·신고(`POST /api/report`) body 의 `photo_data` = data URL 또는 표준 base64(공백 없음). 없으면 예전처럼 `photo` 글자(파일 이름).
- 검사: 디코드 8 MB [가정] 이하, 앞 바이트 JPEG(FF D8 FF)·PNG(89 50 4E 47 0D 0A 1A 0A) 만 — 아니면 400 ("사진은 JPEG·PNG 만 …" / "사진이 너무 큽니다 (n바이트) …" / "사진 자료를 읽지 못했습니다 …"), 줄·파일 없음. 다른 검사를 다 통과한 뒤에만 저장.
- 저장: `사진/YYYYMMDD/{구역}_{동바리확인|해체후점검|해체신고}_{HHMMSS}_{sha 앞 8자}.jpg|png` (날짜·시각 = 그 기록의 시각 — 동바리 확인 = 확인 시각, 신고 = 서버가 받은 시각, 해체 후 점검 = 점검일 + 받은 시각 (총괄 29차), 구역 이름의 영문·숫자·한글·-·_ 밖 글자는 _). 같은 날·구역·종류에 SHA-256 이 같은 파일이 있으면 그 파일. 원본 바이트 그대로 (메타데이터를 읽거나 고치지 않음).
- CSV: `photo` = 위 상대 경로('/' 구분), 선택 열 `photo_sha256` (사진을 올린 줄에만 값). 해체 후 점검은 항목 줄마다 같은 경로. 답에 `"photo": 경로`.
- 본문 상한 `MAX_BODY` 12,000,000 바이트 (8 MB 의 base64 + JSON).
- 쓰는 곳 (새 판단 없음): 슬래브 이력서 '현장 사진' 줄(있을 때만 기록에 `photos` 키 — 사진 없는 이력서의 지문은 그대로), 검측 패키지 `첨부/사진/`(요청 구역·판정일까지 + 요청서 현장 확인 ① 대상 = 위 3개층 평면 겹침 구역의 최근 24 h 동바리 확인·신고 사진, `approval.check_targets`, 총괄 29차), 감사 기록 detail '경로 · sha 앞 12자'·`line_sha`(줄 값에 photo_sha256 포함), 근거 원장 7 해체(협력업체 신고 줄 `sha256`).
- 휴대폰 오프라인 큐: 사진은 기기에 3장 [가정]·localStorage 가 되는 만큼만 — 넘으면 최근 것부터 사진만 빼고 저장(`photoDropped`), 화면을 연 동안은 메모리의 사진으로 보냄(`photoMem`), 안내 "사진은 연결된 뒤 다시".
- 설치 검측 서류의 사진은 근거 원장 1 설치검측 서류(file + sha256). 휴대폰 동바리 점검(§10, F16)은 사진을 받지 않음.

## 8. 협력업체 해체 신고 (2026-11-05 추가, F14)

- 신고 토큰 `r` = 데이터 폴더 `report_token.txt` (보기 토큰 `view_token.txt`·입력 토큰과 다름, 만료 없음, `shoring\sample` 에는 쓰지 않음). 주소 `/view?r=<신고 토큰>&f=<층>` — 현장 입력 창 '협력업체 해체 신고 QR' 에서 층을 골라 반장에게 따로.
- `GET /view?r=` · `GET /api/floor?f=&r=` → 층 화면과 같은 JSON + `"can_report": true` (보기 토큰이면 `false`). 틀린 `r` → 403 "신고 주소가 맞지 않습니다 …".
- `POST /api/report?r=` body `{"zone_id":"9F-A2", "present":"아니오"|"예", "name":"최반장", "company":"(선택)", "client_id":"rep-…"}` → `shore_checks.csv` 한 줄 `zone_id, timestamp(서버 지금, YYYY-MM-DDTHH:MM), present, actor(=name), photo(빈칸)` + 공통 열 + 선택 열 `source`(= 협력업체 신고)·`company`. 답 `{"ok":true, "saved":"shore_checks.csv", "duplicate":…, "zone_id", "present", "timestamp", "message"}`.
- `message`: '예' → "신고 저장됨 — 동바리 다시 세움" / '아니오' + 그 시각까지 그 구역 감리 승인 기록 있음 → "신고 저장됨 — 감리 승인 기록 있음: 해체 시각 기록됨" / 없음 → "신고 저장됨 — 감리 승인 기록 없음: 현장 확인 요청됨". 지시·처벌 문구 없음.

## 10. 동바리 단위 현장 점검 (2026-11-05 추가, F16 — 기준본 `shoring/shore_inspect.py`, C# `ShoreInspect.cs`)

- 사용자 확정: "구역 체크리스트와 문제 동바리 표시", 사진 없음. 새 공유 매개변수 없음.
- 동바리 ID = `구역-R행C열` (예 `9F-A2-R03C05`) — 구역 안 동바리 좌표를 0.1 m [가정] 로 묶어 행(남→북)·열(서→동) 순번. 애드인이 [현장 입력]을 켤 때 모델 동바리(`SHR_ZoneID` 있는 SHR_PipeSupport) 위치로 데이터 폴더 `shores.csv` (`id, zone_id, x, y, field_label`) 를 다시 씀 — `field_label`(현장 이름표)은 같은 ID 면 유지. 샘플 폴더에는 안 씀.
- 단계·항목 (`/api/model` 의 `"inspect": [{stage, items:[{code, label, criterion, source}]}]`): 설치 검측 SHR-B 수평연결재·SHR-S 이음 [확정 안전보건규칙 제332조의2]·SHR-A 상하 정렬 [확정 가이드라인 4.3]·SHR-V·H·P [입력] / 일상 점검 DLY-D 변형·침하 [확정 제334조]·DLY-B·L·W [입력] / 해체 전 PRE-C [확정 KCS 14 20 12]·PRE-A [확정 가이드라인 4.1]·PRE-F [입력] / 해체 후 AC-1–5 [확정 가이드라인 4.3].
- `POST /api/measure` `{"kind":"shore_inspect", "zone_id", "stage", "timestamp"(선택 — 없으면 서버 지금, 분 단위), "items":[{"item_code","result":"OK"|"NG","shore_ids":[…]}], "entered_by", "client_id"}` → `install_inspection.csv` 항목마다 한 줄: 기존 열 `zone_id, item_code, result, inspector(=입력자), date, photo_path(빈칸)` + 공통 열 + 선택 열 `stage, shore_ids(;), timestamp, source(= 현장 점검)`. 기존 열·기존 줄은 그대로.
- 검사 (400, 두 서버 같은 문구): 없는 구역·단계·항목, 항목 두 번, OK/NG 아님, `shore_ids` 가 목록 아님·500개 초과, 그 구역에 없는 동바리 ID, 미래 시각, 동바리 위치가 있는 구역의 NG 인데 동바리 없음 (위치가 없는 구역은 NG = 구역 전체 `*`).
- 답에 `"open_ng"`: 저장 뒤 그 구역의 풀리지 않은 NG 한 줄 (모든 단계, 예 "수평연결재 9F-B1-R01C01 외 1 · 이음 (구역 전체)"), `used_in_decision: true`.
- 규칙 ⑦ (정의서 §3.4-7): 풀리지 않은 **설치 검측** NG → 해체 판정 "존치 (설치 결함 기록)"(상태 2 설치 결함)·타설 전 확인 "타설 보류". 다른 단계 NG 는 기록·Revit 빨강 표시만.
- `GET /api/floor?f=<층>&t=<입력 토큰>` (F16): 층 화면 JSON + `zone_rects` [{zone_id, x0, y0, x1, y1}] + `shores` [{id, zone_id, x, y, label, ng}] + 구역마다 `ng`(한 줄). 보기·신고 토큰 응답도 같은 필드.
- 휴대폰: 구역 → [동바리 점검] → 단계 → 항목 OK/NG (큰 버튼, [전부 OK] — NG 로 고른 항목은 그대로) → NG 항목의 문제 동바리를 구역 평면(SVG)에서 눌러 여러 개 (두 손가락 확대·끌어 옮기기·＋－·구역 전체·선택 지우기, 1 m > 45 px 면 ID 글자). 층 평면·동바리 점은 기기에 담아 오프라인에서도 고르고, 기록은 기존 오프라인 큐.
- Revit: [색] 동바리 상태 덧그림에 'NG 동바리' 빨강 테두리 (풀리지 않은 NG, 모든 단계), 구역 전체 NG 는 구역 빨강 테두리. 동바리 상태의 '설치 검측 NG' 도 같은 규칙 (다시 점검해 OK 면 풀림).
- 토큰: `/api/report` 는 `r` 만 (보기 `v`·입력 `t` → 403). `r` 로 `/api/measure`·`/api/model` → 403.
- 오프라인 큐 (11-05, `view.html`): 못 보낸 신고는 기기 `localStorage`(`onsite.report.queue`)에 저장 → `online`·화면 돌아옴·20초마다 자동 재전송, `client_id` 로 중복 없음. 다시 보낼 때만 body 에 `device_at`(누른 시각, 기기 시계 `YYYY-MM-DDTHH:MM`) → 서버는 `timestamp` = 받은 시각 그대로, `note` = "기기 시각 <device_at> (오프라인 저장 뒤 전송)" (위조 방지 — 판정·경고는 서버 시각만 씀). 400·403 은 큐에 '보내지 못함 + 오류' 로 남고 [지우기].
- 쓰임 (새 판단 없음): 무단 해체 의심(`evidence.removal_watch`, 알림 맨 위)·타설 전 확인(`pour_gate`)이 기존 '아니오'·'예' 규칙대로 받음. `source` = 협력업체 신고 이고 '아니오'인 줄은 근거 원장 7 해체(결과 = 실제 시각, 출처 `shore_checks.csv`) — 안전관리자 확인 '아니오'는 관측이라 7 로 안 셈.

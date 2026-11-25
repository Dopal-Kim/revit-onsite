# Vercel 현장 서버 API (C# 이식용)

기준 구현: `server/vercel/api/handler.js` (서버), `tools/server_sync.py` (PC 동기화 클라이언트 — C# 은 이것을 1:1 로).
시험: `python tools/test_server_sync.py`. 배포 주소: https://revit-onsite-field.vercel.app

## 1. 원칙
- 판정은 Revit(PC)만. 서버는 Revit 이 올린 **스냅샷**을 보여 주고, 휴대폰 입력은 **검사만** 해서 쌓아 둔다 → PC 가 가져가 데이터 폴더 CSV 에 쓴다.
- 휴대폰 쪽 주소·응답 모양은 LAN 서버(`tools/field_server.py`, C# `FieldServer.cs`)와 같다. 차이: measure/report 답에 판정이 없음.
- 순서(버튼 하나 [서버 동기화]): **pull → ack → push**. pull 로 받은 입력을 CSV 에 넣고, 다시 판정한 스냅샷을 올려야 휴대폰에 새 판정이 보인다.

## 2. 인증
| 쓰는 곳 | 방법 | 환경 변수 (Vercel · PC 같은 값) |
|---|---|---|
| 휴대폰 입력 화면·API | `?t=` | FIELD_TOKEN |
| 층 보기 | `?v=` | FIELD_VIEW_TOKEN |
| 협력업체 신고 | `?r=` | FIELD_REPORT_TOKEN |
| PC 동기화 | 헤더 `Authorization: Bearer <키>` | FIELD_SYNC_KEY |
| PC 서버 주소 | — | FIELD_SERVER_URL |
토큰은 만료 없음 (LAN 서버의 12 h 만료와 다름). 바꾸려면 Vercel 환경 변수 교체 + 재배포 + PC 값 교체.

## 3. 동기화 API (PC → 서버)
### POST /api/sync/push
- 본문: 스냅샷 JSON 을 **gzip** 한 바이트, `Content-Type: application/gzip` (일반 JSON 도 받음). gzip 후 4.4 MB 이하.
- 스냅샷 (`server_sync.snapshot`):
```
{ "version": 1, "pushed_at": "2026-11-08T03:41:01",
  "model":  Store.model() 그대로 + model.floors[i] 에 shore_spec·shore_removed 추가,
  "floors": { "9": Store.floor_view(9, false) + shore_spec·shore_removed, "10": … },   // 키 = 층 번호 문자열 (B1 = "-1")
  "recent": Store.recent(200).items,
  "check":  { "zones": {zone_id: {floor, x0, y0, x1, y1}},          // zones.csv
              "pours": {zone_id: pour_date},                         // pours.csv
              "cylinders": [[zone_id, test_date, curing, f1, f2, f3], …],   // cylinders.csv 원문 글자
              "sensor_ts": {"sensor_temp": {sensor_id: [timestamp…]}, "sensor_strength": {…}},
              "shores": {zone_id: [shore_id…]},                      // shores.csv (shore_inspect 검사)
              "stages": {단계: [[item_code, label], …]},              // shore_inspect.STAGES
              "after_items": [[code, name], …],                      // after_check.ITEMS
              "mixes": [배합 이름…],
              "approved_at": {zone_id: "YYYY-MM-DDTHH:MM:SS"},       // approvals.csv 가장 이른 '감리 승인 기록' (해시 사슬 정상일 때만)
              "seen": [client_id…] } }                               // 데이터 폴더 CSV 의 모든 client_id
```
- `shore_spec` = 그 층 shore_spec.csv 줄 `[{zone_id, type, spec, spacing_x_mm, spacing_y_mm, splices, install_date}]` (zone_id 빈칸 = 층 전체), `shore_removed` = shore_removed.csv 를 (날짜, 구역)으로 센 것 `[{date, zone_id, count}]` 최근 날짜부터. 파일 없으면 `[]`.
- 답: `{ok: true, bytes, floors}`. 형식 틀림 400.

### GET /api/sync/pull
- 답: `{ok: true, items: [{client_id, route: "measure"|"report", body, ua, received_at}], more}`
  - `body` = 휴대폰이 보낸 JSON 그대로 (사진 있으면 `photo_data` 포함), `ua` = 휴대폰 User-Agent, `received_at` = 서버가 받은 한국 시각 `YYYY-MM-DDTHH:MM:SS`.
  - 오래된 것부터, 한 번에 약 3 MB. `more: true` 면 ack 뒤 다시 pull.
- PC 처리 (server_sync.pull): 항목마다 판정 시각 = 데모 고정 시각(있으면) 아니면 `received_at` 앞 16자 → `route` 에 따라 `Store.measure(body, ua)` / `Store.report(body, ua)` 에 **그대로** 넣음 (검사·CSV 열·사진 저장·client_id 중복 방지 모두 LAN 서버 코드).
  - 성공·중복 → ack. 입력 오류(Bad) → `error` 붙여 ack (다시 안 받음). 파일 잠김(PermissionError) → ack 안 함 (다음 동기화 때 다시).

### POST /api/sync/ack
- 본문: `{items: [{client_id, error?}]}` → 그 입력은 다시 안 줌, 서버의 사진 사본 삭제. 답 `{ok: true, acked}`.

## 4. 휴대폰 API (field_server 와 같은 주소)
| 주소 | 답 |
|---|---|
| GET `/?t=` · `/view?v=|r=&f=` | 화면 HTML |
| GET `/api/model?t=` | `snapshot.model` + `synced_at`. 스냅샷 없음 503 |
| GET `/api/floor?f=&t=|v=|r=` | `snapshot.floors[f]`, `can_report` = r 토큰일 때 true. 없는 층 404, f 없음 400 |
| GET `/api/recent?t=&n=` | 아직 PC 가 안 가져간 입력(`note` 끝 "(Revit 동기화 대기)") + `snapshot.recent`, n ≤ 200 |
| POST `/api/measure?t=` | 검사 통과 → `{ok, saved, used_in_decision, rows, skipped?, new_sensor?, photo?, now, zone: null, floor_zones: [], warning: "", pending_sync: true, message: "저장됨 — 판정은 Revit 동기화 뒤 반영"}` |
| POST `/api/report?r=` | `{ok, saved: "shore_checks.csv", duplicate, zone_id, present, timestamp, message, pending_sync: true}` (message 는 field_server 와 같은 세 문구) |

## 5. 오류 코드
| 코드 | 언제 |
|---|---|
| 400 | 입력 검사 실패 — `error` 는 field_server.validate 와 **같은 문구** · JSON 아님 · 본문 없음 |
| 403 | 토큰·동기화 키 틀림 (문구는 field_server 와 같음, 동기화는 "동기화 키가 맞지 않습니다 …") |
| 404 | 없는 주소 · 없는 층 |
| 413 | 본문이 너무 큼 (Vercel 은 4.5 MB 넘으면 함수 전에 막음 — 화면이 사진을 1600 px·3 MB 이하로 줄여 보냄) |
| 503 | 아직 스냅샷 없음 ("아직 Revit 에서 서버로 올린 판정이 없습니다 …") |
| 500 | 서버 오류 (`서버 오류 …`) |

## 6. client_id 규칙
- 형식 `^[0-9A-Za-z_-]{1,80}$` (휴대폰: `기기ID-순번`). 틀리면 400.
- 서버 중복 = 스냅샷 `check.seen` 에 있거나 이미 받은 입력 → 저장 안 하고 200 (`duplicate: true, rows: 0`).
- PC 중복 = `Store.seen` (CSV 의 client_id) — pull 을 두 번 해도 한 줄.
- 서버 저장 경로: `inputs/<client_id>.json` (한 건 = 파일 하나), 사진 `photos/<client_id>.txt` (ack 때 삭제), 받음 표시 `acked/<client_id>.json`.

## 7. 알려진 차이
- CSV `entered_at` = PC 가 가져간 시각 (서버가 받은 시각은 pull 의 received_at).
- 서버 검사 뒤 PC 데이터가 바뀌어 거부된 입력은 휴대폰엔 '저장됨'으로 남음 — 사유는 동기화 출력과 ack 의 error 에만.
- 사진은 휴대폰에서 줄인 사본 (긴 변 1600 px, JPEG 0.8) — photo_sha256 도 그 사본 기준.

## 8. v6.2 추가 (docs/redesign_v6.md v6.2 담당 S) — 모두 **선택 필드**, 없으면 서버는 그 기능만 숨김 (기존 응답 형식 그대로)
### 8.1 push 스냅숏에 더하는 것 (Revit → 서버, `server_sync.snapshot` 기준)
```
"timeline": export_4d.timeline_json(strength_timeline.timeline(folder, cure_min=5.0)) + {"steps": [0.4, 0.6, 0.8, 1.0]}
   = { "start": "YYYY-MM-DD", "end": "YYYY-MM-DD",
       "floors":    [{floor, label, fck, mpa: [날짜마다 MPa | null]}],      // 층 곡선 = 그날 타설된 구역 중 최소 (참고)
       "zones":     [{zone, mpa: [날짜마다 MPa | null]}],                   // 구역 색 = mpa ÷ 그 층 fck 를 steps 로 나눈 구간
       "cylinders": [{date, zone, floor, mpa, curing}],                    // 현장양생 공시체 평균 (실측)
       "lines":     [{name, mpa}],                                          // 측면 5 · 지지 슬래브 14 · fck
       "steps":     [0.4, 0.6, 0.8, 1.0] }                                  // strength_timeline.STEPS (색 구간, [가정])
   날짜 i = start + i 일. 타설 기록이 없으면 "timeline": null.
"check.requested": {zone_id: {"at": "YYYY-MM-DDTHH:MM:SS", "actor": 이름}}   // approvals.csv 의 그 구역 마지막 '요청 제출' (해시 사슬 정상일 때만)
```
- `cure_min` 은 Revit 의 [프로젝트 설정] 값이 있으면 그 값 (C# 은 StrengthTimeline 과 같은 값). 웹 공유판(onsite_4d.html)의 `strength_timeline` 과 같은 모양.
- 서버는 이 자료로 그림만 그린다 — 판정·예측을 다시 계산하지 않음.

### 8.2 서버가 새로 주는 것
| 주소 | 토큰 | 답 |
|---|---|---|
| GET `/timeline?v=|r=|t=` | 보기·신고·입력 | 강도 타임라인 화면 (S1) |
| GET `/api/timeline?v=|r=|t=` | 〃 | `{ok, project, now, fixed_now, synced_at, server_now, timeline, floors:[{floor, name, zones:[{zone_id, x0..y1, status, decision}]}], alerts, pending:[…], unapproved_reports:[…]}` |
| GET `/supervisor?s=` · GET `/api/supervisor?s=` | 감리 FIELD_SUPERVISOR_TOKEN | 감리 화면 (S3) · `{ok, now, synced_at, zones:[{zone_id, floor, decision, reason, threshold, f_cyl, f_est_min, level_gate, evidence_stage, requested_at, requested_by, can_confirm, why_not}], checks:[①②③ 문구]}` |
| POST `/api/supervise?s=` | 〃 | 본문 `{client_id, zone_id, name, checks:[true,true,true]}` → `{ok, zone_id, message, pending_sync:true}` |
| GET `/api/push/key?t|v|r|s=` | 아무 화면 토큰 | `{ok, key}` VAPID 공개 키 (FIELD_VAPID_PUBLIC 없으면 404 → 화면이 [알림 받기] 숨김) |
| POST `/api/push/subscribe?…` | 〃 | 브라우저 PushSubscription JSON → `{ok}` (Blob `subs/<해시>.json`) |
| GET `/sw.js` · `/manifest.json` | 없음 | 알림용 서비스 워커 · 홈 화면 추가용 (iPhone 은 홈 화면에 추가해야 알림) |

- `pending` = 아직 Revit 이 반영 안 한 입력(스냅숏 `check.seen` 에 없는 것): `{client_id, kind, zone_id, floor, when, mpa?, present?, received_at}` — 공시체는 평균 MPa.
- `unapproved_reports` (S2) = '뺐음' 신고 중 신고 시각에 그 구역 `approved_at` 이 없고 아직 Revit 이 반영 안 한 것: `{client_id, zone_id, at, name, company}`.
  `/api/model` · `/api/floor` 답에도 같은 이름 필드로 붙음 (없으면 빈 목록). 문구는 "승인 기록 없는 해체 신고 (Revit 판정 전)" — 판정 문구 4개는 쓰지 않음.

### 8.3 pull 의 새 route
- `route: "supervise"` — `body = {client_id, zone_id, name, checks:[true,true,true]}`, `received_at` = 서버가 받은 한국 시각.
  PC 처리(server_sync.supervise, C# 은 W-A): 그 시각 판정이 '해체 승인 요청 가능' + 그 구역 '요청 제출' 기록이 있을 때만
  `approval.record(..., "감리 승인 기록", actor=name, approver=name, signed_at=received_at, note="감리 직접 확인 (서버) …")`.
  통과 못 하면 넣지 않고 ack 에 error(이유). (source 열은 W-A 가 approvals.csv 에 더하면 `감리 직접` 으로.)

### 8.4 웹 푸시 (S8)
- 환경 변수: `FIELD_VAPID_PUBLIC`, `FIELD_VAPID_PRIVATE`, `FIELD_VAPID_SUBJECT`(예: `mailto:` 현장 주소) — 값은 화면·로그·파일에 쓰지 않음.
- 보내는 때: ① S2 승인 기록 없는 해체 신고 받는 즉시 ② push 때 오늘 볼 것에 새로 생긴 '타설 보류'·'무단 해체 의심' (앞 스냅숏과 비교).
- 만료된 구독(404·410)은 지움. 외부 문자·메신저 서비스 없음.

## 9. v6.3 (11-11 개선 계획 0·1차)
### 9.1 화면은 열쇠 없이 (0-7)
- `/` · `/view` · `/timeline` · `/supervisor` 화면(HTML)은 열쇠 없이 열림 — 자료가 없는 껍데기. 자료(`/api/*`)는 그대로 열쇠 필요(틀리면 403).
- 화면은 첫 접속 때 주소의 열쇠(t·v·r·s)를 기기 저장소(localStorage `onsite.tok.<경로>`)로 옮기고 https(Vercel)에서는 주소창에서 지움. QR·층 표지 주소(열쇠 포함)는 그대로 작동.
- Wi-Fi 서버(field_server.py · C# FieldServer)도 v6.4(11-12)부터 `/`·`/index.html`·`/view` 화면을 열쇠 없이 냄 → 화면이 어느 서버에서나 주소창 열쇠를 지움 (자료 `/api/*` 는 그대로 열쇠).

### 9.2 감리 확인 세 가지 (1-2) — POST `/api/supervise?s=` 본문에 `action`
| action | 본문 | 서버 검사 (스냅숏 기준) | PC 처리 (server_sync / C#) |
|---|---|---|---|
| `"plan"` | `{client_id, zone_id, name, action}` | 요청 제출 구역 · 아직 승인·해체 기록 없음 · `check.plan_confirmed` 없음 · 같은 확인 대기 없음 | evidence.csv 단계 2 '감리 확인' 줄 |
| `"final"` (없으면 이것) | `{client_id, zone_id, name, action, checks:[true,true,true]}` | 위 + 판정 '해체 승인 요청 가능' + plan 확인(스냅숏 또는 보낸 것) + `check.evidence[zone]["4"]` 있음 | 지금과 같음 + 단계 2 감리 확인·단계 4 없으면 거절(ack error) |
| `"reject"` | `{client_id, zone_id, name, action, reason}` (reason 1–100자) | 요청 제출 구역 · 승인·해체 기록 없음 | supervise_rejects.csv "감리 보완 요청 — 사유" → 오늘 볼 것 |
- pull 항목은 지금처럼 `route: "supervise"`, body 그대로 (action 이 없던 옛 입력 = final).
- 스냅숏 `check` 에 (총괄 추가): `evidence = {zone_id: {"1".."8": "YYYY-MM-DD" | ""}}`, `plan_confirmed = {zone_id: "YYYY-MM-DDTHH:MM:SS"}`. 없으면 서버는 단계 4 검사를 건너뜀(PC 가 거절로 막음).
- GET `/api/supervisor` 구역마다 더한 필드: `evidence:[{n, name, date}] | null`, `plan_confirmed_at`, `can_plan/why_plan`, `can_final/why_final`, `can_reject/why_reject`, `history:[{action, at, name, reason, state: "대기"|"기록됨"|"거절", error}]` (최근 5, 서버 `sup/<cid>.json` + `acked/<cid>.json`). 옛 `can_confirm/why_not` = final 과 같음.

## 10. v6.4 (2차 묶음)
### 10.1 타설 전 동바리 일괄 확인 (2-2) — 새 route 없음
- 휴대폰이 구역마다 기존 `POST /api/measure` `kind: "shore_check"` 한 건씩 (`client_id` = 공통 id + `-` + 구역 ID 의 영문·숫자 외 → `_`). Wi-Fi 서버(field_server·C#)도 그대로 받음.
- 대상 구역 = shoring/pour_gate.py 와 같은 규칙: 층 순서로 바로 아래 3개층, 평면이 면적 > 0 으로 겹치는 구역 (화면이 model.floors 의 x0..y1 로 계산).
- (`/api/report` 는 쓰지 않음 — 협력업체 신고 출처로 남아 '해체 완료'·무단 해체 의심으로 읽히기 때문)

### 10.2 공시체 사진 (2-4) — PC 쪽 바꿀 것 (총괄)
- 휴대폰 공시체 입력 본문에 `photo_data` (다른 사진과 같음: `data:image/jpeg;base64,…` 1장, 줄인 뒤 3 MB 이하 [Vercel] / 8 MB [Wi-Fi]). 묶음 공시체(zone_id "" + pour_id)도 같음.
- 서버(Vercel): photos.decode·save 와 같은 검사(validate.js photoCheck), 통과하면 답 `photo: "사진 1장 (동기화 때 저장)"`. 사진은 `cylph/<cid>.json`(구역 목록·시험일) + `photos/<cid>.txt` 로 남기고 **ack 때 지우지 않음** (감리 화면 표시).
- PC (field_server.validate cylinder → C# 같게): `self.photo(b, zid or "타설 "+lot, "cylinder", datetime.combine(시험일, 받은 시각))` → cylinders.csv 선택 열 `photo` = 저장 경로(사진/YYYYMMDD/…), `photo_sha256` 추가. photos.KIND_NAME 에 `"cylinder": "공시체"`. 사진 검사 실패 = 같은 문구 Bad.
- GET `/api/photo?s=&id=<client_id>` (감리 열쇠만, 그 외 403) → 사진 바이트. `/api/supervisor` 구역마다 `photos: [{id, test_date, curing, lot}]` (최근 3).

### 10.3 감리 ③ 펼침 (2-4)
- `/api/supervisor` 에 `check3: [{group: "작업준비"|"작업자"|"작업허가", items: [...]}]` (가이드라인 4.1). 화면은 펼친 항목을 모두 체크해야 ③. PC 로 가는 `checks` 는 그대로 3개 불리언.
- 원장 4(해체 전 점검)는 `evidence` 의 날짜를 읽기 전용으로 표시.

### 10.4 오프라인 열기 (2-3)
- `/sw.js` 가 화면 탐색 요청(`/`, `/view`, `/timeline`, `/supervisor`)을 네트워크 먼저(6 초) → 끊기면 마지막 화면. 자료는 화면이 기기에 저장(입력 화면 = 마지막 model, 층 보기 = 층마다 마지막 /api/floor).

### 10.x 해체 후 점검 침하 계측 (2-6 G9, 총괄이 PC 쪽 구현)
- POST `/api/measure` `kind: "after_check"` 본문에 **선택 필드** `settle: {지점: mm}` (예 `{"P1": 1.5, "P2": "0,8"}`) — 지점 = 영문·숫자 1–10자(대문자로 바꿔 저장), mm = -100–100 숫자(쉼표 소수점 받음), 20지점까지.
- PC(field_server.py / FieldServer.cs)는 지점마다 `after_check.csv` 한 줄: `item_code = "SET-"+지점`, `result = mm`. `items` 가 비어도 `settle` 만 있으면 저장. 날짜마다 반복해 넣는 값(같은 지점을 다시 재면 위치 일치).
- 거절 문구: "침하 계측 형식이 틀렸습니다 (지점 → mm, 20지점까지)" · "침하 측정 지점 '…' — 영문·숫자 10자까지 (예: P1)" · 범위 밖은 `num` 공통 문구.
- 화면: 해체 후 점검 폼 '침하 계측' 접힘 칸에 지점·mm 줄 + [＋ 지점 더하기] (20지점까지), 구역마다 지난번 지점 이름 기억. mm 를 넣은 줄만 보냄. 서버(validate.js)도 같은 문구로 검사 (v6.4, vercel-server).

### 10.5 누가 넣었나 (총괄 제안 ①) — 스냅숏에 더해 주면 감리 화면이 바로 표시 (없으면 안 보임)
- `model.floors[].zones[]` 에 `cyl_entered_by` ("이름 (역할)" — 괄호 앞 한 칸, options.json actor_roles, 검토서 출처 표와 같게) · `cyl_entered_at` ("YYYY-MM-DDTHH:MM") — 그 구역 판정에 쓴 공시체 줄(묶음 줄 포함)의 entered_by·entered_at.
- `check.evidence_meta = {zone_id: {"1".."8": {"by": "이름 (역할)", "at": "YYYY-MM-DD"}}}` — 근거 원장 단계마다 마지막 줄의 by·원장 날짜 (evidence.csv 에 입력 시각 열이 없어 날짜).
- (총괄 11-12 구현 완료 — Python·C# 같게)

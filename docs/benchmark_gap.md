# Revit On-Site — 벤치마크 비교와 부족한 점 (2026-11-04)

- 목적: 성숙도(무선 강도) 센서 제품, 거푸집·동바리 BIM 계획 도구, 4D 도구, 연구 시제품과 Revit On-Site 를 기능별로 비교해 **차별점**과 **부족한 점**을 근거와 함께 정리하고, 학생 프로젝트 범위에서 할 수 있는 개선 순위를 정한다.
- 읽은 내부 문서: `동바리검측_프로젝트구상.md` §3.2·§3.4·§3.7·§3.7-1·§3.7-2, `docs/field_input_spec.md`, `docs/문제정의_방향성.md`, `design/ui_spec.md`, `docs/scenario_validation.md`, `HANDOFF.md` §0-0 (구현·배포 상태).
- 표시: ✓ 있음 / 부분 = 일부만 또는 간접 / – = 확인한 자료에 없음 / [미확인] = 공개 자료로 확인하지 못함. "–" 는 "그 기능이 없다"가 아니라 **"이번에 읽은 공개 자료에서 찾지 못했다"** 는 뜻이다.
- 출처는 문서 끝 "출처" 표의 ID (예 [GI1]) 로 단다. 제품 설명은 제조사 공개 페이지 기준이며 실제 사용 시험은 하지 않았다.

---

## 1. 비교 대상

| ID | 대상 | 분류 | 고른 이유 |
|---|---|---|---|
| A | Giatec SmartRock + SmartRock Web (+ Roxi AI) | 무선 성숙도 센서 + 앱·웹 | 세계적으로 가장 널리 쓰인다고 홍보, 국내 대리점 있음 [GI1][MC1] |
| B | Doka Concremote | 센서 + 클라우드 포털 (거푸집 회사) | 탈형·재받침 판단을 명시, 거푸집 회사 제품 [DK1][DK2] |
| C | Converge ConcreteDNA | 센서 + AI 예측 + BIM 디지털 트윈 | BIM 연계 사례(홍콩 Gammon) 공개 [CV1][CV3][NCE] |
| D | Maturix (Sensohive / Kryton / Chryso) | 센서 + 웹·앱 + API | 보정곡선 자동 생성·자가 보정 예측·공개 API [MX1][MX2][MX3] |
| E | 국내 건설사·업체 (포스코이앤씨 스마트 양생관리·네이블 SMARTRI, 대우건설 BaroQ·온도이력 추종 양생, 데키스트) | 국내 IoT 양생 관리 | 국내 규정·현장 관행에 가장 가까움 [PO1][PO2][NB1][NB2][DW1][DW2][DQ1][DQ2] |
| F | DokaCAD for Revit (+ PERI CAD, BIM²form, ULMA Studio) | 거푸집·동바리 BIM 계획 | Revit 안 동바리 자동 배치·사이클 계획 [DC1][DC2][PR1][BF1][UL1] |
| G | Bentley SYNCHRO 4D (+ Navisworks TimeLiner) | 4D 공정 시뮬레이션 | 계획 대비 실적, 현장 진척 입력의 기준 도구 [SY1][NW1][NW2] |
| H | 연구 시제품 (Hamooni 2020 Revit 애드인, Apatos Reshoring Revit 애드인, ISARC 2026 Revit+Dynamo 가설지지 4D) | BIM + 센서/재받침 연구 | 우리와 같은 "Revit 안에서 해체 판단" 시도 [HM1][AP1][IS26] |
| — | **Revit On-Site (현재)** | Revit 2026 C# 애드인 + 모바일 웹 + 웹 공유판 | 이 저장소 |

참고만 한 제품 (표에서 뺀 이유: 기능이 A·D 와 거의 같음): Wake HardTrack [WK1][WK2], COMMAND Center (Transtec) [CC1][CC2], Hilti Concrete Sensors [HL1][HL2], Avontus Designer (비계, 동바리 일부) [AV1].

---

## 2. 기능 비교표

| 기능 | A Giatec | B Concremote | C Converge | D Maturix | E 국내 | F DokaCAD 등 | G SYNCHRO 4D | H 연구 시제품 | **Revit On-Site (현재)** |
|---|---|---|---|---|---|---|---|---|---|
| ① 모델(BIM) 안 센서 위치 | – 타설·구획 단위로 묶음, 도면·BIM 배치 [미확인] [GI1] | – [DK2] | 부분: BIM 모델에 타설 구획 색 표시 [CV3][NCE] (센서 점 위치 [미확인]) | – 지도에 타설 위치 표시 [MX3] | – BIM 언급 없음 [PO1][NB1] | 해당 없음 (센서 기능 없음) | – | ✓ 센서 값을 Revit 부재에 연결 [HM1] | ✓ 센서 x·y → SNS_Sensor 표식, 현장 입력 새 센서 자동 배치 (`field_input_spec` §5.2, HANDOFF §0-0) |
| ② 성숙도 보정 (강도–성숙도 곡선) | ✓ 보정 DB, 4가지 방법, AI 가 보정 오류 감지 [GI2]; 무보정 센서 SmartRock Pro [GI4] | ✓ 콘크리트 등급마다 보정 측정 필수, 보정 상자 [DK2] | ✓ 시험실 파쇄 → 곡선 맞춤 [CV2] | ✓ 시험값 입력 → 곡선 자동, 자가 보정 예측 [MX2][MX3] | 부분: 적산온도–강도 실측 활용 [DW2], 알고리즘·KOLAS 검증 [NB2] | – | – | 부분: ASTM C1074 성숙도 지수 [HM1] | 부분: KDS 14 20 10 강도식 + 배합 계수 [가정] + 공시체로 s 역산(`calibrate_s`). 시험실 보정곡선 입력 없음, 강도 센서는 제조사 값 그대로 (§3.3, `field_input_spec` §2) |
| ③ 구역·부재 단위 강도 (한 슬래브 안 차이) | 부분: 여러 위치 동시 측정 [GI1] | 부분: 측정점별 [DK2] | 부분: 타설 구획별 [NCE] | 부분: 타설(cast)별, 위치 간 18시간 차이 사례 [KR2] | – | 부분: 타설 구획(사이클) 단위 | ✓ 부재 단위 분해 [SY1] (강도 없음) | 부분: 부재별 [HM1] | ✓ 센서 IDW 0.5 m 격자 → **구역 최소 강도와 그 위치**, 경계 3 m 안 센서만 (§3.3, `field_input_spec` §6) |
| ④ 법규 근거 게이트 | – 목표 강도 도달만 (ASTM/ACI 방법) [GI1] | 부분: 독일 DBV·DIN 1045-3 계산 방법 근거 [DK2] | – | – | 부분: 탈형 강도 → 갱폼 인양, 설계강도 → 동바리 해체 [PO3] | – (제품 조립 규칙) | – | 부분: 외국 규준 존치일과 비교 [HM1] | ✓ KCS 14 20 12 3개층 존치·지지층 14 MPa·가이드라인 하중비교·**공시체 없으면 승인 요청 불가** (§3.4, S2·S4·S6 PASS) |
| ⑤ 다층 존치·재받침 하중 검토 | – | 부분: 재받침 판단에 강도 자료 사용을 명시 [DK2] | – | – | – | – (Staxo 등 지지 시스템 배치만 [DC1]) | – | ✓ 단순법(Grundy–Kabaila)으로 재받침 배치·도면 [AP1]; 재받침 4D [IS26] | 부분: 가이드라인 하중비교법 + √(f/fck) 감소 [가정], 필요 존치 층수·동시 세트 수 경고. 층간 하중 분배 해석·재받침 계획은 없음 (§3.2, S3·S11) |
| ⑥ 사이클·4D 계획 (계획 + 실적) | – (시간 절감 계산기만 [GI3]) | 부분: 강도 예측(Forecast)·배합 시나리오 비교 [DK1] | 부분: AI 도달 시점 예측, BIM 에서 층 사이클 시간 [CV3][NCE] | 부분: 목표 도달 예측 [MX2] | 부분: 골조 공정 시점 예측 → 인력·장비 계획 [PO1] | ✓ 사이클·조립·전용 계획, 부재 목록 [DC1][DC2]; 사이클별 최대 물량 [PR1] | ✓ 계획 대비 실적, P6 가져오기, 대안 비교 [SY1] | 부분: 센서로 4D 양생 기간 갱신 제안 [FT18] | ✓ 자동 계획 + 실적 반영 + 공정표 파일, Revit 4D 쌓아 올리기·웹 공유판 (§3.7-1, §3.7-2). 계획 대비 실적 비교 색은 없음 |
| ⑦ 모바일 입력 | ✓ 앱, QR 로 센서 등록 [GI1] | ✓ 앱 [DK1] | ✓ 앱·블루투스 수집 [CV4] | ✓ 앱 [MX3] | ✓ 스마트폰·PC [NB1], 시험일지 앱 [DQ1] | – | ✓ SYNCHRO Field [SY2] | 부분: 블루투스 수집 앱 [IS22] | ✓ 휴대폰 웹(QR 주소+토큰), 공시체·온도·강도 센서·반발경도, 오프라인 큐·중복 방지 (`field_input_spec` §1–§5) |
| ⑧ 알림 | ✓ 목표 강도·임계값 알림 [GI1] | ✓ 목표 강도·온도 한계 푸시 [DK1] | [미확인] | ✓ 맞춤 알람 [MX1] | ✓ 기준 강도 도달 알람 [PO1], 탈형 적산온도 알림 [DQ1] | – | [미확인] | – | 부분: 대시보드 경고 목록만 (ui_spec §5). 푸시·휴대폰 알림 없음 |
| ⑨ 감리 승인용 보고서 | 부분: PDF·CSV 보고서 [GI1] | 부분: 모든 측정 기록 = 의사결정 근거 보고서 [DK1] | [미확인] | ✓ 보고서 자동 생성·PDF [MX1][MX4] | 부분: 반입 시험 법정 서류 자동 생성 [PO1] (해체 승인 서류 [미확인]) | 부분: 도면·부재 목록 | 부분: 시각 보고 [SY1] | 부분: 타설별 도면 [AP1] | 부분: 일람표 2개 있음. [승인 기록]·[보고서]·[감사 기록] 버튼은 "준비 중" (HANDOFF §0-0, 문제정의 §5-4) |
| ⑩ 오프라인 / Revit 없는 공유 | 부분: 현장에서 센서 직접 읽기, 공유는 클라우드 [GI1] | 클라우드 포털 [DK2] | 클라우드 [CV1] | 클라우드 + API [MX2] | 클라우드(LTE·LoRa) [NB3][DQ2] | Revit 파일 | 클라우드 대시보드 [SY1] | – | ✓ 클라우드 없이 같은 LAN, 휴대폰 오프라인 큐, 웹 공유판 HTML 한 파일 (`field_input_spec` §1, §3.7-1). 원격 접속은 없음 |
| ⑪ 여러 모델 범용성 | 해당 없음 | 해당 없음 | 부분: BIM 연계 [CV3] | 해당 없음 | 해당 없음 | ✓ Revit 모델·고객 IFC 에 계획 [DC1] | ✓ IFC 2x3·4, 50여 CAD [SY1] | – (사례 1건) | 부분: 레벨·그리드 이름 비의존, IFC(KBIM)·프리캐스트 제외 판별. RAVA3Pro 부정 시험(S10)은 미실행 (§3.7-1) |
| ⑫ 타설 전 설치 검측 | – | – | – | – | – | 부분: 제품 조립 규칙상 "기술적으로 맞는 배치" [DC1] | – | – | ✓ 높이 3.5/4/5 m·이음·1본 하중 자동 검사 (S8·S9 PASS) |
| ⑬ 외부 연동 (API) | ✓ REST API, Procore [GI1][GI3] | [미확인] | [미확인] | ✓ 공개 REST API [MX2] | [미확인] | ✓ 부재 목록 내보내기 [DC2] | ✓ P6·Asta [SY1] | – | – CSV·붙여넣기만 |
| ⑭ 온도차·저온 경보 (균열·동해) | ✓ 이중 온도로 온도차 [GI1] | ✓ 온도 한계 푸시 [DK1] | ✓ 온도차 한계 감시 [CV1] | ✓ (Chryso: 한중 온도 관리) [MX5] | 부분: 동절기 급열 감시 [NB3] | – | – | – | – 양생 하한 온도(5/15 °C)는 계산에만 씀 |

**표 읽는 법:** 센서 제품(A–E)은 ②⑦⑧⑭가 강하고 ①④⑤⑫가 비어 있다. 계획 도구(F·G)는 ⑥⑪이 강하고 강도 판단이 없다. Revit On-Site 는 ①③④⑫ 와 ⑥ 일부를 한 화면에 묶었고, ②⑧⑨⑬⑭ 가 약하다.

---

## 3. 차별점 — 확인한 자료 중 아무도 하지 않는 것

1. **국내 해체 규정을 판정 순서로 묶은 것 (④).** 센서 제품은 사용자가 정한 목표 강도에 닿았는지 알려 준다 [GI1][DK1][MX1]. 국내 건설사도 "탈형 강도 → 갱폼, 설계강도 → 동바리"까지만 공개했다 [PO3]. Revit On-Site 는 KCS 14 20 12 의 3개층 존치, 지지층 14 MPa, 해체 가이드라인 하중비교, 공시체 우선을 차례로 검사해 결론을 "해체 승인 요청 가능 / 존치 + 사유 + 예측 도달일"로 낸다 (§3.4, S2·S4·S6·S7 PASS). 연구 시제품도 외국 규준의 존치일과만 비교했다 [HM1].
2. **공시체와 센서의 역할을 나눈 것.** Giatec 은 센서가 공시체를 대신한다고 말하고("no lab results or cylinder breaks needed" [GI1]), Converge 도 공시체 의존을 없앤 사례를 내세운다 [UK1]. 국내는 2025년부터 현장양생 공시체가 의무라서(문제정의 §2) 그대로 들여오면 규정과 맞지 않는다. 우리는 공시체 = 기준 근거, 센서 = 위치 근거로 두고 **센서만으로는 승인 요청을 막는다** (S6). 반대로 대우건설의 온도이력 추종 양생 [DW1]처럼 공시체 쪽을 구조체 온도에 맞추는 국내 흐름과는 잘 맞는다.
3. **구역 최소 강도와 그 위치 (③).** 제품은 센서점마다 값을 보여 준다. 한 층 안에서도 위치마다 목표 도달이 18시간 차이 난 사례가 있지만 [KR2], 그것을 해체 단위(구역)의 최소값으로 바꾸는 기능은 찾지 못했다. 우리는 IDW 격자로 구역 최소와 좌표를 내고, 공시체는 합격인데 북측 그늘 구역만 존치로 잡는다 (S1).
4. **타설 전 설치 검측과 해체 판정을 같은 모델에서 (⑫ + ④).** 거푸집 회사 도구는 설치 계획 [DC1][PR1], 센서 제품은 양생 [GI1]만 다룬다. 우리는 높이·이음·수평연결재·1본 하중을 모델로 검사하고 같은 구역 객체로 해체까지 추적한다 (S8·S9).
5. **강도 근거로 계산한 "동시 동바리 세트 수"와 겨울 존치 층수 (⑤ + ⑥).** PERI CAD 의 사이클 최대 물량은 공정 날짜 기준이다 [PR1]. 우리는 센서·공시체 판정일이 공정을 미루는 것을 반영해 겨울에 3개층 존치가 모자라고 세트가 더 든다는 것을 낸다 (S3·S11). 연구 도구 [BU15]는 층간 하중을 계산하지만 초록 기준으로 BIM·센서 실측 연결은 없다.
6. **클라우드 없이 현장 LAN 에서 끝나는 입력과 공유 (⑩).** 확인한 센서 제품은 모두 클라우드·통신망을 전제로 한다 [DK2][MX2][NB3][DQ2]. 우리는 같은 Wi-Fi 의 휴대폰 → 현장 PC CSV → Revit, 그리고 Revit 없는 감리용 웹 공유판 HTML 한 파일이다. 대신 원격 확인은 못 한다 (§4 G8).

주의: 6번은 장점이자 단점이다. 1·2번이 발표의 핵심 차별점이고, 3·4번은 시연으로 보여 줄 수 있다.

---

## 4. 부족한 점 — 다른 제품에 흔히 있고 우리에게 없는 것

크기: S = 하루 이내, M = 2–4일, L = 1주 이상 (학생 프로젝트, Python 기준본 → C# 이식 포함 추정).

| ID | 부족한 점 (있는 곳) | 현장 기술자·감리에게 왜 중요한가 | 크기 | 우리 구조에서 넣는 방법 |
|---|---|---|---|---|
| G1 | **알림** — 목표 강도·온도 한계 알림 (A [GI1], B [DK1], D [MX1], E [PO1][DQ1], HardTrack [WK1], COMMAND Center [CC1]) | 공시체 시험일·해체 승인 요청 가능일을 놓치면 공정이 밀리거나, 반대로 날짜만 보고 해체한다. 국내 포스코 사례는 알람 목적을 갱폼 인양 안전사고 방지로 든다 [PO1] | S | `evaluate.py` 결과에서 "오늘 바뀐 상태"(→5, →9, 공시체 시험 필요)를 뽑는 함수 → `field_server.py` `GET /api/alerts` → 휴대폰 화면 맨 위 배지 + 대시보드 경고. 주의: 브라우저 Notification API 는 HTTPS(보안 컨텍스트)에서만 되므로 LAN http 에서는 **화면 안 배지**로 한다 (푸시 서비스는 클라우드라 범위 밖). C# `FieldServer.cs` 에 같은 경로 |
| G2 | **시험실 성숙도 보정곡선** — ASTM C1074 강도–성숙도 쌍 입력, Nurse-Saul/Arrhenius 선택 (A [GI2], B [DK2], C [CV2], D [MX3], COMMAND Center [CC2], HardTrack [WK2]) | 레미콘사·시험실이 준 보정 자료를 그대로 쓰면 가정 계수(s, Ea) 대신 그 배합 값으로 예측한다. 감리가 "그 강도 어디서 나왔나"를 물을 때 답이 된다 | M | `engine.py` 에 `fit_maturity(pairs, method)` (표준 라이브러리 최소제곱, 로그·쌍곡선 식) + `calibration.csv`(mix, age_h 또는 maturity, mpa, datum_c). 배합에 보정이 있으면 그 곡선, 없으면 지금 KDS 식. 판정 근거 글자에 "보정곡선 C24-A" 표기. 엔진 일치 시험에 샘플 추가 → C# `Engine` 이식 |
| G3 | **단기 예보로 도달일 예측** — Forecast·AI 예측 (B [DK1], C [CV3], D [MX4]) | 지금 예측 해체일은 평년값·지난 관측 기반이라 한파가 오면 늦게 맞는다. 다음 주 인력·장비 계획에 바로 쓰인다 [PO1] | S–M | `weather.csv` 에 `source`(관측/예보/평년) 열 추가, 기상청 단기예보 API 값을 앞날에 채움 (키 파일은 기존 04_Data 방식). `predicted_due` 옆에 "예보 반영" 표시. 예보 API 응답 형식은 구현 때 확인 [미확인] |
| G4 | **저온·온도차 경보** (A [GI1], B [DK1], C [CV1], HardTrack 경보 단계 [WK1], 국내 동절기 급열 감시 [NB3]) | 겨울 공정이 데모 핵심(문제정의 §5-3)이고, 화정 사고 조사도 동절기 양생 미흡을 들었다. 센서 온도가 양생 하한 아래로 떨어지면 그 구역 예측이 무너진다 | S | `evaluate.py` 에 센서별 최근 온도 < `PRJ_CureMin` 또는 몇 시간 내 급락이면 경고 행 추가 (값은 [가정], 한중 콘크리트 기준 KCS 14 20 40 수치는 원문 확인 후 [미확인]). 대시보드 경고·G1 배지로 같이 나감 |
| G5 | **센서 업체 자료 가져오기** — REST API·CSV 내보내기 (A [GI1], D [MX2]) | 현장이 이미 Giatec·Maturix·네이블을 쓰면 값을 손으로 옮기지 않아야 한다. 지금은 붙여넣기만 된다 | S (업체별) | `tools/import_vendor.py` 업체별 열 이름 표 → `sensors.csv`/`sensor_strength.csv` 형식으로 변환, 현장 입력 서버와 같은 값 검사 재사용. 업체 내보내기 실제 열 이름은 샘플 파일이 있어야 함 [미확인]. API 직접 연결은 인터넷이 필요해 선택 사항 |
| G6 | **승인 기록·해체 검측요청서(감리 검측요청 서식 준용)·변경 불가 기록** — 보고서 자동 생성 (D [MX1][MX4]), 측정 기록을 근거 문서로 (B [DK1][DK3]), 바뀌지 않는 자료 (COMMAND Center "unalterable" [CC1]) | 감리는 공동주택 감리 세부기준 별지 제5·6호 서식(§3.2)으로 승인한다. 화정 1심처럼 기록이 증거가 되므로(§3.8) 고친 흔적이 남아야 한다 | M | ① `CmdApprove`: 구역 선택 → 승인자·일시 → SHR_ApprovedBy/Date, 상태 6·7 (판정이 덮어쓰지 않음, ui_spec §6) ② `CmdReport`: `shoring/templates` 서식에 구역 판정표·공시체·센서·지지층·하중비 채워 PDF ③ 데이터 폴더 CSV 줄마다 앞 줄 해시를 잇는 `audit.csv` (표준 라이브러리 hashlib / C# SHA256) → [감사 기록] 내보내기 |
| G7 | **층간 하중 분배 해석** — 단순법(Grundy–Kabaila) (ACI 347.2R [ACI], Apatos [AP1], 개별 동바리 하중 [BU16a][BU16b], 압축성 동바리 [NBS84]) | 재받침(상태 8)을 쓰는 현장이나 존치 층수를 줄이자는 요구에, 가이드라인 하중비교만으로는 층별 실제 부담을 설명하기 어렵다 | M | `engine.py` 에 `simple_method(n_shored, n_reshored, cycle, f_t)` → 층별 하중비 표 (참고 값, 판정 게이트는 그대로 가이드라인). 대시보드·보고서에 "참고: 단순법" 열. 연구 결과와 대조할 예제는 ACI 문서 예제를 원문 확인 후 사용 [미확인] |
| G8 | **원격·여러 현장 대시보드** (A [GI3], D [MX3]) | 본사·감리단이 현장 밖에서 본다 | L | 범위 밖 유지 권장. 대신 웹 공유판 HTML 을 날마다 내보내 사내 메신저·메일로 돌리는 절차를 도움말에 적음 (docs) |
| G9 | **계획 대비 실적 4D** — 기준선 대비 실제, 이른/늦은 작업 색 (G [SY1], TimeLiner 이른·늦은 외관 [NW2]) | "몇 층이 계획보다 며칠 늦는지, 해체가 판정보다 앞서 잡혔는지"를 한눈에. S5(승인 전 해체 경고)를 화면으로 보여 줌 | M | `schedule.py` 가 이미 계획·실적을 함께 가짐 → 웹 4D JSON 에 `planned_start/actual_start` 추가, 늦음 빨강 테두리·이름 옆 "+3일". Revit 4D 는 같은 값으로 `FourDGraphics` 테두리만 |
| G10 | **센서·구역 QR 라벨, 사진 첨부** — QR 센서 등록 (A [GI1], COMMAND Center [CC1]), QR 로 이어진 점검 기록 (Avontus ScaffoldIQ [AV1]) | 현장에서 구역·센서 ID 를 고르다 틀리는 것을 줄이고, 설치 검측 사진(`install_inspection.csv` photo_path)을 남긴다 | S | 이미 있는 `Qr.cs`·`field_server.py` QR 로 `?t=…&zone=9F-A2` 주소 라벨을 구역·센서마다 인쇄 (보고서 부록) → 화면이 그 구역을 미리 고름. 사진은 `POST /api/photo` (크기 제한, 파일명 = client_id) |
| G11 | 배합별 성능 비교·AI 배합 (B 시나리오 비교 [DK1], C Mix AI [CV1]) | 저탄소·조강 배합 선택에 쓰이지만 해체 판정의 핵심은 아님 | L | 범위 밖. 대신 판정 결과 CSV 에 배합·기온·도달일을 남겨 나중에 분석 가능하게 (이미 대부분 남음, 문제정의 §5-4) |

---

## 5. 우선순위 개선 10가지 (효과 × 노력)

효과 = 현장 기술자·감리 고충과 평가 기준(성실성·창의성·적용가능성) 기여, 노력 = §4 크기.

| 순위 | 개선 | 부족 ID | 효과 | 노력 | 부품 | 끝났다고 볼 기준 |
|---|---|---|---|---|---|---|
| 1 | 승인 기록 + 해체 검측요청서(감리 검측요청 서식 준용 — 주택 별지 6호·공공 별지 36호) + 감사 해시 기록 | G6 | 높음 — 감리 승인 흐름이 제품 정의(§0)의 마지막 고리, 지금 "준비 중" | M | add-in (`CmdApprove`·`CmdReport`·`CmdAuditLog`) + engine(해시) + docs(서식) | 승인 → 상태 6, PDF 에 공시체·센서·지지층·하중비·사유, audit.csv 한 줄 고치면 검증이 실패 |
| 2 | 상태 변화 알림 (휴대폰 화면 배지 + 대시보드) | G1 | 높음 — 제품 대부분이 가진 기본 기능, 공시체 시험일 놓침 방지 | S | engine(`alerts`) + field input(`/api/alerts`, 화면) | 판정일을 하루 넘기면 "9F-A2 해체 승인 요청 가능", "10F 공시체 시험 필요"가 뜸 |
| 3 | 저온·급랭 경보 | G4 | 높음 — 겨울 시나리오(S3)·화정형 서사와 바로 연결 | S | engine(evaluate 경고) → add-in 대시보드 | 샘플에 영하 센서 줄을 넣으면 그 구역 경고 + 시나리오 표 1행 추가 |
| 4 | 시험실 성숙도 보정곡선 입력 | G2 | 높음 — 강도 추정 근거를 [가정] 계수에서 실측 곡선으로 | M | engine(Python → C# 일치 시험) + field input(보정 쌍 입력은 선택) | 보정 CSV 가 있으면 그 곡선, 없으면 기존 값과 바이트 동일 |
| 5 | 단기 예보 반영 예측 도달일 | G3 | 중간 — 다음 주 공정·인력 계획 | S–M | engine(weather) + add-in(예보 갱신 버튼) | `predicted_due` 에 "예보 반영" 표시, 예보 없으면 지금과 같음 |
| 6 | 계획 대비 실적 4D 표시 | G9 | 중간 — 발표 시연 효과 큼, S5 를 화면으로 | M | web 4D + add-in(`FourDGraphics`) | 늦은 층 빨강 테두리 + "+n일", 승인 전 해체 공정은 경고 색 |
| 7 | 센서 업체 CSV 가져오기 (최소 1개 업체) | G5 | 중간 — "현장 장비 그대로 쓴다"는 적용가능성 근거 | S | field input / tools (`import_vendor.py`) → C# 데이터 가져오기 | 업체 샘플 파일 1개 → sensors.csv 변환, 같은 값 검사. 샘플 파일 확보가 선행 [미확인] |
| 8 | 구역·센서 QR 라벨 + 설치 검측 사진 | G10 | 중간 — 입력 편의(사용자 최우선 요구 §3.7-2) | S | field input + add-in(라벨 시트) | 라벨 스캔 → 구역이 미리 선택된 화면, 사진이 install_inspection 줄에 연결 |
| 9 | 단순법 층간 하중 참고 계산 | G7 | 중간 — 재받침·존치 층수 논의에 공학 근거 | M | engine → 보고서 열 | ACI 예제 재현(원문 확인 후), 판정 게이트는 변경 없음 |
| 10 | 벤치마크 근거를 발표·도움말에 반영 | §3 | 중간 — 창의성 평가에 "기존 제품과 무엇이 다른가" 답 | S | docs (`presentation_outline.md`, `docs/help`) | 차별점 1·2·3 과 부족 G8·G11 을 "범위 밖"으로 명시한 슬라이드 1장 |

하지 말 것 (범위 밖 유지): 클라우드 서버·원격 다현장 대시보드(G8), 배합 AI(G11), 센서 하드웨어 자체 개발, 자동 해체 지시 문구 (§3.7 ⑥ 원칙).

---

## 6. 연구 동향 요약 (최근 약 8년, 확인한 것만)

| 주제 | 연구 | 요점 | 우리에게 주는 것 |
|---|---|---|---|
| BIM + 센서로 탈형 시기 | Hamooni 외 2020, Appl. Sci. 10(3) 1085 [HM1] | Revit 애드인이 센서 온도를 받아 성숙도 → 부재에 표시 → 탈형 시기 계산, IFC 로 저장. 사례에서 BS 8110 존치일보다 4.4일 빨랐다고 보고 | 우리와 가장 가까운 선행 연구. 국내 규정 게이트·공시체·구역 최소·다층 검토는 없음 → 차별점 근거 |
| 센서로 4D 양생 기간 갱신 | Funtik 외 ISARC 2018 [FT18] | 센서 도달 시점으로 4D 의 탈형 작업 기간을 자동 갱신하자는 제안 | §3.7-1 "실적 반영 재계산"과 같은 방향 |
| 디지털 트윈 (센서 + FEM) | ISARC 2022 바르셀로나 [IS22] | 블루투스 앱 → BIM → 비선형 FEM, 탈형·긴장 시점에 적용 | FEM 은 범위 밖, "모바일 → BIM" 흐름은 우리와 같음 |
| BIM-IoT 원격 강도 | Iqbal 외 2024, Ain Shams Eng. J. [IQ24] | 저가 센서 + Firebase + BIM 디지털 트윈, 재받침 해체 시점 판단 언급 | 클라우드형의 예. 우리는 LAN 형 |
| 센서 기반 의사결정 지원 | Boix-Cots 외 2025, Autom. Constr. [BC25] | 무선 센서 50개 고가 사례에서 성숙도법 도입의 비용·탄소 효과 산정 | 효과 지표(비용·탄소)를 보고서에 넣을 때 참고 |
| 가설지지 BIM 자동 생성 + 4D | ISARC 2026 (Revit + Dynamo) [IS26] | 보·슬래브에서 동바리 자동 생성, 설치·재받침·해체 4D, 물량·비용. 구조 검증은 향후 과제로 남김 | 우리 01_Generate·4D 와 같은 구성, 우리는 강도 판정이 더 있음 |
| 층간 하중·최적화 | Buitrago 외 2015 Eng. Struct. [BU15], 2016 [BU16a][BU16b], 2020 ASCE [BU20] | 층·동바리 하중 계산 도구와 최적화(동바리 비용 최대 53 % 절감 사례), 개별 동바리 하중, 하중 제한 장치 실험 | G7 단순법 참고 계산의 근거 |
| 재받침 고전 방법 | ACI 347.2R 해설 [ACI], NBS 1984 압축성 동바리 해석 [NBS84] | Grundy–Kabaila 단순법과 그 한계(동바리 무한 강성 가정) | G7 을 "참고 값"으로만 두는 이유 |
| 국내: 하중 감지 동바리 | 서형석 외, 안전문화연구 [KCI1] (연도 [미확인]) | 동바리 하중 변화를 실시간 감지해 존치 기간·무단 철거 책임 확인 | 동바리 하중 센서는 범위 밖이지만, "무단 해체 기록" 문제의식은 G6 과 같음 |
| 국내: 성숙도 탈형 강도 | 유재강 외 2018 한국콘크리트학회 [KCI2], 이한승 외 2014 JACT 무선센서 양생 관리 [JACT14] | 적산온도로 현장타설 탈형 강도 평가, 거푸집 일체형 무선 센서로 탈형 판단 지원 | 국내에서 성숙도법 현장 적용 근거 |
| 국내: 시공단계 해석 | 이태규 2013 한국콘텐츠학회논문지 [LT13], 김선영 외 2001 대한건축학회 [KS01] | 거푸집·동바리 설치·제거·재설치를 고려한 시공단계 해석 시스템 | 다층 하중 검토(G7)의 국내 선행 |
| 국내: BIM + 동바리 해체 판정 | — | 이번 검색에서 BIM 위치 매핑 + 공시체 + 국내 해체 기준을 묶은 국내 논문은 찾지 못함 [미확인: KCI·DBpia 전수 검색 아님] | 차별점 주장 시 "확인한 범위에서"라고 말할 것 |

---

## 7. 출처

| ID | 출처 | URL |
|---|---|---|
| GI1 | Giatec SmartRock 제품 페이지 | https://www.giatecscientific.com/products/concrete-sensors/smartrock-maturity-meter/ |
| GI2 | Giatec 강도·성숙도 솔루션 (보정 4가지 방법, 탈형) | https://www.giatecscientific.com/solutions/concrete-strength-and-maturity/ |
| GI3 | Giatec SmartRock Web | https://www.giatecscientific.com/products/software/smartrock-web/ |
| GI4 | Giatec SmartRock Pro (무보정 센서) | https://www.giatecscientific.com/products/concrete-sensors/smartrock-pro-self-calibrating-sensor/ |
| MC1 | 맥아리스 (국내 대리점) SmartRock 소개 | http://www.mcaris.co.kr/44/?bmode=view&idx=17344344 |
| DK1 | Doka Concremote 제품 페이지 | https://www.doka.com/en/system-groups/doka-system-components/concremote-hardware/index |
| DK2 | Doka Concremote 사용자 정보 PDF (보정·재받침·DIN) | https://direct.doka.com/_ext/downloads/downloadcenter/999808402_2024_11_print.pdf |
| DK3 | Doka 보도 "Construction 4.0 – going digital with Concremote" (2016) | https://www.doka.com/en/news/press/construction-4-0-digital-concremote |
| CV1 | Converge ConcreteDNA | https://www.converge.io/intelligence-platform/concrete-dna |
| CV2 | Converge 보정 도움말 | https://www.converge.io/help-article/calibrate-sensor |
| CV3 | Converge 사용 사례 (BIM 디지털 트윈) | https://www.converge.io/use-cases |
| CV4 | Converge Signal 센서 | https://www.converge.io/sensors/converge-signal |
| UK1 | UKGBC ConcreteDNA 사례 | https://ukgbc.org/resources/concrete-dna/ |
| NCE | New Civil Engineer, Gammon 디지털 트윈 (2021) | https://www.newcivilengineer.com/innovative-thinking/digital-twin-helps-speed-concrete-casting-in-hong-kong-05-05-2021/ |
| MX1 | Maturix Software | https://maturix.com/services/maturix-software/ |
| MX2 | Maturix In-situ 공개 API | https://maturix.com/help/access-maturix-in-situ-data-through-api/ |
| MX3 | Maturix Nova 앱·데스크톱 | https://maturix.com/help/software/maturix-nova |
| MX4 | Maturix 소프트웨어 갱신 내역 (예측·PDF) | https://maturix.com/help/software-updates/ |
| MX5 | Chryso Maturix | https://www.au.chryso.com/concrete-monitoring-with-chrysomaturix/ |
| KR2 | Kryton Maturix 사례 (위치별 18시간 차이) | https://blog.kryton.com/2019/09/case-study-of-using-maturix-smart-concrete-sensor-technology-to-estimate-concrete-age-onsite/ |
| PO1 | 경기일보, 포스코이앤씨 POS-Icon·스마트 양생관리 (2025-03-11) | https://www.kyeonggi.com/article/20250311580021 |
| PO2 | kdsn 기획기사, 포스코이앤씨 안전경영 (2025-04) | https://www.kdsn.co.kr/mobile/article.html?no=46660 |
| PO3 | 데일리한국, 포스코이앤씨 스마트 양생시스템 (2024-07-12) | https://daily.hankooki.com/news/articleView.html?idxno=1105188 |
| NB1 | 전자신문, 네이블–포스코건설 스마트 양생관리 (2022-09-21) | https://www.etnews.com/20220921000033 |
| NB2 | 네이블 블로그, SMARTRI 스마트 양생관리 | https://m.blog.naver.com/nable_story/223340861725 |
| NB3 | 매일일보, 현대건설·네이블 적산온도관리시스템 (2019-03-17) | https://www.m-i.kr/news/articleView.html?idxno=504735 |
| DW1 | 연합뉴스, 대우건설 IoT 온도이력 추종 양생 (2025-01-15) | https://www.yna.co.kr/view/AKR20250115105700003 |
| DW2 | 매일경제, 같은 기술 (2025-01-15) | https://www.mk.co.kr/news/realestate/11218784 |
| DQ1 | 폴리뉴스, 대우건설 BaroQ 앱 (2018-12-24) | https://www.polinews.co.kr/news/articleView.html?idxno=375954 |
| DQ2 | 서울경제, 데키스트 스마트 양생 솔루션 (2023-07-27) | https://www.sedaily.com/article/13738942 |
| DC1 | Doka, DokaCAD for Revit 자동 거푸집 계획 (독일어) | https://www.doka.com/de-CH/news/xpress/Automatisierte_Schalungsplanung_mit_DokaCAD_for_Re |
| DC2 | Doka, DokaCAD for Revit 제품 페이지 | https://www.doka.com/en/solutions/services/dfds/dfds-planning-software/dokacad-for-revit-formwork-planning-software |
| PR1 | PERI CAD 브로셔 (사이클·최대 물량·IFC) | https://www.peri.se/dam/jcr:3ce5616f-c59e-353c-b84d-303b04dede60/peri-cad.pdf |
| BF1 | BIM²form (여러 제조사, Revit) | https://bim2.eu/vendor-neutral-formwork-revit/ |
| UL1 | ULMA Studio for Revit (2020) | https://www.ulmaconstruction.com/en-us/ulma/news/ulma-studio-for-revit-available |
| AV1 | Avontus 제품 정보 페이지 (Designer·ScaffoldIQ) | https://www.avontus.com/llm-info-page/ |
| SY1 | Bentley SYNCHRO 4D | https://www.bentley.com/en/products/synchro-4d/ |
| SY2 | Cad Connect, SYNCHRO 4D·Field 소개 | https://www.cadconnect.com/en/products/synchro-4d/ |
| NW1 | Autodesk Navisworks TimeLiner 작업 흐름 | https://help.autodesk.com/cloudhelp/2025/ENU/Navisworks-Timeliner/files/GUID-96D92B8A-CD9D-4E25-A549-0EB2BF15B5CE.htm |
| NW2 | Autodesk TimeLiner 작업 유형 (이른·늦은 외관) | https://help.autodesk.com/cloudhelp/2023/ENU/Navisworks-Timeliner/files/GUID-B8A2405D-0875-4F40-AC7A-E38C1C129CBF.htm |
| WK1 | Wake HardTrack Cloud Sensor 주요 기능 PDF | https://www.wakeinc.com/assets/brochures/HardTrack-Cloud-Sensor-II-Key-Features.pdf |
| WK2 | Wake HardTrack Precast PDF (Nurse-Saul·Arrhenius) | https://www.wakeinc.com/assets/brochures/HardTrack-for-Precast-2024.pdf |
| CC1 | COMMAND Center 홈 | https://www.commandcenterconcrete.com/ |
| CC2 | Transtec COMMAND Center 소프트웨어 | https://www.thetranstecgroup.com/resources/software/command-center-software/ |
| HL1 | Hilti Concrete Sensors 출시 (PR Newswire 2020) | https://www.prnewswire.com/news-releases/hilti-concrete-sensors-system-delivers-an-end-to-end-solution-for-monitoring-concrete-301131245.html |
| HL2 | Hilti Concrete Sensors 앱 (Google Play) | https://play.google.com/store/apps/details?id=com.hilti.mobile.concretesensors&hl=en_US |
| HM1 | Hamooni 외 (2020) Extending BIM Interoperability for Real-Time Concrete Formwork Process Monitoring | https://doi.org/10.3390/app10031085 |
| FT18 | Funtik 외 (ISARC 2018) | https://www.iaarc.org/publications/fulltext/ISARC2018-Paper036.pdf |
| IS22 | ISARC 2022 Paper 59, 성숙도 계측–FEM 파이프라인 | https://www.iaarc.org/publications/fulltext/029_ISARC%202022_Paper_59.pdf |
| IS26 | ISARC 2026, BIM 기반 가설지지 비용·일정 자동 산정 | https://www.iaarc.org/publications/fulltext/ISARC2026_1437.pdf |
| IQ24 | Iqbal 외 (2024) BIM-IoT integration for remote real-time concrete compressive strength monitoring | https://doi.org/10.1016/j.asej.2024.102863 |
| BC25 | Boix-Cots 외 (2025) Sensor data-driven decision support system… | https://doi.org/10.1016/j.autcon.2025.106685 |
| AP1 | Apatos Reshoring (Devpost, Revit 애드인 해커톤 작품) | https://devpost.com/software/apatos-reshoring |
| BU15 | Buitrago 외 (2015) Engineering Structures | https://doi.org/10.1016/j.engstruct.2015.06.007 |
| BU16a | Buitrago 외 (2016) 개별 동바리 하중 추정 | https://perfilesycapacidades.javeriana.edu.co/en/publications/estimating-loads-on-shores-during-the-construction-of-rc-building/ |
| BU16b | Buitrago 외 (2016) Maximum loads on shores | https://doi.org/10.1680/jstbu.15.00089 |
| BU20 | Buitrago 외 (2020) Load Limiters on Temporary Shoring Structures | https://ascelibrary.com/doi/10.1061/%28ASCE%29ST.1943-541X.0002948 |
| ACI | ACI University, Shoring and Reshoring for Multistory Concrete Buildings (ACI 347 해설) | https://aciuniversity.concrete.org/AssetListing/Shoring-and-Reshoring-for-Multistory-Concrete-Buildings-1631/Shoring-and-Reshoring-for-Multistory-Concrete-Buildings-Formwork-economy-and-safety-must-go-together-PDF-3823 |
| NBS84 | Gross, NBSIR 84-2964 압축성 동바리 해석 | https://nvlpubs.nist.gov/nistpubs/Legacy/IR/nbsir84-2964.pdf |
| KCI1 | 서형석 외, 콘크리트 양생 중 하중변화를 실시간으로 감지하는 지능형 동바리 시스템 (안전문화연구) | https://www.kci.go.kr/kciportal/ci/sereArticleSearch/ciSereArtiView.kci?sereArticleSearchBean.artiId=ART003123620 |
| KCI2 | 유재강 외 (2018) 적산온도에 의한 현장타설 콘크리트의 거푸집 탈형강도 평가 | https://www.auric.or.kr/user/rdoc/DocRdoc.aspx?dn=382805&returnVal=RD_R |
| JACT14 | Lee 외 (2014) Curing Management of Early-age Concrete… Integrated Wireless Sensors, JACT 12(3) | https://www.jstage.jst.go.jp/article/jact/12/3/12_91/_article |
| LT13 | 이태규 (2013) 시공단계를 고려한 고층 RC 건물 해석 시스템 | https://doi.org/10.5392/jkca.2013.13.09.410 |
| KS01 | 김선영 외 (2001) 동바리 설치 및 제거를 고려한 RC 구조물 시공단계 해석 시스템 | http://dbpia.co.kr/Article/NODE00365570 |

### 확인하지 못한 것 [미확인]
- Giatec·Concremote·Maturix 가 도면/BIM 위에 센서 점을 찍는 기능이 있는지 (공개 페이지에 없음, 앱 안에는 있을 수 있음).
- Converge 의 알림·보고서 세부, Concremote 의 API.
- 국내 제품(포스코·네이블·데키스트)이 해체 승인 서류를 만드는지, BIM 과 잇는지 — 보도자료에는 없음.
- 업체별 CSV 내보내기 열 이름 (G5 구현에 필요 — 샘플 파일 확보 필요).
- 기상청 단기예보 API 응답 형식, KCS 14 20 40 한중 콘크리트 보호 강도 수치 (G3·G4 구현 전 원문 확인).
- 국내 학술 DB 전수 검색은 하지 않음 → "국내에 BIM + 공시체 + 해체 기준 묶은 연구 없음"은 "이번 검색 범위에서"로만 말할 것.

---
name: shoring-review
description: 동바리 해체 검토 묶음(판정표 · 요약 · 해체 검측요청서 초안)을 만드는 절차와 서식. 동바리, 해체, 존치, 공시체, 성숙도 센서 판정을 요청받으면 사용.
---

# 동바리 해체 검토 묶음

## 입력 위치
- 계산 코드·문서: `/mnt/session/uploads/project/` — `engine.py`, `evaluate.py`, `project_definition.md`(프로젝트 정의서), `research_notes.md`(근거 자료)
- 데이터: `/mnt/session/uploads/data/` — `zones.csv`, `pours.csv`, `sensors.csv`(선택), `cylinders.csv`(선택), `weather.csv`(선택)

## 순서
1. 업로드는 읽기 전용이므로 작업 폴더로 복사: `cp -r /mnt/session/uploads/project /workspace/p && cp -r /mnt/session/uploads/data /workspace/d`
2. 엔진 자체 검사: `cd /workspace/p && python engine.py` — "self-check OK"가 아니면 멈추고 보고
3. 데이터 점검 (판정 전에): 구역 ID가 zones/pours/cylinders에서 일치하는지, 센서 시각이 타설 이후이고 판정 시각 이전인지, 온도 −20~80 °C, 공시체 f1~f3 0~80 MPa, 같은 구역 공시체 3개의 편차가 평균의 15 % 이내인지. 문제는 `data_issues.md`에 표로 기록
4. 판정: `python evaluate.py /workspace/d --at <판정 시각> --out /mnt/session/outputs/zone_decisions.csv 2>&1 | tee /mnt/session/outputs/engine_run.log` — 명령과 출력 전체를 로그에 남김
5. `summary.md`: 판정 시각, 데이터 출처, 층별 결과 표(구역 · 공시체 평균 · 구역 최소 추정 강도와 위치 · 결정 · 사유 · 예측 도달일), 존치 구역별 다음 조치(언제 무엇을 다시 시험할지), 데이터 문제 요약
6. 해체 검측요청서 초안 `removal_request_<판정일 YYYYMMDD>.docx` (docx 스킬 사용) — "해체 승인 요청 가능" 구역만 포함, 없으면 "요청 대상 없음" 한 장

## 해체 검측요청서 서식 (감리 검측요청 서식 준용 — 법령에 '해체 승인 요청서' 조문은 없음)
1. 제목: 동바리 해체 검측요청서 (초안)
2. 공사 개요: 공사명(입력값, 없으면 빈칸), 판정 시각, 작성: shoring-inspector 초안
3. 요청 구역 표: 구역 · 층 · 타설일 · 재령(일) · 배합 · fck · 해체 기준 강도 · 공시체 시험일·양생 방법·평균 · 센서 구역 최소 추정 강도·위치 · 존치 층수 게이트
4. 판정 근거: 「거푸집 및 동바리 해체 가이드라인」(국토교통부 2023.1, 작성 한국콘크리트학회·국가건설기준센터) — 다층 구조 동바리 해체 시 설계기준압축강도 이상, 타설층 포함 3개층 이상 존치, 강도는 현장양생 공시체로 확인, 감리 승인 후 해체 (정의서 §3.2 문구 그대로)
5. 함께 확인할 사항: 해체 계획서 승인 여부, 해체 전 체크리스트, 출입 통제, 재받침 계획 — 체크 칸만 두고 비워 둠
6. 존치 구역 목록과 사유 (참고)
7. 서명란: 작성자 / 현장대리인 / 감리 — 모두 빈칸
8. 바닥글: "본 문서는 계산 도구가 만든 초안이며 해체 승인이 아님"

## 금지
- evaluate.py 출력과 다른 숫자를 쓰지 않는다 (반올림 자릿수 변경만 허용)
- "승인", "해체하라" 같은 결정 문구를 쓰지 않는다
- 서명란을 채우지 않는다

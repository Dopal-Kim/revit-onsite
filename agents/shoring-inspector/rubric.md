# 동바리 해체 검토 묶음 — 채점 기준 (초안, 팀이 조정)
1. /mnt/session/outputs/ 에 zone_decisions.csv, engine_run.log, data_issues.md, summary.md, removal_request_<YYYYMMDD>.docx 다섯 파일이 모두 있다.
2. engine_run.log 에 `python engine.py`의 "self-check OK"와 `python evaluate.py` 실행 명령·출력이 들어 있다.
3. zone_decisions.csv 는 evaluate.py가 쓴 파일이며, 입력 pours.csv의 모든 구역이 한 줄씩 있다.
4. summary.md 와 docx 에 나오는 모든 강도·날짜·결정 값이 zone_decisions.csv 의 해당 값과 일치한다 (소수 자릿수 차이만 허용).
5. docx 의 요청 구역은 정확히 decision 이 "해체 승인 요청 가능"인 구역들이다. 하나도 없으면 "요청 대상 없음"이라고 적혀 있다.
6. docx 에 SKILL.md 서식의 8개 항목이 모두 있고, 서명란 세 개가 비어 있으며, 바닥글 문구가 있다.
7. 어떤 산출물에도 해체를 승인하거나 지시하는 문장이 없다.
8. summary.md 는 존치 구역마다 사유와 다음 조치(재시험 시점 등)를 적는다.
9. data_issues.md 는 SKILL.md 3단계의 다섯 가지 점검 결과를 각각 적는다 (문제가 없으면 "없음").
10. 데이터에 가상 센서값 표시가 있으면 summary.md 첫머리에 그 사실이 적혀 있다.

# shoring-inspector — Claude Managed Agent

동바리 해체 검토 묶음(판정표 · 데이터 점검 · 요약 · 해체 검측요청서 초안)을 만드는 에이전트. 계산은 `shoring/evaluate.py`(= Dynamo 05_Evaluate와 같은 엔진)가 하고, 에이전트는 실행·점검·서류 작성만 한다. 해체 승인은 하지 않는다.

| 파일 | 역할 |
|---|---|
| `agent.md` | 에이전트 설정 (Claude Opus 5.5, effort high, 기본 도구 bash·파일, 웹 도구 끔, 도구 실행 auto 정책, 스킬 docx + shoring-review). 본문 = 시스템 프롬프트 |
| `environment.yaml` | 클라우드 샌드박스, `limited` 네트워크 (패키지 설치만 허용, 외부 호스트 없음) |
| `skills/shoring-review/SKILL.md` | 검토 절차와 해체 검측요청서 서식 |
| `rubric.md` | 결과물 채점 기준 10개 (outcome 기능이 이 기준을 통과할 때까지 고침, 최대 3회) |
| `run_review.py` | 1회 실행 스크립트: 기상 자료(선택) → 업로드 → 세션 → 진행 출력 → 산출물 내려받기 |

## 1. 준비 (한 번)
1. Claude Console(platform.claude.com) 계정, 결제 설정
2. `ant` CLI 1.30.0 이상 설치 — macOS `brew install anthropics/tap/ant`, 그 외 github.com/anthropics/anthropic-cli/releases
3. `ant auth login` (또는 `ANTHROPIC_API_KEY` 설정), `pip install anthropic`
4. 저장소 루트에서 설정 미리 보기 → 적용 (ant 없이 하려면 대신 `python agents/shoring-inspector/setup_agent.py` — 시작 가이드 6번):
   ```sh
   ant apply --dry-run -v agents/shoring-inspector/agent.md agents/shoring-inspector/environment.yaml
   ant apply agents/shoring-inspector/agent.md agents/shoring-inspector/environment.yaml
   ```
   스킬 `shoring-review`는 agent.md가 참조하므로 같이 올라간다. 생성된 ID는 루트 `claude-lock.json`에 기록됨 → 커밋할 것 (없으면 다음 적용 때 중복 생성).
5. (선택) 기상청 ASOS 일자료: 공공데이터포털에서 활용 신청 후 `KMA_SERVICE_KEY` 환경변수. 키는 로컬에서만 쓰고 세션에는 결과 CSV만 올린다 — 이 API는 키를 URL 쿼리로 받기 때문에 Anthropic 보관함(vault) 치환을 쓸 수 없다.

## 2. 실행
```sh
python shoring/sample/make_sample.py                      # 가상 데이터 (9–12층) 생성
python agents/shoring-inspector/run_review.py shoring/sample/data --at 2026-12-31T09:00 --synthetic --out review_out
```
콘솔 세션 링크가 출력되고, 끝나면 `review_out/`에 `zone_decisions.csv`, `engine_run.log`, `data_issues.md`, `summary.md`, `removal_request_20261231.docx`가 내려온다. 실제 현장 데이터는 같은 열 형식의 CSV 폴더를 넘기고 `--synthetic`을 뺀다.

설정을 바꾸려면 파일을 고치고 4번 `ant apply`를 다시 실행 (새 버전이 생기고 이후 세션이 사용).

## 3. 비용
- 세션마다 `--budget-usd` (기본 5달러) 상한. 넘으면 세션이 멈춘다.
- Claude Opus 5.5 목록가 입력 $4 / 출력 $20 per 100만 토큰 + 세션 실행 시간 $0.08/시간. 실제 1회 비용은 아직 측정 안 함 — 첫 실행 후 콘솔에서 확인.

## 4. 확인 안 된 것
- 이 컨테이너에서는 `ant`·API 키가 없어 실제 적용·세션 실행은 하지 못했다. SDK 메서드 이름은 anthropic 2.3.0으로 확인.
- 기상청 엔드포인트 이름(`getWthrDataList`)과 응답 구조는 2차 자료 기준. 실패하면 평년 기온으로 계산된다.
- 정기 실행(매일 아침)은 데이터를 매번 올려야 해서 넣지 않았다. 데이터가 GitHub 저장소에 쌓이는 구조가 되면 deployment로 바꿀 수 있다.

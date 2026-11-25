"""동바리 해체 검토 세션 1회 실행: 기상 자료 받기(선택) → 파일 업로드 → 세션 생성 → 결과물(outcome) 요청 → 진행 출력 → 산출물 내려받기.

사전 준비: `ant apply` 로 agent.md·environment.yaml 적용 (README.md 참고), `pip install anthropic`, `ant auth login` 또는 ANTHROPIC_API_KEY.
실행 예: python agents/shoring-inspector/run_review.py shoring/sample/data --at 2026-12-31T09:00 --synthetic
산출물(--out 폴더)의 zone_decisions.csv 를 Dynamo 04_Data 스크립트가 읽어 Revit 구역 매개변수에 넣는다.
"""
import argparse
import csv
import json
import os
import time
import urllib.parse
import urllib.request
from datetime import date, datetime, timedelta

import anthropic

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
PROJECT_FILES = {
    "engine.py": os.path.join(ROOT, "shoring", "engine.py"),
    "evaluate.py": os.path.join(ROOT, "shoring", "evaluate.py"),
    "project_definition.md": os.path.join(ROOT, "동바리검측_프로젝트구상.md"),
    "research_notes.md": os.path.join(ROOT, "shoring", "research_notes.md"),
}
DATA_FILES = ["zones.csv", "pours.csv", "sensors.csv", "cylinders.csv", "weather.csv"]
BETA = ["managed-agents-2026-04-01"]


def resource_ids():
    """AGENT_ID / ENV_ID 환경변수 → agent_ids.json (setup_agent.py) → claude-lock.json (ant apply) 순서."""
    agent, env = os.environ.get("AGENT_ID"), os.environ.get("ENV_ID")
    lock = os.path.join(ROOT, "claude-lock.json")
    ids = os.path.join(HERE, "agent_ids.json")          # setup_agent.py 가 기록
    if (not agent or not env) and os.path.exists(ids):
        d = json.load(open(ids, encoding="utf-8"))
        agent, env = agent or d.get("agent_id"), env or d.get("environment_id")
    if (not agent or not env) and os.path.exists(lock):
        res = json.load(open(lock, encoding="utf-8")).get("resources", {})
        for path, v in res.items():
            if path.endswith("shoring-inspector/agent.md"):
                agent = agent or v["id"]
            if path.endswith("shoring-inspector/environment.yaml"):
                env = env or v["id"]
    if not agent or not env:
        raise SystemExit("agent·environment ID 없음: setup_agent.py (또는 ant apply) 를 먼저 실행하세요.")
    return agent, env


def fetch_weather(start, end, station, path):
    """기상청 ASOS 일자료 (공공데이터포털). 키는 로컬 환경변수에만 두고 세션에는 결과 CSV만 올린다.
    shortcut: 엔드포인트 이름(getWthrDataList)과 응답 구조는 2차 자료 기준 — 실패하면 평년 기온으로 계산된다."""
    key = os.environ.get("KMA_SERVICE_KEY")
    if not key:
        return False
    q = urllib.parse.urlencode({"serviceKey": key, "pageNo": 1, "numOfRows": 400, "dataType": "JSON", "dataCd": "ASOS",
                                "dateCd": "DAY", "startDt": start.strftime("%Y%m%d"), "endDt": end.strftime("%Y%m%d"),
                                "stnIds": station})
    try:
        with urllib.request.urlopen(f"https://apis.data.go.kr/1360000/AsosDalyInfoService/getWthrDataList?{q}", timeout=20) as r:
            items = json.load(r)["response"]["body"]["items"]["item"]
        with open(path, "w", encoding="utf-8", newline="") as f:
            w = csv.writer(f)
            w.writerow(["date", "avgTa"])
            for it in items:
                if it.get("avgTa") not in (None, ""):
                    w.writerow([it["tm"], it["avgTa"]])
        return True
    except Exception as ex:   # 기상 자료는 선택 입력: 실패해도 검토는 진행
        print(f"[기상] 받기 실패, 평년 기온 사용: {ex}")
        return False


def upload(client, path, name, mount):
    with open(path, "rb") as fh:
        f = client.files.upload(file=(name, fh))
    return {"type": "file", "file_id": f.id, "mount_path": mount}


def drive(client, session_id, event):
    """스트림을 먼저 열고 event 를 보낸 뒤, 에이전트가 멈출 때까지 진행을 출력. 확인이 필요한 도구 실행은 사람에게 묻는다."""
    with client.beta.sessions.events.stream(session_id=session_id) as stream:
        client.beta.sessions.events.send(session_id=session_id, events=[event])
        for ev in stream:
            if ev.type == "agent.message":
                print("".join(b.text for b in ev.content if b.type == "text"), flush=True)
            elif ev.type == "span.outcome_evaluation_end":
                print(f"[채점 {ev.iteration}] {ev.result}: {ev.explanation}")
            elif ev.type in ("agent.tool_use", "agent.mcp_tool_use") and getattr(ev, "evaluated_permission", None) == "ask":
                print(f"[확인 필요] {ev.name}: {json.dumps(ev.input, ensure_ascii=False)[:400]}")
                ok = input("허용할까요? (y/N) ").strip().lower() == "y"
                client.beta.sessions.events.send(session_id=session_id, events=[
                    {"type": "user.tool_confirmation", "tool_use_id": ev.id, "result": "allow" if ok else "deny"}])
            elif ev.type == "session.status_idle":
                if ev.stop_reason.type != "requires_action":
                    print(f"[멈춤] {ev.stop_reason.type}")
                    return
            elif ev.type == "session.status_terminated":
                print("[종료] terminated")
                return


def download(client, session_id, out):
    """에이전트가 /mnt/session/outputs/ 에 쓴 파일을 out 폴더로."""
    files = []
    for _ in range(5):   # 산출물 색인까지 몇 초 걸림
        files = list(client.beta.files.list(scope_id=session_id, betas=BETA))
        if files:
            break
        time.sleep(2)
    os.makedirs(out, exist_ok=True)
    for f in files:
        client.beta.files.download(f.id, betas=BETA).write_to_file(os.path.join(out, f.filename))
        print("받음:", os.path.join(out, f.filename))
    if not files:
        print("받을 파일 없음")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("data", help="zones/pours/sensors/cylinders CSV 폴더")
    ap.add_argument("--at", required=True, help="판정 시각 YYYY-MM-DDTHH:MM")
    ap.add_argument("--out", default="review_out")
    ap.add_argument("--project-name", default="")
    ap.add_argument("--station", default="108", help="ASOS 지점 (108 = 서울)")
    ap.add_argument("--synthetic", action="store_true", help="센서 온도가 가상값임을 세션에 알림")
    ap.add_argument("--budget-usd", type=int, default=5, help="세션 비용 상한 (달러, 정수)")
    ap.add_argument("--max-iterations", type=int, default=3)
    a = ap.parse_args()
    at = datetime.fromisoformat(a.at)
    os.makedirs(a.out, exist_ok=True)

    client = anthropic.Anthropic()
    agent_id, env_id = resource_ids()

    resources = [upload(client, p, n, f"/project/{n}") for n, p in PROJECT_FILES.items()]
    for n in DATA_FILES:
        p = os.path.join(a.data, n)
        if os.path.exists(p):
            resources.append(upload(client, p, n, f"/data/{n}"))
    if not os.path.exists(os.path.join(a.data, "weather.csv")):
        pours = list(csv.DictReader(open(os.path.join(a.data, "pours.csv"), encoding="utf-8-sig")))
        first = min(date.fromisoformat(r["pour_date"]) for r in pours)
        wpath = os.path.join(a.out, "weather.csv")
        if fetch_weather(first, min(at.date(), date.today()) - timedelta(1), a.station, wpath):
            resources.append(upload(client, wpath, "weather.csv", "/data/weather.csv"))
    note = os.path.join(a.out, "DATA_NOTE.txt")
    with open(note, "w", encoding="utf-8") as f:
        f.write(("센서 온도는 가상값(실측 아님).\n" if a.synthetic else "센서 온도는 현장 실측값.\n") + f"공사명: {a.project_name or '(미입력)'}\n")
    resources.append(upload(client, note, "DATA_NOTE.txt", "/data/DATA_NOTE.txt"))

    session = client.beta.sessions.create(
        agent=agent_id, environment_id=env_id, resources=resources,
        title=f"동바리 해체 검토 {at:%Y-%m-%d %H:%M}",
        budget={"type": "limit", "max_list_cost": {"amount": str(a.budget_usd * 100), "currency": "USD"}},
    )
    print(f"세션: https://platform.claude.com/workspaces/default/sessions/{session.id}")

    rubric = open(os.path.join(HERE, "rubric.md"), encoding="utf-8").read()
    outcome = {"type": "user.define_outcome",
               "description": f"판정 시각 {a.at} 기준으로 /mnt/session/uploads/data/ 의 데이터에 대한 동바리 해체 검토 묶음을 "
                              f"shoring-review 스킬 절차대로 /mnt/session/outputs/ 에 만든다. 판정 시각 인자: --at {a.at}",
               "rubric": {"type": "text", "content": rubric}, "max_iterations": a.max_iterations}

    drive(client, session.id, outcome)
    download(client, session.id, a.out)

if __name__ == "__main__":
    main()

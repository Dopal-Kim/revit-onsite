"""에이전트와 자유 대화 (명령을 직접 입력). 프로젝트 문서·엔진은 자동으로 올라가고, --data 폴더와 --attach 파일을 추가로 올릴 수 있다.

실행 예:
  python agents/shoring-inspector/chat.py
  python agents/shoring-inspector/chat.py --data shoring/sample/data --attach dynamo/00_setup.py
대화 중: 그냥 입력 = 에이전트에게 지시 · /받기 = 에이전트가 만든 파일 내려받기 · /끝 = 종료
"""
import argparse
import os

import anthropic

from run_review import DATA_FILES, PROJECT_FILES, download, drive, resource_ids, upload

HELP = """명령: 그냥 입력 = 지시 · /받기 = 만든 파일 내려받기(--out 폴더) · /끝 = 종료
올린 파일 위치 — 프로젝트: /mnt/session/uploads/project/  데이터: /mnt/session/uploads/data/  첨부: /mnt/session/uploads/attach/"""


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--data", help="CSV 폴더 (선택)")
    ap.add_argument("--attach", nargs="*", default=[], help="같이 올릴 파일 (Dynamo 코드, 오류 로그, CSV 등)")
    ap.add_argument("--out", default="chat_out")
    ap.add_argument("--budget-usd", type=int, default=3)
    a = ap.parse_args()

    client = anthropic.Anthropic()
    agent_id, env_id = resource_ids()
    resources = [upload(client, p, n, f"/project/{n}") for n, p in PROJECT_FILES.items()]
    if a.data:
        resources += [upload(client, os.path.join(a.data, n), n, f"/data/{n}") for n in DATA_FILES
                      if os.path.exists(os.path.join(a.data, n))]
    resources += [upload(client, p, os.path.basename(p), f"/attach/{os.path.basename(p)}") for p in a.attach]
    session = client.beta.sessions.create(
        agent=agent_id, environment_id=env_id, resources=resources, title="동바리 검측 대화",
        budget={"type": "limit", "max_list_cost": {"amount": str(a.budget_usd * 100), "currency": "USD"}})
    print(f"세션: https://platform.claude.com/workspaces/default/sessions/{session.id}\n{HELP}")

    while True:
        text = input("\n나> ").strip()
        if not text:
            continue
        if text == "/끝":
            break
        if text == "/받기":
            download(client, session.id, a.out)
            continue
        drive(client, session.id, {"type": "user.message", "content": [{"type": "text", "text": text}]})


if __name__ == "__main__":
    main()

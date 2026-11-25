"""ant 없이 Python으로 에이전트 등록·갱신. agent.md · environment.yaml · skills/shoring-review/ 를 읽어 그대로 보낸다.
생성된 ID는 agent_ids.json 에 저장 (비밀 아님, 커밋해서 팀이 같이 씀). 다시 실행하면 같은 에이전트를 새 버전으로 갱신.

실행: python agents/shoring-inspector/setup_agent.py            (등록·갱신)
      python agents/shoring-inspector/setup_agent.py --dry-run  (보낼 내용만 출력, API 호출 없음)
      python agents/shoring-inspector/setup_agent.py --skill-only  (스킬만 올림 — Console 화면에서 에이전트를 만들 때)
필요: pip install anthropic pyyaml, ANTHROPIC_API_KEY
"""
import hashlib
import json
import os
import sys

import yaml

HERE = os.path.dirname(os.path.abspath(__file__))
IDS = os.path.join(HERE, "agent_ids.json")
SKILL_DIR = os.path.join(HERE, "skills", "shoring-review")


def load_agent():
    """agent.md: --- YAML 설정 --- 본문 = 시스템 프롬프트."""
    _, fm, body = open(os.path.join(HERE, "agent.md"), encoding="utf-8").read().split("---", 2)
    cfg = yaml.safe_load(fm)
    cfg["system"] = body.strip()
    return cfg


def skill_files():
    """Skills API 형식: 최상위 폴더 하나 안에 SKILL.md."""
    out = []
    for root, _, names in os.walk(SKILL_DIR):
        for n in sorted(names):
            p = os.path.join(root, n)
            out.append((f"shoring-review/{os.path.relpath(p, SKILL_DIR).replace(os.sep, '/')}", open(p, "rb").read()))
    return out


def main(dry, skill_only=False):
    ids = json.load(open(IDS, encoding="utf-8")) if os.path.exists(IDS) else {}
    env_cfg = yaml.safe_load(open(os.path.join(HERE, "environment.yaml"), encoding="utf-8"))
    cfg = load_agent()
    files = skill_files()
    digest = hashlib.sha256(b"".join(n.encode() + b for n, b in files)).hexdigest()
    assert any(n == "shoring-review/SKILL.md" for n, _ in files), "SKILL.md 없음"
    if dry:
        print("environment:", env_cfg)
        print("skill files:", [n for n, _ in files])
        print("agent:", {k: v for k, v in cfg.items() if k != "system"}, "| system", len(cfg["system"]), "자")
        print("기존 ID:", ids or "없음 (새로 생성)")
        return

    import anthropic
    client = anthropic.Anthropic()
    if "environment_id" not in ids and not skill_only:     # 환경 설정을 바꾸려면 agent_ids.json 에서 environment_id 를 지우고 다시 실행
        ids["environment_id"] = client.beta.environments.create(name="shoring-inspector-env", config=env_cfg["config"]).id
    if ids.get("skill_hash") != digest:  # 스킬 파일이 바뀐 경우에만 새 버전
        if "skill_id" in ids:
            client.skills.versions.create(ids["skill_id"], files=files)
        else:
            ids["skill_id"] = client.skills.create(files=files, display_name="shoring-review").id
        ids["skill_hash"] = digest
    if skill_only:
        with open(IDS, "w", encoding="utf-8") as f:
            json.dump(ids, f, indent=2)
        print(f"스킬 {ids['skill_id']} 업로드됨 → Console 에이전트 화면의 Skills 에서 shoring-review 를 고르세요")
        return
    skills = [{"type": "custom", "skill_id": ids["skill_id"], "version": "latest"} if s == "./skills/shoring-review" else s
              for s in cfg["skills"]]
    fields = {k: cfg[k] for k in ("name", "description", "model", "tools", "system")} | {"skills": skills}
    if "agent_id" in ids:
        agent = client.beta.agents.update(ids["agent_id"], version=ids["agent_version"], **fields)
    else:
        agent = client.beta.agents.create(**fields)
    ids["agent_id"], ids["agent_version"] = agent.id, agent.version
    with open(IDS, "w", encoding="utf-8") as f:
        json.dump(ids, f, indent=2)
    print(f"에이전트 {agent.id} (버전 {agent.version}), 환경 {ids['environment_id']}, 스킬 {ids['skill_id']} → {IDS}")


if __name__ == "__main__":
    main("--dry-run" in sys.argv, "--skill-only" in sys.argv)

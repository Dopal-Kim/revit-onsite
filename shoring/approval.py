"""해체 검측요청서 초안(감리 검측요청 서식 준용) + 승인 기록(바꾸면 드러나는 기록) — 표준 라이브러리만 (C# 이식 기준본, benchmark_gap 1순위 G6).

프로그램은 승인하지 않는다. 결론은 evaluate 의 "해체 승인 요청 가능"/"존치" 뿐이고, 승인은 감리가 한다.
이 모듈은 ① 요청 가능 구역의 근거를 모은 요청서 초안(HTML, 인쇄 → PDF, 서명란 빈칸)을 만들고
② 사람이 입력한 행위(요청 제출·감리 승인 기록·해체 완료·재받침)를 approvals.csv 에 이어 쓰며
   각 줄에 앞 줄 해시를 묶어(해시 사슬) 나중에 줄을 고치거나 지우면 verify() 가 찾게 한다.
   evidence_sha = 그때 판정 행(근거)의 해시 → "어떤 근거를 보고 승인했는지"가 같이 남는다.
실행: python approval.py  (자체 검사)
"""
from report_style import REPORT_CSS
import csv
import hashlib
import html
import json
import os
from datetime import datetime, timedelta

LOG = "approvals.csv"
DURING_NOTE = "해체 중: 가이드라인 4.2 체크리스트(해체 순서·추락 방지·보호구) — 관리감독자 현장 확인"   # 고정 문구 (총괄 P1, 가이드라인 공식본 대조) — 요청서 '함께 낼 것' 아래·층 표지 상태 줄 아래, 마감 값에는 안 넣음
FIELDS_V1 = ["seq", "timestamp", "zone_id", "action", "actor", "note", "evidence_sha", "prev_sha", "sha"]
FIELDS = FIELDS_V1 + ["approver", "signed_at", "source"]   # 감리 성명·서명 일시 (총괄 11-05) + 출처 (v6.2 W-A) — 뒤에 붙여 옛 줄·옛 해시는 그대로
SOURCES = ("감리 직접", "종이 옮김")   # W-A 감리 승인 기록 출처: 서버 감리 확인(S3) / 종이 서명을 [승인 기록]으로 옮김
ACTIONS = {"요청 제출": None, "감리 승인 기록": 6, "해체 완료": 7, "재받침": 8}   # 행위 → 상태 코드 (정의서 §3.5), 요청 제출은 상태 안 바꿈
GENESIS = "0" * 64


def source_label(r):
    """감리 승인 기록 출처 (W-A) — source 값, 없으면(옛 줄·[승인 기록] 종이 서명) '종이 옮김'. 다른 행위는 빈칸"""
    return (r.get("source") or "").strip() or ("종이 옮김" if r.get("action") == "감리 승인 기록" else "")


def lot_cell(d):
    """W1: 공시체가 타설 묶음 공통 시험이면 근거표 칸에 '타설 P-9F-1 · 6구역 공통' 한 줄 (f-string 안 백슬래시 없이 — Python 3.11 이하)"""
    import evaluate as ev          # 늦은 import (evidence 가 approval 을 import 함)
    lot = ev.lot_label(d)
    return f'<br><span class="s">{html.escape(lot)}</span>' if lot else ""


EVIDENCE_KEYS = ("zone_id", "decision", "reason", "threshold", "f_cyl", "cyl_test_date", "cyl_curing",
                 "f_est_min", "est_source", "level_gate")


def evidence_sha(row):
    """판정 행 → 근거 해시 (열 순서·실수 표기와 무관하게 같은 값)."""
    d = {k: str(row.get(k, "")) for k in EVIDENCE_KEYS}
    return hashlib.sha256(json.dumps(d, ensure_ascii=False, sort_keys=True).encode("utf-8")).hexdigest()


def line_sha(r):
    """줄 해시 = 옛 9열(sha 앞까지). approver·signed_at 은 값이 있을 때만 뒤에 묶는다 — 옛 사슬이 그대로 이어지고, 새 줄의 성명·서명 일시를
    나중에 지우거나 옛 줄에 써 넣으면 해시가 달라져 드러난다."""
    ext = [r.get("approver") or "", r.get("signed_at") or ""] + ([r["source"]] if r.get("source") else [])   # W-A 출처도 값이 있을 때만
    return hashlib.sha256("|".join([str(r[k]) for k in FIELDS_V1[:-1]] + (ext if any(ext) else [])).encode("utf-8")).hexdigest()


def read_log(folder):
    p = os.path.join(folder, LOG)
    if not os.path.exists(p):
        return []
    with open(p, encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def verify(folder):
    """해시 사슬 검사 → (정상 여부, 문구). 줄을 고치거나 지우거나 순서를 바꾸면 그 줄 번호를 알려 준다."""
    prev = GENESIS
    rows = read_log(folder)
    for i, r in enumerate(rows, 1):
        if r["seq"] != str(i) or r["prev_sha"] != prev:
            return False, f"{LOG} {i}번째 기록: 앞 기록과 이어지지 않음 (지워지거나 순서가 바뀜)"
        if line_sha(r) != r["sha"]:
            return False, f"{LOG} {i}번째 기록 ({r['zone_id']} {r['action']}): 내용이 기록 뒤에 바뀜"
        prev = r["sha"]
    return True, f"기록 {len(rows)}건, 사슬 정상"


DEMO_NOTE = "(시연 시각 — 판정 기준일 고정)"


def record_clock(eval_at, now=None):
    """[승인 기록]·[근거 원장] 기본 시각 — 현장 입력 서버(FieldInput.SyncDate)와 같은 규칙: 마지막 판정 시각(판정일 09:00)이
    지금보다 미래면 시연 자료로 보고 그 시각으로 고정 (반환 (eval_at, True)), 아니면 실제 시계 (반환 (지금, False)).
    실제 프로젝트(판정일 ≤ 오늘)는 늘 실제 시계 — 기록의 핵심은 '언제 실제로 넣었나' (총괄 11-04)."""
    now = now or datetime.now()
    return (eval_at, True) if eval_at is not None and eval_at > now else (now, False)


def record(folder, zone_id, action, actor, decision_row, note="", now=None, eval_at=None, approver="", signed_at="", source=""):
    """사람이 한 행위 한 줄을 이어 쓴다. 검사: 행위 이름, 입력자, 사슬 정상, 감리 승인 기록은 그 시점 판정이 '해체 승인 요청 가능'일 때만.
    now 를 안 주면 record_clock(eval_at): 판정 기준 시각이 미래(시연 자료)면 그 시각 + note 끝에 DEMO_NOTE (해시 사슬 형식은 그대로).
    approver·signed_at(YYYY-MM-DD 또는 YYYY-MM-DDTHH:MM) = 종이에 서명한 감리 성명·일시 — '감리 승인 기록'에만 (서명 스캔 SHA 는 근거 원장 6).
    source = 감리 승인 기록 출처 (SOURCES — 서버 감리 확인은 '감리 직접', 애드인 [승인 기록]은 '종이 옮김', 빈칸 = 옛 방식)."""
    if action not in ACTIONS:
        raise ValueError(f"행위 '{action}' 은(는) 없음 — {list(ACTIONS)}")
    if not actor.strip():
        raise ValueError("입력자 이름이 필요합니다")
    ok, msg = verify(folder)
    if not ok:
        raise ValueError("승인 기록이 손상돼 이어 쓰지 않음 — " + msg)
    if action in ("요청 제출", "감리 승인 기록") and decision_row.get("decision") != "해체 승인 요청 가능":
        raise ValueError(f"{zone_id} 판정이 '{decision_row.get('decision')}' — 해체 승인 요청 가능일 때만 {action}")
    approver, signed_at = (approver or "").strip(), (signed_at or "").strip().replace(" ", "T")
    if (approver or signed_at) and action != "감리 승인 기록":
        raise ValueError("감리 성명·서명 일시는 '감리 승인 기록'에만 적습니다")
    source = (source or "").strip()
    if source and (action != "감리 승인 기록" or source not in SOURCES):
        raise ValueError(f"출처 '{source}' — '감리 승인 기록'에만 {' · '.join(SOURCES)} 중 하나")
    if signed_at:
        try:
            datetime.fromisoformat(signed_at)
        except ValueError:
            raise ValueError(f"서명 일시 '{signed_at}' — YYYY-MM-DD 또는 YYYY-MM-DD HH:MM 로 적어 주세요")
    if now is None:
        now, demo = record_clock(eval_at)
        if demo:
            note = f"{note} {DEMO_NOTE}" if note.strip() else DEMO_NOTE
    rows = read_log(folder)
    r = {"seq": str(len(rows) + 1), "timestamp": now.strftime("%Y-%m-%dT%H:%M:%S"),
         "zone_id": zone_id, "action": action, "actor": actor.strip(), "note": note.replace("\n", " "),
         "evidence_sha": evidence_sha(decision_row), "prev_sha": rows[-1]["sha"] if rows else GENESIS,
         "approver": approver, "signed_at": signed_at, "source": source}
    r["sha"] = line_sha(r)
    p = os.path.join(folder, LOG)
    new = not os.path.exists(p)
    if not new:                                  # 옛 9·11열 머리 → 12열 머리로 (줄은 그대로, 빈 칸은 값 없음으로 읽힘 — 해시 무관)
        with open(p, encoding="utf-8", newline="") as f:
            text = f.read()
        head, sep, rest = text.partition("\n")
        if "source" not in head:
            with open(p, "w", encoding="utf-8", newline="") as f:
                f.write(",".join(FIELDS) + ("\r" if head.endswith("\r") else "") + sep + rest)
    with open(p, "a", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=FIELDS)
        if new:
            w.writeheader()
        w.writerow(r)
    return r


SUPERVISE_REJECTS = "supervise_rejects.csv"   # W-A: 동기화 때 넣지 못한 감리 직접 확인 (승인 기록 아님) — '오늘 볼 것' 이유
REJECT_FIELDS = ["zone_id", "received_at", "name", "client_id", "reason"]


def log_supervise_reject(folder, zone_id, name, client_id, received_at, reason):
    """감리 직접 확인(서버)을 Revit 검사에서 넣지 못함 → 한 줄 추가 (server_sync.pull · FieldServer.Pull 의 supervise 거부). 판정·승인 기록은 그대로"""
    p = os.path.join(folder, SUPERVISE_REJECTS)
    new = not os.path.exists(p)
    with open(p, "a", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        if new:
            w.writerow(REJECT_FIELDS)
        w.writerow([zone_id, (received_at or "")[:16], name, client_id, " ".join(str(reason).split())])


def supervise_rejects(folder, at=None):
    """→ {zone_id: (받은 시각, 감리 이름, 이유)} 구역마다 마지막 거절 (at 날짜까지), 그 뒤 감리 승인·해체·재받침 기록이 생긴 구역은 뺌"""
    p = os.path.join(folder, SUPERVISE_REJECTS)
    if not os.path.exists(p):
        return {}
    with open(p, encoding="utf-8-sig", newline="") as f:
        rows = [r for r in csv.DictReader(f) if (r.get("zone_id") or "").strip()]
    done = status_override(folder, at)
    out = {}
    for r in rows:
        z, ts = r["zone_id"].strip(), (r.get("received_at") or "").strip()
        if at is None or ts[:10] <= at.date().isoformat():
            out[z] = (ts, (r.get("name") or "").strip(), (r.get("reason") or "").strip())
    return {z: v for z, v in out.items() if done.get(z) not in (6, 7, 8)}


REPORT_SOURCE = "협력업체 신고"   # shore_checks.csv source 열 (evidence.REPORT_SOURCE 와 같은 값 — evidence 가 이 모듈을 import 해서 여기 따로 둠)


def status_events(folder, at=None):
    """구역별 마지막 상태 사건 → {zone_id: (상태 코드 6|7|8, 시각 글자)}: 승인 기록(요청 제출 제외) + 협력업체 해체 신고 '뺐음'
    (shore_checks.csv source = 협력업체 신고, present = 아니오 → 7 해체 완료 — 66차: 리허설 2회차 '해체 뒤에도 상태 6', 신고만 있고
    '해체 완료' 승인 기록이 없던 구역). 시각 순 (같은 시각이면 승인 기록 다음 신고), 날짜 단위로 판정일까지 (status_override 와 같음)."""
    evs = [(r["timestamp"], r["zone_id"], ACTIONS[r["action"]]) for r in read_log(folder) if ACTIONS.get(r["action"]) is not None]
    p = os.path.join(folder, "shore_checks.csv")
    if os.path.exists(p):
        with open(p, encoding="utf-8-sig", newline="") as f:
            evs += [(r["timestamp"] or "", r["zone_id"] or "", 7) for r in csv.DictReader(f)
                    if (r.get("source") or "").strip() == REPORT_SOURCE and (r.get("present") or "").strip() == "아니오"]
    out = {}
    for ts, z, code in sorted(evs, key=lambda x: x[0][:16].replace(" ", "T")):
        if at is None or ts[:10] <= at.date().isoformat():
            out[z] = (code, ts)
    return out


def status_override(folder, at=None):
    """구역별 마지막 상태 → 상태 코드 {zone_id: 6|7|8} (status_events). 판정 화면이 이 코드를 우선 표시.
    at 을 주면 그 판정일까지의 기록만 (뒤 날짜 승인 기록이 그 판정에 적용되지 않게 — 20차 점검). 날짜 단위인 이유: 판정 시각은
    늘 판정일 09:00 이라, 시각으로 자르면 같은 날 오후 감리 승인 기록이 그날 다시 한 판정에서 빠짐."""
    return {z: c for z, (c, _) in status_events(folder, at).items()}


def request_rows(rows, folder, at=None):
    """검측요청서·검측 패키지에 넣을 판정 줄: 사람 기록(6 감리 승인 기록 · 7 해체 완료 · 8 재받침)이 있는 구역은 뺌.
    11-09 시뮬레이션: 해체 완료된 1F–4F·감리 승인된 구역이 '요청 가능'으로 다시 요청서에 들어가던 것 (대시보드는 status_override 우선)."""
    if not folder:
        return list(rows)
    done = status_override(folder, at)
    return [r for r in rows if r["zone_id"] not in done]


def zone_counts(rows, folder, at=None):
    """요약 숫자 한 곳 (11-11 Revit #1: 대시보드·요청서가 다른 수를 냈음) — 사람 기록(6·7·8) 구역은 recorded 로만 셈.
    → {ready, keep(존치 전부), defect(존치 중 설치 결함), cyl(공시체 시험 필요), recorded}"""
    left = request_rows(rows, folder, at)
    n = lambda f: sum(1 for r in left if f(r["decision"]))  # noqa: E731
    return {"ready": n(lambda d: d == "해체 승인 요청 가능"), "keep": n(lambda d: d.startswith("존치")),
            "defect": n(lambda d: d == "존치 (설치 결함 기록)"), "cyl": n(lambda d: d == "공시체 시험 필요"), "recorded": len(rows) - len(left)}


# ---- 검측요청서 서식 (총괄 11-05: docs/templates/해체검측요청서_템플릿.docx 배치, 작성안내 대응표) ----
# A4 4쪽: ① 검측요청서 + 결과 통보(세부기준 별지 제6호 준용) ② 판정 근거표·존치 사유·감리 확인 3가지·현장 경고·마감 값
#         ③ 해체 검측 체크리스트(별지 제5호 준용, 가이드라인 4.1·4.2·4.3 — 시공자·감리 합불 빈칸) ④ 공사 참여자 실명부(별지 제7호 준용)
# 데이터·마감 값 계산은 그대로. 프로그램이 채우는 칸 = 공사명·요청일·위치·검측부위·도구 판정·근거표·존치 사유·현장 확인·경고·마감 값, 나머지는 빈 입력란(옅은 파랑)
FORM_NAME = "동바리 해체 검측요청서 (감리 검측요청 서식 준용)"
FORM_FOOT = "본 문서는 계산 도구가 만든 초안이며 해체 승인이 아닙니다. 해체 승인은 감리 검측 결과로만 정합니다."
FORM_CSS = ("@page{size:A4;margin:12mm 13mm}body{font:9.5pt 'Malgun Gothic',sans-serif;color:#111;background:#fff;margin:0}\n"
            ".page{break-after:page;page-break-after:always}.page:last-child{break-after:auto;page-break-after:auto}\n"
            ".hd{text-align:right;color:#777;font-size:7.5pt}.form{display:flex;justify-content:space-between;color:#555;font-size:8pt;margin-top:2px}.re{color:#b00020;font-weight:700}\n"
            ".title{border-top:2px solid #222;border-bottom:2px solid #222;text-align:center;padding:4px 0;margin:2px 0 4px}.title b{font-size:16pt;display:block}.title span{color:#555;font-size:8pt}\n"
            "h2{font-size:10.5pt;margin:6px 0 2px}h2 i{color:#1f3a68;font-style:normal;margin-right:6px}h3{font-size:10pt;margin:6px 0 3px}\n"
            "table{border-collapse:collapse;width:100%;margin:0 0 4px;font-size:9pt}th,td{border:1px solid #444;padding:2px 4px;text-align:left;vertical-align:middle}\n"
            "th{background:#e7e9ee;text-align:center;font-weight:700}td.in{background:#eef4fb}.hint{color:#8a93a3}.c{text-align:center}.n{white-space:nowrap}.s{font-size:8pt}\n"
            ".lead{text-align:center;margin:4px 0}.note{color:#555;font-size:8pt;margin:4px 0}.warn{color:#a00}.sign td{height:26px}\n"
            ".ft{border-top:1px solid #aaa;color:#555;font-size:7.5pt;text-align:center;margin-top:8px;padding-top:2px}\n"
            "tr,.sign{break-inside:avoid;page-break-inside:avoid}td.in,th{-webkit-print-color-adjust:exact;print-color-adjust:exact}")   # 11-11: 붙임 1 이 여러 장이어도 줄·서명란이 안 갈리고 입력란 색이 인쇄됨
FORM_CHECKS = ("<td class=\"in\">□ 해체 기준 강도 충족 — 현장양생공시체 평균 ≥ 해체 기준 강도 (2쪽 근거표)<br>□ 센서 구역 최소 추정 강도 확인 (센서 위치별)<br>"
               "□ 타설층 포함 하부 3개 층 동바리 존치<br>□ 해체 후 하중 재하 검토 (하중비교 · 지지층 14 MPa 이상)<br>□ 해체 작업계획서 승인 · 해체 전 체크리스트 (3쪽)</td>")
FORM_ATTACH = ("<td>□ 시공자 검측 체크리스트 (3쪽) &nbsp; □ 시험성과 (현장양생공시체 압축강도 시험)<br>□ 판정 근거표 · 근거 원장 (2쪽) &nbsp; □ 야장 및 도면 (동바리 배치도 · 센서 위치도)<br>"
               "□ 해체 작업계획서 &nbsp; □ 공사 참여자(기능공 포함) 실명부 (4쪽)</td>")
FORM_RESULT = ("<h2><i>②</i>검 측 결 과 통 보</h2>\n"
               "<table><tr><th style=\"width:13%\">번호</th><td class=\"in\" style=\"width:37%\"><span class=\"hint\">20 &nbsp;. &nbsp;. &nbsp;. - &nbsp;호</span></td><th style=\"width:13%\">받음</th><td class=\"in\"><span class=\"hint\">○○공사 현장대리인 ○○○</span></td></tr></table>\n"
               "<p class=\"lead\">문서번호 <span class=\"hint\">「 &nbsp; &nbsp; &nbsp; &nbsp; 」</span> 로 검측요청 한 건에 대하여 20 &nbsp; . &nbsp; . &nbsp; . 검측한 결과를 다음과 같이 통보합니다.</p>\n"
               "<table><tr><th style=\"width:18%\">1. 검측결과</th><td class=\"in\">□ 합격 — 해체 승인 구역 : <br>□ 불합격 — 존치 구역 : <br>□ 재검측 필요 — 재검측 예정일 : 20 &nbsp; . &nbsp; . &nbsp; .</td></tr>\n"
               "<tr><th>2. 지시사항</th><td class=\"in\" style=\"height:46px\"></td></tr><tr><th>붙임</th><td>□ 감리원의 검측 점검표 (3쪽 감리 확인란)</td></tr></table>\n"
               "<table class=\"sign\"><tr><th style=\"width:18%\">감리</th><th style=\"width:16%\">검측감리원</th><td class=\"in\" style=\"text-align:right\">(인)</td><th style=\"width:16%\">총괄감리원</th><td class=\"in\" style=\"text-align:right\">(인)</td></tr>\n"
               "<tr><th>서명 일시</th><td class=\"in\" colspan=\"2\"><span class=\"hint\">20 &nbsp;. &nbsp;. &nbsp;. &nbsp; : </span></td><th>감리 성명</th><td class=\"in\"></td></tr></table>\n"
               "<p class=\"note\"><b>※ 작성요령</b> 1. 재검측 시에는 붉은 글씨로 \"(재)\"를 우측 상단에 표시합니다. 2. 시공자가 재검측을 요청할 때에는 잘못 시공한 기능공의 명단을 첨부합니다. "
               "3. 2부 작성하여 시공자, 감리원이 각 1부를 보관합니다. 4. 옅은 파란 칸이 입력란입니다.</p>\n")
FORM_CHECKLIST = (
    "<table><tr><th rowspan=\"2\" style=\"width:9%\">구분</th><th rowspan=\"2\" style=\"width:33%\">검사항목</th><th rowspan=\"2\">검사기준<br><span class=\"s\">(시방서 · 가이드라인 · 도면)</span></th>"
    "<th colspan=\"2\">검사결과</th><th rowspan=\"2\" style=\"width:12%\">조치사항</th></tr><tr><th style=\"width:9%\">시공자</th><th style=\"width:9%\">감리</th></tr>\n"
    + "".join(f"<tr>{grp}<td>{item}</td><td class=\"s\">{std}</td><td class=\"in c s\">합 · 불</td><td class=\"in c s\">합 · 불</td><td class=\"in\"></td></tr>\n" for grp, item, std in (
        ("<th rowspan=\"7\">4.1<br>해체 전</th>", "해체 작업계획서 작성 · 검토", "계획서 승인 (해체 순서 · 범위 · 일정)"),
        ("", "존치기간 준수 · 강도 발현 확인", "현장양생공시체 ≥ 해체 기준 강도 (가이드라인 표 2.1.1)"),
        ("", "압축강도 확인 후 해체작업 허가", "시공사 해체 허가 기록"),
        ("", "해체 후 하중 재하 시 필러 동바리 존치 · 재설치 구조검토", "하중비교 (가이드라인 2.1.2 계산 예)"),
        ("", "하부 지지층 강도", "최소 14 MPa (가이드라인 2.1.2)"),
        ("", "작업 준비 · 근로자 교육 · 출입 통제", "관리감독자 확인"),
        ("", "센서 위치별 추정 강도 (구역 최소)", "센서 구역 최소 ≥ 해체 기준 강도"),
        ("<th>4.2<br>해체 중</th>", "해체 순서 (벽 → 기둥 → 보 → 슬래브) · 추락 방지 · 보호구 · 기상", "가이드라인 4.2 체크리스트 — 관리감독자 현장 확인"),
        ("<th rowspan=\"5\">4.3<br>해체 후</th>", "필러 · 지지용 동바리 타설층 포함 하부 3개 층 설치", "KCS 14 20 12:2024 3.3.2(2) · 가이드라인 2.1.2(1)④"),
        ("", "각 층 같은 위치에 설치 (상하 정렬)", "층 간 편차 허용값 이내 (설치 검측)"),
        ("", "동바리 연직도", "설치 검측 기준"),
        ("", "자재 변형 · 침하", "육안 · 측정"),
        ("", "콘크리트 면 결함 (돌기물 등)", "가이드라인 4.3"),
        ("<th>기타</th>", "", "")))
    + "</table>\n"
    "<table class=\"sign\"><tr><th style=\"width:18%\">시공자</th><th style=\"width:13%\">점검일자</th><td class=\"in\"><span class=\"hint\">20 &nbsp; . &nbsp; . &nbsp; .</span></td><th style=\"width:13%\">점검직원</th><td class=\"in\" style=\"text-align:right\">(인)</td></tr>\n"
    "<tr><th>감리원</th><th>검측일자</th><td class=\"in\"><span class=\"hint\">20 &nbsp; . &nbsp; . &nbsp; .</span></td><th>검측감리원</th><td class=\"in\" style=\"text-align:right\">(인)</td></tr></table>\n")
FORM_ROSTER = ("<table><tr><th>작업일</th><th>작업위치<br>및 공종</th><th>소속</th><th>직위</th><th>성명</th><th>생년월일<br>(성별)</th><th>공사한 내용</th><th>서명</th></tr>\n"
               + ("<tr>" + "<td class=\"in\" style=\"height:22px\"></td>" * 8 + "</tr>\n") * 18 + "</table>\n"
               "<p class=\"note\"><b>※</b> 실명부는 시공자가 작성하여 감리원에게 제출하고, 감리원은 확인 후 유지·관리합니다. 개인정보는 공사 관리 목적으로만 쓰고 보관 기간이 지나면 파기합니다.</p>\n")


def _form_page(n, form_no, title, sub, body):
    """서식 한 쪽: 머리 · (재)검측 · 제목 띠 · 본문 · 바닥글 (n / 4)."""
    return (f"<section class=\"page\"><div class=\"hd\">{FORM_NAME}</div>\n<div class=\"form\"><span>{form_no}</span><span class=\"re\">(재)검측 □</span></div>\n"
            f"<div class=\"title\"><b>{title}</b><span>{sub}</span></div>\n{body}<div class=\"ft\">{FORM_FOOT} &nbsp; 서식 {n} / 4</div></section>\n")


def request_html(decisions, project, at, folder=None, gate=None):
    """동바리 해체 검측요청서 (감리 검측요청 서식 준용) 초안 HTML — 서식 4가지 (서식 배치는 위 FORM_*, 붙임 1 은 구역 수만큼 여러 장). 요청 가능 구역은 근거와 함께, 존치 구역은 사유만.
    서명·결과·합불 칸은 빈칸 (감리·시공자가 적음). 데이터·마감 값 계산은 그대로.
    gate: evaluate.gate_details (판정과 같은 양생 하한) — 주면 존치 층수 칸에 위 층 수·지지층 강도·하중비 (11-11 G7)."""
    e = html.escape
    import evaluate as ev          # 늦은 import (evidence 가 approval 을 import 함) — 인쇄 날짜 kdate·kstamp
    import evidence as evd
    n_all = len(decisions)
    decisions = request_rows(decisions, folder, at)   # 11-09: 이미 사람 기록(6·7·8)이 있는 구역은 요청서에서 뺌 — 대시보드와 같은 상태 우선
    skip = n_all - len(decisions)
    req = [d for d in decisions if d["decision"] == "해체 승인 요청 가능"]
    keep = [d for d in decisions if d["decision"] != "해체 승인 요청 가능"]
    ok, chain = verify(folder) if folder else (True, "")
    led = evd.ledger_at(folder, at.date()) if folder else {}
    site, seal = (site_checks(req, folder, at) if folder and req else ("", ""))
    seal_v = seal.split("<b>")[1][:8] if seal else ""
    unready = [d["zone_id"] for d in req if folder and not all(k in led.get(d["zone_id"], {}).get("dates", {}) for k in (2, 4))]   # 11-11 1-1: 가이드라인 표 3.2.1 2·4단계
    ids = [d["zone_id"] for d in req]
    fl = list(dict.fromkeys(z.split("-")[0] if "-" in z else (f"B{-int(d['floor'])}" if int(d["floor"]) < 0 else f"{d['floor']}F") for z, d in zip(ids, req)))
    hint = lambda t: f"<span class=\"hint\">{t}</span>"   # noqa: E731
    p1 = ("<h2><i>①</i>검 측 요 청 서</h2>\n"
          f"<table><tr><th style=\"width:13%\">번호</th><td class=\"in\" style=\"width:37%\">{hint('20 &nbsp;. &nbsp;. &nbsp;. - &nbsp;호')}</td><th style=\"width:13%\">받음</th><td class=\"in\">{hint('○○공사 총괄감리원 ○○○')}</td></tr>\n"
          f"<tr><th>공사명</th><td class=\"in\">{e(project)}</td><th>요청일</th><td class=\"in\">{ev.kdate(at.date().isoformat())}</td></tr></table>\n"
          "<p class=\"lead\">다음과 같은 세부공종에 대하여 검측요청 하오니 검사 후 승인하여 주시기 바랍니다.</p>\n"
          f"<table><tr><th style=\"width:18%\">위치 및 공종</th><td class=\"in\">{e(', '.join(ids)) + ' / 거푸집·동바리 해체' if req else hint('해체 승인 요청 가능 구역 없음')}</td></tr>\n"
          f"<tr><th>검측부위</th><td class=\"in\">{e('·'.join(fl)) + ' 슬래브 동바리 해체 · 위 3개층 존치 동바리 확인' if req else ''}</td></tr>\n"
          f"<tr><th>검측요구일시</th><td class=\"in\">{hint('20 &nbsp;. &nbsp;. &nbsp;. ( &nbsp;) &nbsp; : ')}</td></tr>\n"
          f"<tr><th>검측사항</th>{FORM_CHECKS}</tr>\n"
          f"<tr><th>도구 판정</th><td class=\"in\">해체 승인 요청 가능 <b>{len(req)}</b> 구역 &nbsp;/&nbsp; 존치 <b>{sum(d['decision'].startswith('존치') for d in keep)}</b> 구역 &nbsp;/&nbsp; 공시체 시험 필요 <b>{sum(d['decision'] == '공시체 시험 필요' for d in keep)}</b> 구역 &nbsp; &nbsp; 마감 값 <b>{seal_v or '—'}</b>"
          + (f" &nbsp; <span class=\"s\">이미 기록된 {skip}구역 제외 (감리 승인 기록·해체 완료·재받침)</span>" if skip else "")
          + (f"<br><b class=\"warn\">현장 확인 미비 {len(unready)}구역 — 작업계획서·해체 전 점검 근거 원장 기록 없음: {e(', '.join(unready))}</b>" if unready else "")
          + f" &nbsp; <span class=\"s\">(판정 기준 시각 {ev.kstamp(at.isoformat())})</span><br><span class=\"note\">도구는 \"요청 가능 / 존치\"만 표시합니다. 해체 승인은 감리 검측으로만 정합니다.</span></td></tr>\n"
          f"<tr><th>붙임</th>{FORM_ATTACH}</tr></table>\n"
          "<table class=\"sign\"><tr><th style=\"width:18%\">시공자</th><th style=\"width:16%\">점검직원</th><td class=\"in\" style=\"text-align:right\">(인)</td><th style=\"width:16%\">현장대리인</th><td class=\"in\" style=\"text-align:right\">(인)</td></tr></table>\n"
          + FORM_RESULT)
    def no_sensor(d):
        return d["est_source"].startswith("기상 예측")

    def est_cell(d):     # 11-11 Revit #17: 센서가 없으면 칸엔 '센서 없음'만 (추정·설명은 표 아래 한 줄)
        if no_sensor(d):
            return "<td class=\"c\">센서 없음</td>"
        return f"<td>{d['f_est_min']}{' / ' + e(str(d['f_est_xy'])) if d['f_est_xy'] else ''}<br><span class=\"s\">{e(d['est_source'])}</span></td>"

    def gate_cell(zid):  # 11-11 G7: 감리가 직접 볼 값 — 위로 타설된 층 수·지지층 최소 구역 강도·하중비
        g = (gate or {}).get(zid)
        if not g:
            return ""
        low = min(g["supports"], key=lambda s: s[2]) if g["supports"] else None   # 지지층이 여럿이면 가장 약한 층만
        parts = ([f"위 {g['above']}층"] + ([f"지지층 최소 {low[1]} {low[2]:.1f}"] if low else [])
                 + ([f"하중비 {g['ratio']:.2f}"] if g["ratio"] is not None else []))
        return "<br><span class=\"s\">" + e(" · ".join(parts)) + "</span>"

    rows = "".join(
        f"<tr><td class=\"c n\">{e(d['zone_id'])}</td><td class=\"c\">{d['floor']}</td><td class=\"c n\">{ev.kdate(d['pour_date'])}</td><td class=\"c\">{d['age_days']}</td><td class=\"c\">{d['fck']:g}</td>"
        f"<td class=\"n\">{d['f_cyl']} <span class=\"s\">{ev.kdate(d['cyl_test_date'])}</span><br><span class=\"s\">{e(d['cyl_curing'])}</span>"
        f"{lot_cell(d)}</td>"
        f"{est_cell(d)}"
        f"<td class=\"c\">{e(d['level_gate'])}{gate_cell(d['zone_id'])}</td><td class=\"c\">{led.get(d['zone_id'], {}).get('stage', 0)}/8<br><span class=\"s\">{evidence_sha(d)[:12]}</span></td><td class=\"c n\">요청 가능</td></tr>\n" for d in req)
    nosens = [d["est_source"] for d in req if no_sensor(d)]
    grp = {}
    for d in keep:
        grp.setdefault((d["floor"], d["decision"], d["reason"], d["predicted_due"]), []).append(d["zone_id"])
    held = "".join(f"<tr><td>{e(', '.join(z))}</td><td class=\"c\">{k[0]}</td><td>{e(k[1])}: {e(k[2])}</td>"
                   f"<td class=\"c n\">{ev.kdate(k[3])}</td><td></td></tr>\n" for k, z in grp.items())
    p2 = ("<h2><i>③</i>요청 구역 근거표</h2>\n"
          "<table><thead><tr><th>구역</th><th>층</th><th>타설일</th><th class=\"n\">재령<br>(일)</th><th class=\"n\">fck<br>(MPa)</th><th>공시체 평균 (MPa)<br>· 시험일</th><th>센서 구역 최소 (MPa)<br>/ 위치</th>"
          "<th class=\"s n\">존치 층수<br>(규칙 게이트)</th><th class=\"n\">근거<br>n/8 · 해시</th><th class=\"n\">도구 판정</th></tr></thead>\n"
          + (rows or "<tr><td colspan=\"10\" class=\"c\">없음</td></tr>\n") + "</table>\n"
          "<p class=\"note\"><b>판정 근거</b> 거푸집 및 동바리 해체 가이드라인(국토교통부, 2023.1) 표 2.1.1 · KCS 14 20 12:2024 3.3.1·3.3.2 — 해체 기준 강도 ≥ 설계기준압축강도"
          "(판넬 조기 탈형에 한해 필러 동바리 존치·구조계산 시 최소 14 MPa), 타설층 포함 3개 층 존치, 같은 위치 현장양생 공시체 시험(가이드라인 2.1.2(3)④·고시 제2024-879호). "
          "우선순위 규칙 게이트 → 공시체 → 센서 구역 최소. 근거 해시 = 판정 행 해시 (approvals.csv 의 evidence_sha 와 대조)."
          + (" · 승인 기록: " + e(chain) if folder else "")
          + (" · 센서 없음 = " + e(nosens[0]) if nosens else "")
          + (" · " + e(ev.support_note(folder)) if folder else "") + "</p>\n"
          + ("" if ok else "<p class=\"warn\">승인 기록 사슬 손상: " + e(chain) + "</p>\n")
          + "<h2><i>④</i>존치 구역 및 사유</h2>\n"
          "<table><thead><tr><th style=\"width:21%\">구역</th><th style=\"width:4%\">층</th><th>존치 사유</th><th style=\"width:12%\">예측 해체일</th><th style=\"width:7%\">비고</th></tr></thead>\n"
          + (held or "<tr><td colspan=\"5\" class=\"c\">없음</td></tr>\n") + "</table>\n"
          + (site or "<h2><i>⑤</i>감리가 현장에서 확인할 3가지</h2><p class=\"note\">요청 가능 구역이 없어 표가 없습니다.</p>") + "\n"
          f"<table><tr><th style=\"width:12%\">마감 값</th><td class=\"c\" style=\"width:12%\"><b>{seal_v or '—'}</b></td><td class=\"s\">판정 시점 근거를 고정한 값(SHA 앞 8자리) — 앱 화면 값과 같아야 합니다. "
          "서명할 때 종이에 함께 적어 두면 나중에 근거 묶음(판정 근거·원장·승인 기록)이 바뀌었는지 대조됩니다 (바꾸기를 막지는 못하고 드러낼 뿐).</td></tr></table>\n")
    za = {r["zone_id"]: (float(r["x1"]) - float(r["x0"])) * (float(r["y1"]) - float(r["y0"])) for r in ev.read(folder, "zones.csv")} if folder else {}
    area = f"{sum(za[z] for z in ids):.1f}" if ids and all(z in za for z in ids) else "&nbsp; &nbsp;"   # 11-11 Revit #16: 구역 면적 합 (zones.csv, m)
    p3 = ("<table><tr><th style=\"width:9%\">№</th><th style=\"width:14%\">공종 CODE</th><th>위치 및 부위</th><th style=\"width:24%\">공종(세부공종)</th><th style=\"width:20%\">공사량</th></tr>\n"
          f"<tr><td class=\"in\"></td><td class=\"in\"></td><td class=\"in\">{e(', '.join(ids))}</td><td class=\"in\">거푸집·동바리 해체</td><td class=\"in\">해체 면적 {area} m²</td></tr></table>\n"
          + FORM_CHECKLIST + f"<p class=\"note\">{e(DURING_NOTE)}. ※ 작성요령 1. \"시공자\" 칸은 시공자 담당기술자가 먼저 점검하여 합격으로 확인한 뒤 검측요청서와 함께 제출합니다. "
          "2. \"감리\" 칸은 감리원이 현장 확인 후 기록합니다. 3. 불합격 항목은 조치사항을 적고 보완 시공 후 재검측을 받습니다. 4. 4.2 해체 중 항목은 도구가 관리하지 않으며 관리감독자가 현장에서 확인합니다.</p>\n")
    p4 = f"<table><tr><th style=\"width:18%\">공사명</th><td class=\"in\">{e(project)}</td></tr></table>\n" + FORM_ROSTER
    return (f"<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>{FORM_NAME} {e(project)} {at:%Y-%m-%d}</title>\n"
            f"<style>{FORM_CSS}{REPORT_CSS}</style></head><body>\n"
            + _form_page(1, "[별지 제6호서식 준용] 주택건설공사 감리업무 세부기준", "동바리 해체 검측요청서", "거푸집·동바리 해체 — 감리 검측요청 서식 준용 (검측요청 · 결과 통보)", p1)
            + _form_page(2, "[붙임 1] 판정 근거표 — 근거 원장 요약", "해체 요청 구역 판정 근거", "도구 계산값 · 현장양생공시체 시험 · 센서 위치별 추정 강도", p2)
            + _form_page(3, "[별지 제5호서식 준용] 검측 점검표", "거푸집·동바리 해체 검측 체크리스트", "거푸집 및 동바리 해체 가이드라인(2023.1) 4.1 해체 전 · 4.2 해체 중 · 4.3 해체 후", p3)
            + _form_page(4, "[별지 제7호서식 준용] 공사 참여자(기능공 포함) 실명부", "공사 참여자 실명부", "거푸집·동바리 해체 작업 참여자", p4)
            + "</body></html>")


CHECK_FRESH_H = 24.0   # [가정] 위층 동바리 존치 확인으로 인정하는 시간 (pour_gate.py 와 같은 값)


def check_targets(folder, zid):
    """요청서 현장 확인 ① 대상 — zid 위 3개층(층 순서)에서 평면이 겹치는 구역, zones.csv 순서 (site_checks 의 need 와 같은 규칙).
    검측 패키지가 이 구역들의 최근 24 h 동바리 확인 사진을 첨부/사진/ 에 넣을 때 씀 (총괄 29차)."""
    import evaluate as ev          # 늦은 import: evidence 가 approval 을 import 함
    zones = ev.read(folder, "zones.csv")
    floor_of = {r["zone_id"]: int(r["floor"]) for r in zones}
    rect_of = {r["zone_id"]: tuple(float(r[k]) for k in ("x0", "y0", "x1", "y1")) for r in zones}
    if zid not in floor_of:
        return []
    above = [f for f in sorted(set(floor_of.values())) if f > floor_of[zid]][:3]
    return [z for z in floor_of if floor_of[z] in above and ev.overlaps(rect_of[z], rect_of[zid])]


def site_checks(req, folder, at):
    """감리가 현장에서 확인할 3가지 + 마감 값 (docs/field_process_research.md "감리가 무엇을 보고 서명하는가").
    ① 위 3개층(평면 겹침·층 순서) 구역마다 최근 24 h 동바리 '예' 확인 ② 작업계획서(원장 2) ③ 해체 전 체크리스트(원장 4).
    마감 값 = 요청 구역 근거 해시·원장 단계 날짜·승인 기록 사슬 끝 해시의 SHA-256 앞 8자 — 서명한 종이에 적어 두면
    나중에 이 근거 묶음이 바뀌었는지 대조할 수 있다 (조작을 막지는 못하고 드러낼 뿐)."""
    import after_check as ac
    import evaluate as ev          # 늦은 import: evidence 가 approval 을 import 함
    import evidence as evd
    e = html.escape
    zones = {r["zone_id"]: r for r in ev.read(folder, "zones.csv")}
    floor_of = {z: int(r["floor"]) for z, r in zones.items()}
    rect_of = {z: tuple(float(r[k]) for k in ("x0", "y0", "x1", "y1")) for z, r in zones.items()}
    order = sorted(set(floor_of.values()))
    last = {}
    for r in ev.read(folder, "shore_checks.csv"):
        t = ev.ts(r["timestamp"])
        if t <= at and (r["zone_id"] not in last or t >= last[r["zone_id"]][0]):
            last[r["zone_id"]] = (t, r.get("present", "").strip())
    led = evd.ledger_at(folder, at.date())
    key = ev.floor_order(folder)                                     # 구역 목록은 층 순 → 구역 ID (총괄 11-05)
    watch = evd.removal_watch(folder, at.date())                     # 무단 해체 관측 (총괄 P1-2, 경고만)
    rows, seal_src, near = [], [], set()
    for d in req:
        zid = d["zone_id"]
        if zid not in floor_of:
            continue
        above = [f for f in order if f > floor_of[zid]][:3]
        near |= {floor_of[zid], *above}
        need = [z for z in zones if floor_of[z] in above and ev.overlaps(rect_of[z], rect_of[zid])]
        miss = [z for z in need if not (z in last and last[z][1] == "예" and (at - last[z][0]).total_seconds() / 3600 <= CHECK_FRESH_H)]
        L = led.get(zid, {"dates": {}, "stage": 0})
        c1 = "✓ 확인 있음" if need and not miss else ("✕ 확인 없음: " + ", ".join(sorted(miss, key=key)[:6]) + (" …" if len(miss) > 6 else "")) if need else "— 위층 없음"
        sus = sorted((z for z in need + [zid] if z in watch), key=key)
        if sus:
            c1 = f"⚠ 무단 해체 의심 — 확인 필요: {', '.join(sus)} · {c1}"
        c2 = f"✓ {ev.kdate(L['dates'][2])}" if 2 in L["dates"] else "✕ 원장 기록 없음"
        c3 = f"✓ {ev.kdate(L['dates'][4])}" if 4 in L["dates"] else "✕ 원장 기록 없음"
        rows.append(f"<tr><td class=\"n\">{e(zid)}</td><td>{L['stage']}/8</td><td>{e(c1)}</td><td>{e(c2)}</td><td>{e(c3)}</td><td class=\"in c n\">□ 적합</td><td class=\"in c n\">□ 적합</td></tr>")
        seal_src.append([zid, evidence_sha(d), sorted(L["dates"].items())])
    # 이 층·바로 위 3개층 현장 경고 (총괄 적합성 ⓐ-1): 층 단위(평면 겹침 아님) — 같은 층 다른 구역의 무단 해체 의심도 감리가 보게. 마감 값에는 안 넣음
    day = at.date()
    scope = {d["zone_id"]: f"{d['zone_id']} {ev.scope_note(d)}" for d in req if ev.scope_note(d)}   # v6.2 W11 범위 밖 (요청 구역)
    near_w = sorted(((z, t) for src in (watch, evd.signature_watch(folder, day), ac.warnings(folder, day.isoformat()), scope)
                     for z, t in src.items() if floor_of.get(z) in near), key=lambda x: key(x[0]))
    lis = "".join(f"<li>{e(t)}</li>" for _, t in near_w)
    warn = ("<h2><i>⑥</i>이 층·바로 위 3개층 현장 경고</h2>" + (f"<ul class=\"warn\">{lis}</ul>" if lis else "<p class=\"note\">없음</p>")
            + "<p class=\"note\">같은 층·위 3개층의 무단 해체 관측·서명 전 해체 관측·해체 후 점검 미완·범위 밖 가능(두꺼운 슬래브·지하층·높은 동바리) — 경고만 (판정·마감 값에 넣지 않음), 현장에서 확인하세요.</p>")
    head = (read_log(folder) or [{"sha": GENESIS}])[-1]["sha"]
    seal = hashlib.sha256(json.dumps([seal_src, head], ensure_ascii=False).encode("utf-8")).hexdigest()[:8]
    site = ("<h2><i>⑤</i>감리가 현장에서 확인할 3가지</h2><table class=\"s\"><tr><th>구역</th><th>근거 원장</th><th>① 위 3개층 동바리 존치 (최근 24 h)</th>"
            "<th>② 작업계획서</th><th>③ 해체 전 체크리스트 (가이드라인 4.1)</th><th>시공자</th><th>감리</th></tr>" + "".join(rows) + "</table>"
            "<p class=\"note\">✕ 는 프로그램 기록에 없다는 뜻입니다 — 현장에서 직접 확인하고 서류를 받으세요. 동바리 확인은 휴대폰 [동바리 확인], 서류는 [근거 원장].</p>" + warn)
    return site, (f"<p class=\"note\">마감 값 <b>{seal}</b> — 서명할 때 이 값을 종이에 함께 적어 두면, 나중에 이 근거 묶음(판정 근거·원장·승인 기록)이 바뀌었는지 "
                  f"대조할 수 있습니다. 바꾸기를 막지는 못하고 드러낼 뿐입니다.</p>")


if __name__ == "__main__":
    import shutil
    import tempfile
    t = tempfile.mkdtemp()
    try:
        ok_row = {"zone_id": "9F-A1", "decision": "해체 승인 요청 가능", "reason": "기준 충족", "threshold": 24.0, "f_cyl": 24.8,
                  "cyl_test_date": "2026-12-30", "cyl_curing": "현장봉함", "f_est_min": 24.1, "est_source": "센서", "level_gate": "OK",
                  "pour_date": "2026-11-26", "age_days": 35.0}
        no_row = dict(ok_row, zone_id="9F-A2", decision="존치 (구역 최소 강도 미달)", reason="구역 최소 추정 23.90 < 24 MPa")
        assert evidence_sha(ok_row) == evidence_sha(dict(reversed(list(ok_row.items()))))      # 열 순서 무관
        record(t, "9F-A1", "요청 제출", "김현장", ok_row, now=datetime(2026, 12, 31, 10))
        record(t, "9F-A1", "감리 승인 기록", "이감리", ok_row, now=datetime(2026, 12, 31, 14))
        try:
            record(t, "9F-A2", "감리 승인 기록", "이감리", no_row)                              # 존치 구역은 승인 기록 불가
            raise AssertionError("존치 구역 승인 기록을 받음")
        except ValueError as x:
            assert "해체 승인 요청 가능일 때만" in str(x)
        record(t, "9F-A1", "해체 완료", "김현장", ok_row, now=datetime(2027, 1, 2, 9))
        assert verify(t) == (True, "기록 3건, 사슬 정상") and status_override(t) == {"9F-A1": 7}
        # 판정일까지의 기록만 (20차): 12-28 판정 → 없음, 12-31 09:00 판정 → 같은 날 14:00 감리 승인 기록 6, 1-2 → 해체 완료 7
        assert status_override(t, datetime(2026, 12, 28, 9)) == {} and status_override(t, datetime(2026, 12, 31, 9)) == {"9F-A1": 6}
        assert status_override(t, datetime(2027, 1, 2, 9)) == {"9F-A1": 7}
        # 66차 (리허설 2회차 '해체 뒤에도 상태 6'): 감리 승인 기록 뒤 협력업체 해체 신고 '뺐음' 만 있어도 7, 안전관리자 '아니오'(출처 없음)는 관측이라 안 셈,
        # 신고 뒤 재받침 기록이면 8, 판정일 뒤 신고는 그 판정에 안 셈
        record(t, "9F-B1", "감리 승인 기록", "이감리", {**ok_row, "zone_id": "9F-B1"}, now=datetime(2027, 1, 1, 14))
        with open(os.path.join(t, "shore_checks.csv"), "w", encoding="utf-8", newline="") as f:
            f.write("timestamp,zone_id,present,actor,source\n2027-01-02T08:00,9F-B1,아니오,안전관리자,\n")
        assert status_override(t)["9F-B1"] == 6
        with open(os.path.join(t, "shore_checks.csv"), "a", encoding="utf-8", newline="") as f:
            f.write("2027-01-02 10:30,9F-B1,아니오,반장,협력업체 신고\n")
        assert status_override(t)["9F-B1"] == 7 and status_events(t)["9F-B1"] == (7, "2027-01-02 10:30")
        assert status_override(t, datetime(2027, 1, 1, 9))["9F-B1"] == 6
        p = os.path.join(t, LOG)                                                              # 기록 고치기 → 찾음
        s = open(p, encoding="utf-8").read()
        open(p, "w", encoding="utf-8", newline="").write(s.replace("이감리", "박감리"))
        ok, msg = verify(t)
        assert not ok and "2번째" in msg, msg
        try:
            record(t, "9F-A1", "재받침", "김현장", ok_row)
            raise AssertionError("손상된 기록에 이어 씀")
        except ValueError:
            pass
        # 시연 시각: 판정 기준 시각이 미래면 그 시각 + note 표시, 지난 판정이면 실제 시계 그대로
        t2 = tempfile.mkdtemp()
        future, past = datetime.now().replace(microsecond=0) + timedelta(days=400), datetime(2020, 1, 1, 9)
        assert record_clock(future) == (future, True) and record_clock(past)[1] is False and record_clock(None)[1] is False
        r1 = record(t2, "9F-A1", "요청 제출", "김현장", ok_row, "요청서 제출", eval_at=future)
        assert r1["timestamp"] == future.strftime("%Y-%m-%dT%H:%M:%S") and r1["note"] == "요청서 제출 " + DEMO_NOTE, r1
        before = datetime.now().replace(microsecond=0)
        r2 = record(t2, "9F-A1", "감리 승인 기록", "이감리", ok_row, eval_at=past)
        assert before <= datetime.fromisoformat(r2["timestamp"]) <= datetime.now() and r2["note"] == "", r2
        assert verify(t2)[0]
        shutil.rmtree(t2)
        # 감리 성명·서명 일시 (11-05): 옛 9열 파일에 이어 써도 사슬 정상, 새 줄 성명을 지우면 드러남, 감리 승인 기록에만
        t3 = tempfile.mkdtemp()
        old = {"seq": "1", "timestamp": "2026-12-31T10:00:00", "zone_id": "9F-A1", "action": "요청 제출", "actor": "김현장", "note": "",
               "evidence_sha": evidence_sha(ok_row), "prev_sha": GENESIS}
        old["sha"] = line_sha(old)
        with open(os.path.join(t3, LOG), "w", encoding="utf-8", newline="") as f:
            w = csv.DictWriter(f, fieldnames=FIELDS_V1)
            w.writeheader()
            w.writerow(old)
        r3 = record(t3, "9F-A1", "감리 승인 기록", "이감리", ok_row, now=datetime(2026, 12, 31, 14), approver="이감리", signed_at="2026-12-31 13:30")
        assert r3["signed_at"] == "2026-12-31T13:30" and verify(t3) == (True, "기록 2건, 사슬 정상"), verify(t3)
        assert open(os.path.join(t3, LOG), encoding="utf-8").readline().strip() == ",".join(FIELDS)
        record(t3, "9F-A1", "해체 완료", "김현장", ok_row, now=datetime(2027, 1, 2, 9))
        assert verify(t3)[0] and read_log(t3)[0]["approver"] in (None, "")
        p3 = os.path.join(t3, LOG)
        s3 = open(p3, encoding="utf-8").read()
        open(p3, "w", encoding="utf-8", newline="").write(s3.replace(",이감리,2026-12-31T13:30", ",,2026-12-31T13:30"))
        assert not verify(t3)[0] and "2번째" in verify(t3)[1]
        # W-A 출처: 11열 파일에 이어 써도 사슬 정상·머리 12열, 감리 직접 줄의 출처를 지우면 드러남, 옛 줄 = 종이 옮김
        t4 = tempfile.mkdtemp()
        with open(os.path.join(t4, LOG), "w", encoding="utf-8", newline="") as f:
            w = csv.DictWriter(f, fieldnames=FIELDS[:-1])
            w.writeheader()
            w.writerow({**old, "approver": "", "signed_at": ""})
        record(t4, "9F-A1", "감리 승인 기록", "이감리", ok_row, now=datetime(2026, 12, 31, 14), approver="이감리", signed_at="2026-12-31 13:30")
        r5 = record(t4, "9F-B1", "감리 승인 기록", "이감리", {**ok_row, "zone_id": "9F-B1"}, now=datetime(2026, 12, 31, 15), approver="이감리", signed_at="2026-12-31 14:55", source="감리 직접")
        assert verify(t4) == (True, "기록 3건, 사슬 정상") and open(os.path.join(t4, LOG), encoding="utf-8").readline().strip() == ",".join(FIELDS)
        assert [source_label(x) for x in read_log(t4)] == ["", "종이 옮김", "감리 직접"] and r5["sha"] == line_sha(r5)
        p4 = os.path.join(t4, LOG)
        s4 = open(p4, encoding="utf-8").read()
        open(p4, "w", encoding="utf-8", newline="").write(s4.replace(",감리 직접", ","))
        assert not verify(t4)[0] and "3번째" in verify(t4)[1]
        shutil.rmtree(t4)
        for bad in (dict(approver="이감리"), dict(signed_at="12/31"), dict(source="감리 직접"), dict(source="전화")):
            try:
                record(tempfile.mkdtemp(), "9F-A1", "요청 제출" if "approver" in bad or bad.get("source") == "감리 직접" else "감리 승인 기록", "김현장", ok_row, **bad)
                raise AssertionError("받으면 안 됨: " + str(bad))
            except ValueError:
                pass
        shutil.rmtree(t3)
        full = dict(floor=9, fck=24.0, f_est_xy="", predicted_due="2026-12-25")
        h = request_html([{**ok_row, **full}, {**no_row, **full}], "데모", datetime(2026, 12, 31, 9), t)
        assert "9F-A1" in h and "<td>9F-A2</td>" in h and "존치 (구역 최소 강도 미달): 구역 최소 추정" in h and "사슬 손상" in h and "승인합니다" not in h
        assert h.count("<section class=\"page\">") == 4 and h.count("height:22px") == 8 * 18 and FORM_FOOT in h and "공사 참여자 실명부" in h and "4.2<br>해체 중" in h
    finally:
        shutil.rmtree(t)
    print("approval self-check OK")

"""Revit 공유 매개변수 파일(SHR_SharedParameters.txt) 생성. GUID는 이름에서 고정 생성 → 다시 만들어도 같은 매개변수로 인식.
Revit이 쓰는 형식(UTF-16 LE, 탭 구분)을 따른다. 실행: python revit/make_shared_params.py
값이 MPa·kN 인 항목은 단위 문제를 피하려고 NUMBER(단위 없는 숫자)로 둔다. 날짜는 YYYYMMDD 정수 (Revit에 날짜 타입 없음).
"""
import os
import uuid

GROUPS = {
    "SHR_Zone": [  # 구역 상자 (Generic Model)
        ("SHR_ZoneID", "TEXT"), ("SHR_Floor", "INTEGER"), ("SHR_PourDate", "INTEGER"), ("SHR_PourTime", "INTEGER"),
        ("SHR_Mix", "TEXT"), ("SHR_fck", "NUMBER"), ("SHR_fCyl", "NUMBER"), ("SHR_fEst", "NUMBER"), ("SHR_TeDays", "NUMBER"),
        ("SHR_Status", "INTEGER"), ("SHR_Decision", "TEXT"), ("SHR_Reason", "TEXT"), ("SHR_DueDate", "INTEGER"),
        ("SHR_LevelsReq", "INTEGER"), ("SHR_LoadRatio", "NUMBER"), ("SHR_ApprovedBy", "TEXT"), ("SHR_ApprovedDate", "INTEGER"),
        ("SHR_GenBy", "TEXT")],
    "SHR_Shore": [  # 동바리 (Generic Model)
        ("SHR_Type", "TEXT"), ("SHR_Splices", "INTEGER"), ("SHR_HorizBrace", "YESNO"), ("SHR_Allowable_kN", "NUMBER"),
        ("SHR_Load_kN", "NUMBER"), ("SHR_InstallDate", "INTEGER"), ("SHR_RemoveDate", "INTEGER"),
        ("SHR_InspectDate", "INTEGER"), ("SHR_InspectResult", "INTEGER")],
    "SNS": [  # 센서 (Generic Model)
        ("SNS_ID", "TEXT"), ("SNS_Depth_mm", "NUMBER"), ("SNS_LastTemp", "NUMBER"), ("SNS_TeDays", "NUMBER"),
        ("SNS_fEst", "NUMBER"), ("SNS_LastUpdate", "INTEGER")],
    "SHR_Slab": [  # 바닥·지붕 (현장타설 여부를 사람이 지정: 아니오 = 동바리 대상 제외, 예 = 자동 판별 무시하고 포함, 비움 = 자동)
        ("SHR_CastInPlace", "YESNO")],
    "PRJ": [  # 프로젝트 정보
        ("PRJ_StationID", "TEXT"), ("PRJ_CycleDays", "INTEGER"), ("PRJ_MinLevels", "INTEGER"), ("PRJ_CureMin", "NUMBER"),
        ("PRJ_CSVFolder", "TEXT"), ("PRJ_StartDate", "INTEGER"),   # PRJ_StartDate = 첫 타설일(1층 슬래브), 착공일 아님
        ("PRJ_DefaultMix", "TEXT"), ("PRJ_DefaultFck", "NUMBER"),  # 타설 전 공정 계획용 기본 배합·fck (구역별 값은 타설 등록에서)
        ("PRJ_LastEvalDate", "INTEGER"),   # 마지막 해체 판정 기준일 YYYYMMDD (대시보드 기준일 초기값)
        ("PRJ_AlignTolMm", "NUMBER")],     # 설치 검측 상하 정렬 허용값 mm [입력] — 비우거나 0 이면 편차만 (F9, install_rules.align_check tol_mm)
}


def build():
    lines = ["# This is a Revit shared parameter file.", "# Do not edit manually.",
             "*META\tVERSION\tMINVERSION", "META\t2\t1", "*GROUP\tID\tNAME"]
    lines += [f"GROUP\t{i}\t{g}" for i, g in enumerate(GROUPS, 1)]
    lines.append("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEINPROPERTIES")
    for i, (g, params) in enumerate(GROUPS.items(), 1):
        for name, dtype in params:
            guid = uuid.uuid5(uuid.NAMESPACE_URL, f"build-up-2/shoring/{name}")
            lines.append(f"PARAM\t{guid}\t{name}\t{dtype}\t\t{i}\t1\t\t1\t0")
    return "\r\n".join(lines) + "\r\n"


if __name__ == "__main__":
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "SHR_SharedParameters.txt")
    text = build()
    names = [l.split("\t")[2] for l in text.splitlines() if l.startswith("PARAM\t")]
    assert len(names) == len(set(names)) == sum(len(v) for v in GROUPS.values())   # 이름 중복 없음
    with open(out, "w", encoding="utf-16", newline="") as f:   # utf-16 = BOM + LE (Revit이 쓰는 형식)
        f.write(text)
    print(len(names), "개 →", out)

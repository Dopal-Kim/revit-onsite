"""현장 사진 근거 (F15, 총괄 11-05) — 휴대폰 사진 1장을 데이터 폴더 사진/YYYYMMDD/ 에 원본 그대로 저장하고
CSV photo 칸에 상대 경로, photo_sha256 칸에 SHA-256 을 남긴다 (예전 'photo = 파일 이름만' 을 대신함).

받는 곳: 동바리 확인(shore_check) · 해체 후 점검(after_check) · 협력업체 해체 신고(report) — tools/field_server.py (C# Photos·FieldServer 같음).
  설치 검측은 휴대폰 입력이 아니라 install_inspection.csv(사람이 관리)라 여기 없음 — 설치 검측 사진은 [근거 원장] 1 설치검측 서류(파일 + SHA-256)로.
검사: 8 MB [가정] 이하, JPEG·PNG 만(앞 바이트로 판별 — 파일 이름·확장자는 안 믿음).
파일 이름은 서버가 정함: 사진/YYYYMMDD/{구역}_{종류}_{HHMMSS}_{sha 앞 8자}.jpg|png  (날짜·HHMMSS = 그 기록의 시각 — 부르는 쪽이 at 으로 넘김, 총괄 29차)
  같은 날·구역·종류에 같은 사진(SHA-256 같음)이 이미 있으면 그 파일을 다시 씀(새로 만들지 않음).
원본 바이트를 그대로 저장한다 — 메타데이터를 읽거나 고치지 않는다. 판정·결론은 안 바꿈(근거 기록만).
쓰는 곳: 슬래브 이력서 '현장 사진' 줄(경로·해시), 검측 패키지 첨부/사진/(요청 구역 사본), 감사 기록 줄 해시(photo_sha256 열이 줄에 들어감),
  근거 원장 7 해체(협력업체 신고 줄의 sha256).
실행: python photos.py --self-test
"""
import base64
import binascii
import hashlib
import os
import re

import evaluate as ev
import evidence as evd

MAX_BYTES = 8 * 1024 * 1024        # [가정] 휴대폰 사진 1장 상한 (서버·화면 같은 값)
DIR = "사진"
SHA_COL = "photo_sha256"
KIND_NAME = {"shore_check": "동바리확인", "after_check": "해체후점검", "report": "해체신고", "cylinder": "공시체"}
FILES = (("shore_checks.csv", "shore_check", "timestamp"), ("after_check.csv", "after_check", "date"), ("cylinders.csv", "cylinder", "test_date"))   # (파일, 종류, 시각 열)
B64 = re.compile(r"^[A-Za-z0-9+/]*={0,2}$")


def decode(s):
    """'data:image/…;base64,XXXX' 또는 base64 글자 → 바이트 (공백·줄바꿈 없는 표준 base64 만). 못 읽으면 ValueError."""
    s = str(s or "")
    if s.startswith("data:"):
        s = s.split(",", 1)[1] if "," in s else ""
    if not s or len(s) % 4 or not B64.match(s):
        raise ValueError("사진 자료를 읽지 못했습니다 — 사진을 다시 고르세요")
    try:
        return base64.b64decode(s, validate=True)
    except (binascii.Error, ValueError):
        raise ValueError("사진 자료를 읽지 못했습니다 — 사진을 다시 고르세요")


def ext_of(data):
    """앞 바이트 → 'jpg' | 'png' | None."""
    if data[:3] == b"\xff\xd8\xff":
        return "jpg"
    if data[:8] == b"\x89PNG\r\n\x1a\n":
        return "png"
    return None


def _sha(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def save(folder, data, zone, kind, at):
    """사진 바이트 → (상대 경로 '사진/YYYYMMDD/…', sha256). 검사 실패는 ValueError (한국어, 휴대폰에 그대로)."""
    if len(data) > MAX_BYTES:
        raise ValueError(f"사진이 너무 큽니다 ({len(data):,}바이트) — {MAX_BYTES:,}바이트(8 MB) 이하로 찍어 주세요")
    ext = ext_of(data)
    if ext is None:
        raise ValueError("사진은 JPEG·PNG 만 받습니다 — 휴대폰 카메라로 찍은 사진을 고르세요")
    sha = hashlib.sha256(data).hexdigest()
    day = os.path.join(folder, DIR, at.strftime("%Y%m%d"))
    os.makedirs(day, exist_ok=True)
    stem = re.sub(r"[^0-9A-Za-z가-힣_-]", "_", zone) + "_" + KIND_NAME[kind] + "_"
    same = sorted(n for n in os.listdir(day) if n.startswith(stem) and n.endswith(f"_{sha[:8]}.{ext}") and _sha(os.path.join(day, n)) == sha)
    name = same[0] if same else f"{stem}{at:%H%M%S}_{sha[:8]}.{ext}"
    if not same:
        with open(os.path.join(day, name), "xb") as f:      # 원본 바이트 그대로
            f.write(data)
    return f"{DIR}/{at:%Y%m%d}/{name}", sha


def rows(folder, zones=None, day=None):
    """CSV 의 서버 저장 사진(photo_sha256 있는 줄) → [{zone_id, when, kind, path, sha256}] 시각 순.
    같은 경로는 한 번 (해체 후 점검은 항목마다 같은 사진). zones = 그 구역만, day = 그 날짜(YYYY-MM-DD)까지."""
    out, seen = [], set()
    for name, kind, tcol in FILES:
        for r in ev.read(folder, name):
            sha, p, z, w = ((r.get(k) or "").strip() for k in (SHA_COL, "photo", "zone_id", tcol))
            if not sha or not p or p in seen or (zones is not None and z not in zones) or (day and w[:10] > day):
                continue
            seen.add(p)
            k = "report" if (r.get("source") or "").strip() == evd.REPORT_SOURCE else kind
            out.append({"zone_id": z, "when": w, "kind": KIND_NAME[k], "path": p, "sha256": sha})
    return sorted(out, key=lambda x: (x["when"], x["path"]))


if __name__ == "__main__":
    import shutil
    import sys
    import tempfile
    from datetime import datetime
    if "--self-test" not in sys.argv:
        raise SystemExit("python photos.py --self-test")
    t = tempfile.mkdtemp()
    try:
        at = datetime(2026, 12, 31, 9, 5, 7)
        jpg = b"\xff\xd8\xff\xe0\x00\x10JFIF\x00" + bytes(range(256)) * 4          # 가짜 JPEG (앞 바이트만 JPEG)
        p1, s1 = save(t, jpg, "9F-A1", "shore_check", at)
        assert p1 == f"사진/20261231/9F-A1_동바리확인_090507_{s1[:8]}.jpg", p1
        assert open(os.path.join(t, *p1.split("/")), "rb").read() == jpg                  # 원본 그대로
        assert save(t, jpg, "9F-A1", "shore_check", datetime(2026, 12, 31, 10))[0] == p1   # 같은 사진 두 번 → 같은 파일
        assert len(os.listdir(os.path.join(t, DIR, "20261231"))) == 1
        assert save(t, jpg, "9F-A1", "report", at)[0] != p1                              # 종류가 다르면 따로
        png = b"\x89PNG\r\n\x1a\n" + b"\x00" * 16
        p4, s4 = save(t, png, "9F/A2", "after_check", at)
        assert p4 == f"사진/20261231/9F_A2_해체후점검_090507_{s4[:8]}.png", p4
        for bad, msg in ((b"hello, this is text saved as photo.jpg", "JPEG·PNG"), (b"\xff\xd8\xff" + b"\x00" * MAX_BYTES, "너무 큽니다"), (b"", "JPEG·PNG")):
            try:
                save(t, bad, "9F-A1", "shore_check", at)
                raise AssertionError(msg)
            except ValueError as x:
                assert msg in str(x), x
        assert decode("data:image/jpeg;base64," + base64.b64encode(jpg).decode()) == jpg and decode(base64.b64encode(png).decode()) == png
        for bad in ("@@@@", "abc", "data:image/jpeg;base64", "YWJj\nZA=="):
            try:
                decode(bad)
                raise AssertionError(bad)
            except ValueError:
                pass
        with open(os.path.join(t, "shore_checks.csv"), "w", encoding="utf-8", newline="") as f:
            f.write(f"zone_id,timestamp,present,actor,photo,source,photo_sha256\n9F-A1,2026-12-31T09:05,예,박안전,{p1},,{s1}\n"
                    f"9F-A2,2026-12-31T08:00,아니오,최반장,a.jpg,협력업체 신고,\n9F-A1,2027-01-02T08:00,아니오,최반장,x.jpg,협력업체 신고,{s1}\n")
        with open(os.path.join(t, "after_check.csv"), "w", encoding="utf-8", newline="") as f:
            f.write(f"zone_id,date,item_code,result,by,photo,photo_sha256\n9F-A2,2026-12-31,AC-1,OK,박안전,{p4},{s4}\n9F-A2,2026-12-31,AC-2,OK,박안전,{p4},{s4}\n")
        r = rows(t, day="2026-12-31")
        assert [(x["zone_id"], x["kind"], x["path"]) for x in r] == [("9F-A2", "해체후점검", p4), ("9F-A1", "동바리확인", p1)], r   # 파일 이름만인 줄 빠짐, 같은 사진 한 번
        assert [x["kind"] for x in rows(t, {"9F-A1"})] == ["동바리확인", "해체신고"]
        print("photos self-check OK")
    finally:
        shutil.rmtree(t, ignore_errors=True)

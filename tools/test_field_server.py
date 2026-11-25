"""현장 입력 서버 + evaluate 강도 센서 시험 (표준 라이브러리만).
실행: python tools/test_field_server.py
"""
import csv
import json
import os
import time
import re
import shutil
import sys
import tempfile
import threading
import unittest
import urllib.error
import urllib.request
from datetime import datetime, timedelta

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(ROOT, "shoring"))
import field_server as fs   # noqa: E402
import evaluate as ev       # noqa: E402
import engine as e          # noqa: E402

SAMPLE = os.path.join(ROOT, "shoring", "sample", "data")
AT = datetime(2026, 12, 31, 9, 0)


def rows(folder, name):
    with open(os.path.join(folder, name), encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def _rm(path):
    """시험 폴더 지우기 — Windows 에서 방금 쓴 CSV 를 백신·색인이 잠깐 잡으면 rmtree 가 PermissionError(간헐 errors=1, 총괄 11-05).
    몇 번 다시 해 보고, 그래도 안 되면 남겨 둠(시험 결과와 무관)."""
    import time
    for _ in range(10):
        try:
            shutil.rmtree(path)
            return
        except FileNotFoundError:
            return
        except OSError:
            time.sleep(0.2)
    shutil.rmtree(path, ignore_errors=True)


class Server(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        self.data = os.path.join(self.tmp, "data")
        shutil.copytree(SAMPLE, self.data)
        self.srv = fs.serve(self.data, AT, "127.0.0.1", 0)
        threading.Thread(target=self.srv.serve_forever, daemon=True).start()
        self.base = f"http://127.0.0.1:{self.srv.server_address[1]}"
        self.n = 0

    def tearDown(self):
        self.srv.shutdown()
        self.srv.server_close()
        _rm(self.tmp)

    def call(self, path, body=None, token=True):
        url = self.base + path + (("&" if "?" in path else "?") + "t=" + self.srv.token if token else "")
        req = urllib.request.Request(url, data=None if body is None else json.dumps(body).encode(),
                                     headers={"Content-Type": "application/json", "User-Agent": "Mozilla/5.0 (Linux; Android 14) Chrome/130"})
        try:
            with urllib.request.urlopen(req, timeout=20) as r:
                return r.status, json.loads(r.read() or b"null") if "json" in r.headers["Content-Type"] else r.read()
        except urllib.error.HTTPError as x:
            return x.code, json.loads(x.read())

    def post(self, **b):
        self.n += 1
        b.setdefault("entered_by", "김현장")
        b.setdefault("client_id", f"testdev-{self.n}")
        return self.call("/api/measure", b)

    # ---- 보안 ----
    def test_view_floor(self):
        """F12 읽기 전용 층 화면: 보기 토큰 ≠ 입력 토큰(데이터 폴더에 고정), /api/floor·/view 는 v 로만, v 로 POST·입력 쪽은 거부, 층 이름 B1·PH, 지시 문구 없음"""
        v = self.srv.view_token
        self.assertNotEqual(v, self.srv.token)
        self.assertEqual(v, fs.view_token(self.data))                       # 파일에 고정 — 서버를 다시 켜도 같은 QR

        def get(path):
            try:
                with urllib.request.urlopen(self.base + path, timeout=20) as r:
                    return r.status, json.loads(r.read()) if "json" in r.headers["Content-Type"] else r.read()
            except urllib.error.HTTPError as x:
                return x.code, json.loads(x.read())
        code, j = get(f"/api/floor?f=9&v={v}")
        self.assertEqual((code, j["name"], j["kind"]), (200, "9F", "hold"), j)
        lines = {z["zone_id"]: z["line"] for z in j["zones"]}
        self.assertEqual(lines["9F-A1"], "해체 금지 — 감리 확인 전 (해체 승인 요청 가능)")
        self.assertEqual(lines["9F-A2"], "해체 금지 — 존치")
        self.assertEqual(j["status"], "동바리 해체 금지 — 2구역")
        self.assertFalse(any("하세요" in z["line"] for z in j["zones"]))      # 지시 문구 없음
        self.assertEqual(get(f"/api/floor?f=9&v={self.srv.token}")[0], 403)   # 입력 토큰으로는 안 열림
        self.assertEqual(get(f"/api/model?v={v}")[0], 403)                    # 보기 토큰으로 입력 쪽 안 열림
        self.assertEqual(get(f"/api/floor?v={v}")[0], 400)
        self.assertEqual(get(f"/api/floor?f=99&v={v}")[0], 404)
        code, html = get(f"/view?v={v}")
        self.assertEqual(code, 200)
        self.assertIn("<title>층 보기".encode(), html)
        req = urllib.request.Request(self.base + f"/api/measure?v={v}", data=json.dumps({"kind": "rebound"}).encode(), headers={"Content-Type": "application/json"})
        try:
            urllib.request.urlopen(req, timeout=20)
            self.fail("보기 토큰으로 저장됨")
        except urllib.error.HTTPError as x:
            self.assertEqual(x.code, 403)
        with open(os.path.join(self.data, "zones.csv"), "a", encoding="utf-8", newline="") as f:   # 층 이름 = 구역 ID 앞 (B1·PH)
            f.write("B1-A1,-1,0.0,0.0,10.0,9.0\nPH-A1,16,0.0,0.0,10.0,9.0\n")
        b1, ph = get(f"/api/floor?f=-1&v={v}")[1], get(f"/api/floor?f=16&v={v}")[1]
        self.assertEqual((b1["name"], ph["name"], b1["zones"][0]["line"]), ("B1", "PH", "해체 금지 — 타설 전"), (b1, ph))
        self.assertEqual([f["name"] for f in ph["floors"]][:2], ["B1", "9F"])

    def test_token(self):
        for path, body in (("/api/model", None), ("/api/recent", None), ("/api/measure", {"kind": "cylinder"})):
            code, j = self.call(path, body, token=False)
            self.assertEqual(code, 403, path)
            self.assertFalse(j["ok"])
        for path in ("/", "/view"):                                    # 11-12 0-7: 화면 껍데기(자료 없음)는 열쇠 없이 — 주소창에서 열쇠를 지워도 다시 열림
            self.assertEqual(self.call(path, None, token=False)[0], 200, path)
        code, html = self.call("/")
        self.assertEqual(code, 200)
        self.assertIn("<h1>현장 입력</h1>".encode(), html)   # 버튼 [현장 입력] 과 같은 이름 (총괄 11-05 ③)
        # F6: 12 시간 지난 주소는 401 (만료 안내), [새 주소] 뒤 옛 주소는 403·새 주소는 200
        self.srv.token_issued -= timedelta(hours=fs.TOKEN_TTL_H + 0.1)
        code, j = self.call("/api/model")
        self.assertEqual(code, 401)
        self.assertIn("만료", j["error"])
        old = self.srv.token
        fs.new_token(self.srv)
        self.assertNotEqual(old, self.srv.token)
        self.assertEqual(self.call("/api/model")[0], 200)

    # ---- /api/model ----
    def test_model(self):
        code, m = self.call("/api/model")
        self.assertEqual(code, 200)
        self.assertEqual(m["now"], "2026-12-31T09:00")
        self.assertEqual([f["floor"] for f in m["floors"]], [9, 10, 11, 12])
        z = {z["zone_id"]: z for f in m["floors"] for z in f["zones"]}
        # 샘플 정답 (CLAUDE.md): 9F A1·B1·C1·C2 요청 가능 → 상태 5, A2·B2 존치 (예측일 12-25 지남 → 9)
        for zid in ("9F-A1", "9F-B1", "9F-C1", "9F-C2"):
            self.assertEqual((z[zid]["decision"], z[zid]["status"]), ("해체 승인 요청 가능", 5), zid)
        for zid in ("9F-A2", "9F-B2"):
            self.assertTrue(z[zid]["decision"].startswith("존치"))
            self.assertEqual(z[zid]["status"], 9)
        self.assertEqual(z["10F-A1"]["status"], 3)          # 층수 게이트 NG, 예측일 전 → 양생 중
        self.assertEqual((z["9F-A1"]["x0"], z["9F-A1"]["y1"], z["9F-A1"]["pour_date"]), (0.0, 9.0, "2026-11-26"))
        s = {x["sensor_id"]: x for x in m["sensors"]}
        self.assertEqual(sorted(s), ["S1", "S2", "S3", "S4", "S5", "S6"])
        self.assertEqual((s["S1"]["floor"], s["S1"]["x"], s["S1"]["y"], s["S1"]["kind"]), (9, 15.0, 9.0, "sensor_temp"))
        self.assertEqual(s["S1"]["last"]["timestamp"], "2026-12-31T09:00")
        self.assertEqual([k["kind"] for k in m["kinds"]], ["cylinder", "sensor_temp", "sensor_strength", "rebound", "shore_check", "after_check", "pour", "shore_spec", "shore_inspect"])
        self.assertEqual([x["stage"] for x in m["inspect"]], ["설치 검측", "일상 점검", "해체 전", "해체 후"])
        self.assertEqual(m["inspect"][0]["items"][0], {"code": "SHR-B", "label": "수평연결재", "criterion": "높이 3.5 m 초과 → 2 m 이내마다 2방향", "source": "[확정] 안전보건규칙 제332조의2"})

    def test_pour(self):
        """타설 등록 (Revit [타설 등록] 창이 쓰는 종류 pour): 없는 구역·이미 타설·미래·시각·배합·fck 거절, 저장 = pours.csv 한 줄 (공통 열), 판정에 들어감"""
        with open(os.path.join(self.data, "zones.csv"), "a", encoding="utf-8") as f:
            f.write("13F-A1,13,0.0,0.0,10.0,9.0\n")
        ok = {"kind": "pour", "zone_id": "13F-A1", "pour_date": "2026-12-30", "pour_time": "7:30", "mix": "보통", "fck": "24"}
        for patch, msg in [({"zone_id": "13F-Z9"}, "모델에 없습니다"), ({"zone_id": "9F-A1"}, "이미 타설 기록"), ({"pour_date": "2027-01-01"}, "아직 오지 않은"),
                           ({"pour_time": "25:00"}, "타설 시각"), ({"mix": "몰라"}, "배합을 고르세요"), ({"fck": "70"}, "범위 밖")]:
            st, r = self.post(**{**ok, **patch})
            self.assertEqual(st, 400, patch)
            self.assertIn(msg, r["error"], patch)
        st, r = self.post(**ok)
        self.assertEqual((st, r["rows"], r["saved"], r["used_in_decision"]), (200, 1, "pours.csv", True))
        last = ev.read(self.data, "pours.csv")[-1]
        self.assertEqual((last["zone_id"], last["pour_date"], last["pour_time"], last["mix"], last["fck"], last["entered_by"]), ("13F-A1", "2026-12-30", "07:30", "보통", "24", "김현장"))
        st, r = self.post(**ok)
        self.assertIn("이미 타설 기록", r["error"])                                   # 두 번째는 거절 (같은 구역)

    def test_append_lock(self):
        """W7: 두 스레드가 같은 CSV 에 100줄씩 동시에 이어 써도 200줄 (잠금 파일), 남의 잠금이 안 풀리면 Bad·파일 그대로, 30 초 넘은 잠금은 지우고 씀"""
        import threading as th
        p = os.path.join(self.data, "lock_test.csv")
        def go(tag):
            for i in range(100):
                fs.append_rows(p, ["zone_id", "test_date"], [{"zone_id": f"{tag}{i}", "test_date": "2026-12-30"}])
        ts = [th.Thread(target=go, args=(k,)) for k in ("a", "b")]
        [x.start() for x in ts]
        [x.join() for x in ts]
        got = [r["zone_id"] for r in ev.read(self.data, "lock_test.csv")]
        self.assertEqual((len(got), len(set(got))), (200, 200))
        self.assertFalse(os.path.exists(p + ".lock"))
        open(p + ".lock", "w").close()
        old = fs.LOCK_TRIES, fs.LOCK_WAIT_S
        fs.LOCK_TRIES, fs.LOCK_WAIT_S = 3, 0.01
        try:
            with self.assertRaises(fs.Bad) as cm:
                fs.append_rows(p, ["zone_id", "test_date"], [{"zone_id": "x", "test_date": "2026-12-30"}])
            self.assertIn("다른 PC·프로그램이 쓰는 중", str(cm.exception))
            self.assertEqual(len(ev.read(self.data, "lock_test.csv")), 200)
            os.utime(p + ".lock", (time.time() - 60, time.time() - 60))   # 멈춘 프로그램이 남긴 잠금
            fs.append_rows(p, ["zone_id", "test_date"], [{"zone_id": "y", "test_date": "2026-12-30"}])
            self.assertEqual(len(ev.read(self.data, "lock_test.csv")), 201)
        finally:
            fs.LOCK_TRIES, fs.LOCK_WAIT_S = old

    def test_pour_lot(self):
        """v6.2 W1 타설 묶음: 타설 등록에 pour_id (같은 묶음은 같은 날·배합·fck), 공시체를 구역 대신 묶음으로 → zone_id 빈칸 + pour_id 한 줄,
        판정은 묶음의 모든 구역에 (사유 '공시체 (타설 … · n구역 공통)'), 없는 묶음·같은 공시체 두 번 거절"""
        with open(os.path.join(self.data, "zones.csv"), "a", encoding="utf-8") as f:
            f.write("13F-A1,13,0.0,0.0,10.0,9.0\n13F-B1,13,10.0,0.0,20.0,9.0\n")
        ok = {"kind": "pour", "zone_id": "13F-A1", "pour_date": "2026-12-20", "pour_time": "08:00", "mix": "보통", "fck": "24", "pour_id": "P-13F-1"}
        self.assertEqual(self.post(**ok)[0], 200)
        self.assertEqual(ev.read(self.data, "pours.csv")[-1]["pour_id"], "P-13F-1")
        st, r = self.post(**{**ok, "zone_id": "13F-B1", "pour_date": "2026-12-21"})
        self.assertEqual(st, 400)
        self.assertIn("같은 날·같은 배합·같은 fck 만 묶습니다", r["error"])
        self.assertEqual(self.post(**{**ok, "zone_id": "13F-B1"})[0], 200)
        cyl = {"kind": "cylinder", "zone_id": "", "pour_id": "P-13F-1", "test_date": "2026-12-30", "curing": "현장봉함", "f1": 20, "f2": 20, "f3": 20}
        st, r = self.post(**{**cyl, "pour_id": "P-없음"})
        self.assertEqual(st, 400)
        self.assertIn("타설 기록(pours.csv)에 없습니다", r["error"])
        st, r = self.post(**cyl)
        self.assertEqual((st, r["lot_zones"]), (200, ["13F-A1", "13F-B1"]), r)
        last = ev.read(self.data, "cylinders.csv")[-1]
        self.assertEqual((last["zone_id"], last["pour_id"], last["f2"]), ("", "P-13F-1", "20"))
        st, r = self.post(**{**cyl, "f2": "20.0"})
        self.assertIn("같은 공시체 기록이 이미 있습니다 (타설 P-13F-1", r["error"])
        got = {x["zone_id"]: x for x in ev.evaluate(self.data, AT)}
        self.assertEqual([(got[z]["f_cyl"], got[z]["cyl_test_date"]) for z in ("13F-A1", "13F-B1")], [(20.0, "2026-12-30")] * 2)   # 사유 태그는 게이트 통과 때 (test_lot.py)
        self.assertEqual([c["cyl_lot"] for c in ev.cyl_rows(self.data) if c["zone_id"].startswith("13F-")], ["P-13F-1 · 2구역 공통"] * 2)

    def test_shore_spec(self):
        """v6 동바리 사양·설치일 (Revit [동바리 사양] 창이 쓰는 종류 shore_spec): 없는 층·다른 층 구역·종류·규격·간격·이음·미래·타설일 뒤 거절,
        층 전체 줄과 구역 줄 따로 저장 (shore_spec.csv), 같은 층·구역 두 번째 거절, 판정 미사용"""
        ok = {"kind": "shore_spec", "floor": 9, "zone_id": "", "type": "파이프 서포트", "spec": "V4", "spacing_x_mm": 1200, "spacing_y_mm": "1200", "splices": 0,
              "install_date": "2026-11-23"}
        for patch, msg in [({"floor": 99}, "99층이 모델에 없습니다"), ({"floor": "아홉"}, "층 번호"), ({"zone_id": "10F-A1"}, "9F 에 없습니다"),
                           ({"type": "각재"}, "동바리 종류"), ({"spec": " "}, "규격 칸이 비어"), ({"spacing_x_mm": 50}, "범위 밖"), ({"splices": 1.5}, "정수"),
                           ({"install_date": "2027-01-01"}, "아직 오지 않은"), ({"install_date": "2026-11-27"}, "타설일 2026-11-26 보다 뒤")]:
            st, r = self.post(**{**ok, **patch})
            self.assertEqual(st, 400, patch)
            self.assertIn(msg, r["error"], patch)
        st, r = self.post(**ok)
        self.assertEqual((st, r["rows"], r["saved"], r["used_in_decision"]), (200, 1, "shore_spec.csv", False))
        st, r = self.post(**{**ok, "zone_id": "9F-B1", "type": "시스템 동바리", "spec": "SY-1", "spacing_x_mm": 1500, "spacing_y_mm": 1500})
        self.assertEqual(st, 200, r)
        rows_ = ev.read(self.data, "shore_spec.csv")
        self.assertEqual([(x["floor"], x["zone_id"], x["type"], x["spacing_y_mm"], x["splices"], x["install_date"]) for x in rows_],
                         [("9", "", "파이프 서포트", "1200", "0", "2026-11-23"), ("9", "9F-B1", "시스템 동바리", "1500", "0", "2026-11-23")])
        st, r = self.post(**{**ok, "install_date": "2026-11-22"})          # 11-12 2-1: 옛 설치일보다 앞 → 거부
        self.assertIn("9F 층 전체 사양 기록이 있습니다 (설치일 2026-11-23) — 다시 넣으려면 그 날 이후", r["error"])
        st, r = self.post(**{**ok, "spacing_x_mm": 900})                  # 같은 날 다시 넣기 = 정정 (마지막 줄을 씀)
        self.assertEqual(st, 200, r)
        self.assertEqual(ev.read(self.data, "shore_spec.csv")[-1]["spacing_x_mm"], "900")

    def test_shore_inspect(self):
        """F16 동바리 단위 현장 점검: 항목마다 한 줄 (install_inspection.csv 넓힘, 사진 없음), 설치 NG → 판정 '존치 (설치 결함 기록)', 다시 점검 OK → 풀림,
        동바리 소속·NG 인데 동바리 없음·미래 시각·없는 단계·항목 두 번 거절, 층 화면(입력 토큰) 동바리 점 빨강"""
        open(os.path.join(self.data, "shores.csv"), "w", encoding="utf-8").write(
            "id,zone_id,x,y,field_label\n9F-B1-R01C01,9F-B1,10.6,0.6,B1-1\n9F-B1-R01C02,9F-B1,11.8,0.6,\n9F-A2-R01C01,9F-A2,0.6,9.6,\n")
        bad = [({"stage": "없는 단계"}, "점검 단계"), ({"items": [{"item_code": "SHR-B", "result": "NG", "shore_ids": ["9F-A2-R01C01"]}]}, "구역 9F-B1 에 없습니다"),
               ({"items": [{"item_code": "SHR-B", "result": "NG"}]}, "문제 동바리를 평면에서"), ({"timestamp": "2027-01-01T08:00"}, "아직 오지 않은"),
               ({"items": [{"item_code": "SHR-B", "result": "OK"}, {"item_code": "SHR-B", "result": "OK"}]}, "두 번"),
               ({"items": [{"item_code": "DLY-D", "result": "OK"}]}, "항목 'DLY-D'")]
        for over, msg in bad:
            st, r = self.post(**{"kind": "shore_inspect", "zone_id": "9F-B1", "stage": "설치 검측", "items": [{"item_code": "SHR-B", "result": "OK"}], **over})
            self.assertEqual(st, 400, (over, r))
            self.assertIn(msg, r["error"])
        self.assertFalse(os.path.exists(os.path.join(self.data, "install_inspection.csv")))
        st, r = self.post(kind="shore_inspect", zone_id="9F-B1", stage="설치 검측", timestamp="2026-12-31T08:30",
                          items=[{"item_code": "SHR-B", "result": "NG", "shore_ids": ["9F-B1-R01C02", "9F-B1-R01C02"]}, {"item_code": "SHR-S", "result": "OK"}])
        self.assertEqual(st, 200, r)
        self.assertEqual((r["zone"]["decision"], r["open_ng"], r["used_in_decision"]), ("존치 (설치 결함 기록)", "수평연결재 9F-B1-R01C02", True))
        rows = fs.read_rows(os.path.join(self.data, "install_inspection.csv"))
        self.assertEqual([(x["item_code"], x["result"], x["shore_ids"], x["stage"], x["timestamp"], x["photo_path"], x["source"]) for x in rows],
                         [("SHR-B", "NG", "9F-B1-R01C02", "설치 검측", "2026-12-31T08:30", "", "현장 점검"), ("SHR-S", "OK", "", "설치 검측", "2026-12-31T08:30", "", "현장 점검")])
        self.assertNotIn("photo_sha256", rows[0])
        st, f = self.call("/api/floor?f=9")                                        # 입력 토큰으로 층 평면·동바리 점
        self.assertEqual(st, 200, f)
        sh = {x["id"]: x for x in f["shores"]}
        self.assertEqual((sh["9F-B1-R01C02"]["ng"], sh["9F-B1-R01C01"]["ng"], sh["9F-B1-R01C01"]["label"]), (True, False, "B1-1"))
        self.assertEqual(next(z["ng"] for z in f["zones"] if z["zone_id"] == "9F-B1"), "수평연결재 9F-B1-R01C02")
        self.assertEqual(len(f["zone_rects"]), 6)
        st, r = self.post(kind="shore_inspect", zone_id="9F-B1", stage="설치 검측", timestamp="2026-12-31T08:50", items=[{"item_code": "SHR-B", "result": "OK"}])
        self.assertEqual((st, r["zone"]["decision"], r["open_ng"]), (200, "해체 승인 요청 가능", ""))

    # ---- 종류별 저장 ----
    def test_shore_check_feeds_pour_gate(self):   # 동바리 확인 → shore_checks.csv → pour_gate (화정형 타설 전 게이트)
        import pour_gate as pg
        code, j = self.post(kind="shore_check", zone_id="9F-A1", timestamp="2026-12-31 08:30", present="아니오", photo="9F-A1.jpg")
        self.assertEqual(code, 200, j)
        self.assertFalse(j["used_in_decision"])
        r = rows(self.data, "shore_checks.csv")[-1]
        self.assertEqual((r["zone_id"], r["present"], r["actor"], r["photo"]), ("9F-A1", "아니오", "김현장", "9F-A1.jpg"))
        g = pg.pour_gate(self.data, "12F-A1", AT)
        self.assertEqual(g["decision"], "타설 보류")
        self.assertIn("9F-A1", g["reason"])
        code, j = self.post(kind="shore_check", zone_id="9F-A1", timestamp="2026-12-31 08:30", present="몰라")
        self.assertEqual(code, 400)

    def test_pour_gate_and_evidence_on_phone(self):   # 휴대폰 구역 카드: 타설 전 확인은 타설된 최상층 위만, 근거 n/8 은 모든 구역
        r12 = [r for r in fs.ev.read(self.data, "zones.csv") if r["zone_id"] == "12F-A1"][0]
        box = ",".join(r12[k] for k in ("x0", "y0", "x1", "y1"))
        with open(os.path.join(self.data, "zones.csv"), "a", encoding="utf-8", newline="") as f:
            f.write(f"13F-A1,13,{box}\n8F-A1,8,{box}\n")
        _, m = self.call("/api/model")
        z = {z["zone_id"]: z for f in m["floors"] for z in f["zones"]}
        self.assertEqual(z["13F-A1"]["pour_gate"]["decision"], "타설 보류")
        self.assertIsNone(z["8F-A1"]["pour_gate"])            # 아래층(타설 기록 없음)은 보류로 보이지 않게
        self.assertIsNone(z["9F-A1"]["pour_gate"])            # 타설된 구역
        self.assertEqual(z["9F-A1"]["evidence_stage"], 0)     # 샘플엔 1 설치검측이 없어 0

    def test_floor_name(self):                    # KBIM B1·PH: 층 이름 = 첫 구역 ID 의 '-' 앞
        self.assertEqual(fs.floor_name(-1, [{"zone_id": "B1-A1"}]), "B1")
        self.assertEqual(fs.floor_name(12, [{"zone_id": "PH-A1"}]), "PH")
        self.assertEqual(fs.floor_name(-2, [{"zone_id": "Z9"}]), "B2")

    def test_report(self):
        """F14 협력업체 해체 신고: 신고 토큰 r 로만 POST /api/report (보기·입력 토큰 403, r 로 입력 쪽 403). 승인 기록 없이 '뺐음' → 알림 맨 위 무단 해체 의심,
        감리 승인 기록 뒤 '뺐음' → 경고 없음·근거 원장 7 해체(실제 시각). 같은 client_id 는 한 줄, 이름 없으면 400"""
        import alerts as al
        import approval as ap
        import evidence as evd
        rt = self.srv.report_token
        self.assertNotIn(rt, (self.srv.token, self.srv.view_token))
        self.assertEqual(rt, fs.view_token(self.data, fs.REPORT_FILE))          # 데이터 폴더에 고정

        def req(method, path, body=None):
            r = urllib.request.Request(self.base + path, data=None if body is None else json.dumps(body).encode(), method=method,
                                       headers={"Content-Type": "application/json"})
            try:
                with urllib.request.urlopen(r, timeout=20) as x:
                    return x.status, json.loads(x.read())
            except urllib.error.HTTPError as x:
                return x.code, json.loads(x.read())
        rep = {"zone_id": "9F-A2", "present": "아니오", "name": "최반장", "company": "가나건설", "client_id": "rep-1"}
        codes = [req("POST", f"/api/report?v={self.srv.view_token}", rep)[0], req("POST", f"/api/report?t={self.srv.token}", rep)[0],
                 req("POST", f"/api/measure?r={rt}", {"kind": "rebound"})[0], req("GET", f"/api/model?r={rt}")[0], req("GET", "/api/floor?f=9&r=wrong")[0]]
        self.assertEqual(codes, [403, 403, 403, 403, 403])
        self.assertFalse(os.path.exists(os.path.join(self.data, "shore_checks.csv")))
        self.assertTrue(req("GET", f"/api/floor?f=9&r={rt}")[1]["can_report"])
        self.assertFalse(req("GET", f"/api/floor?f=9&v={self.srv.view_token}")[1]["can_report"])
        # 9F-A2: 감리 승인 기록 없음 → 저장, 알림 맨 위 무단 해체 의심, 층 화면 띠도
        code, j = req("POST", f"/api/report?r={rt}", rep)
        self.assertEqual((code, j["message"], j["timestamp"], j["duplicate"]), (200, "신고 저장됨 — 감리 승인 기록 없음: 현장 확인 요청됨", "2026-12-31T09:00", False), j)
        self.assertTrue(req("POST", f"/api/report?r={rt}", rep)[1]["duplicate"])
        sc = rows(self.data, "shore_checks.csv")
        self.assertEqual([(r["zone_id"], r["present"], r["actor"], r["source"], r["company"], r["client_id"]) for r in sc],
                         [("9F-A2", "아니오", "최반장", "협력업체 신고", "가나건설", "rep-1")])
        a = al.alerts(self.data, AT)
        self.assertEqual((a[0]["kind"], a[0]["zone_id"]), ("무단 해체 의심", "9F-A2"), a[:2])
        self.assertEqual(self.call("/api/model")[1]["alerts"][0]["zone_id"], "9F-A2")
        self.assertEqual(req("GET", f"/api/floor?f=9&r={rt}")[1]["status"], "무단 해체 의심 — 확인 필요: 9F-A2")
        # 9F-A1: 감리 승인 기록 뒤 '뺐음' → 경고 없음, 근거 원장 7 해체 (결과 = 실제 시각)
        z = next(z for f in self.call("/api/model")[1]["floors"] for z in f["zones"] if z["zone_id"] == "9F-A1")
        ap.record(self.data, "9F-A1", "감리 승인 기록", "이감리", z, now=datetime(2026, 12, 31, 8))
        j = req("POST", f"/api/report?r={rt}", {**rep, "zone_id": "9F-A1", "company": "", "client_id": "rep-2"})[1]
        self.assertEqual(j["message"], "신고 저장됨 — 감리 승인 기록 있음: 해체 시각 기록됨", j)
        self.assertNotIn("9F-A1", evd.removal_watch(self.data, AT.date()))
        self.assertNotIn("9F-A1", {x["zone_id"] for x in al.alerts(self.data, AT) if x["kind"] == "무단 해체 의심"})
        e7 = [x for x in evd.entries(self.data) if x["stage"] == 7]
        self.assertEqual([(x["zone_id"], x["result"], x["by"]) for x in e7], [("9F-A2", "2026-12-31T09:00", "최반장"), ("9F-A1", "2026-12-31T09:00", "최반장")])
        self.assertEqual(evd.ledger_at(self.data, AT.date())["9F-A1"]["dates"][7], "2026-12-31")
        self.assertEqual(rows(self.data, "shore_checks.csv")[-1]["company"], "")
        self.assertEqual(req("POST", f"/api/report?r={rt}", {**rep, "present": "예", "client_id": "rep-3"})[1]["message"], "신고 저장됨 — 동바리 다시 세움")
        self.assertEqual(req("POST", f"/api/report?r={rt}", {**rep, "name": " ", "client_id": "rep-4"})[0], 400)
        self.assertEqual(req("POST", f"/api/report?r={rt}", {**rep, "zone_id": "XX", "client_id": "rep-5"})[0], 400)
        self.assertEqual(len(rows(self.data, "shore_checks.csv")), 3)
        # 오프라인 저장 뒤 재전송: timestamp = 서버가 받은 시각, 기기 시각은 note 에만 (위조 방지)
        j = req("POST", f"/api/report?r={rt}", {**rep, "zone_id": "9F-B1", "client_id": "rep-7", "device_at": "2026-12-31T08:40"})[1]
        last = rows(self.data, "shore_checks.csv")[-1]
        self.assertEqual((j["timestamp"], last["timestamp"], last["note"]), ("2026-12-31T09:00", "2026-12-31T09:00", "기기 시각 2026-12-31T08:40 (오프라인 저장 뒤 전송)"), last)
        self.assertEqual(rows(self.data, "shore_checks.csv")[0]["note"], "")

    def test_photo(self):
        """F15 현장 사진: 동바리 확인·해체 후 점검·해체 신고에 사진 1장 → 사진/YYYYMMDD/{구역}_{종류}_{HHMMSS}_{sha8}.jpg|png 원본 그대로,
        CSV photo = 상대 경로 + photo_sha256. 텍스트(이름만 .jpg)·크기 초과·base64 아님 → 400 (줄·파일 없음), 같은 사진 두 번 = 같은 파일.
        슬래브 이력서 '현장 사진' 줄·검측 패키지 첨부/사진/·근거 원장 7 sha256·감사 기록에 들어감"""
        import base64
        import hashlib
        import audit_export as ax
        import evidence as evd
        import package as pk
        import photos as ph
        import slab_record as sr
        jpg = b"\xff\xd8\xff\xe0\x00\x10JFIF\x00" + bytes(range(256)) * 8
        data, sha = "data:image/jpeg;base64," + base64.b64encode(jpg).decode(), hashlib.sha256(jpg).hexdigest()
        code, j = self.post(kind="shore_check", zone_id="9F-A1", timestamp="2026-12-31T08:30", present="예", photo_data=data)
        want = f"사진/20261231/9F-A1_동바리확인_083000_{sha[:8]}.jpg"   # 이름 시각 = 기록 시각 08:30 (서버 지금 09:00 아님, 총괄 29차)
        self.assertEqual((code, j.get("photo")), (200, want), j)
        with open(os.path.join(self.data, *want.split("/")), "rb") as f:
            self.assertEqual(f.read(), jpg)                                           # 원본 바이트 그대로
        r = rows(self.data, "shore_checks.csv")[-1]
        self.assertEqual((r["photo"], r["photo_sha256"]), (want, sha))
        self.assertEqual(self.post(kind="shore_check", zone_id="9F-A1", timestamp="2026-12-31T08:40", present="예", photo_data=data)[1]["photo"], want)
        self.assertEqual(os.listdir(os.path.join(self.data, "사진", "20261231")), [want.split("/")[-1]])   # 같은 사진 두 번 → 같은 파일
        n0 = len(rows(self.data, "shore_checks.csv"))
        for bad, msg in ((base64.b64encode(b"not a photo, just text saved as x.jpg").decode(), "JPEG·PNG"),
                         (base64.b64encode(b"\xff\xd8\xff" + b"\x00" * ph.MAX_BYTES).decode(), "너무 큽니다"), ("@@@@", "읽지 못했습니다")):
            code, j = self.post(kind="shore_check", zone_id="9F-A2", timestamp="2026-12-31T08:30", present="예", photo_data=bad)
            self.assertEqual(code, 400, j)
            self.assertIn(msg, j["error"])
        self.assertEqual(len(rows(self.data, "shore_checks.csv")), n0)                 # 거절 → 줄 없음
        self.assertEqual(len(os.listdir(os.path.join(self.data, "사진", "20261231"))), 1)   # 거절 → 파일 없음
        png = b"\x89PNG\r\n\x1a\n" + bytes(300)
        code, j = self.post(kind="after_check", zone_id="9F-A2", date="2026-12-31", items={"AC-1": "예", "AC-2": "아니오"}, photo_data=base64.b64encode(png).decode())
        self.assertTrue(code == 200 and j["photo"].startswith("사진/20261231/9F-A2_해체후점검_090000_") and j["photo"].endswith(".png"), j)
        self.assertTrue(all(x["photo"] == j["photo"] and x["photo_sha256"] for x in rows(self.data, "after_check.csv")[-2:]))
        up = b"\xff\xd8\xff\xe1" + bytes(range(200))                                                       # ① 위 3개층(10F-A1, 9F-A1 위 평면 겹침) 확인 사진
        u1 = self.post(kind="shore_check", zone_id="10F-A1", timestamp="2026-12-31T08:50", present="예", photo_data=base64.b64encode(up).decode())[1]["photo"]
        u2 = self.post(kind="shore_check", zone_id="10F-A1", timestamp="2026-12-29T08:00", present="예", photo_data=base64.b64encode(up + b"x").decode())[1]["photo"]
        # 검측 패키지는 9F-A1 해체 신고 전에 — 신고(상태 7 해체 완료) 뒤에는 요청 구역에서 빠짐 (11-09 request_rows)
        out, files, req_z = pk.build(self.data, AT, "시험")
        names = [f for f, _, _ in files]
        self.assertIn("첨부/사진/" + want.split("/")[-1], names)
        self.assertNotIn("첨부/사진/" + j["photo"].split("/")[-1], names)                            # 9F-A2 는 요청 구역 아님
        self.assertIn("첨부/사진/" + u1.split("/")[-1], names)                                       # ① 대상의 최근 24 h 확인 사진
        self.assertNotIn("첨부/사진/" + u2.split("/")[-1], names)                                    # 24 h 넘은 것은 아님
        req = urllib.request.Request(self.base + f"/api/report?r={self.srv.report_token}", headers={"Content-Type": "application/json"},
                                     data=json.dumps({"zone_id": "9F-A1", "present": "아니오", "name": "최반장", "client_id": "rep-p1", "photo_data": data}).encode())
        with urllib.request.urlopen(req, timeout=20) as x:
            jr = json.loads(x.read())
        self.assertEqual(jr["photo"], f"사진/20261231/9F-A1_해체신고_090000_{sha[:8]}.jpg")
        self.assertEqual([x["sha256"] for x in evd.entries(self.data) if x["stage"] == 7], [sha])          # 근거 원장 7 해체 = 신고 사진 SHA-256
        rec = sr.records(self.data, AT, "9F-A1")[0]
        self.assertEqual([(x["kind"], x["path"]) for x in rec["photos"]], [("동바리확인", want), ("해체신고", jr["photo"])])
        self.assertIn(f"{want} · <small>{sha[:12]}</small>", sr.page([rec], "시험", AT))
        self.assertNotIn("photos", sr.records(self.data, AT, "9F-B1")[0])                                # 사진 없는 구역 = 키 없음 (지문 그대로)
        self.assertTrue(any(a["detail"] == f"{want} · sha {sha[:12]}" for a in ax.rows(self.data)))                # 감사 기록: 경로 · sha 앞 12자

    def test_approval_record_status(self):        # 감리 승인 기록 → 상태 6 (approvals.csv, 프로그램은 승인하지 않고 기록만)
        import approval as ap
        _, m = self.call("/api/model")
        z = next(z for f in m["floors"] for z in f["zones"] if z["zone_id"] == "9F-A1")
        self.assertEqual(z["status"], 5)
        ap.record(self.data, "9F-A1", "감리 승인 기록", "이감리", z)
        _, m = self.call("/api/model")
        z = next(z for f in m["floors"] for z in f["zones"] if z["zone_id"] == "9F-A1")
        self.assertEqual(z["status"], 6)

    def test_other_project_queue_refused(self):      # 감사 A-07: 다른 데이터 폴더에서 쌓인 오프라인 기록
        _, m = self.call("/api/model")
        code, j = self.post(kind="cylinder", zone_id="9F-A2", test_date="2026-12-31", curing="현장봉함", f1=25, f2=25, f3=25,
                            project_id="0123456789")
        self.assertEqual(code, 400, j)
        self.assertIn("다른 프로젝트", j["error"])
        self.assertEqual(len(rows(self.data, "cylinders.csv")), 6)        # 저장 안 됨
        code, j = self.post(kind="cylinder", zone_id="9F-A2", test_date="2026-12-31", curing="현장봉함", f1=25, f2=25, f3=25,
                            project_id=m["project_id"])
        self.assertEqual(code, 200, j)

    def test_cylinder(self):
        code, j = self.post(kind="cylinder", zone_id="9F-A2", test_date="2026-12-31", curing="현장봉함", f1=25.0, f2="25,4", f3=25.8, note="=SUM(A1)")
        self.assertEqual(code, 200, j)
        self.assertEqual(j["saved"], "cylinders.csv")
        self.assertEqual(j["zone"]["zone_id"], "9F-A2")
        self.assertEqual(j["zone"]["cyl_test_date"], "2026-12-31")        # 새 공시체가 판정에 쓰임
        r = rows(self.data, "cylinders.csv")
        self.assertEqual(len(r), 7)
        self.assertEqual({k: r[-1][k] for k in ("zone_id", "test_date", "curing", "f1", "f2", "f3", "entered_by", "client_id", "device")},
                         {"zone_id": "9F-A2", "test_date": "2026-12-31", "curing": "현장봉함", "f1": "25", "f2": "25.4", "f3": "25.8",
                          "entered_by": "김현장", "client_id": "testdev-1", "device": "Android Chrome"})
        self.assertEqual(r[-1]["note"], "'=SUM(A1)")                       # 엑셀 수식 막기
        self.assertEqual(r[0]["entered_by"], "")                           # 기존 줄은 공통 열 빈칸
        with open(os.path.join(self.data, "cylinders.csv"), encoding="utf-8-sig") as f:
            self.assertEqual(f.readline().strip(), "zone_id,test_date,curing,f1,f2,f3,entered_by,entered_at,device,note,client_id")
        ev.evaluate(self.data, AT)                                         # 엔진이 늘어난 파일을 그대로 읽는다

    def test_cylinder_same_twice(self):                  # 61차 ⑤: 같은 공시체(구역·시험일·양생·f1–f3)를 다른 client_id 로 다시 → 400, 줄 그대로
        b = dict(kind="cylinder", zone_id="9F-A2", test_date="2026-12-31", curing="현장봉함", f1=25.0, f2=25.4, f3=25.8)
        self.assertEqual(self.post(**b)[0], 200)
        n = len(rows(self.data, "cylinders.csv"))
        code, j = self.post(**{**b, "f2": "25.40"})
        self.assertEqual((code, j.get("error")), (400, "같은 공시체 기록이 이미 있습니다 (9F-A2 2026-12-31 현장봉함 25 · 25.4 · 25.8 MPa) — 다시 넣지 않아도 됩니다"))
        self.assertEqual(len(rows(self.data, "cylinders.csv")), n)
        self.assertEqual(self.post(**{**b, "curing": "현장수중"})[0], 200)   # 양생이 다르면 다른 공시체

    def test_cylinder_photo(self):                       # 11-12 2-4 웹 #7: 공시체 사진 1장 → 사진/시험일/…_공시체_…, cylinders.csv photo·photo_sha256, 사진 목록(photos.rows)에 '공시체'
        import base64
        import hashlib
        import photos as ph
        jpg = b"\xff\xd8\xff\xe0\x00\x10JFIF\x00" + bytes(range(256)) * 4
        code, j = self.post(kind="cylinder", zone_id="9F-B2", test_date="2026-12-31", curing="현장봉함", f1=25, f2=25.5, f3=26,
                            photo_data="data:image/jpeg;base64," + base64.b64encode(jpg).decode())
        self.assertEqual(code, 200, j)
        self.assertTrue(j["photo"].startswith("사진/20261231/9F-B2_공시체_"), j)
        r = rows(self.data, "cylinders.csv")[-1]
        self.assertEqual((r["photo"], r["photo_sha256"]), (j["photo"], hashlib.sha256(jpg).hexdigest()))
        self.assertIn(("9F-B2", "공시체"), [(x["zone_id"], x["kind"]) for x in ph.rows(self.data)])

    def test_cylinder_sample_info(self):                 # F2: 채취 정보는 선택 열 — 값이 있을 때만 머리에 더하고, 판정은 그대로
        before = {r["zone_id"]: r["decision"] for r in ev.evaluate(self.data, AT)}
        code, j = self.post(kind="cylinder", zone_id="9F-A2", test_date="2026-12-31", curing="현장봉함", f1=25.0, f2=25.2, f3=25.4)
        self.assertEqual(code, 200, j)
        with open(os.path.join(self.data, "cylinders.csv"), encoding="utf-8-sig") as f:
            self.assertNotIn("pour_id", f.readline())                    # 값이 없으면 열을 더하지 않음
        code, j = self.post(kind="cylinder", zone_id="9F-B2", test_date="2026-12-31", curing="현장봉함", f1=25.0, f2=25.2, f3=25.4,
                            pour_id="P-9F-2", sampled_at="2026-11-26 08:40", supplier="=한빛레미콘", truck_no="12가3456", storage_zone="9F-B2", photo="cyl.jpg")
        self.assertEqual(code, 200, j)
        r = rows(self.data, "cylinders.csv")
        self.assertEqual({k: r[-1][k] for k in fs.SAMPLE_COLS if k != "photo_sha256"},   # 사진 SHA 열은 사진을 올린 줄에만 (11-12)
                         {"pour_id": "P-9F-2", "sampled_at": "2026-11-26T08:40", "supplier": "'=한빛레미콘", "truck_no": "12가3456", "storage_zone": "9F-B2", "photo": "cyl.jpg"})
        self.assertEqual(r[-2]["pour_id"], "")                           # 앞 줄은 빈칸
        after = {x["zone_id"]: x["decision"] for x in ev.evaluate(self.data, AT)}
        self.assertEqual(before, after)                                  # 이 두 줄은 25.2 평균이라 결론이 같은 구역만 — 판정 계산은 채취 정보를 안 씀
        code, j = self.post(kind="cylinder", zone_id="9F-B2", test_date="2026-12-31", curing="현장봉함", f1=25, f2=25, f3=25, sampled_at="2026-11-20 08:00")
        self.assertEqual(code, 400)
        self.assertIn("타설일 2026-11-26 보다 앞", j["error"])

    def test_after_check(self):                          # F3: 해체 후 점검 5항목 → after_check.csv, 결과보고(원장 8) 뒤 미완이면 근거 원장 경고
        code, j = self.post(kind="after_check", zone_id="9F-A1", date="2026-12-31", items={"AC-1": "예", "AC-4": "아니오"})
        self.assertEqual(code, 200, j)
        self.assertFalse(j["used_in_decision"])
        r = rows(self.data, "after_check.csv")
        self.assertEqual([(x["item_code"], x["result"], x["by"]) for x in r], [("AC-1", "OK", "김현장"), ("AC-4", "NG", "김현장")])
        self.assertEqual(self.post(kind="after_check", zone_id="9F-A1", date="2026-12-31", items={"AC-2": "몰라"})[0], 400)
        self.assertEqual(self.post(kind="after_check", zone_id="9F-A1", date="2026-12-31", items={})[0], 400)
        code, j = self.post(kind="after_check", zone_id="9F-A1", date="2026-12-31", settle={"p1": "1,5", "P2": 0})   # 11-12 G9 침하 계측만 넣어도 됨
        self.assertEqual(code, 200, j)
        self.assertEqual([(x["item_code"], x["result"]) for x in rows(self.data, "after_check.csv")[-2:]], [("SET-P1", "1.5"), ("SET-P2", "0")])
        self.assertIn("침하 측정 지점", self.post(kind="after_check", zone_id="9F-A1", date="2026-12-31", settle={"P 1": 1})[1]["error"])
        self.assertIn("범위 밖", self.post(kind="after_check", zone_id="9F-A1", date="2026-12-31", settle={"P1": 300})[1]["error"])
        import evidence as evd
        evd.add(self.data, "9F-A1", "2026-12-31", 8, "박안전", "R-1")
        w = evd.ledger_at(self.data, "2026-12-31")["9F-A1"]["warnings"]
        self.assertIn("해체 후 점검 미완 — 9F-A1: AC-2 각 층 같은 위치(상하 정렬), AC-3 연직도, AC-4 자재 변형·침하 NG, AC-5 콘크리트 면 결함", w)

    def test_sensor_temp_existing_and_paste(self):
        code, j = self.post(kind="sensor_temp", sensor_id="S1", floor=9, timestamp="2026-12-31 10:00", temp_c=6.5)
        self.assertEqual(code, 200, j)
        self.assertEqual((j["saved"], j["rows"], j["zone"]["zone_id"]), ("sensors.csv", 1, "9F-B1"))   # S1 (15, 9) → 경계, 먼저 나오는 구역
        self.assertEqual(rows(self.data, "sensors.csv")[-1]["note"], "")                                 # 빈 메모는 빈칸
        code, j = self.post(kind="sensor_temp", sensor_id="S1", floor=9, timestamp="2026-12-31T10:00", temp_c=9)
        self.assertEqual(code, 400)                                                                    # 한 건 입력의 같은 시각 → 알림
        self.assertIn("이미 있습니다", j["error"])
        last = rows(self.data, "sensors.csv")[-1]
        self.assertEqual((last["sensor_id"], last["x"], last["y"], last["timestamp"], last["temp_c"]), ("S1", "15", "9", "2026-12-31T10:00", "6.5"))
        # 붙여넣기: 겹치는 시각은 건너뜀
        rd = [{"timestamp": "2026-12-31T10:00", "temp_c": 6.5}, {"timestamp": "2026/12/31 11:00:00", "temp_c": 7}]
        code, j = self.post(kind="sensor_temp", sensor_id="S1", floor=9, readings=rd)
        self.assertEqual((code, j["rows"], j["skipped"]), (200, 1, 1), j)
        n = len(rows(self.data, "sensors.csv"))
        self.assertEqual(rows(self.data, "sensors.csv")[-1]["timestamp"], "2026-12-31T11:00")
        # 등록된 센서 위치를 다르게 보내면 거절
        code, j = self.post(kind="sensor_temp", sensor_id="S1", floor=9, x=3, y=3, timestamp="2026-12-31T08:00", temp_c=5)
        self.assertEqual(code, 400)
        self.assertIn("새 ID", j["error"])
        self.assertEqual(len(rows(self.data, "sensors.csv")), n)

    def test_sensor_strength_new(self):
        self.assertFalse(os.path.exists(os.path.join(self.data, "sensor_strength.csv")))
        code, j = self.post(kind="sensor_strength", sensor_id="W1", floor=9, x=25, y=4.5, timestamp="2026-12-31T08:00", mpa=20.0)
        self.assertEqual(code, 200, j)
        self.assertTrue(j["new_sensor"])
        self.assertEqual(j["zone"]["zone_id"], "9F-C1")
        # 강도 센서 20 MPa < 24 → C1 존치, 출처에 강도 센서
        self.assertEqual(j["zone"]["decision"], "존치 (구역 최소 강도 미달)")
        self.assertIn("강도 센서 1개 IDW", j["zone"]["est_source"])
        with open(os.path.join(self.data, "sensor_strength.csv"), encoding="utf-8-sig") as f:
            self.assertEqual(f.readline().strip(), "sensor_id,floor,x,y,timestamp,mpa,entered_by,entered_at,device,note,client_id")
        code, m = self.call("/api/model")
        w = next(s for s in m["sensors"] if s["sensor_id"] == "W1")
        self.assertEqual((w["kind"], w["last"]["mpa"]), ("sensor_strength", 20.0))
        # 같은 ID 를 온도 센서로 쓰면 거절
        code, j = self.post(kind="sensor_temp", sensor_id="W1", floor=9, timestamp="2026-12-31T08:00", temp_c=5)
        self.assertEqual(code, 400)
        self.assertIn("강도 센서", j["error"])

    def test_rebound(self):
        code, j = self.post(kind="rebound", zone_id="9F-B1", test_date="2026-12-30", r_value=41)
        self.assertEqual(code, 200, j)
        self.assertFalse(j["used_in_decision"])
        r = rows(self.data, "rebound.csv")[-1]
        self.assertEqual((r["zone_id"], r["x"], r["y"], r["r_value"]), ("9F-B1", "15", "4.5", "41"))   # 위치 없으면 구역 가운데
        self.assertEqual(j["zone"]["decision"], "해체 승인 요청 가능")                                  # 판정 그대로

    # ---- 중복 ----
    def test_dedup(self):
        b = dict(kind="cylinder", zone_id="9F-A1", test_date="2026-12-31", curing="현장봉함", f1=25, f2=25, f3=25, client_id="dev9-1")
        self.assertEqual(self.post(**b)[0], 200)
        code, j = self.post(**b)
        self.assertEqual((code, j["duplicate"], j["rows"]), (200, True, 0))
        self.assertEqual(sum(r["client_id"] == "dev9-1" for r in rows(self.data, "cylinders.csv")), 1)
        # 서버를 다시 켜도 파일의 client_id 로 막는다
        self.assertTrue(fs.Store(self.data, AT).measure(dict(b, entered_by="김"), "")["duplicate"])

    # ---- 값 검사 ----
    def test_validation(self):
        base = dict(kind="cylinder", zone_id="9F-A1", test_date="2026-12-31", curing="현장봉함", f1=25, f2=25, f3=25)
        bad = [
            (dict(base, f2="abc"), "숫자가 아닙니다"),
            (dict(base, f3=240), "kgf/cm²"),
            (dict(base, f1=0), "범위 밖"),
            (dict(base, f1=""), "비어 있습니다"),
            (dict(base, zone_id="9F-Z9"), "모델에 없습니다"),
            (dict(base, test_date="2026-11-20"), "타설일"),
            (dict(base, test_date="2027-01-01"), "아직 오지 않은"),
            (dict(base, test_date="12/31"), "형식"),
            (dict(base, curing="표준양생"), "양생"),
            (dict(base, entered_by=" "), "입력자"),
            (dict(base, kind="xray"), "측정 종류"),
            (dict(base, client_id="a b"), "client_id"),
            (dict(kind="sensor_temp", sensor_id="S1", floor=9, timestamp="2026-12-31T08:00", temp_c=120), "-30~90"),
            (dict(kind="sensor_temp", sensor_id="S1", floor=10, timestamp="2026-12-31T08:00", temp_c=5), "9층 센서"),
            (dict(kind="sensor_temp", sensor_id="S1", floor=9, timestamp="2026-11-01T08:00", temp_c=5), "타설일"),
            (dict(kind="sensor_temp", sensor_id="S1", floor=9, timestamp="2027-01-01T08:00", temp_c=5), "아직 오지 않은"),
            (dict(kind="sensor_strength", sensor_id="W9", floor=9, x=99, y=1, timestamp="2026-12-31T08:00", mpa=5), "x 좌표"),
            (dict(kind="sensor_strength", sensor_id="W9", floor=9, timestamp="2026-12-31T08:00", mpa=5), "x 좌표 값이 비어"),
            (dict(kind="sensor_temp", sensor_id="S1", floor=9, readings=[{"timestamp": "2026-12-31T08:00", "temp_c": 5},
                                                                         {"timestamp": "어제", "temp_c": 5}]), "2번째 줄"),
            (dict(kind="rebound", zone_id="9F-A1", test_date="2026-12-31", r_value=5), "반발경도"),
            (dict(kind="sensor_temp", sensor_id="S 1", floor=9, timestamp="2026-12-31T08:00", temp_c=5), "센서 ID"),
        ]
        before = {n: len(rows(self.data, n)) for n in ("cylinders.csv", "sensors.csv")}
        for b, want in bad:
            code, j = self.post(**b)
            self.assertEqual(code, 400, (b, j))
            self.assertIn(want, j["error"], b)
        self.assertEqual(before, {n: len(rows(self.data, n)) for n in before})
        self.assertFalse(os.path.exists(os.path.join(self.data, "rebound.csv")))
        req = urllib.request.Request(f"{self.base}/api/measure?t={self.srv.token}", data=b"", method="POST")   # 빈 POST
        with self.assertRaises(urllib.error.HTTPError) as x:
            urllib.request.urlopen(req, timeout=20)
        self.assertEqual(x.exception.code, 400)

    # ---- /api/recent ----
    def test_recent(self):
        self.post(kind="cylinder", zone_id="9F-A1", test_date="2026-12-31", curing="현장봉함", f1=25, f2=25, f3=25)
        self.post(kind="sensor_temp", sensor_id="S2", floor=9, readings=[{"timestamp": "2026-12-31T10:00", "temp_c": 3},
                                                                          {"timestamp": "2026-12-31T11:00", "temp_c": 4}])
        code, j = self.call("/api/recent?n=20")
        self.assertEqual(code, 200)
        self.assertEqual(len(j["items"]), 2)          # 샘플 원래 줄(client_id 없음)은 빠지고, 붙여넣기 2줄은 1건
        s = next(i for i in j["items"] if i["kind"] == "sensor_temp")
        self.assertEqual((s["point"], s["count"], s["value"], s["entered_by"]), ("S2", 2, "4 °C 외 1줄", "김현장"))

    def test_header_extension_keeps_rows(self):
        p = os.path.join(self.data, "cylinders.csv")
        with open(p, "rb") as f:
            raw = f.read().rstrip(b"\r\n")              # 끝 줄바꿈 없는 파일 (엑셀 저장)
        with open(p, "wb") as f:
            f.write(raw)
        self.post(kind="cylinder", zone_id="9F-B1", test_date="2026-12-31", curing="현장수중", f1=26, f2=26, f3=26)
        r = rows(self.data, "cylinders.csv")
        self.assertEqual([x["zone_id"] for x in r], ["9F-A1", "9F-B1", "9F-C1", "9F-A2", "9F-B2", "9F-C2", "9F-B1"])
        self.assertEqual(r[5]["f3"], rows(SAMPLE, "cylinders.csv")[5]["f3"])


class StrengthSensors(unittest.TestCase):
    def test_sample_answers_unchanged(self):
        nine = {r["zone_id"]: r for r in ev.evaluate(SAMPLE, AT) if r["floor"] == 9}
        self.assertEqual({z for z, r in nine.items() if r["decision"] == "해체 승인 요청 가능"}, {"9F-A1", "9F-B1", "9F-C1", "9F-C2"})
        # 구역 경계 3 m 안 센서만 (evaluate.SENSOR_MARGIN_M), 없으면 층 전체 + 표시 — 9F-A1 은 가까운 센서가 없음
        src = {z: r["est_source"] for z, r in nine.items()}
        self.assertEqual(src["9F-A1"], "온도 센서 6개 IDW (가까운 센서 없음 — 층 전체 센서) 24.4·기상 예측(하한 없음) 24.1 중 최소 · 참고 (판정에 안 씀)")   # 감사 A-10 · 34차 C (3 m 안 센서 없음 = 참고)
        self.assertTrue(all(re.fullmatch(r"센서 [1-5]개 IDW", s) for z, s in src.items() if z != "9F-A1"), src)

    def test_data_strength_sample(self):
        nine = {r["zone_id"]: r for r in ev.evaluate(os.path.join(ROOT, "shoring", "sample", "data_strength"), AT) if r["floor"] == 9}
        self.assertEqual(nine["9F-C1"]["decision"], "존치 (구역 최소 강도 미달)")
        self.assertEqual(nine["9F-C1"]["est_source"], "온도 센서 2개 IDW 24.4·강도 센서 1개 IDW 23.8 중 최소")
        self.assertEqual(nine["9F-A1"]["decision"], "해체 승인 요청 가능")

    def test_strength_only_latest_before_at(self):
        tmp = tempfile.mkdtemp()
        try:
            for n in ("zones.csv", "pours.csv", "cylinders.csv"):
                shutil.copy(os.path.join(SAMPLE, n), tmp)
            with open(os.path.join(tmp, "sensor_strength.csv"), "w", encoding="utf-8", newline="") as f:
                f.write("sensor_id,floor,x,y,timestamp,mpa\n"
                        "W1,9,5,4.5,2026-12-30T09:00,30\n"
                        "W1,9,5,4.5,2026-12-29T09:00,10\n"        # 순서가 섞여도 시각으로 마지막 값
                        "W1,9,5,4.5,2027-01-05T09:00,1\n"         # 판정 시각 뒤 → 안 씀
                        "W2,9,25,13.5,2026-11-20T09:00,1\n"       # 타설 전 → 안 씀
                        "W2,9,25,13.5,2026-12-31T08:00,26\n"
                        "W3,10,5,5,2026-12-31T08:00,1\n")         # 다른 층
            nine = {r["zone_id"]: r for r in ev.evaluate(tmp, AT) if r["floor"] == 9}
            a1 = nine["9F-A1"]
            self.assertEqual(a1["est_source"], "강도 센서 1개 IDW")       # W2(25,13.5) 는 A1 경계 3 m 밖
            want, xy = e.zone_min_from_values([(5, 4.5)], [30.0], (0, 0, 10, 9))
            self.assertEqual((a1["f_est_min"], a1["f_est_xy"]), (round(want, 1), xy))
            # W3 (10F 1 MPa) 는 9F 위치 근거엔 안 쓰지만, 9F 를 풀면 10F 가 다음 타설을 받쳐야 하므로 지지층 검토에 걸린다 (감사 A-06)
            self.assertEqual((a1["decision"], a1["level_gate"]), ("존치", "NG(지지층)"))
            self.assertIn("지지층 최소 1.0 < 14 MPa", a1["reason"])
            self.assertEqual(nine["9F-C1"]["est_source"], "강도 센서 2개 IDW (가까운 센서 없음 — 층 전체 센서) 26.2·기상 예측(하한 없음) 24.1 중 최소 · 참고 (판정에 안 씀)")
        finally:
            _rm(tmp)


class LargeData(unittest.TestCase):
    """감사 A-20 부하: 센서 30개 × 10분 × 60일(약 26만 줄) 합성 자료에서 응답 시간 — 휴대폰 저장 1건 2 s 이내, 첫 /api/model 5 s 이내 [가정].
    (고치기 전 /api/model·저장 13 s — evaluate 를 저장마다 몇 번씩 다시 돌림. 지금은 센서 색인·판정 결과 캐시, 결과 바이트 그대로.) 합성 자료는 끝에 지움."""
    def test_large_data_response(self):
        import time
        import make_load_data as md
        tmp = tempfile.mkdtemp()
        try:
            d = os.path.join(tmp, "data")
            self.assertGreater(md.make(d), 250000)
            srv = fs.serve(d, AT, "127.0.0.1", 0)
            threading.Thread(target=srv.serve_forever, daemon=True).start()
            base = f"http://127.0.0.1:{srv.server_address[1]}"

            def call(path, body=None):
                req = urllib.request.Request(base + path + ("&" if "?" in path else "?") + "t=" + srv.token,
                                             data=None if body is None else json.dumps(body).encode(), headers={"Content-Type": "application/json"})
                t0 = time.perf_counter()
                with urllib.request.urlopen(req, timeout=120) as r:
                    j = json.loads(r.read())
                return time.perf_counter() - t0, j
            try:
                first, m = call("/api/model")
                self.assertTrue(m["ok"] and m["floors"], m.get("error"))
                save, j = call("/api/measure", {"kind": "cylinder", "client_id": "load-1", "entered_by": "부하", "zone_id": "9F-A2", "test_date": "2026-12-30",
                                                "curing": "현장봉함", "f1": 25, "f2": 25, "f3": 25})
                self.assertTrue(j["ok"] and j["zone"]["zone_id"] == "9F-A2", j)
                print(f"\n  큰 자료(26만 줄) 첫 /api/model {first:.2f}s · 저장 1건 {save:.2f}s", file=sys.stderr)
                self.assertLess(first, 5.0)
                self.assertLess(save, 2.0)
            finally:
                srv.shutdown()
                srv.server_close()
        finally:
            _rm(tmp)


class CommandLine(unittest.TestCase):
    """명령행으로 켠 서버 (휴대폰 시연 경로). Store 를 직접 쓰는 위 시험은 __main__ 을 안 거쳐
    2026-11-04 'import approval as ap' 를 argparse 의 ap 가 덮어 전부 500 이던 것을 못 잡았음."""
    def test_cli_model_200(self):
        import socket
        import subprocess
        import time
        tmp = tempfile.mkdtemp()
        try:
            for n in os.listdir(SAMPLE):
                shutil.copy(os.path.join(SAMPLE, n), tmp)
            with socket.socket() as s:
                s.bind(("127.0.0.1", 0))
                port = s.getsockname()[1]
            p = subprocess.Popen([sys.executable, "-u", os.path.join(HERE, "field_server.py"), "--data", tmp,
                                  "--at", "2026-12-31T09:00", "--host", "127.0.0.1", "--port", str(port)],
                                 stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8",
                                 env={**os.environ, "PYTHONIOENCODING": "utf-8"})   # 한국어 Windows 에서 직접 실행해도 (파이프 기본 = cp949, 총괄 11-05)
            try:
                tok = None
                for _ in range(40):
                    line = p.stdout.readline()
                    m = re.search(r"[?&]t=([A-Za-z0-9_-]+)", line)
                    if m:
                        tok = m.group(1)
                        break
                self.assertIsNotNone(tok, "서버 주소 줄이 안 나옴")
                for _ in range(20):
                    try:
                        with urllib.request.urlopen(f"http://127.0.0.1:{port}/api/model?t={tok}", timeout=10) as r:
                            self.assertEqual(r.status, 200)
                            self.assertTrue(json.loads(r.read().decode("utf-8"))["ok"])
                        break
                    except urllib.error.URLError:
                        time.sleep(0.3)
                else:
                    self.fail("서버에 연결 안 됨")
            finally:
                p.terminate()
                p.wait(timeout=10)
                p.stdout.close()
        finally:
            _rm(tmp)


if __name__ == "__main__":
    unittest.main(verbosity=2)

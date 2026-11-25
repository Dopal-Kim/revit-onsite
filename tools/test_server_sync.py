"""Vercel 현장 서버(server/vercel, Node) + tools/server_sync.py 시험. 표준 라이브러리 + node.
실행: python tools/test_server_sync.py
  ① 입력 검사 문구가 field_server.Store.measure/report 와 글자까지 같은지 (같은 입력을 양쪽에)
  ② push → 휴대폰 입력 → pull 이 데이터 폴더 CSV 에 들어가고, 다시 보내도 한 번만
  ③ 토큰·동기화 키가 틀리면 403
  ⑥ v6.2: 타임라인 스냅숏·화면(S1), 승인 기록 없는 해체 신고 즉시 빨강(S2), 감리 직접 확인 → pull → approvals.csv(S3), 알림 구독·암호화(S8)
"""
import base64
import json
import os
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import unittest
import urllib.error
import urllib.request
from datetime import datetime

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import field_server as fs   # noqa: E402
import server_sync as ss    # noqa: E402

SAMPLE = os.path.join(ROOT, "shoring", "sample", "data")
SHORES = os.path.join(ROOT, "revit", "onsite_data_demo", "shores.csv")
SERVER = os.path.join(ROOT, "server", "vercel")
AT = datetime(2026, 12, 31, 9, 0)
TOK = {"FIELD_TOKEN": "t-test-123456", "FIELD_VIEW_TOKEN": "v-test-123456", "FIELD_REPORT_TOKEN": "r-test-123456", "FIELD_SYNC_KEY": "k-test-123456",
       "FIELD_SUPERVISOR_TOKEN": "s-test-123456"}
JPEG = "data:image/jpeg;base64," + base64.b64encode(b"\xff\xd8\xff\xe0" + b"\x00" * 60).decode()


def free_port():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def req(url, body=None):
    r = urllib.request.Request(url, method="POST" if body is not None else "GET",
                               data=None if body is None else json.dumps(body, ensure_ascii=False).encode("utf-8"),
                               headers={"Content-Type": "application/json", "User-Agent": "Mozilla/5.0 (Linux; Android 14) Chrome/130"})
    try:
        with urllib.request.urlopen(r, timeout=30) as x:
            return x.status, json.loads(x.read().decode("utf-8"))
    except urllib.error.HTTPError as x:
        return x.code, json.loads(x.read().decode("utf-8") or "{}")


@unittest.skipUnless(shutil.which("node"), "node 없음")
class Vercel(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.mkdtemp()
        cls.data = os.path.join(cls.tmp, "data")
        shutil.copytree(SAMPLE, cls.data)
        if os.path.exists(SHORES):
            shutil.copy(SHORES, cls.data)
        port = free_port()
        cls.base = f"http://127.0.0.1:{port}"
        # 시험용 VAPID 키 — 이 시험 안에서만 만들고 화면에 내지 않음
        vap = json.loads(subprocess.run(["node", "-e", "process.stdout.write(JSON.stringify(require('web-push').generateVAPIDKeys()))"],
                                        cwd=SERVER, capture_output=True, text=True, check=True).stdout)
        env = {**os.environ, **TOK, "FIELD_LOCAL_DIR": os.path.join(cls.tmp, "blobs"),
               "FIELD_VAPID_PUBLIC": vap["publicKey"], "FIELD_VAPID_PRIVATE": vap["privateKey"], "FIELD_VAPID_SUBJECT": "mailto:test@example.invalid"}
        env.pop("BLOB_READ_WRITE_TOKEN", None)
        cls.vapid_pub = vap["publicKey"]
        cls.node = subprocess.Popen(["node", "dev.js", str(port)], cwd=SERVER, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        for _ in range(50):
            try:
                socket.create_connection(("127.0.0.1", port), 0.2).close()
                break
            except OSError:
                time.sleep(0.1)
        cls.store = fs.Store(cls.data, AT)
        ss.push(cls.store, cls.base, TOK["FIELD_SYNC_KEY"])

    @classmethod
    def tearDownClass(cls):
        cls.node.kill()
        cls.node.wait()
        shutil.rmtree(cls.tmp, ignore_errors=True)

    def t(self, path):
        return f"{self.base}{path}{'&' if '?' in path else '?'}t={TOK['FIELD_TOKEN']}"

    # ① 같은 입력 → 같은 검사 결과
    def test_same_errors_as_field_server(self):
        z = "9F-A2"
        cases = [
            {"kind": "cylinder", "zone_id": z, "test_date": "2026-12-30", "curing": "현장봉함", "f1": "25", "f2": "24,9", "f3": "25.3"},
            {"kind": "cylinder", "zone_id": "없는구역", "test_date": "2026-12-30", "curing": "현장봉함", "f1": 1, "f2": 1, "f3": 1},
            {"kind": "cylinder", "zone_id": z, "test_date": "2027-01-30", "curing": "현장봉함", "f1": 1, "f2": 1, "f3": 1},
            {"kind": "cylinder", "zone_id": z, "test_date": "2026-02-30", "curing": "현장봉함", "f1": 1, "f2": 1, "f3": 1},
            {"kind": "cylinder", "zone_id": z, "test_date": "2026-12-30", "curing": "표준", "f1": 1, "f2": 1, "f3": 1},
            {"kind": "cylinder", "zone_id": z, "test_date": "2026-12-30", "curing": "현장봉함", "f1": "250", "f2": 1, "f3": 1},
            {"kind": "cylinder", "zone_id": z, "test_date": "2026-12-30", "curing": "현장봉함", "f1": "abc", "f2": 1, "f3": 1},
            {"kind": "cylinder", "zone_id": z, "test_date": "2026-12-30", "curing": "현장봉함", "f1": "0", "f2": 1, "f3": 1},
            {"kind": "cylinder", "zone_id": z, "test_date": "2026-12-30", "curing": "현장봉함", "f1": "", "f2": 1, "f3": 1},
            {"kind": "cylinder", "zone_id": z, "test_date": "2026-12-30", "curing": "현장봉함", "f1": 1, "f2": 1, "f3": 1, "sampled_at": "2026-12-31 08:00"},
            {"kind": "cylinder", "zone_id": z, "test_date": "2026-12-30", "curing": "현장봉함", "f1": 1, "f2": 1, "f3": 1, "supplier": "가" * 41},
            {"kind": "rebound", "zone_id": z, "test_date": "2026-12-30", "r_value": "5"},
            {"kind": "rebound", "zone_id": z, "test_date": "2026-12-30", "r_value": "30", "x": "-3"},
            {"kind": "sensor_temp", "sensor_id": "S1", "floor": 9, "timestamp": "2026-12-30 10:00", "temp_c": "12"},
            {"kind": "sensor_temp", "sensor_id": "S1", "floor": 10, "timestamp": "2026-12-30 10:00", "temp_c": "12"},
            {"kind": "sensor_temp", "sensor_id": "NEW1", "floor": 9, "timestamp": "2026-12-30 10:00", "temp_c": "12", "x": "999", "y": "1"},
            {"kind": "sensor_temp", "sensor_id": "NEW2", "floor": 9, "timestamp": "2026-12-30 10:00", "temp_c": "95", "x": "1", "y": "1"},
            {"kind": "sensor_temp", "sensor_id": "NEW3", "floor": 99, "timestamp": "2026-12-30 10:00", "temp_c": "5", "x": "1", "y": "1"},
            {"kind": "sensor_temp", "sensor_id": "!!", "floor": 9},
            {"kind": "sensor_temp", "sensor_id": "S1", "floor": "구", "temp_c": "5"},
            {"kind": "sensor_strength", "sensor_id": "S1", "floor": 9, "timestamp": "2026-12-30 10:00", "mpa": "5"},
            {"kind": "sensor_temp", "sensor_id": "S1", "floor": 9, "readings": [{"timestamp": "2026-12-30 10:00", "temp_c": 5}, {"timestamp": "2027-01-05 10:00", "temp_c": 5}]},
            {"kind": "sensor_temp", "sensor_id": "S1", "floor": 9, "readings": [{"timestamp": "2026-11-01 10:00", "temp_c": 5}]},
            {"kind": "shore_check", "zone_id": z, "timestamp": "2026-12-31 08:30", "present": "예"},
            {"kind": "shore_check", "zone_id": z, "timestamp": "2026-12-31 25:30", "present": "예"},
            {"kind": "shore_check", "zone_id": z, "timestamp": "2026-12-31 08:30", "present": "몰라"},
            {"kind": "shore_check", "zone_id": z, "timestamp": "2026-12-31 08:30", "present": "예", "photo_data": "data:image/jpeg;base64,abc"},
            {"kind": "shore_check", "zone_id": z, "timestamp": "2026-12-31 08:30", "present": "예", "photo_data": base64.b64encode(b"GIF89a00").decode()},
            {"kind": "after_check", "zone_id": z, "date": "2026-12-31", "items": {"AC-1": "예", "AC-3": "아니오"}},
            {"kind": "after_check", "zone_id": z, "date": "2026-12-31", "items": {}},
            {"kind": "after_check", "zone_id": z, "date": "2026-12-31", "items": {"AC-2": "글쎄"}},
            {"kind": "shore_inspect", "zone_id": z, "stage": "설치 검측", "items": [{"item_code": "SHR-B", "result": "OK"}]},
            {"kind": "shore_inspect", "zone_id": z, "stage": "없는 단계", "items": []},
            {"kind": "shore_inspect", "zone_id": z, "stage": "설치 검측", "items": [{"item_code": "SHR-B", "result": "NG"}]},
            {"kind": "shore_inspect", "zone_id": z, "stage": "설치 검측", "items": [{"item_code": "SHR-B", "result": "NG", "shore_ids": ["없는동바리"]}]},
            {"kind": "shore_inspect", "zone_id": z, "stage": "설치 검측", "items": [{"item_code": "SHR-B", "result": "OK"}, {"item_code": "SHR-B", "result": "OK"}]},
            {"kind": "pour", "zone_id": "12F-A1", "pour_date": "2026-12-30", "pour_time": "8:00", "mix": "보통", "fck": "24"},
            {"kind": "pour", "zone_id": z, "pour_date": "2026-12-30", "pour_time": "8:00", "mix": "보통", "fck": "24"},
            {"kind": "pour", "zone_id": "12F-A1", "pour_date": "2026-12-30", "pour_time": "8시", "mix": "보통", "fck": "24"},
            {"kind": "pour", "zone_id": "12F-A1", "pour_date": "2026-12-30", "pour_time": "08:00", "mix": "고강도", "fck": "24"},
            {"kind": "없는종류"},
            {"kind": "cylinder", "client_id": "나쁜 id"},
            {"kind": "cylinder", "entered_by": ""},
            {"kind": "cylinder", "project_id": "다른프로젝트"},
            {"kind": "cylinder", "note": "가" * 201},
        ]
        cases += [   # v6.4 G9 해체 후 침하 계측 settle {지점: mm}
            {"kind": "after_check", "zone_id": z, "date": "2026-12-31", "items": {}, "settle": {"P1": 1.5, "p2": "0,8"}},
            {"kind": "after_check", "zone_id": z, "date": "2026-12-31", "items": {"AC-1": "예"}, "settle": {"가1": 1}},
            {"kind": "after_check", "zone_id": z, "date": "2026-12-31", "items": {}, "settle": {"P1": 150}},
            {"kind": "after_check", "zone_id": z, "date": "2026-12-31", "items": {}, "settle": {"P1": "abc"}},
            {"kind": "after_check", "zone_id": z, "date": "2026-12-31", "items": {}, "settle": [1]},
            {"kind": "after_check", "zone_id": z, "date": "2026-12-31", "items": {}, "settle": {f"P{i}": 1 for i in range(21)}},
            {"kind": "after_check", "zone_id": z, "date": "2026-12-31", "items": {}, "settle": {}},
        ]
        c0 = fs.read_rows(os.path.join(self.data, "cylinders.csv"))[0]      # 같은 공시체 다시 넣기 (61차 ⑤)
        cases.append({"kind": "cylinder", **{k: c0[k] for k in ("zone_id", "test_date", "curing", "f1", "f2", "f3")}})
        cases.append({"kind": "sensor_temp", "sensor_id": "S1", "floor": 9, "readings": [{"timestamp": "2026-12-30 10:00", "temp_c": 5}, {"timestamp": "2026-12-30 10:07", "temp_c": 5}]})
        orig = fs.append_rows
        fs.append_rows = lambda *a, **k: None      # 파이썬 쪽은 검사만 (저장 안 함)
        try:
            for i, c in enumerate(cases):
                b = {"client_id": f"par-{i}", "entered_by": "김현장", **c}
                with self.subTest(i=i, case=c):
                    py_ok, py_err = True, ""
                    try:
                        fs.Store(self.data, AT).measure(b, "")
                    except fs.Bad as x:
                        py_ok, py_err = False, str(x)
                    code, j = req(self.t("/api/measure"), b)
                    self.assertEqual((code == 200, j.get("error", "")), (py_ok, py_err))
                    if code == 200:
                        self.assertTrue(j["pending_sync"])
                        self.assertEqual(j["message"], "저장됨 — 판정은 Revit 동기화 뒤 반영")
        finally:
            fs.append_rows = orig
        rep = [{"zone_id": "없는구역", "present": "예"}, {"zone_id": "9F-A1", "present": "몰라"}, {"zone_id": "9F-A1", "present": "아니오", "name": ""},
               {"zone_id": "9F-A1", "present": "아니오", "name": "박반장", "company": "가" * 41}]
        for i, c in enumerate(rep):
            b = {"client_id": f"rpar-{i}", **c}
            with self.subTest(report=c):
                try:
                    fs.Store(self.data, AT).report(b, "")
                    py = ""
                except fs.Bad as x:
                    py = str(x)
                code, j = req(f"{self.base}/api/report?r={TOK['FIELD_REPORT_TOKEN']}", b)
                self.assertEqual((code, j.get("error")), (400, py))

    # ② push → 입력 → pull → CSV, 중복 없음
    def test_roundtrip(self):
        data = os.path.join(self.tmp, "rt")
        shutil.copytree(self.data, data)
        store = fs.Store(data, AT)
        base = self.base
        ss.push(store, base, TOK["FIELD_SYNC_KEY"])     # Revit 이 이 폴더로 올림 (project_id = 이 폴더)
        cyl = {"kind": "cylinder", "client_id": "rt-1", "entered_by": "김현장", "zone_id": "9F-A2", "test_date": "2026-12-30", "curing": "현장봉함", "f1": "26", "f2": "26.4", "f3": "25.8", "project_id": store.project_id()}
        sc = {"kind": "shore_check", "client_id": "rt-2", "entered_by": "이안전", "zone_id": "9F-B1", "timestamp": "2026-12-31 08:30", "present": "예", "photo_data": JPEG}
        rep = {"client_id": "rt-3", "zone_id": "9F-A1", "present": "아니오", "name": "박반장", "company": "가설"}
        for b in (cyl, sc):
            code, j = req(self.t("/api/measure"), b)
            self.assertEqual(code, 200, j)
            self.assertFalse(j.get("duplicate"))
        code, j = req(f"{base}/api/report?r={TOK['FIELD_REPORT_TOKEN']}", rep)
        self.assertEqual(code, 200, j)
        self.assertEqual(j["message"], "신고 저장됨 — 감리 승인 기록 없음: 현장 확인 요청됨")
        self.assertTrue(req(self.t("/api/measure"), cyl)[1]["duplicate"])          # 동기화 전 재전송 → 한 번만
        recent = req(self.t("/api/recent?n=5"))[1]["items"]
        self.assertEqual({r["client_id"] for r in recent[:3]}, {"rt-1", "rt-2", "rt-3"})
        r = ss.pull(store, base, TOK["FIELD_SYNC_KEY"], AT)
        self.assertGreaterEqual(r["saved"], 3, r)
        cy = [x for x in fs.read_rows(os.path.join(data, "cylinders.csv")) if x.get("client_id") == "rt-1"]   # 다른 시험 입력도 같은 서버에 있을 수 있음
        self.assertEqual(len(cy), 1)
        self.assertEqual((cy[0]["zone_id"], cy[0]["f2"], cy[0]["entered_by"], cy[0]["device"]), ("9F-A2", "26.4", "김현장", "Android Chrome"))
        sh = {x["client_id"]: x for x in fs.read_rows(os.path.join(data, "shore_checks.csv"))}
        self.assertTrue(sh["rt-2"]["photo"].startswith("사진/20261231/"))           # 사진은 Revit 데이터 폴더에 원본 저장
        self.assertEqual((sh["rt-3"]["source"], sh["rt-3"]["company"], sh["rt-3"]["timestamp"]), ("협력업체 신고", "가설", "2026-12-31T09:00"))
        self.assertEqual(ss.pull(store, base, TOK["FIELD_SYNC_KEY"], AT)["saved"], 0)   # ack 됨 → 다시 안 옴
        ss.push(store, base, TOK["FIELD_SYNC_KEY"])
        self.assertTrue(req(self.t("/api/measure"), cyl)[1]["duplicate"])          # 동기화 뒤 재전송도 한 번만
        self.assertIn("rt-1", {x["client_id"] for x in req(self.t("/api/recent?n=50"))[1]["items"]})
        ss.push(self.store, base, TOK["FIELD_SYNC_KEY"])                           # 다른 시험용 스냅샷 되돌림

    # ④ 동바리 사양·해체 이력 (docs/redesign_v6.md 자료 형식 확정) — 스냅샷 층마다
    def test_shore_info(self):
        data = os.path.join(self.tmp, "si")
        shutil.copytree(self.data, data)
        with open(os.path.join(data, "shore_spec.csv"), "w", encoding="utf-8-sig", newline="") as f:
            f.write("floor,zone_id,type,spec,spacing_x_mm,spacing_y_mm,splices,install_date,entered_by,entered_at\n"
                    "9,,파이프 서포트,V2,900,900,1,2026-11-20,시험,2026-11-20T08:00\n9,9F-A1,시스템 동바리,SS-1,1200,1200,0,2026-11-21,시험,2026-11-21T08:00\n")
        with open(os.path.join(data, "shore_removed.csv"), "w", encoding="utf-8-sig", newline="") as f:
            f.write("id,zone_id,x,y,removed_date,by,note\nP1,9F-A1,1,1,2026-12-30,시험,\nP2,9F-A1,2,1,2026-12-30,시험,\nP3,9F-B1,11,1,2026-12-31,시험,\n")
        snap = ss.snapshot(fs.Store(data, AT))
        f9 = next(f for f in snap["model"]["floors"] if f["floor"] == 9)
        self.assertEqual([s["spec"] for s in f9["shore_spec"]], ["V2", "SS-1"])
        self.assertEqual(f9["shore_removed"], [{"date": "2026-12-31", "zone_id": "9F-B1", "count": 1}, {"date": "2026-12-30", "zone_id": "9F-A1", "count": 2}])
        self.assertEqual(snap["floors"]["9"]["shore_removed"], f9["shore_removed"])
        self.assertEqual(snap["floors"]["10"]["shore_spec"], [])          # 파일에 없는 층은 빈 값

    # ⑤ 화면은 한 벌 — Wi-Fi 서버(web/field)와 Vercel(server/vercel/pages)이 같은 파일 (고치면 web/field 를 고치고 pages 로 복사)
    def test_pages_same_as_web_field(self):
        for n in ("index.html", "view.html"):
            with open(os.path.join(ROOT, "web", "field", n), "rb") as a, open(os.path.join(SERVER, "pages", n), "rb") as b:
                self.assertEqual(a.read(), b.read(), f"{n}: web/field 와 server/vercel/pages 가 다름 — web/field 것을 pages 로 복사")

    # ⑥ v6.2 — 한 데이터 폴더로 S1·S2·S3 차례로 (스냅숏을 바꾸므로 끝에 원래 스냅숏으로 되돌림)
    def test_v62(self):
        import approval
        import evaluate as ev
        data = os.path.join(self.tmp, "v62")
        shutil.copytree(self.data, data)
        store, base, key = fs.Store(data, AT), self.base, TOK["FIELD_SYNC_KEY"]
        row = next(r for r in ev.evaluate(data, AT) if r["zone_id"] == "9F-A1")
        self.assertEqual(row["decision"], "해체 승인 요청 가능")
        approval.record(data, "9F-A1", "요청 제출", "시공자", row, now=AT)
        import evidence
        evidence.add(data, "9F-A1", "2026-12-30", 4, "시공자")     # 해체 전 점검(원장 4) — v6.3 1-2 최종 확인의 전제
        try:
            snap = ss.snapshot(fs.Store(data, AT))
            tl = snap["timeline"]
            self.assertEqual(tl["steps"], [0.4, 0.6, 0.8, 1.0])
            self.assertTrue(tl["floors"] and tl["zones"] and tl["lines"])
            self.assertEqual(len(tl["floors"][0]["mpa"]), (datetime.fromisoformat(tl["end"]) - datetime.fromisoformat(tl["start"])).days + 1)
            self.assertEqual(snap["check"]["requested"]["9F-A1"]["actor"], "시공자")
            ss.push(store, base, key)
            V, R, S = (f"{k}={TOK[e]}" for k, e in (("v", "FIELD_VIEW_TOKEN"), ("r", "FIELD_REPORT_TOKEN"), ("s", "FIELD_SUPERVISOR_TOKEN")))

            # S1 — 타임라인 자료 + 판정 전 공시체
            cyl = {"kind": "cylinder", "client_id": "v62-cyl", "entered_by": "김현장", "zone_id": "9F-A2", "test_date": "2026-12-30", "curing": "현장봉함",
                   "f1": "26", "f2": "27", "f3": "28", "project_id": store.project_id()}
            self.assertEqual(req(self.t("/api/measure"), cyl)[0], 200)
            code, j = req(f"{base}/api/timeline?{V}")
            self.assertEqual(code, 200, j)
            self.assertEqual(j["timeline"]["start"], tl["start"])
            self.assertIn({"kind": "cylinder", "zone_id": "9F-A2", "mpa": 27.0}, [{k: p[k] for k in ("kind", "zone_id", "mpa")} for p in j["pending"] if p["kind"] == "cylinder"])
            self.assertEqual(req(f"{base}/api/timeline?v=wrong")[0], 403)
            with urllib.request.urlopen(f"{base}/timeline?{V}") as x:
                self.assertIn("강도 타임라인", x.read().decode("utf-8"))

            # S2 — 승인 기록 없는 '뺐음' 신고 → 즉시 빨강 (층 화면·model·타임라인), 판정 문구 없음. 구독이 있어도 알림 실패가 저장을 막지 않음
            self.assertEqual(req(f"{base}/api/push/subscribe?{V}", {"endpoint": "http://not-https"})[0], 400)
            self.assertEqual(req(f"{base}/api/push/subscribe?{V}", {"endpoint": "https://push.example.invalid/x1", "keys": {"p256dh": "a" * 87, "auth": "b" * 22}})[0], 200)
            self.assertEqual(req(f"{base}/api/push/key?v=wrong")[0], 403)
            self.assertEqual(req(f"{base}/api/push/key?{V}")[1]["key"], self.vapid_pub)
            code, j = req(f"{base}/api/report?{R}", {"client_id": "v62-rep", "zone_id": "9F-B2", "present": "아니오", "name": "박반장", "company": "가설"})
            self.assertEqual(code, 200, j)
            mine = lambda us: [(u["zone_id"], u["name"]) for u in us if u["client_id"].startswith("v62-")]   # 같은 서버의 다른 시험 신고는 빼고
            self.assertEqual(mine(req(f"{base}/api/floor?f=9&{V}")[1]["unapproved_reports"]), [("9F-B2", "박반장")])
            self.assertEqual(req(f"{base}/api/floor?f=10&{V}")[1]["unapproved_reports"], [])
            self.assertEqual(mine(req(self.t("/api/model"))[1]["unapproved_reports"]), [("9F-B2", "박반장")])
            self.assertEqual(mine(req(f"{base}/api/timeline?{V}")[1]["unapproved_reports"]), [("9F-B2", "박반장")])
            req(f"{base}/api/report?{R}", {"client_id": "v62-rep2", "zone_id": "9F-B2", "present": "예", "name": "박반장"})   # '다시 세움'은 빨강 아님
            self.assertEqual(len(mine(req(self.t("/api/model"))[1]["unapproved_reports"])), 1)

            # S3 + v6.3 1-2 — 감리 두 단계(ⓐ 작업계획서 plan → ⓑ 최종 final) + 보완 요청(reject): 서버 검사, 보낸 기록 상태, pull → approvals.csv
            sup = lambda b: req(f"{base}/api/supervise?{S}", {"zone_id": "9F-A1", "name": "이감리", **b})
            self.assertEqual(req(f"{base}/api/supervisor?s=wrong")[0], 403)
            self.assertEqual(req(f"{base}/api/supervisor?{V}")[0], 403)
            code, j = req(f"{base}/api/supervisor?{S}")
            self.assertEqual(code, 200, j)
            z = {x["zone_id"]: x for x in j["zones"]}
            self.assertEqual(list(z), ["9F-A1"])
            self.assertEqual((z["9F-A1"]["can_plan"], z["9F-A1"]["can_final"], z["9F-A1"]["can_reject"]), (True, False, True), z)
            self.assertIn("작업계획서", z["9F-A1"]["why_final"])
            self.assertEqual(len(j["checks"]), 3)
            c, e = sup({"client_id": "v62-sup0", "checks": [True] * 3})                        # action 없음 = final → 계획서 확인 전이라 거부
            self.assertEqual(c, 400)
            self.assertIn("작업계획서", e["error"])
            self.assertEqual(sup({"client_id": "v62-supA", "action": "approve"})[0], 400)       # 없는 action
            self.assertEqual(sup({"client_id": "v62-supP", "action": "plan"})[0], 200)
            c, e = sup({"client_id": "v62-supP2", "action": "plan"})
            self.assertEqual(c, 400)
            self.assertIn("동기화 대기", e["error"])
            c, e = sup({"client_id": "v62-supR0", "action": "reject"})
            self.assertEqual(c, 400)
            self.assertIn("보완 사유", e["error"])
            self.assertEqual(sup({"client_id": "v62-supR", "action": "reject", "reason": "작업발판 설치 사진 추가"})[0], 200)
            self.assertEqual(sup({"client_id": "v62-supF0", "action": "final", "checks": [True, True, False]})[0], 400)
            self.assertEqual(req(f"{base}/api/supervise?{S}", {"client_id": "v62-supx", "zone_id": "9F-B1", "name": "이감리", "action": "final", "checks": [True] * 3})[0], 400)
            c, j = sup({"client_id": "v62-sup1", "action": "final", "checks": [True] * 3})         # 계획서 확인을 보냈으니(대기) 최종 확인 가능
            self.assertEqual(c, 200, j)
            self.assertTrue(j["pending_sync"])
            c, e = sup({"client_id": "v62-sup2", "action": "final", "checks": [True] * 3})
            self.assertEqual(c, 400)
            self.assertIn("동기화 대기", e["error"])
            h = req(f"{base}/api/supervisor?{S}")[1]["zones"][0]["history"]
            self.assertEqual(sorted((x["action"], x["state"]) for x in h), [("final", "대기"), ("plan", "대기"), ("reject", "대기")])
            self.assertEqual(next(x["reason"] for x in h if x["action"] == "reject"), "작업발판 설치 사진 추가")
            with urllib.request.urlopen(f"{base}/supervisor?{S}") as x:
                self.assertIn("감리 확인", x.read().decode("utf-8"))
            # v6.4 2-4: ③ 펼침 항목(4.1) · 공시체 사진은 감리 화면에서 (감리 열쇠로만), Revit 이 가져가도 서버에 남음
            j = req(f"{base}/api/supervisor?{S}")[1]
            self.assertEqual([g["group"] for g in j["check3"]], ["작업준비", "작업자", "작업허가"])
            cph = {"kind": "cylinder", "client_id": "v62-cph", "entered_by": "김현장", "zone_id": "9F-A1", "test_date": "2026-12-30", "curing": "현장수중",
                   "f1": "26.5", "f2": "26.1", "f3": "26.9", "project_id": store.project_id(), "photo_data": JPEG}
            c, e = req(self.t("/api/measure"), cph)
            self.assertEqual(c, 200, e)
            self.assertIn("사진", e.get("photo", ""))
            bad = {**cph, "client_id": "v62-cph-bad", "f1": "27", "photo_data": "data:image/jpeg;base64,abc"}
            self.assertEqual(req(self.t("/api/measure"), bad)[1].get("error"), "사진 자료를 읽지 못했습니다 — 사진을 다시 고르세요")
            ph = req(f"{base}/api/supervisor?{S}")[1]["zones"][0]["photos"]
            self.assertEqual([(x["id"], x["test_date"]) for x in ph], [("v62-cph", "2026-12-30")])
            with urllib.request.urlopen(f"{base}/api/photo?{S}&id=v62-cph") as x:
                self.assertEqual((x.headers["Content-Type"], x.read()[:3]), ("image/jpeg", b"\xff\xd8\xff"))
            self.assertEqual(req(f"{base}/api/photo?{V}&id=v62-cph")[0], 403)
            self.assertEqual(req(f"{base}/api/photo?{S}&id=v62-cyl")[0], 404)      # 사진 없는 공시체

            r = ss.pull(store, base, key, AT)
            self.assertNotIn("v62-sup1", r["errors"])     # plan·reject 처리는 PC 쪽(총괄) 몫 — 여기선 final 이 기록되는지만
            h = {x["action"]: x for x in req(f"{base}/api/supervisor?{S}")[1]["zones"][0]["history"]}
            self.assertEqual(h["final"]["state"], "기록됨")
            self.assertTrue(all(x["state"] in ("기록됨", "거절") for x in h.values()))
            with urllib.request.urlopen(f"{base}/api/photo?{S}&id=v62-cph") as x:      # ack 뒤에도 감리 화면용으로 남음
                self.assertEqual(x.status, 200)
            log = approval.read_log(data)
            ap = [x for x in log if x["action"] == "감리 승인 기록"]
            self.assertEqual([(x["zone_id"], x["approver"], x["actor"]) for x in ap], [("9F-A1", "이감리", "이감리")])
            self.assertIn("v62-sup1", ap[0]["note"])
            self.assertEqual((ap[0]["source"], approval.source_label(ap[0])), ("감리 직접", "감리 직접"))   # W-A (작업 세션): supervise 줄 = 출처 감리 직접 (종이 [승인 기록]은 '종이 옮김')
            self.assertTrue(approval.verify(data)[0])
            self.assertEqual(ss.pull(store, base, key, AT)["saved"], 0)
            ss.push(store, base, key)
            self.assertEqual(mine(req(self.t("/api/model"))[1]["unapproved_reports"]), [])     # Revit 이 신고를 반영 → 서버 빨강은 내려가고 스냅숏(무단 해체 의심)이 이어받음
            z = {x["zone_id"]: x for x in req(f"{base}/api/supervisor?{S}")[1]["zones"]}
            self.assertFalse(z["9F-A1"]["can_confirm"] or z["9F-A1"]["can_plan"] or z["9F-A1"]["can_reject"])
            self.assertIn("이미", z["9F-A1"]["why_not"])
            # 같은 감리 확인을 Revit 이 다시 받아도 한 번만 (note 의 client_id)
            self.assertEqual(ss.supervise(fs.Store(data, AT), {"client_id": "v62-sup1", "zone_id": "9F-A1", "name": "이감리", "checks": [True] * 3}, "2026-12-31T09:00:00"),
                             {"duplicate": True})
            with self.assertRaises(fs.Bad):     # 요청 제출 없는 구역
                ss.supervise(fs.Store(data, AT), {"client_id": "v62-sup9", "zone_id": "9F-C1", "name": "이감리", "checks": [True] * 3}, "2026-12-31T09:00:00")
        finally:
            ss.push(self.store, base, key)

    # ⑧ W1 타설 묶음 공시체 — 묶음 검사 문구가 field_server 와 같은지, 묶음 줄 다시 넣기 거부까지
    def test_pour_lot(self):
        import csv as _csv
        data = os.path.join(self.tmp, "lot")
        shutil.copytree(self.data, data)
        pp = os.path.join(data, "pours.csv")
        with open(pp, encoding="utf-8-sig", newline="") as f:
            rows = list(_csv.DictReader(f))
        for r in rows:
            r["pour_id"] = "P-9F-1" if r["zone_id"].startswith("9F-") else ("P-10F-1" if r["zone_id"] == "10F-A1" else "")
        with open(pp, "w", encoding="utf-8", newline="") as f:
            w = _csv.DictWriter(f, fieldnames=list(rows[0]))
            w.writeheader()
            w.writerows(rows)
        store, base, key = fs.Store(data, AT), self.base, TOK["FIELD_SYNC_KEY"]
        first = {"kind": "cylinder", "client_id": "lot-0", "entered_by": "김현장", "zone_id": "", "pour_id": "P-9F-1", "test_date": "2026-12-30",
                 "curing": "현장봉함", "f1": "25.5", "f2": "25.9", "f3": "26.1"}
        self.assertEqual(store.measure(first, "")["lot_zones"], ["9F-A1", "9F-A2", "9F-B1", "9F-B2", "9F-C1", "9F-C2"])   # 파이썬 쪽에 묶음 줄 하나 저장
        snap = ss.snapshot(fs.Store(data, AT))
        self.assertEqual(next(z for f in snap["model"]["floors"] for z in f["zones"] if z["zone_id"] == "9F-B1")["pour_id"], "P-9F-1")
        self.assertIn("P-9F-1", [c[6] for c in snap["check"]["cylinders"]])
        ss.push(store, base, key)
        try:
            base_b = {"kind": "cylinder", "entered_by": "김현장", "test_date": "2026-12-30", "curing": "현장봉함", "f1": "24", "f2": "25", "f3": "26"}
            cases = [
                {"zone_id": "", "pour_id": "P-9F-1"},
                {"zone_id": "", "pour_id": "P-10F-1"},                        # 묶음 구역 1개여도 서버 검사는 같음
                {"zone_id": "", "pour_id": "P-없음"},
                {"zone_id": "", "pour_id": ""},
                {"zone_id": "", "pour_id": "P" * 41},
                {"zone_id": "9F-A2", "pour_id": "P" * 41},                    # 구역을 고르면 pour_id 는 채취 정보 '타설 번호'
                {"zone_id": "", "pour_id": "P-9F-1", "test_date": "2026-11-20"},
                {"zone_id": "", "pour_id": "P-9F-1", "sampled_at": "2026-11-20 08:00"},
                {**{k: first[k] for k in ("zone_id", "pour_id", "test_date", "curing", "f1", "f2", "f3")}},   # 같은 묶음 공시체 다시
                {"kind": "rebound", "zone_id": "", "pour_id": "P-9F-1", "r_value": "30"},
            ]
            orig = fs.append_rows
            fs.append_rows = lambda *a, **k: None
            try:
                for i, c in enumerate(cases):
                    b = {**base_b, "client_id": f"lotp-{i}", **c}
                    with self.subTest(i=i, case=c):
                        try:
                            py = fs.Store(data, AT).measure(b, "")
                            py_ok, py_err, py_lot = True, "", py.get("lot_zones")
                        except fs.Bad as x:
                            py_ok, py_err, py_lot = False, str(x), None
                        code, j = req(self.t("/api/measure"), b)
                        self.assertEqual((code == 200, j.get("error", ""), j.get("lot_zones")), (py_ok, py_err, py_lot))
            finally:
                fs.append_rows = orig
        finally:
            ss.push(self.store, base, key)

    # ⑨ W7 잠금: CSV 잠금을 못 잡으면 ack 하지 않고 다음 pull 때 다시 받아 저장
    def test_busy_retry(self):
        data = os.path.join(self.tmp, "busy")
        shutil.copytree(self.data, data)
        store, base, key = fs.Store(data, AT), self.base, TOK["FIELD_SYNC_KEY"]
        ss.push(store, base, key)
        try:
            b = {"kind": "rebound", "client_id": "busy-1", "entered_by": "김현장", "zone_id": "9F-A2", "test_date": "2026-12-30", "r_value": "33",
                 "project_id": store.project_id()}
            self.assertEqual(req(self.t("/api/measure"), b)[0], 200)
            lock = os.path.join(data, "rebound.csv.lock")
            open(lock, "w").close()                       # 다른 PC 가 쓰는 중 (막 만든 잠금 — 30 초 안이라 지우지 않음)
            r = ss.pull(store, base, key, AT)
            self.assertIn("busy-1", r["errors"])
            self.assertIn("다음 동기화 때 다시", r["errors"]["busy-1"])
            os.remove(lock)
            r = ss.pull(store, base, key, AT)             # ack 안 했으니 다시 옴 → 저장
            self.assertEqual((r["saved"], r["errors"]), (1, {}))
            self.assertEqual([x["client_id"] for x in fs.read_rows(os.path.join(data, "rebound.csv")) if x.get("client_id") == "busy-1"], ["busy-1"])
        finally:
            ss.push(self.store, base, key)

    # ⑩ vercel.json 다시 쓰기 (dev.js 가 Vercel 처럼 적용) — 화면과 API 가 이름이 같아도(timeline·supervisor) 섞이지 않는지
    def test_rewrites(self):
        b = self.base
        V, S, T = f"v={TOK['FIELD_VIEW_TOKEN']}", f"s={TOK['FIELD_SUPERVISOR_TOKEN']}", f"t={TOK['FIELD_TOKEN']}"
        want = {f"/?{T}": "text/html", f"/index.html?{T}": "text/html", f"/view?{V}&f=9": "text/html", f"/timeline?{V}": "text/html",
                f"/supervisor?{S}": "text/html", "/sw.js": "text/javascript", "/notify.js": "text/javascript", "/manifest.json?start=/": "application/manifest+json",
                f"/api/timeline?{V}": "application/json", f"/api/supervisor?{S}": "application/json", f"/api/model?{T}": "application/json",
                f"/api/floor?f=9&{V}": "application/json", f"/api/recent?{T}&n=3": "application/json"}
        for path, ctype in want.items():
            with self.subTest(path=path.split("?")[0]):
                with urllib.request.urlopen(b + path) as x:
                    self.assertEqual((x.status, x.headers["Content-Type"].split(";")[0]), (200, ctype))
        with open(os.path.join(SERVER, "vercel.json"), encoding="utf-8") as f:
            rw = json.load(f)["rewrites"]
        self.assertTrue(all("__page=" in r["destination"] for r in rw if not r["source"].startswith("/api/")))
        self.assertFalse(any(":" in r["destination"].replace(":p*", "") for r in rw))   # 목적지의 ':' 는 Vercel 변수 — 배포가 거부됨

    # ⑪ Revit 이 받음(ack) 표시한 입력은 스냅숏에 없어도 '판정 전'·빨강에서 빠짐 (데이터 폴더를 되돌린 경우 — 11-09 배포 확인에서 발견)
    def test_acked_not_pending(self):
        b, V = self.base, f"v={TOK['FIELD_VIEW_TOKEN']}"
        cyl = {"kind": "cylinder", "client_id": "ack-cyl", "entered_by": "김현장", "zone_id": "9F-A2", "test_date": "2026-12-30", "curing": "현장수중",
               "f1": "30", "f2": "31", "f3": "32"}
        self.assertEqual(req(self.t("/api/measure"), cyl)[0], 200)
        req(f"{b}/api/report?r={TOK['FIELD_REPORT_TOKEN']}", {"client_id": "ack-rep", "zone_id": "9F-C2", "present": "아니오", "name": "최반장"})
        j = req(f"{b}/api/timeline?{V}")[1]
        self.assertIn("ack-cyl", [p["client_id"] for p in j["pending"]])
        self.assertIn("ack-rep", [u["client_id"] for u in j["unapproved_reports"]])
        ss.call(b, TOK["FIELD_SYNC_KEY"], "/api/sync/ack", {"items": [{"client_id": "ack-cyl"}, {"client_id": "ack-rep", "error": "시험 — 거부"}]})
        j = req(f"{b}/api/timeline?{V}")[1]
        self.assertNotIn("ack-cyl", [p["client_id"] for p in j["pending"]])
        self.assertNotIn("ack-rep", [u["client_id"] for u in j["unapproved_reports"]])

    # ⑦ S8 알림 암호화 — web-push 가 시험 키·브라우저 모양 구독으로 요청을 만들 수 있는지 (실제 발송은 브라우저 푸시 서비스가 필요해 여기선 안 함)
    def test_push_request(self):
        js = ("const wp=require('web-push'),c=require('crypto');const k=wp.generateVAPIDKeys();const e=c.createECDH('prime256v1');e.generateKeys();"
              "const sub={endpoint:'https://fcm.googleapis.com/fcm/send/test',keys:{p256dh:e.getPublicKey('base64url'),auth:c.randomBytes(16).toString('base64url')}};"
              "const d=wp.generateRequestDetails(sub,JSON.stringify({title:'t',body:'b'}),{vapidDetails:{subject:'mailto:x@example.invalid',publicKey:k.publicKey,privateKey:k.privateKey},TTL:60});"
              "process.stdout.write(JSON.stringify({m:d.method,enc:d.headers['Content-Encoding'],auth:/^vapid t=/.test(d.headers.Authorization),n:d.body.length}))")
        out = json.loads(subprocess.run(["node", "-e", js], cwd=SERVER, capture_output=True, text=True, check=True).stdout)
        self.assertEqual((out["m"], out["enc"], out["auth"]), ("POST", "aes128gcm", True))
        self.assertGreater(out["n"], 100)

    # ⑫ v6.3 0-7: 화면(HTML)은 열쇠 없이 열리고(주소창에서 열쇠를 지워도 새로 고침이 됨) 자료 API 는 열쇠 없으면 403
    def test_pages_open_data_closed(self):
        b = self.base
        for path in ("/", "/view?f=9", "/timeline", "/supervisor"):
            with urllib.request.urlopen(b + path) as x:
                self.assertEqual((x.status, x.headers["Content-Type"].split(";")[0]), (200, "text/html"), path)
        for path in ("/api/model", "/api/floor?f=9", "/api/timeline", "/api/supervisor", "/api/recent"):
            self.assertEqual(req(b + path)[0], 403, path)

    # ③ 토큰
    def test_tokens(self):
        b = self.base
        self.assertEqual(req(f"{b}/api/model?t=wrong")[0], 403)
        self.assertEqual(req(self.t("/api/model"))[0], 200)
        self.assertEqual(req(f"{b}/api/floor?f=9&v=wrong")[0], 403)
        code, j = req(f"{b}/api/floor?f=9&v={TOK['FIELD_VIEW_TOKEN']}")
        self.assertEqual((code, j["can_report"]), (200, False))
        self.assertTrue(req(f"{b}/api/floor?f=9&r={TOK['FIELD_REPORT_TOKEN']}")[1]["can_report"])
        self.assertEqual(req(f"{b}/api/floor?f=77&v={TOK['FIELD_VIEW_TOKEN']}")[0], 404)
        self.assertEqual(req(f"{b}/api/report?r={TOK['FIELD_VIEW_TOKEN']}", {})[0], 403)
        self.assertEqual(req(f"{b}/api/measure?t={TOK['FIELD_VIEW_TOKEN']}", {})[0], 403)
        with self.assertRaises(SystemExit):
            ss.call(b, "wrong", "/api/sync/pull")
        with urllib.request.urlopen(f"{b}/view?v={TOK['FIELD_VIEW_TOKEN']}&f=9") as x:
            self.assertIn("text/html", x.headers["Content-Type"])
        with urllib.request.urlopen(self.t("/")) as x:
            self.assertNotIn("가짜 서버", x.read().decode("utf-8"))


if __name__ == "__main__":
    unittest.main(verbosity=1)

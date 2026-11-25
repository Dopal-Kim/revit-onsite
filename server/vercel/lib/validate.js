// tools/field_server.py 의 measure()/validate()/report() 입력 검사를 JS 로 옮긴 것 — 오류 문구 글자 그대로 (판정 계산은 없음).
// 검사 자료(구역·타설일·공시체·센서 시각·동바리 …)는 Revit 이 올린 스냅샷 check 에서 (tools/server_sync.py check_data).
// 바꾸면 python tools/test_server_sync.py 의 같은 입력 비교 시험을 돌린다.
export class Bad extends Error {}

const TS = /^\s*([0-9]{4})[-/.]([0-9]{1,2})[-/.]([0-9]{1,2})[T ]([0-9]{1,2}):([0-9]{2})(?::[0-9]{2}(?:\.[0-9]+)?)?\s*$/;
const DAY = /^\s*([0-9]{4})[-/.]([0-9]{1,2})[-/.]([0-9]{1,2})\s*$/;
const ID = /^[0-9A-Za-z가-힣_\-]{1,20}$/;
export const CLIENT_ID = /^[0-9A-Za-z_\-]{1,80}$/;
const B64 = /^[A-Za-z0-9+/]*={0,2}$/;
const MAX_PHOTO = 8 * 1024 * 1024;   // shoring/photos.py MAX_BYTES
const LIMITS = {mpa: [0, 100], temp_c: [-30, 90], r_value: [10, 70]};
const MAX_READINGS = 5000;
const SENSOR_VALUE = {sensor_temp: "temp_c", sensor_strength: "mpa"};
const NOT_IN_DECISION = ["rebound", "shore_check", "after_check"];
export const REPORT_FILE = "shore_checks.csv";

// ---- 파이썬 흉내 (문구가 같게) ----
const isObj = v => v !== null && typeof v === "object" && !Array.isArray(v);
export const pyStr = v => (v === undefined || v === null || v === false || v === 0 || v === "") ? "" : v === true ? "True" : String(v);   // str(v or "")
const rawStr = v => v === undefined || v === null ? "None" : v === true ? "True" : v === false ? "False" : String(v);             // str(v)
const flt = x => Number.isInteger(x) && Math.abs(x) < 1e16 ? x + ".0" : String(x);   // 파이썬 float 의 str (예 10.0)
const chars = s => [...s].length;

export function g(x) {   // f"{x:g}"
  if (Number.isNaN(x)) return "nan";
  if (!Number.isFinite(x)) return x > 0 ? "inf" : "-inf";
  if (x === 0) return Object.is(x, -0) ? "-0" : "0";
  const [m, e] = x.toExponential(5).split("e"), ex = Number(e);
  if (ex < -4 || ex >= 6) return m.replace(/\.?0+$/, "") + "e" + (ex < 0 ? "-" : "+") + String(Math.abs(ex)).padStart(2, "0");
  const t = x.toFixed(5 - ex);
  return t.includes(".") ? t.replace(/\.?0+$/, "") : t;
}

function pyFloat(s) {   // float(s) — 못 읽으면 null
  const t = s.trim();
  if (/^[+-]?(inf|infinity)$/i.test(t)) return t[0] === "-" ? -Infinity : Infinity;
  if (/^[+-]?nan$/i.test(t)) return NaN;
  if (!/^[+-]?(\d(_?\d)*(\.(\d(_?\d)*)?)?|\.\d(_?\d)*)([eE][+-]?\d(_?\d)*)?$/.test(t)) return null;
  return Number(t.replaceAll("_", ""));
}

// lo/hi 가 파이썬 float 이면 fl = true (문구에 10.0 처럼)
function num(b, key, label, unit, lo, hi, loOpen = false, fl = false) {
  const v = b[key];
  if (v === undefined || v === null || rawStr(v).trim() === "") throw new Bad(`${label} 값이 비어 있습니다`);
  const f = typeof v === "number" ? v : pyFloat(rawStr(v).trim().split(",").join("."));
  if (f === null) throw new Bad(`${label} '${rawStr(v)}' 은(는) 숫자가 아닙니다 — 숫자만 넣으세요 (예: 24.5)`);
  if (f !== f || f < lo || f > hi || (loOpen && f <= lo)) {
    const hint = unit === "MPa" && f > hi ? " (kgf/cm² 값이면 10.2 로 나누어 MPa 로)" : "";
    const s = x => fl ? flt(x) : String(x);
    throw new Bad(`${label} ${g(f)}${unit ? " " + unit : ""} 은(는) 범위 밖입니다 — ${s(lo)}${loOpen ? "초과" : ""}~${s(hi)} ${unit} 로 넣으세요${hint}`);
  }
  return f;
}

const p2 = n => String(n).padStart(2, "0");
function realDate(y, m, d) {
  const t = new Date(Date.UTC(y, m - 1, d));
  return y >= 1 && t.getUTCFullYear() === y && t.getUTCMonth() === m - 1 && t.getUTCDate() === d;
}
const iso = (y, m, d) => `${String(y).padStart(4, "0")}-${p2(m)}-${p2(d)}`;

function day(v, label) {   // → "YYYY-MM-DD"
  const m = DAY.exec(pyStr(v));
  if (m) { const [y, mo, d] = m.slice(1).map(Number); if (realDate(y, mo, d)) return iso(y, mo, d); }
  throw new Bad(`${label} '${pyStr(v)}' 형식이 틀렸습니다 — 2026-12-30 처럼 넣으세요`);
}

function stamp(v, label = "시각") {   // → "YYYY-MM-DDTHH:MM"
  const m = TS.exec(pyStr(v));
  if (m) {
    const [y, mo, d, h, mi] = m.slice(1).map(Number);
    if (realDate(y, mo, d) && h <= 23 && mi <= 59) return `${iso(y, mo, d)}T${p2(h)}:${p2(mi)}`;
  }
  throw new Bad(`${label} '${pyStr(v)}' 형식이 틀렸습니다 — 2026-12-30 09:00 처럼 날짜와 시각을 넣으세요`);
}
const sp = t => t.replace("T", " ");

export function text(v, label, maxlen, required = false) {
  const s = pyStr(v).split(/\s+/).filter(Boolean).join(" ");
  if (required && !s) throw new Bad(`${label} 칸이 비어 있습니다 — 적어 주세요 (기록에 남습니다)`);
  if (chars(s) > maxlen) throw new Bad(`${label}: ${maxlen}자 이내로 줄여 주세요 (지금 ${chars(s)}자)`);
  return s;
}

export function photoCheck(b) {   // photos.decode + save 의 검사 (저장은 Revit 이 동기화 때)
  if (!b.photo_data) return false;
  let s = pyStr(b.photo_data);
  if (s.startsWith("data:")) s = s.includes(",") ? s.slice(s.indexOf(",") + 1) : "";
  if (!s || s.length % 4 || !B64.test(s)) throw new Bad("사진 자료를 읽지 못했습니다 — 사진을 다시 고르세요");
  const data = Buffer.from(s, "base64");
  if (data.length > MAX_PHOTO) throw new Bad(`사진이 너무 큽니다 (${data.length.toLocaleString("en-US")}바이트) — ${MAX_PHOTO.toLocaleString("en-US")}바이트(8 MB) 이하로 찍어 주세요`);
  const jpg = data[0] === 0xff && data[1] === 0xd8 && data[2] === 0xff;
  const png = data.subarray(0, 8).equals(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]));
  if (!jpg && !png) throw new Bad("사진은 JPEG·PNG 만 받습니다 — 휴대폰 카메라로 찍은 사진을 고르세요");
  return true;
}

function addDays(d, n) { const [y, m, dd] = d.split("-").map(Number); return new Date(Date.UTC(y, m - 1, dd + n)).toISOString().slice(0, 10); }

// ---- field_server.Store.validate (kind, b) → {rows, info} ; now = "YYYY-MM-DDTHH:MM" ----
export function validate(kind, b, ck, now) {
  const today = now.slice(0, 10), tomorrowD = addDays(today, 1), tomorrow = tomorrowD + "T00:00";
  const zones = ck.zones, pours = ck.pours, info = {};
  const zoneOr = (msg) => { const zid = pyStr(b.zone_id).trim(); if (!(zid in zones)) throw new Bad(msg(zid)); return zid; };
  if (kind === "shore_check") {
    const zid = zoneOr(z => `구역 '${z}' 이(가) 모델에 없습니다 — 구역 타일에서 다시 고르세요`);
    const t = stamp(b.timestamp, "확인 시각");
    if (t >= tomorrow) throw new Bad(`확인 시각 ${sp(t)} 은(는) 아직 오지 않은 시각입니다`);
    const present = pyStr(b.present).trim();
    if (present !== "예" && present !== "아니오") throw new Bad("동바리가 서 있는지 '예' 또는 '아니오'를 고르세요");
    text(b.entered_by, "입력자 이름", 20, true); text(b.photo, "사진 파일 이름", 80);
    if (photoCheck(b)) info.photo = "사진 1장 (동기화 때 저장)";
    return {rows: [{zone_id: zid, timestamp: t, present}], info};
  }
  if (kind === "shore_inspect") {
    const zid = zoneOr(z => `구역 '${z}' 이(가) 모델에 없습니다 — 평면에서 구역을 다시 고르세요`);
    const stage = pyStr(b.stage).trim();
    if (!(stage in ck.stages)) throw new Bad(`점검 단계 '${stage}' 은(는) 없습니다 — ${Object.keys(ck.stages).join(", ")} 중 하나`);
    const t = b.timestamp ? stamp(b.timestamp, "점검 시각") : now;
    if (t >= tomorrow) throw new Bad(`점검 시각 ${sp(t)} 은(는) 아직 오지 않은 시각입니다`);
    const items = b.items;
    if (!Array.isArray(items) || !items.length) throw new Bad("점검 항목을 하나 이상 보내세요 (항목마다 OK 또는 NG)");
    const label = Object.fromEntries(ck.stages[stage]);
    const shores = new Set(ck.shores[zid] || []);
    text(b.entered_by, "입력자 이름", 20, true);
    const rows = [], seen = new Set();
    for (const it of items) {
      if (!isObj(it)) throw new Bad("점검 항목 형식이 틀렸습니다 (item_code·result·shore_ids)");
      const c = pyStr(it.item_code).trim();
      if (!(c in label)) throw new Bad(`${stage} 항목 '${c}' 은(는) 없습니다 — 화면을 새로 고치세요`);
      if (seen.has(c)) throw new Bad(`${c} ${label[c]} 이(가) 두 번 있습니다`);
      seen.add(c);
      const res = pyStr(it.result).trim().toUpperCase();
      if (res !== "OK" && res !== "NG") throw new Bad(`${c} ${label[c]}: OK 또는 NG 를 고르세요`);
      let ids = it.shore_ids === undefined || it.shore_ids === null ? [] : it.shore_ids;
      if (!Array.isArray(ids) || ids.length > 500) throw new Bad(`${c} ${label[c]}: 동바리 목록 형식이 틀렸습니다 (500개 이내)`);
      ids = [...new Set(ids.map(x => rawStr(x).trim()).filter(Boolean))];
      const bad = ids.filter(x => !shores.has(x));
      if (bad.length) throw new Bad(`동바리 ${bad.slice(0, 3).join(", ")} 은(는) 구역 ${zid} 에 없습니다 — 평면에서 다시 고르세요`);
      if (res === "NG" && !ids.length && shores.size) throw new Bad(`${c} ${label[c]} NG — 문제 동바리를 평면에서 하나 이상 고르세요`);
      rows.push({zone_id: zid, item_code: c, result: res});
    }
    return {rows, info};
  }
  if (kind === "pour") {
    const zid = zoneOr(z => `구역 '${z}' 이(가) 모델에 없습니다 — 구역을 다시 고르세요`);
    if (zid in pours) throw new Bad(`${zid} 는 이미 타설 기록이 있습니다 (${pours[zid] || ""}) — 고치려면 구역 정보 창 [타설 기록 취소] 뒤 다시 등록`);
    const d = day(b.pour_date, "타설일");
    if (d >= tomorrowD) throw new Bad(`타설일 ${d} 은(는) 아직 오지 않은 날입니다 — 오늘(${today}) 이전으로`);
    if (!/^\s*([01]?[0-9]|2[0-3]):([0-5][0-9])\s*$/.test(pyStr(b.pour_time))) throw new Bad(`타설 시각 '${pyStr(b.pour_time)}' 형식이 틀렸습니다 — 08:00 처럼 넣으세요`);
    const mix = pyStr(b.mix).trim();
    if (!ck.mixes.includes(mix)) throw new Bad(`배합을 고르세요 (${ck.mixes.join(" · ")})`);
    num(b, "fck", "설계기준강도 fck", "MPa", 18, 60);
    return {rows: [{zone_id: zid, pour_date: d}], info};
  }
  if (kind === "after_check") {
    const zid = zoneOr(z => `구역 '${z}' 이(가) 모델에 없습니다 — 구역 타일에서 다시 고르세요`);
    const d = day(b.date, "점검일");
    if (d >= tomorrowD) throw new Bad(`점검일 ${d} 은(는) 아직 오지 않은 날입니다 — 오늘(${today}) 이전으로`);
    const items = !b.items || (Array.isArray(b.items) && !b.items.length) ? {} : b.items;   // 파이썬 `or {}` ([] 도 빈 값)
    if (!isObj(items)) throw new Bad("점검 항목 형식이 틀렸습니다 (항목 → 예/아니오)");
    text(b.entered_by, "입력자 이름", 20, true); text(b.photo, "사진 파일 이름", 80);
    const rows = [];
    for (const [code, name] of ck.after_items) {
      const v = pyStr(items[code]).trim();
      if (!v) continue;
      if (v !== "예" && v !== "아니오") throw new Bad(`${code} ${name}: '예' 또는 '아니오'를 고르세요`);
      rows.push({zone_id: zid, item_code: code, result: v === "예" ? "OK" : "NG"});
    }
    const settle = !b.settle || (Array.isArray(b.settle) && !b.settle.length) ? {} : b.settle;   // 침하 계측 {지점: mm} (v6.4 G9, 파이썬 `or {}`)
    if (!isObj(settle) || Object.keys(settle).length > 20) throw new Bad("침하 계측 형식이 틀렸습니다 (지점 → mm, 20지점까지)");
    for (const pt of Object.keys(settle)) {
      const p = pt.trim().toUpperCase();
      if (!/^[A-Z0-9]{1,10}$/.test(p)) throw new Bad(`침하 측정 지점 '${pt}' — 영문·숫자 10자까지 (예: P1)`);
      num(settle, pt, `침하량 ${p}`, "mm", -100, 100);
      rows.push({zone_id: zid, item_code: "SET-" + p});
    }
    if (!rows.length) throw new Bad("점검 항목을 하나 이상 고르세요 (예/아니오)");
    if (photoCheck(b)) info.photo = "사진 1장 (동기화 때 저장)";
    return {rows, info};
  }
  if (kind === "cylinder" || kind === "rebound") {
    const zid = pyStr(b.zone_id).trim();
    const lot = kind === "cylinder" && !zid ? text(b.pour_id, "타설 묶음", 40) : "";
    let lz = null;
    if (lot) {   // W1: 구역 대신 타설 묶음 — 묶음의 모든 구역 공통 시험 (field_server.validate 와 같음)
      lz = Object.keys(pours).filter(z => (ck.pourIds[z] || "") === lot).sort();
      if (!lz.length) throw new Bad(`타설 묶음 '${lot}' 이(가) 타설 기록(pours.csv)에 없습니다 — [타설 등록]에서 묶음 번호를 넣거나 구역을 고르세요`);
      info.lot_zones = lz;
    } else if (!(zid in zones)) throw new Bad(`구역 '${zid}' 이(가) 모델에 없습니다 — 구역 타일에서 다시 고르세요`);
    else if (!(zid in pours)) throw new Bad(`${zid} 는 타설 기록(pours.csv)이 없습니다 — 타설일을 먼저 등록하세요`);
    const d = day(b.test_date, "시험일"), pd = lot ? lz.map(z => pours[z]).reduce((a, c) => c > a ? c : a) : pours[zid];
    if (d < pd) throw new Bad(`시험일 ${d} 이 타설일 ${pd} 보다 앞입니다 — 시험일을 확인하세요`);
    if (d >= tomorrowD) throw new Bad(`시험일 ${d} 은(는) 아직 오지 않은 날입니다 — 오늘(${today}) 이전으로`);
    if (kind === "cylinder") {
      const curing = pyStr(b.curing).trim();
      if (!ck.curings.includes(curing)) throw new Bad(`양생 방법을 고르세요 (${ck.curings.join(" · ")}) — 해체 판단 근거는 현장양생 공시체`);
      const [lo, hi] = LIMITS.mpa;
      const fs = ["f1", "f2", "f3"].map((k, i) => num(b, k, `공시체 ${i + 1} 강도`, "MPa", lo, hi, true));
      for (const [rz, rd, rc, f1, f2, f3, rp = ""] of ck.cylinders) {
        const same = [f1, f2, f3].map(x => pyFloat(x || "nan")).every((x, i) => x === fs[i]);
        if (same && rz.trim() === zid && (zid || rp.trim() === lot) && rd.trim().slice(0, 10) === d && rc.trim() === curing)
          throw new Bad(`같은 공시체 기록이 이미 있습니다 (${zid || "타설 " + lot} ${d} ${curing} ${fs.map(g).join(" · ")} MPa) — 다시 넣지 않아도 됩니다`);
      }
      if (!lot) text(b.pour_id, "타설 번호", 40);
      text(b.supplier, "레미콘 회사", 40); text(b.truck_no, "차량 번호", 20);
      text(b.storage_zone, "보관 위치", 40); text(b.photo, "사진 파일 이름", 80);
      if (pyStr(b.sampled_at).trim()) {
        const st = stamp(b.sampled_at, "채취 시각");
        if (st.slice(0, 10) < pd) throw new Bad(`채취 시각 ${sp(st)} 이 타설일 ${pd} 보다 앞입니다 — 공시체는 타설 때 채취합니다`);
        if (st.slice(0, 10) > d) throw new Bad(`채취 시각 ${sp(st)} 이 시험일 ${d} 보다 뒤입니다 — 채취 시각을 확인하세요`);
      }
      if (photoCheck(b)) info.photo = "사진 1장 (동기화 때 저장)";   // v6.4 2-4 공시체 사진 (PC: field_server 가 photos.save — API.md §10)
      return {rows: [{zone_id: zid, test_date: d, curing}], info};
    }
    const z = zones[zid];
    const blank = v => v === undefined || v === null || v === "";
    if (!blank(b.x)) num(b, "x", "x 좌표", "m", z.x0, z.x1, false, true);
    if (!blank(b.y)) num(b, "y", "y 좌표", "m", z.y0, z.y1, false, true);
    num(b, "r_value", "반발경도 R", "", ...LIMITS.r_value);
    return {rows: [{zone_id: zid, test_date: d}], info};
  }
  // 센서 (온도·강도)
  const vcol = SENSOR_VALUE[kind];
  const sid = pyStr(b.sensor_id).trim();
  if (!ID.test(sid)) throw new Bad("센서 ID 를 넣으세요 (영문·숫자·한글·-·_ 20자 이내, 예: S7)");
  const fs_ = rawStr(b.floor).trim();
  if (!/^[+-]?\d(_?\d)*$/.test(fs_)) throw new Bad("층을 고르세요");
  const fl = Number(fs_.replaceAll("_", ""));
  const fz = Object.entries(zones).filter(([, z]) => z.floor === fl);
  if (!fz.length) throw new Bad(`${fl}층 구역이 모델에 없습니다 — 층을 다시 고르세요`);
  const pdays = fz.filter(([id]) => id in pours).map(([id]) => pours[id]);
  if (!pdays.length) throw new Bad(`${fl}층은 타설 기록(pours.csv)이 없습니다 — 타설 후에 기록을 넣으세요`);
  const known = ck.sensors;   // [{kind, sensor_id, floor, x, y}] (model.sensors)
  const other = Object.keys(SENSOR_VALUE).find(k => k !== kind && known.some(s => s.kind === k && s.sensor_id === sid));
  if (other) throw new Bad(`${sid} 는 이미 ${ck.kindLabel[other]} ID 입니다 — 다른 ID 를 쓰거나 측정 종류를 바꾸세요`);
  const s = known.find(s => s.kind === kind && s.sensor_id === sid);
  const blank = v => v === undefined || v === null || v === "";
  if (s) {
    if (s.floor !== fl) throw new Bad(`${sid} 는 ${s.floor}층 센서입니다 — 층을 확인하거나 새 ID 를 쓰세요`);
    for (const [k, v] of [["x", s.x], ["y", s.y]])
      if (!blank(b[k]) && Math.abs(num(b, k, `${k} 좌표`, "m", -1e6, 1e6, false, true) - v) > 0.05)
        throw new Bad(`${sid} 의 위치는 (${g(s.x)}, ${g(s.y)}) m 로 등록돼 있습니다 — 센서를 옮겼으면 새 ID 로 넣으세요`);
  } else {
    const zs = fz.map(([, z]) => z);
    num(b, "x", "x 좌표", "m", Math.min(...zs.map(z => z.x0)), Math.max(...zs.map(z => z.x1)), false, true);
    num(b, "y", "y 좌표", "m", Math.min(...zs.map(z => z.y0)), Math.max(...zs.map(z => z.y1)), false, true);
    info.new_sensor = true;
  }
  const [lo, hi] = LIMITS[vcol];
  let readings = b.readings;
  if (readings === undefined || readings === null) readings = [{timestamp: b.timestamp, [vcol]: b[vcol]}];
  if (!Array.isArray(readings) || !readings.length) throw new Bad("기록이 없습니다 — 시각과 값을 넣으세요");
  if (readings.length > MAX_READINGS) throw new Bad(`한 번에 ${MAX_READINGS}줄까지 보낼 수 있습니다 — 나누어 붙여 넣으세요`);
  const label = kind === "sensor_temp" ? "온도" : "추정 강도", unit = kind === "sensor_temp" ? "°C" : "MPa";
  const have = new Set((ck.sensor_ts[kind] || {})[sid] || []);
  const first = pdays.reduce((a, c) => c < a ? c : a);
  const rows = [];
  let skipped = 0;
  readings.forEach((rd, i) => {
    const at = readings.length > 1 ? `${i + 1}번째 줄 ` : "";
    if (!isObj(rd)) throw new Bad(`${at}형식이 틀렸습니다`);
    const t = stamp(rd.timestamp, `${at}시각`);
    if (t.slice(0, 10) < first) throw new Bad(`${at}시각 ${sp(t)} 이 ${fl}층 타설일 ${first} 보다 앞입니다 — 타설 전 기록은 빼고 넣으세요`);
    if (t >= tomorrow) throw new Bad(`${at}시각 ${sp(t)} 은(는) 아직 오지 않은 시각입니다 — 휴대폰 시계를 확인하세요`);
    num(rd, vcol, `${at}${label}`, unit, lo, hi);
    if (have.has(t) && (b.readings === undefined || b.readings === null)) throw new Bad(`${sid} 는 ${sp(t)} 기록이 이미 있습니다 — 측정 시각을 확인하세요`);
    if (have.has(t)) { skipped++; return; }
    have.add(t);
    rows.push({sensor_id: sid, timestamp: t});
  });
  info.skipped = skipped;
  return {rows, info};
}

// ---- field_server.Store.measure 의 앞부분 (저장 전 검사) → {kind, cid, dupFromSnapshot} ----
export function measureHead(b, snap) {
  if (!isObj(b)) throw new Bad("요청 형식이 틀렸습니다 (JSON 객체가 아님)");
  const pid = snap.model.project_id;
  if (!(b.project_id === undefined || b.project_id === null || b.project_id === "" || b.project_id === pid))
    throw new Bad("다른 프로젝트(다른 데이터 폴더)에서 입력한 기록이라 받지 않습니다 — 그 프로젝트의 현장 입력을 켜고 보내거나, 이 기기에서 지우세요");
  const kinds = snap.model.kinds.map(k => k.kind);
  if (!kinds.includes(b.kind)) throw new Bad(`측정 종류 '${rawStr(b.kind)}' 은(는) 없습니다 — [${kinds.map(k => `'${k}'`).join(", ")}] 중 하나`);
  const cid = pyStr(b.client_id);
  if (!CLIENT_ID.test(cid)) throw new Bad("client_id 가 없거나 형식이 틀렸습니다 (영문·숫자·-·_ 80자 이내)");
  text(b.entered_by, "입력자 이름", 20, true); text(b.note, "메모", 200);
  return {kind: b.kind, cid};
}

export function measureAnswer(kind, snap, info) {   // Store.answer 대신 — 판정은 Revit 동기화 뒤
  const k = snap.model.kinds.find(x => x.kind === kind);
  return {ok: true, saved: k.file, used_in_decision: !NOT_IN_DECISION.includes(kind), ...info, now: snap.model.now,
          zone: null, floor_zones: [], warning: "", pending_sync: true, message: "저장됨 — 판정은 Revit 동기화 뒤 반영"};
}

// ---- field_server.Store.report 의 검사 → {zid, present, cid} ----
export function reportHead(b, snap) {
  if (!isObj(b)) throw new Bad("요청 형식이 틀렸습니다 (JSON 객체가 아님)");
  const zid = pyStr(b.zone_id).trim();
  if (!(zid in snap.check.zones)) throw new Bad(`구역 '${zid}' 이(가) 모델에 없습니다 — 층 화면을 새로 고치세요`);
  const present = pyStr(b.present).trim();
  if (present !== "예" && present !== "아니오") throw new Bad("'동바리를 뺐음' 또는 '다시 세움'을 누르세요");
  const cid = pyStr(b.client_id);
  if (!CLIENT_ID.test(cid)) throw new Bad("client_id 가 없거나 형식이 틀렸습니다 (영문·숫자·-·_ 80자 이내)");
  text(b.name, "이름", 20, true); text(b.company, "소속", 40); text(b.device_at, "기기 시각", 30);
  return {zid, present, cid};
}

export function reportMessage(present, approvedAt, ts) {
  const approved = !!approvedAt && approvedAt <= ts + ":00";
  return present === "예" ? "신고 저장됨 — 동바리 다시 세움" :
    approved ? "신고 저장됨 — 감리 승인 기록 있음: 해체 시각 기록됨" : "신고 저장됨 — 감리 승인 기록 없음: 현장 확인 요청됨";
}

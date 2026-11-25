// Revit On-Site 현장 서버 (Vercel Functions, Node). tools/field_server.py 와 같은 주소·응답 + Revit 동기화 (docs/redesign_v6.md §3).
// 판정은 하지 않는다: Revit 이 올린 스냅샷을 보여 주고, 휴대폰 입력은 검사만 해서 쌓아 두면 Revit 이 가져간다 (tools/server_sync.py).
// 환경 변수: FIELD_TOKEN(입력 t) · FIELD_VIEW_TOKEN(보기 v) · FIELD_REPORT_TOKEN(신고 r) · FIELD_SUPERVISOR_TOKEN(감리 s) · FIELD_SYNC_KEY(Revit 쓰기 키)
//            · BLOB_READ_WRITE_TOKEN · FIELD_VAPID_PUBLIC/PRIVATE/SUBJECT(웹 푸시) — 값은 화면·로그에 쓰지 않음
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import {fileURLToPath} from "node:url";
import {getStore} from "../lib/store.js";
import {Bad, CLIENT_ID, REPORT_FILE, measureAnswer, measureHead, reportHead, reportMessage, validate, photoCheck, text} from "../lib/validate.js";
import {cleanSub, pushKey, sendAll, subKey} from "../lib/push.js";

const PAGES = path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "pages");
const PULL_BYTES = 3_000_000;   // 한 번 pull 응답 상한 (Vercel 응답 4.5 MB 아래) — 넘으면 more: true
const NO_SNAPSHOT = "아직 Revit 에서 서버로 올린 판정이 없습니다 — PC 의 Revit 에서 [서버 동기화]를 누르세요";

const json = (code, body) => new Response(JSON.stringify(body), {status: code, headers: {"Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff"}});
const page = name => new Response(fs.readFileSync(path.join(PAGES, name)), {status: 200, headers: {"Content-Type": "text/html; charset=utf-8", "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff", "Referrer-Policy": "no-referrer"}});
const same = (a, b) => !!a && !!b && crypto.timingSafeEqual(crypto.createHash("sha256").update(a).digest(), crypto.createHash("sha256").update(b).digest());
const env = k => (process.env[k] || "").trim();   // 대시보드·CLI 로 넣을 때 끝 줄바꿈이 붙는 일이 있음
const kstNow = () => new Date(Date.now() + 9 * 3600e3).toISOString().slice(0, 19);   // 한국 시각 (서머타임 없음)

function route(url) {   // vercel.json 다시 쓰기 → ?__page=<화면·정적> | ?__p=<api 경로> — 화면과 API 이름이 같아도(timeline) 섞이지 않게 키를 나눔
  const pg = url.searchParams.get("__page"), p = url.searchParams.get("__p");   // (':' 는 Vercel 이 목적지 변수로 읽어서 못 씀)
  if (pg !== null) return pg === "index" ? "/" : "/" + pg;
  return p === null ? url.pathname : "/api/" + p;
}
const STATIC = {"/sw.js": ["sw.js", "text/javascript; charset=utf-8"], "/notify.js": ["notify.js", "text/javascript; charset=utf-8"]};
// 홈 화면 추가용 (iPhone 알림은 홈 화면 앱에서만) — start_url = 그 화면 주소(같은 사이트 경로만)
const manifest = start => new Response(JSON.stringify({name: "Revit On-Site 현장", short_name: "On-Site", display: "standalone", background_color: "#ffffff", theme_color: "#1f2a37",
  start_url: /^\/(?![/\\])/.test(start || "") ? start : "/"}), {status: 200, headers: {"Content-Type": "application/manifest+json", "Cache-Control": "no-store"}});
// 어느 화면 토큰인가 → "t" | "v" | "r" | "s" | ""
function role(q) {
  for (const [k, e] of [["t", "FIELD_TOKEN"], ["r", "FIELD_REPORT_TOKEN"], ["v", "FIELD_VIEW_TOKEN"], ["s", "FIELD_SUPERVISOR_TOKEN"]])
    if (q.get(k) && same(q.get(k), env(e))) return k;
  return "";
}
const PAGES_OPEN = {"/": "index.html", "/index.html": "index.html", "/view": "view.html", "/timeline": "timeline.html", "/supervisor": "supervisor.html"};
const SUP_403 = () => json(403, {ok: false, error: "감리 주소가 맞지 않습니다 — 현장 담당자에게 감리 확인 주소를 다시 받으세요"});
const VIEW_403 = () => json(403, {ok: false, error: "보기 주소가 맞지 않습니다 — 층 표지 QR 을 다시 찍으세요"});

async function snapshot(store) {
  const t = await store.get("snapshot.json");
  return t ? JSON.parse(t) : null;
}
const nowOf = snap => snap.model.fixed_now ? snap.model.now : kstNow().slice(0, 16);   // 데모(--at) 면 그 시각
const ckOf = snap => ({...snap.check, sensors: snap.model.sensors, curings: snap.model.curings,
                       pourIds: Object.fromEntries(snap.model.floors.flatMap(f => f.zones.map(z => [z.zone_id, (z.pour_id || "").trim()]))),   // W1 타설 묶음
                       kindLabel: Object.fromEntries(snap.model.kinds.map(k => [k.kind, k.label]))});

async function readJson(req, max) {   // → 객체 | Response(오류)
  const buf = Buffer.from(await req.arrayBuffer());
  if (!buf.length || buf.length > max) return json(buf.length ? 413 : 400, {ok: false, error: "보낸 내용이 없거나 너무 큽니다"});
  try {
    const raw = req.headers.get("content-encoding") === "gzip" || req.headers.get("content-type") === "application/gzip" ? zlib.gunzipSync(buf) : buf;
    return {body: JSON.parse(new TextDecoder("utf-8", {fatal: true}).decode(raw))};
  } catch {
    return json(400, {ok: false, error: "요청 형식이 틀렸습니다 (JSON 아님)"});
  }
}

// ---------- 휴대폰 최근 입력: 아직 Revit 이 안 가져간 것 + 스냅샷 recent ----------
const LABEL = {supervise: "감리 확인", cylinder: "공시체", sensor_temp: "온도 센서", sensor_strength: "강도 센서", rebound: "반발경도", shore_check: "동바리 확인", after_check: "해체 후 점검", pour: "타설", shore_inspect: "동바리 점검"};
function pendingItem(rec) {
  const b = rec.body, kind = rec.route === "report" ? "shore_check" : rec.route === "supervise" ? "supervise" : b.kind;
  const sv = {sensor_temp: ["temp_c", "°C"], sensor_strength: ["mpa", "MPa"]}[kind];
  const rd = sv && (Array.isArray(b.readings) ? b.readings : [{timestamp: b.timestamp, [sv[0]]: b[sv[0]]}]);
  const value = kind === "cylinder" ? `${b.f1}·${b.f2}·${b.f3} MPa (${b.curing})` : kind === "rebound" ? `R ${b.r_value}` :
    sv ? `${rd[rd.length - 1][sv[0]]} ${sv[1]}` + (rd.length > 1 ? ` 외 ${rd.length - 1}줄` : "") : "";
  return {client_id: rec.client_id, kind, label: LABEL[kind] || kind, point: b.zone_id || b.sensor_id || (b.pour_id ? "타설 " + b.pour_id : ""), floor: b.floor ?? "",
          when: sv ? rd[rd.length - 1].timestamp : (b.test_date || b.timestamp || b.date || b.pour_date || rec.received_at.slice(0, 16)),
          value, count: 1, entered_by: b.entered_by || b.name || "", entered_at: rec.received_at, note: (b.note || "") + " (Revit 동기화 대기)"};
}
async function pending(store) {   // [{key, at}] 아직 ack 안 된 입력, 오래된 것부터
  const [ins, acks] = await Promise.all([store.list("inputs/"), store.list("acked/")]);
  const done = new Set(acks.map(a => a.key.slice(6)));
  return ins.filter(i => !done.has(i.key.slice(7))).sort((a, b) => a.at - b.at);
}

// ---------- S2 승인 기록 없는 해체 신고 (Revit 판정 전, 사실 표시만) ----------
const UNAPPROVED = "승인 기록 없는 해체 신고 (Revit 판정 전)";
// Revit 이 이미 처리한 입력 = 스냅숏 seen(데이터 폴더 CSV) ∪ 서버 acked/ (거부·데이터 폴더 되돌림이어도 다시 '판정 전'으로 보이지 않게)
async function handled(store, snap) {
  return new Set([...(snap ? snap.check.seen : []), ...(await store.list("acked/")).map(a => a.key.slice(6, -5))]);
}
async function unapproved(store, snap, zones) {   // flags/ 중 아직 Revit 이 처리 안 한 것 → [{client_id, zone_id, at, name, company}]
  const seen = await handled(store, snap);
  const fl = (await store.list("flags/")).filter(f => !seen.has(f.key.slice(6, -5)));
  const out = (await Promise.all(fl.map(f => store.get(f.key)))).filter(Boolean).map(t => JSON.parse(t));
  return out.filter(x => !zones || zones.has(x.zone_id)).sort((a, b) => a.at < b.at ? 1 : -1);
}
async function notSynced(store, snap, max = 100) {   // 아직 Revit 이 처리 안 한 입력 — 최근 것부터
  const seen = await handled(store, snap);
  const ins = (await store.list("inputs/")).filter(i => !seen.has(i.key.slice(7, -5))).sort((a, b) => b.at - a.at).slice(0, max);
  return (await Promise.all(ins.map(i => store.get(i.key)))).filter(Boolean).map(t => JSON.parse(t));
}
const zoneFloor = (snap, zid) => (snap.check.zones[zid] || {}).floor;

// ---------- 입력 저장 (한 건 = 파일 하나) ----------
async function saveInput(store, cid, routeName, body, ua, receivedAt) {
  const {photo_data, ...rest} = body;
  if (photo_data) await store.put(`photos/${cid}.txt`, String(photo_data));
  return store.putNew(`inputs/${cid}.json`, JSON.stringify({client_id: cid, route: routeName, body: rest, has_photo: !!photo_data, ua, received_at: receivedAt}));
}

export async function GET(req) {
  const url = new URL(req.url), q = url.searchParams, p = route(url);
  try {
    if (p === "/favicon.ico") return new Response("", {status: 404});
    if (p === "/api/sync/pull") return syncPull(req);
    if (p === "/manifest.json") return manifest(q.get("start"));
    if (STATIC[p]) return new Response(fs.readFileSync(path.join(PAGES, STATIC[p][0])), {status: 200, headers: {"Content-Type": STATIC[p][1], "Cache-Control": "no-cache"}});
    const store = await getStore();
    const who = role(q);
    if (PAGES_OPEN[p]) return page(PAGES_OPEN[p]);   // 0-7 화면 껍데기는 열쇠 없이 (자료는 아래 API 가 열쇠로 막음)
    if (p === "/api/push/key") return who && pushKey() ? json(200, {ok: true, key: pushKey()}) : json(who ? 404 : 403, {ok: false, error: who ? "알림 설정 없음" : "주소가 맞지 않습니다"});
    if (p === "/api/timeline") return ["v", "r", "t"].includes(who) ? timelineApi(store) : VIEW_403();   // S1 강도 타임라인 (보기 v · 신고 r · 입력 t)
    if (p === "/api/supervisor") return who === "s" ? supervisorApi(store) : SUP_403();             // S3 감리 직접 확인
    if (p === "/api/photo") return who === "s" ? cylPhoto(store, q.get("id")) : SUP_403();            // v6.4 2-4 공시체 사진 (감리만)
    if (p === "/api/floor" && q.get("t")) {   // 휴대폰 동바리 점검 화면 — 입력 토큰
      if (!same(q.get("t"), env("FIELD_TOKEN"))) return json(403, {ok: false, error: "접속 코드가 맞지 않습니다 — PC 화면에 나온 주소(QR)로 다시 여세요"});
      return floor(store, q.get("f"), false, "층 번호(f)가 필요합니다");
    }
    if (p === "/api/floor") {   // 층 화면 자료 (보기 v · 신고 r)
      const r = q.get("r") || "", report = same(r, env("FIELD_REPORT_TOKEN"));
      if (!report && !same(q.get("v"), env("FIELD_VIEW_TOKEN")))
        return json(403, {ok: false, error: r ? "신고 주소가 맞지 않습니다 — 현장 담당자에게 신고 QR 을 다시 받으세요" : "보기 주소가 맞지 않습니다 — 층 표지 QR 을 다시 찍으세요"});
      return floor(store, q.get("f"), report, "층 번호(f)가 필요합니다 — 층 표지 QR 을 다시 찍으세요");
    }
    if (!same(q.get("t"), env("FIELD_TOKEN"))) return json(403, {ok: false, error: "접속 코드가 맞지 않습니다 — PC 화면에 나온 주소(QR)로 다시 여세요"});
    if (p === "/api/model") {
      const snap = await snapshot(store);
      return snap ? json(200, {...snap.model, synced_at: snap.pushed_at, unapproved_reports: await unapproved(store, snap)}) : json(503, {ok: false, error: NO_SNAPSHOT});
    }
    if (p === "/api/recent") {
      let n = parseInt(q.get("n") || "20", 10);
      n = Number.isNaN(n) ? 20 : Math.max(1, Math.min(n, 200));
      const snap = await snapshot(store);
      const pend = (await pending(store)).reverse().slice(0, n);
      const recs = (await Promise.all(pend.map(i => store.get(i.key)))).filter(Boolean).map(t => pendingItem(JSON.parse(t)));
      const ids = new Set(recs.map(r => r.client_id));
      return json(200, {ok: true, items: [...recs, ...(snap ? snap.recent : []).filter(r => !ids.has(r.client_id))].slice(0, n)});
    }
    return json(404, {ok: false, error: "없는 주소입니다"});
  } catch (x) {
    console.error(x);
    return json(500, {ok: false, error: `서버 오류: ${x.message}`});
  }
}

async function floor(store, f, report, needMsg) {
  if (!/^\s*[+-]?\d+\s*$/.test(f || "")) return json(400, {ok: false, error: needMsg});
  const snap = await snapshot(store);
  if (!snap) return json(503, {ok: false, error: NO_SNAPSHOT});
  const fl = Number(f), v = snap.floors[String(fl)];
  if (!v) return json(404, {ok: false, error: `${fl}층 구역이 없습니다 — 층 표지 QR 을 다시 찍으세요`});
  return json(200, {...v, can_report: report, synced_at: snap.pushed_at, unapproved_reports: await unapproved(store, snap, new Set(v.zones.map(z => z.zone_id)))});
}

// ---------- S1 강도 타임라인 자료 (그림만 — 판정·예측 다시 계산 안 함) ----------
async function timelineApi(store) {
  const snap = await snapshot(store);
  if (!snap) return json(503, {ok: false, error: NO_SNAPSHOT});
  const m = snap.model;
  const pend = (await notSynced(store, snap)).filter(r => r.route === "report" || ["cylinder", "shore_check"].includes(r.body.kind)).map(r => {
    const b = r.body, kind = r.route === "report" ? "report" : b.kind;
    const f = [b.f1, b.f2, b.f3].map(x => Number(String(x).replace(",", ".")));
    return {client_id: r.client_id, kind, zone_id: b.zone_id, floor: zoneFloor(snap, b.zone_id), received_at: r.received_at,
            when: kind === "cylinder" ? b.test_date : kind === "shore_check" ? String(b.timestamp || "").replace(" ", "T").slice(0, 16) : m.fixed_now ? m.now : r.received_at.slice(0, 16),   // 신고 = 받은 시각 (데모 고정이면 그 시각, 서버 report 와 같음)
            ...(kind === "cylinder" ? {mpa: Math.round(f.reduce((a, c) => a + c, 0) / 3 * 10) / 10, curing: b.curing} : {present: b.present})};
  });
  return json(200, {ok: true, project: m.project, now: m.now, fixed_now: m.fixed_now, synced_at: snap.pushed_at, server_now: kstNow(),
                    timeline: snap.timeline || null, alerts: m.alerts || [],
                    floors: m.floors.map(f => ({floor: f.floor, name: f.name, zones: f.zones.map(z => ({zone_id: z.zone_id, x0: z.x0, y0: z.y0, x1: z.x1, y1: z.y1, status: z.status, decision: z.decision}))})),
                    pending: pend, unapproved_reports: await unapproved(store, snap)});
}

// ---------- S3 감리 직접 확인 — v6.3 1-2: ⓐ 작업계획서 확인(plan) · ⓑ 최종 확인(final) · 보완 요청(reject) — 가이드라인 표 3.2.1 3단계·6단계, 3.2.3 보완 ----------
const SUP_CHECKS = ["① 위 3개층 동바리 존치 (최근 24 h) 현장 확인", "② 작업계획서 확인", "③ 해체 전 체크리스트 확인 (가이드라인 4.1)"];   // approval.site_checks 의 세 칸 (final)
// v6.4 2-4: ③ 을 가이드라인 4.1 '작업준비·작업자·작업허가' 항목으로 펼침 — 화면은 항목을 모두 체크해야 ③ true (PC 로 가는 checks 는 3개 그대로)
const SUP_CHECK3 = [
  ["작업준비", ["작업공간·해체자재 적재장소·조명 등 작업환경 적정", "해체작업 반경 안 관계자 외 출입 통제", "근로자 추락 방지 조치 (작업발판 등)", "낙하물 사고 방지 조치", "해체작업 안전관리책임자·안전관리담당자 지정"]],
  ["작업자", ["해체작업 근로자 특별안전교육 이수", "근로자 자격 (산업안전보건법 제140조·유해위험작업 취업 제한 규칙)", "작업 시작 전 근로자에게 해체작업계획 교육"]],
  ["작업허가", ["존치기간 준수·타설 콘크리트 압축강도 발현 확인"]]];
const EV_NAMES = {1: "설치검측", 2: "작업계획서", 3: "공시체", 4: "해체전점검", 5: "검측요청", 6: "감리확인", 7: "해체", 8: "결과보고"};   // shoring/evidence.py STAGES
const SUP_ACTIONS = ["plan", "final", "reject"];
async function supHistory(store) {   // sup/<cid>.json(보낸 감리 확인 목록) + acked/<cid>.json(Revit 처리 결과) → {zone_id: [{action, at, name, reason, state, error}]} 최근 것부터
  const [sups, acks] = await Promise.all([store.list("sup/"), store.list("acked/")]);
  const acked = new Set(acks.map(a => a.key.slice(6, -5)));
  const recs = (await Promise.all(sups.map(async s => {
    const t = await store.get(s.key); if (!t) return null;
    const r = JSON.parse(t), cid = r.client_id;
    if (!acked.has(cid)) return {...r, state: "대기"};
    const a = JSON.parse(await store.get(`acked/${cid}.json`) || "{}");
    return {...r, state: a.error ? "거절" : "기록됨", error: a.error || "", done_at: a.acked_at || ""};
  }))).filter(Boolean);
  const out = {};
  for (const r of recs.sort((a, b) => a.at < b.at ? 1 : -1)) (out[r.zone_id] ||= []).push(r);
  return out;
}
async function cylPhotos(store) {   // {zone_id: [{id, test_date, curing, lot}]} 최근 것부터 — 공시체 사진 (휴대폰 공시체 폼, v6.4 2-4)
  const out = {};
  const recs = (await Promise.all((await store.list("cylph/")).map(async c => { const t = await store.get(c.key); return t ? JSON.parse(t) : null; }))).filter(Boolean);
  for (const r of recs.sort((a, b) => a.at < b.at ? 1 : -1)) for (const z of r.zones || []) (out[z] ||= []).push({id: r.client_id, test_date: r.test_date, curing: r.curing, lot: r.lot});
  return out;
}
async function cylPhoto(store, id) {
  if (!CLIENT_ID.test(String(id || "")) || !(await store.exists(`cylph/${id}.json`))) return json(404, {ok: false, error: "사진이 없습니다"});
  const d = await store.get(`photos/${id}.txt`);
  const m = /^data:(image\/(?:jpeg|png));base64,(.+)$/s.exec(d || "");
  if (!m) return json(404, {ok: false, error: "사진이 없습니다"});
  return new Response(Buffer.from(m[2], "base64"), {status: 200, headers: {"Content-Type": m[1], "Cache-Control": "private, max-age=3600", "X-Content-Type-Options": "nosniff"}});
}
async function supervisorZones(store, snap) {
  const req = snap.check.requested || {}, appr = snap.check.approved_at || {}, ev = snap.check.evidence || null, planAt = snap.check.plan_confirmed || {}, em = snap.check.evidence_meta || {};
  const [hist, photos] = await Promise.all([supHistory(store), cylPhotos(store)]);
  const out = [];
  for (const f of snap.model.floors) for (const z of f.zones) {
    if (!req[z.zone_id]) continue;
    const h = hist[z.zone_id] || [], live = a => h.some(x => x.action === a && x.state !== "거절");   // 보냈고 거절되지 않음
    const waiting = a => h.some(x => x.action === a && x.state === "대기");
    const e = ev ? ev[z.zone_id] || {} : null;
    const done = appr[z.zone_id] || [6, 7, 8].includes(z.status) ? "이미 감리 승인·해체 기록이 있습니다" : "";
    const planOk = !!planAt[z.zone_id] || live("plan");
    const whyPlan = done || (planAt[z.zone_id] ? `작업계획서 확인 기록 있음 (${planAt[z.zone_id].slice(0, 16).replace("T", " ")})` : waiting("plan") ? "작업계획서 확인 보냄 — Revit 동기화 대기" : "");
    const whyFinal = done || (waiting("final") ? "최종 확인 보냄 — Revit 동기화 대기"
      : z.decision !== "해체 승인 요청 가능" ? `지금 판정이 '${z.decision || "없음"}' 입니다 (Revit 판정 기준)`
      : !planOk ? "ⓐ 작업계획서 확인을 먼저 남기세요 (가이드라인 3단계)"
      : e && !e["4"] ? "해체 전 점검(근거 원장 4) 기록이 없습니다 — 시공자에게 점검 기록을 요청하세요" : "");
    out.push({zone_id: z.zone_id, floor: f.floor, name: f.name, decision: z.decision, reason: z.reason, threshold: z.threshold, f_cyl: z.f_cyl,
              cyl_test_date: z.cyl_test_date, f_est_min: z.f_est_min, level_gate: z.level_gate, evidence_stage: z.evidence_stage,
              evidence: e ? Object.entries(EV_NAMES).map(([n, name]) => ({n: Number(n), name, date: e[n] || "", ...((em[z.zone_id] || {})[n] || {})})) : null,   // + by·at (있으면)
              cyl_entered_by: z.cyl_entered_by || "", cyl_entered_at: z.cyl_entered_at || "",   // v6.4 '누가 넣었나' (스냅숏에 있으면 — API.md §10.5)
              plan_confirmed_at: planAt[z.zone_id] || "", requested_at: req[z.zone_id].at, requested_by: req[z.zone_id].actor,
              can_plan: !whyPlan, why_plan: whyPlan, can_final: !whyFinal, why_final: whyFinal, can_reject: !done, why_reject: done,
              can_confirm: !whyFinal, why_not: whyFinal,   // 옛 이름 (final 과 같음)
              photos: (photos[z.zone_id] || []).slice(0, 3),
              history: h.slice(0, 5).map(x => ({action: x.action, at: x.at, name: x.name, reason: x.reason || "", state: x.state, error: x.error || ""}))});
  }
  return out;
}
async function supervisorApi(store) {
  const snap = await snapshot(store);
  if (!snap) return json(503, {ok: false, error: NO_SNAPSHOT});
  return json(200, {ok: true, project: snap.model.project, now: snap.model.now, fixed_now: snap.model.fixed_now, synced_at: snap.pushed_at,
                    server_now: kstNow(), checks: SUP_CHECKS, check3: SUP_CHECK3.map(([group, items]) => ({group, items})), zones: await supervisorZones(store, snap)});
}

export async function POST(req) {
  const url = new URL(req.url), q = url.searchParams, p = route(url);
  if (p === "/api/sync/push") return syncPush(req);
  if (p === "/api/sync/ack") return syncAck(req);
  if (p === "/api/push/subscribe") return subscribe(req, q);
  if (p === "/api/supervise") return superviseApi(req, q);
  if (p === "/api/report") {
    if (!same(q.get("r"), env("FIELD_REPORT_TOKEN"))) return json(403, {ok: false, error: "신고 주소가 맞지 않습니다 — 현장 담당자에게 신고 QR 을 다시 받으세요"});
  } else if (!same(q.get("t"), env("FIELD_TOKEN"))) return json(403, {ok: false, error: "접속 코드가 맞지 않습니다 — PC 화면에 나온 주소(QR)로 다시 여세요"});
  else if (p !== "/api/measure") return json(404, {ok: false, error: "없는 주소입니다"});
  const r = await readJson(req, 12_000_000);
  if (r instanceof Response) return r;
  const b = r.body, ua = req.headers.get("user-agent") || "";
  try {
    const store = await getStore(), snap = await snapshot(store);
    if (!snap) return json(503, {ok: false, error: NO_SNAPSHOT});
    const seen = new Set(snap.check.seen), now = nowOf(snap);
    if (p === "/api/report") {
      const {zid, present, cid} = reportHead(b, snap);
      let dup = seen.has(cid) || await store.exists(`inputs/${cid}.json`);
      if (!dup) {
        photoCheck(b);
        dup = !(await saveInput(store, cid, "report", b, ua, kstNow()));
        const ap = snap.check.approved_at[zid];
        if (!dup && present === "아니오" && !(ap && ap <= now + ":00")) {   // S2: 승인 기록 없는 '뺐음' → 즉시 빨강 + 알림 (판정 아님, 사실 표시)
          const name = text(b.name, "이름", 20, true), company = text(b.company, "소속", 40);
          await store.put(`flags/${cid}.json`, JSON.stringify({client_id: cid, zone_id: zid, at: now, name, company}));
          await sendAll(store, {title: UNAPPROVED, body: `${zid} — ${name}${company ? " (" + company + ")" : ""} · ${now.replace("T", " ")}`, tag: "s2-" + zid});
        }
      }
      return json(200, {ok: true, saved: REPORT_FILE, duplicate: dup, zone_id: zid, present, timestamp: now,
                        message: reportMessage(present, snap.check.approved_at[zid], now), pending_sync: true});
    }
    const {kind, cid} = measureHead(b, snap);
    if (seen.has(cid) || await store.exists(`inputs/${cid}.json`))   // 오프라인 큐 재전송 → 저장하지 않고 성공
      return json(200, measureAnswer(kind, snap, {duplicate: true, rows: 0}));
    const {rows, info} = validate(kind, b, ckOf(snap), now);
    if (rows.length && !(await saveInput(store, cid, "measure", b, ua, kstNow())))
      return json(200, measureAnswer(kind, snap, {duplicate: true, rows: 0}));
    if (kind === "cylinder" && b.photo_data)   // v6.4 2-4: 공시체 사진은 감리 화면에서 보도록 서버에 남김 (ack 때 안 지움)
      await store.put(`cylph/${cid}.json`, JSON.stringify({client_id: cid, zones: info.lot_zones || [String(b.zone_id).trim()], lot: info.lot_zones ? String(b.pour_id) : "",
                                                          test_date: b.test_date, curing: b.curing, at: kstNow()}));
    return json(200, measureAnswer(kind, snap, {...info, rows: rows.length}));
  } catch (x) {
    if (x instanceof Bad) return json(400, {ok: false, error: x.message});
    console.error(x);
    return json(500, {ok: false, error: `서버 오류 — 저장되지 않았을 수 있습니다: ${x.message}`});
  }
}

// ---------- Revit 동기화 (쓰기 키: Authorization: Bearer <FIELD_SYNC_KEY>) ----------
const syncAuthed = req => same((req.headers.get("authorization") || "").replace(/^Bearer\s+/i, ""), env("FIELD_SYNC_KEY"));
const SYNC_403 = () => json(403, {ok: false, error: "동기화 키가 맞지 않습니다 — Revit 의 서버 설정(FIELD_SYNC_KEY)을 확인하세요"});

async function syncPush(req) {
  if (!syncAuthed(req)) return SYNC_403();
  const r = await readJson(req, 4_400_000);
  if (r instanceof Response) return r;
  const s = r.body;
  if (!s || !s.model || !s.model.ok || !s.floors || !s.check || !Array.isArray(s.recent)) return json(400, {ok: false, error: "스냅샷 형식이 틀렸습니다 (model·floors·recent·check)"});
  const body = JSON.stringify(s);
  let sent = 0;
  try {
    const store = await getStore(), old = await snapshot(store);
    await store.put("snapshot.json", body);
    // S8: 오늘 볼 것에 새로 생긴 '타설 보류'·'무단 해체 의심' (앞 스냅숏에 없던 것) → 알림
    const key = a => a.kind + "|" + a.zone_id, PUSH_KINDS = ["무단 해체 의심", "타설 보류"];
    const before = new Set(((old && old.model.alerts) || []).map(key));
    const fresh = (s.model.alerts || []).filter(a => PUSH_KINDS.includes(a.kind) && !before.has(key(a)));
    if (fresh.length) sent = await sendAll(store, {title: `오늘 볼 것 — ${[...new Set(fresh.map(a => a.kind))].join(" · ")} ${fresh.length}건`,
                                                    body: fresh.slice(0, 3).map(a => a.text).join(" / ") + (fresh.length > 3 ? " …" : ""), tag: "sync"});
  } catch (x) {
    console.error(x);
    return json(500, {ok: false, error: `저장 실패: ${x.message}`});
  }
  return json(200, {ok: true, bytes: body.length, floors: Object.keys(s.floors).length, pushed: sent});
}

async function syncPull(req) {
  if (!syncAuthed(req)) return SYNC_403();
  try {
    const store = await getStore(), items = [];
    let size = 0, more = false;
    for (const i of await pending(store)) {
      if (items.length && size > PULL_BYTES) { more = true; break; }
      const t = await store.get(i.key);
      if (!t) continue;
      const rec = JSON.parse(t);
      if (rec.has_photo) {
        const ph = await store.get(`photos/${rec.client_id}.txt`);
        if (ph) rec.body.photo_data = ph;
      }
      delete rec.has_photo;
      size += t.length + (rec.body.photo_data || "").length;
      items.push(rec);
    }
    return json(200, {ok: true, items, more});
  } catch (x) {
    console.error(x);
    return json(500, {ok: false, error: `서버 오류: ${x.message}`});
  }
}

async function syncAck(req) {
  if (!syncAuthed(req)) return SYNC_403();
  const r = await readJson(req, 1_000_000);
  if (r instanceof Response) return r;
  const items = r.body && r.body.items;
  if (!Array.isArray(items) || items.some(it => !it || !CLIENT_ID.test(String(it.client_id || ""))))
    return json(400, {ok: false, error: "items = [{client_id, error?}] 형식이어야 합니다"});
  try {
    const store = await getStore();
    for (const it of items) {
      await store.put(`acked/${it.client_id}.json`, JSON.stringify({error: String(it.error || ""), acked_at: kstNow()}));
      if (!(await store.exists(`cylph/${it.client_id}.json`)))   // 공시체 사진은 감리 화면용으로 남김 (v6.4 2-4)
        await store.del(`photos/${it.client_id}.txt`).catch(() => {});   // 사진 원본은 Revit 데이터 폴더에 저장됨 — 서버엔 남기지 않음
    }
    return json(200, {ok: true, acked: items.length});
  } catch (x) {
    console.error(x);
    return json(500, {ok: false, error: `서버 오류: ${x.message}`});
  }
}

// ---------- S8 알림 구독 ----------
async function subscribe(req, q) {
  const who = role(q);
  if (!who) return json(403, {ok: false, error: "주소가 맞지 않습니다"});
  if (!pushKey()) return json(404, {ok: false, error: "알림 설정 없음"});
  const r = await readJson(req, 10_000);
  if (r instanceof Response) return r;
  const sub = cleanSub(r.body);
  if (!sub) return json(400, {ok: false, error: "알림 구독 형식이 틀렸습니다"});
  await (await getStore()).put(subKey(sub.endpoint), JSON.stringify({...sub, role: who, at: kstNow()}));
  return json(200, {ok: true});
}

// ---------- S3 감리 직접 확인 저장 → pull route "supervise" ----------
async function superviseApi(req, q) {
  if (role(q) !== "s") return SUP_403();
  const r = await readJson(req, 20_000);
  if (r instanceof Response) return r;
  const b = r.body;
  try {
    if (!b || typeof b !== "object" || Array.isArray(b)) throw new Bad("요청 형식이 틀렸습니다 (JSON 객체가 아님)");
    const cid = String(b.client_id || "");
    if (!CLIENT_ID.test(cid)) throw new Bad("client_id 가 없거나 형식이 틀렸습니다 (영문·숫자·-·_ 80자 이내)");
    const name = text(b.name, "감리 이름", 20, true);
    const action = b.action === undefined || b.action === null || b.action === "" ? "final" : String(b.action);
    if (!SUP_ACTIONS.includes(action)) throw new Bad(`action '${action}' 은(는) 없습니다 — plan · final · reject 중 하나`);
    if (action === "final" && !(Array.isArray(b.checks) && b.checks.length === 3 && b.checks.every(c => c === true))) throw new Bad("현장 확인 3가지를 모두 체크해야 승인 기록을 남깁니다");
    const reason = action === "reject" ? text(b.reason, "보완 사유", 100, true) : "";
    const store = await getStore(), snap = await snapshot(store);
    if (!snap) return json(503, {ok: false, error: NO_SNAPSHOT});
    const zid = String(b.zone_id || "").trim();
    if (new Set(snap.check.seen).has(cid) || await store.exists(`inputs/${cid}.json`))
      return json(200, {ok: true, duplicate: true, zone_id: zid, pending_sync: true, message: "이미 보낸 감리 확인입니다"});
    const z = (await supervisorZones(store, snap)).find(x => x.zone_id === zid);
    if (!z) throw new Bad(`${zid} 는 요청 제출된 구역이 아닙니다 — 화면을 새로 고치세요`);
    const why = {plan: z.why_plan, final: z.why_final, reject: z.why_reject}[action];
    if (why) throw new Bad(`${zid}: ${why}`);
    const at = kstNow(), body = {client_id: cid, zone_id: zid, name, action, ...(action === "final" ? {checks: [true, true, true]} : {}), ...(reason ? {reason} : {})};
    const ok = await saveInput(store, cid, "supervise", body, req.headers.get("user-agent") || "", at);
    if (ok) await store.put(`sup/${cid}.json`, JSON.stringify({client_id: cid, zone_id: zid, action, at, name, reason}));   // 감리 화면 '보냄 → 기록됨/거절' 표시용
    return json(200, {ok: true, duplicate: !ok, zone_id: zid, action, pending_sync: true, message: {
      plan: "작업계획서 확인 저장됨 — Revit 동기화 때 근거 원장(2 작업계획서)에 감리 확인으로 들어갑니다",
      final: "최종 확인 저장됨 — Revit 동기화 때 승인 기록으로 들어갑니다 (그때 판정·점검 기록이 맞지 않으면 거절되고 사유가 이 화면에 나옵니다)",
      reject: "보완 요청 저장됨 — Revit 동기화 때 시공자 '오늘 볼 것'에 사유가 뜹니다"}[action]});
  } catch (x) {
    if (x instanceof Bad) return json(400, {ok: false, error: x.message});
    console.error(x);
    return json(500, {ok: false, error: `서버 오류 — 저장되지 않았을 수 있습니다: ${x.message}`});
  }
}

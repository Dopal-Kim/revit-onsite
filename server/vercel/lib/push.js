// 웹 푸시 (v6.2 S8) — VAPID 키는 환경 변수만 (FIELD_VAPID_PUBLIC · FIELD_VAPID_PRIVATE · FIELD_VAPID_SUBJECT). 외부 문자·메신저 서비스 없음.
// 구독 = Blob subs/<endpoint SHA-256>.json. 만료(404·410) 구독은 지움. 키가 없으면 아무것도 안 함.
import crypto from "node:crypto";

const env = k => (process.env[k] || "").trim();
export const pushKey = () => env("FIELD_VAPID_PUBLIC");
const ready = () => !!(env("FIELD_VAPID_PUBLIC") && env("FIELD_VAPID_PRIVATE"));

export function subKey(endpoint) {
  return "subs/" + crypto.createHash("sha256").update(endpoint).digest("hex").slice(0, 40) + ".json";
}

// 브라우저 PushSubscription JSON 검사 → 저장할 모양 | null
export function cleanSub(b) {
  const ok = b && typeof b.endpoint === "string" && /^https:\/\/[^\s]{8,600}$/.test(b.endpoint) && b.keys &&
    typeof b.keys.p256dh === "string" && typeof b.keys.auth === "string" && b.keys.p256dh.length < 200 && b.keys.auth.length < 100;
  return ok ? {endpoint: b.endpoint, keys: {p256dh: b.keys.p256dh, auth: b.keys.auth}} : null;
}

// payload = {title, body, tag} → 보낸 수 (오류는 삼키고 만료만 지움 — 알림 실패가 입력 저장을 막지 않게)
export async function sendAll(store, payload) {
  if (!ready()) return 0;
  const wp = (await import("web-push")).default;
  wp.setVapidDetails(env("FIELD_VAPID_SUBJECT") || "mailto:onsite@example.invalid", env("FIELD_VAPID_PUBLIC"), env("FIELD_VAPID_PRIVATE"));
  const subs = await store.list("subs/");
  const res = await Promise.allSettled(subs.map(async s => {
    const t = await store.get(s.key);
    if (!t) return false;
    try {
      await wp.sendNotification(JSON.parse(t), JSON.stringify(payload), {TTL: 6 * 3600, urgency: "high"});
      return true;
    } catch (x) {
      if (x.statusCode === 404 || x.statusCode === 410) await store.del(s.key);
      else console.error("push 실패", x.statusCode || "", (x.body || x.message || "").slice(0, 120));
      return false;
    }
  }));
  return res.filter(r => r.status === "fulfilled" && r.value).length;
}

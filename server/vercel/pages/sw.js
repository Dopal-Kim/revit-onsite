// Revit On-Site 알림 서비스 워커 (v6.2 S8) — 받은 알림을 보여 주고, 누르면 열린 화면으로 (없으면 구독한 화면 주소로)
self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", e => e.waitUntil(self.clients.claim()));
// v6.4 2-3 오프라인 열기: 화면(HTML)은 네트워크 먼저, 6 초 안에 안 오거나 끊기면 마지막으로 받은 화면 (자료는 화면이 기기 저장소에서 — index 모델·view 층 자료)
const PAGES = ["/", "/index.html", "/view", "/timeline", "/supervisor"];
self.addEventListener("fetch", e => {
  const u = new URL(e.request.url);
  if (e.request.method !== "GET" || u.origin !== self.location.origin || e.request.mode !== "navigate" || !PAGES.includes(u.pathname)) return;
  e.respondWith((async () => {
    const c = await caches.open("onsite-pages");
    try {
      const r = await Promise.race([fetch(e.request), new Promise((_, no) => setTimeout(() => no(new Error("slow")), 6000))]);
      if (r.ok) await c.put(u.pathname, r.clone());
      return r;
    } catch (_) {
      return (await c.match(u.pathname)) || new Response("오프라인 — 이 화면을 한 번도 연 적이 없어 열 수 없습니다. 연결된 곳에서 한 번 여세요.", {status: 503, headers: {"Content-Type": "text/plain; charset=utf-8"}});
    }
  })());
});
self.addEventListener("push", e => {
  let d = {};
  try { d = e.data ? e.data.json() : {}; } catch (_) { d = {body: e.data ? e.data.text() : ""}; }
  e.waitUntil(self.registration.showNotification(d.title || "Revit On-Site", {body: d.body || "", tag: d.tag || "onsite", renotify: true}));
});
// 화면이 알려 준 자기 주소(토큰 포함)는 이 기기 안 캐시에만 둠 — 서버에 보내지 않음
self.addEventListener("message", e => {
  if (e.data && typeof e.data.open === "string") e.waitUntil(caches.open("onsite-cfg").then(c => c.put("/__open", new Response(e.data.open))));
});
self.addEventListener("notificationclick", e => {
  e.notification.close();
  e.waitUntil((async () => {
    const ws = await self.clients.matchAll({type: "window", includeUncontrolled: true});
    if (ws.length) return ws[0].focus();
    const r = await (await caches.open("onsite-cfg")).match("/__open");
    return self.clients.openWindow(r ? await r.text() : "/");
  })());
});

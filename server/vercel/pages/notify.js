// [알림 받기] (v6.2 S8) — Vercel 서버에서만 동작 (현장 Wi-Fi 서버엔 이 파일·/api/push 가 없어 버튼이 안 보임).
// 화면에 <div id="notify" hidden></div> 가 있으면 그 자리에 버튼. 보내는 것: 승인 기록 없는 해체 신고 · 새 타설 보류·무단 해체 의심.
(async () => {
  const box = document.getElementById("notify");
  if (!box) return;
  // 열쇠: 주소에 있으면 그것, 없으면(v6.3 0-7 로 주소창에서 지움) 화면이 기기에 옮겨 둔 것 (onsite.tok.*)
  const q = new URLSearchParams(location.search), ls = k => { try { return localStorage.getItem("onsite.tok." + k); } catch (_) { return null; } };
  let k = ["t", "v", "r", "s"].find(x => q.get(x)), tok = k ? q.get(k) : "";
  if (!k) { k = ls("last" + location.pathname); tok = k ? ls(k) || "" : ""; }
  if (!k || !tok) return;
  const auth = k + "=" + encodeURIComponent(tok);
  const ios = /iPhone|iPad|iPod/.test(navigator.userAgent);
  const IOS_NOTE = "iPhone 은 Safari [공유] → [홈 화면에 추가] 한 뒤 그 아이콘으로 열어야 알림을 받을 수 있습니다.";
  const say = (h) => { box.hidden = false; box.innerHTML = h; };
  // 홈 화면에 추가할 때 이 화면으로 다시 열리게 — 홈 화면 앱은 Safari 와 저장소가 따로라 열쇠를 시작 주소에 넣음 (처음 열 때 화면이 다시 지움)
  const start = new URL(location.href); start.searchParams.set(k, tok); start.hash = "";
  const ln = document.createElement("link"); ln.rel = "manifest"; ln.href = "/manifest.json?start=" + encodeURIComponent(start.pathname + start.search);
  document.head.appendChild(ln);
  if (!window.isSecureContext || !("serviceWorker" in navigator) || !("PushManager" in window) || !("Notification" in window)) {
    if (ios) say(`<span>🔔 ${IOS_NOTE}</span>`);
    return;
  }
  let key;
  try {
    const r = await fetch("/api/push/key?" + auth, {cache: "no-store"});
    if (!r.ok) return;
    key = (await r.json()).key;
  } catch (_) { return; }
  const b64u = s => { const p = "=".repeat((4 - s.length % 4) % 4), raw = atob((s + p).replace(/-/g, "+").replace(/_/g, "/")); return Uint8Array.from(raw, c => c.charCodeAt(0)); };
  const reg = await navigator.serviceWorker.register("/sw.js");
  const tellSW = async () => { const r = await navigator.serviceWorker.ready; if (r.active) r.active.postMessage({open: location.href}); };
  const on = () => say(`<span>🔔 이 기기에서 알림 받는 중</span>`);
  if (Notification.permission === "granted" && await reg.pushManager.getSubscription()) { on(); tellSW(); return; }
  say(`<button type="button" id="notifyBtn">🔔 알림 받기</button> <span class="sub">승인 기록 없는 해체 신고·새 타설 보류·무단 해체 의심을 이 기기로 알립니다.${ios ? " " + IOS_NOTE : ""}</span>`);
  document.getElementById("notifyBtn").onclick = async () => {
    try {
      if (await Notification.requestPermission() !== "granted") { say("<span>알림이 꺼져 있습니다. 브라우저 설정에서 이 사이트 알림을 허용하세요</span>"); return; }
      const sub = await reg.pushManager.subscribe({userVisibleOnly: true, applicationServerKey: b64u(key)});
      const r = await fetch("/api/push/subscribe?" + auth, {method: "POST", headers: {"Content-Type": "application/json"}, body: JSON.stringify(sub)});
      if (!r.ok) throw new Error((await r.json().catch(() => ({}))).error || r.status);
      await tellSW(); on();
    } catch (x) { say(`<span>알림을 켜지 못했습니다: ${String(x && x.message || x).replace(/[<>&]/g, "")}</span>`); }
  };
})();

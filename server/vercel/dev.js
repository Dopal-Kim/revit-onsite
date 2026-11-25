// 로컬 시험용 서버 (Vercel 없이): FIELD_LOCAL_DIR 폴더에 저장. 실행: FIELD_LOCAL_DIR=<폴더> FIELD_TOKEN=… node dev.js [포트]
// vercel.json 의 rewrites 를 Vercel 처럼 먼저 적용 (원래 주소의 쿼리는 그대로 + 목적지 쿼리) — 로컬 시험이 배포와 같은 길을 타게.
import fs from "node:fs";
import http from "node:http";
import {GET, POST} from "./api/handler.js";

const port = Number(process.argv[2] || 8787);
const RULES = JSON.parse(fs.readFileSync(new URL("./vercel.json", import.meta.url), "utf8")).rewrites.map(r => ({
  re: new RegExp("^" + r.source.replace(/[.+?^${}()|[\]\\]/g, "\\$&").replace("/:p*", "/(.*)") + "$"), dest: r.destination}));

function rewrite(raw) {
  const u = new URL(raw, "http://x");
  if (u.pathname === "/api/handler") return raw;   // Vercel 은 실제 함수 파일이 먼저 (다시 쓰지 않음)
  for (const {re, dest} of RULES) {
    const m = re.exec(u.pathname);
    if (!m) continue;
    const d = new URL(dest.replace(":p*", m[1] ?? ""), "http://x");
    for (const [k, v] of u.searchParams) d.searchParams.append(k, v);
    return d.pathname + d.search;
  }
  return raw;
}

http.createServer(async (req, res) => {
  const chunks = [];
  for await (const c of req) chunks.push(c);
  const r = new Request(`http://localhost:${port}${rewrite(req.url)}`, {method: req.method, headers: req.headers,
                         body: req.method === "GET" || req.method === "HEAD" ? undefined : Buffer.concat(chunks)});
  const out = await (req.method === "POST" ? POST : GET)(r);
  res.writeHead(out.status, Object.fromEntries(out.headers));
  res.end(Buffer.from(await out.arrayBuffer()));
}).listen(port, "127.0.0.1", () => console.log(`http://127.0.0.1:${port}`));

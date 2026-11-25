// 저장소: Vercel Blob (BLOB_READ_WRITE_TOKEN 이 있으면) 또는 로컬 폴더 (FIELD_LOCAL_DIR — 로컬 시험용 dev.js).
// 입력 한 건 = 파일 하나 (동시 저장 충돌 없음). 경로: snapshot.json · inputs/<cid>.json · photos/<cid>.txt · acked/<cid>.json
import fs from "node:fs/promises";
import path from "node:path";

const ACCESS = process.env.BLOB_ACCESS || "private";   // 개인 정보(이름·사진) — 비공개 스토어 [기본]

function local(dir) {
  const p = k => path.join(dir, ...k.split("/"));
  return {
    async get(k) { try { return await fs.readFile(p(k), "utf8"); } catch (e) { if (e.code === "ENOENT") return null; throw e; } },
    async put(k, text) { await fs.mkdir(path.dirname(p(k)), {recursive: true}); await fs.writeFile(p(k), text); },
    async putNew(k, text) {   // 이미 있으면 false (wx = 없을 때만 만듦)
      await fs.mkdir(path.dirname(p(k)), {recursive: true});
      try { await fs.writeFile(p(k), text, {flag: "wx"}); return true; } catch (e) { if (e.code === "EEXIST") return false; throw e; }
    },
    async exists(k) { try { await fs.access(p(k)); return true; } catch { return false; } },
    async list(prefix) {   // → [{key, at}]
      const d = p(prefix);
      let names = [];
      try { names = await fs.readdir(d); } catch { return []; }
      return Promise.all(names.map(async n => ({key: prefix + n, at: (await fs.stat(path.join(d, n))).mtimeMs})));
    },
    async del(k) { await fs.rm(p(k), {force: true}); },
  };
}

async function blob() {
  const b = await import("@vercel/blob");
  const opt = {access: ACCESS, addRandomSuffix: false, contentType: "application/json", cacheControlMaxAge: 60};
  const get = async k => {
    const r = await b.get(k, {access: ACCESS, useCache: false});
    return r && r.statusCode === 200 ? await new Response(r.stream).text() : null;
  };
  const exists = async k => { try { await b.head(k); return true; } catch (e) { if (e instanceof b.BlobNotFoundError) return false; throw e; } };
  return {
    get,
    exists,
    async put(k, text) { await b.put(k, text, {...opt, allowOverwrite: true}); },
    async putNew(k, text) {
      try { await b.put(k, text, {...opt, allowOverwrite: false}); return true; }
      catch (e) { if (await exists(k)) return false; throw e; }   // 이미 있음 → 중복
    },
    async list(prefix) {
      const out = [];
      let cursor;
      do {
        const r = await b.list({prefix, cursor, limit: 1000});
        out.push(...r.blobs.map(x => ({key: x.pathname, at: new Date(x.uploadedAt).getTime()})));
        cursor = r.hasMore ? r.cursor : undefined;
      } while (cursor);
      return out;
    },
    async del(k) { await b.del(k); },
  };
}

let store;
export async function getStore() {
  if (!store) {
    if (process.env.BLOB_READ_WRITE_TOKEN) store = await blob();
    else if (process.env.FIELD_LOCAL_DIR) store = local(process.env.FIELD_LOCAL_DIR);
    else throw new Error("저장소가 없습니다 — Vercel Blob 스토어를 프로젝트에 연결하세요 (BLOB_READ_WRITE_TOKEN)");
  }
  return store;
}

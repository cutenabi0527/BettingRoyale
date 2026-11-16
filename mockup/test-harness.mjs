// 테스트 공용 유틸. 실제 서버를 띄우고 HTTP/SSE 로만 붙는다.
// 종료 시 process.exit() 을 곧바로 부르면 윈도우 node 에서 자식 프로세스 핸들
// 정리와 경합해 libuv assert 로 죽는다. finish() 가 한 박자 늦춰서 그걸 막는다.
import http from 'http';
import net from 'net';
import { spawn } from 'child_process';

export async function startServer(cwd) {
  const port = await new Promise(res => {
    const s = net.createServer();
    s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => res(p)); });
  });
  const proc = spawn('node', ['server.js'], {
    cwd, env: { ...process.env, PORT: String(port) }, stdio: 'ignore'
  });
  const base = `http://127.0.0.1:${port}`;
  for (let i = 0; i < 80; i++) {
    try { if ((await fetch(base + '/network-info')).ok) return { proc, base }; } catch {}
    await sleep(100);
  }
  proc.kill();
  throw new Error('서버가 뜨지 않았습니다');
}

export const sleep = ms => new Promise(r => setTimeout(r, ms));

export function makeReporter() {
  const r = { pass: 0, fail: 0 };
  r.ok = (name, cond, extra = '') => {
    if (cond) { r.pass++; console.log(`  PASS  ${name}`); }
    else { r.fail++; console.log(`  FAIL  ${name} ${extra}`); }
  };
  r.note = text => console.log(`    ${text}`);
  return r;
}

export function makeClient(base) {
  const post = async (path, body) => {
    const res = await fetch(base + path, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
    });
    return { status: res.status, body: await res.json() };
  };
  const get = async path => (await fetch(base + path)).json();
  // SSE 구독. 'error' 를 반드시 삼켜야 한다.
  const subscribe = id => {
    const h = { snapshot: null, closed: false };
    h.req = http.get(`${base}/events?id=${encodeURIComponent(id)}`, res => {
      let buf = '';
      res.on('data', chunk => {
        buf += chunk;
        let i;
        while ((i = buf.indexOf('\n\n')) !== -1) {
          const line = buf.slice(0, i).trim();
          buf = buf.slice(i + 2);
          if (line.startsWith('data: ')) {
            try { h.snapshot = JSON.parse(line.slice(6)); } catch { continue; }
            h.onSnapshot?.(h.snapshot);
          }
        }
      });
      res.on('error', () => {});
    });
    h.req.on('error', () => {});
    h.close = () => { h.closed = true; h.req.destroy(); };
    return h;
  };
  // 방 생성 (첫 플레이어 = 호스트)
  const createRoom = async (playerName, opts = {}) => {
    const { status, body } = await post('/rooms', {
      playerName, name: opts.name || '테스트 방',
      visibility: opts.visibility || 'public', password: opts.password
    });
    if (status !== 200) return { status, body };
    const sub = subscribe(body.id);
    return { status, id: body.id, code: body.code, sub };
  };
  // 방 참가
  const join = async (name, code, password) => {
    const { status, body } = await post('/join', { name, code, password });
    if (status !== 200) return { status, body };
    const sub = subscribe(body.id);
    return { status, id: body.id, code: body.code, sub };
  };
  const act = (id, action, extra = {}) => post('/action', { id, action, ...extra });
  return { post, get, subscribe, createRoom, join, act };
}

// 호스트가 방을 만들고 나머지가 참가한다. 봇을 끄고, 설정 패치를 적용한다.
export async function setupRoom(api, names, settings = {}) {
  const host = await api.createRoom(names[0]);
  await sleep(150);
  await api.act(host.id, 'settings', { settings: { bots: 0, ...settings } });
  await sleep(150);
  const others = [];
  for (const name of names.slice(1)) {
    others.push(await api.join(name, host.code));
    await sleep(120);
  }
  await sleep(200);
  return { host, others, all: [host, ...others], code: host.code };
}

export const find = (snap, id) => snap?.players.find(p => p.id === id);

// ── 이동 ──────────────────────────────────────────────────────────────
export function blocked(map, x, y, radius) {
  for (const w of map.walls) {
    const nx = Math.max(w.x, Math.min(x, w.x + w.w));
    const ny = Math.max(w.y, Math.min(y, w.y + w.h));
    if (Math.hypot(x - nx, y - ny) < radius) return true;
  }
  for (const s of map.stations) {
    if (Math.hypot(x - s.x, y - s.y) < s.radius + radius) return true;
  }
  // 낙사 구덩이도 걷기 경로에서 피한다
  for (const p of map.pits || []) {
    if (x >= p.x - radius && x <= p.x + p.w + radius && y >= p.y - radius && y <= p.y + p.h + radius) return true;
  }
  const b = map.bounds;
  return x < b.minX + radius || x > b.maxX - radius || y < b.minY + radius || y > b.maxY - radius;
}

export function findPath(map, radius, from, to, cell = 10) {
  const b = map.bounds;
  const cols = Math.ceil((b.maxX - b.minX) / cell), rows = Math.ceil((b.maxY - b.minY) / cell);
  const at = (cx, cy) => ({ x: b.minX + (cx + 0.5) * cell, y: b.minY + (cy + 0.5) * cell });
  const cellOf = p => [Math.max(0, Math.min(cols - 1, Math.floor((p.x - b.minX) / cell))),
                       Math.max(0, Math.min(rows - 1, Math.floor((p.y - b.minY) / cell)))];
  const passable = new Uint8Array(cols * rows);
  for (let cy = 0; cy < rows; cy++) {
    for (let cx = 0; cx < cols; cx++) {
      const pt = at(cx, cy);
      passable[cy * cols + cx] = blocked(map, pt.x, pt.y, radius + 1) ? 0 : 1;
    }
  }
  const [sx, sy] = cellOf(from);
  let [gx, gy] = cellOf(to);
  if (!passable[gy * cols + gx]) {
    let best = null, bestD = Infinity;
    for (let cy = 0; cy < rows; cy++) for (let cx = 0; cx < cols; cx++) {
      if (!passable[cy * cols + cx]) continue;
      const d = Math.hypot(cx - gx, cy - gy);
      if (d < bestD) { bestD = d; best = [cx, cy]; }
    }
    if (!best) return null;
    [gx, gy] = best;
  }
  const prev = new Int32Array(cols * rows).fill(-1);
  const seen = new Uint8Array(cols * rows);
  const queue = [sy * cols + sx];
  seen[sy * cols + sx] = 1;
  const goal = gy * cols + gx;
  for (let head = 0; head < queue.length; head++) {
    const node = queue[head];
    if (node === goal) break;
    const cx = node % cols, cy = (node - cx) / cols;
    for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
      const nx = cx + dx, ny = cy + dy;
      if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;
      const next = ny * cols + nx;
      if (seen[next] || !passable[next]) continue;
      seen[next] = 1; prev[next] = node; queue.push(next);
    }
  }
  if (!seen[goal]) return null;
  const path = [];
  for (let node = goal; node !== -1; node = prev[node]) {
    const cx = node % cols;
    path.push(at(cx, (node - cx) / cols));
  }
  path.reverse();
  path.push({ x: to.x, y: to.y });
  return path;
}

// 우회 없이 목표 방향으로 그냥 밀어붙인다. "벽에 막히는가" 를 볼 때 쓴다.
export async function pushToward(api, id, sub, tx, ty, ms = 6000) {
  const start = Date.now();
  while (Date.now() - start < ms) {
    const p = find(sub.snapshot, id);
    if (!p) return false;
    const dx = tx - p.x, dy = ty - p.y;
    const dist = Math.hypot(dx, dy);
    if (dist < 3) return true;
    const step = Math.min(dist, 7);
    await api.act(id, 'move', { x: p.x + dx / dist * step, y: p.y + dy / dist * step, angle: Math.atan2(dy, dx) });
    await sleep(70);
  }
  return false;
}

// 선분이 사각형을 지나는가. 서버의 시야 판정과 같은 slab 방식이다.
export function segmentHitsRect(x1, y1, x2, y2, r) {
  const dx = x2 - x1, dy = y2 - y1;
  let t0 = 0, t1 = 1;
  for (const [p, d, lo, hi] of [[x1, dx, r.x, r.x + r.w], [y1, dy, r.y, r.y + r.h]]) {
    if (Math.abs(d) < 1e-9) { if (p < lo || p > hi) return false; continue; }
    let a = (lo - p) / d, b = (hi - p) / d;
    if (a > b) [a, b] = [b, a];
    t0 = Math.max(t0, a); t1 = Math.min(t1, b);
    if (t0 > t1) return false;
  }
  return true;
}

export function losClear(map, x1, y1, x2, y2) {
  return !map.walls.some(w => segmentHitsRect(x1, y1, x2, y2, w));
}

async function stepToward(api, id, sub, tx, ty, ms) {
  const start = Date.now();
  while (Date.now() - start < ms) {
    const p = find(sub.snapshot, id);
    if (!p) return false;
    const dx = tx - p.x, dy = ty - p.y;
    const dist = Math.hypot(dx, dy);
    if (dist < 5) return true;
    const step = Math.min(dist, 7);
    await api.act(id, 'move', { x: p.x + dx / dist * step, y: p.y + dy / dist * step, angle: Math.atan2(dy, dx) });
    await sleep(70);
  }
  return false;
}

let configCache = null;
export async function cachedConfig(api) {
  if (!configCache) configCache = await api.get('/config');
  return configCache;
}

export async function walkTo(api, id, sub, tx, ty, ms = 20000, mapOverride = null) {
  const cfg = await cachedConfig(api);
  const map = mapOverride || cfg.maps[sub.snapshot.map];
  const here = find(sub.snapshot, id);
  if (!here) return false;
  const path = findPath(map, cfg.render.playerRadius, here, { x: tx, y: ty });
  const deadline = Date.now() + ms;
  if (path) {
    for (let i = 3; i < path.length; i += 3) {
      if (Date.now() > deadline) break;
      await stepToward(api, id, sub, path[i].x, path[i].y, Math.max(600, deadline - Date.now()));
    }
  }
  await stepToward(api, id, sub, tx, ty, Math.max(800, deadline - Date.now()));
  const p = find(sub.snapshot, id);
  return Math.hypot(tx - p.x, ty - p.y) < 12;
}

// 스테이션 상호작용 범위 안으로 들어간다.
export async function walkToStation(api, id, sub, station, ms = 20000) {
  const inset = Math.max(station.radius + 14, station.reach - 12);
  await walkTo(api, id, sub, station.x, station.y + inset, ms);
  const p = find(sub.snapshot, id);
  return Math.hypot(p.x - station.x, p.y - station.y) <= station.reach;
}

export async function waitFor(check, ms = 12000, every = 40) {
  const start = Date.now();
  while (Date.now() - start < ms) {
    const value = check();
    if (value) return value;
    await sleep(every);
  }
  return null;
}

export const waitPhase = (sub, phase, ms) => waitFor(() => sub.snapshot?.phase === phase, ms);
export const waitWheel = (sub, stateName, ms) => waitFor(() => sub.snapshot?.wheel?.state === stateName, ms);

export async function finish(reporter, proc) {
  proc.kill();
  await new Promise(r => proc.on('exit', r));
  await sleep(200);
  console.log(`\n결과: ${reporter.pass} pass / ${reporter.fail} fail`);
  process.exit(reporter.fail ? 1 : 0);
}

const canvas = document.getElementById('arena');
const ctx = canvas.getContext('2d');
const $ = id => document.getElementById(id);

const screens = {
  join: $('joinScreen'), lobby: $('lobbyScreen'), prep: $('prepHud'),
  countdown: $('countdownScreen'), battle: $('battleHud'), result: $('resultScreen')
};
const modals = { wheel: $('wheelModal'), shop: $('shopModal'), anvil: $('anvilModal'),
                 alms: $('almsModal'), board: $('boardModal') };

let myId = null;
let state = { phase: 'LOBBY', round: 1, totalRounds: 3, phaseEndsAt: null, serverNow: Date.now(),
              hostId: null, settings: {}, map: 'plaza', wheel: null, players: [], teams: null,
              goblin: null, final: null, room: null, ready: null };
let cfg = null;                       // 서버가 내보내는 규칙 전체. 클라이언트는 여기서만 읽는다.
let source = null;
let clockOffset = 0;
const keys = new Set();
let mouse = { x: innerWidth / 2, y: innerHeight / 2 };
let lastFrame = performance.now();
let lastMoveSent = 0;
let dashBlockedUntil = 0;
let toastTimer;
let openModal = null;
let chip = 100;
let almsChip = 100;
let almsTargetId = null;
let lastAnnounceSeq = 0;
let lastFeedSeq = 0;
let lastWheelSeq = 0;
let lastPullSeq = 0;
let lastEnhanceSeq = 0;
let camera = { x: 0, y: 0 };
let wheelRotation = 0;
let spinFrom = 0;
let spinSeqSeen = -1;
let lastShopSig = '', lastAnvilSig = '', lastSettingsSig = '', lastGearSig = '', lastBoardSig = '';
let tabHeld = false;
let foundRoomCode = null;

// ── 좌표 보간 ─────────────────────────────────────────────────────────
// 스냅샷은 100ms 마다 state 를 통째로 갈아치운다. 렌더 좌표를 따로 들고 간다.
//  · 내 캐릭터: 예측이 권위를 갖고, 서버와 어긋난 정도가 데드밴드를 넘을 때만 보정한다.
//  · 남의 캐릭터: 최신 서버 좌표를 목표로 두고 매 프레임 지수적으로 따라간다.
const RECONCILE_DEADBAND = 16;
const RECONCILE_SNAP = 34;
const RECONCILE_RATE = 7;
const REMOTE_RATE = 15;
const REMOTE_SNAP = 60;

let localPos = null;
let localAngle = 0;
const remotePos = new Map();

const me = () => state.players.find(p => p.id === myId);
const myPos = () => localPos || me() || { x: 0, y: 0 };

function approach(cur, target, rate, dt) {
  return cur + (target - cur) * Math.min(1, rate * dt);
}
function approachAngle(cur, target, rate, dt) {
  const diff = Math.atan2(Math.sin(target - cur), Math.cos(target - cur));
  return cur + diff * Math.min(1, rate * dt);
}

function reconcileSelf(server) {
  if (!localPos) { localPos = { x: server.x, y: server.y }; return; }
  const gap = Math.hypot(server.x - localPos.x, server.y - localPos.y);
  if (gap > RECONCILE_SNAP) { localPos.x = server.x; localPos.y = server.y; }
}

function snapAllPositions() {
  const mine = me();
  localPos = mine ? { x: mine.x, y: mine.y } : null;
  if (mine) localAngle = mine.angle;
  remotePos.clear();
  for (const p of state.players) {
    if (p.id !== myId) remotePos.set(p.id, { x: p.x, y: p.y, angle: p.angle });
  }
}

function renderPos(p) {
  if (p.id === myId) return { x: myPos().x, y: myPos().y, angle: localAngle };
  const r = remotePos.get(p.id);
  return r ? r : { x: p.x, y: p.y, angle: p.angle };
}

function advanceRemotes(dt) {
  const alive = new Set();
  for (const p of state.players) {
    if (p.id === myId) continue;
    if (p.x == null) { remotePos.delete(p.id); continue; }   // 유령 — 위치가 감춰진 사망자
    alive.add(p.id);
    let r = remotePos.get(p.id);
    if (!r) { remotePos.set(p.id, { x: p.x, y: p.y, angle: p.angle }); continue; }
    if (Math.hypot(p.x - r.x, p.y - r.y) > REMOTE_SNAP) { r.x = p.x; r.y = p.y; r.angle = p.angle; continue; }
    r.x = approach(r.x, p.x, REMOTE_RATE, dt);
    r.y = approach(r.y, p.y, REMOTE_RATE, dt);
    r.angle = approachAngle(r.angle, p.angle, REMOTE_RATE, dt);
  }
  for (const id of [...remotePos.keys()]) if (!alive.has(id)) remotePos.delete(id);
}
const serverNow = () => Date.now() + clockOffset;
const activeMap = () => cfg ? cfg.maps[state.map] : null;
const SQUASH = () => (cfg ? cfg.render.verticalSquash : 0.8);

function post(data) {
  if (!myId) return Promise.resolve();
  return fetch('/action', { method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ id: myId, ...data }) }).catch(() => {});
}

function showToast(text, tone = '') {
  const el = $('toast');
  el.textContent = text;
  el.className = `toast show ${tone}`;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => el.className = 'toast', 1900);
}

const escapeHtml = v => String(v).replace(/[&<>'"]/g, c =>
  ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[c]));
const round1 = v => Number.isInteger(v) ? v : Math.round(v * 10) / 10;
const fmt = v => (v ?? 0).toLocaleString();
const teamOf = idx => cfg && cfg.teams[idx] ? cfg.teams[idx] : { label: `팀 ${idx}`, color: '#888' };
const isTeamMode = () => state.settings.mode === 'team';

// 팀전이면 팀 색, 아니면 개인 색으로 표시한다 (색상만으로 구분하지 않고 이름을 함께 쓴다)
function displayColor(p) {
  return isTeamMode() ? teamOf(p.team).color : p.color;
}

// ── 연결 ──────────────────────────────────────────────────────────────
function connect() {
  if (source) source.close();
  source = new EventSource(`/events?id=${encodeURIComponent(myId)}`);
  source.onopen = () => $('disconnectNotice').classList.add('hidden');
  source.onerror = () => $('disconnectNotice').classList.remove('hidden');
  source.onmessage = event => {
    const next = JSON.parse(event.data);
    const prevPhase = state.phase;
    state = next;
    clockOffset = next.serverNow - Date.now();
    const mine = me();
    if (mine) reconcileSelf(mine);
    if (mine?.feed && mine.feed.seq !== lastFeedSeq) {
      lastFeedSeq = mine.feed.seq;
      showToast(mine.feed.text, mine.feed.tone);
    }
    if (mine?.lastWheel && mine.lastWheel.seq !== lastWheelSeq) {
      lastWheelSeq = mine.lastWheel.seq;
      renderWheelLog(mine.lastWheel);
    }
    if (mine?.lastPull && mine.lastPull.seq !== lastPullSeq) {
      lastPullSeq = mine.lastPull.seq;
      renderPullResult(mine.lastPull);
    }
    if (mine?.lastEnhance && mine.lastEnhance.seq !== lastEnhanceSeq) {
      lastEnhanceSeq = mine.lastEnhance.seq;
      lastAnvilSig = '';
    }
    if (prevPhase !== state.phase) onPhaseChange(prevPhase, state.phase);
    renderUI();
  };
}

function onPhaseChange(from, to) {
  snapAllPositions();
  if (to !== 'PREP') closeModal();
  if (to === 'BATTLE') showToast('FIGHT!', 'fail');
  if (to === 'PREP') showToast(`ROUND ${state.round} · 준비`, 'success');
  lastShopSig = lastAnvilSig = lastGearSig = lastBoardSig = '';
}

// ── 투영 / 카메라 ─────────────────────────────────────────────────────
function viewport() {
  return { w: innerWidth, h: innerHeight - 84, top: 84 };
}

function scaleFor(map) {
  const v = viewport();
  const mw = map.bounds.maxX - map.bounds.minX;
  const mh = map.bounds.maxY - map.bounds.minY;
  // 0.72 → 0.5: 한 화면에 담기는 월드 폭을 30% 줄여 더 가깝게 본다
  const s = Math.max(v.w / (mw * 0.5), v.h / (mh * SQUASH() * 0.5));
  return Math.max(2.3, Math.min(6, s));
}

function updateCamera(map) {
  const v = viewport(), s = scaleFor(map);
  const mine = me();
  const halfW = v.w / 2 / s, halfH = v.h / 2 / (s * SQUASH());
  const target = mine && !mine.spectating ? myPos() : { x: 0, y: 0 };
  const b = map.bounds;
  const spanX = b.maxX - b.minX, spanY = b.maxY - b.minY;
  camera.x = spanX <= halfW * 2 ? (b.minX + b.maxX) / 2
    : Math.max(b.minX + halfW, Math.min(b.maxX - halfW, target.x));
  camera.y = spanY <= halfH * 2 ? (b.minY + b.maxY) / 2
    : Math.max(b.minY + halfH, Math.min(b.maxY - halfH, target.y));
}

function project(x, y, map) {
  const v = viewport(), s = scaleFor(map);
  return {
    x: v.w / 2 + (x - camera.x) * s,
    y: v.top + v.h / 2 + (y - camera.y) * s * SQUASH(),
    s
  };
}

function unproject(sx, sy, map) {
  const v = viewport(), s = scaleFor(map);
  return {
    x: camera.x + (sx - v.w / 2) / s,
    y: camera.y + (sy - v.top - v.h / 2) / (s * SQUASH())
  };
}

// ── 지형 렌더 ─────────────────────────────────────────────────────────
function drawGround(map) {
  ctx.clearRect(0, 0, innerWidth, innerHeight);
  const a = project(map.bounds.minX, map.bounds.minY, map);
  const b = project(map.bounds.maxX, map.bounds.maxY, map);
  const g = ctx.createLinearGradient(a.x, a.y, b.x, b.y);
  if (map.key === 'plaza') { g.addColorStop(0, '#2b3358'); g.addColorStop(1, '#171c33'); }
  else { g.addColorStop(0, '#2a2f42'); g.addColorStop(1, '#14161f'); }
  ctx.fillStyle = g;
  ctx.fillRect(a.x, a.y, b.x - a.x, b.y - a.y);

  ctx.save();
  ctx.beginPath(); ctx.rect(a.x, a.y, b.x - a.x, b.y - a.y); ctx.clip();
  ctx.strokeStyle = 'rgba(255,255,255,.045)'; ctx.lineWidth = 1;
  for (let x = map.bounds.minX; x <= map.bounds.maxX; x += 40) {
    const p1 = project(x, map.bounds.minY, map), p2 = project(x, map.bounds.maxY, map);
    ctx.beginPath(); ctx.moveTo(p1.x, p1.y); ctx.lineTo(p2.x, p2.y); ctx.stroke();
  }
  for (let y = map.bounds.minY; y <= map.bounds.maxY; y += 40) {
    const p1 = project(map.bounds.minX, y, map), p2 = project(map.bounds.maxX, y, map);
    ctx.beginPath(); ctx.moveTo(p1.x, p1.y); ctx.lineTo(p2.x, p2.y); ctx.stroke();
  }
  ctx.restore();

  ctx.strokeStyle = 'rgba(110,224,255,.32)'; ctx.lineWidth = 3;
  ctx.strokeRect(a.x, a.y, b.x - a.x, b.y - a.y);

  // 낙사 구덩이 — 이동을 막지 않고 밟으면 떨어진다. 붉은 경고 테두리를 두른다.
  for (const pit of map.pits || []) {
    const tl = project(pit.x, pit.y, map);
    const br = project(pit.x + pit.w, pit.y + pit.h, map);
    const grad = ctx.createLinearGradient(tl.x, tl.y, tl.x, br.y);
    grad.addColorStop(0, '#05060c');
    grad.addColorStop(1, '#000');
    ctx.fillStyle = grad;
    ctx.fillRect(tl.x, tl.y, br.x - tl.x, br.y - tl.y);
    ctx.strokeStyle = 'rgba(255,66,103,.55)';
    ctx.lineWidth = 2;
    ctx.setLineDash([5, 5]);
    ctx.strokeRect(tl.x, tl.y, br.x - tl.x, br.y - tl.y);
    ctx.setLineDash([]);
  }
}

function drawWall(w, map) {
  const height = (w.tall ? 30 : 16);
  const tl = project(w.x, w.y, map);
  const br = project(w.x + w.w, w.y + w.h, map);
  const s = tl.s;
  const lift = height * s;
  const x = tl.x, y = tl.y, ww = br.x - tl.x, hh = br.y - tl.y;

  ctx.fillStyle = 'rgba(0,0,0,.42)';
  ctx.fillRect(x + 4, y + 6, ww, hh);
  ctx.fillStyle = w.tall ? '#333a5c' : '#2b3049';
  ctx.fillRect(x, y + hh - lift, ww, lift);
  ctx.fillStyle = w.tall ? '#4a5484' : '#3c4468';
  ctx.fillRect(x, y - lift, ww, hh);
  ctx.strokeStyle = 'rgba(255,255,255,.14)'; ctx.lineWidth = 1;
  ctx.strokeRect(x, y - lift, ww, hh);
  ctx.strokeStyle = 'rgba(0,0,0,.35)';
  ctx.strokeRect(x, y - lift + hh, ww, lift);
}

const STATION_STYLE = {
  shop: { base: '#3d5a47', top: '#66e3a4', icon: '✦' },
  anvil: { base: '#5a4038', top: '#ff8f4d', icon: '🔨' },
  board: { base: '#3a4258', top: '#9fb0d8', icon: '🏆' },
  dummy: { base: '#4c4636', top: '#c8b070', icon: '🎯' }
};

function drawStation(st, map) {
  const p = project(st.x, st.y, map);
  const s = p.s;
  const r = st.radius * s;
  const mine = me();
  const here = myPos();
  const near = st.reach > 0 && mine && Math.hypot(here.x - st.x, here.y - st.y) <= st.reach;

  if (st.reach > 0) {
    ctx.save();
    ctx.beginPath();
    ctx.ellipse(p.x, p.y, st.reach * s, st.reach * s * SQUASH(), 0, 0, Math.PI * 2);
    ctx.fillStyle = near ? 'rgba(85,220,255,.10)' : 'rgba(255,255,255,.035)';
    ctx.fill();
    ctx.strokeStyle = near ? 'rgba(85,220,255,.45)' : 'rgba(255,255,255,.10)';
    ctx.setLineDash([6, 6]); ctx.lineWidth = 1.5; ctx.stroke();
    ctx.setLineDash([]);
    ctx.restore();
  }

  if (st.kind === 'wheel') drawWorldWheel(p, r);
  else if (st.kind === 'dummy') drawDummy(st, p);
  else {
    const style = STATION_STYLE[st.kind] || STATION_STYLE.board;
    const lift = 26 * s;
    ctx.fillStyle = 'rgba(0,0,0,.45)';
    ctx.beginPath(); ctx.ellipse(p.x + 4, p.y + 6, r, r * SQUASH(), 0, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = style.base;
    ctx.beginPath(); ctx.ellipse(p.x, p.y, r, r * SQUASH(), 0, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = style.top;
    ctx.beginPath(); ctx.ellipse(p.x, p.y - lift * .5, r * .78, r * .78 * SQUASH(), 0, 0, Math.PI * 2); ctx.fill();
    ctx.font = `${Math.max(14, 18 * s / 3)}px 'Noto Sans KR'`;
    ctx.textAlign = 'center'; ctx.fillStyle = '#0b0d16';
    ctx.fillText(style.icon, p.x, p.y - lift * .5 + 6);
  }

  ctx.font = `800 ${Math.max(11, 13 * s / 3)}px 'Noto Sans KR'`;
  ctx.textAlign = 'center';
  ctx.fillStyle = 'rgba(7,8,15,.8)';
  const label = `${st.label}`;
  const labelR = st.kind === 'dummy' ? 16 : st.radius;   // 허수아비는 말뚝 높이만큼 띄운다
  const tw = ctx.measureText(label).width + 16;
  ctx.fillRect(p.x - tw / 2, p.y - labelR * s * SQUASH() - 46 * s / 3, tw, 20);
  ctx.fillStyle = near ? '#55dcff' : '#e8edf7';
  ctx.fillText(label, p.x, p.y - labelR * s * SQUASH() - 46 * s / 3 + 14);
}

// 허수아비 — 얇은 말뚝 + 십자 팔 + 짚 머리. 맞으면 감쇠 진동으로 흔들려서
// "때렸는지" 가 전원에게 보인다 (서버가 dummyShake 에 피격 시각을 브로드캐스트한다).
const DUMMY_SHAKE_MS = 550;

function drawDummy(st, p) {
  const s = p.s / 3;
  const shakeAt = (state.dummyShake || {})[st.key] || 0;
  const elapsed = serverNow() - shakeAt;
  let tilt = 0;
  if (elapsed >= 0 && elapsed < DUMMY_SHAKE_MS) {
    const t = elapsed / DUMMY_SHAKE_MS;
    tilt = Math.sin(t * Math.PI * 5) * (1 - t) * 0.35;   // 감쇠 진동
  }

  // 바닥 그림자
  ctx.fillStyle = 'rgba(0,0,0,.45)';
  ctx.beginPath(); ctx.ellipse(p.x + 2 * s, p.y + 3 * s, 9 * s, 4.5 * s, 0, 0, Math.PI * 2); ctx.fill();

  ctx.save();
  ctx.translate(p.x, p.y);
  ctx.rotate(tilt);                                       // 발밑을 축으로 기울어진다
  ctx.lineCap = 'round';
  // 말뚝 (얇다)
  ctx.strokeStyle = '#8a6b3f'; ctx.lineWidth = 3.5 * s;
  ctx.beginPath(); ctx.moveTo(0, 0); ctx.lineTo(0, -34 * s); ctx.stroke();
  // 십자 팔
  ctx.strokeStyle = '#7a5c33'; ctx.lineWidth = 3 * s;
  ctx.beginPath(); ctx.moveTo(-13 * s, -25 * s); ctx.lineTo(13 * s, -25 * s); ctx.stroke();
  // 짚 머리
  ctx.fillStyle = '#d8b05a';
  ctx.beginPath(); ctx.ellipse(0, -38 * s, 6.5 * s, 6.5 * s, 0, 0, Math.PI * 2); ctx.fill();
  ctx.strokeStyle = '#a8842f'; ctx.lineWidth = 1.2 * s; ctx.stroke();
  // 피격 순간 하이라이트
  if (tilt !== 0) {
    ctx.strokeStyle = 'rgba(255,210,87,.85)'; ctx.lineWidth = 2 * s;
    ctx.beginPath(); ctx.ellipse(0, -25 * s, 17 * s, 20 * s, 0, 0, Math.PI * 2); ctx.stroke();
  }
  ctx.restore();
}

// 룰렛. 46칸 배치가 곧 확률표이므로 서버가 준 layout 을 그대로 그린다.
const WHEEL_COLORS = { 1: '#4a5484', 3: '#3f8f6b', 5: '#3f6f9f', 10: '#a5761f', 20: '#a33450' };

function drawWheelFace(c, cx, cy, radius, rotation, squash, layout, opts = {}) {
  const n = layout.length;
  c.save();
  c.translate(cx, cy);
  c.scale(1, squash);
  c.rotate(rotation);
  for (let i = 0; i < n; i++) {
    const a0 = i * Math.PI * 2 / n, a1 = (i + 1) * Math.PI * 2 / n;
    c.beginPath();
    c.moveTo(0, 0);
    c.arc(0, 0, radius, a0, a1);
    c.closePath();
    c.fillStyle = WHEEL_COLORS[layout[i]] || '#444';
    c.fill();
    c.strokeStyle = 'rgba(0,0,0,.35)'; c.lineWidth = 1; c.stroke();
    if (opts.labels) {
      c.save();
      c.rotate((a0 + a1) / 2);
      c.textAlign = 'right'; c.textBaseline = 'middle';
      c.fillStyle = '#f4f6ff';
      c.font = `800 ${Math.max(9, radius * .11)}px 'Noto Sans KR'`;
      c.fillText(String(layout[i]), radius - 6, 0);
      c.restore();
    }
  }
  c.restore();
  c.save();
  c.translate(cx, cy); c.scale(1, squash);
  c.beginPath(); c.arc(0, 0, radius * .22, 0, Math.PI * 2);
  c.fillStyle = '#12172a'; c.fill();
  c.strokeStyle = 'rgba(255,255,255,.25)'; c.lineWidth = 2; c.stroke();
  c.restore();
  c.beginPath();
  c.moveTo(cx, cy - radius * squash - 2);
  c.lineTo(cx - radius * .07, cy - radius * squash - radius * .2);
  c.lineTo(cx + radius * .07, cy - radius * squash - radius * .2);
  c.closePath();
  c.fillStyle = '#ffd257'; c.fill();
  c.strokeStyle = '#0b0d16'; c.lineWidth = 1.5; c.stroke();
}

function drawWorldWheel(p, r) {
  if (!cfg) return;
  const s = p.s;
  ctx.fillStyle = 'rgba(0,0,0,.5)';
  ctx.beginPath(); ctx.ellipse(p.x + 5, p.y + 8, r * 1.12, r * 1.12 * SQUASH(), 0, 0, Math.PI * 2); ctx.fill();
  ctx.fillStyle = '#1b2138';
  ctx.beginPath(); ctx.ellipse(p.x, p.y, r * 1.12, r * 1.12 * SQUASH(), 0, 0, Math.PI * 2); ctx.fill();
  drawWheelFace(ctx, p.x, p.y, r, wheelRotation, SQUASH(), cfg.roulette.layout);
  const w = state.wheel;
  if (w && w.state === 'RESULT' && w.number != null) {
    ctx.font = `900 ${Math.max(20, 26 * s / 3)}px 'Black Han Sans'`;
    ctx.textAlign = 'center';
    ctx.fillStyle = '#ffd257';
    ctx.fillText(String(w.number), p.x, p.y - r * SQUASH() - 30 * s / 3);
  }
}

function drawStorm(map) {
  const st = state.storm;
  if (!st) return;
  const c = project(st.cx, st.cy, map);
  const rx = st.radius * c.s, ry = st.radius * c.s * SQUASH();
  const a = project(map.bounds.minX, map.bounds.minY, map);
  const b = project(map.bounds.maxX, map.bounds.maxY, map);

  ctx.save();
  ctx.beginPath();
  ctx.rect(a.x, a.y, b.x - a.x, b.y - a.y);
  ctx.ellipse(c.x, c.y, rx, ry, 0, 0, Math.PI * 2, true);
  ctx.fillStyle = 'rgba(150,30,80,.30)';
  ctx.fill();
  ctx.restore();

  ctx.save();
  ctx.beginPath();
  ctx.ellipse(c.x, c.y, rx, ry, 0, 0, Math.PI * 2);
  ctx.strokeStyle = st.closing ? 'rgba(255,110,150,.9)' : 'rgba(120,200,255,.55)';
  ctx.lineWidth = 2.5;
  ctx.setLineDash(st.closing ? [] : [10, 8]);
  ctx.stroke();
  ctx.setLineDash([]);
  ctx.restore();
}

function drawDrop(d, map) {
  const p = project(d.x, d.y, map);
  const s = p.s;
  const r = (cfg ? cfg.drops.radius : 13) * s;
  const bob = Math.sin(serverNow() / 320 + d.id) * 2 * s;
  ctx.save();
  ctx.fillStyle = 'rgba(0,0,0,.42)';
  ctx.beginPath(); ctx.ellipse(p.x, p.y + 4 * s, r * .8, r * .45, 0, 0, Math.PI * 2); ctx.fill();
  const isMoney = d.kind === 'money';
  ctx.fillStyle = isMoney ? '#c9962c' : '#3fbe86';
  ctx.beginPath();
  ctx.roundRect(p.x - r * .62, p.y - r * 1.1 + bob, r * 1.24, r * 1.1, 3 * s);
  ctx.fill();
  ctx.strokeStyle = isMoney ? '#ffd257' : '#8affc9';
  ctx.lineWidth = 1.6 * s;
  ctx.stroke();
  ctx.fillStyle = isMoney ? '#3a2a06' : '#04301d';
  ctx.font = `800 ${Math.max(9, 10 * s)}px 'Noto Sans KR'`;
  ctx.textAlign = 'center';
  ctx.fillText(isMoney ? 'S' : '+', p.x, p.y - r * .38 + bob);
  ctx.restore();
}

function drawPlayer(p, map, at) {
  const pos = project(at.x, at.y, map);
  const s = pos.s / 3;
  const now = serverNow();
  const isGoblin = state.goblin && state.goblin.id === p.id && ['COUNTDOWN', 'BATTLE'].includes(state.phase);
  const isGhost = state.phase === 'BATTLE' && !p.alive && !p.spectating;
  ctx.save();
  ctx.globalAlpha = p.alive ? 1 : isGhost ? .4 : .2;

  ctx.fillStyle = 'rgba(0,0,0,.45)';
  ctx.beginPath(); ctx.ellipse(pos.x, pos.y + 6 * s, 13 * s, 6 * s, 0, 0, Math.PI * 2); ctx.fill();

  // 팀전이면 발밑에 팀 색 링을 두른다
  if (isTeamMode() && !p.spectating) {
    ctx.strokeStyle = teamOf(p.team).color;
    ctx.lineWidth = 2.2 * s;
    ctx.beginPath(); ctx.ellipse(pos.x, pos.y + 3 * s, 15 * s, 7.5 * s, 0, 0, Math.PI * 2); ctx.stroke();
  }

  if (now < (p.invulnUntil || 0)) {
    ctx.save(); ctx.globalAlpha = .85;
    ctx.strokeStyle = '#55dcff'; ctx.lineWidth = 2.5 * s;
    ctx.beginPath(); ctx.ellipse(pos.x, pos.y - 12 * s, 21 * s, 16 * s, 0, 0, Math.PI * 2); ctx.stroke();
    ctx.restore();
  }

  // 몸
  ctx.fillStyle = p.color;
  ctx.beginPath();
  ctx.roundRect(pos.x - 9 * s, pos.y - 28 * s, 18 * s, 29 * s, 8 * s);
  ctx.fill();
  ctx.beginPath(); ctx.ellipse(pos.x, pos.y - 32 * s, 9 * s, 9 * s, 0, 0, Math.PI * 2); ctx.fill();

  // 황금 고블린 — 왕관. 전투 단계에서 현상금 대상이 한눈에 보이게 한다.
  if (isGoblin) {
    ctx.fillStyle = '#ffd257';
    ctx.beginPath();
    const cy = pos.y - 44 * s;
    ctx.moveTo(pos.x - 8 * s, cy + 6 * s);
    ctx.lineTo(pos.x - 8 * s, cy);
    ctx.lineTo(pos.x - 4 * s, cy + 3.5 * s);
    ctx.lineTo(pos.x, cy - 2 * s);
    ctx.lineTo(pos.x + 4 * s, cy + 3.5 * s);
    ctx.lineTo(pos.x + 8 * s, cy);
    ctx.lineTo(pos.x + 8 * s, cy + 6 * s);
    ctx.closePath();
    ctx.fill();
    ctx.strokeStyle = '#7a5c00'; ctx.lineWidth = 1 * s; ctx.stroke();
  }

  // 무기 — 길이와 스윙 이펙트를 서버가 공개한 실제 사거리(p.range)에서만 가져온다.
  //  · 준비 라운드: 남의 range 는 서버가 감추므로(null) 전원 기본 사거리로 그려진다
  //    → 검 크기로 남의 전투력을 읽을 수 없다. (내 강화는 HUD 에서만 보인다)
  //  · 스윙 호는 판정과 동일하게 발밑 기준 · 실제 사거리 · 실제 공격각(±attackArc)으로
  //    y 압축을 적용해 그린다 — 이펙트에 닿으면 실제로 맞는 거리다.
  const lv = p.enhance?.weapon ?? 0;
  const baseRange = cfg ? cfg.gear.weapon.base.range : 26;
  const rangeWorld = p.range ?? baseRange;
  const arcHalf = cfg ? cfg.combat.attackArc : 0.85;
  // 검과 스윙 호는 같은 축척(사거리의 절반)·같은 원점에서 그린다.
  //  · 호 반지름 = 검 길이 — 사거리가 길어지면 검과 호가 함께 길어지고 호도 넓어진다
  //  · 호 각도는 항상 ±attackArc 로 동일하다
  //  · y 압축을 적용하지 않아 상/하/좌/우 어느 방향이든 화면상 같은 길이로 보인다
  const bladeLen = rangeWorld * 0.5 * pos.s;
  const originY = pos.y - 14 * s;                       // 손 높이 — 검과 호의 공통 원점
  const tipX = pos.x + Math.cos(at.angle) * bladeLen;
  const tipY = originY + Math.sin(at.angle) * bladeLen;
  if (now - p.attackAt < 170) {
    ctx.save();
    ctx.globalAlpha = .55;
    ctx.strokeStyle = p.color; ctx.lineWidth = 4 * s;
    ctx.beginPath();
    ctx.arc(pos.x, originY, bladeLen, at.angle - arcHalf, at.angle + arcHalf);
    ctx.stroke();
    ctx.restore();
  }
  ctx.strokeStyle = '#e8edf7';
  ctx.lineWidth = (3.5 + (state.phase === 'PREP' && p.id !== myId ? 0 : lv) * .55) * s;
  ctx.lineCap = 'round';
  ctx.beginPath(); ctx.moveTo(pos.x, pos.y - 14 * s); ctx.lineTo(tipX, tipY); ctx.stroke();

  if (now - p.hitAt < 220) {
    ctx.strokeStyle = '#ff4267'; ctx.lineWidth = 3 * s;
    ctx.beginPath(); ctx.ellipse(pos.x, pos.y - 14 * s, 18 * s, 15 * s, 0, 0, Math.PI * 2); ctx.stroke();
  }

  // 이름 + HP
  const label = (isGhost ? '👻 ' : '') + (p.bot ? '🤖 ' : '') + p.name + (p.id === myId ? ' · 나' : '');
  ctx.font = `700 ${Math.max(10, 11 * s)}px 'Noto Sans KR'`;
  ctx.textAlign = 'center';
  const tw = ctx.measureText(label).width + 14;
  ctx.fillStyle = 'rgba(7,8,15,.82)';
  ctx.fillRect(pos.x - tw / 2, pos.y - 54 * s, tw, 15 * s);
  ctx.fillStyle = p.id === myId ? '#ffd257' : (isGoblin ? '#ffd257' : '#fff');
  ctx.fillText(label, pos.x, pos.y - 43 * s);

  if ((p.begUntil || 0) > now) {
    const bubbleY = pos.y - 66 * s;
    ctx.font = `800 ${Math.max(10, 11 * s)}px 'Noto Sans KR'`;
    ctx.textAlign = 'center';
    const text = '적선 좀…';
    const bw2 = ctx.measureText(text).width + 18;
    ctx.fillStyle = 'rgba(255,210,87,.94)';
    ctx.beginPath();
    ctx.roundRect(pos.x - bw2 / 2, bubbleY - 13 * s, bw2, 18 * s, 7 * s);
    ctx.fill();
    ctx.beginPath();
    ctx.moveTo(pos.x - 4 * s, bubbleY + 5 * s);
    ctx.lineTo(pos.x + 4 * s, bubbleY + 5 * s);
    ctx.lineTo(pos.x, bubbleY + 11 * s);
    ctx.closePath(); ctx.fill();
    ctx.fillStyle = '#2a1d02';
    ctx.fillText(text, pos.x, bubbleY + 1 * s);
  }

  if (p.maxHp) {
    const bw = 34 * s;
    ctx.fillStyle = '#292b3d'; ctx.fillRect(pos.x - bw / 2, pos.y + 4 * s, bw, 3.5 * s);
    ctx.fillStyle = '#ff4267';
    ctx.fillRect(pos.x - bw / 2, pos.y + 4 * s, bw * Math.max(0, p.hp / p.maxHp), 3.5 * s);
  }
  ctx.restore();
}

// ── 미니맵 ────────────────────────────────────────────────────────────
// 전투 중 우하단. 전체 맵은 어둡게, 내 위치에서 시야가 닿는 곳(벽에 안 가린 곳)만
// 밝게 칠한다. 플레이어는 나와 팀원만 표시한다 — 적 위치는 절대 그리지 않는다.
const MM_CELL = 10;              // 가시영역 격자 (월드 단위)
let mmVis = null;                // Uint8Array — 셀별 시야 여부
let mmVisAt = 0;
let mmVisFrom = { x: 1e9, y: 1e9 };

// 서버와 같은 slab 판정 — 선분이 사각형을 지나는가
function mmSegHitsRect(x1, y1, x2, y2, r) {
  const dx = x2 - x1, dy = y2 - y1;
  let t0 = 0, t1 = 1;
  for (const [p0, d, lo, hi] of [[x1, dx, r.x, r.x + r.w], [y1, dy, r.y, r.y + r.h]]) {
    if (Math.abs(d) < 1e-9) { if (p0 < lo || p0 > hi) return false; continue; }
    let a = (lo - p0) / d, b = (hi - p0) / d;
    if (a > b) [a, b] = [b, a];
    t0 = Math.max(t0, a); t1 = Math.min(t1, b);
    if (t0 > t1) return false;
  }
  return true;
}

function mmRecomputeVisibility(map, fromX, fromY) {
  const b = map.bounds;
  const cols = Math.ceil((b.maxX - b.minX) / MM_CELL);
  const rows = Math.ceil((b.maxY - b.minY) / MM_CELL);
  if (!mmVis || mmVis.length !== cols * rows) mmVis = new Uint8Array(cols * rows);
  for (let cy = 0; cy < rows; cy++) {
    for (let cx = 0; cx < cols; cx++) {
      const wx = b.minX + (cx + 0.5) * MM_CELL;
      const wy = b.minY + (cy + 0.5) * MM_CELL;
      let seen = 1;
      for (const w of map.walls) {
        if (mmSegHitsRect(fromX, fromY, wx, wy, w)) { seen = 0; break; }
      }
      mmVis[cy * cols + cx] = seen;
    }
  }
  return { cols, rows };
}

function drawMinimap() {
  const mm = $('minimap');
  if (state.phase !== 'BATTLE' || !cfg) { mm.classList.add('hidden'); return; }
  const meP = me();
  if (!meP || meP.spectating) { mm.classList.add('hidden'); return; }
  mm.classList.remove('hidden');

  const map = cfg.maps.battlefield;
  const b = map.bounds;
  const c = mm.getContext('2d');
  const sx = mm.width / (b.maxX - b.minX);
  const sy = mm.height / (b.maxY - b.minY);
  const toX = wx => (wx - b.minX) * sx;
  const toY = wy => (wy - b.minY) * sy;

  // 전체를 어둡게
  c.clearRect(0, 0, mm.width, mm.height);
  c.fillStyle = '#07080f';
  c.fillRect(0, 0, mm.width, mm.height);

  // 가시영역 — 250ms 마다, 또는 8 이상 움직였을 때 다시 계산한다
  const at = myPos();
  const now = performance.now();
  if (now - mmVisAt > 250 || Math.hypot(at.x - mmVisFrom.x, at.y - mmVisFrom.y) > 8) {
    mmVisAt = now;
    mmVisFrom = { x: at.x, y: at.y };
    mmRecomputeVisibility(map, at.x, at.y);
  }
  if (mmVis) {
    const cols = Math.ceil((b.maxX - b.minX) / MM_CELL);
    const rows = Math.ceil((b.maxY - b.minY) / MM_CELL);
    c.fillStyle = 'rgba(120,170,230,.20)';
    const cw = MM_CELL * sx, ch = MM_CELL * sy;
    for (let cy = 0; cy < rows; cy++) {
      for (let cx = 0; cx < cols; cx++) {
        if (mmVis[cy * cols + cx]) c.fillRect(cx * cw, cy * ch, cw + .5, ch + .5);
      }
    }
  }

  // 벽 · 구덩이
  for (const w of map.walls) {
    c.fillStyle = w.tall ? '#3a4266' : '#2b3049';
    c.fillRect(toX(w.x), toY(w.y), w.w * sx, w.h * sy);
  }
  c.strokeStyle = 'rgba(255,66,103,.7)';
  c.lineWidth = 1;
  for (const t of map.pits || []) c.strokeRect(toX(t.x), toY(t.y), t.w * sx, t.h * sy);

  // 자기장 안전 구역 경계 (공개 정보)
  const st = state.storm;
  if (st) {
    c.strokeStyle = st.closing ? 'rgba(255,110,150,.8)' : 'rgba(120,200,255,.5)';
    c.beginPath();
    c.ellipse(toX(st.cx), toY(st.cy), st.radius * sx, st.radius * sy, 0, 0, Math.PI * 2);
    c.stroke();
  }

  // 플레이어 — 나와 팀원만. 적 위치는 그리지 않는다.
  for (const p of state.players) {
    if (p.spectating || p.x == null || !p.alive) continue;
    const self = p.id === myId;
    if (!self && !(isTeamMode() && p.team === meP.team)) continue;
    const rp = renderPos(p);
    const px = toX(rp.x), py = toY(rp.y);
    c.fillStyle = self ? '#ffd257' : teamOf(p.team).color;
    c.beginPath(); c.arc(px, py, self ? 3.5 : 3, 0, Math.PI * 2); c.fill();
    if (self) {                               // 내 조준 방향 표시
      c.strokeStyle = '#ffd257'; c.lineWidth = 1.5;
      c.beginPath(); c.moveTo(px, py);
      c.lineTo(px + Math.cos(rp.angle) * 7, py + Math.sin(rp.angle) * 7);
      c.stroke();
    }
  }

  // 테두리
  c.strokeStyle = 'rgba(110,224,255,.3)';
  c.strokeRect(0.5, 0.5, mm.width - 1, mm.height - 1);
}

// ── 전장 안개 (시야 제한) ─────────────────────────────────────────────
// 미니맵과 같은 가시영역 격자(mmVis)를 본 화면에도 그대로 적용한다.
// 시야 밖은 어둡게 덮고, 그 안의 적·보급은 아예 그리지 않는다.
// 나와 팀원은 항상 보인다. 죽어서 유령이 되면 시야 제한이 풀린다.
function fogActive() {
  const p = me();
  return state.phase === 'BATTLE' && !!mmVis && p && p.alive && !p.spectating;
}

function fogVisibleAt(map, wx, wy) {
  if (!mmVis) return true;
  const b = map.bounds;
  const cols = Math.ceil((b.maxX - b.minX) / MM_CELL);
  const rows = Math.ceil((b.maxY - b.minY) / MM_CELL);
  const cx = Math.floor((wx - b.minX) / MM_CELL);
  const cy = Math.floor((wy - b.minY) / MM_CELL);
  if (cx < 0 || cy < 0 || cx >= cols || cy >= rows) return true;
  return !!mmVis[cy * cols + cx];
}

function drawFog(map) {
  const b = map.bounds;
  const cols = Math.ceil((b.maxX - b.minX) / MM_CELL);
  const rows = Math.ceil((b.maxY - b.minY) / MM_CELL);
  ctx.save();
  ctx.fillStyle = 'rgba(4,5,10,.78)';
  // 한 행에서 연속된 어두운 셀을 하나의 사각형으로 합쳐 그린다
  for (let cy = 0; cy < rows; cy++) {
    let run = -1;
    for (let cx = 0; cx <= cols; cx++) {
      const dark = cx < cols && !mmVis[cy * cols + cx];
      if (dark && run < 0) run = cx;
      if (!dark && run >= 0) {
        const p1 = project(b.minX + run * MM_CELL, b.minY + cy * MM_CELL, map);
        const p2 = project(b.minX + cx * MM_CELL, b.minY + (cy + 1) * MM_CELL, map);
        ctx.fillRect(p1.x, p1.y, p2.x - p1.x, p2.y - p1.y);
        run = -1;
      }
    }
  }
  ctx.restore();
}

function draw() {
  const map = activeMap();
  if (!map) { ctx.clearRect(0, 0, innerWidth, innerHeight); return; }
  updateCamera(map);
  drawGround(map);

  drawStorm(map);
  const fog = fogActive();
  const meP = me();
  const allyOf = p => p.id === myId || (isTeamMode() && meP && p.team === meP.team);
  const items = [];
  for (const d of (state.drops || [])) {
    if (fog && !fogVisibleAt(map, d.x, d.y)) continue;      // 시야 밖 보급은 안 보인다
    items.push({ y: d.y, draw: () => drawDrop(d, map) });
  }
  for (const w of map.walls) items.push({ y: w.y + w.h, draw: () => drawWall(w, map) });
  for (const st of map.stations) items.push({ y: st.y + st.radius, draw: () => drawStation(st, map) });
  for (const p of state.players) {
    if (p.spectating || p.x == null) continue;   // 위치가 감춰진 유령은 그리지 않는다
    const at = renderPos(p);
    if (at.x == null) continue;
    if (fog && !allyOf(p) && !fogVisibleAt(map, at.x, at.y)) continue;  // 시야 밖 적
    items.push({ y: at.y, draw: () => drawPlayer(p, map, at) });
  }
  items.sort((a, b) => a.y - b.y).forEach(i => i.draw());
  if (fog) drawFog(map);
}

// ── 입력 ──────────────────────────────────────────────────────────────
function movementLocked() {
  return !!openModal || !['PREP', 'BATTLE'].includes(state.phase);
}

function dashDirection(p) {
  const dx = (keys.has('KeyD') ? 1 : 0) - (keys.has('KeyA') ? 1 : 0);
  const dy = (keys.has('KeyS') ? 1 : 0) - (keys.has('KeyW') ? 1 : 0);
  return (dx || dy) ? Math.atan2(dy, dx) : localAngle;
}

function nearestStation() {
  const map = activeMap();
  const mine = me();
  if (!map || !mine || state.phase !== 'PREP' || mine.spectating) return null;
  const here = myPos();
  let best = null, bestDist = Infinity;
  for (const st of map.stations) {
    if (!st.reach) continue;
    const d = Math.hypot(here.x - st.x, here.y - st.y);
    if (d <= st.reach && d < bestDist) { best = st; bestDist = d; }
  }
  return best;
}

// 벽 충돌 해소. 서버의 resolveWalls 와 같은 규칙이다.
function resolveWalls(map, x, y, radius) {
  for (let pass = 0; pass < 3; pass++) {
    let moved = false;
    for (const w of map.walls) {
      const nx = Math.max(w.x, Math.min(x, w.x + w.w));
      const ny = Math.max(w.y, Math.min(y, w.y + w.h));
      const dx = x - nx, dy = y - ny, dist = Math.hypot(dx, dy);
      if (dist >= radius) continue;
      if (dist > 1e-6) { x = nx + dx / dist * radius; y = ny + dy / dist * radius; }
      else {
        const left = x - w.x, right = w.x + w.w - x, top = y - w.y, bottom = w.y + w.h - y;
        const min = Math.min(left, right, top, bottom);
        if (min === left) x = w.x - radius;
        else if (min === right) x = w.x + w.w + radius;
        else if (min === top) y = w.y - radius;
        else y = w.y + w.h + radius;
      }
      moved = true;
    }
    for (const s of map.stations) {
      const dx = x - s.x, dy = y - s.y, dist = Math.hypot(dx, dy), min = s.radius + radius;
      if (dist >= min) continue;
      if (dist > 1e-6) { x = s.x + dx / dist * min; y = s.y + dy / dist * min; }
      else x = s.x + min;
      moved = true;
    }
    if (!moved) break;
  }
  const b = map.bounds;
  return {
    x: Math.max(b.minX + radius, Math.min(b.maxX - radius, x)),
    y: Math.max(b.minY + radius, Math.min(b.maxY - radius, y))
  };
}

function nearestPlayer() {
  const mine = me();
  if (!mine || state.phase !== 'PREP' || mine.spectating) return null;
  const reach = cfg ? cfg.charity.reach : 46;
  const here = myPos();
  let best = null, bestDist = Infinity;
  for (const p of state.players) {
    if (p.id === myId || p.spectating) continue;
    const at = renderPos(p);
    const d = Math.hypot(here.x - at.x, here.y - at.y);
    if (d <= reach && d < bestDist) { best = p; bestDist = d; }
  }
  return best;
}

function predictMove(p, dt, map, ghost = false) {
  const dx = (keys.has('KeyD') ? 1 : 0) - (keys.has('KeyA') ? 1 : 0);
  const dy = (keys.has('KeyS') ? 1 : 0) - (keys.has('KeyW') ? 1 : 0);
  if (!dx && !dy) return false;
  const len = Math.hypot(dx, dy);
  const speed = p.speed || 46;
  const radius = cfg ? cfg.render.playerRadius : 9;
  const nx = localPos.x + dx / len * speed * dt;
  const ny = localPos.y + dy / len * speed * dt;
  if (ghost) {
    // 서버와 같은 규칙: 유령은 벽을 통과하고 경계만 지킨다
    const b = map.bounds;
    localPos.x = Math.max(b.minX + radius, Math.min(b.maxX - radius, nx));
    localPos.y = Math.max(b.minY + radius, Math.min(b.maxY - radius, ny));
    return true;
  }
  const fixed = resolveWalls(map, nx, ny, radius);
  localPos.x = fixed.x; localPos.y = fixed.y;
  return true;
}

function predictDash(p, dir, map) {
  const dist = p.dashDist || 0;
  if (!dist || !localPos) return;
  const radius = cfg ? cfg.render.playerRadius : 9;
  const steps = 8;
  let x = localPos.x, y = localPos.y;
  for (let i = 0; i < steps; i++) {
    const fixed = resolveWalls(map, x + Math.cos(dir) * dist / steps, y + Math.sin(dir) * dist / steps, radius);
    if (Math.hypot(fixed.x - x, fixed.y - y) < dist / steps * 0.5) break;
    x = fixed.x; y = fixed.y;
  }
  localPos.x = x; localPos.y = y;
}

addEventListener('keydown', e => {
  if (e.target instanceof HTMLInputElement) return;
  if (e.code === 'Escape' && openModal) { e.preventDefault(); closeModal(); return; }
  keys.add(e.code);
  if (e.code === 'Tab') {
    e.preventDefault();
    if (state.phase === 'BATTLE') { tabHeld = true; renderTabBoard(); }
    return;
  }
  if (e.code === 'KeyE') {
    e.preventDefault();
    if (openModal) { closeModal(); return; }
    const st = nearestStation();
    if (st) showModal(st.kind);
    return;
  }
  if (e.code === 'KeyQ') {
    e.preventDefault();
    if (state.phase === 'PREP' && !openModal) post({ action: 'beg' });
    return;
  }
  if (e.code === 'KeyF') {
    e.preventDefault();
    if (openModal) { closeModal(); return; }
    const target = nearestPlayer();
    if (target) openAlms(target.id);
    return;
  }
  if (e.code === 'Space') {
    e.preventDefault();
    if (movementLocked()) return;
    const p = me();
    if (!p || !p.alive || p.spectating) return;
    if ((p.dashCharges ?? 0) <= 0) return;
    if (performance.now() < dashBlockedUntil) return;
    dashBlockedUntil = performance.now() + 120;
    const dir = dashDirection(p);
    predictDash(p, dir, activeMap());
    lastMoveSent = performance.now();
    post({ action: 'dash', dir });
  }
});
addEventListener('keyup', e => {
  keys.delete(e.code);
  if (e.code === 'Tab') { tabHeld = false; $('tabBoard').classList.add('hidden'); }
});
addEventListener('blur', () => { keys.clear(); tabHeld = false; $('tabBoard').classList.add('hidden'); });
addEventListener('mousemove', e => mouse = { x: e.clientX, y: e.clientY });
addEventListener('mousedown', e => {
  if (openModal || e.target.closest('.screen, .modal, .prep-hud, .battle-hud, .topbar, .tab-board')) return;
  const p = me();
  if (!p || !p.alive || p.spectating) return;
  if (state.phase !== 'BATTLE' && state.phase !== 'PREP') return;
  if (serverNow() < (p.attackReadyAt || 0)) return;
  post({ action: 'attack' });   // 준비 페이즈에는 허수아비 근처에서만 반응한다 (서버 판정)
});

// ── 프레임 루프 ───────────────────────────────────────────────────────
function updateWheelRotation(now) {
  const w = state.wheel;
  if (!cfg || !w) return;
  const n = cfg.roulette.layout.length;
  if (w.spinSeq !== spinSeqSeen && w.state === 'SPINNING') {
    spinSeqSeen = w.spinSeq;
    spinFrom = wheelRotation % (Math.PI * 2);
  }
  if (w.state === 'SPINNING' && w.slot != null) {
    const spinMs = cfg.roulette.spinMs;
    const start = w.endsAt - spinMs;
    const t = Math.max(0, Math.min(1, (serverNow() - start) / spinMs));
    const ease = 1 - Math.pow(1 - t, 3);
    const target = -Math.PI / 2 - (w.slot + 0.5) * Math.PI * 2 / n;
    const turns = Math.PI * 2 * 5;
    let delta = target - spinFrom;
    while (delta < 0) delta += Math.PI * 2;
    wheelRotation = spinFrom + ease * (turns + delta);
  } else if (w.state === 'RESULT' && w.slot != null) {
    wheelRotation = -Math.PI / 2 - (w.slot + 0.5) * Math.PI * 2 / n;
  } else {
    wheelRotation += 0.0004 * 16;
  }
}

function update(now) {
  const dt = Math.min(.04, (now - lastFrame) / 1000);
  lastFrame = now;
  const map = activeMap();
  const p = me();

  if (p && map && localPos) {
    // 전투 중 사망자는 유령으로 계속 움직일 수 있다 (벽·구덩이 통과)
    const ghostMode = state.phase === 'BATTLE' && !p.alive && !p.spectating;
    if (!movementLocked() && (p.alive || ghostMode) && !p.spectating) {
      predictMove(p, dt, map, ghostMode);
      const w = unproject(mouse.x, mouse.y, map);
      localAngle = Math.atan2(w.y - localPos.y, w.x - localPos.x);
      const gap = Math.hypot(p.x - localPos.x, p.y - localPos.y);
      if (gap > RECONCILE_DEADBAND) {
        localPos.x = approach(localPos.x, p.x, RECONCILE_RATE, dt);
        localPos.y = approach(localPos.y, p.y, RECONCILE_RATE, dt);
      }
      if (now - lastMoveSent > 55) {
        post({ action: 'move', x: localPos.x, y: localPos.y, angle: localAngle });
        lastMoveSent = now;
      }
    } else {
      localPos.x = approach(localPos.x, p.x, REMOTE_RATE, dt);
      localPos.y = approach(localPos.y, p.y, REMOTE_RATE, dt);
      localAngle = approachAngle(localAngle, p.angle, REMOTE_RATE, dt);
    }
  }
  advanceRemotes(dt);

  updateWheelRotation(now);
  updateTimer();
  if (state.phase === 'PREP') { updatePrepHud(); if (openModal === 'wheel') updateWheelModal(); }
  if (state.phase === 'BATTLE') { updateBattleHud(); if (tabHeld) renderTabBoard(); }
  drawMinimap();
  draw();
  requestAnimationFrame(update);
}

function remainingSeconds() {
  if (!state.phaseEndsAt) return null;
  return Math.max(0, (state.phaseEndsAt - serverNow()) / 1000);
}

function updateTimer() {
  const r = remainingSeconds();
  if (state.phase === 'LOBBY' || r === null) { $('timer').textContent = '--:--'; return; }
  const total = Math.ceil(r);
  $('timer').textContent = `${String(Math.floor(total / 60)).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`;
  if (state.phase === 'COUNTDOWN') $('countdownNumber').textContent = Math.max(1, total);
}

// ── 준비 페이즈 HUD ───────────────────────────────────────────────────
function trinketText(t) {
  if (!t) return '없음';
  const fx = [];
  if (t.charges) fx.push(`대시 +${t.charges}회`);
  if (t.recharge) fx.push(`쿨타임 ${t.recharge}ms`);
  return `${t.label} (${fx.join(' · ')})`;
}

function gearSummary(p) {
  if (!cfg || !p.enhance) return '';
  const rows = [
    { icon: cfg.gear.weapon.icon, label: cfg.gear.weapon.base.label, lv: p.enhance.weapon },
    { icon: cfg.gear.armor.icon, label: cfg.gear.armor.base.label, lv: p.enhance.armor },
    { icon: cfg.gear.trinket.icon, label: p.trinket ? p.trinket.label : '장신구 없음', lv: 0,
      grade: p.trinket ? p.trinket.gradeColor : null }
  ];
  return rows.map(r => `<div class="gear-chip"${r.grade ? ` style="--grade:${r.grade}"` : ''}>
    <span class="gear-icon">${r.icon}</span>
    <b${r.grade ? ' class="graded"' : ''}>${escapeHtml(r.label)}</b>
    <i>${r.lv ? `+${r.lv}` : ''}</i>
  </div>`).join('');
}

function updatePrepHud() {
  const p = me();
  if (!p || !cfg) return;
  $('money').textContent = fmt(p.money);
  $('scoreNow').textContent = fmt(p.score);
  $('spent').textContent = fmt(p.spentThisRound);
  // 지금 잔액을 그대로 남기면 얼마가 이월되는지 실시간으로 보여준다.
  // (전투 획득 재화가 합산된 뒤 한도가 적용되므로 실제 이월액은 이보다 클 수 있다)
  const cap = state.settings.carryCap;
  const over = p.money > cap;
  $('carryHint').innerHTML = over
    ? `지금 남기면 <b>${fmt(cap)}S</b> 이월 · 한도(${fmt(cap)}S) 초과 <b class="over">${fmt(p.money - cap)}S는 소멸</b>`
    : `지금 남기면 <b>${fmt(Math.min(p.money, cap))}S</b> 이월 (한도 ${fmt(cap)}S · 전투 획득 재화도 합산)`;

  const gearSig = JSON.stringify([p.enhance, p.trinket]);
  if (gearSig !== lastGearSig) { $('gearStrip').innerHTML = gearSummary(p); lastGearSig = gearSig; }

  const w = state.wheel;
  if (w) {
    const left = Math.max(0, (w.endsAt - serverNow()) / 1000);
    const labels = { BETTING: '베팅 가능', SPINNING: '회전 중', RESULT: '결과', IDLE: '대기', CLOSED: '마감' };
    $('tickerState').textContent = labels[w.state] || w.state;
    $('tickerState').className = `ticker-state st-${w.state}`;
    $('tickerTime').textContent = (w.state === 'IDLE' || w.state === 'CLOSED') ? '-' : `${left.toFixed(1)}s`;
    $('tickerHistory').innerHTML = (w.history || []).slice(-8).map(h =>
      `<span class="hist n${h.number}">${h.number}</span>`).join('');
  }

  renderAnnouncements();

  const st = nearestStation();
  const prompt = $('prompt');
  if (st && !openModal) {
    prompt.classList.remove('hidden');
    $('promptText').textContent = `${st.label} ${st.hint}`;
  } else prompt.classList.add('hidden');

  const near = nearestPlayer();
  const pp = $('playerPrompt');
  if (near && !st && !openModal) {
    pp.classList.remove('hidden');
    const begging = (near.begUntil || 0) > serverNow();
    $('playerPromptText').textContent = `${near.name}${begging ? ' (구걸 중)' : ''} 에게 적선`;
  } else pp.classList.add('hidden');

  // 준비 완료 — 전체 동의 시 조기 종료 (봇은 자동 동의)
  const r = state.ready || { done: 0, total: 0 };
  const btn = $('readyBtn');
  btn.classList.toggle('on', !!p.ready);
  $('readyCount').textContent = r.total ? `${r.done}/${r.total}` : '';
  btn.firstChild.textContent = p.ready ? '준비 완료 취소 ' : '준비 완료 ';
}

function updateBattleHud() {
  const p = me();
  if (!p) return;
  if (p.maxHp) {
    $('hpBar').style.width = `${Math.max(0, p.hp / p.maxHp * 100)}%`;
    $('hpText').textContent = Math.ceil(p.hp);
  }
  $('killTally').textContent = `킬 ${p.kills} · 어시 ${p.assists}`;

  // 생존자 수 — 팀전이면 팀별로 보여준다
  if (isTeamMode() && state.teams) {
    $('aliveCount').textContent = state.teams.map(t => `${t.label} ${t.alive}`).join(' vs ');
  } else {
    $('aliveCount').textContent = `${state.players.filter(x => x.alive && !x.spectating).length}명 생존`;
  }

  // 황금 고블린 현상금 안내
  const gi = $('goblinInfo');
  if (state.goblin) {
    gi.classList.remove('hidden');
    const isMe = state.goblin.id === myId;
    gi.textContent = isMe
      ? `👑 내가 황금 고블린 · 현상금 ${fmt(state.goblin.killBounty)}S가 걸렸다`
      : `👑 ${state.goblin.name} · 처치 시 +${fmt(state.goblin.killBounty)}S`;
    gi.classList.toggle('is-me', isMe);
  } else gi.classList.add('hidden');

  const st = state.storm;
  const warn = $('stormWarn');
  if (st) {
    const dist = Math.hypot(myPos().x - st.cx, myPos().y - st.cy);
    const outside = dist > st.radius;
    const toStart = (st.startAt - serverNow()) / 1000;
    warn.classList.remove('hidden');
    if (outside) {
      warn.className = 'storm-warn danger';
      warn.textContent = `⚠ 자기장 밖 · 초당 ${st.dps} 피해 · 안전 구역까지 ${Math.round(dist - st.radius)}`;
    } else if (toStart > 0) {
      warn.className = 'storm-warn calm';
      warn.textContent = `자기장 수축까지 ${Math.ceil(toStart)}초`;
    } else {
      warn.className = 'storm-warn closing';
      warn.textContent = `자기장 수축 중 · 반경 ${Math.round(st.radius)} · 여유 ${Math.round(st.radius - dist)}`;
    }
  } else warn.classList.add('hidden');

  const dropCount = (state.drops || []).length;
  $('dropInfo').textContent = dropCount ? `보급 ${dropCount}개 투하됨` : '보급 대기';

  const gearSig = 'b' + JSON.stringify([p.enhance, p.trinket]);
  if (gearSig !== lastGearSig) { $('battleGear').innerHTML = gearSummary(p); lastGearSig = gearSig; }

  const controls = $('controls');
  if (p.spectating) { controls.textContent = '관전 중 · 다음 준비 페이즈부터 참가합니다'; return; }
  if (!p.alive) {
    controls.textContent = '👻 유령 모드 · WASD 이동 (벽 통과) · 살아있는 유저에게는 보이지 않습니다';
    return;
  }
  const max = p.maxDashCharges ?? 1, have = p.dashCharges ?? 0;
  const pips = '●'.repeat(have) + '○'.repeat(Math.max(0, max - have));
  const refill = Math.max(0, ((p.dashRechargeAt || 0) - serverNow()) / 1000);
  const cd = Math.max(0, ((p.attackReadyAt || 0) - serverNow()) / 1000);
  controls.textContent = `대시 ${pips}${have < max && refill > 0 ? ` (+1 ${refill.toFixed(1)}s)` : ''}`
    + ` · 공격 ${cd > 0 ? `${cd.toFixed(2)}s` : '준비'}`
    + ` · 피해 ${round1(p.damage || 0)} · TAB 현황`;
}

// Tab 현황판 — 전투 단계 공개 정보: 강화 단계·생존·킬/어시·생존자 수 (문서 15절)
function renderTabBoard() {
  const el = $('tabBoard');
  el.classList.remove('hidden');
  const rows = state.players.filter(p => !p.spectating)
    .slice()
    .sort((a, b) => (b.alive - a.alive) || (b.kills - a.kills) || (b.score - a.score));
  $('tabRows').innerHTML = rows.map(p => {
    const goblin = state.goblin && state.goblin.id === p.id;
    return `<div class="tab-row${p.alive ? '' : ' dead'}" style="--player:${displayColor(p)}">
      <b>${goblin ? '👑 ' : ''}${p.bot ? '🤖 ' : ''}${escapeHtml(p.name)}${p.id === myId ? ' · 나' : ''}</b>
      ${isTeamMode() ? `<span class="tab-team">${escapeHtml(teamOf(p.team).label)}</span>` : ''}
      <span>무기 +${p.enhance?.weapon ?? 0} · 방어구 +${p.enhance?.armor ?? 0}${p.trinket ? ` · ${escapeHtml(p.trinket.gradeLabel)} 장신구` : ''}</span>
      <span>킬 ${p.kills} · 어시 ${p.assists}</span>
      <em>${p.alive ? '생존' : '탈락'}</em>
    </div>`;
  }).join('');
}

// ── 모달 ──────────────────────────────────────────────────────────────
function showModal(kind) {
  if (state.phase !== 'PREP') return;
  const mine = me();
  if (!mine || mine.spectating) return;
  openModal = kind;
  keys.clear();
  Object.entries(modals).forEach(([k, el]) => el.classList.toggle('hidden', k !== kind));
  if (kind === 'shop') { lastShopSig = ''; renderShop(); }
  if (kind === 'anvil') { lastAnvilSig = ''; renderAnvil(); }
  if (kind === 'wheel') { renderWheelStatic(); updateWheelModal(); }
  if (kind === 'alms') renderAlms();
  if (kind === 'board') { lastBoardSig = ''; renderBoard(); }
}

function closeModal() {
  openModal = null;
  Object.values(modals).forEach(el => el.classList.add('hidden'));
  $('prompt').classList.add('hidden');
}

document.querySelectorAll('[data-close]').forEach(b => b.onclick = closeModal);
Object.values(modals).forEach(el => el.addEventListener('mousedown', e => {
  if (e.target === el) closeModal();
}));

// ── 룰렛 모달 ─────────────────────────────────────────────────────────
function renderWheelStatic() {
  if (!cfg) return;
  const r = cfg.roulette;
  $('wheelRule').innerHTML = `${escapeHtml(r.rule)} · ${r.slots}칸 · `
    + `대기 ${r.betMs / 1000}초 / 회전 ${r.spinMs / 1000}초 · 최소 ${r.minBet}S`
    + `<br>개인별 베팅은 비공개 · <b>전체 판돈과 참여 인원만 공개</b>됩니다`
    + `<br>준비 종료 <b>${r.closeLeadMs / 1000}초 전에 마감</b>되고, 돌지 않은 베팅은 전액 환불됩니다`;
  $('wheelOdds').innerHTML = r.table.map(row => `<div class="odds-tile n${row.number}">
    <b>${row.number}</b>
    <span class="odds-chance">${(row.chance * 100).toFixed(2)}%</span>
    <span class="odds-pay">회수 ×${row.payout}</span>
    <span class="odds-ev">기대 ${(row.expected * 100).toFixed(1)}%</span>
    <span class="odds-slots">${row.slots}칸</span>
  </div>`).join('');
  $('chipRow').innerHTML = [10, 50, 100, 250, 500, 'ALL'].map(c =>
    `<button data-chip="${c}" class="chip${(c === chip || (c === 'ALL' && chip === 'ALL')) ? ' on' : ''}">${c === 'ALL' ? '전액' : c}</button>`).join('');
  $('chipRow').querySelectorAll('[data-chip]').forEach(b => b.onclick = () => {
    chip = b.dataset.chip === 'ALL' ? 'ALL' : Number(b.dataset.chip);
    renderWheelStatic();
  });
  $('betRow').innerHTML = r.numbers.map(n =>
    `<button data-bet="${n}" class="bet-btn n${n}">
      <b>${n}</b><small>×${n + 1}</small><i data-mine="${n}">0</i><em data-pot="${n}">판돈 0</em>
    </button>`).join('');
  $('betRow').querySelectorAll('[data-bet]').forEach(b => b.onclick = () => {
    const p = me();
    if (!p) return;
    const amount = chip === 'ALL' ? p.money : chip;
    post({ action: 'bet', number: Number(b.dataset.bet), amount });
  });
  $('clearBetsBtn').onclick = () => post({ action: 'clearBets' });
}

function updateWheelModal() {
  const w = state.wheel, p = me();
  if (!w || !p || !cfg) return;
  const wc = $('wheelCanvas');
  const c = wc.getContext('2d');
  c.clearRect(0, 0, wc.width, wc.height);
  drawWheelFace(c, wc.width / 2, wc.height / 2, wc.width / 2 - 14, wheelRotation, 1, cfg.roulette.layout, { labels: true });

  const labels = { BETTING: '베팅 가능', SPINNING: '회전 중', RESULT: '결과', IDLE: '대기', CLOSED: '마감' };
  $('wheelStateLabel').textContent = w.state === 'RESULT' && w.number != null ? `${w.number} 당첨` : (labels[w.state] || w.state);
  $('wheelStateLabel').className = `st-${w.state}`;
  const left = Math.max(0, (w.endsAt - serverNow()) / 1000);
  $('wheelStateTime').textContent = (w.state === 'IDLE' || w.state === 'CLOSED') ? '-' : `${left.toFixed(1)}s`;
  $('wheelClosed').classList.toggle('hidden', w.state !== 'CLOSED');

  let staked = 0;
  for (const n of cfg.roulette.numbers) {
    const mine = (w.myBets || {})[n] || 0;
    staked += mine;
    const mineEl = $('betRow').querySelector(`[data-mine="${n}"]`);
    const potEl = $('betRow').querySelector(`[data-pot="${n}"]`);
    if (mineEl) { mineEl.textContent = fmt(mine); mineEl.classList.toggle('has', mine > 0); }
    if (potEl) potEl.textContent = `판돈 ${fmt((w.pot || {})[n] || 0)}`;
    const btn = $('betRow').querySelector(`[data-bet="${n}"]`);
    if (btn) btn.disabled = w.state !== 'BETTING' || p.money < cfg.roulette.minBet;
  }
  $('betSummary').textContent = staked
    ? `베팅 합계 ${fmt(staked)}S · 참여 ${w.bettors}명 · 잔액 ${fmt(p.money)}S`
    : `베팅 없음 · 참여 ${w.bettors}명 · 잔액 ${fmt(p.money)}S`;
  $('clearBetsBtn').disabled = !staked || w.state !== 'BETTING';
}

function renderWheelLog(entry) {
  const el = $('wheelLog');
  if (!el) return;
  const tone = entry.net > 0 ? 'win' : entry.payout ? 'even' : 'lose';
  const row = document.createElement('div');
  row.className = `log-row ${tone}`;
  row.innerHTML = `<b>${entry.number}</b><span>걸음 ${fmt(entry.staked)}S</span>`
    + `<span>회수 ${fmt(entry.payout)}S</span><em>${entry.net >= 0 ? '+' : ''}${fmt(entry.net)}S</em>`;
  el.prepend(row);
  while (el.children.length > 8) el.lastChild.remove();
}

// ── 상점 모달: 장신구 랜덤 뽑기 ──────────────────────────────────────
function renderShop() {
  const p = me();
  if (!p || !cfg) return;
  const T = cfg.trinkets;
  const sig = JSON.stringify([p.trinket, p.money]);
  if (sig === lastShopSig) return;
  lastShopSig = sig;

  $('shopRule').innerHTML = `뽑기 1회 <b>${fmt(T.price)}S</b> · ${escapeHtml(T.rule)}`
    + `<br>장신구는 <b>단독 슬롯 1개</b>입니다. 이미 장착 중이면 <b>무조건 교체</b>되므로 뽑기 자체가 교체 리스크입니다.`;

  $('gachaCurrent').innerHTML = p.trinket
    ? `<small>현재 장착</small><b style="color:${p.trinket.gradeColor}">${escapeHtml(p.trinket.label)}</b>
       <span>${escapeHtml(trinketFx(p.trinket))}</span>`
    : `<small>현재 장착</small><b class="none">장신구 없음</b><span>기본 대시 ${cfg.dash.charges}회 · 재충전 ${cfg.dash.recharge}ms</span>`;

  const btn = $('pullBtn');
  btn.disabled = p.money < T.price;
  btn.textContent = p.money < T.price ? '재화 부족' : `뽑기 · ${fmt(T.price)}S`;
  btn.onclick = () => post({ action: 'pull' });

  // 전체 확률표 — 확률을 숨기지 않는다 (문서 16절 UX 원칙)
  $('gachaTable').innerHTML = `<h4>뽑기 확률표</h4>` + cfg.trinkets.types.map(type => {
    const items = T.items.filter(i => i.type === type.key);
    return `<div class="gacha-type">
      <header><b>${escapeHtml(type.label)}</b><small>${escapeHtml(type.note)}</small></header>
      <div class="gacha-grades">${items.map(i => `
        <div class="gacha-grade" style="--grade:${i.gradeColor}">
          <b>${escapeHtml(i.gradeLabel)}</b>
          <span>${escapeHtml(trinketFx(i))}</span>
          <em>${(i.chance * 100).toFixed(1)}%</em>
        </div>`).join('')}</div>
    </div>`;
  }).join('');
}

function trinketFx(t) {
  const fx = [];
  if (t.charges) fx.push(`대시 횟수 +${t.charges}`);
  if (t.recharge) fx.push(`대시 쿨타임 ${t.recharge}ms`);
  return fx.join(' · ') || '효과 없음';
}

function renderPullResult(pull) {
  lastShopSig = '';
  const el = $('pullResult');
  el.classList.remove('hidden');
  el.innerHTML = `<div class="pull-card" style="--grade:${pull.item.gradeColor}">
    <small>${escapeHtml(pull.item.gradeLabel)}</small>
    <b>${escapeHtml(pull.item.label)}</b>
    <span>${escapeHtml(trinketFx(pull.item))}</span>
    ${pull.replaced ? `<em>${escapeHtml(pull.replaced.label)} 교체됨</em>` : '<em>즉시 장착</em>'}
  </div>`;
  el.classList.remove('pop');
  void el.offsetWidth;
  el.classList.add('pop');
  if (openModal === 'shop') renderShop();
}

// ── 모루 모달: 확률 강화 ─────────────────────────────────────────────
function renderAnvil() {
  const p = me();
  if (!p || !cfg) return;
  const sig = JSON.stringify([p.enhance, p.money, p.lastEnhance?.seq]);
  if (sig === lastAnvilSig) return;
  lastAnvilSig = sig;
  const A = cfg.anvil;

  $('anvilRule').innerHTML = `${escapeHtml(A.rule)} · 슬롯별 <b>+${A.maxEnhance}</b>까지`
    + `<br>강화 상태는 ${state.settings.carryEnhance ? '<b>다음 라운드로 이월</b>됩니다 (호스트 설정)' : '기본적으로 <b>라운드 간 이월되지 않습니다</b>'}`
    + ` · 전투 시작 시 다른 플레이어에게 공개됩니다`;

  $('anvilSlots').innerHTML = A.slots.map(slot => {
    const spec = cfg.gear[slot];
    const lv = p.enhance[slot] || 0;
    const cost = lv >= A.maxEnhance ? null : A.costTable[slot][lv];
    const chance = lv >= A.maxEnhance ? 0 : A.chance[lv];
    const attempts = p.attempts?.[slot] || 0, fails = p.fails?.[slot] || 0;
    const track = Array.from({ length: A.maxEnhance }, (_, i) =>
      `<span class="pip${i < lv ? ' filled' : ''}${i === lv ? ' next' : ''}"
        title="+${i + 1} · ${fmt(A.costTable[slot][i])}S · ${Math.round(A.chance[i] * 100)}%"></span>`).join('');
    const gains = (A.gains[slot] || []).map(g =>
      `<div class="gain-row"><span>${escapeHtml(g.label)}</span>
        <b>${g.per > 0 ? '+' : ''}${round1(g.per)}${g.unit || ''}</b><i>단계당</i></div>`).join('');
    const flash = p.lastEnhance && p.lastEnhance.slot === slot
      && serverNow() - p.lastEnhance.at < 2500 ? (p.lastEnhance.success ? 'flash-ok' : 'flash-fail') : '';
    return `<section class="anvil-slot ${flash}">
      <header>
        <span class="slot-icon">${spec.icon}</span>
        <div><h3>${escapeHtml(spec.label)}</h3>
          <small>${escapeHtml(spec.base.label)}</small></div>
        <span class="level">+${lv}</span>
      </header>
      <div class="level-track">${track}</div>
      <div class="gain-list">${gains}</div>
      ${cost !== null ? `<div class="chance-row">
        <span>성공 확률</span><b class="chance-val">${Math.round(chance * 100)}%</b>
        <span>실패 시</span><i>재화만 소모 · 단계 유지</i>
      </div>` : ''}
      <small class="anvil-remain">${attempts ? `시도 ${attempts}회 · 실패 ${fails}회` : '아직 시도 없음'}</small>
      <button data-enhance="${slot}" ${cost === null || p.money < cost ? 'disabled' : ''}>${
        cost === null ? '최대 단계' : p.money < cost ? '재화 부족'
        : `+${lv + 1} 시도 · ${fmt(cost)}S (${Math.round(chance * 100)}%)`
      }</button>
    </section>`;
  }).join('');

  $('anvilSlots').querySelectorAll('[data-enhance]').forEach(b =>
    b.onclick = () => post({ action: 'enhance', slot: b.dataset.enhance }));
}

// ── 순위판 모달 ──────────────────────────────────────────────────────
function renderBoard() {
  const sig = JSON.stringify(state.players.map(p => [p.score, p.prevRank]));
  if (sig === lastBoardSig) return;
  lastBoardSig = sig;
  const rows = state.players.filter(p => !p.spectating)
    .slice().sort((a, b) => b.score - a.score);
  $('boardRows').innerHTML = rows.map((p, i) => `
    <div class="board-row" style="--player:${displayColor(p)}">
      <span class="place">${i + 1}위</span>
      <b>${p.bot ? '🤖 ' : ''}${escapeHtml(p.name)}${p.id === myId ? ' · 나' : ''}</b>
      ${isTeamMode() ? `<span class="tab-team">${escapeHtml(teamOf(p.team).label)}</span>` : ''}
      <span>${p.prevRank ? `이전 라운드 ${p.prevRank}위` : '첫 라운드'}</span>
      <em>${p.score}점</em>
    </div>`).join('');
}

// ── 적선 / 구걸 ───────────────────────────────────────────────────────
function openAlms(targetId) {
  almsTargetId = targetId;
  showModal('alms');
}

function renderAlms() {
  const p = me();
  if (!p || !cfg) return;
  const target = state.players.find(x => x.id === almsTargetId);
  if (!target) { closeModal(); return; }
  const C = cfg.charity;
  const begging = (target.begUntil || 0) > serverNow();

  $('almsRule').innerHTML = `최소 ${C.minGive}S · 상대에게서 <b>${C.reach} 이내</b>에 있어야 합니다`
    + ` · 적선 내역은 <b>광장 전체에 공개</b>됩니다`
    + (isTeamMode() ? `<br>팀전에서도 재화는 <b>개인 소유</b>이며, 팀원에게도 적선으로만 넘길 수 있습니다` : '');

  $('almsTarget').innerHTML = `<div class="alms-who" style="--player:${displayColor(target)}">
    <b>${target.bot ? '🤖 ' : ''}${escapeHtml(target.name)}</b>
    ${isTeamMode() ? `<span class="tab-team">${escapeHtml(teamOf(target.team).label)}</span>` : ''}
    ${begging ? '<span class="begging">구걸 중</span>' : ''}
    <small>받은 적선 ${fmt(target.received)}S · 베푼 적선 ${fmt(target.given)}S</small>
  </div>`;

  const options = [50, 100, 250, 500, 'ALL'];
  $('almsChips').innerHTML = options.map(c =>
    `<button data-alms-chip="${c}" class="chip${c === almsChip ? ' on' : ''}">${c === 'ALL' ? '전액' : c}</button>`).join('');
  $('almsChips').querySelectorAll('[data-alms-chip]').forEach(b => b.onclick = () => {
    almsChip = b.dataset.almsChip === 'ALL' ? 'ALL' : Number(b.dataset.almsChip);
    renderAlms();
  });

  const amount = almsChip === 'ALL' ? p.money : almsChip;
  $('almsBalance').textContent = `잔액 ${fmt(p.money)}S · 적선 후 ${fmt(Math.max(0, p.money - amount))}S`;
  const btn = $('almsGiveBtn');
  btn.disabled = p.money < C.minGive;
  btn.textContent = p.money < C.minGive ? '재화 부족' : `${fmt(Math.min(amount, p.money))}S 적선하기`;
  btn.onclick = () => {
    post({ action: 'give', target: almsTargetId, amount });
    closeModal();
  };
}

function renderAnnouncements() {
  const list = state.announcements || [];
  const el = $('announceFeed');
  el.innerHTML = list.slice(-5).map(a =>
    `<div class="announce ${a.tone}">${escapeHtml(a.text)}</div>`).join('');
  const newest = list.length ? list[list.length - 1].seq : 0;
  if (newest !== lastAnnounceSeq) {
    lastAnnounceSeq = newest;
    el.classList.remove('pulse');
    void el.offsetWidth;
    el.classList.add('pulse');
  }
}

// ── 로비 / 설정 ───────────────────────────────────────────────────────
function renderLobby() {
  $('lobbyRoomName').textContent = state.room ? state.room.name : 'ROOM';
  $('lobbyCode').textContent = state.room
    ? `${state.room.visibility === 'private' ? '🔒 ' : ''}코드 ${state.room.code}` : '';
  $('lobbyCount').textContent = `${state.players.length} / ${state.room ? state.room.capacity : 8}`;

  // 팀 배정 표시 (문서 16절: 팀 배정·팀 색상 확인 화면)
  const teamsEl = $('lobbyTeams');
  if (isTeamMode() && cfg) {
    teamsEl.classList.remove('hidden');
    teamsEl.innerHTML = cfg.teams.map(t => {
      const members = state.players.filter(p => p.team === t.index);
      return `<div class="lobby-team" style="--team:${t.color}">
        <header><b>${escapeHtml(t.label)}</b><small>${members.length} / ${state.settings.teamSize}</small></header>
        ${members.map(p => `<div class="lobby-team-member">
          ${p.bot ? '🤖 ' : ''}${escapeHtml(p.name)}${p.id === myId ? ' · 나' : ''}
          ${p.id === state.hostId ? '<i>HOST</i>' : ''}</div>`).join('')}
      </div>`;
    }).join('');
    $('lobbyPlayers').innerHTML = '';
  } else {
    teamsEl.classList.add('hidden');
    $('lobbyPlayers').innerHTML = state.players.map(p => `<div class="lobby-player" style="--player:${p.color}">
      <b>${p.bot ? '🤖 ' : ''}${escapeHtml(p.name)}${p.id === myId ? ' · 나' : ''}</b>
      <small>${p.id === state.hostId ? 'HOST' : p.bot ? 'BOT' : 'READY'}</small>
    </div>`).join('');
  }

  const isHost = myId === state.hostId;
  $('startBtn').classList.toggle('hidden', !isHost);
  $('hostWaiting').classList.toggle('hidden', isHost);
  $('settingsOwner').textContent = isHost ? '값을 바꾸면 모두에게 적용됩니다' : '호스트만 변경할 수 있습니다';
  $('startHint').textContent = state.players.length < (cfg ? cfg.minPlayers : 2)
    ? '인간·봇 합산 2명부터 시작할 수 있습니다 (부족하면 봇이 자동 보충됩니다)'
    : '';
  renderSettings();
}

function renderSettings() {
  if (!cfg) return;
  const isHost = myId === state.hostId;
  const sig = JSON.stringify([state.settings, isHost]);
  if (sig === lastSettingsSig) return;
  lastSettingsSig = sig;

  // 대전 모드 (개인전 / 팀전)
  $('settingsEnums').innerHTML = Object.entries(cfg.settingsEnums).map(([key, spec]) => {
    const current = state.settings[key] ?? spec.default;
    return `<div class="setting-enum">
      <span>${escapeHtml(spec.label)}</span>
      <div class="enum-options">${spec.options.map(o =>
        `<button data-enum="${key}" data-value="${o.value}"
          class="enum-btn${o.value === current ? ' on' : ''}" ${isHost ? '' : 'disabled'}>
          <b>${escapeHtml(o.label)}</b><small>${escapeHtml(o.note)}</small>
        </button>`).join('')}</div>
    </div>`;
  }).join('');

  const numericKeys = Object.entries(cfg.settingsSpec)
    .filter(([key]) => key !== 'teamSize' || isTeamMode());
  $('settingsFields').innerHTML = numericKeys.map(([key, spec]) => {
    const value = state.settings[key] ?? spec.default;
    return `<label class="setting-field">
      <span>${escapeHtml(spec.label)}</span>
      <div class="setting-input">
        <button data-step="${key}" data-dir="-1" ${isHost ? '' : 'disabled'}>−</button>
        <input data-setting="${key}" type="number" value="${value}"
          min="${spec.min}" max="${spec.max}" step="${spec.step}" ${isHost ? '' : 'disabled'}>
        <button data-step="${key}" data-dir="1" ${isHost ? '' : 'disabled'}>+</button>
      </div>
      <small>${escapeHtml(spec.unit)} · ${spec.min}~${spec.max}</small>
    </label>`;
  }).join('');

  const flagKeys = Object.entries(cfg.settingsFlags)
    .filter(([key]) => key !== 'friendlyFire' || isTeamMode());
  $('settingsFlags').innerHTML = flagKeys.map(([key, spec]) => {
    const on = !!state.settings[key];
    return `<button class="setting-flag${on ? ' on' : ''}" data-flag="${key}" ${isHost ? '' : 'disabled'}>
      <span class="flag-dot"></span>
      <b>${escapeHtml(spec.label)}</b>
      <small>${escapeHtml(on ? spec.onNote : spec.offNote)}</small>
    </button>`;
  }).join('');

  if (!isHost) return;
  const send = patch => post({ action: 'settings', settings: patch });
  $('settingsEnums').querySelectorAll('[data-enum]').forEach(btn => {
    btn.onclick = () => send({ [btn.dataset.enum]: btn.dataset.value });
  });
  $('settingsFields').querySelectorAll('[data-setting]').forEach(input => {
    input.onchange = () => send({ [input.dataset.setting]: Number(input.value) });
  });
  $('settingsFields').querySelectorAll('[data-step]').forEach(btn => {
    btn.onclick = () => {
      const key = btn.dataset.step;
      const spec = cfg.settingsSpec[key];
      const current = state.settings[key] ?? spec.default;
      send({ [key]: current + spec.step * Number(btn.dataset.dir) });
    };
  });
  $('settingsFlags').querySelectorAll('[data-flag]').forEach(btn => {
    btn.onclick = () => send({ [btn.dataset.flag]: !state.settings[btn.dataset.flag] });
  });
}

// ── 카운트다운 (황금 고블린 발표) ────────────────────────────────────
function renderCountdown() {
  const el = $('goblinBanner');
  if (state.goblin) {
    el.classList.remove('hidden');
    el.innerHTML = `<b>👑 황금 고블린: ${escapeHtml(state.goblin.name)}</b>
      <span>전투력 ${fmt(state.goblin.combatValue)}S · 처치 ${fmt(state.goblin.killBounty)}S / 어시 ${fmt(state.goblin.assistBounty)}S 현상금</span>`;
  } else {
    el.classList.remove('hidden');
    el.innerHTML = `<span>이번 라운드 황금 고블린 없음 (장비 투자자 없음)</span>`;
  }
}

// ── 정산 ──────────────────────────────────────────────────────────────
const DEATH_LABEL = { storm: '자기장', fall: '낙사', combat: '' };

function renderResults(finish) {
  $('teamResult').classList.add('hidden');
  $('dramaBox').classList.add('hidden');
  if (finish && state.final) return renderFinal();

  const ordered = [...state.players].filter(p => !p.spectating)
    .sort((a, b) => (a.rank || 99) - (b.rank || 99));

  $('resultEyebrow').textContent = 'ROUND RESULT';
  $('resultTitle').textContent = `${state.round} 라운드 정산`;
  $('resultLegend').textContent = cfg
    ? `라운드 점수 = 등수(${cfg.scoring.rankPointsTop}~${cfg.scoring.rankPointsBottom}) + 킬 ${cfg.scoring.killPoints}점 + 어시 ${cfg.scoring.assistPoints}점 · 보유 재화는 점수에 포함되지 않습니다`
    : '';

  // 팀전 라운드 승패
  if (isTeamMode() && cfg) {
    const el = $('teamResult');
    el.classList.remove('hidden');
    el.innerHTML = state.roundWinnerTeam !== null && state.roundWinnerTeam !== undefined
      ? `<b style="--team:${teamOf(state.roundWinnerTeam).color}">${escapeHtml(teamOf(state.roundWinnerTeam).label)} 팀 라운드 승리</b>`
        + (state.teams ? `<span>${state.teams.map(t => `${t.label} ${t.wins}승`).join(' · ')}</span>` : '')
      : `<b>라운드 무승부</b>`;
  }

  $('ranking').innerHTML = ordered.map((p, i) => {
    const place = `${p.rank || i + 1}위`;
    const pb = p.pointsBreakdown, yb = p.payoutBreakdown;
    const pointsDetail = pb
      ? `+${p.roundPoints}점 (등수 ${pb.rank} · 킬 ${pb.kills} · 어시 ${pb.assists})`
      : `+${p.roundPoints || 0}점`;
    const moneyDetail = yb
      ? `등수 ${fmt(yb.rank)} · 생존 ${fmt(yb.survival)} · 킬 ${fmt(yb.kills)} · 어시 ${fmt(yb.assists)}`
        + `${yb.bounty ? ` · 현상금 ${fmt(yb.bounty)}` : ''}${yb.looted ? ` · 보급 ${fmt(yb.looted)}` : ''}`
      : '';
    // 이월 계산 — 준비 잔액 + 전투 획득 → min(한도) → 다음 라운드 재화
    const carry = p.carry;
    const carryDetail = carry
      ? `준비 잔액 ${fmt(carry.prepEnd)} + 전투 획득 ${fmt(carry.battleGain)} = ${fmt(carry.raw)}S`
        + ` → 이월 ${fmt(carry.carried)}S${carry.lost ? ` <i class="lost">(한도 초과 ${fmt(carry.lost)}S 소멸)</i>` : ''}`
        + ` → 다음 라운드 ${fmt(carry.next)}S`
      : '';
    const death = p.deathCause && DEATH_LABEL[p.deathCause] ? ` · ${DEATH_LABEL[p.deathCause]}` : '';
    return `<div class="rank-row" style="--player:${displayColor(p)}">
      <span class="place">${place}</span>
      <div class="rank-main">
        <b>${p.bot ? '🤖 ' : ''}${escapeHtml(p.name)}${p.id === myId ? ' · 나' : ''}${isTeamMode() ? ` <span class="tab-team">${escapeHtml(teamOf(p.team).label)}</span>` : ''}</b>
        <span class="gear">킬 ${p.kills} · 어시 ${p.assists}${death}`
          + ` · 무기 +${p.enhance?.weapon ?? 0} 방어구 +${p.enhance?.armor ?? 0}`
          + `${p.trinket ? ` · ${escapeHtml(p.trinket.gradeLabel)} 장신구` : ''}</span>
      </div>
      <div class="rank-detail">
        <span class="points">${pointsDetail} · 누적 ${p.score}점</span>
        <span class="money-detail">재화: ${moneyDetail}</span>
        ${carryDetail ? `<span class="carry-detail">${carryDetail}</span>` : ''}
      </div>
      <span class="score">${p.score} <em>PTS</em></span>
    </div>`;
  }).join('');

  $('nextNotice').textContent = state.round >= state.totalRounds
    ? '최종 집계로 넘어갑니다'
    : `다음 라운드: 기본 ${fmt(state.settings.baseMoney)}S + 이월 재화 · ${state.settings.carryEnhance ? '강화는 이월됩니다' : '강화는 초기화됩니다'}`;
  $('rematchBtn').classList.add('hidden');
  $('lobbyBtn').classList.add('hidden');
}

// 최종 정산 — 누적 점수 순위, 동점 판정 연출, 보유 재화는 별도 자산 지표 (문서 14절)
function renderFinal() {
  const f = state.final;
  $('resultEyebrow').textContent = 'FINAL';
  $('resultTitle').textContent = '최종 결과';
  $('resultLegend').textContent = '최종 점수 = 누적 라운드 전투 점수 · 동점은 총 점수 → 전투력 → 서버 난수 순으로 판정 · 보유 재화는 점수에 포함되지 않습니다';

  if (f.mode === 'team' && f.teams) {
    const el = $('teamResult');
    el.classList.remove('hidden');
    el.innerHTML = f.teams.map((t, i) => `<b style="--team:${t.color}">${i === 0 ? '🏆 ' : ''}${escapeHtml(t.label)} ${t.wins}승 · 팀 합계 ${t.score}점</b>`).join('')
      + (f.teamNote ? `<span class="team-note">${escapeHtml(f.teamNote)}</span>` : '');
  }

  $('ranking').innerHTML = f.rows.map(row => {
    const p = state.players.find(x => x.id === row.id);
    return `<div class="rank-row final" style="--player:${p ? displayColor(p) : '#888'}">
      <span class="place">${row.place}위</span>
      <div class="rank-main">
        <b>${row.bot ? '🤖 ' : ''}${escapeHtml(row.name)}${row.id === myId ? ' · 나' : ''}${f.mode === 'team' ? ` <span class="tab-team">${escapeHtml(teamOf(row.team).label)}</span>` : ''}</b>
        <span class="gear">킬 ${row.kills} · 어시 ${row.assists}</span>
      </div>
      <div class="rank-detail">
        <span class="points">전투력 ${fmt(row.combatValue)}S</span>
        <span class="money-detail">보유 재화 ${fmt(row.money)}S <i>(점수 제외 · 자산 지표)</i></span>
      </div>
      <span class="score">${row.score} <em>PTS</em></span>
    </div>`;
  }).join('');

  // 동점 판정 랜덤 결정 연출 — 2인 동전 뒤집기 · 다인 러시안 룰렛 스타일 (시각 연출)
  if (f.dramas && f.dramas.length) {
    const el = $('dramaBox');
    el.classList.remove('hidden');
    el.innerHTML = `<h4>동점 판정 · 랜덤 결정</h4>` + f.dramas.map(d => `
      <div class="drama ${d.kind}">
        <span class="drama-kind">${d.kind === 'coin' ? '🪙 동전 뒤집기' : '🎲 룰렛 추첨'}</span>
        <span class="drama-members">${d.members.map(m =>
          `<b style="color:${m.color}"${m.id === d.winnerId ? ' class="won"' : ''}>${escapeHtml(m.name)}</b>`).join(' vs ')}</span>
        <em>→ ${escapeHtml(d.winnerName)} 승</em>
        <small>총 점수·전투력 동점 → 서버 난수로 결정 (연출은 시각적 표현입니다)</small>
      </div>`).join('');
  }

  $('nextNotice').textContent = `${state.totalRounds}라운드 종료 · 재대결하거나 로비로 돌아갈 수 있습니다`;
  const isHost = myId === state.hostId;
  $('rematchBtn').classList.toggle('hidden', !isHost);
  $('lobbyBtn').classList.toggle('hidden', !isHost);
}

// ── 화면 전환 ─────────────────────────────────────────────────────────
function renderUI() {
  if (!myId || !me()) return;
  updateTimer();          // rAF 가 멈춘 백그라운드 탭에서도 스냅샷마다 갱신한다
  const labels = { LOBBY: '대기실', PREP: '준비 · 광장', COUNTDOWN: '전장 이동',
                   BATTLE: '전투', RESULT: '라운드 정산', FINISH: '최종 결과' };
  $('roundLabel').textContent = state.phase === 'LOBBY'
    ? `${state.settings.rounds || 3} 라운드 예정`
    : `ROUND ${state.round} / ${state.totalRounds}`;
  $('phaseLabel').textContent = (labels[state.phase] || state.phase)
    + (isTeamMode() ? ' · 팀전' : '');
  if (state.room) {
    $('roomBadge').classList.remove('hidden');
    $('roomBadge').textContent = `${state.room.visibility === 'private' ? '🔒' : '🌐'} ${state.room.code}`;
  }

  Object.values(screens).forEach(el => el.classList.add('hidden'));
  if (state.phase === 'LOBBY') { screens.lobby.classList.remove('hidden'); renderLobby(); }
  if (state.phase === 'PREP') { screens.prep.classList.remove('hidden'); updatePrepHud(); }
  if (state.phase === 'COUNTDOWN') { screens.countdown.classList.remove('hidden'); renderCountdown(); }
  if (state.phase === 'BATTLE') { screens.battle.classList.remove('hidden'); updateBattleHud(); }
  if (state.phase === 'RESULT') { screens.result.classList.remove('hidden'); renderResults(false); }
  if (state.phase === 'FINISH') { screens.result.classList.remove('hidden'); renderResults(true); }
  if (state.phase !== 'BATTLE') {
    $('tabBoard').classList.add('hidden');
    $('minimap').classList.add('hidden');
  }
  if (openModal === 'shop') renderShop();
  if (openModal === 'anvil') renderAnvil();
  if (openModal === 'alms') renderAlms();
  if (openModal === 'board') renderBoard();
}

// ── 시작 화면: 방 생성 · 목록 · 코드 검색 ────────────────────────────
function playerName() {
  return $('nickname').value.trim() || `Player ${Math.floor(Math.random() * 90 + 10)}`;
}

function enterRoom(data) {
  myId = data.id;
  state = data.snapshot;
  screens.join.classList.add('hidden');
  connect();
  renderUI();
}

async function apiPost(path, body) {
  const res = await fetch(path, { method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body) });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) { showToast(data.error || '요청이 실패했습니다', 'fail'); return null; }
  return data;
}

$('tabCreate').onclick = () => {
  $('tabCreate').classList.add('on'); $('tabJoin').classList.remove('on');
  $('createPane').classList.remove('hidden'); $('joinPane').classList.add('hidden');
};
$('tabJoin').onclick = () => {
  $('tabJoin').classList.add('on'); $('tabCreate').classList.remove('on');
  $('joinPane').classList.remove('hidden'); $('createPane').classList.add('hidden');
  refreshRooms();
};

let createVisibility = 'public';
$('visPublic').onclick = () => {
  createVisibility = 'public';
  $('visPublic').classList.add('on'); $('visPrivate').classList.remove('on');
  $('createPassword').classList.add('hidden');
};
$('visPrivate').onclick = () => {
  createVisibility = 'private';
  $('visPrivate').classList.add('on'); $('visPublic').classList.remove('on');
  $('createPassword').classList.remove('hidden');
};

$('createBtn').onclick = async () => {
  if (createVisibility === 'private' && !$('createPassword').value.trim()) {
    return showToast('비공개방은 비밀번호가 필요합니다', 'fail');
  }
  const data = await apiPost('/rooms', {
    playerName: playerName(),
    name: $('roomName').value.trim(),
    visibility: createVisibility,
    password: $('createPassword').value
  });
  if (data) enterRoom(data);
};

const MODE_LABEL = { solo: '개인전', team: '팀전' };
const PHASE_LABEL = { LOBBY: '대기 중', PREP: '준비 중', COUNTDOWN: '이동 중', BATTLE: '전투 중',
                      RESULT: '정산 중', FINISH: '결과 화면' };

async function refreshRooms() {
  const res = await fetch('/rooms').then(r => r.json()).catch(() => null);
  const el = $('roomList');
  if (!res || !res.rooms.length) {
    el.innerHTML = '<div class="room-empty">공개방이 없습니다 · 방을 만들어 보세요</div>';
    return;
  }
  el.innerHTML = res.rooms.map(r => `
    <button class="room-row" data-code="${r.code}">
      <b>${escapeHtml(r.name)}</b>
      <span>${r.count}/${r.capacity}명${r.bots ? ` (봇 ${r.bots})` : ''} · ${MODE_LABEL[r.mode] || r.mode}
        · ${PHASE_LABEL[r.phase] || r.phase}${r.phase !== 'LOBBY' ? ` R${r.round}/${r.totalRounds}` : ''}</span>
      <em>입장</em>
    </button>`).join('');
  el.querySelectorAll('[data-code]').forEach(b => b.onclick = async () => {
    const data = await apiPost('/join', { code: b.dataset.code, name: playerName() });
    if (data) enterRoom(data);
  });
}
$('refreshRooms').onclick = refreshRooms;

$('findBtn').onclick = async () => {
  const code = $('codeInput').value.trim().toUpperCase();
  if (!code) return;
  const data = await apiPost('/rooms/find', { code });
  if (!data) { $('foundRoom').classList.add('hidden'); $('joinCodeBtn').classList.add('hidden'); return; }
  foundRoomCode = data.room.code;
  const r = data.room;
  $('foundRoom').classList.remove('hidden');
  $('foundRoom').innerHTML = `<b>${escapeHtml(r.name)}</b>
    <span>${r.count}/${r.capacity}명 · ${MODE_LABEL[r.mode] || r.mode} · ${PHASE_LABEL[r.phase] || r.phase}</span>`;
  $('joinPassword').classList.toggle('hidden', !r.locked);
  $('joinCodeBtn').classList.remove('hidden');
};

$('joinCodeBtn').onclick = async () => {
  if (!foundRoomCode) return;
  const data = await apiPost('/join', {
    code: foundRoomCode, name: playerName(), password: $('joinPassword').value
  });
  if (data) enterRoom(data);
};

// ── 부트 ──────────────────────────────────────────────────────────────
function resize() {
  const dpr = Math.min(devicePixelRatio || 1, 2);
  canvas.width = innerWidth * dpr;
  canvas.height = innerHeight * dpr;
  canvas.style.width = `${innerWidth}px`;
  canvas.style.height = `${innerHeight}px`;
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
}

$('startBtn').onclick = () => post({ action: 'start' });
$('rematchBtn').onclick = () => post({ action: 'rematch' });
$('lobbyBtn').onclick = () => post({ action: 'lobby' });
$('begBtn').onclick = () => post({ action: 'beg' });
$('readyBtn').onclick = () => post({ action: 'ready' });
addEventListener('resize', resize);
addEventListener('beforeunload', () => {
  if (myId) navigator.sendBeacon('/leave', new Blob([JSON.stringify({ id: myId })], { type: 'application/json' }));
});

fetch('/config').then(r => r.json()).then(config => {
  cfg = config;
  lastSettingsSig = '';
  if (me()) renderUI();
}).catch(() => showToast('규칙 정보를 불러올 수 없습니다', 'fail'));

fetch('/network-info').then(r => r.json()).then(info => {
  if (info.urls?.length) {
    $('networkHint').innerHTML = `다른 단말기 접속 주소<br><b>${info.urls.map(escapeHtml).join('<br>')}</b>`;
  }
}).catch(() => {});

resize();
requestAnimationFrame(update);

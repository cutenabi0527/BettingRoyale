// 자기장·보급 검증 (문서 13절).
// 자기장은 전투 20% 지점에 시작해 100% 지점에 최소 반경으로 수렴하고,
// 수렴점은 중앙 3×3 구획, 밖에서는 지속 피해. 자기장 사망은 킬을 주지 않는다.
// 보급은 안전 구역 안·벽 밖에만 떨어지고 밟으면 회수된다.
import {
  startServer, makeReporter, makeClient, setupRoom, find, sleep,
  waitPhase, waitFor, findPath, blocked, finish
} from './test-harness.mjs';

const { proc, base } = await startServer(import.meta.dirname);
const r = makeReporter();
const api = makeClient(base);

try {
  const cfg = await api.get('/config');
  const BF = cfg.maps.battlefield;

  const { host: a, others: [b], all } = await setupRoom(api, ['도망자', '중앙수비'],
    { rounds: 1, prepSeconds: 30, battleSeconds: 60, dropSeconds: 5, baseMoney: 1000 });
  // 이동/회수 중에도 지나간 피해와 사망을 놓치지 않는다. 최신 스냅샷만 늦게 읽으면
  // RESULT → LOBBY의 체력 초기화를 보고 실제 자기장 피해를 누락할 수 있다.
  let hurt = null, died = null, ended = null;
  a.sub.onSnapshot = snap => {
    const p = find(snap, a.id);
    if (snap.phase === 'BATTLE' && snap.storm && p?.alive && p.hp < p.maxHp
        && Math.hypot(p.x - snap.storm.cx, p.y - snap.storm.cy) > snap.storm.radius) hurt ||= p;
    if (p && !p.alive) died ||= p;
    if (['RESULT', 'FINISH'].includes(snap.phase)) ended ||= snap.phase;
  };

  // 세 칸씩 건너뛴 경로는 모서리를 잘라 벽/구덩이에 들어갈 수 있다.
  // 통과 가능한 격자의 모든 꺾이는 지점을 거치고, 도달 불가능한 원점을 강제하지 않는다.
  async function walkSafely(player, target, ms = 18000) {
    const path = findPath(BF, cfg.render.playerRadius, find(player.sub.snapshot, player.id), target);
    if (!path) return false;
    const turns = path.filter((p, i) => i === 0 || i === path.length - 1
      || (p.x - path[i - 1].x) * (path[i + 1].y - p.y)
         !== (p.y - path[i - 1].y) * (path[i + 1].x - p.x));
    const deadline = Date.now() + ms;
    for (const point of turns) {
      while (Date.now() < deadline) {
        const p = find(player.sub.snapshot, player.id);
        if (!p?.alive || player.sub.snapshot.phase !== 'BATTLE') return false;
        const dx = point.x - p.x, dy = point.y - p.y, d = Math.hypot(dx, dy);
        if (d < 2) break;
        const step = Math.min(d, 7);
        await api.act(player.id, 'move', { x: p.x + dx / d * step, y: p.y + dy / d * step,
          angle: Math.atan2(dy, dx) });
        await sleep(70);
      }
      if (Date.now() >= deadline) return false;
    }
    return true;
  }
  function openPointNearest(x, y) {
    let best = null, distance = Infinity;
    for (let px = BF.bounds.minX + 25; px < BF.bounds.maxX - 20; px += 10)
      for (let py = BF.bounds.minY + 25; py < BF.bounds.maxY - 20; py += 10) {
        if (blocked(BF, px, py, cfg.render.playerRadius + 2)) continue;
        const d = Math.hypot(px - x, py - y);
        if (d < distance) { distance = d; best = { x: px, y: py }; }
      }
    if (!best) throw new Error('안전한 이동 목적지가 없습니다');
    return best;
  }

  await api.act(a.id, 'start');
  await waitPhase(a.sub, 'PREP', 4000);
  for (const p of all) await api.act(p.id, 'ready', { ready: true });   // 조기 종료
  r.ok('전투 진입', await waitPhase(a.sub, 'BATTLE', 8000));
  await waitFor(() => a.sub.snapshot.storm, 4000);

  const storm = a.sub.snapshot.storm;
  r.ok('자기장 생성', !!storm);
  const battleMs = 60 * 1000;
  const spanOk = Math.abs((storm.endAt - storm.startAt) - battleMs * (cfg.storm.endFrac - cfg.storm.startFrac)) < 1500;
  r.ok('수축 구간 = 전투 시간의 20%→100%', spanOk, `${storm.endAt - storm.startAt}ms`);
  const thirdX = (BF.bounds.maxX - BF.bounds.minX) / 3;
  const thirdY = (BF.bounds.maxY - BF.bounds.minY) / 3;
  r.ok('수렴점이 중앙 구획 안',
    storm.cx >= BF.bounds.minX + thirdX && storm.cx <= BF.bounds.maxX - thirdX
    && storm.cy >= BF.bounds.minY + thirdY && storm.cy <= BF.bounds.maxY - thirdY,
    `(${storm.cx.toFixed(0)}, ${storm.cy.toFixed(0)})`);
  const corners = [[BF.bounds.minX, BF.bounds.minY], [BF.bounds.maxX, BF.bounds.minY],
                   [BF.bounds.minX, BF.bounds.maxY], [BF.bounds.maxX, BF.bounds.maxY]];
  const maxCorner = Math.max(...corners.map(([x, y]) => Math.hypot(x - storm.cx, y - storm.cy)));
  r.ok('처음에는 전장 전체가 안전', a.sub.snapshot.storm.radius >= maxCorner - 1);

  const safeCenter = openPointNearest(storm.cx, storm.cy);
  const corner = openPointNearest(storm.cx < 0 ? BF.bounds.maxX - 30 : BF.bounds.minX + 30,
    storm.cy < 0 ? BF.bounds.maxY - 30 : BF.bounds.minY + 30);

  // ── 보급 (A 가 아직 살아 있는 동안 확인한다) ─────────────────────
  r.ok('보급이 투하됨', !!(await waitFor(() => (b.sub.snapshot.drops || []).length > 0, 12000)));
  const dropsNow = b.sub.snapshot.drops || [];
  r.ok('보급이 전장 안에 떨어짐', dropsNow.every(d =>
    d.x > BF.bounds.minX && d.x < BF.bounds.maxX && d.y > BF.bounds.minY && d.y < BF.bounds.maxY));
  const kinds = new Set(dropsNow.map(d => d.kind));
  r.ok('보급 종류는 재화·힐팩', [...kinds].every(k => k === 'money' || k === 'medkit'),
    [...kinds].join(','));

  // 회수와 구석 이동을 병행한다. 이동 중 다른 보급도 주울 수 있으므로 누적 회수액과 대조한다.
  const bPos = find(b.sub.snapshot, b.id);
  const target = dropsNow.slice().sort((x, y) =>
    Math.hypot(x.x - bPos.x, x.y - bPos.y) - Math.hypot(y.x - bPos.x, y.y - bPos.y))[0];
  if (!target) throw new Error('보급을 관측하지 못해 회수 검사를 진행할 수 없습니다');
  const before = find(b.sub.snapshot, b.id);
  const escape = walkSafely(a, corner);
  const reached = await walkSafely(b, target);
  r.ok('보급 위치에 생존 상태로 도달', reached);
  await waitFor(() => !(b.sub.snapshot.drops || []).some(d => d.id === target.id), 2000);
  const after = find(b.sub.snapshot, b.id);
  r.ok('보급 회수 (밟으면 자동)', b.sub.snapshot.phase === 'BATTLE'
    && !(b.sub.snapshot.drops || []).some(d => d.id === target.id));
  if (target.kind === 'money') {
    const looted = after.looted - before.looted;
    r.ok('재화 상자 → 회수액 전체가 재화에 반영', looted >= target.amount
      && after.money - before.money === looted,
      `money +${after.money - before.money}, looted +${looted}, target ${target.amount}`);
  } else {
    r.ok('힐팩 → 체력 회복 (최대 체력 이하)', after.hp >= before.hp && after.hp <= after.maxHp);
  }
  r.ok('중앙 생존 위치로 복귀', await walkSafely(b, safeCenter));
  r.ok('자기장 밖 검사 위치로 이동', await escape);

  // ── 자기장 피해와 사망 ───────────────────────────────────────────
  await waitFor(() => hurt, 50000, 200);
  r.ok('안전 구역 밖 지속 피해', !!hurt, hurt ? `hp ${hurt.hp}` : '피해 없음');

  await waitFor(() => died, 60000, 200);
  r.ok('자기장 사망', !!died && died.deathCause === 'storm', died ? died.deathCause : '생존');
  r.ok('자기장 사망은 킬을 주지 않음', find(b.sub.snapshot, b.id).kills === 0);

  // 최후 1인 → 조기 종료 (정산이 지나갔을 수도 있으니 이후 페이즈도 허용)
  await waitFor(() => ended, 15000);
  r.ok('최후의 1인으로 조기 종료', !!ended, `phase=${b.sub.snapshot.phase}`);
} catch (err) {
  r.ok('예외 없이 완료', false, String(err && err.stack || err));
}

await finish(r, proc);

// 지형 검증 (문서 13절).
// 벽은 이동·시야를 막고, 스폰 8개는 서로 떨어져 있고, 낙사 구덩이는 밟으면 죽는다.
// 전장은 단일 평면 — 고저차 판정은 없다.
import {
  startServer, makeReporter, makeClient, setupRoom, find, sleep,
  waitPhase, waitFor, walkTo, pushToward, findPath, losClear, segmentHitsRect, finish
} from './test-harness.mjs';

const { proc, base } = await startServer(import.meta.dirname);
const r = makeReporter();
const api = makeClient(base);

const overlaps = (ax, ay, aw, ah, bx, by, bw, bh) =>
  ax < bx + bw && ax + aw > bx && ay < by + bh && ay + ah > by;

try {
  const cfg = await api.get('/config');
  const BF = cfg.maps.battlefield;
  const PL = cfg.maps.plaza;
  const R = cfg.render.playerRadius;

  // ── 정적 검증 ────────────────────────────────────────────────────
  r.ok('전장 스폰 8개 (MVP 상한 대응)', BF.spawns.length === 8);
  r.ok('광장 스폰 8개', PL.spawns.length === 8);

  // 스폰 직후 즉시 공격받지 않도록 서로 떨어져 있다
  let minSpawnDist = Infinity;
  for (let i = 0; i < BF.spawns.length; i++) {
    for (let j = i + 1; j < BF.spawns.length; j++) {
      minSpawnDist = Math.min(minSpawnDist,
        Math.hypot(BF.spawns[i].x - BF.spawns[j].x, BF.spawns[i].y - BF.spawns[j].y));
    }
  }
  r.ok('스폰 간 최소 거리 > 기본 사거리의 2배', minSpawnDist > 52, `${minSpawnDist.toFixed(0)}`);

  // 스폰이 벽·구덩이와 겹치지 않는다
  const spawnClear = BF.spawns.every(sp =>
    !BF.walls.some(w => overlaps(sp.x - R, sp.y - R, R * 2, R * 2, w.x, w.y, w.w, w.h))
    && !BF.pits.some(p => overlaps(sp.x - R, sp.y - R, R * 2, R * 2, p.x, p.y, p.w, p.h)));
  r.ok('스폰이 벽·구덩이와 겹치지 않음', spawnClear);

  // 낙사 구덩이가 존재하고, 경계 안에 있고, 벽과 겹치지 않는다
  r.ok('낙사 구덩이 포함 (문서: 낙사는 포함한다)', BF.pits.length > 0, `${BF.pits.length}개`);
  r.ok('구덩이가 경계 안', BF.pits.every(p =>
    p.x > BF.bounds.minX && p.x + p.w < BF.bounds.maxX
    && p.y > BF.bounds.minY && p.y + p.h < BF.bounds.maxY));
  r.ok('구덩이가 벽과 겹치지 않음', BF.pits.every(p =>
    !BF.walls.some(w => overlaps(p.x, p.y, p.w, p.h, w.x, w.y, w.w, w.h))));
  r.ok('광장에는 구덩이 없음', PL.pits.length === 0);

  // 모든 스폰에서 서로에게 걸어갈 수 있다 (막힌 스폰 없음)
  let connected = true;
  for (let i = 1; i < BF.spawns.length; i++) {
    if (!findPath(BF, R, BF.spawns[0], BF.spawns[i])) { connected = false; break; }
  }
  r.ok('모든 스폰이 연결됨 (끼임 없음)', connected);

  // 시야 판정: 두꺼운 상자 뒤는 가려진다 (같은 거리, 시야만 다른 두 경우)
  const box = BF.walls.find(w => !w.tall && w.w >= 30 && w.h >= 30);
  r.ok('엄폐용 상자 존재', !!box);
  if (box) {
    const cx = box.x + box.w / 2, cy = box.y + box.h / 2;
    const d = box.h / 2 + 16;
    r.ok('상자를 사이에 두면 시야 차단',
      !losClear(BF, cx, cy - d, cx, cy + d));
    r.ok('같은 거리라도 옆으로 비키면 시야 확보',
      losClear(BF, cx + box.w / 2 + 30, cy - d, cx + box.w / 2 + 30, cy + d));
  }

  // 얇은 바리케이드: 시야는 끊지만 두께가 얇다
  const thin = BF.walls.filter(w => !w.tall && Math.min(w.w, w.h) <= 14);
  r.ok('얇은 바리케이드 존재', thin.length >= 4, `${thin.length}개`);

  // ── 실제 플레이 검증 ─────────────────────────────────────────────
  const { host: a, others: [b], all } = await setupRoom(api, ['탐험가', '희생양'],
    { rounds: 1, prepSeconds: 30, battleSeconds: 120 });
  await api.act(a.id, 'start');
  await waitPhase(a.sub, 'PREP', 4000);
  for (const p of all) await api.act(p.id, 'ready', { ready: true });
  r.ok('전투 진입', await waitPhase(a.sub, 'BATTLE', 8000));
  await sleep(300);

  // 전장 스폰은 매 라운드 무작위다: 벽·구덩이 밖, 서로 충분히 떨어져 있고,
  // 고정 스폰 좌표와 일치하지 않는다 (무작위 좌표가 우연히 일치할 확률은 0에 가깝다)
  const pa = find(a.sub.snapshot, a.id), pb = find(b.sub.snapshot, b.id);
  r.ok('랜덤 스폰이 서로 떨어져 있음', Math.hypot(pa.x - pb.x, pa.y - pb.y) >= 60,
    `${Math.hypot(pa.x - pb.x, pa.y - pb.y).toFixed(0)}`);
  const onFixedSpawn = p => BF.spawns.some(sp => Math.hypot(sp.x - p.x, sp.y - p.y) < 1);
  r.ok('고정 스폰 좌표가 아님 (매 라운드 무작위)', !onFixedSpawn(pa) && !onFixedSpawn(pb),
    JSON.stringify([{ x: pa.x, y: pa.y }, { x: pb.x, y: pb.y }]));
  const inHazard = p => BF.pits.some(t => p.x >= t.x && p.x <= t.x + t.w && p.y >= t.y && p.y <= t.y + t.h)
    || BF.walls.some(w => p.x > w.x - R && p.x < w.x + w.w + R && p.y > w.y - R && p.y < w.y + w.h + R);
  r.ok('랜덤 스폰이 벽·구덩이 밖', !inHazard(pa) && !inHazard(pb));

  // 벽은 이동을 막는다: 중앙 십자 구조물 안으로 밀어붙여도 못 들어간다
  const cross = BF.walls[0];   // 중앙 가로 벽
  await walkTo(api, a.id, a.sub, cross.x - 30, cross.y + cross.h / 2, 20000);
  await pushToward(api, a.id, a.sub, cross.x + cross.w / 2, cross.y + cross.h / 2, 3000);
  const pushed = find(a.sub.snapshot, a.id);
  r.ok('벽이 이동을 막음', pushed.x <= cross.x - R + 1,
    `x=${pushed.x.toFixed(1)}, 벽 시작 ${cross.x}`);

  // 낙사: 구덩이 위로 걸어 들어가면 죽는다
  const pit = BF.pits[0];
  const px = pit.x + pit.w / 2, py = pit.y + pit.h / 2;
  await walkTo(api, b.id, b.sub, px, pit.y + pit.h + 20, 25000);   // 구덩이 바로 아래까지
  await pushToward(api, b.id, b.sub, px, py, 5000);                 // 안으로 밀어넣는다
  const fell = await waitFor(() => {
    const p = find(b.sub.snapshot, b.id);
    return p && !p.alive ? p : null;
  }, 6000);
  r.ok('구덩이에 들어가면 낙사', !!fell && fell.deathCause === 'fall',
    fell ? fell.deathCause : '생존');
  r.ok('낙사는 킬을 주지 않음', find(a.sub.snapshot, a.id).kills === 0);

  // 최후 1인 → 조기 종료
  r.ok('낙사 후 최후의 1인 조기 종료', await waitPhase(a.sub, 'RESULT', 8000));
} catch (err) {
  r.ok('예외 없이 완료', false, String(err && err.stack || err));
}

await finish(r, proc);

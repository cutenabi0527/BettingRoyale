// 준비 광장 스테이션 검증 (문서 7절·10절·11절).
// 모루: 확률 강화 — 재화 선차감, 실패 시 단계 유지. 거리 제약.
// 상점: 장신구 랜덤 뽑기 — 즉시 장착·무조건 교체. 확률표 공개.
// 허수아비: 리치·데미지 확인.
import {
  startServer, makeReporter, makeClient, setupRoom, find, sleep,
  waitPhase, walkTo, finish
} from './test-harness.mjs';

const { proc, base } = await startServer(import.meta.dirname);
const r = makeReporter();
const api = makeClient(base);

try {
  const cfg = await api.get('/config');

  // ── 규칙표 자체 검증 ─────────────────────────────────────────────
  r.ok('강화 슬롯 = 무기·방어구 (장신구 제외)',
    JSON.stringify(cfg.anvil.slots) === '["weapon","armor"]');
  r.ok('강화 5단계', cfg.anvil.maxEnhance === 5);
  r.ok('강화 확률이 단계마다 정의됨', cfg.anvil.chance.length === 5
    && cfg.anvil.chance.every(c => c > 0 && c < 1));
  r.ok('강화 확률은 단계가 오를수록 낮아짐',
    cfg.anvil.chance.every((c, i) => i === 0 || c <= cfg.anvil.chance[i - 1]));
  r.ok('강화 비용은 단계가 오를수록 비싸짐',
    cfg.anvil.costTable.weapon.every((c, i) => i === 0 || c > cfg.anvil.costTable.weapon[i - 1]));
  r.ok('무기·방어구는 구매 불가', cfg.gear.weapon.purchasable === false
    && cfg.gear.armor.purchasable === false);

  const gradeSum = {};
  for (const item of cfg.trinkets.items) {
    gradeSum[item.grade] = (gradeSum[item.grade] || 0) + item.chance;
  }
  r.ok('장신구 3종류 × 3등급 = 9종', cfg.trinkets.items.length === 9);
  r.ok('장신구 뽑기 확률 합 = 100%',
    Math.abs(cfg.trinkets.items.reduce((a, i) => a + i.chance, 0) - 1) < 1e-9);
  r.ok('일반 > 희귀 > 영웅 확률 순서',
    gradeSum.common > gradeSum.rare && gradeSum.rare > gradeSum.epic,
    JSON.stringify(gradeSum));
  r.ok('장신구 효과는 대시 유틸리티만',
    cfg.trinkets.items.every(i => i.charges >= 0 && i.recharge <= 0));

  const stations = cfg.maps.plaza.stations;
  r.ok('광장 스테이션: 룰렛·상점·모루·순위판·허수아비',
    ['wheel', 'shop', 'anvil', 'board', 'dummy'].every(kind =>
      stations.some(s => s.kind === kind)),
    stations.map(s => s.kind).join(','));

  // ── 실제 플레이 검증 ─────────────────────────────────────────────
  const { host: a, all } = await setupRoom(api, ['강화왕', '구경꾼'],
    { rounds: 1, prepSeconds: 300, battleSeconds: 30, baseMoney: 5000 });
  await api.act(a.id, 'start');
  r.ok('준비 페이즈 진입', await waitPhase(a.sub, 'PREP', 4000));
  await sleep(300);

  // 거리 제약: 모루에서 멀리 있으면 강화가 거부된다
  const before = find(a.sub.snapshot, a.id).money;
  await api.act(a.id, 'enhance', { slot: 'weapon' });
  await sleep(250);
  r.ok('모루에서 멀면 강화 불가 (재화 그대로)', find(a.sub.snapshot, a.id).money === before);

  // 모루로 걸어가서 강화를 반복한다
  const anvil = stations.find(s => s.kind === 'anvil');
  r.ok('모루 도착', await walkTo(api, a.id, a.sub, anvil.x, anvil.y + anvil.reach - 10, 20000));

  let tries = 0, fails = 0, spentTotal = 0;
  for (let i = 0; i < 8; i++) {
    const me = find(a.sub.snapshot, a.id);
    const level = me.enhance.weapon;
    if (level >= cfg.anvil.maxEnhance) break;
    const cost = cfg.anvil.costTable.weapon[level];
    if (me.money < cost) break;
    await api.act(a.id, 'enhance', { slot: 'weapon' });
    await sleep(250);
    const after = find(a.sub.snapshot, a.id);
    tries += 1;
    spentTotal += cost;
    // 성공이든 실패든 재화는 비용만큼 차감된다
    if (after.money !== me.money - cost) {
      r.ok(`강화 ${i + 1}회차 재화 선차감`, false, `${me.money} - ${cost} ≠ ${after.money}`);
      break;
    }
    // 실패하면 단계가 그대로다
    if (after.enhance.weapon === level) fails += 1;
    else if (after.enhance.weapon !== level + 1) {
      r.ok('강화는 1단계씩만 오름', false, `${level} → ${after.enhance.weapon}`);
      break;
    }
  }
  const enhanced = find(a.sub.snapshot, a.id);
  r.ok(`강화 ${tries}회 시도 (실패 ${fails}회) · 재화 선차감 유지`, tries > 0);
  r.ok('시도·실패 횟수가 통계로 기록됨',
    enhanced.attempts.weapon === tries && enhanced.fails.weapon === fails,
    JSON.stringify([enhanced.attempts, enhanced.fails]));
  r.ok('단계 = 시도 - 실패', enhanced.enhance.weapon === tries - fails);

  // 상점: 장신구 뽑기 — 즉시 장착, 두 번째 뽑기는 무조건 교체
  const shop = stations.find(s => s.kind === 'shop');
  r.ok('상점 도착', await walkTo(api, a.id, a.sub, shop.x, shop.y + shop.reach - 10, 20000));
  const beforePull = find(a.sub.snapshot, a.id).money;
  await api.act(a.id, 'pull');
  await sleep(250);
  const p1 = find(a.sub.snapshot, a.id);
  r.ok('뽑기 즉시 장착', !!p1.trinket, JSON.stringify(p1.trinket));
  r.ok('뽑기 비용 차감', p1.money === beforePull - cfg.trinkets.price);
  r.ok('뽑힌 장신구가 확률표의 항목', cfg.trinkets.items.some(i => i.key === p1.trinket.key));
  const firstKey = p1.trinket.key;
  await api.act(a.id, 'pull');
  await sleep(250);
  const p2 = find(a.sub.snapshot, a.id);
  r.ok('두 번째 뽑기도 즉시 장착·교체 (보류 없음)', !!p2.trinket
    && p2.money === beforePull - cfg.trinkets.price * 2,
    `첫 ${firstKey} → ${p2.trinket?.key}`);

  // 허수아비: 준비 중 공격으로 리치·데미지를 확인한다
  const dummy = stations.find(s => s.kind === 'dummy');
  r.ok('허수아비 도착', await walkTo(api, a.id, a.sub, dummy.x, dummy.y + dummy.radius + 14, 20000));
  const meNow = find(a.sub.snapshot, a.id);
  await api.act(a.id, 'move', { x: meNow.x, y: meNow.y,
    angle: Math.atan2(dummy.y - meNow.y, dummy.x - meNow.x) });
  await api.act(a.id, 'attack');
  await sleep(250);
  const hit = find(a.sub.snapshot, a.id).dummyHit;
  r.ok('허수아비 타격으로 피해·사거리 확인', !!hit && hit.damage > 0 && hit.range > 0,
    JSON.stringify(hit));
  r.ok('강화가 피해에 반영됨',
    hit && hit.damage === cfg.gear.weapon.base.damage + enhanced.enhance.weapon * 5);
} catch (err) {
  r.ok('예외 없이 완료', false, String(err && err.stack || err));
}

await finish(r, proc);

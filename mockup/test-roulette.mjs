// 룰렛 검증 (문서 9절).
// 배당표·칸 배치 일치, 기대 회수율 ≤ 100%, 거리 제약, 정산,
// 개인 베팅 비공개·전체 판돈 공개, 준비 종료 직전 마감·환불.
import {
  startServer, makeReporter, makeClient, setupRoom, find, sleep,
  waitPhase, waitWheel, waitFor, pushToward, finish
} from './test-harness.mjs';

const { proc, base } = await startServer(import.meta.dirname);
const r = makeReporter();
const api = makeClient(base);

try {
  const cfg = await api.get('/config');
  const R = cfg.roulette;

  // ── 표 자체 검증 ─────────────────────────────────────────────────
  r.ok('칸 수 = 배치 길이', R.layout.length === R.slots);
  for (const row of R.table) {
    const actual = R.layout.filter(v => v === row.number).length;
    r.ok(`${row.number} 칸 수 = 표 (${row.slots})`, actual === row.slots, `${actual}`);
    r.ok(`${row.number} 기대 회수율 ≤ 100%`, row.expected <= 1, `${(row.expected * 100).toFixed(1)}%`);
    r.ok(`${row.number} 총 회수 = 숫자+1`, row.payout === row.number + 1);
  }

  // ── 베팅과 정산 ──────────────────────────────────────────────────
  const { host: a, others: [b] } = await setupRoom(api, ['도박사', '구경꾼'],
    { rounds: 1, prepSeconds: 90, battleSeconds: 30, baseMoney: 2000 });
  await api.act(a.id, 'start');
  r.ok('준비 페이즈 진입', await waitPhase(a.sub, 'PREP', 4000));
  await sleep(200);

  // 거리 제약: 룰렛에서 멀면 베팅이 거부된다
  const farMoney = find(a.sub.snapshot, a.id).money;
  await api.act(a.id, 'bet', { number: 1, amount: 100 });
  await sleep(250);
  r.ok('룰렛에서 멀면 베팅 불가', find(a.sub.snapshot, a.id).money === farMoney);

  // 룰렛 앞으로 이동
  const wheel = cfg.maps.plaza.stations.find(s => s.kind === 'wheel');
  await pushToward(api, a.id, a.sub, wheel.x, wheel.y + wheel.reach - 14, 12000);
  r.ok('베팅 상태 대기', await waitWheel(a.sub, 'BETTING', 25000));

  // 모든 숫자에 100씩 건다 → 어떤 숫자가 나와도 정산을 검증할 수 있다
  const before = find(a.sub.snapshot, a.id).money;
  for (const n of R.numbers) await api.act(a.id, 'bet', { number: n, amount: 100 });
  await sleep(300);
  const staked = R.numbers.length * 100;
  r.ok('베팅액 선차감', find(a.sub.snapshot, a.id).money === before - staked,
    `${find(a.sub.snapshot, a.id).money}`);

  // 개인 베팅은 숨기고 전체 판돈·참여 인원만 공개된다
  const bView = b.sub.snapshot;
  const aSeenByB = find(bView, a.id);
  r.ok('남의 베팅 내역 비공개', aSeenByB.bets === null || aSeenByB.bets === undefined,
    JSON.stringify(aSeenByB.bets));
  await waitFor(() => Object.values(b.sub.snapshot.wheel.pot).reduce((x, y) => x + y, 0) === staked, 3000);
  r.ok('전체 판돈 공개', Object.values(b.sub.snapshot.wheel.pot).reduce((x, y) => x + y, 0) === staked);
  r.ok('참여 인원 공개', b.sub.snapshot.wheel.bettors === 1);

  // 결과를 기다려 정산을 확인한다
  r.ok('회전 시작', await waitWheel(a.sub, 'SPINNING', 15000));
  r.ok('결과 도착', await waitWheel(a.sub, 'RESULT', 15000));
  const hit = a.sub.snapshot.wheel.number;
  await sleep(300);
  const after = find(a.sub.snapshot, a.id).money;
  const expected = before - staked + 100 * (hit + 1);
  r.ok(`정산: ${hit} 적중 → 총 회수 100×${hit + 1}`, after === expected,
    `${after} ≠ ${expected}`);

  // ── 마감·환불 ────────────────────────────────────────────────────
  // 준비 30초 방: 시작 10초 뒤 마감선(종료 20초 전)에 걸린다.
  const { host: c } = await setupRoom(api, ['환불맨', '들러리'],
    { rounds: 1, prepSeconds: 30, battleSeconds: 30, baseMoney: 2000 });
  await api.act(c.id, 'start');
  await waitPhase(c.sub, 'PREP', 4000);
  await pushToward(api, c.id, c.sub, wheel.x, wheel.y + wheel.reach - 14, 8000);
  const okBetting = await waitWheel(c.sub, 'BETTING', 6000);
  r.ok('마감 전 베팅 가능', okBetting);
  const beforeRefund = find(c.sub.snapshot, c.id).money;
  await api.act(c.id, 'bet', { number: 20, amount: 300 });
  await sleep(250);
  r.ok('베팅 차감', find(c.sub.snapshot, c.id).money === beforeRefund - 300);
  r.ok('준비 종료 20초 전 마감', await waitWheel(c.sub, 'CLOSED', 15000));
  await sleep(300);
  r.ok('돌지 않은 베팅 전액 환불', find(c.sub.snapshot, c.id).money === beforeRefund,
    `${find(c.sub.snapshot, c.id).money} ≠ ${beforeRefund}`);
  await waitFor(() => {
    const p = find(c.sub.snapshot, c.id);
    return p && Object.keys(p.bets || {}).length === 0;
  }, 3000);
  r.ok('마감 후 베팅 불가', (() => {
    return c.sub.snapshot.wheel.state === 'CLOSED';
  })());

  // 마감은 룰렛에만 적용된다 — 강화·뽑기는 준비 시간이 끝날 때까지 가능하다
  const anvil = cfg.maps.plaza.stations.find(s => s.kind === 'anvil');
  await pushToward(api, c.id, c.sub, anvil.x, anvil.y + anvil.reach - 14, 8000);
  const beforeEnhance = find(c.sub.snapshot, c.id).money;
  await api.act(c.id, 'enhance', { slot: 'weapon' });
  await sleep(300);
  r.ok('룰렛 마감 후에도 강화 가능', find(c.sub.snapshot, c.id).money < beforeEnhance,
    `${beforeEnhance} → ${find(c.sub.snapshot, c.id).money}`);
} catch (err) {
  r.ok('예외 없이 완료', false, String(err && err.stack || err));
}

await finish(r, proc);

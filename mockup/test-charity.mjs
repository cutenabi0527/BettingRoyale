// 적선·구걸 검증 (문서 8절).
// 재화 이전은 적선뿐이고, 거리 제약이 있으며, 내역은 전원에게 공개된다.
// 재화 총량은 보존된다 — 적선은 만들어내지 않고 옮긴다.
import {
  startServer, makeReporter, makeClient, setupRoom, find, sleep,
  waitPhase, waitFor, pushToward, finish
} from './test-harness.mjs';

const { proc, base } = await startServer(import.meta.dirname);
const r = makeReporter();
const api = makeClient(base);

const totalMoney = snap => snap.players.reduce((a, p) => a + (p.money ?? 0), 0);

try {
  const cfg = await api.get('/config');
  const C = cfg.charity;

  const { host: a, others: [b] } = await setupRoom(api, ['부자', '거지'],
    { rounds: 1, prepSeconds: 300, battleSeconds: 30, baseMoney: 1000 });
  await api.act(a.id, 'start');
  r.ok('준비 페이즈 진입', await waitPhase(a.sub, 'PREP', 4000));
  await sleep(300);

  // ── 구걸 ─────────────────────────────────────────────────────────
  await api.act(b.id, 'beg');
  await sleep(300);
  r.ok('구걸이 광장 전체에 공개됨',
    a.sub.snapshot.announcements.some(x => x.tone === 'beg' && x.text.includes('거지')));
  r.ok('구걸 말풍선 표시', find(a.sub.snapshot, b.id).begUntil > Date.now() - 1000);

  // 쿨다운: 연달아 구걸해도 알림이 한 번만 는다
  const begCount = a.sub.snapshot.announcements.filter(x => x.tone === 'beg').length;
  await api.act(b.id, 'beg');
  await sleep(250);
  r.ok('구걸 쿨다운', a.sub.snapshot.announcements.filter(x => x.tone === 'beg').length === begCount);

  // ── 거리 제약 ────────────────────────────────────────────────────
  // 스폰이 흩어져 있으므로 우선 멀리 떨어뜨린다
  await pushToward(api, a.id, a.sub, -180, 140, 6000);
  await pushToward(api, b.id, b.sub, 180, 140, 6000);
  const aFar = find(a.sub.snapshot, a.id);
  await api.act(a.id, 'give', { target: b.id, amount: 300 });
  await sleep(250);
  r.ok('멀리서는 적선 불가', find(a.sub.snapshot, a.id).money === aFar.money);

  // ── 적선 ─────────────────────────────────────────────────────────
  await pushToward(api, a.id, a.sub, 150, 140, 10000);   // B 옆까지 걸어간다
  await waitFor(() => {
    const pa = find(a.sub.snapshot, a.id), pb = find(a.sub.snapshot, b.id);
    return Math.hypot(pa.x - pb.x, pa.y - pb.y) <= C.reach;
  }, 6000);

  // 관전자가 아니고 거리 안 — 이제 총량 보존을 확인하며 넘긴다.
  // (준비 중 남의 지갑은 비공개이므로 각자 자기 스냅샷에서 읽는다)
  const aBefore = find(a.sub.snapshot, a.id).money;
  const bBefore = find(b.sub.snapshot, b.id).money;
  await api.act(a.id, 'give', { target: b.id, amount: 300 });
  await sleep(300);
  const aAfter = find(a.sub.snapshot, a.id).money;
  const bAfter = find(b.sub.snapshot, b.id).money;
  r.ok('적선: 보낸 만큼 줄어듦', aAfter === aBefore - 300, `${aBefore} → ${aAfter}`);
  r.ok('적선: 받은 만큼 늘어남', bAfter === bBefore + 300, `${bBefore} → ${bAfter}`);
  r.ok('재화 총량 보존', aBefore + bBefore === aAfter + bAfter);
  r.ok('적선 내역이 전원에게 공개됨',
    b.sub.snapshot.announcements.some(x => x.tone === 'alms' && x.text.includes('300')));
  r.ok('누적 적선/수령 통계 공개',
    find(b.sub.snapshot, a.id).given === 300 && find(a.sub.snapshot, b.id).received === 300);
  r.ok('받으면 구걸 말풍선이 내려감', find(a.sub.snapshot, b.id).begUntil === 0);

  // 최소 금액 미만은 거부, 잔액 초과는 전액으로 잘림
  await api.act(a.id, 'give', { target: b.id, amount: C.minGive - 1 });
  await sleep(250);
  r.ok('최소 금액 미만 거부', find(a.sub.snapshot, a.id).money === aAfter);
  await api.act(a.id, 'give', { target: b.id, amount: 999999 });
  await sleep(300);
  r.ok('잔액 초과는 전액으로 잘림', find(a.sub.snapshot, a.id).money === 0
    && find(b.sub.snapshot, b.id).money === bAfter + aAfter);

  // 자기 자신에게는 적선 불가
  const bNow = find(b.sub.snapshot, b.id).money;
  await api.act(b.id, 'give', { target: b.id, amount: 100 });
  await sleep(250);
  r.ok('자기 자신에게 적선 불가', find(b.sub.snapshot, b.id).money === bNow);
} catch (err) {
  r.ok('예외 없이 완료', false, String(err && err.stack || err));
}

await finish(r, proc);

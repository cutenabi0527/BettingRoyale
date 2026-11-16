// 이동 검증. 서버는 정상 이동을 되돌리지 않고(오차 0), 순간이동은 속도로 자르고,
// 대시 직후의 낡은 이동 패킷은 대시를 취소하지 못한다.
import {
  startServer, makeReporter, makeClient, setupRoom, find, sleep,
  waitPhase, waitFor, finish
} from './test-harness.mjs';

const { proc, base } = await startServer(import.meta.dirname);
const r = makeReporter();
const api = makeClient(base);

try {
  const { host: a } = await setupRoom(api, ['워커', '들러리'],
    { rounds: 1, prepSeconds: 300, battleSeconds: 30 });
  await api.act(a.id, 'start');
  r.ok('준비 페이즈 진입', await waitPhase(a.sub, 'PREP', 4000));
  await sleep(300);

  // 정상 속도 이동은 그대로 받아들여진다
  let me = find(a.sub.snapshot, a.id);
  let worst = 0;
  for (let i = 0; i < 12; i++) {
    const tx = me.x, ty = me.y - 2.5;                 // 위쪽으로 잘게 이동
    await api.act(a.id, 'move', { x: tx, y: ty - 0.0001, angle: -Math.PI / 2 });
    await sleep(160);                                 // 스냅샷 틱(100ms)보다 길게 기다린다
    me = find(a.sub.snapshot, a.id);
    worst = Math.max(worst, Math.hypot(me.x - tx, me.y - (ty - 0.0001)));
  }
  r.ok('정상 이동은 서버가 되돌리지 않음 (오차 ≈ 0)', worst < 0.01, `최대 오차 ${worst.toFixed(4)}`);

  // 순간이동은 속도 한도로 잘린다
  const from = find(a.sub.snapshot, a.id);
  await api.act(a.id, 'move', { x: from.x + 200, y: from.y, angle: 0 });
  await sleep(200);
  const clipped = find(a.sub.snapshot, a.id);
  const moved = Math.hypot(clipped.x - from.x, clipped.y - from.y);
  r.ok('순간이동이 속도 한도로 잘림', moved < 60, `이동량 ${moved.toFixed(1)}`);

  // 대시: 위치가 크게 이동하고, 직후의 낡은 좌표 패킷이 대시를 되돌리지 못한다
  const beforeDash = find(a.sub.snapshot, a.id);
  await api.act(a.id, 'dash', { dir: 0 });            // 오른쪽으로 대시
  await api.act(a.id, 'move', { x: beforeDash.x, y: beforeDash.y, angle: 0 });  // 낡은 패킷
  await sleep(250);
  const afterDash = find(a.sub.snapshot, a.id);
  r.ok('대시로 순간 이동', afterDash.x > beforeDash.x + 10,
    `${beforeDash.x.toFixed(1)} → ${afterDash.x.toFixed(1)}`);

  // 대시 충전은 소모되고 재충전된다
  const meDash = find(a.sub.snapshot, a.id);
  r.ok('대시 충전 소모', meDash.dashCharges < meDash.maxDashCharges
    || meDash.maxDashCharges === 0
    || await waitFor(() => {
      const p = find(a.sub.snapshot, a.id);
      return p.dashCharges === p.maxDashCharges;
    }, 4000));
  r.ok('대시 재충전', await waitFor(() => {
    const p = find(a.sub.snapshot, a.id);
    return p.dashCharges >= p.maxDashCharges;
  }, 5000));
} catch (err) {
  r.ok('예외 없이 완료', false, String(err && err.stack || err));
}

await finish(r, proc);

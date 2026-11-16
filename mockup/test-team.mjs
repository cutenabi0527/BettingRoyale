// 팀전 검증 (문서 1절·8절·12절·17절).
// 팀은 승리 목표와 전투 협력 단위일 뿐, 재화는 개인 소유다.
// 아군 공격은 기본 꺼짐, 최후의 1팀이 남으면 조기 종료, 팀 승수가 집계된다.
import {
  startServer, makeReporter, makeClient, setupRoom, find, sleep,
  waitPhase, waitFor, walkTo, finish
} from './test-harness.mjs';

const { proc, base } = await startServer(import.meta.dirname);
const r = makeReporter();
const api = makeClient(base);

// 사거리 밖이면 다가가면서 때린다 — 제자리 공격은 walkTo 오차만큼 빗나갈 수 있다.
async function beatDown(attacker, targetId, ms = 30000) {
  const start = Date.now();
  while (Date.now() - start < ms) {
    const me = find(attacker.sub.snapshot, attacker.id);
    const foe = find(attacker.sub.snapshot, targetId);
    if (!me || !foe || !foe.alive) return true;
    const dx = foe.x - me.x, dy = foe.y - me.y, d = Math.hypot(dx, dy);
    const angle = Math.atan2(dy, dx);
    const step = Math.min(Math.max(0, d - 12), 7);
    await api.act(attacker.id, 'move',
      { x: me.x + (d ? dx / d * step : 0), y: me.y + (d ? dy / d * step : 0), angle });
    await api.act(attacker.id, 'attack');
    await sleep(140);
  }
  return !find(attacker.sub.snapshot, targetId)?.alive;
}

try {
  const cfg = await api.get('/config');

  // 2v2: 인간 4명
  const { host: a, others: [b, c, d], all } = await setupRoom(api,
    ['알파1', '알파2', '브라보1', '브라보2'],
    { mode: 'team', teamSize: 2, rounds: 1, prepSeconds: 30, battleSeconds: 120, baseMoney: 1000 });

  r.ok('팀전 모드 설정', a.sub.snapshot.settings.mode === 'team');
  r.ok('아군 공격 기본 꺼짐', a.sub.snapshot.settings.friendlyFire === false);

  const teams = new Map(a.sub.snapshot.players.map(p => [p.id, p.team]));
  const t0 = [...teams.values()].filter(t => t === 0).length;
  const t1 = [...teams.values()].filter(t => t === 1).length;
  r.ok('팀이 2 대 2 로 배정됨', t0 === 2 && t1 === 2, `${t0} vs ${t1}`);
  r.ok('팀 스냅샷 제공', Array.isArray(a.sub.snapshot.teams) && a.sub.snapshot.teams.length === 2);

  await api.act(a.id, 'start');
  await waitPhase(a.sub, 'PREP', 4000);
  await sleep(300);
  for (const p of all) await api.act(p.id, 'ready', { ready: true });
  r.ok('전투 진입', await waitPhase(a.sub, 'BATTLE', 8000));
  await sleep(300);

  // 같은 팀 / 다른 팀 참가자를 구분한다
  const myTeam = teams.get(a.id);
  const mate = [b, c, d].find(p => teams.get(p.id) === myTeam);
  const foes = [b, c, d].filter(p => teams.get(p.id) !== myTeam);
  r.ok('아군 1명, 적군 2명', !!mate && foes.length === 2);

  // 아군 공격 꺼짐: 아군을 때려도 체력이 줄지 않는다
  await walkTo(api, a.id, a.sub, -120, 0, 25000);
  await walkTo(api, mate.id, mate.sub, -110, 0, 25000);
  const mateHp = find(a.sub.snapshot, mate.id).hp;
  for (let i = 0; i < 6; i++) {
    const me = find(a.sub.snapshot, a.id);
    const foe = find(a.sub.snapshot, mate.id);
    await api.act(a.id, 'move', { x: me.x, y: me.y, angle: Math.atan2(foe.y - me.y, foe.x - me.x) });
    await api.act(a.id, 'attack');
    await sleep(160);
  }
  r.ok('아군 공격이 막힘', find(a.sub.snapshot, mate.id).hp === mateHp,
    `${mateHp} → ${find(a.sub.snapshot, mate.id).hp}`);

  // 적군은 때릴 수 있다: A+아군이 적군 2명을 처치하면 최후의 1팀 조기 종료
  await walkTo(api, foes[0].id, foes[0].sub, -95, 0, 25000);
  r.ok('적 1 처치', await beatDown(a, foes[0].id));

  // ── 유령 모드: 사망자는 계속 움직일 수 있고, 산 자에게는 위치가 감춰진다 ──
  await sleep(300);
  const ghostSelf = find(foes[0].sub.snapshot, foes[0].id);
  r.ok('사망자 본인에게는 자기 위치가 보임', ghostSelf.x !== null);
  // 벽 안쪽 좌표로 이동을 시도한다 — 유령은 벽을 통과한다
  const wallX = 0, wallY = 0;   // 중앙 십자 구조물 한가운데
  for (let i = 0; i < 30; i++) {
    const g = find(foes[0].sub.snapshot, foes[0].id);
    const dx = wallX - g.x, dy = wallY - g.y, d = Math.hypot(dx, dy);
    if (d < 6) break;
    const step = Math.min(d, 7);
    await api.act(foes[0].id, 'move', { x: g.x + dx / d * step, y: g.y + dy / d * step, angle: 0 });
    await sleep(70);
  }
  const ghostNow = find(foes[0].sub.snapshot, foes[0].id);
  r.ok('유령이 이동함', Math.hypot(ghostNow.x - ghostSelf.x, ghostNow.y - ghostSelf.y) > 20,
    `${ghostSelf.x.toFixed(0)},${ghostSelf.y.toFixed(0)} → ${ghostNow.x.toFixed(0)},${ghostNow.y.toFixed(0)}`);
  r.ok('유령이 벽을 통과함 (중앙 구조물 안)', Math.hypot(ghostNow.x, ghostNow.y) < 20,
    `(${ghostNow.x.toFixed(0)}, ${ghostNow.y.toFixed(0)})`);
  r.ok('살아있는 유저에게는 유령 위치가 감춰짐', find(a.sub.snapshot, foes[0].id).x === null);
  r.ok('생존 여부·킬 기록은 계속 공개됨', find(a.sub.snapshot, foes[0].id).alive === false);
  await walkTo(api, foes[1].id, foes[1].sub, -95, 0, 25000);
  r.ok('적 2 처치', await beatDown(a, foes[1].id));

  r.ok('최후의 1팀 조기 종료', await waitPhase(a.sub, 'RESULT', 10000));
  r.ok('라운드 승리 팀 집계', a.sub.snapshot.roundWinnerTeam === myTeam,
    `${a.sub.snapshot.roundWinnerTeam} ≠ ${myTeam}`);

  // 최종: 팀 승수와 개인 점수가 따로 보인다
  r.ok('최종 정산 진입', await waitPhase(a.sub, 'FINISH', 20000));
  const final = a.sub.snapshot.final;
  r.ok('팀 결과 표 존재', !!final && Array.isArray(final.teams) && final.teams.length === 2);
  r.ok('승리 팀이 1승', final.teams[0].wins === 1 && final.teams[0].index === myTeam,
    JSON.stringify(final.teams));
  r.ok('개인 점수는 개인 단위 유지', final.rows.length === 4
    && final.rows.every(row => typeof row.score === 'number'));
  r.ok('결합 방식 미결정 안내', typeof final.teamNote === 'string');
} catch (err) {
  r.ok('예외 없이 완료', false, String(err && err.stack || err));
}

await finish(r, proc);

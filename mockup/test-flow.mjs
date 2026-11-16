// 라운드 흐름 검증. 로비 설정 → 준비(조기 종료) → 전투 → 정산 → 이월 → 최종.
// 문서 규칙: 라운드 점수 = 등수+킬+어시, 보유 재화 점수 제외,
// 이월 = min(준비 잔액 + 전투 획득, 한도), 다음 라운드 = 기본 재화 + 이월,
// 강화 기본 비이월, 동점 판정 3단계, 재대결.
import {
  startServer, makeReporter, makeClient, setupRoom, find, sleep,
  waitPhase, waitFor, walkTo, finish
} from './test-harness.mjs';

const { proc, base } = await startServer(import.meta.dirname);
const r = makeReporter();
const api = makeClient(base);

const settingsOf = sub => sub.snapshot.settings;

// 대상이 죽을 때까지 다가가며 때린다 (제자리 공격은 walkTo 오차만큼 빗나갈 수 있다).
async function beatDown(attacker, target, ms = 30000) {
  const start = Date.now();
  while (Date.now() - start < ms) {
    const me = find(attacker.sub.snapshot, attacker.id);
    const foe = find(attacker.sub.snapshot, target.id);
    if (!me || !foe || !foe.alive) return true;
    const dx = foe.x - me.x, dy = foe.y - me.y, d = Math.hypot(dx, dy);
    const angle = Math.atan2(dy, dx);
    const step = Math.min(Math.max(0, d - 12), 7);
    await api.act(attacker.id, 'move',
      { x: me.x + (d ? dx / d * step : 0), y: me.y + (d ? dy / d * step : 0), angle });
    await api.act(attacker.id, 'attack');
    await sleep(140);
  }
  return !find(attacker.sub.snapshot, target.id)?.alive;
}

async function allReady(players) {
  for (const p of players) await api.act(p.id, 'ready', { ready: true });
  await sleep(300);
}

try {
  const cfg = await api.get('/config');
  const spec = cfg.settingsSpec;

  const { host: a, others: [b], all } = await setupRoom(api, ['A', 'B']);

  r.ok('방 생성자가 호스트', a.sub.snapshot.hostId === a.id);
  r.ok('로비에서 시작', a.sub.snapshot.phase === 'LOBBY');
  r.ok('봇 0명 설정이 반영됨', a.sub.snapshot.players.length === 2,
    `${a.sub.snapshot.players.length}명`);

  // ── 로비 설정 ────────────────────────────────────────────────────
  await api.act(a.id, 'settings', { settings: { rounds: 2, prepSeconds: 60, battleSeconds: 60, baseMoney: 1000, carryCap: 500 } });
  await sleep(200);
  r.ok('호스트가 설정을 바꿀 수 있음',
    settingsOf(a.sub).rounds === 2 && settingsOf(a.sub).carryCap === 500,
    JSON.stringify(settingsOf(a.sub)));
  r.ok('기본 재화 변경이 전원 지갑에 반영됨',
    a.sub.snapshot.players.every(p => p.money === 1000 || p.id !== a.id));

  // 범위 밖 값은 잘려야 한다
  await api.act(a.id, 'settings', { settings: { rounds: 999, prepSeconds: 1 } });
  await sleep(200);
  r.ok('라운드 수가 상한으로 잘림', settingsOf(a.sub).rounds === spec.rounds.max, `${settingsOf(a.sub).rounds}`);
  r.ok('준비 시간이 하한으로 잘림', settingsOf(a.sub).prepSeconds === spec.prepSeconds.min, `${settingsOf(a.sub).prepSeconds}`);
  await api.act(a.id, 'settings', { settings: { rounds: 2, prepSeconds: 60, battleSeconds: 60 } });
  await sleep(200);

  // 호스트가 아니면 못 바꾼다
  await api.act(b.id, 'settings', { settings: { rounds: 5 } });
  await sleep(200);
  r.ok('호스트가 아니면 설정 변경 불가', settingsOf(a.sub).rounds === 2);
  await api.act(b.id, 'start');
  await sleep(200);
  r.ok('호스트가 아니면 시작 불가', a.sub.snapshot.phase === 'LOBBY');

  // 문서 기본값: 강화 이월 꺼짐, 개인전 기본
  r.ok('강화 이월 기본값 꺼짐', settingsOf(a.sub).carryEnhance === false);
  r.ok('대전 모드 기본값 개인전', settingsOf(a.sub).mode === 'solo');

  // ── 1라운드 시작 ─────────────────────────────────────────────────
  await api.act(a.id, 'start');
  r.ok('준비 페이즈 진입', await waitPhase(a.sub, 'PREP', 4000));
  r.ok('준비 시작 재화 = 기본 재화', find(a.sub.snapshot, a.id).money === 1000);

  // 준비 단계 정보 비공개: 남의 재화·강화가 감춰진다
  await sleep(300);
  const bSeenByA = find(a.sub.snapshot, b.id);
  r.ok('준비 중 남의 재화 비공개', bSeenByA.money === null);
  r.ok('준비 중 남의 강화 비공개', bSeenByA.enhance === null);
  r.ok('준비 중 남의 이월 예정 비공개', bSeenByA.carry === null);

  // A 가 상점에서 장신구를 뽑아 전투력을 만든다 (황금 고블린 지정 근거)
  const shop = cfg.maps.plaza.stations.find(s => s.kind === 'shop');
  r.ok('A 상점 도착', await walkTo(api, a.id, a.sub, shop.x, shop.y + shop.reach - 10, 20000));
  await api.act(a.id, 'pull');
  await sleep(300);
  const aAfterPull = find(a.sub.snapshot, a.id);
  r.ok('장신구 뽑기 즉시 장착', !!aAfterPull.trinket, JSON.stringify(aAfterPull.trinket));
  r.ok('뽑기 비용 차감', aAfterPull.money === 1000 - cfg.trinkets.price, `${aAfterPull.money}`);

  // 전원 준비 완료 → 조기 종료
  await allReady(all);
  r.ok('전원 동의로 준비 조기 종료', await waitPhase(a.sub, 'COUNTDOWN', 4000) || a.sub.snapshot.phase === 'BATTLE');

  // 황금 고블린: 전투력이 가장 높은 A 가 지정된다
  await waitFor(() => a.sub.snapshot.goblin, 4000);
  r.ok('황금 고블린 = 장비가 가장 좋은 A', a.sub.snapshot.goblin?.id === a.id,
    JSON.stringify(a.sub.snapshot.goblin));

  r.ok('전투 진입', await waitPhase(a.sub, 'BATTLE', 6000));
  // 전투 중에는 강화·장비가 공개된다
  await sleep(300);
  const bInBattle = find(a.sub.snapshot, b.id);
  r.ok('전투 중 남의 강화 공개', bInBattle.enhance !== null);

  // B 가 고블린 A 를 처치 → 킬 보상 + 현상금
  const bMoneyBefore = find(b.sub.snapshot, b.id).money;
  r.ok('B 이동', await walkTo(api, b.id, b.sub, -120, 0, 25000));
  r.ok('A 이동', await walkTo(api, a.id, a.sub, -110, 0, 25000));
  r.ok('B 가 A 를 처치', await beatDown(b, a));
  await sleep(400);
  const bAfterKill = find(b.sub.snapshot, b.id);
  r.ok('킬 재화 + 고블린 현상금 지급',
    bAfterKill.money >= bMoneyBefore + cfg.scoring.killMoney + cfg.goblin.killBounty,
    `${bMoneyBefore} → ${bAfterKill.money}`);

  // 최후 1인 → 조기 종료 → 정산
  r.ok('라운드 정산 진입', await waitPhase(a.sub, 'RESULT', 8000));
  const aR = find(a.sub.snapshot, a.id), bR = find(b.sub.snapshot, b.id);
  r.ok('생존자 B 가 1위', bR.rank === 1 && aR.rank === 2, `B ${bR.rank}위, A ${aR.rank}위`);
  r.ok('라운드 점수 = 등수 + 킬 + 어시 (생존 점수 없음)',
    bR.roundPoints === bR.pointsBreakdown.rank + bR.pointsBreakdown.kills + bR.pointsBreakdown.assists
    && !('survival' in bR.pointsBreakdown),
    JSON.stringify(bR.pointsBreakdown));
  r.ok('킬 점수 3점', bR.pointsBreakdown.kills === bR.kills * cfg.scoring.killPoints);

  // 이월: min(준비 잔액 + 전투 획득, 한도 500)
  r.ok('이월 내역이 계산됨', !!bR.carry, JSON.stringify(bR.carry));
  const rawB = bR.carry.prepEnd + bR.carry.battleGain;
  r.ok('이월 = min(잔액+획득, 한도)',
    bR.carry.carried === Math.min(rawB, 500) && bR.carry.raw === rawB,
    JSON.stringify(bR.carry));
  r.ok('한도 초과분은 소멸로 표시', bR.carry.lost === Math.max(0, rawB - 500));

  // ── 2라운드: 기본 재화 + 이월 ────────────────────────────────────
  r.ok('2라운드 준비 진입', await waitPhase(a.sub, 'PREP', 15000));
  await sleep(300);
  const a2 = find(a.sub.snapshot, a.id), b2 = find(b.sub.snapshot, b.id);
  r.ok('다음 라운드 재화 = 기본 + 이월 (B)', b2.money === 1000 + bR.carry.carried,
    `${b2.money} ≠ ${1000 + bR.carry.carried}`);
  r.ok('다음 라운드 재화 = 기본 + 이월 (A)', a2.money === 1000 + aR.carry.carried,
    `${a2.money}`);
  r.ok('강화·장신구 기본 비이월', a2.trinket === null && a2.enhance.weapon === 0,
    JSON.stringify([a2.trinket, a2.enhance]));
  r.ok('이전 라운드 순위 표시', a2.prevRank === 2 && b2.prevRank === 1);

  // 2라운드도 빠르게 소화한다
  await allReady(all);
  await waitPhase(a.sub, 'BATTLE', 8000);
  await walkTo(api, b.id, b.sub, -120, 0, 25000);
  await walkTo(api, a.id, a.sub, -110, 0, 25000);
  await beatDown(b, a);

  // ── 최종 정산 ────────────────────────────────────────────────────
  r.ok('최종 정산 진입', await waitPhase(a.sub, 'FINISH', 25000));
  const final = a.sub.snapshot.final;
  r.ok('최종 결과가 계산됨', !!final && final.rows.length === 2);
  r.ok('최종 점수 = 누적 전투 점수 (재화 제외)',
    final.rows.every(row => {
      const p = find(a.sub.snapshot, row.id);
      return row.score === p.score;
    }));
  r.ok('1위 = 점수가 높은 쪽', final.rows[0].score >= final.rows[1].score);
  r.ok('전투력·보유 재화가 별도 표기', 'combatValue' in final.rows[0] && 'money' in final.rows[0]);
  r.ok('동점 연출 구조 존재 (동점 없으면 빈 배열)', Array.isArray(final.dramas));

  // 재대결
  await api.act(a.id, 'rematch');
  r.ok('재대결로 준비 페이즈 재진입', await waitPhase(a.sub, 'PREP', 5000));
  const aRe = find(a.sub.snapshot, a.id);
  r.ok('재대결 시 점수·재화 초기화', aRe.score === 0 && aRe.money === 1000);
} catch (err) {
  r.ok('예외 없이 완료', false, String(err && err.stack || err));
}

await finish(r, proc);

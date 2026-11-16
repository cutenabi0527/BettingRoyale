// 방 시스템 검증 (문서 5절·17절).
// 공개방은 목록에 노출, 비공개방은 코드 검색 + 비밀번호로만 입장.
// 최소 인원 봇 보충, 봇 자리 양보, 호스트 이전.
import {
  startServer, makeReporter, makeClient, sleep, waitPhase, waitFor, finish
} from './test-harness.mjs';

const { proc, base } = await startServer(import.meta.dirname);
const r = makeReporter();
const api = makeClient(base);

try {
  const cfg = await api.get('/config');
  r.ok('MVP 상한 8명', cfg.maxPlayers === 8);
  r.ok('최소 인원 2명', cfg.minPlayers === 2);

  // ── 공개방 ───────────────────────────────────────────────────────
  const pub = await api.createRoom('호스트', { name: '공개 테스트' });
  await sleep(200);
  let list = await api.get('/rooms');
  r.ok('공개방이 목록에 노출됨', list.rooms.some(x => x.code === pub.code));
  const brief = list.rooms.find(x => x.code === pub.code);
  r.ok('목록에 참가 판단 정보 표시',
    brief && 'name' in brief && 'count' in brief && 'capacity' in brief
    && 'mode' in brief && 'phase' in brief,
    JSON.stringify(brief));
  r.ok('기본 봇 1명이 로비에 있음', brief.bots === 1, `봇 ${brief.bots}명`);

  // ── 비공개방 ─────────────────────────────────────────────────────
  const noPw = await api.post('/rooms', { playerName: 'X', visibility: 'private' });
  r.ok('비공개방은 비밀번호 필수', noPw.status === 400);

  const priv = await api.createRoom('비밀 호스트', {
    name: '비공개 테스트', visibility: 'private', password: 'hunt' });
  await sleep(200);
  list = await api.get('/rooms');
  r.ok('비공개방은 목록에 노출되지 않음', !list.rooms.some(x => x.code === priv.code));

  const found = await api.post('/rooms/find', { code: priv.code.toLowerCase() });
  r.ok('코드 검색으로 비공개방 발견 (대소문자 무시)', found.status === 200
    && found.body.room.locked === true);
  const notFound = await api.post('/rooms/find', { code: 'ZZZZZZ' });
  r.ok('없는 코드는 404', notFound.status === 404);

  const wrongPw = await api.join('침입자', priv.code, 'wrong');
  r.ok('비밀번호가 틀리면 입장 거부', wrongPw.status === 401);
  const rightPw = await api.join('친구', priv.code, 'hunt');
  r.ok('비밀번호가 맞으면 입장', rightPw.status === 200);

  // ── 봇 보충과 자리 양보 ──────────────────────────────────────────
  const solo = await api.createRoom('솔로', { name: '혼자 방' });
  await sleep(200);
  await api.act(solo.id, 'settings', { settings: { bots: 0, rounds: 1, prepSeconds: 30, battleSeconds: 30 } });
  await sleep(200);
  r.ok('봇 0명이면 혼자', solo.sub.snapshot.players.length === 1);
  await api.act(solo.id, 'start');
  r.ok('혼자 시작하면 봇이 자동 보충됨', await waitPhase(solo.sub, 'PREP', 4000));
  r.ok('보충 후 최소 2명', solo.sub.snapshot.players.length >= 2,
    `${solo.sub.snapshot.players.length}명`);
  r.ok('보충된 참가자는 봇', solo.sub.snapshot.players.some(p => p.bot));

  // 로비에서 방이 가득 찼을 때 인간이 봇 자리를 차지한다
  const full = await api.createRoom('꽉찬방', { name: '만석' });
  await sleep(200);
  await api.act(full.id, 'settings', { settings: { bots: 7 } });
  await sleep(300);
  r.ok('봇 7명 + 인간 1명 = 8명 만석', full.sub.snapshot.players.length === 8);
  const late = await api.join('늦은 인간', full.code);
  r.ok('만석이어도 봇을 밀어내고 입장', late.status === 200);
  await sleep(300);
  const bots = full.sub.snapshot.players.filter(p => p.bot).length;
  r.ok('봇이 한 명 줄어듦', bots === 6 && full.sub.snapshot.players.length === 8,
    `봇 ${bots}명, 전체 ${full.sub.snapshot.players.length}명`);

  // ── 호스트 이전 ──────────────────────────────────────────────────
  await api.post('/leave', { id: full.id });
  await sleep(300);
  r.ok('호스트 이탈 시 남은 인간에게 이전', late.sub.snapshot.hostId === late.id,
    `${late.sub.snapshot.hostId}`);

  // 인간이 모두 나가면 방이 닫힌다
  await api.post('/leave', { id: late.id });
  await sleep(300);
  const after = await api.post('/rooms/find', { code: full.code });
  r.ok('인간 전원 이탈 시 방 제거', after.status === 404);
} catch (err) {
  r.ok('예외 없이 완료', false, String(err && err.stack || err));
}

await finish(r, proc);

// BettingRoyale MVP 목업 서버
//
// 「BettingRoyale MVP 기획」 문서를 그대로 구현한다. 문서의 규칙표는 이 파일 한 곳에만
// 정의하고 GET /config 로 그대로 공개하므로, 화면에 뜬 수치가 실제 규칙과 어긋날 수 없다.
//
// 문서에서 '미결정 사항'으로 남긴 항목은 호스트 설정이나 상수로 뽑아두고 주석에 표시했다.
const http = require('http');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const PORT = Number(process.env.PORT || process.argv[2] || 3000);
const PUBLIC = path.join(__dirname, 'public');

// ── 매치 규칙과 초기 설정값 (문서 6절) ────────────────────────────────
const MAX_PLAYERS = 8;              // MVP 상한: 인간·봇 합산 8명
const MIN_TOTAL_PLAYERS = 2;        // 매치 시작 조건: 인간·봇 합산 2명
const COUNTDOWN_SECONDS = 3;        // 전장 이동
const RESULT_SECONDS = 10;          // 정산 화면
const FINISH_SECONDS = 30;          // 최종 정산
const DISCONNECT_GRACE_MS = 8000;   // 재접속 유예 (문서 5~10초)

const COLORS = ['#ff5d73', '#52c8ff', '#ffd257', '#8a7dff', '#66e3a4', '#ff8f4d', '#f06ad8', '#9adb4f'];
const BOT_NAMES = ['봇 카이', '봇 루나', '봇 오토', '봇 네오', '봇 지크', '봇 미르', '봇 세븐'];

// 팀 식별. 팀전은 2v2 · 3v3 · 4v4 두 팀 구성이다.
const TEAMS = [
  { index: 0, label: 'ALPHA', color: '#52c8ff' },
  { index: 1, label: 'BRAVO', color: '#ff5d73' }
];

// ── 방 설정 ───────────────────────────────────────────────────────────
// 호스트가 로비에서 고친다. 범위를 서버에서만 정의하고 /config 로 공개해서,
// 클라이언트 입력 검증과 화면의 선택지가 같은 표에서 나오게 한다.
const SETTINGS_SPEC = {
  rounds:        { label: '라운드 수',       min: 1,   max: 5,     step: 1,   default: 3,    unit: '라운드' },
  prepSeconds:   { label: '준비 시간',       min: 30,  max: 300,   step: 10,  default: 90,   unit: '초' },
  battleSeconds: { label: '전투 시간',       min: 30,  max: 300,   step: 10,  default: 90,   unit: '초' },
  baseMoney:     { label: '라운드 기본 재화', min: 200, max: 5000,  step: 100, default: 1000, unit: 'S' },
  // 최대 이월 금액은 문서상 미결정 사항. 기본값이 라운드당 자동 수입(등수·생존 보상
  // ≈ 200~1,200S)보다 낮으면 전원이 매 라운드 한도에 걸려 "남기는 선택" 이 무의미해진다.
  // 그래서 한도를 자동 수입보다 넉넉하게 잡아, 아낀 만큼 실제로 이월되게 한다.
  carryCap:      { label: '최대 이월 한도',   min: 0,   max: 10000, step: 100, default: 2500, unit: 'S' },
  dropSeconds:   { label: '보급 주기',       min: 5,   max: 60,    step: 5,   default: 15,   unit: '초' },
  bots:          { label: '봇 인원',         min: 0,   max: 7,     step: 1,   default: 1,    unit: '명' },
  teamSize:      { label: '팀 인원',         min: 2,   max: 4,     step: 1,   default: 2,    unit: '명' }
};
const SETTINGS_ENUMS = {
  mode: {
    label: '대전 모드', default: 'solo',
    options: [
      { value: 'solo', label: '개인전', note: '누적 전투 점수로 순위를 정합니다' },
      { value: 'team', label: '팀전',   note: '2v2 · 3v3 · 4v4. 재화는 개인 소유이고 적선으로만 이전합니다' }
    ]
  }
};
const SETTINGS_FLAGS = {
  // 문서 6절: 무기·방어구 강화 상태는 기본적으로 라운드 간 이월하지 않는다.
  carryEnhance: { label: '강화 이월', default: false,
    onNote: '무기·방어구 강화 단계가 다음 라운드로 이월됩니다',
    offNote: '라운드마다 강화가 초기화됩니다 (문서 기본값)' },
  // 문서 19절 미결정 사항: 아군 공격 허용 여부
  friendlyFire: { label: '아군 공격', default: false,
    onNote: '팀전에서 아군을 공격할 수 있습니다',
    offNote: '팀전에서 아군은 공격 대상이 아닙니다 (미결정 사항 · 기본 끄기)' }
};

function defaultSettings() {
  const s = {};
  for (const [key, spec] of Object.entries(SETTINGS_SPEC)) s[key] = spec.default;
  for (const [key, spec] of Object.entries(SETTINGS_ENUMS)) s[key] = spec.default;
  for (const [key, spec] of Object.entries(SETTINGS_FLAGS)) s[key] = spec.default;
  return s;
}

// ── 렌더 투영 ─────────────────────────────────────────────────────────
// 월드는 등방(isotropic)이다. 화면에서만 y 축을 눌러 쿼터뷰를 만든다.
// 이렇게 두면 벽 두께·사거리·시야 판정이 방향에 상관없이 같은 뜻을 갖는다.
const VERTICAL_SQUASH = 0.8;
const PLAYER_RADIUS = 9;

// ── 지형 (문서 7절 준비 광장 · 13절 전장) ─────────────────────────────
// 벽은 축 정렬 사각형(x,y = 좌상단). 상호작용 대상은 원형 스테이션이다.
// 서버가 충돌·시야를 판정하고, 같은 표를 /config 로 내보내 클라이언트가 그린다.
// MVP 전투 가능 영역은 단일 평면이다. 고저차는 판정에 넣지 않는다.
const PLAZA = {
  key: 'plaza', label: '준비 광장',
  bounds: { minX: -240, maxX: 240, minY: -190, maxY: 190 },
  walls: [
    { x: -240, y: -190, w: 44, h: 26, tall: 1 },
    { x: 196, y: -190, w: 44, h: 26, tall: 1 },
    { x: -240, y: 164, w: 44, h: 26, tall: 1 },
    { x: 196, y: 164, w: 44, h: 26, tall: 1 },
    { x: -30, y: -190, w: 60, h: 20, tall: 1 },
    { x: -240, y: -12, w: 46, h: 24, tall: 1 },
    { x: 194, y: -12, w: 46, h: 24, tall: 1 }
  ],
  // 문서 7절 MVP 스테이션: 도박(룰렛) · 상점 · 모루 · 순위판 · 허수아비
  stations: [
    { key: 'wheel', kind: 'wheel', label: '룰렛', x: 0, y: -70, radius: 30, reach: 64, hint: '베팅' },
    { key: 'shop', kind: 'shop', label: '상점', x: -160, y: 52, radius: 24, reach: 56, hint: '장신구 뽑기' },
    { key: 'anvil', kind: 'anvil', label: '모루', x: 160, y: 52, radius: 22, reach: 54, hint: '확률 강화' },
    { key: 'board', kind: 'board', label: '순위판', x: -160, y: -112, radius: 18, reach: 48, hint: '순위 확인' },
    // 허수아비는 얇은 말뚝이다 — 몸통이 두꺼우면 때렸는지 스쳤는지 안 보인다.
    // 맞으면 흔들리는 시각 피드백을 전원에게 브로드캐스트한다 (room.dummyShake).
    { key: 'dummyL', kind: 'dummy', label: '허수아비', x: 132, y: -116, radius: 5, reach: 0, hint: '리치·데미지 확인' },
    { key: 'dummyR', kind: 'dummy', label: '허수아비', x: 186, y: -116, radius: 5, reach: 0, hint: '리치·데미지 확인' }
  ],
  pits: [],
  spawns: [
    { x: -140, y: 130 }, { x: -100, y: 146 }, { x: -60, y: 158 }, { x: -20, y: 164 },
    { x: 20, y: 164 }, { x: 60, y: 158 }, { x: 100, y: 146 }, { x: 140, y: 130 }
  ]
};

// 전장 — 중앙 십자 건물, 좌우 사이드 통로, 상자 12개(4개는 얇다), 낙사 구덩이 4개.
const BATTLEFIELD = {
  key: 'battlefield', label: '전장',
  bounds: { minX: -300, maxX: 300, minY: -220, maxY: 220 },
  walls: [
    // 중앙 십자 구조물 — 네 방향 통로가 열려 있어 한 곳을 물고 늘어질 수 없다
    { x: -72, y: -18, w: 144, h: 36, tall: 1 },
    { x: -18, y: -100, w: 36, h: 82, tall: 1 },
    { x: -18, y: 18, w: 36, h: 82, tall: 1 },
    // 좌우 긴 벽 — 사이드 통로
    { x: -228, y: -146, w: 26, h: 112, tall: 1 },
    { x: 202, y: -146, w: 26, h: 112, tall: 1 },
    { x: -228, y: 34, w: 26, h: 112, tall: 1 },
    { x: 202, y: 34, w: 26, h: 112, tall: 1 },
    // 상하 차단벽 — 정면 돌진을 끊는다
    { x: -140, y: -196, w: 136, h: 24, tall: 1 },
    { x: 4, y: 172, w: 136, h: 24, tall: 1 },
    { x: 4, y: -196, w: 68, h: 24, tall: 0 },
    { x: -140, y: 172, w: 68, h: 24, tall: 0 },
    // 두꺼운 상자 8개 — 넘을 수 없고 시야도 완전히 끊는다
    { x: -168, y: -60, w: 32, h: 32, tall: 0 },
    { x: 136, y: -60, w: 32, h: 32, tall: 0 },
    { x: -168, y: 28, w: 32, h: 32, tall: 0 },
    { x: 136, y: 28, w: 32, h: 32, tall: 0 },
    { x: -20, y: -172, w: 40, h: 30, tall: 0 },
    { x: -20, y: 142, w: 40, h: 30, tall: 0 },
    { x: -262, y: -18, w: 30, h: 36, tall: 0 },
    { x: 232, y: -18, w: 30, h: 36, tall: 0 },
    // 얇은 바리케이드 4개 — 시야는 끊지만 두께가 얇아 사거리로 넘겨 때릴 수 있다
    { x: -96, y: -118, w: 30, h: 14, tall: 0 },
    { x: 66, y: 104, w: 30, h: 14, tall: 0 },
    { x: 66, y: -118, w: 30, h: 14, tall: 0 },
    { x: -96, y: 104, w: 30, h: 14, tall: 0 }
  ],
  stations: [],
  // 낙사 구덩이 (문서 2절 성공 기준 · 13절 「낙사는 포함한다」).
  // 이동을 막지 않는다 — 중심이 들어가면 떨어진다. 점프·수직 이동은 없다.
  pits: [
    { x: -122, y: -92, w: 44, h: 30 },
    { x: 78, y: -92, w: 44, h: 30 },
    { x: -122, y: 62, w: 44, h: 30 },
    { x: 78, y: 62, w: 44, h: 30 }
  ],
  spawns: [
    { x: -270, y: -190 }, { x: 270, y: -190 }, { x: -270, y: 190 }, { x: 270, y: 190 },
    { x: -170, y: -190 }, { x: 170, y: -190 }, { x: -170, y: 190 }, { x: 170, y: 190 }
  ]
};
const MAPS = { plaza: PLAZA, battlefield: BATTLEFIELD };

function mapFor(phase) {
  return phase === 'BATTLE' || phase === 'COUNTDOWN' ? BATTLEFIELD : PLAZA;
}

// 원(플레이어) 과 사각형(벽) 충돌을 풀어서 겹치지 않는 위치를 돌려준다.
function resolveWalls(map, x, y, radius) {
  for (let pass = 0; pass < 3; pass++) {
    let moved = false;
    for (const w of map.walls) {
      const nearestX = Math.max(w.x, Math.min(x, w.x + w.w));
      const nearestY = Math.max(w.y, Math.min(y, w.y + w.h));
      let dx = x - nearestX, dy = y - nearestY;
      let dist = Math.hypot(dx, dy);
      if (dist >= radius) continue;
      if (dist > 1e-6) {
        x = nearestX + dx / dist * radius;
        y = nearestY + dy / dist * radius;
      } else {
        // 중심이 사각형 안에 있다 — 가장 가까운 면으로 밀어낸다
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
      const dx = x - s.x, dy = y - s.y;
      const dist = Math.hypot(dx, dy), min = s.radius + radius;
      if (dist >= min) continue;
      if (dist > 1e-6) { x = s.x + dx / dist * min; y = s.y + dy / dist * min; }
      else { x = s.x + min; }
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

// 낙사 판정. 구덩이는 이동을 막지 않는다 — 중심이 들어가면 떨어진다.
function pitAt(map, x, y) {
  for (const p of map.pits || []) {
    if (x >= p.x && x <= p.x + p.w && y >= p.y && y <= p.y + p.h) return p;
  }
  return null;
}

// 선분이 사각형을 지나는가 (slab 판정). 엄폐물이 공격을 끊는 근거.
function segmentHitsRect(x1, y1, x2, y2, r) {
  const dx = x2 - x1, dy = y2 - y1;
  let t0 = 0, t1 = 1;
  for (const [p, d, lo, hi] of [[x1, dx, r.x, r.x + r.w], [y1, dy, r.y, r.y + r.h]]) {
    if (Math.abs(d) < 1e-9) { if (p < lo || p > hi) return false; continue; }
    let a = (lo - p) / d, b = (hi - p) / d;
    if (a > b) [a, b] = [b, a];
    t0 = Math.max(t0, a); t1 = Math.min(t1, b);
    if (t0 > t1) return false;
  }
  return true;
}

// 두 점 사이에 벽이 없는가. 스테이션(룰렛대 등)도 시야를 막는다. 구덩이는 막지 않는다.
function hasLineOfSight(map, x1, y1, x2, y2) {
  for (const w of map.walls) if (segmentHitsRect(x1, y1, x2, y2, w)) return false;
  for (const s of map.stations) {
    const dx = x2 - x1, dy = y2 - y1;
    const len2 = dx * dx + dy * dy;
    const t = len2 < 1e-9 ? 0 : Math.max(0, Math.min(1, ((s.x - x1) * dx + (s.y - y1) * dy) / len2));
    if (Math.hypot(x1 + dx * t - s.x, y1 + dy * t - s.y) < s.radius) return false;
  }
  return true;
}

// ── 장비 (문서 10절) ─────────────────────────────────────────────────
// 무기·방어구는 상점에서 팔지 않는다. 기본 장비를 지급하고 모루 확률 강화만 제공한다.
// 장신구는 단독 슬롯 1개이며 상점 랜덤 뽑기로만 얻고, 뽑으면 즉시 장착·교체된다.
const BASE_WEAPON = {
  key: 'club', label: '기본 무기', damage: 10, range: 26, cooldown: 460,
  note: '전원 동일 지급 · 모루에서만 확률 강화'
};
const BASE_ARMOR = {
  key: 'fatigues', label: '기본 방어구', maxHp: 100, speed: 46,
  note: '전원 동일 지급 · 모루에서만 확률 강화'
};
const DASH_BASE = { dist: 26, charges: 1, recharge: 1600 };

// 장신구 — 유틸리티(대시 횟수 · 대시 쿨타임)만 건드린다.
// 한 아이템에 하나 이상의 효과를 조합할 수 있다 (질풍의 인장).
const TRINKET_TYPES = [
  { key: 'charm', label: '가벼운 부적', weight: 40, charges: [1, 2, 3], recharge: [0, 0, 0],
    note: '대시 충전 횟수' },
  { key: 'feather', label: '바람 깃털', weight: 40, charges: [0, 0, 0], recharge: [-200, -400, -650],
    note: '대시 재충전 시간' },
  { key: 'sigil', label: '질풍의 인장', weight: 20, charges: [1, 1, 2], recharge: [-100, -300, -500],
    note: '두 효과 조합' }
];
const TRINKET_GRADES = [
  { key: 'common', label: '일반', weight: 60, value: 400, color: '#9fb0c4' },
  { key: 'rare', label: '희귀', weight: 30, value: 900, color: '#52c8ff' },
  { key: 'epic', label: '영웅', weight: 10, value: 1800, color: '#c07bff' }
];
const TRINKET_PRICE = 400;         // 뽑기 1회 비용

// 9종(3 종류 × 3 등급)을 전부 펼쳐서 확률과 함께 공개한다.
const TRINKET_ITEMS = [];
for (const type of TRINKET_TYPES) {
  TRINKET_GRADES.forEach((grade, gi) => {
    TRINKET_ITEMS.push({
      key: `${type.key}_${grade.key}`,
      type: type.key, typeLabel: type.label, typeNote: type.note,
      grade: grade.key, gradeLabel: grade.label, gradeColor: grade.color,
      label: `${grade.label} ${type.label}`,
      charges: type.charges[gi], recharge: type.recharge[gi],
      value: grade.value,
      weight: type.weight * grade.weight
    });
  });
}
const TRINKET_WEIGHT_TOTAL = TRINKET_ITEMS.reduce((a, i) => a + i.weight, 0);
for (const item of TRINKET_ITEMS) item.chance = item.weight / TRINKET_WEIGHT_TOTAL;

function rollTrinket() {
  let roll = Math.random() * TRINKET_WEIGHT_TOTAL;
  for (const item of TRINKET_ITEMS) { roll -= item.weight; if (roll < 0) return item; }
  return TRINKET_ITEMS[TRINKET_ITEMS.length - 1];
}

// ── 강화 (문서 11절) ─────────────────────────────────────────────────
// 확률형 투자다. 재화를 먼저 차감하고, 실패하면 재화만 소모되고 단계는 오르지 않는다.
// 슬롯별 1~5등급. 장신구는 강화 대상이 아니다.
const MAX_ENHANCE = 5;
const ENHANCE_SLOTS = ['weapon', 'armor'];
const ANVIL_BASE = { weapon: 140, armor: 110 };
const ENHANCE_CHANCE = [0.90, 0.75, 0.60, 0.45, 0.30];
const ENHANCE_GAIN = {
  weapon: [
    { key: 'damage', label: '피해량', per: 5 },
    { key: 'range', label: '사거리', per: 3 },
    { key: 'attackCooldown', label: '공격 주기', per: -16, unit: 'ms' }
  ],
  armor: [
    { key: 'maxHp', label: '최대 체력', per: 14 },
    { key: 'speed', label: '이동 속도', per: 1.4 }
  ]
};
const SLOT_LABEL = { weapon: '무기', armor: '방어구', trinket: '장신구' };
const SLOT_ICON = { weapon: '⚔', armor: '⬢', trinket: '✦' };

function enhanceCost(slot, level) {
  if (level >= MAX_ENHANCE) return null;
  return ANVIL_BASE[slot] * (level + 1);
}
function enhanceChance(level) {
  return level >= MAX_ENHANCE ? 0 : ENHANCE_CHANCE[level];
}
// 현재 단계까지 들어간 장비 가격의 합. 동점 판정의 '전투력' 과 황금 고블린 지정에 쓴다.
function enhanceInvested(slot, level) {
  let sum = 0;
  for (let lv = 0; lv < level; lv++) sum += enhanceCost(slot, lv);
  return sum;
}
// 문서 14절: 전투력 = 최종 정산 시 장비 및 소모품 가격의 합산값
function combatValue(p) {
  return enhanceInvested('weapon', p.enhance.weapon)
    + enhanceInvested('armor', p.enhance.armor)
    + (p.trinket ? p.trinket.value : 0);
}

function statsFor(p) {
  const ew = p.enhance.weapon, ea = p.enhance.armor;
  const t = p.trinket;
  return {
    damage: BASE_WEAPON.damage + ew * 5,
    range: BASE_WEAPON.range + ew * 3,
    attackCooldown: Math.max(180, BASE_WEAPON.cooldown - ew * 16),
    maxHp: BASE_ARMOR.maxHp + ea * 14,
    speed: BASE_ARMOR.speed + ea * 1.4,
    dashDist: DASH_BASE.dist,
    dashCharges: DASH_BASE.charges + (t ? t.charges : 0),
    dashRecharge: Math.max(500, DASH_BASE.recharge + (t ? t.recharge : 0))
  };
}
const DASH_INVULN_MS = 250;
const ATTACK_ARC = 0.85;

// ── 자기장 (문서 13절) ───────────────────────────────────────────────
// 전투 시간의 20% 지점에 시작해 100% 지점에 최소 반경으로 수렴한다.
// 수렴 중심은 전장 중앙 구역(3×3 중 가운데 칸) 안에서 무작위로 정한다.
const STORM_START_FRAC = 0.20;
const STORM_END_FRAC = 1.00;
const STORM_FINAL_RADIUS = 40;
const STORM_DPS = 12;               // 「좀 아파야 한다」

// ── 보급 (문서 13절) ─────────────────────────────────────────────────
// MVP 보급은 재화와 힐팩 두 종류. 힐팩은 최대 체력 비례 힐량이다.
const DROP_KINDS = [
  { key: 'money', label: '재화 상자', weight: 60, minAmount: 120, maxAmount: 340 },
  { key: 'medkit', label: '힐팩', weight: 40, healFrac: 0.35 }
];
const DROP_WEIGHT_TOTAL = DROP_KINDS.reduce((sum, d) => sum + d.weight, 0);
const DROP_RADIUS = 13;
const DROP_PICKUP_PAD = 6;
const DROP_MAX_ALIVE = 8;

// ── 도박 마감 (문서 9절) ─────────────────────────────────────────────
// 준비 종료 직전에는 신규 베팅·투자를 열지 않는다. 돌지 않은 베팅은 전액 환불한다.
const ROULETTE_CLOSE_LEAD_MS = 20000;

// ── 적선 / 구걸 (문서 8절) ───────────────────────────────────────────
// 팀전에서도 재화는 개인 소유다. 플레이어 간 재화 이동은 적선으로만 가능하다.
// 적선 대상 범위와 내역 공개 범위는 문서상 미결정 사항 — 목업은 전원 공개·거리 제약으로 둔다.
const CHARITY_MIN = 10;
const CHARITY_REACH = 46;
const BEG_COOLDOWN_MS = 4000;
const BEG_SHOW_MS = 4500;
const ANNOUNCE_KEEP = 10;

// ── 룰렛 (문서 9절 · 러스트 룰렛 레퍼런스) ───────────────────────────
// 1·3·5·10·20 중 하나에 걸고, 맞으면 "베팅액 × 숫자" 를 추가로 받는다.
// 즉 총 회수는 베팅액 ×(숫자+1). 46칸을 22/11/7/4/2 로 나눠서
// 어떤 칸도 기대 회수율이 100% 를 넘지 않게 맞췄다 (91.3% ~ 95.7%).
const WHEEL_NUMBERS = [1, 3, 5, 10, 20];
const WHEEL_SLOT_COUNTS = { 1: 22, 3: 11, 5: 7, 10: 4, 20: 2 };
const WHEEL_SLOTS = 46;
const ROULETTE_BET_MS = 10000;
const ROULETTE_SPIN_MS = 10000;
const MIN_BET = 10;

const WHEEL_LAYOUT = (() => {
  const others = [3, 5, 3, 10, 3, 5, 3, 20, 3, 5, 3, 10, 3, 5, 3, 10, 3, 5, 3, 20, 3, 5, 5, 10];
  const layout = [];
  others.forEach((n, i) => { layout.push(n); if (i < 22) layout.push(1); });
  return layout;
})();

if (WHEEL_LAYOUT.length !== WHEEL_SLOTS) {
  throw new Error(`룰렛 칸 수가 어긋납니다: ${WHEEL_LAYOUT.length} ≠ ${WHEEL_SLOTS}`);
}
for (const n of WHEEL_NUMBERS) {
  const actual = WHEEL_LAYOUT.filter(v => v === n).length;
  if (actual !== WHEEL_SLOT_COUNTS[n]) {
    throw new Error(`룰렛 ${n} 칸 수가 어긋납니다: 배치 ${actual} ≠ 표 ${WHEEL_SLOT_COUNTS[n]}`);
  }
}
const WHEEL_TABLE = WHEEL_NUMBERS.map(n => ({
  number: n,
  slots: WHEEL_SLOT_COUNTS[n],
  chance: WHEEL_SLOT_COUNTS[n] / WHEEL_SLOTS,
  payout: n + 1,
  expected: WHEEL_SLOT_COUNTS[n] / WHEEL_SLOTS * (n + 1)
}));
for (const row of WHEEL_TABLE) {
  if (row.expected > 1) {
    throw new Error(`룰렛 ${row.number} 칸이 기대값 100% 를 넘습니다: ${(row.expected * 100).toFixed(1)}%`);
  }
}

// ── 점수 (문서 14절) ─────────────────────────────────────────────────
// 라운드 점수 = 등수 점수 + 킬 점수 + 어시스트 점수.
// 최종 점수 = 누적 라운드 전투 점수. 보유 재화는 점수에 포함하지 않는다.
// 문서의 초기 가설은 30~5점이었지만, 플레이 결과 1등과 꼴등의 격차가 너무 커서
// 한 라운드로 승부가 굳어 보였다. 격차를 줄여 킬·어시스트와 다음 라운드 역전 여지를 남긴다.
const RANK_POINTS_TOP = 25;
const RANK_POINTS_BOTTOM = 10;
const KILL_POINTS = 3;
const ASSIST_POINTS = 1;
const ASSIST_WINDOW_MS = 10000;

// 재화 보상 — 문서상 '기본 지급과 별도의 후보 규칙'. 목업은 켜둔다.
const ROUND_PAYOUT_TOP = 600;
const ROUND_PAYOUT_BOTTOM = 300;
const KILL_MONEY = 150;
const ASSIST_MONEY = 50;
const SURVIVAL_MONEY_MAX = 250;

// 황금 고블린 (문서 2절 · 5절 현상금 시스템)
// 전투 단계 시작 전, 가장 장비가 좋은 유저를 지정한다. 킬 관여 시 추가 재화를 지급한다.
const GOBLIN_KILL_BOUNTY = 500;
const GOBLIN_ASSIST_BOUNTY = 200;

function spread(top, bottom, index, count) {
  if (count <= 1) return top;
  return Math.round(top - (top - bottom) * index / (count - 1));
}
const rankPointsFor = (i, n) => spread(RANK_POINTS_TOP, RANK_POINTS_BOTTOM, i, n);
const payoutFor = (i, n) => Math.round(spread(ROUND_PAYOUT_TOP, ROUND_PAYOUT_BOTTOM, i, n) / 10) * 10;

// ── 방 ────────────────────────────────────────────────────────────────
const rooms = new Map();            // code -> room
const playerRoom = new Map();       // playerId -> room code

function newRoomCode() {
  const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';   // 혼동되는 글자(I,O,0,1) 제외
  for (let tries = 0; tries < 200; tries++) {
    let code = '';
    for (let i = 0; i < 6; i++) code += alphabet[crypto.randomInt(alphabet.length)];
    if (!rooms.has(code)) return code;
  }
  throw new Error('방 코드를 만들 수 없습니다');
}

function createRoom({ name, visibility, password }) {
  const code = newRoomCode();
  const room = {
    code,
    name: String(name || '').slice(0, 20) || `${code} 방`,
    visibility: visibility === 'private' ? 'private' : 'public',
    password: visibility === 'private' ? String(password || '').slice(0, 16) : '',
    players: new Map(),
    clients: new Map(),
    disconnectTimers: new Map(),
    settings: defaultSettings(),
    phase: 'LOBBY',
    round: 1,
    phaseEndsAt: null,
    hostId: null,
    lastTick: Date.now(),
    attackSeq: 0,
    eventSeq: 0,
    wheel: { cycle: 0, state: 'IDLE', endsAt: 0, slot: null, number: null, spinSeq: 0 },
    wheelHistory: [],
    announcements: [],
    storm: null,
    drops: [],
    dropSeq: 0,
    nextDropAt: 0,
    battleStartedAt: 0,
    dummyShake: {},          // 허수아비 피격 시각 { stationKey: at } — 전원에게 흔들림을 보여준다
    goblinId: null,
    teamWins: [0, 0],
    roundWinnerTeam: null,
    final: null,
    createdAt: Date.now()
  };
  rooms.set(code, room);
  return room;
}

function roomOfPlayer(id) {
  const code = playerRoom.get(id);
  return code ? rooms.get(code) : null;
}

function activePlayers(room) {
  return [...room.players.values()].filter(p => !p.spectating);
}
function humanPlayers(room) {
  return [...room.players.values()].filter(p => !p.bot);
}
function teamCount(room) {
  return room.settings.mode === 'team' ? TEAMS.length : 0;
}
function roomCapacity(room) {
  return room.settings.mode === 'team'
    ? Math.min(MAX_PLAYERS, room.settings.teamSize * TEAMS.length)
    : MAX_PLAYERS;
}

function announce(room, text, tone = '') {
  room.announcements.push({ seq: ++room.eventSeq, text, tone, at: Date.now() });
  if (room.announcements.length > ANNOUNCE_KEEP) room.announcements.shift();
}
function notify(room, p, text, tone = '') {
  p.feed = { seq: ++room.eventSeq, text, tone, at: Date.now() };
}

function newPlayer(room, name, { bot = false } = {}) {
  const used = new Set([...room.players.values()].map(p => p.color));
  return {
    id: crypto.randomUUID(),
    name: String(name || '').slice(0, 12) || `Player ${room.players.size + 1}`,
    color: COLORS.find(c => !used.has(c)) || COLORS[room.players.size % COLORS.length],
    bot,
    team: 0,
    x: 0, y: 0, angle: 0,
    hp: BASE_ARMOR.maxHp, maxHp: BASE_ARMOR.maxHp,
    alive: true, spectating: false, ready: false,
    money: room.settings.baseMoney,
    enhance: { weapon: 0, armor: 0 },
    trinket: null,
    attempts: { weapon: 0, armor: 0 }, fails: { weapon: 0, armor: 0 },
    score: 0, roundPoints: 0, rank: null, prevRank: null, payout: 0,
    kills: 0, assists: 0, deathAt: 0, deathCause: null, damageLog: new Map(),
    attackReadyAt: 0, attackSeq: 0, attackAt: 0, hitAt: 0,
    dashCharges: DASH_BASE.charges, dashRechargeAt: 0, invulnUntil: 0,
    lastMoveAt: 0, moveIgnoreUntil: 0,
    spentThisRound: 0, wagered: 0, won: 0,
    bets: {}, lastWheel: null, feed: null, dummyHit: null, lastPull: null, lastEnhance: null,
    begUntil: 0, begReadyAt: 0, given: 0, received: 0,
    looted: 0, healed: 0, bounty: 0, survivalMs: 0, survivalFrac: 0,
    prepEndMoney: 0, battleGain: 0, carry: null,
    pointsBreakdown: null, payoutBreakdown: null,
    bot_: bot ? { plan: null, target: null, actAt: 0, wanderAt: 0, dir: 0 } : null
  };
}

// ── 봇 (문서 17절) ────────────────────────────────────────────────────
// 봇은 인간과 같은 규칙을 적용받는다. 별도 PvE 규칙은 없다.
function syncBots(room) {
  if (room.phase !== 'LOBBY') return;
  const humans = humanPlayers(room).length;
  const want = Math.max(0, Math.min(room.settings.bots, roomCapacity(room) - humans));
  const bots = [...room.players.values()].filter(p => p.bot);
  for (let i = bots.length; i > want; i--) {
    const victim = bots[i - 1];
    room.players.delete(victim.id);
    playerRoom.delete(victim.id);
  }
  for (let i = bots.length; i < want; i++) {
    const taken = new Set([...room.players.values()].map(p => p.name));
    const name = BOT_NAMES.find(n => !taken.has(n)) || `봇 ${i + 1}`;
    const bot = newPlayer(room, name, { bot: true });
    room.players.set(bot.id, bot);
    playerRoom.set(bot.id, room.code);
  }
  assignTeams(room);
  placeAll(room, PLAZA);
}

// 매치 시작 조건: 인간·봇 합산 최소 2명. 인간 1명이면 봇을 최소 1명 넣는다.
function ensureMinimumRoster(room) {
  let guard = 0;
  while (room.players.size < MIN_TOTAL_PLAYERS && guard++ < MAX_PLAYERS) {
    room.settings.bots = Math.min(SETTINGS_SPEC.bots.max, room.settings.bots + 1);
    const taken = new Set([...room.players.values()].map(p => p.name));
    const name = BOT_NAMES.find(n => !taken.has(n)) || `봇 ${room.players.size + 1}`;
    const bot = newPlayer(room, name, { bot: true });
    room.players.set(bot.id, bot);
    playerRoom.set(bot.id, room.code);
  }
  assignTeams(room);
}

function assignTeams(room) {
  const all = [...room.players.values()];
  if (room.settings.mode !== 'team') { all.forEach(p => p.team = 0); return; }
  const size = room.settings.teamSize;
  const counts = [0, 0];
  all.forEach(p => {
    // 인원이 적은 팀부터 채운다. 팀 정원을 넘기면 어쩔 수 없이 다른 팀에 붙인다.
    let idx = counts[0] <= counts[1] ? 0 : 1;
    if (counts[idx] >= size && counts[1 - idx] < size) idx = 1 - idx;
    p.team = idx;
    counts[idx] += 1;
  });
}

// ── 스냅샷 ────────────────────────────────────────────────────────────
// 준비 단계 공개 정보 (문서 15절): 이름·색상, 누적 점수, 이전 라운드 순위,
// 전체 도박 판돈과 참여 인원. 비공개: 보유 재화, 강화·장신구 상태, 개인 베팅,
// 다음 라운드 이월 예정 재화. 전투가 시작되면 강화 단계와 장비를 공개한다.
function publicPlayer(room, p, viewerId) {
  const self = p.id === viewerId;
  const conceal = room.phase === 'PREP' && !self;
  // 전투 중 사망자는 유령으로 돌아다닌다. 위치는 살아있는 플레이어에게 감춘다 —
  // 죽은 유저·관전자끼리는 서로 보인다. 생존 여부·킬 같은 공개 정보는 그대로 나간다.
  const viewer = room.players.get(viewerId);
  const ghostHidden = room.phase === 'BATTLE' && !p.alive && !p.spectating && !self
    && viewer && viewer.alive && !viewer.spectating;
  const stats = statsFor(p);
  return {
    id: p.id, name: p.name, color: p.color, bot: p.bot, team: p.team,
    x: ghostHidden ? null : p.x, y: ghostHidden ? null : p.y,
    angle: ghostHidden ? 0 : p.angle,
    hp: p.hp, maxHp: conceal ? null : p.maxHp,
    alive: p.alive, spectating: p.spectating, ready: p.ready,
    money: conceal ? null : p.money,
    enhance: conceal ? null : p.enhance,
    trinket: conceal ? null : p.trinket,
    combatValue: conceal ? null : combatValue(p),
    range: conceal ? null : stats.range,
    score: p.score, roundPoints: p.roundPoints, rank: p.rank, prevRank: p.prevRank,
    payout: p.payout,
    kills: p.kills, assists: p.assists, deathCause: p.deathCause,
    begUntil: p.begUntil, given: p.given, received: p.received,
    looted: p.looted, healed: p.healed, bounty: p.bounty,
    survivalMs: p.survivalMs, survivalFrac: p.survivalFrac,
    pointsBreakdown: p.pointsBreakdown, payoutBreakdown: p.payoutBreakdown,
    carry: conceal ? null : p.carry,
    spentThisRound: conceal ? null : p.spentThisRound,
    wagered: conceal ? null : p.wagered, won: conceal ? null : p.won,
    attackSeq: p.attackSeq, attackAt: p.attackAt, hitAt: p.hitAt,
    invulnUntil: p.invulnUntil,
    // 본인만 보는 값
    attackReadyAt: self ? p.attackReadyAt : 0,
    dashCharges: self ? p.dashCharges : 0,
    maxDashCharges: self ? stats.dashCharges : 0,
    dashRechargeAt: self ? p.dashRechargeAt : 0,
    speed: self ? stats.speed : 0,
    dashDist: self ? stats.dashDist : 0,
    damage: self ? stats.damage : 0,
    attackCooldown: self ? stats.attackCooldown : 0,
    maxHpSelf: self ? stats.maxHp : 0,
    attempts: self ? p.attempts : null, fails: self ? p.fails : null,
    bets: self ? p.bets : null,
    lastWheel: self ? p.lastWheel : null,
    lastPull: self ? p.lastPull : null,
    lastEnhance: self ? p.lastEnhance : null,
    dummyHit: self ? p.dummyHit : null,
    feed: self ? p.feed : null
  };
}

// 개인별 도박 선택과 베팅은 숨기고 전체 판돈과 참여 인원만 공개한다.
function publicWheel(room, viewerId) {
  const viewer = room.players.get(viewerId);
  const pot = {};
  for (const n of WHEEL_NUMBERS) pot[n] = 0;
  let bettors = 0;
  for (const p of room.players.values()) {
    let any = false;
    for (const [n, amount] of Object.entries(p.bets)) { pot[n] += amount; if (amount > 0) any = true; }
    if (any) bettors += 1;
  }
  const w = room.wheel;
  return {
    cycle: w.cycle, state: w.state, endsAt: w.endsAt,
    slot: w.state === 'SPINNING' || w.state === 'RESULT' ? w.slot : null,
    number: w.state === 'SPINNING' || w.state === 'RESULT' ? w.number : null,
    spinSeq: w.spinSeq,
    pot, bettors,
    myBets: viewer ? viewer.bets : {},
    history: room.wheelHistory.slice(-12)
  };
}

function teamsSnapshot(room) {
  if (room.settings.mode !== 'team') return null;
  return TEAMS.map(t => {
    const members = activePlayers(room).filter(p => p.team === t.index);
    return {
      ...t,
      members: members.length,
      alive: members.filter(p => p.alive).length,
      wins: room.teamWins[t.index] || 0,
      score: members.reduce((a, p) => a + p.score, 0)
    };
  });
}

function snapshot(room, viewerId) {
  const goblin = room.goblinId ? room.players.get(room.goblinId) : null;
  const humans = activePlayers(room).filter(p => !p.bot);
  return {
    type: 'snapshot', serverNow: Date.now(),
    room: { code: room.code, name: room.name, visibility: room.visibility,
            capacity: roomCapacity(room), maxPlayers: MAX_PLAYERS },
    phase: room.phase, round: room.round, totalRounds: room.settings.rounds,
    phaseEndsAt: room.phaseEndsAt, hostId: room.hostId, settings: room.settings,
    map: mapFor(room.phase).key,
    dummyShake: room.dummyShake,
    wheel: publicWheel(room, viewerId),
    announcements: room.announcements,
    teams: teamsSnapshot(room),
    goblin: goblin ? { id: goblin.id, name: goblin.name, color: goblin.color,
                       combatValue: combatValue(goblin),
                       killBounty: GOBLIN_KILL_BOUNTY, assistBounty: GOBLIN_ASSIST_BOUNTY } : null,
    ready: { done: humans.filter(p => p.ready).length, total: humans.length },
    roundWinnerTeam: room.roundWinnerTeam,
    final: room.final,
    storm: room.phase === 'BATTLE' && room.storm ? {
      cx: room.storm.cx, cy: room.storm.cy, radius: room.storm.radius,
      finalRadius: room.storm.finalRadius, startAt: room.storm.startAt,
      endAt: room.storm.endAt, dps: room.storm.dps,
      closing: Date.now() >= room.storm.startAt
    } : null,
    drops: room.phase === 'BATTLE'
      ? room.drops.map(d => ({ id: d.id, kind: d.kind, label: d.label, x: d.x, y: d.y, amount: d.amount }))
      : [],
    players: [...room.players.values()].map(p => publicPlayer(room, p, viewerId))
  };
}

function send(res, data) { res.write(`data: ${JSON.stringify(data)}\n\n`); }
function broadcast(room) {
  for (const [id, res] of room.clients) {
    try { send(res, snapshot(room, id)); } catch { room.clients.delete(id); }
  }
}

// ── 자기장 ────────────────────────────────────────────────────────────
function initStorm(room) {
  const b = BATTLEFIELD.bounds;
  const thirdX = (b.maxX - b.minX) / 3, thirdY = (b.maxY - b.minY) / 3;
  const cx = b.minX + thirdX + Math.random() * thirdX;
  const cy = b.minY + thirdY + Math.random() * thirdY;
  const r0 = Math.max(
    Math.hypot(cx - b.minX, cy - b.minY), Math.hypot(cx - b.maxX, cy - b.minY),
    Math.hypot(cx - b.minX, cy - b.maxY), Math.hypot(cx - b.maxX, cy - b.maxY));
  const total = room.settings.battleSeconds * 1000;
  const now = Date.now();
  room.storm = {
    cx, cy, r0, radius: r0, finalRadius: STORM_FINAL_RADIUS, dps: STORM_DPS,
    startAt: now + total * STORM_START_FRAC,
    endAt: now + total * STORM_END_FRAC
  };
}

function stormRadiusAt(room, now) {
  const s = room.storm;
  if (!s) return Infinity;
  if (now <= s.startAt) return s.r0;
  if (now >= s.endAt) return s.finalRadius;
  const t = (now - s.startAt) / (s.endAt - s.startAt);
  return s.r0 + (s.finalRadius - s.r0) * t;
}
const outsideStorm = (room, p, radius) =>
  room.storm && Math.hypot(p.x - room.storm.cx, p.y - room.storm.cy) > radius;

// ── 보급 ──────────────────────────────────────────────────────────────
function blockedAt(map, x, y, radius) {
  const fixed = resolveWalls(map, x, y, radius);
  if (Math.hypot(fixed.x - x, fixed.y - y) > 0.01) return true;
  return !!pitAt(map, x, y);              // 구덩이 위에는 보급을 떨어뜨리지 않는다
}

function rollDropKind() {
  let roll = Math.random() * DROP_WEIGHT_TOTAL;
  for (const kind of DROP_KINDS) { roll -= kind.weight; if (roll < 0) return kind; }
  return DROP_KINDS[DROP_KINDS.length - 1];
}

function spawnDrop(room) {
  if (room.drops.length >= DROP_MAX_ALIVE) return null;
  const b = BATTLEFIELD.bounds;
  const safe = room.storm ? Math.max(STORM_FINAL_RADIUS, room.storm.radius * 0.85) : 200;
  const cx = room.storm ? room.storm.cx : 0, cy = room.storm ? room.storm.cy : 0;
  for (let tries = 0; tries < 90; tries++) {
    const angle = Math.random() * Math.PI * 2;
    const dist = Math.sqrt(Math.random()) * safe;
    const x = cx + Math.cos(angle) * dist;
    const y = cy + Math.sin(angle) * dist;
    if (x < b.minX + 24 || x > b.maxX - 24 || y < b.minY + 24 || y > b.maxY - 24) continue;
    if (blockedAt(BATTLEFIELD, x, y, DROP_RADIUS + 2)) continue;
    const kind = rollDropKind();
    const drop = {
      id: ++room.dropSeq, kind: kind.key, label: kind.label, x, y, at: Date.now(),
      amount: kind.key === 'money'
        ? Math.round((kind.minAmount + Math.random() * (kind.maxAmount - kind.minAmount)) / 10) * 10
        : Math.round(kind.healFrac * 100)
    };
    room.drops.push(drop);
    announce(room, `보급이 투하됨 · ${kind.label}`, 'drop');
    return drop;
  }
  return null;
}

function collectDrops(room) {
  const reach = DROP_RADIUS + PLAYER_RADIUS + DROP_PICKUP_PAD;
  for (const p of activePlayers(room)) {
    if (!p.alive) continue;
    for (let i = room.drops.length - 1; i >= 0; i--) {
      const d = room.drops[i];
      if (Math.hypot(p.x - d.x, p.y - d.y) > reach) continue;
      room.drops.splice(i, 1);
      if (d.kind === 'money') {
        p.money += d.amount;
        p.looted += d.amount;
        p.battleGain += d.amount;
        notify(room, p, `보급 회수 · ${d.amount.toLocaleString()}S`, 'success');
      } else {
        const heal = Math.round(statsFor(p).maxHp * (d.amount / 100));
        const before = p.hp;
        p.hp = Math.min(p.maxHp, p.hp + heal);
        p.healed += p.hp - before;
        notify(room, p, `힐팩 · 체력 +${Math.round(p.hp - before)} (최대 체력의 ${d.amount}%)`, 'success');
      }
    }
  }
}

// ── 배치 ──────────────────────────────────────────────────────────────
// 전장 스폰은 매 라운드 무작위로 뽑는다 — 고정 스폰은 자리 외우기가 되고,
// 서로 너무 가까우면 스폰 직후 즉시 교전이 벌어지므로 최소 거리를 지킨다.
// 자리를 못 채우면 최소 거리를 조금씩 완화하고, 그래도 안 되면 고정 스폰으로 채운다.
const SPAWN_MARGIN = 26;
const SPAWN_MIN_DIST = 130;

function randomBattleSpawns(count) {
  const b = BATTLEFIELD.bounds;
  const spots = [];
  let minDist = SPAWN_MIN_DIST;
  for (let tries = 0; spots.length < count && tries < 4000; tries++) {
    if (tries > 0 && tries % 500 === 0) minDist = Math.max(60, minDist - 15);
    const x = b.minX + SPAWN_MARGIN + Math.random() * (b.maxX - b.minX - SPAWN_MARGIN * 2);
    const y = b.minY + SPAWN_MARGIN + Math.random() * (b.maxY - b.minY - SPAWN_MARGIN * 2);
    if (blockedAt(BATTLEFIELD, x, y, PLAYER_RADIUS + 4)) continue;
    if (spots.some(s => Math.hypot(s.x - x, s.y - y) < minDist)) continue;
    spots.push({ x, y });
  }
  for (let i = spots.length; i < count; i++) {
    spots.push(BATTLEFIELD.spawns[i % BATTLEFIELD.spawns.length]);
  }
  return spots;
}

function placeAll(room, map) {
  const all = activePlayers(room);
  const spots = map === BATTLEFIELD ? randomBattleSpawns(all.length) : map.spawns;
  all.forEach((p, i) => {
    const spot = spots[i % spots.length];
    const fixed = resolveWalls(map, spot.x, spot.y, PLAYER_RADIUS);
    const stats = statsFor(p);
    p.x = fixed.x; p.y = fixed.y;
    p.angle = Math.atan2(-fixed.y, -fixed.x);
    p.maxHp = stats.maxHp;
    p.hp = p.maxHp;
    p.alive = true;
    p.rank = null;
    p.deathAt = 0;
    p.deathCause = null;
    p.damageLog = new Map();
    p.attackReadyAt = 0;
    p.dashCharges = stats.dashCharges;
    p.dashRechargeAt = 0;
    p.invulnUntil = 0;
    p.lastMoveAt = Date.now();
    p.moveIgnoreUntil = 0;
  });
}

// 진행 중 입장자는 다른 플레이어를 건드리지 않고 구석에 세워둔다.
function seatSpectator(room, p) {
  const map = mapFor(room.phase);
  p.x = map.bounds.minX + 14; p.y = map.bounds.minY + 14;
  p.alive = false; p.spectating = true; p.rank = null;
}

// ── 룰렛 진행 ─────────────────────────────────────────────────────────
function openBetting(room) {
  const w = room.wheel;
  w.cycle += 1;
  w.state = 'BETTING';
  w.endsAt = Date.now() + ROULETTE_BET_MS;
  w.slot = null;
  w.number = null;
  for (const p of room.players.values()) p.bets = {};
}

function startSpin(room) {
  const w = room.wheel;
  w.state = 'SPINNING';
  w.endsAt = Date.now() + ROULETTE_SPIN_MS;
  w.slot = crypto.randomInt(WHEEL_SLOTS);        // 균일 난수. 칸 배치가 곧 확률이다.
  w.number = WHEEL_LAYOUT[w.slot];
  w.spinSeq += 1;
}

function settleSpin(room) {
  const w = room.wheel;
  const hit = w.number;
  for (const p of room.players.values()) {
    const staked = Object.values(p.bets).reduce((a, b) => a + b, 0);
    if (!staked) { p.bets = {}; continue; }
    const onHit = p.bets[hit] || 0;
    const payout = onHit ? onHit * (hit + 1) : 0;
    p.money += payout;
    p.won += payout;
    p.lastWheel = {
      seq: ++room.eventSeq, cycle: w.cycle, number: hit,
      staked, onHit, payout, net: payout - staked, at: Date.now()
    };
    if (payout > staked) notify(room, p, `룰렛 ${hit} 적중 · ${payout.toLocaleString()}S 회수 (+${(payout - staked).toLocaleString()}S)`, 'jackpot');
    else if (payout) notify(room, p, `룰렛 ${hit} 적중 · ${payout.toLocaleString()}S 회수 (${(payout - staked).toLocaleString()}S)`, 'success');
    else notify(room, p, `룰렛 ${hit} · ${staked.toLocaleString()}S 손실`, 'fail');
    p.bets = {};
  }
  room.wheelHistory.push({ cycle: w.cycle, number: hit, slot: w.slot });
  if (room.wheelHistory.length > 40) room.wheelHistory.shift();
  w.state = 'RESULT';
  w.endsAt = Date.now() + 1600;
}

function stopWheel(room) {
  // 준비 페이즈가 끝나면 진행 중인 베팅은 돌려준다. 돌지 않은 판에 돈을 먹지 않는다.
  for (const p of room.players.values()) {
    const staked = Object.values(p.bets).reduce((a, b) => a + b, 0);
    if (staked) {
      p.money += staked;
      p.wagered -= staked;
      notify(room, p, `준비 시간 종료 · 베팅 ${staked.toLocaleString()}S 반환`, '');
    }
    p.bets = {};
  }
  const w = room.wheel;
  w.state = 'IDLE';
  w.endsAt = 0;
  w.slot = null;
  w.number = null;
}

function closeWheel(room) {
  stopWheel(room);
  room.wheel.state = 'CLOSED';
  room.wheel.endsAt = 0;
}

function tickWheel(room, now) {
  const w = room.wheel;
  if (room.phase !== 'PREP') { if (w.state !== 'IDLE') stopWheel(room); return; }
  const closing = room.phaseEndsAt !== null && room.phaseEndsAt - now <= ROULETTE_CLOSE_LEAD_MS;
  if (w.state === 'CLOSED') return;
  if (closing && (w.state === 'IDLE' || w.state === 'BETTING')) { closeWheel(room); return; }
  if (w.state === 'IDLE') { openBetting(room); return; }
  if (now < w.endsAt) return;
  if (w.state === 'BETTING') startSpin(room);
  else if (w.state === 'SPINNING') settleSpin(room);
  else if (w.state === 'RESULT') { if (closing) closeWheel(room); else openBetting(room); }
}

// ── 페이즈 ────────────────────────────────────────────────────────────
function setPhase(room, next, seconds) {
  room.phase = next;
  room.phaseEndsAt = Number.isFinite(seconds) ? Date.now() + seconds * 1000 : null;
  if (next === 'PREP') {
    // 준비 페이즈 시작에 관전자가 정식 참가한다.
    for (const p of room.players.values()) { p.spectating = false; p.ready = false; }
    assignTeams(room);
    placeAll(room, PLAZA);
    for (const p of room.players.values()) {
      p.alive = true; p.kills = 0; p.assists = 0; p.payout = 0; p.roundPoints = 0;
      p.battleGain = 0; p.looted = 0; p.healed = 0; p.bounty = 0;
      p.pointsBreakdown = null; p.payoutBreakdown = null;
    }
    room.goblinId = null;
    room.roundWinnerTeam = null;
    openBetting(room);
  }
  if (next === 'COUNTDOWN') {
    stopWheel(room);
    // 준비 라운드 종료 잔액을 기록한다. 이월 계산의 첫 항이다.
    for (const p of room.players.values()) p.prepEndMoney = p.money;
    pickGoblin(room);
    placeAll(room, BATTLEFIELD);
    room.drops = [];
    room.storm = null;
  }
  if (next === 'BATTLE') {
    room.battleStartedAt = Date.now();
    initStorm(room);
    room.nextDropAt = room.battleStartedAt + room.settings.dropSeconds * 1000;
  }
  if (next !== 'PREP') for (const p of room.players.values()) p.begUntil = 0;
  if (next === 'RESULT') stopWheel(room);
  if (next === 'LOBBY') { room.goblinId = null; }
  broadcast(room);
}

// 황금 고블린 — 전투 단계 시작 전, 가장 장비가 좋은 유저를 지정한다.
function pickGoblin(room) {
  const pool = activePlayers(room);
  if (pool.length < 2) { room.goblinId = null; return; }
  const best = pool.slice().sort((a, b) => {
    const dv = combatValue(b) - combatValue(a);
    if (dv) return dv;
    return b.score - a.score;
  })[0];
  // 아무도 투자하지 않았다면 현상금을 걸지 않는다
  if (!best || combatValue(best) <= 0) { room.goblinId = null; return; }
  room.goblinId = best.id;
  announce(room, `황금 고블린: ${best.name} (전투력 ${combatValue(best).toLocaleString()}S) · 킬 관여 시 현상금`, 'goblin');
}

function resetPlayerForMatch(room, p) {
  p.money = room.settings.baseMoney;
  p.enhance = { weapon: 0, armor: 0 };
  p.trinket = null;
  p.attempts = { weapon: 0, armor: 0 };
  p.fails = { weapon: 0, armor: 0 };
  p.score = 0; p.roundPoints = 0; p.rank = null; p.prevRank = null; p.payout = 0;
  p.kills = 0; p.assists = 0; p.deathAt = 0; p.deathCause = null; p.damageLog = new Map();
  p.spentThisRound = 0; p.wagered = 0; p.won = 0;
  p.bets = {}; p.lastWheel = null; p.lastPull = null; p.lastEnhance = null;
  p.dummyHit = null; p.spectating = false; p.ready = false;
  p.begUntil = 0; p.begReadyAt = 0; p.given = 0; p.received = 0;
  p.looted = 0; p.healed = 0; p.bounty = 0; p.survivalMs = 0; p.survivalFrac = 0;
  p.prepEndMoney = 0; p.battleGain = 0; p.carry = null;
  p.pointsBreakdown = null; p.payoutBreakdown = null;
  p.maxHp = statsFor(p).maxHp; p.hp = p.maxHp;
}

function resetMatch(room) {
  room.round = 1;
  room.wheelHistory = [];
  room.announcements = [];
  room.teamWins = [0, 0];
  room.roundWinnerTeam = null;
  room.final = null;
  room.goblinId = null;
  for (const p of room.players.values()) resetPlayerForMatch(room, p);
}

// 다음 준비 라운드: 기본 재화 + 상한 이내 이월 재화 (문서 5절 · 8절)
function startNextRound(room) {
  for (const p of room.players.values()) {
    if (!room.settings.carryEnhance) {
      p.enhance = { weapon: 0, armor: 0 };
      p.trinket = null;
    }
    p.money = room.settings.baseMoney + (p.carry ? p.carry.carried : 0);
    p.prevRank = p.rank;
    p.spentThisRound = 0;
    p.wagered = 0; p.won = 0;
    p.rank = null;
    p.begUntil = 0;
    p.ready = false;
    p.lastPull = null; p.lastEnhance = null; p.dummyHit = null;
    p.maxHp = statsFor(p).maxHp;
    p.hp = p.maxHp;
  }
}

// 등수: 생존 > 늦게 죽음 > 킬 > 어시스트 > 잔존 체력.
function settleRound(room) {
  const now = Date.now();
  const battleMs = Math.max(1, now - room.battleStartedAt);
  const ordered = activePlayers(room).sort((a, b) => {
    if (a.alive !== b.alive) return a.alive ? -1 : 1;
    if (!a.alive && !b.alive && a.deathAt !== b.deathAt) return b.deathAt - a.deathAt;
    if (a.kills !== b.kills) return b.kills - a.kills;
    if (a.assists !== b.assists) return b.assists - a.assists;
    return b.hp - a.hp;
  });
  ordered.forEach((p, i) => {
    p.rank = i + 1;
    const lived = Math.max(0, (p.alive ? now : (p.deathAt || now)) - room.battleStartedAt);
    p.survivalMs = lived;
    p.survivalFrac = Math.max(0, Math.min(1, lived / battleMs));

    // 라운드 점수 = 등수 + 킬 + 어시스트 (문서 14절)
    const rankPts = rankPointsFor(i, ordered.length);
    const killPts = p.kills * KILL_POINTS;
    const assistPts = p.assists * ASSIST_POINTS;
    p.pointsBreakdown = { rank: rankPts, kills: killPts, assists: assistPts };
    p.roundPoints = rankPts + killPts + assistPts;
    p.score += p.roundPoints;

    // 재화 보상 — 킬·어시스트·현상금은 전투 중 즉시 지급했고, 여기서 등수·생존을 더한다.
    const rankPay = payoutFor(i, ordered.length);
    const survPay = Math.round(p.survivalFrac * SURVIVAL_MONEY_MAX / 10) * 10;
    p.payoutBreakdown = {
      rank: rankPay, survival: survPay,
      kills: p.kills * KILL_MONEY, assists: p.assists * ASSIST_MONEY,
      looted: p.looted, bounty: p.bounty
    };
    p.payout = rankPay + survPay;
    p.money += p.payout;
    p.battleGain += p.payout;
  });

  // 이월: min(준비 라운드 종료 잔액 + 전투 라운드 획득 재화, 최대 이월 한도)
  const cap = room.settings.carryCap;
  for (const p of room.players.values()) {
    const raw = p.prepEndMoney + p.battleGain;
    const carried = Math.max(0, Math.min(raw, cap));
    p.carry = {
      prepEnd: p.prepEndMoney, battleGain: p.battleGain, raw, cap, carried,
      lost: Math.max(0, raw - carried),
      base: room.settings.baseMoney, next: room.settings.baseMoney + carried
    };
  }

  // 팀전 라운드 승패 — 마지막까지 남은 팀, 없으면 생존 인원 → 킬 합계
  if (room.settings.mode === 'team') {
    const tally = TEAMS.map(t => {
      const members = activePlayers(room).filter(p => p.team === t.index);
      return {
        index: t.index,
        alive: members.filter(p => p.alive).length,
        kills: members.reduce((a, p) => a + p.kills, 0),
        points: members.reduce((a, p) => a + p.roundPoints, 0)
      };
    }).sort((a, b) => b.alive - a.alive || b.kills - a.kills || b.points - a.points);
    if (tally.length > 1 && (tally[0].alive !== tally[1].alive || tally[0].kills !== tally[1].kills)) {
      room.roundWinnerTeam = tally[0].index;
      room.teamWins[tally[0].index] += 1;
      announce(room, `${TEAMS[tally[0].index].label} 팀 라운드 승리`, 'team');
    } else {
      room.roundWinnerTeam = null;
      announce(room, '라운드 무승부', 'team');
    }
  }
}

// ── 최종 정산과 동점 판정 (문서 14절) ────────────────────────────────
// 총 점수 → 전투력(장비·소모품 가격 합) → 서버 난수. 보유 재화는 판정에서 제외한다.
function buildFinal(room) {
  const pool = activePlayers(room).slice();
  const ticket = new Map();
  for (const p of pool) ticket.set(p.id, crypto.randomInt(1e9));

  const rows = pool.map(p => ({
    id: p.id, name: p.name, color: p.color, bot: p.bot, team: p.team,
    score: p.score, combatValue: combatValue(p), money: p.money,
    kills: p.kills, assists: p.assists, ticket: ticket.get(p.id)
  }));
  rows.sort((a, b) => b.score - a.score || b.combatValue - a.combatValue || a.ticket - b.ticket);
  rows.forEach((row, i) => row.place = i + 1);

  // 난수까지 내려간 동점 묶음만 연출 대상으로 남긴다.
  const dramas = [];
  let i = 0;
  while (i < rows.length) {
    let j = i;
    while (j + 1 < rows.length
      && rows[j + 1].score === rows[i].score
      && rows[j + 1].combatValue === rows[i].combatValue) j += 1;
    if (j > i) {
      const members = rows.slice(i, j + 1);
      dramas.push({
        kind: members.length === 2 ? 'coin' : 'roulette',
        members: members.map(m => ({ id: m.id, name: m.name, color: m.color, ticket: m.ticket })),
        winnerId: members[0].id, winnerName: members[0].name
      });
    }
    i = j + 1;
  }

  const teamRows = room.settings.mode === 'team' ? TEAMS.map(t => ({
    ...t,
    wins: room.teamWins[t.index] || 0,
    score: rows.filter(r => r.team === t.index).reduce((a, r) => a + r.score, 0),
    money: rows.filter(r => r.team === t.index).reduce((a, r) => a + r.money, 0)
  })).sort((a, b) => b.wins - a.wins || b.score - a.score) : null;

  room.final = {
    mode: room.settings.mode,
    rows,
    dramas,
    teams: teamRows,
    // 문서 14절: 팀 승패와 개인 누적 전투 점수의 결합 방식은 미결정 사항이다.
    teamNote: room.settings.mode === 'team'
      ? '팀 승패와 개인 누적 전투 점수의 결합 방식은 문서상 미결정 사항입니다. 둘을 따로 보여줍니다.'
      : null
  };
}

function advancePhase(room) {
  if (room.phase === 'PREP') setPhase(room, 'COUNTDOWN', COUNTDOWN_SECONDS);
  else if (room.phase === 'COUNTDOWN') setPhase(room, 'BATTLE', room.settings.battleSeconds);
  else if (room.phase === 'BATTLE') { settleRound(room); setPhase(room, 'RESULT', RESULT_SECONDS); }
  else if (room.phase === 'RESULT') {
    if (room.round >= room.settings.rounds) { buildFinal(room); setPhase(room, 'FINISH', FINISH_SECONDS); }
    else { room.round += 1; startNextRound(room); setPhase(room, 'PREP', room.settings.prepSeconds); }
  } else if (room.phase === 'FINISH') returnToLobby(room);
}

function returnToLobby(room) {
  room.round = 1;
  stopWheel(room);
  room.final = null;
  room.goblinId = null;
  for (const p of room.players.values()) { p.rank = null; p.spectating = false; p.ready = false; }
  setPhase(room, 'LOBBY');
}

const sameTeam = (room, a, b) => room.settings.mode === 'team' && a.team === b.team;

function canEndBattleEarly(room) {
  const active = activePlayers(room);
  if (active.length <= 1) return false;
  if (room.settings.mode === 'team') {
    const teams = new Set(active.filter(p => p.alive).map(p => p.team));
    return teams.size <= 1;
  }
  return active.filter(p => p.alive).length <= 1;
}

// ── 전투 ──────────────────────────────────────────────────────────────
function creditDamage(target, attacker, amount) {
  const entry = target.damageLog.get(attacker.id) || { amount: 0, at: 0 };
  entry.amount += amount;
  entry.at = Date.now();
  target.damageLog.set(attacker.id, entry);
}

function payKill(room, p, amount, label) {
  if (amount <= 0) return;
  p.money += amount;
  p.battleGain += amount;
  if (label === 'bounty') p.bounty += amount;
}

function registerDeath(room, target, killerId, cause = 'combat') {
  const now = Date.now();
  target.alive = false;
  target.deathAt = now;
  target.deathCause = cause;
  const wasGoblin = room.goblinId === target.id;

  if (cause === 'storm' || cause === 'fall') {
    const text = cause === 'storm' ? '자기장에 휩쓸렸습니다' : '구덩이에 떨어졌습니다';
    notify(room, target, text, 'fail');
    announce(room, `${target.name} ${cause === 'storm' ? '자기장 사망' : '낙사'}`, cause);
    // 문서 12절: 피해를 입은 뒤 자기장 사망도 어시스트로 처리한다. 킬은 주지 않는다.
    if (cause === 'storm') {
      for (const [id, entry] of target.damageLog) {
        if (id === target.id) continue;
        if (now - entry.at > ASSIST_WINDOW_MS) continue;
        const helper = room.players.get(id);
        if (!helper) continue;
        helper.assists += 1;
        payKill(room, helper, ASSIST_MONEY, 'assist');
        if (wasGoblin) { payKill(room, helper, GOBLIN_ASSIST_BOUNTY, 'bounty'); }
        notify(room, helper, `${target.name} 어시스트 (자기장)`, '');
      }
    }
    return;
  }

  const killer = room.players.get(killerId);
  if (killer && killer.id !== target.id) {
    killer.kills += 1;
    payKill(room, killer, KILL_MONEY, 'kill');
    if (wasGoblin) {
      payKill(room, killer, GOBLIN_KILL_BOUNTY, 'bounty');
      notify(room, killer, `황금 고블린 ${target.name} 처치 · 현상금 ${GOBLIN_KILL_BOUNTY.toLocaleString()}S`, 'jackpot');
      announce(room, `${killer.name} 이(가) 황금 고블린 ${target.name} 을(를) 처치 · 현상금 ${GOBLIN_KILL_BOUNTY.toLocaleString()}S`, 'goblin');
    } else {
      notify(room, killer, `${target.name} 처치 · ${KILL_MONEY.toLocaleString()}S`, 'success');
    }
  }
  for (const [id, entry] of target.damageLog) {
    if (id === killerId || id === target.id) continue;
    if (now - entry.at > ASSIST_WINDOW_MS) continue;
    const helper = room.players.get(id);
    if (!helper) continue;
    helper.assists += 1;
    payKill(room, helper, ASSIST_MONEY, 'assist');
    if (wasGoblin) payKill(room, helper, GOBLIN_ASSIST_BOUNTY, 'bounty');
    notify(room, helper, `${target.name} 어시스트${wasGoblin ? ' (현상금)' : ''}`, '');
  }
  notify(room, target, killer ? `${killer.name}에게 처치되었습니다` : '탈락했습니다', 'fail');
  if (wasGoblin) room.goblinId = null;
}

// 준비 광장의 허수아비 — 리치·데미지 확인용. 실제 피해는 없다.
function hitDummy(room, p) {
  const now = Date.now();
  if (now < p.attackReadyAt) return;
  const stats = statsFor(p);
  p.attackReadyAt = now + stats.attackCooldown;
  p.attackSeq = ++room.attackSeq;
  p.attackAt = now;
  for (const s of PLAZA.stations) {
    if (s.kind !== 'dummy') continue;
    const dx = s.x - p.x, dy = s.y - p.y;
    const dist = Math.hypot(dx, dy) - s.radius;
    if (dist > stats.range) continue;
    const bearing = Math.atan2(dy, dx);
    const diff = Math.atan2(Math.sin(bearing - p.angle), Math.cos(bearing - p.angle));
    if (Math.abs(diff) > ATTACK_ARC) continue;
    p.dummyHit = { seq: ++room.eventSeq, station: s.key, damage: stats.damage,
                   range: stats.range, cooldown: stats.attackCooldown, at: now };
    room.dummyShake[s.key] = now;                      // 전원에게 흔들림 브로드캐스트
    notify(room, p, `허수아비 · 피해 ${stats.damage} · 사거리 ${stats.range} · 주기 ${stats.attackCooldown}ms`, '');
    broadcast(room);                                    // 다음 틱을 기다리지 않고 즉시 보여준다
    return;
  }
}

function attack(room, attacker) {
  if (attacker.spectating) return;
  if (room.phase === 'PREP') return hitDummy(room, attacker);
  if (room.phase !== 'BATTLE' || !attacker.alive) return;
  const now = Date.now();
  if (now < attacker.attackReadyAt) return;
  const stats = statsFor(attacker);
  attacker.attackReadyAt = now + stats.attackCooldown;
  attacker.attackSeq = ++room.attackSeq;
  attacker.attackAt = now;
  for (const target of room.players.values()) {
    if (target.id === attacker.id || !target.alive || target.spectating) continue;
    if (sameTeam(room, attacker, target) && !room.settings.friendlyFire) continue;
    if (now < target.invulnUntil) continue;                       // 대시 무적
    const dx = target.x - attacker.x, dy = target.y - attacker.y;
    if (Math.hypot(dx, dy) > stats.range) continue;
    const bearing = Math.atan2(dy, dx);
    const diff = Math.atan2(Math.sin(bearing - attacker.angle), Math.cos(bearing - attacker.angle));
    if (Math.abs(diff) > ATTACK_ARC) continue;
    if (!hasLineOfSight(BATTLEFIELD, attacker.x, attacker.y, target.x, target.y)) continue;
    target.hp = Math.max(0, target.hp - stats.damage);
    target.hitAt = now;
    creditDamage(target, attacker, stats.damage);
    if (target.hp <= 0) registerDeath(room, target, attacker.id);
  }
}

function dash(room, p, dirAngle) {
  if (!['PREP', 'BATTLE'].includes(room.phase) || !p.alive || p.spectating) return;
  if (p.dashCharges <= 0) return;
  const stats = statsFor(p);
  const now = Date.now();
  p.dashCharges -= 1;
  if (!p.dashRechargeAt || p.dashRechargeAt <= now) p.dashRechargeAt = now + stats.dashRecharge;
  if (room.phase === 'BATTLE') p.invulnUntil = now + DASH_INVULN_MS;
  const dir = Number.isFinite(dirAngle) ? dirAngle : p.angle;
  const map = mapFor(room.phase);
  // 대시로 벽을 통과할 수 없다 — 잘게 나눠 밀어보고 막히면 그 자리에 선다.
  const steps = 8;
  let x = p.x, y = p.y;
  for (let i = 0; i < steps; i++) {
    const nx = x + Math.cos(dir) * stats.dashDist / steps;
    const ny = y + Math.sin(dir) * stats.dashDist / steps;
    const fixed = resolveWalls(map, nx, ny, PLAYER_RADIUS);
    if (Math.hypot(fixed.x - x, fixed.y - y) < stats.dashDist / steps * 0.5) break;
    x = fixed.x; y = fixed.y;
  }
  p.x = x; p.y = y;
  p.lastMoveAt = now;
  p.moveIgnoreUntil = now + 140;
  checkPit(room, p);
}

function checkPit(room, p) {
  if (room.phase !== 'BATTLE' || !p.alive || p.spectating) return;
  if (pitAt(BATTLEFIELD, p.x, p.y)) registerDeath(room, p, null, 'fall');
}

function rechargeDash(room, p, now) {
  const stats = statsFor(p);
  if (p.dashCharges >= stats.dashCharges) { p.dashRechargeAt = 0; return; }
  if (!p.dashRechargeAt) { p.dashRechargeAt = now + stats.dashRecharge; return; }
  if (now < p.dashRechargeAt) return;
  p.dashCharges = Math.min(stats.dashCharges, p.dashCharges + 1);
  p.dashRechargeAt = p.dashCharges >= stats.dashCharges ? 0 : now + stats.dashRecharge;
}

// 클라이언트가 보내온 좌표를 검증한다. 지난 패킷 이후 흐른 시간에 낼 수 있는
// 거리만 허용하고, 벽을 통과했으면 밀어낸다.
function applyMove(room, p, x, y, angle) {
  const map = mapFor(room.phase);
  const now = Date.now();
  if (Number.isFinite(angle)) p.angle = angle;
  if (!Number.isFinite(x) || !Number.isFinite(y)) return;
  if (now < (p.moveIgnoreUntil || 0)) { p.lastMoveAt = now; return; }
  const elapsed = Math.min(1.2, Math.max(0.016, (now - (p.lastMoveAt || now)) / 1000));
  p.lastMoveAt = now;
  const allowed = statsFor(p).speed * elapsed * 1.4 + 6;
  let dx = x - p.x, dy = y - p.y;
  const dist = Math.hypot(dx, dy);
  if (dist > allowed) { dx *= allowed / dist; dy *= allowed / dist; }
  if (!p.alive) {
    // 유령은 벽·구덩이를 통과한다. 경계만 지킨다.
    const b = map.bounds;
    p.x = Math.max(b.minX + PLAYER_RADIUS, Math.min(b.maxX - PLAYER_RADIUS, p.x + dx));
    p.y = Math.max(b.minY + PLAYER_RADIUS, Math.min(b.maxY - PLAYER_RADIUS, p.y + dy));
    return;
  }
  const fixed = resolveWalls(map, p.x + dx, p.y + dy, PLAYER_RADIUS);
  p.x = fixed.x; p.y = fixed.y;
  checkPit(room, p);
}

// ── 스테이션 ──────────────────────────────────────────────────────────
function nearStation(room, p, kind) {
  const map = mapFor(room.phase);
  const station = map.stations.find(s => s.kind === kind);
  if (!station) return false;
  return Math.hypot(p.x - station.x, p.y - station.y) <= station.reach;
}

// 상점 — 장신구 랜덤 뽑기. 획득하면 선택·보류 없이 즉시 장착하고 기존 것을 교체한다.
function pullTrinket(room, p) {
  if (room.phase !== 'PREP' || p.spectating) return;
  if (!nearStation(room, p, 'shop')) return notify(room, p, '상점에서 멀리 있습니다', 'fail');
  // 준비 종료 직전 마감은 룰렛(결과를 기다리는 도박)에만 적용한다.
  // 뽑기·강화는 즉시 결과가 나오므로 준비 시간이 끝날 때까지 쓸 수 있다.
  if (p.money < TRINKET_PRICE) return notify(room, p, '재화가 부족합니다', 'fail');
  p.money -= TRINKET_PRICE;
  p.spentThisRound += TRINKET_PRICE;
  const before = p.trinket;
  const item = rollTrinket();
  p.trinket = { ...item };
  const stats = statsFor(p);
  p.dashCharges = Math.min(p.dashCharges, stats.dashCharges);
  if (p.dashCharges < stats.dashCharges && !p.dashRechargeAt) p.dashRechargeAt = Date.now() + stats.dashRecharge;
  p.lastPull = {
    seq: ++room.eventSeq, item: p.trinket, replaced: before,
    price: TRINKET_PRICE, at: Date.now()
  };
  notify(room, p, `${item.label} 획득 · 즉시 장착`
    + (before ? ` (${before.label} 교체)` : ''), item.grade === 'epic' ? 'jackpot' : 'success');
}

// 모루 — 확률 강화. 재화를 먼저 차감하고, 실패하면 재화만 소모된다.
function tryEnhance(room, p, slot) {
  if (room.phase !== 'PREP' || p.spectating) return;
  if (!ENHANCE_SLOTS.includes(slot)) return;
  if (!nearStation(room, p, 'anvil')) return notify(room, p, '모루에서 멀리 있습니다', 'fail');
  const level = p.enhance[slot];
  const cost = enhanceCost(slot, level);
  if (cost === null) return notify(room, p, `이미 +${MAX_ENHANCE} 입니다`, '');
  if (p.money < cost) return notify(room, p, '재화가 부족합니다', 'fail');
  const chance = enhanceChance(level);
  p.money -= cost;
  p.spentThisRound += cost;
  p.attempts[slot] += 1;
  const success = Math.random() < chance;
  if (success) {
    p.enhance[slot] = level + 1;
    p.maxHp = statsFor(p).maxHp;
    p.hp = p.maxHp;
    p.dashCharges = statsFor(p).dashCharges;
  } else {
    p.fails[slot] += 1;
  }
  p.lastEnhance = {
    seq: ++room.eventSeq, slot, from: level, to: p.enhance[slot],
    cost, chance, success, at: Date.now()
  };
  notify(room, p, success
    ? `${SLOT_LABEL[slot]} +${level + 1} 성공 · ${cost.toLocaleString()}S`
    : `${SLOT_LABEL[slot]} +${level + 1} 실패 · ${cost.toLocaleString()}S 소모 (단계 유지)`,
    success ? 'success' : 'fail');
}

function placeBet(room, p, number, amount) {
  if (room.phase !== 'PREP' || p.spectating) return;
  if (room.wheel.state !== 'BETTING') return notify(room, p, '지금은 베팅할 수 없습니다', 'fail');
  if (!nearStation(room, p, 'wheel')) return notify(room, p, '룰렛에서 멀리 있습니다', 'fail');
  const n = Number(number);
  if (!WHEEL_NUMBERS.includes(n)) return;
  let bet = Math.floor(Number(amount));
  if (!Number.isFinite(bet) || bet < MIN_BET) return notify(room, p, `최소 베팅은 ${MIN_BET}S 입니다`, 'fail');
  if (bet > p.money) bet = p.money;
  if (bet < MIN_BET) return notify(room, p, '재화가 부족합니다', 'fail');
  p.money -= bet;
  p.wagered += bet;
  p.bets[n] = (p.bets[n] || 0) + bet;
  notify(room, p, `${n} 에 ${bet.toLocaleString()}S 베팅 (적중 시 ${(bet * (n + 1)).toLocaleString()}S)`, '');
}

function clearBets(room, p) {
  if (room.phase !== 'PREP' || room.wheel.state !== 'BETTING') return;
  const staked = Object.values(p.bets).reduce((a, b) => a + b, 0);
  if (!staked) return;
  p.money += staked;
  p.wagered -= staked;
  p.bets = {};
  notify(room, p, `베팅 취소 · ${staked.toLocaleString()}S 반환`, '');
}

function beg(room, p) {
  if (room.phase !== 'PREP' || p.spectating) return;
  const now = Date.now();
  if (now < p.begReadyAt) return;
  p.begReadyAt = now + BEG_COOLDOWN_MS;
  p.begUntil = now + BEG_SHOW_MS;
  announce(room, `${p.name} 님이 구걸합니다`, 'beg');
}

// 적선 — 팀전에서도 재화 이전은 이것뿐이다. 팀 공용 지갑은 없다.
function give(room, p, targetId, amount) {
  if (room.phase !== 'PREP' || p.spectating) return;
  const target = room.players.get(targetId);
  if (!target || target.id === p.id) return;
  if (target.spectating) return notify(room, p, '관전자에게는 적선할 수 없습니다', 'fail');
  if (Math.hypot(p.x - target.x, p.y - target.y) > CHARITY_REACH) {
    return notify(room, p, `${target.name} 에게서 멀리 있습니다`, 'fail');
  }
  let amt = Math.floor(Number(amount));
  if (!Number.isFinite(amt) || amt < CHARITY_MIN) {
    return notify(room, p, `최소 적선은 ${CHARITY_MIN}S 입니다`, 'fail');
  }
  if (amt > p.money) amt = p.money;
  if (amt < CHARITY_MIN) return notify(room, p, '재화가 부족합니다', 'fail');
  p.money -= amt;
  target.money += amt;
  p.given += amt;
  target.received += amt;
  target.begUntil = 0;
  notify(room, p, `${target.name} 에게 ${amt.toLocaleString()}S 적선`, 'success');
  notify(room, target, `${p.name} 님이 ${amt.toLocaleString()}S 적선했습니다`, 'jackpot');
  announce(room, `${p.name} → ${target.name} ${amt.toLocaleString()}S 적선`, 'alms');
}

// ── 봇 AI ─────────────────────────────────────────────────────────────
// 서버가 허용하는 동일한 행동 체계만 쓴다 (문서 17절).
const BOT_PLANS = ['gambler', 'smith', 'shopper', 'hoarder'];

function botStep(room, bot, now, dt) {
  const b = bot.bot_;
  if (room.phase === 'PREP') return botPrep(room, bot, now, dt);
  if (room.phase === 'BATTLE') return botBattle(room, bot, now, dt);
  if (room.phase === 'LOBBY') { bot.ready = true; return; }
  void b;
}

function botMoveToward(room, bot, tx, ty, dt) {
  const map = mapFor(room.phase);
  const stats = statsFor(bot);
  const step = stats.speed * dt;
  const base = Math.atan2(ty - bot.y, tx - bot.x);
  // 정면이 막히면 좌우로 벌려가며 지나갈 방향을 찾는다 (간단한 회피)
  const offsets = [0, 0.5, -0.5, 1.0, -1.0, 1.6, -1.6, 2.4, -2.4, Math.PI];
  for (const off of offsets) {
    const dir = base + off;
    const nx = bot.x + Math.cos(dir) * step;
    const ny = bot.y + Math.sin(dir) * step;
    if (room.phase === 'BATTLE' && pitAt(map, nx, ny)) continue;
    const fixed = resolveWalls(map, nx, ny, PLAYER_RADIUS);
    if (Math.hypot(fixed.x - bot.x, fixed.y - bot.y) < step * 0.4) continue;
    bot.x = fixed.x; bot.y = fixed.y;
    bot.angle = dir;
    bot.lastMoveAt = Date.now();
    return true;
  }
  return false;
}

function botPrep(room, bot, now, dt) {
  const b = bot.bot_;
  if (!b.plan || now > b.wanderAt) {
    b.plan = BOT_PLANS[crypto.randomInt(BOT_PLANS.length)];
    b.wanderAt = now + 9000 + crypto.randomInt(9000);
  }
  // 준비 종료가 가까우면 준비 완료를 눌러둔다 (전원 동의 조기 종료에 참여)
  if (room.phaseEndsAt && room.phaseEndsAt - now < 8000) bot.ready = true;

  const stationKind = { gambler: 'wheel', smith: 'anvil', shopper: 'shop', hoarder: 'board' }[b.plan];
  const station = PLAZA.stations.find(s => s.kind === stationKind);
  if (!station) return;
  const dist = Math.hypot(bot.x - station.x, bot.y - station.y);
  if (dist > station.reach - 6) { botMoveToward(room, bot, station.x, station.y, dt); return; }
  if (now < b.actAt) return;
  b.actAt = now + 1200 + crypto.randomInt(1800);

  if (b.plan === 'gambler') {
    if (room.wheel.state !== 'BETTING') return;
    const budget = Math.floor(bot.money * (0.15 + Math.random() * 0.35));
    if (budget < MIN_BET) return;
    const n = WHEEL_NUMBERS[crypto.randomInt(WHEEL_NUMBERS.length)];
    placeBet(room, bot, n, budget);
  } else if (b.plan === 'smith') {
    const slot = ENHANCE_SLOTS[crypto.randomInt(ENHANCE_SLOTS.length)];
    const cost = enhanceCost(slot, bot.enhance[slot]);
    if (cost !== null && bot.money >= cost) tryEnhance(room, bot, slot);
  } else if (b.plan === 'shopper') {
    if (bot.money >= TRINKET_PRICE * 1.5) pullTrinket(room, bot);
  } else {
    // 존버 — 가끔 구걸만 한다
    if (Math.random() < 0.25) beg(room, bot);
  }
}

function botBattle(room, bot, now, dt) {
  const b = bot.bot_;
  if (!bot.alive) return;
  const stats = statsFor(bot);
  const storm = room.storm;

  // 안전 구역 밖이면 무조건 중심으로
  if (storm && outsideStorm(room, bot, storm.radius - 12)) {
    botMoveToward(room, bot, storm.cx, storm.cy, dt);
    return;
  }

  // 가장 가까운 적
  let foe = null, foeDist = Infinity;
  for (const other of room.players.values()) {
    if (other.id === bot.id || !other.alive || other.spectating) continue;
    if (sameTeam(room, bot, other)) continue;
    const d = Math.hypot(other.x - bot.x, other.y - bot.y);
    if (d < foeDist) { foeDist = d; foe = other; }
  }

  // 근처 보급이 적보다 가까우면 먼저 줍는다
  let drop = null, dropDist = Infinity;
  for (const d of room.drops) {
    const dd = Math.hypot(d.x - bot.x, d.y - bot.y);
    if (dd < dropDist) { dropDist = dd; drop = d; }
  }
  if (drop && dropDist < 120 && (!foe || dropDist < foeDist * 0.8)) {
    botMoveToward(room, bot, drop.x, drop.y, dt);
    return;
  }

  if (!foe) {
    if (storm) botMoveToward(room, bot, storm.cx, storm.cy, dt);
    return;
  }

  bot.angle = Math.atan2(foe.y - bot.y, foe.x - bot.x);
  if (foeDist <= stats.range * 0.85
      && hasLineOfSight(BATTLEFIELD, bot.x, bot.y, foe.x, foe.y)) {
    attack(room, bot);
    // 근접해서 붙었으면 가끔 대시로 파고든다
    if (bot.dashCharges > 0 && now > b.actAt && Math.random() < 0.3) {
      b.actAt = now + 2200;
      dash(room, bot, bot.angle);
    }
    return;
  }
  botMoveToward(room, bot, foe.x, foe.y, dt);
}

// ── 액션 ──────────────────────────────────────────────────────────────
function applySettings(room, patch) {
  for (const [key, spec] of Object.entries(SETTINGS_SPEC)) {
    if (!(key in patch)) continue;
    const raw = Number(patch[key]);
    if (!Number.isFinite(raw)) continue;
    const snapped = Math.round(raw / spec.step) * spec.step;
    room.settings[key] = Math.max(spec.min, Math.min(spec.max, snapped));
  }
  for (const [key, spec] of Object.entries(SETTINGS_ENUMS)) {
    if (!(key in patch)) continue;
    if (spec.options.some(o => o.value === patch[key])) room.settings[key] = patch[key];
  }
  for (const key of Object.keys(SETTINGS_FLAGS)) {
    if (key in patch) room.settings[key] = !!patch[key];
  }
  // 라운드 기본 재화를 바꾸면 로비에 있는 전원의 지갑에 바로 반영한다.
  for (const p of room.players.values()) p.money = room.settings.baseMoney;
  syncBots(room);
}

function startReason(room) {
  if (room.players.size < MIN_TOTAL_PLAYERS) return '봇을 포함해 최소 2명이 필요합니다';
  if (room.settings.mode === 'team' && room.players.size > roomCapacity(room)) {
    return `팀 인원 ${room.settings.teamSize}명 기준 최대 ${roomCapacity(room)}명입니다`;
  }
  return null;
}

// 준비 단계 조기 종료 — 전체 동의 시 (문서 7절)
function checkPrepConsensus(room) {
  if (room.phase !== 'PREP') return;
  const humans = activePlayers(room).filter(p => !p.bot);
  if (!humans.length) return;
  if (humans.every(p => p.ready)) {
    announce(room, '전원 준비 완료 · 준비 단계를 조기 종료합니다', 'ready');
    advancePhase(room);
  }
}

function handleAction(room, body) {
  const p = room.players.get(body.id);
  if (!p) return;
  const a = body.action;

  if (a === 'settings' && room.phase === 'LOBBY' && p.id === room.hostId) {
    applySettings(room, body.settings || {});
    return;
  }
  if (a === 'start' && room.phase === 'LOBBY' && p.id === room.hostId) {
    ensureMinimumRoster(room);
    if (startReason(room)) return notify(room, p, startReason(room), 'fail');
    resetMatch(room);
    setPhase(room, 'PREP', room.settings.prepSeconds);
    return;
  }
  if (a === 'lobby' && (room.phase === 'FINISH') && p.id === room.hostId) { returnToLobby(room); return; }
  if (a === 'rematch' && room.phase === 'FINISH' && p.id === room.hostId) {
    resetMatch(room);
    setPhase(room, 'PREP', room.settings.prepSeconds);
    return;
  }
  if (a === 'ready' && room.phase === 'PREP') {
    p.ready = body.ready === undefined ? !p.ready : !!body.ready;
    checkPrepConsensus(room);
    return;
  }

  if (a === 'move') {
    if (!['PREP', 'BATTLE'].includes(room.phase) || p.spectating) return;
    // 전투 중 사망자는 유령으로 계속 돌아다닐 수 있다 (살아있는 유저에게는 안 보인다)
    if (!p.alive && room.phase !== 'BATTLE') return;
    applyMove(room, p, body.x, body.y, body.angle);
    return;
  }
  if (a === 'attack') return attack(room, p);
  if (a === 'dash') return dash(room, p, body.dir);
  if (a === 'pull') return pullTrinket(room, p);
  if (a === 'enhance') return tryEnhance(room, p, body.slot);
  if (a === 'bet') return placeBet(room, p, body.number, body.amount);
  if (a === 'clearBets') return clearBets(room, p);
  if (a === 'beg') return beg(room, p);
  if (a === 'give') return give(room, p, body.target, body.amount);
}

// ── 이탈 ──────────────────────────────────────────────────────────────
function dropPlayer(room, id) {
  const existed = room.players.delete(id);
  room.clients.delete(id);
  playerRoom.delete(id);
  clearTimeout(room.disconnectTimers.get(id));
  room.disconnectTimers.delete(id);
  if (!existed) return false;
  if (id === room.hostId) {
    const nextHost = [...room.players.values()].find(p => !p.bot);
    room.hostId = nextHost ? nextHost.id : null;
    if (room.hostId) announce(room, `호스트가 ${room.players.get(room.hostId).name} 에게 이전되었습니다`, '');
  }
  if (!humanPlayers(room).length) {
    // 인간이 모두 이탈하면 매치 상태를 초기화하고 방을 닫는다
    for (const bot of [...room.players.values()]) playerRoom.delete(bot.id);
    room.players.clear();
    rooms.delete(room.code);
    return true;
  }
  assignTeams(room);
  if (room.phase === 'PREP') checkPrepConsensus(room);
  return true;
}

function scheduleDrop(room, id) {
  if (!id || !room.players.has(id)) return;
  clearTimeout(room.disconnectTimers.get(id));
  room.disconnectTimers.set(id, setTimeout(() => {
    room.disconnectTimers.delete(id);
    if (room.clients.has(id)) return;
    const name = room.players.get(id)?.name;
    if (dropPlayer(room, id)) {
      console.log(`dropped disconnected player: ${name}`);
      if (rooms.has(room.code)) broadcast(room);
    }
  }, DISCONNECT_GRACE_MS));
}

// ── HTTP ──────────────────────────────────────────────────────────────
function contentType(file) {
  return ({ '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8',
            '.css': 'text/css; charset=utf-8' })[path.extname(file)] || 'application/octet-stream';
}
function json(res, status, data) {
  res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8' });
  res.end(JSON.stringify(data));
}
function readBody(req, done) {
  let raw = '';
  req.on('data', c => raw += c);
  req.on('end', () => { let b = {}; try { b = JSON.parse(raw || '{}'); } catch {} done(b); });
}


// 방 목록에는 참가 판단에 필요한 정보만 표시한다 (문서 17절).
function roomBrief(room) {
  return {
    code: room.code, name: room.name, visibility: room.visibility,
    count: room.players.size, humans: humanPlayers(room).length,
    bots: room.players.size - humanPlayers(room).length,
    capacity: roomCapacity(room), max: MAX_PLAYERS,
    mode: room.settings.mode, teamSize: room.settings.teamSize,
    phase: room.phase, round: room.round, totalRounds: room.settings.rounds,
    locked: room.visibility === 'private' && !!room.password
  };
}

// 규칙 전체 공개. 서버가 실제로 쓰는 표를 그대로 내보내므로 화면과 어긋날 수 없다.
function configPayload() {
  return {
    project: { name: 'BettingRoyale', note: '공식 영문 표기는 BettingRoyale로 확정되었습니다' },
    settingsSpec: SETTINGS_SPEC,
    settingsEnums: SETTINGS_ENUMS,
    settingsFlags: SETTINGS_FLAGS,
    maxPlayers: MAX_PLAYERS,
    minPlayers: MIN_TOTAL_PLAYERS,
    teams: TEAMS,
    render: { verticalSquash: VERTICAL_SQUASH, playerRadius: PLAYER_RADIUS },
    maps: MAPS,
    gear: {
      weapon: { label: SLOT_LABEL.weapon, icon: SLOT_ICON.weapon, base: BASE_WEAPON, purchasable: false },
      armor: { label: SLOT_LABEL.armor, icon: SLOT_ICON.armor, base: BASE_ARMOR, purchasable: false },
      trinket: { label: SLOT_LABEL.trinket, icon: SLOT_ICON.trinket, purchasable: 'gacha' }
    },
    dash: DASH_BASE,
    trinkets: {
      price: TRINKET_PRICE, items: TRINKET_ITEMS,
      grades: TRINKET_GRADES, types: TRINKET_TYPES,
      rule: '뽑기에 성공하면 선택·보류 없이 즉시 장착하고 기존 장신구를 교체한다'
    },
    anvil: {
      maxEnhance: MAX_ENHANCE, slots: ENHANCE_SLOTS, base: ANVIL_BASE,
      chance: ENHANCE_CHANCE,
      gains: ENHANCE_GAIN,
      costTable: Object.fromEntries(ENHANCE_SLOTS.map(slot =>
        [slot, Array.from({ length: MAX_ENHANCE }, (_, lv) => enhanceCost(slot, lv))])),
      rule: '재화를 먼저 차감한다. 실패하면 재화만 소모되고 강화 단계는 오르지 않는다'
    },
    combat: { dashInvulnMs: DASH_INVULN_MS, attackArc: ATTACK_ARC, assistWindowMs: ASSIST_WINDOW_MS },
    storm: { startFrac: STORM_START_FRAC, endFrac: STORM_END_FRAC,
             finalRadius: STORM_FINAL_RADIUS, dps: STORM_DPS },
    drops: { kinds: DROP_KINDS, radius: DROP_RADIUS, pickupPad: DROP_PICKUP_PAD, maxAlive: DROP_MAX_ALIVE },
    charity: { minGive: CHARITY_MIN, reach: CHARITY_REACH, begCooldownMs: BEG_COOLDOWN_MS, begShowMs: BEG_SHOW_MS },
    roulette: {
      numbers: WHEEL_NUMBERS, slots: WHEEL_SLOTS, layout: WHEEL_LAYOUT, table: WHEEL_TABLE,
      betMs: ROULETTE_BET_MS, spinMs: ROULETTE_SPIN_MS, minBet: MIN_BET,
      closeLeadMs: ROULETTE_CLOSE_LEAD_MS,
      rule: '숫자 N 에 걸어 맞으면 베팅액 × N 을 추가로 받는다 (총 회수 = 베팅액 × (N+1))'
    },
    goblin: { killBounty: GOBLIN_KILL_BOUNTY, assistBounty: GOBLIN_ASSIST_BOUNTY,
              rule: '전투 단계 시작 전 전투력이 가장 높은 플레이어를 지정한다. 킬 관여 시 현상금을 지급한다' },
    scoring: {
      rankPointsTop: RANK_POINTS_TOP, rankPointsBottom: RANK_POINTS_BOTTOM,
      killPoints: KILL_POINTS, assistPoints: ASSIST_POINTS,
      payoutTop: ROUND_PAYOUT_TOP, payoutBottom: ROUND_PAYOUT_BOTTOM,
      killMoney: KILL_MONEY, assistMoney: ASSIST_MONEY, survivalMoneyMax: SURVIVAL_MONEY_MAX,
      finalRule: '최종 점수 = 누적 라운드 전투 점수. 보유 재화는 점수에 포함하지 않는다',
      tiebreak: ['총 점수', '전투력 (장비·소모품 가격 합)', '서버 난수 (2인 동전 뒤집기 · 다인 러시안 룰렛 연출)']
    },
    phases: { countdownSeconds: COUNTDOWN_SECONDS, resultSeconds: RESULT_SECONDS,
              finishSeconds: FINISH_SECONDS, disconnectGraceMs: DISCONNECT_GRACE_MS },
    undecided: [
      '최대 이월 금액과 적선 전·후 적용 순서',
      '팀 승리 판정과 개인 전투 점수의 결합 공식',
      '아군 공격 허용 여부',
      '적선 대상 범위와 적선 내역 공개 범위',
      '봇의 의사결정 주기와 난이도 단계',
      '무기·방어구 강화 상태 이월 설정의 허용 범위'
    ]
  };
}

const server = http.createServer((req, res) => {
  const url = new URL(req.url, `http://${req.headers.host || 'localhost'}`);

  if (req.method === 'GET' && url.pathname === '/network-info') return json(res, 200, { urls: [] });
  if (req.method === 'GET' && url.pathname === '/config') return json(res, 200, configPayload());

  // 공개방 목록 — 비공개방은 노출하지 않는다
  if (req.method === 'GET' && url.pathname === '/rooms') {
    const list = [...rooms.values()]
      .filter(r => r.visibility === 'public' && humanPlayers(r).length > 0)
      .sort((a, b) => b.createdAt - a.createdAt)
      .map(roomBrief);
    return json(res, 200, { rooms: list, updatedAt: Date.now() });
  }

  // 비공개방 코드 검색 — 존재 여부와 참가 판단 정보만 준다. 비밀번호는 입장 때 확인한다.
  if (req.method === 'POST' && url.pathname === '/rooms/find') {
    return readBody(req, body => {
      const code = String(body.code || '').trim().toUpperCase();
      const room = rooms.get(code);
      if (!room) return json(res, 404, { error: '그 코드의 방이 없습니다.' });
      json(res, 200, { room: roomBrief(room) });
    });
  }

  if (req.method === 'POST' && url.pathname === '/rooms') {
    return readBody(req, body => {
      const visibility = body.visibility === 'private' ? 'private' : 'public';
      if (visibility === 'private' && !String(body.password || '').trim()) {
        return json(res, 400, { error: '비공개방은 비밀번호가 필요합니다.' });
      }
      const room = createRoom({ name: body.name, visibility, password: body.password });
      const player = newPlayer(room, body.playerName);
      room.players.set(player.id, player);
      playerRoom.set(player.id, room.code);
      room.hostId = player.id;                     // 방 생성자가 호스트가 된다
      syncBots(room);
      placeAll(room, PLAZA);
      json(res, 200, { id: player.id, code: room.code, snapshot: snapshot(room, player.id) });
    });
  }

  if (req.method === 'POST' && url.pathname === '/join') {
    return readBody(req, body => {
      const code = String(body.code || '').trim().toUpperCase();
      const room = rooms.get(code);
      if (!room) return json(res, 404, { error: '방을 찾을 수 없습니다.' });
      if (room.visibility === 'private' && room.password
          && String(body.password || '') !== room.password) {
        return json(res, 401, { error: '비밀번호가 틀렸습니다.' });
      }
      if (room.players.size >= roomCapacity(room)) {
        // 봇 자리가 있으면 봇을 밀어내고 인간을 넣는다
        const bot = [...room.players.values()].find(p => p.bot);
        if (bot && room.phase === 'LOBBY') {
          room.players.delete(bot.id);
          playerRoom.delete(bot.id);
          room.settings.bots = Math.max(0, room.settings.bots - 1);
        } else {
          return json(res, 409, { error: '방이 가득 찼습니다.' });
        }
      }
      const player = newPlayer(room, body.name);
      room.players.set(player.id, player);
      playerRoom.set(player.id, room.code);
      if (!room.hostId) room.hostId = player.id;
      assignTeams(room);
      if (room.phase === 'LOBBY' || room.phase === 'PREP') placeAll(room, PLAZA);
      else seatSpectator(room, player);   // 진행 중 입장자는 다음 준비 단계부터 참가한다
      json(res, 200, { id: player.id, code: room.code, snapshot: snapshot(room, player.id) });
      broadcast(room);
    });
  }

  if (req.method === 'POST' && url.pathname === '/action') {
    return readBody(req, body => {
      const room = roomOfPlayer(body.id);
      if (!room) return json(res, 404, { error: '방을 찾을 수 없습니다.' });
      try { handleAction(room, body); } catch (err) { console.error('action failed', err); }
      json(res, 200, { ok: true });
      if (body.action !== 'move') broadcast(room);
    });
  }

  if (req.method === 'POST' && url.pathname === '/leave') {
    return readBody(req, body => {
      const room = roomOfPlayer(body.id);
      json(res, 200, { ok: true });
      if (!room) return;
      dropPlayer(room, body.id);
      if (rooms.has(room.code)) broadcast(room);
    });
  }

  if (req.method === 'GET' && url.pathname === '/events') {
    const id = url.searchParams.get('id');
    const room = roomOfPlayer(id);
    if (!room) { res.writeHead(404); return res.end('unknown player'); }
    res.writeHead(200, { 'Content-Type': 'text/event-stream', 'Cache-Control': 'no-cache',
                         'Connection': 'keep-alive', 'Access-Control-Allow-Origin': '*' });
    clearTimeout(room.disconnectTimers.get(id));
    room.disconnectTimers.delete(id);
    room.clients.set(id, res);
    send(res, snapshot(room, id));
    req.on('close', () => {
      if (room.clients.get(id) === res) room.clients.delete(id);
      scheduleDrop(room, id);
    });
    return;
  }

  const requested = url.pathname === '/' ? 'index.html' : url.pathname.slice(1);
  const file = path.normalize(path.join(PUBLIC, requested));
  if (!file.startsWith(PUBLIC)) { res.writeHead(403); return res.end('Forbidden'); }
  fs.readFile(file, (err, data) => {
    if (err) { res.writeHead(404); return res.end('Not found'); }
    res.writeHead(200, { 'Content-Type': contentType(file), 'Cache-Control': 'no-store' });
    res.end(data);
  });
});

// ── 틱 ────────────────────────────────────────────────────────────────
function tickRoom(room, now) {
  const dt = Math.min(0.5, Math.max(0, (now - room.lastTick) / 1000));
  room.lastTick = now;
  if (!room.players.size) return;

  tickWheel(room, now);

  for (const bot of room.players.values()) {
    if (!bot.bot || bot.spectating) continue;
    try { botStep(room, bot, now, dt); } catch (err) { console.error('bot failed', err); }
  }

  if (room.phase === 'BATTLE') {
    if (room.storm) {
      room.storm.radius = stormRadiusAt(room, now);
      for (const p of activePlayers(room)) {
        if (!p.alive || !outsideStorm(room, p, room.storm.radius)) continue;
        p.hp = Math.max(0, p.hp - room.storm.dps * dt);
        p.hitAt = now;
        if (p.hp <= 0) registerDeath(room, p, null, 'storm');
      }
    }
    for (const p of activePlayers(room)) if (p.alive) checkPit(room, p);
    if (now >= room.nextDropAt) {
      spawnDrop(room);
      room.nextDropAt = now + room.settings.dropSeconds * 1000;
    }
    collectDrops(room);
  }

  if (room.phase === 'BATTLE' && canEndBattleEarly(room)) advancePhase(room);
  else if (room.phase !== 'LOBBY' && room.phaseEndsAt && now >= room.phaseEndsAt) advancePhase(room);

  if (room.phase === 'BATTLE' || room.phase === 'PREP') {
    for (const p of activePlayers(room)) if (p.alive) rechargeDash(room, p, now);
  }
  broadcast(room);
}

setInterval(() => {
  const now = Date.now();
  for (const room of [...rooms.values()]) {
    try { tickRoom(room, now); } catch (err) { console.error('tick failed', err); }
  }
}, 100);

server.listen(PORT, '127.0.0.1', () => {
  const d = defaultSettings();
  console.log(`BettingRoyale MVP 목업 · http://127.0.0.1:${PORT}`);
  console.log(`참가: 인간·봇 합산 ${MIN_TOTAL_PLAYERS}~${MAX_PLAYERS}명 · 인간 1명부터 시작 가능`);
  console.log(`기본 설정: ${d.rounds}라운드 × (준비 ${d.prepSeconds}초 + 전투 ${d.battleSeconds}초) · 모드 ${d.mode}`);
  console.log(`경제: 라운드 기본 재화 ${d.baseMoney}S · 최대 이월 한도 ${d.carryCap}S (미결정 사항 · 호스트 설정)`);
  console.log(`룰렛: ${WHEEL_SLOTS}칸 (${WHEEL_NUMBERS.map(n => `${n}×${WHEEL_SLOT_COUNTS[n]}`).join(' ')})`);
  for (const row of WHEEL_TABLE) {
    console.log(`      ${String(row.number).padStart(2)} : ${(row.chance * 100).toFixed(2).padStart(5)}% · 총 회수 ×${row.payout} · 기대 회수율 ${(row.expected * 100).toFixed(1)}%`);
  }
  console.log(`모루: 확률 강화 ${ENHANCE_CHANCE.map((c, i) => `+${i + 1} ${Math.round(c * 100)}%`).join(' · ')} · 실패 시 재화만 소모`);
  console.log(`장신구: 뽑기 ${TRINKET_PRICE}S · ${TRINKET_ITEMS.length}종 · 획득 즉시 장착·교체`);
  console.log(`점수: 등수 ${RANK_POINTS_TOP}~${RANK_POINTS_BOTTOM}점 · 킬 ${KILL_POINTS}점 · 어시 ${ASSIST_POINTS}점 (보유 재화는 점수 제외)`);
  console.log(`동점 판정: 총 점수 → 전투력 → 서버 난수 (2인 동전 · 다인 러시안 룰렛 연출)`);
  console.log(`황금 고블린: 전투 시작 전 전투력 1위 지정 · 킬 ${GOBLIN_KILL_BOUNTY}S / 어시 ${GOBLIN_ASSIST_BOUNTY}S 현상금`);
  console.log(`전장: ${BATTLEFIELD.walls.length}벽 · 낙사 구덩이 ${BATTLEFIELD.pits.length}개 · 스폰 ${BATTLEFIELD.spawns.length}개`);
  console.log(`자기장: 전투 ${STORM_START_FRAC * 100}% 시작 → ${STORM_END_FRAC * 100}% 지점 반경 ${STORM_FINAL_RADIUS} · 밖 초당 ${STORM_DPS} 피해`);
});

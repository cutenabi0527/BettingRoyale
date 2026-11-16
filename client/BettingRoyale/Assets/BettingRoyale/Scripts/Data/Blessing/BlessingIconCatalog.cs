using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 축복 하나가 어느 아이콘 칸으로 보이는가 — <b>화면 세 곳이 함께 쓰는 유일한 표</b>
    /// (획득 레일 · 선택 카드 · 장비창 위 효과 칸). S-UX-9MT6CM.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 한 곳인가.</b> 예전에는 두 경로가 따로 정했다 — 레일·카드는 <c>BlessingMvpDefinition.IconIndex</c> 와
    /// 설명문 문자열 추측을, 효과 칸은 효과 키 표를 봤다. 그래서 <b>같은 축복이 레일과 효과 칸에서 다른 그림</b>으로
    /// 보일 수 있었고, 엑셀 전용 축복 54종은 레일에서 전부 공용 '?' 였다(코드 카탈로그에 아이콘이 없으므로).
    /// </para>
    /// <para>
    /// <b>번호 체계는 최종 HUD 번호다.</b> 0~15 는 공용 게임플레이 시트, <see cref="BlessingArtOffset"/> 부터는
    /// 축복 전용 시트다 — <c>UI.MatchHudAssets.Icon</c> 이 그대로 받는 번호이며 <see cref="BlessingArtOffset"/> 은
    /// <c>MatchHudAssets.BlessingIconOffset</c> 과 같아야 한다(EditMode 테스트가 대조한다).
    /// </para>
    /// <para>
    /// <b>모르는 키는 지어내지 않는다.</b> <see cref="BlessingMvpDefinition.MissingIconIndex"/> 를 돌려주고 화면이
    /// 공용 '?' 로 그린다. 새 축복을 넣고 여기 줄을 빠뜨리면 화면에서 바로 드러나야 한다 — 조용히 남의 그림으로
    /// 보이는 쪽이 훨씬 찾기 어렵다.
    /// </para>
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </remarks>
    public static class BlessingIconCatalog
    {
        /// <summary>
        /// 축복 전용 아이콘 시트의 첫 칸이 공용 시트 뒤에서 시작하는 위치.
        /// <c>UI.MatchHudAssets.BlessingIconOffset</c> 과 같은 값이어야 한다 — Data 는 UI 를 참조할 수 없어
        /// 상수를 여기 한 번 더 적고 EditMode 테스트가 두 값이 같은지 지킨다.
        /// </summary>
        public const int BlessingArtOffset = 16;

        /// <summary>공용 게임플레이 시트의 칸 수. 빌려 쓰는 번호는 전부 이 범위 안이다.</summary>
        public const int SharedSheetCells = 16;

        // ── 공용 게임플레이 시트 칸 뜻 ──────────────────────────────────────────
        // 이름을 붙여 두면 아래 표에서 "왜 이 번호인가" 가 주석 없이도 읽힌다.
        private const int Sword = 0, Bow = 1, RedPotion = 2, PoisonBottle = 4, Gold = 6,
            Shield = 7, CrossedSwords = 8, WingedBoot = 9, Pierce = 10, Impact = 11, Skull = 13,
            Handshake = 14, Chest = 15;

        /// <summary>
        /// 효과 키 → 공용 시트에서 빌려 쓰는 칸. <b>전용 아트가 없는 축복</b>이 여기로 온다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠️ <b>전부 잠정값이다.</b> 뜻이 가장 가까운 칸을 빌린 것이고, 최종 아트가 나오면 엑셀·CSV 의
        /// <c>icon_index</c> 에 실려 전용 아트 쪽으로 자동 이관된다 — 그때 이 표에서 해당 줄을 지운다.
        /// </para>
        /// <para>배정 근거와 사람 확인이 필요한 항목은 S-UX-9MT6CM 에 적혀 있다.</para>
        /// </remarks>
        private static readonly Dictionary<string, int> Borrowed = new(StringComparer.Ordinal)
        {
            // ── 전투 규칙이 직접 내는 상태 (축복이 아닌 것 포함) ────────────────
            ["shield"] = Shield,            // 출처가 여럿이라 전용 아트를 고를 수 없다 (잠정 아님)
            ["healing-blocked"] = RedPotion,
            ["stagger"] = Impact,
            ["stun"] = Impact,              // 경직보다 강하다 — 분류 색으로 구분한다
            ["poison"] = PoisonBottle,

            // ── 베팅·경제 계열 ────────────────────────────────────────────────
            ["small-bet"] = Gold,
            ["chip-stack"] = Gold,
            ["house-cut"] = Gold,
            ["petty-cash"] = Gold,
            ["greed"] = Gold,
            ["instant_gold"] = Gold,
            ["interest_income"] = Gold,
            ["roulette_refund"] = Gold,
            ["blind-bet"] = Gold,           // 보너스 골드를 미리 받고 체력을 잃은 채 시작한다
            ["dealer-choice"] = Gold,       // 무료 리롤 — 준비 단계 경제

            // ── 피해 증가 계열 ────────────────────────────────────────────────
            ["cold-streak"] = Impact,
            ["double-down"] = Impact,
            ["all-in"] = Impact,
            ["jackpot"] = Impact,
            ["final-offer"] = Impact,
            ["berserk"] = Impact,
            ["last_stand"] = Impact,
            ["bully"] = Impact,
            ["underdog"] = Impact,
            ["critical"] = Impact,

            // ── 이동·대시 계열 ────────────────────────────────────────────────
            ["quiet-table"] = WingedBoot,
            ["warm-up"] = WingedBoot,
            ["margin-call"] = WingedBoot,
            ["booster"] = WingedBoot,
            ["ouch"] = WingedBoot,
            ["light_step"] = WingedBoot,
            ["afterimage"] = WingedBoot,
            ["body_check"] = WingedBoot,
            ["close_in"] = WingedBoot,
            ["escape_dash"] = WingedBoot,
            ["rage_dash"] = WingedBoot,
            ["hunter_mark"] = WingedBoot,   // 표식은 추격 보너스라 이동 쪽으로 읽힌다

            // ── 공격 속도 계열 ────────────────────────────────────────────────
            ["hot-hand"] = CrossedSwords,
            ["draw-tempo"] = CrossedSwords,
            ["hype"] = CrossedSwords,
            ["attack_speed"] = CrossedSwords,

            // ── 방어·보호막 계열 ──────────────────────────────────────────────
            ["guard-rail"] = Shield,
            ["hilt-guard"] = Shield,
            ["cover-fire"] = Shield,
            ["stop-loss"] = Shield,
            ["table-limit"] = Shield,
            ["one_hit"] = Shield,
            ["sturdy"] = Shield,
            ["nine_lives"] = Shield,
            ["house-edge"] = Shield,
            ["spare-chip"] = Shield,

            // ── 근접 전용 ─────────────────────────────────────────────────────
            ["token-toss"] = Sword,
            ["cross-cut"] = Sword,
            ["riposte-ledger"] = Sword,
            ["edge-dividend"] = Sword,
            ["back-alley"] = Sword,
            ["close-quarters"] = Sword,
            ["swordsman_range"] = Sword,
            ["wide_swing"] = Sword,
            ["edge-case"] = Sword,          // 근접 사거리 바깥 구간 보너스

            // ── 원거리 전용 ───────────────────────────────────────────────────
            ["sight-line"] = Bow,
            ["arrow-change"] = Bow,
            ["ricochet"] = Bow,
            ["split-shot"] = Bow,
            ["rain-check"] = Bow,
            ["fleet_archer"] = Bow,
            ["projectile_speed"] = Bow,
            ["sniper"] = Bow,
            ["triple_shot"] = Bow,
            ["long-odds"] = Bow,            // 먼 거리 원거리 적중 시 치명타

            // ── 방어구 관통 ───────────────────────────────────────────────────
            ["ballista"] = Pierce,
            ["bodkin"] = Pierce,
            ["crossbow"] = Pierce,
            ["piercing-bet"] = Pierce,

            // ── 회복 ──────────────────────────────────────────────────────────
            ["draw"] = RedPotion,
            ["pocket-change"] = RedPotion,
            ["lifesteal"] = RedPotion,

            // ── 그 밖 ─────────────────────────────────────────────────────────
            ["split-pot"] = Handshake,      // 피해를 나눈다
            ["credit-line"] = Skull,        // 아직 들어올 피해
            ["last_five"] = Skull,          // 끝나면 쓰러진다
            ["headshot"] = Skull,           // 즉사
            ["poison_arrow"] = PoisonBottle,
            ["pickpocket"] = Chest,         // 남의 아이템을 빼앗는다
        };

        /// <summary>
        /// 같은 축복의 부수 상태는 본 효과의 그림을 그대로 쓰고 분류 색으로만 구분한다.
        /// </summary>
        private static readonly Dictionary<string, string> EffectFamily = new(StringComparer.Ordinal)
        {
            ["emergency-exit-move"] = "emergency-exit",
            ["mark"] = "mark-shot",
        };

        /// <summary>전용 아트가 있는 효과 키 → 그 칸. 코드 카탈로그(CSV 생성)에서 한 번만 만든다.</summary>
        private static readonly Dictionary<string, int> DedicatedArt = BuildDedicatedArt();

        private static Dictionary<string, int> BuildDedicatedArt()
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (BlessingMvpDefinition entry in BlessingMvpCatalog.All)
            {
                if (entry.IconIndex == BlessingMvpDefinition.MissingIconIndex) continue;
                map[entry.Effect] = BlessingArtOffset + entry.IconIndex;
            }

            return map;
        }

        /// <summary>빌려 쓰는 칸이 배정된 효과 키 수. 테스트·진단이 읽는다.</summary>
        public static int BorrowedCount => Borrowed.Count;

        /// <summary>전용 아트가 붙은 효과 키 수.</summary>
        public static int DedicatedCount => DedicatedArt.Count;

        /// <summary>이 표가 아는 효과 키 전부. 순서는 정하지 않는다.</summary>
        public static IEnumerable<string> KnownEffects
        {
            get
            {
                foreach (string key in DedicatedArt.Keys) yield return key;
                foreach (string key in Borrowed.Keys)
                {
                    if (!DedicatedArt.ContainsKey(key)) yield return key;
                }
            }
        }

        /// <summary>
        /// 효과 키의 아이콘 번호. 전용 아트가 있으면 그것을, 없으면 빌린 칸을,
        /// 둘 다 없으면 <see cref="BlessingMvpDefinition.MissingIconIndex"/> 를 준다.
        /// </summary>
        public static int ForEffect(string effectId)
        {
            if (string.IsNullOrEmpty(effectId)) return BlessingMvpDefinition.MissingIconIndex;
            string family = EffectFamily.TryGetValue(effectId, out string mapped) ? mapped : effectId;
            if (DedicatedArt.TryGetValue(family, out int art)) return art;
            return Borrowed.TryGetValue(family, out int borrowed) ? borrowed : BlessingMvpDefinition.MissingIconIndex;
        }

        /// <summary>
        /// 축복 id 의 아이콘 번호. <b>엑셀 전용 축복도 여기서 그림을 얻는다</b> — 그쪽은 전용 아트가 없어
        /// 정의의 <c>IconIndex</c> 가 비어 있지만 효과 키는 실려 오므로 그 키로 찾는다.
        /// </summary>
        public static int ForBlessing(string blessingId)
        {
            if (string.IsNullOrEmpty(blessingId) || !BlessingMvpCatalog.TryGet(blessingId, out BlessingMvpDefinition entry))
            {
                return BlessingMvpDefinition.MissingIconIndex;
            }

            return entry.IconIndex != BlessingMvpDefinition.MissingIconIndex
                ? BlessingArtOffset + entry.IconIndex
                : ForEffect(entry.Effect);
        }
    }
}

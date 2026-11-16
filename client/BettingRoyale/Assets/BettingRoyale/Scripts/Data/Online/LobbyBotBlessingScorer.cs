using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Common;

namespace BettingRoyale.Data.Online
{
    /// <summary>봇이 축복을 가르는 성격. 기획 엑셀에 없는 값이라 코드 분류표로 둔다(S-GD-6QP2RG, 에이전트 임의).</summary>
    public enum LobbyBotBlessingCategory
    {
        /// <summary>분류표에 없는 새 축복. 점수 중간값으로 다룬다.</summary>
        Other = 0,
        Offense = 1,
        Defense = 2,
        Mobility = 3,
        Economy = 4,
    }

    /// <summary>
    /// 대기방 봇의 축복 고르기(S15P21A606-326, S-GD-9CZKVT) — 후보마다 점수를 매겨 가장 높은 칸을 고른다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 점수 = 준비 성향의 분류 가중치 + (전투 유형과 궁합이 맞으면) 가점 + 동점을 가르는 작은 흔들림.
    /// 흔들림은 매치·봇·라운드로 시드를 고정한 난수라 같은 판에서는 늘 같은 답이다.
    /// </para>
    /// <para>
    /// 분류는 축복 id 에서 등급 접미사(<c>-t1</c>)를 뗀 이름으로 찾는다 — 효과 이름(<c>Effect</c>)은 id 와 다를 수
    /// 있고(<c>hunter</c> 의 효과는 <c>hunter_mark</c>) 정의에 실려 있지 않다. 표에 없는 새 축복은
    /// <see cref="LobbyBotBlessingCategory.Other"/> 로 두어 고를 수는 있게 한다.
    /// </para>
    /// </remarks>
    public static class LobbyBotBlessingScorer
    {
        /// <summary>전투 유형과 궁합이 맞는 축복의 가점.</summary>
        public const int StyleAffinityBonus = 3;

        /// <summary>최고 점수가 이 값 이하이고 재추첨이 싸면 한 번 다시 뽑는다.</summary>
        public const int RerollScoreThreshold = 1;

        private static readonly Dictionary<string, LobbyBotBlessingCategory> Categories = BuildCategories();
        private static readonly Dictionary<string, LobbyBotCombatStyle[]> Affinities = BuildAffinities();

        public static LobbyBotBlessingCategory Classify(string blessingId)
        {
            return Categories.TryGetValue(Stem(blessingId), out LobbyBotBlessingCategory category)
                ? category
                : LobbyBotBlessingCategory.Other;
        }

        /// <summary>이 축복이 전투 유형과 궁합이 맞는가.</summary>
        public static bool Suits(string blessingId, LobbyBotCombatStyle combat)
        {
            if (combat == LobbyBotCombatStyle.Unknown) return false;
            if (!Affinities.TryGetValue(Stem(blessingId), out LobbyBotCombatStyle[] styles)) return false;
            return Array.IndexOf(styles, combat) >= 0;
        }

        /// <summary>준비 성향의 분류 가중치(0~3).</summary>
        public static int CategoryWeight(LobbyBotPrepStyle prep, LobbyBotBlessingCategory category)
        {
            return (prep, category) switch
            {
                (_, LobbyBotBlessingCategory.Other) => 1,
                (LobbyBotPrepStyle.Aggressive, LobbyBotBlessingCategory.Offense) => 3,
                (LobbyBotPrepStyle.Aggressive, LobbyBotBlessingCategory.Mobility) => 2,
                (LobbyBotPrepStyle.Aggressive, LobbyBotBlessingCategory.Defense) => 1,
                (LobbyBotPrepStyle.Aggressive, LobbyBotBlessingCategory.Economy) => 0,
                (LobbyBotPrepStyle.Steady, LobbyBotBlessingCategory.Defense) => 3,
                (LobbyBotPrepStyle.Steady, LobbyBotBlessingCategory.Economy) => 2,
                (LobbyBotPrepStyle.Steady, LobbyBotBlessingCategory.Offense) => 1,
                (LobbyBotPrepStyle.Steady, LobbyBotBlessingCategory.Mobility) => 1,
                (LobbyBotPrepStyle.Gambler, LobbyBotBlessingCategory.Economy) => 3,
                (LobbyBotPrepStyle.Gambler, LobbyBotBlessingCategory.Offense) => 2,
                (LobbyBotPrepStyle.Gambler, LobbyBotBlessingCategory.Mobility) => 1,
                (LobbyBotPrepStyle.Gambler, LobbyBotBlessingCategory.Defense) => 0,
                _ => 1,
            };
        }

        /// <summary>흔들림을 뺀 점수. 테스트와 로그가 읽는다.</summary>
        public static int Score(BlessingDefinition blessing, LobbyBotPersona persona)
        {
            if (blessing == null) return int.MinValue;
            int score = CategoryWeight(persona.Prep, Classify(blessing.Id));
            if (Suits(blessing.Id, persona.Combat)) score += StyleAffinityBonus;
            return score;
        }

        /// <summary>
        /// 가장 점수가 높은 후보의 칸 번호. 후보가 없으면 -1. 동점은 시드 고정 흔들림으로 가른다 — 늘 0번 칸으로
        /// 쏠리지 않게 한다.
        /// </summary>
        public static int ChooseIndex(IReadOnlyList<BlessingCandidate> candidates, LobbyBotPersona persona, int seed)
        {
            if (candidates == null || candidates.Count == 0) return -1;
            var random = new DeterministicRandom(seed);
            int best = -1;
            int bestScore = int.MinValue;
            int bestJitter = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                BlessingDefinition blessing = candidates[i].Blessing;
                if (blessing == null) continue;
                int score = Score(blessing, persona);
                int jitter = random.Next(1000);
                if (score > bestScore || (score == bestScore && jitter > bestJitter))
                {
                    best = i;
                    bestScore = score;
                    bestJitter = jitter;
                }
            }

            return best;
        }

        /// <summary>후보 중 최고 점수. 재추첨 여부를 정할 때 쓴다.</summary>
        public static int BestScore(IReadOnlyList<BlessingCandidate> candidates, LobbyBotPersona persona)
        {
            int best = int.MinValue;
            if (candidates == null) return best;
            for (int i = 0; i < candidates.Count; i++)
            {
                int score = Score(candidates[i].Blessing, persona);
                if (score > best) best = score;
            }

            return best;
        }

        /// <summary>
        /// 재추첨할까 — 최고 점수가 낮고, 재추첨 값이 지금 골드의 일정 비율 이하일 때 라운드당 한 번. 안정형은 공짜일 때만,
        /// 도박형은 조금 더 과감하다.
        /// </summary>
        public static bool ShouldReroll(int bestScore, int rerollCost, int gold, LobbyBotPrepStyle prep)
        {
            if (bestScore > RerollScoreThreshold || rerollCost < 0 || rerollCost > gold) return false;
            int limitPercent = prep switch
            {
                LobbyBotPrepStyle.Steady => 0,
                LobbyBotPrepStyle.Aggressive => 10,
                _ => 20,
            };
            return rerollCost == 0 || rerollCost * 100 <= gold * limitPercent;
        }

        /// <summary>id 에서 등급 접미사(<c>-t1</c>~<c>-t9</c>)를 뗀다.</summary>
        public static string Stem(string blessingId)
        {
            if (string.IsNullOrEmpty(blessingId)) return string.Empty;
            int length = blessingId.Length;
            if (length > 3 && blessingId[length - 3] == '-' && blessingId[length - 2] == 't' && char.IsDigit(blessingId[length - 1]))
                return blessingId.Substring(0, length - 3);
            return blessingId;
        }

        private static Dictionary<string, LobbyBotBlessingCategory> BuildCategories()
        {
            var map = new Dictionary<string, LobbyBotBlessingCategory>(StringComparer.Ordinal);
            void Add(LobbyBotBlessingCategory category, params string[] stems)
            {
                foreach (string stem in stems) map[stem] = category;
            }

            // 2026-09-25 blessings.json(88종) 기준. 설명문으로 가른 에이전트 분류다 — 기획이 원하면 엑셀 열로 옮긴다.
            Add(LobbyBotBlessingCategory.Economy,
                "interest-burst", "petty-cash", "reroll-cashback", "roulette-refund", "show-me-money", "greed",
                "low-buy", "pickpocket-melee", "pickpocket-ranged");
            Add(LobbyBotBlessingCategory.Defense,
                "house-edge", "pocket-change", "safe-asset", "spare-chip", "insurance", "stop-loss", "credit-line",
                "last-chip", "table-limit", "guard-rail", "sturdy", "bloodthirst", "draw", "last-five", "nine-lives",
                "one-hit", "hilt-guard", "parry-dividend", "escape", "margin-call");
            Add(LobbyBotBlessingCategory.Mobility,
                "emergency-exit", "quiet-table", "warm-up", "corner-pocket", "body-check", "booster", "close-in",
                "ouch", "swordsman-range", "afterimage", "fleet-archer", "light-step", "reverse-blade");
            Add(LobbyBotBlessingCategory.Offense,
                "chip-stack", "cold-streak", "critical", "small-bet", "split-pot", "house-cut", "jackpot",
                "swift-strike", "token-toss", "back-alley", "bully", "auto-mouse", "wide-swing", "crossbow",
                "long-dividend", "rapid-shot", "sight-line", "ballista", "mark-shot", "poison-arrow", "blade-tip",
                "riposte-ledger", "triple-slash", "cross-cut", "bodkin", "headshot", "sniper", "fixed-line",
                "piercing-bet", "rain-check", "triple-shot", "arrow-speed", "deadline", "final-offer", "hype",
                "overheat", "counter", "chip-slide", "rage-dash", "hunter", "underdog", "double-down", "all-in",
                "last-stand", "close-quarters", "berserk");
            return map;
        }

        private static Dictionary<string, LobbyBotCombatStyle[]> BuildAffinities()
        {
            var map = new Dictionary<string, List<LobbyBotCombatStyle>>(StringComparer.Ordinal);
            void Add(LobbyBotCombatStyle style, params string[] stems)
            {
                foreach (string stem in stems)
                {
                    if (!map.TryGetValue(stem, out List<LobbyBotCombatStyle> styles)) map[stem] = styles = new List<LobbyBotCombatStyle>();
                    styles.Add(style);
                }
            }

            Add(LobbyBotCombatStyle.SwordRush, "close-in", "body-check", "booster", "berserk", "rage-dash",
                "swift-strike", "auto-mouse", "wide-swing", "cross-cut", "triple-slash", "all-in", "double-down");
            Add(LobbyBotCombatStyle.SwordCautious, "hilt-guard", "guard-rail", "parry-dividend", "riposte-ledger",
                "sturdy", "counter", "blade-tip", "close-quarters", "one-hit");
            Add(LobbyBotCombatStyle.SwordOpportunist, "back-alley", "bully", "hunter", "underdog", "reverse-blade",
                "house-cut", "margin-call");
            Add(LobbyBotCombatStyle.BowKite, "fleet-archer", "light-step", "arrow-speed", "mark-shot",
                "poison-arrow", "escape");
            Add(LobbyBotCombatStyle.BowSniper, "sniper", "sight-line", "fixed-line", "long-dividend", "headshot",
                "ballista", "crossbow");
            Add(LobbyBotCombatStyle.BowPressure, "rapid-shot", "triple-shot", "piercing-bet", "rain-check",
                "bodkin", "overheat");

            var result = new Dictionary<string, LobbyBotCombatStyle[]>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<LobbyBotCombatStyle>> pair in map) result[pair.Key] = pair.Value.ToArray();
            return result;
        }
    }
}

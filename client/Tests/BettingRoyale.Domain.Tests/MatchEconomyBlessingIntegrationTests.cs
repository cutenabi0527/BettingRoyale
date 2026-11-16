using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Shop;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// <see cref="MatchEconomyState"/> 가 경제형 축복(이자 폭발·저점 매수)의 <b>소유자</b>로서 지키는 계약.
    ///
    /// <para>
    /// <b>실제 카탈로그·지갑·보유 목록을 쓴다.</b> 예치금과 보상 금액을
    /// <see cref="BlessingMvpCatalog"/> 에서 읽어 기대값을 만들므로 CSV 가 바뀌면 기대값도 따라 바뀐다.
    /// </para>
    ///
    /// <para>
    /// <b>등급을 라운드에 재배치해 쓴다</b> — 이자 폭발은 플래티넘이라 기본 순서(실버·골드·플래티넘)에서는
    /// 3라운드에나 온다. 라운드별 등급은 설정값이므로(S-GD-HE87JY) 여기서 1R 골드 · 2R 플래티넘 · 3R 실버로 둔다.
    /// 저점 매수는 직전 순위표의 하위권에게만 후보로 나오므로(S-GD-TMPBEF) 2라운드가 골드인 설정을 따로 쓴다.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class MatchEconomyBlessingIntegrationTests
    {
        private const int Alice = 1;
        private const int Bob = 2;

        /// <summary>이 매치의 참가자가 아닌 id. 명령은 던지지 않고 거부돼야 한다.</summary>
        private const int Stranger = 99;

        private const int Seed = 0x4247;

        private const string InterestId = "interest-burst-t3";
        private const string LowBuyId = "low-buy-t2";

        /// <summary>1라운드(골드)를 치르기 위해 고르는 채움 축복.</summary>
        private const string GoldFillerId = "filler-Gold-0";

        // ── 기대값은 카탈로그에서 읽는다 ──────────────────────────────────

        private static int DepositAmount => (int)Math.Floor(Mvp(InterestId).Number("deposit"));

        private static int RefundAmount =>
            (int)Math.Floor(DepositAmount * (double)Mvp(InterestId).Number("refund_fraction"));

        private static int LowBuyGold => (int)Math.Floor(Mvp(LowBuyId).Number("bonus_gold"));

        private static BlessingMvpDefinition Mvp(string blessingId)
        {
            Assert.That(BlessingMvpCatalog.TryGet(blessingId, out var entry), Is.True, blessingId);
            return entry;
        }

        // ── 매치 조립 ─────────────────────────────────────────────────────

        private static BlessingConfig CreateConfig() => new BlessingConfig(
            candidateCount: 3,
            maxFreeLocks: 2,
            rerollBaseCostByRound: new[] { 200, 300, 400 },
            tierByRound: new[] { BlessingTier.Gold, BlessingTier.Platinum, BlessingTier.Silver },
            rerollCostMultiplier: 2);

        /// <summary>
        /// 등급마다 공용 3개씩. 후보가 3개여야 하므로(S-GD-S46S28) 등급별로 정확히 후보 수만 둬서
        /// <b>노리는 축복이 반드시 후보에 뜨게</b> 한다 — 추첨 순서를 가정하지 않는다.
        /// 골드는 채움 3개에 저점 매수가 더 있다 — 1라운드엔 순위표가 없어 저점 매수가 빠져도 3개가 남는다.
        /// </summary>
        private static IReadOnlyList<BlessingDefinition> CreatePool()
        {
            var pool = new List<BlessingDefinition>
            {
                Mvp(LowBuyId).Definition,
                Mvp(InterestId).Definition,
            };

            AddFillers(pool, BlessingTier.Gold, 3);
            AddFillers(pool, BlessingTier.Platinum, 2);
            AddFillers(pool, BlessingTier.Silver, 3);
            return pool;
        }

        private static void AddFillers(List<BlessingDefinition> pool, BlessingTier tier, int count)
        {
            for (var i = 0; i < count; i++)
            {
                pool.Add(new BlessingDefinition($"filler-{tier}-{i}", $"채움 {tier} {i}", WeaponTag.Common, tier));
            }
        }

        private static MatchEconomyState NewMatch()
        {
            var state = new MatchEconomyState(EconomyConfig.Default, CreateConfig(), CreatePool());
            state.BeginMatch(
                new[]
                {
                    new MatchParticipant(Alice, WeaponTag.Sword),
                    new MatchParticipant(Bob, WeaponTag.Sword),
                },
                Seed);

            return state;
        }

        private static void SelectById(MatchEconomyState state, int playerId, string blessingId)
        {
            var candidates = state.GetCandidates(playerId);

            for (var i = 0; i < candidates.Count; i++)
            {
                if (string.Equals(candidates[i].Blessing.Id, blessingId, StringComparison.Ordinal))
                {
                    Assert.That(state.TrySelect(playerId, i), Is.Not.Null);
                    return;
                }
            }

            Assert.Fail($"후보에 {blessingId} 가 없습니다. 풀 구성을 확인하세요.");
        }

        private static void SelectFirst(MatchEconomyState state, int playerId) =>
            Assert.That(state.TrySelect(playerId, 0), Is.Not.Null);

        private static RoundOutcome Outcome(int playerId, int placement, bool survived, int assists = 0) =>
            new RoundOutcome(playerId, 0, assists, 0, survived, placement);

        /// <summary>
        /// 1라운드(골드)를 치른다 — Alice 가 <paramref name="aliceSelection"/> 을 고르고
        /// <b>Bob 이 1등</b>으로 끝난다. <paramref name="aliceAssists"/> 로 Alice 의 점수를 조절한다.
        /// </summary>
        private static void PlayGoldRound(MatchEconomyState state, string aliceSelection, int aliceAssists = 0)
        {
            var round = state.BeginPreparation();
            SelectById(state, Alice, aliceSelection);
            SelectFirst(state, Bob);
            state.EndPreparation();

            Assert.That(
                state.TryApplyCompleteRoundResults(
                    round,
                    new[]
                    {
                        Outcome(Bob, placement: 1, survived: true),
                        Outcome(Alice, placement: 2, survived: true, assists: aliceAssists),
                    }),
                Is.True);
        }

        [TestCase(true, true)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(false, false)]
        public void PreparationResultCannotPoisonLaterDepositSettlement(bool complete, bool deposit)
        {
            var state = NewMatch();
            PlayGoldRound(state, GoldFillerId);
            int round = state.BeginPreparation();
            SelectById(state, Alice, InterestId);
            SelectFirst(state, Bob);
            int before = state.GetGold(Alice);
            if (deposit) Assert.That(state.TryDepositBlessing(Alice), Is.True);
            var result = new[] { Outcome(Alice, 1, true), Outcome(Bob, 2, false) };
            bool premature = complete ? state.TryApplyCompleteRoundResults(round, result)
                : state.TryApplyRoundResults(round, result);
            Assert.That(premature, Is.False);
            Assert.That(state.Tally.CountedRounds, Does.Not.Contain(round));
            state.EndPreparation();
            bool settled = complete ? state.TryApplyCompleteRoundResults(round, result)
                : state.TryApplyRoundResults(round, result);
            Assert.That(settled, Is.True);
            Assert.That(state.GetBlessingEconomy(Alice).RoundSettled, Is.True);
            Assert.That(state.GetGold(Alice), Is.EqualTo(deposit ? before - DepositAmount + RefundAmount : before));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FutureLegacyResultCannotPoisonAnUnopenedRound(bool combat)
        {
            var state = NewMatch();
            state.BeginPreparation();
            if (combat) state.EndPreparation();
            var result = new[] { Outcome(Alice, 1, true), Outcome(Bob, 2, false) };
            Assert.That(state.TryApplyRoundResults(2, result), Is.False);
            Assert.That(state.Tally.CountedRounds, Does.Not.Contain(2));
            if (!combat) state.EndPreparation();
            Assert.That(state.TryApplyCompleteRoundResults(1, result), Is.True);
            state.BeginPreparation();
            SelectById(state, Alice, InterestId);
            SelectFirst(state, Bob);
            Assert.That(state.TryDepositBlessing(Alice), Is.True);
            state.EndPreparation();
            Assert.That(state.TryApplyCompleteRoundResults(2, result), Is.True);
            Assert.That(state.GetBlessingEconomy(Alice).RoundSettled, Is.True);
        }

        [Test]
        public void FinalRoundRetryRemainsIdempotentAfterFinished()
        {
            var state = NewMatch();
            var result = new[] { Outcome(Alice, 1, true), Outcome(Bob, 2, false) };
            for (int round = 1; round <= 3; round++)
            {
                Assert.That(state.BeginPreparation(), Is.EqualTo(round));
                state.EndPreparation();
                Assert.That(state.TryApplyCompleteRoundResults(round, result), Is.True);
            }
            Assert.That(state.Phase, Is.EqualTo(MatchEconomyPhase.Finished));
            Assert.That(state.TryApplyCompleteRoundResults(3, result), Is.True);
        }

        // ── 저점 매수 (엑셀 개편 — S-GD-TMPBEF) ───────────────────────────

        /// <summary>1R 실버 · 2R 골드 · 3R 플래티넘 — 저점 매수가 순위표가 생긴 2라운드에 나오게 한다.</summary>
        private static BlessingConfig LowBuyConfig() => new BlessingConfig(
            candidateCount: 3,
            maxFreeLocks: 2,
            rerollBaseCostByRound: new[] { 200, 300, 400 },
            tierByRound: new[] { BlessingTier.Silver, BlessingTier.Gold, BlessingTier.Platinum },
            rerollCostMultiplier: 2);

        private static IReadOnlyList<BlessingDefinition> LowBuyPool()
        {
            var pool = new List<BlessingDefinition> { Mvp(LowBuyId).Definition };
            AddFillers(pool, BlessingTier.Silver, 3);
            AddFillers(pool, BlessingTier.Gold, 3);
            AddFillers(pool, BlessingTier.Platinum, 3);
            return pool;
        }

        /// <summary>
        /// 1라운드(실버)를 치른 뒤 2라운드(골드) 준비를 연 매치. Bob 이 1등, Alice 가 2등이다
        /// (<paramref name="aliceAssists"/> 로 Alice 점수를 조절). 추첨 순서를 가정하지 않으려고 시드를 넘겨받는다.
        /// </summary>
        private static MatchEconomyState SecondRoundMatch(int seed, int aliceAssists = 0)
        {
            var state = new MatchEconomyState(EconomyConfig.Default, LowBuyConfig(), LowBuyPool());
            state.BeginMatch(
                new[] { new MatchParticipant(Alice, WeaponTag.Sword), new MatchParticipant(Bob, WeaponTag.Sword) }, seed);
            int first = state.BeginPreparation();
            SelectFirst(state, Alice);
            SelectFirst(state, Bob);
            state.EndPreparation();
            Assert.That(
                state.TryApplyCompleteRoundResults(first, new[]
                {
                    Outcome(Bob, placement: 1, survived: true),
                    Outcome(Alice, placement: 2, survived: true, assists: aliceAssists),
                }),
                Is.True);
            state.BeginPreparation();
            return state;
        }

        /// <summary>Alice 의 2라운드 후보에 저점 매수가 뜨는 첫 시드의 매치.</summary>
        private static MatchEconomyState LowBuyOfferedMatch()
        {
            for (var seed = 1; seed <= 256; seed++)
            {
                var state = SecondRoundMatch(seed);
                if (HasCandidate(state, Alice, LowBuyId))
                {
                    return state;
                }
            }

            Assert.Fail("시드 1~256 안에서 Alice 후보에 저점 매수가 뜨지 않았습니다.");
            return null;
        }

        private static bool HasCandidate(MatchEconomyState state, int playerId, string blessingId)
        {
            foreach (var candidate in state.GetCandidates(playerId))
            {
                if (string.Equals(candidate.Blessing.Id, blessingId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        [Test]
        public void 첫_라운드에는_순위표가_없어_저점_매수가_후보에_없다()
        {
            for (var seed = 1; seed <= 32; seed++)
            {
                var state = new MatchEconomyState(EconomyConfig.Default, CreateConfig(), CreatePool());
                state.BeginMatch(
                    new[] { new MatchParticipant(Alice, WeaponTag.Sword), new MatchParticipant(Bob, WeaponTag.Sword) },
                    seed);
                state.BeginPreparation();

                Assert.That(HasCandidate(state, Alice, LowBuyId), Is.False, $"seed {seed}");
                Assert.That(HasCandidate(state, Bob, LowBuyId), Is.False, $"seed {seed}");
            }
        }

        [Test]
        public void 저점_매수는_하위권에게만_후보로_나온다()
        {
            var aliceSeen = false;
            for (var seed = 1; seed <= 64; seed++)
            {
                var state = SecondRoundMatch(seed);
                Assert.That(HasCandidate(state, Bob, LowBuyId), Is.False, "1등에게는 나오지 않는다");
                aliceSeen |= HasCandidate(state, Alice, LowBuyId);
            }

            Assert.That(aliceSeen, Is.True, "하위권에게는 나온다");
        }

        [Test]
        public void 동점이면_저점_매수가_후보에_없다()
        {
            // 2인 매치의 순위 점수 차이는 80 - 65 = 15 다. 어시스트 5개(3점씩)로 정확히 메운다.
            for (var seed = 1; seed <= 32; seed++)
            {
                var state = SecondRoundMatch(seed, aliceAssists: 5);
                Assert.That(HasCandidate(state, Alice, LowBuyId), Is.False, $"seed {seed}");
            }
        }

        [Test]
        public void 저점_매수를_고르면_골드와_물약을_즉시_받는다()
        {
            var state = LowBuyOfferedMatch();
            var gold = state.GetGold(Alice);
            var inflow = state.GetTotalInflow(Alice);

            SelectById(state, Alice, LowBuyId);

            Assert.That(state.GetGold(Alice), Is.EqualTo(gold + LowBuyGold));
            Assert.That(state.GetTotalInflow(Alice), Is.EqualTo(inflow + LowBuyGold), "새로 버는 골드다");
            var items = state.GetBlessingEconomy(Alice).ConsumePendingItemGrants();
            Assert.That(items, Is.EquivalentTo(new[]
            {
                new BlessingItemGrant(PlaceholderShopCatalog.HealthPotionItemId,
                    (int)Mvp(LowBuyId).Number("health_potion")),
                new BlessingItemGrant(PlaceholderShopCatalog.SwiftnessPotionItemId,
                    (int)Mvp(LowBuyId).Number("haste_potion")),
            }), "물약은 어댑터가 꺼내 인벤토리에 넣는다");
        }

        [Test]
        public void 저점_매수를_고른_라운드만_킬_점수가_배가_된다()
        {
            var state = LowBuyOfferedMatch();
            var multiplier = Mvp(LowBuyId).Number("kill_score_multiplier");
            SelectById(state, Alice, LowBuyId);
            SelectFirst(state, Bob);
            state.EndPreparation();

            Assert.That(state.TryApplyCompleteRoundResults(2, new[]
            {
                new RoundOutcome(Alice, 2, 0, 0, true, 1),
                new RoundOutcome(Bob, 0, 0, 0, false, 2),
            }), Is.True);

            state.BeginPreparation();
            SelectFirst(state, Alice);
            SelectFirst(state, Bob);
            state.EndPreparation();
            Assert.That(state.TryApplyCompleteRoundResults(3, new[]
            {
                new RoundOutcome(Alice, 1, 0, 0, true, 1),
                new RoundOutcome(Bob, 0, 0, 0, false, 2),
            }), Is.True);

            FinalStanding alice = default;
            foreach (var standing in state.BuildFinalStandings())
            {
                if (standing.PlayerId == Alice) alice = standing;
            }

            var scoredKills = (int)Math.Floor(2 * (double)multiplier) + 1;
            Assert.That(alice.Kills, Is.EqualTo(3), "실제 킬 수는 그대로다");
            Assert.That(alice.KillScore, Is.EqualTo(scoredKills * ScoringConfig.Default.KillPoints),
                "고른 라운드(2)의 킬만 배가 되고 3라운드는 그대로다");
        }

        // ── 예치 ─────────────────────────────────────────────────────────

        [Test]
        public void 예치는_선택_뒤_준비_단계에서만_한_번_받는다()
        {
            var state = NewMatch();
            PlayGoldRound(state, GoldFillerId);

            state.BeginPreparation();

            Assert.That(state.TryDepositBlessing(Alice), Is.False, "선택 전에는 받지 않는다");

            SelectById(state, Alice, InterestId);
            SelectFirst(state, Bob);

            var goldBefore = state.GetGold(Alice);
            var inflowBefore = state.GetTotalInflow(Alice);

            Assert.That(state.TryDepositBlessing(Alice), Is.True);
            Assert.That(state.GetGold(Alice), Is.EqualTo(goldBefore - DepositAmount));
            Assert.That(state.GetTotalInflow(Alice), Is.EqualTo(inflowBefore), "예치는 지출이므로 누계를 건드리지 않는다");
            Assert.That(state.GetBlessingEconomy(Alice).DepositAmount, Is.EqualTo(DepositAmount));

            Assert.That(state.TryDepositBlessing(Alice), Is.False, "같은 라운드에 두 번 예치되지 않는다");
            Assert.That(state.TryDepositBlessing(Bob), Is.False, "이자 폭발이 없으면 예치할 것이 없다");
            Assert.That(state.TryDepositBlessing(Stranger), Is.False);

            state.EndPreparation();

            Assert.That(state.TryDepositBlessing(Alice), Is.False, "전투 중에는 받지 않는다");
        }

        [Test]
        public void 생존하면_예치금이_정확히_한_번_환급된다()
        {
            var state = NewMatch();
            PlayGoldRound(state, GoldFillerId);

            var round = state.BeginPreparation();
            SelectById(state, Alice, InterestId);
            SelectFirst(state, Bob);
            Assert.That(state.TryDepositBlessing(Alice), Is.True);
            state.EndPreparation();

            var goldBefore = state.GetGold(Alice);
            var inflowBefore = state.GetTotalInflow(Alice);

            var results = new[]
            {
                Outcome(Alice, placement: 1, survived: true),
                Outcome(Bob, placement: 2, survived: false),
            };

            Assert.That(state.TryApplyCompleteRoundResults(round, results), Is.True);
            Assert.That(state.GetGold(Alice), Is.EqualTo(goldBefore + RefundAmount));
            Assert.That(state.GetTotalInflow(Alice), Is.EqualTo(inflowBefore), "환급은 번 것이 아니다 (S-GD-MNFBV7)");
            Assert.That(state.GetBlessingEconomy(Alice).RoundSettled, Is.True);

            // 재전송은 오류가 아니지만 돈은 한 번만 움직인다.
            Assert.That(state.TryApplyCompleteRoundResults(round, results), Is.True);
            Assert.That(state.GetGold(Alice), Is.EqualTo(goldBefore + RefundAmount));
        }

        [Test]
        public void 탈락하면_예치금을_잃는다()
        {
            var state = NewMatch();
            PlayGoldRound(state, GoldFillerId);

            var round = state.BeginPreparation();
            SelectById(state, Alice, InterestId);
            SelectFirst(state, Bob);
            Assert.That(state.TryDepositBlessing(Alice), Is.True);
            state.EndPreparation();

            var goldBefore = state.GetGold(Alice);

            Assert.That(
                state.TryApplyCompleteRoundResults(
                    round,
                    new[]
                    {
                        Outcome(Bob, placement: 1, survived: true),
                        Outcome(Alice, placement: 2, survived: false),
                    }),
                Is.True);

            Assert.That(state.GetGold(Alice), Is.EqualTo(goldBefore));
            Assert.That(state.GetBlessingEconomy(Alice).RoundSettled, Is.True);
        }

        [Test]
        public void 검증에_실패한_결과로는_정산하지_않는다()
        {
            var state = NewMatch();
            PlayGoldRound(state, GoldFillerId);

            var round = state.BeginPreparation();
            SelectById(state, Alice, InterestId);
            SelectFirst(state, Bob);
            Assert.That(state.TryDepositBlessing(Alice), Is.True);
            state.EndPreparation();

            var goldBefore = state.GetGold(Alice);

            // 명부 두 명 중 한 줄만 왔다 — 완결이 아니므로 거절된다.
            Assert.That(
                state.TryApplyCompleteRoundResults(round, new[] { Outcome(Alice, placement: 1, survived: true) }),
                Is.False);

            Assert.That(state.GetBlessingEconomy(Alice).RoundSettled, Is.False, "거절된 배치로 정산하지 않는다");
            Assert.That(state.GetGold(Alice), Is.EqualTo(goldBefore));

            Assert.That(
                state.TryApplyCompleteRoundResults(
                    round,
                    new[]
                    {
                        Outcome(Alice, placement: 1, survived: true),
                        Outcome(Bob, placement: 2, survived: true),
                    }),
                Is.True);

            Assert.That(state.GetGold(Alice), Is.EqualTo(goldBefore + RefundAmount));
        }

        [Test]
        public void 옛_경로는_들어온_줄만_정산하고_남은_예치금은_다음_라운드를_막는다()
        {
            var state = NewMatch();
            PlayGoldRound(state, GoldFillerId);

            var round = state.BeginPreparation();
            SelectById(state, Alice, InterestId);
            SelectById(state, Bob, InterestId);
            Assert.That(state.TryDepositBlessing(Alice), Is.True);
            Assert.That(state.TryDepositBlessing(Bob), Is.True);
            state.EndPreparation();

            var aliceGold = state.GetGold(Alice);
            var bobGold = state.GetGold(Bob);

            // 옛 계약은 명부의 일부만 담긴 배치도 받는다. 오지 않은 Bob 의 생사는 알 수 없다.
            Assert.That(
                state.TryApplyRoundResults(round, new[] { Outcome(Alice, placement: 1, survived: true) }),
                Is.True);

            Assert.That(state.GetGold(Alice), Is.EqualTo(aliceGold + RefundAmount));
            Assert.That(state.GetBlessingEconomy(Bob).RoundSettled, Is.False);
            Assert.That(state.GetGold(Bob), Is.EqualTo(bobGold), "모르는 생사로 환급하지 않는다");

            // 정산되지 않은 예치금을 남긴 채 다음 라운드를 열면 조용히 사라진다 — 막는다.
            Assert.Throws<InvalidOperationException>(() => state.BeginPreparation());
        }

        // ── 라운드 경계 ───────────────────────────────────────────────────

        [Test]
        public void 다음_라운드가_열리면_예치와_정산_기록이_비워진다()
        {
            var state = NewMatch();
            PlayGoldRound(state, GoldFillerId);

            var round = state.BeginPreparation();
            SelectById(state, Alice, InterestId);
            SelectFirst(state, Bob);
            Assert.That(state.TryDepositBlessing(Alice), Is.True);
            state.EndPreparation();

            Assert.That(
                state.TryApplyCompleteRoundResults(
                    round,
                    new[]
                    {
                        Outcome(Bob, placement: 1, survived: true),
                        Outcome(Alice, placement: 2, survived: true),
                    }),
                Is.True);

            var next = state.BeginPreparation();
            var blessing = state.GetBlessingEconomy(Alice);

            Assert.That(blessing.Round, Is.EqualTo(next));
            Assert.That(blessing.IsPreparationOpen, Is.True);
            Assert.That(blessing.InterestDeposited, Is.False);
            Assert.That(blessing.DepositAmount, Is.EqualTo(0));
            Assert.That(blessing.RoundSettled, Is.False);
        }

        [Test]
        public void 참가자가_아니면_축복_상태_조회는_던진다()
        {
            var state = NewMatch();
            state.BeginPreparation();

            Assert.That(state.GetBlessingEconomy(Alice), Is.Not.Null);
            Assert.Throws<ArgumentOutOfRangeException>(() => state.GetBlessingEconomy(Stranger));
        }
    }
}

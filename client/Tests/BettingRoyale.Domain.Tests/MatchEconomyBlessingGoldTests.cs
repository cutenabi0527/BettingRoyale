using System;
using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Remote;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 엑셀 전용 경제 축복이 경기 경제에 닿는지 — 고를 때 골드(쇼 미 더 머니), 이월 한도, 전투 중 골드, 룰렛 환급.
    /// </summary>
    [TestFixture]
    public sealed class MatchEconomyBlessingGoldTests
    {
        private const int Alice = 1;
        private const int Bob = 2;

        [TearDown]
        public void TearDown() => GameContentProvider.Reset();

        private static BlessingMvpDefinition Behavior(BlessingDefinition definition, string effect,
            params (string Key, float Value)[] numbers) =>
            new BlessingMvpDefinition(definition, effect, BlessingMvpDefinition.MissingIconIndex, BlessingStackRule.Unique,
                BlessingDurationKind.Permanent, numbers.Select(n => n.Key).ToArray(), numbers.Select(n => n.Value).ToArray());

        /// <summary>실버 후보가 셋뿐이라 셋 다 뜬다 — 쇼 미 더 머니 · 룰렛 환급 · 채움 하나.</summary>
        private static MatchEconomyState NewMatch()
        {
            var show = new BlessingDefinition("show-me-money-t1", "쇼 미 더 머니", WeaponTag.Common, BlessingTier.Silver);
            var refund = new BlessingDefinition("roulette-refund-t1", "룰렛 환급", WeaponTag.Common, BlessingTier.Silver);
            var filler = new BlessingDefinition("filler-t1", "채움", WeaponTag.Common, BlessingTier.Silver);
            GameContentProvider.Set(new GameContent(2, null, new[] { show, refund, filler }, null, null, null,
                new Dictionary<string, BlessingMvpDefinition>(StringComparer.Ordinal)
                {
                    [show.Id] = Behavior(show, "instant_gold", ("bonus_gold", 800f), ("carryover_cap_bonus", 800f)),
                    [refund.Id] = Behavior(refund, "roulette_refund", ("refund_fraction", 0.30f)),
                }));

            var pool = new List<BlessingDefinition> { show, refund, filler };
            foreach (var tier in new[] { BlessingTier.Gold, BlessingTier.Platinum })
                for (int i = 0; i < 3; i++)
                    pool.Add(new BlessingDefinition($"{tier}-{i}", $"{tier} {i}", WeaponTag.Common, tier));

            var state = new MatchEconomyState(EconomyConfig.Default, BlessingConfig.Default, pool);
            state.BeginMatch(new[] { new MatchParticipant(Alice, WeaponTag.Sword), new MatchParticipant(Bob, WeaponTag.Bow) },
                0x51);
            state.BeginPreparation();
            return state;
        }

        private static void Pick(MatchEconomyState state, int player, string id)
        {
            int index = state.GetCandidates(player).Select(c => c.Blessing.Id).ToList().IndexOf(id);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), id);
            Assert.That(state.TrySelect(player, index)?.Id, Is.EqualTo(id));
        }

        [Test]
        public void 쇼_미_더_머니는_고르는_순간_골드를_받고_이월_한도가_오른다()
        {
            var state = NewMatch();
            int before = state.GetGold(Alice);

            Pick(state, Alice, "show-me-money-t1");

            Assert.That(state.GetGold(Alice), Is.EqualTo(before + 800));

            state.Earn(Alice, 20_000);
            state.EndPreparation();
            state.BeginPreparation();
            Assert.That(state.BuildSnapshot(Alice).CarriedGold, Is.EqualTo(CarryOverLimit.DefaultGold + 800),
                "이월 한도 10,000 + 800");
        }

        [Test]
        public void 전투_중에_번_골드는_그_플레이어_지갑에_들어간다()
        {
            var state = NewMatch();
            int before = state.GetGold(Bob);

            Assert.That(state.TryGrantBlessingGold(Bob, 100), Is.True);
            Assert.That(state.GetGold(Bob), Is.EqualTo(before + 100));
            Assert.That(state.TryGrantBlessingGold(99, 100), Is.False, "모르는 플레이어");
            Assert.That(state.TryGrantBlessingGold(Bob, 0), Is.False);
        }

        [Test]
        public void 룰렛_환급을_가진_사람만_잃은_금액의_일부를_돌려받는다()
        {
            var state = NewMatch();
            Pick(state, Bob, "roulette-refund-t1");
            int bob = state.GetGold(Bob);
            int alice = state.GetGold(Alice);

            Assert.That(state.RefundRouletteLoss(Bob, 1000), Is.EqualTo(300));
            Assert.That(state.GetGold(Bob), Is.EqualTo(bob + 300));
            Assert.That(state.RefundRouletteLoss(Alice, 1000), Is.EqualTo(0));
            Assert.That(state.GetGold(Alice), Is.EqualTo(alice));
        }
    }
}

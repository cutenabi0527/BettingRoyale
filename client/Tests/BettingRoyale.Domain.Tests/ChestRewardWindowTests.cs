using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Economy;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 상자 보상을 받을 수 있는 때 — 그 라운드 전투가 결과 반영 전일 때뿐이다
    /// (<see cref="MatchEconomyState.IsCombatRoundOpen"/> · S-GD-DFG3VR).
    /// </summary>
    public class ChestRewardWindowTests
    {
        private const int Alice = 1;
        private const int Bob = 2;

        private static IReadOnlyList<BlessingDefinition> Pool()
        {
            var pool = new List<BlessingDefinition>();
            foreach (var tier in new[] { BlessingTier.Silver, BlessingTier.Gold, BlessingTier.Platinum })
            {
                for (int i = 0; i < 6; i++)
                {
                    pool.Add(new BlessingDefinition($"{tier}-{i}", $"{tier} {i}", WeaponTag.Common, tier));
                }
            }

            return pool;
        }

        private static MatchEconomyState NewMatch()
        {
            var economy = new MatchEconomyState(EconomyConfig.Default, BlessingConfig.Default, Pool());
            economy.BeginMatch(
                new[] { new MatchParticipant(Alice, WeaponTag.Sword), new MatchParticipant(Bob, WeaponTag.Sword) },
                seed: 777);
            return economy;
        }

        private static RoundOutcome[] Results() => new[]
        {
            new RoundOutcome(Alice, 0, 0, 0, survived: true, placement: 1),
            new RoundOutcome(Bob, 0, 0, 0, survived: false, placement: 2),
        };

        [Test]
        public void 준비_단계에는_열려_있지_않다()
        {
            MatchEconomyState economy = NewMatch();
            int round = economy.BeginPreparation();

            Assert.That(economy.IsCombatRoundOpen(round), Is.False);
        }

        [Test]
        public void 전투가_시작되면_그_라운드만_열린다()
        {
            MatchEconomyState economy = NewMatch();
            int round = economy.BeginPreparation();
            economy.EndPreparation();

            Assert.That(economy.IsCombatRoundOpen(round), Is.True);
            Assert.That(economy.IsCombatRoundOpen(round + 1), Is.False, "아직 열지 않은 라운드");
            Assert.That(economy.IsCombatRoundOpen(0), Is.False);
        }

        [Test]
        public void 결과가_반영되면_단계가_아직_전투여도_닫힌다()
        {
            // 마지막이 아닌 라운드는 결과 반영 뒤에도 다음 준비 전까지 Phase 가 Combat 으로 남는다.
            MatchEconomyState economy = NewMatch();
            int round = economy.BeginPreparation();
            economy.EndPreparation();

            Assert.That(economy.TryApplyCompleteRoundResults(round, Results()), Is.True);

            Assert.That(economy.Phase, Is.EqualTo(MatchEconomyPhase.Combat), "이 틈이 있다는 전제를 확인한다.");
            Assert.That(economy.IsCombatRoundOpen(round), Is.False,
                "결과가 반영된 라운드의 상자 보상은 주지 않는다 — 라운드 종료가 이긴다 (S-GD-DFG3VR).");
        }

        [Test]
        public void 다음_라운드_전투에서_다시_열린다()
        {
            MatchEconomyState economy = NewMatch();
            int first = economy.BeginPreparation();
            economy.EndPreparation();
            economy.TryApplyCompleteRoundResults(first, Results());

            int second = economy.BeginPreparation();
            economy.EndPreparation();

            Assert.That(economy.IsCombatRoundOpen(first), Is.False);
            Assert.That(economy.IsCombatRoundOpen(second), Is.True);
        }
    }
}

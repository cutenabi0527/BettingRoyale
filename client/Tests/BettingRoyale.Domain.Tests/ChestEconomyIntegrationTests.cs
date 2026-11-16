using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Chest;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Economy;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 상자 보상이 라운드 정산과 다음 라운드 이월을 그대로 탄다 (Jira S15P21A606-32 완료 조건 5).
    ///
    /// <para>
    /// <b>-31 의 경제 코드를 한 줄도 고치지 않는다.</b> 티켓이 *"-31의 골드·정산 계약을 변경하지 않고
    /// 확장합니다"* 를 못박았으므로, 여기서 하는 일은 <c>MatchEconomyState.Earn</c> 이라는
    /// 기존 입구로 상자 보상을 넣고 그 뒤가 원래대로 도는지 <b>확인</b>하는 것뿐이다.
    /// </para>
    ///
    /// <para>
    /// 이월 한도 0·50,000 같은 경계값과 동시성은 -33 의 범위다. 여기서는 정상 경로만 본다.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ChestEconomyIntegrationTests
    {
        private const int Alice = 1;
        private const int OpenTicks = 5;
        private const int RespawnTicks = 3;
        /// <summary>재시도 지연. ⚠️ 검증용 임의값이다 — 확정값 1.0초는 데이터로 들어온다 (S-GD-5AZ6HN).</summary>
        private const int RetryTicks = 3;
        private const int Reward = 500;

        private static IReadOnlyList<WorldPosition> Anchors() => new[]
        {
            new WorldPosition(-7f, 0f, 8f), new WorldPosition(7f, 0f, 8f),
            new WorldPosition(-7f, 0f, -8f), new WorldPosition(7f, 0f, -8f),
        };

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

        private static MatchEconomyState NewEconomy(EconomyConfig config)
        {
            var economy = new MatchEconomyState(config, BlessingConfig.Default, Pool());
            economy.BeginMatch(new[] { new MatchParticipant(Alice, WeaponTag.Sword) }, seed: 777);
            return economy;
        }

        private static ChestField OpenedField() =>
            OpenField(new ChestField(new ChestFieldConfig(
                new ChestConfig(OpenTicks, RespawnTicks, RetryTicks), Anchors(), chestCount: 2, reward: new ChestReward(Reward))));

        private static ChestField OpenField(ChestField field)
        {
            field.BeginRound(new DeterministicRandom(31337));
            return field;
        }

        /// <summary>상자를 한 번 열어 보상을 경제에 넣는다. 어댑터가 할 일을 테스트가 대신한다.</summary>
        private static int OpenOneChestAndEarn(ChestField field, MatchEconomyState economy)
        {
            field.TryBeginOpen(chestId: 0, Alice);

            int earnedTotal = 0;
            for (int i = 0; i < OpenTicks + 2; i++)
            {
                foreach (var earned in field.Tick())
                {
                    // 상자 보상은 -31 의 기존 입구로 들어간다. 새 경로를 만들지 않는다.
                    economy.Earn(earned.ClaimerId, earned.Gold);
                    earnedTotal += earned.Gold;
                }
            }

            return earnedTotal;
        }

        [Test]
        public void 완료조건5_상자_보상이_그_라운드_잔액에_더해진다()
        {
            var economy = NewEconomy(EconomyConfig.Default);
            economy.BeginPreparation();
            int afterGrant = economy.GetGold(Alice);
            economy.EndPreparation();

            var field = OpenedField();
            int earned = OpenOneChestAndEarn(field, economy);

            Assert.That(earned, Is.EqualTo(Reward));
            Assert.That(economy.GetGold(Alice), Is.EqualTo(afterGrant + Reward));
        }

        /// <summary>
        /// 정산 순서는 <b>이월 → 기본 지급</b> 이다. 뒤집히면 이번 라운드에 받은 골드까지
        /// 한도에 걸려 소멸한다. 상자 보상이 그 순서를 바꾸지 않는지 확인한다.
        /// </summary>
        [Test]
        public void 완료조건5_상자_보상이_다음_라운드_이월에_반영된다()
        {
            var economy = NewEconomy(EconomyConfig.Default);

            economy.BeginPreparation();
            int roundGrant = economy.GetGold(Alice);
            economy.EndPreparation();

            var field = OpenedField();
            OpenOneChestAndEarn(field, economy);
            field.EndRound();

            economy.TryApplyRoundResults(1, new[] { new RoundOutcome(Alice, 0, 0, true) });
            economy.BeginPreparation();   // 2라운드 — 여기서 1라운드 잔액이 이월된다

            Assert.That(
                economy.GetGold(Alice),
                Is.EqualTo(roundGrant + Reward + roundGrant),
                "이월된 (기본 지급 + 상자 보상) 위에 2라운드 기본 지급이 얹혀야 한다");
        }

        [Test]
        public void 완료조건5_정산_이벤트의_이월액에_상자_보상이_들어간다()
        {
            var economy = NewEconomy(EconomyConfig.Default);

            economy.BeginPreparation();
            int roundGrant = economy.GetGold(Alice);
            economy.EndPreparation();

            var field = OpenedField();
            OpenOneChestAndEarn(field, economy);
            field.EndRound();

            economy.TryApplyRoundResults(1, new[] { new RoundOutcome(Alice, 0, 0, true) });
            economy.BeginPreparation();

            Assert.That(economy.TryBuildRoundGoldSettled(Alice, isMine: true, out var settled), Is.True);
            Assert.That(settled.Round, Is.EqualTo(1), "정산 대상은 1라운드다");
            Assert.That(settled.CarriedGold, Is.EqualTo(roundGrant + Reward));
            Assert.That(settled.ForfeitedGold, Is.EqualTo(0));
        }

        /// <summary>
        /// 이월 한도를 넘으면 상자 보상도 똑같이 소멸한다 — 상자 골드라고 특별 대우하지 않는다.
        /// 경계값 전수는 -33 이 다루고, 여기서는 "같은 규칙을 탄다" 만 확인한다.
        /// </summary>
        [Test]
        public void 완료조건5_상자_보상도_이월_한도를_넘으면_소멸한다()
        {
            var config = EconomyConfig.Default.WithCarryOverLimitSetting(1_200);
            var economy = NewEconomy(config);

            economy.BeginPreparation();
            int roundGrant = economy.GetGold(Alice);
            economy.EndPreparation();

            var field = OpenedField();
            OpenOneChestAndEarn(field, economy);
            field.EndRound();

            economy.TryApplyRoundResults(1, new[] { new RoundOutcome(Alice, 0, 0, true) });
            economy.BeginPreparation();

            Assert.That(economy.TryBuildRoundGoldSettled(Alice, isMine: true, out var settled), Is.True);
            Assert.That(settled.CarriedGold, Is.EqualTo(1_200));
            Assert.That(settled.ForfeitedGold, Is.EqualTo(roundGrant + Reward - 1_200));
        }

        [Test]
        public void 완료조건5_새_매치를_시작하면_상자로_번_골드도_초기화된다()
        {
            var economy = NewEconomy(EconomyConfig.Default);
            economy.BeginPreparation();
            economy.EndPreparation();

            var field = OpenedField();
            OpenOneChestAndEarn(field, economy);
            field.EndRound();

            Assert.That(economy.GetGold(Alice), Is.GreaterThan(0));

            economy.BeginMatch(new[] { new MatchParticipant(Alice, WeaponTag.Sword) }, seed: 778);

            Assert.That(economy.GetGold(Alice), Is.EqualTo(0));
        }
    }
}

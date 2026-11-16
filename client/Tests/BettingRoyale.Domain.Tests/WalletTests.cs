using System;
using BettingRoyale.Data.Economy;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 지갑의 네 가지 조작 — 지급 · 획득 · 지출 · 이월 (Jira S15P21A606-31).
    /// 라운드와 매치를 넘나드는 흐름까지 확인한다 (S-GD-2CFK2Z, S-GD-P5RFZM, S-GD-RFXF8X).
    /// </summary>
    [TestFixture]
    public class WalletTests
    {
        [Test]
        public void 라운드_지급은_기본_골드를_더한다()
        {
            var wallet = new Wallet();

            wallet.GrantRound(EconomyConfig.Default);

            Assert.That(wallet.Gold, Is.EqualTo(1_000));
        }

        [Test]
        public void 라운드_지급은_이월된_잔액_위에_더해진다()
        {
            var wallet = new Wallet(3_000);

            wallet.GrantRound(EconomyConfig.Default);

            Assert.That(wallet.Gold, Is.EqualTo(4_000), "이월 잔액을 덮어쓰지 않는다");
        }

        [Test]
        public void 잔액이_모자라면_지출이_실패하고_잔액이_변하지_않는다()
        {
            var wallet = new Wallet(100);

            var spent = wallet.TrySpend(101);

            Assert.That(spent, Is.False);
            Assert.That(wallet.Gold, Is.EqualTo(100), "부분 지출은 없다");
        }

        [Test]
        public void 잔액과_정확히_같은_금액은_지출할_수_있다()
        {
            var wallet = new Wallet(100);

            Assert.That(wallet.TrySpend(100), Is.True);
            Assert.That(wallet.Gold, Is.EqualTo(0));
        }

        [Test]
        public void 획득에_음수를_넣으면_예외다()
        {
            var wallet = new Wallet();

            Assert.That(() => wallet.Earn(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void 라운드_정산은_한도를_적용하고_잔액을_갱신한다()
        {
            var wallet = new Wallet();
            wallet.Earn(12_000);

            var result = wallet.SettleRound(CarryOverLimit.Default);

            Assert.That(result.CarriedGold, Is.EqualTo(10_000));
            Assert.That(result.ForfeitedGold, Is.EqualTo(2_000));
            Assert.That(wallet.Gold, Is.EqualTo(10_000), "정산 후 잔액 = 이월액");
        }

        [Test]
        public void 새_매치를_시작하면_지갑이_비워진다()
        {
            var wallet = new Wallet(50_000);

            wallet.ResetForNewMatch();

            Assert.That(wallet.Gold, Is.EqualTo(0), "이월은 매치를 넘지 않는다 (S-GD-RFXF8X)");
        }

        /// <summary>
        /// 3라운드 흐름을 한 번에 확인한다. 각 라운드의 정산 결과가 다음 라운드의 시작 잔액이 된다 —
        /// 2PC 에서 같은 값이 나와야 하는 계산이 정확히 이것이다 (S15P21A606-31).
        /// </summary>
        [Test]
        public void 라운드를_이어가면_이월이_누적된다()
        {
            var config = EconomyConfig.Default; // 지급 1,000G / 한도 10,000G
            var wallet = new Wallet();

            // 1라운드: 지급 1,000 + 획득 8,000 = 9,000 → 한도 이하라 전부 이월
            wallet.GrantRound(config);
            wallet.Earn(8_000);
            var round1 = wallet.SettleRound(config.CarryOverLimit);

            Assert.That(round1.CarriedGold, Is.EqualTo(9_000));
            Assert.That(round1.ForfeitedGold, Is.EqualTo(0));

            // 2라운드: 9,000 + 지급 1,000 + 획득 5,000 = 15,000 → 5,000 소멸
            wallet.GrantRound(config);
            wallet.Earn(5_000);
            var round2 = wallet.SettleRound(config.CarryOverLimit);

            Assert.That(round2.CarriedGold, Is.EqualTo(10_000));
            Assert.That(round2.ForfeitedGold, Is.EqualTo(5_000));

            // 3라운드: 10,000 + 지급 1,000 - 지출 3,000 = 8,000 → 전부 이월
            wallet.GrantRound(config);
            Assert.That(wallet.TrySpend(3_000), Is.True);
            var round3 = wallet.SettleRound(config.CarryOverLimit);

            Assert.That(round3.CarriedGold, Is.EqualTo(8_000));
            Assert.That(round3.ForfeitedGold, Is.EqualTo(0));
        }

        [Test]
        public void 무한_이월에서는_라운드를_거듭해도_소멸이_없다()
        {
            var config = EconomyConfig.Default.WithCarryOverLimitSetting(50_001);
            var wallet = new Wallet();

            for (var round = 0; round < 5; round++)
            {
                wallet.GrantRound(config);
                wallet.Earn(20_000);
                var result = wallet.SettleRound(config.CarryOverLimit);

                Assert.That(result.ForfeitedGold, Is.EqualTo(0), $"{round + 1}라운드에서 소멸이 발생했다");
            }

            Assert.That(wallet.Gold, Is.EqualTo((1_000 + 20_000) * 5));
        }

        [Test]
        public void 한도가_0이면_라운드마다_지급액만_남는다()
        {
            var config = EconomyConfig.Default.WithCarryOverLimitSetting(0);
            var wallet = new Wallet();

            wallet.GrantRound(config);
            wallet.Earn(9_999);
            wallet.SettleRound(config.CarryOverLimit);

            wallet.GrantRound(config);

            Assert.That(wallet.Gold, Is.EqualTo(1_000), "이월이 없으면 매 라운드가 지급액에서 시작한다");
        }
    }
}

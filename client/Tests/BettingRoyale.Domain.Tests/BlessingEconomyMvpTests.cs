using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Shop;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 경제형 축복 3종의 도메인 계약 — 리롤 캐시백(재추첨 비용 할인, <see cref="BlessingDraft"/>), 이자 폭발·저점 매수
    /// (<see cref="BlessingEconomyState"/>). 리롤 캐시백·저점 매수는 엑셀 개편 동작이다(S-GD-TMPBEF).
    ///
    /// <para>
    /// <b>실제 카탈로그 수치를 쓴다.</b> 비율을 테스트가 다시 적어두면 CSV 가 바뀔 때 테스트만 옛 값을
    /// 지키게 되므로, 기대값은 <see cref="BlessingMvpCatalog"/> 에서 읽어 계산한다.
    /// </para>
    ///
    /// <para>
    /// <b>후보가 어떻게 뽑히는지는 가정하지 않는다.</b> 재추첨 전후의 id 를 비교해 "새로 등장한 후보" 를
    /// 직접 찾고, 필요한 상황이 나오는 첫 시드를 찾아 쓴다 — 난수 구현이 바뀌어도 계약 검증은 그대로다.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class BlessingEconomyMvpTests
    {
        private const string CashbackId = "reroll-cashback-t1";
        private const string InterestId = "interest-burst-t3";
        private const string LowBuyId = "low-buy-t2";

        private const int StartingGold = 10_000;

        // ──────────────────────────────────────────────────────────────────────
        // 통합 이음매 (integration seams)
        //
        // BlessingConfig · BlessingLoadout · DeterministicRandom 은 이 테스트가 소유하지 않고
        // 작업 스냅샷에도 들어 있지 않다. 실제 정의가 붙을 때 <b>고칠 곳은 아래 세 메서드뿐</b>이다.
        // 각 이음매가 만족해야 하는 계약:
        //   · CreateConfig       — 후보 3장, 무료 잠금 2개, 1라운드 = Silver, 1라운드 재추첨 비용 > 0
        //                          (홀수 비용을 쓰면 내림 검증이 같이 돌아간다)
        //   · CreateLoadout      — 넘긴 정의만 Contains 가 true
        //   · CreateRandom(seed) — 같은 시드면 같은 결과
        // ──────────────────────────────────────────────────────────────────────

        private static BlessingConfig CreateConfig() => new BlessingConfig(
            candidateCount: 3,
            maxFreeLocks: 2,
            tierByRound: new[] { BlessingTier.Silver, BlessingTier.Gold, BlessingTier.Platinum },
            rerollBaseCostByRound: new[] { 125, 150, 200 },
            rerollCostMultiplier: 2);

        private static BlessingLoadout CreateLoadout(IReadOnlyList<BlessingDefinition> acquired)
        {
            var loadout = new BlessingLoadout();
            for (var i = 0; i < acquired.Count; i++)
            {
                loadout.Add(acquired[i]);
            }

            return loadout;
        }

        private static DeterministicRandom CreateRandom(int seed) => new DeterministicRandom(seed);

        // ── 리롤 캐시백: 재추첨 비용 할인 (엑셀 개편 — S-GD-TMPBEF) ──────────

        [Test]
        public void RerollCashback_DiscountsEveryRerollCost_RoundingDown()
        {
            var multiplier = CatalogFraction(CashbackId, "cost_multiplier");
            var plain = BeginDraft(1, Pool(), Acquired());
            var discounted = BeginDraft(1, Pool(), Acquired(CashbackId));

            Assert.IsTrue(plain.TryGetCurrentRerollCost(out var fullFirst));
            Assert.IsTrue(plain.TryGetRerollCostPreview(out var fullSecond));
            Assert.IsTrue(discounted.TryGetCurrentRerollCost(out var first));
            Assert.AreEqual((int)Math.Floor(fullFirst * (double)multiplier), first, "재추첨 비용이 배율만큼 줄고 내림한다.");
            Assert.AreEqual(multiplier, discounted.RerollCostMultiplier, 1e-6);

            Assert.IsTrue(discounted.TryGetRerollCostPreview(out var preview));
            Assert.AreEqual((int)Math.Floor(fullSecond * (double)multiplier), preview, "미리보기도 같은 할인이다.");

            var wallet = new Wallet(StartingGold);
            Assert.AreEqual(RerollResult.Success, discounted.TryReroll(wallet));
            Assert.AreEqual(StartingGold - first, wallet.Gold, "할인된 금액만 낸다.");

            Assert.IsTrue(discounted.TryGetCurrentRerollCost(out var second));
            Assert.AreEqual(preview, second, "두 번째 재추첨도 항상 할인된다.");
            Assert.AreEqual(RerollResult.Success, discounted.TryReroll(wallet));
            Assert.AreEqual(StartingGold - first - second, wallet.Gold);
        }

        [Test]
        public void RerollCashback_NoLongerRefundsOnSelection()
        {
            var draft = BeginDraft(1, Pool(), Acquired(CashbackId));
            var wallet = new Wallet(StartingGold);

            Assert.AreEqual(RerollResult.Success, draft.TryReroll(wallet));
            var afterReroll = wallet.Gold;
            draft.Select(0);

            Assert.AreEqual(afterReroll, wallet.Gold, "고른다고 돈이 돌아오지 않는다.");
            Assert.AreEqual(StartingGold, wallet.TotalInflow);
        }

        [Test]
        public void RerollCashback_AppliesFromTheNextDraft()
        {
            // 지금 고르는 리롤 캐시백은 이번 판 비용을 소급해 바꾸지 않는다.
            var picking = BeginDraft(1, Pool(includeCashbackDefinition: true), Acquired());
            Assert.AreEqual(1f, picking.RerollCostMultiplier);

            var next = BeginDraft(2, Pool(), Acquired(CashbackId));
            Assert.Less(next.RerollCostMultiplier, 1f);
        }

        [Test]
        public void RerollCashback_NotOwned_KeepsFullCost()
        {
            var draft = BeginDraft(1, Pool(), Acquired());
            var wallet = new Wallet(StartingGold);
            Assert.IsTrue(draft.TryGetCurrentRerollCost(out var cost));
            Assert.AreEqual(1f, draft.RerollCostMultiplier);
            Assert.AreEqual(RerollResult.Success, draft.TryReroll(wallet));
            Assert.AreEqual(StartingGold - cost, wallet.Gold);
        }

        // ── 이자 폭발: 예치와 정산 ────────────────────────────────────────────

        [Test]
        public void Deposit_IsNeverAutomatic()
        {
            var state = OpenRound(1, InterestId);

            Assert.IsTrue(state.HasInterest);
            Assert.IsTrue(state.CanDeposit);
            Assert.IsFalse(state.InterestDeposited, "축복을 가졌다고 예치되지 않는다.");
            Assert.AreEqual(0, state.DepositAmount);
        }

        [Test]
        public void Deposit_SpendsExactPrincipalOnce()
        {
            var state = OpenRound(1, InterestId);
            var wallet = new Wallet(500);
            var principal = CatalogNumber(InterestId, "deposit");

            Assert.IsTrue(state.TryDeposit(wallet));

            Assert.AreEqual(500 - principal, wallet.Gold);
            Assert.AreEqual(principal, state.DepositAmount);
            Assert.IsTrue(state.InterestDeposited);
            Assert.IsFalse(state.CanDeposit);

            Assert.IsFalse(state.TryDeposit(wallet), "두 번 예치하지 않는다.");
            Assert.AreEqual(500 - principal, wallet.Gold);
            Assert.AreEqual(principal, state.DepositAmount);
        }

        [Test]
        public void Deposit_WithoutEnoughGold_ChargesNothing()
        {
            var state = OpenRound(1, InterestId);
            var principal = CatalogNumber(InterestId, "deposit");
            var wallet = new Wallet(principal - 1);

            Assert.IsFalse(state.TryDeposit(wallet));

            Assert.AreEqual(principal - 1, wallet.Gold);
            Assert.IsFalse(state.InterestDeposited);
            Assert.AreEqual(0, state.DepositAmount);
        }

        [Test]
        public void Deposit_WithoutBlessing_IsRejected()
        {
            var state = OpenRound(1);
            var wallet = new Wallet(500);

            Assert.IsFalse(state.CanDeposit);
            Assert.IsFalse(state.TryDeposit(wallet));
            Assert.AreEqual(500, wallet.Gold);
        }

        [Test]
        public void Deposit_AfterPreparationClosed_IsRejected()
        {
            var state = OpenRound(1, InterestId);
            var wallet = new Wallet(500);

            state.EndPreparation();
            state.EndPreparation(); // 여러 번 닫아도 같다.

            Assert.IsFalse(state.IsPreparationOpen);
            Assert.IsFalse(state.CanDeposit);
            Assert.IsFalse(state.TryDeposit(wallet));
            Assert.AreEqual(500, wallet.Gold);
        }

        [Test]
        public void Deposit_NullWallet_Throws()
        {
            var state = OpenRound(1, InterestId);
            Assert.Throws<ArgumentNullException>(() => state.TryDeposit(null));
        }

        [Test]
        public void Settle_Survived_RefundsPrincipalFractionWithoutInflow()
        {
            var state = OpenRound(1, InterestId);
            var wallet = new Wallet(500);
            var principal = CatalogNumber(InterestId, "deposit");
            var fraction = CatalogFraction(InterestId, "refund_fraction");

            Assert.IsTrue(state.TryDeposit(wallet));
            state.EndPreparation();

            var refunded = state.SettleRound(1, survived: true, wallet);

            Assert.AreEqual((int)Math.Floor(principal * (double)fraction), refunded);
            Assert.AreEqual(500 - principal + refunded, wallet.Gold);
            Assert.AreEqual(500, wallet.TotalInflow, "환급은 새 수입이 아니다.");
            Assert.IsTrue(state.RoundSettled);
        }

        [Test]
        public void Settle_Death_LosesDeposit()
        {
            var state = OpenRound(1, InterestId);
            var wallet = new Wallet(500);
            var principal = CatalogNumber(InterestId, "deposit");

            Assert.IsTrue(state.TryDeposit(wallet));
            state.EndPreparation();

            Assert.AreEqual(0, state.SettleRound(1, survived: false, wallet));
            Assert.AreEqual(500 - principal, wallet.Gold);
            Assert.IsTrue(state.RoundSettled);
        }

        [Test]
        public void Settle_WithoutDeposit_PaysNothing()
        {
            var state = OpenRound(1, InterestId);
            var wallet = new Wallet(500);

            state.EndPreparation();

            Assert.AreEqual(0, state.SettleRound(1, survived: true, wallet));
            Assert.AreEqual(500, wallet.Gold);
        }

        [Test]
        public void Settle_IsIdempotent()
        {
            var state = OpenRound(1, InterestId);
            var wallet = new Wallet(500);

            Assert.IsTrue(state.TryDeposit(wallet));
            state.EndPreparation();

            var first = state.SettleRound(1, survived: true, wallet);
            var gold = wallet.Gold;

            Assert.Greater(first, 0);
            Assert.AreEqual(0, state.SettleRound(1, survived: true, wallet));
            Assert.AreEqual(0, state.SettleRound(1, survived: false, wallet));
            Assert.AreEqual(gold, wallet.Gold, "두 번째 정산은 돈을 만들지 않는다.");
            Assert.IsTrue(state.RoundSettled);
        }

        [Test]
        public void Settle_BeforePreparationClosed_Throws()
        {
            var state = OpenRound(1, InterestId);
            var wallet = new Wallet(500);

            Assert.IsTrue(state.TryDeposit(wallet));

            Assert.Throws<InvalidOperationException>(() => state.SettleRound(1, true, wallet));
            Assert.AreEqual(500 - CatalogNumber(InterestId, "deposit"), wallet.Gold);
            Assert.IsFalse(state.RoundSettled);
        }

        [Test]
        public void Settle_OtherRound_Throws()
        {
            var state = OpenRound(2, InterestId);
            var wallet = new Wallet(500);

            Assert.IsTrue(state.TryDeposit(wallet));
            state.EndPreparation();

            Assert.Throws<ArgumentOutOfRangeException>(() => state.SettleRound(1, true, wallet));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.SettleRound(3, true, wallet));
            Assert.IsFalse(state.RoundSettled);
        }

        [Test]
        public void Settle_LateForPreviousRound_IsDenied()
        {
            var state = OpenRound(1, InterestId);
            var wallet = new Wallet(500);

            Assert.IsTrue(state.TryDeposit(wallet));
            state.EndPreparation();
            state.SettleRound(1, survived: true, wallet);

            state.BeginPreparation(2, Acquired(InterestId));
            var gold = wallet.Gold;

            // 2라운드가 열린 뒤 1라운드를 다시 정산해 달라는 호출은 순서가 꼬였다는 뜻이다.
            Assert.Throws<ArgumentOutOfRangeException>(() => state.SettleRound(1, true, wallet));
            Assert.AreEqual(gold, wallet.Gold);
        }

        [Test]
        public void Settle_WithoutPreparation_Throws()
        {
            var state = new BlessingEconomyState();
            Assert.Throws<InvalidOperationException>(() => state.SettleRound(1, true, new Wallet(500)));
        }

        [Test]
        public void Settle_NullWallet_Throws()
        {
            var state = OpenRound(1, InterestId);
            state.EndPreparation();

            Assert.Throws<ArgumentNullException>(() => state.SettleRound(1, true, null));
        }

        // ── 준비 단계와 라운드 경계 ──────────────────────────────────────────

        [Test]
        public void BeginPreparation_SameRound_KeepsUsageAndRefundsNothing()
        {
            var state = OpenRound(1, InterestId);
            var wallet = new Wallet(500);
            var principal = CatalogNumber(InterestId, "deposit");

            Assert.IsTrue(state.TryDeposit(wallet));

            // 화면이 준비 단계를 다시 그리며 같은 라운드로 다시 부를 수 있다.
            state.BeginPreparation(1, Acquired(InterestId, LowBuyId));

            Assert.IsTrue(state.InterestDeposited, "같은 라운드의 사용 기록은 살아 있다.");
            Assert.AreEqual(principal, state.DepositAmount);
            Assert.IsFalse(state.CanDeposit);
            Assert.AreEqual(500 - principal, wallet.Gold, "다시 열었다고 환급되지 않는다.");
            Assert.IsTrue(state.HasLowBuy, "보유 목록은 갱신된다.");
        }

        [Test]
        public void BeginPreparation_NewRound_ResetsUsage()
        {
            var state = OpenRound(1, InterestId);
            var wallet = new Wallet(500);

            Assert.IsTrue(state.TryDeposit(wallet));
            state.EndPreparation();
            state.SettleRound(1, survived: true, wallet);

            state.BeginPreparation(2, Acquired(InterestId));

            Assert.AreEqual(2, state.Round);
            Assert.IsFalse(state.InterestDeposited);
            Assert.AreEqual(0, state.DepositAmount);
            Assert.IsFalse(state.RoundSettled);
            Assert.IsTrue(state.IsPreparationOpen);
            Assert.IsTrue(state.CanDeposit);
        }

        [Test]
        public void BeginPreparation_NewRound_WithPendingDeposit_Throws()
        {
            var state = OpenRound(1, InterestId);
            var wallet = new Wallet(500);

            Assert.IsTrue(state.TryDeposit(wallet));
            state.EndPreparation();

            var gold = wallet.Gold;

            // 생사를 모르는 채로 넘어가면 예치금이 조용히 사라지거나 조용히 돌아온다.
            Assert.Throws<InvalidOperationException>(
                () => state.BeginPreparation(2, Acquired(InterestId)));

            Assert.AreEqual(gold, wallet.Gold);
            Assert.AreEqual(1, state.Round);
            Assert.IsTrue(state.InterestDeposited);
        }

        [Test]
        public void BeginPreparation_RejectsBackwardsAndInvalidRounds()
        {
            var state = OpenRound(2, InterestId);

            Assert.Throws<ArgumentOutOfRangeException>(() => state.BeginPreparation(1, Acquired()));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.BeginPreparation(0, Acquired()));
            Assert.Throws<ArgumentOutOfRangeException>(() => state.BeginPreparation(-1, Acquired()));
            Assert.Throws<ArgumentNullException>(() => state.BeginPreparation(3, null));
            Assert.AreEqual(2, state.Round);
        }

        [Test]
        public void UpdateLoadout_AfterSelection_KeepsUsageAndEnablesNewBlessing()
        {
            var state = OpenRound(1);
            var wallet = new Wallet(500);

            Assert.IsFalse(state.CanDeposit);

            // 준비 단계에서 축복을 고른 직후 — 이번 라운드부터 예치할 수 있다.
            state.UpdateLoadout(Acquired(InterestId));

            Assert.IsTrue(state.HasInterest);
            Assert.IsTrue(state.CanDeposit);
            Assert.IsTrue(state.TryDeposit(wallet));

            state.UpdateLoadout(Acquired(InterestId, LowBuyId));

            Assert.IsTrue(state.InterestDeposited, "보유 목록 갱신이 사용 기록을 지우지 않는다.");
            Assert.AreEqual(1, state.Round);
            Assert.Throws<ArgumentNullException>(() => state.UpdateLoadout(null));
        }

        // ── 저점 매수 (엑셀 개편 — S-GD-TMPBEF) ─────────────────────────────

        [Test]
        public void LowBuy_ExposesItsRankFraction_ForTheTrialScreen()
        {
            var state = OpenRound(1, LowBuyId);

            Assert.IsTrue(state.HasLowBuy);
            Assert.AreEqual(CatalogFraction(LowBuyId, "rank_fraction"), state.LowBuyRankFraction, 1e-4);
            Assert.AreEqual(0f, OpenRound(1).LowBuyRankFraction, "저점 매수가 없으면 비율도 없다.");
        }

        [Test]
        public void LowBuy_PaysGoldAndPotionsOnceWhenPicked()
        {
            var state = OpenRound(2);
            var wallet = new Wallet(500);
            state.UpdateLoadout(Acquired(LowBuyId));

            var gold = state.GrantAcquisitionBonuses(wallet);

            Assert.AreEqual(CatalogNumber(LowBuyId, "bonus_gold"), gold);
            Assert.AreEqual(500 + gold, wallet.Gold);
            Assert.AreEqual(500 + gold, wallet.TotalInflow, "새로 버는 골드다.");
            CollectionAssert.AreEquivalent(
                new[]
                {
                    new BlessingItemGrant(PlaceholderShopCatalog.HealthPotionItemId, CatalogNumber(LowBuyId, "health_potion")),
                    new BlessingItemGrant(PlaceholderShopCatalog.SwiftnessPotionItemId, CatalogNumber(LowBuyId, "haste_potion")),
                },
                state.ConsumePendingItemGrants());
            Assert.IsEmpty(state.ConsumePendingItemGrants(), "물약은 한 번만 꺼낸다.");

            Assert.AreEqual(0, state.GrantAcquisitionBonuses(wallet), "같은 축복으로 두 번 받지 않는다.");
            Assert.IsEmpty(state.ConsumePendingItemGrants());
        }

        [Test]
        public void LowBuy_MultipliesKillScoreOnlyInThePickedRound()
        {
            var state = OpenRound(2);
            state.UpdateLoadout(Acquired(LowBuyId));
            Assert.AreEqual(1f, state.KillScoreMultiplierFor(2), "고르기 전에는 배율이 없다.");

            state.GrantAcquisitionBonuses(new Wallet(0));

            Assert.AreEqual(CatalogFraction(LowBuyId, "kill_score_multiplier"), state.KillScoreMultiplierFor(2), 1e-6);
            Assert.AreEqual(1f, state.KillScoreMultiplierFor(1));
            Assert.AreEqual(1f, state.KillScoreMultiplierFor(3), "고른 라운드만이다.");
            Assert.AreEqual(1f, OpenRound(2).KillScoreMultiplierFor(2), "없으면 늘 1 이다.");
        }

        [Test]
        public void LowBuy_IsOfferedOnlyToLowerRanks()
        {
            var lowBuy = Definition(LowBuyId);
            var scores = Standings((1, 40), (2, 30), (3, 20), (4, 10));

            Assert.IsTrue(BlessingEconomyState.IsOfferable(lowBuy, 4, scores));
            Assert.IsFalse(BlessingEconomyState.IsOfferable(lowBuy, 1, scores), "상위권에게는 나오지 않는다.");
            Assert.IsFalse(BlessingEconomyState.IsOfferable(lowBuy, 1, Standings()), "1라운드엔 순위표가 없다.");
            Assert.IsFalse(BlessingEconomyState.IsOfferable(lowBuy, 1, Standings((1, 10), (2, 10))), "전원 동점이면 없다.");
            Assert.IsFalse(BlessingEconomyState.IsOfferable(lowBuy, 1, null), "순위표를 모르면 주지 않는다.");
            Assert.IsTrue(BlessingEconomyState.IsOfferable(Definition(InterestId), 1, Standings()), "다른 축복은 거르지 않는다.");
        }

        [Test]
        public void LowBuy_DraftShowsItOnlyToLowerRanks()
        {
            var pool = new List<BlessingDefinition>();
            for (var i = 0; i < 4; i++)
            {
                pool.Add(new BlessingDefinition($"test-gold-{i}", $"골드 {i}", WeaponTag.Common, BlessingTier.Gold));
            }

            pool.Add(Definition(LowBuyId));
            var scores = Standings((1, 40), (2, 10));
            var seenForLower = false;

            for (var seed = 1; seed <= 64; seed++)
            {
                var leader = BlessingDraft.Begin(CreateConfig(), pool, WeaponTag.Sword, 2, CreateLoadout(Acquired()),
                    CreateRandom(seed), offerable: d => BlessingEconomyState.IsOfferable(d, 1, scores));
                Assert.IsFalse(Contains(Ids(leader), LowBuyId), "상위권에게는 후보로 나오지 않는다.");
                Assert.AreEqual(4, leader.PoolSize);

                var lower = BlessingDraft.Begin(CreateConfig(), pool, WeaponTag.Sword, 2, CreateLoadout(Acquired()),
                    CreateRandom(seed), offerable: d => BlessingEconomyState.IsOfferable(d, 2, scores));
                seenForLower |= Contains(Ids(lower), LowBuyId);
            }

            Assert.IsTrue(seenForLower, "하위권에게는 후보로 나온다.");
        }

        // ── 하위권 판정 ──────────────────────────────────────────────────────

        [Test]
        public void IsLowerHalf_SplitsStandingsByStrictlyHigherScores()
        {
            var scores = Standings((1, 40), (2, 30), (3, 20), (4, 10));

            Assert.IsTrue(BlessingEconomyState.IsLowerHalf(4, scores, 0.5f));
            Assert.IsTrue(BlessingEconomyState.IsLowerHalf(3, scores, 0.5f));
            Assert.IsFalse(BlessingEconomyState.IsLowerHalf(2, scores, 0.5f));
            Assert.IsFalse(BlessingEconomyState.IsLowerHalf(1, scores, 0.5f));
        }

        [Test]
        public void IsLowerHalf_DoesNotSplitTiesAtTheCutoff()
        {
            // 경계에 동점자가 몰렸다고 누구는 되고 누구는 안 되는 자리를 만들지 않는다.
            var tied = Standings((1, 30), (2, 20), (3, 20), (4, 10));

            Assert.IsFalse(BlessingEconomyState.IsLowerHalf(2, tied, 0.5f));
            Assert.IsFalse(BlessingEconomyState.IsLowerHalf(3, tied, 0.5f));
            Assert.IsTrue(BlessingEconomyState.IsLowerHalf(4, tied, 0.5f));

            var allTied = Standings((1, 10), (2, 10), (3, 10), (4, 10));

            for (var playerId = 1; playerId <= 4; playerId++)
            {
                Assert.IsFalse(
                    BlessingEconomyState.IsLowerHalf(playerId, allTied, 0.5f), "전원 동점이면 하위권이 없다.");
            }
        }

        [Test]
        public void IsLowerHalf_OddCountKeepsFractionMeaning()
        {
            var scores = Standings((1, 50), (2, 40), (3, 30), (4, 20), (5, 10));

            // 5명의 하위 50% 는 2명이다 — 3위는 들지 않는다.
            Assert.IsTrue(BlessingEconomyState.IsLowerHalf(5, scores, 0.5f));
            Assert.IsTrue(BlessingEconomyState.IsLowerHalf(4, scores, 0.5f));
            Assert.IsFalse(BlessingEconomyState.IsLowerHalf(3, scores, 0.5f));

            // 하위 30% 는 1명이다.
            Assert.IsTrue(BlessingEconomyState.IsLowerHalf(5, scores, 0.3f));
            Assert.IsFalse(BlessingEconomyState.IsLowerHalf(4, scores, 0.3f));
        }

        [Test]
        public void IsLowerHalf_WithoutStandings_IsFalse()
        {
            // 1라운드에는 직전 누적 순위가 없다 — 시작하자마자 전원이 하위권이 되면 안 된다.
            Assert.IsFalse(BlessingEconomyState.IsLowerHalf(1, Standings(), 0.5f));
            Assert.IsFalse(BlessingEconomyState.IsLowerHalf(1, Standings((1, 10)), 0.5f));
        }

        [Test]
        public void IsLowerHalf_PlayerNotInStandings_IsFalse()
        {
            var scores = Standings((1, 40), (2, 30), (3, 20));

            Assert.IsFalse(BlessingEconomyState.IsLowerHalf(99, scores, 0.5f));
        }

        [Test]
        public void IsLowerHalf_RejectsInvalidInput()
        {
            var scores = Standings((1, 40), (2, 30), (3, 20), (4, 10));

            Assert.Throws<ArgumentNullException>(() => BlessingEconomyState.IsLowerHalf(1, null, 0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => BlessingEconomyState.IsLowerHalf(1, scores, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => BlessingEconomyState.IsLowerHalf(1, scores, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => BlessingEconomyState.IsLowerHalf(1, scores, -0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => BlessingEconomyState.IsLowerHalf(1, scores, 1.5f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => BlessingEconomyState.IsLowerHalf(1, scores, float.NaN));

            var duplicated = Standings((1, 40), (1, 10), (2, 20));
            Assert.Throws<ArgumentException>(() => BlessingEconomyState.IsLowerHalf(1, duplicated, 0.5f));
        }

        // ── 준비 → 전투 경계: 어댑터가 실제로 읽는 것 ────────────────────────
        //
        // 아래 넷은 "도메인이 혼자 맞다" 를 넘어 <b>조립이 무엇을 언제 읽는가</b>를 고정한다.
        //   · LocalPlayMatchRuntime.RebuildBlessings → BeginRound 에서 다시 실행되며
        //     InterestDeposited 로 전투 버프를 켠다.
        //   · OnlineEconomyCoordinator.ConfigureBlessingContracts 가 같은 값을 읽는다.
        // 둘 다 <b>준비가 닫힌 뒤</b>에 일어난다.

        [Test]
        public void OptIn_SurvivesPreparationClose_ForCombatAdapters()
        {
            var state = OpenRound(1, InterestId, LowBuyId);
            state.UpdateLoadout(Acquired(InterestId, LowBuyId));
            var wallet = new Wallet(500);

            Assert.IsTrue(state.TryDeposit(wallet));

            state.EndPreparation();

            Assert.IsFalse(state.IsPreparationOpen);
            Assert.IsTrue(state.InterestDeposited, "전투 시작 때 이자 폭발 버프를 켤 근거가 남아야 한다.");
            Assert.AreEqual(1, state.Round);
        }

        [Test]
        public void OptIn_IsPerRound_AndNewRoundRequiresAcceptingAgain()
        {
            var state = OpenRound(1, InterestId, LowBuyId);
            state.UpdateLoadout(Acquired(InterestId, LowBuyId));
            var wallet = new Wallet(500);

            Assert.IsTrue(state.TryDeposit(wallet));
            state.EndPreparation();
            state.SettleRound(1, survived: true, wallet);

            state.BeginPreparation(2, Acquired(InterestId, LowBuyId));

            Assert.IsFalse(state.InterestDeposited, "예치는 라운드마다 다시 눌러야 한다.");
            Assert.IsTrue(state.CanDeposit);
            Assert.AreEqual(0, state.DepositAmount);
        }

        [Test]
        public void Loadout_OrderDoesNotDecideWhichContractIsSeen()
        {
            // 보유 목록의 순서는 획득 순서다. 한쪽만 잡히면 예치나 저점 매수가 <b>조용히</b> 사라진다.
            foreach (var ids in new[]
            {
                new[] { InterestId, LowBuyId },
                new[] { LowBuyId, InterestId },
            })
            {
                var label = string.Join(",", ids);
                var state = new BlessingEconomyState();
                state.BeginPreparation(1, Acquired(ids));

                Assert.IsTrue(state.HasInterest, label);
                Assert.IsTrue(state.HasLowBuy, label);
                Assert.IsTrue(state.CanDeposit, label);
                Assert.AreEqual(CatalogNumber(InterestId, "deposit"), state.DepositCost, label);
                Assert.AreEqual(CatalogFraction(LowBuyId, "rank_fraction"), state.LowBuyRankFraction, 1e-4, label);

                var constructed = new BlessingEconomyState(Acquired(ids));
                Assert.IsTrue(constructed.HasInterest, label);
                Assert.IsTrue(constructed.HasLowBuy, label);
            }
        }

        [Test]
        public void Lifecycle_ThreeRounds_MatchesAdapterCallOrder()
        {
            var wallet = new Wallet(1000);
            var principal = CatalogNumber(InterestId, "deposit");
            var fraction = CatalogFraction(InterestId, "refund_fraction");
            var acquired = Acquired(InterestId, LowBuyId);
            var state = new BlessingEconomyState();

            // 1라운드 — 예치하고 탈락한다.
            state.BeginPreparation(1, acquired);
            Assert.IsTrue(state.TryDeposit(wallet));
            state.EndPreparation();
            Assert.AreEqual(0, state.SettleRound(1, survived: false, wallet), "탈락하면 예치금은 사라진다.");
            Assert.AreEqual(1000 - principal, wallet.Gold);

            // 2라운드 — 예치한 뒤 살아남는다.
            state.BeginPreparation(2, acquired);
            Assert.IsTrue(state.TryDeposit(wallet));
            state.EndPreparation();
            var refunded = state.SettleRound(2, survived: true, wallet);
            Assert.AreEqual((int)Math.Floor(principal * (double)fraction), refunded);
            Assert.AreEqual(1000 - principal - principal + refunded, wallet.Gold);

            // 3라운드 — 아무것도 누르지 않으면 정산은 0 이고 잔액은 그대로다.
            state.BeginPreparation(3, acquired);
            var before = wallet.Gold;
            state.EndPreparation();
            Assert.AreEqual(0, state.SettleRound(3, survived: true, wallet));
            Assert.AreEqual(before, wallet.Gold);

            // 예치·환급은 낸 돈의 왕복이라 누적 획득을 늘리지 않는다 (S-GD-MNFBV7).
            Assert.AreEqual(1000, wallet.TotalInflow);
        }

        // ── 도우미 ───────────────────────────────────────────────────────────

        private static BlessingDraft BeginDraft(
            int seed, IReadOnlyList<BlessingDefinition> pool, IReadOnlyList<BlessingDefinition> acquired) =>
            BeginDraft(seed, pool, acquired, round: 1);

        private static BlessingDraft BeginDraft(
            int seed, IReadOnlyList<BlessingDefinition> pool, IReadOnlyList<BlessingDefinition> acquired, int round) =>
            BlessingDraft.Begin(
                CreateConfig(),
                pool,
                WeaponTag.Sword,
                round,
                CreateLoadout(acquired),
                CreateRandom(seed));

        private static List<string> Ids(BlessingDraft draft)
        {
            var ids = new List<string>();
            foreach (var candidate in draft.Candidates)
            {
                ids.Add(candidate.Blessing.Id);
            }

            return ids;
        }

        private static bool Contains(IReadOnlyList<string> ids, string id)
        {
            if (ids == null)
            {
                return false;
            }

            for (var i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>실버 등급 공용 후보 풀. 재추첨이 실제로 카드를 바꿀 수 있도록 후보 수보다 넉넉하다.</summary>
        private static IReadOnlyList<BlessingDefinition> Pool(bool includeCashbackDefinition = false)
        {
            var pool = new List<BlessingDefinition>();

            for (var i = 0; i < 12; i++)
            {
                pool.Add(new BlessingDefinition(
                    $"test-silver-{i:00}", $"테스트 축복 {i:00}", WeaponTag.Common, BlessingTier.Silver));
            }

            if (includeCashbackDefinition)
            {
                pool.Add(Definition(CashbackId));
            }

            return pool;
        }

        private static IReadOnlyList<BlessingDefinition> Acquired(params string[] ids)
        {
            var acquired = new List<BlessingDefinition>(ids.Length);
            foreach (var id in ids)
            {
                acquired.Add(Definition(id));
            }

            return acquired;
        }

        private static BlessingDefinition Definition(string id)
        {
            Assert.IsTrue(BlessingMvpCatalog.TryGet(id, out var entry), $"카탈로그에 {id} 가 없습니다.");
            return entry.Definition;
        }

        private static float CatalogFraction(string id, string key)
        {
            BlessingMvpCatalog.TryGet(id, out var entry);
            return entry.Number(key);
        }

        private static int CatalogNumber(string id, string key) => (int)Math.Floor(CatalogFraction(id, key));

        /// <summary>준비 단계를 연 상태를 만든다. 하위권 자격은 기본적으로 없다.</summary>
        private static BlessingEconomyState OpenRound(int round, params string[] acquiredIds)
        {
            var state = new BlessingEconomyState();
            state.BeginPreparation(round, Acquired(acquiredIds));
            return state;
        }

        private static IReadOnlyList<BlessingScore> Standings(params (int PlayerId, int Score)[] entries)
        {
            var scores = new List<BlessingScore>(entries.Length);
            foreach (var entry in entries)
            {
                scores.Add(new BlessingScore(entry.PlayerId, entry.Score));
            }

            return scores;
        }
    }
}

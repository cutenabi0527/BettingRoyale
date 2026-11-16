using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.LocalPlay;
using BettingRoyale.Data.Shop;
using BettingRoyale.Data.Training;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 훈련 경제형 축복 시험 주기.
    ///
    /// <para>
    /// <b>축복 id 를 적어두지 않는다.</b> 카탈로그에서 제품 타입에게 직접 물어 찾는다 —
    /// 이자 폭발·저점 매수는 <see cref="BlessingEconomyState"/> 가 알아보는 것이 정본이고,
    /// 여기에 id 를 복사해두면 카탈로그가 바뀌었을 때 테스트만 옛 세계에 남는다.
    /// </para>
    /// </summary>
    public class TrainingBlessingTrialTests
    {
        private sealed class Inventory : IEquipmentInventoryDriver, IEquipmentItemCatalog, IShopInventoryProvider
        {
            private readonly EquipmentInventory inner = new();
            public EquipmentInventorySnapshot EquipmentSnapshot => inner.Snapshot;
            public bool TryGet(int id, out EquipmentItemDefinition definition)
            {
                definition = new EquipmentItemDefinition(2103, EquipmentItemKind.Consumable,
                    EquipmentArchetype.None, 5103, WeaponType.None, 3);
                return id == 2103;
            }
            public bool TryResolve(int id, out IEquipmentInventoryDriver inventory) { inventory = this; return id > 0; }
            public EquipmentInventoryOperationResult TryAuthorityPickup(int id, int quantity) => inner.TryPickup(id, quantity, this);
            public EquipmentInventoryOperationResult TryAuthorityDropWeapon() => inner.TryDropWeapon();
            public EquipmentInventoryOperationResult TryAuthorityUseConsumable(int slot) => inner.TryUseConsumable(slot, this);
            public void ApplyPresentation(in EquipmentInventorySnapshot snapshot) => inner.ApplySnapshot(snapshot);
        }

        /// <summary>한 참가자와 그 참가자의 순번을 함께 들고 다니는 조작 손잡이.</summary>
        private sealed class Player
        {
            public readonly TrainingPracticeState State;
            private int sequence;

            public Player(int playerId, int seed = 97) =>
                State = new TrainingPracticeState("session-one", playerId, WeaponType.Sword,
                    PlaceholderShopCatalog.Create(), new Inventory(), new Inventory(),
                    LocalPlayBlessingCatalog.All, seed);

            public int Gold => State.Gold;
            public TrainingBlessingTrialView View => State.BuildTrialView();

            public TrainingActionRequest Next(TrainingActionKind kind, int value = 0, string selection = "") =>
                new(State.SessionId, ++sequence, kind, value, selection);

            public TrainingActionResult Send(TrainingActionKind kind, int value = 0, string selection = "") =>
                State.Handle(State.PlayerId, Next(kind, value, selection));

            public TrainingActionResult Send(in TrainingActionRequest request) =>
                State.Handle(State.PlayerId, request);

            public void Equip(BlessingDefinition blessing) =>
                Assert.That(Send(TrainingActionKind.SelectBlessing, selection: blessing.Id).Accepted, Is.True,
                    "시험 전에 축복을 직접 장착할 수 있어야 한다: " + blessing.Id);
        }

        private static IReadOnlyList<BlessingDefinition> Catalog => LocalPlayBlessingCatalog.All;

        /// <summary>제품 타입에게 "이게 이자 폭발인가" 를 묻는다. 효과 판정의 정본이 그쪽이다.</summary>
        private static BlessingDefinition Interest => Require(
            Catalog.FirstOrDefault(d => d.IsCompatibleWith(WeaponTag.Sword)
                                        && new BlessingEconomyState(new[] { d }).HasInterest),
            "이자 폭발");

        private static BlessingDefinition LowBuy => Require(
            Catalog.FirstOrDefault(d => d.IsCompatibleWith(WeaponTag.Sword)
                                        && new BlessingEconomyState(new[] { d }).HasLowBuy),
            "저점 매수");

        private static BlessingDefinition Cashback => Require(
            Catalog.FirstOrDefault(d => d.IsCompatibleWith(WeaponTag.Sword)
                                        && d.Id.StartsWith("reroll-cashback", StringComparison.Ordinal)),
            "리롤 캐시백");

        /// <summary>저점 매수를 고르면 즉시 받는 골드(엑셀 개편 — S-GD-TMPBEF). 시험마다 새 경제라 시험마다 받는다.</summary>
        private static int LowBuyGold
        {
            get
            {
                Assert.That(BlessingMvpCatalog.TryGet(LowBuy.Id, out var entry), Is.True);
                return (int)Math.Floor(entry.Number("bonus_gold"));
            }
        }

        private static BlessingDefinition Require(BlessingDefinition found, string name)
        {
            Assert.That(found, Is.Not.Null, $"카탈로그에 {name} 축복이 있어야 시험 주기를 검증할 수 있다.");
            return found;
        }

        private Player me;

        [SetUp]
        public void Setup() => me = new Player(1);

        // ── 국면 전이 ─────────────────────────────────────────────────────

        [Test]
        public void TrialWalksIdleToPreparationToCombatToSettled()
        {
            Assert.That(me.State.Trial.Phase, Is.EqualTo(TrainingBlessingTrialPhase.Idle));
            Assert.That(me.View.CanBegin, Is.True);

            Assert.That(me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None).Accepted, Is.True);
            Assert.That(me.State.Trial.Phase, Is.EqualTo(TrainingBlessingTrialPhase.Preparation));
            Assert.That(me.View.CanBegin, Is.False, "진행 중인 주기를 덮어쓰면 예치금이 조용히 사라진다");

            Assert.That(me.Send(TrainingActionKind.TrialCombat).Accepted, Is.True);
            Assert.That(me.State.Trial.Phase, Is.EqualTo(TrainingBlessingTrialPhase.Combat));

            Assert.That(me.Send(TrainingActionKind.TrialSettle, 1).Accepted, Is.True);
            Assert.That(me.State.Trial.Phase, Is.EqualTo(TrainingBlessingTrialPhase.Settled));
            Assert.That(me.View.CanBegin, Is.True, "정산이 끝나면 새 주기를 열 수 있다");

            Assert.That(me.Send(TrainingActionKind.TrialBegin, 2, TrainingBlessingTrialRank.Lower).Accepted, Is.True);
            Assert.That(me.State.Trial.Round, Is.EqualTo(2));
        }

        [Test]
        public void CommandsOutsideTheirPhaseAreRejected()
        {
            foreach (TrainingActionKind kind in new[]
                     {
                         TrainingActionKind.TrialDeposit,
                         TrainingActionKind.TrialReroll, TrainingActionKind.TrialToggleLock,
                         TrainingActionKind.TrialSelect, TrainingActionKind.TrialCombat,
                     })
            {
                Assert.That(me.Send(kind).Accepted, Is.False, kind + " 는 준비 단계에서만 받는다");
            }

            Assert.That(me.Send(TrainingActionKind.TrialSettle, 1).Accepted, Is.False, "전투 중이 아니다");

            me.Equip(Interest);
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);
            Assert.That(me.Send(TrainingActionKind.TrialSettle, 1).Accepted, Is.False, "준비 중에는 정산하지 않는다");

            me.Send(TrainingActionKind.TrialCombat);
            Assert.That(me.Send(TrainingActionKind.TrialDeposit).Accepted, Is.False, "전투 중 예치는 없다");
            Assert.That(me.Send(TrainingActionKind.TrialCombat).Accepted, Is.False);
        }

        [Test]
        public void OpenTrialCannotBeOverwrittenAndBlocksDirectLoadoutChanges()
        {
            me.Equip(Interest);
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);

            Assert.That(me.Send(TrainingActionKind.TrialBegin, 2, TrainingBlessingTrialRank.None).Accepted, Is.False);
            Assert.That(me.State.Trial.Round, Is.EqualTo(1));

            Assert.That(me.Send(TrainingActionKind.SelectBlessing, selection: LowBuy.Id).Accepted, Is.False,
                "시험 중 직접 선택은 이미 뽑아둔 후보와 어긋난다");
            Assert.That(me.Send(TrainingActionKind.SelectWeapon, (int)WeaponType.Bow).Accepted, Is.False);
            Assert.That(me.State.SetWeapon(WeaponType.Bow), Is.False);
            Assert.That(me.State.Weapon, Is.EqualTo(WeaponType.Sword));

            me.Send(TrainingActionKind.TrialCombat);
            me.Send(TrainingActionKind.TrialSettle, 0);

            Assert.That(me.Send(TrainingActionKind.SelectBlessing, selection: LowBuy.Id).Accepted, Is.True,
                "주기가 끝나면 다시 직접 고를 수 있다");
            Assert.That(me.State.SetWeapon(WeaponType.Bow), Is.True);
        }

        [TestCase(0)]
        [TestCase(4)]
        [TestCase(-1)]
        public void TrialBeginRejectsUndefinedTierRound(int round) =>
            Assert.That(me.Send(TrainingActionKind.TrialBegin, round, TrainingBlessingTrialRank.None).Accepted, Is.False);

        [TestCase("")]
        [TestCase("LOWER")]
        [TestCase("bottom")]
        public void TrialBeginRejectsUnknownRankProfile(string profile) =>
            Assert.That(me.Send(TrainingActionKind.TrialBegin, 1, profile).Accepted, Is.False);

        [TestCase(2)]
        [TestCase(-1)]
        public void TrialSettleRejectsValuesOtherThanDeadOrSurvived(int value)
        {
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);
            me.Send(TrainingActionKind.TrialCombat);
            Assert.That(me.Send(TrainingActionKind.TrialSettle, value).Accepted, Is.False);
            Assert.That(me.State.Trial.Phase, Is.EqualTo(TrainingBlessingTrialPhase.Combat));
        }

        // ── 이자 폭발: 진짜 지갑이 낸다 ───────────────────────────────────

        [Test]
        public void DepositWithoutInterestBlessingChargesNothing()
        {
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);
            Assert.That(me.View.HasInterest, Is.False);
            Assert.That(me.View.CanDeposit, Is.False);
            Assert.That(me.Send(TrainingActionKind.TrialDeposit).Accepted, Is.False);
            Assert.That(me.Gold, Is.EqualTo(1000));
            Assert.That(me.View.DepositAmount, Is.Zero);
        }

        [Test]
        public void SurvivedTrialRefundsPartOfTheRealDeposit()
        {
            me.Equip(Interest);
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);

            int cost = me.View.DepositCost;
            Assert.That(cost, Is.GreaterThan(0));
            Assert.That(me.View.CanDeposit, Is.True);

            Assert.That(me.Send(TrainingActionKind.TrialDeposit).Accepted, Is.True);
            Assert.That(me.Gold, Is.EqualTo(1000 - cost), "예치는 훈련 지갑에서 실제로 빠진다");
            Assert.That(me.View.DepositAmount, Is.EqualTo(cost));
            Assert.That(me.View.CanDeposit, Is.False, "한 주기에 두 번 예치하지 않는다");
            Assert.That(me.Send(TrainingActionKind.TrialDeposit).Accepted, Is.False);
            Assert.That(me.Gold, Is.EqualTo(1000 - cost));

            Assert.That(me.State.Trial.InterestDeposited, Is.False, "아직 전투가 아니다");
            me.Send(TrainingActionKind.TrialCombat);
            Assert.That(me.State.Trial.InterestDeposited, Is.True);

            Assert.That(me.Send(TrainingActionKind.TrialSettle, 1).Accepted, Is.True);
            int refund = me.State.Trial.SettlementRefund;
            // 비율은 카탈로그가 정한다 — 여기에 배수를 적어두지 않는다(적어두면 데이터가 바뀔 때
            // 테스트만 옛 값에 남는다). 확인하는 것은 "진짜 지갑에 정확히 그만큼 들어왔는가" 다.
            Assert.That(refund, Is.GreaterThan(0));
            Assert.That(me.Gold, Is.EqualTo(1000 - cost + refund));
            Assert.That(me.State.Trial.InterestDeposited, Is.False, "정산이 끝나면 걸린 돈이 없다");
            Assert.That(me.View.SettlementRefund, Is.EqualTo(refund));
        }

        [Test]
        public void DefeatedTrialLosesTheDepositAndRefundsNothing()
        {
            me.Equip(Interest);
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);
            int cost = me.View.DepositCost;
            me.Send(TrainingActionKind.TrialDeposit);
            me.Send(TrainingActionKind.TrialCombat);

            Assert.That(me.Send(TrainingActionKind.TrialSettle, 0).Accepted, Is.True);
            Assert.That(me.State.Trial.SettlementRefund, Is.Zero);
            Assert.That(me.Gold, Is.EqualTo(1000 - cost));
        }

        [Test]
        public void RealDeathSettlesOnceAndOnlyDuringCombat()
        {
            me.Equip(Interest);
            Assert.That(me.State.FinishTrialOnDefeat(), Is.False, "주기가 없다");

            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);
            Assert.That(me.State.FinishTrialOnDefeat(), Is.False, "준비 중에는 정산하지 않는다");

            int cost = me.View.DepositCost;
            me.Send(TrainingActionKind.TrialDeposit);
            me.Send(TrainingActionKind.TrialCombat);

            Assert.That(me.State.FinishTrialOnDefeat(), Is.True);
            Assert.That(me.State.FinishTrialOnDefeat(), Is.False, "두 번째 사망 통지가 다시 정산하지 않는다");
            Assert.That(me.Gold, Is.EqualTo(1000 - cost));
            Assert.That(me.State.Trial.Phase, Is.EqualTo(TrainingBlessingTrialPhase.Settled));

            Assert.That(me.Send(TrainingActionKind.TrialSettle, 1).Accepted, Is.False,
                "이미 정산된 주기를 생존으로 뒤집을 수 없다");
            Assert.That(me.Gold, Is.EqualTo(1000 - cost));
        }

        [Test]
        public void DepositIsRejectedWhenTheRealWalletCannotPay()
        {
            me.Equip(Interest);
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);
            int cost = me.View.DepositCost;

            // 탈락 주기를 반복해 실제로 잔액을 말린다. 잔액을 대입하는 경로는 도메인에 없다.
            int guard = 0;
            while (me.Gold >= cost && guard++ < 4096)
            {
                Assert.That(me.Send(TrainingActionKind.TrialDeposit).Accepted, Is.True);
                me.Send(TrainingActionKind.TrialCombat);
                me.Send(TrainingActionKind.TrialSettle, 0);
                me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);
            }

            Assert.That(me.Gold, Is.LessThan(cost));
            Assert.That(me.View.HasInterest, Is.True);
            Assert.That(me.View.CanDeposit, Is.False, "잔액까지 본 값이라 버튼이 켜지면 안 된다");

            int before = me.Gold;
            Assert.That(me.Send(TrainingActionKind.TrialDeposit).Accepted, Is.False);
            Assert.That(me.Gold, Is.EqualTo(before), "실패한 예치는 아무것도 바꾸지 않는다");
            Assert.That(me.View.DepositAmount, Is.Zero);
        }

        // ── 저점 매수: 하위권 자격 (보급 도전은 엑셀 개편으로 없어졌다 — S-GD-TMPBEF) ──

        [TestCase(TrainingBlessingTrialRank.None, false)]
        [TestCase(TrainingBlessingTrialRank.Upper, false)]
        [TestCase(TrainingBlessingTrialRank.Tied, false)]
        [TestCase(TrainingBlessingTrialRank.Lower, true)]
        public void LowerHalfEligibilityComesFromTheProductionRule(string profile, bool expected)
        {
            me.Equip(LowBuy);
            me.Send(TrainingActionKind.TrialBegin, 1, profile);

            Assert.That(me.State.Trial.LowerHalfEligible, Is.EqualTo(expected));
            Assert.That(me.View.RankProfile, Is.EqualTo(profile));
        }

        [Test]
        public void LowerProfileQualifiesInEveryTierRoundBecauseTheHistoryIsExplicit()
        {
            me.Equip(LowBuy);
            for (int round = 1; round <= 3; round++)
            {
                me.Send(TrainingActionKind.TrialBegin, round, TrainingBlessingTrialRank.Lower);
                Assert.That(me.State.Trial.LowerHalfEligible, Is.True, "round=" + round);
                me.Send(TrainingActionKind.TrialCombat);
                me.Send(TrainingActionKind.TrialSettle, 1);
            }
        }

        [Test]
        public void LowerHalfNeedsTheLowBuyBlessing()
        {
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.Lower);
            Assert.That(me.View.HasLowBuy, Is.False);
            Assert.That(me.State.Trial.LowerHalfEligible, Is.False, "비율이 없으면 판정 자체가 없다");
        }

        [Test]
        public void LowBuyTrialPaysGoldOnce()
        {
            me.Equip(LowBuy);
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.Lower);

            Assert.That(me.Gold, Is.EqualTo(1000 + LowBuyGold), "시험을 열면 고른 순간 보상을 받는다");

            me.Send(TrainingActionKind.TrialCombat);
            Assert.That(me.Gold, Is.EqualTo(1000 + LowBuyGold), "보상은 한 번뿐이다");
        }

        [Test]
        public void RetiredChallengeCommandNumberIsRejected()
        {
            // 12 는 저점 매수의 옛 "위험 보급 도전" 이었다. 번호를 다시 쓰면 옛 클라이언트의 요청이 다른 명령이 된다.
            Assert.That(Enum.IsDefined(typeof(TrainingActionKind), 12), Is.False);

            me.Equip(LowBuy);
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.Lower);
            int gold = me.Gold;
            Assert.That(me.Send((TrainingActionKind)12).Accepted, Is.False);
            Assert.That(me.Gold, Is.EqualTo(gold));
        }

        // ── 추첨: 진짜 BlessingDraft 가 돈을 쓴다 ─────────────────────────

        [Test]
        public void RerollChargesTheRealWalletAndLocksAreBounded()
        {
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);
            TrainingBlessingTrialView view = me.View;

            Assert.That(view.CandidateIds.Length, Is.EqualTo(3), "1라운드 실버 후보 3개가 있어야 한다");
            Assert.That(view.CanSelect, Is.True);
            Assert.That(view.Locked, Is.All.False);

            Assert.That(me.Send(TrainingActionKind.TrialToggleLock, 0).Accepted, Is.True);
            Assert.That(me.Send(TrainingActionKind.TrialToggleLock, 1).Accepted, Is.True);
            Assert.That(me.Send(TrainingActionKind.TrialToggleLock, 2).Accepted, Is.False, "무료 잠금은 2개까지다");
            Assert.That(me.View.Locked, Is.EqualTo(new[] { true, true, false }));

            Assert.That(me.Send(TrainingActionKind.TrialToggleLock, 3).Accepted, Is.False);
            Assert.That(me.Send(TrainingActionKind.TrialToggleLock, -1).Accepted, Is.False);
            Assert.That(me.Send(TrainingActionKind.TrialSelect, 3).Accepted, Is.False);
            Assert.That(me.Gold, Is.EqualTo(1000));

            int cost = me.View.RerollCost;
            Assert.That(cost, Is.GreaterThan(0));
            string keptFirst = me.View.CandidateIds[0];

            Assert.That(me.Send(TrainingActionKind.TrialReroll).Accepted, Is.True);
            Assert.That(me.Gold, Is.EqualTo(1000 - cost), "재추첨은 훈련 지갑에서 실제로 빠진다");
            Assert.That(me.View.RerollCount, Is.EqualTo(1));
            Assert.That(me.View.CandidateIds[0], Is.EqualTo(keptFirst), "잠근 후보는 그대로다");
            Assert.That(me.View.RerollCost, Is.GreaterThan(cost), "같은 준비 단계의 재추첨은 점점 비싸진다");
        }

        [Test]
        public void TrialSelectionAddsToTheRealTrainingLoadout()
        {
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);
            string chosen = me.View.CandidateIds[1];

            Assert.That(me.Send(TrainingActionKind.TrialSelect, 1).Accepted, Is.True);
            Assert.That(me.State.Acquired.Select(x => x.Id), Does.Contain(chosen));
            Assert.That(me.View.SelectedId, Is.EqualTo(chosen));
            Assert.That(me.View.CanSelect, Is.False);
            Assert.That(me.Send(TrainingActionKind.TrialSelect, 0).Accepted, Is.False, "선택은 한 번뿐이다");
            Assert.That(me.State.Acquired.Count, Is.EqualTo(1));
        }

        [Test]
        public void EligibilityFollowsTheLoadoutBecauseTheRatioComesFromTheBlessing()
        {
            // 저점 매수를 고르기 전에는 비율이 없어 하위권 판정 자체가 없다.
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.Lower);
            Assert.That(me.State.Trial.LowerHalfEligible, Is.False);
            me.Send(TrainingActionKind.TrialCombat);
            me.Send(TrainingActionKind.TrialSettle, 1);

            me.Equip(LowBuy);
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.Lower);
            Assert.That(me.State.Trial.LowerHalfEligible, Is.True);
            Assert.That(me.View.HasLowBuy, Is.True);
        }

        [Test]
        public void SelectionStaysOptionalAndCombatDoesNotChooseForYou()
        {
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);
            me.Send(TrainingActionKind.TrialCombat);

            Assert.That(me.State.Acquired, Is.Empty, "훈련 시험은 고르지 않은 사람을 대신 고르지 않는다");
            Assert.That(me.View.SelectedId, Is.Empty);
        }

        [Test]
        public void ExhaustedCandidatePoolStillAllowsTheEconomyCycle()
        {
            // 실버 후보를 2개만 남을 때까지 직접 가져간다 — 가짜 후보를 만들어 채우지 않는다.
            List<BlessingDefinition> silver = Catalog
                .Where(d => d.IsCompatibleWith(WeaponTag.Sword) && d.Tier == BlessingTier.Silver)
                .ToList();
            Assert.That(silver.Count, Is.GreaterThanOrEqualTo(3));

            me.Equip(Interest);
            List<BlessingDefinition> left = silver
                .Where(d => me.State.Acquired.All(a => a.Id != d.Id))
                .ToList();
            foreach (BlessingDefinition definition in left.Take(left.Count - 2)) me.Equip(definition);

            TrainingActionResult begun = me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);
            Assert.That(begun.Accepted, Is.True);
            Assert.That(begun.Message, Does.Contain("후보"));

            TrainingBlessingTrialView view = me.View;
            Assert.That(view.CandidateIds, Is.Empty);
            Assert.That(view.CanSelect, Is.False);
            Assert.That(view.CanReroll, Is.False);
            Assert.That(me.Send(TrainingActionKind.TrialReroll).Accepted, Is.False);
            Assert.That(me.Send(TrainingActionKind.TrialSelect, 0).Accepted, Is.False);
            Assert.That(me.Send(TrainingActionKind.TrialToggleLock, 0).Accepted, Is.False);
            int preparedGold = 1000 + me.State.Trial.Economy.PreparationBonusGold;
            Assert.That(me.Gold, Is.EqualTo(preparedGold));

            // 추첨이 없어도 예치·정산 주기는 그대로 돈다.
            int cost = me.View.DepositCost;
            Assert.That(me.Send(TrainingActionKind.TrialDeposit).Accepted, Is.True);
            me.Send(TrainingActionKind.TrialCombat);
            Assert.That(me.Send(TrainingActionKind.TrialSettle, 1).Accepted, Is.True);
            Assert.That(me.Gold, Is.EqualTo(preparedGold - cost + me.State.Trial.SettlementRefund));
        }

        // ── 리롤 캐시백: 시작 시점 스냅샷과 잠금 규칙 ──────────────────────

        [Test]
        public void WithoutCashbackAtDraftStartThereIsNoRefund()
        {
            // 캐시백 비율은 주기가 열리는 순간에 한 번 찍힌다 — 도중에 생기는 경로가 없다.
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);
            Assert.That(me.View.HasCashback, Is.False);
            int cost = me.View.RerollCost;
            me.Send(TrainingActionKind.TrialReroll);
            me.Send(TrainingActionKind.TrialSelect, 0);

            Assert.That(me.Gold, Is.EqualTo(1000 - cost));
        }

        [Test]
        public void CashbackDiscountsTheRerollCostInsteadOfRefunding()
        {
            // 엑셀 개편 — 리롤 캐시백은 환급이 아니라 재추첨 비용 할인이다(S-GD-TMPBEF).
            var plain = new Player(2);
            plain.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);
            int fullCost = plain.View.RerollCost;

            me.Equip(Cashback);
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);
            Assert.That(me.View.HasCashback, Is.True);
            Assert.That(BlessingMvpCatalog.TryGet(Cashback.Id, out var entry), Is.True);
            int cost = me.View.RerollCost;
            Assert.That(cost, Is.EqualTo((int)Math.Floor(fullCost * (double)entry.Number("cost_multiplier"))));

            Assert.That(me.Send(TrainingActionKind.TrialReroll).Accepted, Is.True);
            Assert.That(me.Send(TrainingActionKind.TrialSelect, 0).Accepted, Is.True);
            Assert.That(me.Gold, Is.EqualTo(1000 - cost), "할인된 금액만 냈다 — 고른다고 돈이 돌아오지 않는다");
        }

        // ── 격리 · 원장 · 정리 ────────────────────────────────────────────

        [Test]
        public void ForgedSenderCannotDriveSomeoneElsesTrial()
        {
            var rival = new Player(2);
            me.Equip(Interest);

            TrainingActionRequest mine = me.Next(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.Lower);

            Assert.That(me.State.Handle(2, mine).Accepted, Is.False, "다른 송신자가 내 주기를 열 수 없다");
            Assert.That(me.State.Trial.Phase, Is.EqualTo(TrainingBlessingTrialPhase.Idle));
            Assert.That(me.State.Trial.Cycle, Is.Zero);

            Assert.That(me.State.Handle(1, new TrainingActionRequest("other-session", mine.Sequence,
                TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None)).Accepted, Is.False);

            Assert.That(me.Send(mine).Accepted, Is.True, "위조 요청이 실제 참가자의 순번을 소비하지 않는다");
            Assert.That(me.State.Trial.Cycle, Is.EqualTo(1));
            Assert.That(rival.State.Trial.Phase, Is.EqualTo(TrainingBlessingTrialPhase.Idle));
            Assert.That(rival.State.Trial.Cycle, Is.Zero);
        }

        [Test]
        public void ReplayedTrialCommandsAreNeverAppliedTwice()
        {
            me.Equip(Interest);
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);
            int cost = me.View.DepositCost;

            TrainingActionRequest deposit = me.Next(TrainingActionKind.TrialDeposit);
            Assert.That(me.Send(deposit).Accepted, Is.True);
            Assert.That(me.Send(deposit).Duplicate, Is.True);
            Assert.That(me.Gold, Is.EqualTo(1000 - cost), "재전송이 두 번 차감하지 않는다");

            TrainingActionRequest combat = me.Next(TrainingActionKind.TrialCombat);
            Assert.That(me.Send(combat).Accepted, Is.True);
            Assert.That(me.Send(combat).Duplicate, Is.True);

            TrainingActionRequest settle = me.Next(TrainingActionKind.TrialSettle, 1);
            Assert.That(me.Send(settle).Accepted, Is.True);
            int refund = me.State.Trial.SettlementRefund;
            Assert.That(me.Send(settle).Duplicate, Is.True);
            Assert.That(me.Gold, Is.EqualTo(1000 - cost + refund), "재전송이 두 번 환급하지 않는다");
        }

        [Test]
        public void TrialsOfDifferentPlayersDoNotTouchEachOther()
        {
            var rival = new Player(2);
            me.Equip(Interest);
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);
            int cost = me.View.DepositCost;
            me.Send(TrainingActionKind.TrialDeposit);

            rival.Equip(LowBuy);
            rival.Send(TrainingActionKind.TrialBegin, 2, TrainingBlessingTrialRank.Lower);

            Assert.That(me.Gold, Is.EqualTo(1000 - cost));
            Assert.That(rival.Gold, Is.EqualTo(1000 + LowBuyGold));
            Assert.That(me.State.Trial.Round, Is.EqualTo(1));
            Assert.That(rival.State.Trial.Round, Is.EqualTo(2));
            Assert.That(me.State.Trial.LowerHalfEligible, Is.False);
            Assert.That(rival.State.Trial.LowerHalfEligible, Is.True);

            me.Send(TrainingActionKind.TrialCombat);
            me.Send(TrainingActionKind.TrialSettle, 0);
            Assert.That(rival.State.Trial.Phase, Is.EqualTo(TrainingBlessingTrialPhase.Preparation),
                "남의 정산이 내 주기를 끝내지 않는다");
            Assert.That(rival.Gold, Is.EqualTo(1000 + LowBuyGold));
        }

        [Test]
        public void ResetAndCloseDropThePendingTrialWithoutReusingItsCycleNumber()
        {
            me.Equip(Interest);
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);
            me.Send(TrainingActionKind.TrialDeposit);
            int firstCycle = me.State.Trial.Cycle;
            Assert.That(firstCycle, Is.EqualTo(1));

            Assert.That(me.Send(TrainingActionKind.Reset).Accepted, Is.True);
            Assert.That(me.State.Trial.Phase, Is.EqualTo(TrainingBlessingTrialPhase.Idle));
            Assert.That(me.State.Trial.InterestDeposited, Is.False);
            Assert.That(me.State.Trial.Round, Is.Zero);
            Assert.That(me.State.Acquired, Is.Empty);
            Assert.That(me.Gold, Is.EqualTo(1000), "초기화는 지갑도 새로 만든다");

            me.Equip(Interest);
            Assert.That(me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None).Accepted, Is.True);
            Assert.That(me.State.Trial.Cycle, Is.GreaterThan(firstCycle),
                "초기화 전후의 주기가 같은 번호를 쓰면 늦게 온 콜백을 구분할 수 없다");

            int secondCycle = me.State.Trial.Cycle;
            me.State.Close();
            Assert.That(me.State.Trial.Phase, Is.EqualTo(TrainingBlessingTrialPhase.Idle));
            Assert.That(me.State.Trial.Cycle, Is.EqualTo(secondCycle));
        }

        // ── 와이어 v2 ─────────────────────────────────────────────────────

        [Test]
        public void SessionViewAlwaysCarriesANonNullTrial()
        {
            Assert.That(TrainingSessionView.Capture(me.State, 1, 0, false, "").Trial, Is.Not.Null);
            Assert.That(new TrainingSessionView("s", 1, 1, 0, false, 0, WeaponType.Sword, 0, 0, 0, 0,
                false, false, null, null).Trial, Is.SameAs(TrainingBlessingTrialView.Empty));
        }

        [Test]
        public void TrialSnapshotSurvivesTheWireRoundTrip()
        {
            me.Equip(LowBuy);
            me.Equip(Interest);
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.Lower);
            Assert.That(me.View.CandidateIds.Length, Is.EqualTo(3));
            me.Send(TrainingActionKind.TrialDeposit);
            me.Send(TrainingActionKind.TrialToggleLock, 1);

            TrainingSessionView sent = TrainingSessionView.Capture(me.State, 7, 9, true, "시험 중");
            Assert.That(TrainingWireProtocol.TryReadState(TrainingWireProtocol.Serialize(sent), out var got), Is.True);

            TrainingBlessingTrialView a = sent.Trial, b = got.Trial;
            Assert.That(b.Phase, Is.EqualTo(a.Phase));
            Assert.That(b.Cycle, Is.EqualTo(a.Cycle));
            Assert.That(b.Round, Is.EqualTo(a.Round));
            Assert.That(b.RankProfile, Is.EqualTo(TrainingBlessingTrialRank.Lower));
            Assert.That(b.LowerHalfEligible, Is.True);
            Assert.That(b.HasInterest, Is.True);
            Assert.That(b.HasLowBuy, Is.True);
            Assert.That(b.HasCashback, Is.EqualTo(a.HasCashback));
            Assert.That(b.CanBegin, Is.False);
            Assert.That(b.CanDeposit, Is.False);
            Assert.That(b.CanReroll, Is.EqualTo(a.CanReroll));
            Assert.That(b.CanSelect, Is.True);
            Assert.That(b.DepositCost, Is.EqualTo(a.DepositCost));
            Assert.That(b.DepositAmount, Is.EqualTo(a.DepositAmount).And.GreaterThan(0));
            Assert.That(b.RerollCost, Is.EqualTo(a.RerollCost));
            Assert.That(b.RerollCount, Is.EqualTo(a.RerollCount));
            Assert.That(b.SettlementRefund, Is.EqualTo(a.SettlementRefund));
            Assert.That(b.SelectedId, Is.EqualTo(a.SelectedId));
            Assert.That(b.CandidateIds, Is.EqualTo(a.CandidateIds));
            Assert.That(b.Locked, Is.EqualTo(a.Locked));
            Assert.That(b.Locked, Is.EqualTo(new[] { false, true, false }));
        }

        [Test]
        public void EmptyTrialSurvivesTheWireRoundTrip()
        {
            TrainingSessionView sent = TrainingSessionView.Capture(me.State, 1, 0, false, "");
            Assert.That(TrainingWireProtocol.TryReadState(TrainingWireProtocol.Serialize(sent), out var got), Is.True);
            Assert.That(got.Trial.Phase, Is.EqualTo(TrainingBlessingTrialPhase.Idle));
            Assert.That(got.Trial.CandidateIds, Is.Empty);
            Assert.That(got.Trial.Locked, Is.Empty);
            Assert.That(got.Trial.RankProfile, Is.EqualTo(TrainingBlessingTrialRank.None));
            Assert.That(got.Trial.CanBegin, Is.True);
        }

        /// <summary>
        /// <b>보유 한도 도달 여부가 훈련 회선을 건너서도 살아남는다</b> (S-GD-FDHBNA).
        ///
        /// <para>
        /// ⚠️ 이 값은 <b>칸 중간</b>에 붙는다. 쓰기만 하고 읽기를 안 올리거나(또는 그 반대) 하면
        /// 뒤 칸이 통째로 밀려 <b>값이 뒤바뀐 채 조용히 읽힌다</b> — 그래서 칸마다 다른 값을 넣고
        /// 두 칸을 함께 본다. <c>CanAfford</c> 와 <b>다른 축</b>이라는 것도 여기서 지킨다:
        /// 돈은 있는데 더 못 드는 칸(<c>true</c>/<c>true</c>)이 그대로 건너와야 한다.
        /// </para>
        /// </summary>
        [Test]
        public void ShopCarryLimitFlagSurvivesTheWireRoundTrip()
        {
            var offers = new[]
            {
                new BettingRoyale.Data.Contracts.ShopOfferView(
                    1, 2101, EquipmentItemKind.Consumable, 120, 1,
                    canAfford: true, carryLimitReached: true),
                new BettingRoyale.Data.Contracts.ShopOfferView(
                    2, 2103, EquipmentItemKind.Consumable, 250, 2,
                    canAfford: false, carryLimitReached: false),
            };

            TrainingSessionView sent = new TrainingSessionView(
                "s", 1, 1, 0, false, 1_000, WeaponType.Sword, 0, 0, 0, 0,
                false, false, null, string.Empty, false, offers);

            Assert.That(TrainingWireProtocol.TryReadState(TrainingWireProtocol.Serialize(sent), out var got), Is.True);
            Assert.That(got.Offers.Length, Is.EqualTo(2));

            Assert.That(got.Offers[0].CarryLimitReached, Is.True, "한도에 닿은 칸이 회선을 건너며 꺼졌다");
            Assert.That(got.Offers[0].CanAfford, Is.True, "한도와 잔액은 다른 축이다");
            Assert.That(got.Offers[0].OfferId, Is.EqualTo(1));
            Assert.That(got.Offers[0].Quantity, Is.EqualTo(1));

            Assert.That(got.Offers[1].CarryLimitReached, Is.False);
            Assert.That(got.Offers[1].CanAfford, Is.False);
            Assert.That(got.Offers[1].OfferId, Is.EqualTo(2), "칸이 밀렸다 — 판을 올렸는지 확인하라");
            Assert.That(got.Offers[1].Quantity, Is.EqualTo(2));
        }

        [Test]
        public void WireRejectsTruncatedTrialStateAndTrailingBytes()
        {
            me.Equip(Interest);
            me.Send(TrainingActionKind.TrialBegin, 1, TrainingBlessingTrialRank.None);
            byte[] valid = TrainingWireProtocol.Serialize(TrainingSessionView.Capture(me.State, 1, 1, false, ""));

            for (int length = 0; length < valid.Length; length++)
                Assert.That(TrainingWireProtocol.TryReadState(valid.AsSpan(0, length), out _), Is.False, "length=" + length);

            Assert.That(TrainingWireProtocol.TryReadState(valid.Concat(new byte[] { 0 }).ToArray(), out _), Is.False);
            Assert.That(TrainingWireProtocol.TryReadState(valid, out _), Is.True);
        }

        [Test]
        public void WireRejectsOtherProtocolVersions()
        {
            byte[] bytes = TrainingWireProtocol.Serialize(TrainingSessionView.Capture(me.State, 1, 0, false, ""));
            byte current = bytes[0];
            Assert.That(TrainingWireProtocol.TryReadState(bytes, out _), Is.True);
            // 새 버전이 추가돼도 현재 버전을 거절 목록에 남기지 않는다. 나머지 255개를 전부 검사한다.
            for (int version = byte.MinValue; version <= byte.MaxValue; version++)
            {
                if (version == current) continue;
                bytes[0] = (byte)version;
                Assert.That(TrainingWireProtocol.TryReadState(bytes, out _), Is.False, "version=" + version);
            }
        }

        [Test]
        public void WireRejectsMalformedTrialFields()
        {
            me.Equip(LowBuy);
            me.Send(TrainingActionKind.TrialBegin, 2, TrainingBlessingTrialRank.Lower);
            byte[] valid = TrainingWireProtocol.Serialize(TrainingSessionView.Capture(me.State, 1, 1, false, ""));

            int at = FindRankOffset(valid, TrainingBlessingTrialRank.Lower, 2);
            Assert.That(at, Is.GreaterThan(0), "직렬화가 순위 프로필을 싣는다");

            Assert.That(TrainingWireProtocol.TryReadState(With(valid, at, Encoding.UTF8.GetBytes("xxxxx")), out _),
                Is.False, "허용 목록 밖의 프로필");

            // 배치: phase(1) cycle(4) round(4) rankLen(2) rank
            Assert.That(TrainingWireProtocol.TryReadState(With(valid, at - 11, new byte[] { 9 }), out _),
                Is.False, "정의되지 않은 국면");
            Assert.That(TrainingWireProtocol.TryReadState(With(valid, at - 10, BitConverter.GetBytes(-1)), out _),
                Is.False, "음수 주기 번호");
            Assert.That(TrainingWireProtocol.TryReadState(With(valid, at - 6, BitConverter.GetBytes(99)), out _),
                Is.False, "정의되지 않은 등급 라운드");
            Assert.That(TrainingWireProtocol.TryReadState(valid, out _), Is.True, "원본은 그대로 읽힌다");
        }

        [Test]
        public void OversizedTrialSnapshotIsRefusedBeforeItLeaves()
        {
            var oversized = new TrainingBlessingTrialView(TrainingBlessingTrialPhase.Preparation, 1, 1,
                TrainingBlessingTrialRank.None, false, false, false, false, false, false, false, true,
                0, 0, 0, 0, 0, "", new[] { "a", "b", "c", "d" }, new[] { false, false, false, false });
            var view = new TrainingSessionView("s", 1, 1, 0, false, 0, WeaponType.Sword, 0, 0, 0, 0,
                false, false, null, null, false, null, oversized);

            Assert.That(() => TrainingWireProtocol.Serialize(view), Throws.ArgumentException);
        }

        [Test]
        public void UnknownRankProfileCollapsesToNoneInsteadOfLeakingToTheWire()
        {
            var view = new TrainingBlessingTrialView(TrainingBlessingTrialPhase.Idle, 0, 0, "bottom-half",
                false, false, false, false, true, false, false, false,
                0, 0, 0, 0, 0, null, null, null);

            Assert.That(view.RankProfile, Is.EqualTo(TrainingBlessingTrialRank.None));
            Assert.That(view.SelectedId, Is.Empty);
            Assert.That(view.CandidateIds, Is.Empty);
            Assert.That(view.Locked, Is.Empty);
        }

        [Test]
        public void TrialViewArraysAreCopiedNotShared()
        {
            var ids = new[] { "a", "b" };
            var locked = new[] { true, false };
            var view = new TrainingBlessingTrialView(TrainingBlessingTrialPhase.Preparation, 1, 1,
                TrainingBlessingTrialRank.None, false, false, false, false, false, false, false, true,
                0, 0, 0, 0, 0, "a", ids, locked);

            ids[0] = "changed";
            locked[0] = false;

            Assert.That(view.CandidateIds[0], Is.EqualTo("a"));
            Assert.That(view.Locked[0], Is.True);
        }

        [Test]
        public void SingleByteCorruptionNeverThrowsAndNeverYieldsAnInvalidTrial()
        {
            me.Equip(LowBuy);
            me.Send(TrainingActionKind.TrialBegin, 3, TrainingBlessingTrialRank.Lower);
            byte[] valid = TrainingWireProtocol.Serialize(TrainingSessionView.Capture(me.State, 1, 2, false, "x"));

            foreach (byte replacement in new byte[] { 0x00, 0x7F, 0xFF })
            {
                for (int i = 0; i < valid.Length; i++)
                {
                    byte[] corrupted = With(valid, i, new[] { replacement });
                    bool read;
                    TrainingSessionView parsed;
                    try
                    {
                        read = TrainingWireProtocol.TryReadState(corrupted, out parsed);
                    }
                    catch (Exception error)
                    {
                        Assert.Fail($"i={i} b={replacement} 에서 {error.GetType().Name} 가 새어 나왔다");
                        return;
                    }

                    if (!read) continue;

                    TrainingBlessingTrialView trial = parsed.Trial;
                    Assert.That(Enum.IsDefined(typeof(TrainingBlessingTrialPhase), trial.Phase), Is.True);
                    Assert.That(TrainingBlessingTrialRank.IsDefined(trial.RankProfile), Is.True);
                    Assert.That(trial.CandidateIds.Length, Is.LessThanOrEqualTo(TrainingBlessingTrialView.MaxCandidates));
                    Assert.That(trial.Locked.Length, Is.EqualTo(trial.CandidateIds.Length));
                    Assert.That(trial.Cycle, Is.GreaterThanOrEqualTo(0));
                    Assert.That(trial.Round, Is.InRange(0, 3));
                    Assert.That(trial.DepositCost, Is.GreaterThanOrEqualTo(0));
                    Assert.That(trial.DepositAmount, Is.GreaterThanOrEqualTo(0));
                    Assert.That(trial.RerollCost, Is.GreaterThanOrEqualTo(0));
                    Assert.That(trial.RerollCount, Is.GreaterThanOrEqualTo(0));
                    Assert.That(trial.SettlementRefund, Is.GreaterThanOrEqualTo(0));
                }
            }
        }

        [Test]
        public void TrialCommandsSurviveTheRequestWire()
        {
            foreach (TrainingActionKind kind in new[]
                     {
                         TrainingActionKind.TrialBegin, TrainingActionKind.TrialDeposit,
                         TrainingActionKind.TrialReroll,
                         TrainingActionKind.TrialToggleLock, TrainingActionKind.TrialSelect,
                         TrainingActionKind.TrialCombat, TrainingActionKind.TrialSettle,
                     })
            {
                var sent = new TrainingActionRequest("session-one", 5, kind, 1, TrainingBlessingTrialRank.Lower);
                byte[] bytes = TrainingWireProtocol.Serialize(sent);
                Assert.That(TrainingWireProtocol.TryReadRequest(bytes, out var got), Is.True, kind.ToString());
                Assert.That(got.Kind, Is.EqualTo(kind));
                Assert.That(got.Value, Is.EqualTo(1));
                Assert.That(got.Selection, Is.EqualTo(TrainingBlessingTrialRank.Lower));
                Assert.That(TrainingWireProtocol.TryReadRequest(bytes.Concat(new byte[] { 0 }).ToArray(), out _), Is.False);
            }

            byte[] unknown = TrainingWireProtocol.Serialize(
                new TrainingActionRequest("session-one", 1, TrainingActionKind.TrialSettle));
            unknown[unknown.Length - 7] = 99; // Kind 바이트
            Assert.That(TrainingWireProtocol.TryReadRequest(unknown, out _), Is.False);
        }

        private static byte[] With(byte[] source, int offset, byte[] replacement)
        {
            var copy = (byte[])source.Clone();
            Array.Copy(replacement, 0, copy, offset, replacement.Length);
            return copy;
        }

        /// <summary>
        /// 시험 블록의 순위 프로필 문자열이 시작하는 위치. 우연히 같은 바이트가 다른 곳(축복 id 등)에
        /// 있어도 <b>앞에 붙은 길이와 등급 라운드까지 함께 맞는 자리</b>만 고르므로 어긋나지 않는다.
        ///
        /// <para>배치: phase(1) cycle(4) round(4) rankLen(2) rank(N).</para>
        /// </summary>
        private static int FindRankOffset(byte[] bytes, string rank, int round)
        {
            byte[] needle = Encoding.UTF8.GetBytes(rank);

            for (int at = 11; at + needle.Length <= bytes.Length; at++)
            {
                bool hit = true;
                for (int i = 0; i < needle.Length && hit; i++) hit = bytes[at + i] == needle[i];
                if (!hit) continue;
                if (BitConverter.ToUInt16(bytes, at - 2) != needle.Length) continue;
                if (BitConverter.ToInt32(bytes, at - 6) != round) continue;
                return at;
            }

            return -1;
        }
    }
}

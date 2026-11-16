using System;
using System.Linq;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.LocalPlay;
using BettingRoyale.Data.Shop;
using BettingRoyale.Data.Scenes;
using BettingRoyale.Data.Training;
using BettingRoyale.Data.Upgrade;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    public class TrainingPracticeStateTests
    {
        private sealed class Inventory : IEquipmentInventoryDriver, IEquipmentItemCatalog, IShopInventoryProvider
        {
            private readonly EquipmentInventory inner = new();
            public bool Deny;
            public EquipmentInventorySnapshot EquipmentSnapshot => inner.Snapshot;
            public bool TryGet(int id, out EquipmentItemDefinition definition)
            {
                definition = new EquipmentItemDefinition(2103, EquipmentItemKind.Consumable,
                    EquipmentArchetype.None, 5103, WeaponType.None, 3);
                return id == 2103;
            }
            public bool TryResolve(int id, out IEquipmentInventoryDriver inventory) { inventory = this; return id > 0; }
            public EquipmentInventoryOperationResult TryAuthorityPickup(int id, int quantity) => Deny
                ? EquipmentInventoryOperationResult.Rejected(EquipmentInventoryFailure.ConsumableCapacityFull, inner.Snapshot)
                : inner.TryPickup(id, quantity, this);
            public EquipmentInventoryOperationResult TryAuthorityDropWeapon() => inner.TryDropWeapon();
            public EquipmentInventoryOperationResult TryAuthorityUseConsumable(int slot) => inner.TryUseConsumable(slot, this);
            public void ApplyPresentation(in EquipmentInventorySnapshot snapshot) => inner.ApplySnapshot(snapshot);
        }
        private Inventory inventory;
        private TrainingPracticeState state;
        private int sequence;
        [SetUp]
        public void Setup()
        {
            inventory = new Inventory(); sequence = 0;
            state = NewState(1, inventory);
        }
        private static TrainingPracticeState NewState(int playerId, Inventory items) => new("session-one", playerId,
            WeaponType.Sword, PlaceholderShopCatalog.Create(), items, items, LocalPlayBlessingCatalog.All);
        private TrainingActionResult Send(TrainingActionKind kind, int value = 0, string selection = "") =>
            state.Handle(1, new TrainingActionRequest(state.SessionId, ++sequence, kind, value, selection));

        [Test]
        public void PurchaseAndBothUpgradeSlotsDebitOneWallet()
        {
            Assert.That(state.Gold, Is.EqualTo(1000));
            Assert.That(Send(TrainingActionKind.Purchase, 3).Accepted, Is.True);
            Assert.That(Send(TrainingActionKind.Upgrade, (int)UpgradeSlot.Weapon).Accepted, Is.True);
            Assert.That(Send(TrainingActionKind.Upgrade, (int)UpgradeSlot.Armor).Accepted, Is.True);
            Assert.That(state.Gold, Is.EqualTo(650));
            Assert.That(inventory.EquipmentSnapshot.ConsumableSlot0.Count, Is.EqualTo(1));
            Assert.That(state.BuildUpgrade().WeaponLevel, Is.EqualTo(1));
            Assert.That(state.BuildUpgrade().ArmorLevel, Is.EqualTo(1));
        }

        /// <summary>
        /// 제품 강화는 이번 라운드 축복을 확정해야 열린다(S-GD-DP2DVC). <b>훈련은 그 추첨을 돌리지
        /// 않는다</b> — 연습 축복은 <c>SelectBlessing</c> 이 따로 들고 있고 경제의 후보를 확정하지
        /// 않으므로, 제품 판정을 그대로 걸면 모루가 한 번도 안 열린다.
        /// </summary>
        [Test]
        public void UpgradeStaysOpenWithoutProductBlessingSelection()
        {
            Assert.That(Send(TrainingActionKind.Upgrade, (int)UpgradeSlot.Weapon).Accepted, Is.True);
            Assert.That(state.BuildUpgrade().WeaponBlockedBy,
                Is.Not.EqualTo(UpgradeRejectReason.SelectionRequired));
        }

        [Test]
        public void FailedInventoryGrantNeverDebitsGold()
        {
            inventory.Deny = true;
            Assert.That(Send(TrainingActionKind.Purchase, 3).Accepted, Is.False);
            Assert.That(state.Gold, Is.EqualTo(1000));
        }
        [Test]
        public void MissingItemDefinitionIsUnavailableAndDoesNotCharge()
        {
            Assert.That(state.BuildOffers().Offers.Single(x => x.OfferId == 1).CanAfford, Is.False);
            Assert.That(Send(TrainingActionKind.Purchase, 1).Accepted, Is.False);
            Assert.That(state.Gold, Is.EqualTo(1000));
        }
        [Test]
        public void DuplicatePurchaseCannotGrantOrChargeTwice()
        {
            var request = new TrainingActionRequest(state.SessionId, 1, TrainingActionKind.Purchase, 3);
            Assert.That(state.Handle(1, request).Accepted, Is.True);
            Assert.That(state.Handle(1, request).Duplicate, Is.True);
            Assert.That(state.Gold, Is.EqualTo(800));
            Assert.That(inventory.EquipmentSnapshot.ConsumableSlot0.Count, Is.EqualTo(1));
        }
        [Test]
        public void SequenceIsSharedAcrossAllCommandKinds()
        {
            Assert.That(Send(TrainingActionKind.GrantGold).Accepted, Is.True);
            Assert.That(state.Handle(1, new TrainingActionRequest(state.SessionId, 1, TrainingActionKind.Purchase, 3)).Duplicate, Is.True);
            Assert.That(state.Gold, Is.EqualTo(2000));
            Assert.That(inventory.EquipmentSnapshot.ConsumableSlot0.IsEmpty, Is.True);
        }
        [Test]
        public void PersonalResetKeepsRequestLedgerAndResetsOnlyOwnProgress()
        {
            var other = NewState(2, new Inventory());
            other.Handle(2, new TrainingActionRequest(other.SessionId, 1, TrainingActionKind.GrantGold));
            Send(TrainingActionKind.Upgrade, (int)UpgradeSlot.Weapon);
            Send(TrainingActionKind.SelectBlessing, selection: "steel-heart-t1");
            Assert.That(Send(TrainingActionKind.Reset).Accepted, Is.True);
            Assert.That(state.Gold, Is.EqualTo(1000));
            Assert.That(state.BuildUpgrade().WeaponLevel, Is.Zero);
            Assert.That(state.Acquired, Is.Empty);
            Assert.That(other.Gold, Is.EqualTo(2000));
            Assert.That(state.Handle(1, new TrainingActionRequest(state.SessionId, 1, TrainingActionKind.Purchase, 3)).Accepted, Is.False);
            Assert.That(inventory.EquipmentSnapshot.ConsumableSlot0.IsEmpty, Is.True);
        }
        [Test]
        public void LateJoinDoesNotResetExistingParticipant()
        {
            Send(TrainingActionKind.GrantGold);
            Send(TrainingActionKind.Upgrade, (int)UpgradeSlot.Armor);
            var late = NewState(2, new Inventory());
            Assert.That(late.Gold, Is.EqualTo(1000));
            Assert.That(state.Gold, Is.EqualTo(1950));
            Assert.That(state.BuildUpgrade().ArmorLevel, Is.EqualTo(1));
        }
        [TestCase(2, "session-one")]
        [TestCase(1, "previous-session")]
        public void SenderAndSessionMustBothMatch(int sender, string session)
        {
            Assert.That(state.Handle(sender, new TrainingActionRequest(session, 1, TrainingActionKind.GrantGold)).Accepted, Is.False);
            Assert.That(state.Gold, Is.EqualTo(1000));
            Assert.That(Send(TrainingActionKind.GrantGold).Accepted, Is.True, "다른 송신자의 요청은 실제 참가자의 순번을 소비하지 않는다");
        }
        [Test]
        public void DirectBlessingSelectionFiltersWeaponAndDuplicateIdWithoutCost()
        {
            Assert.That(Send(TrainingActionKind.SelectBlessing, selection: "precision-aim-t1").Accepted, Is.False);
            Assert.That(Send(TrainingActionKind.SelectBlessing, selection: "steel-heart-t1").Accepted, Is.True);
            Assert.That(Send(TrainingActionKind.SelectBlessing, selection: "steel-heart-t1").Accepted, Is.False);
            Assert.That(state.Acquired.Count, Is.EqualTo(1));
            Assert.That(state.Gold, Is.EqualTo(1000));
        }
        [Test]
        public void SessionCloseRejectsNewCommands()
        {
            state.Close();
            Assert.That(Send(TrainingActionKind.GrantGold).Accepted, Is.False);
            Assert.That(state.Gold, Is.EqualTo(1000));
        }
        [TestCase(TrainingActionKind.Travel, -1)]
        [TestCase(TrainingActionKind.Travel, 5)]
        [TestCase(TrainingActionKind.SetPlayerDamage, 2)]
        [TestCase(TrainingActionKind.Upgrade, 8)]
        [TestCase(TrainingActionKind.SelectWeapon, (int)WeaponType.None)]
        [TestCase(TrainingActionKind.SelectWeapon, 999)]
        public void UnknownSelectionsAreRejected(TrainingActionKind kind, int value) => Assert.That(Send(kind, value).Accepted, Is.False);
        [Test]
        public void WeaponChoiceUsesSameLedgerWithoutChargingOrResettingProgress()
        {
            Send(TrainingActionKind.Upgrade, (int)UpgradeSlot.Weapon);
            Send(TrainingActionKind.SelectBlessing, selection: "steel-heart-t1");
            var request = new TrainingActionRequest(state.SessionId, ++sequence, TrainingActionKind.SelectWeapon, (int)WeaponType.Bow);
            Assert.That(state.Handle(1, request).Accepted, Is.True);
            Assert.That(state.Handle(1, request).Duplicate, Is.True);
            Assert.That(state.Gold, Is.EqualTo(900));
            Assert.That(state.BuildUpgrade().WeaponLevel, Is.EqualTo(1));
            Assert.That(state.Acquired.Count, Is.EqualTo(1));
        }
        [Test]
        public void ClientCannotChooseGoldGrantAmount()
        {
            Assert.That(Send(TrainingActionKind.GrantGold, int.MaxValue).Accepted, Is.True);
            Assert.That(state.Gold, Is.EqualTo(2000));
        }
        [Test]
        public void WireRoundTripRetainsAuthoritativeStateAndRejectReason()
        {
            Send(TrainingActionKind.Upgrade, (int)UpgradeSlot.Armor);
            var view = TrainingSessionView.Capture(state, 4, sequence, true, "거절 이유", true);
            Assert.That(TrainingWireProtocol.TryReadState(TrainingWireProtocol.Serialize(view), out var received), Is.True);
            Assert.That(received.SessionId, Is.EqualTo(state.SessionId));
            Assert.That(received.Gold, Is.EqualTo(950));
            Assert.That(received.ArmorLevel, Is.EqualTo(1));
            Assert.That(received.RequestRejected, Is.True);
            Assert.That(received.Offers.Length, Is.EqualTo(6));
        }
        [Test]
        public void WireRejectsEveryTruncatedRequestAndTrailingBytes()
        {
            byte[] valid = TrainingWireProtocol.Serialize(new TrainingActionRequest(state.SessionId, 1, TrainingActionKind.SelectBlessing, 0, "steel-heart-t1"));
            for (int length = 0; length < valid.Length; length++)
                Assert.That(TrainingWireProtocol.TryReadRequest(valid.AsSpan(0, length), out _), Is.False, "length=" + length);
            Assert.That(TrainingWireProtocol.TryReadRequest(valid.Concat(new byte[] { 0 }).ToArray(), out _), Is.False);
            Assert.That(TrainingWireProtocol.TryReadRequest(valid, out var request), Is.True);
            Assert.That(request.Selection, Is.EqualTo("steel-heart-t1"));
        }
        /// <summary>
        /// 봇 구역 요청이 <b>개인 상태 관문</b>을 통과하는지 본다 (S-GD-V9ZMN3).
        /// </summary>
        /// <remarks>
        /// 이 관문은 모르는 명령을 전부 거절한다. Host 쪽에만 처리를 붙이면 요청이 여기서 먼저
        /// 막혀 <b>칸을 눌러도 아무 일도 일어나지 않는다</b> — 실제로 그렇게 만들었다가 잡았다.
        /// </remarks>
        [Test]
        public void BotZoneRequestPassesPersonalGateInsideRangeOnly()
        {
            for (int zones = TrainingBotRule.NoZones; zones <= TrainingBotRule.AllZones; zones++)
                Assert.That(Send(TrainingActionKind.SetBotZones, zones).Accepted, Is.True, "조합 " + zones);
            Assert.That(Send(TrainingActionKind.SetBotZones, TrainingBotRule.AllZones + 1).Accepted, Is.False);
            Assert.That(Send(TrainingActionKind.SetBotZones, -1).Accepted, Is.False);
        }

        /// <summary>봇은 <b>개수가 아니라 자리</b>라, 한 구역만 켜는 조합이 실제로 가능해야 한다.</summary>
        [Test]
        public void SingleZoneCanBeEnabledWithoutDraggingTheOthersAlong()
        {
            int coverOnly = TrainingBotRule.With(TrainingBotRule.NoZones, 4, true);
            Assert.That(TrainingBotRule.Contains(coverOnly, 4), Is.True);
            for (int number = 1; number <= 3; number++)
                Assert.That(TrainingBotRule.Contains(coverOnly, number), Is.False, "봇 " + number);
            Assert.That(TrainingBotRule.Count(coverOnly), Is.EqualTo(1));

            // 끄기도 같은 방법이다 — 켜기 전용 경로를 따로 두면 두 경로가 갈린다.
            Assert.That(TrainingBotRule.With(coverOnly, 4, false), Is.EqualTo(TrainingBotRule.NoZones));
        }

        /// <summary>번호 ↔ 구역 ↔ 무기가 한 곳에서만 정해진다 (S-GD-V9ZMN3 · S-GD-YAEB2H).</summary>
        [Test]
        public void OnlyTheRangeBotCarriesABow()
        {
            Assert.That(TrainingBotRule.ZoneOf(1), Is.EqualTo(TrainingZoneId.Arena));
            Assert.That(TrainingBotRule.ZoneOf(2), Is.EqualTo(TrainingZoneId.Range));
            Assert.That(TrainingBotRule.ZoneOf(3), Is.EqualTo(TrainingZoneId.Heights));
            Assert.That(TrainingBotRule.ZoneOf(4), Is.EqualTo(TrainingZoneId.Cover));

            Assert.That(TrainingBotRule.WeaponOf(2), Is.EqualTo(WeaponType.Bow));
            for (int number = 1; number <= TrainingBotRule.MaxBotNumber; number++)
                if (number != 2)
                    Assert.That(TrainingBotRule.WeaponOf(number), Is.EqualTo(WeaponType.Sword), "봇 " + number);

            // 설명은 구역·무기를 모두 말해야 한다. 번호만으로는 둘 다 알 수 없다.
            Assert.That(TrainingBotRule.Describe(2), Does.Contain("사격장").And.Contain("활"));
            Assert.That(TrainingBotRule.Describe(4), Does.Contain("엄폐").And.Contain("검"));
        }

        /// <summary>봇 구역은 방 전체 설정이라 <b>개인 스냅샷에 실려</b> 모두에게 같은 값으로 간다.</summary>
        [Test]
        public void WireRoundTripCarriesRoomBotZones()
        {
            int rangeAndCover = TrainingBotRule.With(TrainingBotRule.With(TrainingBotRule.NoZones, 2, true), 4, true);
            var view = TrainingSessionView.Capture(state, 2, sequence, false, "", false, rangeAndCover);
            Assert.That(TrainingWireProtocol.TryReadState(TrainingWireProtocol.Serialize(view), out var received), Is.True);
            Assert.That(received.BotZones, Is.EqualTo(rangeAndCover));

            // 범위 밖 자리는 전송 이전에 떨어진다. 잘린 값이 그대로 실려야 받는 쪽이 거절하지 않는다.
            var clamped = TrainingSessionView.Capture(state, 3, sequence, false, "", false, TrainingBotRule.AllZones | 0b1_0000);
            Assert.That(clamped.BotZones, Is.EqualTo(TrainingBotRule.AllZones));
            Assert.That(TrainingWireProtocol.TryReadState(TrainingWireProtocol.Serialize(clamped), out var trimmed), Is.True);
            Assert.That(trimmed.BotZones, Is.EqualTo(TrainingBotRule.AllZones));
        }

        /// <summary>아무도 조작하지 않은 방은 이 기능이 생기기 전과 같이 <b>네 구역 전부</b>다.</summary>
        [Test]
        public void DefaultBotZonesMatchPreFeatureTrainingRoom()
        {
            Assert.That(TrainingBotRule.Default, Is.EqualTo(TrainingBotRule.AllZones));
            Assert.That(TrainingBotRule.Count(TrainingBotRule.Default), Is.EqualTo(TrainingBotRule.ZoneCount));
            Assert.That(TrainingSessionView.Capture(state, 1, 0, false, "").BotZones,
                Is.EqualTo(TrainingBotRule.Default));
        }

        [Test]
        public void WireRejectsTruncatedAndOversizedState()
        {
            byte[] valid = TrainingWireProtocol.Serialize(TrainingSessionView.Capture(state, 1, 0, false, ""));
            for (int length = 0; length < valid.Length; length++)
                Assert.That(TrainingWireProtocol.TryReadState(valid.AsSpan(0, length), out _), Is.False, "length=" + length);
            Assert.That(TrainingWireProtocol.TryReadState(new byte[TrainingWireProtocol.MaxBytes + 1], out _), Is.False);
        }
    }
}

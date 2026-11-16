using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.Preparation;
using BettingRoyale.Data.Shop;
using BettingRoyale.Data.Upgrade;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 상점 구매·교체 (Jira S15P21A606-84, S-GD-PVVHP9 · S-CLI-GRZGBK).
    ///
    /// <para>
    /// <b>이 파일이 지키는 것은 하나다 — 골드와 물건이 따로 놀지 않는다.</b>
    /// 차감과 지급은 각각 원자적이지만 <b>둘을 합친 것은 아니라서</b>, 순서를 잘못 짜면
    /// "골드만 사라짐" 또는 "물건만 생김" 이 생긴다. 화면에는 정상으로 보이고 컴파일러도 못 잡는다.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ShopPurchaseTests
    {
        private const int Alice = 1;
        private const int Bob = 2;
        private const int Stranger = 99;

        // 소모품 (MaxStack 3)
        private const int Potion = 2101;
        private const int Swift = 2102;
        private const int Bomb = 2103;
        private const int Antidote = 2104;

        // 장신구
        private const int Ring = 3101;
        private const int Necklace = 3102;
        private const int Amulet = 3103;

        /// <summary>무기. 상점이 팔면 안 되는 종류다 (S-GD-PVVHP9 규칙 2).</summary>
        private const int SwordItem = 1101;

        /// <summary>진열에는 있는데 장비 정의가 없는 아이템. 두 카탈로그가 어긋난 상황이다.</summary>
        private const int Ghost = 9999;

        private MatchEconomyState economy;
        private EconomyRequestLedger ledger;
        private FakeItemCatalog items;
        private FakeInventoryProvider inventories;
        private FakeInventory aliceInventory;
        private FakePresenceProvider presence;
        private ShopRequestGate gate;
        private int sequence;

        // ── 준비 ────────────────────────────────────────────────────────

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

        /// <summary>
        /// 검증용 진열대. <b>임시 목록(<see cref="PlaceholderShopCatalog"/>)을 쓰지 않는다</b> —
        /// 그 값은 언제든 바뀌는 임의값이라, 그걸 쓰면 기획이 바뀔 때마다 테스트가 깨진다.
        /// </summary>
        private static ShopCatalog Catalog() => new ShopCatalog(new[]
        {
            new ShopOffer(offerId: 1, itemId: Potion, price: 100),
            new ShopOffer(offerId: 2, itemId: Bomb, price: 50, quantity: 2),
            new ShopOffer(offerId: 3, itemId: Ring, price: 300),
            new ShopOffer(offerId: 4, itemId: Necklace, price: 300),
            new ShopOffer(offerId: 5, itemId: Ghost, price: 10),
            // 살 수 없을 만큼 비싼 장신구. 3·4번과 다른 것이어야 "이미 갖고 있음" 이 아니라
            // "돈이 모자람" 이 검증된다.
            new ShopOffer(offerId: 6, itemId: Amulet, price: 999_999),
            new ShopOffer(offerId: 7, itemId: Swift, price: 100),
            new ShopOffer(offerId: 8, itemId: Antidote, price: 100),
            // 팔면 안 되는 두 칸 — 가격표가 데이터로 들어오므로 실제로 섞일 수 있다 (D-041).
            new ShopOffer(offerId: 9, itemId: SwordItem, price: 100),
            new ShopOffer(offerId: 10, itemId: Ring, price: 100, quantity: 2),
        });

        [SetUp]
        public void SetUp()
        {
            items = new FakeItemCatalog();
            items.AddConsumable(Potion, visualId: 11);
            items.AddConsumable(Swift, visualId: 12);
            items.AddConsumable(Bomb, visualId: 13);
            items.AddConsumable(Antidote, visualId: 14);
            items.AddAccessory(Ring, visualId: 21);
            items.AddAccessory(Necklace, visualId: 22);
            items.AddAccessory(Amulet, visualId: 23);
            items.AddWeapon(SwordItem, visualId: 31);

            aliceInventory = new FakeInventory(items);
            inventories = new FakeInventoryProvider();
            inventories.Set(Alice, aliceInventory);
            inventories.Set(Bob, new FakeInventory(items));

            economy = new MatchEconomyState(EconomyConfig.Default, BlessingConfig.Default, Pool());
            economy.BeginMatch(
                new[]
                {
                    new MatchParticipant(Alice, WeaponTag.Sword),
                    new MatchParticipant(Bob, WeaponTag.Bow),
                },
                seed: 4242);

            ledger = new EconomyRequestLedger();
            economy.BeginPreparation();   // 라운드 기본 지급 1,000G
            ledger.OpenPreparation();

            // 둘 다 이번 라운드의 축복을 골라 뒀다 — 제품의 정상 상태다.
            // 선택 전에는 상점이 통째로 잠기고(S-GD-DP2DVC), 그 상태는 아래
            // "축복 선택" 절의 테스트가 따로 본다. 자동 선택은 골드를 건드리지 않는다.
            economy.TrySelectAutomatically(Alice);
            economy.TrySelectAutomatically(Bob);

            // 기본값은 "둘 다 상점 앞에 서 있다" — 구역 밖 동작은 그 테스트에서 옮긴다.
            presence = new FakePresenceProvider();
            presence.Place(Alice, ShopCenter);
            presence.Place(Bob, ShopCenter);

            gate = new ShopRequestGate(
                economy,
                ledger,
                Catalog(),
                inventories,
                items,
                new PreparationAccessGate(PlaceholderPreparationFacilityMap.Create(), presence));
            sequence = 0;
        }

        /// <summary>준비맵 상점 위치. 배치는 S-CLI-EYCKKC 가 정한 실제 좌표다.</summary>
        private static readonly WorldPosition ShopCenter = new WorldPosition(11f, 0f, 0f);

        private EconomyRequestTicket NextTicket() => new EconomyRequestTicket(ledger.Epoch, ++sequence);

        private ShopPurchaseOutcome Buy(int offerId, int playerId = Alice) =>
            gate.HandlePurchase(playerId, offerId, NextTicket(), isMine: true);

        // ── 정상 구매 ───────────────────────────────────────────────────

        [Test]
        public void 소모품을_사면_골드가_빠지고_물건이_들어온다()
        {
            var r = Buy(offerId: 1);

            Assert.That(r.HasRejection, Is.False, r.HasRejection ? r.Rejection.Reason.ToString() : string.Empty);
            Assert.That(economy.GetGold(Alice), Is.EqualTo(900));
            Assert.That(r.Gold.Delta, Is.EqualTo(-100));
            Assert.That(r.Gold.Gold, Is.EqualTo(900), "잔액은 확정값이 실린다 — 화면이 델타로 계산하지 않는다");

            Assert.That(r.HasInventoryChange, Is.True);
            Assert.That(r.Inventory.Snapshot.GetConsumableSlot(0).ItemId, Is.EqualTo(Potion));
            Assert.That(r.Inventory.Snapshot.GetConsumableSlot(0).Count, Is.EqualTo(1));
        }

        [Test]
        public void 묶음_상품은_진열이_정한_수량만큼_들어온다()
        {
            var r = Buy(offerId: 2);   // 폭탄 ×2, 50G

            Assert.That(economy.GetGold(Alice), Is.EqualTo(950));
            Assert.That(r.Inventory.Snapshot.GetConsumableSlot(0).ItemId, Is.EqualTo(Bomb));
            Assert.That(r.Inventory.Snapshot.GetConsumableSlot(0).Count, Is.EqualTo(2),
                "수량은 화면이 아니라 진열이 정한다");
        }

        [Test]
        public void 장신구를_사면_갖고_있던_장신구가_사라진다()
        {
            Buy(offerId: 3);   // 반지
            Assert.That(aliceInventory.EquipmentSnapshot.AccessoryItemId, Is.EqualTo(Ring));

            var r = Buy(offerId: 4);   // 목걸이로 교체

            Assert.That(r.HasRejection, Is.False);
            Assert.That(aliceInventory.EquipmentSnapshot.AccessoryItemId, Is.EqualTo(Necklace));
            Assert.That(r.Inventory.Snapshot.AccessoryItemId, Is.EqualTo(Necklace));
            Assert.That(economy.GetGold(Alice), Is.EqualTo(400));
        }

        // ── 원자성: 한쪽만 일어나면 안 된다 ─────────────────────────────

        [Test]
        public void 잔액이_모자라면_골드도_물건도_움직이지_않는다()
        {
            var r = Buy(offerId: 6);   // 999,999G

            Assert.That(r.HasRejection, Is.True);
            Assert.That(r.Rejection.Reason, Is.EqualTo(ShopPurchaseRejectReason.NotEnoughGold));
            Assert.That(economy.GetGold(Alice), Is.EqualTo(1_000));
            Assert.That(r.HasInventoryChange, Is.False);
            Assert.That(aliceInventory.EquipmentSnapshot.AccessoryItemId, Is.EqualTo(0));
        }

        [Test]
        public void 살_돈이_없으면_갖고_있던_장신구가_불타지_않는다()
        {
            Buy(offerId: 3);   // 반지 장착 (300G)
            Assert.That(aliceInventory.EquipmentSnapshot.AccessoryItemId, Is.EqualTo(Ring));

            var r = Buy(offerId: 6);   // 같은 반지를 999,999G 에 사려다 실패

            Assert.That(r.Rejection.Reason, Is.EqualTo(ShopPurchaseRejectReason.NotEnoughGold));
            Assert.That(aliceInventory.EquipmentSnapshot.AccessoryItemId, Is.EqualTo(Ring),
                "잔액을 지급보다 먼저 확인하지 않으면, 못 살 물건 때문에 갖고 있던 장신구가 소멸한다");
        }

        [Test]
        public void 소모품_자리가_없으면_골드가_빠지지_않는다()
        {
            Buy(offerId: 1);   // 물약   → 칸 0   (-100)
            Buy(offerId: 7);   // 신속   → 칸 1   (-100)
            Buy(offerId: 2);   // 폭탄×2 → 칸 2   (-50)

            int before = economy.GetGold(Alice);
            Assert.That(before, Is.EqualTo(750));

            var r = Buy(offerId: 8);   // 해독제 — 넣을 칸이 없다

            Assert.That(r.HasRejection, Is.True);
            Assert.That(r.Rejection.Reason, Is.EqualTo(ShopPurchaseRejectReason.InventoryFull));
            Assert.That(economy.GetGold(Alice), Is.EqualTo(before),
                "차감을 지급보다 먼저 하면 여기서 골드만 사라진다");
            Assert.That(r.HasInventoryChange, Is.False);
        }

        [Test]
        public void 전투자가_다른_동작_중이면_잠시_뒤_다시_누르라고_알린다()
        {
            aliceInventory.ForceFailure(EquipmentInventoryFailure.Dashing);

            var r = Buy(offerId: 1);

            Assert.That(r.Rejection.Reason, Is.EqualTo(ShopPurchaseRejectReason.Busy),
                "'자리가 없다' 로 알리면 사용자가 칸을 비우려 하는데 그건 원인이 아니다");
            Assert.That(economy.GetGold(Alice), Is.EqualTo(1_000));
        }

        /// <summary>
        /// <b>보유 한도 초과는 "가방이 찼다" 가 아니다</b> (S-GD-FDHBNA, Jira S15P21A606-140).
        ///
        /// <para>
        /// 소모품은 한 칸에 쌓이므로 <b>한도에 닿아도 칸은 비어 있을 수 있다</b> —
        /// 마력 폭발병은 한 칸에 3개까지 쌓이고 소모품 칸은 3개다. 3개를 들고 4번째를 사면
        /// 칸이 두 개나 비어 있는데도 <c>InventoryFull</c> 로 답하면 화면에
        /// *"가방이 가득 찼습니다"* 라는 <b>거짓 안내</b>가 뜬다.
        /// </para>
        ///
        /// <para>
        /// 이 값이 생기기 전까지 <c>Translate</c> 의 <c>default</c> 가지가 그렇게 답했다 —
        /// 그 가지 주석은 <c>CarryLimitExceeded</c> 가 추가되기 전에 쓰였고,
        /// "여기 떨어질 값은 나올 수 없다" 고 적혀 있었다.
        /// </para>
        /// </summary>
        [Test]
        public void 보유_한도를_넘으면_가방이_찼다고_하지_않는다()
        {
            aliceInventory.ForceFailure(EquipmentInventoryFailure.CarryLimitExceeded);

            var r = Buy(offerId: 1);

            Assert.That(
                r.Rejection.Reason, Is.EqualTo(ShopPurchaseRejectReason.CarryLimitReached),
                "보유 한도 초과를 InventoryFull 로 알리면 사용자가 칸을 비우러 간다 — 원인이 아니다");
            Assert.That(economy.GetGold(Alice), Is.EqualTo(1_000), "거절인데 골드가 나갔다");
            Assert.That(r.HasInventoryChange, Is.False);
        }

        // ── 재전송 ──────────────────────────────────────────────────────

        [Test]
        public void 같은_요청을_두_번_보내도_골드는_한_번만_빠진다()
        {
            var ticket = NextTicket();

            gate.HandlePurchase(Alice, offerId: 1, ticket, isMine: true);
            var again = gate.HandlePurchase(Alice, offerId: 1, ticket, isMine: true);

            Assert.That(economy.GetGold(Alice), Is.EqualTo(900));
            Assert.That(again.Verdict, Is.EqualTo(EconomyRequestVerdict.Duplicate));
            Assert.That(again.HasInventoryChange, Is.False, "물건도 한 번만 들어온다");
            Assert.That(aliceInventory.EquipmentSnapshot.GetConsumableSlot(0).Count, Is.EqualTo(1));
        }

        [Test]
        public void 재전송에는_거절이_오지_않는다()
        {
            var ticket = NextTicket();

            gate.HandlePurchase(Alice, offerId: 1, ticket, isMine: true);
            var again = gate.HandlePurchase(Alice, offerId: 1, ticket, isMine: true);

            Assert.That(again.HasRejection, Is.False,
                "사용자의 클릭은 이미 한 번 성공했다 — 거절을 띄우면 거짓말이다");
            Assert.That(again.HasEvents, Is.True, "그래도 현재 상태는 실어 보낸다");
            Assert.That(again.Gold.Gold, Is.EqualTo(900));
            Assert.That(again.Gold.Delta, Is.EqualTo(0));
        }

        // ── 단계·참가자 ─────────────────────────────────────────────────

        [Test]
        public void 전투_중에_누르면_왜_안_되는지_알려준다()
        {
            economy.EndPreparation();

            var r = Buy(offerId: 1);

            Assert.That(r.Verdict, Is.EqualTo(EconomyRequestVerdict.PreparationClosed));
            Assert.That(r.HasRejection, Is.True,
                "조용히 넘기면 '눌러도 반응 없는 버튼' 이 된다 (규약 §6)");
            Assert.That(r.Rejection.Reason, Is.EqualTo(ShopPurchaseRejectReason.NotPreparation));
            Assert.That(economy.GetGold(Alice), Is.EqualTo(1_000));
            Assert.That(r.HasInventoryChange, Is.False);
        }

        [Test]
        public void 전투_중_재전송에는_거절이_오지_않는다()
        {
            var ticket = NextTicket();
            economy.EndPreparation();

            gate.HandlePurchase(Alice, offerId: 1, ticket, isMine: true);
            var again = gate.HandlePurchase(Alice, offerId: 1, ticket, isMine: true);

            Assert.That(again.Verdict, Is.EqualTo(EconomyRequestVerdict.Duplicate));
            Assert.That(again.HasRejection, Is.False,
                "단계가 닫힌 것은 한 번만 알린다 — 재전송마다 알리면 같은 안내가 반복된다");
        }

        [Test]
        public void 지난_준비_단계의_요청은_조용히_무시된다()
        {
            var stale = NextTicket();
            ledger.OpenPreparation();   // 다음 준비 단계가 열렸다

            var r = gate.HandlePurchase(Alice, offerId: 1, stale, isMine: true);

            Assert.That(r.Verdict, Is.EqualTo(EconomyRequestVerdict.StaleEpoch));
            Assert.That(r.HasRejection, Is.False);
            Assert.That(economy.GetGold(Alice), Is.EqualTo(1_000));
        }

        [Test]
        public void 참가자가_아니면_아무_이벤트도_만들지_않는다()
        {
            var r = gate.HandlePurchase(Stranger, offerId: 1, NextTicket(), isMine: false);

            Assert.That(r.HasEvents, Is.False,
                "방에 없는 사람의 요청에 이벤트를 만들면 그 값이 화면에 흘러간다");
        }

        // ── 데이터가 어긋난 경우 ────────────────────────────────────────

        [Test]
        public void 없는_진열_칸을_요청하면_거절한다()
        {
            var r = Buy(offerId: 404);

            Assert.That(r.Rejection.Reason, Is.EqualTo(ShopPurchaseRejectReason.NoSuchOffer));
            Assert.That(r.Rejection.OfferId, Is.EqualTo(404), "화면이 그 칸 위에 사유를 띄우려면 번호가 필요하다");
        }

        [Test]
        public void 가격표에만_있고_아이템_정의가_없으면_살_수_없다()
        {
            var r = Buy(offerId: 5);   // Ghost

            Assert.That(r.Rejection.Reason, Is.EqualTo(ShopPurchaseRejectReason.UnknownItem));
            Assert.That(economy.GetGold(Alice), Is.EqualTo(1_000));
        }

        [Test]
        public void 가격표에_무기가_섞여_있어도_팔지_않는다()
        {
            Buy(offerId: 1);   // 인벤토리를 한 번 건드려 상태를 만든다
            var before = aliceInventory.EquipmentSnapshot.WeaponItemId;

            var r = Buy(offerId: 9);   // 검 100G

            Assert.That(r.HasRejection, Is.True);
            Assert.That(r.Rejection.Reason, Is.EqualTo(ShopPurchaseRejectReason.UnknownItem));
            Assert.That(aliceInventory.EquipmentSnapshot.WeaponItemId, Is.EqualTo(before),
                "무기를 팔면 들고 있던 무기가 교체돼 사라진다 — 매치 중 무기는 바꿀 수 없다 (S-GD-PVVHP9 규칙 2)");
            Assert.That(economy.GetGold(Alice), Is.EqualTo(900));
        }

        [Test]
        public void 무기_칸은_목록에서도_살_수_없게_보인다()
        {
            var weapon = FindOffer(gate.BuildOffers(Alice, true), 9);

            Assert.That(weapon.CanAfford, Is.False, "100G 는 낼 수 있지만 팔지 않는 종류다");
            Assert.That(weapon.Kind, Is.EqualTo(EquipmentItemKind.None),
                "화면이 무기 진열대를 그리지 않도록 종류를 감춘다");
        }

        [Test]
        public void 소모품이_아닌데_묶음으로_파는_칸은_살_수_없다()
        {
            var r = Buy(offerId: 10);   // 반지 ×2

            Assert.That(r.Rejection.Reason, Is.EqualTo(ShopPurchaseRejectReason.UnknownItem));
            Assert.That(FindOffer(gate.BuildOffers(Alice, true), 10).CanAfford, Is.False,
                "목록에는 보이는데 누르면 항상 실패하는 칸을 만들지 않는다");
        }

        [Test]
        public void 이미_끼고_있는_장신구를_또_사면_거절한다()
        {
            Buy(offerId: 3);   // 반지 300G
            int after = economy.GetGold(Alice);

            var r = Buy(offerId: 3);   // 같은 반지를 또

            Assert.That(r.HasRejection, Is.True);
            Assert.That(r.Rejection.Reason, Is.EqualTo(ShopPurchaseRejectReason.AlreadyOwned));
            Assert.That(economy.GetGold(Alice), Is.EqualTo(after),
                "장신구는 교체가 곧 소멸이라 같은 것으로 바꾸면 아무것도 안 바뀌는데 골드만 나간다");
            Assert.That(FindOffer(gate.BuildOffers(Alice, true), 3).CanAfford, Is.False);
        }

        [Test]
        public void 다른_장신구로_바꾸는_것은_된다()
        {
            Buy(offerId: 3);   // 반지
            var r = Buy(offerId: 4);   // 목걸이

            Assert.That(r.HasRejection, Is.False, "같은 것만 막는다 — 교체 자체는 정상이다");
            Assert.That(aliceInventory.EquipmentSnapshot.AccessoryItemId, Is.EqualTo(Necklace));
        }

        [Test]
        public void 인벤토리를_찾을_수_없으면_골드가_빠지지_않는다()
        {
            inventories.Remove(Alice);

            var r = Buy(offerId: 1);

            Assert.That(r.Rejection.Reason, Is.EqualTo(ShopPurchaseRejectReason.InventoryUnavailable));
            Assert.That(economy.GetGold(Alice), Is.EqualTo(1_000));
        }

        // ── 진열 목록 ───────────────────────────────────────────────────

        [Test]
        public void 진열_목록은_전체를_싣고_살_수_있는지를_함께_알린다()
        {
            var view = gate.BuildOffers(Alice, isMine: true);

            Assert.That(view.Offers.Length, Is.EqualTo(10), "바뀐 칸만이 아니라 전체를 싣는다");

            var potion = FindOffer(view, 1);
            Assert.That(potion.Kind, Is.EqualTo(EquipmentItemKind.Consumable));
            Assert.That(potion.Price, Is.EqualTo(100));
            Assert.That(potion.CanAfford, Is.True);

            Assert.That(FindOffer(view, 6).CanAfford, Is.False, "999,999G 는 살 수 없다");
            Assert.That(FindOffer(view, 5).CanAfford, Is.False, "정의가 없는 아이템은 목록에서도 살 수 없다");
            Assert.That(FindOffer(view, 5).Kind, Is.EqualTo(EquipmentItemKind.None));
        }

        [Test]
        public void 살_수_있는지는_잔액이_줄면_함께_바뀐다()
        {
            Assert.That(FindOffer(gate.BuildOffers(Alice, true), 3).CanAfford, Is.True);

            Buy(offerId: 3);   // 300G
            Buy(offerId: 4);   // 300G  → 잔액 400
            Buy(offerId: 1);   // 100G  → 잔액 300

            Assert.That(economy.GetGold(Alice), Is.EqualTo(300));
            Assert.That(FindOffer(gate.BuildOffers(Alice, true), 3).CanAfford, Is.True, "300G 는 딱 살 수 있다");

            Buy(offerId: 2);   // 50G   → 잔액 250
            Assert.That(FindOffer(gate.BuildOffers(Alice, true), 3).CanAfford, Is.False);
        }

        [Test]
        public void 전투_중에는_모든_칸이_살_수_없음으로_표시된다()
        {
            economy.EndPreparation();

            var view = gate.BuildOffers(Alice, isMine: true);

            foreach (var offer in view.Offers)
            {
                Assert.That(offer.CanAfford, Is.False, $"칸 {offer.OfferId}");
            }
        }

        [Test]
        public void 방에_없는_사람의_목록을_만들어도_터지지_않는다()
        {
            var view = gate.BuildOffers(Stranger, isMine: false);

            Assert.That(view.Offers.Length, Is.EqualTo(10));

            foreach (var offer in view.Offers)
            {
                Assert.That(offer.CanAfford, Is.False, $"칸 {offer.OfferId}");
            }
        }

        [Test]
        public void 잔액이_가격과_정확히_같으면_살_수_있다()
        {
            Buy(offerId: 3);   // 반지   300G → 700
            Buy(offerId: 4);   // 목걸이 300G → 400
            Buy(offerId: 3);   // 반지   300G → 100   (직전이 목걸이라 같은 것 재구매가 아니다)

            Assert.That(economy.GetGold(Alice), Is.EqualTo(100));
            Assert.That(FindOffer(gate.BuildOffers(Alice, true), 1).CanAfford, Is.True,
                "잔액 100 · 가격 100 은 살 수 있어야 한다");

            var r = Buy(offerId: 1);   // 물약 100G

            Assert.That(r.HasRejection, Is.False, "'모자란다' 의 경계는 미만이지 이하가 아니다");
            Assert.That(economy.GetGold(Alice), Is.EqualTo(0));
            Assert.That(FindOffer(gate.BuildOffers(Alice, true), 1).CanAfford, Is.False, "이제는 못 산다");
        }

        // ── 여러 사람 ───────────────────────────────────────────────────

        [Test]
        public void 요청_순번은_사람마다_따로_센다()
        {
            var ticket = new EconomyRequestTicket(ledger.Epoch, 1);

            var alice = gate.HandlePurchase(Alice, offerId: 1, ticket, isMine: true);
            var bob = gate.HandlePurchase(Bob, offerId: 1, ticket, isMine: false);

            Assert.That(alice.HasInventoryChange, Is.True);
            Assert.That(bob.HasInventoryChange, Is.True,
                "순번을 사람별로 세지 않으면 남이 먼저 보낸 번호 때문에 내 구매가 무시된다");
            Assert.That(economy.GetGold(Alice), Is.EqualTo(900));
            Assert.That(economy.GetGold(Bob), Is.EqualTo(900), "각자 자기 골드만 줄어야 한다");
        }

        [Test]
        public void 이벤트에_누구_것인지가_실린다()
        {
            var r = gate.HandlePurchase(Bob, offerId: 1, NextTicket(), isMine: false);

            Assert.That(r.Gold.PlayerId, Is.EqualTo(Bob));
            Assert.That(r.Gold.IsMine, Is.False);
            Assert.That(r.Gold.Reason, Is.EqualTo(GoldChangeReason.Spent));
            Assert.That(r.Offers.PlayerId, Is.EqualTo(Bob));
            Assert.That(r.Offers.IsMine, Is.False);
            Assert.That(r.Inventory.IsMine, Is.False);
        }

        // ── 상점 구역 ───────────────────────────────────────────────────

        [Test]
        public void 상점_구역_밖에서는_살_수_없다()
        {
            presence.Place(Alice, new WorldPosition(0f, 0f, 0f));   // 룰렛 앞

            var r = Buy(offerId: 1);

            Assert.That(r.HasRejection, Is.True);
            Assert.That(r.Rejection.Reason, Is.EqualTo(ShopPurchaseRejectReason.NotInRange));
            Assert.That(economy.GetGold(Alice), Is.EqualTo(1_000));
            Assert.That(r.HasInventoryChange, Is.False);
        }

        [Test]
        public void 구역_밖이면_목록도_전부_살_수_없음으로_나온다()
        {
            presence.Place(Alice, new WorldPosition(11f, 0f, 20f));   // 상점에서 20m 북쪽

            foreach (var offer in gate.BuildOffers(Alice, isMine: true).Offers)
            {
                Assert.That(offer.CanAfford, Is.False, $"칸 {offer.OfferId}");
            }
        }

        [Test]
        public void 위치를_모르면_살_수_없다()
        {
            presence.Remove(Alice);

            var r = Buy(offerId: 1);

            Assert.That(r.Rejection.Reason, Is.EqualTo(ShopPurchaseRejectReason.NotInRange),
                "위치를 모를 때 통과시키면 스폰 전 요청이 전부 뚫린다");
        }

        [TestCase(11f, 0f, true, TestName = "구역_중심")]
        [TestCase(16f, 0f, true, TestName = "경계_위는_안쪽이다")]
        [TestCase(16.01f, 0f, false, TestName = "경계_바로_밖")]
        [TestCase(11f, 5f, true, TestName = "옆으로_5m")]
        [TestCase(6f, 0f, true, TestName = "룰렛_쪽_경계")]
        [TestCase(5.5f, 0f, false, TestName = "룰렛과_상점_사이_빈_공간")]
        [TestCase(5f, 0f, false, TestName = "룰렛_구역_안")]
        [TestCase(14f, 0f, true, TestName = "예전_경계는_이제_한참_안쪽")]
        public void 상점_반경은_5m_다(float x, float z, bool inRange)
        {
            // 3m 였다 (S-GD-VYGZTJ, 2026-09-15 Play). 좁아서 들어가기 힘들다는 실측 보고로 넓혔다.
            // ⚠️ 룰렛 구역(반경 5)과의 빈 공간이 1m 뿐이다 — 5.5 케이스가 그 좁은 틈을 지킨다.
            presence.Place(Alice, new WorldPosition(x, 0f, z));

            var r = Buy(offerId: 1);

            Assert.That(r.HasRejection, Is.EqualTo(!inRange));
        }

        [Test]
        public void 높이는_판정에_쓰지_않는다()
        {
            presence.Place(Alice, new WorldPosition(11f, 50f, 0f));   // 상점 위 50m

            Assert.That(Buy(offerId: 1).HasRejection, Is.False,
                "준비맵은 평지라 지금은 높이를 보지 않는다 — 계단이 생기면 다시 정한다");
        }

        // ── 진열 데이터 자체의 검증 ─────────────────────────────────────

        [Test]
        public void 칸_번호가_겹친_진열은_만들_수_없다()
        {
            Assert.Throws<ArgumentException>(() => new ShopCatalog(new[]
            {
                new ShopOffer(offerId: 1, itemId: Potion, price: 10),
                new ShopOffer(offerId: 1, itemId: Bomb, price: 20),
            }), "겹친 채로 두면 뒤엣것이 앞엣것을 가려 두 칸이 같은 물건을 판다");
        }

        [TestCase(0, Potion, 100, 1, TestName = "칸_번호가_0")]
        [TestCase(1, 0, 100, 1, TestName = "아이템_번호가_0")]
        [TestCase(1, Potion, -1, 1, TestName = "가격이_음수")]
        [TestCase(1, Potion, 100, 0, TestName = "수량이_0")]
        public void 잘못된_상품은_만들_수_없다(int offerId, int itemId, int price, int quantity)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ShopOffer(offerId, itemId, price, quantity));
        }

        // ── 임시 목록 ───────────────────────────────────────────────────

        [Test]
        public void 임시_가격표와_임시_아이템_정의가_어긋나지_않는다()
        {
            var offers = PlaceholderShopCatalog.Create();
            var catalog = new PlaceholderShopItemCatalog();

            // 가격표와 아이템 정의는 서로 다른 파일에 산다. 한쪽만 고치면
            // "목록에는 보이는데 사면 거절되는 칸" 이 생기는데, 컴파일러는 아무 말도 안 한다.
            foreach (var offer in offers.Offers)
            {
                Assert.That(catalog.TryGet(offer.ItemId, out var definition), Is.True,
                    $"칸 {offer.OfferId} 의 아이템 {offer.ItemId} 에 정의가 없다");
                Assert.That(definition.IsValid, Is.True, $"아이템 {offer.ItemId} 정의가 불완전하다");
                Assert.That(definition.Kind, Is.AnyOf(EquipmentItemKind.Consumable, EquipmentItemKind.Accessory),
                    $"아이템 {offer.ItemId} 는 상점이 팔 수 있는 종류가 아니다 (S-GD-PVVHP9 규칙 2)");

                if (definition.Kind != EquipmentItemKind.Consumable)
                {
                    Assert.That(offer.Quantity, Is.EqualTo(1),
                        $"칸 {offer.OfferId} — 소모품이 아닌데 묶음 수량이 붙어 있다");
                }
            }
        }

        [Test]
        public void 임시_목록으로_실제로_살_수_있다()
        {
            // 이 테스트가 실패하면 "상점은 완성됐는데 살 물건이 없다" 는 상태로 돌아간 것이다.
            var gate = new ShopRequestGate(
                economy,
                ledger,
                PlaceholderShopCatalog.Create(),
                inventories,
                new PlaceholderShopItemCatalog(),
                new PreparationAccessGate(PlaceholderPreparationFacilityMap.Create(), presence));

            var potion = gate.HandlePurchase(Alice, offerId: 1, NextTicket(), isMine: true);

            Assert.That(potion.HasRejection, Is.False,
                potion.HasRejection ? potion.Rejection.Reason.ToString() : string.Empty);
            Assert.That(potion.HasInventoryChange, Is.True);
            Assert.That(economy.GetGold(Alice), Is.EqualTo(1_000 - 120), "체력 물약 120G");

            var ring = gate.HandlePurchase(Alice, offerId: 4, NextTicket(), isMine: true);

            Assert.That(ring.HasRejection, Is.False);
            Assert.That(ring.Inventory.Snapshot.AccessoryItemId,
                Is.EqualTo(PlaceholderShopCatalog.RingOfStrengthItemId));
        }

        [Test]
        public void 임시_상품_목록은_칸_번호가_겹치지_않는다()
        {
            var placeholder = PlaceholderShopCatalog.Create();

            Assert.That(placeholder.Count, Is.EqualTo(6));

            var seen = new HashSet<int>();

            foreach (var offer in placeholder.Offers)
            {
                Assert.That(seen.Add(offer.OfferId), Is.True, $"칸 번호 중복: {offer.OfferId}");
                Assert.That(offer.Price, Is.LessThanOrEqualTo(EconomyConfig.DefaultRoundGrantGold),
                    "한 라운드 지급액으로 살 수 없는 칸이 있으면 진열의 뜻이 없다");
            }
        }

        // ── 축복 선택 전에는 상점이 잠긴다 ──────────────────────────────
        //
        // 어댑터(OnlineEconomyCoordinator)가 선택 전 요청을 SelectionRequired 로 거절해 왔는데
        // **진열은 그 조건을 몰라서 밝게 켜져 있었다.** 규약이 없애려던 "켜져 있는데 눌리지 않는
        // 버튼" 이 정확히 그 상태다 (S-CLI-GRZGBK "진열 판정과 구매 판정은 같은 함수를 쓴다").

        /// <summary>축복을 아직 고르지 않은 준비 단계. <c>SetUp</c> 은 이미 골라 둔 상태라 따로 만든다.</summary>
        private ShopRequestGate GateBeforeSelection(out MatchEconomyState fresh)
        {
            fresh = new MatchEconomyState(EconomyConfig.Default, BlessingConfig.Default, Pool());
            fresh.BeginMatch(new[] { new MatchParticipant(Alice, WeaponTag.Sword) }, seed: 4242);
            fresh.BeginPreparation();

            var freshLedger = new EconomyRequestLedger();
            freshLedger.OpenPreparation();

            return new ShopRequestGate(
                fresh,
                freshLedger,
                Catalog(),
                inventories,
                items,
                new PreparationAccessGate(PlaceholderPreparationFacilityMap.Create(), presence));
        }

        [Test]
        public void 축복을_고르기_전에는_진열이_전부_꺼진다()
        {
            var before = GateBeforeSelection(out var fresh);

            Assume.That(fresh.GetGold(Alice), Is.EqualTo(1_000), "돈이 없어서 꺼진 것이 아니어야 한다");

            foreach (var offer in before.BuildOffers(Alice, isMine: true).Offers)
            {
                Assert.That(offer.CanAfford, Is.False,
                    $"칸 {offer.OfferId} — 선택 전에는 어차피 거절된다. 켜 두면 눌리지 않는 버튼이 된다");
            }
        }

        [Test]
        public void 축복을_고르기_전에_누르면_왜_안_되는지_알려준다()
        {
            var before = GateBeforeSelection(out var fresh);

            var r = before.HandlePurchase(
                Alice, offerId: 1, new EconomyRequestTicket(fresh.Round, 1), isMine: true);

            Assert.That(r.HasRejection, Is.True);
            Assert.That(r.Rejection.Reason, Is.EqualTo(ShopPurchaseRejectReason.SelectionRequired));
            Assert.That(fresh.GetGold(Alice), Is.EqualTo(1_000), "거절인데 골드가 나갔다");
            Assert.That(r.HasInventoryChange, Is.False);
        }

        [Test]
        public void 축복을_고르면_같은_칸이_그때_켜지고_실제로_팔린다()
        {
            var before = GateBeforeSelection(out var fresh);
            var ticket = new EconomyRequestTicket(fresh.Round, 1);

            Assume.That(FindOffer(before.BuildOffers(Alice, true), 1).CanAfford, Is.False);

            fresh.TrySelectAutomatically(Alice);

            Assert.That(FindOffer(before.BuildOffers(Alice, true), 1).CanAfford, Is.True,
                "선택을 마쳤는데도 꺼져 있으면 이번에는 반대쪽으로 어긋난 것이다");

            var r = before.HandlePurchase(Alice, offerId: 1, ticket, isMine: true);

            Assert.That(r.HasRejection, Is.False,
                r.HasRejection ? r.Rejection.Reason.ToString() : string.Empty);
            Assert.That(fresh.GetGold(Alice), Is.EqualTo(900));
        }

        [Test]
        public void 훈련처럼_선택_단계가_없는_모드는_선택_없이도_살_수_있다()
        {
            var fresh = new MatchEconomyState(EconomyConfig.Default, BlessingConfig.Default, Pool());
            fresh.BeginMatch(new[] { new MatchParticipant(Alice, WeaponTag.Sword) }, seed: 4242);
            fresh.BeginPreparation();

            var freshLedger = new EconomyRequestLedger();
            freshLedger.OpenPreparation();

            var practice = new ShopRequestGate(
                fresh,
                freshLedger,
                Catalog(),
                inventories,
                items,
                new PreparationAccessGate(PlaceholderPreparationFacilityMap.Create(), presence),
                requiresBlessingSelection: false);

            Assert.That(FindOffer(practice.BuildOffers(Alice, true), 1).CanAfford, Is.True);
            Assert.That(
                practice.HandlePurchase(Alice, 1, new EconomyRequestTicket(fresh.Round, 1), true).HasRejection,
                Is.False,
                "훈련에는 축복 선택 자체가 없다 — 제품 규칙을 그대로 걸면 연습 상점이 통째로 잠긴다");
        }

        // ── 보유 한도 (S-GD-FDHBNA) ─────────────────────────────────────

        [Test]
        public void 보유_한도에_닿은_칸은_비활성으로_알린다()
        {
            items.SetCarryLimit(Potion, 3);

            Buy(offerId: 1);
            Buy(offerId: 1);
            Assert.That(FindOffer(gate.BuildOffers(Alice, true), 1).CarryLimitReached, Is.False,
                "2개째는 아직 한도가 아니다");

            Buy(offerId: 1);   // 3개 — 한도

            var potion = FindOffer(gate.BuildOffers(Alice, true), 1);

            Assert.That(potion.CarryLimitReached, Is.True,
                "한도에 닿은 칸은 화면이 비활성으로 그릴 수 있어야 한다");
            Assert.That(potion.CanAfford, Is.True,
                "돈은 있다 — 두 값을 합치면 화면이 '돈이 없다' 와 '더 못 든다' 를 구분할 수 없다");
        }

        /// <summary>
        /// <b>진열이 비활성으로 알린 칸은 실제로도 거절된다</b> (규약 3 · S-CLI-GRZGBK).
        /// 둘이 어긋나면 흐려진 버튼이 사실은 눌리거나, 밝은 버튼이 눌리지 않는다.
        /// </summary>
        [Test]
        public void 비활성으로_알린_칸은_눌러도_같은_이유로_거절된다()
        {
            items.SetCarryLimit(Potion, 3);
            Buy(offerId: 1);
            Buy(offerId: 1);
            Buy(offerId: 1);

            Assume.That(FindOffer(gate.BuildOffers(Alice, true), 1).CarryLimitReached, Is.True);
            int before = economy.GetGold(Alice);

            var r = Buy(offerId: 1);

            Assert.That(r.Rejection.Reason, Is.EqualTo(ShopPurchaseRejectReason.CarryLimitReached));
            Assert.That(economy.GetGold(Alice), Is.EqualTo(before), "거절인데 골드가 나갔다");
        }

        /// <summary>
        /// <b>"한도에 닿았다" 를 저장하지 않는다</b> (S-GD-FDHBNA). 개수가 줄면 그 자리에서 다시 켜진다 —
        /// 기억해 둔 플래그를 쓰면 구매 되돌리기·소비 뒤에도 비활성으로 남는다.
        /// </summary>
        [Test]
        public void 개수가_줄면_그_자리에서_다시_살_수_있게_보인다()
        {
            items.SetCarryLimit(Potion, 3);
            Buy(offerId: 1);
            Buy(offerId: 1);
            Buy(offerId: 1);

            Assume.That(FindOffer(gate.BuildOffers(Alice, true), 1).CarryLimitReached, Is.True);

            aliceInventory.TryAuthorityUseConsumable(0);   // 하나 썼다 → 2개

            Assert.That(FindOffer(gate.BuildOffers(Alice, true), 1).CarryLimitReached, Is.False,
                "판정 기준은 '지금 몇 개인가' 이지 '몇 번 샀는가' 가 아니다");
        }

        /// <summary>
        /// 묶음 칸은 <b>남은 자리가 묶음 수량보다 적어도</b> 비활성이다.
        /// "보유 == 한도" 로만 보면 한도 3 에 2개를 들고 ×2 짜리 칸이 밝게 켜지는데, 누르면 거절된다.
        /// </summary>
        [Test]
        public void 묶음_칸은_남은_자리가_묶음보다_적으면_비활성이다()
        {
            items.SetCarryLimit(Bomb, 3);

            Buy(offerId: 2);   // 폭탄 ×2 → 2개

            var bundle = FindOffer(gate.BuildOffers(Alice, true), 2);

            Assert.That(bundle.CarryLimitReached, Is.True, "2 + 2 는 한도 3 을 넘는다");
            Assert.That(Buy(offerId: 2).Rejection.Reason,
                Is.EqualTo(ShopPurchaseRejectReason.CarryLimitReached),
                "진열이 '살 수 있다' 고 했는데 지급이 거절하면 그 칸은 눌리지 않는 버튼이다");
        }

        [Test]
        public void 한도를_모르는_물건은_비활성으로_알리지_않는다()
        {
            // Swift 에는 한도를 등록하지 않았다 — 한도가 없는 물건이다.
            Buy(offerId: 7);
            Buy(offerId: 7);
            Buy(offerId: 7);

            Assert.That(FindOffer(gate.BuildOffers(Alice, true), 7).CarryLimitReached, Is.False,
                "모르는 것을 '못 산다' 로 그리면 팔 수 있는 물건이 잠긴다");
        }

        [Test]
        public void 장신구는_보유_한도로_막지_않는다()
        {
            items.SetCarryLimit(Ring, 1);   // 실수로 장신구에 한도가 붙어도

            Buy(offerId: 3);   // 반지를 끼고 있다

            Assert.That(FindOffer(gate.BuildOffers(Alice, true), 3).CarryLimitReached, Is.False,
                "장신구는 AlreadyOwned 가 막는다 — 두 사유가 겹치면 화면 문구가 갈린다");
            Assert.That(FindOffer(gate.BuildOffers(Alice, true), 3).CanAfford, Is.False);
        }

        [Test]
        public void 인벤토리를_모르면_비활성으로_알리지_않는다()
        {
            items.SetCarryLimit(Potion, 3);
            inventories.Remove(Alice);   // 아직 스폰되지 않았다

            Assert.That(FindOffer(gate.BuildOffers(Alice, true), 1).CarryLimitReached, Is.False,
                "보유 개수를 모르는 상태를 '한도에 닿았다' 로 그리지 않는다");
        }

        private static ShopOfferView FindOffer(ShopOffersChanged view, int offerId)
        {
            foreach (var offer in view.Offers)
            {
                if (offer.OfferId == offerId)
                {
                    return offer;
                }
            }

            Assert.Fail($"진열에 칸 {offerId} 가 없습니다.");
            return default;
        }

        // ── 산 것이 실제 스탯이 되는가 (S-GD-YMKKDJ) ────────────────────
        //
        // 위 테스트들은 "골드가 빠지고 물건이 들어온다" 까지 본다. 원래 증상은 그 다음이었다 —
        // 살 수는 있는데 마셔도 장착해도 아무 일이 없었다. 여기서 **구매 → 인벤토리 → 경제 →
        // 스탯 시트** 사슬이 이어졌는지 본다.

        /// <summary>
        /// <b>실제 출고 진열</b>로 사는 게이트. 위 테스트들이 쓰는 <see cref="Catalog"/> 는
        /// 거절 경로를 만들려고 가격을 비틀어 둔 픽스처다 — 수호의 부적이 999,999G 다.
        /// 효과가 실제로 붙는지 보려면 <b>진짜 가격표</b>로 사야 한다.
        /// </summary>
        private ShopRequestGate RealGate() => new(
            economy,
            ledger,
            PlaceholderShopCatalog.Create(),
            inventories,
            items,
            new PreparationAccessGate(PlaceholderPreparationFacilityMap.Create(), presence));

        /// <summary>진열 칸 번호가 아니라 itemId 로 산다. 진열 순서가 바뀌어도 안 깨진다.</summary>
        private ShopPurchaseOutcome BuyItem(int itemId, int playerId = Alice)
        {
            ShopRequestGate real = RealGate();

            foreach (var offer in real.BuildOffers(playerId, isMine: true).Offers)
            {
                if (offer.ItemId == itemId)
                {
                    return real.HandlePurchase(playerId, offer.OfferId, NextTicket(), isMine: true);
                }
            }

            Assert.Fail($"실제 진열에 itemId {itemId} 가 없습니다.");
            return default;
        }

        /// <summary>축복·강화·아이템이 모두 얹힌 시트. 전투가 읽는 것과 같은 경로다.</summary>
        private StatSheet ComposedSheet(int playerId = Alice)
        {
            var sheet = new StatSheet(PlaceholderBlessingBalance.CreateStatRegistry());
            economy.ApplyUpgradeToSheet(playerId, sheet);
            return sheet;
        }

        [Test]
        public void 수호의_부적을_사면_방어력이_5_오른다()
        {
            Assert.That(BuyItem(PlaceholderShopCatalog.AmuletOfWardingItemId).Verdict,
                Is.EqualTo(EconomyRequestVerdict.Fresh));

            Assert.That(ComposedSheet().Get(DamageStatKeys.Defense), Is.EqualTo(5f).Within(0.001f),
                "산 장신구가 스탯 시트에 닿지 않았습니다 — 이것이 원래 증상입니다.");
        }

        [Test]
        public void 신속의_목걸이는_이동과_공격_속도를_함께_올린다()
        {
            BuyItem(PlaceholderShopCatalog.NecklaceOfHasteItemId);

            var sheet = ComposedSheet();
            Assert.That(sheet.Get(UpgradeStatKeys.MoveSpeed), Is.EqualTo(1.05f).Within(0.001f));
            Assert.That(sheet.Get(UpgradeStatKeys.AttackSpeed), Is.EqualTo(1.20f).Within(0.001f));
        }

        [Test]
        public void 검을_든_사람은_힘의_반지에서_근접_보너스를_받는다()
        {
            // Alice 는 검이다. 근접 +5 / 원거리 +3 으로 갈린다.
            BuyItem(PlaceholderShopCatalog.RingOfStrengthItemId);

            Assert.That(ComposedSheet().Get(DamageStatKeys.AttackPower), Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void 활을_든_사람은_힘의_반지에서_원거리_보너스를_받는다()
        {
            BuyItem(PlaceholderShopCatalog.RingOfStrengthItemId, Bob);

            Assert.That(ComposedSheet(Bob).Get(DamageStatKeys.AttackPower), Is.EqualTo(3f).Within(0.001f));
        }

        [Test]
        public void 반지_공격력은_입장_무기가_아니라_현재_무기를_따른다()
        {
            BuyItem(PlaceholderShopCatalog.RingOfStrengthItemId);
            Assert.That(ComposedSheet().Get(DamageStatKeys.AttackPower), Is.EqualTo(5f));
            Assert.That(economy.TrySetEquippedWeaponType(Alice, WeaponType.Bow), Is.True);
            Assert.That(ComposedSheet().Get(DamageStatKeys.AttackPower), Is.EqualTo(3f));
            Assert.That(economy.TrySetEquippedWeaponType(Alice, WeaponType.Sword), Is.True);
            Assert.That(ComposedSheet().Get(DamageStatKeys.AttackPower), Is.EqualTo(5f));
        }

        [Test]
        public void 장신구를_바꾸면_이전_효과가_사라진다()
        {
            // 부적 750 + 반지 600 = 1,350G 다. 라운드 기본 지급은 1,000G 라 그대로는
            // 두 번째 구매가 "돈 부족" 으로 정당하게 거절된다 — 교체 규칙을 보려면 채워야 한다.
            economy.Earn(Alice, 2_000);

            BuyItem(PlaceholderShopCatalog.AmuletOfWardingItemId);
            BuyItem(PlaceholderShopCatalog.RingOfStrengthItemId);

            var sheet = ComposedSheet();

            // 장신구는 한 개만 지닌다(S-GD-B7Q44N). 남으면 둘 다 붙은 셈이 된다.
            Assert.That(sheet.Get(DamageStatKeys.Defense), Is.Zero);
            Assert.That(sheet.Get(DamageStatKeys.AttackPower), Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void 소모품을_사도_장신구_칸은_건드리지_않는다()
        {
            BuyItem(PlaceholderShopCatalog.AmuletOfWardingItemId);
            BuyItem(PlaceholderShopCatalog.HealthPotionItemId);

            Assert.That(economy.GetEquippedAccessory(Alice),
                Is.EqualTo(PlaceholderShopCatalog.AmuletOfWardingItemId));
            Assert.That(ComposedSheet().Get(DamageStatKeys.Defense), Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void 아무것도_안_사면_스탯은_기준값_그대로다()
        {
            var sheet = ComposedSheet();

            Assert.That(sheet.Get(DamageStatKeys.Defense), Is.Zero);
            Assert.That(sheet.Get(DamageStatKeys.AttackPower), Is.Zero);
            Assert.That(sheet.Get(UpgradeStatKeys.MoveSpeed), Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void 시트를_두_번_얹어도_장신구가_겹치지_않는다()
        {
            BuyItem(PlaceholderShopCatalog.AmuletOfWardingItemId);

            var sheet = new StatSheet(PlaceholderBlessingBalance.CreateStatRegistry());
            economy.ApplyUpgradeToSheet(Alice, sheet);
            economy.ApplyUpgradeToSheet(Alice, sheet);

            // 축복 시트는 여러 번 다시 만들어진다. 겹치면 방어력이 14 가 된다.
            Assert.That(sheet.Get(DamageStatKeys.Defense), Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void 장신구는_라운드가_끝나도_유지되고_강화는_초기화된다()
        {
            // S-GD-YMKKDJ 가 가장 크게 경고한 항목이다 — 장신구를 강화 슬롯과 같은 자리에 두면
            // ResetForNextRound 에 같이 쓸려 나가고, 산 사람은 2라운드부터 효과를 잃는다.
            economy.Earn(Alice, 5_000);
            BuyItem(PlaceholderShopCatalog.AmuletOfWardingItemId);
            economy.TryUpgradeSlot(Alice, UpgradeSlot.Armor);
            int upgradedLevel = economy.GetUpgradeLevel(Alice, UpgradeSlot.Armor);
            Assert.That(upgradedLevel, Is.GreaterThan(0), "대조군이 성립하려면 강화가 실제로 올라야 한다.");

            economy.EndPreparation();
            economy.TryApplyRoundResults(1, new[]
            {
                new RoundOutcome(Alice, 0, 0, true),
                new RoundOutcome(Bob, 0, 0, true),
            });
            economy.BeginPreparation();

            Assert.That(economy.GetEquippedAccessory(Alice),
                Is.EqualTo(PlaceholderShopCatalog.AmuletOfWardingItemId), "장신구는 매치가 끝날 때까지 유지된다.");
            Assert.That(ComposedSheet().Get(DamageStatKeys.Defense), Is.EqualTo(5f).Within(0.001f),
                "2라운드에도 부적 방어력이 시트에 남아야 한다.");
            Assert.That(economy.GetUpgradeLevel(Alice, UpgradeSlot.Armor), Is.Zero,
                "강화는 반대다 — 라운드마다 0 으로 돌아간다 (S-GD-A2YDDJ).");
        }

        [Test]
        public void 목걸이는_이동_속도를_5퍼센트_올린다()
        {
            BuyItem(PlaceholderShopCatalog.NecklaceOfHasteItemId);

            Assert.That(ComposedSheet().Get(UpgradeStatKeys.MoveSpeed), Is.EqualTo(1.05f).Within(0.001f));
        }

        [Test]
        public void 소모품은_경제_시트에_들어가지_않는다()
        {
            // 신속 물약은 **마신 본인의 이동 컴포넌트**에 직접 걸린다 — 체력 물약과 같은 경계다.
            // 경제가 들고 있으면 스냅샷·라운드 정산에 얽혀 만료 시점이 어긋난다.
            // 지속·중첩 규칙은 ConsumableBuffStateTests 가, 실제 배선은 CombatantMotor 가 맡는다.
            BuyItem(PlaceholderShopCatalog.SwiftnessPotionItemId);
            BuyItem(PlaceholderShopCatalog.HealthPotionItemId);

            Assert.That(ComposedSheet().Get(UpgradeStatKeys.MoveSpeed), Is.EqualTo(1f).Within(0.001f));
        }

        // ── 대역 ────────────────────────────────────────────────────────

        private sealed class FakeItemCatalog : IEquipmentItemCatalog, IEquipmentItemCarryLimitCatalog
        {
            private readonly Dictionary<int, EquipmentItemDefinition> definitions = new();
            private readonly Dictionary<int, int> carryLimits = new();

            /// <summary>
            /// 이 물건은 전부 몇 개까지 들 수 있는가. <b>등록하지 않으면 한도가 없다</b> —
            /// 진짜 카탈로그도 한도를 아는 물건에만 답한다(<c>RecommendedMagicBottleCatalog</c>).
            /// </summary>
            public void SetCarryLimit(int itemId, int totalCarryLimit) =>
                carryLimits[itemId] = totalCarryLimit;

            public bool TryGetCarryLimit(int itemId, out int totalCarryLimit) =>
                carryLimits.TryGetValue(itemId, out totalCarryLimit);

            public void AddConsumable(int itemId, int visualId) => definitions[itemId] = new EquipmentItemDefinition(
                itemId, EquipmentItemKind.Consumable, EquipmentArchetype.None, visualId, WeaponType.None, maxStack: 3);

            public void AddAccessory(int itemId, int visualId) => definitions[itemId] = new EquipmentItemDefinition(
                itemId, EquipmentItemKind.Accessory, EquipmentArchetype.Accessory, visualId, WeaponType.None, maxStack: 1);

            public void AddWeapon(int itemId, int visualId) => definitions[itemId] = new EquipmentItemDefinition(
                itemId, EquipmentItemKind.Weapon, EquipmentArchetype.Sword, visualId, WeaponType.Sword, maxStack: 1);

            public bool TryGet(int itemId, out EquipmentItemDefinition definition) =>
                definitions.TryGetValue(itemId, out definition);
        }

        /// <summary>진짜 <see cref="EquipmentInventory"/> 를 감싼다 — 슬롯 규칙을 흉내 내지 않기 위해서다.</summary>
        private sealed class FakeInventory : IEquipmentInventoryDriver
        {
            private readonly EquipmentInventory inner = new EquipmentInventory();
            private readonly IEquipmentItemCatalog catalog;
            private bool forced;
            private EquipmentInventoryFailure forcedFailure;

            public FakeInventory(IEquipmentItemCatalog catalog) => this.catalog = catalog;

            /// <summary>전투 상태 때문에 거절되는 경우를 흉내 낸다 — 순수 도메인에서는 만들 수 없다.</summary>
            public void ForceFailure(EquipmentInventoryFailure failure)
            {
                forced = true;
                forcedFailure = failure;
            }

            public EquipmentInventorySnapshot EquipmentSnapshot => inner.Snapshot;

            public EquipmentInventoryOperationResult TryAuthorityPickup(int itemId, int quantity) => forced
                ? EquipmentInventoryOperationResult.Rejected(forcedFailure, inner.Snapshot)
                : inner.TryPickup(itemId, quantity, catalog);

            public EquipmentInventoryOperationResult TryAuthorityDropWeapon() => inner.TryDropWeapon();

            public EquipmentInventoryOperationResult TryAuthorityUseConsumable(int slotIndex) =>
                inner.TryUseConsumable(slotIndex, catalog);

            public void ApplyPresentation(in EquipmentInventorySnapshot snapshot) => inner.ApplySnapshot(snapshot);
        }

        private sealed class FakePresenceProvider : IPreparationPresenceProvider
        {
            private readonly Dictionary<int, WorldPosition> byPlayer = new();

            public void Place(int playerId, WorldPosition position) => byPlayer[playerId] = position;

            public void Remove(int playerId) => byPlayer.Remove(playerId);

            public bool TryGetPosition(int playerId, out WorldPosition position) =>
                byPlayer.TryGetValue(playerId, out position);
        }

        private sealed class FakeInventoryProvider : IShopInventoryProvider
        {
            private readonly Dictionary<int, IEquipmentInventoryDriver> byPlayer = new();

            public void Set(int playerId, IEquipmentInventoryDriver inventory) => byPlayer[playerId] = inventory;

            public void Remove(int playerId) => byPlayer.Remove(playerId);

            public bool TryResolve(int playerId, out IEquipmentInventoryDriver inventory) =>
                byPlayer.TryGetValue(playerId, out inventory);
        }
    }
}

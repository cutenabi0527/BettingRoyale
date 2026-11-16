using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.LocalPlay;
using BettingRoyale.Data.Preparation;
using BettingRoyale.Data.Roulette;
using BettingRoyale.Data.Scenes;
using BettingRoyale.Data.Shop;
using BettingRoyale.Data.Training;
using BettingRoyale.Data.Throwing;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 준비 단계 공용 조립 (Jira S15P21A606-166).
    ///
    /// <para>
    /// <b>이 파일이 지키는 것은 하나다 — 준비 단계의 상점·룰렛이 경로마다 다른 물건이 되지 않는다.</b>
    /// 온라인 Host·Client, 95_ReadyLab, 96_TrainingLab, Local Play/Run MVP 는 같은 게이트·같은 원장·
    /// 같은 지갑·같은 카탈로그를 써야 한다. 갈리면 "한 경로에서만 안 되는" 결함이 생기는데,
    /// 그건 그 경로를 띄우기 전까지 아무도 모른다.
    /// </para>
    /// </summary>
    [TestFixture]
    public class PreparationPathAssemblyTests
    {
        // ── ① 정본 카탈로그가 진열을 전부 덮는가 (D-CLI-R69V68) ────────────────

        /// <summary>
        /// <b>진열 6칸이 전부 팔려야 한다.</b> 예전에는 마력 폭발병 하나만 팔렸다 — 나머지 5칸은
        /// 사거리·골드를 다 통과한 마지막 지급 단계에서 <c>InvalidItem</c> 으로 떨어졌다.
        /// 원인은 상점이 아니라 <b>장비 정의가 없다는 것</b>이었다.
        /// </summary>
        [Test]
        public void 공용_폴백_카탈로그가_임시_진열_여섯_칸을_전부_판다()
        {
            ShopCatalog catalog = PlaceholderShopCatalog.Create();
            IEquipmentItemCatalog items = PreparationShopItemFallbackCatalog.Instance;

            IReadOnlyList<ShopOffer> unsellable = ShopCatalogCoverage.FindUnsellable(catalog, items);

            Assert.That(unsellable, Is.Empty, ShopCatalogCoverage.Describe(unsellable));
            Assert.That(ShopCatalogCoverage.CoversEveryOffer(catalog, items), Is.True);
        }

        /// <summary>
        /// 진열 칸의 종류가 소모품·장신구뿐이어야 한다 (S-GD-PVVHP9 규칙 2).
        /// 무기가 섞이면 구매가 들고 있던 무기를 교체해 버린다.
        /// </summary>
        [Test]
        public void 임시_진열은_소모품과_장신구만_판다()
        {
            ShopCatalog catalog = PlaceholderShopCatalog.Create();

            for (int i = 0; i < catalog.Count; i++)
            {
                ShopOffer offer = catalog.Offers[i];
                Assert.That(
                    ShopRequestGate.IsSellable(
                        offer, PreparationShopItemFallbackCatalog.Instance, out EquipmentItemDefinition definition),
                    Is.True,
                    $"칸 {offer.OfferId}");
                Assert.That(
                    definition.Kind,
                    Is.EqualTo(EquipmentItemKind.Consumable).Or.EqualTo(EquipmentItemKind.Accessory));
            }
        }

        /// <summary>
        /// <b>확정된 보유 한도만 답한다.</b> 마력 폭발병(S-GD-78REEH)과 물약 2종(S-GD-ZD76H8)에는
        /// 한도가 있고, <b>장신구에는 한도라는 개념이 없다</b> — 0 을 지어내면 인벤토리가
        /// "들 수 없는 물건"(<c>InvalidItem</c>)으로 읽어 장신구를 아예 못 사게 된다.
        /// </summary>
        [Test]
        public void 확정된_보유_한도만_답한다()
        {
            var fallback = PreparationShopItemFallbackCatalog.Instance;

            Assert.That(fallback.TryGetCarryLimit(RecommendedMagicBottleCatalog.MagicBottleItemId, out int bottle), Is.True);
            Assert.That(bottle, Is.EqualTo(RecommendedMagicBottleCatalog.MagicBottleCarryLimit));

            Assert.That(fallback.TryGetCarryLimit(PlaceholderShopCatalog.HealthPotionItemId, out int potion), Is.True);
            Assert.That(potion, Is.EqualTo(RecommendedShopItemCatalog.HealthPotionCarryLimit));

            Assert.That(fallback.TryGetCarryLimit(PlaceholderShopCatalog.SwiftnessPotionItemId, out int swiftness), Is.True);
            Assert.That(swiftness, Is.EqualTo(RecommendedShopItemCatalog.SwiftnessPotionCarryLimit));

            Assert.That(fallback.TryGetCarryLimit(PlaceholderShopCatalog.RingOfStrengthItemId, out int accessory), Is.False);
            Assert.That(accessory, Is.Zero);
        }

        /// <summary>
        /// 마력 폭발병은 <b>투척 정의를 가진 쪽</b>이 이긴다. 두 곳이 같은 id 를 다르게 정의하면
        /// 어느 쪽이 이기는지가 호출 순서에 숨는다.
        /// </summary>
        [Test]
        public void 마력_폭발병은_투척_카탈로그_정의를_쓴다()
        {
            var fallback = PreparationShopItemFallbackCatalog.Instance;

            Assert.That(fallback.TryGet(RecommendedMagicBottleCatalog.MagicBottleItemId, out EquipmentItemDefinition definition), Is.True);
            Assert.That(definition.MaxStack, Is.EqualTo(RecommendedMagicBottleCatalog.MagicBottleMaxStack));
            Assert.That(fallback.TryGetThrowing(RecommendedMagicBottleCatalog.MagicBottleItemId, out _), Is.True);
            Assert.That(fallback.TryGetThrowing(PlaceholderShopCatalog.HealthPotionItemId, out _), Is.False);
        }

        /// <summary>
        /// <b>공용 폴백은 제품이 실제로 쓰는 정의를 그대로 답한다</b> (D-CLI-R69V68).
        ///
        /// <para>
        /// ⚠️ 이 검사가 없으면 폴백이 조용히 <b>두 번째 정의 묶음</b>으로 갈라질 수 있다. 실제로
        /// 한 번 갈렸었다 — 런타임은 확정 정의(S-GD-ZD76H8)로 옮겼는데 이 파일의 테스트만 옛
        /// QA 임시 정의를 보고 있었고, 그 상태에서는 "테스트는 초록인데 게임에서는 안 팔리는"
        /// 결함이 다시 와도 아무도 못 잡는다.
        /// </para>
        /// </summary>
        [Test]
        public void 공용_폴백은_확정된_상점_정의를_그대로_답한다()
        {
            var fallback = PreparationShopItemFallbackCatalog.Instance;

            foreach (EquipmentItemDefinition expected in RecommendedShopItemCatalog.All)
            {
                Assert.That(fallback.TryGet(expected.ItemId, out EquipmentItemDefinition actual), Is.True,
                    $"아이템 {expected.ItemId}");
                Assert.That(actual.Kind, Is.EqualTo(expected.Kind), $"아이템 {expected.ItemId} 종류");
                Assert.That(actual.MaxStack, Is.EqualTo(expected.MaxStack), $"아이템 {expected.ItemId} 적재량");
                Assert.That(actual.VisualId, Is.EqualTo(expected.VisualId), $"아이템 {expected.ItemId} 시각 정의");
            }
        }

        // ── ② 구매 원자성과 무기 보존 ─────────────────────────────────────────

        private const int Buyer = 1;

        private sealed class Fixture
        {
            public readonly MatchEconomyState Economy;
            public readonly EconomyRequestLedger Ledger = new EconomyRequestLedger();
            public readonly ShopRequestGate Shop;
            public readonly RouletteRequestGate Roulette;
            public readonly EquipmentInventory Inventory = new EquipmentInventory();
            public readonly PresenceStub Presence = new PresenceStub();
            private int sequence;

            public Fixture()
            {
                Economy = new MatchEconomyState(
                    EconomyConfig.Default, BlessingConfig.Default, LocalPlayBlessingCatalog.All);
                Economy.BeginMatch(
                    new[] { new MatchParticipant(Buyer, WeaponTag.Sword) }, seed: 7);
                Economy.BeginPreparation();
                Ledger.OpenPreparation();

                var access = new PreparationAccessGate(
                    PreparationFacilityMapCatalog.CreateDefault(), Presence);
                var inventories = new InventoryStub(Inventory, PreparationShopItemFallbackCatalog.Instance);

                Shop = new ShopRequestGate(
                    Economy, Ledger, PlaceholderShopCatalog.Create(), inventories,
                    PreparationShopItemFallbackCatalog.Instance, access,
                    requiresBlessingSelection: false);
                Roulette = new RouletteRequestGate(
                    Economy, Ledger,
                    new RouletteTable(
                        PlaceholderRouletteWheel.Create(), PlaceholderRouletteConfig.Create(30)),
                    access);
                Roulette.BeginPreparation(Buyer, true);
            }

            public EconomyRequestTicket NextTicket() =>
                new EconomyRequestTicket(Ledger.Epoch, ++sequence);

            public EconomyRequestTicket LastTicket() =>
                new EconomyRequestTicket(Ledger.Epoch, sequence);

            /// <summary>
            /// 베팅이 열릴 때까지 돌린다. <b>첫 판은 준비 시작 뒤 지연이 있다</b>
            /// (<c>PlaceholderRouletteConfig.StartDelaySeconds</c>) — 그 사실을 흉내 내지 않고
            /// 제품과 같은 시간 규칙을 그대로 지난다.
            /// </summary>
            public void AdvanceToBetting()
            {
                var random = new DeterministicRandom(11);

                for (int i = 0; i < 30 * 120; i++)
                {
                    if (Roulette.Table.State == RouletteState.Betting) return;
                    Roulette.Tick(int.MaxValue, random, Buyer, true);
                }

                Assert.Fail("베팅이 열리지 않았습니다.");
            }

            public void StandAt(PreparationFacility facility)
            {
                PreparationFacilityMap map = PreparationFacilityMapCatalog.CreateDefault();
                Assert.That(map.TryGet(facility, out PreparationFacilityArea area), Is.True);
                Presence.Place(Buyer, area.Center);
            }
        }

        private static ShopOffer FindOffer(EquipmentItemKind kind)
        {
            ShopCatalog catalog = PlaceholderShopCatalog.Create();
            for (int i = 0; i < catalog.Count; i++)
            {
                if (ShopRequestGate.IsSellable(
                        catalog.Offers[i], PreparationShopItemFallbackCatalog.Instance,
                        out EquipmentItemDefinition definition)
                    && definition.Kind == kind)
                {
                    return catalog.Offers[i];
                }
            }

            Assert.Fail($"{kind} 진열 칸이 없습니다.");
            return default;
        }

        [Test]
        public void 정상_구매는_골드와_물건이_함께_움직인다()
        {
            var fixture = new Fixture();
            fixture.StandAt(PreparationFacility.Shop);
            ShopOffer offer = FindOffer(EquipmentItemKind.Consumable);
            int before = fixture.Economy.GetGold(Buyer);

            ShopPurchaseOutcome outcome =
                fixture.Shop.HandlePurchase(Buyer, offer.OfferId, fixture.NextTicket(), true);

            Assert.That(outcome.HasRejection, Is.False);
            Assert.That(outcome.HasInventoryChange, Is.True);
            Assert.That(fixture.Economy.GetGold(Buyer), Is.EqualTo(before - offer.Price));
            Assert.That(fixture.Inventory.Snapshot.GetConsumableSlot(0).ItemId, Is.EqualTo(offer.ItemId));
        }

        /// <summary>
        /// 구역 밖 구매는 <b>골드도 인벤토리도</b> 건드리지 않는다 (S-CLI-9QYFCT).
        /// </summary>
        [Test]
        public void 구역_밖_구매는_잔액과_인벤토리를_바꾸지_않는다()
        {
            var fixture = new Fixture();
            fixture.Presence.Place(Buyer, new WorldPosition(100f, 0f, 100f));
            ShopOffer offer = FindOffer(EquipmentItemKind.Consumable);
            int before = fixture.Economy.GetGold(Buyer);

            ShopPurchaseOutcome outcome =
                fixture.Shop.HandlePurchase(Buyer, offer.OfferId, fixture.NextTicket(), true);

            Assert.That(outcome.HasRejection, Is.True);
            Assert.That(outcome.Rejection.Reason, Is.EqualTo(ShopPurchaseRejectReason.NotInRange));
            Assert.That(fixture.Economy.GetGold(Buyer), Is.EqualTo(before));
            Assert.That(fixture.Inventory.Snapshot.GetConsumableSlot(0).IsEmpty, Is.True);
        }

        /// <summary>같은 티켓을 두 번 보내도 골드는 한 번만 빠지고 물건도 한 번만 들어온다.</summary>
        [Test]
        public void 같은_티켓의_재전송은_잔액도_인벤토리도_다시_바꾸지_않는다()
        {
            var fixture = new Fixture();
            fixture.StandAt(PreparationFacility.Shop);
            ShopOffer offer = FindOffer(EquipmentItemKind.Consumable);

            fixture.Shop.HandlePurchase(Buyer, offer.OfferId, fixture.NextTicket(), true);
            int afterFirst = fixture.Economy.GetGold(Buyer);
            int quantityAfterFirst = fixture.Inventory.Snapshot.GetConsumableSlot(0).Count;

            ShopPurchaseOutcome repeat =
                fixture.Shop.HandlePurchase(Buyer, offer.OfferId, fixture.LastTicket(), true);

            Assert.That(repeat.Verdict, Is.Not.EqualTo(EconomyRequestVerdict.Fresh));
            Assert.That(repeat.HasRejection, Is.False, "재전송에는 거절이 오지 않는다.");
            Assert.That(fixture.Economy.GetGold(Buyer), Is.EqualTo(afterFirst));
            Assert.That(
                fixture.Inventory.Snapshot.GetConsumableSlot(0).Count, Is.EqualTo(quantityAfterFirst));
        }

        /// <summary>
        /// <b>소모품·장신구를 사도 무기 슬롯은 그대로다.</b> 무기 슬롯이 바뀌지 않으면 Unity 어댑터가
        /// 무기 표현을 다시 맞추지 않으므로(<c>EquipmentInventoryBehaviour</c>) 무기가 벗겨지지 않는다.
        /// </summary>
        [Test]
        public void 소모품과_장신구_구매는_무기_슬롯을_건드리지_않는다()
        {
            var fixture = new Fixture();
            fixture.StandAt(PreparationFacility.Shop);

            // 들고 있는 무기를 인벤토리가 알고 있는 상태(제품 스폰이 맞춰 주는 상태)를 만든다.
            Assert.That(
                fixture.Inventory.TryPickup(SwordItemId, 1, new SwordOnlyCatalog()).Succeeded, Is.True);
            int weaponBefore = fixture.Inventory.Snapshot.WeaponItemId;

            foreach (EquipmentItemKind kind in new[] { EquipmentItemKind.Consumable, EquipmentItemKind.Accessory })
            {
                ShopOffer offer = FindOffer(kind);
                fixture.Shop.HandlePurchase(Buyer, offer.OfferId, fixture.NextTicket(), true);
                Assert.That(
                    fixture.Inventory.Snapshot.WeaponItemId, Is.EqualTo(weaponBefore),
                    $"{kind} 구매가 무기 슬롯을 바꿨다.");
            }
        }

        private const int SwordItemId = 1101;

        private sealed class SwordOnlyCatalog : IEquipmentItemCatalog
        {
            public bool TryGet(int itemId, out EquipmentItemDefinition definition)
            {
                if (itemId == SwordItemId)
                {
                    definition = new EquipmentItemDefinition(
                        SwordItemId, EquipmentItemKind.Weapon, EquipmentArchetype.Sword,
                        2101, WeaponType.Sword, 1);
                    return true;
                }

                definition = default;
                return false;
            }
        }

        // ── ③ 룰렛이 같은 지갑·같은 원장을 쓴다 ───────────────────────────────

        /// <summary>
        /// 베팅·물리기가 <b>상점과 같은 지갑</b>을 움직이고, 순번은 <b>같은 원장</b>을 쓴다.
        /// 원장이 갈리면 재전송 판정이 어긋난다.
        /// </summary>
        [Test]
        public void 룰렛_베팅과_물리기가_상점과_같은_지갑과_원장을_쓴다()
        {
            var fixture = new Fixture();
            fixture.StandAt(PreparationFacility.Roulette);
            fixture.AdvanceToBetting();
            int before = fixture.Economy.GetGold(Buyer);

            RouletteRequestOutcome bet =
                fixture.Roulette.HandleBet(Buyer, 1, 100, fixture.NextTicket(), true);

            Assert.That(bet.HasRejection, Is.False);
            Assert.That(bet.HasBetPlaced, Is.True);
            Assert.That(fixture.Economy.GetGold(Buyer), Is.EqualTo(before - bet.BetPlaced.Amount));

            RouletteWithdrawOutcome withdraw =
                fixture.Roulette.HandleWithdraw(Buyer, fixture.NextTicket(), true);

            Assert.That(withdraw.HasRejection, Is.False);
            Assert.That(fixture.Economy.GetGold(Buyer), Is.EqualTo(before));
            Assert.That(
                fixture.Economy.GetTotalInflow(Buyer), Is.EqualTo(EconomyConfig.DefaultRoundGrantGold),
                "물린 돈은 번 돈이 아니다 — TotalInflow 가 오르면 최종 순위가 조용히 틀린다.");
        }

        /// <summary>회전이 끝나면 정산이 같은 지갑에 반영된다. 결과는 Result 상태에서만 밝혀진다.</summary>
        [Test]
        public void 룰렛_회전과_정산이_같은_지갑에_반영된다()
        {
            var fixture = new Fixture();
            fixture.StandAt(PreparationFacility.Roulette);
            fixture.AdvanceToBetting();
            Assert.That(
                fixture.Roulette.HandleBet(Buyer, 1, 100, fixture.NextTicket(), true).HasRejection, Is.False);
            int afterBet = fixture.Economy.GetGold(Buyer);

            var random = new DeterministicRandom(4242);
            bool settled = false;

            for (int i = 0; i < 30 * 60 && !settled; i++)
            {
                RouletteTickOutcome tick = fixture.Roulette.Tick(int.MaxValue, random, Buyer, true);
                settled = tick.Settlements.Length > 0;
            }

            Assert.That(settled, Is.True, "베팅한 판이 끝나야 한다.");
            RouletteStateChanged state = fixture.Roulette.BuildState(Buyer, true);
            Assert.That(state.MyStakeTotal, Is.Zero, "정산된 판의 베팅은 남지 않는다.");
            Assert.That(
                fixture.Economy.GetGold(Buyer), Is.GreaterThanOrEqualTo(0),
                "정산 후 잔액은 음수가 될 수 없습니다.");
        }

        // ── ④ 준비 맵 변형 ──────────────────────────────────────────────────

        /// <summary>
        /// <b>등록된 모든 준비 맵</b>이 세 시설을 그대로 갖는다. 새 준비 맵이 늘었을 때 그 맵에서만
        /// 시설이 사라지는 것을 여기서 잡는다.
        /// </summary>
        [Test]
        public void 등록된_모든_준비_맵이_세_시설을_조립한다()
        {
            int preparationMaps = 0;

            foreach (MatchSceneDescriptor descriptor in MatchSceneCatalog.All)
            {
                if (descriptor.Phase != MatchScenePhase.Preparation) continue;

                preparationMaps++;
                Assert.That(
                    PreparationFacilityMapCatalog.TryCreate(descriptor.Key, out PreparationFacilityMap map),
                    Is.True, descriptor.Key.ToString());
                Assert.That(map.TryGet(PreparationFacility.Shop, out _), Is.True, descriptor.Key.ToString());
                Assert.That(map.TryGet(PreparationFacility.Upgrade, out _), Is.True, descriptor.Key.ToString());
                Assert.That(map.TryGet(PreparationFacility.Roulette, out _), Is.True, descriptor.Key.ToString());
            }

            Assert.That(preparationMaps, Is.GreaterThanOrEqualTo(2), "준비 맵이 둘 이상 등록돼 있어야 한다.");
        }

        /// <summary>전투 맵 키로는 시설을 열지 않는다. 조용히 기본 배치를 주면 잘못된 맵에서도 열린다.</summary>
        [Test]
        public void 전투_맵_키로는_시설_구역표를_만들지_않는다()
        {
            Assert.That(
                PreparationFacilityMapCatalog.TryCreate(MatchSceneCatalog.DefaultCombat, out _), Is.False);
            Assert.That(
                PreparationFacilityMapCatalog.Resolve(MatchSceneCatalog.DefaultCombat, out bool registered),
                Is.Not.Null);
            Assert.That(registered, Is.False);
        }

        // ── ⑤ 96_TrainingLab · 제품 훈련장 ──────────────────────────────────

        /// <summary>
        /// 훈련 세션의 베팅이 열릴 때까지 돌린다. 첫 판 지연은 제품과 같은 값이다.
        /// </summary>
        private static void AdvanceTrainingToBetting(TrainingPracticeState state)
        {
            for (int i = 0; i < 120; i++)
            {
                if (state.BuildRouletteState().State == RouletteState.Betting) return;
                state.TickRoulette(TrainingPracticeState.RouletteTicksPerSecond);
            }

            Assert.Fail("훈련 룰렛의 베팅이 열리지 않았습니다.");
        }

        private static TrainingPracticeState NewTraining(out TrainingInventoryStub inventory)
        {
            var stub = new TrainingInventoryStub(PreparationShopItemFallbackCatalog.Instance);
            inventory = stub;
            return new TrainingPracticeState(
                "session", 1, WeaponType.Sword, PlaceholderShopCatalog.Create(), stub,
                PreparationShopItemFallbackCatalog.Instance, LocalPlayBlessingCatalog.All);
        }

        [Test]
        public void 훈련_세션이_제품_상점과_같은_진열을_연다()
        {
            TrainingPracticeState state = NewTraining(out TrainingInventoryStub inventory);
            ShopOffersChanged offers = state.BuildOffers();

            Assert.That(offers.Offers.Length, Is.EqualTo(PlaceholderShopCatalog.Create().Count));
            foreach (ShopOfferView view in offers.Offers)
            {
                Assert.That(view.Kind, Is.Not.EqualTo(EquipmentItemKind.None), $"칸 {view.OfferId} 의 정의가 없다.");
            }

            ShopOffer offer = FindOffer(EquipmentItemKind.Consumable);
            int before = state.Gold;
            Assert.That(
                state.Handle(1, new TrainingActionRequest("session", 1, TrainingActionKind.Purchase, offer.OfferId, ""))
                    .Accepted,
                Is.True);
            Assert.That(state.Gold, Is.EqualTo(before - offer.Price));
            Assert.That(inventory.Driver.EquipmentSnapshot.GetConsumableSlot(0).ItemId, Is.EqualTo(offer.ItemId));
        }

        [Test]
        public void 훈련_세션의_룰렛이_같은_지갑을_움직이고_정산까지_간다()
        {
            TrainingPracticeState state = NewTraining(out _);
            AdvanceTrainingToBetting(state);
            int before = state.Gold;

            int packed = RouletteRequestPayload.PackBet(1, 100);
            Assert.That(
                state.Handle(1, new TrainingActionRequest("session", 1, TrainingActionKind.Roulette, packed, ""))
                    .Accepted,
                Is.True);
            Assert.That(state.Gold, Is.EqualTo(before - 100));
            Assert.That(state.BuildRouletteView().MyStakeTotal, Is.EqualTo(100));

            bool settled = false;
            for (int i = 0; i < 60 && !settled; i++)
            {
                settled = state.TickRoulette(30).Settlements.Length > 0;
            }

            Assert.That(settled, Is.True, "훈련 세션에서도 판이 끝나야 한다.");
            Assert.That(state.BuildRouletteView().MyStakeTotal, Is.Zero);
        }

        [Test]
        public void 훈련_물리기는_전액_돌려주고_개인_초기화는_걸린_돈을_남기지_않는다()
        {
            TrainingPracticeState state = NewTraining(out _);
            AdvanceTrainingToBetting(state);
            int before = state.Gold;

            state.Handle(1, new TrainingActionRequest(
                "session", 1, TrainingActionKind.Roulette, RouletteRequestPayload.PackBet(3, 200), ""));
            Assert.That(state.Gold, Is.EqualTo(before - 200));

            state.Handle(1, new TrainingActionRequest(
                "session", 2, TrainingActionKind.Roulette, RouletteRequestPayload.PackWithdraw(), ""));
            Assert.That(state.Gold, Is.EqualTo(before));

            state.Handle(1, new TrainingActionRequest(
                "session", 3, TrainingActionKind.Roulette, RouletteRequestPayload.PackBet(3, 200), ""));
            state.Handle(1, new TrainingActionRequest("session", 4, TrainingActionKind.Reset, 0, ""));

            Assert.That(state.Gold, Is.EqualTo(TrainingPracticeState.GrantGoldAmount));
            Assert.That(state.BuildRouletteView().MyStakeTotal, Is.Zero, "초기화 뒤에 옛 베팅이 남으면 안 된다.");
        }

        /// <summary>훈련 스냅샷이 회선을 왕복해도 룰렛 칸이 사라지지 않는다.</summary>
        [Test]
        public void 훈련_스냅샷_왕복이_룰렛_칸을_보존한다()
        {
            TrainingPracticeState state = NewTraining(out _);
            AdvanceTrainingToBetting(state);
            Assert.That(
                state.Handle(1, new TrainingActionRequest(
                    "session", 1, TrainingActionKind.Roulette,
                    RouletteRequestPayload.PackBet(5, 150), "")).Accepted,
                Is.True);

            TrainingSessionView view = TrainingSessionView.Capture(state, 1, 1, false, "");
            byte[] bytes = TrainingWireProtocol.Serialize(view);

            Assert.That(TrainingWireProtocol.TryReadState(bytes, out TrainingSessionView decoded), Is.True);
            Assert.That(decoded.Roulette.MyStakeTotal, Is.EqualTo(150));
            Assert.That(decoded.Roulette.Numbers, Is.EqualTo(view.Roulette.Numbers));
            Assert.That(decoded.Roulette.Payouts, Is.EqualTo(view.Roulette.Payouts));
            Assert.That(decoded.Roulette.MyStakes, Is.EqualTo(view.Roulette.MyStakes));
            Assert.That(decoded.Roulette.MinBet, Is.EqualTo(view.Roulette.MinBet));
        }

        /// <summary>훈련 룰렛도 <b>구역 규칙</b>을 탄다 — 시설 접근 정책이 세 시설을 함께 연다.</summary>
        [Test]
        public void 훈련_접근_정책이_세_시설을_모두_연다()
        {
            TrainingPracticeState state = NewTraining(out _);

            Assert.That(state.CanUse(1, PreparationFacility.Shop), Is.True);
            Assert.That(state.CanUse(1, PreparationFacility.Upgrade), Is.True);
            Assert.That(state.CanUse(1, PreparationFacility.Roulette), Is.True);
            Assert.That(state.CanUse(2, PreparationFacility.Roulette), Is.False);

            state.Close();
            Assert.That(state.CanUse(1, PreparationFacility.Roulette), Is.False);
        }

        // ── ⑥ Local Play / Run MVP ─────────────────────────────────────────

        private static LocalPlaySessionState StartLocalPlay(out EquipmentInventory inventory) =>
            StartLocalPlay(out inventory, out _);

        private static LocalPlaySessionState StartLocalPlay(
            out EquipmentInventory inventory, out MovablePresence presence)
        {
            var state = new LocalPlaySessionState(LocalPlaySettings.Default);
            var owned = new EquipmentInventory();
            inventory = owned;

            var stance = new MovablePresence(LocalPlayPreparationCommerce.LocalPlayerId);
            presence = stance;

            state.Commerce.Configure(
                new InventoryStub(owned, PreparationShopItemFallbackCatalog.Instance, LocalPlayPreparationCommerce.LocalPlayerId),
                PreparationShopItemFallbackCatalog.Instance,
                stance,
                MatchSceneCatalog.DefaultPreparation);

            Assert.That(state.TryStartMatch(out _), Is.True);
            Assert.That(state.TryCompleteMapLoading(out _), Is.True);
            return state;
        }

        /// <summary>Local Play 룰렛의 베팅이 열릴 때까지 돌린다.</summary>
        private static void AdvanceLocalPlayToBetting(LocalPlaySessionState state)
        {
            for (int i = 0; i < 120; i++)
            {
                if (state.Commerce.BuildRouletteState().State == RouletteState.Betting) return;
                state.Commerce.TickRoulette(LocalPlayPreparationCommerce.RouletteTicksPerSecond);
            }

            Assert.Fail("Local Play 룰렛의 베팅이 열리지 않았습니다.");
        }

        [Test]
        public void 로컬_플레이가_제품_상점_계약을_실행한다()
        {
            LocalPlaySessionState state = StartLocalPlay(out EquipmentInventory inventory);
            ShopOffersChanged offers = state.Commerce.BuildOffers();

            Assert.That(offers.Offers.Length, Is.EqualTo(PlaceholderShopCatalog.Create().Count));
            ShopOffer offer = FindOffer(EquipmentItemKind.Consumable);
            int before = state.Gold;

            ShopPurchaseOutcome outcome = state.Commerce.Purchase(offer.OfferId);

            Assert.That(outcome.HasRejection, Is.False);
            Assert.That(state.Gold, Is.EqualTo(before - offer.Price), "축복 지갑과 상점 지갑이 같아야 한다.");
            Assert.That(inventory.Snapshot.GetConsumableSlot(0).ItemId, Is.EqualTo(offer.ItemId));
        }

        [Test]
        public void 로컬_플레이_룰렛이_같은_지갑을_움직이고_준비_종료에_환불한다()
        {
            LocalPlaySessionState state = StartLocalPlay(out _, out MovablePresence presence);
            presence.Facility = PreparationFacility.Roulette;
            AdvanceLocalPlayToBetting(state);
            int before = state.Gold;

            RouletteRequestOutcome bet = state.Commerce.Bet(1, 100);

            Assert.That(bet.HasRejection, Is.False);
            Assert.That(state.Gold, Is.EqualTo(before - bet.BetPlaced.Amount));

            // 준비 단계를 떠나면 돌지 않은 판의 베팅은 전액 돌아온다 (S-GD-7VKZA5).
            state.SkipCurrentPhase();

            Assert.That(state.Gold, Is.EqualTo(before));
            Assert.That(state.Commerce.BuildRouletteState().MyStakeTotal, Is.Zero);
        }

        /// <summary>
        /// 준비 단계가 닫혀 있으면 어느 시설도 열리지 않는다 — 전투 중에 상점이 열리면 안 된다
        /// (S-GD-PVVHP9 규칙 1).
        /// </summary>
        [Test]
        public void 로컬_플레이는_전투_중에_시설을_열지_않는다()
        {
            LocalPlaySessionState state = StartLocalPlay(out _);
            Assert.That(state.Commerce.BuildInRange().Facility, Is.Not.EqualTo(PreparationFacility.None));

            state.SkipCurrentPhase();

            Assert.That(state.Commerce.BuildInRange().Facility, Is.EqualTo(PreparationFacility.None));
            Assert.That(state.Commerce.CanUse(
                LocalPlayPreparationCommerce.LocalPlayerId, PreparationFacility.Shop), Is.False);
        }

        /// <summary>
        /// 라운드마다 룰렛이 다시 열린다. <b>빠뜨리면 1라운드에만 돌고 2·3라운드는 조용히 안 열린다.</b>
        /// </summary>
        [Test]
        public void 로컬_플레이는_라운드마다_룰렛을_다시_연다()
        {
            LocalPlaySessionState state = StartLocalPlay(out _, out MovablePresence presence);
            presence.Facility = PreparationFacility.Roulette;

            for (int round = 1; round <= 2; round++)
            {
                Assert.That(state.CurrentRound, Is.EqualTo(round));
                AdvanceLocalPlayToBetting(state);
                Assert.That(state.Commerce.Bet(1, 50).HasRejection, Is.False, $"{round}라운드 베팅");

                state.SkipCurrentPhase();                       // 준비 → 전투 맵 로딩
                Assert.That(state.TryCompleteMapLoading(out _), Is.True);
                state.SkipCurrentPhase();                       // 카운트다운 → 전투
                state.TrySettleBlessingRound(round, true);
                state.SkipCurrentPhase();                       // 전투 → 전투 요약
                state.SkipCurrentPhase();                       // 요약 → 다음 준비 맵 로딩
                Assert.That(state.TryCompleteMapLoading(out _), Is.True);
            }
        }

        // ── 대역 ────────────────────────────────────────────────────────────

        private sealed class PresenceStub : IPreparationPresenceProvider
        {
            private readonly Dictionary<int, WorldPosition> byPlayer = new Dictionary<int, WorldPosition>();

            public void Place(int playerId, WorldPosition position) => byPlayer[playerId] = position;

            public bool TryGetPosition(int playerId, out WorldPosition position) =>
                byPlayer.TryGetValue(playerId, out position);
        }

        /// <summary>
        /// 지정한 시설 구역 한가운데에 서 있는 사람. <b>걸어 다니는 것을 흉내 낸다</b> —
        /// 시설마다 다른 구역이라는 규칙(S-GD-66GTWA)을 테스트가 우회하지 않게 한다.
        /// </summary>
        private sealed class MovablePresence : IPreparationPresenceProvider
        {
            private readonly int playerId;

            public MovablePresence(int playerId) => this.playerId = playerId;

            public PreparationFacility Facility { get; set; } = PreparationFacility.Shop;

            public bool TryGetPosition(int id, out WorldPosition position)
            {
                position = default;
                if (id != playerId || Facility == PreparationFacility.None) return false;

                PreparationFacilityMap map = PreparationFacilityMapCatalog.CreateDefault();
                Assert.That(map.TryGet(Facility, out PreparationFacilityArea area), Is.True);
                position = area.Center;
                return true;
            }
        }

        private sealed class InventoryStub : IShopInventoryProvider
        {
            private readonly InventoryDriverStub driver;
            private readonly int playerId;

            public InventoryStub(EquipmentInventory inventory, IEquipmentItemCatalog catalog, int playerId = Buyer)
            {
                driver = new InventoryDriverStub(inventory, catalog);
                this.playerId = playerId;
            }

            public IEquipmentInventoryDriver Driver => driver;

            public bool TryResolve(int id, out IEquipmentInventoryDriver inventory)
            {
                inventory = id == playerId ? driver : null;
                return inventory != null;
            }
        }

        private sealed class TrainingInventoryStub : IShopInventoryProvider
        {
            private readonly InventoryDriverStub driver;

            public TrainingInventoryStub(IEquipmentItemCatalog catalog) =>
                driver = new InventoryDriverStub(new EquipmentInventory(), catalog);

            public IEquipmentInventoryDriver Driver => driver;

            public bool TryResolve(int playerId, out IEquipmentInventoryDriver inventory)
            {
                inventory = playerId == 1 ? driver : null;
                return inventory != null;
            }
        }

        private sealed class InventoryDriverStub : IEquipmentInventoryDriver
        {
            private readonly EquipmentInventory inner;
            private readonly IEquipmentItemCatalog catalog;

            public InventoryDriverStub(EquipmentInventory inner, IEquipmentItemCatalog catalog)
            {
                this.inner = inner;
                this.catalog = catalog;
            }

            public EquipmentInventorySnapshot EquipmentSnapshot => inner.Snapshot;

            public EquipmentInventoryOperationResult TryAuthorityPickup(int itemId, int quantity) =>
                inner.TryPickup(itemId, quantity, catalog);

            public EquipmentInventoryOperationResult TryAuthorityDropWeapon() => inner.TryDropWeapon();

            public EquipmentInventoryOperationResult TryAuthorityUseConsumable(int slotIndex) =>
                inner.TryUseConsumable(slotIndex, catalog);

            public void ApplyPresentation(in EquipmentInventorySnapshot snapshot) =>
                inner.ApplySnapshot(snapshot);
        }
    }
}

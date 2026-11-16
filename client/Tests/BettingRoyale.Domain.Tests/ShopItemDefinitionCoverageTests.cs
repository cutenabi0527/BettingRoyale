using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.Shop;
using BettingRoyale.Data.Throwing;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// <b>진열된 물건이 실제로 인벤토리에 들어가는가</b> (S-GD-ZD76H8 · D-CLI-R69V68).
    ///
    /// <para>
    /// <b>이 파일이 없어서 놓쳤던 것.</b> 2026-09-15 Play 에서 진열 6칸 중 마력 폭발병 하나만
    /// 팔렸다. 그런데 <c>ShopPurchaseTests</c> 는 전부 초록이었다 — 거기서는 게이트에
    /// <c>PlaceholderShopItemCatalog</c>(6종을 다 아는 테스트용 목록)를 넣기 때문이다.
    /// <b>제품 경로는 전투원 인벤토리의 카탈로그를 쓰고</b>(D-CLI-R69V68) 그쪽에는 정의가
    /// 폭발병 하나뿐이었다. 즉 <b>테스트가 검증한 카탈로그와 게임이 쓰는 카탈로그가 달랐다.</b>
    /// </para>
    ///
    /// <para>
    /// 그래서 이 파일은 <b>제품이 실제로 들고 다니는 정의</b>만 본다 — 대역을 하나도 쓰지 않고
    /// 진짜 <see cref="EquipmentInventory"/> 에 넣어 본다. 지급 단계에서 <c>InvalidItem</c> 이
    /// 나오면 여기서 빨간불이 켜진다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>여기서 검증하는 것은 "정의가 있다" 까지다.</b> 회복량·이동 속도 같은 효과는 아직
    /// 구현되지 않았고(S-GD-ZD76H8 "반영 위치" 참조) 이 테스트는 그것을 주장하지 않는다.
    /// </para>
    ///
    /// <para>
    /// <b>짝 테스트</b>: 실제 <c>EquipmentItemCatalogBehaviour</c> 가 같은 답을 주는지는
    /// EditMode 쪽 <c>ShopItemDefinitionRuntimeTests</c> 가 본다 — 여기서는 <see cref="Runtime"/>
    /// 가 그 폴백 순서를 <b>복제</b>하므로, 이 파일만으로는 배선이 끊긴 것을 잡지 못한다.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ShopItemDefinitionCoverageTests
    {
        /// <summary>
        /// 제품 런타임이 아이템을 찾는 순서를 그대로 옮긴 것
        /// (<c>EquipmentItemCatalogBehaviour</c>: 직렬화 정의 → 폭발병 → 상점 폴백).
        /// 직렬화 정의는 Unity 에셋이라 여기서는 비어 있는 셈이다.
        /// </summary>
        private static readonly CompositeCatalog Runtime = new CompositeCatalog();

        // ── 진열과 정의가 어긋나지 않는다 ────────────────────────────────

        [Test]
        public void 진열된_모든_칸이_런타임_정의를_가진다()
        {
            foreach (ShopOffer offer in PlaceholderShopCatalog.Create().Offers)
            {
                Assert.That(Runtime.TryGet(offer.ItemId, out EquipmentItemDefinition definition), Is.True,
                    $"칸 {offer.OfferId} 의 아이템 {offer.ItemId} 에 장비 정의가 없다 — 이 칸은 팔리지 않는다");

                Assert.That(definition.IsValid, Is.True,
                    $"아이템 {offer.ItemId} 의 정의가 불완전하다 (VisualId 가 0 이면 IsValid 가 false 다)");

                Assert.That(definition.Kind,
                    Is.AnyOf(EquipmentItemKind.Consumable, EquipmentItemKind.Accessory),
                    $"아이템 {offer.ItemId} 는 상점이 팔 수 있는 종류가 아니다 (S-GD-PVVHP9 규칙 2)");
            }
        }

        [Test]
        public void 진열된_모든_칸이_실제로_인벤토리에_들어간다()
        {
            // 거절이 나던 바로 그 자리다 — 사거리·골드를 다 통과한 뒤의 마지막 지급 단계.
            foreach (ShopOffer offer in PlaceholderShopCatalog.Create().Offers)
            {
                // 칸마다 새 인벤토리로 본다. 앞 칸이 채워 둔 소모품 칸 때문에 뒤 칸이
                // 실패하면 "정의가 없다" 와 구별이 안 된다.
                var inventory = new EquipmentInventory();

                EquipmentInventoryOperationResult result =
                    inventory.TryPickup(offer.ItemId, offer.Quantity, Runtime);

                Assert.That(result.Succeeded, Is.True,
                    $"칸 {offer.OfferId} (아이템 {offer.ItemId}) 지급 실패: {result.Failure}");
            }
        }

        // ── 보유 한도 (S-GD-ZD76H8) ──────────────────────────────────────

        [TestCase(PlaceholderShopCatalog.HealthPotionItemId, 3, TestName = "체력_물약은_3개")]
        [TestCase(PlaceholderShopCatalog.SwiftnessPotionItemId, 2, TestName = "신속_물약은_2개")]
        public void 소모품_보유_한도는_스펙대로다(int itemId, int expected)
        {
            Assert.That(Runtime.TryGetCarryLimit(itemId, out int limit), Is.True);
            Assert.That(limit, Is.EqualTo(expected));
        }

        [TestCase(PlaceholderShopCatalog.HealthPotionItemId, 3, TestName = "체력_물약_4개째_거절")]
        [TestCase(PlaceholderShopCatalog.SwiftnessPotionItemId, 2, TestName = "신속_물약_3개째_거절")]
        public void 한도를_넘기면_지급이_거절된다(int itemId, int limit)
        {
            var inventory = new EquipmentInventory();

            for (int i = 0; i < limit; i++)
            {
                Assert.That(inventory.TryPickup(itemId, 1, Runtime).Succeeded, Is.True, $"{i + 1}개째");
            }

            EquipmentInventoryOperationResult overflow = inventory.TryPickup(itemId, 1, Runtime);

            Assert.That(overflow.Succeeded, Is.False);
            Assert.That(overflow.Failure, Is.EqualTo(EquipmentInventoryFailure.CarryLimitExceeded));
        }

        [Test]
        public void 한도만큼_한_칸에_쌓여서_소모품_칸을_하나만_쓴다()
        {
            // "한 칸에 쌓임 = 보유 한도" 라 두 물약을 다 들어도 칸이 2개면 충분하다
            // (S-GD-ZD76H8). MaxStack 을 1 로 적으면 물약 하나가 칸 3개를 다 먹는다.
            var inventory = new EquipmentInventory();

            for (int i = 0; i < 3; i++)
            {
                Assert.That(
                    inventory.TryPickup(PlaceholderShopCatalog.HealthPotionItemId, 1, Runtime).Succeeded,
                    Is.True);
            }

            for (int i = 0; i < 2; i++)
            {
                Assert.That(
                    inventory.TryPickup(PlaceholderShopCatalog.SwiftnessPotionItemId, 1, Runtime).Succeeded,
                    Is.True);
            }

            EquipmentInventorySnapshot snapshot = inventory.Snapshot;

            Assert.That(snapshot.ConsumableSlot0.Count, Is.EqualTo(3), "체력 물약 3개가 한 칸에");
            Assert.That(snapshot.ConsumableSlot1.Count, Is.EqualTo(2), "신속 물약 2개가 한 칸에");
            Assert.That(snapshot.ConsumableSlot2.IsEmpty, Is.True, "세 번째 칸은 남아 있어야 한다");
        }

        [TestCase(PlaceholderShopCatalog.RingOfStrengthItemId, TestName = "힘의_반지")]
        [TestCase(PlaceholderShopCatalog.NecklaceOfHasteItemId, TestName = "신속의_목걸이")]
        [TestCase(PlaceholderShopCatalog.AmuletOfWardingItemId, TestName = "수호의_부적")]
        public void 장신구는_보유_한도를_답하지_않는다(int itemId)
        {
            // 한도 0 을 지어내면 EquipmentInventory 가 "들 수 없는 물건"(InvalidItem)으로 읽어
            // 장신구를 통째로 못 사게 된다. "모른다" 와 "0" 은 다른 답이다.
            Assert.That(Runtime.TryGetCarryLimit(itemId, out int limit), Is.False);
            Assert.That(limit, Is.EqualTo(0));
        }

        [Test]
        public void 장신구를_새로_사면_갖고_있던_것이_사라진다()
        {
            // S-GD-B7Q44N — 한 개만 지닌다. 한도가 아니라 교체로 막는다.
            var inventory = new EquipmentInventory();

            inventory.TryPickup(PlaceholderShopCatalog.RingOfStrengthItemId, 1, Runtime);
            EquipmentInventoryOperationResult swap =
                inventory.TryPickup(PlaceholderShopCatalog.AmuletOfWardingItemId, 1, Runtime);

            Assert.That(swap.Succeeded, Is.True);
            Assert.That(swap.DestroyedAccessoryItemId,
                Is.EqualTo(PlaceholderShopCatalog.RingOfStrengthItemId));
            Assert.That(inventory.Snapshot.AccessoryItemId,
                Is.EqualTo(PlaceholderShopCatalog.AmuletOfWardingItemId));
        }

        // ── 소유 경계 ───────────────────────────────────────────────────

        [Test]
        public void 폭발병은_상점_폴백이_아니라_투척_카탈로그가_답한다()
        {
            // 두 곳이 같은 번호를 알면 어느 쪽이 이기는지가 호출 순서에 숨는다.
            Assert.That(
                RecommendedShopItemCatalog.Instance.TryGet(
                    RecommendedMagicBottleCatalog.MagicBottleItemId, out _),
                Is.False,
                "2103 은 RecommendedMagicBottleCatalog 의 것이다");

            Assert.That(
                RecommendedShopItemCatalog.Instance.TryGetCarryLimit(
                    RecommendedMagicBottleCatalog.MagicBottleItemId, out _),
                Is.False);
        }

        [Test]
        public void 상점_폴백은_진열에_없는_번호를_지어내지_않는다()
        {
            Assert.That(RecommendedShopItemCatalog.Instance.TryGet(9999, out _), Is.False);
            Assert.That(RecommendedShopItemCatalog.Instance.TryGet(1101, out _), Is.False,
                "무기는 상점이 팔지 않는다");
        }

        // ── 대역이 아니라 제품 조합이다 ──────────────────────────────────

        /// <summary>
        /// <b>제품 폴백 순서의 복제.</b> <c>EquipmentItemCatalogBehaviour</c> 는 UnityEngine 에
        /// 의존해 여기서 컴파일할 수 없다 — 그래서 <b>순서만</b> 옮겨 적었다.
        /// ⚠️ 그쪽 배선이 끊기면 이 파일은 여전히 초록이다. EditMode 짝 테스트가 그 몫을 본다.
        /// </summary>
        private sealed class CompositeCatalog : IEquipmentItemCatalog, IEquipmentItemCarryLimitCatalog
        {
            private readonly RecommendedMagicBottleCatalog throwing = new RecommendedMagicBottleCatalog();

            public bool TryGet(int itemId, out EquipmentItemDefinition definition) =>
                throwing.TryGet(itemId, out definition)
                || RecommendedShopItemCatalog.Instance.TryGet(itemId, out definition);

            public bool TryGetCarryLimit(int itemId, out int totalCarryLimit) =>
                throwing.TryGetCarryLimit(itemId, out totalCarryLimit)
                || RecommendedShopItemCatalog.Instance.TryGetCarryLimit(itemId, out totalCarryLimit);
        }
    }
}

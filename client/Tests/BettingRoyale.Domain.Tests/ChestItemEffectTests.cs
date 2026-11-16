using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Chest;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.Shop;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 상자 아이템 12종의 효과·장비 정의가 확정 스펙 그대로인지 못 박는다 (S-GD-DDXPX9 · S-GD-RVVB4H).
    /// </summary>
    public class ChestItemEffectTests
    {
        private static IReadOnlyList<ConsumableEffect> Effects(int itemId)
        {
            Assert.That(ShopItemEffectCatalog.TryGetConsumableEffects(itemId, out IReadOnlyList<ConsumableEffect> effects),
                Is.True, $"{itemId} 의 효과가 정의되지 않았다.");
            return effects;
        }

        private static ConsumableEffect Single(int itemId)
        {
            IReadOnlyList<ConsumableEffect> effects = Effects(itemId);
            Assert.That(effects.Count, Is.EqualTo(1));
            return effects[0];
        }

        // ── 효과 수치 ───────────────────────────────────────────────────────

        [Test]
        public void 큰_체력_물약은_50_회복()
        {
            ConsumableEffect e = Single(RecommendedChestItemCatalog.LargeHealthPotionItemId);
            Assert.That(e.Kind, Is.EqualTo(ConsumableEffectKind.InstantHeal));
            Assert.That(e.Value, Is.EqualTo(50f));
        }

        [Test]
        public void 질주_물약은_4초간_이동_30퍼센트()
        {
            ConsumableEffect e = Single(RecommendedChestItemCatalog.SprintPotionItemId);
            Assert.That(e.Kind, Is.EqualTo(ConsumableEffectKind.TimedMoveSpeed));
            Assert.That(e.Value, Is.EqualTo(0.30f).Within(1e-6), "증감분이다 — 1.30 이 아니다.");
            Assert.That(e.DurationSeconds, Is.EqualTo(4f));
        }

        [Test]
        public void 투지_물약은_6초간_피해_20퍼센트()
        {
            ConsumableEffect e = Single(RecommendedChestItemCatalog.FuryPotionItemId);
            Assert.That(e.Kind, Is.EqualTo(ConsumableEffectKind.TimedDamageBonus));
            Assert.That(e.Value, Is.EqualTo(0.20f).Within(1e-6));
            Assert.That(e.DurationSeconds, Is.EqualTo(6f));
        }

        [Test]
        public void 광란_물약은_6초간_공격_속도_30퍼센트()
        {
            ConsumableEffect e = Single(RecommendedChestItemCatalog.FrenzyPotionItemId);
            Assert.That(e.Kind, Is.EqualTo(ConsumableEffectKind.TimedAttackSpeed));
            Assert.That(e.Value, Is.EqualTo(0.30f).Within(1e-6));
            Assert.That(e.DurationSeconds, Is.EqualTo(6f));
        }

        [Test]
        public void 철벽_물약은_6초간_받는_피해_25퍼센트_감소()
        {
            ConsumableEffect e = Single(RecommendedChestItemCatalog.BulwarkPotionItemId);
            Assert.That(e.Kind, Is.EqualTo(ConsumableEffectKind.TimedDamageTaken));
            Assert.That(e.Value, Is.EqualTo(0.25f).Within(1e-6), "감소분이다 — 곱할 배율 0.75 가 아니다.");
            Assert.That(e.DurationSeconds, Is.EqualTo(6f));
        }

        [Test]
        public void 보호막_부적은_30을_10초()
        {
            ConsumableEffect e = Single(RecommendedChestItemCatalog.ShieldCharmItemId);
            Assert.That(e.Kind, Is.EqualTo(ConsumableEffectKind.Shield));
            Assert.That(e.Value, Is.EqualTo(30f));
            Assert.That(e.DurationSeconds, Is.EqualTo(10f));
        }

        [Test]
        public void 대시_충전석은_즉시_충전과_3초간_재충전_절반_두_가지다()
        {
            IReadOnlyList<ConsumableEffect> effects = Effects(RecommendedChestItemCatalog.DashStoneItemId);

            Assert.That(effects.Select(e => e.Kind), Is.EquivalentTo(new[]
            {
                ConsumableEffectKind.DashRefill, ConsumableEffectKind.TimedDashRecharge,
            }));
            ConsumableEffect recharge = effects.Single(e => e.Kind == ConsumableEffectKind.TimedDashRecharge);
            Assert.That(recharge.Value, Is.EqualTo(0.5f));
            Assert.That(recharge.DurationSeconds, Is.EqualTo(3f));
        }

        [Test]
        public void 재생_물약은_8초간_초당_5()
        {
            ConsumableEffect e = Single(RecommendedChestItemCatalog.RegenerationPotionItemId);
            Assert.That(e.Kind, Is.EqualTo(ConsumableEffectKind.Regeneration));
            Assert.That(e.Value, Is.EqualTo(5f), "초당 6 에서 5 로 하향했다 — 총 40 이다.");
            Assert.That(e.DurationSeconds, Is.EqualTo(8f));
        }

        [Test]
        public void 프로틴_쉐이크는_10초간_방어력7_공격력3()
        {
            IReadOnlyList<ConsumableEffect> effects = Effects(RecommendedChestItemCatalog.ProteinShakeItemId);

            Assert.That(effects.Select(e => e.Kind), Is.EquivalentTo(new[]
            {
                ConsumableEffectKind.TimedDefense, ConsumableEffectKind.TimedAttackPower,
            }));

            ConsumableEffect defense = effects.Single(e => e.Kind == ConsumableEffectKind.TimedDefense);
            Assert.That(defense.Value, Is.EqualTo(7f), "고정 +7 이다 — 7% 가 아니다.");
            Assert.That(defense.DurationSeconds, Is.EqualTo(10f));

            ConsumableEffect attack = effects.Single(e => e.Kind == ConsumableEffectKind.TimedAttackPower);
            Assert.That(attack.Value, Is.EqualTo(3f), "고정 +3 이다 — 3% 가 아니다.");
            Assert.That(attack.DurationSeconds, Is.EqualTo(10f));
        }

        [Test]
        public void 프로틴_쉐이크는_장신구_두_개와_같은_단위다()
        {
            // 수호의 부적 방어력 · 힘의 반지(원거리) 공격력과 같은 단위(고정 수치)다.
            // 한쪽만 고쳐서 "장신구는 고정, 물약은 퍼센트" 로 갈리는 것을 막는다.
            // 부적은 +5 로 내려갔고 쉐이크는 +7 을 유지한다 — 값이 같다는 연결은 S-GD-2YQQG4 가 끊었다.
            Assert.That(ShopItemEffectCatalog.ProteinShakeDefense, Is.EqualTo(7f));
            Assert.That(ShopItemEffectCatalog.AmuletDefense, Is.EqualTo(5f));
            Assert.That(ShopItemEffectCatalog.ProteinShakeAttackPower,
                Is.EqualTo(ShopItemEffectCatalog.RingRangedAttackPower));
        }

        [Test]
        public void 무적_부적은_1초()
        {
            ConsumableEffect e = Single(RecommendedChestItemCatalog.InvulnerabilityCharmItemId);
            Assert.That(e.Kind, Is.EqualTo(ConsumableEffectKind.Invulnerable));
            Assert.That(e.DurationSeconds, Is.EqualTo(1f));
        }

        [Test]
        public void 상점_물약_효과는_그대로다()
        {
            // 상자에서도 나오지만 같은 아이템이다. 상자 작업이 상점 수치를 건드리면 안 된다 (S-GD-YMKKDJ).
            Assert.That(Single(PlaceholderShopCatalog.HealthPotionItemId).Value, Is.EqualTo(25f));
            ConsumableEffect swift = Single(PlaceholderShopCatalog.SwiftnessPotionItemId);
            Assert.That(swift.Value, Is.EqualTo(0.10f).Within(1e-6));
            Assert.That(swift.DurationSeconds, Is.EqualTo(5f));
        }

        [Test]
        public void 과잉_보상은_회복25_보호막10_공속15_이속15이고_피해_증가는_없다()
        {
            IReadOnlyList<ConsumableEffect> overflow = ShopItemEffectCatalog.ChestOverflowEffects;

            Assert.That(overflow.Single(e => e.Kind == ConsumableEffectKind.InstantHeal).Value, Is.EqualTo(25f));
            ConsumableEffect shield = overflow.Single(e => e.Kind == ConsumableEffectKind.Shield);
            Assert.That(shield.Value, Is.EqualTo(10f));
            Assert.That(shield.DurationSeconds, Is.EqualTo(6f));
            ConsumableEffect attack = overflow.Single(e => e.Kind == ConsumableEffectKind.TimedAttackSpeed);
            Assert.That(attack.Value, Is.EqualTo(0.15f).Within(1e-6));
            Assert.That(attack.DurationSeconds, Is.EqualTo(6f));
            ConsumableEffect move = overflow.Single(e => e.Kind == ConsumableEffectKind.TimedMoveSpeed);
            Assert.That(move.Value, Is.EqualTo(0.15f).Within(1e-6));
            Assert.That(move.DurationSeconds, Is.EqualTo(6f));

            Assert.That(overflow.Any(e => e.Kind == ConsumableEffectKind.TimedDamageBonus), Is.False,
                "과잉 보상에 피해 증가는 넣지 않는다 — '버티면서 싸워라' 쪽이다.");
        }

        // ── 칸을 쓰는가 ─────────────────────────────────────────────────────

        // ── 추첨표 ↔ 정의 짝 ────────────────────────────────────────────────

        [Test]
        public void 추첨표의_모든_아이템은_효과가_정의돼_있다()
        {
            foreach (ChestLootEntry entry in PlaceholderChestLootTable.Entries)
            {
                Assert.That(ShopItemEffectCatalog.TryGetConsumableEffects(entry.ItemId, out _), Is.True,
                    $"{entry.ItemId} 가 뽑히면 아무 일도 안 일어난다.");
            }
        }

        [Test]
        public void 추첨표의_모든_아이템은_인벤토리가_아는_소모품이다()
        {
            // 인벤토리 폴백 체인이 모르면 지급이 InvalidItem 으로 떨어지고, 그건 '받지 못함' 이 되어
            // 매번 과잉 보상으로 바뀐다. 아이템을 영영 못 받는다.
            foreach (ChestLootEntry entry in PlaceholderChestLootTable.Entries)
            {
                Assert.That(PreparationShopItemFallbackCatalog.Instance.TryGet(entry.ItemId, out EquipmentItemDefinition def),
                    Is.True, $"{entry.ItemId} 의 장비 정의가 없다.");
                Assert.That(def.Kind, Is.EqualTo(EquipmentItemKind.Consumable));
                Assert.That(PreparationShopItemFallbackCatalog.Instance.TryGetCarryLimit(entry.ItemId, out _), Is.True,
                    $"{entry.ItemId} 의 보유 한도가 없다.");
            }
        }

        [Test]
        public void 상자_전용품은_한_칸에_1개_보유_한도_1()
        {
            foreach (EquipmentItemDefinition def in RecommendedChestItemCatalog.All)
            {
                Assert.That(def.MaxStack, Is.EqualTo(1), $"{def.ItemId}");
                Assert.That(RecommendedChestItemCatalog.Instance.TryGetCarryLimit(def.ItemId, out int limit), Is.True);
                Assert.That(limit, Is.EqualTo(1), $"{def.ItemId}");
            }
        }

        [Test]
        public void 상점_진열_목록에는_상자_전용품이_섞이지_않는다()
        {
            IReadOnlyCollection<int> shopIds = PreparationShopItemFallbackCatalog.ItemIds;

            foreach (EquipmentItemDefinition def in RecommendedChestItemCatalog.All)
            {
                Assert.That(shopIds.Contains(def.ItemId), Is.False,
                    $"{def.ItemId} 가 상점 진열 목록에 섞이면 상점 정의·진열 짝 검사가 깨진다.");
            }
        }
        [Test]
        public void 폐기한_두_아이템의_효과는_남아_있지_않다()
        {
            // S-GD-DDXPX9 §7 — 골드 주머니(2113)·재추첨권(2114). 효과가 남아 있으면 번호를 재사용한 순간 되살아난다.
            Assert.That(ShopItemEffectCatalog.TryGetConsumableEffects(2113, out _), Is.False);
            Assert.That(ShopItemEffectCatalog.TryGetConsumableEffects(2114, out _), Is.False);
            Assert.That(PreparationShopItemFallbackCatalog.Instance.TryGet(2113, out _), Is.False);
            Assert.That(PreparationShopItemFallbackCatalog.Instance.TryGet(2114, out _), Is.False);
        }

        [Test]
        public void 상자_전용품은_10종이다()
        {
            Assert.That(RecommendedChestItemCatalog.All.Length, Is.EqualTo(10));
        }

    }
}

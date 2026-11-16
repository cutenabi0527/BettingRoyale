using System;
using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Chest;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Shop;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 소모품 효과가 HUD 효과 칸으로 올라가는 모양 — 남은 시간과 비율 (S-UX-3ESB5T). 건 아이템을 모르면 효과 칸마다
    /// 하나(S-UX-7DRFJQ), 알면 그 아이템 그림 하나(S-UX-XEC4CX).
    /// </summary>
    public class ConsumableHudEffectsTests
    {
        private const int Source = 10_001;

        private ConsumableBuffState buff;
        private List<HudStatusEffect> effects;

        [SetUp]
        public void SetUp()
        {
            buff = new ConsumableBuffState();
            effects = new List<HudStatusEffect>();
        }

        private void Capture(float now) => ConsumableHudEffects.Capture(buff, now, Source, effects);

        [Test]
        public void 아무것도_안_마셨으면_칸이_없다()
        {
            Capture(0f);

            Assert.That(effects, Is.Empty);
        }

        [Test]
        public void 켜진_효과_칸마다_남은_시간과_비율을_싣는다()
        {
            buff.Apply(ConsumableEffect.TimedDamageBonus(ShopItemEffectCatalog.FuryPotionDamageBonus,
                ShopItemEffectCatalog.FuryPotionSeconds), now: 0f);

            Capture(1.5f);

            Assert.That(effects.Count, Is.EqualTo(1));
            HudStatusEffect fury = effects[0];
            Assert.That(fury.EffectId, Is.EqualTo("item-damage-bonus"));
            Assert.That(fury.SourceId, Is.EqualTo(Source));
            Assert.That(fury.Category, Is.EqualTo(HudStatusEffectCategory.GeneralBuff));
            Assert.That(fury.Icon, Is.EqualTo(ConsumableHudEffects.ImpactIcon));
            Assert.That(fury.Remaining, Is.EqualTo(4.5f).Within(0.0001f));
            Assert.That(fury.NormalizedRemaining, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(fury.IsUnlimited, Is.False);
            Assert.That(fury.Stacks, Is.EqualTo(1));
        }

        [Test]
        public void 근원을_모르는_같은_칸은_하나이고_다시_받으면_새_시간을_보인다()
        {
            // 건 아이템을 모르는 효과(근원 0)는 (칸, 근원) 이 같아 새로 채운다 — 칸 하나가 5초로 바뀐다.
            buff.Apply(ConsumableEffect.TimedMoveSpeed(0.30f, 4f), now: 0f);
            buff.Apply(ConsumableEffect.TimedMoveSpeed(0.10f, 5f), now: 1f);

            Capture(1f);

            Assert.That(effects.Count, Is.EqualTo(1));
            Assert.That(effects[0].EffectId, Is.EqualTo("item-move-speed"));
            Assert.That(effects[0].Remaining, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(effects[0].NormalizedRemaining, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void 프로틴_쉐이크는_방어력과_공격력_두_칸이_뜬다()
        {
            Assert.That(ShopItemEffectCatalog.TryGetConsumableEffects(RecommendedChestItemCatalog.ProteinShakeItemId,
                out IReadOnlyList<ConsumableEffect> shake), Is.True);
            foreach (ConsumableEffect effect in shake)
            {
                buff.Apply(effect, now: 0f);
            }

            Capture(0f);

            Assert.That(effects.Select(e => e.EffectId),
                Is.EquivalentTo(new[] { "item-defense", "item-attack-power" }));
        }

        [Test]
        public void 과잉_보상은_보호막_공격_속도_이동_속도_세_칸이다()
        {
            // 즉시 회복은 남은 시간이 없어 칸이 없다.
            foreach (ConsumableEffect effect in ShopItemEffectCatalog.ChestOverflowEffects)
            {
                buff.Apply(effect, now: 0f);
            }

            Capture(0f);

            Assert.That(effects.Select(e => e.EffectId),
                Is.EquivalentTo(new[] { "item-shield", "item-attack-speed", "item-move-speed" }));
            Assert.That(effects.Single(e => e.EffectId == "item-shield").Category,
                Is.EqualTo(HudStatusEffectCategory.Ability), "축복 보호막과 같은 분류 색이다.");
        }

        [Test]
        public void 보호막이_다_깎이면_칸이_사라진다()
        {
            buff.Apply(ConsumableEffect.Shield(10f, 6f), now: 0f);
            buff.AbsorbWithShield(10f);

            Capture(0f);

            Assert.That(effects, Is.Empty);
        }

        [Test]
        public void 시간이_다_된_칸은_올리지_않는다()
        {
            buff.Apply(ConsumableEffect.Invulnerable(1f), now: 0f);

            Capture(1f);

            Assert.That(effects, Is.Empty, "만료 처리(Tick) 전이라도 남은 시간이 0 이면 자리를 차지하지 않는다.");
        }

        [Test]
        public void 재생_물약은_남은_틱이_다_나올_때까지를_남은_시간으로_보인다()
        {
            buff.Apply(ConsumableEffect.Regeneration(5f, 8f), now: 0f);
            buff.TakeRegenerationHeal(3.2f); // 1·2·3초 틱이 나갔다. 다음은 4초, 남은 틱 5개.

            Capture(3.2f);

            HudStatusEffect regen = effects.Single();
            Assert.That(regen.EffectId, Is.EqualTo("item-regeneration"));
            Assert.That(regen.Remaining, Is.EqualTo(0.8f + 4f).Within(0.0001f));
            Assert.That(regen.NormalizedRemaining, Is.EqualTo(4.8f / 8f).Within(0.0001f));
        }

        [Test]
        public void 받은_목록을_비우지_않고_뒤에_덧붙인다()
        {
            // 축복 효과 뒤에 이어 붙어 한 줄이 된다 — 비우면 축복 칸이 사라진다.
            effects.Add(new HudStatusEffect(7, Source, "shield", HudStatusEffectCategory.Ability, 3f, 1f));
            buff.Apply(ConsumableEffect.Shield(30f, 10f), now: 0f);

            Capture(0f);

            Assert.That(effects.Select(e => e.EffectId), Is.EqualTo(new[] { "shield", "item-shield" }),
                "축복 보호막과 아이템 보호막은 따로 쌓이는 별개 효과라 칸도 따로다.");
        }

        [Test]
        public void 모든_효과_칸은_고유한_키와_공용_시트_안의_아이콘을_갖는다()
        {
            var ids = new HashSet<string>();
            for (int i = 0; i < ConsumableBuffState.ChannelCount; i++)
            {
                var channel = (ConsumableBuffChannel)i;
                string id = ConsumableHudEffects.EffectId(channel);
                Assert.That(ids.Add(id), Is.True, $"{channel} 의 키 {id} 가 겹친다.");
                Assert.That(id, Does.StartWith("item-"), "축복 효과 키와 겹치지 않게 접두어를 붙인다.");
                Assert.That(ConsumableHudEffects.Icon(channel), Is.InRange(0, 15), "공용 4×4 시트 안이어야 한다.");
            }
        }

        // ── 건 아이템을 알면 아이템 그림 한 칸 (S-UX-XEC4CX) ───────────────────────

        private void Drink(int itemId, float now)
        {
            Assert.That(ShopItemEffectCatalog.TryGetConsumableEffects(itemId, out IReadOnlyList<ConsumableEffect> list),
                Is.True, $"{itemId} 의 효과가 없다.");
            foreach (ConsumableEffect effect in list)
            {
                buff.Apply(effect, now, itemId);
            }
        }

        [Test]
        public void 건_아이템을_알면_효과_칸이_여럿이어도_그_아이템_그림_한_칸이다()
        {
            Drink(RecommendedChestItemCatalog.ProteinShakeItemId, 0f);

            Capture(2.5f);

            HudStatusEffect shake = effects.Single();
            Assert.That(shake.Icon, Is.EqualTo(ItemHudIcons.ChestItemArtOffset + 9), "쉐이커 컵 칸");
            Assert.That(shake.EffectId, Is.EqualTo("item-2115"));
            Assert.That(shake.Category, Is.EqualTo(HudStatusEffectCategory.GeneralBuff));
            Assert.That(shake.Remaining, Is.EqualTo(ShopItemEffectCatalog.ProteinShakeSeconds - 2.5f).Within(0.0001f));
            Assert.That(shake.NormalizedRemaining, Is.EqualTo(0.75f).Within(0.0001f));
        }

        [Test]
        public void 아이템_묶음의_남은_시간은_가장_늦게_끝나는_칸의_것이다()
        {
            // 대시 충전석: 즉시 충전(칸 없음) + 재충전 절반 3초. 여기에 같은 근원으로 5초짜리 칸을 하나 더 얹는다.
            buff.Apply(ConsumableEffect.TimedDashRecharge(0.5f, 3f), 0f, RecommendedChestItemCatalog.DashStoneItemId);
            buff.Apply(ConsumableEffect.TimedMoveSpeed(0.1f, 5f), 0f, RecommendedChestItemCatalog.DashStoneItemId);

            Capture(1f);

            HudStatusEffect stone = effects.Single();
            Assert.That(stone.Remaining, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(stone.NormalizedRemaining, Is.EqualTo(4f / 5f).Within(0.0001f));
        }

        [Test]
        public void 과잉_보상은_상자_그림_한_칸이고_보호막_분류다()
        {
            foreach (ConsumableEffect effect in ShopItemEffectCatalog.ChestOverflowEffects)
            {
                buff.Apply(effect, 0f, ShopItemEffectCatalog.ChestOverflowSourceId);
            }

            Capture(0f);

            HudStatusEffect overflow = effects.Single();
            Assert.That(overflow.Icon, Is.EqualTo(ItemHudIcons.TreasureChestIcon));
            Assert.That(overflow.EffectId, Is.EqualTo(ConsumableHudEffects.ChestOverflowEffectId));
            Assert.That(overflow.Category, Is.EqualTo(HudStatusEffectCategory.Ability));
        }

        [Test]
        public void 질주_도중_신속을_마시면_두_그림이_함께_뜨고_각자_끝난다()
        {
            // 서로 다른 물약은 덮어쓰지 않는다 (S-GD-G3QJCW).
            Drink(RecommendedChestItemCatalog.SprintPotionItemId, 0f);
            Drink(PlaceholderShopCatalog.SwiftnessPotionItemId, 1f);

            Capture(1f);

            Assert.That(effects.Select(e => e.Icon), Is.EquivalentTo(new[]
            {
                ItemHudIcons.IconIndex(RecommendedChestItemCatalog.SprintPotionItemId), ItemHudIcons.SwiftnessPotionIcon,
            }));
            HudStatusEffect swift = effects.Single(e => e.Icon == ItemHudIcons.SwiftnessPotionIcon);
            Assert.That(swift.Remaining, Is.EqualTo(ShopItemEffectCatalog.SwiftnessPotionSeconds).Within(0.0001f));

            float swiftEnds = 1f + ShopItemEffectCatalog.SwiftnessPotionSeconds;
            float sprintEnds = ShopItemEffectCatalog.SprintPotionSeconds;
            float later = Math.Min(swiftEnds, sprintEnds) + 0.01f;
            buff.Tick(later);
            effects.Clear();
            Capture(later);
            Assert.That(effects.Count, Is.EqualTo(1), "먼저 끝난 물약의 그림만 사라진다.");
        }

        [Test]
        public void 과잉_보상_도중_질주를_마시면_두_칸이다()
        {
            // 서로 덮어쓰지 않으므로 과잉 보상 묶음(보호막·공속·이속)과 질주가 따로 뜬다.
            foreach (ConsumableEffect effect in ShopItemEffectCatalog.ChestOverflowEffects)
            {
                buff.Apply(effect, 0f, ShopItemEffectCatalog.ChestOverflowSourceId);
            }

            Drink(RecommendedChestItemCatalog.SprintPotionItemId, 1f);

            Capture(1f);

            Assert.That(effects.Select(e => e.Icon), Is.EquivalentTo(new[]
            {
                ItemHudIcons.TreasureChestIcon, ItemHudIcons.ChestItemArtOffset + 1,
            }));
        }

        [TestCase(RecommendedChestItemCatalog.InvulnerabilityCharmItemId, HudStatusEffectCategory.Condition)]
        [TestCase(RecommendedChestItemCatalog.ShieldCharmItemId, HudStatusEffectCategory.Ability)]
        [TestCase(RecommendedChestItemCatalog.FuryPotionItemId, HudStatusEffectCategory.GeneralBuff)]
        [TestCase(RecommendedChestItemCatalog.RegenerationPotionItemId, HudStatusEffectCategory.GeneralBuff)]
        public void 아이템_묶음의_테두리_색은_무적_보호막_일반_순이다(int itemId, HudStatusEffectCategory expected)
        {
            Drink(itemId, 0f);

            Capture(0.25f);

            Assert.That(effects.Single().Category, Is.EqualTo(expected));
        }

        [Test]
        public void 지속_효과가_있는_상자_아이템은_전부_자기_그림으로_뜬다()
        {
            // 즉발만 있는 큰 체력 물약은 칸이 없다. 나머지 9종은 각자 시트의 자기 칸.
            for (int i = 0; i < RecommendedChestItemCatalog.All.Length; i++)
            {
                int itemId = RecommendedChestItemCatalog.All[i].ItemId;
                buff.Clear();
                effects.Clear();
                Drink(itemId, 0f);

                Capture(0.25f);

                if (itemId == RecommendedChestItemCatalog.LargeHealthPotionItemId)
                {
                    Assert.That(effects, Is.Empty);
                    continue;
                }

                Assert.That(effects.Single().Icon, Is.EqualTo(ItemHudIcons.ChestItemArtOffset + i), $"{itemId}");
            }
        }

        [Test]
        public void 아이템_번호표는_인벤토리의_예전_그림을_지키고_상자_아이템만_새_시트로_보낸다()
        {
            Assert.That(ItemHudIcons.IconIndex(1101), Is.EqualTo(0));
            Assert.That(ItemHudIcons.IconIndex(1104), Is.EqualTo(1));
            Assert.That(ItemHudIcons.IconIndex(PlaceholderShopCatalog.HealthPotionItemId), Is.EqualTo(2));
            Assert.That(ItemHudIcons.IconIndex(PlaceholderShopCatalog.SwiftnessPotionItemId), Is.EqualTo(3));
            Assert.That(ItemHudIcons.IconIndex(PlaceholderShopCatalog.ThrowingBombItemId), Is.EqualTo(4));
            Assert.That(ItemHudIcons.IconIndex(PlaceholderShopCatalog.RingOfStrengthItemId), Is.EqualTo(5));
            Assert.That(ItemHudIcons.IconIndex(0), Is.EqualTo(ItemHudIcons.None));

            var seen = new HashSet<int>();
            for (int i = 0; i < RecommendedChestItemCatalog.All.Length; i++)
            {
                int icon = ItemHudIcons.IconIndex(RecommendedChestItemCatalog.All[i].ItemId);
                Assert.That(icon, Is.EqualTo(ItemHudIcons.ChestItemArtOffset + i));
                Assert.That(seen.Add(icon), Is.True);
            }

            Assert.That(RecommendedChestItemCatalog.All.Length, Is.LessThanOrEqualTo(ItemHudIcons.ChestItemArtCells));
        }

        [Test]
        public void 장신구는_반지만_공용_그림이고_목걸이와_부적은_장신구_시트다()
        {
            // S-UX-3XAJ35 — 힘의 반지는 기존 그림 그대로, 나머지 둘은 새 그림.
            Assert.That(ItemHudIcons.IconIndex(PlaceholderShopCatalog.RingOfStrengthItemId), Is.EqualTo(ItemHudIcons.RingIcon));
            Assert.That(ItemHudIcons.IconIndex(PlaceholderShopCatalog.NecklaceOfHasteItemId),
                Is.EqualTo(ItemHudIcons.AccessoryArtOffset + 0));
            Assert.That(ItemHudIcons.IconIndex(PlaceholderShopCatalog.AmuletOfWardingItemId),
                Is.EqualTo(ItemHudIcons.AccessoryArtOffset + 1));
            Assert.That(ItemHudIcons.TryGetAccessoryArt(PlaceholderShopCatalog.RingOfStrengthItemId, out _), Is.False);

            Assert.That(ItemHudIcons.IconIndex(3199), Is.EqualTo(ItemHudIcons.RingIcon), "그림이 아직 없는 장신구는 반지로 둔다.");
            Assert.That(ItemHudIcons.AccessoryArtOffset,
                Is.GreaterThanOrEqualTo(ItemHudIcons.ChestItemArtOffset + ItemHudIcons.ChestItemArtCells),
                "장신구 번호대가 상자 아이템 번호대와 겹친다.");
        }

        [Test]
        public void 아이템_효과_키는_매_프레임_새로_만들지_않는다()
        {
            Assert.That(ConsumableHudEffects.ItemEffectId(2106),
                Is.SameAs(ConsumableHudEffects.ItemEffectId(2106)));
        }

        [Test]
        public void 시간이_있는_효과를_거는_근원이_모두_켜지면_최대치만큼_올라간다()
        {
            // 건 아이템마다 한 칸이다 (S-GD-G3QJCW). 상자 추첨표의 모든 아이템을 마시고 과잉 보상까지 받는다.
            int timedSources = 0;
            foreach (ChestLootEntry entry in PlaceholderChestLootTable.Entries)
            {
                Assert.That(ShopItemEffectCatalog.TryGetConsumableEffects(entry.ItemId, out IReadOnlyList<ConsumableEffect> list),
                    Is.True, $"{entry.ItemId} 의 효과가 없다.");
                bool timed = false;
                foreach (ConsumableEffect effect in list)
                {
                    timed |= buff.Apply(effect, 0f, entry.ItemId);
                }

                timedSources += timed ? 1 : 0;
            }

            foreach (ConsumableEffect effect in ShopItemEffectCatalog.ChestOverflowEffects)
            {
                buff.Apply(effect, 0f, ShopItemEffectCatalog.ChestOverflowSourceId);
            }

            Capture(0.5f);

            Assert.That(timedSources + 1, Is.EqualTo(ConsumableHudEffects.Maximum), "시간이 있는 아이템 수 + 과잉 보상");
            Assert.That(effects.Count, Is.EqualTo(ConsumableHudEffects.Maximum),
                "HUD 넘침 정책의 근거(ProducerMaximum)가 이 수를 쓴다.");
        }
    }
}

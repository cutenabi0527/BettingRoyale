using BettingRoyale.Data.Shop;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 상자 아이템이 쓰는 버프 칸 — 공격 속도·주는 피해·받는 피해·대시 재충전·보호막·무적·지속 회복·방어력·공격력
    /// (S-GD-DDXPX9 §2·§5 · S-GD-RVVB4H §6·§7 · S-GD-G3QJCW). 이동 속도 칸은 <see cref="ConsumableBuffStateTests"/> 가 본다.
    /// </summary>
    public class ConsumableBuffChannelTests
    {
        private ConsumableBuffState buff;

        [SetUp]
        public void SetUp() => buff = new ConsumableBuffState();

        [Test]
        public void 꺼져_있을_때는_모든_칸이_항등원이다()
        {
            Assert.That(buff.MoveSpeedMultiplier, Is.EqualTo(1f));
            Assert.That(buff.AttackSpeedMultiplier, Is.EqualTo(1f));
            Assert.That(buff.DamageBonus, Is.EqualTo(0f));
            Assert.That(buff.DamageTakenMultiplier, Is.EqualTo(1f));
            Assert.That(buff.DashRechargeMultiplier, Is.EqualTo(1f));
            Assert.That(buff.ShieldAmount, Is.EqualTo(0f));
            Assert.That(buff.IsInvulnerable, Is.False);
            Assert.That(buff.IsRegenerating, Is.False);
            Assert.That(buff.DefenseBonus, Is.EqualTo(0f), "고정 칸의 항등원은 1 이 아니라 0 이다.");
            Assert.That(buff.AttackPowerBonus, Is.EqualTo(0f), "고정 칸의 항등원은 1 이 아니라 0 이다.");
            Assert.That(buff.AbsorbWithShield(10f), Is.EqualTo(10f));
            Assert.That(buff.TakeRegenerationHeal(100f), Is.EqualTo(0f));
        }

        [Test]
        public void 광란_물약은_공격_속도_배율_1점3()
        {
            buff.Apply(ConsumableEffect.TimedAttackSpeed(0.30f, 6f), now: 0f);
            Assert.That(buff.AttackSpeedMultiplier, Is.EqualTo(1.30f).Within(1e-5));
        }

        [Test]
        public void 투지_물약은_피해_증감분_0점2()
        {
            buff.Apply(ConsumableEffect.TimedDamageBonus(0.20f, 6f), now: 0f);
            Assert.That(buff.DamageBonus, Is.EqualTo(0.20f).Within(1e-5));
        }

        [Test]
        public void 철벽_물약은_받는_피해_배율_0점75()
        {
            buff.Apply(ConsumableEffect.TimedDamageTaken(0.25f, 6f), now: 0f);
            Assert.That(buff.DamageTakenMultiplier, Is.EqualTo(0.75f).Within(1e-5), "−25% 는 배율로 0.75 다 — 계산식에는 −0.25 로 더해진다 (S-GD-EM82G5).");
        }

        [Test]
        public void 받는_피해_감소가_100퍼센트를_넘어도_배율은_0_아래로_안_간다()
        {
            buff.Apply(ConsumableEffect.TimedDamageTaken(1.5f, 6f), now: 0f);
            Assert.That(buff.DamageTakenMultiplier, Is.EqualTo(0f), "음수 배율이면 맞을수록 회복한다.");
        }

        [Test]
        public void 대시_충전석은_재충전_배율_0점5를_3초()
        {
            buff.Apply(ConsumableEffect.TimedDashRecharge(0.5f, 3f), now: 10f);
            Assert.That(buff.DashRechargeMultiplier, Is.EqualTo(0.5f));

            Assert.That(buff.Tick(12.99f), Is.False);
            Assert.That(buff.Tick(13f), Is.True);
            Assert.That(buff.DashRechargeMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void 칸끼리는_독립이라_투지와_광란이_함께_켜진다()
        {
            buff.Apply(ConsumableEffect.TimedDamageBonus(0.20f, 6f), now: 0f);
            buff.Apply(ConsumableEffect.TimedAttackSpeed(0.30f, 6f), now: 0f);
            buff.Apply(ConsumableEffect.TimedMoveSpeed(0.30f, 4f), now: 0f);

            Assert.That(buff.DamageBonus, Is.EqualTo(0.20f).Within(1e-5));
            Assert.That(buff.AttackSpeedMultiplier, Is.EqualTo(1.30f).Within(1e-5));
            Assert.That(buff.MoveSpeedMultiplier, Is.EqualTo(1.30f).Within(1e-5));
        }

        [Test]
        public void 다른_아이템은_같은_칸이라도_덮어쓰지_않고_더한다()
        {
            // S-GD-G3QJCW — 질주(+30%, 4초) 도중 신속(+10%, 5초)을 마시면 +40% 이고, 각자 제 시간에 끝난다.
            buff.Apply(ConsumableEffect.TimedMoveSpeed(0.30f, 4f), now: 0f, RecommendedChestItemCatalog.SprintPotionItemId);
            buff.Apply(ConsumableEffect.TimedMoveSpeed(0.10f, 5f), now: 2f, PlaceholderShopCatalog.SwiftnessPotionItemId);

            Assert.That(buff.MoveSpeedMultiplier, Is.EqualTo(1.40f).Within(1e-5), "서로 다른 물약은 덮어쓰지 않는다.");
            Assert.That(buff.RemainingSeconds(2f), Is.EqualTo(5f).Within(1e-5), "모두 끝나기까지는 신속의 5초다.");

            Assert.That(buff.Tick(4f), Is.True);
            Assert.That(buff.MoveSpeedMultiplier, Is.EqualTo(1.10f).Within(1e-5), "질주가 끝나도 신속은 남는다.");
            Assert.That(buff.Tick(7f), Is.True);
            Assert.That(buff.MoveSpeedMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void 과잉_보상과_광란의_공격_속도는_더한다()
        {
            buff.Apply(ConsumableEffect.TimedAttackSpeed(0.30f, 6f), now: 0f, RecommendedChestItemCatalog.FrenzyPotionItemId);
            buff.Apply(ConsumableEffect.TimedAttackSpeed(0.15f, 6f), now: 1f, ShopItemEffectCatalog.ChestOverflowSourceId);

            Assert.That(buff.AttackSpeedMultiplier, Is.EqualTo(1.45f).Within(1e-5));
        }

        [Test]
        public void 같은_아이템을_두_번_받아도_효과는_한_번치고_시간만_새로_찬다()
        {
            int frenzy = RecommendedChestItemCatalog.FrenzyPotionItemId;
            buff.Apply(ConsumableEffect.TimedAttackSpeed(0.30f, 6f), now: 0f, frenzy);
            buff.Apply(ConsumableEffect.TimedAttackSpeed(0.30f, 6f), now: 1f, frenzy);

            Assert.That(buff.AttackSpeedMultiplier, Is.EqualTo(1.30f).Within(1e-5), "+60% 가 되면 안 된다.");
            Assert.That(buff.Remaining(ConsumableBuffChannel.AttackSpeed, 1f), Is.EqualTo(6f).Within(1e-5));
        }

        [Test]
        public void 한_칸이_끝나도_다른_칸은_남는다()
        {
            buff.Apply(ConsumableEffect.TimedMoveSpeed(0.30f, 4f), now: 0f);
            buff.Apply(ConsumableEffect.TimedDamageBonus(0.20f, 6f), now: 0f);

            Assert.That(buff.Tick(4f), Is.True);
            Assert.That(buff.MoveSpeedMultiplier, Is.EqualTo(1f));
            Assert.That(buff.DamageBonus, Is.EqualTo(0.20f).Within(1e-5));

            Assert.That(buff.Tick(5f), Is.False, "새로 꺼진 칸이 없으면 false 여야 매 프레임 다시 밀지 않는다.");
            Assert.That(buff.Tick(6f), Is.True);
            Assert.That(buff.DamageBonus, Is.EqualTo(0f));
        }

        // ── 보호막 ─────────────────────────────────────────────────────────

        [Test]
        public void 보호막이_피해를_먼저_막고_남은_피해만_넘긴다()
        {
            buff.Apply(ConsumableEffect.Shield(30f, 10f), now: 0f);

            Assert.That(buff.AbsorbWithShield(12f), Is.EqualTo(0f));
            Assert.That(buff.ShieldAmount, Is.EqualTo(18f));

            Assert.That(buff.AbsorbWithShield(25f), Is.EqualTo(7f), "보호막 18 을 넘는 7 만 체력으로 간다.");
            Assert.That(buff.ShieldAmount, Is.EqualTo(0f));
            Assert.That(buff.AbsorbWithShield(5f), Is.EqualTo(5f), "다 깎인 보호막은 더 막지 않는다.");
        }

        [Test]
        public void 보호막은_시간이_지나면_남은_양과_함께_사라진다()
        {
            buff.Apply(ConsumableEffect.Shield(10f, 6f), now: 0f);

            Assert.That(buff.Tick(6f), Is.True);
            Assert.That(buff.ShieldAmount, Is.EqualTo(0f));
            Assert.That(buff.AbsorbWithShield(4f), Is.EqualTo(4f));
        }

        [Test]
        public void 같은_보호막은_새로_채우고_다른_근원의_보호막은_더한다()
        {
            // 보호막 부적 30 을 두 번 써도 60 이 아니다. 과잉 보상 10 은 다른 근원이라 더해 40 이다 (S-GD-G3QJCW).
            int charm = RecommendedChestItemCatalog.ShieldCharmItemId;
            buff.Apply(ConsumableEffect.Shield(30f, 10f), now: 0f, charm);
            buff.Apply(ConsumableEffect.Shield(30f, 10f), now: 1f, charm);
            Assert.That(buff.ShieldAmount, Is.EqualTo(30f));

            buff.Apply(ConsumableEffect.Shield(10f, 6f), now: 2f, ShopItemEffectCatalog.ChestOverflowSourceId);
            Assert.That(buff.ShieldAmount, Is.EqualTo(40f));
        }

        [Test]
        public void 보호막이_둘이면_먼저_끝나는_것부터_깎인다()
        {
            buff.Apply(ConsumableEffect.Shield(30f, 10f), now: 0f, RecommendedChestItemCatalog.ShieldCharmItemId); // 10초에 끝
            buff.Apply(ConsumableEffect.Shield(10f, 6f), now: 0f, ShopItemEffectCatalog.ChestOverflowSourceId);   // 6초에 끝

            Assert.That(buff.AbsorbWithShield(15f), Is.EqualTo(0f));
            Assert.That(buff.ShieldAmount, Is.EqualTo(25f), "과잉 보상 10 을 먼저 다 쓰고 부적에서 5 를 깎는다.");

            Assert.That(buff.Tick(6f), Is.False, "다 깎인 과잉 보상 보호막은 이미 없다 — 6초에 꺼질 것이 남아 있지 않다.");
            Assert.That(buff.ShieldAmount, Is.EqualTo(25f));
        }

        // ── 무적 ───────────────────────────────────────────────────────────

        [Test]
        public void 무적은_1초간_켜졌다가_꺼진다()
        {
            buff.Apply(ConsumableEffect.Invulnerable(1f), now: 50f);
            Assert.That(buff.IsInvulnerable, Is.True);

            Assert.That(buff.Tick(50.99f), Is.False);
            Assert.That(buff.IsInvulnerable, Is.True);
            Assert.That(buff.Tick(51f), Is.True);
            Assert.That(buff.IsInvulnerable, Is.False);
        }

        // ── 지속 회복 ──────────────────────────────────────────────────────

        [Test]
        public void 재생_물약은_1초마다_5씩_8번_총_40()
        {
            buff.Apply(ConsumableEffect.Regeneration(5f, 8f), now: 0f);

            float total = 0f;
            for (int second = 1; second <= 12; second++)
            {
                buff.Tick(second);   // 다른 칸의 만료 처리와 같은 프레임에 돌아도 마지막 회복을 놓치지 않는다.
                total += buff.TakeRegenerationHeal(second);
            }

            Assert.That(total, Is.EqualTo(40f));
            Assert.That(buff.IsRegenerating, Is.False);
        }

        [Test]
        public void 회복_틱은_1초가_되기_전에는_주지_않는다()
        {
            buff.Apply(ConsumableEffect.Regeneration(5f, 8f), now: 0f);

            Assert.That(buff.TakeRegenerationHeal(0.5f), Is.EqualTo(0f));
            Assert.That(buff.TakeRegenerationHeal(1f), Is.EqualTo(5f));
            Assert.That(buff.TakeRegenerationHeal(1.5f), Is.EqualTo(0f), "같은 초에 두 번 주면 안 된다.");
        }

        [Test]
        public void 프레임이_밀려도_밀린_회복을_한꺼번에_준다()
        {
            buff.Apply(ConsumableEffect.Regeneration(5f, 8f), now: 0f);

            Assert.That(buff.TakeRegenerationHeal(3.2f), Is.EqualTo(15f), "1·2·3초 세 번이 밀렸다.");
            Assert.That(buff.TakeRegenerationHeal(100f), Is.EqualTo(25f), "나머지 5번. 8번을 넘기지 않는다.");
        }

        [Test]
        public void 재생_물약을_다시_마시면_처음부터_다시_센다()
        {
            buff.Apply(ConsumableEffect.Regeneration(5f, 8f), now: 0f);
            Assert.That(buff.TakeRegenerationHeal(5f), Is.EqualTo(25f));

            buff.Apply(ConsumableEffect.Regeneration(5f, 8f), now: 5f);

            Assert.That(buff.TakeRegenerationHeal(100f), Is.EqualTo(40f), "덮어쓰기라 남은 3번이 아니라 새로 8번이다.");
        }

        // ── 방어력 · 공격력 (프로틴 쉐이크) ────────────────────────────────

        [Test]
        public void 프로틴_쉐이크는_방어력_7과_공격력_3을_10초간_고정으로_올린다()
        {
            buff.Apply(ConsumableEffect.TimedDefense(7f, 10f), now: 0f);
            buff.Apply(ConsumableEffect.TimedAttackPower(3f, 10f), now: 0f);

            Assert.That(buff.DefenseBonus, Is.EqualTo(7f), "배율 1.07 이 아니라 더할 수 7 이다.");
            Assert.That(buff.AttackPowerBonus, Is.EqualTo(3f), "배율 1.03 이 아니라 더할 수 3 이다.");

            Assert.That(buff.Tick(9.99f), Is.False);
            Assert.That(buff.DefenseBonus, Is.EqualTo(7f));
            Assert.That(buff.Tick(10f), Is.True);
            Assert.That(buff.DefenseBonus, Is.EqualTo(0f));
            Assert.That(buff.AttackPowerBonus, Is.EqualTo(0f));
        }

        [Test]
        public void 방어력_공격력은_서로_다른_칸이라_따로_만료된다()
        {
            buff.Apply(ConsumableEffect.TimedDefense(7f, 10f), now: 0f);
            buff.Apply(ConsumableEffect.TimedAttackPower(3f, 4f), now: 0f);

            Assert.That(buff.Tick(4f), Is.True);
            Assert.That(buff.AttackPowerBonus, Is.EqualTo(0f));
            Assert.That(buff.DefenseBonus, Is.EqualTo(7f), "방어력 칸은 아직 살아 있어야 한다.");
        }

        [Test]
        public void 프로틴_쉐이크를_두_번_마셔도_고정값이_두_배가_되지_않는다()
        {
            buff.Apply(ConsumableEffect.TimedDefense(7f, 10f), now: 0f);
            buff.Apply(ConsumableEffect.TimedDefense(7f, 10f), now: 3f);

            Assert.That(buff.DefenseBonus, Is.EqualTo(7f), "+14 가 되면 안 된다 — 덮어쓰기다.");
        }

        [Test]
        public void 방어력_칸은_주는_피해_칸과_섞이지_않는다()
        {
            // 공격력(고정 +3)과 주는 피해(+20%)는 다른 칸이다. 한쪽을 켜도 다른 쪽은 항등원이어야 한다.
            buff.Apply(ConsumableEffect.TimedAttackPower(3f, 10f), now: 0f);
            Assert.That(buff.DamageBonus, Is.EqualTo(0f));

            buff.Apply(ConsumableEffect.TimedDamageBonus(0.20f, 6f), now: 0f);
            Assert.That(buff.AttackPowerBonus, Is.EqualTo(3f));
            Assert.That(buff.DamageBonus, Is.EqualTo(0.20f).Within(1e-5));
        }

        // ── 즉시형 · 초기화 ────────────────────────────────────────────────

        [Test]
        public void 즉시형_효과는_버프_칸을_건드리지_않는다()
        {
            Assert.That(buff.Apply(ConsumableEffect.InstantHeal(50f), 0f), Is.False);
            Assert.That(buff.Apply(ConsumableEffect.DashRefill(), 0f), Is.False);
            Assert.That(buff.Tick(1_000f), Is.False);
        }

        [Test]
        public void 라운드_경계에서_모든_칸이_지워진다()
        {
            buff.Apply(ConsumableEffect.TimedMoveSpeed(0.30f, 4f), 0f);
            buff.Apply(ConsumableEffect.TimedAttackSpeed(0.30f, 6f), 0f);
            buff.Apply(ConsumableEffect.TimedDamageBonus(0.20f, 6f), 0f);
            buff.Apply(ConsumableEffect.TimedDamageTaken(0.25f, 6f), 0f);
            buff.Apply(ConsumableEffect.TimedDashRecharge(0.5f, 3f), 0f);
            buff.Apply(ConsumableEffect.Shield(30f, 10f), 0f);
            buff.Apply(ConsumableEffect.Invulnerable(1f), 0f);
            buff.Apply(ConsumableEffect.Regeneration(5f, 8f), 0f);
            buff.Apply(ConsumableEffect.TimedDefense(7f, 10f), 0f);
            buff.Apply(ConsumableEffect.TimedAttackPower(3f, 10f), 0f);

            buff.Clear();

            Assert.That(buff.MoveSpeedMultiplier, Is.EqualTo(1f));
            Assert.That(buff.AttackSpeedMultiplier, Is.EqualTo(1f));
            Assert.That(buff.DamageBonus, Is.EqualTo(0f));
            Assert.That(buff.DamageTakenMultiplier, Is.EqualTo(1f));
            Assert.That(buff.DashRechargeMultiplier, Is.EqualTo(1f));
            Assert.That(buff.ShieldAmount, Is.EqualTo(0f));
            Assert.That(buff.IsInvulnerable, Is.False);
            Assert.That(buff.DefenseBonus, Is.EqualTo(0f));
            Assert.That(buff.AttackPowerBonus, Is.EqualTo(0f));
            Assert.That(buff.TakeRegenerationHeal(100f), Is.EqualTo(0f));
        }
    }
}

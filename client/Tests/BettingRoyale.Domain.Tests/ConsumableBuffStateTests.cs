using BettingRoyale.Data.Shop;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 신속 물약의 <b>"갱신되지만 중첩되지 않는다"</b> 규칙을 못 박는다 (S-GD-YMKKDJ).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 사용자 확정(2026-09-17): 지속 중에 다시 마시면 <b>남은 시간만 5초로 초기화</b>되고
    /// 이동 속도는 +10% 그대로다. <b>+20% 가 되지 않는다.</b>
    /// </para>
    /// <para>
    /// ⚠️ <b>여기서 보는 것은 배율 값까지다.</b> 그 배율이 실제로 캐릭터를 빠르게 만드는지는
    /// <c>CombatantMotor</c> 가 축복 배율과 <b>곱해서</b> 쓰는지에 달려 있고,
    /// 그쪽은 EditMode 테스트(<c>ConsumableMoveSpeedTests</c>)가 본다.
    /// </para>
    /// </remarks>
    public class ConsumableBuffStateTests
    {
        private ConsumableBuffState buff;

        [SetUp]
        public void SetUp() => buff = new ConsumableBuffState();

        private static ConsumableEffect Swiftness()
        {
            ShopItemEffectCatalog.TryGetConsumableEffect(
                PlaceholderShopCatalog.SwiftnessPotionItemId, out ConsumableEffect effect);
            return effect;
        }

        [Test]
        public void Drinking_TurnsTheBuffOnForFiveSeconds()
        {
            Assert.That(buff.Apply(Swiftness(), now: 100f), Is.True);

            Assert.That(buff.IsMoveSpeedActive, Is.True);
            Assert.That(buff.RemainingSeconds(100f), Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void ActiveBuff_MultipliesMoveSpeedByOnePointOne()
        {
            buff.Apply(Swiftness(), now: 0f);

            Assert.That(buff.MoveSpeedMultiplier, Is.EqualTo(1.10f).Within(0.001f));
        }

        [Test]
        public void IdleBuff_IsTheIdentityMultiplier()
        {
            // 1 이어야 이동 컴포넌트가 아무 때나 곱해도 속도가 변하지 않는다.
            Assert.That(buff.MoveSpeedMultiplier, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void DrinkingAgainWhileActive_RefreshesTimeButNotSpeed()
        {
            buff.Apply(Swiftness(), now: 0f);

            // 남은 1초에 다시 마신다.
            buff.Apply(Swiftness(), now: 4f);

            Assert.That(buff.RemainingSeconds(4f), Is.EqualTo(5f).Within(0.001f),
                "지속 시간은 갱신되어야 한다.");
            Assert.That(buff.MoveSpeedMultiplier, Is.EqualTo(1.10f).Within(0.001f),
                "이동 속도는 중첩되지 않는다 — +20% 가 되면 안 된다.");
        }

        [Test]
        public void DrinkingThreeTimes_StillOnlyTenPercent()
        {
            buff.Apply(Swiftness(), now: 0f);
            buff.Apply(Swiftness(), now: 1f);
            buff.Apply(Swiftness(), now: 2f);

            Assert.That(buff.MoveSpeedMultiplier, Is.EqualTo(1.10f).Within(0.001f));
        }

        [Test]
        public void BeforeExpiry_TickKeepsTheBuff()
        {
            buff.Apply(Swiftness(), now: 0f);

            Assert.That(buff.Tick(4.99f), Is.False);
            Assert.That(buff.IsMoveSpeedActive, Is.True);
        }

        [Test]
        public void AtExpiry_TickReportsItOnceAndClears()
        {
            buff.Apply(Swiftness(), now: 0f);

            Assert.That(buff.Tick(5f), Is.True, "만료된 순간에 한 번은 알려야 배율을 되돌린다.");
            Assert.That(buff.IsMoveSpeedActive, Is.False);

            // 두 번째부터는 조용해야 한다. 매 프레임 true 면 모터에 매번 다시 쓴다.
            Assert.That(buff.Tick(6f), Is.False);
        }

        [Test]
        public void ExpiredBuff_ReturnsToTheIdentityMultiplier()
        {
            buff.Apply(Swiftness(), now: 0f);
            buff.Tick(5f);

            Assert.That(buff.MoveSpeedMultiplier, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void HealthPotion_IsNotATimedBuff()
        {
            ShopItemEffectCatalog.TryGetConsumableEffect(
                PlaceholderShopCatalog.HealthPotionItemId, out ConsumableEffect heal);

            Assert.That(buff.Apply(heal, now: 0f), Is.False, "즉시 회복은 버프 상태를 건드리지 않는다.");
            Assert.That(buff.IsMoveSpeedActive, Is.False);
            Assert.That(buff.MoveSpeedMultiplier, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void Clear_DropsTheBuffImmediately()
        {
            buff.Apply(Swiftness(), now: 0f);
            buff.Clear();

            Assert.That(buff.IsMoveSpeedActive, Is.False);
            Assert.That(buff.MoveSpeedMultiplier, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void InactiveBuff_ReportsNoRemainingTime()
        {
            Assert.That(buff.RemainingSeconds(0f), Is.Zero);
        }
    }
}

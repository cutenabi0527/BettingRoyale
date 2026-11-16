using BettingRoyale.Data.Combat;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 무기 수치 정본(<see cref="WeaponStats"/>)이 S-GD-NEGNFZ 표와 일치하는지 지킨다.
    ///
    /// 이 테스트가 존재하는 이유: 값이 프리팹·씬에 흩어져 있던 동안에는 어떤 자동 검사도
    /// 수치를 읽을 수 없었고, 경로마다 다른 값이 들어가도 아무도 알아채지 못했다 (S15P21A606-200).
    /// 값을 의도적으로 조정할 때는 이 표와 S-GD-NEGNFZ 를 <b>함께</b> 갱신한다 — 한쪽만 고치면 여기서 깨진다.
    /// </summary>
    public class WeaponStatsTests
    {
        /// <summary>
        /// 조정값은 전역 상태다. 한 테스트가 남긴 값이 다음 테스트로 새면 원인을 찾기 어려우므로
        /// 매 테스트 전후로 비운다. 제품 경로는 조정값을 넣지 않는 것이 정상이다.
        /// </summary>
        [SetUp]
        [TearDown]
        public void ResetOverrides() => WeaponStats.ClearOverrides();

        [Test]
        public void Sword_MatchesPlaytestTable()
        {
            Assert.That(WeaponStats.Sword.Damage, Is.EqualTo(25f), "검 피해량");
            Assert.That(WeaponStats.Sword.Range, Is.EqualTo(2f), "검 사거리(공격 폭이 아니다 — S-GD-NBWWF7)");
            Assert.That(WeaponStats.Sword.CooldownSeconds, Is.EqualTo(0.6f), "검 쿨타임");
            Assert.That(WeaponStats.Sword.ProjectileSpeed, Is.EqualTo(0f), "근접 무기는 투사체 속도가 0 이다");
        }

        [Test]
        public void Bow_MatchesPlaytestTable()
        {
            Assert.That(WeaponStats.Bow.Damage, Is.EqualTo(22f), "활 피해량 — 저연사 고위력");
            Assert.That(WeaponStats.Bow.Range, Is.EqualTo(12f), "활 최대 비행거리 — 유효 사거리에 맞춘 값");
            Assert.That(WeaponStats.Bow.CooldownSeconds, Is.EqualTo(0.75f), "활 쿨타임");
            Assert.That(WeaponStats.Bow.ProjectileSpeed, Is.EqualTo(18f), "화살 탄속");
        }

        [Test]
        public void TryGet_ReturnsSameInstanceAsPublicField()
        {
            Assert.That(WeaponStats.TryGet(WeaponType.Sword, out WeaponConfig sword), Is.True);
            Assert.That(sword.Damage, Is.EqualTo(WeaponStats.Sword.Damage));
            Assert.That(sword.Range, Is.EqualTo(WeaponStats.Sword.Range));
            Assert.That(sword.CooldownSeconds, Is.EqualTo(WeaponStats.Sword.CooldownSeconds));
            Assert.That(sword.ProjectileSpeed, Is.EqualTo(WeaponStats.Sword.ProjectileSpeed));

            Assert.That(WeaponStats.TryGet(WeaponType.Bow, out WeaponConfig bow), Is.True);
            Assert.That(bow.Damage, Is.EqualTo(WeaponStats.Bow.Damage));
            Assert.That(bow.Range, Is.EqualTo(WeaponStats.Bow.Range));
            Assert.That(bow.CooldownSeconds, Is.EqualTo(WeaponStats.Bow.CooldownSeconds));
            Assert.That(bow.ProjectileSpeed, Is.EqualTo(WeaponStats.Bow.ProjectileSpeed));
        }

        /// <summary>
        /// 미구현 무기는 수치를 지어내지 않는다. WeaponCatalog 가 태그를 거부하는 것과 같은 경계다 —
        /// 둘 중 하나만 통과하면 "태그는 없는데 수치는 있는" 무기가 생긴다 (S-GD-7DRYPY).
        ///
        /// 태그 쪽 짝 검증은 여기서 하지 않는다. WeaponCatalog 를 이 프로젝트에 링크하면
        /// Data.Combat.WeaponTag 와 Data.Blessing.WeaponTag 의 이름 충돌로 기존 테스트가 깨진다(D-065).
        /// </summary>
        [Test]
        public void 활만_공격_감속을_갖는다()
        {
            Assert.That(WeaponStats.GetAttackSlow(WeaponType.Sword).IsActive, Is.False,
                "붙어야 하는 무기에 후딜까지 주면 접근이 성립하지 않는다");

            WeaponAttackSlow bow = WeaponStats.GetAttackSlow(WeaponType.Bow);
            Assert.That(bow.IsActive, Is.True);
            Assert.That(bow.Multiplier, Is.EqualTo(0.4f));
            Assert.That(bow.Seconds, Is.EqualTo(0.5f));
        }

        [Test]
        public void 감속_지속은_쿨타임의_3분의2를_넘지_않는다()
        {
            // 넘기면 다음 사격이 창을 갱신해 사실상 상시 감속이 된다 — "공격 직후 페널티" 가 아니게 된다.
            WeaponAttackSlow bow = WeaponStats.GetAttackSlow(WeaponType.Bow);
            Assert.That(bow.Seconds / WeaponStats.Bow.CooldownSeconds, Is.LessThanOrEqualTo(0.67f).Within(0.005f),
                "쿨타임을 줄였다면 감속 지속도 같이 줄여야 한다");
        }

        [Test]
        public void 정의되지_않은_무기는_감속도_없다()
        {
            Assert.That(WeaponStats.GetAttackSlow(WeaponType.Mace).IsActive, Is.False);
            Assert.That(WeaponStats.GetAttackSlow(WeaponType.None).IsActive, Is.False);
        }

        [Test]
        public void UndefinedWeapons_HaveNoStats()
        {
            Assert.That(WeaponStats.TryGet(WeaponType.Mace, out WeaponConfig mace), Is.False, "둔기 수치는 아직 없다");
            Assert.That(mace.Damage, Is.EqualTo(0f), "거부된 무기는 0 으로 남는다");
            Assert.That(mace.Range, Is.EqualTo(0f));
            Assert.That(mace.CooldownSeconds, Is.EqualTo(0f));
            Assert.That(mace.ProjectileSpeed, Is.EqualTo(0f));

            Assert.That(WeaponStats.TryGet(WeaponType.None, out _), Is.False, "무기 없음은 수치도 없다");
        }

        /// <summary>
        /// 90_CombatLab Dev 패널의 밸런스 실측 경로. 조정값은 정본을 덮되 <b>지우지 않는다</b> —
        /// 되돌리기 한 번으로 원래 수치가 그대로 돌아와야 실측을 반복할 수 있다 (S15P21A606-200).
        /// </summary>
        [Test]
        public void Override_ShadowsDefaultAndRestoresOnClear()
        {
            Assert.That(WeaponStats.HasOverrides, Is.False, "기본 상태는 조정값 없음이다");

            var tuned = new WeaponConfig(damage: 40f, range: 30f, cooldownSeconds: 0.2f, projectileSpeed: 25f);
            Assert.That(WeaponStats.TrySetOverride(WeaponType.Bow, tuned), Is.True);

            Assert.That(WeaponStats.HasOverrides, Is.True);
            Assert.That(WeaponStats.TryGet(WeaponType.Bow, out WeaponConfig live), Is.True);
            Assert.That(live.Damage, Is.EqualTo(40f), "조정값이 우선한다");

            Assert.That(WeaponStats.TryGetDefault(WeaponType.Bow, out WeaponConfig baseline), Is.True);
            Assert.That(baseline.Damage, Is.EqualTo(22f), "정본 상수는 덮이지 않는다");
            Assert.That(WeaponStats.Sword.Damage, Is.EqualTo(25f), "다른 무기는 영향받지 않는다");

            WeaponStats.ClearOverride(WeaponType.Bow);
            Assert.That(WeaponStats.HasOverrides, Is.False);
            Assert.That(WeaponStats.TryGet(WeaponType.Bow, out WeaponConfig restored), Is.True);
            Assert.That(restored.Damage, Is.EqualTo(22f), "되돌리면 정본으로 복귀한다");
        }

        /// <summary>정본에 없는 무기는 조정도 거부한다 — 슬라이더로 수치를 지어내지 못하게 한다.</summary>
        [Test]
        public void Override_IsRejectedForUndefinedWeapons()
        {
            var anything = new WeaponConfig(damage: 1f, range: 1f, cooldownSeconds: 1f, projectileSpeed: 1f);

            Assert.That(WeaponStats.TrySetOverride(WeaponType.Mace, anything), Is.False);
            Assert.That(WeaponStats.TrySetOverride(WeaponType.None, anything), Is.False);
            Assert.That(WeaponStats.HasOverrides, Is.False);
            Assert.That(WeaponStats.TryGet(WeaponType.Mace, out _), Is.False, "거부 뒤에도 미정의 상태를 유지한다");
        }
    }
}

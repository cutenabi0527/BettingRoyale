using System;
using BettingRoyale.Data.Blessing;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 무기 태그 규약의 도메인 동작 (S-GD-SPV9FY, S-GD-SB84DQ).
    ///
    /// 확장 절차를 <b>강제</b>하는 검사(카탈로그 완전성·숫자 값 고정·이름 충돌)는
    /// <c>Scripts/Editor/BlessingWeaponTagCatalogTests.cs</c> 에 있다 —
    /// <c>verify-all.ps1</c> 이 EditMode 는 돌리지만 이 프로젝트는 안 돌리기 때문이다(O-019).
    /// 여기는 순수 도메인 행동만 본다.
    /// </summary>
    [TestFixture]
    public class BlessingWeaponTagTests
    {
        [Test]
        public void 공용_축복은_어느_무기로도_뽑힌다()
        {
            var blessing = new BlessingDefinition("common-1", "공용", WeaponTag.Common, BlessingTier.Silver);

            Assert.That(blessing.IsCompatibleWith(WeaponTag.Sword), Is.True);
            Assert.That(blessing.IsCompatibleWith(WeaponTag.Bow), Is.True);
            Assert.That(blessing.IsCompatibleWith(WeaponTag.Mace), Is.True);
        }

        [Test]
        public void 전용_축복은_다른_무기로_뽑히지_않는다()
        {
            var sword = new BlessingDefinition("sword-1", "검 전용", WeaponTag.Sword, BlessingTier.Silver);

            Assert.That(sword.IsCompatibleWith(WeaponTag.Sword), Is.True);
            Assert.That(sword.IsCompatibleWith(WeaponTag.Bow), Is.False);
            Assert.That(sword.IsCompatibleWith(WeaponTag.Mace), Is.False);
        }

        /// <summary>
        /// 외부 데이터(D-041)에서 복원한 값은 캐스팅만으로 통과한다 —
        /// 막지 않으면 <b>아무 무기와도 호환되지 않는 축복</b>이 예외 없이 만들어지고
        /// 후보에서만 조용히 사라진다.
        /// </summary>
        [Test]
        public void 정의되지_않은_태그로는_축복을_만들_수_없다()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new BlessingDefinition("bad", "잘못된 태그", (WeaponTag)99, BlessingTier.Silver));
        }

        /// <summary>
        /// <c>Common</c> 은 축복이 갖는 값이지 <b>플레이어가 드는 무기가 아니다.</b>
        /// 장착값으로 흘러들면 전용 축복이 전부 탈락해 후보가 공용만 남는다 —
        /// 예외가 안 나므로 이 구분을 테스트로 고정한다.
        /// </summary>
        [Test]
        public void 공용은_장착_무기가_아니다()
        {
            Assert.That(BlessingWeaponTagCatalog.IsEquippable(WeaponTag.Common), Is.False);
            Assert.That(BlessingWeaponTagCatalog.IsEquippable(WeaponTag.Sword), Is.True);
            Assert.That(BlessingWeaponTagCatalog.IsDefined(WeaponTag.Common), Is.True);
        }

        [Test]
        public void 둔기_태그는_규약에_존재한다()
        {
            // 무기 구현은 아직 없지만(WeaponCatalog 가 Mace 에 false), 규약에는 자리가 있어야 한다.
            // Blunt 라는 옛 이름이 되살아나면 이 테스트가 컴파일되지 않는다 (S-GD-SPV9FY).
            Assert.That(BlessingWeaponTagCatalog.IsDefined(WeaponTag.Mace), Is.True);
        }

        // ------------------------------------------------------------ 2층 분류 (S-GD-DPWH9W)

        [Test]
        public void 근접_축복은_근접_무기_전부에_뜨고_원거리에는_안_뜬다()
        {
            var melee = new BlessingDefinition("m", "근접", WeaponTag.Melee, BlessingTier.Silver);

            Assert.That(melee.IsCompatibleWith(WeaponTag.Sword), Is.True);
            Assert.That(melee.IsCompatibleWith(WeaponTag.Mace), Is.True, "둔기도 근접이다");
            Assert.That(melee.IsCompatibleWith(WeaponTag.Bow), Is.False);
        }

        [Test]
        public void 원거리_축복은_활에만_뜬다()
        {
            var ranged = new BlessingDefinition("r", "원거리", WeaponTag.Ranged, BlessingTier.Silver);

            Assert.That(ranged.IsCompatibleWith(WeaponTag.Bow), Is.True);
            Assert.That(ranged.IsCompatibleWith(WeaponTag.Sword), Is.False);
            Assert.That(ranged.IsCompatibleWith(WeaponTag.Mace), Is.False);
        }

        /// <summary>
        /// 새 무기를 넣고 <b>분류를 안 정하면</b> 그 무기는 2층 축복을 하나도 못 받는다.
        /// 후보 자리는 공용이 채우므로 예외도 로그도 없이 조용히 좁아진다 (S-GD-DPWH9W).
        /// </summary>
        [Test]
        public void 장착_가능한_무기는_전부_분류를_갖는다()
        {
            foreach (var weapon in BlessingWeaponTagCatalog.Equippable)
            {
                Assert.That(
                    BlessingWeaponTagCatalog.TryGetCategory(weapon, out var category), Is.True,
                    $"{weapon} 의 분류가 표에 없습니다. 근접인지 원거리인지 정해야 합니다");
                Assert.That(
                    BlessingWeaponTagCatalog.IsCategory(category), Is.True,
                    $"{weapon} 의 분류로 2층이 아닌 값이 들어 있습니다");
            }
        }

        [Test]
        public void 분류_태그는_장착_무기가_아니다()
        {
            foreach (var category in BlessingWeaponTagCatalog.CategoryTags)
            {
                Assert.That(
                    BlessingWeaponTagCatalog.IsEquippable(category), Is.False,
                    $"{category} 는 축복이 요구하는 값이지 플레이어가 드는 무기가 아닙니다");
            }
        }

        /// <summary>
        /// 표에 없는 무기가 <c>out</c> 으로 나온 값을 그냥 쓰면
        /// <c>Common</c>(0) 이 되어 <b>모든 축복과 호환되는 것처럼</b> 보인다.
        /// </summary>
        [Test]
        public void 표에_없는_무기는_분류를_못_찾는다()
        {
            Assert.That(BlessingWeaponTagCatalog.TryGetCategory((WeaponTag)99, out _), Is.False);
            Assert.That(BlessingWeaponTagCatalog.TryGetCategory(WeaponTag.Common, out _), Is.False,
                "공용은 무기가 아니다");
            Assert.That(BlessingWeaponTagCatalog.TryGetCategory(WeaponTag.Melee, out _), Is.False,
                "분류 자체는 무기가 아니다");
        }

        [Test]
        public void 전용_축복은_분류가_같아도_다른_무기에는_안_뜬다()
        {
            var swordOnly = new BlessingDefinition("s", "검 전용", WeaponTag.Sword, BlessingTier.Silver);

            Assert.That(swordOnly.IsCompatibleWith(WeaponTag.Mace), Is.False,
                "검과 둔기는 둘 다 근접이지만 검 전용은 둔기에 뜨면 안 된다");
        }
    }
}

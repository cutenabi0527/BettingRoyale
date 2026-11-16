using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Throwing;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 이동·대시·투척 실측 조정 레이어 (S15P21A606-191 · S15P21A606-192).
    /// </summary>
    /// <remarks>
    /// 여기서 지키는 것은 수치가 아니라 <b>"조정이 비어 있으면 아무 일도 없다"</b> 는 계약이다.
    /// 그게 깨지면 Dev 패널을 열지 않은 제품 경로의 동작이 조용히 달라진다.
    /// </remarks>
    [TestFixture]
    public sealed class CombatTuningTests
    {
        [SetUp]
        [TearDown]
        public void ResetTuning()
        {
            CombatTuning.ClearAll();
            ThrowTuning.Clear();
        }

        [Test]
        public void 기본_상태는_조정이_없다()
        {
            Assert.That(CombatTuning.HasOverrides, Is.False, "이동·대시 조정이 걸려 있으면 안 된다");
            Assert.That(ThrowTuning.HasOverrides, Is.False, "투척 조정이 걸려 있으면 안 된다");
            Assert.That(CombatTuning.Dash.IsEmpty, Is.True);
            Assert.That(CombatTuning.TryGetWeaponMovement(WeaponType.Bow, out _), Is.False);
        }

        [Test]
        public void 감속은_배율과_지속이_둘_다_유효할_때만_켜진다()
        {
            Assert.That(new WeaponMovementTuning(0f, 0.5f, 0f).HasAttackSlow, Is.False, "지속이 0 이면 감속이 아니다");
            Assert.That(new WeaponMovementTuning(0f, 1f, 0.4f).HasAttackSlow, Is.False, "배율 1 은 감속이 아니다");
            Assert.That(new WeaponMovementTuning(0f, 1.5f, 0.4f).HasAttackSlow, Is.False, "1 보다 크면 가속이라 거부한다");
            Assert.That(new WeaponMovementTuning(0f, 0.5f, 0.4f).HasAttackSlow, Is.True);
        }

        [Test]
        public void 정본에_없는_무기는_이동_조정도_거부한다()
        {
            var tuning = new WeaponMovementTuning(7f, 0.5f, 0.4f);

            Assert.That(CombatTuning.TrySetWeaponMovement(WeaponType.Mace, tuning), Is.False, "둔기는 수치가 없다");
            Assert.That(CombatTuning.TrySetWeaponMovement(WeaponType.None, tuning), Is.False, "무기 없음도 거부한다");
            Assert.That(CombatTuning.TrySetWeaponMovement(WeaponType.Bow, tuning), Is.True);
            Assert.That(CombatTuning.HasOverrides, Is.True);
        }

        [Test]
        public void 빈_조정을_넣으면_등록이_사라진다()
        {
            Assert.That(CombatTuning.TrySetWeaponMovement(WeaponType.Sword, new WeaponMovementTuning(6f, 1f, 0f)), Is.True);
            Assert.That(CombatTuning.TryGetWeaponMovement(WeaponType.Sword, out _), Is.True);

            // 사람이 슬라이더를 기본값으로 되돌린 상황 — 등록이 남아 있으면 "조정 중" 표시가 영원히 켜진다.
            Assert.That(CombatTuning.TrySetWeaponMovement(WeaponType.Sword, default), Is.True);
            Assert.That(CombatTuning.TryGetWeaponMovement(WeaponType.Sword, out _), Is.False);
            Assert.That(CombatTuning.HasOverrides, Is.False);
        }

        [Test]
        public void 무기별로_따로_걸리고_따로_풀린다()
        {
            CombatTuning.TrySetWeaponMovement(WeaponType.Sword, new WeaponMovementTuning(4f, 1f, 0f));
            CombatTuning.TrySetWeaponMovement(WeaponType.Bow, new WeaponMovementTuning(6f, 0.4f, 0.5f));

            CombatTuning.ClearWeaponMovement(WeaponType.Sword);

            Assert.That(CombatTuning.TryGetWeaponMovement(WeaponType.Sword, out _), Is.False);
            Assert.That(CombatTuning.TryGetWeaponMovement(WeaponType.Bow, out WeaponMovementTuning bow), Is.True);
            Assert.That(bow.MoveSpeedMetersPerSecond, Is.EqualTo(6f), "활 조정이 검 해제에 휩쓸리면 안 된다");
        }

        [Test]
        public void 대시_조정은_항목별로_독립이다()
        {
            CombatTuning.SetDash(new DashMovementTuning(0f, 4, 0f));

            Assert.That(CombatTuning.Dash.HasMaxCharges, Is.True);
            Assert.That(CombatTuning.Dash.HasSpeed, Is.False, "속도를 안 건드렸으면 조정이 아니다");
            Assert.That(CombatTuning.Dash.HasRecharge, Is.False);
            Assert.That(CombatTuning.HasOverrides, Is.True);

            CombatTuning.ClearDash();
            Assert.That(CombatTuning.Dash.IsEmpty, Is.True);
        }

        [Test]
        public void 투척_조정이_없으면_정의를_그대로_돌려준다()
        {
            ThrowingItemDefinition original = RecommendedMagicBottleCatalog.MagicBottle;
            ThrowingItemDefinition applied = ThrowTuning.Apply(original);

            Assert.That(applied.FlightSeconds, Is.EqualTo(original.FlightSeconds));
            Assert.That(applied.WarningSeconds, Is.EqualTo(original.WarningSeconds));
            Assert.That(applied.ActivationDelay, Is.EqualTo(original.ActivationDelay));
        }

        [Test]
        public void 투척_조정은_비행과_경고만_바꾸고_나머지는_보존한다()
        {
            ThrowingItemDefinition original = RecommendedMagicBottleCatalog.MagicBottle;
            ThrowTuning.SetMaxFlightSeconds(0.4d);
            ThrowTuning.SetWarningSeconds(2.5d);

            ThrowingItemDefinition applied = ThrowTuning.Apply(original);

            Assert.That(applied.FlightSeconds, Is.EqualTo(0.4d));
            Assert.That(applied.WarningSeconds, Is.EqualTo(2.5d));
            Assert.That(applied.ActivationDelay, Is.EqualTo(2.5d), "폭발 연출 지연은 경고 시간을 따라간다");

            Assert.That(applied.ItemId, Is.EqualTo(original.ItemId));
            Assert.That(applied.Damage, Is.EqualTo(original.Damage));
            Assert.That(applied.ExplosionRadiusMeters, Is.EqualTo(original.ExplosionRadiusMeters));
            Assert.That(applied.ThrowDistanceMeters, Is.EqualTo(original.ThrowDistanceMeters));
            Assert.That(applied.CarryLimit, Is.EqualTo(original.CarryLimit));
            Assert.That(applied.SharedCooldownSeconds, Is.EqualTo(original.SharedCooldownSeconds));
            Assert.That(applied.VisibilityPolicy, Is.EqualTo(original.VisibilityPolicy));
        }

        [Test]
        public void 카탈로그_조회가_조정을_얹어_돌려준다()
        {
            var catalog = new RecommendedMagicBottleCatalog();
            ThrowTuning.SetMaxFlightSeconds(0.5d);

            Assert.That(catalog.TryGetThrowing(RecommendedMagicBottleCatalog.MagicBottleItemId,
                out ThrowingItemDefinition tuned), Is.True);
            Assert.That(tuned.FlightSeconds, Is.EqualTo(0.5d), "투척 규칙이 정의를 얻는 유일한 경로다");

            ThrowTuning.Clear();
            Assert.That(catalog.TryGetThrowing(RecommendedMagicBottleCatalog.MagicBottleItemId,
                out ThrowingItemDefinition restored), Is.True);
            Assert.That(restored.FlightSeconds,
                Is.EqualTo(RecommendedMagicBottleCatalog.MagicBottleFlightSeconds), "해제하면 상수로 돌아온다");
        }

        [Test]
        public void 유한하지_않은_값은_조정으로_받지_않는다()
        {
            ThrowTuning.SetMaxFlightSeconds(double.NaN);
            Assert.That(ThrowTuning.HasFlight, Is.False);

            ThrowTuning.SetMaxFlightSeconds(double.PositiveInfinity);
            Assert.That(ThrowTuning.HasFlight, Is.False);

            ThrowTuning.SetWarningSeconds(-1d);
            Assert.That(ThrowTuning.HasWarning, Is.False);
        }
    }
}

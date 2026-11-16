using BettingRoyale.Data.Combat;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 권위 틱 배치 경계의 회귀 (S-GD-7G8ZTA). 선행 블록에서 발견한 결함을 고정한다.
    /// </summary>
    [TestFixture]
    public sealed class CombatRoundBatchBoundaryTests
    {
        private static readonly CombatantId A = new CombatantId(10_001);
        private static readonly CombatantId B = new CombatantId(10_002);
        private static readonly CombatantId Ally = new CombatantId(10_003);

        private static CombatRoundFactLedger NewState(bool strict, bool aggressiveEnemyFilter = true)
        {
            var state = new CombatRoundFactLedger(
                10d,
                aggressiveEnemyFilter
                    ? (attacker, target) => attacker != target && target != Ally
                    : (System.Func<CombatantId, CombatantId, bool>)null);
            if (strict)
            {
                state.EnableStrictBatchMode();
            }

            state.BeginRound(1, new[] { A, B, Ally });
            return state;
        }

        [Test]
        public void 닫힌_틱_번호로_뒤늦게_온_사실은_완료된_배치를_바꾸지_못한다()
        {
            CombatRoundFactLedger state = NewState(strict: false);

            state.BeginAuthorityTick(10);
            state.RecordDamageFact(A, B, WeaponType.Sword, 10f, CombatDamageSource.Weapon, 1d, 10);
            state.EndAuthorityTick();

            // 틱 10 은 이미 확정됐다. 그 번호로 들어온 늦은 사실은 거절한다.
            Assert.That(
                state.RecordDamageFact(A, B, WeaponType.Sword, 50f, CombatDamageSource.Weapon, 1.5d, 10),
                Is.False);
            Assert.That(
                state.RecordDownedFact(B, A, CombatDamageSource.Weapon, 1.5d, 10),
                Is.False);

            // 음수 틱도 거절한다.
            Assert.That(
                state.RecordDamageFact(A, B, WeaponType.Sword, 5f, CombatDamageSource.Weapon, 1.5d, -1),
                Is.False);

            Assert.That(state.TrySettle(2d, out CombatRoundResultsReady settled), Is.True);
            Assert.That(settled.TryGetResult(A, out CombatantRoundResult attacker), Is.True);
            Assert.That(attacker.EnemyDamageDealt, Is.EqualTo(10f));
            Assert.That(settled.TryGetResult(B, out CombatantRoundResult target), Is.True);
            Assert.That(target.Survived, Is.True, "완료된 배치가 늦은 빈사로 바뀌었다.");
        }

        [Test]
        public void 엄격_배치_모드는_열린_틱_밖의_사실을_아예_받지_않는다()
        {
            CombatRoundFactLedger state = NewState(strict: true);

            Assert.That(state.IsStrictBatchMode, Is.True);
            Assert.That(
                state.RecordDamageFact(A, B, WeaponType.Sword, 10f, CombatDamageSource.Weapon, 1d, null),
                Is.False,
                "틱이 열려 있지 않은데 사실을 받았다.");

            state.BeginAuthorityTick(4);
            Assert.That(
                state.RecordDamageFact(A, B, WeaponType.Sword, 10f, CombatDamageSource.Weapon, 1d, 4),
                Is.True);
            Assert.That(
                state.RecordDamageFact(A, B, WeaponType.Sword, 10f, CombatDamageSource.Weapon, 1d, 5),
                Is.False,
                "열린 틱과 다른 번호를 섞어 담았다.");
            state.EndAuthorityTick();
        }

        [Test]
        public void 틱을_모르는_옛_호출자는_기본_모드에서_그대로_동작한다()
        {
            CombatRoundFactLedger state = NewState(strict: false);

            Assert.That(
                state.RecordDamageFact(A, B, WeaponType.Sword, 12f, CombatDamageSource.Weapon, 1d, null),
                Is.True);
            Assert.That(
                state.RecordDownedFact(B, A, CombatDamageSource.Weapon, 1.2d, null),
                Is.True);

            Assert.That(state.TrySettle(2d, out CombatRoundResultsReady settled), Is.True);
            Assert.That(settled.TryGetResult(A, out CombatantRoundResult killer), Is.True);
            Assert.That(killer.Kills, Is.EqualTo(1));
        }

        [Test]
        public void 적_판정이_거절한_공격자는_킬을_가져가지_않는다()
        {
            CombatRoundFactLedger state = NewState(strict: false);

            state.BeginAuthorityTick(2);
            // B 가 아군(Ally)을 쓰러뜨렸다 — 적 판정이 거절하므로 실제 피해도 킬도 없다.
            Assert.That(
                state.RecordDamageFact(B, Ally, WeaponType.Sword, 30f, CombatDamageSource.Weapon, 1d, 2),
                Is.False);
            Assert.That(
                state.RecordDownedFact(Ally, B, CombatDamageSource.Weapon, 1d, 2),
                Is.True);
            state.EndAuthorityTick();

            Assert.That(state.TrySettle(2d, out CombatRoundResultsReady settled), Is.True);
            Assert.That(settled.TryGetResult(B, out CombatantRoundResult friendly), Is.True);
            Assert.That(friendly.Kills, Is.Zero, "아군 처치가 킬로 집계됐다 (S-GD-7G8ZTA).");
            Assert.That(friendly.EnemyDamageDealt, Is.Zero);
            Assert.That(settled.TryGetResult(Ally, out CombatantRoundResult victim), Is.True);
            Assert.That(victim.Survived, Is.False);
            Assert.That(victim.EliminationTick, Is.EqualTo(2));
        }

        [Test]
        public void 정산은_한_라운드에_한_번만_된다()
        {
            CombatRoundFactLedger state = NewState(strict: true);

            state.BeginAuthorityTick(1);
            state.RecordDownedFact(B, A, CombatDamageSource.Weapon, 1d, 1);
            state.EndAuthorityTick();

            Assert.That(state.TrySettle(1d, out _), Is.True);
            Assert.That(state.TrySettle(1d, out _), Is.False, "같은 라운드를 두 번 확정했다.");
        }
    }
}

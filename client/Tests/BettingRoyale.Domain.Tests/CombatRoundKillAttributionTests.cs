using BettingRoyale.Data.Combat;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 결과 화면(점수)의 킬·어시스트가 축복이 보는 킬·어시스트와 <b>같은 기준</b>인지 고정한다
    /// (사용자 확정 2026-09-18, S-GD-Y534KH). 칼·화살·투척물·축복 폭발·독 모두 사람이 준 피해이고,
    /// 보호막만 깎아도 어시스트이며, 공격자가 없는 축복 피해로 쓰러지면 자기장처럼 마지막 타격자가 킬이다.
    /// </summary>
    [TestFixture]
    public sealed class CombatRoundKillAttributionTests
    {
        private static readonly CombatantId A = new CombatantId(20_001);
        private static readonly CombatantId B = new CombatantId(20_002);
        private static readonly CombatantId Victim = new CombatantId(20_003);
        /// <summary>신용 한도의 미룬 몫이 들어올 때 쓰는 "공격자 없음".</summary>
        private static readonly CombatantId NoAttacker = new CombatantId(-1);

        private static CombatRoundFactLedger NewLedger()
        {
            var ledger = new CombatRoundFactLedger(10d, (attacker, target) => attacker != target);
            ledger.BeginRound(1, new[] { A, B, Victim });
            return ledger;
        }

        private static CombatantRoundResult Result(CombatRoundResultsReady settled, CombatantId id)
        {
            Assert.That(settled.TryGetResult(id, out CombatantRoundResult result), Is.True);
            return result;
        }

        [Test]
        public void 축복_폭발과_독_피해도_실제_적_피해와_어시스트로_센다()
        {
            CombatRoundFactLedger ledger = NewLedger();

            Assert.That(ledger.RecordDamageFact(A, Victim, WeaponType.None, 12f, CombatDamageSource.Blessing, 1d, null),
                Is.True, "축복 폭발");
            Assert.That(ledger.RecordDamageFact(B, Victim, WeaponType.None, 4f, CombatDamageSource.Dot, 2d, null),
                Is.True, "독");
            Assert.That(ledger.RecordDownedFact(Victim, B, CombatDamageSource.Dot, 3d, null), Is.True);

            Assert.That(ledger.TrySettle(4d, out CombatRoundResultsReady settled), Is.True);
            Assert.That(Result(settled, B).Kills, Is.EqualTo(1), "독으로 쓰러뜨린 사람이 킬이다");
            Assert.That(Result(settled, A).Assists, Is.EqualTo(1), "축복 폭발로 깎은 사람은 어시스트다");
            Assert.That(Result(settled, A).EnemyDamageDealt, Is.EqualTo(12f));
            Assert.That(Result(settled, B).EnemyDamageDealt, Is.EqualTo(4f));
        }

        [Test]
        public void 보호막만_깎아도_어시스트지만_실제_피해량은_아니다()
        {
            CombatRoundFactLedger ledger = NewLedger();

            Assert.That(ledger.RecordShieldFact(A, Victim, CombatDamageSource.Weapon, 1d, null), Is.True);
            ledger.RecordDamageFact(B, Victim, WeaponType.Sword, 30f, CombatDamageSource.Weapon, 2d, null);
            ledger.RecordDownedFact(Victim, B, CombatDamageSource.Weapon, 3d, null);

            Assert.That(ledger.TrySettle(4d, out CombatRoundResultsReady settled), Is.True);
            Assert.That(Result(settled, A).Assists, Is.EqualTo(1), "보호막만 깎았어도 어시스트다");
            Assert.That(Result(settled, A).EnemyDamageDealt, Is.Zero, "체력이 줄지 않았으니 피해량은 아니다");
        }

        [Test]
        public void 보호막_기록도_자기장과_자기_자신은_받지_않는다()
        {
            CombatRoundFactLedger ledger = NewLedger();

            Assert.That(ledger.RecordShieldFact(A, Victim, CombatDamageSource.Zone, 1d, null), Is.False);
            Assert.That(ledger.RecordShieldFact(Victim, Victim, CombatDamageSource.Weapon, 1d, null), Is.False);
        }

        [Test]
        public void 공격자_없는_축복_피해로_쓰러지면_10초_안의_마지막_타격자가_킬이다()
        {
            CombatRoundFactLedger ledger = NewLedger();

            ledger.RecordDamageFact(A, Victim, WeaponType.Sword, 10f, CombatDamageSource.Weapon, 1d, null);
            ledger.RecordDamageFact(B, Victim, WeaponType.Sword, 10f, CombatDamageSource.Weapon, 2d, null);
            // 신용 한도의 미룬 몫이 마지막 한 대가 됐다 — 공격자가 없다.
            Assert.That(ledger.RecordDownedFact(Victim, NoAttacker, CombatDamageSource.Blessing, 3d, null), Is.True);

            Assert.That(ledger.TrySettle(4d, out CombatRoundResultsReady settled), Is.True);
            Assert.That(Result(settled, B).Kills, Is.EqualTo(1), "10초 안에 마지막으로 때린 사람이 킬이다");
            Assert.That(Result(settled, A).Assists, Is.EqualTo(1));
            Assert.That(Result(settled, Victim).AttributionPending, Is.False, "원인 미상으로 남기지 않는다");
            Assert.That(settled.TryGetResult(NoAttacker, out _), Is.False, "공격자 없음을 참가자로 만들지 않는다");
        }

        [Test]
        public void 쓰러진_공격자가_남긴_독으로_처치해도_킬은_센다()
        {
            // 사용자 확정 2026-09-18 — 쓰러진 사람의 킬도 올라간다. 처치 후 축복 효과만 없다(축복 쪽에서 막는다).
            CombatRoundFactLedger ledger = NewLedger();

            ledger.RecordDamageFact(A, Victim, WeaponType.None, 5f, CombatDamageSource.Dot, 1d, null);
            ledger.RecordDownedFact(A, B, CombatDamageSource.Weapon, 2d, null);
            ledger.RecordDownedFact(Victim, A, CombatDamageSource.Dot, 3d, null);

            Assert.That(ledger.TrySettle(4d, out CombatRoundResultsReady settled), Is.True);
            Assert.That(Result(settled, A).Kills, Is.EqualTo(1));
        }
    }
}

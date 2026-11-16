using System;
using BettingRoyale.Data.Combat;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 정산 경계가 소비하는 라운드 전투 사실 계약 테스트(S-GD-JRKHQA, O-025).
    /// 여기서는 순위·점수를 계산하지 않는다 — 원시 사실의 유효성만 본다.
    /// </summary>
    public class CombatRoundResultContractTests
    {
        [Test]
        public void EliminationOrder_IsNull_WhenNotSupplied()
        {
            var result = new CombatantRoundResult(new CombatantId(1), kills: 2, assists: 1, survived: true);

            Assert.That(result.EliminationOrder, Is.Null, "관측된 빈사가 없으면 탈락 순번은 null 이어야 합니다.");
            Assert.That(result.Survived, Is.True);
            Assert.That(result.AttributionPending, Is.False);
        }

        [Test]
        public void EliminationOrder_IsKept_ForDownedCombatant()
        {
            var result = new CombatantRoundResult(
                new CombatantId(4),
                kills: 0,
                assists: 0,
                survived: false,
                attributionPending: true,
                eliminationOrder: 3);

            Assert.That(result.EliminationOrder, Is.EqualTo(3));
            Assert.That(result.AttributionPending, Is.True);
        }

        [Test]
        public void Constructor_Rejects_NonPositiveEliminationOrder([Values(0, -1)] int eliminationOrder)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatantRoundResult(
                new CombatantId(2),
                kills: 0,
                assists: 0,
                survived: false,
                attributionPending: false,
                eliminationOrder: eliminationOrder));
        }

        [Test]
        public void Constructor_Rejects_SurvivorWithEliminationOrder()
        {
            Assert.Throws<ArgumentException>(() => new CombatantRoundResult(
                new CombatantId(2),
                kills: 0,
                assists: 0,
                survived: true,
                attributionPending: false,
                eliminationOrder: 1));
        }

        /// <summary>S-GD-7G8ZTA: 순위 원천 사실에 권위 틱과 실제 적 피해가 붙는다.</summary>
        [Test]
        public void EliminationTickAndEnemyDamage_AreOptionalAndDefaultToUnknown()
        {
            var legacy = new CombatantRoundResult(new CombatantId(1), kills: 1, assists: 0, survived: false,
                attributionPending: false, eliminationOrder: 2);

            Assert.That(legacy.EliminationTick, Is.Null, "틱을 모르는 옛 경계는 null 이어야 합니다.");
            Assert.That(legacy.EnemyDamageDealt, Is.Zero);

            var extended = new CombatantRoundResult(new CombatantId(1), kills: 1, assists: 0, survived: false,
                attributionPending: false, eliminationOrder: 2, eliminationTick: 80, enemyDamageDealt: 42.5f);

            Assert.That(extended.EliminationTick, Is.EqualTo(80));
            Assert.That(extended.EnemyDamageDealt, Is.EqualTo(42.5f));
        }

        [Test]
        public void Constructor_Rejects_SurvivorWithEliminationTick()
        {
            Assert.Throws<ArgumentException>(() => new CombatantRoundResult(
                new CombatantId(2), kills: 0, assists: 0, survived: true, attributionPending: false,
                eliminationOrder: null, eliminationTick: 3));
        }

        [Test]
        public void Constructor_Rejects_NegativeTickOrMalformedDamage(
            [Values(float.NaN, float.NegativeInfinity, -1f)] float damage)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatantRoundResult(
                new CombatantId(2), kills: 0, assists: 0, survived: false, attributionPending: false,
                eliminationOrder: 1, eliminationTick: -1));

            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatantRoundResult(
                new CombatantId(2), kills: 0, assists: 0, survived: true, attributionPending: false,
                eliminationOrder: null, eliminationTick: null, enemyDamageDealt: damage));
        }

        [Test]
        public void SettledSnapshot_CarriesAuthorityTick()
        {
            var settled = new CombatRoundResultsReady(
                round: 3,
                results: new[]
                {
                    new CombatantRoundResult(new CombatantId(1), 0, 0, survived: false,
                        attributionPending: false, eliminationOrder: 1, eliminationTick: 80,
                        enemyDamageDealt: 10f),
                },
                settledTick: 80);

            Assert.That(settled.SettledTick, Is.EqualTo(80));
            Assert.That(new CombatRoundResultsReady(3, settled.CopyResults()).SettledTick, Is.Null,
                "틱을 모르는 옛 생성자는 null 로 남는다.");
        }

        [Test]
        public void SnapshotRoundTrip_PreservesEliminationOrderPerCombatant()
        {
            var survivor = new CombatantId(1);
            var firstOut = new CombatantId(2);
            var secondOut = new CombatantId(3);
            var source = new[]
            {
                new CombatantRoundResult(survivor, kills: 2, assists: 0, survived: true),
                new CombatantRoundResult(firstOut, kills: 0, assists: 1, survived: false, attributionPending: false, eliminationOrder: 1),
                new CombatantRoundResult(secondOut, kills: 0, assists: 0, survived: false, attributionPending: true, eliminationOrder: 2),
            };

            var settled = new CombatRoundResultsReady(round: 2, results: source);

            // 방어 복사가 탈락 순번까지 지키는지 확인한다.
            source[1] = new CombatantRoundResult(firstOut, kills: 9, assists: 9, survived: false, attributionPending: false, eliminationOrder: 9);

            Assert.That(settled.TryGetResult(survivor, out CombatantRoundResult survivorResult), Is.True);
            Assert.That(survivorResult.EliminationOrder, Is.Null);

            Assert.That(settled.TryGetResult(firstOut, out CombatantRoundResult firstResult), Is.True);
            Assert.That(firstResult.EliminationOrder, Is.EqualTo(1));

            Assert.That(settled.TryGetResult(secondOut, out CombatantRoundResult secondResult), Is.True);
            Assert.That(secondResult.EliminationOrder, Is.EqualTo(2));
            Assert.That(secondResult.AttributionPending, Is.True);

            CombatantRoundResult[] exported = settled.CopyResults();
            Assert.That(exported[2].EliminationOrder, Is.EqualTo(2));
        }
    }
}

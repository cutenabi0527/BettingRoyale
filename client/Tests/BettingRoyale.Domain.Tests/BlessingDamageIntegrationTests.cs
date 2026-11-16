using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    public sealed class BlessingDamageIntegrationTests
    {
        private sealed class Map : ICombatantPlayerMap
        {
            public bool TryGetPlayerId(CombatantId id, out int playerId)
            { playerId = id.Value; return id.Value == 1 || id.Value == 2; }
        }

        [Test]
        public void PrimaryWeaponBonusesApplyAgainstAnUnregisteredTarget()
        {
            var registry = PlaceholderBlessingBalance.CreateStatRegistry();
            var resolver = new BlessingCombatResolver(new DamagePipeline(0), new Map(), registry);
            var sheet = new StatSheet(registry);
            sheet.Add(DamageStatKeys.AttackPower, 4, StatStackMode.Add, "combat-instinct-t1");
            resolver.SetSheet(1, sheet);
            Assert.That(resolver.ResolveDamage(new CombatantId(99),
                new DamageInfo(new CombatantId(1), WeaponType.Sword, 25)), Is.EqualTo(29));
        }

        [Test]
        public void AttackBonusesAddBeforePenaltiesAndDefense()
        {
            var registry = PlaceholderBlessingBalance.CreateStatRegistry();
            var attacker = new StatSheet(registry);
            var target = new StatSheet(registry);
            attacker.SetBase(DamageStatKeys.AttackPower, 100);
            attacker.Add(DamageStatKeys.DamageBonus, .2f, StatStackMode.Add, "upgrade");
            target.Add(DamageStatKeys.Defense, 10, StatStackMode.Add, "armor");
            var result = new DamagePipeline(0).Resolve(attacker, target,
                DamageRequest.Attack(false, .3f, .8f));
            Assert.That(result.Amount, Is.EqualTo(110).Within(.001));
        }

        [Test]
        public void CriticalFlagReachesDamagePipeline()
        {
            var registry = PlaceholderBlessingBalance.CreateStatRegistry();
            var resolver = new BlessingCombatResolver(new DamagePipeline(0), new Map(), registry);
            var sheet = new StatSheet(registry);
            sheet.Add(DamageStatKeys.CritDamage, .3f, StatStackMode.Add, "precision-aim-t1");
            resolver.SetSheet(1, sheet);
            Assert.That(resolver.ResolveDamage(new CombatantId(99),
                new DamageInfo(new CombatantId(1), WeaponType.Bow, 20, isCritical: true)),
                Is.EqualTo(46).Within(.001));
        }

        [Test]
        public void ProcDamageRetainsTargetDefenseWithoutRepeatingAttackBonuses()
        {
            var registry = PlaceholderBlessingBalance.CreateStatRegistry();
            var resolver = new BlessingCombatResolver(new DamagePipeline(0), new Map(), registry);
            var attack = new StatSheet(registry);
            attack.Add(DamageStatKeys.AttackPower, 100, StatStackMode.Add, "attack");
            resolver.SetSheet(1, attack);
            var defense = new StatSheet(registry);
            defense.Add(DamageStatKeys.Defense, 5, StatStackMode.Add, "armor");
            resolver.SetSheet(2, defense);
            Assert.That(resolver.ResolveDamage(new CombatantId(2),
                new DamageInfo(new CombatantId(1), WeaponType.Bow, 12, CombatDamageSource.Blessing)),
                Is.EqualTo(7));
        }
    }
}

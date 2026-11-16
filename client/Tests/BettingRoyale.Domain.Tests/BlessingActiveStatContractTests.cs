using System;
using System.Collections.Generic;
using System.Reflection;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Remote;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    public sealed class BlessingActiveStatContractTests
    {
        [TearDown] public void Cleanup() => GameContentProvider.Reset();

        // Explicit independent values: state activation is arranged here; trigger mechanics have separate tests.
        [TestCase("deadline", "deadline", "damage_bonus", "deadlineEndsAt", .23f)]
        [TestCase("interest-burst", "interestBurst", "damage_bonus", "battleElapsed", .23f)]
        [TestCase("chip-stack", "chipStack", "damage_per_stack", "chipStacksUntil", .46f)]
        [TestCase("cold-streak", "coldStreak", "damage_bonus", "currentHealth", .23f)]
        [TestCase("house-cut", "houseCut", "kill_bonus", "time", .46f)]
        [TestCase("final-offer", "finalOffer", "damage_bonus", "battleElapsed", .23f)]
        [TestCase("double-down", "doubleDown", "damage_bonus", "doubleDownUntil", .23f)]
        [TestCase("all-in", "allIn", "damage_bonus", "allInUntil", .23f)]
        [TestCase("overheat", "overheat", "max_damage_bonus", "overheatStacksUntil", .23f)]
        [TestCase("berserk", "berserk", "damage_bonus", "berserkUntil", .23f)]
        [TestCase("last_stand", "lastStand", "stat_bonus", "time", .23f)]
        [TestCase("sight-line", "sightLine", "damage_bonus", "time", .23f)]
        [TestCase("piercing-bet", "piercingBet", "damage_bonus", "time", .23f)]
        public void Active_damage_contract_matches_attack_without_consuming_state(
            string effect, string field, string key, string timer, float expected)
        {
            var state = new BlessingCombatState(Array.Empty<BlessingDefinition>());
            var definition = new BlessingDefinition("contract-t1", "contract", WeaponTag.Common, BlessingTier.Silver);
            var behavior = new BlessingMvpDefinition(definition, effect, -1, BlessingStackRule.Unique,
                BlessingDurationKind.Permanent,
                new[] { key, "duration", "hp_threshold", "remaining_seconds", "still_seconds", "start_seconds", "range_bonus", "pierce_targets", "damage_multiplier", "max_stacks" },
                new[] { .23f, 100f, .5f, 100f, 1f, 0f, .1f, 2f, .75f, 4f });
            Set(state, field, behavior);
            Set(state, "combatActive", true);
            Set(state, "deadlineTriggered", true); Set(state, "interestDeposited", true);
            Set(state, "lastStandTriggered", true); Set(state, "chipStacks", 2); Set(state, "houseCutStacks", 2);
            Set(state, "overheatStacks", 4);
            Set(state, "maxHealth", 100f); Set(state, "combatDurationSeconds", 100f);
            Set(state, "time", 2f); Set(state, timer, 20f);
            Assert.That(state.ActiveDamageBonus(WeaponTag.Bow), Is.EqualTo(expected).Within(.0001f));
            Assert.That(state.ActiveDamageBonus(WeaponTag.Bow), Is.EqualTo(expected).Within(.0001f));
            Assert.That(state.BeginAttack(2f, WeaponTag.Bow).DamageBonus, Is.EqualTo(expected).Within(.0001f));
            Assert.That(state.BeginAttack(20f, WeaponTag.Bow).DamageBonus, Is.EqualTo(
                timer.EndsWith("Until", StringComparison.Ordinal) || timer == "deadlineEndsAt" ? 0f : expected).Within(.0001f),
                "At expiry, the attack must not retain the displayed timed bonus.");
            state.ResetRound(21f);
            Assert.That(state.ActiveDamageBonus(WeaponTag.Bow), Is.EqualTo(effect == "piercing-bet" ? .23f : 0f).Within(.0001f));
            if (effect == "piercing-bet" || effect == "sight-line")
                Assert.That(state.ActiveDamageBonus(WeaponTag.Sword), Is.Zero);

        }

        [TestCase("houseEdge", "house-edge")]
        [TestCase("coverFire", "cover-fire")]
        [TestCase("guardRail", "guard-rail")]
        public void Active_defense_contract_is_shared_and_does_not_double_apply(string field, string effect)
        {
            var state = new BlessingCombatState(Array.Empty<BlessingDefinition>());
            var d = new BlessingDefinition("contract-t1", "contract", WeaponTag.Common, BlessingTier.Silver);
            Set(state, field, new BlessingMvpDefinition(d, effect, -1, BlessingStackRule.Unique,
                BlessingDurationKind.Timed, new[] { "damage_reduction" }, new[] { .2f }));
            Set(state, "coverFireUntil", 10f); Set(state, "guardRailUntil", 10f);
            Assert.That(state.ActiveDamageTakenBonus(1f), Is.EqualTo(-.2f).Within(.0001f));
            Assert.That(state.IncomingDamageTakenBonus(1f, true), Is.EqualTo(-.2f).Within(.0001f));
            Assert.That(state.IncomingDamageTakenBonus(1f, false), Is.EqualTo(-.2f).Within(.0001f));
        }

        private static void Set(BlessingCombatState state, string name, object value)
        {
            var field = typeof(BlessingCombatState).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(state, value);
        }
    }
}

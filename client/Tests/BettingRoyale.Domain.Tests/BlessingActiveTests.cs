using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Remote;
using MessagePack;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 직접 눌러 쓰는 축복(액티브) — 한 번에 하나, 누를 때만 켜짐, 대가는 누를 때 한 번
    /// (S-GD-NJQ5NM · S-GD-H8MSNC · S-GD-M7D8QT).
    /// </summary>
    [TestFixture]
    public sealed class BlessingActiveTests
    {
        private const int Seed = 20260918;

        // ------------------------------------------------------------ 뽑기 — 한 번에 하나 (S-GD-NJQ5NM §2)

        /// <summary>골드 등급에 액티브 둘과 패시브 넷. 검 기준으로 전부 후보가 될 수 있다.</summary>
        private static IReadOnlyList<BlessingDefinition> GoldPool() => new[]
        {
            new BlessingDefinition("active_a", "액티브A", WeaponTag.Common, BlessingTier.Gold, isActive: true),
            new BlessingDefinition("active_b", "액티브B", WeaponTag.Common, BlessingTier.Gold, isActive: true),
            new BlessingDefinition("passive_0", "패시브0", WeaponTag.Common, BlessingTier.Gold),
            new BlessingDefinition("passive_1", "패시브1", WeaponTag.Common, BlessingTier.Gold),
            new BlessingDefinition("passive_2", "패시브2", WeaponTag.Sword, BlessingTier.Gold),
            new BlessingDefinition("passive_3", "패시브3", WeaponTag.Common, BlessingTier.Gold),
        };

        private static BlessingLoadout HoldingActive()
        {
            var loadout = new BlessingLoadout();
            loadout.Add(new BlessingDefinition("held_active", "보유 액티브", WeaponTag.Common, BlessingTier.Silver,
                isActive: true));
            return loadout;
        }

        private static BlessingDraft Begin(BlessingLoadout acquired, int seed) =>
            BlessingDraft.Begin(BlessingConfig.Default, GoldPool(), WeaponTag.Sword, round: 2, acquired,
                new DeterministicRandom(seed));

        [Test]
        public void 액티브를_가졌으면_다른_액티브는_후보에_나오지_않는다()
        {
            // 한 번의 운이 아니라 규칙이어야 한다 — 여러 씨앗과 재추첨까지 전부 본다.
            for (int seed = 0; seed < 50; seed++)
            {
                BlessingDraft draft = Begin(HoldingActive(), Seed + seed);
                Assert.That(draft.Candidates.Select(c => c.Blessing.IsActive), Has.None.True, $"seed {seed}");
                Assert.That(draft.PoolSize, Is.EqualTo(4), "빠진 자리는 패시브가 채운다");
            }
        }

        [Test]
        public void 재추첨해도_다른_액티브는_나오지_않는다()
        {
            var wallet = new BettingRoyale.Data.Economy.Wallet(10000);
            BlessingDraft draft = Begin(HoldingActive(), Seed);

            for (int i = 0; i < 5; i++)
            {
                draft.TryReroll(wallet);
                Assert.That(draft.Candidates.Select(c => c.Blessing.IsActive), Has.None.True, $"reroll {i}");
            }
        }

        [Test]
        public void 액티브가_없으면_액티브도_후보가_된다()
        {
            Assert.That(Begin(new BlessingLoadout(), Seed).PoolSize, Is.EqualTo(6));
        }

        [Test]
        public void 패시브만_가졌으면_액티브를_막지_않는다()
        {
            var loadout = new BlessingLoadout();
            loadout.Add(new BlessingDefinition("held_passive", "보유 패시브", WeaponTag.Common, BlessingTier.Silver));

            Assert.That(loadout.HasActive, Is.False);
            Assert.That(Begin(loadout, Seed).PoolSize, Is.EqualTo(6));
        }

        // ------------------------------------------------------------ 엑셀 표시 (S-GD-NJQ5NM §4)

        [TestCase("Active", true)]
        [TestCase("Timed", false)]
        [TestCase("", false)]
        [TestCase("active", false)]
        public void 엑셀_duration_kind_가_Active_면_액티브다(string durationKind, bool expected)
        {
            var data = new GameData
            {
                SchemaVersion = 2,
                Blessings = new[]
                {
                    new BlessingRow
                    {
                        Id = "double-down-t2", Name = "더블 다운", Tier = "Gold", Tag = "Common",
                        DurationKind = durationKind,
                    },
                },
            };

            GameDataLoadResult result = GameDataLoader.Load(MessagePackSerializer.Serialize(data));

            Assert.That(result.Success, Is.True, result.Failure);
            Assert.That(result.Content.Blessings[0].IsActive, Is.EqualTo(expected));
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void 코드_축복의_지속_규칙과_액티브_표시가_어긋나면_만들지_않는다(bool activeKind, bool activeFlag)
        {
            // 뽑기는 IsActive, 전투는 지속 규칙으로 액티브를 알아본다 — 갈리면 한 번에 하나 제한이 조용히 풀린다.
            var definition = new BlessingDefinition("x-t1", "x", WeaponTag.Common, BlessingTier.Silver, isActive: activeFlag);

            Assert.Throws<System.ArgumentException>(() => new BlessingMvpDefinition(definition, "x", -1,
                BlessingStackRule.Unique, activeKind ? BlessingDurationKind.Active : BlessingDurationKind.Timed,
                new string[0], new float[0]));
        }

        // ------------------------------------------------------------ 발동 — 누를 때만, 대가는 한 번 (S-GD-H8MSNC)

        private static BlessingCombatState StateWith(string id)
        {
            Assert.That(BlessingMvpCatalog.TryGet(id, out BlessingMvpDefinition entry), Is.True, id);
            var state = new BlessingCombatState(new[] { entry.Definition });
            state.Tick(1f, 0f, combatActive: true, moving: false);
            return state;
        }

        private static float Number(string id, string key)
        {
            BlessingMvpCatalog.TryGet(id, out BlessingMvpDefinition entry);
            return entry.Number(key);
        }

        [TestCase("double-down-t2")]
        [TestCase("all-in-t3")]
        public void 공격해도_저절로_켜지지_않는다(string id)
        {
            BlessingCombatState state = StateWith(id);

            BlessingAttack attack = state.BeginAttack(1f, WeaponTag.Sword);

            Assert.That(attack.DamageBonus, Is.EqualTo(0f));
            Assert.That(attack.SelfHealthCostFraction, Is.EqualTo(0f), "누르지 않았으면 대가도 없다");
            Assert.That(state.ActiveBlessingReady, Is.True, "공격이 재사용 대기를 쓰지 않는다");
        }

        [TestCase("double-down-t2")]
        [TestCase("all-in-t3")]
        public void 누르면_켜지고_켜진_동안_공격은_보너스를_받되_대가는_다시_내지_않는다(string id)
        {
            BlessingCombatState state = StateWith(id);
            float bonus = Number(id, "damage_bonus");

            Assert.That(state.ActiveBlessingHealthCost, Is.EqualTo(Number(id, "hp_cost")), "누를 때 낼 대가");
            Assert.That(state.ActivateReadyActive(1f), Is.True);

            for (int i = 0; i < 5; i++)
            {
                state.Tick(1.5f + i, 0f, combatActive: true, moving: false);
                BlessingAttack attack = state.BeginAttack(1.5f + i, WeaponTag.Sword);
                Assert.That(attack.DamageBonus, Is.EqualTo(bonus).Within(1e-6f), $"{i}번째 공격");
                Assert.That(attack.SelfHealthCostFraction, Is.EqualTo(0f), $"{i}번째 공격은 대가를 내지 않는다");
            }
        }

        [TestCase("double-down-t2")]
        [TestCase("all-in-t3")]
        public void 지속이_끝나면_보너스가_사라지고_재사용_대기는_누른_순간부터_센다(string id)
        {
            BlessingCombatState state = StateWith(id);
            float duration = Number(id, "duration");
            float cooldown = Number(id, "cooldown");
            state.ActivateReadyActive(1f);

            float afterDuration = 1f + duration + 0.01f;
            state.Tick(afterDuration, 0f, combatActive: true, moving: false);
            Assert.That(state.BeginAttack(afterDuration, WeaponTag.Sword).DamageBonus, Is.EqualTo(0f));

            state.Tick(1f + cooldown - 0.01f, 0f, combatActive: true, moving: false);
            Assert.That(state.ActiveBlessingReady, Is.False, "아직 대기 중");
            Assert.That(state.ActiveBlessingHealthCost, Is.EqualTo(0f), "대기 중엔 낼 대가도 없다");
            Assert.That(state.ActivateReadyActive(1f + cooldown - 0.01f), Is.False, "대기 중에 누르면 아무 일도 없다");

            state.Tick(1f + cooldown, 0f, combatActive: true, moving: false);
            Assert.That(state.ActiveBlessingReady, Is.True);
        }

        [Test]
        public void 라운드가_시작하면_바로_쓸_수_있다()
        {
            BlessingCombatState state = StateWith("double-down-t2");
            state.ActivateReadyActive(1f);
            Assert.That(state.ActiveBlessingReady, Is.False);

            state.ResetRound(2f);

            Assert.That(state.ActiveBlessingReady, Is.True);
            Assert.That(state.BeginAttack(2f, WeaponTag.Sword).DamageBonus, Is.EqualTo(0f), "켜진 상태도 풀린다");
        }

        // ------------------------------------------------------------ 켜진 동안의 추가 효과 (엑셀 parameters)

        [TestCase("double-down-t2")]
        [TestCase("all-in-t3")]
        public void 켜진_동안만_이동_속도가_오른다(string id)
        {
            BlessingCombatState state = StateWith(id);
            Assert.That(state.MoveBonus, Is.EqualTo(0f), "누르기 전");

            state.ActivateReadyActive(1f);
            state.Tick(2f, 0f, combatActive: true, moving: false);
            Assert.That(state.MoveBonus, Is.EqualTo(Number(id, "move_bonus")).Within(1e-6f));

            state.Tick(1f + Number(id, "duration") + 0.01f, 0f, combatActive: true, moving: false);
            Assert.That(state.MoveBonus, Is.EqualTo(0f), "지속이 끝나면 사라진다");
        }

        private sealed class OnePlayer : ICombatantPlayerMap
        {
            public bool TryGetPlayerId(BettingRoyale.Data.Combat.CombatantId id, out int player)
            {
                player = 1;
                return true;
            }
        }

        [Test]
        public void 올인은_켜진_동안만_공격력과_방어구_관통을_계산표에_얹는다()
        {
            StatRegistry registry = PlaceholderBlessingBalance.CreateStatRegistry();
            var resolver = new BlessingCombatResolver(new DamagePipeline(0f), new OnePlayer(), registry);
            var sheet = new StatSheet(registry);
            resolver.SetSheet(1, sheet);
            var me = new BettingRoyale.Data.Combat.CombatantId(7);
            BlessingCombatState state = StateWith("all-in-t3");

            resolver.SyncTimedStats(me, state.TimedStats);
            Assert.That(sheet.Get(DamageStatKeys.AttackPower, 25f), Is.EqualTo(25f), "누르기 전");

            state.ActivateReadyActive(1f);
            state.Tick(2f, 0f, combatActive: true, moving: false);
            resolver.SyncTimedStats(me, state.TimedStats);
            resolver.SyncTimedStats(me, state.TimedStats); // 매 틱 불려도 두 번 얹히지 않는다
            Assert.That(sheet.Get(DamageStatKeys.AttackPower, 25f), Is.EqualTo(25f + Number("all-in-t3", "atk_flat")));
            Assert.That(sheet.Get(DamageStatKeys.ArmorPen), Is.EqualTo(Number("all-in-t3", "armor_pen")));

            state.Tick(1f + Number("all-in-t3", "duration") + 0.01f, 0f, combatActive: true, moving: false);
            resolver.SyncTimedStats(me, state.TimedStats);
            Assert.That(sheet.Get(DamageStatKeys.AttackPower, 25f), Is.EqualTo(25f), "끝나면 걷힌다");
            Assert.That(sheet.Get(DamageStatKeys.ArmorPen), Is.EqualTo(0f));
        }

        [Test]
        public void 올인이_시간을_채우고_끝나면_한_번_회복한다()
        {
            BlessingCombatState state = StateWith("all-in-t3");
            state.ActivateReadyActive(1f);

            state.Tick(2f, 0f, combatActive: true, moving: false);
            Assert.That(state.ConsumePendingHealFraction(), Is.EqualTo(0f), "켜진 동안은 아직");

            state.Tick(1f + Number("all-in-t3", "duration"), 0f, combatActive: true, moving: false);
            Assert.That(state.ConsumePendingHealFraction(), Is.EqualTo(Number("all-in-t3", "heal_on_end")).Within(1e-6f));
            Assert.That(state.ConsumePendingHealFraction(), Is.EqualTo(0f), "한 번만");

            state.Tick(20f, 0f, combatActive: true, moving: false);
            Assert.That(state.ConsumePendingHealFraction(), Is.EqualTo(0f), "다시 켜기 전엔 또 나오지 않는다");
        }

        [Test]
        public void 올인_도중에_라운드가_바뀌면_회복하지_않는다()
        {
            BlessingCombatState state = StateWith("all-in-t3");
            state.ActivateReadyActive(1f);

            state.ResetRound(2f);
            state.Tick(30f, 0f, combatActive: true, moving: false);

            Assert.That(state.ConsumePendingHealFraction(), Is.EqualTo(0f));
        }

        [Test]
        public void 더블_다운은_끝나도_회복하지_않는다()
        {
            BlessingCombatState state = StateWith("double-down-t2");
            state.ActivateReadyActive(1f);

            state.Tick(1f + Number("double-down-t2", "duration") + 1f, 0f, combatActive: true, moving: false);

            Assert.That(state.ConsumePendingHealFraction(), Is.EqualTo(0f));
            Assert.That(state.TimedStats, Is.Empty);
        }

        [Test]
        public void 액티브가_없으면_누를_것도_없다()
        {
            BlessingCombatState state = StateWith("chip-stack-t1");

            Assert.That(state.ActiveBlessingReady, Is.False);
            Assert.That(state.ActiveBlessingHealthCost, Is.EqualTo(0f));
            Assert.That(state.ActivateReadyActive(1f), Is.False);
        }
    }
}

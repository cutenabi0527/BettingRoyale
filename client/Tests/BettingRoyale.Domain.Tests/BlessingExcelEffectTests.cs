using System;
using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Remote;
using MessagePack;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 엑셀에만 있는 축복의 전투 동작 — 엑셀 <c>effect</c> 이름과 <c>parameters</c> 로 돈다 (S-GD-RTB6H2 · S-GD-PA3E9T).
    /// 수치는 2026-09-18 엑셀 값이다.
    /// </summary>
    [TestFixture]
    public sealed class BlessingExcelEffectTests
    {
        [TearDown]
        public void TearDown() => GameContentProvider.Reset();

        /// <summary>엑셀 전용 축복 하나를 올리고 그 축복만 가진 전투 상태를 만든다.</summary>
        private static BlessingCombatState With(string effect, params (string Key, float Value)[] numbers)
        {
            string id = effect.Replace('_', '-') + "-t1";
            var definition = new BlessingDefinition(id, id, WeaponTag.Common, BlessingTier.Silver);
            var behavior = new BlessingMvpDefinition(definition, effect, BlessingMvpDefinition.MissingIconIndex,
                BlessingStackRule.Unique, BlessingDurationKind.Permanent,
                numbers.Select(n => n.Key).ToArray(), numbers.Select(n => n.Value).ToArray());
            GameContentProvider.Set(new GameContent(2, null, new[] { definition }, null, null, null,
                new Dictionary<string, BlessingMvpDefinition>(StringComparer.Ordinal) { [id] = behavior }));
            var state = new BlessingCombatState(new[] { definition });
            state.Tick(1f, 0f, combatActive: true, moving: false);
            return state;
        }

        /// <summary>글자 파라미터(<c>compare_mode</c> 등)까지 얹은 전투 상태.</summary>
        private static BlessingCombatState WithText(string effect, (string Key, string Value)[] texts,
            params (string Key, float Value)[] numbers)
        {
            string id = effect.Replace('_', '-') + "-t1";
            var definition = new BlessingDefinition(id, id, WeaponTag.Common, BlessingTier.Silver);
            var behavior = new BlessingMvpDefinition(definition, effect, BlessingMvpDefinition.MissingIconIndex,
                BlessingStackRule.Unique, BlessingDurationKind.Permanent,
                numbers.Select(n => n.Key).ToArray(), numbers.Select(n => n.Value).ToArray(), statTokens: null,
                parameterTexts: texts.Select(t => new KeyValuePair<string, string>(t.Key, t.Value)).ToArray());
            GameContentProvider.Set(new GameContent(2, null, new[] { definition }, null, null, null,
                new Dictionary<string, BlessingMvpDefinition>(StringComparer.Ordinal) { [id] = behavior }));
            var state = new BlessingCombatState(new[] { definition });
            state.Tick(1f, 0f, combatActive: true, moving: false);
            return state;
        }

        /// <summary>맞는다 — 보통 공격(피격 트리거 발동)이나 자기장(발동 안 함).</summary>
        private static void Hit(BlessingCombatState state, float now, float damage, float healthAfter, bool zone = false)
        {
            state.BeforeDamage(now, damage, healthAfter + damage, 100f, !zone, zone ? float.PositiveInfinity : 1f,
                fixedDamage: zone, firesOnDamaged: !zone);
            state.AfterDamage(now, damage, healthAfter, 100f, !zone);
        }

        // ── 불러오기 ────────────────────────────────────────────────────

        [Test]
        public void 엑셀_전용_축복은_효과_이름과_숫자_파라미터로_불러온다()
        {
            var data = new GameData
            {
                SchemaVersion = 2,
                Blessings = new[]
                {
                    new BlessingRow
                    {
                        Id = "booster-t1", Name = "부스터", Tier = "Silver", Tag = "Melee", Effect = "booster",
                        StackRule = "Refresh", DurationKind = "Timed",
                        Parameters = new[]
                        {
                            new KeyValueRow { Key = "move_speed", Value = "0.25" },
                            new KeyValueRow { Key = "duration", Value = "2" },
                            new KeyValueRow { Key = "invuln_kind", Value = "ATTACKABLE" },
                            new KeyValueRow { Key = "no_stack_on_invuln", Value = "true" },
                        },
                    },
                    // 효과 이름이 코드 축복과 겹치지만 동작이 다르다 — 쓰지 않고 경고한다.
                    new BlessingRow
                    {
                        Id = "interest-burst-t1", Name = "이자 폭발", Tier = "Silver", Tag = "Common",
                        Effect = "interest-burst",
                    },
                },
            };

            GameDataLoadResult result = GameDataLoader.Load(MessagePackSerializer.Serialize(data));
            Assert.That(result.Success, Is.True, result.Failure);
            GameContentProvider.Set(result.Content);

            Assert.That(BlessingMvpCatalog.TryGet("booster-t1", out BlessingMvpDefinition booster), Is.True);
            Assert.That(booster.Effect, Is.EqualTo("booster"));
            Assert.That(booster.Number("move_speed"), Is.EqualTo(0.25f));
            Assert.That(booster.Number("no_stack_on_invuln"), Is.EqualTo(1f), "true 는 1");
            Assert.That(booster.NumberKeys, Has.No.Member("invuln_kind"), "숫자가 아닌 값은 빠진다");
            Assert.That(booster.StackRule, Is.EqualTo(BlessingStackRule.Refresh));
            Assert.That(booster.DurationKind, Is.EqualTo(BlessingDurationKind.Timed));

            Assert.That(BlessingMvpCatalog.TryGet("interest-burst-t1", out _), Is.False);
            Assert.That(result.Warnings.Any(w => w.Contains("interest-burst-t1")), Is.True);
            Assert.That(BlessingMvpCatalog.ContainsCode("booster-t1"), Is.False);
        }

        [Test]
        public void 엑셀이_없으면_엑셀_전용_축복은_없다()
        {
            GameContentProvider.Reset();
            Assert.That(BlessingMvpCatalog.TryGet("booster-t1", out _), Is.False);
        }

        // ── 이동 속도 ───────────────────────────────────────────────────

        [Test]
        public void 가벼운_발걸음은_처음_맞기_전까지만_빠르다_자기장은_세지_않는다()
        {
            var state = With("light_step", ("move_speed", 0.25f));
            Assert.That(state.MoveBonus, Is.EqualTo(0.25f));

            Hit(state, 2f, 5f, 95f, zone: true);
            Assert.That(state.MoveBonus, Is.EqualTo(0.25f), "자기장은 피격이 아니다");

            Hit(state, 3f, 5f, 90f);
            Assert.That(state.MoveBonus, Is.EqualTo(0f));

            state.ResetRound(10f);
            Assert.That(state.MoveBonus, Is.EqualTo(0.25f), "라운드마다 다시");
        }

        [Test]
        public void 부스터는_대시하면_잠깐_빨라진다()
        {
            var state = With("booster", ("move_speed", 0.25f), ("duration", 2f));
            state.OnDash(2f, WeaponTag.Sword);
            state.Tick(3f, 0f, true, false);
            Assert.That(state.MoveBonus, Is.EqualTo(0.25f));
            state.Tick(4.01f, 0f, true, false);
            Assert.That(state.MoveBonus, Is.EqualTo(0f));
        }

        [Test]
        public void 아야는_맞으면_잠깐_빨라지고_다시_맞으면_시간이_초기화된다()
        {
            var state = With("ouch", ("move_speed", 0.30f), ("duration", 2f));
            Hit(state, 2f, 5f, 95f);
            state.Tick(3.5f, 0f, true, false);
            Hit(state, 3.5f, 5f, 90f);
            state.Tick(5f, 0f, true, false);
            Assert.That(state.MoveBonus, Is.EqualTo(0.30f).Within(1e-6f), "3.5 초에 다시 맞아 5.5 초까지");
            state.Tick(5.6f, 0f, true, false);
            Assert.That(state.MoveBonus, Is.EqualTo(0f));
        }

        [Test]
        public void 쾌속_접근은_거리_안의_적에게_다가갈_때만_빠르다()
        {
            var state = With("close_in", ("move_speed", 0.15f), ("trigger_distance", 10f));
            Assert.That(state.WantsApproach, Is.True);
            Assert.That(state.CloseInDistance, Is.EqualTo(10f));
            state.SetApproach(true, false, false);
            Assert.That(state.MoveBonus, Is.EqualTo(0.15f));
            state.SetApproach(false, false, false);
            Assert.That(state.MoveBonus, Is.EqualTo(0f));
        }

        [Test]
        public void 신난다는_처치하면_켜지고_또_처치하면_늘어난다()
        {
            var state = With("hype", ("move_speed", 0.45f), ("attack_speed", 0.45f), ("duration", 3f),
                ("extend_amount", 3f));
            state.NotifyKill(2f);
            state.NotifyKill(3f);
            state.Tick(7.5f, 0f, true, false);
            Assert.That(state.MoveBonus, Is.EqualTo(0.45f).Within(1e-6f), "2+3 초에 3 초가 더해져 8 초까지");
            Assert.That(state.AttackSpeedBonus, Is.EqualTo(0.45f).Within(1e-6f));
            state.Tick(8.01f, 0f, true, false);
            Assert.That(state.MoveBonus, Is.EqualTo(0f));
        }

        [Test]
        public void 신난다는_어시스트로도_켜지고_늘어난다()
        {
            // S-GD-NN5PR4 #3 — 처치와 어시스트 모두.
            var state = With("hype", ("move_speed", 0.45f), ("attack_speed", 0.45f), ("duration", 3f),
                ("extend_amount", 3f));
            state.NotifyAssist(2f, 5);
            Assert.That(state.MoveBonus, Is.EqualTo(0.45f).Within(1e-6f), "어시스트로 켜진다");
            Assert.That(state.HypeRemaining, Is.EqualTo(3f).Within(1e-6f));
            state.NotifyAssist(3f, 6);
            state.Tick(7.5f, 0f, true, false);
            Assert.That(state.AttackSpeedBonus, Is.EqualTo(0.45f).Within(1e-6f), "또 얻으면 늘어난다(2+3+3=8)");
            state.Tick(8.01f, 0f, true, false);
            Assert.That(state.MoveBonus, Is.EqualTo(0f));
        }

        [Test]
        public void 어시스트는_처치_전용_보상을_주지_않는다()
        {
            var state = With("hunter_mark", ("chase_angle", 60f), ("move_speed", 0.10f), ("damage_bonus", 0.05f),
                ("kill_reward_gold", 100f));
            state.NotifyPrimaryHit(7, WeaponTag.Sword, false, 1);
            state.NotifyAssist(2f, 7);
            Assert.That(state.ConsumeEarnedGold(), Is.EqualTo(0), "사냥꾼 골드는 처치자 몫이다");
            Assert.That(state.HunterTarget, Is.EqualTo(7), "표식도 그대로다");
        }

        [Test]
        public void 사냥꾼은_주무기로_마지막_한_대를_넣어_쓰러뜨려도_골드를_받는다()
        {
            // 처치 알림이 표식 갱신보다 먼저 와도 그 적중이 "마지막으로 때린 적" 이다(S-GD-Y534KH).
            var state = With("hunter_mark", ("chase_angle", 60f), ("move_speed", 0.10f), ("damage_bonus", 0.05f),
                ("kill_reward_gold", 100f));
            state.NotifyPrimaryHit(3, WeaponTag.Sword, false, 1);
            state.NotifyKill(2f, 9, killingBlowByPrimary: true);
            Assert.That(state.ConsumeEarnedGold(), Is.EqualTo(100));
            state.NotifyKill(3f, 4, killingBlowByPrimary: false);
            Assert.That(state.ConsumeEarnedGold(), Is.EqualTo(0), "주무기가 아니면 표식 대상만");
        }

        // ── 처치·어시스트 귀속 (S-GD-NMFBE9 · S-GD-Y534KH) ────────────────

        [Test]
        public void 쓰러지면_처치자를_빼고_10초_안에_깎은_전원이_어시스트다()
        {
            var state = new BlessingCombatState(null);
            state.RecordDamageFrom(3, 1f);
            state.RecordDamageFrom(4, 5f);
            state.RecordDamageFrom(7, 8f);
            var assists = new List<int>();

            int killer = state.ResolveDownCredit(7, 11f, assists);

            Assert.That(killer, Is.EqualTo(7));
            Assert.That(assists, Is.EqualTo(new[] { 3, 4 }), "경계인 정확히 10초(1 → 11)도 포함");
        }

        [Test]
        public void 창_밖의_피해와_라운드가_바뀐_피해는_어시스트가_아니다()
        {
            var state = new BlessingCombatState(null);
            state.RecordDamageFrom(3, 1f);
            var assists = new List<int>();
            state.ResolveDownCredit(9, 11.01f, assists);
            Assert.That(assists, Is.Empty, "10초를 넘었다");

            state.RecordDamageFrom(4, 12f);
            state.ResetRound(0f);
            state.ResolveDownCredit(9, 1f, assists);
            Assert.That(assists, Is.Empty, "라운드가 바뀌면 기록이 지워진다");
        }

        [Test]
        public void 전투원이_없는_피해로_쓰러지면_10초_안의_마지막_타격자가_처치자다()
        {
            // 자기장 사망 규칙(S-GD-87YH9R)과 같다 — 가장 최근에 깎은 사람이 처치, 나머지는 어시스트.
            var state = new BlessingCombatState(null);
            state.RecordDamageFrom(3, 2f);
            state.RecordDamageFrom(4, 6f);
            var assists = new List<int>();

            int killer = state.ResolveDownCredit(BlessingCombatState.NoTarget, 10f, assists);

            Assert.That(killer, Is.EqualTo(4));
            Assert.That(assists, Is.EqualTo(new[] { 3 }));
        }

        [Test]
        public void 아무도_깎지_않았으면_처치자도_어시스트도_없다()
        {
            var state = new BlessingCombatState(null);
            var assists = new List<int> { 99 };
            Assert.That(state.ResolveDownCredit(BlessingCombatState.NoTarget, 5f, assists),
                Is.EqualTo(BlessingCombatState.NoTarget));
            Assert.That(assists, Is.Empty);
        }

        [Test]
        public void 같은_사람의_여러_타격은_어시스트_한_번이고_기록은_한_번_쓰면_비워진다()
        {
            var state = new BlessingCombatState(null);
            state.RecordDamageFrom(3, 1f);
            state.RecordDamageFrom(3, 2f);
            var assists = new List<int>();
            state.ResolveDownCredit(8, 3f, assists);
            Assert.That(assists, Is.EqualTo(new[] { 3 }));
            state.ResolveDownCredit(8, 4f, assists);
            Assert.That(assists, Is.Empty, "쓰러짐 한 번에 한 번만 센다");
        }

        // ── 대상에 따른 피해 ────────────────────────────────────────────

        private static BlessingAttack Swing(BlessingCombatState state, float now) => state.BeginAttack(now, WeaponTag.Sword);

        [Test]
        public void 사냥꾼은_마지막으로_때린_적에게_표식을_남기고_그_적에게만_강하다()
        {
            var state = With("hunter_mark", ("chase_angle", 60f), ("move_speed", 0.10f), ("damage_bonus", 0.05f));
            state.NotifyPrimaryHit(7, WeaponTag.Sword, false, 1);
            Assert.That(state.HunterTarget, Is.EqualTo(7));
            Assert.That(state.HunterChaseHalfAngle, Is.EqualTo(60f));

            var attack = Swing(state, 2f);
            Assert.That(state.PreviewHit(attack, 7, 0f, 0.5f, false).DamageBonus, Is.EqualTo(0.05f).Within(1e-6f));
            Assert.That(state.PreviewHit(attack, 8, 0f, 0.5f, false).DamageBonus, Is.EqualTo(0f));

            state.SetApproach(false, true, false);
            Assert.That(state.MoveBonus, Is.EqualTo(0.10f).Within(1e-6f));
        }

        [Test]
        public void 분노의_질주는_정해진_적에게만_강하다()
        {
            var state = With("rage_dash", ("move_speed", 0.25f), ("damage_bonus", 0.15f));
            Assert.That(state.TracksTopDamageDealer, Is.True);
            state.SetRageTarget(9);
            var attack = Swing(state, 2f);
            Assert.That(state.PreviewHit(attack, 9, 0f, 0.5f, false).DamageBonus, Is.EqualTo(0.15f).Within(1e-6f));
            Assert.That(state.PreviewHit(attack, 3, 0f, 0.5f, false).DamageBonus, Is.EqualTo(0f));
            state.SetApproach(false, false, true);
            Assert.That(state.MoveBonus, Is.EqualTo(0.25f));
        }

        [Test]
        public void 언더독은_나보다_체력_비율이_높은_적에게_강하고_그_적에게_덜_맞는다()
        {
            var state = With("underdog", ("damage_bonus", 1.20f), ("damage_taken", -0.10f));
            state.SetHealthSnapshot(50f, 100f);
            var attack = Swing(state, 2f);
            var higher = new BlessingCombatState.TargetContext(0.9f, -1f, -1f);
            var lower = new BlessingCombatState.TargetContext(0.3f, -1f, -1f);
            Assert.That(state.PreviewHit(attack, 1, 0f, 0.5f, false, -1f, higher).DamageBonus, Is.EqualTo(1.20f).Within(1e-6f));
            Assert.That(state.PreviewHit(attack, 1, 0f, 0.5f, false, -1f, lower).DamageBonus, Is.EqualTo(0f));

            // 받는 피해 −10% 는 계산 4단계 합산에 더한다(S-GD-SH74SC) — 뒤에서 곱하지 않는다.
            Assert.That(state.IncomingDamageTakenBonus(3f, true, attackerHealthRatio: 0.9f), Is.EqualTo(-0.10f).Within(1e-6f));
            Assert.That(state.IncomingDamageTakenBonus(3f, true, attackerHealthRatio: 0.3f), Is.EqualTo(0f));
            float fromHigher = state.BeforeDamage(3f, 20f, 50f, 100f, true, 1f, false, true, attackerHealthRatio: 0.9f);
            Assert.That(fromHigher, Is.EqualTo(20f).Within(1e-4f), "계산이 끝난 피해에 다시 곱하지 않는다");
        }

        [Test]
        public void 약자_멸시는_공격력이_낮은_적에게_강하고_같거나_높으면_약하다()
        {
            var state = With("bully", ("damage_bonus", 1.20f), ("damage_penalty", -0.10f));
            var attack = Swing(state, 2f);
            var weaker = new BlessingCombatState.TargetContext(-1f, 15f, 25f);
            var equal = new BlessingCombatState.TargetContext(-1f, 25f, 25f);
            Assert.That(state.PreviewHit(attack, 1, 0f, 0.5f, false, -1f, weaker).DamageBonus, Is.EqualTo(1.20f).Within(1e-6f));
            Assert.That(state.PreviewHit(attack, 1, 0f, 0.5f, false, -1f, equal).DamageBonus, Is.EqualTo(-0.10f).Within(1e-6f));
            Assert.That(state.PreviewHit(attack, 1, 0f, 0.5f, false).DamageBonus, Is.EqualTo(0f), "대상을 모르면 비교하지 않는다");
        }

        // ── 대시·무적 ───────────────────────────────────────────────────

        [Test]
        public void 도망가는_맞으면_대시를_충전하고_대시하면_회복한다()
        {
            var state = With("escape_dash", ("dash_charge_on_damaged", 1f), ("internal_cooldown", 0.5f),
                ("heal_ratio_on_dash", 0.03f));
            Hit(state, 2f, 5f, 95f);
            Hit(state, 2.2f, 5f, 90f);
            Assert.That(state.ConsumePendingDashCharges(), Is.EqualTo(1), "0.5 초 안의 두 번째는 충전하지 않는다");
            Hit(state, 2.6f, 5f, 85f);
            Assert.That(state.ConsumePendingDashCharges(), Is.EqualTo(1));
            Hit(state, 3f, 5f, 80f, zone: true);
            Assert.That(state.ConsumePendingDashCharges(), Is.EqualTo(0), "자기장은 피격이 아니다");

            state.OnDash(4f, WeaponTag.Sword);
            Assert.That(state.ConsumePendingHealFraction(), Is.EqualTo(0.03f).Within(1e-6f));
        }

        [Test]
        public void 잔상은_공격하면_대시를_충전하고_공격_속도를_곱한다()
        {
            var state = With("afterimage", ("dash_charge_on_attack", 1f), ("attack_speed_mult", 0.72f));
            Swing(state, 2f);
            Assert.That(state.ConsumePendingDashCharges(), Is.EqualTo(1));
            Assert.That(state.AttackSpeedMultiplier, Is.EqualTo(0.72f).Within(1e-6f));
        }

        [Test]
        public void 한_대만은_맞으면_1초_무적이고_자기장에는_켜지지_않는다()
        {
            var state = With("one_hit", ("invuln_duration", 1f));
            Hit(state, 2f, 5f, 95f, zone: true);
            Assert.That(state.IsInvulnerable, Is.False);
            Hit(state, 3f, 5f, 90f);
            Assert.That(state.IsInvulnerable, Is.True);
            state.Tick(4.01f, 0f, true, false);
            Assert.That(state.IsInvulnerable, Is.False);
        }

        [Test]
        public void 멸망해도_5초는_죽을_피해를_체력_1로_버티고_5초_뒤에_죽는다()
        {
            var state = With("last_five", ("invuln_duration", 5f), ("death_on_expire", 1f));
            state.SetHealthSnapshot(30f, 100f);

            float through = state.BeforeDamage(2f, 500f, 30f, 100f, true, 1f, false, true);

            Assert.That(through, Is.EqualTo(29f), "체력 1 을 남긴다");
            Assert.That(state.IsInvulnerable, Is.True);
            Assert.That(state.LastFiveActive, Is.True);
            state.Tick(6.9f, 0f, true, false);
            Assert.That(state.ConsumeLastFiveDeath(), Is.False);
            state.Tick(7f, 0f, true, false);
            Assert.That(state.ConsumeLastFiveDeath(), Is.True);
            Assert.That(state.ConsumeLastFiveDeath(), Is.False, "한 번만");
            Assert.That(state.BeforeDamage(8f, 500f, 30f, 100f, true, 1f, false, true), Is.EqualTo(500f), "라운드에 한 번");
        }

        [Test]
        public void 멸망해도_5초로_버티는_동안은_상자를_열_수_없다()
        {
            // 엑셀 cancels_chest_open=true — 상자 쪽은 이 값을 개봉 자격 없음으로 읽는다(S-GD-Y534KH).
            var state = With("last_five", ("invuln_duration", 5f), ("cancels_chest_open", 1f));
            Assert.That(state.BlocksChestOpening, Is.False, "버티기 전에는 막지 않는다");
            state.BeforeDamage(2f, 500f, 30f, 100f, true, 1f, false, true);
            Assert.That(state.BlocksChestOpening, Is.True);
            Assert.That(state.LastFiveRemaining, Is.EqualTo(5f).Within(1e-5f));
            state.Tick(7f, 0f, true, false);
            state.ConsumeLastFiveDeath();
            Assert.That(state.BlocksChestOpening, Is.False, "쓰러지면 버티기가 끝난다");
        }

        [Test]
        public void 멸망해도_5초는_엑셀이_상자_취소를_끄면_상자를_막지_않는다()
        {
            var state = With("last_five", ("invuln_duration", 5f), ("cancels_chest_open", 0f));
            state.BeforeDamage(2f, 500f, 30f, 100f, true, 1f, false, true);
            Assert.That(state.LastFiveActive, Is.True);
            Assert.That(state.BlocksChestOpening, Is.False);
        }

        [Test]
        public void 효과_칸은_켜진_일시_효과의_남은_시간을_읽는다()
        {
            // S-UX-AYM6K3 — 표시 전용 읽기 창은 판정과 같은 조건을 읽기만 한다.
            var booster = With("booster", ("move_speed", 0.25f), ("duration", 2f));
            Assert.That(booster.BoosterRemaining, Is.EqualTo(0f));
            booster.OnDash(2f, WeaponTag.Sword);
            Assert.That(booster.BoosterRemaining, Is.EqualTo(2f).Within(1e-5f));
            Assert.That(booster.BoosterDuration, Is.EqualTo(2f));

            var ouch = With("ouch", ("move_speed", 0.30f), ("duration", 2f));
            Hit(ouch, 2f, 5f, 95f);
            Assert.That(ouch.OuchRemaining, Is.EqualTo(2f).Within(1e-5f));

            var oneHit = With("one_hit", ("invuln_duration", 1f));
            Hit(oneHit, 3f, 5f, 90f);
            Assert.That(oneHit.OneHitRemaining, Is.EqualTo(1f).Within(1e-5f));
            oneHit.Tick(3.5f, 0f, true, false);
            Assert.That(oneHit.OneHitRemaining, Is.EqualTo(0.5f).Within(1e-5f), "읽어도 시계를 건드리지 않는다");

            var light = With("light_step", ("move_speed", 0.25f));
            Assert.That(light.LightStepActive, Is.True);
            Hit(light, 2f, 5f, 95f);
            Assert.That(light.LightStepActive, Is.False, "맞으면 사라진다");
        }

        [Test]
        public void 멸망해도_5초는_자기장으로_죽어도_발동한다()
        {
            var state = With("last_five", ("invuln_duration", 5f));
            Assert.That(state.BeforeDamage(2f, 500f, 30f, 100f, false, float.PositiveInfinity, true, false),
                Is.EqualTo(29f));
        }

        // ── 체력 임계 ───────────────────────────────────────────────────

        [Test]
        public void 광전사는_체력이_절반_밑이면_폭주한다()
        {
            var state = With("berserk", ("trigger_hp_ratio", 0.5f), ("duration", 10f), ("internal_cooldown", 30f),
                ("move_speed", 0.30f), ("atk_flat", 5f), ("attack_speed", 0.20f), ("damage_bonus", 0.10f),
                ("lifesteal_ratio", 0.10f), ("damage_taken", 0.15f));
            Hit(state, 2f, 30f, 70f);
            Assert.That(state.MoveBonus, Is.EqualTo(0f), "아직 절반 위");

            Hit(state, 3f, 30f, 40f);
            Assert.That(state.MoveBonus, Is.EqualTo(0.30f).Within(1e-6f));
            Assert.That(state.AttackSpeedBonus, Is.EqualTo(0.20f).Within(1e-6f));
            Assert.That(Swing(state, 4f).DamageBonus, Is.EqualTo(0.10f).Within(1e-6f));
            var stats = state.TimedStats.ToDictionary(c => c.StatId, c => c.Value);
            Assert.That(stats[DamageStatKeys.AttackPower], Is.EqualTo(5f));
            Assert.That(stats[DamageStatKeys.Lifesteal], Is.EqualTo(0.10f).Within(1e-6f));
            Assert.That(stats[DamageStatKeys.DamageTakenMult], Is.EqualTo(0.15f).Within(1e-6f));

            state.Tick(13.01f, 0f, true, false);
            Assert.That(state.MoveBonus, Is.EqualTo(0f), "10 초 뒤 끝");
            Hit(state, 14f, 1f, 39f);
            Assert.That(state.MoveBonus, Is.EqualTo(0f), "재사용 대기(30 초) 중");
            Hit(state, 33.5f, 1f, 38f);
            Assert.That(state.MoveBonus, Is.EqualTo(0.30f).Within(1e-6f), "대기가 끝나면 다시");
        }

        [Test]
        public void 무승부는_체력이_30퍼센트_밑이면_회복하고_버틴다()
        {
            var state = With("draw", ("trigger_hp_ratio", 0.30f), ("instant_heal_ratio", 0.20f), ("duration", 5f),
                ("internal_cooldown", 60f), ("move_speed", 0.40f), ("damage_taken", -0.40f), ("atk_flat", -15f));
            Hit(state, 2f, 75f, 25f);
            Assert.That(state.ConsumePendingHealFraction(), Is.EqualTo(0.20f).Within(1e-6f));
            Assert.That(state.MoveBonus, Is.EqualTo(0.40f).Within(1e-6f));
            var stats = state.TimedStats.ToDictionary(c => c.StatId, c => c.Value);
            Assert.That(stats[DamageStatKeys.AttackPower], Is.EqualTo(-15f));
            Assert.That(stats[DamageStatKeys.DamageTakenMult], Is.EqualTo(-0.40f).Within(1e-6f));
            state.Tick(7.01f, 0f, true, false);
            Assert.That(state.TimedStats, Is.Empty);
        }

        // ── 활 ──────────────────────────────────────────────────────────

        [Test]
        public void 삼연발은_좌우로_두_발을_더_쏜다()
        {
            var state = With("triple_shot", ("extra_projectiles", 2f), ("spread_angle", 30f));
            var bow = state.BeginAttack(2f, WeaponTag.Bow);
            Assert.That(bow.ExtraProjectileCount, Is.EqualTo(2));
            Assert.That(bow.ExtraProjectileSpreadDegrees, Is.EqualTo(60f), "추가 화살은 가운데에서 좌우 30도");
            Assert.That(bow.ExtraProjectileDamageMultiplier, Is.EqualTo(1f));
            Assert.That(state.BeginAttack(3f, WeaponTag.Sword).ExtraProjectileCount, Is.EqualTo(0));
        }

        [Test]
        public void 헤드샷은_추가_화살과_검에는_붙지_않는다()
        {
            var state = With("headshot", ("instant_kill_chance", 1f));
            Assert.That(state.NotifyPrimaryHit(1, WeaponTag.Bow, false, 5).InstantKill, Is.True);
            Assert.That(state.NotifyPrimaryHit(1, WeaponTag.Bow, true, 5).InstantKill, Is.False);
            Assert.That(state.NotifyPrimaryHit(1, WeaponTag.Sword, false, 5).InstantKill, Is.False);

            var rare = With("headshot", ("instant_kill_chance", 0.01f));
            int kills = Enumerable.Range(0, 2000).Count(i => rare.NotifyPrimaryHit(1, WeaponTag.Bow, false, i).InstantKill);
            Assert.That(kills, Is.InRange(5, 45), "1% 근처");
        }

        // ── 경제·인원 ────────────────────────────────────────────────────

        [Test]
        public void 탐욕은_보유_골드의_일부를_공격력으로_바꾸고_기준_이하면_없다()
        {
            var state = With("greed", ("bonus_gold", 1500f), ("atk_from_gold_ratio", 0.005f), ("min_gold", 1000f));
            state.SetGold(3000);
            state.Tick(2f, 0f, true, false);
            var stats = state.TimedStats.ToDictionary(c => c.StatId, c => c.Value);
            Assert.That(stats[DamageStatKeys.AttackPower], Is.EqualTo(15f).Within(1e-4f));

            state.SetGold(1000);
            Assert.That(state.TimedStats, Is.Empty, "1000 이하면 발동하지 않는다");
        }

        [Test]
        public void 승부사는_나를_포함해_2명이_남으면_전부_회복하고_강해진다()
        {
            var state = With("last_stand", ("trigger_alive_count", 2f), ("min_match_size", 4f), ("heal_ratio", 1f),
                ("stat_bonus", 0.20f), ("pre_damage_taken", 0.10f));
            state.SetHealthSnapshot(40f, 100f);
            Assert.That(state.TimedStats.Single().Value, Is.EqualTo(0.10f).Within(1e-6f), "그전에는 받는 피해 +10%");

            // 최소 매치 인원(4)을 채운 매치여야 한다 — 참가 인원은 이 라운드에서 본 최대 생존 수다 (S-GD-85DD2Y).
            state.SetAliveCount(4);
            Assert.That(state.LastStandActive, Is.False);
            state.SetAliveCount(3);
            Assert.That(state.LastStandActive, Is.False);
            state.SetAliveCount(2);
            Assert.That(state.LastStandActive, Is.True);
            Assert.That(state.ConsumePendingHealFraction(), Is.EqualTo(1f), "체력을 전부 회복");
            Assert.That(state.MoveBonus, Is.EqualTo(0.20f).Within(1e-6f));
            Assert.That(state.AttackSpeedBonus, Is.EqualTo(0.20f).Within(1e-6f));
            Assert.That(Swing(state, 3f).DamageBonus, Is.EqualTo(0.20f).Within(1e-6f));
            Assert.That(state.TimedStats, Is.Empty, "받는 피해 증가가 풀린다");

            state.SetAliveCount(2);
            Assert.That(state.ConsumePendingHealFraction(), Is.EqualTo(0f), "한 번만");
        }

        /// <summary>
        /// 최소 매치 인원을 못 채운 소규모 전투에서는 발동하지 않는다 (S-GD-85DD2Y — 최소 매치 인원도 엑셀 값이다).
        /// </summary>
        /// <remarks>
        /// 이 검사가 없으면 2인 전투가 시작하자마자 "최후 2인" 이 되어, 발동 전 대가(받는 피해 +10%)는 한 번도
        /// 걸리지 않고 보상만 남는다.
        /// </remarks>
        [Test]
        public void 승부사는_최소_매치_인원을_못_채운_전투에서는_발동하지_않는다()
        {
            var state = With("last_stand", ("trigger_alive_count", 2f), ("min_match_size", 4f), ("heal_ratio", 1f),
                ("stat_bonus", 0.20f), ("pre_damage_taken", 0.10f));
            state.SetHealthSnapshot(100f, 100f);

            state.SetAliveCount(2);
            state.SetAliveCount(2);

            Assert.That(state.LastStandActive, Is.False, "참가 인원이 2명뿐인 전투에서는 발동하지 않는다");
            Assert.That(state.ConsumePendingHealFraction(), Is.EqualTo(0f));
            Assert.That(state.TimedStats.Single().Value, Is.EqualTo(0.10f).Within(1e-6f),
                "발동하지 않았으니 그 전의 받는 피해 증가는 그대로 걸려 있다");
            Assert.That(state.MoveBonus, Is.EqualTo(0f).Within(1e-6f));
        }

        /// <summary>인원이 기준을 넘었다가 줄어든 매치는 발동한다 — 기준은 <b>참가 인원</b>이지 지금 생존 수가 아니다.</summary>
        [Test]
        public void 승부사는_참가_인원이_기준을_넘었으면_생존자가_줄어든_뒤에_발동한다()
        {
            var state = With("last_stand", ("trigger_alive_count", 2f), ("min_match_size", 4f), ("heal_ratio", 1f),
                ("stat_bonus", 0.20f), ("pre_damage_taken", 0.10f));
            state.SetHealthSnapshot(100f, 100f);

            state.SetAliveCount(8);
            state.SetAliveCount(2);

            Assert.That(state.LastStandActive, Is.True);
        }

        /// <summary>라운드가 바뀌면 참가 인원도 다시 센다 — 지난 라운드의 인원으로 조건을 통과하면 안 된다.</summary>
        [Test]
        public void 승부사의_참가_인원은_라운드마다_다시_센다()
        {
            var state = With("last_stand", ("trigger_alive_count", 2f), ("min_match_size", 4f), ("heal_ratio", 1f),
                ("stat_bonus", 0.20f), ("pre_damage_taken", 0.10f));
            state.SetHealthSnapshot(100f, 100f);
            state.SetAliveCount(8);

            state.ResetRound(10f);
            state.SetHealthSnapshot(100f, 100f);
            state.SetAliveCount(2);

            Assert.That(state.LastStandActive, Is.False);
        }

        [Test]
        public void 승부사는_내가_쓰러져_있으면_발동하지_않는다()
        {
            var state = With("last_stand", ("trigger_alive_count", 2f), ("heal_ratio", 1f), ("stat_bonus", 0.20f),
                ("pre_damage_taken", 0.10f));
            state.BeforeDamage(2f, 500f, 30f, 100f, true, 1f, false, true);
            state.AfterDamage(2f, 30f, 0f, 100f, true);

            state.SetAliveCount(2);

            Assert.That(state.LastStandActive, Is.False);
        }

        [Test]
        public void 소매치기는_확률로_빼앗고_재사용_대기_동안은_다시_굴리지_않는다()
        {
            var state = With("pickpocket", ("steal_chance", 1f), ("internal_cooldown", 0.5f), ("steal_count", 1f));
            Assert.That(state.NotifyPrimaryHit(1, WeaponTag.Sword, false, 3).StealCount, Is.EqualTo(1));
            Assert.That(state.NotifyPrimaryHit(1, WeaponTag.Sword, false, 4).StealCount, Is.EqualTo(0), "0.5 초 안");
            state.Tick(1.6f, 0f, true, false);
            Assert.That(state.NotifyPrimaryHit(1, WeaponTag.Bow, false, 5).StealCount, Is.EqualTo(1));
        }

        [Test]
        public void 사냥꾼은_표식이_붙은_적을_처치하면_골드를_받는다()
        {
            var state = With("hunter_mark", ("chase_angle", 60f), ("move_speed", 0.10f), ("damage_bonus", 0.05f),
                ("kill_reward_gold", 100f));
            state.NotifyPrimaryHit(7, WeaponTag.Sword, false, 1);
            state.NotifyKill(2f, 8);
            Assert.That(state.ConsumeEarnedGold(), Is.EqualTo(0), "표식 없는 적");
            state.NotifyKill(2f, 7);
            Assert.That(state.ConsumeEarnedGold(), Is.EqualTo(100));
            Assert.That(state.HunterTarget, Is.EqualTo(BlessingCombatState.NoTarget), "표식은 처치로 사라진다");
        }

        [Test]
        public void 이자_폭발_t1_은_예치하면_전투_중_주기적으로_이자를_받는다()
        {
            var state = With("interest_income", ("deposit", 300f), ("start_seconds", 20f), ("interval", 5f),
                ("interest", 100f), ("kill_bonus", 300f), ("first_place_bonus", 500f));
            state.Tick(10f, 10f, true, false);
            Assert.That(state.ConsumeEarnedGold(), Is.EqualTo(0), "예치하지 않았다");

            Assert.That(state.InterestIncomeActive, Is.False);
            state.SetInterestDeposited(true);
            state.Tick(19f, 19f, true, false);
            for (int i = 0; i < 3; i++) Assert.That(state.InterestIncomeActive, Is.True,
                "첫 지급 전에도 계약이 활성이고, 조회는 지급을 소비하지 않는다.");
            Assert.That(state.ConsumeEarnedGold(), Is.EqualTo(0), "20 초 전");
            state.Tick(20f, 20f, true, false);
            state.Tick(30f, 30f, true, false);
            Assert.That(state.ConsumeEarnedGold(), Is.EqualTo(300), "20·25·30 초");

            state.NotifyKill(31f, 9);
            Assert.That(state.ConsumeEarnedGold(), Is.EqualTo(300), "처치 보너스");
            state.SetAliveCount(1);
            Assert.That(state.ConsumeEarnedGold(), Is.EqualTo(500), "혼자 남으면 1등 보너스");
            state.SetAliveCount(1);
            Assert.That(state.ConsumeEarnedGold(), Is.EqualTo(0), "1등 보너스는 라운드에 한 번");

            state.AfterDamage(32f, 100f, 0f, 100f, true);
            state.Tick(60f, 60f, true, false);
            Assert.That(state.ConsumeEarnedGold(), Is.EqualTo(0), "쓰러지면 멈춘다");
            Assert.That(state.InterestIncomeActive, Is.False, "쓰러지면 효과 HUD에서도 내려간다.");
            state.ResetRound(70f);
            Assert.That(state.InterestIncomeActive, Is.False, "전투 종료 중에는 표시하지 않는다.");
            state.SetInterestDeposited(false);
            state.Tick(70f, 0f, true, false);
            Assert.That(state.InterestIncomeActive, Is.False, "이전 예치가 다음 라운드에 남지 않는다.");
        }

        [Test]
        public void 쇼_미_더_머니와_탐욕은_고를_때_한_번_골드를_받고_이월_한도를_바꾼다()
        {
            var show = new BlessingDefinition("show-me-money-t1", "쇼 미 더 머니", WeaponTag.Common, BlessingTier.Silver);
            var greedy = new BlessingDefinition("greed-t2", "탐욕", WeaponTag.Common, BlessingTier.Gold);
            GameContentProvider.Set(new GameContent(2, null, new[] { show, greedy }, null, null, null,
                new Dictionary<string, BlessingMvpDefinition>(StringComparer.Ordinal)
                {
                    ["show-me-money-t1"] = new BlessingMvpDefinition(show, "instant_gold", -1, BlessingStackRule.Unique,
                        BlessingDurationKind.Round, new[] { "bonus_gold", "carryover_cap_bonus" }, new[] { 800f, 800f }),
                    ["greed-t2"] = new BlessingMvpDefinition(greedy, "greed", -1, BlessingStackRule.Unique,
                        BlessingDurationKind.Permanent, new[] { "bonus_gold", "atk_from_gold_ratio", "min_gold" },
                        new[] { 1500f, 0.005f, 1000f }),
                }));
            var wallet = new BettingRoyale.Data.Economy.Wallet(0);
            var economy = new BlessingEconomyState(new[] { show });

            Assert.That(economy.GrantAcquisitionBonuses(wallet), Is.EqualTo(800));
            Assert.That(economy.GrantAcquisitionBonuses(wallet), Is.EqualTo(0), "같은 축복으로 두 번 받지 않는다");
            Assert.That(economy.CarryOverLimitFor(BettingRoyale.Data.Economy.CarryOverLimit.Default).Gold,
                Is.EqualTo(BettingRoyale.Data.Economy.CarryOverLimit.DefaultGold + 800));

            economy.UpdateLoadout(new[] { show, greedy });
            Assert.That(economy.GrantAcquisitionBonuses(wallet), Is.EqualTo(1500), "새로 고른 탐욕만");
            Assert.That(economy.CarryOverLimitFor(BettingRoyale.Data.Economy.CarryOverLimit.Default).IsUnlimited, Is.True,
                "탐욕은 이월 한도를 없앤다");
            Assert.That(wallet.Gold, Is.EqualTo(2300));
        }

        [Test]
        public void 룰렛_환급은_잃은_금액의_일부를_돌려준다()
        {
            var refund = new BlessingDefinition("roulette-refund-t1", "룰렛 환급", WeaponTag.Common, BlessingTier.Silver);
            GameContentProvider.Set(new GameContent(2, null, new[] { refund }, null, null, null,
                new Dictionary<string, BlessingMvpDefinition>(StringComparer.Ordinal)
                {
                    ["roulette-refund-t1"] = new BlessingMvpDefinition(refund, "roulette_refund", -1,
                        BlessingStackRule.Unique, BlessingDurationKind.Permanent, new[] { "refund_fraction" }, new[] { 0.30f }),
                }));

            Assert.That(new BlessingEconomyState(new[] { refund }).RouletteRefundFraction, Is.EqualTo(0.30f).Within(1e-6f));
            Assert.That(new BlessingEconomyState().RouletteRefundFraction, Is.EqualTo(0f));
        }

        [Test]
        public void 이자_폭발_t1_도_예치를_받고_환급_비율이_없으면_환급하지_않는다()
        {
            var t1 = new BlessingDefinition("interest-burst-t1", "이자 폭발", WeaponTag.Common, BlessingTier.Silver);
            GameContentProvider.Set(new GameContent(2, null, new[] { t1 }, null, null, null,
                new Dictionary<string, BlessingMvpDefinition>(StringComparer.Ordinal)
                {
                    ["interest-burst-t1"] = new BlessingMvpDefinition(t1, "interest_income", -1, BlessingStackRule.Unique,
                        BlessingDurationKind.Round, new[] { "deposit", "interest" }, new[] { 300f, 100f }),
                }));
            var economy = new BlessingEconomyState();
            economy.BeginPreparation(1, new[] { t1 });
            var wallet = new BettingRoyale.Data.Economy.Wallet(1000);

            Assert.That(economy.HasInterest, Is.True);
            Assert.That(economy.TryDeposit(wallet), Is.True);
            Assert.That(wallet.Gold, Is.EqualTo(700));
            economy.EndPreparation();
            Assert.That(economy.SettleRound(1, true, wallet), Is.EqualTo(0), "t1 은 전투 중 이자로 받는다");
        }

        [Test]
        public void 독화살은_적중_고정_피해와_도트를_돌려준다()
        {
            var state = With("poison_arrow", ("on_hit_fixed_damage", 3f), ("dot_damage_per_second", 2f),
                ("dot_duration", 5f), ("dot_max_stacks", 3f));
            var outcome = state.NotifyPrimaryHit(1, WeaponTag.Bow, true, 5);
            Assert.That(outcome.PoisonHitDamage, Is.EqualTo(3f));
            Assert.That(outcome.PoisonDps, Is.EqualTo(2f));
            Assert.That(outcome.PoisonSeconds, Is.EqualTo(5f));
            Assert.That(outcome.PoisonMaxStacks, Is.EqualTo(3));
            Assert.That(state.NotifyPrimaryHit(1, WeaponTag.Sword, false, 5).PoisonDps, Is.EqualTo(0f));
        }

        // ── 능력치만 돌던 엑셀 전용 축복 8종의 특수 효과 (S-GD-9MWJRR) ─────────────

        /// <summary>엑셀 전용 축복 여러 개와(같은 효과의 여러 등급 포함) 코드 축복을 함께 든 상태.</summary>
        private static BlessingCombatState WithAll(string[] codeIds,
            params (string Id, string Effect, (string Key, float Value)[] Numbers)[] rows)
        {
            var definitions = new List<BlessingDefinition>();
            var behaviors = new Dictionary<string, BlessingMvpDefinition>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var definition = new BlessingDefinition(row.Id, row.Id, WeaponTag.Common, BlessingTier.Silver);
                definitions.Add(definition);
                behaviors[row.Id] = new BlessingMvpDefinition(definition, row.Effect, BlessingMvpDefinition.MissingIconIndex,
                    BlessingStackRule.Unique, BlessingDurationKind.Permanent,
                    row.Numbers.Select(n => n.Key).ToArray(), row.Numbers.Select(n => n.Value).ToArray());
            }

            GameContentProvider.Set(new GameContent(2, null, definitions, null, null, null, behaviors));
            foreach (string id in codeIds ?? Array.Empty<string>())
            {
                Assert.That(BlessingMvpCatalog.TryGet(id, out var code), Is.True, id);
                definitions.Add(code.Definition);
            }

            var state = new BlessingCombatState(definitions);
            state.Tick(1f, 0f, combatActive: true, moving: false);
            return state;
        }

        private static float TimedStat(BlessingCombatState state, string statId, StatStackMode stack = StatStackMode.Add) =>
            state.TimedStats.Where(c => c.StatId == statId && c.Stack == stack).Sum(c => c.Value);

        [Test]
        public void 쇠뇌와_발리스타는_공격_속도를_깎는_패널티이고_패널티끼리는_곱한다()
        {
            Assert.That(With("crossbow", ("attack_speed_mult", 0.7f)).AttackSpeedMultiplier, Is.EqualTo(0.7f).Within(1e-6f));
            var both = WithAll(null,
                ("crossbow-t1", "crossbow", new[] { ("attack_speed_mult", 0.7f) }),
                ("ballista-t2", "ballista", new[] { ("attack_speed_mult", 0.7f) }));
            Assert.That(both.AttackSpeedMultiplier, Is.EqualTo(0.49f).Within(1e-5f), "공격 속도 감소 패널티는 곱한다(S-GD-SH74SC)");
        }

        [Test]
        public void 저격수는_화살이_기준보다_멀리_날아가_맞히면_등급마다_피해가_더해진다()
        {
            var state = WithAll(null,
                ("sniper-t1", "sniper", new[] { ("min_distance", 15f), ("damage_bonus", 0.30f) }),
                ("sniper-t3", "sniper", new[] { ("min_distance", 15f), ("damage_bonus", 1.00f) }));
            var shot = state.BeginAttack(2f, WeaponTag.Bow);
            var unknown = BlessingCombatState.TargetContext.Unknown;

            Assert.That(state.PreviewHit(shot, 1, 16f, 0.5f, false, -1f, unknown).DamageBonus,
                Is.EqualTo(1.30f).Within(1e-5f), "두 등급의 피해 증가를 더한다");
            Assert.That(state.PreviewHit(shot, 1, 15f, 0.5f, false, -1f, unknown).DamageBonus,
                Is.EqualTo(1.30f).Within(1e-5f), "15m 이상이면 붙는다 — 정확히 15m 도 든다(S-GD-9MWJRR)");
            Assert.That(state.PreviewHit(shot, 1, 14.9f, 0.5f, false, -1f, unknown).DamageBonus,
                Is.EqualTo(0f), "15m 미만은 없다");

            var swing = state.BeginAttack(3f, WeaponTag.Sword);
            Assert.That(state.PreviewHit(swing, 1, 30f, 0.5f, false, -1f, unknown).DamageBonus,
                Is.EqualTo(0f), "근접 공격에는 붙지 않는다");
        }

        [Test]
        public void 단단해지기는_맞을_때마다_방어력이_쌓이고_쌓이는_몫은_상한까지다()
        {
            var state = With("sturdy", ("defense_per_hit", 1f), ("defense_cap", 10f));

            Hit(state, 2f, 5f, 95f, zone: true);
            Assert.That(TimedStat(state, DamageStatKeys.Defense), Is.EqualTo(0f), "자기장은 세지 않는다");

            for (int i = 0; i < 3; i++) Hit(state, 3f + i, 1f, 94f - i);
            Assert.That(TimedStat(state, DamageStatKeys.Defense), Is.EqualTo(3f));
            Assert.That(state.SturdyStacks, Is.EqualTo(3));

            for (int i = 0; i < 12; i++) Hit(state, 10f + i, 1f, 90f - i);
            Assert.That(TimedStat(state, DamageStatKeys.Defense), Is.EqualTo(10f), "쌓이는 몫은 최대 10");

            state.ResetRound(30f);
            Assert.That(TimedStat(state, DamageStatKeys.Defense), Is.EqualTo(0f), "라운드마다 초기화");
        }

        [Test]
        public void 기절은_경직처럼_막고_경직만으로는_기절이_아니며_시간이_지나면_풀린다()
        {
            var staggered = With("sturdy", ("defense_per_hit", 1f), ("defense_cap", 10f));
            staggered.ApplyStagger(2f, 1f);
            Assert.That(staggered.IsStunned, Is.False, "경직은 소모품을 막지 않는다");

            var stunned = With("sturdy", ("defense_per_hit", 1f), ("defense_cap", 10f));
            stunned.ApplyStun(2f, 0.5f);
            Assert.That(stunned.IsStunned, Is.True);
            Assert.That(stunned.AttackBlockedUntil, Is.EqualTo(2.5f).Within(1e-5f), "기절도 공격을 막는다");
            Assert.That(stunned.MovePenaltyMultiplier, Is.EqualTo(0f), "기절도 움직이지 못한다");
            stunned.Tick(2.6f, 1.6f, true, false);
            Assert.That(stunned.IsStunned, Is.False);
            Assert.That(stunned.MovePenaltyMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void 몸통_박치기는_발동하면_재사용_대기_동안_다시_걸지_않는다()
        {
            var state = With("body_check", ("stun", 0.5f), ("internal_cooldown", 2f));
            Assert.That(state.BodyCheckReady, Is.True);
            Assert.That(state.BodyCheckStunSeconds, Is.EqualTo(0.5f));

            state.MarkBodyCheck(2f);
            Assert.That(state.BodyCheckReady, Is.False);
            state.Tick(3.9f, 2.9f, true, false);
            Assert.That(state.BodyCheckReady, Is.False);
            state.Tick(4.01f, 3.01f, true, false);
            Assert.That(state.BodyCheckReady, Is.True);
        }

        [Test]
        public void 아홉_목숨은_최대_체력_9_피해_1_고정_회복_불가_맞으면_잠깐_무적이다()
        {
            var state = With("nine_lives", ("max_hp_override", 9f), ("damage_taken_fixed", 1f), ("shield_cap", 1f),
                ("invuln_duration", 1f), ("heal_blocked", 1f));

            Assert.That(state.MaxHealthOverride, Is.EqualTo(9f));
            Assert.That(state.HealingBlockedUntil, Is.EqualTo(float.PositiveInfinity), "회복할 수 없다");
            Assert.That(TimedStat(state, DamageStatKeys.DamageTakenFixed, StatStackMode.Min), Is.EqualTo(1f),
                "한 대 피해를 1 로 고정한다");

            Hit(state, 2f, 1f, 8f);
            Assert.That(state.IsInvulnerable, Is.True, "맞으면 1초 무적");
            state.Tick(3.01f, 2.01f, true, false);
            Assert.That(state.IsInvulnerable, Is.False);
        }

        [Test]
        public void 아홉_목숨은_보호막_합계를_상한까지만_받는다()
        {
            // 안전 자산(코드 축복)은 공격하면 보호막을 얻는다. 9개의 목숨이 있으면 상한 1 까지만 생긴다.
            var state = WithAll(new[] { "safe-asset-t1" },
                ("nine-lives-t3", "nine_lives", new[] { ("shield_cap", 1f) }));
            state.Tick(10f, 9f, true, false);
            state.BeginAttack(10f, WeaponTag.Sword);
            Assert.That(state.Shield, Is.EqualTo(1f).Within(1e-5f));
        }
        // ── 동작을 고르는 파라미터 (예전에는 아무 코드도 읽지 않았다) ─────────────

        /// <summary>
        /// <c>compare_mode</c> 가 실제 비교를 고른다. 예전에는 효과마다 비교가 박혀 있어
        /// 엑셀에서 방식을 바꿔도 게임이 그대로였다.
        /// </summary>
        [Test]
        public void 비교_방식은_엑셀이_고른다()
        {
            var byHealth = WithText("underdog", new[] { ("compare_mode", "HP_RATIO") },
                ("damage_bonus", 1.2f), ("damage_taken", -0.10f));
            byHealth.SetHealthSnapshot(50f, 100f);
            var attack = byHealth.BeginAttack(2f, WeaponTag.Sword);
            // 체력 비율이 나보다 높은 대상 → 보너스. 공격력은 견주지 않는다.
            Assert.That(byHealth.PreviewHit(attack, 1, 0f, 1f, false, -1f,
                new BlessingCombatState.TargetContext(0.9f, 999f, 1f)).DamageBonus,
                Is.EqualTo(1.2f).Within(1e-4f));

            var byPower = WithText("underdog", new[] { ("compare_mode", "BASE_ATTACK_POWER") },
                ("damage_bonus", 1.2f), ("damage_taken", -0.10f));
            byPower.SetHealthSnapshot(50f, 100f);
            var swing = byPower.BeginAttack(2f, WeaponTag.Sword);
            // 같은 대상이라도 이제 공격력으로 견준다 — 대상이 더 세므로 보너스가 없다.
            Assert.That(byPower.PreviewHit(swing, 1, 0f, 1f, false, -1f,
                new BlessingCombatState.TargetContext(0.9f, 999f, 1f)).DamageBonus,
                Is.EqualTo(0f).Within(1e-4f));
            Assert.That(byPower.UnsupportedDeclarations, Is.Empty);
        }

        [Test]
        public void 모르는_비교_방식은_조용히_기본값으로_흐르지_않고_기록된다()
        {
            var state = WithText("underdog", new[] { ("compare_mode", "MOON_PHASE") },
                ("damage_bonus", 1.2f), ("damage_taken", -0.10f));

            Assert.That(state.UnsupportedDeclarations, Is.Not.Empty);
            Assert.That(state.UnsupportedDeclarations[0], Does.Contain("MOON_PHASE"));
        }

        /// <summary>
        /// <c>invuln_kind</c> — <c>ATTACKABLE</c> 은 무적 중에도 공격할 수 있다는 뜻이고, 그것이 유일하게 구현된 종류다.
        /// </summary>
        /// <remarks>
        /// 모르는 종류를 선언해도 <b>동작은 구현된 쪽을 유지</b>한다 — 데이터 오타 하나로 축복이 꺼지는 편이 더 나쁘다.
        /// 대신 미지원으로 기록해 전체 축복 QA 가 실패로 잡는다.
        /// </remarks>
        [Test]
        public void 무적의_종류는_읽고_검사하며_모르는_값은_기록만_남긴다()
        {
            var attackable = WithText("one_hit", new[] { ("invuln_kind", "ATTACKABLE") },
                ("invuln_duration", 1f));
            Hit(attackable, 2f, 10f, 90f);
            Assert.That(attackable.IsInvulnerable, Is.True);
            Assert.That(attackable.InvulnerabilityAllowsAttacking, Is.True);
            Assert.That(attackable.AttackBlockedUntil, Is.LessThanOrEqualTo(2f), "공격을 막지 않는다");
            Assert.That(attackable.UnsupportedDeclarations, Is.Empty);

            var unknown = WithText("one_hit", new[] { ("invuln_kind", "UNTOUCHABLE") }, ("invuln_duration", 1f));
            Hit(unknown, 2f, 10f, 90f);
            Assert.That(unknown.IsInvulnerable, Is.True);
            Assert.That(unknown.AttackBlockedUntil, Is.LessThanOrEqualTo(2f),
                "모르는 종류라고 공격을 막아 버리지 않는다");
            Assert.That(unknown.UnsupportedDeclarations, Is.Not.Empty);
            Assert.That(unknown.UnsupportedDeclarations[0], Does.Contain("UNTOUCHABLE"));
        }

        /// <summary><c>mark_target</c> — 아는 기준일 때만 표식을 고른다.</summary>
        [Test]
        public void 표식_대상_기준은_엑셀이_고른다()
        {
            var top = WithText("rage_dash", new[] { ("mark_target", "TOP_DAMAGE_DEALER") },
                ("move_speed", 0.25f), ("damage_bonus", 0.15f));
            Assert.That(top.TracksTopDamageDealer, Is.True);
            Assert.That(top.UnsupportedDeclarations, Is.Empty);

            var unknown = WithText("rage_dash", new[] { ("mark_target", "NEAREST") },
                ("move_speed", 0.25f), ("damage_bonus", 0.15f));
            Assert.That(unknown.TracksTopDamageDealer, Is.True, "모르는 기준이라고 표식을 꺼 버리지 않는다");
            Assert.That(unknown.UnsupportedDeclarations, Is.Not.Empty);
            Assert.That(unknown.UnsupportedDeclarations[0], Does.Contain("NEAREST"));
        }

        /// <summary><c>death_on_expire</c> — 시간이 끝나면 쓰러지는지를 엑셀이 정한다.</summary>
        [Test]
        public void 지구가_멸망해도_5초의_만료_사망은_엑셀이_정한다()
        {
            var dies = With("last_five", ("invuln_duration", 5f), ("death_on_expire", 1f));
            dies.SetHealthSnapshot(30f, 100f);
            dies.BeforeDamage(2f, 500f, 30f, 100f, true, 1f);
            dies.Tick(8f, 6f, true, false);
            Assert.That(dies.ConsumeLastFiveDeath(), Is.True);

            var survives = With("last_five", ("invuln_duration", 5f), ("death_on_expire", 0f));
            survives.SetHealthSnapshot(30f, 100f);
            survives.BeforeDamage(2f, 500f, 30f, 100f, true, 1f);
            survives.Tick(8f, 6f, true, false);
            Assert.That(survives.ConsumeLastFiveDeath(), Is.False, "무적만 끝나고 쓰러지지 않는다");
        }

        /// <summary><c>triggers_on_instant_kill</c> — 즉사를 가로채는지.</summary>
        [Test]
        public void 지구가_멸망해도_5초가_즉사를_가로채는지는_엑셀이_정한다()
        {
            var ignores = With("last_five", ("invuln_duration", 5f), ("triggers_on_instant_kill", 0f));
            Assert.That(ignores.LastFiveInterceptsInstantKill, Is.False);

            var intercepts = With("last_five", ("invuln_duration", 5f), ("triggers_on_instant_kill", 1f));
            Assert.That(intercepts.LastFiveInterceptsInstantKill, Is.True);
        }

        /// <summary><c>no_stack_on_invuln</c> — 무적 대상에 독을 쌓을지.</summary>
        [Test]
        public void 독화살이_무적_대상을_건너뛰는지는_엑셀이_정한다()
        {
            var skips = With("poison_arrow", ("on_hit_fixed_damage", 3f), ("dot_damage_per_second", 2f),
                ("dot_duration", 5f), ("dot_max_stacks", 3f), ("no_stack_on_invuln", 1f));
            Assert.That(skips.NotifyPrimaryHit(1, WeaponTag.Bow, false, 1).PoisonSkipsInvulnerable, Is.True);

            var stacks = With("poison_arrow", ("on_hit_fixed_damage", 3f), ("dot_damage_per_second", 2f),
                ("dot_duration", 5f), ("dot_max_stacks", 3f), ("no_stack_on_invuln", 0f));
            Assert.That(stacks.NotifyPrimaryHit(1, WeaponTag.Bow, false, 1).PoisonSkipsInvulnerable, Is.False);
        }

        /// <summary><c>applies_to_extra_projectiles</c> — 추가 화살에도 헤드샷이 붙는지.</summary>
        [Test]
        public void 헤드샷이_추가_화살에_붙는지는_엑셀이_정한다()
        {
            var primaryOnly = With("headshot", ("instant_kill_chance", 1f),
                ("applies_to_extra_projectiles", 0f), ("ignores_defense", 1f));
            Assert.That(primaryOnly.NotifyPrimaryHit(1, WeaponTag.Bow, false, 5).InstantKill, Is.True);
            Assert.That(primaryOnly.NotifyPrimaryHit(1, WeaponTag.Bow, true, 5).InstantKill, Is.False);

            var everyArrow = With("headshot", ("instant_kill_chance", 1f),
                ("applies_to_extra_projectiles", 1f), ("ignores_defense", 1f));
            Assert.That(everyArrow.NotifyPrimaryHit(1, WeaponTag.Bow, true, 5).InstantKill, Is.True);
        }

        /// <summary><c>ignores_defense</c> — 구현한 값은 참뿐이다. 거짓이면 조용히 넘기지 않고 기록한다.</summary>
        [Test]
        public void 방어력을_따지는_즉사는_구현하지_않았고_그_사실을_기록한다()
        {
            var declared = With("headshot", ("instant_kill_chance", 0.01f),
                ("applies_to_extra_projectiles", 0f), ("ignores_defense", 1f));
            Assert.That(declared.UnsupportedDeclarations, Is.Empty);

            var unsupported = With("headshot", ("instant_kill_chance", 0.01f),
                ("applies_to_extra_projectiles", 0f), ("ignores_defense", 0f));
            Assert.That(unsupported.UnsupportedDeclarations, Is.Not.Empty);
            Assert.That(unsupported.UnsupportedDeclarations[0], Does.Contain("ignores_defense"));
        }

        /// <summary><c>active_slot</c> — 선언한 슬롯에서만 켜진다.</summary>
        [Test]
        public void 액티브_축복은_선언한_슬롯에서만_켜진다()
        {
            var state = With("double-down", ("hp_cost", 0.1f), ("damage_bonus", 0.4f), ("move_bonus", 0.15f),
                ("duration", 6f), ("cooldown", 20f), ("active_slot", 2f));

            Assert.That(state.ActiveBlessingReadyIn(BlessingCombatState.DefaultActiveSlot), Is.False,
                "기본 슬롯에는 없다");
            Assert.That(state.ActiveBlessingReadyIn(2), Is.True);
            Assert.That(state.ActivateReadyActive(2f, BlessingCombatState.DefaultActiveSlot), Is.False);
            Assert.That(state.ActivateReadyActive(2f, 2), Is.True);
        }

    }
}

using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests.Blessing
{
    /// <summary>
    /// <see cref="BlessingCombatState"/> 의 동작 시험.
    ///
    /// <para>
    /// <b>기대값에 숫자를 적지 않는다.</b> 임계·지속·배수는 모두 카탈로그에서 읽어 계산한다 —
    /// CSV 가 바뀌었을 때 터져야 하는 것은 이 시험이 아니라 밸런스 검토다.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class BlessingCombatStateTests
    {
        private const float Tolerance = 1e-4f;

        [Test]
        public void RainCheck_ConsumesCooldownOnlyAtActualImpact()
        {
            var state = State("rain-check");
            state.Tick(0, 0, true, false);
            var miss = state.BeginAttack(0, WeaponTag.Bow);
            state.CompleteAttack(miss, 0, false);
            Near(0, state.CooldownRemaining("rain-check"));
            var first = state.BeginAttack(0, WeaponTag.Bow);
            var concurrent = state.BeginAttack(0, WeaponTag.Bow);
            state.Tick(.5f, .5f, true, false);
            Assert.That(state.TryConsumeRainCheck(first, .5f), Is.True);
            Assert.That(state.TryConsumeRainCheck(concurrent, .5f), Is.False);
            Near(Number("rain-check", "cooldown"), state.CooldownRemaining("rain-check"));
        }

        [Test]
        public void Has_And_Number_ReadFromCatalog()
        {
            var state = State("overheat");

            Assert.That(state.Has("overheat"), Is.True, "들고 있는 효과는 참이다");
            Assert.That(state.Has("counter"), Is.False, "없는 효과는 거짓이다");
            Assert.That(state.Has(null), Is.False, "null 은 거짓이다");
            Near(Number("overheat", "max_stacks"), state.Number("overheat", "max_stacks"), "수치는 표에서 온다");

            Assert.Throws<KeyNotFoundException>(
                () => state.Number("counter", "cooldown"), "없는 효과의 수치는 던진다");
            Assert.Throws<KeyNotFoundException>(
                () => state.Number("overheat", "없는키"), "없는 수치는 던진다");
        }

        [Test]
        public void EmptyLoadout_HasNoEffects()
        {
            var state = State();

            Near(0f, state.MoveBonus, "가진 축복이 없으면 이동 보너스도 없다");
            Near(1f, state.MovePenaltyMultiplier, "감소도 없다");
            Near(0f, state.Shield, "보호막도 없다");

            var attack = state.BeginAttack(0f, WeaponTag.Sword);

            Near(0f, attack.DamageBonus, "기본 수치는 축복 밖에 있다");
            Near(1f, attack.DamagePenaltyMultiplier);
            Near(1f, attack.RangeMultiplier);
            Assert.That(attack.HasHalfAngleOverride, Is.False, "각도는 무기 기본값을 쓴다");
        }

        // ── 마감 직전 ────────────────────────────────────────────────────────

        [Test]
        public void Deadline_TriggersOnCrossingWhenCombatIsRecent()
        {
            var state = State("deadline");
            float start = Number("deadline", "start_seconds");
            float window = Number("deadline", "recent_hit_window");
            float duration = Number("deadline", "duration");

            state.Tick(0f, 0f, true, false);
            state.AfterDamage(start - window * 0.5f, 5f, 95f, 100f, true);
            state.Tick(start + 0.1f, start + 0.1f, true, false);

            Assert.That(state.DeadlineActive, Is.True, "교전 기록이 최근이면 발동한다");
            Near(Number("deadline", "move_bonus"), state.MoveBonus, "이동 보너스가 붙는다");
            Near(
                Number("deadline", "damage_bonus"),
                state.BeginAttack(start + 0.1f, WeaponTag.Sword).DamageBonus,
                "피해 보너스가 붙는다");

            float end = start + 0.1f + duration;
            state.Tick(end + 0.1f, end + 0.1f, true, false);

            Assert.That(state.DeadlineActive, Is.False, "지속이 끝나면 꺼진다");
            Near(0f, state.MoveBonus, "이동 보너스도 같이 사라진다");
        }

        [Test]
        public void Deadline_DoesNotCatchUpWhenCrossingHadNoCombat()
        {
            var state = State("deadline");
            float start = Number("deadline", "start_seconds");

            state.Tick(0f, 0f, true, false);
            state.Tick(start + 0.1f, start + 0.1f, true, false);

            Assert.That(state.DeadlineActive, Is.False, "교전 기록이 없으면 발동하지 않는다");

            state.AfterDamage(start + 1f, 5f, 95f, 100f, true);
            state.Tick(start + 2f, start + 2f, true, false);

            Assert.That(state.DeadlineActive, Is.False, "지나간 발동 시점은 따라오지 않는다");
        }

        [Test]
        public void Deadline_DoesNotTriggerWhileCombatInactive()
        {
            var state = State("deadline");
            float start = Number("deadline", "start_seconds");

            state.Tick(0f, 0f, true, false);
            state.AfterDamage(start - 1f, 5f, 95f, 100f, true);
            state.Tick(start + 0.1f, start + 0.1f, false, false);
            state.Tick(start + 0.2f, start + 0.2f, true, false);

            Assert.That(state.DeadlineActive, Is.False, "전투가 꺼져 있던 동안의 경계는 없던 일이다");
        }

        // ── 이자 폭발 ────────────────────────────────────────────────────────

        [Test]
        public void Interest_NeedsDepositAndWindow()
        {
            var state = State("interest-burst");
            float start = Number("interest-burst", "start_seconds");
            float duration = Number("interest-burst", "duration");

            state.Tick(start + 1f, start + 1f, true, false);
            Assert.That(state.InterestActive, Is.False, "예치하지 않으면 버프가 없다");

            state.SetInterestDeposited(true);
            state.Tick(start + 2f, start + 2f, true, false);
            Assert.That(state.InterestActive, Is.True, "예치했고 구간 안이면 켜진다");
            Near(
                Number("interest-burst", "damage_bonus"),
                state.BeginAttack(start + 2f, WeaponTag.Bow).DamageBonus,
                "피해 보너스가 붙는다");

            state.Tick(start + duration + 1f, start + duration + 1f, true, false);
            Assert.That(state.InterestActive, Is.False, "구간이 끝나면 꺼진다");
        }

        // ── 안전 자산 · 보호막 ───────────────────────────────────────────────

        [Test]
        public void SafeAsset_ReducesOnlyEnemyPrimaryUntilNextAttack()
        {
            var state = State("safe-asset");
            float idle = Number("safe-asset", "idle_seconds");
            float reduction = Number("safe-asset", "damage_reduction");

            state.Tick(idle - 0.1f, idle - 0.1f, true, false);
            Near(0f, state.IncomingDamageTakenBonus(idle - 0.1f, true), "충전 전에는 감소가 없다");

            state.Tick(idle, idle, true, false);
            // 감소율은 계산 4단계의 받는 피해 합산에 더한다 — 뒤에서 곱하지 않는다(S-GD-SH74SC).
            Near(-reduction, state.IncomingDamageTakenBonus(idle, true), "적 주무기만 줄인다");
            Near(0f, state.IncomingDamageTakenBonus(idle, false), "장판·기타 피해는 그대로다");
            Near(100f, state.BeforeDamage(idle, 100f, 100f, 100f, true), "계산이 끝난 피해에 다시 곱하지 않는다");
        }

        [Test]
        public void SafeAsset_AttackConvertsChargeToExpiringShield()
        {
            var state = State("safe-asset");
            float idle = Number("safe-asset", "idle_seconds");
            float shield = Number("safe-asset", "shield");
            float seconds = Number("safe-asset", "shield_seconds");

            state.Tick(idle, idle, true, false);
            state.BeginAttack(idle, WeaponTag.Sword);

            Near(shield, state.Shield, "공격하는 순간 보호막으로 바뀐다");
            Near(0f, state.IncomingDamageTakenBonus(idle, true), "감소는 사라진다");
            Near(100f, state.BeforeDamage(idle, 100f + shield, 200f, 200f, true), "보호막이 막는다");
            Near(0f, state.Shield, "막은 만큼 닳는다");

            state.Tick(idle + idle, idle + idle, true, false);
            state.BeginAttack(idle + idle, WeaponTag.Sword);
            Near(shield, state.Shield, "다시 붙어도 누적이 아니라 같은 값이다");

            state.Tick(idle + idle + seconds, idle + idle + seconds, true, false);
            Near(0f, state.Shield, "보호막은 시간이 지나면 사라진다");
        }

        [Test]
        public void TwoShields_SumAndExpireSeparately()
        {
            var state = State("safe-asset", "insurance");
            float threshold = Number("insurance", "damage_threshold");
            float delay = Number("insurance", "delay");
            float insured = Number("insurance", "shield");
            float idle = Number("safe-asset", "idle_seconds");
            float safeShield = Number("safe-asset", "shield");

            // 안전 자산이 충전되는 시각에 보험 보호막이 도착하도록 맞춘다.
            float grantAt = idle;

            state.Tick(0f, 0f, true, false);
            state.AfterDamage(grantAt - delay, threshold, 100f - threshold, 100f, true);
            state.Tick(grantAt, grantAt, true, false);
            Near(insured, state.Shield, "보험 보호막이 붙는다");

            state.BeginAttack(grantAt, WeaponTag.Sword);
            Near(insured + safeShield, state.Shield, "출처가 다르면 합쳐진다");

            float total = insured + safeShield;
            Near(0f, state.BeforeDamage(grantAt, total, 500f, 500f, false), "두 보호막이 모든 피해를 막는다");
            Near(0f, state.Shield, "둘 다 소진된다");
        }

        [Test]
        public void TwoShields_TheOneEndingSoonerAbsorbsFirst()
        {
            // 보호막이 둘 이상이면 먼저 끝나는 것부터 깎는다(S-GD-GQZX55).
            var state = State("safe-asset", "insurance");
            float threshold = Number("insurance", "damage_threshold");
            float delay = Number("insurance", "delay");
            float insured = Number("insurance", "shield");
            float insuredSeconds = Number("insurance", "shield_seconds");
            float idle = Number("safe-asset", "idle_seconds");
            float safeShield = Number("safe-asset", "shield");
            float safeSeconds = Number("safe-asset", "shield_seconds");
            Assert.That(safeSeconds, Is.LessThan(insuredSeconds), "전제: 안전 자산 보호막이 먼저 끝난다");

            float grantAt = idle;
            state.Tick(0f, 0f, true, false);
            state.AfterDamage(grantAt - delay, threshold, 100f - threshold, 100f, true);
            state.Tick(grantAt, grantAt, true, false);
            state.BeginAttack(grantAt, WeaponTag.Sword);
            Near(insured + safeShield, state.Shield);

            // 안전 자산 몫만큼 맞는다 — 먼저 끝나는 안전 자산이 다 닳아야 한다.
            Near(0f, state.BeforeDamage(grantAt, safeShield, 500f, 500f, false));
            Near(insured, state.Shield, "오래 남는 보험 보호막은 그대로다");

            float between = grantAt + (safeSeconds + insuredSeconds) * 0.5f;
            state.Tick(between, between, true, false);
            Near(insured, state.Shield, "안전 자산이 끝난 뒤에도 보험 보호막이 온전히 남는다");
        }

        // ── 보험 약관 ────────────────────────────────────────────────────────

        [Test]
        public void Insurance_RollingWindowDropsOldDamage()
        {
            var state = State("insurance");
            float threshold = Number("insurance", "damage_threshold");
            float window = Number("insurance", "damage_window");
            float delay = Number("insurance", "delay");
            float part = threshold * 0.6f;

            state.Tick(0f, 0f, true, false);
            state.AfterDamage(0f, part, 100f - part, 200f, true);

            // 두 번째 타격 시점에는 첫 타격이 이미 창 밖이라 누적이 기준을 넘지 못한다.
            float second = window * 1.25f;
            state.AfterDamage(second, part, 100f - part * 2f, 200f, true);

            float third = second + delay + 0.1f;
            state.Tick(third, third, true, false);
            Near(0f, state.Shield, "창 밖으로 나간 피해는 누적에서 빠진다");

            state.AfterDamage(third, part, 100f - part * 3f, 200f, true);
            state.Tick(third + delay * 0.5f, third + delay * 0.5f, true, false);
            Near(0f, state.Shield, "보호막은 즉시 생기지 않는다");

            state.Tick(third + delay, third + delay, true, false);
            Near(Number("insurance", "shield"), state.Shield, "지연이 지나면 생긴다");
        }

        [Test]
        public void Insurance_DoesNotSaveAlreadyDead()
        {
            var state = State("insurance");
            float threshold = Number("insurance", "damage_threshold");
            float delay = Number("insurance", "delay");

            state.Tick(0f, 0f, true, false);
            state.AfterDamage(0f, threshold, 100f - threshold, 200f, true);
            state.AfterDamage(delay * 0.5f, 100f, 0f, 200f, true);
            state.Tick(delay + 0.1f, delay + 0.1f, true, false);

            Near(0f, state.Shield, "이미 죽었으면 보호막이 오지 않는다");
        }

        // ── 반격 배당 ────────────────────────────────────────────────────────

        [Test]
        public void Counter_GroupsRepeatedHitsAndConsumesOnMiss()
        {
            var state = State("counter");
            float group = Number("counter", "hit_group_window");
            int hits = (int)Number("counter", "hits");

            state.Tick(0f, 0f, true, false);

            float at = 0f;

            for (int i = 0; i < hits - 1; i++)
            {
                state.AfterDamage(at, 5f, 90f, 100f, true);
                state.AfterDamage(at + group * 0.25f, 5f, 85f, 100f, true);
                at += group * 1.5f;
            }

            state.AfterDamage(at, 5f, 80f, 100f, false);
            Assert.That(state.CounterReady, Is.False, "적 주무기가 아닌 피해는 세지 않는다");

            state.AfterDamage(at, 5f, 75f, 100f, true);
            Assert.That(state.CounterReady, Is.True, "묶음 밖의 타격이 필요 횟수를 채우면 충전된다");

            var attack = state.BeginAttack(at + 1f, WeaponTag.Sword);
            Assert.That(attack.CounterWave, Is.True, "다음 기본 공격이 충격파가 된다");
            Assert.That(state.CounterReady, Is.False, "쓰면 풀린다");

            state.CompleteAttack(attack, at + 1.2f, false);
            Assert.That(state.CounterReady, Is.False, "빗나가도 되돌아오지 않는다");
        }

        [Test]
        public void Counter_CooldownStartsWhenWaveIsFired()
        {
            var state = State("counter");
            float group = Number("counter", "hit_group_window");
            int hits = (int)Number("counter", "hits");
            float cooldown = Number("counter", "cooldown");

            state.Tick(0f, 0f, true, false);
            Charge(state, 0f, group, hits);

            // 쿨다운은 충격파가 나간 시점부터 돈다 — 마지막 타격 시각이 아니다.
            float firedAt = group * 1.5f * hits;
            state.BeginAttack(firedAt, WeaponTag.Sword);

            Charge(state, firedAt + cooldown * 0.5f, group, hits);
            Assert.That(state.CounterReady, Is.False, "재사용 대기 중에는 다시 차지 않는다");

            Charge(state, firedAt + cooldown + 0.1f, group, hits);
            Assert.That(state.CounterReady, Is.True, "대기가 끝나면 다시 찬다");
        }

        // ── 과열 배당 ────────────────────────────────────────────────────────

        [Test]
        public void Overheat_StacksOncePerAttackAndCapsBonus()
        {
            var state = State("overheat");
            int max = (int)Number("overheat", "max_stacks");
            float perStack = Number("overheat", "attack_speed_per_stack");

            state.Tick(0f, 0f, true, false);

            var first = state.BeginAttack(0f, WeaponTag.Sword);
            state.ConfirmHit(first, 1, 0f, 0f, 0f);
            Near(perStack, state.AttackSpeedBonus, "적중 하나당 한 중첩이다");

            var duplicate = state.ConfirmHit(first, 1, 0f, 0f, 0f);
            Near(0f, duplicate.ExtraDamage, "같은 공격·같은 대상은 두 번 세지 않는다");
            state.ConfirmHit(first, 2, 0f, 0f, 0f);
            Near(perStack, state.AttackSpeedBonus, "여러 대상을 맞혀도 중첩은 한 번이다");
            state.CompleteAttack(first, 0f, true);

            for (int i = 1; i < max; i++)
            {
                var attack = state.BeginAttack(i, WeaponTag.Sword);
                state.ConfirmHit(attack, 1, i, 0f, 0f);
                state.CompleteAttack(attack, i, true);
            }

            Near(max * perStack, state.AttackSpeedBonus, "최대 중첩까지 쌓인다");
            Near(
                Number("overheat", "max_damage_bonus"),
                state.BeginAttack(max, WeaponTag.Sword).DamageBonus,
                "최대 중첩에서는 피해 보너스가 붙는다");
        }

        [Test]
        public void Overheat_MissHasNoPenalty()
        {
            // 엑셀 개편 — 빗나감 패널티(중첩 소실·공격 지연)가 삭제됐다(S-GD-TMPBEF).
            var state = State("overheat");
            float perStack = Number("overheat", "attack_speed_per_stack");

            state.Tick(0f, 0f, true, false);

            var hit = state.BeginAttack(0f, WeaponTag.Sword);
            state.ConfirmHit(hit, 1, 0f, 0f, 0f);
            state.CompleteAttack(hit, 0f, true);

            var missed = state.BeginAttack(1f, WeaponTag.Sword);
            state.CompleteAttack(missed, 1f, false);

            Near(perStack, state.AttackSpeedBonus, "빗나가도 중첩은 그대로다");
            Near(0f, state.AttackBlockedUntil, "다음 공격이 늦어지지 않는다");
        }

        [Test]
        public void Overheat_EachStackAlsoRaisesMoveSpeed()
        {
            var state = State("overheat");
            float perStack = Number("overheat", "move_speed_per_stack");
            float window = Number("overheat", "stack_window");

            state.Tick(0f, 0f, true, false);
            Near(0f, state.MoveBonus, "중첩이 없으면 이동 보너스도 없다");

            for (int i = 0; i < 2; i++)
            {
                var attack = state.BeginAttack(i * 0.5f, WeaponTag.Sword);
                state.ConfirmHit(attack, 1, i * 0.5f, 0f, 0f);
                state.CompleteAttack(attack, i * 0.5f, true);
            }

            Near(2f * perStack, state.MoveBonus, "중첩 하나마다 이동 속도가 오른다");

            state.Tick(0.5f + window + 0.1f, 0.5f + window + 0.1f, true, false);
            Near(0f, state.MoveBonus, "중첩이 풀리면 이동 보너스도 사라진다");
        }

        [Test]
        public void Overheat_StacksExpireAfterWindow()
        {
            var state = State("overheat");
            float window = Number("overheat", "stack_window");

            state.Tick(0f, 0f, true, false);

            var attack = state.BeginAttack(0f, WeaponTag.Sword);
            state.ConfirmHit(attack, 1, 0f, 0f, 0f);
            state.CompleteAttack(attack, 0f, true);

            state.Tick(window - 0.1f, window - 0.1f, true, false);
            Assert.That(state.AttackSpeedBonus > 0f, Is.True, "창 안에서는 유지된다");

            state.Tick(window + 0.1f, window + 0.1f, true, false);
            Near(0f, state.AttackSpeedBonus, "다시 맞히지 못하면 풀린다");
        }

        [Test]
        public void CompleteAttack_DoesNotTurnConfirmedHitIntoMiss()
        {
            var state = State("overheat");

            state.Tick(0f, 0f, true, false);

            var attack = state.BeginAttack(0f, WeaponTag.Bow);
            state.ConfirmHit(attack, 1, 0f, 1f, 0f);
            state.CompleteAttack(attack, 2f, false);

            Assert.That(state.AttackSpeedBonus > 0f, Is.True, "맞은 공격은 뒤늦은 보고로도 빗나감이 되지 않는다");
            Near(0f, state.AttackBlockedUntil, "빗나감 후딜도 걸리지 않는다");
        }

        [Test]
        public void StaleGeneration_IsIgnored()
        {
            var state = State("overheat");

            state.Tick(0f, 0f, true, false);

            var attack = state.BeginAttack(0f, WeaponTag.Bow);
            state.ConfirmHit(attack, 1, 0f, 1f, 0f);
            float bonus = state.AttackSpeedBonus;

            state.ReplaceLoadout(Definitions("overheat", "counter"));

            var late = state.ConfirmHit(attack, 2, 1f, 1f, 0f);
            Near(0f, late.ExtraDamage, "세대가 다른 적중은 무시된다");
            Near(bonus, state.AttackSpeedBonus, "중첩도 건드리지 않는다");

            state.CompleteAttack(attack, 1f, false);
            Near(bonus, state.AttackSpeedBonus, "세대가 다른 빗나감도 무시된다");
            Near(0f, state.AttackBlockedUntil);
        }

        // ── 삼연참 · 역날 베기 ───────────────────────────────────────────────

        [TestCase(45f, 20f)]
        [TestCase(120f, 95f)]
        [TestCase(30f, 10f)]
        public void TripleSlash_CurrentArcReductionMatchesPreviewAndAttack(float currentHalfAngle, float expected)
        {
            var state = State("triple-slash");
            state.Tick(0f, 0f, true, false);
            LandSwordHits(state, 0f, (int)Number("triple-slash", "hits") - 1);
            var preview = CombatantStatFormula.ApplyToAttack(state.PreviewGeometry(WeaponTag.Sword), 1f, currentHalfAngle, 1f);
            var repeatedPreview = CombatantStatFormula.ApplyToAttack(state.PreviewGeometry(WeaponTag.Sword), 1f, currentHalfAngle, 1f);
            var third = CombatantStatFormula.ApplyToAttack(state.BeginAttack(1f, WeaponTag.Sword), 1f, currentHalfAngle, 1f);
            Near(expected, preview.HalfAngleOverride);
            Near(expected, repeatedPreview.HalfAngleOverride, "가이드는 삼연참 준비 상태를 소비하지 않는다");
            Near(expected, third.HalfAngleOverride);
            var fourth = CombatantStatFormula.ApplyToAttack(state.BeginAttack(1.1f, WeaponTag.Sword), 1f, currentHalfAngle, 1f);
            Near(currentHalfAngle, fourth.HalfAngleOverride, "다음 공격에는 반각 감소가 남지 않는다");
        }

        [Test]
        public void TripleSlash_ThirdSwordAttackGetsGeometryAndDamage()
        {
            var state = State("triple-slash");
            int hits = (int)Number("triple-slash", "hits");

            state.Tick(0f, 0f, true, false);
            LandSwordHits(state, 0f, hits - 1);

            var third = state.BeginAttack(1f, WeaponTag.Sword);
            Near(Number("triple-slash", "damage_bonus"), third.DamageBonus, "마지막 타격에 피해가 붙는다");
            Near(1f + Number("triple-slash", "range_bonus"), third.RangeMultiplier, "사거리가 늘어난다");
            Assert.That(third.HasHalfAngleOverride, Is.False, "삼연참은 절대 반각을 덮어쓰지 않는다");
            Near(Number("triple-slash", "half_angle_reduction"), third.HalfAngleReduction);
            Near(Number("triple-slash", "min_half_angle"), third.MinimumHalfAngle);

            var fourth = state.BeginAttack(1.1f, WeaponTag.Sword);
            Near(0f, fourth.DamageBonus, "연속 수는 발동과 함께 초기화된다");
            Assert.That(fourth.HasHalfAngleOverride, Is.False);
        }

        [Test]
        public void TripleSlash_MissAndTimeoutResetCombo()
        {
            var state = State("triple-slash");
            int hits = (int)Number("triple-slash", "hits");
            float window = Number("triple-slash", "window");

            state.Tick(0f, 0f, true, false);
            LandSwordHits(state, 0f, hits - 1);

            var missed = state.BeginAttack(0.9f, WeaponTag.Sword);
            state.CompleteAttack(missed, 0.9f, false);
            Near(0f, state.BeginAttack(1f, WeaponTag.Sword).DamageBonus, "중간에 빗나가면 초기화된다");

            LandSwordHits(state, 2f, hits - 1);
            state.Tick(2f + window + 0.1f, 2f + window + 0.1f, true, false);
            Near(0f, state.BeginAttack(2f + window + 0.1f, WeaponTag.Sword).DamageBonus, "시간이 지나도 초기화된다");
        }

        [Test]
        public void TripleSlash_OnlyCountsSwordAttacksOncePerAttack()
        {
            var state = State("triple-slash");
            int hits = (int)Number("triple-slash", "hits");

            state.Tick(0f, 0f, true, false);

            // 한 번의 공격이 여러 대상을 쓸어도 연속 수는 하나만 오른다.
            for (int i = 0; i < hits - 1; i++)
            {
                var attack = state.BeginAttack(i * 0.1f, WeaponTag.Sword);
                state.ConfirmHit(attack, 1, i * 0.1f, 0f, 0f);
                state.ConfirmHit(attack, 2, i * 0.1f, 0f, 0f);
                state.ConfirmHit(attack, 3, i * 0.1f, 0f, 0f);
                state.CompleteAttack(attack, i * 0.1f, true);
            }

            Near(
                Number("triple-slash", "damage_bonus"),
                state.BeginAttack(1f, WeaponTag.Sword).DamageBonus,
                "쓸어 맞혀도 필요한 공격 횟수는 그대로다");
        }

        [Test]
        public void ReverseBlade_IsOneUsePerDashAndWinsGeometry()
        {
            var state = State("reverse-blade", "triple-slash");
            int hits = (int)Number("triple-slash", "hits");

            state.Tick(0f, 0f, true, false);
            LandSwordHits(state, 0f, hits - 1);
            state.OnDash(1f, WeaponTag.Sword);

            var attack = state.BeginAttack(1f, WeaponTag.Sword);
            Near(Number("reverse-blade", "half_angle"), attack.HalfAngleOverride, "형태는 역날이 이긴다");
            Near(
                (1f + Number("triple-slash", "range_bonus")) * Number("reverse-blade", "range_multiplier"),
                attack.RangeMultiplier,
                "사거리는 보너스를 더한 뒤 감소를 곱한다");
            Near(Number("triple-slash", "damage_bonus"), attack.DamageBonus, "삼연참 피해는 그대로 들어간다");
            Near(Number("reverse-blade", "damage_multiplier"), attack.DamagePenaltyMultiplier, "역날 감소도 같이 걸린다");

            var second = state.BeginAttack(1.1f, WeaponTag.Sword);
            Near(1f, second.DamagePenaltyMultiplier, "대시당 한 번만 쓴다");
            Near(0f, second.DamageBonus, "연속 수도 이미 소모됐다");
        }

        [Test]
        public void ReverseBlade_WindowExpiresAndBowDoesNotConsume()
        {
            var state = State("reverse-blade");
            float window = Number("reverse-blade", "window");

            state.OnDash(0f, WeaponTag.Sword);
            state.BeginAttack(0.1f, WeaponTag.Bow);
            Near(
                Number("reverse-blade", "damage_multiplier"),
                state.BeginAttack(0.2f, WeaponTag.Sword).DamagePenaltyMultiplier,
                "활 공격은 역날을 소모하지 않는다");

            state.OnDash(1f, WeaponTag.Sword);
            Near(
                1f,
                state.BeginAttack(1f + window + 0.1f, WeaponTag.Sword).DamagePenaltyMultiplier,
                "창이 지나면 사라진다");
        }

        // ── 느린 한 발 · 사선 고정 ───────────────────────────────────────────

        [Test]
        public void SlowShot_OnlyBeginConsumesCooldown()
        {
            var state = State("slow-shot");
            float cooldown = Number("slow-shot", "cooldown");
            float windup = Number("slow-shot", "windup");

            Near(windup, state.WindupSeconds(0f, WeaponTag.Bow), "첫 발부터 강화탄이 준비되어 있다");
            Near(0f, state.WindupSeconds(0f, WeaponTag.Sword), "검에는 해당하지 않는다");
            Near(windup, state.WindupSeconds(0f, WeaponTag.Bow), "예고를 물어도 쿨다운이 돌지 않는다");

            var attack = state.BeginAttack(0f, WeaponTag.Bow);
            Near(Number("slow-shot", "speed_multiplier"), attack.ProjectileSpeedMultiplier);
            Near(Number("slow-shot", "radius"), attack.ProjectileRadius);
            Near(Number("slow-shot", "stagger"), attack.StaggerSeconds);
            Near(Number("slow-shot", "recovery_multiplier"), attack.RecoveryMultiplier);

            Near(0f, state.WindupSeconds(0.1f, WeaponTag.Bow), "쏘고 나면 대기 중이다");
            var plain = state.BeginAttack(cooldown - 0.1f, WeaponTag.Bow);
            Near(1f, plain.ProjectileSpeedMultiplier, "대기 중에는 보통 화살이다");
            Near(0f, plain.StaggerSeconds);

            Near(windup, state.WindupSeconds(cooldown, WeaponTag.Bow), "대기가 끝나면 다시 준비된다");
        }

        [Test]
        public void FixedLine_ChargesWhileStationaryAndBreaksOnMotionOrDamage()
        {
            var state = State("fixed-line");
            float charge = Number("fixed-line", "charge_seconds");

            state.Tick(0f, 0f, true, false);
            state.Tick(charge - 0.1f, charge - 0.1f, true, false);
            Assert.That(state.FixedLineReady, Is.False, "충전 중에는 준비되지 않는다");

            state.Tick(charge, charge, true, false);
            Assert.That(state.FixedLineReady, Is.True, "가만히 있으면 충전된다");

            state.BeginAttack(charge, WeaponTag.Sword);
            Assert.That(state.FixedLineReady, Is.True, "활이 아니면 쓰지 않는다");

            var shot = state.BeginAttack(charge, WeaponTag.Bow);
            Near(1f + Number("fixed-line", "range_bonus"), shot.RangeMultiplier, "사거리가 늘어난다");
            Near(Number("fixed-line", "radius"), shot.ProjectileRadius, "판정 반경이 커진다");
            Assert.That(state.FixedLineReady, Is.False, "한 발에 소모된다");

            float moved = charge * 2f;
            state.Tick(moved, moved, true, true);
            state.Tick(moved + charge - 0.1f, moved + charge - 0.1f, true, false);
            Assert.That(state.FixedLineReady, Is.False, "움직이면 충전이 사라진다");

            state.Tick(moved + charge, moved + charge, true, false);
            Assert.That(state.FixedLineReady, Is.True, "다시 멈추면 처음부터 찬다");

            float hurt = moved + charge;
            state.BeforeDamage(hurt, 10f, 100f, 100f, true);
            state.Tick(hurt + charge - 0.1f, hurt + charge - 0.1f, true, false);
            Assert.That(state.FixedLineReady, Is.False, "피격도 충전을 지운다");
        }

        [Test]
        public void FixedLine_CumulativeDisplacementBreaksCharge()
        {
            var state = State("fixed-line");
            float charge = Number("fixed-line", "charge_seconds");
            float threshold = Number("fixed-line", "movement_threshold");

            state.Tick(0f, 0f, true, false);
            state.NotifyMovement(0.1f, threshold * 0.4f);
            state.NotifyMovement(0.2f, threshold * 0.4f);
            state.Tick(charge, charge, true, false);
            Assert.That(state.FixedLineReady, Is.True, "기준 미만의 미끄러짐은 충전을 깨지 않는다");

            state.NotifyMovement(charge + 0.1f, threshold * 0.6f);
            state.NotifyMovement(charge + 0.2f, threshold * 0.6f);
            Assert.That(state.FixedLineReady, Is.False, "누적이 기준을 넘으면 사라진다");
        }

        // ── 거리 · 칼끝 · 표식 ───────────────────────────────────────────────

        [Test]
        public void LongDividend_IsRangedOnlyAndScalesWithDistance()
        {
            var state = State("long-dividend");
            float min = Number("long-dividend", "min_distance");
            float max = Number("long-dividend", "max_distance");
            float maxBonus = Number("long-dividend", "max_damage_bonus");
            float maxStagger = Number("long-dividend", "max_stagger");

            var melee = state.BeginAttack(0f, WeaponTag.Sword);
            var meleeHit = state.PreviewHit(melee, max, 1f);
            Near(0f, meleeHit.DamageBonus, "근접에는 거리 배당이 없다");
            Near(1f, meleeHit.DamagePenaltyMultiplier);

            var shot = state.BeginAttack(0f, WeaponTag.Bow);

            var close = state.PreviewHit(shot, min, 0f);
            // 가까우면 줄어들되 곱하지 않고 피해 증감 합계에 더한다(공격 속도 감소 패널티만 곱연산).
            Near(Number("long-dividend", "close_multiplier") - 1f, close.DamageBonus, "가까우면 줄어든다");
            Near(1f, close.DamagePenaltyMultiplier, "곱하는 배율로 넣지 않는다");

            float middle = (min + max) * 0.5f;
            var mid = state.PreviewHit(shot, middle, 0f);
            Near(maxBonus * 0.5f, mid.DamageBonus, "거리에 비례해 오른다");
            Near(maxStagger * 0.5f, mid.StaggerSeconds);

            var far = state.PreviewHit(shot, max * 2f, 0f);
            Near(maxBonus, far.DamageBonus, "최대 거리에서 멈춘다");
            Near(maxStagger, far.StaggerSeconds);
        }

        [Test]
        public void BladeTip_NeedsOuterFractionAndGrantsMoveBonus()
        {
            var state = State("blade-tip");
            float fraction = Number("blade-tip", "min_range_fraction");
            float duration = Number("blade-tip", "duration");

            state.Tick(0f, 0f, true, false);

            var attack = state.BeginAttack(0f, WeaponTag.Sword);
            Near(0f, state.PreviewHit(attack, 0f, fraction * 0.5f).StaggerSeconds, "가까이 붙으면 보너스가 없다");
            Near(Number("blade-tip", "stagger"), state.PreviewHit(attack, 0f, fraction).StaggerSeconds, "칼끝이면 경직이 붙는다");
            Near(0f, state.MoveBonus, "미리보기는 상태를 바꾸지 않는다");

            var result = state.ConfirmHit(attack, 1, 0f, 0f, fraction);
            Near(Number("blade-tip", "stagger"), result.StaggerSeconds, "확정 적중도 같은 경직을 준다");
            Near(Number("blade-tip", "move_bonus"), state.MoveBonus, "이동 보너스가 붙는다");

            state.Tick(duration + 0.1f, duration + 0.1f, true, false);
            Near(0f, state.MoveBonus, "지속이 끝나면 사라진다");
        }

        [Test]
        public void MarkShot_TransfersOwnershipAndNeedsGapWithinLifetime()
        {
            var state = State("mark-shot");
            float gap = Number("mark-shot", "cooldown");
            float life = Number("mark-shot", "mark_seconds");
            float damage = Number("mark-shot", "damage");

            state.Tick(0f, 0f, true, false);

            var first = state.BeginAttack(0f, WeaponTag.Bow);
            var applied = state.ConfirmHit(first, 7, 0f, 5f, 0f);
            Assert.That(applied.MarkApplied, Is.True, "첫 적중이 표식을 남긴다");
            Assert.That(applied.MarkDetonated, Is.False);
            Near(0f, applied.ExtraDamage);

            var tooSoon = state.BeginAttack(gap * 0.5f, WeaponTag.Bow);
            var early = state.ConfirmHit(tooSoon, 7, gap * 0.5f, 5f, 0f);
            Assert.That(early.MarkDetonated, Is.False, "간격을 채우기 전에는 터지지 않는다");

            var second = state.BeginAttack(gap * 2f, WeaponTag.Bow);
            var boom = state.ConfirmHit(second, 7, gap * 2f, 5f, 0f);
            Assert.That(boom.MarkDetonated, Is.True, "같은 대상을 다시 맞히면 터진다");
            Near(damage, boom.ExtraDamage);

            var other = state.BeginAttack(gap * 3f, WeaponTag.Bow);
            var moved = state.ConfirmHit(other, 8, gap * 3f, 5f, 0f);
            Assert.That(moved.MarkApplied, Is.True, "표식은 하나뿐이라 대상을 옮긴다");
            Assert.That(moved.MarkDetonated, Is.False);

            var back = state.BeginAttack(gap * 4f, WeaponTag.Bow);
            var returned = state.ConfirmHit(back, 7, gap * 4f, 5f, 0f);
            Assert.That(returned.MarkDetonated, Is.False, "옮겨간 뒤에는 예전 대상이 비어 있다");

            var expired = state.BeginAttack(gap * 4f + life + 0.1f, WeaponTag.Bow);
            var stale = state.ConfirmHit(expired, 7, gap * 4f + life + 0.1f, 5f, 0f);
            Assert.That(stale.MarkDetonated, Is.False, "수명이 지난 표식은 터지지 않는다");
            Assert.That(stale.MarkApplied, Is.True, "대신 새로 붙는다");
        }

        [Test]
        public void MarkShot_DetonationConsumesTheMarkAndNeedsANewHitToReapply()
        {
            var state = State("mark-shot");
            float gap = Number("mark-shot", "cooldown");
            float damage = Number("mark-shot", "damage");

            state.Tick(0f, 0f, true, false);

            // 적중 → 표식, 다음 적중 → 폭발, 그 다음 적중 → 다시 표식. 폭발한 적중이 표식을 다시 남기면
            // 첫 적중 이후의 모든 적중이 폭발이 되어 "연속으로 적중해야 한다" 는 조건이 사라진다.
            bool[] expectedApplied = { true, false, true, false };
            bool[] expectedDetonated = { false, true, false, true };

            for (int i = 0; i < expectedApplied.Length; i++)
            {
                float at = i * gap * 2f;
                var attack = state.BeginAttack(at, WeaponTag.Bow);
                var result = state.ConfirmHit(attack, 7, at, 5f, 0f);

                Assert.That(result.MarkApplied, Is.EqualTo(expectedApplied[i]), $"{i}번째 적중의 표식 부착");
                Assert.That(result.MarkDetonated, Is.EqualTo(expectedDetonated[i]), $"{i}번째 적중의 폭발");
                Near(expectedDetonated[i] ? damage : 0f, result.ExtraDamage, $"{i}번째 적중의 추가 피해");
            }
        }

        [Test]
        public void MarkShot_ResetRoundClearsTheMarkEvenWhenTheClockRestarts()
        {
            var state = State("mark-shot");
            float gap = Number("mark-shot", "cooldown");

            state.Tick(gap * 10f, gap * 10f, true, false);
            var previous = state.BeginAttack(gap * 10f, WeaponTag.Bow);
            state.ConfirmHit(previous, 7, gap * 10f, 5f, 0f);

            // 라운드마다 0 부터 세는 어댑터. 지난 라운드의 표식이 남아 있으면 시각이 미래처럼 보여
            // 새 라운드의 첫 적중이 "몰린 적중" 으로 오인되고 표식도 폭발도 사라진다.
            state.ResetRound(0f);
            state.Tick(0f, 0f, true, false);

            var attack = state.BeginAttack(0f, WeaponTag.Bow);
            var result = state.ConfirmHit(attack, 7, 0f, 5f, 0f);

            Assert.That(result.MarkApplied, Is.True, "새 라운드의 첫 적중은 표식을 새로 남긴다");
            Assert.That(result.MarkDetonated, Is.False, "지난 라운드의 표식은 터지지 않는다");
            Near(0f, result.ExtraDamage);
        }

        [Test]
        public void MarkShot_IgnoresMeleeAttacks()
        {
            var state = State("mark-shot");

            state.Tick(0f, 0f, true, false);

            var attack = state.BeginAttack(0f, WeaponTag.Sword);
            var result = state.ConfirmHit(attack, 7, 0f, 5f, 1f);

            Assert.That(result.MarkApplied, Is.False, "원거리 축복은 검 공격에 붙지 않는다");
        }

        // ── 마지막 칩 · 비상구 ───────────────────────────────────────────────

        [Test]
        public void LastChip_SurvivesOneLethalEnemyHitAndLocksHealing()
        {
            var state = State("last-chip");
            float remaining = Number("last-chip", "remaining_hp");
            float healLock = Number("last-chip", "heal_lock");
            float duration = Number("last-chip", "duration");

            state.Tick(0f, 0f, true, false);

            float survived = state.BeforeDamage(1f, 500f, 30f, 100f, true);
            Near(30f - remaining, survived, "체력 1 만 남기고 버틴다");
            Near(1f + healLock, state.HealingBlockedUntil, "회복이 잠긴다");
            Assert.That(state.LastChipActive, Is.True);
            Near(Number("last-chip", "move_bonus"), state.MoveBonus, "이동 보너스가 붙는다");

            Near(500f, state.BeforeDamage(1f, 500f, remaining, 100f, true), "라운드당 한 번이다");

            state.Tick(1f + duration + 0.1f, 1f + duration + 0.1f, true, false);
            Near(0f, state.MoveBonus, "이동 보너스는 지속이 끝나면 사라진다");
        }

        [Test]
        public void LastChip_HoldsAgainstLethalZoneDamageButDoesNotRevive()
        {
            // 죽을 피해 트리거는 소스와 무관하게 돈다(엑셀 README — S-GD-PA3E9T). 예전에는 장판을 막지 않았다.
            var state = State("last-chip");

            state.Tick(0f, 0f, true, false);
            Near(500f, state.BeforeDamage(1f, 500f, 0f, 100f, true), "이미 죽었으면 살리지 않는다");

            Near(30f - Number("last-chip", "remaining_hp"), state.BeforeDamage(1f, 500f, 30f, 100f, false,
                    float.PositiveInfinity, fixedDamage: true, firesOnDamaged: false),
                "자기장으로 죽을 피해도 버틴다");
            Near(500f, state.BeforeDamage(2f, 500f, 30f, 100f, true), "라운드에 한 번뿐이다");
        }

        [Test]
        public void LastChip_TriggersAtOneHealthAndStillConsumes()
        {
            var state = State("last-chip");
            float remaining = Number("last-chip", "remaining_hp");

            state.Tick(0f, 0f, true, false);
            Near(0f, state.BeforeDamage(1f, remaining, remaining, 100f, true), "남길 체력과 같으면 피해가 0 이 된다");
            Near(500f, state.BeforeDamage(2f, 500f, remaining, 100f, true), "그래도 한 번은 소모된다");
        }

        [Test]
        public void LoadoutRebuild_DoesNotRearmLastChip()
        {
            var state = State("last-chip");

            state.Tick(0f, 0f, true, false);
            state.BeforeDamage(1f, 500f, 30f, 100f, true);

            state.ReplaceLoadout(Definitions("last-chip", "counter"));
            Near(500f, state.BeforeDamage(2f, 500f, 30f, 100f, true), "축복을 더 얻어도 라운드당 한 번은 그대로다");

            state.ResetRound(10f);
            Near(30f - Number("last-chip", "remaining_hp"), state.BeforeDamage(11f, 500f, 30f, 100f, true),
                "새 라운드에서는 다시 쓸 수 있다");
        }

        [Test]
        public void EmergencyExit_GrantsDashResetOnceWithAttackPenalty()
        {
            var state = State("emergency-exit");
            float threshold = Number("emergency-exit", "hp_threshold");
            float penalty = Number("emergency-exit", "penalty");
            float seconds = Number("emergency-exit", "penalty_seconds");
            float max = 100f;

            state.Tick(0f, 0f, true, false);

            state.AfterDamage(1f, 20f, max * threshold + 10f, max, true);
            Assert.That(state.ConsumeEmergencyDashReset(), Is.False, "기준 위에서는 발동하지 않는다");

            state.AfterDamage(2f, 20f, max * threshold - 1f, max, true);
            Assert.That(state.ConsumeEmergencyDashReset(), Is.True, "기준을 넘어 내려가면 한 번 준다");
            Assert.That(state.ConsumeEmergencyDashReset(), Is.False, "한 번만 가져갈 수 있다");
            var weakened = state.BeginAttack(2f, WeaponTag.Sword);
            Near(100f * (1f - penalty), state.ApplyActiveAttackPower(100f), "공격력 단계에서 감소한다");
            Near(0f, weakened.DamageBonus, "피해 증가에는 섞지 않는다");
            Near(1f, weakened.DamagePenaltyMultiplier, "곱하는 배율로 넣지 않는다");

            state.AfterDamage(3f, 5f, max * threshold - 5f, max, true);
            Assert.That(state.ConsumeEmergencyDashReset(), Is.False, "라운드당 한 번이다");

            state.Tick(2f + seconds + 0.1f, 2f + seconds + 0.1f, true, false);
            Near(0f, state.BeginAttack(2f + seconds + 0.1f, WeaponTag.Sword).DamageBonus, "대가는 시간이 지나면 끝난다");
        }

        [Test]
        public void EmergencyExit_TriggersOnAnyDamageThatCrossesTheThreshold()
        {
            var state = State("emergency-exit");
            float threshold = Number("emergency-exit", "hp_threshold");
            float penalty = Number("emergency-exit", "penalty");
            float max = 100f;

            state.Tick(0f, 0f, true, false);

            // 표는 출처가 아니라 체력만 말한다 — 자기장·장판에 밀려 내려가도 탈출 기회는 같다.
            state.AfterDamage(1f, 80f, max * threshold - 1f, max, false);

            Assert.That(state.ConsumeEmergencyDashReset(), Is.True, "주무기가 아닌 피해로 내려가도 발동한다");
            Near(100f * (1f - penalty), state.ApplyActiveAttackPower(100f), "공격력 대가도 같이 붙는다");

            state.AfterDamage(2f, 5f, max * threshold - 6f, max, true);
            Assert.That(state.ConsumeEmergencyDashReset(), Is.False, "그래도 라운드당 한 번이다");
        }

        [Test]
        public void EmergencyExit_DoesNotTriggerWhenDead()
        {
            var state = State("emergency-exit");

            state.Tick(0f, 0f, true, false);
            state.AfterDamage(1f, 100f, 0f, 100f, true);

            Assert.That(state.ConsumeEmergencyDashReset(), Is.False, "죽은 뒤에는 탈출구가 없다");
        }

        // ── 패링 배당 (엑셀 개편 — S-GD-TMPBEF) ─────────────────────────────

        [Test]
        public void ParryDividend_DashEvadeGrantsChargeShieldAndNextSwordBonus()
        {
            var state = State("parry-dividend");
            float bonusSeconds = Number("parry-dividend", "bonus_seconds");

            state.Tick(0f, 0f, true, false);
            Assert.That(state.NotifyDashEvade(0f), Is.True, "대시 무적으로 막으면 발동한다");

            Assert.That(state.ConsumePendingDashCharges(), Is.EqualTo(1), "대시가 한 번 충전된다");
            Near(Number("parry-dividend", "shield"), state.Shield, "보호막을 얻는다");
            Near(Number("parry-dividend", "shield_seconds"), state.ShieldRemaining, "보호막은 정해진 시간 간다");
            Near(bonusSeconds, state.ParryDividendRemaining, "다음 검 공격 보너스가 기다린다");

            Near(0f, state.BeginAttack(0.05f, WeaponTag.Bow).DamageBonus, "활 공격에는 실리지 않는다");
            Near(Number("parry-dividend", "next_attack_bonus"), state.BeginAttack(0.06f, WeaponTag.Sword).DamageBonus,
                "다음 검 공격이 세진다");
            Near(0f, state.BeginAttack(0.07f, WeaponTag.Sword).DamageBonus, "한 번만 쓴다");
            Near(0f, state.ParryDividendRemaining, "쓰면 칸이 사라진다");
        }

        [Test]
        public void ParryDividend_UnusedBonusExpiresAndCooldownGatesNextEvade()
        {
            var state = State("parry-dividend");
            float cooldown = Number("parry-dividend", "cooldown");
            float bonusSeconds = Number("parry-dividend", "bonus_seconds");

            state.Tick(0f, 0f, true, false);
            Assert.That(state.NotifyDashEvade(0f), Is.True);
            state.ConsumePendingDashCharges();

            Assert.That(state.NotifyDashEvade(cooldown - 0.01f), Is.False, "재사용 대기 중에는 발동하지 않는다");
            Assert.That(state.ConsumePendingDashCharges(), Is.Zero, "대기 중에는 충전도 없다");

            float expired = bonusSeconds + 0.1f;
            state.Tick(expired, expired, true, false);
            Near(0f, state.BeginAttack(expired, WeaponTag.Sword).DamageBonus, "제때 쓰지 않으면 사라진다");

            Assert.That(state.NotifyDashEvade(cooldown + 0.01f), Is.True, "대기가 끝나면 다시 발동한다");
        }

        [Test]
        public void ParryDividend_NoLongerHasWindowPenaltyOrDamageReduction()
        {
            var state = State("parry-dividend");

            state.Tick(0f, 0f, true, false);
            state.OnDash(0f, WeaponTag.Sword);
            Near(100f, state.BeforeDamage(0.05f, 100f, 100f, 100f, true), "대시만으로는 피해를 줄이지 않는다");

            state.Tick(1f, 1f, true, false);
            Near(0f, state.AttackBlockedUntil, "실패 대가(공격 지연)는 없다");
            Near(1f, state.MovePenaltyMultiplier, "실패 대가(이동 감속)도 없다");

            state.ApplyStagger(2f, 1f);
            Near(3f, state.AttackBlockedUntil, "경직도 줄이지 않는다");
        }

        [Test]
        public void ParryDividend_WithoutBlessingIgnoresEvade()
        {
            var state = State("counter");
            state.Tick(0f, 0f, true, false);
            Assert.That(state.NotifyDashEvade(0f), Is.False);
            Assert.That(state.ConsumePendingDashCharges(), Is.Zero);
        }

        // ── 엑셀 개편으로 바뀐 코드 축복 (S-GD-TMPBEF · S-GD-KH2AJ6) ─────────

        [Test]
        public void BladeTip_TipHitAlsoRaisesDamage()
        {
            var state = State("blade-tip");
            float fraction = Number("blade-tip", "min_range_fraction");

            state.Tick(0f, 0f, true, false);
            var attack = state.BeginAttack(0f, WeaponTag.Sword);

            Near(Number("blade-tip", "damage_bonus"), state.PreviewHit(attack, 0f, fraction).DamageBonus,
                "칼끝으로 맞히면 피해가 오른다");
            Near(0f, state.PreviewHit(attack, 0f, fraction * 0.5f).DamageBonus, "가까이서 맞히면 없다");
        }

        [Test]
        public void Counter_AlwaysAddsDefenseWhileHeld()
        {
            var state = State("counter");
            float defense = 0f;
            foreach (var contribution in state.TimedStats)
            {
                if (contribution.StatId == DamageStatKeys.Defense) defense += contribution.Value;
            }

            Near(Number("counter", "defense_bonus"), defense, "조건 없이 방어력이 붙는다");
            Assert.That(State("overheat").TimedStats, Is.Empty, "반격 배당이 없으면 없다");
        }

        [Test]
        public void CreditLine_KillClearsDebtAndHeals()
        {
            var state = State("credit-line");
            float max = 200f;

            state.Tick(0f, 0f, true, false);
            state.BeforeDamage(0f, 100f, max, max, true);
            Assert.That(state.PendingDelayedDamage, Is.GreaterThan(0f), "빚이 쌓인다");

            float heal = state.NotifyKill(0.5f, 3);

            Near(0f, state.PendingDelayedDamage, "처치하면 남은 빚이 사라진다");
            Near(max * Number("credit-line", "heal_on_kill"), heal, "최대 체력의 일부를 회복한다");
            Near(0f, state.ConsumeDelayedDamage(5f), "사라진 빚은 나중에도 들어오지 않는다");
        }

        [Test]
        public void CreditLine_PaysTheDebtInOneSecondTicks()
        {
            // 사용자 확정 2026-09-18 — 받은 피해의 30% 를 1초마다 10% 씩 3번에 나눠 입는다(죽음의 무도).
            var state = State("credit-line");
            float fraction = Number("credit-line", "delayed_fraction");
            float delay = Number("credit-line", "delay");
            int ticks = (int)Math.Ceiling(delay);
            float owed = 100f * fraction;

            state.Tick(0f, 0f, true, false);
            Near(100f - owed, state.BeforeDamage(0f, 100f, 1000f, 1000f, true), "미룬 몫을 뺀 만큼만 바로 들어온다");

            Near(0f, state.ConsumeDelayedDamage(0.5f), "1초가 되기 전에는 아무것도 들어오지 않는다");
            Near(owed / ticks, state.ConsumeDelayedDamage(1f), "1초에 한 몫");
            Near(0f, state.ConsumeDelayedDamage(1.9f), "다음 1초까지 기다린다");
            Near(owed / ticks, state.ConsumeDelayedDamage(2f), "2초에 한 몫");
            Near(owed / ticks, state.ConsumeDelayedDamage(delay + 0.5f), "마지막 몫");
            Near(0f, state.PendingDelayedDamage, "다 들어왔다");
        }

        [Test]
        public void CreditLine_HealingDoesNotReduceTheDebt()
        {
            // 사용자 확정 2026-09-18 — 빚은 처치로만 사라진다. 회복으로 줄지 않는다.
            var state = State("credit-line");
            state.Tick(0f, 0f, true, false);
            state.BeforeDamage(0f, 100f, 1000f, 1000f, true);
            float owed = state.PendingDelayedDamage;

            state.SetHealthSnapshot(1000f, 1000f);

            Near(owed, state.PendingDelayedDamage, "회복해도 빚은 그대로다");
        }

        [Test]
        public void CreditLine_DefersEveryEnemyHitButNotFixedOrOwnerlessDamage()
        {
            // 사용자 확정 2026-09-18 — 적이 준 피해 전부(칼·화살·축복 폭발·투척물). 독·자기장 같은 고정 피해와
            // 미뤄 둔 몫이 들어오는 피해(공격자 없음)는 다시 미루지 않는다.
            float fraction = Number("credit-line", "delayed_fraction");

            var explosion = State("credit-line");
            explosion.Tick(0f, 0f, true, false);
            Near(100f * (1f - fraction), explosion.BeforeDamage(0f, 100f, 1000f, 1000f, false, float.PositiveInfinity,
                fixedDamage: false, firesOnDamaged: true, fromEnemy: true), "축복 폭발·투척물도 미룬다");

            var poison = State("credit-line");
            poison.Tick(0f, 0f, true, false);
            Near(100f, poison.BeforeDamage(0f, 100f, 1000f, 1000f, false, float.PositiveInfinity,
                fixedDamage: true, firesOnDamaged: false, fromEnemy: true), "고정 피해는 미루지 않는다");

            var deferred = State("credit-line");
            deferred.Tick(0f, 0f, true, false);
            Near(100f, deferred.BeforeDamage(0f, 100f, 1000f, 1000f, false, float.PositiveInfinity,
                fixedDamage: false, firesOnDamaged: true, fromEnemy: false), "공격자 없는 피해는 다시 미루지 않는다");
        }

        [Test]
        public void EmergencyExit_AlsoGrantsShortMoveBonus()
        {
            var state = State("emergency-exit");
            float max = 100f;
            float threshold = Number("emergency-exit", "hp_threshold");
            float duration = Number("emergency-exit", "duration");

            state.Tick(0f, 0f, true, false);
            Near(0f, state.MoveBonus);
            state.AfterDamage(1f, 80f, max * threshold - 1f, max, true);

            Assert.That(state.ConsumeEmergencyDashReset(), Is.True, "대시가 전부 충전된다");
            Near(Number("emergency-exit", "move_bonus"), state.MoveBonus, "이동 속도가 오른다");
            Near(duration, state.EmergencyMoveRemaining);

            state.Tick(1f + duration + 0.01f, 1f + duration + 0.01f, true, false);
            Near(0f, state.MoveBonus, "시간이 지나면 끝난다");
        }

        [Test]
        public void FixedLine_ChargedShotAlsoRaisesDamage()
        {
            var state = State("fixed-line");
            float charge = Number("fixed-line", "charge_seconds");

            state.Tick(0f, 0f, true, false);
            state.Tick(charge + 0.1f, charge + 0.1f, true, false);

            Near(Number("fixed-line", "damage_bonus"), state.BeginAttack(charge + 0.1f, WeaponTag.Bow).DamageBonus,
                "고정한 한 발은 피해가 오른다");
            Near(0f, state.BeginAttack(charge + 0.2f, WeaponTag.Bow).DamageBonus, "다음 발은 다시 충전해야 한다");
        }

        [Test]
        public void SightLine_StandingStillAlsoRaisesDamage()
        {
            var state = State("sight-line");
            float still = Number("sight-line", "still_seconds");

            state.Tick(0f, 0f, true, false);
            Near(0f, state.BeginAttack(0f, WeaponTag.Bow).DamageBonus, "버티기 전에는 없다");

            state.Tick(still + 0.1f, still + 0.1f, true, false);
            Near(Number("sight-line", "damage_bonus"), state.BeginAttack(still + 0.1f, WeaponTag.Bow).DamageBonus,
                "제자리에서 버티면 피해가 오른다");
            Near(0f, state.BeginAttack(still + 0.1f, WeaponTag.Sword).DamageBonus, "원거리에만 붙는다");
        }

        [Test]
        public void PiercingBet_RaisesBowDamage()
        {
            var state = State("piercing-bet");
            state.Tick(0f, 0f, true, false);
            Near(Number("piercing-bet", "damage_bonus"), state.BeginAttack(0f, WeaponTag.Bow).DamageBonus);
            Near(0f, state.BeginAttack(0.1f, WeaponTag.Sword).DamageBonus, "활에만 붙는다");
        }

        [Test]
        public void HiltGuard_HasCooldownFromActivation()
        {
            var state = State("hilt-guard");
            float duration = Number("hilt-guard", "duration");
            float cooldown = Number("hilt-guard", "cooldown");

            state.Tick(0f, 0f, true, false);
            state.BeginAttack(0f, WeaponTag.Sword);
            Near(duration, state.HiltGuardRemaining, "검을 내보내면 켜진다");

            float later = duration + 0.1f;
            state.Tick(later, later, true, false);
            state.BeginAttack(later, WeaponTag.Sword);
            Near(0f, state.HiltGuardRemaining, "재사용 대기 중에는 다시 켜지지 않는다");
            Near(100f, state.BeforeDamage(later, 100f, 500f, 500f, true), "켜지지 않았으니 줄지 않는다");

            state.Tick(cooldown + 0.1f, cooldown + 0.1f, true, false);
            state.BeginAttack(cooldown + 0.1f, WeaponTag.Sword);
            Near(duration, state.HiltGuardRemaining, "대기가 끝나면 다시 켜진다");
        }

        [Test]
        public void Insurance_ShieldArrivesWithGold()
        {
            var state = State("insurance");
            float threshold = Number("insurance", "damage_threshold");
            float delay = Number("insurance", "delay");

            state.Tick(0f, 0f, true, false);
            state.AfterDamage(0f, threshold, 200f - threshold, 200f, true);
            Assert.That(state.ConsumeEarnedGold(), Is.Zero, "보호막이 생기기 전에는 없다");

            state.Tick(delay, delay, true, false);
            Near(Number("insurance", "shield"), state.Shield);
            Assert.That(state.ConsumeEarnedGold(), Is.EqualTo((int)Number("insurance", "bonus_gold")), "보호막과 함께 골드");
            Assert.That(state.ConsumeEarnedGold(), Is.Zero, "한 번만 준다");
        }

        [Test]
        public void TokenToss_TriggersOnSwordWithFlatDamageAndCooldown()
        {
            // 예전에는 WeaponTag.Melee 와 같은지만 봐서 실제 전투의 Sword 로는 한 번도 발동하지 않았다.
            var state = State("token-toss");
            float cooldown = Number("token-toss", "cooldown");

            state.Tick(0f, 0f, true, false);
            var first = state.BeginAttack(0f, WeaponTag.Sword);
            Near(Number("token-toss", "damage"), state.PreviewHit(first, 0f, 0.5f).FlatDamage, "검 적중에 추가 피해가 실린다");
            var hit = state.ConfirmHit(first, 1, 0f, 0f, 0.5f);
            Near(Number("token-toss", "stagger"), hit.StaggerSeconds, "경직도 붙는다");

            var second = state.BeginAttack(0.5f, WeaponTag.Sword);
            Near(0f, state.PreviewHit(second, 0f, 0.5f).FlatDamage, "재사용 대기 중에는 없다");
            Near(0f, state.ConfirmHit(second, 1, 0.5f, 0f, 0.5f).StaggerSeconds);

            var bow = state.BeginAttack(cooldown + 0.1f, WeaponTag.Bow);
            Near(0f, state.PreviewHit(bow, 0f, 0.5f).FlatDamage, "원거리에는 없다");
        }

        [Test]
        public void TableLimit_CapsEveryHitWithoutCooldown()
        {
            var state = State("table-limit");
            float cap = Number("table-limit", "cap");

            state.Tick(0f, 0f, true, false);
            Near(cap, state.BeforeDamage(0f, cap * 3f, 1000f, 1000f, true), "첫 타격을 자른다");
            Near(cap, state.BeforeDamage(0.1f, cap * 3f, 1000f, 1000f, true), "바로 다음 타격도 자른다");
            // 사용자 확정 2026-09-18 — 모든 피해의 상한이다. 축복 폭발·투척물도, 독·자기장 같은 고정 피해도 자른다.
            Near(cap, state.BeforeDamage(0.2f, cap * 3f, 1000f, 1000f, false), "적 주무기가 아닌 피해도 자른다");
            Near(cap, state.BeforeDamage(0.3f, cap * 3f, 1000f, 1000f, false, float.PositiveInfinity,
                fixedDamage: true, firesOnDamaged: false), "고정 피해도 자른다");
            Near(cap * 0.5f, state.BeforeDamage(0.4f, cap * 0.5f, 1000f, 1000f, true), "상한보다 작은 피해는 그대로다");
            Assert.That(state.TableLimitReady, Is.True, "늘 켜져 있다");
        }

        // ── 코너 포켓 · 칩 슬라이드 ──────────────────────────────────────────

        [TestCase(1234)]
        [TestCase(-1234)]
        public void CornerPocket_AcceptsSignedRuntimeWallIdentity(int wallId)
        {
            var state = State("corner-pocket");
            state.Tick(0f, 0f, true, false);
            Assert.That(state.TryWallTurn(0f, wallId, .5f, 90f), Is.True);
            Assert.That(state.TryWallTurn(10f, wallId, .5f, 90f), Is.False);
            Assert.That(state.TryWallTurn(10f, -1, 3f, 0f), Is.False);
            Assert.That(state.TryWallTurn(10f, wallId, .5f, 90f), Is.True);
        }

        [Test]
        public void CornerPocket_NeedsSharpTurnNearWall()
        {
            var state = State("corner-pocket");
            float degrees = Number("corner-pocket", "turn_degrees");
            float distance = Number("corner-pocket", "wall_distance");

            state.Tick(0f, 0f, true, false);

            Assert.That(state.TryWallTurn(0f, 1, distance * 0.5f, degrees - 1f), Is.False, "덜 꺾으면 안 된다");
            Assert.That(state.TryWallTurn(0f, 1, distance * 1.5f, degrees + 10f), Is.False, "멀면 안 된다");
            Assert.That(state.TryWallTurn(0f, 1, float.NaN, degrees + 10f), Is.False, "이상한 값은 거른다");

            Assert.That(state.TryWallTurn(0f, 1, distance * 0.5f, degrees + 10f), Is.True, "가까이서 크게 꺾으면 발동한다");
            Near(Number("corner-pocket", "move_bonus"), state.MoveBonus, "이동 보너스가 붙는다");
            Near(
                Number("corner-pocket", "next_attack_bonus"),
                state.BeginAttack(0f, WeaponTag.Sword).DamageBonus,
                "다음 공격이 세진다");
            Near(0f, state.BeginAttack(0.1f, WeaponTag.Sword).DamageBonus, "다음 공격 하나뿐이다");
        }

        [Test]
        public void CornerPocket_SameWallNeedsToBeLeftBeforeReuse()
        {
            var state = State("corner-pocket");
            float degrees = Number("corner-pocket", "turn_degrees") + 10f;
            float near = Number("corner-pocket", "wall_distance") * 0.5f;
            float cooldown = Number("corner-pocket", "cooldown");
            float reset = Number("corner-pocket", "wall_reset_distance");

            state.Tick(0f, 0f, true, false);
            Assert.That(state.TryWallTurn(0f, 1, near, degrees), Is.True);
            Assert.That(state.TryWallTurn(cooldown * 0.5f, 2, near, degrees), Is.False, "재사용 대기가 먼저다");
            Assert.That(state.TryWallTurn(cooldown + 1f, 1, near, degrees), Is.False, "같은 벽은 한 번 떨어져야 한다");
            Assert.That(state.TryWallTurn(cooldown + 1f, 2, near, degrees), Is.True, "다른 벽은 바로 쓸 수 있다");

            float away = cooldown * 2f + 2f;
            Assert.That(state.TryWallTurn(away, -1, reset + 1f, 0f), Is.False, "멀어졌다는 보고는 발동이 아니다");
            Assert.That(state.TryWallTurn(away, 2, near, degrees), Is.True, "떨어졌던 벽은 다시 쓸 수 있다");
        }

        [Test]
        public void ChipSlide_EveryDashAsksForTrailWithoutCooldown()
        {
            // 엑셀 개편 — 재사용 대기가 없어졌다. 동시 개수 제한(max_trails)은 흔적을 가진 어댑터가 센다(S-GD-TMPBEF).
            var state = State("chip-slide");

            Assert.That(state.OnDash(0f, WeaponTag.Sword), Is.True, "대시가 흔적을 남긴다");
            Assert.That(state.OnDash(0.1f, WeaponTag.Sword), Is.True, "바로 다시 대시해도 흔적을 요청한다");
            Assert.That(state.ChipSlideReady, Is.True, "재사용 대기로 꺼지지 않는다");
            Assert.That(state.ChipSlideMaxTrails, Is.EqualTo((int)Number("chip-slide", "max_trails")));
            Assert.That(State().OnDash(0f, WeaponTag.Sword), Is.False, "축복이 없으면 흔적도 없다");
        }

        // ── 경직 · 시계 · 구성 변경 ──────────────────────────────────────────

        [Test]
        public void Stagger_BlocksAttacksAndPinsMovement()
        {
            var state = State("blade-tip");

            state.ApplyStagger(1f, 0.5f);
            Near(1.5f, state.AttackBlockedUntil, "경직 동안은 공격할 수 없다");
            Near(0f, state.MovePenaltyMultiplier, "이동 배수가 0 이 된다");

            state.ApplyStagger(1f, float.NaN);
            state.ApplyStagger(1f, -1f);
            Near(1.5f, state.AttackBlockedUntil, "이상한 값은 아무것도 바꾸지 않는다");

            state.Tick(1.6f, 1.6f, true, false);
            Near(1f, state.MovePenaltyMultiplier, "끝나면 풀린다");
        }

        [Test]
        public void Clock_NeverRunsBackwards()
        {
            var state = State("blade-tip");

            state.ApplyStagger(10f, 1f);
            state.Tick(5f, 5f, true, false);

            Near(0f, state.MovePenaltyMultiplier, "과거 시각으로 시계를 되돌릴 수 없다");
            Near(11f, state.AttackBlockedUntil);

            state.ApplyStagger(5f, 0.5f);
            Near(11f, state.AttackBlockedUntil, "뒤로 간 시각의 경직은 현재 시각으로 본다");
        }

        [Test]
        public void BeforeDamage_RejectsNonFiniteAndNegative()
        {
            var state = State("safe-asset", "last-chip");
            float idle = Number("safe-asset", "idle_seconds");

            state.Tick(idle, idle, true, false);

            Near(0f, state.BeforeDamage(idle, float.NaN, 100f, 100f, true), "NaN 은 피해가 아니다");
            Near(0f, state.BeforeDamage(idle, float.PositiveInfinity, 100f, 100f, true), "무한도 아니다");
            Near(0f, state.BeforeDamage(idle, -5f, 100f, 100f, true), "음수 피해는 회복이 아니다");
            Near(0f, state.Shield, "아무 상태도 바뀌지 않는다");

            // 마지막 칩이 소모되지 않았는지 확인한다.
            Near(
                30f - Number("last-chip", "remaining_hp"),
                state.BeforeDamage(idle, 500f, 30f, 100f, true),
                "거른 호출은 라운드당 한 번짜리를 쓰지 않는다");
        }

        [Test]
        public void ReplaceLoadout_KeepsStateWhenLoadoutIsIdentical()
        {
            var state = State("safe-asset");
            float idle = Number("safe-asset", "idle_seconds");

            state.Tick(idle, idle, true, false);
            state.ReplaceLoadout(Definitions("safe-asset"));

            Near(
                -Number("safe-asset", "damage_reduction"),
                state.IncomingDamageTakenBonus(idle, true),
                "같은 구성이면 충전이 그대로다");
        }

        [Test]
        public void ReplaceLoadout_StartsNewEffectsFromNow()
        {
            var state = State("counter");
            float idle = Number("safe-asset", "idle_seconds");

            state.Tick(idle * 2f, idle * 2f, true, false);
            state.ReplaceLoadout(Definitions("counter", "safe-asset"));

            Near(0f, state.IncomingDamageTakenBonus(idle * 2f, true), "새로 얻은 축복은 완충 상태로 오지 않는다");

            state.Tick(idle * 3f, idle * 3f, true, false);
            Near(
                -Number("safe-asset", "damage_reduction"),
                state.IncomingDamageTakenBonus(idle * 3f, true),
                "지금부터 충전한다");
        }

        [Test]
        public void ReplaceLoadout_ClearsRemovedEffects()
        {
            var state = State("safe-asset", "counter");
            float idle = Number("safe-asset", "idle_seconds");

            state.Tick(idle, idle, true, false);
            state.BeginAttack(idle, WeaponTag.Sword);
            Near(Number("safe-asset", "shield"), state.Shield);

            state.ReplaceLoadout(Definitions("counter"));
            Near(0f, state.Shield, "사라진 축복이 남긴 보호막도 같이 사라진다");
        }

        [Test]
        public void ResetRound_ClearsTimedStateAndInvalidatesInFlightAttacks()
        {
            var state = State("safe-asset", "overheat");
            float idle = Number("safe-asset", "idle_seconds");

            state.Tick(idle, idle, true, false);
            var attack = state.BeginAttack(idle, WeaponTag.Bow);
            state.ApplyStagger(idle, 5f);
            Near(Number("safe-asset", "shield"), state.Shield);

            state.ResetRound(0f);

            Near(0f, state.Shield, "보호막이 사라진다");
            Near(0f, state.AttackBlockedUntil, "경직도 사라진다");
            Near(1f, state.MovePenaltyMultiplier);

            var late = state.ConfirmHit(attack, 1, 1f, 1f, 0f);
            Near(0f, late.ExtraDamage, "지난 라운드의 화살은 무시된다");
            Near(0f, state.AttackSpeedBonus, "중첩도 오르지 않는다");
        }

        [Test]
        public void ActiveEffectSummary_ProjectsFlagsForUi()
        {
            var state = State("safe-asset");
            float idle = Number("safe-asset", "idle_seconds");

            Assert.That(state.ActiveEffectSummary.Length == 0, Is.True, "아무것도 켜져 있지 않으면 비어 있다");

            state.Tick(idle, idle, true, false);
            Assert.That(state.ActiveEffectSummary.Contains("safe-asset"), Is.True, "충전 상태가 보인다");
        }

        [Test]
        public void NewSwordEffectsAreCarriedIntoTheAttackAndDamageBoundaries()
        {
            var state = State("hilt-guard", "cross-cut", "margin-call");
            float reduction = Number("hilt-guard", "damage_reduction");
            float heal = Number("margin-call", "heal");

            var attack = state.BeginAttack(1f, WeaponTag.Sword);
            Assert.That(attack.CrossCutReady, Is.True, "십자 베기는 다음 검 공격 스냅숏에 실린다");
            Near(Number("cross-cut", "damage"), attack.CrossCutDamage);
            Near(-reduction, state.IncomingDamageTakenBonus(1f, true),
                "손잡이 방어는 실제 주무기 피격의 받는 피해 합산에 더해진다");
            Near(heal, state.NotifyKill(2f), "마진 콜은 실제 처치 결과에서 회복량을 반환한다");
        }

        [Test]
        public void CoverFire_ReducesIncomingDamageAndMovementAfterRangedHit()
        {
            var state = State("cover-fire");
            float now = 1f;
            float duration = Number("cover-fire", "duration");
            float damageReduction = Number("cover-fire", "damage_reduction");
            float movePenalty = Number("cover-fire", "move_penalty");

            var attack = state.BeginAttack(now, WeaponTag.Bow);
            state.ConfirmHit(attack, 42, now, 5f, 1f);

            Near(-damageReduction,
                state.IncomingDamageTakenBonus(now, true),
                "원거리 명중 뒤 받는 피해 감소가 공통 피해 경계에 적용된다");
            Near(1f - movePenalty, state.MovePenaltyMultiplier,
                "원거리 명중 뒤 이동 패널티도 함께 적용된다");

            state.Tick(now + duration, now + duration, true, false);
            Near(0f, state.IncomingDamageTakenBonus(now + duration, true),
                "엄호 사격 지속시간이 끝나면 피해 감소가 사라진다");
        }

        [Test]
        public void IncomingReductions_AddUpInsteadOfMultiplying()
        {
            // 하우스 엣지(상시)와 손잡이 방어(검을 휘두른 뒤)를 함께 들면 감소율을 더한다 — 곱하지 않는다(S-GD-SH74SC).
            var state = State("house-edge", "hilt-guard");
            float house = Number("house-edge", "damage_reduction");
            float hilt = Number("hilt-guard", "damage_reduction");

            state.BeginAttack(1f, WeaponTag.Sword);

            Near(-(house + hilt), state.IncomingDamageTakenBonus(1f, true), "두 감소율의 합");
            Near(-house, state.IncomingDamageTakenBonus(1f, false), "손잡이 방어는 적 주무기에만");
        }

        [Test]
        public void GuardRail_TheTriggeringHitIsAlsoReduced_AndOnlyBeforeDamageArmsIt()
        {
            var state = State("guard-rail");
            float reduction = Number("guard-rail", "damage_reduction");
            float cooldown = Number("guard-rail", "cooldown");
            float duration = Number("guard-rail", "duration");

            Near(-reduction, state.IncomingDamageTakenBonus(1f, true), "켜는 한 대도 줄어든다");
            Assert.That(state.GuardRailReady, Is.True, "묻는 것만으로 켜지지 않는다");

            state.BeforeDamage(1f, 10f, 100f, 100f, true);
            Assert.That(state.GuardRailReady, Is.False, "맞으면 켜진다");
            Near(-reduction, state.IncomingDamageTakenBonus(1f + duration * 0.5f, true), "창 안에서는 줄어든다");

            float between = 1f + duration + 0.01f;
            if (between < 1f + cooldown)
            {
                Near(0f, state.IncomingDamageTakenBonus(between, true), "창이 끝나고 재사용 대기 중이면 없다");
            }
        }

        [Test]
        public void BackAlley_UsesConfiguredRearHalfAngle()
        {
            var state = State("back-alley");
            var attack = state.BeginAttack(1f, WeaponTag.Sword);
            float halfAngle = Number("back-alley", "half_angle");
            float bonus = Number("back-alley", "damage_bonus");

            Near(bonus,
                state.PreviewHit(attack, 1, 0f, 0f, targetBehind: true, targetRearAngleDegrees: halfAngle - .01f)
                    .DamageBonus,
                "설정된 후방 반각 안에서는 뒷골목 거래가 발동한다");
            Near(0f,
                state.PreviewHit(attack, 1, 0f, 0f, targetBehind: true, targetRearAngleDegrees: halfAngle + .01f)
                    .DamageBonus,
                "후방 반각 밖에서는 뒷골목 거래가 발동하지 않는다");
        }

        [Test]
        public void CloseQuarters_UsesIncomingAttackerDistanceForDefense()
        {
            var state = State("close-quarters");
            state.SetCurrentAttackRange(2f);
            state.NotifyTargetDistance(1f); // 공격자가 아닌 피격자의 직전 공격 거리

            Near(100f - Number("close-quarters", "defense_bonus"),
                state.BeforeDamage(1f, 100f, 100f, 100f, true, 1.5f),
                "실제 공격자가 가까우면 방어 보너스가 적용된다");
            Near(100f,
                state.BeforeDamage(1f, 100f, 100f, 100f, true, 2.1f),
                "직전 공격이 가까워도 들어온 공격자가 멀면 적용하지 않는다");
        }

        [Test]
        public void PiercingBetCarriesTargetLimitAndPostFirstHitMultiplier()
        {
            var state = State("piercing-bet");
            var attack = state.BeginAttack(1f, WeaponTag.Bow);

            Assert.That(attack.PierceTargets, Is.EqualTo((int)Number("piercing-bet", "pierce_targets")));
            Near(Number("piercing-bet", "damage_multiplier"), attack.PierceDamageMultiplier);
            Assert.That(state.ConfirmHit(attack, 10, 1f, 1f, .2f, false, 20f).ExtraDamage, Is.Zero);
            // 같은 공격의 같은 대상은 한 번만 기록되며, 두 번째 대상도 관통 상한 안에서만 인정된다.
            Assert.That(state.ConfirmHit(attack, 11, 1f, 1f, .2f, false, 20f).ExtraDamage, Is.Zero);
            Assert.That(state.ConfirmHit(attack, 12, 1f, 1f, .2f, false, 20f).ExtraDamage, Is.Zero);
        }

        [Test]
        public void SecondaryDamageUsesActualAppliedHealthLoss()
        {
            var state = State("split-pot", "ricochet");
            var attack = state.BeginAttack(1f, WeaponTag.Bow);
            float share = Number("split-pot", "share");
            float ricochetMultiplier = Number("ricochet", "damage_multiplier");

            var result = state.ConfirmHit(
                attack, 10, 1f, 0f, 0f, false, baseDamage: 100f, appliedDamage: 12f);

            Near(12f * share, result.SplashDamage,
                "팟 분배는 방어 적용 전 기본 피해가 아니라 실제 체력 손실을 나눈다");
            Near(12f * ricochetMultiplier, result.RicochetDamage,
                "도탄도 실제 체력 손실을 기준으로 계산한다");
        }

        // ── 도구 ─────────────────────────────────────────────────────────────

        private static void Near(float expected, float actual, string message = null)
        {
            Assert.That(actual, Is.EqualTo(expected).Within(Tolerance), message);
        }

        private static BlessingMvpDefinition Entry(string effect)
        {
            var all = BlessingMvpCatalog.All;

            for (int i = 0; i < all.Count; i++)
            {
                if (string.Equals(all[i].Effect, effect, StringComparison.Ordinal))
                {
                    return all[i];
                }
            }

            throw new ArgumentException($"MVP 표에 효과 '{effect}' 가 없습니다.", nameof(effect));
        }

        private static float Number(string effect, string key) => Entry(effect).Number(key);

        private static List<BlessingDefinition> Definitions(params string[] effects)
        {
            var list = new List<BlessingDefinition>(effects.Length);

            for (int i = 0; i < effects.Length; i++)
            {
                list.Add(Entry(effects[i]).Definition);
            }

            return list;
        }

        private static BlessingCombatState State(params string[] effects) =>
            new BlessingCombatState(Definitions(effects));

        private static void Charge(BlessingCombatState state, float from, float group, int hits)
        {
            for (int i = 0; i < hits; i++)
            {
                state.AfterDamage(from + i * group * 1.5f, 5f, 90f, 100f, true);
            }
        }

        private static void LandSwordHits(BlessingCombatState state, float from, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float at = from + i * 0.1f;
                var attack = state.BeginAttack(at, WeaponTag.Sword);
                state.ConfirmHit(attack, 1, at, 0f, 0f);
                state.CompleteAttack(attack, at, true);
            }
        }
    }
}

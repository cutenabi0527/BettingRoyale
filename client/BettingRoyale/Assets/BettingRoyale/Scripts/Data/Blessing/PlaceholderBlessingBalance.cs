using System.Collections.Generic;
using BettingRoyale.Data.Upgrade;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>기본 스탯 정의와 CSV에서 생성한 축복 효과표의 로컬 공급자.</summary>
    public static class PlaceholderBlessingBalance
    {
        /// <summary>
        /// 피해 계산이 읽는 스탯의 정의. <see cref="DamageStatKeys"/> 가 이름으로 참조하는 것을
        /// 전부 덮는다 — 하나라도 빠지면 <see cref="StatRegistry"/> 가 던진다.
        /// </summary>
        /// <remarks>
        /// 기본값·상한은 ⚠️ 목업이다. 다만 <b>구조상 의미가 있는 둘은 임의값이 아니다</b> —
        /// <c>damage-taken-mult</c> 의 기본 1(곱연산 항등원)과
        /// <c>damage-taken-fixed</c>·<c>damage-taken-cap</c> 의 <c>+∞</c>(<see cref="StatKind.Min"/> 항등원)는
        /// 축복이 없을 때 피해가 그대로 통과하게 하는 값이다. 0 으로 두면 아무도 안 맞는다.
        /// </remarks>
        public static IReadOnlyList<StatDefinition> CreateStatDefinitions() => new[]
        {
            new StatDefinition(DamageStatKeys.AttackPower, "공격력", 0f, StatKind.Flat, min: 0f),

            // ⚠️ 여기 쌓이는 것은 상시 기여뿐이다 (S-UX-WDF6W5).
            // MVP 축복은 아래 CreateEffectRows 가 `mvp_effect` 행동 파라미터로만 내보내므로
            // 이 스탯에 한 건도 들어오지 않는다 — 조건부·쿨다운 보너스는 BlessingCombatState 가
            // 공격 스냅샷(BlessingAttack.DamageBonus)으로 따로 실어 보내고, DamagePipeline 이
            // `attacker.Get(damage-bonus) + request.DamageBonus` 로 한 번만 더한다.
            // 그래서 HUD 의 "피해 증가" 칸(CombatStatsHudView)에는 강화 세트 같은 상시 기여만 보인다.
            // 여기에 조건부 축복을 얹으면 표시가 맞는 대신 실제 피해가 두 번 오른다.
            new StatDefinition(DamageStatKeys.DamageBonus, "피해 증가", 0f, StatKind.Flat),

            // 치명타 배율 기본 2배. 상한은 두지 않는다 — 기획 §1.4 가 상한을 정하면 그때 넣는다.
            new StatDefinition(DamageStatKeys.CritDamage, "치명타 배율", 2f, StatKind.Flat),

            new StatDefinition(DamageStatKeys.ArmorPen, "방어력 관통", 0f, StatKind.Flat, min: 0f),

            // 흡혈은 비율이다. 1(100%)을 넘으면 때릴수록 체력이 늘어나므로 상한을 둔다.
            new StatDefinition(DamageStatKeys.Lifesteal, "흡혈", 0f, StatKind.Flat, min: 0f, max: 1f),

            new StatDefinition(DamageStatKeys.Defense, "방어력", 0f, StatKind.Flat, min: 0f),
            new StatDefinition(DamageStatKeys.DamageTakenMult, "받는 피해", 1f, StatKind.Multiplier, min: 0f),
            new StatDefinition(
                DamageStatKeys.DamageTakenFixed, "받는 피해 고정", float.PositiveInfinity, StatKind.Min),
            new StatDefinition(
                DamageStatKeys.DamageTakenCap, "받는 피해 상한", float.PositiveInfinity, StatKind.Min),

            // 강화가 쓰는 셋 (S-GD-A2YDDJ). 피해 계산식이 이름으로 읽지 않고 체력·이동·공격 컴포넌트가 소비한다.
            // ⚠️ 기본값만 정한다 — 등급별 수치는 강화 표에 있고 여기서 밸런스를 새로 만들지 않는다.
            // 최대 체력의 기준선은 전투원의 직렬화 값이라 0 으로 두고 밖에서 SetBase 로 주입한다.
            new StatDefinition(UpgradeStatKeys.MaxHp, "최대 체력", 0f, StatKind.Flat),

            // 배율이라 기본값은 곱연산 항등원 1 이다. 0 으로 두면 강화가 없을 때 아무도 못 움직이고 못 때린다.
            new StatDefinition(UpgradeStatKeys.MoveSpeed, "이동 속도", 1f, StatKind.Multiplier),
            new StatDefinition(UpgradeStatKeys.AttackSpeed, "공격 속도", 1f, StatKind.Multiplier),

            // 활 화살 충돌 반경 배율(S-GD-G3D2WP). 엑셀 stats 에도 같은 이름이 있다. 기본 반경은 무기 표가 갖는다.
            new StatDefinition(CombatStatKeys.ProjectileRadius, "화살 충돌 반경", 1f, StatKind.Multiplier, min: 0f),

            // 검 사거리 배율 — 엑셀 stats 의 attack-range(MULTIPLIER, 기본 반경은 무기 표). 강화 세트가 검을 들었을 때
            // +10% 씩 올린다(엑셀 upgrade_sets, S-GD-G3D2WP). 없으면 데이터 없는 대역에서 세트를 얹는 순간 던진다.
            // 기본 1 은 곱셈 항등원이고, 전투가 읽는 StatOrBaseline(attack-range, 1) 의 대체값과 같다.
            new StatDefinition(CombatStatKeys.AttackRange, "공격 사거리", 1f, StatKind.Multiplier, min: 0f),
        };

        public static IReadOnlyList<BlessingEffectRow> CreateEffectRows()
        {
            var rows = new List<BlessingEffectRow>(LegacyBlessingData.Rows);
            foreach (var entry in BlessingMvpCatalog.All)
                rows.Add(new BlessingEffectRow(entry.Definition.Id, "mvp_effect", entry.Effect));
            return rows;
        }

        public static StatRegistry CreateStatRegistry() => new(CreateStatDefinitions());

        public static BlessingEffectCatalog CreateEffectCatalog() => new(CreateEffectRows());

    }
}

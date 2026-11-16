using System;

namespace BettingRoyale.Data.Combat
{
    /// <summary>피해 계산기가 현재 적용하는 값. 무기 기준값은 호출자가 제공한다.</summary>
    public readonly struct CombatDamageStats
    {
        public readonly float AttackPower, Defense, ArmorPen, DamageBonus, DamageTakenMultiplier;

        /// <summary>
        /// 치명타가 떴을 때 <b>실제로 곱해지는 배율</b>(<c>crit-damage</c>, 기획 §2.1 3단계).
        ///
        /// <para>
        /// <b>확률이 아니다.</b> 치명타 여부는 Host 가 정해서 피해 요청에 실어 보내고(기획 §9),
        /// 이 값은 그때 곱해질 배율일 뿐이다. 표시 경로가 확률을 만들어 내거나 난수를 굴리면
        /// 피어마다 다른 숫자가 보인다 — 그래서 여기에는 <b>이미 적용되는 값</b>만 싣는다.
        /// </para>
        /// <para>
        /// 기본값 <c>0</c> 은 "스탯 시트가 없어 모른다"는 뜻이다. 봇·훈련 더미처럼 규칙이 붙지 않는
        /// 전투원이 이 경로로 온다. 화면은 0 을 배율 0배로 읽지 않고 <b>표시하지 않는다</b>.
        /// </para>
        /// </summary>
        public readonly float CritDamage;
        public readonly float CritChance;

        public CombatDamageStats(float attackPower, float defense = 0f, float armorPen = 0f,
            float damageBonus = 0f, float damageTakenMultiplier = 1f, float critDamage = 0f, float critChance = 0f)
        {
            AttackPower = attackPower; Defense = defense; ArmorPen = armorPen;
            DamageBonus = damageBonus; DamageTakenMultiplier = damageTakenMultiplier;
            CritDamage = critDamage; CritChance = critChance;
        }
    }

    public interface ICombatDamageStatsSource
    {
        CombatDamageStats CaptureDamageStats(CombatantId combatant, float weaponDamage, WeaponType weapon = WeaponType.None);
    }

    public interface ICombatStatsSource
    {
        CombatStatsSnapshot CaptureStats();
    }

    /// <summary>S-UX-3RQTP5: 전투에 적용된 수치만 전달한다. UI는 기여를 다시 합산하지 않는다.</summary>
    public readonly struct CombatStatsSnapshot : IEquatable<CombatStatsSnapshot>
    {
        public readonly WeaponType Weapon;
        public readonly float AttackPower, Defense, ArmorPen, DamageBonus, DamageTakenMultiplier;
        public readonly float AttackInterval, AttackRange, MoveSpeed, MaxHealth, DashDistance, DashRecharge;

        /// <summary>
        /// 치명타 배율. <see cref="CombatDamageStats.CritDamage"/> 를 그대로 옮긴다 —
        /// 화면이 확률을 추정하거나 기본값을 지어내지 않는다. 0 이면 "모름"이다.
        /// </summary>
        public readonly float CritDamage;
        public readonly float CritChance;
        public float AttackSpeed => AttackInterval > 0f ? 1f / AttackInterval : 0f;

        public CombatStatsSnapshot(WeaponType weapon, in CombatDamageStats damage, float attackInterval,
            float attackRange, float moveSpeed, float maxHealth, float dashDistance, float dashRecharge)
        {
            Weapon = weapon; AttackPower = damage.AttackPower; Defense = damage.Defense;
            ArmorPen = damage.ArmorPen; DamageBonus = damage.DamageBonus;
            DamageTakenMultiplier = damage.DamageTakenMultiplier; CritDamage = damage.CritDamage; CritChance = damage.CritChance;
            AttackInterval = attackInterval; AttackRange = attackRange; MoveSpeed = moveSpeed;
            MaxHealth = maxHealth; DashDistance = dashDistance; DashRecharge = dashRecharge;
        }

        public bool Equals(CombatStatsSnapshot other) =>
            CritDamage.Equals(other.CritDamage) && CritChance.Equals(other.CritChance) &&
            Weapon == other.Weapon && AttackPower.Equals(other.AttackPower) && Defense.Equals(other.Defense)
            && ArmorPen.Equals(other.ArmorPen) && DamageBonus.Equals(other.DamageBonus)
            && DamageTakenMultiplier.Equals(other.DamageTakenMultiplier) && AttackInterval.Equals(other.AttackInterval)
            && AttackRange.Equals(other.AttackRange) && MoveSpeed.Equals(other.MoveSpeed)
            && MaxHealth.Equals(other.MaxHealth) && DashDistance.Equals(other.DashDistance)
            && DashRecharge.Equals(other.DashRecharge);
    }
}

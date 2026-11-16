using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;

namespace BettingRoyale.Data.Upgrade
{
    /// <summary>
    /// 강화 밸런스 표의 코드 대역. 단계별 수치·비용·성공률·세트 효과를 엑셀 <c>upgrades</c>·<c>upgrade_levels</c>·
    /// <c>upgrade_sets</c> 시트와 같게 맞춘다 (2026-09-23 재조정, S15P21A606-304). 구조는 S-GD-A2YDDJ를 따른다.
    /// 정본은 엑셀이다 — 어긋나면 엑셀이 맞다.
    ///
    /// <para>
    /// <b>값이 코드에 있는 것은 임시다.</b> 정본은 <c>upgrades.json</c> 이고 데이터 파이프라인으로
    /// 들어온다(D-041). 그때 이 클래스는 지운다.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 무기는 계속 늘어난다.</b> 그래서 무기 하나가 <b>표 한 덩어리</b>로 끝나게 짰다 —
    /// <see cref="WeaponTables"/> 에 줄을 더하는 것이 전부이고, 다른 곳은 손대지 않는다.
    /// 코드가 무기 이름을 나열하는 곳은 그 사전 하나뿐이다.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 스탯 값은 0강 대비 누적 증가분이다.</b> 검 3강의 공격력 9 는 "2강에서 9 더" 가
    /// 아니라 "기본 공격력에서 9 더한 값" 이다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public static class PlaceholderUpgradeCatalog
    {
        private const string AttackPower = "attack-power";
        private const string AttackSpeed = "attack-speed";
        private const string MaxHp = "max-hp";
        private const string Defense = "defense";
        private const string MoveSpeed = "move-speed";
        private const string DamageBonus = "damage-bonus";
        private const string DamageTakenMult = "damage-taken-mult";
        private const string AttackRange = "attack-range";
        private const string ProjectileRadius = "projectile-radius";

        /// <summary>
        /// 무기 강화 표 — <b>무기를 추가할 때 고치는 유일한 곳</b> (기획서 §1).
        ///
        /// <code>
        /// 한 줄 = { 비용, 성공률(%), 공격력, 공격 속도 }   (스탯은 전부 0강 대비 누적)
        /// </code>
        ///
        /// <para>
        /// 새 무기는 <see cref="WeaponType"/> 에 값을 더하고 여기에 줄 하나를 더하면 끝이다.
        /// 표가 없는 무기는 <b>강화만 못 할 뿐 게임은 돈다</b> —
        /// <c>UpgradeCatalog.FindWeaponsWithoutTrack</c> 이 시작할 때 알려준다.
        /// </para>
        /// </summary>
        private static readonly Dictionary<WeaponType, float[][]> WeaponTables = new()
        {
            // 검 — 엑셀 upgrades(weapon/sword) · upgrade_levels (S15P21A606-304 재조정).
            // 방어력 관통은 엑셀 upgrades 에 없으므로 싣지 않는다.
            [WeaponType.Sword] = new[]
            {
                new[] { 100f, 100f, 3f, 0.05f },
                new[] { 150f, 100f, 6f, 0.10f },
                new[] { 200f, 90f, 9f, 0.15f },
                new[] { 300f, 75f, 12f, 0.20f },
                new[] { 500f, 60f, 16f, 0.25f },
            },

            // 활 — 엑셀 upgrades(weapon/bow) · upgrade_levels (S15P21A606-304 재조정).
            [WeaponType.Bow] = new[]
            {
                new[] { 100f, 100f, 2.6f, 0.05f },
                new[] { 150f, 100f, 5.3f, 0.10f },
                new[] { 200f, 90f, 8f, 0.15f },
                new[] { 300f, 75f, 10.6f, 0.20f },
                new[] { 500f, 60f, 14f, 0.25f },
            },

            // 둔기(Mace)는 아직 무기 자체가 구현되지 않았다 (WeaponType.Mace 주석 참고).
            // 구현되면 여기에 줄 하나만 더하면 된다.
        };

        /// <summary>
        /// 방어구 강화 표 (기획서 §2). 무기와 달리 종류 구분이 없다.
        /// 한 줄 = { 비용, 성공률(%), 최대 체력, 방어력 } — 엑셀 upgrades(armor) · upgrade_levels (S15P21A606-304).
        /// 방어력은 단계당 1.2 (S-GD-2TZT7S).
        /// </summary>
        private static readonly float[][] ArmorTable =
        {
            new[] { 50f, 100f, 12f, 1.2f },
            new[] { 75f, 100f, 24f, 2.4f },
            new[] { 100f, 90f, 36f, 3.6f },
            new[] { 175f, 75f, 50f, 4.8f },
            new[] { 300f, 60f, 65f, 6f },
        };

        private static StatContribution Add(string id, float value) =>
            new StatContribution(id, value, StatStackMode.Add);

        /// <summary>표 한 줄을 단계로 바꾼다. <c>{ 비용, 성공률, 공격력, 공속 }</c>.</summary>
        private static UpgradeTrack BuildWeaponTrack(float[][] rows)
        {
            var levels = new List<UpgradeLevel>(rows.Length);

            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];

                levels.Add(new UpgradeLevel(i + 1, (int)row[0], new[]
                {
                    Add(AttackPower, row[2]),
                    Add(AttackSpeed, row[3]),
                }, (int)row[1]));
            }

            return new UpgradeTrack(levels);
        }

        /// <summary>표 한 줄을 단계로 바꾼다. <c>{ 비용, 성공률, 최대 체력, 방어력 }</c>.</summary>
        private static UpgradeTrack BuildArmorTrack(float[][] rows)
        {
            var levels = new List<UpgradeLevel>(rows.Length);

            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];

                levels.Add(new UpgradeLevel(i + 1, (int)row[0], new[]
                {
                    Add(MaxHp, row[2]),
                    Add(Defense, row[3]),
                }, (int)row[1]));
            }

            return new UpgradeTrack(levels);
        }

        /// <summary>
        /// 세트 효과 — 엑셀 <c>upgrade_sets</c> 와 같은 순서·값 (S15P21A606-304). <b>낮은 쪽 단계로 판정하고 누적된다</b> —
        /// 5강이면 1·3·5강이 전부 붙어 이동 속도 +10%, 피해 +10%, 받는 피해 -10% 가 된다.
        /// 검 사거리·활 화살 반경은 그 무기를 들었을 때만 붙는다 (S-GD-G3D2WP).
        /// </summary>
        private static UpgradeSetBonus[] SetBonuses() => new[]
        {
            SetTier(1, 0.03f, 0.03f, -0.03f),
            SetTier(3, 0.03f, 0.03f, -0.03f),
            new UpgradeSetBonus(5,
                new[]
                {
                    Add(MoveSpeed, 0.04f),
                    Add(DamageBonus, 0.04f),
                    Add(DamageTakenMult, -0.04f),   // 증감분 표기 — 4% 감소
                    Add(AttackRange, 0.1f),
                    Add(ProjectileRadius, 0.1f),
                    Add(AttackPower, 3f),
                    Add(Defense, 2f),
                },
                new[]
                {
                    WeaponType.None, WeaponType.None, WeaponType.None,
                    WeaponType.Sword, WeaponType.Bow,
                    WeaponType.None, WeaponType.None,
                }),
        };

        /// <summary>1·3강 세트 — 공용 3종 + 검 사거리·활 화살 반경 +10%.</summary>
        private static UpgradeSetBonus SetTier(int level, float moveSpeed, float damageBonus, float damageTaken) =>
            new UpgradeSetBonus(level,
                new[]
                {
                    Add(MoveSpeed, moveSpeed),
                    Add(DamageBonus, damageBonus),
                    Add(DamageTakenMult, damageTaken),
                    Add(AttackRange, 0.1f),
                    Add(ProjectileRadius, 0.1f),
                },
                new[] { WeaponType.None, WeaponType.None, WeaponType.None, WeaponType.Sword, WeaponType.Bow });

        public static UpgradeCatalog Create()
        {
            var weapons = new Dictionary<WeaponType, UpgradeTrack>(WeaponTables.Count);

            foreach (var pair in WeaponTables)
            {
                weapons[pair.Key] = BuildWeaponTrack(pair.Value);
            }

            return new UpgradeCatalog(weapons, BuildArmorTrack(ArmorTable), SetBonuses());
        }
    }
}

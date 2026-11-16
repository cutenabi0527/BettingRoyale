using System;
using BettingRoyale.Data.Upgrade;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 엑셀 <c>stats</c> 시트에 있고 전투가 직접 읽는 능력치 이름. 계산표가 이 이름으로 값을 준다.
    /// </summary>
    /// <remarks>
    /// 이동·공격 속도·최대 체력은 <see cref="UpgradeStatKeys"/>, 피해 계산이 읽는 것은 <see cref="DamageStatKeys"/> 에 있다.
    /// 여기는 그 둘에 없던 <b>공격 모양·대시·치명타 확률</b>이다.
    /// </remarks>
    public static class CombatStatKeys
    {
        public const string CritChance = "crit-chance";
        /// <summary>잭팟 확률. 터지면 피해 계산 3-B 가 <c>jackpot-multiplier</c> 를 곱한다(S-GD-PA3E9T).</summary>
        public const string JackpotChance = "jackpot-chance";
        public const string AttackRange = "attack-range";

        /// <summary>투사체 충돌 반경 배율(1 = 무기 기본 반경). 활 세트가 올린다 (S-GD-G3D2WP).</summary>
        public const string ProjectileRadius = "projectile-radius";
        public const string AttackArc = "attack-arc";
        public const string ProjectileSpeed = "projectile-speed";
        public const string DashDistance = "dash-distance";
    }

    /// <summary>
    /// 캐릭터에 넣을 최종 능력치를 <b>한 가지 식으로</b> 만든다. 제품 온라인·훈련장·축복 갱신이 전부 이 식을 쓴다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>모든 증감은 합연산이다</b>(축복 시스템 기획 §1.3 — 공격 속도 디버프만 예외로 곱한다).
    /// 상시 축복·강화·장신구는 계산표(<see cref="StatSheet"/>)가 이미 더해 두고, 조건부 축복 보너스(전투 중에 켜졌다
    /// 꺼지는 것)는 여기서 <b>같은 기본값에 더한다</b>.
    /// <code>
    /// 최종 = 기본 × (1 + 상시 증감 합 + 조건부 증감 합)
    /// </code>
    /// 조건부를 따로 곱하면 +20% 와 +30% 가 +56% 가 되어 기획과 달라진다.
    /// </para>
    /// <para>
    /// <b>기본값은 엑셀이 정한다</b>(S-GD-RTB6H2). 게임 데이터가 없으면(개발 씬·EditMode) 캐릭터·무기 설정값을 쓴다.
    /// 계산표의 대역 정의는 이동 속도를 배율(기본 1)로 적어 두었으므로 <b>계산표 기본값을 그대로 읽으면 안 된다</b> —
    /// 그러면 개발 씬에서 초당 1m 로 걷는다. 그래서 기본값을 여기서 정해 <see cref="StatSheet.Get(string, float)"/> 에 넘긴다.
    /// </para>
    /// <para>
    /// <b>새 능력치를 붙일 때</b> — 계산은 계산표가 이미 한다(엑셀 <c>stats</c> 에 한 줄이면 된다). 전투가 그 값을
    /// 어디에 쓰는지만 이 파일과 그 쓰는 곳에 한 번 잇는다. 축복마다 코드를 짜지 않는다.
    /// </para>
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </remarks>
    public static class CombatantStatFormula
    {
        // ── 기본값 ─────────────────────────────────────────────────────────

        /// <summary>이동 속도의 기본값(m/s). 엑셀 <c>stats</c> 의 <c>move-speed</c>, 없으면 캐릭터 설정값.</summary>
        public static float MoveSpeedBase(float configured) => ExcelBase(UpgradeStatKeys.MoveSpeed, configured);

        /// <summary>
        /// 엑셀이 올라와 있고 그 능력치의 기본값이 유한한 양수면 그 값, 아니면 <paramref name="fallback"/>.
        /// </summary>
        public static float ExcelBase(string statId, float fallback)
        {
            if (!BlessingBalance.IsFromGameData
                || !BlessingBalance.StatRegistry.TryGet(statId, out StatDefinition definition))
            {
                return fallback;
            }

            float value = definition.BaseValue;
            return IsFinite(value) && value > 0f ? value : fallback;
        }

        // ── 이동 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 최종 이동 속도(m/s). <paramref name="withSheet"/> 는 <see cref="MoveSpeedBase"/> 를 기본값으로 넘겨 계산표에서
        /// 읽은 값이다(<c>sheet.Get(MoveSpeed, base)</c>). 조건부 보너스는 같은 기본값에 <b>더한다</b>.
        /// </summary>
        public static float MoveSpeed(float withSheet, float baseSpeed, float conditionalBonus) =>
            Math.Max(0.01f, withSheet + baseSpeed * conditionalBonus);

        // ── 공격 모양 ──────────────────────────────────────────────────────

        /// <summary>
        /// 배율 두 개를 <b>합연산</b>으로 합친다 — <c>1 + (상시 − 1) + (조건부 − 1)</c>.
        /// 사거리 +10% 상시와 +25% 조건부는 +35% 다(곱하면 +37.5%).
        /// </summary>
        public static float CombineMultipliers(float sheetMultiplier, float conditionalMultiplier) =>
            Math.Max(0.01f, sheetMultiplier + conditionalMultiplier - 1f);

        /// <summary>
        /// 공격 좌우 반각. 역날 같은 절대 반각이 우선한다. 삼연참은 현재 반각에서 감소량을 빼고 하한을 적용한다.
        /// 감소가 없으면 상시 값, 상시 값도 없으면 -1(무기 기본)을 보존한다.
        /// </summary>
        public static float HalfAngle(float sheetHalfAngle, float conditionalOverride,
            float reduction = 0f, float minimum = 0f)
        {
            if (conditionalOverride >= 0f) return conditionalOverride;
            return reduction > 0f && sheetHalfAngle >= 0f
                ? Math.Max(minimum, sheetHalfAngle - reduction)
                : sheetHalfAngle;
        }

        /// <summary>
        /// 상시 능력치를 조건부 축복이 만든 공격 스냅샷에 합친다. 공격마다 한 번, 공격을 시작할 때 부른다.
        /// </summary>
        /// <param name="snapshot">조건부 축복이 만든 스냅샷.</param>
        /// <param name="sheetRangeMultiplier">계산표의 사거리 배율(<c>1 + Σ</c>).</param>
        /// <param name="sheetHalfAngle">계산표의 좌우 반각. 없으면 <c>-1</c>.</param>
        /// <param name="sheetProjectileMultiplier">계산표의 투사체 속도 배율.</param>
        public static BlessingAttack ApplyToAttack(
            in BlessingAttack snapshot,
            float sheetRangeMultiplier,
            float sheetHalfAngle,
            float sheetProjectileMultiplier) =>
            snapshot.WithGeometry(
                CombineMultipliers(sheetRangeMultiplier, snapshot.RangeMultiplier),
                HalfAngle(sheetHalfAngle, snapshot.HalfAngleOverride, snapshot.HalfAngleReduction, snapshot.MinimumHalfAngle),
                CombineMultipliers(sheetProjectileMultiplier, snapshot.ProjectileSpeedMultiplier));

        // ── 화살 충돌 반경 ─────────────────────────────────────────────────

        /// <summary>
        /// 무기 기본 충돌 반경(weapons.xlsx <c>CollisionRadius</c>)에 계산표의 <c>projectile-radius</c> 배율(<c>1 + Σ</c>,
        /// 활 세트가 합연산으로 올린다)을 곱한다 (S-GD-3Q9GV2). 음수·NaN 은 0 으로 본다.
        /// </summary>
        public static float ScaledCollisionRadius(float weaponRadius, float sheetMultiplier)
        {
            float radius = IsFinite(weaponRadius) && weaponRadius > 0f ? weaponRadius : 0f;
            float multiplier = IsFinite(sheetMultiplier) && sheetMultiplier > 0f ? sheetMultiplier : 0f;
            return radius * multiplier;
        }

        /// <summary>
        /// 화살의 실제 판정 반경 = <c>max(축복 반경, 기본 반경 × 배율)</c> (S-GD-3Q9GV2). 축복(고정 사격·느린 화살)이
        /// 주는 반경은 더하지 않고 큰 쪽을 쓴다. 0 이면 선(레이) 판정이다.
        /// </summary>
        public static float ProjectileCollisionRadius(float blessingRadius, float scaledWeaponRadius)
        {
            float blessing = IsFinite(blessingRadius) && blessingRadius > 0f ? blessingRadius : 0f;
            float weapon = IsFinite(scaledWeaponRadius) && scaledWeaponRadius > 0f ? scaledWeaponRadius : 0f;
            return Math.Max(blessing, weapon);
        }

        // ── 치명타 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 치명타 확률을 굴린다. 같은 <paramref name="seed"/> 면 같은 결과다 — Host 가 한 번 굴리고 결과를 복제한다.
        /// </summary>
        public static bool RollCritical(float chance, int seed)
        {
            return IsFinite(chance) && chance > 0f && CriticalSample(seed) < chance;
        }

        /// <summary>공격별 시드를 섞어 [0, 1) 표본을 만든다. 공유 상태가 없고 조회 순서에 영향받지 않는다.</summary>
        public static float CriticalSample(int seed)
        {
            unchecked
            {
                // 연속 시드의 비트가 출력 전체에 퍼지도록 두 번 섞는다(S-CLI-AGXPNX).
                // 한 번의 선형 곱셈과 xor만 쓰면 전체 빈도는 맞아도 연속 결과가 몰린다.
                uint value = (uint)seed;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return (value >> 8) / 16777216f;
            }
        }

        /// <summary>공격 하나의 치명타 판정 씨앗. 전투원·공격 순번·세대로 정해진다.</summary>
        public static int CriticalSeed(int combatantId, int sequence, int generation) =>
            unchecked(combatantId * 7919 + sequence * 397 + generation * 31 + 0x2545F491);

        /// <summary>
        /// 공격 하나의 잭팟 판정 씨앗. 치명타 씨앗과 <b>달라야 한다</b> — 같으면 두 확률이 늘 함께 터지거나 함께 빗나간다.
        /// 판정식은 <see cref="RollCritical"/> 을 그대로 쓴다.
        /// </summary>
        public static int JackpotSeed(int combatantId, int sequence, int generation) =>
            unchecked(CriticalSeed(combatantId, sequence, generation) * 1103515245 + 0x6A09E667);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

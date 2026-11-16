using System;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 피해 계산이 읽는 스탯 키 (축복 시스템 기획 §2.1).
    ///
    /// <para>
    /// <b>스탯은 문자열 키다</b>(§0). 다만 <b>파이프라인의 계산식이 이름으로 참조하는 것들</b>은
    /// 구조의 일부라 여기 모아 둔다 — 흩어 놓으면 오타가 나도 <b>그 항만 조용히 0 이 된다.</b>
    /// 이름을 바꾸면 <c>stats.json</c> 과 여기를 함께 고쳐야 하고, 안 고치면
    /// <see cref="StatRegistry"/> 가 던진다.
    /// </para>
    /// </summary>
    public static class DamageStatKeys
    {
        public const string AttackPower = "attack-power";
        public const string DamageBonus = "damage-bonus";
        public const string CritDamage = "crit-damage";
        /// <summary>3-B 단계. 잭팟이 터지면 곱한다 — 치명타와 <b>곱해진다</b>(엑셀 README).</summary>
        public const string JackpotMultiplier = "jackpot-multiplier";
        public const string ArmorPen = "armor-pen";
        public const string Lifesteal = "lifesteal";

        public const string Defense = "defense";
        public const string DamageTakenMult = "damage-taken-mult";
        public const string DamageTakenFixed = "damage-taken-fixed";
        public const string DamageTakenCap = "damage-taken-cap";
    }

    /// <summary>
    /// 한 대를 때리는 요청 (기획 §2.2).
    ///
    /// <para>
    /// <b>확률 판정은 여기 없다.</b> 치명타가 떴는지는 <b>서버가 이미 정해서</b> 넘긴다(§9) —
    /// 파이프라인이 난수를 굴리면 피어마다 다른 값이 나온다.
    /// </para>
    /// </summary>
    public readonly struct DamageRequest
    {
        private DamageRequest(DamageSource source, bool isCritical, bool isFixed, float fixedAmount,
            float damageBonus = 0f, float damagePenaltyMultiplier = 1f, float damageMultiplier = 1f,
            bool isJackpot = false, float damageTakenBonus = 0f, float attackPowerBonus = 0f, float defenseBonus = 0f)
        {
            Source = source;
            IsCritical = isCritical;
            IsFixed = isFixed;
            FixedAmount = fixedAmount;
            DamageBonus = damageBonus;
            DamagePenaltyMultiplier = damagePenaltyMultiplier;
            DamageMultiplier = damageMultiplier;
            IsJackpot = isJackpot;
            AttackPowerBonus = attackPowerBonus;
            DefenseBonus = defenseBonus;
            DamageTakenBonus = damageTakenBonus;
        }

        public readonly DamageSource Source;

        /// <summary>치명타인가. <b>서버가 굴린 결과</b>다.</summary>
        public readonly bool IsCritical;

        /// <summary>
        /// 경감을 우회하는 고정 피해인가 (§2.3). 참이면 파이프라인 1~6단계를 <b>전부 건너뛴다</b> —
        /// 방어력·받는 피해 감소·증가를 모두 무시하고, 무적만 막는다.
        /// </summary>
        public readonly bool IsFixed;

        /// <summary><see cref="IsFixed"/> 일 때 그대로 들어가는 값.</summary>
        public readonly float FixedAmount;

        /// <summary>공격자의 스탯에서 피해를 계산하는 보통의 한 대.</summary>
        public readonly float DamageBonus;
        public readonly float DamagePenaltyMultiplier;

        /// <summary>
        /// 이 한 대에만 곱하는 배율 — 잭팟 축복·분할 사격·관통 감쇠 같은 것. <b>1 단계 전체</b>(무기 공격력 + flat)에
        /// 곱한다. 무기 기본값에 미리 곱해 넘기면 축복으로 더한 공격력이 배율을 빠져나간다.
        /// </summary>
        public readonly float DamageMultiplier;

        /// <summary>잭팟이 터졌는가(3-B). <b>서버가 굴린 결과</b>다 — 치명타와 같다.</summary>
        public readonly bool IsJackpot;

        /// <summary>
        /// 1단계 공격력에 더하는 <b>시트 밖</b> 고정값 — 소모품(프로틴 쉐이크 +3, S-GD-RVVB4H §6).
        /// 시트의 flat 기여와 같은 단위다.
        /// </summary>
        public readonly float AttackPowerBonus;

        /// <summary>
        /// 5단계 방어력에 더하는 <b>시트 밖</b> 고정값 — 소모품(프로틴 쉐이크 +7). 관통은 합친 방어력에서 뺀다.
        /// </summary>
        public readonly float DefenseBonus;


        /// <summary>
        /// 4단계 받는 피해 배율의 <b>합산에 더하는</b> 시트 밖 증감분 — 소모품(철벽 물약 −25% 면 <c>-0.25</c>)과
        /// 축복의 조건부 받는 피해 감소(하우스 엣지 −10% 면 <c>-0.1</c>). 따로 곱하지 않는다 — 스탯 효과는 공격 속도
        /// 감소 패널티 말고는 전부 합연산이다(S-GD-EM82G5 · S-GD-SH74SC).
        /// </summary>
        public readonly float DamageTakenBonus;

        public static DamageRequest Attack(bool isCritical = false, float damageBonus = 0f,
            float damagePenaltyMultiplier = 1f, float damageMultiplier = 1f, bool isJackpot = false,
            float damageTakenBonus = 0f, float attackPowerBonus = 0f, float defenseBonus = 0f) =>
            new DamageRequest(DamageSource.Direct, isCritical, false, 0f, damageBonus, damagePenaltyMultiplier,
                damageMultiplier, isJackpot, damageTakenBonus, attackPowerBonus, defenseBonus);

        /// <param name="damageBonus">축복 피해에 붙는 주는 피해 증감분 — 소모품(투지 물약).</param>
        /// <param name="defenseBonus">맞는 쪽 시트 밖 방어력 — 소모품(프로틴 쉐이크).</param>
        /// <param name="damageTakenBonus">맞는 쪽 시트 밖 받는 피해 증감분 — 소모품(철벽 물약)·축복의 조건부 받는 피해 감소.</param>
        public static DamageRequest Blessing(float damageBonus = 0f, float defenseBonus = 0f,
            float damageTakenBonus = 0f) =>
            new DamageRequest(DamageSource.Blessing, false, false, 0f, damageBonus,
                defenseBonus: defenseBonus, damageTakenBonus: damageTakenBonus);

        /// <summary>
        /// 정해진 숫자를 그대로 넣는 피해 — 독화살·자기장·표식 사격(엑셀 README "고정 피해").
        /// 1~6 단계와 <b>피해 상한까지</b> 전부 건너뛴다. 소스가 DOT·ZONE 이면 피격 트리거도 돌지 않는다.
        /// </summary>
        public static DamageRequest Fixed(DamageSource source, float amount)
        {
            if (amount < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "고정 피해는 음수일 수 없습니다.");
            }

            return new DamageRequest(source, isCritical: false, isFixed: true, fixedAmount: amount);
        }
    }

    /// <summary>한 대의 결과.</summary>
    public readonly struct DamageResult
    {
        internal DamageResult(
            float amount, float lifesteal, bool wasImmune, bool wasOverridden, bool wasCapped, bool firesOnDamaged)
        {
            Amount = amount;
            Lifesteal = lifesteal;
            WasImmune = wasImmune;
            WasOverridden = wasOverridden;
            WasCapped = wasCapped;
            FiresOnDamaged = firesOnDamaged;
        }

        /// <summary>실제로 깎일 체력. <b>0 이면 막힌 것이고 그것도 정상 결과다</b>(§1.4 의도된 상성).</summary>
        public readonly float Amount;

        /// <summary>공격자가 회복할 양 (§2.1 7단계). 흡혈이 없으면 0.</summary>
        public readonly float Lifesteal;

        /// <summary>소스 면역이라 통째로 0 이 됐다.</summary>
        public readonly bool WasImmune;

        /// <summary><c>damage-taken-fixed</c> 가 값을 <b>덮어썼다</b>. 계산 결과보다 클 수도 있다.</summary>
        public readonly bool WasOverridden;

        /// <summary><c>damage-taken-cap</c> 이 위를 잘랐다.</summary>
        public readonly bool WasCapped;

        /// <summary>
        /// <c>ON_DAMAGED</c> 를 발동시켜야 하는가 (엑셀 README "트리거").
        /// <b>DOT · ZONE 만 거짓이다</b> — 도트·자기장이 발동시키면 "1초에 한 대만!" 의 무적이 계속 갱신되어
        /// <b>영구 무적</b>이 된다. 표식 사격처럼 고정 피해라도 맞은 것은 맞은 것이다.
        /// </summary>
        public readonly bool FiresOnDamaged;

        /// <summary>피해가 0 이다. 화면이 <c>block</c> 을 띄운다.</summary>
        public bool WasBlocked => Amount <= 0f;

        public override string ToString() =>
            $"피해 {Amount:0.##}" + (WasImmune ? " (면역)" : WasOverridden ? " (덮어씀)" : WasBlocked ? " (block)" : string.Empty);
    }

    /// <summary>
    /// <b>피해 계산의 유일한 자리</b> (축복 시스템 기획 §2 · 엑셀 README "피해 계산 순서" — S-GD-PA3E9T).
    ///
    /// <code>
    /// 1.   atk = attack-power                      base + Σflat (+ 소모품 고정값)
    /// 2.   dmg = atk × (1 + Σ damage-bonus)        합연산
    /// 3.   치명타면  dmg ×= crit-damage
    /// 3-B. 잭팟이면  dmg ×= jackpot-multiplier      치명타와 곱해진다. 이 한 대의 배율도 여기서 곱한다
    /// 4.   dmg ×= damage-taken-mult                받는 쪽. 감소는 음수 기여(소모품·축복 증감도 합산에 더한다)
    /// 5.   eff_def = max(0, 방어력 (+ 소모품 고정값) − 관통)
    ///      dmg −= eff_def                          고정 차감, 항상 마지막
    /// 6.   dmg = max(dmg, 0)                       0 이면 block
    ///      받는 피해 상한                           고정 피해에는 걸지 않는다
    /// 7.   회복 = dmg × lifesteal
    /// </code>
    ///
    /// <para>
    /// <b>6-B 보호막은 여기 없다</b> — 전투원의 축복 상태가 체력을 깎기 직전에 먼저 깎는다(고정 피해도 깎는다).
    /// </para>
    ///
    /// <para>
    /// <b>순서를 바꾸면 결과가 달라진다.</b> 특히 5단계의 고정 차감이 마지막이라
    /// <b>방어력이 곱연산 증폭보다 뒤에 온다</b> — 치명타 25×2.2=55 에서 방어력 30 을 빼면 25 지만,
    /// 순서를 바꾸면 (25−30) 이 0 이 되어 치명타가 통째로 사라진다.
    /// </para>
    ///
    /// <para>
    /// <b>방어력 관통의 초과분은 추가 피해가 되지 않는다</b>(§2.1). 카운터 전용이다.
    /// </para>
    ///
    /// <para>
    /// <b>여기에 수치가 없다.</b> 최소 피해 하한조차 생성자로 받는다 — 전역 상수는
    /// <c>constants.json</c> 이 준다(D-041, 기획 §0).
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class DamagePipeline
    {
        /// <summary>
        /// 엑셀이 없을 때 쓰는 최소 피해 하한 (S-GD-56NVEJ). <b>방어력이 피해보다 높아도 1 은 들어간다.</b>
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>정본은 기획 엑셀 <c>constants</c> 의 <c>MIN_DAMAGE_FLOOR</c> 다</b> —
        /// 제품 경로는 <see cref="BlessingBalance.MinDamageFloor"/> 로 그 값을 읽는다. 여기 상수는
        /// 엑셀이 없는 배치모드(CI·EditMode 테스트)의 대역이고, 엑셀과 같은 값으로 맞춰 둔다.
        /// </para>
        /// <para>
        /// <b>실행 경로 네 곳이 같은 값을 써야 한다</b>(온라인·훈련 코디네이터·Local Play·96 연습).
        /// 한 곳만 다른 숫자를 넣으면 같은 전투가 경로마다 다르게 끝난다.
        /// </para>
        /// </remarks>
        public const float DefaultMinDamageFloor = 1f;

        private readonly float minDamageFloor;

        /// <param name="minDamageFloor">
        /// <c>MIN_DAMAGE_FLOOR</c>. 제품 경로는 <see cref="DefaultMinDamageFloor"/> 를 넘긴다 —
        /// 인자로 남겨 둔 것은 <b>테스트가 하한 없이 계산식만 보기 위해서</b>다.
        /// </param>
        public DamagePipeline(float minDamageFloor)
        {
            this.minDamageFloor = minDamageFloor;
        }

        /// <summary>
        /// 한 대를 계산한다.
        /// </summary>
        /// <param name="attacker">공격자의 스탯. 고정 피해에서는 <c>null</c> 이어도 된다.</param>
        /// <param name="target">맞는 쪽의 스탯.</param>
        /// <param name="immuneSources">맞는 쪽이 면역인 소스들.</param>
        public DamageResult Resolve(
            StatSheet attacker,
            StatSheet target,
            in DamageRequest request,
            DamageSourceSet immuneSources = default)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            // 피격 트리거는 소스만 보고 정해진다 — 피해가 0 이어도 맞은 것은 맞은 것이다. DOT·ZONE 만 빠진다.
            bool firesOnDamaged = request.Source != DamageSource.Dot && request.Source != DamageSource.Zone;

            // ① 소스 면역 — 계산할 것도 없이 0 이다 (§2.5 적용 순서 1).
            if (immuneSources.Contains(request.Source))
            {
                return new DamageResult(
                    0f, 0f, wasImmune: true, wasOverridden: false, wasCapped: false, firesOnDamaged);
            }

            // ② 받는 피해 고정 — 값을 **덮어쓴다**. 상한이 아니라 덮어쓰기라
            //    자기장 틱이 20 이든 0.9 든 결과가 같다 (§2.6, 검증 13·13-B).
            float fixedTaken = target.Get(DamageStatKeys.DamageTakenFixed);

            if (!float.IsPositiveInfinity(fixedTaken))
            {
                return new DamageResult(
                    Math.Max(fixedTaken, minDamageFloor),
                    0f,
                    wasImmune: false,
                    wasOverridden: true,
                    wasCapped: false,
                    firesOnDamaged);
            }

            float damage = request.IsFixed
                ? request.FixedAmount                       // ③ 고정 피해는 1~6단계를 건너뛴다 (§2.3)
                : ComputeAttackDamage(attacker, target, request);

            // 6단계 — 최소 피해 하한 (S-GD-56NVEJ). **면역보다 뒤다** — 앞에 두면 9개의 목숨이 독에 1 씩 맞아
            //          "독 면역" 이 무너진다. 무적은 애초에 여기 오지 않는다(명중 자체가 성립하지 않는다).
            damage = Math.Max(damage, minDamageFloor);

            // ④ 받는 피해 상한 — 위만 막는다 (§2.5 적용 순서 3). **고정 피해에는 걸지 않는다** —
            //    받는 피해 감소·방어력·피해 상한 모두 고정 피해를 줄이지 못한다(엑셀 README).
            float cap = target.Get(DamageStatKeys.DamageTakenCap);
            bool wasCapped = false;

            if (!request.IsFixed && !float.IsPositiveInfinity(cap) && damage > cap)
            {
                damage = Math.Max(cap, minDamageFloor);
                wasCapped = true;
            }

            // 7단계 — 회복은 **최종 피해** 기준이다. 막힌 대(0)에서는 흡혈도 0 이다.
            float lifesteal = request.Source == DamageSource.Direct && attacker != null
                ? damage * attacker.Get(DamageStatKeys.Lifesteal) : 0f;

            return new DamageResult(
                damage, lifesteal, wasImmune: false, wasOverridden: false, wasCapped, firesOnDamaged);
        }

        /// <summary>§2.1 의 1~5단계. 고정 피해는 여기 오지 않는다.</summary>
        private static float ComputeAttackDamage(StatSheet attacker, StatSheet target, in DamageRequest request)
        {
            if (attacker == null)
            {
                throw new ArgumentNullException(
                    nameof(attacker), "고정 피해가 아니면 공격자의 스탯이 있어야 공격력을 읽습니다.");
            }

            // 1. 공격력 (무기 기본값 + flat 기여 + 시트 밖 소모품 고정값)
            float damage = attacker.Get(DamageStatKeys.AttackPower) + request.AttackPowerBonus;

            // 2. 피해 증가 — 합연산
            damage *= (1f + attacker.Get(DamageStatKeys.DamageBonus) + request.DamageBonus)
                * request.DamagePenaltyMultiplier;

            // 3. 치명타
            if (request.IsCritical)
            {
                damage *= attacker.Get(DamageStatKeys.CritDamage);
            }

            // 3-B. 잭팟 — 치명타와 곱해진다. 이 한 대의 배율(잭팟 축복·분할 사격 등)도 여기서 1 단계 전체에 곱한다.
            damage *= request.DamageMultiplier;
            if (request.IsJackpot && attacker.Knows(DamageStatKeys.JackpotMultiplier))
            {
                damage *= attacker.Get(DamageStatKeys.JackpotMultiplier);
            }

            // 4. 받는 피해 배율 — 맞는 쪽 스탯. 감소는 음수 기여로 들어온다.
            //    시트 밖 증감(소모품 철벽·축복의 조건부 받는 피해 감소)도 같은 합산에 **더한다** — 기본 × (1 + Σ + 증감) × Π
            //    (S-GD-EM82G5 · S-GD-SH74SC). 시트 값은 이미 (1 + Σ) × Π 이므로 증감에도 Π 를 곱해 더하면 같은 식이 된다.
            float takenMultiplier = target.Get(DamageStatKeys.DamageTakenMult)
                + request.DamageTakenBonus * target.PenaltyOf(DamageStatKeys.DamageTakenMult);
            damage *= Math.Max(0f, takenMultiplier);

            // 5. 방어력 − 관통. **초과분은 추가 피해가 되지 않는다** — 그래서 max(0, ...) 이다.
            //    관통은 임시 비활성화 정책(BlessingBalance.ArmorPenetrationEnabled)이 꺼져 있으면 0 이다 (S-GD-3DEQ2M).
            float armorPen = BlessingBalance.ArmorPenetrationEnabled ? attacker.Get(DamageStatKeys.ArmorPen) : 0f;
            float effectiveDefense = Math.Max(
                0f, target.Get(DamageStatKeys.Defense) + request.DefenseBonus - armorPen);

            return damage - effectiveDefense;
        }
    }
}

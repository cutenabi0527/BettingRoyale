using System;
using System.Collections.Generic;
using BettingRoyale.Data.Combat;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 전투원 하나를 경제 플레이어로 옮긴다.
    ///
    /// <para>
    /// <b>모든 전투원이 플레이어인 것은 아니다.</b> 봇·훈련 더미·샌드박스 NPC 는 경제에 없고,
    /// 실제 매핑(오프셋 10,000)은 <c>Scripts/Network/</c> 가 안다. 그래서 도메인은 이 구멍으로만
    /// 묻는다 — 여기에 오프셋 숫자를 적으면 네트워크 규칙이 도메인으로 새어 들어온다.
    /// </para>
    /// </summary>
    public interface ICombatantPlayerMap
    {
        /// <summary>이 전투원의 경제 플레이어 id. 플레이어가 아니면 <c>false</c>.</summary>
        bool TryGetPlayerId(CombatantId combatant, out int playerId);
    }

    /// <summary>
    /// <b>축복을 실제 피해에 먹이는 어댑터</b> (축복 시스템 기획 §2·§3, D-069).
    ///
    /// <para>
    /// 전투가 체력을 깎기 직전에 이걸 부르고, 여기서 <see cref="DamagePipeline"/> 을 돌려
    /// 최종 숫자를 돌려준다. <b>전투 코드는 규칙을 하나도 모른다.</b>
    /// </para>
    ///
    /// <para>
    /// <b>모르는 대상은 neutral target 시트로 계산한다.</b> 봇·훈련 더미는 경제 스탯이 없지만,
    /// 알려진 공격자의 축복·무기 보너스와 neutral target 경계를 함께 통과해야 준비 라운드의
    /// 측정값이 전투 라운드와 일치한다. 해석기가 없는 호출만 <c>CombatantHealth</c> 의 원본값
    /// fallback을 사용하며, 모르는 대상이라고 계산기를 우회하지 않는다.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 자해는 건너뛴다.</b> 디버그 빈사 토글(G키)이 <c>공격자 == 대상</c> 으로
    /// "현재 체력만큼" 피해를 넣는데, 방어력을 빼면 <b>안 눕는다.</b> 그 경로는 규칙 밖이다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class BlessingCombatResolver : ICombatDamageResolver, ICombatDamageStatsSource
    {
        private readonly DamagePipeline pipeline;
        private readonly ICombatantPlayerMap playerMap;

        /// <summary>
        /// 축복이 하나도 없는 공격자용 시트.
        ///
        /// <para>
        /// <b>공격자를 모른다고 해서 맞는 쪽의 방어력까지 무시하면 안 된다.</b> 그래서 빈 시트를
        /// 하나 두고 무기 피해량만 얹어 쓴다 — 봇이 때려도 방어력 축복이 정상으로 먹는다.
        /// </para>
        ///
        /// <para>Host 에서만, 한 번에 하나씩 불리므로 재사용해도 안전하다.</para>
        /// </summary>
        private readonly StatSheet neutralAttacker;
        private readonly StatSheet neutralTarget;

        private readonly Dictionary<int, StatSheet> sheetsByPlayer = new Dictionary<int, StatSheet>();
        private readonly Dictionary<int, DamageSourceSet> immuneByPlayer = new Dictionary<int, DamageSourceSet>();

        public BlessingCombatResolver(
            DamagePipeline pipeline, ICombatantPlayerMap playerMap, StatRegistry registry)
        {
            this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            this.playerMap = playerMap ?? throw new ArgumentNullException(nameof(playerMap));

            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            neutralAttacker = new StatSheet(registry);
            neutralTarget = new StatSheet(registry);
        }

        /// <summary>
        /// 이 플레이어의 스탯 시트를 등록한다. 준비 단계가 끝날 때 축복을 얹어 만든 것을 넣는다.
        /// </summary>
        public void SetSheet(int playerId, StatSheet sheet, DamageSourceSet immuneSources = default)
        {
            if (sheet == null)
            {
                throw new ArgumentNullException(nameof(sheet));
            }

            sheetsByPlayer[playerId] = sheet;
            immuneByPlayer[playerId] = immuneSources;
        }

        /// <summary>매치가 끝났다. 다음 매치에 지난 축복이 새지 않게 비운다 (S-GD-RFXF8X).</summary>
        private readonly Dictionary<int, BlessingCombatState> combatStates = new Dictionary<int, BlessingCombatState>();
        private readonly Dictionary<int, IReadOnlyList<BlessingDefinition>> loadouts = new Dictionary<int, IReadOnlyList<BlessingDefinition>>();

        public void SetBlessings(int playerId, IReadOnlyList<BlessingDefinition> acquired)
        {
            var copy = acquired == null ? Array.Empty<BlessingDefinition>() : new List<BlessingDefinition>(acquired).ToArray();
            loadouts[playerId] = copy;
            if (combatStates.TryGetValue(playerId, out var state)) state.ReplaceLoadout(copy);
            else combatStates[playerId] = new BlessingCombatState(copy);
        }

        public BlessingCombatState CombatStateFor(CombatantId id) =>
            playerMap.TryGetPlayerId(id, out int player) && combatStates.TryGetValue(player, out var state) ? state : null;

        public float StatFor(CombatantId id, string key, float baseline) =>
            playerMap.TryGetPlayerId(id, out int player) && sheetsByPlayer.TryGetValue(player, out var sheet)
                ? sheet.Get(key, baseline) : baseline;

        /// <summary>
        /// <see cref="StatFor"/> 와 같되, 시트가 그 능력치를 모르면 던지지 않고 <paramref name="baseline"/> 을 준다.
        /// 엑셀에만 있는 능력치(치명타 확률·사거리·공격 폭·투사체 속도·대시 거리)를 읽을 때 쓴다 —
        /// 엑셀 없이 도는 개발 씬·EditMode 의 대역 계산표에는 그 능력치가 없다.
        /// </summary>
        public float StatOrBaseline(CombatantId id, string key, float baseline) =>
            playerMap.TryGetPlayerId(id, out int player) && sheetsByPlayer.TryGetValue(player, out var sheet)
                && sheet.Knows(key)
                ? sheet.Get(key, baseline) : baseline;

        private readonly List<StatContribution> timedBuffer = new List<StatContribution>();

        /// <summary>
        /// 켜졌다 꺼지는 축복 효과의 능력치 기여를 이 전투원의 계산표에 맞춘다 — 이전 것을 걷고 지금 것으로 바꾼다.
        /// 계산표가 모르는 능력치(엑셀 없는 대역 계산표)는 건너뛴다. 계산표가 없으면 아무 일도 없다.
        /// </summary>
        public void SyncTimedStats(CombatantId id, IReadOnlyList<StatContribution> contributions)
        {
            if (!playerMap.TryGetPlayerId(id, out int player) || !sheetsByPlayer.TryGetValue(player, out var sheet)) return;
            timedBuffer.Clear();
            if (contributions != null)
                for (int i = 0; i < contributions.Count; i++)
                    if (sheet.Knows(contributions[i].StatId)) timedBuffer.Add(contributions[i]);
            sheet.ReplaceFrom(BlessingCombatState.TimedStatSource, timedBuffer);
        }

        public bool HasFamily(CombatantId id, string family)
        {
            if (!playerMap.TryGetPlayerId(id, out int player) || !loadouts.TryGetValue(player, out var items)) return false;
            foreach (var item in items) if (item != null && item.Id.StartsWith(family + "-t", StringComparison.Ordinal)) return true;
            return false;
        }

        public int PrecisionEveryShotsFor(CombatantId id)
        {
            int every = int.MaxValue;
            if (!playerMap.TryGetPlayerId(id, out int player) || !loadouts.TryGetValue(player, out var items)) return every;
            foreach (var item in items)
                foreach (var row in LegacyBlessingData.Rows)
                    if (item != null && row.BlessingId == item.Id && row.ParamKey == "critical_every"
                        && row.TryGetNumber(out float number)) every = Math.Min(every, (int)number);
            return Math.Max(1, every);
        }

        public void Clear()
        {
            combatStates.Clear();
            loadouts.Clear();
            sheetsByPlayer.Clear();
            immuneByPlayer.Clear();
        }

        public bool HasSheet(int playerId) => sheetsByPlayer.ContainsKey(playerId);

        /// <summary>
        /// 그 전투원 시트의 <b>곱연산(패널티) 몫</b>. 시트가 없으면 1. 시트 밖 소모품 보너스를 합연산으로 붙일 때
        /// <c>시트 값 + 보너스 × 이 값</c> 으로 쓴다 (S-GD-EM82G5) — 공격 속도 물약이 공격 속도 패널티를 피하지 못한다.
        /// </summary>
        public float PenaltyFor(CombatantId id, string key) =>
            playerMap.TryGetPlayerId(id, out int player) && sheetsByPlayer.TryGetValue(player, out var sheet)
                ? sheet.PenaltyOf(key) : 1f;

        /// <summary>지금 규칙이 적용되는 플레이어 수. 진단용이다.</summary>
        public int TrackedPlayerCount => sheetsByPlayer.Count;

        /// <summary>실제 확률 판정과 표시가 공유하는 치명타 확률. 조회만 하며 난수/공격 상태를 바꾸지 않는다.</summary>
        public float CriticalChanceFor(CombatantId id) => StatOrBaseline(id, CombatStatKeys.CritChance,
            CombatantStatFormula.ExcelBase(CombatStatKeys.CritChance, 0f));

        public CombatDamageStats CaptureDamageStats(CombatantId combatant, float weaponDamage, WeaponType weapon = WeaponType.None)
        {
            if (!playerMap.TryGetPlayerId(combatant, out int playerId)
                || !sheetsByPlayer.TryGetValue(playerId, out var sheet)) return new CombatDamageStats(weaponDamage, critChance: CriticalChanceFor(combatant));
            // ⚠️ 치명타는 배율만 읽는다. "몇 발마다 터지는가"(critical_every)는 축복 로드아웃이 정하고
            //    파이프라인은 Host 가 정한 결과 플래그만 받는다 — 표시 경로가 확률을 만들면 안 된다.
            BlessingCombatState state = CombatStateFor(combatant);
            WeaponTag tag = weapon == WeaponType.Bow ? WeaponTag.Bow
                : weapon == WeaponType.Sword ? WeaponTag.Sword : WeaponTag.Common;
            float attackPower = sheet.Get(DamageStatKeys.AttackPower, weaponDamage);
            return new CombatDamageStats(state?.ApplyActiveAttackPower(attackPower) ?? attackPower,
                sheet.Get(DamageStatKeys.Defense) + (state?.CloseQuartersDefense ?? 0f),
                // 표시도 실제 계산과 같은 정책을 따른다 — 꺼져 있으면 0 (S-GD-3DEQ2M).
                BlessingBalance.ArmorPenetrationEnabled ? sheet.Get(DamageStatKeys.ArmorPen) : 0f,
                // 표시 스냅샷에만 합산한다. 스탯 시트에 얹으면 공격 스냅샷과 이중 적용된다.
                sheet.Get(DamageStatKeys.DamageBonus) + (state?.ActiveDamageBonus(tag) ?? 0f) + (state?.CloseQuartersDamage(tag) ?? 0f),
                Math.Max(0f, sheet.Get(DamageStatKeys.DamageTakenMult) + (state?.CurrentDamageTakenBonus ?? 0f)),
                sheet.Get(DamageStatKeys.CritDamage), CriticalChanceFor(combatant));
        }

        public float ResolveDamage(CombatantId target, in DamageInfo damageInfo)
        {
            if (float.IsNaN(damageInfo.Amount) || float.IsInfinity(damageInfo.Amount) || damageInfo.Amount <= 0f
                || float.IsNaN(damageInfo.DamageBonus) || float.IsInfinity(damageInfo.DamageBonus)
                || float.IsNaN(damageInfo.DamagePenaltyMultiplier) || float.IsInfinity(damageInfo.DamagePenaltyMultiplier)
                || damageInfo.DamagePenaltyMultiplier < 0f
                || float.IsNaN(damageInfo.DamageMultiplier) || float.IsInfinity(damageInfo.DamageMultiplier)
                || damageInfo.DamageMultiplier < 0f) return 0f;
            // ⚠️ 자해(디버그 빈사 토글)는 규칙 밖이다. 방어력을 빼면 안 눕는다.
            if (damageInfo.Attacker == target)
            {
                return damageInfo.Amount;
            }

            int targetPlayerId = -1;
            StatSheet targetSheet = null;
            if (playerMap.TryGetPlayerId(target, out targetPlayerId))
                sheetsByPlayer.TryGetValue(targetPlayerId, out targetSheet);
            targetSheet ??= neutralTarget;

            // 공격자는 없을 수 있다 — 자기장은 공격자가 default 다. 파이프라인이 그것을 허용한다.
            StatSheet attackerSheet = null;

            if (playerMap.TryGetPlayerId(damageInfo.Attacker, out int attackerPlayerId))
            {
                sheetsByPlayer.TryGetValue(attackerPlayerId, out attackerSheet);
            }

            immuneByPlayer.TryGetValue(targetPlayerId, out var immune);

            var source = Translate(damageInfo.Source);

            // 고정 피해(표식 사격 등) — 받는 피해 감소·방어력·피해 상한을 전부 건너뛴다(S-GD-PA3E9T).
            // 소스는 그대로 넘긴다: 면역 판정과 피격 트리거가 소스를 본다.
            if (damageInfo.IsFixed)
            {
                return pipeline.Resolve(
                    attackerSheet, targetSheet, DamageRequest.Fixed(source, damageInfo.Amount), immune).Amount;
            }

            // 직접 공격이 아니면 원래 숫자를 고정 피해로 흘려보낸다 — 자기장·도트가 이 경로다.
            //
            // ⚠️ **공격자를 모른다는 이유로 여기 보내면 안 된다.** 고정 피해는 방어력과 받는 피해
            //    감소를 통째로 건너뛰므로(§2.3), 봇이 때렸다고 방어력 축복이 무력해진다.
            //    "공격자가 없다" 와 "경감을 우회한다" 는 다른 얘기다.
            if (source == DamageSource.Blessing)
            {
                neutralAttacker.Clear();
                neutralAttacker.SetBase(DamageStatKeys.AttackPower, damageInfo.Amount);
                // 소모품 공격력(+3)은 싣지 않는다 — 시트 공격력(힘의 반지)도 축복 피해에는 들어가지 않는다.
                // 피해 증가(투지)와 맞는 쪽 방어력(+7)·받는 피해 증감(철벽·축복 감소)은 싣는다 (S-GD-RVVB4H §6).
                return pipeline.Resolve(neutralAttacker, targetSheet,
                    DamageRequest.Blessing(damageBonus: damageInfo.DamageBonus,
                        defenseBonus: FiniteOrZero(damageInfo.DefenseBonus),
                        damageTakenBonus: FiniteOrZero(damageInfo.DamageTakenBonus)), immune).Amount;
            }

            if (source != DamageSource.Direct)
            {
                // 고정 피해(자기장·도트)는 무적 말고는 어떤 피해 감소도 받지 않는다 — 소모품 받는 피해 감소(철벽)·방어력도
                // 싣지 않는다. 줄일 수 있는 것은 받는 피해 고정(damage-taken-fixed)뿐이다 (§2.3, S-GD-S9KNJM).
                return pipeline.Resolve(
                    attackerSheet, targetSheet, DamageRequest.Fixed(source, damageInfo.Amount), immune).Amount;
            }

            if (attackerSheet == null)
            {
                // 축복 없는 공격자. 무기 피해량만 얹어 맞는 쪽 규칙은 그대로 태운다.
                neutralAttacker.Clear();
                attackerSheet = neutralAttacker;
            }

            // ⚠️ 들어온 숫자가 곧 **이 무기의 공격력**이다(검 25 / 활 15). 그것을 기본값으로 넣지 않으면
            //    파이프라인 1단계가 무기를 무시하고 축복 기여만으로 피해를 만든다 —
            //    검이 25 가 아니라 0 이 되고, 축복이 없으면 아무도 안 죽는다.
            //    StatSheet.SetBase 가 "무기가 정하는 기본값은 밖에서 주입한다" 를 위한 자리다.
            attackerSheet.SetBase(DamageStatKeys.AttackPower, damageInfo.Amount);

            float attackPower = attackerSheet.Get(DamageStatKeys.AttackPower);
            float externalAttackPower = FiniteOrZero(damageInfo.AttackPowerBonus);
            var activeState = CombatStateFor(damageInfo.Attacker);
            float effectiveAttackPower = activeState?.ApplyActiveAttackPower(attackPower + externalAttackPower)
                ?? attackPower + externalAttackPower;
            return pipeline.Resolve(attackerSheet, targetSheet, DamageRequest.Attack(
                damageInfo.IsCritical, damageInfo.DamageBonus, damageInfo.DamagePenaltyMultiplier,
                damageInfo.DamageMultiplier, damageInfo.IsJackpot,
                damageTakenBonus: FiniteOrZero(damageInfo.DamageTakenBonus),
                attackPowerBonus: effectiveAttackPower - attackPower,
                defenseBonus: FiniteOrZero(damageInfo.DefenseBonus)), immune).Amount;
        }

        /// <summary>계산에 섞기 전에 거른다 — NaN 하나가 피해를 통째로 NaN 으로 만든다.</summary>
        private static float FiniteOrZero(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;

        /// <summary>
        /// 전투의 소스 분류를 축복 쪽 분류로 옮긴다.
        ///
        /// <para>
        /// <b>⚠️ 캐스팅하면 안 된다.</b> 두 열거형은 값이 어긋나 있다 —
        /// <c>CombatDamageSource.Zone</c> 은 1 인데 <see cref="DamageSource.Zone"/> 은 2 다.
        /// 그리고 전투 쪽에는 <c>Dot</c> 이 아직 없다. 숫자로 이으면 <b>자기장이 도트로 읽힌다.</b>
        /// </para>
        ///
        /// <para>
        /// 전투 쪽에 <c>Dot</c> 이 생기면 여기에 한 줄을 더한다. 그 전까지 독화살은
        /// <c>DamageRequest.Fixed(DamageSource.Dot, ...)</c> 로 축복 쪽에서 직접 만든다.
        /// </para>
        /// </summary>
        public static DamageSource Translate(CombatDamageSource source)
        {
            switch (source)
            {
                case CombatDamageSource.Weapon:
                    return DamageSource.Direct;

                case CombatDamageSource.Blessing:
                    return DamageSource.Blessing;

                case CombatDamageSource.Zone:
                    return DamageSource.Zone;

                case CombatDamageSource.Dot:
                    return DamageSource.Dot;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(source), source,
                        "모르는 전투 피해 소스입니다. 축복 쪽 DamageSource 로 옮길 방법을 정해야 합니다.");
            }
        }

        public override string ToString() => $"축복 피해 해석기 · 추적 {sheetsByPlayer.Count}명";
    }
}

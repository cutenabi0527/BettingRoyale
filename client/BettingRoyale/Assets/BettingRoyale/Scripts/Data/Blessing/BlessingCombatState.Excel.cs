using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 표식을 누구에게 붙이는가 (<c>mark_target</c>). 대상을 실제로 고르는 것은 어댑터 몫이고,
    /// <b>어떤 기준으로 고를지</b>를 엑셀이 정한다.
    /// </summary>
    public enum BlessingMarkSelection
    {
        /// <summary>표식을 붙이지 않는다 — 축복이 없거나, 구현하지 않은 기준을 선언했다.</summary>
        None = 0,

        /// <summary>나에게 가장 많은 피해를 준 적 (<c>TOP_DAMAGE_DEALER</c>).</summary>
        TopDamageDealer = 1,
    }

    /// <summary>
    /// <b>엑셀에만 있는 축복</b>의 전투 동작. 엑셀 <c>effect</c> 이름으로 찾고 수치는 엑셀 <c>parameters</c> 에서 읽는다
    /// (<see cref="BettingRoyale.Data.Remote.GameContent.Behaviors"/>). 새 축복은 여기 한 곳에 효과를 더하면 된다.
    /// </summary>
    /// <remarks>
    /// 트리거 규칙은 엑셀 README 를 따른다(S-GD-PA3E9T) — 피격 트리거는 DOT·ZONE 에 돌지 않고, 죽을 피해 트리거는 출처와
    /// 무관하다. 기획이 정하지 않은 세부는 S-GD-NN5PR4 에 적었다.
    /// </remarks>
    public sealed partial class BlessingCombatState
    {
        private const string EffectLightStep = "light_step";
        private const string EffectBooster = "booster";
        private const string EffectOuch = "ouch";
        private const string EffectCloseIn = "close_in";
        private const string EffectHype = "hype";
        private const string EffectHunterMark = "hunter_mark";
        private const string EffectRageDash = "rage_dash";
        private const string EffectUnderdog = "underdog";
        private const string EffectBully = "bully";
        private const string EffectEscapeDash = "escape_dash";
        private const string EffectAfterimage = "afterimage";
        private const string EffectOneHit = "one_hit";
        private const string EffectBerserk = "berserk";
        private const string EffectDraw = "draw";
        private const string EffectLastFive = "last_five";
        private const string EffectTripleShot = "triple_shot";
        private const string EffectHeadshot = "headshot";
        private const string EffectPoisonArrow = "poison_arrow";
        private const string EffectGreed = "greed";
        private const string EffectLastStand = "last_stand";
        private const string EffectPickpocket = "pickpocket";
        /// <summary>엑셀 <c>interest-burst-t1</c> — 예치하면 전투 중 주기적으로 이자를 받는다(코드 이자 폭발과 동작이 다르다).</summary>
        private const string EffectInterestIncome = "interest_income";
        private const string EffectBodyCheck = "body_check";
        private const string EffectSturdy = "sturdy";
        private const string EffectNineLives = "nine_lives";
        private const string EffectCrossbow = "crossbow";
        private const string EffectBallista = "ballista";
        /// <summary>저격수 — 같은 효과의 여러 등급을 함께 들면 피해 증가가 더해진다(S-GD-ZQMB7Y).</summary>
        private const string EffectSniper = "sniper";

        // ── 동작을 고르는 파라미터 이름 (엑셀 parameters) ────────────────────
        // 이 이름들은 예전에 **아무 코드도 읽지 않았다**. 그래서 엑셀에서 값을 바꿔도 게임이 그대로였고,
        // 아무 신호가 없어 "구현된 줄 알았다" 로 넘어갔다. 지금은 전부 읽으며, 모르는 값은
        // UnsupportedDeclarations 에 남아 QA 가 실패로 잡는다.
        private const string ParamCompareMode = "compare_mode";
        private const string ParamInvulnerableKind = "invuln_kind";
        private const string ParamMarkTarget = "mark_target";
        private const string ParamDeathOnExpire = "death_on_expire";
        private const string ParamTriggersOnInstantKill = "triggers_on_instant_kill";
        private const string ParamNoStackOnInvulnerable = "no_stack_on_invuln";
        private const string ParamIgnoresDefense = "ignores_defense";
        private const string ParamAppliesToExtraProjectiles = "applies_to_extra_projectiles";

        /// <summary>대상과 나를 무엇으로 견주는가 (<c>compare_mode</c>).</summary>
        private const string CompareHealthRatio = "HP_RATIO";
        private const string CompareBaseAttackPower = "BASE_ATTACK_POWER";

        /// <summary>무적의 종류 (<c>invuln_kind</c>). 지금 구현한 것은 이 하나다.</summary>
        private const string InvulnerableAttackable = "ATTACKABLE";

        /// <summary>표식 대상 고르는 법 (<c>mark_target</c>).</summary>
        private const string MarkTopDamageDealer = "TOP_DAMAGE_DEALER";

        private readonly List<string> unsupportedDeclarations = new List<string>();

        /// <summary>
        /// 엑셀이 선언했지만 <b>이 코드가 구현하지 않은 값</b>. 비어 있는 것이 정상이다.
        /// </summary>
        /// <remarks>
        /// <b>조용히 기본 동작으로 흘리지 않기 위한 목록이다.</b> 여기 줄이 생기면 기획이 적은 것과 게임이 하는 것이
        /// 다르다는 뜻이고, 전체 축복 QA 가 그것을 실패로 잡는다. 동작은 구현된 쪽을 유지한다 — 데이터 오타 하나로
        /// 축복이 통째로 꺼지는 편이 더 나쁘다.
        /// </remarks>
        public IReadOnlyList<string> UnsupportedDeclarations => unsupportedDeclarations;

        /// <summary>
        /// 선언된 이름을 읽는다. 없으면 <paramref name="fallback"/>, 아는 값이 아니면 미지원으로 적고 대체값을 쓴다.
        /// </summary>
        private string DeclaredMode(BlessingMvpDefinition entry, string key, string fallback, params string[] supported)
        {
            if (entry == null || !entry.TryText(key, out string value) || string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            value = value.Trim();
            for (int i = 0; i < supported.Length; i++)
            {
                if (string.Equals(value, supported[i], StringComparison.OrdinalIgnoreCase))
                {
                    return supported[i];
                }
            }

            unsupportedDeclarations.Add($"{entry.Definition.Id}.{key}='{value}' — 구현하지 않은 값이라 "
                + $"'{fallback}' 로 동작한다 (아는 값: {string.Join(", ", supported)}).");
            return fallback;
        }

        /// <summary>선언된 참/거짓을 읽는다. 없으면 <paramref name="fallback"/>.</summary>
        private static bool DeclaredFlag(BlessingMvpDefinition entry, string key, bool fallback) =>
            entry != null && entry.TryNumber(key, out float value) ? value > 0f : fallback;

        /// <summary>표식·복수 대상이 없다는 뜻의 전투원 번호.</summary>
        public const int NoTarget = int.MinValue;

        /// <summary>
        /// "다가간다" 의 각도(좌우 반각). 사냥꾼은 엑셀 <c>chase_angle</c> 을 쓰고, 값이 없는 쾌속 접근·분노의 질주는 이 값이다.
        /// </summary>
        public const float DefaultApproachHalfAngle = 60f;

        private BlessingMvpDefinition lightStep, booster, ouch, closeIn, hype, hunterMark, rageDash, underdog, bully,
            escapeDash, afterimage, oneHit, berserk, draw, lastFive, tripleShot, headshot, poisonArrow,
            greed, lastStand, pickpocket, interestIncome, bodyCheck, sturdy, nineLives, crossbow, ballista;

        /// <summary>가진 저격수 등급 전부. 등급마다 피해 증가를 더한다.</summary>
        private readonly List<BlessingMvpDefinition> sniperTiers = new List<BlessingMvpDefinition>();

        private float bodyCheckReadyAt;
        private int sturdyHits;
        /// <summary>기절의 끝. 기절은 경직(이동·공격 불가)에 더해 소모품도 쓰지 못한다(S-GD-9MWJRR).</summary>
        private float stunUntil;
        /// <summary>9개의 목숨이 준 무적의 끝(HUD 용).</summary>
        private float nineLivesUntil;

        private bool lightStepBroken;
        private float boosterUntil, ouchUntil, hypeUntil;
        private bool approachingNear, approachingHunter, approachingRage;
        private int hunterTarget = NoTarget, rageTarget = NoTarget;
        private float escapeReadyAt;
        private int pendingDashCharges;
        private float invulnerableUntil;
        /// <summary>"1초에 한 대만!" 이 준 무적의 끝. HUD 가 "지구가 멸망해도 5초!" 의 무적과 따로 보인다.</summary>
        private float oneHitUntil;
        private float berserkUntil, berserkReadyAt, drawUntil, drawReadyAt;
        private bool lastFiveUsed, lastFiveDeathPending;
        private float lastFiveDeathAt;
        private bool lastHitFiresOnDamaged;
        private int goldSnapshot;
        private bool lastStandTriggered, firstPlaceGranted, interestStarted;

        /// <summary>
        /// 이 라운드에서 본 <b>가장 많은 생존 인원</b> = 매치 참가 인원. 승부사의 <c>min_match_size</c> 가 이 값을 본다
        /// (S-GD-85DD2Y — 최소 매치 인원도 엑셀 <c>parameters</c> 다).
        /// </summary>
        /// <remarks>
        /// 참가 인원을 따로 주입받지 않는 이유: 실행 경로마다(온라인·Local Play·훈련) 인원을 아는 주체가 달라
        /// 네 곳에서 같은 값을 넣어야 하고, 한 곳만 빠뜨리면 <b>조용히 조건이 풀린다</b>. 라운드 내내 본 최댓값은
        /// 어느 경로에서든 같은 뜻이고 어댑터가 이미 매 틱 넘겨 주는 값에서 나온다.
        /// </remarks>
        private int roundPeakAlive;
        private float pickpocketReadyAt, interestNextAt;

        /// <summary>
        /// 전투 라운드가 <b>실제로 돌고 있는가</b>. 라운드 경제 지급(이자 폭발의 주기 이자)은 이 값이 참일 때만 일어난다
        /// (S-GD-XBP3VA). 기본값은 <c>true</c> 다 — 라운드 국면이라는 개념이 없는 경로(CombatLab·96 훈련 랩·EditMode)는
        /// 예전과 같이 돈다. 제품 Host 와 Local Play 는 국면이 바뀔 때마다 이 값을 밀어 넣는다.
        /// </summary>
        /// <remarks>
        /// ⚠️ <c>combatActive</c>(= 공격 실행 가능 여부)와 <b>다르다.</b> 준비 단계에도 공격은 허용되므로
        /// (<c>OnlineCoreLoopSnapshot.CanUseCombatActions</c>) 그 값으로 지급을 막으면 준비 단계 공격까지 막힌다.
        /// 실제로 제품에서는 <c>combatActive</c> 가 정산·다음 준비에서도 참이어서 <b>라운드가 끝난 뒤에도 이자가 들어왔다.</b>
        /// </remarks>
        private bool combatRoundRunning = true;
        private int earnedGold;

        /// <summary>한 대를 맞힐 때 대상과 나를 비교하는 값(언더독·약자 멸시). 모르면 음수다.</summary>
        public readonly struct TargetContext
        {
            public TargetContext(float targetHealthRatio, float targetAttackPower, float selfAttackPower)
            {
                TargetHealthRatio = targetHealthRatio;
                TargetAttackPower = targetAttackPower;
                SelfAttackPower = selfAttackPower;
            }

            public float TargetHealthRatio { get; }
            public float TargetAttackPower { get; }
            public float SelfAttackPower { get; }

            public static TargetContext Unknown => new TargetContext(-1f, -1f, -1f);
        }

        /// <summary>주무기 적중 한 번이 부른 엑셀 효과 — 헤드샷 즉사·독화살.</summary>
        public readonly struct PrimaryHitOutcome
        {
            public PrimaryHitOutcome(bool instantKill, float poisonHitDamage, float poisonDps, float poisonSeconds,
                int poisonMaxStacks, int stealCount = 0, bool poisonSkipsInvulnerable = true)
            {
                StealCount = stealCount;
                InstantKill = instantKill;
                PoisonHitDamage = poisonHitDamage;
                PoisonDps = poisonDps;
                PoisonSeconds = poisonSeconds;
                PoisonMaxStacks = poisonMaxStacks;
                PoisonSkipsInvulnerable = poisonSkipsInvulnerable;
            }

            public bool InstantKill { get; }
            public float PoisonHitDamage { get; }
            public float PoisonDps { get; }
            public float PoisonSeconds { get; }
            public int PoisonMaxStacks { get; }

            /// <summary>소매치기가 빼앗을 개수. 0 이면 발동하지 않았다.</summary>
            public int StealCount { get; }

            /// <summary>무적인 대상에는 중첩을 쌓지 않는가 (<c>no_stack_on_invuln</c>).</summary>
            public bool PoisonSkipsInvulnerable { get; }
        }

        private void BindExcelEffects()
        {
            byEffect.TryGetValue(EffectLightStep, out lightStep);
            byEffect.TryGetValue(EffectBooster, out booster);
            byEffect.TryGetValue(EffectOuch, out ouch);
            byEffect.TryGetValue(EffectCloseIn, out closeIn);
            byEffect.TryGetValue(EffectHype, out hype);
            byEffect.TryGetValue(EffectHunterMark, out hunterMark);
            byEffect.TryGetValue(EffectRageDash, out rageDash);
            byEffect.TryGetValue(EffectUnderdog, out underdog);
            byEffect.TryGetValue(EffectBully, out bully);
            byEffect.TryGetValue(EffectEscapeDash, out escapeDash);
            byEffect.TryGetValue(EffectAfterimage, out afterimage);
            byEffect.TryGetValue(EffectOneHit, out oneHit);
            byEffect.TryGetValue(EffectBerserk, out berserk);
            byEffect.TryGetValue(EffectDraw, out draw);
            byEffect.TryGetValue(EffectLastFive, out lastFive);
            byEffect.TryGetValue(EffectTripleShot, out tripleShot);
            byEffect.TryGetValue(EffectHeadshot, out headshot);
            byEffect.TryGetValue(EffectPoisonArrow, out poisonArrow);
            byEffect.TryGetValue(EffectGreed, out greed);
            byEffect.TryGetValue(EffectLastStand, out lastStand);
            byEffect.TryGetValue(EffectPickpocket, out pickpocket);
            byEffect.TryGetValue(EffectInterestIncome, out interestIncome);
            byEffect.TryGetValue(EffectBodyCheck, out bodyCheck);
            byEffect.TryGetValue(EffectSturdy, out sturdy);
            byEffect.TryGetValue(EffectNineLives, out nineLives);
            byEffect.TryGetValue(EffectCrossbow, out crossbow);
            byEffect.TryGetValue(EffectBallista, out ballista);
            ResolveDeclarations();
        }

        /// <summary>
        /// 엑셀이 고른 동작을 한 번 해석해 둔다. 매 틱 문자열을 비교하지 않기 위한 것이고,
        /// 모르는 값은 여기서 한 번만 기록된다.
        /// </summary>
        private void ResolveDeclarations()
        {
            unsupportedDeclarations.Clear();

            underdogCompareMode = DeclaredMode(underdog, ParamCompareMode, CompareHealthRatio,
                CompareHealthRatio, CompareBaseAttackPower);
            bullyCompareMode = DeclaredMode(bully, ParamCompareMode, CompareBaseAttackPower,
                CompareHealthRatio, CompareBaseAttackPower);

            // 무적의 종류 — 셋 다 같은 종류여야 한다. 하나라도 공격을 막는 종류면 막는 쪽이 이긴다.
            invulnerabilityAllowsAttacking = true;
            foreach (BlessingMvpDefinition entry in new[] { lastFive, nineLives, oneHit })
            {
                if (entry == null)
                {
                    continue;
                }

                string kind = DeclaredMode(entry, ParamInvulnerableKind, InvulnerableAttackable,
                    InvulnerableAttackable);
                invulnerabilityAllowsAttacking &= string.Equals(kind, InvulnerableAttackable, StringComparison.Ordinal);
            }

            rageMarkSelection = rageDash == null ? BlessingMarkSelection.None
                : string.Equals(
                    DeclaredMode(rageDash, ParamMarkTarget, MarkTopDamageDealer, MarkTopDamageDealer),
                    MarkTopDamageDealer, StringComparison.Ordinal)
                    ? BlessingMarkSelection.TopDamageDealer
                    : BlessingMarkSelection.None;

            lastFiveKillsOnExpire = DeclaredFlag(lastFive, ParamDeathOnExpire, true);
            lastFiveInterceptsInstantKill = DeclaredFlag(lastFive, ParamTriggersOnInstantKill, false);
            poisonSkipsInvulnerableTargets = DeclaredFlag(poisonArrow, ParamNoStackOnInvulnerable, true);
            headshotAppliesToExtraProjectiles = DeclaredFlag(headshot, ParamAppliesToExtraProjectiles, false);

            // 헤드샷의 즉사는 피해 숫자를 만들지 않아 방어력이 끼어들 자리가 없다 — 선언이 'true' 일 때만 지금 구현과 같다.
            if (headshot != null && !DeclaredFlag(headshot, ParamIgnoresDefense, true))
            {
                unsupportedDeclarations.Add($"{headshot.Definition.Id}.{ParamIgnoresDefense}=false — "
                    + "방어력을 따지는 즉사는 구현하지 않았다. 지금은 방어력과 무관하게 즉사한다.");
            }
        }

        private string underdogCompareMode = CompareHealthRatio;
        private string bullyCompareMode = CompareBaseAttackPower;
        private bool invulnerabilityAllowsAttacking = true;
        private BlessingMarkSelection rageMarkSelection;
        private bool lastFiveKillsOnExpire = true;
        private bool lastFiveInterceptsInstantKill;
        private bool poisonSkipsInvulnerableTargets = true;
        private bool headshotAppliesToExtraProjectiles;

        /// <summary>
        /// 무적 중에 공격할 수 있는가 (<c>invuln_kind</c>). <c>ATTACKABLE</c> 이면 할 수 있다.
        /// </summary>
        public bool InvulnerabilityAllowsAttacking => invulnerabilityAllowsAttacking;

        /// <summary>분노의 질주가 표식을 고르는 법 (<c>mark_target</c>). 어댑터가 이 값으로 대상을 고른다.</summary>
        public BlessingMarkSelection RageMarkSelection => rageMarkSelection;

        /// <summary>독화살 중첩이 무적 대상을 건너뛰는가 (<c>no_stack_on_invuln</c>).</summary>
        public bool PoisonSkipsInvulnerableTargets => poisonSkipsInvulnerableTargets;

        /// <summary>
        /// "지구가 멸망해도 5초!" 가 즉사(헤드샷)도 가로채는가 (<c>triggers_on_instant_kill</c>).
        /// 거짓이면 즉사는 그대로 통한다(엑셀 비고, S-GD-9MWJRR).
        /// </summary>
        public bool LastFiveInterceptsInstantKill => lastFive != null && lastFiveInterceptsInstantKill;

        /// <summary>같은 효과를 여러 등급 가질 수 있는 엑셀 효과를 모은다. <see cref="BuildLoadout"/> 이 부른다.</summary>
        private void CollectExcelTier(BlessingMvpDefinition entry)
        {
            if (entry.Effect == EffectSniper)
            {
                sniperTiers.Add(entry);
            }
        }

        /// <summary>라운드가 바뀌면 일시 효과·표식·예약이 전부 풀린다.</summary>
        private void ResetExcelEffects(float now)
        {
            lightStepBroken = false;
            boosterUntil = ouchUntil = hypeUntil = now;
            approachingNear = approachingHunter = approachingRage = false;
            hunterTarget = rageTarget = NoTarget;
            escapeReadyAt = now;
            pendingDashCharges = 0;
            invulnerableUntil = now;
            oneHitUntil = now;
            berserkUntil = berserkReadyAt = drawUntil = drawReadyAt = now;
            lastFiveUsed = lastFiveDeathPending = false;
            lastFiveDeathAt = now;
            lastHitFiresOnDamaged = false;
            lastStandTriggered = firstPlaceGranted = interestStarted = false;
            roundPeakAlive = 0;
            pickpocketReadyAt = interestNextAt = now;
            earnedGold = 0;
            bodyCheckReadyAt = now;
            sturdyHits = 0;
            stunUntil = now;
            nineLivesUntil = now;
        }

        // ── 이동·공격 속도 ─────────────────────────────────────────────────

        /// <summary>엑셀 효과가 주는 이동 속도 증감(합연산).</summary>
        private float ExcelMoveBonus
        {
            get
            {
                float bonus = 0f;
                if (lightStep != null && !lightStepBroken) bonus += lightStep.Number("move_speed");
                if (booster != null && time < boosterUntil) bonus += booster.Number("move_speed");
                if (ouch != null && time < ouchUntil) bonus += ouch.Number("move_speed");
                if (closeIn != null && approachingNear) bonus += closeIn.Number("move_speed");
                if (hunterMark != null && approachingHunter && hunterTarget != NoTarget)
                    bonus += hunterMark.Number("move_speed");
                if (rageDash != null && approachingRage && rageTarget != NoTarget) bonus += rageDash.Number("move_speed");
                if (hype != null && time < hypeUntil) bonus += hype.Number("move_speed");
                if (berserk != null && time < berserkUntil) bonus += berserk.Number("move_speed");
                if (draw != null && time < drawUntil) bonus += draw.Number("move_speed");
                if (lastStand != null && lastStandTriggered) bonus += lastStand.Number("stat_bonus");
                return bonus;
            }
        }

        /// <summary>엑셀 효과가 주는 공격 속도 증감(합연산).</summary>
        private float ExcelAttackSpeedBonus
        {
            get
            {
                float bonus = 0f;
                if (hype != null && time < hypeUntil) bonus += hype.Number("attack_speed");
                if (berserk != null && time < berserkUntil) bonus += berserk.Number("attack_speed");
                if (lastStand != null && lastStandTriggered) bonus += lastStand.Number("stat_bonus");
                return bonus;
            }
        }

        /// <summary>
        /// 공격 속도 <b>곱연산</b> 배율 — 기획상 공격 속도 감소 패널티만 곱한다(잔상 ×0.72). 기본 1.
        /// </summary>
        public float AttackSpeedMultiplier
        {
            get
            {
                // 잔상·쇠뇌·발리스타의 공격 속도 감소 패널티. 패널티끼리는 곱한다(S-GD-SH74SC 의 유일한 곱연산).
                float multiplier = 1f;
                if (afterimage != null) multiplier *= afterimage.Number("attack_speed_mult");
                if (crossbow != null) multiplier *= crossbow.Number("attack_speed_mult");
                if (ballista != null) multiplier *= ballista.Number("attack_speed_mult");
                return Math.Max(0.01f, multiplier);
            }
        }

        // ── 켜진 동안의 능력치 ────────────────────────────────────────────

        private void AddExcelTimedStats(List<StatContribution> into)
        {
            if (berserk != null && time < berserkUntil)
            {
                into.Add(new StatContribution(DamageStatKeys.AttackPower, berserk.Number("atk_flat"), StatStackMode.Add,
                    berserk.Definition.Id));
                into.Add(new StatContribution(DamageStatKeys.Lifesteal, berserk.Number("lifesteal_ratio"),
                    StatStackMode.Add, berserk.Definition.Id));
                into.Add(new StatContribution(DamageStatKeys.DamageTakenMult, berserk.Number("damage_taken"),
                    StatStackMode.Add, berserk.Definition.Id));
            }

            if (draw != null && time < drawUntil)
            {
                into.Add(new StatContribution(DamageStatKeys.AttackPower, draw.Number("atk_flat"), StatStackMode.Add,
                    draw.Definition.Id));
                into.Add(new StatContribution(DamageStatKeys.DamageTakenMult, draw.Number("damage_taken"),
                    StatStackMode.Add, draw.Definition.Id));
            }

            // 탐욕 — 보유 골드의 일부가 공격력이 된다. 골드가 기준 이하면 없다. 골드는 전투를 시작할 때 받는다.
            if (greed != null && goldSnapshot > greed.Number("min_gold"))
            {
                float bonus = goldSnapshot * greed.Number("atk_from_gold_ratio");
                // S-GD-GX2K2G: unlimited carryover must not create unlimited attack power.
                // Older content without this parameter retains its original behavior.
                if (greed.TryNumber("max_attack_bonus", out float cap)) bonus = Math.Min(bonus, Math.Max(0f, cap));
                into.Add(new StatContribution(DamageStatKeys.AttackPower,
                    bonus, StatStackMode.Add, greed.Definition.Id));
            }

            // 승부사 — 최후 2인이 되기 전까지는 받는 피해가 는다.
            if (LastStandPenaltyActive)
            {
                into.Add(new StatContribution(DamageStatKeys.DamageTakenMult, lastStand.Number("pre_damage_taken"),
                    StatStackMode.Add, lastStand.Definition.Id));
            }

            // 단단해지기! — 맞을 때마다 쌓이는 방어력. 쌓이는 몫이 최대 defense_cap 이다(기본 방어력과 별개, S-GD-9MWJRR).
            if (sturdy != null && SturdyBonusDefense > 0f)
            {
                into.Add(new StatContribution(DamageStatKeys.Defense, SturdyBonusDefense, StatStackMode.Add,
                    sturdy.Definition.Id));
            }

            // 9개의 목숨 — 받는 피해가 한 대에 정해진 값으로 고정된다(고정 피해 포함, 무적·면역만 막는다).
            if (nineLives != null && nineLives.TryNumber("damage_taken_fixed", out float fixedTaken) && fixedTaken >= 0f)
            {
                into.Add(new StatContribution(DamageStatKeys.DamageTakenFixed, fixedTaken, StatStackMode.Min,
                    nineLives.Definition.Id));
            }
        }

        /// <summary>단단해지기! 가 지금까지 쌓은 방어력(상한 적용).</summary>
        private float SturdyBonusDefense => sturdy == null ? 0f
            : Math.Min(sturdyHits * Math.Max(0f, sturdy.Number("defense_per_hit")), Math.Max(0f, sturdy.Number("defense_cap")));

        // ── 공격 ───────────────────────────────────────────────────────────

        /// <summary>공격을 시작할 때 소비하는 엑셀 효과 — 잔상 대시 충전, 3연발.</summary>
        private void ApplyExcelAttack(float now, WeaponTag weapon, ref int extraProjectileCount,
            ref float extraProjectileSpreadDegrees, ref float extraProjectileDamageMultiplier)
        {
            if (afterimage != null)
            {
                pendingDashCharges += Math.Max(0, (int)afterimage.Number("dash_charge_on_attack"));
            }

            if (tripleShot != null && weapon == WeaponTag.Bow)
            {
                // "좌우 {spread_angle}도" — 추가 화살은 가운데 화살에서 양쪽으로 그만큼 벌어진다. 발사 코드는 추가 화살
                // 사이 간격을 받으므로 두 배로 넘긴다. 피해는 원래 화살과 같다.
                if (extraProjectileCount == 0)
                {
                    extraProjectileDamageMultiplier = 1f;
                }

                extraProjectileCount += Math.Max(0, (int)tripleShot.Number("extra_projectiles"));
                extraProjectileSpreadDegrees = Math.Max(extraProjectileSpreadDegrees,
                    2f * tripleShot.Number("spread_angle"));
            }
        }

        /// <summary>한 대상에게만 붙는 피해 증감 — 사냥꾼·분노의 질주·언더독·약자 멸시.</summary>
        private float ExcelTargetDamageBonus(int target, in TargetContext context)
        {
            float bonus = 0f;
            if (hunterMark != null && target != NoTarget && target == hunterTarget)
            {
                bonus += hunterMark.Number("damage_bonus");
            }

            if (rageDash != null && target != NoTarget && target == rageTarget)
            {
                bonus += rageDash.Number("damage_bonus");
            }

            // 언더독·약자 멸시는 **엑셀이 고른 비교 방식**(compare_mode)으로 대상과 나를 견준다.
            // 예전에는 두 효과가 각자 비교를 박아 두어, 엑셀에서 방식을 바꿔도 게임이 그대로였다.
            bool? underdogFavors = CompareFavorsMe(underdogCompareMode, context);
            if (underdog != null && underdogFavors == true)
            {
                bonus += underdog.Number("damage_bonus");
            }

            bool? bullyFavors = CompareFavorsMe(bullyCompareMode, context);
            if (bully != null && bullyFavors.HasValue)
            {
                bonus += bullyFavors.Value
                    ? bully.Number("damage_bonus")
                    : bully.Number("damage_penalty");
            }

            return bonus;
        }

        /// <summary>
        /// 엑셀이 고른 방식으로 <b>내가 조건을 만족하는가</b>를 본다.
        /// <c>true</c> 보너스 · <c>false</c> 반대 · <c>null</c> 견줄 값이 없다.
        /// </summary>
        /// <remarks>
        /// 언더독은 <c>HP_RATIO</c>(대상 체력 비율이 나보다 높을 때), 약자 멸시는
        /// <c>BASE_ATTACK_POWER</c>(대상 공격력이 나보다 낮을 때)가 기본이다. 둘 다 엑셀이 바꿀 수 있다.
        /// </remarks>
        private bool? CompareFavorsMe(string mode, in TargetContext context)
        {
            if (string.Equals(mode, CompareHealthRatio, StringComparison.Ordinal))
            {
                if (context.TargetHealthRatio < 0f)
                {
                    return null;
                }

                float selfRatio = maxHealth > 0f ? currentHealth / maxHealth : 0f;
                return context.TargetHealthRatio > selfRatio;
            }

            if (string.Equals(mode, CompareBaseAttackPower, StringComparison.Ordinal))
            {
                if (context.TargetAttackPower < 0f || context.SelfAttackPower < 0f)
                {
                    return null;
                }

                return context.TargetAttackPower < context.SelfAttackPower;
            }

            return null;
        }

        /// <summary>
        /// 날아간 거리로 붙는 피해 증가 — 저격수. 화살이 <c>min_distance</c> 이상 날아가 맞히면 등급마다 더한다
        /// (거리 기준은 장거리 배당과 같은 비행 거리, S-GD-9MWJRR).
        /// </summary>
        private float ExcelDistanceDamageBonus(WeaponTag weapon, float travelDistance)
        {
            if (sniperTiers.Count == 0 || !IsRanged(weapon) || !IsFinite(travelDistance))
            {
                return 0f;
            }

            float bonus = 0f;
            for (int i = 0; i < sniperTiers.Count; i++)
            {
                if (travelDistance >= sniperTiers[i].Number("min_distance"))
                {
                    bonus += sniperTiers[i].Number("damage_bonus");
                }
            }

            return bonus;
        }

        /// <summary>
        /// 주무기가 대상을 맞혔다(권위 판정). 사냥꾼 표식을 옮기고, 헤드샷·독화살 결과를 돌려준다 — 실제 적용은 전투원 몫이다.
        /// </summary>
        /// <param name="extraProjectile">3연발·분할 사격의 <b>추가</b> 화살인가. 헤드샷은 추가 화살에 붙지 않는다.</param>
        public PrimaryHitOutcome NotifyPrimaryHit(int target, WeaponTag weapon, bool extraProjectile, int seed) =>
            NotifyPrimaryHit(target, weapon, extraProjectile, seed, derivedHit: false);

        /// <param name="derivedHit">
        /// 화살비처럼 원본 공격에서 <b>파생</b>된 적중인가 (S-GD-ND8JZ5). 사냥꾼 표식·헤드샷(추가 화살 정책)·독화살은
        /// 붙지만, 소매치기(아이템 강탈)는 원본 적중의 일회성 경제 효과라 파생 적중에서 다시 굴리지 않는다.
        /// </param>
        public PrimaryHitOutcome NotifyPrimaryHit(int target, WeaponTag weapon, bool extraProjectile, int seed, bool derivedHit)
        {
            if (hunterMark != null && target != NoTarget)
            {
                hunterTarget = target;
            }

            // 추가 화살(3연발·분할 사격)에도 붙는지는 엑셀이 정한다(applies_to_extra_projectiles).
            bool instantKill = headshot != null && weapon == WeaponTag.Bow
                && (headshotAppliesToExtraProjectiles || !extraProjectile)
                && DeterministicProc(headshot.Number("instant_kill_chance"), seed);

            // 소매치기 — 확률로 빼앗고, 재사용 대기(internal_cooldown) 동안은 다시 굴리지 않는다.
            int steal = 0;
            if (!derivedHit && pickpocket != null && time >= pickpocketReadyAt
                && DeterministicProc(pickpocket.Number("steal_chance"), unchecked(seed * 31 + 0x5F3759DF)))
            {
                steal = Math.Max(1, (int)pickpocket.Number("steal_count"));
                pickpocketReadyAt = time + pickpocket.Number("internal_cooldown");
            }

            if (poisonArrow == null || !IsRanged(weapon))
            {
                return new PrimaryHitOutcome(instantKill, 0f, 0f, 0f, 0, steal);
            }

            return new PrimaryHitOutcome(instantKill,
                Math.Max(0f, poisonArrow.Number("on_hit_fixed_damage")),
                Math.Max(0f, poisonArrow.Number("dot_damage_per_second")),
                Math.Max(0f, poisonArrow.Number("dot_duration")),
                Math.Max(1, (int)poisonArrow.Number("dot_max_stacks")), steal,
                poisonSkipsInvulnerableTargets);
        }

        // ── 맞을 때 ────────────────────────────────────────────────────────

        /// <summary>언더독 — 나보다 체력 비율이 높은 적에게 받는 피해가 준다(고정 피해에는 부르지 않는다).</summary>
        /// <summary>언더독의 받는 피해 증감(나보다 체력 비율이 높은 적에게 맞을 때). 계산 4단계 합산에 더한다.</summary>
        private float ExcelIncomingBonus(float attackerHealthRatio)
        {
            // 맞는 쪽에는 공격자의 공격력이 오지 않는다 — 체력 비율로 견주는 방식일 때만 적용한다.
            if (underdog == null || !string.Equals(underdogCompareMode, CompareHealthRatio, StringComparison.Ordinal))
            {
                return 0f;
            }

            float selfRatio = maxHealth > 0f ? currentHealth / maxHealth : 0f;
            if (attackerHealthRatio >= 0f && attackerHealthRatio > selfRatio)
            {
                return underdog.Number("damage_taken");
            }

            return 0f;
        }

        /// <summary>
        /// 죽을 피해 트리거 "지구가 멸망해도 5초!" — 체력 1 로 버티며 무적이 되고, 시간이 끝나면 죽는다.
        /// </summary>
        private float ApplyLastFive(float now, float damage)
        {
            if (lastFive == null || lastFiveUsed || currentHealth <= 0f || damage < currentHealth)
            {
                return damage;
            }

            lastFiveUsed = true;
            float seconds = Math.Max(0f, lastFive.Number("invuln_duration"));
            invulnerableUntil = Math.Max(invulnerableUntil, now + seconds);
            lastFiveDeathAt = now + seconds;
            lastFiveDeathPending = true;
            return Math.Max(0f, currentHealth - 1f);
        }

        /// <summary>실제로 맞은 뒤의 엑셀 효과 — 피격 트리거와 체력 임계.</summary>
        private void OnExcelAfterDamage(float now, float health, float max)
        {
            // 이 표시는 맞을 때마다 BeforeDamage 가 새로 쓴다 — 지울 필요가 없다.
            if (lastHitFiresOnDamaged)
            {
                lightStepBroken = true;

                if (ouch != null)
                {
                    ouchUntil = now + ouch.Number("duration");
                }

                if (escapeDash != null && now >= escapeReadyAt)
                {
                    pendingDashCharges += Math.Max(0, (int)escapeDash.Number("dash_charge_on_damaged"));
                    escapeReadyAt = now + escapeDash.Number("internal_cooldown");
                }

                if (oneHit != null)
                {
                    oneHitUntil = Math.Max(oneHitUntil, now + oneHit.Number("invuln_duration"));
                    invulnerableUntil = Math.Max(invulnerableUntil, oneHitUntil);
                }

                // 9개의 목숨 — 맞으면 잠깐 무적(1초에 한 대만! 과 같은 방식 — 무적 중에도 공격한다).
                if (nineLives != null)
                {
                    nineLivesUntil = Math.Max(nineLivesUntil, now + nineLives.Number("invuln_duration"));
                    invulnerableUntil = Math.Max(invulnerableUntil, nineLivesUntil);
                }

                // 단단해지기! — 공격받을 때마다 한 칸. 상한은 능력치로 넣을 때 건다.
                if (sturdy != null)
                {
                    sturdyHits++;
                }
            }

            if (max <= 0f)
            {
                return;
            }

            if (berserk != null && now >= berserkReadyAt && health < max * berserk.Number("trigger_hp_ratio"))
            {
                berserkUntil = now + berserk.Number("duration");
                berserkReadyAt = now + berserk.Number("internal_cooldown");
            }

            if (draw != null && now >= drawReadyAt && health < max * draw.Number("trigger_hp_ratio"))
            {
                drawUntil = now + draw.Number("duration");
                drawReadyAt = now + draw.Number("internal_cooldown");
                pendingHealFraction += Math.Max(0f, draw.Number("instant_heal_ratio"));
            }
        }

        private void OnExcelDash(float now)
        {
            if (booster != null)
            {
                boosterUntil = now + booster.Number("duration");
            }

            if (escapeDash != null)
            {
                pendingHealFraction += Math.Max(0f, escapeDash.Number("heal_ratio_on_dash"));
            }
        }

        private void OnExcelKill(float now, int victim, bool killingBlowByPrimary)
        {
            TriggerHype(now);

            // 사냥꾼 — 표식이 붙은 적을 처치하면 골드. 주무기로 쓰러뜨렸으면 그 적중이 마지막으로 때린 것이라 표식 대상이다.
            if (hunterMark != null && victim != NoTarget && (victim == hunterTarget || killingBlowByPrimary))
            {
                earnedGold += Math.Max(0, (int)hunterMark.Number("kill_reward_gold"));
                hunterTarget = NoTarget;
            }

            // 이자 폭발 t1 — 예치했으면 처치마다 골드.
            if (interestIncome != null && interestDeposited && !dead)
            {
                earnedGold += Math.Max(0, (int)interestIncome.Number("kill_bonus"));
            }
        }

        /// <summary>어시스트 — 신난다! 만 켠다(S-GD-NN5PR4 #3). 처치 보상(사냥꾼 골드·이자)은 처치자 몫이다.</summary>
        private void OnExcelAssist(float now, int victim) => TriggerHype(now);

        /// <summary>신난다! — 켜져 있으면 늘리고, 꺼져 있으면 새로 켠다. 처치와 어시스트가 같은 규칙이다.</summary>
        private void TriggerHype(float now)
        {
            if (hype != null)
            {
                hypeUntil = now < hypeUntil ? hypeUntil + hype.Number("extend_amount") : now + hype.Number("duration");
            }
        }

        /// <summary>이자 폭발 t1 — 예치했으면 전투 시작 N초부터 일정 간격으로 이자. 쓰러지거나 라운드가 끝나면 멈춘다.</summary>
        private void TickExcel(float now, float elapsed, bool active)
        {
            if (interestIncome == null || !interestDeposited || !active || dead || !combatRoundRunning)
            {
                return;
            }

            if (!interestStarted)
            {
                if (elapsed < interestIncome.Number("start_seconds"))
                {
                    return;
                }

                interestStarted = true;
                interestNextAt = now;
            }

            float interval = Math.Max(0.1f, interestIncome.Number("interval"));
            while (now >= interestNextAt)
            {
                earnedGold += Math.Max(0, (int)interestIncome.Number("interest"));
                interestNextAt += interval;
            }
        }

        /// <summary>
        /// 예치된 실버 이자 계약의 활성 상태. 첫 지급 대기 중과 지급 사이에도 계약은 유지된다.
        /// 표시 조회는 이자 시계·지급 대기 금액을 소비하지 않는다.
        /// </summary>
        public bool InterestIncomeActive =>
            interestIncome != null && interestDeposited && combatActive && combatRoundRunning && !dead;

        /// <summary>전투 라운드 실행 상태를 밀어 넣는다. 세션(제품 Host·Local Play)이 국면 전환에서 부른다 (S-GD-XBP3VA).</summary>
        public void SetCombatRoundRunning(bool running) => combatRoundRunning = running;

        /// <summary>지금 라운드 경제 지급이 열려 있는가. 진단·테스트가 읽는다.</summary>
        public bool IsCombatRoundRunning => combatRoundRunning;

        // ── 전투원이 읽고 쓰는 것 ─────────────────────────────────────────

        /// <summary>다가감을 따져야 하는 효과가 있는가. 없으면 전투원이 주변을 훑지 않는다.</summary>
        public bool WantsApproach => closeIn != null || hunterMark != null || rageDash != null;

        /// <summary>쾌속 접근의 거리(m). 없으면 0.</summary>
        public float CloseInDistance => closeIn != null ? closeIn.Number("trigger_distance") : 0f;

        /// <summary>사냥꾼이 "다가간다" 로 보는 좌우 반각(도).</summary>
        public float HunterChaseHalfAngle =>
            hunterMark != null ? hunterMark.Number("chase_angle") : DefaultApproachHalfAngle;

        /// <summary>사냥꾼 표식이 붙은 적. 없으면 <see cref="NoTarget"/>.</summary>
        public int HunterTarget => hunterMark != null ? hunterTarget : NoTarget;

        /// <summary>분노의 질주 대상(나를 가장 많이 때린 적). 없으면 <see cref="NoTarget"/>.</summary>
        public int RageTarget => rageDash != null ? rageTarget : NoTarget;

        /// <summary>
        /// 분노의 질주가 <b>나를 가장 많이 때린 적</b>을 대상으로 정해야 하는가 (<c>mark_target</c>).
        /// 엑셀이 다른 기준을 선언하면 거짓이고, 그 선언은 <see cref="UnsupportedDeclarations"/> 에 남는다.
        /// </summary>
        public bool TracksTopDamageDealer => rageMarkSelection == BlessingMarkSelection.TopDamageDealer;

        /// <summary>전투원이 이번 틱의 이동 방향으로 계산한 "다가감" 을 넣는다.</summary>
        public void SetApproach(bool nearEnemy, bool towardHunterTarget, bool towardRageTarget)
        {
            approachingNear = nearEnemy;
            approachingHunter = towardHunterTarget;
            approachingRage = towardRageTarget;
        }

        /// <summary>분노의 질주 대상을 정한다. 전투원이 받은 피해를 공격자별로 모아 가장 큰 쪽을 넣는다.</summary>
        public void SetRageTarget(int attacker) => rageTarget = attacker;

        /// <summary>축복이 준 무적(1초에 한 대만!·지구가 멸망해도 5초!) 중인가. 무적 중에도 공격은 할 수 있다.</summary>
        public bool IsInvulnerable => time < invulnerableUntil;

        /// <summary>쌓인 대시 충전(도망가·잔상)을 꺼낸다. 전투원이 모터에 지급한다.</summary>
        public int ConsumePendingDashCharges()
        {
            int charges = pendingDashCharges;
            pendingDashCharges = 0;
            return charges;
        }

        /// <summary>"지구가 멸망해도 5초!" 의 시간이 끝났는가. <b>한 번만 참</b> — 전투원이 그때 죽인다.</summary>
        public bool ConsumeLastFiveDeath()
        {
            // 시간이 끝나면 쓰러지는지는 엑셀이 정한다(death_on_expire). 거짓이면 무적만 끝난다.
            if (!lastFiveKillsOnExpire)
            {
                lastFiveDeathPending = false;
                return false;
            }

            if (!lastFiveDeathPending || time < lastFiveDeathAt)
            {
                return false;
            }

            lastFiveDeathPending = false;
            return true;
        }

        /// <summary>"지구가 멸망해도 5초!" 로 버티는 중인가.</summary>
        public bool LastFiveActive => lastFiveDeathPending;

        /// <summary>
        /// 지금 상자를 열 수 없는가 — "지구가 멸망해도 5초!" 로 버티는 동안이고 엑셀 <c>cancels_chest_open</c> 이 참이다.
        /// </summary>
        /// <remarks>
        /// 상자 쪽은 이 값을 "개봉 자격 없음" 으로 읽는다 — 여는 중이면 취소하고, 새로 열지 못하게 한다(S-GD-Y534KH).
        /// 버티기를 켠 피해가 체력을 깎으면 피격 취소가 먼저 걸리지만, 체력이 이미 1 이면 깎인 체력이 없어 그 취소가
        /// 걸리지 않는다. 그 경우와 버티는 5초 동안 새로 여는 경우를 이 값이 막는다.
        /// </remarks>
        public bool BlocksChestOpening => lastFive != null && lastFiveDeathPending
            && lastFive.TryNumber("cancels_chest_open", out float cancels) && cancels > 0f;

        // ── 표시 전용 읽기 창 (HUD 효과 칸 — S-UX-AYM6K3) ─────────────────
        // 위 판정과 같은 조건을 읽기만 한다. 시계·중첩을 건드리지 않는다.

        /// <summary>가벼운 발걸음 — 아직 피해를 받지 않아 이동 속도가 붙어 있는가.</summary>
        public bool LightStepActive => lightStep != null && !lightStepBroken;

        /// <summary>부스터(대시 뒤 이동 속도)의 남은 시간.</summary>
        public float BoosterRemaining => booster != null ? Math.Max(0f, boosterUntil - time) : 0f;
        public float BoosterDuration => booster != null ? booster.Number("duration") : 0f;

        /// <summary>아야!(맞은 뒤 이동 속도)의 남은 시간.</summary>
        public float OuchRemaining => ouch != null ? Math.Max(0f, ouchUntil - time) : 0f;
        public float OuchDuration => ouch != null ? ouch.Number("duration") : 0f;

        /// <summary>신난다!(처치·어시스트 뒤 이동·공격 속도)의 남은 시간. 늘어날 수 있다.</summary>
        public float HypeRemaining => hype != null ? Math.Max(0f, hypeUntil - time) : 0f;
        public float HypeDuration => hype != null ? hype.Number("duration") : 0f;

        /// <summary>광전사의 남은 시간.</summary>
        public float BerserkRemaining => berserk != null ? Math.Max(0f, berserkUntil - time) : 0f;
        public float BerserkDuration => berserk != null ? berserk.Number("duration") : 0f;

        /// <summary>무승부의 남은 시간.</summary>
        public float DrawRemaining => draw != null ? Math.Max(0f, drawUntil - time) : 0f;
        public float DrawDuration => draw != null ? draw.Number("duration") : 0f;

        /// <summary>1초에 한 대만! 이 준 무적의 남은 시간.</summary>
        public float OneHitRemaining => oneHit != null ? Math.Max(0f, oneHitUntil - time) : 0f;
        public float OneHitDuration => oneHit != null ? oneHit.Number("invuln_duration") : 0f;

        /// <summary>지구가 멸망해도 5초! — 쓰러지기까지 남은 시간. 버티는 중이 아니면 0.</summary>
        public float LastFiveRemaining => lastFive != null && lastFiveDeathPending
            ? Math.Max(0f, lastFiveDeathAt - time) : 0f;
        public float LastFiveDuration => lastFive != null ? lastFive.Number("invuln_duration") : 0f;

        /// <summary>전투를 시작할 때 보유 골드(탐욕의 공격력 환산).</summary>
        public void SetGold(int gold) => goldSnapshot = Math.Max(0, gold);

        /// <summary>살아 있는 인원을 따져야 하는 효과가 있는가(승부사·이자 폭발 1등).</summary>
        public bool WantsAliveCount => lastStand != null || interestIncome != null;

        /// <summary>
        /// 지금 살아 있는 인원(나 포함). 승부사는 <b>내가 살아 있고</b> 나를 포함해 기준 인원만 남았을 때 한 번 발동한다
        /// (S-GD-85DD2Y). 이자 폭발 t1 은 나 혼자 남으면(1등) 보너스를 받는다.
        /// </summary>
        public void SetAliveCount(int alive)
        {
            // 인원은 쓰러져 있어도 센다 — 참가 인원은 내 생사와 무관하다.
            if (alive > roundPeakAlive)
            {
                roundPeakAlive = alive;
            }

            if (dead || currentHealth <= 0f)
            {
                return;
            }

            if (lastStand != null && !lastStandTriggered && HasMinimumMatchSize(lastStand)
                && alive == (int)lastStand.Number("trigger_alive_count"))
            {
                lastStandTriggered = true;
                pendingHealFraction += Math.Max(0f, lastStand.Number("heal_ratio"));
            }

            if (interestIncome != null && interestDeposited && !firstPlaceGranted && alive == 1)
            {
                firstPlaceGranted = true;
                earnedGold += Math.Max(0, (int)interestIncome.Number("first_place_bonus"));
            }
        }

        /// <summary>
        /// 이 매치가 <c>min_match_size</c> 를 채웠는가. 키가 없으면 제한이 없는 것으로 본다.
        /// </summary>
        /// <remarks>
        /// <b>이 검사가 없으면 소규모 전투에서 곧바로 발동한다.</b> 2인 전투는 시작하자마자 "최후 2인" 이라
        /// 발동 전 대가(받는 피해 증가)는 한 번도 걸리지 않고 보상만 남는다.
        /// </remarks>
        private bool HasMinimumMatchSize(BlessingMvpDefinition entry) =>
            !entry.TryNumber("min_match_size", out float minimum) || roundPeakAlive >= (int)minimum;

        /// <summary>최후 2인 발동 전의 받는 피해 증가. 실제 스탯과 HUD가 같은 조건을 읽는다.</summary>
        public bool LastStandPenaltyActive => lastStand != null && !lastStandTriggered
            && lastStand.Number("pre_damage_taken") > 0f;

        /// <summary>승부사가 발동했는가.</summary>
        public bool LastStandActive => lastStandTriggered;

        // ── 기절 · 몸통 박치기! · 9개의 목숨 (S-GD-9MWJRR) ──────────────────

        /// <summary>
        /// 기절을 건다. 기절은 경직처럼 이동·공격을 막고, <b>소모품도 쓰지 못한다</b> — 경직과 다른 점이다.
        /// </summary>
        public void ApplyStun(float now, float seconds)
        {
            if (!IsFinite(seconds) || seconds <= 0f)
            {
                return;
            }

            ApplyStagger(now, seconds);
            stunUntil = Math.Max(stunUntil, time + seconds);
        }

        /// <summary>기절 중인가 — 소모품·투척물을 쓰지 못한다.</summary>
        public bool IsStunned => time < stunUntil;

        /// <summary>기절의 남은 시간.</summary>
        public float StunRemaining => Math.Max(0f, stunUntil - time);

        /// <summary>몸통 박치기! 가 지금 발동할 수 있는가(재사용 대기가 끝났는가).</summary>
        public bool BodyCheckReady => bodyCheck != null && time >= bodyCheckReadyAt;

        /// <summary>몸통 박치기! 로 부딪힌 적에게 거는 기절 시간.</summary>
        public float BodyCheckStunSeconds => bodyCheck != null ? Math.Max(0f, bodyCheck.Number("stun")) : 0f;

        /// <summary>몸통 박치기! 가 발동했다 — 재사용 대기를 건다.</summary>
        public void MarkBodyCheck(float now)
        {
            if (bodyCheck == null)
            {
                return;
            }

            now = Advance(now);
            bodyCheckReadyAt = now + Math.Max(0f, bodyCheck.Number("internal_cooldown"));
        }

        /// <summary>단단해지기! 가 쌓은 방어력(HUD 중첩 배지).</summary>
        public int SturdyStacks => (int)Math.Round(SturdyBonusDefense);

        /// <summary>9개의 목숨이 정한 최대 체력. 없으면 null — 강화·장신구 최대 체력을 그대로 쓴다.</summary>
        public float? MaxHealthOverride => nineLives != null && nineLives.TryNumber("max_hp_override", out float hp) && hp > 0f
            ? hp : (float?)null;

        /// <summary>9개의 목숨 — 회복할 수 없다.</summary>
        private bool NineLivesBlocksHealing => nineLives != null
            && nineLives.TryNumber("heal_blocked", out float blocked) && blocked > 0f;

        /// <summary>9개의 목숨 — 보호막 합계의 상한. 없으면 무한.</summary>
        private float ShieldCap => nineLives != null && nineLives.TryNumber("shield_cap", out float cap) && cap >= 0f
            ? cap : float.PositiveInfinity;

        /// <summary>9개의 목숨이 준 무적의 남은 시간(HUD).</summary>
        public float NineLivesRemaining => nineLives != null ? Math.Max(0f, nineLivesUntil - time) : 0f;
        public float NineLivesDuration => nineLives != null ? nineLives.Number("invuln_duration") : 0f;

        /// <summary>전투 중에 번 골드(사냥꾼·이자)를 꺼낸다. 전투원이 경제로 넘긴다.</summary>
        public int ConsumeEarnedGold()
        {
            int gold = earnedGold;
            earnedGold = 0;
            return gold;
        }
    }
}

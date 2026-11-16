using System;
using System.Collections.Generic;
using System.Text;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// MVP 축복 중 <b>전투 중에 돌아가는 것</b>의 순수 상태 기계.
    ///
    /// <para>
    /// <b>여기에 게임 오브젝트가 없다.</b> 체력·물리·연출·권위 판정은 전부 어댑터 몫이고,
    /// 이 타입은 "지금 무엇이 켜져 있는가" 와 "이 입력으로 무엇이 바뀌는가" 만 안다.
    /// <c>UnityEngine</c> 을 참조하지 않으며(D-060) 난수도 쓰지 않는다.
    /// </para>
    ///
    /// <para>
    /// <b>수치는 하나도 여기 없다.</b> 모두 <see cref="BlessingMvpCatalog"/> 의
    /// <see cref="BlessingMvpDefinition.Number"/> 에서 읽는다 — 밸런스는 CSV 가 정한다.
    /// </para>
    ///
    /// <para>
    /// <b>호출 순서 계약</b> — 어댑터가 지켜야 한다:
    /// <list type="number">
    ///   <item>라운드 시작에 <see cref="ResetRound"/></item>
    ///   <item>매 프레임 <see cref="Tick"/> (그리고 이동이 있으면 <see cref="NotifyMovement"/>)</item>
    ///   <item>실제로 나간 기본 공격 하나마다 <see cref="BeginAttack"/> → (적중마다 <see cref="ConfirmHit"/>) → <see cref="CompleteAttack"/></item>
    ///   <item>피해를 받을 때 <see cref="BeforeDamage"/> → 체력 반영 → <see cref="AfterDamage"/></item>
    /// </list>
    /// <b><see cref="ConfirmHit"/> 는 권위 판정이 "실제로 유효한 피해가 들어갔다" 고 인정한 뒤에만 부른다.</b>
    /// 장판·표식 폭발·소모품 피해로 다시 부르면 축복이 축복을 발동시키는 재귀가 된다.
    /// </para>
    ///
    /// <para>
    /// 준비 단계 경제 축복(<c>low-buy</c>, <c>reroll-cashback</c>, <c>petty-cash</c>,
    /// <c>blind-bet</c>, <c>dealer-choice</c>)은 <b>여기 없다.</b> 이자 폭발의 전투 창만
    /// 예치 여부를 <see cref="SetInterestDeposited"/> 로 받아 적용한다 — 돈을 빼고 돌려주는 것은
    /// 경제 쪽 일이다.
    /// </para>
    /// </summary>
    public sealed partial class BlessingCombatState
    {
        // ── 효과 키 ──────────────────────────────────────────────────────────
        private const string EffectBladeTip = "blade-tip";
        private const string EffectChipSlide = "chip-slide";
        private const string EffectCornerPocket = "corner-pocket";
        private const string EffectCounter = "counter";
        private const string EffectDeadline = "deadline";
        private const string EffectEmergencyExit = "emergency-exit";
        private const string EffectFixedLine = "fixed-line";
        private const string EffectInsurance = "insurance";
        private const string EffectInterestBurst = "interest-burst";
        private const string EffectLastChip = "last-chip";
        private const string EffectLongDividend = "long-dividend";
        private const string EffectMarkShot = "mark-shot";
        private const string EffectOverheat = "overheat";
        private const string EffectParryDividend = "parry-dividend";
        private const string EffectReverseBlade = "reverse-blade";
        private const string EffectSafeAsset = "safe-asset";
        private const string EffectSlowShot = "slow-shot";
        private const string EffectTripleSlash = "triple-slash";
        private const string EffectChipStack = "chip-stack";
        private const string EffectSmallBet = "small-bet";
        private const string EffectPocketChange = "pocket-change";
        private const string EffectHouseEdge = "house-edge";
        private const string EffectColdStreak = "cold-streak";
        private const string EffectWarmUp = "warm-up";
        private const string EffectTokenToss = "token-toss";
        private const string EffectGuardRail = "guard-rail";
        private const string EffectArrowChange = "arrow-change";
        private const string EffectSightLine = "sight-line";
        private const string EffectHiltGuard = "hilt-guard";
        private const string EffectDrawTempo = "draw-tempo";
        private const string EffectSpareChip = "spare-chip";
        private const string EffectQuietTable = "quiet-table";
        private const string EffectDoubleDown = "double-down";
        private const string EffectSplitPot = "split-pot";
        private const string EffectHotHand = "hot-hand";
        private const string EffectMarginCall = "margin-call";
        private const string EffectStopLoss = "stop-loss";
        private const string EffectEdgeCase = "edge-case";
        private const string EffectBackAlley = "back-alley";
        private const string EffectLongOdds = "long-odds";
        private const string EffectRicochet = "ricochet";
        private const string EffectRiposteLedger = "riposte-ledger";
        private const string EffectSplitShot = "split-shot";
        private const string EffectAllIn = "all-in";
        private const string EffectJackpot = "jackpot";
        private const string EffectHouseCut = "house-cut";
        private const string EffectCreditLine = "credit-line";
        private const string EffectFinalOffer = "final-offer";
        private const string EffectBlindBet = "blind-bet";
        private const string EffectDealerChoice = "dealer-choice";
        private const string EffectTableLimit = "table-limit";
        private const string EffectCrossCut = "cross-cut";
        private const string EffectEdgeDividend = "edge-dividend";
        private const string EffectPiercingBet = "piercing-bet";
        private const string EffectRainCheck = "rain-check";
        private const string EffectCoverFire = "cover-fire";
        private const string EffectCloseQuarters = "close-quarters";

        // 보호막은 출처별로 따로 만료된다. 같은 출처가 다시 들어오면 큰 쪽을 쓴다(누적 아님).
        private const int ShieldSafeAsset = 0;
        private const int ShieldInsurance = 1;
        private const int ShieldSpareChip = 2;
        private const int ShieldParry = 3;
        private const int ShieldSourceCount = 4;

        // 기록은 유한하다. 늦게 도착한 화살을 위해 과거를 무한히 들고 있지 않는다.
        private const int RecordCapacity = 128;
        private const int TargetCapacity = 32;
        private const int DamageSampleCapacity = 64;

        private readonly Dictionary<string, BlessingMvpDefinition> byEffect =
            new Dictionary<string, BlessingMvpDefinition>(StringComparer.Ordinal);

        private readonly HashSet<string> loadoutIds = new HashSet<string>(StringComparer.Ordinal);

        // 구성 비교용 임시 집합. 매 프레임 호출되므로 다시 쓴다.
        private readonly HashSet<string> scratchIds = new HashSet<string>(StringComparer.Ordinal);

        private readonly AttackRecord[] records = new AttackRecord[RecordCapacity];

        private readonly float[] shieldAmount = new float[ShieldSourceCount];
        private readonly float[] shieldExpiry = new float[ShieldSourceCount];

        private readonly float[] damageSampleTime = new float[DamageSampleCapacity];
        private readonly float[] damageSampleAmount = new float[DamageSampleCapacity];
        private int damageSampleHead;
        private int damageSampleCount;
        private float damageSampleSum;

        // 자주 보는 항목은 참조로 들고 있다. null 이면 그 축복이 없다는 뜻이다.
        private BlessingMvpDefinition bladeTip;
        private BlessingMvpDefinition chipSlide;
        private BlessingMvpDefinition cornerPocket;
        private BlessingMvpDefinition counter;
        private BlessingMvpDefinition deadline;
        private BlessingMvpDefinition emergencyExit;
        private BlessingMvpDefinition fixedLine;
        private BlessingMvpDefinition insurance;
        private BlessingMvpDefinition interestBurst;
        private BlessingMvpDefinition lastChip;
        private BlessingMvpDefinition longDividend;
        private BlessingMvpDefinition markShot;
        private BlessingMvpDefinition overheat;
        private BlessingMvpDefinition parry;
        private BlessingMvpDefinition reverseBlade;
        private BlessingMvpDefinition safeAsset;
        private BlessingMvpDefinition slowShot;
        private BlessingMvpDefinition tripleSlash;
        private BlessingMvpDefinition chipStack;
        private BlessingMvpDefinition smallBet;
        private BlessingMvpDefinition pocketChange;
        private BlessingMvpDefinition houseEdge;
        private BlessingMvpDefinition coldStreak;
        private BlessingMvpDefinition warmUp;
        private BlessingMvpDefinition tokenToss;
        private BlessingMvpDefinition guardRail;
        private BlessingMvpDefinition arrowChange;
        private BlessingMvpDefinition sightLine;
        private BlessingMvpDefinition hiltGuard;
        private BlessingMvpDefinition drawTempo;
        private BlessingMvpDefinition spareChip;
        private BlessingMvpDefinition quietTable;
        private BlessingMvpDefinition doubleDown;
        private BlessingMvpDefinition splitPot;
        private BlessingMvpDefinition hotHand;
        private BlessingMvpDefinition marginCall;
        private BlessingMvpDefinition stopLoss;
        private BlessingMvpDefinition edgeCase;
        private BlessingMvpDefinition backAlley;
        private BlessingMvpDefinition longOdds;
        private BlessingMvpDefinition ricochet;
        private BlessingMvpDefinition riposteLedger;
        private BlessingMvpDefinition splitShot;
        private BlessingMvpDefinition allIn;
        private BlessingMvpDefinition jackpot;
        private BlessingMvpDefinition houseCut;
        private BlessingMvpDefinition creditLine;
        private BlessingMvpDefinition finalOffer;
        private BlessingMvpDefinition blindBet;
        private BlessingMvpDefinition dealerChoice;
        private BlessingMvpDefinition tableLimit;
        private BlessingMvpDefinition crossCut;
        private BlessingMvpDefinition edgeDividend;
        private BlessingMvpDefinition piercingBet;
        private BlessingMvpDefinition rainCheck;
        private BlessingMvpDefinition coverFire;
        private BlessingMvpDefinition closeQuarters;

        private float time;
        private float battleElapsed;
        private bool combatActive;
        private bool dead;

        private int generation;
        public bool IsCurrent(in BlessingAttack attack) => attack.Generation == generation;
        private int sequence;

        private float lastAttackTime;
        private float lastCombatTime;
        private bool hasCombatRecord;
        private float lastAbsorbedByShield;

        /// <summary>
        /// 방금 <see cref="BeforeDamage(float,float,float,float,bool)"/> 에서 보호막이 흡수한 양. 다음 <c>AfterDamage</c> 가 비운다 —
        /// 그 전에 읽는다. 보호막만 깎은 타격도 어시스트다(사용자 확정 2026-09-18, S-GD-Y534KH).
        /// </summary>
        public float LastHitAbsorbedByShield => lastAbsorbedByShield;

        private bool deadlineChecked;
        private bool deadlineTriggered;
        private float deadlineEndsAt;

        private bool interestDeposited;

        private int overheatStacks;
        private float overheatStacksUntil;

        private int tripleCombo;
        private float tripleComboUntil;

        private bool reverseArmed;
        private float reverseUntil;

        private float fixedLineChargeStart;
        private float fixedLineDisplacement;

        private float slowShotReadyAt;

        private bool counterCharged;
        private int counterHits;
        private float counterGroupEnd;
        private float counterReadyAt;

        private bool insurancePending;
        private float insuranceShieldAt;
        private float insuranceReadyAt;

        private bool lastChipUsed;
        private float lastChipMoveUntil;
        private float healingBlockedUntil;

        private bool emergencyUsed;
        private bool emergencyDashReset;
        private float emergencyPenaltyUntil;
        private float emergencyMoveUntil;

        // 패링 배당 — 대시 무적으로 적의 공격을 무효화하면 발동한다(엑셀 개편, S-GD-TMPBEF · S-GD-KH2AJ6).
        private float parryCooldownUntil;
        private bool parryBonusPending;
        private float parryBonusUntil;

        private int cornerLastWall = -1;
        private bool cornerHasLastWall;
        private float cornerCooldownUntil;
        private float cornerMoveUntil;
        private bool cornerNextAttackPending;
        private float cornerNextAttackUntil;

        private float staggerUntil;
        private float bladeTipMoveUntil;

        // 신규 MVP 전투 상태. 모든 값은 CSV 정의에서만 읽는다.
        private int chipStacks;
        private float chipStacksUntil;
        private float smallBetReadyAt;
        private float pocketChangeReadyAt;
        private float splitPotReadyAt;
        private float warmUpUntil;
        private bool warmUpTriggered;
        private float tokenTossReadyAt;
        private float guardRailUntil;
        private float guardRailReadyAt;
        private float arrowSpeedUntil;
        private float sightStillSince;
        private float hiltGuardUntil;
        private float hiltGuardReadyAt;
        private float drawTempoUntil;
        private bool spareChipUsed;
        private float doubleDownUntil;
        private float doubleDownReadyAt;
        private float hotHandUntil;
        private int hotHandHits;
        private float hotHandComboUntil;
        private float marginMoveUntil;
        private float stopLossReadyAt;
        private float riposteUntil;
        private float riposteReadyAt;
        private float ricochetReadyAt;
        private float splitShotReadyAt;
        private float allInUntil;
        private float allInReadyAt;
        private bool allInEndHealPending;
        private float pendingHealFraction;
        private readonly List<StatContribution> timedStats = new List<StatContribution>();
        private float jackpotReadyAt;
        private int houseCutStacks;
        private float coverFireUntil;
        private float edgeDividendUntil;
        private int edgeDividendHits;
        private bool edgeDividendPending;
        private float crossCutReadyAt;
        private float rainCheckReadyAt;
        private float currentHealth = 100f;
        private float maxHealth = 100f;
        private float lastHealthSnapshot = 100f;
        private float lastTargetDistance = float.PositiveInfinity;
        private float nearestEnemyDistance = float.PositiveInfinity;
        public void SetNearestEnemyDistance(float distance) => nearestEnemyDistance = distance;
        private float currentAttackRange;
        public void SetCurrentAttackRange(float range) => currentAttackRange = Math.Max(0f, range);
        public bool CloseQuartersActive => closeQuarters != null && currentAttackRange > 0f && nearestEnemyDistance <= currentAttackRange;
        public float CloseQuartersDefense => CloseQuartersActive ? closeQuarters.Number("defense_bonus") : 0f;
        public float CloseQuartersDamage(WeaponTag weapon) => CloseQuartersActive && IsMelee(weapon) ? closeQuarters.Number("damage_bonus") : 0f;
        private float combatDurationSeconds = 90f;

        private const int DelayedDamageCapacity = 16;
        /// <summary>신용 한도의 미룬 피해가 들어오는 간격(초). 1초마다 한 몫씩이다.</summary>
        private const float DelayedDamageTickSeconds = 1f;
        private readonly DelayedDamage[] delayedDamages = new DelayedDamage[DelayedDamageCapacity];

        private bool markActive;
        private int markTarget;
        private float markAppliedAt;

        /// <param name="acquired">
        /// 이번 라운드에 들고 있는 축복. <c>null</c> 이면 빈 목록으로 본다.
        /// MVP 표에 없는 id 와 전투와 무관한 경제 축복은 조용히 버린다.
        /// </param>
        public BlessingCombatState(IEnumerable<BlessingDefinition> acquired)
        {
            for (int i = 0; i < RecordCapacity; i++)
            {
                records[i] = new AttackRecord();
            }

            BuildLoadout(acquired, initializeAdded: false);
            ResetRound(0f);
        }

        /// <summary>이동 속도 <b>가산</b> 보너스의 합. 기본 이동 속도는 여기 없다.</summary>
        public float MoveBonus
        {
            get
            {
                float bonus = 0f;

                if (DeadlineActive)
                {
                    bonus += deadline.Number("move_bonus");
                }

                if (bladeTip != null && time < bladeTipMoveUntil)
                {
                    bonus += bladeTip.Number("move_bonus");
                }

                if (cornerPocket != null && time < cornerMoveUntil)
                {
                    bonus += cornerPocket.Number("move_bonus");
                }

                if (lastChip != null && time < lastChipMoveUntil)
                {
                    bonus += lastChip.Number("move_bonus");
                }

                if (warmUp != null && time < warmUpUntil)
                {
                    bonus += warmUp.Number("move_bonus");
                }

                if (quietTable != null && combatActive && time - lastCombatTime >= quietTable.Number("idle_seconds"))
                {
                    bonus += quietTable.Number("move_bonus");
                }

                if (marginCall != null && time < marginMoveUntil)
                {
                    bonus += marginCall.Number("move_bonus");
                }

                if (FinalOfferActive)
                {
                    bonus += finalOffer.Number("move_bonus");
                }

                // 비상구는 발동하면 대시 충전과 함께 잠깐 빨라진다(엑셀 개편, S-GD-TMPBEF).
                if (emergencyExit != null && time < emergencyMoveUntil)
                {
                    bonus += emergencyExit.Number("move_bonus");
                }

                // 과열은 중첩 하나마다 이동 속도도 오른다(엑셀 개편, S-GD-TMPBEF).
                if (overheat != null && overheatStacks > 0 && time < overheatStacksUntil)
                {
                    bonus += overheatStacks * overheat.Number("move_speed_per_stack");
                }

                // 액티브 축복은 켜진 동안만 — 다른 조건부 보너스와 같이 합연산이다(기획 §1.3).
                if (doubleDown != null && time < doubleDownUntil)
                {
                    bonus += doubleDown.Number("move_bonus");
                }

                if (allIn != null && time < allInUntil)
                {
                    bonus += allIn.Number("move_bonus");
                }

                bonus += ExcelMoveBonus;
                return bonus;
            }
        }

        /// <summary>표시 어댑터가 읽는 현재 시각. 판정용 시계를 외부에서 수정할 수는 없다.</summary>
        public float CurrentTime => time;

        /// <summary>
        /// 이동 속도 <b>승산</b> 배수. 감소만 들어간다(기본 1).
        /// 경직 중에는 0 이다 — 순간이동이 아니라 속도를 0 으로 만드는 방식이다.
        /// </summary>
        public float MovePenaltyMultiplier
        {
            get
            {
                if (time < staggerUntil)
                {
                    return 0f;
                }

                float multiplier = 1f;

                if (coverFire != null && time < coverFireUntil)
                {
                    multiplier *= 1f - coverFire.Number("move_penalty");
                }

                return multiplier;
            }
        }

        /// <summary>과열 중첩이 주는 공격 속도 가산 보너스.</summary>
        public float AttackSpeedBonus
        {
            get
            {
                float bonus = overheat != null && overheatStacks > 0 && time < overheatStacksUntil
                    ? overheatStacks * overheat.Number("attack_speed_per_stack")
                    : 0f;

                if (hotHand != null && time < hotHandUntil)
                {
                    bonus += hotHand.Number("attack_speed");
                }

                if (drawTempo != null && time < drawTempoUntil)
                {
                    bonus += drawTempo.Number("attack_speed");
                }

                bonus += ExcelAttackSpeedBonus;
                return bonus;
            }
        }

        /// <summary>
        /// 이 시각까지 기본 공격을 낼 수 없다 — 경직이다. 과열 빗나감 후딜·패링 실패 대가는 엑셀 개편으로 없어졌다
        /// (S-GD-TMPBEF).
        /// </summary>
        public float AttackBlockedUntil => IsInvulnerable && !InvulnerabilityAllowsAttacking
            ? Math.Max(staggerUntil, invulnerableUntil)
            : staggerUntil;

        /// <summary>살아 있는 보호막의 총량. 출처가 둘이면 합이다.</summary>
        public float Shield
        {
            get
            {
                float total = 0f;

                for (int i = 0; i < ShieldSourceCount; i++)
                {
                    if (shieldAmount[i] > 0f && time < shieldExpiry[i])
                    {
                        total += shieldAmount[i];
                    }
                }

                return total;
            }
        }

        /// <summary>현재 보호막 중 가장 늦게 끝나는 시점까지의 남은 시간.</summary>
        public float ShieldRemaining
        {
            get
            {
                float latest = 0f;
                for (int i = 0; i < ShieldSourceCount; i++)
                {
                    if (shieldAmount[i] > 0f && time < shieldExpiry[i])
                    {
                        latest = Math.Max(latest, shieldExpiry[i] - time);
                    }
                }

                return latest;
            }
        }

        /// <summary>반격이 충전되어 있는가. 다음 기본 공격에서 소모된다(빗나가도 소모).</summary>
        public bool CounterReady => counterCharged;

        /// <summary>사선이 고정되었는가. 다음 활 공격 하나에만 쓰인다.</summary>
        public bool FixedLineReady =>
            fixedLine != null && time - fixedLineChargeStart >= fixedLine.Number("charge_seconds");

        /// <summary>마감 직전이 발동해 있는가.</summary>
        public bool DeadlineActive => deadlineTriggered && combatActive && time < deadlineEndsAt;

        public float DeadlineRemaining => DeadlineActive ? Math.Max(0f, deadlineEndsAt - time) : 0f;
        public float DeadlineDuration => deadline == null ? 0f : deadline.Number("duration");

        /// <summary>이자 폭발 구간인가. 예치하지 않았으면 영원히 <c>false</c> 다.</summary>
        public bool InterestActive
        {
            get
            {
                if (interestBurst == null || !interestDeposited || !combatActive)
                {
                    return false;
                }

                float start = interestBurst.Number("start_seconds");
                return battleElapsed >= start && battleElapsed < start + interestBurst.Number("duration");
            }
        }

        public float InterestRemaining
        {
            get
            {
                if (!InterestActive) return 0f;
                return Math.Max(0f, interestBurst.Number("start_seconds")
                    + interestBurst.Number("duration") - battleElapsed);
            }
        }

        public float InterestDuration => interestBurst == null ? 0f : interestBurst.Number("duration");

        /// <summary>마지막 칩이 발동한 직후의 버프 구간인가.</summary>
        public bool LastChipActive => lastChip != null && time < lastChipMoveUntil;
        public float LastChipRemaining => LastChipActive ? Math.Max(0f, lastChipMoveUntil - time) : 0f;
        public float LastChipDuration => lastChip == null ? 0f : lastChip.Number("duration");

        /// <summary>이 시각까지 어떤 방법으로도 회복할 수 없다. 실제 차단은 어댑터가 한다.</summary>
        public float HealingBlockedUntil => NineLivesBlocksHealing ? float.PositiveInfinity : healingBlockedUntil;
        public float HealingBlockedRemaining => Math.Max(0f, healingBlockedUntil - time);
        public float StaggerRemaining => Math.Max(0f, staggerUntil - time);
        /// <summary>판정에서 쓰는 재사용 가능 시각을 그대로 읽는다. 조회는 시계를 변경하지 않는다.</summary>
        public float CooldownRemaining(string effect)
        {
            if (!byEffect.ContainsKey(effect)) return 0f;
            float readyAt = CooldownReadyAt(effect);
            return float.IsNaN(readyAt) ? 0f : Math.Max(0f, readyAt - time);
        }

        public bool HasCooldownPresentation(string effect) => !float.IsNaN(CooldownReadyAt(effect));

        private float CooldownReadyAt(string effect) => effect switch
            {
                "slow-shot" => slowShotReadyAt,
                "counter" => counterReadyAt,
                "insurance" => insuranceReadyAt,
                "parry-dividend" => parryCooldownUntil,
                "corner-pocket" => cornerCooldownUntil,
                "small-bet" => smallBetReadyAt,
                "pocket-change" => pocketChangeReadyAt,
                "split-pot" => splitPotReadyAt,
                "token-toss" => tokenTossReadyAt,
                "guard-rail" => guardRailReadyAt,
                "hilt-guard" => hiltGuardReadyAt,
                "double-down" => doubleDownReadyAt,
                "stop-loss" => stopLossReadyAt,
                "riposte-ledger" => riposteReadyAt,
                "ricochet" => ricochetReadyAt,
                "split-shot" => splitShotReadyAt,
                "all-in" => allInReadyAt,
                "jackpot" => jackpotReadyAt,
                "cross-cut" => crossCutReadyAt,
                "rain-check" => rainCheckReadyAt,
                "body_check" => bodyCheckReadyAt,
                "escape_dash" => escapeReadyAt,
                "mark-shot" => markActive && markShot != null ? markAppliedAt + markShot.Number("cooldown") : time,
                "berserk" => berserkReadyAt,
                "draw" => drawReadyAt,
                "pickpocket" => pickpocketReadyAt,
                _ => float.NaN,
            };

        public float CooldownDuration(string effect)
        {
            if (!byEffect.TryGetValue(effect, out var definition)) return 0f;
            definition.TryNumber("cooldown", out float cooldown);
            definition.TryNumber("internal_cooldown", out float internalCooldown);
            return Math.Max(cooldown, internalCooldown);
        }

        public IEnumerable<string> CoolingDownEffects
        {
            get
            {
                foreach (var entry in byEffect)
                    if (CooldownRemaining(entry.Key) > 0f) yield return entry.Key;
            }
        }

        public int OverheatStacks => overheatStacks;
        public bool OverheatMaxBonusActive => overheat != null && overheatStacks >= RequiredMaxStacks(overheat)
            && time < overheatStacksUntil;
        public float OverheatRemaining => overheatStacks > 0 ? Math.Max(0f, overheatStacksUntil - time) : 0f;
        public float OverheatDuration => overheat == null ? 0f : overheat.Number("stack_window");
        public bool SafeAssetReady => SafeAssetCharged;
        public bool SlowShotReady => slowShot != null && time >= slowShotReadyAt;
        /// <summary>현재 유지되는 피해 증감의 공통 계약. 공격과 HUD가 함께 읽으며 상태를 소비하지 않는다.</summary>
        /// <remarks>다음 한 발, 확률, 공격 대상/거리 조건은 개별 공격에만 더한다. 시트에는 중복 등록하지 않는다.</remarks>
        public float ActiveDamageBonus(WeaponTag weapon)
        {
            float bonus = 0f;
            if (DeadlineActive) bonus += deadline.Number("damage_bonus");
            if (InterestActive) bonus += interestBurst.Number("damage_bonus");
            if (chipStack != null && ChipStacksRemaining > 0f) bonus += chipStacks * chipStack.Number("damage_per_stack");
            if (ColdStreakActive) bonus += coldStreak.Number("damage_bonus");
            if (houseCut != null) bonus += houseCutStacks * houseCut.Number("kill_bonus");
            if (FinalOfferActive) bonus += finalOffer.Number("damage_bonus");
            if (doubleDown != null && time < doubleDownUntil) bonus += doubleDown.Number("damage_bonus");
            if (allIn != null && time < allInUntil) bonus += allIn.Number("damage_bonus");
            if (OverheatMaxBonusActive) bonus += overheat.Number("max_damage_bonus");
            if (berserk != null && time < berserkUntil) bonus += berserk.Number("damage_bonus");
            if (lastStand != null && lastStandTriggered) bonus += lastStand.Number("stat_bonus");
            if (weapon == WeaponTag.Bow)
            {
                if (sightLine != null && time - sightStillSince >= sightLine.Number("still_seconds"))
                    bonus += sightLine.Number("damage_bonus");
                if (piercingBet != null) bonus += piercingBet.Number("damage_bonus");
            }
            return bonus;
        }

        /// <summary>일시 공격력 비율을 공격력 단계에만 적용한다. 피해 증가와 합산하지 않는다.</summary>
        public float ApplyActiveAttackPower(float attackPower) =>
            attackPower * Math.Max(0f, 1f - (emergencyExit != null && time < emergencyPenaltyUntil
                ? emergencyExit.Number("penalty") : 0f));

        public float CurrentDamageTakenBonus => ActiveDamageTakenBonus(time);

        /// <summary>공격자 종류와 무관하게 현재 유지되는 받는 피해 증감. 다음 피격 발동분은 제외한다.</summary>
        public float ActiveDamageTakenBonus(float now)
        {
            now = Peek(now);
            float bonus = houseEdge != null ? -houseEdge.Number("damage_reduction") : 0f;
            if (coverFire != null && now < coverFireUntil) bonus -= coverFire.Number("damage_reduction");
            if (guardRail != null && now < guardRailUntil) bonus -= guardRail.Number("damage_reduction");
            return bonus;
        }

        public int ChipStacks => chipStacks;
        public float ChipStacksRemaining => chipStacks > 0 ? Math.Max(0f, chipStacksUntil - time) : 0f;
        public float HotHandRemaining => hotHand != null ? Math.Max(0f, hotHandUntil - time) : 0f;
        public float DrawTempoRemaining => drawTempo != null ? Math.Max(0f, drawTempoUntil - time) : 0f;
        public float GuardRailRemaining => guardRail != null ? Math.Max(0f, guardRailUntil - time) : 0f;
        public float HiltGuardRemaining => hiltGuard != null ? Math.Max(0f, hiltGuardUntil - time) : 0f;
        public float ArrowChangeRemaining => arrowChange != null ? Math.Max(0f, arrowSpeedUntil - time) : 0f;
        public float RiposteLedgerRemaining => riposteLedger != null ? Math.Max(0f, riposteUntil - time) : 0f;
        public float CoverFireRemaining => coverFire != null ? Math.Max(0f, coverFireUntil - time) : 0f;
        public bool FinalOfferActive => finalOffer != null && combatActive
            && battleElapsed >= Math.Max(0f, combatDurationSeconds - finalOffer.Number("remaining_seconds"));
        public float FinalOfferRemaining => FinalOfferActive
            ? Math.Max(0f, combatDurationSeconds - battleElapsed) : 0f;
        public float PendingDelayedDamage
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < DelayedDamageCapacity; i++)
                {
                    if (delayedDamages[i] != null)
                    {
                        total += Math.Max(0f, delayedDamages[i].Amount - delayedDamages[i].Delivered);
                    }
                }
                return total;
            }
        }

        // ── 표시 전용 읽기 창 ────────────────────────────────────────────────
        //
        // HUD 효과 칸(CombatBlessingAgent.CaptureHudEffects)이 읽는 것들이다. 위의 기존
        // 속성들과 같은 규칙을 따른다 — **전부 순수 게터**이고 시계·쿨다운·중첩을 건드리지 않는다.
        // 표시가 상태를 소비하면 "보기만 했는데 축복이 사라지는" 증상이 되고, 원인을 찾기 어렵다.
        //
        // 준비(`~Ready`) 계열은 **지금 그 축복이 다음 한 번에 실린다**는 뜻이다. 쿨다운으로 대기
        // 중일 때는 false 라서 HUD 칸이 사라진다 — 상시 패시브처럼 계속 떠 있지 않는다.
        // 판정 조건은 아래 각 사용처(BeginAttack·ConfirmHit·BeforeDamage)와 글자 그대로 같게 둔다.
        // 다르게 적으면 HUD 가 "준비됨"인데 실제로는 안 터지는, 가장 알아채기 어려운 거짓말이 된다.

        /// <summary>소액 베팅이 충전되어 다음 기본 공격의 피해를 올릴 수 있는가.</summary>
        public bool SmallBetReady => smallBet != null && time >= smallBetReadyAt;

        /// <summary>잔돈 회수가 충전되어 다음 적중에서 회복하는가.</summary>
        public bool PocketChangeReady => pocketChange != null && time >= pocketChangeReadyAt;

        /// <summary>
        /// 칩 슬라이드를 들고 있어 대시가 칩 흔적을 남길 수 있는가. 재사용 대기시간은 엑셀 개편으로 없어졌다 —
        /// 제한은 "같은 시점에 최대 <c>max_trails</c> 개" 뿐이고 그것은 흔적을 가진 어댑터가 센다(S-GD-TMPBEF).
        /// </summary>
        public bool ChipSlideReady => chipSlide != null && chipSlide.Number("max_trails") > 0f;

        /// <summary>같은 시점에 남을 수 있는 칩 흔적 수. 칩 슬라이드가 없으면 0.</summary>
        public int ChipSlideMaxTrails =>
            chipSlide != null ? Math.Max(0, (int)chipSlide.Number("max_trails")) : 0;

        /// <summary>토큰 던지기가 충전되어 다음 근접 적중에 경직을 더하는가.</summary>
        public bool TokenTossReady => tokenToss != null && time >= tokenTossReadyAt;

        /// <summary>팟 분배가 충전되어 다음 적중 피해를 주변에 나누는가.</summary>
        public bool SplitPotReady => splitPot != null && time >= splitPotReadyAt;

        /// <summary>도탄 정산이 충전되어 다음 원거리 적중이 한 번 튀는가.</summary>
        public bool RicochetReady => ricochet != null && time >= ricochetReadyAt;

        /// <summary>잭팟이 충전되어 다음 기본 공격이 확률 판정에 들어가는가. <b>확률 자체는 여기서 굴리지 않는다.</b></summary>
        public bool JackpotReady => jackpot != null && time >= jackpotReadyAt;

        /// <summary>십자 베기가 충전되어 다음 검 공격이 광역을 함께 베는가.</summary>
        public bool CrossCutReady => crossCut != null && time >= crossCutReadyAt;

        /// <summary>화살비 예약이 충전되어 다음 활 공격이 화살비를 남기는가.</summary>
        public bool RainCheckReady => rainCheck != null && time >= rainCheckReadyAt;

        /// <summary>
        /// 파생 적중(화살비) 전용 공격 순번을 연다 (S-GD-ND8JZ5). <see cref="BeginAttack(float, WeaponTag)"/> 와 달리
        /// 일회성 효과(다음 공격 보너스·삼연참·역날·사선 고정·반격·화살비 예약 …)를 <b>소모하지 않는다</b> —
        /// 예약당 대상 중복 제거 기록만 새로 연다.
        /// </summary>
        public int BeginDerivedAttack()
        {
            if (sequence == int.MaxValue)
            {
                sequence = 0;
            }

            sequence++;
            records[Slot(sequence)].Begin(sequence);
            return sequence;
        }

        /// <summary>
        /// 고정 회복량 파라미터를 <b>기준</b>에 따라 해석한다 (S-GD-Q75D75): <c>heal_basis=max_health</c> 면 값은
        /// 최대 체력 비율(0~1)이고, 없으면 고정량이다. 이미 비율인 축복(<c>heal_ratio*</c>)은 이 함수를 쓰지 않는다 —
        /// 두 번 곱하지 않는다.
        /// </summary>
        private float ResolveHeal(BlessingMvpDefinition entry, string key)
        {
            float value = Math.Max(0f, entry.Number(key));
            if (entry.TryText(HealBasisKey, out string basis)
                && string.Equals(basis, HealBasisMaxHealth, StringComparison.OrdinalIgnoreCase))
            {
                return maxHealth > 0f ? maxHealth * Math.Min(1f, value) : 0f;
            }

            return value;
        }

        /// <summary>엑셀 파라미터 — 회복 기준 이름(<c>heal_basis</c>)과 그 값(<c>max_health</c>).</summary>
        public const string HealBasisKey = "heal_basis";
        public const string HealBasisMaxHealth = "max_health";

        /// <summary>발사가 아니라 첫 실제 적중에서 준비 상태를 소비한다 (S-GD-PA3S9K).</summary>
        public bool TryConsumeRainCheck(in BlessingAttack attack, float now)
        {
            if (!IsCurrent(attack) || !attack.RainCheckReady || rainCheck == null || now < rainCheckReadyAt)
                return false;
            rainCheckReadyAt = now + rainCheck.Number("cooldown");
            return true;
        }

        /// <summary>분할 사격이 충전되어 다음 활 공격이 화살을 더 쏘는가.</summary>
        public bool SplitShotReady => splitShot != null && time >= splitShotReadyAt;

        /// <summary>손절매가 충전되어 다음 큰 피격을 최대 체력 비율로 자르는가.</summary>
        public bool StopLossReady => stopLoss != null && time >= stopLossReadyAt;

        /// <summary>
        /// 테이블 리밋을 들고 있는가. 재사용 대기시간이 엑셀 개편으로 없어져 <b>상시</b>다 — 그래서 HUD 효과 칸에
        /// 올리지 않는다(상시 패시브, S-UX-WDF6W5).
        /// </summary>
        public bool TableLimitReady => tableLimit != null;

        /// <summary>난간 잡기가 충전되어 다음 피격에서 감소가 열리는가.</summary>
        public bool GuardRailReady => guardRail != null && time >= guardRailReadyAt;

        /// <summary>반격 장부의 창을 다음 피격에서 다시 열 수 있는가.</summary>
        public bool RiposteLedgerReady => riposteLedger != null && time >= riposteReadyAt;

        /// <summary>연패 보정 조건(체력 임계 아래)이 지금 성립하는가.</summary>
        public bool ColdStreakActive => coldStreak != null && maxHealth > 0f
            && currentHealth <= maxHealth * coldStreak.Number("hp_threshold");

        /// <summary>조용한 자리 조건(교전하지 않은 시간)이 지금 성립하는가.</summary>
        public bool QuietTableActive => quietTable != null && combatActive
            && time - lastCombatTime >= quietTable.Number("idle_seconds");

        /// <summary>시야 정렬 조건(제자리 유지)이 지금 성립하는가.</summary>
        public bool SightLineActive => sightLine != null
            && time - sightStillSince >= sightLine.Number("still_seconds");

        /// <summary>몸풀기 이동 보너스의 남은 시간.</summary>
        public float WarmUpRemaining => warmUp != null ? Math.Max(0f, warmUpUntil - time) : 0f;

        /// <summary>마진 콜 이동 보너스의 남은 시간.</summary>
        public float MarginCallRemaining => marginCall != null ? Math.Max(0f, marginMoveUntil - time) : 0f;

        /// <summary>
        /// 코너 포켓의 남은 시간. 이동 보너스와 "다음 공격" 예약이 같은 꺾기에서 함께 열리므로
        /// <b>칸 하나로 묶는다</b> — 같은 그림 두 개가 나란히 뜨면 두 축복으로 읽힌다.
        /// </summary>
        public float CornerPocketRemaining => cornerPocket == null ? 0f
            : Math.Max(
                Math.Max(0f, cornerMoveUntil - time),
                cornerNextAttackPending ? Math.Max(0f, cornerNextAttackUntil - time) : 0f);

        /// <summary>칼끝 계산 이동 보너스의 남은 시간.</summary>
        public float BladeTipRemaining => bladeTip != null ? Math.Max(0f, bladeTipMoveUntil - time) : 0f;

        /// <summary>역날 베기가 장전된 창의 남은 시간. 대시 후 첫 검 공격까지만이다.</summary>
        public float ReverseBladeRemaining =>
            reverseBlade != null && reverseArmed ? Math.Max(0f, reverseUntil - time) : 0f;

        /// <summary>삼연참의 현재 연속 적중 수. 창이 닫혔으면 0 이다.</summary>
        public int TripleSlashStacks => tripleSlash != null && time < tripleComboUntil ? tripleCombo : 0;

        /// <summary>삼연참 연속 창의 남은 시간.</summary>
        public float TripleSlashRemaining =>
            TripleSlashStacks > 0 ? Math.Max(0f, tripleComboUntil - time) : 0f;

        /// <summary>칼날 배당이 다음 검 공격 하나에 실릴 준비가 됐는가.</summary>
        public bool EdgeDividendNextAttackReady => edgeDividend != null && edgeDividendPending;

        /// <summary>칼날 배당의 현재 누적 적중 수. 창이 닫혔으면 0 이다.</summary>
        public int EdgeDividendStacks => edgeDividend != null && time < edgeDividendUntil ? edgeDividendHits : 0;

        /// <summary>칼날 배당 누적 창의 남은 시간.</summary>
        public float EdgeDividendRemaining =>
            EdgeDividendStacks > 0 ? Math.Max(0f, edgeDividendUntil - time) : 0f;

        /// <summary>
        /// 패링 배당이 발동해 <b>다음 검 공격 보너스</b>가 기다리는 남은 시간. 대시 무적으로 공격을 무효화한 순간부터
        /// <c>bonus_seconds</c> 동안이다(S-GD-TMPBEF). 보호막은 <see cref="Shield"/> 가 든다.
        /// </summary>
        public float ParryDividendRemaining => parry != null && parryBonusPending
            ? Math.Max(0f, parryBonusUntil - time) : 0f;

        /// <summary>비상구의 대가(공격력 감소)가 남은 시간.</summary>
        public float EmergencyPenaltyRemaining =>
            emergencyExit != null ? Math.Max(0f, emergencyPenaltyUntil - time) : 0f;

        /// <summary>비상구가 준 이동 속도 증가의 남은 시간(엑셀 개편, S-GD-TMPBEF).</summary>
        public float EmergencyMoveRemaining =>
            emergencyExit != null ? Math.Max(0f, emergencyMoveUntil - time) : 0f;

        /// <summary>보험 약관이 보호막을 내놓기까지 남은 시간. 보호막 자체는 <see cref="Shield"/> 가 든다.</summary>
        public float InsurancePendingRemaining =>
            insurance != null && insurancePending ? Math.Max(0f, insuranceShieldAt - time) : 0f;

        /// <summary>더블 다운 버프의 남은 시간.</summary>
        public float DoubleDownRemaining => doubleDown != null ? Math.Max(0f, doubleDownUntil - time) : 0f;

        /// <summary>더블 다운을 Q/Y 로 다시 켤 수 있는가.</summary>
        public bool DoubleDownReady => doubleDown != null && time >= doubleDownReadyAt;

        /// <summary>올인 버프의 남은 시간.</summary>
        public float AllInRemaining => allIn != null ? Math.Max(0f, allInUntil - time) : 0f;

        /// <summary>올인을 Q/Y 로 다시 켤 수 있는가.</summary>
        public bool AllInReady => allIn != null && time >= allInReadyAt;

        /// <summary>
        /// 켜졌다 꺼지는 효과가 계산표에 얹는 기여의 <b>출처 이름</b>. 전투원이 매 틱 이 이름으로 통째로 바꿔 끼운다
        /// (<see cref="StatSheet.ReplaceFrom"/>) — 효과가 끝나면 빈 목록이 들어가 저절로 걷힌다.
        /// </summary>
        public const string TimedStatSource = "blessing-timed";

        /// <summary>
        /// 지금 켜진 효과가 주는 능력치 기여(합연산). 올인의 공격력·방어구 관통이 여기 있다.
        /// <b>같은 목록을 다시 채워 돌려준다</b> — 매 틱 불려도 새로 만들지 않는다.
        /// </summary>
        public IReadOnlyList<StatContribution> TimedStats
        {
            get
            {
                timedStats.Clear();
                if (allIn != null && time < allInUntil)
                {
                    timedStats.Add(new StatContribution(
                        DamageStatKeys.AttackPower, allIn.Number("atk_flat"), StatStackMode.Add, allIn.Definition.Id));
                    timedStats.Add(new StatContribution(
                        DamageStatKeys.ArmorPen, allIn.Number("armor_pen"), StatStackMode.Add, allIn.Definition.Id));
                }

                // 반격 배당의 방어력 — 엑셀 설명 "방어력이 {defense_bonus} 오르고" 는 조건이 없다. 들고 있는 동안
                // 늘 붙는다(S-GD-KH2AJ6). 엑셀은 특수 효과 키(밑줄)로 적어 능력치로 잡히지 않으므로 여기서 얹는다.
                if (counter != null)
                {
                    timedStats.Add(new StatContribution(
                        DamageStatKeys.Defense, counter.Number("defense_bonus"), StatStackMode.Add,
                        counter.Definition.Id));
                }

                AddExcelTimedStats(timedStats);
                return timedStats;
            }
        }

        /// <summary>
        /// 쌓인 회복(최대 체력 대비 비율)을 꺼낸다 — 올인이 끝날 때(<c>heal_on_end</c>), 무승부 발동, 도망가 대시.
        /// <b>한 번만</b> 준다. 라운드가 바뀌면 사라진다.
        /// </summary>
        public float ConsumePendingHealFraction()
        {
            float fraction = pendingHealFraction;
            pendingHealFraction = 0f;
            return fraction;
        }

        /// <summary>지금 Q/Y 로 켤 수 있는 액티브 축복이 있는가(S-GD-NJQ5NM).</summary>
        /// <summary>
        /// 액티브 축복이 앉는 슬롯의 기본 번호. 지금은 한 번에 하나만 갖는다(S-GD-NJQ5NM).
        /// </summary>
        public const int DefaultActiveSlot = 1;

        /// <summary>이 액티브가 앉은 슬롯 (<c>active_slot</c>). 선언이 없으면 기본 슬롯이다.</summary>
        private static int SlotOf(BlessingMvpDefinition entry) =>
            entry != null && entry.TryNumber("active_slot", out float slot) && slot >= 1f
                ? (int)slot
                : DefaultActiveSlot;

        public bool ActiveBlessingReady => ActiveBlessingReadyIn(DefaultActiveSlot);

        /// <summary>그 슬롯에 준비된 액티브가 있는가.</summary>
        public bool ActiveBlessingReadyIn(int slot) =>
            DoubleDownReady && SlotOf(doubleDown) == slot || AllInReady && SlotOf(allIn) == slot;

        /// <summary>
        /// 준비된 액티브를 켤 때 낼 체력 — <b>최대 체력 대비 비율</b>. 준비된 것이 없으면 0 (S-GD-H8MSNC).
        /// </summary>
        public float ActiveBlessingHealthCost => ActiveBlessingHealthCostIn(DefaultActiveSlot);

        /// <summary>그 슬롯의 준비된 액티브를 켤 때 낼 체력.</summary>
        public float ActiveBlessingHealthCostIn(int slot) =>
            (DoubleDownReady && SlotOf(doubleDown) == slot ? doubleDown.Number("hp_cost") : 0f)
            + (AllInReady && SlotOf(allIn) == slot ? allIn.Number("hp_cost") : 0f);

        /// <summary>
        /// 준비된 액티브를 <b>전부</b> 켠다 — 정상 경로에선 하나뿐이다(뽑기 제한, S-GD-M7D8QT #6).
        /// 재사용 대기는 지금부터 센다. 켤 것이 없으면 <c>false</c>.
        /// </summary>
        /// <remarks>
        /// <b>대가는 받지 않는다.</b> 체력은 전투원이 쥐고 있으므로 부르는 쪽이 <see cref="ActiveBlessingHealthCost"/>
        /// 를 먼저 내고, 낼 수 있을 때만 이것을 부른다(S-GD-H8MSNC).
        /// </remarks>
        public bool ActivateReadyActive(float now) => ActivateReadyActive(now, DefaultActiveSlot);

        /// <summary>
        /// 그 슬롯에 준비된 액티브를 켠다 (<c>active_slot</c>). 슬롯이 다르면 켜지지 않는다.
        /// </summary>
        public bool ActivateReadyActive(float now, int slot)
        {
            // 다른 판정과 같은 시계를 쓴다 — 뒤로 가거나 NaN 인 시각은 마지막 시각으로 본다.
            now = Advance(now);

            bool activated = false;
            if (doubleDown != null && now >= doubleDownReadyAt && SlotOf(doubleDown) == slot)
            {
                doubleDownReadyAt = now + doubleDown.Number("cooldown");
                doubleDownUntil = now + doubleDown.Number("duration");
                activated = true;
            }

            if (allIn != null && now >= allInReadyAt && SlotOf(allIn) == slot)
            {
                allInReadyAt = now + allIn.Number("cooldown");
                allInUntil = now + allIn.Number("duration");
                allInEndHealPending = true;
                activated = true;
            }

            return activated;
        }

        /// <summary>하우스 몫의 현재 처치 중첩. 라운드가 끝나면 0 이다.</summary>
        public int HouseCutStacks => houseCut != null ? houseCutStacks : 0;

        /// <summary>
        /// 신용 한도가 아직 나눠 넣지 않은 피해가 <b>전부 들어오기까지</b> 남은 시간.
        /// 금액은 <see cref="PendingDelayedDamage"/> 가 든다.
        /// </summary>
        public float DelayedDamageRemaining
        {
            get
            {
                float latest = 0f;
                for (int i = 0; i < DelayedDamageCapacity; i++)
                {
                    var delayed = delayedDamages[i];
                    if (delayed == null || delayed.Amount - delayed.Delivered <= 0f)
                    {
                        continue;
                    }

                    latest = Math.Max(latest, delayed.Start + delayed.Duration - time);
                }

                return Math.Max(0f, latest);
            }
        }

        /// <summary>
        /// UI·로그용 한 줄 요약. <b>판정에 쓰지 않는다</b> — 형식은 언제든 바뀐다.
        /// </summary>
        public string ActiveEffectSummary
        {
            get
            {
                var text = new StringBuilder();

                Append(text, DeadlineActive, EffectDeadline);
                Append(text, InterestActive, EffectInterestBurst);
                Append(text, InterestIncomeActive, EffectInterestIncome);
                Append(text, LastChipActive, EffectLastChip);
                Append(text, ParryDividendRemaining > 0f, EffectParryDividend);
                Append(text, CounterReady, EffectCounter);
                Append(text, FixedLineReady, EffectFixedLine);
                Append(text, SafeAssetCharged, EffectSafeAsset);
                Append(text, slowShot != null && time >= slowShotReadyAt, EffectSlowShot);
                Append(text, overheatStacks > 0 && time < overheatStacksUntil, EffectOverheat);
                Append(text, Shield > 0f, "shield");
                Append(text, time < staggerUntil, "stagger");

                return text.ToString();
            }
        }

        // 공격하지 않고 버틴 시간이 기준을 넘으면 안전 자산이 충전된다. 전투 중에만 센다.
        private bool SafeAssetCharged =>
            safeAsset != null && combatActive && time - lastAttackTime >= safeAsset.Number("idle_seconds");

        /// <summary>
        /// 라운드 경계. <b>시계도 여기서 다시 놓인다</b> — 라운드마다 0 부터 세는 어댑터도 받아들인다.
        /// 라운드당 한 번짜리(마지막 칩·비상구)는 여기서만 풀린다.
        /// </summary>
        public void ResetRound(float now)
        {
            if (!IsFinite(now))
            {
                now = 0f;
            }

            time = now;
            battleElapsed = 0f;
            combatActive = false;
            dead = false;

            generation++;

            lastAttackTime = now;
            lastCombatTime = now;
            hasCombatRecord = false;
            lastAbsorbedByShield = 0f;
            Array.Clear(lastShieldAbsorption, 0, lastShieldAbsorption.Length);
            currentHealth = maxHealth > 0f ? maxHealth : 100f;
            lastHealthSnapshot = currentHealth;
            lastTargetDistance = nearestEnemyDistance = float.PositiveInfinity;

            deadlineChecked = false;
            deadlineTriggered = false;
            deadlineEndsAt = now;

            // 예치 여부는 유지한다 — 예치는 준비 단계, 즉 이 경계보다 먼저 일어난다.

            overheatStacks = 0;
            overheatStacksUntil = now;

            tripleCombo = 0;
            tripleComboUntil = now;

            reverseArmed = false;
            reverseUntil = now;

            fixedLineChargeStart = now;
            fixedLineDisplacement = 0f;

            slowShotReadyAt = now;

            counterCharged = false;
            counterHits = 0;
            counterGroupEnd = now;
            counterReadyAt = now;

            insurancePending = false;
            insuranceShieldAt = now;
            insuranceReadyAt = now;
            ClearDamageWindow();

            lastChipUsed = false;
            lastChipMoveUntil = now;
            healingBlockedUntil = now;

            emergencyUsed = false;
            emergencyDashReset = false;
            emergencyPenaltyUntil = now;
            emergencyMoveUntil = now;

            parryCooldownUntil = now;
            parryBonusPending = false;
            parryBonusUntil = now;

            cornerLastWall = -1;
            cornerHasLastWall = false;
            cornerCooldownUntil = now;
            cornerMoveUntil = now;
            cornerNextAttackPending = false;
            cornerNextAttackUntil = now;

            staggerUntil = now;
            bladeTipMoveUntil = now;

            chipStacks = 0;
            chipStacksUntil = now;
            smallBetReadyAt = now;
            pocketChangeReadyAt = now;
            splitPotReadyAt = now;
            warmUpUntil = now;
            warmUpTriggered = false;
            tokenTossReadyAt = now;
            guardRailUntil = now;
            guardRailReadyAt = now;
            arrowSpeedUntil = now;
            sightStillSince = now;
            hiltGuardUntil = now;
            hiltGuardReadyAt = now;
            drawTempoUntil = now;
            spareChipUsed = false;
            doubleDownUntil = now;
            doubleDownReadyAt = now;
            hotHandUntil = now;
            hotHandHits = 0;
            hotHandComboUntil = now;
            marginMoveUntil = now;
            stopLossReadyAt = now;
            riposteUntil = now;
            riposteReadyAt = now;
            ricochetReadyAt = now;
            splitShotReadyAt = now;
            allInUntil = now;
            allInReadyAt = now;
            allInEndHealPending = false;
            pendingHealFraction = 0f;
            damagedBy.Clear();
            ResetExcelEffects(now);
            jackpotReadyAt = now;
            houseCutStacks = 0;
            coverFireUntil = now;
            edgeDividendUntil = now;
            edgeDividendHits = 0;
            edgeDividendPending = false;
            crossCutReadyAt = now;
            rainCheckReadyAt = now;

            for (int i = 0; i < DelayedDamageCapacity; i++)
            {
                delayedDamages[i] = null;
            }

            // 라운드 시계가 다시 0에서 시작해도 과거 표식이 남지 않는다 (S-GD-WSG5CC).
            ClearMark(now);

            for (int i = 0; i < ShieldSourceCount; i++)
            {
                shieldAmount[i] = 0f;
                shieldExpiry[i] = now;
            }

            for (int i = 0; i < RecordCapacity; i++)
            {
                records[i].Clear();
            }
        }

        /// <summary>
        /// 들고 있는 축복이 바뀌었다. <b>같은 구성이면 아무것도 건드리지 않는다</b> —
        /// 매 프레임 불러도 시간과 상태가 그대로다.
        ///
        /// <para>
        /// 바뀌면 사라진 축복의 진행 상태만 지운다. <b>라운드당 한 번짜리는 풀리지 않는다</b> —
        /// 축복을 하나 더 얻었다고 마지막 칩이 다시 차면 라운드 규칙이 무너진다.
        /// </para>
        /// </summary>
        public void ReplaceLoadout(IEnumerable<BlessingDefinition> acquired)
        {
            if (SameLoadout(acquired))
            {
                return;
            }

            BuildLoadout(acquired, initializeAdded: true);

            // 구성이 바뀌면 날아가던 공격은 남의 세대다. 늦게 도착해도 무시된다.
            generation++;
        }

        /// <summary>이 효과를 들고 있는가. 효과 키는 등급이 아니라 동작을 가리킨다.</summary>
        public bool Has(string effect) => effect != null && byEffect.ContainsKey(effect);

        /// <summary>
        /// 상태가 소비하는 MVP 효과 키인지 확인한다. 카탈로그에만 남은 행동 파라미터를
        /// 조용히 완성된 것으로 보고하지 않도록 진단·테스트가 이 목록을 사용한다.
        /// </summary>
        public static IReadOnlyCollection<string> RuntimeEffectKeys { get; } = new[]
        {
            EffectBladeTip, EffectChipSlide, EffectCornerPocket, EffectCounter, EffectDeadline,
            EffectEmergencyExit, EffectFixedLine, EffectInsurance, EffectInterestBurst, EffectLastChip,
            EffectLongDividend, EffectMarkShot, EffectOverheat, EffectParryDividend, EffectReverseBlade,
            EffectSafeAsset, EffectSlowShot, EffectTripleSlash, EffectChipStack, EffectSmallBet,
            EffectPocketChange, EffectHouseEdge, EffectColdStreak, EffectWarmUp, EffectTokenToss,
            EffectGuardRail, EffectArrowChange, EffectSightLine, EffectHiltGuard, EffectDrawTempo,
            EffectSpareChip, EffectQuietTable, EffectDoubleDown, EffectSplitPot, EffectHotHand,
            EffectMarginCall, EffectStopLoss, EffectEdgeCase, EffectBackAlley, EffectLongOdds,
            EffectRicochet, EffectRiposteLedger, EffectSplitShot, EffectAllIn, EffectJackpot,
            EffectHouseCut, EffectCreditLine, EffectFinalOffer, EffectBlindBet, EffectDealerChoice,
            EffectTableLimit, EffectCrossCut, EffectEdgeDividend, EffectPiercingBet, EffectRainCheck,
            EffectCoverFire, EffectCloseQuarters
        };

        /// <summary>전투 어댑터가 현재 체력과 최대 체력을 상태에 전달한다.</summary>
        public void SetHealthSnapshot(float current, float maximum)
        {
            if (IsFinite(current))
            {
                currentHealth = Math.Max(0f, current);
            }

            if (IsFinite(maximum) && maximum > 0f)
            {
                maxHealth = maximum;
            }
        }

        /// <summary>기본 전투 시간(90초)을 사용하는 라운드의 실제 종료 시계를 주입한다.</summary>
        public void SetCombatDuration(float seconds)
        {
            if (IsFinite(seconds) && seconds > 0f)
            {
                combatDurationSeconds = seconds;
            }
        }

        /// <summary>명중 어댑터가 최근 대상과의 거리를 전달한다(공격자의 근접 피해 조건용).</summary>
        public void NotifyTargetDistance(float distance)
        {
            if (IsFinite(distance) && distance >= 0f)
            {
                lastTargetDistance = distance;
            }
        }

        /// <summary>
        /// 신용 한도의 지연 몫을 현재 시점까지 방출한다. 반환값은 어댑터가 실제 피해로 넣는다.
        /// </summary>
        /// <remarks>
        /// <b>1초마다 한 번씩 나눠 들어온다</b> — <c>delay</c> 3 이면 1·2·3초에 미룬 몫의 1/3 씩, 받은 피해의 10% 씩이다
        /// (사용자 확정 2026-09-18, S-GD-KH2AJ6). 그 사이 회복해도 빚은 줄지 않는다. 처치하면 남은 빚이 사라진다.
        /// </remarks>
        public float ConsumeDelayedDamage(float now)
        {
            now = Advance(now);
            float total = 0f;

            for (int i = 0; i < DelayedDamageCapacity; i++)
            {
                var delayed = delayedDamages[i];
                if (delayed == null || now <= delayed.Start)
                {
                    continue;
                }

                int ticks = Math.Max(1, (int)Math.Ceiling(delayed.Duration - 1e-4f));
                int elapsedTicks = delayed.Duration <= 0f
                    ? ticks
                    : Math.Min(ticks, (int)Math.Floor((now - delayed.Start) / DelayedDamageTickSeconds + 1e-4f));
                float progress = (float)elapsedTicks / ticks;
                float due = delayed.Amount * progress - delayed.Delivered;
                if (due > 0f)
                {
                    delayed.Delivered += due;
                    total += due;
                }

                if (progress >= 1f)
                {
                    delayedDamages[i] = null;
                }
            }

            return total;
        }

        /// <summary>
        /// 수치 하나를 읽는다. <b>없으면 던진다</b> — 0 을 돌려주면 오타가 밸런스 버그가 된다.
        /// </summary>
        public float Number(string effect, string key)
        {
            if (effect != null && byEffect.TryGetValue(effect, out var entry))
            {
                return entry.Number(key);
            }

            throw new KeyNotFoundException($"축복 효과 '{effect}' 를 들고 있지 않습니다.");
        }

        /// <summary>
        /// 시간을 흘린다. <paramref name="moving"/> 는 <b>의미 있는 이동</b>이어야 한다 —
        /// 그 판단(속도 임계·경사 미끄러짐 등)은 어댑터가 한다. 물리 변위는 <see cref="NotifyMovement"/> 로 준다.
        ///
        /// <para>
        /// 시계는 뒤로 가지 않는다. 전투가 꺼져 있으면 발동도 충전도 없다.
        /// </para>
        /// </summary>
        public void Tick(float now, float battleElapsed, bool combatActive, bool moving)
        {
            now = Advance(now);

            if (!IsFinite(battleElapsed) || battleElapsed < this.battleElapsed)
            {
                battleElapsed = this.battleElapsed;
            }

            bool startedCombat = combatActive && !this.combatActive;
            this.combatActive = combatActive;
            if (!combatActive) { lastAttackTime = now; ResetFixedLineCharge(now); }

            if (startedCombat && warmUp != null && !warmUpTriggered)
            {
                warmUpTriggered = true;
                warmUpUntil = now + warmUp.Number("duration");
            }

            ExpireOverheat(now);
            ExpireCombo(now);

            if (allInEndHealPending && now >= allInUntil)
            {
                // 올인이 시간을 다 채우고 끝났다 — 끝날 때 한 번 회복한다. 꺼내는 쪽은 전투원이다.
                allInEndHealPending = false;
                pendingHealFraction += allIn != null ? allIn.Number("heal_on_end") : 0f;
            }

            if (moving)
            {
                ResetFixedLineCharge(now);
                sightStillSince = now;
            }

            if (combatActive)
            {
                ResolveInsurance(now);
                TryTriggerDeadline(now, battleElapsed);
            }

            this.battleElapsed = battleElapsed;
            TickExcel(now, battleElapsed, combatActive);
        }

        /// <summary>
        /// 이번 프레임의 실제 변위. 사선 고정은 <b>충전 시작점부터의 누적</b>이 기준을 넘으면 풀린다 —
        /// 한 프레임씩 아주 조금씩 미끄러지는 것으로 조준이 유지되면 안 된다.
        /// </summary>
        public void NotifyMovement(float now, float distanceMoved)
        {
            if (!IsFinite(distanceMoved) || distanceMoved <= 0f)
            {
                return;
            }

            now = Advance(now);
            sightStillSince = now;

            if (fixedLine == null)
            {
                return;
            }

            fixedLineDisplacement += distanceMoved;

            if (fixedLineDisplacement >= fixedLine.Number("movement_threshold"))
            {
                ResetFixedLineCharge(now);
            }
        }

        /// <summary>비상구가 이동기 쿨다운 초기화를 내놓았는가. <b>한 번만 참을 돌려준다.</b></summary>
        public bool ConsumeEmergencyDashReset()
        {
            if (!emergencyDashReset)
            {
                return false;
            }

            emergencyDashReset = false;
            return true;
        }

        /// <summary>
        /// 이자 폭발의 예치 여부. <b>돈은 여기서 움직이지 않는다</b> —
        /// 지불·환급은 경제 쪽이 하고, 여기는 "예치했으니 버프를 줘도 된다" 만 안다.
        /// </summary>
        public void SetInterestDeposited(bool value)
        {
            interestDeposited = value;
        }

        /// <summary>
        /// 이번 발사에 필요한 예고 시간(초). 느린 한 발이 준비되어 있고 활일 때만 0 이 아니다.
        /// <b>상태를 바꾸지 않는다</b> — 쿨다운은 <see cref="BeginAttack"/> 에서만 돈다.
        /// </summary>
        public float WindupSeconds(float now, WeaponTag weapon)
        {
            if (slowShot == null || weapon != WeaponTag.Bow)
            {
                return 0f;
            }

            return Peek(now) >= slowShotReadyAt ? slowShot.Number("windup") : 0f;
        }

        /// <summary>
        /// <b>실제로 나간 기본 공격 하나</b>를 확정한다. 예고·취소된 공격에는 부르지 않는다 —
        /// 여기서 일회성 자원(반격·역날·삼연참·사선·느린 한 발)이 소모된다.
        ///
        /// <para>
        /// <see cref="AttackBlockedUntil"/> 를 여기서 강제하지 않는다. 공격을 낼 수 있는지는 어댑터가 정한다.
        /// </para>
        /// </summary>
        public BlessingAttack PreviewGeometry(WeaponTag weapon)
        {
            float range = 1, penalty = 1, angle = -1, radius = 0, speed = 1;
            float angleReduction = 0, minimumAngle = 0;
            if (weapon == WeaponTag.Sword)
            {
                if (tripleSlash != null && time < tripleComboUntil && tripleCombo >= RequiredComboHits(tripleSlash))
                {
                    range += tripleSlash.Number("range_bonus");
                    angleReduction = tripleSlash.Number("half_angle_reduction");
                    minimumAngle = tripleSlash.Number("min_half_angle");
                }
                if (reverseBlade != null && reverseArmed && time < reverseUntil)
                { penalty *= reverseBlade.Number("range_multiplier"); angle = reverseBlade.Number("half_angle"); }
                if (edgeCase != null)
                { range += edgeCase.Number("range_bonus"); }
            }
            if (weapon == WeaponTag.Bow)
            {
                if (FixedLineReady) { range += fixedLine.Number("range_bonus"); radius += fixedLine.Number("radius"); }
                if (slowShot != null && time >= slowShotReadyAt)
                { speed *= slowShot.Number("speed_multiplier"); radius += slowShot.Number("radius"); }
                if (sightLine != null && time - sightStillSince >= sightLine.Number("still_seconds"))
                { range += sightLine.Number("range_bonus"); }
                if (arrowChange != null && time < arrowSpeedUntil)
                { speed *= 1f + arrowChange.Number("speed_bonus"); }
            }
            int extras = 0;
            float spread = 0f;
            if (weapon == WeaponTag.Bow)
            {
                if (SplitShotReady) { extras += Math.Max(0, (int)splitShot.Number("extra_arrows")); spread = splitShot.Number("spread_degrees"); }
                if (tripleShot != null) { extras += Math.Max(0, (int)tripleShot.Number("extra_projectiles")); spread = Math.Max(spread, 2f * tripleShot.Number("spread_angle")); }
            }
            return new BlessingAttack(0, generation, weapon, 0, 1, range * penalty, angle, speed, radius, 0, 1, false,
                extraProjectileCount: extras, extraProjectileSpreadDegrees: spread,
                halfAngleReduction: angleReduction, minimumHalfAngle: minimumAngle);
        }

        public BlessingAttack BeginAttack(float now, WeaponTag weapon)
        {
            return BeginAttack(now, weapon, currentHealth, maxHealth);
        }

        /// <summary>체력 조건 축복을 포함해 기본 공격 스냅샷을 만든다.</summary>
        public BlessingAttack BeginAttack(float now, WeaponTag weapon, float currentHealth, float maxHealth)
        {
            now = Advance(now);

            if (IsFinite(currentHealth)) this.currentHealth = Math.Max(0f, currentHealth);
            if (IsFinite(maxHealth) && maxHealth > 0f) this.maxHealth = maxHealth;

            if (sequence == int.MaxValue)
            {
                sequence = 0;
            }

            sequence++;

            float damageBonus = ActiveDamageBonus(weapon);
            if (chipStack != null && now >= chipStacksUntil) chipStacks = 0;
            float damagePenalty = 1f;
            float rangeBonus = 0f;
            float rangePenalty = 1f;
            float halfAngle = -1f;
            float halfAngleReduction = 0f, minimumHalfAngle = 0f;
            float speedMultiplier = 1f;
            float radius = 0f;
            float stagger = 0f;
            float recovery = 1f;
            bool counterWave = false;
            float damageMultiplier = 1f;
            float selfHealthCostFraction = 0f;
            int extraProjectileCount = 0;
            float extraProjectileDamageMultiplier = 1f;
            float extraProjectileSpreadDegrees = 0f;
            int pierceTargets = 0;
            bool splitPotReady = false;
            float splitPotShare = 0f;
            float splitPotRadius = 0f;
            bool crossCutReady = false;
            float crossCutDamage = 0f;
            float crossCutRadius = 0f;
            bool rainCheckReady = false;
            int rainCheckArrows = 0;
            float rainCheckDamage = 0f;
            float rainCheckRadius = 0f;
            float rainCheckDelay = 0f;
            float pierceDamageMultiplier = 1f;

            // 안전 자산: 다시 공격하는 순간 피해 감소가 사라지고 보호막으로 바뀐다.
            if (SafeAssetCharged)
            {
                AddShield(
                    ShieldSafeAsset, safeAsset.Number("shield"), now + safeAsset.Number("shield_seconds"), now);
            }

            lastAttackTime = now;

            if (smallBet != null && now >= smallBetReadyAt)
            {
                damageBonus += smallBet.Number("bonus");
                smallBetReadyAt = now + smallBet.Number("cooldown");
            }

            if (riposteLedger != null && weapon == WeaponTag.Sword && now < riposteUntil && now >= riposteReadyAt)
            {
                damageBonus += riposteLedger.Number("damage_bonus");
                riposteReadyAt = now + riposteLedger.Number("cooldown");
            }

            if (edgeDividend != null && weapon == WeaponTag.Sword && edgeDividendPending)
            {
                edgeDividendPending = false;
                damageBonus += edgeDividend.Number("damage_bonus");
            }

            // 더블 다운·올인은 Q/Y 로 켠다(ActivateReadyActive — S-GD-H8MSNC). 공격은 켜진 동안 보너스만 받는다.
            // 대가는 켤 때 한 번 냈으므로 여기서 다시 받지 않는다.

            if (jackpot != null && now >= jackpotReadyAt && DeterministicProc(jackpot.Number("chance"), sequence + generation * 31))
            {
                damageMultiplier *= jackpot.Number("damage_multiplier");
                jackpotReadyAt = now + jackpot.Number("cooldown");
            }

            if (parryBonusPending && weapon == WeaponTag.Sword && now < parryBonusUntil)
            {
                parryBonusPending = false;
                damageBonus += parry.Number("next_attack_bonus");
            }

            if (cornerNextAttackPending && now < cornerNextAttackUntil)
            {
                cornerNextAttackPending = false;
                damageBonus += cornerPocket.Number("next_attack_bonus");
            }

            if (weapon == WeaponTag.Sword)
            {
                if (edgeCase != null) rangeBonus += edgeCase.Number("range_bonus");
                // 손잡이 방어 — 재사용 대기시간이 엑셀 개편으로 생겼다. 켠 순간부터 센다(S-GD-TMPBEF).
                if (hiltGuard != null && now >= hiltGuardReadyAt)
                {
                    hiltGuardUntil = now + hiltGuard.Number("duration");
                    hiltGuardReadyAt = now + hiltGuard.Number("cooldown");
                }
                if (crossCut != null && now >= crossCutReadyAt)
                {
                    crossCutReadyAt = now + crossCut.Number("cooldown");
                    crossCutReady = true;
                    crossCutDamage = crossCut.Number("damage");
                    crossCutRadius = crossCut.Number("radius");
                }
            }

            if (weapon == WeaponTag.Bow)
            {
                if (sightLine != null && now - sightStillSince >= sightLine.Number("still_seconds"))
                {
                    rangeBonus += sightLine.Number("range_bonus");
                    // 엑셀 개편 — 사거리와 함께 피해도 오른다(S-GD-TMPBEF).
                }
                if (arrowChange != null && now < arrowSpeedUntil)
                    speedMultiplier *= 1f + arrowChange.Number("speed_bonus");
                if (splitShot != null && now >= splitShotReadyAt)
                {
                    splitShotReadyAt = now + splitShot.Number("cooldown");
                    extraProjectileCount = Math.Max(0, (int)splitShot.Number("extra_arrows"));
                    extraProjectileDamageMultiplier = splitShot.Number("damage_multiplier");
                    extraProjectileSpreadDegrees = splitShot.Number("spread_degrees");
                }
                if (piercingBet != null)
                {
                    pierceTargets = Math.Max(1, (int)piercingBet.Number("pierce_targets"));
                    pierceDamageMultiplier = Math.Max(0f, piercingBet.Number("damage_multiplier"));
                    // 엑셀 개편 — 활 공격 피해가 오른다. 방어구 관통(armor-pen)은 엑셀 능력치라 계산표가 얹는다.
                }
                if (rainCheck != null && now >= rainCheckReadyAt)
                {
                    rainCheckReady = true;
                    // arrows 는 표현(떨어지는 화살 수) 전용이다 — 피해 횟수와 무관하다 (S-GD-ND8JZ5).
                    rainCheckArrows = Math.Max(1, (int)rainCheck.Number("arrows"));
                    // 고정 피해(damage)는 폐기됐다. 남아 있는 옛 데이터라도 읽어만 두고 판정에 쓰지 않는다 — 기본 화살 피해 모드.
                    rainCheckDamage = rainCheck.TryNumber("damage", out float legacyRainDamage) ? legacyRainDamage : 0f;
                    rainCheckRadius = rainCheck.Number("radius");
                    rainCheckDelay = rainCheck.Number("delay");
                }
            }

            if (splitPot != null && now >= splitPotReadyAt)
            {
                splitPotReady = true;
                splitPotShare = splitPot.Number("share");
                splitPotRadius = splitPot.Number("radius");
                splitPotReadyAt = now + splitPot.Number("cooldown");
            }

            // 삼연참: 앞선 두 번이 맞았으면 이 세 번째가 마무리다. 연속 수는 여기서 초기화된다.
            // 마무리(강화) 타격의 적중은 다음 콤보에 세지 않는다 — 1타→2타→강화→1타→2타→강화 (S-GD-HBA3MZ).
            bool tripleFinisher = false;
            if (tripleSlash != null && weapon == WeaponTag.Sword && now < tripleComboUntil &&
                tripleCombo >= RequiredComboHits(tripleSlash))
            {
                tripleFinisher = true;
                tripleCombo = 0;
                tripleComboUntil = now;

                damageBonus += tripleSlash.Number("damage_bonus");
                rangeBonus += tripleSlash.Number("range_bonus");
                halfAngleReduction = tripleSlash.Number("half_angle_reduction");
                minimumHalfAngle = tripleSlash.Number("min_half_angle");
            }

            // 역날 베기는 형태를 통째로 바꾼다 — 삼연참과 겹치면 이쪽 각도·사거리가 이긴다.
            if (reverseBlade != null && reverseArmed && weapon == WeaponTag.Sword && now < reverseUntil)
            {
                reverseArmed = false;

                damagePenalty *= reverseBlade.Number("damage_multiplier");
                rangePenalty *= reverseBlade.Number("range_multiplier");
                halfAngle = reverseBlade.Number("half_angle");
            }

            if (weapon == WeaponTag.Bow && FixedLineReady)
            {
                rangeBonus += fixedLine.Number("range_bonus");
                radius += fixedLine.Number("radius");
                // 엑셀 개편 — 고정한 한 발은 피해도 오른다(S-GD-TMPBEF).
                damageBonus += fixedLine.Number("damage_bonus");
                ResetFixedLineCharge(now);
            }

            if (weapon == WeaponTag.Bow && slowShot != null && now >= slowShotReadyAt)
            {
                slowShotReadyAt = now + slowShot.Number("cooldown");

                speedMultiplier *= slowShot.Number("speed_multiplier");
                radius += slowShot.Number("radius");
                stagger += slowShot.Number("stagger");
                recovery *= slowShot.Number("recovery_multiplier");
            }

            if (counterCharged)
            {
                counterCharged = false;
                counterWave = true;
                counterReadyAt = now + counter.Number("cooldown");
            }

            ApplyExcelAttack(now, weapon, ref extraProjectileCount, ref extraProjectileSpreadDegrees,
                ref extraProjectileDamageMultiplier);

            records[Slot(sequence)].Begin(sequence);
            // 마무리 타격은 이미 콤보에 반영된 것으로 표시해 ConfirmHit 이 연속 수를 올리지 않게 한다.
            if (tripleFinisher) records[Slot(sequence)].TripleApplied = true;

            return new BlessingAttack(
                sequence,
                generation,
                weapon,
                damageBonus,
                damagePenalty,
                (1f + rangeBonus) * rangePenalty,
                halfAngle,
                speedMultiplier,
                radius,
                stagger,
                recovery,
                counterWave,
                damageMultiplier,
                selfHealthCostFraction,
                extraProjectileCount,
                extraProjectileDamageMultiplier,
                extraProjectileSpreadDegrees,
                pierceTargets,
                splitPotReady,
                splitPotShare,
                splitPotRadius,
                crossCutReady,
                crossCutDamage,
                crossCutRadius,
                rainCheckReady,
                rainCheckArrows,
                rainCheckDamage,
                rainCheckRadius,
                rainCheckDelay,
                pierceDamageMultiplier,
                halfAngleReduction,
                minimumHalfAngle);
        }

        /// <summary>
        /// 거리·각도 보정을 계산한다. <b>순수 함수다</b> — 몇 번을 불러도 상태가 변하지 않는다.
        /// </summary>
        /// <param name="travelDistance">투사체가 날아간 거리(m). 근접이면 의미 없다.</param>
        /// <param name="rangeFraction">유효 사거리 대비 적중 지점 비율(0~1).</param>
        public BlessingHit PreviewHit(in BlessingAttack attack, float travelDistance, float rangeFraction)
        {
            return PreviewHit(attack, 0, travelDistance, rangeFraction, false);
        }

        /// <summary>대상 식별자·후방 여부까지 포함한 공통 명중 보정이다.</summary>
        public BlessingHit PreviewHit(
            in BlessingAttack attack, int target, float travelDistance, float rangeFraction, bool targetBehind,
            float targetRearAngleDegrees = -1f) =>
            PreviewHit(attack, target, travelDistance, rangeFraction, targetBehind, targetRearAngleDegrees,
                TargetContext.Unknown);

        /// <summary>대상과 나를 비교하는 값(체력 비율·공격력)까지 받는 명중 보정이다.</summary>
        public BlessingHit PreviewHit(
            in BlessingAttack attack, int target, float travelDistance, float rangeFraction, bool targetBehind,
            float targetRearAngleDegrees, in TargetContext context)
        {
            float damageBonus = 0f;
            float damagePenalty = 1f;
            float stagger = 0f;
            float damageMultiplier = 1f;

            if (longDividend != null && IsRanged(attack.Weapon) && IsFinite(travelDistance))
            {
                float min = longDividend.Number("min_distance");

                if (travelDistance <= min)
                {
                    // 엑셀은 "0.85배" 로 적지만 스탯 증감은 합연산이다 — ×0.85 가 아니라 Σ 에 −15% 를 더한다(S-GD-SH74SC).
                    damageBonus += longDividend.Number("close_multiplier") - 1f;
                }
                else
                {
                    float max = longDividend.Number("max_distance");
                    float span = max - min;
                    float t = span > 0f ? (travelDistance - min) / span : 1f;

                    if (t > 1f)
                    {
                        t = 1f;
                    }

                    damageBonus += longDividend.Number("max_damage_bonus") * t;
                    stagger += longDividend.Number("max_stagger") * t;
                }
            }

            if (bladeTip != null && attack.Weapon == WeaponTag.Sword && IsFinite(rangeFraction) &&
                rangeFraction >= bladeTip.Number("min_range_fraction"))
            {
                stagger += bladeTip.Number("stagger");
                // 엑셀 개편 — 칼끝으로 맞히면 피해도 오른다(S-GD-TMPBEF).
                damageBonus += bladeTip.Number("damage_bonus");
            }

            // 토큰 던지기 — 준비돼 있으면 이 근접 적중에 추가 피해가 실린다. 재사용 대기는 ConfirmHit 이 돌린다.
            // 추가 피해는 1 단계 flat 이다(공격력 + Σ flat — S-GD-PA3E9T). 고정 피해가 아니다(S-GD-KH2AJ6).
            float flatDamage = TokenTossApplies(attack.Weapon) ? tokenToss.Number("damage") : 0f;

            if (edgeCase != null && attack.Weapon == WeaponTag.Sword && IsFinite(rangeFraction) &&
                rangeFraction >= 0.8f)
            {
                // CSV가 정의한 "늘어난 바깥 구간"을 비율로 표현한 임시 판정값이다.
                damageBonus += edgeCase.Number("damage_bonus");
            }

            // targetRearAngleDegrees 는 대상의 정면 반대 방향을 0도로 둔 실제 평면 각도다.
            // 값이 전달되지 않은 순수 호출은 기존 bool 경계를 유지해 호환한다.
            bool withinBackAlleyAngle = true;
            if (backAlley != null && IsFinite(targetRearAngleDegrees) && targetRearAngleDegrees >= 0f)
            {
                withinBackAlleyAngle = targetRearAngleDegrees <= backAlley.Number("half_angle");
            }
            if (backAlley != null && attack.Weapon == WeaponTag.Sword && targetBehind && withinBackAlleyAngle)
            {
                damageBonus += backAlley.Number("damage_bonus");
            }

            if (longOdds != null && IsRanged(attack.Weapon) && IsFinite(travelDistance) &&
                travelDistance >= longOdds.Number("min_distance") &&
                DeterministicProc(longOdds.Number("crit_chance"), target ^ attack.Sequence * 397))
            {
                damageMultiplier *= longOdds.Number("crit_multiplier");
            }

            if (closeQuarters != null && currentAttackRange > 0f && IsMelee(attack.Weapon) && lastTargetDistance <= currentAttackRange)
            {
                damageBonus += closeQuarters.Number("damage_bonus");
            }

            damageBonus += ExcelTargetDamageBonus(target, context);
            damageBonus += ExcelDistanceDamageBonus(attack.Weapon, travelDistance);
            return new BlessingHit(damageBonus, damagePenalty, stagger, damageMultiplier, flatDamage);
        }

        /// <summary>
        /// 토큰 던지기가 이 공격의 적중에 실리는가 — 근접 무기이고 재사용 대기가 끝났다.
        /// </summary>
        /// <remarks>
        /// 근접 여부는 무기 분류로 본다. 예전에는 <c>WeaponTag.Melee</c> 와 같은지만 봐서, 실제 전투가 넘기는
        /// <c>Sword</c> 로는 한 번도 발동하지 않았다.
        /// </remarks>
        private bool TokenTossApplies(WeaponTag weapon) =>
            tokenToss != null && (weapon == WeaponTag.Melee || IsMelee(weapon)) && time >= tokenTossReadyAt;

        /// <summary>
        /// <b>권위 판정이 인정한 유효 적중</b> 하나를 반영한다.
        /// 같은 공격·같은 대상으로 두 번 부르면 두 번째는 아무것도 하지 않는다.
        ///
        /// <para>
        /// 과열 중첩과 삼연참 연속 수는 <b>공격당 한 번</b>만 오른다 —
        /// 산탄이나 다수 적중이 중첩을 몰아 올리지 않는다.
        /// </para>
        ///
        /// <para>
        /// <b>장판·표식 폭발·소모품 피해로는 부르지 않는다.</b> 축복이 만든 피해가 다시 축복을 발동시킨다.
        /// </para>
        /// </summary>
        public BlessingHitResult ConfirmHit(
            in BlessingAttack attack, int target, float now, float travelDistance, float rangeFraction)
        {
            return ConfirmHit(attack, target, now, travelDistance, rangeFraction, false, 1f);
        }

        public BlessingHitResult ConfirmHit(
            in BlessingAttack attack, int target, float now, float travelDistance, float rangeFraction, bool targetBehind)
        {
            return ConfirmHit(attack, target, now, travelDistance, rangeFraction, targetBehind, 1f);
        }

        /// <summary>실제 무기 기본 피해를 받아 축복이 만든 추가 피해를 절대값으로 계산한다.</summary>
        public BlessingHitResult ConfirmHit(
            in BlessingAttack attack, int target, float now, float travelDistance, float rangeFraction,
            bool targetBehind, float baseDamage, float appliedDamage = -1f,
            float targetRearAngleDegrees = -1f)
        {
            if (attack.Generation != generation)
            {
                return default;
            }

            var record = FindRecord(attack.Sequence);

            if (record == null || record.HasTarget(target)
                || attack.PierceTargets > 0 && record.TargetCount >= attack.PierceTargets
                || !record.TryAddTarget(target))
            {
                return default;
            }

            now = Advance(now);

            record.AnyHit = true;
            lastCombatTime = now;
            hasCombatRecord = true;

            var preview = PreviewHit(attack, target, travelDistance, rangeFraction, targetBehind,
                targetRearAngleDegrees);

            if (overheat != null && !record.OverheatApplied)
            {
                record.OverheatApplied = true;
                AddOverheatStack(now);
            }

            if (tripleSlash != null && attack.Weapon == WeaponTag.Sword && !record.TripleApplied)
            {
                record.TripleApplied = true;
                tripleCombo++;
                tripleComboUntil = now + tripleSlash.Number("window");
            }

            if (bladeTip != null && attack.Weapon == WeaponTag.Sword && IsFinite(rangeFraction) &&
                rangeFraction >= bladeTip.Number("min_range_fraction"))
            {
                bladeTipMoveUntil = now + bladeTip.Number("duration");
            }

            if (chipStack != null)
            {
                int max = Math.Max(1, (int)chipStack.Number("max_stacks"));
                chipStacks = Math.Min(max, chipStacks + 1);
                chipStacksUntil = now + chipStack.Number("stack_duration");
            }

            bool tokenTossTriggered = TokenTossApplies(attack.Weapon);
            if (tokenTossTriggered)
            {
                tokenTossReadyAt = now + tokenToss.Number("cooldown");
            }

            if (attack.Weapon == WeaponTag.Bow)
            {
                if (arrowChange != null)
                    arrowSpeedUntil = now + arrowChange.Number("duration");
                if (drawTempo != null)
                    drawTempoUntil = now + drawTempo.Number("duration");
                if (coverFire != null)
                    coverFireUntil = now + coverFire.Number("duration");
            }

            if (hotHand != null)
            {
                if (now > hotHandComboUntil) hotHandHits = 0;
                hotHandHits++;
                hotHandComboUntil = now + hotHand.Number("window");
                if (hotHandHits >= RequiredHits(hotHand))
                {
                    hotHandHits = 0;
                    hotHandUntil = now + hotHand.Number("duration");
                }
            }

            if (edgeDividend != null && attack.Weapon == WeaponTag.Sword)
            {
                if (now > edgeDividendUntil) edgeDividendHits = 0;
                edgeDividendHits++;
                edgeDividendUntil = now + edgeDividend.Number("window");
                if (edgeDividendHits >= RequiredHits(edgeDividend))
                {
                    edgeDividendHits = 0;
                    edgeDividendPending = true;
                }
            }

            bool markApplied = false;
            bool markDetonated = false;

            if (markShot != null && IsRanged(attack.Weapon))
            {
                ResolveMark(now, target, ref markApplied, ref markDetonated);
            }

            float extraDamage = markDetonated ? markShot.Number("damage") : 0f;
            float healAmount = pocketChange != null && now >= pocketChangeReadyAt
                ? ResolveHeal(pocketChange, "heal") : 0f;
            if (healAmount > 0f)
            {
                pocketChangeReadyAt = now + pocketChange.Number("cooldown");
            }

            // 분할·도탄은 "적중한 피해"의 일부다. Unity 어댑터가 실제 체력 경계에서
            // 측정한 값을 주면 방어력·보호막·피해 감소를 통과한 뒤의 손실을 그대로 쓴다.
            // 순수 도메인 호출에는 예전처럼 공격 스냅샷으로 계산한 예상값을 사용한다.
            float resultDamage = IsFinite(appliedDamage) && appliedDamage >= 0f
                ? appliedDamage
                : Math.Max(0f, baseDamage + preview.FlatDamage)
                    * Math.Max(0f, 1f + attack.DamageBonus + preview.DamageBonus)
                    * Math.Max(0f, attack.DamagePenaltyMultiplier * preview.DamagePenaltyMultiplier)
                    * Math.Max(0f, attack.DamageMultiplier * preview.DamageMultiplier);
            float splashDamage = attack.SplitPotReady && attack.SplitPotShare > 0f
                ? resultDamage * attack.SplitPotShare : 0f;
            bool ricochetTriggered = ricochet != null && IsRanged(attack.Weapon) && now >= ricochetReadyAt;
            float ricochetDamage = ricochetTriggered
                ? resultDamage * ricochet.Number("damage_multiplier") : 0f;
            if (ricochetDamage > 0f) ricochetReadyAt = now + ricochet.Number("cooldown");

            float staggerResult = preview.StaggerSeconds;
            if (tokenTossTriggered)
                staggerResult += tokenToss.Number("stagger");

            return new BlessingHitResult(extraDamage, staggerResult, markApplied, markDetonated,
                healAmount, splashDamage, attack.SplitPotReady ? attack.SplitPotRadius : 0f,
                ricochetDamage, ricochet != null ? ricochet.Number("bounce_range") : 0f, tokenTossTriggered);
        }

        /// <summary>
        /// 공격 하나가 끝났다. 늦게 도착한 화살이 순서를 어겨도 안전하다 —
        /// 세대가 다르면 무시하고, <b>이미 맞은 공격을 빗나감으로 되돌리지 않는다.</b>
        /// </summary>
        public void CompleteAttack(in BlessingAttack attack, float now, bool hit)
        {
            if (attack.Generation != generation)
            {
                return;
            }

            var record = FindRecord(attack.Sequence);

            if (record == null || record.Completed)
            {
                return;
            }

            now = Advance(now);
            record.Completed = true;

            if (hit || record.AnyHit)
            {
                return;
            }

            // 과열은 빗나가도 벌칙이 없다 — 엑셀 개편으로 빗나감 패널티가 삭제됐다. 중첩은 stack_window 안에
            // 다시 맞히지 못하면 저절로 풀린다(ExpireOverheat, S-GD-TMPBEF).

            if (tripleSlash != null)
            {
                tripleCombo = 0;
                tripleComboUntil = now;
            }
        }

        /// <summary>
        /// 체력이 깎이기 <b>전에</b> 부른다. 방어·보호막·마지막 칩을 거친 피해를 돌려준다.
        ///
        /// <para>
        /// 보호막은 출처를 가리지 않고 깎인다(고정 피해 포함). 안전 자산 같은 감소는 <b>적의 주무기 피해에만</b>,
        /// 고정 피해에는 어떤 감소도 걸리지 않는다. 마지막 칩은 죽을 피해 트리거라 <b>출처와 무관하게</b> 버틴다
        /// (엑셀 README — S-GD-PA3E9T).
        /// </para>
        ///
        /// <para>값이 유한하지 않거나 음수면 아무것도 바꾸지 않고 0 을 돌려준다.</para>
        /// </summary>
        public float BeforeDamage(
            float now, float damage, float currentHealth, float maxHealth, bool enemyPrimaryWeapon)
        {
            // 기존 순수 도메인 호출자는 마지막으로 확인한 대상 거리를 넘겨 왔다.
            // 실제 Unity 경계는 아래 오버로드로 피격자의 공격자 거리를 주입한다.
            return BeforeDamage(now, damage, currentHealth, maxHealth, enemyPrimaryWeapon,
                lastTargetDistance);
        }

        /// <summary>
        /// 피격자 기준으로 계산한 공격자와의 실제 거리까지 받아 방어 조건을 판정한다.
        /// 공격자가 없는 환경 피해는 무한 거리로 넣어 근접 방어를 잘못 켜지 않는다.
        /// </summary>
        public float BeforeDamage(
            float now, float damage, float currentHealth, float maxHealth, bool enemyPrimaryWeapon,
            float incomingTargetDistance) =>
            BeforeDamage(now, damage, currentHealth, maxHealth, enemyPrimaryWeapon, incomingTargetDistance,
                fixedDamage: false, firesOnDamaged: true);

        /// <summary>
        /// 피해의 종류까지 받아 판정한다(엑셀 README "피해 계산 순서" — S-GD-PA3E9T).
        /// </summary>
        /// <param name="fixedDamage">
        /// 고정 피해(자기장·독화살·표식 사격). <b>받는 피해 감소·상한은 하나도 걸지 않는다</b> — 통하는 것은 보호막(6-B)과
        /// 죽을 피해 트리거뿐이다.
        /// </param>
        /// <param name="firesOnDamaged">피격 트리거를 돌리는가. DOT·ZONE 은 거짓이다.</param>
        /// <param name="attackerHealthRatio">때린 쪽의 체력 비율(언더독). 모르면 음수.</param>
        /// <param name="fromEnemy">
        /// 다른 전투원이 준 피해인가(칼·화살·축복 폭발·투척물). 신용 한도가 미루는 대상이다. 모르면
        /// <paramref name="enemyPrimaryWeapon"/> 와 같게 본다.
        /// </param>
        public float BeforeDamage(
            float now, float damage, float currentHealth, float maxHealth, bool enemyPrimaryWeapon,
            float incomingTargetDistance, bool fixedDamage, bool firesOnDamaged, float attackerHealthRatio = -1f,
            bool? fromEnemy = null)
        {
            lastHitFiresOnDamaged = firesOnDamaged;
            Array.Clear(lastShieldAbsorption, 0, lastShieldAbsorption.Length);

            if (!IsFinite(damage) || damage < 0f || !IsFinite(currentHealth) || !IsFinite(maxHealth))
            {
                return 0f;
            }

            now = Advance(now);
            this.currentHealth = Math.Max(0f, currentHealth);
            if (maxHealth > 0f) this.maxHealth = maxHealth;
            lastAbsorbedByShield = 0f;

            if (damage == 0f)
            {
                return 0f;
            }

            // 피격은 조준을 흔든다 — 피격 트리거라 도트·자기장은 흔들지 않는다.
            if (firesOnDamaged)
            {
                ResetFixedLineCharge(now);
            }

            if (!fixedDamage)
            {
                // 받는 피해 **감소율**은 여기서 곱하지 않는다 — 계산 4단계에 더해 들어갔다(IncomingDamageTakenBonus).
                damage = ReduceIncoming(now, damage, enemyPrimaryWeapon, incomingTargetDistance);
            }

            // 테이블 리밋 — 한 번에 받는 피해의 상한. 출처를 가리지 않고 고정 피해에도 건다(사용자 확정 2026-09-18,
            // S-GD-KH2AJ6). 재사용 대기는 없다(S-GD-TMPBEF).
            if (tableLimit != null)
            {
                float cap = tableLimit.Number("cap");
                if (damage > cap)
                {
                    damage = cap;
                }
            }

            // 6-B 보호막 — 체력보다 먼저 깎인다. 고정 피해도 깎는다.
            lastAbsorbedByShield = AbsorbWithShields(now, ref damage);

            // 죽을 피해 트리거는 소스와 무관하게 돈다(엑셀 README). 자기장으로 죽어도 마지막 칩이 버틴다.
            if (lastChip != null && !lastChipUsed && currentHealth > 0f &&
                damage >= currentHealth)
            {
                lastChipUsed = true;

                float remaining = lastChip.Number("remaining_hp");
                damage = currentHealth > remaining ? currentHealth - remaining : 0f;

                healingBlockedUntil = now + lastChip.Number("heal_lock");
                lastChipMoveUntil = now + lastChip.Number("duration");
            }

            damage = ApplyLastFive(now, damage);

            // 신용 한도 — 적이 준 피해(칼·화살·축복 폭발·투척물)의 일부를 미룬다. 독·자기장 같은 고정 피해와
            // 미뤄 둔 몫이 나중에 들어오는 피해(공격자 없음)는 다시 미루지 않는다(사용자 확정 2026-09-18).
            if (!fixedDamage && (fromEnemy ?? enemyPrimaryWeapon) && creditLine != null && damage > 0f)
            {
                float delayedFraction = Math.Min(1f, Math.Max(0f, creditLine.Number("delayed_fraction")));
                float delayed = damage * delayedFraction;
                damage -= delayed;
                if (delayed > 0f) EnqueueDelayedDamage(delayed, now, creditLine.Number("delay"));
            }

            return damage;
        }

        /// <summary>
        /// 이 한 대에 붙는 축복의 <b>받는 피해 증감</b>(감소는 음수) — 하우스 엣지·엄호 사격·손잡이 방어·가드레일·
        /// 안전 자산·언더독. 패링 배당은 엑셀 개편으로 피해를 줄이지 않는다(S-GD-TMPBEF). 피해 계산 4단계의 받는 피해 합산에 <b>더한다</b>: 축복끼리도, 시트의 받는 피해(세트·물약·
        /// 광전사 등)와도 곱하지 않는다(S-GD-SH74SC — 곱연산은 공격 속도 감소 패널티뿐). 방어력보다 먼저 들어간다(S-GD-PA3E9T).
        /// </summary>
        /// <remarks>
        /// 고정 피해에는 부르지 않는다. <b>상태를 바꾸지 않는다</b> — 가드레일 창 열기는 뒤이어 부르는
        /// <see cref="BeforeDamage(float, float, float, float, bool, float, bool, bool, float, bool?)"/> 가 한다. 두 곳의 조건은 같아야 한다.
        /// </remarks>
        public float IncomingDamageTakenBonus(float now, bool enemyPrimaryWeapon, float attackerHealthRatio = -1f)
        {
            now = Peek(now);
            float bonus = ActiveDamageTakenBonus(now);

            // 손잡이 방어는 검을 내보낸 뒤 잠깐 열리는 피격 경감이다.
            if (enemyPrimaryWeapon && hiltGuard != null && now < hiltGuardUntil)
            {
                bonus -= hiltGuard.Number("damage_reduction");
            }

            // 가드레일은 이 한 대로 켜질 수도 있다 — 켜는 한 대도 줄어든다(BeforeDamage 가 창을 열고 같은 대에 적용해 왔다).
            if (guardRail != null
                && now >= guardRailUntil && now >= guardRailReadyAt && guardRail.Number("duration") > 0f)
            {
                bonus -= guardRail.Number("damage_reduction");
            }

            if (enemyPrimaryWeapon && safeAsset != null && combatActive
                && now - lastAttackTime >= safeAsset.Number("idle_seconds"))
            {
                bonus -= safeAsset.Number("damage_reduction");
            }

            return bonus + ExcelIncomingBonus(attackerHealthRatio);
        }

        /// <summary>
        /// 축복이 주는 <b>받는 피해 상한·고정 차감</b>과 감소 축복의 발동 처리. 감소율 자체는
        /// <see cref="IncomingDamageTakenBonus"/> 로 계산 4단계에 더해졌다. 고정 피해에는 부르지 않는다(S-GD-PA3E9T).
        /// </summary>
        private float ReduceIncoming(float now, float damage, bool enemyPrimaryWeapon, float incomingTargetDistance)
        {
            if (closeQuarters != null && currentAttackRange > 0f && IsFinite(incomingTargetDistance)
                && incomingTargetDistance <= currentAttackRange)
            {
                damage = Math.Max(0f, damage - closeQuarters.Number("defense_bonus"));
            }

            // 가드레일은 맞으면 켜진다 — 피격 트리거다. 도트·자기장은 고정 피해라 이 단계에 오지 않는다.
            if (guardRail != null && now >= guardRailReadyAt)
            {
                guardRailReadyAt = now + guardRail.Number("cooldown");
                guardRailUntil = now + guardRail.Number("duration");
            }

            if (enemyPrimaryWeapon && stopLoss != null && now >= stopLossReadyAt && maxHealth > 0f)
            {
                float cap = maxHealth * stopLoss.Number("cap_fraction");
                if (damage > cap)
                {
                    damage = cap;
                    stopLossReadyAt = now + stopLoss.Number("cooldown");
                }
            }

            return damage;
        }

        /// <summary>
        /// 체력이 깎인 <b>뒤에</b> 부른다. <paramref name="currentHealth"/> 는 <b>반영 후</b> 체력이다.
        ///
        /// <para>
        /// 보험·반격은 <b>적 주무기로 실제로 들어온 타격</b>만 센다. 보호막이 전부 막아도 타격은 타격이다.
        /// <b>비상구는 다르다</b> — 표가 출처가 아니라 체력 임계만 말하므로 자기장·장판으로 내려가도 발동한다.
        /// 이미 죽었으면 아무것도 발동하지 않는다.
        /// </para>
        /// </summary>
        public void AfterDamage(
            float now, float actualHealthDamage, float currentHealth, float maxHealth, bool enemyPrimaryWeapon)
        {
            if (!IsFinite(actualHealthDamage) || actualHealthDamage < 0f || !IsFinite(currentHealth) ||
                !IsFinite(maxHealth))
            {
                return;
            }

            now = Advance(now);
            this.currentHealth = Math.Max(0f, currentHealth);
            if (maxHealth > 0f) this.maxHealth = maxHealth;

            float absorbed = lastAbsorbedByShield;
            lastAbsorbedByShield = 0f;
            Array.Clear(lastShieldAbsorption, 0, lastShieldAbsorption.Length);

            bool effectiveHit = actualHealthDamage > 0f || absorbed > 0f;

            if (effectiveHit)
            {
                lastCombatTime = now;
                hasCombatRecord = true;
            }

            if (currentHealth <= 0f)
            {
                dead = true;
                insurancePending = false;
                lastHealthSnapshot = 0f;
                return;
            }

            if (!effectiveHit)
            {
                return;
            }

            // 비상구는 피해 출처와 관계없이 생존 체력 임계에 도달하면 라운드당 한 번 발동한다.
            if (emergencyExit != null && !emergencyUsed && maxHealth > 0f &&
                currentHealth <= maxHealth * emergencyExit.Number("hp_threshold"))
            {
                emergencyUsed = true;
                emergencyDashReset = true;
                emergencyPenaltyUntil = now + emergencyExit.Number("penalty_seconds");
                emergencyMoveUntil = now + emergencyExit.Number("duration");
            }

            OnExcelAfterDamage(now, currentHealth, maxHealth);

            if (!enemyPrimaryWeapon)
            {
                if (spareChip != null && !spareChipUsed && maxHealth > 0f &&
                    lastHealthSnapshot > maxHealth * spareChip.Number("hp_threshold") &&
                    currentHealth <= maxHealth * spareChip.Number("hp_threshold"))
                {
                    spareChipUsed = true;
                    AddShield(ShieldSpareChip, spareChip.Number("shield"),
                        now + spareChip.Number("shield_seconds"), now);
                }
                lastHealthSnapshot = currentHealth;
                return;
            }

            if (spareChip != null && !spareChipUsed && maxHealth > 0f &&
                lastHealthSnapshot > maxHealth * spareChip.Number("hp_threshold") &&
                currentHealth <= maxHealth * spareChip.Number("hp_threshold"))
            {
                spareChipUsed = true;
                AddShield(ShieldSpareChip, spareChip.Number("shield"),
                    now + spareChip.Number("shield_seconds"), now);
            }

            if (riposteLedger != null && now >= riposteReadyAt)
            {
                riposteUntil = now + riposteLedger.Number("window");
            }

            if (counter != null && !counterCharged && now >= counterReadyAt && now >= counterGroupEnd)
            {
                // 같은 순간에 몰려 들어온 타격은 한 번으로 묶는다.
                counterGroupEnd = now + counter.Number("hit_group_window");
                counterHits++;

                if (counterHits >= RequiredHits(counter))
                {
                    counterHits = 0;
                    counterCharged = true;
                }
            }

            if (insurance != null && actualHealthDamage > 0f)
            {
                PushDamageSample(now, actualHealthDamage);

                if (!insurancePending && now >= insuranceReadyAt &&
                    damageSampleSum >= insurance.Number("damage_threshold"))
                {
                    insurancePending = true;
                    insuranceShieldAt = now + insurance.Number("delay");
                    insuranceReadyAt = now + insurance.Number("cooldown");
                    ClearDamageWindow();
                }
            }

            lastHealthSnapshot = currentHealth;
        }

        /// <summary>적을 쓰러뜨린 권위 결과를 축복 상태에 전달한다.</summary>
        public float NotifyKill(float now) => NotifyKill(now, NoTarget);

        /// <summary>처치했다. <paramref name="victim"/> 은 쓰러뜨린 전투원(사냥꾼 표식 보상). 모르면 <see cref="NoTarget"/>.</summary>
        /// <returns>이 처치로 회복할 체력(절대값). 없으면 0.</returns>
        public float NotifyKill(float now, int victim) => NotifyKill(now, victim, killingBlowByPrimary: false);

        /// <param name="killingBlowByPrimary">
        /// 내 주무기 적중이 쓰러뜨렸는가. 그 적중이 곧 "마지막으로 때린 적" 이라 사냥꾼 표식은 그 대상에 있다 —
        /// 처치 알림이 표식 갱신보다 먼저 와도 보상이 빠지지 않게 한다(S-GD-Y534KH).
        /// </param>
        public float NotifyKill(float now, int victim, bool killingBlowByPrimary)
        {
            now = Advance(now);
            OnExcelKill(now, victim, killingBlowByPrimary);
            float heal = 0f;
            if (creditLine != null)
            {
                // 신용 한도 — 쓰러뜨리면 남은 빚이 사라지고 최대 체력의 일부를 회복한다(엑셀 개편, S-GD-TMPBEF).
                for (int i = 0; i < DelayedDamageCapacity; i++) delayedDamages[i] = null;
                heal += Math.Max(0f, maxHealth * creditLine.Number("heal_on_kill"));
            }
            if (houseCut != null)
            {
                houseCutStacks = Math.Min(
                    Math.Max(1, (int)houseCut.Number("max_stacks")),
                    houseCutStacks + 1);
            }
            if (marginCall != null)
            {
                marginMoveUntil = now + marginCall.Number("duration");
                heal += ResolveHeal(marginCall, "heal");
            }

            return heal;
        }

        /// <summary>어시스트 인정 창(초). 사망 10초 이내, 경계 포함이다(S-GD-NMFBE9).</summary>
        public const float AssistWindowSeconds = 10f;

        /// <summary>나를 때린 전투원 → 마지막으로 체력을 깎은 시각·순번. 쓰러질 때 처치·어시스트를 가린다.</summary>
        private readonly Dictionary<int, (float At, int Order)> damagedBy = new Dictionary<int, (float, int)>();
        private int damagedByOrder;

        /// <summary>
        /// 다른 전투원이 내 체력을 실제로 깎았다. 쓰러질 때 처치자·어시스트를 가리는 기록이다(S-GD-Y534KH).
        /// 자기장·지연 피해처럼 때린 전투원이 없는 피해는 부르지 않는다. 라운드가 바뀌면 지워진다.
        /// </summary>
        public void RecordDamageFrom(int attacker, float now)
        {
            if (attacker == NoTarget)
            {
                return;
            }

            now = Advance(now);
            damagedBy[attacker] = (now, ++damagedByOrder);
        }

        /// <summary>
        /// 내가 쓰러졌다 — 처치자와 어시스트를 정하고 기록을 비운다(S-GD-NMFBE9 · S-GD-Y534KH).
        /// </summary>
        /// <param name="downingAttacker">쓰러뜨린 피해를 준 전투원. 없으면(자기장 등) <see cref="NoTarget"/>.</param>
        /// <param name="assists">어시스트 전투원을 번호 순으로 담는다. 처치자는 빠진다.</param>
        /// <returns>
        /// 처치자. 쓰러뜨린 피해에 전투원이 없으면 10초 안에 마지막으로 체력을 깎은 사람이다(자기장 사망 규칙,
        /// S-GD-87YH9R). 그런 사람도 없으면 <see cref="NoTarget"/>.
        /// </returns>
        public int ResolveDownCredit(int downingAttacker, float now, List<int> assists)
        {
            now = Advance(now);
            assists?.Clear();

            int killer = downingAttacker;
            if (killer == NoTarget)
            {
                float latest = float.NegativeInfinity;
                int latestOrder = int.MinValue;
                foreach (var pair in damagedBy)
                {
                    if (!WithinAssistWindow(pair.Value.At, now))
                    {
                        continue;
                    }

                    // 같은 시각이면 늦게 기록된 쪽이 가져간다(S-GD-87YH9R 경계).
                    if (pair.Value.At > latest || (pair.Value.At == latest && pair.Value.Order > latestOrder))
                    {
                        latest = pair.Value.At;
                        latestOrder = pair.Value.Order;
                        killer = pair.Key;
                    }
                }
            }

            if (assists != null)
            {
                foreach (var pair in damagedBy)
                {
                    if (pair.Key != killer && WithinAssistWindow(pair.Value.At, now))
                    {
                        assists.Add(pair.Key);
                    }
                }

                assists.Sort();
            }

            damagedBy.Clear();
            return killer;
        }

        private static bool WithinAssistWindow(float damagedAt, float downedAt)
        {
            float age = downedAt - damagedAt;
            return age >= 0f && age <= AssistWindowSeconds;
        }

        /// <summary>
        /// 어시스트를 얻었다 — 쓰러진 적에게 10초 안에 피해를 줬고 처치자가 아니다(S-GD-NMFBE9).
        /// 어시스트로 켜지는 것은 신난다! 뿐이다(S-GD-NN5PR4 #3). 처치 전용 효과(하우스 몫·마진 콜 등)는 켜지 않는다.
        /// </summary>
        public void NotifyAssist(float now, int victim)
        {
            now = Advance(now);
            OnExcelAssist(now, victim);
        }

        /// <summary>
        /// 대시했다. 역날 베기가 여기서 장전되고,
        /// <b>칩 흔적을 놓아야 하면 <c>true</c></b> 를 돌려준다 — 실제 소환·동시 개수 제한(<see cref="ChipSlideMaxTrails"/>)은
        /// 어댑터 몫이다. 칩 슬라이드의 재사용 대기시간은 엑셀 개편으로 없어졌다(S-GD-TMPBEF).
        /// </summary>
        public bool OnDash(float now, WeaponTag weapon)
        {
            now = Advance(now);
            OnExcelDash(now);

            if (reverseBlade != null && weapon == WeaponTag.Sword)
            {
                reverseArmed = true;
                reverseUntil = now + reverseBlade.Number("window");
            }

            return ChipSlideReady;
        }

        /// <summary>
        /// 대시 무적이 적의 공격을 무효화했다 — 패링 배당이 발동한다(엑셀 개편, S-GD-TMPBEF).
        /// 대시가 한 번 충전되고, 다음 검 공격 피해가 오르며, 보호막을 얻는다. 재사용 대기 중이면 아무 일도 없다.
        /// </summary>
        /// <remarks>
        /// 무엇이 "무효화" 인지는 어댑터가 정한다 — 적의 주무기(검·화살)가 대시 무적에 막힌 순간이다(S-GD-KH2AJ6).
        /// 대시 충전은 <see cref="ConsumePendingDashCharges"/> 로 전투원이 꺼내 모터에 준다.
        /// </remarks>
        /// <returns>발동했으면 true.</returns>
        public bool NotifyDashEvade(float now)
        {
            if (parry == null)
            {
                return false;
            }

            now = Advance(now);
            if (now < parryCooldownUntil)
            {
                return false;
            }

            parryCooldownUntil = now + parry.Number("cooldown");
            parryBonusPending = true;
            parryBonusUntil = now + parry.Number("bonus_seconds");
            AddShield(ShieldParry, parry.Number("shield"), now + parry.Number("shield_seconds"), now);
            pendingDashCharges += 1;
            return true;
        }

        /// <summary>
        /// 벽 근처에서 크게 꺾었다. 성공하면 이동·다음 공격 보너스가 붙는다.
        ///
        /// <para>
        /// <b>같은 벽은 한 번 떨어져야 다시 쓴다.</b> <paramref name="wallDistance"/> 가 재장전 거리를 넘으면
        /// 그 벽이 풀린다 — 벽이 없는 경우 <paramref name="wallId"/> 에 -1을 넘긴다. 그 외 음수 ID는 유효하다.
        /// </para>
        /// </summary>
        public bool TryWallTurn(float now, int wallId, float wallDistance, float turnDegrees)
        {
            if (cornerPocket == null || !IsFinite(wallDistance) || !IsFinite(turnDegrees) || wallDistance < 0f)
            {
                return false;
            }

            now = Advance(now);

            if (wallDistance > cornerPocket.Number("wall_reset_distance"))
            {
                if (cornerHasLastWall && (wallId == -1 || wallId == cornerLastWall))
                {
                    cornerHasLastWall = false;
                    cornerLastWall = -1;
                }

                return false;
            }

            if (wallId == -1 || wallDistance > cornerPocket.Number("wall_distance") ||
                turnDegrees <= cornerPocket.Number("turn_degrees") || now < cornerCooldownUntil ||
                (cornerHasLastWall && cornerLastWall == wallId))
            {
                return false;
            }

            cornerHasLastWall = true;
            cornerLastWall = wallId;
            cornerCooldownUntil = now + cornerPocket.Number("cooldown");

            float duration = cornerPocket.Number("duration");
            cornerMoveUntil = now + duration;
            cornerNextAttackUntil = now + duration;
            cornerNextAttackPending = true;

            return true;
        }

        /// <summary>
        /// 경직을 건다. 공격이 막히고 이동 배수가 0 이 된다 — <b>위치를 옮기지는 않는다.</b>
        /// </summary>
        public void ApplyStagger(float now, float seconds)
        {
            if (!IsFinite(seconds) || seconds <= 0f)
            {
                return;
            }

            now = Advance(now);

            float until = now + seconds;

            if (until > staggerUntil)
            {
                staggerUntil = until;
            }
        }

        private void EnqueueDelayedDamage(float amount, float start, float duration)
        {
            for (int i = 0; i < DelayedDamageCapacity; i++)
            {
                if (delayedDamages[i] == null)
                {
                    delayedDamages[i] = new DelayedDamage(amount, start, Math.Max(0f, duration));
                    return;
                }
            }

            // 포화 시 가장 오래된 슬롯을 새 타격으로 덮지 않고, 마지막 슬롯에 합쳐
            // 지연 피해가 조용히 사라지지 않게 한다.
            delayedDamages[DelayedDamageCapacity - 1] ??= new DelayedDamage(0f, start, Math.Max(0f, duration));
            delayedDamages[DelayedDamageCapacity - 1].Amount += amount;
        }

        private static bool DeterministicProc(float chance, int seed) =>
            CombatantStatFormula.RollCritical(chance, seed);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsRanged(WeaponTag weapon) =>
            BlessingWeaponTagCatalog.TryGetCategory(weapon, out var category) && category == WeaponTag.Ranged;

        private static bool IsMelee(WeaponTag weapon) =>
            BlessingWeaponTagCatalog.TryGetCategory(weapon, out var category) && category == WeaponTag.Melee;

        private static int RequiredHits(BlessingMvpDefinition entry)
        {
            int hits = (int)entry.Number("hits");
            return hits < 1 ? 1 : hits;
        }

        // 삼연참은 "앞선 n-1 번이 맞았으면 n 번째가 마무리" 다.
        private static int RequiredComboHits(BlessingMvpDefinition entry) => RequiredHits(entry) - 1;

        private static int Slot(int sequence) => (sequence & int.MaxValue) % RecordCapacity;

        private static void Append(StringBuilder text, bool active, string label)
        {
            if (!active)
            {
                return;
            }

            if (text.Length > 0)
            {
                text.Append(',');
            }

            text.Append(label);
        }

        private float Advance(float now)
        {
            if (!IsFinite(now))
            {
                return time;
            }

            if (now < time)
            {
                now = time;
            }

            time = now;
            return now;
        }

        private float Peek(float now)
        {
            if (!IsFinite(now))
            {
                return time;
            }

            return now < time ? time : now;
        }

        private bool SameLoadout(IEnumerable<BlessingDefinition> acquired)
        {
            scratchIds.Clear();

            if (acquired != null)
            {
                foreach (var definition in acquired)
                {
                    if (definition == null || !BlessingMvpCatalog.TryGet(definition.Id, out _))
                    {
                        continue;
                    }

                    if (!loadoutIds.Contains(definition.Id))
                    {
                        return false;
                    }

                    scratchIds.Add(definition.Id);
                }
            }

            return scratchIds.Count == loadoutIds.Count;
        }

        private void BuildLoadout(IEnumerable<BlessingDefinition> acquired, bool initializeAdded)
        {
            var previous = initializeAdded ? new List<string>(byEffect.Keys) : null;

            byEffect.Clear();
            loadoutIds.Clear();
            sniperTiers.Clear();

            if (acquired != null)
            {
                foreach (var definition in acquired)
                {
                    if (definition == null || !BlessingMvpCatalog.TryGet(definition.Id, out var entry))
                    {
                        continue;
                    }

                    if (!loadoutIds.Add(definition.Id))
                    {
                        continue;
                    }

                    CollectExcelTier(entry);

                    // 같은 효과가 둘이면 먼저 온 것을 쓴다 — MVP 규칙상 중복 획득은 없다.
                    if (!byEffect.ContainsKey(entry.Effect))
                    {
                        byEffect.Add(entry.Effect, entry);
                    }
                }
            }

            byEffect.TryGetValue(EffectBladeTip, out bladeTip);
            byEffect.TryGetValue(EffectChipSlide, out chipSlide);
            byEffect.TryGetValue(EffectCornerPocket, out cornerPocket);
            byEffect.TryGetValue(EffectCounter, out counter);
            byEffect.TryGetValue(EffectDeadline, out deadline);
            byEffect.TryGetValue(EffectEmergencyExit, out emergencyExit);
            byEffect.TryGetValue(EffectFixedLine, out fixedLine);
            byEffect.TryGetValue(EffectInsurance, out insurance);
            byEffect.TryGetValue(EffectInterestBurst, out interestBurst);
            byEffect.TryGetValue(EffectLastChip, out lastChip);
            byEffect.TryGetValue(EffectLongDividend, out longDividend);
            byEffect.TryGetValue(EffectMarkShot, out markShot);
            byEffect.TryGetValue(EffectOverheat, out overheat);
            byEffect.TryGetValue(EffectParryDividend, out parry);
            byEffect.TryGetValue(EffectReverseBlade, out reverseBlade);
            byEffect.TryGetValue(EffectSafeAsset, out safeAsset);
            byEffect.TryGetValue(EffectSlowShot, out slowShot);
            byEffect.TryGetValue(EffectTripleSlash, out tripleSlash);
            byEffect.TryGetValue(EffectChipStack, out chipStack);
            byEffect.TryGetValue(EffectSmallBet, out smallBet);
            byEffect.TryGetValue(EffectPocketChange, out pocketChange);
            byEffect.TryGetValue(EffectHouseEdge, out houseEdge);
            byEffect.TryGetValue(EffectColdStreak, out coldStreak);
            byEffect.TryGetValue(EffectWarmUp, out warmUp);
            byEffect.TryGetValue(EffectTokenToss, out tokenToss);
            byEffect.TryGetValue(EffectGuardRail, out guardRail);
            byEffect.TryGetValue(EffectArrowChange, out arrowChange);
            byEffect.TryGetValue(EffectSightLine, out sightLine);
            byEffect.TryGetValue(EffectHiltGuard, out hiltGuard);
            byEffect.TryGetValue(EffectDrawTempo, out drawTempo);
            byEffect.TryGetValue(EffectSpareChip, out spareChip);
            byEffect.TryGetValue(EffectQuietTable, out quietTable);
            byEffect.TryGetValue(EffectDoubleDown, out doubleDown);
            byEffect.TryGetValue(EffectSplitPot, out splitPot);
            byEffect.TryGetValue(EffectHotHand, out hotHand);
            byEffect.TryGetValue(EffectMarginCall, out marginCall);
            byEffect.TryGetValue(EffectStopLoss, out stopLoss);
            byEffect.TryGetValue(EffectEdgeCase, out edgeCase);
            byEffect.TryGetValue(EffectBackAlley, out backAlley);
            byEffect.TryGetValue(EffectLongOdds, out longOdds);
            byEffect.TryGetValue(EffectRicochet, out ricochet);
            byEffect.TryGetValue(EffectRiposteLedger, out riposteLedger);
            byEffect.TryGetValue(EffectSplitShot, out splitShot);
            byEffect.TryGetValue(EffectAllIn, out allIn);
            byEffect.TryGetValue(EffectJackpot, out jackpot);
            byEffect.TryGetValue(EffectHouseCut, out houseCut);
            byEffect.TryGetValue(EffectCreditLine, out creditLine);
            byEffect.TryGetValue(EffectFinalOffer, out finalOffer);
            byEffect.TryGetValue(EffectBlindBet, out blindBet);
            BindExcelEffects();
            byEffect.TryGetValue(EffectDealerChoice, out dealerChoice);
            byEffect.TryGetValue(EffectTableLimit, out tableLimit);
            byEffect.TryGetValue(EffectCrossCut, out crossCut);
            byEffect.TryGetValue(EffectEdgeDividend, out edgeDividend);
            byEffect.TryGetValue(EffectPiercingBet, out piercingBet);
            byEffect.TryGetValue(EffectRainCheck, out rainCheck);
            byEffect.TryGetValue(EffectCoverFire, out coverFire);
            byEffect.TryGetValue(EffectCloseQuarters, out closeQuarters);

            if (previous == null)
            {
                return;
            }

            for (int i = 0; i < previous.Count; i++)
            {
                if (!byEffect.ContainsKey(previous[i]))
                {
                    ClearRemovedEffect(previous[i]);
                }
            }

            foreach (var effect in byEffect.Keys)
            {
                if (!previous.Contains(effect))
                {
                    InitializeAddedEffect(effect);
                }
            }
        }

        // 사라진 축복이 남긴 진행 상태를 지운다. 라운드당 한 번짜리 소모 기록은 건드리지 않는다.
        private void ClearRemovedEffect(string effect)
        {
            switch (effect)
            {
                case EffectBladeTip:
                    bladeTipMoveUntil = time;
                    break;
                case EffectCornerPocket:
                    cornerMoveUntil = time;
                    cornerNextAttackPending = false;
                    cornerHasLastWall = false;
                    cornerLastWall = -1;
                    break;
                case EffectCounter:
                    counterCharged = false;
                    counterHits = 0;
                    break;
                case EffectDeadline:
                    deadlineTriggered = false;
                    break;
                case EffectFixedLine:
                    fixedLineDisplacement = 0f;
                    break;
                case EffectInsurance:
                    insurancePending = false;
                    ClearDamageWindow();
                    shieldAmount[ShieldInsurance] = 0f;
                    break;
                case EffectLastChip:
                    lastChipMoveUntil = time;
                    break;
                case EffectOverheat:
                    overheatStacks = 0;
                    break;
                case EffectParryDividend:
                    parryBonusPending = false;
                    shieldAmount[ShieldParry] = 0f;
                    break;
                case EffectEmergencyExit:
                    emergencyMoveUntil = time;
                    break;
                case EffectReverseBlade:
                    reverseArmed = false;
                    break;
                case EffectSafeAsset:
                    shieldAmount[ShieldSafeAsset] = 0f;
                    break;
                case EffectTripleSlash:
                    tripleCombo = 0;
                    break;
                case EffectMarkShot:
                    ClearMark(time);
                    break;
                case EffectChipStack:
                    chipStacks = 0;
                    chipStacksUntil = time;
                    break;
                case EffectWarmUp:
                    warmUpUntil = time;
                    warmUpTriggered = false;
                    break;
                case EffectGuardRail:
                    guardRailUntil = time;
                    break;
                case EffectArrowChange:
                    arrowSpeedUntil = time;
                    break;
                case EffectHiltGuard:
                    hiltGuardUntil = time;
                    break;
                case EffectDrawTempo:
                    drawTempoUntil = time;
                    break;
                case EffectSpareChip:
                    spareChipUsed = false;
                    shieldAmount[ShieldSpareChip] = 0f;
                    break;
                case EffectDoubleDown:
                    doubleDownUntil = time;
                    break;
                case EffectHotHand:
                    hotHandHits = 0;
                    hotHandUntil = time;
                    break;
                case EffectMarginCall:
                    marginMoveUntil = time;
                    break;
                case EffectStopLoss:
                    stopLossReadyAt = time;
                    break;
                case EffectRiposteLedger:
                    riposteUntil = time;
                    break;
                case EffectRicochet:
                    ricochetReadyAt = time;
                    break;
                case EffectAllIn:
                    allInUntil = time;
                    allInEndHealPending = false;
                    break;
                case EffectHouseCut:
                    houseCutStacks = 0;
                    break;
                case EffectCreditLine:
                    for (int i = 0; i < DelayedDamageCapacity; i++) delayedDamages[i] = null;
                    break;
                case EffectCoverFire:
                    coverFireUntil = time;
                    break;
                case EffectEdgeDividend:
                    edgeDividendHits = 0;
                    edgeDividendPending = false;
                    break;
                case EffectSplitPot:
                    splitPotReadyAt = time;
                    break;
            }
        }

        // 새로 얻은 축복의 충전은 지금부터 시작한다 — 얻자마자 완충되어 있으면 안 된다.
        private void InitializeAddedEffect(string effect)
        {
            switch (effect)
            {
                case EffectFixedLine:
                    ResetFixedLineCharge(time);
                    break;
                case EffectSafeAsset:
                    lastAttackTime = time;
                    break;
                case EffectSightLine:
                    sightStillSince = time;
                    break;
            }
        }

        private void TryTriggerDeadline(float now, float elapsed)
        {
            if (deadline == null || deadlineChecked)
            {
                return;
            }

            float start = deadline.Number("start_seconds");

            if (battleElapsed >= start || elapsed < start)
            {
                return;
            }

            // 넘어가는 그 순간에만 본다. 늦게 합류해서 놓친 발동은 따라오지 않는다.
            deadlineChecked = true;

            if (hasCombatRecord && now - lastCombatTime <= deadline.Number("recent_hit_window"))
            {
                deadlineTriggered = true;
                deadlineEndsAt = now + deadline.Number("duration");
            }
        }

        private void ExpireOverheat(float now)
        {
            if (overheat == null || overheatStacks == 0)
            {
                return;
            }

            if (now >= overheatStacksUntil)
            {
                overheatStacks = 0;
            }
        }

        private void AddOverheatStack(float now)
        {
            int max = RequiredMaxStacks(overheat);

            if (now >= overheatStacksUntil)
            {
                overheatStacks = 1;
            }
            else if (overheatStacks < max)
            {
                overheatStacks++;
            }

            overheatStacksUntil = now + overheat.Number("stack_window");

            // 최대 중첩 피해 보너스도 이 중첩 창 하나를 따른다(S-GD-CZ9XWH).
        }

        private static int RequiredMaxStacks(BlessingMvpDefinition entry)
        {
            int max = (int)entry.Number("max_stacks");
            return max < 1 ? 1 : max;
        }

        private void ExpireCombo(float now)
        {
            if (tripleSlash == null || tripleCombo == 0)
            {
                return;
            }

            if (now >= tripleComboUntil)
            {
                tripleCombo = 0;
            }
        }

        private void ResolveInsurance(float now)
        {
            if (!insurancePending || dead || now < insuranceShieldAt)
            {
                return;
            }

            insurancePending = false;
            AddShield(
                ShieldInsurance, insurance.Number("shield"), now + insurance.Number("shield_seconds"), now);

            // 엑셀 개편 — 보호막이 생길 때 골드도 받는다. 전투 중 골드는 전투원이 경제로 넘긴다(S-GD-TMPBEF).
            earnedGold += Math.Max(0, (int)insurance.Number("bonus_gold"));
        }

        private void ResetFixedLineCharge(float now)
        {
            fixedLineChargeStart = now;
            fixedLineDisplacement = 0f;
        }

        private void ResolveMark(float now, int target, ref bool markApplied, ref bool markDetonated)
        {
            float markSeconds = markShot.Number("mark_seconds");
            float gap = markShot.Number("cooldown");

            bool alive = markActive && now - markAppliedAt <= markSeconds;

            if (alive && markTarget == target)
            {
                if (now - markAppliedAt < gap)
                {
                    // 같은 순간에 몰린 연속 적중으로는 터지지 않는다. 표식은 그대로 둔다.
                    return;
                }

                // 폭발은 표식을 소비한다. 새 표식은 그다음 적중부터 붙는다 (S-GD-WSG5CC).
                markDetonated = true;
                ClearMark(now);
                return;
            }

            // 표식은 하나뿐이다. 대상을 바꾸면 옮겨 붙는다.
            markActive = true;
            markTarget = target;
            markAppliedAt = now;
            markApplied = true;
        }

        // 표식을 지운다. 라운드 경계와 폭발이 같은 자리를 쓴다.
        private void ClearMark(float now)
        {
            markActive = false;
            markTarget = 0;
            markAppliedAt = now;
        }

        private void AddShield(int source, float amount, float expiry, float now)
        {
            ExpireShields(now);

            if (shieldAmount[source] > 0f)
            {
                // 같은 출처가 다시 들어오면 누적하지 않고 큰 쪽을 쓴다.
                if (shieldAmount[source] > amount)
                {
                    amount = shieldAmount[source];
                }

                if (shieldExpiry[source] > expiry)
                {
                    expiry = shieldExpiry[source];
                }
            }

            // 9개의 목숨 — 보호막 합계가 상한을 넘지 않는다(다른 출처의 남은 보호막을 먼저 센다).
            float others = 0f;
            for (int i = 0; i < ShieldSourceCount; i++)
            {
                if (i != source && shieldAmount[i] > 0f && now < shieldExpiry[i]) others += shieldAmount[i];
            }

            amount = Math.Min(amount, Math.Max(0f, ShieldCap - others));

            shieldAmount[source] = amount;
            shieldExpiry[source] = expiry;
        }

        private void ExpireShields(float now)
        {
            for (int i = 0; i < ShieldSourceCount; i++)
            {
                if (shieldAmount[i] > 0f && now >= shieldExpiry[i])
                {
                    shieldAmount[i] = 0f;
                }
            }
        }

        private float AbsorbWithShields(float now, ref float damage)
        {
            ExpireShields(now);

            float absorbed = 0f;

            // 먼저 사라질 보호막부터 쓴다.
            for (int step = 0; step < ShieldSourceCount && damage > 0f; step++)
            {
                int best = -1;

                for (int i = 0; i < ShieldSourceCount; i++)
                {
                    if (shieldAmount[i] <= 0f)
                    {
                        continue;
                    }

                    if (best < 0 || shieldExpiry[i] < shieldExpiry[best])
                    {
                        best = i;
                    }
                }

                if (best < 0)
                {
                    break;
                }

                float used = shieldAmount[best] < damage ? shieldAmount[best] : damage;

                lastShieldAbsorption[best] += used;
                shieldAmount[best] -= used;
                damage -= used;
                absorbed += used;
            }

            return absorbed;
        }

        private void PushDamageSample(float now, float amount)
        {
            PruneDamageWindow(now);

            if (damageSampleCount == DamageSampleCapacity)
            {
                // 창 안에 이만큼 쌓일 일은 없다. 그래도 무한히 자라지 않게 가장 오래된 것을 버린다.
                damageSampleSum -= damageSampleAmount[damageSampleHead];
                damageSampleHead = (damageSampleHead + 1) % DamageSampleCapacity;
                damageSampleCount--;
            }

            int tail = (damageSampleHead + damageSampleCount) % DamageSampleCapacity;
            damageSampleTime[tail] = now;
            damageSampleAmount[tail] = amount;
            damageSampleCount++;
            damageSampleSum += amount;
        }

        private void PruneDamageWindow(float now)
        {
            if (insurance == null)
            {
                return;
            }

            float oldest = now - insurance.Number("damage_window");

            while (damageSampleCount > 0 && damageSampleTime[damageSampleHead] < oldest)
            {
                damageSampleSum -= damageSampleAmount[damageSampleHead];
                damageSampleHead = (damageSampleHead + 1) % DamageSampleCapacity;
                damageSampleCount--;
            }

            if (damageSampleCount == 0)
            {
                damageSampleSum = 0f;
            }
        }

        private void ClearDamageWindow()
        {
            damageSampleHead = 0;
            damageSampleCount = 0;
            damageSampleSum = 0f;
        }

        private AttackRecord FindRecord(int sequence)
        {
            var record = records[Slot(sequence)];
            return record.Sequence == sequence ? record : null;
        }

        /// <summary>
        /// 공격 하나의 뒷정리 기록. 고정 개수로 돌려 쓴다 —
        /// 오래된 공격은 밀려나고, 밀려난 뒤에 도착한 결과는 무시된다.
        /// </summary>
        private sealed class AttackRecord
        {
            private readonly List<int> targets = new List<int>(4);

            public int Sequence { get; private set; } = -1;

            public bool Completed { get; set; }

            public bool OverheatApplied { get; set; }

            public bool TripleApplied { get; set; }

            public bool AnyHit { get; set; }

            public void Begin(int sequence)
            {
                Sequence = sequence;
                Completed = false;
                OverheatApplied = false;
                TripleApplied = false;
                AnyHit = false;
                targets.Clear();
            }

            public void Clear()
            {
                Sequence = -1;
                Completed = false;
                OverheatApplied = false;
                TripleApplied = false;
                AnyHit = false;
                targets.Clear();
            }

            public bool HasTarget(int target) => targets.Contains(target);
            public int TargetCount => targets.Count;

            public bool TryAddTarget(int target)
            {
                if (targets.Count >= TargetCapacity)
                {
                    return false;
                }

                targets.Add(target);
                return true;
            }
        }

        private sealed class DelayedDamage
        {
            public DelayedDamage(float amount, float start, float duration)
            {
                Amount = Math.Max(0f, amount);
                Start = start;
                Duration = Math.Max(0f, duration);
            }

            public float Amount;
            public float Start;
            public float Duration;
            public float Delivered;
        }
    }
}

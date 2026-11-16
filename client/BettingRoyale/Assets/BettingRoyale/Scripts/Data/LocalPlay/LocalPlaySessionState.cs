using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Remote;
using BlessingWeaponTag = BettingRoyale.Data.Blessing.WeaponTag;
using BettingRoyale.Data.Roulette;

namespace BettingRoyale.Data.LocalPlay
{
    /// <summary>Local Play의 사용자-visible 단계. 온라인 방 상태와 공유하지 않는다.</summary>
    public enum LocalPlayPhase
    {
        Lobby = 0,
        PreparationLoading = 1,
        Preparation = 2,
        CombatLoading = 3,
        CombatCountdown = 4,
        Combat = 5,
        CombatSummary = 6,
    }

    public enum LocalPlayMap
    {
        None = 0,
        Preparation = 1,
        Combat = 2,
    }

    /// <summary>
    /// Local Play 상태를 Unity 런타임 토글로 투영한 값. 컨트롤러는 이 단일 정책만 적용한다.
    /// </summary>
    public readonly struct LocalPlayRuntimePhasePolicy
    {
        private LocalPlayRuntimePhasePolicy(
            bool isSimulationPaused,
            bool participantDamageEnabled,
            bool humanControlEnabled,
            bool botCombatEnabled,
            bool humanAimEnabled,
            bool botAimEnabled,
            bool humanAttackEnabled)
        {
            IsSimulationPaused = isSimulationPaused;
            ParticipantDamageEnabled = participantDamageEnabled;
            HumanControlEnabled = humanControlEnabled;
            BotCombatEnabled = botCombatEnabled;
            HumanAimEnabled = humanAimEnabled;
            BotAimEnabled = botAimEnabled;
            HumanAttackEnabled = humanAttackEnabled;
        }

        public bool IsSimulationPaused { get; }
        public bool ParticipantDamageEnabled { get; }
        public bool HumanControlEnabled { get; }
        public bool BotCombatEnabled { get; }
        public bool HumanAimEnabled { get; }
        public bool BotAimEnabled { get; }

        /// <summary>
        /// 사람 공격 명령을 실행할 수 있는가. 관찰 중에는 이동·조준과 분리해 끈다
        /// (S15P21A606-170) — 피해만 억제하고 공격 연출을 허용하면 관찰 중 확정 사실과
        /// 화면이 갈라질 수 있다.
        /// </summary>
        public bool HumanAttackEnabled { get; }

        public static LocalPlayRuntimePhasePolicy Resolve(
            LocalPlayPhase phase,
            bool canLocalPlayerControl) =>
            Resolve(phase, canLocalPlayerControl, isObservingLastSurvivor: false);

        /// <param name="isObservingLastSurvivor">
        /// 마지막 생존 진영 관찰 중인가 (S15P21A606-170). 참이면 <b>전투 단계 그대로</b>
        /// 이동·시점은 살리고 피해와 봇 전투만 끈다 — 확정된 사실이 더 바뀌지 않게 한다.
        /// </param>
        public static LocalPlayRuntimePhasePolicy Resolve(
            LocalPlayPhase phase,
            bool canLocalPlayerControl,
            bool isObservingLastSurvivor)
        {
            if (isObservingLastSurvivor && phase == LocalPlayPhase.Combat)
            {
                return new LocalPlayRuntimePhasePolicy(
                    isSimulationPaused: false,
                    participantDamageEnabled: false,
                    humanControlEnabled: true,
                    botCombatEnabled: false,
                    humanAimEnabled: true,
                    botAimEnabled: false,
                    humanAttackEnabled: false);
            }

            switch (phase)
            {
                case LocalPlayPhase.Lobby:
                    return Inactive(paused: false);

                case LocalPlayPhase.PreparationLoading:
                case LocalPlayPhase.CombatLoading:
                case LocalPlayPhase.CombatCountdown:
                case LocalPlayPhase.CombatSummary:
                    return Inactive(paused: true);

                case LocalPlayPhase.Preparation:
                    return new LocalPlayRuntimePhasePolicy(
                        isSimulationPaused: false,
                        participantDamageEnabled: false,
                        humanControlEnabled: canLocalPlayerControl,
                        botCombatEnabled: false,
                        humanAimEnabled: canLocalPlayerControl,
                        botAimEnabled: false,
                        humanAttackEnabled: canLocalPlayerControl);

                case LocalPlayPhase.Combat:
                    return new LocalPlayRuntimePhasePolicy(
                        isSimulationPaused: false,
                        participantDamageEnabled: true,
                        humanControlEnabled: true,
                        botCombatEnabled: true,
                        humanAimEnabled: true,
                        botAimEnabled: true,
                        humanAttackEnabled: true);

                default:
                    throw new ArgumentOutOfRangeException(nameof(phase));
            }
        }

        private static LocalPlayRuntimePhasePolicy Inactive(bool paused) =>
            new LocalPlayRuntimePhasePolicy(
                isSimulationPaused: paused,
                participantDamageEnabled: false,
                humanControlEnabled: false,
                botCombatEnabled: false,
                humanAimEnabled: false,
                botAimEnabled: false,
                humanAttackEnabled: false);
    }

    /// <summary>봇 전체의 MVP 시작 무기 구성. 개별 장비 편집은 정식 대기실 범위다.</summary>
    public enum LocalPlayBotWeaponPreset
    {
        Alternating = 0,
        Sword = 1,
        Bow = 2,
    }

    /// <summary>
    /// Local Play 화면이 <see cref="BlessingDraft"/>를 복제 없이 그리도록 만든 불변 투영.
    /// 후보 생성·재추첨·선택 가능 여부는 이 타입이 아니라 Blessing 도메인이 소유한다.
    /// </summary>
    public readonly struct LocalPlayBlessingDraftSnapshot
    {
        public const int Count = BlessingConfig.DefaultCandidateCount;

        /// <param name="isFirstLocked">1번 자리 잠금 여부 (S-GD-S46S28).</param>
        /// <param name="isSecondLocked">2번 자리 잠금 여부.</param>
        /// <param name="isThirdLocked">3번 자리 잠금 여부.</param>
        /// <param name="remainingLockCount">
        /// 아직 잠글 수 있는 개수. <b>여기서 계산하지 않는다</b> — 상한은 <see cref="BlessingConfig.MaxFreeLocks"/>
        /// 하나뿐이어야 하므로 <see cref="BlessingDraft.RemainingLockCount"/> 를 그대로 받는다.
        /// 잠금 정보를 넘기지 않는 옛 호출부(잠금 개념이 없던 화면)를 위해 기본값은 스펙 기본 상한이다.
        /// </param>
        public LocalPlayBlessingDraftSnapshot(
            BlessingDefinition first,
            BlessingDefinition second,
            BlessingDefinition third,
            int rerollCount,
            int currentRerollCost,
            int nextRerollCostPreview,
            int gold,
            RerollResult rerollAvailability,
            bool isFirstLocked = false,
            bool isSecondLocked = false,
            bool isThirdLocked = false,
            int remainingLockCount = BlessingConfig.DefaultMaxFreeLocks)
        {
            First = first ?? throw new ArgumentNullException(nameof(first));
            Second = second ?? throw new ArgumentNullException(nameof(second));
            Third = third ?? throw new ArgumentNullException(nameof(third));
            RerollCount = rerollCount;
            CurrentRerollCost = currentRerollCost;
            NextRerollCostPreview = nextRerollCostPreview;
            Gold = gold;
            RerollAvailability = rerollAvailability;
            IsFirstLocked = isFirstLocked;
            IsSecondLocked = isSecondLocked;
            IsThirdLocked = isThirdLocked;
            RemainingLockCount = Math.Max(0, remainingLockCount);
        }

        public BlessingDefinition First { get; }
        public BlessingDefinition Second { get; }
        public BlessingDefinition Third { get; }
        public int RerollCount { get; }
        public int CurrentRerollCost { get; }
        public int NextRerollCostPreview { get; }
        public int Gold { get; }
        public RerollResult RerollAvailability { get; }
        public bool CanReroll => RerollAvailability == RerollResult.Success;
        public bool IsFirstLocked { get; }
        public bool IsSecondLocked { get; }
        public bool IsThirdLocked { get; }

        /// <summary>아직 잠글 수 있는 개수 (S-GD-S46S28). 0이면 화면이 나머지 잠금 버튼을 흐리게 한다.</summary>
        public int RemainingLockCount { get; }

        /// <summary>지금 잠긴 자리 수.</summary>
        public int LockedCount =>
            (IsFirstLocked ? 1 : 0) + (IsSecondLocked ? 1 : 0) + (IsThirdLocked ? 1 : 0);

        public BlessingDefinition GetAt(int index) => index switch
        {
            0 => First,
            1 => Second,
            2 => Third,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        /// <summary>자리 번호의 잠금 여부. <see cref="GetAt"/> 와 같은 번호 체계다.</summary>
        public bool IsLocked(int index) => index switch
        {
            0 => IsFirstLocked,
            1 => IsSecondLocked,
            2 => IsThirdLocked,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        public bool TryGetIndex(string blessingId, out int index)
        {
            if (!string.IsNullOrWhiteSpace(blessingId))
            {
                for (int i = 0; i < Count; i++)
                {
                    if (string.Equals(GetAt(i).Id, blessingId, StringComparison.Ordinal))
                    {
                        index = i;
                        return true;
                    }
                }
            }

            index = -1;
            return false;
        }
    }

    /// <summary>
    /// MVP에서 효과 연결 전 사용할 축복 정의 데이터. 추첨 규칙은 공용 <see cref="BlessingDraft"/>가 맡는다.
    ///
    /// <para>
    /// <b>등급마다 다른 <c>Id</c> 를 준다</b> — 접미사 <c>-t1</c>/<c>-t2</c>/<c>-t3</c> (S-GD-ZQMB7Y).
    /// <see cref="BlessingDefinition"/> 의 동등성이 <b><c>Id</c> 만 보고 등급은 안 보기</b> 때문에,
    /// 세 등급이 같은 id 를 쓰면 <see cref="BlessingLoadout.Contains"/> 가 상위 등급까지 "이미 가진 것"
    /// 으로 판정한다. 그러면 <b>1라운드에 먹은 축복의 상위 등급이 재추첨을 몇 번 하든 다시 뜨지 않는다</b>
    /// — 후보에서 빠질 뿐 아무 신호가 없어 "확률이 낮은가 보다" 로 오해하기 쉽다.
    /// 2026-09-03 실측으로 확인된 문제다(S-GD-ZQMB7Y 본문).
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>이 카탈로그는 로컬 전용이 아니다.</b> <c>OnlineEconomyCoordinator</c> 가 실제 온라인 매치에
    /// 그대로 넣으므로 <b>id 가 회선에 실린다</b> — 바꾸면 전원이 같은 빌드를 써야 한다.
    /// </para>
    ///
    /// <para>표시명은 등급이 같아도 된다. 등급 구분은 화면이 <see cref="BlessingDefinition.Tier"/> 로 보여준다.</para>
    /// </summary>
    public static class LocalPlayBlessingCatalog
    {
        /// <summary>
        /// 기획 정본을 못 불러왔을 때 쓰는 대역. <b>배치모드(CI·EditMode 테스트)의 정상 경로다</b> —
        /// <c>Bootstrap</c> 이 그때 원격 콘텐츠 준비를 통째로 건너뛴다(D-CLI-CAJZW6).
        /// </summary>
        private static readonly BlessingDefinition[] Fallback = CreateDefinitions();

        /// <summary>
        /// 축복 정본. <c>gamedata.bin</c> 을 불러왔으면 그것을, 아니면 <see cref="Fallback"/> 을 돌려준다
        /// (S15P21A606-201).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>두 피어가 서로 다른 목록을 보면 안 된다.</b> 위 경고대로 id 가 회선에 실리기 때문이다.
        /// 그 어긋남은 <c>Bootstrap</c> 이 막는다 — 데이터를 못 읽으면 <b>진입 자체를 차단</b>하므로
        /// 대역을 든 채 온라인 매치에 들어오는 피어가 없다.
        /// </para>
        /// <para>
        /// ⚠️ 하드코딩 목록을 <b>지우지 않고 대역으로 남긴 이유</b>는 배치모드에 정본이 없기 때문이다.
        /// 지우면 <c>BlessingDraft</c> 가 후보 3개를 못 채워 EditMode 테스트가 통째로 깨진다.
        /// </para>
        /// </remarks>
        public static IReadOnlyList<BlessingDefinition> All => FilterDisabled(
            GameContentProvider.IsLoaded && GameContentProvider.Current.Blessings.Count > 0
                ? GameContentProvider.Current.Blessings
                : Fallback);

        private static IReadOnlyList<BlessingDefinition> filteredSource;
        private static IReadOnlyList<BlessingDefinition> filteredResult;

        /// <summary>
        /// constants.DISABLED_BLESSING_IDS 에 적힌 축복을 후보 목록에서 뺀다 (S-GD-3DEQ2M — 임시 비활성 정책).
        /// 제품·Local Play·훈련이 모두 이 목록으로 추첨하므로 한 곳에서 거른다. 정의·ID 는 지우지 않는다.
        /// </summary>
        private static IReadOnlyList<BlessingDefinition> FilterDisabled(IReadOnlyList<BlessingDefinition> source)
        {
            var balance = BettingRoyale.Data.Balance.BalanceCatalogs.Current;
            if (balance.DisabledBlessingIds.Count == 0)
            {
                return source;
            }

            if (ReferenceEquals(source, filteredSource) && filteredResult != null)
            {
                return filteredResult;
            }

            var kept = new List<BlessingDefinition>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                if (!balance.IsBlessingDisabled(source[i].Id))
                {
                    kept.Add(source[i]);
                }
            }

            filteredSource = source;
            filteredResult = kept;
            return kept;
        }

        public static BlessingWeaponTag ResolveWeaponTag(WeaponType weapon) => weapon switch
        {
            WeaponType.Sword => BlessingWeaponTag.Sword,
            WeaponType.Bow => BlessingWeaponTag.Bow,
            _ => throw new ArgumentOutOfRangeException(nameof(weapon)),
        };

        private static BlessingDefinition[] CreateDefinitions()
        {
            var result = new List<BlessingDefinition>(18);
            BlessingEffectCatalog effects = PlaceholderBlessingBalance.CreateEffectCatalog();
            BlessingTier[] tiers =
            {
                BlessingTier.Silver,
                BlessingTier.Gold,
                BlessingTier.Platinum,
            };
            for (int i = 0; i < tiers.Length; i++)
            {
                BlessingTier tier = tiers[i];

                // 등급 접미사. 등급 이름(실버·골드·플래티넘)을 id 에 넣지 않는다 (S-GD-ZQMB7Y).
                // 계열은 접미사를 뗀 앞부분이다 — steel-heart-t2 의 계열은 steel-heart 다.
                string tierSuffix = $"-t{i + 1}";

                result.Add(new BlessingDefinition($"steel-heart{tierSuffix}", "강철 심장", BlessingWeaponTag.Common, tier));
                result.Add(new BlessingDefinition($"quick-footwork{tierSuffix}", "신속한 발놀림", BlessingWeaponTag.Common, tier));
                result.Add(new BlessingDefinition($"precision-aim{tierSuffix}", "정밀 조준", BlessingWeaponTag.Bow, tier));
                result.Add(new BlessingDefinition($"combat-instinct{tierSuffix}", "전투 본능", BlessingWeaponTag.Common, tier));
                result.Add(new BlessingDefinition($"guardians-oath{tierSuffix}", "수호의 맹세", BlessingWeaponTag.Sword, tier));
                result.Add(new BlessingDefinition($"weak-point-analysis{tierSuffix}", "약점 포착", BlessingWeaponTag.Common, tier));
            }

            // Local/Online 모두 이 카탈로그의 Description을 기존 DTO로 전달한다.
            // UI 갱신마다 설명을 생성하거나 전투 수치를 복제하지 않는다 (S-UX-Z78WCK).
            for (int i = 0; i < result.Count; i++)
            {
                BlessingDefinition definition = result[i];
                result[i] = new BlessingDefinition(definition.Id, definition.DisplayName,
                    definition.Tag, definition.Tier,
                    PlaceholderBlessingDescription.Create(effects.For(definition.Id)), definition.IsActive);
            }
            foreach (BlessingMvpDefinition entry in BlessingMvpCatalog.All)
                result.Add(entry.Definition);
            return result.ToArray();
        }
    }

    /// <summary>라운드 흐름과 UI가 함께 읽는 Local Play 불변 규칙.</summary>
    public readonly struct LocalPlayMatchRules : IEquatable<LocalPlayMatchRules>
    {
        public LocalPlayMatchRules(
            int totalRounds,
            float preparationSeconds,
            float augmentSelectionSeconds,
            float combatCountdownSeconds,
            float combatSeconds,
            float combatSummarySeconds)
        {
            if (totalRounds <= 0 || totalRounds > BlessingConfig.Default.DefinedRoundCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(totalRounds),
                    totalRounds,
                    $"Local Play 라운드는 1~{BlessingConfig.Default.DefinedRoundCount} 범위여야 합니다.");
            }
            if (!IsFinitePositive(preparationSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(preparationSeconds));
            }
            if (!IsFinitePositive(augmentSelectionSeconds)
                || augmentSelectionSeconds > preparationSeconds)
            {
                throw new ArgumentOutOfRangeException(nameof(augmentSelectionSeconds));
            }
            if (!IsFinitePositive(combatCountdownSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(combatCountdownSeconds));
            }
            if (!IsFinitePositive(combatSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(combatSeconds));
            }
            if (!IsFinitePositive(combatSummarySeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(combatSummarySeconds));
            }

            TotalRounds = totalRounds;
            PreparationSeconds = preparationSeconds;
            AugmentSelectionSeconds = augmentSelectionSeconds;
            CombatCountdownSeconds = combatCountdownSeconds;
            CombatSeconds = combatSeconds;
            CombatSummarySeconds = combatSummarySeconds;
        }

        public static LocalPlayMatchRules Default => new LocalPlayMatchRules(
            totalRounds: 3,
            preparationSeconds: 90f,
            augmentSelectionSeconds: 30f,
            combatCountdownSeconds: 3f,
            combatSeconds: 82f,
            combatSummarySeconds: 10f);

        public int TotalRounds { get; }
        public float PreparationSeconds { get; }
        public float AugmentSelectionSeconds { get; }
        public float CombatCountdownSeconds { get; }
        public float CombatSeconds { get; }
        public float CombatSummarySeconds { get; }

        public bool Equals(LocalPlayMatchRules other) =>
            TotalRounds == other.TotalRounds
            && PreparationSeconds.Equals(other.PreparationSeconds)
            && AugmentSelectionSeconds.Equals(other.AugmentSelectionSeconds)
            && CombatCountdownSeconds.Equals(other.CombatCountdownSeconds)
            && CombatSeconds.Equals(other.CombatSeconds)
            && CombatSummarySeconds.Equals(other.CombatSummarySeconds);

        public override bool Equals(object obj) => obj is LocalPlayMatchRules other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            TotalRounds,
            PreparationSeconds,
            AugmentSelectionSeconds,
            CombatCountdownSeconds,
            CombatSeconds,
            CombatSummarySeconds);

        private static bool IsFinitePositive(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// Local Play 로비에서 다음 매치까지 유지하는 구성(S-GD-CHVZ88/S-GD-CSYE3V).
    /// 확정된 라운드 수와 시간 규칙은 사용자 설정이 아니므로 이 값에 포함하지 않는다.
    /// </summary>
    public readonly struct LocalPlaySettings : IEquatable<LocalPlaySettings>
    {
        public const int MaxBotCount = 7;

        public LocalPlaySettings(
            int botCount,
            WeaponType humanWeapon,
            LocalPlayBotWeaponPreset botWeaponPreset)
        {
            if (botCount < 0 || botCount > MaxBotCount)
            {
                throw new ArgumentOutOfRangeException(nameof(botCount));
            }
            if (!IsImplementedWeapon(humanWeapon))
            {
                throw new ArgumentOutOfRangeException(nameof(humanWeapon));
            }
            if (!Enum.IsDefined(typeof(LocalPlayBotWeaponPreset), botWeaponPreset))
            {
                throw new ArgumentOutOfRangeException(nameof(botWeaponPreset));
            }

            BotCount = botCount;
            HumanWeapon = humanWeapon;
            BotWeaponPreset = botWeaponPreset;
        }

        public static LocalPlaySettings Default => new LocalPlaySettings(
            botCount: 3,
            humanWeapon: WeaponType.Sword,
            botWeaponPreset: LocalPlayBotWeaponPreset.Alternating);

        public int BotCount { get; }
        public WeaponType HumanWeapon { get; }
        public LocalPlayBotWeaponPreset BotWeaponPreset { get; }
        public int ParticipantCount => BotCount + 1;

        public WeaponType GetBotWeapon(int zeroBasedBotIndex)
        {
            if (zeroBasedBotIndex < 0 || zeroBasedBotIndex >= BotCount)
            {
                throw new ArgumentOutOfRangeException(nameof(zeroBasedBotIndex));
            }

            return BotWeaponPreset switch
            {
                LocalPlayBotWeaponPreset.Sword => WeaponType.Sword,
                LocalPlayBotWeaponPreset.Bow => WeaponType.Bow,
                _ => zeroBasedBotIndex % 2 == 0 ? WeaponType.Sword : WeaponType.Bow,
            };
        }

        public LocalPlaySettings WithBotCount(int value) =>
            new LocalPlaySettings(value, HumanWeapon, BotWeaponPreset);

        public LocalPlaySettings WithHumanWeapon(WeaponType value) =>
            new LocalPlaySettings(BotCount, value, BotWeaponPreset);

        public LocalPlaySettings WithBotWeaponPreset(LocalPlayBotWeaponPreset value) =>
            new LocalPlaySettings(BotCount, HumanWeapon, value);

        public bool Equals(LocalPlaySettings other) =>
            BotCount == other.BotCount
            && HumanWeapon == other.HumanWeapon
            && BotWeaponPreset == other.BotWeaponPreset;

        public override bool Equals(object obj) => obj is LocalPlaySettings other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(BotCount, HumanWeapon, BotWeaponPreset);

        public static bool operator ==(LocalPlaySettings left, LocalPlaySettings right) => left.Equals(right);

        public static bool operator !=(LocalPlaySettings left, LocalPlaySettings right) => !left.Equals(right);

        private static bool IsImplementedWeapon(WeaponType weapon) =>
            weapon == WeaponType.Sword || weapon == WeaponType.Bow;
    }

    public enum LocalPlayTransition
    {
        None = 0,
        PreparationMapLoadingStarted = 1,
        PreparationStarted = 2,
        PreparationControlUnlocked = 3,
        CombatMapLoadingStarted = 4,
        CombatCountdownStarted = 5,
        CombatStarted = 6,
        CombatSummaryStarted = 7,
        MatchCompleted = 8,
    }

    /// <summary>UI가 그대로 그릴 수 있는 Local Play 상태 스냅샷.</summary>
    public readonly struct LocalPlaySessionSnapshot
    {
        public LocalPlaySessionSnapshot(
            LocalPlaySettings settings,
            LocalPlayMatchRules rules,
            LocalPlayPhase phase,
            LocalPlayMap activeMap,
            int currentRound,
            float remainingSeconds,
            float augmentSelectionRemainingSeconds,
            int aliveParticipants,
            LocalPlayBlessingDraftSnapshot augmentOffers,
            BlessingDefinition selectedAugment,
            bool isAugmentSelectionOpen,
            bool isSimulationPaused,
            bool canLocalPlayerControl,
            bool hasCompletedMatch,
            IReadOnlyList<BlessingDefinition> acquiredAugments = null,
            bool isObservingLastSurvivor = false,
            float lastSurvivorObservationRemainingSeconds = 0f,
            IReadOnlyList<CombatRoundLiveStanding> liveStandings = null,
            IReadOnlyList<BettingRoyale.Data.Online.OnlineCoreLoopRoundResult> roundResults = null)
        {
            RoundResults = CopyRoundResults(roundResults);
            IsObservingLastSurvivor = isObservingLastSurvivor;
            LastSurvivorObservationRemainingSeconds = isObservingLastSurvivor
                ? Math.Max(0f, lastSurvivorObservationRemainingSeconds)
                : 0f;
            Settings = settings;
            Rules = rules;
            Phase = phase;
            ActiveMap = activeMap;
            CurrentRound = currentRound;
            RemainingSeconds = Math.Max(0f, remainingSeconds);
            AugmentSelectionRemainingSeconds = Math.Max(0f, augmentSelectionRemainingSeconds);
            AliveParticipants = Math.Max(0, aliveParticipants);
            AugmentOffers = augmentOffers;
            SelectedAugment = selectedAugment;
            IsAugmentSelectionOpen = isAugmentSelectionOpen;
            IsSimulationPaused = isSimulationPaused;
            CanLocalPlayerControl = canLocalPlayerControl;
            HasCompletedMatch = hasCompletedMatch;
            AcquiredAugments = CopyAcquired(acquiredAugments);
            LiveStandings = CopyLiveStandings(liveStandings);
        }

        /// <summary>
        /// 찍은 순간의 목록을 <b>복사해서</b> 갖는다. 상태가 다음 라운드에서 축복을 더 담아도
        /// 이미 찍힌 스냅샷은 바뀌지 않는다 — 화면이 프레임 사이에 들고 있어도 안전하다.
        /// </summary>
        private static IReadOnlyList<BlessingDefinition> CopyAcquired(
            IReadOnlyList<BlessingDefinition> source)
        {
            if (source == null || source.Count == 0)
            {
                return EmptyAcquired;
            }

            var copy = new BlessingDefinition[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            // 배열을 그대로 돌려주면 IReadOnlyList 로 받은 쪽이 배열로 되캐스팅해 고칠 수 있다.
            return new ReadOnlyCollection<BlessingDefinition>(copy);
        }

        private static readonly IReadOnlyList<BlessingDefinition> EmptyAcquired =
            new ReadOnlyCollection<BlessingDefinition>(Array.Empty<BlessingDefinition>());

        /// <summary>
        /// 이번 라운드에 Host/공용 런타임이 지금까지 확정한 사실만 복사한다.
        /// 점수·등수는 최종 정산 경계 전까지 이 스냅샷에 존재하지 않는다(S15P21A606-170).
        /// </summary>
        private static IReadOnlyList<CombatRoundLiveStanding> CopyLiveStandings(
            IReadOnlyList<CombatRoundLiveStanding> source)
        {
            if (source == null || source.Count == 0)
            {
                return EmptyLiveStandings;
            }

            var copy = new CombatRoundLiveStanding[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            return new ReadOnlyCollection<CombatRoundLiveStanding>(copy);
        }

        private static readonly IReadOnlyList<CombatRoundLiveStanding> EmptyLiveStandings =
            new ReadOnlyCollection<CombatRoundLiveStanding>(Array.Empty<CombatRoundLiveStanding>());

        private static IReadOnlyList<BettingRoyale.Data.Online.OnlineCoreLoopRoundResult> CopyRoundResults(
            IReadOnlyList<BettingRoyale.Data.Online.OnlineCoreLoopRoundResult> source)
        {
            if (source == null || source.Count == 0)
            {
                return EmptyRoundResults;
            }

            var copy = new BettingRoyale.Data.Online.OnlineCoreLoopRoundResult[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                copy[i] = source[i];
            }

            return new ReadOnlyCollection<BettingRoyale.Data.Online.OnlineCoreLoopRoundResult>(copy);
        }

        private static readonly IReadOnlyList<BettingRoyale.Data.Online.OnlineCoreLoopRoundResult> EmptyRoundResults =
            new ReadOnlyCollection<BettingRoyale.Data.Online.OnlineCoreLoopRoundResult>(
                Array.Empty<BettingRoyale.Data.Online.OnlineCoreLoopRoundResult>());

        /// <summary>
        /// 이번 라운드 참가자별 순위·점수. 인덱스가 참가자 번호다(0 = 사람). 관찰 창에서 순위가 먼저
        /// 실리고 정산에서 확정된다 (S15P21A606-292 · S-ONL-9D34TP). 없으면 빈 목록이다.
        /// </summary>
        public IReadOnlyList<BettingRoyale.Data.Online.OnlineCoreLoopRoundResult> RoundResults { get; }

        /// <summary>사람의 이번 라운드 순위. 공용 런타임이 아직 정하지 않았으면 0 이다.</summary>
        public int LocalRoundPlacement =>
            RoundResults.Count > 0 && RoundResults[0].HasRank && RoundResults[0].Round == CurrentRound
                ? RoundResults[0].Rank
                : 0;

        public LocalPlaySettings Settings { get; }
        public LocalPlayMatchRules Rules { get; }
        public LocalPlayPhase Phase { get; }
        public LocalPlayMap ActiveMap { get; }
        public int CurrentRound { get; }
        public float RemainingSeconds { get; }
        public float AugmentSelectionRemainingSeconds { get; }
        public int AliveParticipants { get; }
        public LocalPlayBlessingDraftSnapshot AugmentOffers { get; }
        public BlessingDefinition SelectedAugment { get; }
        public bool IsAugmentSelectionOpen { get; }
        public bool IsSimulationPaused { get; }
        public bool CanLocalPlayerControl { get; }
        public bool HasCompletedMatch { get; }

        /// <summary>
        /// 마지막 생존 진영 관찰 중인가 (S15P21A606-170). 전투 단계 그대로이며 공격·피해만 막혀 있다.
        /// </summary>
        public bool IsObservingLastSurvivor { get; }

        /// <summary>관찰 창에 남은 시간. 관찰 중이 아니면 0 이다.</summary>
        public float LastSurvivorObservationRemainingSeconds { get; }

        /// <summary>
        /// 이번 라운드 Host/공용 런타임 사실의 복사본. 생존·이탈·킬·어시스트만 담고
        /// 정산 전 점수·등수는 추정하지 않는다(S-GD-P7SC3Q).
        /// </summary>
        public IReadOnlyList<CombatRoundLiveStanding> LiveStandings { get; }

        public bool HasLiveRoundFacts => LiveStandings.Count > 0;

        /// <summary>중간 사실 기준 생존 인원과 명부 인원.</summary>
        public bool TryCountLiveParticipants(out int alive, out int total)
        {
            alive = 0;
            total = LiveStandings.Count;
            for (int i = 0; i < LiveStandings.Count; i++)
            {
                if (LiveStandings[i].IsAlive)
                {
                    alive++;
                }
            }

            return total > 0;
        }

        public CombatRoundLiveStanding[] CopyLiveStandings() =>
            LiveStandings.Count == 0
                ? Array.Empty<CombatRoundLiveStanding>()
                : new List<CombatRoundLiveStanding>(LiveStandings).ToArray();

        /// <summary>
        /// 이번 매치에서 지금까지 획득한 축복 (S-GD-RFXF8X). 획득 순서대로이며 <b>라운드를 넘어 쌓인다.</b>
        /// 절대 null 이 아니다 — 매치 전에는 빈 목록이다.
        /// </summary>
        public IReadOnlyList<BlessingDefinition> AcquiredAugments { get; }
    }

    /// <summary>
    /// 로비 → 별도 준비/전투 맵의 로딩 경계 → 3라운드 → 같은 설정의 로비 복귀를 결정적으로 소유한다.
    /// Unity 프레임·Fusion 세션·표시 계층을 모르는 순수 상태다(S-GD-7RCHF8).
    /// </summary>
    public sealed class LocalPlaySessionState
    {
        private LocalPlaySettings settings;
        private readonly LocalPlayMatchRules rules;
        private readonly BlessingConfig blessingConfig = BlessingConfig.Default;

        /// <summary>
        /// 이 매치에서 후보로 뜬 축복 id — 재추첨·다음 준비 단계에서 다시 나오지 않는다 (S-GD-H78GRS).
        /// 새 매치를 시작할 때 비운다.
        /// </summary>
        private readonly HashSet<string> exposedBlessingIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly BlessingLoadout blessingLoadout = new BlessingLoadout();

        /// <summary>
        /// 준비 단계 거래의 경제 정본. <b>축복 지갑과 같은 지갑이다</b> —
        /// <see cref="blessingWallet"/> 는 이 안의 참가자 지갑을 가리킨다.
        ///
        /// <para>
        /// 지갑을 둘로 두면 상점에서 쓴 돈이 축복 재추첨 잔액에 안 보이고, 룰렛으로 딴 돈이
        /// 최종 순위 근거(<c>TotalInflow</c>)에서 빠진다. <b>둘 다 화면에는 정상으로 보인다.</b>
        /// </para>
        /// </summary>
        private readonly MatchEconomyState commerceEconomy = new MatchEconomyState(
            EconomyConfig.Default, BlessingConfig.Default, LocalPlayBlessingCatalog.All);

        /// <summary>
        /// 준비 단계 상점·룰렛. 제품과 같은 게이트를 쓴다 (<see cref="LocalPlayPreparationCommerce"/>).
        /// </summary>
        private readonly LocalPlayPreparationCommerce commerce;

        /// <summary>
        /// 준비 종료 때 룰렛 게이트가 만든 환불·정산 결과. 상태 전환은 순수 도메인이 소유하고,
        /// Unity 어댑터가 이 값을 이벤트로 발행한다 — 닫히는 프레임의 환불을 잃지 않는다.
        /// </summary>
        public RouletteTickOutcome LastPreparationRouletteOutcome { get; private set; }

        /// <summary>
        /// 로컬 플레이어의 지갑. <b>매치를 시작할 때 <see cref="commerceEconomy"/> 의 것으로 바뀐다.</b>
        /// 로비에서는 아직 참가자가 없으므로 빈 지갑을 가리킨다 — 그때는 잔액이 0 이 맞다.
        /// </summary>
        private Wallet blessingWallet = new Wallet();

        /// <summary>
        /// 경제형 축복(이자 폭발·저점 매수)의 준비 단계 상태. <b>지갑은 <see cref="blessingWallet"/> 하나다</b> —
        /// 예치·환급이 <see cref="Gold"/> 와 같은 잔액을 움직인다.
        /// </summary>
        private BlessingEconomyState blessingEconomy = new BlessingEconomyState();

        /// <summary>
        /// 직전 라운드까지의 누적 순위 점수. <b>월드가 넣어준다</b>(<see cref="SetBlessingStandings"/>) —
        /// Local Play 상태는 봇의 점수를 집계하지 않는다.
        /// </summary>
        private readonly List<BlessingScore> blessingStandings = new List<BlessingScore>();

        /// <summary>순위표에서 로컬 플레이어를 가리키는 id. 표가 없으면 의미가 없다.</summary>
        private int blessingStandingsLocalPlayerId;

        /// <summary>
        /// 마지막 생존 진영 관찰 중인가 (S15P21A606-170). 전투 단계를 유지한 채 열린다.
        /// </summary>
        private bool isObservingLastSurvivor;

        /// <summary>관찰 창에 남은 시간. 0 이 되면 월드가 정산 경계를 지난다.</summary>
        private float lastSurvivorObservationRemainingSeconds;

        private LocalPlayPhase phase = LocalPlayPhase.Lobby;
        private int currentRound;
        private float remainingSeconds;
        private float augmentSelectionRemainingSeconds;
        private bool isAugmentSelectionOpen;
        private DeterministicRandom blessingRandom;
        private BlessingDraft blessingDraft;
        private LocalPlayBlessingDraftSnapshot augmentOffers;
        private BlessingDefinition selectedAugment;
        private bool hasCompletedMatch;

        public LocalPlaySessionState(LocalPlaySettings initialSettings)
            : this(initialSettings, LocalPlayMatchRules.Default)
        {
        }

        public LocalPlaySessionState(LocalPlaySettings initialSettings, LocalPlayMatchRules rules)
        {
            settings = initialSettings;
            this.rules = rules;
            commerce = new LocalPlayPreparationCommerce(commerceEconomy);
        }

        /// <summary>
        /// 준비 단계 상점·룰렛. <b>Local Play 도 제품과 같은 계약을 실행한다</b> —
        /// 화면은 여기서 나온 이벤트를 그대로 그린다.
        /// </summary>
        public LocalPlayPreparationCommerce Commerce => commerce;

        public LocalPlaySettings Settings => settings;
        public LocalPlayMatchRules Rules => rules;
        public LocalPlayPhase Phase => phase;
        public LocalPlayMap ActiveMap => ResolveActiveMap(phase);
        public int CurrentRound => currentRound;
        public float RemainingSeconds => remainingSeconds;
        public float AugmentSelectionRemainingSeconds => augmentSelectionRemainingSeconds;
        public bool IsAugmentSelectionOpen => isAugmentSelectionOpen;
        public LocalPlayBlessingDraftSnapshot AugmentOffers => augmentOffers;
        public BlessingDefinition SelectedAugment => selectedAugment;

        /// <summary>
        /// 이번 매치에서 획득한 축복 (S-GD-RFXF8X). <b>읽기 전용 복사본</b>이라 밖에서 담거나 지울 수 없다 —
        /// 획득은 선택·자동 선택 경로 하나로만 일어난다.
        /// </summary>
        public IReadOnlyList<BlessingDefinition> AcquiredAugments => CaptureAcquiredAugments();
        public LocalPlayRuntimePhasePolicy RuntimePhasePolicy =>
            LocalPlayRuntimePhasePolicy.Resolve(
                phase,
                canLocalPlayerControl: !isAugmentSelectionOpen,
                isObservingLastSurvivor: isObservingLastSurvivor);

        /// <summary>
        /// 마지막 생존 진영을 관찰 중인가 (S15P21A606-170). 전투 단계는 그대로이며 요약으로
        /// 넘어가지 않는다 — 이동·시점은 살아 있고 공격·피해만 막힌다.
        /// </summary>
        public bool IsObservingLastSurvivor => isObservingLastSurvivor;

        /// <summary>관찰 창에 남은 시간. 관찰 중이 아니면 0 이다.</summary>
        public float LastSurvivorObservationRemainingSeconds =>
            isObservingLastSurvivor ? lastSurvivorObservationRemainingSeconds : 0f;

        /// <summary>
        /// 관찰 창을 다 썼는가. 월드는 이 값이 참일 때만 라운드를 확정하고 요약으로 넘긴다
        /// — 정산 경계는 그대로 하나다.
        /// </summary>
        public bool IsLastSurvivorObservationComplete =>
            isObservingLastSurvivor && lastSurvivorObservationRemainingSeconds <= 0f;

        /// <summary>
        /// 마지막 생존 진영 관찰을 연다. 전투 중 <b>첫 호출만</b> true 다(멱등).
        /// </summary>
        /// <remarks>
        /// ⚠️ 여기서 단계를 바꾸지 않는다 — 요약 전환은 월드가 사실을 확정한 뒤
        /// <see cref="TryCompleteCombat"/> 로 한 번 한다.
        /// </remarks>
        public bool TryBeginLastSurvivorObservation()
        {
            if (phase != LocalPlayPhase.Combat || isObservingLastSurvivor)
            {
                return false;
            }

            isObservingLastSurvivor = true;
            lastSurvivorObservationRemainingSeconds = CombatRoundObservationWindow.DefaultSeconds;
            return true;
        }

        /// <summary>남은 관찰 창을 지금 닫는다. 개발용 SKIP·매치 취소 같은 명시적 경계에서만 쓴다.</summary>
        public bool CompleteLastSurvivorObservationNow()
        {
            if (!isObservingLastSurvivor || lastSurvivorObservationRemainingSeconds <= 0f)
            {
                return false;
            }

            lastSurvivorObservationRemainingSeconds = 0f;
            return true;
        }
        public bool IsSimulationPaused => RuntimePhasePolicy.IsSimulationPaused;
        public bool CanLocalPlayerControl => RuntimePhasePolicy.HumanControlEnabled;
        public bool HasCompletedMatch => hasCompletedMatch;
        public int Gold => blessingWallet.Gold;
        public int TotalGoldInflow => blessingWallet.TotalInflow;

        /// <summary>
        /// 경제형 축복 상태. 화면은 여기서 <b>읽기만</b> 한다 — 예치·블라인드 베팅은
        /// 아래 <c>Try~</c> 들이 단계와 선택 여부를 확인한 뒤에만 통과시킨다.
        ///
        /// <para><b>매치마다 새 인스턴스다</b> — 지난 매치의 예치 기록이 남지 않는다 (S-GD-RFXF8X).</para>
        /// </summary>
        public BlessingEconomyState BlessingEconomy => blessingEconomy;

        public bool TryUpdateSettings(LocalPlaySettings value)
        {
            if (phase != LocalPlayPhase.Lobby)
            {
                return false;
            }

            settings = value;
            hasCompletedMatch = false;
            return true;
        }

        public bool TryStartMatch(out LocalPlayTransition transition)
        {
            if (phase != LocalPlayPhase.Lobby)
            {
                transition = LocalPlayTransition.None;
                return false;
            }

            currentRound = 1;
            hasCompletedMatch = false;
            blessingLoadout.ResetForNewMatch();
            BeginCommerceMatch();
            ResetBlessingEconomyForNewMatch();
            blessingRandom = new DeterministicRandom(
                unchecked(0x4C504D56 ^ (int)settings.HumanWeapon));
            blessingDraft = null;
            transition = BeginPreparationMapLoading();
            return true;
        }

        /// <summary>
        /// 현재 맵에 필요한 모든 참가자가 로딩 완료됐을 때 호출한다.
        /// Local Play는 로컬 월드 조립 직후 호출하고, 온라인 구현은 Host의 전원 완료 집계 뒤 호출한다.
        /// </summary>
        public bool TryCompleteMapLoading(out LocalPlayTransition transition)
        {
            switch (phase)
            {
                case LocalPlayPhase.PreparationLoading:
                    phase = LocalPlayPhase.Preparation;
                    remainingSeconds = rules.PreparationSeconds;
                    augmentSelectionRemainingSeconds = rules.AugmentSelectionSeconds;
                    isAugmentSelectionOpen = true;
                    PrepareBlessingDraft();
                    selectedAugment = null;
                    transition = LocalPlayTransition.PreparationStarted;
                    return true;

                case LocalPlayPhase.CombatLoading:
                    phase = LocalPlayPhase.CombatCountdown;
                    remainingSeconds = rules.CombatCountdownSeconds;
                    transition = LocalPlayTransition.CombatCountdownStarted;
                    return true;

                default:
                    transition = LocalPlayTransition.None;
                    return false;
            }
        }

        public bool TrySelectAugment(
            string candidateId,
            out LocalPlayTransition transition)
        {
            if (phase != LocalPlayPhase.Preparation
                || !isAugmentSelectionOpen
                || blessingDraft == null
                || !augmentOffers.TryGetIndex(candidateId, out int candidateIndex))
            {
                transition = LocalPlayTransition.None;
                return false;
            }

            selectedAugment = blessingDraft.Select(candidateIndex);
            blessingLoadout.Add(selectedAugment);
            RefreshBlessingLoadout();

            // 고른 순간 받는 보상(쇼 미 더 머니·탐욕·저점 매수)이 잔액을 바꾸므로 다시 찍는다.
            augmentOffers = CaptureBlessingDraft();
            isAugmentSelectionOpen = false;
            augmentSelectionRemainingSeconds = 0f;
            transition = LocalPlayTransition.PreparationControlUnlocked;
            return true;
        }

        /// <summary>
        /// 후보 자리의 잠금을 켜고 끈다 (S-GD-S46S28). 무료지만 동시에 2개까지다 —
        /// 상한 판정은 <see cref="BlessingDraft"/> 가 하고 여기서는 따라 하지 않는다.
        ///
        /// <para>
        /// <b>거부할 때 던지지 않는다.</b> 화면 입력이 그대로 들어오는 경로라
        /// 선택이 끝난 뒤 늦게 도착한 클릭이나 범위 밖 번호가 정상적으로 올 수 있다.
        /// </para>
        /// </summary>
        /// <returns>잠금 상태가 실제로 바뀌었으면 true.</returns>
        public bool TryToggleAugmentLock(int candidateIndex)
        {
            if (phase != LocalPlayPhase.Preparation
                || !isAugmentSelectionOpen
                || blessingDraft == null
                || candidateIndex < 0
                || candidateIndex >= LocalPlayBlessingDraftSnapshot.Count)
            {
                return false;
            }

            if (!blessingDraft.TryToggleLock(candidateIndex))
            {
                // 상한(2개)에 걸렸거나 이미 선택이 끝났다. 상태는 그대로 둔다.
                return false;
            }

            augmentOffers = CaptureBlessingDraft();
            return true;
        }

        public bool TryRerollAugmentOffers()
        {
            if (phase != LocalPlayPhase.Preparation || !isAugmentSelectionOpen)
            {
                return false;
            }

            if (blessingDraft == null || blessingDraft.TryReroll(blessingWallet) != RerollResult.Success)
            {
                return false;
            }

            augmentOffers = CaptureBlessingDraft();
            return true;
        }

        // ── 경제형 축복 (이자 폭발 · 저점 매수) ───────────────────────────

        /// <summary>
        /// 직전 라운드까지의 누적 순위 점수를 넣는다. 저점 매수의 하위권 판정이 이것만 본다.
        ///
        /// <para>
        /// <b>Local Play 상태는 점수를 만들지 않는다</b> — 봇의 킬·생존을 집계하는 것은 월드이고,
        /// 여기서 순위를 추측하면 판정이 두 곳으로 갈린다. 표는 <b>다음 준비 단계부터</b> 적용된다.
        /// </para>
        ///
        /// <para>
        /// 빈 표·<c>null</c>·로컬 플레이어가 빠진 표·같은 사람이 두 번 있는 표는 <b>자격 없음</b>으로
        /// 처리한다 — 던지지 않는다. 월드가 한 프레임 늦게 채우는 경우가 정상이고, 동점자는
        /// 같은 답을 받으므로(<see cref="BlessingEconomyState.IsLowerHalf"/>) 전원 동점이면 아무도 자격이 없다.
        /// </para>
        /// </summary>
        /// <param name="localPlayerId">순위표에서 로컬 플레이어를 가리키는 id.</param>
        /// <param name="previousRoundScores">직전 라운드까지의 누적 점수. <b>복사해서</b> 보관한다.</param>
        public void SetBlessingStandings(int localPlayerId, IReadOnlyList<BlessingScore> previousRoundScores)
        {
            blessingStandings.Clear();
            blessingStandingsLocalPlayerId = localPlayerId;

            if (previousRoundScores == null)
            {
                return;
            }

            for (int i = 0; i < previousRoundScores.Count; i++)
            {
                BlessingScore score = previousRoundScores[i];

                for (int j = 0; j < i; j++)
                {
                    if (previousRoundScores[j].PlayerId == score.PlayerId)
                    {
                        // 같은 사람이 두 줄이면 "나보다 높은 사람" 수가 부풀어 판정이 조용히 뒤집힌다.
                        // 표 전체를 버린다 — 절반만 믿으면 어느 절반인지 아무도 모른다.
                        blessingStandings.Clear();
                        return;
                    }
                }

                blessingStandings.Add(score);
            }
        }

        /// <summary>
        /// 이자 폭발의 예치를 접수한다. 금액·환급 조건은 카탈로그가 정하고 차감은 <see cref="Gold"/> 와
        /// 같은 지갑에서 일어난다.
        /// </summary>
        /// <returns>
        /// 예치했으면 true. 준비 단계가 아니거나 <b>아직 축복을 고르지 않았거나</b> 이자 폭발이 없거나
        /// 이미 예치했거나 골드가 모자라면 false.
        /// </returns>
        public bool TryDepositBlessing() =>
            IsBlessingCommandAllowed() && blessingEconomy.TryDeposit(blessingWallet);

        /// <summary>블라인드 베팅을 준비 단계에서 명시적으로 받아들인다.</summary>
        public bool TryAcceptBlessingBlindBet() =>
            IsBlessingCommandAllowed() && blessingEconomy.TryAcceptBlindBet(blessingWallet);

        /// <summary>이자 폭발 예치를 거절한다 — 돈이 오가지 않는다 (S-GD-XBP3VA).</summary>
        public bool TryDeclineBlessingDeposit() =>
            IsBlessingCommandAllowed() && blessingEconomy.DeclineDeposit();

        /// <summary>블라인드 베팅을 거절한다.</summary>
        public bool TryDeclineBlessingBlindBet() =>
            IsBlessingCommandAllowed() && blessingEconomy.DeclineBlindBet();

        /// <summary>
        /// 라운드를 정산한다 — 살아남았으면 예치 원금을 되돌려준다.
        ///
        /// <para>
        /// <b>월드가 전투 요약에서 "실제로 살아남았는가" 를 확정한 뒤에 부른다.</b> 남은 시간이
        /// 0 이 됐다거나 요약 화면으로 넘어갔다는 사실은 생사와 무관하므로 상태 기계가 스스로
        /// 부르지 않는다 — 모르는 채로 환급하면 <b>탈락자에게도 돈이 돌아간다.</b>
        /// </para>
        ///
        /// <para>
        /// 정산하지 않고 다음 라운드를 열면 <see cref="TryCompleteMapLoading"/> 가 던진다.
        /// 조용히 넘기면 예치금이 사라지거나 돌아오고, <b>잔액만 틀린 채 예외는 나지 않는다.</b>
        /// </para>
        /// </summary>
        /// <param name="round">정산하려는 라운드. 현재 라운드가 아니면 아무것도 하지 않는다.</param>
        /// <param name="survived">그 라운드를 살아남았는가. 탈락이면 예치금은 사라진다.</param>
        /// <returns>이번 호출이 정산을 수행했으면 true. 이미 정산했거나 시점이 맞지 않으면 false.</returns>
        public bool TrySettleBlessingRound(int round, bool survived)
        {
            if (phase != LocalPlayPhase.Combat && phase != LocalPlayPhase.CombatSummary)
            {
                return false;
            }

            if (blessingEconomy.Round < 1
                || round != blessingEconomy.Round
                || blessingEconomy.IsPreparationOpen
                || blessingEconomy.RoundSettled)
            {
                return false;
            }

            blessingEconomy.SettleRound(round, survived, blessingWallet);
            return true;
        }

        /// <summary>생존 진영 판정이 충족됐을 때 제한 시간을 기다리지 않고 전투 요약으로 전환한다.</summary>
        public bool TryCompleteCombat(out LocalPlayTransition transition)
        {
            if (phase != LocalPlayPhase.Combat)
            {
                transition = LocalPlayTransition.None;
                return false;
            }

            transition = BeginCombatSummary();
            return true;
        }

        /// <summary>
        /// 월드 조립이나 로딩 완료 처리에 실패한 매치를 취소하고 같은 설정의 로비로 복구한다.
        /// 이미 로비라면 아무 상태도 바꾸지 않는다.
        /// </summary>
        public bool TryAbortMatch()
        {
            if (phase == LocalPlayPhase.Lobby)
            {
                return false;
            }

            phase = LocalPlayPhase.Lobby;
            currentRound = 0;
            remainingSeconds = 0f;
            augmentSelectionRemainingSeconds = 0f;
            isAugmentSelectionOpen = false;
            augmentOffers = default;
            selectedAugment = null;
            blessingDraft = null;
            hasCompletedMatch = false;
            ClearLastSurvivorObservation();

            // 취소한 매치의 획득 축복을 남기면 다음 매치의 1라운드 후보에서 그대로 빠진다
            // (S-GD-VF67AG 필터). 매치를 넘지 않는 값이므로 여기서 비운다 (S-GD-RFXF8X).
            blessingLoadout.ResetForNewMatch();
            BeginCommerceMatch();
            ResetBlessingEconomyForNewMatch();
            return true;
        }

        /// <summary>
        /// 거래 경제를 새 매치로 연다. <b>지갑 참조도 여기서 갈아끼운다</b> —
        /// <c>BeginMatch</c> 가 참가자 지갑을 새로 만들기 때문에, 옛 참조를 들고 있으면
        /// 축복은 옛 지갑을 보고 상점·룰렛은 새 지갑을 봐서 <b>잔액이 두 벌</b>이 된다.
        /// </summary>
        private void BeginCommerceMatch()
        {
            commerce.ResetForNewMatch();
            LastPreparationRouletteOutcome = default;
            commerceEconomy.BeginMatch(
                new[]
                {
                    new MatchParticipant(
                        LocalPlayPreparationCommerce.LocalPlayerId,
                        LocalPlayBlessingCatalog.ResolveWeaponTag(settings.HumanWeapon)),
                },
                unchecked(0x4C504D56 ^ (int)settings.HumanWeapon));
            blessingWallet = commerceEconomy.GetParticipantWallet(
                LocalPlayPreparationCommerce.LocalPlayerId);
        }

        public LocalPlayTransition Tick(float deltaSeconds)
        {
            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            }
            if (deltaSeconds <= 0f)
            {
                return LocalPlayTransition.None;
            }

            return phase switch
            {
                LocalPlayPhase.Preparation => TickPreparation(deltaSeconds),
                LocalPlayPhase.CombatCountdown => TickTimedPhase(
                    deltaSeconds,
                    BeginCombat),
                LocalPlayPhase.Combat => TickCombat(deltaSeconds),
                LocalPlayPhase.CombatSummary => TickTimedPhase(
                    deltaSeconds,
                    CompleteCombatSummary),
                _ => LocalPlayTransition.None,
            };
        }

        /// <summary>에디터 MVP 검증에서 현재 상태의 대기 시간을 끝낸다.</summary>
        public LocalPlayTransition SkipCurrentPhase()
        {
            switch (phase)
            {
                case LocalPlayPhase.PreparationLoading:
                case LocalPlayPhase.CombatLoading:
                    return TryCompleteMapLoading(out LocalPlayTransition loadingTransition)
                        ? loadingTransition
                        : LocalPlayTransition.None;

                case LocalPlayPhase.Preparation:
                    return BeginCombatMapLoading();

                case LocalPlayPhase.CombatCountdown:
                    return BeginCombat();

                case LocalPlayPhase.Combat:
                    return BeginCombatSummary();

                case LocalPlayPhase.CombatSummary:
                    return CompleteCombatSummary();

                default:
                    return LocalPlayTransition.None;
            }
        }

        public LocalPlaySessionSnapshot Capture(
            int aliveParticipants,
            IReadOnlyList<CombatRoundLiveStanding> liveStandings = null,
            IReadOnlyList<BettingRoyale.Data.Online.OnlineCoreLoopRoundResult> roundResults = null)
        {
            LocalPlayRuntimePhasePolicy runtimePolicy = RuntimePhasePolicy;
            return new LocalPlaySessionSnapshot(
                settings,
                rules,
                phase,
                ActiveMap,
                currentRound,
                remainingSeconds,
                augmentSelectionRemainingSeconds,
                aliveParticipants,
                augmentOffers,
                selectedAugment,
                isAugmentSelectionOpen,
                runtimePolicy.IsSimulationPaused,
                runtimePolicy.HumanControlEnabled,
                hasCompletedMatch,
                blessingLoadout.Acquired,
                isObservingLastSurvivor,
                LastSurvivorObservationRemainingSeconds,
                liveStandings,
                roundResults);
        }

        private LocalPlayTransition TickPreparation(float deltaSeconds)
        {
            remainingSeconds = Math.Max(0f, remainingSeconds - deltaSeconds);
            if (isAugmentSelectionOpen)
            {
                augmentSelectionRemainingSeconds = Math.Max(
                    0f,
                    augmentSelectionRemainingSeconds - deltaSeconds);
            }

            if (remainingSeconds <= 0f)
            {
                return BeginCombatMapLoading();
            }
            if (isAugmentSelectionOpen && augmentSelectionRemainingSeconds <= 0f)
            {
                // S-GD-NK8ZJ7 — 시간이 끝났으면 대신 골라준다. 아무것도 못 받고 라운드를 넘기지 않는다.
                EnsureAugmentAcquired();
                isAugmentSelectionOpen = false;
                return LocalPlayTransition.PreparationControlUnlocked;
            }

            return LocalPlayTransition.None;
        }

        private LocalPlayTransition TickCombat(float deltaSeconds)
        {
            // S-GD-657ZN5: this countdown describes global zone coverage, never match termination.
            remainingSeconds = Math.Max(0f, remainingSeconds - deltaSeconds);

            // 관찰 창은 전투 시간과 별개다 — 라운드 종료 사실이 이미 확정된 뒤에만 흐른다.
            // ⚠️ 여기서 요약으로 넘기지 않는다. 사실 확정과 경제 반영은 월드가 한 번 한다.
            if (isObservingLastSurvivor)
            {
                lastSurvivorObservationRemainingSeconds = Math.Max(
                    0f, lastSurvivorObservationRemainingSeconds - deltaSeconds);
            }

            return LocalPlayTransition.None;
        }

        private LocalPlayTransition TickTimedPhase(
            float deltaSeconds,
            Func<LocalPlayTransition> onCompleted)
        {
            remainingSeconds = Math.Max(0f, remainingSeconds - deltaSeconds);
            return remainingSeconds <= 0f
                ? onCompleted()
                : LocalPlayTransition.None;
        }

        private LocalPlayTransition BeginPreparationMapLoading()
        {
            phase = LocalPlayPhase.PreparationLoading;
            remainingSeconds = 0f;
            augmentSelectionRemainingSeconds = 0f;
            isAugmentSelectionOpen = false;
            augmentOffers = default;
            selectedAugment = null;
            blessingDraft = null;
            return LocalPlayTransition.PreparationMapLoadingStarted;
        }

        private LocalPlayTransition BeginCombatMapLoading()
        {
            // 준비 시간이 다 됐거나 건너뛰었는데 아직 선택하지 않았을 수 있다. 준비를 떠나기 전이
            // 대신 고를 마지막 지점이다 — 여기를 지나면 이번 라운드의 후보가 사라진다 (S-GD-NK8ZJ7).
            EnsureAugmentAcquired();

            // 준비를 떠나면 예치·블라인드 베팅을 받지 않는다. 정산은 전투 결과가 확정된 뒤
            // 월드가 TrySettleBlessingRound 로 부른다 — 여기서 추측하지 않는다.
            blessingEconomy.EndPreparation();

            // 상점은 닫히고, 돌지 않은 룰렛 판의 베팅은 전액 돌아온다 (S-GD-7VKZA5).
            LastPreparationRouletteOutcome = commerce.EndPreparation();
            commerceEconomy.EndPreparation();

            phase = LocalPlayPhase.CombatLoading;
            remainingSeconds = 0f;
            augmentSelectionRemainingSeconds = 0f;
            isAugmentSelectionOpen = false;
            return LocalPlayTransition.CombatMapLoadingStarted;
        }

        private LocalPlayTransition BeginCombat()
        {
            phase = LocalPlayPhase.Combat;
            remainingSeconds = rules.CombatSeconds;
            ClearLastSurvivorObservation();
            return LocalPlayTransition.CombatStarted;
        }

        private LocalPlayTransition BeginCombatSummary()
        {
            phase = LocalPlayPhase.CombatSummary;
            remainingSeconds = rules.CombatSummarySeconds;
            // 관찰은 라운드 안에서만 뜻이 있다. 남기면 다음 라운드가 피해가 막힌 채 시작한다.
            ClearLastSurvivorObservation();
            return LocalPlayTransition.CombatSummaryStarted;
        }

        private void ClearLastSurvivorObservation()
        {
            isObservingLastSurvivor = false;
            lastSurvivorObservationRemainingSeconds = 0f;
        }

        private LocalPlayTransition CompleteCombatSummary()
        {
            if (currentRound >= rules.TotalRounds)
            {
                phase = LocalPlayPhase.Lobby;
                currentRound = 0;
                remainingSeconds = 0f;
                augmentSelectionRemainingSeconds = 0f;
                isAugmentSelectionOpen = false;
                augmentOffers = default;
                selectedAugment = null;
                blessingDraft = null;
                hasCompletedMatch = true;
                return LocalPlayTransition.MatchCompleted;
            }

            currentRound++;
            return BeginPreparationMapLoading();
        }

        /// <summary>
        /// 아직 선택하지 않았으면 <see cref="BlessingDraft.SelectAutomatically"/> 로 대신 고른다 (S-GD-NK8ZJ7).
        ///
        /// <para>
        /// <b>한 준비 단계에서 많아야 한 번만 담는다.</b> 시간 만료와 준비 이탈이 이어서 불릴 수 있는데
        /// (30초 만료 → 90초 종료), 두 번 담으면 <see cref="BlessingLoadout"/> 가 중복으로 던진다.
        /// 선택 여부를 <see cref="BlessingDraft.IsSelected"/> 로 확인해 두 번째 호출은 아무것도 하지 않는다.
        /// </para>
        ///
        /// <para>잠금은 건드리지 않는다 — 자동 선택이 잠근 후보 중에서 뽑는 근거이기 때문이다.</para>
        /// </summary>
        private void EnsureAugmentAcquired()
        {
            if (blessingDraft == null || blessingDraft.IsSelected || selectedAugment != null)
            {
                return;
            }

            selectedAugment = blessingDraft.SelectAutomatically();
            blessingLoadout.Add(selectedAugment);
            RefreshBlessingLoadout();
            augmentOffers = CaptureBlessingDraft();
        }

        /// <summary>
        /// 예치·블라인드 베팅 명령을 받을 수 있는 상태인가 — 준비 단계이고 <b>축복 선택이 끝났어야</b> 한다.
        /// 선택 중에는 화면이 축복 카드로 덮여 있으므로 그때 들어온 명령은 화면과 어긋난 것이다.
        /// </summary>
        private bool IsBlessingCommandAllowed() =>
            phase == LocalPlayPhase.Preparation
            && blessingDraft != null
            && blessingDraft.IsSelected;

        /// <summary>
        /// 새로 고른 축복을 경제형 축복 상태에 반영한다. <b>이번 라운드의 예치·블라인드 베팅 기록은 남는다.</b>
        /// </summary>
        private void RefreshBlessingLoadout()
        {
            blessingEconomy.UpdateLoadout(blessingLoadout.Acquired);
            // 고른 순간 받는 골드(쇼 미 더 머니·탐욕·저점 매수). 같은 축복으로는 한 번뿐이다.
            // 저점 매수의 물약은 여기 쌓이고 월드가 BlessingEconomy.ConsumePendingItemGrants 로 꺼내 지급한다.
            blessingEconomy.GrantAcquisitionBonuses(blessingWallet);
        }

        /// <summary>전투 중에 축복이 번 골드(사냥꾼 처치·이자 폭발)를 받는다.</summary>
        public bool TryGrantBlessingGold(int amount)
        {
            if (amount <= 0) return false;
            blessingWallet.Earn(amount);
            return true;
        }

        /// <summary>
        /// 새 매치(또는 취소)의 경제형 축복 상태를 만든다. <b>순위표도 비운다</b> —
        /// 남기면 새 매치 1라운드에 지난 매치의 하위권 자격이 그대로 붙는다 (S-GD-RFXF8X).
        /// </summary>
        private void ResetBlessingEconomyForNewMatch()
        {
            blessingEconomy = new BlessingEconomyState();
            blessingStandings.Clear();
            blessingStandingsLocalPlayerId = 0;
            // 노출 기록은 매치 한정이다 — 새 매치는 전체 풀에서 다시 시작한다 (S-GD-H78GRS).
            exposedBlessingIds.Clear();
        }

        private IReadOnlyList<BlessingDefinition> CaptureAcquiredAugments()
        {
            IReadOnlyList<BlessingDefinition> acquired = blessingLoadout.Acquired;
            var copy = new BlessingDefinition[acquired.Count];
            for (int i = 0; i < acquired.Count; i++)
            {
                copy[i] = acquired[i];
            }

            return new ReadOnlyCollection<BlessingDefinition>(copy);
        }

        private void PrepareBlessingDraft()
        {
            if (blessingEconomy.InterestDeposited && !blessingEconomy.RoundSettled && blessingEconomy.Round < currentRound)
                throw new InvalidOperationException("The previous blessing deposit must be settled before preparation.");
            // ⚠️ 이월 정산 → 기본 지급을 **제품 경제가** 한다. 여기서 따로 지갑을 만지면
            //    상점·룰렛이 보는 잔액과 순서가 갈린다 (MatchEconomyState.BeginPreparation 주석의
            //    "지급을 먼저 하면 이번 라운드 골드까지 이월 한도에 걸린다" 도 같은 규칙이다).
            commerceEconomy.BeginPreparation();
            commerce.BeginPreparation(currentRound);

            // 지급이 끝난 뒤에 준비를 연다 — 예치 가능 여부는 이번 라운드 잔액으로 판단해야 한다.
            blessingEconomy.BeginPreparation(currentRound, blessingLoadout.Acquired);
            blessingEconomy.GrantPreparationBonuses(blessingWallet);

            blessingRandom = blessingRandom ?? new DeterministicRandom(
                unchecked(0x4C504D56 ^ (int)settings.HumanWeapon));
            // 저점 매수는 누적 순위 하위권에게만 후보로 나온다(S-GD-TMPBEF). 순위표는 월드가 넣어준 직전 표다.
            blessingDraft = BlessingDraft.Begin(
                blessingConfig,
                LocalPlayBlessingCatalog.All,
                LocalPlayBlessingCatalog.ResolveWeaponTag(settings.HumanWeapon),
                currentRound,
                blessingLoadout,
                blessingRandom,
                offerable: definition => BlessingEconomyState.IsOfferable(
                    definition, blessingStandingsLocalPlayerId, blessingStandings),
                // 한 번 뜬 축복은 이 매치에서 다시 뜨지 않는다 (S-GD-H78GRS).
                exposedIds: exposedBlessingIds);
            augmentOffers = CaptureBlessingDraft();
        }

        private LocalPlayBlessingDraftSnapshot CaptureBlessingDraft()
        {
            IReadOnlyList<BlessingCandidate> candidates = blessingDraft.Candidates;
            if (candidates.Count != LocalPlayBlessingDraftSnapshot.Count)
            {
                throw new InvalidOperationException(
                    $"Local Play 축복 후보가 {LocalPlayBlessingDraftSnapshot.Count}개가 아닙니다.");
            }

            if (!blessingDraft.TryGetCurrentRerollCost(out int currentCost)
                || !blessingDraft.TryGetRerollCostPreview(out int previewCost))
            {
                throw new InvalidOperationException($"{currentRound}라운드 재추첨 비용이 정의되지 않았습니다.");
            }

            return new LocalPlayBlessingDraftSnapshot(
                candidates[0].Blessing,
                candidates[1].Blessing,
                candidates[2].Blessing,
                blessingDraft.RerollCount,
                currentCost,
                previewCost,
                blessingWallet.Gold,
                blessingDraft.GetRerollAvailability(blessingWallet),
                candidates[0].IsLocked,
                candidates[1].IsLocked,
                candidates[2].IsLocked,
                blessingDraft.RemainingLockCount);
        }

        private static LocalPlayMap ResolveActiveMap(LocalPlayPhase value) => value switch
        {
            LocalPlayPhase.Lobby => LocalPlayMap.Preparation,
            LocalPlayPhase.PreparationLoading => LocalPlayMap.Preparation,
            LocalPlayPhase.Preparation => LocalPlayMap.Preparation,
            LocalPlayPhase.CombatLoading => LocalPlayMap.Combat,
            LocalPlayPhase.CombatCountdown => LocalPlayMap.Combat,
            LocalPlayPhase.Combat => LocalPlayMap.Combat,
            LocalPlayPhase.CombatSummary => LocalPlayMap.Combat,
            _ => LocalPlayMap.None,
        };
    }
}

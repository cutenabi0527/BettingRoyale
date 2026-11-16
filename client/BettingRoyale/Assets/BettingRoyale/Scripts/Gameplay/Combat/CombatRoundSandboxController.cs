using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Audio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BettingRoyale.Core;
using BettingRoyale.Data.Chest;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.LocalPlay;
using BettingRoyale.Data.Scenes;
using BettingRoyale.Managers;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BettingRoyale.Gameplay.Combat
{
    /// <summary>단일 라운드 Direct Play의 로컬 조정자. 전투/맵은 기존 공용 런타임을 사용한다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LocalPlayBotRoster))]
    public sealed class CombatRoundSandboxController : MonoBehaviour,
        LocalCombatInputDriver.IInputFrameConstraint, IDamageAuthority
    {
        public const int DefaultSeed = 0;
        private readonly CombatSandboxRoundState round = new();
        private readonly List<Transform> spawnAnchors = new();
        private readonly List<Transform> chestAnchors = new();
        private readonly List<Transform> scenarioSpawns = new();
        private readonly List<ICombatRoundRuntime> runtimes = new();
        private readonly Queue<string> recentEvents = new();
        private readonly CombatRoundResultState resultState = new(10d);
        private readonly CombatSandboxChestRuntime chestRuntime = new();
        private readonly Dictionary<LocalCombatInputDriver, ICombatInteractionCommandSink> chestInteractionBindings = new();
        [SerializeField] private GameObject combatChestVisualPrefab;
        private LocalPlayBotRoster roster;
        private CombatantHealth player;
        private GameObject playerObject;
        private LocalCombatInputDriver input;
        private Transform combatantsRoot;
        private Camera worldCamera;
        private CombatVisibilityPresenter visibility;
        private CombatZoneRuntime zone;
        private Rect movementBounds;
        private bool hasMapGeometry;
        private IMatchSceneProvider provider;
        private Task mapOperation;
        private Task resetOperation;
        private int mapLoadGeneration;
        private MatchSceneDescriptor map;
        private bool initialized;
        private bool disposed;
        private Rect inputSurface;
        private string warning = string.Empty;
        private float timeScaleBeforePause;
        private bool audioPausedBeforePause;
        private int settledRoundNumber;
        private CombatSandboxRoundSummary summary;

        public CombatSandboxState State => round.State;
        public bool CanEditSetup => isActiveAndEnabled && initialized && round.CanEditSetup;
        public bool IsMapReady => round.IsMapReady;
        public bool IsBusy => mapOperation != null && !mapOperation.IsCompleted;
        public bool IsActive => true;
        public int Seed { get; private set; } = DefaultSeed;
        public bool IsIsolated { get; private set; }
        public bool IsAiPaused { get; private set; }
        public bool ExperimentalZone { get; private set; } = true;
        public bool IsPaused { get; private set; }
        public bool CanPause => isActiveAndEnabled && (State == CombatSandboxState.Countdown || State == CombatSandboxState.Combat);
        public bool IsPlayerInDanger => player != null && !player.IsDowned && zone != null && zone.IsActive
            && zone.IsOutside(player.transform.position, zone.Radius);
        public CombatZoneRuntime Zone => zone;
        public string VisibilityStatus => visibility != null && visibility.IsConfigured
            ? (visibility.IgnoresTerrainFog ? "유령 · 안개 영향 없음 · 적 가이드 OFF" : "지형 시야 ON · 적 가이드 OFF") : "시야 미연결";
        public float CountdownRemaining => round.CountdownRemaining;
        public float CombatRemaining => round.CombatRemaining;
        public bool IsObservingLastSurvivor => round.IsObservingLastSurvivor;
        public float ObservationRemaining => round.ObservationRemaining;
        public CombatSandboxScenarioSnapshot Snapshot => round.Snapshot;
        public CombatSandboxRoundSummary Summary => summary;
        public CombatSandboxRoundEndReason? EndReason => round.EndReason;
        public CombatSandboxChestRuntime Chests => chestRuntime;
        public string LastError => round.LastError;
        public string StatusMessage => State == CombatSandboxState.Faulted ? LastError : warning;
        public MatchSceneDescriptor Map => map;
        public IReadOnlyList<MatchSceneDescriptor> AvailableMaps { get; private set; } = System.Array.Empty<MatchSceneDescriptor>();
        public int SpawnAnchorCount => spawnAnchors.Count;
        public int ChestAnchorCount => chestAnchors.Count;
        public LocalPlayBotRoster Roster => roster;
        public IEnumerable<string> RecentEvents => recentEvents;
        public int EventCount => recentEvents.Count;
        public event Action<CombatSandboxState> StateChanged;

        /// <summary>
        /// Direct Lab가 그릴 수 있는 이번 라운드의 사실 스냅샷. 결과 원장을 봉인하지 않고
        /// 읽기만 하며, 최종 점수·등수는 만들지 않는다(S15P21A606-170).
        /// </summary>
        public bool TryBuildLiveStandings(out CombatRoundLiveStanding[] standings)
        {
            if (State != CombatSandboxState.Combat
                || roster == null
                || !resultState.TryCopyLiveFacts(out CombatantRoundResult[] facts))
            {
                standings = Array.Empty<CombatRoundLiveStanding>();
                return false;
            }

            var rows = new CombatRoundLiveStanding[roster.Participants.Count];
            for (int i = 0; i < roster.Participants.Count; i++)
            {
                CombatantHealth actor = roster.Participants[i];
                if (actor == null)
                {
                    standings = Array.Empty<CombatRoundLiveStanding>();
                    return false;
                }

                bool isAlive = !actor.IsDowned;
                int kills = 0;
                int assists = 0;
                for (int j = 0; j < facts.Length; j++)
                {
                    if (facts[j].Combatant != actor.Id) continue;
                    isAlive = !actor.IsDowned && facts[j].Survived;
                    kills = facts[j].Kills;
                    assists = facts[j].Assists;
                    break;
                }

                rows[i] = new CombatRoundLiveStanding(
                    playerId: i,
                    combatant: actor.Id,
                    isAlive: isAlive,
                    isForfeited: false,
                    kills: kills,
                    assists: assists);
            }

            standings = rows;
            return true;
        }

        private void Awake()
        {
            combatantsRoot = FindSceneRoot("Combatants");
            Transform actor = combatantsRoot != null ? combatantsRoot.Find("PlayerCombatant") : null;
            player = actor != null ? actor.GetComponent<CombatantHealth>() : null;
            playerObject = actor != null ? actor.gameObject : null;
            // Direct / Local Play 모두 비동기 맵 로딩 전에 전투원의 표시와 이동·중력을 멈춘다.
            // Local Play는 안전한 준비 맵 배치가 끝난 뒤 자신의 수명에서 다시 활성화한다.
            if (playerObject != null) playerObject.SetActive(false);
            if (LocalPlayDevelopmentLaunch.IsRequested) { enabled = false; return; }
            roster = GetComponent<LocalPlayBotRoster>();
            worldCamera = Camera.main;
            visibility = GetComponent<CombatVisibilityPresenter>();
            zone = GetComponent<CombatZoneRuntime>();
            if (player == null || worldCamera == null)
            {
                Fault("Combatants/PlayerCombatant 또는 Main Camera 계약 누락");
                return;
            }
            input = player.GetComponent<LocalCombatInputDriver>();
            if (input == null) { Fault("플레이어 입력 계약 누락"); return; }
            AvailableMaps = CombatLabMapCatalog.Discover();
            provider = new BuiltInMatchSceneProvider(AvailableMaps);
            // 씬에 저장된 카메라 배치를 읽어 쓰지 않고 공용 정본을 적용한다 (S-CLI-6BWF48).
            // CombatLab 실측 구도가 그 공용 값으로 승격됐으므로 Direct Play 구도는 그대로 유지된다.
            SharedCombatCamera.ConfigureProjection(worldCamera);
            input.SetInputFrameConstraint(this);
            foreach (MonoBehaviour behaviour in GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour is ICombatRoundRuntime runtime) runtimes.Add(runtime);
            chestRuntime.FixtureRewardClaimed += OnFixtureRewardClaimed;
            initialized = true;
            roster.SetParticipantDamageEnabled(true);
            ApplyExecution(false);
        }

        private void Start()
        {
            if (initialized && enabled) mapOperation = LoadMapAsync(MatchSceneCatalog.DefaultCombat);
        }

        private void OnEnable()
        {
            EventBus.Subscribe<HitLanded>(OnHitLanded);
            EventBus.Subscribe<DamageApplied>(OnDamageApplied);
            EventBus.Subscribe<CombatantDowned>(OnCombatantDowned);
            EventBus.Subscribe<CombatantMoved>(OnCombatantMoved);
            EventBus.Subscribe<AttackPerformed>(OnChestAttackPerformed);
            EventBus.Subscribe<CombatantDashStarted>(OnChestDashStarted);
            EventBus.Subscribe<EnvironmentDamageObserved>(OnEnvironmentDamage);
            if (initialized && input != null)
            {
                input.SetInputFrameConstraint(this);
                if (round.IsMapReady && !ConfigureMapPresentation()) { Fault("맵 시야 복원 실패"); return; }
                ApplyExecution(false);
                NotifyState();
            }
        }

        private void OnDisable()
        {
            mapLoadGeneration++;
            RestoreGameTime();
            EventBus.Unsubscribe<HitLanded>(OnHitLanded);
            EventBus.Unsubscribe<DamageApplied>(OnDamageApplied);
            EventBus.Unsubscribe<CombatantDowned>(OnCombatantDowned);
            EventBus.Unsubscribe<CombatantMoved>(OnCombatantMoved);
            EventBus.Unsubscribe<AttackPerformed>(OnChestAttackPerformed);
            EventBus.Unsubscribe<CombatantDashStarted>(OnChestDashStarted);
            EventBus.Unsubscribe<EnvironmentDamageObserved>(OnEnvironmentDamage);
            if (!initialized) return;
            round.TryBeginReset();
            summary = null;
            ApplyExecution(false);
            CleanupRuntime();
            visibility?.Unbind();
            hasMapGeometry = false;
            if (input != null) input.ClearInputFrameConstraint(this);
            round.TryCompleteReset(round.IsMapReady);
        }

        private void OnDestroy()
        {
            disposed = true;
            mapLoadGeneration++;
            chestRuntime.FixtureRewardClaimed -= OnFixtureRewardClaimed;
            ClearChestInteractionBindings();
            chestRuntime.ResetRound();
            if (initialized) _ = ReleaseMapAsync();
        }

        private async Task ReleaseMapAsync()
        {
            if (mapOperation != null) await mapOperation;
            if (provider != null && provider.IsLoaded(map.Key)) await provider.UnloadAsync(map.Key);
        }

        private void Update()
        {
            if (!initialized || disposed || IsPaused) return;
            // 비활성화가 로딩을 취소하면 Start는 재호출되지 않는다. 이전 비동기 정리가 끝난 뒤
            // 미완료 Reset만 복구하며, 진행 중인 명시적 Reset과 새 로드를 중복 시작하지 않는다.
            if (State == CombatSandboxState.Resetting && !IsMapReady && !IsBusy
                && (resetOperation == null || resetOperation.IsCompleted))
            {
                MatchSceneKey key = CombatLabMapCatalog.TryGet(AvailableMaps, map.Key, out MatchSceneDescriptor current)
                    && current.Phase == MatchScenePhase.Combat ? current.Key : MatchSceneCatalog.DefaultCombat;
                mapOperation = LoadMapAsync(key);
                return;
            }
            if (State == CombatSandboxState.Countdown)
                EventBus.Publish(new SoundStageChanged(SoundId.CombatBgm, CountdownRemaining));
            if (round.AdvanceCountdown(Time.deltaTime))
            {
                BeginCombatRuntime();
                return;
            }
            if (State != CombatSandboxState.Combat) return;
            chestRuntime.Tick(Time.deltaTime);
            EvaluateSurvivors();
            if (State != CombatSandboxState.Combat) return;
            if (round.AdvanceCombat(Time.deltaTime)) FinalizeRound();
        }

        private bool BeginCombatRuntime()
        {
            try
            {
                foreach (ICombatRoundRuntime runtime in runtimes) runtime.BeginCombat();
                if (!chestRuntime.BeginCombat()) throw new InvalidOperationException("Experimental chest prefab/lifecycle contract is missing.");
                ApplyExecution(true);
                NotifyState();
                return true;
            }
            catch (Exception exception) { Fault("전투 시스템 시작 실패", exception); return false; }
        }

        private void LateUpdate()
        {
            if (initialized && player != null && worldCamera != null)
            {
                CombatantMotor motor = player.GetComponent<CombatantMotor>();
                // 유령 상태에서는 유령 위치를 계속 비춘다(기존 계약). 구도는 공용 추적을 그대로 쓴다.
                Vector3 target = motor.IsGhostActive ? motor.GhostPosition : player.transform.position;
                SharedCombatCamera.Follow(worldCamera, target);
                visibility?.SetDangerZone(zone != null && zone.IsActive, zone != null ? zone.Center : Vector3.zero,
                    zone != null ? zone.Radius : 0);
            }
        }

        public Task ChangeMapAsync(MatchSceneKey key)
        {
            if (!CanEditSetup || IsBusy) return Task.CompletedTask;
            mapOperation = LoadMapAsync(key);
            return mapOperation;
        }

        private async Task LoadMapAsync(MatchSceneKey key)
        {
            if (!CombatLabMapCatalog.TryGet(AvailableMaps, key, out MatchSceneDescriptor selected)
                || selected.Phase != MatchScenePhase.Combat) { Fault("등록되지 않은 전투 맵"); return; }
            if (!round.TryBeginLoading()) return;
            int operationGeneration = ++mapLoadGeneration;
            warning = "맵 로딩 중";
            ApplyExecution(false);
            HideParticipantsForMapLoad();
            if (!CleanupRuntime()) return;
            visibility?.Unbind();
            hasMapGeometry = false;
            NotifyState();
            try
            {
                // Sandbox가 활성화한 Combat Map은 한 개만 유지한다.
                foreach (MatchSceneDescriptor candidate in AvailableMaps)
                {
                    if (candidate.Key.Equals(key) || !provider.IsLoaded(candidate.Key)) continue;
                    MatchSceneLoadResult removed = await provider.UnloadAsync(candidate.Key);
                    if (!IsCurrentMapLoad(operationGeneration)) return;
                    if (!removed.Succeeded) { Fault("이전 맵 언로드 실패: " + removed.Code); return; }
                }
                if (!IsCurrentMapLoad(operationGeneration)) return;
                map = selected;
                MatchSceneLoadResult loaded = await provider.LoadAsync(key);
                if (!IsCurrentMapLoad(operationGeneration))
                {
                    if (loaded.Succeeded && provider.IsLoaded(key)) await provider.UnloadAsync(key);
                    return;
                }
                if (!loaded.Succeeded) { Fault("맵 로드 실패: " + loaded.Code); return; }
                // ⚠️ 두 실패를 한 문구로 합치지 않는다. 빌드에서 Faulted 가 났을 때 무엇이 깨졌는지
                //    알 수 없었고, 실제 원인을 찾는 데 정적 분석만 반복하게 됐다 (S15P21A606-179).
                if (!ResolveAnchors(out string anchorFailure)) { Fault("맵 앵커 해석 실패 — " + anchorFailure); return; }
                if (!RevealParticipantsAfterMapReady())
                { Fault("참가자 배치 실패 — roster 조립 또는 스폰 지점 재배치가 거부됐습니다."); return; }
                if (!ConfigureMapPresentation()) { Fault("맵 Collision 경계 또는 시야 셰이더 계약 누락"); return; }
                round.TryCompleteLoading();
                warning = string.Empty;
                ApplyExecution(false);
                NotifyState();
            }
            catch (Exception exception)
            {
                if (IsCurrentMapLoad(operationGeneration)) Fault("맵 로드 중 예외", exception);
            }
        }

        private bool IsCurrentMapLoad(int operationGeneration) =>
            !disposed && isActiveAndEnabled && operationGeneration == mapLoadGeneration;

        /// <summary>앵커를 해석한다. 실패하면 <b>어느 관문에서 막혔는지</b>를 문자열로 돌려준다.</summary>
        private bool ResolveAnchors(out string failureReason)
        {
            failureReason = string.Empty;
            if (provider == null) { failureReason = "맵 provider 가 없습니다."; return false; }
            if (!provider.IsLoaded(map.Key))
            {
                failureReason = $"provider 가 '{map.BuiltInSceneName}' 를 로드된 것으로 보지 않습니다.";
                return false;
            }
            if (!MatchSceneContentResolver.TryGetMapRoot(map, out _, out string rootFailure))
            {
                failureReason = rootFailure;
                return false;
            }
            if (!MatchSceneContentResolver.TryGetCombatSpawnAnchors(map, 8, spawnAnchors))
            {
                failureReason = "Anchors 직속에서 CombatSpawn_01..08 을 모으지 못했습니다 "
                    + "(개수 부족이거나 번호가 비어 있습니다).";
                return false;
            }
            if (!MatchSceneContentResolver.TryGetChestAnchors(map, 8, chestAnchors))
            {
                failureReason = "Anchors 직속에서 ChestAnchor_01..08 을 모으지 못했습니다 "
                    + "(개수 부족이거나 번호가 비어 있습니다).";
                return false;
            }
            return true;
        }

        /// <summary>맵 앵커와 정적 표현을 해석한 뒤에만 사람과 봇을 활성화하고 스폰한다.</summary>
        private bool RevealParticipantsAfterMapReady()
        {
            if (!roster.HasBuilt)
            {
                playerObject.SetActive(true);
                if (!roster.Configure(player, playerObject, Array.Empty<LocalPlayBotRoster.BotSpawnSlot>(), 0)
                    || !roster.TryBuildRoster())
                {
                    playerObject.SetActive(false);
                    return false;
                }

                roster.SetParticipantDamageEnabled(true);
            }
            else
            {
                foreach (CombatantHealth participant in roster.Participants)
                    if (participant != null) participant.gameObject.SetActive(true);
            }

            return roster.TryRelocateParticipants(spawnAnchors);
        }

        private void HideParticipantsForMapLoad()
        {
            if (!roster.HasBuilt)
            {
                if (playerObject != null) playerObject.SetActive(false);
                return;
            }

            foreach (CombatantHealth participant in roster.Participants)
                if (participant != null) participant.gameObject.SetActive(false);
        }

        private bool ConfigureMapPresentation()
        {
            hasMapGeometry = MatchSceneContentResolver.TryGetCombatGeometry(map, out Collider[] blockers, out movementBounds);
            return hasMapGeometry && CombatWorldComposition.TryConfigureVisibility(
                transform,
                worldCamera,
                player,
                new[] { combatantsRoot },
                blockers,
                IndividualCombatSideResolver.Instance,
                out visibility,
                forceRebind: true);
        }

        /// <summary>Compact 빠른 시작. 단독 참가자는 명시적으로 ISOLATED를 고정한다.</summary>
        public bool StartTest()
        {
            if (!CanEditSetup) return false;
            IsIsolated = roster.Participants.Count == 1;
            return StartRound(true);
        }

        public bool StartRound(bool skipCountdown = false)
        {
            if (!CanEditSetup) return false;
            if (!ResolveAnchors(out string snapshotFailure))
            { Fault("맵 앵커 계약이 변경됐습니다 — " + snapshotFailure); return false; }
            var participants = new CombatSandboxScenarioSnapshot.Participant[roster.Participants.Count];
            for (int i = 0; i < participants.Length; i++)
            {
                CombatantHealth actor = roster.Participants[i];
                if (actor == null) { Fault("로스터 참가자 누락"); return false; }
                participants[i] = new CombatSandboxScenarioSnapshot.Participant(actor.Id.Value,
                    actor.GetComponent<WeaponAttackController>().EquippedWeapon, actor.IsDamageSuppressed);
            }
            var snapshot = new CombatSandboxScenarioSnapshot(map.Key, Seed, IsIsolated, participants, IsAiPaused, ExperimentalZone, skipCountdown);
            if (!round.TryStart(snapshot))
            {
                warning = "Full Round는 최소 2명 필요 · 단독 검증은 ISOLATED";
                return false;
            }
            return PrepareScenario();
        }

        public bool ReplaySameScenario()
        {
            if (!isActiveAndEnabled || !round.TryReplay()) return false;
            return PrepareScenario();
        }

        private bool PrepareScenario()
        {
            try
            {
                ApplyExecution(false);
                if (!CleanupRuntime()) return false;
                if (!ResolveAnchors(out string replayFailure)
                    || roster.Participants.Count != Snapshot.Participants.Count)
                { Fault("Replay 맵/로스터 계약 불일치 — " + replayFailure); return false; }
                IsAiPaused = Snapshot.StartWithAiPaused;
                scenarioSpawns.Clear();
                int[] indices = Snapshot.SelectSpawnIndices(spawnAnchors.Count);
                for (int i = 0; i < indices.Length; i++) scenarioSpawns.Add(spawnAnchors[indices[i]]);
                for (int i = 0; i < roster.Participants.Count; i++)
                {
                    CombatantHealth actor = roster.Participants[i];
                    if (actor == null || actor.Id.Value != Snapshot.Participants[i].Id)
                    { Fault("Replay 참가자 ID 불일치"); return false; }
                    WeaponAttackController weapon = actor.GetComponent<WeaponAttackController>();
                    weapon.SetWeaponSelectionLocked(false);
                    if (!weapon.TryEquip(Snapshot.Participants[i].Weapon)) { Fault("무기 복원 실패"); return false; }
                    actor.SetDamageSuppressed(Snapshot.Participants[i].Invulnerable);
                }
                if (!roster.TryRelocateParticipants(scenarioSpawns)) { Fault("시나리오 스폰 실패"); return false; }
                if (zone == null) { Fault("자기장 수명 슬롯 누락"); return false; }
                Vector2 center = movementBounds.center;
                var capsule = player.GetComponent<CharacterController>();
                float groundY = spawnAnchors[0].position.y
                    - (capsule.height * 0.5f - capsule.center.y) * player.transform.lossyScale.y;
                zone.Configure(roster.Participants, new Vector3(center.x, groundY, center.y),
                    Snapshot.ExperimentalZone ? CombatZoneProfile.ExperimentalForMap(movementBounds) : null);
                // PM 승인 QA profile: 10초 개봉 / 1초 재생성. 제품 기본값이 아니다.
                if (!chestRuntime.Configure(combatChestVisualPrefab, transform, chestAnchors,
                    Snapshot.SelectChestAnchorIndices(chestAnchors.Count), new ChestConfig(10, 1, 1)))
                {
                    Fault("EXPERIMENTAL 상자 prefab/8-anchor 주입 계약 누락");
                    return false;
                }
                BindChestInteractions();
                foreach (ICombatRoundRuntime runtime in runtimes) runtime.PrepareRound(Snapshot.Seed);
                settledRoundNumber++;
                resultState.BeginRound(settledRoundNumber,
                    Snapshot.Participants.Select(participant => new CombatantId(participant.Id)));
                summary = null;
                recentEvents.Clear();
                warning = string.Empty;
                ApplyExecution(false);
                NotifyState();
                if (Snapshot.SkipCountdown && round.TryBeginCombat()) return BeginCombatRuntime();
                return true;
            }
            catch (Exception exception) { Fault("시나리오 초기화 실패", exception); return false; }
        }

        /// <summary>QA 수동 종료도 같은 결과 정산 경계를 통과한다.</summary>
        public bool CompleteRound()
        {
            return isActiveAndEnabled && round.TryFinishRound(CombatSandboxRoundEndReason.Manual) && FinalizeRound();
        }

        public Task AbortRoundAsync() => State == CombatSandboxState.Countdown || State == CombatSandboxState.Combat
            ? ResetToSetupAsync() : Task.CompletedTask;

        public Task ResetToSetupAsync() => StartResetAsync(false);
        public Task ResetSandboxAsync() => StartResetAsync(true);

        private Task StartResetAsync(bool full)
        {
            if (resetOperation != null && !resetOperation.IsCompleted) return resetOperation;
            resetOperation = ResetAsync(full);
            return resetOperation;
        }

        private async Task ResetAsync(bool full)
        {
            if (!initialized || disposed) return;
            mapLoadGeneration++;
            if (!round.TryBeginReset()) return;
            RestoreGameTime();
            ApplyExecution(false);
            NotifyState();
            if (IsBusy) await mapOperation;
            if (disposed) return;
            if (!CleanupRuntime()) return;
            recentEvents.Clear();
            summary = null;
            warning = string.Empty;
            IsAiPaused = false;
            roster.SetConfigurationLocked(false);
            if (full)
            {
                roster.ClearBots();
                roster.SetParticipantDamageEnabled(true);
                Seed = DefaultSeed;
                IsIsolated = false;
                ExperimentalZone = true;
                player.GetComponent<WeaponAttackController>().SetWeaponSelectionLocked(false);
                player.GetComponent<WeaponAttackController>().TryEquip(WeaponType.Sword);
            }
            if ((full && !map.Key.Equals(MatchSceneCatalog.DefaultCombat)) || !ResolveAnchors(out _))
            {
                mapOperation = LoadMapAsync(full ? MatchSceneCatalog.DefaultCombat : map.Key);
                await mapOperation;
                return;
            }
            if (!roster.TryRelocateParticipants(spawnAnchors)) { Fault("Setup 스폰 복원 실패"); return; }
            round.TryCompleteReset(true);
            ApplyExecution(false);
            NotifyState();
        }

        public bool AddBot(WeaponType weapon)
        {
            if (!CanEditSetup || roster.BotCount >= LocalPlayBotRoster.MaxBotCount
                || (weapon != WeaponType.Sword && weapon != WeaponType.Bow)) return false;
            Transform spawn = spawnAnchors[roster.BotCount + 1];
            if (!roster.TryAddBot(spawn.position, spawn.rotation, weapon)) return false;
            ApplyExecution(false);
            warning = string.Empty;
            return true;
        }

        public bool RemoveBot(int index)
        {
            if (!CanEditSetup || !roster.TryRemoveBot(index)) return false;
            roster.TryRelocateParticipants(spawnAnchors);
            return true;
        }
        public void ClearBots() { if (CanEditSetup) roster.ClearBots(); }
        public bool SetSeed(int value) { if (!CanEditSetup) return false; Seed = value; return true; }
        public bool SetExperimentalZone(bool value) { if (!CanEditSetup) return false; ExperimentalZone = value; return true; }
        public bool SetIsolated(bool value) { if (!CanEditSetup) return false; IsIsolated = value; return true; }
        public bool SetPlayerWeapon(WeaponType weapon) => CanEditSetup
            && (weapon == WeaponType.Sword || weapon == WeaponType.Bow)
            && player.GetComponent<WeaponAttackController>().TryEquip(weapon);
        public void SetPlayerInvulnerable(bool value) { if (CanEditSetup) roster.SetHumanInvulnerable(value); }
        public void SetBotsInvulnerable(bool value) { if (CanEditSetup) roster.SetBotsInvulnerable(value); }
        public void SetAiPaused(bool value)
        {
            if (State != CombatSandboxState.Setup && State != CombatSandboxState.Combat) return;
            IsAiPaused = value;
            // 관찰 중에는 AI 토글로 전투를 되살리지 않는다 (S15P21A606-170).
            roster.SetCombatEnabled(
                State == CombatSandboxState.Combat && !value && !round.IsObservingLastSurvivor);
        }

        public bool SetPaused(bool value)
        {
            if (!CanPause) return false;
            if (IsPaused == value) return true;
            if (value)
            {
                timeScaleBeforePause = Time.timeScale;
                audioPausedBeforePause = AudioListener.pause;
                IsPaused = true;
                Time.timeScale = 0f;
                AudioListener.pause = true;
            }
            else RestoreGameTime();
            // 전투 중간 상태와 VFX는 보존한다. Stop/Reset과 달리 Clock과 명령만 잠근다.
            foreach (CombatantHealth actor in roster.Participants)
            {
                if (actor == null) continue;
                actor.GetComponent<CombatantMotor>().SetExternalSimulation(value || State != CombatSandboxState.Combat);
                actor.GetComponent<WeaponAttackController>().SetRoundExecutionEnabled(
                    !value && State == CombatSandboxState.Combat && !round.IsObservingLastSurvivor);
                if (actor.TryGetComponent(out LocalCombatBotController bot)) bot.SetSimulationPaused(value);
            }
            return true;
        }

        private void RestoreGameTime()
        {
            if (!IsPaused) return;
            IsPaused = false;
            Time.timeScale = timeScaleBeforePause;
            AudioListener.pause = audioPausedBeforePause;
        }

        public void SetInputSurface(Rect surface) => inputSurface = surface;
        public CombatMotionInputFrame Constrain(in CombatMotionInputFrame frame)
        {
            if (State != CombatSandboxState.Combat || IsPaused) return CombatMotionInputFrame.Neutral;
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 position = mouse.position.ReadValue();
                if (inputSurface.Contains(new Vector2(position.x, Screen.height - position.y)))
                    return CombatMotionInputFrame.Neutral;
            }
            // 관찰 상태에서는 이동·조준만 남긴다 (S15P21A606-170). 공격·대시·소비는 잠그되
            // 전역 정지를 걸지 않으므로 마지막 생존자는 계속 움직이며 전장을 볼 수 있다.
            if (round.IsObservingLastSurvivor)
            {
                return new CombatMotionInputFrame(
                    frame.MoveDirection, frame.AimDirection, frame.HasAim, dashPressed: false);
            }

            // 스냅샷 무기는 키보드/게임패드에서도 잠근다.
            return new CombatMotionInputFrame(frame.MoveDirection, frame.AimDirection, frame.HasAim,
                frame.DashPressed, frame.AttackPressed, false, false,
                frame.UseConsumable1Pressed, frame.UseConsumable2Pressed, frame.UseConsumable3Pressed, frame.PickupPressed,
                frame.SelectWeaponPressed, frame.SelectConsumable1Pressed, frame.SelectConsumable2Pressed,
                frame.SelectConsumable3Pressed, frame.NextEquipmentFocusPressed, frame.PreviousEquipmentFocusPressed, frame.ThrowAim,
                frame.ActiveBlessingPressed);
        }
        /// <summary>
        /// 관찰 상태에서는 어떤 피해도 적용하지 않는다 (S15P21A606-170) — 자기장 따라잡기 틱도
        /// 여기서 걸린다. 확정된 Host 사실이 관찰 도중에 달라지지 않게 하는 마지막 관문이다.
        /// </summary>
        public bool CanApplyDamage(CombatantId target, in DamageInfo info) =>
            !disposed && !IsPaused && State == CombatSandboxState.Combat
            && !round.IsObservingLastSurvivor;

        /// <summary>Environment accordion의 명시적 QA 상자 개봉 명령이다. 거리·경쟁 제품 규칙은 추가하지 않는다.</summary>
        public bool TryBeginExperimentalChestOpen(int chestIndex, int openerId)
        {
            return State == CombatSandboxState.Combat && !IsPaused
                && FindParticipant(new CombatantId(openerId)) != null
                && chestRuntime.TryBeginOpen(chestIndex, new CombatantId(openerId));
        }

        public bool TryClaimExperimentalChestReward(int chestIndex) =>
            State == CombatSandboxState.Combat && !IsPaused && chestRuntime.TryClaimFixtureReward(chestIndex);

        /// <summary>씬을 수정하지 않는 Play contract의 최소 prefab 주입 seam이다.</summary>
        public bool TrySetExperimentalChestVisualPrefabForTesting(GameObject prefab)
        {
            if (!CanEditSetup || prefab == null) return false;
            combatChestVisualPrefab = prefab;
            return true;
        }

        private void ApplyExecution(bool active)
        {
            if (roster == null || !roster.HasBuilt) return;
            roster.SetConfigurationLocked(!round.CanEditSetup);
            roster.SetCombatEnabled(active && !IsAiPaused);
            foreach (CombatantHealth actor in roster.Participants)
            {
                if (actor == null) continue;
                if (actor.TryGetComponent(out LocalCombatBotController bot)) bot.SetSimulationPaused(IsPaused);
                actor.SetDamageAuthority(this);
                CombatantMotor motor = actor.GetComponent<CombatantMotor>();
                if (hasMapGeometry) motor.SetGhostMovementBounds(movementBounds);
                motor.SetExternalSimulation(!active);
                if (!active) motor.Handle(new MoveCommand(Vector2.zero));
                WeaponAttackController weapon = actor.GetComponent<WeaponAttackController>();
                weapon.SetRoundExecutionEnabled(active);
                foreach (CombatVfxPresenter vfx in actor.GetComponentsInChildren<CombatVfxPresenter>(true))
                    vfx.SetRoundExecutionEnabled(active);
                weapon.SetWeaponSelectionLocked(!round.CanEditSetup);
            }
        }

        private bool CleanupRuntime(bool allowConfirmedHitPresentation = false)
        {
            ClearChestInteractionBindings();
            visibility?.SetDangerZone(false, Vector3.zero, 0);
            bool succeeded = true;
            try { chestRuntime.StopCombat(); chestRuntime.ResetRound(); }
            catch (Exception exception) { succeeded = false; Debug.LogException(exception, this); }
            foreach (ICombatRoundRuntime runtime in runtimes)
            {
                try { runtime.StopCombat(); } catch (Exception exception) { succeeded = false; Debug.LogException(exception, this); }
                try { runtime.ResetRound(); } catch (Exception exception) { succeeded = false; Debug.LogException(exception, this); }
            }
            if (combatantsRoot != null)
            {
                foreach (Projectile projectile in combatantsRoot.GetComponentsInChildren<Projectile>(true))
                {
                    projectile.gameObject.SetActive(false);
                    if (Application.isPlaying) Destroy(projectile.gameObject); else DestroyImmediate(projectile.gameObject);
                }
                foreach (CombatVfxPresenter vfx in combatantsRoot.GetComponentsInChildren<CombatVfxPresenter>(true))
                    vfx.SetRoundExecutionEnabled(false, allowConfirmedHitPresentation);
            }
            if (!succeeded) { round.Fail("런타임 정리 실패 · Diagnostics 확인"); NotifyState(); }
            return succeeded;
        }

        private void BindChestInteractions()
        {
            ClearChestInteractionBindings();
            foreach (CombatantHealth actor in roster.Participants)
            {
                if (actor == null || !actor.TryGetComponent(out LocalCombatInputDriver participantInput)) continue;
                ICombatInteractionCommandSink sink = chestRuntime.CreateInteractionSink(actor.Id, actor.transform);
                chestInteractionBindings.Add(participantInput, sink);
                participantInput.SetInteractionCommandSink(sink);
            }
        }

        private void ClearChestInteractionBindings()
        {
            foreach (KeyValuePair<LocalCombatInputDriver, ICombatInteractionCommandSink> binding in chestInteractionBindings)
            {
                if (binding.Key != null) binding.Key.ClearInteractionCommandSink(binding.Value);
            }
            chestInteractionBindings.Clear();
        }

        private void Fault(string message, Exception exception = null)
        {
            mapLoadGeneration++;
            RestoreGameTime();
            // 빌드에서는 DevPanel 을 볼 수 없는 상황도 있다. Player.log 에도 남긴다.
            Debug.LogError($"[CombatRoundSandboxController] {message}", this);
            round.Fail(message);
            ApplyExecution(false);
            CleanupRuntime();
            if (exception != null) Debug.LogException(exception, this);
            NotifyState();
        }
        private CombatSandboxState? lastAudioRoundState;

        private void NotifyState()
        {
            SoundId music = State switch
            {
                CombatSandboxState.Countdown => SoundId.CombatBgm,
                CombatSandboxState.Combat => SoundId.CombatBgm,
                CombatSandboxState.RoundSummary => SoundMusicTimeline.RoundResult(settledRoundNumber),
                _ => SoundId.None
            };
            EventBus.Publish(new SoundStageChanged(music,
                State == CombatSandboxState.Countdown ? CountdownRemaining : 0f));
            if (lastAudioRoundState != State)
            {
                // The sound stage owns the combat horn; no duplicate cue here.
                // The selected round-result music replaces the old placeholder stinger.
                lastAudioRoundState = State;
            }
            StateChanged?.Invoke(State);
        }
        private void Record(string message)
        {
            if (recentEvents.Count == 20) recentEvents.Dequeue();
            recentEvents.Enqueue($"{Time.time:0.00} {message}");
        }
        private CombatantHealth FindParticipant(CombatantId id)
        {
            if (roster != null) foreach (CombatantHealth actor in roster.Participants)
                if (actor != null && actor.Id.Equals(id)) return actor;
            return null;
        }
        private void OnHitLanded(HitLanded hit)
        {
            if (hit.Weapon == WeaponType.None) return; // 환경은 아래 관찰 이벤트의 원인/피해/억제를 한 행으로 표시한다.
            CombatantHealth target = FindParticipant(hit.Target);
            if (target == null) return;
            string suppression = State != CombatSandboxState.Combat ? "Phase" : target.IsDamageSuppressed ? "Invulnerable" : "Hit";
            Record($"{hit.Attacker} → {hit.Target} {hit.Weapon} · {suppression}");
        }
        private void OnDamageApplied(DamageApplied hit)
        {
            if (hit.Source != CombatDamageSource.Weapon) return;
            CombatantHealth target = FindParticipant(hit.Target);
            // 관찰 중에는 사실을 더 받지 않는다 — 피해 자체가 위에서 막히지만 기록도 닫아 둔다.
            if (target == null || State != CombatSandboxState.Combat || round.IsObservingLastSurvivor) return;
            chestRuntime.CancelFor(hit.Target, ChestCancelReason.Damaged);
            if (FindParticipant(hit.Attacker) != null) resultState.RecordDamage(hit, Time.timeAsDouble);
            Record($"{hit.Attacker} → {hit.Target} {hit.Source}/{hit.Weapon} · -{hit.Amount:0.#} HP {target.CurrentHealth:0.#}");
        }
        private void OnEnvironmentDamage(EnvironmentDamageObserved hit)
        {
            if (FindParticipant(hit.Target) != null)
            {
                if (State == CombatSandboxState.Combat && hit.AppliedDamage > 0f)
                    chestRuntime.CancelFor(hit.Target, ChestCancelReason.Damaged);
                Record($"{hit.Source} → {hit.Target} 예정 {hit.PlannedDamage:0.#} 실제 {hit.AppliedDamage:0.#} · {hit.Suppression} HP {hit.RemainingHealth:0.#} 빈사 {hit.IsDowned}");
            }
        }
        private void OnCombatantMoved(CombatantMoved moved)
        {
            if (State == CombatSandboxState.Combat) chestRuntime.CancelFor(moved.Combatant, ChestCancelReason.Moved);
        }

        private void OnChestAttackPerformed(AttackPerformed attack)
        {
            if (State == CombatSandboxState.Combat) chestRuntime.CancelFor(attack.Attacker, ChestCancelReason.Attacked);
        }

        private void OnChestDashStarted(CombatantDashStarted dash)
        {
            if (State == CombatSandboxState.Combat) chestRuntime.CancelFor(dash.Combatant, ChestCancelReason.Dashed);
        }
        private void OnCombatantDowned(CombatantDowned downed)
        {
            if (State != CombatSandboxState.Combat || round.IsObservingLastSurvivor
                || FindParticipant(downed.Combatant) == null) return;
            CombatantDowned normalized = downed;
            if (downed.Source == CombatDamageSource.Weapon && FindParticipant(downed.Attacker) == null)
                normalized = new CombatantDowned(downed.Combatant, downed.Combatant, downed.Source);
            if (!resultState.RecordDowned(normalized, Time.timeAsDouble)) return;
            EvaluateSurvivors();
        }
        /// <summary>
        /// 생존 판정을 본다. <b>마지막 생존 진영은 즉시 요약으로 넘기지 않고 관찰 상태로 들어간다</b>
        /// (S15P21A606-170) — 이동·카메라·유령 관전은 그대로이고 공격·피해만 막힌다.
        /// 전멸(NoSurvivors)은 관찰할 대상이 없으므로 기존대로 곧바로 정산한다.
        /// </summary>
        private void EvaluateSurvivors()
        {
            if (Snapshot == null || State != CombatSandboxState.Combat) return;
            var members = new List<CombatSideRules.MemberStatus>(roster.Participants.Count);
            foreach (CombatantHealth actor in roster.Participants)
            {
                if (actor == null) continue;
                bool alive = !actor.IsDowned;
                members.Add(new CombatSideRules.MemberStatus(IndividualCombatSideResolver.Instance.ResolveSideId(actor.Id), alive));
            }
            CombatSandboxRoundEndReason? reason = CombatSandboxRoundState.ResolveSurvivorEndReason(Snapshot.IsIsolated, members);
            if (!reason.HasValue) return;
            if (reason.Value == CombatSandboxRoundEndReason.SurvivingSide)
            {
                if (round.TryBeginLastSurvivorObservation(reason.Value))
                {
                    ApplyObservationExecution();
                    Record($"last surviving side · 관찰 {round.ObservationRemaining:0.#}s · 공격/피해 잠금");
                    NotifyState();
                }

                return;
            }

            if (round.TryFinishRound(reason.Value))
            {
                FinalizeRound();
            }
        }

        /// <summary>
        /// 관찰 상태의 실행 토글. <b>이동·중력·카메라·유령 관전은 건드리지 않는다</b> —
        /// 전역 정지를 걸지 않는 것이 이 상태의 요점이다. 공격 실행과 봇 전투만 끈다.
        /// </summary>
        private void ApplyObservationExecution()
        {
            if (roster == null || !roster.HasBuilt) return;
            roster.SetCombatEnabled(false);
            foreach (CombatantHealth actor in roster.Participants)
            {
                if (actor == null) continue;
                if (actor.TryGetComponent(out LocalCombatBotController bot)) bot.SetSimulationPaused(true);
                actor.GetComponent<WeaponAttackController>().SetRoundExecutionEnabled(false);
            }
        }
        private bool FinalizeRound()
        {
            if (!round.EndReason.HasValue
                || !resultState.TrySettle(Time.timeAsDouble, out CombatRoundResultsReady settled))
            {
                Fault("라운드 결과 정산 실패");
                return false;
            }
            summary = new CombatSandboxRoundSummary(round.EndReason.Value, round.CombatElapsed, round.CombatRemaining, settled);
            EventBus.Publish(settled);
            RestoreGameTime();
            ApplyExecution(false);
            // ApplyDamageCore 는 CombatantDowned 를 먼저 게시하고, HitUtility 가 그 뒤에
            // 확정 명중 표현을 게시한다. 요약 정리만 그 마지막 명중 버스트를 수용한다.
            if (!CleanupRuntime(allowConfirmedHitPresentation: true)) return false;
            NotifyState();
            return true;
        }
        private void OnFixtureRewardClaimed(CombatSandboxChestRuntime.FixtureReward reward)
        {
            Record($"EXPERIMENTAL chest {reward.ChestIndex + 1} fixture claim · opener {reward.OpenerId}");
        }
        private Transform FindSceneRoot(string nameToFind)
        {
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                if (root.name == nameToFind) return root.transform;
            return null;
        }
        public static void FollowPlayerCamera(Camera camera, Transform target, Vector3 offset, Quaternion rotation)
        {
            if (camera != null && target != null) camera.transform.SetPositionAndRotation(target.position + offset, rotation);
        }
    }
}

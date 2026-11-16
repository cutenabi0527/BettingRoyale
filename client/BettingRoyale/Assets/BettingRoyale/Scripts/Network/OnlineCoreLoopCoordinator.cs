using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BettingRoyale.Core;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Online;
using BettingRoyale.Data.Scenes;
using BettingRoyale.Managers;
using BettingRoyale.Network.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BettingRoyale.Network
{
    /// <summary>
    /// 제품 로비의 실제 Fusion 방을 유지한 채 준비·전투·정산·최종 결과·로비 복귀를 연결한다.
    /// Host만 시간을 진행하고 결과를 누적하며, 신뢰 가능한 데이터 채널로 전체 스냅샷을 전파한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OnlineCoreLoopCoordinator : MonoBehaviour
    {
        private sealed class PlayerAggregate
        {
            public PlayerAggregate(int playerId, string nickName)
            {
                PlayerId = playerId;
                NickName = nickName;
            }

            public int PlayerId { get; }
            public string NickName { get; set; }
            public int Kills { get; set; }
            public int Assists { get; set; }
            public int SurvivedRounds { get; set; }

            /// <summary>
            /// 정산 경계에서 확정한 가장 최근 라운드의 순위·점수 (S15P21A606-292 · S-ONL-9D34TP).
            /// 라운드 번호를 함께 들고 있어 받는 쪽이 지난 라운드 값을 현재 것으로 읽지 않는다.
            /// </summary>
            public OnlineCoreLoopRoundResult RoundResult { get; set; }
        }

        private const float SnapshotBroadcastIntervalSeconds = 1f;
        private const float CombatantSpawnTimeoutSeconds = 5f;
        private const float FinalResultReturnSeconds = 5f;

        private static OnlineCoreLoopCoordinator instance;

        private readonly Dictionary<int, PlayerSlot> players = new();
        private readonly Dictionary<int, PlayerAggregate> aggregates = new();
        private readonly HashSet<int> forfeitedMatchPlayers = new();
        private readonly Dictionary<int, byte> loadingProgressByPlayer = new();

        /// <summary>전투 진입 절차 — 전원 전투 맵 준비 확인 뒤 3초 카운트다운(S-GD-7RCHF8 · S-GD-C66RN8).</summary>
        private readonly CombatEntryGate combatEntry = new();
        private readonly List<int> combatEntryHumans = new();

        private OnlineCoreLoopRules rules = OnlineCoreLoopRules.Default;
        private OnlineCoreLoopSnapshot snapshot;
        private OnlineCoreLoopPhase phase = OnlineCoreLoopPhase.Lobby;
        private int currentRound;
        private float remainingSeconds;
        private float broadcastCountdown;
        private float spawnWaitSeconds;
        private int lastReceivedSequence;

        /// <summary>
        /// 진행 중인 매치의 식별자. 매치 밖에서는 빈 문자열이다 (identity-contract 2-6).
        /// </summary>
        /// <remarks>
        /// Host 는 <see cref="OnRoomMatchStarted"/> 에서 발급하고, Client 는 스냅샷으로 받아 그대로 쓴다.
        /// <b>Client 가 스스로 만들지 않는다</b> — 그러면 피어마다 값이 갈려 로그도 서버 제출도 어긋난다.
        /// </remarks>
        private string matchId = string.Empty;
        private MatchSceneKey preparationMapKey = MatchSceneCatalog.DefaultPreparation;
        private MatchSceneKey combatMapKey = MatchSceneCatalog.DefaultCombat;
        private bool isInRoom;
        private bool matchLaunchPending;
        private bool isWaitingForCombatants;
        private bool returnToLobbyRequested;
        private bool loadingGateWarningLogged;
        private string preparationBlockedReason = string.Empty;

        // ── 준비 구역 집결 (S-GD-6VWPVE) ───────────────────────────────
        // 판정은 Host 전용이다. client 는 스냅샷의 숫자만 그리고 스스로 거리를 재지 않는다
        // (S-CLI-9QYFCT 규칙 1 — 화면이 잰 값을 믿으면 맵 반대편에서 전투를 열 수 있다).

        /// <summary>Host 가 전투원 좌표를 직접 읽는 경계. 시설 구역 판정과 같은 구현을 쓴다.</summary>
        private readonly NetworkPreparationPresenceProvider rallyPresence = new();

        /// <summary>이번 틱에 Host 가 확정한 집결 현황. 스냅샷으로 전 피어에 실린다.</summary>
        private PreparationRallyStatus rallyStatus;

        /// <summary>
        /// 저작 앵커에서 읽은 집결 구역. 맵 쌍이 바뀌기 전에는 다시 읽지 않는다 —
        /// <c>TryGetMapRoot</c> 이 매 프레임 씬 루트를 훑고 배열을 만들기 때문이다.
        /// </summary>
        private PreparationRallyArea rallyArea = PreparationRallyArea.Default;
        private Transform rallyMapRoot;
        private MatchSceneKey rallyResolvedMapKey;

        public static bool IsIntegrationMatchActive =>
            instance != null && instance.snapshot.IsMatchActive;

        /// <summary>
        /// 지금 진행 중인 매치의 식별자. 매치 밖에서는 빈 문자열 (S15P21A606-69).
        /// </summary>
        /// <remarks>
        /// <b>진단 로그의 조인 키다.</b> Host 가 발급하고 Client 는 스냅샷으로 받아 그대로 쓰므로
        /// (<see cref="matchId"/> 참고) 모든 피어에서 값이 같다. 틱과 함께 찍어두면 여러 PC 의
        /// 로그를 나중에 오프라인에서 한 줄기로 병합할 수 있다 — 틱만으로는 <b>몇 번째 매치의
        /// 틱인지</b> 구분되지 않는다.
        /// </remarks>
        public static string CurrentMatchId =>
            instance != null ? instance.matchId : string.Empty;

        public static bool CanMoveCombatant =>
            instance == null || !instance.snapshot.IsMatchActive || instance.snapshot.CanMove;

        public static bool CanUseCombatActions =>
            instance == null || !instance.snapshot.IsMatchActive || instance.snapshot.CanUseCombatActions;

        public static bool CanReceiveCombatDamage =>
            instance == null || !instance.snapshot.IsMatchActive || instance.snapshot.CanReceiveCombatDamage;

        /// <summary>
        /// 지금 준비 맵의 집결 구역(준비 맵 로컬 좌표). 대기방 봇이 할 일을 마치고 기다리는 자리다(S15P21A606-326).
        /// 맵이 아직 붙지 않았으면 기본 구역을 돌려준다.
        /// </summary>
        internal static bool TryGetPreparationRallyArea(out PreparationRallyArea area)
        {
            area = PreparationRallyArea.Default;
            if (instance == null) return false;
            instance.EnsureRallyArea();
            area = instance.rallyArea;
            return true;
        }

        public static bool TryGetSnapshot(out OnlineCoreLoopSnapshot current)
        {
            if (instance == null)
            {
                current = default;
                return false;
            }

            current = instance.snapshot;
            return true;
        }

        public static bool TryApplyRules(OnlineCoreLoopRules value)
        {
            // 전용 서버 방의 방장은 클라이언트다 — 요청을 서버로 보내고 결과는 스냅샷으로 받는다 (S15P21A606-321).
            if (instance != null && instance.phase == OnlineCoreLoopPhase.Lobby
                && TryForwardToServer(RoomOwnerCommand.ApplyRules(value)))
            {
                return true;
            }

            if (instance == null || !instance.CanEditLobbyRules())
            {
                return false;
            }

            instance.rules = value;
            instance.PublishAndBroadcast(force: true);
            return true;
        }

        public static bool TryUseQuickPlaytestRules() =>
            TryApplyRules(OnlineCoreLoopRules.QuickPlaytest);

        /// <summary>
        /// 대기실 맵 선택 UI가 사용할 Host 전용 경계. 키 쌍은 하나의 저작 맵 묶음이어야 하며,
        /// 선택 결과는 라운드 시작 전에 스냅샷으로 모든 피어에게 전파된다.
        /// </summary>
        public static bool TrySetMapSelection(MatchSceneKey preparation, MatchSceneKey combat)
        {
            if (instance != null && instance.phase == OnlineCoreLoopPhase.Lobby
                && MatchSceneCatalog.IsValidSelection(preparation, combat)
                && TryForwardToServer(RoomOwnerCommand.SetMapSelection(preparation, combat)))
            {
                return true;
            }

            if (instance == null || !instance.CanEditLobbyRules()
                || !MatchSceneCatalog.IsValidSelection(preparation, combat))
            {
                return false;
            }

            instance.preparationMapKey = preparation;
            instance.combatMapKey = combat;
            instance.PublishAndBroadcast(force: true);
            Debug.Log(
                $"[OnlineCoreLoopCoordinator] 대기실 맵 선택 확정 — 준비={preparation}, 전투={combat}");
            return true;
        }

        public static bool TrySkipCurrentPhase()
        {
            if (instance != null && TryForwardToServer(RoomOwnerCommand.SkipPhase()))
            {
                return true;
            }

            if (instance == null || !instance.IsLocalHost())
            {
                return false;
            }

            switch (instance.phase)
            {
                case OnlineCoreLoopPhase.Preparation:
                    // 전투 진입은 관측·전장 기하가 모두 준비돼야 성공한다. 실패했으면 단계를
                    // 넘기지 못했다고 정직하게 알린다 — 호출자가 성공으로 오해하면 안 된다.
                    instance.BeginCombat();
                    return instance.phase == OnlineCoreLoopPhase.Combat;

                case OnlineCoreLoopPhase.Combat:
                    return instance.TrySettleCurrentRound();

                case OnlineCoreLoopPhase.Summary:
                    instance.AdvanceAfterSummary();
                    return true;

                default:
                    return false;
            }
        }

        public static bool TryReturnToLobby()
        {
            if (instance != null && instance.phase == OnlineCoreLoopPhase.Finished
                && TryForwardToServer(RoomOwnerCommand.ReturnToLobby()))
            {
                return true;
            }

            if (instance == null
                || instance.phase != OnlineCoreLoopPhase.Finished
                || !instance.IsLocalHost()
                || !NetworkManager.HasInstance
                || instance.returnToLobbyRequested)
            {
                return false;
            }

            // 즉시 버튼과 종료 카운트다운은 모두 이 경계로 들어온다. 씬 로드가 끝나기 전
            // 다음 프레임에 두 요청이 겹쳐도 한 번만 같은 방 복귀를 시작한다.
            instance.returnToLobbyRequested = true;
            instance.ResetForLobby(preserveRules: true);
            instance.PublishAndBroadcast(force: true);

            // 매치 시작 때 닫아둔 방을 다시 연다. 안 열면 재대결 대기실이 계속 "참가 불가" 로
            // 남아 새 사람이 합류할 수 없다 (S15P21A606-55).
            NetworkManager.Instance.SetRoomOpen(true);

            if (!NetworkManager.Instance.LoadLevelForRoom(SceneName.Lobby))
            {
                return false;
            }

            Debug.Log("[OnlineCoreLoopCoordinator] 같은 Fusion 세션을 유지한 채 대기실 복귀를 요청했습니다.");
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (instance != null)
            {
                return;
            }

            var coordinatorObject = new GameObject("[OnlineCoreLoopCoordinator]");
            DontDestroyOnLoad(coordinatorObject);
            instance = coordinatorObject.AddComponent<OnlineCoreLoopCoordinator>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            snapshot = BuildSnapshot();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            RebindEvents();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            UnbindEvents();
        }

        private void Update()
        {
            if (!NetworkManager.HasInstance)
            {
                return;
            }

            NetworkManager network = NetworkManager.Instance;
            isInRoom = network.IsInRoom;
            if (!isInRoom)
            {
                return;
            }

            if (!network.IsHost)
            {
                // client 는 Host 가 확정한 진행 시각만 그린다. 피해도 순위도 만들지 않는다.
                TickClientZonePresentation();
                return;
            }

            float deltaTime = Mathf.Max(0f, Time.unscaledDeltaTime);
            if (isWaitingForCombatants)
            {
                TickCombatantSpawnWait(deltaTime);
            }
            else
            {
                TickHostPhase(deltaTime);
            }

            broadcastCountdown -= deltaTime;
            if (broadcastCountdown <= 0f)
            {
                PublishAndBroadcast(force: true);
            }
        }

        private void TickCombatantSpawnWait(float deltaTime)
        {
            spawnWaitSeconds += deltaTime;

            // ⚠️ <b>여기서 매 틱 다시 보장한다</b> (S15P21A606-188). 전투원 스폰은 전장 앵커가 있어야
            //    자리를 받는데, 씬 로드 완료 시점에는 맵 결합이 아직 안 끝나 자리가 없다. 사람은
            //    참가 콜백이 여러 번 오는 덕에 결국 서지만, <b>봇은 부르는 곳이 한 번뿐이라</b>
            //    그 한 번을 놓치면 영원히 서지 않았다 — "기대 1명, 확인 2명" 으로 Loading 에 굳는다.
            //    멱등 호출이라 이미 선 전투원은 건드리지 않는다.
            if (NetworkManager.HasInstance) NetworkManager.Instance.EnsureProductCombatants();

            // 봇도 참가자다. 사람 수만 기대하면 봇이 서기 전에 Preparation 으로 넘어가고,
            // 그 뒤 배치가 실패해 전원이 맵 밖에 남는다.
            int expectedCount = Math.Max(1, ResolveExpectedCombatantCount());
            int spawnedCount = CountSpawnedNetworkCombatants();
            bool worldReady = OnlineCoreLoopWorldPresenter.IsReadyForSimulation(
                OnlineCoreLoopPhase.Loading, currentRound);
            bool everyHumanReady = LoadingProgressAverage() >= 0.999f;
            if (spawnedCount >= expectedCount && worldReady && everyHumanReady)
            {
                if (TryBeginPreparation(round: 1))
                {
                    isWaitingForCombatants = false;
                    loadingGateWarningLogged = false;
                }
                return;
            }

            if (spawnWaitSeconds < CombatantSpawnTimeoutSeconds)
            {
                return;
            }

            if (!loadingGateWarningLogged)
            {
                loadingGateWarningLogged = true;
                if (OnlineCoreLoopWorldPresenter.TryGetWorldBindingFailure(out string reason))
                {
                    Debug.LogError(
                        $"[OnlineCoreLoopCoordinator] 전투원 스폰 대기 만료 후에도 Loading을 유지합니다 — "
                        + $"기대 {expectedCount}명, 확인 {spawnedCount}명, 맵 결합 실패: {reason}");
                }
                else
                {
                    Debug.LogWarning(
                        $"[OnlineCoreLoopCoordinator] 전투원·맵 준비 대기 만료 — 기대 {expectedCount}명, "
                        + $"확인 {spawnedCount}명. Preparation으로 진행하지 않고 Loading을 유지합니다.");
                }
            }
        }

        private void TickHostPhase(float deltaTime)
        {
            switch (phase)
            {
                case OnlineCoreLoopPhase.Preparation:
                    if (!OnlineCoreLoopWorldPresenter.IsReadyForSimulation(phase, currentRound)) break;
                    if (combatEntry.Stage == CombatEntryStage.FadeOut)
                    {
                        // 화면이 다 검어진 뒤에야 전투 단계로 넘어가 순간이동한다(S-GD-4JDCGB). 막히면 다음 틱에 다시 한다.
                        combatEntry.Advance(deltaTime, CombatEntryHumans());
                        if (combatEntry.IsFadeOutComplete) BeginCombat();
                        break;
                    }

                    remainingSeconds = Mathf.Max(0f, remainingSeconds - deltaTime);
                    // 집결은 남은 시간과 **독립**이다 (S-GD-6VWPVE). 제한시간이 남아 있어도 전원이
                    // 구역 안에 들어온 순간 전투로 넘어간다 — S-GD-SJXQ2W 가 정한 "전원 준비 완료
                    // 또는 제한시간 종료" 두 종료 조건 중 앞쪽을 좌표로 판정하는 경로다.
                    EvaluateRally();
                    if ((rallyStatus.IsComplete || remainingSeconds <= 0f) && CanBeginCombat())
                    {
                        combatEntry.BeginFadeOut(currentRound);
                        Debug.Log(
                            $"[OnlineCoreLoopCoordinator] 라운드 {currentRound} 준비 종료 — "
                            + (rallyStatus.IsComplete ? "전원 집결" : "제한시간 종료") + ", 페이드 아웃");
                        PublishAndBroadcast(force: true);
                    }
                    break;

                case OnlineCoreLoopPhase.Combat:
                    if (combatEntry.IsActive)
                    {
                        // 진입 절차 중에는 관찰·정산·자기장을 흘리지 않는다. 자기장 시간은 카운트다운이 끝난 때부터 센다
                        // (S-GD-657ZN5 의 "전투 시작 카운트다운이 끝난 때부터").
                        CombatEntryStage before = combatEntry.Stage;
                        IReadOnlyCollection<int> humans = CombatEntryHumans();
                        if (combatEntry.Advance(deltaTime, humans))
                        {
                            if (combatEntry.Stage != before)
                            {
                                CombatEntryStatus entry = combatEntry.Status(humans);
                                Debug.Log(
                                    $"[OnlineCoreLoopCoordinator] 라운드 {currentRound} 전투 전환 {before} → {combatEntry.Stage}"
                                    + (before == CombatEntryStage.Loading ? $" — 준비 {entry.ReadyCount}/{entry.TotalCount}" : string.Empty));
                            }

                            PublishAndBroadcast(force: true);
                        }

                        remainingSeconds = zoneAuthority != null
                            ? Mathf.Max(0f, zoneAuthority.SecondsUntilGlobal)
                            : 0f;
                        break;
                    }

                    if (observation != null)
                    {
                        // 마지막 생존 진영 관찰 창은 Host 시간으로 흐른다 (S15P21A606-170).
                        // 창이 끝나는 순간이 이번 라운드의 정산 경계다 — 경제 반영이 거절되면
                        // 다음 틱에 같은 경계로 다시 시도한다.
                        observation.AdvanceObservation(deltaTime);
                        if (observation.IsSettled || observation.IsSettlementBoundaryReached)
                        {
                            TrySettleRoundNow(HostSimulationTime(), forced: false);
                            if (phase != OnlineCoreLoopPhase.Combat) break;
                        }
                    }
                    // ⚠️ **전투에는 시간 만료가 없다** (S-GD-657ZN5). 표시 시간은 자기장이 전역을
                    //    다 덮기까지 남은 시간이며, 0 이 되어도 정산하지 않는다. 라운드는 권위 틱
                    //    경계에서 생존 진영이 1 이하가 될 때만 끝난다.
                    //    ⚠️ 관찰 중에도 이 값의 뜻은 그대로다 — client 가 이 값으로 같은 자기장을
                    //    되돌려 그리므로 관찰 남은 시간으로 바꿔 쓰지 않는다.
                    remainingSeconds = zoneAuthority != null
                        ? Mathf.Max(0f, zoneAuthority.SecondsUntilGlobal)
                        : 0f;
                    break;

                case OnlineCoreLoopPhase.Summary:
                    remainingSeconds = Mathf.Max(0f, remainingSeconds - deltaTime);
                    if (remainingSeconds <= 0f)
                    {
                        AdvanceAfterSummary();
                    }
                    break;

                case OnlineCoreLoopPhase.Finished:
                    remainingSeconds = Mathf.Max(0f, remainingSeconds - deltaTime);
                    if (remainingSeconds <= 0f && !TryReturnToLobby())
                    {
                        Debug.LogError(
                            "[OnlineCoreLoopCoordinator] 최종 결과 자동 대기실 복귀 요청이 거부됐습니다.");
                    }
                    break;
            }

            snapshot = BuildSnapshot();
        }

        private void OnRoomJoined(RoomJoined gameEvent)
        {
            isInRoom = true;
            lastReceivedSequence = 0;
            if (gameEvent.IsHost)
            {
                ResetForLobby(preserveRules: true);
                PublishAndBroadcast(force: true);
            }
        }

        private void OnRoomLeft(RoomLeft _) =>
            ResetForLobby(preserveRules: true, preserveMapSelection: false);

        private void OnNetworkDisconnected(NetworkDisconnected _) =>
            ResetForLobby(preserveRules: true, preserveMapSelection: false);

        private void OnRoomPlayersChanged(RoomPlayersChanged gameEvent)
        {
            players.Clear();
            foreach (PlayerSlot player in gameEvent.Players)
            {
                players[player.PlayerId] = player;
                if (!aggregates.TryGetValue(player.PlayerId, out PlayerAggregate aggregate))
                {
                    aggregates[player.PlayerId] = new PlayerAggregate(player.PlayerId, player.NickName);
                }
                else
                {
                    aggregate.NickName = player.NickName;
                }
            }

            if (IsLocalHost() && !string.IsNullOrEmpty(matchId) && NetworkManager.HasInstance)
            {
                UpdateMatchForfeits();
                ApplyForfeitsToObservation();
            }

            if (IsLocalHost())
            {
                PublishAndBroadcast(force: true);
            }
        }

        private void OnRoomMatchStarted(RoomMatchStarted gameEvent)
        {
            if (!IsLocalHost() || !IsProductMatchScene(gameEvent.SceneName))
            {
                return;
            }

            matchLaunchPending = true;
            loadingProgressByPlayer.Clear();
            combatEntry.Reset();
            isWaitingForCombatants = false;
            loadingGateWarningLogged = false;
            preparationBlockedReason = string.Empty;
            currentRound = 1;

            if (!MatchSceneCatalog.IsValidSelection(preparationMapKey, combatMapKey))
            {
                preparationMapKey = MatchSceneCatalog.DefaultPreparation;
                combatMapKey = MatchSceneCatalog.DefaultCombat;
                Debug.LogWarning(
                    "[OnlineCoreLoopCoordinator] 유효하지 않은 맵 선택을 기본 맵 쌍으로 되돌렸습니다.");
            }

            // ⚠️ MatchId 는 **여기 한 곳에서만** 발급한다 (identity-contract 2-6).
            //    전원 Ready 를 통과해 Host 가 시작을 확정한 이 지점이 "매치를 시작하는 순간" 이다.
            //    재대결도 이 경로를 다시 타므로 자동으로 새 값을 받는다 — 같은 값이면 서버가
            //    두 번째 결과를 중복으로 보고 버린다.
            matchId = MatchIdentity.NewMatchId();
            Debug.Log($"[OnlineCoreLoopCoordinator] 매치를 시작합니다. MatchId={matchId}");

            // 참가자 명부를 지금 고정한다 (S15P21A606-52). 매치 도중 나간 사람도 결과에 한 줄로
            // 들어가야 하는데(S-GD-QJA6NY), 그때는 이미 PlayerInfo 도 accountId 매핑도 사라진 뒤다.
            if (NetworkManager.HasInstance)
            {
                NetworkManager.Instance.CaptureMatchRoster();

                // 무기 선택도 같은 자리에서 얼린다 (S-UX-NDANX2 · S-GD-29RMAQ).
                //
                // ⚠️ 여기서 얼리지 않으면 **씬 전환이 값을 먹는다** — PlayerInfo 는 매치 씬 전환에서
                //    사라지고, 새로 스폰된 인스턴스의 WeaponChoice 는 복제 기본값 0(= 검)이다.
                //    활을 고른 사람이 전투에서 검을 들던 원인이다.
                //
                //    ⚠️ 명부 뜨기와 **같은 프레임**이어야 한다. 이 시점의 확정값을 축복 후보 필터
                //    (ResolveParticipantWeapon)와 전투 장착(ApplyLobbyWeaponChoice)이 함께 읽는다.
                NetworkManager.Instance.CaptureMatchWeaponChoices();
                forfeitedMatchPlayers.Clear();
                lastCommittedRound = 0;
                resultsSubmitted = false;
            }

            // ⚠️ **누적을 여기서 비운다 — 안 비우면 최종 순위표에 같은 사람이 여러 줄로 나온다**
            //    (2026-09-03 실측: 같은 닉네임 3줄, 그중 둘은 SURV 0).
            //
            //    aggregates 는 방에 들어온 사람마다 만들어지고(OnRoomPlayersChanged) **추가만 된다.**
            //    players 는 매번 비우지만 aggregates 는 ResetForLobby 까지 남는다. 그런데
            //    PlayerRef 는 세션마다 재사용되므로, 나갔다 다시 들어온 사람은 **다른 PlayerId** 로
            //    새 항목을 얻고 옛 항목은 그대로 남는다. BuildStandings 가 aggregates 전체를
            //    순위표에 넣으므로 그 유령들이 함께 뜬다.
            //
            //    기준은 "방에 들어온 사람" 이 아니라 **"매치에 참가한 사람"** 이다 —
            //    [S-GD-QJA6NY](../../../../../docs/specs/game-design/S-GD-QJA6NY.md) 의
            //    "N 은 매치 시작 시 참가한 계정 수, 매치 내내 고정" 과 같은 기준이다.
            //
            //    ⚠️ 시작 시점에만 비운다. 매치 도중 나간 사람의 항목은 **남겨야** 한다 —
            //    이탈자도 꼴등 점수로 결과에 한 줄 들어가야 하기 때문이다(같은 스펙).
            aggregates.Clear();

            SetPhase(OnlineCoreLoopPhase.Loading, remaining: 0f);
        }

        private void OnNetworkSnapshotReceived(OnlineCoreLoopSnapshotReceived gameEvent)
        {
            if (IsLocalHost() || gameEvent.Sequence <= lastReceivedSequence)
            {
                return;
            }

            lastReceivedSequence = gameEvent.Sequence;
            rules = gameEvent.Snapshot.Rules;
            phase = gameEvent.Snapshot.Phase;
            currentRound = gameEvent.Snapshot.CurrentRound;
            remainingSeconds = gameEvent.Snapshot.RemainingSeconds;
            // Host 가 발급한 값을 받았을 때만 남긴다. 매 스냅샷(초당 1회)마다 찍으면 로그가 덮인다.
            if (matchId != gameEvent.Snapshot.MatchId)
            {
                Debug.Log(
                    "[OnlineCoreLoopCoordinator] CLIENT MatchId 수신: " +
                    $"{(string.IsNullOrEmpty(gameEvent.Snapshot.MatchId) ? "(없음)" : gameEvent.Snapshot.MatchId)}");
            }

            matchId = gameEvent.Snapshot.MatchId;
            preparationMapKey = gameEvent.Snapshot.PreparationMapKey;
            combatMapKey = gameEvent.Snapshot.CombatMapKey;
            snapshot = gameEvent.Snapshot;
            EventBus.Publish(new OnlineCoreLoopChanged(snapshot));
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode loadMode)
        {
            RebindEvents();

            if (IsProductMatchScene(scene.name)
                && matchLaunchPending
                && IsLocalHost())
            {
                matchLaunchPending = false;
                isWaitingForCombatants = true;
                spawnWaitSeconds = 0f;
                return;
            }

            if (scene.name == SceneName.Lobby
                && NetworkManager.HasInstance
                && NetworkManager.Instance.IsInRoom
                && phase != OnlineCoreLoopPhase.Lobby)
            {
                ResetForLobby(preserveRules: true);
                if (IsLocalHost())
                {
                    PublishAndBroadcast(force: true);
                }
            }
        }

        private static bool IsProductMatchScene(string sceneName) => sceneName == SceneName.InGame;

        private bool TryBeginPreparation(int round)
        {
            if (!OnlineCoreLoopWorldPresenter.IsWorldReadyForMatch())
            {
                ReportPreparationBlocked(
                    "지정한 준비·전투 맵과 전장 경계가 준비되지 않아 Preparation을 열지 않습니다.");
                return false;
            }

            currentRound = Math.Clamp(round, 1, rules.TotalRounds);
            SetPhase(OnlineCoreLoopPhase.Preparation, rules.PreparationSeconds);
            return true;
        }

        private void BeginPreparation(int round) => TryBeginPreparation(round);

        /// <summary>
        /// 집결 구역 안 인원을 <b>Host 가 직접 센다</b> (S-GD-6VWPVE). 숫자가 바뀐 틱에만 전파한다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 좌표는 <see cref="NetworkPreparationPresenceProvider"/> 로 읽는다 — 시설 구역 판정과
        /// <b>같은 구현</b>이다. 전투원이 아직 스폰되지 않았거나 이미 사라졌으면 <b>구역 밖으로 센다</b>:
        /// 통과시키면 스폰 전 한 프레임에 전원 집결로 보여 준비 단계가 즉시 끝난다
        /// (S-CLI-9QYFCT 규칙 5 와 같은 판단).
        /// </para>
        /// <para>
        /// 총원은 <b>지금 방에 있는 참가자</b>다. 매치 시작 명부로 세면 중간에 나간 사람 때문에
        /// 남은 인원이 아무리 모여도 영원히 전원이 되지 않는다.
        /// </para>
        /// </remarks>
        private void EvaluateRally()
        {
            EnsureRallyArea();

            var inside = 0;
            var total = 0;
            foreach (int playerId in players.Keys)
            {
                if (NetworkCombatantMotion.IsLobbyBotActorId(playerId)) continue;
                total++;
                if (rallyPresence.TryGetPosition(playerId, out WorldPosition position)
                    && rallyArea.Contains(position))
                {
                    inside++;
                }
            }

            var next = new PreparationRallyStatus(inside, total);
            if (next == rallyStatus)
            {
                return;
            }

            rallyStatus = next;
            // 바뀔 때만 보낸다. 매 틱 보내면 방 인원만큼 곱해져 회선을 채우고, 1초 주기 방송만
            // 기다리면 마지막 한 명이 들어온 뒤 다른 피어의 숫자가 최대 1초 늦게 바뀐다.
            PublishAndBroadcast(force: true);
        }

        /// <summary>
        /// 집결 구역을 <b>저작 앵커에서</b> 읽는다. 맵 쌍이 바뀌기 전에는 다시 읽지 않는다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 좌표를 상수로 들고 있지 않는 이유는 하나다 — 아트가 "준비 완료 위치" 간판을 옮기면
        /// 판정도 따라가야 한다 (S-GD-S5TBXY §4). 대신 <b>매 프레임 읽지 않는다</b>:
        /// <c>TryGetMapRoot</c> 은 로드된 씬을 훑고 루트 배열을 만든다.
        /// </para>
        /// <para>
        /// 못 읽었을 때 폴백 구역으로 두는 것도 의도다. "모르면 전부 통과" 로 열면 스폰 직후
        /// 준비 단계가 0 초 만에 끝나고, 그 증상은 네트워크 문제처럼 보인다.
        /// </para>
        /// </remarks>
        private void EnsureRallyArea()
        {
            if (rallyMapRoot != null && rallyResolvedMapKey == preparationMapKey)
            {
                return;
            }

            if (!MatchSceneCatalog.TryGetForPhase(
                    preparationMapKey, MatchScenePhase.Preparation, out MatchSceneDescriptor descriptor)
                || !MatchSceneContentResolver.TryGetMapRoot(descriptor, out Transform root, out _))
            {
                // 아직 맵이 붙지 않았다. 다음 틱에 다시 읽는다 — 여기서 실패를 기록하면 로딩 중
                // 한두 프레임 때문에 실제 저작 오류가 묻힌다.
                rallyMapRoot = null;
                rallyArea = PreparationRallyArea.Default;
                return;
            }

            rallyMapRoot = root;
            rallyResolvedMapKey = preparationMapKey;

            Transform anchor = root.Find(PreparationRallyDefaults.AnchorPath);
            if (anchor == null)
            {
                rallyArea = PreparationRallyArea.Default;
                Debug.LogWarning(
                    $"[OnlineCoreLoopCoordinator] 준비맵 '{preparationMapKey}' 에 "
                    + $"{PreparationRallyDefaults.AnchorPath} 이 없어 기본 집결 구역 {rallyArea} 로 판정합니다.");
                return;
            }

            // 전투원 좌표가 준비맵 로컬로 들어오므로(NetworkPreparationPresenceProvider) 앵커도 같은
            // 공간으로 바꾼다. 월드끼리 비교하면 콜로세움을 옮기는 순간 한쪽만 따라간다.
            Vector3 local = anchor.position - OnlineCoreLoopWorldPresenter.PreparationMapOrigin;
            rallyArea = new PreparationRallyArea(
                new WorldPosition(local.x, local.y, local.z), PreparationRallyDefaults.Radius);
            Debug.Log(
                $"[OnlineCoreLoopCoordinator] 집결 구역 확정 — 맵 '{preparationMapKey}', {rallyArea}");
        }

        /// <summary>집결 현황을 비운다. 준비 단계를 벗어나거나 매치가 끝나면 숫자를 남기지 않는다.</summary>
        private void ResetRally()
        {
            rallyStatus = default;
            rallyMapRoot = null;
            rallyArea = PreparationRallyArea.Default;
            rallyResolvedMapKey = default;
        }

        private void ReportPreparationBlocked(string reason)
        {
            if (string.Equals(preparationBlockedReason, reason, StringComparison.Ordinal))
            {
                return;
            }

            preparationBlockedReason = reason;
            Debug.LogError(
                $"[OnlineCoreLoopCoordinator] 라운드 {currentRound} Preparation 진입을 보류합니다 — {reason}");
        }

        /// <summary>
        /// 전투로 넘어간다. <b>관측 무장과 실제 전장 기하로 세운 자기장이 모두 성공해야</b>
        /// 단계를 바꾼다 — 자기장 없는 전투나 원점에 세운 자기장으로 라운드를 시작하지 않는다.
        /// 실패하면 준비 단계에 머물고 다음 틱에 다시 시도한다(월드 조립을 기다리는 경우가 그렇다).
        /// </summary>
        private void BeginCombat()
        {
            if (!NetworkManager.HasInstance || NetworkManager.Instance.MatchLobbyJoinOrders.Count < 2)
            {
                ReportCombatStartBlocked("전투 시작에는 참가자가 2명 이상 필요합니다.");
                return;
            }
            if (!TryResolveBattlefieldBounds(out CombatZoneBounds battlefield)
                || !TryArmRoundObservation(battlefield))
            {
                return;
            }

            combatStartBlockedReason = string.Empty;
            // 진입 절차를 먼저 연다 — 전투 단계의 첫 스냅샷부터 이동·공격이 잠기고 화면이 검어야 한다.
            combatEntry.BeginLoading(currentRound);
            SetPhase(
                OnlineCoreLoopPhase.Combat,
                zoneAuthority != null ? zoneAuthority.SecondsUntilGlobal : 0f);
        }

        /// <summary>
        /// 전투로 넘어갈 수 있는지 페이드를 시작하기 전에 미리 본다. 넘어갈 수 없는데 화면부터 검게 만들면 검은 화면에 갇힌다.
        /// </summary>
        private bool CanBeginCombat()
        {
            if (!NetworkManager.HasInstance || NetworkManager.Instance.MatchLobbyJoinOrders.Count < 2)
            {
                ReportCombatStartBlocked("전투 시작에는 참가자가 2명 이상 필요합니다.");
                return false;
            }

            return TryResolveBattlefieldBounds(out _);
        }

        /// <summary>지금 방에 있는 사람(봇 제외). 전투 진입은 이들의 준비 보고만 기다린다.</summary>
        private IReadOnlyCollection<int> CombatEntryHumans()
        {
            combatEntryHumans.Clear();
            foreach (int playerId in players.Keys)
                if (!NetworkCombatantMotion.IsLobbyBotActorId(playerId)) combatEntryHumans.Add(playerId);
            return combatEntryHumans;
        }

        /// <summary>
        /// 한 사람이 이번 라운드 전투 맵에 섰다고 알렸다. 보낸 사람은 연결에서 판정한 값이다. Host 만 받는다.
        /// </summary>
        internal static void HostReceiveCombatEntryReady(int playerId, int round)
        {
            if (instance == null || !instance.IsLocalHost() || instance.phase != OnlineCoreLoopPhase.Combat
                || !instance.players.ContainsKey(playerId))
            {
                return;
            }

            if (instance.combatEntry.MarkReady(playerId, round))
            {
                instance.PublishAndBroadcast(force: true);
            }
        }

        /// <summary>
        /// 조립된 전장의 <b>실제</b> 경계. 같은 계층의 월드 표현기가 세운 전투 맵 벽에서만 나온다 —
        /// 아직 없으면 지어내지 않고 false 다.
        /// </summary>
        private bool TryResolveBattlefieldBounds(out CombatZoneBounds battlefield)
        {
            if (OnlineCoreLoopWorldPresenter.TryGetCombatFieldBounds(out battlefield, out string reason))
            {
                return true;
            }

            ReportCombatStartBlocked($"전장 경계를 읽지 못했습니다 — {reason}");
            return false;
        }

        /// <summary>
        /// 전투 진입 보류를 남긴다. 보류는 매 틱 재시도되므로 <b>이유가 바뀔 때만</b> 찍는다 —
        /// 매 프레임 찍으면 정작 필요한 로그가 덮인다.
        /// </summary>
        private void ReportCombatStartBlocked(string reason)
        {
            if (string.Equals(combatStartBlockedReason, reason, StringComparison.Ordinal))
            {
                return;
            }

            combatStartBlockedReason = reason;
            Debug.LogError(
                $"[OnlineCoreLoopCoordinator] 라운드 {currentRound} 전투 진입을 보류합니다 — {reason}");
        }

        private void AdvanceAfterSummary()
        {
            if (currentRound >= rules.TotalRounds)
            {
                SetPhase(OnlineCoreLoopPhase.Finished, remaining: FinalResultReturnSeconds);
                TrySubmitCanonicalFinalResults();
                return;
            }

            BeginPreparation(currentRound + 1);
        }

        // ─────────────────────────────────────────────────────────────
        // 라운드 관측·정산 (Host 전용) — S-GD-7G8ZTA · S-GD-657ZN5
        //
        // ⚠️ **LocalRoundDirector 를 찾지 않는다.** 제품 전투는 개발 씬 컴포넌트의 존재를
        //    전제로 하지 않는다 (S-CLI-RQB4QD). 관측 런타임과 자기장은 Data 계약으로 받는다.
        // ─────────────────────────────────────────────────────────────

        /// <summary>Host 관측 런타임. Data 계약 구현이며 씬 오브젝트가 아니다.</summary>
        private MatchRoundObservationRuntime observation;

        /// <summary>
        /// 관측 런타임이 쓰는 사실 기록기. 관찰 창에서 라운드 순위를 먼저 정할 때 <b>봉인하지 않은</b>
        /// 사본만 읽는다(<see cref="ICombatRoundFactRecorder.TryCopyLiveFacts"/>) — 정산은 여전히 한 번이다.
        /// </summary>
        private ICombatRoundFactRecorder observationRecorder;

        /// <summary>제품 자기장. Host 는 권위 틱으로 진행시키고 client 는 표시만 한다.</summary>
        private ICombatZoneAuthorityRuntime zoneAuthority;

        /// <summary>
        /// 지금 세워둔 자기장이 <b>어느 매치·라운드의 것인가</b>. client 는 스냅샷을 놓칠 수 있으므로
        /// (초당 1회 브로드캐스트) 값이 어긋나면 지난 라운드의 자기장을 이어 쓰지 않고 다시 세운다.
        /// </summary>
        private string zoneMatchId = string.Empty;
        private int zoneRound = -1;

        /// <summary>마지막으로 남긴 전투 진입 보류 이유. 같은 이유를 반복해 찍지 않는다.</summary>
        private string combatStartBlockedReason = string.Empty;

        private bool isObservingCombatFacts;
        private bool roundSettlementFailureLogged;

        /// <summary>
        /// 이번 라운드에 버려진 빈사를 이미 보고했는가. 라운드마다 한 번만 찍어 로그를 덮지 않는다
        /// (S15P21A606-278).
        /// </summary>
        private bool droppedDownedFactLogged;

        /// <summary>이번 라운드 정산을 이미 반영했는가. 늦은 재정산 시도를 막는다.</summary>
        private int lastCommittedRound;

        /// <summary>
        /// 전투 진입에서 명부를 고정하고 관측·자기장을 무장한다. <b>하나라도 실패하면 아무것도
        /// 남기지 않고 false 다</b> — 절반만 무장된 상태로 전투에 들어가면 라운드가 끝나지 않거나
        /// 자기장 없이 싸우게 된다.
        /// </summary>
        /// <param name="battlefield">
        /// 월드가 조립된 뒤의 실제 전장 경계. 여기서 추측하거나 기본값으로 메우지 않는다.
        /// </param>
        private bool TryArmRoundObservation(CombatZoneBounds battlefield)
        {
            DisarmRoundObservation();

            if (!IsLocalHost() || string.IsNullOrEmpty(matchId))
            {
                ReportCombatStartBlocked("Host 권위 또는 MatchId 가 없어 관측을 무장할 수 없습니다.");
                return false;
            }

            if (!CombatRoundFactRecorders.TryCreate(
                    AssistWindowSeconds,
                    (attacker, target) => observation != null && observation.AreEnemies(attacker, target),
                    out ICombatRoundFactRecorder recorder))
            {
                ReportCombatStartBlocked("라운드 사실 기록기 구현이 등록되지 않았습니다.");
                return false;
            }

            observation = new MatchRoundObservationRuntime(recorder);
            observationRecorder = recorder;
            if (!observation.Arm(matchId, currentRound, BuildRoster()))
            {
                observation = null;
                observationRecorder = null;
                ReportCombatStartBlocked(
                    "명부가 불완전해 라운드 관측을 무장하지 못했습니다 " +
                    "(대기실 입장 순서 중복·미확정 여부를 확인하세요).");
                return false;
            }

            UpdateMatchForfeits();
            ApplyForfeitsToObservation();
            roundSettlementFailureLogged = false;
            droppedDownedFactLogged = false;
            EventBus.Subscribe<DamageApplied>(OnAuthoritativeDamageApplied);
            EventBus.Subscribe<DamageAbsorbedByShield>(OnAuthoritativeShieldHit);
            EventBus.Subscribe<CombatantDowned>(OnAuthoritativeCombatantDowned);
            isObservingCombatFacts = true;

            if (!TryBeginZoneRound(
                    presentationOnly: false,
                    battlefield,
                    combatant => observation != null && observation.IsRosterCombatant(combatant)))
            {
                ReportCombatStartBlocked($"실제 전장 경계({battlefield})로 자기장을 세우지 못했습니다.");
                DisarmRoundObservation();
                return false;
            }

            Debug.Log(
                $"[OnlineCoreLoopCoordinator] 라운드 {currentRound} 관측 무장 — " +
                $"명부 {observation.RosterSize}명, 매치 {matchId}, 전장 {battlefield}");
            return true;
        }

        /// <summary>
        /// 자기장을 <b>명시한 실제 경계로</b> 세운다. 구현이 없거나 경계를 거절당하면 아무것도
        /// 남기지 않는다 — 추측 기하로 선 자기장을 쓰지 않기 때문이다.
        /// </summary>
        private bool TryBeginZoneRound(
            bool presentationOnly, CombatZoneBounds battlefield, Func<CombatantId, bool> isParticipant)
        {
            if (!CombatZoneAuthorities.TryCreate(presentationOnly, out ICombatZoneAuthorityRuntime zone))
            {
                return false;
            }

            if (!zone.TryBeginRound(isParticipant, battlefield))
            {
                zone.Dispose();
                return false;
            }

            zoneAuthority = zone;
            zoneMatchId = matchId;
            zoneRound = currentRound;
            return true;
        }

        /// <summary>
        /// 관측·자기장을 정리한다. 라운드 전환·매치 종료·연결 해제·씬 로드에서 반드시 부른다 —
        /// 남겨 두면 다음 라운드의 사실이 앞 라운드 집계로 새어 들어간다.
        /// </summary>
        private void DisarmRoundObservation()
        {
            if (isObservingCombatFacts)
            {
                EventBus.Unsubscribe<DamageApplied>(OnAuthoritativeDamageApplied);
                EventBus.Unsubscribe<DamageAbsorbedByShield>(OnAuthoritativeShieldHit);
                EventBus.Unsubscribe<CombatantDowned>(OnAuthoritativeCombatantDowned);
                isObservingCombatFacts = false;
            }

            observation?.Disarm();
            observation = null;
            observationRecorder = null;

            ReleaseZone();
        }

        /// <summary>
        /// 자기장을 멈추고 정리한다. <b>어느 라운드의 것이었는지도 함께 비운다</b> —
        /// 남겨두면 다음 라운드에서 "이미 세워져 있다" 고 오해해 지난 기하를 이어 그린다.
        /// </summary>
        private void ReleaseZone()
        {
            zoneAuthority?.StopRound();
            zoneAuthority?.Dispose();
            zoneAuthority = null;
            zoneMatchId = string.Empty;
            zoneRound = -1;
        }

        /// <summary>
        /// 매치 시작 명부 그대로의 관측 명부. <b>전투원 식별자는 Network 매핑에서 얻는다</b> —
        /// <c>CombatantId.Value</c> 와 <c>PlayerId</c> 는 같은 값이 아니다(identity-contract).
        /// </summary>
        private CombatRosterMember[] BuildRoster()
        {
            if (!NetworkManager.HasInstance) return Array.Empty<CombatRosterMember>();
            IReadOnlyDictionary<int, int> frozen = NetworkManager.Instance.MatchLobbyJoinOrders;
            var members = new List<CombatRosterMember>(frozen.Count);
            foreach (KeyValuePair<int, int> entry in frozen)
            {
                if (entry.Value <= 0) return Array.Empty<CombatRosterMember>();
                // 봇의 명부 키는 이미 전투원 식별자다 — 사람처럼 한 번 더 변환하면 다른 번호가 된다.
                CombatantId combatantId = NetworkCombatantMotion.IsLobbyBotActorId(entry.Key)
                    ? new CombatantId(entry.Key)
                    : NetworkCombatantMotion.CreateNetworkCombatantId(entry.Key);
                members.Add(new CombatRosterMember(combatantId, entry.Key, entry.Value));
            }

            return members.ToArray();
        }

        private void UpdateMatchForfeits()
        {
            if (!NetworkManager.HasInstance) return;
            if (!NetworkManager.Instance.TryGetHostActivePlayerIds(out int[] activePlayerIds)) return;
            var activeIds = new HashSet<int>(activePlayerIds);
            foreach (int playerId in NetworkManager.Instance.MatchLobbyJoinOrders.Keys)
            {
                // ⚠️ 봇은 접속자가 아니라 ActivePlayers 에 영원히 없다. 거르지 않으면 매치가 시작되는
                //    순간 모든 봇이 몰수 처리되어 전장이 즉시 비어 버린다 (S15P21A606-188).
                if (NetworkCombatantMotion.IsLobbyBotActorId(playerId)) continue;
                if (!activeIds.Contains(playerId)) forfeitedMatchPlayers.Add(playerId);
            }
        }

        private void ApplyForfeitsToObservation()
        {
            if (observation == null) return;
            foreach (int playerId in forfeitedMatchPlayers) observation.MarkForfeited(playerId);
        }

        private void OnAuthoritativeDamageApplied(DamageApplied gameEvent)
        {
            if (observation == null || !IsLocalHost() || phase != OnlineCoreLoopPhase.Combat)
            {
                return;
            }

            observation.ObserveDamage(
                gameEvent.Attacker,
                gameEvent.Target,
                gameEvent.Weapon,
                gameEvent.Amount,
                gameEvent.Source,
                HostSimulationTime());
        }

        /// <summary>보호막이 전부 받은 타격도 킬·어시스트 기여다(S-GD-Y534KH). 실제 피해량에는 넣지 않는다.</summary>
        private void OnAuthoritativeShieldHit(DamageAbsorbedByShield gameEvent)
        {
            if (observation == null || !IsLocalHost() || phase != OnlineCoreLoopPhase.Combat)
            {
                return;
            }

            observation.ObserveShieldHit(
                gameEvent.Attacker,
                gameEvent.Target,
                gameEvent.Source,
                HostSimulationTime());
        }

        private void OnAuthoritativeCombatantDowned(CombatantDowned gameEvent)
        {
            if (observation == null || !IsLocalHost() || phase != OnlineCoreLoopPhase.Combat)
            {
                return;
            }

            if (observation.ObserveDowned(
                    gameEvent.Combatant,
                    gameEvent.Attacker,
                    gameEvent.Source,
                    HostSimulationTime()))
            {
                return;
            }

            // ⚠️ 버려진 빈사는 **라운드가 끝나지 않는다**는 뜻이다. 생존 진영 수가 줄지 않으므로
            //    마지막 생존 진영 확정이 영원히 오지 않는다. 예전에는 이 경로가 조용해서 화살
            //    빈사가 통째로 사라진 것을 로그로 볼 수 없었다 (S15P21A606-278).
            if (observation.IsRosterCombatant(gameEvent.Combatant)
                && !observation.IsSettled
                && !observation.IsObservingLastSurvivor
                && !droppedDownedFactLogged)
            {
                droppedDownedFactLogged = true;
                Debug.LogError(
                    $"[OnlineCoreLoopCoordinator] 라운드 {currentRound} 빈사 사실을 버렸습니다 — " +
                    $"전투원 {gameEvent.Combatant.Value}, 출처 {gameEvent.Source}, " +
                    $"권위 틱 열림={observation.IsTickOpen}. " +
                    "이 전투원은 계속 생존자로 세어져 라운드가 끝나지 않습니다. " +
                    "틱 배치 밖에서 발행된 피해 경로를 확인하세요.");
            }
        }

        /// <summary>
        /// Host 권위 틱이 <b>시뮬레이션 전에</b> 배치를 연다. 실제 Runner 틱 번호만 쓴다 —
        /// 프레임 수·벽시계로 근사하지 않는다(S-GD-7G8ZTA).
        /// </summary>
        internal static void HostBeginAuthorityTick(int tick)
        {
            if (instance == null || instance.observation == null
                || instance.phase != OnlineCoreLoopPhase.Combat || !instance.IsLocalHost())
            {
                return;
            }

            instance.observation.BeginAuthorityTick(tick);
        }

        /// <summary>
        /// 같은 Host 틱의 <b>모든</b> 전투원·투사체 시뮬레이션이 끝난 뒤 자기장 피해를 넣고
        /// 배치를 닫은 다음에만 생존 판정·정산을 한다(S-GD-7G8ZTA · S-GD-657ZN5).
        /// </summary>
        internal static void HostAfterAuthorityTick(int tick, double simulationTime, float deltaTime)
        {
            if (instance == null || instance.observation == null
                || instance.phase != OnlineCoreLoopPhase.Combat || !instance.IsLocalHost()
                || instance.observation.IsSettled || instance.observation.OpenTick != tick
                || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime <= 0f
                || double.IsNaN(simulationTime) || double.IsInfinity(simulationTime))
            {
                return;
            }

            OnlineCoreLoopCoordinator self = instance;

            // 자기장은 마지막이다 — 같은 틱의 무기 피해·빈사가 먼저 반영돼야 킬·어시스트 귀속이 맞다.
            // ⚠️ 여기서 예외가 새면 아래 EndAuthorityTick 이 불리지 않아 관측이 그 틱에 멈추고, 이후 모든 틱이
            //    `OpenTick != tick` 으로 빠져 라운드가 끝나지 않는다(전용 서버의 자기장 그리기 예외, S15P21A606-326).
            //    자기장 한 틱을 잃더라도 라운드 판정은 이어 간다.
            try
            {
                // 전투 진입 카운트다운 중에는 자기장을 멈춘다 — 조작도 피해도 없는 동안 좁아지면 안 된다.
                if (!self.combatEntry.IsActive) self.zoneAuthority?.AdvanceAuthority(deltaTime);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            if (!self.observation.EndAuthorityTick()) return;

            // 마지막 생존 진영이 확정돼도 **여기서 요약으로 넘기지 않는다** (S15P21A606-170).
            // 관찰 상태를 열어 이동·관전을 살린 채 공격·피해만 막고, 정산은 관찰 창이 끝나는
            // 기존 라운드 종료 경계에서 한 번 한다. 같은 틱 전멸(생존 진영 0)은 관찰할 대상이
            // 없으므로 곧바로 정산한다.
            if (self.observation.RequiresLastSurvivorObservation)
            {
                self.BeginLastSurvivorObservation();
                return;
            }

            if (self.observation.IsSettlementBoundaryReached)
            {
                self.TrySettleRoundNow(simulationTime, forced: false);
            }
        }

        /// <summary>
        /// 마지막 생존 진영 관찰을 연다. 사실은 하나도 바꾸지 않고 입력·피해 게이트만 닫으며,
        /// 그 사실을 <b>즉시</b> 전파한다 — client 의 공격·피해 게이트도 같은 순간 닫혀야 한다.
        /// </summary>
        private void BeginLastSurvivorObservation()
        {
            if (observation == null || !observation.TryBeginLastSurvivorObservation())
            {
                return;
            }

            Debug.Log(
                $"[OnlineCoreLoopCoordinator] 라운드 {currentRound} 마지막 생존 진영 확정 — " +
                $"{observation.ObservationWindowSeconds:0.#}초 관찰 상태로 들어갑니다. " +
                "요약·전역 정지 없이 이동과 관전을 유지하고 공격·피해만 막습니다 (S15P21A606-170).");
            PublishAndBroadcast(force: true);
        }

        /// <summary>
        /// 라운드를 확정해 경제에 반영하고 요약으로 넘긴다.
        /// <b>경제가 반영을 확인(ack)하기 전에는 Summary 로 넘어가지 않는다</b> —
        /// EventBus 구독 순서에 정산 순서를 맡기지 않는다.
        /// </summary>
        private void TrySettleRoundNow(double occurredAt, bool forced)
        {
            if (observation == null || phase != OnlineCoreLoopPhase.Combat
                || !IsLocalHost() || !observation.ShouldSettle)
            {
                return;
            }

            if (currentRound <= lastCommittedRound)
            {
                // 같은 라운드를 두 번 확정하지 않는다.
                return;
            }

            // 개발용 SKIP 처럼 명시적인 경계가 관찰 도중에 들어올 수 있다. 정산이 관찰의 끝이므로
            // 여기서 창을 닫아 관찰 상태와 확정 사실이 갈라지지 않게 한다 (S15P21A606-170).
            observation.CompleteObservationNow();

            // 정산을 확정하는 순간 자기장 따라잡기 틱이 더 이상 사람을 죽이지 않게 먼저 멈춘다.
            zoneAuthority?.StopRound();

            if (!observation.TrySettle(occurredAt, out CombatRoundResultsReady results))
            {
                if (!roundSettlementFailureLogged)
                {
                    roundSettlementFailureLogged = true;
                    Debug.LogError(
                        "[OnlineCoreLoopCoordinator] 라운드 결과를 확정하지 못했습니다 " +
                        $"(라운드 {currentRound}, 강제={forced}).");
                }

                return;
            }

            if (!TryBuildRoundOutcomes(results, out RoundOutcome[] outcomes))
            {
                return;
            }

            // 이번 라운드 점수는 Host 의 누적 확정 점수 차이다 — 저점 매수 배율 같은 규칙이 이미
            // 경제 경계 안에서 반영된 값이라 화면이 배점을 다시 계산하지 않는다 (S-ONL-9D34TP).
            Dictionary<int, int> scoresBefore = CanonicalScoresByPlayer();

            if (!TryCommitRoundToEconomy(outcomes))
            {
                return;
            }

            lastCommittedRound = currentRound;
            ApplyAggregates(results);
            ApplyRoundResults(outcomes, scoresBefore, CanonicalScoresByPlayer());

            // 다른 소비자(UI·진단)를 위해 사실을 알린다. **정산 순서를 이 이벤트에 의존하지 않는다.**
            EventBus.Publish(results);

            DisarmRoundObservation();
            SetPhase(OnlineCoreLoopPhase.Summary, rules.SummarySeconds);
        }

        /// <summary>전투원의 라운드 사실을 찾는다. 확정 스냅샷·진행 중 사본 어느 쪽이든 같은 모양이다.</summary>
        private delegate bool RoundResultLookup(CombatantId combatant, out CombatantRoundResult result);

        private bool TryBuildRoundOutcomes(
            in CombatRoundResultsReady results, out RoundOutcome[] outcomes)
        {
            outcomes = Array.Empty<RoundOutcome>();
            if (results.Round != currentRound || results.ResultCount != observation.Roster.Count) return false;
            CombatRoundResultsReady sealedResults = results;
            return TryPlanRoundOutcomes(sealedResults.TryGetResult, logFailure: true, out outcomes);
        }

        /// <summary>
        /// 명부 전원의 사실을 경제 경계의 순위 규칙(<see cref="RoundSettlementPlanner"/>)에 넣는다.
        /// 정산과 관찰 창 순위가 <b>같은 입력 규칙</b>을 쓰도록 한 곳에 둔다 (S-GD-7G8ZTA · S-ONL-9D34TP).
        /// </summary>
        private bool TryPlanRoundOutcomes(RoundResultLookup lookup, bool logFailure, out RoundOutcome[] outcomes)
        {
            outcomes = Array.Empty<RoundOutcome>();
            IReadOnlyList<CombatRosterMember> roster = observation.Roster;
            var inputs = new List<RoundSettlementInput>(roster.Count);

            for (var i = 0; i < roster.Count; i++)
            {
                CombatRosterMember member = roster[i];
                if (!lookup(member.Combatant, out CombatantRoundResult result)) return false;
                bool forfeited = observation.IsForfeited(member.PlayerId);
                inputs.Add(new RoundSettlementInput(
                    member.PlayerId,
                    member.LobbyJoinOrder,
                    result.Survived && !forfeited,
                    result.Survived && !forfeited
                        ? (int?)null
                        : result.EliminationTick ?? observation.LastCompletedTick ?? 0,
                    result.Kills,
                    result.Assists,
                    rangedAssists: 0,
                    result.EnemyDamageDealt,
                    forfeited));
            }

            if (!RoundSettlementPlanner.TryBuild(
                    currentRound,
                    inputs,
                    roster.Count,
                    out outcomes,
                    out RoundSettlementFailure failure,
                    out RoundPlacementFailure placementFailure))
            {
                if (!logFailure) return false;
                Debug.LogError(
                    $"[OnlineCoreLoopCoordinator] 라운드 {currentRound} 순위를 정하지 못했습니다 — " +
                    $"{failure}/{placementFailure}. 집계를 반영하지 않습니다 (S-GD-7G8ZTA).");
                return false;
            }

            return true;
        }

        private bool TryCommitRoundToEconomy(RoundOutcome[] outcomes)
        {
            OnlineEconomyCoordinator economy = OnlineEconomyCoordinator.Instance;
            if (economy == null)
            {
                Debug.LogError(
                    "[OnlineCoreLoopCoordinator] 경제 조정자가 없어 라운드 결과를 반영하지 못했습니다.");
                return false;
            }

            if (!economy.TryCommitRoundResults(matchId, currentRound, outcomes))
            {
                Debug.LogError(
                    $"[OnlineCoreLoopCoordinator] 경제가 라운드 {currentRound} 결과를 거절했습니다. " +
                    "요약으로 넘어가지 않습니다.");
                return false;
            }

            return true;
        }

        private void ApplyAggregates(in CombatRoundResultsReady results)
        {
            foreach (CombatantRoundResult result in results.CopyResults())
            {
                if (observation == null || !observation.TryGetPlayerId(result.Combatant, out int playerId))
                {
                    continue;
                }

                if (!aggregates.TryGetValue(playerId, out PlayerAggregate aggregate))
                {
                    string nickName = players.TryGetValue(playerId, out PlayerSlot player)
                        ? player.NickName
                        : $"Player {playerId}";
                    aggregate = new PlayerAggregate(playerId, nickName);
                    aggregates[playerId] = aggregate;
                }

                aggregate.Kills += result.Kills;
                aggregate.Assists += result.Assists;
                if (result.Survived && !observation.IsForfeited(playerId))
                {
                    aggregate.SurvivedRounds++;
                }
            }
        }

        /// <summary>Host 경제 경계가 지금 확정한 누적 점수. 아직 라운드가 없으면 비어 있다.</summary>
        private Dictionary<int, int> CanonicalScoresByPlayer()
        {
            var scores = new Dictionary<int, int>();
            OnlineEconomyCoordinator economy = OnlineEconomyCoordinator.Instance;
            if (!IsLocalHost()
                || economy == null
                || !economy.TryBuildFinalStandings(matchId, out FinalStanding[] canonical))
            {
                return scores;
            }

            for (int i = 0; i < canonical.Length; i++)
            {
                scores[canonical[i].PlayerId] = canonical[i].Score;
            }

            return scores;
        }

        /// <summary>
        /// 정산한 라운드의 순위·점수를 참가자 줄에 남긴다 (S15P21A606-292 · S-ONL-9D34TP).
        /// 순위는 정산 계획의 유일 순위, 점수는 누적 확정 점수의 차이다.
        /// </summary>
        private void ApplyRoundResults(
            RoundOutcome[] outcomes, Dictionary<int, int> scoresBefore, Dictionary<int, int> scoresAfter)
        {
            for (int i = 0; i < outcomes.Length; i++)
            {
                RoundOutcome outcome = outcomes[i];
                if (!aggregates.TryGetValue(outcome.PlayerId, out PlayerAggregate aggregate))
                {
                    string nickName = players.TryGetValue(outcome.PlayerId, out PlayerSlot player)
                        ? player.NickName
                        : $"Player {outcome.PlayerId}";
                    aggregate = new PlayerAggregate(outcome.PlayerId, nickName);
                    aggregates[outcome.PlayerId] = aggregate;
                }

                scoresBefore.TryGetValue(outcome.PlayerId, out int before);
                scoresAfter.TryGetValue(outcome.PlayerId, out int after);
                aggregate.RoundResult = new OnlineCoreLoopRoundResult(
                    currentRound,
                    outcome.Placement,
                    Math.Max(0, after - before),
                    outcome.Kills,
                    outcome.Assists,
                    isSettled: true);
            }
        }

        /// <summary>
        /// 관찰 창이 열린 동안 Host 가 <b>그 순간의 사실</b>로 이번 라운드 순위를 정한다
        /// (S15P21A606-292 · S-ONL-9D34TP). 관찰 중에는 공격·피해가 막혀 생사가 더 바뀌지 않으므로
        /// 정산 경계와 같은 순위가 나온다. 관찰 중 이탈이 생기면 다음 방송에서 다시 정하고, 정산이 최종이다.
        /// </summary>
        /// <remarks>
        /// 원장을 봉인하지 않는다 — 봉인은 정산 경계 한 번뿐이다(S-GD-JRKHQA). 점수는 싣지 않는다.
        /// </remarks>
        private Dictionary<int, OnlineCoreLoopRoundResult> BuildObservationRoundResults()
        {
            var results = new Dictionary<int, OnlineCoreLoopRoundResult>();
            if (!IsLocalHost()
                || observation == null
                || observationRecorder == null
                || !observation.IsObservingLastSurvivor
                || observation.IsSettled
                || !observationRecorder.TryCopyLiveFacts(out CombatantRoundResult[] facts))
            {
                return results;
            }

            bool Lookup(CombatantId combatant, out CombatantRoundResult result)
            {
                for (int i = 0; i < facts.Length; i++)
                {
                    if (facts[i].Combatant == combatant)
                    {
                        result = facts[i];
                        return true;
                    }
                }

                result = default;
                return false;
            }

            if (!TryPlanRoundOutcomes(Lookup, logFailure: false, out RoundOutcome[] outcomes))
            {
                return results;
            }

            for (int i = 0; i < outcomes.Length; i++)
            {
                RoundOutcome outcome = outcomes[i];
                results[outcome.PlayerId] = new OnlineCoreLoopRoundResult(
                    currentRound,
                    outcome.Placement,
                    score: 0,
                    outcome.Kills,
                    outcome.Assists,
                    isSettled: false);
            }

            return results;
        }

        /// <summary>
        /// 최종 등수를 <b>경제 경계의 정본</b>에서 받아 서버로 넘긴다. Host 만, 매치당 한 번.
        /// </summary>
        private void TrySubmitCanonicalFinalResults()
        {
            if (!IsLocalHost() || resultsSubmitted)
            {
                return;
            }

            OnlineEconomyCoordinator economy = OnlineEconomyCoordinator.Instance;
            if (economy == null
                || !economy.TryBuildFinalStandings(matchId, out FinalStanding[] standings)
                || standings.Length == 0)
            {
                // 로그만 남기면 화면은 "결과를 기다리는 중…" 에서 영원히 멈춘다 (S-CLI-4Q6HW3).
                ReportResultSubmissionBlocked("최종 순위를 만들지 못해 결과를 기록하지 못했습니다");
                return;
            }

            // ⚠️ <b>순위에는 봇이 들어가고 제출에는 들어가지 않는다</b> (S-ONL-9GXTYQ · S15P21A606-188).
            //    배점 인원 N 은 전투 참가자 수(봇 포함)라 순위 자체는 봇을 세어야 하지만, 제출은
            //    계정 단위다 — 봇에게는 accountId 가 없고 지어내면 메달이 지급된다.
            //    여기서 거르지 않으면 MatchSubmissionBuilder 가 "매치 명부에 없습니다" 로 제출
            //    전체를 막아, **봇이 낀 매치는 사람의 결과도 사라진다.**
            var rankList = new List<PlayerRankEntry>(standings.Length);
            var scoresByAccount = new Dictionary<string, int>(standings.Length);
            IReadOnlyDictionary<int, string> roster = NetworkManager.HasInstance
                ? NetworkManager.Instance.MatchAccountRoster
                : null;
            for (var i = 0; i < standings.Length; i++)
            {
                FinalStanding standing = standings[i];
                if (NetworkCombatantMotion.IsLobbyBotActorId(standing.PlayerId)) continue;
                rankList.Add(new PlayerRankEntry(standing.PlayerId, standing.Rank));
                if (roster != null && roster.TryGetValue(standing.PlayerId, out string accountId)
                    && !string.IsNullOrEmpty(accountId))
                {
                    scoresByAccount[accountId] = standing.Score;
                }
            }

            PlayerRankEntry[] ranks = rankList.ToArray();

            // 결과 화면의 점수는 서버에 없다 — 세션 확정 점수를 여기서 넘긴다.
            if (MatchResultAdapter.HasInstance && MatchResultAdapter.Instance != null)
            {
                MatchResultAdapter.Instance.SetSessionScores(scoresByAccount);
            }

            // ⚠️ true 는 **요청을 보냈다**는 뜻이다. 서버 저장 성공은 MatchResultAdapter 가 따로 알린다.
            TrySubmitFinalResults(ranks);
        }

        private static double HostSimulationTime() =>
            NetworkManager.HasInstance && NetworkManager.Instance.TryGetHostSimulationTime(out double simulationTime)
                ? simulationTime
                : Time.unscaledTimeAsDouble;

        /// <summary>어시스트·자기장 귀속 창(S-GD-NMFBE9 확정 10초).</summary>
        private const double AssistWindowSeconds = 10d;

        // ─────────────────────────────────────────────────────────────
        // 결과 제출 — 등수를 받아서 서버로 넘긴다 (S15P21A606-52 완료 조건 4)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 최종 등수를 받아 서버에 제출한다. <b>Host 만 성공한다.</b>
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>이 메서드가 순위 계산과 제출 사이의 이음매다.</b> 등수를 만드는 쪽
        /// (배점 [S-GD-4TPCXJ](../../../../../docs/specs/game-design/S-GD-4TPCXJ.md),
        /// 인원 [S-GD-QJA6NY](../../../../../docs/specs/game-design/S-GD-QJA6NY.md))이
        /// 준비되면 여기로 넘기기만 하면 된다.
        /// </para>
        /// <para>
        /// ⚠️ <b>점수가 아니라 등수를 받는다.</b> 점수를 받아 여기서 정렬하면 동점 처리를
        /// 이 클래스가 정하게 되는데 그것은 [O-026](../../../../../docs/decisions-later.md) 이고
        /// 아직 미결이다. <c>api.md</c> 3-3 이 한 제출 안에서 <c>rank</c> 유일을 강제하므로
        /// 실제로 갈라야 하는 결정이다 — 이음매 바깥에 남긴다.
        /// </para>
        /// <para>
        /// ⚠️ <b>되돌릴 수 없다.</b> 서버는 최초 성공 제출로 결과를 영구 고정한다(api.md 3-3).
        /// 그래서 <see cref="MatchSubmissionBuilder"/> 가 계약 위반을 먼저 거른다.
        /// </para>
        /// </remarks>
        /// <returns>제출 요청을 보냈으면 <c>true</c>. 조건이 안 되면 <c>false</c> 이고 아무 일도 없다.</returns>
        public static bool TrySubmitFinalResults(IReadOnlyList<PlayerRankEntry> ranks)
        {
            if (instance == null || !instance.IsLocalHost())
            {
                Debug.LogWarning("[OnlineCoreLoopCoordinator] Host 만 결과를 제출할 수 있습니다.");
                return false;
            }

            if (string.IsNullOrEmpty(instance.matchId))
            {
                Debug.LogError("[OnlineCoreLoopCoordinator] MatchId 가 없어 결과를 제출할 수 없습니다.");
                return false;
            }

            if (!NetworkManager.HasInstance
                || !MatchResultAdapter.HasInstance || MatchResultAdapter.Instance == null)
            {
                Debug.LogError("[OnlineCoreLoopCoordinator] 결과 어댑터가 없어 제출하지 못했습니다.");
                return false;
            }

            // 전용 서버에는 로그인한 계정이 없으므로 참가자 중 최고 등수를 제출자로 쓴다 (S15P21A606-321).
            var submitter = DedicatedServerRuntime.IsActive
                ? MatchSubmissionBuilder.ResolveServerSubmitter(NetworkManager.Instance.MatchAccountRoster, ranks)
                : MatchResultAdapter.Instance.LocalAccountId;
            if (!MatchSubmissionBuilder.TryBuild(
                    NetworkManager.Instance.MatchAccountRoster,
                    ranks,
                    submitter,
                    out var results,
                    out var error))
            {
                // 서버가 400 으로 막아 주기는 하지만 그때는 이유가 화면에 안 남는다. 여기서 남긴다.
                Debug.LogError($"[OnlineCoreLoopCoordinator] 결과를 만들지 못해 제출하지 않았습니다 — {error}");
                ReportResultSubmissionBlocked("결과를 만들지 못해 기록하지 못했습니다");
                return false;
            }

            MatchResultAdapter.Instance.Submit(instance.matchId, submitter, results);
            instance.resultsSubmitted = true;
            Debug.Log($"[OnlineCoreLoopCoordinator] 결과를 제출했습니다. {results.Length}명.");
            return true;
        }

        /// <summary>이번 매치의 결과를 서버로 보냈는가. 매치가 정리될 때 함께 비운다.</summary>
        /// <remarks>
        /// 요청을 <b>보냈다</b>는 뜻이고 서버가 받았다는 뜻은 아니다 — 전송 실패·재시도는
        /// <c>MatchResultAdapter</c> 가 <c>ApiRequestFailed</c> 로 따로 알린다.
        /// 여기서 구분하려는 것은 <b>아예 시도조차 하지 않은 경우</b>다.
        /// </remarks>
        private bool resultsSubmitted;

        /// <summary>
        /// 제출 전 단계에서 막혔다는 것을 <b>화면까지</b> 전달한다.
        /// </summary>
        /// <remarks>
        /// 서버에 요청이 나가지 않았으므로 <c>ApiRequestFailed</c> 를 낼 사람이 어댑터에도 없다.
        /// 여기서 알리지 않으면 결과 화면은 "결과를 기다리는 중…" 에서 멈춘 채로 끝난다.
        /// </remarks>
        private static void ReportResultSubmissionBlocked(string reason)
        {
            Debug.LogError($"[OnlineCoreLoopCoordinator] {reason}");
            if (MatchResultAdapter.HasInstance && MatchResultAdapter.Instance != null)
            {
                MatchResultAdapter.Instance.ReportSubmissionBlocked(reason);
            }
        }

        /// <summary>
        /// 매치를 끝까지 치르고도 결과를 <b>보내지조차 않은</b> 채 정리되면 크게 남긴다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠️ <b>2026-09-16 정정.</b> 이 주석은 오랫동안 "순위 공급원이 없어서 항상 걸린다" 고
        /// 적고 있었지만 <b>사실이 아니다</b>. 전투→경제 어댑터(<see cref="TryBuildRoundOutcomes"/> +
        /// <c>RoundSettlementPlanner</c> + <c>OnlineEconomyCoordinator.TryBuildFinalStandings</c>)가
        /// 이미 있고, 2인 매치를 완주시켜 서버 <c>match_results</c> 기록과 메달 지급(1위 100 / 2위 60)까지
        /// 확인했다 (S15P21A606-199). <c>O-025</c>·<c>O-024</c> 본문도 그 시점의 기록이다.
        /// </para>
        /// <para>
        /// 그래서 이 경고는 <b>이제 상시 경고가 아니라 진짜 이상 신호다</b>: 결과가 안 올라간 것을
        /// 모르면 메달이 없는 이유를 찾는 데 시간이 든다.
        /// </para>
        /// <para>
        /// ⚠️ <b>정상 종료한 매치에서만 경고한다.</b> Host 이탈·연결 끊김으로 중간에 끝난 매치는
        /// 결과가 없는 것이 맞으므로(<c>Finished</c> 에 도달하지 못한다) 여기서 걸리지 않는다.
        /// </para>
        /// </remarks>
        private void WarnIfResultsNotSubmitted()
        {
            if (phase != OnlineCoreLoopPhase.Finished || resultsSubmitted || !IsLocalHost())
            {
                return;
            }

            Debug.LogWarning(
                "[OnlineCoreLoopCoordinator] 매치가 끝났지만 결과를 제출하지 않았습니다 — " +
                $"{nameof(TrySubmitCanonicalFinalResults)} 가 최종 순위를 만들지 못했거나 제출 전에 막혔습니다. " +
                "바로 위 오류 로그에 이유가 있습니다. " +
                $"명부 {(NetworkManager.HasInstance ? NetworkManager.Instance.MatchAccountRoster.Count : 0)}명 대기 중.");
        }

        /// <summary>SKIP도 확정된 종료 조건을 충족한 라운드만 정산한다.</summary>
        private bool TrySettleCurrentRound()
        {
            if (observation == null || !observation.ShouldSettle) return false;
            TrySettleRoundNow(HostSimulationTime(), forced: false);
            return phase == OnlineCoreLoopPhase.Summary;
        }

        /// <summary>
        /// 이번 매치에 서야 할 전투원 수. <b>봇을 포함한다</b> (S15P21A606-188).
        /// </summary>
        /// <remarks>
        /// 매치 명부가 아직 비었으면(시작 직전) 사람 수로 떨어진다 — 그때는 봇도 없다.
        /// </remarks>
        private int ResolveExpectedCombatantCount()
        {
            if (!NetworkManager.HasInstance) return players.Count;
            IReadOnlyDictionary<int, int> frozen = NetworkManager.Instance.MatchLobbyJoinOrders;
            return frozen != null && frozen.Count > 0 ? frozen.Count : players.Count;
        }

        /// <summary>
        /// 지금 서 있는 전투원 수. 같은 참가자의 중복 오브젝트는 한 명으로 센다.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>집합 키는 <c>PlayerId</c> 가 아니라 참가자 키다</b> (S15P21A606-188).
        /// 봇은 소유자가 <c>PlayerRef.None</c> 이라 <c>PlayerId</c> 가 전부 0 이다 — 그대로 세면
        /// <b>봇 7기가 하나로 뭉쳐</b> "확인 2명" 이 되고, 기대치를 영원히 못 채워 Loading 에 굳는다.
        /// </remarks>
        private static int CountSpawnedNetworkCombatants()
        {
            var participantIds = new HashSet<int>();
            NetworkCombatantMotion[] combatants = FindObjectsByType<NetworkCombatantMotion>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (NetworkCombatantMotion combatant in combatants)
            {
                if (combatant != null && combatant.Object != null && combatant.Object.IsValid)
                {
                    participantIds.Add(combatant.MatchParticipantId);
                }
            }

            return participantIds.Count;
        }

        private void SetPhase(OnlineCoreLoopPhase next, float remaining)
        {
            if (next == OnlineCoreLoopPhase.Finished)
            {
                returnToLobbyRequested = false;
            }

            // 집결 숫자는 준비 단계의 것이다. 단계를 옮길 때 비우지 않으면 다음 준비 단계가
            // 지난 라운드의 "4 / 4" 로 시작해 그 한 틱에 전투로 넘어간다.
            if (next != OnlineCoreLoopPhase.Preparation || phase != next)
            {
                rallyStatus = default;
            }

            if (next != OnlineCoreLoopPhase.Combat)
            {
                combatEntry.Reset();
            }

            phase = next;
            remainingSeconds = Mathf.Max(0f, remaining);
            snapshot = BuildSnapshot();
            Debug.Log(
                $"[OnlineCoreLoopCoordinator] 단계={phase}, 라운드={currentRound}/{rules.TotalRounds}, 남은시간={remainingSeconds:0.0}초");
            PublishAndBroadcast(force: true);
        }

        private void PublishAndBroadcast(bool force)
        {
            snapshot = BuildSnapshot();
            EventBus.Publish(new OnlineCoreLoopChanged(snapshot));
            broadcastCountdown = SnapshotBroadcastIntervalSeconds;

            if (force && IsLocalHost() && NetworkManager.HasInstance)
            {
                NetworkManager.Instance.BroadcastOnlineCoreLoopSnapshot(snapshot);
            }
        }

        private OnlineCoreLoopSnapshot BuildSnapshot() => new(
            rules,
            phase,
            currentRound,
            remainingSeconds,
            BuildStandings(),
            matchId,
            preparationMapKey,
            combatMapKey,
            observation != null && observation.IsObservingLastSurvivor,
            rallyStatus,
            LoadingProgressAverage(),
            combatEntry.Status(CombatEntryHumans()));

        private float LoadingProgressAverage()
        {
            int count = 0;
            int total = 0;
            foreach (int playerId in players.Keys)
            {
                if (NetworkCombatantMotion.IsLobbyBotActorId(playerId)) continue;
                count++;
                total += loadingProgressByPlayer.TryGetValue(playerId, out byte progress) ? progress : 0;
            }
            return count > 0 ? total / (count * 100f) : 0f;
        }

        private void OnMatchLoadingProgress(MatchLoadingProgressReceived report)
        {
            if (!IsLocalHost() || phase != OnlineCoreLoopPhase.Loading || !players.ContainsKey(report.PlayerId)) return;
            loadingProgressByPlayer[report.PlayerId] = report.Percent;
            PublishAndBroadcast(force: true);
        }

        /// <summary>
        /// Host 가 지금까지 관측한 <b>이번 라운드</b> 사실을 PlayerId 로 찾을 수 있게 모은다
        /// (S15P21A606-170). 점수·등수는 만들지 않는다 (S-GD-P7SC3Q · D-GD-7ZMMYN).
        /// </summary>
        private Dictionary<int, OnlineCoreLoopRoundFacts> BuildLiveRoundFacts()
        {
            var facts = new Dictionary<int, OnlineCoreLoopRoundFacts>();
            if (!IsLocalHost()
                || observation == null
                || !observation.TryBuildLiveStandings(out CombatRoundLiveStanding[] live))
            {
                return facts;
            }

            for (var i = 0; i < live.Length; i++)
            {
                CombatRoundLiveStanding row = live[i];
                facts[row.PlayerId] = new OnlineCoreLoopRoundFacts(
                    observation.Round, row.IsAlive, row.IsForfeited, row.Kills, row.Assists);
            }

            return facts;
        }

        private OnlineCoreLoopStanding[] BuildStandings()
        {
            foreach (PlayerSlot player in players.Values)
            {
                if (!aggregates.TryGetValue(player.PlayerId, out PlayerAggregate aggregate))
                {
                    aggregates[player.PlayerId] = new PlayerAggregate(player.PlayerId, player.NickName);
                }
                else
                {
                    aggregate.NickName = player.NickName;
                }
            }

            // ⚠️ <b>봇도 한 줄을 갖는다</b> (S15P21A606-188). <c>players</c> 는 접속자뿐이라, 봇을
            //    넣지 않으면 standings 에 봇 줄이 없어 <c>TryCountLiveParticipants</c> 가 봇의
            //    생사 사실을 세지 못한다. 그러면 HUD 가 Host 사실 대신 <b>내 화면에 보이는 것만
            //    세는 로컬 집계</b>로 떨어져, 안개 밖 봇이 빠진 숫자가 나온다 — 내가 죽어 유령이
            //    되면 시야가 열려 그때부터 맞아 보이던 것이 이 때문이다.
            if (NetworkManager.HasInstance)
            {
                foreach (int participantId in NetworkManager.Instance.MatchLobbyJoinOrders.Keys)
                {
                    if (!NetworkCombatantMotion.IsLobbyBotActorId(participantId)) continue;
                    if (aggregates.ContainsKey(participantId)) continue;
                    aggregates[participantId] = new PlayerAggregate(
                        participantId,
                        "봇 " + (participantId - NetworkCombatantMotion.LobbyBotActorIdOffset));
                }
            }

            // ⚠️ **여기서 순위를 만들지 않는다.** 정본은 경제 경계의 FinalStandings 하나뿐이다
            //    (S-GD-P7SC3Q 배점 · S-GD-MNFBV7 동점 9단계). 예전의 사전식 비교
            //    (킬 > 어시 > 생존 > PlayerId)는 스펙과 다른 두 번째 순위 규칙이었다 —
            //    같은 매치에서 화면 순위와 서버 제출 등수가 갈렸다. 그래서 제거했다.
            var scoreByPlayer = new Dictionary<int, FinalStanding>();
            OnlineEconomyCoordinator economy = OnlineEconomyCoordinator.Instance;
            if (IsLocalHost()
                && economy != null
                && economy.TryBuildFinalStandings(matchId, out FinalStanding[] canonical))
            {
                for (int i = 0; i < canonical.Length; i++)
                {
                    scoreByPlayer[canonical[i].PlayerId] = canonical[i];
                }
            }

            var ordered = new List<PlayerAggregate>(aggregates.Values);
            ordered.Sort((left, right) =>
            {
                bool hasLeft = scoreByPlayer.TryGetValue(left.PlayerId, out FinalStanding leftStanding);
                bool hasRight = scoreByPlayer.TryGetValue(right.PlayerId, out FinalStanding rightStanding);
                if (hasLeft && hasRight)
                {
                    return leftStanding.Rank.CompareTo(rightStanding.Rank);
                }

                if (hasLeft != hasRight)
                {
                    // 확정 등수가 있는 줄이 먼저 온다. 없는 줄에 임의 순위를 지어내지 않는다.
                    return hasLeft ? -1 : 1;
                }

                // 확정 등수가 아직 없다 — 안정적인 표시 순서만 준다. 이것은 순위가 아니다.
                return left.PlayerId.CompareTo(right.PlayerId);
            });

            // 진행 중인 라운드의 Host 사실. 정산 전에는 점수·등수가 없고 이 줄만 실린다.
            Dictionary<int, OnlineCoreLoopRoundFacts> liveFacts = BuildLiveRoundFacts();

            // 관찰 창에서 먼저 정한 이번 라운드 순위. 정산 뒤에는 확정값(aggregate.RoundResult)을 쓴다.
            Dictionary<int, OnlineCoreLoopRoundResult> observationResults = BuildObservationRoundResults();

            var result = new OnlineCoreLoopStanding[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                PlayerAggregate aggregate = ordered[i];
                bool hasStanding = scoreByPlayer.TryGetValue(aggregate.PlayerId, out FinalStanding standing);
                liveFacts.TryGetValue(aggregate.PlayerId, out OnlineCoreLoopRoundFacts roundFacts);
                if (!observationResults.TryGetValue(aggregate.PlayerId, out OnlineCoreLoopRoundResult roundResult))
                {
                    roundResult = aggregate.RoundResult;
                }

                result[i] = new OnlineCoreLoopStanding(
                    aggregate.PlayerId,
                    aggregate.NickName,
                    aggregate.Kills,
                    aggregate.Assists,
                    aggregate.SurvivedRounds,
                    hasStanding ? standing.Score : 0,
                    hasStanding ? standing.Rank : OnlineCoreLoopStanding.UnrankedRank,
                    roundFacts,
                    roundResult);
            }

            return result;
        }

        private void ResetForLobby(bool preserveRules, bool preserveMapSelection = true)
        {
            if (!preserveRules)
            {
                rules = OnlineCoreLoopRules.Default;
            }

            if (!preserveMapSelection)
            {
                preparationMapKey = MatchSceneCatalog.DefaultPreparation;
                combatMapKey = MatchSceneCatalog.DefaultCombat;
            }

            aggregates.Clear();
            forfeitedMatchPlayers.Clear();

            // 집결 구역은 맵 쌍에 딸린 값이라 매치 밖으로 들고 나가지 않는다. 다음 매치가 다른
            // 준비맵을 고르면 지난 맵의 원으로 판정하게 된다.
            ResetRally();

            // 관측·자기장은 매치 밖으로 절대 살아 나가지 않는다. 남기면 다음 매치의 첫 라운드가
            // 앞 매치의 명부·집계를 그대로 물려받는다.
            DisarmRoundObservation();
            lastCommittedRound = 0;
            roundSettlementFailureLogged = false;
            droppedDownedFactLogged = false;
            combatStartBlockedReason = string.Empty;
            preparationBlockedReason = string.Empty;

            // 매치를 끝까지 치르고도 결과가 서버에 안 올라간 채 정리되는가 (S15P21A606-52).
            // ⚠️ 판정은 **여기서** 한다 — Finished 진입 시점에는 아직 제출 기회가 오지 않아
            //    성공하는 경우에도 경고가 먼저 뜬다.
            WarnIfResultsNotSubmitted();

            // 다음 매치는 반드시 새 MatchId 를 받아야 하므로 여기서 비운다.
            matchId = string.Empty;
            resultsSubmitted = false;

            phase = OnlineCoreLoopPhase.Lobby;
            currentRound = 0;
            remainingSeconds = 0f;
            matchLaunchPending = false;
            isWaitingForCombatants = false;
            loadingGateWarningLogged = false;
            combatEntry.Reset();
            snapshot = BuildSnapshot();
            EventBus.Publish(new OnlineCoreLoopChanged(snapshot));
        }

        /// <summary>
        /// 내가 전용 서버 방의 방장 클라이언트면 요청을 서버로 보내고 <c>true</c>. Host 모드·서버·방장 아님은 <c>false</c>.
        /// </summary>
        private static bool TryForwardToServer(in RoomOwnerCommand command) =>
            NetworkManager.HasInstance && NetworkManager.Instance.TryForwardRoomOwnerCommand(command);

        /// <summary>
        /// 내가 방장인가 — 방장 버튼(규칙·맵·단계 넘기기·대기실 복귀)을 보일지의 기준 (S15P21A606-321).
        /// Host 모드에서는 Host, 전용 서버 방에서는 서버가 정한 방장이다.
        /// </summary>
        public static bool IsLocalRoomOwner =>
            NetworkManager.HasInstance && NetworkManager.Instance.IsInRoom && NetworkManager.Instance.IsLocalRoomOwner;

        private bool CanEditLobbyRules() =>
            phase == OnlineCoreLoopPhase.Lobby && isInRoom && IsLocalHost();

        private bool IsLocalHost() =>
            NetworkManager.HasInstance
            && NetworkManager.Instance.IsInRoom
            && NetworkManager.Instance.IsHost;

        /// <summary>
        /// client 표시 전용 자기장. Host 스냅샷의 "전역화까지 남은 시간" 을 경과 시각으로 되돌려
        /// 같은 자기장을 그린다 — <b>local 피해를 만들지 않는다</b>(S-GD-657ZN5).
        /// </summary>
        private void TickClientZonePresentation()
        {
            if (phase != OnlineCoreLoopPhase.Combat)
            {
                ReleaseZone();
                return;
            }

            // 매치·라운드가 바뀌면 다시 세운다. **중간 스냅샷을 놓쳤어도** 마찬가지다 —
            // 전투→요약→전투 사이의 스냅샷이 유실되면 단계만 보고는 라운드 교체를 알 수 없다.
            if (zoneAuthority != null
                && (zoneRound != currentRound
                    || !string.Equals(zoneMatchId, matchId, StringComparison.Ordinal)))
            {
                ReleaseZone();
            }

            if (zoneAuthority == null)
            {
                // Host 와 **같은 실제 경계**로만 그린다. 월드가 아직 없으면 원점에 지어내지 않고
                // 다음 틱에 다시 시도한다 — 표시용이라고 다른 전장을 그리면 안 된다.
                if (!OnlineCoreLoopWorldPresenter.TryGetCombatFieldBounds(
                        out CombatZoneBounds battlefield, out _)
                    || !TryBeginZoneRound(presentationOnly: true, battlefield, _ => false))
                {
                    return;
                }
            }

            float globalization = CombatZoneSettings.Confirmed.GlobalizationSeconds;
            zoneAuthority.ApplyReplicatedElapsed(Mathf.Max(0f, globalization - remainingSeconds));
        }

        private void RebindEvents()
        {
            UnbindEvents();
            if (isObservingCombatFacts)
            {
                // ⚠️ GameManager 의 EventBus.Clear()·씬 로드가 구독을 날린다. 무장 중이면 다시 건다 —
                //    안 그러면 관측이 조용히 멈추고 라운드가 영원히 끝나지 않는다.
                EventBus.Subscribe<DamageApplied>(OnAuthoritativeDamageApplied);
                EventBus.Subscribe<DamageAbsorbedByShield>(OnAuthoritativeShieldHit);
                EventBus.Subscribe<CombatantDowned>(OnAuthoritativeCombatantDowned);
            }

            EventBus.Subscribe<RoomJoined>(OnRoomJoined);
            EventBus.Subscribe<RoomLeft>(OnRoomLeft);
            EventBus.Subscribe<NetworkDisconnected>(OnNetworkDisconnected);
            EventBus.Subscribe<RoomPlayersChanged>(OnRoomPlayersChanged);
            EventBus.Subscribe<RoomMatchStarted>(OnRoomMatchStarted);
            EventBus.Subscribe<OnlineCoreLoopSnapshotReceived>(OnNetworkSnapshotReceived);
            EventBus.Subscribe<MatchLoadingProgressReceived>(OnMatchLoadingProgress);
        }

        private void UnbindEvents()
        {
            if (isObservingCombatFacts)
            {
                EventBus.Unsubscribe<DamageApplied>(OnAuthoritativeDamageApplied);
                EventBus.Unsubscribe<DamageAbsorbedByShield>(OnAuthoritativeShieldHit);
                EventBus.Unsubscribe<CombatantDowned>(OnAuthoritativeCombatantDowned);
            }

            EventBus.Unsubscribe<RoomJoined>(OnRoomJoined);
            EventBus.Unsubscribe<RoomLeft>(OnRoomLeft);
            EventBus.Unsubscribe<NetworkDisconnected>(OnNetworkDisconnected);
            EventBus.Unsubscribe<RoomPlayersChanged>(OnRoomPlayersChanged);
            EventBus.Unsubscribe<RoomMatchStarted>(OnRoomMatchStarted);
            EventBus.Unsubscribe<OnlineCoreLoopSnapshotReceived>(OnNetworkSnapshotReceived);
            EventBus.Unsubscribe<MatchLoadingProgressReceived>(OnMatchLoadingProgress);
        }
    }

    /// <summary>
    /// 코어루프 스냅샷 전송 형식. <b>버전 바이트가 다르면 해석하지 않고 버린다</b> —
    /// 구버전 피어가 신버전 payload 를 절반만 읽는 것보다 낫다.
    /// </summary>
    public static class OnlineCoreLoopProtocol
    {
        public const int Magic = 0x4252434C;

        // ⚠️ 필드를 더하거나 뺄 때마다 올린다. 받는 쪽이 버전이 다르면 **해석하지 않고 버린다** —
        //    구버전 피어가 신버전 payload 를 절반만 읽어 엉뚱한 상태로 진행하는 것보다 낫다.
        //    2: MatchId 추가 (S15P21A606-25)
        //    3: 순위표에 Host 확정 Score·Rank 추가 (S15P21A606-94 / S-GD-7G8ZTA)
        //    4: Host 확정 준비·전투 맵 키 추가 (S15P21A606-158 / S15P21A606-159)
        //    5: 마지막 생존 진영 관찰 상태 + 이번 라운드 Host 사실(생사·이탈·킬·어시) 추가
        //       (S15P21A606-170). 점수·등수는 여전히 정산 뒤에만 실린다.
        //    6: 준비 구역 집결 현황(현재 인원 / 총원) 추가 (S-GD-6VWPVE). 방 인원 상한이 8 이라
        //       두 값 모두 1바이트다.
        //    8: 줄마다 Host 가 정한 이번 라운드 결과(라운드·순위·확정 여부·라운드 점수·킬·어시) 추가
        //       (S15P21A606-292 · S-ONL-9D34TP). 관찰 창에서 순위가 먼저 실리고 정산에서 확정된다.
        //    9: 전투 진입 현황(대기 여부·카운트다운·준비 인원) 추가 (S15P21A606-326 · S-GD-C66RN8).
        //   10: 대기 여부 대신 전환 단계(페이드 아웃·검은 화면·페이드 인·카운트다운)와 단계 남은 시간 (S-GD-4JDCGB).
        public const byte Version = 10;
        private const int MaxPlayers = 8;
        private const int MaxNicknameBytes = 48;
        private const int MaxMatchIdBytes = MatchIdentity.MatchIdLength;
        private const int MaxMapKeyBytes = 64;
        private const int MaxPayloadBytes = 4096;

        public static byte[] Serialize(in OnlineCoreLoopSnapshot snapshot)
        {
            using var stream = new MemoryStream(256);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            writer.Write(Version);
            writer.Write((byte)snapshot.Phase);
            // 마지막 생존 진영 관찰 상태. client 의 공격·피해 게이트가 Host 와 같은 값을 써야 한다.
            writer.Write(snapshot.IsObservingLastSurvivor);
            writer.Write((byte)snapshot.CurrentRound);
            writer.Write((byte)snapshot.Rules.TotalRounds);
            writer.Write(Mathf.RoundToInt(snapshot.RemainingSeconds * 1000f));
            writer.Write((ushort)snapshot.Rules.PreparationSeconds);
            writer.Write((ushort)snapshot.Rules.CombatSeconds);
            writer.Write((ushort)snapshot.Rules.SummarySeconds);

            // 준비 구역 집결 현황 (S-GD-6VWPVE). 총원 0 이 "Host 가 재지 않았다" 는 뜻이라
            // 별도 플래그를 두지 않는다.
            writer.Write((byte)Math.Clamp(snapshot.Rally.InsideCount, 0, MaxPlayers));
            writer.Write((byte)Math.Clamp(snapshot.Rally.TotalCount, 0, MaxPlayers));
            writer.Write((byte)Math.Clamp(Mathf.RoundToInt(snapshot.LoadingProgress * 100f), 0, 100));

            // 준비 → 전투 전환 현황. 단계 남은 시간은 밀리초(최대 65초)로 싣는다.
            CombatEntryStatus entry = snapshot.CombatEntry;
            writer.Write((byte)entry.Stage);
            writer.Write((ushort)Math.Clamp(Mathf.RoundToInt(entry.StageRemainingSeconds * 1000f), 0, ushort.MaxValue));
            writer.Write((byte)Math.Clamp(entry.ReadyCount, 0, MaxPlayers));
            writer.Write((byte)Math.Clamp(entry.TotalCount, 0, MaxPlayers));

            // MatchId 는 하이픈을 포함한 36자 ASCII(UUID 8-4-4-4-12)다. 상한을 넘으면 자르지 않고 빈 값으로 보낸다 —
            // 잘린 식별자는 서버에서 다른 매치로 보이므로 없느니만 못하다.
            byte[] matchIdBytes = Encoding.UTF8.GetBytes(snapshot.MatchId ?? string.Empty);
            if (matchIdBytes.Length > MaxMatchIdBytes)
            {
                Debug.LogError(
                    $"[OnlineCoreLoopProtocol] MatchId 가 {matchIdBytes.Length}바이트로 상한({MaxMatchIdBytes})을 넘어 " +
                    "빈 값으로 보냅니다. MatchIdentity.NewMatchId() 로 발급하세요.");
                matchIdBytes = Array.Empty<byte>();
            }

            writer.Write((byte)matchIdBytes.Length);
            writer.Write(matchIdBytes, 0, matchIdBytes.Length);

            MatchSceneKey preparationMapKey = NormalizeMapKey(
                snapshot.PreparationMapKey, MatchScenePhase.Preparation);
            MatchSceneKey combatMapKey = NormalizeMapKey(
                snapshot.CombatMapKey, MatchScenePhase.Combat);
            WriteMapKey(writer, preparationMapKey, MatchScenePhase.Preparation);
            WriteMapKey(writer, combatMapKey, MatchScenePhase.Combat);

            OnlineCoreLoopStanding[] standings = snapshot.CopyStandings();
            int count = Math.Min(standings.Length, MaxPlayers);
            writer.Write((byte)count);
            for (int i = 0; i < count; i++)
            {
                OnlineCoreLoopStanding standing = standings[i];
                byte[] nickname = Encoding.UTF8.GetBytes(standing.NickName ?? string.Empty);
                int nicknameLength = Math.Min(nickname.Length, MaxNicknameBytes);
                writer.Write(standing.PlayerId);
                writer.Write((ushort)Math.Min(standing.Kills, ushort.MaxValue));
                writer.Write((ushort)Math.Min(standing.Assists, ushort.MaxValue));
                writer.Write((ushort)Math.Min(standing.SurvivedRounds, ushort.MaxValue));
                // Host 확정 점수·등수. client 는 이 값을 그대로 쓰고 스스로 순위를 만들지 않는다.
                writer.Write((ushort)Math.Clamp(standing.Score, 0, ushort.MaxValue));
                writer.Write((byte)Math.Clamp(standing.Rank, 0, byte.MaxValue));
                // 이번 라운드 Host 사실 (S15P21A606-170). Round 0 은 "아직 싣지 않았다" 는 뜻이다.
                OnlineCoreLoopRoundFacts roundFacts = standing.RoundFacts;
                writer.Write((byte)Math.Clamp(roundFacts.Round, 0, byte.MaxValue));
                writer.Write((byte)((roundFacts.IsAlive ? 1 : 0) | (roundFacts.IsForfeited ? 2 : 0)));
                writer.Write((ushort)Math.Min(roundFacts.Kills, ushort.MaxValue));
                writer.Write((ushort)Math.Min(roundFacts.Assists, ushort.MaxValue));
                OnlineCoreLoopRoundResult roundResult = standing.RoundResult;
                writer.Write((byte)Math.Clamp(roundResult.Round, 0, byte.MaxValue));
                writer.Write((byte)Math.Clamp(roundResult.Rank, 0, byte.MaxValue));
                writer.Write((byte)(roundResult.IsSettled ? 1 : 0));
                writer.Write((ushort)Math.Clamp(roundResult.Score, 0, ushort.MaxValue));
                writer.Write((ushort)Math.Min(roundResult.Kills, ushort.MaxValue));
                writer.Write((ushort)Math.Min(roundResult.Assists, ushort.MaxValue));
                writer.Write((byte)nicknameLength);
                writer.Write(nickname, 0, nicknameLength);
            }

            writer.Flush();
            return stream.ToArray();
        }

        public static bool TryDeserialize(ReadOnlySpan<byte> payload, out OnlineCoreLoopSnapshot snapshot)
        {
            snapshot = default;
            if (payload.Length <= 0 || payload.Length > MaxPayloadBytes)
            {
                return false;
            }

            try
            {
                using var stream = new MemoryStream(payload.ToArray(), writable: false);
                using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
                if (reader.ReadByte() != Version)
                {
                    return false;
                }

                var phase = (OnlineCoreLoopPhase)reader.ReadByte();
                bool isObservingLastSurvivor = reader.ReadBoolean();
                int currentRound = reader.ReadByte();
                int totalRounds = reader.ReadByte();
                int remainingMilliseconds = reader.ReadInt32();
                int preparationSeconds = reader.ReadUInt16();
                int combatSeconds = reader.ReadUInt16();
                int summarySeconds = reader.ReadUInt16();
                int rallyInside = reader.ReadByte();
                int rallyTotal = reader.ReadByte();
                int loadingPercent = reader.ReadByte();
                var entryStage = (CombatEntryStage)reader.ReadByte();
                int entryRemainingMilliseconds = reader.ReadUInt16();
                int entryReady = reader.ReadByte();
                int entryTotal = reader.ReadByte();
                if (rallyInside > MaxPlayers || rallyTotal > MaxPlayers || loadingPercent > 100
                    || !Enum.IsDefined(typeof(CombatEntryStage), entryStage)
                    || entryReady > MaxPlayers || entryTotal > MaxPlayers)
                {
                    return false;
                }

                int matchIdLength = reader.ReadByte();
                if (matchIdLength > MaxMatchIdBytes
                    || stream.Position + matchIdLength > stream.Length)
                {
                    return false;
                }

                string matchId = Encoding.UTF8.GetString(reader.ReadBytes(matchIdLength));

                if (!TryReadMapKey(
                        reader, stream, MatchScenePhase.Preparation, out MatchSceneKey preparationMapKey)
                    || !TryReadMapKey(
                        reader, stream, MatchScenePhase.Combat, out MatchSceneKey combatMapKey))
                {
                    return false;
                }

                if (!MatchSceneCatalog.IsValidSelection(preparationMapKey, combatMapKey))
                {
                    return false;
                }

                int count = reader.ReadByte();
                if (!Enum.IsDefined(typeof(OnlineCoreLoopPhase), phase)
                    || remainingMilliseconds < 0
                    || count < 0
                    || count > MaxPlayers)
                {
                    return false;
                }

                var rules = new OnlineCoreLoopRules(
                    totalRounds,
                    preparationSeconds,
                    combatSeconds,
                    summarySeconds);
                var standings = new OnlineCoreLoopStanding[count];
                for (int i = 0; i < count; i++)
                {
                    int playerId = reader.ReadInt32();
                    int kills = reader.ReadUInt16();
                    int assists = reader.ReadUInt16();
                    int survivedRounds = reader.ReadUInt16();
                    int score = reader.ReadUInt16();
                    int rank = reader.ReadByte();
                    int roundNumber = reader.ReadByte();
                    byte roundFlags = reader.ReadByte();
                    int roundKills = reader.ReadUInt16();
                    int roundAssists = reader.ReadUInt16();
                    int resultRound = reader.ReadByte();
                    int resultRank = reader.ReadByte();
                    byte resultFlags = reader.ReadByte();
                    int resultScore = reader.ReadUInt16();
                    int resultKills = reader.ReadUInt16();
                    int resultAssists = reader.ReadUInt16();
                    if (resultRank > MaxPlayers || (resultFlags & ~1) != 0)
                    {
                        return false;
                    }

                    int nicknameLength = reader.ReadByte();
                    if (nicknameLength > MaxNicknameBytes
                        || stream.Position + nicknameLength > stream.Length)
                    {
                        return false;
                    }

                    string nickname = Encoding.UTF8.GetString(reader.ReadBytes(nicknameLength));
                    standings[i] = new OnlineCoreLoopStanding(
                        playerId,
                        nickname,
                        kills,
                        assists,
                        survivedRounds,
                        score,
                        rank,
                        new OnlineCoreLoopRoundFacts(
                            roundNumber,
                            (roundFlags & 1) != 0,
                            (roundFlags & 2) != 0,
                            roundKills,
                            roundAssists),
                        new OnlineCoreLoopRoundResult(
                            resultRound,
                            resultRank,
                            resultScore,
                            resultKills,
                            resultAssists,
                            isSettled: (resultFlags & 1) != 0));
                }

                if (stream.Position != stream.Length)
                {
                    return false;
                }

                snapshot = new OnlineCoreLoopSnapshot(
                    rules,
                    phase,
                    currentRound,
                    remainingMilliseconds / 1000f,
                    standings,
                    matchId,
                    preparationMapKey,
                    combatMapKey,
                    isObservingLastSurvivor,
                    new PreparationRallyStatus(rallyInside, rallyTotal),
                    loadingPercent / 100f,
                    new CombatEntryStatus(entryStage, entryRemainingMilliseconds / 1000f, entryReady, entryTotal));
                return true;
            }
            catch (Exception exception) when (exception is IOException or ArgumentException)
            {
                return false;
            }
        }

        private static MatchSceneKey NormalizeMapKey(MatchSceneKey key, MatchScenePhase phase) =>
            MatchSceneCatalog.TryGetForPhase(key, phase, out _)
                ? key
                : phase == MatchScenePhase.Preparation
                    ? MatchSceneCatalog.DefaultPreparation
                    : MatchSceneCatalog.DefaultCombat;

        private static void WriteMapKey(BinaryWriter writer, MatchSceneKey key, MatchScenePhase phase)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(key.Value ?? string.Empty);
            if (bytes.Length > MaxMapKeyBytes)
            {
                // Catalog 키는 ASCII로 제한되므로 이 경로는 방어 코드다. 유효하지 않은 값은
                // Serialize 호출자가 이미 기본 키로 정규화했다.
                MatchSceneKey fallback = phase == MatchScenePhase.Preparation
                    ? MatchSceneCatalog.DefaultPreparation
                    : MatchSceneCatalog.DefaultCombat;
                bytes = Encoding.UTF8.GetBytes(fallback.Value);
            }

            writer.Write((byte)bytes.Length);
            writer.Write(bytes, 0, bytes.Length);
        }

        private static bool TryReadMapKey(
            BinaryReader reader,
            MemoryStream stream,
            MatchScenePhase phase,
            out MatchSceneKey key)
        {
            key = default;
            int length = reader.ReadByte();
            if (length <= 0 || length > MaxMapKeyBytes || stream.Position + length > stream.Length)
            {
                return false;
            }

            string value = Encoding.UTF8.GetString(reader.ReadBytes(length));
            return MatchSceneKey.TryCreate(value, out key)
                && MatchSceneCatalog.TryGetForPhase(key, phase, out _);
        }
    }
}

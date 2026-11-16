using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BettingRoyale.Core;
using BettingRoyale.Data.Chest;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.LocalPlay;
using BettingRoyale.Data.Scenes;
using BettingRoyale.Gameplay.Combat;
using BettingRoyale.Gameplay.Roulette;
using BettingRoyale.Managers;
using BettingRoyale.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BettingRoyale.Editor
{
    /// <summary>
    /// 04_CombatSandbox를 직접 열어 Default 맵 + 플레이어 + 동적 봇 Dev 흐름으로 실행한다.
    /// </summary>
    [InitializeOnLoad]
    public static class LocalPlayMvpLauncher
    {
        private const string DevSceneDirectory = "Assets/BettingRoyale/Scenes/Dev/";
        private const string PreviousPlayModeStartSceneKey =
            "BettingRoyale.LocalPlayMvpLauncher.PreviousPlayModeStartScene";
        // OnlineCoreLoopWorldPresenter uses the same QA-only identity for its locally assembled target.
        // Keep this Local Play fixture owned by the runtime rig; do not adopt a scene combatant as a dummy.
        private static readonly CombatantId LocalPlayTrainingDummyId = new CombatantId(900054);

        private static readonly Vector3[] BotSpawnOffsets =
        {
            new Vector3(-6f, 0f, 4f),
            new Vector3(-3f, 0f, 6f),
            new Vector3(0f, 0f, 6f),
            new Vector3(3f, 0f, 6f),
            new Vector3(6f, 0f, 4f),
            new Vector3(6f, 0f, -1f),
            new Vector3(-6f, 0f, -1f),
        };

        static LocalPlayMvpLauncher()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("BettingRoyale/Local Play/Run MVP (1PC + Bots)")]
        private static void RunLocalPlayMvp()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[LocalPlayMvpLauncher] Play Mode가 끝난 뒤 다시 실행하세요.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(
                $"{DevSceneDirectory}{SceneName.CombatSandbox}.unity",
                OpenSceneMode.Single);
            SceneAsset previousStartScene = EditorSceneManager.playModeStartScene;
            SessionState.SetString(
                PreviousPlayModeStartSceneKey,
                previousStartScene != null ? AssetDatabase.GetAssetPath(previousStartScene) : string.Empty);
            EditorSceneManager.playModeStartScene = null;
            // CombatRoundSandboxController.Awake가 Direct Play를 만들기 전에 이 요청을 관찰해야 한다.
            LocalPlayDevelopmentLaunch.Request();
            EditorApplication.isPlaying = true;
        }

        [MenuItem("BettingRoyale/Local Play/Run MVP (1PC + Bots)", validate = true)]
        private static bool CanRunLocalPlayMvp() =>
            !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>
        /// MPPM 시나리오 관리자가 Play 단축키를 가로채는 상태에서도 Local Play를 복구 가능하게 끝낸다.
        /// </summary>
        [MenuItem("BettingRoyale/Local Play/Stop MVP")]
        private static void StopLocalPlayMvp()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            LocalPlayDevelopmentLaunch.Clear();
            EditorApplication.isPlaying = false;
        }

        [MenuItem("BettingRoyale/Local Play/Stop MVP", validate = true)]
        private static bool CanStopLocalPlayMvp() =>
            EditorApplication.isPlayingOrWillChangePlaymode;

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && LocalPlayDevelopmentLaunch.IsRequested)
            {
                InstallRuntimeRig();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                LocalPlayDevelopmentLaunch.Clear();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                LocalPlayDevelopmentLaunch.Clear();
                RestorePlayModeStartScene();
            }
        }

        private static async void InstallRuntimeRig()
        {
            LocalCombatInputDriver humanInput = FindHumanInput();
            if (humanInput == null)
            {
                Debug.LogError("[LocalPlayMvpLauncher] PlayerCombatant의 LocalCombatInputDriver를 찾지 못했습니다.");
                EditorApplication.isPlaying = false;
                return;
            }

            Camera worldCamera = FindWorldCamera();
            if (worldCamera == null)
            {
                Debug.LogError("[LocalPlayMvpLauncher] Local Play를 표시할 월드 카메라를 찾지 못했습니다.");
                EditorApplication.isPlaying = false;
                return;
            }

            if (await InstallWithCombatMapAsync(humanInput, worldCamera) == null)
            {
                EditorApplication.isPlaying = false;
                return;
            }

            // Request must remain set through every failure/installation step because Direct Awake observes it.
            // Only a fully configured, active Local runtime consumes it.
            LocalPlayDevelopmentLaunch.Clear();
        }

        internal static async Task<GameObject> InstallWithCombatMapAsync(LocalCombatInputDriver humanInput, Camera camera)
        {
            var provider = new BuiltInMatchSceneProvider();
            MatchSceneKey key = MatchSceneCatalog.DefaultCombat;
            // Do not adopt or later unload a map owned by another running tool.
            if (provider.IsLoaded(key) || provider.IsLoaded(MatchSceneCatalog.DefaultPreparation))
            {
                Debug.LogError("[LocalPlayMvpLauncher] Default 전투 맵이 이미 사용 중입니다.");
                return null;
            }
            try
            {
                MatchSceneLoadResult preparationResult = await provider.LoadAsync(MatchSceneCatalog.DefaultPreparation);
                if (preparationResult.Code != MatchSceneLoadResult.ResultCode.Loaded)
                {
                    Debug.LogError(
                        $"[LocalPlayMvpLauncher] Preparation map load failed: " +
                        $"{preparationResult.Code} {preparationResult.Message}");
                    return null;
                }
                MatchSceneLoadResult result = await provider.LoadAsync(key);
                if (result.Code != MatchSceneLoadResult.ResultCode.Loaded
                    || !Application.isPlaying || humanInput == null || camera == null)
                {
                    if (result.Code != MatchSceneLoadResult.ResultCode.Loaded)
                        Debug.LogError($"[LocalPlayMvpLauncher] Combat map load failed: {result.Code} {result.Message}");
                    if (Application.isPlaying) { await provider.UnloadAsync(key); await provider.UnloadAsync(MatchSceneCatalog.DefaultPreparation); }
                    return null;
                }
                MatchSceneCatalog.TryGet(key, out MatchSceneDescriptor descriptor);
                MatchSceneCatalog.TryGet(MatchSceneCatalog.DefaultPreparation, out MatchSceneDescriptor preparationDescriptor);
                string preparationReason = "not evaluated";
                string combatReason = "not evaluated";
                string boundsReason = "not evaluated";
                if (MatchSceneContentResolver.TryGetMapRoot(preparationDescriptor,
                        out Transform preparation, out preparationReason)
                    && MatchSceneContentResolver.TryGetMapRoot(descriptor,
                        out Transform combat, out combatReason)
                    && MatchSceneContentResolver.TryResolveMovementBounds(
                        combat, out _, out boundsReason)
                    && TryInstallRuntimeForScene(humanInput, camera, out GameObject rig,
                        combat, provider, preparation))
                    return rig;
                Debug.LogError(
                    $"[LocalPlayMvpLauncher] authored map contract failed: " +
                    $"preparation='{preparationReason}', combat='{combatReason}', bounds='{boundsReason}'");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            if (Application.isPlaying) { await provider.UnloadAsync(key); await provider.UnloadAsync(MatchSceneCatalog.DefaultPreparation); }
            return null;
        }

        /// <summary>
        /// Installs from the real 90_CombatLab shape (Main Camera / Combatants / Systems / PlayerCombatant).
        /// Arena and a pre-existing training dummy are intentionally not prerequisites.
        /// </summary>
        internal static bool TryInstallRuntimeForScene(
            LocalCombatInputDriver humanInput,
            Camera worldCamera,
            out GameObject installedRig,
            Transform loadedCombatRoot = null,
            IMatchSceneProvider mapProvider = null, Transform loadedPreparationRoot = null)
        {
            installedRig = null;
            if (humanInput == null)
            {
                Debug.LogError("[LocalPlayMvpLauncher] PlayerCombatant의 LocalCombatInputDriver를 찾지 못했습니다.");
                return false;
            }

            CombatantHealth human = humanInput.GetComponent<CombatantHealth>();
            if (human == null)
            {
                Debug.LogError("[LocalPlayMvpLauncher] PlayerCombatant의 CombatantHealth를 찾지 못했습니다.");
                return false;
            }
            if (worldCamera == null)
            {
                Debug.LogError("[LocalPlayMvpLauncher] Local Play를 표시할 월드 카메라를 찾지 못했습니다.");
                return false;
            }

            DisableSandboxOnlyTools();
            // Legacy Arena is only optional scene cleanup. The current lab instead has Combatants and Systems roots.
            Transform legacyArenaRoot = FindSceneRoot(worldCamera, "Arena");
            var rigObject = new GameObject("[Local Play MVP]");
            // Finish identity/training configuration while inactive so it can never briefly register as a human clone.
            rigObject.SetActive(false);
            CombatantHealth trainingDummy = CreateOwnedTrainingDummy(rigObject.transform);
            var world = new LocalPlayMvpWorld(rigObject.transform, legacyArenaRoot, worldCamera, loadedCombatRoot, mapProvider, loadedPreparationRoot);
            ICombatSideResolver sideResolver = IndividualCombatSideResolver.Instance;
            if (!world.Build(trainingDummy))
            {
                Debug.LogError("[LocalPlayMvpLauncher] 준비/전투 맵 조립에 실패했습니다.");
                DestroyRuntimeRig(rigObject);
                return false;
            }

            if (!LocalPlayProductUiHarness.Install(rigObject, human))
            {
                DestroyRuntimeRig(rigObject);
                return false;
            }
            TrainingDummyDamagePopup damagePopup = rigObject.AddComponent<TrainingDummyDamagePopup>();
            damagePopup.Configure(
                // The rig remains inactive until all components are configured, so CombatantHealth has not
                // necessarily cached its identity yet. Use the explicit fixture identity, never default(0).
                LocalPlayTrainingDummyId,
                trainingDummy.transform,
                worldCamera,
                human.Id,
                sideResolver);
            rigObject.AddComponent<LocalPlayBotRoster>();
            LocalPlaySessionController controller = rigObject.AddComponent<LocalPlaySessionController>();
            Transform[] spawnPoints = CreateSpawnPoints(rigObject.transform, human.transform.position.y);
            if (!controller.Configure(
                    human,
                    human.gameObject,
                    spawnPoints,
                    LocalPlaySettings.Default,
                    world,
                    new LocalPlayCameraTracker(worldCamera, human.transform),
                    sideResolver))
            {
                Debug.LogError("[LocalPlayMvpLauncher] Local Play 세션 구성에 실패했습니다.");
                DestroyRuntimeRig(rigObject);
                return false;
            }

            rigObject.SetActive(true);
            installedRig = rigObject;
            Debug.Log(
                "[LocalPlayMvpLauncher] Local Play MVP 시작 — 로비 → 준비 콜로세움 → 직사각형 전장 → 10초 요약 → 3라운드 뒤 같은 로비");
            return true;
        }

        private static LocalCombatInputDriver FindHumanInput()
        {
            LocalCombatInputDriver fallback = null;
            LocalCombatInputDriver[] inputs = UnityEngine.Object.FindObjectsByType<LocalCombatInputDriver>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < inputs.Length; i++)
            {
                LocalCombatInputDriver candidate = inputs[i];
                if (candidate.gameObject.name == "PlayerCombatant")
                {
                    return candidate;
                }
                if (fallback == null)
                {
                    fallback = candidate;
                }
            }

            return fallback;
        }

        private static Camera FindWorldCamera()
        {
            if (Camera.main != null)
            {
                return Camera.main;
            }

            Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            return cameras.Length > 0 ? cameras[0] : null;
        }

        private static Transform FindSceneRoot(Camera worldCamera, string rootName)
        {
            if (worldCamera == null || string.IsNullOrEmpty(rootName))
            {
                return null;
            }

            GameObject[] roots = worldCamera.gameObject.scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == rootName)
                {
                    return roots[i].transform;
                }
            }

            return null;
        }

        private static void DisableSandboxOnlyTools()
        {
            CombatSandboxDebugHud[] debugHuds = UnityEngine.Object.FindObjectsByType<CombatSandboxDebugHud>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < debugHuds.Length; i++)
            {
                debugHuds[i].enabled = false;
            }

            LocalRoundDirector[] roundDirectors = UnityEngine.Object.FindObjectsByType<LocalRoundDirector>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < roundDirectors.Length; i++)
            {
                roundDirectors[i].enabled = false;
            }

            MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] == null)
                {
                    continue;
                }

                string typeName = behaviours[i].GetType().Name;
                if (typeName == "AutoConnectHarness" || typeName == "NetworkTestPanel")
                {
                    behaviours[i].enabled = false;
                }
            }

        }

        private static CombatantHealth CreateOwnedTrainingDummy(Transform runtimeOwner)
        {
            GameObject dummyObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            dummyObject.name = "LocalPlayTrainingDummy";
            dummyObject.transform.SetParent(runtimeOwner, worldPositionStays: false);

            Combatant identity = dummyObject.AddComponent<Combatant>();
            identity.SetId(LocalPlayTrainingDummyId);
            CombatantHealth health = dummyObject.AddComponent<CombatantHealth>();
            health.SetTrainingDummy(true);

            // This fixture is not cloned from PlayerCombatant, but keep future template changes input-safe.
            LocalCombatInputDriver localInput = dummyObject.GetComponent<LocalCombatInputDriver>();
            if (localInput != null)
            {
                localInput.enabled = false;
            }

            return health;
        }

        private static void DestroyRuntimeRig(GameObject rigObject)
        {
            if (rigObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(rigObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(rigObject);
            }
        }

        private static Transform[] CreateSpawnPoints(Transform parent, float height)
        {
            var result = new Transform[BotSpawnOffsets.Length];
            for (int i = 0; i < BotSpawnOffsets.Length; i++)
            {
                var spawnObject = new GameObject($"LocalBotSpawn_{i + 1}");
                spawnObject.transform.SetParent(parent, worldPositionStays: false);
                Vector3 position = BotSpawnOffsets[i];
                position.y = height;
                spawnObject.transform.position = position;
                result[i] = spawnObject.transform;
            }

            return result;
        }

        private static void RestorePlayModeStartScene()
        {
            string previousPath = SessionState.GetString(PreviousPlayModeStartSceneKey, string.Empty);
            SessionState.EraseString(PreviousPlayModeStartSceneKey);
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previousPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<SceneAsset>(previousPath);
        }
    }

    /// <summary>
    /// 전투 중에는 공용 전투 카메라 정본(<see cref="SharedCombatCamera"/>)의 구도·렌즈를 그대로 소비해 로컬 인간
    /// 유닛을 따라가고, 로비로 돌아갈 때는 생성 시점에 한 번 스냅샷한 원래 구도·배경·렌즈를 그대로 복원한다.
    /// 화면 비율은 실행 화면 값을 그대로 두며 어느 경로에서도 강제하지 않는다.
    /// </summary>
    internal sealed class LocalPlayCameraTracker : ILocalPlayCameraRig
    {
        private readonly Camera worldCamera;
        private readonly Vector3 lobbyPosition;
        private readonly Quaternion lobbyRotation;
        private readonly CameraClearFlags lobbyClearFlags;
        private readonly Color lobbyBackgroundColor;
        private readonly bool lobbyOrthographic;
        private readonly float lobbyOrthographicSize;
        private readonly float lobbyFieldOfView;
        private readonly float lobbyNearClipPlane;
        private readonly float lobbyFarClipPlane;
        private Transform followTarget;
        private CombatantMotor followMotor;

        public LocalPlayCameraTracker(Camera camera, Transform initialTarget)
        {
            worldCamera = camera != null
                ? camera
                : throw new ArgumentNullException(nameof(camera));
            if (initialTarget == null)
            {
                throw new ArgumentNullException(nameof(initialTarget));
            }

            // 로비 복원 계약: 원래 구도·배경·렌즈를 수정 전에 한 번만 붙잡아 둔다.
            lobbyPosition = worldCamera.transform.position;
            lobbyRotation = worldCamera.transform.rotation;
            lobbyClearFlags = worldCamera.clearFlags;
            lobbyBackgroundColor = worldCamera.backgroundColor;
            lobbyOrthographic = worldCamera.orthographic;
            lobbyOrthographicSize = worldCamera.orthographicSize;
            lobbyFieldOfView = worldCamera.fieldOfView;
            lobbyNearClipPlane = worldCamera.nearClipPlane;
            lobbyFarClipPlane = worldCamera.farClipPlane;
        }

        public void Follow(Transform target)
        {
            followTarget = target;
            followMotor = target != null ? target.GetComponent<CombatantMotor>() : null;
            // 공용 기본 렌즈는 추적 시작 시 한 번만 적용한다(매 프레임 시야각 덮어쓰기 금지).
            SharedCombatCamera.ConfigureProjection(worldCamera);
            Tick();
        }

        public void Tick()
        {
            if (worldCamera == null || followTarget == null)
            {
                return;
            }

            SharedCombatCamera.Follow(
                worldCamera,
                ResolveFollowPosition(
                    followTarget.position,
                    followMotor != null ? followMotor.GhostPosition : followTarget.position,
                    followMotor != null && followMotor.IsGhostActive));
        }

        public void RestoreLobbyView()
        {
            followTarget = null;
            followMotor = null;
            if (worldCamera == null)
            {
                return;
            }

            worldCamera.transform.SetPositionAndRotation(lobbyPosition, lobbyRotation);
            worldCamera.clearFlags = lobbyClearFlags;
            worldCamera.backgroundColor = lobbyBackgroundColor;
            worldCamera.orthographic = lobbyOrthographic;
            worldCamera.orthographicSize = lobbyOrthographicSize;
            worldCamera.fieldOfView = lobbyFieldOfView;
            worldCamera.nearClipPlane = lobbyNearClipPlane;
            worldCamera.farClipPlane = lobbyFarClipPlane;
        }

        internal static Vector3 ResolveFollowPosition(Vector3 body, Vector3 ghost, bool active) => active ? ghost : body;
    }

    /// <summary>
    /// Local Play 검증용으로 두 맵을 서로 떨어진 좌표에 조립하고 하나씩만 활성화한다.
    /// 정식 씬/아트가 들어오면 같은 ILocalPlayWorld 경계를 구현하는 씬 로더로 교체한다.
    /// </summary>
    internal sealed class LocalPlayMvpWorld
        : ILocalPlayWorld,
            ILocalPlayCombatPresentation,
            ILocalPlayPracticeTargets,
            ILocalPlayPreparationWorld,
            IDisposable
    {
        internal const float GroundSurfaceY = 0f;
        internal const float SpawnFootY = 0.02f;

        private const int MaximumParticipants = 8;
        private const int ColosseumWallSegmentCount = 32;
        private const float ColosseumRadius = 20f;
        private const float WallHeight = 3f;
        private const int CombatChestsPerRound = 2;
        private const float ChestColliderCenterY = 0.4225f;
        private static readonly Vector3 ChestColliderSize = new Vector3(1.105f, 0.845f, 0.94f);

        private static readonly Vector3 PreparationMapPosition = new Vector3(-60f, 0f, 0f);
        private static readonly Vector3 CombatMapPosition = new Vector3(60f, 0f, 0f);
        private static readonly Vector3[] PreparationSpawnLocalPositions =
        {
            new Vector3(-7.7f, 0f, -12f),
            new Vector3(-5.5f, 0f, -12f),
            new Vector3(-3.3f, 0f, -12f),
            new Vector3(-1.1f, 0f, -12f),
            new Vector3(1.1f, 0f, -12f),
            new Vector3(3.3f, 0f, -12f),
            new Vector3(5.5f, 0f, -12f),
            new Vector3(7.7f, 0f, -12f),
        };
        private static readonly Vector3[] CombatSpawnLocalPositions =
        {
            new Vector3(-19f, 0f, -10f),
            new Vector3(0f, 0f, -11f),
            new Vector3(19f, 0f, -10f),
            new Vector3(21f, 0f, 0f),
            new Vector3(19f, 0f, 10f),
            new Vector3(0f, 0f, 11f),
            new Vector3(-19f, 0f, 10f),
            new Vector3(-21f, 0f, 0f),
        };
        private static readonly Vector3[] CombatChestLocalPositions =
        {
            new Vector3(7f, 0f, 8f),
            new Vector3(-7f, 0f, 8f),
            new Vector3(7f, 0f, -8f),
            new Vector3(-7f, 0f, -8f),
            new Vector3(0f, 0f, 6f),
            new Vector3(0f, 0f, -6f),
            new Vector3(12f, 0f, 0f),
            new Vector3(-12f, 0f, 0f),
        };

        private readonly int[] shuffledCombatSpawnIndices = new int[MaximumParticipants];
        private readonly StringBuilder spawnLogBuilder = new StringBuilder();
        private readonly Transform owner;
        private readonly Transform legacyArenaRoot;
        private readonly Camera worldCamera;
        private readonly List<Transform> chestAnchors = new List<Transform>();
        private readonly List<GameObject> chestVisuals = new List<GameObject>();
        private readonly Dictionary<LocalCombatInputDriver, ICombatInteractionCommandSink> chestBindings =
            new Dictionary<LocalCombatInputDriver, ICombatInteractionCommandSink>();
        private readonly CombatChestAuthorityRuntime chestAuthority = new CombatChestAuthorityRuntime();
        private readonly ProductCombatZoneAuthority localZone = new ProductCombatZoneAuthority(false);
        private readonly List<Transform> visibilityRoots = new List<Transform>();
        private Transform preparationRoot;
        private Transform combatRoot;
        private CombatantHealth trainingDummy;
        private readonly List<CombatantHealth> practiceTargets = new List<CombatantHealth>(1);
        private Transform trainingDummyAnchor;
        private CombatVisibilityPresenter visibility;
        private float chestTickAccumulator;
        private bool hasBuilt;
        private readonly Transform loadedCombatRoot;
        private readonly Transform loadedPreparationRoot;
        private readonly IMatchSceneProvider mapProvider;
        private readonly List<Transform> mapSpawnAnchors = new();
        private bool disposed;

        internal Transform PreparationRoot => preparationRoot;

        /// <summary>
        /// <see cref="ILocalPlayPreparationWorld.PreparationMapRoot"/>. 시설 구역표가 맵 로컬 좌표라
        /// 준비 상점·룰렛의 사거리 판정이 이 루트를 기준으로 계산된다.
        /// </summary>
        public Transform PreparationMapRoot => preparationRoot;

        internal Transform CombatRoot => combatRoot;
        internal int CombatChestCount => chestVisuals.Count;
        internal bool TryGetClaimSnapshot(int chestId, out int sequence, out int claimerId) =>
            chestAuthority.TryGetClaimSnapshot(chestId, out sequence, out claimerId);

        public LocalPlayMvpWorld(Transform owner, Transform legacyArenaRoot, Camera worldCamera,
            Transform loadedCombatRoot = null, IMatchSceneProvider mapProvider = null, Transform loadedPreparationRoot = null)
        {
            this.owner = owner;
            this.legacyArenaRoot = legacyArenaRoot;
            this.worldCamera = worldCamera;
            this.loadedCombatRoot = loadedCombatRoot;
            this.loadedPreparationRoot = loadedPreparationRoot;
            this.mapProvider = mapProvider;
            chestAuthority.Changed += RefreshChestVisuals;
        }

        public void Dispose()
        {
            if (disposed) return;
            localZone.Dispose();
            disposed = true;
            ClearCombatRoundPresentation();
            chestAuthority.Changed -= RefreshChestVisuals;
            if (mapProvider != null && Application.isPlaying) _ = ReleaseMapAsync();
        }

        private async Task ReleaseMapAsync()
        {
            try { await mapProvider.UnloadAsync(MatchSceneCatalog.DefaultCombat); await mapProvider.UnloadAsync(MatchSceneCatalog.DefaultPreparation); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        public bool Build(CombatantHealth dummy)
        {
            if (hasBuilt)
            {
                return true;
            }
            if (dummy == null)
            {
                Debug.LogError("[LocalPlayMvpWorld] 공격 측정용 허수아비가 비었습니다.");
                return false;
            }

            if (loadedPreparationRoot == null || loadedCombatRoot == null)
            {
                Debug.LogError("[LocalPlayMvpWorld] authored Preparation/Combat Map Root가 모두 필요합니다.");
                return false;
            }

            string preparationReason = "not evaluated";
            string combatReason = "not evaluated";
            if (!MatchSceneContentResolver.TryValidateMapRoot(
                    loadedPreparationRoot, out _, out preparationReason)
                || !MatchSceneContentResolver.TryValidateMapRoot(
                    loadedCombatRoot, out _, out combatReason))
            {
                Debug.LogError(
                    $"[LocalPlayMvpWorld] authored Map Root 계약이 유효하지 않습니다: " +
                    $"preparation='{preparationReason}', combat='{combatReason}'");
                return false;
            }

            if (!MatchSceneContentResolver.TryResolveMovementBounds(
                    loadedCombatRoot, out _, out string boundsReason))
            {
                Debug.LogError($"[LocalPlayMvpWorld] authored Combat Map 경계를 읽지 못했습니다: {boundsReason}");
                return false;
            }

            preparationRoot = loadedPreparationRoot;
            combatRoot = loadedCombatRoot;

            // 준비맵의 3D 회전판을 온라인 경로(OnlineCoreLoopWorldPresenter)와 똑같이 돌린다.
            // 붙이지 않으면 맵 바퀴는 멈춰 있고 베팅 창의 2D 바퀴만 혼자 돌아 둘이 다른 칸을 가리킨다
            // (S-UX-YTD44R). 피벗이 없는 맵이면 아무것도 하지 않는다.
            RouletteWheelPresentation.TryBind(preparationRoot);

            trainingDummyAnchor = preparationRoot.Find("Anchors/ActivityAnchors/TrainingDummyAnchor_01");
            if (trainingDummyAnchor == null)
            {
                Debug.LogError("[LocalPlayMvpWorld] 준비 맵의 TrainingDummyAnchor_01이 없습니다.");
                return false;
            }
            preparationRoot.position = PreparationMapPosition;
            combatRoot.position = CombatMapPosition;
            if (!MatchSceneContentResolver.TryCollectAnchors(combatRoot, MatchSceneContentResolver.CombatSpawnPrefix, MaximumParticipants, mapSpawnAnchors)
                || !MatchSceneContentResolver.TryCollectAnchors(combatRoot, MatchSceneContentResolver.ChestAnchorPrefix, MaximumParticipants, chestAnchors))
                return false;

            trainingDummy = dummy;
            // 준비 라운드 연습 표적도 매치 런타임의 공용 피해 해석기를 받는다 (S15P21A606-167).
            practiceTargets.Clear();
            practiceTargets.Add(trainingDummy);
            trainingDummy.gameObject.name = "LocalPlayTrainingDummy";
            trainingDummy.SetTrainingDummy(true);
            trainingDummy.transform.SetParent(loadedPreparationRoot != null ? owner : preparationRoot, worldPositionStays: false);
            TeleportCombatant(
                trainingDummy,
                trainingDummyAnchor.position,
                trainingDummyAnchor.rotation, preserveAnchorHeight: true);

            for (int i = 0; i < shuffledCombatSpawnIndices.Length; i++)
            {
                shuffledCombatSpawnIndices[i] = i;
            }

            hasBuilt = true;
            if (legacyArenaRoot != null)
            {
                legacyArenaRoot.gameObject.SetActive(false);
            }
            SetMapActive(preparation: true, combat: false);
            Debug.Log(
                loadedCombatRoot != null
                    ? "[LocalPlayMvpWorld] 실제 Default 준비/전투 Map 로드 완료"
                    : "[LocalPlayMvpWorld] 계약 테스트용 준비/전투 fixture 조립 완료");
            return true;
        }

        /// <summary>
        /// 준비 공간의 연습 더미. 로스터 참가자가 아니므로 매치 런타임이 이 경계로만 찾아 같은
        /// 피해 해석기를 연결한다 (S15P21A606-167).
        /// </summary>
        public IReadOnlyList<CombatantHealth> PracticeTargets => practiceTargets;

        public bool TryActivateLobby(IReadOnlyList<CombatantHealth> participants)
        {
            return TryActivateColosseum(participants, "대기실");
        }

        public bool TryActivatePreparationMap(IReadOnlyList<CombatantHealth> participants)
        {
            return TryActivateColosseum(participants, "준비 라운드");
        }

        private bool TryActivateColosseum(
            IReadOnlyList<CombatantHealth> participants,
            string phaseLabel)
        {
            if (!ValidateParticipants(participants))
            {
                return false;
            }

            SetMapActive(preparation: true, combat: false);
            trainingDummy.gameObject.SetActive(true);
            trainingDummy.RecoverForNextRound();
            TeleportCombatant(
                trainingDummy,
                trainingDummyAnchor.position,
                trainingDummyAnchor.rotation, preserveAnchorHeight: true);

            for (int i = 0; i < participants.Count; i++)
            {
                if (loadedPreparationRoot != null)
                {
                    if (!MatchSceneContentResolver.TryGetSpawnPose(preparationRoot, MatchScenePhase.Preparation,
                        i, out Pose pose, out string reason)) { Debug.LogError(reason); return false; }
                    TeleportCombatant(participants[i], pose.position, pose.rotation, preserveAnchorHeight: true);
                }
                else TeleportCombatant(participants[i], preparationRoot.TransformPoint(PreparationSpawnLocalPositions[i]), Quaternion.identity);
            }

            RefreshParticipantBounds(participants);
            Physics.SyncTransforms();
            // 초기 맵 로딩 동안 멈춰 둔 사람과 복제 봇은 전원 안전하게 배치된 뒤에만 표시한다.
            for (int i = 0; i < participants.Count; i++) participants[i].gameObject.SetActive(true);
            Debug.Log(
                $"[LocalPlayMvpWorld] 콜로세움 {phaseLabel} 진입 — 하단 집결 인원 {participants.Count}명");
            return true;
        }

        public bool TryActivateCombatMap(IReadOnlyList<CombatantHealth> participants, int round)
        {
            if (!ValidateParticipants(participants))
            {
                return false;
            }

            SetMapActive(preparation: false, combat: true);
            ShuffleCombatSpawnIndices();
            spawnLogBuilder.Clear();
            spawnLogBuilder.Append("[LocalPlayMvpWorld] ").Append(round).Append("라운드 무작위 전투 스폰");
            for (int i = 0; i < participants.Count; i++)
            {
                int spawnIndex = shuffledCombatSpawnIndices[i];
                if (loadedCombatRoot != null)
                {
                    if (!MatchSceneContentResolver.TryGetSpawnPose(combatRoot, MatchScenePhase.Combat,
                        spawnIndex, out Pose pose, out string reason)) { Debug.LogError(reason); return false; }
                    TeleportCombatant(participants[i], pose.position, pose.rotation, preserveAnchorHeight: true);
                }
                else TeleportCombatant(participants[i], combatRoot.TransformPoint(CombatSpawnLocalPositions[spawnIndex]), Quaternion.identity);
                spawnLogBuilder.Append(" · #").Append(i + 1).Append("→").Append(spawnIndex + 1);
            }

            RefreshParticipantBounds(participants);
            Physics.SyncTransforms();
            Debug.Log(spawnLogBuilder.ToString());
            return true;
        }

        public bool TryConfigureCombatPresentation(
            IReadOnlyList<CombatantHealth> participants,
            ICombatSideResolver sideResolver)
        {
            if (!hasBuilt || worldCamera == null || participants == null || participants.Count == 0)
            {
                return false;
            }

            visibilityRoots.Clear();
            for (int i = 0; i < participants.Count; i++)
            {
                CombatantHealth participant = participants[i];
                if (participant == null)
                {
                    return false;
                }

                visibilityRoots.Add(participant.transform);
            }

            Collider[] blockers = CombatWorldComposition.CollectTerrainBlockers(combatRoot);
            return blockers.Length > 0 && CombatWorldComposition.TryConfigureVisibility(
                owner,
                worldCamera,
                participants[0],
                visibilityRoots,
                blockers,
                sideResolver,
                out visibility,
                forceRebind: true,
                chestScope: combatRoot);
        }

        public bool TryBeginCombatRound(IReadOnlyList<CombatantHealth> participants, int round)
        {
            ClearChestFixtures();
            if (!hasBuilt || participants == null || participants.Count == 0
                || chestAnchors.Count != CombatChestLocalPositions.Length
                || !CombatChestRoundSelection.TrySelectTwo(round, chestAnchors.Count, out int first, out int second))
            {
                return false;
            }

            chestVisuals.Add(CreateChestFixture(chestAnchors[first], round, 1));
            chestVisuals.Add(CreateChestFixture(chestAnchors[second], round, 2));
            if (chestVisuals[0] == null || chestVisuals[1] == null)
            {
                ClearChestFixtures();
                return false;
            }

            var interactionBounds = new CombatChestInteractionBounds[CombatChestsPerRound];
            for (int i = 0; i < interactionBounds.Length; i++)
            {
                if (!TryGetChestInteractionBounds(chestVisuals[i], out interactionBounds[i]))
                {
                    ClearChestFixtures();
                    return false;
                }
            }

            // 제품과 같은 개봉 5초 (S-GD-GQNA2B). 이 월드는 1초에 한 틱이다. 기본 ChestConfig로 승격하지 않는다.
            if (!chestAuthority.ConfigureWithInteractionBounds(
                    round,
                    new ChestConfig(
                        openDurationTicks: PlaceholderChestLootTable.OpenSeconds,
                        respawnDelayTicks: CombatChestRespawnPlacement.DelaySeconds, retryDelayTicks: 1),
                    interactionBounds,
                    LocalChestPlayerIdMapper.Instance,
                    NoEconomyChestRewardAdapter.Instance))
            {
                ClearChestFixtures();
                return false;
            }

            if (!MatchSceneContentResolver.TryResolveMovementBounds(combatRoot, out Rect bounds, out _))
                return false;
            var ids = new HashSet<CombatantId>();
            foreach (CombatantHealth participant in participants) if (participant != null) ids.Add(participant.Id);
            if (!localZone.TryBeginRound(ids.Contains, new CombatZoneBounds(
                    bounds.center.x, bounds.center.y, bounds.width, bounds.height))) return false;
            BindChestInteractions(participants);
            chestAuthority.RespawnPlacement = id => CombatChestRespawnPlacement.TryMove(id, chestVisuals, localZone.ActiveZone);
            RefreshChestVisuals();
            return chestVisuals.Count == CombatChestsPerRound;
        }

        public void TickCombatRound(float deltaSeconds)
        {
            localZone.AdvanceAuthority(deltaSeconds);
            if (chestAuthority.Count != CombatChestsPerRound || deltaSeconds <= 0f
                || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
            {
                return;
            }

            chestTickAccumulator += deltaSeconds;
            foreach (GameObject chest in chestVisuals)
                if (chest != null) CombatChestPresentation.Ensure(chest).Advance(deltaSeconds);
            while (chestTickAccumulator >= 1f)
            {
                chestTickAccumulator -= 1f;
                chestAuthority.Tick();
                RefreshChestVisuals();
            }
        }

        public void CancelChestInteraction(CombatantId combatantId, ChestCancelReason reason)
        {
            chestAuthority.CancelFor(combatantId, reason);
        }

        public void ClearCombatRoundPresentation()
        {
            localZone.StopRound();
            ClearChestFixtures();
            if (visibility != null)
            {
                visibility.Unbind();
            }
        }

        private GameObject CreateChestFixture(Transform anchor, int round, int ordinal)
        {
            if (anchor == null)
            {
                return null;
            }

            var chest = new GameObject($"LocalChest_R{round:00}_{ordinal:00}");
            chest.transform.SetParent(combatRoot, worldPositionStays: false);
            chest.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            chest.transform.localScale = Vector3.one;

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            visual.transform.SetParent(chest.transform, worldPositionStays: false);
            visual.transform.localPosition = Vector3.up * .3f;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = new Vector3(ChestColliderSize.x, .6f, ChestColliderSize.z);
            Collider visualCollider = visual.GetComponent<Collider>();
            if (visualCollider != null)
            {
                visualCollider.enabled = false;
            }

            Renderer renderer = visual.GetComponent<Renderer>();
            if (renderer != null && Application.isPlaying)
            {
                renderer.material.color = new Color(0.42f, 0.22f, 0.08f);
            }

            CombatChestTraversalBlocker.CreateDedicated(
                chest.transform,
                "TraversalBlocker",
                new Vector3(0f, ChestColliderCenterY, 0f),
                ChestColliderSize);
            var lid = new GameObject("ChestLid");
            lid.transform.SetParent(chest.transform, false);
            lid.transform.localPosition = new Vector3(0, .6f, -.47f);
            GameObject lidMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lidMesh.name = "LidVisual";
            lidMesh.transform.SetParent(lid.transform, false);
            lidMesh.transform.localPosition = new Vector3(0, .1225f, .47f);
            lidMesh.transform.localScale = new Vector3(ChestColliderSize.x, .245f, ChestColliderSize.z);
            lidMesh.GetComponent<Collider>().enabled = false;
            lidMesh.GetComponent<Renderer>().sharedMaterial = renderer.sharedMaterial;
            CombatChestPresentation.Ensure(chest);
            return chest;
        }

        private static bool TryGetChestInteractionBounds(
            GameObject chest,
            out CombatChestInteractionBounds interactionBounds)
        {
            interactionBounds = default;
            if (chest == null)
            {
                return false;
            }

            CombatChestTraversalBlocker blocker = chest.GetComponentInChildren<CombatChestTraversalBlocker>(true);
            return blocker != null && blocker.TryGetInteractionBounds(out interactionBounds);
        }

        private void BindChestInteractions(IReadOnlyList<CombatantHealth> participants)
        {
            ClearChestBindings();
            for (int i = 0; i < participants.Count; i++)
            {
                CombatantHealth participant = participants[i];
                if (participant == null
                    || !participant.TryGetComponent(out LocalCombatInputDriver input))
                {
                    continue;
                }

                ICombatInteractionCommandSink sink = chestAuthority.CreateInteractionSink(
                    participant.Id,
                    participant.transform);
                chestBindings.Add(input, sink);
                input.SetInteractionCommandSink(sink);
            }
        }

        private void ClearChestBindings()
        {
            foreach (KeyValuePair<LocalCombatInputDriver, ICombatInteractionCommandSink> binding in chestBindings)
            {
                if (binding.Key != null)
                {
                    binding.Key.ClearInteractionCommandSink(binding.Value);
                }
            }

            chestBindings.Clear();
        }

        private void ClearChestFixtures()
        {
            ClearChestBindings();
            chestTickAccumulator = 0f;
            chestAuthority.Clear();
            for (int i = 0; i < chestVisuals.Count; i++)
            {
                GameObject chest = chestVisuals[i];
                if (chest == null)
                {
                    continue;
                }

                chest.SetActive(false);
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(chest);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(chest);
                }
            }

            chestVisuals.Clear();
        }

        private void RefreshChestVisuals()
        {
            for (int i = 0; i < chestVisuals.Count; i++)
            {
                GameObject chest = chestVisuals[i];
                if (chest != null && chestAuthority.TryGetState(i, out ChestAuthorityState state))
                {
                    CombatChestPresentation.Ensure(chest).Apply(state, 1f, chestAuthority.ResolveOpeningActor(state.OpenerId));
                }
            }
        }

        private sealed class LocalChestPlayerIdMapper : IChestPlayerIdMapper
        {
            public static readonly LocalChestPlayerIdMapper Instance = new LocalChestPlayerIdMapper();

            public bool TryGetPlayerId(CombatantId combatantId, out int playerId)
            {
                playerId = combatantId.Value;
                return playerId >= 0;
            }
        }

        private sealed class NoEconomyChestRewardAdapter : IChestRewardAdapter
        {
            public static readonly NoEconomyChestRewardAdapter Instance = new NoEconomyChestRewardAdapter();
            public void NotifyRewardClaimed(int playerId, int chestId) { }
        }

#if false // Legacy procedural maps: disconnected; scheduled for deletion.
        private void BuildPreparationMap()
        {
            GameObject floor = CreatePrimitive(
                preparationRoot,
                "ColosseumFloor",
                PrimitiveType.Cylinder,
                new Vector3(0f, -0.25f, 0f),
                new Vector3(40f, 0.25f, 40f),
                new Color(0.62f, 0.48f, 0.30f));
            UseVisibleMeshForStaticCollision(floor);

            float segmentLength = 2f * Mathf.PI * ColosseumRadius / ColosseumWallSegmentCount + 0.3f;
            for (int i = 0; i < ColosseumWallSegmentCount; i++)
            {
                float angleDegrees = i * (360f / ColosseumWallSegmentCount);
                float angleRadians = angleDegrees * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Sin(angleRadians), 0f, Mathf.Cos(angleRadians));
                CreatePrimitive(
                    preparationRoot,
                    $"ColosseumWall_{i + 1:00}",
                    PrimitiveType.Cube,
                    direction * ColosseumRadius + Vector3.up * (WallHeight * 0.5f),
                    new Vector3(segmentLength, WallHeight, 0.8f),
                    new Color(0.28f, 0.23f, 0.19f),
                    localRotation: Quaternion.Euler(0f, angleDegrees, 0f));
            }

            CreateCardinalGhostBounds(preparationRoot, 20.5f, 20.5f);

            CreatePrimitive(
                preparationRoot,
                "Roulette_Center",
                PrimitiveType.Cylinder,
                new Vector3(0f, 0.005f, 0f),
                new Vector3(7f, 0.005f, 7f),
                new Color(0.92f, 0.62f, 0.10f),
                hasCollision: false);
            CreateAnvil(preparationRoot);
            CreateShop(preparationRoot);
            CreatePrimitive(
                preparationRoot,
                "GatheringArea_Bottom",
                PrimitiveType.Cylinder,
                new Vector3(0f, 0.005f, -12f),
                new Vector3(18f, 0.005f, 7f),
                new Color(0.16f, 0.55f, 0.28f),
                hasCollision: false);
            CreatePrimitive(
                preparationRoot,
                "TrainingDummyMarker_Top",
                PrimitiveType.Cylinder,
                new Vector3(0f, 0.005f, 12f),
                new Vector3(7f, 0.005f, 7f),
                new Color(0.65f, 0.16f, 0.16f),
                hasCollision: false);
        }

        private void BuildCombatMap()
        {
            CreatePrimitive(
                combatRoot,
                "CombatFloor",
                PrimitiveType.Cube,
                new Vector3(0f, -0.25f, 0f),
                new Vector3(50f, 0.5f, 30f),
                new Color(0.23f, 0.27f, 0.31f));
            CreateBoundary(
                combatRoot,
                "NorthWall",
                new Vector3(0f, WallHeight * 0.5f, 15.5f),
                new Vector3(50f, WallHeight, 1f),
                visible: true);
            CreateBoundary(
                combatRoot,
                "SouthWall",
                new Vector3(0f, WallHeight * 0.5f, -15.5f),
                new Vector3(50f, WallHeight, 1f),
                visible: true);
            CreateBoundary(
                combatRoot,
                "EastWall",
                new Vector3(25.5f, WallHeight * 0.5f, 0f),
                new Vector3(1f, WallHeight, 30f),
                visible: true);
            CreateBoundary(
                combatRoot,
                "WestWall",
                new Vector3(-25.5f, WallHeight * 0.5f, 0f),
                new Vector3(1f, WallHeight, 30f),
                visible: true);
            BuildCombatChestAnchors();
        }

#endif

        private void BuildCombatChestAnchors()
        {
            var anchorRootObject = new GameObject("Anchors");
            anchorRootObject.transform.SetParent(combatRoot, worldPositionStays: false);
            for (int i = 0; i < CombatChestLocalPositions.Length; i++)
            {
                var anchorObject = new GameObject($"ChestAnchor_{i + 1:00}");
                anchorObject.transform.SetParent(anchorRootObject.transform, worldPositionStays: false);
                anchorObject.transform.localPosition = CombatChestLocalPositions[i];
                chestAnchors.Add(anchorObject.transform);
            }
        }

        private Transform CreateRoot(string objectName, Vector3 horizontalPosition)
        {
            var rootObject = new GameObject(objectName);
            rootObject.transform.SetParent(owner, worldPositionStays: false);
            rootObject.transform.position = new Vector3(
                horizontalPosition.x,
                GroundSurfaceY,
                horizontalPosition.z);
            return rootObject.transform;
        }

        private static void CreateAnvil(Transform parent)
        {
            CreatePrimitive(
                parent,
                "Anvil_Left_Base",
                PrimitiveType.Cube,
                new Vector3(-11f, 0.6f, 0f),
                new Vector3(2.5f, 1.2f, 2f),
                new Color(0.18f, 0.20f, 0.23f));
            CreatePrimitive(
                parent,
                "Anvil_Left_Top",
                PrimitiveType.Cube,
                new Vector3(-11f, 1.45f, 0f),
                new Vector3(4f, 0.5f, 1.5f),
                new Color(0.10f, 0.12f, 0.15f));
        }

        private static void CreateShop(Transform parent)
        {
            CreatePrimitive(
                parent,
                "Shop_Right_Counter",
                PrimitiveType.Cube,
                new Vector3(11f, 1f, 0f),
                new Vector3(5f, 2f, 3f),
                new Color(0.12f, 0.38f, 0.67f));
            CreatePrimitive(
                parent,
                "Shop_Right_Roof",
                PrimitiveType.Cube,
                new Vector3(11f, 2.4f, 0f),
                new Vector3(6f, 0.4f, 4f),
                new Color(0.70f, 0.20f, 0.18f));
        }

        private static void CreateCardinalGhostBounds(Transform parent, float halfWidth, float halfDepth)
        {
            CreateBoundary(
                parent,
                "NorthWall",
                new Vector3(0f, WallHeight * 0.5f, halfDepth),
                new Vector3(halfWidth * 2f, WallHeight, 0.25f),
                visible: false);
            CreateBoundary(
                parent,
                "SouthWall",
                new Vector3(0f, WallHeight * 0.5f, -halfDepth),
                new Vector3(halfWidth * 2f, WallHeight, 0.25f),
                visible: false);
            CreateBoundary(
                parent,
                "EastWall",
                new Vector3(halfWidth, WallHeight * 0.5f, 0f),
                new Vector3(0.25f, WallHeight, halfDepth * 2f),
                visible: false);
            CreateBoundary(
                parent,
                "WestWall",
                new Vector3(-halfWidth, WallHeight * 0.5f, 0f),
                new Vector3(0.25f, WallHeight, halfDepth * 2f),
                visible: false);
        }

        private static void CreateBoundary(
            Transform parent,
            string objectName,
            Vector3 localPosition,
            Vector3 localScale,
            bool visible)
        {
            GameObject boundary = CreatePrimitive(
                parent,
                objectName,
                PrimitiveType.Cube,
                localPosition,
                localScale,
                new Color(0.12f, 0.14f, 0.17f));
            if (!visible)
            {
                Renderer boundaryRenderer = boundary.GetComponent<Renderer>();
                if (boundaryRenderer != null)
                {
                    boundaryRenderer.enabled = false;
                }
            }
        }

        private static GameObject CreatePrimitive(
            Transform parent,
            string objectName,
            PrimitiveType primitiveType,
            Vector3 localPosition,
            Vector3 localScale,
            Color color,
            bool hasCollision = true,
            Quaternion? localRotation = null)
        {
            GameObject instance = GameObject.CreatePrimitive(primitiveType);
            instance.name = objectName;
            instance.transform.SetParent(parent, worldPositionStays: false);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation ?? Quaternion.identity;
            instance.transform.localScale = localScale;

            Collider primitiveCollider = instance.GetComponent<Collider>();
            if (!hasCollision && primitiveCollider != null)
            {
                primitiveCollider.enabled = false;
            }

            Renderer primitiveRenderer = instance.GetComponent<Renderer>();
            if (primitiveRenderer != null && Application.isPlaying)
            {
                primitiveRenderer.material.color = color;
            }

            return instance;
        }

        private static void UseVisibleMeshForStaticCollision(GameObject primitive)
        {
            Collider generatedCollider = primitive.GetComponent<Collider>();
            if (generatedCollider != null)
            {
                generatedCollider.enabled = false;
            }

            MeshFilter meshFilter = primitive.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogError($"[LocalPlayMvpWorld] {primitive.name}: 정적 충돌에 사용할 메시가 비었습니다.");
                return;
            }

            MeshCollider meshCollider = primitive.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;
        }

        private void SetMapActive(bool preparation, bool combat)
        {
            if (preparationRoot != null)
            {
                preparationRoot.gameObject.SetActive(preparation);
            }
            if (combatRoot != null)
            {
                combatRoot.gameObject.SetActive(combat);
            }
            Transform active = preparation ? preparationRoot : combatRoot;
            if ((preparation && loadedPreparationRoot != null) || (combat && loadedCombatRoot != null))
                SceneManager.SetActiveScene(active.gameObject.scene);
            Physics.SyncTransforms();
        }

        private bool ValidateParticipants(IReadOnlyList<CombatantHealth> participants)
        {
            if (!hasBuilt || participants == null || participants.Count <= 0 || participants.Count > MaximumParticipants)
            {
                Debug.LogError("[LocalPlayMvpWorld] 맵에 배치할 Local Play 참가자 구성이 올바르지 않습니다.");
                return false;
            }

            for (int i = 0; i < participants.Count; i++)
            {
                if (participants[i] == null)
                {
                    Debug.LogError($"[LocalPlayMvpWorld] 참가자 {i + 1} 참조가 비었습니다.");
                    return false;
                }
            }

            return true;
        }

        private void ShuffleCombatSpawnIndices()
        {
            for (int i = 0; i < shuffledCombatSpawnIndices.Length; i++)
            {
                shuffledCombatSpawnIndices[i] = i;
            }
            for (int i = shuffledCombatSpawnIndices.Length - 1; i > 0; i--)
            {
                int swapIndex = UnityEngine.Random.Range(0, i + 1);
                (shuffledCombatSpawnIndices[i], shuffledCombatSpawnIndices[swapIndex]) =
                    (shuffledCombatSpawnIndices[swapIndex], shuffledCombatSpawnIndices[i]);
            }
        }

        private static void TeleportCombatant(
            CombatantHealth combatant,
            Vector3 position,
            Quaternion rotation, bool preserveAnchorHeight = false)
        {
            CharacterController characterController = combatant.GetComponent<CharacterController>();
            if (!preserveAnchorHeight) position.y = ResolveRootYForGroundedVisual(
                characterController,
                combatant.transform,
                SpawnFootY);
            bool wasControllerEnabled = characterController != null && characterController.enabled;
            if (wasControllerEnabled)
            {
                characterController.enabled = false;
            }

            combatant.transform.SetPositionAndRotation(position, rotation);

            if (wasControllerEnabled)
            {
                characterController.enabled = true;
            }
        }

        private static float ResolveRootYForGroundedVisual(
            CharacterController characterController,
            Transform combatantTransform,
            float desiredFootY)
        {
            float rootY = ResolveRootYForFoot(
                characterController,
                combatantTransform,
                desiredFootY);
            if (!TryResolveVisibleFootOffset(combatantTransform, out float visibleFootOffset))
            {
                return rootY;
            }

            return Mathf.Max(rootY, desiredFootY - visibleFootOffset);
        }

        private static float ResolveRootYForFoot(
            CharacterController characterController,
            Transform combatantTransform,
            float desiredFootY)
        {
            if (characterController == null || combatantTransform == null)
            {
                return desiredFootY;
            }

            float verticalScale = Mathf.Abs(combatantTransform.lossyScale.y);
            float controllerBottomOffset =
                (characterController.center.y - characterController.height * 0.5f) * verticalScale;
            return desiredFootY - controllerBottomOffset;
        }

        private static bool TryResolveVisibleFootOffset(
            Transform combatantTransform,
            out float visibleFootOffset)
        {
            visibleFootOffset = 0f;
            if (combatantTransform == null)
            {
                return false;
            }

            SkinnedMeshRenderer[] renderers =
                combatantTransform.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: false);
            bool hasVisibleRenderer = false;
            float visibleFootY = float.PositiveInfinity;
            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Bounds bounds = renderer.bounds;
                if (bounds.size.sqrMagnitude <= Mathf.Epsilon)
                {
                    continue;
                }

                visibleFootY = Mathf.Min(visibleFootY, bounds.min.y);
                hasVisibleRenderer = true;
            }

            if (!hasVisibleRenderer)
            {
                return false;
            }

            visibleFootOffset = visibleFootY - combatantTransform.position.y;
            return true;
        }

        private void RefreshParticipantBounds(IReadOnlyList<CombatantHealth> participants)
        {
            MatchSceneCatalog.TryGet(MatchSceneCatalog.DefaultCombat, out MatchSceneDescriptor descriptor);
            Transform active = preparationRoot.gameObject.activeInHierarchy ? preparationRoot : combatRoot;
            bool useMapBounds = active == preparationRoot ? loadedPreparationRoot != null : loadedCombatRoot != null;
            Rect movementBounds = default;
            bool hasMapBounds = useMapBounds && MatchSceneContentResolver.TryResolveMovementBounds(active, out movementBounds, out _);
            for (int i = 0; i < participants.Count; i++)
            {
                CombatantMotor motor = participants[i].GetComponent<CombatantMotor>();
                if (motor != null)
                {
                    if (hasMapBounds) motor.SetGhostMovementBounds(movementBounds);
                    else motor.ClearGhostMovementBounds();
                }
                if (motor != null && !motor.RefreshArenaBounds())
                {
                    Debug.LogWarning(
                        $"[LocalPlayMvpWorld] {participants[i].name}: 활성 맵의 유령 이동 경계를 찾지 못했습니다.");
                }
            }
        }
    }
}

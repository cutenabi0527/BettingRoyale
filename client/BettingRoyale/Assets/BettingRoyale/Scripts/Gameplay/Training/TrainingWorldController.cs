using System;
using System.Collections.Generic;
using BettingRoyale.Core;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Scenes;
using BettingRoyale.Gameplay.Combat;
using BettingRoyale.Managers;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BettingRoyale.Gameplay.Training
{
    /// <summary>제품 훈련 App Scene의 맵·공용 카메라·조준·봇 내비게이션 조립. Fusion을 참조하지 않는다.</summary>
    public sealed class TrainingWorldController : MonoBehaviour, ITrainingWorldRuntime,
        ICombatSideResolver, IDamageAuthority, LocalCombatInputDriver.IInputFrameConstraint
    {
        private static TrainingWorldController instance;
        private readonly Dictionary<int, CombatantHealth> actors = new();
        private readonly Dictionary<int, LocalCombatBotController> bots = new();
        private readonly List<Transform> actorRoots = new();
        private IMatchSceneProvider loader;
        private TrainingMapDefinition map;
        private TrainingMovingTarget[] targets = Array.Empty<TrainingMovingTarget>();
        private CombatBotWorld botWorld;
        private Camera worldCamera;
        private CombatantHealth localPlayer;
        private bool playerDamage, authority, menuOpen, destroyed;
        public bool IsReady { get; private set; }
        public bool IsActive => menuOpen;
        public CombatMotionInputFrame Constrain(in CombatMotionInputFrame frame) => CombatMotionInputFrame.Neutral;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            instance = null;
            TrainingWorldRuntimeRegistry.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != SceneName.Training || instance != null) return;
            var root = new GameObject("[Training World]");
            SceneManager.MoveGameObjectToScene(root, scene);
            instance = root.AddComponent<TrainingWorldController>();
        }

        private void OnEnable() => EventBus.Subscribe<TrainingMenuVisibilityChanged>(OnMenu);
        private void OnDisable() => EventBus.Unsubscribe<TrainingMenuVisibilityChanged>(OnMenu);

        private async void Start()
        {
            TrainingWorldRuntimeRegistry.Register(this);
            try
            {
                var cameraObject = new GameObject("Training Camera");
                cameraObject.transform.SetParent(transform, false);
                cameraObject.tag = "MainCamera";
                worldCamera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
                SharedCombatCamera.ConfigureProjection(worldCamera);
                // ⚠️ 카메라가 맵보다 먼저 생긴다. 새 Camera 의 기본값은 Skybox 라, 아래 맵 로드를
                //    기다리는 동안 빈 월드 위로 Unity 기본 하늘이 드러난다 (S15P21A606-199).
                //    맵이 붙고 추적이 시작되면 SharedCombatCamera.Follow 가 저절로 되돌린다.
                SharedCombatCamera.SuppressSkyboxUntilWorldReady(worldCamera);
                loader = new BuiltInMatchSceneProvider(TrainingMapSceneCatalog.Maps);
                MatchSceneLoadResult result = await loader.LoadAsync(TrainingMapSceneCatalog.DefaultMapKey);
                if (destroyed)
                {
                    await loader.UnloadAsync(TrainingMapSceneCatalog.DefaultMapKey);
                    return;
                }
                if (!result.Succeeded) throw new InvalidOperationException(result.Message);
                map = FindFirstObjectByType<TrainingMapDefinition>();
                if (map == null) throw new InvalidOperationException("훈련 맵 정의가 없습니다.");
                targets = map.transform.root.GetComponentsInChildren<TrainingMovingTarget>();
                foreach (TrainingMovingTarget target in targets) target.Health.SetDamageAuthority(this);
                botWorld = gameObject.AddComponent<CombatBotWorld>();
                IsReady = true;
                EventBus.Publish(new TrainingEntryStatusChanged(false, "훈련 아카데미 준비 완료"));
            }
            catch (Exception error)
            {
                Debug.LogError("[TrainingWorldController] " + error.Message);
                EventBus.Publish(new TrainingEntryStatusChanged(false, "훈련장 로딩 실패: " + error.Message));
            }
        }

        public bool TryGetSpawn(int npcIndex, int playerSlot, out WorldPosition position)
        {
            position = default;
            if (!IsReady) return false;
            if (npcIndex > 0)
            {
                Transform anchor = npcIndex <= 4 ? map.Find((TrainingZoneId)(npcIndex - 1)).BotSpawn
                    : map.transform.parent.Find("Anchors/DummySpawn_" + (npcIndex - 5));
                if (anchor == null) return false;
                Vector3 point = anchor.position + Vector3.up * (TrainingLocalSession.ActorCapsuleHeight * .5f + .06f);
                position = new WorldPosition(point.x, point.y, point.z);
                return true;
            }
            TrainingMapDefinition.Zone entrance = map.Find(TrainingZoneId.Entrance);
            foreach (Transform entry in entrance.Entries)
            {
                if (entry == null) continue;
                for (int i = 0; i < 25; i++)
                {
                    int slot = (i + Math.Max(0, playerSlot)) % 25;
                    Vector3 foot = entry.position + new Vector3((slot % 5 - 2) * 1.4f, 0, (slot / 5 - 2) * 1.4f);
                    if (!entrance.Contains(foot, .6f) || !TryGround(foot, out Vector3 ground)) continue;
                    Vector3 root = ground + Vector3.up * 1.06f;
                    if (Physics.CheckCapsule(root - Vector3.up * .55f, root + Vector3.up * .55f,
                        .43f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    position = new WorldPosition(root.x, root.y, root.z);
                    return true;
                }
            }
            return false;
        }

        private bool TryGround(Vector3 point, out Vector3 ground)
        {
            float nearest = float.PositiveInfinity;
            ground = default;
            var ray = new Ray(point + Vector3.up * 2, Vector3.down);
            foreach (Collider collider in map.CollisionRoot.GetComponentsInChildren<Collider>())
                if (collider.enabled && !collider.isTrigger && collider.Raycast(ray, out RaycastHit hit, 4)
                    && hit.normal.y >= .75f && hit.distance < nearest)
                { nearest = hit.distance; ground = hit.point; }
            return !float.IsPositiveInfinity(nearest);
        }

        public void BindActor(int actorId, int npcIndex, bool isLocal, bool hasAuthority)
        {
            if (!IsReady) return;
            bool firstRegistration = !actors.TryGetValue(actorId, out CombatantHealth health);
            if (firstRegistration)
            {
                foreach (CombatantHealth candidate in FindObjectsByType<CombatantHealth>(FindObjectsSortMode.None))
                    if (candidate.Id.Value == actorId) { health = candidate; break; }
                if (health == null) return;
                actors.Add(actorId, health);
                actorRoots.Add(health.transform);
                authority |= hasAuthority;
                var motor = health.GetComponent<CombatantMotor>();
                motor.SetGhostMovementBounds(new Rect(-47, -47, 94, 94));
                if (npcIndex >= 5) health.SetTrainingDummy(true);
                else if (npcIndex > 0 && hasAuthority)
                {
                    var bot = health.GetComponent<LocalCombatBotController>();
                    if (bot == null) bot = health.gameObject.AddComponent<LocalCombatBotController>();
                    bot.SetExternalInput(true);
                    bot.SetNavigation(new TrainingBotNavigation(map.Find((TrainingZoneId)(npcIndex - 1)),
                        TrainingLocalSession.ResolveGroundToRootOffset(health.GetComponent<CharacterController>())));
                    bot.SetCombatEnabled(true);
                    bots.Add(actorId, bot);
                }
            }
            if (health == null) return;

            // 등록 시점 뒤에 로컬 권위가 확인될 수 있다. 최초 등록과 로컬 조립은 별도로 수렴시킨다.
            bool localChanged = isLocal && localPlayer != health;
            if (isLocal)
            {
                localPlayer = health;
                if (localChanged) SharedCombatCamera.Follow(worldCamera, health.transform.position);
                var input = health.GetComponent<LocalCombatInputDriver>();
                if (input == null) input = health.gameObject.AddComponent<LocalCombatInputDriver>();
                input.SetAimCamera(worldCamera);
                input.SetInputFrameConstraint(this);
                var guide = health.GetComponent<CombatAimIndicator>();
                if (guide == null) guide = health.gameObject.AddComponent<CombatAimIndicator>();
                guide.SetVisibleToViewer(true);
            }
            else if (firstRegistration)
            {
                var guide = health.GetComponent<CombatAimIndicator>();
                if (guide != null) guide.SetVisibleToViewer(false);
            }
            if (firstRegistration || localChanged) ConfigureBots();
        }

        private void ConfigureBots()
        {
            if (localPlayer == null || botWorld == null) return;
            botWorld.Configure(worldCamera, localPlayer, actorRoots,
                map.CollisionRoot.GetComponentsInChildren<Collider>(), this, null);
            CombatWorldComposition.TryConfigureVisibility(transform, worldCamera, localPlayer, actorRoots,
                map.CollisionRoot.GetComponentsInChildren<Collider>(), this, out _, chestScope: map.transform);
            foreach (LocalCombatBotController bot in bots.Values) bot.ConfigureWorld(botWorld);
        }

        public void RemoveActor(int actorId)
        {
            if (actors.TryGetValue(actorId, out CombatantHealth actor))
            {
                if (actor != null) actorRoots.Remove(actor.transform);
                actors.Remove(actorId);
                bots.Remove(actorId);
                ConfigureBots();
            }
        }

        public CombatMotionInputFrame TickBot(int actorId, float deltaTime, float now)
        {
            if (!bots.TryGetValue(actorId, out LocalCombatBotController bot) || bot == null)
                return CombatMotionInputFrame.Neutral;
            bot.Tick(deltaTime, now);
            return bot.PlannedInput;
        }

        public void TickTargets(float sharedSeconds, bool hasAuthority)
        {
            authority = hasAuthority;
            foreach (TrainingMovingTarget target in targets) if (target != null) target.ApplySharedTime(sharedSeconds);
        }

        public bool TryTravel(int actorId, int zoneId, out WorldPosition position, out string reason)
        {
            position = default;
            reason = "훈련장 플레이어가 준비되지 않았습니다.";
            if (!authority || !actors.TryGetValue(actorId, out CombatantHealth actor) || actor == null) return false;
            TrainingTravelResolved result = new TrainingTravelAuthority(map, actor).TryTravel(actor, (TrainingZoneId)zoneId);
            reason = result.Reason;
            Vector3 point = actor.transform.position;
            position = new WorldPosition(point.x, point.y, point.z);
            return result.Accepted;
        }

        public bool Recover(int actorId)
        {
            if (!authority || !actors.TryGetValue(actorId, out CombatantHealth actor) || actor == null) return false;
            actor.RecoverForNextRound();
            return true;
        }
        public bool IsDowned(int actorId) => actors.TryGetValue(actorId, out CombatantHealth actor) && actor != null && actor.IsDowned;
        public void SetPlayerDamageEnabled(bool enabled) { playerDamage = enabled; }
        public void SetDamageResolver(ICombatDamageResolver resolver)
        {
            foreach (TrainingMovingTarget target in targets) if (target != null) target.Health.SetDamageResolver(resolver);
        }
        public int ResolveSideId(CombatantId id) => id.Value >= 20_000 || id.Value < 10_000 ? 2 : playerDamage ? id.Value : 1;
        public bool CanApplyDamage(CombatantId target, in DamageInfo damageInfo) => authority;
        private void OnMenu(TrainingMenuVisibilityChanged state) { menuOpen = state.IsOpen; }
        private void LateUpdate()
        {
            if (localPlayer != null) SharedCombatCamera.Follow(worldCamera, localPlayer.transform.position);
        }

        private async void OnDestroy()
        {
            destroyed = true;
            bool wasReady = IsReady;
            IsReady = false;
            TrainingWorldRuntimeRegistry.Release(this);
            if (localPlayer != null) localPlayer.GetComponent<LocalCombatInputDriver>()?.ClearInputFrameConstraint(this);
            if (wasReady && loader != null)
            {
                try { await loader.UnloadAsync(TrainingMapSceneCatalog.DefaultMapKey); }
                catch (Exception error) { Debug.LogWarning("[TrainingWorldController] 정리: " + error.Message); }
            }
        }
    }
}

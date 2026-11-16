using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BettingRoyale.Core;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.Scenes;
using BettingRoyale.Data.Training;
using BettingRoyale.Gameplay.Combat;
using BettingRoyale.Gameplay.Equipment;
using BettingRoyale.Managers;
using UnityEngine;

namespace BettingRoyale.Gameplay.Training
{
    /// <summary>네트워크 없이 공용 연습 메뉴·경제·축복·전투를 조립하는 96 개발 씬 어댑터.</summary>
    public sealed class TrainingLocalSession : MonoBehaviour, ICombatSideResolver, LocalCombatInputDriver.IInputFrameConstraint
    {
        [SerializeField] private GameObject characterVisual;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private EquipmentItemCatalogBehaviour equipmentCatalog;
        /// <summary>공용 전투 조립과 같은 캡슐 형상. 루트가 캡슐 중심이고 발은 루트 아래 1m다 (실측 CombatLab).</summary>
        public const float ActorCapsuleHeight=2f;
        public const float ActorCapsuleRadius=.45f;

        /// <summary>빈사 → 재생성까지의 전투 봇 대기 시간(초). 사용자 확정 2026-09-10 (S-GD-XCT7KN).</summary>
        public const float BotRespawnDelaySeconds=3f;

        private TrainingMapDefinition map;
        private CombatantHealth player;
        private TrainingTravelAuthority travel;
        private Transform actorRoot;
        private CombatAimIndicator localAimGuide;
        private EquipmentInventoryBehaviour playerInventory;
        private TrainingLocalPracticeRuntime practice;
        private EquipmentFocus lastEquipmentFocus;
        private readonly List<CombatantHealth> actors=new();
        private readonly Dictionary<CombatantHealth,Vector3> spawnPoints=new();
        /// <summary>봇별 재생성 예정 시각. 세션 종료로 비우며 죽음마다 독립적으로 다시 센다.</summary>
        private readonly Dictionary<CombatantHealth,float> botRespawnDueTime=new();
        private bool isMenuOpen;
        private bool previousRunInBackground;
        /// <summary>Additive 훈련 맵 로딩 경계. 테스트는 주입하고 런타임은 공용 Built-in Provider 를 만든다.</summary>
        private IMatchSceneProvider mapLoader;
        /// <summary>이 세션이 직접 로드한 맵만 정리한다. 미리 열려 있던 맵은 소유하지 않는다.</summary>
        private bool ownsLoadedMap;
        /// <summary>진행 중인 부트스트랩을 무효화하는 세대 번호. 비활성화·파괴가 올린다.</summary>
        private int bootstrapGeneration;
        private bool isBootstrapping;
        private bool hasStarted;
        private bool isQuitting;
        private Task releaseMapTask=Task.CompletedTask;
        private CombatBotWorld botWorld;
        public bool IsActive=>isMenuOpen;
        /// <summary>훈련 맵이 준비돼 실제 세션 조립까지 끝났는지. 진입 회귀 테스트가 관찰한다.</summary>
        public bool IsReady { get; private set; }
        public CombatantHealth Player=>player;
        public CombatMotionInputFrame Constrain(in CombatMotionInputFrame inputFrame)=>CombatMotionInputFrame.Neutral;
        public int ResolveSideId(CombatantId id)=>id.Value==1 ? 1 : 2;
        public void Configure(GameObject visual,Camera camera,EquipmentItemCatalogBehaviour catalog=null)
        { characterVisual=visual; worldCamera=camera; equipmentCatalog=catalog; }
        /// <summary>테스트가 실제 씬 로딩 대신 좁은 로더를 주입하는 지점. 키는 공용 카탈로그 것을 그대로 쓴다.</summary>
        public void ConfigureMapLoader(IMatchSceneProvider loader)=>mapLoader=loader;
        private void OnEnable()
        {
            previousRunInBackground=Application.runInBackground;
            Application.runInBackground=true;
            EventBus.Subscribe<TrainingTravelCommand>(OnTravel);
            EventBus.Subscribe<TrainingMenuVisibilityChanged>(OnMenu);
            EventBus.Subscribe<TrainingActionCommand>(OnPracticeAction);
            // 같은 Play 안에서 껐다 켜는 재진입도 처음 진입과 같은 경로로 다시 조립한다.
            // Start 전에는 아무것도 하지 않아 EditMode 에서 컴포넌트를 붙이는 것만으로 씬을 로드하지 않는다.
            if(hasStarted)Bootstrap();
        }
        private void OnDisable()
        {
            bootstrapGeneration++; // 진행 중인 부트스트랩이 액터를 만들지 못하게 한다.
            IsReady=false;
            MatchHudVisibility.LocalTraining=false;
            Application.runInBackground=previousRunInBackground;
            EventBus.Unsubscribe<TrainingTravelCommand>(OnTravel);
            EventBus.Unsubscribe<TrainingMenuVisibilityChanged>(OnMenu);
            EventBus.Unsubscribe<TrainingActionCommand>(OnPracticeAction);
            practice?.Dispose();
            practice=null;
            EventBus.Publish(new TrainingSessionStateChanged(null));
            if(player!=null)player.GetComponent<LocalCombatInputDriver>()?.ClearInputFrameConstraint(this);
            if(playerInventory!=null)playerInventory.InventoryChanged-=OnPlayerInventoryChanged;
            playerInventory=null;
            ReleaseLocalAimGuide();
            var visibility = GetComponent<CombatVisibilityPresenter>();
            if (visibility != null) visibility.Unbind();
            travel=null;
            if(actorRoot!=null)Destroy(actorRoot.gameObject);
            if(botWorld!=null)Destroy(botWorld);
            actorRoot=null;player=null;botWorld=null;isMenuOpen=false;
            actors.Clear();spawnPoints.Clear();botRespawnDueTime.Clear();
            // 부트스트랩이 진행 중이면 그쪽이 자기 로드 결과를 정리한다(중복 언로드 방지).
            if(!isBootstrapping)ReleaseOwnedMap();
        }
        private void OnApplicationQuit()=>isQuitting=true;

        /// <summary>
        /// 훈련장 전투원의 캡슐을 공용 전투 조립과 같은 형상으로 만든다 — 중심이 루트인 높이 2m 캡슐.
        /// 예전 훈련장은 중심을 <c>Vector3.up</c> 으로 두어 루트가 발밑이었고, 그 결과 권위 공격이
        /// 쓰는 <see cref="AttackContext.Origin"/>(=<c>transform.position</c>)이 캡슐 바닥 평면에 놓여
        /// 기본 화살이 상대 캡슐의 접선을 지나가 통과했다. 공격 규칙·판정 도형은 바꾸지 않고
        /// 조립물 좌표계를 실제 CombatLab(중심 0, 높이 2, 루트 y=1)과 같게 맞춘다.
        /// </summary>
        public static CharacterController ConfigureActorCapsule(GameObject actor)
        {
            var capsule=actor.AddComponent<CharacterController>();
            capsule.height=ActorCapsuleHeight;capsule.radius=ActorCapsuleRadius;capsule.center=Vector3.zero;
            return capsule;
        }

        /// <summary>지면(발) 좌표에서 액터 루트까지의 높이. 캡슐 형상에서 유도하며 상수를 따로 두지 않는다.</summary>
        public static float ResolveGroundToRootOffset(CharacterController capsule)
        {
            if(capsule==null)return 0;
            float scale=Mathf.Abs(capsule.transform.lossyScale.y);
            return (capsule.height*.5f-capsule.center.y)*scale;
        }

        /// <summary>지면 앵커(맵의 스폰·입장 지점)를 액터 루트 좌표로 바꾼다.</summary>
        public static Vector3 ResolveActorRoot(Vector3 groundPosition,CharacterController capsule)
            =>groundPosition+Vector3.up*ResolveGroundToRootOffset(capsule);

        /// <summary>액터 루트 좌표를 지면(발) 좌표로 되돌린다. NavMesh·지형 질의는 이 좌표계를 쓴다.</summary>
        public static Vector3 ResolveGroundPoint(Vector3 rootPosition,CharacterController capsule)
            =>rootPosition-Vector3.up*ResolveGroundToRootOffset(capsule);
        private void Start() { hasStarted=true; Bootstrap(); }

        /// <summary>
        /// 96_TrainingLab 을 단독으로 열고 Play 만 눌러도 훈련장이 서게 한다 (S-QA-ZRH4ZN).
        /// 맵이 이미 열려 있으면 그것을 그대로 쓰고, 없으면 공용 씬 로딩 경계로 현재 기본 훈련 맵을
        /// Additive 로 한 번 로드한 뒤 조립한다. 사용자가 수동으로 맵을 얹는 단계를 요구하지 않는다.
        /// </summary>
        private async void Bootstrap()
        {
            if(isBootstrapping)return;
            isBootstrapping=true;
            int generation=bootstrapGeneration;
            try
            {
                if (Application.isPlaying && !TrainingGameContentLoader.EnsureLoaded())
                { enabled=false; return; }
                // 이전 실행의 언로드가 끝나기 전에 맵을 재사용하면 곧 사라질 맵에 액터를 만든다.
                await releaseMapTask;
                if(generation!=bootstrapGeneration || this==null)return;
                if(await EnsureMapAsync(generation))Begin();
            }
            catch(Exception exception)
            {
                Debug.LogError("[TrainingLocalSession] 훈련장 진입에 실패했습니다: "+exception.Message);
                if(this!=null && generation==bootstrapGeneration)enabled=false;
            }
            finally
            {
                // 로드를 기다리는 동안 꺼졌다면 여기서 우리가 올린 맵을 정리한다.
                if(generation!=bootstrapGeneration)
                {
                    ReleaseOwnedMap();
                    await releaseMapTask;
                }
                isBootstrapping=false;
                // 로딩 중 disable→enable 요청은 버리지 않고, 취소된 실행의 정리가 끝난 뒤 다시 시작한다.
                if(this!=null && hasStarted && isActiveAndEnabled && !isQuitting
                    && generation!=bootstrapGeneration)Bootstrap();
            }
        }

        /// <summary>
        /// 훈련 맵을 확보한다. 이미 유효한 맵이 로드돼 있으면 재로드하지 않고 소유권도 갖지 않는다.
        /// 대기 중 세션이 꺼지거나 파괴되면 조립하지 않고 자기 로드분만 되돌린다.
        /// </summary>
        private async Task<bool> EnsureMapAsync(int generation)
        {
            map=FindFirstObjectByType<TrainingMapDefinition>();
            if(map==null)
            {
                mapLoader??=new BuiltInMatchSceneProvider(TrainingMapSceneCatalog.Maps);
                MatchSceneLoadResult result=await mapLoader.LoadAsync(TrainingMapSceneCatalog.DefaultMapKey);
                if(result.Code==MatchSceneLoadResult.ResultCode.Loaded)ownsLoadedMap=true;
                if(generation!=bootstrapGeneration || this==null)return false;
                if(!result.Succeeded)
                {
                    Debug.LogError("[TrainingLocalSession] 훈련 맵 "+TrainingMapSceneCatalog.DefaultScenePath
                        +" 을(를) 로드하지 못했습니다 ("+result.Code+"): "+result.Message);
                    enabled=false;
                    return false;
                }
                map=FindFirstObjectByType<TrainingMapDefinition>();
            }
            if(map==null || worldCamera==null || characterVisual==null)
            {
                Debug.LogError("[TrainingLocalSession] 훈련 맵 정의·카메라·캐릭터 비주얼이 없어 세션을 시작할 수 없습니다.");
                enabled=false;
                return false;
            }
            return true;
        }

        /// <summary>이 세션이 로드한 맵만 언로드한다. 먼저 열려 있던 맵은 건드리지 않는다.</summary>
        private void ReleaseOwnedMap()
        {
            if(!ownsLoadedMap || mapLoader==null)return;
            ownsLoadedMap=false;
            if(isQuitting)return; // 앱 종료 중에는 씬 정리를 엔진에 맡긴다.
            releaseMapTask=UnloadOwnedMapAsync();
        }

        private async Task UnloadOwnedMapAsync()
        {
            try
            {
                MatchSceneLoadResult result=await mapLoader.UnloadAsync(TrainingMapSceneCatalog.DefaultMapKey);
                if(!result.Succeeded)Debug.LogError("[TrainingLocalSession] 훈련 맵 정리 실패: "+result.Message);
            }
            catch(Exception exception)
            { Debug.LogError("[TrainingLocalSession] 훈련 맵 정리 실패: "+exception.Message); }
        }

        private void Begin()
        {
            actorRoot=new GameObject("Training Actors").transform;actorRoot.SetParent(transform,false);
            player=Spawn("Player",1,map.Find(TrainingZoneId.Entrance).Entries[1].position,false);
            playerInventory=player.GetComponent<EquipmentInventoryBehaviour>();
            playerInventory.InventoryChanged+=OnPlayerInventoryChanged;
            lastEquipmentFocus=playerInventory.SelectedEquipmentFocus;
            EventBus.Publish(new EquipmentInventoryStateChanged(playerInventory.EquipmentSnapshot,true));
            EventBus.Publish(new EquipmentFocusChanged(lastEquipmentFocus,true));
            // 훈련장은 공용 기본 카메라(실제 CombatLab 구도 승격: 오프셋 (0,9,-7), 회전 (45,0,0), FOV 60)를
            // 그대로 소비한다 (S-CLI-6BWF48). 별도 확대 배율을 여기서 적용하지 않는다.
            SharedCombatCamera.ConfigureProjection(worldCamera);
            SharedCombatCamera.Follow(worldCamera,player.transform.position);
            player.gameObject.AddComponent<LocalCombatInputDriver>().SetInputFrameConstraint(this);
            localAimGuide=AttachLocalAimGuide(player);
            player.gameObject.AddComponent<CombatLabMagicBottleBootstrap>().ConfigureIndependentPractice();
            travel=new TrainingTravelAuthority(map,player);
            var bots=new List<(LocalCombatBotController controller,TrainingMapDefinition.Zone zone)>();
            foreach(var zone in map.Zones)
            {
                if(zone.Id==TrainingZoneId.Entrance)continue;
                CombatantHealth bot=Spawn("Bot_"+zone.Id,10+(int)zone.Id,zone.BotSpawn.position,true);
                bots.Add((bot.gameObject.AddComponent<LocalCombatBotController>(),zone));
            }
            Transform anchors=map.transform.parent.Find("Anchors");
            for(int i=0;i<5;i++)
            {
                Transform anchor=anchors.Find("DummySpawn_"+i);
                CombatantHealth dummy=Spawn("Dummy_"+i,30+i,anchor.position,true);
                dummy.SetTrainingDummy(true);
                dummy.GetComponent<CombatantMotor>().enabled=false;
                dummy.GetComponent<WeaponAttackController>().SetRoundExecutionEnabled(false);
            }
            var world=gameObject.AddComponent<CombatBotWorld>();botWorld=world;
            world.Configure(worldCamera,player,new[]{actorRoot},map.CollisionRoot.GetComponentsInChildren<Collider>(),this,null);
            CombatWorldComposition.TryConfigureVisibility(transform, worldCamera, player, new[] {actorRoot},
                map.CollisionRoot.GetComponentsInChildren<Collider>(), this, out _, chestScope: map.transform);
            foreach(var bot in bots)
            {
                bot.controller.ConfigureWorld(world);
                // 봇 컨트롤러는 루트 좌표로 질의하고 NavMesh 는 지면 좌표를 돌려준다.
                // 캡슐에서 유도한 높이 차를 내비게이션에 넘겨 두 좌표계를 한 곳에서 변환한다.
                bot.controller.SetNavigation(new TrainingBotNavigation(bot.zone,
                    ResolveGroundToRootOffset(bot.controller.GetComponent<CharacterController>())));
                bot.controller.SetCombatEnabled(true);
            }
            int movingTargets=RegisterMovingTargets();
            practice=new TrainingLocalPracticeRuntime(player,actors);
            IsReady=true;
            MatchHudVisibility.LocalTraining=true;
            MatchHudVisibility.TrainingPlayerCount=1;
            PublishPracticeState(practice.Snapshot());
            Debug.Log("[TrainingLocalSession] Ready: one local player, four zone bots, five dummies, "+movingTargets+" moving range targets. No online room or account persistence.");
        }

        /// <summary>
        /// 로컬 플레이어에게만 공용 조준 가이드를 붙인다(S-UX-QR2R62 → S-UX-KYJ5SZ). 방향 화살표·검 부채꼴·
        /// 활 조준선과 총구 차단 판정(S-UX-9GWHYJ)은 공용 CombatAimIndicator 구현을 그대로 소비하며
        /// 여기서 도형이나 전투 규칙을 다시 만들지 않는다. 봇·허수아비는 상대 진영이므로 붙이지 않는다.
        /// </summary>
        private CombatAimIndicator AttachLocalAimGuide(CombatantHealth localPlayer)
        {
            if(localPlayer==null)
            { Debug.LogError("[TrainingLocalSession] 로컬 플레이어가 없어 조준 가이드를 붙이지 못했습니다."); return null; }
            // DisallowMultipleComponent 이므로 재진입 시 중복 생성 대신 기존 것을 재사용한다.
            var indicator=localPlayer.GetComponent<CombatAimIndicator>();
            if(indicator==null)indicator=localPlayer.gameObject.AddComponent<CombatAimIndicator>();
            indicator.enabled=true;
            indicator.SetVisibleToViewer(true);
            return indicator;
        }

        /// <summary>세션이 붙인 조준 가이드만 정리한다. 프리뷰를 다시 열 때 표시선이 남지 않게 한다.</summary>
        private void ReleaseLocalAimGuide()
        {
            if(localAimGuide!=null)
            {
                localAimGuide.SetVisibleToViewer(false);
                if(Application.isPlaying)Destroy(localAimGuide);
                else DestroyImmediate(localAimGuide);
            }
            localAimGuide=null;
        }

        /// <summary>
        /// 맵이 이미 들고 있는 좌우 이동 표적(S-GD-VWW8YK)을 확인만 한다. 표적은 자기 계층에서
        /// 스스로 움직이고 기존 피해 경계를 그대로 쓰므로 세션이 새로 조립할 것이 없다.
        /// 표적이 없는 맵(map01)에서는 0을 돌려주고 아무것도 바꾸지 않는다.
        /// </summary>
        private int RegisterMovingTargets()
        {
            var targets=FindObjectsByType<TrainingMovingTarget>(FindObjectsInactive.Exclude,FindObjectsSortMode.None);
            foreach(var target in targets)
            {
                if(!target.HasHitVolumeFollowingVisual)
                    Debug.LogError("[TrainingLocalSession] "+target.name+": 이동 표적의 명중 콜라이더가 시각물과 함께 움직이지 않습니다. 배치를 확인하세요.");
                foreach(var actor in actors)
                    if(actor!=null && actor.Id==target.TargetId)
                        Debug.LogError("[TrainingLocalSession] "+target.name+": CombatantId "+target.TargetId.Value+" 가 세션 액터와 겹칩니다. 표적 id를 바꾸세요.");
            }
            // ⚠️ 측정 피해 팝업(TrainingDummyDamagePopup)은 UI 레이어 조립 지점이 필요하다.
            // Gameplay 는 UI 를 참조할 수 없어(레이어 규칙) 여기서 붙이지 않는다. 표적은
            // TrainingDummyDamageMeasured 를 기존 허수아비와 동일하게 발행하므로 UI 쪽에서
            // 한 번 Configure 하면 그대로 동작한다.
            return targets.Length;
        }
        /// <param name="groundPosition">맵의 지면 앵커. 루트 좌표는 캡슐 형상에서 유도한다.</param>
        private CombatantHealth Spawn(string name,int id,Vector3 groundPosition,bool opponent)
        {
            var actor=new GameObject(name);actor.SetActive(false);actor.transform.SetParent(actorRoot,false);
            var capsule=ConfigureActorCapsule(actor);
            Vector3 rootPosition=ResolveActorRoot(groundPosition,capsule);
            actor.transform.position=rootPosition;
            actor.AddComponent<Combatant>().SetId(new CombatantId(id));
            var health=actor.AddComponent<CombatantHealth>();
            actor.AddComponent<CombatantMotor>();
            actor.AddComponent<SwordAttackResolver>();actor.AddComponent<BowAttackResolver>();
            actor.AddComponent<WeaponAttackController>();
            var inventory=actor.AddComponent<EquipmentInventoryBehaviour>();
            inventory.ConfigureRuntimeCatalog(equipmentCatalog);
            var visual=Instantiate(characterVisual,actor.transform);visual.name="CharacterVisual";
            if(visual.GetComponent<CharacterEquipmentPresenter>()==null)
                visual.AddComponent<CharacterEquipmentPresenter>();
            // 루트가 캡슐 중심으로 올라갔으므로 시각물은 같은 높이만큼 내려 발이 지면에 닿게 둔다
            // (CombatAnimationTestSceneBuilder 의 제품 조립과 같은 -1m 배치). 발 접지 보정
            // (CharacterAnimationGrounding)은 이 기준 위에서 상대 오차만 다듬는다.
            visual.transform.localPosition-=Vector3.up*ResolveGroundToRootOffset(capsule);
            foreach(Collider collider in visual.GetComponentsInChildren<Collider>())collider.enabled=false;
            actor.SetActive(true);
            actor.GetComponent<CombatantMotor>().SetGhostMovementBounds(new Rect(-47,-47,94,94));
            var selector=visual.GetComponent<CharacterVisualSelector>();
            if(selector!=null)selector.ApplyDistinctLocalPlayAppearance(97,opponent ? Mathf.Clamp(id-9,1,7) : 0);
            if(!opponent){playerSelector=selector;RequestSavedAppearance();}
            inventory.TryAuthorityEquipWeapon(WeaponType.Sword);
            actors.Add(health);spawnPoints.Add(health,rootPosition);
            return health;
        }
        // ── 저장 외형 (S-CLI-CQETNV) — 로그인돼 있으면 계정 저장본을, 없으면 seed 외형을 쓴다 ──
        private CharacterVisualSelector playerSelector;
        private readonly Dictionary<string,BettingRoyale.Data.Contracts.CharacterAppearancePreset> savedAppearance=new Dictionary<string,BettingRoyale.Data.Contracts.CharacterAppearancePreset>(System.StringComparer.Ordinal);
        private string appliedAppearanceWeaponKey;
        private bool appearanceSubscribed;

        private void RequestSavedAppearance()
        {
            if(!appearanceSubscribed){EventBus.Subscribe<BettingRoyale.Data.Contracts.CharacterAppearanceSnapshotChanged>(OnSavedAppearanceChanged);EventBus.Subscribe<BettingRoyale.Data.Contracts.CharacterAppearanceSaved>(OnSavedAppearanceSaved);appearanceSubscribed=true;}
            // 응답은 CharacterAppearanceSnapshotChanged 로 온다. 로그인 전이면 Failed 가 오고 seed 외형이 남는다.
            EventBus.Publish(new BettingRoyale.Data.Contracts.CharacterAppearanceRequested());
        }
        private void OnSavedAppearanceChanged(BettingRoyale.Data.Contracts.CharacterAppearanceSnapshotChanged e)
        {
            if(!e.Success)return;
            savedAppearance.Clear();
            for(int i=0;i<e.Presets.Count;i++)savedAppearance[e.Presets[i].WeaponKey]=e.Presets[i];
            appliedAppearanceWeaponKey=null;
        }
        private void OnSavedAppearanceSaved(BettingRoyale.Data.Contracts.CharacterAppearanceSaved e)
        {
            if(!e.Success)return;
            savedAppearance[e.Preset.WeaponKey]=e.Preset;appliedAppearanceWeaponKey=null;
        }
        /// <summary>든 무기가 바뀌거나 저장본이 갱신되면 그 무기의 저장 외형을 입힌다. 저장본이 없는 무기는 seed 외형을 유지한다.</summary>
        private void ApplySavedAppearanceIfChanged()
        {
            if(playerSelector==null||player==null)return;
            var attack=player.GetComponent<WeaponAttackController>();
            string key=BettingRoyale.Data.Profile.CharacterAppearanceCatalog.WeaponKey(attack!=null?attack.EquippedWeapon:WeaponType.None);
            if(string.IsNullOrEmpty(key)||key==appliedAppearanceWeaponKey)return;
            if(savedAppearance.TryGetValue(key,out var preset)){playerSelector.ApplyAppearancePreset(preset);appliedAppearanceWeaponKey=key;}
            else if(appliedAppearanceWeaponKey!=null){playerSelector.ApplyDistinctLocalPlayAppearance(97,0);appliedAppearanceWeaponKey=key;}
        }
        private void OnDestroy()
        {
            if(appearanceSubscribed){EventBus.Unsubscribe<BettingRoyale.Data.Contracts.CharacterAppearanceSnapshotChanged>(OnSavedAppearanceChanged);EventBus.Unsubscribe<BettingRoyale.Data.Contracts.CharacterAppearanceSaved>(OnSavedAppearanceSaved);appearanceSubscribed=false;}
        }

        private void LateUpdate()
        {
            if(player==null)return;
            SharedCombatCamera.Follow(worldCamera,player.transform.position);
            ApplySavedAppearanceIfChanged();
            if(playerInventory!=null && lastEquipmentFocus!=playerInventory.SelectedEquipmentFocus)
            {
                lastEquipmentFocus=playerInventory.SelectedEquipmentFocus;
                EventBus.Publish(new EquipmentFocusChanged(lastEquipmentFocus,true));
            }
            TickRespawns(Time.time);

            // 96 도 제품과 같은 바퀴를 돌린다 (S-QA-HQABXR). 돌리지 않으면 베팅은 받아 놓고
            // 결과가 영원히 나오지 않아 골드가 묶인다.
            practice?.TickRoulette(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// 자동 재생성(S-GD-YQ2RER 5)을 시간 기준으로 처리한다. 전투 봇은 빈사가 된 프레임에
        /// 되살리지 않고 <see cref="BotRespawnDelaySeconds"/> 초 동안 쓰러진 상태를 그대로 보여준 뒤
        /// 원래 구역의 스폰 지점에서 완전 회복한다 (S-GD-XCT7KN). 죽음마다 타이머는 독립이며,
        /// 허수아비·이동 표적은 애초에 빈사가 되지 않으므로(측정 전용) 이 경로에 들어오지 않는다.
        /// 플레이어의 기존 즉시 회복 규칙은 이 변경으로 재정의하지 않는다.
        /// 시간은 호출자가 주입해 3초 경계를 실제로 검사할 수 있게 한다.
        /// </summary>
        public void TickRespawns(float now)
        {
            foreach(var actor in actors)
            {
                if(actor==null)continue;
                var bot=actor.GetComponent<LocalCombatBotController>();
                if(!actor.IsDowned)
                {
                    if(bot!=null)botRespawnDueTime.Remove(actor);
                    continue;
                }
                if(bot==null) { Respawn(actor);continue; }
                if(!botRespawnDueTime.TryGetValue(actor,out float due))
                {
                    botRespawnDueTime[actor]=now+BotRespawnDelaySeconds;
                    continue; // 같은 프레임에 되살리지 않는다 — 쓰러진 표시가 보여야 한다.
                }
                if(now<due)continue;
                botRespawnDueTime.Remove(actor);
                Respawn(actor);
            }
        }

        /// <summary>완전 회복 후 원래 구역의 스폰 루트 좌표로 되돌린다. AI·월드·내비게이션 구성은 유지한다.</summary>
        private void Respawn(CombatantHealth actor)
        {
            actor.RecoverForNextRound();
            var capsule=actor.GetComponent<CharacterController>();
            bool wasEnabled=capsule!=null && capsule.enabled;
            if(capsule!=null)capsule.enabled=false;
            actor.transform.position=spawnPoints[actor];
            if(capsule!=null)capsule.enabled=wasEnabled;
            actor.GetComponent<LocalCombatBotController>()?.Configure(null);
        }
        private void OnTravel(TrainingTravelCommand command)
        {
            var result=travel!=null ? travel.TryTravel(player,command.ZoneId)
                : new TrainingTravelResolved(false,"Training session is unavailable.");
            if(result.Accepted)EventBus.Publish(result);
            else EventBus.Publish(new TrainingTravelRejected(command.ZoneId,result.Reason));
        }
        private void OnMenu(TrainingMenuVisibilityChanged message)=>isMenuOpen=message.IsOpen;

        private void OnPlayerInventoryChanged(EquipmentInventoryOperationResult result)
            =>EventBus.Publish(new EquipmentInventoryStateChanged(result.Snapshot,true));

        private void OnPracticeAction(TrainingActionCommand command)
        {
            if(practice==null || !IsReady)return;
            TrainingActionResult result=practice.Handle(command);
            TrainingBlessingDiagnostics.Record("COMMAND", command.Kind + " id=" + command.Selection
                + " accepted=" + result.Accepted + " message=" + result.Message);
            if(result.Accepted && command.Kind==TrainingActionKind.Travel)
            {
                TrainingTravelResolved travelled=travel.TryTravel(player,(TrainingZoneId)command.Value);
                result=new TrainingActionResult(travelled.Accepted,travelled.Reason);
                if(travelled.Accepted)EventBus.Publish(travelled);
                else EventBus.Publish(new TrainingTravelRejected((TrainingZoneId)command.Value,travelled.Reason));
            }
            PublishPracticeState(practice.Snapshot(result.Message,!result.Accepted));
        }

        private void PublishPracticeState(TrainingSessionView snapshot)
        {
            EventBus.Publish(new TrainingSessionStateChanged(snapshot));
            // UI가 세션보다 늦게 생겨도 같은 Sync 요청으로 첫 인벤토리·포커스를 받는다.
            EventBus.Publish(new EquipmentInventoryStateChanged(playerInventory.EquipmentSnapshot,true));
            EventBus.Publish(new EquipmentFocusChanged(playerInventory.SelectedEquipmentFocus,true));
        }
    }
}

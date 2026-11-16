using System;
using System.Collections;
using BettingRoyale.Data.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;
using BettingRoyale.Core;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Online;

namespace BettingRoyale.Managers
{
    /// <summary>
    /// 씬 로딩 단일 창구. 씬 전환이 필요하면 SceneManager 를 직접 부르지 않고 이 매니저를 쓴다.
    /// 진행률은 EventBus(SceneLoadProgressed)로 흘려보내고 로딩 UI 가 구독한다.
    /// 주의: 방 안에서 App Scene을 바꾸는 동기화는 Fusion의 NetworkSceneManager가 담당하므로
    /// Network 레이어(NetworkManager.LoadLevelForRoom)에서 처리한다. 반면 Host가 확정한 키에 따라
    /// 각 피어가 붙이는 Additive Map Scene은 <see cref="MatchScenes"/> 경계로 로드한다.
    /// </summary>
    public class SceneLoader : MonoSingleton<SceneLoader>
    {
        private const float FakeMinimumDuration = 0.3f; // 로딩 화면이 깜빡이는 것 방지

        /// <summary>
        /// 새 씬이 활성화된 뒤 커튼을 걷기까지 양보하는 프레임 수.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>한 프레임으로는 부족했다.</b> <c>01_Title</c>·<c>02_Lobby</c> 에는 씬에 저장된 Canvas 가
        /// 하나도 없고 화면을 전부 런타임에 만든다. 활성화 다음 프레임에 걷으면 아직 아무것도 그려지지 않은
        /// 씬이 드러난다 — 그 화면이 바로 기본 스카이박스다 (S15P21A606-199).
        /// <para>값은 에이전트가 정했다. 늘리면 전환이 그만큼 길어지고, 줄이면 다시 깜빡인다.</para>
        /// </remarks>
        private const int RevealDelayFrames = 2;

        /// <summary>
        /// 방 안 전환에서 완료 신호를 기다리는 한계. 여기까지 오지 않으면 <b>커튼을 강제로 걷는다</b>.
        /// </summary>
        /// <remarks>
        /// 완료 신호는 Fusion 의 <c>OnSceneLoadDone</c> 에서 온다. 그 코루틴이 예외로 끊기면 신호가
        /// 영원히 오지 않는데, 그때 커튼을 붙잡고 있으면 <b>화면이 멈춘 채 남는다</b> — 덮이지 않은 것보다
        /// 나쁘다. 값은 에이전트가 정했다.
        /// </remarks>
        private const float NetworkTransitionRevealTimeoutSeconds = 10f;

        public bool IsLoading { get; private set; }

        public IMatchSceneProvider MatchScenes { get; private set; }

        private SceneTransitionCurtain transitionCurtain;

        /// <summary>
        /// 전환 커튼. 진단·테스트용으로만 읽는다 — 덮고 걷는 시점은 이 클래스가 정한다.
        /// </summary>
        public SceneTransitionCurtain TransitionCurtain => transitionCurtain;

        /// <summary>진행 중인 방 안 전환 커튼 루틴. 겹쳐 오면 앞의 것을 버린다.</summary>
        private Coroutine networkTransitionRoutine;

        /// <summary>방 안 전환의 완료 신호가 도착했는가.</summary>
        private bool networkTransitionCompleted;

        /// <summary>
        /// 전환 세대. 커튼을 걷으려는 루틴이 <b>아직 최신 전환인지</b> 확인하는 데만 쓴다.
        /// </summary>
        /// <remarks>
        /// 커튼을 걷는 것은 활성화보다 한 프레임 늦다. 그 사이에 다음 전환이 시작됐다면 커튼은
        /// 이제 그쪽 것이므로, 지난 전환이 걷어내면 <b>새 전환이 벗겨져</b> 기본 배경이 드러난다
        /// (S15P21A606-145 가 막으려던 바로 그 증상이다).
        /// </remarks>
        private int loadGeneration;
        private bool matchLoading;
        private int pendingPreparationRound;
        private int localSelectionReadyRound;
        private Coroutine preparedMatchRevealRoutine;

        /// <summary>화면 가림막에 마지막으로 반영한 준비 → 전투 전환 단계(S-GD-4JDCGB).</summary>
        private CombatEntryStage appliedCombatEntryStage;

        protected override void OnSingletonAwake()
        {
            MatchScenes = new BuiltInMatchSceneProvider();
            transitionCurtain = CreateTransitionCurtain();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            RebindEvents();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => RebindEvents();

        private void RebindEvents()
        {
            // Bootstrap GameManager가 EventBus.Clear()를 호출해도 영속 매니저의 구독을 복구한다.
            UnbindEvents();
            BindEvents();
        }

        private void BindEvents()
        {
            EventBus.Subscribe<NetworkSceneTransitionStarted>(OnNetworkSceneTransitionStarted);
            EventBus.Subscribe<NetworkSceneTransitionCompleted>(OnNetworkSceneTransitionCompleted);
            EventBus.Subscribe<NetworkSceneTransitionCancelled>(OnNetworkSceneTransitionCancelled);
            EventBus.Subscribe<OnlineCoreLoopChanged>(OnOnlineCoreLoopChanged);
            EventBus.Subscribe<LocalPreparationSelectionReady>(OnLocalPreparationSelectionReady);
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (preparedMatchRevealRoutine != null)
            {
                StopCoroutine(preparedMatchRevealRoutine);
                preparedMatchRevealRoutine = null;
            }

            UnbindEvents();
        }

        private void UnbindEvents()
        {
            EventBus.Unsubscribe<NetworkSceneTransitionStarted>(OnNetworkSceneTransitionStarted);
            EventBus.Unsubscribe<NetworkSceneTransitionCompleted>(OnNetworkSceneTransitionCompleted);
            EventBus.Unsubscribe<NetworkSceneTransitionCancelled>(OnNetworkSceneTransitionCancelled);
            EventBus.Unsubscribe<OnlineCoreLoopChanged>(OnOnlineCoreLoopChanged);
            EventBus.Unsubscribe<LocalPreparationSelectionReady>(OnLocalPreparationSelectionReady);
        }

        private void OnOnlineCoreLoopChanged(OnlineCoreLoopChanged gameEvent)
        {
            ApplyCombatEntryCurtain(gameEvent.Snapshot);
            if (gameEvent.Snapshot.Phase == OnlineCoreLoopPhase.Loading)
            {
                if (preparedMatchRevealRoutine != null)
                {
                    StopCoroutine(preparedMatchRevealRoutine);
                    preparedMatchRevealRoutine = null;
                }

                matchLoading = true;
                pendingPreparationRound = 0;
                localSelectionReadyRound = 0;
                transitionCurtain.ShowMatchLoading(gameEvent.Snapshot.LoadingProgress);
                return;
            }
            if (!matchLoading) return;
            if (gameEvent.Snapshot.Phase == OnlineCoreLoopPhase.Preparation)
            {
                pendingPreparationRound = gameEvent.Snapshot.CurrentRound;
                TryRevealPreparedMatch();
            }
        }

        /// <summary>
        /// 준비 → 전투 전환을 화면 가림막으로 그린다(S-GD-4JDCGB). 1초 페이드 아웃 → 검은 화면(대기 인원) → 1초 페이드 인.
        /// 단계와 인원은 Host 가 보낸 값이고, 화면은 단계가 바뀔 때 연출만 시작한다.
        /// </summary>
        private void ApplyCombatEntryCurtain(in OnlineCoreLoopSnapshot snapshot)
        {
            if (transitionCurtain == null || matchLoading)
            {
                appliedCombatEntryStage = CombatEntryStage.None;
                return;
            }

            CombatEntryStatus entry = snapshot.CombatEntry;
            switch (entry.Stage)
            {
                case CombatEntryStage.FadeOut:
                    if (appliedCombatEntryStage != CombatEntryStage.FadeOut)
                        transitionCurtain.FadeToBlack(CombatEntryGate.FadeOutSeconds);
                    break;
                case CombatEntryStage.Loading:
                    transitionCurtain.ShowCombatEntryWaiting(entry.ReadyCount, entry.TotalCount);
                    break;
                case CombatEntryStage.FadeIn:
                    if (appliedCombatEntryStage != CombatEntryStage.FadeIn)
                        transitionCurtain.FadeFromBlack(CombatEntryGate.FadeInSeconds);
                    break;
                default:
                    // 페이드 인을 건너뛰고 끝났다(늦게 받은 스냅샷·매치 중단). 검은 막을 남겨 두지 않는다.
                    if (appliedCombatEntryStage is CombatEntryStage.FadeOut or CombatEntryStage.Loading)
                        transitionCurtain.FadeFromBlack(0.2f);
                    break;
            }

            appliedCombatEntryStage = entry.Stage;
        }

        private void OnLocalPreparationSelectionReady(LocalPreparationSelectionReady gameEvent)
        {
            localSelectionReadyRound = gameEvent.Round;
            TryRevealPreparedMatch();
        }

        private void TryRevealPreparedMatch()
        {
            if (!matchLoading || pendingPreparationRound <= 0
                || localSelectionReadyRound != pendingPreparationRound
                || preparedMatchRevealRoutine != null) return;

            preparedMatchRevealRoutine = StartCoroutine(RevealPreparedMatchRoutine(pendingPreparationRound));
        }

        private IEnumerator RevealPreparedMatchRoutine(int readyRound)
        {
            // SelectionReady는 UI 오브젝트가 조립됐다는 뜻이지, 화면에 한 번 그려졌다는 뜻은 아니다.
            // 그 자리에서 커튼을 걷으면 준비 맵의 첫 렌더 전에 기본 skybox가 한 프레임 노출된다.
            yield return WaitForNewSceneFirstFrame();

            preparedMatchRevealRoutine = null;
            if (!matchLoading || pendingPreparationRound != readyRound
                || localSelectionReadyRound != readyRound) yield break;

            matchLoading = false;
            pendingPreparationRound = 0;
            localSelectionReadyRound = 0;
            transitionCurtain.Reveal();
        }

        public void Load(string sceneName, Action onCompleted = null)
        {
            if (IsLoading)
            {
                Debug.LogWarning($"[SceneLoader] 이미 로딩 중입니다. 요청 무시: {sceneName}");
                return;
            }

            StartCoroutine(LoadRoutine(sceneName, onCompleted));
        }

        private IEnumerator LoadRoutine(string sceneName, Action onCompleted)
        {
            var generation = ++loadGeneration;
            IsLoading = true;
            EventBus.Publish(new SceneLoadStarted(sceneName));

            // 이번 프레임이 다 그려진 뒤 직전 화면을 붙잡아 덮는다. 이전 씬이 사라지기 전에
            // 덮어야 Unity 기본 배경이 한 프레임도 보이지 않는다. 이 시점에는 아직 아무것도
            // 부수지 않았으므로 화면에는 이전 UI 가 그대로 있다.
            if (transitionCurtain != null)
            {
                yield return transitionCurtain.CoverAfterCapture();
            }

            var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (operation == null)
            {
                Debug.LogError($"[SceneLoader] 씬을 찾을 수 없습니다: {sceneName} (Build Settings 확인)");
                IsLoading = false;
                transitionCurtain?.Reveal();
                yield break;
            }

            operation.allowSceneActivation = false;
            var elapsed = 0f;

            while (operation.progress < 0.9f || elapsed < FakeMinimumDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var progress = Mathf.Clamp01(operation.progress / 0.9f);
                EventBus.Publish(new SceneLoadProgressed(sceneName, progress));
                yield return null;
            }

            EventBus.Publish(new SceneLoadProgressed(sceneName, 1f));
            operation.allowSceneActivation = true;
            yield return operation;

            // ⚠️ 로딩은 새 씬이 활성화된 이 순간 끝났다. 커튼을 걷는 뒤 한 프레임까지
            //    IsLoading 으로 붙잡고 있으면, 그 틈에 도착한 다음 요청이 경고 한 줄만 남기고
            //    통째로 버려진다 — 새 씬이 켜지자마자 다음 화면을 요청하는 쪽(AuthScreenRouter 는
            //    Authenticated 를 받고 '한 번만' Load 한다)이 그대로 멈춘다.
            IsLoading = false;
            EventBus.Publish(new SceneLoadCompleted(sceneName));
            onCompleted?.Invoke();

            // 활성화 직후에는 새 씬 UI의 첫 렌더가 아직 없다. 새 화면이 실제로 한 번 그려질 때까지
            // 양보한 뒤 커튼을 걷는다. 느린 전환에서도 붙잡아 둔 직전 화면만 보이고 skybox/빈 프레임은
            // 노출되지 않는다.
            yield return WaitForNewSceneFirstFrame();

            // 그 사이에 다음 전환이 시작했다면 커튼은 그쪽 것이다. 여기서 걷으면
            // 새 전환이 벗겨진다.
            if (generation == loadGeneration)
            {
                transitionCurtain?.Reveal();
            }
        }

        /// <summary>
        /// 방 안 전환(Fusion)이 시작됐다. 같은 커튼으로 덮는다.
        /// </summary>
        /// <remarks>
        /// <b>왜 여기인가.</b> 커튼은 <see cref="SceneLoader"/> 의 자식이라 <c>DontDestroyOnLoad</c> 이고,
        /// 씬 전환을 살아남는 유일한 자리다. 전환을 일으키는 쪽(Network)은 이벤트만 발행하고
        /// 덮고 걷는 시점은 여기서 정한다 — 레이어 방향(Managers → Network)을 뒤집지 않는다.
        /// </remarks>
        private void OnNetworkSceneTransitionStarted(NetworkSceneTransitionStarted gameEvent)
        {
            if (transitionCurtain == null)
            {
                return;
            }

            // 로비에서 출발해도 훈련장은 준비 라운드·축복 선택을 발행하지 않는다.
            // Host의 명시적 시작 신호 또는 OnlineCoreLoop Loading 스냅샷만 매치 대기를 연다.
            if (gameEvent.IsMatchStart)
            {
                matchLoading = true;
                transitionCurtain.ShowMatchLoading(0f);
            }

            networkTransitionCompleted = false;
            if (networkTransitionRoutine != null)
            {
                StopCoroutine(networkTransitionRoutine);
            }

            networkTransitionRoutine = StartCoroutine(NetworkTransitionRoutine());
        }

        private void OnNetworkSceneTransitionCompleted(NetworkSceneTransitionCompleted _)
        {
            networkTransitionCompleted = true;
        }

        private void OnNetworkSceneTransitionCancelled(NetworkSceneTransitionCancelled _)
        {
            networkTransitionCompleted = true;
            if (networkTransitionRoutine != null)
            {
                StopCoroutine(networkTransitionRoutine);
                networkTransitionRoutine = null;
            }

            if (preparedMatchRevealRoutine != null)
            {
                StopCoroutine(preparedMatchRevealRoutine);
                preparedMatchRevealRoutine = null;
            }

            matchLoading = false;
            pendingPreparationRound = 0;
            localSelectionReadyRound = 0;
            transitionCurtain?.Reveal();
        }

        private IEnumerator NetworkTransitionRoutine()
        {
            var generation = ++loadGeneration;

            // 아직 아무것도 부수지 않은 시점이다. 이전 씬이 그대로 살아 있으므로 직전 화면을 붙잡는다.
            if (matchLoading)
            {
                transitionCurtain.ShowMatchLoading(0f);
            }
            else
            {
                yield return transitionCurtain.CoverAfterCapture();
            }

            var waited = 0f;
            while (!networkTransitionCompleted && waited < NetworkTransitionRevealTimeoutSeconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!networkTransitionCompleted)
            {
                Debug.LogWarning(
                    "[SceneLoader] 방 안 씬 전환 완료 신호가 오지 않아 커튼을 강제로 걷습니다. " +
                    "Fusion 씬 전환이 중간에 끊겼는지 콘솔을 확인하세요.");
            }

            yield return WaitForNewSceneFirstFrame();

            networkTransitionRoutine = null;

            // SceneLoader.Load 가 뒤이어 시작했다면 커튼은 이제 그쪽 것이다.
            if (generation == loadGeneration && !matchLoading)
            {
                transitionCurtain.Reveal();
            }
        }

        /// <summary>
        /// 새 화면이 <b>실제로 한 번 그려질 때까지</b> 기다린다.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>그리지 않는 실행에서는 프레임 끝을 기다리지 않는다.</b> 배치모드·Null 그래픽스에서는
        /// <c>WaitForEndOfFrame</c> 이 영원히 재개되지 않아, 그것을 기다리는 로딩 루틴이 통째로 멈춘다
        /// (<see cref="SceneTransitionCurtain.CanCaptureFrame"/> 의 S15P21A606-141 회귀와 같은 함정이다).
        /// </remarks>
        private static IEnumerator WaitForNewSceneFirstFrame()
        {
            for (var frame = 0; frame < RevealDelayFrames; frame++)
            {
                yield return null;
            }

            if (SceneTransitionCurtain.CanCaptureFrame)
            {
                yield return new WaitForEndOfFrame();
            }
        }

        private SceneTransitionCurtain CreateTransitionCurtain()
        {
            var curtainRoot = new GameObject("[Scene Transition Curtain]", typeof(RectTransform));
            curtainRoot.transform.SetParent(transform, false);
            return curtainRoot.AddComponent<SceneTransitionCurtain>();
        }
    }
}

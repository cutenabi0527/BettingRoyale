using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;
using BettingRoyale.Core;
using BettingRoyale.Data.Online;
using BettingRoyale.Data.Online.Lobby;
using BettingRoyale.Data.Training;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Managers;

namespace BettingRoyale.Network
{
    /// <summary>
    /// 네트워크 <b>명령 창구 + 콜백 번역</b>. UI·Gameplay 는 Fusion 대신 이 클래스의 메서드를 호출하고,
    /// 결과는 <see cref="EventBus"/> 로 받는다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PUN2 시절에는 <c>NetworkManager</c> 와 <c>PhotonEventRelay</c> 두 컴포넌트로 나뉘어 있었다.
    /// C# 단일 상속이라 <c>MonoSingleton&lt;T&gt;</c> 와 <c>MonoBehaviourPunCallbacks</c> 를
    /// 동시에 상속할 수 없었기 때문이다. Fusion 은 콜백을 상속이 아니라
    /// <see cref="INetworkRunnerCallbacks"/> <b>인터페이스</b>로 받으므로 그 제약이 없다 → D-031 에서 하나로 합쳤다.
    /// </para>
    /// <para>
    /// <b>규칙 (D-009):</b> Fusion 타입(<c>PlayerRef</c>, <c>SessionInfo</c>, <c>NetDisconnectReason</c>)을
    /// 이 클래스 밖으로 내보내지 않는다. 경계에서 <see cref="PlayerSlot"/>·<see cref="RoomSlot"/>·string 으로 바꾼다.
    /// 콜백 안에 게임 로직을 넣지 않는다 — 번역만 한다.
    /// </para>
    /// <para>
    /// <b>권위 (D-030):</b> Host Mode 다. 클라이언트는 입력을 보내고 결과를 <c>[Networked]</c> 상태로 받는다.
    /// ⚠️ Host 는 여전히 플레이어 PC 이므로 <b>치트에 열려 있다.</b> 개선된 것은 일관성이지 보안이 아니다.
    /// </para>
    /// </remarks>
    public partial class NetworkManager : MonoSingleton<NetworkManager>, INetworkRunnerCallbacks
    {
        private const string LobbyConnectExceptionMessage = "세션 로비 연결 중 내부 오류가 발생했습니다.";
        private const string SessionStartExceptionMessage = "방 참가 중 내부 오류가 발생했습니다.";

        // 운영 정원은 직렬화하지 않는다. 과거 프리팹의 8명이 코드 정본을 덮지 않아야 한다.
        private const int maxPlayersPerRoom = LobbyRoomRules.MaxCapacity;

        [Header("프리팹")]
        [Tooltip("플레이어당 스폰되는 메타데이터 오브젝트. Net_PlayerInfo 를 연결한다.")]
        [SerializeField] private NetworkObject playerInfoPrefab;

        [Tooltip("플레이어당 스폰되는 이동 오브젝트. NetworkObject/NetworkTransform/NetworkCombatantMotion을 포함한 Net_ 프리팹을 연결한다.")]
        [SerializeField] private NetworkObject combatantPrefab;

        // ─────────────────────────────────────────────────────────────
        // ⚠️ Runner 는 씬·프리팹에 미리 두지 않고 세션마다 새로 만든다.
        //
        // Fusion 문서: "Once a NetworkRunner disconnects, it must be destroyed
        // before creating a new instance for subsequent sessions."
        //
        // 즉 Shutdown 된 Runner 는 재사용할 수 없다. [Managers] 프리팹에 NetworkRunner 를
        // 붙여두면 첫 세션은 되지만 방을 나갔다 다시 들어갈 때 조용히 실패한다.
        // 그래서 [RequireComponent] 를 쓰지 않고, Connect() 에서 만들고 OnShutdown 에서 파괴한다.
        // ─────────────────────────────────────────────────────────────
        private NetworkRunner runner;

        /// <summary>화면 표시용 지연을 몇 초마다 다시 재는가. 표시 갱신 주기와 맞춘다.</summary>
        private const float LatencySampleInterval = .5f;

        /// <summary>다음 지연 측정까지 남은 시간(초).</summary>
        private float latencyCountdown;
        private int trainingWireSequence;
        private const string RoomModeProperty = "mode";

        public bool IsTrainingRoom => runner != null && runner.IsRunning
            && runner.SessionInfo.Properties.TryGetValue(RoomModeProperty, out SessionProperty mode)
            && mode.IsString && (string)mode == "training";

        internal int LocalTrainingPlayerId => runner != null && runner.IsRunning ? runner.LocalPlayer.PlayerId : 0;
        internal float TrainingSimulationTime => runner != null && runner.IsRunning ? (float)runner.SimulationTime : 0f;

        public Task CreateTrainingRoom(string code) =>
            StartSession(GameMode.Host, code, false, "훈련 아카데미", training: true);

        public Task JoinTrainingRoom(string code) => StartSession(GameMode.Client, code);

        internal void SendTrainingCommand(byte[] payload)
        {
            if (!IsTrainingRoom || IsHost || payload == null || payload.Length > TrainingWireProtocol.MaxBytes) return;
            ReliableSendCount++;
            runner.SendReliableDataToServer(ReliableKey.FromInts(TrainingWireProtocol.CommandMagic, ++trainingWireSequence), payload);
        }

        internal void SendTrainingState(int playerId, byte[] payload)
        {
            if (!IsTrainingRoom || !IsHost || payload == null || payload.Length > TrainingWireProtocol.MaxBytes) return;
            foreach (PlayerRef player in runner.ActivePlayers)
            {
                if (player.PlayerId == playerId && player != runner.LocalPlayer)
                {
                    ReliableSendCount++;
                    runner.SendReliableDataToPlayer(player,
                        ReliableKey.FromInts(TrainingWireProtocol.StateMagic, ++trainingWireSequence), payload);
                }
            }
        }

        /// <summary>닉네임 스냅샷의 기본값. 세션이 끝나면 이 값으로 되돌린다 (S15P21A606-96).</summary>
        private const string DefaultNickName = "Player";

        /// <summary>
        /// 지금 진행 중인 셧다운이 <see cref="Disconnect"/>(로그아웃)發인가.
        /// 참이면 <see cref="OnShutdown"/> 이 세션 알림을 내보내지 않는다 — 로그아웃은 세션
        /// 사고가 아니라서, 알리면 <c>SessionReturnRouter</c> 가 로비로 끌고 가 익명 닉네임으로
        /// 자동 재접속하는 역효과가 났다 (2026-09-07 실측, S15P21A606-96). 화면 전환은
        /// 로그아웃 흐름(AuthStateChanged)이 맡는다. 콜백이 소비하고 되돌린다.
        /// </summary>
        private bool suppressShutdownPublish;

        private string nickName = DefaultNickName;
        private bool isConnected;
        private bool isConnecting;
        private bool isStartingSession;
        private int onlineCoreLoopSequence;

        /// <summary>
        /// 방 안 플레이어들의 메타데이터. <see cref="PlayerInfo.Spawned"/> 가 스스로 등록한다.
        /// </summary>
        /// <remarks>
        /// Fusion 에 "이 방의 PlayerInfo 전부" 를 주는 API 가 없어서 우리가 들고 있는다.
        /// <see cref="MonoSingleton{T}"/> 인스턴스에 붙어 있으므로 Play 를 다시 눌러도 남지 않는다 —
        /// <c>static</c> 으로 두면 도메인 리로드를 끈 설정(D-015) 때문에 옛 항목이 살아남는다.
        /// </remarks>
        private readonly List<PlayerInfo> playerInfos = new();

        /// <summary>
        /// <b>Host 만 채우는</b> <c>PlayerRef</c> → <c>accountId</c> 매핑 (S15P21A606-52).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>왜 <c>[Networked]</c> 가 아닌가.</b> 남의 계정 키를 방 전원이 알 이유가 없다.
        /// 서버에 결과를 제출하는 것은 Host 뿐이므로 Host 만 알면 된다
        /// (identity-contract 2-5: 세션 메모리 한정, 영속 저장 금지).
        /// </para>
        /// <para>
        /// ⚠️ <b>세션 한정이다.</b> 방을 나가면 <see cref="DisposeRunner"/> 가 통째로 비운다.
        /// 안 비우면 다음 방에서 같은 <c>PlayerRef</c> 번호를 받은 <b>다른 사람</b>이
        /// 이전 사람의 계정으로 매핑된다.
        /// </para>
        /// <para>
        /// ⚠️ <b>클라이언트가 신고한 값이라 검증되지 않았다.</b> S0 에는 인증이 없어 Host 도 서버도
        /// 위조를 가려낼 수 없다(S15P21A606-52 "알고 감수하는 한계"). 근본 해결은 -34(JWT) + O-016.
        /// </para>
        /// </remarks>
        private readonly Dictionary<PlayerRef, string> accountIdsByPlayer = new();

        /// <summary>
        /// <b>실제 대기실 입장 순서</b>. Host 가 참가 콜백 순간에 1부터 단조 증가로 매긴다
        /// (S-GD-7G8ZTA · S-GD-MQJPFK).
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠️ <b><c>PlayerId</c> 정렬은 입장 순서가 아니다.</b> Fusion 은 빈 슬롯 번호를 재사용하므로
        /// 나갔다 들어온 사람이 작은 번호를 다시 받는다 — 그 순간 늦게 온 사람이 "가장 먼저 들어온
        /// 사람" 이 되어 동점 처리 마지막 단계가 조용히 뒤집힌다. <c>PublishPlayers</c> 가
        /// <c>PlayerId</c> 로 정렬하는 것도 표시 순서일 뿐 입장 순서가 아니다.
        /// </para>
        /// <para>
        /// 세션 한정이라 <see cref="DisposeRunner"/> 에서 비운다. 매치 동안 고정된 값은
        /// <see cref="matchLobbyJoinOrders"/> 스냅샷이 따로 들고 있으므로 이탈해도 사라지지 않는다.
        /// </para>
        /// </remarks>
        private readonly Dictionary<PlayerRef, int> lobbyJoinOrdersByPlayer = new();

        /// <summary>지금까지 발급한 입장 순서의 최댓값. 재사용하지 않는다.</summary>
        private int lastAssignedLobbyJoinOrder;

        /// <summary>매치 시작 시점의 <c>playerId → 입장 순서</c> 스냅샷. 이탈해도 남는다.</summary>
        private readonly Dictionary<int, int> matchLobbyJoinOrders = new();

        /// <summary>
        /// 매치 시작 시점에 얼린 봇 번호. <b>대기방 명단(<see cref="botRoster"/>)과 따로 둔다.</b>
        /// </summary>
        /// <remarks>
        /// 대기방 명단은 방으로 돌아오면 다시 쓰이지만, 진행 중 매치의 참가자는 시작 순간에 확정돼야
        /// 한다 — 사람 쪽 <see cref="matchLobbyJoinOrders"/> 와 같은 이유다.
        /// </remarks>
        private readonly List<int> matchBotNumbers = new();

        /// <summary>
        /// 계정 프로필에서 온 외형 프리셋. PlayerInfo의 Networked 값이 정본이고,
        /// 이 캐시는 전투 오브젝트 스폰 전에 이미 검증된 세션 값을 꺼내기 위한 Host 전용 보조본이다.
        /// </summary>
        private readonly Dictionary<PlayerRef, int> appearancePresetSeedsByPlayer = new();

        /// <summary>플레이어별 저장 외형(무기별 칸 번호). Host 가 세션 동안 기억하고 전투원에 복제한다 (S-CLI-CQETNV).</summary>
        private readonly Dictionary<PlayerRef, NetworkAppearancePresetState> appearancePresetsByPlayer = new();

        private ICombatVisualDriver combatantAppearanceDriver;
        private readonly int[] appearanceEncodeBuffer = new int[NetworkAppearancePresetState.SlotCount];

        /// <summary>
        /// <b>로비 명부</b> — 대기방에서 사람이 고른 <b>원본</b> 무기 선택 (S15P21A606-226).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>왜 필요한가.</b> <c>PlayerInfo</c> 는 매치 씬 전환을 넘지 못하고, Host 가 새로 스폰한
        /// 인스턴스의 <c>WeaponChoice</c> 는 복제 기본값 <b>0(= 검)</b> 이다. 그래서 한 판을 마치고
        /// 대기방으로 돌아오면 활을 골랐던 사람의 화면이 검으로 되돌아갔다.
        /// <see cref="appearancePresetSeedsByPlayer"/>·<c>accountIdsByPlayer</c> 와 <b>같은 처방</b>이다 —
        /// 씬을 넘어 살아야 하는 값은 복제 오브젝트가 아니라 Host 의 기억에 둔다.
        /// </para>
        /// <para>
        /// ⚠️ <b>매치 명부(<see cref="matchWeaponChoices"/>)로 대신할 수 없다.</b> 그쪽은 랜덤이 이미
        /// <b>확정된</b> 값이라, 되살리면 <c>랜덤</c> 을 고른 사람이 영영 특정 무기로 굳는다.
        /// 랜덤은 매치마다 다시 뽑혀야 한다 (S-GD-29RMAQ). 그래서 <b>사람이 고른 원본</b>을 따로 둔다.
        /// </para>
        /// </remarks>
        private readonly Dictionary<PlayerRef, LobbyWeaponChoice> lobbyWeaponChoicesByPlayer = new();

        /// <summary>씬에 살아 있는 전투 오브젝트. 유령이 남았는지 보려면 소유 기준이 아니라 이쪽을 센다.</summary>
        private readonly List<NetworkCombatantMotion> spawnedCombatants = new();

        /// <summary>
        /// 마지막 목록 갱신에서 <b>랜덤 참가 후보로 판정된</b> 방 이름들 (S15P21A606-55 · S-UX-BXXTHX).
        /// </summary>
        /// <remarks>
        /// <see cref="JoinRandomRoom"/> 이 여기서 고른다. Fusion 에 방 선택을 맡기면
        /// 우리 필터를 거치지 않아 죽은 방을 골라 실패하기 때문이다.
        /// <para>
        /// ⚠️ <b>목록에 보이는 방보다 좁다.</b> 참가 가능 조건에 더해 <b>잠기지 않은 방</b>만 담는다 —
        /// 사용자가 고르지 않은 방에 들어가는 경로에는 비밀번호를 물을 자리가 없다
        /// (S-UX-BXXTHX "랜덤 참가의 후보는 … 비밀번호가 없는 방"). 목록 자체는
        /// 잠긴 방·닫힌 방·가득 찬 방을 그대로 보여준다.
        /// </para>
        /// <para>
        /// ⚠️ 갱신 시점의 스냅샷이다. 여기 있다고 지금도 들어갈 수 있다는 보장은 없다.
        /// </para>
        /// </remarks>
        private readonly List<string> randomJoinCandidates = new();

        /// <summary>
        /// 마지막으로 발행한 방 목록·참가자 목록의 사본 (S-UX-BXXTHX).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>왜 들고 있는가.</b> 목록은 <b>갱신될 때만</b> 발행된다. 화면이 그 순간보다 늦게 켜지면
        /// (로비 홈 → 방 찾기처럼 화면을 나눈 뒤에는 늘 그렇다) 다음 갱신까지 빈 목록을 그린다.
        /// <c>CurrentRoomTitle</c> 이 존재하는 이유와 같다 — 늦게 붙은 구독자는 현재 상태를
        /// 물어볼 수 있어야 한다(ui-domain-contract §13-3).
        /// </para>
        /// <para>
        /// ⚠️ <b>밖으로는 사본만 준다.</b> 배열을 그대로 넘기면 화면이 정렬·삭제로 우리 상태를
        /// 바꿀 수 있고, 그 변경은 다음 발행 때까지 드러나지 않는다.
        /// </para>
        /// </remarks>
        private RoomSlot[] roomListSnapshot = Array.Empty<RoomSlot>();
        private PlayerSlot[] playerListSnapshot = Array.Empty<PlayerSlot>();

        /// <summary>
        /// 대기방 봇 명단 (S-ONL-9GXTYQ). <b>정본은 Host 메모리</b>이고 클라이언트의 이것은 복제본이다
        /// (D-ONL-AM6C7M).
        /// </summary>
        /// <remarks>
        /// 양쪽이 같은 타입을 쓰는 이유는 권한·정원 규칙이 갈라지지 않게 하기 위해서다 —
        /// 화면이 "버튼을 보일지" 를 판정하는 코드와 Host 가 "요청을 받을지" 를 판정하는 코드가 같다.
        /// </remarks>
        private readonly LobbyBotRoster botRoster = new LobbyBotRoster();
        private int botWireSequence;

        /// <summary>
        /// Host 가 스냅샷에 실어 보낸 방장 <c>PlayerId</c>. 클라이언트 전용 보조값이다.
        /// </summary>
        /// <remarks>
        /// ⚠️ <see cref="PlayerInfo"/> 는 RPC 로 뒤늦게 도착한다. 그동안 <see cref="HostPlayerId"/> 가
        /// 0 이면 권한 판정이 통째로 거짓이 되어 <b>버튼이 잠깐 사라졌다 나타난다</b> — 스냅샷이 이미
        /// 들고 있는 값을 폴백으로 쓴다.
        /// </remarks>
        private int replicatedBotHostPlayerId;

        /// <summary>
        /// 이 방의 입장 심사 (S-UX-BXXTHX). 규칙과 한계는 <see cref="LobbyRoomAdmission"/> 에 있다.
        /// </summary>
        /// <remarks>
        /// Host 일 때만 채워진다. 잠기지 않은 방에서는 빈 채로 남아 누구나 통과시키므로,
        /// 기존 방·훈련장 경로는 이 필드를 만나도 동작이 달라지지 않는다.
        /// </remarks>
        private readonly LobbyRoomAdmission roomAdmission = new();
        private bool sessionJoined;

        /// <summary>프리팹 미할당 오류를 한 번만 알리기 위한 표시 (S15P21A606-47).</summary>
        // ───────── 진단 카운터 (S15P21A606-275) ─────────

        /// <summary>전투원 프리팹 Instantiate 비용 누계(마이크로초).</summary>
        /// <remarks>
        /// ⚠️ <b>NetworkCombatantMotion.Spawned 를 재는 것으로는 부족하다.</b>
        /// 실측에서 <c>Spawned()</c> 8기 합계가 58ms 인데 그 창의 프레임은 1760ms 였다.
        /// Fusion 은 프리팹을 <c>Instantiate</c> 한 <b>뒤에</b> <c>Spawned()</c> 를 부르므로,
        /// 캐릭터 조립(메시·애니메이터·리깅) 비용은 그 측정 밖에 있었다.
        /// </remarks>
        internal static long CombatantInstantiateMicros;

        /// <summary>이번 창에서 <c>runner.Spawn</c> 을 부른 횟수.</summary>
        internal static int CombatantInstantiateCount;

        private static void AddInstantiateCost(long startTicks)
        {
            CombatantInstantiateCount++;
            CombatantInstantiateMicros +=
                (System.Diagnostics.Stopwatch.GetTimestamp() - startTicks)
                * 1000000L / System.Diagnostics.Stopwatch.Frequency;
        }

        /// <summary>Host 가 마지막으로 내보낸 경제 상태 순번. 진단이 읽는다.</summary>
        /// <remarks>
        /// ⚠️ <b>이 값과 클라의 수신 순번을 맞대보는 것이 유일한 검출 수단이다.</b> Host→Client
        /// 신뢰 채널이 매치 중 한 순간에 멎어도 양쪽 어디에도 오류가 남지 않는다. 송신 순번만
        /// 계속 올라가고 수신이 멈춰 있으면 그 채널이 죽은 것이다.
        /// </remarks>
        internal static int LastBroadcastEconomySequence;

        /// <summary>이동 입력이 <b>실제로 0이 아니었던</b> 틱 수. 진단이 읽고 비운다.</summary>
        /// <remarks>
        /// ⚠️ <b>내용을 세지 않으면 전부 초록인 채로 아무도 안 움직인다.</b>
        /// "입력이 도착했다" 와 "게이트를 통과했다" 는 둘 다 이동 벡터가 0인지는 보지 않는다.
        /// 실제로 모든 지표가 정상인데 플레이가 안 되는 회차가 나왔고, 그때 남은 설명이 이것뿐이다.
        /// </remarks>
        internal static int OnInputMovingCount;

        /// <summary>
        /// Fusion 이 이 클라이언트에게 입력을 요구한 횟수와, 실제로 실은 횟수.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>한 번도 재지 않은 고리다.</b> Host 는 창당 160틱에서 소유자 입력을 못 받는데
        /// Fusion 의 <c>buffer was full</c> 은 17회뿐이다 — 숫자가 안 맞는다. 버려진 것이 아니라
        /// <b>애초에 만들어지지 않았을</b> 가능성이 있다. 클라가 예측 한계에 박혀 틱을 못 돌리면
        /// Fusion 은 <c>OnInput</c> 을 부르지 않고, 그러면 보낼 입력 자체가 없다 —
        /// 그 경우 "입력이 유실된다" 가 아니라 "입력이 생기지 않는다" 가 맞는 진단이다.
        /// </remarks>
        internal static int OnInputCallCount;

        /// <inheritdoc cref="OnInputCallCount"/>
        internal static int OnInputFilledCount;

        // ─────────────────────────────────────────────────────────────
        // 신뢰 데이터 건수 (S15P21A606-275, 5차)
        //
        // ⚠️ **들어오는 패킷의 85%가 상태가 아니다.** 2026-09-22 실측: 클라가 프레임당 2~3패킷
        //    (초당 120~180)을 받는데 호스트의 상태 전송은 초당 32 뿐이다. 나머지가 무엇인지
        //    세지 않으면 "상태가 밀린다" 의 원인을 신뢰 데이터에 물을 수도, 풀어줄 수도 없다.
        //
        //    `ReliableTransfersCompleted` 는 `OnReliableDataProgress` 가 세는데 그 콜백은 실측에서
        //    **한 번도 불리지 않았다**(전송이 작아 청크 진행이 없다). 그래서 실제 건수를 모른다.
        //    보내는 쪽·받는 쪽을 각각 직접 센다.
        //
        // ⚠️ 이 값은 **전송 건수**다. `SendReliableDataToPlayer` 를 N명에게 부르면 N 이 오른다 —
        //    준비 단계의 O(N²) 방송이 회선에 실제로 몇 건을 얹는지가 그렇게 드러난다.
        // ─────────────────────────────────────────────────────────────

        /// <summary>Host — 이 창에서 <c>SendReliableDataToPlayer/Server</c> 를 부른 횟수(수신자별).</summary>
        internal static int ReliableSendCount;

        /// <summary>어느 쪽이든 — 이 창에서 <c>OnReliableDataReceived</c> 가 불린 횟수.</summary>
        internal static int ReliableReceiveCount;

        /// <summary>그중 경제 상태(<c>'ESTA'</c>)가 차지한 건수. 준비 단계 방송의 몫을 가른다.</summary>
        internal static int ReliableEconomyStateReceiveCount;

        /// <summary>입력을 싣지 못하는 상태를 이미 알렸는가. 상태가 바뀔 때만 찍는다.</summary>
        private bool inputWithoutPlayerObjectReported;

        /// <summary>전투 오브젝트를 한 번이라도 받은 적이 있는가. 대기실의 정상 공백과 고장을 가른다.</summary>
        private bool hadPlayerObject;

        private bool playerInfoPrefabReported;
        private bool combatantPrefabReported;

        /// <summary>방 설정 브로드캐스트의 순번. 늦게 도착한 옛 값이 새 값을 덮지 않게 한다.</summary>
        private int roomSettingsSequence;

        /// <summary>Client 가 마지막으로 받아들인 방 설정 순번.</summary>
        private int lastRoomSettingsSequence;

        /// <summary>
        /// Host 가 확정한 이 방의 설정. 근거와 규칙은 <see cref="HostRoomSettings"/> 에 있다.
        /// </summary>
        /// <remarks>
        /// 세션 속성 왕복을 기다리지 않기 위한 로컬 정본이다. <b>기다렸다 읽는 방법은 없다</b> —
        /// Fusion 에는 방 안에서 세션 속성 변경을 알려주는 콜백이 없고
        /// (<c>OnSessionListUpdated</c> 는 로비 목록 전용), 남는 선택지는 폴링뿐인데 그건 이
        /// 프로젝트가 피하기로 한 방향이다.
        /// </remarks>
        private readonly HostRoomSettings hostRoomSettings = new();

        /// <summary>Host 가 보내는 경제 상태의 순번 (S15P21A606-26).</summary>
        private int economyStateSequence;

        /// <summary>
        /// Host 가 주인에게 보내는 효과 줄의 순번 (S15P21A606-300). <c>ReliableKey</c> 를 매번 다르게 만들고,
        /// 받는 쪽이 늦게 도착한 옛 줄을 버리는 데 쓴다.
        /// </summary>
        private int ownerHudEffectSequence;

        /// <summary>
        /// Client 가 보내는 경제 명령의 순번 (S15P21A606-26).
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>이것은 중복 판정에 쓰는 요청 번호가 아니다.</b> 이건 <c>ReliableKey</c> 를 매번 다르게
        /// 만들기 위한 전송 계층 번호다 — 같은 키로 두 번 보내면 Fusion 이 뒤엣것을 흘린다.
        /// 중복 적용 방지에 쓰는 번호는 페이로드 안의 <see cref="EconomyCommandRequest.Sequence"/> 다.
        /// 둘을 합치면 "재전송인데 키가 달라 새 요청처럼 보이는" 경우를 표현할 수 없어진다.
        /// </remarks>
        private int economyCommandSequence;
        private int matchLoadingSequence;
        private int combatEntryReadySequence;

        /// <summary>내가 입력한 닉네임. <see cref="PlayerInfo"/> 가 Host 에게 보낼 때 쓴다.</summary>
        public string LocalNickName => nickName;

        /// <summary>
        /// 방을 만들거나 참가할 준비가 됐는가.
        /// </summary>
        /// <remarks>
        /// PUN2 에는 "마스터 서버 접속" 단계가 있었지만 Fusion 에는 없다.
        /// 여기서는 <see cref="Connect"/> 가 Runner 를 만들고 세션 로비에 붙은 상태를 뜻한다.
        /// </remarks>
        public bool IsConnected => isConnected;

        /// <summary>
        /// 마지막으로 끊긴 사유. <b>씬을 건너 화면에 사유를 전달하는 유일한 통로다</b> (S15P21A606-27).
        /// </summary>
        /// <remarks>
        /// <para>
        /// 세션이 죽으면 <see cref="SessionReturnRouter"/> 가 방 브라우저로 씬을 바꾸는데,
        /// 그 순간 <b>전투 씬 화면의 구독이 전부 사라진다.</b> 이벤트를 다시 발행해도 새 화면은
        /// 아직 구독 전이라 받지 못한다. 그래서 값으로 남겨 두고 도착한 화면이 읽어 간다.
        /// </para>
        /// <para>
        /// ⚠️ <b>표시용이다.</b> 분기 조건으로 쓰지 않는다 — <see cref="NetworkDisconnected.Reason"/> 과 같은 규칙이다.
        /// </para>
        /// </remarks>
        public string LastDisconnectReason { get; private set; } = string.Empty;

        /// <summary>
        /// 마지막 씬 전환 요청이 거부된 이유. UI가 실패 원인을 추측하지 않고 사용자에게 보여 주기 위한 값이다.
        /// </summary>
        /// <remarks>
        /// 표시용 상태이며, 시작 가능 여부나 권한 판정에 사용하지 않는다. 성공적으로 요청을 보냈으면 비운다.
        /// </remarks>
        public string LastSceneLoadFailureReason { get; private set; } = string.Empty;


        /// <summary>세션 로비 연결 비동기 작업이 진행 중인가. 중복 클릭과 중복 하네스 요청을 막는 표시용 상태다.</summary>
        public bool IsConnecting => isConnecting;

        /// <summary>
        /// 방 생성·참가 요청이 진행 중인가 (S-UX-BXXTHX).
        /// </summary>
        /// <remarks>
        /// <para>
        /// 마법사 마지막 단계의 "방 만들기" 는 <b>요청 중 중복 클릭과 이탈을 막아야 한다.</b>
        /// 그 판단을 화면이 자기 플래그로 따로 세면 실패·연결 종료 경로에서 풀지 못해
        /// 버튼이 영영 잠긴 채로 남는다 — 진행 여부는 요청을 실제로 들고 있는 이쪽이 안다.
        /// </para>
        /// <para>
        /// ⚠️ 표시용이다. 이 값으로 권한이나 시작 가능 여부를 판정하지 않는다.
        /// </para>
        /// </remarks>
        public bool IsStartingSession => isStartingSession;

        /// <summary>현재 세션(방)에 들어와 있는가.</summary>
        public bool IsInRoom => runner != null && runner.IsRunning;

        /// <summary>내가 Host 인가. PUN2 의 <c>IsMasterClient</c> 를 대체한다.</summary>
        public bool IsHost => runner != null && runner.IsRunning && runner.IsServer;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// <b>진단 전용</b> Runner 접근자. 돌고 있지 않으면 <c>null</c> (S15P21A606-69).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="NetworkTickDiagnostics"/> 가 매 프레임 <c>TicksExecuted</c> 를 읽어야 하는데,
        /// <see cref="NetworkRunner.Instances"/> 로 아무 Runner 나 집으면 Fusion 데모(D-033)가 띄운
        /// 것을 잡을 수 있다. <b>우리가 소유한 Runner 만</b> 넘긴다.
        /// </para>
        /// <para>
        /// ⚠️ <b>게임 로직에서 쓰지 않는다.</b> Runner 를 직접 만지면 이 클래스가 수명을 관리하는
        /// 의미가 없어진다. 필요한 동작은 여기에 메서드로 추가한다. 개발 빌드에만 존재하므로
        /// 실수로 의존하면 출시 빌드에서 컴파일이 깨져 바로 드러난다.
        /// </para>
        /// </remarks>
        public NetworkRunner DiagnosticsRunner => runner != null && runner.IsRunning ? runner : null;
#endif

        internal bool TryGetHostSimulationTime(out double simulationTime)
        {
            simulationTime = 0d;
            if (runner == null || !runner.IsRunning || !runner.IsServer) return false;
            simulationTime = runner.SimulationTime;
            return true;
        }

        internal bool TryGetHostActivePlayerIds(out int[] playerIds)
        {
            playerIds = null;
            if (runner == null || !runner.IsRunning || !runner.IsServer) return false;
            var ids = new System.Collections.Generic.List<int>();
            foreach (var player in runner.ActivePlayers) ids.Add(player.PlayerId);
            playerIds = ids.ToArray();
            return true;
        }

        // ─────────────────────────────────────────────────────────────
        // 스폰 개수 조회 (S15P21A606-47)
        //
        // "참가자당 PlayerInfo 1개 + 전투 오브젝트 1개" 가 완료 조건인데, 빌드에는 Hierarchy 가
        // 없어 셀 방법이 없다. 씬 전체를 훑지 않고 Runner 가 이미 아는 것만 읽는다 —
        // 참가자는 8명이 상한이라 매 프레임 읽어도 비용이 없다.
        // ─────────────────────────────────────────────────────────────

        /// <summary>지금 방에 있는 참가자 수. <b>아래 두 개수의 기대값</b>이다.</summary>
        public int ActivePlayerCount
        {
            get
            {
                if (runner == null || !runner.IsRunning)
                {
                    return 0;
                }

                var count = 0;
                foreach (PlayerRef _ in runner.ActivePlayers)
                {
                    count++;
                }

                return count;
            }
        }

        /// <summary>실제로 살아 있는 <see cref="PlayerInfo"/> 개수.</summary>
        public int PlayerInfoCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < playerInfos.Count; i++)
                {
                    if (IsUsable(playerInfos[i]))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// 전투 오브젝트를 가진 참가자 수.
        /// </summary>
        /// <remarks>
        /// 전투 씬 밖에서는 0 이 정상이다 — 대기실에는 전투원이 없어야 한다(S15P21A606-47).
        /// </remarks>
        public int CombatantCount
        {
            get
            {
                if (runner == null || !runner.IsRunning)
                {
                    return 0;
                }

                var count = 0;
                foreach (PlayerRef player in runner.ActivePlayers)
                {
                    if (runner.TryGetPlayerObject(player, out NetworkObject combatant)
                        && combatant != null
                        && combatant.IsValid)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>세션 이름(= 방 이름). 세션에 없으면 빈 문자열.</summary>
        public string RoomName =>
            runner != null && runner.IsRunning && runner.SessionInfo != null
                ? runner.SessionInfo.Name
                : string.Empty;

        // ─────────────────────────────────────────────────────────────
        // 명령 — UI 가 부르는 것
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 닉네임을 정하고 세션 로비에 붙는다. 이후 방 목록(<see cref="RoomListChanged"/>)이 흘러온다.
        /// </summary>
        /// <remarks>
        /// Fusion 에는 PUN2 의 "마스터 서버 접속" 이 없다. 대신 <c>JoinSessionLobby</c> 로
        /// 세션 목록을 구독하는 것이 그 자리를 대신한다 —
        /// <b>이걸 하지 않으면 <see cref="OnSessionListUpdated"/> 가 한 번도 호출되지 않는다.</b>
        /// </remarks>
        public async Task Connect(string nick)
        {
            if (isConnected)
            {
                Debug.LogWarning("[NetworkManager] 이미 연결되어 있습니다.");
                return;
            }

            if (isConnecting)
            {
                Debug.LogWarning("[NetworkManager] 로비 연결 요청이 이미 진행 중입니다.");
                return;
            }

            isConnecting = true;
            nickName = PlayerInfo.NormalizeNickName(nick);
            try
            {
                EnsureRunner();
                Debug.Log($"[NetworkManager] 세션 로비 접속 시도: {nickName}, 빌드={BuildVersionIdentity.Current}");

                // Fusion 2.1.1 확인: JoinSessionLobby 는 Task<StartGameResult> 를 반환한다.
                //
                // ⚠️ AppVersion 은 **여기에도** 실어야 한다 (S-ONL-RBCJKD). 세션 시작에만 걸면
                //    참가는 막히지만 방 목록에는 다른 버전의 방이 그대로 떠서, 사용자는 보이는 방에
                //    들어가지 못하는 이유를 알 수 없다.
                var result = await runner.JoinSessionLobby(
                    SessionLobby.ClientServer,
                    customAppSettings: BuildCompatibilityAppSettings());

                if (!result.Ok)
                {
                    // 실패 이유는 문자열로만 밖에 내보낸다 — Fusion 타입을 노출하지 않는다 (D-009)
                    Debug.LogError($"[NetworkManager] 로비 접속 실패: {result.ShutdownReason}");
                    FinishLobbyConnectionFailure(DescribeShutdownReason(result.ShutdownReason));
                    return;
                }

                isConnected = true;

                // 새 연결이 성사됐으므로 지난 실패 사유를 지운다. 안 지우면 다음에 방 브라우저로
                // 돌아왔을 때 이미 해결된 옛 사유가 다시 뜬다.
                LastDisconnectReason = string.Empty;
                Debug.Log("[NetworkManager] 세션 로비 접속 성공");
                EventBus.Publish(new NetworkConnected(string.Empty));
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkManager] 로비 접속 중 예외: {e.Message}");
                FinishLobbyConnectionFailure(LobbyConnectExceptionMessage);
            }
            finally
            {
                isConnecting = false;
            }
        }

        /// <summary>방을 만들고 Host 가 된다.</summary>
        /// <summary>
        /// 방을 만들고 Host 가 된다. 공개 방이고 표시 제목은 방 코드와 같다.
        /// </summary>
        public void CreateRoom(string roomName) => CreateRoom(roomName, roomName, isPublic: true);

        /// <summary>
        /// 방을 만들고 Host 가 된다 (S15P21A606-55).
        /// </summary>
        /// <param name="roomCode">
        /// 방 코드. <b>이것이 곧 Photon 세션 이름</b>이라 AppId 안에서 유일해야 한다.
        /// 비우면 Photon 이 만들어 준다.
        /// </param>
        /// <param name="title">목록에 보여줄 방 제목. 유일할 필요가 없다.</param>
        /// <param name="isPublic">
        /// <c>false</c> 면 방 목록에서 숨긴다. 숨겨도 <b>코드를 아는 사람은 들어올 수 있다</b> —
        /// 그것이 비공개방의 작동 방식이다.
        /// </param>
        /// <remarks>
        /// <para>
        /// <b>왜 방 코드를 세션 이름으로 쓰는가.</b> 비공개방은 <c>IsVisible=false</c> 라 세션 목록에
        /// 뜨지 않고, 목록에 없으면 <see cref="SessionInfo.Properties"/> 를 <b>읽을 방법이 없다.</b>
        /// 코드를 속성에 두면 비공개방을 찾을 수단이 사라진다. 이름은 목록 없이도 직접 참가할 수
        /// 있으므로 코드는 이름이어야 한다 — 취향이 아니라 제약이다.
        /// </para>
        /// </remarks>
        public void CreateRoom(string roomCode, string title, bool isPublic) =>
            _ = StartSession(GameMode.Host, roomCode, isPublic, title);

        /// <summary>
        /// 정원까지 지정해 방을 만든다 (S15P21A606-55).
        /// </summary>
        /// <param name="maxPlayers">
        /// 2~8. 범위를 벗어나면 잘라낸다 — S-GD-EZ682A 가 상한 8을 정했고, 하한 2는
        /// 혼자서는 대전이 성립하지 않기 때문이다.
        /// </param>
        public void CreateRoom(string roomCode, string title, bool isPublic, int maxPlayers) =>
            _ = StartSession(GameMode.Host, roomCode, isPublic, title, maxPlayers);

        /// <summary>
        /// 비밀번호까지 지정해 방을 만든다 (S-UX-BXXTHX 방 만들기 3/3).
        /// </summary>
        /// <param name="password">
        /// 선택적 입장 비밀번호. 비우면 잠기지 않은 방이 된다 — <b>기존 오버로드와 같은 방</b>이다.
        /// </param>
        /// <remarks>
        /// <para>
        /// <b>이것은 계정 인증이 아니다.</b> 이 방에 한정된 입장 암호이며 Ready 신원 게이트
        /// (S-ONL-ZJS2NM)를 대체하지 않는다. 비밀번호를 맞춘 사람도 로그인하지 않았으면
        /// 준비할 수 없다.
        /// </para>
        /// <para>
        /// ⚠️ <b>잠긴 방은 방 코드를 비울 수 없다.</b> 검증값이 방 코드에 묶여 있어서
        /// (<see cref="LobbyRoomAdmission"/>), Photon 이 이름을 지어 주면 참가자가 같은 값을
        /// 만들 수 없다. 코드 없이 잠그려 하면 방을 열지 않고
        /// <see cref="RoomJoinFailed"/> 로 알린다.
        /// </para>
        /// </remarks>
        public void CreateRoom(string roomCode, string title, bool isPublic, int maxPlayers, string password) =>
            _ = StartSession(GameMode.Host, roomCode, isPublic, title, maxPlayers, password: password);

        /// <summary>
        /// 마지막으로 발행한 방 목록의 <b>사본</b> (S-UX-BXXTHX).
        /// </summary>
        /// <remarks>
        /// 늦게 켜진 화면이 <c>OnShow</c> 에서 현재 목록을 직접 물어보기 위한 창구다.
        /// 이후 변경은 <see cref="RoomListChanged"/> 로 온다 — 둘을 함께 쓰면 구독 시점과
        /// 무관하게 같은 화면이 나온다. 방을 나가거나 로비를 떠나면 비어 있다.
        /// </remarks>
        public RoomSlot[] GetRoomListSnapshot() => (RoomSlot[])roomListSnapshot.Clone();

        /// <summary>
        /// 마지막으로 발행한 참가자 목록의 <b>사본</b> (S-UX-BXXTHX).
        /// </summary>
        /// <remarks>
        /// <para>
        /// 대기방이 독립 화면이 되면서 <b>참가자 목록보다 화면이 늦게 켜진다.</b> 입장 직후의
        /// <see cref="RoomPlayersChanged"/> 를 놓친 화면이 빈 슬롯만 그리지 않도록 현재 상태를
        /// 여기서 읽는다.
        /// </para>
        /// <para>
        /// ⚠️ <b>시작 가능 여부는 여기 없다.</b> 그 판정은 Host 의 <see cref="CanStartMatch"/> 뿐이고
        /// (D-030), 화면은 <see cref="RoomPlayersChanged.CanStart"/> 를 그대로 쓴다.
        /// </para>
        /// </remarks>
        public PlayerSlot[] GetPlayerListSnapshot() => (PlayerSlot[])playerListSnapshot.Clone();

        /// <summary>이 방이 지금 참가를 받는가. 방에 없으면 <c>false</c>.</summary>
        public bool IsRoomOpen =>
            TryResolveRoomSettings(out RoomSettingsChanged settings) && settings.IsOpen;

        /// <summary>
        /// 지금 들어와 있는 방의 <b>실제 정원</b>. 방에 없으면 기본 설정값.
        /// </summary>
        /// <remarks>
        /// ⚠️ <see cref="maxPlayersPerRoom"/> 과 구분한다. 그쪽은 <b>새 방을 만들 때의 기본값</b>이고,
        /// 이쪽은 <b>지금 이 방이 실제로 몇 명까지 받는가</b>다. 정원 4명으로 만든 방에서 둘을 섞어 쓰면
        /// 목록과 대기실이 다른 숫자를 보여준다.
        /// </remarks>
        public int CurrentRoomMaxPlayers =>
            TryResolveRoomSettings(out RoomSettingsChanged settings) && settings.MaxPlayers > 0
                ? settings.MaxPlayers
                : maxPlayersPerRoom;

        /// <summary>
        /// 지금 방의 제목. 방 밖이면 빈 문자열 (S15P21A606-55 · -37).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>왜 조회 창구가 필요한가.</b> <see cref="RoomSettingsChanged"/> 는 값이 정해지거나
        /// 바뀔 때만 발행된다. 화면이 그 순간보다 늦게 켜지면 다음 변경이 있을 때까지 제목을
        /// 알 수 없고, 그 사이 방 코드를 대신 그리게 된다. 화면이 <c>OnShow</c> 에서 현재
        /// 상태를 직접 물어볼 수 있어야 한다
        /// (<c>docs/client/ui-domain-contract.md</c> §13-3, MR !45 리뷰에서 요청됨).
        /// </para>
        /// <para>
        /// <b>제목이 없는 방은 방 코드가 그대로 나온다</b> — <see cref="RoomSlot.Title"/> 과 같은 규칙이다.
        /// </para>
        /// </remarks>
        public string CurrentRoomTitle
        {
            get => TryResolveRoomSettings(out RoomSettingsChanged settings)
                ? settings.Title
                : string.Empty;
        }

        /// <summary>이 방이 목록에 보이는가(= 공개방인가). 방에 없으면 <c>false</c>.</summary>
        public bool IsRoomVisible =>
            TryResolveRoomSettings(out RoomSettingsChanged settings) && settings.IsPublic;

        /// <summary>이름을 지정해 방에 참가한다(Client 로).</summary>
        public void JoinRoom(string roomName) => _ = StartSession(GameMode.Client, roomName);

        /// <summary>
        /// 비밀번호를 함께 보내 방에 참가한다 (S-UX-BXXTHX 코드 입장·잠긴 방).
        /// </summary>
        /// <param name="password">
        /// 방 비밀번호. 비우면 <see cref="JoinRoom(string)"/> 과 완전히 같다 —
        /// 잠기지 않은 방은 아무것도 보내지 않는다.
        /// </param>
        /// <remarks>
        /// <para>
        /// 원문은 회선을 넘지 않는다. <see cref="LobbyRoomAdmission.BuildJoinToken"/> 이 만든
        /// 고정 길이 파생값만 <c>StartGameArgs.ConnectionToken</c> 에 실리고, 맞지 않으면
        /// Host 의 <see cref="OnConnectRequest"/> 가 거부한다.
        /// </para>
        /// <para>
        /// ⚠️ <b>비밀번호가 틀렸다는 것은 거부 사유로 구분되지 않는다.</b> 거부는 연결 실패로
        /// 돌아오므로 화면은 <see cref="RoomJoinFailed"/> 문구를 그대로 보여준다 —
        /// 여기서 "비밀번호가 틀렸다" 를 지어내면 방이 사라진 경우까지 같은 말로 덮는다.
        /// </para>
        /// </remarks>
        public void JoinRoom(string roomName, string password) =>
            _ = StartSession(GameMode.Client, roomName, password: password);

        /// <summary>
        /// 지금 참가 가능한 <b>잠기지 않은 공개 방</b> 하나를 골라 들어간다 (S-UX-BXXTHX).
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠️ <b>예전에는 <c>GameMode.AutoHostOrClient</c> 에 세션 이름 없이 맡겼다.</b>
        /// 그러면 방 선택을 Fusion 이 내부에서 하므로 <see cref="OnSessionListUpdated"/> 의
        /// 필터를 <b>한 번도 거치지 않는다.</b> 목록에서 "참가 불가" 로 걸러낸 죽은 방을
        /// 그대로 골라 <c>GameNotFound</c> 로 실패했다 — 표시만 고치고 동작은 그대로였다
        /// (실측 2026-09-01). 그래서 우리가 고른다.
        /// </para>
        /// <para>
        /// ⚠️ <b>후보가 없으면 방을 만들지 않는다</b> (S-UX-BXXTHX "랜덤 참가로 방을 자동
        /// 생성하지 않는다"). 예전에는 여기서 Host 로 새 방을 열었는데, 사용자는 <b>참가</b>를
        /// 눌렀지 방을 만들 생각이 없었다 — 방 만들기는 마법사 마지막 단계에서만 일어난다.
        /// 대신 안전한 문구로 실패를 알려 화면이 버튼을 닫을 수 있게 한다.
        /// </para>
        /// <para>
        /// 후보는 <see cref="randomJoinCandidates"/> 로 좁힌다 — 참가 가능하고, 비어 있지 않고,
        /// 정원이 남았고, 공개이고, <b>잠기지 않은</b> 방이다. 잠긴 방을 넣으면 비밀번호를 물을
        /// 자리가 없어 반드시 거부당한다.
        /// </para>
        /// <para>
        /// ⚠️ <b>그래도 실패할 수 있다.</b> 목록은 갱신 시점의 스냅샷이라, 고른 방이 그 사이
        /// 닫혔을 수 있다. 실패는 <see cref="RoomJoinFailed"/> 로 사용자에게 전달된다 —
        /// 여기서 자동 재시도하지 않는 이유는, 재시도가 같은 낡은 목록을 다시 보기 때문이다.
        /// </para>
        /// </remarks>
        public void JoinRandomRoom()
        {
            if (randomJoinCandidates.Count == 0)
            {
                Debug.Log("[NetworkManager] 랜덤 참가 후보가 없습니다. 방을 만들지 않습니다.");
                // 랜덤 참가는 방을 만들지 않는다 — 문구에서도 방 만들기를 권하지 않는다.
                // 여기서 권하면 사용자가 "참가" 를 누른 흐름이 방 만들기로 이어진 것처럼 읽힌다.
                EventBus.Publish(new RoomJoinFailed(
                    "지금 바로 참가할 수 있는 공개 방이 없습니다. 목록을 새로고침해 주세요."));
                return;
            }

            string picked = randomJoinCandidates[UnityEngine.Random.Range(0, randomJoinCandidates.Count)];
            Debug.Log($"[NetworkManager] 랜덤 참가 후보 {randomJoinCandidates.Count}개 중 '{picked}' 을 고릅니다.");
            _ = StartSession(GameMode.Client, picked);
        }

        /// <summary>세션에서 나간다. 결과는 <see cref="OnShutdown"/> 으로 돌아온다.</summary>
        public async Task LeaveRoom()
        {
            if (runner == null || !runner.IsRunning)
            {
                Debug.LogWarning("[NetworkManager] 세션에 있지 않습니다.");
                return;
            }

            try
            {
                await runner.Shutdown();
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkManager] 세션 종료 중 예외: {e.Message}");
            }
        }

        /// <summary>
        /// Photon 연결을 통째로 내린다 — 방 안이든 로비뿐이든. 로그아웃·다른 기기 로그인
        /// (<c>SESSION_REPLACED</c>)처럼 <b>계정 세션이 끝났을 때</b> 부른다 (S15P21A606-96).
        /// 호출부는 <c>AuthAdapter.SignOut()</c> — 연결은 축5 가 맡는다 (-81 후속).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>왜 <see cref="LeaveRoom"/> 으로는 안 되는가.</b> LeaveRoom 의 가드는
        /// <c>runner.IsRunning</c> — 방 세션이 돌 때만 통과한다. 로비에만 붙은 runner 는
        /// IsRunning 이 아니라서, 로그아웃해도 ① 죽은 계정이 Photon 접속을 유지하고(CCU 점유,
        /// D-032 메모) ② <see cref="Connect"/> 가 다시 불리지 않아 닉네임 스냅샷이 이전 계정
        /// 값으로 남았다 — 결과·메달(accountId)과 화면 표시(닉네임)가 다른 사람으로 갈린다.
        /// </para>
        /// <para>
        /// 내려간 뒤는 전부 기존 경로 재사용이다 — <see cref="OnShutdown"/> 이
        /// <c>DisposeRunner</c> 로 세션 잔재(닉네임 스냅샷 포함)를 비우고, 다음 로그인의
        /// 자동 접속이 <see cref="IsConnected"/>=false 를 보고 Connect(새 닉네임)를 다시 탄다.
        /// </para>
        /// <para>
        /// runner 가 없으면(접속 전에 로그아웃) 스냅샷만 비우고 조용히 끝난다 —
        /// 로그아웃 경로에서 반복 호출될 수 있어 경고를 찍지 않는다.
        /// </para>
        /// </remarks>
        public async Task Disconnect()
        {
            if (runner == null)
            {
                // 접속 전 로그아웃이거나, runner 가 콜백 없이 파괴된 잔존 상태다
                // (예: 로비 전용 runner 가 씬 전환에서 죽으면 OnShutdown 이 오지 않아
                // isConnected 가 true 로 남는다). 스냅샷과 함께 플래그도 정리해서
                // 다음 자동 접속이 Connect 를 다시 타게 한다.
                nickName = DefaultNickName;
                isConnected = false;
                return;
            }

            Debug.Log("[NetworkManager] 연결을 내립니다 (로그아웃).");
            LastDisconnectReason = string.Empty;

            // 로그아웃 셧다운은 사고가 아니다 — OnShutdown 의 세션 알림을 억제한다.
            // 억제하지 않으면 SessionReturnRouter 가 "세션 끊김" 으로 오해해 로비로 끌고 가고,
            // LobbyScreen 자동 접속이 로그아웃 상태의 기본 닉네임("Player")으로 유령 재접속했다
            // (2026-09-07 실측). 플래그는 OnShutdown 이 소비하고, 콜백이 안 오는 경로를 위해
            // 아래 finally 가 한 번 더 되돌린다.
            suppressShutdownPublish = true;
            try
            {
                await runner.Shutdown();

                // 로비에만 붙은 runner(IsRunning=false)에서는 Shutdown() 이 no-op 으로 즉시
                // 완료되고 OnShutdown 도 오지 않을 수 있다 — 2026-09-07 에디터 실측.
                // 그 사각지대를 여기서 닫는다. OnShutdown 이 이미 정리했어도 같은 결과를 내는
                // 멱등 호출이다.
                DisposeRunner();
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkManager] 연결 종료 중 예외 — 정리는 계속합니다: {e.Message}");
                DisposeRunner();
            }
            finally
            {
                suppressShutdownPublish = false;
            }
        }

        /// <summary>
        /// 내 준비 상태를 바꾼다. 내 <see cref="PlayerInfo"/> 를 통해 Host 에게 요청한다.
        /// </summary>
        /// <remarks>
        /// <b>내가 쓰는 것이 아니라 Host 에게 부탁하는 것이다</b> — <c>[Networked]</c> 는 StateAuthority 만
        /// 쓸 수 있으므로 값이 실제로 바뀌는 것은 Host 의 처리 이후이고, 그 결과가
        /// <see cref="RoomPlayersChanged"/> 로 돌아온다. UI 는 자기 화면 값을 먼저 뒤집지 말고
        /// 이벤트를 기다린다. 그렇게 하지 않으면 Host 가 거절한 상태와 화면이 어긋난다.
        /// <para>
        /// 스폰 직후에는 <see cref="PlayerInfo"/> 가 아직 없을 수 있다. 그때는 경고만 남기고 무시한다.
        /// </para>
        /// </remarks>
        public void SetReady(bool ready)
        {
            if (runner == null || !runner.IsRunning)
            {
                Debug.LogWarning("[NetworkManager] 세션에 있지 않아 준비 상태를 바꿀 수 없습니다.");
                return;
            }

            var mine = FindLocalPlayerInfo();
            if (mine == null)
            {
                Debug.LogWarning("[NetworkManager] 내 PlayerInfo 가 아직 스폰되지 않았습니다. 잠시 후 다시 시도하세요.");
                return;
            }

            // 로그인하지 않았으면 Host 가 어차피 거절한다(S-ONL-ZJS2NM). 여기서 미리 걸러 요청을 아낀다.
            //
            // ⚠️ 이건 편의일 뿐 권위가 아니다 — 진짜 판정은 Host 의 PlayerInfo.RPC_SetReady 가 한다.
            //    Host 자신은 아래 조건을 통과해도 자기 accountId 등록이 늦으면 그쪽에서 걸린다.
            if (ready
                && (!Api.AuthAdapter.HasInstance
                    || Api.AuthAdapter.Instance == null
                    || !Api.AuthAdapter.Instance.IsSignedIn))
            {
                Debug.LogWarning("[NetworkManager] 로그인 상태가 아니어서 준비할 수 없습니다.");
                return;
            }

            mine.RPC_SetReady(ready);
        }

        /// <summary>내가 준비 상태인가. 표시용이다.</summary>
        public bool IsLocalReady
        {
            get
            {
                var mine = FindLocalPlayerInfo();
                return mine != null && mine.IsReady;
            }
        }

        /// <summary>세션 참가자가 없을 때 거부 이벤트에 싣는 참가자 ID. <c>LobbyPlayerView.Id</c> 에 없는 값이다.</summary>
        public const int NoLocalPlayerId = -1;

        private bool weaponChoiceCommandSubscribed;

        protected override void OnSingletonAwake()
        {
            base.OnSingletonAwake();
            SubscribeLobbyWeaponChoiceCommand();
        }

        // 싱글톤으로 확정된 인스턴스만 구독한다. OnEnable 에 두면 Awake 에서 파괴될 중복 인스턴스도
        // 그 프레임 동안 구독해 거부가 두 번 나간다.
        private void SubscribeLobbyWeaponChoiceCommand()
        {
            if (weaponChoiceCommandSubscribed) return;
            EventBus.Subscribe<LobbyWeaponChoiceCommand>(OnLobbyWeaponChoiceCommand);
            weaponChoiceCommandSubscribed = true;
        }

        private void UnsubscribeLobbyWeaponChoiceCommand()
        {
            if (!weaponChoiceCommandSubscribed) return;
            EventBus.Unsubscribe<LobbyWeaponChoiceCommand>(OnLobbyWeaponChoiceCommand);
            weaponChoiceCommandSubscribed = false;
        }

        /// <summary>
        /// 무기 선택 명령을 실어 보낼 내 <see cref="PlayerInfo"/> 가 없으면 거부를 알린다 (S15P21A606-268).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>왜 <see cref="PlayerInfo"/> 가 아니라 여기서 판정하는가.</b> 명령은 방 안의 모든
        /// <see cref="PlayerInfo"/> 가 받고, 그중 입력 권한을 가진 하나만 Host 에게 보낸다. 나머지는
        /// "남의 인스턴스" 라 조용히 빠지는 것이 맞다. 거부를 거기서 쏘면 인원수만큼 나가고, 정작
        /// <see cref="PlayerInfo"/> 가 하나도 없을 때(방 입장 전·스폰 전)는 구독자가 없어 아무것도 안 나간다.
        /// 이 컴포넌트는 싱글톤이고 <see cref="FindLocalPlayerInfo"/> 로 내 것을 특정하므로 정확히 한 번이다.
        /// </para>
        /// <para>
        /// 내 <see cref="PlayerInfo"/> 가 있으면 아무것도 하지 않는다 — 그쪽이 Host 에 보내고, Host 의 거절은
        /// <see cref="PlayerInfo.RPC_WeaponChoiceRejected"/> 로 돌아온다. 여기서도 쏘면 이중 발행이다.
        /// </para>
        /// </remarks>
        private void OnLobbyWeaponChoiceCommand(LobbyWeaponChoiceCommand command)
        {
            int localPlayerId = runner != null && runner.IsRunning ? runner.LocalPlayer.PlayerId : NoLocalPlayerId;
            RejectWeaponChoiceWithoutLocalPlayer(command, FindLocalPlayerInfo() != null, localPlayerId);
        }

        /// <summary>로컬 참가자가 없을 때만 <see cref="LobbyWeaponChoiceRejected"/> 를 한 번 발행한다.</summary>
        /// <returns>거부를 발행했으면 <c>true</c>.</returns>
        internal static bool RejectWeaponChoiceWithoutLocalPlayer(
            LobbyWeaponChoiceCommand command, bool hasLocalPlayerInfo, int localPlayerId)
        {
            if (hasLocalPlayerInfo) return false;

            Debug.LogWarning($"[NetworkManager] 내 PlayerInfo 가 없어 무기 선택({command.Choice})을 보낼 수 없습니다.");
            // 확정된 값이 없으므로 LobbyWeaponChoiceChanged 로 되맞추지 않는다 — 되맞출 기준이 없다.
            EventBus.Publish(new LobbyWeaponChoiceRejected(localPlayerId, command.Choice,
                LobbyWeaponChoiceRules.Default, LobbyWeaponChoiceRejectReason.NotInRoom, true));
            return true;
        }

        /// <summary>
        /// 매치를 시작할 수 있는 최소 인원. <b>2명이다</b> (S-GD-CC8KMR · S-UX-BXXTHX).
        /// </summary>
        /// <remarks>
        /// 상한은 세션 생성 인자인 <see cref="maxPlayersPerRoom"/> 쪽이다(S-GD-EZ682A 로 8명).
        /// </remarks>
        private const int MinPlayersToStart = 2;

        /// <summary>
        /// 지금 매치를 시작할 수 있는가. <b>Host 에서만 <c>true</c> 가 될 수 있다.</b>
        /// </summary>
        /// <remarks>
        /// <para>
        /// 조건은 넷이다 — 내가 Host 이고, 방에 <see cref="MinPlayersToStart"/> 명 이상이 있고,
        /// <b>지금 방에 있는 사람 전원</b>이 준비를 마쳤고, 그 전원이 계정·외형 등록을 마쳤다
        /// (S-GD-CC8KMR 최소 2명, S-ONL-42ATRV·S-ONL-ZJS2NM 신원 게이트).
        /// </para>
        /// <para>
        /// ⚠️ <b>"준비된 <see cref="PlayerInfo"/> 를 세는" 것으로는 안 된다.</b> 방금 들어온 사람은
        /// 아직 <see cref="PlayerInfo"/> 가 없을 수 있는데, 있는 것만 세면 <b>그 사람은 존재하지
        /// 않는 것처럼 취급되어</b> 준비하지 않은 채로 매치에 끌려 들어간다. 그래서 기준을 뒤집어
        /// <c>ActivePlayers</c> 를 돌면서 <b>각자에게 쓸 수 있는 <see cref="PlayerInfo"/> 가 있는지</b>
        /// 를 요구한다 — 없으면 아직 시작할 수 없다(잠시 뒤 스폰되면 다시 계산된다).
        /// </para>
        /// <para>
        /// ⚠️ <b>계정·외형 등록까지 본다.</b> 등록이 없으면 결과 제출이 통째로 거부되고
        /// (<see cref="CaptureMatchRoster"/>), 전투 오브젝트도 스폰되지 않는다. Ready 게이트가
        /// 이미 막는 조건이지만, <b>여기서 한 번 더 확인해도 비용이 없고</b> 게이트가 새면
        /// 매치가 시작된 뒤에야 드러난다.
        /// </para>
        /// <para>
        /// 클라이언트에서는 항상 <c>false</c> 다. 클라이언트가 이 값을 보고 시작을 시도하는 경로를
        /// 아예 만들지 않기 위한 것이다 (D-030).
        /// </para>
        /// </remarks>
        public bool CanStartMatch
        {
            get
            {
                if (runner == null || !runner.IsRunning || !runner.IsServer) return false;

                // ⚠️ 훈련장 예외 분기를 두지 않는다. 훈련 진입은 자기 경로
                //    (CreateTrainingRoom/JoinTrainingRoom)로 들어오고 이 게이트를 거치지 않으므로,
                //    예외를 만들면 누군가 훈련 방에서 TryStartMatch 를 부를 때 최소 인원·신원
                //    조건만 약해진다. 남은 규칙이 곧 매치 규칙이다.
                var activeCount = 0;
                foreach (PlayerRef player in runner.ActivePlayers)
                {
                    activeCount++;

                    if (!TryFindPlayerInfo(player, out PlayerInfo info) || !info.IsReady)
                    {
                        return false;
                    }

                    if (!HasAccountId(player) || !HasUsableAppearancePresetSeed(player))
                    {
                        return false;
                    }
                }

                // 봇이 최소 인원을 채울 수 있다 (S-ONL-9GXTYQ). 다만 위 루프의 준비·신원 게이트는
                // 사람에게만 적용되고 봇이 그것을 대신하지 않는다 — 봇은 셀 뿐이다.
                return activeCount + botRoster.Count <= LobbyRoomRules.MaxCapacity
                    && LobbyBotRoster.MeetsMinimumRoster(activeCount, botRoster.Count);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 대기방 봇 (S-ONL-9GXTYQ · D-ONL-AM6C7M)
        // ─────────────────────────────────────────────────────────────

        /// <summary>지금 방의 봇 명단. Host·클라이언트 모두 같은 값을 읽는다.</summary>
        public IReadOnlyList<LobbyBotSlot> LobbyBots => botRoster.Slots;

        /// <summary>
        /// 지금 이 클라이언트가 봇을 추가·제거할 수 있는가. 화면이 버튼을 <b>보일지</b>의 기준이다.
        /// </summary>
        public bool CanLocalManageBots =>
            runner != null && runner.IsRunning
            && botRoster.HasControl(runner.LocalPlayer.PlayerId, HostPlayerId);

        /// <summary>방장의 <c>PlayerId</c>. 아직 아무도 Host 로 복제되지 않았으면 0.</summary>
        /// <remarks>
        /// ⚠️ <c>runner.IsServer</c> 로 대신할 수 없다 — 그것은 "내가 Host 인가" 이고, 권한 판정에는
        /// "누가 Host 인가" 가 필요하다. 클라이언트도 같은 규칙으로 버튼을 그려야 하므로 복제된
        /// <see cref="PlayerInfo.IsHost"/> 에서 읽는다.
        /// </remarks>
        private int HostPlayerId
        {
            get
            {
                for (var i = 0; i < playerInfos.Count; i++)
                {
                    PlayerInfo info = playerInfos[i];
                    if (IsUsable(info) && info.IsHost) return info.Owner.PlayerId;
                }

                // 내가 Host 면 PlayerInfo 가 아직 없어도 답을 안다. 클라이언트는 복제값으로 버틴다.
                // 전용 서버는 로컬 플레이어가 없으므로 자기가 정한 방장을 답한다 (S15P21A606-321).
                if (runner != null && runner.IsRunning && runner.IsServer)
                    return runner.IsPlayer ? runner.LocalPlayer.PlayerId : roomOwnerPlayerId;
                return replicatedBotHostPlayerId;
            }
        }

        /// <summary>봇 하나 추가를 요청한다. Host 면 바로 적용하고, 클라이언트면 Host 에게 보낸다.</summary>
        public void RequestAddBot() => SendBotCommand(LobbyBotCommandKind.Add);

        /// <summary>
        /// 봇 하나 제거를 요청한다. 대상은 <b>가장 나중에 추가된 봇</b>이다 (S-UX-9SMAFJ).
        /// </summary>
        public void RequestRemoveBot() => SendBotCommand(LobbyBotCommandKind.Remove);

        private void SendBotCommand(LobbyBotCommandKind kind)
        {
            if (runner == null || !runner.IsRunning) return;

            var command = new LobbyBotCommand(kind);
            if (runner.IsServer)
            {
                // Host 는 자기 요청을 통로에 태우지 않는다 — 신뢰 데이터는 자기 자신에게 오지 않는다.
                ApplyBotCommand(runner.LocalPlayer.PlayerId, command);
                return;
            }

            byte[] payload = LobbyBotWireProtocol.SerializeCommand(command);
            if (payload == null) return;
            ReliableSendCount++;
            runner.SendReliableDataToServer(
                ReliableKey.FromInts(LobbyBotWireProtocol.CommandMagic, ++botWireSequence), payload);
        }

        /// <summary>Host 에서만 돈다. 권한·정원 판정은 전부 <see cref="LobbyBotRoster"/> 가 한다.</summary>
        private void ApplyBotCommand(int requesterPlayerId, in LobbyBotCommand command)
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer) return;

            int hostId = HostPlayerId;
            int humanCount = CountActivePlayers();
            LobbyBotActionResult result = command.Kind switch
            {
                LobbyBotCommandKind.Add =>
                    botRoster.TryAdd(requesterPlayerId, hostId, humanCount, CurrentRoomMaxPlayers),
                LobbyBotCommandKind.Remove =>
                    botRoster.TryRemove(requesterPlayerId, hostId, botId: 0),
                _ => LobbyBotActionResult.NotFound,
            };

            if (result != LobbyBotActionResult.Ok) return;

            BroadcastBotState();
            PublishBots();
            // 봇 수가 시작 가능 여부를 바꾼다 — 화면의 `게임 시작` 이 같은 프레임에 따라와야 한다.
            PublishPlayers();
        }

        private int CountActivePlayers()
        {
            if (runner == null || !runner.IsRunning) return 0;
            var count = 0;
            foreach (PlayerRef _ in runner.ActivePlayers) count++;
            return count;
        }

        /// <summary>Host → 전원. 명단 전체를 보낸다 — 델타로 쪼개면 한 건을 놓쳤을 때 복구가 없다.</summary>
        private void BroadcastBotState()
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer) return;

            byte[] payload = LobbyBotWireProtocol.SerializeState(
                HostPlayerId, botRoster.Slots, botRoster.GrantedPlayerIds);
            if (payload == null) return;

            ReliableKey key = ReliableKey.FromInts(LobbyBotWireProtocol.StateMagic, ++botWireSequence, 0, 0);
            foreach (PlayerRef player in runner.ActivePlayers)
            {
                if (player == runner.LocalPlayer) continue;
                ReliableSendCount++;
                runner.SendReliableDataToPlayer(player, key, payload);
            }
        }

        /// <summary>
        /// Host가 늦게 들어온 한 사람에게 현재 봇 명단 전체를 보낸다.
        /// 봇이 이미 추가된 뒤 참가한 Client는 과거 broadcast를 받을 수 없으므로 참가 시점 스냅샷이 필요하다.
        /// </summary>
        private void SendBotStateToPlayer(PlayerRef player)
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer || player == runner.LocalPlayer) return;

            byte[] payload = LobbyBotWireProtocol.SerializeState(
                HostPlayerId, botRoster.Slots, botRoster.GrantedPlayerIds);
            if (payload == null) return;

            ReliableKey key = ReliableKey.FromInts(LobbyBotWireProtocol.StateMagic, ++botWireSequence, 0, 0);
            runner.SendReliableDataToPlayer(player, key, payload);
        }

        private void PublishBots()
        {
            // 사본을 실어 보낸다 — 구독자가 배열을 만지면 우리 명단까지 바뀐다 (RoomListChanged 와 같은 규칙).
            IReadOnlyList<LobbyBotSlot> slots = botRoster.Slots;
            var copy = new LobbyBotSlot[slots.Count];
            for (var i = 0; i < copy.Length; i++) copy[i] = slots[i];

            EventBus.Publish(new LobbyBotsChanged(
                copy, CanLocalManageBots, LobbyBotRoster.MaxBots(CurrentRoomMaxPlayers)));
        }

        /// <summary>
        /// 사람이 들어와 정원을 넘기면 봇을 자른다 (S-ONL-9GXTYQ).
        /// </summary>
        /// <remarks>
        /// ⚠️ Photon 의 <c>MaxPlayers</c> 는 <b>실제 피어만</b> 센다. 봇은 피어가 아니므로 봇으로 찬
        /// 방에도 사람은 그대로 들어온다 — 여기서 자르지 않으면 8명 방에 9명분 슬롯이 생긴다.
        /// </remarks>
        private void TrimBotsToCapacity()
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer) return;
            if (botRoster.TrimToCapacity(CountActivePlayers(), CurrentRoomMaxPlayers) <= 0) return;

            BroadcastBotState();
            PublishBots();
        }

        /// <summary>이 플레이어의 <see cref="PlayerInfo"/> 를 찾는다. 아직 없으면 <c>false</c>.</summary>
        /// <remarks>
        /// 없는 것이 곧 결함은 아니다 — 참가 콜백과 스폰 사이 한두 프레임에는 정상적으로 없다.
        /// 다만 그동안은 <b>시작할 수 없다</b>.
        /// </remarks>
        private bool TryFindPlayerInfo(PlayerRef player, out PlayerInfo found)
        {
            for (var i = 0; i < playerInfos.Count; i++)
            {
                PlayerInfo info = playerInfos[i];
                if (IsUsable(info) && info.Owner == player)
                {
                    found = info;
                    return true;
                }
            }

            found = null;
            return false;
        }

        /// <summary>
        /// 이 플레이어의 계정 외형 프리셋을 <b>읽을 수 있는가</b>. 캐시가 비었으면 복제값도 본다.
        /// </summary>
        /// <remarks>
        /// <see cref="TryGetAppearancePresetSeed"/> 와 달리 캐시를 채우지 않는다 —
        /// 판정을 읽는 자리에서 상태를 바꾸면 언제 채워졌는지 따라가기 어려워진다.
        /// </remarks>
        private bool HasUsableAppearancePresetSeed(PlayerRef player)
        {
            if (HasAppearancePresetSeed(player))
            {
                return true;
            }

            return TryFindPlayerInfo(player, out PlayerInfo info) && info.AppearancePresetSeed > 0;
        }

        /// <summary>
        /// 전원이 준비됐으면 매치를 시작한다(= 방 전원의 씬을 전환한다).
        /// </summary>
        /// <returns>씬 전환 요청이 수락됐으면 <c>true</c>. 조건 또는 요청이 거부되면 <c>false</c> 이고 시작 이벤트를 발행하지 않는다.</returns>
        /// <remarks>
        /// <b>판정과 시작이 같은 곳에 있고, 그곳은 Host 뿐이다.</b> 클라이언트가 이걸 불러도
        /// <see cref="CanStartMatch"/> 가 <c>false</c> 라 아무 일도 하지 않는다.
        /// </remarks>
        public bool TryStartMatch(string sceneName)
        {
            LastSceneLoadFailureReason = string.Empty;
            if (!CanStartMatch)
            {
                Debug.LogWarning("[NetworkManager] 아직 시작할 수 없습니다 (Host 가 아니거나 준비되지 않은 플레이어가 있습니다).");
                return false;
            }

            // 씬 전환 요청이 수락되기 전에는 방을 닫거나 매치 시작 이벤트를 발행하지 않는다.
            // 그렇게 하면 등록되지 않은 씬이나 권한 거부 뒤에도 Coordinator가 매치를 시작했다고
            // 오인하는 것을 막는다.
            if (!LoadLevelForRoom(sceneName, isMatchStart: true))
            {
                return false;
            }

            // 진행 중인 방에 새 사람이 들어오면 스폰·라운드 상태가 어긋난다.
            // 목록에는 남되 참가만 막는다 (S15P21A606-55).
            SetRoomOpen(false);

            EventBus.Publish(new RoomMatchStarted(sceneName));
            return true;
        }

        /// <summary>내 <see cref="PlayerInfo"/>. 없으면 <c>null</c>.</summary>
        private PlayerInfo FindLocalPlayerInfo()
        {
            if (runner == null || !runner.IsRunning) return null;

            foreach (var info in playerInfos)
            {
                if (IsUsable(info) && info.Owner == runner.LocalPlayer)
                {
                    return info;
                }
            }

            return null;
        }

        /// <summary>
        /// 방 안 전원의 씬을 함께 전환한다. <b>씬 권한을 가진 쪽(Host)만 호출할 수 있다.</b>
        /// </summary>
        /// <returns>Fusion에 씬 전환 요청을 보냈으면 <c>true</c>. 등록·세션·권한 검증 또는 요청이 거부되면 <c>false</c>.</returns>
        /// <remarks>
        /// 방 밖(타이틀 ↔ 로비)에서는 <c>SceneLoader.Load</c> 를 쓴다.
        /// 두 경로를 섞으면 그 사람만 씬이 바뀌고 나머지는 남는데, <b>콘솔에 아무것도 나오지 않는다.</b>
        /// </remarks>
        public bool LoadLevelForRoom(string sceneName)
        {
            return LoadLevelForRoom(sceneName, isMatchStart: false);
        }

        private bool LoadLevelForRoom(string sceneName, bool isMatchStart)
        {
            if (!TryResolveScene(sceneName, out SceneRef sceneRef))
            {
                return RejectSceneLoad(
                    $"'{sceneName}' 매치 씬이 Build Settings에 등록되지 않았습니다. " +
                    "등록한 뒤 File > Save Project를 실행하세요.");
            }

            if (runner == null || !runner.IsRunning)
            {
                return RejectSceneLoad("방에 연결된 뒤에만 매치 씬을 전환할 수 있습니다.");
            }

            // Fusion 문서: LoadScene / UnloadScene 은 Server/Host(= 씬 권한자)만 호출할 수 있다.
            if (!runner.IsSceneAuthority)
            {
                return RejectSceneLoad("Host만 매치 씬을 전환할 수 있습니다.");
            }

            try
            {
                // Fusion 콜백을 기다리면 이미 Lobby가 내려간 뒤라 첫 빈 프레임이 노출될 수 있다.
                // 씬을 실제로 요청하기 직전에 로컬 커튼부터 올린다. Client는 OnSceneLoadStart가 담당한다.
                EventBus.Publish(new NetworkSceneTransitionStarted(isMatchStart));
                runner.LoadScene(sceneRef, LoadSceneMode.Single);
                LastSceneLoadFailureReason = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                EventBus.Publish(new NetworkSceneTransitionCancelled());
                Debug.LogException(exception);
                return RejectSceneLoad("매치 씬 전환 요청을 시작하지 못했습니다. 다시 시도하세요.");
            }
        }

        private bool RejectSceneLoad(string reason)
        {
            LastSceneLoadFailureReason = reason;
            Debug.LogError($"[NetworkManager] {reason}");
            return false;
        }

        /// <summary>
        /// Host가 확정한 코어루프 스냅샷을 현재 방의 모든 Client에게 신뢰 채널로 보낸다.
        /// Fusion 타입과 직렬화는 Network 경계 안에만 남긴다.
        /// </summary>
        public void BroadcastOnlineCoreLoopSnapshot(in OnlineCoreLoopSnapshot snapshot)
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer)
            {
                Debug.LogWarning("[NetworkManager] Host 세션이 아니어서 코어루프 스냅샷을 보낼 수 없습니다.");
                return;
            }

            onlineCoreLoopSequence = onlineCoreLoopSequence == int.MaxValue
                ? 1
                : onlineCoreLoopSequence + 1;
            byte[] payload = OnlineCoreLoopProtocol.Serialize(snapshot);
            ReliableKey key = ReliableKey.FromInts(
                OnlineCoreLoopProtocol.Magic,
                onlineCoreLoopSequence,
                0,
                0);

            foreach (PlayerRef player in runner.ActivePlayers)
            {
                if (player == runner.LocalPlayer)
                {
                    continue;
                }

                ReliableSendCount++;
                runner.SendReliableDataToPlayer(player, key, payload);
            }
        }

        /// <summary>내 로컬 맵·전투원 준비율을 Host에 보낸다. Host 자신은 같은 이벤트로 직접 반영한다.</summary>
        public void SendMatchLoadingProgress(byte percent)
        {
            if (runner == null || !runner.IsRunning) return;
            percent = percent > 100 ? (byte)100 : percent;
            if (runner.IsServer)
            {
                EventBus.Publish(new MatchLoadingProgressReceived(runner.LocalPlayer.PlayerId, percent));
                return;
            }

            matchLoadingSequence = matchLoadingSequence == int.MaxValue ? 1 : matchLoadingSequence + 1;
            runner.SendReliableDataToServer(
                ReliableKey.FromInts(MatchLoadingProgressProtocol.Magic, matchLoadingSequence, 0, 0),
                new[] { MatchLoadingProgressProtocol.Version, percent });
        }

        /// <summary>
        /// 이번 라운드 전투 맵에 내 전투원이 섰다고 Host 에 알린다(S15P21A606-326 · S-GD-C66RN8). Host 자신은 바로 반영한다.
        /// </summary>
        public void SendCombatEntryReady(int round)
        {
            if (runner == null || !runner.IsRunning || round <= 0 || round > byte.MaxValue) return;
            if (runner.IsServer)
            {
                if (runner.IsPlayer) OnlineCoreLoopCoordinator.HostReceiveCombatEntryReady(runner.LocalPlayer.PlayerId, round);
                return;
            }

            combatEntryReadySequence = combatEntryReadySequence == int.MaxValue ? 1 : combatEntryReadySequence + 1;
            runner.SendReliableDataToServer(
                ReliableKey.FromInts(CombatEntryReadyProtocol.Magic, combatEntryReadySequence, 0, 0),
                new[] { CombatEntryReadyProtocol.Version, (byte)round });
        }

        /// <summary>
        /// Host 가 확정한 경제·축복 상태를 방의 모든 Client 에게 보낸다 (S15P21A606-26).
        /// </summary>
        /// <remarks>
        /// <b>전원분을 전원에게 보낸다.</b> 관전 때 남의 골드·후보를 그려야 하고
        /// (규약 §7), 받는 쪽이 "내 것인가" 를 자기 <c>PlayerId</c> 로 판정하므로
        /// 수신자마다 따로 만들 필요가 없다 — 한 번 직렬화해서 그대로 돌린다.
        /// </remarks>
        public void BroadcastEconomyState(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
            {
                return;
            }

            if (runner == null || !runner.IsRunning || !runner.IsServer)
            {
                Debug.LogWarning("[NetworkManager] Host 세션이 아니어서 경제 상태를 보낼 수 없습니다.");
                return;
            }

            economyStateSequence = economyStateSequence == int.MaxValue ? 1 : economyStateSequence + 1;
            LastBroadcastEconomySequence = economyStateSequence;
            ReliableKey key = ReliableKey.FromInts(EconomyStateProtocol.Magic, economyStateSequence, 0, 0);

            foreach (PlayerRef player in runner.ActivePlayers)
            {
                if (player == runner.LocalPlayer)
                {
                    continue;
                }

                ReliableSendCount++;
                runner.SendReliableDataToPlayer(player, key, payload);
            }
        }

        /// <summary>
        /// Client 가 준비 단계 조작 요청을 Host 에게 올린다 (S15P21A606-26).
        /// </summary>
        /// <remarks>
        /// <b>Host 는 이 함수를 쓰지 않는다</b> — 자기 요청은 네트워크를 거치지 않고 바로 처리한다.
        /// 굳이 자기 자신에게 보내면 왕복이 생겨 Host 만 반응이 느려진다.
        /// </remarks>
        public void SendEconomyCommandToHost(byte[] payload)
        {
            if (payload == null || payload.Length == 0)
            {
                return;
            }

            if (runner == null || !runner.IsRunning || runner.IsServer)
            {
                Debug.LogWarning("[NetworkManager] Client 세션이 아니어서 경제 명령을 보낼 수 없습니다.");
                return;
            }

            economyCommandSequence = economyCommandSequence == int.MaxValue ? 1 : economyCommandSequence + 1;
            ReliableKey key = ReliableKey.FromInts(EconomyCommandProtocol.Magic, economyCommandSequence, 0, 0);

            ReliableSendCount++;
            runner.SendReliableDataToServer(key, payload);
        }

        /// <summary>
        /// Host 가 전투원 하나의 효과 줄을 <b>그 전투원의 주인 한 명에게만</b> 보낸다 (S15P21A606-300 · D-ONL-S62W2S).
        /// </summary>
        /// <remarks>
        /// <para>
        /// 경제 상태(<see cref="BroadcastEconomyState"/>)와 달리 <b>전원에게 돌리지 않는다</b> — 효과 칸은 주인만 그리고
        /// (S-UX-3ESB5T "남의 효과를 내 효과 칸에 띄우지 않는다"), 관전 화면도 이 줄을 쓰지 않는다.
        /// </para>
        /// <para>
        /// Host 자신은 이 통로를 쓰지 않는다 — 신뢰 데이터는 자기 자신에게 오지 않고, Host 는 자기 효과를 직접 만든다.
        /// </para>
        /// </remarks>
        public void SendOwnerHudEffects(PlayerRef owner, byte[] payload)
        {
            if (payload == null || payload.Length == 0) return;
            if (runner == null || !runner.IsRunning || !runner.IsServer) return;
            if (!owner.IsRealPlayer || owner == runner.LocalPlayer) return;

            ownerHudEffectSequence = ownerHudEffectSequence == int.MaxValue ? 1 : ownerHudEffectSequence + 1;
            ReliableKey key = ReliableKey.FromInts(HudEffectReplication.Magic, ownerHudEffectSequence, 0, 0);

            ReliableSendCount++;
            runner.SendReliableDataToPlayer(owner, key, payload);
        }

        // ─────────────────────────────────────────────────────────────
        // 세션 시작 (내부)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 이 방을 참가 가능 상태로 둘지 바꾼다. <b>Host 만 통한다.</b> (S15P21A606-55)
        /// </summary>
        /// <remarks>
        /// <para>
        /// 매치가 시작되면 닫아서 진행 중인 방에 새 사람이 들어오지 못하게 하고, 대기실로 돌아오면
        /// 다시 연다. 닫힌 방은 <see cref="OnSessionListUpdated"/> 에서 "참가 불가" 로 판정된다.
        /// </para>
        /// <para>
        /// ⚠️ 목록에서 <b>사라지지는 않는다.</b> 숨기는 것은 <c>IsVisible</c> 이고 이건 참가 허용 여부다.
        /// 진행 중인 방이 목록에 보이되 들어갈 수 없는 상태가 의도한 결과다.
        /// </para>
        /// </remarks>
        public void SetRoomOpen(bool isOpen)
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer)
            {
                return;
            }

            if (runner.SessionInfo == null || !runner.SessionInfo.IsValid)
            {
                // 세션 스냅샷이 서버 확인을 기다리는 중이면 쓸 수 없다. 조용히 넘기지 않는다 —
                // 매치 시작 시 방을 닫는 경로라, 실패하면 진행 중인 방에 사람이 들어온다.
                Debug.LogWarning(
                    $"[NetworkManager] 세션이 서버 확인 대기 중이라 참가 허용을 바꾸지 못했습니다: {isOpen}");
                return;
            }

            // 쓰기 전에 정본을 확보한다. 여기서는 스냅샷이 유효함이 위 가드로 보장되므로,
            // 아직 비어 있다면 지금이 채울 수 있는 유일한 시점이다.
            SessionInfo session = runner.SessionInfo;
            hostRoomSettings.CaptureOnce(SessionPeerCapacity(session), session.IsVisible, session.IsOpen);

            session.IsOpen = isOpen;

            // 세션에 쓴 값을 정본에도 남긴다. 이 쓰기 역시 왕복이라, 바로 뒤의 발행이 스냅샷을
            // 못 읽고 정본을 쓰게 되는데 그때 옛 값이 나가면 안 된다.
            hostRoomSettings.SetOpen(isOpen);

            Debug.Log($"[NetworkManager] 방 참가 허용 = {isOpen}");
            BroadcastRoomSettings();
        }

        /// <summary>
        /// 지금 이 방의 설정을 하나로 해석한다. 방에 없으면 <c>false</c>. (S15P21A606-55)
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>왜 <c>SessionInfo</c> 를 직접 읽지 않는가.</b> <c>SessionInfo.IsValid</c> 는 서버로 보낸
        /// 변경이 확인되기를 기다리는 동안 <c>false</c> 로 내려간다. 그런데 설정을 알려야 하는
        /// 순간은 정확히 그때다. 스냅샷이 유효한 동안에 Host 정본을 채워두고, 대기 중에는 그
        /// 정본으로 답한다 (<see cref="HostRoomSettings"/> 주석).
        /// </para>
        /// <para>
        /// ⚠️ <b>부수효과가 있다</b> — Host 라면 이 호출이 정본을 갱신한다. 이름이 <c>Try</c> 인데
        /// 상태를 바꾸는 것은 어색하지만, 읽는 모든 자리에서 갱신 타이밍을 각자 챙기는 것보다
        /// 한 곳에 모으는 편이 틀릴 여지가 적다.
        /// </para>
        /// <para>
        /// Client 는 정본을 두지 않는다. 참가자의 <c>Properties</c> 는 서버가 준 사본이라 이미
        /// 정확하고, 참가자는 설정을 바꾸지 못하므로 기다릴 왕복도 없다.
        /// </para>
        /// </remarks>
        private bool TryResolveRoomSettings(out RoomSettingsChanged settings)
        {
            settings = default;

            if (runner == null || !runner.IsRunning)
            {
                return false;
            }

            SessionInfo session = runner.SessionInfo;
            bool isSnapshotReady = session != null && session.IsValid;

            if (!runner.IsServer)
            {
                if (!isSnapshotReady)
                {
                    return false;
                }

                settings = new RoomSettingsChanged(
                    ReadRoomTitle(session),
                    SessionPeerCapacity(session),
                    session.IsVisible,
                    session.IsOpen);
                return true;
            }

            if (isSnapshotReady)
            {
                // 처음 한 번만 담긴다 — 되읽기가 방금 쓴 의도를 덮지 않게 하는 것이 규칙이다.
                hostRoomSettings.CaptureOnce(SessionPeerCapacity(session), session.IsVisible, session.IsOpen);
            }

            // 제목이 아직 없으면 방 코드로 대신한다. 스냅샷을 못 읽는 동안에는 그것조차 없다.
            return hostRoomSettings.TryBuild(
                isSnapshotReady ? ReadRoomTitle(session) : null,
                out settings);
        }

        /// <summary>
        /// 방 설정을 방 전원에게 알린다. <b>Host 만 보낸다.</b> (S15P21A606-55 · -37)
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>왜 감지가 아니라 통지인가.</b> Fusion 에는 방 안에서 세션 속성 변경을 알려주는 콜백이 없다
        /// (<c>OnSessionListUpdated</c> 는 로비 목록 전용이다). 그렇다고 <c>SessionInfo</c> 를 주기적으로
        /// 훑는 것은 <b>값이 언제 바뀌는지 이미 아는 쪽이 있는데도 모두가 계속 확인하는</b> 낭비다 —
        /// 바꾸는 것은 언제나 Host 의 이 클래스이므로, 바꾼 그 자리에서 알리면 된다.
        /// </para>
        /// <para>
        /// 전송은 <see cref="BroadcastOnlineCoreLoopSnapshot"/> 과 같은 신뢰 데이터 채널을 쓴다.
        /// 이미 있는 경로를 재사용하는 것이라 새 프리팹도 <c>NetworkObject</c> 도 필요 없다.
        /// </para>
        /// <para>
        /// ⚠️ <b>Host 자신도 여기서 발행한다.</b> 보내는 쪽은 자기에게 보내지 않으므로,
        /// 이 줄이 없으면 Host 화면만 옛 값을 계속 보여준다.
        /// </para>
        /// </remarks>
        private void BroadcastRoomSettings()
        {
            // ⚠️ 여기서 SessionInfo.IsValid 를 요구하지 않는다. 요구하면 이 메서드를 부르는 가장
            //    중요한 순간 — 방금 설정을 바꾼 직후 — 에 항상 컷된다. 대신 해석을 한 곳에 모았다.
            if (!TryResolveRoomSettings(out RoomSettingsChanged settings))
            {
                return;
            }

            // Host 자신의 화면부터 갱신한다.
            EventBus.Publish(settings);

            if (!runner.IsServer)
            {
                return;
            }

            roomSettingsSequence = roomSettingsSequence == int.MaxValue ? 1 : roomSettingsSequence + 1;

            byte[] payload = RoomSettingsProtocol.Serialize(settings);
            ReliableKey key = ReliableKey.FromInts(RoomSettingsProtocol.Magic, roomSettingsSequence, 0, 0);

            foreach (PlayerRef player in runner.ActivePlayers)
            {
                if (player == runner.LocalPlayer)
                {
                    continue;
                }

                ReliableSendCount++;
                runner.SendReliableDataToPlayer(player, key, payload);
            }
        }

        /// <summary>방 제목을 담는 세션 속성 키 (S15P21A606-55).</summary>
        /// <remarks>
        /// 세션 이름은 방 코드라 사람이 읽기 어렵다. 목록에 띄울 이름은 여기 따로 싣는다.
        /// </remarks>
        private const string RoomTitleProperty = "title";

        /// <summary>방을 만든 Host 의 닉네임을 담는 세션 속성 키 (S15P21A606-55 · -37).</summary>
        /// <remarks>
        /// <para>
        /// 목록에 "누구 방인가" 를 띄우려면 <b>목록 단계에서</b> Host 를 알아야 한다. 방에 들어가야
        /// 알 수 있는 <see cref="PlayerInfo"/> 로는 늦다 — 들어가기 전에 고르는 정보이기 때문이다.
        /// </para>
        /// <para>
        /// ⚠️ 방을 만든 시점의 닉네임이다. 이후 Host 가 닉네임을 바꿔도 여기는 따라가지 않는다.
        /// 필요해지면 <see cref="UpdateRoomProperties"/> 로 갱신한다.
        /// </para>
        /// </remarks>
        private const string RoomHostProperty = "host";

        /// <summary>
        /// 이 방이 비밀번호로 잠겼는지만 담는 세션 속성 키. 값은 <b>0 또는 1</b> 이다 (S-UX-BXXTHX).
        /// </summary>
        /// <remarks>
        /// <para>
        /// 목록에 자물쇠를 그리고 랜덤 참가 후보에서 빼려면 <b>들어가기 전에</b> 알아야 한다.
        /// 그래서 이 한 비트만 공개한다.
        /// </para>
        /// <para>
        /// ⚠️ <b>여기에 비밀번호나 검증값을 싣지 않는다.</b> 세션 속성은 로비 목록을 받는 모든
        /// 피어가 읽는다 — 검증값을 실으면 그것을 그대로 되보내 입장할 수 있다.
        /// 심사에 필요한 것은 Host 메모리에만 있다(<see cref="LobbyRoomAdmission"/>).
        /// </para>
        /// </remarks>
        private const string RoomLockedProperty = LobbyRoomAdmission.LockedPropertyKey;

        /// <summary>방 생성 시 실을 세션 속성. 제목이 비면 방 코드를 그대로 쓴다.</summary>
        /// <remarks>
        /// <para>
        /// ⚠️ <b>키는 값이 비어도 반드시 선언한다.</b> Fusion 은 방을 만든 뒤에 <b>없는 키를
        /// 추가하지 못한다</b> — 시도하면 요청이 조용히 거부된다.
        /// </para>
        /// <code>
        /// [Fusion] Invalid custom property key [title], ignore.
        ///          Only existing custom properties can be updated.
        /// </code>
        /// <para>
        /// 2026-09-02 실측으로 잡았다(진단 창 <c>RoomSettingsDiagnosticsWindow</c>). 그전에는
        /// 제목을 조건부로만 실었기 때문에 <b>방 이름과 제목을 둘 다 비우고 만든 방은 이후 제목을
        /// 영원히 바꿀 수 없었다.</b> 자동 이름으로 만들면 항상 그 상태가 된다.
        /// </para>
        /// <para>
        /// 빈 값은 표시에 문제가 없다 — <see cref="ReadRoomTitle"/> 가 방 코드로 대신한다.
        /// </para>
        /// </remarks>
        private string ResolveRoomHostDisplayName()
        {
            string name = DedicatedServerRuntime.IsActive ? dedicatedOwnerNickname : nickName;
            return string.IsNullOrWhiteSpace(name) ? string.Empty : name;
        }

        private Dictionary<string, SessionProperty> BuildSessionProperties(
            string sessionName,
            string title,
            bool training = false,
            string password = null,
            bool dedicatedServer = false)
        {
            string resolvedTitle = string.IsNullOrWhiteSpace(title) ? sessionName : title;

            var properties = new Dictionary<string, SessionProperty>
            {
                // 전용 서버 방은 "server" — 목록·대기실이 서버 자리를 빼고 셀 근거다(SessionPeerCapacity).
                //   훈련 판정(IsTrainingRoom)은 "training" 만 보므로 영향이 없다.
                [RoomModeProperty] = training ? "training" : dedicatedServer ? RoomModeServer : "match",
                // 나중에 갱신할 키는 지금 전부 만들어 둔다 (위 주석).
                [RoomTitleProperty] = string.IsNullOrWhiteSpace(resolvedTitle)
                    ? string.Empty
                    : resolvedTitle.Trim(),

                // 목록에서 "누구 방인가" 를 보려면 들어가기 전에 알아야 한다.
                // 전용 서버는 자기 닉네임이 없으므로 방을 배정받은 방장 이름을 싣는다 (S15P21A606-321).
                [RoomHostProperty] = ResolveRoomHostDisplayName(),

                // ⚠️ **잠겼는지만** 싣는다 — 0 또는 1 이다 (S-UX-BXXTHX).
                //    세션 속성은 방에 들어오지 않은 사람도 읽는다. 비밀번호 원문이나 검증값을
                //    여기 실으면 목록을 받는 모든 피어에게 그대로 나눠주는 것과 같다.
                //    잠기지 않은 방도 키를 선언한다 — 만든 뒤에는 없는 키를 추가할 수 없다(D-071).
                [RoomLockedProperty] = LobbyRoomAdmission.LockedPropertyValue(password),
            };

            // 방을 만드는 순간부터 Host 의 정본을 채운다. 이걸 빠뜨리면 입장 직후의
            // 첫 BroadcastRoomSettings 가 아직 왕복이 끝나지 않은 Properties 를 읽는다.
            // 빈 제목은 SetTitle 이 무시한다 — 그때는 방 코드가 대신 나간다.
            hostRoomSettings.SetTitle(resolvedTitle);

            return properties;
        }

        /// <summary>
        /// 방 설정을 <b>매치 중에</b> 바꾼다. <b>Host 만 통한다.</b> (S15P21A606-55)
        /// </summary>
        /// <remarks>
        /// <para>
        /// 방을 만든 뒤에 제목이나 공개 여부를 바꾸는 경로다. Photon 이 속성을 방 전체에 복제하므로
        /// 별도 RPC 가 필요 없고, 다른 피어의 목록도 다음 갱신에서 새 값을 받는다.
        /// </para>
        /// <para>
        /// ⚠️ 넘긴 값만 바꾼다. <c>null</c> 인 인자는 건드리지 않는다 — 제목만 바꾸려다 공개 여부가
        /// 함께 초기화되는 사고를 막기 위해서다.
        /// </para>
        /// <para>
        /// <b>멱등이다.</b> 이미 그 값이면 서버로 보내지 않고 현재 상태만 다시 알린다 — Fusion 이
        /// 바꿀 것 없는 요청을 거부하기 때문이다(D-071). 그래서 같은 값으로 두 번 눌러도
        /// 에러가 아니라 같은 결과가 나온다.
        /// </para>
        /// <para>
        /// 정원은 여기서 바꿀 수 없다. Photon 의 <c>MaxPlayers</c> 는 세션 생성 인자이지 속성이 아니다.
        /// </para>
        /// </remarks>
        public void UpdateRoomProperties(string title = null, bool? isPublic = null)
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer)
            {
                Debug.LogWarning("[NetworkManager] Host 만 방 설정을 바꿀 수 있습니다.");
                return;
            }

            if (runner.SessionInfo == null || !runner.SessionInfo.IsValid)
            {
                // ⚠️ 직전 변경의 서버 확인을 기다리는 중이면 여기서 걸린다. 조용히 반환하면
                //    사용자에게는 "눌렀는데 아무 일도 없었다" 로 보인다. 화면에 알릴 창구가 아직
                //    없어 로그로 남긴다 (후속: 요청 중 버튼 비활성 — S-UX-APFPWN 과 같은 규칙).
                Debug.LogWarning("[NetworkManager] 세션이 서버 확인 대기 중이라 방 설정을 바꾸지 못했습니다.");
                return;
            }

            // 쓰기 전에 정본을 확보한다 (SetRoomOpen 주석). 이걸 빠뜨리면 정본이 빈 채로
            // 제목만 바뀌어, 뒤따르는 발행이 "확정된 값이 없다" 로 판단해 통째로 건너뛴다.
            SessionInfo session = runner.SessionInfo;
            hostRoomSettings.CaptureOnce(SessionPeerCapacity(session), session.IsVisible, session.IsOpen);

            // ⚠️ 이미 그 값이면 보내지 않는다. Fusion 은 바꿀 것이 없는 요청을 거부하므로
            //    (2026-09-02 실측: 같은 제목을 두 번 보내면 두 번째가 false), 그대로 보내면
            //    "이미 그 상태" 가 에러 로그로 남아 <b>진짜 거부와 구분되지 않는다.</b>
            //    보내지 않고 아래에서 현재 상태만 다시 알린다 — 결과적으로 멱등이 된다.
            if (!string.IsNullOrWhiteSpace(title) && title.Trim() != hostRoomSettings.Title)
            {
                string trimmed = title.Trim();

                // ⚠️ 반환값을 버리지 않는다. 거부와 지연을 구분할 방법이 이것뿐이다.
                if (!runner.SessionInfo.UpdateCustomProperties(
                        new Dictionary<string, SessionProperty> { [RoomTitleProperty] = trimmed }))
                {
                    // ⚠️ 속성을 함께 남긴다. 남은 거부 원인은 "그 키가 없다" 이고(D-071),
                    //    키가 없는 것과 값이 빈 것은 화면에서 구분되지 않는다.
                    Debug.LogError(
                        $"[NetworkManager] 방 제목 변경 요청이 거부됐습니다: {trimmed}"
                        + $" · 현재 세션 속성 {DescribeRoomProperties()}");
                    return;
                }

                // 서버 반영을 기다리지 않고 여기서 확정한다. 되읽으면 옛 값이 나온다
                // (HostRoomSettings 주석).
                hostRoomSettings.SetTitle(trimmed);
                Debug.Log($"[NetworkManager] 방 제목 변경: {trimmed}");
            }

            if (isPublic.HasValue && isPublic.Value != hostRoomSettings.IsPublic)
            {
                runner.SessionInfo.IsVisible = isPublic.Value;

                // 제목과 같은 이유로 정본에도 남긴다 (SetRoomOpen 주석).
                hostRoomSettings.SetPublic(isPublic.Value);

                Debug.Log($"[NetworkManager] 방 공개 여부 변경: {isPublic.Value}");
            }

            BroadcastRoomSettings();
        }

        /// <summary>
        /// 지금 방의 세션 속성을 사람이 읽을 한 줄로 만든다. <b>진단용이다.</b>
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>왜 필요한가.</b> Fusion 은 방을 만든 뒤 없는 키를 추가하지 못한다(D-071). 그래서
        /// <b>"키가 없다" 와 "키는 있고 값이 비었다" 는 완전히 다른 상태인데</b>, 화면에는 둘 다
        /// 제목이 방 코드로 보여서 구분되지 않는다. 갱신이 거부될 때 어느 쪽인지 보려면
        /// 속성 자체를 봐야 한다.
        /// </para>
        /// <para>
        /// Fusion 타입은 넘기지 않는다 — 경계에서 문자열로 바꾼다(D-009).
        /// </para>
        /// </remarks>
        public string DescribeRoomProperties()
        {
            if (runner == null || runner.SessionInfo == null || !runner.SessionInfo.IsValid)
            {
                return "(세션 스냅샷을 읽을 수 없음 — 서버 확인 대기 중이거나 방 밖)";
            }

            if (runner.SessionInfo.Properties == null || runner.SessionInfo.Properties.Count == 0)
            {
                return "(속성 없음)";
            }

            var builder = new StringBuilder();
            foreach (var pair in runner.SessionInfo.Properties)
            {
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(pair.Key).Append('=');

                if (pair.Value.IsString)
                {
                    string value = pair.Value;
                    builder.Append('"').Append(value).Append('"');
                }
                else
                {
                    builder.Append("(문자열 아님)");
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// 세션 속성에서 방 제목을 꺼낸다. 없으면 세션 이름으로 대체한다.
        /// </summary>
        /// <remarks>
        /// ⚠️ 제목이 없는 방도 목록에 떠야 한다 — 다른 도구로 만든 방이나 예전 빌드의 방이
        /// 이름 없이 보이면 사용자는 목록이 깨진 줄 안다.
        /// </remarks>
        private static string ReadRoomTitle(SessionInfo session)
        {
            if (session.Properties != null
                && session.Properties.TryGetValue(RoomTitleProperty, out SessionProperty property)
                && property.IsString)
            {
                string value = property;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return session.Name;
        }

        /// <summary>세션 속성에서 Host 닉네임을 꺼낸다. 없으면 빈 문자열.</summary>
        /// <remarks>
        /// 빈 문자열을 주는 이유 — 없는 것을 "알 수 없음" 같은 문구로 채우면 그 문구가 UI 마다
        /// 달라진다. 표시 방식은 화면이 정한다(S15P21A606-37).
        /// </remarks>
        /// <summary>
        /// 세션 속성에서 "잠긴 방인가" 를 읽는다. 키가 없으면 잠기지 않은 것으로 본다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠️ <b>없는 키를 잠김으로 읽지 않는다.</b> 이 키가 생기기 전에 만들어진 방과 다른
        /// 도구로 만든 방에는 키가 없다. 그것을 잠김으로 보면 <b>멀쩡한 공개 방이 전부</b>
        /// 랜덤 참가 후보에서 빠지고 목록에 자물쇠가 달린다.
        /// </para>
        /// <para>
        /// 정수로 실었지만 문자열로 들어온 값도 받아 준다 — 값의 뜻은 같고, 여기서 거부하면
        /// 그 방이 조용히 공개 방으로 보인다.
        /// </para>
        /// </remarks>
        private static bool ReadRoomLocked(SessionInfo session)
        {
            if (session.Properties == null
                || !session.Properties.TryGetValue(RoomLockedProperty, out SessionProperty property))
            {
                return false;
            }

            if (property.IsInt)
            {
                int value = property;
                return value != 0;
            }

            if (property.IsString)
            {
                string value = property;
                return value == "1";
            }

            return false;
        }

        private static string ReadRoomHost(SessionInfo session)
        {
            if (session.Properties != null
                && session.Properties.TryGetValue(RoomHostProperty, out SessionProperty property)
                && property.IsString)
            {
                string value = property;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        /// <param name="password">
        /// 선택적 방 비밀번호 (S-UX-BXXTHX). <b>마지막 선택 인자라 기존 호출부는 잠기지 않은 방</b>
        /// 그대로다 — 훈련장도 여기에 포함된다.
        /// <para>
        /// Host 는 이것으로 심사값을 만들고(메모리 한정), Client 는 파생값만
        /// <c>ConnectionToken</c> 에 실어 보낸다. 원문은 세션 속성에도 로그에도 남지 않는다.
        /// </para>
        /// </param>
        private async Task StartSession(
            GameMode mode,
            string sessionName,
            bool isPublic = true,
            string title = null,
            int? maxPlayers = null,
            bool training = false,
            string password = null)
        {
            if (runner != null && runner.IsRunning)
            {
                Debug.LogWarning("[NetworkManager] 이미 세션에 있습니다. 먼저 LeaveRoom() 하세요.");
                return;
            }

            if (isStartingSession)
            {
                Debug.LogWarning("[NetworkManager] 세션 시작 요청이 이미 진행 중입니다.");
                return;
            }

            // ⚠️ 공개 여부·속성은 **방을 만들 때만** 넘긴다.
            //    참가할 때 SessionProperties 를 넘기면 Fusion 이 그것을 매치메이킹 **필터**로
            //    해석해서, 속성이 맞지 않는 방에는 들어가지 못한다.
            // 전용 서버(GameMode.Server)도 방을 만드는 쪽이다 (S15P21A606-321).
            bool isCreating = mode == GameMode.Host || mode == GameMode.Server;
            bool wantsPassword = LobbyRoomAdmission.RequiresPassword(password);
            string resolvedName = string.IsNullOrWhiteSpace(sessionName) ? null : sessionName.Trim();

            // ⚠️ 자르지 않는다. 잘린 비밀번호로 방이 열리면 **방장이 알려준 비밀번호로는
            //    아무도 못 들어온다.** 규칙은 도메인(LobbyRoomRules)과 같은 것 하나뿐이고,
            //    문구는 규칙만 말한다 — 입력값을 되비추지 않는다.
            if (wantsPassword && !LobbyRoomAdmission.IsAcceptablePassword(password))
            {
                Debug.LogWarning("[NetworkManager] 방 비밀번호가 입력 규칙에 맞지 않아 요청을 중단했습니다.");
                EventBus.Publish(new RoomJoinFailed(
                    $"비밀번호는 {LobbyRoomAdmission.MaxPasswordLength}자 이하로, 특수 제어 문자 없이 입력해 주세요."));
                return;
            }

            // 잠긴 방은 방 코드가 곧 심사값의 일부다. 이름을 Photon 이 지어 주면 참가자가
            // 같은 값을 만들 수 없어 **아무도 못 들어오는 방**이 된다.
            if (isCreating && wantsPassword && resolvedName == null)
            {
                Debug.LogError("[NetworkManager] 비밀번호가 있는 방은 방 코드가 필요합니다.");
                EventBus.Publish(new RoomJoinFailed("비밀번호가 있는 방은 방 코드를 입력해 주세요."));
                return;
            }

            isStartingSession = true;
            try
            {
                // ── 심사값 준비 ──
                // ⚠️ Runner 를 만들기 **전에** 한다. 여기서 실패하면 세션을 시작하지 않으므로,
                //    먼저 만들어 두면 쓰이지 않는 Runner 오브젝트가 남는다.
                // Host: 이 방에서만 쓰는 소금 + 해시를 메모리에 둔다. 이전 방의 값이 남아 있으면
                //       공개 방이 옛 비밀번호로 심사되므로 잠그지 않는 경우에도 반드시 비운다.
                // Client: 보낼 파생값을 만든다. 비밀번호가 없으면 null 이라 아무것도 실리지 않는다.
                byte[] connectionToken = null;
                if (isCreating)
                {
                    roomAdmission.Clear();

                    if (wantsPassword && !roomAdmission.Lock(resolvedName, password))
                    {
                        Debug.LogError("[NetworkManager] 방 비밀번호를 설정하지 못해 방을 만들지 않았습니다.");
                        EventBus.Publish(new RoomJoinFailed("방 비밀번호를 설정하지 못했습니다. 다시 시도해 주세요."));
                        return;
                    }
                }
                else if (wantsPassword)
                {
                    connectionToken = LobbyRoomAdmission.BuildJoinToken(resolvedName, password);
                    if (connectionToken == null)
                    {
                        Debug.LogWarning("[NetworkManager] 비밀번호를 보낼 방 코드가 없습니다.");
                        EventBus.Publish(new RoomJoinFailed("방 코드를 입력해 주세요."));
                        return;
                    }
                }

                EnsureRunner();

                var args = new StartGameArgs
                {
                    GameMode = mode,
                    SessionName = resolvedName,
                    // 훈련장 피어 정원은 기존 8명 유지. 온라인 매치만 임시 운영 정원 적용.
                    PlayerCount = training ? 8 : LobbyRoomRules.ResolveCapacity(maxPlayers),
                    SceneManager = EnsureRunnerServices(),

                    // 만들 때는 항상 열어 둔다. 매치가 시작되면 SetRoomOpen(false) 로 닫는다.
                    IsOpen = true,
                    IsVisible = !isCreating || isPublic,
                    SessionProperties = isCreating
                        ? BuildSessionProperties(sessionName, title, training, password, mode == GameMode.Server)
                        : null,

                    // ⚠️ 고정 길이(LobbyRoomAdmission.TokenBytes)의 파생값만 실린다. 비밀번호 원문은
                    //    회선을 넘지 않는다. 잠기지 않은 방에서는 null 이라 예전과 동일하다.
                    //    여기 담긴 배열은 지우지 않는다 — Fusion 이 재연결에서 다시 쓸 수 있고,
                    //    지운 배열을 되보내면 정상 참가자가 거부당한다.
                    ConnectionToken = connectionToken,

                    // Scene 을 지정하지 않으면 현재 씬을 유지한 채 세션만 연다.
                    // 로비(02_Lobby)에서 방을 만들고, 인게임 전환은 LoadLevelForRoom 이 따로 한다.

                    // 호환되지 않는 빌드가 같은 방에 들어오지 못하게 한다 (S-ONL-RBCJKD).
                    CustomPhotonAppSettings = BuildCompatibilityAppSettings(),
                };

                // ⚠️ 비밀번호·토큰은 로그에 남기지 않는다. 잠금 여부만 남긴다.
                Debug.Log(
                    $"[NetworkManager] 세션 시작 시도: mode={mode}, name={args.SessionName ?? "(자동)"}, "
                    + $"잠금={(wantsPassword ? "예" : "아니오")}, 빌드={BuildVersionIdentity.Current}");

                var result = await runner.StartGame(args);

                if (!result.Ok)
                {
                    Debug.LogError($"[NetworkManager] 세션 시작 실패: {result.ShutdownReason}");
                    // Fusion Runner는 실패하거나 종료된 뒤 재사용할 수 없다. 특히 Client 자동 접속
                    // 재시도에서 이 인스턴스를 남겨두면 이후 모든 시도가 같은 Runner에서 연쇄 실패한다.
                    //
                    // ⚠️ DisposeRunner 가 심사값도 함께 비운다. 실패한 방의 검증값을 들고 있으면
                    //    다음에 만든 공개 방이 그 비밀번호로 심사된다.
                    DisposeRunner();
                    EventBus.Publish(new RoomJoinFailed(DescribeShutdownReason(result.ShutdownReason)));
                    return;
                }

                sessionJoined = true;
                // 전용 서버 방에 클라이언트로 들어왔는가 — 서버가 사라졌을 때 "방장이 나갔다" 가 아니라
                // "서버 오류" 로 알리기 위한 것이다 (S-GD-JCN6CM). 훈련장은 Host 모드라 해당하지 않는다.
                joinedDedicatedServerRoom = mode == GameMode.Client && !training && OnlineHostingMode.UsesDedicatedServer
                    && !IsTrainingRoom;
                EventBus.Publish(new NetworkSessionStateChanged(isActive: true));
                EventBus.Publish(new RoomJoined(RoomName, runner.IsServer));

                // 방에 들어온 직후 설정 초기값을 알린다. 화면은 RoomSettingsChanged 하나만 구독하면
                // 초기값과 이후 변경을 모두 받는다 (S15P21A606-55 · -37).
                BroadcastRoomSettings();
                PublishPlayers();

                // 세션이 열린 방식을 남긴다. 2PC 실측(S15P21A606-16)의 원자료가 된다.
                StartCoroutine(DiagnoseConnectionRoutine("세션 시작"));
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkManager] 세션 시작 중 예외: {e.Message}");
                DisposeRunner();
                EventBus.Publish(new RoomJoinFailed(SessionStartExceptionMessage));
            }
            finally
            {
                isStartingSession = false;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 연결 진단 — 2PC NAT 실측(S15P21A606-16)의 측정 도구
        //
        // 이것이 없으면 "붙었다/안 붙었다" 만 알 수 있고 **왜** 가 남지 않는다.
        // Direct 로 붙었는지 Relay 로 우회했는지는 교육장 망에서 발표가 되느냐를
        // 가르는 정보라, 세션이 열리는 순간마다 로그로 남긴다.
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 연결 방식을 다시 재는 간격(초).
        /// </summary>
        /// <remarks>
        /// 접속 직후 값은 확정이 아니다. Fusion 은 먼저 펀치스루(Direct)를 시도하고
        /// 실패하면 Relay 로 떨어지므로, 한 번만 찍으면 과도기 값을 진실로 오해한다.
        /// 그래서 잠시 뒤 한 번 더 찍는다. <b>측정용 값이라 게임 스펙이 아니다.</b>
        /// </remarks>
        private const float ConnectionRecheckSeconds = 3f;

        /// <summary>
        /// 지금 연결이 <b>Direct 인지 Relay 인지</b>, 그리고 이 PC 의 NAT 유형을 콘솔에 남긴다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>NAT 유형</b>(<c>OpenInternet</c>/<c>FullCone</c>/<c>Symmetric</c>/<c>UdpBlocked</c>)은
        /// 이 PC 가 P2P 로 뚫릴 수 있는지를 말해준다. <c>Symmetric</c> 이 나오면 상대가 무엇이든
        /// Direct 는 거의 불가능하고 Relay 로만 붙는다 — 교육장 망 판정의 핵심 근거다.
        /// </para>
        /// <para>
        /// ⚠️ <c>GetPlayerConnectionType</c> 은 <b>Server(Host)에서만 유효하다</b>(Fusion 문서).
        /// 그래서 클라이언트는 자기와 서버 사이(<c>CurrentConnectionType</c>)만 알 수 있고,
        /// 누가 Direct 로 붙었는지는 <b>Host 쪽 로그를 봐야 한다.</b> 2PC 실측에서
        /// 양쪽 로그를 다 걷어야 하는 이유다.
        /// </para>
        /// </remarks>
        /// <param name="reason">어느 시점의 측정인지. 로그에 그대로 찍힌다.</param>
        public void LogConnectionDiagnostics(string reason)
        {
            if (runner == null || !runner.IsRunning)
            {
                Debug.LogWarning($"[NetworkManager] 연결 진단({reason}) 생략 — 세션에 있지 않습니다.");
                return;
            }

            var role = runner.IsServer ? "Host" : "Client";

            // GetRttToPhotonCloud 는 (평균 RTT, 최근 RTT) 를 **초** 단위로 준다.
            // 세션에 붙어 있지 않으면 (0, 0).
            //
            // ⚠️ 요약문은 "network latency ... along with the last calculated RTT" 라고 적어
            //    앞 항목이 편도 지연인 것처럼 읽히지만, <returns> 는 명확하다 —
            //    "A tuple with the Average RTT and the Last RTT". 둘 다 RTT 다.
            //    실측에서도 평균 47ms / 최근 13ms 로 나왔다(초기 핸드셰이크가 평균을 끌어올린다).
            //    판단에는 **최근 값**을 쓴다. 평균은 맥락일 뿐이다.
            var (avgRtt, lastRtt) = runner.GetRttToPhotonCloud();

            // 로컬 바인딩 주소를 함께 남긴다. 어느 망에서 측정했는지가 로그 안에 남아야
            // 나중에 "그때 테더링이었나?" 를 Windows 이벤트 로그로 역추적하지 않아도 된다.
            // 2PC 실측(S15P21A606-16)에서 실제로 그 역추적을 해야 했다.
            Debug.Log(
                $"[NetworkManager] 연결 진단({reason}) — 역할={role}, " +
                $"NAT={runner.NATType}, 서버와의 연결={runner.CurrentConnectionType}, " +
                $"클라우드 RTT={ToMillis(lastRtt)} (평균 {ToMillis(avgRtt)}), " +
                $"로컬주소={runner.LocalAddress}, 방={RoomName}");

            if (!runner.IsServer)
            {
                // 클라이언트에서 GetPlayerConnectionType 을 부르면 의미 없는 값이 나온다. 부르지 않는다.
                return;
            }

            foreach (var player in runner.ActivePlayers)
            {
                if (player == runner.LocalPlayer)
                {
                    continue;   // 나 자신과의 연결은 없다
                }

                Debug.Log(
                    $"[NetworkManager]   └ Player {player.PlayerId} — 연결={runner.GetPlayerConnectionType(player)}, " +
                    $"RTT={ToMillis(runner.GetPlayerRtt(player))}");
            }
        }

        /// <summary>
        /// Fusion 이 주는 초 단위 시간을 읽기 쉬운 ms 문자열로 바꾼다.
        /// </summary>
        /// <remarks>
        /// 0 은 "빠르다" 가 아니라 <b>아직 측정되지 않았다</b>는 뜻이다(Fusion 문서: 세션에 붙어
        /// 있지 않으면 0 을 준다). 숫자 0ms 로 찍으면 측정값으로 오해하므로 구분해서 표시한다.
        /// </remarks>
        private static string ToMillis(double seconds)
        {
            return seconds <= 0d ? "(미측정)" : $"{seconds * 1000d:0.0}ms";
        }

        /// <summary>
        /// 화면에 띄울 왕복 지연을 <see cref="NetworkLatencySnapshot"/> 에 올린다 (S-UX-TXGZWZ).
        /// </summary>
        /// <remarks>
        /// <para>
        /// UI 는 <c>Scripts/Network/</c> 를 참조할 수 없다(D-009). 그래서 Fusion 이 주는 <b>초</b> 단위
        /// 값을 여기서 ms 로 번역해 Data 계층 캐시에 올리고, 화면은 그 숫자만 읽는다.
        /// </para>
        /// <para>
        /// <b>최근 값을 쓴다.</b> <see cref="LogConnectionDiagnostics"/> 에 적어 둔 대로 평균 RTT 는
        /// 초기 핸드셰이크에 끌려 올라가 지금 상태를 나타내지 못한다.
        /// </para>
        /// <para>
        /// 세션에 있지 않으면 <b>값을 지우고 지어내지 않는다.</b> 로컬 플레이·훈련장에는 왕복 지연이
        /// 없으므로 화면이 그 자리를 비운다.
        /// </para>
        /// </remarks>
        private void Update()
        {
            PollDedicatedRosterChanges();

            latencyCountdown -= Time.unscaledDeltaTime;
            if (latencyCountdown > 0f) return;

            latencyCountdown = LatencySampleInterval;

            if (runner == null || !runner.IsRunning)
            {
                NetworkLatencySnapshot.Clear();
                return;
            }

            var (_, lastRtt) = runner.GetRttToPhotonCloud();

            // Fusion 은 아직 측정하지 못했을 때도 0 을 준다 — <see cref="ToMillis"/> 가 0 을 "(미측정)"
            // 으로 적는 것과 같은 이유다. 0ms 로 띄우면 "지연 없음" 이라는 거짓말이 된다.
            if (lastRtt <= 0d)
            {
                NetworkLatencySnapshot.Clear();
                return;
            }

            NetworkLatencySnapshot.Set((float)(lastRtt * 1000d));
        }

        /// <summary>
        /// 접속 직후 한 번, <see cref="ConnectionRecheckSeconds"/> 뒤 한 번 더 잰다.
        /// 펀치스루가 늦게 성공/실패하는 것을 놓치지 않기 위해서다.
        /// </summary>
        private IEnumerator DiagnoseConnectionRoutine(string reason)
        {
            LogConnectionDiagnostics(reason);

            yield return new WaitForSeconds(ConnectionRecheckSeconds);

            // 그사이 방을 나갔을 수 있다. LogConnectionDiagnostics 가 스스로 확인한다.
            LogConnectionDiagnostics($"{reason} +{ConnectionRecheckSeconds:0}초");
        }

        // ─────────────────────────────────────────────────────────────
        // Runner 수명 관리
        // ─────────────────────────────────────────────────────────────

        private void EnsureRunner()
        {
            if (runner != null) return;

            // Fusion은 초기화 중 Runner에 DontDestroyOnLoad를 적용하므로 반드시 루트여야 한다.
            // 수명은 부모 계층이 아니라 runner 참조로 추적하고 DisposeRunner에서 명시적으로 정리한다.
            //
            // ⚠️ 다만 그 적용은 **세션 시작(StartGame) 때** 이야기다. JoinSessionLobby 만 한
            // runner 는 DDOL 이 걸리지 않아 **씬이 바뀌면 그대로 파괴**된다 — 2026-09-07 에디터
            // 실측: Bootstrap 에서 로비 접속 → Title 전환 후 runner 가 fake-null, 그런데
            // isConnected 플래그는 남아서 자동 접속이 영원히 건너뛰었다 (S15P21A606-96 의 ②).
            // 그래서 만들 때 우리가 직접 건다. 파괴는 변함없이 DisposeRunner 한 곳이다.
            var go = new GameObject("NetworkRunner");
            DontDestroyOnLoad(go);

            runner = go.AddComponent<NetworkRunner>();
            // 이 피어가 입력을 제공한다. 전용 서버는 조작하는 사람이 없으므로 제공하지 않는다 (S15P21A606-321).
            runner.ProvideInput = !DedicatedServerRuntime.IsActive;
            runner.AddCallbacks(this);
        }

        /// <summary>
        /// 이 빌드가 만날 수 있는 상대를 <c>AppVersion</c> 으로 좁힌 접속 설정 (S-ONL-RBCJKD · D-ONL-WD9WX9).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>왜 코드에서 덮어쓰는가.</b> 값이 들어가야 할 자리는
        /// <c>Assets/Photon/Fusion/Resources/PhotonAppSettings.asset</c> 인데 그곳은 서드파티 영역이라
        /// 통째로 교체할 수 있어야 한다(D-007). 에셋은 그대로 두고, Fusion 이 열어 둔
        /// <c>CustomPhotonAppSettings</c> 로 이 실행에서만 값을 바꿔 넣는다.
        /// </para>
        /// <para>
        /// <b>매번 새로 만든다.</b> Fusion 은 접속 과정에서 이 객체의 <c>Server</c>·<c>Port</c> 를 채워
        /// 넣으므로, 한 벌을 만들어 돌려 쓰면 지난 세션의 주소가 다음 세션에 섞여 들어간다.
        /// </para>
        /// </remarks>
        private static Fusion.Photon.Realtime.FusionAppSettings BuildCompatibilityAppSettings()
        {
            // 환경별 AppId/리전 설정을 먼저 복사하고 동일한 설정에 프로토콜 세대를 적용한다.
            var settings = FusionAppSettingsProvider.Resolve();
            settings.AppVersion = BuildVersionIdentity.Current;
            return settings;
        }

        /// <summary>
        /// 세션 잔재를 전부 비운다. Runner 가 있으면 함께 파괴한다.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>Runner 가 없어도 조기 반환하지 않는다.</b> 심사값(<see cref="roomAdmission"/>)과
        /// 목록 스냅샷은 Runner 를 만들기 <b>전에</b> 준비될 수 있어서(StartSession 은 잠금을 먼저
        /// 건다), 초기화가 실패해 Runner 가 없는 상태로 여기 오면 이전 방의 검증값이 그대로 남는다 —
        /// 그 다음에 만든 공개 방이 옛 비밀번호로 심사되어 아무도 들어오지 못한다.
        /// 여러 번 불려도 같은 결과인 멱등 호출이다.
        /// </remarks>
        private void DisposeRunner()
        {
            sessionJoined = false;
            joinedDedicatedServerRoom = false;
            if (runner != null)
            {
                runner.RemoveCallbacks(this);
                Destroy(runner.gameObject);
                runner = null;
            }

            isConnected = false;

            // 로비를 떠나면 방 목록 갱신도 끊긴다. 남겨두면 다음 접속에서 첫 갱신이 오기 전에
            // 랜덤 참가가 **이전 세션의 낡은 방 이름**을 골라 GameNotFound 로 실패한다.
            randomJoinCandidates.Clear();

            // 같은 이유로 스냅샷도 비운다. 늦게 켜진 화면이 이미 사라진 방 목록이나
            // 이전 방의 참가자 줄을 그리면, 눌러도 아무 일이 없는 목록이 된다 (S-UX-BXXTHX).
            roomListSnapshot = Array.Empty<RoomSlot>();
            playerListSnapshot = Array.Empty<PlayerSlot>();

            // 주인 효과 줄도 세션 한정이다. 순번이 세션마다 새로 시작하므로 남기면 다음 세션의 줄을 버린다.
            HudEffectReplication.OwnerInbox.Clear();
            ownerHudEffectSequence = 0;

            // 봇 명단도 방에 딸린 것이다. 남겨 두면 다음 방의 첫 화면이 이전 방의 봇을 그린다.
            botRoster.Clear();
            replicatedBotHostPlayerId = 0;
            PublishBots();

            // ⚠️ 심사값은 세션 한정이다 (S-UX-BXXTHX). 남기면 다음에 만든 **공개 방**이
            //    이전 방의 비밀번호로 심사되어 아무도 들어오지 못한다.
            roomAdmission.Clear();

            // 이전 방의 제목이 남으면 다음 방에서 그것이 먼저 발행된다. 정원·공개 여부도 같다.
            hostRoomSettings.Clear();

            // 방을 나가면 다음 입장 때 초기값을 다시 받아야 한다. 순번을 안 비우면
            // 새 세션의 1번 통지가 "이미 지난 것" 으로 걸러진다.
            lastRoomSettingsSequence = 0;
            roomSettingsSequence = 0;

            // 세션이 끝나면 accountId 매핑도 함께 버린다 (S15P21A606-52).
            // PlayerRef 는 세션 한정 번호라 다음 세션에서 같은 번호가 다른 사람을 가리킨다.
            accountIdsByPlayer.Clear();

            // 닉네임 스냅샷도 계정 세션의 산물이라 여기서 버린다 (S15P21A606-96).
            // 남겨두면 재로그인 뒤 자동 접속이 IsConnected 만 보고 Connect 를 건너뛸 때
            // **이전 계정의 닉네임**이 다음 방으로 올라간다 — 결과·메달은 새 계정(accountId),
            // 화면 표시는 옛 계정으로 갈린다. 다음 Connect(nick) 가 반드시 다시 채운다.
            nickName = DefaultNickName;
            appearancePresetSeedsByPlayer.Clear();
            appearancePresetsByPlayer.Clear();
            matchAccountRoster.Clear();

            // 무기 명부도 세션 한정이다. 남겨두면 다음 세션(특히 훈련장)에서 같은 PlayerId 가
            // 지난 매치의 무기를 물려받는다 — accountId 매핑을 여기서 버리는 것과 같은 이유다.
            matchWeaponChoices.Clear();
            lobbyWeaponChoicesByPlayer.Clear();

            // 입장 순서도 세션 한정이다. 다음 세션은 1부터 다시 센다.
            lobbyJoinOrdersByPlayer.Clear();
            matchLobbyJoinOrders.Clear();
            lastAssignedLobbyJoinOrder = 0;
            roomOwnerPlayerId = -1;

            // ⚠️ **이걸 빠뜨리면 방을 오갈 때 참가자가 두 줄로 보인다** (2026-09-03 실측).
            //
            //    위의 Destroy 는 Unity 규칙상 **프레임 끝까지 지연된다.** 그동안 옛 PlayerInfo 는
            //    여전히 살아 있어 IsUsable 을 통과하므로, 그 창에서 다시 입장하면
            //    PublishPlayers 가 옛 항목과 새 항목을 함께 발행한다 — 같은 PlayerId 가 두 번 나온다.
            //
            //    게다가 이것은 despawn 이 아니라 하드 파괴라 PlayerInfo.Despawned 가 오지 않을 수 있고,
            //    그러면 UnregisterPlayer 도 돌지 않아 항목이 세션마다 계속 쌓인다.
            //
            //    ⚠️ 반대 방향 결함도 같이 막는다 — PlayerRef 는 세션마다 재사용되므로,
            //    옛 항목이 남아 있으면 EnsurePlayerInfo 의 `Owner == player` 검사가 걸려
            //    **새로 필요한 PlayerInfo 를 만들지 않고 건너뛴다.**
            playerInfos.Clear();
        }

        private void FinishLobbyConnectionFailure(string reason)
        {
            DisposeRunner();
            // UI가 실패 이벤트를 처리할 때 Connect 버튼을 즉시 다시 열 수 있어야 한다.
            // finally까지 기다린 뒤에는 별도 상태 이벤트가 없으므로 발행 전에 먼저 내린다.
            isConnecting = false;
            EventBus.Publish(new NetworkDisconnected(reason));
        }

        /// <summary>
        /// 씬 매니저와 오브젝트 프로바이더를 Runner 에 붙인다.
        /// Fusion 이 제공하는 <c>FusionBootstrap.InitializeNetworkRunner</c> 가 하는 것과 같은 일이다.
        /// </summary>
        private INetworkSceneManager EnsureRunnerServices()
        {
            var sceneManager = runner.GetComponent<INetworkSceneManager>()
                               ?? runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

            // 오브젝트 프로바이더가 없으면 NetworkObject 스폰이 실패한다.
            // 지금은 스폰하는 것이 없어 증상이 안 보이지만, 플레이어 오브젝트를 만드는 순간 필요하다.
            if (runner.GetComponent<INetworkObjectProvider>() == null)
            {
                runner.gameObject.AddComponent<NetworkObjectProviderDefault>();
            }

            return sceneManager;
        }

        /// <summary>
        /// 씬 이름 → <c>SceneRef</c>. 빌드 인덱스를 쓰므로 <b>Build Profiles 에 등록되어 있어야</b> 한다.
        /// </summary>
        private static bool TryResolveScene(string sceneName, out SceneRef sceneRef)
        {
            sceneRef = SceneRef.None;
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                return false;
            }

            for (var i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName)
                {
                    sceneRef = SceneRef.FromIndex(i);
                    return sceneRef.IsValid;
                }
            }

            return false;
        }

        // ─────────────────────────────────────────────────────────────
        // 콜백 번역 — Fusion → EventBus. 여기에 게임 로직을 넣지 않는다.
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 이 콜백이 <b>이미 버린 Runner</b> 에서 온 것인가.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Fusion 은 <c>Destroy</c> 가 실제로 반영되기 전(같은 프레임)에 옛 Runner 의 콜백을 한 번 더
        /// 전달할 수 있고, 우리는 세션마다 Runner 를 새로 만든다. 그때 옛 콜백을 그대로 처리하면
        /// 이전 세션의 목록이 새 방의 스냅샷을 덮거나, 이미 끝난 세션의 "끊김" 이 새 방 화면에 뜬다.
        /// </para>
        /// <para>
        /// ⚠️ <b>우리가 Runner 를 들고 있을 때만 판정한다.</b> <c>runner == null</c> 이면
        /// (정리가 먼저 끝난 경우) 예전처럼 통과시킨다 — 여기서 막으면 정리 도중 도착한
        /// <see cref="OnShutdown"/> 알림이 사라져 화면이 그대로 멈춘다. 정상적인 첫
        /// <see cref="OnPlayerJoined"/> 는 <c>EnsureRunner</c> 가 필드를 먼저 채우므로 영향을 받지 않는다.
        /// </para>
        /// </remarks>
        private bool IsStaleRunner(NetworkRunner r) => runner != null && !ReferenceEquals(r, runner);

        /// <summary>
        /// Host 만 <see cref="PlayerInfo"/> 를 스폰한다. 클라이언트에는 복제로 도착하고
        /// 그때 <see cref="PlayerInfo.Spawned"/> 가 스스로 등록한다.
        /// </summary>
        public void OnPlayerJoined(NetworkRunner r, PlayerRef player)
        {
            // 버린 Runner 의 참가 통지는 무시한다 — 옛 세션의 PlayerRef 로 입장 순서를 발급하면
            // 새 방의 번호가 밀린다. 새 Runner 의 첫 통지는 여기서 걸리지 않는다.
            if (IsStaleRunner(r)) return;

            // ⚠️ 실제 참가 순간에 입장 순서를 못 박는다 (S-GD-7G8ZTA). 나중에 목록을 정렬해
            //    되짚으면 재사용된 PlayerRef 번호 때문에 순서가 뒤바뀐다.
            AssignLobbyJoinOrder(player);
            EnsurePlayerInfo(r, player);
            EnsureCombatant(r, player);

            // 사람이 먼저다 — 봇으로 찬 방에도 사람은 들어오므로 여기서 넘친 봇을 자른다.
            TrimBotsToCapacity();
            PublishPlayers();

            // 늦게 들어온 사람에게 현재 방 설정을 알린다. 통지 방식이라 스스로 물어볼 수 없으므로,
            // 새 참가자가 생길 때 Host 가 다시 보내지 않으면 그 사람만 제목·정원을 모른 채 남는다.
            if (r.IsServer && player != r.LocalPlayer)
            {
                BroadcastRoomSettings();
                SendBotStateToPlayer(player);
            }

            // Host 만 상대와의 연결 방식을 알 수 있다. 참가자가 붙을 때마다 잰다.
            if (r.IsServer && player != r.LocalPlayer)
            {
                StartCoroutine(DiagnoseConnectionRoutine($"Player {player.PlayerId} 참가"));
            }
        }

        /// <summary>
        /// 나간 플레이어의 <see cref="PlayerInfo"/> 를 Host 가 정리한다.
        /// 스폰한 쪽이 치우지 않으면 유령 오브젝트가 남아 로비 인원이 줄지 않는다.
        /// </summary>
        public void OnPlayerLeft(NetworkRunner r, PlayerRef player)
        {
            // 버린 Runner 의 퇴장 통지는 무시한다 — PlayerRef 번호는 세션마다 재사용되므로
            // 그대로 처리하면 새 방의 **다른 사람** 매핑이 지워진다.
            if (IsStaleRunner(r)) return;

            // 이탈은 라운드 경계 배치의 대상 수를 바꾼다(S-GD-BYVCAF). 이 줄이 없으면 Host 로그만으로는
            // 누가 언제 나갔는지 알 수 없어 준비 단계 정지를 추적하지 못했다 (S15P21A606-306).
            Debug.Log($"[NetworkManager] Player {player.PlayerId} 이(가) 방을 떠났습니다 — " +
                $"매치 명부 유지={matchLobbyJoinOrders.ContainsKey(player.PlayerId)}, 서버={r.IsServer}");

            // ⚠️ 반드시 지운다. Fusion 은 빈 슬롯의 PlayerRef 번호를 재사용하므로, 남겨두면
            //    다음에 들어온 **다른 사람**이 이 사람의 accountId 를 가진 것으로 보이고
            //    자기 RPC 가 도착하기 전에 Ready 가 열린다 (S-ONL-ZJS2NM 가 막으려는 그것이다).
            //
            //    ⚠️ 그래서 이탈자의 accountId 는 여기서 사라진다. 이탈자도 결과에 한 줄로
            //    들어가야 하므로(S-GD-QJA6NY 꼴등 점수), 결과 제출은 이 매핑이 아니라
            //    **매치 시작 시점의 명부 스냅샷**을 써야 한다. 그 명부는 순위 계산과 함께
            //    붙는다 — 지금은 O-024·O-025 가 열려 있어 만들 수 없다 (S15P21A606-52 완료 조건 4).
            accountIdsByPlayer.Remove(player);
            appearancePresetSeedsByPlayer.Remove(player);
            appearancePresetsByPlayer.Remove(player);
            lobbyWeaponChoicesByPlayer.Remove(player);

            // 재사용될 번호라 현재 세션 매핑에서는 지운다. 진행 중 매치의 입장 순서는
            // matchLobbyJoinOrders 스냅샷이 들고 있으므로 이탈자도 순위 계산에 남는다.
            lobbyJoinOrdersByPlayer.Remove(player);

            // 전용 서버에서 방장이 나가면 남은 사람 중 가장 먼저 들어온 사람에게 넘긴다 (S-ONL-FAG9B8).
            HandRoomOwnershipIfNeeded(r, player);

            // ⚠️ 봇 관리 권한도 같이 지운다. Fusion 은 빈 슬롯의 PlayerRef 번호를 재사용하므로,
            //    남겨 두면 **다음에 들어온 다른 사람**이 앞사람의 권한을 물려받는다 — 바로 위
            //    accountId 를 지우는 것과 같은 이유다 (S-ONL-9GXTYQ).
            if (botRoster.ForgetPlayer(player.PlayerId) && r.IsServer)
            {
                BroadcastBotState();
                PublishBots();
            }

            if (r.IsServer)
            {
                if (r.TryGetPlayerObject(player, out NetworkObject combatant)
                    && combatant != null
                    && combatant.IsValid)
                {
                    r.Despawn(combatant);
                }

                for (var i = playerInfos.Count - 1; i >= 0; i--)
                {
                    var info = playerInfos[i];
                    if (IsUsable(info) && info.Owner == player)
                    {
                        r.Despawn(info.Object);
                    }
                }
            }

            PublishPlayers();
        }

        // ─────────────────────────────────────────────────────────────
        // PlayerInfo 등록 — PlayerInfo 가 자기 Spawned/Despawned 에서 부른다
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 이 <see cref="PlayerInfo"/> 를 지금 읽어도 되는가.
        /// </summary>
        /// <remarks>
        /// <c>info != null</c> 만으로는 부족하다 — <see cref="PlayerInfo.Owner"/> 와
        /// <c>Object.IsValid</c> 가 모두 <c>Object</c> 를 역참조하므로, 세션 종료·despawn 도중
        /// <c>Object</c> 가 파괴된 항목이 목록에 남아 있으면 그대로 터진다.
        /// 세 조건을 한곳에 모아 호출부가 빠뜨리지 못하게 한다.
        /// </remarks>
        private static bool IsUsable(PlayerInfo info)
        {
            return info != null && info.Object != null && info.Object.IsValid;
        }

        /// <summary>
        /// 지금 씬에 실제로 존재하는 전투 오브젝트 수 (S15P21A606-47).
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠️ <see cref="CombatantCount"/> 와 다르다. 그쪽은 <b>참가자가 소유한</b> 것만 세므로,
        /// 정리에 실패해 옛 전투원이 씬에 남아도 새 스폰이 <c>SetPlayerObject</c> 를 덮으면
        /// <b>개수가 그대로 보인다</b> — 유령 객체를 못 잡는다. 두 값이 다르면 정리가 실패한 것이다.
        /// </para>
        /// <para>
        /// 씬을 훑지 않고 <see cref="PlayerInfo"/> 와 같은 자기등록 방식으로 센다.
        /// </para>
        /// </remarks>
        public int SpawnedCombatantCount => spawnedCombatants.Count;

        /// <summary>전투 오브젝트가 <c>Spawned</c> 에서 스스로 등록한다.</summary>
        internal void RegisterCombatant(NetworkCombatantMotion combatant)
        {
            if (combatant != null && !spawnedCombatants.Contains(combatant))
            {
                spawnedCombatants.Add(combatant);
            }
        }

        /// <summary>전투 오브젝트가 <c>Despawned</c> 에서 스스로 등록을 뺀다.</summary>
        internal void UnregisterCombatant(NetworkCombatantMotion combatant) =>
            spawnedCombatants.Remove(combatant);

        /// <summary>
        /// 이 플레이어의 전투원을 찾는다 (S15P21A606-109 §A).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>못 찾는 것은 정상이다</b> — 아직 스폰되지 않았거나 이미 사라진 경우다. 준비 단계
        /// 진입 직후 한 프레임에서 실제로 일어난다. 예외를 던지면 그 한 번 때문에 방 전체가 멈춘다.
        /// </para>
        /// <para>
        /// <b>왜 별도 사전을 두지 않는가.</b> <see cref="spawnedCombatants"/> 가 이미
        /// <c>Spawned</c>/<c>Despawned</c> 에서 정확히 관리된다. 사전을 하나 더 두면 두 컬렉션이
        /// 갈릴 수 있고, static 으로 두면 도메인 리로드마다 초기화 훅이 따로 필요해진다
        /// (<c>ResetSandboxBridgeCache</c> 가 그 비용을 보여준다). 방 정원이 한 자리 수라
        /// 선형 탐색이 사전보다 느릴 일도 없다.
        /// </para>
        /// <para>
        /// ⚠️ <b>경제 도메인은 이 타입을 몰라야 한다</b> (D-060). 상점·룰렛이 쓰는
        /// <c>IPreparationPresenceProvider</c>·<c>IShopInventoryProvider</c> 구현이 이 메서드를
        /// 감싸고, 도메인은 그 인터페이스만 본다.
        /// </para>
        /// </remarks>
        public bool TryGetCombatant(int playerId, out NetworkCombatantMotion combatant)
        {
            // 대기방 봇은 입력 권한자가 없다(PlayerRef.None). 봇 참가자 키(30001~)로 물으면 매치 참가자 키로 찾는다 —
            // 그렇지 않으면 봇의 시설 원 판정·축복 아이템 지급·강화 무기 동기화가 전부 "없음" 이 된다(S15P21A606-326).
            // 사람의 PlayerId 는 이 범위를 쓰지 않으므로 사람 조회는 그대로다.
            bool lobbyBot = NetworkCombatantMotion.IsLobbyBotActorId(playerId);
            for (var i = 0; i < spawnedCombatants.Count; i++)
            {
                NetworkCombatantMotion candidate = spawnedCombatants[i];

                // Despawn 과 목록 제거 사이의 한 프레임에 파괴된 것이 남아 있을 수 있다.
                if (candidate == null || candidate.Object == null)
                {
                    continue;
                }

                if (lobbyBot ? candidate.MatchParticipantId == playerId : candidate.Object.InputAuthority.PlayerId == playerId)
                {
                    combatant = candidate;
                    return true;
                }
            }

            combatant = null;
            return false;
        }

        /// <summary>
        /// 아무 전투원이나 하나. <b>모든 전투원이 공유하는 것</b>을 꺼낼 때만 쓴다.
        /// </summary>
        /// <remarks>
        /// 지금 유일한 용도는 아이템 카탈로그다 — 카탈로그는 프리팹 하나를 전투원 전부가
        /// 참조하므로(<c>Net_Combatant.prefab</c> → <c>EquipmentCatalog.prefab</c>) 누구에게
        /// 물어도 같은 답이 나온다. <b>플레이어별로 달라지는 것을 여기서 꺼내면 안 된다</b> —
        /// 목록 순서에 따라 답이 바뀐다.
        /// </remarks>
        public bool TryGetAnyCombatant(out NetworkCombatantMotion combatant)
        {
            for (var i = 0; i < spawnedCombatants.Count; i++)
            {
                NetworkCombatantMotion candidate = spawnedCombatants[i];

                if (candidate == null || candidate.Object == null)
                {
                    continue;
                }

                combatant = candidate;
                return true;
            }

            combatant = null;
            return false;
        }


        // ─────────────────────────────────────────────────────────────
        // accountId 매핑 — Host 전용 (S15P21A606-52)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// <c>accountId</c> 상한. <c>docs/server/api.md</c> 3-2 의 "최대 32자 ASCII" 와 같은 값이다.
        /// </summary>
        /// <remarks>
        /// Fusion 의 복제·RPC 문자열은 <b>고정 크기</b>라 상한이 없으면 슬롯을 정할 수 없다.
        /// 서버 발급 형식이 바뀌어도 이 상한은 유지된다는 것이 계약이다.
        /// </remarks>
        internal const int MaxAccountIdLength = 32;

        /// <summary>
        /// 이 플레이어의 <c>accountId</c> 를 Host 가 기억한다. <see cref="PlayerInfo.RPC_SetAccountId"/> 가 부른다.
        /// </summary>
        /// <remarks>
        /// Host 에서만 의미가 있다 — 클라이언트에서는 RPC 자체가 실행되지 않는다.
        /// 빈 값은 등록하지 않는다. 등록되지 않은 플레이어는 Ready 를 할 수 없다(S-ONL-ZJS2NM).
        /// </remarks>
        internal void RegisterAccountId(PlayerRef player, string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                Debug.LogWarning($"[NetworkManager] Player {player.PlayerId} 가 빈 accountId 를 보냈습니다. 무시합니다.");
                return;
            }

            var trimmed = accountId.Trim();
            if (trimmed.Length > MaxAccountIdLength)
            {
                // 자르지 않는다 — 잘린 키는 남의 계정을 가리킬 수 있고, 그건 빈칸보다 나쁘다.
                Debug.LogError(
                    $"[NetworkManager] Player {player.PlayerId} 의 accountId 가 {trimmed.Length}자로 " +
                    $"상한({MaxAccountIdLength})을 넘어 버립니다. 서버 계약(api.md 3-2)을 확인하세요.");
                return;
            }

            accountIdsByPlayer[player] = trimmed;

            // ⚠️ accountId 는 서버 저장 키다. 콘솔·Player.log 에 값을 남기지 않는다.
            Debug.Log($"[NetworkManager] Player {player.PlayerId} 의 accountId 를 등록했습니다.");

            // Ready 가 막혀 있던 사람이 이제 풀렸다. 화면이 다시 그리도록 알린다.
            PublishPlayers();
        }

        /// <summary>이 플레이어의 <c>accountId</c> 를 Host 가 알고 있는가 (S-ONL-ZJS2NM).</summary>
        internal bool HasAccountId(PlayerRef player) => accountIdsByPlayer.ContainsKey(player);

        /// <summary>
        /// 계정 로그인에서 받은 외형 프리셋을 Host가 세션 동안 기억한다.
        /// 값 생성·재추첨은 서버 회원가입 경계의 책임이며, 이 계층은 검증 후 복제만 한다.
        /// </summary>
        /// <summary>
        /// 입력 권한자가 보낸 저장 외형(칸 번호)을 Host 가 검증·기억하고, 이미 스폰된 전투원이면 바로 복제한다.
        /// 번호 범위는 전투원 드라이버가 적용할 때 거른다 — 모르는 번호는 기본 파츠로 떨어진다.
        /// </summary>
        internal void RegisterAppearancePresets(PlayerRef player, NetworkAppearancePresetState presets)
        {
            if (!IsHost) return;
            if (appearancePresetsByPlayer.TryGetValue(player, out var prior) && prior.ContentEquals(presets)) return;
            presets.Revision = prior.Revision + 1;
            appearancePresetsByPlayer[player] = presets;
            if (runner != null && runner.TryGetPlayerObject(player, out NetworkObject existing) && existing != null)
            {
                var motion = existing.GetComponent<NetworkCombatantMotion>();
                if (motion != null) motion.SetAppearancePresets(presets);
            }

            Debug.Log($"[NetworkManager] Player {player.PlayerId} 의 저장 외형을 등록했습니다 (검 {presets.HasWeapon(WeaponType.Sword)} · 활 {presets.HasWeapon(WeaponType.Bow)}).");
        }

        private bool TryGetAppearancePresets(PlayerRef player, out NetworkAppearancePresetState presets) =>
            appearancePresetsByPlayer.TryGetValue(player, out presets);

        /// <summary>
        /// 로컬 계정의 저장 외형을 전투원 프리팹의 공용 카탈로그 칸 번호로 옮긴다 — 입력 권한자가 Host 에 보내기 전에 부른다.
        /// 저장본이 하나도 없으면 <c>false</c>(seed 외형).
        /// </summary>
        internal bool TryBuildLocalAppearancePresets(out NetworkAppearancePresetState presets)
        {
            presets = default;
            if (!BettingRoyale.Network.Api.AuthAdapter.HasInstance || BettingRoyale.Network.Api.AuthAdapter.Instance == null
                || !BettingRoyale.Network.Api.AuthAdapter.Instance.IsSignedIn) return false;
            if (combatantAppearanceDriver == null && combatantPrefab != null)
            {
                combatantAppearanceDriver = combatantPrefab.GetComponentInChildren<ICombatVisualDriver>(true);
            }

            if (combatantAppearanceDriver == null) return false;
            bool any = false;
            foreach (WeaponType weapon in new[] { WeaponType.Sword, WeaponType.Bow })
            {
                string key = BettingRoyale.Data.Profile.CharacterAppearanceCatalog.WeaponKey(weapon);
                if (!BettingRoyale.Network.Api.AuthAdapter.Instance.TryGetSavedAppearancePreset(key, out var preset)) continue;
                if (!combatantAppearanceDriver.TryEncodeAppearancePreset(preset, appearanceEncodeBuffer)) continue;
                presets.SetWeapon(weapon, appearanceEncodeBuffer);
                any = true;
            }

            return any;
        }

        internal void RegisterAppearancePresetSeed(PlayerRef player, int seed)
        {
            if (seed <= 0)
            {
                Debug.LogWarning($"[NetworkManager] Player {player.PlayerId} 가 유효하지 않은 외형 프리셋을 보냈습니다.");
                return;
            }

            appearancePresetSeedsByPlayer[player] = seed;
            Debug.Log($"[NetworkManager] Player {player.PlayerId} 의 계정 외형 프리셋을 등록했습니다.");
            PublishPlayers();
        }

        /// <summary>
        /// 방금 고친 프로필 초상화를 내 <see cref="PlayerInfo"/> 로 흘려보낸다.
        /// </summary>
        /// <remarks>
        /// 방 밖이면 보낼 곳이 없으므로 조용히 지나간다 — 선택은 이미 계정에 남았고,
        /// 다음에 방에 들어갈 때 <c>Spawned</c> 가 같은 값을 보낸다.
        /// </remarks>
        public void RefreshLocalProfilePortrait()
        {
            if (runner == null || !runner.IsRunning) return;
            if (!TryFindPlayerInfo(runner.LocalPlayer, out PlayerInfo info)) return;
            info.RefreshProfilePortrait();
        }

        /// <summary>이 플레이어의 계정 외형 프리셋이 Host에 등록됐는가.</summary>
        internal bool HasAppearancePresetSeed(PlayerRef player) =>
            appearancePresetSeedsByPlayer.TryGetValue(player, out int seed) && seed > 0;

        /// <summary>
        /// <b>매치 명부</b> — 매치 시작 시점의 <c>playerId → accountId</c> 스냅샷 (S15P21A606-52).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b><see cref="accountIdsByPlayer"/> 와 수명이 다르다.</b> 저쪽은 "지금 방에 있는 사람" 이라
        /// 나가면 지워진다. 이쪽은 <b>매치가 끝날 때까지 고정</b>된다 — 이탈자도 결과에 한 줄로
        /// 들어가야 하기 때문이다([S-GD-QJA6NY](../../../../../docs/specs/game-design/S-GD-QJA6NY.md):
        /// 이탈자는 남은 라운드마다 꼴등 점수를 받고, `api.md` 3-3 은 "참가한 모든 계정" 을 요구한다).
        /// </para>
        /// <para>
        /// 두 개를 나누지 않으면 둘 중 하나가 반드시 깨진다 — 이탈 때 지우면 이탈자의 계정을 잃고,
        /// 안 지우면 재사용된 <c>PlayerRef</c> 번호가 남의 계정을 가리킨다.
        /// </para>
        /// </remarks>
        private readonly Dictionary<int, string> matchAccountRoster = new();

        /// <summary>매치 명부. 비어 있으면 아직 매치가 시작되지 않았거나 명부를 뜨지 못한 것이다.</summary>
        public IReadOnlyDictionary<int, string> MatchAccountRoster => matchAccountRoster;

        /// <summary>
        /// 지금 방에 있는 사람들로 매치 명부를 뜬다. <b>Host 가 매치를 시작할 때 한 번</b> 부른다.
        /// </summary>
        /// <remarks>
        /// accountId 가 없는 사람은 애초에 Ready 를 못 하므로(S-ONL-ZJS2NM) 매치가 시작됐다면
        /// 전원이 등록돼 있어야 한다. 그렇지 않다면 게이트가 새고 있는 것이라 <b>경고를 남긴다</b> —
        /// 이 상태로 매치를 마치면 제출이 통째로 거부된다.
        /// </remarks>
        /// <returns>명부에 담긴 인원. 0 이면 제출이 불가능한 매치다.</returns>
        internal int CaptureMatchRoster()
        {
            matchAccountRoster.Clear();
            matchLobbyJoinOrders.Clear();

            if (runner == null || !runner.IsRunning || !runner.IsServer)
            {
                return 0;
            }

            // ⚠️ 봇은 **명부에는 들어가고 계정 명부에는 들어가지 않는다** (S-ONL-9GXTYQ).
            //    matchLobbyJoinOrders 는 배점 인원 N 과 전투 진입 게이트가 쓰는 "참가자 수" 이고,
            //    matchAccountRoster 는 결과·메달을 받을 계정이다. 봇에게 가짜 계정을 만들어 주면
            //    메달이 지급된다 — 티켓이 명시적으로 금지한 것이다.
            //    입장 순서는 사람 뒤에 잇는다(사람이 먼저, 봇이 뒤 — S-UX-9SMAFJ 와 같은 순서).
            matchBotNumbers.Clear();
            foreach (LobbyBotSlot bot in botRoster.Slots) matchBotNumbers.Add(bot.Number);

            foreach (PlayerRef player in runner.ActivePlayers)
            {
                // 입장 순서는 매치 시작 시점에 함께 얼린다. 이탈해도 순위 계산이 그대로 성립한다.
                matchLobbyJoinOrders[player.PlayerId] = AssignLobbyJoinOrder(player);

                if (accountIdsByPlayer.TryGetValue(player, out var accountId))
                {
                    matchAccountRoster[player.PlayerId] = accountId;
                    continue;
                }

                Debug.LogError(
                    $"[NetworkManager] Player {player.PlayerId} 의 accountId 를 모른 채 매치가 시작됐습니다. " +
                    "결과 제출이 거부됩니다 (S-ONL-ZJS2NM 준비 게이트를 확인하세요).");
            }

            // 봇을 사람 뒤 순서로 명부에 잇는다. 계정 명부에는 넣지 않는다.
            int nextOrder = 0;
            foreach (int order in matchLobbyJoinOrders.Values) if (order > nextOrder) nextOrder = order;
            for (var i = 0; i < matchBotNumbers.Count; i++)
            {
                matchLobbyJoinOrders[NetworkCombatantMotion.LobbyBotActorId(matchBotNumbers[i])] = ++nextOrder;
            }

            Debug.Log(
                $"[NetworkManager] 매치 명부를 떴습니다. 사람 {matchAccountRoster.Count}명" +
                (matchBotNumbers.Count > 0 ? $" · 봇 {matchBotNumbers.Count}기" : string.Empty) + ".");
            return matchAccountRoster.Count;
        }

        /// <summary>
        /// 이 플레이어의 <c>accountId</c>. 모르면 <c>false</c> 이고 <paramref name="accountId"/> 는 빈 문자열이다.
        /// </summary>
        /// <remarks>
        /// 결과 제출이 <c>PlayerId</c> 기반 순위를 <c>accountId</c> 기반으로 옮길 때 쓴다
        /// (<c>MatchResultAdapter.Submit</c>, api.md 3-3).
        /// </remarks>
        /// <summary>
        /// 이 플레이어의 대기실 입장 순서를 확정한다. 이미 있으면 그대로 돌려준다 —
        /// <b>한 번 매긴 순서는 세션 안에서 바뀌지 않는다</b>.
        /// </summary>
        private int AssignLobbyJoinOrder(PlayerRef player)
        {
            if (lobbyJoinOrdersByPlayer.TryGetValue(player, out int existing))
            {
                return existing;
            }

            lastAssignedLobbyJoinOrder++;
            lobbyJoinOrdersByPlayer[player] = lastAssignedLobbyJoinOrder;
            return lastAssignedLobbyJoinOrder;
        }

        /// <summary>
        /// 매치 명부의 입장 순서 스냅샷. 비어 있으면 아직 매치를 시작하지 않은 것이다.
        /// </summary>
        public IReadOnlyDictionary<int, int> MatchLobbyJoinOrders => matchLobbyJoinOrders;

        /// <summary>
        /// 이 플레이어의 대기실 입장 순서(1부터). 매치 스냅샷을 먼저 보고, 없으면 현재 세션 값을 본다.
        /// </summary>
        public bool TryGetLobbyJoinOrder(int playerId, out int joinOrder)
        {
            if (matchLobbyJoinOrders.TryGetValue(playerId, out joinOrder) && joinOrder > 0)
            {
                return true;
            }

            foreach (var pair in lobbyJoinOrdersByPlayer)
            {
                if (pair.Key.PlayerId == playerId)
                {
                    joinOrder = pair.Value;
                    return joinOrder > 0;
                }
            }

            joinOrder = 0;
            return false;
        }

        public bool TryGetAccountId(PlayerRef player, out string accountId) =>
            accountIdsByPlayer.TryGetValue(player, out accountId) || Fail(out accountId);

        private static bool Fail(out string accountId)
        {
            accountId = string.Empty;
            return false;
        }

        internal void RegisterPlayer(PlayerInfo info)
        {
            if (info != null && !playerInfos.Contains(info))
            {
                playerInfos.Add(info);
                PublishPlayers();
            }
        }

        internal void UnregisterPlayer(PlayerInfo info)
        {
            if (playerInfos.Remove(info))
            {
                PublishPlayers();
            }
        }

        /// <summary>닉네임이 늦게 도착했을 때 UI 를 다시 그리게 한다.</summary>
        internal void NotifyPlayersChanged() => PublishPlayers();

        public void OnDisconnectedFromServer(NetworkRunner r, NetDisconnectReason reason)
        {
            // 이미 새 Runner 로 넘어왔다면 옛 연결의 끊김은 지금 화면의 사고가 아니다.
            // 그대로 발행하면 방금 들어간 방에서 "연결이 끊어졌습니다" 가 뜨고 SessionReturnRouter 가
            // 멀쩡한 세션을 로비로 되돌린다.
            if (IsStaleRunner(r))
            {
                Debug.Log($"[NetworkManager] 이전 Runner 의 연결 끊김 통지를 무시합니다: {reason}");
                return;
            }

            if (isStartingSession && !sessionJoined)
            {
                EventBus.Publish(new RoomJoinFailed(DescribeDisconnectReason(reason)));
                return;
            }

            Debug.Log($"[NetworkManager] 연결 끊김: {reason}");
            LastDisconnectReason = DescribeDedicatedServerLoss() ?? DescribeDisconnectReason(reason);
            EventBus.Publish(new NetworkDisconnected(LastDisconnectReason));
        }

        public void OnConnectFailed(NetworkRunner r, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            // 옛 Runner 의 실패는 이미 끝난 시도다. 새 시도 화면에 실패 문구를 덮지 않는다.
            if (IsStaleRunner(r))
            {
                Debug.Log($"[NetworkManager] 이전 Runner 의 접속 실패 통지를 무시합니다: {reason}");
                return;
            }

            Debug.LogError($"[NetworkManager] 접속 실패: {reason}");
            EventBus.Publish(new RoomJoinFailed(DescribeConnectFailedReason(reason)));
        }

        /// <summary>
        /// 세션이 끝났다. <b>여기서 Runner 를 파괴한다</b> — 재사용할 수 없기 때문이다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="RoomLeft"/> 만 발행하고 <see cref="NetworkDisconnected"/> 는 발행하지 않는다.
        /// 정상 퇴장까지 "연결 끊김" 으로 표시되면 사용자가 오류로 오해한다.
        /// 진짜 연결 문제는 <see cref="OnDisconnectedFromServer"/> 가 따로 알린다.
        /// </para>
        /// <para>
        /// ⚠️ <b>Runner 를 파괴하므로 <see cref="IsConnected"/> 도 false 가 된다</b> —
        /// 방을 나오면 로비 접속부터 다시 해야 한다. PUN2 는 방을 나가도 마스터 서버 연결이 유지됐지만
        /// Fusion 은 Runner 를 재사용할 수 없어서 그럴 수 없다.
        /// UI 는 이 상태를 그대로 반영한다(로비 버튼 비활성, 접속 버튼 활성).
        /// 자동 재접속을 넣고 싶다면 여기가 아니라 UI 쪽에서 <see cref="Connect"/> 를 다시 부르는 편이 낫다 —
        /// 여기서 하면 사용자가 나가려는 것인지 재접속인지 구분할 수 없다.
        /// </para>
        /// </remarks>
        public void OnShutdown(NetworkRunner r, ShutdownReason shutdownReason)
        {
            // 우리가 이미 다른 Runner 를 들고 있다면 이건 버린 세션의 종료다 — 여기서 정리하면
            // **지금 쓰는 Runner 의 상태**를 지우게 된다. 우리 Runner 가 없을 때(정리가 먼저 끝난
            // 경우)는 예전처럼 진행해 알림이 사라지지 않게 한다.
            if (IsStaleRunner(r))
            {
                Debug.Log($"[NetworkManager] 이전 Runner 의 세션 종료 통지를 무시합니다: {shutdownReason}");
                return;
            }

            bool hadJoinedRoom = sessionJoined;
            bool failedHandshake = isStartingSession && !hadJoinedRoom;
            Debug.Log($"[NetworkManager] 세션 종료: {shutdownReason}");

            // ⚠️ **Host 가 앱을 종료하면 Client 는 이 경로로 온다** — 2026-09-03 2PC 실측에서
            //    `DisconnectedByPluginLogic` 이 여기로 들어왔다. <see cref="OnDisconnectedFromServer"/>
            //    는 불리지 않았다. 그래서 사유 보관을 그쪽에만 두면 **Host 이탈에서 사유가 빈다.**
            //
            //    Ok 는 내가 나가기를 눌러 정상 종료한 경우다. 그때는 보여줄 사유가 없다 —
            //    남겨두면 다음에 로비로 돌아왔을 때 "연결이 끊어졌습니다" 가 괜히 뜬다.
            LastDisconnectReason = shutdownReason == ShutdownReason.Ok
                ? string.Empty
                : DescribeDedicatedServerLoss() ?? DescribeShutdownReason(shutdownReason);

            // ⚠️ <b>Runner 정리가 실패해도 알림은 반드시 나가야 한다.</b>
            //
            //    2026-09-03 2PC 실측: Host 가 앱을 종료하자 이 메서드의 첫 줄은 찍혔는데
            //    아래 두 발행에 아무도 반응하지 않았다 — LobbyScreen 도, OnlineCoreLoopCoordinator 도.
            //    두 구독은 살아 있었고(스냅샷은 계속 받고 있었다) 그 사이에 있는 것은 DisposeRunner
            //    뿐이다. 정상 퇴장(LeaveRoom → Ok)에서는 같은 코드가 멀쩡히 돌았는데, 그때는
            //    우리가 부른 Shutdown 이고 이번엔 **Photon 의 OnDisconnected 콜백 스택 안**이다.
            //    그 안에서 Runner 의 GameObject 를 파괴하는 것이 다르다.
            //
            //    발행이 막히면 Client 는 아무 통보도 못 받고 화면이 그대로 멈춘다. 정리 실패보다
            //    그쪽이 훨씬 나쁘므로, 예외를 삼키더라도 알림은 내보낸다.
            try
            {
                DisposeRunner();
            }
            catch (Exception e)
            {
                Debug.LogError($"[NetworkManager] 세션 정리 중 예외 — 알림은 계속 보냅니다: {e}");
            }

            if (suppressShutdownPublish)
            {
                // 로그아웃(Disconnect)發 셧다운 — 세션 사고가 아니므로 알림을 내보내지 않는다.
                // 내보내면 SessionReturnRouter 가 로비로 되돌리고 익명 자동 재접속이 따라온다.
                suppressShutdownPublish = false;
                Debug.Log("[NetworkManager] 로그아웃 종료 — 세션 알림을 생략합니다.");
                return;
            }

            // 입장 승인 전 실패는 떠날 방이 없다. RoomLeft를 발행하면 로비가 재로드되어
            // 비밀번호 팝업과 실패 사유를 잃으므로 요청 실패만 알린다.
            if (failedHandshake)
            {
                EventBus.Publish(new RoomJoinFailed(DescribeShutdownReason(shutdownReason)));
                return;
            }
            EventBus.Publish(new NetworkSessionStateChanged(isActive: false));
            EventBus.Publish(new RoomLeft());
        }

        /// <summary>
        /// Fusion 의 종료 사유를 <b>사용자가 읽을 문장</b>으로 바꾼다 (S15P21A606-55).
        /// </summary>
        /// <remarks>
        /// <para>
        /// 예전에는 <c>ShutdownReason.ToString()</c> 을 그대로 화면에 올렸다. 사용자에게
        /// "GameNotFound" 는 아무 뜻도 아니고, 무엇을 해야 하는지도 알려주지 않는다.
        /// </para>
        /// <para>
        /// <b>번역을 여기서 끝내는 이유</b> — Fusion 타입을 이 클래스 밖으로 내보내지 않는다(D-009).
        /// UI 가 <c>ShutdownReason</c> 을 알게 되면 그 순간 경계가 깨진다.
        /// </para>
        /// <para>
        /// ⚠️ 모르는 사유는 <b>원문을 그대로 붙여서</b> 내보낸다. 문장을 지어내면 진짜 원인이 사라져
        /// 로그로 추적할 수 없게 된다.
        /// </para>
        /// </remarks>
        private static string DescribeShutdownReason(ShutdownReason reason) => reason switch
        {
            // ⚠️ 버전이 다른 빌드는 서로의 방이 **애초에 보이지 않는다**(S-ONL-RBCJKD).
            //    Photon 은 그 경우에도 "없는 방" 과 같은 사유를 주므로 둘을 구분해 줄 수 없다.
            //    그래서 단정하지 않고, 사용자가 직접 확인할 수 있는 곳을 짚어 준다.
            ShutdownReason.GameNotFound =>
                "방을 찾지 못했습니다. 이미 닫혔을 수 있습니다. 버전이 다르면 서로의 방이 보이지 않으니 "
                + "화면 구석의 버전을 서로 비교해 주세요.",
            ShutdownReason.GameIsFull =>
                "방 정원이 찼습니다.",
            ShutdownReason.GameClosed =>
                "이미 시작된 방이라 참가할 수 없습니다.",
            ShutdownReason.GameIdAlreadyExists =>
                "같은 이름의 방이 이미 있습니다. 다른 이름을 쓰세요.",
            ShutdownReason.ServerInRoom =>
                "이미 다른 방에 있습니다. 나간 뒤 다시 시도하세요.",
            ShutdownReason.MaxCcuReached =>
                "동시 접속 한도에 걸렸습니다. 잠시 후 다시 시도하세요.",
            ShutdownReason.InvalidRegion =>
                "접속 지역 설정이 잘못됐습니다.",
            ShutdownReason.InvalidAuthentication =>
                "인증에 실패했습니다. Photon AppId 설정을 확인하세요.",
            // ⚠️ "비밀번호가 틀렸다" 로 단정하지 않는다. Host 는 심사 실패와 참가 조건(닫힌 방·
            //    정원·옛 빌드)을 구분해 알려주지 않으므로, 확인할 곳만 짚어 준다.
            ShutdownReason.ConnectionRefused =>
                "연결이 거부됐습니다. 방 비밀번호 또는 참가 조건을 확인해 주세요.",
            ShutdownReason.ConnectionTimeout or ShutdownReason.OperationTimeout or ShutdownReason.PhotonCloudTimeout =>
                "응답이 없습니다. 네트워크를 확인하고 다시 시도하세요.",
            ShutdownReason.OperationCanceled =>
                "요청이 취소됐습니다. 다시 시도하세요.",
            ShutdownReason.IncompatibleConfiguration =>
                "빌드 설정이 서로 맞지 않습니다. 양쪽 빌드를 같은 버전으로 맞추세요.",

            // ⚠️ **Host 가 앱을 종료했을 때 Client 가 받는 값이다** — 2026-09-03 2PC 실측으로 확인했다.
            //    Host Migration 이 없으므로 Host 가 사라지면 방이 닫힌다(D-030). 결함이 아니라 설계다.
            ShutdownReason.DisconnectedByPluginLogic =>
                "방장이 나가 방이 닫혔습니다.",

            // 우리는 Host Migration 을 하지 않는다(D-030). 이 값이 오면 설정이 어긋난 것이다.
            ShutdownReason.HostMigration =>
                "방장이 바뀌는 중 연결이 끊겼습니다.",

            // 모르는 사유. 원문을 남겨 로그로 추적할 수 있게 한다.
            _ => $"방에 참가하지 못했습니다 ({reason}).",
        };

        /// <summary>
        /// 연결이 끊긴 사유를 <b>사용자가 읽을 문장</b>으로 바꾼다 (S15P21A606-27).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="DescribeShutdownReason"/> 과 같은 이유로 존재한다. 다만 <b>이쪽이 Client 가
        /// 실제로 타는 경로</b>다 — Host 가 사라지면 Client 에게는 <c>ShutdownReason</c> 이 아니라
        /// <c>NetDisconnectReason</c> 이 온다. 이 번역이 없으면 <c>UIScreen.UserSafeReason</c> 이
        /// 한글 없는 enum 이름을 통째로 버려서 <b>사유가 화면에서 사라진다.</b>
        /// </para>
        /// <para>
        /// ⚠️ 반환 문장은 <b>원인만</b> 담는다. 화면이 이미 "연결이 끊어졌습니다 — " 를 앞에 붙이므로
        /// (<c>RoomSetupScreen</c>·<c>RoomBrowserScreen</c>) 여기서 같은 말을 반복하면 두 번 나온다.
        /// </para>
        /// </remarks>
        private static string DescribeDisconnectReason(NetDisconnectReason reason) => reason switch
        {
            NetDisconnectReason.ByRemote =>
                "방장이 방을 닫았거나 연결을 끊었습니다.",
            NetDisconnectReason.Timeout =>
                "서버 응답이 없습니다. 네트워크를 확인하세요.",
            NetDisconnectReason.Requested =>
                "연결을 정상적으로 종료했습니다.",
            NetDisconnectReason.ProtocolError =>
                "통신 규약이 맞지 않습니다. 양쪽 빌드를 같은 버전으로 맞추세요.",
            NetDisconnectReason.SendWindowFull =>
                "네트워크가 혼잡해 연결을 유지하지 못했습니다.",
            NetDisconnectReason.SequenceOutOfBounds =>
                "패킷 순서가 어긋났습니다.",

            // 모르는 사유. 원문을 남겨 로그로 추적할 수 있게 한다.
            _ => $"알 수 없는 원인입니다 ({reason}).",
        };

        /// <summary>
        /// 접속 실패 사유를 <b>사용자가 읽을 문장</b>으로 바꾼다 (S15P21A606-27).
        /// </summary>
        /// <remarks>
        /// 문구는 <see cref="DescribeShutdownReason"/> 의 같은 뜻 항목과 맞춘다 — 같은 상황을
        /// 두 경로가 다른 말로 알리면 사용자는 서로 다른 문제로 오해한다.
        /// </remarks>
        private static string DescribeConnectFailedReason(NetConnectFailedReason reason) => reason switch
        {
            NetConnectFailedReason.ServerFull =>
                "방 정원이 찼습니다.",
            // 같은 상황을 두 경로가 다른 말로 알리지 않도록 ConnectionRefused 문구와 맞춘다.
            NetConnectFailedReason.ServerRefused =>
                "연결이 거부됐습니다. 방 비밀번호 또는 참가 조건을 확인해 주세요.",
            NetConnectFailedReason.Timeout =>
                "응답이 없습니다. 네트워크를 확인하고 다시 시도하세요.",

            // 모르는 사유. 원문을 남겨 로그로 추적할 수 있게 한다.
            _ => $"연결하지 못했습니다 ({reason}).",
        };

        public void OnSessionListUpdated(NetworkRunner r, List<SessionInfo> sessionList)
        {
            // 버린 Runner 의 목록은 이미 낡았다. 그대로 받으면 랜덤 참가가 **이전 세션의 방 이름**을
            // 고르고, 늦게 켜진 화면이 사라진 방 목록을 그린다.
            if (IsStaleRunner(r) || sessionList == null) return;

            var rooms = new RoomSlot[sessionList.Count];
            randomJoinCandidates.Clear();

            for (var i = 0; i < sessionList.Count; i++)
            {
                var s = sessionList[i];

                // 목록에 남아 있다고 들어갈 수 있는 것이 아니다 (S15P21A606-55).
                //
                //  IsOpen      방장이 참가를 닫았거나 매치가 시작된 방
                //  IsVisible   비공개로 숨긴 방
                //  PlayerCount 정원이 찼는가
                //
                // ⚠️ PlayerCount > 0 이 특히 중요하다. **아무도 없는 방이 목록에 남는다.**
                //    Host Mode 에서 인원 0 은 곧 Host 가 없다는 뜻이라 Client 로 들어갈 대상이
                //    아니고, 실제로 시도하면 GameNotFound 가 난다. 방을 만든 직후에는 Host 가
                //    이미 들어가 있으므로 정상 방은 최소 1 이다 (실측 2026-09-01).
                // 전용 서버 방은 서버도 Photon 참가자라 인원·정원에 한 자리씩 들어 있다 — 사람 기준으로 센다
                // (SessionPeerCapacity). 빼지 않으면 사람이 다 나간 방이 "1/…" 로 남아 들어갈 수 있어 보인다.
                int people = SessionPeopleCount(s);
                int capacity = SessionPeerCapacity(s);
                bool joinable =
                    s.IsOpen
                    && s.IsVisible
                    && people > 0
                    && people < capacity;

                // ⚠️ 잠금은 **참가 가능 여부를 깎지 않는다.** 비밀번호를 아는 사람은 들어갈 수
                //    있으므로 목록에는 그대로 보이고 자물쇠만 붙는다 (S-UX-BXXTHX).
                bool locked = ReadRoomLocked(s);

                rooms[i] = new RoomSlot(
                    s.Name,
                    people,
                    capacity,
                    joinable,
                    ReadRoomTitle(s),
                    ReadRoomHost(s),
                    locked);

                // 랜덤 참가만 좁힌다 — 고르지 않은 방에 들어가는 경로에는 비밀번호를 물을 자리가 없다.
                if (joinable && !locked)
                {
                    randomJoinCandidates.Add(s.Name);
                }
            }

            // 늦게 켜진 화면이 물어볼 수 있도록 발행값을 들고 있는다 (S-UX-BXXTHX).
            // ⚠️ 사본을 보관한다 — 발행한 배열을 구독자가 정렬하면 우리 상태까지 바뀐다.
            roomListSnapshot = (RoomSlot[])rooms.Clone();
            EventBus.Publish(new RoomListChanged(rooms));
        }

        /// <summary>
        /// 인원 변화를 <b>현재 전체 목록</b>으로 발행한다.
        /// 델타로 쪼개지 않는 이유: UI 가 상태를 누적해야 하고, 이벤트 하나를 놓치면
        /// 화면과 실제가 어긋난 채 재현이 안 되는 버그가 된다.
        /// </summary>
        private void PublishPlayers()
        {
            if (runner == null || !runner.IsRunning) return;

            var players = new List<PlayerSlot>();
            foreach (var info in playerInfos)
            {
                if (!IsUsable(info))
                {
                    continue;   // despawn 직후 한 프레임 남아 있을 수 있다
                }

                var owner = info.Owner;
                var replicated = info.NickName.Value;

                players.Add(new PlayerSlot(
                    playerId: owner.PlayerId,
                    // 닉네임은 RPC 로 뒤늦게 도착한다. 비어 있는 동안 이름 없는 줄이 보이지 않도록 대체한다.
                    nickName: string.IsNullOrEmpty(replicated) ? $"Player {owner.PlayerId}" : replicated,
                    isHost: info.IsHost,
                    isLocal: owner == runner.LocalPlayer,
                    isReady: info.IsReady,
                    portraitId: info.ProfilePortrait.ToString()));
            }

            players.Sort((a, b) => a.PlayerId.CompareTo(b.PlayerId));

            // 늦게 켜진 대기방 화면이 물어볼 수 있도록 사본을 들고 있는다 (S-UX-BXXTHX).
            playerListSnapshot = players.ToArray();

            // ⚠️ **이 방의 실제 정원**을 쓴다. maxPlayersPerRoom 은 새 방을 만들 때의 기본값일 뿐이라,
            //    정원 4명으로 만든 방에서도 8 이 나가 목록("3/4")과 대기실("3/8")이 어긋났다
            //    (김지윤 님 리뷰, 2026-09-01). 정원 지정 기능을 넣으면서 생긴 회귀다.
            EventBus.Publish(new RoomPlayersChanged(
                (PlayerSlot[])playerListSnapshot.Clone(),
                CurrentRoomMaxPlayers,
                CanStartMatch));
        }

        // ─────────────────────────────────────────────────────────────
        // 플레이어 메타데이터는 PlayerInfo(NetworkObject)가 복제한다.
        // PlayerRef 자체에는 닉네임도 Host 여부도 없다 — PUN2 와 다른 점이다.
        //
        // ⚠️ 남는 한계: 닉네임은 RPC 로 오므로 스폰 직후 한두 프레임은 비어 있다.
        //    그동안 "Player 2" 로 보이다가 채워진다. UI 가 깜빡이면 이것이 원인이다.
        // ─────────────────────────────────────────────────────────────

        // ─────────────────────────────────────────────────────────────
        // 나머지 콜백 — 현재 계약에 필요한 입력·씬 수명만 구현하고 그 밖에는 번역 대상이 생길 때 채운다.
        //
        // Fusion 2.1.1 Stable (build 2177) 의 INetworkRunnerCallbacks 멤버 19개와 일치하도록 맞췄다.
        // 버전을 올리면 멤버가 늘거나 줄 수 있다 — 그때는 IDE 의 "인터페이스 구현" 으로 다시 맞춘다.
        // ─────────────────────────────────────────────────────────────

        public void OnInput(NetworkRunner r, NetworkInput input)
        {
            OnInputCallCount++;
            if (!r.TryGetPlayerObject(r.LocalPlayer, out NetworkObject localCombatant)
                || localCombatant == null)
            {
                // ⚠️ **여기서 조용히 나가면 "클라가 안 움직인다" 로만 보인다** (S15P21A606-275).
                //
                //    ⚠️ 단 <b>아직 한 번도 못 받은 것은 정상이다</b> — 방에 들어와서 전투원이 스폰되기
                //    전까지(대기실) 이 경로를 매 틱 지난다. 처음 넣었을 때 그 구간을 경고로 도배해
                //    오탐만 남겼다. <b>가지고 있다가 잃은 경우</b>만 알린다 — 그때가 진짜 고장이다.
                if (hadPlayerObject && !inputWithoutPlayerObjectReported)
                {
                    inputWithoutPlayerObjectReported = true;
                    Debug.LogWarning(
                        "[NetworkManager] 내 전투 오브젝트를 잃어 입력을 싣지 못합니다. " +
                        "이 상태가 이어지면 이 피어는 움직이지 않습니다.");
                }

                return;
            }

            if (inputWithoutPlayerObjectReported)
            {
                inputWithoutPlayerObjectReported = false;
                Debug.Log("[NetworkManager] 내 전투 오브젝트를 다시 찾아 입력을 싣습니다.");
            }

            hadPlayerObject = true;
            OnInputFilledCount++;

            var motion = localCombatant.GetComponent<NetworkCombatantMotion>();
            if (motion != null)
            {
                CombatNetworkInput frame = motion.ConsumeInput();
                if (frame.MoveDirection.sqrMagnitude > 0.0001f) OnInputMovingCount++;
                input.Set(frame);
            }
        }

        /// <summary>
        /// 방에 남아 있는 전투 오브젝트를 모두 치운다. <b>Host 만 호출된다.</b> (S15P21A606-47)
        /// </summary>
        /// <remarks>
        /// <see cref="PlayerInfo"/> 는 건드리지 않는다 — 대기실에서도 참가자 목록·Ready 는 유지되어야 한다.
        /// 전투 오브젝트만 매치의 수명을 따른다.
        /// </remarks>
        private void DespawnCombatants(NetworkRunner r)
        {
            var removed = 0;
            foreach (PlayerRef player in r.ActivePlayers)
            {
                if (!r.TryGetPlayerObject(player, out NetworkObject combatant)
                    || combatant == null
                    || !combatant.IsValid)
                {
                    continue;
                }

                r.SetPlayerObject(player, null);
                r.Despawn(combatant);
                removed++;
            }

            if (removed > 0)
            {
                Debug.Log($"[NetworkManager] 대기실 복귀 — 전투 오브젝트 {removed}개를 정리했습니다.");
            }
        }

        /// <summary>
        /// 프리팹 미할당을 <b>한 번만</b> 알린다 (S15P21A606-47).
        /// </summary>
        /// <remarks>
        /// <see cref="OnSceneLoadDone"/> 가 참가자 수만큼 돌기 때문에, 매번 찍으면 같은 줄이
        /// 로그와 디버그 오버레이를 가득 채워 정작 원인이 되는 다른 줄을 밀어낸다.
        /// </remarks>
        private static void ReportMissingPrefabOnce(ref bool reported, string fieldName, string expectedPrefab)
        {
            if (reported)
            {
                return;
            }

            reported = true;
            Debug.LogError(
                $"[NetworkManager] {fieldName} 이 비어 있어 스폰하지 못했습니다. " +
                $"[Managers] 프리팹의 NetworkManager 에 {expectedPrefab} 을 연결하세요.");
        }

        private void EnsurePlayerInfo(NetworkRunner r, PlayerRef player)
        {
            if (!r.IsServer)
            {
                return;
            }

            if (playerInfoPrefab == null)
            {
                ReportMissingPrefabOnce(ref playerInfoPrefabReported, nameof(playerInfoPrefab), "Net_PlayerInfo");
                return;
            }

            for (int i = 0; i < playerInfos.Count; i++)
            {
                if (IsUsable(playerInfos[i]) && playerInfos[i].Owner == player)
                {
                    return;
                }
            }

            // 방장인지 여기서 판정해 [Networked] 에 박아둔다. Host 모드에서는 Host 자신이고,
            // 전용 서버에서는 서버가 정한 방장이다 (S15P21A606-321, S-ONL-FAG9B8).
            // 클라이언트는 "누가 방장인지" 를 알 방법이 없으므로 복제해줘야 한다.
            bool isHost = ResolveRoomOwnerFlag(r, player);
            r.Spawn(
                prefab: playerInfoPrefab,
                inputAuthority: player,
                onBeforeSpawned: (_, obj) =>
                {
                    var info = obj.GetComponent<PlayerInfo>();
                    if (info != null)
                    {
                        info.IsHost = isHost;
                    }
                });
        }

        /// <summary>이번 호출에 세울 전투원 수의 기본 상한.</summary>
        /// <remarks>
        /// ⚠️ <b>8기를 한 프레임에 세우면 그 프레임이 매치를 망가뜨린다.</b> 실측(2026-09-22):
        /// 8기 스폰이 한 프레임에 <c>spawnMs 60 + instMs 143 = 203ms</c> 를 썼고, 그 프레임이
        /// 호스트 창의 <b>246.9ms</b> 였다. 같은 창에서 client 의 확정 서버틱이 0.25초 단위로
        /// 뭉쳐 오기 시작했고(<c>stateRecvDelta</c> 0.033 → 0.247), 클라 로컬틱이 예측 천장
        /// (확정틱 +64)에 박혔다.
        /// <para>
        /// ⚠️ <b>천장은 저절로 풀리지 않는다.</b> Fusion 의 <c>SimulationSpeed</c> 실측 최대가
        /// <b>1.014</b> 라 클라는 1초에 <b>14ms</b> 씩만 리드를 태운다 — 1초치 리드를 되돌리는 데
        /// 70초가 걸린다. 그래서 <b>프레임 하나의 크기보다 창 전체의 초과 시간 합계가 중요하다.</b>
        /// </para>
        /// 부르는 곳이 전부 매 프레임·주기 재시도 루프라 나눠 세워도 빠지는 전투원은 없다
        /// (<c>OnlineCoreLoopWorldPresenter.ApplySnapshot</c> 의 <c>placementPending</c>,
        /// <c>OnlineCoreLoopCoordinator.TickCombatantSpawnWait</c>).
        /// </remarks>
        internal const int DefaultSpawnBudgetPerCall = 1;

        /// <summary>
        /// 아직 안 선 전투원을 세운다. <paramref name="spawnBudget"/> 개까지만 세우고 나머지는
        /// 다음 호출로 미룬다 (D-CLI-B47KX7).
        /// </summary>
        internal void EnsureProductCombatants(int spawnBudget = DefaultSpawnBudgetPerCall)
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer) return;
            if (spawnBudget < 1) spawnBudget = 1;

            foreach (PlayerRef player in runner.ActivePlayers)
            {
                if (EnsureCombatant(runner, player)) spawnBudget--;
                if (spawnBudget <= 0) return;
            }

            // 봇도 같은 순간에 세운다. 사람만 세우고 봇을 미루면 배점 인원과 실제 전장이 어긋난 채
            // 라운드가 시작된다 (S-ONL-9GXTYQ).
            //
            // ⚠️ "같은 순간" 은 <b>같은 프레임</b>이 아니라 <b>같은 대기 구간</b>이다 — 라운드는
            //    `placementPending` 이 풀려야 열리고, 그때까지 전원이 서 있어야 한다는 계약은
            //    그대로다. 나눠 세워도 명부와 전장은 어긋나지 않는다.
            EnsureBotCombatants(spawnBudget);
        }

        internal void EnsureTrainingNpc(int index)
        {
            ITrainingWorldRuntime world = TrainingWorldRuntimeRegistry.Current;
            if (!IsTrainingRoom || !IsHost || combatantPrefab == null || index < 1 || index > 9
                || world == null || !world.IsReady) return;
            foreach (NetworkCombatantMotion actor in spawnedCombatants)
                if (actor != null && actor.Object != null && actor.Object.IsValid && actor.TrainingNpcIndex == index) return;
            if (!world.TryGetSpawn(index, 0, out WorldPosition position)) return;
            runner.Spawn(combatantPrefab, new Vector3(position.X, position.Y, position.Z), Quaternion.identity,
                PlayerRef.None, (_, obj) =>
                {
                    var motion = obj.GetComponent<NetworkCombatantMotion>();
                    motion.ReplicatedTrainingNpcIndex = index;
                    motion.SetInitialAppearancePresetSeed(97 + index);
                });
        }

        /// <summary>
        /// 훈련장 NPC 하나를 걷어낸다 (S15P21A606-188). 없으면 아무 일도 하지 않는다.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>스폰한 쪽이 치운다.</b> Host 가 세운 NetworkObject 를 Host 가 없애지 않으면 유령
        /// 오브젝트가 남아 다음 조회에서 "이미 있다" 로 읽힌다 — 봇 수를 줄여도 줄지 않는다.
        /// </remarks>
        internal void RemoveTrainingNpc(int index)
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer || index < 1) return;

            for (var i = spawnedCombatants.Count - 1; i >= 0; i--)
            {
                NetworkCombatantMotion actor = spawnedCombatants[i];
                if (actor == null || actor.Object == null || !actor.Object.IsValid) continue;
                if (actor.TrainingNpcIndex != index) continue;
                runner.Despawn(actor.Object);
            }
        }

        internal IReadOnlyList<NetworkCombatantMotion> TrainingActors => spawnedCombatants;

        /// <summary>
        /// 매치 명부에 얼린 봇마다 전투원을 세운다 (S-ONL-9GXTYQ · S15P21A606-188).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>훈련장 NPC 와 같은 모양이다</b>(<see cref="EnsureTrainingNpc"/>) — 소유자 없는
        /// (<c>PlayerRef.None</c>) NetworkObject 를 Host 가 세우고 Host 가 입력을 만든다. 다른 점은
        /// 자리 배치를 제품 전장이 정한다는 것뿐이다.
        /// </para>
        /// <para>
        /// ⚠️ <b>멱등이다.</b> 씬 로드 완료·라운드 전환마다 불릴 수 있고, 이미 선 봇은 건너뛴다.
        /// 여기서 중복 스폰하면 명부에 없는 전투원이 생겨 배점 인원과 실제가 어긋난다.
        /// </para>
        /// </remarks>
        private void EnsureBotCombatants(int spawnBudget)
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer) return;
            if (IsTrainingRoom || matchBotNumbers.Count == 0 || !IsCombatSceneActive()) return;
            if (combatantPrefab == null)
            {
                ReportMissingPrefabOnce(ref combatantPrefabReported, nameof(combatantPrefab), "Net_Combatant");
                return;
            }

            for (var i = 0; i < matchBotNumbers.Count; i++)
            {
                int number = matchBotNumbers[i];
                if (HasBotCombatant(number))
                {
                    // 스폰한 틱에는 Spawned() 가 아직 돌지 않아 장비를 들릴 수 없다 — 선 뒤의 보장 루프에서 들려 준다.
                    RetryBotWeaponChoice(number);
                    continue;
                }
                if (!OnlineCoreLoopWorldPresenter.TryResolveInitialSpawn(
                        NetworkCombatantMotion.LobbyBotActorId(number), out Pose pose))
                {
                    // ⚠️ 조용히 넘기지 않는다. 처음 만들 때 여기서 그냥 continue 했더니 명부에는
                    //    봇 7기가 있는데 전장에는 아무도 서지 않았고 **로그가 한 줄도 없었다**.
                    //    다음 호출에서 다시 시도하는 것은 맞지만, 그 사실이 보여야 한다.
                    ReportBotSpawnDeferredOnce(number);
                    continue;
                }

                botSpawnDeferredReported.Remove(number);

                int botNumber = number;
                long botSpawnTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                runner.Spawn(
                    prefab: combatantPrefab,
                    position: pose.position,
                    rotation: pose.rotation,
                    inputAuthority: PlayerRef.None,
                    onBeforeSpawned: (_, obj) =>
                    {
                        var motion = obj.GetComponent<NetworkCombatantMotion>();
                        if (motion == null) return;
                        motion.ReplicatedLobbyBotNumber = botNumber;
                        // 외형은 봇 번호에서 결정한다 — 사람의 계정 프리셋과 겹치지 않는 대역을 쓴다.
                        motion.SetInitialAppearancePresetSeed(131 + botNumber);
                    });
                AddInstantiateCost(botSpawnTicks);

                // 이번 프레임 몫을 다 썼다. 나머지는 다음 호출이 세운다.
                if (--spawnBudget <= 0) return;
            }
        }

        /// <summary>
        /// 매치 명부에서 확정한 봇 무기를 아직 들려 주지 못한 봇에게만 들려 준다 (S-GD-XJ4WMT). 들려 준 봇은 명부가 걸러내므로
        /// 매치 도중 바뀐 장비를 되돌리지 않는다 — 사람의 <see cref="RetryLobbyWeaponChoice"/> 와 같은 규칙이다.
        /// </summary>
        private void RetryBotWeaponChoice(int number)
        {
            int botId = NetworkCombatantMotion.LobbyBotActorId(number);
            if (!matchWeaponChoices.NeedsApply(botId)) return;
            if (!matchWeaponChoices.TryGet(botId, out LobbyWeaponChoice choice)
                || !LobbyWeaponChoiceRules.TryGetWeaponType(choice, out WeaponType weapon)) return;
            if (!TryGetCombatant(botId, out NetworkCombatantMotion motion) || motion == null) return;
            if (motion.EquipMatchWeapon(weapon)) matchWeaponChoices.MarkApplied(botId);
        }

        /// <summary>봇마다 한 번만 알린다. 매 프레임 불리는 경로라 그대로 찍으면 로그가 잠긴다.</summary>
        private readonly HashSet<int> botSpawnDeferredReported = new();

        private void ReportBotSpawnDeferredOnce(int number)
        {
            if (!botSpawnDeferredReported.Add(number)) return;
            Debug.LogWarning(
                $"[NetworkManager] 봇 {number} 의 전장 자리를 아직 받지 못해 스폰을 미룹니다. " +
                "월드가 준비되면 다시 시도합니다 — 계속 남아 있으면 매치 명부와 배치 목록이 어긋난 것입니다.");
        }

        private bool HasBotCombatant(int number)
        {
            for (var i = 0; i < spawnedCombatants.Count; i++)
            {
                NetworkCombatantMotion actor = spawnedCombatants[i];
                if (actor != null && actor.Object != null && actor.Object.IsValid
                    && actor.LobbyBotNumber == number) return true;
            }

            return false;
        }

        /// <summary>이 참가자의 전투원을 보장한다. <b>이번 호출에서 새로 세웠으면 true.</b></summary>
        private bool EnsureCombatant(NetworkRunner r, PlayerRef player)
        {
            if (!r.IsServer || !IsCombatSceneActive())
            {
                return false;
            }

            // ⚠️ 프리팹 미할당을 조용히 넘기지 않는다 (S15P21A606-47).
            //    예전에는 여기서 그냥 return 해서, 전투 씬에 들어가도 아무도 스폰되지 않는데
            //    **콘솔에 한 줄도 나오지 않았다.** 빌드에는 Hierarchy 도 없어 원인을 찾을 수 없다.
            if (combatantPrefab == null)
            {
                ReportMissingPrefabOnce(ref combatantPrefabReported, nameof(combatantPrefab), "Net_Combatant");
                return false;
            }

            if (r.TryGetPlayerObject(player, out NetworkObject existing)
                && existing != null
                && existing.IsValid)
            {
                // ⚠️ 여기서 그냥 돌아가면 **무기를 들려 줄 기회가 매치당 한 번뿐**이다.
                //    그 한 번이 Spawned() 가 아직 돌지 않은 틱에 걸리면 장비 드라이버가 없어
                //    장착이 실패하고, 복구 경로가 없어 검을 든 채 전투에 들어간다.
                //    명부에 미적용으로 남아 있을 때만 다시 시도한다 — 이미 들려 준 사람은
                //    건드리지 않으므로 매치 도중 바뀐 장비를 되돌리지 않는다.
                RetryLobbyWeaponChoice(player, existing);
                return false;
            }

            if (!TryGetAppearancePresetSeed(player, out int appearancePresetSeed))
            {
                if (IsTrainingRoom) return false;
                Debug.LogError(
                    $"[NetworkManager] Player {player.PlayerId}의 계정 외형 프리셋이 없어 " +
                    "전투 오브젝트를 스폰하지 않았습니다. Ready 게이트와 PlayerInfo 등록을 확인하세요.");
                return false;
            }

            Scene productScene = SceneManager.GetSceneByName(SceneName.InGame);
            Vector3? spawnPosition = null;
            Quaternion? spawnRotation = null;
            if (IsTrainingRoom)
            {
                ITrainingWorldRuntime world = TrainingWorldRuntimeRegistry.Current;
                if (world == null || !world.IsReady || !world.TryGetSpawn(0, player.PlayerId, out WorldPosition position)) return false;
                spawnPosition = new Vector3(position.X, position.Y, position.Z);
                spawnRotation = Quaternion.identity;
            }
            else if (productScene.IsValid() && productScene.isLoaded)
            {
                if (!OnlineCoreLoopWorldPresenter.TryResolveInitialSpawn(player.PlayerId, out Pose pose)) return false;
                spawnPosition = pose.position;
                spawnRotation = pose.rotation;
            }
            long playerSpawnTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            NetworkObject combatant = r.Spawn(
                prefab: combatantPrefab,
                position: spawnPosition,
                rotation: spawnRotation,
                inputAuthority: player,
                onBeforeSpawned: (_, obj) =>
                {
                    var motion = obj.GetComponent<NetworkCombatantMotion>();
                    if (motion != null)
                    {
                        motion.SetInitialAppearancePresetSeed(appearancePresetSeed);
                        // 저장 외형이 이미 도착했으면 seed 와 함께 싣는다. 늦게 오면 RegisterAppearancePresets 가 덧붙인다.
                        if (TryGetAppearancePresets(player, out var savedPresets)) motion.SetAppearancePresets(savedPresets);
                    }
                });
            AddInstantiateCost(playerSpawnTicks);
            if (combatant != null)
            {
                r.SetPlayerObject(player, combatant);
                ApplyLobbyWeaponChoice(player, combatant);
                return true;
            }

            Debug.LogError(
                $"[NetworkManager] Player {player.PlayerId} 전투 오브젝트 스폰에 실패했습니다. " +
                "NetworkPrefabTable 등록을 확인하세요.");
            return false;
        }

        /// <summary>
        /// 전투 NetworkObject는 실제 이동·충돌 환경이 있는 씬에서만 만든다.
        /// </summary>
        /// <remarks>
        /// 참가 콜백은 타이틀·로비에서도 오지만 그 씬에는 경기장 경계가 없다. 그때 미리 스폰하면
        /// 전투 컴포넌트가 불완전한 환경에서 초기화되고, 어차피 Single 씬 전환에서 다시 파괴된다.
        /// 전투 씬 전환 완료 뒤 <see cref="OnSceneLoadDone"/>가 현재 참가자 전원을 다시 보장한다.
        /// </remarks>
        private static bool IsCombatSceneActive()
        {
            Scene productScene = SceneManager.GetSceneByName(SceneName.InGame);
            return (productScene.IsValid() && productScene.isLoaded)
                || SceneManager.GetSceneByName(SceneName.Training).isLoaded
                || SceneManager.GetActiveScene().name == SceneName.CombatSandbox;
        }

        /// <summary>
        /// 매치 시작 시점에 Host 가 얼려 둔 참가자별 무기 선택 (S-UX-NDANX2 · S-GD-29RMAQ).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>복제값만으로는 부족하다.</b> 원본인 <c>PlayerInfo.WeaponChoice</c> 는 매치 씬 전환에서
        /// 오브젝트째 사라지고, <see cref="EnsurePlayerInfo"/> 가 새로 스폰한 인스턴스의 값은
        /// 복제 기본값 0(= 검)이다. 활을 고른 사람이 전투에서 검을 들던 원인이 이것이다.
        /// </para>
        /// <para>
        /// 그래서 <see cref="appearancePresetSeedsByPlayer"/> 와 <b>같은 처방</b>을 쓴다 —
        /// 씬을 넘어 살아야 하는 값은 복제 오브젝트가 아니라 Host 의 기억에 둔다.
        /// </para>
        /// </remarks>
        private readonly MatchWeaponChoiceRoster matchWeaponChoices = new MatchWeaponChoiceRoster();

        /// <summary>
        /// 지금 방에 있는 사람들의 무기 선택을 매치 명부로 얼린다. <b>Host 가 매치를 시작할 때 한 번</b> 부른다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="CaptureMatchRoster"/> 와 같은 시점·같은 이유다 — 이 값들은 매치가 끝날 때까지
        /// 고정돼야 하는데, 그때는 이미 <see cref="PlayerInfo"/> 가 씬 전환에 쓸려 사라진 뒤다.
        /// </para>
        /// <para>
        /// <b>여기서 '랜덤' 을 확정한다</b>(S-GD-29RMAQ). 확정 시점은 그대로 매치 시작이며,
        /// 뒤로 미루지 않는다 — 준비 단계의 축복 후보를 무엇으로 거를지가 이 값에 달려 있다
        /// (S-GD-SB84DQ). <see cref="PlayerInfo.DecideWeaponChoiceForMatch"/> 는 두 번 불려도
        /// 결과가 바뀌지 않으므로, <c>RoomMatchStarted</c> 구독 순서와 무관하게 같은 값이 나온다.
        /// </para>
        /// </remarks>
        /// <returns>명부에 담긴 인원.</returns>
        internal int CaptureMatchWeaponChoices()
        {
            matchWeaponChoices.Clear();

            if (runner == null || !runner.IsRunning || !runner.IsServer)
            {
                return 0;
            }

            foreach (PlayerRef player in runner.ActivePlayers)
            {
                if (!TryFindPlayerInfo(player, out PlayerInfo info))
                {
                    Debug.LogWarning(
                        $"[NetworkManager] Player {player.PlayerId} 의 PlayerInfo 가 없어 무기 선택을 얼리지 못했습니다. "
                        + "그 참가자는 프리팹 기본 무기로 전투에 들어갑니다 (S-UX-NDANX2).");
                    continue;
                }

                matchWeaponChoices.Capture(
                    player.PlayerId,
                    info.DecideWeaponChoiceForMatch(UnityEngine.Random.Range(0, 2)));
            }

            // 대기방 봇은 PlayerInfo 가 없어 위 고리에 걸리지 않는다. 예전에는 명부에서 빠져 축복 후보 필터·봇 성향·손에 든 무기가
            // 모두 기본값 검이 됐다(2026-09-28 "봇은 랜덤 무기여야 하는데 검으로 고정"). 봇의 "랜덤" 을 매치·봇마다 정해진 값으로
            // 확정해 같은 명부에 담는다 — 사람과 같은 길로 축복 필터와 전투 장착이 읽는다(S-GD-XJ4WMT). 봇 명부는 바로 위
            // CaptureMatchRoster 가 같은 프레임에 채웠다.
            string matchId = OnlineCoreLoopCoordinator.CurrentMatchId;
            for (int i = 0; i < matchBotNumbers.Count; i++)
            {
                int botId = NetworkCombatantMotion.LobbyBotActorId(matchBotNumbers[i]);
                matchWeaponChoices.Capture(botId, LobbyWeaponChoiceRules.DecideBotWeapon(matchId, botId));
            }

            Debug.Log($"[NetworkManager] 매치 무기 선택을 얼렸습니다. {matchWeaponChoices.Count}명"
                + (matchBotNumbers.Count > 0 ? $"(봇 {matchBotNumbers.Count}기 포함)." : "."));
            return matchWeaponChoices.Count;
        }

        /// <summary>
        /// 확정된 무기 선택 하나를 매치 명부에 담는다. <see cref="PlayerInfo.OnRoomMatchStarted"/> 가 부른다.
        /// </summary>
        /// <remarks>
        /// <see cref="CaptureMatchWeaponChoices"/> 를 거치지 않는 경로(훈련장·샌드박스)에서도
        /// 명부가 채워지게 하는 짝이다. 같은 값을 두 번 담아도 결과가 같다.
        /// </remarks>
        internal void RegisterMatchWeaponChoice(PlayerRef player, LobbyWeaponChoice choice)
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer) return;
            if (matchWeaponChoices.TryGet(player.PlayerId, out LobbyWeaponChoice existing)
                && existing == LobbyWeaponChoiceRules.Normalize(choice))
            {
                // 이미 같은 값으로 담겨 있다. 다시 담으면 '적용함' 표시까지 지워져 장착을 또 시도한다.
                return;
            }

            matchWeaponChoices.Capture(player.PlayerId, choice);
        }

        /// <summary>
        /// 대기방에서 사람이 고른 <b>원본</b> 선택을 Host 의 기억에 남긴다 (S15P21A606-226).
        /// </summary>
        /// <remarks>
        /// <c>랜덤</c> 도 그대로 담는다 — 확정은 매치 시작에 하고, 여기 담는 것은 <b>사람이 무엇을
        /// 눌렀는가</b>이기 때문이다. 확정값을 담으면 다음 판에서 랜덤이 굳는다.
        /// </remarks>
        internal void RememberLobbyWeaponChoice(PlayerRef player, LobbyWeaponChoice choice)
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer) return;
            lobbyWeaponChoicesByPlayer[player] = LobbyWeaponChoiceRules.Normalize(choice);
        }

        /// <summary>
        /// 새로 스폰된 <see cref="PlayerInfo"/> 에 지난 대기방 선택을 되살린다 (S15P21A606-226).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>왜 필요한가.</b> 매치 씬을 다녀오면 이 오브젝트는 새 인스턴스이고 <c>WeaponChoice</c> 는
        /// 복제 기본값 <b>0(= 검)</b> 이다. 그 0 이 화면으로 흘러가 활을 골랐던 사람이 검으로
        /// 되돌아간 것처럼 보였다 — <c>LobbyWeaponChoice</c> 주석이 경고하던 "0 은 '검' 과
        /// '아직 안 들어옴' 을 구분하지 못한다" 가 로비 표시 경로에서 그대로 터진 것이다.
        /// </para>
        /// <para>
        /// 기억이 없으면 아무것도 하지 않는다 — 처음 들어온 사람의 기본값(검)을 덮지 않기 위해서다.
        /// </para>
        /// </remarks>
        internal bool TryRestoreLobbyWeaponChoice(PlayerRef player, out LobbyWeaponChoice choice)
        {
            return lobbyWeaponChoicesByPlayer.TryGetValue(player, out choice);
        }

        /// <summary>
        /// 대기방에서 고른 무기를 스폰한 전투 오브젝트에 실제로 들려 준다 (S-UX-NDANX2).
        /// </summary>
        /// <remarks>
        /// ⚠️ 이게 없으면 <b>선택은 보이는데 아무 효과가 없다</b> — 대기방 배지만 활로 바뀌고
        /// 손에는 계속 검이 들린다. 무기는 축복 후보를 거르는 입력이라(S-GD-SB84DQ) 어긋난 채로
        /// 두면 활 축복을 뽑아 놓고 검을 드는 상태가 된다.
        ///
        /// <c>Random</c> 은 여기서 처리하지 않는다. Host 가 매치 시작에 확정하며
        /// (<see cref="CaptureMatchWeaponChoices"/>), 확정 전이면 <c>TryGetWeaponType</c> 이 false 를 돌려
        /// 프리팹 기본 무기가 그대로 남는다 — 조용히 검을 집어들지 않게 하려는 것이다.
        /// </remarks>
        private void ApplyLobbyWeaponChoice(PlayerRef player, NetworkObject combatant)
        {
            if (!TryGetWeaponChoice(player, out LobbyWeaponChoice choice))
            {
                // ⚠️ 예전에는 여기서 조용히 돌아갔다. 그래서 "활을 골랐는데 검을 든다" 를
                //    콘솔에서 확인할 방법이 아예 없었다 — 침묵을 성공으로 읽지 않는다(D-026).
                Debug.LogWarning(
                    $"[NetworkManager] Player {player.PlayerId} 의 대기방 무기 선택을 찾지 못했습니다. "
                    + "프리팹 기본 무기로 진행합니다 (S-UX-NDANX2).");
                return;
            }

            if (!LobbyWeaponChoiceRules.TryGetWeaponType(choice, out WeaponType weapon))
            {
                Debug.LogWarning(
                    $"[NetworkManager] Player {player.PlayerId} 의 무기 선택이 아직 확정되지 않았습니다({choice}). "
                    + "프리팹 기본 무기로 진행합니다 (S-GD-29RMAQ).");
                return;
            }

            var motion = combatant.GetComponent<NetworkCombatantMotion>();
            if (motion != null && motion.EquipMatchWeapon(weapon))
            {
                // 성공한 사람은 다시 손대지 않는다. 매치 도중 바뀐 장비를 되돌리지 않기 위해서다.
                matchWeaponChoices.MarkApplied(player.PlayerId);
                return;
            }

            Debug.LogWarning(
                $"[NetworkManager] Player {player.PlayerId} 의 선택 무기 {weapon} 를 장착하지 못했습니다. "
                + "다음 보장 루프에서 다시 시도합니다.");
        }

        /// <summary>
        /// 아직 무기를 들려 주지 못한 전투 오브젝트에만 다시 시도한다.
        /// </summary>
        /// <remarks>
        /// <c>Runner.Spawn</c> 이 시뮬레이션 밖에서 불리면 <c>Spawned()</c> 가 다음 틱으로 밀려
        /// 장비 드라이버가 아직 없다. 그 틱에 걸린 참가자에게 <b>복구 기회를 준다</b> —
        /// 이미 들려 준 사람은 명부가 걸러내므로 장비를 덮어쓰지 않는다.
        /// </remarks>
        private void RetryLobbyWeaponChoice(PlayerRef player, NetworkObject combatant)
        {
            if (!matchWeaponChoices.NeedsApply(player.PlayerId)) return;
            ApplyLobbyWeaponChoice(player, combatant);
        }

        /// <summary>
        /// 그 참가자가 이번 매치에 쓸 무기. 매치 명부를 먼저 보고, 없으면 살아 있는
        /// <see cref="PlayerInfo"/> 의 복제값을 본다. 둘 다 없으면 false 다 —
        /// 호출자가 "모른다" 와 "검을 골랐다" 를 구분할 수 있어야 한다.
        /// </summary>
        /// <remarks>
        /// <b>순서가 중요하다.</b> 씬 전환 뒤의 <see cref="PlayerInfo"/> 는 새로 스폰된 빈 인스턴스라
        /// 복제값이 0(= 검)으로 돌아와 있다. 명부를 먼저 보지 않으면 그 0 이 선택을 덮어쓴다.
        /// </remarks>
        internal bool TryGetWeaponChoice(PlayerRef player, out LobbyWeaponChoice choice)
        {
            return TryGetWeaponChoice(player.PlayerId, out choice);
        }

        /// <inheritdoc cref="TryGetWeaponChoice(PlayerRef, out LobbyWeaponChoice)"/>
        internal bool TryGetWeaponChoice(int playerId, out LobbyWeaponChoice choice)
        {
            if (matchWeaponChoices.TryGet(playerId, out choice))
            {
                return true;
            }

            for (int i = 0; i < playerInfos.Count; i++)
            {
                PlayerInfo info = playerInfos[i];
                if (!IsUsable(info) || info.Owner.PlayerId != playerId) continue;
                choice = LobbyWeaponChoiceRules.Normalize(info.WeaponChoice);
                return true;
            }

            choice = LobbyWeaponChoiceRules.Default;
            return false;
        }

        private bool TryGetAppearancePresetSeed(PlayerRef player, out int seed)
        {
            if (appearancePresetSeedsByPlayer.TryGetValue(player, out seed) && seed > 0)
            {
                return true;
            }

            for (int i = 0; i < playerInfos.Count; i++)
            {
                PlayerInfo info = playerInfos[i];
                if (IsUsable(info) && info.Owner == player && info.AppearancePresetSeed > 0)
                {
                    seed = info.AppearancePresetSeed;
                    appearancePresetSeedsByPlayer[player] = seed;
                    return true;
                }
            }

            seed = default;
            return false;
        }

        public void OnInputMissing(NetworkRunner r, PlayerRef player, NetworkInput input) { }
        /// <summary>
        /// 클라이언트가 Host/서버에 붙었다. <b>이 피어에서 유일하게 연결 방식을 알 수 있는 시점</b>이라
        /// 여기서 진단을 남긴다 (Host 쪽은 <see cref="OnPlayerJoined"/> 가 담당한다).
        /// </summary>
        public void OnConnectedToServer(NetworkRunner r)
        {
            StartCoroutine(DiagnoseConnectionRoutine("서버 접속"));
        }
        /// <summary>
        /// 참가 요청을 받아들일지 Host 가 정한다 (S-UX-BXXTHX "잘못된 비밀번호는 Host 가 거부한다").
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>언제나 명시적으로 답한다.</b> 예전에는 이 콜백이 비어 있었고, 그때는 Fusion 의
        /// 기본 동작이 모든 요청을 받아들였다. 심사가 생긴 지금 비워 두면 <b>잠긴 방이 잠기지
        /// 않는다</b> — 조건을 하나 빠뜨린 것이 아니라 아무 일도 하지 않은 것이 통과가 된다.
        /// </para>
        /// <para>
        /// 판정은 <see cref="LobbyRoomAdmission"/> 하나뿐이다. 잠기지 않은 방(기존 방·훈련장
        /// 전부)은 토큰을 보지 않고 받아들이므로 기존 동작이 그대로 유지된다.
        /// </para>
        /// <para>
        /// ⚠️ <b>지금 우리가 소유한 Runner 의 요청만 심사한다.</b> 심사값은 <c>NetworkManager</c> 의
        /// 필드라서, 버린 Runner 의 요청을 그대로 처리하면 <b>새 방의 검증값으로 옛 방 손님을
        /// 받아들이게</b> 된다(잠기지 않은 새 방이면 무조건 통과다). 그런 요청은 무조건 거부한다 —
        /// 진짜 참가자는 새 Runner 로 다시 붙는다. 별도의 Host 여부 검사는 두지 않는다:
        /// Fusion 은 이 콜백을 서버에서만 올리고, 여기서 <c>IsServer</c> 가 아직 세워지지 않은
        /// 시점을 가정하면 정상 참가자를 거부할 수 있다.
        /// </para>
        /// <para>
        /// ⚠️ <b>거부 로그에 토큰을 남기지 않는다.</b> 거부된 값도 누군가의 입력이고,
        /// Player.log 는 공유된다.
        /// </para>
        /// </remarks>
        public void OnConnectRequest(NetworkRunner r, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
        {
            if (r == null || !ReferenceEquals(r, runner))
            {
                Debug.LogWarning("[NetworkManager] 현재 세션의 요청이 아니어서 참가를 거부했습니다.");
                request.Refuse();
                return;
            }

            if (roomAdmission.IsAdmitted(token))
            {
                request.Accept();
                return;
            }

            // 사유를 좁혀 말하지 않는다 — 토큰이 맞지 않는 경우와 규약이 다른 옛 빌드가 붙은 경우를
            // 여기서 구분할 수 없다.
            Debug.LogWarning("[NetworkManager] 입장 심사를 통과하지 못해 참가 요청을 거부했습니다.");
            request.Refuse();
        }
        // SimulationMessagePtr 은 Fusion 2.1.1 에서 [Obsolete] 이지만
        // 인터페이스 멤버라 구현은 여전히 필요하다. 경고만 끈다.
#pragma warning disable CS0618
        public void OnUserSimulationMessage(NetworkRunner r, SimulationMessagePtr message) { }
#pragma warning restore CS0618
        public void OnCustomAuthenticationResponse(NetworkRunner r, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner r, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(
            NetworkRunner r,
            PlayerRef player,
            ReliableKey key,
            ReadOnlySpan<byte> data)
        {
            key.GetInts(out int magic, out int sequence, out _, out _);

            // ⚠️ 규약을 가르기 **전에** 센다. 아래 분기 중 하나라도 조용히 return 하면 "안 왔다" 와
            //    "와서 버려졌다" 가 구별되지 않는다. 받은 사실 자체는 무조건 남긴다.
            ReliableReceiveCount++;
            if (magic == EconomyStateProtocol.Magic)
            {
                ReliableEconomyStateReceiveCount++;
            }

            if (sequence <= 0)
            {
                return;
            }

            if (magic == TrainingWireProtocol.CommandMagic)
            {
                if (IsTrainingRoom && r.IsServer && TrainingWireProtocol.TryReadRequest(data.ToArray(), out TrainingActionRequest request))
                    TrainingSessionCoordinator.ReceiveCommand(player.PlayerId, request);
                return;
            }
            if (magic == TrainingWireProtocol.StateMagic)
            {
                // ── 송신자 신원으로 막지 않는다 (S15P21A606-190) ──
                //
                // ⚠️ 예전에는 여기에 `IsKnownTrainingHost(player)` 가 한 칸 더 있었다. 그런데
                //    클라이언트가 받는 신뢰 데이터는 **서버에서 온 것뿐이다** — 클라는 서버로만
                //    보낼 수 있다. 그래서 이 저장소의 다른 Host→Client 통로(경제 'ESTA', 방 설정,
                //    코어루프)는 전부 `!r.IsServer` 하나로 판정한다. 훈련 상태만 검사를 더 달았고,
                //    그 한 칸이 false 로 굳으면 **로그 한 줄 없이 스냅샷을 전부 버린다.**
                //
                //    그 결과가 이 티켓이다 — 비방장 참가자는 상태를 못 받아 sessionId 가 비고,
                //    sessionId 가 비면 명령을 만들지도 못해 연습 메뉴 전체가 빈 껍데기가 된다.
                //    Host 는 자기 상태를 이 채널이 아니라 메모리로 직접 받으므로(SendState 의
                //    LocalTrainingPlayerId 분기) **Host 화면만 멀쩡해 보여** 원인이 드러나지 않았다.
                //
                //    위조 방어를 잃는 것이 아니다. ReceiveState 가 내 PlayerId 의 상태가 아니면
                //    버리고, 세션 id 가 다르거나 version 이 역행해도 버린다. Host Mode 의 신뢰
                //    경계는 D-030·O-002 그대로이며 이 한 칸이 치트를 막고 있던 것이 아니다.
                if (IsTrainingRoom && !r.IsServer
                    && TrainingWireProtocol.TryReadState(data.ToArray(), out TrainingSessionView state))
                    TrainingSessionCoordinator.ReceiveState(state);
                return;
            }

            // ── 방장 요청 (S15P21A606-321) — 전용 서버에서 방장도 클라이언트일 때의 통로 ──
            if (magic == RoomOwnerWireProtocol.CommandMagic)
            {
                if (r.IsServer && RoomOwnerWireProtocol.TryDeserialize(data, out RoomOwnerCommand ownerCommand))
                {
                    ApplyRoomOwnerCommand(player, ownerCommand);
                }

                return;
            }

            // ── 대기방 봇 (S-ONL-9GXTYQ) ──
            if (magic == LobbyBotWireProtocol.CommandMagic)
            {
                // 클라이언트는 남의 명령을 받을 일이 없다. 권한 판정은 ApplyBotCommand 가 한다 —
                // 여기서 미리 거르면 "권한 없음" 과 "통로 오류" 가 같은 침묵이 된다.
                if (r.IsServer && LobbyBotWireProtocol.TryDeserializeCommand(data, out LobbyBotCommand command))
                {
                    ApplyBotCommand(player.PlayerId, command);
                }

                return;
            }

            if (magic == LobbyBotWireProtocol.StateMagic)
            {
                // 클라이언트가 받는 신뢰 데이터는 서버에서 온 것뿐이다 — 훈련 상태와 같은 판정이다.
                if (!r.IsServer
                    && LobbyBotWireProtocol.TryDeserializeState(
                        data, out int hostPlayerId, out List<LobbyBotSlot> slots, out List<int> grants))
                {
                    botRoster.ApplySnapshot(slots, grants);
                    replicatedBotHostPlayerId = hostPlayerId;
                    PublishBots();
                }

                return;
            }

            // ── Client → Host: 준비 단계 조작 요청 (S15P21A606-26) ──
            //
            // ⚠️ 이 저장소에서 Client 가 Host 로 보내는 첫 신뢰 데이터다. 그 전까지는 아래 한 줄이
            //    "Host 면 무조건 버린다" 였다 — 상태를 발행하는 쪽이 Host 하나뿐이었으므로 그것으로 충분했다.
            //    이제 방향이 둘이 됐으므로 **매직으로 먼저 가른다**. 순서를 뒤집어 "Host 면 버린다" 를
            //    앞에 두면 명령이 영원히 도착하지 않는다.
            if (magic == EconomyCommandProtocol.Magic)
            {
                // Client 는 남의 명령을 받을 일이 없다. 받았다면 무시한다.
                //
                // ⚠️ 두 조건을 `||` 로 합치지 않는다. 단축 평가로 TryDeserialize 가 건너뛰어지면
                //    out 변수가 확정 대입되지 않아 컴파일이 막힌다 (CS0165).
                if (!r.IsServer)
                {
                    return;
                }

                if (!EconomyCommandProtocol.TryDeserialize(data, out EconomyCommandRequest request))
                {
                    return;
                }

                // 보낸 사람은 **연결에서** 판정한다. 페이로드에 적힌 값을 믿지 않는다 (규약 §5).
                EventBus.Publish(new EconomyCommandReceived(player.PlayerId, request));
                return;
            }

            if (magic == MatchLoadingProgressProtocol.Magic)
            {
                if (r.IsServer && MatchLoadingProgressProtocol.TryDeserialize(data, out byte percent))
                    EventBus.Publish(new MatchLoadingProgressReceived(player.PlayerId, percent));
                return;
            }

            if (magic == CombatEntryReadyProtocol.Magic)
            {
                // 보낸 사람은 연결에서 판정한다. 라운드는 늦게 온 지난 라운드 보고를 거르는 데만 쓴다.
                if (r.IsServer && CombatEntryReadyProtocol.TryDeserialize(data, out byte round))
                    OnlineCoreLoopCoordinator.HostReceiveCombatEntryReady(player.PlayerId, round);
                return;
            }

            // ── Host → Client: 상태 복제 ──
            // 아래 것들은 Host만 발행한다. Host에서 Client가 보낸 같은 키를 받아도 무시한다.
            if (r.IsServer)
            {
                return;
            }

            if (magic == EconomyStateProtocol.Magic)
            {
                EventBus.Publish(new EconomyStateReceived(sequence, data.ToArray()));
                return;
            }

            // 내 전투원의 효과 줄 (S15P21A606-300). Host 가 주인에게만 보내므로 받은 것은 전부 내 것이다 —
            // 그래도 전투원 id 로 보관하고 화면은 자기 전투원 id 로만 읽는다.
            if (magic == HudEffectReplication.Magic)
            {
                HudEffectReplication.OwnerInbox.Receive(sequence, data, Time.unscaledTimeAsDouble);
                return;
            }

            // 방 설정 (S15P21A606-55). 늦게 도착한 옛 값이 새 값을 덮지 않도록 순번을 본다.
            if (magic == RoomSettingsProtocol.Magic)
            {
                if (sequence <= lastRoomSettingsSequence
                    || !RoomSettingsProtocol.TryDeserialize(data, out RoomSettingsChanged settings))
                {
                    return;
                }

                lastRoomSettingsSequence = sequence;
                EventBus.Publish(settings);
                return;
            }

            if (magic != OnlineCoreLoopProtocol.Magic
                || !OnlineCoreLoopProtocol.TryDeserialize(data, out OnlineCoreLoopSnapshot snapshot))
            {
                return;
            }

            EventBus.Publish(new OnlineCoreLoopSnapshotReceived(sequence, snapshot));
        }
        /// <summary>
        /// 신뢰 전송의 진행도. <b>막힌 전송을 보이게 하는 유일한 창이다</b> (S15P21A606-275).
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>예전에는 빈 메서드였다.</b> 실측에서 Host → Client 신뢰 채널이 매치 시작 몇 초 뒤
        /// 영구히 죽는다 — 호스트는 경제 순번 45 까지 보냈는데 클라는 17 에서 멈췄고, 코어루프도
        /// 49 에서 멈췄다. 반대 방향(클라 → 호스트)은 멀쩡하다. 그 사이에서 무슨 일이 일어나는지
        /// 볼 방법이 이 콜백뿐이다.
        ///
        /// 완료(1.0)는 세기만 하고, <b>끝나지 않은 전송</b>만 마지막 진행도를 남긴다 —
        /// 그 값이 1 미만에서 멈춰 있으면 그 전송이 채널을 막고 있는 것이다.
        ///
        /// ⚠️ 2026-09-21 실측에서 <b>이 콜백은 한 번도 불리지 않았다</b> — 전송이 작아 청크 진행이
        /// 없기 때문으로 보인다. 그래도 남긴다: 안 불린다는 사실 자체가 "대형 전송이 막은 것은
        /// 아니다" 를 확정해 준 근거였다.
        /// </remarks>
        public void OnReliableDataProgress(NetworkRunner r, PlayerRef player, ReliableKey key, float progress)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            key.GetInts(out int magic, out int sequence, out _, out _);
            if (progress >= 1f)
            {
                ReliableTransfersCompleted++;
                reliableInFlight.Remove((magic, sequence));
                return;
            }

            reliableInFlight[(magic, sequence)] = progress;
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>끝나지 않은 신뢰 전송의 마지막 진행도. 키는 (magic, sequence).</summary>
        private readonly System.Collections.Generic.Dictionary<(int Magic, int Sequence), float>
            reliableInFlight = new();

        /// <summary>완료된 신뢰 전송 수. 진단이 읽고 비운다.</summary>
        internal static int ReliableTransfersCompleted;

        /// <summary>아직 끝나지 않은 신뢰 전송 수. <b>계속 늘면 채널이 막힌 것이다.</b></summary>
        internal static int ReliableTransfersInFlight =>
            HasInstance && Instance != null ? Instance.reliableInFlight.Count : 0;

        /// <summary>끝나지 않은 전송 중 가장 낮은 진행도. 1 미만에서 굳으면 그것이 막은 전송이다.</summary>
        internal static float ReliableWorstProgress
        {
            get
            {
                if (!HasInstance || Instance == null) return 1f;
                var worst = 1f;
                foreach (var pair in Instance.reliableInFlight)
                {
                    if (pair.Value < worst) worst = pair.Value;
                }

                return worst;
            }
        }
#endif
        public void OnObjectEnterAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
        public void OnObjectExitAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
        /// <summary>
        /// 방 안 App Scene 전환이 시작됐다. <b>전환 연출에만 쓰고 게임 로직은 넣지 않는다</b>(D-009 콜백 규약).
        /// </summary>
        /// <remarks>
        /// 이 전환은 <c>SceneLoader.Load</c> 를 지나지 않아 전환 커튼이 걸리지 않았고, 새 씬의 첫 프레임까지
        /// Unity 기본 하늘이 드러났다(S15P21A606-199). 커튼은 Managers 소유이므로 이벤트로만 알린다 —
        /// Network 가 Managers 를 직접 부르지 않는다.
        /// </remarks>
        public void OnSceneLoadStart(NetworkRunner r)
        {
            EventBus.Publish(new NetworkSceneTransitionStarted(isMatchStart: false));
        }

        public void OnSceneLoadDone(NetworkRunner r)
        {
            // ⚠️ 아래 Host 전용 구간보다 먼저 알린다. 커튼은 Client 화면도 덮고 있으므로
            //    여기서 조기 반환에 묻히면 Client 만 덮인 채 남는다.
            EventBus.Publish(new NetworkSceneTransitionCompleted());

            if (!r.IsServer)
            {
                return;
            }

            // Fusion 씬 전환은 이전 씬의 동적 오브젝트를 정리할 수 있다. OnPlayerJoined는 다시 오지 않으므로
            // 현재 참가자를 기준으로 메타데이터를 재보장하고, 전투 씬이면 입력 대상도 중복 없이 만든다.
            foreach (PlayerRef player in r.ActivePlayers)
            {
                EnsurePlayerInfo(r, player);
                EnsureCombatant(r, player);
            }

            // 대기실로 돌아왔으면 전투 오브젝트를 치운다 (S15P21A606-47).
            //
            // ⚠️ "Fusion 이 씬 전환에서 알아서 지운다" 에 기대지 않는다. 남으면 다음 매치에서
            //    참가자당 두 개가 되고, 그 상태는 **개수를 세기 전까지 화면에 드러나지 않는다.**
            //    이미 정리됐다면 아래 유효성 검사에서 걸러지므로 두 번 지우지 않는다.
            if (!IsCombatSceneActive())
            {
                DespawnCombatants(r);
            }

            PublishPlayers();
        }

        protected override void OnDestroy()
        {
            UnsubscribeLobbyWeaponChoiceCommand();
            DisposeRunner();
            base.OnDestroy();
        }
    }

    /// <summary>전투 맵 준비 보고 — [버전, 라운드] 두 바이트 (S15P21A606-326).</summary>
    internal static class CombatEntryReadyProtocol
    {
        public const int Magic = 0x42524345;
        public const byte Version = 1;

        public static bool TryDeserialize(ReadOnlySpan<byte> payload, out byte round)
        {
            round = 0;
            if (payload.Length != 2 || payload[0] != Version || payload[1] == 0) return false;
            round = payload[1];
            return true;
        }
    }

    internal static class MatchLoadingProgressProtocol
    {
        public const int Magic = 0x42524C50;
        public const byte Version = 1;

        public static bool TryDeserialize(ReadOnlySpan<byte> payload, out byte percent)
        {
            percent = 0;
            if (payload.Length != 2 || payload[0] != Version || payload[1] > 100) return false;
            percent = payload[1];
            return true;
        }
    }

    /// <summary>
    /// 방 설정을 신뢰 채널로 보내기 위한 최소 규약 (S15P21A606-55).
    /// </summary>
    /// <remarks>
    /// <c>OnlineCoreLoopProtocol</c> 과 같은 형태다 — 버전 바이트를 앞에 두고, 받는 쪽은 버전이
    /// 다르면 해석하지 않고 버린다. 구버전 피어가 절반만 읽고 엉뚱한 상태로 진행하는 것보다 낫다.
    /// </remarks>
    internal static class RoomSettingsProtocol
    {
        /// <summary><c>OnlineCoreLoopProtocol.Magic</c> 과 달라야 한다. 같은 채널을 나눠 쓰기 때문이다.</summary>
        public const int Magic = 0x42525354;

        private const byte Version = 1;
        private const int MaxTitleBytes = 64;
        private const int MaxPayloadBytes = 256;

        public static byte[] Serialize(in RoomSettingsChanged settings)
        {
            using var stream = new MemoryStream(64);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            byte[] title = Encoding.UTF8.GetBytes(settings.Title ?? string.Empty);
            int titleLength = Math.Min(title.Length, MaxTitleBytes);

            writer.Write(Version);
            writer.Write((byte)Math.Clamp(settings.MaxPlayers, 0, byte.MaxValue));
            writer.Write(settings.IsPublic);
            writer.Write(settings.IsOpen);
            writer.Write((byte)titleLength);
            writer.Write(title, 0, titleLength);

            writer.Flush();
            return stream.ToArray();
        }

        public static bool TryDeserialize(ReadOnlySpan<byte> payload, out RoomSettingsChanged settings)
        {
            settings = default;
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

                int maxPlayers = reader.ReadByte();
                bool isPublic = reader.ReadBoolean();
                bool isOpen = reader.ReadBoolean();
                int titleLength = reader.ReadByte();

                if (titleLength > MaxTitleBytes || stream.Position + titleLength > stream.Length)
                {
                    return false;
                }

                string title = Encoding.UTF8.GetString(reader.ReadBytes(titleLength));

                if (stream.Position != stream.Length)
                {
                    return false;
                }

                settings = new RoomSettingsChanged(title, maxPlayers, isPublic, isOpen);
                return true;
            }
            catch (Exception exception) when (exception is IOException or ArgumentException)
            {
                return false;
            }
        }
    }
}

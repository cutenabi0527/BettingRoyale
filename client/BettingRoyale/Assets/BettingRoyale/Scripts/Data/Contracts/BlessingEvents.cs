using System;
using BettingRoyale.Core;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Online;

namespace BettingRoyale.Data.Contracts
{
    /// <summary>
    /// 축복 선택 모달이 그리는 <b>남은 선택 시간 하나</b>. 초 단위 값과 한도만 담는 순수 계산이다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>시계를 새로 만들지 않는다.</b> 남은 시간의 주인은 이미 두 곳에 있다 —
    /// 온라인은 <c>OnlineCoreLoopSnapshot.BlessingSelectionRemainingSeconds</c>,
    /// Local Play 는 <c>LocalPlaySessionSnapshot.AugmentSelectionRemainingSeconds</c> 다.
    /// 화면이 자기 <c>Update</c> 에서 따로 세면 두 번째 시계가 생기고, 둘은 반드시 어긋난다.
    /// 이 구조체는 <b>그 값을 받아 표시 형태로 바꾸기만</b> 한다.
    /// </para>
    /// <para>
    /// <b>표시 한도는 30초를 넘지 않는다</b> ([S-GD-DP2DVC](../../../../../../docs/specs/game-design/S-GD-DP2DVC.md):
    /// 축복 선택은 <i>최대</i> 30초). 준비 단계가 짧아 규칙이 30초보다 작은 한도를 주면
    /// <b>그 작은 값을 그대로 쓴다</b> — 30초로 부풀리지 않는다. 30 이라는 숫자의 주인은
    /// <see cref="OnlineCoreLoopRules.MaxBlessingSelectionSeconds"/> 하나다.
    /// </para>
    /// <para>
    /// 한도를 알 수 없으면(아직 스냅샷이 없거나 규칙이 0·NaN) <see cref="HasLimit"/> 가 false 다.
    /// <b>그 때 0초로 그리지 않는다</b> — "모른다" 와 "다 됐다" 는 다른 이야기다.
    /// </para>
    /// </remarks>
    public readonly struct BlessingSelectionTimer
    {
        /// <summary>표시 한도의 절대 상한(초). 규칙이 더 큰 값을 줘도 여기서 잘린다.</summary>
        public const float MaxLimitSeconds = OnlineCoreLoopRules.MaxBlessingSelectionSeconds;

        private BlessingSelectionTimer(float remainingSeconds, float limitSeconds)
        {
            LimitSeconds = limitSeconds;
            RemainingSeconds = remainingSeconds;
        }

        /// <summary>아직 아무 스냅샷도 받지 못한 상태. 숫자 대신 "—" 를 그린다.</summary>
        public static BlessingSelectionTimer Unknown => default;

        /// <summary>
        /// 남은 시간과 한도를 표시 값으로 정리한다. 음수·NaN·한도 초과를 전부 여기서 흡수한다.
        /// </summary>
        public static BlessingSelectionTimer FromSeconds(float remainingSeconds, float limitSeconds)
        {
            if (!IsFinite(limitSeconds) || limitSeconds <= 0f)
            {
                return Unknown;
            }

            float limit = Math.Min(limitSeconds, MaxLimitSeconds);
            float remaining = IsFinite(remainingSeconds) ? remainingSeconds : 0f;

            // 남은 시간이 한도를 넘는 스냅샷(한도 축소·지연 도착)은 한도로 눕힌다.
            // 그래야 원형 게이지가 한 바퀴를 넘지 않는다.
            remaining = Math.Min(Math.Max(remaining, 0f), limit);
            return new BlessingSelectionTimer(remaining, limit);
        }

        /// <summary>한도를 아는가. false 면 남은 시간도 의미가 없다.</summary>
        public bool HasLimit => LimitSeconds > 0f;

        /// <summary>이번 선택의 제한시간(초). 항상 0 초과 <see cref="MaxLimitSeconds"/> 이하다.</summary>
        public float LimitSeconds { get; }

        /// <summary>남은 시간(초). 0 이상 <see cref="LimitSeconds"/> 이하다.</summary>
        public float RemainingSeconds { get; }

        /// <summary>
        /// 화면에 적는 정수 초. <b>올림</b>이라 0.2초가 남았으면 아직 1 이고, 정확히 0 일 때만 0 이다 —
        /// 내림으로 적으면 마지막 1초 내내 "0" 이 떠 있어 이미 끝난 것처럼 읽힌다.
        /// </summary>
        public int DisplaySeconds => HasLimit ? (int)Math.Ceiling(RemainingSeconds) : 0;

        /// <summary>원형 게이지가 채우는 비율 0~1. 한도를 모르면 0 이다.</summary>
        public float Progress => HasLimit ? Math.Min(1f, Math.Max(0f, RemainingSeconds / LimitSeconds)) : 0f;

        /// <summary>시간이 다 됐는가. 한도를 모르는 동안은 false 다 (S-GD-NK8ZJ7 자동 선택 시점).</summary>
        public bool IsExpired => HasLimit && RemainingSeconds <= 0f;

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// 후보 하나를 화면에 그리는 데 필요한 것 전부.
    ///
    /// 도메인의 <see cref="BlessingDefinition"/> 을 그대로 내보내지 않는다 —
    /// 그건 참조 타입이라 네트워크로 복제할 수 없고, UI 가 도메인 객체를 붙들면 잠금 상태가 두 곳에 생긴다.
    /// </summary>
    public readonly struct BlessingCandidateView
    {
        /// <summary>
        /// <b>0부터 시작하는 후보 자리 번호.</b> 화면이 잠금·선택을 요청할 때 이 값을 그대로 명령에 넣는다
        /// (<see cref="BlessingLockToggleCommand.CandidateIndex"/>, <see cref="BlessingSelectCommand.CandidateIndex"/>).
        ///
        /// <b>왜 문자열 id 가 아니라 인덱스인가</b> — 명령이 인덱스로만 받기 때문이다.
        /// id 로 주면 화면과 도메인 사이에 매핑 테이블이 하나 더 생기고, 명령 경계에서 다시 인덱스로 되돌려야 한다.
        /// 표시·로그·아트 조회가 필요하면 <see cref="BlessingId"/> 를 따로 쓴다.
        /// </summary>
        public readonly int CandidateIndex;

        /// <summary>변하지 않는 문자열 식별자. 로그·아트 조회용이다. <b>명령에는 쓰지 않는다.</b></summary>
        public readonly string BlessingId;

        public readonly string DisplayName;

        /// <summary>
        /// 카드 설명문. 축복 목록이 정해지기 전에는 <b>빈 문자열</b>이다
        /// (<see cref="BlessingDefinition.Description"/> 참고).
        /// </summary>
        public readonly string Description;

        public readonly WeaponTag Tag;

        /// <summary>
        /// 등급 (S-GD-EQCVWS). 실버·골드·플래티넘. UI 가 테두리 색 등으로 구분한다.
        ///
        /// <b>한 라운드의 후보 3개는 등급이 전부 같다</b> — 등급은 라운드로 정해지기 때문이다(S-GD-HE87JY).
        /// 그래도 후보마다 들려 보내는 이유는, 나중에 "다음 축복의 등급을 올려주는 축복" 이 들어오면
        /// 그 전제가 깨지기 때문이다. UI 는 지금도 후보별로 읽는 편이 안전하다.
        /// </summary>
        public readonly BlessingTier Tier;

        /// <summary>잠금 상태 (S-GD-VN5VD7 — "고정" 이 아니라 잠금).</summary>
        public readonly bool IsLocked;

        public BlessingCandidateView(
            int candidateIndex,
            string blessingId,
            string displayName,
            string description,
            WeaponTag tag,
            BlessingTier tier,
            bool isLocked)
        {
            CandidateIndex = candidateIndex;
            BlessingId = blessingId;
            DisplayName = displayName;
            Description = description ?? string.Empty;
            Tag = tag;
            Tier = tier;
            IsLocked = isLocked;
        }
    }

    /// <summary>
    /// 축복 제시(offer)가 바뀌었다 — 준비 단계 시작, 잠금 토글, 재추첨 성공 <b>모두</b> 이 하나로 알린다.
    ///
    /// <b>델타가 아니라 후보 전체를 싣는다.</b> "2번이 바뀌었다" 로 보내면 이벤트 하나를 놓쳤을 때
    /// 화면과 실제가 어긋난 채 재현이 안 된다 (<c>client/AGENTS.md</c> 7절, D-018 과 같은 근거).
    /// </summary>
    public readonly struct BlessingOfferChanged : IGameEvent
    {
        public readonly int PlayerId;

        /// <summary>화면을 보는 사람의 것인가. 규약은 <see cref="GoldChanged.IsMine"/> 참조 (§7).</summary>
        public readonly bool IsMine;

        /// <summary>1부터 시작하는 라운드 번호.</summary>
        public readonly int Round;

        /// <summary>후보 전체. 길이는 <c>BlessingConfig.CandidateCount</c>.</summary>
        public readonly BlessingCandidateView[] Candidates;

        /// <summary>이번 준비 단계에서 이미 재추첨한 횟수.</summary>
        public readonly int RerollCount;

        /// <summary>
        /// <b>아직 잠글 수 있는 개수</b> — 화면의 "잠금 가능 N개" 에 그대로 쓴다 (S-GD-S46S28, 최대 2개 무료).
        ///
        /// 남은 개수를 도메인이 계산해서 보낸다. 화면이 <c>MaxFreeLocks - LockedCount</c> 를 하면
        /// 상한 규칙이 두 곳에 생기고, 상한이 바뀔 때 한쪽만 고쳐진다.
        /// </summary>
        public readonly int RemainingLockCount;

        /// <summary>
        /// 이번 매치에서 지금까지 획득한 축복 (S-GD-RFXF8X). 화면이 "누적 축복 N개: …" 목록을 그린다.
        ///
        /// 획득한 것은 후보가 아니므로 <see cref="BlessingCandidateView.CandidateIndex"/> 는 의미가 없다.
        /// 그 자리에 <c>-1</c> 을 넣고, <see cref="BlessingCandidateView.IsLocked"/> 는 <c>false</c> 다.
        /// </summary>
        public readonly BlessingCandidateView[] Acquired;

        /// <summary>
        /// <b>지금 재추첨 버튼을 누르면 차감되는 금액.</b> 화면의 "재추첨 N G" 에 그대로 쓴다.
        ///
        /// ⚠️ 이 필드는 원래 <c>NextRerollCost</c> 였는데 2026-08-28 에 개명했다.
        /// <c>Scripts/UI/MatchViewEvents.cs</c> 의 <c>BlessingOfferChanged</c> 에도 같은 이름이 있는데
        /// <b>거기서는 "한 번 더 누르면 드는 값"(= 이 값의 2배)을 뜻한다.</b> 이름이 같고 뜻이 반대라
        /// 어댑터에서 그대로 대입하면 화면 금액이 한 칸씩 밀린다 — 1라운드 첫 재추첨에
        /// "재추첨 0G (다음 100G)" 라고 뜨는데 실제로는 100G 가 빠진다.
        /// 컴파일도 린트도 이걸 잡지 못하므로 이름으로 구분한다.
        /// </summary>
        public readonly int CurrentRerollCost;

        /// <summary>
        /// <b>한 번 더 재추첨하면 드는 금액</b> (= <see cref="CurrentRerollCost"/> × 배수).
        /// 화면이 "재추첨 100G (다음 200G)" 처럼 미리 보여주는 데 쓴다.
        ///
        /// <b>이 값을 UI 가 직접 곱해서 만들지 않게 하려고 도메인이 실어 보낸다.</b>
        /// UI 가 ×2 를 하는 순간 S-GD-SHJGNB 의 배수 규칙이 두 곳에 생기고, 배수가 바뀌면 한쪽만 고쳐진다.
        /// </summary>
        public readonly int NextRerollCostPreview;

        /// <summary>
        /// 재추첨 비용이 정해져 있는가. <b>항상 true</b> 다.
        ///
        /// S-GD-HE87JY 이후로는 등급이 정의되지 않은 라운드에서 준비 단계가 <b>시작조차 되지 않으므로</b>,
        /// 이 이벤트가 발행된 시점에는 비용이 반드시 정의돼 있다.
        ///
        /// ⚠️ 이 필드를 남겨둔 이유를 2026-08-28 에 정정한다 — 전에는 "UI 파트와 합의한 모양이라"
        /// 라고 적었는데, 확인해보니 <b>UI 는 이 계약을 쓰지 않고 자기 것을 따로 갖고 있다.</b>
        /// 합의된 적이 없다. 지금 남겨두는 이유는 계약 통합(회의 안건) 전까지 모양을 흔들지 않기 위해서다.
        /// </summary>
        public readonly bool HasRerollCost;

        /// <summary>
        /// 지금 재추첨 버튼을 누를 수 있는가. <b>화면은 이 값으로 버튼을 비활성화한다</b> —
        /// 눌렀다가 거부당하는 대신 애초에 못 누르게 한다(사용자 결정, 2026-08-27).
        ///
        /// <b>화면이 골드를 직접 비교하지 않는 것이 핵심이다.</b> 비교를 화면이 하면 같은 규칙이
        /// 두 곳에 생기고 반드시 어긋난다. 판정은 <c>BlessingDraft.GetRerollAvailability</c> 하나뿐이다.
        /// </summary>
        public readonly bool CanReroll;

        /// <summary>
        /// <see cref="CanReroll"/> 가 false 인 이유. true 면 <see cref="RerollResult.Success"/> 다.
        /// 비활성 버튼의 툴팁 문구를 고르는 데 쓴다 — "골드 부족" 과 "이미 선택함" 은 다른 안내다.
        /// </summary>
        public readonly RerollResult RerollBlockedBy;

        public BlessingOfferChanged(
            int playerId,
            bool isMine,
            int round,
            BlessingCandidateView[] candidates,
            int rerollCount,
            int remainingLockCount,
            int currentRerollCost,
            int nextRerollCostPreview,
            bool hasRerollCost,
            RerollResult rerollBlockedBy,
            BlessingCandidateView[] acquired)
        {
            PlayerId = playerId;
            IsMine = isMine;
            Round = round;
            Candidates = candidates;
            RerollCount = rerollCount;
            RemainingLockCount = remainingLockCount;
            CurrentRerollCost = currentRerollCost;
            NextRerollCostPreview = nextRerollCostPreview;
            HasRerollCost = hasRerollCost;
            RerollBlockedBy = rerollBlockedBy;
            Acquired = acquired;
            CanReroll = rerollBlockedBy == RerollResult.Success;
        }
    }

    /// <summary>
    /// 재추첨이 거부됐다. 사유를 그대로 실어 UI 가 다른 문구를 띄우게 한다 —
    /// "골드 부족" 과 "이 라운드는 재추첨할 수 없음" 은 사용자에게 완전히 다른 이야기다.
    /// </summary>
    public readonly struct BlessingRerollRejected : IGameEvent
    {
        public readonly int PlayerId;

        /// <summary>화면을 보는 사람의 것인가. 규약은 <see cref="GoldChanged.IsMine"/> 참조 (§7).</summary>
        public readonly bool IsMine;

        public readonly RerollResult Reason;

        public BlessingRerollRejected(int playerId, bool isMine, RerollResult reason)
        {
            PlayerId = playerId;
            IsMine = isMine;
            Reason = reason;
        }
    }

    /// <summary>축복이 선택되어 준비 단계가 끝났다 (S-GD-S46S28). 선택은 잠금 수와 무관하게 1개다.</summary>
    public readonly struct BlessingSelected : IGameEvent
    {
        public readonly int PlayerId;

        /// <summary>화면을 보는 사람의 것인가. 규약은 <see cref="GoldChanged.IsMine"/> 참조 (§7).</summary>
        public readonly bool IsMine;

        public readonly int Round;
        public readonly string BlessingId;

        /// <summary>이번 매치에서 지금까지 획득한 축복 수 (S-GD-RFXF8X). 매치가 바뀌면 다시 0부터다.</summary>
        public readonly int AcquiredCount;

        public BlessingSelected(int playerId, bool isMine, int round, string blessingId, int acquiredCount)
        {
            PlayerId = playerId;
            IsMine = isMine;
            Round = round;
            BlessingId = blessingId;
            AcquiredCount = acquiredCount;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 거부 이벤트 (docs/client/ui-domain-contract.md §6 "침묵은 성공이 아니다")
    //
    // 명령 하나에는 성공 이벤트와 거부 이벤트가 짝으로 있어야 한다.
    // 없으면 화면은 "거부당한 것" 과 "아직 응답이 안 온 것" 을 구분할 수 없고,
    // 목업의 "눌러도 아무 일 없는 버튼" 이 그대로 재현된다.
    //
    // 사유 enum 의 값은 전부 도메인이 실제로 만들어내는 것만 담는다 — 있을 법한 것을 지어내지 않는다.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>잠금 요청이 거부된 이유. <c>BlessingDraft.TryToggleLock</c> 이 false 를 내는 조건과 1:1 이다.</summary>
    public enum BlessingLockRejectReason
    {
        /// <summary>이미 축복을 선택해 이번 준비 단계가 끝났다 (<c>IsSelected</c>).</summary>
        AlreadySelected = 0,

        /// <summary>무료 잠금 상한에 걸렸다 — 최대 2개다 (S-GD-S46S28).</summary>
        LockLimitReached = 1,

        /// <summary>그런 후보 번호가 없다. 화면과 도메인의 후보 목록이 어긋났다는 뜻이라 로그를 남길 값이다.</summary>
        NoSuchCandidate = 2,
    }

    /// <summary>
    /// 잠금 요청이 거부됐다 (<see cref="BlessingLockToggleCommand"/> 의 짝).
    ///
    /// <b>잠금 해제는 거부되지 않는다.</b> 이미 잠긴 것을 푸는 것은 언제나 성공한다 —
    /// 상한은 켤 때만 본다.
    /// </summary>
    public readonly struct BlessingLockRejected : IGameEvent
    {
        public readonly int PlayerId;

        /// <summary>화면을 보는 사람의 것인가. 규약은 <see cref="GoldChanged.IsMine"/> 참조 (§7).</summary>
        public readonly bool IsMine;

        /// <summary>거부된 요청의 후보 번호. 화면이 그 카드만 흔들어 보이는 데 쓴다.</summary>
        public readonly int CandidateIndex;

        public readonly BlessingLockRejectReason Reason;

        public BlessingLockRejected(int playerId, bool isMine, int candidateIndex, BlessingLockRejectReason reason)
        {
            PlayerId = playerId;
            IsMine = isMine;
            CandidateIndex = candidateIndex;
            Reason = reason;
        }
    }

    /// <summary>선택 요청이 거부된 이유. <c>BlessingDraft.Select</c> 가 예외를 던지는 조건과 1:1 이다.</summary>
    public enum BlessingSelectRejectReason
    {
        /// <summary>이미 이번 준비 단계에서 축복을 선택했다. 버튼 연타로 쉽게 발생한다.</summary>
        AlreadySelected = 0,

        /// <summary>그런 후보 번호가 없다.</summary>
        NoSuchCandidate = 1,
    }

    /// <summary>
    /// 축복 선택이 거부됐다 (<see cref="BlessingSelectCommand"/> 의 짝).
    ///
    /// ⚠️ <b>도메인은 이 상황에서 예외를 던진다</b>(<c>BlessingDraft.Select</c>).
    /// 예외를 그대로 두면 <c>EventBus</c> 가 로그 한 줄로 삼켜서 화면까지 오지 않는다 —
    /// <b>어댑터가 잡아서 이 이벤트로 바꿔 발행한다</b> (§6).
    /// </summary>
    public readonly struct BlessingSelectRejected : IGameEvent
    {
        public readonly int PlayerId;

        /// <summary>화면을 보는 사람의 것인가. 규약은 <see cref="GoldChanged.IsMine"/> 참조 (§7).</summary>
        public readonly bool IsMine;

        public readonly int CandidateIndex;
        public readonly BlessingSelectRejectReason Reason;

        public BlessingSelectRejected(int playerId, bool isMine, int candidateIndex, BlessingSelectRejectReason reason)
        {
            PlayerId = playerId;
            IsMine = isMine;
            CandidateIndex = candidateIndex;
            Reason = reason;
        }
    }
}

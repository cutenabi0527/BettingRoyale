using System;
using BettingRoyale.Data.Blessing;

namespace BettingRoyale.Data.Economy
{
    /// <summary>재추첨 요청 하나를 처리한 결과. 어댑터는 <b>판단하지 않고 그대로 발행</b>한다.</summary>
    public readonly struct RerollRequestOutcome
    {
        public readonly EconomyRequestVerdict Verdict;

        /// <summary><c>false</c> 면 참가자가 아니라서 아무것도 발행하지 않는다.</summary>
        public readonly bool HasEvents;

        /// <summary>항상 발행한다. 적용되지 않았으면 <c>Delta</c> 가 0 이다.</summary>
        public readonly Contracts.GoldChanged Gold;

        /// <summary>항상 발행한다. 후보 <b>전체</b>를 실으므로 재전송이 공짜다.</summary>
        public readonly Contracts.BlessingOfferChanged Offer;

        public readonly bool HasRejection;
        public readonly Contracts.BlessingRerollRejected Rejection;

        internal RerollRequestOutcome(
            EconomyRequestVerdict verdict,
            bool hasEvents,
            Contracts.GoldChanged gold,
            Contracts.BlessingOfferChanged offer,
            bool hasRejection,
            Contracts.BlessingRerollRejected rejection)
        {
            Verdict = verdict;
            HasEvents = hasEvents;
            Gold = gold;
            Offer = offer;
            HasRejection = hasRejection;
            Rejection = rejection;
        }
    }

    /// <summary>잠금 토글 요청 하나를 처리한 결과.</summary>
    public readonly struct LockRequestOutcome
    {
        public readonly EconomyRequestVerdict Verdict;
        public readonly bool HasEvents;
        public readonly Contracts.BlessingOfferChanged Offer;
        public readonly bool HasRejection;
        public readonly Contracts.BlessingLockRejected Rejection;

        internal LockRequestOutcome(
            EconomyRequestVerdict verdict,
            bool hasEvents,
            Contracts.BlessingOfferChanged offer,
            bool hasRejection,
            Contracts.BlessingLockRejected rejection)
        {
            Verdict = verdict;
            HasEvents = hasEvents;
            Offer = offer;
            HasRejection = hasRejection;
            Rejection = rejection;
        }
    }

    /// <summary>축복 선택 요청 하나를 처리한 결과.</summary>
    public readonly struct SelectRequestOutcome
    {
        public readonly EconomyRequestVerdict Verdict;
        public readonly bool HasEvents;
        public readonly Contracts.BlessingOfferChanged Offer;

        /// <summary>이번 요청으로 <b>실제로 선택됐을 때만</b> 있다. 재전송에는 없다.</summary>
        public readonly bool HasSelected;
        public readonly Contracts.BlessingSelected Selected;

        public readonly bool HasRejection;
        public readonly Contracts.BlessingSelectRejected Rejection;

        internal SelectRequestOutcome(
            EconomyRequestVerdict verdict,
            bool hasEvents,
            Contracts.BlessingOfferChanged offer,
            bool hasSelected,
            Contracts.BlessingSelected selected,
            bool hasRejection,
            Contracts.BlessingSelectRejected rejection)
        {
            Verdict = verdict;
            HasEvents = hasEvents;
            Offer = offer;
            HasSelected = hasSelected;
            Selected = selected;
            HasRejection = hasRejection;
            Rejection = rejection;
        }
    }

    /// <summary>
    /// <b>Host 가 받은 준비 단계 요청을 판정하고, 화면에 보낼 이벤트를 완성해 돌려준다</b>
    /// (Jira S15P21A606-26).
    ///
    /// <para>
    /// <b>왜 이것이 <c>Scripts/Data/</c> 에 있는가</b> — 중복 방지의 <b>소유는 어댑터(-26)</b> 지만,
    /// 판정 코드를 <c>Scripts/Network/</c> 에 두면 Fusion·UnityEngine 의존이라
    /// <c>client/Tests</c> 가 영원히 컴파일할 수 없다. 그러면 완료 조건 1·2 를 도메인 테스트로 증명할 수 없다.
    /// 그래서 <b>결정 표 전체를 여기로 끌어냈고</b>, 어댑터에는 판단이 한 줄도 남지 않는다:
    /// </para>
    ///
    /// <code>
    /// var r = gate.HandleReroll(playerId, ticket, isMine);
    /// if (!r.HasEvents) return;
    /// if (r.HasRejection) EventBus.Publish(r.Rejection);
    /// EventBus.Publish(r.Gold);
    /// EventBus.Publish(r.Offer);
    /// </code>
    ///
    /// <para>
    /// ⚠️ <b>이 클래스는 발행하지 않는다.</b> <c>EventBus</c> 가 <c>UnityEngine</c> 을 참조하므로
    /// <c>Scripts/Data/</c> 에서는 부를 수 없다 (D-060). 이벤트를 <b>만들어 돌려주기만</b> 한다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <c>playerId</c> 는 <b>반드시 RPC 입력 권한에서 꺼낸 값</b>이어야 한다.
    /// 페이로드에서 온 값을 넘기면 규약 §5 가 없앤 "남의 골드를 쓰는" 경로가 되살아난다.
    /// </para>
    /// </summary>
    public sealed class EconomyRequestGate
    {
        private readonly MatchEconomyState economy;
        private readonly EconomyRequestLedger ledger;

        public EconomyRequestGate(MatchEconomyState economy, EconomyRequestLedger ledger)
        {
            this.economy = economy ?? throw new ArgumentNullException(nameof(economy));
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        }

        /// <summary>현재 준비 단계 번호. 어댑터가 이 값을 복제해 클라이언트가 티켓에 싣는다.</summary>
        public int Epoch => ledger.Epoch;

        /// <summary>
        /// 준비 단계를 연다. <b><see cref="MatchEconomyState.BeginPreparation"/> 를 부른 직후에 부른다.</b>
        /// 순서가 뒤집히면 새 단계의 요청이 지난 번호로 걸린다.
        /// </summary>
        public int OpenPreparation() => ledger.OpenPreparation();

        /// <summary>재추첨 요청. 완료 조건 1 — 같은 요청을 두 번 보내도 골드는 한 번만 차감된다.</summary>
        public RerollRequestOutcome HandleReroll(int playerId, EconomyRequestTicket ticket, bool isMine)
        {
            if (!IsParticipant(playerId))
            {
                return default;
            }

            var verdict = ledger.Accept(playerId, ticket);

            if (verdict != EconomyRequestVerdict.Fresh || economy.Phase != MatchEconomyPhase.Preparation)
            {
                // 재전송·지난 요청·준비 종료 — 거부가 아니라 "현재 확정 상태" 를 돌려준다.
                // 사용자의 클릭은 이미 한 번 성공했으므로 거부를 띄우면 거짓말이 된다 (-26 본문).
                return new RerollRequestOutcome(
                    ClosedIfNeeded(verdict),
                    hasEvents: true,
                    gold: economy.BuildGoldChanged(playerId, isMine, 0, Contracts.GoldChangeReason.Spent),
                    offer: economy.BuildBlessingOffer(playerId, isMine),
                    hasRejection: false,
                    rejection: default);
            }

            int before = economy.GetGold(playerId);
            var result = economy.TryReroll(playerId);
            int delta = economy.GetGold(playerId) - before;

            return new RerollRequestOutcome(
                verdict,
                hasEvents: true,
                gold: economy.BuildGoldChanged(playerId, isMine, delta, Contracts.GoldChangeReason.Spent),
                offer: economy.BuildBlessingOffer(playerId, isMine),
                hasRejection: result != RerollResult.Success,
                rejection: new Contracts.BlessingRerollRejected(playerId, isMine, result));
        }

        /// <summary>잠금 토글 요청. 골드가 움직이지 않으므로 <c>GoldChanged</c> 를 내지 않는다.</summary>
        public LockRequestOutcome HandleLockToggle(
            int playerId, int candidateIndex, EconomyRequestTicket ticket, bool isMine)
        {
            if (!IsParticipant(playerId))
            {
                return default;
            }

            var verdict = ledger.Accept(playerId, ticket);

            if (verdict != EconomyRequestVerdict.Fresh || economy.Phase != MatchEconomyPhase.Preparation)
            {
                return new LockRequestOutcome(
                    ClosedIfNeeded(verdict), true, economy.BuildBlessingOffer(playerId, isMine), false, default);
            }

            bool applied = economy.TryToggleLock(playerId, candidateIndex);

            return new LockRequestOutcome(
                verdict,
                hasEvents: true,
                offer: economy.BuildBlessingOffer(playerId, isMine),
                hasRejection: !applied,
                rejection: new Contracts.BlessingLockRejected(
                    playerId, isMine, candidateIndex, LockReason(playerId, candidateIndex)));
        }

        /// <summary>축복 선택 요청. 완료 조건 2 — 같은 요청을 두 번 보내도 축복은 하나만 얻는다.</summary>
        public SelectRequestOutcome HandleSelect(
            int playerId, int candidateIndex, EconomyRequestTicket ticket, bool isMine)
        {
            if (!IsParticipant(playerId))
            {
                return default;
            }

            var verdict = ledger.Accept(playerId, ticket);

            if (verdict != EconomyRequestVerdict.Fresh || economy.Phase != MatchEconomyPhase.Preparation)
            {
                // 여기서 거부를 띄우면 안 된다. 도메인은 두 번째 선택에 null 을 주지만
                // 사용자의 한 번뿐인 클릭은 이미 성공했다. 원장이 그 구분을 만든다.
                return new SelectRequestOutcome(
                    ClosedIfNeeded(verdict), true, economy.BuildBlessingOffer(playerId, isMine),
                    false, default, false, default);
            }

            var selected = economy.TrySelect(playerId, candidateIndex);

            return new SelectRequestOutcome(
                verdict,
                hasEvents: true,
                offer: economy.BuildBlessingOffer(playerId, isMine),
                hasSelected: selected != null,
                selected: selected == null
                    ? default
                    : economy.BuildBlessingSelected(playerId, isMine, selected),
                hasRejection: selected == null,
                rejection: new Contracts.BlessingSelectRejected(
                    playerId, isMine, candidateIndex, SelectReason(playerId, candidateIndex)));
        }

        private bool IsParticipant(int playerId)
        {
            var ids = economy.PlayerIds;

            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == playerId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>준비 단계가 아니어서 막힌 경우를 원장 판정과 구분해 표시한다.</summary>
        private static EconomyRequestVerdict ClosedIfNeeded(EconomyRequestVerdict verdict) =>
            verdict == EconomyRequestVerdict.Fresh ? EconomyRequestVerdict.PreparationClosed : verdict;

        private Contracts.BlessingLockRejectReason LockReason(int playerId, int candidateIndex)
        {
            if (candidateIndex < 0 || candidateIndex >= economy.GetCandidates(playerId).Count)
            {
                return Contracts.BlessingLockRejectReason.NoSuchCandidate;
            }

            return economy.IsSelectionDone(playerId)
                ? Contracts.BlessingLockRejectReason.AlreadySelected
                : Contracts.BlessingLockRejectReason.LockLimitReached;
        }

        private Contracts.BlessingSelectRejectReason SelectReason(int playerId, int candidateIndex) =>
            candidateIndex < 0 || candidateIndex >= economy.GetCandidates(playerId).Count
                ? Contracts.BlessingSelectRejectReason.NoSuchCandidate
                : Contracts.BlessingSelectRejectReason.AlreadySelected;
    }
}

using BettingRoyale.Core;
using BettingRoyale.Data.Blessing;

namespace BettingRoyale.Data.Contracts
{
    // 2 는 저점 매수의 옛 "위험 보급 도전" 이었다(2026-09-18 엑셀 개편으로 삭제, S-GD-TMPBEF). 번호를 다시 쓰지 않는다.
    // 4·5 는 거절이다 (S-GD-XBP3VA) — 수락과 같은 창에서 나가는 별개 명령이라 같은 번호를 재사용하지 않는다.
    public enum BlessingContractAction { Deposit = 1, BlindBet = 3, DeclineDeposit = 4, DeclineBlindBet = 5 }
    public readonly struct BlessingContractCommand : IGameEvent
    {
        public readonly BlessingContractAction Action;
        public BlessingContractCommand(BlessingContractAction action) => Action = action;
    }
    public enum BlessingContractRejectReason { NotAvailable = 1, NotPreparation = 2 }
    public readonly struct BlessingContractRejected : IGameEvent
    {
        public readonly int PlayerId;
        public readonly bool IsMine;
        public readonly BlessingContractAction Action;
        public readonly BlessingContractRejectReason Reason;
        public BlessingContractRejected(int playerId, bool isMine, BlessingContractAction action, BlessingContractRejectReason reason)
        { PlayerId = playerId; IsMine = isMine; Action = action; Reason = reason; }
    }
    public readonly struct BlessingContractStateChanged : IGameEvent
    {
        public readonly int PlayerId, Round, DepositCost, BlindBetBonusGold;
        public readonly float BlindBetHealthCostFraction;
        public readonly bool IsMine, IsPreparation, HasInterest, CanDeposit, Deposited;
        public readonly bool HasBlindBet, CanBlindBet, BlindBetAccepted;

        /// <summary>이번 준비 단계에서 거절했는가 (S-GD-XBP3VA). 거절한 계약은 창에 다시 뜨지 않는다.</summary>
        public readonly bool DepositDeclined, BlindBetDeclined;
        public BlessingContractStateChanged(int playerId, int round, bool isMine, bool isPreparation,
            bool hasInterest, bool canDeposit, bool deposited, int depositCost,
            bool hasBlindBet = false, bool canBlindBet = false, bool blindBetAccepted = false,
            int blindBetBonusGold = 0, float blindBetHealthCostFraction = 0f,
            bool depositDeclined = false, bool blindBetDeclined = false)
        {
            PlayerId = playerId; Round = round; IsMine = isMine; IsPreparation = isPreparation;
            HasInterest = hasInterest; CanDeposit = canDeposit; Deposited = deposited; DepositCost = depositCost;
            HasBlindBet = hasBlindBet; CanBlindBet = canBlindBet; BlindBetAccepted = blindBetAccepted;
            BlindBetBonusGold = blindBetBonusGold; BlindBetHealthCostFraction = blindBetHealthCostFraction;
            DepositDeclined = depositDeclined; BlindBetDeclined = blindBetDeclined;
        }
        public BlessingContractStateChanged ForViewer(bool mine) => new BlessingContractStateChanged(PlayerId,
            Round, mine, IsPreparation, HasInterest, CanDeposit, Deposited, DepositCost,
            HasBlindBet, CanBlindBet, BlindBetAccepted, BlindBetBonusGold, BlindBetHealthCostFraction,
            DepositDeclined, BlindBetDeclined);
        public static BlessingContractStateChanged From(int player, bool mine, bool preparation,
            BlessingEconomyState state, int gold)
        {
            // 비용은 <b>지금 보유한 이자 폭발</b>에서 온다. 특정 등급 id 를 박아두면 등급이 바뀌는 순간
            // 화면의 금액과 실제 차감액이 조용히 갈린다.
            int deposit = state.DepositCost;
            return new BlessingContractStateChanged(player, state.Round, mine, preparation,
                state.HasInterest, preparation && state.CanDeposit && gold >= deposit, state.InterestDeposited, deposit,
                state.HasBlindBet,
                preparation && state.HasBlindBet && !state.BlindBetAccepted && !state.BlindBetDeclined,
                state.BlindBetAccepted, state.BlindBetBonusGold, state.BlindBetCostFraction,
                state.DepositDeclined, state.BlindBetDeclined);
        }
    }
}

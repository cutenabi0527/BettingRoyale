using System;

namespace BettingRoyale.Data.Training
{
    /// <summary>
    /// Host 가 보낸 시험 주기 스냅샷. <b>화면은 이 값들로 계산하지 않는다</b> —
    /// "지금 예치할 수 있는가" 같은 판정은 전부 도메인이 끝냈다.
    ///
    /// <para>
    /// 배열은 들어올 때 복사한다. 스냅샷을 만든 쪽의 후보 목록이 뒤에 바뀌어도
    /// 이미 보낸 스냅샷이 따라 바뀌면 안 된다.
    /// </para>
    /// </summary>
    public sealed class TrainingBlessingTrialView
    {
        /// <summary>후보 상한. 설정의 <c>CandidateCount</c> 와 같고 와이어가 이 길이를 강제한다.</summary>
        public const int MaxCandidates = 3;

        /// <summary>주기가 없을 때의 기본값. <see cref="TrainingSessionView.Trial"/> 은 절대 null 이 아니다.</summary>
        public static readonly TrainingBlessingTrialView Empty = new(
            TrainingBlessingTrialPhase.Idle, 0, 0, TrainingBlessingTrialRank.None,
            false, false, false, false,
            false, false, false, false,
            0, 0, 0, 0, 0,
            string.Empty, null, null);

        public TrainingBlessingTrialPhase Phase { get; }
        public int Cycle { get; }

        /// <summary>흉내 내는 등급 라운드 (1~3). 주기 밖에서는 0.</summary>
        public int Round { get; }

        public string RankProfile { get; }
        public bool LowerHalfEligible { get; }
        public bool HasInterest { get; }
        public bool HasLowBuy { get; }
        public bool HasCashback { get; }
        public bool HasBlindBet { get; }
        public bool CanBlindBet { get; }
        public bool BlindBetAccepted { get; }
        public int BlindBetBonusGold { get; }
        public float BlindBetHealthCostFraction { get; }
        public bool CanBegin { get; }

        /// <summary><b>잔액까지 본 값이다.</b> 참이면 누르는 즉시 예치가 성립한다.</summary>
        public bool CanDeposit { get; }

        public bool CanReroll { get; }
        public bool CanSelect { get; }

        /// <summary>지금 예치한다면 낼 금액.</summary>
        public int DepositCost { get; }

        /// <summary>이미 낸 원금. 환급 계산의 기준이다.</summary>
        public int DepositAmount { get; }

        public int RerollCost { get; }
        public int RerollCount { get; }

        /// <summary>정산에서 실제 환급된 예치금. 탈락이면 0.</summary>
        public int SettlementRefund { get; }

        /// <summary>확정한 후보의 id. 고르지 않았으면 빈 문자열이다.</summary>
        public string SelectedId { get; }

        public string[] CandidateIds { get; }
        public bool[] Locked { get; }

        public TrainingBlessingTrialView(
            TrainingBlessingTrialPhase phase,
            int cycle,
            int round,
            string rankProfile,
            bool lowerHalfEligible,
            bool hasInterest,
            bool hasLowBuy,
            bool hasCashback,
            bool canBegin,
            bool canDeposit,
            bool canReroll,
            bool canSelect,
            int depositCost,
            int depositAmount,
            int rerollCost,
            int rerollCount,
            int settlementRefund,
            string selectedId,
            string[] candidateIds,
            bool[] locked,
            bool hasBlindBet = false,
            bool canBlindBet = false,
            bool blindBetAccepted = false,
            int blindBetBonusGold = 0,
            float blindBetHealthCostFraction = 0f)
        {
            Phase = phase;
            Cycle = cycle;
            Round = round;
            RankProfile = TrainingBlessingTrialRank.IsDefined(rankProfile)
                ? rankProfile
                : TrainingBlessingTrialRank.None;
            LowerHalfEligible = lowerHalfEligible;
            HasInterest = hasInterest;
            HasLowBuy = hasLowBuy;
            HasCashback = hasCashback;
            HasBlindBet = hasBlindBet;
            CanBlindBet = canBlindBet;
            BlindBetAccepted = blindBetAccepted;
            BlindBetBonusGold = blindBetBonusGold;
            BlindBetHealthCostFraction = blindBetHealthCostFraction;
            CanBegin = canBegin;
            CanDeposit = canDeposit;
            CanReroll = canReroll;
            CanSelect = canSelect;
            DepositCost = depositCost;
            DepositAmount = depositAmount;
            RerollCost = rerollCost;
            RerollCount = rerollCount;
            SettlementRefund = settlementRefund;
            SelectedId = selectedId ?? string.Empty;
            CandidateIds = candidateIds == null ? Array.Empty<string>() : (string[])candidateIds.Clone();

            // 두 배열은 같은 후보를 가리킨다. 길이가 어긋나면 화면이 잠금 표시를 한 칸씩 밀어 그린다.
            Locked = new bool[CandidateIds.Length];
            if (locked != null)
            {
                int shared = locked.Length < Locked.Length ? locked.Length : Locked.Length;
                Array.Copy(locked, Locked, shared);
            }

            for (int i = 0; i < CandidateIds.Length; i++)
            {
                CandidateIds[i] ??= string.Empty;
            }
        }
    }
}

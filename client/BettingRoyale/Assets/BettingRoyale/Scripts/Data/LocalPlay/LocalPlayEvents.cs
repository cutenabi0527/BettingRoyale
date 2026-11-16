using BettingRoyale.Core;

namespace BettingRoyale.Data.LocalPlay
{
    public readonly struct LocalPlaySettingsChangeRequested : IGameEvent
    {
        public LocalPlaySettingsChangeRequested(LocalPlaySettings settings)
        {
            Settings = settings;
        }

        public LocalPlaySettings Settings { get; }
    }

    public readonly struct LocalPlayMatchStartRequested : IGameEvent { }

    public readonly struct LocalPlayPhaseSkipRequested : IGameEvent { }

    public readonly struct LocalPlayAugmentSelectionCompletedRequested : IGameEvent
    {
        public LocalPlayAugmentSelectionCompletedRequested(string candidateId)
        {
            CandidateId = candidateId ?? string.Empty;
        }

        public string CandidateId { get; }
    }

    public readonly struct LocalPlayAugmentRerollRequested : IGameEvent { }

    /// <summary>
    /// 후보 하나의 잠금을 켜거나 끈다 (S-GD-S46S28). 상한(2개)·선택 이후 거부는 도메인이 판단하므로
    /// 화면은 <b>누른 자리 번호만</b> 싣는다.
    ///
    /// <para>
    /// 잠금은 선택과 달리 <b>id 가 아니라 자리 번호</b>다 — 재추첨하면 같은 자리의 축복이 바뀌고,
    /// 잠긴 자리는 그대로 남는다. 사용자가 누르는 대상은 "그 카드" 가 아니라 "그 칸" 이다.
    /// </para>
    /// </summary>
    public readonly struct LocalPlayAugmentLockToggleRequested : IGameEvent
    {
        public LocalPlayAugmentLockToggleRequested(int candidateIndex)
        {
            CandidateIndex = candidateIndex;
        }

        /// <summary>0부터 시작하는 후보 자리. 범위 밖 값도 그대로 싣고 거부는 상태가 한다.</summary>
        public int CandidateIndex { get; }
    }

    public readonly struct LocalPlaySessionChanged : IGameEvent
    {
        public LocalPlaySessionChanged(LocalPlaySessionSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public LocalPlaySessionSnapshot Snapshot { get; }
    }
}

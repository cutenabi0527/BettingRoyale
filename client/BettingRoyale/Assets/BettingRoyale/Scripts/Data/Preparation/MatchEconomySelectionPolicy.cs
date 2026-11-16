using System;
using BettingRoyale.Data.Economy;

namespace BettingRoyale.Data.Preparation
{
    /// <summary>
    /// 제품 경로의 <see cref="IPreparationSelectionPolicy"/> — <b>축복 추첨 상태를 그대로 본다</b>.
    ///
    /// <para>
    /// <c>MatchEconomyState.IsSelectionDone</c> 한 줄을 감싼다. 감싸는 이유는 <b>제품이 아닌
    /// 경로가 다른 답을 줄 수 있어야 하기 때문</b>이다(훈련 세션 —
    /// <see cref="AlwaysConfirmedSelectionPolicy"/>). 게이트가 경제를 직접 읽으면 그 갈래가
    /// 사라져 훈련에서 모루가 열리지 않는다.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 참가자가 아닌 id 로 부르면 던진다</b> — <c>MatchEconomyState</c> 가 그렇게 짜여 있다.
    /// 부르는 쪽(게이트)이 참가자 판정을 먼저 한다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class MatchEconomySelectionPolicy : IPreparationSelectionPolicy
    {
        private readonly MatchEconomyState economy;

        public MatchEconomySelectionPolicy(MatchEconomyState economy)
        {
            this.economy = economy ?? throw new ArgumentNullException(nameof(economy));
        }

        public bool IsSelectionConfirmed(int playerId) => economy.IsSelectionDone(playerId);
    }
}

namespace BettingRoyale.Data.Preparation
{
    /// <summary>
    /// <b>언제나 확정된 것으로 본다.</b> 제품 축복 추첨을 돌리지 않는 경로가 쓴다.
    ///
    /// <para>
    /// 훈련 세션(<c>TrainingPracticeState</c>)이 그렇다 — 연습 축복은 <c>BlessingLoadout</c> 에
    /// 직접 넣고 <c>MatchEconomyState</c> 의 후보를 확정하지 않으므로, 제품 규칙을 그대로 걸면
    /// <b>모루가 영원히 열리지 않는다.</b>
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 제품 경로에 쓰지 않는다.</b> 쓰면 축복을 고르기 전에 강화가 열린다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class AlwaysConfirmedSelectionPolicy : IPreparationSelectionPolicy
    {
        /// <summary>상태가 없으므로 하나를 돌려쓴다.</summary>
        public static readonly AlwaysConfirmedSelectionPolicy Instance = new AlwaysConfirmedSelectionPolicy();

        public bool IsSelectionConfirmed(int playerId) => true;
    }
}

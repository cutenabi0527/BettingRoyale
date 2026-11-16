namespace BettingRoyale.Data.Roulette
{
    /// <summary>
    /// 한 판이 끝났을 때 한 사람의 결과 (S-GD-7VKZA5).
    ///
    /// <para>
    /// <b>건 돈은 이미 빠져 있다.</b> 그래서 지갑에 해야 할 일은 <see cref="Payout"/> 을
    /// <b>더하는 것뿐</b>이다 — 빼는 일은 없다.
    /// </para>
    /// </summary>
    public readonly struct RouletteSettlement
    {
        public readonly int PlayerId;

        /// <summary>이 판에 건 돈 전부. 여러 숫자에 걸었으면 그 합이다.</summary>
        public readonly int Staked;

        /// <summary>그중 적중 숫자에 건 금액. 0 이면 전부 틀렸다.</summary>
        public readonly int StakedOnWinner;

        /// <summary>
        /// 돌려받는 총액. <c>적중액 × (숫자+1)</c> 이고 틀렸으면 0 이다.
        /// <b>건 돈이 포함된 금액</b>이라 순이익이 아니다.
        /// </summary>
        public readonly int Payout;

        public RouletteSettlement(int playerId, int staked, int stakedOnWinner, int payout)
        {
            PlayerId = playerId;
            Staked = staked;
            StakedOnWinner = stakedOnWinner;
            Payout = payout;
        }

        /// <summary>순손익. 양수면 땄다.</summary>
        public int Net => Payout - Staked;

        /// <summary>
        /// 축복 트리거 <c>ON_ROULETTE_LOSS</c> 가 쓸 손실액 (S-GD-EYJEH7).
        /// <b>딴 판은 손실이 아니다</b> — 0 이다.
        /// </summary>
        public int Loss => Net < 0 ? -Net : 0;

        public override string ToString() =>
            $"p{PlayerId} 건돈 {Staked} · 회수 {Payout} · 손익 {Net:+#;-#;0}";
    }
}

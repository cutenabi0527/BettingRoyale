namespace BettingRoyale.Data.Chest
{
    /// <summary>
    /// 보물상자의 상태 (S-GD-5Y4PTJ).
    ///
    /// <code>
    /// 대기 → 개봉(진행 중) → 보상 → 재생성 → 대기
    ///           ↓
    ///     이동/공격/대시/피격 시 취소 → 대기
    /// </code>
    /// </summary>
    public enum ChestState
    {
        /// <summary>대기. 아무도 개봉하고 있지 않다.</summary>
        Idle = 0,

        /// <summary>개봉 진행 중. 개봉자가 그 자리에 멈춰 있어야 한다.</summary>
        Opening = 1,

        /// <summary>개봉 완료. 보상을 받을 수 있다. 받고 나면 재생성으로 넘어간다.</summary>
        Rewarded = 2,

        /// <summary>재생성 대기 중. 이 동안에는 개봉할 수 없다.</summary>
        Respawning = 3,
    }

    /// <summary>개봉이 취소된 이유 (S-GD-5Y4PTJ).</summary>
    public enum ChestCancelReason
    {
        /// <summary>개봉자가 움직였다.</summary>
        Moved = 0,

        /// <summary>개봉자가 피격당했다.</summary>
        Damaged = 1,

        /// <summary>
        /// 전투 라운드가 끝나 상자가 제거됐다 (S-GD-MQP6AS, Jira S15P21A606-32).
        ///
        /// <b>개봉자의 잘못이 아니다.</b> 화면이 <see cref="Moved"/>·<see cref="Damaged"/> 와 같은
        /// 문구를 쓰면 플레이어는 자기가 뭘 잘못한 줄 안다 — 문구를 구분해야 한다.
        ///
        /// ⚠️ <b>값은 뒤에만 붙인다.</b> 이 열거형은 <c>ChestOpenCancelled</c>·<c>ChestOpenCancelCommand</c>
        /// 계약에 실려 회선과 저장 경계를 넘을 수 있다. 중간에 끼우면 배포된 값의 뜻이 밀린다.
        /// </summary>
        RoundEnded = 2,
        /// <summary>개봉자가 공격했다.</summary>
        Attacked = 3,

        /// <summary>개봉자가 대시했다.</summary>
        Dashed = 4,
    }
}

namespace BettingRoyale.Data.Roulette
{
    /// <summary>
    /// <b>룰렛의 틱 ↔ 초 환산 정본</b> (S-GD-DP2DVC).
    ///
    /// <para>
    /// 도메인은 시간을 정수 틱으로만 센다(<see cref="RouletteConfig"/>) — float 누적은 기기마다
    /// 결과가 갈려 Host 와 클라이언트의 상태가 어긋나기 때문이다. 그래서 <b>"1틱이 몇 초인가" 는
    /// 도메인 밖의 값</b>이고, 계약(<c>RouletteStateChanged.RemainingTicks</c>)도 단위를 싣지 않는다.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 그 값이 여러 곳에 각자 박혀 있으면 시계가 조용히 갈린다.</b> 2026-09-15 기준으로
    /// 실제로 세 곳(<c>OnlineEconomyCoordinator</c> 의 Host 클럭, <c>RouletteScreen</c> 의
    /// 카운트다운, <c>OnlineCoreLoopOverlay</c> 의 카운트다운)이 각자 <c>20</c> 을 들고 있었다.
    /// 한 곳만 바꾸면 <b>화면의 남은 시간과 실제 마감이 어긋나는데 오류도 로그도 나지 않는다.</b>
    /// 그래서 여기 하나로 모은다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>아직 다 모으지 못했다.</b> 화면 쪽 소비처(<c>RouletteScreen</c>·
    /// <c>OnlineCoreLoopOverlay</c>·<c>EconomyDebugPanel</c>)만 여기를 보고, <b>Host 클럭인
    /// <c>OnlineEconomyCoordinator.RouletteTicksPerSecond</c> 는 여전히 자기 <c>20</c> 을
    /// 따로 들고 있다</b>(같은 작업에서 건드릴 수 없는 공용 파일이었다). 즉 <b>여기 값만 바꾸면
    /// Host 는 옛 속도로 재고 화면만 새 속도로 세어, 카운트다운이 조용히 갈린다</b> —
    /// 도메인 테스트는 Network 레이어를 컴파일하지 않아 이 어긋남을 <b>잡지 못한다</b>.
    /// 이 값을 바꿀 때는 <c>OnlineEconomyCoordinator</c> 도 같이 바꾸고,
    /// 거기서 이 상수를 참조하도록 옮기는 것이 남은 일이다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>값 자체는 임의값이다.</b> 20 을 고른 이유는 Fusion 기본 시뮬레이션 레이트와 같아서
    /// 나중에 시뮬레이션 틱으로 옮길 때 시간이 안 바뀌기 때문이다. 확정 스펙은 <b>초</b>만 정한다
    /// (베팅 10 · 회전 8 · 결과 2, S-GD-DP2DVC) — 틱 레이트는 정하지 않았다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public static class RouletteTiming
    {
        /// <summary>
        /// 룰렛이 한 초에 몇 틱 도는가. <b>Host 의 클럭과 화면의 카운트다운이 같은 값을 써야 한다.</b>
        /// </summary>
        public const int TicksPerSecond = 20;

        /// <summary>틱을 초로 옮긴다. 화면이 남은 시간을 그릴 때 쓴다.</summary>
        public static float ToSeconds(int ticks) => ticks / (float)TicksPerSecond;

        /// <summary>초를 틱으로 옮긴다. <b>내림</b>이다 — 모자란 시간에 판을 여는 쪽이 더 해롭다.</summary>
        public static int ToTicks(float seconds)
        {
            if (seconds <= 0f)
            {
                return 0;
            }

            double ticks = System.Math.Floor(seconds * TicksPerSecond);
            return ticks >= int.MaxValue ? int.MaxValue : (int)ticks;
        }
    }
}

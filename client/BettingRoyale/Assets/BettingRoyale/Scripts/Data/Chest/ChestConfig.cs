using System;

namespace BettingRoyale.Data.Chest
{
    /// <summary>
    /// 보물상자 설정 모델 (S-GD-5Y4PTJ).
    ///
    /// <b>⚠️ 이 클래스에는 일부러 <c>Default</c> 가 없다.</b>
    /// 세 값 모두 스펙에서 확정됐지만(5.0초 / 1.0초 / 1.0초), <b>확정값은 코드 상수가 아니라
    /// 데이터 파이프라인으로 들어온다</b>(D-041). S-GD-GQNA2B 가 *"확정값이 생겼다고 이 판단을
    /// 뒤집지 않는다"* 고 명시했다 — 기본값을 두면 그 숫자가 데이터를 우회해 사실상의 스펙으로
    /// 굳고, 하필 <c>= 0</c> 은 "지연 없음" 이라 <b>구현된 척하면서 규칙만 없는</b> 상태가 된다.
    /// 그래서 쓰는 쪽이 값을 <b>명시적으로 넘기게</b> 강제한다.
    ///
    /// <b>시간을 초가 아니라 틱으로 센다.</b> float 누적은 기기마다 결과가 갈려
    /// "2PC 에서 동일하다"(Jira S15P21A606-31)를 검증할 수 없게 된다. Fusion 도 틱 기반이다.
    ///
    /// UnityEngine 을 참조하지 않는다 (D-060).
    /// </summary>
    public sealed class ChestConfig
    {
        /// <param name="openDurationTicks">개봉 완료까지 필요한 틱 수. 5.0초로 확정 (S-GD-GQNA2B).</param>
        /// <param name="respawnDelayTicks">
        /// 보상을 받아 상자가 사라진 뒤 <b>같은 자리에 다시 놓이기까지</b>의 틱 수.
        /// 1.0초로 확정 (S-GD-5AZ6HN).
        /// </param>
        /// <param name="retryDelayTicks">
        /// 개봉이 <b>취소된 뒤</b> 그 사람이 <b>그 상자를</b> 다시 열 수 있게 되기까지의 틱 수.
        /// 1.0초로 확정 (S-GD-5AZ6HN).
        /// ⚠️ <paramref name="respawnDelayTicks"/> 와 <b>다른 값이다</b> — 아래 요약 참고.
        /// </param>
        public ChestConfig(int openDurationTicks, int respawnDelayTicks, int retryDelayTicks)
        {
            if (openDurationTicks < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(openDurationTicks), openDurationTicks, "개봉 시간은 1틱 이상이어야 합니다.");
            }

            if (respawnDelayTicks < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(respawnDelayTicks), respawnDelayTicks, "재생성 대기는 음수일 수 없습니다.");
            }

            if (retryDelayTicks < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(retryDelayTicks), retryDelayTicks, "재시도 지연은 음수일 수 없습니다.");
            }

            OpenDurationTicks = openDurationTicks;
            RespawnDelayTicks = respawnDelayTicks;
            RetryDelayTicks = retryDelayTicks;
        }

        /// <summary>개봉 완료까지의 틱. 5.0초 확정 (S-GD-GQNA2B) — 값은 데이터로 들어온다.</summary>
        public int OpenDurationTicks { get; }

        /// <summary>
        /// <b>상자</b>가 사라진 뒤 다시 놓이기까지의 틱. 1.0초 확정 (S-GD-5AZ6HN).
        /// </summary>
        public int RespawnDelayTicks { get; }

        /// <summary>
        /// 개봉이 취소된 <b>사람</b>이 <b>그 상자를</b> 다시 열 수 있게 되기까지의 틱.
        /// 1.0초 확정 (S-GD-5AZ6HN).
        ///
        /// <para>
        /// ⚠️ <b><see cref="RespawnDelayTicks"/> 와 혼동하기 쉽다. 둘 다 1초인데 다른 값이다.</b>
        /// </para>
        ///
        /// <list type="bullet">
        /// <item><b>재시도 지연</b> — 개봉이 <b>끊긴 뒤</b> 다시 열 수 있을 때까지. <b>사람</b>에게 붙는다</item>
        /// <item><b>재생성 지연</b> — 상자가 <b>사라진 뒤</b> 다시 나타날 때까지. <b>상자</b>에 붙는다</item>
        /// </list>
        ///
        /// <para>
        /// <b>다른 사람은 즉시 열 수 있다</b> — 지연은 취소당한 본인에게만 걸린다.
        /// 그리고 <b>그 상자에만</b> 걸린다 — 다른 상자는 바로 열 수 있다.
        /// </para>
        /// </summary>
        public int RetryDelayTicks { get; }

        public override string ToString() =>
            $"개봉 {OpenDurationTicks}틱 / 재생성 {RespawnDelayTicks}틱 / 재시도 {RetryDelayTicks}틱";
    }
}

using System;

namespace BettingRoyale.Data.Roulette
{
    /// <summary>
    /// 룰렛 한 판의 시간과 최소 베팅 (S-GD-7VKZA5).
    ///
    /// <para>
    /// <b>시간을 초가 아니라 틱으로 센다.</b> float 누적은 기기마다 결과가 갈려
    /// Host 와 클라이언트의 상태가 어긋난다 — <c>ChestConfig</c> 와 같은 이유다.
    /// </para>
    ///
    /// <para>
    /// <b>이 클래스에는 일부러 <c>Default</c> 가 없다.</b> 확정값은 코드 상수가 아니라
    /// 데이터로 들어온다(D-041). 임시값은 <see cref="PlaceholderRouletteConfig"/> 에 있다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class RouletteConfig
    {
        /// <param name="betTicks">베팅을 받는 시간. 이 동안만 걸 수 있다.</param>
        /// <param name="spinTicks">회전 연출 시간. <b>결과는 회전이 시작될 때 이미 정해져 있다.</b></param>
        /// <param name="resultTicks">적중 숫자를 보여주는 시간.</param>
        /// <param name="minBet">한 번에 걸 수 있는 최소 금액.</param>
        /// <param name="openMarginTicks">
        /// <b>한 판을 다 돌고도 이만큼은 남아야 새 판을 연다</b> (기획 확정 2026-09-10).
        /// 남은 시간이 <c>한 판 + 이 여유</c> 이하면 열지 않는다 —
        /// <b>룰렛이 돌다가 전투 라운드로 넘어가는 것을 막는다.</b>
        ///
        /// <para>
        /// 싸이클 길이는 바뀔 수 있으므로 <b>절대 시각이 아니라 여유분으로 둔다</b>.
        /// 판정은 <see cref="MinTicksToOpen"/> 이 싸이클에서 자동으로 계산한다.
        /// </para>
        /// </param>
        /// <param name="startDelayTicks">
        /// <b>준비 단계가 시작되고 이만큼 지나야 첫 판이 열린다</b> (기획 확정 2026-09-10).
        /// 그동안은 축복을 고르는 구간이라 이동·상호작용이 막혀 있다.
        ///
        /// <para><b>첫 판에만 걸린다.</b> 그 뒤 판들은 앞 판이 끝나는 대로 이어서 연다.</para>
        /// </param>
        public RouletteConfig(
            int betTicks, int spinTicks, int resultTicks, int minBet, int openMarginTicks, int startDelayTicks = 0)
        {
            if (betTicks < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(betTicks), betTicks, "베팅 시간은 1틱 이상이어야 합니다.");
            }

            if (spinTicks < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(spinTicks), spinTicks, "회전 시간은 1틱 이상이어야 합니다.");
            }

            if (resultTicks < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resultTicks), resultTicks, "결과 표시 시간은 1틱 이상이어야 합니다.");
            }

            if (minBet < 1)
            {
                // 0 이면 아무것도 안 걸고 판을 어지럽힐 수 있다.
                throw new ArgumentOutOfRangeException(
                    nameof(minBet), minBet, "최소 베팅은 1 이상이어야 합니다.");
            }

            if (openMarginTicks < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(openMarginTicks), openMarginTicks, "여유 시간은 음수일 수 없습니다.");
            }

            BetTicks = betTicks;
            SpinTicks = spinTicks;
            ResultTicks = resultTicks;
            MinBet = minBet;
            if (startDelayTicks < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(startDelayTicks), startDelayTicks, "첫 판 지연은 음수일 수 없습니다.");
            }

            OpenMarginTicks = openMarginTicks;
            StartDelayTicks = startDelayTicks;
        }

        public int BetTicks { get; }

        public int SpinTicks { get; }

        public int ResultTicks { get; }

        public int MinBet { get; }

        /// <summary>한 판을 다 돌고도 남아 있어야 하는 여유 틱.</summary>
        public int OpenMarginTicks { get; }

        /// <summary>준비 단계 시작 후 첫 판이 열리기까지 기다리는 틱. <b>첫 판에만 걸린다.</b></summary>
        public int StartDelayTicks { get; }

        /// <summary>한 판이 처음부터 끝까지 도는 데 걸리는 틱.</summary>
        public int CycleTicks => BetTicks + SpinTicks + ResultTicks;

        /// <summary>
        /// <b>새 판을 열려면 남은 시간이 이 값보다 커야 한다</b> (기획 확정 2026-09-10).
        ///
        /// <para>
        /// <c>한 판 + 여유</c> 다. <b>싸이클을 바꾸면 이 값도 자동으로 따라간다</b> —
        /// 시간값을 조정할 때 두 곳을 고칠 일이 없다.
        /// </para>
        /// </summary>
        public int MinTicksToOpen => CycleTicks + OpenMarginTicks;

        public override string ToString() =>
            $"룰렛 베팅 {BetTicks}t · 회전 {SpinTicks}t · 결과 {ResultTicks}t · 최소 {MinBet}";
    }

    /// <summary>
    /// ⚠️ <b>임시 시간값</b> (S-GD-7VKZA5). 목업 값을 틱으로 옮긴 것이고,
    /// <b>우리 준비 90초에 맞는지는 검증되지 않았다.</b>
    ///
    /// <para>
    /// 축복 선택 30초를 빼면 60초가 남는데 한 판이 약 20초라 세 판이 돈다.
    /// 플레이로 확인이 필요하다.
    /// </para>
    /// </summary>
    public static class PlaceholderRouletteConfig
    {
        public const int BetSeconds = 10;
        public const int SpinSeconds = 8;
        public const int MinBet = 10;
        public const int OpenMarginSeconds = 10;

        /// <summary>
        /// 첫 판까지 기다리는 시간 (기획 확정 2026-09-10).
        ///
        /// <para>
        /// <b>축복 선택과 겹치는 것이 의도다.</b> 축복 선택은 <b>최대</b> 30초이고 그동안 이동·상호작용이
        /// 막히는데, 10초 안에 빨리 고르면 <b>첫 판부터 참여할 수 있다.</b> 끝까지 고민하면
        /// 첫 판을 놓친다 — <b>선택 속도가 룰렛 기회로 이어지는 트레이드오프</b>다.
        /// </para>
        /// </summary>
        public const int StartDelaySeconds = 10;

        /// <summary>결과 표시. 목업은 1.6초인데 틱으로 세려고 2초로 올렸다. ⚠️ 임의값.</summary>
        public const int ResultSeconds = 2;

        /// <param name="ticksPerSecond">틱 레이트. 부르는 쪽이 준다 — 도메인은 Fusion 설정을 모른다.</param>
        public static RouletteConfig Create(int ticksPerSecond) => new RouletteConfig(
            betTicks: checked(BetSeconds * ticksPerSecond),
            spinTicks: checked(SpinSeconds * ticksPerSecond),
            resultTicks: checked(ResultSeconds * ticksPerSecond),
            minBet: MinBet,
            openMarginTicks: checked(OpenMarginSeconds * ticksPerSecond),
            startDelayTicks: checked(StartDelaySeconds * ticksPerSecond));
    }
}

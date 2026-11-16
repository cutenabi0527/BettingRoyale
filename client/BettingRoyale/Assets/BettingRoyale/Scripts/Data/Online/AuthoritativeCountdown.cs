using System;

namespace BettingRoyale.Data.Online
{
    /// <summary>
    /// 한 카운트다운이 "같은 카운트다운" 으로 이어지는 범위. 이 값이 바뀌면 이어 세지 않고 새로 맞춘다.
    /// </summary>
    /// <remarks>
    /// <paramref name="Source"/> 는 Local Play 와 온라인처럼 <b>서로 다른 단계 열거형</b>을 가르는 칸이다.
    /// 두 경로의 <c>Phase</c> 정수값이 우연히 겹쳐도 다른 카운트다운으로 읽히게 한다.
    /// </remarks>
    public readonly struct CountdownScope : IEquatable<CountdownScope>
    {
        public CountdownScope(int source, int phase, int round)
        {
            Source = source;
            Phase = phase;
            Round = round;
        }

        public int Source { get; }
        public int Phase { get; }
        public int Round { get; }

        public bool Equals(CountdownScope other) =>
            Source == other.Source && Phase == other.Phase && Round == other.Round;

        public override bool Equals(object obj) => obj is CountdownScope other && Equals(other);

        public override int GetHashCode() => (Source * 397 ^ Phase) * 397 ^ Round;

        public static bool operator ==(CountdownScope left, CountdownScope right) => left.Equals(right);

        public static bool operator !=(CountdownScope left, CountdownScope right) => !left.Equals(right);
    }

    /// <summary>
    /// 권위 피어가 확정한 남은 시간을 <b>화면이 이어서 세는</b> 시계 (S15P21A606-208).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 client 가 직접 세는가.</b> 예전에는 "client 가 자기 시계로 보간하면 두 PC 의 숫자가
    /// 갈린다" 는 이유로 <b>받은 값만</b> 그렸다. 그런데 코어루프 스냅샷은 경제 상태와 같은
    /// <c>SendReliableDataToPlayer</c> 대기열을 쓰고, 준비 단계에는 경제 쪽이 초당 5~20건을
    /// 밀어넣는다. 그 결과 초당 1건짜리 스냅샷이 <b>최대 2분까지 밀렸고</b>(2026-09-17 diag 실측)
    /// 받은 값만 그리던 화면은 그동안 <b>멈춰 있다가 건너뛰었다.</b> 안 세는 쪽이 오히려 더 갈린다.
    /// </para>
    /// <para>
    /// <b>갈리지 않게 하는 두 규칙.</b> ① 같은 <see cref="CountdownScope"/> 안에서는 <b>절대
    /// 늘지 않는다</b> — 늦게 도착한 옛 스냅샷이 시계를 되돌리지 못한다. ② 범위가 바뀌면
    /// (단계·라운드 전환) 무조건 권위 값으로 <b>다시 맞춘다</b> — 누적 오차가 라운드를 넘어가지 않는다.
    /// 그래서 두 PC 의 차이는 "대기열에 밀린 시간" 이 아니라 "왕복 지연" 으로 줄어든다.
    /// </para>
    /// <para>
    /// ⚠️ <b>권위가 아니다.</b> 단계 전환·정산은 여전히 Host 가 정한다. 이 시계는 <b>표시 전용</b>이며
    /// 0 에 닿아도 아무것도 끝내지 않는다.
    /// </para>
    /// </remarks>
    public struct AuthoritativeCountdown
    {
        private CountdownScope scope;

        /// <summary>권위 값을 한 번이라도 받았는가. 받기 전에는 세지 않는다.</summary>
        public bool HasValue { get; private set; }

        /// <summary>지금 화면이 들고 있는 남은 시간(초). 항상 0 이상이다.</summary>
        public float Seconds { get; private set; }

        /// <summary>화면에 찍을 초. 남은 시간이 조금이라도 있으면 1 로 올린다.</summary>
        public int DisplaySeconds => HasValue ? (int)Math.Ceiling(Math.Max(0f, Seconds)) : 0;

        /// <summary>시계를 끈다. 켜기 전까지 <see cref="Advance"/> 는 아무 일도 하지 않는다.</summary>
        public void Clear()
        {
            HasValue = false;
            Seconds = 0f;
            scope = default;
        }

        /// <summary>권위 피어가 확정한 값을 받는다.</summary>
        public void Apply(CountdownScope authoritativeScope, float authoritativeSeconds)
        {
            if (float.IsNaN(authoritativeSeconds) || float.IsInfinity(authoritativeSeconds))
            {
                // 깨진 값으로 화면을 덮지 않는다. 이미 세고 있던 시계는 그대로 이어 간다.
                return;
            }

            float sample = Math.Max(0f, authoritativeSeconds);

            if (!HasValue || scope != authoritativeScope)
            {
                scope = authoritativeScope;
                Seconds = sample;
                HasValue = true;
                return;
            }

            // 같은 범위 안에서는 줄어들기만 한다. 대기열에 밀린 옛 스냅샷은 지금보다 큰 값을
            // 들고 오는데, 그것을 받아들이면 화면의 숫자가 거꾸로 올라간다.
            if (sample < Seconds)
            {
                Seconds = sample;
            }
        }

        /// <summary>프레임 시간만큼 흘린다. 권위 값을 받기 전에는 흐르지 않는다.</summary>
        public void Advance(float deltaSeconds)
        {
            if (!HasValue
                || deltaSeconds <= 0f
                || float.IsNaN(deltaSeconds)
                || float.IsInfinity(deltaSeconds))
            {
                return;
            }

            Seconds = Math.Max(0f, Seconds - deltaSeconds);
        }
    }
}

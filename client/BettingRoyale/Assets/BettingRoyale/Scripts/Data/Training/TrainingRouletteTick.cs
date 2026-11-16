using System;
using BettingRoyale.Data.Contracts;

namespace BettingRoyale.Data.Training
{
    /// <summary>
    /// 훈련 세션이 한 번에 여러 틱을 돌렸을 때 <b>일어난 일 전부</b>.
    ///
    /// <para>
    /// <see cref="Roulette.RouletteTickOutcome"/> 은 <b>한 틱</b>의 결과다. 훈련 어댑터는 프레임
    /// 간격만큼 틱을 접어 부르므로 그대로 쓰면 <b>중간 판의 정산이 사라진다</b> — 돈은 움직였는데
    /// 화면은 모르는 상태가 된다. 그래서 이 타입이 정산·골드 변화를 모아서 나른다.
    /// </para>
    ///
    /// <para>규칙을 새로 만들지 않는다 — 전부 제품 게이트가 만든 값 그대로다.</para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public readonly struct TrainingRouletteTick
    {
        /// <summary>아무 일도 없었을 때의 값. <b>배열은 <c>null</c> 이 아니다.</b></summary>
        public static readonly TrainingRouletteTick None = new(
            false, default, Array.Empty<RouletteSettled>(), Array.Empty<GoldChanged>());

        public TrainingRouletteTick(
            bool hasStateChange,
            RouletteStateChanged state,
            RouletteSettled[] settlements,
            GoldChanged[] golds)
        {
            HasStateChange = hasStateChange;
            State = state;
            Settlements = settlements ?? Array.Empty<RouletteSettled>();
            Golds = golds ?? Array.Empty<GoldChanged>();
        }

        /// <summary>이번 호출에서 바퀴 상태가 한 번이라도 바뀌었다.</summary>
        public bool HasStateChange { get; }

        /// <summary>마지막 틱이 끝난 시점의 상태. 화면은 이것만 그리면 된다.</summary>
        public RouletteStateChanged State { get; }

        /// <summary>이번 호출에서 끝난 판들의 정산. 환불 라운드도 여기에 들어온다.</summary>
        public RouletteSettled[] Settlements { get; }

        /// <summary>그 정산으로 움직인 골드. <see cref="Settlements"/> 와 같은 순서·같은 길이다.</summary>
        public GoldChanged[] Golds { get; }
    }
}

using System;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 스탯을 <b>어떻게 계산하는가</b> (축복 시스템 기획 §1.2).
    ///
    /// <para>
    /// ⚠️ <b>이 열거형은 스탯의 "종류" 가 아니라 "계산 방식" 이다.</b> 스탯 자체는 문자열 id 로
    /// 다루며 <b>enum 으로 만들지 않는다</b>(§0) — 축복은 계속 늘어나므로 스탯을 추가할 때마다
    /// 컴파일이 필요하면 "데이터만 고쳐서 추가" 가 성립하지 않는다.
    /// </para>
    /// </summary>
    public enum StatKind
    {
        /// <summary>
        /// <c>base + Σ기여</c>. 방어력·관통·치명타 확률·치명타 배율·흡혈·공격력이 쓴다.
        /// </summary>
        Flat = 0,

        /// <summary>
        /// <c>base × (1 + Σadd) × Π(mul)</c>. 공속·이속·사거리·투사체 속도·개봉 시간·받는 피해가 쓴다.
        /// </summary>
        Multiplier = 1,

        /// <summary>
        /// <c>max(base, 기여 중 최댓값)</c>. 공격 폭·회복 상한 비율이 쓴다.
        /// <b>더하지 않는다</b> — 여러 개를 들어도 가장 큰 하나만 산다.
        /// </summary>
        Max = 2,

        /// <summary>
        /// <c>min(base, 기여 중 최솟값)</c>. 받는 피해 상한·고정이 쓴다. 기본값은 ∞(없음)다.
        /// </summary>
        Min = 3,
    }

    /// <summary>최종값을 정수로 맞추는 방식 (§1.1 <c>round</c>).</summary>
    public enum StatRounding
    {
        None = 0,
        Floor = 1,
        Ceil = 2,
    }

    /// <summary>
    /// 스탯 한 종류의 정의 (축복 시스템 기획 §1.1). <b>엑셀 <c>stats</c> 시트 한 행</b>에 대응한다 —
    /// <c>id | display_name | base_value | kind | min | max</c>.
    ///
    /// <para>
    /// <b>이 타입에 수치를 박지 않는다.</b> 값은 전부 데이터에서 온다(D-041, 기획 §0).
    /// 새 스탯은 <c>stats.json</c> 에 한 줄 추가하면 끝이고 코드는 바뀌지 않는다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class StatDefinition
    {
        /// <param name="min">하한. 없으면 <c>null</c>.</param>
        /// <param name="max">상한. 없으면 <c>null</c>. <b>초과분은 버린다</b>(§1.4 치명타 100%).</param>
        /// <param name="rounding">
        /// 엑셀 <c>stats</c> 시트에는 아직 이 열이 없다 — 전부 <see cref="StatRounding.None"/> 이다.
        /// 기획 §1.1 이 선언한 칸이라 미리 열어 둔다.
        /// </param>
        public StatDefinition(
            string id,
            string displayName,
            float baseValue,
            StatKind kind,
            float? min = null,
            float? max = null,
            StatRounding rounding = StatRounding.None)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("스탯 id 가 비었습니다.", nameof(id));
            }

            if (min.HasValue && max.HasValue && min.Value > max.Value)
            {
                throw new ArgumentException(
                    $"스탯 {id} 의 하한({min})이 상한({max})보다 큽니다.", nameof(min));
            }

            Id = id;
            DisplayName = displayName ?? id;
            BaseValue = baseValue;
            Kind = kind;
            Min = min;
            Max = max;
            Rounding = rounding;
        }

        /// <summary>문자열 키. <b>대소문자를 구분한다</b> — 비교가 <c>Ordinal</c> 이다.</summary>
        public string Id { get; }

        public string DisplayName { get; }

        /// <summary>
        /// 기여가 하나도 없을 때의 값.
        ///
        /// <para>
        /// ⚠️ <see cref="StatKind.Multiplier"/> 의 기본값은 보통 <b>1.0</b> 이다 — 0 이면 무엇을
        /// 곱해도 0 이 된다. 다만 여기서 강제하지 않는다: 기본값은 데이터가 정하고,
        /// 무기별 기본값(검 25 / 활 15)처럼 <b>밖에서 주입되는 스탯</b>도 있다.
        /// </para>
        /// </summary>
        public float BaseValue { get; }

        public StatKind Kind { get; }

        public float? Min { get; }

        public float? Max { get; }

        public StatRounding Rounding { get; }

        /// <summary>계산이 끝난 값에 상·하한과 반올림을 적용한다.</summary>
        public float Clamp(float value)
        {
            if (Min.HasValue && value < Min.Value)
            {
                value = Min.Value;
            }

            if (Max.HasValue && value > Max.Value)
            {
                value = Max.Value;
            }

            switch (Rounding)
            {
                case StatRounding.Floor:
                    return (float)Math.Floor(value);

                case StatRounding.Ceil:
                    return (float)Math.Ceiling(value);

                default:
                    return value;
            }
        }

        public override string ToString() => $"{Id}({Kind}, base {BaseValue})";
    }
}

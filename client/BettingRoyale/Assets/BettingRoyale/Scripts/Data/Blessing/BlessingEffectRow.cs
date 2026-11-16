using System;
using System.Globalization;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 축복 계수 한 줄 (엑셀 <c>effects</c> 시트 한 행, 축복 시스템 기획 §8).
    ///
    /// <code>
    /// blessing_id | param_key | value | stack | condition
    ///  crossbow-t1 | attack-speed | 0.70 | MUL   |
    ///  sniper-t1   | damage-bonus | 0.15 | ADD   | DISTANCE_GTE(15)
    ///  nine-lives-t3 | immune_sources | DOT |     |
    /// </code>
    ///
    /// <para>
    /// <b>값을 문자열 그대로 들고 있는다.</b> <c>param_key</c> 는 스탯일 수도(<c>attack-power</c>)
    /// 행동 파라미터일 수도(<c>invuln_kind</c>) 있고, 값도 숫자·<c>TRUE</c>·<c>DOT</c> 처럼 제각각이다.
    /// 여기서 타입을 정해 버리면 <b>새 파라미터를 추가할 때마다 이 타입을 고쳐야 한다</b> —
    /// 기획 §0 이 금지하는 것이다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public readonly struct BlessingEffectRow
    {
        public BlessingEffectRow(
            string blessingId, string paramKey, string value, string stack = null, string condition = null)
        {
            if (string.IsNullOrWhiteSpace(blessingId))
            {
                throw new ArgumentException("축복 id 가 비었습니다.", nameof(blessingId));
            }

            if (string.IsNullOrWhiteSpace(paramKey))
            {
                throw new ArgumentException(
                    $"{blessingId} 의 param_key 가 비었습니다.", nameof(paramKey));
            }

            BlessingId = blessingId.Trim();
            ParamKey = paramKey.Trim();
            Value = (value ?? string.Empty).Trim();
            Stack = (stack ?? string.Empty).Trim();
            Condition = (condition ?? string.Empty).Trim();
        }

        public readonly string BlessingId;

        /// <summary>스탯 id 이거나 행동 파라미터 이름. 어느 쪽인지는 <see cref="StatRegistry"/> 가 안다.</summary>
        public readonly string ParamKey;

        public readonly string Value;

        /// <summary><c>ADD</c>·<c>MUL</c>·<c>MAX</c>·<c>MIN</c>. 비어 있을 수 있다.</summary>
        public readonly string Stack;

        /// <summary>
        /// 조건. 비어 있으면 상시다. <b>복합 조건이 있다</b> —
        /// 실제 데이터에 <c>DISTANCE_LTE(10) AND MOVING_TOWARD_TARGET</c> 이 들어 있다.
        /// <b>그래서 열거형으로 만들지 않는다.</b>
        /// </summary>
        public readonly string Condition;

        /// <summary>
        /// 조건부인가 (§3.3). <b>조건부 기여는 스탯 시트에 넣지 않는다</b> —
        /// 대상과 거리에 따라 매번 달라지므로 계산 시점에 평가해야 한다.
        /// </summary>
        public bool IsConditional => Condition.Length > 0;

        /// <summary>숫자로 읽는다. <b>문화권 무관</b>으로 파싱한다 — 데이터는 항상 <c>.</c> 소수점이다.</summary>
        public bool TryGetNumber(out float number) =>
            float.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);

        /// <summary><c>TRUE</c> 인가. 대소문자를 가리지 않는다.</summary>
        public bool IsTrue => string.Equals(Value, "TRUE", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 스택 방식. <b>비어 있으면 합연산</b>이다 — 기획 §1.3 이
        /// *"공속을 제외한 모든 스탯 = 합연산"* 이라고 정했다.
        ///
        /// <para><b>모르는 값이면 던진다</b> — 조용히 합연산으로 넘기면 패널티가 상쇄된다.</para>
        /// </summary>
        public StatStackMode ResolveStack()
        {
            if (Stack.Length == 0)
            {
                return StatStackMode.Add;
            }

            switch (Stack.ToUpperInvariant())
            {
                case "ADD":
                    return StatStackMode.Add;

                case "MUL":
                    return StatStackMode.Multiply;

                case "MAX":
                    return StatStackMode.Max;

                case "MIN":
                    return StatStackMode.Min;

                default:
                    throw new FormatException(
                        $"{BlessingId}.{ParamKey} 의 stack '{Stack}' 을 모릅니다. ADD·MUL·MAX·MIN 중 하나여야 합니다.");
            }
        }

        public override string ToString() =>
            $"{BlessingId}.{ParamKey} = {Value}"
            + (Stack.Length > 0 ? $" [{Stack}]" : string.Empty)
            + (IsConditional ? $" if {Condition}" : string.Empty);
    }
}

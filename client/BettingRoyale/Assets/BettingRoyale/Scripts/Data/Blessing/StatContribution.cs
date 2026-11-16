using System;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 기여 하나를 <b>어떻게 쌓는가</b> (축복 시스템 기획 §1.3, 엑셀 <c>effects</c> 시트의 <c>stack</c> 열).
    ///
    /// <para>
    /// <b>기본은 합연산이다.</b> 곱연산은 <b>공격 속도 패널티 셋뿐</b>이다(쇠뇌·발리스타·잔상).
    /// 합연산으로 두면 <c>쇠뇌(−30%) + 속사(+20%) = −10%</c> 가 되어 <b>패널티가 상쇄된다</b> —
    /// 곱연산이어야 공속을 아무리 쌓아도 비율만큼의 손해가 유지된다.
    /// </para>
    /// </summary>
    public enum StatStackMode
    {
        /// <summary>
        /// 합연산. <b>값은 최종 배율이 아니라 베이스 대비 증감분</b>이다(§1.2-B) —
        /// 10% 감소는 <c>0.90</c> 이 아니라 <c>-0.10</c> 이다.
        /// </summary>
        Add = 0,

        /// <summary>
        /// 곱연산. <b>여기만 최종 배율을 쓴다</b> — 30% 감소는 <c>0.70</c> 이다(§1.2-B 예외).
        /// </summary>
        Multiply = 1,

        /// <summary>가장 큰 하나만 산다. 공격 폭·회복 상한이 쓴다.</summary>
        Max = 2,

        /// <summary>
        /// 가장 작은 하나만 산다. <c>damage-taken-fixed</c>·<c>damage-taken-cap</c> 이 쓴다.
        ///
        /// <b>스택을 정하지 않으면 획득 순서에 따라 결과가 달라진다</b>(§2.5) —
        /// <c>fixed: 1</c> 과 <c>fixed: 2</c> 를 함께 들면 낮은 쪽인 1 이 이긴다.
        /// </summary>
        Min = 3,
    }

    /// <summary>
    /// 축복 하나가 스탯 하나에 주는 기여 (엑셀 <c>effects</c> 시트 한 행).
    ///
    /// <code>
    /// blessing_id | param_key | value | stack | condition
    ///  crossbow-t1 | attack-speed | 0.70 | MUL |
    /// </code>
    ///
    /// <para>
    /// <b><see cref="SourceBlessingId"/> 를 들고 다니는 이유</b>: 값이 이상할 때 어느 축복에서
    /// 왔는지 알아야 한다. 계산에는 쓰지 않지만 <b>진단이 없으면 데이터 오류를 못 찾는다</b> —
    /// 축복이 65종이고 계수가 194행이다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public readonly struct StatContribution
    {
        public StatContribution(string statId, float value, StatStackMode stack, string sourceBlessingId = null)
        {
            if (string.IsNullOrWhiteSpace(statId))
            {
                throw new ArgumentException("스탯 id 가 비었습니다.", nameof(statId));
            }

            StatId = statId;
            Value = value;
            Stack = stack;
            SourceBlessingId = sourceBlessingId ?? string.Empty;
        }

        /// <summary>어느 스탯에 붙는가. <c>stats</c> 시트의 <c>id</c> 와 같아야 한다.</summary>
        public readonly string StatId;

        /// <summary>
        /// 기여값. <see cref="StatStackMode.Add"/> 면 증감분, <see cref="StatStackMode.Multiply"/> 면
        /// 최종 배율이다 (§1.2-B).
        /// </summary>
        public readonly float Value;

        public readonly StatStackMode Stack;

        /// <summary>어느 축복에서 왔는가. 진단용이며 계산에 쓰지 않는다.</summary>
        public readonly string SourceBlessingId;

        public override string ToString() =>
            $"{StatId} {Value:+0.###;-0.###;0} ({Stack})"
            + (SourceBlessingId.Length > 0 ? $" ← {SourceBlessingId}" : string.Empty);
    }
}

using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 한 전투원의 <b>최종 스탯을 계산하는 자리</b> (축복 시스템 기획 §1.2·§1.3).
    ///
    /// <para>
    /// 축복이 준 기여를 모아 두고, 물으면 그때 계산해서 답한다. <b>계산식은 스탯의
    /// <see cref="StatKind"/> 가 정한다.</b>
    /// </para>
    ///
    /// <code>
    ///   FLAT        base + Σ기여
    ///   MULTIPLIER  base × (1 + Σadd) × Π(mul)
    ///   MAX         max(base, 기여 중 최댓값)
    ///   MIN         min(base, 기여 중 최솟값)
    /// </code>
    ///
    /// <para>
    /// <b>왜 곱연산이 <c>MULTIPLIER</c> 안에서만 따로 있는가</b>(§1.3): 합연산이면
    /// <c>쇠뇌(−30%) + 속사(+20%) = −10%</c> 가 되어 <b>패널티가 상쇄된다.</b> 곱연산이어야
    /// 공속을 아무리 쌓아도 비율만큼의 손해가 유지된다. 해당 축복은 셋뿐이다
    /// (쇠뇌 ×0.70 · 발리스타 ×0.70 · 잔상 ×0.72).
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 조건부 스탯은 여기에 캐시하지 않는다</b>(§3.4). 언더독·약자 멸시·저격수처럼
    /// 대상과 거리에 따라 켜지는 효과는 <b>계산 시점마다 조건을 평가</b>해야 하므로,
    /// 그 기여는 이 시트에 넣지 말고 피해 계산 쪽에서 매번 합친다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class StatSheet
    {
        private readonly StatRegistry registry;
        private readonly List<StatContribution> contributions = new List<StatContribution>();

        /// <summary>스탯별로 밖에서 주입한 기본값. 무기별 공격력(검 25 / 활 15)처럼 데이터 밖에서 온다.</summary>
        private readonly Dictionary<string, float> baseOverrides = new Dictionary<string, float>(StringComparer.Ordinal);

        public StatSheet(StatRegistry registry)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public IReadOnlyList<StatContribution> Contributions => contributions;

        /// <summary>
        /// 기여를 하나 얹는다.
        ///
        /// <para>
        /// <b>정의에 없는 스탯이면 던진다</b> — 오타 하나로 그 축복만 조용히 무효가 되는 것을 막는다.
        /// <b>스탯의 계산 방식과 맞지 않는 스택도 던진다</b> — 예를 들어 <c>FLAT</c> 스탯에
        /// 곱연산 기여를 붙이면 계산식에 그것을 넣을 자리가 없어 <b>값이 조용히 무시된다.</b>
        /// </para>
        /// </summary>
        public void Add(in StatContribution contribution)
        {
            Validate(contribution);
            contributions.Add(contribution);
        }

        /// <summary>
        /// <b>한 출처의 기여를 통째로 갈아 끼운다.</b> 걷어내기와 얹기가 <b>한 덩어리로 일어난다</b>.
        ///
        /// <para>
        /// <b>⚠️ 걷어낸 뒤에 얹다가 던지면 시트가 부르기 전보다 나빠진 채 남는다.</b>
        /// 강화처럼 "매 라운드 다시 얹는" 것은 이 경로를 써야 한다 — 데이터가 하나만 잘못돼도
        /// <b>멀쩡하던 기여까지 사라진 상태로 전투가 시작된다.</b>
        /// 그래서 <b>전부 검사한 뒤에</b> 걷어내고 얹는다.
        /// </para>
        /// </summary>
        public void ReplaceFrom(string sourceId, IReadOnlyList<StatContribution> replacements)
        {
            if (string.IsNullOrEmpty(sourceId))
            {
                throw new ArgumentException("출처 이름이 비었습니다.", nameof(sourceId));
            }

            if (replacements != null)
            {
                // 먼저 전부 검사한다. 여기서 던지면 시트는 손도 안 댄 상태다.
                for (int i = 0; i < replacements.Count; i++)
                {
                    Validate(replacements[i]);
                }
            }

            RemoveFrom(sourceId);

            if (replacements == null)
            {
                return;
            }

            for (int i = 0; i < replacements.Count; i++)
            {
                var source = replacements[i];

                // 출처를 이 이름으로 통일한다 — 다음에 걷어낼 때 이 이름으로 찾는다.
                contributions.Add(
                    new StatContribution(source.StatId, source.Value, source.Stack, sourceId));
            }
        }

        /// <summary>기여 하나가 이 시트에 들어올 수 있는지 본다. <b>상태를 바꾸지 않는다.</b></summary>
        private void Validate(in StatContribution contribution)
        {
            var definition = registry.Get(contribution.StatId);

            if (contribution.Stack == StatStackMode.Multiply && definition.Kind != StatKind.Multiplier)
            {
                throw new ArgumentException(
                    $"스탯 {definition.Id} 는 {definition.Kind} 인데 곱연산 기여가 붙었습니다"
                    + $"{Origin(contribution)}. 곱연산은 MULTIPLIER 스탯에만 쓸 수 있습니다 (기획 §1.3).",
                    nameof(contribution));
            }

            if (contribution.Stack == StatStackMode.Max && definition.Kind != StatKind.Max)
            {
                throw new ArgumentException(
                    $"스탯 {definition.Id} 는 {definition.Kind} 인데 MAX 스택 기여가 붙었습니다{Origin(contribution)}.",
                    nameof(contribution));
            }

            if (contribution.Stack == StatStackMode.Min && definition.Kind != StatKind.Min)
            {
                throw new ArgumentException(
                    $"스탯 {definition.Id} 는 {definition.Kind} 인데 MIN 스택 기여가 붙었습니다{Origin(contribution)}.",
                    nameof(contribution));
            }
        }

        public void Add(string statId, float value, StatStackMode stack, string sourceBlessingId = null) =>
            Add(new StatContribution(statId, value, stack, sourceBlessingId));

        /// <summary>
        /// 이 스탯의 기본값을 밖에서 정한다. <b>무기가 정하는 값</b>(검 공격력 25 / 활 15)처럼
        /// 스탯 시트에 고정으로 적을 수 없는 것에 쓴다.
        /// </summary>
        public void SetBase(string statId, float baseValue)
        {
            registry.Get(statId);   // 정의에 없는 스탯이면 여기서 걸린다
            baseOverrides[statId] = baseValue;
        }

        /// <summary>축복이 풀렸다. 라운드 시작마다 일시 효과를 걷어낼 때 쓴다 (S-GD-AN554W).</summary>
        public void RemoveFrom(string sourceBlessingId)
        {
            if (string.IsNullOrEmpty(sourceBlessingId))
            {
                return;
            }

            contributions.RemoveAll(c => string.Equals(c.SourceBlessingId, sourceBlessingId, StringComparison.Ordinal));
        }

        public void Clear() => contributions.Clear();

        /// <summary>
        /// 최종값. <b>상·하한과 반올림까지 적용된 값</b>이다.
        /// </summary>
        public float Get(string statId)
        {
            var definition = registry.Get(statId);
            float baseValue = baseOverrides.TryGetValue(statId, out float injected)
                ? injected
                : definition.BaseValue;

            return Evaluate(definition, baseValue);
        }

        /// <summary>무기 기준값으로 평가하되 저장된 기본값은 변경하지 않는다.</summary>
        public float Get(string statId, float baseValue) => Evaluate(registry.Get(statId), baseValue);

        /// <summary>
        /// 이 시트가 그 능력치를 아는가. 모르는 능력치로 <see cref="Get(string)"/> 을 부르면 던진다 —
        /// 엑셀 없이 도는 대역 계산표에는 치명타 확률·사거리 같은 능력치가 없다.
        /// </summary>
        public bool Knows(string statId) => registry.Contains(statId);

        private float Evaluate(StatDefinition definition, float baseValue)
        {
            string statId = definition.Id;
            switch (definition.Kind)
            {
                case StatKind.Flat:
                    return definition.Clamp(baseValue + SumOf(statId));

                case StatKind.Multiplier:
                    return definition.Clamp(baseValue * (1f + SumOf(statId)) * ProductOf(statId));

                case StatKind.Max:
                    return definition.Clamp(BestOf(statId, baseValue, takeLarger: true));

                case StatKind.Min:
                    return definition.Clamp(BestOf(statId, baseValue, takeLarger: false));

                default:
                    throw new InvalidOperationException(
                        $"모르는 계산 방식입니다: {definition.Kind} ({statId}).");
            }
        }

        /// <summary>합연산 기여의 합. <b>증감분이라 음수가 정상이다</b> (§1.2-B).</summary>
        private float SumOf(string statId)
        {
            float sum = 0f;

            for (int i = 0; i < contributions.Count; i++)
            {
                var c = contributions[i];

                if (c.Stack == StatStackMode.Add && string.Equals(c.StatId, statId, StringComparison.Ordinal))
                {
                    sum += c.Value;
                }
            }

            return sum;
        }

        /// <summary>곱연산 기여의 곱. <b>여기 값은 증감분이 아니라 최종 배율이다</b> (§1.2-B 예외).</summary>
        private float ProductOf(string statId)
        {
            float product = 1f;

            for (int i = 0; i < contributions.Count; i++)
            {
                var c = contributions[i];

                if (c.Stack == StatStackMode.Multiply && string.Equals(c.StatId, statId, StringComparison.Ordinal))
                {
                    product *= c.Value;
                }
            }

            return product;
        }

        /// <summary>
        /// 배율 스탯의 <b>곱연산(패널티) 몫</b> — <c>기본 × (1 + Σ) × Π</c> 의 Π. 배율 스탯이 아니면 1.
        /// </summary>
        /// <remarks>
        /// 시트 <b>밖</b>에서 오는 증감(소모품 물약·축복의 조건부 받는 피해 감소)을 합연산으로 붙일 때 쓴다(S-GD-EM82G5) — 시트 값에
        /// <c>증감 × Π</c> 를 더하면 증감이 Σ 안에 들어간 것과 같다. 따로 곱하면 패널티만 곱연산이라는 규칙(S-GD-SH74SC)이 깨진다.
        /// </remarks>
        public float PenaltyOf(string statId) =>
            registry.TryGet(statId, out StatDefinition definition) && definition.Kind == StatKind.Multiplier
                ? ProductOf(statId)
                : 1f;

        /// <summary>기여 중 가장 크거나 작은 하나. 기여가 없으면 기본값 그대로다.</summary>
        private float BestOf(string statId, float baseValue, bool takeLarger)
        {
            float best = baseValue;
            var wanted = takeLarger ? StatStackMode.Max : StatStackMode.Min;

            for (int i = 0; i < contributions.Count; i++)
            {
                var c = contributions[i];

                if (c.Stack != wanted || !string.Equals(c.StatId, statId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (takeLarger ? c.Value > best : c.Value < best)
                {
                    best = c.Value;
                }
            }

            return best;
        }

        private static string Origin(in StatContribution contribution) =>
            contribution.SourceBlessingId.Length > 0 ? $" (출처 {contribution.SourceBlessingId})" : string.Empty;

        public override string ToString() => $"스탯 시트 · 기여 {contributions.Count}건";
    }
}

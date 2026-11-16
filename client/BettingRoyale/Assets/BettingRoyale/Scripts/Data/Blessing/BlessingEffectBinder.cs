using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 축복 계수를 <b>적용한 결과</b>. 스탯에 들어간 것 말고 <b>남은 것들</b>이 여기 있다.
    /// </summary>
    public sealed class BlessingBindResult
    {
        internal BlessingBindResult(
            int appliedStatCount,
            List<BlessingEffectRow> conditional,
            List<BlessingEffectRow> behavioral)
        {
            AppliedStatCount = appliedStatCount;
            Conditional = conditional;
            Behavioral = behavioral;
        }

        /// <summary>스탯 시트에 실제로 들어간 기여 수.</summary>
        public int AppliedStatCount { get; }

        /// <summary>
        /// <b>조건부 스탯 기여</b> (§3.3). <b>스탯 시트에 넣지 않았다</b> — 대상·거리에 따라
        /// 매번 달라지므로 피해 계산 시점에 조건을 평가해서 합쳐야 한다.
        /// 캐시하면 언더독·저격수가 상시 켜진다.
        /// </summary>
        public IReadOnlyList<BlessingEffectRow> Conditional { get; }

        /// <summary>
        /// 스탯이 아닌 파라미터 — <c>trigger</c>·<c>invuln_kind</c>·<c>duration</c> 등.
        /// <b>다른 시스템이 읽는다</b>(트리거·무적·도트·상자·경제).
        /// </summary>
        public IReadOnlyList<BlessingEffectRow> Behavioral { get; }

        /// <summary>
        /// 행동 파라미터에서 면역 소스를 뽑는다 (§2.5 <c>immune_sources</c>).
        /// 여러 축복이 각각 면역을 주면 <b>합집합</b>이다.
        /// </summary>
        public DamageSourceSet BuildImmuneSources()
        {
            var set = DamageSourceSet.None;

            for (int i = 0; i < Behavioral.Count; i++)
            {
                var row = Behavioral[i];

                if (!string.Equals(row.ParamKey, "immune_sources", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var token in row.Value.Split(new[] { ',', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    switch (token.Trim().ToUpperInvariant())
                    {
                        case "DIRECT":
                            set = set.Union(DamageSourceSet.Of(DamageSource.Direct));
                            break;

                        case "DOT":
                            set = set.Union(DamageSourceSet.Of(DamageSource.Dot));
                            break;

                        case "ZONE":
                            set = set.Union(DamageSourceSet.Of(DamageSource.Zone));
                            break;

                        default:
                            throw new FormatException(
                                $"{row.BlessingId}.immune_sources 의 '{token}' 을 모릅니다. DIRECT·DOT·ZONE 중 하나여야 합니다.");
                    }
                }
            }

            return set;
        }

        /// <summary>행동 파라미터 하나를 찾는다. 없으면 <c>false</c>.</summary>
        public bool TryGetBehavioral(string blessingId, string paramKey, out BlessingEffectRow row)
        {
            for (int i = 0; i < Behavioral.Count; i++)
            {
                if (string.Equals(Behavioral[i].BlessingId, blessingId, StringComparison.Ordinal)
                    && string.Equals(Behavioral[i].ParamKey, paramKey, StringComparison.Ordinal))
                {
                    row = Behavioral[i];
                    return true;
                }
            }

            row = default;
            return false;
        }

        public override string ToString() =>
            $"스탯 {AppliedStatCount}건 · 조건부 {Conditional.Count}건 · 행동 {Behavioral.Count}건";
    }

    /// <summary>
    /// <b>축복 계수를 스탯 시트에 얹는 자리</b> (축복 시스템 기획 §1·§3).
    ///
    /// <para>
    /// 엑셀 <c>effects</c> 한 행이 여기서 세 갈래로 나뉜다.
    /// </para>
    ///
    /// <code>
    /// param_key 가 스탯이고 조건이 없다  →  스탯 시트에 바로 얹는다
    /// param_key 가 스탯이고 조건이 있다  →  Conditional 로 뺀다 (§3.3 — 캐시하면 안 된다)
    /// param_key 가 스탯이 아니다         →  Behavioral 로 뺀다 (트리거·무적·도트가 읽는다)
    /// </code>
    ///
    /// <para>
    /// <b>⚠️ 스탯 오타는 조용히 행동 파라미터가 된다.</b> <c>move-speeed</c> 라고 적으면 스탯이
    /// 아니므로 <see cref="BlessingBindResult.Behavioral"/> 로 빠지고 <b>아무 효과가 없다.</b>
    /// 그래서 데이터에는 규칙이 있다 — <b>스탯은 <c>kebab-case</c>, 행동 파라미터는
    /// <c>snake_case</c></b> 다. 그 규칙을 어긴 키는 던져서 드러낸다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class BlessingEffectBinder
    {
        private readonly StatRegistry registry;
        private readonly BlessingEffectCatalog catalog;

        public BlessingEffectBinder(StatRegistry registry, BlessingEffectCatalog catalog)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>
        /// 획득한 축복들의 계수를 시트에 얹는다.
        ///
        /// <para>
        /// <b>같은 계열의 여러 등급은 그냥 중첩된다</b>(§1.5) — 상속이 아니므로 별도 처리가 없다.
        /// <c>safecracker-t1</c> 과 <c>-t2</c> 는 서로 다른 축복이고 둘 다 얹힌다.
        /// </para>
        /// </summary>
        public BlessingBindResult Apply(StatSheet sheet, IEnumerable<string> blessingIds)
        {
            if (sheet == null)
            {
                throw new ArgumentNullException(nameof(sheet));
            }

            if (blessingIds == null)
            {
                throw new ArgumentNullException(nameof(blessingIds));
            }

            int applied = 0;
            var conditional = new List<BlessingEffectRow>();
            var behavioral = new List<BlessingEffectRow>();

            foreach (var blessingId in blessingIds)
            {
                var rows = catalog.For(blessingId);

                for (int i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];

                    if (!registry.Contains(row.ParamKey))
                    {
                        RequireBehavioralNaming(row);
                        behavioral.Add(row);
                        continue;
                    }

                    if (row.IsConditional)
                    {
                        // §3.3 — 조건부는 시트에 넣지 않는다. 넣으면 상시 켜진다.
                        conditional.Add(row);
                        continue;
                    }

                    if (!row.TryGetNumber(out float value))
                    {
                        // 스탯 기여인데 숫자가 아니다. 조용히 넘기면 그 축복만 효과가 없다.
                        throw new FormatException(
                            $"{row.BlessingId}.{row.ParamKey} 의 값 '{row.Value}' 가 숫자가 아닙니다. "
                            + "스탯 기여는 숫자여야 합니다.");
                    }

                    sheet.Add(new StatContribution(row.ParamKey, value, StackFor(row), row.BlessingId));
                    applied++;
                }
            }

            return new BlessingBindResult(applied, conditional, behavioral);
        }

        /// <summary>
        /// 이 기여를 어떤 방식으로 얹는가. <b>방식 칸이 비어 있으면 그 능력치의 계산 방식을 따른다.</b>
        /// </summary>
        /// <remarks>
        /// <para>
        /// 엑셀에는 방식(<c>stack</c>) 칸이 없어 모든 기여가 비어 있다. 비어 있으면 합연산이지만(기획 §1.3),
        /// <b>최댓값·최솟값 능력치</b>는 계산표가 그 방식의 기여만 본다(<see cref="StatSheet"/>) — 합연산으로 넣으면
        /// <b>조용히 무시된다.</b> 실제로 넓은 베기(<c>attack-arc</c> 120)가 공격 폭을 못 바꾸고 있었다.
        /// </para>
        /// <para>방식을 적어 둔 행(코드 대역 데이터)은 그대로 따른다.</para>
        /// </remarks>
        private StatStackMode StackFor(in BlessingEffectRow row)
        {
            if (row.Stack.Length == 0 && registry.TryGet(row.ParamKey, out StatDefinition definition))
            {
                if (definition.Kind == StatKind.Max) return StatStackMode.Max;
                if (definition.Kind == StatKind.Min) return StatStackMode.Min;
            }

            return row.ResolveStack();
        }

        /// <summary>
        /// 스탯이 아닌 키는 <c>snake_case</c> 여야 한다.
        ///
        /// <para>
        /// <b>이 검사가 없으면 스탯 이름 오타가 조용히 통과한다.</b> <c>move-speeed</c> 는
        /// 정의에 없으니 "행동 파라미터" 로 분류되어 아무 일도 하지 않고, 아무 신호도 남기지 않는다.
        /// 실제 데이터의 행동 파라미터 69종은 <b>전부</b> <c>snake_case</c> 이고 하이픈이 없다.
        /// </para>
        /// </summary>
        private void RequireBehavioralNaming(in BlessingEffectRow row)
        {
            if (row.ParamKey.IndexOf('-') < 0)
            {
                return;
            }

            throw new KeyNotFoundException(
                $"{row.BlessingId}.{row.ParamKey} — '-' 가 들어간 키는 스탯인데 stats 정의에 없습니다. "
                + "오타이거나 stats 시트에 빠진 항목입니다. "
                + $"행동 파라미터라면 '{row.ParamKey.Replace('-', '_')}' 처럼 '_' 를 씁니다.");
        }
    }
}

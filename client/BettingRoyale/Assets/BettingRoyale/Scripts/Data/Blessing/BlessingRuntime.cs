using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// <b>획득한 축복을 실제로 먹는 상태로 바꾸는 자리</b> (축복 시스템 기획 §3).
    ///
    /// <para>
    /// 지금까지 조각들이 따로 있었다 — 스탯 정의, 계수 표, 계산기, 피해 파이프라인.
    /// <b>이 클래스가 그것들을 한 사람분으로 조립한다.</b> 어댑터는 라운드 경계에서
    /// <see cref="Rebuild"/> 만 부르면 된다.
    /// </para>
    ///
    /// <code>
    /// 준비 단계가 끝났다
    ///   → 사람마다 Rebuild(playerId, economy.GetAcquired(playerId))
    ///   → 스탯 시트가 만들어져 BlessingCombatResolver 에 등록된다
    ///   → 전투에서 피해가 그 시트를 통과한다
    /// </code>
    ///
    /// <para>
    /// <b>매번 처음부터 다시 만든다.</b> 라운드가 시작될 때 일시 효과와 쿨타임은 풀리고
    /// 축복 자체만 남아야 하는데(S-GD-AN554W), <b>새로 만들면 그 규칙이 공짜로 지켜진다</b> —
    /// 무엇을 지워야 하는지 목록으로 관리하면 하나 빠뜨렸을 때 조용히 남는다.
    /// </para>
    ///
    /// <para>
    /// <b>조건부 기여와 행동 파라미터는 시트에 넣지 않고 따로 보관한다</b>(§3.3).
    /// 언더독·저격수는 대상과 거리에 따라 매번 달라지고, 트리거·무적·도트는 다른 시스템이 읽는다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class BlessingRuntime
    {
        private static readonly BlessingEffectRow[] NoRows = new BlessingEffectRow[0];

        private readonly StatRegistry registry;
        private readonly BlessingEffectBinder binder;
        private readonly BlessingCombatResolver resolver;

        private readonly Dictionary<int, BlessingBindResult> resultsByPlayer =
            new Dictionary<int, BlessingBindResult>();

        /// <summary>발행된 시트. 체력·이동·공격처럼 <b>피해 계산 밖에서</b> 소비하는 쪽이 이것으로 읽는다.</summary>
        private readonly Dictionary<int, StatSheet> sheetsByPlayer = new Dictionary<int, StatSheet>();

        /// <summary>재사용 버퍼. 사람마다 새 목록을 만들지 않는다.</summary>
        private readonly List<string> idBuffer = new List<string>(8);

        public BlessingRuntime(
            StatRegistry registry, BlessingEffectCatalog catalog, BlessingCombatResolver resolver)
        {
            this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            binder = new BlessingEffectBinder(registry, catalog);
        }

        /// <summary>지금 규칙이 만들어져 있는 사람 수. 진단용이다.</summary>
        public int TrackedPlayerCount => resultsByPlayer.Count;

        /// <summary>
        /// 이 사람의 축복 규칙을 <b>처음부터 다시</b> 만들어 전투에 등록한다.
        ///
        /// <para>
        /// 준비 단계가 끝날 때와 라운드가 시작될 때 부른다. 축복을 새로 얻었을 때 바로 불러도 된다 —
        /// 같은 입력이면 같은 결과라 여러 번 불러도 안전하다.
        /// </para>
        /// </summary>
        /// <param name="acquired">
        /// 이 사람이 획득한 축복. <c>MatchEconomyState.GetAcquired(playerId)</c> 를 그대로 넘긴다.
        /// <b>비어 있어도 부른다</b> — 지난 라운드의 기여가 남지 않게 하려면 빈 시트로 덮어야 한다.
        /// </param>
        /// <param name="compose">
        /// 축복을 얹은 <b>뒤</b>, 전투에 발행하기 <b>전</b>에 같은 시트에 다른 공급원을 합칠 자리다 —
        /// 강화(<c>UpgradeState.ApplyTo</c>)와 무기 기준값 주입(<c>StatSheet.SetBase</c>)이 여기로 온다.
        /// <b>여기서 던지면 시트를 발행하지 않는다</b> — 반쪽 시트가 전투로 나가는 대신 이전 결과가 그대로 남는다.
        /// </param>
        public BlessingBindResult Rebuild(
            int playerId, IReadOnlyList<BlessingDefinition> acquired, Action<StatSheet> compose = null)
        {
            idBuffer.Clear();

            if (acquired != null)
            {
                for (int i = 0; i < acquired.Count; i++)
                {
                    var definition = acquired[i];

                    if (definition != null)
                    {
                        idBuffer.Add(definition.Id);
                    }
                }
            }

            var sheet = new StatSheet(registry);
            var result = binder.Apply(sheet, idBuffer);

            // 발행 전에 부른다. 던지면 아래 등록을 하지 않아 지난 라운드의 완성된 시트가 그대로 살아 있다.
            compose?.Invoke(sheet);

            resolver.SetSheet(playerId, sheet, result.BuildImmuneSources());
            resolver.SetBlessings(playerId, acquired);
            resultsByPlayer[playerId] = result;
            sheetsByPlayer[playerId] = sheet;

            return result;
        }

        /// <summary>
        /// 이 사람의 <b>조건부 기여</b> — 대상·거리에 따라 켜지는 것들 (§3.3).
        /// 피해를 계산할 때마다 조건을 평가해서 합쳐야 한다. <b><c>null</c> 이 아니다.</b>
        /// </summary>
        public IReadOnlyList<BlessingEffectRow> ConditionalOf(int playerId) =>
            resultsByPlayer.TryGetValue(playerId, out var result) ? result.Conditional : NoRows;

        /// <summary>
        /// 이 사람의 <b>행동 파라미터</b> — 트리거·무적·도트·상자·경제가 읽는다.
        /// <b><c>null</c> 이 아니다.</b>
        /// </summary>
        public IReadOnlyList<BlessingEffectRow> BehavioralOf(int playerId) =>
            resultsByPlayer.TryGetValue(playerId, out var result) ? result.Behavioral : NoRows;

        /// <summary>이 사람의 규칙이 만들어져 있는가.</summary>
        public bool Has(int playerId) => resultsByPlayer.ContainsKey(playerId);

        /// <summary>
        /// 마지막으로 <b>발행된</b> 시트. 최대 체력·이동 속도·공격 속도처럼 피해 계산이 이름으로 읽지 않는
        /// 값을 소비하는 쪽이 쓴다. 합성 중 던진 <see cref="Rebuild"/> 의 반쪽 시트는 여기 들어오지 않는다.
        /// </summary>
        public bool TryGetSheet(int playerId, out StatSheet sheet) =>
            sheetsByPlayer.TryGetValue(playerId, out sheet);

        /// <summary>
        /// 새 매치를 시작한다 (S-GD-RFXF8X). <b>지난 매치의 축복이 새지 않게 전부 비운다.</b>
        /// </summary>
        public void Clear()
        {
            resultsByPlayer.Clear();
            sheetsByPlayer.Clear();
            resolver.Clear();
        }

        public override string ToString() => $"축복 런타임 · {resultsByPlayer.Count}명";
    }
}

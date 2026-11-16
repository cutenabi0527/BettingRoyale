using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 스탯 정의 전체 (축복 시스템 기획 §1.1). 엑셀 <c>stats</c> 시트가 그대로 들어온다.
    ///
    /// <para>
    /// <b>문자열 키 사전이다. enum 이 아니다</b>(§0). 스탯을 추가할 때 컴파일이 필요하면
    /// "데이터만 고쳐서 추가" 가 성립하지 않는다 — 지금 21개이고 계속 는다.
    /// </para>
    ///
    /// <para>
    /// <b>여기에 수치가 없다.</b> 생성자가 정의를 받을 뿐이고, 값은 데이터 파이프라인이 준다(D-041).
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class StatRegistry
    {
        private readonly Dictionary<string, StatDefinition> byId;

        public StatRegistry(IEnumerable<StatDefinition> definitions)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            byId = new Dictionary<string, StatDefinition>(StringComparer.Ordinal);

            foreach (var definition in definitions)
            {
                if (definition == null)
                {
                    throw new ArgumentException("스탯 정의에 null 이 있습니다.", nameof(definitions));
                }

                if (byId.ContainsKey(definition.Id))
                {
                    // 같은 id 가 두 벌이면 어느 쪽이 이기는지가 로드 순서에 달린다.
                    // 데이터 오류이므로 만들 때 터뜨린다 — 나중에 값이 이상할 때는 원인을 못 찾는다.
                    throw new ArgumentException(
                        $"스탯 id 가 중복됩니다: {definition.Id}", nameof(definitions));
                }

                byId[definition.Id] = definition;
            }
        }

        public int Count => byId.Count;

        public IReadOnlyCollection<string> Ids => byId.Keys;

        public bool Contains(string statId) => statId != null && byId.ContainsKey(statId);

        public bool TryGet(string statId, out StatDefinition definition)
        {
            if (statId == null)
            {
                definition = null;
                return false;
            }

            return byId.TryGetValue(statId, out definition);
        }

        /// <summary>
        /// 정의를 가져온다. <b>없으면 던진다</b> — 축복 계수가 존재하지 않는 스탯을 가리키는 것은
        /// 데이터 오류이고, 조용히 0 으로 넘기면 <b>그 축복만 아무 효과가 없다.</b>
        /// 아무 신호가 없어 "약한가 보다" 로 넘어간다.
        /// </summary>
        public StatDefinition Get(string statId)
        {
            if (!TryGet(statId, out var definition))
            {
                throw new KeyNotFoundException(
                    $"정의되지 않은 스탯입니다: '{statId}'. stats 시트에 없는 키를 축복 계수가 가리키고 있습니다.");
            }

            return definition;
        }

        public override string ToString() => $"스탯 {byId.Count}종";
    }
}

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 피해가 <b>어디서 왔는가</b> (축복 시스템 기획 §2.2).
    ///
    /// <para>
    /// <b>이 분류 하나가 규칙 넷을 대신한다</b> — 피격 트리거가 도는가, 경감을 우회하는가,
    /// 무적이 막는가, 도트 스택이 갱신되는가.
    /// </para>
    ///
    /// <b>⚠️ 값은 뒤에만 붙인다.</b> 회선을 넘을 수 있다.
    /// </summary>
    public enum DamageSource
    {
        /// <summary>검·활 같은 직접 공격. <b>피격 트리거가 도는 유일한 소스다.</b></summary>
        Direct = 0,

        /// <summary>독화살 같은 지속 피해. 경감을 우회하고 피격 트리거를 돌리지 않는다.</summary>
        Dot = 1,

        /// <summary>자기장. 경감을 우회하고 피격 트리거를 돌리지 않는다.</summary>
        Zone = 2,

        // 축복 충격파·표식 폭발. 방어는 적용하고 적중 연쇄는 만들지 않는다.
        Blessing = 3,
    }

    /// <summary>
    /// 면역인 소스들의 집합 (기획 §2.5 <c>immune_sources</c>).
    ///
    /// <para>
    /// <b>합집합으로 쌓인다</b> — 여러 축복이 각각 다른 소스를 면역시키면 전부 면역이다.
    /// 지금 쓰는 곳은 <c>nine-lives-t3</c>(9개의 목숨) 하나이고 <see cref="DamageSource.Dot"/> 를 막는다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public readonly struct DamageSourceSet
    {
        private readonly int mask;

        private DamageSourceSet(int mask) => this.mask = mask;

        /// <summary>아무것도 면역이 아니다. <b>기본값</b>이다.</summary>
        public static DamageSourceSet None => new DamageSourceSet(0);

        public static DamageSourceSet Of(params DamageSource[] sources)
        {
            int mask = 0;

            if (sources != null)
            {
                for (int i = 0; i < sources.Length; i++)
                {
                    mask |= 1 << (int)sources[i];
                }
            }

            return new DamageSourceSet(mask);
        }

        public bool Contains(DamageSource source) => (mask & (1 << (int)source)) != 0;

        /// <summary>합집합. 축복 여러 개가 각각 면역을 주면 이렇게 합친다.</summary>
        public DamageSourceSet Union(DamageSourceSet other) => new DamageSourceSet(mask | other.mask);

        public bool IsEmpty => mask == 0;

        public override string ToString()
        {
            if (mask == 0)
            {
                return "면역 없음";
            }

            var parts = new System.Collections.Generic.List<string>(3);

            if (Contains(DamageSource.Direct)) { parts.Add(nameof(DamageSource.Direct)); }
            if (Contains(DamageSource.Dot)) { parts.Add(nameof(DamageSource.Dot)); }
            if (Contains(DamageSource.Zone)) { parts.Add(nameof(DamageSource.Zone)); }

            return "면역 " + string.Join("·", parts);
        }
    }
}

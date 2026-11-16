namespace BettingRoyale.Data.Upgrade
{
    /// <summary>
    /// 강화가 쓰는 스탯 키 중 <b>피해 계산이 이름으로 참조하지 않는 것들</b> (S-GD-A2YDDJ).
    ///
    /// <para>
    /// 강화는 축복과 <b>같은 스탯 시트·같은 id</b> 를 쓴다. 그래서 공격력·방어력·방어력 관통·
    /// 피해 증가·받는 피해는 <c>BettingRoyale.Data.Blessing.DamageStatKeys</c> 에 이미 있고
    /// <b>여기서 다시 정의하지 않는다</b> — 두 곳에 적으면 한쪽만 고쳐서 조용히 갈린다.
    /// </para>
    ///
    /// <para>
    /// 남은 셋은 피해 <b>계산식</b>이 이름으로 읽지 않아 그쪽에 없다. 최대 체력은 체력 컴포넌트가,
    /// 이동·공격 속도는 이동·공격 컴포넌트가 소비한다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public static class UpgradeStatKeys
    {
        /// <summary>
        /// 최대 체력. <b>기여는 증가분이고 기준선은 밖에서 준다</b> —
        /// 전투원의 직렬화 기본값이 기준선이므로 시트의 기본값으로 박을 수 없다
        /// (<c>StatSheet.SetBase</c>, S-CLI-VJ8HKS).
        /// </summary>
        public const string MaxHp = "max-hp";

        /// <summary>이동 속도 배율. 세트 효과가 여기에 붙는다.</summary>
        public const string MoveSpeed = "move-speed";

        /// <summary>공격 속도 배율.</summary>
        public const string AttackSpeed = "attack-speed";
    }
}

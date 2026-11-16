namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 전투 참여자 식별자의 Fusion 비의존 주입 경계. 네트워크 스폰 어댑터는 Gameplay 구현 타입을
    /// 직접 참조하지 않고 입력 권위자의 플레이어 id만 이 계약으로 전달한다.
    /// </summary>
    public interface ICombatantIdentity
    {
        CombatantId Id { get; }
        void SetId(CombatantId id);
    }
}

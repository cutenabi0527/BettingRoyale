namespace BettingRoyale.Data.Combat
{
    /// <summary>진단이 읽는 현재 수직 속도(m/s). 접지 판정이 살아 있는지를 밖에서 보기 위한 창이다.</summary>
    /// <remarks>
    /// ⚠️ 게임 규칙에 쓰지 않는다. 접지 중이면 중력 한 틱분에서 멈춰 있어야 하고, 값이 계속 커지면
    /// 바닥을 못 잡고 있다는 뜻이다 — 그 상태의 <c>CharacterController</c> 는 지형에 파고들며
    /// 수평으로 밀려나므로, 이동 입력이 0인데도 평면 변위가 생긴다.
    /// </remarks>
    public interface ICombatMotionVerticalSource
    {
        float VerticalVelocity { get; }
    }
}

namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 하나의 공용 상호작용 입력(E / gamepad west)을 소비하는 경계.
    /// true는 상자가 가까워 그 요청을 소비했다는 뜻이며, 성공·거부와 무관하게 장비 획득으로
    /// 같은 프레임에 fall-through 하지 않는다. false일 때만 기존 장비 획득 경로를 이어 간다.
    /// </summary>
    public interface ICombatInteractionCommandSink
    {
        bool TryHandleInteraction();
    }
}

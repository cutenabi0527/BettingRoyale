using BettingRoyale.Data.Throwing;

namespace BettingRoyale.Data.Equipment
{
    /// <summary>
    /// 장비 인벤토리가 소유한 투척 사용 게이트의 좁은 권위 경계.
    /// 소비 수량과 공유 쿨다운은 이 경계를 통과한 경우에만 함께 갱신한다.
    /// </summary>
    public interface IEquipmentThrowUseDriver
    {
        bool BeginAuthorityThrowRound(int ownerId, int roundNumber, double startedAtSeconds);
        ThrowUseResult TryAuthorityUseThrowable(in ThrowUseRequest request, in ThrowRoundContext context);
        bool TryAuthorityDropFocusedThrowable(out ThrowingItemDefinition definition);
    }
}

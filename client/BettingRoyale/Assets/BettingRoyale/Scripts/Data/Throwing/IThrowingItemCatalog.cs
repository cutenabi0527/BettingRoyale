namespace BettingRoyale.Data.Throwing
{
    /// <summary>ItemId 를 투척 규칙에 필요한 불변 정의로 해석하는 Unity·Fusion 비의존 경계.</summary>
    public interface IThrowingItemCatalog
    {
        /// <summary>해당 항목이 투척 소비품이면 true 와 정의를 돌려준다.</summary>
        bool TryGetThrowing(int itemId, out ThrowingItemDefinition definition);
    }
}

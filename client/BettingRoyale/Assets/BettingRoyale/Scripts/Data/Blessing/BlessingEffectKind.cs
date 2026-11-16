namespace BettingRoyale.Data.Blessing
{
    /// <summary>효과 인스턴스의 표시·수명 분류. 수치 계산은 이 열거형의 책임이 아니다.</summary>
    public enum BlessingEffectKind
    {
        Passive = 0,
        Duration = 1,
        Stack = 2,
        Condition = 3,
        Empower = 4,
    }

    public static class BlessingEffectKindExtensions
    {
        /// <summary>상시 패시브만 HUD 인스턴스를 만들지 않는다.</summary>
        public static bool CreatesInstance(this BlessingEffectKind kind) => kind != BlessingEffectKind.Passive;
    }
}

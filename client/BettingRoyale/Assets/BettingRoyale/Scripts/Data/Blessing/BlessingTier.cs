namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 축복 등급 (S-GD-EQCVWS). TFT·칼바람 아수라장의 '증강' 에서 따온 체계이고 이름은 LoL 티어를 쓴다.
    ///
    /// <b>값은 순서다.</b> 숫자가 클수록 높은 등급이다 — 비교(<c>&gt;</c>, <c>&lt;</c>)가 그대로 의미를 갖는다.
    ///
    /// <b>0 을 쓰지 않는다.</b> <c>default</c> 가 유효한 등급으로 보이면 "등급을 안 넣었다" 를 놓친다.
    /// <see cref="BlessingDefinition"/> 생성자가 범위를 검사한다.
    ///
    /// ⚠️ <b>에메랄드는 아직 없다.</b> 사용자가 추가를 제안할 생각이라고만 밝혔다(2026-08-27, S-GD-EQCVWS).
    /// LoL 티어 순서상 플래티넘 위에 들어가므로 <c>Emerald = 4</c> 로 붙이면 된다 — 그때까지 만들지 않는다.
    /// </summary>
    public enum BlessingTier
    {
        Silver = 1,
        Gold = 2,
        Platinum = 3,
    }
}

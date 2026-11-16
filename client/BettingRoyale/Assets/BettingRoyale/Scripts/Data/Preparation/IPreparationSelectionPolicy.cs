namespace BettingRoyale.Data.Preparation
{
    /// <summary>
    /// <b>이번 준비 단계의 축복 선택을 확정했는가</b> (S-GD-DP2DVC — *"선택 중 이동과 다른
    /// 상호작용이 막힌다"*).
    ///
    /// <para>
    /// <see cref="IPreparationAccessPolicy"/> 와 같은 자리의 경계다. 저쪽은 <b>어디에 서 있는가</b>
    /// 를, 이쪽은 <b>지금 이 사람이 준비 시설을 쓸 자격이 되는가</b> 를 답한다. 둘을 하나로 합치지
    /// 않는 이유는 <b>거절 사유가 다르기 때문</b>이다 — 구역 밖은 <c>NotInRange</c>, 선택 전은
    /// <c>SelectionRequired</c> 이고, 화면은 그 둘에 다른 안내를 띄운다.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 이 판정이 어댑터에만 있으면 안 된다.</b> 실제로 그랬고, 그래서 같은 게이트를 쓰는
    /// 훈련 경로에는 규칙이 없었다. 규칙은 게이트가 직접 봐야 <b>어느 경로로 들어와도</b> 같다
    /// (S-GD-DP2DVC).
    ///
    /// <b>⚠️ 어댑터의 사본은 아직 남아 있다</b> — <c>OnlineEconomyCoordinator</c> 가 같은 판정을
    /// 한 번 더 한다. 답이 같아 지금은 중복일 뿐이고, 제거는 그 파일을 소유한 작업에서 한다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public interface IPreparationSelectionPolicy
    {
        /// <summary>
        /// 이 사람이 이번 준비 단계의 축복을 확정했는가.
        ///
        /// <b>준비 단계가 아닐 때 무엇을 돌려주든 상관없다</b> — 부르는 쪽이 단계를 먼저 본다.
        /// </summary>
        bool IsSelectionConfirmed(int playerId);
    }
}

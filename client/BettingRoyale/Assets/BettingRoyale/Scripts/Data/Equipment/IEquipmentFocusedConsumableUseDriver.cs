namespace BettingRoyale.Data.Equipment
{
    /// <summary>
    /// 지금 고른 소모품 칸의 <b>투척이 아닌</b> 소모품(물약·부적)을 공격 입력으로 쓴다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 입력 계약은 S-GD-YDBKKW §2 그대로다 — 숫자 2·3·4(또는 LB/RB)로 칸을 <b>고르고</b>,
    /// 마우스 좌클릭·RT 로 <b>쓴다</b>. 그 좌클릭이 지금까지는 투척류에만 연결돼 있어서
    /// 물약을 사도 마실 방법이 없었다.
    /// </para>
    /// <para>
    /// 투척류는 이 경로가 아니라 투척 경로(<c>ThrowUseGate</c>)가 맡는다. 같은 누름을 두 경로가
    /// 모두 받으면 한 번 눌러 두 개가 사라지므로, 구현은 투척류를 거절해야 한다.
    /// </para>
    /// </remarks>
    public interface IEquipmentFocusedConsumableUseDriver
    {
        /// <param name="consumeQuantity">
        /// false 면 효과만 내고 수량은 그대로 둔다 — 준비 단계 규칙(S-GD-GQW2W8)이다.
        /// 단계를 아는 쪽(실행 경로)이 정해서 넘긴다.
        /// </param>
        EquipmentInventoryOperationResult TryAuthorityUseFocusedConsumable(bool consumeQuantity);

        /// <summary>라운드 경계다. 남은 소모품 버프를 지운다 (S-GD-AN554W).</summary>
        void ResetConsumableBuffsForNextRound();

        /// <summary>
        /// 전투 라운드 종료를 Host 가 확정한 경계에서 소모품 3칸과 그 잔존 효과를 비운다 (S-GD-3DEQ2M).
        /// 무기·장신구는 남긴다. 비운 것이 있으면 <c>true</c>. 라운드 초기화(<see cref="ResetConsumableBuffsForNextRound"/>)와
        /// 다른 경계다 — 그쪽은 버프만 지우고 수량은 남긴다.
        /// </summary>
        bool ClearConsumablesForRoundEnd();
    }
}

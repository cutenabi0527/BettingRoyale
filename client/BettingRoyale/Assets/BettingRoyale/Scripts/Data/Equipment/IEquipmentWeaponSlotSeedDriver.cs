namespace BettingRoyale.Data.Equipment
{
    /// <summary>
    /// <b>들고 있는데 인벤토리가 모르는 무기</b>를 무기 슬롯에 적어 맞추는 경계.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 필요한가.</b> 제품 온라인 매치에서 무기는 <c>Net_Combatant</c> 프리팹의
    /// <c>equippedWeapon</c> 으로 들어오고 <b>인벤토리 무기 슬롯에는 기록되지 않는다</b> —
    /// 훈련장 경로만 <c>TryAuthorityEquipWeapon</c> 을 거친다. 그래서 인벤토리가 보기에는
    /// 플레이어가 맨손이고, 그 오해가 두 군데에서 드러났다:
    /// </para>
    /// <list type="number">
    /// <item><b>물건을 사면 무기가 벗겨졌다</b> — 2026-09-15 Play. 지금은 막혀 있다.</item>
    /// <item><b>HUD 1번 칸이 비어 보였다</b> — 물건을 살 때까지 무기가 안 보였다
    /// (2026-09-15 사용자 보고).</item>
    /// </list>
    /// <para>
    /// ⚠️ <b>이것은 임시 수습이다.</b> 제대로 된 해결은 제품 스폰 경로가 훈련장처럼
    /// <c>TryAuthorityEquipWeapon</c> 으로 무기를 주는 것이다. 그 변경은 온라인 스폰·무기 선택
    /// (S-GD-GW8PAX)에 닿으므로 별도로 다룬다.
    /// </para>
    /// <para>
    /// <b>권위 피어에서만 부른다.</b> 원격 피어는 복제 스냅샷을 그대로 받아야 하고, 거기서
    /// 스스로 슬롯을 채우면 다음 복제가 덮어써 값이 두 번 튄다.
    /// </para>
    /// <para>
    /// <b>좁은 인터페이스로 두는 이유</b>: <c>IEquipmentInventoryDriver</c> 에 넣으면 대역을 포함한
    /// 모든 구현체가 이 메서드를 갖게 된다. <see cref="IEquipmentFocusDriver"/> 와 같은 방식이다.
    /// </para>
    /// </remarks>
    public interface IEquipmentWeaponSlotSeedDriver
    {
        /// <summary>
        /// 무기 슬롯이 비어 있고 실제로 무기를 들고 있으면 슬롯을 채운다.
        /// <b>이미 맞으면 아무것도 하지 않는다</b> — 매 틱 불러도 안전하다.
        /// </summary>
        void EnsureWeaponSlotSeeded();
    }
}

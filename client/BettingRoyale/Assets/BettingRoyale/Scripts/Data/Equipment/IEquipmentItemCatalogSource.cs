namespace BettingRoyale.Data.Equipment
{
    /// <summary>
    /// 이 인벤토리가 <b>실제로 지급 판정에 쓰는</b> 카탈로그를 밖에서 볼 수 있게 여는 경계.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 필요한가.</b> 상점 게이트(<c>ShopRequestGate</c>)와 인벤토리
    /// (<c>EquipmentInventory.TryPickup</c>)는 <b>둘 다</b> "이 itemId 가 존재하는가" 를 묻는데,
    /// 서로 다른 <see cref="IEquipmentItemCatalog"/> 를 받으면 두 답이 갈린다. 갈리면
    /// <b>상점은 팔 수 있다고 진열하고 지급만 실패한다</b> — 사거리·골드 검사를 전부 통과한 뒤
    /// 마지막에 <c>UnknownItem</c> 이 나오므로 원인이 데이터라는 것이 드러나지 않는다.
    /// 2026-09-10 실측에서 실제로 이 모양으로 나타났다.
    /// </para>
    /// <para>
    /// 그래서 게이트는 카탈로그를 따로 받지 않고 <b>인벤토리가 쓰는 그것</b>을 받는다.
    /// 이 인터페이스가 그 하나뿐인 출처다.
    /// </para>
    /// <para>
    /// ⚠️ <b>구현하지 않아도 된다.</b> 카탈로그를 노출할 수 없는 구현
    /// (테스트 대역 등)이 있을 수 있으므로 <see cref="IEquipmentInventoryDriver"/> 에 넣지 않고
    /// 선택 인터페이스로 둔다. 찾지 못하면 부르는 쪽이 "팔 수 있는 것이 없다" 로 처리한다.
    /// </para>
    /// </remarks>
    public interface IEquipmentItemCatalogSource
    {
        /// <summary>지급 판정에 쓰이는 카탈로그. 아직 연결되지 않았으면 <c>null</c> 이다.</summary>
        IEquipmentItemCatalog ItemCatalog { get; }
    }
}

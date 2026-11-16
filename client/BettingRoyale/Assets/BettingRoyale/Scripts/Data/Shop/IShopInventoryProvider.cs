using BettingRoyale.Data.Equipment;

namespace BettingRoyale.Data.Shop
{
    /// <summary>
    /// 플레이어 번호로 그 사람의 장비 인벤토리를 찾아주는 경계 (S-CLI-GRZGBK).
    ///
    /// <para>
    /// <b>왜 이 인터페이스가 필요한가</b> — 경제(<c>MatchEconomyState</c>)는 플레이어를 <c>PlayerId</c> 로
    /// 알고, 인벤토리는 전투자 컴포넌트에 붙어 있다. 둘은 서로를 모르고, <b>모르는 편이 맞다</b>.
    /// 상점은 그 둘을 동시에 만져야 하는 유일한 자리라 여기서만 잇는다.
    /// </para>
    ///
    /// <para>
    /// <b>구현은 <c>Scripts/Data/</c> 밖에 둔다.</b> 어떤 전투자가 어떤 플레이어인지는 세션이 아는 것이고
    /// 도메인은 세션을 모른다 (D-060). 이 인터페이스는 그 사실을 타입으로 굳힌 것이다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <c>CombatantId</c> 가 아니라 <c>PlayerId</c> 를 받는다. 상자 쪽은 반대로
    /// <c>IChestPlayerIdMapper</c> 로 <c>CombatantId</c> → <c>PlayerId</c> 를 옮긴다 —
    /// 상자는 입력이 전투에서 오고 상점은 화면에서 오기 때문이다. <b>둘을 같은 값이라고 가정하지 않는다.</b>
    /// </para>
    /// </summary>
    public interface IShopInventoryProvider
    {
        /// <summary>
        /// 이 플레이어의 인벤토리를 찾는다.
        ///
        /// <b>못 찾는 것은 정상이다</b> — 전투자가 아직 스폰되지 않았거나 이미 사라진 경우다.
        /// 그때는 예외를 던지지 않고 <c>false</c> 를 돌려준다. 준비 단계 진입 직후 한 프레임에서
        /// 실제로 일어나며, 그 한 번 때문에 Host 가 터지면 방 전체가 멈춘다.
        ///
        /// <para>
        /// ⚠️ <b>돌려주는 인벤토리는 지급 도중에 경제(지갑·정산)를 건드리면 안 된다.</b>
        /// <c>TryAuthorityPickup</c> 안에서 동기 콜백으로 골드가 줄면 <c>ShopRequestGate</c> 가
        /// <b>"물건은 줬는데 돈을 못 받은"</b> 상태가 되어 예외를 던진다. 지급은 인벤토리만 바꾼다.
        /// </para>
        /// </summary>
        bool TryResolve(int playerId, out IEquipmentInventoryDriver inventory);
    }
}

using BettingRoyale.Data.Common;

namespace BettingRoyale.Data.Preparation
{
    /// <summary>
    /// 플레이어가 지금 어디 서 있는지 알려주는 경계 (S-CLI-9QYFCT).
    ///
    /// <para>
    /// <b>Host 가 채워야 한다.</b> 좌표를 클라이언트가 보내는 값에서 가져오면, 보내는 쪽이
    /// "나 상점 앞에 있어" 라고 적어 <b>맵 반대편에서 물건을 살 수 있다.</b> 아이템 줍기가
    /// 같은 이유로 Host 에서 거리를 다시 잰다(<c>NetworkEquipmentPickupInteractor</c>).
    /// </para>
    ///
    /// <para>
    /// <b>구현은 <c>Scripts/Data/</c> 밖에 둔다.</b> 어떤 전투자가 어떤 플레이어인지도, 그 전투자의
    /// <c>Transform</c> 도 도메인은 모른다 (D-060). 이 인터페이스가 그 경계다.
    /// </para>
    /// </summary>
    public interface IPreparationPresenceProvider
    {
        /// <summary>
        /// 이 플레이어의 현재 위치.
        ///
        /// <b>못 찾는 것은 정상이다</b> — 전투자가 아직 스폰되지 않았거나 이미 사라진 경우다.
        /// 그때는 <c>false</c> 를 돌려준다. 예외를 던지면 그 한 프레임 때문에 방 전체가 멈춘다.
        /// </summary>
        bool TryGetPosition(int playerId, out WorldPosition position);
    }
}

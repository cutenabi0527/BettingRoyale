using BettingRoyale.Core;
using BettingRoyale.Data.Preparation;

namespace BettingRoyale.Data.Contracts
{
    /// <summary>
    /// 이 사람이 지금 쓸 수 있는 준비맵 시설이 바뀌었다 (S-GD-66GTWA, S-CLI-9QYFCT).
    ///
    /// <para>
    /// <b>화면이 이 이벤트로 두 가지를 한다.</b>
    /// </para>
    ///
    /// <list type="number">
    /// <item><see cref="Facility"/> 가 <c>None</c> 이 아니면 "E 를 눌러 여세요" 안내를 띄운다</item>
    /// <item><b><c>None</c> 이 오면 열려 있던 창을 닫는다</b> — 구역 밖으로 걸어 나간 것이다</item>
    /// </list>
    ///
    /// <para>
    /// <b>진열 목록(<c>ShopOffersChanged</c>)과 따로 두는 이유</b>: 가까워졌다·멀어졌다는 걸어다니면
    /// 계속 바뀐다. 목록에 끼워 넣으면 <b>한 걸음 옮길 때마다 상품 목록 전체가 다시 날아간다.</b>
    /// 아이템 줍기 안내(<c>EquipmentPickupPromptChanged</c>)도 같은 이유로 따로 있다.
    /// </para>
    ///
    /// <para>
    /// <b>바뀔 때만 보낸다.</b> 매 프레임 보내면 방 인원만큼 곱해져 회선을 채운다 —
    /// 보내는 쪽이 직전 값과 같으면 건너뛴다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>이 이벤트는 안내일 뿐 허가가 아니다.</b> 실제 구매·강화·베팅은 Host 가
    /// <c>PreparationAccessGate</c> 로 다시 판정한다. 화면이 받은 값을 믿고 요청을 보내도,
    /// 그사이 걸어 나갔으면 Host 가 거절한다.
    /// </para>
    /// </summary>
    public readonly struct PreparationFacilityInRangeChanged : IGameEvent
    {
        /// <summary><c>Scripts/Network/</c> 의 <c>PlayerSlot.PlayerId</c> 와 같은 값.</summary>
        public readonly int PlayerId;

        /// <summary>지금 화면을 보는 사람의 것인가. 정본 설명은 <see cref="GoldChanged.IsMine"/> 에 있다.</summary>
        public readonly bool IsMine;

        /// <summary>쓸 수 있는 시설. <c>None</c> 이면 어느 구역에도 없다.</summary>
        public readonly PreparationFacility Facility;

        public PreparationFacilityInRangeChanged(int playerId, bool isMine, PreparationFacility facility)
        {
            PlayerId = playerId;
            IsMine = isMine;
            Facility = facility;
        }
    }
}

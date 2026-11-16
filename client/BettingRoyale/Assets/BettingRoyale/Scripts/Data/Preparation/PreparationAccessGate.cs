using System;
using BettingRoyale.Data.Contracts;

namespace BettingRoyale.Data.Preparation
{
    /// <summary>
    /// "지금 이 사람이 어느 시설을 쓸 수 있는가" 를 답하는 유일한 자리 (S-CLI-9QYFCT).
    ///
    /// <para>
    /// 상점·강화·룰렛이 <b>같은 판정을 공유한다.</b> 각자 거리를 재게 두면 셋의 규칙이 갈리고,
    /// 갈린 것은 "상점은 되는데 강화는 안 되는" 식으로만 드러나 원인을 찾기 어렵다.
    /// </para>
    ///
    /// <para>
    /// <b>화면에 띄우는 안내와 실제 판정이 같은 함수를 쓴다.</b> 아이템 줍기는 화면 쪽 거리 계산과
    /// Host 쪽 거리 계산이 각각 있는데(<c>NetworkEquipmentPickupInteractor</c>), 그건 표시를 매 프레임
    /// 갱신해야 해서 그렇다. 판정 자체는 <b>Host 가 이 게이트로 다시 한다</b> — 안 그러면
    /// 보내는 쪽이 좌표를 지어내 맵 반대편에서 살 수 있다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class PreparationAccessGate : IPreparationAccessPolicy
    {
        private readonly PreparationFacilityMap facilities;
        private readonly IPreparationPresenceProvider presence;

        public PreparationAccessGate(
            PreparationFacilityMap facilities, IPreparationPresenceProvider presence)
        {
            this.facilities = facilities ?? throw new ArgumentNullException(nameof(facilities));
            this.presence = presence ?? throw new ArgumentNullException(nameof(presence));
        }

        /// <summary>
        /// 지금 이 사람이 쓸 수 있는 시설. 위치를 모르거나 어느 구역에도 없으면
        /// <see cref="PreparationFacility.None"/>.
        /// </summary>
        public PreparationFacility Resolve(int playerId) =>
            presence.TryGetPosition(playerId, out var position)
                ? facilities.Resolve(position)
                : PreparationFacility.None;

        /// <summary>이 사람이 지금 그 시설을 쓸 수 있는가.</summary>
        public bool CanUse(int playerId, PreparationFacility facility) =>
            facility != PreparationFacility.None && Resolve(playerId) == facility;

        /// <summary>
        /// 화면에 보낼 안내 상태. <b>구역을 벗어나면 <see cref="PreparationFacility.None"/> 이 실려
        /// 열려 있던 창을 닫는 신호가 된다</b> (조영운 확정, 2026-09-08).
        /// </summary>
        public PreparationFacilityInRangeChanged BuildInRange(int playerId, bool isMine) =>
            new PreparationFacilityInRangeChanged(playerId, isMine, Resolve(playerId));
    }
}

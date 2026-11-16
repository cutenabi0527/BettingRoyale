using System;
using BettingRoyale.Data.Common;

namespace BettingRoyale.Data.Online
{
    /// <summary>
    /// 준비맵의 집결 구역 하나 — 중심과 반경 (S-GD-6VWPVE · S-CLI-62DM8M).
    ///
    /// <para>
    /// <b>시설 구역과 같은 모양을 일부러 따랐다</b>(<c>PreparationFacilityArea</c>). 바닥에 그린 원이고
    /// 높이를 보지 않으며 경계는 포함이다. 두 판정이 다른 모양이면 <i>"상점은 들어가지는데 집결은
    /// 안 되는"</i> 식의 신고가 생기고, 그때 원인이 규칙인지 좌표인지 구분되지 않는다.
    /// </para>
    ///
    /// <para>
    /// 좌표계는 <b>준비맵 로컬</b>이다. 전투원의 월드 좌표를 준비맵 원점 기준으로 바꿔 주는 것은
    /// <c>NetworkPreparationPresenceProvider</c> 의 몫이며, 시설 구역표가 이미 같은 공간을 쓴다 —
    /// 여기서 월드를 쓰면 콜로세움을 옮기는 순간 집결 구역만 따라오지 않는다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public readonly struct PreparationRallyArea
    {
        /// <summary>구역 중심. <b>Y 는 쓰지 않는다</b> — 바닥 평면의 원이다.</summary>
        public readonly WorldPosition Center;

        public readonly float Radius;

        public PreparationRallyArea(WorldPosition center, float radius)
        {
            if (!(radius > 0f))
            {
                // NaN 도 여기서 걸린다 — NaN 은 어떤 비교에도 false 라 조용히 "아무도 못 들어가는 구역" 이 된다.
                throw new ArgumentOutOfRangeException(
                    nameof(radius), radius, "집결 구역 반경은 0 보다 커야 합니다.");
            }

            Center = center;
            Radius = radius;
        }

        /// <summary>
        /// 저작 앵커를 읽지 못했을 때 쓰는 폴백 구역.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>폴백이 판정을 열어 두지 않게</b> 실제 구역과 같은 크기로 만든다. "못 읽었으니 전부
        /// 통과" 로 두면 스폰 직후 전원이 집결한 것으로 보여 준비 단계가 0 초 만에 끝난다.
        /// </remarks>
        public static PreparationRallyArea Default => new(
            new WorldPosition(
                PreparationRallyDefaults.FallbackCenterX,
                0f,
                PreparationRallyDefaults.FallbackCenterZ),
            PreparationRallyDefaults.Radius);

        /// <summary>이 좌표가 집결 구역 안인가. 바닥 평면(XZ)에서만 잰다.</summary>
        public bool Contains(in WorldPosition position)
        {
            float dx = position.X - Center.X;
            float dz = position.Z - Center.Z;
            return (dx * dx) + (dz * dz) <= Radius * Radius;
        }

        public override string ToString() => $"Rally @{Center} r{Radius:F1}";
    }

    /// <summary>
    /// 집결 구역의 ⚠️ <b>임의값</b>들 (S-CLI-62DM8M, status: assumed).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 중심은 상수가 아니라 <b>준비맵이 저작한 앵커</b>가 정한다 —
    /// <c>Anchors/ActivityAnchors/GatheringAreaAnchor_01</c>. 기본 준비맵
    /// <c>Map_Default_Preparation_02</c> 에는 그 앵커 앞에 "준비 완료 위치" 간판과 근위병·성화가
    /// 이미 서 있다. 코드가 좌표를 따로 들고 있으면 아트가 간판을 옮겨도 판정만 제자리에 남는다
    /// (S-GD-S5TBXY §4 "Transform 앵커가 위치의 정의").
    /// </para>
    /// </remarks>
    public static class PreparationRallyDefaults
    {
        /// <summary>
        /// 집결 앵커의 맵 루트 기준 경로. 시설 앵커·스폰 앵커와 같은 <c>Anchors/</c> 계약 안에 있다.
        /// </summary>
        public const string AnchorPath = "Anchors/ActivityAnchors/GatheringAreaAnchor_01";

        /// <summary>
        /// 집결 반경(m). ⚠️ <b>에이전트 임의값</b> (S-CLI-62DM8M).
        /// </summary>
        /// <remarks>
        /// <para>
        /// 기본 준비맵의 간판 앞 공간을 기준으로 골랐다 — 성화가 x = ±4.1, 근위병이 ±3.6 에 서 있어
        /// <b>4 m 면 연출물 안쪽</b>이다. 시설 반경 5 m 보다 좁게 잡은 이유는 룰렛 구역(중심 원점,
        /// 반경 5)과의 여유다: 앵커가 z = 12.5 이므로 12.5 - 5 - 4 = <b>3.5 m</b> 만 남는다.
        /// </para>
        /// <para>
        /// ⚠️ <b>더 키우려면 반경이 아니라 앵커 간격부터 본다.</b> 8 명이 동시에 서야 하므로 좁으면
        /// "한 명만 안 들어와서 안 넘어간다" 가 되고, 넓히면 룰렛 앞에 선 사람이 집결로 세어진다.
        /// </para>
        /// </remarks>
        public const float Radius = 4f;

        /// <summary>앵커를 읽지 못했을 때의 폴백 중심 X. 기본 준비맵 앵커 좌표와 같다.</summary>
        public const float FallbackCenterX = 0f;

        /// <summary>앵커를 읽지 못했을 때의 폴백 중심 Z. 기본 준비맵 앵커 좌표와 같다.</summary>
        public const float FallbackCenterZ = 12.5f;

        /// <summary>
        /// 집결만으로 전투를 열 수 있는 최소 인원. 매치 최소 인원과 같은 값이다 (S-GD-CC8KMR).
        /// </summary>
        /// <remarks>
        /// 혼자 남은 방에서 "1 / 1" 이 되는 순간 전투로 넘어가면, 어차피 전투 시작이 인원 부족으로
        /// 거절되어 <b>매 틱 보류 로그만 쌓인다.</b> 판정 쪽에서 미리 닫는다.
        /// </remarks>
        public const int MinimumParticipants = 2;
    }

    /// <summary>
    /// Host 가 확정해 전 피어에 보내는 집결 현황 — <b>"현재 있는 인원 / 총원"</b> (S-GD-6VWPVE).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>받는 쪽은 아무것도 재지 않는다.</b> 화면이 스스로 거리를 재면 판정이 두 곳에 생기고,
    /// 자기 화면에서는 들어갔는데 전투가 안 열리는 상태가 만들어진다 — 시설 구역이 같은 이유로
    /// Host 에서 다시 재는 것(S-CLI-9QYFCT 규칙 1)과 같은 근거다.
    /// </para>
    /// <para>
    /// <see cref="IsTracking"/> 이 거짓이면 <b>Host 가 아직 집결을 재지 않았다</b>는 뜻이다(준비 단계가
    /// 아니거나 참가자 명부가 비었다). 그때 화면은 "0 / 0" 을 그리지 않고 표시 자체를 접는다.
    /// </para>
    /// </remarks>
    public readonly struct PreparationRallyStatus : IEquatable<PreparationRallyStatus>
    {
        public PreparationRallyStatus(int insideCount, int totalCount)
        {
            TotalCount = Math.Max(0, totalCount);
            // 들어온 인원이 총원을 넘는 일은 없어야 하지만, 넘긴 값을 그대로 실으면 화면에 "9 / 8" 이
            // 뜨고 완료 판정이 조용히 참이 된다. 계약 경계에서 잘라 둔다.
            InsideCount = Math.Clamp(insideCount, 0, TotalCount);
        }

        /// <summary>Host 가 이번 틱에 집결 구역 안에서 확인한 참가자 수.</summary>
        public int InsideCount { get; }

        /// <summary>Host 가 세고 있는 참가자 총원. 0 이면 재지 않았다는 뜻이다.</summary>
        public int TotalCount { get; }

        /// <summary>Host 가 집결을 재고 있는가. 거짓이면 화면은 표시를 접는다.</summary>
        public bool IsTracking => TotalCount > 0;

        /// <summary>
        /// 전원이 모였는가. <b>최소 인원(S-GD-CC8KMR)을 함께 본다</b> —
        /// 혼자 남은 방의 "1 / 1" 로 전투를 열지 않는다.
        /// </summary>
        public bool IsComplete =>
            TotalCount > 0 && InsideCount >= TotalCount;

        public bool Equals(PreparationRallyStatus other) =>
            InsideCount == other.InsideCount && TotalCount == other.TotalCount;

        public override bool Equals(object obj) => obj is PreparationRallyStatus other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(InsideCount, TotalCount);

        public static bool operator ==(PreparationRallyStatus left, PreparationRallyStatus right) =>
            left.Equals(right);

        public static bool operator !=(PreparationRallyStatus left, PreparationRallyStatus right) =>
            !left.Equals(right);

        public override string ToString() => $"{InsideCount} / {TotalCount}";
    }
}

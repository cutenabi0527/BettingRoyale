namespace BettingRoyale.Data.Upgrade
{
    /// <summary>
    /// 강화 요청의 <b>부위를 정수 한 칸에 담는</b> 변환 (S-GD-Z9CJ4H).
    ///
    /// <para>
    /// <c>EconomyCommandRequest</c> 에는 정수 한 칸(<c>CandidateIndex</c>)뿐이라 부위를 실을 자리가
    /// 없다. 룰렛이 같은 문제를 <c>RouletteRequestPayload</c> 로 풀었고 강화도 같은 방법을 쓴다 —
    /// 전송 구조체를 늘리지 않으므로 프로토콜 버전이 오르지 않는다.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 푸는 자리는 여기 하나뿐이다.</b> 두 군데서 풀면 한쪽만 고쳐서 조용히 어긋난다.
    /// 그리고 <b>아는 값만 통과시킨다</b> — 회선에서 온 값이라 <c>(UpgradeSlot)7</c> 이 실제로 올 수 있고,
    /// 그것이 그대로 도메인에 들어가면 <b>무기 가격을 내고 방어구 단계가 오르는</b> 경로가 된다
    /// (<c>UpgradeRouletteRegressionTests.모르는_부위는_조용히_통과하지_않고_터진다</c>).
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public static class UpgradeRequestPayload
    {
        /// <summary>부위를 요청 한 칸으로 담는다.</summary>
        public static int Pack(UpgradeSlot slot) => (int)slot;

        /// <summary>
        /// 요청 한 칸을 부위로 되돌린다. <b>모르는 값이면 <c>false</c></b> —
        /// 우리가 담은 적 없는 값이므로 부르는 쪽이 거절로 처리한다.
        /// </summary>
        public static bool TryUnpack(int payload, out UpgradeSlot slot)
        {
            switch (payload)
            {
                case (int)UpgradeSlot.Weapon:
                    slot = UpgradeSlot.Weapon;
                    return true;

                case (int)UpgradeSlot.Armor:
                    slot = UpgradeSlot.Armor;
                    return true;

                default:
                    slot = UpgradeSlot.Weapon;
                    return false;
            }
        }

        /// <summary>아는 부위인가. 게이트가 방어선으로 한 번 더 본다.</summary>
        public static bool IsKnown(UpgradeSlot slot) =>
            slot == UpgradeSlot.Weapon || slot == UpgradeSlot.Armor;
    }
}

namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 체력 구현이 <b>추가로</b> 제공하는 스탯 적용 경계다. 축복·강화 합산 결과를 최대 체력에 먹이고
    /// 피해 계산기를 끼우는 데 필요한 것만 담는다.
    /// </summary>
    /// <remarks>
    /// <see cref="ICombatHealthDriver"/> 에 넣지 않는다 — 그쪽은 복제 표현 계약이고 테스트 대역이 이미 여럿 구현한다.
    /// 어댑터는 <c>driver as ICombatHealthStatDriver</c> 로 물어보고 없으면 스탯을 적용하지 않는다.
    /// </remarks>
    public interface ICombatHealthStatDriver
    {
        /// <summary>직렬화 원본 최대 체력. <b>이전 보정값을 절대 반영하지 않는다</b> — 합산 기준선이 매 라운드 흘러가면 안 된다.</summary>
        float ConfiguredMaxHealth { get; }

        /// <summary>합산 결과를 <b>절대값</b>으로 지정한다. null 은 직렬화 원본 복원이고, 비유한·0 이하는 false 다.</summary>
        bool SetMaxHealthOverride(float? maxHealth);

        /// <summary>권한·방어 검사 뒤의 피해 계산만 교체한다. null 이면 원래 피해량을 쓴다.</summary>
        void SetDamageResolver(ICombatDamageResolver resolver);
    }

    /// <summary>이동 구현이 추가로 제공하는 이동 속도 배율 경계.</summary>
    public interface ICombatMotionStatDriver
    {
        /// <summary>구성값(또는 디버그 보정값)에 곱할 배율을 <b>교체</b>한다. 유한 양수만 받고, 그 외에는 이전 값을 유지하며 false 다.</summary>
        bool SetMoveSpeedMultiplier(float multiplier);

        /// <summary>
        /// 기획 데이터가 정한 <b>기본 이동 속도(m/s)</b> 를 교체한다. 프리팹 직렬화 값을 대신한다.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>배율이 아니라 실제 속도다.</b> 엑셀 <c>stats</c> 시트의 <c>move-speed</c> 는 기본값이
        /// 5(= 초당 5m)이고, 축복·강화 기여가 곱해진 <b>최종 속도</b>가 여기로 들어온다.
        /// 이 값을 배율로 넘기면 전원이 5배속이 된다.
        /// 유한 양수가 아니면 <b>프리팹 값으로 되돌리고</b> false 를 돌려준다.
        /// </remarks>
        bool SetStatMoveSpeed(float metersPerSecond);

        /// <summary>캐릭터(프리팹)에 설정된 기본 이동 속도(m/s). 게임 데이터가 없을 때의 기본값이다.</summary>
        float ConfiguredMoveSpeed { get; }
    }

    /// <summary>공격 구현이 추가로 제공하는 공격 속도 배율 경계.</summary>
    public interface ICombatActionStatDriver
    {
        /// <summary>모든 무기의 쿨타임에 적용할 공속 배율을 <b>교체</b>한다. 유한 양수만 받고, 그 외에는 이전 값을 유지하며 false 다.</summary>
        bool SetAttackSpeedMultiplier(float multiplier);
    }
}

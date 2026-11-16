using System.Collections.Generic;

namespace BettingRoyale.Data.Combat
{
    /// <summary>무기 하나의 이동 관련 실측 조정값. 0·1 은 "조정 없음" 을 뜻한다.</summary>
    /// <remarks>
    /// <see cref="WeaponStats"/> 의 조정 레이어와 형제지만 <b>정본 상수를 두지 않는다</b> —
    /// 기본 이동속도의 정본은 프리팹(<c>CombatantMotor.moveSpeed</c>)과 기획 엑셀 <c>move-speed</c> 두 곳이고,
    /// 여기에 세 번째 정본을 만들면 티켓 S15P21A606-200 이 무기 수치에서 없앤 중복을 이동 쪽에 다시 만드는 셈이다.
    /// </remarks>
    public readonly struct WeaponMovementTuning
    {
        /// <summary>기본 이동 속도(m/s). 0 이하이면 조정하지 않는다.</summary>
        public readonly float MoveSpeedMetersPerSecond;

        /// <summary>공격 직후 이동 속도에 곱할 배율. 1 이상이면 감속하지 않는다.</summary>
        public readonly float AttackSlowMultiplier;

        /// <summary>공격 직후 감속이 유지되는 시간(초). 0 이하이면 감속하지 않는다.</summary>
        public readonly float AttackSlowSeconds;

        public WeaponMovementTuning(
            float moveSpeedMetersPerSecond, float attackSlowMultiplier, float attackSlowSeconds)
        {
            MoveSpeedMetersPerSecond = moveSpeedMetersPerSecond;
            AttackSlowMultiplier = attackSlowMultiplier;
            AttackSlowSeconds = attackSlowSeconds;
        }

        public bool HasMoveSpeed => CombatTuning.IsFinitePositive(MoveSpeedMetersPerSecond);

        /// <summary>감속은 <b>배율과 지속 시간이 둘 다 유효할 때만</b> 켜진다. 한쪽만 만지면 아무 일도 없다.</summary>
        public bool HasAttackSlow =>
            CombatTuning.IsFinitePositive(AttackSlowSeconds)
            && CombatTuning.IsFinitePositive(AttackSlowMultiplier)
            && AttackSlowMultiplier < 1f;

        public bool IsEmpty => !HasMoveSpeed && !HasAttackSlow;
    }

    /// <summary>대시 실측 조정값. 0 이하는 "조정 없음" 이다.</summary>
    /// <remarks>
    /// <b>대시 지속 시간은 노브로 두지 않는다.</b> 무적 시간이 대시 시간에 묶여 있어
    /// (<c>CombatantMotor.EffectiveDashSpeed</c> 주석) 지속을 건드리면 무적 길이가 함께 움직인다.
    /// 거리는 <c>속도 × 지속</c> 의 파생값이므로 거리 노브는 속도를 역산해서 넣는다 — 기획 데이터의
    /// <c>dash-distance</c> 가 쓰는 방식과 같다.
    /// </remarks>
    public readonly struct DashMovementTuning
    {
        public readonly float SpeedMetersPerSecond;
        public readonly int MaxCharges;
        public readonly float RechargeSeconds;

        public DashMovementTuning(float speedMetersPerSecond, int maxCharges, float rechargeSeconds)
        {
            SpeedMetersPerSecond = speedMetersPerSecond;
            MaxCharges = maxCharges;
            RechargeSeconds = rechargeSeconds;
        }

        public bool HasSpeed => CombatTuning.IsFinitePositive(SpeedMetersPerSecond);
        public bool HasMaxCharges => MaxCharges > 0;
        public bool HasRecharge => CombatTuning.IsFinitePositive(RechargeSeconds);
        public bool IsEmpty => !HasSpeed && !HasMaxCharges && !HasRecharge;
    }

    /// <summary>
    /// 90_CombatLab 밸런스 실측용 <b>조정 레이어</b>. 이동·대시 수치를 런타임에 덮어쓴다
    /// (S15P21A606-191 · S15P21A606-192).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>비어 있는 것이 정상 상태</b>다. 제품 경로는 아무것도 넣지 않으므로 이 클래스가 있다고 해서
    /// 동작이 달라지지 않는다. Dev 패널만 값을 넣는다.
    /// </para>
    /// <para>
    /// ⚠️ <b>저장되지 않는다.</b> 실측으로 찾은 값은 사람이 프리팹·기획 엑셀에 옮겨 적어야 한다.
    /// </para>
    /// <para>
    /// ⚠️ 이동 속도 조정은 <c>CombatantMotor.SetDebugMoveSpeedOverride</c> 칸으로 들어간다.
    /// 그 칸은 축복·강화가 계산한 <b>기본 속도를 대신</b>하므로, 조정이 걸려 있는 동안에는 축복의
    /// 기본 속도 기여가 보이지 않는다(배율 기여는 그대로 곱해진다). 실측 도구라 의도한 동작이지만,
    /// 축복과 함께 재보려면 조정을 끄고 봐야 한다.
    /// </para>
    /// </remarks>
    public static class CombatTuning
    {
        private static readonly Dictionary<WeaponType, WeaponMovementTuning> WeaponMovement = new();
        private static DashMovementTuning dash;

        /// <summary>조정값이 하나라도 걸려 있으면 true. HUD 가 "지금 정본이 아니다" 를 표시하는 근거다.</summary>
        public static bool HasOverrides => WeaponMovement.Count > 0 || !dash.IsEmpty;

        /// <summary>
        /// 조정값이 바뀔 때마다 오르는 번호. 소비자가 <b>자기 상태를 다시 맞춰야 하는지</b> 싸게 판단하는 근거다.
        /// </summary>
        /// <remarks>
        /// ⚠️ 이게 없으면 조정을 <b>밀어넣는</b>(push) 수밖에 없고, 그러면 <b>그 순간 존재하던 전투원만</b>
        /// 값을 받는다 — 라운드를 다시 시작해 새로 스폰되면 조정이 조용히 사라진다. 실제로 그 버그가 났다.
        /// 무기 수치(<see cref="WeaponStats"/>)는 쓸 때마다 읽어가므로(pull) 이 문제가 없었다.
        /// </remarks>
        public static int Revision { get; private set; }

        public static DashMovementTuning Dash => dash;

        /// <summary>이 무기에 걸린 이동 조정값. 없으면 false 와 빈 값이다.</summary>
        public static bool TryGetWeaponMovement(WeaponType type, out WeaponMovementTuning tuning) =>
            WeaponMovement.TryGetValue(type, out tuning);

        /// <summary>
        /// 무기 이동 조정값을 건다. <see cref="WeaponStats"/> 가 모르는 무기는 거부한다 —
        /// 수치가 없는 무기에 이동만 지어내지 않기 위해서다.
        /// </summary>
        public static bool TrySetWeaponMovement(WeaponType type, WeaponMovementTuning tuning)
        {
            if (!WeaponStats.TryGetDefault(type, out _))
            {
                return false;
            }

            if (tuning.IsEmpty)
            {
                WeaponMovement.Remove(type);
                Revision++;
                return true;
            }

            WeaponMovement[type] = tuning;
            Revision++;
            return true;
        }

        public static void ClearWeaponMovement(WeaponType type)
        {
            WeaponMovement.Remove(type);
            Revision++;
        }

        public static void SetDash(DashMovementTuning tuning)
        {
            dash = tuning;
            Revision++;
        }

        public static void ClearDash()
        {
            dash = default;
            Revision++;
        }

        /// <summary>모든 조정값을 해제해 정본 상태로 되돌린다.</summary>
        public static void ClearAll()
        {
            WeaponMovement.Clear();
            dash = default;
            Revision++;
        }

        internal static bool IsFinitePositive(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

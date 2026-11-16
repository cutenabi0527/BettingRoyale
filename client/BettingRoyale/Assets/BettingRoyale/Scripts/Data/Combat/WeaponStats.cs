using System.Collections.Generic;

namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 공격 직후 이동 속도에 걸리는 감속. 무기 정본이 들고 있는 <b>제품 규칙</b>이다 (S-GD-MVRG3H).
    /// </summary>
    /// <remarks>
    /// 90_CombatLab 의 실측 조정(<c>CombatTuning</c>)과 다르다 — 그쪽은 Dev 전용이고 비어 있는 것이
    /// 정상이며, 이것은 제품 경로가 항상 읽는 값이다. 조정이 걸려 있으면 조정이 우선한다.
    /// </remarks>
    public readonly struct WeaponAttackSlow
    {
        /// <summary>이동 속도에 곱할 배율. 1 이상이면 감속하지 않는다.</summary>
        public readonly float Multiplier;

        /// <summary>감속이 유지되는 시간(초). 0 이하면 감속하지 않는다.</summary>
        public readonly float Seconds;

        public WeaponAttackSlow(float multiplier, float seconds)
        {
            Multiplier = multiplier;
            Seconds = seconds;
        }

        /// <summary>감속이 실제로 걸리는가. <b>배율과 지속이 둘 다 유효할 때만</b> 참이다.</summary>
        public bool IsActive =>
            Seconds > 0f && !float.IsNaN(Seconds) && !float.IsInfinity(Seconds)
            && Multiplier > 0f && Multiplier < 1f;

        /// <summary>감속 없음. 배율 1 이 항등원이라 계산에 그대로 끼워도 안전하다.</summary>
        public static WeaponAttackSlow None => new(1f, 0f);
    }

    /// <summary>
    /// 검·활 기본 수치의 <b>정본</b>. 씬·프리팹·실행 경로가 모두 이 하나만 읽는다 (S-CLI-RQB4QD).
    /// </summary>
    /// <remarks>
    /// 여기 값은 S-GD-NEGNFZ 의 임시 플레이테스트 수치이며 <b>확정 밸런스가 아니다</b>(status: assumed).
    /// 수치를 고치는 곳은 이 파일 하나다 — 프리팹·씬·다른 카탈로그에 같은 값을 다시 두지 않는다.
    /// 중복이 생기는 순간 경로마다 다른 전투가 되고, 그것이 이 파일이 생긴 이유다.
    ///
    /// <para><see cref="WeaponConfig"/> 를 정의한 CombatConfigs 는 "여기에 기본값·상수를 두지 않는다" 고
    /// 못박고 있으므로 구조체와 값을 다른 파일로 분리했다. 그 규칙의 취지(수치를 계약 타입에 섞지 않는다)는
    /// 유지하면서, 흩어져 있던 값만 한곳에 모은 것이다.</para>
    ///
    /// <para>원격 데이터(<c>gamedata.bin</c>) 이관은 이 클래스의 반환값만 교체하면 된다 (D-041).
    /// 지금 이관하지 않는 이유는 전송 수단이 없어서가 아니라 — 다운로드·캐시는 이미 동작한다 —
    /// MessagePack 이 아직 스파이크이고 <b>AOT 문제가 Mono 빌드에서 드러나지 않기 때문</b>이다 (D-073).</para>
    /// </remarks>
    public static class WeaponStats
    {
        /// <summary>검: 피해 25 / 사거리 2m / 쿨타임 0.6초. 근접이라 투사체 속도는 0 이다.</summary>
        public static readonly WeaponConfig Sword =
            new WeaponConfig(damage: 25f, range: 2f, cooldownSeconds: 0.6f, projectileSpeed: 0f);

        /// <summary>
        /// 활: 피해 22 / 최대 비행거리 12m / 쿨타임 0.75초 / 탄속 18 m/s.
        /// </summary>
        /// <remarks>
        /// <b>저연사 고위력</b>이다. 명목 사거리를 <see cref="Bow"/> 의 유효 사거리에 맞춘 것이 핵심이다 —
        /// 사거리 12m·탄속 18m/s면 최대 거리 비행은 약 0.67초다. 기획 데이터의
        /// <c>stats.projectile-speed</c> 기준선과 무기 탄속을 같은 값으로 유지한다(S-GD-68GK4P).
        /// 한 발의 무게를 올린 대신 <see cref="BowAttackSlow"/> 로 빗나감의 대가를 붙였다.
        /// </remarks>
        public static readonly WeaponConfig Bow =
            new WeaponConfig(damage: 22f, range: 12f, cooldownSeconds: 0.75f, projectileSpeed: 18f,
                collisionRadius: BowCollisionRadius);

        /// <summary>
        /// 활 화살의 기본 충돌 반경(m) — 코드 대역. 정본은 weapons.xlsx 의 CollisionRadius 다.
        /// ⚠️ 에이전트 임의값(S-GD-G3D2WP 는 "기본 반경 × (1 + 보너스)" 만 정했다). 0 이면 세트 배율이 뜻을 잃는다.
        /// </summary>
        public const float BowCollisionRadius = 0.15f;

        /// <summary>검은 공격 직후 감속하지 않는다. 붙어야 하는 무기에 후딜까지 주면 접근 자체가 성립하지 않는다.</summary>
        public static readonly WeaponAttackSlow SwordAttackSlow = WeaponAttackSlow.None;

        /// <summary>
        /// 활: 공격 직후 <b>0.5초 동안 이동 속도 ×0.4</b> (S-GD-MVRG3H).
        /// </summary>
        /// <remarks>
        /// <b>지속은 쿨타임의 2/3 를 넘기지 않는다</b>(0.5 ÷ 0.75 = 67%). 넘기면 다음 사격이 감속 창을
        /// 갱신해 사실상 상시 감속이 되고, 그러면 "공격 직후 페널티" 가 아니라 그냥 느린 캐릭터가 된다 —
        /// 플레이어가 <i>지금 쏠 것인가</i> 를 고를 여지가 사라진다. 쿨타임을 바꾸면 이 비율을 다시 본다.
        /// </remarks>
        public static readonly WeaponAttackSlow BowAttackSlow = new(multiplier: 0.4f, seconds: 0.5f);

        /// <summary>
        /// 개발용 런타임 조정값. <b>비어 있는 것이 정상 상태</b>이며 제품 경로는 아무것도 넣지 않는다.
        /// </summary>
        /// <remarks>
        /// 90_CombatLab 의 Dev 패널이 밸런스 실측(S15P21A606-191/-192)을 위해 여기에만 값을 넣는다.
        /// 정본 상수는 그대로 남으므로 <see cref="ClearOverrides"/> 한 번으로 원래 값으로 돌아온다.
        /// ⚠️ 여기 들어간 값은 <b>저장되지 않는다</b> — 실측으로 찾은 수치는 사람이 위 상수에 옮겨 적어야 한다.
        /// </remarks>
        private static readonly Dictionary<WeaponType, WeaponConfig> Overrides = new();

        /// <summary>조정값이 하나라도 걸려 있으면 true. HUD·진단이 "지금 정본이 아니다" 를 표시하는 근거다.</summary>
        public static bool HasOverrides => Overrides.Count > 0;

        /// <summary>
        /// 수치가 정의된 무기면 true 와 설정을 돌려준다. 조정값이 걸려 있으면 그것이 우선이다.
        /// 미구현 무기(둔기(Mace))와 <see cref="WeaponType.None"/> 은 WeaponCatalog.TryGetTags 와
        /// 같이 false 다 — 예외를 흐름 제어로 쓰지 않는 TryGet 패턴이다 (S-GD-7DRYPY, coding-convention 5절).
        /// 태그와 수치 중 한쪽만 등록하면 "태그는 없는데 수치는 있는" 무기가 생기므로 둘을 함께 추가한다.
        /// </summary>
        /// <summary>
        /// 무기 수치. 우선순위: 개발 덮어쓰기(<see cref="TrySetOverride"/>) → <b>기획 엑셀 weapons 시트</b>
        /// (<c>BalanceCatalogs.Current.Weapons</c>, S-CLI-QXRTWX) → 코드 대역(<see cref="Sword"/>·<see cref="Bow"/>).
        /// </summary>
        public static bool TryGet(WeaponType type, out WeaponConfig config)
        {
            if (Overrides.TryGetValue(type, out config))
            {
                return true;
            }

            var data = BettingRoyale.Data.Balance.BalanceCatalogs.Current.Weapons;
            if (data != null && data.TryGet(type, out config))
            {
                return true;
            }

            return TryGetDefault(type, out config);
        }

        /// <summary>
        /// 이 무기의 공격 직후 감속. 정의되지 않은 무기는 <see cref="WeaponAttackSlow.None"/> 이다.
        /// </summary>
        /// <remarks>
        /// 미구현 무기에도 <c>None</c> 을 돌려준다 — 감속은 "없음" 이 항등원이라 호출부가 분기하지 않아도
        /// 안전하고, 수치가 없는 무기에 감속만 지어내는 일도 생기지 않는다.
        /// </remarks>
        /// <summary>공격 직후 감속. 기획 엑셀이 있으면 그 값(weapons.AttackSlow*), 없으면 코드 대역.</summary>
        public static WeaponAttackSlow GetAttackSlow(WeaponType type)
        {
            var data = BettingRoyale.Data.Balance.BalanceCatalogs.Current.Weapons;
            if (data != null && data.TryGetAttackSlow(type, out WeaponAttackSlow slow))
            {
                return slow;
            }

            return type switch
            {
                WeaponType.Sword => SwordAttackSlow,
                WeaponType.Bow => BowAttackSlow,
                _ => WeaponAttackSlow.None,
            };
        }

        /// <summary>정본이 정의한 값. 조정값을 무시하고 항상 상수를 돌려준다 — HUD 의 "되돌리기" 기준이다.</summary>
        public static bool TryGetDefault(WeaponType type, out WeaponConfig config)
        {
            switch (type)
            {
                case WeaponType.Sword:
                    config = Sword;
                    return true;
                case WeaponType.Bow:
                    config = Bow;
                    return true;
                default:
                    config = default;
                    return false;
            }
        }

        /// <summary>
        /// 개발용 조정값을 건다. 정본에 없는 무기는 조정도 받지 않는다 — 수치를 지어내지 않기 위해서다.
        /// </summary>
        public static bool TrySetOverride(WeaponType type, WeaponConfig config)
        {
            if (!TryGetDefault(type, out _))
            {
                return false;
            }

            Overrides[type] = config;
            return true;
        }

        /// <summary>한 무기의 조정값만 해제한다.</summary>
        public static void ClearOverride(WeaponType type) => Overrides.Remove(type);

        /// <summary>모든 조정값을 해제해 정본 상태로 되돌린다.</summary>
        public static void ClearOverrides() => Overrides.Clear();
    }
}

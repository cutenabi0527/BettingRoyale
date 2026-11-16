using System.Collections.Generic;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Equipment;

namespace BettingRoyale.Data.Throwing
{
    /// <summary>
    /// 마력 폭발병 1종만 담은 코드 상수 카탈로그.
    /// </summary>
    /// <remarks>
    /// 여기 수치는 승인된 "초기 튜닝 값"이며 확정 밸런스가 아니다 (S-GD-78REEH).
    /// 표시 이름도 초기 이름이다. 실제 데이터 공급은 이후 Inspector/Addressables 어댑터로 교체하고,
    /// 이 클래스는 도메인 테스트와 초기 통합의 기준 구현으로 남긴다.
    /// ScriptableObject·직렬화 자산에 의존하지 않는다.
    /// </remarks>
    public sealed class RecommendedMagicBottleCatalog
        : IEquipmentItemCatalog, IEquipmentItemCarryLimitCatalog, IThrowingItemCatalog
    {
        public const int MagicBottleItemId = 2103;
        public const int MagicBottleVisualId = 5103;
        public const int MagicBottleMaxStack = 3;
        public const int MagicBottleCarryLimit = 3;
        public const int MagicBottleDropQuantity = 1;
        public const int MagicBottleDamage = 40;
        public const int MagicBottlePrice = 200;
        public const float MagicBottleThrowDistanceMeters = 8f;
        public const float MagicBottleExplosionRadiusMeters = 2.5f;
        /// <summary>최대 사거리에서의 비행 시간. 가까울수록 짧아진다 (S-GD-JFG6XM · ThrowFlightTiming).</summary>
        /// <remarks>
        /// 1초에서 0.7초로 줄였다 — 8m 를 1초에 날면 평균 8m/s 라 보고 걸어서 피할 수 있었다.
        /// 0.7초면 평균 11.4m/s 로 반응해야 피한다.
        /// </remarks>
        public const double MagicBottleFlightSeconds = 0.7d;
        /// <summary>착지 후 폭발까지의 경고 시간.</summary>
        /// <remarks>
        /// 1초에서 0.6초로 줄였다. 반경 2.5m 를 0.6초에 벗어나려면 4.17m/s 가 필요하다 — 이동 속도 5 로
        /// <b>반응하면 피할 수 있되 서 있으면 맞는</b> 선이다. 대시(무적 0.15초)면 확실히 피한다.
        /// </remarks>
        public const double MagicBottleWarningSeconds = 0.6d;
        public const double SharedCooldownSeconds = 2d;

        // 노션 컨셉의 4상태 색상 계약. 착탄은 기존 전투 위험 표기의 주황 계열을 보존하고,
        // 발동 폭발만 어댑터에서 연보라 VFX로 틴트한다.
        public static readonly ThrowGuideColor MagicBottleGuideColor = new(48, 170, 255);
        public static readonly ThrowGuideColor MagicBottleFlightGuideColor = new(255, 64, 64);
        public static readonly ThrowGuideColor MagicBottleImpactOverlayColor = new(255, 96, 32);

        /// <summary>초기 표시 이름. 확정 명칭이 아니다.</summary>
        public const string MagicBottleDisplayName = "마력 폭발병";

        /// <summary>인벤토리 규칙이 보는 장비 정의. 무기 게임플레이 타입과 외형 원형은 모두 None 이다.</summary>
        public static readonly EquipmentItemDefinition MagicBottleEquipment = new(
            MagicBottleItemId,
            EquipmentItemKind.Consumable,
            EquipmentArchetype.None,
            MagicBottleVisualId,
            WeaponType.None,
            MagicBottleMaxStack);

        /// <summary>투척 규칙이 보는 불변 정의.</summary>
        public static readonly ThrowingItemDefinition MagicBottle = new(
            MagicBottleItemId,
            MagicBottleDisplayName,
            MagicBottleThrowDistanceMeters,
            MagicBottleDamage,
            MagicBottleExplosionRadiusMeters,
            MagicBottlePrice,
            MagicBottleDropQuantity,
            MagicBottleCarryLimit,
            MagicBottleFlightSeconds,
            MagicBottleWarningSeconds,
            SharedCooldownSeconds,
            MagicBottleGuideColor,
            MagicBottleFlightGuideColor,
            MagicBottleImpactOverlayColor,
            ThrowActivationVfx.MagicExplosion,
            MagicBottleWarningSeconds,
            2d,
            ThrowVisibilityPolicy.OwnerAndAllies);

        /// <summary>
        /// Direct Play 보급 UI와 시작 지급이 함께 읽는 등록 투척류다.
        /// 새 병을 추가할 때 이 목록과 카탈로그 질의를 같이 확장하면 종류별 버튼이 자동으로 늘어난다.
        /// </summary>
        public static IReadOnlyList<ThrowingItemDefinition> AllThrowables { get; } =
            new[] { MagicBottle };

        public bool TryGet(int itemId, out EquipmentItemDefinition definition)
        {
            if (itemId == MagicBottleItemId)
            {
                definition = MagicBottleEquipment;
                return true;
            }

            definition = default;
            return false;
        }

        public bool TryGetCarryLimit(int itemId, out int totalCarryLimit)
        {
            if (itemId == MagicBottleItemId)
            {
                totalCarryLimit = MagicBottleCarryLimit;
                return true;
            }

            totalCarryLimit = 0;
            return false;
        }

        /// <remarks>
        /// 투척 규칙이 정의를 얻는 <b>유일한 경로</b>라 실측 조정(<see cref="ThrowTuning"/>)도 여기서 얹는다.
        /// 조정이 없으면 <see cref="MagicBottle"/> 상수가 그대로 나가므로 제품 경로는 달라지지 않는다.
        /// </remarks>
        public bool TryGetThrowing(int itemId, out ThrowingItemDefinition definition)
        {
            if (itemId == MagicBottleItemId)
            {
                definition = ThrowTuning.Apply(MagicBottle);
                return true;
            }

            definition = default;
            return false;
        }
    }
}

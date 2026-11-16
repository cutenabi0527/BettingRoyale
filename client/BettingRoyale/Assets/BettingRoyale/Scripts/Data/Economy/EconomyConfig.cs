using System;

namespace BettingRoyale.Data.Economy
{
    /// <summary>
    /// 경제 규칙의 설정 모델 (S-GD-2CFK2Z, S-GD-P5RFZM).
    ///
    /// 완료 조건이 "수치를 하드코딩하지 않고 설정 모델에서 변경 가능" 이므로,
    /// 도메인 코드는 상수를 직접 읽지 않고 <b>항상 이 객체를 통해</b> 값을 얻는다.
    /// <c>Default*</c> 상수는 "스펙이 정한 기본값" 이지 "코드에 박힌 값" 이 아니다.
    ///
    /// 불변이다. 값을 바꾸려면 <see cref="WithRoundGrantGold"/> 처럼 새 인스턴스를 만든다 —
    /// 라운드 중간에 설정이 바뀌어 같은 매치에서 규칙이 달라지는 것을 막는다.
    ///
    /// UnityEngine 을 참조하지 않는다 (D-060). ScriptableObject 래퍼는 이 클래스를 담기만 한다.
    /// </summary>
    public sealed class EconomyConfig
    {
        /// <summary>라운드마다 지급하는 기본 골드 (S-GD-2CFK2Z).</summary>
        public const int DefaultRoundGrantGold = 1_000;

        public EconomyConfig(int roundGrantGold, CarryOverLimit carryOverLimit)
        {
            if (roundGrantGold < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(roundGrantGold), roundGrantGold, "라운드 지급 골드는 음수일 수 없습니다.");
            }

            RoundGrantGold = roundGrantGold;
            CarryOverLimit = carryOverLimit;
        }

        /// <summary>라운드 시작 시 지급되는 골드.</summary>
        public int RoundGrantGold { get; }

        /// <summary>라운드 사이에 넘길 수 있는 골드의 상한.</summary>
        public CarryOverLimit CarryOverLimit { get; }

        /// <summary>스펙 기본값(1,000G 지급 / 10,000G 이월)으로 만든 설정.</summary>
        /// <summary>기획 데이터(constants.ROUND_GOLD·CARRYOVER_CAP)의 설정. 없으면 코드 대역(1,000G · 이월 10,000G).</summary>
        public static EconomyConfig Default => BettingRoyale.Data.Balance.BalanceCatalogs.Current.Economy;

        public EconomyConfig WithRoundGrantGold(int roundGrantGold) =>
            new(roundGrantGold, CarryOverLimit);

        public EconomyConfig WithCarryOverLimit(CarryOverLimit carryOverLimit) =>
            new(RoundGrantGold, carryOverLimit);

        /// <summary>설정 화면의 이월 한도 입력값을 그대로 받아 반영한다 (50,000 초과 = 무제한).</summary>
        public EconomyConfig WithCarryOverLimitSetting(int settingGold) =>
            new(RoundGrantGold, CarryOverLimit.FromSetting(settingGold));

        public override string ToString() =>
            $"지급 {RoundGrantGold:N0}G / 이월 한도 {CarryOverLimit}";
    }
}

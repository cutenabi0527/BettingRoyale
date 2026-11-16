using System;

namespace BettingRoyale.Data.Economy
{
    /// <summary>
    /// 라운드가 끝날 때 다음 라운드로 넘길 수 있는 골드의 상한 (S-GD-P5RFZM).
    ///
    /// 설정 입력은 0 ~ 50,000G 범위이고, <b>50,000 을 넘는 값이나 "무제한" 선택은 무한 이월</b>을 뜻한다.
    /// 50,001 은 잘못된 입력이 아니라 무한을 의미하는 유효한 입력이므로 거부하거나 잘라내지 않는다.
    ///
    /// UnityEngine 을 참조하지 않는다 — Unity 밖 dotnet 테스트에서 그대로 컴파일된다 (D-060).
    /// </summary>
    public readonly struct CarryOverLimit : IEquatable<CarryOverLimit>
    {
        /// <summary>설정으로 지정할 수 있는 최솟값. 0 이면 이월이 없다 (매 라운드 초기화).</summary>
        public const int SettingMinGold = 0;

        /// <summary>설정으로 지정할 수 있는 최댓값. 이 값을 넘기면 무한 이월로 해석한다.</summary>
        public const int SettingMaxGold = 50_000;

        /// <summary>스펙이 정한 기본 이월 한도.</summary>
        public const int DefaultGold = 10_000;

        /// <summary>이월에 상한이 없다. 남은 골드가 전부 넘어간다.</summary>
        public static readonly CarryOverLimit Unlimited = new(0, true);

        /// <summary>스펙 기본값(10,000G)으로 만든 한도.</summary>
        public static CarryOverLimit Default => new(DefaultGold, false);

        private readonly int gold;
        private readonly bool isUnlimited;

        private CarryOverLimit(int gold, bool isUnlimited)
        {
            this.gold = gold;
            this.isUnlimited = isUnlimited;
        }

        /// <summary>상한이 없는가.</summary>
        public bool IsUnlimited => isUnlimited;

        /// <summary>
        /// 상한 금액. <see cref="IsUnlimited"/> 가 true 면 의미가 없다.
        /// 무한 한도에서 이 값을 읽으려 하면 예외를 던진다 — 조용히 0 을 돌려주면
        /// "이월 없음" 과 구분되지 않아 버그가 숨는다.
        /// </summary>
        public int Gold => isUnlimited
            ? throw new InvalidOperationException("무한 이월 한도에는 금액이 없습니다. IsUnlimited 를 먼저 확인하세요.")
            : gold;

        /// <summary>
        /// 설정 입력값을 한도로 해석한다.
        /// <paramref name="settingGold"/> 가 <see cref="SettingMaxGold"/> 를 넘으면 무한 이월이다.
        /// </summary>
        /// <remarks>
        /// ⚠️ 음수 입력의 처리는 스펙에 없다. 여기서는 0(이월 없음)으로 붙여 처리한다 —
        /// 설정 경로에서 예외를 던지면 UI 가 값을 되돌릴 방법이 없기 때문이다. 확인 후 조정한다.
        /// </remarks>
        public static CarryOverLimit FromSetting(int settingGold)
        {
            if (settingGold > SettingMaxGold)
            {
                return Unlimited;
            }

            return settingGold < SettingMinGold
                ? new CarryOverLimit(SettingMinGold, false)
                : new CarryOverLimit(settingGold, false);
        }

        /// <summary>설정 화면에서 "무제한" 을 고른 경우.</summary>
        public static CarryOverLimit FromUnlimitedSelection() => Unlimited;

        /// <summary>
        /// 라운드 종료 시 남은 골드에 이 한도를 적용한다.
        /// </summary>
        /// <param name="leftoverGold">라운드가 끝났을 때 지갑에 남은 골드. 음수는 허용하지 않는다.</param>
        /// <returns>다음 라운드로 넘어가는 금액과 사라지는 금액.</returns>
        public CarryOverResult Apply(int leftoverGold)
        {
            if (leftoverGold < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(leftoverGold), leftoverGold, "남은 골드는 음수일 수 없습니다.");
            }

            if (isUnlimited || leftoverGold <= gold)
            {
                return new CarryOverResult(leftoverGold, 0);
            }

            return new CarryOverResult(gold, leftoverGold - gold);
        }

        public bool Equals(CarryOverLimit other) =>
            isUnlimited == other.isUnlimited && (isUnlimited || gold == other.gold);

        public override bool Equals(object obj) => obj is CarryOverLimit other && Equals(other);

        public override int GetHashCode() => isUnlimited ? int.MinValue : gold;

        public override string ToString() => isUnlimited ? "무제한" : $"{gold:N0}G";
    }

    /// <summary>이월 계산 결과. 넘어간 금액과 사라진 금액을 함께 돌려준다.</summary>
    public readonly struct CarryOverResult
    {
        /// <summary>다음 라운드로 넘어가는 골드.</summary>
        public readonly int CarriedGold;

        /// <summary>한도를 넘어 사라지는 골드. UI 가 "N G 소멸" 을 보여줄 때 쓴다.</summary>
        public readonly int ForfeitedGold;

        public CarryOverResult(int carriedGold, int forfeitedGold)
        {
            CarriedGold = carriedGold;
            ForfeitedGold = forfeitedGold;
        }
    }
}

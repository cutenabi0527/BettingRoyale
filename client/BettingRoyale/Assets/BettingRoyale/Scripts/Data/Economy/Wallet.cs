using System;

namespace BettingRoyale.Data.Economy
{
    /// <summary>
    /// 플레이어 한 명의 골드 지갑. 지급 · 획득 · 지출 · 이월 네 가지 조작만 제공한다
    /// (Jira S15P21A606-31 "라운드 지급·획득·지출·이월 결과가 2PC 에서 동일하다").
    ///
    /// 잔액을 직접 대입하는 setter 를 두지 않는다 — 조작을 거치지 않은 변경이 생기면
    /// Host 와 클라이언트의 값이 갈렸을 때 어디서 갈렸는지 추적할 수 없다.
    ///
    /// UnityEngine 을 참조하지 않는다 (D-060).
    /// </summary>
    public sealed class Wallet
    {
        private int gold;
        private int totalInflow;

        public Wallet(int initialGold = 0)
        {
            if (initialGold < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialGold), initialGold, "초기 골드는 음수일 수 없습니다.");
            }

            gold = initialGold;
            totalInflow = initialGold;
        }

        /// <summary>현재 잔액.</summary>
        public int Gold => gold;

        /// <summary>
        /// 이 매치 동안 지갑에 <b>들어온 골드의 누계</b> — 라운드 기본 지급과 획득(킬·상자)을 모두 센다.
        /// 최종 순위 동점 처리 5단계가 이 값을 본다 (S-GD-MNFBV7).
        ///
        /// <para>
        /// <b>쓰거나 이월로 소멸해도 줄지 않는다.</b> "얼마를 벌었나" 를 묻는 값이지
        /// "얼마가 남았나" 가 아니기 때문이다 — 남은 쪽은 4단계의 <see cref="Gold"/> 다.
        /// 두 단계의 방향이 반대인 것은 의도다.
        /// </para>
        /// </summary>
        public int TotalInflow => totalInflow;

        /// <summary>라운드 시작 시 기본 골드를 지급한다 (S-GD-2CFK2Z). 이월된 잔액 위에 더해진다.</summary>
        public void GrantRound(EconomyConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Add(config.RoundGrantGold);
        }

        /// <summary>플레이 중 골드를 얻는다 (킬 보상, 상자 보상 등).</summary>
        public void Earn(int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(amount), amount, "획득 금액은 음수일 수 없습니다. 차감은 TrySpend 를 씁니다.");
            }

            Add(amount);
        }

        /// <summary>
        /// 골드를 쓴다. 잔액이 모자라면 <b>아무것도 바꾸지 않고</b> false 를 돌려준다.
        /// 부분 지출은 없다 — 축복 재추첨처럼 "되거나 안 되거나" 인 조작만 있기 때문이다.
        /// </summary>
        public bool TrySpend(int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(amount), amount, "지출 금액은 음수일 수 없습니다.");
            }

            if (amount > gold)
            {
                return false;
            }

            gold -= amount;
            return true;
        }

        /// <summary>
        /// <b>낸 돈을 되돌린다</b> — 룰렛에서 돌지 않은 판의 베팅 환불 (S-GD-7VKZA5).
        ///
        /// <para>
        /// <b>⚠️ <see cref="Earn"/> 을 쓰면 안 되는 자리다.</b> 그쪽은 <see cref="TotalInflow"/>
        /// 를 함께 올리는데, 그 값이 최종 순위 동점 처리 5단계 <b>"매치 동안 모은 골드"</b> 다
        /// (S-GD-MNFBV7). 환불은 번 것이 아니라 <b>안 쓴 것으로 되돌리는 것</b>이므로
        /// 누계에 넣으면 실제로 벌지 않은 골드가 순위 근거에 섞인다.
        /// <b>컴파일러도 테스트도 못 잡는 종류다</b> — 잔액은 맞고 순위만 조용히 틀린다.
        /// </para>
        ///
        /// <para>
        /// 그래서 이 메서드는 <see cref="Gold"/> 만 올리고 <see cref="TotalInflow"/> 는 건드리지 않는다.
        /// <b>지출을 정확히 되돌릴 때만</b> 쓴다 — 새로 생긴 골드는 <see cref="Earn"/> 이다.
        /// </para>
        /// </summary>
        public void Refund(int amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(amount), amount, "환불 금액은 음수일 수 없습니다.");
            }

            try
            {
                gold = checked(gold + amount);
            }
            catch (OverflowException e)
            {
                throw new InvalidOperationException(
                    $"환불로 골드가 int 범위를 넘었습니다. 현재 {gold}, 환불 {amount}.", e);
            }
        }

        /// <summary>
        /// 라운드를 정산한다 (S-GD-P5RFZM). 남은 골드에 이월 한도를 적용하고 잔액을 그 결과로 만든다.
        /// </summary>
        /// <returns>다음 라운드로 넘어간 금액과 사라진 금액.</returns>
        public CarryOverResult SettleRound(CarryOverLimit limit)
        {
            var result = limit.Apply(gold);
            gold = result.CarriedGold;
            return result;
        }

        /// <summary>새 매치를 시작할 때 지갑을 비운다 (S-GD-RFXF8X). 이월은 매치를 넘지 않는다.</summary>
        public void ResetForNewMatch()
        {
            gold = 0;
            totalInflow = 0;
        }

        private void Add(int amount)
        {
            // 무한 이월 설정에서 장시간 누적될 수 있으므로 오버플로를 조용히 넘기지 않는다.
            try
            {
                totalInflow = checked(totalInflow + amount);
                gold = checked(gold + amount);
            }
            catch (OverflowException e)
            {
                throw new InvalidOperationException(
                    $"골드가 int 범위를 넘었습니다. 현재 {gold}, 추가 {amount}.", e);
            }
        }

        public override string ToString() => $"{gold:N0}G";
    }
}

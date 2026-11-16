using System;

namespace BettingRoyale.Data.Chest
{
    /// <summary>
    /// 상자 하나를 열었을 때 나오는 것.
    ///
    /// <para>
    /// <b>지금 담는 것은 골드 하나뿐이다.</b> 그런데도 <c>int</c> 대신 타입을 만든 이유는
    /// 기획 정본(『게임플레이 SoT』 §5.2)이 상자 내용물을 *"소모품, 장신구, 골드"* 로 적어
    /// 두었기 때문이다 — 사용자(조영운) 확인, 2026-09-04:
    /// *"당장은 골드가 맞고, 언제든지 보상 목록을 확장할 수 있도록 설계해서 코드를 짰으면 좋겠어."*
    /// </para>
    ///
    /// <para>
    /// <b>확장하는 방법</b> — 이 구조체에 필드를 하나 더하고 생성자 오버로드를 추가한다.
    /// <see cref="ChestFieldConfig"/> 와 <see cref="ChestRewardEarned"/> 는 이 타입을 통째로
    /// 나르므로 <b>시그니처가 안 바뀌고</b>, 읽는 쪽도 <c>reward.Gold</c> 를 그대로 쓴다.
    /// 즉 확장 비용이 이 파일 하나로 갇힌다.
    /// </para>
    ///
    /// <para>
    /// <b>왜 보상 종류 <c>enum</c> + 항목 배열이 아닌가</b> — 오늘 항목이 하나인데 enum·항목
    /// 구조체·배열·지급 루프가 한꺼번에 생기고, 무엇보다 <b>아이템 종류표와 확률표가 아직 없다</b>
    /// (S-GD-GQNA2B 가 *"값이 아예 없다"* 로 분류했다). 값을 모르는 채 enum 순서를 먼저 잠그면
    /// 그것이 아무도 승인하지 않은 스펙이 된다. 배열은 매 틱 할당도 만든다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b><see cref="ChestFieldConfig"/> 와 같은 이유로 <c>Default</c> 를 두지 않는다.</b>
    /// 보상량의 정본은 데이터 파이프라인으로 들어온다 (D-041, S-GD-GQNA2B).
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public readonly struct ChestReward
    {
        /// <summary>개봉 성공 시 주는 골드. ⚠️ 값 자체는 스펙 미정이다.</summary>
        public readonly int Gold;

        /// <param name="gold">
        /// 개봉 성공 시 주는 골드. <b>0 도 유효하다</b> — 골드가 없는 상자를 표현할 수 있어야 한다.
        /// </param>
        public ChestReward(int gold)
        {
            if (gold < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(gold), gold, "보상은 음수일 수 없습니다.");
            }

            Gold = gold;
        }

        /// <summary>골드만 주는 보상. 지금은 이것이 유일한 형태다.</summary>
        public static ChestReward OfGold(int gold) => new ChestReward(gold);

        /// <summary>아무것도 주지 않는다. 아이템이 생기면 이 판정도 그때 함께 넓힌다.</summary>
        public bool IsEmpty => Gold == 0;

        public override string ToString() => $"{Gold:N0}G";
    }
}

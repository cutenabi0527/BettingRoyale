using System.Collections.Generic;

namespace BettingRoyale.Data.Shop
{
    /// <summary>
    /// 지속형 소모품 효과가 <b>지금 무엇이 켜져 있고 언제까지인가</b> (S-GD-YMKKDJ · S-GD-DDXPX9 · S-GD-G3QJCW).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>효과는 "건 아이템 × 효과 칸" 마다 따로 산다.</b> 같은 아이템을 다시 쓰면 그 효과의 값과 시간을 새로 채운다 —
    /// 더하지 않는다(신속 물약 두 번 = +10% 에 시간만 갱신). <b>다른 아이템이면 덮어쓰지 않는다</b> — 각자 제 시간만큼 살고
    /// 값은 더한다(질주 +30% 도중 신속 +10% → +40%, 신속이 먼저 끝나면 +30%). 상자 과잉 보상도 근원 하나다.
    /// </para>
    /// <para>
    /// <b>효과 칸</b>: 이동 속도 · 공격 속도 · 주는 피해 · 받는 피해 · 대시 재충전 · 아이템 보호막 · 무적 ·
    /// 지속 회복 · <b>방어력</b> · <b>공격력</b>. 칸끼리도 독립이라 투지(+피해)와 광란(+공속)은 함께 켜진다.
    /// </para>
    /// <para>
    /// ⚠️ <b>방어력·공격력 칸만 퍼센트가 아니라 고정 수다.</b> 프로틴 쉐이크의 +7·+3 은 수호의 부적·힘의 반지와
    /// 같은 단위라 피해 계산에 그대로 더하고 뺀다 — 배율 칸처럼 1 을 항등원으로 쓰지 않고 <b>0</b> 이 항등원이다.
    /// </para>
    /// <para>
    /// <b>시간을 밖에서 받는다.</b> <c>UnityEngine</c> 을 모르는 계층이라(D-060) 부르는 쪽이 지금 시각을 넣는다.
    /// 만료는 <see cref="Tick"/> 이 처리하므로, 조회 값은 마지막 <c>Tick</c> 기준이다.
    /// </para>
    /// <para>
    /// <b>스탯 시트를 거치지 않는다.</b> 소모품은 마신 본인에게만 그 자리에서 걸린다. 이동·공격 컴포넌트는
    /// 배율을 받아 <b>축복 합산에 더한다</b>(합연산, S-GD-EM82G5) — 축복 쪽 칸과 달라야 매 프레임 덮어쓰기를 피한다.
    /// </para>
    /// </remarks>
    public sealed class ConsumableBuffState
    {
        private struct Timed
        {
            public ConsumableBuffChannel Channel;

            /// <summary>효과를 건 아이템 번호. 0 이면 모름, 음수는 아이템이 아닌 근원(상자 과잉 보상).</summary>
            public int SourceItemId;

            public float Value;
            public float ExpiresAt;

            /// <summary>마지막으로 걸린 효과의 전체 지속 시간(초). HUD 가 남은 비율을 그린다.</summary>
            public float Duration;
        }

        /// <summary>효과 칸 수. <see cref="ConsumableBuffChannel"/> 의 값 개수와 같다.</summary>
        public const int ChannelCount = 10;

        /// <summary>켜진 지속형 효과. (칸, 근원) 마다 하나다. 켜진 순서대로 쌓인다.</summary>
        private readonly List<Timed> timed = new List<Timed>(ChannelCount);

        private bool regenerationActive;
        private float regenerationPerTick;
        private float nextRegenerationAt;
        private int regenerationTicksLeft;
        private float regenerationDuration;
        private int regenerationSourceItemId;

        // ── 조회 ────────────────────────────────────────────────────────────

        /// <summary>지금 이동 속도 버프가 살아 있는가.</summary>
        public bool IsMoveSpeedActive => IsActive(ConsumableBuffChannel.MoveSpeed);

        /// <summary>이동 컴포넌트에 넣을 <b>배율</b> — 켜진 효과를 모두 더한다. 없으면 1(항등원).</summary>
        public float MoveSpeedMultiplier => 1f + Sum(ConsumableBuffChannel.MoveSpeed);

        /// <summary>공격 컴포넌트에 넣을 <b>배율</b> — 켜진 효과를 모두 더한다. 없으면 1.</summary>
        public float AttackSpeedMultiplier => 1f + Sum(ConsumableBuffChannel.AttackSpeed);

        /// <summary>공격을 시작할 때 붙일 피해 <b>증감분</b>(0.20 = +20%). 없으면 0.</summary>
        public float DamageBonus => Sum(ConsumableBuffChannel.DamageBonus);

        /// <summary>
        /// 받는 피해 배율(−25% 면 0.75). 없으면 1. 감소분을 모두 더한다. 쓰는 쪽이 <c>배율 − 1</c> 을 받는 피해 합산에
        /// 더한다 (S-GD-EM82G5).
        /// </summary>
        public float DamageTakenMultiplier
        {
            get
            {
                float multiplier = 1f - Sum(ConsumableBuffChannel.DamageTaken);
                return multiplier > 0f ? multiplier : 0f;
            }
        }

        /// <summary>
        /// 대시 재충전 시간에 곱할 배율(절반이면 0.5). 없으면 1. 여럿이면 증감분(<c>배율 − 1</c>)을 더한다.
        /// </summary>
        public float DashRechargeMultiplier
        {
            get
            {
                float multiplier = 1f;
                for (int i = 0; i < timed.Count; i++)
                {
                    if (timed[i].Channel == ConsumableBuffChannel.DashRecharge)
                    {
                        multiplier += timed[i].Value - 1f;
                    }
                }

                return multiplier > 0f ? multiplier : 0f;
            }
        }

        /// <summary>남은 아이템 보호막 양 — 근원마다 따로 산 보호막의 합. 없으면 0.</summary>
        public float ShieldAmount => Sum(ConsumableBuffChannel.Shield);

        /// <summary>지금 무적인가 — 받는 피해가 0 이다.</summary>
        public bool IsInvulnerable => IsActive(ConsumableBuffChannel.Invulnerable);

        /// <summary>지속 회복이 남아 있는가.</summary>
        public bool IsRegenerating => regenerationActive;

        /// <summary>
        /// 맞는 쪽 방어력에 더할 <b>고정</b> 증가량(+7 이면 7). 없으면 0.
        /// ⚠️ 배율이 아니다 — 항등원은 1 이 아니라 0 이다.
        /// </summary>
        public float DefenseBonus => Sum(ConsumableBuffChannel.Defense);

        /// <summary>
        /// 공격하는 쪽 공격력에 더할 <b>고정</b> 증가량(+3 이면 3). 없으면 0.
        /// ⚠️ 주는 피해 <see cref="DamageBonus"/>(퍼센트)와 다른 칸이다 — 공격력은 곱하기 전의 기본값에 더한다.
        /// </summary>
        public float AttackPowerBonus => Sum(ConsumableBuffChannel.AttackPower);

        /// <summary>이동 속도 버프가 모두 끝나기까지 남은 시간(초). 없으면 0.</summary>
        public float RemainingSeconds(float now) => Remaining(ConsumableBuffChannel.MoveSpeed, now);

        // ── 변경 ────────────────────────────────────────────────────────────

        /// <summary>효과 하나를 건다. 건 아이템은 모름(0)으로 둔다.</summary>
        /// <returns>버프 칸이 바뀌었으면 <c>true</c>. 즉시형(회복·대시 충전·골드 등)이면 <c>false</c>.</returns>
        public bool Apply(in ConsumableEffect effect, float now) => Apply(effect, now, 0);

        /// <summary>
        /// 효과 하나를 건다. <b>같은 아이템의 같은 칸</b>이 이미 켜져 있으면 값과 시간을 새로 채우고, 아니면 따로 하나를 더 켠다.
        /// </summary>
        /// <param name="sourceItemId">
        /// 건 아이템 번호. 0 은 모름, 음수는 아이템이 아닌 근원(상자 과잉 보상 <see cref="ShopItemEffectCatalog.ChestOverflowSourceId"/>).
        /// </param>
        /// <returns>버프 칸이 바뀌었으면 <c>true</c>. 즉시형(회복·대시 충전·골드 등)이면 <c>false</c>.</returns>
        public bool Apply(in ConsumableEffect effect, float now, int sourceItemId) => Apply(effect, now, sourceItemId, 0f);

        /// <param name="maxHealth">
        /// 사용 시점의 최종 최대 체력. 비율 지속 회복(재생 물약 40%)의 <b>총량을 이 순간 스냅샷</b>해 지속 시간에 나눈다 —
        /// 지속 중 최대 체력이 바뀌어도 남은 총량이 부풀지 않는다 (S-GD-Q75D75 구현 제안).
        /// </param>
        public bool Apply(in ConsumableEffect effect, float now, int sourceItemId, float maxHealth)
        {
            switch (effect.Kind)
            {
                case ConsumableEffectKind.TimedMoveSpeed:
                    Set(ConsumableBuffChannel.MoveSpeed, effect, now, sourceItemId);
                    return true;
                case ConsumableEffectKind.TimedAttackSpeed:
                    Set(ConsumableBuffChannel.AttackSpeed, effect, now, sourceItemId);
                    return true;
                case ConsumableEffectKind.TimedDamageBonus:
                    Set(ConsumableBuffChannel.DamageBonus, effect, now, sourceItemId);
                    return true;
                case ConsumableEffectKind.TimedDamageTaken:
                    Set(ConsumableBuffChannel.DamageTaken, effect, now, sourceItemId);
                    return true;
                case ConsumableEffectKind.TimedDashRecharge:
                    Set(ConsumableBuffChannel.DashRecharge, effect, now, sourceItemId);
                    return true;
                case ConsumableEffectKind.Shield:
                    Set(ConsumableBuffChannel.Shield, effect, now, sourceItemId);
                    return true;
                case ConsumableEffectKind.Invulnerable:
                    Set(ConsumableBuffChannel.Invulnerable, effect, now, sourceItemId);
                    return true;
                case ConsumableEffectKind.TimedDefense:
                    Set(ConsumableBuffChannel.Defense, effect, now, sourceItemId);
                    return true;
                case ConsumableEffectKind.TimedAttackPower:
                    Set(ConsumableBuffChannel.AttackPower, effect, now, sourceItemId);
                    return true;
                case ConsumableEffectKind.Regeneration:
                    // 1초마다 한 번, 지속 시간(초)만큼. 8초면 8번. 다시 받으면 처음부터 다시 센다.
                    // 지속 회복을 거는 것은 재생 물약 하나뿐이라 근원을 나누지 않는다.
                    regenerationTicksLeft = (int)(effect.DurationSeconds + 0.5f);
                    // 총량 = 고정이면 초당 × 초, 비율이면 최대 체력 × 비율. 틱마다 총량/틱수 — 소수는 그대로 들고 간다(내림 없음).
                    float regenerationTotal = effect.ResolveHealAmount(maxHealth);
                    regenerationActive = regenerationTicksLeft > 0 && regenerationTotal > 0f;
                    regenerationPerTick = regenerationActive ? regenerationTotal / regenerationTicksLeft : 0f;
                    nextRegenerationAt = now + 1f;
                    regenerationDuration = regenerationTicksLeft;
                    regenerationSourceItemId = sourceItemId;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 시간이 지난 효과를 끈다. <b>이번 호출에서 하나라도 꺼졌을 때만</b> <c>true</c> —
        /// 부르는 쪽이 그때 한 번만 배율을 다시 밀면 된다.
        /// </summary>
        /// <remarks>지속 회복은 여기서 끄지 않는다 — 마지막 회복을 놓치지 않도록 <see cref="TakeRegenerationHeal"/> 이 센다.</remarks>
        public bool Tick(float now)
        {
            bool changed = false;
            for (int i = timed.Count - 1; i >= 0; i--)
            {
                if (now >= timed[i].ExpiresAt)
                {
                    timed.RemoveAt(i);
                    changed = true;
                }
            }

            return changed;
        }

        /// <summary>이번에 줘야 할 지속 회복 양. 여러 초가 밀렸으면 한꺼번에 준다.</summary>
        public float TakeRegenerationHeal(float now)
        {
            float heal = 0f;
            while (regenerationActive && now >= nextRegenerationAt)
            {
                heal += regenerationPerTick;
                nextRegenerationAt += 1f;
                regenerationTicksLeft--;
                if (regenerationTicksLeft <= 0)
                {
                    regenerationActive = false;
                }
            }

            return heal;
        }

        /// <summary>
        /// 들어온 피해를 아이템 보호막이 먼저 막는다. 막고 <b>남은</b> 피해를 돌려준다.
        /// 보호막이 여럿이면 <b>먼저 끝나는 것부터</b> 깎는다 — 곧 사라질 보호막을 남겨 두면 그만큼 손해다.
        /// </summary>
        public float AbsorbWithShield(float damage)
        {
            while (damage > 0f)
            {
                int soonest = -1;
                for (int i = 0; i < timed.Count; i++)
                {
                    if (timed[i].Channel == ConsumableBuffChannel.Shield
                        && (soonest < 0 || timed[i].ExpiresAt < timed[soonest].ExpiresAt))
                    {
                        soonest = i;
                    }
                }

                if (soonest < 0)
                {
                    break;
                }

                Timed shield = timed[soonest];
                float absorbed = damage < shield.Value ? damage : shield.Value;
                shield.Value -= absorbed;
                damage -= absorbed;
                if (shield.Value <= 0f)
                {
                    timed.RemoveAt(soonest);
                }
                else
                {
                    timed[soonest] = shield;
                }
            }

            return damage;
        }

        /// <summary>라운드·세션이 끝났다. 모든 효과를 지운다 (S-GD-AN554W).</summary>
        public void Clear()
        {
            timed.Clear();
            regenerationActive = false;
            regenerationPerTick = 0f;
            nextRegenerationAt = 0f;
            regenerationTicksLeft = 0;
            regenerationDuration = 0f;
            regenerationSourceItemId = 0;
        }

        // ── 표시용 읽기 창 — 상태를 바꾸지 않는다 ────────────────────────────────

        /// <summary>이 칸에 켜진 효과가 하나라도 있는가. 만료는 마지막 <see cref="Tick"/> 기준이다.</summary>
        public bool IsActive(ConsumableBuffChannel channel)
        {
            if (channel == ConsumableBuffChannel.Regeneration)
            {
                return regenerationActive;
            }

            for (int i = 0; i < timed.Count; i++)
            {
                if (timed[i].Channel == channel)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 이 칸의 효과가 <b>모두</b> 끝나기까지 남은 시간(초). 없으면 0. 지속 회복은 남은 틱이 모두 나올 때까지의 시간이다.
        /// </summary>
        public float Remaining(ConsumableBuffChannel channel, float now)
        {
            int longest = Longest(channel);
            return longest == RegenerationIndex ? RegenerationRemaining(now)
                : longest >= 0 ? Max0(timed[longest].ExpiresAt - now) : 0f;
        }

        /// <summary>이 칸에서 가장 늦게 끝나는 효과의 전체 지속 시간(초). 없으면 0.</summary>
        public float Duration(ConsumableBuffChannel channel)
        {
            int longest = Longest(channel);
            return longest == RegenerationIndex ? regenerationDuration
                : longest >= 0 ? timed[longest].Duration : 0f;
        }

        /// <summary>
        /// 이 칸에서 가장 늦게 끝나는 효과를 건 아이템 번호. 없거나 모르면 0, 상자 과잉 보상이면
        /// <see cref="ShopItemEffectCatalog.ChestOverflowSourceId"/>.
        /// </summary>
        public int Source(ConsumableBuffChannel channel)
        {
            int longest = Longest(channel);
            return longest == RegenerationIndex ? regenerationSourceItemId
                : longest >= 0 ? timed[longest].SourceItemId : 0;
        }

        /// <summary>켜진 효과 수(지속 회복 포함). HUD 가 근원마다 묶을 때 하나씩 읽는다.</summary>
        public int EntryCount => timed.Count + (regenerationActive ? 1 : 0);

        /// <summary><paramref name="index"/> 번째 효과의 칸. 지속 회복은 맨 뒤에 온다.</summary>
        public ConsumableBuffChannel EntryChannel(int index) =>
            index < timed.Count ? timed[index].Channel : ConsumableBuffChannel.Regeneration;

        /// <summary><paramref name="index"/> 번째 효과를 건 아이템 번호.</summary>
        public int EntrySource(int index) => index < timed.Count ? timed[index].SourceItemId : regenerationSourceItemId;

        /// <summary><paramref name="index"/> 번째 효과의 남은 시간(초).</summary>
        public float EntryRemaining(int index, float now) =>
            index < timed.Count ? Max0(timed[index].ExpiresAt - now) : RegenerationRemaining(now);

        /// <summary><paramref name="index"/> 번째 효과의 전체 지속 시간(초).</summary>
        public float EntryDuration(int index) => index < timed.Count ? timed[index].Duration : regenerationDuration;

        // ── 내부 ────────────────────────────────────────────────────────────

        /// <summary><see cref="Longest"/> 가 지속 회복을 가리킬 때의 값.</summary>
        private const int RegenerationIndex = int.MaxValue;

        private float Sum(ConsumableBuffChannel channel)
        {
            float sum = 0f;
            for (int i = 0; i < timed.Count; i++)
            {
                if (timed[i].Channel == channel)
                {
                    sum += timed[i].Value;
                }
            }

            return sum;
        }

        /// <summary>이 칸에서 가장 늦게 끝나는 효과의 자리. 없으면 −1, 지속 회복이면 <see cref="RegenerationIndex"/>.</summary>
        private int Longest(ConsumableBuffChannel channel)
        {
            if (channel == ConsumableBuffChannel.Regeneration)
            {
                return regenerationActive ? RegenerationIndex : -1;
            }

            int longest = -1;
            for (int i = 0; i < timed.Count; i++)
            {
                if (timed[i].Channel == channel && (longest < 0 || timed[i].ExpiresAt > timed[longest].ExpiresAt))
                {
                    longest = i;
                }
            }

            return longest;
        }

        private float RegenerationRemaining(float now) => regenerationActive
            ? Max0(nextRegenerationAt - now) + (regenerationTicksLeft - 1)
            : 0f;

        private void Set(ConsumableBuffChannel channel, in ConsumableEffect effect, float now, int sourceItemId)
        {
            var entry = new Timed
            {
                Channel = channel,
                SourceItemId = sourceItemId,
                Value = effect.Value,
                ExpiresAt = now + effect.DurationSeconds,
                Duration = effect.DurationSeconds,
            };

            // ⚠️ 같은 아이템의 같은 칸만 새로 채운다(더하지 않는다 — 두 번 마셔도 두 배가 되면 안 된다).
            //    다른 아이템은 덮어쓰지 않고 따로 켠다 (S-GD-G3QJCW).
            for (int i = 0; i < timed.Count; i++)
            {
                if (timed[i].Channel == channel && timed[i].SourceItemId == sourceItemId)
                {
                    timed[i] = entry;
                    return;
                }
            }

            timed.Add(entry);
        }

        private static float Max0(float value) => value > 0f ? value : 0f;
    }
}

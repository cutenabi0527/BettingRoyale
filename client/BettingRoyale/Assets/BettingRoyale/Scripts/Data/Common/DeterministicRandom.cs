using System;

namespace BettingRoyale.Data.Common
{
    /// <summary>
    /// 시드가 같으면 어느 기기에서든 같은 수열을 내는 난수원.
    ///
    /// <b>왜 System.Random 을 쓰지 않는가</b> — .NET 구현마다 알고리즘이 다르다. Unity(Mono)와
    /// dotnet 테스트(.NET 8)에서 같은 시드가 다른 수열을 낸다. 그러면 (1) 테스트가 Unity 로
    /// 옮겨갈 때 깨지고 (2) 2PC 검증(Jira S15P21A606-31 "2PC 에서 동일하다")의 근거가 사라진다.
    /// 그래서 알고리즘을 여기에 직접 고정한다 — xorshift32.
    ///
    /// <b>보안용이 아니다.</b> 예측 가능하며, 축복 후보·상자 보상처럼 Host 가 뽑아 복제하는
    /// 값에만 쓴다. 클라이언트가 자기 몫을 스스로 뽑는 데 쓰지 않는다.
    ///
    /// UnityEngine 을 참조하지 않는다 (D-060).
    /// </summary>
    public sealed class DeterministicRandom
    {
        private uint state;

        /// <param name="seed">0 은 xorshift 가 고정점이라 내부에서 대체값으로 바꾼다.</param>
        public DeterministicRandom(int seed)
        {
            state = seed == 0 ? 0x9E3779B9u : unchecked((uint)seed);
        }

        /// <summary>다음 난수. 전체 uint 범위.</summary>
        public uint NextUInt()
        {
            // xorshift32 — Marsaglia. 주기 2^32-1.
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        /// <summary>[0, exclusiveMax) 범위의 정수.</summary>
        public int Next(int exclusiveMax)
        {
            if (exclusiveMax <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(exclusiveMax), exclusiveMax, "상한은 1 이상이어야 합니다.");
            }

            // 모듈로 편향을 제거한다. 상한이 작을 때는 사실상 한 번에 통과한다.
            var limit = uint.MaxValue - (uint.MaxValue % (uint)exclusiveMax);
            uint value;
            do
            {
                value = NextUInt();
            }
            while (value >= limit);

            return (int)(value % (uint)exclusiveMax);
        }

        /// <summary>리스트를 제자리에서 섞는다 (Fisher-Yates). 후보를 뽑기 전에 쓴다.</summary>
        public void Shuffle<T>(System.Collections.Generic.IList<T> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            for (var i = items.Count - 1; i > 0; i--)
            {
                var j = Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }
    }
}

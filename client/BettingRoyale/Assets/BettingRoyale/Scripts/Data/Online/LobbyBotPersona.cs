using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Common;

namespace BettingRoyale.Data.Online
{
    /// <summary>
    /// 대기방 봇의 준비 성향 — 준비 라운드에 골드를 어디에 쓰는가(S15P21A606-326, S-GD-9CZKVT).
    /// </summary>
    public enum LobbyBotPrepStyle
    {
        /// <summary>공격형 — 무기 강화에 크게 쓰고 장신구를 산다. 룰렛은 거의 하지 않는다.</summary>
        Aggressive = 0,

        /// <summary>안정형 — 물약으로 소모품 칸을 채우고 성공률 높은 강화만 한다. 일부를 남겨 이월한다.</summary>
        Steady = 1,

        /// <summary>도박형 — 룰렛에 적극적으로 걸고 싼 강화만 한다.</summary>
        Gambler = 2,
    }

    /// <summary>
    /// 대기방 봇의 전투 유형 — 무기마다 세 가지(S-GD-9CZKVT). 1단계(S15P21A606-326)는 축복 고르기에만 쓰고,
    /// 전투 행동은 후속 단계에서 이 값을 읽는다.
    /// </summary>
    public enum LobbyBotCombatStyle
    {
        /// <summary>무기를 아직 모른다. 축복 점수에서 전투 유형 가중치를 빼고 계산한다.</summary>
        Unknown = 0,

        /// <summary>검 돌격형 — 발견 즉시 대시로 붙고 끝까지 싸운다.</summary>
        SwordRush = 1,

        /// <summary>검 신중형 — 사거리 경계에서 기다렸다가 들어오면 친다. 체력이 낮으면 물러난다.</summary>
        SwordCautious = 2,

        /// <summary>검 기회형 — 체력 낮은 적, 다른 적과 싸우는 적을 노린다.</summary>
        SwordOpportunist = 3,

        /// <summary>활 카이팅형 — 사거리 끝을 유지하며 다가오면 뒤로 빠진다.</summary>
        BowKite = 4,

        /// <summary>활 저격형 — 멀리 자리 잡고 적게 움직인다.</summary>
        BowSniper = 5,

        /// <summary>활 압박형 — 중거리까지 들어가 연사한다.</summary>
        BowPressure = 6,
    }

    /// <summary>봇 한 기의 성향(준비 성향 × 전투 유형). 매치마다 시드를 고정해 배정한다.</summary>
    public readonly struct LobbyBotPersona
    {
        public readonly LobbyBotPrepStyle Prep;
        public readonly LobbyBotCombatStyle Combat;

        public LobbyBotPersona(LobbyBotPrepStyle prep, LobbyBotCombatStyle combat)
        {
            Prep = prep;
            Combat = combat;
        }

        public override string ToString() => $"{Prep}/{Combat}";

        /// <summary>
        /// 매치 id·참가자 키로 성향을 정한다. 같은 매치의 같은 봇은 언제 물어도 같은 답이다 — Host 가 도중에
        /// 다시 계산해도 봇이 성격을 바꾸지 않는다. 무기를 모르면 전투 유형은 <see cref="LobbyBotCombatStyle.Unknown"/> 이다.
        /// </summary>
        public static LobbyBotPersona Assign(string matchId, int participantId, WeaponType weapon)
        {
            var random = new DeterministicRandom(Seed(matchId, participantId));
            var prep = (LobbyBotPrepStyle)random.Next(3);
            int pick = random.Next(3);
            LobbyBotCombatStyle combat = weapon switch
            {
                WeaponType.Sword => (LobbyBotCombatStyle)((int)LobbyBotCombatStyle.SwordRush + pick),
                WeaponType.Bow => (LobbyBotCombatStyle)((int)LobbyBotCombatStyle.BowKite + pick),
                _ => LobbyBotCombatStyle.Unknown,
            };
            return new LobbyBotPersona(prep, combat);
        }

        /// <summary>
        /// 결정 난수의 시드. <c>string.GetHashCode</c> 는 프로세스마다 달라질 수 있어 쓰지 않는다(FNV-1a).
        /// </summary>
        public static int Seed(string matchId, int participantId)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string text = matchId ?? string.Empty;
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= 16777619u;
                }

                hash ^= (uint)participantId;
                hash *= 16777619u;
                return (int)(hash == 0 ? 1u : hash);
            }
        }
    }
}

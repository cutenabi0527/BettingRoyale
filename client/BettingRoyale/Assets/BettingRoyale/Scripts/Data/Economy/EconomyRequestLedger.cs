using System.Collections.Generic;

namespace BettingRoyale.Data.Economy
{
    /// <summary>요청 하나를 살펴본 결과 (Jira S15P21A606-26).</summary>
    public enum EconomyRequestVerdict
    {
        /// <summary>처음 보는 요청이다. 도메인에 적용한다.</summary>
        Fresh = 0,

        /// <summary>
        /// 이미 <b>처리한</b> 요청이다. 성공했는지와 무관하다 —
        /// 티켓이 "이미 <b>처리한</b> requestId 를 다시 적용하지 않고" 라고 쓴다.
        ///
        /// <b>성공만 기록하면 조용히 새는 경로가 생긴다</b>: 골드가 모자라 거부된 요청이 재전송될 때
        /// 다시 판정하게 되고, 그사이 상자 보상이 들어왔으면
        /// <b>사용자가 이미 "골드 부족" 을 보고 손을 뗀 클릭이 나중에 성공한다.</b>
        /// 그래서 거부도 "처리했다" 로 기록한다.
        /// </summary>
        Duplicate = 1,

        /// <summary>
        /// 지난 준비 단계의 요청이다 — 이전 라운드이거나 이전 매치다.
        /// <see cref="EconomyRequestLedger.Epoch"/> 가 둘을 한 값으로 가른다.
        /// </summary>
        StaleEpoch = 2,

        /// <summary>순번이 1 미만이다. 보내는 쪽의 결함이거나 조작이다.</summary>
        Malformed = 3,

        /// <summary>
        /// 준비 단계가 열려 있지 않다. 전투 중이거나 매치가 끝났다.
        ///
        /// <b>원장은 이 값을 절대 돌려주지 않는다</b> — 단계를 모르기 때문이다.
        /// <see cref="EconomyRequestGate"/> 만 낸다.
        /// </summary>
        PreparationClosed = 4,
    }

    /// <summary>
    /// 클라이언트가 Host 에 보내는 <b>요청 표</b> — Jira S15P21A606-26 의 <c>requestId</c> 다.
    ///
    /// <para>
    /// 이 구조체에 <b>신원 칸이 없다. 일부러 없다.</b> 티켓은 "매치·라운드·플레이어·순번을 구분할 수 있는
    /// requestId" 를 요구하지만 <b>플레이어는 회선을 건너오지 않는다</b> —
    /// Host 가 <c>Object.InputAuthority.PlayerId</c> 에서 채운다.
    /// 보내는 쪽이 적을 수 있는 칸으로 두면 규약 §5 가 없앤
    /// "남의 id 를 적으면 남의 골드를 쓸 수 있던" 구조가 <b>이름만 바꿔 되살아난다.</b>
    /// 이 저장소의 <c>PlayerInfo.RPC_SetReady</c> 가 이미 같은 모양이다.
    /// </para>
    ///
    /// <para>
    /// <b>매치와 라운드도 값으로 싣지 않는다 — <see cref="Epoch"/> 하나가 둘을 대신한다.</b>
    /// 준비 단계가 열릴 때마다 1 오르고 매치가 바뀌어도 되돌아가지 않는 카운터라,
    /// 정수 하나를 비교하면 "지난 라운드의 요청" 과 "지난 매치의 요청" 이 함께 걸린다.
    /// 티켓 문구는 <b>"구분할 수 있는"</b> 이지 "네 값을 다 실어라" 가 아니다.
    /// </para>
    ///
    /// <para>
    /// <b>이것은 <c>MatchId</c> 가 아니다</b>(S15P21A606-25). 세션 밖으로 나가지 않고 서버에도 가지 않는다.
    /// </para>
    /// </summary>
    public readonly struct EconomyRequestTicket
    {
        /// <summary>요청이 속한 준비 단계. <see cref="EconomyRequestLedger.Epoch"/> 와 비교한다.</summary>
        public readonly int Epoch;

        /// <summary>같은 준비 단계 안에서 1부터 오르는 순번. 명령 종류와 무관한 하나짜리 카운터다.</summary>
        public readonly int Sequence;

        public EconomyRequestTicket(int epoch, int sequence)
        {
            Epoch = epoch;
            Sequence = sequence;
        }

        public override string ToString() => $"e{Epoch}#{Sequence}";
    }

    /// <summary>
    /// <b>같은 요청이 두 번 적용되는 것을 막는 원장</b> (Jira S15P21A606-26 완료 조건 1·2).
    ///
    /// <para>
    /// 재전송은 오류가 아니라 정상이다 — 재접속·RPC 재배달·버튼 연타에서 늘 생긴다.
    /// <see cref="MatchRoundTally"/> 가 라운드 단위로 같은 일을 하지만,
    /// <b>재추첨·선택·잠금은 한 라운드 안에서 여러 번 일어나므로</b> 그 방식이 그대로는 안 된다.
    /// </para>
    ///
    /// <para>
    /// <b>왜 <c>HashSet</c> 하나로 안 되는가</b> — 잠금은 무료이고 <b>토글 횟수에 상한이 없다</b>
    /// (동시에 2개까지라는 제한이 있을 뿐이다). 본 순번을 전부 기억하면 버튼 연타 하나로
    /// Host 메모리가 계속 는다. Host 는 서버가 아니라 <b>플레이어 PC</b> 다(D-030).
    /// 그래서 플레이어마다 <b>"이 값 이하는 전부 처리됨" 정수 하나 + 아직 안 온 순번(구멍) 집합</b>만 든다.
    /// </para>
    ///
    /// <para>
    /// <c>UnityEngine</c> 을 참조하지 않는다 (D-060). 순수 C# 이라 Unity 없이 테스트할 수 있다.
    /// </para>
    /// </summary>
    public sealed class EconomyRequestLedger
    {
        /// <summary>
        /// 한 플레이어가 기억하는 구멍의 최대 개수.
        ///
        /// <b>이 값은 에이전트가 임의로 정했다</b>(S-CLI-GNPHGJ). RPC 순서가 지켜지면 구멍은 늘 0이고,
        /// 안 지켜져도 어긋남이 이 크기를 넘는 경우는 관측된 적이 없다.
        /// 넘치면 <b>가장 오래된 구멍을 버린다</b> — 그 요청은 나중에 와도
        /// <see cref="EconomyRequestVerdict.Duplicate"/> 로 처리된다.
        /// <b>안전한 방향이다</b>: 잃어버린 요청이 무시될 뿐, 두 번 적용되지는 않는다.
        /// </summary>
        public const int MaxTrackedGaps = 64;

        private sealed class Row
        {
            /// <summary>이 순번까지는 전부 처리했다(구멍 제외).</summary>
            public int Watermark { get; set; }

            /// <summary>워터마크 아래인데 아직 안 온 순번.</summary>
            public HashSet<int> Gaps { get; } = new();
        }

        private readonly Dictionary<int, Row> rows = new();

        /// <summary>
        /// 현재 준비 단계 번호. <see cref="OpenPreparation"/> 이 올린다.
        /// <b>0 은 "준비 단계가 열린 적 없음" 이다</b> — 그래서 티켓의 <c>Epoch</c> 는 1부터다.
        /// </summary>
        public int Epoch { get; private set; }

        /// <summary>추적 중인 플레이어 수. 테스트가 메모리 증가를 확인하는 데 쓴다.</summary>
        public int TrackedPlayerCount => rows.Count;

        /// <summary>
        /// 준비 단계를 연다. <see cref="Epoch"/> 를 올리고 <b>이전 단계의 원장을 전부 버린다.</b>
        ///
        /// 버려도 안전한 이유: 지난 단계의 요청은 <see cref="EconomyRequestVerdict.StaleEpoch"/> 로
        /// <b>순번을 보기 전에</b> 걸린다. 그래서 매치가 길어져도 메모리가 쌓이지 않는다.
        /// </summary>
        public int OpenPreparation()
        {
            rows.Clear();
            Epoch++;
            return Epoch;
        }

        /// <summary>
        /// 요청을 살펴보고 <b>처리해도 되는지</b> 답한다.
        ///
        /// <para>
        /// <b>이 메서드는 상태를 바꾼다</b> — <see cref="EconomyRequestVerdict.Fresh"/> 를 돌려준 순간
        /// 그 순번은 "처리했음" 으로 기록된다. 같은 티켓으로 다시 부르면 <c>Duplicate</c> 다.
        /// 그래서 <b>부른 쪽은 반드시 결과를 처리해야 한다</b> — 결과를 버리면 그 요청은 영영 적용되지 않는다.
        /// </para>
        ///
        /// <para>
        /// 성공·거부를 구분하지 않고 <b>"처리했다" 만 기록한다.</b> 이유는
        /// <see cref="EconomyRequestVerdict.Duplicate"/> 주석에 있다.
        /// </para>
        /// </summary>
        /// <param name="playerId">Host 가 RPC 입력 권한에서 꺼낸 값. <b>페이로드에서 온 값이면 안 된다.</b></param>
        /// <param name="ticket">클라이언트가 보낸 요청 표.</param>
        public EconomyRequestVerdict Accept(int playerId, EconomyRequestTicket ticket)
        {
            if (ticket.Sequence < 1)
            {
                return EconomyRequestVerdict.Malformed;
            }

            // Epoch 0 은 "준비 단계가 열린 적 없음" 이다. 그 상태에서 Epoch 0 짜리 요청이 오면
            // 단순 비교로는 통과해버린다 — 준비 전에 도착한 요청이 적용되는 경로다.
            if (Epoch < 1 || ticket.Epoch != Epoch)
            {
                return EconomyRequestVerdict.StaleEpoch;
            }

            if (!rows.TryGetValue(playerId, out var row))
            {
                row = new Row();
                rows[playerId] = row;
            }

            if (ticket.Sequence > row.Watermark)
            {
                // 중간을 건너뛰었으면 그 사이는 "아직 안 온 것" 으로 남긴다.
                //
                // ⚠️ 반복 횟수를 회선에서 온 값으로 정하면 안 된다. ticket.Sequence 는 클라이언트가
                //    보낸 값이라 int.MaxValue 도 올 수 있고, 그러면 Host 가 20억 번을 돈다 —
                //    Host 는 서버가 아니라 플레이어 PC 이므로(D-030) 그동안 게임이 멈춘다.
                //    어차피 MaxTrackedGaps 를 넘는 구멍은 오래된 것부터 버려지므로,
                //    처음부터 마지막 MaxTrackedGaps 개만 만든다. 결과는 같고 반복은 상한 안에 있다.
                long span = (long)ticket.Sequence - row.Watermark - 1;

                if (span > MaxTrackedGaps)
                {
                    // 여기까지 벌어졌으면 그 아래는 전부 "이미 처리함" 으로 취급된다.
                    row.Gaps.Clear();
                    span = MaxTrackedGaps;
                }

                for (int i = 0; i < span; i++)
                {
                    if (row.Gaps.Count >= MaxTrackedGaps)
                    {
                        RemoveOldestGap(row);
                    }

                    row.Gaps.Add(ticket.Sequence - (int)span + i);
                }

                row.Watermark = ticket.Sequence;
                return EconomyRequestVerdict.Fresh;
            }

            // 워터마크 아래다. 구멍이면 늦게 도착한 것이고, 아니면 이미 처리했다.
            return row.Gaps.Remove(ticket.Sequence)
                ? EconomyRequestVerdict.Fresh
                : EconomyRequestVerdict.Duplicate;
        }

        /// <summary>테스트·진단용. 이 플레이어가 지금 몇 개의 구멍을 들고 있는가.</summary>
        public int GapCountOf(int playerId) => rows.TryGetValue(playerId, out var row) ? row.Gaps.Count : 0;

        private static void RemoveOldestGap(Row row)
        {
            int oldest = int.MaxValue;

            foreach (var gap in row.Gaps)
            {
                if (gap < oldest)
                {
                    oldest = gap;
                }
            }

            row.Gaps.Remove(oldest);
        }

        public override string ToString() => $"원장 e{Epoch} · 추적 {rows.Count}명";
    }
}

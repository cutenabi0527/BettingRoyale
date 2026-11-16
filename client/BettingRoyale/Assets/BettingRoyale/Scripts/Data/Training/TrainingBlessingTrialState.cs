using System;
using BettingRoyale.Data.Blessing;

namespace BettingRoyale.Data.Training
{
    /// <summary>
    /// 훈련 시험 주기의 국면. 제품 매치의 <c>MatchEconomyPhase</c> 와 <b>다른 축</b>이다 —
    /// 이쪽은 한 사람이 혼자 열고 닫는 연습 주기이고 라운드 진행·계정 정산과 연결되지 않는다.
    /// </summary>
    public enum TrainingBlessingTrialPhase
    {
        /// <summary>아직 주기를 열지 않았다. 여기서만(또는 <see cref="Settled"/> 에서만) 새 주기를 연다.</summary>
        Idle = 0,

        /// <summary>준비 단계. 예치·도전 접수·잠금·재추첨·선택이 이때만 된다.</summary>
        Preparation = 1,

        /// <summary>전투 중. 예치금이 걸려 있고 보급 보상을 받을 수 있는 유일한 국면이다.</summary>
        Combat = 2,

        /// <summary>생사가 확정돼 정산이 끝났다. 예치금은 환급됐거나 사라졌다.</summary>
        Settled = 3,
    }

    /// <summary>
    /// <see cref="TrainingActionKind.TrialBegin"/> 이 받는 순위 프로필.
    ///
    /// <para>
    /// <b>이것은 조작된 과거 점수다.</b> 훈련에는 실제 지난 라운드 점수 기록이 없어서
    /// 하위권 자격(<see cref="BlessingEconomyState.IsLowerHalf"/>)을 만들 근거가 없다.
    /// 그래서 "무엇을 시험하려는가" 를 <b>사용자가 명시적으로</b> 고르게 하고,
    /// 판정 자체는 제품과 같은 함수에 2명짜리 점수표를 넣어서 받는다 —
    /// 자격을 <c>true</c> 로 대입하지 않는다.
    /// </para>
    /// </summary>
    public static class TrainingBlessingTrialRank
    {
        /// <summary>점수표가 아예 없다 (제품의 1라운드와 같다). 자격이 생기지 않는다.</summary>
        public const string None = "none";

        /// <summary>나보다 점수가 높은 사람이 있다. 하위권 자격이 성립하는 유일한 프로필이다.</summary>
        public const string Lower = "lower";

        /// <summary>내가 더 높다. 자격이 없다.</summary>
        public const string Upper = "upper";

        /// <summary>동점이다. 동점자는 같은 답을 받으므로 아무도 자격이 없다.</summary>
        public const string Tied = "tied";

        /// <summary>와이어가 그대로 실을 수 있는 허용 목록. 이 밖의 문자열은 패킷 단계에서 거절된다.</summary>
        public static readonly string[] All = { None, Lower, Upper, Tied };

        /// <summary>가장 긴 프로필 문자열의 바이트 길이. 와이어 상한이다.</summary>
        public const int MaxTextBytes = 8;

        public static bool IsDefined(string profile)
        {
            if (profile == null)
            {
                return false;
            }

            for (var i = 0; i < All.Length; i++)
            {
                // 네트워크로 오가는 값이라 문화권에 따라 판정이 갈리면 안 된다 — Ordinal 로 고정한다.
                if (string.Equals(All[i], profile, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// 한 사람의 경제형 축복 시험 주기 상태.
    ///
    /// <para>
    /// <b>판정과 돈은 전부 제품 타입이 한다.</b> 여기가 들고 있는
    /// <see cref="BlessingEconomyState"/> 와 <see cref="BlessingDraft"/> 는 진짜이고,
    /// 지갑도 <c>MatchEconomyState</c> 가 들고 있는 <b>훈련 지갑 그 자체</b>다.
    /// 이 클래스는 국면·주기 번호처럼 훈련에만 있는 값만 보관한다.
    /// </para>
    ///
    /// <para>UnityEngine 을 참조하지 않는다 (D-060).</para>
    /// </summary>
    public sealed class TrainingBlessingTrialState
    {
        /// <summary>이번 주기의 진짜 경제형 축복 상태. 주기 밖에서는 null 이다.</summary>
        internal BlessingEconomyState Economy { get; set; }

        /// <summary>
        /// 이번 주기의 진짜 추첨. <b>후보를 채울 수 없으면 null 이다</b> —
        /// 가짜 후보를 만들어 채우지 않는다. 그래도 예치 시험은 계속할 수 있다.
        /// </summary>
        internal BlessingDraft Draft { get; set; }

        public TrainingBlessingTrialPhase Phase { get; internal set; } = TrainingBlessingTrialPhase.Idle;

        /// <summary>
        /// 주기 일련번호. 주기를 열 때마다 1씩 오르고 <b>개인 초기화로도 되돌아가지 않는다</b> —
        /// 되돌아가면 초기화 전후의 주기가 같은 번호를 갖게 되고, 늦게 도착한 콜백이
        /// 다음 주기의 것으로 오인된다.
        /// </summary>
        public int Cycle { get; internal set; }

        /// <summary>이번 주기가 흉내 내는 등급 라운드 (1~3). 주기 밖에서는 0 이다.</summary>
        public int Round { get; internal set; }

        /// <summary>이번 주기에 고른 순위 프로필 (<see cref="TrainingBlessingTrialRank"/>).</summary>
        public string RankProfile { get; internal set; } = TrainingBlessingTrialRank.None;

        /// <summary>
        /// 하위권 자격. <b>대입한 값이 아니라</b> <see cref="BlessingEconomyState.IsLowerHalf"/> 가
        /// 조작된 2인 점수표에 답한 결과다.
        /// </summary>
        public bool LowerHalfEligible { get; internal set; }

        /// <summary>직전 정산에서 실제로 환급된 골드. 탈락이면 0 이다.</summary>
        public int SettlementRefund { get; internal set; }

        /// <summary>주기가 살아 있는가. 이 동안에는 직접 축복·무기 선택을 받지 않는다.</summary>
        public bool IsActive =>
            Phase == TrainingBlessingTrialPhase.Preparation || Phase == TrainingBlessingTrialPhase.Combat;

        /// <summary>
        /// <b>지금 예치금이 걸려 있는가.</b> 전투 중일 때만 참이다 — 준비 중에 낸 돈은
        /// 아직 위험에 놓이지 않았고, 정산이 끝난 뒤에는 이미 결과가 났다.
        /// 전투 버프 창을 여는 판단이 이 값이다.
        /// </summary>
        public bool InterestDeposited =>
            Phase == TrainingBlessingTrialPhase.Combat && Economy != null && Economy.InterestDeposited;

        /// <summary>
        /// 주기를 버린다 (개인 초기화 · 세션 종료). <b><see cref="Cycle"/> 은 그대로 둔다</b> —
        /// 위 설명대로 번호가 되돌아가면 주기 식별이 무너진다.
        /// </summary>
        internal void Clear()
        {
            Economy = null;
            Draft = null;
            Phase = TrainingBlessingTrialPhase.Idle;
            Round = 0;
            RankProfile = TrainingBlessingTrialRank.None;
            LowerHalfEligible = false;
            SettlementRefund = 0;
        }
    }
}

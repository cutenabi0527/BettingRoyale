using System;

namespace BettingRoyale.Data.Dto
{
    /// <summary>
    /// 매치 결과 제출 요청의 한 사람 (<c>docs/server/api.md</c> 3-3).
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>rank 는 한 제출 안에서 유일해야 한다.</b> 같은 rank 가 두 번 오면 서버가
    /// <c>400 VALIDATION_ERROR</c> 로 거절한다. 동점 정책이 아직 없는 동안(api.md 5절)
    /// 무승인 공동 순위가 사실상의 스펙으로 굳는 것을 막으려는 제약이다.
    /// </remarks>
    [Serializable]
    public sealed class PlayerResultDto
    {
        public string accountId;
        public int rank;
    }

    /// <summary>매치 결과 제출 본문 (api.md 3-3).</summary>
    [Serializable]
    public sealed class MatchResultRequestDto
    {
        public string submitterAccountId;
        public PlayerResultDto[] results;
    }

    /// <summary>
    /// 한 사람의 메달 영수증 (api.md 3-3 응답 · 3-5 조회).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b><see cref="duplicate"/> 는 UI 의 <c>IsFreshGrant</c> 와 뜻이 반대다.</b>
    /// 뒤집는 책임은 <c>MatchResultAdapter</c> 한 곳에 있다. 그대로 이으면 처음 지급인데
    /// 화면이 "이미 지급된 결과입니다" 를 띄우고 획득 연출이 통째로 사라진다.
    /// </para>
    /// <para>
    /// ⚠️ 서버는 메달을 <c>long</c> 으로 준다. UI 계약(<c>MatchResultReceived</c>)은 <c>int</c> 라
    /// 어댑터가 좁히는 변환을 한다. 지금 규모에서는 문제가 없지만 사실은 남겨 둔다.
    /// </para>
    /// <para>
    /// <b>점수는 여기 없다.</b> 서버는 rank 만 받고 점수를 검증도 저장도 하지 않는다(D-004).
    /// <c>StandingView.Score</c> 는 Host 세션 데이터에서 온다 (축5 합의, 2026-08-28).
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class MedalReceiptDto
    {
        public string accountId;
        public string nickname;
        public int rank;
        public long medalsAwarded;
        public long newBalance;
        public bool duplicate;
    }

    /// <summary>매치 결과 응답 (api.md 3-3 · 3-5).</summary>
    /// <remarks>
    /// <c>receipts</c> 의 순서가 곧 화면에 그릴 순서다 — 완료 조건
    /// "최종 순위 행 순서와 서버 rank 가 일치"(S15P21A606-38)가 이 순서에 기댄다.
    /// 어댑터는 <b>다시 정렬하지 않는다.</b> 정렬해 버리면 서버가 순서를 어겨도
    /// 화면에서는 멀쩡해 보여 결함이 숨는다.
    /// </remarks>
    [Serializable]
    public sealed class MatchResultResponseDto
    {
        public string matchId;
        public MedalReceiptDto[] receipts;
    }

    /// <summary>
    /// 서버 오류 본문 (api.md 2절 오류 모델).
    /// </summary>
    /// <remarks>
    /// <c>detail</c> 은 <b>화면에 그대로 띄우지 않는다</b> — 내부 정보가 새어 나간다
    /// (S15P21A606-39 완료 조건). 사람에게 보여줄 문구는 어댑터가 <see cref="code"/> 로 고른다.
    /// </remarks>
    [Serializable]
    public sealed class ApiErrorDto
    {
        public string code;
        public string message;
        public string detail;
    }
}

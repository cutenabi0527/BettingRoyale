using BettingRoyale.Core;

namespace BettingRoyale.Data.Contracts
{
    /// <summary>
    /// UI 조작 피드백의 종류. 화면이 "어떤 클립"이 아니라 "무슨 일이 있었는지"만 알린다 (D-065).
    /// </summary>
    public enum UiFeedbackCue
    {
        /// <summary>버튼·항목을 눌렀다.</summary>
        Click = 0,

        /// <summary>요청이 받아들여졌다(로그인 성공, 선택 확정 등).</summary>
        Success = 1,

        /// <summary>요청이 거절되거나 실패했다.</summary>
        Fail = 2,
    }

    /// <summary>
    /// UI 가 표현 계층에 피드백을 요청한다. 소비자는 사운드(<c>SoundManager</c>)이며
    /// 게임 상태를 바꾸지 않는다 — 이 이벤트를 게임 규칙 신호로 쓰지 않는다.
    /// </summary>
    public readonly struct UiFeedbackRequested : IGameEvent
    {
        public readonly UiFeedbackCue Cue;

        public UiFeedbackRequested(UiFeedbackCue cue) => Cue = cue;
    }
}

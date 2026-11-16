using BettingRoyale.Core;
using BettingRoyale.Data.Audio;
using BettingRoyale.Data.Common;

namespace BettingRoyale.Data.Contracts
{
    /// <summary>Local presentation only; never replicated as a gameplay command.</summary>
    public readonly struct SoundCueRequested : IGameEvent
    {
        public SoundCueRequested(SoundId id)
        {
            Id = id;
            Position = null;
        }

        /// <summary>월드에서 난 소리. 그 자리에서 재생되고 거리에 따라 감쇠한다 (S-AUD-4AMCAR).</summary>
        public SoundCueRequested(SoundId id, WorldPosition position)
        {
            Id = id;
            Position = position;
        }

        public SoundId Id { get; }

        /// <summary>
        /// 소리가 난 자리. <c>null</c> 이면 2D 전역 재생이다 — 위치가 없는 UI·연출음의 기존 동작이다.
        /// </summary>
        public WorldPosition? Position { get; }
    }

    /// <summary>Owner identity scopes a loop. A stop must use the same owner and ID.</summary>
    public readonly struct SoundLoopRequested : IGameEvent
    {
        public SoundLoopRequested(object owner, SoundId id, bool playing)
        { Owner = owner; Id = id; Playing = playing; Position = null; }

        /// <summary>
        /// 월드에서 나는 반복음. 그 자리에서 재생되고 거리에 따라 감쇠한다 (S-AUD-4AMCAR).
        /// </summary>
        /// <remarks>
        /// <b>움직이는 소리는 같은 <c>owner</c>·<c>id</c> 로 다시 발행해 위치를 갱신한다.</b>
        /// 재생 중인 보이스는 샘플 위치를 유지한 채 좌표만 옮기므로 소리가 끊기지 않는다 —
        /// 화살 비행음이 그 경로다.
        /// </remarks>
        public SoundLoopRequested(object owner, SoundId id, bool playing, WorldPosition position)
        { Owner = owner; Id = id; Playing = playing; Position = position; }

        public object Owner { get; }
        public SoundId Id { get; }
        public bool Playing { get; }

        /// <summary>소리가 나는 자리. <c>null</c> 이면 2D 전역 재생이다(기존 동작).</summary>
        public WorldPosition? Position { get; }
    }

    public readonly struct SoundStageChanged : IGameEvent
    {
        public SoundStageChanged(SoundId music, float countdownSeconds = 0f)
        { Music = music; CountdownSeconds = countdownSeconds; }
        public SoundId Music { get; }
        public float CountdownSeconds { get; }
    }

}

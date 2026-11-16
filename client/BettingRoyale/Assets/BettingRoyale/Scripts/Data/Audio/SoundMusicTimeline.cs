namespace BettingRoyale.Data.Audio
{
    /// <summary>Presentation timing only. Never ends a gameplay phase.</summary>
    public static class SoundMusicTimeline
    {
        public const float PreparationTransitionSeconds = 83f;
        public const float CombatTransitionSeconds = 61f;

        public static SoundId Resolve(SoundId stage, float elapsed)
        {
            if (stage == SoundId.PreparationBgm && elapsed >= PreparationTransitionSeconds)
                return SoundId.None;
            if (stage == SoundId.CombatBgm && elapsed >= 90f) return SoundId.None;
            if (stage == SoundId.CombatBgm && elapsed >= CombatTransitionSeconds)
                return SoundId.CombatLateBgm;
            return stage;
        }

        public static SoundId RoundResult(int round) =>
            round >= 3 ? SoundId.ThirdRoundResultBgm : SoundId.ResultBgm;

        public static bool IsOneShot(SoundId id) => id == SoundId.ResultBgm
            || id == SoundId.ThirdRoundResultBgm || id == SoundId.FinalResultBgm;
    }
}

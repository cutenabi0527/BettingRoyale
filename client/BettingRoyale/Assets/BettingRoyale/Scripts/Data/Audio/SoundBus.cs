namespace BettingRoyale.Data.Audio
{
    /// <summary>
    /// 사운드가 흘러가는 믹서 버스. AudioMixer 그룹 구조와 1:1 로 대응한다 (S-AUD-VJHFVK).
    /// </summary>
    public enum SoundBus
    {
        /// <summary>Master 직속 배경음 버스. 이번 범위에서는 클립을 채우지 않는다.</summary>
        Bgm = 0,

        /// <summary>전투·라운드 연출 효과음 버스.</summary>
        CombatSfx = 1,

        /// <summary>UI 조작 피드백 버스.</summary>
        UiSfx = 2,

        Ambience = 3,
        Voice = 4,
    }
}

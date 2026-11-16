namespace BettingRoyale.Data.Scenes
{
    public enum MatchScenePhase
    {
        Preparation,
        Combat,
    }

    /// <summary>안정적인 매치 씬 키를 현재 Player Build의 씬 이름에 연결한다.</summary>
    public readonly struct MatchSceneDescriptor
    {
        public MatchSceneDescriptor(
            MatchSceneKey key,
            string builtInSceneName,
            string mapId,
            MatchScenePhase phase)
        {
            Key = key;
            BuiltInSceneName = builtInSceneName;
            MapId = mapId;
            Phase = phase;
        }

        public MatchSceneKey Key { get; }

        public string BuiltInSceneName { get; }

        /// <summary>준비/전투 씬을 같은 맵 묶음으로 식별하는 안정적인 소문자 ID.</summary>
        public string MapId { get; }

        public MatchScenePhase Phase { get; }
    }
}

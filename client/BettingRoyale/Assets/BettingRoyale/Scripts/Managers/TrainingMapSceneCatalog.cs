using System.Collections.Generic;
using BettingRoyale.Data.Scenes;

namespace BettingRoyale.Managers
{
    /// <summary>
    /// 제품 05_Training과 Dev 96_TrainingLab이 함께 소비하는 훈련 맵 식별자.
    /// 일반 매치의 맵 순환에는 넣지 않고 별도 허용 목록을 공용 Built-in provider에 제공한다.
    /// 제품 진입을 위해 현재 아카데미 맵은 Player Build에 포함한다.
    /// </summary>
    public static class TrainingMapSceneCatalog
    {
        /// <summary>현재 훈련장 콘텐츠의 기본 맵. 구형 맵 삭제 후 현행 02를 01로 승격했다 (S-QA-3K39BA).</summary>
        public const string DefaultScenePath =
            "Assets/BettingRoyale/Scenes/Maps/Training/Map_Training_Academy_01.unity";

        public static readonly MatchSceneKey DefaultMapKey =
            new("lab/training/map_training_academy_01");

        public static readonly MatchSceneDescriptor DefaultMap = new(
            DefaultMapKey,
            DefaultScenePath,
            "training/academy/01",
            MatchScenePhase.Combat);

        private static readonly MatchSceneDescriptor[] MapArray = { DefaultMap };

        /// <summary>훈련 세션에 허용한 맵 목록. 일반 매치의 맵 순환과 독립적이다.</summary>
        public static readonly IReadOnlyList<MatchSceneDescriptor> Maps = System.Array.AsReadOnly(MapArray);
    }
}

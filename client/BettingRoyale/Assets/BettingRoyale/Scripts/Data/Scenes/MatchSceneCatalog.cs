using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Scenes
{
    /// <summary>
    /// MVP가 제공할 매치 맵 씬의 코드 계약.
    /// 실제 씬을 제작할 때 Build Profile 등록과 이 목록 변경을 같은 작업으로 처리한다.
    /// </summary>
    public static class MatchSceneCatalog
    {
        /// <summary>
        /// ⚠️ 명시적 Dev/회귀 fixture 전용 키다. 제품 경로(Run MVP·Local Play·온라인 Host/Client)는
        /// 이 키를 기본값으로 고르지 않는다 (S-CLI-E3KG5D). 판별은 <see cref="IsDevRegressionFixture"/>.
        /// </summary>
        public static readonly MatchSceneKey DefaultPreparation01 =
            new("map/default/preparation/01");

        public static readonly MatchSceneKey DefaultPreparation02 =
            new("map/default/preparation/02");

        /// <summary>제품 온라인 매치와 Local Play가 함께 사용하는 기본 준비 맵.</summary>
        public static MatchSceneKey DefaultPreparation => DefaultPreparation02;

        /// <summary>
        /// ⚠️ 명시적 Dev/회귀 fixture 전용 키다. Run MVP·Local Play·온라인 Host/Client의 기본
        /// 전투 맵은 <see cref="DefaultCombat"/>(= <see cref="DefaultCombat02"/>) 하나뿐이며,
        /// 어떤 제품 경로도 이 키로 조용히 되돌아가지 않는다 (S-CLI-E3KG5D).
        /// </summary>
        public static readonly MatchSceneKey DefaultCombat01 =
            new("map/default/combat/01");

        public static readonly MatchSceneKey DefaultCombat02 =
            new("map/default/combat/02");

        /// <summary>제품 온라인 매치와 Local Play가 함께 사용하는 기본 전투 맵.</summary>
        public static MatchSceneKey DefaultCombat => DefaultCombat02;

        private static readonly MatchSceneKey[] DevRegressionFixtureKeys =
        {
            DefaultPreparation01,
            DefaultCombat01,
        };

        private static readonly MatchSceneDescriptor[] Descriptors =
        {
            new(
                DefaultPreparation01,
                "Map_Default_Preparation_01",
                "default",
                MatchScenePhase.Preparation),
            new(
                DefaultPreparation02,
                "Map_Default_Preparation_02",
                "default",
                MatchScenePhase.Preparation),
            new(
                DefaultCombat01,
                "Map_Default_Combat_01",
                "default",
                MatchScenePhase.Combat),
            new(
                DefaultCombat02,
                "Map_Default_Combat_02",
                "default",
                MatchScenePhase.Combat),
        };

        private static readonly IReadOnlyList<MatchSceneDescriptor> ReadOnlyDescriptors =
            Array.AsReadOnly(Descriptors);

        private static readonly IReadOnlyList<MatchSceneDescriptor> ReadOnlyCombatDescriptors =
            Array.AsReadOnly(Array.FindAll(
                Descriptors,
                descriptor => descriptor.Phase == MatchScenePhase.Combat));

        private static readonly Dictionary<MatchSceneKey, MatchSceneDescriptor> ByKey = BuildIndex();

        public static IReadOnlyList<MatchSceneDescriptor> All => ReadOnlyDescriptors;

        /// <summary>CombatSandbox와 매치 흐름이 선택할 수 있는 전투 맵 씬만 반환한다.</summary>
        public static IReadOnlyList<MatchSceneDescriptor> Combat => ReadOnlyCombatDescriptors;

        public static bool TryGet(MatchSceneKey key, out MatchSceneDescriptor descriptor) =>
            ByKey.TryGetValue(key, out descriptor);

        /// <summary>
        /// 카탈로그에는 남아 있지만 제품 기본 경로가 고를 수 없는 Dev/회귀 fixture 키인지 알려준다.
        /// Dev Lab의 명시적 맵 선택과 회귀 테스트만 이 키를 직접 요청한다.
        /// </summary>
        public static bool IsDevRegressionFixture(MatchSceneKey key)
        {
            for (int i = 0; i < DevRegressionFixtureKeys.Length; i++)
            {
                if (DevRegressionFixtureKeys[i] == key)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Host가 선택한 키가 해당 국면의 등록된 맵인지 확인한다. 네트워크로 받은 키와
        /// 대기실의 향후 맵 선택을 같은 계약으로 검증한다.
        /// </summary>
        public static bool TryGetForPhase(
            MatchSceneKey key,
            MatchScenePhase phase,
            out MatchSceneDescriptor descriptor) =>
            ByKey.TryGetValue(key, out descriptor) && descriptor.Phase == phase;

        /// <summary>준비·전투가 같은 저작 맵 묶음에 속하는지 확인한다.</summary>
        public static bool IsValidSelection(MatchSceneKey preparation, MatchSceneKey combat)
        {
            return TryGetForPhase(preparation, MatchScenePhase.Preparation, out MatchSceneDescriptor preparationDescriptor)
                && TryGetForPhase(combat, MatchScenePhase.Combat, out MatchSceneDescriptor combatDescriptor)
                && string.Equals(preparationDescriptor.MapId, combatDescriptor.MapId, StringComparison.Ordinal);
        }

        private static Dictionary<MatchSceneKey, MatchSceneDescriptor> BuildIndex()
        {
            var result = new Dictionary<MatchSceneKey, MatchSceneDescriptor>(Descriptors.Length);
            foreach (MatchSceneDescriptor descriptor in Descriptors)
            {
                result.Add(descriptor.Key, descriptor);
            }

            return result;
        }
    }
}

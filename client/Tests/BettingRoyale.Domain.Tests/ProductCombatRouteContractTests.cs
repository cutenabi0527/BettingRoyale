using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 제품 전투 맵 라우팅 회귀 (S15P21A606-169 · S-CLI-E3KG5D).
    ///
    /// Run MVP(Local Play), 제품 온라인 Host/Client, 그 변형 경로는 전부
    /// <c>MatchSceneCatalog.DefaultCombat</c>(= Combat_02)을 <c>MatchSceneKey</c>/<c>IMatchSceneProvider</c>
    /// 로 소비해야 한다. 이 테스트는 어떤 제품 경로도 Combat_01 을 다시 하드코딩하거나
    /// 조용한 fallback 으로 되살리지 못하게 소스 자체를 잠근다.
    ///
    /// ⚠️ 이 테스트는 컴파일 산출물이 아니라 원본 소스를 읽는다. 파일이 이동하면
    /// "경로를 못 찾음"으로 실패하며, 그때 이 목록을 갱신하는 것이 의도된 절차다.
    /// </summary>
    [TestFixture]
    public sealed class ProductCombatRouteContractTests
    {
        private const string ScriptsRelativePath =
            "client/BettingRoyale/Assets/BettingRoyale/Scripts";

        /// <summary>전투 맵을 고르거나 로드하는 제품 실행 경로.</summary>
        private static readonly string[] ProductCombatRouteSources =
        {
            // 온라인 Host 권위 상태 + 신뢰 채널 스냅샷
            "Data/Online/OnlineCoreLoopState.cs",
            "Network/OnlineCoreLoopCoordinator.cs",
            // Host/Client 가 같은 키로 Additive Map Scene 을 결합하는 경로
            "Network/OnlineCoreLoopWorldPresenter.cs",
            // Run MVP (Local Play) 런타임 조립
            "Editor/LocalPlayMvpLauncher.cs",
            // Direct Play(04_CombatSandbox) 라운드 진입
            "Gameplay/Combat/CombatRoundSandboxController.cs",
        };

        /// <summary>전투 맵 키를 직접 고르지는 않지만 Combat_01 로 되돌아가면 안 되는 경계.</summary>
        private static readonly string[] SupportingRouteSources =
        {
            "Managers/SceneLoader.cs",
            "Managers/BuiltInMatchSceneProvider.cs",
            "Managers/IMatchSceneProvider.cs",
            // Dev Lab 탐색도 제품 기본 맵을 먼저 세우고 fixture 를 하드코딩하지 않는다.
            "Managers/CombatLabMapCatalog.cs",
            "Managers/TrainingMapSceneCatalog.cs",
            "Gameplay/Training/TrainingLocalSession.cs",
            "Gameplay/Training/TrainingWorldController.cs",
        };

        private static readonly string[] Combat01Markers =
        {
            "DefaultCombat01",
            "Map_Default_Combat_01",
            "map/default/combat/01",
        };

        [Test]
        public void EveryProductCombatRouteSelectsTheCanonicalDefaultCombatKey()
        {
            foreach (string relativePath in ProductCombatRouteSources)
            {
                string source = ReadRouteSource(relativePath);

                Assert.That(
                    source,
                    Does.Contain("MatchSceneCatalog.DefaultCombat"),
                    $"{relativePath}: 제품 전투 경로는 공용 기본 전투 맵 키를 소비해야 합니다.");
            }
        }

        [TestCaseSource(nameof(AllRouteSources))]
        public void NoProductRouteMentionsCombat01(string relativePath)
        {
            string source = ReadRouteSource(relativePath);

            foreach (string marker in Combat01Markers)
            {
                Assert.That(
                    source,
                    Does.Not.Contain(marker),
                    $"{relativePath}: '{marker}' 은 명시적 Dev/회귀 fixture 전용입니다. "
                    + "제품 경로는 MatchSceneCatalog.DefaultCombat(Combat_02) 만 사용합니다.");
            }
        }

        [Test]
        public void TrainingRouteKeepsItsOwnAcademyMapInsteadOfTheDefaultCombatMap()
        {
            string trainingCatalog = ReadRouteSource("Managers/TrainingMapSceneCatalog.cs");

            Assert.That(trainingCatalog, Does.Contain("Map_Training_Academy_01.unity"));
            Assert.That(
                trainingCatalog,
                Does.Not.Contain("MatchSceneCatalog."),
                "훈련장은 제품 매치 맵 순환과 독립된 허용 목록을 유지합니다.");

            foreach (string relativePath in new[]
            {
                "Gameplay/Training/TrainingLocalSession.cs",
                "Gameplay/Training/TrainingWorldController.cs",
            })
            {
                string source = ReadRouteSource(relativePath);
                Assert.That(
                    source,
                    Does.Contain("TrainingMapSceneCatalog.DefaultMapKey"),
                    $"{relativePath}: 훈련 경로는 훈련 카탈로그 키만 로드해야 합니다.");
                Assert.That(
                    source,
                    Does.Not.Contain("MatchSceneCatalog.DefaultCombat"),
                    $"{relativePath}: 훈련장을 Combat_02 로 바꾸지 않습니다.");
            }
        }

        private static IEnumerable<string> AllRouteSources()
        {
            foreach (string path in ProductCombatRouteSources)
            {
                yield return path;
            }

            foreach (string path in SupportingRouteSources)
            {
                yield return path;
            }
        }

        private static string ReadRouteSource(string relativePath)
        {
            string fullPath = Path.Combine(ResolveScriptsRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(
                File.Exists(fullPath),
                Is.True,
                $"제품 경로 소스를 찾지 못했습니다: {relativePath}. "
                + "파일을 옮겼다면 이 라우팅 회귀 목록을 함께 갱신하세요.");
            return File.ReadAllText(fullPath);
        }

        private static string ResolveScriptsRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(
                    directory.FullName,
                    ScriptsRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                $"저장소 루트에서 {ScriptsRelativePath} 를 찾지 못했습니다 (시작: {AppContext.BaseDirectory}).");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BettingRoyale.Core;
using BettingRoyale.Data.Chest;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.Online;
using BettingRoyale.Data.Scenes;
using BettingRoyale.Gameplay.Combat;
using BettingRoyale.Gameplay.Roulette;
using BettingRoyale.Managers;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BettingRoyale.Network
{
    /// <summary>
    /// 제품 매치의 준비·전투 공간을 <b>저작된 Default Map Scene 두 개</b>로 세운다.
    /// 정적 콘텐츠는 씬이 소유하고, 플레이어 위치는 Host의 NetworkTransform이 확정한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 이 표현기는 더 이상 런타임에 맵을 만들지 않는다 (S15P21A606 s94 · D-076).
    /// <see cref="MatchSceneCatalog.DefaultPreparation"/> 과 <see cref="MatchSceneCatalog.DefaultCombat"/> 을
    /// <see cref="BuiltInMatchSceneProvider"/> 로 Additive 로드하고, 두 씬의 <b>단일 Map Root</b> 와
    /// <c>Anchors</c>·<c>Collision</c> 계약만 소비한다. 맵이 없거나 계층이 다르면
    /// <b>가짜 맵으로 대체하지 않고 닫힌 채 실패한다</b> — 조용한 폴백은 저작 실수를 런타임에 숨긴다.
    /// </para>
    /// <para>
    /// <b>저작 헬퍼(<see cref="BuildPreparationMap"/>·<see cref="BuildCombatMap"/>)는 남긴다.</b>
    /// 추후 삭제 대상으로 격리했으며 기존 EditMode fixture만 사용한다. 제품 맵 재생성 메뉴는 차단했다.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class OnlineCoreLoopWorldPresenter : MonoBehaviour
    {
        internal const string PreparationMapName = "[Online Preparation Map - Colosseum]";
        internal const string CombatMapName = "[Online Combat Map - Rectangle]";

        /// <summary>
        /// 라운드마다 다시 세우는 상자·표현물의 루트. <b>저작 맵 안에 두지 않는다</b> — 정적 씬의
        /// 소유권과 런타임 활동의 소유권을 섞으면 맵 언로드·재바인딩에서 무엇이 남는지 알 수 없다.
        /// </summary>
        private const string CombatRuntimeRootName = "[Online Combat Runtime Activities]";
        internal static readonly CombatantId PreparationTrainingDummyId = new(900054);

        private const int MaximumParticipants = 8;
        private const int ColosseumWallSegmentCount = 32;
        private const float ColosseumRadius = 20f;
        private const float ColosseumWallHeight = 3f;
        private const float ColosseumWallThickness = 0.55f;
        private const float StandTierDepth = 2.5f;
        private const int ColumnCount = 16;
        private const float ColumnRingRadius = 21.4f;
        private const float ColumnDiameter = 0.8f;
        private const float ColumnHeight = 5f;
        private const float ColumnAngleOffsetDegrees = 11.25f;
        private const float GatePierOffset = 2.56f;
        private const string AnchorRootName = "Anchors";
        private const string PreparationSpawnAnchorPrefix = "PreparationSpawn_";
        private const string CombatSpawnAnchorPrefix = "CombatSpawn_";
        private const string ChestAnchorPrefix = "ChestAnchor_";
        private const float PlayerRootY = 1f;

        private static readonly Vector3 PreparationMapPosition = new(-60f, 0f, 0f);

        /// <summary>
        /// 준비 콜로세움 루트의 월드 위치. <b>준비 단계 시설 좌표의 원점이다</b>
        /// (S15P21A606-109 §A).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>왜 노출하는가.</b> 시설 구역표(<c>PlaceholderPreparationFacilityMap</c>)는 상점·모루·룰렛을
        /// <b>콜로세움 로컬 좌표</b>로 적는다 — 프리팹을 저작한 공간이고 콜로세움을 어디 두든 안 바뀐다.
        /// 반면 전투원 위치는 월드다. 둘을 맞추는 것은 어댑터 몫이라
        /// <c>NetworkPreparationPresenceProvider</c> 가 이 값을 빼서 변환한다.
        /// </para>
        /// <para>
        /// ⚠️ <b>회전은 없다고 가정한다.</b> 루트는 <see cref="CreateRoot"/> 에서 위치만 받는다.
        /// 콜로세움을 돌리게 되면 이 값 하나로는 부족해지므로 변환을 행렬로 바꿔야 한다.
        /// </para>
        /// </remarks>
        public static Vector3 PreparationMapOrigin => PreparationMapPosition;
        private static readonly Vector3 CombatMapPosition = new(60f, 0f, 0f);
        // S-CLI-EYCKKC — 아래 콜로세움 외형 수치·색은 전부 ⚠️ 임시(assumed)다.
        private static readonly int[] GateWallSlots = { 0, 8, 16, 24 };
        private static readonly float[] StandTierRadii = { 22f, 24.5f, 27f };
        private static readonly float[] StandTierHeights = { 1.6f, 3.2f, 4.8f };
        private static readonly Color[] StandTierColors =
        {
            new(0.66f, 0.62f, 0.54f),
            new(0.61f, 0.57f, 0.50f),
            new(0.56f, 0.53f, 0.47f),
        };
        private static readonly Color SandFloorColor = new(0.76f, 0.66f, 0.45f);
        private static readonly Color StoneWallColor = new(0.55f, 0.53f, 0.50f);
        private static readonly Color StoneBeigeColor = new(0.72f, 0.66f, 0.52f);
        private static readonly Color ArchInteriorColor = new(0.07f, 0.06f, 0.08f);
        private static readonly Color BattlefieldCoverColor = new(0.26f, 0.31f, 0.38f);

        // 2차 아트 패스 — "숲속에 남겨진 오래된 석조 투기장" (S-CLI-NY4HVG).
        // Greybox 바닥은 폴백이자 석조 타일 사이로 드러나는 흙바닥이라 흙빛으로 둔다.
        private static readonly Color BattlefieldEarthFloorColor = new(0.28f, 0.22f, 0.15f);

        // 48x28 전장을 4m 정사각 타일 12x7 격자로 나눈다. 좌표는 타일 중심이다.
        private const float CombatFloorTileSize = 4f;
        private const int CombatFloorTileColumns = 12;
        private const int CombatFloorTileRows = 7;
        private const float CombatFloorFirstTileX = -22f;
        private const float CombatFloorFirstTileZ = -12f;

        // 주 교전 공간을 덮는 석조 바닥 범위. 이 밖은 흙바닥이 드러난다.
        private const float CombatStoneFloorHalfWidth = 14f;
        private const float CombatStoneFloorHalfDepth = 8f;

        // 변형 석판을 하나도 두지 않는다 (S-CLI-3NYWVN). 런타임 화면에서 육각 석조 사이의 사각 패치가
        // 낡은 석판이 아니라 텍스처 누락처럼 읽혔다. 84칸이 전부 같은 타일이라 외곽선이 생기지 않는다.
        // 배열만 비우고 해석 경로는 남겨, 나중에 되살릴 때 격자 규칙을 다시 만들지 않게 한다.
        private static readonly Vector2Int[] CombatFloorAccentCells = System.Array.Empty<Vector2Int>();

        // Full / Connector 높이 (S-GD-8MSZMP). 캐릭터는 2m·반지름 0.5 이고 카메라는 플레이어 기준
        // (0,13,-11)·pitch 48° 라, 2m 벽은 밀착해도 상위 0.57m 가 노출된다. 2.7m 는 밀착 시 전신,
        // 1m 거리에서 몸통 약 78% 를 가린다. 2m 거리 전신 은폐는 4.18m 가 필요해 목표로 삼지 않는다.
        // 2.7m 로는 밀착해도 머리·상체가 벽 위로 보였다. 3.2m 가 1차 적용값이다 (S-GD-KXZQA3).
        private const float FullCoverHeight = 3.2f;
        private const float FullCoverCenterY = 1.6f;

        // Full Cover 끝 기둥. pillar.fbx 는 1.5 x 4 x 1.5 다.
        // scale 0.675(높이 2.7m)로 낮췄더니 벽 상판과 같은 높이가 되어 화면에서 사라졌고,
        // scale 0.9(높이 3.6m)도 벽 두께 안에 두느라 둥근 상단만 솟아 굴뚝처럼 보였다.
        // 그래서 기둥을 벽의 교전 공간 쪽 측면에 반쯤 얹어, 세로 몸체가 벽 끝을 받치는 것처럼
        // 읽히게 한다 (S-GD-KXZQA3). 벽 밖으로 나온 부피는 기둥 전용 프록시 Collider 가 막는다.
        // scale 0.95 → 높이 3.8m(벽 3.2m 위로 0.6m), 폭 1.425m. 회전은 footprint 를 키우지 않게 0 이다.
        private const float FullCoverEndPillarScale = 0.95f;
        private const float FullCoverEndPillarInset = 0.95f;
        private const float FullCoverEndPillarYawStep = 0f;

        // 회전이 없으므로 수평 반너비는 변의 절반이다.
        private const float FullCoverEndPillarRadius = 1.5f * FullCoverEndPillarScale * 0.5f;

        // 북쪽 외벽 랜드마크 — 봉인된 철문. 남/북 12 세그먼트 중 이 인덱스만 게이트 벽으로 바꾼다.
        private const int CombatNorthLandmarkSegmentIndex = 6;

        // wall_cracked 는 짧은 노후 구간에만 쓴다. 이전에는 세 칸마다여서 너무 흔했다.
        private static readonly int[] CombatCrackedWallSeeds = { 2, 3 };

        // 벽 횃불 6개 — 북 2 / 남 2 / 동 1 / 서 1. 랜드마크(x≈2)와 코너 기둥을 피한다.
        private static readonly (string Name, Vector3 Position, float Yaw)[] CombatWallTorches =
        {
            ("Torch_North_West", new Vector3(-9f, 2f, 13.6f), 180f),
            ("Torch_North_East", new Vector3(11f, 2f, 13.6f), 180f),
            ("Torch_South_West", new Vector3(-13f, 2f, -13.6f), 0f),
            ("Torch_South_East", new Vector3(8f, 2f, -13.6f), 0f),
            ("Torch_East", new Vector3(23.6f, 2f, -6f), 270f),
            ("Torch_West", new Vector3(-23.6f, 2f, 5f), 90f),
        };

        // 해질녘 전투 조명 — 씬 값을 영구히 덮지 않고 전투 진입 때만 적용하고 복귀 때 되돌린다.
        private static readonly Color CombatSunsetLightColor = new(0.82f, 0.85f, 0.95f);
        private const float CombatSunsetLightIntensity = 0.55f;
        private const float CombatSunsetAmbientIntensity = 0.6f;

        // 내부 폐허 장식 — 사용자 검토에서 갈색 돌무더기 8개가 회색 석조 바닥과 어울리지 않고
        // 흙더미·오염물처럼 보인다고 확인돼 전부 뺐다. 같은 에셋을 줄여 다시 놓거나 다른 자갈로
        // 즉시 대체하지 않는다. 바닥은 깨끗한 석조 전장으로 되돌린다.
        // 프리팹(CombatRubbleVisual)과 카탈로그 슬롯은 남겨 두고 배치만 비운다.
        private static readonly (string Name, Vector3 Position, float Scale, float Yaw)[] CombatInteriorRubble =
            System.Array.Empty<(string, Vector3, float, float)>();

        // 라운드마다 서로 다른 ChestAnchor 두 곳에 상자를 표시한다 (S-GD-VDXTYM → S-GD-34YV6X).
        private const string CombatChestRootName = "ChestVisuals";
        private const int CombatChestsPerRound = 2;
        private const string CombatChestMovementLayerName = "Ignore Raycast";

        // 상자는 시각물보다 높은 이동 차단 프록시를 사용해 CharacterController가 올라설 수 없게 한다.
        // 공격 쿼리는 Ignore Raycast 레이어를 제외하므로 이 높이가 공격 판정을 가리지 않는다 (S-GD-R2W6E9).
        private static readonly Vector3 CombatChestColliderSize = new(1.105f, 2f, 0.94f);
        private static readonly Vector3 CombatChestColliderCenter = new(0f, 1f, 0f);

        /// <summary>
        /// Full Cover 주 벽의 (중심, 장축 길이, <b>장축</b> yaw). yaw 는 벽이 바라보는 방향이 아니라
        /// 기둥이 늘어설 축이다. 이걸 반대로 넘기면 기둥이 두께 방향으로 밀려 Collider 밖으로 나간다.
        /// </summary>
        /// <remarks>
        /// SideOffset 은 벽 두께의 절반이라, 기둥 중심이 교전 공간 쪽 벽 측면에 정확히 얹히고
        /// 절반이 밖으로 나온다. 그래서 세로 몸체가 보이고, 나온 부피는 전용 Collider 가 막는다.
        /// </remarks>
        private static readonly (string Cover, Vector3 WallCenter, float WallLength, float LongAxisYaw, Vector3 SideOffset)[]
            CombatFullCoverEndPillarWalls =
        {
            ("Cover_East_Mid", new Vector3(17.6f, 0f, 0f), 4.5f, 90f, new Vector3(-0.9f, 0f, 0f)),
            ("FC_North_Center", new Vector3(3.5f, 0f, 6.2f), 4.4f, 0f, new Vector3(0f, 0f, -0.9f)),
            ("FC_South_Center", new Vector3(3.6f, 0f, -6.4f), 4f, 0f, new Vector3(0f, 0f, 0.9f)),
        };

        // floor_tile_large 실측: bounds center y = -0.025, size y = 0.15 → 타일 윗면이 자기 원점보다
        // 0.05m 높다. 그대로 y = 0 에 놓으면 Collider 윗면(y = 0)보다 5cm 떠서 발이 묻혀 보인다.
        private const float CombatFloorTileY = -0.05f;

        // 외벽 안쪽 면을 덮는 4m 벽 세그먼트. 남/북 12개, 동/서 7개로 48x28 을 정확히 채운다.
        private static readonly float[] CombatWallSegmentXs =
        { -22f, -18f, -14f, -10f, -6f, -2f, 2f, 6f, 10f, 14f, 18f, 22f };
        private static readonly float[] CombatWallSegmentZs =
        { -12f, -8f, -4f, 0f, 4f, 8f, 12f };
        private static readonly Vector3 CombatWallSegmentScale = new(4f, 3f, 0.6f);
        private const float CombatWallSegmentCenterY = 1.5f;

        // 네 모서리 기둥. pillar_decorated 는 높이 4m 라 3m 외벽보다 1m 높은 실루엣을 만든다.
        // 전부 외벽 Collider(|x| = 24.3, |z| = 14.3) 바깥이라 통과 가능한 가짜 장애물이 되지 않는다.
        private static readonly (string Name, Vector3 Position, float Scale, float Yaw)[] CombatCornerPillars =
        {
            ("ArenaPillar_NorthWest", new Vector3(-25.4f, 0f, 15.4f), 1f, 15f),
            ("ArenaPillar_NorthEast", new Vector3(25.4f, 0f, 15.4f), 0.9f, 105f),
            ("ArenaPillar_SouthWest", new Vector3(-25.4f, 0f, -15.4f), 1.05f, 195f),
            ("ArenaPillar_SouthEast", new Vector3(25.4f, 0f, -15.4f), 0.95f, 285f),
        };

        // 무너진 기둥 밑동. 모서리 두 곳만 실루엣을 다르게 만든다.
        private static readonly (string Name, Vector3 Position, float Yaw)[] CombatCornerStumps =
        {
            ("ArenaPillarStump_NorthEast", new Vector3(26.9f, 0.55f, 14.2f), 40f),
            ("ArenaPillarStump_SouthWest", new Vector3(-26.9f, 0.55f, -14.2f), 220f),
        };

        private static readonly Vector3 CombatCornerStumpScale = new(0.9f, 0.55f, 0.9f);

        // 외벽 바깥 지형 — 잔디 링과 성벽 기초 전이 구간. 안쪽은 비워 BattlefieldFloor 를 덮지 않는다.
        // 고정 카메라(offset (0,13,-11), pitch 48°, vFOV 60, 16:9)는 지면을 최대 z + 45.3m,
        // 좌우 ±46.5m 까지 비춘다. 플레이어 이동 범위를 더해 x ±72, z -26~48 이면 빈 하늘 지면이 안 보인다.
        private static readonly Color CombatOuterGrassColor = new(0.26f, 0.30f, 0.16f);
        // 잔디와 명도를 거의 맞춘 어두운 녹갈색이다. 이전 (0.30, 0.26, 0.17) 은 갈색 띠로 읽혔다.
        private static readonly Color CombatOuterVergeColor = new(0.27f, 0.29f, 0.19f);
        private const float CombatOuterGroundCenterY = -0.2f;
        private const float CombatOuterGroundThickness = 0.4f;

        // 성벽 기초를 잇는 전이선. 폭 2.5m 는 도랑이나 재질 경계선처럼 보여 0.6m 로 줄였다 (S-CLI-3NYWVN).
        private const float CombatOuterVergeWidth = 0.6f;

        private static readonly (string Name, Vector3 Position, Vector3 Size)[] CombatOuterVergeStrips =
        {
            ("OuterVerge_North", new Vector3(0f, CombatOuterGroundCenterY, 14.3f), new Vector3(49.2f, CombatOuterGroundThickness, CombatOuterVergeWidth)),
            ("OuterVerge_South", new Vector3(0f, CombatOuterGroundCenterY, -14.3f), new Vector3(49.2f, CombatOuterGroundThickness, CombatOuterVergeWidth)),
            ("OuterVerge_West", new Vector3(-24.3f, CombatOuterGroundCenterY, 0f), new Vector3(CombatOuterVergeWidth, CombatOuterGroundThickness, 28f)),
            ("OuterVerge_East", new Vector3(24.3f, CombatOuterGroundCenterY, 0f), new Vector3(CombatOuterVergeWidth, CombatOuterGroundThickness, 28f)),
        };

        // 잔디를 전이선 바로 바깥(|x| = 24.6, |z| = 14.6)까지 끌어와 지면 대부분이 잔디로 읽히게 한다.
        // 바깥 경계(x ±72, z -26~48)는 카메라 가시 범위 계산 결과라 그대로 둔다.
        private static readonly (string Name, Vector3 Position, Vector3 Size)[] CombatOuterGrassStrips =
        {
            ("OuterGrass_North", new Vector3(0f, CombatOuterGroundCenterY, 31.3f), new Vector3(144f, CombatOuterGroundThickness, 33.4f)),
            ("OuterGrass_South", new Vector3(0f, CombatOuterGroundCenterY, -20.3f), new Vector3(144f, CombatOuterGroundThickness, 11.4f)),
            ("OuterGrass_West", new Vector3(-48.3f, CombatOuterGroundCenterY, 0f), new Vector3(47.4f, CombatOuterGroundThickness, 29.2f)),
            ("OuterGrass_East", new Vector3(48.3f, CombatOuterGroundCenterY, 0f), new Vector3(47.4f, CombatOuterGroundThickness, 29.2f)),
        };

        // 팔레트는 나무(Pine_2)·바위(Rock_Medium_*)·수풀(Bush_Common_Flowers)·낮은 풀 네 계열로 제한한다.
        // 북쪽 중앙(x -21 ~ 20.5)은 의도적으로 비우고, 카메라 쪽(z <= -14)에는 큰 나무를 두지 않는다.
        private static readonly (string Name, EnvironmentVisualSlot Slot, Vector3 Position, float Scale, float Yaw)[]
            CombatOuterNatureClusters =
        {
            ("Nature_NW_Grove_A", EnvironmentVisualSlot.CombatNatureVisual, new Vector3(-31f, 0f, 20f), 1f, 25f),
            ("Nature_NW_Grove_B", EnvironmentVisualSlot.CombatNatureVisual, new Vector3(-21f, 0f, 24.5f), 0.85f, 200f),
            ("Nature_NW_Verge", EnvironmentVisualSlot.CombatNatureLowVisual, new Vector3(-27.5f, 0f, 16.5f), 1f, 110f),
            ("Nature_NE_Rocks_A", EnvironmentVisualSlot.CombatNatureRockVisual, new Vector3(28f, 0f, 19f), 1f, 300f),
            ("Nature_NE_Rocks_B", EnvironmentVisualSlot.CombatNatureRockVisual, new Vector3(20.5f, 0f, 24f), 0.9f, 145f),
            ("Nature_NE_Verge", EnvironmentVisualSlot.CombatNatureLowVisual, new Vector3(31.5f, 0f, 16.5f), 1.05f, 60f),
            ("Nature_West_Mid", EnvironmentVisualSlot.CombatNatureLowVisual, new Vector3(-29.5f, 0f, 1f), 1f, 75f),
            ("Nature_East_Mid", EnvironmentVisualSlot.CombatNatureLowVisual, new Vector3(29.5f, 0f, -2f), 0.9f, 250f),
            ("Nature_South_West", EnvironmentVisualSlot.CombatNatureLowVisual, new Vector3(-16f, 0f, -18.5f), 1f, 160f),
            ("Nature_South_East", EnvironmentVisualSlot.CombatNatureLowVisual, new Vector3(14f, 0f, -19.5f), 1.1f, 320f),
        };

        // 전장 안쪽 식생은 잡초·클로버뿐이고 벽면에서 0.2~0.8m 안쪽에만 둔다.
        // 서쪽은 약한 침식, 동쪽은 한 곳만 조금 강하게 준다.
        private static readonly (string Name, Vector3 Position, float Scale, float Yaw)[] CombatGroundCoverClusters =
        {
            ("GroundCover_West_A", new Vector3(-23.2f, 0f, 6.5f), 0.35f, 20f),
            ("GroundCover_West_B", new Vector3(-23.2f, 0f, -7.5f), 0.3f, 210f),
            ("GroundCover_East_A", new Vector3(23.25f, 0f, 4.5f), 0.55f, 120f),
            ("GroundCover_East_B", new Vector3(23.2f, 0f, -8.5f), 0.4f, 295f),
            ("GroundCover_North_A", new Vector3(-13.5f, 0f, 13.2f), 0.35f, 65f),
            ("GroundCover_South_A", new Vector3(11.5f, 0f, -13.2f), 0.3f, 240f),
        };

        private static readonly Vector3[] PreparationSpawnLocalPositions =
        {
            new(-7.7f, PlayerRootY, -12f),
            new(-5.5f, PlayerRootY, -12f),
            new(-3.3f, PlayerRootY, -12f),
            new(-1.1f, PlayerRootY, -12f),
            new(1.1f, PlayerRootY, -12f),
            new(3.3f, PlayerRootY, -12f),
            new(5.5f, PlayerRootY, -12f),
            new(7.7f, PlayerRootY, -12f),
        };
        private static readonly Vector3[] CombatSpawnLocalPositions =
        {
            new(-19f, PlayerRootY, -10f),
            new(0f, PlayerRootY, -11f),
            new(19f, PlayerRootY, -10f),
            new(21f, PlayerRootY, 0f),
            new(19f, PlayerRootY, 10f),
            new(0f, PlayerRootY, 11f),
            new(-19f, PlayerRootY, 10f),
            new(-21f, PlayerRootY, 0f),
        };

        // S-CLI-DYMR66 이 정한 상자 후보 8곳을 현재 48x28 전장에 그대로 옮겼다.
        // 무작위 2곳 선택과 상자 생성은 S15P21A606-32 의 몫이고 여기서는 후보 Transform 만 제공한다.
        private static readonly Vector3[] ChestAnchorLocalPositions =
        {
            new(7f, 0f, 8f),
            new(-7f, 0f, 8f),
            new(7f, 0f, -8f),
            new(-7f, 0f, -8f),
            new(0f, 0f, 6f),
            new(0f, 0f, -6f),
            new(12f, 0f, 0f),
            new(-12f, 0f, 0f),
        };

        // 전투 단계 조명 — 원래 씬 값을 한 번만 기억해 두고 복원에만 쓴다.
        private Light stageLight;
        private bool hasCapturedStageLighting;
        private bool appliedCombatStageLighting;
        private float capturedAmbientIntensity;
        private float capturedLightIntensity;
        private Color capturedLightColor = Color.white;

        private static OnlineCoreLoopWorldPresenter instance;

        private static readonly MatchSceneKey PreparationSceneKey = MatchSceneCatalog.DefaultPreparation;
        private static readonly MatchSceneKey CombatSceneKey = MatchSceneCatalog.DefaultCombat;

        // 정적 필드는 기존 Editor 계약 테스트가 확인하는 기본값이다. 실제 매치에서는 Host가
        // 스냅샷으로 확정한 키를 사용해 향후 대기실 맵 선택을 같은 로딩 경로에 태운다.
        private MatchSceneKey boundPreparationSceneKey = MatchSceneCatalog.DefaultPreparation;
        private MatchSceneKey boundCombatSceneKey = MatchSceneCatalog.DefaultCombat;

        private readonly IMatchSceneProvider mapSceneProvider = new BuiltInMatchSceneProvider();
        private Task<MatchSceneLoadResult> preparationSceneLoad;
        private Task<MatchSceneLoadResult> combatSceneLoad;
        private readonly List<NetworkCombatantMotion> placementCandidates = new();

        private Transform preparationRoot;
        private Transform[] preparationSpawnAnchors;
        private Transform[] combatSpawnAnchors;
        private Transform combatRoot;
        private Transform combatRuntimeRoot;
        private Transform preparationRuntimeRoot;
        /// <summary>
        /// 준비 라운드 연습 더미의 체력 경계. NetworkObject 가 아니라 이 표현 계층이 소유하므로,
        /// Host 의 공용 피해 해석기를 여기서 직접 연결한다 (S15P21A606-167).
        /// </summary>
        private CombatantHealth preparationTrainingDummy;
        private Transform legacyArenaRoot;
        private Camera worldCamera;
        private CombatVisibilityPresenter visibility;
        private Collider[] combatTerrainBlockers = Array.Empty<Collider>();

        /// <summary>준비 국면 미니맵이 쓰는 준비 맵 차폐물. 전투 것과 섞지 않는다.</summary>
        private Collider[] preparationTerrainBlockers = Array.Empty<Collider>();

        // -------------------------------------------------------------
        // 매치 시작 프레임 분산 (S15P21A606-275)
        //
        // ⚠️ **매치 시작 작업이 한 프레임에 전부 몰려 있었다.** 맵 결합 → 런타임 루트 생성 →
        //    차폐물 수집 → 맵 활성 → 전투원 8기 스폰 → 배치 → 시야·안개 조립이 같은 `Update`
        //    안에서 끝났다. client 실측 한 프레임 **90.28ms**(`AcquireInstance` 8회 20.4ms /
        //    `Activate` 143회 + `Deactivate` 82회 27.7ms / `Instantiate` 78회 9.2ms / self 24.2ms),
        //    host 는 최대 **1347ms** 였다(2026-09-22 프로파일러).
        //
        //    그 한 프레임이 Fusion 클라 로컬틱을 **확정 서버틱 +64(1초) 천장**으로 밀어넣는다.
        //    천장에 박히면 Fusion 이 이번 프레임의 틱 수를 0 으로 내리고, 그러면 `OnInput` 이
        //    호출되지 않아 **입력이 아예 생성되지 않는다** — "애니메이션은 나오는데 안 움직인다" 의
        //    최종 원인이다. 64Hz 이므로 프레임 하나가 곧 64틱의 리드다.
        //
        //    그래서 **한 프레임에 무거운 단계를 하나만** 전진시킨다. 총 소요 시간은 오히려 늘지만
        //    (프레임 수가 늘어난다) 어느 프레임도 천장을 만들지 않는다. 이 구간은 Loading 국면이
        //    덮고 있어 몇 프레임 늘어나는 것은 화면에 보이지 않는다.
        // -------------------------------------------------------------

        /// <summary>
        /// 월드 조립을 한 프레임에 하나씩만 전진시키는 단계. 순서를 바꾸면 안 된다 —
        /// 뒤 단계가 앞 단계의 결과를 쓴다.
        /// </summary>
        private enum WorldAssemblyStep
        {
            /// <summary>맵 루트를 원점으로 옮기고 앵커·전장 경계를 검증한다.</summary>
            BindMapScenes = 0,

            /// <summary>준비맵의 원거리 장식 컬링·상시 안내판 정리를 적용한다 (S15P21A606-292).</summary>
            /// <remarks>
            /// ⚠️ <b>dev 에서는 맵 결합 직후 같은 프레임에 돌던 것을 떼어 단계로 만들었다.</b>
            /// 둘 다 준비맵 계층을 통째로 훑으므로 결합 프레임에 얹으면 그 프레임이 다시
            /// 무거워진다 — D-CLI-B47KX7 이 "무거운 일은 새 단계로 넣는다" 고 정한 그대로다.
            /// 실제 비용은 <c>asmMs[1]</c> 이 다음 회차에 알려준다.
            /// </remarks>
            ApplyPreparationRenderPolicy,

            /// <summary>런타임 루트와 준비 라운드 연습 더미를 만든다.</summary>
            CreateRuntimeRoots,

            /// <summary>전투 맵 차폐물을 모은다.</summary>
            CollectCombatBlockers,

            /// <summary>준비 맵 차폐물·카메라를 모으고 월드를 연다.</summary>
            Finish,
        }

        /// <summary>조립이 어디까지 갔는가. <see cref="ClearSceneReferences"/> 가 처음으로 되돌린다.</summary>
        private WorldAssemblyStep worldAssemblyStep;

        /// <summary>
        /// 무거운 조립 단계를 마지막으로 전진시킨 프레임. <c>Update</c> 가 내리는 불린이 아니라
        /// <b>프레임 도장</b>이다 — <c>OnCoreLoopChanged</c>·<c>OnSceneLoaded</c> 도
        /// <see cref="ApplySnapshot"/> 을 직접 부르므로, 같은 프레임에 두 번 들어와도
        /// 단계가 두 번 전진하지 않아야 한다.
        /// </summary>
        private int worldAssemblyFrame = -1;

        /// <summary>이번 프레임에 무거운 조립 단계를 이미 하나 했는가.</summary>
        private bool WorldAssemblyAdvancedThisFrame => worldAssemblyFrame == Time.frameCount;

        /// <summary>이번 프레임 몫의 조립 단계를 썼다고 도장을 찍는다.</summary>
        private void MarkWorldAssemblyAdvanced() => worldAssemblyFrame = Time.frameCount;

        /// <summary>전장 경계는 결합 첫 단계에서 검증하고 마지막 단계의 로그에서 쓴다.</summary>
        private CombatZoneBounds pendingFieldBounds;

        /// <summary>
        /// 국면 전환 로그를 아직 남기지 않았는가. 전환을 감지한 프레임과 전환이 <b>끝나는</b>
        /// 프레임이 갈렸으므로(스폰·배치를 다음 프레임으로 미룬다) 플래그로 이어 붙인다 —
        /// 그러지 않으면 "Default 맵 전환" 로그가 영영 나오지 않는다.
        /// </summary>
        private bool phaseTransitionLogPending;

        /// <summary>
        /// 지금 결합된 시야가 전투 맵 차폐물을 쓰고 있는가. 국면이 바뀌어 차폐물 집합이 달라지면
        /// <c>TryConfigureVisibility</c> 는 같은 카메라·관찰자를 보고 재결합을 건너뛰므로,
        /// 이 값이 뒤집힐 때 강제로 다시 결합해야 미니맵이 옛 맵 경계를 계속 그리지 않는다.
        /// </summary>
        private bool visibilityUsesCombatTerrain;
        private bool headlessBotWorldBound;
        private readonly List<Transform> visibilityActorRoots = new();
        private Transform localPlayer;

        /// <summary>
        /// 로컬 전투원의 모터. <b>카메라가 유령을 따라가기 위해</b> 함께 들고 있는다
        /// (S15P21A606-188 테스트 중 발견).
        /// </summary>
        /// <remarks>
        /// ⚠️ 빈사가 되면 <b>몸은 쓰러진 자리에 남고 유령만 움직인다</b>(D-052 · D-063).
        /// <c>localPlayer.position</c> 만 보면 카메라가 시체에 묶여 유령을 놓친다.
        /// </remarks>
        private CombatantMotor localPlayerMotor;

        /// <summary>
        /// 이번 프레임에 카메라를 따라가야 하는가, 그리고 그 기준 스냅샷.
        /// <see cref="Update"/> 가 세우고 <see cref="LateUpdate"/> 가 소비한다.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>추적을 <see cref="Update"/> 에서 하면 안 된다.</b> Fusion 의 기본 업데이터는
        /// <c>NetworkTransform</c> 보간을 실제로 <c>transform</c> 에 적용하는 <c>RenderInternal</c> 을
        /// <b>스크립트 Update 다음, LateUpdate 직전</b>에 부른다. Update 에서 따라가면 카메라는 늘
        /// <b>한 프레임 낡은</b> 위치를 읽고, 그 어긋남이 프레임마다 달라져 화면 전체가 떨린다
        /// (S15P21A606-273 · S15P21A606-264).
        /// <para/>
        /// ⚠️ <b>호스트도 떨린다.</b> 권위 피어가 소유한 오브젝트도 <c>RenderTimeframe.Local</c> 에서
        /// 틱 사이를 보간해 렌더된다(<c>RenderSource.Interpolated</c> · <c>Runner.LocalAlpha</c>).
        /// 60Hz 틱과 가변 렌더 프레임을 메우려면 그래야 하기 때문이다. 클라이언트는 <c>Remote</c>
        /// 타임프레임이라 증분이 네트워크 도착에 따라 더 불균등할 뿐, <b>정도 차이지 유무 차이가 아니다</b>.
        /// </remarks>
        private bool followPending;
        private OnlineCoreLoopSnapshot followSnapshot;
        private OnlineCoreLoopPhase appliedPhase = (OnlineCoreLoopPhase)(-1);
        private int appliedRound = -1;
        private bool placementPending;
        private bool arenaBoundsRefreshPending;

        /// <summary>
        /// 권위가 없는 피어가 원래 맵을 켜 둔 채 내 전투원이 목적지 맵에 도착하기를 기다리는 중인가.
        /// </summary>
        /// <remarks>
        /// 단계 소식과 Host 의 순간이동은 따로 도착한다. 소식만 먼저 받아 원래 맵을 끄면 내 캐릭터 예측이 바닥 없이
        /// 떨어졌다가 순간이동으로 끌려갔다 — 전용 서버에서는 방장도 클라이언트라 모두가 봤다(2026-09-25 Preview,
        /// S15P21A606-326). 두 맵은 ±60m 떨어져 있어 잠깐 함께 켜 두어도 겹치지 않는다.
        /// </remarks>
        private bool remoteArrivalPending;
        private float remoteArrivalWaitSeconds;

        /// <summary>도착을 기다리는 최대 시간(초). 관전·유령처럼 옮겨지지 않는 경우에도 맵 전환이 멈추지 않게 한다.</summary>
        private const float RemoteArrivalTimeoutSeconds = 3f;

        /// <summary>마지막으로 남긴 배치 막힘 이유. 같은 이유를 매 프레임 다시 찍지 않는다 (S15P21A606-306).</summary>
        private string placementBlockedReason = string.Empty;

        /// <summary>저작 맵 두 개가 로드되고 루트·앵커·경계 검증까지 끝났는가.</summary>
        private bool worldReady;

        /// <summary>결합에 실패한 상태. 같은 이유를 매 틱 다시 찍지 않으려고 한 번만 기록하고 닫는다.</summary>
        private bool worldBindingFailed;
        private string worldBindingFailureReason = string.Empty;
        private string boundMatchId = string.Empty;
        private Task mapRelease;
        private bool isQuitting;
        private bool visibilityConfigurationFailureReported;
        private int loggedLocalPreparationRound = -1;
        private readonly List<GameObject> combatChestVisuals = new();
        private readonly List<CombatChestInteractionBounds> combatChestInteractionBounds = new();
        private sealed class ProductChestBinding
        {
            public ProductChestBinding(CombatantId combatantId, ICombatInteractionCommandSink sink)
            {
                CombatantId = combatantId;
                Sink = sink;
            }

            public CombatantId CombatantId { get; }
            public ICombatInteractionCommandSink Sink { get; }
        }

        private readonly Dictionary<NetworkCombatantMotion, ProductChestBinding> productChestSinks = new();
        private CombatChestAuthorityRuntime productChestAuthority;
        private NetworkCombatantMotion productChestPublisher;
        private int productChestRound = -1;

        // 상자 아이템 추첨은 Host 만 한다(S-GD-RVVB4H §7) — 룰렛 추첨처럼 Host 시계로 한 번 시드한다.
        private DeterministicRandom productChestRewardRandom;
        private string appliedChestMatchId = string.Empty;
        private int appliedChestStateRound = -1;
        private int appliedChestStateVersion = -1;
        private readonly int[] appliedChestClaimSequences = new int[CombatChestsPerRound];

        // 내 거부·취소 되돌림의 마지막 순번 (O-028).
        //
        // ⚠️ -1 로 시작하면 안 된다 — 복제 필드 기본값이 0 이라 스폰 직후 없던 거부가 한 번 뜬다.
        //
        // ⚠️ 전투원이 바뀌면 **그 시점의 복제값으로 맞추고 그 프레임은 건너뛴다.** 0 으로 되돌리면
        //    안 된다 — 복제 필드가 이미 올라가 있으면 지난 사건이 새 전투원에서 재생된다.
        //    반대로 아무것도 안 하면 새 매치·재스폰의 복제 순번이 0 부터 시작하는데 여기만 지난
        //    값을 들고 있어 **거부가 영원히 안 뜬다**(새 1 이 옛 3 보다 작아 계속 걸러진다).
        //    프레젠터는 싱글턴이라 매치를 넘어 산다.
        private NetworkCombatantMotion appliedChestFeedbackSource;
        private int appliedChestRejectSequence;
        private int appliedChestCancelSequence;
        private byte lastReportedLoadingPercent = byte.MaxValue;

        /// <summary>전투 맵 준비를 마지막으로 알린 라운드와 다시 알릴 시각. 신뢰 채널이지만 대기 중에는 2초마다 다시 보낸다.</summary>
        private int lastCombatEntryReadyRound;
        private float combatEntryReadyResendAt;
        private const float CombatEntryReadyResendSeconds = 2f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (instance != null)
            {
                return;
            }

            var presenterObject = new GameObject("[OnlineCoreLoopWorldPresenter]");
            DontDestroyOnLoad(presenterObject);
            instance = presenterObject.AddComponent<OnlineCoreLoopWorldPresenter>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            EventBus.Subscribe<OnlineCoreLoopChanged>(OnCoreLoopChanged);
        }

        private void OnApplicationQuit() => isQuitting = true;

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            EventBus.Unsubscribe<OnlineCoreLoopChanged>(OnCoreLoopChanged);

            // 중복 인스턴스는 Awake 에서 스스로를 버린다. 그 OnDisable 이 진짜 소유자의 맵을 내리면 안 된다.
            // 종료 중에는 씬 언로드가 의미 없고 Provider 작업만 붕 뜨므로 참조만 정리한다.
            ReleaseMatchWorld(unloadMapScenes: instance == this && !isQuitting);
        }

        // ─────────────────────────────────────────────────────────────
        // Update 구간 마커 (S15P21A606-275, 7차)
        //
        // ⚠️ **이 Update 한 호출이 한 프레임에 156.7MB 를 할당하고 462ms 를 쓴다**
        //    (2026-09-22 프로파일러 실측, self 367ms · GC.Alloc 29,992회). 그 프레임이 클라를
        //    Fusion 예측 한계(-64)로 밀어넣고, 거기 박히면 틱이 0 이 되어 회복하지 못한다 —
        //    "클라가 안 움직인다" 의 최종 원인이 이 한 줄이다.
        //
        //    그런데 프로파일러에는 155.7MB 가 **이름 없는 `GC.Alloc` 노드 하나**로 뭉쳐 있다.
        //    아래 호출들 중 어느 것인지 가릴 수 없다. Deep Profile 은 프레임을 수십 배 늘려
        //    이 프레임 자체를 다른 것으로 만들어 버리므로, **구간 마커로 가른다.**
        //
        // ⚠️ `ProfilerMarker` 는 `ENABLE_PROFILER` 가 없으면 컴파일에서 사라진다 —
        //    출시 빌드의 동작·비용에 영향이 없다. 원인을 잡으면 이 마커들은 남겨도 되고
        //    지워도 된다(남기는 쪽이 다음 회귀를 싸게 잡는다).
        // ─────────────────────────────────────────────────────────────

        private static readonly ProfilerMarker CameraMarker = new("BettingRoyale.World.EnsureWorldCamera");
        private static readonly ProfilerMarker SnapshotMarker = new("BettingRoyale.World.ApplySnapshot");
        private static readonly ProfilerMarker PracticeMarker = new("BettingRoyale.World.RefreshPracticeTarget");
        private static readonly ProfilerMarker VisibilityMarker = new("BettingRoyale.World.RefreshVisibility");
        private static readonly ProfilerMarker ChestAuthorityMarker = new("BettingRoyale.World.ChestAuthority");
        private static readonly ProfilerMarker ChestStateMarker = new("BettingRoyale.World.ChestState");
        private static readonly ProfilerMarker ChestFeedbackMarker = new("BettingRoyale.World.ChestFeedback");
        private static readonly ProfilerMarker ArenaBoundsMarker = new("BettingRoyale.World.ArenaBounds");

        private void Update()
        {
            // 추적 요청은 매 프레임 새로 세운다. 아래 어느 early return 으로 빠져도 이번 프레임 추적은 없다.
            followPending = false;

            if (!IsProductMatchSceneLoaded())
            {
                // 매치 씬이 사라졌다면 Unity 가 Additive 맵도 함께 내렸다. 죽은 루트를 다음 매치로 넘기지 않는다.
                if (worldReady
                    || worldBindingFailed
                    || preparationSceneLoad != null || combatSceneLoad != null
                    || visibility != null
                    || productChestAuthority != null
                    || productChestSinks.Count > 0)
                {
                    ReleaseMatchWorld(unloadMapScenes: true);
                }

                return;
            }

            if (!OnlineCoreLoopCoordinator.TryGetSnapshot(out OnlineCoreLoopSnapshot snapshot)
                || !snapshot.IsMatchActive)
            {
                if (visibility != null && visibility.IsConfigured)
                {
                    visibility.Unbind();
                }
                if (productChestAuthority != null || productChestSinks.Count > 0)
                {
                    ClearProductChestAuthority();
                }
                HideCombatChestVisuals();
                appliedPhase = (OnlineCoreLoopPhase)(-1);
                appliedRound = -1;
                return;
            }

            // 시야·미니맵이 같은 프레임에 카메라를 볼 수 있도록 해석만 먼저 끝낸다. 추적은 LateUpdate 다.
            using (CameraMarker.Auto()) EnsureWorldCamera();
            using (SnapshotMarker.Auto()) ApplySnapshot(snapshot);

            // ⚠️ **로딩 진행률은 조립 프레임에도 보낸다.** 아래 게이트보다 앞에 둔다 —
            //    조립이 한 단계씩 가는 동안 막대가 멈추면 "멈췄다" 로 보인다.
            //    값이 바뀔 때만 보내므로 매 프레임 불러도 싸다 (S15P21A606-292).
            ReportLocalMatchLoading(snapshot);
            ReportLocalCombatEntry(snapshot);

            // ⚠️ **조립 단계를 한 프레임에 둘 이상 하지 않는다.** 시야·안개 조립은 그 자체로
            //    한 프레임에 130ms 를 쓰므로(2026-09-22 실측), 방금 맵을 켜거나 전투원을 스폰한
            //    프레임에 겹쳐 놓으면 그 프레임 하나가 예측 천장을 만든다. 다음 프레임이 이어서
            //    한다 — 이 경로는 매 프레임 다시 들어오므로 한 프레임 미루는 것이 곧 분산이다.
            if (WorldAssemblyAdvancedThisFrame)
            {
                followSnapshot = snapshot;
                followPending = true;
                return;
            }

            using (PracticeMarker.Auto()) RefreshPreparationPracticeTarget();
            using (VisibilityMarker.Auto()) RefreshProductVisibility(snapshot);
            using (ChestAuthorityMarker.Auto()) UpdateProductChestAuthority(snapshot);
            using (ChestStateMarker.Auto()) ApplyReplicatedChestState(snapshot);
            using (ChestFeedbackMarker.Auto()) ApplyReplicatedChestFeedback(snapshot);
            if (!placementPending && arenaBoundsRefreshPending)
            {
                using (ArenaBoundsMarker.Auto())
                {
                    arenaBoundsRefreshPending = !TryRefreshCombatantArenaBounds();
                }
            }

            followSnapshot = snapshot;
            followPending = true;
        }

        /// <summary>
        /// 전투 진입 대기 중이면, 전투 맵이 켜지고 내 전투원이 그 맵에 섰을 때 Host 에 준비를 알린다
        /// (S15P21A606-326 · S-GD-C66RN8). 모두가 알려야 카운트다운이 시작된다.
        /// </summary>
        private void ReportLocalCombatEntry(in OnlineCoreLoopSnapshot snapshot)
        {
            if (snapshot.Phase != OnlineCoreLoopPhase.Combat || !snapshot.CombatEntry.IsWaiting
                || !NetworkManager.HasInstance)
            {
                return;
            }

            if (appliedPhase != OnlineCoreLoopPhase.Combat || appliedRound != snapshot.CurrentRound
                || placementPending || remoteArrivalPending
                || combatRoot == null || !combatRoot.gameObject.activeInHierarchy
                || !HasLocalCombatantArrived(combatRoot, preparationRoot))
            {
                return;
            }

            float now = Time.unscaledTime;
            if (lastCombatEntryReadyRound == snapshot.CurrentRound && now < combatEntryReadyResendAt) return;
            lastCombatEntryReadyRound = snapshot.CurrentRound;
            combatEntryReadyResendAt = now + CombatEntryReadyResendSeconds;
            NetworkManager.Instance.SendCombatEntryReady(snapshot.CurrentRound);
        }

        private void ReportLocalMatchLoading(in OnlineCoreLoopSnapshot snapshot)
        {
            if (snapshot.Phase != OnlineCoreLoopPhase.Loading || !NetworkManager.HasInstance) return;

            byte percent = 10; // InGame 씬 진입 완료
            if (preparationSceneLoad != null || combatSceneLoad != null) percent = 45;
            if (preparationRoot != null && combatRoot != null) percent = 75;
            if (worldReady && !worldBindingFailed) percent = 90;

            // ⚠️ **여기서 권위를 보지 않는다** (S15P21A606-297). 예전엔 `AuthorityInventory != null`
            //    로 전투원 준비를 확인했는데, 그 속성은 `HasStateAuthority` 인 피어에게만 인벤토리를
            //    준다 — **참가자 클라이언트는 자기 캐릭터라도 항상 `null`** 이라 90% 에서 멈췄다.
            //    Host 100 / 참가자 90 의 평균이 **95%** 라서 `LoadingProgressAverage() >= 0.999f`
            //    게이트가 영원히 열리지 않았다. 봇은 평균에서 빠지므로 혼자 할 때만 멀쩡해 보였다.
            //    준비율은 각 피어가 **자기 화면 기준**으로 보고한다 (S-UX-5AES3H).
            NetworkManager network = NetworkManager.Instance;
            int localPlayerId = network.LocalTrainingPlayerId;
            if (percent >= 90
                && localPlayerId > 0
                && network.TryGetCombatant(localPlayerId, out NetworkCombatantMotion combatant)
                && combatant != null
                && combatant.IsPresentationReady
                && OnlineEconomyCoordinator.Instance != null)
            {
                percent = 100;
            }

            if (percent == lastReportedLoadingPercent) return;
            lastReportedLoadingPercent = percent;
            network.SendMatchLoadingProgress(percent);
        }

        /// <summary>
        /// Fusion 이 <c>NetworkTransform</c> 보간을 <c>transform</c> 에 적용한 <b>뒤</b>에 카메라를 옮긴다
        /// (S15P21A606-273). 훈련장·CombatLab·Local Play 도 같은 단계에서 추적하므로 경로마다
        /// 추적 시점이 갈리지 않는다.
        /// </summary>
        private void LateUpdate()
        {
            if (!followPending)
            {
                return;
            }

            FollowLocalPlayer(followSnapshot);
        }

        /// <summary>
        /// ⚠️ <b>Additive 맵 로드도 이 콜백을 부른다.</b> 예전처럼 무조건 참조를 지우면 방금 결합한
        /// 월드가 자기 맵 로드에 지워져 EnsureWorld 가 영원히 반복된다. Single 전환만 월드를 무너뜨린다.
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive)
            {
                return;
            }

            // Single 전환은 Additive 맵까지 이미 파괴했다. 우리는 남은 참조만 놓는다.
            ReleaseMatchWorld(unloadMapScenes: false);

            // 매치 씬에 막 들어왔지만 맵은 아직 Additive 로 붙지 않았다. 씬에 저장된 카메라의
            // clearFlags 를 그대로 두면 그 사이에 Unity 기본 하늘이 보인다 (S15P21A606-199).
            if (scene.name == SceneName.InGame)
            {
                SharedCombatCamera.SuppressSkyboxUntilWorldReady(ResolveWorldCamera());
            }

            if (scene.name == SceneName.InGame
                && OnlineCoreLoopCoordinator.TryGetSnapshot(out OnlineCoreLoopSnapshot snapshot)
                && snapshot.IsMatchActive)
            {
                ApplySnapshot(snapshot);
            }
        }

        private void OnCoreLoopChanged(OnlineCoreLoopChanged gameEvent)
        {
            if (IsProductMatchSceneLoaded() && gameEvent.Snapshot.IsMatchActive)
            {
                ApplySnapshot(gameEvent.Snapshot);
            }
        }

        internal static bool IsReadyForSimulation(in OnlineCoreLoopSnapshot snapshot) =>
            IsReadyForSimulation(snapshot.Phase, snapshot.CurrentRound);

        internal static bool IsReadyForSimulation(OnlineCoreLoopPhase phase, int round) =>
            instance != null && instance.worldReady && !instance.worldBindingFailed && !instance.placementPending
            && instance.appliedPhase == phase && instance.appliedRound == round;

        /// <summary>
        /// 매치 국면을 열 수 있는 최소 월드 게이트. 루트·앵커·전투 경계 검증이 끝난 뒤에만 true다.
        /// </summary>
        internal static bool IsWorldReadyForMatch() =>
            instance != null
            && instance.worldReady
            && !instance.worldBindingFailed
            && instance.preparationRoot != null
            && instance.combatRoot != null
            && instance.preparationSpawnAnchors != null
            && instance.combatSpawnAnchors != null;

        /// <summary>
        /// 이번 매치의 참가자 전체를 입장 순서대로 돌려준다. <b>봇을 포함한다</b> (S15P21A606-188).
        /// </summary>
        /// <remarks>
        /// 매치 명부가 아직 비어 있으면(매치 시작 전) 접속자 목록으로 떨어진다 — 그때는 봇이 없으므로
        /// 두 목록이 같다.
        /// </remarks>
        private static bool TryResolveMatchParticipantIds(out int[] ids)
        {
            ids = Array.Empty<int>();
            if (!NetworkManager.HasInstance) return false;

            NetworkManager network = NetworkManager.Instance;
            IReadOnlyDictionary<int, int> frozen = network.MatchLobbyJoinOrders;
            if (frozen != null && frozen.Count > 0)
            {
                var participants = new int[frozen.Count];
                var index = 0;
                foreach (int id in frozen.Keys) participants[index++] = id;
                SortPlayerIdsByJoinOrder(participants);
                ids = participants;
                return true;
            }

            if (!network.TryGetHostActivePlayerIds(out int[] live)) return false;
            SortPlayerIdsByJoinOrder(live);
            ids = live;
            return true;
        }

        /// <summary>
        /// 입장 순서로 정렬된 매치 명부에서 <b>아직 매치에 남은 참가자만</b> 순서를 유지해 고른다 (S-GD-BYVCAF).
        /// 이탈자를 빼고 앞으로 당기므로 남은 참가자가 앞 슬롯부터 채운다. 명부 자체는 바꾸지 않는다 —
        /// 순위·점수는 이탈자까지 명부로 계산한다.
        /// </summary>
        public static int[] ResolvePlacementParticipantIds(IReadOnlyList<int> rosterIds, Predicate<int> isStillInMatch)
        {
            if (rosterIds == null || rosterIds.Count == 0) return Array.Empty<int>();
            if (isStillInMatch == null) return new List<int>(rosterIds).ToArray();

            var remaining = new List<int>(rosterIds.Count);
            for (int i = 0; i < rosterIds.Count; i++)
                if (isStillInMatch(rosterIds[i])) remaining.Add(rosterIds[i]);
            return remaining.ToArray();
        }

        /// <summary>
        /// 명부의 이 참가자가 아직 매치에 남아 있는가. 사람은 Host 의 현재 접속자에 있어야 하고,
        /// 봇은 피어가 아니라 떠날 수 없으므로 항상 남아 있다. 접속자 목록을 못 읽으면 전원을 남은 것으로 본다 —
        /// 그러면 옛 동작(명부 전체와 대조)으로 돌아갈 뿐 없는 이탈을 지어내지 않는다.
        /// </summary>
        private static bool IsParticipantStillInMatch(int participantId)
        {
            if (NetworkCombatantMotion.IsLobbyBotActorId(participantId)) return true;
            if (!NetworkManager.HasInstance
                || !NetworkManager.Instance.TryGetHostActivePlayerIds(out int[] active)) return true;
            return Array.IndexOf(active, participantId) >= 0;
        }

        /// <summary>
        /// 배치를 이번 프레임 미루고 그 이유를 남긴다. 배치는 성공할 때까지 매 프레임 다시 오므로 이유가
        /// 바뀔 때만 한 번 찍는다 — 예전에는 여기가 전부 조용한 <c>return false</c> 라, 준비 단계가 멈춰도
        /// Host 로그에 아무 줄도 없었다 (S15P21A606-306).
        /// </summary>
        private bool BlockPlacement(in OnlineCoreLoopSnapshot snapshot, string reason)
        {
            string key = $"{snapshot.Phase}/{snapshot.CurrentRound}: {reason}";
            if (key == placementBlockedReason) return false;
            placementBlockedReason = key;
            Debug.LogWarning(
                $"[OnlineCoreLoopWorldPresenter] Host 권위 배치 대기 — {snapshot.Phase}, " +
                $"{snapshot.CurrentRound}라운드: {reason}. 성공할 때까지 매 프레임 다시 시도합니다.");
            return false;
        }

        private static void SortPlayerIdsByJoinOrder(int[] playerIds)
        {
            if (playerIds == null || playerIds.Length < 2 || !NetworkManager.HasInstance)
            {
                return;
            }

            NetworkManager network = NetworkManager.Instance;
            Array.Sort(playerIds, (left, right) => ComparePlayerIdsByJoinOrder(network, left, right));
        }

        private static int ComparePlayerIdsByJoinOrder(NetworkManager network, int left, int right)
        {
            if (left == right) return 0;

            bool hasLeft = network.TryGetLobbyJoinOrder(left, out int leftOrder);
            bool hasRight = network.TryGetLobbyJoinOrder(right, out int rightOrder);
            if (hasLeft && hasRight)
            {
                int orderComparison = leftOrder.CompareTo(rightOrder);
                if (orderComparison != 0) return orderComparison;
            }
            else if (hasLeft != hasRight)
            {
                return hasLeft ? -1 : 1;
            }

            // 입장 순서를 아직 모르는 진단/스폰 초기 순간에도 정렬은 결정적이어야 한다.
            return left.CompareTo(right);
        }

        internal static bool TryResolveInitialSpawn(int playerId, out Pose pose)
        {
            pose = default;
            if (instance == null || !instance.worldReady || !NetworkManager.HasInstance) return false;

            // ⚠️ <b>접속자 목록이 아니라 매치 명부에서 자리를 찾는다</b> (S15P21A606-188).
            //    대기방 봇은 피어가 아니라 ActivePlayers 에 영원히 없다 — 예전 코드로는 봇이
            //    자리를 못 받아 스폰이 조용히 건너뛰어졌고, 명부에는 8명인데 전장에는 1명만
            //    섰다("Host 권위 배치 완료 … 1명"). 명부는 매치 시작 시점에 얼린 참가자 전체다.
            if (!TryResolveMatchParticipantIds(out int[] ids)) return false;
            int slot = Array.IndexOf(ids, playerId);
            if (slot < 0) return false;
            bool preparing = instance.appliedPhase is OnlineCoreLoopPhase.Loading or OnlineCoreLoopPhase.Preparation;
            if (!preparing) slot = ResolveCombatSpawnIndex(slot, instance.appliedRound);
            return MatchSceneContentResolver.TryGetSpawnPose(
                preparing ? instance.preparationRoot : instance.combatRoot,
                preparing ? MatchScenePhase.Preparation : MatchScenePhase.Combat, slot, out pose, out _);
        }

        /// <summary>
        /// 제품 UI가 월드 결합 실패를 로딩·축복·전투 화면과 분리해 표시할 수 있게 한다.
        /// 내부 진단 문자열만 돌려주며, 실패한 월드의 부분 루트는 사용 가능 상태로 노출하지 않는다.
        /// </summary>
        internal static bool TryGetWorldBindingFailure(out string reason)
        {
            reason = instance?.worldBindingFailureReason ?? string.Empty;
            return instance != null
                && instance.worldBindingFailed
                && !string.IsNullOrEmpty(reason);
        }

        private void ApplySnapshot(in OnlineCoreLoopSnapshot snapshot)
        {
            // 맵을 아직 읽지 못한 동안에도 기존 InGame/Arena 바닥을 남겨두지 않는다. 그 바닥은
            // 제품 맵의 대체물이 아니며, 특히 원격 Client가 들판에 놓인 것처럼 보이게 만든다.
            HideLegacyArenaRoot();

            // 이벤트 경로도 여기로 들어온다. 이번 프레임 몫을 이미 썼으면 다음 프레임에 이어서 한다 —
            // 그러지 않으면 `Update` 와 `OnCoreLoopChanged` 가 한 프레임에 두 단계를 밀어 버린다.
            if (WorldAssemblyAdvancedThisFrame)
            {
                return;
            }

            if (!EnsureWorld(snapshot))
            {
                return;
            }

            bool phaseChanged = snapshot.Phase != appliedPhase || snapshot.CurrentRound != appliedRound;
            if (!phaseChanged && !placementPending && !remoteArrivalPending) return;
            bool showPreparation = snapshot.Phase is OnlineCoreLoopPhase.Loading or OnlineCoreLoopPhase.Preparation;
            Transform destination = showPreparation ? preparationRoot : combatRoot;
            Transform source = showPreparation ? combatRoot : preparationRoot;
            destination.gameObject.SetActive(true);
            Physics.SyncTransforms();
            if (phaseChanged)
            {
                // 보이는 맵이 실제로 바뀌는 전환에서만 기다린다(Loading → Preparation 은 같은 맵이다).
                bool wasShowingPreparation = appliedPhase is OnlineCoreLoopPhase.Loading or OnlineCoreLoopPhase.Preparation;
                bool mapChanges = (int)appliedPhase >= 0 && wasShowingPreparation != showPreparation;
                remoteArrivalPending = mapChanges && NetworkManager.HasInstance && !NetworkManager.Instance.IsHost
                    && source != null && source.gameObject.activeSelf;
                remoteArrivalWaitSeconds = 0f;
                appliedPhase = snapshot.Phase;
                appliedRound = snapshot.CurrentRound;
                ApplyCombatRoundRunning(snapshot);
                ClearConsumablesForRoundEnd(snapshot);
                RefreshCombatChestVisuals(combatRoot, snapshot);
                placementPending = snapshot.Phase is OnlineCoreLoopPhase.Loading or OnlineCoreLoopPhase.Preparation or OnlineCoreLoopPhase.Combat;
                arenaBoundsRefreshPending = true;
                phaseTransitionLogPending = true;

                // ⚠️ **맵 활성·상자 표현과 전투원 스폰을 같은 프레임에 하지 않는다.**
                //    맵 루트 하나를 켜면 하위 수백 개가 활성 전이하고(`Activate` 143회 17.4ms),
                //    스폰은 `INetworkObjectProvider.AcquireInstance` 8회로 20.4ms 다(2026-09-22 실측).
                //    `placementPending` 이 켜져 있으면 이 경로는 매 프레임 다시 들어오므로,
                //    여기서 끊어도 다음 프레임이 그대로 이어서 한다.
                if (placementPending)
                {
                    MarkWorldAssemblyAdvanced();
                    return;
                }
            }

            // 이번 프레임이 스폰·배치를 맡았는가. 맡았다면 시야 조립은 다음 프레임으로 미룬다.
            bool placingThisFrame = placementPending;
            if (NetworkManager.HasInstance) NetworkManager.Instance.EnsureProductCombatants();
            // Keep the source floor until all currently connected participants have a destination.
            if (placementPending) placementPending = !TryPlaceAuthorityPlayers(snapshot);
            if (placementPending)
            {
                MarkWorldAssemblyAdvanced();
                return;
            }

            if (placingThisFrame) MarkWorldAssemblyAdvanced();
            if (remoteArrivalPending)
            {
                remoteArrivalWaitSeconds += Time.unscaledDeltaTime;
                if (!HasLocalCombatantArrived(destination, source)
                    && remoteArrivalWaitSeconds < RemoteArrivalTimeoutSeconds)
                {
                    return;
                }

                remoteArrivalPending = false;
            }

            preparationRoot.gameObject.SetActive(showPreparation);
            preparationRuntimeRoot.gameObject.SetActive(showPreparation);
            combatRoot.gameObject.SetActive(!showPreparation);
            combatRuntimeRoot.gameObject.SetActive(!showPreparation);
            SceneManager.SetActiveScene(destination.gameObject.scene);
            Physics.SyncTransforms();
            if (phaseTransitionLogPending)
            {
                phaseTransitionLogPending = false;
                Debug.Log($"[OnlineCoreLoopWorldPresenter] Default 맵 전환 — {snapshot.Phase}, 라운드={snapshot.CurrentRound}");
            }
        }

        /// <summary>
        /// 내 전투원이 원래 맵보다 목적지 맵 쪽에 서 있는가. 전투원이 없으면(관전 등) 기다릴 대상이 없으므로 도착으로 본다.
        /// </summary>
        private static bool HasLocalCombatantArrived(Transform destination, Transform source)
        {
            if (destination == null || source == null || !NetworkManager.HasInstance) return true;
            NetworkManager network = NetworkManager.Instance;
            int localPlayerId = network.LocalTrainingPlayerId;
            if (localPlayerId <= 0 || !network.TryGetCombatant(localPlayerId, out NetworkCombatantMotion combatant)
                || combatant == null)
            {
                return true;
            }

            Vector3 position = combatant.transform.position;
            return (position - destination.position).sqrMagnitude < (position - source.position).sqrMagnitude;
        }

        /// <summary>
        /// 저작된 준비·전투 Map Scene 두 개를 로드해 결합한다. 둘 다 <b>로드 완료 + 루트·앵커·경계 검증</b>을
        /// 통과하기 전에는 절대 참을 돌려주지 않는다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Update 가 매 틱 부르므로 이 함수는 <b>한 단계씩 전진하는 상태 기계</b>다. 로드가 끝나지 않았으면
        /// 조용히 false 를 돌려 다음 틱을 기다리고, 실패는 <see cref="worldBindingFailed"/> 로 닫아
        /// 같은 오류를 한 번만 남긴다. <b>런타임 mock 맵으로 대체하지 않는다.</b>
        /// </para>
        /// <para>
        /// 결합할 때 씬 루트를 통째로 <see cref="PreparationMapPosition"/>·<see cref="CombatMapPosition"/> 으로
        /// 한 번 옮긴다. 온라인 월드 원점을 그대로 유지해야 기존 시설 좌표 어댑터
        /// (<see cref="PreparationMapOrigin"/>)와 카메라·자기장 계약이 같이 유효하다.
        /// </para>
        /// </remarks>
        private bool EnsureWorld(in OnlineCoreLoopSnapshot snapshot)
        {
            string normalizedMatchId = snapshot.MatchId ?? string.Empty;
            MatchSceneKey requestedPreparation = ResolveMapKey(
                snapshot.PreparationMapKey, MatchScenePhase.Preparation);
            MatchSceneKey requestedCombat = ResolveMapKey(
                snapshot.CombatMapKey, MatchScenePhase.Combat);
            if (worldReady)
            {
                if (preparationRoot != null
                    && combatRoot != null
                    && string.Equals(boundMatchId, normalizedMatchId, StringComparison.Ordinal)
                    && boundPreparationSceneKey == requestedPreparation
                    && boundCombatSceneKey == requestedCombat)
                {
                    return true;
                }

                // 매치가 바뀌었거나 맵 루트가 사라졌다. 지난 매치의 루트·앵커·런타임 활동을 물려주지 않는다.
                bool mapSelectionChanged = boundPreparationSceneKey != requestedPreparation
                    || boundCombatSceneKey != requestedCombat;
                ReleaseMatchWorld(unloadMapScenes: mapSelectionChanged);
            }

            if (mapRelease != null && !mapRelease.IsCompleted) return false;

            if (worldBindingFailed || !IsProductMatchSceneLoaded())
            {
                return false;
            }

            boundPreparationSceneKey = requestedPreparation;
            boundCombatSceneKey = requestedCombat;

            if (!TryAdvanceMapSceneLoad(
                    requestedPreparation, ref preparationSceneLoad, out bool preparationLoaded, out string failure)
                || !TryAdvanceMapSceneLoad(
                    requestedCombat, ref combatSceneLoad, out bool combatLoaded, out failure))
            {
                FailWorldBinding(failure);
                return false;
            }

            if (!preparationLoaded || !combatLoaded)
            {
                // 반쪽 월드로 매치를 열지 않는다.
                return false;
            }

            // ⚠️ **여기부터는 한 프레임에 한 단계씩만 전진한다** (S15P21A606-275).
            //    아래 네 단계가 예전에는 전부 같은 프레임에서 끝났고, 그 프레임이 매치 시작
            //    스파이크였다. 각 단계는 자기 일을 끝내고 `false` 를 돌려 다음 프레임을 기다린다 —
            //    `Update` 가 매 프레임 이 함수를 다시 부르므로 그것으로 충분하다.
            // 단계마다 얼마나 걸렸는지 남긴다 — 다음 회차가 "어느 단계가 아직 무거운가" 를
            // 스스로 답하게 한다. 매치 시작에 네 줄뿐이다.
            WorldAssemblyStep loggedStep = worldAssemblyStep;
            float stepStartedAt = Time.realtimeSinceStartup;
            switch (worldAssemblyStep)
            {
                case WorldAssemblyStep.BindMapScenes:
                    if (!TryBindMapScene(
                            requestedPreparation,
                            PreparationMapPosition,
                            PreparationSpawnAnchorPrefix,
                            out Transform boundPreparationRoot,
                            out Transform[] boundPreparationAnchors,
                            out failure)
                        || !TryBindMapScene(
                            requestedCombat,
                            CombatMapPosition,
                            CombatSpawnAnchorPrefix,
                            out Transform boundCombatRoot,
                            out Transform[] boundCombatAnchors,
                            out failure))
                    {
                        FailWorldBinding(failure);
                        return false;
                    }

                    if (!TryResolveCombatFieldBounds(boundCombatRoot, out CombatZoneBounds fieldBounds, out failure))
                    {
                        FailWorldBinding($"전투 맵의 Collision 경계를 읽지 못했습니다 — {failure}");
                        return false;
                    }

                    preparationRoot = boundPreparationRoot;
                    combatRoot = boundCombatRoot;
                    preparationSpawnAnchors = boundPreparationAnchors;
                    combatSpawnAnchors = boundCombatAnchors;
                    pendingFieldBounds = fieldBounds;
                    worldAssemblyStep = WorldAssemblyStep.ApplyPreparationRenderPolicy;
                    LogAssemblyStep(loggedStep, stepStartedAt);
                    MarkWorldAssemblyAdvanced();
                    return false;

                case WorldAssemblyStep.ApplyPreparationRenderPolicy:
                    // 준비맵 원거리 장식 컬링·상시 안내판 정리 (S15P21A606-292).
                    // dev 에서는 맵 결합과 같은 프레임에 돌던 것을 여기로 떼어 왔다.
                    int culledPreparationLods = PreparationMapRenderBudget.Apply(preparationRoot);
                    int hiddenPreparationDecorations = PreparationMapPresentationPolicy.Apply(preparationRoot);
                    Debug.Log($"[OnlineCoreLoopWorldPresenter] 준비맵 원거리 장식 컬링 구성 — LODGroup {culledPreparationLods}개");
                    Debug.Log($"[OnlineCoreLoopWorldPresenter] 준비맵 상시 안내판·스폰 병사 정리 — {hiddenPreparationDecorations}개");
                    worldAssemblyStep = WorldAssemblyStep.CreateRuntimeRoots;
                    LogAssemblyStep(loggedStep, stepStartedAt);
                    MarkWorldAssemblyAdvanced();
                    return false;

                case WorldAssemblyStep.CreateRuntimeRoots:
                    // 회전판 표현을 준비맵의 회전 피벗에 붙인다 (S-GD-49WM7S · S-CLI-B2TGQ8).
                    // ⚠️ 피벗이 없는 준비맵 아트면 null 을 돌려주고 아무것도 하지 않는다 —
                    //    적중 판정과 정산은 도메인이 하므로 회전이 안 보여도 게임은 그대로 돈다.
                    RouletteWheelPresentation.TryBind(preparationRoot);

                    // 상자 같은 런타임 활동은 저작 씬 밖, 우리가 소유하는 루트에 둔다.
                    combatRuntimeRoot = CreateRoot(CombatRuntimeRootName, CombatMapPosition);
                    SceneManager.MoveGameObjectToScene(combatRuntimeRoot.gameObject, SceneManager.GetSceneByName(SceneName.InGame));
                    combatRuntimeRoot.gameObject.SetActive(false);
                    preparationRuntimeRoot = CreateRoot("[Online Preparation Runtime Activities]", PreparationMapPosition);
                    SceneManager.MoveGameObjectToScene(preparationRuntimeRoot.gameObject, SceneManager.GetSceneByName(SceneName.InGame));
                    Transform dummyAnchor = preparationRoot.Find("Anchors/ActivityAnchors/TrainingDummyAnchor_01");
                    if (dummyAnchor != null)
                    {
                        Transform dummy = CreateTrainingDummy(preparationRuntimeRoot,
                            FindFirstObjectByType<EnvironmentArtCatalog>(FindObjectsInactive.Include));
                        dummy.SetPositionAndRotation(dummyAnchor.position, dummyAnchor.rotation);
                        dummy.localScale = dummyAnchor.lossyScale;
                        preparationTrainingDummy = dummy.GetComponent<CombatantHealth>();
                        RefreshPreparationPracticeTarget();
                    }
                    preparationRuntimeRoot.gameObject.SetActive(false);
                    worldAssemblyStep = WorldAssemblyStep.CollectCombatBlockers;
                    LogAssemblyStep(loggedStep, stepStartedAt);
                    MarkWorldAssemblyAdvanced();
                    return false;

                case WorldAssemblyStep.CollectCombatBlockers:
                    // 지형 차폐도 저작 Collision 계층에서만 모은다 — 시각 메시는 게임플레이 경계가 아니다.
                    Transform blockerScope =
                        MatchSceneContentResolver.GetSection(combatRoot, MatchSceneContentResolver.CollisionRootName)
                        ?? combatRoot;

                    // ⚠️ **맵을 켜지 않고 모은다.** 예전에는 `combatRoot.SetActive(true)` → 수집 →
                    //    `SetActive(false)` 로 왕복시켰는데, 그 왕복 하나가 하위 오브젝트 수백 개의
                    //    활성 전이를 일으켰다(2026-09-22 실측: Activate 143회 17.4ms + Deactivate
                    //    82회 10.3ms). **그런데 그 왕복은 처음부터 아무 효과도 없었다** —
                    //    `GetComponentsInChildren(false)` 는 호출 대상 자신의 활성 상태를 보지 않아
                    //    꺼진 루트에서도 같은 집합을 내놓는다(`CombatWorldCompositionTests`).
                    combatTerrainBlockers = CombatWorldComposition.CollectTerrainBlockers(blockerScope);

                    // 차폐물을 새로 모았다. 안개 형상 캐시가 지난 맵의 것을 들고 있으면 안 된다.
                    FogVisibilityPolygonBuilder.InvalidateBlockerCache();
                    worldAssemblyStep = WorldAssemblyStep.Finish;
                    LogAssemblyStep(loggedStep, stepStartedAt);
                    MarkWorldAssemblyAdvanced();
                    return false;

                default:
                    // 준비 국면에도 미니맵을 띄우려면(S-UX-9FT5C2) 그 국면의 지형도 함께 알아야 한다.
                    // 미니맵 경계는 차폐물 집합에서 나오므로, 전투 맵 것을 그대로 쓰면 준비맵에 서 있는
                    // 동안 표식이 전부 맵 밖으로 나가 테두리에 몰린다.
                    // 준비 맵 루트는 이 시점에 꺼져 있지만 그래도 모인다 — 위 전투 맵과 같은 이유다.
                    Transform preparationBlockerScope =
                        MatchSceneContentResolver.GetSection(preparationRoot, MatchSceneContentResolver.CollisionRootName)
                        ?? preparationRoot;
                    preparationTerrainBlockers =
                        CombatWorldComposition.CollectTerrainBlockers(preparationBlockerScope);

                    worldCamera = ResolveWorldCamera();
                    SharedCombatCamera.ConfigureProjection(worldCamera);

                    // 기존 10x10 샌드박스 지형만 감춘다. 제품 NetworkObject actor root는 Lab과 다르므로
                    // 여기서 찾아 이동/활성 전환하지 않고, 아래 visibility/placement 경로가 live actor를 직접 수집한다.
                    HideLegacyArenaRoot();

                    boundMatchId = normalizedMatchId;
                    worldReady = true;
                    appliedPhase = (OnlineCoreLoopPhase)(-1);
                    appliedRound = -1;
                    Debug.Log(
                        "[OnlineCoreLoopWorldPresenter] 저작 맵 결합 완료 — " +
                        $"준비 '{requestedPreparation}' x={PreparationMapPosition.x}, " +
                        $"전투 '{requestedCombat}' x={CombatMapPosition.x}, 전장 경계 {pendingFieldBounds}");

                    // 월드는 열렸지만 이번 프레임은 여기까지다. 맵 활성·스폰·배치는 다음 프레임이
                    // 맡는다 — 위 `if (worldReady)` 가 그때 바로 true 를 돌려준다.
                    LogAssemblyStep(loggedStep, stepStartedAt);
                    MarkWorldAssemblyAdvanced();
                    return false;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // 매치 시작 창의 비용을 **단계별로** diag 에 남긴다 (S15P21A606-275, 4차)
        //
        // ⚠️ **콘솔 로그로는 부족하다.** `Debug.Log` 는 diag 에 안 실리고(Warning 이상만 실린다),
        //    Warning 으로 올리면 같은 문구가 한 번으로 합쳐져 네 단계를 구분할 수 없다.
        //    그래서 숫자로 넘겨 `ReplicationLoad` 에 싣는다.
        //
        //    ⚠️ **이 숫자가 예산의 전부가 아니다.** 2026-09-22 실측에서 매치 시작 창의
        //    `excessMs` 가 host 793.5 / client 650.1 였는데 스폰(8기)은 261ms 뿐이었다.
        //    나머지 500ms 가 어디인지가 지금 질문이고, 이 배열이 그 중 조립 몫을 가른다.
        // ─────────────────────────────────────────────────────────────

        /// <summary>단계별 소요 시간(ms). 첨자는 <see cref="WorldAssemblyStep"/> 순서다.</summary>
        internal static readonly float[] AssemblyStepMs = new float[5];

        /// <summary>단계별 소요 시간을 JSON 배열로. 아직 안 돈 단계는 0 이다.</summary>
        internal static string DescribeAssemblyStepMs() =>
            $"[{AssemblyStepMs[0]:0.0},{AssemblyStepMs[1]:0.0},{AssemblyStepMs[2]:0.0},"
            + $"{AssemblyStepMs[3]:0.0},{AssemblyStepMs[4]:0.0}]";

        /// <summary>조립 단계 하나가 얼마나 걸렸는지 남긴다 (S15P21A606-275).</summary>
        /// <remarks>
        /// 한 프레임에 한 단계만 도므로 이 값이 곧 <b>그 프레임이 이 프레젠터에 쓴 시간</b>이다.
        /// 어느 단계도 300ms 를 넘으면 안 된다 — 그 프레임 하나가 Fusion 예측 천장(확정틱 +64)을
        /// 만들어 입력 생성을 끊는다.
        /// </remarks>
        private static void LogAssemblyStep(WorldAssemblyStep step, float startedAt)
        {
            float elapsedMs = (Time.realtimeSinceStartup - startedAt) * 1000f;
            var index = (int)step;
            if (index >= 0 && index < AssemblyStepMs.Length) AssemblyStepMs[index] = elapsedMs;
            Debug.Log($"[OnlineCoreLoopWorldPresenter] 조립 단계 {step} — {elapsedMs:0.0}ms");
        }

        private static MatchSceneKey ResolveMapKey(MatchSceneKey key, MatchScenePhase phase) =>
            MatchSceneCatalog.TryGetForPhase(key, phase, out _)
                ? key
                : phase == MatchScenePhase.Preparation
                    ? MatchSceneCatalog.DefaultPreparation
                    : MatchSceneCatalog.DefaultCombat;

        private void HideLegacyArenaRoot()
        {
            if (legacyArenaRoot == null)
            {
                legacyArenaRoot = FindRoot(SceneManager.GetSceneByName(SceneName.InGame), "Arena");
            }

            if (legacyArenaRoot != null)
            {
                legacyArenaRoot.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 맵 씬 하나의 로드를 한 단계 전진시킨다. 돌려주는 값은 <b>계속 기다려도 되는가</b>이고,
        /// <paramref name="loaded"/> 가 true 일 때만 씬을 읽어도 된다.
        /// </summary>
        /// <remarks>
        /// 완료된 Task 는 <b>반드시 여기서 읽는다</b> — 결과를 보지 않고 버리면 Provider 예외가
        /// 관측되지 않은 채 사라진다. 성공 보고와 실제 핸들이 어긋나도 무한 재시도하지 않고 닫는다.
        /// </remarks>
        private bool TryAdvanceMapSceneLoad(
            MatchSceneKey key,
            ref Task<MatchSceneLoadResult> pending,
            out bool loaded,
            out string failureReason)
        {
            loaded = false;
            failureReason = string.Empty;

            var reportedSuccess = false;
            if (pending != null && pending.IsCompleted)
            {
                Task<MatchSceneLoadResult> completed = pending;
                pending = null;
                if (completed.Status != TaskStatus.RanToCompletion)
                {
                    failureReason = $"'{key}' 맵 씬 로드가 끝나지 못했습니다: {DescribeTaskFailure(completed)}";
                    return false;
                }

                MatchSceneLoadResult result = completed.Result;
                if (result.Code != MatchSceneLoadResult.ResultCode.Loaded)
                {
                    failureReason =
                        $"'{key}' 맵 씬을 로드하지 못했습니다 [{result.Code}] {result.Message}";
                    return false;
                }

                reportedSuccess = true;
            }

            if (mapSceneProvider.IsLoaded(key))
            {
                loaded = true;
                return true;
            }

            if (reportedSuccess)
            {
                failureReason = $"'{key}' 로드가 성공으로 보고됐지만 씬 핸들이 없습니다.";
                return false;
            }

            pending ??= mapSceneProvider.LoadAsync(key);
            return true;
        }

        /// <summary>
        /// 로드가 끝난 맵의 단일 루트를 검증하고 온라인 월드 원점으로 <b>한 번</b> 옮긴 뒤 앵커를 읽는다.
        /// </summary>
        private bool TryBindMapScene(
            MatchSceneKey key,
            Vector3 origin,
            string spawnAnchorPrefix,
            out Transform root,
            out Transform[] spawnAnchors,
            out string failureReason)
        {
            root = null;
            spawnAnchors = null;
            if (!MatchSceneCatalog.TryGet(key, out MatchSceneDescriptor descriptor))
            {
                failureReason = $"'{key}' 가 MatchSceneCatalog 에 없습니다.";
                return false;
            }

            if (!MatchSceneContentResolver.TryGetMapRoot(descriptor, out root, out failureReason))
            {
                return false;
            }

            // 회전은 주지 않는다. PreparationMapOrigin 을 빼는 시설 좌표 어댑터가 회전 없음을 전제한다.
            root.SetPositionAndRotation(origin, Quaternion.identity);

            // 어느 맵을 보여줄지는 스냅샷이 정한다. 결합 순간에는 둘 다 꺼 둔다.
            root.gameObject.SetActive(false);

            if (!TryResolveSpawnAnchors(root, spawnAnchorPrefix, out spawnAnchors))
            {
                failureReason =
                    $"'{key}' 맵의 Anchors/{spawnAnchorPrefix}01..{MaximumParticipants:00} 을 찾지 못했습니다.";
                root = null;
                return false;
            }

            failureReason = string.Empty;
            return true;
        }

        private void FailWorldBinding(string failureReason)
        {
            if (worldBindingFailed)
            {
                return;
            }

            worldBindingFailed = true;
            worldBindingFailureReason = string.IsNullOrWhiteSpace(failureReason)
                ? "저작 맵을 확인하지 못했습니다."
                : failureReason;
            placementPending = false;
            preparationRoot?.gameObject.SetActive(false);
            combatRoot?.gameObject.SetActive(false);
            Debug.LogError(
                "[OnlineCoreLoopWorldPresenter] 저작 맵을 결합하지 못해 제품 월드를 열지 않습니다 — " +
                $"{worldBindingFailureReason} (Build Profile 의 Map Scene 등록과 Map Root 계층을 확인하세요)");
        }

        private static string DescribeTaskFailure(Task task) => task.IsCanceled
            ? "취소됨"
            : task.Exception?.GetBaseException().Message ?? "알 수 없는 오류";

        /// <summary>버릴 Task 도 결과를 한 번 읽어 관측되지 않은 예외를 남기지 않는다.</summary>
        private static void ObserveMapSceneTask(Task<MatchSceneLoadResult> task, string description)
        {
            if (task == null)
            {
                return;
            }

            task.ContinueWith(
                completed =>
                {
                    if (completed.Status != TaskStatus.RanToCompletion)
                    {
                        Debug.LogWarning(
                            $"[OnlineCoreLoopWorldPresenter] {description} 이(가) 끝나지 못했습니다: " +
                            DescribeTaskFailure(completed));
                        return;
                    }

                    if (!completed.Result.Succeeded)
                    {
                        Debug.LogWarning(
                            $"[OnlineCoreLoopWorldPresenter] {description} 실패 " +
                            $"[{completed.Result.Code}] {completed.Result.Message}");
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static Camera ResolveWorldCamera()
        {
            Camera resolved = Camera.main;
            if (resolved != null)
            {
                return resolved;
            }

            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            return cameras.Length > 0 ? cameras[0] : null;
        }

        private bool TryRefreshCombatantArenaBounds()
        {
            Transform root = preparationRoot != null && preparationRoot.gameObject.activeSelf ? preparationRoot : combatRoot;
            if (!MatchSceneContentResolver.TryResolveMovementBounds(root, out Rect bounds, out _)) return false;
            var motions = FindObjectsByType<NetworkCombatantMotion>(FindObjectsSortMode.None);
            bool found = false;
            foreach (var motion in motions)
            {
                if (!IsLiveProductCombatant(motion)) continue;
                var motor = motion.GetComponent<CombatantMotor>();
                if (motor == null) return false;
                motor.SetGhostMovementBounds(bounds);
                found = true;
            }
            return found;
        }

        // ─────────────────────────────────────────────────────────────
        // 전장 기하 — 자기장 경계의 정본 (S-GD-657ZN5 · S-CLI-RQB4QD)
        //
        // ⚠️ 경계는 **조립된 전투 맵 루트의 실제 벽**에서만 나온다. 씬 전체나 준비 콜로세움을
        //    함께 훑지 않으므로 루트 직속 자식만 본다 — 준비 맵에도 벽이 있고, 엄폐물 세그먼트도
        //    이름에 Wall 이 들어간다. 중심·치수를 코드에 다시 적지 않으므로 맵을 옮기거나
        //    넓히면 자기장도 함께 따라간다.
        // ─────────────────────────────────────────────────────────────

        /// <summary>전장 경계를 정하는 네 벽. <b>전투 맵 루트 직속 자식</b>의 이름이다.</summary>
        public const string CombatNorthWallName = "NorthWall";

        /// <inheritdoc cref="CombatNorthWallName"/>
        public const string CombatSouthWallName = "SouthWall";

        /// <inheritdoc cref="CombatNorthWallName"/>
        public const string CombatEastWallName = "EastWall";

        /// <inheritdoc cref="CombatNorthWallName"/>
        public const string CombatWestWallName = "WestWall";

        /// <summary>
        /// 지금 조립된 전투 맵의 <b>실제</b> 전장 경계. 맵이 아직 없으면 false 이며 원점·기본 크기를
        /// 지어내지 않는다 — 호출자는 다음 틱에 다시 물어야 한다.
        /// </summary>
        /// <param name="failureReason">
        /// 실패 이유. 호출자가 <b>한 번만</b> 로그로 남길 수 있게 여기서는 기록하지 않는다 —
        /// 월드 조립 대기는 매 틱 재시도되므로 여기서 찍으면 로그가 덮인다.
        /// </param>
        public static bool TryGetCombatFieldBounds(out CombatZoneBounds bounds, out string failureReason)
        {
            bounds = default;
            if (instance == null)
            {
                failureReason = "월드 표현기가 아직 없습니다.";
                return false;
            }

            if (!instance.worldReady || instance.combatRoot == null)
            {
                failureReason = "저작 전투 맵이 아직 결합되지 않았습니다.";
                return false;
            }

            return TryResolveCombatFieldBounds(instance.combatRoot, out bounds, out failureReason);
        }

        /// <summary>
        /// 전투 맵의 네 경계벽에서 전장 경계를 읽는다. 경계는 벽 <b>안쪽 면</b> 기준이다 —
        /// 전투원이 실제로 서 있을 수 있는 공간이 자기장의 정본이기 때문이다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>어디를 보는가.</b> 저작 Map Root 에는 <c>Collision</c> 소유권 계층이 있고 경계벽은 그 안에 있다.
        /// 그 계층이 있으면 <b>거기만</b> 훑는다 — 시각 메시(<c>Geometry</c>)나 다른 맵의 벽이 섞이면
        /// 자기장이 조용히 다른 전장을 그린다. <c>Collision</c> 이 없는 <b>평면 루트</b>(저작 도구가 만든
        /// 임시 루트와 기존 시험 배치)는 예전처럼 루트 직속 자식만 본다.
        /// </para>
        /// <para>
        /// ⚠️ <b>비활성 루트에서도 값이 나와야 한다.</b> 준비 단계에서 전투 맵은 꺼져 있고 그때
        /// <see cref="Collider.bounds"/> 는 빈 상자다. 그래서 <see cref="BoxCollider.center"/>·
        /// <see cref="BoxCollider.size"/> 를 벽 Transform 으로 직접 환산한다.
        /// <b>질의만을 위해 오브젝트를 켜지 않는다</b> — 켜면 스냅샷이 정하지 않은 맵이 한 프레임 보인다.
        /// </para>
        /// <para>
        /// 네 벽이 하나라도 없거나 같은 이름이 둘 이상이거나 안쪽 공간이 뒤집혀 있으면 실패한다 —
        /// 셋 중 무엇도 조용히 기본값으로 메우지 않는다.
        /// </para>
        /// </remarks>
        public static bool TryResolveCombatFieldBounds(
            Transform combatFieldRoot, out CombatZoneBounds bounds, out string failureReason)
        {
            bounds = default;
            if (combatFieldRoot == null)
            {
                failureReason = "전장 루트가 없습니다.";
                return false;
            }

            Transform north = null;
            Transform south = null;
            Transform east = null;
            Transform west = null;
            Transform collisionRoot = MatchSceneContentResolver.GetSection(
                combatFieldRoot, MatchSceneContentResolver.CollisionRootName);
            if (!TryClaimBoundaryWalls(
                    collisionRoot != null ? collisionRoot : combatFieldRoot,
                    searchNested: collisionRoot != null,
                    ref north,
                    ref south,
                    ref east,
                    ref west,
                    out failureReason))
            {
                return false;
            }

            if (!TryReadWallSpan(
                    north, CombatNorthWallName,
                    out Vector3 northCenter, out Vector3 northExtents, out failureReason)
                || !TryReadWallSpan(
                    south, CombatSouthWallName,
                    out Vector3 southCenter, out Vector3 southExtents, out failureReason)
                || !TryReadWallSpan(
                    east, CombatEastWallName,
                    out Vector3 eastCenter, out Vector3 eastExtents, out failureReason)
                || !TryReadWallSpan(
                    west, CombatWestWallName,
                    out Vector3 westCenter, out Vector3 westExtents, out failureReason))
            {
                return false;
            }

            // 안쪽 면끼리 만나는 사각형이다. 벽이 남북·동서로 뒤바뀌어 있으면 치수가 음수가 되어
            // IsValid 에서 걸린다 — 조용히 뒤집어 고치지 않는다.
            CombatZoneBounds resolved = CombatZoneBounds.FromCorners(
                westCenter.x + westExtents.x,
                southCenter.z + southExtents.z,
                eastCenter.x - eastExtents.x,
                northCenter.z - northExtents.z);
            if (!resolved.IsValid)
            {
                failureReason =
                    $"네 벽 안쪽 공간이 유효한 전장이 아닙니다({resolved}). 벽 위치·두께를 확인하세요.";
                return false;
            }

            bounds = resolved;
            failureReason = string.Empty;
            return true;
        }

        /// <summary>
        /// 경계 후보를 모은다. <paramref name="searchNested"/> 는 저작 <c>Collision</c> 계층처럼
        /// 벽이 하위 묶음에 들어 있을 수 있는 범위에서만 켠다. 이미 벽으로 인정한 오브젝트의 아래로는
        /// 내려가지 않는다 — 벽에 붙은 부속물이 같은 이름을 갖고 있어도 중복으로 세지 않기 위해서다.
        /// </summary>
        private static bool TryClaimBoundaryWalls(
            Transform scope,
            bool searchNested,
            ref Transform north,
            ref Transform south,
            ref Transform east,
            ref Transform west,
            out string failureReason)
        {
            failureReason = string.Empty;
            for (int i = 0; i < scope.childCount; i++)
            {
                Transform child = scope.GetChild(i);
                switch (child.name)
                {
                    case CombatNorthWallName:
                        if (!TryClaimWall(ref north, child, out failureReason))
                        {
                            return false;
                        }

                        break;

                    case CombatSouthWallName:
                        if (!TryClaimWall(ref south, child, out failureReason))
                        {
                            return false;
                        }

                        break;

                    case CombatEastWallName:
                        if (!TryClaimWall(ref east, child, out failureReason))
                        {
                            return false;
                        }

                        break;

                    case CombatWestWallName:
                        if (!TryClaimWall(ref west, child, out failureReason))
                        {
                            return false;
                        }

                        break;

                    default:
                        if (searchNested
                            && !TryClaimBoundaryWalls(
                                child, true, ref north, ref south, ref east, ref west, out failureReason))
                        {
                            return false;
                        }

                        break;
                }
            }

            return true;
        }

        /// <summary>
        /// 같은 이름의 벽이 둘 이상이면 <b>둘 중 하나를 고르지 않는다</b> — 어느 쪽이 진짜
        /// 경계인지 알 수 없고, 잘못 고르면 자기장이 조용히 다른 전장을 그린다.
        /// </summary>
        private static bool TryClaimWall(ref Transform slot, Transform candidate, out string failureReason)
        {
            if (slot != null)
            {
                failureReason =
                    $"전장 경계 범위 안에 '{candidate.name}' 이 둘 이상 있습니다. 경계를 고르지 않습니다.";
                return false;
            }

            slot = candidate;
            failureReason = string.Empty;
            return true;
        }

        /// <summary>
        /// 벽 하나가 차지하는 축 정렬 범위. 로컬 축을 월드로 옮겨 합산하므로 벽이 기울어져 있어도
        /// 실제 범위가 나오고, <b>비활성 오브젝트에서도</b> 값이 나온다.
        /// </summary>
        private static bool TryReadWallSpan(
            Transform wall, string wallName, out Vector3 center, out Vector3 extents, out string failureReason)
        {
            center = default;
            extents = default;
            if (wall == null)
            {
                failureReason = $"전장 경계 범위 안에 '{wallName}' 이 없습니다.";
                return false;
            }

            var box = wall.GetComponent<BoxCollider>();
            if (box == null)
            {
                failureReason = $"'{wallName}' 에 BoxCollider 가 없어 경계를 읽을 수 없습니다.";
                return false;
            }

            Matrix4x4 localToWorld = wall.localToWorldMatrix;
            Vector3 half = box.size * 0.5f;
            Vector3 alongX = localToWorld.MultiplyVector(new Vector3(half.x, 0f, 0f));
            Vector3 alongY = localToWorld.MultiplyVector(new Vector3(0f, half.y, 0f));
            Vector3 alongZ = localToWorld.MultiplyVector(new Vector3(0f, 0f, half.z));
            center = localToWorld.MultiplyPoint3x4(box.center);
            extents = new Vector3(
                Mathf.Abs(alongX.x) + Mathf.Abs(alongY.x) + Mathf.Abs(alongZ.x),
                Mathf.Abs(alongX.y) + Mathf.Abs(alongY.y) + Mathf.Abs(alongZ.y),
                Mathf.Abs(alongX.z) + Mathf.Abs(alongY.z) + Mathf.Abs(alongZ.z));
            if (!IsFinite(center) || !IsFinite(extents) || extents.x <= 0f || extents.z <= 0f)
            {
                failureReason = $"'{wallName}' 의 크기·위치가 경계로 쓸 수 없는 값입니다.";
                return false;
            }

            failureReason = string.Empty;
            return true;
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>라운드·국면 조합의 경계 키. 0 은 "키 없음" 이라 라운드 0 도 0 이 되지 않게 1 을 더한다.</summary>
        internal static int RoundResetKey(int round, OnlineCoreLoopPhase phase) => (round + 1) * 16 + (int)phase;

        /// <summary>마지막으로 소모품을 비운 매치와 라운드. 정산·종료 스냅샷이 여러 번 와도 한 번만 비운다.</summary>
        /// <remarks>
        /// ⚠️ <b>매치까지 함께 기억한다</b> (S15P21A606-335). 이 발표자는 앱이 떠 있는 동안 하나이고 라운드 번호는 매치마다
        /// 1 부터 다시 센다. 라운드만 기억하던 때는 첫 매치의 마지막 라운드(3)가 남아, 다음 매치부터는 1·2·3 라운드 모두
        /// "이미 비웠다" 로 건너뛰었다 — 전투가 끝나도 소모품이 준비 라운드까지 남았다.
        /// </remarks>
        private string consumablesClearedMatchId = string.Empty;
        private int consumablesClearedRound = -1;

        /// <summary>
        /// 이 스냅샷에서 전투 종료 소모품 정리를 해야 하는가 — 정산·종료 국면이고, 이 매치의 이 라운드를 아직 비우지 않았다.
        /// </summary>
        public static bool ShouldClearConsumablesForRoundEnd(OnlineCoreLoopPhase phase, string matchId, int round,
            string clearedMatchId, int clearedRound)
        {
            if (phase is not (OnlineCoreLoopPhase.Summary or OnlineCoreLoopPhase.Finished)) return false;
            bool sameMatch = string.Equals(matchId ?? string.Empty, clearedMatchId ?? string.Empty, StringComparison.Ordinal);
            return !sameMatch || round > clearedRound;
        }

        /// <summary>
        /// 이 단계의 배치가 전투원을 라운드 경계로 되돌리는가 — 체력 100%, 소모품·축복 버프/디버프
        /// 해제, 쿨타임 초기화 (S-GD-AN554W).
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠️ <b>Loading 은 요구하지 않는다</b> — 요구하면 순환 대기로 굳는다. 회복 경로는
        /// <c>OnlineEconomyCoordinator.RefreshCombatStats</c> 성공을 요구하고, 그쪽은 <c>economy.Round</c>
        /// 가 스냅샷 라운드와 같아야 한다. 그런데 <c>economy.BeginMatch</c> 직후 Round 는 0 이고 1 로
        /// 올리는 <c>BeginPreparation</c> 은 Preparation 단계에서만 불린다. 그 Preparation 진입은 다시
        /// 이 배치가 끝나야 열리므로 셋이 서로를 기다린다. Loading 은 스폰 앵커에 세우기만 하면 되고,
        /// 체력·축복 회복은 곧 이어지는 Preparation 배치가 같은 전투원에게 수행한다.
        /// </para>
        /// <para>
        /// ⚠️ <b>Combat 은 반드시 요구한다</b> — 준비맵에서도 더미를 때리고 서로 맞고 액티브 축복
        /// (Q/Y — S-GD-NJQ5NM)을 켤 수 있다. 여기서 Preparation 만 보면 <b>준비 단계에서 시험 삼아 켠
        /// 올인·더블 다운의 공격력·관통·피해·이동 보너스와 이미 지불한 체력, 그리고 재사용 대기가
        /// 그대로 전투 라운드로 넘어간다.</b> 준비 단계에서 만든 차이는 축복·강화·장비로만 남아야 한다
        /// (S-GD-AN554W). 라운드 경계 초기화 자체는 <c>CombatantHealth.RecoverForNextRound</c> 와
        /// <c>BlessingCombatState.ResetRound</c> 가 이미 갖고 있고, 온라인 제품 경로만 그것을 전투
        /// 진입에서 부르지 않고 있었다 (Local Play 는 <c>LocalPlayBotRoster.TryRelocateParticipants</c>
        /// 가 같은 경계에서 이미 부른다).
        /// </para>
        /// </remarks>
        public static bool RecoversOnPlacement(OnlineCoreLoopPhase phase) =>
            phase is OnlineCoreLoopPhase.Preparation or OnlineCoreLoopPhase.Combat;

        /// <summary>
        /// 라운드 경제 지급(이자 폭발)이 열려 있는 국면인지 Host 가 모든 전투원에 알린다 (S-GD-XBP3VA).
        /// 정산·다음 준비에서 이자가 계속 들어오던 것을 국면 전환에서 끊는다.
        /// </summary>
        private void ApplyCombatRoundRunning(in OnlineCoreLoopSnapshot snapshot)
        {
            if (!NetworkManager.HasInstance || !NetworkManager.Instance.IsHost) return;
            bool running = snapshot.Phase == OnlineCoreLoopPhase.Combat;
            foreach (var motion in FindObjectsByType<NetworkCombatantMotion>(FindObjectsSortMode.None))
                if (IsLiveProductCombatant(motion) && motion.HasStateAuthority)
                    motion.SetCombatRoundRunning(running);
        }

        /// <summary>
        /// Host 가 전투 종료(Summary·Finished)를 확정한 경계에서 모든 제품 전투원의 소모품을 비운다 (S-GD-3DEQ2M).
        /// 생존자·빈사자 모두 대상이다. 다음 준비에서 새로 산 물건은 다음 라운드 번호라 건드리지 않는다.
        /// </summary>
        private void ClearConsumablesForRoundEnd(in OnlineCoreLoopSnapshot snapshot)
        {
            if (!NetworkManager.HasInstance || !NetworkManager.Instance.IsHost) return;
            if (!ShouldClearConsumablesForRoundEnd(snapshot.Phase, snapshot.MatchId, snapshot.CurrentRound,
                    consumablesClearedMatchId, consumablesClearedRound)) return;
            consumablesClearedMatchId = snapshot.MatchId ?? string.Empty;
            consumablesClearedRound = snapshot.CurrentRound;
            foreach (var motion in FindObjectsByType<NetworkCombatantMotion>(FindObjectsSortMode.None))
                if (IsLiveProductCombatant(motion) && motion.HasStateAuthority)
                    motion.TryAuthorityClearConsumablesForRoundEnd();
        }

        private bool TryPlaceAuthorityPlayers(in OnlineCoreLoopSnapshot snapshot)
        {
            if (!NetworkManager.HasInstance || !NetworkManager.Instance.IsHost)
            {
                return true;
            }

            placementCandidates.Clear();
            foreach (var motion in FindObjectsByType<NetworkCombatantMotion>(FindObjectsSortMode.None))
                if (IsLiveProductCombatant(motion) && motion.HasStateAuthority) placementCandidates.Add(motion);
            // ⚠️ <b>접속자 목록이 아니라 매치 참가자 전체를 쓴다</b> (S15P21A606-188).
            //    봇은 피어가 아니라 ActivePlayers 에 없는데, 아래 `liveIds.Length != count` 는
            //    실제 전투원 수와 비교한다 — 봇이 하나라도 서면 8 != 1 로 **배치가 통째로 실패**하고,
            //    맵 루트가 ±60m 이동하는 동안 아무도 재배치되지 않아 전원이 맵 밖에 남는다.
            if (!TryResolveMatchParticipantIds(out int[] rosterIds))
                return BlockPlacement(snapshot, "매치 참가자 명부를 읽지 못했습니다");
            // ⚠️ <b>명부 전체가 아니라 "명부 중 아직 방에 남은 참가자" 와 대조한다</b> (S15P21A606-306 · S-GD-BYVCAF).
            //    명부는 이탈해도 줄지 않는데 이탈자의 전투원은 OnPlayerLeft 에서 사라진다. 전체와 대조하면
            //    `3 != 2` 로 배치가 영원히 실패해 준비 타이머·시뮬레이션이 멈추고, 회복이 이 배치 안에
            //    있어 빈사자가 유령으로 남았다. 방에 남아 있는데 전투원이 아직 없는 참가자는 계속 기다린다.
            int[] liveIds = ResolvePlacementParticipantIds(rosterIds, IsParticipantStillInMatch);
            int count = placementCandidates.Count;
            if (count == 0 || count > MaximumParticipants || liveIds.Length != count)
                return BlockPlacement(snapshot,
                    $"배치 대상 {liveIds.Length}명(명부 {rosterIds.Length}명)과 전투원 {count}명이 맞지 않습니다");
            // ⚠️ 봇의 PlayerId 는 전부 0 이다. 참가자 키로 정렬·대조해야 7기가 서로 다른 자리를 받는다.
            //    이탈자를 뺀 뒤 입장 순서로 정렬하므로 남은 사람이 앞 슬롯부터 채운다 (S-GD-BYVCAF).
            placementCandidates.Sort((left, right) =>
                ComparePlayerIdsByJoinOrder(NetworkManager.Instance, left.MatchParticipantId, right.MatchParticipantId));
            for (int i = 0; i < liveIds.Length; i++)
                if (placementCandidates[i].MatchParticipantId != liveIds[i])
                    return BlockPlacement(snapshot,
                        $"{i}번째 전투원 참가자 {placementCandidates[i].MatchParticipantId} 이(가) 명부 순서 {liveIds[i]} 와 다릅니다");
            Physics.SyncTransforms();
            bool preparing = snapshot.Phase is OnlineCoreLoopPhase.Loading or OnlineCoreLoopPhase.Preparation;
            Transform map = preparing ? preparationRoot : combatRoot;
            var poses = new Pose[count];
            for (int i = 0; i < count; i++)
            {
                int slot = preparing ? i : ResolveCombatSpawnIndex(i, snapshot.CurrentRound);
                if (!MatchSceneContentResolver.TryGetSpawnPose(map,
                    preparing ? MatchScenePhase.Preparation : MatchScenePhase.Combat, slot, out poses[i], out _))
                    return BlockPlacement(snapshot, $"{slot}번 스폰 자리를 찾지 못했습니다");
            }
            // 준비 진입과 **전투 진입** 양쪽에서 초기화한다 (S-GD-3DEQ2M · S-GD-AN554W).
            //    Loading 을 요구하지 않는 이유는 RecoversOnPlacement 의 설명에 적혀 있다.
            //    경계 키(라운드·국면)로 전투원마다 정확히 한 번만 회복한다 — 배치 재시도나
            //    같은 스냅샷 재수신에서는 위치만 다시 세운다.
            bool recoverForNextRound = RecoversOnPlacement(snapshot.Phase);
            int roundResetKey = RoundResetKey(snapshot.CurrentRound, snapshot.Phase);
            for (int i = 0; i < count; i++)
                if (!placementCandidates[i].TryAuthorityTeleport(poses[i].position, poses[i].rotation,
                    recoverForNextRound, roundResetKey))
                    return BlockPlacement(snapshot,
                        $"참가자 {placementCandidates[i].MatchParticipantId} 텔레포트·회복이 거절됐습니다(경제 스탯 확정 대기 포함)");

            Physics.SyncTransforms();

            // ⚠️ 텔레포트가 "성공" 을 돌려줘도 실제로 옮겨졌는지는 별개다. 시뮬레이션이 값을 덮으면
            //    전투원이 옛 맵에 남고, 곧이어 그 맵 루트가 꺼지면서 발밑이 사라져 끝없이 추락한다.
            //    그래서 여기서 실제 좌표를 확인하고, 맵을 통째로 벗어난 수준이면 배치를 완료로
            //    보고하지 않는다 — 옛 바닥을 남겨둔 채 다음 틱에 다시 시도하는 편이 낫다.
            //    (WrongMapDrift 는 두 맵 원점 간 거리 120m 보다 훨씬 작게, 지형 보정보다는 크게 잡는다.)
            const float PlacementDriftWarning = 0.5f;
            const float WrongMapDrift = 5f;
            bool placed = true;
            for (int i = 0; i < count; i++)
            {
                Vector3 actual = placementCandidates[i].transform.position;
                float drift = Vector3.Distance(actual, poses[i].position);
                if (drift <= PlacementDriftWarning) continue;

                if (drift > WrongMapDrift) placed = false;
                Debug.LogError(
                    $"[OnlineCoreLoopWorldPresenter] 배치가 실제 위치에 반영되지 않았습니다 — "
                    + $"Player {placementCandidates[i].PlayerId}, {snapshot.Phase}, "
                    + $"요청={poses[i].position.ToString("0.0")} 실제={actual.ToString("0.0")} "
                    + $"어긋남={drift:0.0}m (NetworkTransform 유무와 권한을 확인하세요)");
            }

            if (!placed) return false;
            placementBlockedReason = string.Empty;

            // ⚠️ "몇 명" 만 찍으면 어디에 세웠는지 알 수 없다. 추락을 쫓을 때 요청 좌표가
            //    맞았는지(= 앵커 문제) 틀렸는지(= 해석 문제)를 이 줄 하나로 갈라야 한다.
            var placedAt = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i > 0) placedAt.Append(", ");
                placedAt.Append(placementCandidates[i].PlayerId).Append('=')
                    .Append(poses[i].position.ToString("0.0"));
            }

            Debug.Log(
                $"[OnlineCoreLoopWorldPresenter] Host 권위 배치 완료 — {snapshot.Phase}, " +
                $"{snapshot.CurrentRound}라운드, {count}명, 맵원점={map.position.ToString("0.0")}, " +
                $"요청 [{placedAt}]");
            return true;
        }

        /// <summary>
        /// 추적·시야·미니맵이 함께 쓰는 월드 카메라를 해석한다. 공용 기본 렌즈만 적용하며
        /// 구도(오프셋·회전·FOV)는 <see cref="SharedCombatCamera"/> 가 소유한다 (S-CLI-6BWF48).
        /// </summary>
        private void EnsureWorldCamera()
        {
            if (worldCamera != null)
            {
                return;
            }

            worldCamera = Camera.main;
            if (worldCamera != null) SharedCombatCamera.ConfigureProjection(worldCamera);
        }

        private void FollowLocalPlayer(in OnlineCoreLoopSnapshot snapshot)
        {
            EnsureWorldCamera();
            if (worldCamera == null)
            {
                return;
            }

            if (localPlayer == null)
            {
                NetworkCombatantMotion[] combatants = FindObjectsByType<NetworkCombatantMotion>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);
                for (int i = 0; i < combatants.Length; i++)
                {
                    if (combatants[i].AcceptsLocalInput)
                    {
                        localPlayer = combatants[i].transform;
                        localPlayerMotor = combatants[i].GetComponent<CombatantMotor>();
                        break;
                    }
                }
            }

            if (snapshot.Phase == OnlineCoreLoopPhase.Preparation
                && localPlayer != null
                && loggedLocalPreparationRound != snapshot.CurrentRound)
            {
                Vector3 localOffset = localPlayer.position - PreparationMapPosition;
                localOffset.y = 0f;
                if (localOffset.sqrMagnitude <= (ColosseumRadius + 2f) * (ColosseumRadius + 2f))
                {
                    loggedLocalPreparationRound = snapshot.CurrentRound;
                    Debug.Log(
                        $"[OnlineCoreLoopWorldPresenter] 로컬 콜로세움 진입 확인 — " +
                        $"라운드={snapshot.CurrentRound}, 위치={localPlayer.position}");
                }
            }

            // 빈사 중에는 유령이 곧 관찰자다 — 몸이 아니라 유령을 따라간다.
            Vector3 focus = localPlayer != null
                ? localPlayerMotor != null && localPlayerMotor.IsGhostActive
                    ? localPlayerMotor.GhostPosition
                    : localPlayer.position
                : snapshot.Phase is OnlineCoreLoopPhase.Loading or OnlineCoreLoopPhase.Preparation
                    ? PreparationMapPosition
                    : CombatMapPosition;
            SharedCombatCamera.Follow(worldCamera, focus);

            // ⚠️ 공용 구도 계약상 Follow 는 매 프레임 clearFlags 를 Skybox 로 되돌린다(S-CLI-6BWF48).
            //    맵이 아직 붙지 않은 구간에서는 그 하늘이 곧 Unity 기본 하늘이므로, 월드가 설 때까지만
            //    다시 눌러 둔다 (S15P21A606-199). 구도·렌즈는 건드리지 않는다.
            if (!worldReady)
            {
                SharedCombatCamera.SuppressSkyboxUntilWorldReady(worldCamera);
            }
        }

        /// <summary>
        /// 전용 게임 서버에서는 안개·미니맵 없이 봇 월드만 조립한다 (S15P21A606-321).
        /// </summary>
        /// <remarks>
        /// 서버에는 보는 사람도 카메라도 없어 <see cref="RefreshProductVisibility"/> 의 조건(로컬 관찰자·카메라)이
        /// 영원히 서지 않는다. 그러면 봇 두뇌가 월드를 못 받아 대기방 봇이 제자리에 선다. 봇 월드가 필요로 하는
        /// 것은 차폐물·전투원 목록·기준 위치 하나뿐이다.
        /// </remarks>
        private void RefreshHeadlessBotWorld(bool bindable, bool combatTerrain, Collider[] terrainBlockers)
        {
            if (!bindable || combatRoot == null || preparationRoot == null
                || terrainBlockers == null || terrainBlockers.Length == 0)
            {
                return;
            }

            NetworkCombatantMotion[] motions = FindObjectsByType<NetworkCombatantMotion>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            visibilityActorRoots.Clear();
            CombatantHealth viewer = null;
            for (int i = 0; i < motions.Length; i++)
            {
                NetworkCombatantMotion motion = motions[i];
                if (!IsLiveProductCombatant(motion)) continue;
                CombatantHealth health = motion.GetComponent<CombatantHealth>();
                if (health == null) continue;
                visibilityActorRoots.Add(motion.transform);
                if (viewer == null) viewer = health;
            }

            if (viewer == null) return;
            bool terrainChanged = !headlessBotWorldBound || visibilityUsesCombatTerrain != combatTerrain;
            headlessBotWorldBound = CombatWorldComposition.TryConfigureBotWorld(
                transform, viewer, visibilityActorRoots, terrainBlockers, IndividualCombatSideResolver.Instance,
                forceRebind: terrainChanged);
            visibilityUsesCombatTerrain = combatTerrain;
        }

        /// <summary>
        /// 제품 전투원은 Dev Combatants root 아래에 모인다는 보장이 없다. 각 live NetworkObject root와
        /// runner scene에 분리된 VFX tag를 공용 시야 프레젠터에 전달해 Host/client가 같은 표시 규칙을 쓴다.
        /// </summary>
        private void RefreshProductVisibility(in OnlineCoreLoopSnapshot snapshot)
        {
            // ⚠️ 전투 국면에서만 결합하던 시절, 준비맵에는 미니맵도 머리 위 표시도 없었다.
            //    전술 HUD 는 이 프레젠터가 소유하므로 여기서 Unbind 하면 캔버스째 사라진다.
            //    그래서 준비·정산까지 결합을 유지하고, 국면별로 다른 것은 아래 둘뿐이다 —
            //    어느 맵의 차폐물을 볼 것인가, 그리고 안개를 적용할 것인가.
            bool combatTerrain = snapshot.Phase is OnlineCoreLoopPhase.Combat
                or OnlineCoreLoopPhase.Summary;
            bool bindable = snapshot.Phase is OnlineCoreLoopPhase.Preparation
                or OnlineCoreLoopPhase.Combat
                or OnlineCoreLoopPhase.Summary;
            Collider[] terrainBlockers = combatTerrain ? combatTerrainBlockers : preparationTerrainBlockers;
            Transform terrainScope = combatTerrain ? combatRuntimeRoot : preparationRuntimeRoot;
            if (DedicatedServerRuntime.IsActive)
            {
                RefreshHeadlessBotWorld(bindable, combatTerrain, terrainBlockers);
                return;
            }

            if (!bindable || combatRoot == null || preparationRoot == null || worldCamera == null)
            {
                visibility?.Unbind();
                return;
            }

            NetworkCombatantMotion[] motions = FindObjectsByType<NetworkCombatantMotion>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            visibilityActorRoots.Clear();
            CombatantHealth localViewer = null;
            for (int i = 0; i < motions.Length; i++)
            {
                NetworkCombatantMotion motion = motions[i];
                if (!IsLiveProductCombatant(motion))
                {
                    continue;
                }

                CombatantHealth health = motion.GetComponent<CombatantHealth>();
                if (health == null)
                {
                    continue;
                }

                visibilityActorRoots.Add(motion.transform);
                if (motion.AcceptsLocalInput)
                {
                    localViewer = health;
                }
            }

            if (localViewer == null || visibilityActorRoots.Count == 0 || terrainBlockers.Length == 0)
            {
                visibility?.Unbind();
                return;
            }

            // 카메라·관찰자가 그대로면 재결합을 건너뛰므로, 차폐물 집합이 바뀌는 국면 전환에서는
            // 강제로 다시 결합해야 미니맵이 옛 맵 경계를 계속 그리지 않는다.
            bool terrainChanged = visibility == null || visibilityUsesCombatTerrain != combatTerrain;
            if (!CombatWorldComposition.TryConfigureVisibility(
                transform,
                worldCamera,
                localViewer,
                visibilityActorRoots,
                terrainBlockers,
                IndividualCombatSideResolver.Instance,
                out visibility,
                forceRebind: terrainChanged,
                chestScope: terrainScope))
            {
                visibility?.Unbind();
                if (!visibilityConfigurationFailureReported)
                {
                    visibilityConfigurationFailureReported = true;
                    Debug.LogWarning(
                        "[OnlineCoreLoopWorldPresenter] 제품 전투 시야를 조립하지 못했습니다. " +
                        "CombatTerrainFog shader/actor root/terrain blocker 계약과 Player build shader retention을 확인하세요.");
                }
                return;
            }

            visibilityConfigurationFailureReported = false;
            visibilityUsesCombatTerrain = combatTerrain;
            // 전장의 안개는 전장에서만 건다. 준비 콜로세움까지 가리면 상점·룰렛이 안 보인다.
            visibility.TerrainFogSuppressed = !combatTerrain;
        }

        private void ReleaseMatchWorld(bool unloadMapScenes)
        {
            // 맵을 내리는 순간부터 다음 맵이 붙을 때까지 월드는 비어 있다. 그 구간을 그대로 두면
            // 카메라가 Unity 기본 하늘을 그린다 (S15P21A606-199). 추적이 다시 시작되면
            // SharedCombatCamera.Follow 가 저절로 되돌린다.
            SharedCombatCamera.SuppressSkyboxUntilWorldReady(worldCamera);

            if (unloadMapScenes)
                mapRelease = ReleaseMapsAfterLoads(
                    preparationSceneLoad,
                    combatSceneLoad,
                    boundPreparationSceneKey,
                    boundCombatSceneKey);
            else
            {
                ObserveMapSceneTask(preparationSceneLoad, "준비 맵 로드");
                ObserveMapSceneTask(combatSceneLoad, "전투 맵 로드");
            }
            preparationSceneLoad = null;
            combatSceneLoad = null;
            if (combatRuntimeRoot != null) Destroy(combatRuntimeRoot.gameObject);
            combatRuntimeRoot = null;
            if (preparationRuntimeRoot != null) Destroy(preparationRuntimeRoot.gameObject);
            preparationRuntimeRoot = null;
            ClearSceneReferences();
        }

        private async Task ReleaseMapsAfterLoads(
            Task<MatchSceneLoadResult> preparation,
            Task<MatchSceneLoadResult> combat,
            MatchSceneKey preparationKey,
            MatchSceneKey combatKey)
        {
            try
            {
                if (preparation != null) await preparation;
                if (combat != null) await combat;
                await mapSceneProvider.UnloadAsync(preparationKey);
                await mapSceneProvider.UnloadAsync(combatKey);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[OnlineCoreLoopWorldPresenter] 맵 정리 실패: {exception.GetType().Name}");
            }
        }

        private void ClearSceneReferences()
        {
            ClearProductChestAuthority();
            visibility?.Unbind();
            visibility = null;
            combatChestVisuals.Clear();
            combatChestInteractionBounds.Clear();
            combatTerrainBlockers = Array.Empty<Collider>();
            preparationTerrainBlockers = Array.Empty<Collider>();
            visibilityActorRoots.Clear();
            // 연습 더미를 버리기 전에 공용 해석기를 놓는다. Local Play 매치 런타임의 Clear 와 같은 계약이다
            // — 참조를 먼저 지우면 다음 매치까지 지난 매치의 해석기가 붙어 있는 대상이 남는다.
            OnlineEconomyCoordinator.ReleasePracticeTarget(preparationTrainingDummy);
            preparationTrainingDummy = null;
            preparationRoot = null;
            combatRoot = null;
            preparationSpawnAnchors = null;
            combatSpawnAnchors = null;
            // 조립을 처음부터 다시 한다. 되돌리지 않으면 다음 매치가 중간 단계에서 시작해
            // 런타임 루트 없는 월드를 연다.
            worldAssemblyStep = WorldAssemblyStep.BindMapScenes;
            worldAssemblyFrame = -1;
            Array.Clear(AssemblyStepMs, 0, AssemblyStepMs.Length);
            phaseTransitionLogPending = false;
            legacyArenaRoot = null;
            worldCamera = null;
            localPlayer = null;
            localPlayerMotor = null;
            worldReady = false;
            worldBindingFailed = false;
            worldBindingFailureReason = string.Empty;
            boundMatchId = string.Empty;
            boundPreparationSceneKey = MatchSceneCatalog.DefaultPreparation;
            boundCombatSceneKey = MatchSceneCatalog.DefaultCombat;
            placementPending = false;
            remoteArrivalPending = false;
            lastCombatEntryReadyRound = 0;
            placementBlockedReason = string.Empty;
            arenaBoundsRefreshPending = false;
            loggedLocalPreparationRound = -1;
            visibilityConfigurationFailureReported = false;
            appliedPhase = (OnlineCoreLoopPhase)(-1);
            appliedRound = -1;
        }

        private void HideCombatChestVisuals()
        {
            for (int i = 0; i < combatChestVisuals.Count; i++)
            {
                if (combatChestVisuals[i] != null)
                {
                    combatChestVisuals[i].SetActive(false);
                }
            }
        }

        internal static int ResolveCombatSpawnIndex(int sortedPlayerIndex, int round)
        {
            int normalizedPlayer = Mathf.Clamp(sortedPlayerIndex, 0, MaximumParticipants - 1);
            int normalizedRound = Mathf.Max(1, round);
            return (normalizedPlayer * 3 + normalizedRound * 5) % MaximumParticipants;
        }

        private static bool IsProductMatchSceneLoaded()
        {
            Scene scene = SceneManager.GetSceneByName(SceneName.InGame);
            return scene.IsValid() && scene.isLoaded;
        }

        private static Transform FindRoot(Scene scene, string rootName)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == rootName)
                {
                    return roots[i].transform;
                }
            }

            return null;
        }

        private static Transform CreateRoot(string objectName, Vector3 position)
        {
            var rootObject = new GameObject(objectName);
            rootObject.transform.position = position;
            return rootObject.transform;
        }

        /// <summary>
        /// 준비 콜로세움을 <c>Visual</c> / <c>Collision</c> / <c>Activities</c> / <c>Anchors</c> 네 서브루트로
        /// 나눠 조립한다 (S-CLI-EYCKKC, S-GD-S5TBXY §4).
        /// </summary>
        /// <remarks>
        /// <b><c>Visual</c> 아래에는 Collider 를 하나도 두지 않는다.</b> 시각 메시를 게임플레이 경계로 쓰면
        /// 실 에셋 교체(S15P21A606-41)가 충돌 판정을 함께 바꿔 버린다. 충돌은 <c>Collision</c> 프록시만 담당한다.
        /// </remarks>
        // QUARANTINED — 추후 삭제 대상. EditMode migration fixtures only.
#if false // Legacy mock map: disconnected, scheduled for deletion.
        private static void BuildPreparationMap(Transform parent)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Legacy map generation is quarantined.");
            var sharedMaterials = new Dictionary<Color, Material>();
            EnvironmentArtCatalog artCatalog = FindFirstObjectByType<EnvironmentArtCatalog>(
                FindObjectsInactive.Include);
            Transform visualRoot = CreateChildRoot(parent, "Visual");
            Transform collisionRoot = CreateChildRoot(parent, "Collision");
            Transform activityRoot = CreateChildRoot(parent, "Activities");
            Transform anchorRoot = CreateChildRoot(parent, "Anchors");

            BuildPreparationVisual(visualRoot, sharedMaterials);
            BuildPreparationCollision(collisionRoot);
            BuildPreparationActivities(activityRoot, sharedMaterials, artCatalog);
            BuildPreparationSpawnAnchors(anchorRoot);
        }
#endif


        private static void BuildPreparationVisual(
            Transform parent,
            Dictionary<Color, Material> sharedMaterials)
        {
            CreateVisual(
                parent,
                "PreparationFloor",
                PrimitiveType.Cylinder,
                new Vector3(0f, -0.25f, 0f),
                new Vector3(ColosseumRadius * 2f, 0.25f, ColosseumRadius * 2f),
                SandFloorColor,
                sharedMaterials);

            Transform wallRoot = CreateChildRoot(parent, "ColosseumWallVisuals");
            Transform gateRoot = CreateChildRoot(parent, "GateVisuals");
            for (int i = 0; i < ColosseumWallSegmentCount; i++)
            {
                float angleDegrees = i * (360f / ColosseumWallSegmentCount);
                if (Array.IndexOf(GateWallSlots, i) >= 0)
                {
                    BuildColosseumGate(gateRoot, angleDegrees, sharedMaterials);
                    continue;
                }

                CreateVisual(
                    wallRoot,
                    $"ColosseumWallVisual_{i + 1:00}",
                    PrimitiveType.Cube,
                    RingPosition(ColosseumRadius, angleDegrees, ColosseumWallHeight * 0.5f),
                    new Vector3(RingSegmentLength(ColosseumRadius), ColosseumWallHeight, ColosseumWallThickness),
                    StoneWallColor,
                    sharedMaterials,
                    Quaternion.Euler(0f, angleDegrees, 0f));
            }

            Transform standRoot = CreateChildRoot(parent, "StandVisuals");
            for (int tier = 0; tier < StandTierRadii.Length; tier++)
            {
                Transform tierRoot = CreateChildRoot(standRoot, $"StandTier_{tier + 1}");
                float tierRadius = StandTierRadii[tier];
                float tierHeight = StandTierHeights[tier];
                for (int i = 0; i < ColosseumWallSegmentCount; i++)
                {
                    float angleDegrees = i * (360f / ColosseumWallSegmentCount);
                    CreateVisual(
                        tierRoot,
                        $"StandSegment_{i + 1:00}",
                        PrimitiveType.Cube,
                        RingPosition(tierRadius, angleDegrees, tierHeight * 0.5f),
                        new Vector3(RingSegmentLength(tierRadius), tierHeight, StandTierDepth),
                        StandTierColors[tier],
                        sharedMaterials,
                        Quaternion.Euler(0f, angleDegrees, 0f));
                }
            }

            Transform columnRoot = CreateChildRoot(parent, "ColumnVisuals");
            for (int i = 0; i < ColumnCount; i++)
            {
                float angleDegrees = ColumnAngleOffsetDegrees + (i * (360f / ColumnCount));
                CreateVisual(
                    columnRoot,
                    $"Column_{i + 1:00}",
                    PrimitiveType.Cylinder,
                    RingPosition(ColumnRingRadius, angleDegrees, ColumnHeight * 0.5f),
                    new Vector3(ColumnDiameter, ColumnHeight * 0.5f, ColumnDiameter),
                    StoneBeigeColor,
                    sharedMaterials);
            }

            // S15P21A606-41 이 테마 메시를 붙일 자리. 비워 두는 것이 계약이다 (S-GD-S5TBXY §4).
            CreateChildRoot(parent, "ThemeDecor");
        }

        /// <summary>
        /// 아치 게이트 하나를 세운다. 로컬 +X 가 접선, 로컬 +Z 가 바깥쪽이다.
        /// </summary>
        /// <remarks>
        /// 게이트 자리의 외벽 <b>시각</b> 세그먼트는 만들지 않지만 <b>충돌</b> 경계는 끊기지 않는다.
        /// 문은 닫힌 장식이라는 S-GD-S5TBXY 의 가정을 유지한다.
        /// </remarks>
        private static void BuildColosseumGate(
            Transform parent,
            float angleDegrees,
            Dictionary<Color, Material> sharedMaterials)
        {
            var gateRootObject = new GameObject($"Gate_{ResolveCardinalSuffix(angleDegrees)}");
            gateRootObject.transform.SetParent(parent, worldPositionStays: false);
            gateRootObject.transform.localPosition = RingPosition(ColosseumRadius, angleDegrees, 0f);
            gateRootObject.transform.localRotation = Quaternion.Euler(0f, angleDegrees, 0f);
            Transform gateRoot = gateRootObject.transform;

            var pierSize = new Vector3(0.9f, 3.6f, 0.9f);
            CreateVisual(gateRoot, "Pier_L", PrimitiveType.Cube, new Vector3(-GatePierOffset, 1.8f, 0f), pierSize, StoneBeigeColor, sharedMaterials);
            CreateVisual(gateRoot, "Pier_R", PrimitiveType.Cube, new Vector3(GatePierOffset, 1.8f, 0f), pierSize, StoneBeigeColor, sharedMaterials);
            CreateVisual(gateRoot, "Lintel", PrimitiveType.Cube, new Vector3(0f, 3.4f, 0f), new Vector3(5.6f, 0.8f, 0.9f), StoneBeigeColor, sharedMaterials);
            CreateVisual(gateRoot, "Keystone", PrimitiveType.Cube, new Vector3(0f, 3.9f, 0f), new Vector3(0.9f, 0.9f, 0.9f), StoneBeigeColor, sharedMaterials);
            CreateVisual(gateRoot, "ArchInterior", PrimitiveType.Cube, new Vector3(0f, 1.4f, -0.12f), new Vector3(4f, 2.8f, 0.3f), ArchInteriorColor, sharedMaterials);
        }

        private static void BuildPreparationCollision(Transform parent)
        {
            GameObject groundProxy = CreatePrimitive(
                parent,
                "PreparationGroundProxy",
                PrimitiveType.Cylinder,
                new Vector3(0f, -0.25f, 0f),
                new Vector3(ColosseumRadius * 2f, 0.25f, ColosseumRadius * 2f),
                SandFloorColor);
            UseVisibleMeshForStaticCollision(groundProxy);
            DestroyCreatedComponent(groundProxy.GetComponent<MeshRenderer>());

            Transform boundaryRoot = CreateChildRoot(parent, "PreparationBoundaryProxy");
            for (int i = 0; i < ColosseumWallSegmentCount; i++)
            {
                float angleDegrees = i * (360f / ColosseumWallSegmentCount);
                CreateBoxProxy(
                    boundaryRoot,
                    $"BoundarySegmentProxy_{i + 1:00}",
                    RingPosition(ColosseumRadius, angleDegrees, ColosseumWallHeight * 0.5f),
                    new Vector3(RingSegmentLength(ColosseumRadius), ColosseumWallHeight, ColosseumWallThickness),
                    Quaternion.Euler(0f, angleDegrees, 0f));
            }

            // 이름 네 개는 CombatantMotor 가 빈사 유령 경계를 찾는 계약이다 (S-QA-A8PNYN). 바꾸지 않는다.
            CreateCardinalGhostBounds(boundaryRoot, ColosseumRadius + 0.5f, ColosseumRadius + 0.5f);

            Transform obstacleRoot = CreateChildRoot(parent, "PreparationObstacleProxies");
            // 강화 구역은 시각물마다 프록시를 하나씩 둔다 (S-CLI-9V5D68). 하나의 큰 상자로 감싸면
            // 모루 남쪽 모서리처럼 아무것도 없는 공간이 막힌다.
            // Anvil bounds     x [-12.508, -11.392] z [0.461, 1.410]
            // Workbench bounds x [-11.014,  -9.991] z [-1.010, 1.010]
            // 두 시각물 사이 틈 0.378m 는 캐릭터 지름 1.0m 보다 좁아 통과할 수 없으므로 막지 않는다.
            CreateBoxProxy(obstacleRoot, "AnvilProxy", new Vector3(-11.95f, 1f, 0.94f), new Vector3(1.16f, 2f, 0.99f));
            CreateBoxProxy(obstacleRoot, "WorkbenchProxy", new Vector3(-10.5f, 1f, 0f), new Vector3(1.07f, 2f, 2.06f));
            // ShopProxy 는 매대(Stall) 본체만 막는다. Barrel·Crate·Chest 는 Collider 가 없는
            // 장식이라 통과가 정상이다. 매대 시각 bounds x [10.528, 11.461] · z [-0.922, 0.922] 를
            // 감싸는 최소 크기이며, 캐릭터 반지름 0.5m 기준 매대 앞 약 0.5m 까지 접근할 수 있다.
            CreateBoxProxy(obstacleRoot, "ShopProxy", new Vector3(11f, 1.4f, 0f), new Vector3(1.13f, 2.8f, 2.04f));
            // 룰렛은 서 있는 얇은 판이라 시각물만으로는 몸이 통과했다 (S-CLI-T6VSD2).
            // 래퍼(PrepareGambleRouletteVisual)는 시각 전용이므로 이동 충돌은 여기서만 가진다.
            // 래퍼 Renderer 실측(슬롯 좌표 (0, 0.12, 0) 기준):
            //   Frame_Foot_L/R (받침대) x [-1.821, 1.821] z [-0.209, 0.616]
            //   Frame_Post_L/R (기둥)   x [-1.508, 1.508] y  [0.121, 2.886]
            //   Wheel_Outer_Rim(회전판) x [-1.295, 1.295] y  [0.281, 2.871]
            // 회전판 아랫면이 y 0.281 이라 받침대 사이도 실제로 막혀 있다. 프레임 전체
            // footprint 3.56 m^2 만 막으며 폴백 원판(지름 6m = 28.3 m^2)을 기준으로 하지 않는다.
            CreateBoxProxy(obstacleRoot, "RouletteProxy", new Vector3(0f, 1f, 0.131f), new Vector3(3.67f, 2f, 0.97f));
        }

        /// <summary>
        /// 룰렛·모루·상점·집결 공간·허수아비는 <b>배치용 MOCK</b> 이다. 상호작용은 구현돼 있지 않다.
        /// </summary>
        private static void BuildPreparationActivities(
            Transform parent,
            Dictionary<Color, Material> sharedMaterials,
            EnvironmentArtCatalog artCatalog)
        {
            CreateArtVisualOrFallback(
                artCatalog,
                EnvironmentVisualSlot.Gamble,
                parent,
                "Roulette_Center",
                new Vector3(0f, 0.12f, 0f),
                PrimitiveType.Cylinder,
                Vector3.one,
                new Vector3(6f, 0.12f, 6f),
                new Color(0.09f, 0.65f, 0.58f),
                sharedMaterials,
                fallbackKeepsDisabledCollider: true);
            // 룰렛 시각물 최상단이 y 3.099 다. 0.45 에서는 라벨이 회전판 한가운데를 가렸다.
            // 3.25 로 올려 룰렛 위에 띄운다 (S-CLI-WFVN75). 앵커가 LowerCenter 라 위로 자란다.

            // 모루·상점의 충돌은 PreparationObstacleProxies 가 가진다. 여기서는 시각만 남긴다.
            CreateArtVisualOrFallback(
                artCatalog,
                EnvironmentVisualSlot.Upgrade,
                parent,
                "Anvil_Left",
                new Vector3(-11f, 1f, 0f),
                PrimitiveType.Cube,
                Vector3.one,
                new Vector3(4f, 2f, 4f),
                new Color(0.34f, 0.39f, 0.48f),
                sharedMaterials);

            CreateArtVisualOrFallback(
                artCatalog,
                EnvironmentVisualSlot.Shop,
                parent,
                "Shop_Right",
                new Vector3(11f, 1.4f, 0f),
                PrimitiveType.Cube,
                Vector3.one,
                new Vector3(6f, 2.8f, 4f),
                new Color(0.63f, 0.18f, 0.17f),
                sharedMaterials);

            CreatePrimitive(
                parent,
                "GatheringArea_Bottom",
                PrimitiveType.Cube,
                new Vector3(0f, 0.04f, -12f),
                new Vector3(18f, 0.08f, 4f),
                new Color(0.10f, 0.38f, 0.66f),
                hasCollision: false);

            CreateTrainingDummy(parent, artCatalog);
            CreateWorldLabel(parent, "TRAINING DUMMY", new Vector3(0f, 2.75f, 12f));
        }

        /// <summary>
        /// 준비 라운드 연습 더미가 <b>전투 라운드와 같은</b> 공용 축복·피해 계산을 쓰도록 유지한다.
        /// Host 는 경제 어댑터의 해석기를 받고, client 는 받지 않아 피해를 다시 계산하지 않는다.
        /// </summary>
        private void RefreshPreparationPracticeTarget()
        {
            if (preparationTrainingDummy == null)
            {
                return;
            }

            OnlineEconomyCoordinator.RefreshPracticeTargetStats(preparationTrainingDummy);
        }

        /// <summary>
        /// 연습 더미의 측정은 <b>Host 만</b> 만든다. client 에서 같은 명중 경계가 열려도 여기서 막히므로
        /// 피해가 피어마다 따로 계산되지 않는다 (D-ONL-7HSVXA · D-ONL-H9Z4TW).
        /// </summary>
        private sealed class PreparationPracticeDamageAuthority : IDamageAuthority, ICombatStateAuthority
        {
            public bool HasCombatStateAuthority =>
                NetworkManager.HasInstance && NetworkManager.Instance.IsInRoom && NetworkManager.Instance.IsHost;

            public bool CanApplyDamage(CombatantId target, in DamageInfo damageInfo) => HasCombatStateAuthority;
        }

        private static Transform CreateTrainingDummy(Transform parent, EnvironmentArtCatalog artCatalog)
        {
            // 허수아비만 Collider 를 유지한다. 명중 판정이 이 Collider 로 성립한다.
            GameObject trainingDummy = CreatePrimitive(
                parent,
                "PreparationTrainingDummy",
                PrimitiveType.Capsule,
                new Vector3(0f, 1f, 12f),
                new Vector3(1.1f, 1.1f, 1.1f),
                new Color(0.94f, 0.68f, 0.16f));
            Combatant dummyIdentity = trainingDummy.AddComponent<Combatant>();
            dummyIdentity.SetId(PreparationTrainingDummyId);
            CombatantHealth dummyHealth = trainingDummy.AddComponent<CombatantHealth>();
            dummyHealth.SetTrainingDummy(true);
            dummyHealth.SetDamageAuthority(new PreparationPracticeDamageAuthority());
            trainingDummy.AddComponent<CombatSandboxHitFeedback>();
            if (artCatalog != null
                && artCatalog.TryCreateVisual(
                    EnvironmentVisualSlot.TrainingVisual,
                    trainingDummy.transform,
                    "Visual",
                    Vector3.zero,
                    Vector3.one,
                    Quaternion.identity,
                    out GameObject _))
            {
                Renderer fallbackRenderer = trainingDummy.GetComponent<Renderer>();
                if (fallbackRenderer != null)
                {
                    fallbackRenderer.enabled = false;
                }
            }
            return trainingDummy.transform;
        }

        /// <summary>
        /// 준비 스폰 위치를 Transform 앵커로 노출한다 (S-GD-S5TBXY §4).
        /// </summary>
        /// <remarks>
        /// <see cref="TryPlaceAuthorityPlayers"/> 는 이 앵커를 통해서만 좌표를 얻는다.
        /// <see cref="PreparationSpawnLocalPositions"/> 는 앵커를 처음 만들 때 쓰는 시드일 뿐이다.
        /// </remarks>
        private static void BuildPreparationSpawnAnchors(Transform parent) =>
            CreateAnchors(parent, PreparationSpawnAnchorPrefix, PreparationSpawnLocalPositions);

        /// <summary>
        /// 위치 계약만 담당하는 빈 Transform 을 만든다. Renderer 도 Collider 도 붙이지 않는다.
        /// </summary>
        private static void CreateAnchors(Transform parent, string anchorPrefix, Vector3[] localPositions)
        {
            for (int i = 0; i < localPositions.Length; i++)
            {
                var anchorObject = new GameObject($"{anchorPrefix}{i + 1:00}");
                anchorObject.transform.SetParent(parent, worldPositionStays: false);
                anchorObject.transform.localPosition = localPositions[i];
            }
        }

        /// <summary>
        /// 맵 루트 아래 <c>Anchors/{prefix}01..08</c> 을 찾아 배열로 돌려준다. 하나라도 없으면 실패한다.
        /// </summary>
        public static bool TryResolveSpawnAnchors(
            Transform mapRoot,
            string anchorPrefix,
            out Transform[] anchors)
        {
            anchors = null;
            var resolved = new List<Transform>(MaximumParticipants);
            if (!MatchSceneContentResolver.TryCollectAnchors(
                    mapRoot, anchorPrefix, MaximumParticipants, resolved))
            {
                return false;
            }

            anchors = resolved.ToArray();
            return true;
        }

        /// <summary>
        /// 한 참가자의 권위 배치 좌표를 정한다.
        /// </summary>
        /// <remarks>
        /// <b>배치 좌표의 source of truth 는 런타임 Transform 앵커다</b> (S-GD-S5TBXY §4).
        /// <see cref="PreparationSpawnLocalPositions"/> 와 <see cref="CombatSpawnLocalPositions"/> 는
        /// 앵커를 처음 만들 때 쓰는 <b>시드</b>일 뿐이고, 앵커가 생긴 뒤에 앵커를 옮기면 배치도 따라간다.
        /// 라운드별 자리 배정 규칙은 <see cref="ResolveCombatSpawnIndex"/> 가 그대로 소유한다.
        /// </remarks>
        public static bool TryResolveAuthorityPlacement(
            OnlineCoreLoopPhase phase,
            int sortedPlayerIndex,
            int round,
            Transform[] preparationAnchors,
            Transform[] combatAnchors,
            out Vector3 position)
        {
            if (phase == OnlineCoreLoopPhase.Preparation)
            {
                return TryReadAnchorPosition(preparationAnchors, sortedPlayerIndex, out position);
            }

            int spawnIndex = ResolveCombatSpawnIndex(sortedPlayerIndex, round);
            return TryReadAnchorPosition(combatAnchors, spawnIndex, out position);
        }

        private static bool TryReadAnchorPosition(Transform[] anchors, int index, out Vector3 position)
        {
            position = default;
            if (anchors == null || index < 0 || index >= anchors.Length || anchors[index] == null)
            {
                return false;
            }

            position = anchors[index].position;
            return true;
        }

        private static Transform CreateChildRoot(Transform parent, string objectName)
        {
            var rootObject = new GameObject(objectName);
            rootObject.transform.SetParent(parent, worldPositionStays: false);
            rootObject.transform.localPosition = Vector3.zero;
            rootObject.transform.localRotation = Quaternion.identity;
            return rootObject.transform;
        }

        /// <summary>
        /// L자·T자 복합 엄폐물의 논리 그룹. 그룹 자신은 표시·충돌을 갖지 않고
        /// 자식 세그먼트가 Hard Cover 충돌을 갖는다 (S-GD-9R8AD3).
        /// </summary>
        private static Transform CreateCoverGroup(Transform parent, string groupName, Vector3 localPosition) =>
            CreateCoverGroup(parent, groupName, localPosition, Quaternion.identity);

        /// <summary>
        /// 회전이 필요한 논리 그룹이다. 회전은 <b>그룹에만</b> 적용하고 자식 세그먼트의
        /// 로컬 회전은 identity 로 남긴다 — 세그먼트 좌표를 그룹 기준으로 읽을 수 있어야 한다.
        /// </summary>
        private static Transform CreateCoverGroup(
            Transform parent,
            string groupName,
            Vector3 localPosition,
            Quaternion localRotation)
        {
            Transform group = CreateChildRoot(parent, groupName);
            group.localPosition = localPosition;
            group.localRotation = localRotation;
            return group;
        }

        private static Vector3 RingPosition(float radius, float angleDegrees, float height)
        {
            float angle = angleDegrees * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(angle) * radius, height, Mathf.Cos(angle) * radius);
        }

        /// <summary>이웃 세그먼트와 0.3 겹치게 해 원 근사에서 틈이 보이지 않게 한다.</summary>
        private static float RingSegmentLength(float radius) =>
            (2f * Mathf.PI * radius / ColosseumWallSegmentCount) + 0.3f;

        private static string ResolveCardinalSuffix(float angleDegrees) => angleDegrees switch
        {
            0f => "N",
            90f => "E",
            180f => "S",
            _ => "W",
        };

        /// <summary>Collider 없는 순수 시각 오브젝트. 같은 색은 머티리얼 하나를 공유한다.</summary>
        private static GameObject CreateVisual(
            Transform parent,
            string objectName,
            PrimitiveType primitiveType,
            Vector3 localPosition,
            Vector3 localScale,
            Color color,
            Dictionary<Color, Material> sharedMaterials,
            Quaternion? localRotation = null)
        {
            GameObject visualObject = CreatePrimitive(
                parent,
                objectName,
                primitiveType,
                localPosition,
                localScale,
                color,
                localRotation,
                hasCollision: true,
                sharedMaterial: ResolveSharedMaterial(sharedMaterials, color));
            DestroyCreatedComponent(visualObject.GetComponent<Collider>());
            return visualObject;
        }

        private static GameObject CreateArtVisualOrFallback(
            EnvironmentArtCatalog artCatalog,
            EnvironmentVisualSlot slot,
            Transform parent,
            string objectName,
            Vector3 localPosition,
            PrimitiveType fallbackPrimitiveType,
            Vector3 artScale,
            Vector3 fallbackScale,
            Color fallbackColor,
            Dictionary<Color, Material> sharedMaterials,
            Quaternion? localRotation = null,
            bool fallbackKeepsDisabledCollider = false)
        {
            Quaternion rotation = localRotation ?? Quaternion.identity;
            if (artCatalog != null
                && artCatalog.TryCreateVisual(
                    slot,
                    parent,
                    objectName,
                    localPosition,
                    artScale,
                    rotation,
                    out GameObject artInstance))
            {
                return artInstance;
            }

            if (fallbackKeepsDisabledCollider)
            {
                return CreatePrimitive(
                    parent,
                    objectName,
                    fallbackPrimitiveType,
                    localPosition,
                    fallbackScale,
                    fallbackColor,
                    localRotation,
                    hasCollision: false,
                    sharedMaterial: ResolveSharedMaterial(sharedMaterials, fallbackColor));
            }

            return CreateVisual(
                parent,
                objectName,
                fallbackPrimitiveType,
                localPosition,
                fallbackScale,
                fallbackColor,
                sharedMaterials,
                localRotation);
        }

        private static Material ResolveSharedMaterial(Dictionary<Color, Material> sharedMaterials, Color color)
        {
            if (sharedMaterials.TryGetValue(color, out Material cached) && cached != null)
            {
                return cached;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader)
            {
                color = color,
                name = $"Runtime_Preparation_{ColorUtility.ToHtmlStringRGB(color)}_Material",
            };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            sharedMaterials[color] = material;
            return material;
        }

        private static void CreateBoxProxy(
            Transform parent,
            string objectName,
            Vector3 localPosition,
            Vector3 size,
            Quaternion? localRotation = null)
        {
            var proxyObject = new GameObject(objectName);
            proxyObject.transform.SetParent(parent, worldPositionStays: false);
            proxyObject.transform.localPosition = localPosition;
            proxyObject.transform.localRotation = localRotation ?? Quaternion.identity;
            BoxCollider proxy = proxyObject.AddComponent<BoxCollider>();
            proxy.size = size;
        }

        /// <summary>
        /// 방금 만든 오브젝트의 컴포넌트를 즉시 없앤다.
        /// </summary>
        /// <remarks>
        /// <see cref="Object.Destroy(Object)"/> 는 프레임 끝까지 지연되므로 그 사이 Collider 가 살아 있다.
        /// 먼저 <c>enabled</c> 를 끄고 지운다. Edit 모드(테스트)에서는 지연 파괴가 동작하지 않아 즉시 파괴한다.
        /// </remarks>
        private static void DestroyCreatedComponent(Component component)
        {
            if (component == null)
            {
                return;
            }

            if (component is Collider colliderComponent)
            {
                colliderComponent.enabled = false;
            }
            else if (component is Renderer rendererComponent)
            {
                rendererComponent.enabled = false;
            }

            if (Application.isPlaying)
            {
                Destroy(component);
            }
            else
            {
                DestroyImmediate(component);
            }
        }

        // QUARANTINED — 추후 삭제 대상. EditMode migration fixtures only.
#if false // Legacy mock map: disconnected, scheduled for deletion.
        private static void BuildCombatMap(Transform parent)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Legacy map generation is quarantined.");
            EnvironmentArtCatalog artCatalog = FindFirstObjectByType<EnvironmentArtCatalog>(
                FindObjectsInactive.Include);
            Transform artRoot = artCatalog != null
                ? CreateChildRoot(parent, "ArtVisuals")
                : null;

            // 바닥·외벽의 Collider 는 v2.2 계약 그대로다. 색과 Renderer 표시 여부만 아트가 건드린다.
            GameObject battlefieldFloor = CreatePrimitive(
                parent,
                "BattlefieldFloor",
                PrimitiveType.Cube,
                new Vector3(0f, -0.25f, 0f),
                new Vector3(48f, 0.5f, 28f),
                BattlefieldEarthFloorColor);
            GameObject northWall = CreatePrimitive(parent, "NorthWall", PrimitiveType.Cube, new Vector3(0f, 1.5f, 14f), new Vector3(48f, 3f, 0.6f), new Color(0.30f, 0.34f, 0.42f));
            GameObject southWall = CreatePrimitive(parent, "SouthWall", PrimitiveType.Cube, new Vector3(0f, 1.5f, -14f), new Vector3(48f, 3f, 0.6f), new Color(0.30f, 0.34f, 0.42f));
            GameObject eastWall = CreatePrimitive(parent, "EastWall", PrimitiveType.Cube, new Vector3(24f, 1.5f, 0f), new Vector3(0.6f, 3f, 28f), new Color(0.30f, 0.34f, 0.42f));
            GameObject westWall = CreatePrimitive(parent, "WestWall", PrimitiveType.Cube, new Vector3(-24f, 1.5f, 0f), new Vector3(0.6f, 3f, 28f), new Color(0.30f, 0.34f, 0.42f));

            // S-GD-9R8AD3 — 실루엣(L·원통·T)은 S-GD-RFRVZW 그대로 두고 위치·회전만 바꾼다.
            // 서쪽은 장축을 진입로와 나란히 25° 기울인 사슬로 두어 연속 이동을 허용하고,
            // 동쪽은 장축을 진입로와 수직으로 두어 한 번 넘은 뒤 노출 구간이 남게 한다.
            // 북/남(x≈0) 직선 경로는 엄폐물을 두지 않아 계속 개방한다.
            // D1(S-GD-8MSZMP): Full Cover 5그룹은 3.2m(S-GD-KXZQA3), Connector 5그룹은 2.0m 다. 둘 다 Hard Cover 라
            // 공격 LOS 차단 규칙은 같고, 높이 차이는 고정 쿼터뷰에서의 시각 은폐량만 바꾼다.
            Transform westNorthCover = CreateCoverGroup(
                parent,
                "Cover_West_North",
                new Vector3(-11.5f, 0f, 5f),
                Quaternion.Euler(0f, 25f, 0f));
            // 25° 회전이라 두께를 키우면 AABB 가 중앙 경계(x = -8)로 밀려온다. 1.2 -> 1.6 이 여유 0.19m 를
            // 남기는 안전 최댓값이고, 1.8 은 0.15m 밖에 안 남아 v2.2 침범 사고를 반복할 위험이 있다.
            GameObject westNorthLong = CreatePrimitive(westNorthCover, "Segment_LongArm", PrimitiveType.Cube, new Vector3(0f, FullCoverCenterY, 0.6f), new Vector3(6f, FullCoverHeight, 1.6f), BattlefieldCoverColor);
            AttachCombatArtVisual(artCatalog, EnvironmentVisualSlot.CombatCoverVisual, artRoot, parent, westNorthLong.transform);
            GameObject westNorthShort = CreatePrimitive(westNorthCover, "Segment_ShortArm", PrimitiveType.Cube, new Vector3(-2.4f, FullCoverCenterY, -0.65f), new Vector3(1.2f, FullCoverHeight, 2.5f), BattlefieldCoverColor);
            // 짧은 팔만 금 간 벽으로 바꿔 Connector 의 매끈한 벽과 실루엣을 구분한다.
            AttachCombatArtVisual(artCatalog, EnvironmentVisualSlot.CombatArenaWallVisual, artRoot, parent, westNorthShort.transform);

            // Cylinder 프리미티브의 기본 CapsuleCollider 는 높이가 지름보다 짧으면 구로 클램프되어
            // 높이 2m 계약을 위아래로 0.25m 어기고, 높이에 따라 차단 반지름이 줄어든다.
            // PreparationGroundProxy 와 같은 방식으로 보이는 원통 메시를 그대로 충돌로 쓴다.
            GameObject westMidCover = CreatePrimitive(
                parent,
                "Cover_West_Mid",
                PrimitiveType.Cylinder,
                new Vector3(-9.5f, 1f, 0f),
                new Vector3(2.5f, 1f, 2.5f),
                BattlefieldCoverColor);
            UseVisibleMeshForStaticCollision(westMidCover);
            AttachCombatArtVisual(artCatalog, EnvironmentVisualSlot.CombatColumnVisual, artRoot, parent, westMidCover.transform);

            Transform westSouthCover = CreateCoverGroup(
                parent,
                "Cover_West_South",
                new Vector3(-11.5f, 0f, -5f),
                Quaternion.Euler(0f, -25f, 0f));
            GameObject westSouthLong = CreatePrimitive(westSouthCover, "Segment_LongArm", PrimitiveType.Cube, new Vector3(0f, FullCoverCenterY, -0.6f), new Vector3(6f, FullCoverHeight, 1.6f), BattlefieldCoverColor);
            AttachCombatArtVisual(artCatalog, EnvironmentVisualSlot.CombatCoverVisual, artRoot, parent, westSouthLong.transform);
            GameObject westSouthShort = CreatePrimitive(westSouthCover, "Segment_ShortArm", PrimitiveType.Cube, new Vector3(-2.4f, FullCoverCenterY, 0.65f), new Vector3(1.2f, FullCoverHeight, 2.5f), BattlefieldCoverColor);
            AttachCombatArtVisual(artCatalog, EnvironmentVisualSlot.CombatArenaWallVisual, artRoot, parent, westSouthShort.transform);

            // 동쪽 단일 엄폐물은 장축을 z 로 두므로 회전이 필요 없다.
            GameObject eastNorthCover = CreatePrimitive(parent, "Cover_East_North", PrimitiveType.Cube, new Vector3(14f, 1f, 5.8f), new Vector3(1.5f, 2f, 4f), BattlefieldCoverColor);
            AttachCombatArtVisual(artCatalog, EnvironmentVisualSlot.CombatCoverVisual, artRoot, parent, eastNorthCover.transform);
            GameObject eastSouthCover = CreatePrimitive(parent, "Cover_East_South", PrimitiveType.Cube, new Vector3(14f, 1f, -5.8f), new Vector3(1.5f, 2f, 4f), BattlefieldCoverColor);
            AttachCombatArtVisual(artCatalog, EnvironmentVisualSlot.CombatCoverVisual, artRoot, parent, eastSouthCover.transform);

            // 회전이 없어 두께를 1.8 로 키워도 AABB 가 예측 가능하다. 서쪽 끝(x = 15.4)은 그대로 두고
            // 바깥쪽(x = 18.5)으로만 두꺼워져 외벽 여유 5.2m 를 유지한다.
            Transform eastMidCover = CreateCoverGroup(parent, "Cover_East_Mid", new Vector3(17f, 0f, 0f));
            GameObject eastCrossbar = CreatePrimitive(eastMidCover, "Segment_Crossbar", PrimitiveType.Cube, new Vector3(0.6f, FullCoverCenterY, 0f), new Vector3(1.8f, FullCoverHeight, 4.5f), BattlefieldCoverColor);
            AttachCombatArtVisual(artCatalog, EnvironmentVisualSlot.CombatCoverVisual, artRoot, parent, eastCrossbar.transform);
            GameObject eastStem = CreatePrimitive(eastMidCover, "Segment_Stem", PrimitiveType.Cube, new Vector3(-0.7f, FullCoverCenterY, 0f), new Vector3(1.8f, FullCoverHeight, 1.8f), BattlefieldCoverColor);
            AttachCombatArtVisual(artCatalog, EnvironmentVisualSlot.CombatArenaWallVisual, artRoot, parent, eastStem.transform);

            // Plan A 신규 2그룹 (S-GD-7KW8YT). 중앙 x[-8,8]·z[-4,4] 와 북쪽 z>=8 개방대 밖에만 둔다.
            // 회전은 identity 다 — v2.2 에서 25° L 자의 AABB 가 예상보다 커져 중앙을 침범한 전례가 있어
            // 신규 그룹은 축 정렬로 footprint 를 정확히 통제한다.

            // 남쪽 중앙 진입을 둘로 나누는 엇갈린 벽. 서쪽 사슬 대각선의 동쪽에 두어
            // 서쪽 스폰의 우회 거리를 늘리지 않는다(16m 상한까지 여유가 1.43m 뿐이다).
            Transform southWestCover = CreateCoverGroup(parent, "Cover_South_West", new Vector3(-3f, 0f, -8f));
            GameObject southWestWest = CreatePrimitive(southWestCover, "Segment_West", PrimitiveType.Cube, new Vector3(-0.8f, 1f, 0.6f), new Vector3(2f, 2f, 1.2f), BattlefieldCoverColor);
            AttachCombatArtVisual(artCatalog, EnvironmentVisualSlot.CombatCoverVisual, artRoot, parent, southWestWest.transform);
            GameObject southWestSouth = CreatePrimitive(southWestCover, "Segment_South", PrimitiveType.Cube, new Vector3(0.6f, 1f, -1f), new Vector3(1.2f, 2f, 2.4f), BattlefieldCoverColor);
            AttachCombatArtVisual(artCatalog, EnvironmentVisualSlot.CombatCoverVisual, artRoot, parent, southWestSouth.transform);

            // 동쪽 z≈0 사선을 끊는 ㄱ자. 팔 끝을 x = 10.7 로 당겨 ChestAnchor_07(12,0) 과 1.3m,
            // Cover_East_North 와 2.55m 통로를 확보한다. 중앙을 봉쇄하지 않도록 서쪽 끝은 x = 8.6 이다.
            Transform centerEastCover = CreateCoverGroup(parent, "Cover_Center_East", new Vector3(9.8f, 0f, 0.4f));
            GameObject centerEastSpine = CreatePrimitive(centerEastCover, "Segment_Spine", PrimitiveType.Cube, new Vector3(-0.6f, 1f, 0.05f), new Vector3(1.2f, 2f, 4.2f), BattlefieldCoverColor);
            AttachCombatArtVisual(artCatalog, EnvironmentVisualSlot.CombatCoverVisual, artRoot, parent, centerEastSpine.transform);
            GameObject centerEastArm = CreatePrimitive(centerEastCover, "Segment_Arm", PrimitiveType.Cube, new Vector3(0.35f, 1f, 1.6f), new Vector3(1.1f, 2f, 1.2f), BattlefieldCoverColor);
            AttachCombatArtVisual(artCatalog, EnvironmentVisualSlot.CombatCoverVisual, artRoot, parent, centerEastArm.transform);

            // D1 신규 Full Cover 2그룹 (S-GD-8MSZMP). 중앙 보호 박스 z ±4 와 북쪽 개방대 z >= 8 사이의
            // 빈 4m 띠에 넣어, 중앙을 막지 않으면서 넓은 바닥을 통로형으로 나눈다. 회전은 identity 다.

            // 북쪽 — 두꺼운 직선벽 + 서쪽 끝 기둥.
            Transform northCenterCover = CreateCoverGroup(parent, "FC_North_Center", new Vector3(3.5f, 0f, 5.8f));
            GameObject northCenterWall = CreatePrimitive(northCenterCover, "Segment_Wall", PrimitiveType.Cube, new Vector3(0f, FullCoverCenterY, 0.4f), new Vector3(4.4f, FullCoverHeight, 1.8f), BattlefieldCoverColor);
            AttachCombatArtVisual(artCatalog, EnvironmentVisualSlot.CombatCoverVisual, artRoot, parent, northCenterWall.transform);
            GameObject northCenterPillar = CreatePrimitive(northCenterCover, "Segment_Pillar", PrimitiveType.Cube, new Vector3(-1.6f, FullCoverCenterY, -0.65f), new Vector3(1.2f, FullCoverHeight, 1.3f), BattlefieldCoverColor);
            AttachCombatArtVisual(artCatalog, EnvironmentVisualSlot.CombatArenaWallVisual, artRoot, parent, northCenterPillar.transform);

            // 남쪽 — 북쪽과 대칭 복사하지 않는다. 짧은 ㄱ자에 파손 벽 끝처리를 쓴다.
            Transform southCenterCover = CreateCoverGroup(parent, "FC_South_Center", new Vector3(3.6f, 0f, -6f));
            GameObject southCenterSpine = CreatePrimitive(southCenterCover, "Segment_Spine", PrimitiveType.Cube, new Vector3(0f, FullCoverCenterY, -0.4f), new Vector3(4f, FullCoverHeight, 1.8f), BattlefieldCoverColor);
            AttachCombatArtVisual(artCatalog, EnvironmentVisualSlot.CombatCoverVisual, artRoot, parent, southCenterSpine.transform);
            GameObject southCenterBroken = CreatePrimitive(southCenterCover, "Segment_Broken", PrimitiveType.Cube, new Vector3(1.3f, FullCoverCenterY, 0.7f), new Vector3(1.4f, FullCoverHeight, 1.2f), BattlefieldCoverColor);
            AttachCombatArtVisual(artCatalog, EnvironmentVisualSlot.CombatArenaWallVisual, artRoot, parent, southCenterBroken.transform);
            // 끝 기둥 3그룹 6개를 한 번에 세운다. 위치는 CombatFullCoverEndPillarWalls 표가 정한다.
            AttachFullCoverEndPillars(artCatalog, artRoot, parent);

            // 벽 횃불 6개. artRoot(Combat Art Root) 아래라 맵과 함께 한 번만 생성되고,
            // 라운드 갱신·상자 흐름과 섞이지 않는다.
            for (int i = 0; i < CombatWallTorches.Length; i++)
            {
                (string torchName, Vector3 position, float yaw) = CombatWallTorches[i];
                CreateArtOnlyVisual(
                    artCatalog,
                    EnvironmentVisualSlot.CombatTorchVisual,
                    artRoot,
                    torchName,
                    position,
                    Vector3.one,
                    Quaternion.Euler(0f, yaw, 0f));
            }

            // 내부 폐허 장식 — 외벽 아래·모서리·Full Cover 기초에만. 중앙과 x=0 통로는 비워 둔다.
            for (int i = 0; i < CombatInteriorRubble.Length; i++)
            {
                (string rubbleName, Vector3 position, float scale, float yaw) = CombatInteriorRubble[i];
                CreateArtOnlyVisual(
                    artCatalog,
                    EnvironmentVisualSlot.CombatRubbleVisual,
                    artRoot,
                    rubbleName,
                    position,
                    new Vector3(scale, scale, scale),
                    Quaternion.Euler(0f, yaw, 0f));
            }

            CreatePrimitive(
                parent,
                "CombatCenterMark",
                PrimitiveType.Cylinder,
                new Vector3(0f, 0.04f, 0f),
                new Vector3(5f, 0.04f, 5f),
                new Color(0.70f, 0.15f, 0.18f),
                hasCollision: false);

            // 기존 바닥·벽·엄폐물은 그대로 두고 위치 계약만 별도 루트로 분리한다 (S-GD-S5TBXY §4).
            Transform anchorRoot = CreateChildRoot(parent, AnchorRootName);
            CreateAnchors(anchorRoot, CombatSpawnAnchorPrefix, CombatSpawnLocalPositions);
            CreateAnchors(anchorRoot, ChestAnchorPrefix, ChestAnchorLocalPositions);

            // 외벽 바깥 지형은 카탈로그가 없어도 만든다. 없으면 전장 밖이 빈 하늘 지면으로 보인다 (S-CLI-NY3D7P).
            BuildCombatOuterGround(artCatalog, parent);

            if (artRoot != null)
            {
                // 3차 마감 패스 (S-CLI-NY3D7P). 순서: 바닥 -> 외벽 안쪽 면·모서리 -> 바깥 자연물 -> 잡초.
                if (BuildCombatFloorArt(artCatalog, artRoot))
                {
                    // 84칸을 빠짐없이 덮으므로 Greybox 바닥이 보일 자리가 없다. 같은 높이라 z-fighting 도 난다.
                    DisableGreyboxRenderer(battlefieldFloor);
                }

                if (BuildCombatArenaWallArt(artCatalog, artRoot))
                {
                    // 아트 벽이 Greybox 벽과 같은 자리에 겹치므로 회색 벽면은 숨긴다.
                    // Collider 는 그대로 살아 있고 CombatantMotor 도 이름으로만 벽을 찾는다.
                    DisableGreyboxRenderer(northWall);
                    DisableGreyboxRenderer(southWall);
                    DisableGreyboxRenderer(eastWall);
                    DisableGreyboxRenderer(westWall);
                }

                BuildCombatOuterNature(artCatalog, artRoot);
                BuildCombatGroundCover(artCatalog, artRoot);
            }
        }
#endif


        /// <summary>
        /// 전장 바닥 84칸을 빠짐없이 석조 타일로 덮는다. 가장자리 몇 장만 낡은 석판으로 섞는다.
        /// </summary>
        private static bool BuildCombatFloorArt(EnvironmentArtCatalog artCatalog, Transform artRoot)
        {
            var created = false;
            for (int column = 0; column < CombatFloorTileColumns; column++)
            {
                for (int row = 0; row < CombatFloorTileRows; row++)
                {
                    float x = CombatFloorFirstTileX + (column * CombatFloorTileSize);
                    float z = CombatFloorFirstTileZ + (row * CombatFloorTileSize);
                    EnvironmentVisualSlot slot = ResolveCombatFloorSlot(column, row, x, z);

                    // 정사각 타일이라 90도 단위 회전은 격자를 어긋내지 않고 반복 무늬만 흐린다.
                    float yaw = 90f * (((column * 3) + row) % 4);
                    created |= CreateArtOnlyVisual(
                        artCatalog,
                        slot,
                        artRoot,
                        $"Floor_{column:D2}_{row:D2}",
                        new Vector3(x, CombatFloorTileY, z),
                        Vector3.one,
                        Quaternion.Euler(0f, yaw, 0f));
                }
            }

            return created;
        }

        private static EnvironmentVisualSlot ResolveCombatFloorSlot(int column, int row, float x, float z)
        {
            // 주 교전 공간은 예외 없이 같은 석조라 하나의 연속된 바닥으로 읽힌다.
            var isMainCombatSpace = Mathf.Abs(x) <= CombatStoneFloorHalfWidth
                && Mathf.Abs(z) <= CombatStoneFloorHalfDepth;
            if (isMainCombatSpace)
            {
                return EnvironmentVisualSlot.CombatFloorVisual;
            }

            return ContainsCell(CombatFloorAccentCells, column, row)
                ? EnvironmentVisualSlot.CombatFloorAccentVisual
                : EnvironmentVisualSlot.CombatFloorVisual;
        }

        /// <summary>
        /// 외벽 바깥을 잔디 링과 성벽 기초 전이 구간으로 덮는다. Collider 는 만들지 않는다.
        /// </summary>
        private static void BuildCombatOuterGround(EnvironmentArtCatalog artCatalog, Transform parent)
        {
            Material grassMaterial = artCatalog != null ? artCatalog.CombatOuterGrassMaterial : null;
            Material vergeMaterial = artCatalog != null ? artCatalog.CombatOuterVergeMaterial : null;

            for (int i = 0; i < CombatOuterGrassStrips.Length; i++)
            {
                (string stripName, Vector3 position, Vector3 size) = CombatOuterGrassStrips[i];
                CreatePrimitive(
                    parent, stripName, PrimitiveType.Cube, position, size,
                    CombatOuterGrassColor, hasCollision: false, sharedMaterial: grassMaterial);
            }

            for (int i = 0; i < CombatOuterVergeStrips.Length; i++)
            {
                (string stripName, Vector3 position, Vector3 size) = CombatOuterVergeStrips[i];
                CreatePrimitive(
                    parent, stripName, PrimitiveType.Cube, position, size,
                    CombatOuterVergeColor, hasCollision: false, sharedMaterial: vergeMaterial);
            }
        }

        private static bool ContainsCell(Vector2Int[] cells, int column, int row)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].x == column && cells[i].y == row)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 외벽 안쪽 면을 석조 벽 세그먼트로 덮고 바깥에 기둥을 세운다. Collider 는 만들지 않는다.
        /// </summary>
        private static bool BuildCombatArenaWallArt(EnvironmentArtCatalog artCatalog, Transform artRoot)
        {
            var created = false;
            for (int i = 0; i < CombatWallSegmentXs.Length; i++)
            {
                float x = CombatWallSegmentXs[i];
                created |= CreateCombatArenaWallSegment(
                    artCatalog, artRoot, $"ArenaWall_North_{i:D2}", i, new Vector3(x, CombatWallSegmentCenterY, 14f), 180f);
                created |= CreateCombatArenaWallSegment(
                    artCatalog, artRoot, $"ArenaWall_South_{i:D2}", i + 1, new Vector3(x, CombatWallSegmentCenterY, -14f), 0f);
            }

            for (int i = 0; i < CombatWallSegmentZs.Length; i++)
            {
                float z = CombatWallSegmentZs[i];
                created |= CreateCombatArenaWallSegment(
                    artCatalog, artRoot, $"ArenaWall_East_{i:D2}", i, new Vector3(24f, CombatWallSegmentCenterY, z), 270f);
                created |= CreateCombatArenaWallSegment(
                    artCatalog, artRoot, $"ArenaWall_West_{i:D2}", i + 2, new Vector3(-24f, CombatWallSegmentCenterY, z), 90f);
            }

            for (int i = 0; i < CombatCornerPillars.Length; i++)
            {
                (string pillarName, Vector3 position, float scale, float yaw) = CombatCornerPillars[i];
                CreateArtOnlyVisual(
                    artCatalog,
                    EnvironmentVisualSlot.CombatArenaPillarVisual,
                    artRoot,
                    pillarName,
                    position,
                    new Vector3(scale, scale, scale),
                    Quaternion.Euler(0f, yaw, 0f));
            }

            for (int i = 0; i < CombatCornerStumps.Length; i++)
            {
                (string stumpName, Vector3 position, float yaw) = CombatCornerStumps[i];
                CreateArtOnlyVisual(
                    artCatalog,
                    EnvironmentVisualSlot.CombatColumnVisual,
                    artRoot,
                    stumpName,
                    position,
                    CombatCornerStumpScale,
                    Quaternion.Euler(0f, yaw, 0f));
            }

            return created;
        }

        /// <summary>
        /// 같은 벽이 38번 반복되지 않도록 세 칸에 한 번은 금 간 벽을 섞는다.
        /// </summary>
        private static bool CreateCombatArenaWallSegment(
            EnvironmentArtCatalog artCatalog,
            Transform artRoot,
            string objectName,
            int variantSeed,
            Vector3 localPosition,
            float yaw)
        {
            // 북쪽 벽 한 칸만 봉인된 철문 랜드마크로 바꾼다. 벽 세그먼트를 교체하는 방식이라
            // 새 Collider 도, 통과 가능한 문도 만들지 않는다 (S-GD-KXZQA3).
            var isNorthLandmark = objectName.StartsWith("ArenaWall_North_", System.StringComparison.Ordinal)
                && variantSeed == CombatNorthLandmarkSegmentIndex;
            EnvironmentVisualSlot slot = isNorthLandmark
                ? EnvironmentVisualSlot.CombatArenaGateVisual
                : System.Array.IndexOf(CombatCrackedWallSeeds, variantSeed) >= 0
                    ? EnvironmentVisualSlot.CombatArenaWallVisual
                    : EnvironmentVisualSlot.CombatCoverVisual;
            return CreateArtOnlyVisual(
                artCatalog,
                slot,
                artRoot,
                objectName,
                localPosition,
                CombatWallSegmentScale,
                Quaternion.Euler(0f, yaw, 0f));
        }

        private static void BuildCombatOuterNature(EnvironmentArtCatalog artCatalog, Transform artRoot)
        {
            for (int i = 0; i < CombatOuterNatureClusters.Length; i++)
            {
                (string clusterName, EnvironmentVisualSlot slot, Vector3 position, float scale, float yaw) =
                    CombatOuterNatureClusters[i];
                CreateArtOnlyVisual(
                    artCatalog,
                    slot,
                    artRoot,
                    clusterName,
                    position,
                    new Vector3(scale, scale, scale),
                    Quaternion.Euler(0f, yaw, 0f));
            }
        }

        private static void BuildCombatGroundCover(EnvironmentArtCatalog artCatalog, Transform artRoot)
        {
            for (int i = 0; i < CombatGroundCoverClusters.Length; i++)
            {
                (string clusterName, Vector3 position, float scale, float yaw) = CombatGroundCoverClusters[i];
                CreateArtOnlyVisual(
                    artCatalog,
                    EnvironmentVisualSlot.CombatGroundCoverVisual,
                    artRoot,
                    clusterName,
                    position,
                    new Vector3(scale, scale, scale),
                    Quaternion.Euler(0f, yaw, 0f));
            }
        }

        /// <summary>
        /// Full Cover 주 벽의 양 끝에 표시용 기둥을 세워 Connector 의 매끈한 벽과 실루엣을 구분한다.
        /// </summary>
        /// <remarks>
        /// 기둥 중심은 <c>SideOffset</c>(벽 두께의 절반)만큼 교전 공간 쪽 벽 측면에 얹혀 <b>절반이 벽 밖으로
        /// 나온다.</b> 벽 두께 안에 두면 둥근 상단만 솟아 굴뚝처럼 보였기 때문이다 (S-GD-KXZQA3).
        /// 나온 부피는 기둥마다 <c>Segment_EndPillarCollider_XX</c> 전용 BoxCollider 를 해당 Full Cover 그룹
        /// 아래에 만들어 막는다 — 벽 Collider 를 늘리지 않으므로 기둥과 벽 사이에 투명벽이 생기지 않는다.
        /// <paramref name="wallLength"/> 에서 안쪽으로 물리는 것은 그대로라, 기둥은 그룹 AABB 안에 남는다.
        /// </remarks>
        private static void AttachFullCoverEndPillars(
            EnvironmentArtCatalog artCatalog,
            Transform artRoot,
            Transform combatRoot)
        {
            if (artCatalog == null || artRoot == null || combatRoot == null)
            {
                return;
            }

            for (int w = 0; w < CombatFullCoverEndPillarWalls.Length; w++)
            {
                (string coverName, Vector3 wallCenter, float wallLength, float longAxisYaw, Vector3 sideOffset) =
                    CombatFullCoverEndPillarWalls[w];
                Transform coverGroup = combatRoot.Find(coverName);
                var scale = new Vector3(FullCoverEndPillarScale, FullCoverEndPillarScale, FullCoverEndPillarScale);
                for (int i = 0; i < 2; i++)
                {
                    Vector3 position = ResolveFullCoverEndPillarPosition(
                        wallCenter, wallLength, longAxisYaw, sideOffset, i);
                    CreateArtOnlyVisual(
                        artCatalog,
                        EnvironmentVisualSlot.CombatArenaPillarVisual,
                        artRoot,
                        $"{coverName}_EndPillar_{i:D2}",
                        position,
                        scale,
                        Quaternion.Euler(0f, longAxisYaw + (i * FullCoverEndPillarYawStep), 0f));

                    // 우선안 B — 벽 밖으로 나온 세로 몸체를 전용 Collider 가 막는다. 벽 Collider 를
                    // 사각형으로 늘리지 않으므로 기둥과 벽 사이에 투명벽이 생기지 않는다.
                    // 높이는 벽과 같은 3.2m 로 맞춘다. 그 위 0.6m 는 캐릭터(2m)가 닿지 않는다.
                    if (coverGroup == null)
                    {
                        continue;
                    }

                    var pillarProxy = new GameObject($"Segment_EndPillarCollider_{i:D2}");
                    pillarProxy.transform.SetParent(coverGroup, worldPositionStays: false);
                    // position 은 전장 루트 기준이므로 그룹 로컬로 변환한다.
                    pillarProxy.transform.position = combatRoot.TransformPoint(position);
                    pillarProxy.transform.rotation = combatRoot.rotation;
                    BoxCollider proxyCollider = pillarProxy.AddComponent<BoxCollider>();
                    proxyCollider.isTrigger = false;
                    proxyCollider.center = new Vector3(0f, FullCoverCenterY, 0f);
                    proxyCollider.size = new Vector3(
                        FullCoverEndPillarRadius * 2f, FullCoverHeight, FullCoverEndPillarRadius * 2f);
                }
            }
        }

        /// <summary>
        /// 기둥 위치는 테스트가 Collider 포함 여부를 재계산할 수 있도록 같은 함수를 쓴다.
        /// </summary>
        private static Vector3 ResolveFullCoverEndPillarPosition(
            Vector3 wallCenter,
            float wallLength,
            float longAxisYaw,
            Vector3 sideOffset,
            int endIndex)
        {
            float offset = (wallLength * 0.5f) - FullCoverEndPillarInset;
            Vector3 along = Quaternion.Euler(0f, longAxisYaw, 0f) * Vector3.right;
            return wallCenter + sideOffset + (along * (endIndex == 0 ? -offset : offset));
        }

        /// <summary>
        /// 라운드마다 서로 다른 <c>ChestAnchor</c> 두 곳에 상자 Visual 을 다시 세운다 (S-GD-VDXTYM).
        /// </summary>
        /// <remarks>
        /// 선택은 <b>복제된 라운드 번호만의 순수 함수</b>다. 각 피어가 <c>UnityEngine.Random</c> 을
        /// 따로 굴리면 Host 와 Client 가 다른 위치를 보게 되므로, 새 네트워크 계약을 만들지 않고
        /// 이미 모두가 같은 값을 보는 <see cref="OnlineCoreLoopSnapshot.CurrentRound"/> 에서 유도한다.
        /// 상자 선택 위치는 복제 라운드가 정하고, 상자 시각물은 충돌을 갖지 않는다.
        /// 상호작용·상태는 공용 Host 권위 경로가, 이동 차단은 별도 프록시가 담당한다.
        /// </remarks>
        private void RefreshCombatChestVisuals(Transform combatRoot, in OnlineCoreLoopSnapshot snapshot)
        {
            combatChestVisuals.Clear();
            combatChestInteractionBounds.Clear();
            if (combatRoot == null)
            {
                return;
            }

            Transform chestOwner = combatRuntimeRoot != null ? combatRuntimeRoot : combatRoot;
            Transform chestRoot = chestOwner.Find(CombatChestRootName);
            if (chestRoot == null)
            {
                chestRoot = CreateChildRoot(chestOwner, CombatChestRootName);
            }

            // 라운드가 바뀌면 이전 상자를 먼저 정리한다.
            for (int i = chestRoot.childCount - 1; i >= 0; i--)
            {
                GameObject previousChest = chestRoot.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    Destroy(previousChest);
                }
                else
                {
                    DestroyImmediate(previousChest);
                }
            }

            if (snapshot.Phase != OnlineCoreLoopPhase.Combat)
            {
                return;
            }

            EnvironmentArtCatalog artCatalog = FindFirstObjectByType<EnvironmentArtCatalog>(
                FindObjectsInactive.Include);
            if (artCatalog == null)
            {
                return;
            }

            Transform anchorRoot = combatRoot.Find(AnchorRootName);
            if (anchorRoot == null)
            {
                return;
            }

            var picks = new int[CombatChestsPerRound];
            ResolveRoundChestAnchorIndices(snapshot.CurrentRound, ChestAnchorLocalPositions.Length, picks);
            for (int i = 0; i < picks.Length; i++)
            {
                string anchorName = $"{ChestAnchorPrefix}{picks[i] + 1:00}";
                Transform anchor = anchorRoot.Find(anchorName);
                if (anchor == null)
                {
                    Debug.LogError($"[OnlineCoreLoopWorldPresenter] 상자 앵커를 찾지 못했습니다: {anchorName}");
                    continue;
                }

                // TryCreateVisual 이 래퍼가 들고 온 Collider 를 전부 지우므로, 이동 충돌은
                // 여기서 프로젝트 코드가 별도 프록시로 직접 붙인다. 임포트된 Collider 를 되살리지 않는다.
                if (!artCatalog.TryCreateVisual(
                        EnvironmentVisualSlot.CombatChestVisual,
                        chestRoot,
                        $"Chest_R{snapshot.CurrentRound:00}_{anchorName}",
                        anchor.localPosition,
                        Vector3.one,
                        Quaternion.Euler(0f, picks[i] * 45f, 0f),
                        out GameObject chest))
                {
                    continue;
                }

                CombatChestTraversalBlocker blocker = CombatChestTraversalBlocker.CreateDedicated(
                    chest.transform,
                    "TraversalBlocker",
                    CombatChestColliderCenter,
                    CombatChestColliderSize);
                if (blocker == null || !blocker.TryGetInteractionBounds(out CombatChestInteractionBounds interactionBounds))
                {
                    continue;
                }

                combatChestVisuals.Add(chest);
                CombatChestPresentation.Ensure(chest);
                combatChestInteractionBounds.Add(interactionBounds);
            }
        }

        private void UpdateProductChestAuthority(in OnlineCoreLoopSnapshot snapshot)
        {
            if (snapshot.Phase != OnlineCoreLoopPhase.Combat)
            {
                ClearProductChestAuthority();
                return;
            }

            if (!NetworkManager.HasInstance || NetworkManager.Instance == null || !NetworkManager.Instance.IsHost)
            {
                if (productChestAuthority != null || productChestSinks.Count > 0)
                {
                    ClearProductChestAuthority();
                }
                return;
            }

            if (combatChestVisuals.Count != CombatChestsPerRound
                || combatChestInteractionBounds.Count != CombatChestsPerRound)
            {
                ClearProductChestAuthority();
                return;
            }

            NetworkCombatantMotion[] motions = FindObjectsByType<NetworkCombatantMotion>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            NetworkCombatantMotion selectedPublisher = SelectChestPublisher(motions);
            if (selectedPublisher == null || selectedPublisher.Runner == null || selectedPublisher.Runner.TickRate <= 0)
            {
                ClearProductChestAuthority();
                return;
            }

            if (productChestAuthority == null || productChestRound != snapshot.CurrentRound)
            {
                ClearProductChestAuthority();
                var config = CreateProductChestConfig(selectedPublisher.Runner.TickRate);
                productChestAuthority = new CombatChestAuthorityRuntime();
                if (!productChestAuthority.ConfigureWithInteractionBounds(
                        snapshot.CurrentRound,
                        config,
                        combatChestInteractionBounds,
                        NetworkChestPlayerIdMapper.Instance,
                        CreateProductChestRewardAdapter(snapshot)))
                {
                    productChestAuthority = null;
                    return;
                }
                productChestRound = snapshot.CurrentRound;
                productChestAuthority.RespawnPlacement = id => CombatChestRespawnPlacement.TryMove(
                    id, combatChestVisuals, FindFirstObjectByType<CombatZoneRuntime>());
            }

            bool publisherChanged = productChestPublisher != selectedPublisher;
            productChestPublisher = selectedPublisher;
            var activeMotions = new HashSet<NetworkCombatantMotion>();
            for (int i = 0; i < motions.Length; i++)
            {
                NetworkCombatantMotion motion = motions[i];
                if (!IsLiveProductCombatant(motion))
                {
                    continue;
                }

                activeMotions.Add(motion);
                if (!productChestSinks.TryGetValue(motion, out ProductChestBinding binding))
                {
                    // 봇은 PlayerId 가 0 이라 CreateNetworkCombatantId 로 만들면 7기가 같은 id 를
                    // 받는다. 전투원이 스스로 쓰는 식별자를 그대로 쓴다 (S15P21A606-188).
                    CombatantId combatantId = new CombatantId(motion.TrainingActorId);
                    ICombatInteractionCommandSink sink = productChestAuthority.CreateInteractionSink(combatantId, motion.transform);
                    productChestSinks.Add(motion, new ProductChestBinding(combatantId, sink));
                    motion.SetInteractionCommandSink(sink);
                    motion.ConfigureChestAuthority(productChestAuthority, motion == productChestPublisher);
                }
                else if (publisherChanged)
                {
                    motion.ConfigureChestAuthority(productChestAuthority, motion == productChestPublisher);
                }
            }

            var staleMotions = new List<NetworkCombatantMotion>();
            foreach (KeyValuePair<NetworkCombatantMotion, ProductChestBinding> pair in productChestSinks)
            {
                if (!activeMotions.Contains(pair.Key))
                {
                    // Unity가 이미 파괴한 motion에서도 stable CombatantId는 binding에 남아 있다.
                    // Presenter의 다음 Update보다 먼저 Host AfterTick이 와도 runtime이 등록 actor를
                    // 검사하지만, 이 release는 sink를 즉시 무효화하고 Opening을 취소한다.
                    productChestAuthority.ReleaseInteractionSink(pair.Value.CombatantId);
                    if (pair.Key != null)
                    {
                        pair.Key.ClearInteractionCommandSink(pair.Value.Sink);
                        pair.Key.ClearChestAuthority(productChestAuthority);
                    }
                    staleMotions.Add(pair.Key);
                }
            }
            for (int i = 0; i < staleMotions.Count; i++)
            {
                productChestSinks.Remove(staleMotions[i]);
            }
        }

        private static NetworkCombatantMotion SelectChestPublisher(IReadOnlyList<NetworkCombatantMotion> motions)
        {
            NetworkCombatantMotion selected = null;
            for (int i = 0; i < motions.Count; i++)
            {
                NetworkCombatantMotion candidate = motions[i];
                if (!IsLiveProductCombatant(candidate) || !candidate.HasStateAuthority)
                {
                    continue;
                }

                // 참가자 키로 비교한다. 봇은 30,000 대라 사람(1..8)보다 뒤로 밀려
                // 대표로 뽑히지 않는다 — 봇의 PlayerId 0 은 최소값이라 그대로 두면 먼저 뽑힌다.
                if (selected == null || candidate.MatchParticipantId < selected.MatchParticipantId)
                {
                    selected = candidate;
                }
            }

            return selected;
        }

        private void ClearProductChestAuthority()
        {
            ResetReplicatedChestApplication();
            if (productChestAuthority == null && productChestSinks.Count == 0)
            {
                return;
            }

            // ⚠️ 두 번 돈다. 한 번에 하면 취소가 사라진다.
            //
            // 취소를 듣는 구독자는 **대표 전투원 한 명뿐**이고(publishesChestState),
            // ClearChestAuthority 가 그 구독을 뗀다. 한 루프에서 "취소 → 구독 해제" 를 같이 하면
            // 대표 항목이 먼저 순회될 때 그 뒤 사람들의 RoundEnded 취소가 **구독자 0 명에게 발화**돼
            // 통째로 버려진다. 사전 순서가 FindObjectsByType 순서라 간헐적으로 나고,
            // 대표는 대개 Host 라 "나는 보이는데 남은 안 보인다" 로 드러난다.

            // 1패스 — 구독이 살아 있는 동안 취소를 전부 낸다.
            // 라운드·매치가 끝나 상자 세계가 사라지는 자리다. 기본 사유(Moved)를 쓰면
            // 개봉 중이던 사람에게 "움직여서 취소됨" 이라고 알리게 된다 (S-GD-DFG3VR).
            foreach (KeyValuePair<NetworkCombatantMotion, ProductChestBinding> pair in productChestSinks)
            {
                productChestAuthority?.ReleaseInteractionSink(
                    pair.Value.CombatantId, ChestCancelReason.RoundEnded);
            }

            // 2패스 — 그 다음에 입력 창구와 구독을 끊는다.
            foreach (KeyValuePair<NetworkCombatantMotion, ProductChestBinding> pair in productChestSinks)
            {
                if (pair.Key == null)
                {
                    continue;
                }

                pair.Key.ClearInteractionCommandSink(pair.Value.Sink);
                pair.Key.ClearChestAuthority(productChestAuthority);
            }
            productChestSinks.Clear();
            productChestAuthority?.Clear();
            productChestAuthority = null;
            productChestPublisher = null;
            productChestRound = -1;
        }

        /// <summary>
        /// 내 상자 거부·취소를 계약 이벤트로 올린다 (O-028, 규약 3).
        /// </summary>
        /// <remarks>
        /// <b>왜 상자 상태 폴링과 따로 두는가.</b> 저쪽은 대표 한 명이 쏘는 방 전체의 상자를 읽지만
        /// 여기는 <b>내 전투원이 들고 있는 내 것만</b> 읽는다. 대표가 아직 안 뽑혔거나 상자 상태가
        /// 무효인 순간에도 거부는 이미 일어났을 수 있어, 저쪽의 이른 <c>return</c> 에 묶이면 삼켜진다.
        ///
        /// <para>
        /// 판정은 Host 에서만 일어난다. Host 자신이면 자기 필드를 바로 읽고, 클라이언트면 복제된
        /// 값을 읽는다 — 어느 쪽이든 같은 코드다.
        /// </para>
        /// </remarks>
        private void ApplyReplicatedChestFeedback(in OnlineCoreLoopSnapshot snapshot)
        {
            // ⚠️ 단계로 거르지 않는다. 라운드 종료 취소는 **전투가 끝나는 그 순간** 나오므로,
            //    Combat 단계만 통과시키면 그 알림이 통째로 삼켜진다 (S-GD-DFG3VR).
            //    같은 프레임에서 바로 위 UpdateProductChestAuthority 가 취소를 내고 여기가 받는다
            //    (호출 순서는 ApplyPhase 안의 세 줄 — 줄 번호는 적지 않는다, 어긋난다).
            if (!NetworkManager.HasInstance)
            {
                return;
            }

            int localPlayerId = ResolveLocalPlayerId();
            if (localPlayerId < 0
                || !NetworkManager.Instance.TryGetCombatant(localPlayerId, out NetworkCombatantMotion mine)
                || mine == null)
            {
                return;
            }

            // 전투원이 바뀌었으면 그 시점의 복제값에 맞추고 이 프레임은 넘긴다.
            // 새 전투원의 복제 필드는 0 이므로 사실상 0 부터 다시 세는 것이고,
            // 이미 값이 올라가 있는 경우에도 지난 사건을 재생하지 않는다.
            if (!ReferenceEquals(appliedChestFeedbackSource, mine))
            {
                appliedChestFeedbackSource = mine;
                appliedChestRejectSequence = mine.ReplicatedChestRejectSequence;
                appliedChestCancelSequence = mine.ReplicatedChestCancelSequence;
                return;
            }

            if (mine.ReplicatedChestRejectSequence > appliedChestRejectSequence)
            {
                appliedChestRejectSequence = mine.ReplicatedChestRejectSequence;
                EventBus.Publish(new ChestActionRejected(
                    localPlayerId,
                    isMine: true,
                    mine.ReplicatedChestRejectChestId,
                    (ChestRejectReason)mine.ReplicatedChestRejectReason));
            }

            if (mine.ReplicatedChestCancelSequence > appliedChestCancelSequence)
            {
                appliedChestCancelSequence = mine.ReplicatedChestCancelSequence;
                EventBus.Publish(new ChestOpenCancelled(
                    mine.ReplicatedChestCancelChestId,
                    localPlayerId,
                    (ChestCancelReason)mine.ReplicatedChestCancelReason));
            }
        }

        private void ApplyReplicatedChestState(in OnlineCoreLoopSnapshot snapshot)
        {
            if (snapshot.Phase != OnlineCoreLoopPhase.Combat || combatChestVisuals.Count != CombatChestsPerRound)
            {
                return;
            }

            NetworkCombatantMotion source = SelectReplicatedChestPublisher();
            if (source == null || source.ReplicatedChestRound != snapshot.CurrentRound)
            {
                return;
            }

            if (appliedChestMatchId != snapshot.MatchId || appliedChestStateRound != source.ReplicatedChestRound)
            {
                appliedChestMatchId = snapshot.MatchId;
                appliedChestStateRound = source.ReplicatedChestRound;
                appliedChestStateVersion = -1;
                Array.Clear(appliedChestClaimSequences, 0, appliedChestClaimSequences.Length);
            }

            if (!source.TryGetReplicatedChestState(0, out ChestAuthorityState first)
                || !source.TryGetReplicatedChestState(1, out ChestAuthorityState second))
            {
                return;
            }

            // Elapsed ticks and a late-spawned actor can change without a domain state transition.
            CombatChestRespawnPlacement.ApplyCenter(combatChestVisuals[0], source.ReplicatedChest0Center);
            CombatChestRespawnPlacement.ApplyCenter(combatChestVisuals[1], source.ReplicatedChest1Center);
            ApplyChestPresentation(first, source.ReplicatedChestTicksPerSecond);
            ApplyChestPresentation(second, source.ReplicatedChestTicksPerSecond);
            if (source.ReplicatedChestStateVersion == appliedChestStateVersion)
            {
                return;
            }

            int localPlayerId = ResolveLocalPlayerId();
            ChestAuthorityState[] states = { first, second };
            var snapshots = new ChestSnapshot[states.Length];
            for (int i = 0; i < states.Length; i++)
            {
                ChestAuthorityState state = states[i];
                GameObject visual = combatChestVisuals[i];
                Vector3 position = visual != null ? visual.transform.position : Vector3.zero;
                float progress = state.State == ChestState.Opening && state.OpenDurationTicks > 0
                    ? Mathf.Clamp01((float)state.ElapsedTicks / state.OpenDurationTicks)
                    : 0f;
                snapshots[i] = new ChestSnapshot(
                    state.ChestId,
                    state.State,
                    state.OpenerId,
                    state.OpenerId == localPlayerId,
                    progress,
                    state.ElapsedTicks,
                    state.OpenDurationTicks,
                    new WorldPosition(position.x, position.y, position.z));
            }

            EventBus.Publish(new ChestsChanged(snapshots, source.ReplicatedChestTicksPerSecond));
            // 각 상자의 claim sequence/payload를 따로 소비한다. 두 상자가 한 snapshot 사이에 수령돼도 누락되지 않는다.
            for (int i = 0; i < CombatChestsPerRound; i++)
            {
                if (!source.TryGetReplicatedChestClaim(i, out int sequence, out int claimerId)
                    || sequence <= appliedChestClaimSequences[i]
                    || claimerId < 0)
                {
                    continue;
                }
                EventBus.Publish(new ChestRewardClaimed(i, claimerId));
                appliedChestClaimSequences[i] = sequence;
            }

            appliedChestStateVersion = source.ReplicatedChestStateVersion;
        }

        private static NetworkCombatantMotion SelectReplicatedChestPublisher()
        {
            NetworkCombatantMotion[] motions = FindObjectsByType<NetworkCombatantMotion>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            NetworkCombatantMotion selected = null;
            for (int i = 0; i < motions.Length; i++)
            {
                NetworkCombatantMotion candidate = motions[i];
                if (!IsLiveProductCombatant(candidate)
                    || !candidate.ReplicatedChestStateValid || !candidate.ReplicatedChestStatePublisher)
                {
                    continue;
                }
                // 참가자 키로 비교한다. 봇은 30,000 대라 사람(1..8)보다 뒤로 밀려
                // 대표로 뽑히지 않는다 — 봇의 PlayerId 0 은 최소값이라 그대로 두면 먼저 뽑힌다.
                if (selected == null || candidate.MatchParticipantId < selected.MatchParticipantId)
                {
                    selected = candidate;
                }
            }
            return selected;
        }

        private static Transform ResolveChestOpeningActor(int playerId)
        {
            if (playerId < 0) return null;
            foreach (NetworkCombatantMotion motion in FindObjectsByType<NetworkCombatantMotion>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (IsLiveProductCombatant(motion) && motion.PlayerId == playerId) return motion.transform;
            return null;
        }

        private void ApplyChestPresentation(ChestAuthorityState state, float ticksPerSecond)
        {
            if (state.ChestId < 0 || state.ChestId >= combatChestVisuals.Count) return;
            GameObject chest = combatChestVisuals[state.ChestId];
            if (chest == null) return;
            CombatChestPresentation view = CombatChestPresentation.Ensure(chest);
            Transform actor = state.State == ChestState.Opening
                ? view.OpenerId == state.OpenerId && view.OpeningActor != null
                    ? view.OpeningActor : ResolveChestOpeningActor(state.OpenerId)
                : null;
            view.Apply(state, ticksPerSecond, actor, autoAdvance: true);
        }

        /// <summary>
        /// NetworkSceneManagerDefault가 runner 전용 additive scene에 NetworkObject를 둘 수 있으므로
        /// 카메라/맵 Scene 동일성은 제품 actor의 소속 판정으로 쓰지 않는다. 대신 live Runner와
        /// 유효 NetworkObject를 확인한다. 현재 InGame gate는 이 조건이 다른 dev session을 섞지 않게 한다.
        /// </summary>
        private static bool IsLiveProductCombatant(NetworkCombatantMotion motion) =>
            motion != null
            && motion.gameObject.activeInHierarchy
            && motion.Runner != null
            && motion.Runner.IsRunning
            && motion.Object != null
            && motion.Object.IsValid
            // ⚠️ <b>봇은 PlayerId 가 0 이다</b>(소유자 PlayerRef.None). `PlayerId > 0` 만 보면 대기방
            //    봇이 배치 후보·경계 적용에서 통째로 빠져, 맵 루트가 ±60m 이동할 때 옛 좌표에 남는다
            //    (S15P21A606-188). 참가자 키로 판정한다 — 사람은 PlayerId, 봇은 봇 식별자다.
            && motion.MatchParticipantId > 0;

        private static int ResolveLocalPlayerId()
        {
            NetworkCombatantMotion[] motions = FindObjectsByType<NetworkCombatantMotion>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (int i = 0; i < motions.Length; i++)
            {
                if (motions[i] != null && motions[i].AcceptsLocalInput)
                {
                    return motions[i].PlayerId;
                }
            }
            return TreasureChest.NoOpener;
        }

        private void ResetReplicatedChestApplication()
        {
            appliedChestMatchId = string.Empty;
            appliedChestStateRound = -1;
            appliedChestStateVersion = -1;
            Array.Clear(appliedChestClaimSequences, 0, appliedChestClaimSequences.Length);

            // ⚠️ 거부·취소 순번은 **여기서 지우지 않는다.** 복제 필드는 전투원이 살아 있는 한
            //    값을 유지하므로, 여기서 0 으로 되돌리면 **다음 라운드에 지난 취소가 다시 나온다.**
            //    그 두 값은 전투원 인스턴스가 바뀔 때만 다시 센다
            //    (ApplyReplicatedChestFeedback 의 ReferenceEquals 분기).
        }

        private sealed class NetworkChestPlayerIdMapper : IChestPlayerIdMapper
        {
            public static readonly NetworkChestPlayerIdMapper Instance = new();
            public bool TryGetPlayerId(CombatantId combatantId, out int playerId) =>
                NetworkCombatantMotion.TryGetPlayerId(combatantId, out playerId);
        }

        /// <summary>
        /// 제품 상자 시간 — 개봉 5초 (S-GD-GQNA2B) · 재생성 1초 · 재시도 1초. <c>ChestConfig.Default</c> 를 만들지 않고 여기서 조립한다.
        /// </summary>
        public static ChestConfig CreateProductChestConfig(int tickRate)
        {
            // 기획 엑셀 constants.CHEST_OPEN_TIME·CHEST_RESPAWN_DELAY·CHEST_RETRY_DELAY 가 정본이다 (S-CLI-QXRTWX).
            var rules = BettingRoyale.Data.Balance.BalanceCatalogs.Current.Chest;
            return new ChestConfig(
                Mathf.Max(1, Mathf.RoundToInt(tickRate * rules.OpenSeconds)),
                Mathf.Max(0, Mathf.RoundToInt(tickRate * rules.RespawnDelaySeconds)),
                Mathf.Max(0, Mathf.RoundToInt(tickRate * rules.RetryDelaySeconds)));
        }

        /// <summary>
        /// 이번 전투 라운드의 상자 보상 지급을 조립한다 — 300G + 아이템 1개, 못 받으면 과잉 보상 (S-GD-DDXPX9 · S-GD-BPB9S3).
        /// </summary>
        /// <remarks>
        /// 라운드마다 새로 만든다. 등급 확률이 매치의 마지막 라운드에서만 달라서(S-GD-DDXPX9 §3 · S-GD-RVVB4H §2)
        /// 그 판정을 이 자리의 스냅샷으로 한 번 정해 넘긴다.
        /// </remarks>
        private IChestRewardAdapter CreateProductChestRewardAdapter(in OnlineCoreLoopSnapshot snapshot)
        {
            productChestRewardRandom ??= new DeterministicRandom(Environment.TickCount);
            var payout = new ChestRewardPayout(
                BettingRoyale.Data.Balance.BalanceCatalogs.Current.ChestLoot,
                BettingRoyale.Data.Balance.BalanceCatalogs.Current.Chest.RewardGold,
                isFinalRound: snapshot.CurrentRound >= snapshot.Rules.TotalRounds,
                productChestRewardRandom,
                new NetworkChestRewardRecipients(snapshot.CurrentRound));
            return new ProductChestRewardAdapter(payout);
        }

        /// <summary>
        /// 제품 Host 의 상자 개봉 완료 → 지급. 판정부(<see cref="CombatChestAuthorityRuntime"/>)가 게이지가 다 찬 틱에
        /// 부른다 — 따로 누르는 수령 입력은 없다 (S-GD-RC63HQ).
        /// </summary>
        /// <remarks>
        /// 지급 결과는 따로 복제하지 않는다 — 골드는 경제 스냅샷(<c>ESTA</c>), 아이템은 전투원 인벤토리 복제,
        /// 회복·이동 속도는 Host 시뮬레이션 결과로 각 피어에 간다.
        /// </remarks>
        private sealed class ProductChestRewardAdapter : IChestRewardAdapter
        {
            private readonly ChestRewardPayout payout;

            public ProductChestRewardAdapter(ChestRewardPayout payout) => this.payout = payout;

            public void NotifyRewardClaimed(int playerId, int chestId)
            {
                ChestPayoutResult result = payout.Pay(playerId, chestId);
                if (!result.Paid)
                {
                    // 라운드 결과가 이미 반영된 뒤의 수령이면 정상이다 (S-GD-DFG3VR — 라운드 종료가 이긴다).
                    Debug.LogWarning($"[OnlineCoreLoopWorldPresenter] 상자 보상을 지급하지 않았습니다 — {result}");
                    return;
                }

                if (result.IsUnexpectedOverflow || !result.GoldGranted
                    || (result.Outcome.Delivery == ChestRewardDelivery.Overflow && !result.OverflowApplied))
                {
                    Debug.LogWarning($"[OnlineCoreLoopWorldPresenter] 상자 보상 일부가 들어가지 않았습니다 — {result}");
                    return;
                }

                if (result.Outcome.Delivery == ChestRewardDelivery.Blocked
                    && NetworkManager.HasInstance
                    && NetworkManager.Instance != null
                    && NetworkManager.Instance.TryGetCombatant(playerId, out NetworkCombatantMotion combatant)
                    && combatant != null)
                {
                    combatant.GetComponent<NetworkEquipmentPickupInteractor>()
                        ?.ReportAuthorityInteractionRejected(EquipmentInventoryFailure.ConsumableCapacityFull);
                }

                if (Debug.isDebugBuild)
                {
                    Debug.Log($"[OnlineCoreLoopWorldPresenter] 상자 보상 지급 — {result}");
                }
            }
        }

        /// <summary>
        /// 상자 보상을 넣을 곳 — Host 경제와 전투원의 권위 인벤토리.
        /// </summary>
        private sealed class NetworkChestRewardRecipients : IChestRewardRecipients
        {
            private readonly int round;

            public NetworkChestRewardRecipients(int round) => this.round = round;

            public bool TryResolveRecipient(int playerId, out BettingRoyale.Data.Equipment.IEquipmentInventoryDriver inventory)
            {
                inventory = null;
                OnlineEconomyCoordinator economy = OnlineEconomyCoordinator.Instance;
                if (economy == null
                    || !economy.CanGrantChestReward(playerId, round)
                    || !NetworkManager.HasInstance
                    || NetworkManager.Instance == null
                    || !NetworkManager.Instance.TryGetCombatant(playerId, out NetworkCombatantMotion combatant)
                    || combatant == null)
                {
                    return false;
                }

                // 권위 피어가 아니면 null 이다 — 표시용 인벤토리에 넣으면 다음 복제가 덮어쓴다.
                inventory = combatant.AuthorityInventory;
                return inventory != null;
            }

            public bool TryGrantGold(int playerId, int amount)
            {
                OnlineEconomyCoordinator economy = OnlineEconomyCoordinator.Instance;
                return economy != null && economy.TryGrantChestGold(playerId, round, amount);
            }
        }

        /// <summary>
        /// 라운드 번호에서 서로 다른 상자 앵커 인덱스를 뽑는다. 순수 함수라 모든 피어가 같은 값을 얻는다.
        /// </summary>
        private static void ResolveRoundChestAnchorIndices(int round, int anchorCount, int[] picks)
        {
            if (picks == null || picks.Length < CombatChestsPerRound)
            {
                return;
            }

            if (!CombatChestRoundSelection.TrySelectTwo(round, anchorCount, out int first, out int second))
            {
                for (int i = 0; i < CombatChestsPerRound; i++)
                {
                    picks[i] = -1;
                }
                return;
            }

            picks[0] = first;
            picks[1] = second;
        }

        /// <summary>
        /// 전투 단계에서만 해질녘 조명을 적용하고, 준비 단계로 돌아오면 원래 값을 복원한다.
        /// </summary>
        /// <remarks>
        /// 준비맵과 전투맵이 같은 Scene 조명을 공유하므로 Scene 값을 영구히 덮지 않는다.
        /// 첫 적용 직전에 원래 값을 한 번만 기억하고, 복원은 그 값으로만 되돌린다.
        /// Skybox·Bloom·Post Processing 은 건드리지 않는다.
        /// </remarks>
        private void ApplyCombatStageLighting(bool combatStage)
        {
            if (stageLight == null)
            {
                Light[] lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i].type == LightType.Directional)
                    {
                        stageLight = lights[i];
                        break;
                    }
                }
            }

            if (!hasCapturedStageLighting)
            {
                capturedAmbientIntensity = RenderSettings.ambientIntensity;
                capturedLightIntensity = stageLight != null ? stageLight.intensity : 0f;
                capturedLightColor = stageLight != null ? stageLight.color : Color.white;
                hasCapturedStageLighting = true;
            }

            if (combatStage == appliedCombatStageLighting)
            {
                return;
            }

            appliedCombatStageLighting = combatStage;
            RenderSettings.ambientIntensity = combatStage
                ? CombatSunsetAmbientIntensity
                : capturedAmbientIntensity;
            if (stageLight != null)
            {
                stageLight.intensity = combatStage ? CombatSunsetLightIntensity : capturedLightIntensity;
                stageLight.color = combatStage ? CombatSunsetLightColor : capturedLightColor;
            }
        }

        private static void DisableGreyboxRenderer(GameObject greyboxObject)
        {
            if (greyboxObject == null)
            {
                return;
            }

            Renderer greyboxRenderer = greyboxObject.GetComponent<Renderer>();
            if (greyboxRenderer != null)
            {
                greyboxRenderer.enabled = false;
            }
        }

        private static void AttachCombatArtVisual(
            EnvironmentArtCatalog artCatalog,
            EnvironmentVisualSlot slot,
            Transform artRoot,
            Transform combatRoot,
            Transform collisionObject)
        {
            if (artCatalog == null || artRoot == null || combatRoot == null || collisionObject == null)
            {
                return;
            }

            Vector3 localPosition = combatRoot.InverseTransformPoint(collisionObject.position);
            Quaternion localRotation = Quaternion.Inverse(combatRoot.rotation) * collisionObject.rotation;

            if (!artCatalog.TryCreateVisual(
                    slot,
                    artRoot,
                    $"{collisionObject.name}_ArtVisual",
                    localPosition,
                    collisionObject.localScale,
                    localRotation,
                    out GameObject _))
            {
                return;
            }

            Renderer greyboxRenderer = collisionObject.GetComponent<Renderer>();
            if (greyboxRenderer != null)
            {
                greyboxRenderer.enabled = false;
            }
        }

        private static bool CreateArtOnlyVisual(
            EnvironmentArtCatalog artCatalog,
            EnvironmentVisualSlot slot,
            Transform parent,
            string objectName,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation)
        {
            if (artCatalog == null || parent == null)
            {
                return false;
            }

            return artCatalog.TryCreateVisual(
                slot,
                parent,
                objectName,
                localPosition,
                localScale,
                localRotation,
                out GameObject _);
        }

        private static GameObject CreatePrimitive(
            Transform parent,
            string objectName,
            PrimitiveType primitiveType,
            Vector3 localPosition,
            Vector3 localScale,
            Color color,
            Quaternion? localRotation = null,
            bool hasCollision = true,
            Material sharedMaterial = null)
        {
            GameObject instanceObject = GameObject.CreatePrimitive(primitiveType);
            instanceObject.name = objectName;
            instanceObject.transform.SetParent(parent, worldPositionStays: false);
            instanceObject.transform.localPosition = localPosition;
            instanceObject.transform.localRotation = localRotation ?? Quaternion.identity;
            instanceObject.transform.localScale = localScale;

            Collider primitiveCollider = instanceObject.GetComponent<Collider>();
            if (!hasCollision && primitiveCollider != null)
            {
                primitiveCollider.enabled = false;
            }

            Renderer primitiveRenderer = instanceObject.GetComponent<Renderer>();
            if (primitiveRenderer != null && sharedMaterial != null)
            {
                primitiveRenderer.sharedMaterial = sharedMaterial;
            }
            else if (primitiveRenderer != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Universal Render Pipeline/Unlit")
                    ?? primitiveRenderer.sharedMaterial?.shader;
                if (shader != null)
                {
                    var runtimeMaterial = new Material(shader)
                    {
                        color = color,
                        name = $"Runtime_{objectName}_Material",
                    };
                    if (runtimeMaterial.HasProperty("_BaseColor"))
                    {
                        runtimeMaterial.SetColor("_BaseColor", color);
                    }
                    primitiveRenderer.sharedMaterial = runtimeMaterial;
                }
            }

            return instanceObject;
        }

        private static void UseVisibleMeshForStaticCollision(GameObject primitive)
        {
            Collider generatedCollider = primitive.GetComponent<Collider>();
            if (generatedCollider != null)
            {
                generatedCollider.enabled = false;
            }

            MeshFilter meshFilter = primitive.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogError(
                    $"[OnlineCoreLoopWorldPresenter] {primitive.name}: 정적 충돌에 사용할 메시가 비었습니다.");
                return;
            }

            MeshCollider meshCollider = primitive.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;
        }

        private static void CreateCardinalGhostBounds(Transform parent, float halfWidth, float halfDepth)
        {
            CreateInvisibleBoundary(parent, "NorthWall", new Vector3(0f, 1.5f, halfDepth), new Vector3(halfWidth * 2f, 3f, 0.25f));
            CreateInvisibleBoundary(parent, "SouthWall", new Vector3(0f, 1.5f, -halfDepth), new Vector3(halfWidth * 2f, 3f, 0.25f));
            CreateInvisibleBoundary(parent, "EastWall", new Vector3(halfWidth, 1.5f, 0f), new Vector3(0.25f, 3f, halfDepth * 2f));
            CreateInvisibleBoundary(parent, "WestWall", new Vector3(-halfWidth, 1.5f, 0f), new Vector3(0.25f, 3f, halfDepth * 2f));
        }

        private static void CreateInvisibleBoundary(
            Transform parent,
            string objectName,
            Vector3 localPosition,
            Vector3 size)
        {
            var boundaryObject = new GameObject(objectName);
            boundaryObject.transform.SetParent(parent, worldPositionStays: false);
            boundaryObject.transform.localPosition = localPosition;
            BoxCollider boundary = boundaryObject.AddComponent<BoxCollider>();
            boundary.size = size;
        }

        private static void CreateWorldLabel(Transform parent, string text, Vector3 localPosition)
        {
            var labelObject = new GameObject($"Label_{text.Replace(' ', '_')}");
            labelObject.transform.SetParent(parent, worldPositionStays: false);
            labelObject.transform.localPosition = localPosition;
            labelObject.transform.localRotation = Quaternion.identity;
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.text = text;
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            // 0.38 -> 0.266 (70%). 환경 에셋을 덜 가리도록 표시 크기만 줄인다 (S-CLI-WFVN75).
            // fontSize 는 48 그대로다 — 렌더 해상도라 줄이면 글자가 뭉개진다.
            label.characterSize = 0.266f;
            label.fontSize = 48;
            label.color = new Color(0.85f, 0.96f, 1f);
        }
    }
}

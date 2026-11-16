using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Settings
{
    /// <summary>
    /// 데이터 주도 설정 정의 표. UI/검증/기본값/마이그레이션이 모두 이 표만 참조한다.
    /// 수치와 기본값은 S-CLI-JZKPDF 작업 가정이며 플레이테스트 후 재검토 대상이다.
    /// </summary>
    public static class SettingsDefinitions
    {
        /// <summary>일반 슬라이더 최소 단위.</summary>
        private const float Step05 = 0.05f;

        private static readonly string[] EmptyChoices = new string[0];

        private static readonly string[] OnOffQuality = { "Low", "Medium", "High" };

        private static readonly SettingDefinition[] AllDefinitions = BuildAll();

        private static readonly Dictionary<string, SettingDefinition> ById = BuildIndex(AllDefinitions);

        private static readonly Dictionary<SettingsCategory, SettingDefinition[]> ByCategory =
            BuildCategoryIndex(AllDefinitions);

        /// <summary>별칭 ID -> 실제 저장 ID. 저장은 항상 원본 ID 한 곳에만 이뤄진다.</summary>
        private static readonly Dictionary<string, string> AliasToCanonical = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { SettingsIds.AccessibilityUiScale, SettingsIds.HudScale }
        };

        private static readonly Dictionary<SettingsCategory, string[]> BindingActionIds =
            new Dictionary<SettingsCategory, string[]>
            {
                {
                    SettingsCategory.KeyboardMouse, new[]
                    {
                        SettingsIds.ActionMoveUp,
                        SettingsIds.ActionMoveDown,
                        SettingsIds.ActionMoveLeft,
                        SettingsIds.ActionMoveRight,
                        SettingsIds.ActionAttack,
                        SettingsIds.ActionDash,
                        SettingsIds.ActionInteract,
                        SettingsIds.ActionActiveBlessing,
                        SettingsIds.ActionCancel,
                        SettingsIds.ActionSlot1,
                        SettingsIds.ActionSlot2,
                        SettingsIds.ActionSlot3,
                        SettingsIds.ActionSlot4,
                        SettingsIds.ActionMenu,
                        SettingsIds.ActionMinimap,
                        SettingsIds.ActionDetails,
                        SettingsIds.ActionConfirm
                    }
                },
                {
                    SettingsCategory.Controller, new[]
                    {
                        SettingsIds.ActionAttack,
                        SettingsIds.ActionDash,
                        SettingsIds.ActionInteract,
                        SettingsIds.ActionActiveBlessing,
                        SettingsIds.ActionPreviousSlot,
                        SettingsIds.ActionNextSlot,
                        SettingsIds.ActionMenu,
                        SettingsIds.ActionMinimap,
                        SettingsIds.ActionDetails,
                        SettingsIds.ActionBlessingFocus,
                        SettingsIds.ActionConfirm,
                        SettingsIds.ActionCancel
                    }
                }
            };

        private static readonly string[] NoActions = new string[0];

        public static IReadOnlyList<SettingDefinition> All
        {
            get { return AllDefinitions; }
        }

        public static SettingDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            return ById.TryGetValue(id, out var definition) ? definition : null;
        }

        public static IReadOnlyList<SettingDefinition> ForCategory(SettingsCategory category)
        {
            return ByCategory.TryGetValue(category, out var definitions)
                ? definitions
                : (IReadOnlyList<SettingDefinition>)Array.Empty<SettingDefinition>();
        }

        /// <summary>별칭이면 실제 저장 ID로 바꾼다. 별칭이 아니면 그대로 돌려준다.</summary>
        public static string CanonicalId(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return id;
            }

            return AliasToCanonical.TryGetValue(id, out var canonical) ? canonical : id;
        }

        public static bool IsAlias(string id)
        {
            return !string.IsNullOrEmpty(id) && AliasToCanonical.ContainsKey(id);
        }

        /// <summary>바인딩 정의 ID 규칙: "{categoryKey}.{actionId}".</summary>
        public static string BindingDefinitionId(SettingsCategory category, string actionId)
        {
            return SettingsSchema.CategoryKey(category) + "." + (actionId ?? string.Empty);
        }

        public static string ActionIdOf(string definitionId)
        {
            if (string.IsNullOrEmpty(definitionId))
            {
                return string.Empty;
            }

            var separator = definitionId.IndexOf('.');
            return separator >= 0 && separator + 1 < definitionId.Length
                ? definitionId.Substring(separator + 1)
                : definitionId;
        }

        /// <summary>해당 분류에서 재지정 가능한 액션 목록(충돌 검사 대상 집합).</summary>
        public static IReadOnlyList<string> BindingActions(SettingsCategory category)
        {
            return BindingActionIds.TryGetValue(category, out var actions) ? actions : NoActions;
        }

        public static string DefaultBinding(SettingsCategory category, string actionId)
        {
            var definition = Find(BindingDefinitionId(category, actionId));
            return definition != null && definition.Kind == SettingKind.Binding
                ? definition.DefaultValue
                : string.Empty;
        }

        private static Dictionary<string, SettingDefinition> BuildIndex(SettingDefinition[] definitions)
        {
            var index = new Dictionary<string, SettingDefinition>(definitions.Length, StringComparer.Ordinal);
            for (var i = 0; i < definitions.Length; i++)
            {
                index.Add(definitions[i].Id, definitions[i]);
            }

            return index;
        }

        private static Dictionary<SettingsCategory, SettingDefinition[]> BuildCategoryIndex(SettingDefinition[] definitions)
        {
            var buckets = new Dictionary<SettingsCategory, List<SettingDefinition>>();
            foreach (var category in SettingsSchema.Categories())
            {
                buckets.Add(category, new List<SettingDefinition>());
            }

            for (var i = 0; i < definitions.Length; i++)
            {
                buckets[definitions[i].Category].Add(definitions[i]);
            }

            var index = new Dictionary<SettingsCategory, SettingDefinition[]>();
            foreach (var pair in buckets)
            {
                index.Add(pair.Key, pair.Value.ToArray());
            }

            return index;
        }

        private static SettingDefinition Toggle(
            string id,
            SettingsCategory category,
            string label,
            string description,
            bool defaultValue)
        {
            return new SettingDefinition(
                id,
                label,
                description,
                category,
                SettingKind.Toggle,
                SettingsSchema.FormatBool(defaultValue));
        }

        private static SettingDefinition Slider(
            string id,
            SettingsCategory category,
            string label,
            string description,
            float defaultValue,
            float min,
            float max,
            float step)
        {
            return new SettingDefinition(
                id,
                label,
                description,
                category,
                SettingKind.Slider,
                SettingsSchema.FormatFloat(defaultValue),
                min,
                max,
                step);
        }

        private static SettingDefinition Choice(
            string id,
            SettingsCategory category,
            string label,
            string description,
            string defaultValue,
            string[] choices,
            string platform = null)
        {
            return new SettingDefinition(
                id,
                label,
                description,
                category,
                SettingKind.Choice,
                defaultValue,
                0f,
                0f,
                0f,
                choices,
                false,
                platform);
        }

        private static SettingDefinition Binding(
            SettingsCategory category,
            string actionId,
            string label,
            string description,
            string defaultPath)
        {
            return new SettingDefinition(
                BindingDefinitionId(category, actionId),
                label,
                description,
                category,
                SettingKind.Binding,
                defaultPath);
        }

        private static SettingDefinition[] BuildAll()
        {
            var list = new List<SettingDefinition>(160);

            // ---------------- 일반/게임플레이 ----------------
            list.Add(Toggle(SettingsIds.GameplayShowDamageNumber, SettingsCategory.Gameplay,
                "피해량 표시", "전투 중 피해량 숫자를 표시합니다.", true));
            list.Add(Toggle(SettingsIds.GameplayShowOwnName, SettingsCategory.Gameplay,
                "내 이름 표시", "내 캐릭터 위에 이름표를 표시합니다.", true));
            list.Add(Toggle(SettingsIds.GameplayShowOtherNames, SettingsCategory.Gameplay,
                "다른 플레이어 이름 표시", "다른 플레이어의 이름표를 표시합니다.", true));
            list.Add(Toggle(SettingsIds.GameplayShowHealthBars, SettingsCategory.Gameplay,
                "체력바 표시", "캐릭터 위 체력바를 표시합니다.", true));
            list.Add(Toggle(SettingsIds.GameplayShowAttackGuide, SettingsCategory.Gameplay,
                "공격 가이드 표시", "공격 방향과 범위 가이드를 표시합니다.", true));
            list.Add(Toggle(SettingsIds.GameplayShowThrowGuide, SettingsCategory.Gameplay,
                "투척 궤적 표시", "투척 아이템의 예상 궤적을 표시합니다.", true));
            list.Add(Toggle(SettingsIds.GameplayShowThrowRange, SettingsCategory.Gameplay,
                "투척 폭발 범위 표시", "투척 아이템의 폭발 예상 범위를 표시합니다.", true));
            list.Add(Toggle(SettingsIds.GameplayAutoSwitchEquipment, SettingsCategory.Gameplay,
                "자동 장비 전환", "빈손일 때 무기를 얻으면 자동으로 듭니다. 소모품을 얻어도 고른 칸은 그대로입니다.", true));
            list.Add(Slider(SettingsIds.GameplayCameraShake, SettingsCategory.Gameplay,
                "카메라 흔들림", "카메라 흔들림 강도입니다. 0이면 흔들리지 않습니다.", 1f, 0f, 1f, Step05));
            list.Add(Slider(SettingsIds.GameplayScreenEffects, SettingsCategory.Gameplay,
                "화면 효과 강도", "화면 전체를 덮는 연출 효과의 강도입니다.", 1f, 0f, 1f, Step05));
            list.Add(Slider(SettingsIds.GameplayHitEffects, SettingsCategory.Gameplay,
                "피격 효과 강도", "피격 시 화면 연출 강도입니다.", 1f, 0f, 1f, Step05));

            // ---------------- HUD/인터페이스 ----------------
            // 조작 안내가 HUD 탭 맨 위에 온다 — 소제목 '조작 안내' (SettingsPanelView.GroupTitle).
            list.Add(Toggle(SettingsIds.HudShowMenuControlsGuide, SettingsCategory.Hud,
                "메뉴 화면 조작 안내", "로그인·메인 메뉴·로비·대기방·설정 화면 오른쪽 아래에 지금 쓰는 장치의 조작 키를 표시합니다.", true));
            list.Add(Toggle(SettingsIds.HudShowInGameControlsGuide, SettingsCategory.Hud,
                "인게임 조작 안내", "준비·전투 HUD와 상점·강화·룰렛·축복 선택·현황판에 조작 키를 표시합니다.", true));
            list.Add(Slider(SettingsIds.HudScale, SettingsCategory.Hud,
                "HUD 전체 배율", "HUD 요소의 전체 크기 배율입니다. 월드 카메라에는 영향을 주지 않습니다.",
                1f, 0.7f, 1.3f, Step05));
            list.Add(Toggle(SettingsIds.HudShowStats, SettingsCategory.Hud,
                "능력치 표시", "능력치 위젯을 표시합니다.", true));
            list.Add(Toggle(SettingsIds.HudShowEquipment, SettingsCategory.Hud,
                "장비 표시", "장비 슬롯 위젯을 표시합니다.", true));
            list.Add(Toggle(SettingsIds.HudShowHp, SettingsCategory.Hud,
                "HP 표시", "HP 위젯을 표시합니다.", true));
            list.Add(Toggle(SettingsIds.HudShowMinimap, SettingsCategory.Hud,
                "미니맵 표시", "미니맵 위젯을 표시합니다.", true));
            list.Add(Toggle(SettingsIds.HudShowBlessings, SettingsCategory.Hud,
                "축복 표시", "축복 위젯을 표시합니다.", true));
            list.Add(Toggle(SettingsIds.HudShowStatusEffects, SettingsCategory.Hud,
                "상태 효과 표시", "상태 효과 위젯을 표시합니다.", true));
            list.Add(Toggle(SettingsIds.HudShowRound, SettingsCategory.Hud,
                "라운드 정보 표시", "라운드 정보 위젯을 표시합니다.", true));
            list.Add(Toggle(SettingsIds.HudShowCurrency, SettingsCategory.Hud,
                "재화 표시", "재화 위젯을 표시합니다.", true));
            list.Add(Toggle(SettingsIds.HudShowZone, SettingsCategory.Hud,
                "자기장 정보 표시", "자기장 정보 위젯을 표시합니다.", true));
            list.Add(Toggle(SettingsIds.HudShowWorldNames, SettingsCategory.Hud,
                "월드 이름표 표시", "월드 공간의 이름표 레이어를 표시합니다.", true));
            list.Add(Toggle(SettingsIds.HudShowPerformance, SettingsCategory.Hud,
                "성능 표시", "우상단에 초당 프레임과 네트워크 지연을 표시합니다.", true));
            list.Add(Slider(SettingsIds.HudMinimapScale, SettingsCategory.Hud,
                "미니맵 크기", "미니맵 위젯의 크기 배율입니다.", 1f, 0.7f, 1.5f, Step05));
            list.Add(Slider(SettingsIds.HudMinimapOpacity, SettingsCategory.Hud,
                "미니맵 투명도", "미니맵 배경의 불투명도입니다.", 1f, 0.2f, 1f, Step05));
            list.Add(Choice(SettingsIds.HudMinimapRotation, SettingsCategory.Hud,
                "미니맵 방향", "북쪽 고정 또는 캐릭터 방향 회전을 고릅니다.",
                "NorthUp", new[] { "NorthUp", "PlayerUp" }));
            list.Add(Toggle(SettingsIds.HudMinimapView, SettingsCategory.Hud,
                "미니맵 시야 표시", "미니맵에 시야 범위를 표시합니다.", true));
            list.Add(Toggle(SettingsIds.HudMinimapZone, SettingsCategory.Hud,
                "미니맵 자기장 표시", "미니맵에 자기장을 표시합니다.", true));
            list.Add(Toggle(SettingsIds.HudMinimapObjects, SettingsCategory.Hud,
                "미니맵 오브젝트 표시", "미니맵에 주요 오브젝트를 표시합니다.", true));

            // ---------------- 키보드/마우스 ----------------
            list.Add(Slider(SettingsIds.KeyboardMouseSensitivity, SettingsCategory.KeyboardMouse,
                "감도", "마우스 전체 감도 배율입니다.", 1f, 0.1f, 3f, Step05));
            list.Add(Slider(SettingsIds.KeyboardMouseXSensitivity, SettingsCategory.KeyboardMouse,
                "X축 감도", "좌우 방향 감도 배율입니다.", 1f, 0.1f, 3f, Step05));
            list.Add(Slider(SettingsIds.KeyboardMouseYSensitivity, SettingsCategory.KeyboardMouse,
                "Y축 감도", "상하 방향 감도 배율입니다.", 1f, 0.1f, 3f, Step05));
            list.Add(Slider(SettingsIds.KeyboardMouseAimMultiplier, SettingsCategory.KeyboardMouse,
                "조준 감도 배율", "조준 중에 적용되는 추가 감도 배율입니다.", 1f, 0.1f, 3f, Step05));
            list.Add(Toggle(SettingsIds.KeyboardMouseInvertX, SettingsCategory.KeyboardMouse,
                "X축 반전", "좌우 입력을 반전합니다.", false));
            list.Add(Toggle(SettingsIds.KeyboardMouseInvertY, SettingsCategory.KeyboardMouse,
                "Y축 반전", "상하 입력을 반전합니다.", false));

            list.Add(Binding(SettingsCategory.KeyboardMouse, SettingsIds.ActionMoveUp,
                "위로 이동", "위쪽 이동 키입니다.", "<Keyboard>/w"));
            list.Add(Binding(SettingsCategory.KeyboardMouse, SettingsIds.ActionMoveDown,
                "아래로 이동", "아래쪽 이동 키입니다.", "<Keyboard>/s"));
            list.Add(Binding(SettingsCategory.KeyboardMouse, SettingsIds.ActionMoveLeft,
                "왼쪽 이동", "왼쪽 이동 키입니다.", "<Keyboard>/a"));
            list.Add(Binding(SettingsCategory.KeyboardMouse, SettingsIds.ActionMoveRight,
                "오른쪽 이동", "오른쪽 이동 키입니다.", "<Keyboard>/d"));
            list.Add(Binding(SettingsCategory.KeyboardMouse, SettingsIds.ActionAttack,
                "공격", "공격 입력입니다.", "<Mouse>/leftButton"));
            list.Add(Binding(SettingsCategory.KeyboardMouse, SettingsIds.ActionDash,
                "대시", "대시 입력입니다.", "<Keyboard>/space"));
            list.Add(Binding(SettingsCategory.KeyboardMouse, SettingsIds.ActionInteract,
                "상호작용", "상호작용 입력입니다.", "<Keyboard>/e"));
            list.Add(Binding(SettingsCategory.KeyboardMouse, SettingsIds.ActionActiveBlessing,
                "액티브 축복", "직접 눌러 쓰는 축복을 켭니다. 한 번에 하나만 가질 수 있습니다.", "<Keyboard>/q"));
            list.Add(Binding(SettingsCategory.KeyboardMouse, SettingsIds.ActionCancel,
                "취소", "UI 취소 입력입니다. 설정 열기와 겹치지 않도록 기본값은 마우스 오른쪽 버튼입니다.",
                "<Mouse>/rightButton"));
            list.Add(Binding(SettingsCategory.KeyboardMouse, SettingsIds.ActionSlot1,
                "슬롯 1(무기)", "1번 슬롯을 사용합니다.", "<Keyboard>/1"));
            list.Add(Binding(SettingsCategory.KeyboardMouse, SettingsIds.ActionSlot2,
                "슬롯 2(소모품)", "2번 슬롯을 사용합니다.", "<Keyboard>/2"));
            list.Add(Binding(SettingsCategory.KeyboardMouse, SettingsIds.ActionSlot3,
                "슬롯 3(소모품)", "3번 슬롯을 사용합니다.", "<Keyboard>/3"));
            list.Add(Binding(SettingsCategory.KeyboardMouse, SettingsIds.ActionSlot4,
                "슬롯 4(소모품)", "4번 슬롯을 사용합니다.", "<Keyboard>/4"));
            list.Add(Binding(SettingsCategory.KeyboardMouse, SettingsIds.ActionMenu,
                "설정/메뉴 열기", "인게임 메뉴를 엽니다.", "<Keyboard>/escape"));
            list.Add(Binding(SettingsCategory.KeyboardMouse, SettingsIds.ActionMinimap,
                "전투 현황", "참가자 전투 현황을 엽니다.", "<Keyboard>/tab"));
            list.Add(Binding(SettingsCategory.KeyboardMouse, SettingsIds.ActionDetails,
                "상세 능력치", "상세 능력치 보기입니다.", "<Keyboard>/c"));

            list.Add(Binding(SettingsCategory.KeyboardMouse, SettingsIds.ActionConfirm,
                "UI 확인", "선택한 메뉴 항목을 실행합니다.", "<Keyboard>/enter"));

            // ---------------- 컨트롤러 ----------------
            list.Add(Slider(SettingsIds.ControllerLeftDeadzone, SettingsCategory.Controller,
                "왼쪽 스틱 데드존", "왼쪽 스틱의 중앙 무반응 범위입니다.", 0.15f, 0f, 0.8f, Step05));
            list.Add(Slider(SettingsIds.ControllerRightDeadzone, SettingsCategory.Controller,
                "오른쪽 스틱 데드존", "오른쪽 스틱의 중앙 무반응 범위입니다.", 0.15f, 0f, 0.8f, Step05));
            list.Add(Slider(SettingsIds.ControllerLeftOuterDeadzone, SettingsCategory.Controller,
                "왼쪽 스틱 외곽 데드존", "이 값 이상은 최대 입력으로 간주합니다.", 0.95f, 0.8f, 1f, 0.01f));
            list.Add(Slider(SettingsIds.ControllerRightOuterDeadzone, SettingsCategory.Controller,
                "오른쪽 스틱 외곽 데드존", "이 값 이상은 최대 입력으로 간주합니다.", 0.95f, 0.8f, 1f, 0.01f));
            list.Add(Slider(SettingsIds.ControllerAimSensitivity, SettingsCategory.Controller,
                "조준 감도", "컨트롤러 조준 전체 감도 배율입니다.", 1f, 0.1f, 3f, Step05));
            list.Add(Slider(SettingsIds.ControllerXSensitivity, SettingsCategory.Controller,
                "X축 감도", "좌우 방향 감도 배율입니다.", 1f, 0.1f, 3f, Step05));
            list.Add(Slider(SettingsIds.ControllerYSensitivity, SettingsCategory.Controller,
                "Y축 감도", "상하 방향 감도 배율입니다.", 1f, 0.1f, 3f, Step05));
            list.Add(Toggle(SettingsIds.ControllerInvertX, SettingsCategory.Controller,
                "X축 반전", "좌우 입력을 반전합니다.", false));
            list.Add(Toggle(SettingsIds.ControllerInvertY, SettingsCategory.Controller,
                "Y축 반전", "상하 입력을 반전합니다.", false));
            list.Add(Choice(SettingsIds.ControllerResponseCurve, SettingsCategory.Controller,
                "응답 곡선", "스틱 입력의 반응 곡선입니다. 조정값은 플레이테스트 후 재검토합니다.",
                "Linear", new[] { "Linear", "Classic", "Dynamic" }));
            list.Add(Slider(SettingsIds.ControllerTriggerDeadzone, SettingsCategory.Controller,
                "트리거 데드존", "트리거 입력이 무시되는 초기 구간입니다.", 0.1f, 0f, 0.8f, Step05));
            list.Add(Slider(SettingsIds.ControllerTriggerThreshold, SettingsCategory.Controller,
                "트리거 임계값", "트리거를 눌림으로 판정하는 지점입니다.", 0.5f, 0.1f, 1f, Step05));
            list.Add(Toggle(SettingsIds.ControllerVibration, SettingsCategory.Controller,
                "진동 사용", "컨트롤러 진동을 사용합니다. 지원하지 않는 장치에서는 비활성화됩니다.", true));
            list.Add(Slider(SettingsIds.ControllerVibrationStrength, SettingsCategory.Controller,
                "전체 진동 강도", "모든 진동에 곱해지는 전체 강도입니다.", 1f, 0f, 1f, Step05));
            list.Add(Slider(SettingsIds.ControllerAttackVibration, SettingsCategory.Controller,
                "공격 진동", "공격 시 진동 강도입니다.", 1f, 0f, 1f, Step05));
            list.Add(Slider(SettingsIds.ControllerHitVibration, SettingsCategory.Controller,
                "피격 진동", "피격 시 진동 강도입니다.", 1f, 0f, 1f, Step05));
            list.Add(Slider(SettingsIds.ControllerExplosionVibration, SettingsCategory.Controller,
                "폭발 진동", "폭발 시 진동 강도입니다.", 1f, 0f, 1f, Step05));

            list.Add(Binding(SettingsCategory.Controller, SettingsIds.ActionAttack,
                "공격", "공격 입력입니다.", "<Gamepad>/rightTrigger"));
            list.Add(Binding(SettingsCategory.Controller, SettingsIds.ActionDash,
                "대시", "대시 입력입니다.", "<Gamepad>/leftTrigger"));
            list.Add(Binding(SettingsCategory.Controller, SettingsIds.ActionInteract,
                "상호작용", "상호작용 입력입니다. UI 확인과 겹치지 않도록 기본값은 X 버튼입니다.",
                "<Gamepad>/buttonWest"));
            list.Add(Binding(SettingsCategory.Controller, SettingsIds.ActionActiveBlessing,
                "액티브 축복", "직접 눌러 쓰는 축복을 켭니다. 한 번에 하나만 가질 수 있습니다.", "<Gamepad>/buttonNorth"));
            list.Add(Binding(SettingsCategory.Controller, SettingsIds.ActionPreviousSlot,
                "이전 슬롯", "슬롯을 역순으로 순회합니다.", "<Gamepad>/leftShoulder"));
            list.Add(Binding(SettingsCategory.Controller, SettingsIds.ActionNextSlot,
                "다음 슬롯", "슬롯을 순서대로 순회합니다.", "<Gamepad>/rightShoulder"));
            list.Add(Binding(SettingsCategory.Controller, SettingsIds.ActionMenu,
                "설정/메뉴 열기", "인게임 메뉴를 엽니다.", "<Gamepad>/start"));
            list.Add(Binding(SettingsCategory.Controller, SettingsIds.ActionMinimap,
                "전투 현황", "참가자 전투 현황을 엽니다.", "<Gamepad>/select"));
            list.Add(Binding(SettingsCategory.Controller, SettingsIds.ActionDetails,
                "상세 능력치", "상세 능력치 보기입니다. 기본값은 오른쪽 스틱 누르기입니다.",
                "<Gamepad>/rightStickPress"));
            // 전투 현황(View)과 겹치지 않는 빈 버튼이다 (S-UX-KTR5GX). 누르면 목록이 열리고 D-pad 위아래로 고른다.
            list.Add(Binding(SettingsCategory.Controller, SettingsIds.ActionBlessingFocus,
                "축복 보기", "획득한 축복 목록을 열어 D-pad 위아래로 살펴봅니다. 기본값은 왼쪽 스틱 누르기입니다.",
                "<Gamepad>/leftStickPress"));
            list.Add(Binding(SettingsCategory.Controller, SettingsIds.ActionConfirm,
                "UI 확인", "UI 확인 입력입니다.", "<Gamepad>/buttonSouth"));
            list.Add(Binding(SettingsCategory.Controller, SettingsIds.ActionCancel,
                "UI 취소", "UI 취소/뒤로 입력입니다.", "<Gamepad>/buttonEast"));

            // ---------------- 오디오 ----------------
            list.Add(Slider(SettingsIds.AudioMaster, SettingsCategory.Audio,
                "마스터", "전체 볼륨입니다.", 1f, 0f, 1f, Step05));
            list.Add(Slider(SettingsIds.AudioMusic, SettingsCategory.Audio,
                "음악", "음악 볼륨입니다.", 1f, 0f, 1f, Step05));
            list.Add(Slider(SettingsIds.AudioSfx, SettingsCategory.Audio,
                "효과음", "효과음 볼륨입니다.", 1f, 0f, 1f, Step05));
            list.Add(Slider(SettingsIds.AudioUi, SettingsCategory.Audio,
                "UI", "UI 사운드 볼륨입니다.", 1f, 0f, 1f, Step05));
            list.Add(Slider(SettingsIds.AudioAmbience, SettingsCategory.Audio,
                "환경음", "환경음 볼륨입니다. 해당 콘텐츠가 없어도 채널 구조는 유지됩니다.", 1f, 0f, 1f, Step05));
            list.Add(Slider(SettingsIds.AudioVoice, SettingsCategory.Audio,
                "음성", "음성 볼륨입니다. 해당 콘텐츠가 없어도 채널 구조는 유지됩니다.", 1f, 0f, 1f, Step05));
            list.Add(Toggle(SettingsIds.AudioMute, SettingsCategory.Audio,
                "전체 음소거", "모든 소리를 음소거합니다.", false));
            list.Add(Toggle(SettingsIds.AudioMuteUnfocused, SettingsCategory.Audio,
                "비활성 창 음소거", "게임 창이 비활성일 때 음소거합니다.", true));

            // ---------------- 그래픽 ----------------
            list.Add(Choice(SettingsIds.GraphicsPreset, SettingsCategory.Graphics,
                "품질 프리셋", "품질 항목을 한 번에 바꿉니다. 개별 항목을 바꾸면 사용자 설정으로 전환됩니다.",
                GraphicsProfiles.DefaultPreset,
                new[] { "Low", "Medium", "High", "Ultra", "Custom" }));
            list.Add(Choice(SettingsIds.GraphicsDisplayMode, SettingsCategory.Graphics,
                "화면 모드", "테두리 없는 창/전체 화면/창 모드를 고릅니다.",
                "Borderless", new[] { "Borderless", "Fullscreen", "Windowed" }, SettingsPlatforms.Standalone));
            list.Add(Choice(SettingsIds.GraphicsResolution, SettingsCategory.Graphics,
                "해상도", "지원 해상도 목록에서 고릅니다. 비어 있으면 현재 디스플레이 해상도를 씁니다.",
                string.Empty, EmptyChoices, SettingsPlatforms.Standalone));
            list.Add(Choice(SettingsIds.GraphicsRefreshRate, SettingsCategory.Graphics,
                "주사율", "지원 주사율 목록에서 고릅니다. 0이면 디스플레이 기본값을 씁니다.",
                "0", EmptyChoices, SettingsPlatforms.Standalone));
            list.Add(Choice(SettingsIds.GraphicsVsync, SettingsCategory.Graphics,
                "수직 동기화", "0은 사용 안 함, 1은 매 프레임, 2는 두 프레임마다 동기화합니다.",
                "1", new[] { "0", "1", "2" }, SettingsPlatforms.Standalone));
            list.Add(Choice(SettingsIds.GraphicsFpsLimit, SettingsCategory.Graphics,
                "FPS 제한", "-1은 제한 없음입니다.",
                "-1", new[] { "30", "60", "90", "120", "144", "165", "240", "-1" }, SettingsPlatforms.Standalone));
            list.Add(Slider(SettingsIds.GraphicsRenderScale, SettingsCategory.Graphics,
                "렌더 스케일", "렌더 해상도 배율입니다.", 1f, 0.5f, 1.5f, Step05));
            list.Add(Choice(SettingsIds.GraphicsAntiAliasing, SettingsCategory.Graphics,
                "안티에일리어싱", "지원하지 않는 방식은 비활성화됩니다.",
                "SMAA", new[] { "None", "FXAA", "SMAA", "MSAA2", "MSAA4", "MSAA8" }));
            list.Add(Slider(SettingsIds.GraphicsTextureQuality, SettingsCategory.Graphics,
                "텍스처 품질", "0이 최고 품질이고 값이 커질수록 해상도를 낮춥니다.", 0f, 0f, 3f, 1f));
            list.Add(Choice(SettingsIds.GraphicsFiltering, SettingsCategory.Graphics,
                "이방성 필터링", "텍스처 필터링 방식입니다.",
                "Enabled", new[] { "Disabled", "Enabled", "Forced" }));
            list.Add(Choice(SettingsIds.GraphicsShadows, SettingsCategory.Graphics,
                "그림자 품질", "그림자 전반의 품질입니다.",
                "High", new[] { "Off", "Low", "Medium", "High" }));
            list.Add(Slider(SettingsIds.GraphicsShadowDistance, SettingsCategory.Graphics,
                "그림자 거리", "그림자를 그리는 최대 거리입니다.", 90f, 0f, 150f, 5f));
            list.Add(Choice(SettingsIds.GraphicsShadowResolution, SettingsCategory.Graphics,
                "그림자 해상도", "그림자 맵 해상도입니다.",
                "2048", new[] { "512", "1024", "2048", "4096" }));
            list.Add(Choice(SettingsIds.GraphicsAdditionalLights, SettingsCategory.Graphics,
                "추가 조명 수", "픽셀 단위로 계산할 추가 조명 개수입니다.",
                "4", new[] { "0", "2", "4", "8" }));
            list.Add(Choice(SettingsIds.GraphicsEffectsQuality, SettingsCategory.Graphics,
                "효과 품질", "일반 이펙트 품질입니다.", "High", OnOffQuality));
            list.Add(Choice(SettingsIds.GraphicsParticleQuality, SettingsCategory.Graphics,
                "입자 품질", "파티클 품질입니다.", "High", OnOffQuality));
            list.Add(Choice(SettingsIds.GraphicsExplosionQuality, SettingsCategory.Graphics,
                "폭발 품질", "폭발 연출 품질입니다.", "High", OnOffQuality));
            list.Add(Choice(SettingsIds.GraphicsEnvironmentDetail, SettingsCategory.Graphics,
                "환경 디테일", "환경 오브젝트 디테일입니다.", "High", OnOffQuality));
            list.Add(Choice(SettingsIds.GraphicsVegetationDensity, SettingsCategory.Graphics,
                "식생 밀도", "풀/나무 등의 밀도입니다.", "High", OnOffQuality));
            list.Add(Choice(SettingsIds.GraphicsDecalQuality, SettingsCategory.Graphics,
                "데칼 품질", "데칼 표현 품질입니다.", "High", OnOffQuality));
            list.Add(Toggle(SettingsIds.GraphicsPostProcessing, SettingsCategory.Graphics,
                "후처리", "후처리 효과를 사용합니다.", true));
            list.Add(Toggle(SettingsIds.GraphicsBloom, SettingsCategory.Graphics,
                "Bloom", "밝은 영역의 번짐 효과입니다.", true));
            list.Add(Toggle(SettingsIds.GraphicsMotionBlur, SettingsCategory.Graphics,
                "모션 블러", "움직임 번짐 효과입니다. 기본값은 꺼짐입니다.", false));
            list.Add(Toggle(SettingsIds.GraphicsDepthOfField, SettingsCategory.Graphics,
                "피사계 심도", "초점 밖 영역을 흐리게 합니다.", false));

            // ---------------- 접근성 ----------------
            list.Add(Slider(SettingsIds.AccessibilityUiScale, SettingsCategory.Accessibility,
                "UI 배율", "HUD 전체 배율과 같은 값입니다. 한쪽을 바꾸면 다른 쪽도 함께 바뀝니다.",
                1f, 0.7f, 1.3f, Step05));
            list.Add(Toggle(SettingsIds.AccessibilitySubtitles, SettingsCategory.Accessibility,
                "자막", "음성/효과음 자막을 표시합니다.", true));
            list.Add(Choice(SettingsIds.AccessibilityColorVision, SettingsCategory.Accessibility,
                "색각 보정", "색각 특성에 맞춰 색상을 보정합니다.",
                "None", new[] { "None", "Protanopia", "Deuteranopia", "Tritanopia" }));
            list.Add(Toggle(SettingsIds.AccessibilityReduceFlash, SettingsCategory.Accessibility,
                "번쩍임 줄이기", "강한 명암 변화를 완화합니다.", false));
            list.Add(Toggle(SettingsIds.AccessibilityReduceMotion, SettingsCategory.Accessibility,
                "UI 애니메이션 줄이기", "UI 이동/전환 애니메이션을 줄입니다.", false));

            return list.ToArray();
        }
    }
}

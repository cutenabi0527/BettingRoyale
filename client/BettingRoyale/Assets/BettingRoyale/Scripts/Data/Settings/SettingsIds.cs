namespace BettingRoyale.Data.Settings
{
    /// <summary>
    /// 안정적인 설정 ID 상수. 저장 문서와 코드의 문자열 오타를 막기 위한 단일 출처다.
    /// ID는 한 번 배포되면 변경하지 않는다(제거된 ID는 문서에서 무시하되 파괴하지 않는다).
    /// </summary>
    public static class SettingsIds
    {
        // ---- gameplay ----
        public const string GameplayShowDamageNumber = "gameplay.showDamageNumber";
        public const string GameplayShowOwnName = "gameplay.showOwnName";
        public const string GameplayShowOtherNames = "gameplay.showOtherNames";
        public const string GameplayShowHealthBars = "gameplay.showHealthBars";
        public const string GameplayShowAttackGuide = "gameplay.showAttackGuide";
        public const string GameplayShowThrowGuide = "gameplay.showThrowGuide";
        public const string GameplayShowThrowRange = "gameplay.showThrowRange";
        public const string GameplayAutoSwitchEquipment = "gameplay.autoSwitchEquipment";
        public const string GameplayCameraShake = "gameplay.cameraShake";
        public const string GameplayScreenEffects = "gameplay.screenEffects";
        public const string GameplayHitEffects = "gameplay.hitEffects";

        // ---- hud ----
        public const string HudScale = "hud.scale";
        /// <summary>인게임 전 화면(로그인·메뉴·로비·대기방 등)의 우하단 조작 안내. 기본 켜짐.</summary>
        public const string HudShowMenuControlsGuide = "hud.showMenuControlsGuide";
        /// <summary>준비·전투·훈련 중 HUD 목록과 창의 우하단 조작 안내. 기본 켜짐.</summary>
        public const string HudShowInGameControlsGuide = "hud.showInGameControlsGuide";
        public const string HudShowStats = "hud.showStats";
        public const string HudShowEquipment = "hud.showEquipment";
        public const string HudShowHp = "hud.showHp";
        public const string HudShowMinimap = "hud.showMinimap";
        public const string HudShowBlessings = "hud.showBlessings";
        public const string HudShowStatusEffects = "hud.showStatusEffects";
        public const string HudShowRound = "hud.showRound";
        public const string HudShowCurrency = "hud.showCurrency";
        public const string HudShowZone = "hud.showZone";
        public const string HudShowWorldNames = "hud.showWorldNames";
        public const string HudShowPerformance = "hud.showPerformance";
        public const string HudMinimapScale = "hud.minimapScale";
        public const string HudMinimapOpacity = "hud.minimapOpacity";
        public const string HudMinimapRotation = "hud.minimapRotation";
        public const string HudMinimapView = "hud.minimapView";
        public const string HudMinimapZone = "hud.minimapZone";
        public const string HudMinimapObjects = "hud.minimapObjects";

        // ---- keyboardMouse ----
        public const string KeyboardMouseSensitivity = "keyboardMouse.sensitivity";
        public const string KeyboardMouseXSensitivity = "keyboardMouse.xSensitivity";
        public const string KeyboardMouseYSensitivity = "keyboardMouse.ySensitivity";
        public const string KeyboardMouseAimMultiplier = "keyboardMouse.aimMultiplier";
        public const string KeyboardMouseInvertX = "keyboardMouse.invertX";
        public const string KeyboardMouseInvertY = "keyboardMouse.invertY";

        // ---- controller ----
        public const string ControllerLeftDeadzone = "controller.leftDeadzone";
        public const string ControllerRightDeadzone = "controller.rightDeadzone";
        public const string ControllerLeftOuterDeadzone = "controller.leftOuterDeadzone";
        public const string ControllerRightOuterDeadzone = "controller.rightOuterDeadzone";
        public const string ControllerAimSensitivity = "controller.aimSensitivity";
        public const string ControllerXSensitivity = "controller.xSensitivity";
        public const string ControllerYSensitivity = "controller.ySensitivity";
        public const string ControllerInvertX = "controller.invertX";
        public const string ControllerInvertY = "controller.invertY";
        public const string ControllerResponseCurve = "controller.responseCurve";
        public const string ControllerTriggerDeadzone = "controller.triggerDeadzone";
        public const string ControllerTriggerThreshold = "controller.triggerThreshold";
        public const string ControllerVibration = "controller.vibration";
        public const string ControllerVibrationStrength = "controller.vibrationStrength";
        public const string ControllerAttackVibration = "controller.attackVibration";
        public const string ControllerHitVibration = "controller.hitVibration";
        public const string ControllerExplosionVibration = "controller.explosionVibration";

        // ---- audio ----
        public const string AudioMaster = "audio.master";
        public const string AudioMusic = "audio.music";
        public const string AudioSfx = "audio.sfx";
        public const string AudioUi = "audio.ui";
        public const string AudioAmbience = "audio.ambience";
        public const string AudioVoice = "audio.voice";
        public const string AudioMute = "audio.mute";
        public const string AudioMuteUnfocused = "audio.muteUnfocused";

        // ---- graphics ----
        public const string GraphicsPreset = "graphics.preset";
        public const string GraphicsDisplayMode = "graphics.displayMode";
        public const string GraphicsResolution = "graphics.resolution";
        public const string GraphicsRefreshRate = "graphics.refreshRate";
        public const string GraphicsVsync = "graphics.vsync";
        public const string GraphicsFpsLimit = "graphics.fpsLimit";
        public const string GraphicsRenderScale = "graphics.renderScale";
        public const string GraphicsAntiAliasing = "graphics.antiAliasing";
        public const string GraphicsTextureQuality = "graphics.textureQuality";
        public const string GraphicsFiltering = "graphics.filtering";
        public const string GraphicsShadows = "graphics.shadows";
        public const string GraphicsShadowDistance = "graphics.shadowDistance";
        public const string GraphicsShadowResolution = "graphics.shadowResolution";
        public const string GraphicsAdditionalLights = "graphics.additionalLights";
        public const string GraphicsEffectsQuality = "graphics.effectsQuality";
        public const string GraphicsParticleQuality = "graphics.particleQuality";
        public const string GraphicsExplosionQuality = "graphics.explosionQuality";
        public const string GraphicsEnvironmentDetail = "graphics.environmentDetail";
        public const string GraphicsVegetationDensity = "graphics.vegetationDensity";
        public const string GraphicsDecalQuality = "graphics.decalQuality";
        public const string GraphicsPostProcessing = "graphics.postProcessing";
        public const string GraphicsBloom = "graphics.bloom";
        public const string GraphicsMotionBlur = "graphics.motionBlur";
        public const string GraphicsDepthOfField = "graphics.depthOfField";

        // ---- accessibility ----
        /// <summary>hud.scale 의 별칭. 저장은 항상 hud.scale 한 곳에만 이뤄진다.</summary>
        public const string AccessibilityUiScale = "accessibility.uiScale";
        public const string AccessibilitySubtitles = "accessibility.subtitles";
        public const string AccessibilityColorVision = "accessibility.colorVision";
        public const string AccessibilityReduceFlash = "accessibility.reduceFlash";
        public const string AccessibilityReduceMotion = "accessibility.reduceMotion";

        // ---- keyboardMouse 바인딩 액션 ----
        public const string ActionMoveUp = "moveUp";
        public const string ActionMoveDown = "moveDown";
        public const string ActionMoveLeft = "moveLeft";
        public const string ActionMoveRight = "moveRight";
        public const string ActionAttack = "attack";
        public const string ActionDash = "dash";
        public const string ActionInteract = "interact";
        /// <summary>액티브 축복 켜기 — 키보드 Q · 게임패드 Y (S-GD-NJQ5NM). 키보드·컨트롤러 둘 다 있다.</summary>
        public const string ActionActiveBlessing = "activeBlessing";
        public const string ActionCancel = "cancel";
        public const string ActionSlot1 = "slot1";
        public const string ActionSlot2 = "slot2";
        public const string ActionSlot3 = "slot3";
        public const string ActionSlot4 = "slot4";
        public const string ActionMenu = "menu";
        public const string ActionMinimap = "minimap";
        public const string ActionDetails = "details";

        // ---- controller 전용 바인딩 액션 ----
        public const string ActionPreviousSlot = "previousSlot";
        public const string ActionNextSlot = "nextSlot";
        public const string ActionConfirm = "confirm";
        /// <summary>
        /// 획득한 축복 목록 보기 — 게임패드 왼쪽 스틱 누르기 (S-UX-KTR5GX). 마우스는 레일에 올리기만 하면 되므로 키보드 쪽이 없다.
        /// </summary>
        public const string ActionBlessingFocus = "blessingFocus";
    }
}

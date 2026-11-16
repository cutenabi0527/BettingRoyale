using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Settings
{
    /// <summary>
    /// 그래픽 품질 프리셋 표와 적용 헬퍼.
    /// 프리셋은 품질 항목만 바꾸고 디스플레이/하드웨어 항목(화면 모드·해상도·주사율·VSync·FPS 제한)은 보존한다.
    /// 개별 품질 항목을 바꾸면 프리셋은 Custom 으로 내려간다.
    /// </summary>
    public static class GraphicsProfiles
    {
        public const string Low = "Low";
        public const string Medium = "Medium";
        public const string High = "High";
        public const string Ultra = "Ultra";
        public const string Custom = "Custom";

        /// <summary>기본 프리셋. 정의 표의 개별 품질 기본값과 반드시 일치한다.</summary>
        public const string DefaultPreset = High;

        /// <summary>프리셋이 관리하는 품질 항목 ID(디스플레이/하드웨어 항목은 제외).</summary>
        private static readonly string[] ManagedIdList =
        {
            SettingsIds.GraphicsRenderScale,
            SettingsIds.GraphicsAntiAliasing,
            SettingsIds.GraphicsTextureQuality,
            SettingsIds.GraphicsFiltering,
            SettingsIds.GraphicsShadows,
            SettingsIds.GraphicsShadowDistance,
            SettingsIds.GraphicsShadowResolution,
            SettingsIds.GraphicsAdditionalLights,
            SettingsIds.GraphicsEffectsQuality,
            SettingsIds.GraphicsParticleQuality,
            SettingsIds.GraphicsExplosionQuality,
            SettingsIds.GraphicsEnvironmentDetail,
            SettingsIds.GraphicsVegetationDensity,
            SettingsIds.GraphicsDecalQuality,
            SettingsIds.GraphicsPostProcessing,
            SettingsIds.GraphicsBloom,
            SettingsIds.GraphicsMotionBlur,
            SettingsIds.GraphicsDepthOfField
        };

        /// <summary>프리셋이 건드리지 않는 디스플레이/하드웨어 항목 ID.</summary>
        private static readonly string[] PreservedIdList =
        {
            SettingsIds.GraphicsDisplayMode,
            SettingsIds.GraphicsResolution,
            SettingsIds.GraphicsRefreshRate,
            SettingsIds.GraphicsVsync,
            SettingsIds.GraphicsFpsLimit
        };

        private static readonly string[] PresetNameList = { Low, Medium, High, Ultra, Custom };

        private static readonly Dictionary<string, SettingValue[]> Profiles =
            new Dictionary<string, SettingValue[]>(StringComparer.Ordinal)
            {
                {
                    Low, new[]
                    {
                        new SettingValue(SettingsIds.GraphicsRenderScale, "0.75"),
                        new SettingValue(SettingsIds.GraphicsAntiAliasing, "None"),
                        new SettingValue(SettingsIds.GraphicsTextureQuality, "2"),
                        new SettingValue(SettingsIds.GraphicsFiltering, "Disabled"),
                        new SettingValue(SettingsIds.GraphicsShadows, "Low"),
                        new SettingValue(SettingsIds.GraphicsShadowDistance, "40"),
                        new SettingValue(SettingsIds.GraphicsShadowResolution, "512"),
                        new SettingValue(SettingsIds.GraphicsAdditionalLights, "0"),
                        new SettingValue(SettingsIds.GraphicsEffectsQuality, "Low"),
                        new SettingValue(SettingsIds.GraphicsParticleQuality, "Low"),
                        new SettingValue(SettingsIds.GraphicsExplosionQuality, "Low"),
                        new SettingValue(SettingsIds.GraphicsEnvironmentDetail, "Low"),
                        new SettingValue(SettingsIds.GraphicsVegetationDensity, "Low"),
                        new SettingValue(SettingsIds.GraphicsDecalQuality, "Low"),
                        new SettingValue(SettingsIds.GraphicsPostProcessing, "false"),
                        new SettingValue(SettingsIds.GraphicsBloom, "false"),
                        new SettingValue(SettingsIds.GraphicsMotionBlur, "false"),
                        new SettingValue(SettingsIds.GraphicsDepthOfField, "false")
                    }
                },
                {
                    Medium, new[]
                    {
                        new SettingValue(SettingsIds.GraphicsRenderScale, "0.9"),
                        new SettingValue(SettingsIds.GraphicsAntiAliasing, "FXAA"),
                        new SettingValue(SettingsIds.GraphicsTextureQuality, "1"),
                        new SettingValue(SettingsIds.GraphicsFiltering, "Enabled"),
                        new SettingValue(SettingsIds.GraphicsShadows, "Medium"),
                        new SettingValue(SettingsIds.GraphicsShadowDistance, "60"),
                        new SettingValue(SettingsIds.GraphicsShadowResolution, "1024"),
                        new SettingValue(SettingsIds.GraphicsAdditionalLights, "2"),
                        new SettingValue(SettingsIds.GraphicsEffectsQuality, "Medium"),
                        new SettingValue(SettingsIds.GraphicsParticleQuality, "Medium"),
                        new SettingValue(SettingsIds.GraphicsExplosionQuality, "Medium"),
                        new SettingValue(SettingsIds.GraphicsEnvironmentDetail, "Medium"),
                        new SettingValue(SettingsIds.GraphicsVegetationDensity, "Medium"),
                        new SettingValue(SettingsIds.GraphicsDecalQuality, "Medium"),
                        new SettingValue(SettingsIds.GraphicsPostProcessing, "true"),
                        new SettingValue(SettingsIds.GraphicsBloom, "true"),
                        new SettingValue(SettingsIds.GraphicsMotionBlur, "false"),
                        new SettingValue(SettingsIds.GraphicsDepthOfField, "false")
                    }
                },
                {
                    High, new[]
                    {
                        new SettingValue(SettingsIds.GraphicsRenderScale, "1"),
                        new SettingValue(SettingsIds.GraphicsAntiAliasing, "SMAA"),
                        new SettingValue(SettingsIds.GraphicsTextureQuality, "0"),
                        new SettingValue(SettingsIds.GraphicsFiltering, "Enabled"),
                        new SettingValue(SettingsIds.GraphicsShadows, "High"),
                        new SettingValue(SettingsIds.GraphicsShadowDistance, "90"),
                        new SettingValue(SettingsIds.GraphicsShadowResolution, "2048"),
                        new SettingValue(SettingsIds.GraphicsAdditionalLights, "4"),
                        new SettingValue(SettingsIds.GraphicsEffectsQuality, "High"),
                        new SettingValue(SettingsIds.GraphicsParticleQuality, "High"),
                        new SettingValue(SettingsIds.GraphicsExplosionQuality, "High"),
                        new SettingValue(SettingsIds.GraphicsEnvironmentDetail, "High"),
                        new SettingValue(SettingsIds.GraphicsVegetationDensity, "High"),
                        new SettingValue(SettingsIds.GraphicsDecalQuality, "High"),
                        new SettingValue(SettingsIds.GraphicsPostProcessing, "true"),
                        new SettingValue(SettingsIds.GraphicsBloom, "true"),
                        new SettingValue(SettingsIds.GraphicsMotionBlur, "false"),
                        new SettingValue(SettingsIds.GraphicsDepthOfField, "false")
                    }
                },
                {
                    Ultra, new[]
                    {
                        new SettingValue(SettingsIds.GraphicsRenderScale, "1"),
                        new SettingValue(SettingsIds.GraphicsAntiAliasing, "MSAA4"),
                        new SettingValue(SettingsIds.GraphicsTextureQuality, "0"),
                        new SettingValue(SettingsIds.GraphicsFiltering, "Forced"),
                        new SettingValue(SettingsIds.GraphicsShadows, "High"),
                        new SettingValue(SettingsIds.GraphicsShadowDistance, "150"),
                        new SettingValue(SettingsIds.GraphicsShadowResolution, "4096"),
                        new SettingValue(SettingsIds.GraphicsAdditionalLights, "8"),
                        new SettingValue(SettingsIds.GraphicsEffectsQuality, "High"),
                        new SettingValue(SettingsIds.GraphicsParticleQuality, "High"),
                        new SettingValue(SettingsIds.GraphicsExplosionQuality, "High"),
                        new SettingValue(SettingsIds.GraphicsEnvironmentDetail, "High"),
                        new SettingValue(SettingsIds.GraphicsVegetationDensity, "High"),
                        new SettingValue(SettingsIds.GraphicsDecalQuality, "High"),
                        new SettingValue(SettingsIds.GraphicsPostProcessing, "true"),
                        new SettingValue(SettingsIds.GraphicsBloom, "true"),
                        new SettingValue(SettingsIds.GraphicsMotionBlur, "false"),
                        new SettingValue(SettingsIds.GraphicsDepthOfField, "true")
                    }
                }
            };

        /// <summary>Custom 을 포함한 프리셋 선택지.</summary>
        public static IReadOnlyList<string> PresetNames
        {
            get { return PresetNameList; }
        }

        /// <summary>프리셋이 덮어쓰는 품질 항목 ID 목록.</summary>
        public static IReadOnlyList<string> ManagedIds
        {
            get { return ManagedIdList; }
        }

        /// <summary>프리셋이 절대 건드리지 않는 디스플레이/하드웨어 항목 ID 목록.</summary>
        public static IReadOnlyList<string> PreservedIds
        {
            get { return PreservedIdList; }
        }

        public static bool IsManaged(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            for (var i = 0; i < ManagedIdList.Length; i++)
            {
                if (string.Equals(ManagedIdList[i], id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsPreset(string preset)
        {
            if (string.IsNullOrEmpty(preset))
            {
                return false;
            }

            for (var i = 0; i < PresetNameList.Length; i++)
            {
                if (string.Equals(PresetNameList[i], preset, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Custom 이 아닌 프리셋의 품질 값 목록. 없으면 null.</summary>
        public static IReadOnlyList<SettingValue> Profile(string preset)
        {
            return Profiles.TryGetValue(preset ?? string.Empty, out var values) ? values : null;
        }

        /// <summary>
        /// 프리셋을 문서에 적용한다. Custom 이나 알 수 없는 이름이면 품질 값은 건드리지 않고
        /// graphics.preset 만 기록한다. 디스플레이/하드웨어 항목은 항상 보존된다.
        /// </summary>
        public static void ApplyPreset(UserSettings settings, string preset)
        {
            if (settings == null)
            {
                return;
            }

            var values = Profile(preset);
            if (values != null)
            {
                for (var i = 0; i < values.Count; i++)
                {
                    settings.Set(values[i].id, values[i].value);
                }
            }

            settings.Set(SettingsIds.GraphicsPreset, IsPreset(preset) ? preset : Custom);
        }

        /// <summary>현재 품질 값과 정확히 일치하는 프리셋 이름. 없으면 Custom.</summary>
        public static string DetectPreset(UserSettings settings)
        {
            if (settings == null)
            {
                return Custom;
            }

            for (var i = 0; i < PresetNameList.Length; i++)
            {
                var name = PresetNameList[i];
                var values = Profile(name);
                if (values == null)
                {
                    continue;
                }

                if (MatchesProfile(settings, values))
                {
                    return name;
                }
            }

            return Custom;
        }

        public static bool MatchesProfile(UserSettings settings, IReadOnlyList<SettingValue> values)
        {
            if (settings == null || values == null)
            {
                return false;
            }

            for (var i = 0; i < values.Count; i++)
            {
                var expected = values[i];
                if (!string.Equals(settings.Get(expected.id), expected.value, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}

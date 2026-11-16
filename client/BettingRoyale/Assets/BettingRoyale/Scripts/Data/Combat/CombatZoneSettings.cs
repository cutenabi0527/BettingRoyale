using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Combat
{
    /// <summary>틱 피해 계산 방식(S-CLI-6V2XAP). 확정값은 전부 <see cref="Flat"/> 이다.</summary>
    public enum CombatZoneDamageKind
    {
        /// <summary><c>DamageValue</c> 를 초당 절대 피해로 쓴다.</summary>
        Flat = 0,

        /// <summary>최대 체력 × <c>DamageValue</c>. 아직 쓰지 않으며 교체 가능성만 열어둔다.</summary>
        MaxHealthRatio = 1,
    }

    /// <summary>
    /// 자기장 한 단계의 설정(S-CLI-6V2XAP 스키마, 수치 정본 S-GD-3B7RWQ·S-GD-657ZN5).
    /// <b>반경이 아니라 면적 비율로 담는다</b> — 맵이 바뀌어도 같은 데이터를 쓰기 위해서다.
    /// </summary>
    public readonly struct CombatZoneStageSettings
    {
        /// <summary>전투 카운트다운 종료 기준 이 단계의 축소 시작 시각.</summary>
        public readonly float StartSeconds;

        /// <summary>축소에 걸리는 시간. <c>0</c> 이면 즉시 전환이다.</summary>
        public readonly float ShrinkSeconds;

        /// <summary>맵 면적 대비 안전 구역 목표 비율. <c>0</c> 이면 안전지대가 사라진다.</summary>
        public readonly float TargetAreaRatio;

        /// <summary>단계 진입 후 첫 피해 틱까지의 지연. 3단계의 79→80초 경고가 이 값이다.</summary>
        public readonly float FirstTickDelaySeconds;

        public readonly CombatZoneDamageKind DamageKind;

        /// <summary><see cref="CombatZoneDamageKind.Flat"/> 이면 초당 절대 피해.</summary>
        public readonly float DamageValue;

        public CombatZoneStageSettings(
            float startSeconds,
            float shrinkSeconds,
            float targetAreaRatio,
            float damageValue,
            float firstTickDelaySeconds = 0f,
            CombatZoneDamageKind damageKind = CombatZoneDamageKind.Flat)
        {
            Require(startSeconds, nameof(startSeconds));
            Require(shrinkSeconds, nameof(shrinkSeconds));
            Require(targetAreaRatio, nameof(targetAreaRatio));
            Require(firstTickDelaySeconds, nameof(firstTickDelaySeconds));
            Require(damageValue, nameof(damageValue));
            if (targetAreaRatio > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(targetAreaRatio), "면적 비율은 0~1 이어야 합니다.");
            }

            StartSeconds = startSeconds;
            ShrinkSeconds = shrinkSeconds;
            TargetAreaRatio = targetAreaRatio;
            FirstTickDelaySeconds = firstTickDelaySeconds;
            DamageKind = damageKind;
            DamageValue = damageValue;
        }

        /// <summary>축소가 끝나는 시각.</summary>
        public float EndSeconds => StartSeconds + ShrinkSeconds;

        /// <summary>이 단계의 첫 피해 틱이 가능해지는 시각.</summary>
        public float DamageStartSeconds => StartSeconds + FirstTickDelaySeconds;

        /// <summary>안전지대가 사라지는 전역 단계인가.</summary>
        public bool IsGlobal => TargetAreaRatio <= 0f;

        private static void Require(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(name, "유한한 음이 아닌 값이어야 합니다.");
            }
        }
    }

    /// <summary>
    /// 자기장 전체 설정(S-CLI-6V2XAP). <b><c>UnityEngine</c> 을 참조하지 않는 순수 데이터</b>라
    /// 도메인 테스트가 그대로 검증한다. 반경 계산은 맵 크기를 받아 여기서 도출한다.
    ///
    /// <para>
    /// ⚠️ 이 타입은 <b>설정을 담고 검증</b>할 뿐이며 JSON 로더·에셋 파이프라인이 아니다.
    /// S15P21A606-114 의 데이터 파이프라인은 아직 열려 있고, 그때 이 타입이 로더의 출력 모델이 된다.
    /// </para>
    /// </summary>
    public sealed class CombatZoneSettings
    {
        private readonly CombatZoneStageSettings[] stages;

        /// <summary>첫 자기장이 맵 밖에서 보이는 시간. 초기 반경이 여기서 나온다.</summary>
        public readonly float AwarenessSeconds;

        /// <summary>틱 간격. 확정값은 1초다.</summary>
        public readonly float TickIntervalSeconds;

        public CombatZoneSettings(
            float awarenessSeconds,
            float tickIntervalSeconds,
            IReadOnlyList<CombatZoneStageSettings> stages)
        {
            if (stages == null)
            {
                throw new ArgumentNullException(nameof(stages));
            }
            if (stages.Count == 0)
            {
                throw new ArgumentException("자기장 단계가 최소 하나는 있어야 합니다.", nameof(stages));
            }
            if (float.IsNaN(awarenessSeconds) || float.IsInfinity(awarenessSeconds) || awarenessSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(awarenessSeconds));
            }
            if (float.IsNaN(tickIntervalSeconds) || float.IsInfinity(tickIntervalSeconds)
                || tickIntervalSeconds <= 0f)
            {
                // 검증 규칙 8.
                throw new ArgumentOutOfRangeException(
                    nameof(tickIntervalSeconds), "틱 간격은 0보다 커야 합니다.");
            }

            var copied = new CombatZoneStageSettings[stages.Count];
            for (var i = 0; i < stages.Count; i++)
            {
                copied[i] = stages[i];
                if (i == 0)
                {
                    // 검증 규칙 4: 첫 단계의 축소 시간이 인지 시간보다 커야 초기 반경 식이 성립한다.
                    if (copied[0].ShrinkSeconds <= awarenessSeconds)
                    {
                        throw new ArgumentException(
                            "첫 단계의 shrinkSeconds 는 awarenessSeconds 보다 커야 합니다 (S-CLI-6V2XAP 검증 4).",
                            nameof(stages));
                    }

                    continue;
                }

                // 검증 규칙 1: 시작 시각 오름차순이고 구간이 겹치지 않는다.
                if (copied[i].StartSeconds < copied[i - 1].EndSeconds)
                {
                    throw new ArgumentException(
                        "자기장 단계가 겹치거나 시작 시각이 오름차순이 아닙니다 (S-CLI-6V2XAP 검증 1).",
                        nameof(stages));
                }

                // 검증 규칙 2: 면적 비율은 단조 감소한다.
                if (copied[i].TargetAreaRatio > copied[i - 1].TargetAreaRatio)
                {
                    throw new ArgumentException(
                        "면적 비율은 단조 감소해야 합니다 (S-CLI-6V2XAP 검증 2).",
                        nameof(stages));
                }
            }

            // S-GD-657ZN5: 마지막 단계는 안전 구역 0으로 전역을 덮어야 한다.
            // 폐기된 90초 종료 제한에 맞추려고 단계를 잘라내지 않으므로 종료 시각 상한 검사는 없다.
            if (!copied[copied.Length - 1].IsGlobal)
            {
                throw new ArgumentException(
                    "마지막 단계는 안전 구역 0(전역)이어야 합니다 (S-GD-657ZN5).", nameof(stages));
            }

            AwarenessSeconds = awarenessSeconds;
            TickIntervalSeconds = tickIntervalSeconds;
            this.stages = copied;
        }

        public int StageCount => stages.Length;

        public CombatZoneStageSettings GetStage(int index) => stages[index];

        /// <summary>자기장이 맵 전역을 덮는 시각. "전투 시간"의 표시 정본이다(S-GD-657ZN5).</summary>
        public float GlobalizationSeconds
        {
            get
            {
                for (var i = 0; i < stages.Length; i++)
                {
                    if (stages[i].IsGlobal)
                    {
                        return stages[i].EndSeconds;
                    }
                }

                return stages[stages.Length - 1].EndSeconds;
            }
        }

        /// <summary>전역화 이후 첫 피해 틱 시각. 확정 데이터에서는 80초다.</summary>
        public float FirstGlobalDamageSeconds
        {
            get
            {
                for (var i = 0; i < stages.Length; i++)
                {
                    if (stages[i].IsGlobal)
                    {
                        return stages[i].EndSeconds + stages[i].FirstTickDelaySeconds;
                    }
                }

                return GlobalizationSeconds;
            }
        }

        /// <summary>면적 비율에서 반경을 도출한다. <c>반경 = √(맵 면적 × 면적비 ÷ π)</c>.</summary>
        public static float RadiusForAreaRatio(float mapWidth, float mapHeight, float areaRatio)
        {
            if (areaRatio <= 0f)
            {
                return 0f;
            }

            double area = (double)Math.Abs(mapWidth) * Math.Abs(mapHeight) * areaRatio;
            return (float)Math.Sqrt(area / Math.PI);
        }

        /// <summary>맵 외접원 반경. 맵 전체가 안전한 최소 원이다.</summary>
        public static float MapCircumradius(float mapWidth, float mapHeight) =>
            (float)(Math.Sqrt((double)mapWidth * mapWidth + (double)mapHeight * mapHeight) * 0.5d);

        /// <summary>
        /// 초기 반경을 도출한다(S-CLI-6V2XAP): <c>R₀ = (R맵 × T − a × r₁) / (T − a)</c>.
        /// 100×100 맵과 확정 수치에서 76.11m 가 나오고 인지 시간이 2초가 된다.
        /// </summary>
        public float ResolveInitialRadius(float mapWidth, float mapHeight)
        {
            CombatZoneStageSettings first = stages[0];
            float mapRadius = MapCircumradius(mapWidth, mapHeight);
            float firstRadius = RadiusForAreaRatio(mapWidth, mapHeight, first.TargetAreaRatio);
            double denominator = first.ShrinkSeconds - AwarenessSeconds;
            double initial =
                (mapRadius * (double)first.ShrinkSeconds - AwarenessSeconds * (double)firstRadius) / denominator;
            return (float)Math.Max(initial, mapRadius);
        }

        /// <summary>
        /// 확정 자기장 설정(S-GD-ABSQAP 3단계 · S-GD-3B7RWQ 1·2단계 · S-CLI-6V2XAP 스키마).
        /// 18~30초 면적비 60%·초당 5, 50~60초 25%·초당 8,
        /// <b>75~82초에 걸쳐 0 으로 좁아지며 75초부터 초당 15</b>, 간격 1초.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>3단계는 더 이상 즉시 전환이 아니다.</b> 예전에는 79초에 순간적으로 전역이 되고
        /// 80초에 첫 틱이었는데, 그 설계는 <b>"라운드를 90초 안에 끝낸다"</b> 를 전제로 한 것이었다.
        /// S-GD-657ZN5 가 시간 만료를 없애면서 전제가 사라졌다 — 이제 7초에 걸쳐 중앙으로 좁아지고,
        /// 축소가 눈에 보이므로 경고 지연(<c>firstTickDelaySeconds</c>)도 두지 않는다.
        /// </remarks>
        public static CombatZoneSettings Confirmed => BettingRoyale.Data.Balance.BalanceCatalogs.Current.Zone;

        /// <summary>코드 대역 — S-GD-657ZN5 확정 단계표. 기획 데이터(zone_phases)가 있으면 <see cref="Confirmed"/> 는 그쪽이다.</summary>
        public static CombatZoneSettings CreatePlaceholderConfirmed() => new CombatZoneSettings(
            awarenessSeconds: 2f,
            tickIntervalSeconds: 1f,
            stages: new[]
            {
                new CombatZoneStageSettings(
                    startSeconds: 18f, shrinkSeconds: 12f, targetAreaRatio: 0.60f, damageValue: 5f),
                new CombatZoneStageSettings(
                    startSeconds: 50f, shrinkSeconds: 10f, targetAreaRatio: 0.25f, damageValue: 8f),
                new CombatZoneStageSettings(
                    startSeconds: 75f, shrinkSeconds: 7f, targetAreaRatio: 0f, damageValue: 15f),
            });
    }
}

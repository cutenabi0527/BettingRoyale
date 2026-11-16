using System;
using BettingRoyale.Data.Combat;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 자기장 설정 스키마 테스트 (S-CLI-6V2XAP 그릇 · S-GD-3B7RWQ 수치 · S-GD-657ZN5 전역화).
    /// 반경은 저장하지 않고 <b>면적 비율과 맵 크기</b>에서 도출한다.
    /// </summary>
    public class CombatZoneSettingsTests
    {
        private const float MapSide = 100f;

        [Test]
        public void ConfirmedStages_MatchApprovedNumbers()
        {
            CombatZoneSettings settings = CombatZoneSettings.Confirmed;

            Assert.That(settings.StageCount, Is.EqualTo(3));
            Assert.That(settings.AwarenessSeconds, Is.EqualTo(2f));
            Assert.That(settings.TickIntervalSeconds, Is.EqualTo(1f));

            CombatZoneStageSettings first = settings.GetStage(0);
            Assert.That(first.StartSeconds, Is.EqualTo(18f));
            Assert.That(first.EndSeconds, Is.EqualTo(30f));
            Assert.That(first.TargetAreaRatio, Is.EqualTo(0.60f));
            Assert.That(first.DamageValue, Is.EqualTo(5f));
            Assert.That(first.DamageKind, Is.EqualTo(CombatZoneDamageKind.Flat));

            CombatZoneStageSettings second = settings.GetStage(1);
            Assert.That(second.StartSeconds, Is.EqualTo(50f));
            Assert.That(second.EndSeconds, Is.EqualTo(60f));
            Assert.That(second.TargetAreaRatio, Is.EqualTo(0.25f));
            Assert.That(second.DamageValue, Is.EqualTo(8f));

            // 3단계는 즉시 전환이 아니라 7초에 걸친 축소다 (S-GD-ABSQAP).
            // 예전 값(79초 즉시 전환·80초 첫 틱)은 "90초 안에 끝낸다" 를 전제로 한 것이었고,
            // S-GD-657ZN5 가 시간 만료를 없애면서 전제가 사라졌다.
            CombatZoneStageSettings third = settings.GetStage(2);
            Assert.That(third.StartSeconds, Is.EqualTo(75f));
            Assert.That(third.ShrinkSeconds, Is.EqualTo(7f), "3단계는 중앙으로 7초에 걸쳐 좁아진다.");
            Assert.That(third.EndSeconds, Is.EqualTo(82f));
            Assert.That(third.IsGlobal, Is.True);
            Assert.That(third.DamageValue, Is.EqualTo(15f));
            Assert.That(third.DamageStartSeconds, Is.EqualTo(75f),
                "축소가 눈에 보이므로 경고 지연을 두지 않는다 — 3단계 피해는 75초부터다.");

            Assert.That(settings.GlobalizationSeconds, Is.EqualTo(82f));
            Assert.That(settings.FirstGlobalDamageSeconds, Is.EqualTo(82f),
                "중심에 서 있어도 맞기 시작하는 시각이다. 3단계 피해 자체는 75초부터 들어간다.");
        }

        [Test]
        public void RadiusComesFromAreaRatio_OnA100x100Map()
        {
            Assert.That(
                CombatZoneSettings.RadiusForAreaRatio(MapSide, MapSide, 0.60f),
                Is.EqualTo(43.70f).Within(0.01f));
            Assert.That(
                CombatZoneSettings.RadiusForAreaRatio(MapSide, MapSide, 0.25f),
                Is.EqualTo(28.21f).Within(0.01f));
            Assert.That(CombatZoneSettings.RadiusForAreaRatio(MapSide, MapSide, 0f), Is.EqualTo(0f));
            Assert.That(
                CombatZoneSettings.MapCircumradius(MapSide, MapSide), Is.EqualTo(70.71f).Within(0.01f));
        }

        [Test]
        public void InitialRadius_GivesTwoSecondsOfAwareness()
        {
            CombatZoneSettings settings = CombatZoneSettings.Confirmed;
            float initial = settings.ResolveInitialRadius(MapSide, MapSide);

            Assert.That(initial, Is.EqualTo(76.11f).Within(0.01f));

            float firstRadius = CombatZoneSettings.RadiusForAreaRatio(MapSide, MapSide, 0.60f);
            float speed = (initial - firstRadius) / settings.GetStage(0).ShrinkSeconds;
            Assert.That(speed, Is.EqualTo(2.70f).Within(0.01f), "축소 속도는 이동 속도 5m/s 이하다.");

            float mapRadius = CombatZoneSettings.MapCircumradius(MapSide, MapSide);
            Assert.That((initial - mapRadius) / speed, Is.EqualTo(2f).Within(0.01f));
        }

        [Test]
        public void InitialRadius_AlwaysCoversTheMap_OnOtherMapSizes()
        {
            float initial = CombatZoneSettings.Confirmed.ResolveInitialRadius(48f, 28f);

            Assert.That(initial, Is.GreaterThanOrEqualTo(CombatZoneSettings.MapCircumradius(48f, 28f)));
        }

        [Test]
        public void Validation_RejectsOverlappingStages()
        {
            Assert.Throws<ArgumentException>(() => new CombatZoneSettings(2f, 1f, new[]
            {
                new CombatZoneStageSettings(18f, 12f, 0.6f, 5f),
                new CombatZoneStageSettings(25f, 0f, 0f, 15f),
            }));
        }

        [Test]
        public void Validation_RejectsGrowingAreaRatio()
        {
            Assert.Throws<ArgumentException>(() => new CombatZoneSettings(2f, 1f, new[]
            {
                new CombatZoneStageSettings(18f, 12f, 0.25f, 5f),
                new CombatZoneStageSettings(50f, 10f, 0.60f, 8f),
                new CombatZoneStageSettings(79f, 0f, 0f, 15f, firstTickDelaySeconds: 1f),
            }));
        }

        [Test]
        public void Validation_RequiresShrinkLongerThanAwareness()
        {
            Assert.Throws<ArgumentException>(() => new CombatZoneSettings(12f, 1f, new[]
            {
                new CombatZoneStageSettings(18f, 12f, 0.6f, 5f),
                new CombatZoneStageSettings(79f, 0f, 0f, 15f, firstTickDelaySeconds: 1f),
            }));
        }

        [Test]
        public void Validation_RequiresGlobalLastStage_AndPositiveInterval()
        {
            Assert.Throws<ArgumentException>(() => new CombatZoneSettings(2f, 1f, new[]
            {
                new CombatZoneStageSettings(18f, 12f, 0.6f, 5f),
            }), "마지막 단계가 전역이 아니면 거부한다 (S-GD-657ZN5).");

            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatZoneSettings(2f, 0f, new[]
            {
                new CombatZoneStageSettings(18f, 12f, 0.6f, 5f),
                new CombatZoneStageSettings(79f, 0f, 0f, 15f, firstTickDelaySeconds: 1f),
            }));
        }

        [Test]
        public void Validation_RejectsMalformedStageValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CombatZoneStageSettings(float.NaN, 12f, 0.6f, 5f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CombatZoneStageSettings(18f, 12f, 1.5f, 5f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CombatZoneStageSettings(18f, 12f, 0.6f, -1f));
        }

        [Test]
        public void NoNinetySecondCutoffIsEncoded()
        {
            // S-GD-657ZN5: 90초 강제 종료가 폐기됐다. 마지막 단계에는 종료 시각이 없다.
            CombatZoneStageSettings last = CombatZoneSettings.Confirmed.GetStage(2);

            Assert.That(last.EndSeconds, Is.EqualTo(82f));
            Assert.That(last.IsGlobal, Is.True, "마지막 단계는 끝나지 않고 전역 피해를 계속한다.");
        }
    }
}

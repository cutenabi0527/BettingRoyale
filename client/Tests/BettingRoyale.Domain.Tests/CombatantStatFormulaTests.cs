using System;
using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Remote;
using BettingRoyale.Data.Upgrade;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 캐릭터에 넣는 최종 능력치의 공용 식. <b>모든 증감은 합연산</b>이다(기획 §1.3 — 공격 속도 디버프만 곱한다).
    /// </summary>
    [TestFixture]
    public sealed class CombatantStatFormulaTests
    {
        private const float Configured = 5f;

        [TearDown]
        public void TearDown() => GameContentProvider.Reset();

        private static void LoadMoveSpeedBase(float value)
        {
            GameContentProvider.Set(new GameContent(
                schemaVersion: 1,
                weapons: Array.Empty<WeaponRow>(),
                blessings: Array.Empty<BlessingDefinition>(),
                stats: new StatRegistry(new[]
                {
                    new StatDefinition(UpgradeStatKeys.MoveSpeed, "이동 속도", value, StatKind.Multiplier),
                }),
                effects: new BlessingEffectCatalog(new[] { new BlessingEffectRow("x-t1", "k", "1") }),
                constants: new Dictionary<string, string>(StringComparer.Ordinal)));
        }

        // ── 기본값 ─────────────────────────────────────────────────────────

        [Test]
        public void 엑셀이_없으면_기본값은_캐릭터_설정값이다()
        {
            Assert.That(CombatantStatFormula.MoveSpeedBase(5.5f), Is.EqualTo(5.5f));
        }

        [Test]
        public void 엑셀이_있으면_기본값은_엑셀_값이다()
        {
            // 팀장 결정 — 코드가 엑셀 값을 쓴다. 기획이 엑셀에서 6 으로 바꾸면 6 으로 걷는다.
            LoadMoveSpeedBase(6f);

            Assert.That(CombatantStatFormula.MoveSpeedBase(Configured), Is.EqualTo(6f));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        public void 엑셀_기본값이_비정상이면_캐릭터_설정값이다(float bad)
        {
            LoadMoveSpeedBase(bad);

            Assert.That(CombatantStatFormula.MoveSpeedBase(Configured), Is.EqualTo(Configured));
        }

        /// <summary>
        /// 대역 계산표는 이동 속도를 배율(기본 1)로 적어 두었다. 계산표 기본값을 그대로 읽으면 개발 씬에서 초당 1m 로 걷는다.
        /// </summary>
        [Test]
        public void 대역_계산표에서도_초당_1m_가_되지_않는다()
        {
            var sheet = new StatSheet(PlaceholderBlessingBalance.CreateStatRegistry());

            Assert.That(sheet.Get(UpgradeStatKeys.MoveSpeed), Is.EqualTo(1f), "대역 정의는 배율이다 — 이 테스트의 전제.");
            Assert.That(sheet.Get(UpgradeStatKeys.MoveSpeed, CombatantStatFormula.MoveSpeedBase(Configured)),
                Is.EqualTo(Configured));
        }

        // ── 합연산 ─────────────────────────────────────────────────────────

        [Test]
        public void 상시_증감과_조건부_증감은_더한다()
        {
            // 상시 +20%, 조건부 +30% → +50%. 곱하면 +56% 가 된다.
            var sheet = new StatSheet(PlaceholderBlessingBalance.CreateStatRegistry());
            sheet.Add(UpgradeStatKeys.MoveSpeed, 0.2f, StatStackMode.Add);
            float baseSpeed = CombatantStatFormula.MoveSpeedBase(Configured);

            float speed = CombatantStatFormula.MoveSpeed(
                sheet.Get(UpgradeStatKeys.MoveSpeed, baseSpeed), baseSpeed, conditionalBonus: 0.3f);

            Assert.That(speed, Is.EqualTo(7.5f).Within(1e-5f), "5 × (1 + 0.2 + 0.3)");
        }

        [Test]
        public void 조건부만_있어도_기본값에_더한다()
        {
            Assert.That(CombatantStatFormula.MoveSpeed(Configured, Configured, 0.3f), Is.EqualTo(6.5f).Within(1e-5f));
            Assert.That(CombatantStatFormula.MoveSpeed(Configured, Configured, 0f), Is.EqualTo(Configured));
        }

        [Test]
        public void 감소가_커도_멈추지_않는다()
        {
            Assert.That(CombatantStatFormula.MoveSpeed(Configured, Configured, -5f), Is.EqualTo(0.01f));
        }

        // ── 공격 모양 ──────────────────────────────────────────────────────

        [Test]
        public void 배율은_상시와_조건부를_더한다()
        {
            // 사거리 +10% 상시, +25% 조건부 → +35%. 곱하면 +37.5%.
            Assert.That(CombatantStatFormula.CombineMultipliers(1.10f, 1.25f), Is.EqualTo(1.35f).Within(1e-5f));
            Assert.That(CombatantStatFormula.CombineMultipliers(1f, 1f), Is.EqualTo(1f), "둘 다 없으면 1.");
            Assert.That(CombatantStatFormula.CombineMultipliers(0.9f, 1f), Is.EqualTo(0.9f).Within(1e-5f), "저격수의 투사체 -10%.");
        }

        [Test]
        public void 배율이_0_아래로_내려가지_않는다()
        {
            Assert.That(CombatantStatFormula.CombineMultipliers(0.3f, 0.3f), Is.EqualTo(0.01f));
        }

        [Test]
        public void 조건부가_공격_모양을_정하면_그것이_이긴다()
        {
            // 절대 모양을 지정한 공격은 상시 폭보다 우선한다. 삼연참은 이제 감소량을 사용한다.
            Assert.That(CombatantStatFormula.HalfAngle(sheetHalfAngle: 120f, conditionalOverride: 20f), Is.EqualTo(20f));
            Assert.That(CombatantStatFormula.HalfAngle(sheetHalfAngle: 120f, conditionalOverride: -1f), Is.EqualTo(120f));
            Assert.That(CombatantStatFormula.HalfAngle(sheetHalfAngle: -1f, conditionalOverride: -1f), Is.EqualTo(-1f),
                "둘 다 없으면 무기 기본 폭.");
            Assert.That(CombatantStatFormula.HalfAngle(120f, 180f, 25f, 10f), Is.EqualTo(180f), "역날은 감소량보다 우선한다.");
            Assert.That(CombatantStatFormula.HalfAngle(5f, -1f), Is.EqualTo(5f), "일반 공격에는 삼연참 하한을 적용하지 않는다.");
        }

        [Test]
        public void 공격_스냅샷에_상시_능력치를_합치고_나머지는_그대로다()
        {
            var conditional = new BlessingAttack(7, 3, WeaponTag.Bow, 0.1f, 1f,
                rangeMultiplier: 1.25f, halfAngleOverride: -1f, projectileSpeedMultiplier: 1f,
                projectileRadius: 0.2f, staggerSeconds: 0f, recoveryMultiplier: 1f, counterWave: false,
                pierceTargets: 2);

            BlessingAttack merged = CombatantStatFormula.ApplyToAttack(conditional,
                sheetRangeMultiplier: 1.10f, sheetHalfAngle: -1f, sheetProjectileMultiplier: 1.5f);

            Assert.That(merged.RangeMultiplier, Is.EqualTo(1.35f).Within(1e-5f));
            Assert.That(merged.ProjectileSpeedMultiplier, Is.EqualTo(1.5f).Within(1e-5f));
            Assert.That(merged.HalfAngleOverride, Is.EqualTo(-1f));
            Assert.That(merged.Sequence, Is.EqualTo(7), "순번·세대가 같아야 적중 확정이 같은 공격으로 알아본다.");
            Assert.That(merged.Generation, Is.EqualTo(3));
            Assert.That(merged.DamageBonus, Is.EqualTo(0.1f));
            Assert.That(merged.PierceTargets, Is.EqualTo(2));
            Assert.That(merged.ProjectileRadius, Is.EqualTo(0.2f));
        }

        // ── 치명타 ─────────────────────────────────────────────────────────

        [Test]
        public void 치명타_확률_0과_1은_확정이다()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                Assert.That(CombatantStatFormula.RollCritical(0f, seed), Is.False);
                Assert.That(CombatantStatFormula.RollCritical(1f, seed), Is.True);
            }

            Assert.That(CombatantStatFormula.RollCritical(float.NaN, 1), Is.False);
        }

        [Test]
        public void 치명타는_같은_씨앗이면_같은_결과다()
        {
            // Host 가 한 번 굴리고 결과를 복제한다. 같은 공격을 다시 계산해도 뒤집히면 안 된다.
            for (int i = 0; i < 100; i++)
            {
                int seed = CombatantStatFormula.CriticalSeed(4, i, 2);
                Assert.That(CombatantStatFormula.RollCritical(0.3f, seed),
                    Is.EqualTo(CombatantStatFormula.RollCritical(0.3f, seed)));
            }
        }

        [TestCase(0.05f)]
        [TestCase(0.30f)]
        [TestCase(0.80f)]
        public void 치명타는_확률만큼_터진다(float chance)
        {
            // 기본 5% · 치명적인 공격 t1(+25%) · t3(+75%) 근처.
            const int attacks = 20000;
            int hits = 0;
            for (int i = 0; i < attacks; i++)
            {
                if (CombatantStatFormula.RollCritical(chance, CombatantStatFormula.CriticalSeed(i % 8 + 1, i, 1)))
                {
                    hits++;
                }
            }

            Assert.That((double)hits / attacks, Is.EqualTo(chance).Within(0.015));
        }

        [TestCase(1, 1, .05f)]
        [TestCase(1, 1, .30f)]
        [TestCase(1, 1, .80f)]
        [TestCase(1, 7, .05f)]
        [TestCase(7101, 2, .05f)]
        [TestCase(10001, 3, .30f)]
        public void ConsecutiveAttacksDoNotInheritThePreviousCriticalOutcome(int actor, int generation, float chance)
        {
            const int count = 100000;
            int hits = 0, previousHits = 0, afterHit = 0, afterMiss = 0;
            bool previous = false;
            for (int sequence = 1; sequence <= count; sequence++)
            {
                bool critical = CombatantStatFormula.RollCritical(chance,
                    CombatantStatFormula.CriticalSeed(actor, sequence, generation));
                if (critical) hits++;
                if (sequence > 1)
                {
                    if (previous) { previousHits++; if (critical) afterHit++; }
                    else if (critical) afterMiss++;
                }
                previous = critical;
            }
            double rate = (double)hits / count;
            double afterCritical = (double)afterHit / previousHits;
            double afterNormal = (double)afterMiss / (count - 1 - previousHits);
            TestContext.WriteLine($"actor={actor} generation={generation} chance={chance} total={rate} afterCritical={afterCritical} afterNormal={afterNormal}");
            Assert.That(rate, Is.EqualTo(chance).Within(.015), "전체 빈도");
            Assert.That(afterCritical, Is.EqualTo(chance).Within(.02), "치명타 직후에도 같은 확률이어야 한다");
            Assert.That(afterNormal, Is.EqualTo(chance).Within(.02), "일반 공격 직후에도 같은 확률이어야 한다");
        }

        [TestCase(1, 1)] [TestCase(1, 2)] [TestCase(1, 37)]
        [TestCase(11, 1)] [TestCase(11, 2)] [TestCase(11, 37)]
        [TestCase(7101, 1)] [TestCase(7101, 2)] [TestCase(7101, 37)]
        [TestCase(10001, 1)] [TestCase(10001, 2)] [TestCase(10001, 37)]
        public void CriticalSamplesAreUniformAndUncorrelatedAcrossAttackLags(int actor, int generation)
        {
            const int count = 100000;
            var values = new double[count];
            var bins = new int[10];
            for (int i = 0; i < count; i++)
            {
                float sample = CombatantStatFormula.CriticalSample(CombatantStatFormula.CriticalSeed(actor, i + 1, generation));
                Assert.That(sample, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
                values[i] = sample; bins[(int)(sample * 10)]++;
            }
            double chiSquared = 0;
            foreach (int bin in bins) chiSquared += (bin - count / 10.0) * (bin - count / 10.0) / (count / 10.0);
            Assert.That(chiSquared, Is.LessThan(50), "10개 구간의 균등 분포");
            double largestCorrelation = 0;
            for (int lag = 1; lag <= 32; lag++)
            {
                double x = 0, y = 0, xx = 0, yy = 0, xy = 0;
                int n = count - lag;
                for (int i = 0; i < n; i++)
                {
                    double a = values[i], b = values[i + lag];
                    x += a; y += b; xx += a * a; yy += b * b; xy += a * b;
                }
                double correlation = (n * xy - x * y) / Math.Sqrt((n * xx - x * x) * (n * yy - y * y));
                largestCorrelation = Math.Max(largestCorrelation, Math.Abs(correlation));
                Assert.That(Math.Abs(correlation), Is.LessThan(.02), "연속 공격 간격 " + lag);
            }
            TestContext.WriteLine($"actor={actor} generation={generation} chi2={chiSquared:F4} maxLagCorrelation={largestCorrelation:F6}");
        }

        [Test]
        public void SeventyFiveAttackWindowsFollowTheConfiguredThirtyPercentDistribution()
        {
            const int windows = 10000, shots = 75;
            double sum = 0, squareSum = 0;
            int lowWindows = 0;
            for (int window = 0; window < windows; window++)
            {
                int hits = 0;
                for (int shot = 1; shot <= shots; shot++)
                    if (CombatantStatFormula.RollCritical(.3f,
                        CombatantStatFormula.CriticalSeed(1, window * shots + shot, 2))) hits++;
                sum += hits; squareSum += hits * hits;
                if (hits <= 16) lowWindows++;
            }
            double mean = sum / windows, variance = squareSum / windows - mean * mean;
            // 독립 베르누이의 이항분포에서 계산한다. 현재 구현의 출력으로 기대값을 만들지 않는다.
            double probability = Math.Pow(.7, shots), lowerTail = probability;
            for (int k = 1; k <= 16; k++)
            {
                probability *= (shots - k + 1.0) / k * .3 / .7;
                lowerTail += probability;
            }
            Assert.That(mean / shots, Is.EqualTo(.3).Within(.003));
            Assert.That(variance, Is.EqualTo(shots * .3 * .7).Within(1.0));
            Assert.That(lowWindows / (double)windows, Is.EqualTo(lowerTail).Within(.01));
            TestContext.WriteLine($"windows={windows} shots={shots} rate={mean / shots:F6} variance={variance:F6} <=16rate={lowWindows / (double)windows:F6} expectedLowerTail={lowerTail:F6}");
        }

        // ── 모르는 능력치 ──────────────────────────────────────────────────

        [Test]
        public void 대역_계산표는_엑셀에만_있는_능력치를_모른다()
        {
            var sheet = new StatSheet(PlaceholderBlessingBalance.CreateStatRegistry());

            Assert.That(sheet.Knows(CombatStatKeys.CritChance), Is.False);
            Assert.That(sheet.Knows(UpgradeStatKeys.MoveSpeed), Is.True);
        }

        [Test]
        public void 계산기는_모르는_능력치에_던지지_않고_기본값을_준다()
        {
            var registry = PlaceholderBlessingBalance.CreateStatRegistry();
            var resolver = new BlessingCombatResolver(new DamagePipeline(0f), new OnePlayerMap(), registry);
            resolver.SetSheet(1, new StatSheet(registry));

            Assert.That(resolver.StatOrBaseline(new BettingRoyale.Data.Combat.CombatantId(1), CombatStatKeys.CritChance, 0f),
                Is.EqualTo(0f), "개발 씬에서 치명타 확률을 읽다가 전투가 멈추면 안 된다.");
            Assert.That(resolver.StatOrBaseline(new BettingRoyale.Data.Combat.CombatantId(1), CombatStatKeys.DashDistance, 1.8f),
                Is.EqualTo(1.8f));
        }

        [Test]
        public void 계산기는_아는_능력치면_계산표_값을_준다()
        {
            var registry = new StatRegistry(new[]
            {
                new StatDefinition(CombatStatKeys.CritChance, "치명타 확률", 0.05f, StatKind.Flat),
            });
            var sheet = new StatSheet(registry);
            sheet.Add(CombatStatKeys.CritChance, 0.25f, StatStackMode.Add);
            var resolver = new BlessingCombatResolver(new DamagePipeline(0f), new OnePlayerMap(), registry);
            resolver.SetSheet(1, sheet);

            Assert.That(resolver.StatOrBaseline(new BettingRoyale.Data.Combat.CombatantId(1), CombatStatKeys.CritChance, 0.05f),
                Is.EqualTo(0.30f).Within(1e-5f), "기본 5% + 치명적인 공격 25% = 30% — 합연산.");
        }

        private sealed class OnePlayerMap : ICombatantPlayerMap
        {
            public bool TryGetPlayerId(BettingRoyale.Data.Combat.CombatantId id, out int playerId)
            {
                playerId = id.Value;
                return true;
            }
        }
    }
}

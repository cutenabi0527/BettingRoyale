using System.Linq;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Remote;
using MessagePack;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// <c>gamedata.bin</c> → 도메인 타입 변환 검증 (S15P21A606-201).
    ///
    /// <para>
    /// 바이트를 직접 만들어 넣는다 — 파이프라인 산출물에 의존하면 그 파일이 없는 환경에서 테스트가
    /// 깨진다. 실제 산출물과의 정합은 파이프라인 빌드가 따로 확인한다.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class GameDataLoaderTests
    {
        private static byte[] Serialize(GameData data) => MessagePackSerializer.Serialize(data);

        private static GameData Sample()
        {
            return new GameData
            {
                SchemaVersion = 2,
                Weapons = new[]
                {
                    new WeaponRow { WeaponId = "sword", DisplayName = "검", Damage = 25, RangeMeters = 2f },
                },
                Blessings = new[]
                {
                    new BlessingRow
                    {
                        Id = "chip-stack-t1",
                        Name = "칩 적립",
                        Tier = "Silver",
                        Tag = "Common",
                        DescriptionTemplate = "적중할 때마다 칩이 쌓인다",
                        Parameters = new[]
                        {
                            new KeyValueRow { Key = "max_stacks", Value = "5" },
                            new KeyValueRow { Key = "damage_per_stack", Value = "0.05" },
                        },
                    },
                    new BlessingRow { Id = "precision-aim-t3", Name = "정밀 조준", Tier = "Platinum", Tag = "Bow" },
                },
                Stats = new[]
                {
                    new StatRow { Id = "attack_power", DisplayName = "공격력", BaseValue = 10f, Kind = "Flat" },
                    new StatRow { Id = "crit_rate", DisplayName = "치명타", Kind = "Multiplier", Min = 0f, Max = 1f },
                },
                Constants = new[]
                {
                    new ConstantRow { Key = "BOW_DAMAGE", Value = "15", Status = "ASSUMED" },
                    new ConstantRow { Key = "ATTACK_INPUT_MODE", Value = "CLICK", Status = "CONFIRMED" },
                },
            };
        }

        [Test]
        public void 코드_축복의_엑셀_효과_이름이_다르면_안_쓰인다고_경고한다()
        {
            // D-CLI-VMX6AN — 이 열은 엑셀에만 있는 축복의 동작을 고르는 기준이다. 코드에도 있는 축복은
            // id 로 찾으므로 여기를 고쳐도 반영되지 않는다. 조용히 넘기면 "바꿨는데 아무 일도 안 일어난다"가 된다.
            GameData data = Sample();
            data.Blessings[0].Effect = "interest_income";   // 코드는 chip-stack 으로 돈다

            GameDataLoadResult result = GameDataLoader.Load(Serialize(data));

            Assert.That(result.Success, Is.True, result.Failure);
            Assert.That(result.Warnings, Has.Some.Contains("chip-stack-t1").And.Some.Contains("interest_income"));
            Assert.That(result.Warnings, Has.Some.Contains("chip-stack"),
                "코드가 실제로 쓰는 이름을 적지 않으면 어느 쪽에 맞춰야 할지 알 수 없다.");
        }

        [Test]
        public void 코드_축복의_엑셀_효과_이름이_같으면_조용하다()
        {
            GameData data = Sample();
            data.Blessings[0].Effect = "chip-stack";

            GameDataLoadResult result = GameDataLoader.Load(Serialize(data));

            Assert.That(result.Success, Is.True, result.Failure);
            Assert.That(result.Warnings, Has.None.Contains("쓰이지 않습니다"));
        }

        [Test]
        public void 정상_바이너리는_도메인_타입으로_복원된다()
        {
            GameDataLoadResult result = GameDataLoader.Load(Serialize(Sample()));

            Assert.That(result.Success, Is.True, result.Failure);
            Assert.That(result.Content.SchemaVersion, Is.EqualTo(2));
            Assert.That(result.Content.Weapons.Count, Is.EqualTo(1));
            Assert.That(result.Content.Blessings.Count, Is.EqualTo(2));
        }

        /// <summary>엑셀은 등급·태그를 문자열로 싣는다 (D-CLI-ETYTBX). 이름으로 되돌아와야 한다.</summary>
        [Test]
        public void 등급과_무기태그가_이름으로_복원된다()
        {
            GameDataLoadResult result = GameDataLoader.Load(Serialize(Sample()));

            BlessingDefinition silver = result.Content.Blessings.Single(b => b.Id == "chip-stack-t1");
            BlessingDefinition platinum = result.Content.Blessings.Single(b => b.Id == "precision-aim-t3");

            Assert.That(silver.Tier, Is.EqualTo(BlessingTier.Silver));
            Assert.That(silver.Tag, Is.EqualTo(WeaponTag.Common));
            Assert.That(platinum.Tier, Is.EqualTo(BlessingTier.Platinum));
            Assert.That(platinum.Tag, Is.EqualTo(WeaponTag.Bow));
        }

        /// <summary>엑셀에 effects 시트가 없다 — parameters 의 쌍 하나가 계수 한 줄이 된다.</summary>
        [Test]
        public void parameters_쌍이_축복_계수_행이_된다()
        {
            GameDataLoadResult result = GameDataLoader.Load(Serialize(Sample()));

            var rows = result.Content.Effects.For("chip-stack-t1");

            Assert.That(rows.Count, Is.EqualTo(2));
            Assert.That(rows.Any(r => r.ParamKey == "max_stacks" && r.Value == "5"), Is.True);
            Assert.That(rows.Any(r => r.ParamKey == "damage_per_stack" && r.Value == "0.05"), Is.True);
        }

        [Test]
        public void 스탯과_상수가_복원된다()
        {
            GameDataLoadResult result = GameDataLoader.Load(Serialize(Sample()));

            Assert.That(result.Content.Stats.Contains("attack_power"), Is.True);
            Assert.That(result.Content.Stats.Get("attack_power").BaseValue, Is.EqualTo(10f));
            // base_value 가 빈 칸이면 그 계산 방식의 항등원이다 — crit_rate 는 Multiplier 라 1 이다.
            // (0 으로 두면 곱연산 스탯이 통째로 0 이 된다. 아래 항등원 테스트가 네 종류를 다 못 박는다.)
            Assert.That(result.Content.Stats.Get("crit_rate").BaseValue, Is.EqualTo(1f));
            Assert.That(result.Content.Stats.Get("crit_rate").Max, Is.EqualTo(1f));
            Assert.That(result.Content.Constants["ATTACK_INPUT_MODE"], Is.EqualTo("CLICK"));
        }

        [Test]
        public void 상수를_숫자로_읽을_수_있고_없으면_기본값이다()
        {
            GameDataLoadResult result = GameDataLoader.Load(Serialize(Sample()));

            Assert.That(
                GameDataLoader.ConstantOrDefault(result.Content.Constants, "BOW_DAMAGE", -1f),
                Is.EqualTo(15f));
            Assert.That(
                GameDataLoader.ConstantOrDefault(result.Content.Constants, "ATTACK_INPUT_MODE", -1f),
                Is.EqualTo(-1f), "숫자가 아닌 값은 기본값으로 떨어져야 한다");
            Assert.That(
                GameDataLoader.ConstantOrDefault(result.Content.Constants, "NOT_THERE", -1f),
                Is.EqualTo(-1f));
        }

        /// <summary>
        /// 모르는 태그를 Common 으로 떨어뜨리지 않는다 — 모든 무기에 뜨게 되어 밸런스가 조용히 달라진다.
        /// </summary>
        [Test]
        public void 모르는_태그나_등급은_버리고_경고를_남긴다()
        {
            GameData data = Sample();
            data.Blessings = new[]
            {
                new BlessingRow { Id = "bad-tag-t1", Name = "x", Tier = "Silver", Tag = "Shield" },
                new BlessingRow { Id = "bad-tier-t1", Name = "y", Tier = "Bronze", Tag = "Common" },
                new BlessingRow { Id = "good-t1", Name = "z", Tier = "Silver", Tag = "Common" },
            };

            GameDataLoadResult result = GameDataLoader.Load(Serialize(data));

            Assert.That(result.Success, Is.True);
            Assert.That(result.Content.Blessings.Select(b => b.Id), Is.EqualTo(new[] { "good-t1" }));
            Assert.That(result.Warnings.Count, Is.EqualTo(2), string.Join(" / ", result.Warnings));
        }

        /// <summary>
        /// 상위 버전은 거부한다. 모르는 Key 를 건너뛰고 계속하면 **새 규칙을 모른 채 플레이**하게 된다.
        /// </summary>
        [Test]
        public void 지원하지_않는_상위_스키마는_실패한다()
        {
            GameData data = Sample();
            data.SchemaVersion = GameDataLoader.SupportedSchemaVersion + 1;

            GameDataLoadResult result = GameDataLoader.Load(Serialize(data));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Failure, Does.Contain("상위 스키마"));
        }

        [Test]
        public void 빈_바이트나_깨진_바이트는_예외가_아니라_실패로_돌아온다()
        {
            Assert.That(GameDataLoader.Load(null).Success, Is.False);
            Assert.That(GameDataLoader.Load(new byte[0]).Success, Is.False);

            GameDataLoadResult broken = GameDataLoader.Load(new byte[] { 0xC1, 0xFF, 0x00, 0x42 });
            Assert.That(broken.Success, Is.False);
            Assert.That(broken.Failure, Is.Not.Empty);
        }

        // ── base_value 가 빈 칸일 때 ──────────────────────────────────────────

        private static StatRegistry StatsOf(params StatRow[] rows)
        {
            GameData data = Sample();
            data.Stats = rows;
            GameDataLoadResult result = GameDataLoader.Load(Serialize(data));
            Assert.That(result.Success, Is.True, result.Failure);
            return result.Content.Stats;
        }

        /// <summary>
        /// 빈 <c>base_value</c> 는 <b>그 계산 방식의 항등원</b>이다. 전부 0 으로 읽으면
        /// 곱연산 스탯이 0 이 되고 <c>Min</c> 스탯이 모든 값을 0 으로 끌어내린다.
        /// </summary>
        [Test]
        public void 빈_base_value_는_계산_방식마다_다른_항등원이_된다()
        {
            StatRegistry stats = StatsOf(
                new StatRow { Id = "flat-stat", DisplayName = "합", Kind = "Flat" },
                new StatRow { Id = "mult-stat", DisplayName = "곱", Kind = "Multiplier" },
                new StatRow { Id = "min-stat", DisplayName = "최소", Kind = "Min" },
                new StatRow { Id = "max-stat", DisplayName = "최대", Kind = "Max" });

            var sheet = new StatSheet(stats);

            Assert.That(sheet.Get("flat-stat"), Is.EqualTo(0f), "덧셈 항등원은 0 이다.");
            Assert.That(sheet.Get("mult-stat"), Is.EqualTo(1f), "곱셈 항등원은 1 이다 — 0 이면 결과가 통째로 0 이다.");
            Assert.That(float.IsPositiveInfinity(sheet.Get("min-stat")), Is.True, "min 항등원은 +∞ 다.");
            Assert.That(float.IsNegativeInfinity(sheet.Get("max-stat")), Is.True, "max 항등원은 −∞ 다.");
        }

        /// <summary>
        /// 실제 엑셀의 <c>damage-taken-fixed</c>·<c>damage-taken-cap</c> 이 빈 칸이다.
        /// 0 으로 읽히면 <see cref="DamagePipeline"/> 이 모든 피해를 0 으로 덮어써 <b>아무도 맞지 않는다.</b>
        /// </summary>
        [Test]
        public void 받는_피해_고정과_상한이_비어_있어도_피해가_사라지지_않는다()
        {
            StatRegistry stats = StatsOf(
                new StatRow { Id = DamageStatKeys.AttackPower, DisplayName = "공격력", BaseValue = 20f, Kind = "Flat" },
                new StatRow { Id = DamageStatKeys.DamageBonus, DisplayName = "피해 증가", BaseValue = 0f, Kind = "Flat" },
                new StatRow { Id = DamageStatKeys.CritDamage, DisplayName = "치명타 피해", BaseValue = 2f, Kind = "Flat" },
                new StatRow { Id = DamageStatKeys.ArmorPen, DisplayName = "관통", BaseValue = 0f, Kind = "Flat" },
                new StatRow { Id = DamageStatKeys.Lifesteal, DisplayName = "흡혈", BaseValue = 0f, Kind = "Flat" },
                new StatRow { Id = DamageStatKeys.Defense, DisplayName = "방어력", BaseValue = 0f, Kind = "Flat" },
                new StatRow
                {
                    Id = DamageStatKeys.DamageTakenMult, DisplayName = "받는 피해", BaseValue = 1f, Kind = "Multiplier",
                },
                new StatRow { Id = DamageStatKeys.DamageTakenFixed, DisplayName = "받는 피해 고정", Kind = "Min" },
                new StatRow { Id = DamageStatKeys.DamageTakenCap, DisplayName = "받는 피해 상한", Kind = "Min" });

            var attacker = new StatSheet(stats);
            var target = new StatSheet(stats);
            var pipeline = new DamagePipeline(minDamageFloor: 0f);

            DamageResult hit = pipeline.Resolve(attacker, target, DamageRequest.Attack());

            Assert.That(hit.Amount, Is.EqualTo(20f), "공격력 20 이 그대로 들어가야 한다.");
            Assert.That(hit.WasOverridden, Is.False, "빈 칸이 '받는 피해 고정 0' 으로 읽히면 안 된다.");
            Assert.That(hit.WasCapped, Is.False, "빈 칸이 '받는 피해 상한 0' 으로 읽히면 안 된다.");
        }
    }
}

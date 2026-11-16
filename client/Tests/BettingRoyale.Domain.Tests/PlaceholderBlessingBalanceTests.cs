using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.LocalPlay;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 목업 축복 밸런스가 <b>실제로 붙는지</b> 확인한다 (S-GD-FW56RC, S15P21A606-109 §G).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 필요한가.</b> 이 층은 어긋나도 예외를 던지지 않는다 — 축복 id 에 오타가 나면
    /// <c>catalog.For(id)</c> 가 빈 목록을 돌려주고 <b>그 축복만 조용히 효과가 0</b> 이 된다.
    /// 런타임 로그의 "N명 갱신" 은 그것을 잡지 못한다. 사람이 눈으로 볼 수 없는 종류의 결함이라
    /// 여기서 고정한다.
    /// </para>
    /// <para>
    /// ⚠️ <b>수치가 맞는지는 검사하지 않는다.</b> 전부 목업이라 바뀔 값이다.
    /// 검사하는 것은 <b>연결</b>이다 — id 가 맞물리는가, 스탯 키가 등록돼 있는가, 값이 실제로
    /// 시트에 들어가는가.
    /// </para>
    /// </remarks>
    [TestFixture]
    public class PlaceholderBlessingBalanceTests
    {
        private static StatRegistry Registry() => PlaceholderBlessingBalance.CreateStatRegistry();

        private static BlessingEffectCatalog Catalog() =>
            PlaceholderBlessingBalance.CreateEffectCatalog();

        /// <summary>
        /// 피해 계산이 이름으로 참조하는 스탯이 하나라도 빠지면 <see cref="StatRegistry"/> 가 던진다.
        /// 여기서 먼저 잡는다.
        /// </summary>
        [Test]
        public void 목업_스탯표는_피해계산이_읽는_키를_전부_정의한다()
        {
            StatRegistry registry = Registry();

            foreach (string key in new[]
            {
                DamageStatKeys.AttackPower, DamageStatKeys.DamageBonus, DamageStatKeys.CritDamage,
                DamageStatKeys.ArmorPen, DamageStatKeys.Lifesteal, DamageStatKeys.Defense,
                DamageStatKeys.DamageTakenMult, DamageStatKeys.DamageTakenFixed,
                DamageStatKeys.DamageTakenCap,
            })
            {
                Assert.That(registry.Contains(key), Is.True, $"스탯 '{key}' 가 정의되지 않았습니다");
            }
        }

        /// <summary>
        /// ⚠️ 축복이 없을 때 피해가 그대로 통과해야 한다. 곱연산·Min 항등원이 틀리면
        /// <b>아무도 피해를 입지 않거나 전원이 즉사한다</b> (S-GD-FW56RC §2).
        /// </summary>
        [Test]
        public void 목업_스탯표의_항등원이_피해를_통과시킨다()
        {
            var sheet = new StatSheet(Registry());

            Assert.That(sheet.Get(DamageStatKeys.DamageTakenMult), Is.EqualTo(1f),
                "곱연산 항등원이 1 이 아니면 축복 없이도 피해가 달라진다");
            Assert.That(sheet.Get(DamageStatKeys.DamageTakenFixed), Is.EqualTo(float.PositiveInfinity),
                "Min 항등원이 +∞ 가 아니면 고정 피해가 상시 적용된다");
            Assert.That(sheet.Get(DamageStatKeys.DamageTakenCap), Is.EqualTo(float.PositiveInfinity),
                "Min 항등원이 +∞ 가 아니면 피해 상한이 상시 적용된다");
        }

        /// <summary>
        /// <b>이 테스트가 이 파일의 핵심이다.</b> 카탈로그의 축복 id 가
        /// <see cref="LocalPlayBlessingCatalog"/> 와 어긋나면 그 축복은 조용히 효과가 사라진다.
        /// </summary>
        [Test]
        public void 목업_효과표의_축복_id_는_전부_실제_카탈로그에_있다()
        {
            var known = new HashSet<string>();
            foreach (BlessingDefinition definition in LocalPlayBlessingCatalog.All)
            {
                known.Add(definition.Id);
            }

            foreach (BlessingEffectRow row in PlaceholderBlessingBalance.CreateEffectRows())
            {
                Assert.That(known.Contains(row.BlessingId), Is.True,
                    $"효과표의 '{row.BlessingId}' 가 축복 카탈로그에 없습니다. "
                    + "id 가 어긋나면 그 축복만 조용히 효과가 0 이 됩니다");
            }
        }

        /// <summary>
        /// 반대 방향도 본다 — 카탈로그에 있는데 효과가 없는 축복은 "고르면 아무 일도 안 나는" 것이다.
        /// </summary>
        [Test]
        public void 카탈로그의_모든_축복이_효과를_하나_이상_갖는다()
        {
            BlessingEffectCatalog catalog = Catalog();

            foreach (BlessingDefinition definition in LocalPlayBlessingCatalog.All)
            {
                Assert.That(catalog.For(definition.Id).Count, Is.GreaterThan(0),
                    $"'{definition.Id}'({definition.DisplayName}) 에 효과가 없습니다");
            }
        }

        /// <summary>
        /// 붙이는 것까지 해본다. <c>ParamKey</c> 가 등록되지 않은 스탯이면 행동 파라미터로 새고,
        /// 값이 숫자가 아니면 <see cref="BlessingEffectBinder.Apply"/> 가 던진다.
        /// </summary>
        [Test]
        public void 모든_축복이_스탯_시트에_기여를_남긴다()
        {
            StatRegistry registry = Registry();
            var binder = new BlessingEffectBinder(registry, Catalog());

            foreach (BlessingDefinition definition in LocalPlayBlessingCatalog.All)
            {
                var sheet = new StatSheet(registry);
                BlessingBindResult result = binder.Apply(sheet, new[] { definition.Id });

                if (BlessingMvpCatalog.TryGet(definition.Id, out var mvp))
                {
                    Assert.That(result.Behavioral.Count, Is.EqualTo(1), definition.Id);
                    var state = new BlessingCombatState(new[] { definition });
                    Assert.That(state.Has(mvp.Effect), Is.True, definition.Id);
                    continue;
                }
                Assert.That(result.AppliedStatCount, Is.GreaterThan(0),
                    $"'{definition.Id}' 가 스탯 시트에 아무것도 남기지 않았습니다 — "
                    + "param_key 가 스탯으로 등록되지 않았을 수 있습니다");
                Assert.That(result.Behavioral.Count, Is.EqualTo(definition.Id.StartsWith("precision-aim-") ? 1 : 0),
                    $"'{definition.Id}' 의 행 중 스탯으로 인식되지 않은 것이 있습니다");
            }
        }

        /// <summary>
        /// 등급이 오를수록 세져야 한다. 표를 손으로 고치다 t2·t3 를 뒤집는 실수를 잡는다.
        /// </summary>
        /// <remarks>
        /// <c>quick-footwork</c> 는 <b>받는 피해</b>라 방향이 반대다 — 낮을수록 세다.
        /// </remarks>
        [Test]
        public void 등급이_오르면_효과가_세진다()
        {
            StatRegistry registry = Registry();
            var binder = new BlessingEffectBinder(registry, Catalog());

            AssertRising(binder, registry, "steel-heart", DamageStatKeys.Defense, stronger: 1);
            AssertRising(binder, registry, "precision-aim", DamageStatKeys.CritDamage, stronger: 1);
            AssertRising(binder, registry, "combat-instinct", DamageStatKeys.AttackPower, stronger: 1);
            AssertRising(binder, registry, "weak-point-analysis", DamageStatKeys.ArmorPen, stronger: 1);
            AssertRising(binder, registry, "guardians-oath", DamageStatKeys.Defense, stronger: 1);

            // 받는 피해는 낮을수록 세다.
            AssertRising(binder, registry, "quick-footwork", DamageStatKeys.DamageTakenMult, stronger: -1);
        }

        private static void AssertRising(
            BlessingEffectBinder binder, StatRegistry registry, string family, string statId, int stronger)
        {
            var values = new List<float>(3);
            for (var tier = 1; tier <= 3; tier++)
            {
                var sheet = new StatSheet(registry);
                binder.Apply(sheet, new[] { $"{family}-t{tier}" });
                values.Add(sheet.Get(statId));
            }

            for (var i = 1; i < values.Count; i++)
            {
                float delta = (values[i] - values[i - 1]) * stronger;
                Assert.That(delta, Is.GreaterThan(0f),
                    $"{family} t{i} → t{i + 1} 의 {statId} 가 세지지 않았습니다 "
                    + $"({values[i - 1]} → {values[i]})");
            }
        }
    }
}

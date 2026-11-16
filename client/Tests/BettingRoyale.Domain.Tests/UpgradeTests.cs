using System.Collections.Generic;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Upgrade;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 무기·방어구 강화 (Jira S15P21A606-107, S-GD-A2YDDJ · S-GD-Z9CJ4H).
    ///
    /// <para>
    /// <b>기대값은 기획 엑셀 <c>upgrades</c>·<c>upgrade_levels</c>·<c>upgrade_sets</c> 에서 그대로 가져왔다</b>
    /// (2026-09-23 재조정, S15P21A606-304). 카탈로그를 다시 읽어 기대값을 만들지 않는다.
    /// </para>
    ///
    /// <para>
    /// <b>이 파일이 지키는 것 셋.</b> ① 표의 값이 <b>0강 대비 누적</b>이라 단계를 올릴 때
    /// 더하면 안 된다. ② 세트는 <b>낮은 쪽</b>으로 판정하고 <b>누적</b>된다. ③ 라운드가 끝나면
    /// 두 부위 다 0강이 되고 골드는 안 돌아온다.
    /// </para>
    /// </summary>
    [TestFixture]
    public class UpgradeTests
    {
        private const string AttackPower = "attack-power";
        private const string ArmorPen = "armor-pen";
        private const string AttackSpeed = "attack-speed";
        private const string MaxHp = "max-hp";
        private const string Defense = "defense";
        private const string MoveSpeed = "move-speed";
        private const string DamageBonus = "damage-bonus";
        private const string DamageTakenMult = "damage-taken-mult";
        private const string AttackRange = "attack-range";
        private const string ProjectileRadius = "projectile-radius";

        /// <summary>축복과 같은 스탯 정의를 쓴다 — 강화는 별도 체계가 아니다.</summary>
        private static StatRegistry Registry() => new StatRegistry(new List<StatDefinition>
        {
            new StatDefinition(AttackPower, "공격력", 0f, StatKind.Flat, min: 0f),
            new StatDefinition(ArmorPen, "방어력 관통", 0f, StatKind.Flat, min: 0f),
            new StatDefinition(AttackSpeed, "공격 속도", 1f, StatKind.Multiplier),
            new StatDefinition(MaxHp, "최대 체력", 100f, StatKind.Flat, min: 1f),
            new StatDefinition(Defense, "방어력", 0f, StatKind.Flat, min: 0f),
            new StatDefinition(MoveSpeed, "이동 속도", 5f, StatKind.Multiplier),
            new StatDefinition(DamageBonus, "피해 증가", 0f, StatKind.Flat),
            new StatDefinition(DamageTakenMult, "받는 피해", 1f, StatKind.Multiplier),

            // 무기 전용 세트(검 사거리·활 화살 반경, 엑셀 upgrade_sets)가 가리킨다.
            new StatDefinition(AttackRange, "공격 사거리", 1f, StatKind.Multiplier),
            new StatDefinition(ProjectileRadius, "화살 충돌 반경", 1f, StatKind.Multiplier),
        });

        private StatRegistry registry;
        private UpgradeCatalog catalog;
        private UpgradeState state;
        private StatSheet sheet;
        private Wallet wallet;

        [SetUp]
        public void SetUp()
        {
            registry = Registry();
            catalog = PlaceholderUpgradeCatalog.Create();
            state = new UpgradeState(catalog, WeaponType.Sword, UpgradeSuccessRoll.AlwaysSucceeds);
            sheet = new StatSheet(registry);
            wallet = new Wallet(100_000);
        }

        private void Raise(UpgradeSlot slot, int times)
        {
            for (int i = 0; i < times; i++)
            {
                Assert.That(state.TryUpgrade(slot, wallet), Is.EqualTo(UpgradeAttemptResult.Success));
            }
        }

        // ── 표가 그대로 들어왔는가 ──────────────────────────────────────

        /// <summary>엑셀 upgrades — 검 5강 공격력 41 (기본 25 + 16), 방관 없음, 공속 +25%</summary>
        [Test]
        public void 검_5강은_기획서_표와_같다()
        {
            Raise(UpgradeSlot.Weapon, 5);
            sheet.SetBase(AttackPower, 25f);
            state.ApplyTo(sheet);

            Assert.That(sheet.Get(AttackPower), Is.EqualTo(41f).Within(0.0001f));
            Assert.That(sheet.Get(ArmorPen), Is.EqualTo(0f));
            Assert.That(sheet.Get(AttackSpeed), Is.EqualTo(1.25f).Within(0.0001f));
        }

        /// <summary>엑셀 upgrades — 활 5강 공격력 29 (기본 15 + 14). 검(16)과 다른 표를 읽는다</summary>
        [Test]
        public void 활_5강은_검과_다른_표를_쓴다()
        {
            state.EquippedWeapon = WeaponType.Bow;
            Raise(UpgradeSlot.Weapon, 5);
            sheet.SetBase(AttackPower, 15f);
            state.ApplyTo(sheet);

            Assert.That(sheet.Get(AttackPower), Is.EqualTo(29f).Within(0.0001f), "활은 검과 다른 표를 읽는다");
            Assert.That(sheet.Get(ArmorPen), Is.EqualTo(0f), "S-GD-G3D2WP 부터 강화에 방관이 없다");
        }

        /// <summary>엑셀 upgrades — 방어구 5강 최대 체력 165 (기본 100 + 65), 방어력 6 (S-GD-2TZT7S)</summary>
        [Test]
        public void 방어구_5강은_기획서_표와_같다()
        {
            Raise(UpgradeSlot.Armor, 5);
            state.ApplyTo(sheet);

            Assert.That(sheet.Get(MaxHp), Is.EqualTo(165f).Within(0.0001f));
            Assert.That(sheet.Get(Defense), Is.EqualTo(6f).Within(0.0001f),
                "방어구만 5강이라 세트 단계는 0 이다 — 5강 세트의 방어력 +2 는 안 붙는다");
        }

        /// <summary>
        /// <b>표의 값은 0강 대비 누적이다.</b> 단계를 올릴 때 이전 단계를 더하면 값이 몇 배가 된다.
        /// </summary>
        [Test]
        public void 단계를_올려도_이전_단계가_누적되지_않는다()
        {
            sheet.SetBase(AttackPower, 25f);

            Raise(UpgradeSlot.Weapon, 1);
            state.ApplyTo(sheet);
            Assert.That(sheet.Get(AttackPower), Is.EqualTo(28f).Within(0.0001f), "1강 = 25 + 3");

            Raise(UpgradeSlot.Weapon, 1);
            state.ApplyTo(sheet);
            Assert.That(sheet.Get(AttackPower), Is.EqualTo(31f).Within(0.0001f),
                "2강 = 25 + 6 이다. 이전 단계와 중복 합산되면 안 된다");

            Raise(UpgradeSlot.Weapon, 3);
            state.ApplyTo(sheet);
            Assert.That(sheet.Get(AttackPower), Is.EqualTo(41f).Within(0.0001f), "5강 = 25 + 16");
        }

        [Test]
        public void 여러_번_얹어도_값이_늘지_않는다()
        {
            Raise(UpgradeSlot.Weapon, 3);
            sheet.SetBase(AttackPower, 25f);

            state.ApplyTo(sheet);
            state.ApplyTo(sheet);
            state.ApplyTo(sheet);

            Assert.That(sheet.Get(AttackPower), Is.EqualTo(34f).Within(0.0001f), "라운드마다 부르는 구조라 누적되면 안 된다");
        }

        // ── 세트 효과 (기획서 §3) ───────────────────────────────────────

        [Test]
        public void 세트는_낮은_쪽_단계로_판정한다()
        {
            Raise(UpgradeSlot.Weapon, 5);
            Raise(UpgradeSlot.Armor, 1);

            Assert.That(state.SetLevel, Is.EqualTo(1), "한쪽만 5강이어도 낮은 쪽이 1강이면 1강 세트다");

            state.ApplyTo(sheet);

            // 1강 세트만: 이동 +3%
            Assert.That(sheet.Get(MoveSpeed), Is.EqualTo(5.15f).Within(0.0001f));
        }

        /// <summary>엑셀 upgrade_sets — 5강 세트면 1·3·5강이 전부 적용돼 이동 속도 +10%</summary>
        [Test]
        public void 세트는_누적된다()
        {
            Raise(UpgradeSlot.Weapon, 5);
            Raise(UpgradeSlot.Armor, 5);
            state.ApplyTo(sheet);

            Assert.That(state.SetLevel, Is.EqualTo(5));

            // 이동 5.0 × (1 + 0.03 + 0.03 + 0.04) = 5.5
            Assert.That(sheet.Get(MoveSpeed), Is.EqualTo(5.5f).Within(0.0001f), "1+3+5강 세트가 전부 붙는다");
            Assert.That(sheet.Get(DamageBonus), Is.EqualTo(0.10f).Within(0.0001f), "3% + 3% + 4%");
            Assert.That(sheet.Get(DamageTakenMult), Is.EqualTo(0.90f).Within(0.0001f), "받는 피해 3% + 3% + 4% 감소");
            Assert.That(sheet.Get(AttackRange), Is.EqualTo(1.3f).Within(0.0001f), "검을 들었으니 사거리 +10% × 3");
            Assert.That(sheet.Get(ProjectileRadius), Is.EqualTo(1f).Within(0.0001f), "활 전용 세트는 검에 붙지 않는다");
        }

        [Test]
        public void 한쪽이_0강이면_세트가_없다()
        {
            Raise(UpgradeSlot.Weapon, 5);
            state.ApplyTo(sheet);

            Assert.That(state.SetLevel, Is.Zero);
            Assert.That(sheet.Get(MoveSpeed), Is.EqualTo(5f), "몰빵에는 세트가 안 붙는다");
        }

        /// <summary>
        /// <b>무기 0~5강 × 방어구 0~5강 = 36가지를 전부 확인한다.</b>
        ///
        /// <para>
        /// 앞의 테스트들은 0·1·5강 근처만 봤다. <b>중간값(2·4강)에서 세트가 잘못 붙는 것은
        /// 그 조합을 실제로 플레이할 때까지 안 드러난다</b> — 예를 들어 "3강 이상" 을
        /// "3강일 때" 로 잘못 짜면 4강 세트에서 3강 보너스가 빠지는데, 4강 세트는
        /// 1,150골드라 자주 나오지도 않는다.
        /// </para>
        ///
        /// <para>기대표는 기획서 §3 의 누적 규칙에서 직접 계산했다 — 카탈로그를 다시 읽지 않는다.</para>
        /// </summary>
        [Test]
        public void 세트_조합_36가지가_전부_기획서대로다()
        {
            // 세트 단계별 누적 증가분 (엑셀 upgrade_sets: 1강 3% · 3강 3% · 5강 4% — 이동·피해·받는 피해 감소)
            float[] moveBySet = { 0f, 0.03f, 0.03f, 0.06f, 0.06f, 0.10f };
            float[] damageBonusBySet = { 0f, 0.03f, 0.03f, 0.06f, 0.06f, 0.10f };
            float[] takenMultBySet = { 1f, 0.97f, 0.97f, 0.94f, 0.94f, 0.90f };

            // 5강 세트에서만 붙는 방어력 +2, 공격력 +3 (S15P21A606-304)
            float[] setDefenseBySet = { 0f, 0f, 0f, 0f, 0f, 2f };
            float[] setAttackBySet = { 0f, 0f, 0f, 0f, 0f, 3f };

            // 방어구 자체 (엑셀 upgrades) — 0강 대비 누적
            float[] armorDefense = { 0f, 1.2f, 2.4f, 3.6f, 4.8f, 6f };
            float[] armorMaxHp = { 0f, 12f, 24f, 36f, 50f, 65f };

            // 무기(검) 자체 (엑셀 upgrades) — 0강 대비 누적. 방관은 강화에 없다(S-GD-G3D2WP)
            float[] swordAttack = { 0f, 3f, 6f, 9f, 12f, 16f };
            float[] swordArmorPen = { 0f, 0f, 0f, 0f, 0f, 0f };
            float[] swordAttackSpeed = { 0f, 0.05f, 0.10f, 0.15f, 0.20f, 0.25f };

            for (int weaponLevel = 0; weaponLevel <= 5; weaponLevel++)
            {
                for (int armorLevel = 0; armorLevel <= 5; armorLevel++)
                {
                    var combo = new UpgradeState(catalog, WeaponType.Sword, UpgradeSuccessRoll.AlwaysSucceeds);
                    var purse = new Wallet(100_000);
                    var comboSheet = new StatSheet(registry);

                    for (int i = 0; i < weaponLevel; i++)
                    {
                        Assert.That(combo.TryUpgrade(UpgradeSlot.Weapon, purse), Is.EqualTo(UpgradeAttemptResult.Success));
                    }

                    for (int i = 0; i < armorLevel; i++)
                    {
                        Assert.That(combo.TryUpgrade(UpgradeSlot.Armor, purse), Is.EqualTo(UpgradeAttemptResult.Success));
                    }

                    combo.ApplyTo(comboSheet);

                    int expectedSet = weaponLevel < armorLevel ? weaponLevel : armorLevel;
                    string where = $"무기 {weaponLevel}강 · 방어구 {armorLevel}강 (세트 {expectedSet})";

                    Assert.That(combo.SetLevel, Is.EqualTo(expectedSet), where);

                    Assert.That(
                        comboSheet.Get(MoveSpeed),
                        Is.EqualTo(5f * (1f + moveBySet[expectedSet])).Within(0.0001f),
                        $"{where} — 이동 속도");

                    Assert.That(
                        comboSheet.Get(DamageBonus),
                        Is.EqualTo(damageBonusBySet[expectedSet]).Within(0.0001f),
                        $"{where} — 피해 증가");

                    Assert.That(
                        comboSheet.Get(DamageTakenMult),
                        Is.EqualTo(takenMultBySet[expectedSet]).Within(0.0001f),
                        $"{where} — 받는 피해");

                    Assert.That(
                        comboSheet.Get(Defense),
                        Is.EqualTo(armorDefense[armorLevel] + setDefenseBySet[expectedSet]).Within(0.0001f),
                        $"{where} — 방어력 (방어구 {armorDefense[armorLevel]} + 세트 {setDefenseBySet[expectedSet]})");

                    // 세트가 주는 세 스탯 중 공격력만 빠져 있으면 5강 세트 +3 을 아무도 안 지킨다.
                    // 무기·방어구 자체 칸도 여기서 함께 잠근다 — 1~4강은 다른 테스트가 안 본다.
                    Assert.That(
                        comboSheet.Get(AttackPower),
                        Is.EqualTo(swordAttack[weaponLevel] + setAttackBySet[expectedSet]).Within(0.0001f),
                        $"{where} — 공격력 (무기 {swordAttack[weaponLevel]} + 세트 {setAttackBySet[expectedSet]})");

                    Assert.That(
                        comboSheet.Get(ArmorPen),
                        Is.EqualTo(swordArmorPen[weaponLevel]).Within(0.0001f),
                        $"{where} — 방어력 관통");

                    Assert.That(
                        comboSheet.Get(AttackSpeed),
                        Is.EqualTo(1f * (1f + swordAttackSpeed[weaponLevel])).Within(0.0001f),
                        $"{where} — 공격 속도");

                    Assert.That(
                        comboSheet.Get(MaxHp),
                        Is.EqualTo(100f + armorMaxHp[armorLevel]).Within(0.0001f),
                        $"{where} — 최대 체력");
                }
            }
        }

        /// <summary>
        /// <b>표의 모든 칸을 잠근다</b> — 무기 2종 × 5단계 × { 비용·성공률·공격력·방관·공속 } +
        /// 방어구 5단계 × { 비용·성공률·최대 체력·방어력 }.
        ///
        /// <para>
        /// 위 개별 테스트들은 5강 근처만 본다. <b>1~4강 칸은 아무도 안 지킨다</b> —
        /// 활 3강 공격력을 4에서 9로 바꿔도 587개가 그대로 통과했다(2026-09-10 변형 테스트).
        /// D-041 로 <c>upgrades.json</c> 로더가 붙어 표를 옮겨 적을 때가 정확히 그 위험 구간이다.
        /// </para>
        ///
        /// <para>
        /// 비용도 단계마다 따로 본다. 누적 합만 보면 <b>2강 −10 · 3강 +10 처럼 서로
        /// 상쇄되는 오타를 못 잡는다.</b>
        /// </para>
        ///
        /// <para>기대값은 기획서에서 가져왔고 카탈로그를 다시 읽지 않는다.</para>
        /// </summary>
        [Test]
        public void 무기_방어구_표_전_칸이_기획서와_같다()
        {
            // { 비용, 공격력, 방관, 공속, 성공률(%) } — 스탯은 전부 0강 대비 누적 (엑셀, S15P21A606-304)
            var weaponRows = new Dictionary<WeaponType, float[][]>
            {
                [WeaponType.Sword] = new[]
                {
                    new[] { 100f, 3f, 0f, 0.05f, 100f },
                    new[] { 150f, 6f, 0f, 0.10f, 100f },
                    new[] { 200f, 9f, 0f, 0.15f, 90f },
                    new[] { 300f, 12f, 0f, 0.20f, 75f },
                    new[] { 500f, 16f, 0f, 0.25f, 60f },
                },

                [WeaponType.Bow] = new[]
                {
                    new[] { 100f, 2.6f, 0f, 0.05f, 100f },
                    new[] { 150f, 5.3f, 0f, 0.10f, 100f },
                    new[] { 200f, 8f, 0f, 0.15f, 90f },
                    new[] { 300f, 10.6f, 0f, 0.20f, 75f },
                    new[] { 500f, 14f, 0f, 0.25f, 60f },
                },
            };

            foreach (var pair in weaponRows)
            {
                for (int level = 1; level <= pair.Value.Length; level++)
                {
                    var weapon = new UpgradeState(catalog, pair.Key, UpgradeSuccessRoll.AlwaysSucceeds);
                    var purse = new Wallet(100_000);
                    var weaponSheet = new StatSheet(registry);
                    string where = $"{pair.Key} {level}강";

                    for (int i = 1; i <= level; i++)
                    {
                        Assert.That(weapon.TryGetNextCost(UpgradeSlot.Weapon, out int cost), Is.True, where);
                        Assert.That(cost, Is.EqualTo((int)pair.Value[i - 1][0]), $"{where} — {i}강 비용");
                        Assert.That(
                            weapon.NextSuccessRatePercent(UpgradeSlot.Weapon),
                            Is.EqualTo((int)pair.Value[i - 1][4]),
                            $"{where} — {i}강 성공률");
                        Assert.That(
                            weapon.TryUpgrade(UpgradeSlot.Weapon, purse),
                            Is.EqualTo(UpgradeAttemptResult.Success),
                            where);
                    }

                    // 방어구 0강이라 세트가 안 붙는다 — 무기 표만 본다.
                    weapon.ApplyTo(weaponSheet);
                    float[] row = pair.Value[level - 1];

                    Assert.That(weaponSheet.Get(AttackPower), Is.EqualTo(row[1]).Within(0.0001f), $"{where} — 공격력");
                    Assert.That(weaponSheet.Get(ArmorPen), Is.EqualTo(row[2]).Within(0.0001f), $"{where} — 방관");
                    Assert.That(weaponSheet.Get(AttackSpeed), Is.EqualTo(1f + row[3]).Within(0.0001f), $"{where} — 공속");
                }
            }

            // { 비용, 최대 체력, 방어력, 성공률(%) } — 엑셀 upgrades(armor) · upgrade_levels
            float[][] armorRows =
            {
                new[] { 50f, 12f, 1.2f, 100f },
                new[] { 75f, 24f, 2.4f, 100f },
                new[] { 100f, 36f, 3.6f, 90f },
                new[] { 175f, 50f, 4.8f, 75f },
                new[] { 300f, 65f, 6f, 60f },
            };

            for (int level = 1; level <= armorRows.Length; level++)
            {
                var armor = new UpgradeState(catalog, WeaponType.Sword, UpgradeSuccessRoll.AlwaysSucceeds);
                var purse = new Wallet(100_000);
                var armorSheet = new StatSheet(registry);
                string where = $"방어구 {level}강";

                for (int i = 1; i <= level; i++)
                {
                    Assert.That(armor.TryGetNextCost(UpgradeSlot.Armor, out int cost), Is.True, where);
                    Assert.That(cost, Is.EqualTo((int)armorRows[i - 1][0]), $"{where} — {i}강 비용");
                    Assert.That(
                        armor.NextSuccessRatePercent(UpgradeSlot.Armor),
                        Is.EqualTo((int)armorRows[i - 1][3]),
                        $"{where} — {i}강 성공률");
                    Assert.That(
                        armor.TryUpgrade(UpgradeSlot.Armor, purse),
                        Is.EqualTo(UpgradeAttemptResult.Success),
                        where);
                }

                // 무기 0강이라 세트가 안 붙는다 — 방어구 표만 본다.
                armor.ApplyTo(armorSheet);

                Assert.That(
                    armorSheet.Get(MaxHp),
                    Is.EqualTo(100f + armorRows[level - 1][1]).Within(0.0001f),
                    $"{where} — 최대 체력");

                Assert.That(
                    armorSheet.Get(Defense),
                    Is.EqualTo(armorRows[level - 1][2]).Within(0.0001f),
                    $"{where} — 방어력");
            }
        }

        // ── 비용 (기획서 §4) ────────────────────────────────────────────

        [Test]
        public void 세트_누적_비용이_기획서와_같다()
        {
            var sword = catalog;
            sword.TryGetWeaponTrack(WeaponType.Sword, out var weapon);

            // S-GD-GX2K2G 세트 누적: 150 / 375 / 675 / 1150 / 1950
            Assert.That(weapon.CumulativeCost(3) + catalog.Armor.CumulativeCost(3), Is.EqualTo(675),
                "3강 세트 675G — 기본 지급 뒤 한쪽4강까지 투자 가능");
            Assert.That(weapon.CumulativeCost(5) + catalog.Armor.CumulativeCost(5), Is.EqualTo(1_950),
                "5강 세트 1950G — 기본 지급과 추가 수입을 합쳐 도달");
        }

        [Test]
        public void 골드가_모자라면_단계도_골드도_그대로다()
        {
            var poor = new Wallet(50);

            Assert.That(state.TryUpgrade(UpgradeSlot.Weapon, poor), Is.EqualTo(UpgradeAttemptResult.NotEnoughGold));
            Assert.That(state.WeaponLevel, Is.Zero);
            Assert.That(poor.Gold, Is.EqualTo(50));
        }

        [Test]
        public void 성공하면_그_단계의_비용만큼_빠진다()
        {
            var w = new Wallet(1_000);

            state.TryUpgrade(UpgradeSlot.Weapon, w);   // 검 1강 100
            state.TryUpgrade(UpgradeSlot.Armor, w);    // 방어구 1강 50

            Assert.That(w.Gold, Is.EqualTo(850));
        }

        // ── 경계 ────────────────────────────────────────────────────────

        [Test]
        public void 최대_단계를_넘길_수_없다()
        {
            Raise(UpgradeSlot.Weapon, 5);

            Assert.That(state.TryUpgrade(UpgradeSlot.Weapon, wallet), Is.EqualTo(UpgradeAttemptResult.MaxLevelReached));
            Assert.That(state.WeaponLevel, Is.EqualTo(5));
        }

        [Test]
        public void 표가_없는_무기는_강화할_수_없다()
        {
            // 둔기는 아직 표가 없다. 데이터가 없는 것이지 조작이 잘못된 것이 아니다.
            state.EquippedWeapon = WeaponType.Mace;

            Assert.That(state.TryUpgrade(UpgradeSlot.Weapon, wallet), Is.EqualTo(UpgradeAttemptResult.NoTrack));
            Assert.That(state.TryUpgrade(UpgradeSlot.Armor, wallet), Is.EqualTo(UpgradeAttemptResult.Success),
                "무기 표가 없어도 방어구는 올릴 수 있다");
        }

        /// <summary>
        /// 확률 실패 (S15P21A606-304 · S-GD-3PGNS5) — <b>비용은 나가고 단계는 그대로</b>다. 하락·파괴는 없다.
        /// 1·2강은 100% 라 판정기가 "실패" 를 내도 성공한다.
        /// </summary>
        [Test]
        public void 확률_실패면_골드만_나가고_단계는_그대로다()
        {
            var unlucky = new UpgradeState(catalog, WeaponType.Sword, UpgradeSuccessRoll.AlwaysFails);
            var w = new Wallet(1_000);

            Assert.That(unlucky.TryUpgrade(UpgradeSlot.Weapon, w), Is.EqualTo(UpgradeAttemptResult.Success), "1강 100%");
            Assert.That(unlucky.TryUpgrade(UpgradeSlot.Weapon, w), Is.EqualTo(UpgradeAttemptResult.Success), "2강 100%");
            Assert.That(w.Gold, Is.EqualTo(750));

            Assert.That(unlucky.NextSuccessRatePercent(UpgradeSlot.Weapon), Is.EqualTo(90));
            Assert.That(unlucky.TryUpgrade(UpgradeSlot.Weapon, w), Is.EqualTo(UpgradeAttemptResult.Failed));
            Assert.That(unlucky.WeaponLevel, Is.EqualTo(2), "실패해도 단계는 내려가지 않는다");
            Assert.That(w.Gold, Is.EqualTo(550), "3강 비용 200G 는 돌려주지 않는다");
            Assert.That(unlucky.NextSuccessRatePercent(UpgradeSlot.Weapon), Is.EqualTo(90), "같은 단계를 다시 시도한다");
        }

        /// <summary>골드가 모자라면 굴리지 않는다 — 판정 기회를 공짜로 쓰면 안 된다.</summary>
        [Test]
        public void 골드가_모자라면_확률을_굴리지_않는다()
        {
            var roll = new CountingRoll();
            var s = new UpgradeState(catalog, WeaponType.Sword, roll);

            Assert.That(s.TryUpgrade(UpgradeSlot.Weapon, new Wallet(10)), Is.EqualTo(UpgradeAttemptResult.NotEnoughGold));
            Assert.That(roll.Calls, Is.Zero);
        }

        /// <summary>같은 시드면 같은 판정 — Host 재현·테스트가 여기에 기댄다. 60% 는 실제로 둘 다 나온다.</summary>
        [Test]
        public void 시드가_같으면_판정도_같고_성공과_실패가_둘_다_나온다()
        {
            var a = new UpgradeSuccessRoll(20260923);
            var b = new UpgradeSuccessRoll(20260923);
            int successes = 0;

            for (int i = 0; i < 1000; i++)
            {
                bool first = a.Succeeds(60);
                Assert.That(b.Succeeds(60), Is.EqualTo(first), $"{i} 번째 판정");
                if (first) successes++;
            }

            Assert.That(successes, Is.InRange(540, 660), "60% 근처여야 한다");
            Assert.That(a.Succeeds(100), Is.True, "100% 는 언제나 성공");
            Assert.That(a.Succeeds(0), Is.False, "0% 는 언제나 실패");
        }

        private sealed class CountingRoll : IUpgradeSuccessRoll
        {
            public int Calls;

            public bool Succeeds(int successRatePercent)
            {
                Calls++;
                return true;
            }
        }

        // ── 라운드 초기화 ───────────────────────────────────────────────

        [Test]
        public void 라운드가_끝나면_두_부위_다_0강이_되고_골드는_안_돌아온다()
        {
            var w = new Wallet(1_000);
            state.TryUpgrade(UpgradeSlot.Weapon, w);
            state.TryUpgrade(UpgradeSlot.Armor, w);
            int spent = 1_000 - w.Gold;

            state.ResetForNextRound();

            Assert.That(state.WeaponLevel, Is.Zero);
            Assert.That(state.ArmorLevel, Is.Zero);
            Assert.That(state.SetLevel, Is.Zero);
            Assert.That(w.Gold, Is.EqualTo(1_000 - spent), "환급 없음 — 매 라운드 다시 산다");
        }

        [Test]
        public void 초기화_뒤_다시_얹으면_스탯도_원래대로_돌아간다()
        {
            sheet.SetBase(AttackPower, 25f);
            Raise(UpgradeSlot.Weapon, 5);
            state.ApplyTo(sheet);
            Assert.That(sheet.Get(AttackPower), Is.EqualTo(41f).Within(0.0001f));

            state.ResetForNextRound();
            state.ApplyTo(sheet);

            Assert.That(sheet.Get(AttackPower), Is.EqualTo(25f), "강화 기여가 전부 걷혀야 한다");
            Assert.That(sheet.Get(MoveSpeed), Is.EqualTo(5f));
        }

        // ── 무기가 늘어날 때 (S15P21A606-107) ───────────────────────────

        /// <summary>
        /// <b>데이터의 무기 이름이 자동으로 인식돼야 한다.</b> switch 로 짜면 무기를 추가할 때마다
        /// 매핑도 고쳐야 하고, 빠뜨리면 그 무기만 조용히 강화가 안 된다.
        /// </summary>
        [Test]
        public void 무기_이름은_열거형에서_자동으로_읽힌다()
        {
            Assert.That(UpgradeCatalog.TryParseWeapon("sword", out var sword), Is.True);
            Assert.That(sword, Is.EqualTo(WeaponType.Sword));

            Assert.That(UpgradeCatalog.TryParseWeapon("Bow", out var bow), Is.True, "대소문자를 가리지 않는다");
            Assert.That(bow, Is.EqualTo(WeaponType.Bow));

            // 아직 구현 안 된 무기도 이름만으로 읽힌다 — 표만 넣으면 바로 동작한다.
            Assert.That(UpgradeCatalog.TryParseWeapon("mace", out var mace), Is.True);
            Assert.That(mace, Is.EqualTo(WeaponType.Mace));

            Assert.That(UpgradeCatalog.TryParseWeapon("spear", out _), Is.False, "없는 이름은 거부한다");
            Assert.That(UpgradeCatalog.TryParseWeapon("none", out _), Is.False, "None 은 무기가 아니다");
            Assert.That(UpgradeCatalog.TryParseWeapon("", out _), Is.False);
        }

        /// <summary>
        /// <b>표를 빠뜨린 무기를 시작할 때 알 수 있어야 한다.</b> 안 그러면 플레이 중에
        /// "왜 강화가 안 되지" 로 발견된다.
        /// </summary>
        [Test]
        public void 표가_없는_무기를_미리_찾아낼_수_있다()
        {
            var playable = new[] { WeaponType.Sword, WeaponType.Bow, WeaponType.Mace };

            var missing = catalog.FindWeaponsWithoutTrack(playable);

            Assert.That(missing, Is.EqualTo(new[] { WeaponType.Mace }));
            Assert.That(catalog.WeaponTypes, Is.EquivalentTo(new[] { WeaponType.Sword, WeaponType.Bow }));
        }

        [Test]
        public void 무기_표를_추가하면_다른_곳을_안_고쳐도_동작한다()
        {
            // 새 무기가 들어오는 상황을 재현한다 — 표만 있으면 상태·세트·비용이 전부 그대로 돈다.
            catalog.TryGetWeaponTrack(WeaponType.Sword, out var borrowed);

            var extended = new UpgradeCatalog(
                new Dictionary<WeaponType, UpgradeTrack>
                {
                    [WeaponType.Sword] = borrowed,
                    [WeaponType.Mace] = borrowed,
                },
                catalog.Armor,
                new[] { new UpgradeSetBonus(1, new[] { new StatContribution(MoveSpeed, 0.03f, StatStackMode.Add) }) });

            var mace = new UpgradeState(extended, WeaponType.Mace, UpgradeSuccessRoll.AlwaysSucceeds);
            var w = new Wallet(1_000);

            Assert.That(mace.TryUpgrade(UpgradeSlot.Weapon, w), Is.EqualTo(UpgradeAttemptResult.Success));
            Assert.That(extended.FindWeaponsWithoutTrack(new[] { WeaponType.Mace }), Is.Empty);
        }

        // ── 축복과 함께 쓸 수 있는가 ────────────────────────────────────

        [Test]
        public void 축복_기여와_같은_시트에서_섞인다()
        {
            sheet.SetBase(AttackPower, 25f);
            sheet.Add(AttackPower, 5f, StatStackMode.Add, "sharpen-t1");   // 축복

            Raise(UpgradeSlot.Weapon, 3);
            state.ApplyTo(sheet);

            Assert.That(sheet.Get(AttackPower), Is.EqualTo(39f).Within(0.0001f), "기본 25 + 축복 5 + 강화 9");
        }

        [Test]
        public void 강화만_걷어내도_축복은_남는다()
        {
            sheet.SetBase(AttackPower, 25f);
            sheet.Add(AttackPower, 5f, StatStackMode.Add, "sharpen-t1");

            Raise(UpgradeSlot.Weapon, 3);
            state.ApplyTo(sheet);

            state.ResetForNextRound();
            state.ApplyTo(sheet);

            Assert.That(sheet.Get(AttackPower), Is.EqualTo(30f), "축복 5 는 남고 강화 9 만 빠진다");
        }
    }
}

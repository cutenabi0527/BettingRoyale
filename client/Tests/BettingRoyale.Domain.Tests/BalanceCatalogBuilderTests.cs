using System.Collections.Generic;
using System.Linq;
using BettingRoyale.Data.Balance;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.Preparation;
using BettingRoyale.Data.Profile;
using BettingRoyale.Data.Remote;
using BettingRoyale.Data.Shop;
using BettingRoyale.Data.Upgrade;
using MessagePack;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 엑셀 밸런스 표 → 카탈로그 (S-CLI-QXRTWX, S15P21A606-274).
    /// 표를 메모리에서 만들어 넣는다 — 실제 xlsx 는 파이프라인 빌드가 따로 검증한다. 여기서 지키는 것은
    /// "표의 숫자가 바뀌면 카탈로그가 바뀐다" 와 "표가 없거나 틀리면 코드 대역으로 떨어진다" 다.
    /// </summary>
    [TestFixture]
    public sealed class BalanceCatalogBuilderTests
    {
        private static BalanceTableRow Row(string sheet, params (string, string)[] cells) => new BalanceTableRow
        {
            Sheet = sheet,
            Cells = cells.Select(c => new KeyValueRow { Key = c.Item1, Value = c.Item2 }).ToArray(),
        };

        private static List<BalanceTableRow> SampleTables(float swordLevel3AttackPower = 16f, string potionBasis = "max_health", string potionValue = "0.20")
        {
            var rows = new List<BalanceTableRow>();
            void Up(string weapon, int level, string stat, float value) =>
                rows.Add(Row("upgrades", ("track", weapon.Length == 0 ? "armor" : "weapon"), ("weapon", weapon), ("level", level.ToString()), ("stat_id", stat), ("value", value.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            float[] swordAtk = { 5, 10, swordLevel3AttackPower, 23, 32 }, bowAtk = { 4, 8, 13, 19, 26 }, spd = { .12f, .24f, .36f, .48f, .6f };
            float[] hp = { 20, 40, 65, 95, 130 }, def = { 3, 6, 10, 15, 22 };
            int[] weaponCost = { 100, 150, 200, 300, 500 }, armorCost = { 50, 75, 100, 175, 300 };
            for (int l = 1; l <= 5; l++)
            {
                Up("sword", l, "attack-power", swordAtk[l - 1]); Up("sword", l, "attack-speed", spd[l - 1]);
                Up("bow", l, "attack-power", bowAtk[l - 1]); Up("bow", l, "attack-speed", spd[l - 1]);
                Up("", l, "max-hp", hp[l - 1]); Up("", l, "defense", def[l - 1]);
                rows.Add(Row("upgrade_levels", ("track", "weapon"), ("weapon", "sword"), ("level", l.ToString()), ("cost", weaponCost[l - 1].ToString()), ("success_rate", "1")));
                rows.Add(Row("upgrade_levels", ("track", "weapon"), ("weapon", "bow"), ("level", l.ToString()), ("cost", weaponCost[l - 1].ToString()), ("success_rate", "1")));
                rows.Add(Row("upgrade_levels", ("track", "armor"), ("weapon", ""), ("level", l.ToString()), ("cost", armorCost[l - 1].ToString()), ("success_rate", "1")));
            }

            void Set(int threshold, string filter, string stat, float inc) =>
                rows.Add(Row("upgrade_sets", ("threshold", threshold.ToString()), ("weapon_filter", filter), ("stat_id", stat), ("increment", inc.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            Set(1, "", "move-speed", .03f); Set(1, "", "damage-bonus", .03f); Set(1, "", "damage-taken-mult", -.03f);
            Set(1, "sword", "attack-range", .10f); Set(1, "bow", "projectile-radius", .10f);
            Set(3, "", "move-speed", .03f); Set(3, "", "damage-bonus", .05f); Set(3, "", "damage-taken-mult", -.05f);
            Set(3, "sword", "attack-range", .10f); Set(3, "bow", "projectile-radius", .10f);
            Set(5, "", "move-speed", .04f); Set(5, "", "damage-bonus", .07f); Set(5, "", "damage-taken-mult", -.04f);
            Set(5, "sword", "attack-range", .10f); Set(5, "bow", "projectile-radius", .10f);
            Set(5, "", "attack-power", 5f); Set(5, "", "defense", 8f);

            void Item(int id, string name, string kind, int visual, int stack, int carry, bool buy, int offer, int price) =>
                rows.Add(Row("items", ("item_id", id.ToString()), ("name", name), ("kind", kind), ("visual_id", visual.ToString()),
                    ("max_stack", stack.ToString()), ("carry_limit", carry.ToString()), ("purchasable", buy ? "TRUE" : "FALSE"),
                    ("offer_id", buy ? offer.ToString() : ""), ("price", buy ? price.ToString() : ""), ("quantity", buy ? "1" : "")));
            Item(2101, "체력 물약", "consumable", 5101, 3, 3, true, 1, 120);
            Item(2102, "신속 물약", "consumable", 5102, 2, 2, true, 2, 150);
            Item(2103, "마력 폭발병", "throwable", 5103, 3, 3, true, 3, 200);
            Item(3101, "힘의 반지", "accessory", 5301, 1, 0, true, 4, 600);
            Item(2104, "큰 체력 물약", "consumable", 5104, 1, 1, false, 0, 0);
            Item(2111, "재생 물약", "consumable", 5111, 1, 1, false, 0, 0);

            void Effect(int id, string kind, string stat, string value, string dur, string basis, string filter) =>
                rows.Add(Row("item_effects", ("item_id", id.ToString()), ("effect_kind", kind), ("stat_id", stat), ("value", value),
                    ("duration_seconds", dur), ("basis", basis), ("weapon_filter", filter)));
            Effect(2101, "instant_heal", "", potionValue, "", potionBasis, "");
            Effect(2102, "timed_move_speed", "", "0.10", "5", "", "");
            Effect(2104, "instant_heal", "", "0.50", "", "max_health", "");
            Effect(2111, "regeneration", "", "0.40", "8", "max_health", "");
            Effect(3101, "stat", "attack-power", "5", "", "", "melee");
            Effect(3101, "stat", "attack-power", "3", "", "", "ranged");
            Effect(-1, "instant_heal", "", "0.15", "", "max_health", "");
            Effect(-1, "shield", "", "10", "6", "", "");

            rows.Add(Row("throwables", ("item_id", "2103"), ("damage", "40"), ("throw_distance", "8"), ("explosion_radius", "2.5"),
                ("flight_seconds", "0.7"), ("warning_seconds", "0.6"), ("shared_cooldown_seconds", "2"), ("activation_duration_seconds", "2"), ("drop_quantity", "1")));
            rows.Add(Row("chest_loot", ("entry_id", "1"), ("item_id", "2101"), ("rarity", "common")));
            rows.Add(Row("chest_loot", ("entry_id", "2"), ("item_id", "2111"), ("rarity", "rare")));
            rows.Add(Row("chest_loot", ("entry_id", "3"), ("item_id", "2104"), ("rarity", "epic")));
            rows.Add(Row("chest_rarity_weights", ("round_group", "regular"), ("common", "58"), ("rare", "30"), ("epic", "12")));
            rows.Add(Row("chest_rarity_weights", ("round_group", "final"), ("common", "38"), ("rare", "42"), ("epic", "20")));
            rows.Add(Row("facilities", ("map_key", "default"), ("facility", "roulette"), ("x", "0"), ("y", "0"), ("z", "0"), ("radius", "8")));
            rows.Add(Row("facilities", ("map_key", "default"), ("facility", "upgrade"), ("x", "-11"), ("y", "0"), ("z", "0"), ("radius", "5")));
            rows.Add(Row("facilities", ("map_key", "default"), ("facility", "shop"), ("x", "11"), ("y", "0"), ("z", "0"), ("radius", "5")));
            int[] layout = { 1, 3, 1, 5, 1, 3, 1, 10, 1, 3, 1, 5, 1, 3, 1, 20, 1, 3, 1, 5, 1, 3, 1, 10, 5 };
            for (int i = 0; i < layout.Length; i++) rows.Add(Row("roulette_slots", ("slot_index", i.ToString()), ("number", layout[i].ToString())));
            rows.Add(Row("zone_phases", ("stage", "1"), ("start_seconds", "18"), ("shrink_seconds", "12"), ("target_area_ratio", "0.6"), ("damage_per_second", "5"), ("first_tick_delay_seconds", "0")));
            rows.Add(Row("zone_phases", ("stage", "2"), ("start_seconds", "50"), ("shrink_seconds", "10"), ("target_area_ratio", "0.25"), ("damage_per_second", "8"), ("first_tick_delay_seconds", "0")));
            rows.Add(Row("zone_phases", ("stage", "3"), ("start_seconds", "75"), ("shrink_seconds", "7"), ("target_area_ratio", "0"), ("damage_per_second", "15"), ("first_tick_delay_seconds", "0")));
            return rows;
        }

        private static Dictionary<string, string> SampleConstants(string armorPen = "FALSE") => new Dictionary<string, string>
        {
            ["CHEST_OPEN_TIME"] = "5", ["CHEST_RESPAWN_DELAY"] = "5", ["CHEST_RETRY_DELAY"] = "1", ["CHEST_REWARD_GOLD"] = "300", ["CHEST_COUNT"] = "2",
            ["FACILITY_ALLOW_OVERLAP"] = "TRUE", ["ROULETTE_BET_SECONDS"] = "10", ["ROULETTE_SPIN_SECONDS"] = "8", ["ROULETTE_RESULT_SECONDS"] = "2",
            ["ROULETTE_MIN_BET"] = "10", ["ROULETTE_OPEN_MARGIN_SECONDS"] = "10", ["ROULETTE_START_DELAY_SECONDS"] = "10",
            ["TOTAL_ROUNDS"] = "3", ["PREPARATION_SECONDS"] = "90", ["ROUND_DURATION"] = "90", ["SUMMARY_SECONDS"] = "10",
            ["ROUND_GOLD"] = "1000", ["CARRYOVER_CAP"] = "10000", ["SCORE_SURVIVAL"] = "[80,65,50,40,35,30,25,20]", ["SCORE_KILL"] = "10", ["SCORE_ASSIST"] = "3",
            ["CANDIDATE_COUNT"] = "3", ["FREE_LOCK_COUNT"] = "2", ["REROLL_COST_R1"] = "100", ["REROLL_COST_R2"] = "200", ["REROLL_COST_R3"] = "300", ["REROLL_COST_MULTIPLIER"] = "2",
            ["ZONE_AWARENESS_SECONDS"] = "2", ["ZONE_TICK_INTERVAL"] = "1", ["ARMOR_PEN_ENABLED"] = armorPen, ["DISABLED_BLESSING_IDS"] = "",
        };

        private static StatRegistry SampleStats() => new StatRegistry(new[]
        {
            new StatDefinition("attack-power", "공격력", 0f, StatKind.Flat),
            new StatDefinition("attack-speed", "공격 속도", 1f, StatKind.Multiplier),
            new StatDefinition("move-speed", "이동 속도", 6.5f, StatKind.Multiplier),
            new StatDefinition("max-hp", "최대 체력", 150f, StatKind.Flat),
            new StatDefinition("defense", "방어력", 0f, StatKind.Flat),
            new StatDefinition("damage-bonus", "피해 증가", 0f, StatKind.Flat),
            new StatDefinition("damage-taken-mult", "받는 피해", 1f, StatKind.Multiplier),
            new StatDefinition("attack-range", "공격 사거리", 1f, StatKind.Multiplier),
            new StatDefinition("projectile-radius", "화살 충돌 반경", 1f, StatKind.Multiplier),
            new StatDefinition("dash-charges", "대시 충전", 3f, StatKind.Flat),
            new StatDefinition("dash-recharge", "대시 재충전", 1.5f, StatKind.Multiplier),
            new StatDefinition("dash-duration", "대시 지속", 0.15f, StatKind.Multiplier),
        });

        private static GameData SampleWire(List<BalanceTableRow> tables = null) => new GameData
        {
            SchemaVersion = 3,
            Weapons = new[]
            {
                new WeaponRow { WeaponId = "sword", DisplayName = "검", Damage = 25, CooldownSeconds = .6f, RangeMeters = 2f },
                new WeaponRow { WeaponId = "bow", DisplayName = "활", Damage = 22, CooldownSeconds = .75f, RangeMeters = 12f, ProjectileSpeedMps = 18f, AttackSlowMultiplier = .4f, AttackSlowSeconds = .5f, CollisionRadiusMeters = .15f },
            },
            Tables = (tables ?? SampleTables()).ToArray(),
        };

        private static BalanceCatalogs Build(List<BalanceTableRow> tables = null, Dictionary<string, string> constants = null, List<string> warnings = null)
        {
            warnings ??= new List<string>();
            var built = BalanceCatalogBuilder.Build(SampleWire(tables), SampleStats(), constants ?? SampleConstants(), warnings);
            return built;
        }

        [TearDown]
        public void Reset() => GameContentProvider.Reset();

        [Test]
        public void 표가_카탈로그로_옮겨진다_강화_세트_아이템_상자_시설_룰렛_자기장_무기()
        {
            var warnings = new List<string>();
            BalanceCatalogs balance = Build(warnings: warnings);
            Assert.That(balance, Is.Not.Null, string.Join(" / ", warnings));
            Assert.That(balance.IsFromGameData, Is.True);

            Assert.That(balance.Upgrades.TryGetWeaponTrack(WeaponType.Sword, out var sword), Is.True);
            Assert.That(sword.TryGet(3, out var sword3), Is.True);
            Assert.That(sword3.CostGold, Is.EqualTo(200));
            Assert.That(sword3.Stats.Single(s => s.StatId == "attack-power").Value, Is.EqualTo(16f));
            Assert.That(balance.Upgrades.Armor.TryGet(5, out var armor5), Is.True);
            Assert.That(armor5.Stats.Single(s => s.StatId == "max-hp").Value, Is.EqualTo(130f));
            Assert.That(balance.Upgrades.SetBonuses.Select(b => b.Level), Is.EqualTo(new[] { 1, 3, 5 }));
            Assert.That(balance.Upgrades.SetBonuses[2].Stats.Any(s => s.StatId == "attack-power" && s.Value == 5f), Is.True);

            Assert.That(balance.Shop.Offers.Count, Is.EqualTo(4));
            Assert.That(balance.Items.TryGet(2104, out var large), Is.True);
            Assert.That(large.Kind, Is.EqualTo(EquipmentItemKind.Consumable));
            Assert.That(balance.Items.TryGetCarryLimit(2101, out int carry) && carry == 3, Is.True);
            Assert.That(balance.Items.TryGetThrowing(2103, out var bottle), Is.True);
            Assert.That(bottle.Damage, Is.EqualTo(40));
            Assert.That(bottle.ExplosionRadiusMeters, Is.EqualTo(2.5f));
            Assert.That(balance.Items.ShelfItemIds, Is.EquivalentTo(new[] { 2101, 2102, 3101 }), "투척물은 진열 정합 대상에서 빠진다");

            Assert.That(balance.ItemEffects.TryGetConsumableEffects(2101, out var potion), Is.True);
            Assert.That(potion[0].Kind, Is.EqualTo(ConsumableEffectKind.InstantHeal));
            Assert.That(potion[0].IsMaxHealthRatio, Is.True);
            Assert.That(potion[0].ResolveHealAmount(150f), Is.EqualTo(30f).Within(1e-4f), "최대 체력 150 의 20% = 30");
            Assert.That(balance.ItemEffects.ChestOverflowEffects.Count, Is.EqualTo(2));
            var ringMelee = balance.ItemEffects.BuildAccessoryStats(3101, WeaponType.Sword, false, "x");
            var ringRanged = balance.ItemEffects.BuildAccessoryStats(3101, WeaponType.Bow, true, "x");
            Assert.That(ringMelee.Single().Value, Is.EqualTo(5f));
            Assert.That(ringRanged.Single().Value, Is.EqualTo(3f));

            Assert.That(balance.Chest.RewardGold, Is.EqualTo(300));
            Assert.That(balance.Chest.RespawnDelaySeconds, Is.EqualTo(5f));
            Assert.That(balance.ChestLoot.Entries.Count, Is.EqualTo(3));

            Assert.That(balance.Facilities.AllowOverlap, Is.True);
            Assert.That(balance.Facilities.DefaultMap.TryGet(PreparationFacility.Roulette, out var roulette), Is.True);
            Assert.That(roulette.Radius, Is.EqualTo(8f));
            Assert.That(balance.Facilities.DefaultMap.Resolve(new WorldPosition(4f, 0f, 0f)), Is.EqualTo(PreparationFacility.Roulette));
            Assert.That(balance.Facilities.DefaultMap.Resolve(new WorldPosition(7f, 0f, 0f)), Is.EqualTo(PreparationFacility.Shop),
                "겹친 자리(룰렛 8m 안 · 상점 5m 안)는 중심이 가까운 상점이 이긴다");

            Assert.That(balance.Roulette.Layout.Count, Is.EqualTo(25));
            Assert.That(balance.Roulette.CreateConfig(20).BetTicks, Is.EqualTo(200));
            Assert.That(balance.Rounds.PreparationSeconds, Is.EqualTo(90));
            Assert.That(balance.Economy.RoundGrantGold, Is.EqualTo(1000));
            Assert.That(balance.Scoring.PlacementPoints[0], Is.EqualTo(80));
            Assert.That(balance.Blessing.CandidateCount, Is.EqualTo(3));
            Assert.That(balance.Zone.StageCount, Is.EqualTo(3));
            Assert.That(balance.Zone.GetStage(1).DamageValue, Is.EqualTo(8f));

            Assert.That(balance.Weapons.TryGet(WeaponType.Bow, out var bow), Is.True);
            Assert.That(bow.ProjectileSpeed, Is.EqualTo(18f), "엑셀 무기 정본의 활 탄속");
            Assert.That(bow.CollisionRadius, Is.EqualTo(.15f));
            Assert.That(balance.Weapons.TryGetAttackSlow(WeaponType.Bow, out var slow) && slow.IsActive, Is.True);
            Assert.That(balance.Combatant.DashCharges, Is.EqualTo(3));
            Assert.That(balance.Combatant.DashRechargeSeconds, Is.EqualTo(1.5f));
            Assert.That(balance.ArmorPenetrationEnabled, Is.False);
        }

        [Test]
        public void 표의_숫자만_바꾸면_같은_코드에서_카탈로그가_바뀐다()
        {
            BalanceCatalogs a = Build(SampleTables(swordLevel3AttackPower: 16f));
            BalanceCatalogs b = Build(SampleTables(swordLevel3AttackPower: 99f));
            a.Upgrades.TryGetWeaponTrack(WeaponType.Sword, out var trackA); trackA.TryGet(3, out var levelA);
            b.Upgrades.TryGetWeaponTrack(WeaponType.Sword, out var trackB); trackB.TryGet(3, out var levelB);
            Assert.That(levelA.Stats.Single(s => s.StatId == "attack-power").Value, Is.EqualTo(16f));
            Assert.That(levelB.Stats.Single(s => s.StatId == "attack-power").Value, Is.EqualTo(99f));
        }

        [Test]
        public void 비율과_고정량을_섞으면_표가_거부되고_코드_대역으로_떨어진다()
        {
            var warnings = new List<string>();
            BalanceCatalogs built = Build(SampleTables(potionBasis: "max_health", potionValue: "20"), warnings: warnings);
            Assert.That(built, Is.Null);
            Assert.That(warnings.Any(w => w.Contains("20 과 0.20")), Is.True, string.Join(" / ", warnings));
        }

        [Test]
        public void 표가_없으면_Current는_코드_대역이고_Default_프로퍼티들이_그것을_돌려준다()
        {
            GameContentProvider.Reset();
            BalanceCatalogs current = BalanceCatalogs.Current;
            Assert.That(current.IsFromGameData, Is.False);
            Assert.That(ReferenceEquals(current, BalanceCatalogs.Fallback), Is.True);
            Assert.That(Data.Online.OnlineCoreLoopRules.Default.PreparationSeconds, Is.EqualTo(90));
            Assert.That(Data.Economy.EconomyConfig.Default.RoundGrantGold, Is.EqualTo(1000));
            Assert.That(Data.Economy.ScoringConfig.Default.KillPoints, Is.EqualTo(10));
            Assert.That(BlessingConfig.Default.CandidateCount, Is.EqualTo(3));
            Assert.That(CombatZoneSettings.Confirmed.StageCount, Is.EqualTo(3));
            Assert.That(current.ArmorPenetrationEnabled, Is.True, "코드 대역은 관통이 켜져 있다 — 끄는 것은 데이터다");
        }

        [Test]
        public void 로더가_v3_바이너리에서_카탈로그를_붙이고_v2에서는_경고와_함께_비운다()
        {
            var v3 = SampleWire();
            v3.Stats = SampleStats().Ids.Select(id => new StatRow { Id = id, DisplayName = id, Kind = id.Contains("speed") || id.Contains("range") || id.Contains("radius") || id.Contains("mult") || id.Contains("dash-re") || id.Contains("dash-du") ? "Multiplier" : "Flat", BaseValue = id == "max-hp" ? 150f : id == "dash-charges" ? 3f : id == "dash-recharge" ? 1.5f : id == "dash-duration" ? .15f : (float?)null }).ToArray();
            v3.Constants = SampleConstants().Select(p => new ConstantRow { Key = p.Key, Value = p.Value }).ToArray();
            v3.Blessings = new[] { new BlessingRow { Id = "critical-t1", Name = "치명", Tier = "Silver", Tag = "Common", Effect = "critical", DescriptionTemplate = "x" } };
            var result = GameDataLoader.Load(MessagePackSerializer.Serialize(v3));
            Assert.That(result.Success, Is.True, string.Join(" / ", result.Warnings));
            Assert.That(result.Content.Balance, Is.Not.Null, string.Join(" / ", result.Warnings));
            Assert.That(result.Content.Balance.Rounds.TotalRounds, Is.EqualTo(3));

            var v2 = SampleWire(new List<BalanceTableRow>());
            v2.SchemaVersion = 2;
            v2.Tables = System.Array.Empty<BalanceTableRow>();
            var older = GameDataLoader.Load(MessagePackSerializer.Serialize(v2));
            Assert.That(older.Success, Is.True);
            Assert.That(older.Content.Balance, Is.Null);
            Assert.That(older.Warnings.Any(w => w.Contains("밸런스 표")), Is.False, "v2 는 표가 없는 것이 정상이라 경고하지 않는다");

            var emptyV3 = SampleWire(new List<BalanceTableRow>());
            var missing = GameDataLoader.Load(MessagePackSerializer.Serialize(emptyV3));
            Assert.That(missing.Content.Balance, Is.Null);
            Assert.That(missing.Warnings.Any(w => w.Contains("밸런스 표가 없습니다")), Is.True);
        }

        [Test]
        public void 데이터가_실리면_WeaponStats와_상점_효과_카탈로그가_데이터를_읽는다()
        {
            BalanceCatalogs balance = Build();
            GameContentProvider.Set(new GameContent(3, System.Array.Empty<WeaponRow>(), System.Array.Empty<BlessingDefinition>(),
                SampleStats(), new BlessingEffectCatalog(System.Array.Empty<BlessingEffectRow>()), SampleConstants(), null, balance));
            Assert.That(WeaponStats.TryGet(WeaponType.Bow, out var bow), Is.True);
            Assert.That(bow.CollisionRadius, Is.EqualTo(.15f));
            Assert.That(WeaponStats.GetAttackSlow(WeaponType.Bow).Seconds, Is.EqualTo(.5f));
            Assert.That(ShopItemEffectCatalog.TryGetConsumableEffects(2111, out var regen), Is.True);
            Assert.That(regen[0].IsMaxHealthRatio, Is.True);
            Assert.That(ShopItemEffectCatalog.ChestOverflowEffects.Count, Is.EqualTo(2));
            Assert.That(UpgradeSetBonusGuide.DefaultCatalog.SetBonuses[0].Stats.Count, Is.EqualTo(5));
            Assert.That(PreparationShopItemFallbackCatalog.ItemIds, Is.EquivalentTo(new[] { 2101, 2102, 3101 }));
            Assert.That(BlessingBalance.ArmorPenetrationEnabled, Is.False);
        }

        [Test]
        public void 무기_전용_세트는_든_무기에만_붙는다()
        {
            BalanceCatalogs balance = Build();
            var sheetSword = new StatSheet(SampleStats());
            var sword = new UpgradeState(balance.Upgrades, WeaponType.Sword, UpgradeSuccessRoll.AlwaysSucceeds);
            var wallet = new Data.Economy.Wallet(100000);
            sword.TryUpgrade(UpgradeSlot.Weapon, wallet); sword.TryUpgrade(UpgradeSlot.Armor, wallet);
            sword.ApplyTo(sheetSword);
            Assert.That(sheetSword.Get("attack-range"), Is.EqualTo(1.10f).Within(1e-4f));
            Assert.That(sheetSword.Get("projectile-radius"), Is.EqualTo(1f).Within(1e-4f), "검은 화살 반경 세트를 받지 않는다");

            var sheetBow = new StatSheet(SampleStats());
            var bow = new UpgradeState(balance.Upgrades, WeaponType.Bow, UpgradeSuccessRoll.AlwaysSucceeds);
            for (int i = 0; i < 5; i++) { bow.TryUpgrade(UpgradeSlot.Weapon, wallet); bow.TryUpgrade(UpgradeSlot.Armor, wallet); }
            bow.ApplyTo(sheetBow);
            Assert.That(sheetBow.Get("projectile-radius"), Is.EqualTo(1.30f).Within(1e-4f));
            Assert.That(sheetBow.Get("attack-range"), Is.EqualTo(1f).Within(1e-4f), "활은 검 사거리 세트를 받지 않는다");
            Assert.That(sheetBow.Get("move-speed"), Is.EqualTo(6.5f * 1.10f).Within(1e-3f), "공용 세트는 누적 10%");
        }

        [Test]
        public void 세트_안내는_무기_전용_줄에_무기_이름을_앞세운다()
        {
            BalanceCatalogs balance = Build();
            string text = UpgradeSetBonusGuide.Describe(balance.Upgrades.SetBonuses[0], " / ");
            Assert.That(text, Does.Contain("검: "));
            Assert.That(text, Does.Contain("활: "));
            Assert.That(text, Does.Contain("+10%"));
        }

        [Test]
        public void 비율_지속_회복은_사용_시점_최대_체력을_스냅샷해_8틱에_나눈다()
        {
            var state = new ConsumableBuffState();
            Assert.That(state.Apply(ConsumableEffect.RegenerationRatio(.40f, 8f), now: 0f, sourceItemId: 2111, maxHealth: 150f), Is.True);
            float total = 0f;
            for (int second = 1; second <= 8; second++) total += state.TakeRegenerationHeal(second + .01f);
            Assert.That(total, Is.EqualTo(60f).Within(1e-3f), "150 의 40% = 60 을 8초에 걸쳐 준다 (초당 7.5, 내림 없음)");
            Assert.That(state.TakeRegenerationHeal(20f), Is.EqualTo(0f), "총량을 다 주면 멈춘다");
        }

        [Test]
        public void 고정_지속_회복은_예전처럼_초당_값이다()
        {
            var state = new ConsumableBuffState();
            state.Apply(ConsumableEffect.Regeneration(5f, 8f), 0f, 2111, 150f);
            Assert.That(state.TakeRegenerationHeal(1.01f), Is.EqualTo(5f).Within(1e-4f));
        }

        [Test]
        public void 소모품_비우기는_무기와_장신구를_남긴다()
        {
            var inventory = new EquipmentInventory();
            inventory.ApplySnapshot(new EquipmentInventorySnapshot(1101, 3101,
                new EquipmentInventorySlot(2101, 2), new EquipmentInventorySlot(2102, 1), default));
            Assert.That(inventory.ClearConsumables(), Is.True);
            Assert.That(inventory.Snapshot.WeaponItemId, Is.EqualTo(1101));
            Assert.That(inventory.Snapshot.AccessoryItemId, Is.EqualTo(3101));
            Assert.That(inventory.Snapshot.ConsumableSlot0.IsEmpty && inventory.Snapshot.ConsumableSlot1.IsEmpty, Is.True);
            Assert.That(inventory.ClearConsumables(), Is.False, "이미 비었으면 false — 두 번 통지돼도 다시 비우지 않는다");
        }

        [Test]
        public void 겹침_허용_구역표는_닿아도_던지지_않고_비허용은_던진다()
        {
            var areas = new[]
            {
                new PreparationFacilityArea(PreparationFacility.Roulette, new WorldPosition(0, 0, 0), 8f),
                new PreparationFacilityArea(PreparationFacility.Shop, new WorldPosition(11, 0, 0), 5f),
            };
            Assert.That(() => new PreparationFacilityMap(areas), Throws.ArgumentException);
            var map = new PreparationFacilityMap(areas, allowOverlap: true);
            Assert.That(map.AllowOverlap, Is.True);
            Assert.That(map.Resolve(new WorldPosition(3f, 0f, 0f)), Is.EqualTo(PreparationFacility.Roulette));
            Assert.That(map.Resolve(new WorldPosition(7.9f, 0f, 0f)), Is.EqualTo(PreparationFacility.Shop));
            Assert.That(map.Resolve(new WorldPosition(0f, 0f, 9f)), Is.EqualTo(PreparationFacility.None));
        }

        [Test]
        public void 파생_적중_스냅샷은_일회성_효과를_끄고_새_순번을_받는다()
        {
            var original = new BlessingAttack(7, 2, WeaponTag.Bow, .3f, 1f, 1.2f, -1f, 1f, .1f, 0f, 1f, counterWave: true,
                pierceTargets: 2, splitPotReady: true, splitPotShare: .45f, splitPotRadius: 3f, crossCutReady: true, crossCutDamage: 45f, crossCutRadius: 2.5f,
                rainCheckReady: true, rainCheckArrows: 4, rainCheckRadius: 4f, rainCheckDelay: .5f, extraProjectileCount: 2, selfHealthCostFraction: .1f);
            BlessingAttack derived = original.AsDerivedHit(99);
            Assert.That(derived.Sequence, Is.EqualTo(99));
            Assert.That(derived.Generation, Is.EqualTo(2));
            Assert.That(derived.DamageBonus, Is.EqualTo(.3f));
            Assert.That(derived.RangeMultiplier, Is.EqualTo(1.2f));
            Assert.That(derived.RainCheckReady || derived.SplitPotReady || derived.CrossCutReady || derived.CounterWave, Is.False);
            Assert.That(derived.PierceTargets, Is.EqualTo(0));
            Assert.That(derived.ExtraProjectileCount, Is.EqualTo(0));
            Assert.That(derived.SelfHealthCostFraction, Is.EqualTo(0f));
        }

        [Test]
        public void 저장_외형은_카탈로그_칸_번호로_오가고_모르는_ID는_기본으로_남는다()
        {
            var catalog = new CharacterAppearanceCatalog(new Dictionary<string, IReadOnlyList<string>>
            {
                [CharacterAppearanceSlotCatalog.Body] = new[] { "Body_11", "Body_12" },
                [CharacterAppearanceSlotCatalog.Face] = new[] { "Male_Emotion_Usual_01" },
                [CharacterAppearanceSlotCatalog.Hat] = new[] { "default", "Hat_24" },
                [CharacterAppearanceSlotCatalog.Outwear] = new[] { "default", "Outwear_19" },
            });
            var preset = new CharacterAppearancePreset("sword", new[]
            {
                new CharacterAppearanceSelection(CharacterAppearanceSlotCatalog.Body, "Body_12"),
                new CharacterAppearanceSelection(CharacterAppearanceSlotCatalog.Face, "Male_Emotion_Usual_01"),
                new CharacterAppearanceSelection(CharacterAppearanceSlotCatalog.Hat, "Hat_24"),
                new CharacterAppearanceSelection(CharacterAppearanceSlotCatalog.Outwear, "Outwear_404"),
            });
            int[] indices = CharacterAppearanceSlotIndices.CreateEmpty();
            var unknown = new List<string>();
            Assert.That(CharacterAppearanceSlotIndices.TryEncode(catalog, preset, indices, unknown), Is.True);
            Assert.That(indices[0], Is.EqualTo(1));
            Assert.That(indices[1], Is.EqualTo(0));
            Assert.That(indices[CharacterAppearanceSlotCatalog.All.ToList().IndexOf(CharacterAppearanceSlotCatalog.Hat)], Is.EqualTo(1));
            Assert.That(indices[CharacterAppearanceSlotCatalog.All.ToList().IndexOf(CharacterAppearanceSlotCatalog.Outwear)], Is.EqualTo(-1));
            Assert.That(unknown, Is.EqualTo(new[] { "outwear:Outwear_404" }));
            CharacterAppearancePreset back = CharacterAppearanceSlotIndices.Decode(catalog, "sword", indices);
            Assert.That(back.TryGetItemId(CharacterAppearanceSlotCatalog.Hat, out string hat) && hat == "Hat_24", Is.True);
            Assert.That(back.TryGetItemId(CharacterAppearanceSlotCatalog.Outwear, out _), Is.False);
        }

        [Test]
        public void 코드_축복도_엑셀의_글자_파라미터를_받는다()
        {
            var definition = new BlessingDefinition("pocket-change-t1", "잔돈 회수", WeaponTag.Common, BlessingTier.Silver, "x");
            var raw = new BlessingMvpDefinition(definition, "pocket-change", 0, BlessingStackRule.Unique, BlessingDurationKind.Instant,
                new[] { "heal", "cooldown" }, new[] { 4f, 2f });
            var tuned = raw.WithNumbersAndTexts(new Dictionary<string, float> { ["heal"] = .03f },
                new Dictionary<string, string> { ["heal_basis"] = "max_health" });
            Assert.That(tuned.Number("heal"), Is.EqualTo(.03f));
            Assert.That(tuned.TryText("heal_basis", out string basis) && basis == "max_health", Is.True);
            Assert.That(tuned.Number("cooldown"), Is.EqualTo(2f), "엑셀에 없는 키는 코드 값을 지킨다");
        }
    }
}

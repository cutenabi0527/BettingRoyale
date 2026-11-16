using System;
using System.Collections.Generic;
using System.Globalization;
using BettingRoyale.Data.Blessing;
using BettingRoyale.Data.Chest;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.Online;
using BettingRoyale.Data.Preparation;
using BettingRoyale.Data.Remote;
using BettingRoyale.Data.Roulette;
using BettingRoyale.Data.Shop;
using BettingRoyale.Data.Throwing;
using BettingRoyale.Data.Upgrade;

namespace BettingRoyale.Data.Balance
{
    /// <summary>
    /// <c>gamedata.bin</c> v3 의 밸런스 표(<see cref="GameData.Tables"/>)와 상수·스탯을 도메인 카탈로그로 옮긴다
    /// (S-CLI-QXRTWX). <see cref="GameDataLoader"/> 가 한 번 부른다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>실패는 결과로 돌려준다.</b> 표가 없거나(v2 바이너리) 규칙에 어긋나면 <c>null</c> 을 돌려주고 이유를
    /// <paramref name="warnings"/> 에 남긴다 — 그러면 <see cref="BalanceCatalogs.Current"/> 가 코드 대역으로 떨어진다.
    /// 파이프라인이 같은 규칙(<see cref="BalanceTableSchema.Validate"/>)으로 먼저 막으므로 배포본이 여기서 떨어지는 일은
    /// 옛 캐시 파일뿐이어야 한다.
    /// </para>
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060).</para>
    /// </remarks>
    public static class BalanceCatalogBuilder
    {
        /// <summary>상점 게이트·스탯 시트가 장신구 기여를 식별하는 출처 이름. <see cref="ShopItemEffectCatalog.AccessorySourceId"/> 와 같다.</summary>
        private const string AccessorySourceId = ShopItemEffectCatalog.AccessorySourceId;

        public static BalanceCatalogs Build(GameData wire, StatRegistry stats, IReadOnlyDictionary<string, string> constants,
            List<string> warnings)
        {
            if (wire == null || constants == null) return null;
            BalanceTableRow[] tables = wire.Tables ?? Array.Empty<BalanceTableRow>();
            if (tables.Length == 0)
            {
                // v2 이하 바이너리는 표가 없는 것이 정상이다 — 경고하지 않는다(옛 캐시·테스트 페이로드).
                // v3 인데 표가 비었으면 파이프라인이 막았어야 할 상태라 알린다.
                if (wire.SchemaVersion >= 3)
                    warnings?.Add("밸런스 표가 없습니다 — 강화·아이템·상자·시설·룰렛은 코드 대역으로 돕니다.");
                return null;
            }

            var errors = new List<string>();
            var statIds = new HashSet<string>(StringComparer.Ordinal);
            if (stats != null) foreach (string id in stats.Ids) statIds.Add(id);
            BalanceTableSchema.Validate(tables, statIds, errors);
            if (errors.Count > 0)
            {
                warnings?.Add($"밸런스 표 검증 실패 {errors.Count}건 — 코드 대역으로 돕니다: {string.Join(" / ", errors)}");
                return null;
            }

            var bySheet = new Dictionary<string, List<BalanceTableRow>>(StringComparer.Ordinal);
            foreach (string sheet in BalanceTableSchema.Sheets) bySheet[sheet] = new List<BalanceTableRow>();
            foreach (BalanceTableRow row in tables) if (row != null && bySheet.TryGetValue(row.Sheet, out var list)) list.Add(row);

            try
            {
                var items = BuildItems(bySheet[BalanceTableSchema.Items], bySheet[BalanceTableSchema.Throwables], out ShopCatalog shop);
                var effects = BuildItemEffects(bySheet[BalanceTableSchema.ItemEffects]);
                var upgrades = BuildUpgrades(bySheet[BalanceTableSchema.Upgrades], bySheet[BalanceTableSchema.UpgradeLevels],
                    bySheet[BalanceTableSchema.UpgradeSets]);
                var chestLoot = BuildChestLoot(bySheet[BalanceTableSchema.ChestLoot], bySheet[BalanceTableSchema.ChestRarityWeights]);
                var chest = new ChestRules(
                    Const(constants, BalanceTableSchema.Constants.ChestOpenTime),
                    Const(constants, BalanceTableSchema.Constants.ChestRespawnDelay),
                    Const(constants, BalanceTableSchema.Constants.ChestRetryDelay),
                    ConstInt(constants, BalanceTableSchema.Constants.ChestRewardGold),
                    ConstInt(constants, BalanceTableSchema.Constants.ChestCount));
                bool allowOverlap = ConstBool(constants, BalanceTableSchema.Constants.FacilityAllowOverlap, false);
                var facilities = new FacilityBalance(BuildFacilities(bySheet[BalanceTableSchema.Facilities], allowOverlap), allowOverlap);
                var roulette = new RouletteBalance(
                    BuildRouletteLayout(bySheet[BalanceTableSchema.RouletteSlots]),
                    ConstInt(constants, BalanceTableSchema.Constants.RouletteBetSeconds),
                    ConstInt(constants, BalanceTableSchema.Constants.RouletteSpinSeconds),
                    ConstInt(constants, BalanceTableSchema.Constants.RouletteResultSeconds),
                    ConstInt(constants, BalanceTableSchema.Constants.RouletteMinBet),
                    ConstInt(constants, BalanceTableSchema.Constants.RouletteOpenMarginSeconds),
                    ConstInt(constants, BalanceTableSchema.Constants.RouletteStartDelaySeconds));
                var rounds = new OnlineCoreLoopRules(
                    ConstInt(constants, BalanceTableSchema.Constants.TotalRounds),
                    ConstInt(constants, BalanceTableSchema.Constants.PreparationSeconds),
                    ConstInt(constants, BalanceTableSchema.Constants.RoundDuration),
                    ConstInt(constants, BalanceTableSchema.Constants.SummarySeconds));
                var economy = new EconomyConfig(
                    ConstInt(constants, BalanceTableSchema.Constants.RoundGold),
                    CarryOverLimit.FromSetting(ConstInt(constants, BalanceTableSchema.Constants.CarryoverCap)));
                var scoring = new ScoringConfig(
                    ParseIntList(constants, BalanceTableSchema.Constants.ScoreSurvival),
                    ConstInt(constants, BalanceTableSchema.Constants.ScoreKill),
                    ConstInt(constants, BalanceTableSchema.Constants.ScoreAssist),
                    ConstInt(constants, BalanceTableSchema.Constants.ScoreAssist));
                var blessing = new BlessingConfig(
                    ConstInt(constants, BalanceTableSchema.Constants.CandidateCount),
                    ConstInt(constants, BalanceTableSchema.Constants.FreeLockCount),
                    new[]
                    {
                        ConstInt(constants, BalanceTableSchema.Constants.RerollCostR1),
                        ConstInt(constants, BalanceTableSchema.Constants.RerollCostR2),
                        ConstInt(constants, BalanceTableSchema.Constants.RerollCostR3),
                    },
                    new[] { BlessingTier.Silver, BlessingTier.Gold, BlessingTier.Platinum },
                    ConstInt(constants, BalanceTableSchema.Constants.RerollCostMultiplier));
                var zone = BuildZone(bySheet[BalanceTableSchema.ZonePhases],
                    Const(constants, BalanceTableSchema.Constants.ZoneAwarenessSeconds),
                    Const(constants, BalanceTableSchema.Constants.ZoneTickInterval));
                var weapons = BuildWeapons(wire.Weapons, warnings);
                var combatant = BuildCombatant(stats);
                bool armorPen = ConstBool(constants, BalanceTableSchema.Constants.ArmorPenEnabled, true);
                var disabled = ParseIdList(constants, BalanceTableSchema.Constants.DisabledBlessingIds);

                return new BalanceCatalogs(true, upgrades, shop, items, effects, chestLoot, chest, facilities, roulette,
                    rounds, economy, scoring, blessing, zone, weapons, combatant, armorPen, disabled);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException || ex is KeyNotFoundException)
            {
                warnings?.Add($"밸런스 카탈로그를 만들지 못했습니다 — 코드 대역으로 돕니다: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        // ── 상수 ──────────────────────────────────────────────────────────────
        private static float Const(IReadOnlyDictionary<string, string> constants, string key)
        {
            if (!constants.TryGetValue(key, out string raw) || !BalanceTableSchema.TryFloat(raw, out float value))
                throw new KeyNotFoundException($"constants.{key} 가 없거나 숫자가 아닙니다.");
            return value;
        }

        private static int ConstInt(IReadOnlyDictionary<string, string> constants, string key)
        {
            if (!constants.TryGetValue(key, out string raw) || !BalanceTableSchema.TryInt(raw, out int value))
                throw new KeyNotFoundException($"constants.{key} 가 없거나 정수가 아닙니다.");
            return value;
        }

        private static bool ConstBool(IReadOnlyDictionary<string, string> constants, string key, bool fallback) =>
            constants.TryGetValue(key, out string raw) && BalanceTableSchema.TryBool(raw, out bool value) ? value : fallback;

        private static int[] ParseIntList(IReadOnlyDictionary<string, string> constants, string key)
        {
            if (!constants.TryGetValue(key, out string raw)) throw new KeyNotFoundException($"constants.{key} 가 없습니다.");
            string[] parts = raw.Trim().Trim('[', ']').Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!BalanceTableSchema.TryInt(parts[i], out result[i])) throw new FormatException($"constants.{key} 의 '{parts[i]}' 가 정수가 아닙니다.");
            }

            return result;
        }

        private static IReadOnlyCollection<string> ParseIdList(IReadOnlyDictionary<string, string> constants, string key)
        {
            if (!constants.TryGetValue(key, out string raw) || string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
            var result = new List<string>();
            foreach (string part in raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string id = part.Trim();
                if (id.Length > 0 && !result.Contains(id)) result.Add(id);
            }

            return result;
        }

        private static float F(BalanceTableRow row, string col, float fallback = 0f) =>
            BalanceTableSchema.TryFloat(BalanceTableSchema.Cell(row, col), out float v) ? v : fallback;

        private static int I(BalanceTableRow row, string col, int fallback = 0) =>
            BalanceTableSchema.TryInt(BalanceTableSchema.Cell(row, col), out int v) ? v : fallback;

        private static string T(BalanceTableRow row, string col) => BalanceTableSchema.Cell(row, col).Trim();

        private static string L(BalanceTableRow row, string col) => T(row, col).ToLowerInvariant();

        private static bool B(BalanceTableRow row, string col, bool fallback = false) =>
            BalanceTableSchema.TryBool(BalanceTableSchema.Cell(row, col), out bool v) ? v : fallback;

        // ── 아이템 ────────────────────────────────────────────────────────────
        private static DataItemCatalog BuildItems(List<BalanceTableRow> itemRows, List<BalanceTableRow> throwableRows, out ShopCatalog shop)
        {
            var catalog = new DataItemCatalog();
            var offers = new List<ShopOffer>();
            var priceById = new Dictionary<int, int>();
            var carryById = new Dictionary<int, int>();
            foreach (BalanceTableRow row in itemRows)
            {
                int id = I(row, "item_id");
                string kind = L(row, "kind");
                EquipmentItemKind itemKind = kind == "accessory" ? EquipmentItemKind.Accessory : EquipmentItemKind.Consumable;
                int maxStack = I(row, "max_stack", 1);
                int carry = I(row, "carry_limit");
                bool purchasable = B(row, "purchasable");
                var definition = new EquipmentItemDefinition(id, itemKind, EquipmentArchetype.None, I(row, "visual_id"), WeaponType.None, maxStack);
                catalog.AddItem(definition, T(row, "name"), carry, onShelf: purchasable && kind != "throwable");
                carryById[id] = carry;
                if (purchasable)
                {
                    int price = I(row, "price");
                    priceById[id] = price;
                    offers.Add(new ShopOffer(I(row, "offer_id"), id, price, Math.Max(1, I(row, "quantity", 1))));
                }
            }

            offers.Sort((a, b) => a.OfferId.CompareTo(b.OfferId));
            shop = new ShopCatalog(offers);

            foreach (BalanceTableRow row in throwableRows)
            {
                int id = I(row, "item_id");
                priceById.TryGetValue(id, out int price);
                carryById.TryGetValue(id, out int carry);
                double warning = F(row, "warning_seconds");
                catalog.AddThrowable(new ThrowingItemDefinition(
                    id,
                    catalog.NameOf(id),
                    F(row, "throw_distance"),
                    (int)Math.Round(F(row, "damage")),
                    F(row, "explosion_radius"),
                    price,
                    Math.Max(1, I(row, "drop_quantity", 1)),
                    Math.Max(1, carry),
                    F(row, "flight_seconds"),
                    warning,
                    F(row, "shared_cooldown_seconds"),
                    RecommendedMagicBottleCatalog.MagicBottleGuideColor,
                    RecommendedMagicBottleCatalog.MagicBottleFlightGuideColor,
                    RecommendedMagicBottleCatalog.MagicBottleImpactOverlayColor,
                    ThrowActivationVfx.MagicExplosion,
                    warning,
                    F(row, "activation_duration_seconds"),
                    ThrowVisibilityPolicy.OwnerAndAllies));
            }

            return catalog;
        }

        private static ConsumableEffectTable BuildItemEffects(List<BalanceTableRow> rows)
        {
            var table = new ConsumableEffectTable();
            foreach (BalanceTableRow row in rows)
            {
                int id = I(row, "item_id");
                string kind = L(row, "effect_kind");
                float value = F(row, "value");
                float seconds = F(row, "duration_seconds");
                bool ratio = L(row, "basis") == "max_health";
                switch (kind)
                {
                    case "instant_heal":
                        table.AddEffect(id, ratio ? ConsumableEffect.InstantHealRatio(value) : ConsumableEffect.InstantHeal(value));
                        break;
                    case "timed_move_speed": table.AddEffect(id, ConsumableEffect.TimedMoveSpeed(value, seconds)); break;
                    case "timed_attack_speed": table.AddEffect(id, ConsumableEffect.TimedAttackSpeed(value, seconds)); break;
                    case "timed_damage_bonus": table.AddEffect(id, ConsumableEffect.TimedDamageBonus(value, seconds)); break;
                    case "timed_damage_taken": table.AddEffect(id, ConsumableEffect.TimedDamageTaken(value, seconds)); break;
                    case "shield": table.AddEffect(id, ConsumableEffect.Shield(value, seconds)); break;
                    case "invulnerable": table.AddEffect(id, ConsumableEffect.Invulnerable(seconds)); break;
                    case "dash_refill": table.AddEffect(id, ConsumableEffect.DashRefill()); break;
                    case "timed_dash_recharge": table.AddEffect(id, ConsumableEffect.TimedDashRecharge(value, seconds)); break;
                    case "regeneration":
                        table.AddEffect(id, ratio ? ConsumableEffect.RegenerationRatio(value, seconds) : ConsumableEffect.Regeneration(value, seconds));
                        break;
                    case "timed_defense": table.AddEffect(id, ConsumableEffect.TimedDefense(value, seconds)); break;
                    case "timed_attack_power": table.AddEffect(id, ConsumableEffect.TimedAttackPower(value, seconds)); break;
                    case "stat":
                        table.AddAccessoryStat(id, new ConsumableEffectTable.AccessoryStat(T(row, "stat_id"), value, L(row, "weapon_filter")));
                        break;
                    default:
                        throw new FormatException($"item_effects({id}): 모르는 effect_kind '{kind}'");
                }
            }

            return table;
        }

        // ── 강화 ──────────────────────────────────────────────────────────────
        private static UpgradeCatalog BuildUpgrades(List<BalanceTableRow> rows, List<BalanceTableRow> levelRows, List<BalanceTableRow> setRows)
        {
            // track/weapon → level → stats
            var statsByTrack = new Dictionary<string, SortedDictionary<int, List<StatContribution>>>(StringComparer.Ordinal);
            foreach (BalanceTableRow row in rows)
            {
                string key = L(row, "track") + "/" + L(row, "weapon");
                if (!statsByTrack.TryGetValue(key, out var levels)) statsByTrack[key] = levels = new SortedDictionary<int, List<StatContribution>>();
                int level = I(row, "level");
                if (!levels.TryGetValue(level, out var stats)) levels[level] = stats = new List<StatContribution>();
                stats.Add(new StatContribution(T(row, "stat_id"), F(row, "value"), StatStackMode.Add));
            }

            // 비용과 성공률(0~1 → 0~100%)은 upgrade_levels 한 줄에 같이 있다 (S15P21A606-304).
            var costs = new Dictionary<string, Dictionary<int, (int Cost, int SuccessRatePercent)>>(StringComparer.Ordinal);
            foreach (BalanceTableRow row in levelRows)
            {
                string key = L(row, "track") + "/" + L(row, "weapon");
                if (!costs.TryGetValue(key, out var byLevel)) costs[key] = byLevel = new Dictionary<int, (int, int)>();
                byLevel[I(row, "level")] = (I(row, "cost"), UpgradeLevel.PercentFromRatio(F(row, "success_rate")));
            }

            UpgradeTrack Track(string key)
            {
                if (!statsByTrack.TryGetValue(key, out var levels)) throw new KeyNotFoundException($"upgrades: '{key}' 표가 없습니다.");
                var list = new List<UpgradeLevel>(levels.Count);
                foreach (var pair in levels)
                {
                    if (!costs.TryGetValue(key, out var byLevel) || !byLevel.TryGetValue(pair.Key, out var levelRow))
                        throw new KeyNotFoundException($"upgrade_levels: '{key}' {pair.Key}강 비용이 없습니다.");
                    list.Add(new UpgradeLevel(pair.Key, levelRow.Cost, pair.Value, levelRow.SuccessRatePercent));
                }

                return new UpgradeTrack(list);
            }

            var weapons = new Dictionary<WeaponType, UpgradeTrack>();
            foreach (string key in statsByTrack.Keys)
            {
                if (!key.StartsWith("weapon/", StringComparison.Ordinal)) continue;
                string weaponName = key.Substring("weapon/".Length);
                if (!UpgradeCatalog.TryParseWeapon(weaponName, out WeaponType weapon))
                    throw new FormatException($"upgrades: 모르는 무기 '{weaponName}'");
                weapons[weapon] = Track(key);
            }

            UpgradeTrack armor = Track("armor/");

            var setsByThreshold = new SortedDictionary<int, (List<StatContribution> Stats, List<WeaponType> Filters)>();
            foreach (BalanceTableRow row in setRows)
            {
                int threshold = I(row, "threshold");
                if (!setsByThreshold.TryGetValue(threshold, out var set))
                    setsByThreshold[threshold] = set = (new List<StatContribution>(), new List<WeaponType>());
                string filter = L(row, "weapon_filter");
                WeaponType weaponFilter = WeaponType.None;
                if (filter.Length > 0 && !UpgradeCatalog.TryParseWeapon(filter, out weaponFilter))
                    throw new FormatException($"upgrade_sets: 모르는 weapon_filter '{filter}'");
                set.Stats.Add(new StatContribution(T(row, "stat_id"), F(row, "increment"), StatStackMode.Add));
                set.Filters.Add(weaponFilter);
            }

            var sets = new List<UpgradeSetBonus>(setsByThreshold.Count);
            foreach (var pair in setsByThreshold) sets.Add(new UpgradeSetBonus(pair.Key, pair.Value.Stats, pair.Value.Filters));
            return new UpgradeCatalog(weapons, armor, sets);
        }

        // ── 상자 ──────────────────────────────────────────────────────────────
        private static ChestLootTable BuildChestLoot(List<BalanceTableRow> lootRows, List<BalanceTableRow> weightRows)
        {
            var entries = new List<ChestLootEntry>(lootRows.Count);
            foreach (BalanceTableRow row in lootRows)
            {
                ChestItemRarity rarity = L(row, "rarity") switch
                {
                    "common" => ChestItemRarity.Common,
                    "rare" => ChestItemRarity.Rare,
                    "epic" => ChestItemRarity.Epic,
                    _ => throw new FormatException($"chest_loot: 모르는 rarity '{T(row, "rarity")}'"),
                };
                entries.Add(new ChestLootEntry(I(row, "item_id"), rarity));
            }

            ChestRarityWeights regular = default, final = default;
            bool hasRegular = false, hasFinal = false;
            foreach (BalanceTableRow row in weightRows)
            {
                var weights = new ChestRarityWeights(I(row, "common"), I(row, "rare"), I(row, "epic"));
                if (L(row, "round_group") == "final") { final = weights; hasFinal = true; }
                else { regular = weights; hasRegular = true; }
            }

            if (!hasRegular || !hasFinal) throw new KeyNotFoundException("chest_rarity_weights: regular·final 두 행이 필요합니다.");
            return new ChestLootTable(entries, regular, final);
        }

        // ── 시설 ──────────────────────────────────────────────────────────────
        private static PreparationFacilityMap BuildFacilities(List<BalanceTableRow> rows, bool allowOverlap)
        {
            var areas = new List<PreparationFacilityArea>(rows.Count);
            foreach (BalanceTableRow row in rows)
            {
                // 맵 키는 지금 하나(default)다. 맵별 배치가 생기면 카탈로그가 키로 고른다.
                PreparationFacility facility = L(row, "facility") switch
                {
                    "roulette" => PreparationFacility.Roulette,
                    "shop" => PreparationFacility.Shop,
                    "upgrade" => PreparationFacility.Upgrade,
                    _ => throw new FormatException($"facilities: 모르는 facility '{T(row, "facility")}'"),
                };
                areas.Add(new PreparationFacilityArea(facility,
                    new WorldPosition(F(row, "x"), F(row, "y"), F(row, "z")), F(row, "radius")));
            }

            return new PreparationFacilityMap(areas, allowOverlap);
        }

        private static IReadOnlyList<int> BuildRouletteLayout(List<BalanceTableRow> rows)
        {
            var sorted = new SortedDictionary<int, int>();
            foreach (BalanceTableRow row in rows) sorted[I(row, "slot_index")] = I(row, "number");
            var layout = new List<int>(sorted.Count);
            foreach (var pair in sorted) layout.Add(pair.Value);
            return layout;
        }

        private static CombatZoneSettings BuildZone(List<BalanceTableRow> rows, float awarenessSeconds, float tickIntervalSeconds)
        {
            var sorted = new SortedDictionary<int, CombatZoneStageSettings>();
            foreach (BalanceTableRow row in rows)
            {
                sorted[I(row, "stage")] = new CombatZoneStageSettings(
                    F(row, "start_seconds"), F(row, "shrink_seconds"), F(row, "target_area_ratio"),
                    F(row, "damage_per_second"), F(row, "first_tick_delay_seconds"));
            }

            var stages = new List<CombatZoneStageSettings>(sorted.Count);
            foreach (var pair in sorted) stages.Add(pair.Value);
            return new CombatZoneSettings(awarenessSeconds, tickIntervalSeconds, stages);
        }

        // ── 무기·전투원 ───────────────────────────────────────────────────────
        private static WeaponBalance BuildWeapons(WeaponRow[] rows, List<string> warnings)
        {
            if (rows == null || rows.Length == 0) return null;
            var balance = new WeaponBalance();
            foreach (WeaponRow row in rows)
            {
                if (row == null || !UpgradeCatalog.TryParseWeapon(row.WeaponId, out WeaponType weapon))
                {
                    warnings?.Add($"weapons: 모르는 무기 '{row?.WeaponId}' — 건너뜁니다.");
                    continue;
                }

                if (row.Damage <= 0 || row.RangeMeters <= 0f || row.CooldownSeconds <= 0f)
                {
                    warnings?.Add($"weapons({row.WeaponId}): 피해·사거리·쿨타임이 양수가 아니어서 코드 값을 씁니다.");
                    continue;
                }

                balance.Set(weapon,
                    new WeaponConfig(row.Damage, row.RangeMeters, row.CooldownSeconds, row.ProjectileSpeedMps,
                        Math.Max(0f, row.CollisionRadiusMeters)),
                    row.AttackSlowSeconds > 0f && row.AttackSlowMultiplier > 0f && row.AttackSlowMultiplier < 1f
                        ? new WeaponAttackSlow(row.AttackSlowMultiplier, row.AttackSlowSeconds)
                        : WeaponAttackSlow.None);
            }

            return balance.Count > 0 ? balance : null;
        }

        private static CombatantBalance BuildCombatant(StatRegistry stats)
        {
            if (stats == null) return null;
            int charges = stats.TryGet("dash-charges", out StatDefinition c) && c.BaseValue > 0f ? (int)Math.Round(c.BaseValue) : 0;
            float recharge = stats.TryGet("dash-recharge", out StatDefinition r) && r.BaseValue > 0f ? r.BaseValue : 0f;
            float duration = stats.TryGet("dash-duration", out StatDefinition d) && d.BaseValue > 0f ? d.BaseValue : 0f;
            return charges > 0 || recharge > 0f || duration > 0f ? new CombatantBalance(charges, recharge, duration) : null;
        }
    }
}

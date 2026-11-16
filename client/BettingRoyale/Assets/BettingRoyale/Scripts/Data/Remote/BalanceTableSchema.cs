using System;
using System.Collections.Generic;
using System.Globalization;

namespace BettingRoyale.Data.Remote
{
    /// <summary>
    /// 밸런스 시트(<see cref="BalanceTableRow"/>)의 <b>시트 이름·열 이름·값 규칙</b> 정본
    /// (S15P21A606-274, S-CLI-QXRTWX).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 파이프라인(<c>data/tools/DataContracts</c>)이 이 파일을 링크해 빌드 시점에 같은 규칙으로 검증하고,
    /// 클라이언트 로더는 같은 규칙으로 표를 도메인 카탈로그로 옮긴다. 열 이름이 계약이다 — 위치가 아니다.
    /// </para>
    /// <para>
    /// <see cref="UnityEngine"/> 과 다른 도메인 타입을 참조하지 않는다. 그래야 <c>net8.0</c> 파이프라인에서
    /// 그대로 컴파일된다 (D-060).
    /// </para>
    /// </remarks>
    public static class BalanceTableSchema
    {
        // ── 시트 이름 (소문자) ────────────────────────────────────────────────
        public const string Upgrades = "upgrades";
        public const string UpgradeLevels = "upgrade_levels";
        public const string UpgradeSets = "upgrade_sets";
        public const string Items = "items";
        public const string ItemEffects = "item_effects";
        public const string Throwables = "throwables";
        public const string ChestLoot = "chest_loot";
        public const string ChestRarityWeights = "chest_rarity_weights";
        public const string Facilities = "facilities";
        public const string RouletteSlots = "roulette_slots";
        public const string ZonePhases = "zone_phases";

        /// <summary>파이프라인이 <see cref="BalanceTableRow"/> 로 싣는 시트 전부.</summary>
        public static readonly IReadOnlyList<string> Sheets = new[]
        {
            Upgrades, UpgradeLevels, UpgradeSets, Items, ItemEffects, Throwables,
            ChestLoot, ChestRarityWeights, Facilities, RouletteSlots, ZonePhases,
        };

        /// <summary>시트별 필수 열.</summary>
        public static IReadOnlyList<string> RequiredColumns(string sheet) => sheet switch
        {
            Upgrades => new[] { "track", "weapon", "level", "stat_id", "value" },
            UpgradeLevels => new[] { "track", "weapon", "level", "cost", "success_rate" },
            UpgradeSets => new[] { "threshold", "weapon_filter", "stat_id", "increment" },
            Items => new[] { "item_id", "name", "kind", "visual_id", "max_stack", "carry_limit", "purchasable", "offer_id", "price", "quantity" },
            ItemEffects => new[] { "item_id", "effect_kind", "stat_id", "value", "duration_seconds", "basis", "weapon_filter" },
            Throwables => new[] { "item_id", "damage", "throw_distance", "explosion_radius", "flight_seconds", "warning_seconds", "shared_cooldown_seconds", "activation_duration_seconds", "drop_quantity" },
            ChestLoot => new[] { "entry_id", "item_id", "rarity" },
            ChestRarityWeights => new[] { "round_group", "common", "rare", "epic" },
            Facilities => new[] { "map_key", "facility", "x", "y", "z", "radius" },
            RouletteSlots => new[] { "slot_index", "number" },
            ZonePhases => new[] { "stage", "start_seconds", "shrink_seconds", "target_area_ratio", "damage_per_second", "first_tick_delay_seconds" },
            _ => Array.Empty<string>(),
        };

        // ── 열 값 집합 ────────────────────────────────────────────────────────
        public static readonly string[] UpgradeTracks = { "weapon", "armor" };
        public static readonly string[] WeaponFilters = { "", "sword", "bow" };
        public static readonly string[] ItemKinds = { "consumable", "accessory", "throwable" };
        public static readonly string[] EffectKinds =
        {
            "instant_heal", "timed_move_speed", "timed_attack_speed", "timed_damage_bonus", "timed_damage_taken",
            "shield", "invulnerable", "dash_refill", "timed_dash_recharge", "regeneration", "timed_defense",
            "timed_attack_power", "stat",
        };
        public static readonly string[] HealBases = { "flat", "max_health" };
        public static readonly string[] Rarities = { "common", "rare", "epic" };
        public static readonly string[] RoundGroups = { "regular", "final" };
        public static readonly string[] FacilityNames = { "roulette", "shop", "upgrade" };

        /// <summary>상자 과잉 보상 효과가 쓰는 아이템 번호 — 아이템이 아니라 음수다 (S-UX-XEC4CX).</summary>
        public const int ChestOverflowItemId = -1;

        // ── 상수 키 (constants 시트) ─────────────────────────────────────────
        public static class Constants
        {
            public const string ArmorPenEnabled = "ARMOR_PEN_ENABLED";
            public const string DisabledBlessingIds = "DISABLED_BLESSING_IDS";
            public const string FacilityAllowOverlap = "FACILITY_ALLOW_OVERLAP";
            public const string ChestOpenTime = "CHEST_OPEN_TIME";
            public const string ChestRewardGold = "CHEST_REWARD_GOLD";
            public const string ChestRetryDelay = "CHEST_RETRY_DELAY";
            public const string ChestRespawnDelay = "CHEST_RESPAWN_DELAY";
            public const string ChestCount = "CHEST_COUNT";
            public const string RouletteBetSeconds = "ROULETTE_BET_SECONDS";
            public const string RouletteSpinSeconds = "ROULETTE_SPIN_SECONDS";
            public const string RouletteResultSeconds = "ROULETTE_RESULT_SECONDS";
            public const string RouletteMinBet = "ROULETTE_MIN_BET";
            public const string RouletteOpenMarginSeconds = "ROULETTE_OPEN_MARGIN_SECONDS";
            public const string RouletteStartDelaySeconds = "ROULETTE_START_DELAY_SECONDS";
            public const string TotalRounds = "TOTAL_ROUNDS";
            public const string PreparationSeconds = "PREPARATION_SECONDS";
            public const string RoundDuration = "ROUND_DURATION";
            public const string SummarySeconds = "SUMMARY_SECONDS";
            public const string RoundGold = "ROUND_GOLD";
            public const string CarryoverCap = "CARRYOVER_CAP";
            public const string ScoreSurvival = "SCORE_SURVIVAL";
            public const string ScoreKill = "SCORE_KILL";
            public const string ScoreAssist = "SCORE_ASSIST";
            public const string CandidateCount = "CANDIDATE_COUNT";
            public const string FreeLockCount = "FREE_LOCK_COUNT";
            public const string RerollCostR1 = "REROLL_COST_R1";
            public const string RerollCostR2 = "REROLL_COST_R2";
            public const string RerollCostR3 = "REROLL_COST_R3";
            public const string RerollCostMultiplier = "REROLL_COST_MULTIPLIER";
            public const string ZoneAwarenessSeconds = "ZONE_AWARENESS_SECONDS";
            public const string ZoneTickInterval = "ZONE_TICK_INTERVAL";

            /// <summary>밸런스 소비자가 숫자로 읽는 상수 전부. 파이프라인이 값 존재·0 이상을 검사한다.</summary>
            public static readonly IReadOnlyList<string> NumericKeys = new[]
            {
                ChestOpenTime, ChestRewardGold, ChestRetryDelay, ChestRespawnDelay, ChestCount,
                RouletteBetSeconds, RouletteSpinSeconds, RouletteResultSeconds, RouletteMinBet,
                RouletteOpenMarginSeconds, RouletteStartDelaySeconds,
                TotalRounds, PreparationSeconds, RoundDuration, SummarySeconds, RoundGold, CarryoverCap,
                ScoreKill, ScoreAssist, CandidateCount, FreeLockCount,
                RerollCostR1, RerollCostR2, RerollCostR3, RerollCostMultiplier,
                ZoneAwarenessSeconds, ZoneTickInterval,
            };
        }

        // ── 셀 파싱 ───────────────────────────────────────────────────────────
        public static string Cell(BalanceTableRow row, string column)
        {
            if (row?.Cells == null) return string.Empty;
            for (int i = 0; i < row.Cells.Length; i++)
            {
                KeyValueRow cell = row.Cells[i];
                if (cell != null && string.Equals(cell.Key, column, StringComparison.OrdinalIgnoreCase))
                    return cell.Value ?? string.Empty;
            }

            return string.Empty;
        }

        public static bool Has(BalanceTableRow row, string column) => Cell(row, column).Trim().Length > 0;

        public static bool TryFloat(string raw, out float value)
        {
            value = 0f;
            if (raw == null) return false;
            raw = raw.Trim();
            if (raw.Length == 0) return false;
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public static bool TryInt(string raw, out int value)
        {
            value = 0;
            if (!TryFloat(raw, out float f)) return false;
            if (Math.Abs(f - Math.Round(f)) > 1e-6) return false;
            value = (int)Math.Round(f);
            return true;
        }

        /// <summary>TRUE/FALSE/1/0/yes/no. 빈 칸은 <c>false</c> 가 아니라 "없음" — 호출자가 기본값을 정한다.</summary>
        public static bool TryBool(string raw, out bool value)
        {
            value = false;
            if (raw == null) return false;
            switch (raw.Trim().ToLowerInvariant())
            {
                case "true": case "1": case "yes": case "y": value = true; return true;
                case "false": case "0": case "no": case "n": value = false; return true;
                default: return false;
            }
        }

        // ── 검증 — 파이프라인과 클라이언트가 같은 함수를 부른다 ─────────────────
        /// <summary>
        /// 시트 전체를 검증한다. 실패는 전부 모아 <paramref name="errors"/> 에 넣는다 — 첫 건에서 멈추지 않는다.
        /// </summary>
        /// <param name="statIds">stats 시트의 id 집합. 스탯을 가리키는 열의 FK 검사에 쓴다.</param>
        public static void Validate(IReadOnlyList<BalanceTableRow> rows, ICollection<string> statIds, List<string> errors)
        {
            if (rows == null || errors == null) return;
            var bySheet = new Dictionary<string, List<BalanceTableRow>>(StringComparer.Ordinal);
            foreach (string sheet in Sheets) bySheet[sheet] = new List<BalanceTableRow>();
            foreach (BalanceTableRow row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.Sheet)) { errors.Add("balance: 시트 이름이 빈 행"); continue; }
                if (!bySheet.TryGetValue(row.Sheet, out var list)) { errors.Add($"balance: 모르는 시트 '{row.Sheet}'"); continue; }
                list.Add(row);
            }

            foreach (string sheet in Sheets)
            {
                if (bySheet[sheet].Count == 0) errors.Add($"{sheet}: 행이 0개 — 빈 표는 배포하지 않는다");
                foreach (BalanceTableRow row in bySheet[sheet])
                    foreach (string column in RequiredColumns(sheet))
                        if (!HasColumn(row, column)) errors.Add($"{sheet}: 필수 열 없음 — {column}");
            }

            var itemIds = new HashSet<int>();
            var itemKinds = new Dictionary<int, string>();
            ValidateItems(bySheet[Items], itemIds, itemKinds, errors);
            ValidateItemEffects(bySheet[ItemEffects], itemIds, statIds, errors);
            ValidateThrowables(bySheet[Throwables], itemKinds, errors);
            ValidateUpgrades(bySheet[Upgrades], bySheet[UpgradeLevels], statIds, errors);
            ValidateUpgradeSets(bySheet[UpgradeSets], statIds, errors);
            ValidateChest(bySheet[ChestLoot], bySheet[ChestRarityWeights], itemIds, errors);
            ValidateFacilities(bySheet[Facilities], errors);
            ValidateRoulette(bySheet[RouletteSlots], errors);
            ValidateZone(bySheet[ZonePhases], errors);
        }

        private static bool HasColumn(BalanceTableRow row, string column)
        {
            if (row?.Cells == null) return false;
            for (int i = 0; i < row.Cells.Length; i++)
                if (row.Cells[i] != null && string.Equals(row.Cells[i].Key, column, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static bool In(string value, string[] allowed)
        {
            value = (value ?? string.Empty).Trim().ToLowerInvariant();
            for (int i = 0; i < allowed.Length; i++) if (allowed[i] == value) return true;
            return false;
        }

        private static float Num(BalanceTableRow row, string col, string at, List<string> errors, float min = float.NegativeInfinity, bool required = true)
        {
            string raw = Cell(row, col);
            if (raw.Trim().Length == 0)
            {
                if (required) errors.Add($"{at}: '{col}' 이 비어 있음");
                return 0f;
            }
            if (!TryFloat(raw, out float v)) { errors.Add($"{at}: '{col}' 값 '{raw}' 이 숫자가 아님"); return 0f; }
            if (v < min) errors.Add($"{at}: '{col}' 은 {min} 이상이어야 함 (실제 {raw})");
            return v;
        }

        private static int Int(BalanceTableRow row, string col, string at, List<string> errors, int min = int.MinValue, bool required = true)
        {
            string raw = Cell(row, col);
            if (raw.Trim().Length == 0)
            {
                if (required) errors.Add($"{at}: '{col}' 이 비어 있음");
                return 0;
            }
            if (!TryInt(raw, out int v)) { errors.Add($"{at}: '{col}' 값 '{raw}' 이 정수가 아님"); return 0; }
            if (v < min) errors.Add($"{at}: '{col}' 은 {min} 이상이어야 함 (실제 {raw})");
            return v;
        }

        private static void ValidateItems(List<BalanceTableRow> rows, HashSet<int> ids, Dictionary<int, string> kinds, List<string> errors)
        {
            var offers = new HashSet<int>();
            foreach (BalanceTableRow row in rows)
            {
                int id = Int(row, "item_id", Items, errors, 1);
                string at = $"{Items}({id})";
                if (!ids.Add(id)) errors.Add($"{at}: item_id 중복");
                string kind = Cell(row, "kind").Trim().ToLowerInvariant();
                if (!In(kind, ItemKinds)) errors.Add($"{at}: kind '{kind}' 는 {string.Join("·", ItemKinds)} 중 하나여야 함");
                kinds[id] = kind;
                if (Cell(row, "name").Trim().Length == 0) errors.Add($"{at}: name 비어 있음");
                Int(row, "visual_id", at, errors, 1);
                int maxStack = Int(row, "max_stack", at, errors, 1);
                int carry = Int(row, "carry_limit", at, errors, 0);
                if (kind == "consumable" && carry < 1) errors.Add($"{at}: 소모품 carry_limit 은 1 이상이어야 함");
                if (kind == "consumable" && carry < maxStack) errors.Add($"{at}: carry_limit({carry}) 이 max_stack({maxStack}) 보다 작을 수 없음");
                if (!TryBool(Cell(row, "purchasable"), out bool purchasable)) errors.Add($"{at}: purchasable 은 TRUE/FALSE 여야 함");
                if (purchasable)
                {
                    int offer = Int(row, "offer_id", at, errors, 1);
                    if (!offers.Add(offer)) errors.Add($"{at}: offer_id {offer} 중복");
                    Int(row, "price", at, errors, 0);
                    Int(row, "quantity", at, errors, 1);
                }
            }
        }

        private static void ValidateItemEffects(List<BalanceTableRow> rows, HashSet<int> itemIds, ICollection<string> statIds, List<string> errors)
        {
            foreach (BalanceTableRow row in rows)
            {
                int id = Int(row, "item_id", ItemEffects, errors);
                string at = $"{ItemEffects}({id})";
                if (id != ChestOverflowItemId && !itemIds.Contains(id)) errors.Add($"{at}: items 에 없는 item_id");
                string kind = Cell(row, "effect_kind").Trim().ToLowerInvariant();
                if (!In(kind, EffectKinds)) { errors.Add($"{at}: effect_kind '{kind}' 는 허용 목록 밖"); continue; }
                string basis = Cell(row, "basis").Trim().ToLowerInvariant();
                if (basis.Length > 0 && !In(basis, HealBases)) errors.Add($"{at}: basis '{basis}' 는 flat|max_health 여야 함");
                float value = Num(row, "value", at, errors, required: kind != "dash_refill" && kind != "invulnerable");
                float duration = Num(row, "duration_seconds", at, errors, 0f, required: false);
                bool timed = kind.StartsWith("timed_", StringComparison.Ordinal) || kind == "shield" || kind == "invulnerable" || kind == "regeneration";
                if (timed && duration <= 0f) errors.Add($"{at}: {kind} 은 duration_seconds 가 양수여야 함");
                if ((kind == "instant_heal" || kind == "regeneration") && basis == "max_health" && (value <= 0f || value > 1f))
                    errors.Add($"{at}: 최대 체력 비율은 0 초과 1 이하여야 함 (실제 {value}) — 20 과 0.20 을 섞지 않는다");
                if ((kind == "instant_heal" || kind == "regeneration") && basis != "max_health" && value > 0f && value < 1f)
                    errors.Add($"{at}: 고정 회복량 {value} 은 1 미만 — 비율이면 basis=max_health 를 적을 것");
                if (kind == "stat")
                {
                    string stat = Cell(row, "stat_id").Trim();
                    if (stat.Length == 0 || (statIds != null && !statIds.Contains(stat))) errors.Add($"{at}: stat_id '{stat}' 이 stats 에 없음");
                }
                string filter = Cell(row, "weapon_filter").Trim().ToLowerInvariant();
                if (filter.Length > 0 && filter != "melee" && filter != "ranged" && !In(filter, WeaponFilters))
                    errors.Add($"{at}: weapon_filter '{filter}' 는 melee|ranged|sword|bow|빈칸 이어야 함");
            }
        }

        private static void ValidateThrowables(List<BalanceTableRow> rows, Dictionary<int, string> kinds, List<string> errors)
        {
            foreach (BalanceTableRow row in rows)
            {
                int id = Int(row, "item_id", Throwables, errors, 1);
                string at = $"{Throwables}({id})";
                if (!kinds.ContainsKey(id) || kinds[id] != "throwable") errors.Add($"{at}: items 의 kind 가 throwable 이 아니거나 없음");
                Num(row, "damage", at, errors, 0f);
                Num(row, "throw_distance", at, errors, 0.01f);
                Num(row, "explosion_radius", at, errors, 0.01f);
                Num(row, "flight_seconds", at, errors, 0.01f);
                Num(row, "warning_seconds", at, errors, 0f);
                Num(row, "shared_cooldown_seconds", at, errors, 0f);
                Num(row, "activation_duration_seconds", at, errors, 0f);
                Int(row, "drop_quantity", at, errors, 1);
            }
        }

        private static void ValidateUpgrades(List<BalanceTableRow> rows, List<BalanceTableRow> levels, ICollection<string> statIds, List<string> errors)
        {
            var levelsByTrack = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            var costsByTrack = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            foreach (BalanceTableRow row in rows)
            {
                string track = Cell(row, "track").Trim().ToLowerInvariant();
                string weapon = Cell(row, "weapon").Trim().ToLowerInvariant();
                int level = Int(row, "level", Upgrades, errors, 1);
                string at = $"{Upgrades}({track}/{weapon}/{level})";
                if (!In(track, UpgradeTracks)) errors.Add($"{at}: track 은 weapon|armor");
                if (track == "weapon" && (weapon.Length == 0 || !In(weapon, WeaponFilters))) errors.Add($"{at}: weapon 표는 weapon 이 sword|bow 여야 함");
                if (track == "armor" && weapon.Length > 0) errors.Add($"{at}: armor 표는 weapon 이 비어야 함");
                string stat = Cell(row, "stat_id").Trim();
                if (stat.Length == 0 || (statIds != null && !statIds.Contains(stat))) errors.Add($"{at}: stat_id '{stat}' 이 stats 에 없음");
                Num(row, "value", at, errors);
                string key = track + "/" + weapon;
                if (!levelsByTrack.TryGetValue(key, out var set)) levelsByTrack[key] = set = new HashSet<int>();
                set.Add(level);
            }
            foreach (BalanceTableRow row in levels)
            {
                string track = Cell(row, "track").Trim().ToLowerInvariant();
                string weapon = Cell(row, "weapon").Trim().ToLowerInvariant();
                int level = Int(row, "level", UpgradeLevels, errors, 1);
                string at = $"{UpgradeLevels}({track}/{weapon}/{level})";
                Int(row, "cost", at, errors, 0);
                float rate = Num(row, "success_rate", at, errors, 0f);
                if (rate > 1f) errors.Add($"{at}: success_rate 는 0~1 비율 (실제 {rate})");
                string key = track + "/" + weapon;
                if (!costsByTrack.TryGetValue(key, out var set)) costsByTrack[key] = set = new HashSet<int>();
                if (!set.Add(level)) errors.Add($"{at}: 단계 중복");
            }
            foreach (var pair in levelsByTrack)
            {
                int max = 0;
                foreach (int l in pair.Value) if (l > max) max = l;
                for (int l = 1; l <= max; l++)
                    if (!pair.Value.Contains(l)) errors.Add($"{Upgrades}({pair.Key}): {l}강 행이 없음 — 단계에 구멍이 있으면 그 위 단계가 조용히 사라진다");
                if (!costsByTrack.TryGetValue(pair.Key, out var costs)) { errors.Add($"{UpgradeLevels}({pair.Key}): 비용 표가 없음"); continue; }
                for (int l = 1; l <= max; l++)
                    if (!costs.Contains(l)) errors.Add($"{UpgradeLevels}({pair.Key}): {l}강 비용이 없음");
            }
        }

        private static void ValidateUpgradeSets(List<BalanceTableRow> rows, ICollection<string> statIds, List<string> errors)
        {
            foreach (BalanceTableRow row in rows)
            {
                int threshold = Int(row, "threshold", UpgradeSets, errors, 1);
                string filter = Cell(row, "weapon_filter").Trim().ToLowerInvariant();
                string at = $"{UpgradeSets}({threshold}/{filter})";
                if (!In(filter, WeaponFilters)) errors.Add($"{at}: weapon_filter 는 sword|bow|빈칸");
                string stat = Cell(row, "stat_id").Trim();
                if (stat.Length == 0 || (statIds != null && !statIds.Contains(stat))) errors.Add($"{at}: stat_id '{stat}' 이 stats 에 없음");
                Num(row, "increment", at, errors);
            }
        }

        private static void ValidateChest(List<BalanceTableRow> loot, List<BalanceTableRow> weights, HashSet<int> itemIds, List<string> errors)
        {
            var entries = new HashSet<int>();
            foreach (BalanceTableRow row in loot)
            {
                int entry = Int(row, "entry_id", ChestLoot, errors, 1);
                string at = $"{ChestLoot}({entry})";
                if (!entries.Add(entry)) errors.Add($"{at}: entry_id 중복");
                int item = Int(row, "item_id", at, errors, 1);
                if (!itemIds.Contains(item)) errors.Add($"{at}: items 에 없는 item_id {item}");
                if (!In(Cell(row, "rarity"), Rarities)) errors.Add($"{at}: rarity 는 common|rare|epic");
            }
            var groups = new HashSet<string>();
            foreach (BalanceTableRow row in weights)
            {
                string group = Cell(row, "round_group").Trim().ToLowerInvariant();
                string at = $"{ChestRarityWeights}({group})";
                if (!In(group, RoundGroups)) errors.Add($"{at}: round_group 은 regular|final");
                if (!groups.Add(group)) errors.Add($"{at}: 중복");
                int c = Int(row, "common", at, errors, 0), r = Int(row, "rare", at, errors, 0), e = Int(row, "epic", at, errors, 0);
                if (c + r + e <= 0) errors.Add($"{at}: 가중치가 전부 0");
            }
            foreach (string group in RoundGroups) if (!groups.Contains(group)) errors.Add($"{ChestRarityWeights}: '{group}' 행이 없음");
        }

        private static void ValidateFacilities(List<BalanceTableRow> rows, List<string> errors)
        {
            var seen = new HashSet<string>();
            foreach (BalanceTableRow row in rows)
            {
                string map = Cell(row, "map_key").Trim();
                string facility = Cell(row, "facility").Trim().ToLowerInvariant();
                string at = $"{Facilities}({map}/{facility})";
                if (map.Length == 0) errors.Add($"{at}: map_key 비어 있음");
                if (!In(facility, FacilityNames)) errors.Add($"{at}: facility 는 roulette|shop|upgrade");
                if (!seen.Add(map + "/" + facility)) errors.Add($"{at}: 같은 맵에 같은 시설이 두 번");
                Num(row, "x", at, errors); Num(row, "y", at, errors); Num(row, "z", at, errors);
                Num(row, "radius", at, errors, 0.01f);
            }
        }

        private static void ValidateRoulette(List<BalanceTableRow> rows, List<string> errors)
        {
            var slots = new HashSet<int>();
            foreach (BalanceTableRow row in rows)
            {
                int index = Int(row, "slot_index", RouletteSlots, errors, 0);
                string at = $"{RouletteSlots}({index})";
                if (!slots.Add(index)) errors.Add($"{at}: slot_index 중복");
                Int(row, "number", at, errors, 0);
            }
            for (int i = 0; i < slots.Count; i++)
                if (!slots.Contains(i)) errors.Add($"{RouletteSlots}: slot_index 가 0부터 연속이 아님 ({i} 없음)");
        }

        private static void ValidateZone(List<BalanceTableRow> rows, List<string> errors)
        {
            var stages = new HashSet<int>();
            float lastStart = -1f;
            foreach (BalanceTableRow row in rows)
            {
                int stage = Int(row, "stage", ZonePhases, errors, 1);
                string at = $"{ZonePhases}({stage})";
                if (!stages.Add(stage)) errors.Add($"{at}: stage 중복");
                float start = Num(row, "start_seconds", at, errors, 0f);
                Num(row, "shrink_seconds", at, errors, 0f);
                float ratio = Num(row, "target_area_ratio", at, errors, 0f);
                if (ratio > 1f) errors.Add($"{at}: target_area_ratio 는 0~1");
                Num(row, "damage_per_second", at, errors, 0f);
                Num(row, "first_tick_delay_seconds", at, errors, 0f, required: false);
                if (start < lastStart) errors.Add($"{at}: start_seconds 가 앞 단계보다 이르다");
                lastStart = start;
            }
        }
    }
}

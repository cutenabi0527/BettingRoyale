using System;
using System.Collections.Generic;
using BettingRoyale.Data.Common;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.Preparation;
using BettingRoyale.Data.Upgrade;

namespace BettingRoyale.Data.Online
{
    public enum LobbyBotPrepActionKind
    {
        /// <summary>할 일이 없다. 집결 구역으로 가서 기다린다.</summary>
        GoRally = 0,

        /// <summary>시설 원 안에서 다음 기회를 기다린다(룰렛 다음 판).</summary>
        WaitAt = 1,

        Upgrade = 2,
        Buy = 3,
        Bet = 4,
    }

    /// <summary>봇 한 기의 다음 할 일. 시설이 필요한 일이면 <see cref="Facility"/> 원 안에서만 요청한다.</summary>
    public readonly struct LobbyBotPrepAction
    {
        public readonly LobbyBotPrepActionKind Kind;
        public readonly PreparationFacility Facility;
        public readonly UpgradeSlot Slot;
        public readonly int OfferId;
        public readonly int Number;
        public readonly int Amount;

        private LobbyBotPrepAction(LobbyBotPrepActionKind kind, PreparationFacility facility, UpgradeSlot slot,
            int offerId, int number, int amount)
        {
            Kind = kind;
            Facility = facility;
            Slot = slot;
            OfferId = offerId;
            Number = number;
            Amount = amount;
        }

        public bool NeedsFacility => Kind is LobbyBotPrepActionKind.Upgrade or LobbyBotPrepActionKind.Buy
            or LobbyBotPrepActionKind.Bet or LobbyBotPrepActionKind.WaitAt;

        public static LobbyBotPrepAction Rally() => new(LobbyBotPrepActionKind.GoRally, default, default, 0, 0, 0);
        public static LobbyBotPrepAction Wait(PreparationFacility facility) => new(LobbyBotPrepActionKind.WaitAt, facility, default, 0, 0, 0);
        public static LobbyBotPrepAction UpgradeSlot(UpgradeSlot slot) => new(LobbyBotPrepActionKind.Upgrade, PreparationFacility.Upgrade, slot, 0, 0, 0);
        public static LobbyBotPrepAction Buy(int offerId) => new(LobbyBotPrepActionKind.Buy, PreparationFacility.Shop, default, offerId, 0, 0);
        public static LobbyBotPrepAction Bet(int number, int amount) => new(LobbyBotPrepActionKind.Bet, PreparationFacility.Roulette, default, 0, number, amount);

        public override string ToString() => Kind switch
        {
            LobbyBotPrepActionKind.Upgrade => $"Upgrade({Slot})",
            LobbyBotPrepActionKind.Buy => $"Buy(#{OfferId})",
            LobbyBotPrepActionKind.Bet => $"Bet({Number}x, {Amount}G)",
            LobbyBotPrepActionKind.WaitAt => $"Wait({Facility})",
            _ => "Rally",
        };
    }

    /// <summary>상점 한 칸을 봇이 보는 모습.</summary>
    public readonly struct LobbyBotShopOption
    {
        public readonly int OfferId;
        public readonly EquipmentItemKind Kind;
        public readonly int Price;

        /// <summary>돈도 있고 보유 한도에도 걸리지 않는다.</summary>
        public readonly bool CanBuy;

        public LobbyBotShopOption(int offerId, EquipmentItemKind kind, int price, bool canBuy)
        {
            OfferId = offerId;
            Kind = kind;
            Price = price;
            CanBuy = canBuy;
        }
    }

    /// <summary>봇이 판단하는 순간의 관측값. 전부 Host 경제 도메인이 이미 내주는 값이다.</summary>
    public readonly struct LobbyBotPrepSnapshot
    {
        public readonly int Gold;
        public readonly float RemainingSeconds;
        public readonly bool CanUpgradeWeapon;
        public readonly int WeaponCost;
        public readonly int WeaponSuccessPercent;
        public readonly bool CanUpgradeArmor;
        public readonly int ArmorCost;
        public readonly int ArmorSuccessPercent;
        public readonly IReadOnlyList<LobbyBotShopOption> Offers;
        public readonly bool RouletteBetting;
        public readonly bool RouletteCanBet;
        public readonly int RouletteCycle;
        public readonly int RouletteMinBet;

        /// <summary>이번 준비 단계에 룰렛 판이 더 열리는가(남은 시간이 한 판 + 여유보다 길다).</summary>
        public readonly bool RouletteOpensAgain;

        public LobbyBotPrepSnapshot(int gold, float remainingSeconds,
            bool canUpgradeWeapon, int weaponCost, int weaponSuccessPercent,
            bool canUpgradeArmor, int armorCost, int armorSuccessPercent,
            IReadOnlyList<LobbyBotShopOption> offers,
            bool rouletteBetting, bool rouletteCanBet, int rouletteCycle, int rouletteMinBet, bool rouletteOpensAgain)
        {
            Gold = gold;
            RemainingSeconds = remainingSeconds;
            CanUpgradeWeapon = canUpgradeWeapon;
            WeaponCost = weaponCost;
            WeaponSuccessPercent = weaponSuccessPercent;
            CanUpgradeArmor = canUpgradeArmor;
            ArmorCost = armorCost;
            ArmorSuccessPercent = armorSuccessPercent;
            Offers = offers ?? Array.Empty<LobbyBotShopOption>();
            RouletteBetting = rouletteBetting;
            RouletteCanBet = rouletteCanBet;
            RouletteCycle = rouletteCycle;
            RouletteMinBet = rouletteMinBet;
            RouletteOpensAgain = rouletteOpensAgain;
        }
    }

    /// <summary>
    /// 봇 한 기가 이번 준비 라운드에 한 일. Host 드라이버가 요청 결과(골드 변화)를 보고 채운다.
    /// 장신구 보유는 매치 동안 남고 나머지는 라운드마다 비운다.
    /// </summary>
    public sealed class LobbyBotPrepMemory
    {
        public int Round { get; private set; } = -1;
        public int RoundStartGold { get; private set; }
        public int UpgradeSpent { get; set; }
        public int ShopSpent { get; set; }
        public int ConsumablesBought { get; set; }
        public int CyclesBet { get; set; }
        public int LastBetCycle { get; set; } = -1;
        public bool AccessoryOwned { get; set; }
        public bool UpgradeBlocked { get; set; }
        public bool ShopBlocked { get; set; }
        public bool RouletteBlocked { get; set; }

        public void BeginRound(int round, int gold)
        {
            if (round == Round) return;
            Round = round;
            RoundStartGold = Math.Max(0, gold);
            UpgradeSpent = ShopSpent = ConsumablesBought = CyclesBet = 0;
            LastBetCycle = -1;
            UpgradeBlocked = ShopBlocked = RouletteBlocked = false;
        }
    }

    /// <summary>
    /// 대기방 봇의 준비 라운드 결정(S15P21A606-326, S-GD-9CZKVT). 성향마다 순서·예산·기준이 다르다 — 수치는
    /// 에이전트 임의값이다(S-GD-6QP2RG).
    /// </summary>
    /// <remarks>
    /// 한 번 부를 때 할 일 하나만 고른다. 요청을 보내고 결과가 반영된 다음 호출에서 다음 일을 고른다 — 한 번에
    /// 여러 요청을 몰아 보내면 방 전체로 가는 상태 방송이 몰린다.
    /// </remarks>
    public static class LobbyBotPreparationPlanner
    {
        /// <summary>남은 준비 시간이 이보다 적으면 새 일을 시작하지 않고 집결 구역으로 간다.</summary>
        public const float RallySeconds = 6f;

        private enum Step { Upgrade, Shop, RouletteIfOpen, RouletteWait }

        private static readonly Step[] AggressiveSteps = { Step.Upgrade, Step.Shop };
        private static readonly Step[] SteadySteps = { Step.Shop, Step.Upgrade };
        private static readonly Step[] GamblerSteps = { Step.RouletteIfOpen, Step.Upgrade, Step.Shop, Step.RouletteWait };

        /// <summary>성향별 수치(S-GD-6QP2RG).</summary>
        public readonly struct Profile
        {
            public readonly int UpgradeBudgetPercent;
            public readonly int ShopBudgetPercent;
            public readonly int MinUpgradeSuccessPercent;
            public readonly int ConsumableCap;
            public readonly bool BuysAccessory;
            public readonly int RouletteCycles;
            public readonly int BetMinPercent;
            public readonly int BetMaxPercent;

            public Profile(int upgradeBudgetPercent, int shopBudgetPercent, int minUpgradeSuccessPercent,
                int consumableCap, bool buysAccessory, int rouletteCycles, int betMinPercent, int betMaxPercent)
            {
                UpgradeBudgetPercent = upgradeBudgetPercent;
                ShopBudgetPercent = shopBudgetPercent;
                MinUpgradeSuccessPercent = minUpgradeSuccessPercent;
                ConsumableCap = consumableCap;
                BuysAccessory = buysAccessory;
                RouletteCycles = rouletteCycles;
                BetMinPercent = betMinPercent;
                BetMaxPercent = betMaxPercent;
            }
        }

        public static Profile ProfileOf(LobbyBotPrepStyle style) => style switch
        {
            LobbyBotPrepStyle.Aggressive => new Profile(70, 30, 60, 1, true, 0, 0, 0),
            LobbyBotPrepStyle.Steady => new Profile(40, 40, 75, 2, true, 0, 0, 0),
            _ => new Profile(20, 10, 90, 1, false, 2, 20, 40),
        };

        /// <summary>룰렛 숫자 가중치 — 배율이 낮은 칸 위주(도박형이라도 대부분 1·3 에 건다).</summary>
        private static readonly (int Number, int Weight)[] BetNumbers = { (1, 40), (3, 35), (5, 15), (10, 7), (20, 3) };

        public static LobbyBotPrepAction Decide(LobbyBotPersona persona, in LobbyBotPrepSnapshot snapshot,
            LobbyBotPrepMemory memory, DeterministicRandom random)
        {
            if (memory == null) throw new ArgumentNullException(nameof(memory));
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (snapshot.RemainingSeconds < RallySeconds) return LobbyBotPrepAction.Rally();

            Profile profile = ProfileOf(persona.Prep);
            Step[] steps = persona.Prep switch
            {
                LobbyBotPrepStyle.Aggressive => AggressiveSteps,
                LobbyBotPrepStyle.Steady => SteadySteps,
                _ => GamblerSteps,
            };

            foreach (Step step in steps)
            {
                LobbyBotPrepAction? action = step switch
                {
                    Step.Upgrade => DecideUpgrade(profile, snapshot, memory),
                    Step.Shop => DecideShop(persona.Prep, profile, snapshot, memory, random),
                    Step.RouletteIfOpen => DecideBet(profile, snapshot, memory, random),
                    Step.RouletteWait => DecideRouletteWait(profile, snapshot, memory),
                    _ => null,
                };
                if (action.HasValue) return action.Value;
            }

            return LobbyBotPrepAction.Rally();
        }

        private static LobbyBotPrepAction? DecideUpgrade(in Profile profile, in LobbyBotPrepSnapshot s, LobbyBotPrepMemory m)
        {
            if (m.UpgradeBlocked) return null;
            int budgetLeft = m.RoundStartGold * profile.UpgradeBudgetPercent / 100 - m.UpgradeSpent;
            if (Affordable(s.CanUpgradeWeapon, s.WeaponCost, s.WeaponSuccessPercent, budgetLeft, s.Gold, profile))
                return LobbyBotPrepAction.UpgradeSlot(UpgradeSlot.Weapon);
            if (Affordable(s.CanUpgradeArmor, s.ArmorCost, s.ArmorSuccessPercent, budgetLeft, s.Gold, profile))
                return LobbyBotPrepAction.UpgradeSlot(UpgradeSlot.Armor);
            return null;
        }

        private static bool Affordable(bool can, int cost, int successPercent, int budgetLeft, int gold, in Profile profile) =>
            can && cost > 0 && cost <= budgetLeft && cost <= gold && successPercent >= profile.MinUpgradeSuccessPercent;

        private static LobbyBotPrepAction? DecideShop(LobbyBotPrepStyle style, in Profile profile, in LobbyBotPrepSnapshot s,
            LobbyBotPrepMemory m, DeterministicRandom random)
        {
            if (m.ShopBlocked) return null;
            int budgetLeft = m.RoundStartGold * profile.ShopBudgetPercent / 100 - m.ShopSpent;
            if (budgetLeft <= 0) return null;

            if (profile.BuysAccessory && !m.AccessoryOwned)
            {
                // 공격형은 살 수 있는 가장 비싼 장신구, 안정형은 가장 싼 장신구.
                int accessory = Pick(s.Offers, EquipmentItemKind.Accessory, budgetLeft, s.Gold,
                    preferExpensive: style == LobbyBotPrepStyle.Aggressive, random: null);
                if (accessory >= 0) return LobbyBotPrepAction.Buy(accessory);
            }

            if (m.ConsumablesBought < profile.ConsumableCap)
            {
                // 안정형은 가장 싼 소모품(대개 물약), 공격형은 가장 비싼 것(투척 등), 도박형은 아무거나.
                int consumable = Pick(s.Offers, EquipmentItemKind.Consumable, budgetLeft, s.Gold,
                    preferExpensive: style == LobbyBotPrepStyle.Aggressive,
                    random: style == LobbyBotPrepStyle.Gambler ? random : null);
                if (consumable >= 0) return LobbyBotPrepAction.Buy(consumable);
            }

            return null;
        }

        private static int Pick(IReadOnlyList<LobbyBotShopOption> offers, EquipmentItemKind kind, int budgetLeft, int gold,
            bool preferExpensive, DeterministicRandom random)
        {
            var candidates = new List<LobbyBotShopOption>();
            for (int i = 0; i < offers.Count; i++)
            {
                LobbyBotShopOption offer = offers[i];
                if (offer.Kind == kind && offer.CanBuy && offer.Price > 0 && offer.Price <= budgetLeft && offer.Price <= gold)
                    candidates.Add(offer);
            }

            if (candidates.Count == 0) return -1;
            if (random != null) return candidates[random.Next(candidates.Count)].OfferId;
            LobbyBotShopOption chosen = candidates[0];
            foreach (LobbyBotShopOption offer in candidates)
            {
                if (preferExpensive ? offer.Price > chosen.Price : offer.Price < chosen.Price) chosen = offer;
            }

            return chosen.OfferId;
        }

        private static LobbyBotPrepAction? DecideBet(in Profile profile, in LobbyBotPrepSnapshot s, LobbyBotPrepMemory m,
            DeterministicRandom random)
        {
            if (m.RouletteBlocked || m.CyclesBet >= profile.RouletteCycles) return null;
            if (!s.RouletteBetting || !s.RouletteCanBet || m.LastBetCycle == s.RouletteCycle) return null;
            int minBet = Math.Max(1, s.RouletteMinBet);
            if (s.Gold < minBet) return null;
            int percent = profile.BetMinPercent + random.Next(Math.Max(1, profile.BetMaxPercent - profile.BetMinPercent + 1));
            int amount = Math.Clamp(s.Gold * percent / 100, minBet, s.Gold);
            return LobbyBotPrepAction.Bet(PickNumber(random), amount);
        }

        private static LobbyBotPrepAction? DecideRouletteWait(in Profile profile, in LobbyBotPrepSnapshot s, LobbyBotPrepMemory m)
        {
            if (m.RouletteBlocked || m.CyclesBet >= profile.RouletteCycles) return null;
            if (s.Gold < Math.Max(1, s.RouletteMinBet)) return null;
            // 지금 판에 이미 걸었거나 베팅 창이 닫혔으면 다음 판이 열릴 때만 기다린다.
            if (s.RouletteBetting && s.RouletteCanBet && m.LastBetCycle != s.RouletteCycle) return LobbyBotPrepAction.Wait(PreparationFacility.Roulette);
            return s.RouletteOpensAgain ? LobbyBotPrepAction.Wait(PreparationFacility.Roulette) : null;
        }

        private static int PickNumber(DeterministicRandom random)
        {
            int total = 0;
            foreach ((int _, int weight) in BetNumbers) total += weight;
            int roll = random.Next(total);
            foreach ((int number, int weight) in BetNumbers)
            {
                if (roll < weight) return number;
                roll -= weight;
            }

            return BetNumbers[0].Number;
        }
    }
}

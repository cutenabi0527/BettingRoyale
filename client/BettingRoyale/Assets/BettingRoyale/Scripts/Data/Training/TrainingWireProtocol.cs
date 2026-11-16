using System;
using System.IO;
using System.Text;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.Roulette;

namespace BettingRoyale.Data.Training
{
    /// <summary>훈련 전용 신뢰 채널. 작은 유한 패킷만 허용하며 다른 세션/송신자 검증은 Host 어댑터가 한다.</summary>
    public static class TrainingWireProtocol
    {
        public const int CommandMagic = 0x54434D44;
        public const int StateMagic = 0x54535441;
        public const int MaxBytes = 16384;

        /// <summary>
        /// 패킷 판. <b>6 은 상태 스냅숏에 봇을 세운 구역 조합(<c>BotZones</c>) 한 바이트가 붙은 판이다</b>(S-GD-V9ZMN3).
        /// 5 는 시험 스냅숏에서 저점 매수의 위험 보급 필드(도전 가능·보상·지급)와 리롤 캐시백의
        /// 옛 환급액을 뺀 판이고(S-GD-TMPBEF), 4 는 진열 한도와 룰렛 스냅샷이 함께 붙은 판이다. 필드를 더하거나 뺄 때마다
        /// 판을 올린다 — 예전 판을 그대로 읽으면 필드 경계가 어긋나 값이 조용히 뒤바뀐다.
        /// </summary>
        private const byte Version = 6;

        /// <summary>순위 프로필 문자열의 최대 바이트. 허용 목록 밖의 값은 읽기에서 거절된다.</summary>
        private const int RankTextMax = TrainingBlessingTrialRank.MaxTextBytes;

        /// <summary>
        /// 시험 스냅숏 꼬리의 블라인드 베팅 필드 크기(바이트).
        /// bool 3개(3) + int 1개(4) + float 1개(4) = 11. 잘린 패킷을 여기서 먼저 거절한다.
        /// </summary>
        private const int BlindBetFieldBytes = 11;

        private static readonly UTF8Encoding Utf8 = new(false, true);

        public static byte[] Serialize(in TrainingActionRequest request)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, true);
            writer.Write(Version);
            WriteText(writer, request.SessionId, 64);
            writer.Write(request.Sequence);
            writer.Write((byte)request.Kind);
            writer.Write(request.Value);
            WriteText(writer, request.Selection, 128);
            return stream.ToArray();
        }

        public static bool TryReadRequest(ReadOnlySpan<byte> bytes, out TrainingActionRequest request)
        {
            request = default;
            if (bytes.Length < 1 || bytes.Length > MaxBytes) return false;
            try
            {
                using var stream = new MemoryStream(bytes.ToArray(), false);
                using var reader = new BinaryReader(stream, Utf8);
                if (reader.ReadByte() != Version) return false;
                string session = ReadText(reader, 64);
                int sequence = reader.ReadInt32();
                var kind = (TrainingActionKind)reader.ReadByte();
                int value = reader.ReadInt32();
                string selection = ReadText(reader, 128);
                if (stream.Position != stream.Length || sequence < 1 || session.Length == 0
                    || !Enum.IsDefined(typeof(TrainingActionKind), kind)) return false;
                request = new TrainingActionRequest(session, sequence, kind, value, selection);
                return true;
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is ArgumentException) { return false; }
        }

        public static byte[] Serialize(TrainingSessionView state)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Utf8, true);
            writer.Write(Version);
            WriteText(writer, state.SessionId, 64);
            writer.Write(state.Version);
            writer.Write(state.PlayerId);
            writer.Write(state.AcknowledgedSequence);
            writer.Write(state.PlayerDamageEnabled);
            // 방 전체 설정 둘을 붙여 둔다. 구역이 넷이라 한 바이트에 다 들어간다.
            writer.Write((byte)TrainingBotRule.Clamp(state.BotZones));
            writer.Write(state.Gold);
            writer.Write((int)state.Weapon);
            writer.Write(state.WeaponLevel);
            writer.Write(state.ArmorLevel);
            writer.Write(state.WeaponNextCost);
            writer.Write(state.ArmorNextCost);
            writer.Write(state.CanUpgradeWeapon);
            writer.Write(state.CanUpgradeArmor);
            if (state.AcquiredIds.Length > 64) throw new ArgumentException("축복 목록이 너무 큽니다.");
            writer.Write((byte)state.AcquiredIds.Length);
            foreach (string id in state.AcquiredIds) WriteText(writer, id, 128);
            WriteText(writer, state.Message, 512);
            writer.Write(state.RequestRejected);
            if (state.Offers.Length > 32) throw new ArgumentException("상품 목록이 너무 큽니다.");
            writer.Write((byte)state.Offers.Length);
            foreach (ShopOfferView offer in state.Offers)
            {
                writer.Write(offer.OfferId); writer.Write(offer.ItemId); writer.Write((int)offer.Kind);
                writer.Write(offer.Price); writer.Write(offer.Quantity); writer.Write(offer.CanAfford);
                writer.Write(offer.CarryLimitReached);
            }
            WriteTrial(writer, state.Trial);
            WriteRoulette(writer, state.Roulette);
            return stream.ToArray();
        }

        /// <summary>
        /// 룰렛 스냅샷을 싣는다. <b>유한하다</b> — 숫자 종류는 상한이 있고 나머지는 고정 폭이다.
        /// 25칸 배치 배열은 싣지 않는다(<see cref="TrainingRouletteView"/> 주석).
        /// </summary>
        private static void WriteRoulette(BinaryWriter writer, TrainingRouletteView roulette)
        {
            if (roulette == null) throw new ArgumentNullException(nameof(roulette));
            if (roulette.Numbers.Length > TrainingRouletteView.MaxNumbers)
                throw new ArgumentException("룰렛 숫자 목록이 너무 큽니다.");
            if (roulette.Payouts.Length != roulette.Numbers.Length
                || roulette.MyStakes.Length != roulette.Numbers.Length)
                throw new ArgumentException("룰렛 배열 길이가 어긋납니다.");

            writer.Write((byte)roulette.State);
            writer.Write(roulette.Cycle);
            writer.Write(roulette.RemainingTicks);
            writer.Write(roulette.WinningNumber);
            writer.Write(roulette.MyStakeTotal);
            writer.Write(roulette.BettorCount);
            writer.Write(roulette.MinBet);
            writer.Write(roulette.CanBet);
            writer.Write(roulette.LastNet);
            writer.Write((byte)roulette.Numbers.Length);
            for (int i = 0; i < roulette.Numbers.Length; i++)
            {
                writer.Write(roulette.Numbers[i]);
                writer.Write(roulette.Payouts[i]);
                writer.Write(roulette.MyStakes[i]);
            }
        }

        /// <summary>
        /// 룰렛 스냅샷을 읽는다. <b>못 믿는 값은 전부 여기서 막는다</b> — 정의되지 않은 국면,
        /// 음수 금액, 상한을 넘는 숫자 종류. 하나라도 어긋나면 스냅샷 전체를 버린다.
        /// </summary>
        private static bool TryReadRoulette(BinaryReader reader, out TrainingRouletteView roulette)
        {
            roulette = null;
            var state = (RouletteState)reader.ReadByte();
            int cycle = reader.ReadInt32(), remaining = reader.ReadInt32();
            int winning = reader.ReadInt32(), staked = reader.ReadInt32();
            int bettors = reader.ReadInt32(), minBet = reader.ReadInt32();
            bool canBet = reader.ReadBoolean();
            int lastNet = reader.ReadInt32();
            int count = reader.ReadByte();

            if (count > TrainingRouletteView.MaxNumbers) return false;

            var numbers = new int[count];
            var payouts = new int[count];
            var stakes = new int[count];
            for (int i = 0; i < count; i++)
            {
                numbers[i] = reader.ReadInt32();
                payouts[i] = reader.ReadInt32();
                stakes[i] = reader.ReadInt32();
                if (numbers[i] < 1 || payouts[i] < 1 || stakes[i] < 0) return false;
            }

            if (!Enum.IsDefined(typeof(RouletteState), state)
                || cycle < 0 || remaining < 0 || winning < 0 || staked < 0
                || bettors < 0 || minBet < 0) return false;

            roulette = new TrainingRouletteView(state, cycle, remaining, winning, staked, bettors,
                minBet, canBet, lastNet, numbers, payouts, stakes);
            return true;
        }

        /// <summary>
        /// 시험 주기 스냅샷을 싣는다. <b>유한하다</b> — 후보는 최대 3개, 프로필은 허용 목록 안의
        /// 짧은 문자열, 나머지는 고정 폭 숫자와 bool 이다.
        /// </summary>
        private static void WriteTrial(BinaryWriter writer, TrainingBlessingTrialView trial)
        {
            if (trial == null) throw new ArgumentNullException(nameof(trial));
            if (trial.CandidateIds.Length > TrainingBlessingTrialView.MaxCandidates)
                throw new ArgumentException("시험 후보 목록이 너무 큽니다.");
            if (!TrainingBlessingTrialRank.IsDefined(trial.RankProfile))
                throw new ArgumentException("등록되지 않은 순위 프로필입니다.");

            writer.Write((byte)trial.Phase);
            writer.Write(trial.Cycle);
            writer.Write(trial.Round);
            WriteText(writer, trial.RankProfile, RankTextMax);
            writer.Write(trial.LowerHalfEligible);
            writer.Write(trial.HasInterest);
            writer.Write(trial.HasLowBuy);
            writer.Write(trial.HasCashback);
            writer.Write(trial.CanBegin);
            writer.Write(trial.CanDeposit);
            writer.Write(trial.CanReroll);
            writer.Write(trial.CanSelect);
            writer.Write(trial.DepositCost);
            writer.Write(trial.DepositAmount);
            writer.Write(trial.RerollCost);
            writer.Write(trial.RerollCount);
            writer.Write(trial.SettlementRefund);
            WriteText(writer, trial.SelectedId, 128);
            writer.Write((byte)trial.CandidateIds.Length);
            for (int i = 0; i < trial.CandidateIds.Length; i++)
            {
                WriteText(writer, trial.CandidateIds[i], 128);
                writer.Write(trial.Locked[i]);
            }
            writer.Write(trial.HasBlindBet);
            writer.Write(trial.CanBlindBet);
            writer.Write(trial.BlindBetAccepted);
            writer.Write(trial.BlindBetBonusGold);
            writer.Write(trial.BlindBetHealthCostFraction);
        }

        /// <summary>
        /// 시험 주기 스냅샷을 읽는다. <b>못 믿는 값은 전부 여기서 막는다</b> —
        /// 정의되지 않은 국면, 허용 목록 밖의 프로필, 음수 금액, 상한을 넘는 후보 수.
        /// 하나라도 어긋나면 스냅샷 전체를 버린다.
        /// </summary>
        private static bool TryReadTrial(BinaryReader reader, out TrainingBlessingTrialView trial)
        {
            trial = null;
            var phase = (TrainingBlessingTrialPhase)reader.ReadByte();
            int cycle = reader.ReadInt32(), round = reader.ReadInt32();
            string rank = ReadText(reader, RankTextMax);
            bool lowerHalf = reader.ReadBoolean(), hasInterest = reader.ReadBoolean();
            bool hasLowBuy = reader.ReadBoolean(), hasCashback = reader.ReadBoolean();
            bool canBegin = reader.ReadBoolean(), canDeposit = reader.ReadBoolean();
            bool canReroll = reader.ReadBoolean(), canSelect = reader.ReadBoolean();
            int depositCost = reader.ReadInt32(), depositAmount = reader.ReadInt32();
            int rerollCost = reader.ReadInt32(), rerollCount = reader.ReadInt32();
            int settlementRefund = reader.ReadInt32();
            string selectedId = ReadText(reader, 128);
            int candidateCount = reader.ReadByte();

            if (candidateCount > TrainingBlessingTrialView.MaxCandidates) return false;

            var candidateIds = new string[candidateCount];
            var locked = new bool[candidateCount];
            for (int i = 0; i < candidateCount; i++)
            {
                candidateIds[i] = ReadText(reader, 128);
                locked[i] = reader.ReadBoolean();
                // 빈 id 는 화면이 누를 수 없는 카드를 그리게 된다. 후보는 항상 id 를 갖는다.
                if (candidateIds[i].Length == 0) return false;
            }

            long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
            // 지금 판의 시험 스냅숏은 블라인드 베팅 5개 필드(1+1+1+4+4 = 11바이트)를 반드시 싣는다.
            // 중간에서 잘린 패킷을 구버전(필드 없음)으로 오인해 정상 상태로 발행하지 않는다.
            //
            // ⚠️ "정확히 11" 로 보면 안 된다. 시험 블록 뒤에는 룰렛 블록이 더 붙으므로
            // (Serialize 의 WriteTrial → WriteRoulette 순서) 남은 바이트는 11보다 크다.
            // 꼬리에 남는 군더더기는 TryReadState 의 Position != Length 검사가 잡는다.
            if (remaining < BlindBetFieldBytes) return false;
            bool hasBlindBet = reader.ReadBoolean();
            bool canBlindBet = reader.ReadBoolean();
            bool blindBetAccepted = reader.ReadBoolean();
            int blindBetBonusGold = reader.ReadInt32();
            float blindBetHealthCostFraction = reader.ReadSingle();

            if (!Enum.IsDefined(typeof(TrainingBlessingTrialPhase), phase)
                || !TrainingBlessingTrialRank.IsDefined(rank)
                || cycle < 0 || round < 0 || round > 3
                || depositCost < 0 || depositAmount < 0 || rerollCost < 0 || rerollCount < 0
                || settlementRefund < 0
                || blindBetBonusGold < 0 || float.IsNaN(blindBetHealthCostFraction)
                || float.IsInfinity(blindBetHealthCostFraction) || blindBetHealthCostFraction < 0f
                || blindBetHealthCostFraction > 1f)
                return false;

            trial = new TrainingBlessingTrialView(phase, cycle, round, rank, lowerHalf, hasInterest,
                hasLowBuy, hasCashback, canBegin, canDeposit, canReroll, canSelect,
                depositCost, depositAmount, rerollCost, rerollCount, settlementRefund,
                selectedId, candidateIds, locked,
                hasBlindBet, canBlindBet, blindBetAccepted, blindBetBonusGold, blindBetHealthCostFraction);
            return true;
        }

        public static bool TryReadState(ReadOnlySpan<byte> bytes, out TrainingSessionView state)
        {
            state = null;
            if (bytes.Length < 1 || bytes.Length > MaxBytes) return false;
            try
            {
                using var stream = new MemoryStream(bytes.ToArray(), false);
                using var reader = new BinaryReader(stream, Utf8);
                if (reader.ReadByte() != Version) return false;
                string session = ReadText(reader, 64);
                int version = reader.ReadInt32(), player = reader.ReadInt32(), acknowledged = reader.ReadInt32();
                bool pvp = reader.ReadBoolean();
                int botZones = reader.ReadByte();
                int gold = reader.ReadInt32();
                var weapon = (WeaponType)reader.ReadInt32();
                int weaponLevel = reader.ReadInt32(), armorLevel = reader.ReadInt32();
                int weaponCost = reader.ReadInt32(), armorCost = reader.ReadInt32();
                bool weaponOpen = reader.ReadBoolean(), armorOpen = reader.ReadBoolean();
                int count = reader.ReadByte();
                if (count > 64) return false;
                var acquired = new string[count];
                for (int i = 0; i < count; i++) acquired[i] = ReadText(reader, 128);
                string message = ReadText(reader, 512);
                bool rejected = reader.ReadBoolean();
                int offerCount = reader.ReadByte();
                if (offerCount > 32) return false;
                var offers = new ShopOfferView[offerCount];
                for (int i = 0; i < offerCount; i++)
                {
                    int offerId = reader.ReadInt32(), itemId = reader.ReadInt32();
                    var kind = (EquipmentItemKind)reader.ReadInt32();
                    int price = reader.ReadInt32(), quantity = reader.ReadInt32();
                    bool affordable = reader.ReadBoolean();
                    bool carryLimitReached = reader.ReadBoolean();
                    if (offerId < 1 || itemId < 1 || price < 0 || quantity < 1 || !Enum.IsDefined(typeof(EquipmentItemKind), kind)) return false;
                    offers[i] = new ShopOfferView(offerId, itemId, kind, price, quantity, affordable, carryLimitReached);
                }
                if (!TryReadTrial(reader, out TrainingBlessingTrialView trial)) return false;
                if (!TryReadRoulette(reader, out TrainingRouletteView roulette)) return false;
                if (stream.Position != stream.Length || session.Length == 0 || version < 1 || player < 1
                    || acknowledged < 0 || gold < 0 || weaponLevel < 0 || weaponLevel > 5 || armorLevel < 0 || armorLevel > 5
                    || weaponCost < 0 || armorCost < 0 || !Enum.IsDefined(typeof(WeaponType), weapon)
                    || !TrainingBotRule.IsValid(botZones)) return false;
                state = new TrainingSessionView(session, version, player, acknowledged, pvp, gold, weapon,
                    weaponLevel, armorLevel, weaponCost, armorCost, weaponOpen, armorOpen, acquired,
                    message, rejected, offers, trial, roulette, botZones);
                return true;
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is ArgumentException) { return false; }
        }

        private static void WriteText(BinaryWriter writer, string value, int max)
        {
            byte[] bytes = Utf8.GetBytes(value ?? string.Empty);
            if (bytes.Length > max) throw new ArgumentException("훈련 메시지 길이를 초과했습니다.");
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadText(BinaryReader reader, int max)
        {
            int count = reader.ReadUInt16();
            if (count > max || count > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("훈련 메시지 길이가 올바르지 않습니다.");
            return Utf8.GetString(reader.ReadBytes(count));
        }
    }
}

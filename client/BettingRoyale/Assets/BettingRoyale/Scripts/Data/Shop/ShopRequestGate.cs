using System;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Economy;
using BettingRoyale.Data.Equipment;
using BettingRoyale.Data.Preparation;

namespace BettingRoyale.Data.Shop
{
    /// <summary>
    /// 상점 구매 요청 하나를 <b>끝까지 판정하는 유일한 자리</b> (S-GD-PVVHP9, S-CLI-GRZGBK).
    ///
    /// <para>
    /// <c>EconomyRequestGate</c> 와 같은 모양이다 — 어댑터는 결과를 받아 <b>그대로 발행만</b> 하고
    /// 규칙을 다시 구현하지 않는다 (D-069, D-079).
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ 순서가 곧 원자성이다.</b> 골드 차감(<c>Wallet.TrySpend</c>)과 지급
    /// (<c>EquipmentInventory.TryPickup</c>)은 <b>각각은</b> 전부 되거나 전부 안 되지만,
    /// <b>둘을 합친 것은 아니다.</b> 그래서 이 순서로만 부른다:
    /// </para>
    ///
    /// <code>
    /// 5. 잔액이 되는지 확인만 한다 (차감하지 않는다)
    /// 6. 물건을 먼저 지급한다        ← 실패하면 골드는 손도 안 댔다
    /// 7. 지급이 성공했을 때만 차감한다
    /// </code>
    ///
    /// <para>
    /// <b>차감을 먼저 하면 안 되는 이유</b>: 소모품 칸 3개가 다 차 있으면 지급이 거절되는데
    /// (<c>ConsumableCapacityFull</c>), 그때는 <b>골드만 사라진다.</b> 반대 방향은
    /// 5번의 잔액 확인이 막는다. 그래서 7번이 실패하면 그것은 잔액 부족이 아니라
    /// <b>지급 경로가 경제를 건드린 것</b>이라 던진다 — 자세한 것은 그 자리의 주석에 있다.
    /// </para>
    ///
    /// <para>
    /// <b>장신구는 사는 순간 갖고 있던 것이 사라진다</b> — 드롭되지 않고 소멸한다
    /// (S-GD-B7Q44N, 장신구 간 마법적 충돌). 그 규칙은 <c>EquipmentInventory</c> 안에 이미 있고
    /// 여기서 다시 구현하지 않는다. 다만 <b>돈을 낸 뒤에 사라지는 것이 아니라</b> 4번에서 잔액을
    /// 먼저 확인하므로, 못 살 물건 때문에 갖고 있던 장신구를 잃는 일은 없다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <paramref name="playerId"/> 는 <b>반드시 RPC 입력 권한에서 꺼낸 값</b>이어야 한다.
    /// 페이로드에서 온 값을 넘기면 규약 §5 가 없앤 "남의 골드를 쓰는" 경로가 되살아난다.
    /// </para>
    ///
    /// <para>
    /// <c>UnityEngine</c> 을 참조하지 않는다 (D-060).
    /// </para>
    /// </summary>
    public sealed class ShopRequestGate
    {
        private readonly MatchEconomyState economy;
        private readonly EconomyRequestLedger ledger;
        private readonly ShopCatalog catalog;
        private readonly IShopInventoryProvider inventories;
        private readonly IEquipmentItemCatalog items;
        private readonly IPreparationAccessPolicy access;
        private readonly bool requiresBlessingSelection;

        /// <param name="ledger">
        /// <b>축복 재추첨과 같은 원장을 쓴다.</b> 순번은 명령 종류와 무관한 하나짜리 카운터라
        /// (<c>EconomyRequestTicket.Sequence</c> 주석) 상점만 따로 원장을 두면 순번이 두 갈래로 갈린다.
        /// </param>
        /// <param name="items">
        /// 장비 정의 카탈로그. <b>가격표와 다른 곳에 산다</b> — 진열에는 있는데 정의가 없으면
        /// <c>UnknownItem</c> 으로 걸러 목록에서도 살 수 없게 만든다.
        /// </param>
        /// <param name="access">
        /// 상점 구역 안에 있는지 판정한다 (S-CLI-9QYFCT). <b>Host 가 다시 재는 자리다</b> —
        /// 화면이 "가까이 있다" 고 보낸 값을 믿으면 맵 반대편에서 살 수 있다.
        /// </param>
        /// <param name="requiresBlessingSelection">
        /// <b>이번 라운드의 축복 선택을 확정하기 전에는 상점을 쓸 수 없는가.</b>
        /// 제품 세션은 <c>true</c> 다 — 선택 중에는 이동과 상호작용이 막히고(S-GD-DP2DVC),
        /// 어댑터가 그 상태의 요청을 <c>SelectionRequired</c> 로 거절한다.
        ///
        /// <para>
        /// <b>⚠️ 기본값이 <c>true</c> 인 것이 중요하다.</b> 이 값을 모르는 새 호출부는
        /// <b>진열이 꺼지는 쪽</b>으로 떨어진다. 반대로 기본을 <c>false</c> 로 두면 새 호출부가
        /// 조용히 <b>살 수 없는 칸을 밝게 켜 놓는다</b> — 규약이 없애려던 바로 그 버튼이다.
        /// </para>
        ///
        /// <para>
        /// 훈련 세션은 <c>false</c> 를 넘긴다. 거기엔 축복 선택 단계 자체가 없어서
        /// <c>IsSelectionDone</c> 이 영원히 <c>false</c> 이고, 그대로 두면 연습 상점이
        /// <b>한 칸도 살 수 없는 상태로 굳는다.</b>
        /// </para>
        /// </param>
        public ShopRequestGate(
            MatchEconomyState economy,
            EconomyRequestLedger ledger,
            ShopCatalog catalog,
            IShopInventoryProvider inventories,
            IEquipmentItemCatalog items,
            IPreparationAccessPolicy access,
            bool requiresBlessingSelection = true)
        {
            this.economy = economy ?? throw new ArgumentNullException(nameof(economy));
            this.ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.inventories = inventories ?? throw new ArgumentNullException(nameof(inventories));
            this.items = items ?? throw new ArgumentNullException(nameof(items));
            this.access = access ?? throw new ArgumentNullException(nameof(access));
            this.requiresBlessingSelection = requiresBlessingSelection;
        }

        /// <summary>
        /// 진열대 현황. 화면이 처음 열릴 때와 라운드가 바뀔 때 어댑터가 이걸 발행한다.
        ///
        /// <b>여기의 <c>CanAfford</c> 와 <see cref="HandlePurchase"/> 의 판정은 같은 조건을 본다.</b>
        /// 어긋나면 "밝게 켜져 있는데 눌리지 않는 버튼" 이 생긴다.
        /// </summary>
        public ShopOffersChanged BuildOffers(int playerId, bool isMine)
        {
            var views = new ShopOfferView[catalog.Count];
            bool participant = IsParticipant(playerId);
            int gold = participant ? economy.GetGold(playerId) : 0;

            // ⚠️ participant 를 맨 앞에 둔 것은 순서가 아니라 방어다. 방에 없는 id 로
            //    economy 를 물으면 던진다 — 진열은 늦게 들어온 피어의 id 로도 만들어진다.
            bool open = participant
                && economy.Phase == MatchEconomyPhase.Preparation
                && IsSelectionSettled(playerId)
                && access.CanUse(playerId, PreparationFacility.Shop);

            // 인벤토리는 칸마다가 아니라 한 번만 찾는다 — 한 번의 진열 안에서 바뀌지 않는다.
            // 못 찾는 것은 정상이다(스폰 전). 그때는 보유 개수를 모르므로 한도 판정을 하지 않는다.
            IEquipmentInventoryDriver inventory = null;

            if (participant)
            {
                inventories.TryResolve(playerId, out inventory);
            }

            for (int i = 0; i < catalog.Count; i++)
            {
                var offer = catalog.Offers[i];
                bool sellable = TryResolveSellable(offer, out var definition);

                views[i] = new ShopOfferView(
                    offer.OfferId,
                    offer.ItemId,
                    sellable ? definition.Kind : EquipmentItemKind.None,
                    offer.Price,
                    offer.Quantity,
                    canAfford: open
                        && sellable
                        && gold >= offer.Price
                        && !IsAlreadyOwned(inventory, definition)
                        && !IsConsumableInventoryFull(inventory, definition),
                    // ⚠️ 매번 다시 센다. "한도에 닿았다" 를 저장하면 구매 되돌리기가 생겼을 때
                    //    개수가 줄어도 비활성으로 남는다 (S-GD-FDHBNA).
                    carryLimitReached: sellable && WouldExceedCarryLimit(inventory, definition, offer.Quantity));
            }

            return new ShopOffersChanged(playerId, isMine, views);
        }

        /// <summary>
        /// 구매 요청. <b>같은 요청을 두 번 보내도 골드는 한 번만 나가고 물건도 한 번만 들어온다.</b>
        /// </summary>
        public ShopPurchaseOutcome HandlePurchase(
            int playerId, int offerId, EconomyRequestTicket ticket, bool isMine)
        {
            if (!IsParticipant(playerId))
            {
                return default;
            }

            // ⚠️ 원장은 판정보다 먼저 소비한다. 거절된 요청도 "처리했다" 로 기록된다 —
            //    이유는 EconomyRequestVerdict.Duplicate 주석에 있다: 골드가 모자라 거절된 요청이
            //    나중에 재전송되면, 그사이 상자 보상이 들어왔을 때 **사용자가 이미 "골드 부족" 을 보고
            //    손을 뗀 클릭이 뒤늦게 성공한다.** 그래서 클릭마다 새 순번을 뽑아야 한다.
            var verdict = ledger.Accept(playerId, ticket);

            if (verdict != EconomyRequestVerdict.Fresh)
            {
                // 재전송·지난 요청 — 거절이 아니라 "현재 확정 상태" 를 돌려준다.
                // 사용자의 클릭은 이미 한 번 처리됐으므로 거절을 띄우면 거짓말이 된다.
                return Snapshot(verdict, playerId, isMine, delta: 0);
            }

            // 처음 보는 요청인데 단계가 닫혀 있다 — 이건 재전송과 달리 **알려야 한다.**
            // 조용히 넘기면 규약 §6 이 없애려던 "눌러도 반응 없는 버튼" 이 된다.
            if (economy.Phase != MatchEconomyPhase.Preparation)
            {
                return Reject(
                    EconomyRequestVerdict.PreparationClosed,
                    playerId, isMine, offerId, ShopPurchaseRejectReason.NotPreparation);
            }

            // 이번 라운드의 축복 선택을 아직 확정하지 않았다 (S-GD-DP2DVC — 선택 중에는 상호작용이 막힌다).
            //
            // ⚠️ **진열의 CanAfford 와 같은 조건이다.** 이 판정이 여기 없으면 진열만 꺼지고
            //    도메인은 팔아버린다 — 어댑터가 먼저 걸러주기 때문에 지금까지 드러나지 않았을 뿐이고,
            //    어댑터의 선(先)판정이 사라지는 순간 "꺼진 버튼인데 실제로는 팔리는" 상태가 된다.
            if (!IsSelectionSettled(playerId))
            {
                return Reject(
                    verdict, playerId, isMine, offerId, ShopPurchaseRejectReason.SelectionRequired);
            }

            // 상점 구역 안에 서 있는가 — Host 가 직접 잰다 (S-CLI-9QYFCT).
            // 화면이 보낸 "가까이 있다" 를 믿으면 맵 반대편에서 살 수 있다.
            if (!access.CanUse(playerId, PreparationFacility.Shop))
            {
                return Reject(verdict, playerId, isMine, offerId, ShopPurchaseRejectReason.NotInRange);
            }

            // ① 진열에 있는 칸인가
            if (!catalog.TryGet(offerId, out var offer))
            {
                return Reject(verdict, playerId, isMine, offerId, ShopPurchaseRejectReason.NoSuchOffer);
            }

            // ② 애초에 팔 수 있는 칸인가 — 정의 없음 / 무기 / 비소모품 묶음판매를 한 번에 거른다
            if (!TryResolveSellable(offer, out var definition))
            {
                return Reject(verdict, playerId, isMine, offerId, ShopPurchaseRejectReason.UnknownItem);
            }

            // ③ 이 사람의 인벤토리를 찾을 수 있는가
            if (!inventories.TryResolve(playerId, out var inventory) || inventory == null)
            {
                return Reject(verdict, playerId, isMine, offerId, ShopPurchaseRejectReason.InventoryUnavailable);
            }

            // ④ 이미 끼고 있는 장신구인가 — 사면 아무것도 안 바뀌는데 골드만 나간다
            if (IsAlreadyOwned(inventory, definition))
            {
                return Reject(verdict, playerId, isMine, offerId, ShopPurchaseRejectReason.AlreadyOwned);
            }

            // 상점은 세 칸이 모두 점유되면 기존 스택에 여유가 있더라도 더 팔지 않는다.
            // 상자에서만 비투척 소모품을 즉시 사용하는 별도 규칙을 적용한다.
            if (IsConsumableInventoryFull(inventory, definition))
            {
                return Reject(verdict, playerId, isMine, offerId, ShopPurchaseRejectReason.InventoryFull);
            }

            // ⑤ 잔액 확인만 한다. 아직 차감하지 않는다 — ⑥ 이 실패하면 골드는 손도 안 댄 상태여야 한다.
            if (economy.GetGold(playerId) < offer.Price)
            {
                return Reject(verdict, playerId, isMine, offerId, ShopPurchaseRejectReason.NotEnoughGold);
            }

            // ⑥ 지급 먼저. 자리가 없으면 여기서 거절되고 골드는 그대로다.
            var pickup = inventory.TryAuthorityPickup(offer.ItemId, offer.Quantity);

            if (!pickup.Succeeded)
            {
                return Reject(verdict, playerId, isMine, offerId, Translate(pickup.Failure));
            }

            // ⑦ 지급이 성공했을 때만 차감한다.
            if (!economy.TrySpend(playerId, offer.Price))
            {
                // ⑤ 에서 잔액을 확인했으므로 여기 오면 **⑥ 안에서 잔액이 줄었다**는 뜻이다.
                //
                // ⚠️ 이것은 "불가능" 이 아니라 "금지" 다. ⑥ 은 도메인 밖(어댑터가 구현한
                //    IEquipmentInventoryDriver)으로 나가고, 그 안에서 동기 콜백이 돌 수 있다.
                //    그 콜백이 경제를 만지면 여기 도달한다 — 그래서 IShopInventoryProvider 주석에
                //    "지급 중에 경제를 만지지 않는다" 를 못박아 뒀다.
                //
                // ⚠️ 던진 예외가 화면까지 가지 않을 수 있다. 어댑터가 EventBus 구독부 안에서
                //    이 메서드를 부르면 EventBus 가 예외를 로그 한 줄로 삼킨다. 그래도 던지는 이유는
                //    **물건은 이미 지급됐는데 골드가 안 빠진 상태를 성공으로 반환하지 않기 위해서**다.
                //    로그라도 남는 쪽이 조용히 공짜로 파는 것보다 낫다.
                throw new InvalidOperationException(
                    $"구매 차감이 실패했습니다. player={playerId} offer={offer} " +
                    $"잔액={economy.GetGold(playerId)}. 지급은 이미 끝났으므로 이 상태로 진행하면 안 됩니다. " +
                    "인벤토리 지급 경로가 경제를 건드리고 있는지 확인하세요.");
            }

            // ⑧ 장신구를 샀다면 경제도 같은 값을 보게 한다 (S-GD-YMKKDJ 스탯 기여).
            //
            // ⚠️ **인벤토리가 주인이고 경제는 옮겨 적기만 한다.** 여기서 offer.ItemId 를 쓰지 않고
            //    지급 결과 스냅샷을 읽는 이유다 — 지급이 무엇을 밀어냈는지(한 개만 지니는 규칙,
            //    S-GD-B7Q44N)는 인벤토리가 정한다. 추측해서 적으면 두 값이 갈린다.
            //
            //    장신구가 아닌 물건을 사도 그대로 옮겨 적는다. 소모품 구매는 장신구 칸을
            //    건드리지 않으므로 같은 값이 다시 쓰일 뿐이고, 분기를 두면 "어떤 종류일 때만
            //    동기화한다" 는 규칙이 하나 더 생긴다.
            economy.SetEquippedAccessory(playerId, pickup.Snapshot.AccessoryItemId);

            var outcome = Snapshot(verdict, playerId, isMine, delta: -offer.Price);

            return new ShopPurchaseOutcome(
                outcome.Verdict,
                hasEvents: true,
                gold: outcome.Gold,
                offers: outcome.Offers,
                hasInventoryChange: true,
                inventory: new EquipmentInventoryStateChanged(pickup.Snapshot, isMine),
                hasRejection: false,
                rejection: default);
        }

        /// <summary>거절·재전송에도 현재 상태를 실어 보낸다 — 화면이 이벤트 하나로 다시 그려지게.</summary>
        private ShopPurchaseOutcome Snapshot(
            EconomyRequestVerdict verdict, int playerId, bool isMine, int delta) =>
            new ShopPurchaseOutcome(
                verdict,
                hasEvents: true,
                gold: economy.BuildGoldChanged(playerId, isMine, delta, GoldChangeReason.Spent),
                offers: BuildOffers(playerId, isMine),
                hasInventoryChange: false,
                inventory: default,
                hasRejection: false,
                rejection: default);

        private ShopPurchaseOutcome Reject(
            EconomyRequestVerdict verdict,
            int playerId,
            bool isMine,
            int offerId,
            ShopPurchaseRejectReason reason)
        {
            var snapshot = Snapshot(verdict, playerId, isMine, delta: 0);

            return new ShopPurchaseOutcome(
                snapshot.Verdict,
                hasEvents: true,
                gold: snapshot.Gold,
                offers: snapshot.Offers,
                hasInventoryChange: false,
                inventory: default,
                hasRejection: true,
                rejection: new ShopPurchaseRejected(playerId, isMine, offerId, reason));
        }

        /// <summary>
        /// 애초에 팔 수 있는 칸인가 (S-GD-PVVHP9 규칙 2).
        ///
        /// <b>진열과 구매가 이 판정 하나를 같이 쓴다.</b> 따로 두면 목록에는 살 수 있다고 뜨는데
        /// 누르면 거절되는 칸이 생긴다.
        /// </summary>
        private bool TryResolveSellable(in ShopOffer offer, out EquipmentItemDefinition definition) =>
            IsSellable(offer, items, out definition);

        /// <summary>
        /// <see cref="TryResolveSellable"/> 의 정본. <b>게이트 밖에서도 같은 답이 필요하다</b> —
        /// 진열 목록과 장비 정의가 어긋났는지 검사하는 <see cref="ShopCatalogCoverage"/> 가 이것을 부른다.
        /// 규칙을 복사하면 검사기와 게이트가 갈려서, <b>검사는 통과하는데 구매만 거절되는</b> 상태를
        /// 만들 수 있다.
        /// </summary>
        public static bool IsSellable(
            in ShopOffer offer, IEquipmentItemCatalog items, out EquipmentItemDefinition definition)
        {
            definition = default;

            if (items == null || !items.TryGet(offer.ItemId, out definition) || !definition.IsValid)
            {
                return false;
            }

            // 무기는 매치 시작 전에 고르고 매치 중 바꿀 수 없다 (SoT §2.1).
            // 진열이 데이터로 들어오므로(D-041) 가격표에 무기 한 줄이 섞이는 것은 실제로 일어날 수 있다.
            // 막지 않으면 그 칸을 사는 순간 들고 있던 무기가 교체되어 사라진다.
            if (definition.Kind != EquipmentItemKind.Consumable
                && definition.Kind != EquipmentItemKind.Accessory)
            {
                return false;
            }

            // 소모품이 아닌데 묶음 수량이 붙으면 EquipmentInventory 가 항상 거절한다.
            // 여기서 막지 않으면 목록에는 보이는데 누르면 언제나 실패하는 칸이 된다.
            if (definition.Kind != EquipmentItemKind.Consumable && offer.Quantity != 1)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 이미 끼고 있는 장신구인가.
        ///
        /// <b>장신구는 교체가 곧 소멸이라</b>(S-GD-B7Q44N) 같은 것으로 바꾸면 들어온 것도 나간 것도 없다.
        /// 그런데 <c>TryPickup</c> 은 성공을 돌려주므로 그대로 두면 <b>골드만 나간다.</b>
        /// 환불이 없으므로(S-GD-PVVHP9 규칙 4) 도메인이 미리 막는다.
        ///
        /// <para>소모품은 해당 없다 — 같은 것을 여러 개 쌓는 것이 정상이다.</para>
        /// </summary>
        private static bool IsAlreadyOwned(
            IEquipmentInventoryDriver inventory, in EquipmentItemDefinition definition)
        {
            if (definition.Kind != EquipmentItemKind.Accessory)
            {
                return false;
            }

            return inventory != null
                && inventory.EquipmentSnapshot.AccessoryItemId == definition.ItemId;
        }

        private static bool IsConsumableInventoryFull(
            IEquipmentInventoryDriver inventory, in EquipmentItemDefinition definition)
        {
            if (inventory == null || definition.Kind != EquipmentItemKind.Consumable)
            {
                return false;
            }

            EquipmentInventorySnapshot snapshot = inventory.EquipmentSnapshot;
            for (int i = 0; i < EquipmentInventorySnapshot.ConsumableSlotCount; i++)
            {
                if (snapshot.GetConsumableSlot(i).IsEmpty) return false;
            }

            return true;
        }

        /// <summary>
        /// 이번 라운드의 축복 선택이 끝났는가 (S-GD-DP2DVC).
        ///
        /// <para>
        /// <b>진열과 구매가 이 판정 하나를 같이 쓴다.</b> 훈련 세션처럼 축복 선택 단계가 없는
        /// 모드는 <c>requiresBlessingSelection: false</c> 로 만들어져 항상 통과한다 —
        /// 거기서 <c>IsSelectionDone</c> 은 영원히 <c>false</c> 라 그대로 두면 상점이 통째로 잠긴다.
        /// </para>
        /// </summary>
        private bool IsSelectionSettled(int playerId) =>
            !requiresBlessingSelection || economy.IsSelectionDone(playerId);

        /// <summary>
        /// 이 칸을 <b>지금 한 번 사면</b> 그 물건의 보유 한도를 넘는가 (S-GD-FDHBNA).
        ///
        /// <para>
        /// <b>⚠️ <c>EquipmentInventory.AddConsumable</c> 과 같은 식을 쓴다</b> —
        /// <c>보유 + 묶음수량 &gt; 한도</c>. "보유 == 한도" 로만 보면 <b>묶음 판매에서 어긋난다</b>:
        /// 한도 3 에 2개를 들고 ×2 짜리 칸을 보면 진열은 "살 수 있다" 고 하는데 지급은
        /// <c>CarryLimitExceeded</c> 로 거절한다. 스펙이 예로 든 3/3 은 수량 1 짜리라 두 식이 같다.
        /// </para>
        ///
        /// <para>
        /// <b>소모품만 본다.</b> 인벤토리도 소모품 지급 경로에서만 한도를 재고, 장신구는
        /// <see cref="IsAlreadyOwned"/> 가 따로 막는다.
        /// </para>
        ///
        /// <para>
        /// 한도를 모르면 <c>false</c> — 한도 없는 물건이거나, 카탈로그가 한도를 제공하지 않는
        /// 종류다(<c>IEquipmentItemCarryLimitCatalog</c> 를 구현하지 않은 카탈로그). <b>모르는 것을
        /// "못 산다" 로 그리지 않는다.</b>
        /// </para>
        /// </summary>
        private bool WouldExceedCarryLimit(
            IEquipmentInventoryDriver inventory, in EquipmentItemDefinition definition, int quantity)
        {
            if (inventory == null || definition.Kind != EquipmentItemKind.Consumable)
            {
                return false;
            }

            if (!(items is IEquipmentItemCarryLimitCatalog limits)
                || !limits.TryGetCarryLimit(definition.ItemId, out int carryLimit)
                || carryLimit <= 0)
            {
                return false;
            }

            return CountConsumable(inventory.EquipmentSnapshot, definition.ItemId) + (long)quantity
                > carryLimit;
        }

        /// <summary>지금 몇 개를 들고 있는가. <b>칸이 아니라 개수를 센다</b> — 한 칸에 여러 개가 쌓인다.</summary>
        private static long CountConsumable(in EquipmentInventorySnapshot snapshot, int itemId)
        {
            long total = 0;

            for (int i = 0; i < EquipmentInventorySnapshot.ConsumableSlotCount; i++)
            {
                EquipmentInventorySlot slot = snapshot.GetConsumableSlot(i);

                if (slot.ItemId == itemId)
                {
                    total += slot.Count;
                }
            }

            return total;
        }

        /// <summary>인벤토리 실패를 화면이 읽을 수 있는 사유로 옮긴다.</summary>
        private static ShopPurchaseRejectReason Translate(EquipmentInventoryFailure failure)
        {
            switch (failure)
            {
                case EquipmentInventoryFailure.ConsumableCapacityFull:
                    return ShopPurchaseRejectReason.InventoryFull;

                // ⚠️ 위와 다르다. 저쪽은 "넣을 칸이 없다" 이고 이쪽은 "이 물건은 여기까지만 든다" 다
                //    (S-GD-FDHBNA). 소모품은 한 칸에 쌓이므로 한도에 닿아도 칸은 비어 있을 수 있어,
                //    InventoryFull 로 답하면 화면에 "가방이 가득 찼습니다" 라는 거짓 안내가 뜬다.
                case EquipmentInventoryFailure.CarryLimitExceeded:
                    return ShopPurchaseRejectReason.CarryLimitReached;

                // 진열이 잘못된 경우다 — 정의가 없거나, 소모품이 아닌데 묶음 수량이 붙어 있다.
                case EquipmentInventoryFailure.InvalidItem:
                case EquipmentInventoryFailure.InvalidQuantity:
                    return ShopPurchaseRejectReason.UnknownItem;

                // 전투자가 지금 다른 것을 하고 있다. 준비 단계에서도 대시·상호작용은 일어난다.
                case EquipmentInventoryFailure.Downed:
                case EquipmentInventoryFailure.Dashing:
                case EquipmentInventoryFailure.Attacking:
                case EquipmentInventoryFailure.Interacting:
                case EquipmentInventoryFailure.Stunned:
                    return ShopPurchaseRejectReason.Busy;

                default:
                    // SlotEmpty·SlotDoesNotContainConsumable·None 은 지급 경로에서 나올 수 없다.
                    // 그래도 값을 하나 골라야 하므로 "자리가 없다" 로 둔다 — 만에 하나 나오면
                    // 사용자가 실제로 해볼 수 있는 행동(자리 비우기)을 알려주는 쪽이 덜 해롭다.
                    //
                    // ⚠️ 이 가지에 **실제로 나오는 값이 떨어진 적이 있다.** CarryLimitExceeded 가
                    //    나중에 추가됐는데 여기 주석은 그것을 모른 채 남아 있었고, 보유 한도 초과가
                    //    조용히 InventoryFull 로 나가 거짓 안내가 떴다(2026-09-14 정정).
                    //    EquipmentInventoryFailure 에 값을 더하면 **이 switch 를 같이 본다.**
                    return ShopPurchaseRejectReason.InventoryFull;
            }
        }

        private bool IsParticipant(int playerId)
        {
            var ids = economy.PlayerIds;

            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == playerId)
                {
                    return true;
                }
            }

            return false;
        }

    }
}

namespace BettingRoyale.Data.Audio
{
    /// <summary>
    /// 사운드 재생 요청의 논리 식별자. AudioClip 을 직접 들고 다니는 대신 이 값을 주고받아
    /// 게임플레이·UI 가 Unity 오디오 에셋을 몰라도 되게 한다 (S-AUD-6MZMQM).
    /// </summary>
    /// <remarks>
    /// 실제 클립·믹서 그룹·동시 재생 수는 <c>SoundCatalog</c> 가 정한다.
    /// 값이 저장·복제 경계를 넘을 수 있으므로 기존 멤버의 숫자 값은 바꾸지 않고 뒤에만 추가한다.
    /// </remarks>
    public enum SoundId
    {
        None = 0,

        SwordAttack = 1,
        SwordHit = 2,
        BowFire = 3,
        ArrowHit = 4,
        ArrowExpire = 5,
        CommonHit = 6,
        Dash = 7,
        Downed = 8,
        Recover = 9,

        ReadyStart = 10,
        CombatStart = 11,
        RoundEnd = 12,
        FinalResult = 13,

        UIClick = 14,
        UISuccess = 15,
        UIFail = 16,

        TitleBgm = 17,
        LobbyBgm = 18,
        PreparationBgm = 19,
        CombatBgm = 20,
        TrainingBgm = 21,
        ResultBgm = 22,
        CountdownBgm = 23,
        CountdownTick = 24,
        ThrowRelease = 25,
        ThrowLand = 26,
        ThrowFuse = 27,
        ThrowExplosion = 28,
        SmokeActivate = 29,
        ChestOpening = 30,
        ChestOpened = 31,
        ChestCancelled = 32,
        BlessingAppear = 33,
        BlessingReroll = 34,
        BlessingLock = 35,
        BlessingSelect = 36,
        RouletteSpin = 37,
        RouletteBet = 38,
        RouletteWin = 39,
        RouletteLose = 40,
        UpgradeSuccess = 41,
        UpgradeFail = 42,
        ShopPurchase = 43,
        EquipmentChange = 44,
        ItemPickup = 45,
        ItemUse = 46,
        ReadyOn = 47,
        ReadyOff = 48,
        ZoneShrink = 49,
        ZoneGlobal = 50,
        RoomBgm = 51,
        CombatLateBgm = 52,
        ThirdRoundResultBgm = 53,
        FinalResultBgm = 54,
        ArrowFlight = 55,
        RoundGoldGrant = 56,
        ZoneDamage = 57,
        HealingPotion = 58,

    }
}

namespace BettingRoyale.Data.Training
{
    public enum TrainingActionKind
    {
        Sync = 0,
        GrantGold = 1,
        Purchase = 2,
        Upgrade = 3,
        SelectBlessing = 4,
        Heal = 5,
        Reset = 6,
        Travel = 7,
        SetPlayerDamage = 8,
        SelectWeapon = 9,

        // ── 경제형 축복 시험 주기 (훈련 전용) ──────────────────────────────
        // 값은 뒤에만 덧붙인다. 기존 번호를 옮기면 예전 클라이언트의 패킷이 다른 명령이 된다.

        /// <summary>시험 주기를 연다. Value 는 등급 라운드 1~3, Selection 은 순위 프로필이다.</summary>
        TrialBegin = 10,

        /// <summary>이자 폭발 예치. Value 는 보지 않는다 — 금액은 카탈로그가 정한다.</summary>
        TrialDeposit = 11,

        // 12 는 저점 매수의 옛 "위험 보급 도전" 이었다(2026-09-18 엑셀 개편으로 삭제, S-GD-TMPBEF).
        // 번호를 다시 쓰지 않는다 — 옛 클라이언트의 도전 요청이 다른 명령으로 읽힌다.

        /// <summary>시험 추첨의 유료 재추첨. Value 는 보지 않는다 — 비용은 설정이 정한다.</summary>
        TrialReroll = 13,

        /// <summary>시험 추첨 후보의 잠금 토글. Value 는 후보 번호다.</summary>
        TrialToggleLock = 14,

        /// <summary>시험 추첨 후보 확정. Value 는 후보 번호다.</summary>
        TrialSelect = 15,

        /// <summary>시험 준비를 닫고 전투로 넘어간다. Value 는 보지 않는다.</summary>
        TrialCombat = 16,

        /// <summary>시험 전투를 끝내고 정산한다. Value 는 0(탈락) 또는 1(생존) 이다.</summary>
        TrialSettle = 17,

        /// <summary>
        /// 룰렛 베팅·물리기. <b>Value 는 <c>RouletteRequestPayload</c> 로 눌러 담은 한 칸</b>이다 —
        /// 제품 경제 명령과 <b>같은 포장</b>을 쓴다. 훈련 전용 포맷을 따로 만들면 같은 요청을
        /// 두 가지 방법으로 풀게 되고, 한쪽만 고쳐서 조용히 어긋난다.
        /// </summary>
        Roulette = 18,

        /// <summary>
        /// 시험 준비 단계에서 블라인드 베팅을 수락한다.
        /// <para>
        /// 값이 19 인 것은 병합 결과다. 두 갈래가 각각 18 을 덧붙였는데, sprint3 에 이미 올라가
        /// 있는 <see cref="Roulette"/> 가 18 을 지킨다 — 이미 나간 클라이언트의 패킷이 다른
        /// 명령으로 읽히면 안 된다. 뒤늦게 들어온 이 값이 다음 번호를 받았다. 번호는 뒤에만 붙인다.
        /// </para>
        /// </summary>
        TrialBlindBet = 19,

        /// <summary>
        /// 훈련장 전투 봇을 <b>구역별로</b> 켜고 끈다. <c>Value</c> 는 켜 둘 구역의 조합이다 — S-GD-V9ZMN3.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 비트 0~3 이 훈련 NPC 번호 1~4(= 구역 <c>번호 − 1</c>)에 하나씩 대응한다. 해석·검사는
        /// <see cref="TrainingBotRule"/> 한 곳에서만 한다.
        /// </para>
        /// <para>
        /// ⚠️ <b>연습 더미(5~9번)는 건드리지 않는다.</b> 그쪽은 피해량을 재는 과녁이라 항상 서 있어야
        /// 하고, 이 명령이 다루는 것은 <b>싸우는 봇</b>뿐이다.
        /// </para>
        /// <para>
        /// 켜기·끄기를 따로 두지 않고 <b>원하는 조합</b> 하나로 보낸다. 토글 명령은 유실되면 값이
        /// 영원히 뒤집힌 채 남지만, 조합은 다음 한 건이 스스로 바로잡는다 — 대기방 봇이 명단 전체를
        /// 보내는 것과 같은 이유다(D-ONL-AM6C7M).
        /// </para>
        /// <para>
        /// 이 번호는 <b>개수를 보내던 옛 의미</b>(<c>SetBotCount</c>)를 쓴 적이 있으나, 그 판은
        /// sprint3 에 나가기 전에 이 뜻으로 바뀌었다. 받은 클라이언트가 세상에 없어 번호를 아낄 수 있었다.
        /// </para>
        /// </remarks>
        SetBotZones = 20,
    }
}

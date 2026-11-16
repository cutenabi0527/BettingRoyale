namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// <b>축복이 어느 무기 종류에 붙는가</b> 를 나타내는 규약 (S-GD-SB84DQ).
    /// 후보를 뽑을 때 <b>현재 든 무기와 호환되는 축복만</b> 대상이 된다.
    /// <see cref="Common"/> 은 모든 무기와 호환된다.
    ///
    /// <para>
    /// ⚠️ <b>이름이 같은 타입이 하나 더 있다. 헷갈리면 조용히 틀린다.</b>
    /// 저장소에 무기와 관련된 열거형이 <b>셋</b> 있고 각각 다른 질문에 답한다 —
    /// 검 한 자루를 예로 들면:
    /// </para>
    /// <list type="table">
    ///   <item>
    ///     <term><see cref="BettingRoyale.Data.Combat.WeaponType"/></term>
    ///     <description><b>무엇인가</b> — <c>Sword</c>. <b>무기 목록의 정본</b>이다. 애니메이션·전투·네트워크가 쓴다</description>
    ///   </item>
    ///   <item>
    ///     <term><see cref="BettingRoyale.Data.Combat.WeaponTag"/></term>
    ///     <description><b>어떤 성질인가</b> — <c>Melee | Sword</c>. 무기 에셋의 특성 태그다. 전투 판정이 쓴다</description>
    ///   </item>
    ///   <item>
    ///     <term>이 타입</term>
    ///     <description><b>누구에게 붙나</b> — 검 전용 축복이면 <c>Sword</c>. 축복 후보 필터가 쓴다</description>
    ///   </item>
    /// </list>
    ///
    /// <para>
    /// 셋을 하나로 합치려던 시도(<c>O-021</c>)는 <b>전제가 틀려서 철회됐다</b> —
    /// 서로 다른 질문에 답하므로 합칠 대상이 아니다. 각자 자리에 남는다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>새 무기를 추가하는 절차</b> (방패·단검·주먹 등):
    /// <list type="number">
    ///   <item><see cref="BettingRoyale.Data.Combat.WeaponType"/> 에 <b>먼저</b> 추가한다. 그쪽이 무기 목록의 정본이다</item>
    ///   <item>이 열거형에 <b>같은 이름</b>으로 멤버를 추가한다. 값은 <b>뒤에만 붙인다</b></item>
    ///   <item>축복 풀에 그 무기 전용 축복이 없어도 된다 — 공용만으로도 후보는 채워진다</item>
    /// </list>
    /// <b>기존 멤버의 숫자 값을 바꾸지 않는다.</b> 축복 정의는 외부 데이터(Excel → JSON → Binary, D-041)로
    /// 들어올 예정이라, 값이 밀리면 <b>이미 배포된 데이터의 태그가 통째로 어긋난다.</b>
    /// 중간에 끼워 넣지 말고 항상 뒤에 붙인다.
    /// </para>
    /// </summary>
    public enum WeaponTag
    {
        /// <summary>모든 무기와 호환된다.</summary>
        Common = 0,

        /// <summary>검 전용. <see cref="BettingRoyale.Data.Combat.WeaponType.Sword"/> 에 대응한다.</summary>
        Sword = 1,

        /// <summary>활 전용. <see cref="BettingRoyale.Data.Combat.WeaponType.Bow"/> 에 대응한다.</summary>
        Bow = 2,

        /// <summary>
        /// 둔기 전용. <see cref="BettingRoyale.Data.Combat.WeaponType.Mace"/> 에 대응한다.
        ///
        /// ⚠️ <b>둔기 무기 자체가 아직 구현되지 않았다</b> —
        /// <see cref="BettingRoyale.Data.Combat.WeaponCatalog.TryGetTags"/> 가 <c>Mace</c> 에 <c>false</c> 를 반환한다.
        /// 그래서 지금 이 태그를 가진 축복을 만들면 <b>아무에게도 뜨지 않는다.</b> 무기가 생긴 뒤에 만든다.
        /// </summary>
        Mace = 3,

        // ── 2층: 분류 ────────────────────────────────────────────────────────
        // ⚠️ 값이 무기별 태그(1~3)보다 뒤에 온다. 층 순서대로 재배열하고 싶어지는데,
        //    그게 D-041 이 금지하는 동작이다 — 3층이 먼저 만들어졌고 2층은 2026-09-01 에 정해졌다.
        //    "새 멤버는 항상 현재 최대값 + 1" 을 지킨 결과다.

        /// <summary>
        /// <b>근접 무기 전체</b>에 적용된다 (S-GD-DPWH9W).
        /// 어느 무기가 근접인지는 <see cref="BlessingWeaponTagCatalog"/> 의 표가 정한다.
        ///
        /// <b>같은 효과라도 무기 효율 격차가 크면 <see cref="Common"/> 대신 이것과
        /// <see cref="Ranged"/> 로 나눠 수치를 따로 준다</b> (S-GD-5QEWMQ).
        /// 판단 기준은 "적용 가능한가" 가 아니라 <b>"같은 수치를 줘도 공평한가"</b> 다.
        /// </summary>
        Melee = 4,

        /// <summary>
        /// <b>원거리 무기 전체</b>에 적용된다 (S-GD-DPWH9W).
        /// <see cref="Melee"/> 의 짝이다.
        /// </summary>
        Ranged = 5,
    }
}

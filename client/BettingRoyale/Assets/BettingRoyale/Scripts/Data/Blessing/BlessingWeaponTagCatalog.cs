using System.Collections.Generic;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 무기 하나와 그 무기의 <b>분류</b>를 짝지은 한 줄 (S-GD-DPWH9W).
    ///
    /// <b>쌍으로 두는 것이 요점이다.</b> 무기를 추가하면서 근접인지 원거리인지를 안 적는 것이
    /// 구조적으로 불가능해진다 — 분류 칸을 비우면 컴파일이 안 된다.
    /// 두 목록을 따로 관리하면 한쪽만 채워지고, 그 무기는 2층 축복을 <b>조용히</b> 못 받는다.
    /// </summary>
    public readonly struct WeaponCategoryEntry
    {
        /// <summary>무기별 태그. 3층이다.</summary>
        public readonly WeaponTag Weapon;

        /// <summary><see cref="WeaponTag.Melee"/> 또는 <see cref="WeaponTag.Ranged"/>. 2층이다.</summary>
        public readonly WeaponTag Category;

        public WeaponCategoryEntry(WeaponTag weapon, WeaponTag category)
        {
            Weapon = weapon;
            Category = category;
        }
    }

    /// <summary>
    /// <see cref="WeaponTag"/> 의 <b>규칙과 층 구조를 한 곳에 모은 표</b> (S-GD-SPV9FY, S-GD-DPWH9W).
    /// 전투 쪽 <c>Data.Combat.WeaponCatalog</c> 와 같은 자리이고 이름도 그 관례를 따랐다.
    ///
    /// <para>
    /// <b>태그는 3단 계층이다</b> — 축복은 이 중 하나를 요구한다:
    /// </para>
    /// <list type="number">
    ///   <item><b>공용</b> <see cref="WeaponTag.Common"/> — 어느 무기든</item>
    ///   <item><b>분류</b> <see cref="WeaponTag.Melee"/> · <see cref="WeaponTag.Ranged"/> — 근접·원거리 전체</item>
    ///   <item><b>무기별</b> <see cref="WeaponTag.Sword"/> · <see cref="WeaponTag.Bow"/> · <see cref="WeaponTag.Mace"/></item>
    /// </list>
    ///
    /// <para>
    /// <b>손으로 유지하는 것은 <see cref="WeaponCategories"/> 하나뿐이다.</b>
    /// <see cref="All"/>·<see cref="Equippable"/> 은 거기서 파생된다 —
    /// 목록을 여러 개 두면 반드시 한쪽만 갱신된다.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>분류는 전투 쪽에도 있다.</b> <c>Data.Combat.WeaponCatalog.TryGetTags</c> 가 검에
    /// <c>Melee | Sword</c> 를 준다. 같은 사실이 두 곳에 있는 셈인데, <b>참조로 합칠 수는 없다</b> —
    /// 전투 카탈로그는 <c>Mace</c> 에 <c>false</c> 를 돌려주므로(미구현) 둔기 분류가 거기엔 존재하지 않는다.
    /// 대신 <c>Scripts/Editor/WeaponTaxonomyConsistencyTests.cs</c> 가 두 표의 어긋남을 잡는다.
    /// </para>
    ///
    /// <para>
    /// <b>여기 없는 것: 화면 문구.</b> 한국어 라벨은 UI 소유다
    /// (<c>docs/client/ui-domain-contract.md</c> §10). 도메인은 <c>enum</c> 까지만 준다.
    /// </para>
    ///
    /// <para>
    /// <c>UnityEngine</c> 을 참조하지 않는다 (D-060). <b>리플렉션도 쓰지 않는다</b> —
    /// <c>Enum.IsDefined</c>·<c>enum.ToString()</c> 은 IL2CPP(D-042) 스트리핑에 취약하고 매 호출 박싱하며,
    /// 무엇보다 <b>미정의 값에 조용히 틀린 답을 준다</b>
    /// (<c>((WeaponTag)99).ToString()</c> 은 예외가 아니라 <c>"99"</c> 를 돌려준다).
    /// </para>
    /// </summary>
    public static class BlessingWeaponTagCatalog
    {
        // ⚠️ 새 무기를 추가하는 절차 (방패·단검·주먹 등, S-GD-SPV9FY):
        //    1. Data.Combat.WeaponType 에 먼저 추가한다 — 그쪽이 무기 목록의 정본이다.
        //    2. WeaponTag 에 같은 이름으로 멤버를 추가한다. 값은 현재 최대값 + 1 로 뒤에만 붙인다.
        //    3. 아래 표에 (무기, 분류) 한 줄을 추가한다.
        //    빠뜨리면 Scripts/Editor/BlessingWeaponTagCatalogTests.cs 가 실패한다.
        private static readonly WeaponCategoryEntry[] Entries =
        {
            new WeaponCategoryEntry(WeaponTag.Sword, WeaponTag.Melee),
            new WeaponCategoryEntry(WeaponTag.Bow, WeaponTag.Ranged),
            new WeaponCategoryEntry(WeaponTag.Mace, WeaponTag.Melee),
        };

        // ⚠️ 2층에 분류가 추가되면(예: 투척형 주무기 — S-GD-9W5RHS) 여기에도 넣는다.
        //    "근접이냐 아니냐" 를 bool 로 만들지 않은 이유가 이것이다. 2층은 둘로 닫히지 않는다.
        private static readonly WeaponTag[] Categories =
        {
            WeaponTag.Melee,
            WeaponTag.Ranged,
        };

        private static readonly WeaponTag[] AllTags = BuildAll();
        private static readonly WeaponTag[] EquippableTags = BuildEquippable();

        /// <summary>무기와 분류의 짝. <b>이 표만 손으로 유지한다.</b></summary>
        public static IReadOnlyList<WeaponCategoryEntry> WeaponCategories => Entries;

        /// <summary>2층 분류 태그. 축복이 요구할 수 있지만 장착되지는 않는다.</summary>
        public static IReadOnlyList<WeaponTag> CategoryTags => Categories;

        /// <summary>정의된 태그 전부. 세 층을 모두 포함한다.</summary>
        public static IReadOnlyList<WeaponTag> All => AllTags;

        /// <summary>
        /// 무기로 <b>장착될 수 있는</b> 태그 — 3층뿐이다.
        /// <see cref="WeaponTag.Common"/> 과 분류 태그는 빠진다. 축복이 갖는 값이지 플레이어가 드는 무기가 아니다.
        /// </summary>
        public static IReadOnlyList<WeaponTag> Equippable => EquippableTags;

        /// <summary>
        /// 정의된 태그인가. <c>(WeaponTag)99</c> 같은 값을 걸러낸다.
        /// 외부 데이터(D-041)에서 복원한 값은 <b>반드시 이것을 통과시킨 뒤</b> 쓴다.
        /// </summary>
        public static bool IsDefined(WeaponTag tag) => Contains(AllTags, tag);

        /// <summary>장착 무기로 쓸 수 있는 값인가. 공용과 분류 태그는 <c>false</c> 다.</summary>
        public static bool IsEquippable(WeaponTag tag) => Contains(EquippableTags, tag);

        /// <summary>2층 분류 태그인가.</summary>
        public static bool IsCategory(WeaponTag tag) => Contains(Categories, tag);

        /// <summary>
        /// 이 무기가 어느 분류에 속하는가. 3층 태그가 아니면 <c>false</c>.
        ///
        /// <b>반환값을 반드시 검사한다.</b> <c>out</c> 값을 그냥 쓰면 표에 없는 무기가
        /// <see cref="WeaponTag.Common"/>(0)으로 흘러 <b>모든 축복과 호환되는</b> 것처럼 보인다.
        /// </summary>
        public static bool TryGetCategory(WeaponTag weapon, out WeaponTag category)
        {
            for (int i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Weapon == weapon)
                {
                    category = Entries[i].Category;
                    return true;
                }
            }

            category = WeaponTag.Common;
            return false;
        }

        /// <summary>
        /// <b>호환 판정의 유일한 정본이다.</b> 축복이 요구하는 <paramref name="required"/> 를
        /// <paramref name="equippedPrimaryWeapon"/> 이 만족하는가.
        ///
        /// <para>세 층을 순서대로 본다:</para>
        /// <list type="number">
        ///   <item><see cref="WeaponTag.Common"/> 이면 무조건 통과</item>
        ///   <item>무기 자체와 같으면 통과 — 전용 축복</item>
        ///   <item>그 무기의 <b>분류</b>와 같으면 통과 — 근접·원거리 축복</item>
        /// </list>
        ///
        /// <para>
        /// ⚠️ <paramref name="equippedPrimaryWeapon"/> 은 <b>주무기</b>다 (S-GD-9W5RHS).
        /// 소모품으로 얻은 무기에는 공격형 축복이 적용되지 않는다. 그 구분은 아직 코드에 없으므로
        /// <b>이름과 이 주석이 유일한 계약</b>이다 — 소모품이 생기면 여기 넘기지 마라.
        /// </para>
        ///
        /// <para>
        /// 순수 술어다 — <b>던지지 않는다.</b> 필터 루프(<see cref="BlessingDraft"/>) 안에서 불리므로
        /// 여기서 예외를 던지면 호출자의 기대를 깬다. 값 검증은 값이 들어오는 <b>경계</b>에서 한다.
        /// </para>
        /// </summary>
        public static bool Accepts(WeaponTag required, WeaponTag equippedPrimaryWeapon)
        {
            if (required == WeaponTag.Common || required == equippedPrimaryWeapon)
            {
                return true;
            }

            return TryGetCategory(equippedPrimaryWeapon, out var category) && required == category;
        }

        private static bool Contains(WeaponTag[] source, WeaponTag tag)
        {
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] == tag)
                {
                    return true;
                }
            }

            return false;
        }

        private static WeaponTag[] BuildAll()
        {
            var all = new WeaponTag[1 + Categories.Length + Entries.Length];
            all[0] = WeaponTag.Common;

            for (int i = 0; i < Categories.Length; i++)
            {
                all[1 + i] = Categories[i];
            }

            for (int i = 0; i < Entries.Length; i++)
            {
                all[1 + Categories.Length + i] = Entries[i].Weapon;
            }

            return all;
        }

        private static WeaponTag[] BuildEquippable()
        {
            var equippable = new WeaponTag[Entries.Length];

            for (int i = 0; i < Entries.Length; i++)
            {
                equippable[i] = Entries[i].Weapon;
            }

            return equippable;
        }
    }
}

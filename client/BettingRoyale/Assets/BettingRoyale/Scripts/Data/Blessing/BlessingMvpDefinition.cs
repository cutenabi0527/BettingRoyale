using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 설명문 안에서 <b>스탯 토큰으로 그려질 구간</b> 하나. 저작이 정한 것을 그대로 옮긴 값이다.
    ///
    /// <para>
    /// <b>여기에 색도 아이콘도 마크업도 없다.</b> 구간과 계열 이름뿐이고, 그것을 어떤 색·아이콘으로
    /// 그릴지는 UI 의 몫이다. 데이터 계층은 <c>UnityEngine</c>·<c>TMPro</c> 를 참조하지 않으므로
    /// UI 의 enum 을 공유할 수 없다 — <see cref="Kind"/> 문자열이 두 계층 사이의 계약이다
    /// (생성기가 이름을 검증한다).
    /// </para>
    ///
    /// <para>
    /// 위치를 들고 있는 이유: 같은 숫자가 한 설명에 두 번 나올 수 있다("이동 속도 15%와 … 피해 15%").
    /// 값으로 다시 찾으면 어느 쪽이 어느 계열인지 흔들리지만, 구간은 흔들리지 않는다.
    /// <see cref="Start"/> 와 <see cref="Length"/> 는 C# 문자열 인덱스와 같은 UTF-16 코드 단위다.
    /// </para>
    /// </summary>
    public readonly struct BlessingStatToken
    {
        /// <param name="start">설명문 안의 시작 위치. 음수면 던진다.</param>
        /// <param name="length">토큰 길이. 0 이하면 던진다 — 빈 토큰은 그릴 것이 없다.</param>
        /// <param name="kind">계열 이름. 비어 있으면 던진다.</param>
        public BlessingStatToken(int start, int length, string kind)
        {
            if (start < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(start), start, "토큰 시작은 0 이상이어야 합니다.");
            }

            if (length <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), length, "토큰 길이는 1 이상이어야 합니다.");
            }

            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new ArgumentException("토큰 계열 이름이 비었습니다.", nameof(kind));
            }

            Start = start;
            Length = length;
            Kind = kind;
        }

        /// <summary>설명문 안의 시작 위치 (UTF-16 코드 단위).</summary>
        public int Start { get; }

        /// <summary>토큰 길이 (UTF-16 코드 단위). 단위 표기("m", "초")를 포함한다.</summary>
        public int Length { get; }

        /// <summary>계열 이름. UI 의 스탯 계열 enum 이름과 글자 그대로 같다.</summary>
        public string Kind { get; }

        public override string ToString() => $"{Kind}@{Start}+{Length}";
    }

    /// <summary>
    /// 같은 축복을 다시 얻거나 다시 발동했을 때 <b>무엇이 일어나는가</b>.
    ///
    /// <para>
    /// <b>이것이 설명문의 산문이 아니라 데이터인 이유.</b> "재사용 대기시간 8초" 같은 문장만으로는
    /// 두 번째 발동이 시간을 늘리는지, 세기를 더하는지, 아무 일도 없는지가 읽는 사람마다 달라진다.
    /// 런타임·UI·기획이 각자 다르게 구현하면 그 차이는 <b>버그로 보이지 않는다</b> — 그냥
    /// "체감이 이상하다" 로 끝난다. 저작이 한 값으로 못 박는다.
    /// </para>
    ///
    /// <para>이름은 CSV 의 <c>stack_rule</c> 칸과 글자 그대로 같다 (생성기가 검증한다).</para>
    /// </summary>
    public enum BlessingStackRule
    {
        /// <summary>겹치지 않는다. 이미 켜져 있으면 다시 켜도 그대로다.</summary>
        Unique = 0,

        /// <summary>다시 발동하면 남은 시간만 새로 채운다. 세기는 그대로다.</summary>
        Refresh = 1,

        /// <summary>상한까지 쌓인다. 상한은 수치가 정한다(예: <c>max_stacks</c>).</summary>
        Stack = 2,

        /// <summary>발동마다 따로 만들어지고 따로 만료된다.</summary>
        Independent = 3,
    }

    /// <summary>
    /// 효과가 <b>언제까지 가는가</b>. <see cref="BlessingStackRule"/> 과 짝이다.
    ///
    /// <para>이름은 CSV 의 <c>duration_kind</c> 칸과 글자 그대로 같다 (생성기가 검증한다).</para>
    /// </summary>
    public enum BlessingDurationKind
    {
        /// <summary>지속 시간이 없다. 그 순간에 끝난다.</summary>
        Instant = 0,

        /// <summary>수치가 정한 초만큼 간다.</summary>
        Timed = 1,

        /// <summary>조건이 성립하는 동안만 간다. 조건이 깨지면 즉시 사라진다.</summary>
        WhileCondition = 2,

        /// <summary>라운드 단위다. 라운드가 바뀌면 초기화된다.</summary>
        Round = 3,

        /// <summary>전투 내내 상시다.</summary>
        Permanent = 4,

        /// <summary>
        /// 플레이어가 <b>직접 눌러서</b> 켠다(키보드 Q · 게임패드 Y — S-GD-NJQ5NM). 켜진 뒤로는 수치가 정한 초만큼 간다.
        /// </summary>
        Active = 5,
    }

    /// <summary>
    /// MVP 축복 한 종류. <see cref="BlessingDefinition"/>(후보·필터가 쓰는 정본) 위에
    /// <b>게임플레이가 필요로 하는 것</b>만 얹는다 — 효과 키, 아이콘, 수치.
    ///
    /// <para>
    /// <b>여기에 로직이 없다.</b> 이 타입은 표의 한 줄일 뿐이고,
    /// <see cref="Effect"/> 를 보고 무엇을 할지는 공용 전투·경제 런타임이 정한다.
    /// 카탈로그와 런타임은 이 정의를 함께 읽어 설명·수치·효과 키를 일치시킨다.
    /// </para>
    ///
    /// <para>
    /// 수치는 <c>client/Balance/blessings.csv</c> 에서 생성된 <see cref="BlessingMvpData"/> 가 준다.
    /// <b>런타임은 CSV 를 읽지 않는다</b> — 파일 접근도 파싱도 없다.
    /// </para>
    ///
    /// <para>
    /// <b>기존 <see cref="BlessingEffectCatalog"/> 와 다른 물건이다.</b> 그쪽은 엑셀 <c>effects</c> 시트를
    /// 문자열 그대로 들고 있는 범용 표(D-041)고, 이쪽은 MVP 등급별 축복을 위해 <b>검증된 숫자만</b> 담은 좁은 표다.
    /// 데이터 파이프라인이 들어오면 이 타입이 먼저 사라진다.
    /// </para>
    ///
    /// <para><c>UnityEngine</c> 을 참조하지 않는다 (D-060). C# 9 로 컴파일된다.</para>
    /// </summary>
    public sealed class BlessingMvpDefinition
    {
        private readonly Dictionary<string, float> numbers;

        /// <summary>
        /// 숫자가 아닌 파라미터 — <c>invuln_kind=ATTACKABLE</c>·<c>compare_mode=HP_RATIO</c> 처럼
        /// <b>동작을 고르는 이름</b>이다.
        /// </summary>
        /// <remarks>
        /// 숫자 사전과 따로 두는 이유: 이것들은 계산에 들어가지 않고 <b>분기</b>에 쓰인다. 같은 사전에 넣으려면
        /// 이름마다 숫자 코드를 정해야 하고, 그 대응표가 엑셀 밖에 생기는 순간 기획이 값을 바꿔도 코드가 모른다.
        /// </remarks>
        private readonly Dictionary<string, string> texts;

        /// <summary>
        /// <b>최종 아이콘 아트가 아직 없다</b>는 뜻의 아이콘 번호.
        ///
        /// <para>
        /// 아틀라스 칸을 아무거나 빌려 쓰지 않는 이유: 그러면 서로 다른 축복이 같은 그림으로 뜨면서도
        /// <b>아트가 준비된 것처럼 보인다.</b> 나중에 어느 칸이 진짜인지 아무도 구분하지 못하고,
        /// "아이콘이 겹친다" 는 버그 제보만 남는다. 없음을 값 하나로 못 박아 화면이 공용 '?' 로
        /// 그리게 한다 (<c>Scripts/UI/MatchHudAssets.cs</c>).
        /// </para>
        ///
        /// <para>⚠️ 임시다. 아트가 들어오면 CSV 의 <c>icon_index</c> 를 채우고 다시 생성한다.</para>
        /// </summary>
        public const int MissingIconIndex = -1;

        /// <param name="definition">후보 필터·UI 가 쓰는 정본 정의. <c>null</c> 이면 던진다.</param>
        /// <param name="effect">
        /// 이 축복이 어떤 동작을 요구하는지 나타내는 안정된 키 (<c>overheat</c>, <c>counter</c> …).
        /// <b>등급 접미사가 없다</b> — 같은 효과의 다른 등급은 같은 키를 공유한다.
        /// </param>
        /// <param name="iconIndex">아이콘 아틀라스 인덱스. <see cref="MissingIconIndex"/>면 최종 아트가 없어 공용 '?'로 표시한다.</param>
        /// <param name="stackRule">같은 축복이 다시 발동될 때의 중첩·갱신 규칙.</param>
        /// <param name="durationKind">효과가 지속되는 단위.</param>
        /// <param name="parameterKeys">수치 이름. <paramref name="parameterValues"/> 와 길이가 같아야 한다.</param>
        /// <param name="parameterValues">수치 값. 생성기가 유한한 숫자임을 이미 보장한다.</param>
        /// <param name="statTokens">
        /// 설명문에서 스탯 토큰으로 그릴 구간. 설명문 순서대로 오름차순이고 서로 겹치지 않아야 한다.
        /// <c>null</c> 이면 빈 목록이 되고, UI 는 예전처럼 추론에 기댄다.
        /// </param>
        public BlessingMvpDefinition(
            BlessingDefinition definition,
            string effect,
            int iconIndex,
            BlessingStackRule stackRule,
            BlessingDurationKind durationKind,
            IReadOnlyList<string> parameterKeys,
            IReadOnlyList<float> parameterValues,
            IReadOnlyList<BlessingStatToken> statTokens = null,
            IReadOnlyList<KeyValuePair<string, string>> parameterTexts = null)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (string.IsNullOrWhiteSpace(effect))
            {
                throw new ArgumentException($"{definition.Id} 의 효과 키가 비었습니다.", nameof(effect));
            }

            if (iconIndex < MissingIconIndex)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(iconIndex), iconIndex,
                    $"{definition.Id} 의 아이콘 인덱스는 0 이상이거나 최종 아트 없음({MissingIconIndex})이어야 합니다.");
            }

            if (!IsDefined(stackRule))
            {
                throw new ArgumentOutOfRangeException(nameof(stackRule), stackRule,
                    $"{definition.Id} 의 중첩 규칙이 정의되지 않았습니다.");
            }

            if (!IsDefined(durationKind))
            {
                throw new ArgumentOutOfRangeException(nameof(durationKind), durationKind,
                    $"{definition.Id} 의 지속 규칙이 정의되지 않았습니다.");
            }

            if ((durationKind == BlessingDurationKind.Active) != definition.IsActive)
            {
                // 뽑기는 정의의 IsActive 로, 전투는 이 지속 규칙으로 액티브를 알아본다. 둘이 갈리면
                // 한 번에 하나 제한(S-GD-NJQ5NM)이 조용히 풀리거나 누를 수 없는 축복이 된다.
                throw new ArgumentException(
                    $"{definition.Id} 의 지속 규칙({durationKind})과 액티브 표시({definition.IsActive})가 맞지 않습니다.",
                    nameof(definition));
            }

            if (parameterKeys == null)
            {
                throw new ArgumentNullException(nameof(parameterKeys));
            }

            if (parameterValues == null)
            {
                throw new ArgumentNullException(nameof(parameterValues));
            }

            if (parameterKeys.Count != parameterValues.Count)
            {
                throw new ArgumentException(
                    $"{definition.Id} 의 수치 이름 {parameterKeys.Count}개와 값 {parameterValues.Count}개가 맞지 않습니다.",
                    nameof(parameterValues));
            }

            // 들어온 목록을 그대로 들고 있지 않는다 — 호출자가 배열을 계속 쥐고 있으면
            // 카탈로그가 만들어진 뒤에도 수치를 바꿀 수 있다. 복사해서 끊는다.
            numbers = new Dictionary<string, float>(parameterKeys.Count, StringComparer.Ordinal);

            for (int i = 0; i < parameterKeys.Count; i++)
            {
                var key = parameterKeys[i];

                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new ArgumentException(
                        $"{definition.Id} 의 {i}번째 수치 이름이 비었습니다.", nameof(parameterKeys));
                }

                if (numbers.ContainsKey(key))
                {
                    throw new ArgumentException(
                        $"{definition.Id} 의 수치 '{key}' 가 중복됩니다.", nameof(parameterKeys));
                }

                var value = parameterValues[i];

                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    // NaN 은 모든 비교에서 false 를 내므로, 여기서 막지 않으면
                    // "조건이 영원히 성립하지 않는 축복" 이 아무 경고 없이 만들어진다.
                    throw new ArgumentOutOfRangeException(
                        nameof(parameterValues), value,
                        $"{definition.Id}.{key} 가 유한한 숫자가 아닙니다.");
                }

                numbers.Add(key, value);
            }

            texts = new Dictionary<string, string>(StringComparer.Ordinal);

            for (int i = 0; i < (parameterTexts?.Count ?? 0); i++)
            {
                KeyValuePair<string, string> pair = parameterTexts[i];

                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    throw new ArgumentException(
                        $"{definition.Id} 의 {i}번째 글자 수치 이름이 비었습니다.", nameof(parameterTexts));
                }

                if (texts.ContainsKey(pair.Key))
                {
                    throw new ArgumentException(
                        $"{definition.Id} 의 글자 수치 '{pair.Key}' 가 중복됩니다.", nameof(parameterTexts));
                }

                texts.Add(pair.Key, pair.Value ?? string.Empty);
            }

            Definition = definition;
            Effect = effect;
            IconIndex = iconIndex;
            StackRule = stackRule;
            DurationKind = durationKind;
            StatTokens = CopyTokens(definition, statTokens);
        }

        /// <summary>
        /// 기존 호출자 호환용 생성자. 메타데이터가 없던 테스트·외부 표는 순간·단일 효과로 취급한다.
        /// 새 CSV 생성물은 아래 메타데이터 포함 생성자를 사용한다.
        /// </summary>
        public BlessingMvpDefinition(
            BlessingDefinition definition,
            string effect,
            int iconIndex,
            IReadOnlyList<string> parameterKeys,
            IReadOnlyList<float> parameterValues,
            IReadOnlyList<BlessingStatToken> statTokens = null)
            : this(definition, effect, iconIndex, BlessingStackRule.Unique, BlessingDurationKind.Instant,
                parameterKeys, parameterValues, statTokens)
        {
        }

        private static bool IsDefined(BlessingStackRule value) =>
            value >= BlessingStackRule.Unique && value <= BlessingStackRule.Independent;

        private static bool IsDefined(BlessingDurationKind value) =>
            value >= BlessingDurationKind.Instant && value <= BlessingDurationKind.Active;

        /// <summary>
        /// 들어온 구간을 검사하고 **읽기 전용 복사본**으로 끊는다.
        ///
        /// <para>
        /// 겹치거나 뒤로 가는 구간은 여기서 막는다. 그대로 두면 UI 가 같은 글자를 두 번 그리거나
        /// 문자열 밖을 잘라 예외를 던진다 — 카드 한 장이 통째로 사라지는 실패다.
        /// </para>
        /// </summary>
        private static ReadOnlyCollection<BlessingStatToken> CopyTokens(
            BlessingDefinition definition, IReadOnlyList<BlessingStatToken> statTokens)
        {
            var count = statTokens?.Count ?? 0;
            var copy = new BlessingStatToken[count];
            var descriptionLength = (definition.Description ?? string.Empty).Length;
            var cursor = 0;

            for (int i = 0; i < count; i++)
            {
                var token = statTokens[i];

                if (string.IsNullOrWhiteSpace(token.Kind) || token.Length <= 0 || token.Start < 0)
                {
                    throw new ArgumentException(
                        $"{definition.Id} 의 {i}번째 스탯 토큰이 비었습니다.", nameof(statTokens));
                }

                if (token.Start < cursor)
                {
                    throw new ArgumentException(
                        $"{definition.Id} 의 스탯 토큰이 설명문 순서대로 있지 않거나 겹칩니다 ({token}).",
                        nameof(statTokens));
                }

                if (token.Start > descriptionLength || token.Length > descriptionLength - token.Start)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(statTokens), token,
                        $"{definition.Id} 의 스탯 토큰이 설명문({descriptionLength}자) 밖을 가리킵니다.");
                }

                cursor = token.Start + token.Length;
                copy[i] = token;
            }

            return new ReadOnlyCollection<BlessingStatToken>(copy);
        }

        /// <summary>후보 추첨·무기 호환·UI 표시에 쓰는 정본 정의.</summary>
        public BlessingDefinition Definition { get; }

        /// <summary>
        /// 동작 키. <b>등급이 아니라 효과를 가리킨다</b> —
        /// <c>overheat-t2</c> 와 (나중에 붙을) <c>overheat-t3</c> 는 둘 다 <c>overheat</c> 다.
        /// </summary>
        public string Effect { get; }

        /// <summary>아이콘 아틀라스 인덱스. 실제 아틀라스 연결은 UI 쪽 몫이다.</summary>
        public int IconIndex { get; }

        /// <summary>재발동 시 중첩·갱신 규칙.</summary>
        public BlessingStackRule StackRule { get; }

        /// <summary>효과의 지속 단위.</summary>
        public BlessingDurationKind DurationKind { get; }

        /// <summary>
        /// 설명문에서 스탯 토큰으로 그릴 구간. 설명문 순서대로이고 비어 있을 수 있다.
        ///
        /// <para>
        /// <b>바깥에서 바꿀 수 없다.</b> 생성기가 준 배열을 복사해 읽기 전용으로 감쌌다.
        /// </para>
        /// </summary>
        public IReadOnlyList<BlessingStatToken> StatTokens { get; }

        /// <summary>
        /// 수치 하나를 읽는다.
        ///
        /// <para>
        /// <b>없으면 던진다.</b> 0 을 돌려주면 "쿨다운 0초" 나 "피해 0" 같은 값이 조용히 흘러가
        /// 오타 한 글자가 밸런스 버그로 둔갑한다. 키 집합은 생성기가 효과별로 고정해 두었으므로
        /// 여기서 터지는 것은 곧 코드와 CSV 가 어긋났다는 뜻이다.
        /// </para>
        /// </summary>
        public float Number(string key)
        {
            if (key != null && numbers.TryGetValue(key, out var value))
            {
                return value;
            }

            throw new KeyNotFoundException(
                $"{Definition.Id}({Effect}) 에 수치 '{key}' 가 없습니다. 있는 수치: {string.Join(", ", SortedKeys())}");
        }

        /// <summary>수치가 있으면 꺼낸다. 엑셀 전용 축복처럼 키가 빠질 수 있는 곳에서 쓴다.</summary>
        public bool TryNumber(string key, out float value)
        {
            value = 0f;
            return key != null && numbers.TryGetValue(key, out value);
        }

        /// <summary>
        /// 글자 파라미터를 꺼낸다(<c>compare_mode</c>·<c>invuln_kind</c>·<c>mark_target</c>).
        /// <b>없으면 <c>false</c></b> — 기본 동작을 고르는 것은 읽는 쪽 몫이다.
        /// </summary>
        public bool TryText(string key, out string value)
        {
            value = null;
            return key != null && texts.TryGetValue(key, out value);
        }

        /// <summary>코드가 읽을 수 있는 글자 파라미터 이름 전부.</summary>
        internal IEnumerable<string> TextKeys => texts.Keys;

        public override string ToString() => $"{Definition.Id}({Effect}/#{IconIndex})";

        /// <summary>
        /// 같은 축복을 <b>수치만 바꿔</b> 새로 만든다. 키 집합·효과·아이콘·설명·토큰은 그대로다
        /// (<see cref="BlessingMvpTuning"/> 가 엑셀 수치를 입힐 때 쓴다).
        /// </summary>
        /// <param name="replacements">코드 키 → 새 값. 여기 없는 키는 원래 값을 유지한다.</param>
        /// <remarks>
        /// <b>키를 늘리거나 줄이지 않는다.</b> <see cref="Number"/> 가 없는 키에 던지는 계약은
        /// "코드가 읽는 키가 전부 있다" 를 보장하는 안전망이라, 엑셀에 키가 빠졌다고 그 키를 지우면
        /// 전투 중에 예외가 난다.
        /// </remarks>
        internal BlessingMvpDefinition WithNumbers(IReadOnlyDictionary<string, float> replacements) =>
            WithNumbersAndTexts(replacements, null);

        /// <summary>
        /// 수치를 바꾸고 엑셀의 <b>글자 파라미터</b>(<c>heal_basis=max_health</c> 같은 이름값)를 더한다.
        /// 수치 키는 늘리거나 줄이지 않는다(<see cref="WithNumbers"/> 계약). 글자 키는 엑셀 것이 코드 것을 덮는다 —
        /// 그래야 코드 목록 축복도 엑셀에서 동작 이름값을 바꿀 수 있다 (S-GD-Q75D75 후속).
        /// </summary>
        internal BlessingMvpDefinition WithNumbersAndTexts(
            IReadOnlyDictionary<string, float> replacements, IReadOnlyDictionary<string, string> textReplacements)
        {
            var keys = new string[numbers.Count];
            var values = new float[numbers.Count];
            var i = 0;

            foreach (var pair in numbers)
            {
                keys[i] = pair.Key;
                values[i] = replacements != null && replacements.TryGetValue(pair.Key, out var value)
                    ? value
                    : pair.Value;
                i++;
            }

            var carried = new List<KeyValuePair<string, string>>(texts.Count);
            foreach (var pair in texts)
            {
                if (textReplacements != null && textReplacements.ContainsKey(pair.Key)) continue;
                carried.Add(pair);
            }

            if (textReplacements != null)
            {
                foreach (var pair in textReplacements)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || numbers.ContainsKey(pair.Key)) continue;
                    carried.Add(new KeyValuePair<string, string>(pair.Key, pair.Value ?? string.Empty));
                }
            }

            return new BlessingMvpDefinition(
                Definition, Effect, IconIndex, StackRule, DurationKind, keys, values, StatTokens, carried);
        }

        /// <summary>코드가 읽는 수치 이름 전부.</summary>
        internal IEnumerable<string> NumberKeys => numbers.Keys;

        private string[] SortedKeys()
        {
            var keys = new string[numbers.Count];
            numbers.Keys.CopyTo(keys, 0);
            Array.Sort(keys, StringComparer.Ordinal);
            return keys;
        }
    }
}

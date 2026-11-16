using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace BettingRoyale.Data.Online.Lobby
{
    /// <summary>
    /// 로비 목록의 방 한 칸을 UI 로 옮기기 위한 읽기 전용 스냅샷 (S-UX-BXXTHX).
    /// 네트워크 타입(Fusion 의 SessionInfo 등)을 UI 로 흘려보내지 않기 위한 경계 모델이며,
    /// 이 파일은 UnityEngine 을 참조하지 않는다 (D-058, 순수 도메인 테스트 대상).
    /// </summary>
    public readonly struct LobbyRoomView
    {
        // 문자열은 필드로 들고 getter 에서 null 을 막는다.
        // default(LobbyRoomView) 는 생성자를 거치지 않으므로, 자동 속성만 쓰면 null 이 UI 로 새어 나간다.
        private readonly string _code;
        private readonly string _title;
        private readonly string _hostName;

        public LobbyRoomView(
            string code,
            string title,
            string hostName,
            int playerCount,
            int maxPlayers,
            bool isJoinable,
            bool hasPassword)
        {
            // 원격 방 ID(Code)는 어댑터가 준 값을 그대로 보존한다. 대소문자를 바꾸면 재참가가 깨진다.
            _code = code ?? string.Empty;
            _title = title ?? string.Empty;
            _hostName = hostName ?? string.Empty;
            PlayerCount = playerCount;
            MaxPlayers = maxPlayers;
            IsJoinable = isJoinable;
            HasPassword = hasPassword;
        }

        /// <summary>원격 세션 식별자. 표시와 코드 입장 양쪽에 쓰이며 절대 가공하지 않는다.</summary>
        public string Code => _code ?? string.Empty;

        public string Title => _title ?? string.Empty;

        public string HostName => _hostName ?? string.Empty;

        public int PlayerCount { get; }

        public int MaxPlayers { get; }

        /// <summary>어댑터가 판단한 참가 가능 여부(열린 세션 · 진행 중 아님).</summary>
        public bool IsJoinable { get; }

        public bool HasPassword { get; }

        public bool IsFull => MaxPlayers > 0 && PlayerCount >= MaxPlayers;

        public override string ToString()
        {
            return string.Concat(
                "LobbyRoomView(Code='", Code,
                "', Title='", Title,
                "', Host='", HostName,
                "', Players=", PlayerCount.ToString(CultureInfo.InvariantCulture),
                "/", MaxPlayers.ToString(CultureInfo.InvariantCulture),
                ", Joinable=", IsJoinable ? "true" : "false",
                ", HasPassword=", HasPassword ? "true" : "false",
                ")");
        }
    }

    /// <summary>대기방 슬롯 한 칸. 준비 상태와 방장/로컬 여부만 UI 로 전달한다.</summary>
    public readonly struct LobbyPlayerView
    {
        private readonly string _name;

        private readonly string _portraitId;

        public LobbyPlayerView(int id, string name, bool isHost, bool isLocal, bool isReady,
                string portraitId = null)
        {
            Id = id;
            _name = name ?? string.Empty;
            IsHost = isHost;
            IsLocal = isLocal;
            IsReady = isReady;
            _portraitId = portraitId ?? string.Empty;
        }

        /// <summary>세션 안에서 안정적인 불투명 식별자. 네트워크 어댑터가 PlayerRef 등에서 매핑한다.</summary>
        public int Id { get; }

        public string Name => _name ?? string.Empty;

        public bool IsHost { get; }

        public bool IsLocal { get; }

        public bool IsReady { get; }

        /// <summary>
        /// 이 참가자의 프로필 초상화 ID. 아직 모르면 빈 문자열이고 화면이 기본값으로 떨어뜨린다.
        /// </summary>
        /// <remarks>
        /// ⚠️ <b>화면이 제 임의로 계산하지 않는다.</b> 예전에는 <c>Id % 초상화수</c> 로 만들고 본인 것만
        /// 로컬 선택으로 덮어써서, 같은 사람이 클라이언트마다 다른 얼굴로 보였다. 이 값은
        /// <c>PlayerInfo</c> 가 복제하므로 전원이 같은 것을 본다.
        /// </remarks>
        public string PortraitId => _portraitId ?? string.Empty;

        public override string ToString()
        {
            return string.Concat(
                "LobbyPlayerView(Id=", Id.ToString(CultureInfo.InvariantCulture),
                ", Name='", Name,
                "', Host=", IsHost ? "true" : "false",
                ", Local=", IsLocal ? "true" : "false",
                ", Ready=", IsReady ? "true" : "false",
                ")");
        }
    }

    /// <summary>방 찾기 화면의 정렬 기준. 팀전 등 존재하지 않는 축은 넣지 않는다.</summary>
    public enum LobbyRoomSort
    {
        Title = 0,
        MostPlayers = 1,
        LeastPlayers = 2,
    }

    /// <summary>입력 검증 실패 사유. 사용자 문구는 UI 계층이 안전한 한국어로 매핑한다.</summary>
    public enum LobbyValidationError
    {
        None = 0,

        /// <summary>필수 입력이 비어 있다(공백만 입력 포함).</summary>
        Required = 1,

        /// <summary>허용 최소 길이 미만.</summary>
        TooShort = 2,

        /// <summary>허용 최대 길이(또는 UTF-8 바이트) 초과.</summary>
        TooLong = 3,

        /// <summary>제어 문자 등 허용되지 않는 문자가 포함됐다.</summary>
        InvalidCharacters = 4,

        /// <summary>정원이 2·4·6·8 이 아니다.</summary>
        InvalidCapacity = 5,
    }

    /// <summary>
    /// 검증을 통과한 방 생성 입력. 마지막 wizard 단계에서만 만들어진다.
    /// 비밀번호 원문은 <see cref="ToString"/> 에 절대 노출하지 않는다(S-UX-BXXTHX 로그 금지 조항).
    /// </summary>
    public readonly struct LobbyCreateRoomDetails
    {
        private readonly string _title;
        private readonly string _password;

        internal LobbyCreateRoomDetails(string title, int maxPlayers, bool isPublic, string password)
        {
            _title = title ?? string.Empty;
            MaxPlayers = maxPlayers;
            IsPublic = isPublic;
            _password = password ?? string.Empty;
        }

        public string Title => _title ?? string.Empty;

        public int MaxPlayers { get; }

        public bool IsPublic { get; }

        /// <summary>비밀번호 원문. 없으면 빈 문자열. 로그·세션 속성·파일에 쓰지 않는다.</summary>
        public string Password => _password ?? string.Empty;

        public bool HasPassword => Password.Length > 0;

        /// <summary>비밀번호를 포함하지 않는 진단 문자열.</summary>
        public override string ToString()
        {
            return string.Concat(
                "LobbyCreateRoomDetails(Title='", Title,
                "', MaxPlayers=", MaxPlayers.ToString(CultureInfo.InvariantCulture),
                ", IsPublic=", IsPublic ? "true" : "false",
                ", HasPassword=", HasPassword ? "true" : "false",
                ")");
        }
    }

    /// <summary>
    /// 로비 플로우의 순수 규칙: 입력 검증, 목록 검색/필터/정렬, 랜덤 참가 후보 판정.
    ///
    /// 규칙 근거: 개인전 · 맵 1개 · 3라운드 고정, 정원 상한 8명(S-GD-EZ682A).
    /// 시작 가능 판정은 NetworkManager 가 권위적으로 계산해 스냅샷의 CanStart 로 내려주며,
    /// UI 는 그 값만 쓴다(여기서 중복 계산하지 않는다).
    /// 이 타입은 UnityEngine · Fusion 을 참조하지 않으며 네트워크 호출도 하지 않는다.
    /// </summary>
    public static class LobbyRoomRules
    {
        /// <summary>방 제목 최소 길이(텍스트 요소 기준).</summary>
        public const int MinTitleLength = 1;

        /// <summary>방 제목 최대 길이. 한글/이모지를 1칸으로 세기 위해 텍스트 요소(grapheme)로 센다.</summary>
        public const int MaxTitleLength = 16;

        /// <summary>방 제목 UTF-8 바이트 상한. 전송 계층에서 잘리지 않도록 실제 와이어 상한과 맞춘다.</summary>
        public const int MaxTitleUtf8Bytes = 64;

        /// <summary>비밀번호 최대 길이(텍스트 요소 기준).</summary>
        public const int MaxPasswordLength = 16;

        /// <summary>비밀번호 UTF-8 바이트 상한. 네트워크 속성 길이 제한과 맞춘다.</summary>
        public const int MaxPasswordUtf8Bytes = 64;

        /// <summary>직접 입력하는 방 코드의 최소 길이.</summary>
        public const int MinRoomCodeLength = 4;

        /// <summary>직접 입력하는 방 코드의 최대 길이. 36자 GUID 형태의 레거시 세션 이름까지 받는다.</summary>
        public const int MaxRoomCodeLength = 64;

        /// <summary>시작에 필요한 최소 인원.</summary>
        public const int MinPlayersToStart = 2;

        /// <summary>온라인 운영 정원. 8명 복구 시 이 값만 변경한다(S-GD-FYWEYZ).</summary>
        /// <remarks>
        /// ⚠️ 2026-09-22 8인 실측: 클라가 예측 천장(<c>worstGap −64</c>)에 박혀 <b>소유자 입력의 36%가
        /// 버려졌다</b>(호스트 <c>inputHit 413 / inputMiss 229</c>). 상태 수신 주기가 계약값 1/32초의
        /// 5.5~6배(0.17~0.19초)였다. 원인은 S15P21A606-275 (B) 로 미해결이다.
        /// 그쪽이 풀리기 전에는 이 값을 올리지 않는다. 측정 구성은 S-QA-4XJKF7 이다.
        /// </remarks>
        public const int MaxCapacity = 6;

        /// <summary>기본 정원은 현재 운영 상한을 따른다.</summary>
        public const int DefaultCapacity = MaxCapacity;

        private static readonly int[] CapacityValues = BuildCapacityValues();

        private static int[] BuildCapacityValues()
        {
            var values = new int[MaxCapacity / 2];
            for (int i = 0; i < values.Length; i++) values[i] = (i + 1) * 2;
            return values;
        }

        /// <summary>오래된 프리팹이나 직접 호출도 운영 상한을 넘지 못하게 한다.</summary>
        public static int ResolveCapacity(int? requested) =>
            Math.Max(MinPlayersToStart, Math.Min(requested ?? DefaultCapacity, MaxCapacity));

        private static readonly ReadOnlyCollection<int> CapacityOptionsView =
            new ReadOnlyCollection<int>(CapacityValues);

        /// <summary>게임 설정 단계에서 고를 수 있는 정원 목록(2·4·6·8).</summary>
        public static IReadOnlyList<int> CapacityOptions => CapacityOptionsView;

        /// <summary>정원이 허용된 값(2·4·6·8)인지 검사한다.</summary>
        public static bool IsValidCapacity(int maxPlayers)
        {
            for (int i = 0; i < CapacityValues.Length; i++)
            {
                if (CapacityValues[i] == maxPlayers)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 방 제목을 검증한다. 앞뒤 공백을 제거한 뒤 1~16 텍스트 요소이면서 UTF-8 64 바이트 이하여야 하고
        /// 제어 문자·쌍이 맞지 않는 서로게이트를 포함할 수 없다(인코딩 시 잘리거나 치환되는 입력을 미리 막는다).
        /// 입력 문자열은 변경하지 않으며 정규화 결과를 <paramref name="title"/> 로 돌려준다.
        /// </summary>
        public static bool TryNormalizeRoomTitle(string raw, out string title, out LobbyValidationError error)
        {
            title = string.Empty;

            string trimmed = raw == null ? string.Empty : raw.Trim();
            if (trimmed.Length == 0)
            {
                error = LobbyValidationError.Required;
                return false;
            }

            if (ContainsControlCharacter(trimmed) || ContainsUnpairedSurrogate(trimmed))
            {
                error = LobbyValidationError.InvalidCharacters;
                return false;
            }

            int length = CountTextElements(trimmed);
            if (length < MinTitleLength)
            {
                error = LobbyValidationError.TooShort;
                return false;
            }

            if (length > MaxTitleLength)
            {
                error = LobbyValidationError.TooLong;
                return false;
            }

            // 결합 문자를 많이 붙이면 16 글자라도 와이어 상한을 넘길 수 있다.
            if (Encoding.UTF8.GetByteCount(trimmed) > MaxTitleUtf8Bytes)
            {
                error = LobbyValidationError.TooLong;
                return false;
            }

            title = trimmed;
            error = LobbyValidationError.None;
            return true;
        }

        /// <summary>
        /// 선택적 비밀번호를 검증한다. null/빈 문자열은 "비밀번호 없음" 으로 통과한다.
        /// 비밀번호는 <b>trim 하지 않는다</b> — 공백도 사용자가 입력한 유효한 문자이기 때문이다.
        /// 최대 16 텍스트 요소이면서 UTF-8 64 바이트 이하, 제어 문자 금지.
        /// </summary>
        public static bool TryNormalizePassword(string raw, out string password, out LobbyValidationError error)
        {
            password = string.Empty;

            if (string.IsNullOrEmpty(raw))
            {
                error = LobbyValidationError.None;
                return true;
            }

            if (ContainsControlCharacter(raw) || ContainsUnpairedSurrogate(raw))
            {
                error = LobbyValidationError.InvalidCharacters;
                return false;
            }

            if (CountTextElements(raw) > MaxPasswordLength)
            {
                error = LobbyValidationError.TooLong;
                return false;
            }

            if (Encoding.UTF8.GetByteCount(raw) > MaxPasswordUtf8Bytes)
            {
                error = LobbyValidationError.TooLong;
                return false;
            }

            password = raw;
            error = LobbyValidationError.None;
            return true;
        }

        /// <summary>
        /// 사용자가 직접 입력한 방 코드를 검증한다. 앞뒤 공백만 제거하고 4~64자의 ASCII 영숫자·하이픈만 허용한다
        /// (36자 GUID 형태의 레거시 세션 이름도 그대로 통과한다).
        /// <b>대소문자를 바꾸지 않는다</b>: 원격 세션 이름은 대소문자를 구분할 수 있어, 임의로 올리면 입장이 실패한다.
        /// </summary>
        public static bool TryNormalizeTypedRoomCode(string raw, out string code, out LobbyValidationError error)
        {
            code = string.Empty;

            string trimmed = raw == null ? string.Empty : raw.Trim();
            if (trimmed.Length == 0)
            {
                error = LobbyValidationError.Required;
                return false;
            }

            if (trimmed.Length < MinRoomCodeLength)
            {
                error = LobbyValidationError.TooShort;
                return false;
            }

            if (trimmed.Length > MaxRoomCodeLength)
            {
                error = LobbyValidationError.TooLong;
                return false;
            }

            if (!IsRoomCodeCharset(trimmed))
            {
                error = LobbyValidationError.InvalidCharacters;
                return false;
            }

            code = trimmed;
            error = LobbyValidationError.None;
            return true;
        }

        /// <summary>
        /// <b>새로 생성한</b> 방 코드를 정규화한다. 이때만 trim 후 대문자로 올린다
        /// (사람이 불러주고 받아 적는 코드라 대소문자 혼동을 없앤다). 목록에서 고른 원격 방 ID 에는 쓰지 않는다.
        /// </summary>
        public static bool TryNormalizeGeneratedRoomCode(string raw, out string code, out LobbyValidationError error)
        {
            code = string.Empty;

            string trimmed = raw == null ? string.Empty : raw.Trim();
            if (trimmed.Length == 0)
            {
                error = LobbyValidationError.Required;
                return false;
            }

            string upper = trimmed.ToUpperInvariant();
            if (!TryNormalizeTypedRoomCode(upper, out string validated, out error))
            {
                return false;
            }

            code = validated;
            return true;
        }

        /// <summary>
        /// 방 만들기 wizard 의 전체 입력을 한 번에 검증한다. 마지막 단계에서만 호출한다.
        /// 실패 사유는 <paramref name="error"/> 로만 전달하고 비밀번호 원문은 어디에도 남기지 않는다.
        /// </summary>
        public static bool TryCreateRoomDetails(
            string rawTitle,
            int maxPlayers,
            bool isPublic,
            string rawPassword,
            out LobbyCreateRoomDetails details,
            out LobbyValidationError error)
        {
            details = default;

            if (!TryNormalizeRoomTitle(rawTitle, out string title, out error))
            {
                return false;
            }

            if (!IsValidCapacity(maxPlayers))
            {
                error = LobbyValidationError.InvalidCapacity;
                return false;
            }

            if (!TryNormalizePassword(rawPassword, out string password, out error))
            {
                return false;
            }

            details = new LobbyCreateRoomDetails(title, maxPlayers, isPublic, password);
            error = LobbyValidationError.None;
            return true;
        }

        /// <summary>
        /// 방 목록에 검색·참가 가능 필터·정렬을 적용한 <b>새 배열</b>을 돌려준다. 입력 목록은 읽기만 한다.
        /// <paramref name="joinableOnly"/> 가 false 면 가득 찬 방·진행 중인 방·잠긴 방도 모두 보여주고,
        /// true 면 실제로 들어갈 수 있는 방만 남긴다(정원 초과·비정상 인원 항목 제외).
        /// 검색은 제목·방 코드·방장 이름을 대소문자 구분 없이 부분 일치로 본다.
        /// 동점은 코드로 안정적으로 끊어 같은 입력에 항상 같은 순서를 만든다.
        /// </summary>
        public static LobbyRoomView[] FilterAndSort(
            IReadOnlyList<LobbyRoomView> rooms,
            string search,
            bool joinableOnly,
            LobbyRoomSort sort)
        {
            if (rooms == null || rooms.Count == 0)
            {
                return Array.Empty<LobbyRoomView>();
            }

            string needle = search == null ? string.Empty : search.Trim();

            var matched = new List<LobbyRoomView>(rooms.Count);
            for (int i = 0; i < rooms.Count; i++)
            {
                LobbyRoomView room = rooms[i];
                if (joinableOnly && !IsOpenForJoin(room))
                {
                    continue;
                }

                if (needle.Length > 0 && !MatchesSearch(room, needle))
                {
                    continue;
                }

                matched.Add(room);
            }

            if (matched.Count == 0)
            {
                return Array.Empty<LobbyRoomView>();
            }

            // 원래 순서를 최종 tie-break 로 남겨 완전한 결정성을 확보한다(List.Sort 는 불안정 정렬).
            var indexed = new int[matched.Count];
            for (int i = 0; i < indexed.Length; i++)
            {
                indexed[i] = i;
            }

            LobbyRoomView[] source = matched.ToArray();
            Array.Sort(indexed, (leftIndex, rightIndex) => Compare(source, leftIndex, rightIndex, sort));

            var result = new LobbyRoomView[source.Length];
            for (int i = 0; i < indexed.Length; i++)
            {
                result[i] = source[indexed[i]];
            }

            return result;
        }

        /// <summary>
        /// 랜덤 참가 후보가 하나라도 있는지 본다. 후보는 참가 가능 · 비밀번호 없음 · 정원 미달이며 인원 정보가 정상인 방이다.
        /// 후보가 없으면 UI 는 버튼을 비활성화하며, 랜덤 참가로 방을 새로 만들지 않는다.
        /// </summary>
        public static bool HasRandomCandidate(IReadOnlyList<LobbyRoomView> rooms)
        {
            if (rooms == null)
            {
                return false;
            }

            for (int i = 0; i < rooms.Count; i++)
            {
                if (IsRandomCandidate(rooms[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>랜덤 참가 후보만 모아 새 배열로 돌려준다(입력 순서 보존).</summary>
        public static LobbyRoomView[] FilterRandomCandidates(IReadOnlyList<LobbyRoomView> rooms)
        {
            if (rooms == null || rooms.Count == 0)
            {
                return Array.Empty<LobbyRoomView>();
            }

            var candidates = new List<LobbyRoomView>(rooms.Count);
            for (int i = 0; i < rooms.Count; i++)
            {
                if (IsRandomCandidate(rooms[i]))
                {
                    candidates.Add(rooms[i]);
                }
            }

            return candidates.Count == 0 ? Array.Empty<LobbyRoomView>() : candidates.ToArray();
        }

        /// <summary>방 하나가 랜덤 참가 후보인지 본다(참가 가능 · 비밀번호 없음).</summary>
        public static bool IsRandomCandidate(LobbyRoomView room)
        {
            return IsOpenForJoin(room) && !room.HasPassword;
        }

        /// <summary>
        /// 실제로 들어갈 수 있는 방인지 본다. 어댑터의 <see cref="LobbyRoomView.IsJoinable"/> 외에
        /// 정원이 채워졌거나 정원·인원이 0 이하로 온 비정상 항목(집계 지연·닫히는 중인 세션)도 제외한다.
        /// 목록 표시(참가 가능 필터 해제)에는 쓰지 않는다.
        /// </summary>
        private static bool IsOpenForJoin(LobbyRoomView room)
        {
            return room.IsJoinable
                && room.MaxPlayers > 0
                && room.PlayerCount > 0
                && !room.IsFull;
        }

        private static bool MatchesSearch(LobbyRoomView room, string needle)
        {
            return ContainsIgnoreCase(room.Title, needle)
                || ContainsIgnoreCase(room.Code, needle)
                || ContainsIgnoreCase(room.HostName, needle);
        }

        private static bool ContainsIgnoreCase(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack))
            {
                return false;
            }

            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int Compare(LobbyRoomView[] source, int leftIndex, int rightIndex, LobbyRoomSort sort)
        {
            LobbyRoomView left = source[leftIndex];
            LobbyRoomView right = source[rightIndex];

            int primary;
            switch (sort)
            {
                case LobbyRoomSort.MostPlayers:
                    primary = right.PlayerCount.CompareTo(left.PlayerCount);
                    break;
                case LobbyRoomSort.LeastPlayers:
                    primary = left.PlayerCount.CompareTo(right.PlayerCount);
                    break;
                default:
                    primary = string.Compare(left.Title, right.Title, StringComparison.OrdinalIgnoreCase);
                    break;
            }

            if (primary != 0)
            {
                return primary;
            }

            int byCode = string.Compare(left.Code, right.Code, StringComparison.OrdinalIgnoreCase);
            if (byCode != 0)
            {
                return byCode;
            }

            byCode = string.CompareOrdinal(left.Code, right.Code);
            if (byCode != 0)
            {
                return byCode;
            }

            return leftIndex.CompareTo(rightIndex);
        }

        private static bool IsRoomCodeCharset(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool ok = (c >= '0' && c <= '9')
                    || (c >= 'A' && c <= 'Z')
                    || (c >= 'a' && c <= 'z')
                    || c == '-';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 쌍이 맞지 않는 서로게이트를 찾는다. 이런 문자열은 UTF-8 인코딩 시 U+FFFD 로 조용히 치환돼
        /// 길이 계산과 실제 전송 바이트가 어긋난다.
        /// </summary>
        private static bool ContainsUnpairedSurrogate(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                    {
                        return true;
                    }

                    i++;
                    continue;
                }

                if (char.IsLowSurrogate(c))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsControlCharacter(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsControl(value[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 사용자가 세는 "글자 수" 로 길이를 잰다. 결합 문자·이모지·한글 조합을 1칸으로 센다
        /// (string.Length 는 UTF-16 단위라 이모지 하나가 2로 세어진다).
        /// </summary>
        private static int CountTextElements(string value)
        {
            return new StringInfo(value).LengthInTextElements;
        }
    }
}

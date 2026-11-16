using System;
using System.Collections.Generic;
using BettingRoyale.Data.Contracts;
using BettingRoyale.Data.Online.Lobby;
using NUnit.Framework;

namespace BettingRoyale.Domain.Tests
{
    /// <summary>
    /// 로비 플로우 순수 규칙 회귀 테스트 (S-UX-BXXTHX).
    /// UnityEngine · Fusion 없이 검증 가능한 범위만 다룬다.
    /// </summary>
    public class LobbyRoomRulesTests
    {
        private static LobbyRoomView Room(
            string code,
            string title,
            string host,
            int players,
            int max,
            bool joinable = true,
            bool hasPassword = false)
        {
            return new LobbyRoomView(code, title, host, players, max, joinable, hasPassword);
        }

        // ---------- 방 제목 ----------

        [Test]
        public void RoomTitle_TrimsSurroundingWhitespaceAndKeepsInnerSpacing()
        {
            Assert.That(
                LobbyRoomRules.TryNormalizeRoomTitle("   붉은 성곽  ", out string title, out LobbyValidationError error),
                Is.True);
            Assert.That(title, Is.EqualTo("붉은 성곽"));
            Assert.That(error, Is.EqualTo(LobbyValidationError.None));
        }

        [Test]
        public void RoomTitle_RejectsNullEmptyAndWhitespaceOnlyInput()
        {
            Assert.That(LobbyRoomRules.TryNormalizeRoomTitle(null, out string a, out LobbyValidationError e1), Is.False);
            Assert.That(a, Is.EqualTo(string.Empty));
            Assert.That(e1, Is.EqualTo(LobbyValidationError.Required));

            Assert.That(LobbyRoomRules.TryNormalizeRoomTitle("", out _, out LobbyValidationError e2), Is.False);
            Assert.That(e2, Is.EqualTo(LobbyValidationError.Required));

            Assert.That(LobbyRoomRules.TryNormalizeRoomTitle("    ", out _, out LobbyValidationError e3), Is.False);
            Assert.That(e3, Is.EqualTo(LobbyValidationError.Required));
        }

        [Test]
        public void RoomTitle_CountsGraphemeClustersSoEmojiAndCombiningMarksFitInSixteen()
        {
            // 이모지 16개 = UTF-16 32칸이지만 사용자가 세는 글자 수는 16이다.
            string sixteenEmoji = string.Concat(Repeat("\U0001F525", 16));
            Assert.That(LobbyRoomRules.TryNormalizeRoomTitle(sixteenEmoji, out string kept, out LobbyValidationError ok), Is.True);
            Assert.That(kept, Is.EqualTo(sixteenEmoji));
            Assert.That(ok, Is.EqualTo(LobbyValidationError.None));

            string seventeenEmoji = string.Concat(Repeat("\U0001F525", 17));
            Assert.That(LobbyRoomRules.TryNormalizeRoomTitle(seventeenEmoji, out _, out LobbyValidationError tooLong), Is.False);
            Assert.That(tooLong, Is.EqualTo(LobbyValidationError.TooLong));

            // 결합 문자(e + U+0301)는 한 칸으로 센다.
            string combining = string.Concat(Repeat("e\u0301", 16));
            Assert.That(LobbyRoomRules.TryNormalizeRoomTitle(combining, out _, out LobbyValidationError combiningError), Is.True);
            Assert.That(combiningError, Is.EqualTo(LobbyValidationError.None));
        }

        [Test]
        public void RoomTitle_AcceptsBoundaryLengthsOneAndSixteen()
        {
            Assert.That(LobbyRoomRules.TryNormalizeRoomTitle("성", out string one, out _), Is.True);
            Assert.That(one, Is.EqualTo("성"));

            Assert.That(LobbyRoomRules.TryNormalizeRoomTitle(new string('가', 16), out _, out _), Is.True);
            Assert.That(LobbyRoomRules.TryNormalizeRoomTitle(new string('가', 17), out _, out LobbyValidationError error), Is.False);
            Assert.That(error, Is.EqualTo(LobbyValidationError.TooLong));
        }

        [Test]
        public void RoomTitle_RejectsOverSixtyFourUtf8BytesEvenWhenGraphemeCountFits()
        {
            // 이모지 16개 = 16 글자 = UTF-8 64바이트: 상한과 정확히 같아 통과한다.
            Assert.That(LobbyRoomRules.TryNormalizeRoomTitle(string.Concat(Repeat("\U0001F525", 16)), out _, out _), Is.True);

            // 한 글자에 결합 문자를 많이 붙이면 글자 수는 1이지만 바이트는 상한을 넘는다.
            string heavyCombining = "e" + string.Concat(Repeat("\u0301", 33));
            Assert.That(LobbyRoomRules.TryNormalizeRoomTitle(heavyCombining, out _, out LobbyValidationError tooLong), Is.False);
            Assert.That(tooLong, Is.EqualTo(LobbyValidationError.TooLong));

            // 결합 문자 31개(1 + 62 = 63바이트)는 상한 안이라 통과한다.
            Assert.That(
                LobbyRoomRules.TryNormalizeRoomTitle("e" + string.Concat(Repeat("\u0301", 31)), out _, out _),
                Is.True);
        }

        [Test]
        public void RoomTitle_RejectsUnpairedSurrogatesThatWouldBeReplacedOnTheWire()
        {
            Assert.That(LobbyRoomRules.TryNormalizeRoomTitle("성\uD83D곽", out _, out LobbyValidationError high), Is.False);
            Assert.That(high, Is.EqualTo(LobbyValidationError.InvalidCharacters));

            Assert.That(LobbyRoomRules.TryNormalizeRoomTitle("\uDE00성곽", out _, out LobbyValidationError low), Is.False);
            Assert.That(low, Is.EqualTo(LobbyValidationError.InvalidCharacters));

            // 정상 쌍(U+1F525)은 그대로 통과한다.
            Assert.That(LobbyRoomRules.TryNormalizeRoomTitle("\U0001F525 성곽", out string ok, out _), Is.True);
            Assert.That(ok, Is.EqualTo("\U0001F525 성곽"));
        }

        [Test]
        public void RoomTitle_RejectsControlCharactersIncludingTabNewlineAndDelete()
        {
            Assert.That(LobbyRoomRules.TryNormalizeRoomTitle("성\t곽", out _, out LobbyValidationError tab), Is.False);
            Assert.That(tab, Is.EqualTo(LobbyValidationError.InvalidCharacters));

            // 앞뒤 개행은 trim 으로 사라지지만 중간 개행은 거부한다.
            Assert.That(LobbyRoomRules.TryNormalizeRoomTitle("\n성곽\n", out string trimmed, out _), Is.True);
            Assert.That(trimmed, Is.EqualTo("성곽"));
            Assert.That(LobbyRoomRules.TryNormalizeRoomTitle("성\n곽", out _, out LobbyValidationError newline), Is.False);
            Assert.That(newline, Is.EqualTo(LobbyValidationError.InvalidCharacters));

            Assert.That(LobbyRoomRules.TryNormalizeRoomTitle("a\u007Fb", out _, out LobbyValidationError del), Is.False);
            Assert.That(del, Is.EqualTo(LobbyValidationError.InvalidCharacters));

            Assert.That(LobbyRoomRules.TryNormalizeRoomTitle("a\u0000b", out _, out LobbyValidationError nul), Is.False);
            Assert.That(nul, Is.EqualTo(LobbyValidationError.InvalidCharacters));
        }

        // ---------- 정원 ----------

        [Test]
        public void Capacity_AcceptsOnlySupportedEvenValuesWithinOperatingLimit()
        {
            Assert.That(LobbyRoomRules.IsValidCapacity(2), Is.True);
            Assert.That(LobbyRoomRules.IsValidCapacity(4), Is.True);
            Assert.That(LobbyRoomRules.IsValidCapacity(6), Is.True);
            Assert.That(LobbyRoomRules.IsValidCapacity(8), Is.EqualTo(LobbyRoomRules.MaxCapacity >= 8));
            Assert.That(LobbyRoomRules.IsValidCapacity(LobbyRoomRules.MaxCapacity + 2), Is.False);

            Assert.That(LobbyRoomRules.IsValidCapacity(1), Is.False);
            Assert.That(LobbyRoomRules.IsValidCapacity(3), Is.False);
            Assert.That(LobbyRoomRules.IsValidCapacity(5), Is.False);
            Assert.That(LobbyRoomRules.IsValidCapacity(7), Is.False);
            Assert.That(LobbyRoomRules.IsValidCapacity(10), Is.False);
            Assert.That(LobbyRoomRules.IsValidCapacity(0), Is.False);
            Assert.That(LobbyRoomRules.IsValidCapacity(-2), Is.False);
        }

        [Test]
        public void CapacityOptions_ExposeSupportedValuesInAscendingOrder()
        {
            IReadOnlyList<int> options = LobbyRoomRules.CapacityOptions;
            Assert.That(options.Count, Is.EqualTo(LobbyRoomRules.MaxCapacity / 2));
            Assert.That(options[0], Is.EqualTo(2));
            Assert.That(options[1], Is.EqualTo(4));
            Assert.That(options[2], Is.EqualTo(6));
            Assert.That(options[options.Count - 1], Is.EqualTo(LobbyRoomRules.MaxCapacity));

            Assert.That(LobbyRoomRules.DefaultCapacity, Is.EqualTo(LobbyRoomRules.MaxCapacity));
            Assert.That(LobbyRoomRules.IsValidCapacity(LobbyRoomRules.DefaultCapacity), Is.True);
        }

        // ---------- 비밀번호 ----------

        [TestCase(null)]
        [TestCase(8)]
        [TestCase(99)]
        public void Capacity_DirectAndLegacyRequestsAreCapped(int? requested)
        {
            Assert.That(LobbyRoomRules.ResolveCapacity(requested), Is.EqualTo(LobbyRoomRules.MaxCapacity));
        }

        [TestCase(-1, 2)]
        [TestCase(2, 2)]
        [TestCase(4, 4)]
        public void Capacity_SmallerRoomsRetainTheirSize(int requested, int expected)
        {
            Assert.That(LobbyRoomRules.ResolveCapacity(requested), Is.EqualTo(expected));
        }

        [Test]
        public void Password_IsOptionalSoNullAndEmptyPassValidationAsNoPassword()
        {
            Assert.That(LobbyRoomRules.TryNormalizePassword(null, out string a, out LobbyValidationError e1), Is.True);
            Assert.That(a, Is.EqualTo(string.Empty));
            Assert.That(e1, Is.EqualTo(LobbyValidationError.None));

            Assert.That(LobbyRoomRules.TryNormalizePassword("", out string b, out LobbyValidationError e2), Is.True);
            Assert.That(b, Is.EqualTo(string.Empty));
            Assert.That(e2, Is.EqualTo(LobbyValidationError.None));
        }

        [Test]
        public void Password_KeepsSurroundingSpacesBecauseTheyAreTypedCharacters()
        {
            Assert.That(LobbyRoomRules.TryNormalizePassword("  pw  ", out string password, out _), Is.True);
            Assert.That(password, Is.EqualTo("  pw  "));
        }

        [Test]
        public void Password_RejectsOverSixteenCharactersAndOverSixtyFourUtf8Bytes()
        {
            Assert.That(LobbyRoomRules.TryNormalizePassword(new string('a', 16), out _, out _), Is.True);
            Assert.That(LobbyRoomRules.TryNormalizePassword(new string('a', 17), out _, out LobbyValidationError tooLong), Is.False);
            Assert.That(tooLong, Is.EqualTo(LobbyValidationError.TooLong));

            // 한글 16자 = UTF-8 48바이트: 길이·바이트 모두 통과한다.
            Assert.That(LobbyRoomRules.TryNormalizePassword(new string('가', 16), out _, out _), Is.True);

            // 이모지 16개 = 16 텍스트 요소지만 UTF-8 64바이트 상한과 정확히 같다(통과).
            string sixteenEmoji = string.Concat(Repeat("\U0001F525", 16));
            Assert.That(LobbyRoomRules.TryNormalizePassword(sixteenEmoji, out _, out _), Is.True);

            // 이모지 15개 + 한글 1자 = 63 바이트(통과), 이모지 16개 + 한글 1자는 길이 초과.
            Assert.That(LobbyRoomRules.TryNormalizePassword(string.Concat(Repeat("\U0001F525", 15)) + "가", out _, out _), Is.True);
            Assert.That(
                LobbyRoomRules.TryNormalizePassword(sixteenEmoji + "가", out _, out LobbyValidationError overflow),
                Is.False);
            Assert.That(overflow, Is.EqualTo(LobbyValidationError.TooLong));
        }

        [Test]
        public void Password_RejectsMalformedUnicode()
        {
            Assert.That(LobbyRoomRules.TryNormalizePassword("pw\uD800", out _, out _), Is.False);
            Assert.That(LobbyRoomRules.TryNormalizePassword("pw\uDC00", out _, out _), Is.False);
        }

        [Test]
        public void Password_RejectsControlCharacters()
        {
            Assert.That(LobbyRoomRules.TryNormalizePassword("pw\tpw", out _, out LobbyValidationError tab), Is.False);
            Assert.That(tab, Is.EqualTo(LobbyValidationError.InvalidCharacters));

            Assert.That(LobbyRoomRules.TryNormalizePassword("pw\r\n", out _, out LobbyValidationError newline), Is.False);
            Assert.That(newline, Is.EqualTo(LobbyValidationError.InvalidCharacters));
        }

        // ---------- 방 코드 ----------

        [Test]
        public void TypedRoomCode_TrimsButNeverChangesCaseOfTheRemoteSessionName()
        {
            Assert.That(
                LobbyRoomRules.TryNormalizeTypedRoomCode("  aB3-xY  ", out string code, out LobbyValidationError error),
                Is.True);
            Assert.That(code, Is.EqualTo("aB3-xY"));
            Assert.That(error, Is.EqualTo(LobbyValidationError.None));
        }

        [Test]
        public void TypedRoomCode_EnforcesFourToSixtyFourAsciiLettersDigitsAndHyphen()
        {
            Assert.That(LobbyRoomRules.TryNormalizeTypedRoomCode("ABCD", out _, out _), Is.True);
            Assert.That(LobbyRoomRules.TryNormalizeTypedRoomCode(new string('A', 64), out _, out _), Is.True);

            Assert.That(LobbyRoomRules.TryNormalizeTypedRoomCode("ABC", out _, out LobbyValidationError shortError), Is.False);
            Assert.That(shortError, Is.EqualTo(LobbyValidationError.TooShort));

            Assert.That(
                LobbyRoomRules.TryNormalizeTypedRoomCode(new string('A', 65), out _, out LobbyValidationError longError),
                Is.False);
            Assert.That(longError, Is.EqualTo(LobbyValidationError.TooLong));

            Assert.That(LobbyRoomRules.TryNormalizeTypedRoomCode("   ", out _, out LobbyValidationError required), Is.False);
            Assert.That(required, Is.EqualTo(LobbyValidationError.Required));
        }

        [Test]
        public void TypedRoomCode_AcceptsLegacyThirtySixCharacterGuidSessionNamesWithCasePreserved()
        {
            const string guid = "9f2b1c4d-5e6a-47b8-9c0d-1e2f3a4b5c6D";
            Assert.That(guid.Length, Is.EqualTo(36));

            Assert.That(LobbyRoomRules.TryNormalizeTypedRoomCode(guid, out string code, out LobbyValidationError error), Is.True);
            Assert.That(code, Is.EqualTo(guid));
            Assert.That(error, Is.EqualTo(LobbyValidationError.None));
        }

        [Test]
        public void TypedRoomCode_RejectsNonAsciiSpacesAndSymbols()
        {
            Assert.That(LobbyRoomRules.TryNormalizeTypedRoomCode("방코드1", out _, out LobbyValidationError hangul), Is.False);
            Assert.That(hangul, Is.EqualTo(LobbyValidationError.InvalidCharacters));

            Assert.That(LobbyRoomRules.TryNormalizeTypedRoomCode("AB CD", out _, out LobbyValidationError space), Is.False);
            Assert.That(space, Is.EqualTo(LobbyValidationError.InvalidCharacters));

            Assert.That(LobbyRoomRules.TryNormalizeTypedRoomCode("AB_CD", out _, out LobbyValidationError underscore), Is.False);
            Assert.That(underscore, Is.EqualTo(LobbyValidationError.InvalidCharacters));

            Assert.That(LobbyRoomRules.TryNormalizeTypedRoomCode("AB\tCD", out _, out LobbyValidationError control), Is.False);
            Assert.That(control, Is.EqualTo(LobbyValidationError.InvalidCharacters));
        }

        [Test]
        public void GeneratedRoomCode_IsTheOnlyPathThatUppercases()
        {
            Assert.That(
                LobbyRoomRules.TryNormalizeGeneratedRoomCode(" ab3-xy ", out string generated, out LobbyValidationError error),
                Is.True);
            Assert.That(generated, Is.EqualTo("AB3-XY"));
            Assert.That(error, Is.EqualTo(LobbyValidationError.None));

            Assert.That(LobbyRoomRules.TryNormalizeGeneratedRoomCode("a b", out _, out LobbyValidationError invalid), Is.False);
            Assert.That(invalid, Is.EqualTo(LobbyValidationError.TooShort));
        }

        [Test]
        public void SelectingARoomFromTheListUsesItsCodeVerbatim()
        {
            LobbyRoomView room = Room("aB3-xY-remote", "성곽", "호스트", 1, 4);
            LobbyActionRequested request = LobbyActionRequested.Join(room.Code);

            Assert.That(request.Text, Is.EqualTo("aB3-xY-remote"));
            Assert.That(room.Code, Is.EqualTo("aB3-xY-remote"));
        }

        // ---------- 방 생성 입력 ----------

        [Test]
        public void CreateRoomDetails_ValidatesTitleCapacityAndPasswordTogether()
        {
            Assert.That(
                LobbyRoomRules.TryCreateRoomDetails("  성곽 결투  ", 6, true, "pw", out LobbyCreateRoomDetails details, out LobbyValidationError error),
                Is.True);
            Assert.That(error, Is.EqualTo(LobbyValidationError.None));
            Assert.That(details.Title, Is.EqualTo("성곽 결투"));
            Assert.That(details.MaxPlayers, Is.EqualTo(6));
            Assert.That(details.IsPublic, Is.True);
            Assert.That(details.Password, Is.EqualTo("pw"));
            Assert.That(details.HasPassword, Is.True);
        }

        [Test]
        public void CreateRoomDetails_FailsOnBadCapacityAndReportsInvalidCapacity()
        {
            Assert.That(
                LobbyRoomRules.TryCreateRoomDetails("성곽", 5, true, null, out LobbyCreateRoomDetails details, out LobbyValidationError error),
                Is.False);
            Assert.That(error, Is.EqualTo(LobbyValidationError.InvalidCapacity));
            Assert.That(details.Title, Is.EqualTo(string.Empty));
            Assert.That(details.MaxPlayers, Is.EqualTo(0));

            Assert.That(LobbyRoomRules.TryCreateRoomDetails(" ", 4, true, null, out _, out LobbyValidationError titleError), Is.False);
            Assert.That(titleError, Is.EqualTo(LobbyValidationError.Required));

            Assert.That(
                LobbyRoomRules.TryCreateRoomDetails("성곽", 4, false, new string('a', 17), out _, out LobbyValidationError passwordError),
                Is.False);
            Assert.That(passwordError, Is.EqualTo(LobbyValidationError.TooLong));
        }

        [Test]
        public void CreateRoomDetails_ToStringNeverLeaksThePassword()
        {
            LobbyRoomRules.TryCreateRoomDetails("성곽", 4, false, "s3cret-pw", out LobbyCreateRoomDetails details, out _);

            string text = details.ToString();
            Assert.That(text.Contains("s3cret-pw"), Is.False);
            Assert.That(text.Contains("HasPassword=true"), Is.True);
        }

        // ---------- 검색 · 필터 · 정렬 ----------

        [Test]
        public void FilterAndSort_ReturnsACopyAndLeavesTheInputListUntouched()
        {
            var rooms = new List<LobbyRoomView>
            {
                Room("B", "나 성곽", "호스트B", 2, 4),
                Room("A", "가 성곽", "호스트A", 1, 4),
            };

            LobbyRoomView[] sorted = LobbyRoomRules.FilterAndSort(rooms, null, false, LobbyRoomSort.Title);

            Assert.That(sorted.Length, Is.EqualTo(2));
            Assert.That(sorted[0].Code, Is.EqualTo("A"));
            // 입력은 원래 순서 그대로여야 한다.
            Assert.That(rooms[0].Code, Is.EqualTo("B"));
            Assert.That(rooms[1].Code, Is.EqualTo("A"));

            // 반환 배열을 바꿔도 입력에 영향이 없다.
            sorted[0] = Room("ZZZ", "변경", "변경", 0, 2);
            Assert.That(rooms[1].Code, Is.EqualTo("A"));
        }

        [Test]
        public void FilterAndSort_ReturnsEmptyForNullEmptyAndNoMatchInputs()
        {
            Assert.That(LobbyRoomRules.FilterAndSort(null, "x", false, LobbyRoomSort.Title).Length, Is.EqualTo(0));
            Assert.That(
                LobbyRoomRules.FilterAndSort(new List<LobbyRoomView>(), null, true, LobbyRoomSort.MostPlayers).Length,
                Is.EqualTo(0));

            var rooms = new List<LobbyRoomView> { Room("A", "성곽", "호스트", 1, 4) };
            Assert.That(LobbyRoomRules.FilterAndSort(rooms, "없는검색어", false, LobbyRoomSort.Title).Length, Is.EqualTo(0));
        }

        [Test]
        public void FilterAndSort_SearchesTitleCodeAndHostCaseInsensitively()
        {
            var rooms = new List<LobbyRoomView>
            {
                Room("ROOM-AAAA", "성곽 결투", "GoldKnight", 1, 4),
                Room("ROOM-BBBB", "야간 훈련", "torchbearer", 2, 4),
                Room("ROOM-CCCC", "Torch Hall", "무명", 3, 4),
            };

            // 제목
            LobbyRoomView[] byTitle = LobbyRoomRules.FilterAndSort(rooms, "성곽", false, LobbyRoomSort.Title);
            Assert.That(byTitle.Length, Is.EqualTo(1));
            Assert.That(byTitle[0].Code, Is.EqualTo("ROOM-AAAA"));

            // 코드(소문자 검색어)
            LobbyRoomView[] byCode = LobbyRoomRules.FilterAndSort(rooms, "room-bbbb", false, LobbyRoomSort.Title);
            Assert.That(byCode.Length, Is.EqualTo(1));
            Assert.That(byCode[0].Code, Is.EqualTo("ROOM-BBBB"));

            // 방장 이름과 제목에 동시에 걸리는 대소문자 혼합 검색어
            LobbyRoomView[] byHost = LobbyRoomRules.FilterAndSort(rooms, "TORCH", false, LobbyRoomSort.Title);
            Assert.That(byHost.Length, Is.EqualTo(2));

            // 검색어의 앞뒤 공백은 무시한다.
            Assert.That(LobbyRoomRules.FilterAndSort(rooms, "  gold  ", false, LobbyRoomSort.Title).Length, Is.EqualTo(1));
            // 공백만 입력하면 필터가 걸리지 않는다.
            Assert.That(LobbyRoomRules.FilterAndSort(rooms, "   ", false, LobbyRoomSort.Title).Length, Is.EqualTo(3));
        }

        [Test]
        public void FilterAndSort_JoinableOnlyDropsClosedAndFullRoomsButKeepsLockedOnes()
        {
            var rooms = new List<LobbyRoomView>
            {
                Room("A", "열린 방", "호스트", 1, 4),
                Room("B", "진행 중", "호스트", 2, 4, joinable: false),
                Room("C", "가득 찬 방", "호스트", 4, 4),
                Room("D", "잠긴 방", "호스트", 1, 4, hasPassword: true),
            };

            // 필터를 끄면 가득 찬 방·진행 중인 방·잠긴 방까지 모두 보여준다.
            LobbyRoomView[] all = LobbyRoomRules.FilterAndSort(rooms, null, false, LobbyRoomSort.Title);
            Assert.That(all.Length, Is.EqualTo(4));

            // 참가 가능 필터는 실제로 들어갈 수 있는 방만 남긴다. 가득 찬 방(C)은 눌러도 실패하므로 제외된다.
            LobbyRoomView[] joinable = LobbyRoomRules.FilterAndSort(rooms, null, true, LobbyRoomSort.Title);
            Assert.That(joinable.Length, Is.EqualTo(2));
            var codes = new List<string>();
            for (int i = 0; i < joinable.Length; i++)
            {
                codes.Add(joinable[i].Code);
            }

            Assert.That(codes, Does.Contain("A"));
            // 비밀번호가 있어도 코드/목록으로 참가할 수 있으므로 남는다.
            Assert.That(codes, Does.Contain("D"));
            Assert.That(codes, Does.Not.Contain("B"));
            Assert.That(codes, Does.Not.Contain("C"));
        }

        [Test]
        public void FilterAndSort_JoinableOnlyDropsRoomsWithNonPositiveCapacityOrPlayerCount()
        {
            var rooms = new List<LobbyRoomView>
            {
                Room("OK", "정상", "호스트", 1, 4),
                Room("ZERO-MAX", "정원 0", "호스트", 1, 0),
                Room("NEG-MAX", "정원 음수", "호스트", 1, -4),
                Room("ZERO-PLAYERS", "인원 0", "호스트", 0, 4),
                Room("NEG-PLAYERS", "인원 음수", "호스트", -1, 4),
                Room("OVER", "정원 초과", "호스트", 5, 4),
            };

            // 표시용 목록에는 비정상 항목도 그대로 남는다(집계 지연을 UI 가 숨기지 않는다).
            Assert.That(LobbyRoomRules.FilterAndSort(rooms, null, false, LobbyRoomSort.Title).Length, Is.EqualTo(6));

            LobbyRoomView[] joinable = LobbyRoomRules.FilterAndSort(rooms, null, true, LobbyRoomSort.Title);
            Assert.That(joinable.Length, Is.EqualTo(1));
            Assert.That(joinable[0].Code, Is.EqualTo("OK"));
        }

        [Test]
        public void FilterAndSort_SortsByPlayerCountInBothDirections()
        {
            var rooms = new List<LobbyRoomView>
            {
                Room("A", "가", "호스트", 3, 8),
                Room("B", "나", "호스트", 1, 8),
                Room("C", "다", "호스트", 6, 8),
            };

            LobbyRoomView[] most = LobbyRoomRules.FilterAndSort(rooms, null, false, LobbyRoomSort.MostPlayers);
            Assert.That(most[0].Code, Is.EqualTo("C"));
            Assert.That(most[1].Code, Is.EqualTo("A"));
            Assert.That(most[2].Code, Is.EqualTo("B"));

            LobbyRoomView[] least = LobbyRoomRules.FilterAndSort(rooms, null, false, LobbyRoomSort.LeastPlayers);
            Assert.That(least[0].Code, Is.EqualTo("B"));
            Assert.That(least[1].Code, Is.EqualTo("A"));
            Assert.That(least[2].Code, Is.EqualTo("C"));
        }

        [Test]
        public void FilterAndSort_SortsTitlesCaseInsensitively()
        {
            var rooms = new List<LobbyRoomView>
            {
                Room("A", "banner", "호스트", 1, 4),
                Room("B", "Arena", "호스트", 1, 4),
                Room("C", "castle", "호스트", 1, 4),
            };

            LobbyRoomView[] sorted = LobbyRoomRules.FilterAndSort(rooms, null, false, LobbyRoomSort.Title);
            Assert.That(sorted[0].Title, Is.EqualTo("Arena"));
            Assert.That(sorted[1].Title, Is.EqualTo("banner"));
            Assert.That(sorted[2].Title, Is.EqualTo("castle"));
        }

        [Test]
        public void FilterAndSort_BreaksTiesByCodeSoInputOrderCannotChangeTheResult()
        {
            var ascending = new List<LobbyRoomView>
            {
                Room("AAA", "같은 제목", "호스트", 2, 4),
                Room("BBB", "같은 제목", "호스트", 2, 4),
                Room("CCC", "같은 제목", "호스트", 2, 4),
            };
            var shuffled = new List<LobbyRoomView>
            {
                Room("CCC", "같은 제목", "호스트", 2, 4),
                Room("AAA", "같은 제목", "호스트", 2, 4),
                Room("BBB", "같은 제목", "호스트", 2, 4),
            };

            foreach (LobbyRoomSort sort in new[] { LobbyRoomSort.Title, LobbyRoomSort.MostPlayers, LobbyRoomSort.LeastPlayers })
            {
                LobbyRoomView[] first = LobbyRoomRules.FilterAndSort(ascending, null, false, sort);
                LobbyRoomView[] second = LobbyRoomRules.FilterAndSort(shuffled, null, false, sort);

                Assert.That(first.Length, Is.EqualTo(3));
                Assert.That(second.Length, Is.EqualTo(3));
                for (int i = 0; i < first.Length; i++)
                {
                    Assert.That(second[i].Code, Is.EqualTo(first[i].Code));
                }

                Assert.That(first[0].Code, Is.EqualTo("AAA"));
                Assert.That(first[1].Code, Is.EqualTo("BBB"));
                Assert.That(first[2].Code, Is.EqualTo("CCC"));
            }
        }

        [Test]
        public void FilterAndSort_KeepsIdenticalCodesStableInInputOrder()
        {
            var rooms = new List<LobbyRoomView>
            {
                Room("SAME", "먼저", "호스트", 2, 4),
                Room("SAME", "나중", "호스트", 2, 4),
            };

            LobbyRoomView[] sorted = LobbyRoomRules.FilterAndSort(rooms, null, false, LobbyRoomSort.MostPlayers);
            Assert.That(sorted[0].Title, Is.EqualTo("먼저"));
            Assert.That(sorted[1].Title, Is.EqualTo("나중"));
        }

        // ---------- 랜덤 참가 ----------

        [Test]
        public void RandomCandidates_ExcludeLockedFullAndNonJoinableRooms()
        {
            var rooms = new List<LobbyRoomView>
            {
                Room("LOCK", "잠긴 방", "호스트", 1, 4, hasPassword: true),
                Room("FULL", "가득 찬 방", "호스트", 4, 4),
                Room("BUSY", "진행 중", "호스트", 1, 4, joinable: false),
                Room("OPEN", "열린 방", "호스트", 1, 4),
            };

            Assert.That(LobbyRoomRules.HasRandomCandidate(rooms), Is.True);

            LobbyRoomView[] candidates = LobbyRoomRules.FilterRandomCandidates(rooms);
            Assert.That(candidates.Length, Is.EqualTo(1));
            Assert.That(candidates[0].Code, Is.EqualTo("OPEN"));
        }

        [Test]
        public void RandomCandidates_AreAbsentForNullEmptyAndAllExcludedLists()
        {
            Assert.That(LobbyRoomRules.HasRandomCandidate(null), Is.False);
            Assert.That(LobbyRoomRules.HasRandomCandidate(new List<LobbyRoomView>()), Is.False);
            Assert.That(LobbyRoomRules.FilterRandomCandidates(null).Length, Is.EqualTo(0));

            var blocked = new List<LobbyRoomView>
            {
                Room("LOCK", "잠긴 방", "호스트", 1, 4, hasPassword: true),
                Room("FULL", "가득 찬 방", "호스트", 8, 8),
                Room("BUSY", "진행 중", "호스트", 1, 4, joinable: false),
            };
            Assert.That(LobbyRoomRules.HasRandomCandidate(blocked), Is.False);
            Assert.That(LobbyRoomRules.FilterRandomCandidates(blocked).Length, Is.EqualTo(0));
        }

        [Test]
        public void RandomCandidates_ExcludeRoomsWithNonPositiveCapacityOrPlayerCount()
        {
            var broken = new List<LobbyRoomView>
            {
                Room("ZERO-MAX", "정원 0", "호스트", 1, 0),
                Room("NEG-MAX", "정원 음수", "호스트", 1, -4),
                Room("ZERO-PLAYERS", "인원 0", "호스트", 0, 4),
                Room("NEG-PLAYERS", "인원 음수", "호스트", -1, 4),
                Room("OVER", "정원 초과", "호스트", 5, 4),
                default(LobbyRoomView),
            };

            Assert.That(LobbyRoomRules.HasRandomCandidate(broken), Is.False);
            Assert.That(LobbyRoomRules.FilterRandomCandidates(broken).Length, Is.EqualTo(0));

            broken.Add(Room("OK", "정상", "호스트", 1, 4));
            Assert.That(LobbyRoomRules.HasRandomCandidate(broken), Is.True);

            LobbyRoomView[] candidates = LobbyRoomRules.FilterRandomCandidates(broken);
            Assert.That(candidates.Length, Is.EqualTo(1));
            Assert.That(candidates[0].Code, Is.EqualTo("OK"));
        }

        // 시작 가능 판정은 NetworkManager 가 권위적으로 계산해 스냅샷의 CanStart 로 내려준다.
        // UI 쪽 중복 규칙(LobbyRoomRules.CanStartMatch)은 제거했으므로 여기서 검증하지 않는다.

        // ---------- 요청 이벤트 ----------

        [Test]
        public void ActionRequested_CreateCarriesTitleCapacityVisibilityAndPassword()
        {
            LobbyRoomRules.TryCreateRoomDetails("성곽 결투", LobbyRoomRules.MaxCapacity, false, "pw", out LobbyCreateRoomDetails details, out _);
            LobbyActionRequested request = LobbyActionRequested.Create(details);

            Assert.That(request.Kind, Is.EqualTo(LobbyActionKind.Create));
            Assert.That(request.Text, Is.EqualTo("성곽 결투"));
            Assert.That(request.MaxPlayers, Is.EqualTo(LobbyRoomRules.MaxCapacity));
            Assert.That(request.Flag, Is.False);
            Assert.That(request.Password, Is.EqualTo("pw"));
            Assert.That(request.HasPassword, Is.True);
        }

        [Test]
        public void ActionRequested_JoinUsesTextAsRoomCodeAndSetReadyUsesFlag()
        {
            LobbyActionRequested join = LobbyActionRequested.Join("ROOM-1234", "pw");
            Assert.That(join.Kind, Is.EqualTo(LobbyActionKind.Join));
            Assert.That(join.Text, Is.EqualTo("ROOM-1234"));
            Assert.That(join.MaxPlayers, Is.EqualTo(0));

            LobbyActionRequested ready = LobbyActionRequested.SetReady(true);
            Assert.That(ready.Kind, Is.EqualTo(LobbyActionKind.SetReady));
            Assert.That(ready.Flag, Is.True);
            Assert.That(ready.HasPassword, Is.False);

            LobbyActionRequested cancel = LobbyActionRequested.SetReady(false);
            Assert.That(cancel.Flag, Is.False);
        }

        [Test]
        public void ActionRequested_DefaultsNullStringsToEmptyForEveryKind()
        {
            LobbyActionRequested request = new LobbyActionRequested(LobbyActionKind.RandomJoin);
            Assert.That(request.Text, Is.EqualTo(string.Empty));
            Assert.That(request.Password, Is.EqualTo(string.Empty));
            Assert.That(request.HasPassword, Is.False);

            Assert.That(LobbyActionRequested.Connect().Kind, Is.EqualTo(LobbyActionKind.Connect));
            Assert.That(LobbyActionRequested.Refresh().Kind, Is.EqualTo(LobbyActionKind.Refresh));
            Assert.That(LobbyActionRequested.RandomJoin().Kind, Is.EqualTo(LobbyActionKind.RandomJoin));
            Assert.That(LobbyActionRequested.Leave().Kind, Is.EqualTo(LobbyActionKind.Leave));
            Assert.That(LobbyActionRequested.StartMatch().Kind, Is.EqualTo(LobbyActionKind.StartMatch));
            Assert.That(LobbyActionRequested.Snapshot().Kind, Is.EqualTo(LobbyActionKind.Snapshot));
        }

        [Test]
        public void ActionRequested_ToStringNeverLeaksThePassword()
        {
            string text = LobbyActionRequested.Join("ROOM-1234", "s3cret-pw").ToString();

            Assert.That(text.Contains("s3cret-pw"), Is.False);
            Assert.That(text.Contains("ROOM-1234"), Is.True);
            Assert.That(text.Contains("HasPassword=true"), Is.True);
        }

        // ---------- 스냅샷 ----------

        [Test]
        public void Snapshot_EmptyIsUsableBeforeAnyConnection()
        {
            var snapshot = new LobbyFlowSnapshot();

            Assert.That(snapshot.Connected, Is.False);
            Assert.That(snapshot.Connecting, Is.False);
            Assert.That(snapshot.InRoom, Is.False);
            Assert.That(snapshot.IsHost, Is.False);
            Assert.That(snapshot.IsLocalReady, Is.False);
            Assert.That(snapshot.CanStart, Is.False);
            Assert.That(snapshot.Busy, Is.False);
            Assert.That(snapshot.Status, Is.EqualTo(string.Empty));
            Assert.That(snapshot.RoomCode, Is.EqualTo(string.Empty));
            Assert.That(snapshot.RoomTitle, Is.EqualTo(string.Empty));
            Assert.That(snapshot.MaxPlayers, Is.EqualTo(0));
            Assert.That(snapshot.Rooms.Count, Is.EqualTo(0));
            Assert.That(snapshot.Players.Count, Is.EqualTo(0));
            Assert.That(LobbyFlowSnapshot.Empty.Rooms.Count, Is.EqualTo(0));
            Assert.That(LobbyFlowSnapshot.Empty.Players.Count, Is.EqualTo(0));
        }

        [Test]
        public void Snapshot_ClonesListsSoLaterMutationOfTheSourceCannotChangeIt()
        {
            var rooms = new List<LobbyRoomView> { Room("A", "성곽", "호스트", 1, 4) };
            var players = new List<LobbyPlayerView> { new LobbyPlayerView(1, "방장", true, true, false) };

            var snapshot = new LobbyFlowSnapshot(
                connected: true,
                connecting: false,
                inRoom: true,
                isHost: true,
                isLocalReady: false,
                canStart: false,
                busy: false,
                status: "대기 중",
                roomCode: "A",
                roomTitle: "성곽",
                maxPlayers: 4,
                isPublic: true,
                rooms: rooms,
                players: players);

            rooms.Add(Room("B", "추가", "호스트", 1, 4));
            players.Clear();

            Assert.That(snapshot.Rooms.Count, Is.EqualTo(1));
            Assert.That(snapshot.Rooms[0].Code, Is.EqualTo("A"));
            Assert.That(snapshot.Players.Count, Is.EqualTo(1));
            Assert.That(snapshot.Players[0].Name, Is.EqualTo("방장"));
            Assert.That(snapshot.Status, Is.EqualTo("대기 중"));
            Assert.That(snapshot.IsPublic, Is.True);
        }

        [Test]
        public void Snapshot_CollectionsCannotBeMutatedThroughAnArrayCastOrIListSetter()
        {
            var snapshot = new LobbyFlowSnapshot(
                connected: true,
                connecting: false,
                inRoom: true,
                isHost: true,
                isLocalReady: false,
                canStart: false,
                busy: false,
                status: "대기 중",
                roomCode: "A",
                roomTitle: "성곽",
                maxPlayers: 4,
                isPublic: true,
                rooms: new List<LobbyRoomView> { Room("A", "성곽", "호스트", 1, 4) },
                players: new List<LobbyPlayerView> { new LobbyPlayerView(1, "방장", true, true, false) });

            // 내부 배열을 그대로 노출하면 소비자가 캐스팅해 발행된 스냅샷을 바꿀 수 있다.
            Assert.That(snapshot.Rooms, Is.Not.InstanceOf<LobbyRoomView[]>());
            Assert.That(snapshot.Players, Is.Not.InstanceOf<LobbyPlayerView[]>());

            var rooms = snapshot.Rooms as IList<LobbyRoomView>;
            var players = snapshot.Players as IList<LobbyPlayerView>;
            Assert.That(rooms, Is.Not.Null);
            Assert.That(players, Is.Not.Null);

            Assert.That(() => { rooms[0] = Room("HACK", "변조", "변조", 0, 2); }, Throws.TypeOf<NotSupportedException>());
            Assert.That(() => rooms.Add(Room("HACK", "변조", "변조", 0, 2)), Throws.TypeOf<NotSupportedException>());
            Assert.That(() => rooms.Clear(), Throws.TypeOf<NotSupportedException>());
            Assert.That(() => { players[0] = new LobbyPlayerView(9, "변조", false, false, true); }, Throws.TypeOf<NotSupportedException>());
            Assert.That(() => players.RemoveAt(0), Throws.TypeOf<NotSupportedException>());

            Assert.That(snapshot.Rooms.Count, Is.EqualTo(1));
            Assert.That(snapshot.Rooms[0].Code, Is.EqualTo("A"));
            Assert.That(snapshot.Players.Count, Is.EqualTo(1));
            Assert.That(snapshot.Players[0].Name, Is.EqualTo("방장"));

            // 빈 목록도 같은 보호를 받는다.
            Assert.That(LobbyFlowSnapshot.Empty.Rooms, Is.Not.InstanceOf<LobbyRoomView[]>());
            Assert.That(
                () => ((IList<LobbyRoomView>)LobbyFlowSnapshot.Empty.Rooms).Add(default(LobbyRoomView)),
                Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void Snapshot_AcceptsNullListsAndNullStrings()
        {
            var snapshot = new LobbyFlowSnapshot(
                connected: true,
                connecting: false,
                inRoom: false,
                isHost: false,
                isLocalReady: false,
                canStart: false,
                busy: true,
                status: null,
                roomCode: null,
                roomTitle: null,
                maxPlayers: 0,
                isPublic: false,
                rooms: null,
                players: null);

            Assert.That(snapshot.Status, Is.EqualTo(string.Empty));
            Assert.That(snapshot.RoomCode, Is.EqualTo(string.Empty));
            Assert.That(snapshot.RoomTitle, Is.EqualTo(string.Empty));
            Assert.That(snapshot.Rooms.Count, Is.EqualTo(0));
            Assert.That(snapshot.Players.Count, Is.EqualTo(0));
            Assert.That(snapshot.Busy, Is.True);
        }

        [Test]
        public void Snapshot_ToStringSummarizesCountsWithoutAnySecret()
        {
            var snapshot = new LobbyFlowSnapshot(
                connected: true,
                connecting: false,
                inRoom: true,
                isHost: true,
                isLocalReady: true,
                canStart: true,
                busy: false,
                status: "시작 가능",
                roomCode: "ROOM-1234",
                roomTitle: "성곽",
                maxPlayers: 4,
                isPublic: false,
                rooms: new List<LobbyRoomView>(),
                players: new List<LobbyPlayerView>
                {
                    new LobbyPlayerView(1, "방장", true, true, true),
                    new LobbyPlayerView(2, "참가자", false, false, true),
                });

            string text = snapshot.ToString();
            Assert.That(text.Contains("Players=2"), Is.True);
            Assert.That(text.Contains("Rooms=0"), Is.True);
            Assert.That(text.Contains("Password"), Is.False);
        }

        [Test]
        public void SnapshotRoomsAndPlayers_ExposeTheValuesTheWaitingRoomNeeds()
        {
            LobbyRoomView room = Room("ROOM-1234", "성곽", "방장", 4, 4, joinable: true, hasPassword: true);
            Assert.That(room.IsFull, Is.True);
            Assert.That(room.HasPassword, Is.True);
            Assert.That(LobbyRoomRules.IsRandomCandidate(room), Is.False);

            var player = new LobbyPlayerView(7, "참가자", false, true, true);
            Assert.That(player.Id, Is.EqualTo(7));
            Assert.That(player.IsHost, Is.False);
            Assert.That(player.IsLocal, Is.True);
            Assert.That(player.IsReady, Is.True);

            // null 이름/코드는 빈 문자열로 방어한다(UI 가 NullReference 로 죽지 않게).
            var defensive = new LobbyRoomView(null, null, null, 0, 0, false, false);
            Assert.That(defensive.Code, Is.EqualTo(string.Empty));
            Assert.That(defensive.Title, Is.EqualTo(string.Empty));
            Assert.That(defensive.HostName, Is.EqualTo(string.Empty));
            Assert.That(defensive.IsFull, Is.False);
            Assert.That(new LobbyPlayerView(0, null, false, false, false).Name, Is.EqualTo(string.Empty));
        }

        [Test]
        public void DefaultStructValues_NeverExposeNullStringsToTheUiLayer()
        {
            // 검증 실패 경로는 out 파라미터로 default 구조체를 돌려준다.
            // 생성자를 거치지 않는 이 값도 UI 가 그대로 바인딩하므로 null 이 나오면 안 된다.
            LobbyCreateRoomDetails details = default;
            Assert.That(details.Title, Is.EqualTo(string.Empty));
            Assert.That(details.Password, Is.EqualTo(string.Empty));
            Assert.That(details.HasPassword, Is.False);

            LobbyRoomView room = default;
            Assert.That(room.Code, Is.EqualTo(string.Empty));
            Assert.That(room.Title, Is.EqualTo(string.Empty));
            Assert.That(room.HostName, Is.EqualTo(string.Empty));

            LobbyPlayerView player = default;
            Assert.That(player.Name, Is.EqualTo(string.Empty));

            LobbyActionRequested request = default;
            Assert.That(request.Kind, Is.EqualTo(LobbyActionKind.Connect));
            Assert.That(request.Text, Is.EqualTo(string.Empty));
            Assert.That(request.Password, Is.EqualTo(string.Empty));
            Assert.That(request.HasPassword, Is.False);
        }

        private static IEnumerable<string> Repeat(string value, int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return value;
            }
        }
    }
}

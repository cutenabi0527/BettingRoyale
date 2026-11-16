#if UNITY_EDITOR
using System.Text;
using BettingRoyale.Data.Online.Lobby;
using BettingRoyale.Network;
using NUnit.Framework;

namespace BettingRoyale.Editor
{
    /// <summary>
    /// <see cref="LobbyRoomAdmission"/> 의 입장 심사 규칙 테스트 (S-UX-BXXTHX).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>공개 헬퍼만 부른다.</b> 리플렉션으로 소금·검증값을 들여다보지 않고, 비밀번호나 토큰 내용을
    /// 실패 메시지에 싣지도 않는다 — 테스트 로그도 공유되는 기록이다.
    /// </para>
    /// <para>
    /// EditMode 전용이다. <c>NetworkManager</c> 는 Fusion·UnityEngine 없이는 돌지 않으므로
    /// 여기서 확인하는 것은 <b>규칙</b>이고, Host 콜백(<c>OnConnectRequest</c>)이 그 답만 쓴다.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class LobbyRoomAdmissionTests
    {
        private const string RoomCode = "ROOM-1234";
        private const string Password = "열쇠pw-1";

        private static byte[] Token(string roomCode, string password) =>
            LobbyRoomAdmission.BuildJoinToken(roomCode, password);

        // ── 잠긴 방: 맞는 토큰 / 틀린 토큰 ─────────────────────────────

        [Test]
        public void CorrectToken_IsAdmitted()
        {
            var admission = new LobbyRoomAdmission();
            Assert.IsTrue(admission.Lock(RoomCode, Password), "방이 잠기지 않았다.");
            Assert.IsTrue(admission.IsLocked);

            Assert.IsTrue(admission.IsAdmitted(Token(RoomCode, Password)));
        }

        [Test]
        public void WrongPassword_IsRefused()
        {
            var admission = new LobbyRoomAdmission();
            Assert.IsTrue(admission.Lock(RoomCode, Password));

            Assert.IsFalse(admission.IsAdmitted(Token(RoomCode, "열쇠pw-2")));
        }

        [Test]
        public void NullToken_IsRefusedWhileLocked()
        {
            var admission = new LobbyRoomAdmission();
            Assert.IsTrue(admission.Lock(RoomCode, Password));

            // 비밀번호 없이 붙는 클라이언트가 이 경로다.
            Assert.IsFalse(admission.IsAdmitted(null));
        }

        [Test]
        public void MalformedTokenLengths_AreRefused()
        {
            var admission = new LobbyRoomAdmission();
            Assert.IsTrue(admission.Lock(RoomCode, Password));

            Assert.IsFalse(admission.IsAdmitted(new byte[0]), "빈 토큰");
            Assert.IsFalse(admission.IsAdmitted(new byte[LobbyRoomAdmission.TokenBytes - 1]), "짧은 토큰");
            Assert.IsFalse(admission.IsAdmitted(new byte[LobbyRoomAdmission.TokenBytes + 1]), "긴 토큰");
        }

        [Test]
        public void OversizedToken_IsRefusedWithoutHashing()
        {
            var admission = new LobbyRoomAdmission();
            Assert.IsTrue(admission.Lock(RoomCode, Password));

            // 상한을 넘는 버퍼는 해싱조차 하지 않는다. 결과는 어쨌든 거부여야 한다.
            Assert.IsFalse(admission.IsAdmitted(new byte[LobbyRoomAdmission.MaxTokenBytes + 1]));
            Assert.IsFalse(admission.IsAdmitted(new byte[4096]));
        }

        // ── 잠기지 않은 방(기존 방·훈련장) ─────────────────────────────

        [Test]
        public void UnlockedRoom_AdmitsEveryone()
        {
            var admission = new LobbyRoomAdmission();

            Assert.IsFalse(admission.IsLocked, "새 인스턴스는 잠겨 있지 않아야 한다.");
            Assert.IsTrue(admission.IsAdmitted(null), "기존 방·훈련장 경로");
            Assert.IsTrue(admission.IsAdmitted(new byte[LobbyRoomAdmission.TokenBytes]));
            Assert.IsTrue(admission.IsAdmitted(new byte[1]));
        }

        [Test]
        public void EmptyPassword_DoesNotLock()
        {
            var admission = new LobbyRoomAdmission();

            Assert.IsFalse(admission.Lock(RoomCode, null));
            Assert.IsFalse(admission.Lock(RoomCode, string.Empty));
            Assert.IsFalse(admission.IsLocked);
            Assert.IsNull(Token(RoomCode, string.Empty), "잠그지 않은 방에는 토큰을 싣지 않는다.");
        }

        // ── 방 코드 결속 ───────────────────────────────────────────────

        [Test]
        public void Token_IsBoundToRoomCode_AndIsCaseSensitive()
        {
            var admission = new LobbyRoomAdmission();
            Assert.IsTrue(admission.Lock(RoomCode, Password));

            // 다른 방 코드로 만든 토큰은 통하지 않는다 — 한 방에서 새어 나가도 다른 방에 못 쓴다.
            Assert.IsFalse(admission.IsAdmitted(Token("ROOM-9999", Password)), "다른 방 코드");

            // 대소문자를 올리거나 내리지 않는다. 세션 이름은 대소문자를 구분한다.
            Assert.IsFalse(admission.IsAdmitted(Token(RoomCode.ToLowerInvariant(), Password)), "소문자 방 코드");
        }

        [Test]
        public void Token_IgnoresSurroundingWhitespaceInRoomCodeOnly()
        {
            var admission = new LobbyRoomAdmission();
            Assert.IsTrue(admission.Lock("  " + RoomCode + "  ", Password));

            // 방 코드는 앞뒤 공백만 떼고 쓴다(StartSession 이 세션 이름에 하는 것과 같다).
            Assert.IsTrue(admission.IsAdmitted(Token(RoomCode, Password)));
        }

        [Test]
        public void LockWithoutRoomCode_Fails()
        {
            var admission = new LobbyRoomAdmission();

            Assert.IsFalse(admission.Lock(null, Password));
            Assert.IsFalse(admission.Lock("   ", Password));
            Assert.IsFalse(admission.IsLocked, "실패한 잠금이 상태를 남기면 안 된다.");
            Assert.IsNull(Token(null, Password));
        }

        // ── 재잠금 · 해제 ──────────────────────────────────────────────

        [Test]
        public void Relock_WithDifferentPassword_InvalidatesOldToken()
        {
            var admission = new LobbyRoomAdmission();
            Assert.IsTrue(admission.Lock(RoomCode, Password));
            byte[] oldToken = Token(RoomCode, Password);

            Assert.IsTrue(admission.Lock(RoomCode, "다른pw-9"));

            // 두 비밀번호가 동시에 통하면 방장이 바꾼 비밀번호가 의미를 잃는다.
            Assert.IsFalse(admission.IsAdmitted(oldToken), "이전 비밀번호");
            Assert.IsTrue(admission.IsAdmitted(Token(RoomCode, "다른pw-9")));
        }

        [Test]
        public void Clear_UnlocksRoomAndForgetsVerifier()
        {
            var admission = new LobbyRoomAdmission();
            Assert.IsTrue(admission.Lock(RoomCode, Password));

            admission.Clear();

            Assert.IsFalse(admission.IsLocked);
            Assert.IsTrue(admission.IsAdmitted(null), "지운 뒤에는 잠기지 않은 방과 같아야 한다.");

            // 멱등이어야 한다 — DisposeRunner 가 여러 경로에서 부른다.
            admission.Clear();
            Assert.IsFalse(admission.IsLocked);
        }

        // ── 비밀번호 입력 규칙(도메인과 동일) ─────────────────────────

        [Test]
        public void MaxPasswordLength_MatchesDomainRule()
        {
            Assert.AreEqual(LobbyRoomRules.MaxPasswordLength, LobbyRoomAdmission.MaxPasswordLength);
        }

        [Test]
        public void WhitespaceOnlyPassword_IsAPassword()
        {
            // 도메인 규칙이 비밀번호를 trim 하지 않으므로 공백뿐인 값도 방을 잠근다.
            Assert.IsTrue(LobbyRoomAdmission.RequiresPassword("   "));
            Assert.IsTrue(LobbyRoomAdmission.IsAcceptablePassword("   "));
            Assert.AreEqual(1, LobbyRoomAdmission.LockedPropertyValue("   "));

            var admission = new LobbyRoomAdmission();
            Assert.IsTrue(admission.Lock(RoomCode, "   "));
            Assert.IsTrue(admission.IsAdmitted(Token(RoomCode, "   ")));
            Assert.IsFalse(admission.IsAdmitted(Token(RoomCode, "  ")), "공백 개수도 그대로 센다.");
        }

        [Test]
        public void Password_PreservesExactCharacters()
        {
            var admission = new LobbyRoomAdmission();
            Assert.IsTrue(admission.Lock(RoomCode, " pw "));

            Assert.IsTrue(admission.IsAdmitted(Token(RoomCode, " pw ")));
            Assert.IsFalse(admission.IsAdmitted(Token(RoomCode, "pw")), "앞뒤 공백을 떼면 안 된다.");
        }

        [Test]
        public void ControlCharacterPassword_IsRejected()
        {
            Assert.IsFalse(LobbyRoomAdmission.IsAcceptablePassword("pw\nnew"));
            Assert.IsFalse(LobbyRoomAdmission.IsAcceptablePassword("pw\t1"));
            Assert.IsFalse(LobbyRoomAdmission.IsAcceptablePassword("pw\0"));

            var admission = new LobbyRoomAdmission();
            Assert.IsFalse(admission.Lock(RoomCode, "pw\nnew"), "제어 문자는 방을 열지 않는다.");
            Assert.IsFalse(admission.IsLocked);
            Assert.IsNull(Token(RoomCode, "pw\nnew"));
        }

        [Test]
        public void OverlengthPassword_IsRejectedAtTheDomainLimit()
        {
            string atLimit = new string('a', LobbyRoomAdmission.MaxPasswordLength);
            string overLimit = atLimit + "a";

            Assert.IsTrue(LobbyRoomAdmission.IsAcceptablePassword(atLimit));
            Assert.IsFalse(LobbyRoomAdmission.IsAcceptablePassword(overLimit));

            var admission = new LobbyRoomAdmission();
            Assert.IsFalse(admission.Lock(RoomCode, overLimit), "상한을 넘으면 자르지 않고 거절한다.");
            Assert.IsFalse(admission.IsLocked);
            Assert.IsNull(Token(RoomCode, overLimit));
        }

        [Test]
        public void LockedPropertyValue_IsOnlyZeroOrOne()
        {
            Assert.AreEqual(0, LobbyRoomAdmission.LockedPropertyValue(null));
            Assert.AreEqual(0, LobbyRoomAdmission.LockedPropertyValue(string.Empty));
            Assert.AreEqual(1, LobbyRoomAdmission.LockedPropertyValue(" "));
            Assert.AreEqual(1, LobbyRoomAdmission.LockedPropertyValue(Password));

            // 상한을 넘는 입력에도 값은 0/1 뿐이다 — 세션 속성에는 길이도 새지 않는다.
            Assert.AreEqual(1, LobbyRoomAdmission.LockedPropertyValue(new string('a', 512)));
        }

        [Test]
        public void Token_HasFixedLength_AndCarriesNoPlaintext()
        {
            byte[] token = Token(RoomCode, Password);

            Assert.IsNotNull(token);
            Assert.AreEqual(LobbyRoomAdmission.TokenBytes, token.Length);
            Assert.LessOrEqual(token.Length, LobbyRoomAdmission.MaxTokenBytes);

            // 원문 바이트가 그대로 실리지 않는다(값은 비교만 하고 출력하지 않는다).
            byte[] plaintext = Encoding.UTF8.GetBytes(Password);
            CollectionAssert.AreNotEqual(plaintext, token);
        }
    }
}
#endif

using System;
using System.Security.Cryptography;
using System.Text;
using BettingRoyale.Data.Online.Lobby;

namespace BettingRoyale.Network
{
    /// <summary>
    /// 비공개 방의 <b>입장 심사</b> (S-UX-BXXTHX "잘못된 비밀번호는 Host 가 거부한다").
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>계정 인증이 아니다.</b> 방 하나에 한정된 입장 암호이며 Ready 신원 게이트
    /// (S-ONL-ZJS2NM)를 대체하지 않는다. 전송 계층(<c>NetworkManager</c>)은 이 클래스에 묻고
    /// Accept/Refuse 만 하며, 규칙이 여기 있어야 EditMode 테스트가 실제 판정 코드를 부를 수 있다.
    /// </para>
    /// <para>
    /// <b>회선을 넘는 것.</b> 비밀번호 원문은 넘지 않는다 — <see cref="BuildJoinToken"/> 의
    /// <see cref="TokenBytes"/> 바이트 파생값만 <c>ConnectionToken</c> 에 실린다. Host 는 방마다
    /// 다른 무작위 소금이 섞인 해시만 메모리에 들고 비교한다. 세션 속성에 나가는 것은
    /// <see cref="LockedPropertyKey"/> 의 0/1 뿐이다.
    /// </para>
    /// <para>
    /// ⚠️ <b>한계.</b> Host 는 플레이어 PC 이고(D-030) 파생값은 그 방에 한해 재전송될 수 있다.
    /// 막는 것은 <b>비밀번호를 모르는 사람의 입장</b>이지 캡처·재생 공격이 아니다.
    /// 소금·검증값은 인스턴스 필드뿐이라 <see cref="Clear"/> 로 사라진다 — <c>static</c> 이면
    /// 도메인 리로드를 끈 설정(D-015)에서 이전 방의 검증값이 다음 방까지 살아남는다.
    /// </para>
    /// </remarks>
    public sealed class LobbyRoomAdmission
    {
        /// <summary>클라이언트가 보내는 검증 바이트의 <b>정확한</b> 길이. 다른 길이는 전부 거부한다.</summary>
        public const int TokenBytes = 32;

        /// <summary>
        /// 해싱이라도 해 볼 토큰의 상한. 길이는 공개 상수라 이것으로 거부해도 새는 정보가 없고,
        /// 상한이 없으면 아무나 붙여 보낸 거대한 버퍼를 Host 가 해싱하게 된다.
        /// </summary>
        public const int MaxTokenBytes = 64;

        /// <summary>
        /// 방 비밀번호 길이 상한(텍스트 요소 기준). <b>도메인 규칙의 별칭</b>이다 —
        /// 값을 여기서 따로 정하면 wizard 입력 검증과 전송 계층이 서로 다른 길이를 허용한다.
        /// </summary>
        /// <remarks>
        /// 넘으면 방을 만들지 않는다. 조용히 자르면 사용자가 입력한 것과 <b>다른</b> 비밀번호로
        /// 방이 열려, 정작 방장이 알려준 비밀번호로는 아무도 못 들어온다.
        /// </remarks>
        public const int MaxPasswordLength = LobbyRoomRules.MaxPasswordLength;

        /// <summary>
        /// 방이 잠겨 있는지만 알리는 <b>공개</b> 세션 속성 키. 값은 0 또는 1 이다.
        /// 목록의 자물쇠·랜덤 참가 후보 제외는 방에 들어가기 전에 판단해야 하는데,
        /// 세션 속성은 누구나 읽으므로 실을 수 있는 것은 이 한 비트뿐이다.
        /// </summary>
        public const string LockedPropertyKey = "locked";

        private const int SaltBytes = 16;

        /// <summary>
        /// 파생 반복 수. 입장 때 한 번만 도는 비용이다.
        /// ⚠️ 바꾸면 구버전 피어의 토큰과 맞지 않으므로 <see cref="DeriveDomain"/> 버전도 함께 올린다.
        /// </summary>
        private const int DeriveIterations = 12000;

        /// <summary>파생 규약 이름 + 버전. 방 코드와 함께 KDF 소금을 이룬다.</summary>
        private const string DeriveDomain = "BettingRoyale.RoomAdmission.v1";

        /// <summary>이 방에서만 쓰는 무작위 소금. 메모리 한정이다.</summary>
        private byte[] salt;

        /// <summary><c>SHA256(salt ‖ token)</c>. 이것만 들고 있으면 토큰을 되돌릴 수 없다.</summary>
        private byte[] verifier;

        /// <summary>이 방이 비밀번호로 잠겨 있는가. 잠기지 않았으면 누구나 들어온다.</summary>
        public bool IsLocked => verifier != null && salt != null;

        /// <summary>비밀번호를 <b>설정한 것으로 볼</b> 입력인가. 빈 문자열·null 만 "없음" 이다.</summary>
        /// <remarks>
        /// ⚠️ <b>공백만 입력한 비밀번호도 비밀번호다</b> — 도메인 규칙
        /// (<see cref="LobbyRoomRules.TryNormalizePassword"/>)이 비밀번호를 trim 하지 않기 때문이다.
        /// 여기서 <c>IsNullOrWhiteSpace</c> 로 걸러내면, wizard 는 유효하다고 받아들인 입력으로
        /// 방이 <b>잠기지 않은 채</b> 열려 목록의 자물쇠와 실제 심사가 어긋난다.
        /// </remarks>
        public static bool RequiresPassword(string password) => !string.IsNullOrEmpty(password);

        /// <summary>
        /// 이 비밀번호로 방을 만들 수 있는가. 비어 있으면(= 잠그지 않음) 참이다.
        /// 판정은 도메인 규칙 하나뿐이다 — 16 텍스트 요소 · UTF-8 64 바이트 이하 · 제어 문자 금지.
        /// </summary>
        public static bool IsAcceptablePassword(string password) =>
            !RequiresPassword(password)
            || LobbyRoomRules.TryNormalizePassword(password, out _, out _);

        /// <summary>
        /// 세션 속성에 실을 잠금 표시(0/1). <b>여기서 나가는 것은 이 숫자뿐이다.</b>
        /// </summary>
        public static int LockedPropertyValue(string password) => RequiresPassword(password) ? 1 : 0;

        /// <summary>
        /// 이 방을 잠근다. <b>Host 만, 방을 만드는 순간 한 번</b> 부른다.
        /// </summary>
        /// <param name="roomCode">
        /// 방 코드(= 세션 이름). <b>비워 둘 수 없다</b> — 검증값이 방 코드에 묶여 있어서,
        /// 코드를 모른 채 잠그면 참가자가 같은 파생값을 만들 방법이 없다.
        /// </param>
        /// <returns>실제로 잠겼으면 <c>true</c>. <c>false</c> 면 방을 열지 않는 편이 낫다.</returns>
        public bool Lock(string roomCode, string password)
        {
            // 다시 잠글 때 옛 검증값이 남아 있으면 두 비밀번호가 동시에 통한다.
            Clear();

            if (!RequiresPassword(password) || !IsAcceptablePassword(password))
            {
                return false;
            }

            byte[] token = BuildJoinToken(roomCode, password);
            if (token == null)
            {
                return false;
            }

            var freshSalt = new byte[SaltBytes];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(freshSalt);
            }

            byte[] digest = Digest(freshSalt, token);

            // 파생값 자체는 검증에 필요 없다. 들고 있으면 새어 나갈 곳만 늘어난다.
            Wipe(token);

            salt = freshSalt;
            verifier = digest;
            return true;
        }

        /// <summary>
        /// 검증값을 지운다. <b>방을 떠나거나 세션 시작이 실패하면 반드시 부른다.</b>
        /// 지우지 않으면 다음에 만든 공개 방이 이전 방의 비밀번호로 심사되어 아무도 못 들어온다.
        /// </summary>
        public void Clear()
        {
            Wipe(salt);
            Wipe(verifier);
            salt = null;
            verifier = null;
        }

        /// <summary>
        /// 클라이언트가 보낼 검증 바이트를 만든다. 비밀번호나 방 코드가 없으면 <c>null</c>.
        /// </summary>
        /// <remarks>
        /// 방 코드가 KDF 소금에 들어가므로 <b>같은 비밀번호라도 방마다 다른 값</b>이 된다 —
        /// 한 방에서 새어 나간 토큰이 다른 방에 통하지 않는다. 방 코드는 앞뒤 공백만 떼고
        /// <b>대소문자를 그대로 둔다</b>(세션 이름이 대소문자를 구분한다). 비밀번호는 문자 하나도
        /// 바꾸지 않으며, 문자열을 보관하지 않고 받는 즉시 파생만 한다.
        /// </remarks>
        public static byte[] BuildJoinToken(string roomCode, string password)
        {
            if (!RequiresPassword(password) || !IsAcceptablePassword(password))
            {
                return null;
            }

            string code = NormalizeRoomCode(roomCode);
            if (code.Length == 0)
            {
                return null;
            }

            byte[] secret = Encoding.UTF8.GetBytes(password);
            byte[] kdfSalt = Sha256(Encoding.UTF8.GetBytes(DeriveDomain + "|" + code));

            try
            {
                using var kdf = new Rfc2898DeriveBytes(secret, kdfSalt, DeriveIterations, HashAlgorithmName.SHA256);
                return kdf.GetBytes(TokenBytes);
            }
            finally
            {
                Wipe(secret);
            }
        }

        /// <summary>
        /// 이 토큰으로 들어와도 되는가. <b>Host 의 <c>OnConnectRequest</c> 가 이 답만 쓴다.</b>
        /// 잠기지 않은 방은 토큰을 보지 않고 통과시킨다 — 기존 방·훈련장이 전부 이 경로다.
        /// </summary>
        /// <remarks>
        /// ⚠️ 내용 비교는 중간에 빠져나오지 않는다(타이밍 차이로 한 바이트씩 복원되는 것을 막는다).
        /// 길이 검사는 별개다 — 길이는 공개 상수라 숨길 것이 없다.
        /// </remarks>
        public bool IsAdmitted(byte[] token)
        {
            byte[] expected = verifier;
            byte[] currentSalt = salt;

            if (expected == null || currentSalt == null)
            {
                return true;
            }

            if (token == null)
            {
                return false;
            }

            // 망가진 입력을 먼저 자른다. 상한을 넘는 버퍼는 해싱조차 하지 않는다.
            if (token.Length > MaxTokenBytes || token.Length != TokenBytes)
            {
                return false;
            }

            byte[] candidate = Digest(currentSalt, token);

            var difference = 0;
            for (var i = 0; i < expected.Length; i++)
            {
                difference |= expected[i] ^ candidate[i];
            }

            Wipe(candidate);
            return difference == 0;
        }

        private static string NormalizeRoomCode(string roomCode) => (roomCode ?? string.Empty).Trim();

        private static byte[] Digest(byte[] saltBytes, byte[] token)
        {
            var buffer = new byte[saltBytes.Length + token.Length];
            Buffer.BlockCopy(saltBytes, 0, buffer, 0, saltBytes.Length);
            Buffer.BlockCopy(token, 0, buffer, saltBytes.Length, token.Length);

            try
            {
                return Sha256(buffer);
            }
            finally
            {
                Wipe(buffer);
            }
        }

        private static byte[] Sha256(byte[] payload)
        {
            using SHA256 sha = SHA256.Create();
            return sha.ComputeHash(payload);
        }

        private static void Wipe(byte[] buffer)
        {
            if (buffer != null)
            {
                Array.Clear(buffer, 0, buffer.Length);
            }
        }
    }
}

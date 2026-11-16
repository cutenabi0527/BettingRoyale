using System;

namespace BettingRoyale.Data.Dto
{
    // ─────────────────────────────────────────────────────────────
    //  회원가입·로그인 전송 형식. 정본은 docs/server/api.md 3-6 · 3-7 (D-064).
    //
    //  ⚠️ 필드 이름이 곧 계약이다. JsonUtility 는 이름으로만 맞추므로 바꾸면 조용히 null 이 된다.
    //     서버와 이름을 맞추기 위해 public 필드를 쓴다 — lint-rules.ps1 이 이 폴더를 예외로 둔다.
    //
    //  ⚠️ 비밀번호는 요청 DTO 에만 있다. 응답에도, 로그에도, UI 계약에도 두지 않는다.
    // ─────────────────────────────────────────────────────────────

    /// <summary>회원가입 요청 (api.md 3-6). 아이디는 이메일 형식이다.</summary>
    [Serializable]
    public sealed class SignUpRequestDto
    {
        public string loginId;
        public string password;
        public string nickname;
    }

    /// <summary>로그인 요청 (api.md 3-7).</summary>
    [Serializable]
    public sealed class LoginRequestDto
    {
        public string loginId;
        public string password;
    }

    /// <summary>
    /// 계정 정보. 가입 응답(3-6)과 로그인 응답(3-7)의 <c>account</c> 가 같은 형태다.
    /// </summary>
    [Serializable]
    public sealed class AccountDto
    {
        /// <summary>
        /// 서버가 발급하는 불투명 키. <b>사용자가 입력하는 로그인 아이디가 아니다.</b>
        /// 계약 상한은 32자 ASCII (api.md 3-2).
        /// </summary>
        public string accountId;

        public string nickname;
        public long medalBalance;

        /// <summary>
        /// 계정 생성 시 서버가 한 번 발급한 외형 프리셋 seed.
        /// 0은 이전 서버 응답 또는 미인증 상태를 뜻하며, 실제 계정 값으로 사용하지 않는다.
        /// </summary>
        public int appearancePresetSeed;

        /// <summary>
        /// 로비·대기방에 보이는 프로필 초상화 ID (api.md 3-8, S-UX-RQ5N9E).
        /// </summary>
        /// <remarks>
        /// <see cref="appearancePresetSeed"/> 와 다른 값이다 — 그쪽은 3D 캐릭터 외형이라 사람이 바꾸지 않는다.
        /// 비어 있으면 이전 서버 응답이라는 뜻이며 화면이 카탈로그 기본값으로 떨어뜨린다.
        /// </remarks>
        public string profilePortraitId;

        /// <summary>고를 수 있는 초상화. 소유 검증은 서버가 하며 화면은 이 목록만 그린다.</summary>
        public string[] ownedPortraitIds;
    }

    /// <summary>
    /// 프로필 초상화 변경 요청 본문 (api.md 3-9).
    /// </summary>
    [Serializable]
    public class ProfilePortraitRequestDto
    {
        public string profilePortraitId;
    }

    /// <summary>
    /// 로그인 응답 (api.md 3-7).
    /// </summary>
    /// <remarks>
    /// <c>accessToken</c> 은 <b>불투명 문자열</b>이다. 클라이언트는 디코드하지 않는다 —
    /// 만료는 <c>expiresInSeconds</c> 와 401 로만 안다.
    /// </remarks>
    [Serializable]
    public sealed class LoginResponseDto
    {
        public string accessToken;
        public string tokenType;
        public long expiresInSeconds;
        public AccountDto account;
    }
}

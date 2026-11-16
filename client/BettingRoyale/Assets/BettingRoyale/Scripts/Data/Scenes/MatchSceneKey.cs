using System;

namespace BettingRoyale.Data.Scenes
{
    /// <summary>
    /// 맵 로딩 방식과 무관하게 네트워크·매치 상태가 공유하는 안정적인 씬 식별자.
    /// Built-in 씬 이름이나 Addressables 주소는 카탈로그가 이 키에 매핑한다.
    /// </summary>
    public readonly struct MatchSceneKey : IEquatable<MatchSceneKey>
    {
        public MatchSceneKey(string value)
        {
            if (!IsValid(value))
            {
                throw new ArgumentException(
                    "씬 키는 소문자 영숫자와 '/', '-', '_'만 사용하고 구분자 사이가 비어 있으면 안 됩니다.",
                    nameof(value));
            }

            Value = value;
        }

        public string Value { get; }

        public bool Equals(MatchSceneKey other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is MatchSceneKey other && Equals(other);

        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(MatchSceneKey left, MatchSceneKey right) => left.Equals(right);

        public static bool operator !=(MatchSceneKey left, MatchSceneKey right) => !left.Equals(right);

        public static bool TryCreate(string value, out MatchSceneKey key)
        {
            if (!IsValid(value))
            {
                key = default;
                return false;
            }

            key = new MatchSceneKey(value);
            return true;
        }

        private static bool IsValid(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value[0] == '/'
                || value[value.Length - 1] == '/')
            {
                return false;
            }

            bool previousWasSeparator = false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool isSeparator = character == '/';
                bool isAllowed = character is >= 'a' and <= 'z'
                    || character is >= '0' and <= '9'
                    || character == '-'
                    || character == '_'
                    || isSeparator;

                if (!isAllowed || isSeparator && previousWasSeparator)
                {
                    return false;
                }

                previousWasSeparator = isSeparator;
            }

            return true;
        }
    }
}

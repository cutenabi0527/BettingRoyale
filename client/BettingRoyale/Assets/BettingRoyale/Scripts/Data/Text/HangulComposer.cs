using System;
using System.Globalization;
using System.Text;

namespace BettingRoyale.Data.Text
{
    /// <summary>
    /// 낱자(자모)를 받아 한글 음절로 합친다 (S-UX-XVGYXB).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 필요한가.</b> 화상 키보드는 <c>ㄱ</c>·<c>ㅏ</c> 같은 낱자를 하나씩 보낸다. 그것을
    /// 그대로 입력 칸에 이어 붙이면 <c>ㄲㅓ</c> 처럼 낱자가 나열될 뿐 <c>꺼</c> 가 되지 않는다
    /// (2026-09-18 사용자 화면 확인). 물리 키보드는 OS 의 IME 가 이 일을 해 주지만 화상
    /// 키보드에는 그 경로가 없다.
    /// </para>
    /// <para>
    /// <b>순수 계산이다.</b> <c>UnityEngine</c> 을 참조하지 않으므로 테스트에서 그대로 돌릴 수
    /// 있고, 화면이 없어도 규칙을 확인할 수 있다.
    /// </para>
    /// <para>
    /// 조합 중인 글자는 <see cref="Text"/> 의 <b>마지막 한 글자</b>로 보인다. 다음 낱자가
    /// 들어오면 그 자리에서 자라거나, 더 붙일 수 없으면 확정되고 새 글자가 시작된다.
    /// </para>
    /// </remarks>
    public sealed class HangulComposer
    {
        private const char SyllableBase = '가';
        private const int MediumCount = 21;
        private const int FinalCount = 28;

        /// <summary>초성 19자. 순서가 곧 유니코드 계산의 첨자다.</summary>
        private const string Initials = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";

        /// <summary>중성 21자.</summary>
        private const string Mediums = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";

        /// <summary>종성 28자. 첫 칸은 "종성 없음" 이다.</summary>
        private const string Finals = "\0ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ";

        /// <summary>
        /// 이미 놓인 종성에 자음을 하나 더 붙여 겹받침이 되는 짝.
        /// </summary>
        /// <remarks>
        /// 이것이 없으면 <c>앉</c>·<c>없</c> 처럼 겹받침이 있는 글자를 칠 수 없다 — 자판에는
        /// 겹받침 글쇠가 없기 때문이다.
        /// </remarks>
        private static readonly (char Held, char Added, char Merged)[] FinalPairs =
        {
            ('ㄱ', 'ㅅ', 'ㄳ'),
            ('ㄴ', 'ㅈ', 'ㄵ'), ('ㄴ', 'ㅎ', 'ㄶ'),
            ('ㄹ', 'ㄱ', 'ㄺ'), ('ㄹ', 'ㅁ', 'ㄻ'), ('ㄹ', 'ㅂ', 'ㄼ'),
            ('ㄹ', 'ㅅ', 'ㄽ'), ('ㄹ', 'ㅌ', 'ㄾ'), ('ㄹ', 'ㅍ', 'ㄿ'), ('ㄹ', 'ㅎ', 'ㅀ'),
            ('ㅂ', 'ㅅ', 'ㅄ'),
        };

        /// <summary>
        /// 이미 놓인 중성에 모음을 하나 더 붙여 겹모음이 되는 짝.
        /// </summary>
        /// <remarks>
        /// 자판에 <c>ㅘ</c> 가 따로 있어도 <c>ㅗ</c>+<c>ㅏ</c> 로 치는 사람이 있다. 둘 다 받는다.
        /// </remarks>
        private static readonly (char Held, char Added, char Merged)[] MediumPairs =
        {
            ('ㅗ', 'ㅏ', 'ㅘ'), ('ㅗ', 'ㅐ', 'ㅙ'), ('ㅗ', 'ㅣ', 'ㅚ'),
            ('ㅜ', 'ㅓ', 'ㅝ'), ('ㅜ', 'ㅔ', 'ㅞ'), ('ㅜ', 'ㅣ', 'ㅟ'),
            ('ㅡ', 'ㅣ', 'ㅢ'),
        };

        private readonly StringBuilder settled = new StringBuilder();
        private int initial = -1, medium = -1, final;

        /// <summary>조합 중인 글자까지 포함한 지금 값.</summary>
        public string Text => settled.ToString() + Pending();

        /// <summary>지금 조합 중인 글자가 있는가.</summary>
        public bool IsComposing => initial >= 0 || medium >= 0;

        /// <summary>바깥에서 값을 통째로 바꾼다. 조합 중이던 글자는 버린다.</summary>
        public void Reset(string value)
        {
            settled.Clear();
            if (!string.IsNullOrEmpty(value)) settled.Append(value);
            initial = medium = -1;
            final = 0;
        }

        /// <summary>낱자 하나를 넣는다. 한글이 아니면 그대로 이어 붙인다.</summary>
        public void Append(string glyph)
        {
            if (string.IsNullOrEmpty(glyph)) return;
            if (glyph.Length != 1) { Commit(); settled.Append(glyph); return; }

            char c = glyph[0];
            if (Initials.IndexOf(c) < 0 && Mediums.IndexOf(c) < 0)
            {
                // 한글 낱자가 아니다 — 영문·숫자·기호·공백은 조합하지 않는다.
                Commit();
                settled.Append(c);
                return;
            }

            if (Mediums.IndexOf(c) >= 0) AppendMedium(c);
            else AppendConsonant(c);
        }

        /// <summary>
        /// <paramref name="glyph"/> 를 넣었을 때의 값을 <b>상태를 바꾸지 않고</b> 돌려준다.
        /// </summary>
        /// <remarks>
        /// 글자 수 한계를 판정하려고 둔다 (S15P21A606-292). 낱자 하나가 글자 수를 늘리는지는 조합
        /// 상태에 따라 달라서 — <c>가</c>+<c>ㄴ</c> 은 그대로 한 글자(<c>간</c>)지만 <c>간</c>+<c>ㅏ</c> 는 두 글자
        /// (<c>가나</c>)다 — 규칙을 따로 흉내 내면 반드시 어긋난다. 실제로 넣어 보고 되돌린다.
        /// <see cref="Append"/> 는 확정 부분에 덧붙이기만 하므로 길이를 되돌리면 원상태가 된다.
        /// </remarks>
        public string TextAfter(string glyph)
        {
            int settledLength = settled.Length;
            int savedInitial = initial, savedMedium = medium, savedFinal = final;
            Append(glyph);
            string result = Text;
            settled.Length = settledLength;
            initial = savedInitial;
            medium = savedMedium;
            final = savedFinal;
            return result;
        }

        /// <summary>한 글자 지운다. 조합 중이면 <b>낱자 단위로</b> 되돌린다.</summary>
        /// <remarks>
        /// 조합 중인 글자를 통째로 지우면 <c>각</c> 에서 받침만 빼려던 사람이 글자를 잃는다.
        /// </remarks>
        public void Backspace()
        {
            if (final > 0)
            {
                char held = Finals[final];
                foreach (var pair in FinalPairs)
                {
                    if (pair.Merged != held) continue;
                    final = Finals.IndexOf(pair.Held);
                    return;
                }
                final = 0;
                return;
            }

            if (medium >= 0)
            {
                char held = Mediums[medium];
                foreach (var pair in MediumPairs)
                {
                    if (pair.Merged != held) continue;
                    medium = Mediums.IndexOf(pair.Held);
                    return;
                }
                medium = -1;
                return;
            }

            if (initial >= 0) { initial = -1; return; }
            if (settled.Length == 0) return;

            // 확정된 글자는 <b>사람이 세는 한 글자</b>(텍스트 요소)를 통째로 지운다. UTF-16 한 칸만
            // 지우면 이모지 같은 서로게이트 쌍이 반쪽으로 남아 깨진 글자가 된다.
            string value = settled.ToString();
            int[] starts = StringInfo.ParseCombiningCharacters(value);
            settled.Length = starts.Length > 0 ? starts[starts.Length - 1] : 0;
        }

        /// <summary>조합을 끝낸다. 입력 칸을 떠날 때 부른다.</summary>
        public void Commit()
        {
            string pending = Pending();
            if (pending.Length > 0) settled.Append(pending);
            initial = medium = -1;
            final = 0;
        }

        private void AppendConsonant(char c)
        {
            if (initial < 0)
            {
                // ⚠️ 모음만 조합 중이었다면(<c>ㅏ</c>) 그 모음을 먼저 확정한다. 그대로 초성을 채우면
                //    <c>ㅏ</c>+<c>ㄱ</c> 이 <c>가</c> 로 합쳐져 친 순서가 뒤집혔다 (S15P21A606-292).
                if (medium >= 0) Commit();
                initial = Initials.IndexOf(c);
                return;
            }

            if (medium < 0)
            {
                // 초성만 있는데 자음이 또 왔다 — 앞 글자를 확정하고 새로 시작한다.
                Commit();
                initial = Initials.IndexOf(c);
                return;
            }

            int asFinal = Finals.IndexOf(c);
            if (final == 0 && asFinal > 0) { final = asFinal; return; }

            if (final > 0)
            {
                char held = Finals[final];
                foreach (var pair in FinalPairs)
                {
                    if (pair.Held != held || pair.Added != c) continue;
                    final = Finals.IndexOf(pair.Merged);
                    return;
                }
            }

            Commit();
            initial = Initials.IndexOf(c);
        }

        private void AppendMedium(char c)
        {
            if (medium < 0)
            {
                // 초성 없이 모음만 온 경우도 그대로 보인다 — 치는 도중에는 흔한 상태다.
                medium = Mediums.IndexOf(c);
                return;
            }

            if (final == 0)
            {
                char held = Mediums[medium];
                foreach (var pair in MediumPairs)
                {
                    if (pair.Held != held || pair.Added != c) continue;
                    medium = Mediums.IndexOf(pair.Merged);
                    return;
                }

                Commit();
                medium = Mediums.IndexOf(c);
                return;
            }

            // ⚠️ <b>받침이 다음 글자의 초성으로 넘어간다.</b> <c>간</c> 에 <c>ㅏ</c> 를 치면
            //    <c>가나</c> 가 되어야 한다. 겹받침이면 뒤쪽 하나만 넘긴다.
            char carried = Finals[final];
            char stays = '\0';
            foreach (var pair in FinalPairs)
            {
                if (pair.Merged != carried) continue;
                stays = pair.Held;
                carried = pair.Added;
                break;
            }

            final = stays == '\0' ? 0 : Finals.IndexOf(stays);
            Commit();
            initial = Initials.IndexOf(carried);
            medium = Mediums.IndexOf(c);
        }

        /// <summary>조합 중인 글자 하나를 글자로 만든다.</summary>
        private string Pending()
        {
            if (initial >= 0 && medium >= 0)
                return ((char)(SyllableBase + (initial * MediumCount + medium) * FinalCount + final)).ToString();

            if (initial >= 0) return Initials[initial].ToString();
            if (medium >= 0) return Mediums[medium].ToString();
            return string.Empty;
        }
    }
}

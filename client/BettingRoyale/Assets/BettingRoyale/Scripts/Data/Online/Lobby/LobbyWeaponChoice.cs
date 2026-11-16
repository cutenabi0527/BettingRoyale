using System.Collections.Generic;
using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Common;

namespace BettingRoyale.Data.Online.Lobby
{
    /// <summary>
    /// 대기방에서 참가자가 고르는 <b>이번 매치의 무기 선택 의사</b> ([S-UX-NDANX2]).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 <see cref="WeaponType"/> 을 그대로 쓰지 않는가.</b> '랜덤' 은 무기가 아니라
    /// <b>무기를 아직 고르지 않겠다는 의사</b>다. 이것을 <see cref="WeaponType"/> 에 멤버로 넣으면
    /// 장착·스폰·축복 후보처럼 "무기를 받는" 모든 경로가 <i>존재하지 않는 무기</i>를 들 수 있게 된다 —
    /// 그 순간 <c>WeaponCatalog</c> 가 태그를 못 찾아 조용히 비무장이 되고, 컴파일로는 절대 안 잡힌다.
    /// 그래서 선택 의사와 실제 무기를 <b>다른 타입</b>으로 갈라 두고 경계에서만 변환한다
    /// (<see cref="LobbyWeaponChoiceRules.TryGetWeaponType"/>).
    /// </para>
    /// <para>
    /// ⚠️ 이 값은 <c>PlayerInfo.WeaponChoice</c> 로 <b>네트워크 복제 경계를 넘는다.</b>
    /// 기존 멤버의 숫자 값을 바꾸지 않는다 — <see cref="WeaponType"/> 과 같은 규칙이다.
    /// 새 선택지는 뒤에 추가만 한다.
    /// </para>
    /// <para>
    /// 0 번이 <see cref="Sword"/> 인 것은 우연이 아니다. <c>[Networked]</c> 필드는 채워지기 전에
    /// 0 이므로, 아무도 고르지 않은 상태가 곧 기존 기본값(S-GD-4YA4SE, 전원 검)과 같아진다.
    /// </para>
    /// </remarks>
    public enum LobbyWeaponChoice
    {
        /// <summary>검. 아무도 고르지 않았을 때의 값이기도 하다.</summary>
        Sword = 0,

        /// <summary>활.</summary>
        Bow = 1,

        /// <summary>
        /// 랜덤 — <b>무기가 아니라 "Host 가 정해 달라" 는 의사</b>다.
        /// 매치 시작 시 Host 가 <see cref="Sword"/> 나 <see cref="Bow"/> 로 확정한다 ([S-GD-29RMAQ]).
        /// </summary>
        Random = 2,
    }

    /// <summary>무기 선택을 거절한 이유. 회선에 전달하므로 기존 숫자는 유지한다.</summary>
    public enum LobbyWeaponChoiceRejectReason
    {
        None = 0,
        MatchStarted = 1,
        Ready = 2,
        UnsupportedChoice = 3,

        /// <summary>
        /// 요청을 실어 보낼 내 참가자(<c>PlayerInfo</c>)가 없다 — 방에 들어가기 전이거나 스폰 전이다
        /// (S15P21A606-268). Host 가 아니라 <b>로컬 세션이 판정</b>하며 회선에 실리지 않는다.
        /// 이때는 확정된 무기 값도 없으므로 <c>CurrentChoice</c> 는 기본값을 담는다.
        /// </summary>
        NotInRoom = 4,
    }

    /// <summary>무기 선택·확정 규칙. 표시 문구는 UI가 매핑한다.</summary>
    public static class LobbyWeaponChoiceRules
    {
        /// <summary>아무도 고르지 않았을 때의 선택. <c>default(LobbyWeaponChoice)</c> 와 같아야 한다.</summary>
        public const LobbyWeaponChoice Default = LobbyWeaponChoice.Sword;

        /// <summary>Host의 잠금과 입력을 판정한다. 클라이언트의 표시 상태를 신뢰하지 않는다.</summary>
        public static LobbyWeaponChoiceRejectReason RejectionFor(
            LobbyWeaponChoice choice, bool matchLocked, bool isReady)
        {
            if (matchLocked) return LobbyWeaponChoiceRejectReason.MatchStarted;
            if (isReady) return LobbyWeaponChoiceRejectReason.Ready;
            return IsSupported(choice)
                ? LobbyWeaponChoiceRejectReason.None
                : LobbyWeaponChoiceRejectReason.UnsupportedChoice;
        }

        /// <summary>
        /// 이 저장소가 실제로 다룰 수 있는 값인가. <b>복제된 값을 그대로 믿지 않기 위한 관문</b>이다 —
        /// 열거형은 정의되지 않은 숫자도 담을 수 있으므로, RPC 로 도착한 값은 먼저 여기를 통과시킨다.
        /// </summary>
        public static bool IsSupported(LobbyWeaponChoice choice)
        {
            return choice == LobbyWeaponChoice.Sword
                || choice == LobbyWeaponChoice.Bow
                || choice == LobbyWeaponChoice.Random;
        }

        /// <summary>이미 무기가 정해진 선택인가. <see cref="LobbyWeaponChoice.Random"/> 만 <c>false</c> 다.</summary>
        public static bool IsDecided(LobbyWeaponChoice choice)
        {
            return choice == LobbyWeaponChoice.Sword || choice == LobbyWeaponChoice.Bow;
        }

        /// <summary>알 수 없는 값을 기본값으로 떨어뜨린다. 빈 칸을 만들지 않기 위한 것이다.</summary>
        public static LobbyWeaponChoice Normalize(LobbyWeaponChoice choice)
        {
            return IsSupported(choice) ? choice : Default;
        }

        /// <summary>
        /// 랜덤을 실제 무기로 확정한다 ([S-GD-29RMAQ]). 이미 정해진 선택은 <b>그대로 돌려준다</b> —
        /// 두 번 불러도 결과가 바뀌지 않아야 매치 시작이 두 번 관측돼도 무기가 흔들리지 않는다.
        /// </summary>
        /// <param name="roll">
        /// 뽑기값. <b>난수 생성을 이 함수 안에 두지 않는 이유</b>는 그렇게 하면 결과를 검증할 수 없기
        /// 때문이다. 권위를 가진 쪽(Host)이 뽑아서 넣고, 여기서는 해석만 한다.
        /// 음수도 안전하다 — 최하위 비트만 본다.
        /// </param>
        public static LobbyWeaponChoice Decide(LobbyWeaponChoice choice, int roll)
        {
            if (!IsSupported(choice))
            {
                return Default;
            }

            if (IsDecided(choice))
            {
                return choice;
            }

            return (roll & 1) == 0 ? LobbyWeaponChoice.Sword : LobbyWeaponChoice.Bow;
        }

        /// <summary>대기방 봇 무기 뽑기에 섞는 값 — 같은 시드를 쓰는 봇 성향 뽑기와 결과가 엮이지 않게 한다.</summary>
        private const int BotWeaponSalt = 0x3C6EF372;

        /// <summary>
        /// 대기방 봇의 이번 매치 무기 — 봇의 "랜덤" 을 검·활 반반으로 확정한다 (S-GD-XJ4WMT, 정민호 2026-09-28 "봇은 랜덤 무기여야 한다").
        /// </summary>
        /// <remarks>
        /// 매치 ID·봇 참가자 키로 시드를 만든다(<see cref="LobbyBotPersona.Seed"/>) — 같은 매치의 같은 봇은 몇 번 물어도 같은 무기라,
        /// 축복 후보 필터·봇 성향·손에 든 무기가 어긋나지 않는다(S-GD-K8X2V8). 매치가 바뀌면 다시 뽑힌다.
        /// </remarks>
        public static LobbyWeaponChoice DecideBotWeapon(string matchId, int botParticipantId)
        {
            var random = new DeterministicRandom(LobbyBotPersona.Seed(matchId, botParticipantId) ^ BotWeaponSalt);
            return Decide(LobbyWeaponChoice.Random, (int)(random.NextUInt() & 1u));
        }

        /// <summary>
        /// 실제 전투 무기로 바꾼다. <see cref="LobbyWeaponChoice.Random"/> 은 <c>false</c> 를 돌려준다 —
        /// <b>확정을 건너뛴 경로가 조용히 검을 집어들지 않게</b> 하려는 것이다.
        /// 호출자는 먼저 <see cref="Decide"/> 로 확정해야 한다.
        /// </summary>
        public static bool TryGetWeaponType(LobbyWeaponChoice choice, out WeaponType weapon)
        {
            switch (choice)
            {
                case LobbyWeaponChoice.Sword:
                    weapon = WeaponType.Sword;
                    return true;
                case LobbyWeaponChoice.Bow:
                    weapon = WeaponType.Bow;
                    return true;
                default:
                    weapon = WeaponType.None;
                    return false;
            }
        }
    }

    /// <summary>
    /// 매치 시작 시점에 <b>Host 가 얼려 둔</b> 참가자별 무기 선택 (S-UX-NDANX2 · S-GD-29RMAQ).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 복제값을 다시 읽지 않고 얼리는가.</b> 무기 선택의 원본은 <c>PlayerInfo.WeaponChoice</c>
    /// 라는 <c>[Networked]</c> 필드인데, <b>그 오브젝트는 매치 씬 전환을 넘지 못한다</b> — Fusion 이
    /// 이전 씬의 동적 오브젝트를 정리하고 Host 가 새로 스폰하면, 새 인스턴스의 값은 복제 기본값인
    /// 0(= <see cref="LobbyWeaponChoice.Sword"/>)이다. 그래서 대기방에서 활을 고른 사람이 전투에서
    /// 검을 들었다. 0 이 "검을 골랐다" 와 "아직 아무것도 안 들어왔다" 를 구분하지 못하는 것이
    /// 이 고장이 조용했던 이유다.
    /// </para>
    /// <para>
    /// 계정 외형 seed 를 Host 딕셔너리에 얼려 두는 것과 <b>같은 처방</b>이다. 씬을 넘어 살아야 하는
    /// 값은 복제 오브젝트가 아니라 Host 의 기억에 둔다.
    /// </para>
    /// <para>
    /// <b>적용 여부를 함께 들고 있는 이유.</b> 전투 오브젝트에 무기를 들려 주는 기회는 스폰 직후
    /// 한 번뿐이었는데, 그 한 번이 <c>Spawned()</c> 가 아직 돌지 않은 틱에 걸리면 장비 드라이버가
    /// 없어 조용히 실패했다. 미적용을 기억해 두면 다음 보장 루프가 다시 시도할 수 있고,
    /// <b>이미 적용한 사람에게는 다시 손대지 않아</b> 매치 도중 바뀐 장비를 되돌리지 않는다.
    /// </para>
    /// <para>
    /// 키는 <c>PlayerSlot.PlayerId</c> 와 같은 세션 한정 번호다. 매치가 시작될 때마다 통째로 다시
    /// 뜨므로 지난 세션의 번호가 남아 남의 무기를 가리키지 않는다.
    /// </para>
    /// </remarks>
    public sealed class MatchWeaponChoiceRoster
    {
        private readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();

        /// <summary>얼려 둔 참가자 수. 0 이면 아직 매치 명부를 뜨지 않은 것이다.</summary>
        public int Count => entries.Count;

        /// <summary>명부를 버린다. 매치를 새로 뜨기 전에 부른다.</summary>
        public void Clear() => entries.Clear();

        /// <summary>
        /// 이 참가자의 선택을 매치 명부에 얼린다. 같은 사람을 다시 담으면 <b>덮어쓰고</b>
        /// 적용 여부도 초기화한다 — 재대결은 새 매치이므로 지난 회차의 장착 기록을 물려받지 않는다.
        /// </summary>
        /// <returns>실제로 담긴 값. 알 수 없는 값은 <see cref="LobbyWeaponChoiceRules.Default"/> 로 떨어진다.</returns>
        public LobbyWeaponChoice Capture(int playerId, LobbyWeaponChoice choice)
        {
            LobbyWeaponChoice normalized = LobbyWeaponChoiceRules.Normalize(choice);
            entries[playerId] = new Entry(normalized, applied: false);
            return normalized;
        }

        /// <summary>
        /// 얼려 둔 선택. 명부에 없으면 <c>false</c> 이고, 호출자는 "모른다" 와 "검을 골랐다" 를
        /// 구분할 수 있어야 한다.
        /// </summary>
        public bool TryGet(int playerId, out LobbyWeaponChoice choice)
        {
            if (entries.TryGetValue(playerId, out Entry entry))
            {
                choice = entry.Choice;
                return true;
            }

            choice = LobbyWeaponChoiceRules.Default;
            return false;
        }

        /// <summary>
        /// 아직 전투 오브젝트에 들려 주지 못한 확정 무기가 있는가.
        /// <see cref="LobbyWeaponChoice.Random"/> 은 확정이 아니므로 <c>false</c> 다 —
        /// 확정을 건너뛴 경로가 재시도를 반복하게 두지 않는다.
        /// </summary>
        public bool NeedsApply(int playerId)
        {
            return entries.TryGetValue(playerId, out Entry entry)
                && !entry.Applied
                && LobbyWeaponChoiceRules.IsDecided(entry.Choice);
        }

        /// <summary>이 참가자에게 얼려 둔 무기를 실제로 들려 줬다고 기록한다.</summary>
        public void MarkApplied(int playerId)
        {
            if (entries.TryGetValue(playerId, out Entry entry))
            {
                entries[playerId] = new Entry(entry.Choice, applied: true);
            }
        }

        /// <summary>이 참가자에게 얼려 둔 무기를 이미 들려 줬는가.</summary>
        public bool IsApplied(int playerId) =>
            entries.TryGetValue(playerId, out Entry entry) && entry.Applied;

        private readonly struct Entry
        {
            public Entry(LobbyWeaponChoice choice, bool applied)
            {
                Choice = choice;
                Applied = applied;
            }

            public LobbyWeaponChoice Choice { get; }

            public bool Applied { get; }
        }
    }
}

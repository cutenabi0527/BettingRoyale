using UnityEngine;

namespace BettingRoyale.Data.Combat
{
    // 조작 의도(명령) 계약 모음. 이동·조준·대시·공격은 서로 독립이라 어느 조합으로든
    // 같은 틱에 함께 발생할 수 있다 (S-GD-7DRYPY).
    // 추후 Fusion 어댑터(Scripts/Network)가 이 구조체들을 네트워크 입력으로/에서 번역한다 (D-044) —
    // 이 파일은 Fusion 을 모른다.
    // 입력 스킴(S-UX-6E6C6E: 키보드마우스 / 게임패드)과 무관하게 방향은 계산이 끝난 2D 벡터로 통일한다.

    /// <summary>이동 의도. 방향만 담는다 — 속도는 <see cref="CombatantConfig.MoveSpeed"/> 가 정한다.</summary>
    public readonly struct MoveCommand
    {
        /// <summary>이동 방향(정규화). 입력이 없으면 zero.</summary>
        public readonly Vector2 Direction;

        public MoveCommand(Vector2 direction) => Direction = direction;
    }

    /// <summary>조준 의도. 마우스든 오른쪽 스틱이든 방향으로 환산되어 도착한다.</summary>
    public readonly struct AimCommand
    {
        public readonly Vector2 Direction;

        public AimCommand(Vector2 direction) => Direction = direction;
    }

    /// <summary>대시 의도. 속도·지속·쿨타임은 <see cref="DashConfig"/> 가 정한다.</summary>
    public readonly struct DashCommand
    {
        public readonly Vector2 Direction;

        public DashCommand(Vector2 direction) => Direction = direction;
    }

    /// <summary>
    /// 공격 의도. 무기와 수치는 장착 상태(<see cref="WeaponType"/> + <see cref="WeaponConfig"/>)가
    /// 정하므로 페이로드가 없다.
    /// </summary>
    public readonly struct AttackCommand { }
}

using BettingRoyale.Core;
using UnityEngine;

namespace BettingRoyale.Data.Combat
{
    // 전투 이벤트 계약 모음. 기능 전용 이벤트는 기능 폴더에 둔다(GameEvents.cs 의 배치 규칙).
    // Network(추후 Fusion 어댑터)가 발행하고 Gameplay/UI 가 구독해야 하므로,
    // 양쪽이 모두 참조할 수 있는 Data 레이어에 있다 (D-044).
    // 소비 예정: 상자 열기 캔슬(이동·피격 시 취소, S-GD-7DRYPY), UI 연출, 축복 후보 필터링.

    /// <summary>전투 참여자가 실제로 이동했다(변위 발생). 상자 열기 캔슬의 이동 신호다 (S-GD-7DRYPY).</summary>
    public readonly struct CombatantMoved : IGameEvent
    {
        public readonly CombatantId Combatant;
        public readonly Vector3 Position;

        public CombatantMoved(CombatantId combatant, Vector3 position)
        {
            Combatant = combatant;
            Position = position;
        }
    }

    /// <summary>공격이 발동됐다. 명중 여부와 무관하다.</summary>
    public readonly struct AttackPerformed : IGameEvent
    {
        public readonly CombatantId Attacker;
        public readonly WeaponType Weapon;
        public readonly Vector2 AimDirection;

        public AttackPerformed(CombatantId attacker, WeaponType weapon, Vector2 aimDirection)
        {
            Attacker = attacker;
            Weapon = weapon;
            AimDirection = aimDirection;
        }
    }

    /// <summary>공격이 대상에게 명중했다. 피해 반영은 <see cref="DamageApplied"/> 가 따로 알린다.</summary>
    public readonly struct HitLanded : IGameEvent
    {
        public readonly CombatantId Attacker;
        public readonly CombatantId Target;
        public readonly WeaponType Weapon;

        public HitLanded(CombatantId attacker, CombatantId target, WeaponType weapon)
        {
            Attacker = attacker;
            Target = target;
            Weapon = weapon;
        }
    }

    /// <summary>
    /// 권위 명중이 실제 피해 또는 훈련 더미 측정을 끝낸 뒤에만 소비하는 표현 전용 통지다.
    /// 피해·빈사·상자 취소 같은 게임플레이 결과로 역변환하지 않는다.
    /// </summary>
    public readonly struct HitPresentationRequested : IGameEvent
    {
        public readonly CombatantId Attacker;
        public readonly CombatantId Target;
        public readonly WeaponType Weapon;
        public readonly Vector3 WorldContactPoint;

        public HitPresentationRequested(
            CombatantId attacker,
            CombatantId target,
            WeaponType weapon,
            Vector3 worldContactPoint)
        {
            Attacker = attacker;
            Target = target;
            Weapon = weapon;
            WorldContactPoint = worldContactPoint;
        }
    }

    /// <summary>피해가 적용됐다. 상자 열기 캔슬의 피격 신호다 (S-GD-7DRYPY). 피해량은 설정에서 온 값이다.</summary>
    public readonly struct DamageApplied : IGameEvent
    {
        public readonly CombatantId Attacker;
        public readonly CombatantId Target;
        public readonly WeaponType Weapon;
        public readonly float Amount;

        public readonly CombatDamageSource Source;

        public DamageApplied(CombatantId attacker, CombatantId target, WeaponType weapon, float amount, CombatDamageSource source = CombatDamageSource.Weapon)
        {
            Attacker = attacker;
            Target = target;
            Weapon = weapon;
            Amount = amount;
            Source = source;
        }
    }

    /// <summary>
    /// 보호막이 한 대를 <b>전부</b> 받아 체력이 깎이지 않았다. 킬·어시스트 기여로만 쓴다 — 보호막만 깎아도
    /// 어시스트다(사용자 확정 2026-09-18, S-GD-Y534KH).
    /// </summary>
    /// <remarks>
    /// 체력 피해가 아니라서 <see cref="DamageApplied"/> 를 내지 않는다. 그 이벤트는 피격 소리·연출·상자 취소·피해 집계가
    /// 함께 들으므로, 0 피해를 섞으면 그쪽 동작이 바뀐다. 체력도 일부 깎였으면 <see cref="DamageApplied"/> 하나로 충분하다.
    /// </remarks>
    public readonly struct DamageAbsorbedByShield : IGameEvent
    {
        public readonly CombatantId Attacker;
        public readonly CombatantId Target;
        public readonly WeaponType Weapon;
        public readonly float Amount;
        public readonly CombatDamageSource Source;

        public DamageAbsorbedByShield(CombatantId attacker, CombatantId target, WeaponType weapon, float amount,
            CombatDamageSource source)
        {
            Attacker = attacker;
            Target = target;
            Weapon = weapon;
            Amount = amount;
            Source = source;
        }
    }

    /// <summary>
    /// 불사 훈련 허수아비에 적용될 예정이었던 피해량. 실제 체력·빈사 상태는 바꾸지 않으며
    /// 표시 계층은 공격자의 전투 진영에만 이 수치를 노출한다.
    /// </summary>
    public readonly struct TrainingDummyDamageMeasured : IGameEvent
    {
        public readonly CombatantId Attacker;
        public readonly CombatantId Target;
        public readonly WeaponType Weapon;
        public readonly float Amount;

        public TrainingDummyDamageMeasured(
            CombatantId attacker,
            CombatantId target,
            WeaponType weapon,
            float amount)
        {
            Attacker = attacker;
            Target = target;
            Weapon = weapon;
            Amount = amount;
        }
    }

    /// <summary>전투 참여자가 빈사 상태가 됐다.</summary>
    public readonly struct CombatantDowned : IGameEvent
    {
        public readonly CombatantId Combatant;
        public readonly CombatantId Attacker;

        public readonly CombatDamageSource Source;

        public CombatantDowned(CombatantId combatant, CombatantId attacker, CombatDamageSource source = CombatDamageSource.Weapon)
        {
            Combatant = combatant;
            Attacker = attacker;
            Source = source;
        }
    }

    /// <summary>빈사 상태였던 참여자가 다음 라운드 시작과 함께 회복했다 (S-GD-7DRYPY).</summary>
    public readonly struct CombatantRecovered : IGameEvent
    {
        public readonly CombatantId Combatant;

        public CombatantRecovered(CombatantId combatant) => Combatant = combatant;
    }

    /// <summary>
    /// 대시가 시작됐다. <b>표현 계층 전용 신호다</b> — 무적·이동 판정은 <c>CombatantMotor</c> 가 이미 끝냈고
    /// 이 이벤트를 구독해도 전투 결과를 바꿀 수 없다 (S-AUD-6MZMQM).
    /// </summary>
    /// <remarks>
    /// 권위 피어는 대시 명령을 받아들인 순간, 원격 피어는 복제된 대시 상태의 상승 에지에서 한 번씩 발행한다.
    /// 한 피어에서 같은 대시가 두 번 발행되지 않는다.
    /// </remarks>
    public readonly struct CombatantDashStarted : IGameEvent
    {
        public readonly CombatantId Combatant;

        public CombatantDashStarted(CombatantId combatant) => Combatant = combatant;
    }

    /// <summary>
    /// 투사체가 <b>아무도 맞히지 못하고</b> 사라졌다 — 사거리 소진 또는 벽·엄폐물 충돌.
    /// 대상에 명중한 소멸은 <see cref="HitLanded"/> 가 알리므로 여기서 발행하지 않는다.
    /// </summary>
    public readonly struct ProjectileExpired : IGameEvent
    {
        public readonly CombatantId Attacker;
        public readonly WeaponType Weapon;
        public readonly Vector3 Position;

        public ProjectileExpired(CombatantId attacker, WeaponType weapon, Vector3 position)
        {
            Attacker = attacker;
            Weapon = weapon;
            Position = position;
        }
    }

    /// <summary>검증 UI가 로컬 라운드 결과 확정과 다음 라운드 회복을 요청한다(S-QA-8SAYB3).</summary>
    public readonly struct NextCombatRoundRequested : IGameEvent
    {
    }
}

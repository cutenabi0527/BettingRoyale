using BettingRoyale.Core;
using BettingRoyale.Data.Combat;

namespace BettingRoyale.Data.Audio
{
    /// <summary>
    /// 원격 피어가 복제 상태에서 되살린 전투 사실. <b>표현 전용이다</b> (S-AUD-6MZMQM).
    /// </summary>
    public enum ReplicatedCombatCue
    {
        /// <summary>공격이 이 참여자에게 명중했다. 무기별 타격음.</summary>
        HitLanded = 0,

        /// <summary>이 참여자에게 피해가 실제로 들어갔다. 공용 둔탁음.</summary>
        DamageApplied = 1,

        /// <summary>이 참여자가 빈사로 바뀌었다.</summary>
        Downed = 2,

        /// <summary>빈사였던 이 참여자가 회복했다.</summary>
        Recovered = 3,
    }

    /// <summary>
    /// 권위 피어에서 일어난 전투 사실을 원격 피어가 복제 상태로 관찰했다는 신호.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>왜 <c>HitLanded</c>·<c>DamageApplied</c>·<c>CombatantDowned</c>·<c>CombatantRecovered</c> 를
    /// 그대로 다시 발행하지 않는가.</b> 그 넷은 <b>전투 규칙 이벤트</b>다 — 상자 열기 캔슬(S-GD-7DRYPY)·경제·
    /// 라운드 판정이 구독한다. 원격 피어에서 다시 쏘면 표현이 아니라 <b>게임 규칙이 두 번 도는</b> 것이 된다.
    /// 게다가 그 계약들은 <c>Attacker</c> 를 요구하는데, 복제 상태에는 가해자 <see cref="CombatantId"/> 가 없다.
    /// 소리를 내려고 없는 ID 를 지어내지 않는다.
    /// </para>
    /// <para>
    /// 그래서 이 계약은 <b>실재하는 값만</b> 담는다 — 관찰 대상 자신의 <see cref="CombatantId"/> 와,
    /// 명중일 때 복제된 실제 <see cref="WeaponType"/>. 가해자는 담지 않는다.
    /// </para>
    /// <para>
    /// <b>중복이 나지 않는 이유.</b> 권위 피어는 이 이벤트를 발행하지 않고 자기 로컬 전투 이벤트를 그대로 쓴다.
    /// 원격 피어는 <c>NetworkCombatantMotion.Render</c> 에서 복제 시퀀스·빈사 상태의 <b>변화 에지에서 한 번씩만</b>
    /// 발행한다. 한 피어는 둘 중 한 경로만 탄다.
    /// </para>
    /// <para>
    /// <b>이 이벤트를 게임 규칙 신호로 쓰지 않는다.</b> 구독을 전부 지워도 전투 판정·체력·라운드 결과는 같아야 한다.
    /// </para>
    /// </remarks>
    public readonly struct ReplicatedCombatCueObserved : IGameEvent
    {
        /// <summary>사실이 일어난 대상. 관찰한 네트워크 전투원 자신의 실제 ID 다.</summary>
        public readonly CombatantId Combatant;

        public readonly ReplicatedCombatCue Cue;

        /// <summary>
        /// <see cref="ReplicatedCombatCue.HitLanded"/> 일 때 복제된 실제 무기.
        /// 다른 큐에서는 의미가 없다.
        /// </summary>
        public readonly WeaponType Weapon;
        public readonly CombatDamageSource Source;

        public ReplicatedCombatCueObserved(
            CombatantId combatant,
            ReplicatedCombatCue cue,
            WeaponType weapon,
            CombatDamageSource source = CombatDamageSource.Weapon)
        {
            Combatant = combatant;
            Cue = cue;
            Weapon = weapon;
            Source = source;
        }
    }
}

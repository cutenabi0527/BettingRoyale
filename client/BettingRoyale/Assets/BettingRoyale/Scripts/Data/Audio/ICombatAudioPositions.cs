using BettingRoyale.Data.Combat;
using BettingRoyale.Data.Common;

namespace BettingRoyale.Data.Audio
{
    /// <summary>
    /// 전투 사운드가 "어디서 나는 소리인가" 를 묻는 경계 (S-AUD-4AMCAR).
    /// </summary>
    /// <remarks>
    /// <para>
    /// 전투 이벤트(<c>HitLanded</c>·<c>DamageApplied</c> 등)는 <see cref="CombatantId"/> 만 싣고
    /// 좌표를 싣지 않는다. 공간 감쇠를 하려면 그 id 를 좌표로 바꿔야 하는데,
    /// <b>레이어 규칙상 <c>Managers</c> 는 <c>Gameplay</c> 를 참조할 수 없다</b>
    /// (<c>UI → Gameplay → Managers → (Network | Data) → Core</c>).
    /// 그래서 이벤트에 좌표 필드를 새로 넣는 대신 이 인터페이스를 <c>Data</c> 에 두고,
    /// 좌표를 아는 위쪽 레이어가 구현해 <c>SoundManager</c> 에 주입한다.
    /// </para>
    /// <para>
    /// ⚠️ <b>구현체가 없거나 조회에 실패하면 그 사운드는 2D 로 재생된다</b> — 소리가 사라지지 않는다.
    /// 공간화는 표현 개선이지 재생 조건이 아니다. 사운드는 전투 판정에 개입하지 않는다(S-AUD-6MZMQM).
    /// </para>
    /// </remarks>
    public interface ICombatAudioPositions
    {
        /// <summary>전투원의 현재 위치. 모르면 <c>false</c> 를 돌려주고 호출자는 2D 로 재생한다.</summary>
        bool TryGetPosition(CombatantId combatant, out WorldPosition position);
    }
}

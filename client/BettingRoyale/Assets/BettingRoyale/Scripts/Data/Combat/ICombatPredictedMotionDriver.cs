using UnityEngine;

namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 권위가 아닌 <b>소유</b> 피어가 자기 캐릭터의 위치만 미리 적분하는 좁은 경계.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ICombatMotionDriver.Tick"/> 과 갈라 두는 이유는 <b>되감기(resim)</b> 다. Fusion 은
    /// 서버 스냅샷이 올 때마다 확정 틱까지 되감고 예측 구간을 <b>다시</b> 실행한다. 그래서 예측 경로에는
    /// 되감아지지 않는 상태를 바꾸거나 한 번만 일어나야 할 일이 있으면 안 된다 —
    /// 대시 충전·축복 타이머·감속 창·일회성 이벤트 발행이 전부 그런 것들이고,
    /// 공용 <c>Tick</c> 은 그것들을 전부 한다.
    /// </para>
    /// <para>
    /// 이 메서드는 <b>평면 이동과 중력 적분만</b> 한다. 대시·빈사·유령처럼 되감을 수 없는 상태가
    /// 관여하는 구간은 예측하지 않고 권위 복제에 맡긴다.
    /// </para>
    /// </remarks>
    public interface ICombatPredictedMotionDriver
    {
        /// <summary>
        /// 이번 예측 틱의 이동을 적분한다. 예측할 수 없는 구간(대시·빈사·유령)이면 아무것도 하지 않고
        /// <c>false</c> 를 돌려준다.
        /// </summary>
        /// <param name="moveSpeedMetersPerSecond">
        /// <b>권위가 확정한 최종 이동 속도</b>(m/s). 소유 피어가 자기 모터에서 계산한 값을 쓰면 안 된다 —
        /// 강화·축복 이동 보너스, 축복 이동 패널티, 소모품 배율, 공격 직후 감속은 <b>전부 권위 전용
        /// 경로로만</b> 모터에 들어가므로 클라의 모터는 프리팹 기본값만 들고 있다. 그 값으로 적분하면
        /// 본인 캐릭터가 호스트보다 빠르게 움직인다 (S15P21A606-299 후속 실측).
        /// 유효하지 않은 값(0 이하·비유한)이면 예측하지 않는다 — 지어내지 않는다.
        /// </param>
        bool TickPredictedMotion(float deltaTime, float moveSpeedMetersPerSecond);

        /// <summary>
        /// 유령 위치를 <paramref name="from"/> 에서 한 걸음 전진시킨 값을 돌려준다. 경기장 경계로 자른다.
        /// </summary>
        /// <remarks>
        /// <para>
        /// ⚠️ <b>유령은 몸통과 같은 방식으로 예측할 수 없다.</b> 유령 이동은
        /// <c>CharacterController</c> 가 아니라 평범한 <c>Vector3</c> 필드를 옮기는데, 그 필드는 클라에서
        /// <c>Render</c> 의 복제 적용이 <b>매 프레임 덮어쓴다</b>. 틱에서 예측해 봐야 그 프레임에 지워진다.
        /// 그래서 유령은 <b>표현 경로</b>에서 전진시킨다.
        /// </para>
        /// <para>
        /// 이 메서드는 <b>아무 상태도 바꾸지 않는다</b> — 값을 받아 값을 돌려줄 뿐이다. 호출자가 새 권위
        /// 위치가 올 때마다 거기서 다시 시작하므로, 전진 속도가 권위와 같으면 보정량은 0 에 수렴한다.
        /// <see cref="EventBus"/> 발행도 하지 않는다 — 그건 권위 경로의 몫이다.
        /// </para>
        /// </remarks>
        Vector3 AdvancePredictedGhost(
            Vector3 from, Vector2 moveDirection, float moveSpeedMetersPerSecond, float deltaTime);
    }
}

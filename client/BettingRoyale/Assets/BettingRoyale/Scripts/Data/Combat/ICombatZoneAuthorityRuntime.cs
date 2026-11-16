using System;

namespace BettingRoyale.Data.Combat
{
    /// <summary>
    /// 자기장을 세울 <b>전장 경계</b>. 순수 스칼라만 담는다 — 씬·컴포넌트·UnityEngine 타입에
    /// 기대지 않으므로 Data 계층 정본으로 둘 수 있다 (S-CLI-RQB4QD · D-CLI-HPB8K7).
    ///
    /// <para>
    /// XZ 평면 기준이다. <see cref="CenterZ"/> 는 월드 z, 높이(y)는 자기장 계약에 없다.
    /// 유효하지 않은 경계(기본값·NaN·무한대·0 이하·뒤집힘)는 <see cref="IsValid"/> 가 false 이며
    /// 자기장을 <b>세우지 않는</b> 근거가 된다. 대체 기하를 지어내지 않는다.
    /// </para>
    /// </summary>
    public readonly struct CombatZoneBounds : IEquatable<CombatZoneBounds>
    {
        /// <param name="centerX">전장 중심의 월드 x.</param>
        /// <param name="centerZ">전장 중심의 월드 z.</param>
        /// <param name="width">x 축 전체 폭. 0 이하는 유효하지 않다.</param>
        /// <param name="depth">z 축 전체 깊이. 0 이하는 유효하지 않다.</param>
        public CombatZoneBounds(float centerX, float centerZ, float width, float depth)
        {
            CenterX = centerX;
            CenterZ = centerZ;
            Width = width;
            Depth = depth;
        }

        public float CenterX { get; }

        public float CenterZ { get; }

        public float Width { get; }

        public float Depth { get; }

        public float MinX => CenterX - (Width * 0.5f);

        public float MaxX => CenterX + (Width * 0.5f);

        public float MinZ => CenterZ - (Depth * 0.5f);

        public float MaxZ => CenterZ + (Depth * 0.5f);

        /// <summary>
        /// 자기장을 세워도 되는 경계인가. 기본값(<c>default</c>)·NaN·무한대·0 이하 치수는 모두 false 다.
        /// 뒤집힌 모서리로 만든 경계는 폭/깊이가 음수가 되어 여기서 걸린다.
        ///
        /// <para>
        /// 중심과 치수가 각각 유한해도 <b>모서리가 float 범위를 넘칠 수 있다</b>(예: 중심 3e38 · 폭 3e38).
        /// 그런 경계로 세운 자기장은 반경·면적 환산이 무한대가 되므로 모서리까지 함께 본다.
        /// </para>
        /// </summary>
        public bool IsValid =>
            IsFinite(CenterX) && IsFinite(CenterZ) &&
            IsFinite(Width) && IsFinite(Depth) &&
            Width > 0f && Depth > 0f &&
            IsFinite(MinX) && IsFinite(MaxX) &&
            IsFinite(MinZ) && IsFinite(MaxZ);

        /// <summary>
        /// 두 모서리로 경계를 만든다. <paramref name="maxX"/>/<paramref name="maxZ"/> 가 더 작으면
        /// 음수 치수가 되어 <see cref="IsValid"/> 가 false 다 — 조용히 뒤집어 고치지 않는다.
        /// </summary>
        public static CombatZoneBounds FromCorners(float minX, float minZ, float maxX, float maxZ) =>
            new CombatZoneBounds(
                (minX + maxX) * 0.5f,
                (minZ + maxZ) * 0.5f,
                maxX - minX,
                maxZ - minZ);

        public bool Equals(CombatZoneBounds other) =>
            CenterX.Equals(other.CenterX) &&
            CenterZ.Equals(other.CenterZ) &&
            Width.Equals(other.Width) &&
            Depth.Equals(other.Depth);

        public override bool Equals(object obj) => obj is CombatZoneBounds other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = CenterX.GetHashCode();
                hash = (hash * 397) ^ CenterZ.GetHashCode();
                hash = (hash * 397) ^ Width.GetHashCode();
                hash = (hash * 397) ^ Depth.GetHashCode();
                return hash;
            }
        }

        public override string ToString() =>
            $"center=({CenterX}, {CenterZ}) size=({Width} x {Depth})";

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// 제품 전투의 자기장 경계. Host 는 <b>권위 틱마다 직접 진행</b>시키고 원격 client 는
    /// 복제된 진행 시각만 반영한다 (S-GD-657ZN5).
    ///
    /// <para>
    /// Network 는 이 계약만 본다 — 자기장 구현(<c>Gameplay/Combat/CombatZoneRuntime</c>)과
    /// 그 MonoBehaviour 조립은 Gameplay 소유다 (D-009 · S-CLI-RQB4QD).
    /// </para>
    /// </summary>
    public interface ICombatZoneAuthorityRuntime
    {
        bool IsRunning { get; }

        /// <summary>전투 시작 카운트다운 종료 기준 경과 시간.</summary>
        float Elapsed { get; }

        /// <summary>
        /// 자기장이 전역을 다 덮기까지 남은 시간. 전역화가 끝났으면 0 이다.
        /// <b>이 값이 0 이 되어도 전투는 끝나지 않는다</b> — 시간 만료 개념이 없다.
        /// </summary>
        float SecondsUntilGlobal { get; }

        /// <summary>안전지대가 사라졌는가.</summary>
        bool IsGlobal { get; }

        /// <summary>
        /// <b>더 이상 자기장을 세우지 않는다.</b> 명시적 전장 경계 없이는 맵을 뒤져 추측하지 않기
        /// 때문이다. 호출자 이관 전까지 남겨둔 서명이며, 불리면 실패를 남기고 이전 라운드까지
        /// 확실히 멈춘다. 새 호출자는 <see cref="TryBeginRound"/> 를 쓴다.
        /// </summary>
        /// <param name="isParticipant">
        /// 이 전투원이 이번 라운드 명부인가. 다른 씬의 더미·허수아비를 거르는 유일한 기준이다.
        /// </param>
        void BeginRound(Func<CombatantId, bool> isParticipant);

        /// <summary>
        /// 명시된 전장 경계로 새 라운드의 자기장을 세운다. 명부에 든 전투원만 피해 대상이 된다.
        /// </summary>
        /// <param name="isParticipant">이번 라운드 명부 판정. null 이면 세우지 않는다.</param>
        /// <param name="bounds">
        /// 월드가 조립된 뒤의 <b>실제</b> 전장 경계. <see cref="CombatZoneBounds.IsValid"/> 가 아니면
        /// 자기장을 세우지 않고 false 를 돌려준다 — 기본 크기를 지어내지 않는다.
        /// </param>
        /// <returns>자기장을 세우고 전투를 시작했으면 true.</returns>
        bool TryBeginRound(Func<CombatantId, bool> isParticipant, CombatZoneBounds bounds);

        /// <summary>
        /// Host 권위 틱 안에서 자기장을 <paramref name="deltaSeconds"/> 만큼 진행한다.
        /// <b>같은 틱의 모든 전투원 시뮬레이션이 끝난 뒤</b> 부른다.
        /// </summary>
        void AdvanceAuthority(float deltaSeconds);

        /// <summary>
        /// 원격 client 표시용. 피해를 만들지 않는다.
        /// <b>같은 명시적 경계로 라운드를 세운 뒤에만</b> 반영된다 — 표시용이라고 경계를 추측해
        /// 숙주 자기장과 다른 기하를 그리지 않는다.
        /// </summary>
        void ApplyReplicatedElapsed(float elapsed);

        /// <summary>라운드를 멈춘다. 멈춘 뒤에는 밀린 틱을 따라잡지 않는다.</summary>
        void StopRound();

        /// <summary>런타임과 그 표현물을 정리한다.</summary>
        void Dispose();
    }

    /// <summary>
    /// 자기장 구현을 씬 없이 얻는 자리. Gameplay 가 부팅에서 등록하고 Network 가 받아 쓴다.
    /// </summary>
    public static class CombatZoneAuthorities
    {
        private static Func<bool, ICombatZoneAuthorityRuntime> factory;

        public static bool HasFactory => factory != null;

        /// <param name="value">인자는 <c>presentationOnly</c> — true 면 피해를 만들지 않는다.</param>
        public static void Register(Func<bool, ICombatZoneAuthorityRuntime> value) =>
            factory = value ?? throw new ArgumentNullException(nameof(value));

        public static void Clear() => factory = null;

        public static bool TryCreate(bool presentationOnly, out ICombatZoneAuthorityRuntime runtime)
        {
            if (factory == null)
            {
                runtime = null;
                return false;
            }

            runtime = factory(presentationOnly);
            return runtime != null;
        }
    }
}

using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Online
{
    /// <summary>준비 → 전투 전환 단계(S-GD-4JDCGB).</summary>
    public enum CombatEntryStage : byte
    {
        None = 0,

        /// <summary>준비 단계 끝 — 화면이 검게 어두워진다. 조작은 막힌다.</summary>
        FadeOut = 1,

        /// <summary>검은 화면 — 전원의 전투 맵 준비를 기다린다. 최소 시간을 채워야 넘어간다.</summary>
        Loading = 2,

        /// <summary>검은 화면이 걷히며 전투 맵이 드러난다.</summary>
        FadeIn = 3,

        /// <summary>3·2·1 카운트다운.</summary>
        Countdown = 4,
    }

    /// <summary>
    /// 전투 진입 현황 — Host 가 정해 스냅샷에 싣는다. 받는 쪽은 그대로 그리고 스스로 세지 않는다.
    /// </summary>
    public readonly struct CombatEntryStatus : IEquatable<CombatEntryStatus>
    {
        public CombatEntryStatus(CombatEntryStage stage, float stageRemainingSeconds, int readyCount, int totalCount)
        {
            Stage = Enum.IsDefined(typeof(CombatEntryStage), stage) ? stage : CombatEntryStage.None;
            StageRemainingSeconds = Stage == CombatEntryStage.None ? 0f : Math.Max(0f, stageRemainingSeconds);
            ReadyCount = Math.Max(0, readyCount);
            TotalCount = Math.Max(0, totalCount);
        }

        public CombatEntryStage Stage { get; }

        /// <summary>지금 단계의 남은 시간(초). 검은 화면 대기는 끝이 정해지지 않아 최소 시간의 남은 몫이다.</summary>
        public float StageRemainingSeconds { get; }

        public int ReadyCount { get; }
        public int TotalCount { get; }

        /// <summary>전원의 전투 맵 준비를 기다리는 검은 화면인가.</summary>
        public bool IsWaiting => Stage == CombatEntryStage.Loading;

        /// <summary>카운트다운 남은 시간(초). 카운트다운이 아니면 0.</summary>
        public float CountdownSeconds => Stage == CombatEntryStage.Countdown ? StageRemainingSeconds : 0f;

        /// <summary>전환 중인가. 이 동안 이동·공격·피해·자기장이 멈춘다.</summary>
        public bool IsActive => Stage != CombatEntryStage.None;

        public bool Equals(CombatEntryStatus other) =>
            Stage == other.Stage && StageRemainingSeconds.Equals(other.StageRemainingSeconds)
            && ReadyCount == other.ReadyCount && TotalCount == other.TotalCount;

        public override bool Equals(object obj) => obj is CombatEntryStatus other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Stage, StageRemainingSeconds, ReadyCount, TotalCount);
        public static bool operator ==(CombatEntryStatus left, CombatEntryStatus right) => left.Equals(right);
        public static bool operator !=(CombatEntryStatus left, CombatEntryStatus right) => !left.Equals(right);
    }

    /// <summary>
    /// 준비 → 전투 전환(S-GD-4JDCGB). 1초 페이드 아웃 → 검은 화면(전원 준비 + 최소 1초) → 1초 페이드 인 → 3초 카운트다운.
    /// 카운트다운이 끝난 뒤에만 전투와 조작을 연다. Host 만 쓴다.
    /// </summary>
    /// <remarks>
    /// 예전 온라인 경로에는 이 절차가 없어 준비가 끝나는 순간 곧바로 전투가 열렸다(2026-09-25 Preview, S15P21A606-326).
    /// </remarks>
    public sealed class CombatEntryGate
    {
        /// <summary>페이드 아웃·인 시간(초). 확정값이다(S-GD-4JDCGB).</summary>
        public const float FadeOutSeconds = 1f, FadeInSeconds = 1f;

        /// <summary>검은 화면 최소 유지 시간(초). 전원이 준비돼도 이만큼은 유지한다(S-GD-4JDCGB).</summary>
        public const float MinBlackSeconds = 1f;

        /// <summary>카운트다운 길이(초). 확정값이다(S-GD-7RCHF8).</summary>
        public const float CountdownSeconds = 3f;

        /// <summary>
        /// 준비 보고를 기다리는 최대 시간(초). 한 사람의 보고가 끝내 오지 않아도 방 전체가 멈추지 않게 한다(S-GD-C66RN8, 에이전트 임의).
        /// </summary>
        public const float ReadyTimeoutSeconds = 10f;

        /// <summary>
        /// Host 가 페이드 아웃을 화면보다 조금 더 길게 잡는 여유(초). 단계 소식이 늦게 도착한 피어의 화면이 다 검어지기 전에
        /// 순간이동이 보이지 않게 한다. 에이전트 임의(S-GD-4JDCGB).
        /// </summary>
        public const float FadeOutNetworkMarginSeconds = 0.2f;

        private readonly HashSet<int> ready = new();
        private float remaining;
        private float blackElapsed;

        public CombatEntryStage Stage { get; private set; }
        public bool IsActive => Stage != CombatEntryStage.None;
        public bool IsWaiting => Stage == CombatEntryStage.Loading;

        /// <summary>페이드 아웃이 끝나 전투 단계로 넘어갈 차례인가.</summary>
        public bool IsFadeOutComplete => Stage == CombatEntryStage.FadeOut && remaining <= 0f;

        public int Round { get; private set; }

        /// <summary>준비 단계 끝 — 페이드 아웃을 시작한다.</summary>
        public void BeginFadeOut(int round)
        {
            ready.Clear();
            Round = round;
            Stage = CombatEntryStage.FadeOut;
            remaining = FadeOutSeconds + FadeOutNetworkMarginSeconds;
            blackElapsed = 0f;
        }

        /// <summary>전투 단계로 넘어갔다 — 검은 화면에서 준비를 기다린다. 페이드 아웃 없이 불려도(복구 경로) 같은 절차를 탄다.</summary>
        public void BeginLoading(int round)
        {
            if (Round != round) ready.Clear();
            Round = round;
            Stage = CombatEntryStage.Loading;
            remaining = MinBlackSeconds;
            blackElapsed = 0f;
        }

        public void Reset()
        {
            ready.Clear();
            remaining = 0f;
            blackElapsed = 0f;
            Round = 0;
            Stage = CombatEntryStage.None;
        }

        /// <summary>이 참가자가 이번 라운드 전투 맵에 섰다고 보고했다. 다른 라운드의 늦은 보고는 버린다.</summary>
        public bool MarkReady(int playerId, int round)
        {
            if (!IsActive || round != Round) return false;
            return ready.Add(playerId);
        }

        /// <summary>
        /// 시간을 흘린다. <paramref name="humans"/> 는 지금 방에 있는 사람(봇 제외)이다 — 나간 사람을 기다리지 않는다.
        /// 단계가 바뀌거나 카운트다운 초가 바뀌면 true 다(알릴 일).
        /// </summary>
        public bool Advance(float deltaSeconds, IReadOnlyCollection<int> humans)
        {
            if (!IsActive || float.IsNaN(deltaSeconds) || deltaSeconds < 0f) return false;
            switch (Stage)
            {
                case CombatEntryStage.FadeOut:
                    if (remaining <= 0f) return false;
                    remaining -= deltaSeconds;
                    return remaining <= 0f;

                case CombatEntryStage.Loading:
                    blackElapsed += deltaSeconds;
                    remaining = Math.Max(0f, MinBlackSeconds - blackElapsed);
                    if (blackElapsed < MinBlackSeconds) return false;
                    if (!AllReady(humans) && blackElapsed < ReadyTimeoutSeconds) return false;
                    Stage = CombatEntryStage.FadeIn;
                    remaining = FadeInSeconds;
                    return true;

                case CombatEntryStage.FadeIn:
                    remaining -= deltaSeconds;
                    if (remaining > 0f) return false;
                    Stage = CombatEntryStage.Countdown;
                    remaining = CountdownSeconds;
                    return true;

                case CombatEntryStage.Countdown:
                    int before = (int)Math.Ceiling(remaining);
                    remaining -= deltaSeconds;
                    if (remaining <= 0f)
                    {
                        Stage = CombatEntryStage.None;
                        remaining = 0f;
                        return true;
                    }

                    return (int)Math.Ceiling(remaining) != before;

                default:
                    return false;
            }
        }

        public CombatEntryStatus Status(IReadOnlyCollection<int> humans)
        {
            if (!IsActive) return default;
            return new CombatEntryStatus(Stage, remaining, CountReady(humans), humans?.Count ?? 0);
        }

        private bool AllReady(IReadOnlyCollection<int> humans)
        {
            if (humans == null) return true;
            foreach (int id in humans)
                if (!ready.Contains(id)) return false;
            return true;
        }

        private int CountReady(IReadOnlyCollection<int> humans)
        {
            if (humans == null) return 0;
            var count = 0;
            foreach (int id in humans)
                if (ready.Contains(id)) count++;
            return count;
        }
    }
}

using System;
using System.Collections.Generic;

namespace BettingRoyale.Data.Blessing
{
    /// <summary>
    /// 효과의 Apply/Refresh/StackChanged/Remove 수명과 HUD FIFO 읽기 모델을 관리한다.
    /// 스탯·피해·라운드 정책은 호출자와 게임플레이 상태의 책임이며 HUD는 이 목록을 제한하지 않는다.
    /// </summary>
    public sealed class BlessingEffectTracker
    {
        private readonly List<Entry> entries = new List<Entry>(8);
        private readonly Dictionary<BlessingEffectKey, Entry> byKey = new Dictionary<BlessingEffectKey, Entry>();
        private readonly List<BlessingEffectInstance> snapshot = new List<BlessingEffectInstance>(8);
        private bool snapshotStale = true;
        private float time;

        public float Time => time;
        public int Count => entries.Count;

        /// <summary>최초 활성화 순서의 읽기 모델. 갱신은 위치를 바꾸지 않는다.</summary>
        public IReadOnlyList<BlessingEffectInstance> Snapshot
        {
            get
            {
                RebuildSnapshot();
                return snapshot;
            }
        }

        public BlessingEffectInstance this[int index]
        {
            get
            {
                if (index < 0 || index >= entries.Count) throw new ArgumentOutOfRangeException(nameof(index));
                return entries[index].ToInstance(time);
            }
        }

        public BlessingEffectChange Apply(float now, int sourceId, string effectId, BlessingEffectKind kind,
            BlessingEffectDuration duration, int stacks = 1, string iconId = null)
        {
            var key = new BlessingEffectKey(sourceId, effectId);
            AdvanceAndExpire(now);

            if (!kind.CreatesInstance()) return BlessingEffectChange.None;
            if (duration.IsExpired)
            {
                return Remove(key) ? BlessingEffectChange.Removed : BlessingEffectChange.None;
            }

            stacks = stacks < 1 ? 1 : stacks;
            if (byKey.TryGetValue(key, out var entry))
            {
                bool stackChanged = entry.Stacks != stacks;
                entry.Stacks = stacks;
                entry.Kind = kind;
                if (!string.IsNullOrEmpty(iconId)) entry.IconId = iconId;
                entry.Restart(time, duration);
                snapshotStale = true;
                return stackChanged ? BlessingEffectChange.StackChanged : BlessingEffectChange.Refreshed;
            }

            var added = new Entry(key, string.IsNullOrEmpty(iconId) ? key.EffectId : iconId, kind, stacks);
            added.Restart(time, duration);
            entries.Add(added);
            byKey.Add(key, added);
            snapshotStale = true;
            return BlessingEffectChange.Added;
        }

        public BlessingEffectChange Refresh(float now, int sourceId, string effectId, BlessingEffectDuration duration)
        {
            var key = new BlessingEffectKey(sourceId, effectId);
            AdvanceAndExpire(now);
            if (!byKey.TryGetValue(key, out var entry)) return BlessingEffectChange.None;
            if (duration.IsExpired)
            {
                Remove(key);
                return BlessingEffectChange.Removed;
            }

            entry.Restart(time, duration);
            snapshotStale = true;
            return BlessingEffectChange.Refreshed;
        }

        public BlessingEffectChange Refresh(float now, int sourceId, string effectId)
        {
            var key = new BlessingEffectKey(sourceId, effectId);
            AdvanceAndExpire(now);
            if (!byKey.TryGetValue(key, out var entry)) return BlessingEffectChange.None;
            entry.Restart(time, entry.CurrentDuration());
            snapshotStale = true;
            return BlessingEffectChange.Refreshed;
        }

        public BlessingEffectChange SetStacks(float now, int sourceId, string effectId, int stacks) =>
            SetStacks(now, sourceId, effectId, stacks, false, default(BlessingEffectDuration));

        public BlessingEffectChange SetStacks(float now, int sourceId, string effectId, int stacks,
            BlessingEffectDuration duration) => SetStacks(now, sourceId, effectId, stacks, true, duration);

        public BlessingEffectChange AddStacks(float now, int sourceId, string effectId, int delta)
        {
            var key = new BlessingEffectKey(sourceId, effectId);
            AdvanceAndExpire(now);
            if (!byKey.TryGetValue(key, out var entry)) return BlessingEffectChange.None;
            return SetStacks(now, sourceId, effectId, entry.Stacks + delta);
        }

        public bool Remove(int sourceId, string effectId) => Remove(new BlessingEffectKey(sourceId, effectId));

        public int RemoveSource(int sourceId)
        {
            int removed = 0;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].Key.SourceId != sourceId) continue;
                byKey.Remove(entries[i].Key);
                entries.RemoveAt(i);
                removed++;
            }
            if (removed > 0) snapshotStale = true;
            return removed;
        }

        public int Tick(float now)
        {
            Advance(now);
            return ExpireDue();
        }

        public bool Contains(int sourceId, string effectId) =>
            byKey.ContainsKey(new BlessingEffectKey(sourceId, effectId));

        public bool TryGet(int sourceId, string effectId, out BlessingEffectInstance instance)
        {
            if (byKey.TryGetValue(new BlessingEffectKey(sourceId, effectId), out var entry))
            {
                instance = entry.ToInstance(time);
                return true;
            }

            instance = default(BlessingEffectInstance);
            return false;
        }

        public void CopySnapshotTo(List<BlessingEffectInstance> buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            buffer.Clear();
            for (int i = 0; i < entries.Count; i++) buffer.Add(entries[i].ToInstance(time));
        }

        /// <summary>라운드 경계에서 비울지는 호출자가 결정한다.</summary>
        public void Clear()
        {
            entries.Clear();
            byKey.Clear();
            snapshotStale = true;
        }

        private BlessingEffectChange SetStacks(float now, int sourceId, string effectId, int stacks,
            bool useGivenDuration, BlessingEffectDuration duration)
        {
            var key = new BlessingEffectKey(sourceId, effectId);
            AdvanceAndExpire(now);
            if (!byKey.TryGetValue(key, out var entry)) return BlessingEffectChange.None;
            if (stacks < 1)
            {
                Remove(key);
                return BlessingEffectChange.Removed;
            }

            BlessingEffectDuration next = useGivenDuration ? duration : entry.CurrentDuration();
            if (next.IsExpired)
            {
                Remove(key);
                return BlessingEffectChange.Removed;
            }

            bool changed = entry.Stacks != stacks;
            entry.Stacks = stacks;
            entry.Restart(time, next);
            snapshotStale = true;
            return changed ? BlessingEffectChange.StackChanged : BlessingEffectChange.Refreshed;
        }

        private bool Remove(BlessingEffectKey key)
        {
            if (!byKey.TryGetValue(key, out var entry)) return false;
            byKey.Remove(key);
            entries.Remove(entry);
            snapshotStale = true;
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private void Advance(float now)
        {
            if (!IsFinite(now) || now <= time) return;
            time = now;
            snapshotStale = true;
        }

        private void AdvanceAndExpire(float now)
        {
            Advance(now);
            ExpireDue();
        }

        private int ExpireDue()
        {
            int removed = 0;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (entry.IsUnlimited || time < entry.ExpiresAt) continue;
                byKey.Remove(entry.Key);
                entries.RemoveAt(i);
                removed++;
            }
            if (removed > 0) snapshotStale = true;
            return removed;
        }

        private void RebuildSnapshot()
        {
            if (!snapshotStale) return;
            snapshot.Clear();
            for (int i = 0; i < entries.Count; i++) snapshot.Add(entries[i].ToInstance(time));
            snapshotStale = false;
        }

        private sealed class Entry
        {
            public Entry(BlessingEffectKey key, string iconId, BlessingEffectKind kind, int stacks)
            {
                Key = key;
                IconId = iconId;
                Kind = kind;
                Stacks = stacks;
            }

            public readonly BlessingEffectKey Key;
            public string IconId;
            public BlessingEffectKind Kind;
            public int Stacks;
            public float StartTime;
            public float TotalSeconds;
            public float ExpiresAt;
            public bool IsUnlimited;

            public void Restart(float now, BlessingEffectDuration duration)
            {
                StartTime = now;
                IsUnlimited = duration.IsUnlimited;
                TotalSeconds = duration.IsUnlimited ? 0f : duration.Seconds;
                ExpiresAt = duration.IsUnlimited ? now : now + duration.Seconds;
            }

            public BlessingEffectDuration CurrentDuration() =>
                IsUnlimited ? BlessingEffectDuration.Unlimited : BlessingEffectDuration.Of(TotalSeconds);

            public BlessingEffectInstance ToInstance(float now) => new BlessingEffectInstance(
                Key.SourceId, Key.EffectId, IconId, Kind, StartTime, TotalSeconds,
                IsUnlimited ? 0f : ExpiresAt - now, IsUnlimited, Stacks);
        }
    }

    public enum BlessingEffectChange
    {
        None = 0,
        Added = 1,
        Refreshed = 2,
        StackChanged = 3,
        Removed = 4,
    }
}

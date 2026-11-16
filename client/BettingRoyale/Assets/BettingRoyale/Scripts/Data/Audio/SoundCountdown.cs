namespace BettingRoyale.Data.Audio
{
    /// <summary>Repeated snapshots do not restart cues; skipped seconds are not replayed.</summary>
    public sealed class SoundCountdown
    {
        private int previous;
        public bool Observe(float seconds)
        {
            int next = float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f
                ? 0 : (int)System.Math.Ceiling(seconds);
            bool play = next > 0 && next <= 10 && next != previous;
            previous = next;
            return play;
        }
        public void Reset() => previous = 0;
    }
}

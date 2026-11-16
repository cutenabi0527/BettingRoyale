using System;

namespace BettingRoyale.Data.Throwing
{
    /// <summary>정상 투척의 거리별 비행 시간. 착지 후 경고 시간은 변경하지 않는다.</summary>
    public static class ThrowFlightTiming
    {
        public static double Resolve(double maximumSeconds, double distanceRatio)
        {
            if (double.IsNaN(maximumSeconds) || double.IsInfinity(maximumSeconds) || maximumSeconds <= 0
                || double.IsNaN(distanceRatio) || double.IsInfinity(distanceRatio)) return 0;
            double minimum = Math.Min(0.2, maximumSeconds);
            return minimum + (maximumSeconds - minimum) * Math.Max(0, Math.Min(1, distanceRatio));
        }
    }
}

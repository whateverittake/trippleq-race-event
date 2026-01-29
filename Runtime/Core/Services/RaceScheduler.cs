using System;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    /// <summary>
    /// RaceScheduler: gom toàn bộ logic "thời gian" cho RaceEvent.
    /// - Reset boundary theo resetHourLocal (vd 4:00).
    /// - WindowId theo reset boundary (để once-per-window).
    /// - Cooldown tham gia dựa trên "time thực" (DateTime local), tránh lệch timezone/unix.
    /// </summary>
    public sealed class RaceScheduler
    {
        public readonly struct RoundTimeSnapshot
        {
            public readonly DateTime NextResetLocal;
            public readonly DateTime RoundEndLocal;
            public readonly DateTime NextAllowedStartLocal;
            public readonly bool IsOverflow;

            public RoundTimeSnapshot(DateTime nextResetLocal, DateTime roundEndLocal, DateTime nextAllowedStartLocal)
            {
                NextResetLocal = nextResetLocal;
                RoundEndLocal = roundEndLocal;
                NextAllowedStartLocal = nextAllowedStartLocal;
                IsOverflow = nextAllowedStartLocal >= nextResetLocal;
            }
        }

        /// <summary>
        /// Kết quả snapshot thời gian dùng cho Service/HUD/Eligibility.
        /// </summary>
        public readonly struct TimeSnapshot
        {
            public readonly int WindowId;
            public readonly DateTime NextResetLocal;

            public readonly bool IsInCooldown;
            public readonly long CooldownRemainingSeconds;

            public readonly bool HasShownEntryThisWindow;

            public TimeSnapshot(
                int windowId,
                DateTime nextResetLocal,
                bool isInCooldown,
                long cooldownRemainingSeconds,
                bool hasShownEntryThisWindow)
            {
                WindowId = windowId;
                NextResetLocal = nextResetLocal;

                IsInCooldown = isInCooldown;
                CooldownRemainingSeconds = cooldownRemainingSeconds;

                HasShownEntryThisWindow = hasShownEntryThisWindow;
            }
        }

        /// <summary>
        /// EvaluateGapFromBaseUtc:
        /// Tính NextAllowedStart dựa trên 1 mốc baseUtcSeconds (policy-dependent).
        ///
        /// - Policy cũ: base = roundEndUtcSeconds (gap tính từ End).
        /// - Policy vNext (strict): base = claimedUtcSeconds (gap tính từ Claim).
        /// </summary>
        public RoundTimeSnapshot EvaluateGapFromBaseUtc(
        DateTime localNow,
        int resetHourLocal,
        long baseUtcSeconds,
        int gapMinutes)
        {
            var nextResetLocal = ComputeNextResetLocal(localNow, resetHourLocal);

            // Convert baseUtc -> local DateTime (device local)
            var baseLocal = DateTimeOffset.FromUnixTimeSeconds(baseUtcSeconds).LocalDateTime;

            var nextAllowedStartLocal = baseLocal.AddMinutes(Math.Max(0, gapMinutes));

            return new RoundTimeSnapshot(nextResetLocal, baseLocal, nextAllowedStartLocal);
        }

        // Backward-compatible wrapper (gap from End).
        public RoundTimeSnapshot EvaluateRoundTime(
            DateTime localNow,
            int resetHourLocal,
            long roundEndUtcSeconds,
            int gapMinutes)
            => EvaluateGapFromBaseUtc(localNow, resetHourLocal, roundEndUtcSeconds, gapMinutes);

        // Tính mốc reset kế tiếp theo giờ local (ví dụ reset 4:00).
        // Nếu đã qua 4:00 hôm nay → reset kế tiếp là 4:00 ngày mai.
        // Nếu chưa tới 4:00 → reset kế tiếp là 4:00 hôm nay.
        public DateTime ComputeNextResetLocal(DateTime localNow, int resetHourLocal)
        {
            // reset hour like 4:00
            var resetToday = new DateTime(localNow.Year, localNow.Month, localNow.Day, resetHourLocal, 0, 0);

            // if we're already past today's reset => next reset is tomorrow
            if (localNow >= resetToday)
                return resetToday.AddDays(1);

            // else next reset is today at reset hour
            return resetToday;
        }

        // Tạo “mã cửa sổ ngày” theo ranh giới reset.
        // Ví dụ reset 4:00 → thời điểm 02:00 sẽ thuộc window của ngày hôm trước.
        // Dùng để đảm bảo “mỗi window chỉ show entry 1 lần”, “mỗi window advance cursor 1 lần”, v.v.
        public int ComputeWindowId(DateTime localNow, int resetHourLocal)
        {
            // Shift time by reset boundary to make "day window"
            // If resetHourLocal = 4, then 02:00 belongs to previous day window.
            var shifted = localNow.AddHours(-resetHourLocal);
            // int like 20251226
            return shifted.Year * 10000 + shifted.Month * 100 + shifted.Day;
        }

        // Kiểm tra player có đang bị cooldown tham gia race hay không.
        // Dựa trên lastJoinLocalTime (time thực local) và entryCooldownHours.
        // Trả về true nếu chưa đủ số giờ cooldown kể từ lần join gần nhất.
        public bool IsInEntryCooldown(DateTime localNow, DateTime? lastJoinLocalTime, double entryCooldownHours)
        {
            if (entryCooldownHours <= 0) return false;
            if (!lastJoinLocalTime.HasValue) return false;

            var hours = (localNow - lastJoinLocalTime.Value).TotalHours;
            return hours < entryCooldownHours;
        }

        // Kiểm tra đã hiện entry popup trong “window” hiện tại chưa.
        // So sánh lastEntryShownWindowId với ComputeWindowId(localNow, resetHourLocal).
        // Dùng để chặn “spam entry popup” trong cùng một ngày cửa sổ.
        public bool HasShownEntryInWindow(DateTime localNow, int resetHourLocal, int lastEntryShownWindowId)
        {
            var windowId = ComputeWindowId(localNow, resetHourLocal);
            return lastEntryShownWindowId == windowId;
        }

        /// <summary>
        /// EvaluateTimeSnapshot:
        /// Trả về 1 "gói" thông tin thời gian để Service chỉ cần gọi 1 lần.
        ///
        /// - windowId: cửa sổ ngày theo resetHourLocal (00:00-03:59 vẫn thuộc window hôm qua nếu reset=4)
        /// - nextResetLocal: mốc reset kế tiếp (local time)
        /// - cooldown: dựa trên lastJoinLocalTime + entryCooldownHours (time thực local)
        /// - hasShownEntryThisWindow: so sánh lastEntryShownWindowId với windowId hiện tại
        /// </summary>
        public TimeSnapshot EvaluateTimeSnapshot(
            DateTime localNow,
            int resetHourLocal,
            DateTime? lastJoinLocalTime,
            double entryCooldownHours,
            int lastEntryShownWindowId)
        {
            // 1) WindowId + NextResetLocal: luôn dùng 1 nguồn tính để tránh lệch logic
            var windowId = ComputeWindowId(localNow, resetHourLocal);
            var nextResetLocal = ComputeNextResetLocal(localNow, resetHourLocal);

            // 2) Cooldown join (time thực local)
            bool isInCooldown = false;
            long cooldownRemainingSeconds = 0;

            if (entryCooldownHours > 0 && lastJoinLocalTime.HasValue)
            {
                var cooldownEndLocal = lastJoinLocalTime.Value.AddHours(entryCooldownHours);

                if (localNow < cooldownEndLocal)
                {
                    isInCooldown = true;
                    cooldownRemainingSeconds = ClampToNonNegativeSeconds(cooldownEndLocal - localNow);
                }
            }

            // 3) Check đã show entry trong window hiện tại chưa
            bool hasShownEntryThisWindow = (lastEntryShownWindowId == windowId);

            return new TimeSnapshot(
                windowId,
                nextResetLocal,
                isInCooldown,
                cooldownRemainingSeconds,
                hasShownEntryThisWindow
            );
        }

        #region HELPERS

        /// <summary>
        /// Clamp (TimeSpan) -> seconds >= 0.
        /// </summary>
        private static long ClampToNonNegativeSeconds(TimeSpan span)
        {
            var seconds = (long)Math.Ceiling(span.TotalSeconds);
            return seconds < 0 ? 0 : seconds;
        }

        #endregion
    }
}

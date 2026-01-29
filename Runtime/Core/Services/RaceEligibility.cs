using System;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    /// <summary>
    /// RaceEligibility:
    /// Chịu trách nhiệm đánh giá điều kiện tham gia Race (eligible),
    /// KHÔNG xử lý auto-show popup, KHÔNG save, KHÔNG đổi state.
    /// </summary>
    public class RaceEligibility
    {
        /// <summary>
        /// Context dùng cho RaceEligibility.
        /// Chứa toàn bộ dữ liệu cần thiết để đánh giá:
        /// - user có eligible để tham gia race không
        /// - có nên auto-show entry khi vào game không
        ///
        /// Context này KHÔNG có side-effect:
        /// - không save
        /// - không request popup
        /// - không thay đổi state
        /// </summary>
        public readonly struct RaceEligibilityContext
        {
            // -------- Runtime state --------
            public readonly RaceEventState State;
            public readonly int CurrentLevel;

            // -------- Time --------
            public readonly DateTime LocalNow;

            // -------- Save data --------
            public readonly long LastJoinLocalUnixSeconds;
            public readonly int LastEntryShownWindowId;

            // -------- Config --------
            public readonly RaceEventConfig Cfg;

            public RaceEligibilityContext(
            RaceEventState state,
            int currentLevel,
            DateTime localNow,
            long lastJoinLocalUnixSeconds,
            int lastEntryShownWindowId,
            RaceEventConfig config)
            {
                State = state;
                CurrentLevel = currentLevel;
                LocalNow = localNow;
                LastJoinLocalUnixSeconds = lastJoinLocalUnixSeconds;
                LastEntryShownWindowId = lastEntryShownWindowId;
                Cfg = config;
            }

            /// <summary>
            /// Helper: trả về thời điểm join race gần nhất (local),
            /// hoặc null nếu user chưa từng join.
            /// </summary>
            public DateTime? GetLastJoinLocalTime()
            {
                if (LastJoinLocalUnixSeconds <= 0)
                    return null;

                return DateTimeOffset
                    .FromUnixTimeSeconds(LastJoinLocalUnixSeconds)
                    .LocalDateTime;
            }
        }

        private readonly RaceScheduler _raceScheduler;

        public RaceEligibility(RaceScheduler raceScheduler)
        {
            _raceScheduler = raceScheduler;
        }

        /// <summary>
        /// IsEligible:
        /// Trả về TRUE nếu user đủ điều kiện để mở Entry / tham gia Race.
        ///
        /// Lưu ý:
        /// - KHÔNG quản lý "show popup 1 lần/ngày"
        /// - KHÔNG request popup
        /// - KHÔNG save
        /// </summary>
        public bool IsEligible(in RaceEligibilityContext ctx)
        {
            // 1) State gating
            // Busy -> not eligible
            // vNext Eligibility chỉ dùng cho Entry/Join (Idle),
            // không dùng để quyết định StartNextRound khi Ended.
            if (ctx.State == RaceEventState.Searching ||
                ctx.State == RaceEventState.InRace)
                return false;

            var cfg = ctx.Cfg;

            // 2) Feature gating
            if (!cfg.Enabled)
                return false;

            // 4) Min level gating
            if (ctx.CurrentLevel < ctx.Cfg.MinPlayerLevel)
                return false;

            // 5) Cooldown gating (dựa trên time snapshot)
            DateTime? lastJoinLocalTime = ctx.GetLastJoinLocalTime();

            var timeSnapshot = _raceScheduler.EvaluateTimeSnapshot(
                ctx.LocalNow,
                cfg.ResetHourLocal,
                lastJoinLocalTime,
                cfg.EntryCooldownHours,
                ctx.LastEntryShownWindowId
            );

            if (timeSnapshot.IsInCooldown)
                return false;

            return true;
        }
    }
}

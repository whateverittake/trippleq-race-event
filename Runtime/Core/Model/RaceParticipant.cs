#nullable enable
using System;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    [Serializable]
    public sealed class RaceParticipant
    {
        public string Id = "";
        public string DisplayName = "";
        public string AvatarId="";

        // Progress metric (MVP): levels completed during this run
        public int LevelsCompleted;

        // For tie-break & sim
        public long LastUpdateUtcSeconds;

        // Bot only
        public bool IsBot;
        public float AvgSecondsPerLevel=160f; // bot speed (ghost)

        public bool HasFinished;
        public long FinishedUtcSeconds; // thời điểm chạm goal lần đầu

        // ---- Humanization (bot only) ----
        public int TimezoneOffsetMinutes;     // e.g. VN = +420
        public int SleepStartLocalHour = 1;   // start sleeping at 01:00 local
        public int SleepDurationHours = 7;    // sleep 7h (-> 08:00)

        public long StuckUntilUtcSeconds;     // bot “kẹt màn” tới thời điểm này

        // Optional: per-bot personality
        public float JitterPct = 0.15f;       // +-15% speed variation per simulate step
        public float StuckChancePerHour = 0.06f; // 6% per hour active window
        public int StuckMinMinutes = 60;      // 1h
        public int StuckMaxMinutes = 180;     // 3h

        public BotPersonality Personality = BotPersonality.Normal;
    }
}

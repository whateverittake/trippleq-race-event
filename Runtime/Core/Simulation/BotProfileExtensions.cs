#nullable enable
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public static class BotProfileExtensions
    {
        public static RaceParticipant ToRaceParticipant(
            this BotProfile profile,
            long utcNow)
        {
            return new RaceParticipant
            {
                Id = profile.Id,
                DisplayName = profile.DisplayName,
                AvatarId = profile.AvatarId,
                IsBot = true,

                LevelsCompleted = 0,
                LastUpdateUtcSeconds = utcNow,

                AvgSecondsPerLevel = UnityEngine.Random.Range(
                    profile.AvgSecondsPerLevelMin,
                    profile.AvgSecondsPerLevelMax),

                HasFinished = false,
                FinishedUtcSeconds = 0,

                // Humanization
                TimezoneOffsetMinutes = profile.TimezoneOffsetMinutes,
                SleepStartLocalHour = profile.SleepStartLocalHour,
                SleepDurationHours = profile.SleepDurationHours,

                JitterPct = profile.JitterPct,
                StuckChancePerHour = profile.StuckChancePerHour,
                StuckMinMinutes = profile.StuckMinMinutes,
                StuckMaxMinutes = profile.StuckMaxMinutes,

                Personality = profile.Personality
            };
        }
    }
}

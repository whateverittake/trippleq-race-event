#nullable enable

using System;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    /// <summary>
    /// Ghost sim: bots progress deterministically from time.
    /// No per-frame state needed; just "simulate up to utcNow".
    /// </summary>
    public static class GhostBotSimulator
    {
        public static void SimulateBots(RaceRun run, long utcNowSeconds)
        {
            if (run == null) return;
            if (run.Opponents == null) return;

            for (int i = 0; i < run.Opponents.Count; i++)
            {
                var bot = run.Opponents[i];
                if (bot == null || !bot.IsBot) continue;

                SimulateOneBot(bot, run.RunId, run.GoalLevels, utcNowSeconds);
            }
        }

        private static void SimulateOneBot(RaceParticipant bot, string runId, int goalLevels, long utcNow)
        {
            if (bot.HasFinished) return;

            var last = bot.LastUpdateUtcSeconds;
            if (last <= 0)
            {
                bot.LastUpdateUtcSeconds = utcNow; // IMPORTANT
                return; // hoặc last = utcNow; rồi cho chạy tiếp cũng ok
            }

            long delta = utcNow - last;
            if (delta <= 0) return;

            // If bot is sleeping now -> no progress
            if (IsSleepingNow(bot, utcNow))
            {
                bot.LastUpdateUtcSeconds = utcNow;
                return;
            }

            // If bot is stuck -> no progress
            if (bot.StuckUntilUtcSeconds > 0 && utcNow < bot.StuckUntilUtcSeconds)
            {
                bot.LastUpdateUtcSeconds = utcNow;
                return;
            }

            // Deterministic RNG per bot per run
            var rng = CreateDeterministicRng(runId, bot.Id);

            // Chance to enter "stuck" state (based on elapsed hours)
            var deltaHours = delta / 3600.0;

            if (deltaHours > 0.01)
            {
                if (RollStuck(rng, bot.StuckChancePerHour, deltaHours))
                {
                    int stuckMin = Math.Max(1, bot.StuckMinMinutes);
                    int stuckMax = Math.Max(stuckMin, bot.StuckMaxMinutes);
                    int stuckMins = rng.Next(stuckMin, stuckMax + 1);

                    bot.StuckUntilUtcSeconds = utcNow + stuckMins * 60L;
                    bot.LastUpdateUtcSeconds = utcNow;
                    return;
                }
            }

            // Active: compute gained levels
            float jitterPct = Clamp01Abs(bot.JitterPct);
            float jitter = (float)(rng.NextDouble() * 2.0 - 1.0) * jitterPct; // [-pct, +pct]
            float secPerLevel = Math.Max(10f, bot.AvgSecondsPerLevel * (1f + jitter));

            int gained = (int)(delta / secPerLevel);
            if (gained <= 0)
            {
                bot.LastUpdateUtcSeconds = utcNow;
                return;
            }

            bot.LevelsCompleted += gained;
            bot.LastUpdateUtcSeconds = utcNow;

            if (!bot.HasFinished && bot.LevelsCompleted >= goalLevels)
            {
                bot.HasFinished = true;
                bot.FinishedUtcSeconds = utcNow;

                UnityEngine.Debug.Log(
                                        $"[RACE][BOT FINISH] botId={bot.Id} " +
                                        $"levels={bot.LevelsCompleted}/{goalLevels} " +
                                        $"finishUtc={utcNow}"
                                );
            }
        }

        private static bool IsSleepingNow(RaceParticipant bot, long utcNow)
        {
            // Convert utcNow to "bot local time"
            var local = DateTimeOffset.FromUnixTimeSeconds(utcNow)
                                      .ToOffset(TimeSpan.FromMinutes(bot.TimezoneOffsetMinutes))
                                      .DateTime;

            int startHour = ClampHour(bot.SleepStartLocalHour);
            int duration = Math.Max(0, bot.SleepDurationHours);

            // Build sleep windows for today and yesterday (handles crossing midnight)
            var todayStart = new DateTime(local.Year, local.Month, local.Day, startHour, 0, 0);
            var todayEnd = todayStart.AddHours(duration);

            if (local >= todayStart && local < todayEnd) return true;

            var yStart = todayStart.AddDays(-1);
            var yEnd = yStart.AddHours(duration);

            if (local >= yStart && local < yEnd) return true;

            return false;
        }

        private static bool RollStuck(Random rng, float chancePerHour, double deltaHours)
        {
            if (chancePerHour <= 0f) return false;

            // Convert per-hour chance to chance over deltaHours:
            // p = 1 - (1 - c)^(deltaHours)
            double c = Math.Clamp(chancePerHour, 0.0, 0.95);
            double p = 1.0 - Math.Pow(1.0 - c, deltaHours);
            return rng.NextDouble() < p;
        }

        private static Random CreateDeterministicRng(string runId, string botId)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (runId?.GetHashCode() ?? 0);
                h = h * 31 + (botId?.GetHashCode() ?? 0);
                return new Random(h);
            }
        }

        private static int ClampHour(int h) => h < 0 ? 0 : (h > 23 ? 23 : h);
        private static float Clamp01Abs(float v)
        {
            if (v < 0f) v = -v;
            if (v > 0.5f) v = 0.5f; // cap 50%
            return v;
        }

        internal static void SimulateSingleBot(RaceParticipant bot, int goalLevels, long fakeUtc)
        {
            if (bot.HasFinished) return;

            // logic y hệt SimulateBots nhưng áp cho 1 bot
            var elapsed = fakeUtc - bot.LastUpdateUtcSeconds;
            if (elapsed <= 0) return;

            int gainedLevels = (int)(elapsed / bot.AvgSecondsPerLevel);
            if (gainedLevels <= 0) return;

            bot.LevelsCompleted += gainedLevels;
            bot.LastUpdateUtcSeconds += (long)(gainedLevels * bot.AvgSecondsPerLevel);

            if (!bot.HasFinished && bot.LevelsCompleted >= goalLevels)
            {
                bot.HasFinished = true;
                bot.FinishedUtcSeconds = bot.LastUpdateUtcSeconds;
            }
        }
    }
}

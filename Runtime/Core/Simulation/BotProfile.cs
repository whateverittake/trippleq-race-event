using System;
using System.Collections.Generic;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    [Serializable]
    public sealed class BotProfile
    {
        public string Id = "";
        public string DisplayName = "";
        public int AvatarId;
        public BotPersonality Personality;

        public float AvgSecondsPerLevelMin = 450;
        public float AvgSecondsPerLevelMax = 650;

        public int MinPlayerLevel = 1;
        public int MaxPlayerLevel = 999;

        // humanization defaults
        public int TimezoneOffsetMinutes = 420;
        public int SleepStartLocalHour = 0;
        public int SleepDurationHours = 7;
        public float JitterPct = 0.15f;
        public float StuckChancePerHour = 0.05f;
        public int StuckMinMinutes = 60;
        public int StuckMaxMinutes = 180;
    }

    [Serializable]
    public sealed class BotPoolJson
    {
        public List<BotProfile> Bots = new();
    }

    public enum BotPersonality
    {
        Noob,
        Normal,
        Boss
    }
}

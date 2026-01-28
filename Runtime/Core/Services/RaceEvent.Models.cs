
using System.Collections.Generic;
using System;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public readonly struct LeaderboardSnapshot
    {
        public readonly IReadOnlyList<RaceParticipant> Top;
        public readonly int PlayerRank;      // 1-based
        public readonly bool PlayerFinished;

        public LeaderboardSnapshot(IReadOnlyList<RaceParticipant> top, int playerRank, bool playerFinished)
        {
            Top = top;
            PlayerRank = playerRank;
            PlayerFinished = playerFinished;
        }

        public static LeaderboardSnapshot Empty() => new LeaderboardSnapshot(Array.Empty<RaceParticipant>(), 0, false);
    }
    public readonly struct RaceHudStatus
    {
        public readonly bool IsVisible;
        public readonly bool IsSleeping;     // icon xám + zzz
        public readonly bool HasClaim;       // show "!" rung / claim now
        public readonly TimeSpan Remaining;  // countdown text
        public readonly string Label;        // optional: "NEXT RACE"
        public readonly bool ShowTextCountdown;

        public readonly bool IsLocked;
        public readonly int UnlockAtLevel;

        public RaceHudStatus(bool isVisible, bool isSleeping, bool hasClaim, TimeSpan remaining, string label, bool showTextCountdown)
         : this(isVisible, isSleeping, hasClaim, remaining, label, showTextCountdown, isLocked: false, unlockAtLevel: 0) { }

        public RaceHudStatus(bool isVisible, bool isSleeping, bool hasClaim, TimeSpan remaining, string label, bool showTextCountdown,
                        bool isLocked, int unlockAtLevel)
        {
            IsVisible = isVisible;
            IsSleeping = isSleeping;
            HasClaim = hasClaim;
            Remaining = remaining;
            Label = label;
            ShowTextCountdown = showTextCountdown;

            IsLocked = isLocked;
            UnlockAtLevel = unlockAtLevel;
        }
    }
    public enum RaceHudClickAction
    {
        None,
        OpenEntry,
        OpenInRace,
        OpenEnded
    }
    public enum HudMode
    {
        Hidden,
        Locked,
        Claim,       // ended & can claim OR extend offer
        InRace,
        Entry,
        Sleeping     // next in / nothing to do
    }
}

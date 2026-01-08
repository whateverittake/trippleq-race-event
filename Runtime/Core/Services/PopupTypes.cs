#nullable enable

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class PopupTypes
    {
        public enum PopupType
        {
            Entry,
            Searching,
            Main,
            Ended,
            Claim,
            LastChance,
            Info
        }

        public readonly struct SearchingPlan
        {
            public readonly float DurationSeconds;

            public SearchingPlan(float durationSeconds)
            {
                DurationSeconds = durationSeconds;
            }
        }

        public readonly struct PopupRequest
        {
            public readonly PopupType Type;
            public readonly SearchingPlan Searching;

            public PopupRequest(PopupType type, SearchingPlan searching = default)
            {
                Type = type;
                Searching = searching;
            }
        }
    }
}

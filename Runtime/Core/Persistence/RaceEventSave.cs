using System;
using System.Collections.Generic;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    /// <summary>
    /// Minimal save data for Step 4.
    /// </summary>
    [Serializable]
    public struct RaceEventSave
    {
        /// <summary>
        /// "Window id" computed from (localNow - resetHour). Example: 20251226.
        /// </summary>
        public int LastEntryShownWindowId;

        /// <summary>
        /// Used for cooldown logic (optional). Stored as unix seconds (local clock).
        /// </summary>
        public long LastJoinLocalUnixSeconds;

        public int ConfigCursor;

        public RaceRun? CurrentRun;

        public RaceEventState LastFlowState;
        public long SearchingStartUtcSeconds;

        public List<int> SeenPopupTypes;

        public static RaceEventSave Empty()
        {
            return new RaceEventSave
            {
                LastEntryShownWindowId = 0,
                LastJoinLocalUnixSeconds = 0,
                ConfigCursor = 0,
                CurrentRun = null,
                LastFlowState = RaceEventState.Idle,
                SearchingStartUtcSeconds = 0,
                SeenPopupTypes = new List<int>()
            };
        }
    }
}

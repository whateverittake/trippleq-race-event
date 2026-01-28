using System;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed partial class RaceEventService
    {
        // --------------------
        // Config / Cursor
        // --------------------
        private int ClampConfigIndex(int i) => Math.Clamp(i, 0, _configs.Count - 1);

        private RaceEventConfig GetConfigByIndex(int i) => _configs[ClampConfigIndex(i)];

        internal RaceEventConfig ActiveConfigForRunOrCursor()
        {
            if (_run != null) return GetConfigByIndex(_run.ConfigIndex);

            int eligibleIndex = GetEligibleCursorIndex();

            if (eligibleIndex != _save.ConfigCursor)
            {
                _save.ConfigCursor = eligibleIndex;
                TrySave();
            }

            return GetConfigByIndex(eligibleIndex);
        }

        private int GetEligibleCursorIndex()
        {
            // cursor base (đã clamp)
            int i = ClampConfigIndex(_save.ConfigCursor);

            // lùi dần về 0 để tìm config hợp lệ theo MinPlayerLevel
            while (i > 0 && CurrentLevel < _configs[i].MinPlayerLevel)
                i--;

            return i;
        }
    }
}

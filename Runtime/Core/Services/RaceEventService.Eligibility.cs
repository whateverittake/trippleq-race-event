using System;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed partial class RaceEventService
    {
        // --------------------
        // Eligibility (B)
        // --------------------
        public bool ShouldShowEntryPopup(bool isInTutorial, DateTime localNow)
        {
            ThrowIfNotInitialized();
            // Log("xx 1: " + "ShouldShowEntryPopup");
            // Do not show when already in race flow
            if (State == RaceEventState.Searching || State == RaceEventState.InRace)
                return false;
            // Feature gating
            // Log("xx 2: " + "ShouldShowEntryPopup");
            if (!ActiveConfigForRunOrCursor().Enabled) return false;
            // Tutorial gating
            // Log("xx 3: " + "ShouldShowEntryPopup");
            if (ActiveConfigForRunOrCursor().BlockDuringTutorial && isInTutorial) return false;
            // Min level gating
            // Log("xx 4: " + "ShouldShowEntryPopup: "+ CurrentLevel+"/"+ ActiveConfigForRunOrCursor().MinPlayerLevel);
            if (CurrentLevel < ActiveConfigForRunOrCursor().MinPlayerLevel) return false;

            //Log("xx 8: " + isInTutorial);
            // Cooldown gating (hours)
            // Log("xx 5: " + "ShouldShowEntryPopup");
            if (IsInCooldown(localNow)) return false;
            //Log("xx 9: " + isInTutorial);
            // Once per day gating (based on resetHourLocal)
            // Log("xx 6: " + "ShouldShowEntryPopup");
            if (HasShownEntryInCurrentWindow(localNow)) return false;
            //Log("xx 7: " + "ShouldShowEntryPopup");
            return true;
        }

        private void RefreshEligibility(bool isInTutorial, DateTime localNow)
        {
            // If currently searching/in race, don't downgrade to Eligible/Idle
            if (State == RaceEventState.Searching ||
                State == RaceEventState.InRace ||
                State == RaceEventState.Ended ||
                State == RaceEventState.ExtendOffer)
                return;

            var canShow = ShouldShowEntryPopup(isInTutorial, localNow);
            // We separate 'Eligible' from just 'Idle' to let UI/HUD react later
            _sm.SetState(canShow ? RaceEventState.Eligible : RaceEventState.Idle);
        }

        private bool IsInCooldown(DateTime localNow)
        {
            if (_save.LastJoinLocalUnixSeconds <= 0) return false;

            var lastJoin = DateTimeOffset.FromUnixTimeSeconds(_save.LastJoinLocalUnixSeconds).LocalDateTime;
            var hours = (localNow - lastJoin).TotalHours;
            return hours < ActiveConfigForRunOrCursor().EntryCooldownHours;
        }

        private bool HasShownEntryInCurrentWindow(DateTime localNow)
        {
            // We store the "window id" as the local date of reset boundary.
            // Example: reset at 4AM:
            // - from 04:00 today to 03:59 tomorrow => same window id.

            var windowId = GetWindowId(localNow, ActiveConfigForRunOrCursor().ResetHourLocal);
            return _save.LastEntryShownWindowId == windowId;
        }

        internal int GetWindowId(DateTime localNow, int resetHourLocal)
        {
            // Shift time by reset boundary to make "day window"
            // If resetHourLocal = 4, then 02:00 belongs to previous day window.
            var shifted = localNow.AddHours(-resetHourLocal);
            // int like 20251226
            return shifted.Year * 10000 + shifted.Month * 100 + shifted.Day;
        }
    }
}

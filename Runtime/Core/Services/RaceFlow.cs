using System;
using static TrippleQ.Event.RaceEvent.Runtime.PopupTypes;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed class RaceFlow
    {
        private readonly RaceEventService _owner;

        public RaceFlow(RaceEventService owner)
        {
            _owner = owner;
        }

        public bool CanJoinRace(bool isInTutorial, DateTime localNow)
        {
            if (_owner.State == RaceEventState.Searching || _owner.State == RaceEventState.InRace) return false;
            return _owner.ShouldShowEntryPopup(isInTutorial, localNow);
        }

        /// <summary>
        /// Called by Entry popup when player taps "Play/Join".
        /// </summary>
        public void JoinRace(bool isInTutorial, DateTime localNow)
        {
            _owner.ThrowIfNotInitialized();
            if (!CanJoinRace(isInTutorial, localNow))
            {
                _owner.Log("JoinRace rejected (not eligible)");
                return;
            }

            _owner.BeginSearching(localNow);
        }

        /// <summary>
        /// Called by UI if player closes entry popup without joining.
        /// Still counts as "shown once/day" in this step (common LiveOps behavior).
        /// </summary>
        public void MarkEntryShown(DateTime localNow)
        {
            _owner.ThrowIfNotInitialized();

            _owner.MarkEntryShown(localNow);
        }

        public SearchingPlan GetSearchingSnapshot()
        {
            return _owner.GetSearchingSnapshot();
        }

        public void ConfirmSearchingFinished()
        {
            _owner.ThrowIfNotInitialized();

            if (_owner.State != RaceEventState.Searching)
            {
                _owner.Log($"ConfirmSearchingFinished ignored (State={_owner.State})");
                return;
            }

            _owner.FinishSearchingAndCreateRun();
        }
    }
}

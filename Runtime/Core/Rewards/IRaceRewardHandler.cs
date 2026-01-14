using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public interface IRaceRewardHandler
    {
        /// Return true if claim succeeded and reward was applied.
        bool TryApplyReward(RaceReward reward, int rank, string runId);

        void PlayClaimAnimation(IRaceEndPopupView view, RaceReward reward);
    }
}

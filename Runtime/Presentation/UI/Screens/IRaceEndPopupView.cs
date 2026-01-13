using System;
using System.Collections.Generic;
using TrippleQ.UiKit;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public enum ClaimButtonState
    {
        Hidden,
        Ready,
        Claimed,
        Disabled,
    }

    public enum RaceEndPopupState
    {
        FirstPlace,
        NormalPlace,
        NoClaim,
        LastChance,
        CanExtend,
    }

    public interface IRaceEndPopupView : ITrippleQPopupView
    {
        int PlayerRank { get; set; }
        void SetViewState(RaceEndPopupState state);
        void SetClaimVisible(bool visible);
        void SetExtendVisible(bool extendVisible);
        void SetOnClose(Action onClick);
        void SetOnClaim(Action onClick);
        void SetOnExtend(Action onClick);

        void SetOnCloseToOpenExtendView(Action onClick);

        void SetOnCloseWithoutExtend(Action onClick);

        void SetDataLeaderBoard(List<RaceParticipant> allRacer);
        void RenderLeaderBoard();
        void RenderUserReward();
        void OpenChestAnim();
    }
}

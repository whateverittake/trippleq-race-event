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
        NoReward
    }

    public interface IRaceEndPopupView : ITrippleQPopupView
    {
        void SetViewState(RaceEndPopupState state, RewardData reward);
        void SetClaimVisible(bool visible);
        void SetExtendVisible(bool extendVisible);
        void SetOnClose(Action onClick);
        void SetOnClaim(Action onClick);
        void SetOnExtend(Action onClick);

        void SetOnAcceptNoReward(Action onClick);

        void SetOnCloseToOpenExtendView(Action onClick);

        void SetOnCloseWithoutExtend(Action onClick);
        void SetOnAcceptToHideView(Action onAcceptToHide);
        void SetOnWatchAds(Action onClick);

        void SetDataLeaderBoard(IReadOnlyList<RaceParticipant> allRacer, int playerRank);
        void RenderLeaderBoard();
        void RenderUserReward();
        void OpenChestAnim();
        void ShowAdsBtn(bool canWatchAds);
        void ShowPayCoins(int coinCost);
    }
}

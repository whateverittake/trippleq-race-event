using System;
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
    public interface IRaceEndPopupView : ITrippleQPopupView
    {
        void SetExtendVisible(bool visible);
        void SetClaimState(ClaimButtonState state);

        void SetOnClose(Action onClick);
        void SetOnClaim(Action onClick);
        void SetOnExtend(Action onClick);
    }
}

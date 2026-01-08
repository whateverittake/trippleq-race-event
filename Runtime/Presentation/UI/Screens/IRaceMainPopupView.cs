using System;
using TrippleQ.UiKit;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public interface IRaceMainPopupView : ITrippleQPopupView
    {
        //for debug purpose
        void SetOnEndRace(Action onClick);

        void SetOnClose(Action onClick);

        void SetOnInfoClick(Action onClick);
    }
}

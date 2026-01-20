using System;
using TrippleQ.UiKit;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public interface IRaceEntryPopupView : ITrippleQPopupView
    {
        void PlayEntryTutorial();
        void SetOnAgree(Action onClick);
        void SetOnClose(Action onClick);
    }
}

using System;
using TrippleQ.UiKit;
using static TrippleQ.Event.RaceEvent.Runtime.PopupTypes;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public interface IRaceSearchingPopupView : ITrippleQPopupView
    {
        void Play(SearchingPlan plan, Action onFinished);
        void Stop();
        void SetOnClose(Action onClick);
        void RenderUserAvatar();
    }
}

using System;
using TrippleQ.UiKit;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed class RaceInfoPopupPresenter : BasePopupPresenter<IRaceInfoPopupView>
    {
        private readonly RaceEventService _svc;
        public RaceInfoPopupPresenter(RaceEventService svc)
        {
            _svc = svc;
        }

        protected override void OnBind()
        {
            View.SetClose(()=> 
            {
                Hide();
                _svc.RequestPopup(PopupTypes.PopupType.Main);
            });
        }

        protected override void OnUnbind()
        {
            View.SetClose(null);
        }

        internal void Tick(float deltaTime)
        {
            if (!IsBound) return;
            var localNow = DateTime.Now;
            var s = _svc.GetHudStatus(localNow);

            View.SetTimeStatus(_svc.FormatHMS(s.Remaining));
        }
    }
}

using System;
using TrippleQ.UiKit;
using static TrippleQ.Event.RaceEvent.Runtime.PopupTypes;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed class RaceInfoPopupPresenter : BasePopupPresenter<IRaceInfoPopupView>
    {
        private readonly RaceEventService _svc;

        private float _hudAccum = 0f;

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

        public void Tick(float deltaTime)
        {
            if (!IsBound) return;

            _hudAccum += deltaTime;
            if (_hudAccum < 1f) return;     // 1s update 1 lần
            _hudAccum = 0f;

            var localNow = DateTime.Now;
            var s = _svc.BuildHudStatus(localNow);

            View.SetTimeStatus(_svc.FormatHMS(s.Remaining));
        }

        protected override void OnBeforeShow()
        {
            base.OnBeforeShow();
        }

        protected override void OnAfterShow()
        {
            base.OnAfterShow();
            _svc.NotifyPopupShown(PopupType.Info);
        }

        protected override void OnAfterHide()
        {
            _svc.NotifyPopupHidden(PopupType.Info);
            base.OnAfterHide();
        }
    }
}

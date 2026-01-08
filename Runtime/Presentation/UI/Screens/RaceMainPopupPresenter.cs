using System;
using TrippleQ.UiKit;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceMainPopupPresenter : BasePopupPresenter<IRaceMainPopupView>
    {
        private readonly RaceEventService _svc;
        private readonly Func<bool> _isInTutorial;

        public RaceMainPopupPresenter(RaceEventService svc, Func<bool> isInTutorial)
        {
            _svc = svc;
            _isInTutorial = isInTutorial ?? (() => false);
        }

        override protected void OnBind()
        {
            View.SetOnEndRace(OnForceEndRace);
            View.SetOnInfoClick(OnInfoClick);
            View.SetOnClose(Hide);
            // optional: wire close X nếu view có
            View.SetClose(Hide);
        }

        override protected void OnUnbind()
        {
            View.SetOnEndRace(null);
            View.SetOnClose(null);
            View.SetClose(null);
            View.SetOnInfoClick(null);
        }

        private void OnForceEndRace()
        {
            _svc.DebugEndEvent();
        }

        private void OnInfoClick()
        {
            _svc.OnEnterInfo();
        }
    }
}

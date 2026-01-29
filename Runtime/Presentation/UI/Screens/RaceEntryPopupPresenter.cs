using System;
using TrippleQ.UiKit;
using UnityEngine;
using static TrippleQ.Event.RaceEvent.Runtime.PopupTypes;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed class RaceEntryPopupPresenter : BasePopupPresenter<IRaceEntryPopupView>
    {
        private readonly RaceEventService _svc;
        private readonly Func<bool> _isInTutorial;

        public RaceEntryPopupPresenter(RaceEventService svc, Func<bool> isInTutorial)
        {
            _svc = svc;
            _isInTutorial = isInTutorial ?? (() => false);
        }

        protected override void OnAfterShow()
        {
            base.OnAfterShow();

            if (_svc.ConsumeFirstTimePopup(PopupType.Entry))
            {
                View.PlayEntryTutorial();
            }
        }

        protected override void OnBind()
        {
            View.SetOnAgree(OnAgree);
            View.SetOnClose(Hide);

            // optional: wire close X nếu view có
            View.SetClose(Hide);
        }

        protected override void OnUnbind()
        {
            View.SetOnAgree(null);
            View.SetOnClose(null);
            View.SetClose(null);
        }

        private void OnAgree()
        {
            _svc.TryJoinOrStart(localNow: DateTime.Now);
            // service sẽ RequestPopup(Main/Search…) tuỳ flow, controller sẽ routing
        }
    }
}

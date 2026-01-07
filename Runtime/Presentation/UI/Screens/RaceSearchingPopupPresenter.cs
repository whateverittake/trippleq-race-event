using TrippleQ.UiKit;
using static TrippleQ.Event.RaceEvent.Runtime.PopupTypes;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed class RaceSearchingPopupPresenter : BasePopupPresenter<IRaceSearchingPopupView>
    {
        private readonly RaceEventService _svc;
        private SearchingPlan _plan;

        public RaceSearchingPopupPresenter(RaceEventService svc)
        {
            _svc = svc;
        }

        public void SetPlan(SearchingPlan plan)
        {
            _plan = plan;
        }

        protected override void OnBind()
        {
            View.SetOnClose(OnClose);
            View.SetClose(OnClose);
        }

        protected override void OnAfterShow()
        {
            // start animation/timer mỗi lần show
            View.Play(_plan, OnFinished);
        }

        protected override void OnBeforeHide()
        {
            View.Stop();
        }

        protected override void OnUnbind()
        {
            View.Stop();
            View.SetOnClose(null);
            View.SetClose(null);
        }

        private void OnFinished()
        {
            // service sẽ guard state Searching (nếu popup bị đóng sớm)
            _svc.ConfirmSearchingFinished();
        }

        private void OnClose()
        {
            Hide();
        }
    }
}

using TrippleQ.UiKit;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed class RaceInfoPopupPresenter : BasePopupPresenter<ITrippleQPopupView>
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
    }
}

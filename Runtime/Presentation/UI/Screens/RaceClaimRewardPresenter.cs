using TrippleQ.UiKit;
namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed class RaceClaimRewardPresenter : BasePopupPresenter<IRaceClaimRewardPopupView>
    {
        private readonly RaceEventService _svc;

        public RaceClaimRewardPresenter(RaceEventService svc)
        {
            _svc = svc;
        }

        protected override void OnBind()
        {
            View.SetClose(Hide);

            Render();
        }

        protected override void OnUnbind()
        {
            View.SetClose(null);
        }

        private void Render()
        {

        }
    }
}

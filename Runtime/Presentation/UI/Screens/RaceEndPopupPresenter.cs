using System.Security.Claims;
using TrippleQ.UiKit;
using UnityEngine;
using static UnityEngine.CullingGroup;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed class RaceEndPopupPresenter : BasePopupPresenter<IRaceEndPopupView>
    {
        private readonly RaceEventService _svc;

        public RaceEndPopupPresenter(RaceEventService svc)
        {
            _svc = svc;
        }

        protected override void OnBind()
        {
            View.SetOnClose(Hide);
            View.SetClose(Hide);
            View.SetOnClaim(OnClaim);
            View.SetOnExtend(OnExtend);

            _svc.OnStateChanged += OnStateChanged;
            _svc.OnRunUpdated += OnRunUpdated;

            Render();
        }

        protected override void OnUnbind()
        {
            _svc.OnStateChanged -= OnStateChanged;
            _svc.OnRunUpdated -= OnRunUpdated;

            View.SetOnClose(null);
            View.SetClose(null);
            View.SetOnClaim(null);
            View.SetOnExtend(null);
        }

        private void OnStateChanged(RaceEventState a, RaceEventState b) => Render();
        private void OnRunUpdated(RaceRun? run) => Render();

        private void Render()
        {
            // extend
            View.SetExtendVisible(_svc.CanExtend1H());

            // claim state
            var run = _svc.CurrentRun;
            if (run == null)
            {
                View.SetClaimState(ClaimButtonState.Hidden);
                return;
            }

            if (run.HasClaimed)
            {
                View.SetClaimState(ClaimButtonState.Claimed);
                return;
            }

            if (_svc.CanClaim())
            {
                View.SetClaimState(ClaimButtonState.Ready);
            }
            else
            {
                View.SetClaimState(ClaimButtonState.Disabled);
            }
        }

        private void OnClaim()
        {
            _svc.Claim();
            Render(); // service có thể bắn event, nhưng render ngay cho chắc
        }

        private void OnExtend()
        {
            _svc.Extend1H();
            Render();
        }
    }
}

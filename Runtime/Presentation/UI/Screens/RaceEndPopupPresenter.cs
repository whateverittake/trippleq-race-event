using System.Diagnostics;
using TrippleQ.UiKit;

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
            if (View == null) return;
            if (_svc == null) return;

            //reward +postions rank

            var run = _svc.CurrentRun;
            if (run == null)
            {
                // không có run thì không hiện gì cả
                return;
            }

            SetUpData(run);

            // claim state
            View.SetClaimVisible(_svc.CanClaim());

            //can check truong hop de hien thi state view

            //if (run.GetPlayerRank() == 1)
            //{
            //    View.SetViewState(RaceEndPopupState.FirstPlace);
            //}
            //else
            //{
            //    View.SetViewState(RaceEndPopupState.NormalPlace);
            //}

            //View.SetViewState(RaceEndPopupState.CanExtend);
        }

        private void SetUpData(RaceRun run)
        {
            RaceReward reward = new RaceReward(0, 0, 0, 0, 0, 0);

            if (run.GetPlayerRank() == 1)
            {
                reward = _svc.GetRewardForRank(1);
            }
            else if (_svc.CanClaim())
            {
                reward = _svc.GetRewardForRank(run.GetPlayerRank());
            }

            View.SetReward(reward);
            View.SetDataOpponent(run.Opponents);
            View.PlayerRank = run.GetPlayerRank();
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

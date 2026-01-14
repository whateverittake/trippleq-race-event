using System;
using TrippleQ.UiKit;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceMainPopupPresenter : BasePopupPresenter<IRaceMainPopupView>
    {
        private readonly RaceEventService _svc;
        private readonly Func<bool> _isInTutorial;

        private float _refreshAccum = 0f;

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
            if(_svc.HasRun) View.SetGoal(_svc.CurrentRun.GoalLevels);
        }

        override protected void OnUnbind()
        {
            View.SetOnEndRace(null);
            View.SetOnClose(null);
            View.SetClose(null);
            View.SetOnInfoClick(null);
            View.SetGoal(0);
        }

        protected override void OnAfterShow()
        {
            base.OnAfterShow();

            // Initialize data to show on UI
            if(_svc.CurrentRun == null)
            {
                return;
            }

            View.InitData(_svc.CurrentRun);
            View.InitDataReward(_svc.GetRewardForRank(1),_svc.GetRewardForRank(2),_svc.GetRewardForRank(3));
        }

        internal void Tick(float deltaTime)
        {
            if (!IsBound) return;
            var localNow = DateTime.Now;
            var s = _svc.GetHudStatus(localNow);

            View.SetTimeStatus(_svc.FormatHMS(s.Remaining));

            _refreshAccum += deltaTime;
            if (_refreshAccum < 1f) return;
            _refreshAccum = 0f;

            var run = _svc.CurrentRun;
            if (run != null)
                View.UpdateData(run);
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

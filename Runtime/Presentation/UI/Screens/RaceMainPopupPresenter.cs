using System;
using System.Collections.Generic;
using TrippleQ.UiKit;
using UnityEngine;
using static TrippleQ.Event.RaceEvent.Runtime.PopupTypes;

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

            _svc.NotifyPopupShown(PopupType.Main);

            View.InitData(_svc.CurrentRun);
            View.InitDataReward(_svc.GetRewardForRank(1),_svc.GetRewardForRank(2),_svc.GetRewardForRank(3));

            if (_svc.ConsumeFirstTimePopup(PopupType.Main))
            {
                View.PlayMainTutorial(
                    View.GetRectForTutOne(),
                    View.GetRectForTutTwo(),
                    View.GetRectForTutThree()
                );
            }
        }

        protected override void OnAfterHide()
        {
            _svc.NotifyPopupHidden(PopupType.Main);
            base.OnAfterHide();
        }

        public void Tick(float deltaTime)
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

        #region SELF TUT
        public RectTransform GetRectForTutOne()
        {
            return View.GetRectForTutOne();
        }

        public RectTransform GetRectForTutTwo()
        {
            return View.GetRectForTutTwo();
        }
        public RectTransform GetRectForTutThree() 
        {
            return View.GetRectForTutThree();
        }

        private void PlayRaceEventMainTut(RectTransform rect1, RectTransform rect2, RectTransform rect3)
        {
            // Guard
            if (rect1 == null || rect2 == null || rect3 == null)
                return;

            var targets = new List<RectTransform> { rect1, rect2, rect3 };

            //var steps = new List<TutorialStep>
            //{
            //    new TutorialStep(
            //        id: "Step1",
            //        description: "Tap a chest to see what rewards you can win!",
            //        onEnter: () => _overlay.Show("Tap a chest to see what rewards you can win!", rect1),
            //        onCompleted: null
            //    ),
            //    new TutorialStep(
            //        id: "Step2",
            //        description: "That’s you! Complete levels to score points and race ahead.",
            //        onEnter: () => _overlay.Show("That’s you! Complete levels to score points and race ahead.", rect2),
            //        onCompleted: null
            //    ),
            //    new TutorialStep(
            //        id: "Step3", // ✅ FIX: không trùng Step2
            //        description: "Curious? Tap here to check the event rules.",
            //        onEnter: () => _overlay.Show("Curious? Tap here to check the event rules.", rect3),
            //        onCompleted: null
            //    )
            //};
            //Play(steps, targets);
        }

        #endregion
    }
}

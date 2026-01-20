using System;
using TrippleQ.UiKit;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public interface IRaceMainPopupView : ITrippleQPopupView
    {
        //for debug purpose
        void SetOnEndRace(Action onClick);

        void SetOnClose(Action onClick);

        void SetOnInfoClick(Action onClick);

        void SetTimeStatus(string text);
        void InitData(RaceRun currentRun);

        void UpdateData(RaceRun currentRun);
        void InitDataReward(RaceReward firstRankReward, RaceReward secondRankReward, RaceReward thirdRankReward);

        void SetGoal(int goal);
        RectTransform GetRectForTutOne();
        RectTransform GetRectForTutTwo();
        void PlayMainTutorial(RectTransform rectTransform1, RectTransform rectTransform2 );
    }
}

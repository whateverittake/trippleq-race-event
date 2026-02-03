using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using TrippleQ.UiKit;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceMainPopupView : MonoBehaviour, IRaceMainPopupView
    {
        const string DES_PREFIX = "The first 3 players to clear {0} levels win big rewards!";
        [SerializeField] TMP_Text _timeText, _desText;
        [SerializeField] RaceTrackController _userTrack;
        [SerializeField] RaceTrackController[] _opponentTracks;
        [SerializeField] ChestTooltipHook[] _chestTooltipHooks;
        [SerializeField] RectTransform _infoBtnRect, _closeBtnRect;
        [SerializeField] QuickTutorialOverlayView _quickTutorialOverlayView;

        private Action _onDebugEndRace;
        private Action _onClose;
        private Action _onInfoClick;
        private Action _onCloseOptional;

        // Button hook
        public void OnClickEndRace() => _onDebugEndRace?.Invoke();
        public void OnQuitPopup()
        {
            _onClose?.Invoke();
            _onCloseOptional?.Invoke();
        }

        public void OnClickInfoButton() => _onInfoClick?.Invoke();

        // IRaceMainPopupView
        public bool IsVisible => gameObject.activeSelf;
        public void Show()
        {
            gameObject.SetActive(true);
        }
        public void Hide() => gameObject.SetActive(false);

        public void SetTitle(string title) { }     // optional, nếu popup có title text
        public void SetMessage(string message) { } // optional

        public void SetPrimary(string label, Action onClick) { }
        public void SetSecondary(string label, Action onClick) { }
        public void SetClose(Action onClick) => _onClose = onClick;
        public void SetOnInfoClick(Action onClick) => _onInfoClick = onClick;
        public void SetOnEndRace(Action onClick) => _onDebugEndRace = onClick;
        public void SetOnClose(Action onClick) => _onClose = onClick;
        public void SetCloseOptional(Action onClick)
        {
            _onCloseOptional = onClick;
        }
        public void SetTimeStatus(string text)
        {
            _timeText.text = text;
        }

        public void SetGoal(int goalCount)
        {
            _desText.text= string.Format(DES_PREFIX, goalCount);
        }

        public void InitData(RaceRun currentRun)
        {
            RenderCurrentUI(currentRun);
        }

        public void InitDataReward(RaceReward firstRankReward, RaceReward secondRankReward, RaceReward thirdRankReward)
        {
            if(firstRankReward != null)
            {
                _chestTooltipHooks[0].InitData(firstRankReward.Gold, firstRankReward.Gems, firstRankReward.Booster1, firstRankReward.Booster2, firstRankReward.Booster3, firstRankReward.Booster4);
            }

            if(secondRankReward != null)
            {
                _chestTooltipHooks[1].InitData(secondRankReward.Gold, secondRankReward.Gems, secondRankReward.Booster1, secondRankReward.Booster2, secondRankReward.Booster3, secondRankReward.Booster4);
            }

            if (thirdRankReward != null)
            {
                _chestTooltipHooks[2].InitData(thirdRankReward.Gold, thirdRankReward.Gems, thirdRankReward.Booster1, thirdRankReward.Booster2, thirdRankReward.Booster3, thirdRankReward.Booster4);
            }
        }

        public void UpdateData(RaceRun currentRun)
        {
            RenderCurrentUI(currentRun);
        }

        private void RenderCurrentUI(RaceRun currentRun)
        {
            if (currentRun == null) return;

            // ✅ View chỉ render standings từ snapshot
            var standings = currentRun.UiSnapshot.Top as IList<RaceParticipant>;
            if (standings == null || standings.Count == 0)
            {
                // Fallback an toàn (nếu snapshot chưa kịp refresh)
                standings = new List<RaceParticipant> { currentRun.Player };
                standings = standings.Concat(currentRun.Opponents).ToList();
            }

            // rank = index + 1
            var participantRanks = new Dictionary<RaceParticipant, int>(standings.Count);
            for (int i = 0; i < standings.Count; i++)
                participantRanks[standings[i]] = i + 1;

            RaceParticipant leader = standings.Count > 0 ? standings[0] : null;

            int userRank = participantRanks[currentRun.Player];
            bool isUserLeader = leader == currentRun.Player;
            _userTrack.InitAsPlayer(currentRun.Player, currentRun.GoalLevels, isUserLeader, userRank);

            for (int i = 0; i < _opponentTracks.Length; i++)
            {
                if (i < currentRun.Opponents.Count)
                {
                    var opponent = currentRun.Opponents[i];
                    int opponentRank = participantRanks[opponent];
                    bool isOpponentLeader = leader == opponent;
                    _opponentTracks[i].gameObject.SetActive(true);
                    _opponentTracks[i].InitAsOpponent(currentRun.Opponents[i], currentRun.GoalLevels, isOpponentLeader, opponentRank);
                }
                else
                {
                    _opponentTracks[i].gameObject.SetActive(false);
                }
            }
        }

        #region TUT
        public RectTransform GetRectForTutOne()
        {
            return _infoBtnRect;
        }

        public RectTransform GetRectForTutTwo()
        {
            return _closeBtnRect;
        }

        public void PlayMainTutorial(RectTransform r1)
        {
            var targets = new[] { r1};
            var texts = new[]
            {
                "Tap to view the race rules."
            };

            _quickTutorialOverlayView.Play(targets, texts);
        }

        public void PlayMainTutorial2(RectTransform r2)
        {
            var targets = new[] {r2 };
            var texts = new[]
            {
                "Tap to close."
            };

            _quickTutorialOverlayView.Play(targets, texts);
        }

        #endregion
    }
}

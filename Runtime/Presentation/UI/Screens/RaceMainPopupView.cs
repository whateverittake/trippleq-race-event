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
        [SerializeField] TMP_Text _timeText;
        [SerializeField] RaceTrackController _userTrack;
        [SerializeField] RaceTrackController[] _opponentTracks;
        [SerializeField] ChestTooltipHook[] _chestTooltipHooks;

        private Action _onDebugEndRace;
        private Action _onClose;
        private Action _onInfoClick;

        // Button hook
        public void OnClickEndRace() => _onDebugEndRace?.Invoke();
        public void OnQuitPopup() => _onClose?.Invoke();

        public void OnClickInfoButton() => _onInfoClick?.Invoke();

        // IRaceMainPopupView
        public bool IsVisible => gameObject.activeSelf;
        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);

        public void SetTitle(string title) { }     // optional, nếu popup có title text
        public void SetMessage(string message) { } // optional

        public void SetPrimary(string label, Action onClick) { }
        public void SetSecondary(string label, Action onClick) { }
        public void SetClose(Action onClick) => _onClose = onClick;
        public void SetOnInfoClick(Action onClick) => _onInfoClick = onClick;
        public void SetOnEndRace(Action onClick) => _onDebugEndRace = onClick;
        public void SetOnClose(Action onClick) => _onClose = onClick;

        public void SetTimeStatus(string text)
        {
            _timeText.text = text;
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
            //nếu finish, cần biết đc thứ tự finish để show quà

            // Gom tất cả participant (player + opponents)
            var allParticipants = new List<RaceParticipant> { currentRun.Player };
            allParticipants.AddRange(currentRun.Opponents);

            // Xác định thứ tự về đích (finish order)
            var finishedList = allParticipants
                            .Where(p => p.HasFinished)
                            .OrderBy(p => p.FinishedUtcSeconds)
                            .ToList();


            // Gán rank cho người đã finish, ai chưa finish thì rank = -1
            var participantRanks = new Dictionary<RaceParticipant, int>();
            for (int i = 0; i < finishedList.Count; i++)
            {
                participantRanks[finishedList[i]] = i + 1; // rank bắt đầu từ 1
            }

            // Xác định leader
            RaceParticipant leader = null;
            if (finishedList.Count > 0)
            {
                // Nếu đã có người finish, leader là người finish đầu tiên
                leader = finishedList[0];
            }
            else
            {
                // Nếu chưa ai finish, leader là người có LevelsCompleted cao nhất
                int maxLevel = allParticipants.Max(p => p.LevelsCompleted);
                var topLevel = allParticipants.Where(p => p.LevelsCompleted == maxLevel).ToList();
                leader = topLevel.FirstOrDefault(p => p == currentRun.Player) ?? topLevel.First();
            }

            // Render player
            int userRank = participantRanks.ContainsKey(currentRun.Player) ? participantRanks[currentRun.Player] : -1;
            bool isUserLeader = leader == currentRun.Player;

            _userTrack.InitAsPlayer(currentRun.Player, currentRun.GoalLevels, isUserLeader, userRank);

            for (int i = 0; i < _opponentTracks.Length; i++)
            {
                if (i < currentRun.Opponents.Count)
                {
                    var opponent = currentRun.Opponents[i];
                    int opponentRank = participantRanks.ContainsKey(opponent) ? participantRanks[opponent] : -1;
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
    }
}

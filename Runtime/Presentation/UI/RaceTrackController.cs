using System.Collections.Generic;
using TMPro;
using TrippleQ.AvatarSystem;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceTrackController : MonoBehaviour
    {
        const string USER_NAME = "You";
        const string PrefixAvatar= "avatar";

        [SerializeField] AvatarItemView _avatarItemView;
        [SerializeField] GameObject _baloon;
        [SerializeField] GameObject _scoreObj;
        [SerializeField] TMP_Text _scoreText,_nameText;
        [SerializeField] GameObject _scoreBg, _leadScoreBg, _leadRaceTrackBg;
        [SerializeField] GameObject _userBgName, _otherBgName;
        [SerializeField] GameObject[] _stepMoves; //max 10 step for now
        [SerializeField] GameObject _defaultStartStep;
        [SerializeField] GameObject _rewardObj;
        [SerializeField] GameObject[] _rewardIconArr;

        [SerializeField] RectTransform _avatarRect;

        private List<GameObject> _listStepUse= null;

        public void SetUp(bool isUser)
        {
            if (isUser) 
            {
                _nameText.text = USER_NAME;
                _nameText.color = Color.yellow;
                _avatarItemView.Refresh();
                _userBgName.SetActive(true);
                _otherBgName.SetActive(false);
            }
            else 
            {
                _nameText.color = Color.white;
                _userBgName.SetActive(false);
                _otherBgName.SetActive(true);
            }

            _scoreObj.SetActive(false);
            _scoreBg.SetActive(false);
            _leadScoreBg.SetActive(false);
            _leadRaceTrackBg.SetActive(false);
            UpdatePosAvatar(0);
        }

        private void UpdateScore(int score, bool isLeader)
        {
            _scoreObj.SetActive(true);
            _scoreText.text = score.ToString();

            if (isLeader)
            {
                _leadScoreBg.SetActive(true);
                _leadRaceTrackBg.SetActive(true) ;
                _scoreBg.SetActive(false);
            }
            else
            {
                _leadScoreBg.SetActive(false);
                _leadRaceTrackBg.SetActive(false);
                _scoreBg.SetActive(true);
            }

            if(score<=0) _leadRaceTrackBg.SetActive(false);
        }

        internal void InitAsOpponent(RaceParticipant raceParticipant, int goalLevels, bool isLeader, int rewardRankId)
        {
            SetUp(false);

            _avatarItemView.UpDateAvatar(AvatarIconResolver.Get(PrefixAvatar+raceParticipant.AvatarId));
            _nameText.text = raceParticipant.DisplayName;


            SetUpStep(goalLevels);

            UpdateScore(raceParticipant.LevelsCompleted, isLeader);
            UpdatePosAvatar(raceParticipant.LevelsCompleted);

            UpdateReward(rewardRankId);
        }

        internal void InitAsPlayer(RaceParticipant player, int goalLevels, bool isLeader, int rewardRankId)
        {
            SetUp(true);

            SetUpStep(goalLevels);
            UpdateScore(player.LevelsCompleted, isLeader);
            UpdatePosAvatar(player.LevelsCompleted);

            UpdateReward(rewardRankId);
        }

        private void UpdateReward(int rewardRankId)
        {
            if(rewardRankId <= 0 || rewardRankId>3)
            {
                //disable reward Obj
                _rewardObj.SetActive(false);
                return;
            }
             
            //endable reward icon follow id
            _rewardObj.SetActive(true);
            for (int i = 0; i < _rewardIconArr.Length; i++)
            {
                if (i == rewardRankId - 1)
                {
                    _rewardIconArr[i].SetActive(true);
                }
                else
                {
                    _rewardIconArr[i].SetActive(false);
                }
            }
        }

        private void UpdatePosAvatar(int levelsCompleted)
        {
            if (_baloon == null)
                return;

            if (_listStepUse == null || _listStepUse.Count == 0)
                return;

            if (levelsCompleted <= 0)
            {
                if (_defaultStartStep != null)
                {
                    _baloon.transform.SetParent(_defaultStartStep.transform, false);
                    _baloon.transform.localPosition = Vector3.zero;
                }
            }
            else
            {
                int stepIdx = Mathf.Clamp(levelsCompleted - 1, 0, _listStepUse.Count - 1);
                var stepObj = _listStepUse[stepIdx];
                if (stepObj != null)
                {
                    _baloon.transform.SetParent(stepObj.transform, false);
                    _baloon.transform.localPosition = Vector3.zero;
                }
            }

            _baloon.gameObject.SetActive(true);
        }

        private void SetUpStep(int maxCount)
        {
            _listStepUse= new List<GameObject>();
            if (maxCount > 0)
            {
                for (int i = 0; i < _stepMoves.Length; i++)
                {
                    if (i < maxCount)
                    {
                        _stepMoves[i].SetActive(true);
                        _listStepUse.Add(_stepMoves[i]);
                    }
                    else
                    {
                        _stepMoves[i].SetActive(false);
                    }
                }
            }
        }

        public RectTransform GetAvatarRect()
        {
            return _avatarRect;
        }
    }
}

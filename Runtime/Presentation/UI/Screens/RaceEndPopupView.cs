using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using TrippleQ.AvatarSystem;
using UnityEngine;
using UnityEngine.UI;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceEndPopupView : MonoBehaviour, IRaceEndPopupView
    {
        const string PrefixAvatar = "avatar";
        const string PrefixDesFirstRank = "You defeated 4 opponents";
        const string PrefixDesOtherRank = "You finishd {0}.Revenge awaits next race";

        [SerializeField] GameObject _claimButton, _extendButton;
        [SerializeField] GameObject _rankView, _extendOfferView;
        [SerializeField] GameObject _lastChanceView;

        [SerializeField] Sprite _titleChampion, _titleFininsh;
        [SerializeField] Image _titleImage;

        [SerializeField] TMP_Text _desText;
        [SerializeField] Sprite[] _chestIconClose;
        [SerializeField] Sprite[] _chestIconOpen;
        [SerializeField] Image _chestImage;

        [SerializeField] RankClaimRewardUI[] _leaderBoardRanks;

        private Action _onClose;
        private Action _onClaim;
        private Action _onExtend;
        private Action _onCloseToOpenExtendView;
        private Action _onCloseWithoutExtend;

        private RaceParticipant _player;
        private List<RaceParticipant> _racer;
        private int _playerRank = 1;

        // ===== Button hooks (gán từ UI Button OnClick) =====
        public void OnQuitPopup() => _onClose?.Invoke();
        public void OnClaimReward() => _onClaim?.Invoke();
        public void CloseAndOpenExtendViewClick()
        {
            _onCloseToOpenExtendView?.Invoke();
        }

        public void CloseWithoutExtendClick()
        {
            _onCloseWithoutExtend?.Invoke();
        }

        public void OnExtendRaceClick() => _onExtend?.Invoke();

        // ===== IRaceEndPopupView =====

        public void SetViewState(RaceEndPopupState state)
        {
            HideAll();
           
            switch (state)
            {
                case RaceEndPopupState.FirstPlace:
                    Render1stReward();
                    break;
                case RaceEndPopupState.NormalPlace:
                    RenderNormalReward();
                    break;
                case RaceEndPopupState.NoClaim:
                    if(_playerRank==1) Render1stReward();
                    else RenderNormalReward();
                    break;
                case RaceEndPopupState.LastChance:
                    RenderLastChance();
                    break;
                case RaceEndPopupState.CanExtend:
                    RenderCanExtendOffer();
                    break;

            }
        }

        public void SetOnClose(Action onClick) => _onClose = onClick;
        public void SetOnClaim(Action onClick) => _onClaim = onClick;
        public void SetOnExtend(Action onClick) => _onExtend = onClick;

        // ===== ITrippleQPopupView tối thiểu =====
        public bool IsVisible => gameObject.activeSelf;

        public int PlayerRank
        {
                get 
                { 
                    return _playerRank; 
                }
                set
                {
                    _playerRank = value;
                }
        } 

        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);

        public void SetTitle(string title) 
        {

        }
        public void SetMessage(string message) 
        {
            _desText.text = message;
        }
        public void SetPrimary(string label, Action onClick) { }
        public void SetSecondary(string label, Action onClick) { }
        public void SetClose(Action onClick) => _onClose = onClick;

        public void SetDataLeaderBoard(List<RaceParticipant> racers)
        {
            _racer = racers;
        }

        public void RenderLeaderBoard()
        {
            if (_leaderBoardRanks == null || _leaderBoardRanks.Length == 0)
                return;

            // clear all slots first
            for (int i = 0; i < _leaderBoardRanks.Length; i++)
                _leaderBoardRanks[i].RenderData(null, string.Empty);

            if (_racer == null || _racer.Count == 0)
                return;

            var finishedList = _racer
                            .Where(p => p.HasFinished)
                            .OrderBy(p => p.FinishedUtcSeconds)
                            .Take(_leaderBoardRanks.Length)
                            .ToList();

            for (int i = 0; i < finishedList.Count && i < _leaderBoardRanks.Length; i++)
            {
                var part = finishedList[i];
                _leaderBoardRanks[i].RenderData(AvatarIconResolver.Get(PrefixAvatar + part.AvatarId), part?.DisplayName ?? string.Empty);
            }

            string suff = string.Empty;
            var rankList = _racer
                        .Where(p => p.HasFinished)
                        .OrderBy(p => p.FinishedUtcSeconds)
                        .ToList();
            int playerRank = 0;
            for (int i = 0; playerRank < rankList.Count; i++)
            {
                var part = rankList[i];
                if (part != null)
                {
                    if (part == _player)
                    {
                        playerRank = i + 1;
                    }
                }
            }

            switch (playerRank)
            {
                case 1:
                    _desText.text = PrefixDesFirstRank;
                    break;
                case 2:
                    _desText.text = string.Format(PrefixDesOtherRank, "2nd");
                    break;
                case 3:
                    _desText.text = string.Format(PrefixDesFirstRank, "3rd");
                    break;
                case 4:
                    _desText.text = string.Format(PrefixDesOtherRank, "4th"); 
                    break;
                case 5:
                    _desText.text = string.Format(PrefixDesOtherRank, "5th");
                    break;
                default:
                    Debug.LogError("bugggg");
                    break;
            }
        }

        public void RenderUserReward()
        {
            switch (_playerRank)
            {
                case 1:
                    //1st
                    _chestImage.sprite = _chestIconClose[0];
                    break;
                case 2:
                    //2nd
                    _chestImage.sprite = _chestIconClose[1];
                    break;
                case 3:
                    _chestImage.sprite = _chestIconClose[2];
                    break;
                default:
                    _chestImage.sprite = _chestIconClose[3];
                    break;

            }
        }

        private void Render1stReward()
        {
            HideAll();
            _rankView?.SetActive(true);
            _titleImage.sprite = _titleChampion;
            _titleImage.SetNativeSize();
        }

        private void RenderNormalReward()
        {
            HideAll();
            _rankView?.SetActive(true);
            _titleImage.sprite = _titleFininsh;
            _titleImage.SetNativeSize();
        }

        private void RenderCanExtendOffer()
        {
            HideAll();
            _extendOfferView?.SetActive(true);
        }

        private void RenderLastChance()
        {
            HideAll();
            _lastChanceView?.SetActive(true);
        }

        private void HideAll()
        {
            _rankView?.SetActive(false);
            _extendOfferView?.SetActive(false);
            _lastChanceView?.SetActive(false);
        }

        public void SetClaimVisible(bool visible)
        {
            _claimButton?.SetActive(visible);
        }

        public void SetExtendVisible(bool extendVisible)
        {
            _extendButton?.SetActive(extendVisible);
        }

        public void SetOnCloseToOpenExtendView(Action onClick)
        {
            _onCloseToOpenExtendView= onClick;
        }

        public void SetOnCloseWithoutExtend(Action onClick)
        {
            _onCloseWithoutExtend = onClick;
        }

        public void OpenChestAnim()
        {
            int index = 0;
            for (int i = 0; i < _chestIconClose.Count(); i++) 
            {
                if(_chestIconClose[i]== _chestImage.sprite)
                {
                    index = i;
                    break;
                }
            }

            _chestImage.sprite = _chestIconOpen[index];
        }
    }
}

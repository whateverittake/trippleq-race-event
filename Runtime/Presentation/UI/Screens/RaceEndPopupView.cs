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
        const string PrefixLastChanceDes = "You're currently in {0} place";

        private static readonly string[] FirstPlaceLines =
        {
            "You beat all 4 opponents!",
            "Champion! You crushed the competition!",
            "Victory! You dominated the race!",
            "Unstoppable! You beat all rivals!"
        };

        private static readonly string[] OtherRankLines =
        {
            "You finished {0}. Try again next race!",
            "You finished {0}. Ready for a comeback?",
            "You finished {0}. Better luck next time!"
        };

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

        [Space]
        [SerializeField] AvatarItemView _lastChanceAvatar;
        [SerializeField] TMP_Text _desLastChance, _paidCoinText;
        [SerializeField] GameObject _adsBtn,_paidCoinBtn;

        private Action _onClose;
        private Action _onClaim;
        private Action _onExtend;
        private Action _onCloseToOpenExtendView;
        private Action _onCloseWithoutExtend;
        private Action _onWatchAds;
        private Action _onCloseOptional;

        private IReadOnlyList<RaceParticipant> _racer;
        private int _playerRank = 1;

        // ===== Button hooks (gán từ UI Button OnClick) =====
        public void OnQuitPopup() 
        { 
            _onClose?.Invoke();
            _onCloseOptional?.Invoke();
        }

        public void OnClaimReward() 
        {
            _onClaim?.Invoke();
            _onCloseOptional?.Invoke();
        }
        public void CloseAndOpenExtendViewClick()
        {
            _onCloseToOpenExtendView?.Invoke();
        }

        public void CloseWithoutExtendClick()
        {
            _onCloseWithoutExtend?.Invoke();
            _onCloseOptional?.Invoke();
        }

        public void OnExtendRaceClick() => _onExtend?.Invoke();

        public void OnWatchAdsClick()=> _onWatchAds?.Invoke();

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

        public void SetCloseOptional(Action onClick)
        {
            _onCloseOptional= onClick;
        }
        public void SetOnWatchAds(Action onClick) => _onWatchAds = onClick;

        // ===== ITrippleQPopupView tối thiểu =====
        public bool IsVisible => gameObject.activeSelf;

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

        public void SetDataLeaderBoard(IReadOnlyList<RaceParticipant> racers, int playerRank)
        {
            _racer = racers;
            _playerRank = playerRank;
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

            int count = Mathf.Min(_racer.Count, _leaderBoardRanks.Length);
            for (int i = 0; i < count; i++)
            {
                var part = _racer[i];
                _leaderBoardRanks[i].RenderData(
                    AvatarIconResolver.Get(PrefixAvatar + part.AvatarId),
                    part?.DisplayName ?? string.Empty
                );
            }

            // description dựa trên _playerRank (đã từ service)
            _desText.text = (_playerRank == 1)
                ? GetFirstPlaceLine()
                : GetOtherRankLine(_playerRank);
        }

        public string GetFirstPlaceLine()
        {
            return FirstPlaceLines[UnityEngine.Random.Range(0, FirstPlaceLines.Length)];
        }

        public string GetOtherRankLine(int rank)
        {
            var line = OtherRankLines[UnityEngine.Random.Range(0, OtherRankLines.Length)];
            return string.Format(line, ToOrdinal(rank));
        }

        private static string ToOrdinal(int n)
        {
            if (n % 100 is 11 or 12 or 13) return $"{n}th";
            return (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" };
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
            _lastChanceAvatar.Refresh();
            _desLastChance.text = string.Format(PrefixLastChanceDes, ToOrdinal(_playerRank));
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

        public void ShowAdsBtn(bool isShow)
        {
            _adsBtn.gameObject.SetActive(isShow);
            _paidCoinBtn.gameObject.SetActive(!isShow);
        }

        public void ShowPayCoins(int coinCost)
        {
            _paidCoinText.text = coinCost.ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceEndPopupView : MonoBehaviour, IRaceEndPopupView
    {
        [SerializeField] GameObject _claimButton, _extendButton;
        [SerializeField] GameObject _firstRankView, _normalRankView, _extendOfferView;
        [SerializeField] GameObject _lastChanceView;

        private Action _onClose;
        private Action _onClaim;
        private Action _onExtend;
        private Action _onCloseToOpenExtendView;
        private Action _onCloseWithoutExtend;

        private RaceReward _reward;
        private List<RaceParticipant> _opponents;
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

        public void SetTitle(string title) { }
        public void SetMessage(string message) { }
        public void SetPrimary(string label, Action onClick) { }
        public void SetSecondary(string label, Action onClick) { }
        public void SetClose(Action onClick) => _onClose = onClick;

        public void SetReward(RaceReward raceReward)
        {
            _reward = raceReward;
        }

        public void SetDataOpponent(List<RaceParticipant> opponents)
        {
            _opponents= opponents;
        }

        private void Render1stReward()
        {
            HideAll();
            _firstRankView?.SetActive(true);
        }

        private void RenderNormalReward()
        {
            HideAll();
            _normalRankView?.SetActive(true);
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
            _firstRankView?.SetActive(false);
            _normalRankView?.SetActive(false);
            _extendOfferView?.SetActive(false);
            _lastChanceView?.SetActive(false);
        }

        public void SetClaimVisible(bool visible)
        {
            _claimButton?.SetActive(visible);
        }

        public void SetOnCloseToOpenExtendView(Action onClick)
        {
            _onCloseToOpenExtendView= onClick;
        }

        public void SetOnCloseWithoutExtend(Action onClick)
        {
            _onCloseWithoutExtend = onClick;
        }
    }
}

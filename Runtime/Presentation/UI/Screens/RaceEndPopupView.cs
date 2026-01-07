using System;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceEndPopupView : MonoBehaviour, IRaceEndPopupView
    {
        [SerializeField] GameObject _claimButton, _extendButton;

        private Action _onClose;
        private Action _onClaim;
        private Action _onExtend;

        // ===== Button hooks (gán từ UI Button OnClick) =====
        public void OnQuitPopup() => _onClose?.Invoke();
        public void OnClaimReward() => _onClaim?.Invoke();
        public void OnExtend1H() => _onExtend?.Invoke();

        // ===== IRaceEndPopupView =====
        public void SetExtendVisible(bool visible)
        {
            if (_extendButton != null) _extendButton.SetActive(visible);
        }

        public void SetClaimState(ClaimButtonState state)
        {
            // Hiện tại prefab chỉ có 1 gameobject claim button,
            // nên state khác nhau thì mapping đơn giản:
            // - Ready => show
            // - còn lại => hide
            // (Sau này nếu có label/disable state thì nâng cấp)
            bool show = state == ClaimButtonState.Ready;
            if (_claimButton != null) _claimButton.SetActive(show);
        }

        public void SetOnClose(Action onClick) => _onClose = onClick;
        public void SetOnClaim(Action onClick) => _onClaim = onClick;
        public void SetOnExtend(Action onClick) => _onExtend = onClick;

        // ===== ITrippleQPopupView tối thiểu =====
        public bool IsVisible => gameObject.activeSelf;
        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);

        public void SetTitle(string title) { }
        public void SetMessage(string message) { }
        public void SetPrimary(string label, Action onClick) { }
        public void SetSecondary(string label, Action onClick) { }
        public void SetClose(Action onClick) => _onClose = onClick;

        public void SetInteractable(bool interactable)
        {
            
        }

        public void SetLoading(bool isLoading)
        {
            
        }
    }
}

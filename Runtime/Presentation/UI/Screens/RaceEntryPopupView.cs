using System;
using TrippleQ.AvatarSystem;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceEntryPopupView : MonoBehaviour, IRaceEntryPopupView
    {
        [SerializeField] AvatarItemView _avatar;

        private Action _onAgree;
        private Action _onClose;

        // Button hook
        public void OnClick_AgreeEnterRace() => _onAgree?.Invoke();
        public void OnQuitPopup() => _onClose?.Invoke();

        // IRaceEntryPopupView
        public bool IsVisible => gameObject.activeSelf;
        public void Show() 
        {
            gameObject.SetActive(true);
            _avatar.Refresh();
        }
        public void Hide() => gameObject.SetActive(false);

        public void SetTitle(string title) { }     // optional, nếu popup có title text
        public void SetMessage(string message) { } // optional

        public void SetPrimary(string label, Action onClick) => _onAgree = onClick;
        public void SetSecondary(string label, Action onClick) { }
        public void SetClose(Action onClick) => _onClose = onClick;

        public void SetOnAgree(Action onClick) => _onAgree = onClick;
        public void SetOnClose(Action onClick) => _onClose = onClick;

        public void SetInteractable(bool interactable)
        {
            
        }

        public void SetLoading(bool isLoading)
        {
            
        }
    }
}

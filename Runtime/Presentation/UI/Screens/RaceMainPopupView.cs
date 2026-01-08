using System;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceMainPopupView : MonoBehaviour, IRaceMainPopupView
    {
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

        public void SetInteractable(bool interactable)
        {

        }

        public void SetLoading(bool isLoading)
        {

        }
    }
}

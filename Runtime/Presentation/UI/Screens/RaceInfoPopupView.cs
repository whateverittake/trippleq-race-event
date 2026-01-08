using System;
using TrippleQ.UiKit;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceInfoPopupView : MonoBehaviour, ITrippleQPopupView
    {
        private Action _onAgree;

        public bool IsVisible => gameObject.activeSelf;

        public void OnAgreeClick() => _onAgree?.Invoke();

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        public void SetClose(Action onClick)
        {
            _onAgree = onClick;
        }

        public void SetInteractable(bool interactable)
        {
            
        }

        public void SetLoading(bool isLoading)
        {
            
        }

        public void SetMessage(string message)
        {
           
        }

        public void SetPrimary(string label, Action onClick)
        {
            
        }

        public void SetSecondary(string label, Action onClick)
        {
            
        }

        public void SetTitle(string title)
        {
            
        }

        public void Show()
        {
            gameObject.SetActive(true);
        }
    }
}

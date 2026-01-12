using System;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceClaimRewardPopupView : MonoBehaviour, IRaceClaimRewardPopupView
    {
        public bool IsVisible => gameObject.activeSelf;

        public void Hide() => gameObject.SetActive(false);

        public void SetClose(Action onClick)
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

        public void Show() => gameObject.SetActive(true);
    }
}

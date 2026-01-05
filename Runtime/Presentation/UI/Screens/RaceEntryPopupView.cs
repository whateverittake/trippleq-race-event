using System;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceEntryPopupView : MonoBehaviour
    {
        private RaceEventService _service;

        public void OnClick_AgreeEnterRace()
        {
            _service.JoinRace(isInTutorial: false,
                                localNow: DateTime.Now);
        }

        public void OnQuitPopup()
        {
            gameObject.SetActive(false);
        }

        internal void Bind(RaceEventService svc)
        {
            _service= svc;
        }
    }
}

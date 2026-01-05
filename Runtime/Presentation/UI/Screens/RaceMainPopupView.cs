using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceMainPopupView : MonoBehaviour
    {
        private RaceEventService _service;

        public void OnQuitPopup()
       {
         gameObject.SetActive(false);
       }

        public void Bind(RaceEventService svc)
        {
            _service= svc;
        }

        public void OnClickEndRace()
        {
            _service.DebugEndEvent();
        }
    }
}

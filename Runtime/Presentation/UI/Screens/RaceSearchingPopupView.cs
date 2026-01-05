using System.Collections;
using UnityEngine;
using static TrippleQ.Event.RaceEvent.Runtime.PopupTypes;
using static TrippleQ.Event.RaceEvent.Runtime.RaceEventService;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceSearchingPopupView : MonoBehaviour
    {
        private RaceEventService _service;
        private float _duration = 4f;

        public void Bind(RaceEventService service)
        {
            _service = service;
        }

        public void Show(SearchingPlan plan)
        {
            _duration = Mathf.Max(0.1f, plan.DurationSeconds);
            //gameObject.SetActive(true);
        }

        private void OnEnable()
        {
            StopAllCoroutines();
            StartCoroutine(CoSearching());
        }

        private IEnumerator CoSearching()
        {
            // TODO: reveal opponent slots here

            yield return new WaitForSeconds(_duration);

            _service.ConfirmSearchingFinished();
        }
    }
}

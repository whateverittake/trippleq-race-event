using System;
using System.Collections;
using UnityEngine;
using static TrippleQ.Event.RaceEvent.Runtime.PopupTypes;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceSearchingPopupView : MonoBehaviour, IRaceSearchingPopupView
    {
        private Coroutine _co;
        private Action _onFinished;
        private Action _onClose;

        public void Play(SearchingPlan plan, Action onFinished)
        {
            _onFinished = onFinished;
            var duration = Mathf.Max(0.1f, plan.DurationSeconds);

            Stop(); // restart clean
            _co = StartCoroutine(Co(duration));
        }

        public void Stop()
        {
            if (_co != null)
            {
                StopCoroutine(_co);
                _co = null;
            }
            //_onFinished = null;
        }

        private IEnumerator Co(float duration)
        {
            yield return new WaitForSeconds(duration);
            _co = null;
            _onFinished?.Invoke();
        }

        public void SetOnClose(Action onClick) => _onClose = onClick;

        // Hook button
        public void OnQuitPopup() => _onClose?.Invoke();

        private void OnDisable()
        {
            Stop();
        }

        // ===== ITrippleQPopupView minimal =====
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

        //private float _duration = 4f;
        //private bool _armed; // chỉ cho phép chạy khi Show() đã được gọi

        //public void Bind(RaceEventService service)
        //{
        //    _service = service;
        //}

        //public void Show(SearchingPlan plan)
        //{
        //    _duration = Mathf.Max(0.1f, plan.DurationSeconds);
        //    //gameObject.SetActive(true);
        //    _armed = true;

        //    // restart timer mỗi lần show
        //    if (_co != null) StopCoroutine(_co);
        //    _co = StartCoroutine(CoSearching());
        //}

        //public void Hide()
        //{
        //    _armed = false;
        //    if (_co != null)
        //    {
        //        StopCoroutine(_co);
        //        _co = null;
        //    }
        //}

        //private void OnDisable()
        //{
        //    // đảm bảo tắt popup thì timer cũng tắt
        //    Hide();
        //}


        //private IEnumerator CoSearching()
        //{
        //    // nếu view bật lên nhưng chưa Show() thì không chạy
        //    if (!_armed) yield break;

        //    if (_service == null)
        //    {
        //        Debug.LogError("[RaceSearchingPopupView] Missing service bind.");
        //        yield break;
        //    }

        //    yield return new WaitForSeconds(_duration);

        //    // service sẽ tự guard state Searching
        //    _service.ConfirmSearchingFinished();
        //}
    }
}

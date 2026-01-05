using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceEndPopupView : MonoBehaviour
    {
        [SerializeField] GameObject _claimButton, _extendButton;

        private RaceEventService _svc;

        private void OnEnable()
        {
            if (_svc != null)
            {
                _svc.OnStateChanged += HandleStateChanged;
                _svc.OnRunUpdated += HandleRunUpdated;
            }

            UpdateUI();
        }

        private void OnDisable()
        {
            if (_svc != null)
            {
                _svc.OnStateChanged -= HandleStateChanged;
                _svc.OnRunUpdated -= HandleRunUpdated;
            }
        }

        private void HandleStateChanged(RaceEventState a, RaceEventState b) => UpdateUI();
        private void HandleRunUpdated(RaceRun? run) => UpdateUI();

        private void UpdateUI()
        {
            if (_svc == null) return;
            bool canExtend = _svc.CanExtend1H();
            _extendButton.SetActive(canExtend);

            var run = _svc.CurrentRun;
            if (run == null)
            {
                _claimButton.SetActive(false);
                return;
            }

            if (run.HasClaimed)
            {
                SetClaimButtonClaimed();
            }
            else if (_svc.CanClaim())
            {
                SetClaimButtonReady();
            }
            else
            {
                SetClaimButtonDisabled();
            }
        }

        public void Bind(RaceEventService svc)
        {
            _svc= svc;
            UpdateUI();
        }

        private void SetClaimButtonClaimed()
        {
            _claimButton.SetActive(false);
        }
        private void SetClaimButtonReady()
        {
            _claimButton.SetActive(true);
        }
        private void SetClaimButtonDisabled()
        {
            _claimButton.SetActive(false);
        }

        public void OnQuitPopup()
        {
            gameObject.SetActive(false);
        }

        public void OnClaimReward()
        {
            _svc.Claim();
            UpdateUI();
        }

        public void OnExtend1H()
        {
            _svc.Extend1H();
            UpdateUI();
        }
    }
}

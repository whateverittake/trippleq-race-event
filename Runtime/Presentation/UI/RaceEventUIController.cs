using System;
using TMPro;
using UnityEngine;
using static TrippleQ.Event.RaceEvent.Runtime.PopupTypes;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed class RaceEventUIController : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private RaceEventBootstrap _bootstrap;

        [Header("Views")]
        [SerializeField] private RaceSearchingPopupView _searchingView;
        [SerializeField] private RaceEndPopupView _endedView;
        [SerializeField] private RaceMainPopupView _mainPopupView;
        [SerializeField] private RaceEntryPopupView _entryView;
        [SerializeField] private RaceEventHudWidgetView _hudWidgetView;

        [Header("Debug UI")]
        [SerializeField] private TMP_Text _iconText;

        private RaceEventService _svc;

        private void Awake()
        {
            // nếu bootstrap Awake chạy trước thì Service đã có sẵn
            if (_bootstrap != null && _bootstrap.Service != null)
            {
                Bind(_bootstrap.Service);
                return;
            }

            // fallback: chờ signal ready
            _bootstrap.OnServiceReady += Bind;
        }

        private void OnDestroy()
        {
            if (_bootstrap != null)
                _bootstrap.OnServiceReady -= Bind;

            Unbind();
        }

        private void Bind(RaceEventService svc)
        {
            if (_svc == svc) return;

            Unbind();
            _svc = svc;

            // --- events ---
            _svc.OnStateChanged += OnStateChanged;
            _svc.OnPopupRequested += HandlePopup;
            _svc.OnRunUpdated += OnRunUpdated;
            _svc.OnRewardGranted += OnRewardGranted;

            // --- initial bind snapshot ---
            ReplaySnapshot();

            // HUD bind
            _hudWidgetView.Bind(_svc, isInTutorialGetter: () => false);
        }

        private void Unbind()
        {
            if (_svc == null) return;

            _svc.OnStateChanged -= OnStateChanged;
            _svc.OnPopupRequested -= HandlePopup;
            _svc.OnRunUpdated -= OnRunUpdated;
            _svc.OnRewardGranted -= OnRewardGranted;

            _svc = null;
        }

        private void OnStateChanged(RaceEventState oldState, RaceEventState newState)
        {
            if (_iconText != null) _iconText.text = newState.ToString();
        }

        private void OnRewardGranted(RaceReward reward)
        {
            Debug.Log($"Reward granted: coins={reward.Gold}");
        }

        private void HandlePopup(PopupRequest req)
        {
            Debug.Log("HandlePopup: " + req.Type);

            switch (req.Type)
            {
                case PopupType.Entry:
                    ShowEntry();
                    break;
                case PopupType.Searching:
                    ShowSearching(req);
                    break;
                case PopupType.Main:
                    ShowMain();
                    break;
                case PopupType.Ended:
                    ShowEnd();
                    break;
                default:
                    Debug.LogWarning("Unhandled popup type: " + req.Type);
                    break;
            }
        }

        private void OnRunUpdated(RaceRun run)
        {
            if (run == null)
            {
                // clear standings UI
                return;
            }

            // update standings UI here if needed
            // var list = RaceStandings.Compute(run.AllParticipants(), run.GoalLevels);

            // mirror logic bạn đang làm
            if (_svc.State == RaceEventState.Ended || _svc.State == RaceEventState.ExtendOffer)
            {
                _endedView.Bind(_svc);
                ShowEnd();
            }
            else if (_svc.State == RaceEventState.InRace)
            {
                _mainPopupView.Bind(_svc);
            }
        }

        // ---------------- UI actions / show hide ----------------

        public void JoinRace()
        {
            _svc.JoinRace(isInTutorial: false, localNow: DateTime.Now);
        }

        public void OnClickClaim()
        {
            _svc.Claim();
        }

        public void OnClickExtend1H()
        {
            _svc.Extend1H();
        }

        public void OnClickIconRace()
        {
            switch (_svc.State)
            {
                case RaceEventState.InRace:
                    ShowMain();
                    break;
                case RaceEventState.Searching:
                    // do nothing
                    break;
                case RaceEventState.Ended:
                case RaceEventState.ExtendOffer:
                    ShowEnd();
                    break;
                case RaceEventState.Eligible:
                    ShowEntry();
                    break;
                default:
                    Debug.Log("OnClickIconRace: not state can show: " + _svc.State);
                    break;
            }
        }

        private void ShowEntry()
        {
            _entryView.Bind(_svc);
            _entryView.gameObject.SetActive(true);

            _searchingView.gameObject.SetActive(false);
            _mainPopupView.gameObject.SetActive(false);
            _endedView.gameObject.SetActive(false);
        }

        private void ShowSearching(PopupRequest req)
        {
            _entryView.gameObject.SetActive(false);
            _searchingView.gameObject.SetActive(true);
            _mainPopupView.gameObject.SetActive(false);
            _endedView.gameObject.SetActive(false);

            _searchingView.Bind(_svc);
            _searchingView.Show(req.Searching);
        }

        private void ShowMain()
        {
            _entryView.gameObject.SetActive(false);
            _searchingView.gameObject.SetActive(false);
            _mainPopupView.gameObject.SetActive(true);
            _endedView.gameObject.SetActive(false);
        }

        private void ShowEnd()
        {
            _entryView.gameObject.SetActive(false);
            _searchingView.gameObject.SetActive(false);
            _mainPopupView.gameObject.SetActive(false);

            _endedView.Bind(_svc);
            _endedView.gameObject.SetActive(true);
        }

        private void ReplaySnapshot()
        {
            var run = _svc.CurrentRun;
            if (run == null) return;

            if (_svc.State == RaceEventState.Ended || _svc.State == RaceEventState.ExtendOffer)
                _endedView.Bind(_svc);
            else if (_svc.State == RaceEventState.InRace)
                _mainPopupView.Bind(_svc);
        }
    }
}

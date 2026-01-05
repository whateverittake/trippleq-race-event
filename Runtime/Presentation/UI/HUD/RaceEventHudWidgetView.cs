#nullable enable
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceEventHudWidgetView : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private Button _button = null!;
        [SerializeField] private GameObject _root = null!;
        [SerializeField] private GameObject _zzzBubble = null!;
        [SerializeField] private Image _icon = null!;
        [SerializeField] private TMP_Text _label = null!;
        [SerializeField] private TMP_Text _countdown = null!;
        [SerializeField] private GameObject _claimBang = null!; // optional: "!" or shake

        private RaceEventService? _svc;
        private Func<bool>? _isInTutorial; // optional hook
        private float _accum;

        public void Bind(RaceEventService svc, Func<bool>? isInTutorialGetter = null)
        {
            _svc = svc;
            _isInTutorial = isInTutorialGetter;
            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(OnClick);
            RefreshNow();
        }

        private void Update()
        {
            if (_svc == null) return;

            // refresh every 1s
            _accum += Time.deltaTime;
            if (_accum < 1f) return;
            _accum = 0f;

            RefreshNow();
        }

        private void RefreshNow()
        {
            if (_svc == null) return;

            var localNow = DateTime.Now;
            var status = _svc.GetHudStatus(localNow);

            _root.SetActive(status.IsVisible);
            if (!status.IsVisible) return;

            _zzzBubble.SetActive(status.IsSleeping);
            _claimBang.SetActive(status.HasClaim);
            _label.text = status.Label;

            _countdown.gameObject.SetActive(status.ShowTextCountdown);
            if (status.ShowTextCountdown)
            {
               
                _countdown.text = FormatHMS(status.Remaining);
            }
        }

        private void OnClick()
        {
            if (_svc == null) return;

            var action = _svc.GetHudClickAction(
                isInTutorial: _isInTutorial?.Invoke() ?? false,
                localNow: DateTime.Now
            );

            switch (action)
            {
                case RaceHudClickAction.OpenEnded:
                    _svc.RequestEndedPopup();
                    break;

                case RaceHudClickAction.OpenInRace:
                    _svc.RequestInRacePopup();
                    break;

                case RaceHudClickAction.OpenEntry:
                    _svc.RequestEntryPopup(
                        _isInTutorial?.Invoke() ?? false,
                        DateTime.Now
                    );
                    break;

                case RaceHudClickAction.None:
                default:
                    Debug.Log("Not handle on click: " + action);
                    // sleeping / locked
                    // optional: play locked sound / shake / tooltip
                    break;
            }
        }

        private static string FormatHMS(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            int h = (int)t.TotalHours;
            int m = t.Minutes;
            int s = t.Seconds;
            return $"{h:00}:{m:00}:{s:00}";
        }
    }
}

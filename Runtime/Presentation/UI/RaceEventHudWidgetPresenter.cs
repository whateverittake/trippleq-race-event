using System;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed class RaceEventHudWidgetPresenter
    {
        private readonly RaceEventService _svc;
        private readonly RaceEventHudWidgetView _view;
        private readonly Func<bool> _isInTutorial;

        private float _accum;

        public RaceEventHudWidgetPresenter(RaceEventService svc, RaceEventHudWidgetView view, Func<bool>? isInTutorial = null)
        {
            _svc = svc;
            _view = view;
            _isInTutorial = isInTutorial ?? (() => false);

            _view.SetOnClick(OnClick);
            RefreshNow();
        }

        public void Tick(float dt)
        {
            _accum += dt;
            if (_accum < 1f) return;
            _accum = 0f;
            RefreshNow();
        }

        public void RefreshNow()
        {
            var localNow = DateTime.Now;
            var s = _svc.GetHudStatus(localNow);

            // map từ svc HudStatus -> view RaceHudStatus
            var status = new RaceHudStatus(
                s.IsVisible,
                s.IsSleeping,
                s.HasClaim,
                s.Remaining,
                s.Label,
                s.ShowTextCountdown
            );
            _view.SetStatus(status);
        }

        private void OnClick()
        {
            if (_isInTutorial()) return;

            var action = _svc.GetHudClickAction(_isInTutorial(),DateTime.Now);

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

        internal void Dispose()
        {
            _view.SetOnClick(null);
        }
    }
}

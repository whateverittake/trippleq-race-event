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

        public event Action OnClickLocked;
        public event Action OnClickTimeGap;

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
            var s = _svc.BuildHudStatus(localNow);

            // map từ svc HudStatus -> view RaceHudStatus
            var status = new RaceHudStatus(
                s.IsVisible,
                s.IsSleeping,
                s.HasClaim,
                s.Remaining,
                s.Label,
                s.ShowTextCountdown
            );
            _view.SetStatus(status, _svc.FormatHM(s.Remaining));
        }

        private void OnClick()
        {
            if (_isInTutorial()) return;

            var now = DateTime.Now;

            var action = _svc.BuildHudClickAction(now);

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
                        DateTime.Now
                    );
                    break;
                case RaceHudClickAction.OpenEntryNextRound:
                    _svc.ForceRequestEntryPopup(DateTime.Now);
                    break;
                case RaceHudClickAction.None:
                    var hud = _svc.BuildHudStatus(now);

                    if (hud.IsLocked)
                    {
                        OnClickLocked?.Invoke();
                    }
                    else if (hud.IsSleeping)
                    {
                        OnClickTimeGap?.Invoke();
                    }

                    break;
                default:
                    Debug.Log("Not handle on click: " + action);
                    // sleeping / locked
                    // optional: play locked sound / shake / tooltip
                    break;
            }
        }

        public void Dispose()
        {
            _view.SetOnClick(null);
        }
    }
}

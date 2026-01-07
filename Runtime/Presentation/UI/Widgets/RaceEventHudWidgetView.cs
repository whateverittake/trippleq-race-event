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

        private Action? _onClick;

        private void Awake()
        {
            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(() => _onClick?.Invoke());
        }

        public void SetOnClick(Action? onClick) => _onClick = onClick;

        public void SetStatus(RaceHudStatus status)
        {
            _root.SetActive(status.IsVisible);
            if (!status.IsVisible) return;

            _zzzBubble.SetActive(status.IsSleeping);
            _claimBang.SetActive(status.HasClaim);
            _label.text = status.Label;

            _countdown.gameObject.SetActive(status.ShowTextCountdown);
            _countdown.text = FormatHMS(status.Remaining);
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

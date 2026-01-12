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
        [SerializeField] private GameObject _claimBang = null!; // optional: "!" or shake

        [SerializeField] GameObject _activeObj, _sleepingObj;

        private Action? _onClick;

        private void Awake()
        {
            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(() => _onClick?.Invoke());
        }

        public void SetOnClick(Action? onClick) => _onClick = onClick;

        public void SetStatus(RaceHudStatus status, string timeRemaining)
        {
            _root.SetActive(status.IsVisible);
            if (!status.IsVisible) return;

            _zzzBubble.SetActive(status.IsSleeping);
            _claimBang.SetActive(status.HasClaim);

            _activeObj.SetActive(!status.IsSleeping);
            _sleepingObj.SetActive(status.IsSleeping);

            string suffix = string.Empty;
           
            if (status.ShowTextCountdown)
            {
                suffix = timeRemaining;
            }

            _label.text = status.Label + suffix;
        }
    }
}

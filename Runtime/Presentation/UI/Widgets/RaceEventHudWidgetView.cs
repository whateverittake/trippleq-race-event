#nullable enable
using System;
using System.Collections;
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

        [Header("Click Anim")]
        [SerializeField] private float _pressScale = 0.92f;
        [SerializeField] private float _pressDuration = 0.06f;

        private Action? _onClick;
        private Coroutine? _scaleRoutine;

        private void Awake()
        {
            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(OnClickInternal);
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
        private void OnClickInternal()
        {
            // play anim
            if (_scaleRoutine != null)
                StopCoroutine(_scaleRoutine);

            _scaleRoutine = StartCoroutine(PressAnim());

            // forward logic click
            _onClick?.Invoke();
        }


        private IEnumerator PressAnim()
        {
            if (_root == null) yield break;

            Transform t = _root.transform;

            Vector3 original = Vector3.one;
            Vector3 pressed = Vector3.one * _pressScale;

            // scale down
            float t0 = 0f;
            while (t0 < _pressDuration)
            {
                t0 += Time.unscaledDeltaTime;
                float k = t0 / _pressDuration;
                t.localScale = Vector3.Lerp(original, pressed, k);
                yield return null;
            }

            // scale up
            t0 = 0f;
            while (t0 < _pressDuration)
            {
                t0 += Time.unscaledDeltaTime;
                float k = t0 / _pressDuration;
                t.localScale = Vector3.Lerp(pressed, original, k);
                yield return null;
            }

            t.localScale = original;
        }

    }
}

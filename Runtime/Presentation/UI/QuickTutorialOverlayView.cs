using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    /// <summary>
    /// Quick temporary tutorial overlay:
    /// - Dim 4 sides
    /// - Leave a "hole" area over a target rect
    /// - Tap the hole to advance steps
    /// - No Next/Skip buttons, no highlight border
    ///
    /// Extra:
    /// - Blinking description (pulse alpha)
    /// - Hand pointer follows hole + small bob animation
    /// </summary>
    public class QuickTutorialOverlayView : MonoBehaviour
    {
        [Header("Canvas/Root")]
        [Tooltip("Overlay root RectTransform (full-screen, same canvas as targets). Usually this object's RectTransform.")]
        [SerializeField] private RectTransform _overlayRoot;

        [Tooltip("Optional. If your canvas is ScreenSpace-Camera/WorldSpace, assign it so coordinate conversion uses worldCamera.")]
        [SerializeField] private Canvas _rootCanvas;

        [Header("Dim Rects (Images)")]
        [SerializeField] private RectTransform _dimTop;
        [SerializeField] private RectTransform _dimBottom;
        [SerializeField] private RectTransform _dimLeft;
        [SerializeField] private RectTransform _dimRight;

        [Header("Hole (clickable area)")]
        [Tooltip("RectTransform representing the hole area (usually an Image with alpha=0 and RaycastTarget ON).")]
        [SerializeField] private RectTransform _holeRect;
        [Tooltip("Button on the hole area. Tap to advance step.")]
        [SerializeField] private Button _holeButton;

        [Header("Description")]
        [SerializeField] private TMP_Text _descText;
        [SerializeField] private RectTransform _descBubbleRect; // parent của Des Text
        [SerializeField] private Vector2 _bubbleOffset = new Vector2(0, 24f);
        [SerializeField] private float _bubbleMargin = 16f;

        [Header("Hand Pointer (Optional)")]
        [Tooltip("Hand rect transform (e.g., an Image of a pointing hand).")]
        [SerializeField] private RectTransform _handRect;
        [Tooltip("Offset from hole center to place the hand (pixels, overlay local).")]
        [SerializeField] private Vector2 _handOffset = new Vector2(80f, -60f);
        [Tooltip("Hand bob distance (pixels).")]
        [SerializeField] private float _handBobAmplitude = 10f;
        [Tooltip("Hand bob speed.")]
        [SerializeField] private float _handBobSpeed = 4f;
        [Tooltip("If true, show hand; otherwise ignore.")]
        [SerializeField] private bool _showHand = true;

        [Header("Text Blink (Optional)")]
        [Tooltip("If true, blink/pulse the description bubble.")]
        [SerializeField] private bool _blinkText = true;
        [Tooltip("Blink speed (higher = faster).")]
        [SerializeField] private float _blinkSpeed = 2.5f;
        [Tooltip("Min alpha for blink.")]
        [Range(0f, 1f)]
        [SerializeField] private float _blinkMinAlpha = 0.55f;

        [Header("Options")]
        [Tooltip("Padding around the target hole in pixels.")]
        [SerializeField] private Vector2 _holePadding = new Vector2(12f, 12f);

        [Tooltip("If true, keep syncing hole position each LateUpdate (recommended when layout groups animate/settle).")]
        [SerializeField] private bool _continuousSync = true;

        private RectTransform[] _targets;
        private string[] _texts;
        private int _index;
        private bool _playing;

        private float _blinkT;
        private float _handT;
        private Vector2 _lastHoleCenter; // overlay local

        // Use correct UI camera if needed
        private Camera UICamera
        {
            get
            {
                if (_rootCanvas == null) return null; // ScreenSpaceOverlay: OK
                if (_rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
                return _rootCanvas.worldCamera;
            }
        }

        public event Action OnFinished;

        public void Play(RectTransform[] targets, string[] texts)
        {
            if (targets == null || texts == null) return;
            int n = Mathf.Min(targets.Length, texts.Length);
            if (n <= 0) return;

            if (_overlayRoot == null) _overlayRoot = (RectTransform)transform;

            _targets = new RectTransform[n];
            _texts = new string[n];
            Array.Copy(targets, _targets, n);
            Array.Copy(texts, _texts, n);

            _index = 0;
            _playing = true;

            _blinkT = 0f;
            _handT = 0f;

            gameObject.SetActive(true);

            BindHoleClick();
            ShowValidStepOrFinish(startIndex: 0);

            // ensure visuals reset
            SetBubbleAlpha(1f);
            if (_handRect != null) _handRect.gameObject.SetActive(_showHand);
        }

        public void Stop()
        {
            if (!_playing)
            {
                gameObject.SetActive(false);
                return;
            }

            _playing = false;
            UnbindHoleClick();

            gameObject.SetActive(false);
            OnFinished?.Invoke();
        }

        private void BindHoleClick()
        {
            if (_holeButton == null) return;
            _holeButton.onClick.RemoveListener(OnHoleClicked);
            _holeButton.onClick.AddListener(OnHoleClicked);
        }

        private void UnbindHoleClick()
        {
            if (_holeButton == null) return;
            _holeButton.onClick.RemoveListener(OnHoleClicked);
        }

        private void OnHoleClicked()
        {
            if (!_playing) return;

            int next = _index + 1;
            if (next >= _targets.Length)
            {
                Stop();
                return;
            }

            ShowValidStepOrFinish(next);
        }

        private void ShowValidStepOrFinish(int startIndex)
        {
            if (!_playing) return;

            _index = Mathf.Clamp(startIndex, 0, _targets.Length);

            // skip null/inactive targets
            while (_index < _targets.Length)
            {
                var t = _targets[_index];
                if (t != null && t.gameObject.activeInHierarchy)
                {
                    ShowStep(_index);
                    return;
                }
                _index++;
            }

            Stop();
        }

        private void ShowStep(int i)
        {
            if (!_playing) return;

            if (_descText != null)
                _descText.text = _texts[i] ?? string.Empty;

            // reset blink phase for each step (optional)
            _blinkT = 0f;

            SyncHoleAndDim(_targets[i]);
        }

        private void LateUpdate()
        {
            if (!_playing) return;

            // keep syncing hole (layout might settle)
            if (_continuousSync)
            {
                if (_index >= 0 && _index < _targets.Length)
                {
                    var t = _targets[_index];
                    if (t != null && t.gameObject.activeInHierarchy)
                        SyncHoleAndDim(t);
                }
            }

            // text blink
            if (_blinkText)
            {
                _blinkT += Time.unscaledDeltaTime * Mathf.Max(0.01f, _blinkSpeed);
                float s = (Mathf.Sin(_blinkT * Mathf.PI * 2f) + 1f) * 0.5f; // 0..1
                float a = Mathf.Lerp(_blinkMinAlpha, 1f, s);
                SetBubbleAlpha(a);
            }
            else
            {
                SetBubbleAlpha(1f);
            }

            // hand bob
            if (_showHand && _handRect != null && _handRect.gameObject.activeSelf)
            {
                _handT += Time.unscaledDeltaTime * Mathf.Max(0.01f, _handBobSpeed);
                float bob = Mathf.Sin(_handT * Mathf.PI * 2f) * _handBobAmplitude;

                // place hand near hole center + offset, then bob along Y (you can change to X if you prefer)
                _handRect.anchoredPosition = _lastHoleCenter + _handOffset + new Vector2(0f, bob);
            }
        }

        private void OnDisable()
        {
            // Ensure cleanup if popup closes suddenly
            if (_playing)
            {
                _playing = false;
                UnbindHoleClick();
            }
        }

        private void SyncHoleAndDim(RectTransform target)
        {
            if (_overlayRoot == null || _holeRect == null) return;
            if (_dimTop == null || _dimBottom == null || _dimLeft == null || _dimRight == null) return;
            if (target == null) return;

            // Get target world corners
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);

            // Convert to local points in overlayRoot (use correct camera if needed)
            Vector2 sp0 = RectTransformUtility.WorldToScreenPoint(UICamera, corners[0]); // bottom-left
            Vector2 sp2 = RectTransformUtility.WorldToScreenPoint(UICamera, corners[2]); // top-right

            RectTransformUtility.ScreenPointToLocalPointInRectangle(_overlayRoot, sp0, UICamera, out var lp0);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_overlayRoot, sp2, UICamera, out var lp2);

            Vector2 min = Vector2.Min(lp0, lp2);
            Vector2 max = Vector2.Max(lp0, lp2);

            // padding
            min -= _holePadding;
            max += _holePadding;

            // Clamp hole inside overlay rect to avoid negative dims if target is near edges
            float halfW = _overlayRoot.rect.width * 0.5f;
            float halfH = _overlayRoot.rect.height * 0.5f;

            float minX = Mathf.Clamp(min.x, -halfW, halfW);
            float maxX = Mathf.Clamp(max.x, -halfW, halfW);
            float minY = Mathf.Clamp(min.y, -halfH, halfH);
            float maxY = Mathf.Clamp(max.y, -halfH, halfH);

            min = new Vector2(minX, minY);
            max = new Vector2(maxX, maxY);

            Vector2 center = (min + max) * 0.5f;
            Vector2 size = max - min;

            // save hole center for hand follow
            _lastHoleCenter = center;

            // update bubble position (before or after hole ok)
            PositionBubble(center, min, max);

            // Hole rect
            //_holeRect.anchoredPosition = center;
            //_holeRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(0, size.x));
            //_holeRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(0, size.y));

            // Dim rects around hole
            float w = _overlayRoot.rect.width;

            // Top: from hole top to screen top
            SetDim(_dimTop,
                pos: new Vector2(0f, (halfH + max.y) * 0.5f),
                size: new Vector2(w, halfH - max.y));

            // Bottom: from screen bottom to hole bottom
            SetDim(_dimBottom,
                pos: new Vector2(0f, (min.y - halfH) * 0.5f),
                size: new Vector2(w, halfH + min.y));

            // Left: left side beside hole
            SetDim(_dimLeft,
                pos: new Vector2((min.x - halfW) * 0.5f, center.y),
                size: new Vector2(halfW + min.x, size.y));

            // Right: right side beside hole
            SetDim(_dimRight,
                pos: new Vector2((halfW + max.x) * 0.5f, center.y),
                size: new Vector2(halfW - max.x, size.y));

            // ensure hand visibility
            if (_handRect != null)
                _handRect.gameObject.SetActive(_showHand);
        }

        private void PositionBubble(Vector2 holeCenter, Vector2 holeMin, Vector2 holeMax)
        {
            if (_descBubbleRect == null) return;

            // Ensure layout is updated so rect.size is correct
            Canvas.ForceUpdateCanvases();
            var bubbleSize = _descBubbleRect.rect.size;

            float halfW = _overlayRoot.rect.width * 0.5f;
            float halfH = _overlayRoot.rect.height * 0.5f;

            // try place below hole
            Vector2 pos = new Vector2(holeCenter.x, holeMin.y - _bubbleOffset.y - bubbleSize.y * 0.5f);

            // if overflow bottom -> place above
            float bubbleBottom = pos.y - bubbleSize.y * 0.5f;
            if (bubbleBottom < -halfH + _bubbleMargin)
                pos.y = holeMax.y + _bubbleOffset.y + bubbleSize.y * 0.5f;

            // clamp left/right
            float left = pos.x - bubbleSize.x * 0.5f;
            float right = pos.x + bubbleSize.x * 0.5f;

            if (left < -halfW + _bubbleMargin) pos.x += (-halfW + _bubbleMargin) - left;
            if (right > halfW - _bubbleMargin) pos.x -= right - (halfW - _bubbleMargin);

            _descBubbleRect.anchoredPosition = pos;
        }

        private void SetBubbleAlpha(float a)
        {
            // Option 1: fade whole bubble via CanvasGroup (if you have one)
            if (_descBubbleRect != null)
            {
                var cg = _descBubbleRect.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.alpha = a;
                    return;
                }
            }

            // Option 2: fade text only
            if (_descText != null)
            {
                var c = _descText.color;
                c.a = a;
                _descText.color = c;
            }
        }

        private static void SetDim(RectTransform rt, Vector2 pos, Vector2 size)
        {
            if (rt == null) return;

            rt.anchoredPosition = pos;
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(0f, size.x));
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(0f, size.y));
        }
    }
}

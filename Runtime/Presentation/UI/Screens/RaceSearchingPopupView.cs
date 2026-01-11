using System;
using System.Collections;
using System.Collections.Generic;
using TrippleQ.AvatarSystem;
using UnityEngine;
using static TrippleQ.Event.RaceEvent.Runtime.PopupTypes;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceSearchingPopupView : MonoBehaviour, IRaceSearchingPopupView
    {
        [SerializeField] AvatarItemView _userAvatar;
        [SerializeField] AvatarItemView[] _opponentAvatars;

        private const float LAST_REVEAL_EARLY = 0.25f; // 250ms

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
            int n = _opponentAvatars.Length; // bạn nói là 4
            if (n <= 0) yield break;

            // 1) Start: all Searching
            for (int i = 0; i < n; i++)
                _opponentAvatars[i].SetSearching();

            // 2) Build timeline: list of (time, indices-to-reveal)
            var plan = BuildRevealPlan(duration, n);

            float start = Time.time;
            float elapsed = 0f;

            // 3) Execute
            foreach (var step in plan)
            {
                // wait until step.time
                while (elapsed < step.time)
                {
                    elapsed = Time.time - start;
                    yield return null;
                }

                // reveal one or multiple indices at this moment
                for (int k = 0; k < step.indices.Length; k++)
                {
                    int idx = step.indices[k];
                    if (idx >= 0 && idx < n)
                        _opponentAvatars[idx].SetFound();
                }
            }

            // 4) Safety: ensure all found by the end (nếu duration ngắn / skip frame)
            for (int i = 0; i < n; i++)
                _opponentAvatars[i].SetFound();

            _co = null;
            yield return new WaitForSeconds(0.12f);
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

        public void RenderUserAvatar()
        {
            _userAvatar.Refresh();
        }

        #region ANIM SEARCHING OPPONENTS
        private enum RevealScenario
        {
            LeftToRight,
            RightToLeft,
            RandomOrderEven,
            RandomOrderFrontLoaded,
            RandomOrderBackLoaded,
            TwoThenTwo,
            OneTwoOne,          // 1 -> (2 cùng lúc) -> 1
            SuspenseLastSecond, // 3 cái ra khá sớm, cái cuối sát giờ
            Count
        }

        private RevealScenario _lastScenario = (RevealScenario)(-1);

        // ======= Timeline builder =======

        private struct RevealStep
        {
            public float time;        // seconds since start
            public int[] indices;     // which avatars become Found
        }

        private List<RevealStep> BuildRevealPlan(float duration, int n)
        {
            // tránh reveal ngay lập tức: cho 1 chút "đang quét"
            float minDelay = Mathf.Clamp(duration * 0.10f, 0.15f, 0.40f);
            float endBuffer = Mathf.Clamp(duration * 0.12f, 0.20f, 0.60f);

            float usable = Mathf.Max(
                0.01f,
                duration - minDelay - endBuffer - LAST_REVEAL_EARLY
            );

            // pick scenario (avoid repeating last)
            RevealScenario sc = (RevealScenario)UnityEngine.Random.Range(0, (int)RevealScenario.Count);
            if (sc == _lastScenario)
                sc = (RevealScenario)(((int)sc + UnityEngine.Random.Range(1, 3)) % (int)RevealScenario.Count);
            _lastScenario = sc;

            // order of indices
            int[] order = BuildOrder(sc, n);

            // times
            // base: 4 reveal moments trong [minDelay .. minDelay+usable], tùy “nhịp”
            float[] t = BuildTimes(sc, minDelay, usable, n);

            // pack steps (có scenario cho reveal đôi)
            var steps = new List<RevealStep>(n);

            switch (sc)
            {
                case RevealScenario.TwoThenTwo:
                    // 2 cái cùng lúc, rồi 2 cái cùng lúc
                    steps.Add(new RevealStep { time = t[0], indices = new[] { order[0], order[1] } });
                    steps.Add(new RevealStep { time = t[2], indices = new[] { order[2], order[3] } });
                    break;
                case RevealScenario.OneTwoOne:
                    // 1 -> 2 cùng lúc -> 1
                    steps.Add(new RevealStep { time = t[0], indices = new[] { order[0] } });
                    steps.Add(new RevealStep { time = t[1], indices = new[] { order[1], order[2] } });
                    steps.Add(new RevealStep { time = t[3], indices = new[] { order[3] } });
                    break;
                default:
                    // bình thường: từng cái một
                    for (int i = 0; i < n; i++)
                        steps.Add(new RevealStep { time = t[i], indices = new[] { order[i] } });
                    break;
            }

            // đảm bảo time tăng dần (phòng trường hợp jitter)
            steps.Sort((a, b) => a.time.CompareTo(b.time));
            return steps;
        }

        private int[] BuildOrder(RevealScenario sc, int n)
        {
            int[] order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;

            switch (sc)
            {
                case RevealScenario.LeftToRight:
                case RevealScenario.TwoThenTwo:
                case RevealScenario.OneTwoOne:
                case RevealScenario.SuspenseLastSecond:
                    // giữ nguyên
                    return order;

                case RevealScenario.RightToLeft:
                    System.Array.Reverse(order);
                    return order;

                case RevealScenario.RandomOrderEven:
                case RevealScenario.RandomOrderFrontLoaded:
                case RevealScenario.RandomOrderBackLoaded:
                    Shuffle(order);
                    return order;

                default:
                    Shuffle(order);
                    return order;
            }
        }
        private float[] BuildTimes(RevealScenario sc, float minDelay, float usable, int n)
        {
            // base evenly spaced
            float[] t = new float[n];
            for (int i = 0; i < n; i++)
                t[i] = minDelay + usable * (i + 1) / (n + 1);

            // jitter giúp “tự nhiên”, nhưng vẫn giữ tăng dần
            float jitter = Mathf.Clamp(usable * 0.08f, 0.03f, 0.18f);
            switch (sc)
            {
                case RevealScenario.RandomOrderEven:
                case RevealScenario.LeftToRight:
                case RevealScenario.RightToLeft:
                    // đều, chỉ jitter nhẹ
                    for (int i = 0; i < n; i++)
                        t[i] += UnityEngine.Random.Range(-jitter, jitter);
                    break;

                case RevealScenario.RandomOrderFrontLoaded:
                    // dồn 3 cái đầu sớm, cái cuối cách ra
                    // ví dụ: 20%, 35%, 50%, 90% của usable
                    t[0] = minDelay + usable * 0.20f;
                    t[1] = minDelay + usable * 0.35f;
                    t[2] = minDelay + usable * 0.50f;
                    t[3] = minDelay + usable * 0.85f;
                    for (int i = 0; i < n; i++)
                        t[i] += UnityEngine.Random.Range(-jitter, jitter);
                    break;

                case RevealScenario.RandomOrderBackLoaded:
                    // giữ suspense đầu lâu hơn rồi ra dồn dập
                    // 15%, 60%, 78%, 92%
                    t[0] = minDelay + usable * 0.15f;
                    t[1] = minDelay + usable * 0.60f;
                    t[2] = minDelay + usable * 0.78f;
                    t[3] = minDelay + usable * 0.92f;
                    for (int i = 0; i < n; i++)
                        t[i] += UnityEngine.Random.Range(-jitter, jitter);
                    break;
                case RevealScenario.TwoThenTwo:
                    // 2 cụm: cụm 1 sớm, cụm 2 muộn
                    t[0] = minDelay + usable * 0.28f;
                    t[1] = t[0]; // not used directly (step uses t[0])
                    t[2] = minDelay + usable * 0.78f;
                    t[3] = t[2]; // not used directly
                    t[0] += UnityEngine.Random.Range(-jitter, jitter);
                    t[2] += UnityEngine.Random.Range(-jitter, jitter);
                    break;

                case RevealScenario.OneTwoOne:
                    // 1 -> (2 cùng lúc) -> 1
                    t[0] = minDelay + usable * 0.22f;
                    t[1] = minDelay + usable * 0.55f; // step 2 uses t[1]
                    t[2] = t[1];                      // not used directly
                    t[3] = minDelay + usable * 0.85f;
                    t[0] += UnityEngine.Random.Range(-jitter, jitter);
                    t[1] += UnityEngine.Random.Range(-jitter, jitter);
                    t[3] += UnityEngine.Random.Range(-jitter, jitter);
                    break;
                case RevealScenario.SuspenseLastSecond:
                    // 3 cái ra “khá ổn”, cái cuối sát end
                    t[0] = minDelay + usable * 0.25f;
                    t[1] = minDelay + usable * 0.45f;
                    t[2] = minDelay + usable * 0.62f;
                    t[3] = minDelay + usable * 0.85f; // sát end buffer
                    for (int i = 0; i < n; i++)
                        t[i] += UnityEngine.Random.Range(-jitter, jitter);
                    break;
            }

            // enforce non-decreasing and clamp
            System.Array.Sort(t);
            float last = -999f;
            for (int i = 0; i < n; i++)
            {
                t[i] = Mathf.Clamp(t[i], 0f, minDelay + usable);
                if (t[i] < last + 0.02f) t[i] = last + 0.02f; // khoảng cách tối thiểu 20ms
                last = t[i];
            }
            return t;
        }

        private void Shuffle(int[] a)
        {
            for (int i = a.Length - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (a[i], a[j]) = (a[j], a[i]);
            }
        }
        #endregion
    }
}


#nullable enable
using System;
using System.Collections.Generic;
using static TrippleQ.Event.RaceEvent.Runtime.PopupTypes;
using static UnityEngine.UI.GridLayoutGroup;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    /// <summary>
    /// Facade/API entry for host games.
    /// - Pure C# service (no MonoBehaviour).
    /// - Orchestrates: StateMachine + Simulation + Persistence + Providers.
    /// - UI listens to events emitted by this service.
    /// </summary>
    public sealed partial class RaceEventService : IDisposable
    {
        // ---- Events for UI/Bootstrap ----
        public event Action<string>? OnLog;
        public event Action<RaceEventState, RaceEventState>? OnStateChanged;
        public event Action<PopupRequest>? OnPopupRequested;
        public event Action<RaceRun?>? OnRunUpdated;
        public event Action<RaceReward>? OnRewardGranted;
        public event Action<Action<bool>>? OnExtendAdsRequested;

        // ---- Core ----
        private readonly RaceEventStateMachine _sm = new RaceEventStateMachine();

        private bool _initialized;
        private bool _disposed;

        private RaceFlow _flow;
        public RaceFlow Flow => _flow;

        // ---- Config / Save (minimal) ----
        private IReadOnlyList<RaceEventConfig> _configs;

        private RaceEventSave _save;

        private IRaceStorage _storage = null!;

        private RaceRun? _run;

        private BotPoolJson _botPool = new BotPoolJson();

        private float _tickAccum;
        private const float TickIntervalSeconds = 1f;
        private long _lastSimulatedUtc; // để tránh simulate trùng giây

        public bool IsInitialized => _initialized;
        public RaceEventState State => _sm.State;

        public RaceRun? CurrentRun => _run;
        public bool HasRun => _run != null;

        private SearchingPlan _currentSearchingPlan;

        public event Action<PopupType>? OnTutorialRequested;

        /// <summary>
        /// Temporary level mirror (host passes values in).
        /// Later: we'll read from ProgressProvider.
        /// </summary>
        public int CurrentLevel { get; private set; }

        private PopupType? _currentPopupTypeOpen;

        // DEBUG ONLY: store last fakeUtc used by debug sim
        private long _debugFakeUtcSeconds;

        // --------------------
        // TEST MODE
        // --------------------
        // When enabled, all "Now" calls are driven by the device local clock
        // (DateTimeOffset.Now / DateTime.Now). This lets QA change the device
        // local time to fast-forward DailyEnd / Extend flows.
        public bool IsInTestMode { get; private set; }

        public void SetTestMode(bool enabled)
        {
            IsInTestMode = enabled;
            Log($"TestMode={(enabled ? "ON" : "OFF")}");
        }

        public RaceEventService()
        {
            _sm.OnStateChanged += (from, to) =>
            {
                OnStateChanged?.Invoke(from, to);
                Log($"State: {from} -> {to}");
            };
        }

        public void Initialize(IReadOnlyList<RaceEventConfig> configs, IRaceStorage storage, int initialLevel, bool isInTutorial, BotPoolJson botPool)
        {
            _flow = new RaceFlow(this);

            ThrowIfDisposed();
            if (_initialized) return;

            _configs = (configs != null && configs.Count > 0)
                       ? configs
                       : throw new ArgumentException("configs is null/empty", nameof(configs));

            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _botPool = botPool ?? new BotPoolJson();

            var loaded = _storage.Load();
            _save = loaded ?? RaceEventSave.Empty();
            _run = _save.CurrentRun;

            // If app quit/crash during Searching -> reset flow so next entry can re-join
            if (_save.LastFlowState == RaceEventState.Searching)
            {
                Log("Recovered from Searching interruption -> reset to Entry flow");

                // No run was created yet, just reset flow markers
                _save.LastFlowState = RaceEventState.Idle;
                _save.SearchingStartUtcSeconds = 0;

                // Allow Entry to show again in same window if you want:
                _save.LastEntryShownWindowId = 0;

                // Optionally also rollback join time so cooldown doesn't block:
                _save.LastJoinLocalUnixSeconds = 0;

                _save.CurrentRun = null;
                _run = null;

                PersistAndPublish();
            }

            CleanupExpiredRunIfNeeded(NowUtcSeconds());

            CurrentLevel = initialLevel;

            var utcNow = NowUtcSeconds();

            if (_run == null)
            {
                _sm.SetState(RaceEventState.Idle);
            }
            else if (_run.IsFinalized)
            {
                // already ended, just resume
                _sm.SetState(RaceEventState.Ended);
            }
            else if (_save.LastFlowState == RaceEventState.ExtendOffer)
            {
                // nếu lần trước đang offer extend mà user thoát app
                _sm.SetState(RaceEventState.ExtendOffer);
            }
            else if (utcNow >= _run.EndUtcSeconds)
            {
                _sm.SetState(RaceEventState.InRace);
                // time up but not finalized yet -> finalize once
                FinalizeIfTimeUp(utcNow);
            }
            else
            {
                _sm.SetState(RaceEventState.InRace);
            }


            _initialized = true;
            Log($"Initialized. CurrentLevel={CurrentLevel}");

            // Update eligibility once on init
            RefreshEligibility(isInTutorial, NowLocal());

            PublishRunUpdated();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_initialized)
                TrySave();

            OnLog = null;
            OnStateChanged= null;
            OnPopupRequested = null;
            OnRunUpdated = null;
            OnRewardGranted=null;
            OnExtendAdsRequested=null;
        }

        public void Tick(float deltaTime)
        {
            ThrowIfNotInitialized();

            if (deltaTime < 0f) deltaTime = 0f;

            _tickAccum += deltaTime;
            if (_tickAccum < TickIntervalSeconds) return;
            _tickAccum = 0f;

            SimulateBotsTick();
        }

        internal void PersistAndPublish()
        {
            TrySave();
            PublishRunUpdated();
        }

        internal void BeginSearching(DateTime localNow)
        {
            // Mark "shown once per day"
            _save.LastEntryShownWindowId = GetWindowId(localNow, ActiveConfigForRunOrCursor().ResetHourLocal);

            // Mark join time (local)
            _save.LastJoinLocalUnixSeconds = NowLocalUnixSeconds();

            // Transition
            _sm.SetState(RaceEventState.Searching);
            _save.LastFlowState = RaceEventState.Searching;
            _save.SearchingStartUtcSeconds = NowUtcSeconds();

            // Ask UI to show searching popup
            _currentSearchingPlan = new SearchingPlan(ActiveConfigForRunOrCursor().SearchingDurationSeconds);
            RequestPopup(new PopupRequest(PopupType.Searching, _currentSearchingPlan));

            Log("JoinRace accepted -> Searching");

            _save.CurrentRun = null;
            _run = null;

            PersistAndPublish();
        }

        internal void FinishSearchingAndCreateRun()
        {
            _sm.SetState(RaceEventState.InRace);
            _save.LastFlowState = RaceEventState.InRace;

            // Create run NOW
            var utcNow = NowUtcSeconds();
            var startUtc = utcNow;

            var localNow = NowLocal();
            var dailyEndLocal = GetNextResetLocal(localNow); // theo config
            var endUtc = new DateTimeOffset(dailyEndLocal).ToUnixTimeSeconds();
            //var endUtc = utcNow + (long)TimeSpan.FromHours(ActiveConfigForRunOrCursor().DurationHours).TotalSeconds;
            var cfgIndex = _save.ConfigCursor;

            var cfg = ActiveConfigForRunOrCursor();

            _run = RaceRun.CreateNew(
                runId: Guid.NewGuid().ToString("N"),
                startUtc: startUtc,
                endUtc: endUtc,
                goalLevels: cfg.GoalLevels,
                playersCount: cfg.PlayersPerRace
            );

            _run.ConfigIndex = cfgIndex;

            _run.Player = new RaceParticipant
            {
                Id = "player",
                DisplayName = "You",
                AvatarId = ExtractNumberSuffix(AvatarSystem.AvatarServiceLocator.Service.GetSelectedAvatarId().value.Value),
                LevelsCompleted = 0,
                LastUpdateUtcSeconds = utcNow,
                IsBot = false
            };

            SeedBots(_run, utcNow);

            // Persist run
            _save.CurrentRun = _run;

            // Clear searching markers
            _save.SearchingStartUtcSeconds = 0;

            PersistAndPublish();

            // Show main race screen
            RequestPopup(new PopupRequest(PopupType.Main));
            Log("Searching finished -> InRace -> Main");
        }

        internal void MarkEntryShown(DateTime localNow)
        {
            _save.LastEntryShownWindowId =
                    GetWindowId(localNow, ActiveConfigForRunOrCursor().ResetHourLocal);

            TrySave();
            Log($"Entry shown marked for windowId={_save.LastEntryShownWindowId}");
        }

        internal SearchingPlan GetSearchingSnapshot()
        {
            var total = ActiveConfigForRunOrCursor().SearchingDurationSeconds;
            var start = _save.SearchingStartUtcSeconds;

            if (start <= 0) return new SearchingPlan(total);

            var now = NowUtcSeconds();
            var elapsed = (int)Math.Max(0, now - start);
            var remaining = Math.Max(0, total - elapsed);
            return new SearchingPlan(remaining);
        }
    }
}

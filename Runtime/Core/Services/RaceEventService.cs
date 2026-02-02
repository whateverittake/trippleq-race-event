
#nullable enable
using System;
using System.Collections.Generic;
using static TrippleQ.Event.RaceEvent.Runtime.PopupTypes;
using static TrippleQ.Event.RaceEvent.Runtime.RaceEligibility;
using static TrippleQ.Event.RaceEvent.Runtime.RaceHudPresenter;

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
        private readonly HudContextToken _hudContextToken = new HudContextToken();

        private const int RoundGapMinutes = 15;
        private const int RoundHours = 8;
        private const string PlayerId = "player";
        private const string PlayerDisplayNameDefault = "You";

        // ---- Events for UI/Bootstrap ----
        public event Action<string>? OnLog;
        public event Action<RaceEventState, RaceEventState>? OnStateChanged;
        public event Action<PopupRequest>? OnPopupRequested;
        public event Action<RaceRun?>? OnRunUpdated;
        public event Action<RaceReward>? OnRewardGranted;

        // ---- Core ----
        private readonly RaceEventStateMachine _sm = new RaceEventStateMachine();
        private readonly RaceScheduler _raceScheduler= new RaceScheduler();
        private readonly RaceEngine _raceEngine = new RaceEngine();
        private readonly RaceHudPresenter _raceHudPresenter = new RaceHudPresenter();
        private readonly RaceEligibility _raceEligibility;

        private bool _initialized;
        private bool _disposed;

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

        private long NowUtcSeconds()
        {
            // Normal: strict UTC.
            // Test: use device local clock (affected by changing local time).
            return IsInTestMode
                ? DateTimeOffset.Now.ToUnixTimeSeconds()
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public DateTime NowLocal() => DateTime.Now;

        private long NowLocalUnixSeconds() => DateTimeOffset.Now.ToUnixTimeSeconds();

        public RaceEventService()
        {
            _raceEligibility = new RaceEligibility(_raceScheduler);
            _sm.OnStateChanged += (from, to) =>
            {
                OnStateChanged?.Invoke(from, to);
                Log($"State: {from} -> {to}");
            };
        }

        public void Initialize(IReadOnlyList<RaceEventConfig> configs, IRaceStorage storage, int initialLevel, bool isInTutorial, BotPoolJson botPool)
        {
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

                TrySave();
                PublishRunUpdated();
            }

            CleanupExpiredRunIfNeeded(NowLocal(), NowUtcSeconds());

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
            RefreshEligibility(NowLocal());

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
        }

        private void TrySave()
        {
            try
            {
                _storage.Save(_save);
            }
            catch (Exception e)
            {
                Log("Save failed: " + e.Message);
            }
        }

        public void Tick(float deltaTime)
        {
            ThrowIfNotInitialized();

            if (deltaTime < 0f) deltaTime = 0f;

            _tickAccum += deltaTime;
            if (_tickAccum < TickIntervalSeconds) return;
            _tickAccum = 0f;

            SimulateBotsIfChanged();
        }

        public bool CanClaim()
        {
            ThrowIfNotInitialized();
            if (_run == null) return false;
            if (State != RaceEventState.Ended) return false;
            if (!_run.IsFinalized) return false;
            if (_run.HasClaimed) return false;

            if (!_run.Player.HasFinished) return false;

            return true;
        }

        public void Claim()
        {
            ThrowIfNotInitialized();

            if (!CanClaim())
            {
                Log("Claim rejected (not claimable)");
                return;
            }

            // snapshot result at claim time
            _run!.HasClaimed = true;
            _run.ClaimedUtcSeconds = NowUtcSeconds();
            _run.ClaimedRank = _run.FinalPlayerRank;
            _run.ClaimedWinnerId = _run.WinnerId;

            // vNext strict policy: gap 15 phút chỉ bắt đầu SAU khi claim.
            // NextAllowedStart = ClaimedTime + 15m (không phải End + 15m).
            // NOTE: chỉ có gap/start-next cho Round 0/1. Sau Round 2/3 (RoundIndex>=2)
            // thì HUD phải countdown tới reset 4h sáng, KHÔNG được hiển thị 15p gap.
            _run.NextAllowedStartUtcSeconds = (_run.RoundIndex < 2)
                 ? _run.ClaimedUtcSeconds + (long)TimeSpan.FromMinutes(RoundGapMinutes).TotalSeconds
                 : 0;

            // Overflow guard: nếu claim muộn khiến gap vượt qua reset ngày -> clear run, không chain sang ngày mới.
            // (Chống overflow ngày theo spec vNext)
            var cfg = ActiveConfigForRunOrCursor();
            var localNow = NowLocal();

            var snap = _raceScheduler.EvaluateGapFromBaseUtc(
                localNow,
                cfg.ResetHourLocal,
                _run.ClaimedUtcSeconds,
                gapMinutes: RoundGapMinutes);

            // IMPORTANT: không được ClearRun trước khi grant reward, nếu không user mất reward.
            bool overflowAfterClaim = snap.IsOverflow;

            var reward = GetRewardForRank(_run.FinalPlayerRank);

            // Persist
            _save.CurrentRun = _run;
            _save.LastFlowState = RaceEventState.Ended; // keep stable

            // Advance config only if player FINISHED the goal
            if (_run!.Player.HasFinished) // hoặc _run.HasPlayerReachedGoal()
            {
                _save.ConfigCursor = ClampConfigIndex(_save.ConfigCursor + 1);
            }

            TrySave();

            // Notify host to actually grant economy items
            OnRewardGranted?.Invoke(reward);

            PublishRunUpdated();
            Log($"Claimed. Rank={_run.FinalPlayerRank}, RewardCoins={reward.Gold}");

            // Nếu overflow -> clear SAU claim/grant để không chain qua ngày mới.
            if (overflowAfterClaim)
            {
                ClearRun("Overflow after claim (gap crosses daily reset)");
                return;
            }
        }

        // --------------------
        // Host hooks
        // --------------------

        public void OnEnterInfo()
        {
            ThrowIfNotInitialized();
            Log("OnEnterInfo()");

            RequestPopup(new PopupRequest(PopupType.Info));
        }

        /// <summary>
        /// Host calls this when entering main screen/home scene.
        /// </summary>
        public void OnEnterMain(DateTime localNow)
        {
            ThrowIfNotInitialized();
            Log("OnEnterMain()");

            RefreshEligibility(localNow);

            if (ShouldAutoShowEntryOnEnterMain(localNow))
            {
                RequestPopup(new PopupRequest(PopupType.Entry));
            }

            var utcNow = NowUtcSeconds();
            if (_run != null && State == RaceEventState.InRace)
            {
                GhostBotSimulator.SimulateBots(_run, utcNow);
                _lastSimulatedUtc = utcNow;
                _save.CurrentRun = _run;
                TrySave();
                PublishRunUpdated();
            }
        }

        /// <summary>
        /// Host calls this after player wins a level.
        /// </summary>
        public void OnLevelWin(int newLevel, DateTime localNow)
        {
            ThrowIfNotInitialized();

            if (newLevel < 0) newLevel = 0;

            if (newLevel != CurrentLevel)
            {
                CurrentLevel = newLevel;
                Log($"OnLevelWin(): CurrentLevel={CurrentLevel}");
            }

            RefreshEligibility(localNow);

            var max = ActiveConfigForRunOrCursor().GoalLevels;

            if (_run != null && State == RaceEventState.InRace)
            {
                var utcNow = NowUtcSeconds();

                _run.Player.LevelsCompleted += 1;
                _run.Player.LevelsCompleted = Math.Min(_run.Player.LevelsCompleted, max);
                _run.Player.LastUpdateUtcSeconds = utcNow;

                if (!_run.Player.HasFinished && _run.Player.LevelsCompleted >= _run.GoalLevels)
                {
                    _run.Player.HasFinished = true;
                    _run.Player.FinishedUtcSeconds = utcNow;

                    Log(
                            $"[RACE][PLAYER FINISH] levels={_run.Player.LevelsCompleted}/{_run.GoalLevels} finishUtc={utcNow}"
                        );

                    EndRaceNowAndFinalize();
                    return;
                }

                GhostBotSimulator.SimulateBots(_run, utcNow);

                _save.CurrentRun = _run;
                TrySave();
                PublishRunUpdated();

                // NOTE: no early end here
                FinalizeIfTimeUp(utcNow);
            }
        }

        //Update state machine
        private void RefreshEligibility(DateTime localNow)
        {
            // If currently searching/in race, don't downgrade to Eligible/Idle
            if (State == RaceEventState.Searching ||
                State == RaceEventState.InRace ||
                State == RaceEventState.Ended)
                return;

            // vNext: luôn giữ Idle. UI/HUD dùng ctx.IsEligible để hiển thị Entry / Sleeping.
            _sm.SetState(RaceEventState.Idle);
        }

        // --------------------
        // Join flow
        // --------------------

        /// <summary>
        /// Called by Entry popup when player taps "Play/Join".
        /// </summary>
        public void JoinRace(DateTime localNow)
        {
            ThrowIfNotInitialized();

            if (!IsEligibleForEntry(localNow))
            {
                Log("JoinRace rejected (not eligible)");
                return;
            }

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
            TrySave();

            PublishRunUpdated();
        }

        public void TryJoinOrStart(DateTime localNow)
        {
            ThrowIfNotInitialized();

            // 1) If already has a run:
            if (_run != null)
            {
                switch (State)
                {
                    case RaceEventState.InRace:
                        // treat as "open main"
                        RequestInRacePopup();
                        return;

                    case RaceEventState.Searching:
                        // searching in progress -> ignore
                        Log("TryJoinOrStart ignored (already searching)");
                        return;

                    case RaceEventState.Ended:
                        // strict next round gate
                        if (CanStartNextRoundNow(localNow))
                        {
                            StartNextRound(localNow);
                        }
                        else
                        {
                            // not ready -> show ended screen (claim / next-in)
                            RequestEndedPopup();
                            Log("TryJoinOrStart rejected (ended but not ready for next round)");
                        }
                        return;

                    case RaceEventState.Idle:
                    default:
                        // run != null but idle is weird, still route to open ended/inrace based on run flags
                        if (_run.IsFinalized) RequestEndedPopup();
                        else RequestInRacePopup();
                        return;
                }
            }

            // 2) No run -> this is Round 0 join flow
            if (State != RaceEventState.Idle)
            {
                Log($"TryJoinOrStart rejected (State={State}, no run but not idle)");
                return;
            }

            JoinRace(localNow); // existing method (eligibility check inside)
        }

        /// <summary>
        /// Called by UI if player closes entry popup without joining.
        /// Still counts as "shown once/day" in this step (common LiveOps behavior).
        /// </summary>
        public void MarkEntryShown(DateTime localNow)
        {
            ThrowIfNotInitialized();
            var cfg = ActiveConfigForRunOrCursor();
            _save.LastEntryShownWindowId = _raceScheduler.ComputeWindowId(localNow, cfg.ResetHourLocal);
            TrySave();
            Log($"Entry shown marked for windowId={_save.LastEntryShownWindowId}");
        }

        public void ConfirmSearchingFinished()
        {
            ThrowIfNotInitialized();

            if (State != RaceEventState.Searching)
            {
                Log($"ConfirmSearchingFinished ignored (State={State})");
                return;
            }

            if (_run != null)
            {
                // Safety: prevent overwriting an existing run
                Log("ConfirmSearchingFinished rejected (_run already exists)");
                _sm.SetState(RaceEventState.InRace);
                _save.LastFlowState = RaceEventState.InRace;
                RequestInRacePopup();
                return;
            }

            _sm.SetState(RaceEventState.InRace);
            _save.LastFlowState = RaceEventState.InRace;

            // Create run NOW
            var cfg = ActiveConfigForRunOrCursor();
            var utcNow = NowUtcSeconds();
            var localNow = NowLocal();

            int windowId = _raceScheduler.ComputeWindowId(localNow, cfg.ResetHourLocal);
            var nextResetLocal = _raceScheduler.ComputeNextResetLocal(localNow, cfg.ResetHourLocal);
            long dayResetUtc = new DateTimeOffset(nextResetLocal).ToUnixTimeSeconds();

            var startUtc = utcNow;
            long endUtc = utcNow + (long)TimeSpan.FromHours(RoundHours).TotalSeconds; // Round0 fixed 8h

            _run = RaceRun.CreateNew(Guid.NewGuid().ToString("N"), startUtc, endUtc, cfg.GoalLevels, cfg.PlayersPerRace);
            _run.ConfigIndex = _save.ConfigCursor;

            _run.WindowId = windowId;
            _run.RoundIndex = 0;
            _run.DayResetUtcSeconds = dayResetUtc;

            _run.NextAllowedStartUtcSeconds = 0;

            _run.Player = new RaceParticipant
            {
                Id = PlayerId,
                DisplayName = PlayerDisplayNameDefault,
                AvatarId = ExtractNumberSuffix(AvatarSystem.AvatarServiceLocator.Service.GetSelectedAvatarId().value.Value),
                LevelsCompleted = 0,
                LastUpdateUtcSeconds = utcNow,
                IsBot = false
            };

            _raceEngine.EnsureBotsSeeded(_run, utcNow,CurrentLevel, ActiveConfigForRunOrCursor(), _botPool, out string logString);
            if(logString!=String.Empty)
            {
                Log(logString);
            }

            // Persist run
            _save.CurrentRun = _run;

            // Clear searching markers
            _save.SearchingStartUtcSeconds = 0;

            TrySave();
            PublishRunUpdated();

            // Show main race screen
            RequestPopup(new PopupRequest(PopupType.Main));
            Log("Searching finished -> InRace -> Main");
        }

        public bool CanStartNextRoundNow(DateTime localNow)
        {
            if (_run == null) return false; // chưa có run -> join bình thường
            if (State != RaceEventState.Ended) return false;

            if (!_run.HasClaimed) return false;

            var utcNow = NowUtcSeconds();
            if (_run.NextAllowedStartUtcSeconds > 0 && utcNow < _run.NextAllowedStartUtcSeconds)
                return false;

            return CanStartNextRound(localNow);
        }

        public void RequestPopup(PopupType type)
        {
            RequestPopup(new PopupRequest(type));
        }

        private void RequestPopup(PopupRequest type) => OnPopupRequested?.Invoke(type);

        // --------------------
        // Helpers
        // --------------------
        private void Log(string msg) => OnLog?.Invoke(msg);
        private void ThrowIfNotInitialized()
        {
            ThrowIfDisposed();
            if (!_initialized) throw new InvalidOperationException("RaceEventService not initialized. Call Initialize(initialLevel).");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(RaceEventService));
        }

        private void PublishRunUpdated()
        {
            OnRunUpdated?.Invoke(_run);
        }

        private void CleanupExpiredRunIfNeeded(DateTime localNow, long utcNow)
        {
            if (_run == null) return;

            // vNext: window boundary guard (chống giữ run qua ngày/reset)
            // Ưu tiên check windowId trước khi làm các logic khác để tránh finalize/extend sai ngày.
            var cfg = ActiveConfigForRunOrCursor();
            int windowNow = _raceScheduler.ComputeWindowId(localNow, cfg.ResetHourLocal);
            if (_run.WindowId != 0 && windowNow != _run.WindowId)
            {
                ClearRun("Window changed");
                return;
            }

            // data invalid -> clear ngay
            if (_run.PlayersCount <= 0 || _run.GoalLevels <= 0)
            {
                ClearRun("Invalid run data");
                return;
            }

            // If time is up: DO NOT clear.
            // - If NOT finished => restart same round (no claim, no reward).
            // - If finished => finalize -> Ended/Claimable.
            if (utcNow >= _run.EndUtcSeconds)
            {
                if (!_run.IsFinalized)
                {
                    FinalizeIfTimeUp(utcNow);// may restart or finalize depending on HasFinished
                }
                return;
            }

            // vNext (3 rounds/day): Sau khi claim round cuối (RoundIndex>=2), KHÔNG được clear sớm.
            // Lý do: nếu clear sớm thì HUD rơi về Idle và (IsEligible==true) sẽ hiện "Race Event"/Entry,
            // trong khi đúng UX là phải countdown tới reset 4h sáng.
            // -> Chỉ clear khi qua reset (window changed đã handle) hoặc khi host muốn force cleanup.
            if (_run.IsFinalized && _run.HasClaimed && _run.RoundIndex >= 2)
            {
                // Nếu có DayResetUtcSeconds thì dùng để clear đúng mốc; nếu không thì để window guard xử lý.
                if (_run.DayResetUtcSeconds > 0 && utcNow >= _run.DayResetUtcSeconds)
                {
                    ClearRun("Day reset reached");
                }
                return;
            }

            // Optional: if already claimed (non-final round) and too old -> clear
            // (giữ nguyên policy cũ nếu bạn muốn cleanup snapshot sớm cho round 0/1)
            if (_run.IsFinalized && _run.HasClaimed && _run.RoundIndex < 2)
            {
                var keepSeconds = (long)TimeSpan.FromHours(ActiveConfigForRunOrCursor().KeepClaimedHours).TotalSeconds;
                if (keepSeconds > 0 && _run.ClaimedUtcSeconds > 0 && utcNow >= _run.ClaimedUtcSeconds + keepSeconds)
                {
                    ClearRun("Claimed run expired");
                }
            }
        }

        // --------------------
        // vNext: Next Round (strict: claim -> gap -> user start)
        // --------------------

        /// <summary>
        /// Chỉ dùng cho vNext 3 rounds/day:
        /// - Phải Ended + HasClaimed
        /// - Đã qua gap (Claimed + 15m)
        /// - Không overflow ngày
        /// - RoundIndex < 2
        /// </summary>
        public bool CanStartNextRound(DateTime localNow)
        {
            ThrowIfNotInitialized();
            if (_run == null) return false;

            // Must be current window (chống giữ run cũ qua ngày)
            var cfg = ActiveConfigForRunOrCursor();
            int windowNow = _raceScheduler.ComputeWindowId(localNow, cfg.ResetHourLocal);
            if (_run.WindowId != 0 && windowNow != _run.WindowId) return false;

            if (_run.RoundIndex >= 2) return false;
            if (!_run.IsFinalized) return false;
            if (!_run.HasClaimed) return false; // strict policy

            // Gap gate: based on Claim time
            long utcNow = NowUtcSeconds();
            if (_run.NextAllowedStartUtcSeconds <= 0) return false;
            if (utcNow < _run.NextAllowedStartUtcSeconds) return false;

            // Overflow gate: claim+gap không được vượt qua next reset local
            // (re-check để an toàn khi QA đổi giờ local trong TestMode)
            var snap = _raceScheduler.EvaluateGapFromBaseUtc(
                localNow,
                cfg.ResetHourLocal,
                _run.ClaimedUtcSeconds,
                gapMinutes: RoundGapMinutes);

            if (snap.IsOverflow) return false;

            return true;
        }

        /// <summary>
        /// Start round kế tiếp theo policy:
        /// Ended -> Claim -> Gap -> (User Start Next Round)
        ///
        /// Implementation tối giản: tạo RaceRun mới cho round mới (đỡ reset state/bot/progress phức tạp).
        /// </summary>
        public void StartNextRound(DateTime localNow)
        {
            ThrowIfNotInitialized();

            if (!CanStartNextRound(localNow))
            {
                Log("StartNextRound rejected (not ready)");
                return;
            }

            var cfg = ActiveConfigForRunOrCursor();
            long utcNow = NowUtcSeconds();

            int nextRoundIndex = Math.Clamp(_run!.RoundIndex + 1, 0, 2);

            // Compute end time by round index
            long endUtc;
            if (nextRoundIndex <= 1)
            {
                endUtc = utcNow + (long)TimeSpan.FromHours(RoundHours).TotalSeconds;
            }
            else
            {
                // Round2 ends at day reset (captured when round0 was created)
                endUtc = _run.DayResetUtcSeconds > 0
                    ? _run.DayResetUtcSeconds
                    : new DateTimeOffset(_raceScheduler.ComputeNextResetLocal(localNow, cfg.ResetHourLocal)).ToUnixTimeSeconds();
            }

            // Preserve some stable info
            string playerDisplayName = _run.Player.DisplayName;
            string playerAvatarId = _run.Player.AvatarId;
            int windowId = _run.WindowId;
            long dayResetUtc = _run.DayResetUtcSeconds;
            int configIndex = _run.ConfigIndex;

            // Create a fresh run for the new round
            var newRun = RaceRun.CreateNew(Guid.NewGuid().ToString("N"), utcNow, endUtc, cfg.GoalLevels, cfg.PlayersPerRace);
            newRun.ConfigIndex = configIndex;
            newRun.WindowId = windowId;
            newRun.RoundIndex = nextRoundIndex;
            newRun.DayResetUtcSeconds = dayResetUtc;

            // strict policy: gap only after next claim
            newRun.NextAllowedStartUtcSeconds = 0;

            newRun.Player = new RaceParticipant
            {
                Id = PlayerId,
                DisplayName = playerDisplayName,
                AvatarId = playerAvatarId,
                LevelsCompleted = 0,
                LastUpdateUtcSeconds = utcNow,
                IsBot = false,
                HasFinished = false,
                FinishedUtcSeconds = 0
            };

            _run = newRun;
            _save.CurrentRun = _run;
            _save.LastFlowState = RaceEventState.InRace;

            // Ensure bots for the new round
            _raceEngine.EnsureBotsSeeded(_run, utcNow, CurrentLevel, cfg, _botPool, out string logString);
            if (logString != string.Empty)
                Log(logString);

            TrySave();

            // Move to InRace
            _sm.SetState(RaceEventState.InRace);

            PublishRunUpdated();
            RequestPopup(new PopupRequest(PopupType.Main));
            Log($"StartNextRound accepted. RoundIndex={_run.RoundIndex} EndUtc={_run.EndUtcSeconds}");
        }

        private void ClearRun(string reason)
        {
            _run = null;
            _save.CurrentRun = null;
            _save.LastFlowState = RaceEventState.Idle;
            TrySave();
            Log($"Run cleared: {reason}");
            PublishRunUpdated();
        }

        private bool CanSimulateBots(long utcNow)
        {
            if (_run == null) return false;
            // Chỉ simulate khi đang trong race
            if (State != RaceEventState.InRace) return false;
            // tránh double tick trong cùng 1 giây
           
            if (utcNow == _lastSimulatedUtc) return false;
            return true;
        }

        //Chỉ simulate 1 lần mỗi giây (dùng lastSimulatedUtc).
        //Tính hash trước/sau để biết có thay đổi progress hay không.
        //Trả về true nếu run thay đổi(để Service quyết định Save/Publish).
        private void SimulateBotsIfChanged()
        {
            var utcNow = NowUtcSeconds();
            if (!CanSimulateBots(utcNow)) return;

            _lastSimulatedUtc = utcNow;

            var beforeHash = _raceEngine.ComputeRunProgressHash(_run);
            GhostBotSimulator.SimulateBots(_run, utcNow);
            var afterHash = _raceEngine.ComputeRunProgressHash(_run);

            // Nếu có thay đổi thì mới publish + save (đỡ spam)
            if (afterHash != beforeHash)
            {
                _save.CurrentRun = _run;
                TrySave();
                PublishRunUpdated();
                Log("Bots simulated (tick)");
            }

            FinalizeIfTimeUp(utcNow);
        }

        private void FinalizeIfTimeUp(long utcNow)
        {
            if (_run == null) return;
            if (_run.IsFinalized) return;

            if (utcNow < _run.EndUtcSeconds) return;

            var cfg = ActiveConfigForRunOrCursor();
            var localNow = NowLocal();

            // Luồng mới: KHÔNG còn Extend.
            // Nếu hết giờ mà chưa finish -> restart lại đúng round đó (không reward).
            if (!_run.Player.HasFinished)
            {
                // Không cho claim/reward nếu chưa finish -> restart same round
                RestartSameRound(localNow, utcNow, "Time up (NOT finished) -> Restart same round");
                return;
            }

            // 2) Nếu đã finish -> finalize thành Ended/Claimable như cũ
            _run.IsFinalized = true;
            _run.FinalizedUtcSeconds = utcNow;

            foreach (var p in _run.AllParticipants())
            {
                if (!p.HasFinished) continue;

                if (p.FinishedUtcSeconds > 0) continue;

                if (p.LastUpdateUtcSeconds > 0) p.FinishedUtcSeconds = p.LastUpdateUtcSeconds;
                else p.FinishedUtcSeconds = utcNow;
            }

            var standings = RaceStandings.Compute(_run.AllParticipants(), _run.GoalLevels);

            int rank = standings.FindIndex(p => p.Id == _run.Player.Id) + 1;
            _run.FinalPlayerRank = rank <= 0 ? standings.Count : rank;
            _run.WinnerId = standings.Count > 0 ? standings[0].Id : "";

            _save.CurrentRun = _run;
            TrySave();

            _sm.SetState(RaceEventState.Ended);
            _save.LastFlowState = RaceEventState.Ended;

            if (IsPopupActive(PopupType.Main) || IsPopupActive(PopupType.Info))
            {
                RequestPopup(new PopupRequest(PopupType.Ended)); // dùng Ended popup, đổi nút thành Claimed
            }

            PublishRunUpdated();
            Log($"Race finalized. Winner={_run.WinnerId}, PlayerRank={_run.FinalPlayerRank}");
        }

        /// <summary>
        /// Restart lại đúng round hiện tại (không reward).
        /// Reset sạch progress player/bots và thời gian round.
        /// </summary>
        private void RestartSameRound(DateTime localNow, long utcNow, string reason)
        {
            if (_run == null) return;

            var cfg = ActiveConfigForRunOrCursor();

            // Preserve stable fields
            int windowId = _run.WindowId;
            int roundIndex = _run.RoundIndex;
            long dayResetUtc = _run.DayResetUtcSeconds;
            int configIndex = _run.ConfigIndex;

            string playerDisplayName = string.IsNullOrEmpty(_run.Player.DisplayName) ? PlayerDisplayNameDefault : _run.Player.DisplayName;
            string playerAvatarId = _run.Player.AvatarId;

            // Compute end time for this round
            long endUtc;
            if (roundIndex <= 1)
            {
                endUtc = utcNow + (long)TimeSpan.FromHours(RoundHours).TotalSeconds;
            }
            else
            {
                // Round2 ends at day reset (if available), else compute next reset
                endUtc = dayResetUtc > 0
                    ? dayResetUtc
                    : new DateTimeOffset(_raceScheduler.ComputeNextResetLocal(localNow, cfg.ResetHourLocal)).ToUnixTimeSeconds();
            }

            // Create fresh run instance for same round (simplest & safest reset)
            var newRun = RaceRun.CreateNew(Guid.NewGuid().ToString("N"), utcNow, endUtc, cfg.GoalLevels, cfg.PlayersPerRace);
            newRun.ConfigIndex = configIndex;
            newRun.WindowId = windowId;
            newRun.RoundIndex = roundIndex;
            newRun.DayResetUtcSeconds = dayResetUtc;

            // strict policy: gap only after claim (claim only after finish)
            newRun.NextAllowedStartUtcSeconds = 0;

            newRun.Player = new RaceParticipant
            {
                Id = PlayerId,
                DisplayName = playerDisplayName,
                AvatarId = playerAvatarId,
                LevelsCompleted = 0,
                LastUpdateUtcSeconds = utcNow,
                IsBot = false,
                HasFinished = false,
                FinishedUtcSeconds = 0
            };

            _run = newRun;
            _save.CurrentRun = _run;

            // Return to InRace
            _sm.SetState(RaceEventState.InRace);
            _save.LastFlowState = RaceEventState.InRace;

            // Reseed bots for the restarted round
            _raceEngine.EnsureBotsSeeded(_run, utcNow, CurrentLevel, cfg, _botPool, out string logString);
            if (!string.IsNullOrEmpty(logString))
                Log(logString);

            TrySave();
            PublishRunUpdated();

            // Bring user back to main race UI if they are already inside race views
            if (IsPopupActive(PopupType.Main) || IsPopupActive(PopupType.Info) || IsPopupActive(PopupType.Ended))
                RequestPopup(new PopupRequest(PopupType.Main));

            Log(reason);
        }

        private void EndRaceNowAndFinalize()
        {
            if (_run == null) return;
            if (State != RaceEventState.InRace) return;

            var utcNow = NowUtcSeconds();

            // ép end time = now để FinalizeIfTimeUp chạy
            _run.EndUtcSeconds = utcNow;

            // đảm bảo chưa finalize
            _run.IsFinalized = false;

            FinalizeIfTimeUp(utcNow);
        }

        public RaceReward GetRewardForRank(int rank)
        {
            var cfg = ActiveConfigForRunOrCursor(); // run-config

            return rank switch
            {
                1 => cfg.Rank1Reward,
                2 => cfg.Rank2Reward,
                3 => cfg.Rank3Reward,
                4 => cfg.Rank4Reward,
                _ => cfg.Rank5Reward,
            };
        }

        public void ForceRequestEntryPopup(DateTime localNow)
        {
            ThrowIfNotInitialized();

            RefreshEligibility(localNow);

            RequestPopup(new PopupRequest(PopupType.Entry));
        }

        public void RequestInRacePopup()
        {
            ThrowIfNotInitialized();
            if (State == RaceEventState.InRace)
                RequestPopup(new PopupRequest(PopupType.Main));
        }

        public void RequestEndedPopup()
        {
            ThrowIfNotInitialized();
            if (State == RaceEventState.Ended)
                RequestPopup(new PopupRequest(PopupType.Ended));
        }

        public void RequestEntryPopup(DateTime localNow)
        {
            ThrowIfNotInitialized();

            RefreshEligibility(localNow);

            if (IsEligibleForEntry(localNow))
                RequestPopup(new PopupRequest(PopupType.Entry));
        }

        public void ClearCurrentRun()
        {
            ThrowIfNotInitialized();

            if (_run == null)
                return;

            Log("[RaceEvent] ClearCurrentRun");

            // 1. clear runtime
            _run = null;
            _save.CurrentRun = null;

            // 2. reset flow/state
            _sm.SetState(RaceEventState.Idle);
            _save.LastFlowState = RaceEventState.Idle;

            // 3. persist
            TrySave();

            // 4. notify UI
            PublishRunUpdated();
        }

        private int ClampConfigIndex(int i) => Math.Clamp(i, 0, _configs.Count - 1);
        private RaceEventConfig GetConfigByIndex(int i) => _configs[ClampConfigIndex(i)];
        private RaceEventConfig ActiveConfigForRunOrCursor()
        {
            if (_run != null) return GetConfigByIndex(_run.ConfigIndex);

            int eligibleIndex = GetEligibleCursorIndex();

            if (eligibleIndex != _save.ConfigCursor)
            {
                _save.ConfigCursor = eligibleIndex;
                TrySave();
            }

            return GetConfigByIndex(eligibleIndex);
        }
        private int GetEligibleCursorIndex()
        {
            // cursor base (đã clamp)
            int i = ClampConfigIndex(_save.ConfigCursor);

            // lùi dần về 0 để tìm config hợp lệ theo MinPlayerLevel
            while (i > 0 && CurrentLevel < _configs[i].MinPlayerLevel)
                i--;

            return i;
        }
        public SearchingPlan GetSearchingSnapshot()
        {
            var total = ActiveConfigForRunOrCursor().SearchingDurationSeconds;
            var start = _save.SearchingStartUtcSeconds;

            if (start <= 0) return new SearchingPlan(total);

            var now = NowUtcSeconds();
            var elapsed = (int)System.Math.Max(0, now - start);
            var remaining = System.Math.Max(0, total - elapsed);
            return new SearchingPlan(remaining);
        }
        public string FormatHMS(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            int h = (int)t.TotalHours;
            int m = t.Minutes;
            int s = t.Seconds;
            return $"{h:00}:{m:00}:{s:00}";
        }
        public string FormatHM(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;

            // làm tròn lên theo phút
            var totalMinutes = (int)Math.Ceiling(t.TotalMinutes);

            int h = totalMinutes / 60;
            int m = totalMinutes % 60;

            return $"{h}h{m:00}'";
        }

        public LeaderboardSnapshot BuildLeaderboardSnapshot(int topN)
        {
            ThrowIfNotInitialized();
            return _raceEngine.BuildLeaderboardSnapshot(_run, topN);
        }

        public string ExtractNumberSuffix(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            int i = input.Length - 1;
            while (i >= 0 && char.IsDigit(input[i]))
                i--;

            return input.Substring(i + 1);
        }

        public void NotifyPopupShown(PopupType type)
        {
            _currentPopupTypeOpen = type;
            //Log($"[DEBUG] NotifyPopupShown.{type}");
            //if (ConsumeFirstTimePopup(type))
            //{
            //    Log($"[Tutorial] First time popup shown: {type}");
            //    OnTutorialRequested?.Invoke(type);
            //}
        }

        public void NotifyPopupHidden(PopupType type)
        {
            if (_currentPopupTypeOpen == type)
                _currentPopupTypeOpen = null;
        }

        private bool IsPopupActive(PopupType type)
        {
            return _currentPopupTypeOpen == type;
        }

        public bool ConsumeFirstTimePopup(PopupType type)
        {
            ThrowIfNotInitialized();

            _save.SeenPopupTypes ??= new List<int>();

            int key = (int)type;
            for (int i = 0; i < _save.SeenPopupTypes.Count; i++)
                if (_save.SeenPopupTypes[i] == key)
                    return false;

            // first time ever
            _save.SeenPopupTypes.Add(key);
            TrySave();
            return true;
        }

        public void RemoveConsumePopup(PopupType type) 
        { 
            ThrowIfNotInitialized();
            _save.SeenPopupTypes ??= new List<int>();

            int key = (int)type;

            if (_save.SeenPopupTypes.Contains(key))
            {
                _save.SeenPopupTypes.Remove(key);
            }
        }

        #region ELIGIBILITY
        private RaceEligibilityContext BuildEligibilityContext(DateTime localNow, RaceEventConfig cfg)
        {
            return new RaceEligibilityContext(
                            state: State,
                            currentLevel: CurrentLevel,
                            localNow: localNow,
                            lastJoinLocalUnixSeconds: _save.LastJoinLocalUnixSeconds,
                            lastEntryShownWindowId: _save.LastEntryShownWindowId,
                            config: cfg
                            );
        }

        /// <summary>
        /// Eligible thuần: dùng cho HUD / enable nút / manual open entry.
        /// KHÔNG chứa rule "shown once/day".
        /// </summary>
        public bool IsEligibleForEntry(DateTime localNow)
        {
            ThrowIfNotInitialized();

            // vNext semantics: "EligibleForEntry" nghĩa là *có thể mở flow Entry để join/start ngay bây giờ*.
            // Nếu đang có run hoặc đang ở state khác Idle (Searching/InRace/Ended) thì KHÔNG được Entry.
            // (StartNext là một flow riêng, dùng CanStartNextRoundNow).
            if (_run != null) return false;
            if (State != RaceEventState.Idle) return false;

            var cfg = ActiveConfigForRunOrCursor();
            var ctx = BuildEligibilityContext(localNow, cfg);
            return _raceEligibility.IsEligible(ctx);
        }

        /// <summary>
        /// Auto show entry chỉ 1 lần/window: chỉ dùng trong OnEnterMain.
        /// </summary>
        private bool ShouldAutoShowEntryOnEnterMain(DateTime localNow)
        {
            // Auto show chỉ hợp lệ khi đang Idle và chưa có run.
            // Nếu đang Ended/InRace/Search thì tuyệt đối không auto-open Entry.
            if (_run != null) return false;
            if (State != RaceEventState.Idle) return false;

            var cfg = ActiveConfigForRunOrCursor();

            // Reuse cùng semantics với HUD/Entry
            if (!IsEligibleForEntry(localNow)) return false;

            int windowId = _raceScheduler.ComputeWindowId(localNow, cfg.ResetHourLocal);
            if (_save.LastEntryShownWindowId == windowId) return false;

            // Mark ngay để đảm bảo "1 lần/ngày"
            _save.LastEntryShownWindowId = windowId;
            TrySave();
            return true;
        }
        #endregion

        #region HUD Status
        public RaceHudStatus BuildHudStatus(DateTime localNow)
        {
            ThrowIfNotInitialized();

            var ctx = BuildHudContext(localNow);

            return _raceHudPresenter.BuildHudStatus(ctx);
        }

        public RaceHudStatus BuildHudStatus(DateTime localNow, out object context)
        {
            ThrowIfNotInitialized();

            // Build struct context (không alloc)
            var ctx = BuildHudContext(localNow);

            // Reuse token (không boxing)
            _hudContextToken.Set(ctx);
            context = _hudContextToken;

            return _raceHudPresenter.BuildHudStatus(ctx);
        }

        private RaceHudContext BuildHudContext(DateTime localNow)
        {
            RaceEventConfig cfg = ActiveConfigForRunOrCursor();
            long utcNow = NowUtcSeconds();

            // next reset theo config (local time)
            DateTime nextResetLocal = _raceScheduler.ComputeNextResetLocal(localNow, cfg.ResetHourLocal);

            // HUD cần biết có claim được không + có entry được không
            bool canClaim = CanClaim();
            bool isEligible = IsEligibleForEntry(localNow);
            bool canStartNextRoundNow = CanStartNextRoundNow(localNow);

            var ctx = new RaceHudContext(
                        localNow: localNow,
                        utcNow: utcNow,
                        currentLevel: CurrentLevel,
                        state: State,
                        cfg: cfg,
                        run: _run,
                        canClaim: canClaim,
                        canStartNextRoundNow: canStartNextRoundNow,
                        isEligible: isEligible,
                        nextResetLocal: nextResetLocal
                    );

            return ctx;
        }

        public RaceHudClickAction BuildHudClickAction(DateTime localNow)
        {
            ThrowIfNotInitialized();

            return _raceHudPresenter.BuildHudClickAction(GetHudMode(localNow));
        }

        private HudMode GetHudMode(DateTime localNow)
        {
            ThrowIfNotInitialized();

            var hud = BuildHudStatus(localNow, out object context);

            if (context is HudContextToken token)
                return _raceHudPresenter.BuildHudMode(token.Ctx, hud);

            // fallback an toàn
            return HudMode.Hidden;
        }

        #endregion
    }
}

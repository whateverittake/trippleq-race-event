
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

        // ---- Events for UI/Bootstrap ----
        public event Action<string>? OnLog;
        public event Action<RaceEventState, RaceEventState>? OnStateChanged;
        public event Action<PopupRequest>? OnPopupRequested;
        public event Action<RaceRun?>? OnRunUpdated;
        public event Action<RaceReward>? OnRewardGranted;
        public event Action<Action<bool>>? OnExtendAdsRequested;

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

        private DateTime NowLocal() => DateTime.Now;

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
            OnExtendAdsRequested=null;
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

            // ClearRunAfterClaim();
        }

        public void RequestWatchAdsToExtend()
        {
            ThrowIfNotInitialized();

            if (!CanExtend1H())
            {
                Log("WatchAdsToExtend rejected (not extendable)");
                return;
            }

            if (OnExtendAdsRequested == null)
            {
                Log("WatchAdsToExtend rejected (no ads handler bound)");
                return;
            }

            // ask host to show ad, host calls back with success/fail
            OnExtendAdsRequested.Invoke(success =>
            {
                if (!success)
                {
                    Log("WatchAdsToExtend failed (ad not completed)");
                    return;
                }

                Extend1H(); // ✅ reuse existing logic
            });
        }

        public ExtendOfferModel GetExtendOffer()
        {
            ThrowIfNotInitialized();
            if (!CanExtend1H()) return ExtendOfferModel.None();

            var cfg = ActiveConfigForRunOrCursor();

            if (!cfg.AllowExtend1H) return ExtendOfferModel.None();

            return cfg.ExtendPayType switch
            {
                ExtendPayType.WatchAds => new ExtendOfferModel(
                                                        ExtendPayType.WatchAds,
                                                        coinCost: 0,
                                                        extendHours: cfg.ExtendHours),
                ExtendPayType.Coins => new ExtendOfferModel(
                                                        ExtendPayType.Coins, 
                                                        coinCost: Math.Max(0, cfg.ExtendCoinCost), 
                                                        extendHours: cfg.ExtendHours),

                _ => ExtendOfferModel.None()
            };
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
                State == RaceEventState.Ended||
                State == RaceEventState.ExtendOffer)
                return;

            var isEligible = IsEligibleForEntry(localNow);
            // We separate 'Eligible' from just 'Idle' to let UI/HUD react later
            _sm.SetState(isEligible ? RaceEventState.Eligible : RaceEventState.Idle);
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

            _sm.SetState(RaceEventState.InRace);
            _save.LastFlowState = RaceEventState.InRace;

            // Create run NOW
            var utcNow = NowUtcSeconds();
            var startUtc = utcNow;

            var localNow = NowLocal();
            var dailyEndLocal = _raceScheduler.ComputeNextResetLocal(localNow, ActiveConfigForRunOrCursor().ResetHourLocal); // theo config
            var endUtc = new DateTimeOffset(dailyEndLocal).ToUnixTimeSeconds();
            //var endUtc = utcNow + (long)TimeSpan.FromHours(ActiveConfigForRunOrCursor().DurationHours).TotalSeconds;
            var cfgIndex = _save.ConfigCursor;

            _run = RaceRun.CreateNew(
                runId: Guid.NewGuid().ToString("N"),
                startUtc: startUtc,
                endUtc: endUtc,
                goalLevels: ActiveConfigForRunOrCursor().GoalLevels,
                playersCount: ActiveConfigForRunOrCursor().PlayersPerRace
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

        private void CleanupExpiredRunIfNeeded(long utcNow)
        {
            if (_run == null) return;

            // data invalid -> clear ngay
            if (_run.PlayersCount <= 0 || _run.GoalLevels <= 0)
            {
                ClearRun("Invalid run data");
                return;
            }

            // If time is up: DO NOT clear.
            // Instead: finalize (so it becomes claimable).
            if (utcNow >= _run.EndUtcSeconds)
            {
                if (!_run.IsFinalized)
                {
                    FinalizeIfTimeUp(utcNow); // will set state Ended + save
                }
                return;
            }

            // Optional: if already claimed and too old -> clear
            // Example: keep ended run for 24h after claim then clear
            if (_run.IsFinalized && _run.HasClaimed)
            {
                var keepSeconds = (long)TimeSpan.FromHours(ActiveConfigForRunOrCursor().KeepClaimedHours).TotalSeconds; // add config or const
                if (_run.ClaimedUtcSeconds > 0 && utcNow >= _run.ClaimedUtcSeconds + keepSeconds)
                {
                    ClearRun("Claimed run expired");
                }
            }
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

            //nếu chưa về đích, chưa extend, và config cho phép => offer extend
            if (ActiveConfigForRunOrCursor().AllowExtend1H &&!_run.HasExtended && !_run.Player.HasFinished)
            {
                _sm.SetState(RaceEventState.ExtendOffer);
                _save.LastFlowState = RaceEventState.ExtendOffer;
                TrySave();

                if (IsPopupActive(PopupType.Main)|| IsPopupActive(PopupType.Info))
                {
                    RequestPopup(new PopupRequest(PopupType.Ended)); // dùng Ended popup, đổi nút thành Extend/Claimed tuỳ state
                }

                PublishRunUpdated();
                Log("Time up -> ExtendOffer");
                return;
            }

            _run.IsFinalized = true;
            _run.FinalizedUtcSeconds = utcNow;

            var standings = RaceStandings.Compute(_run.AllParticipants(), _run.GoalLevels);

            for(int i=0;i < standings.Count; i++)
            {
                var standing = standings[i];
                if(standing.HasFinished== false)
                {
                    Log("xx not finish");
                }
            }

            int rank = standings.FindIndex(p => p.Id == _run.Player.Id) + 1;
            _run.FinalPlayerRank = rank <= 0 ? standings.Count : rank;
            _run.WinnerId = standings.Count > 0 ? standings[0].Id : "";

            _save.CurrentRun = _run;
            TrySave();

            _sm.SetState(RaceEventState.Ended);
            _save.LastFlowState = RaceEventState.Ended;

            if (IsPopupActive(PopupType.Main) || IsPopupActive(PopupType.Info))
            {
                RequestPopup(new PopupRequest(PopupType.Ended)); // dùng Ended popup, đổi nút thành Extend/Claimed tuỳ state
            }

            PublishRunUpdated();
            Log($"Race finalized. Winner={_run.WinnerId}, PlayerRank={_run.FinalPlayerRank}");
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

        public void DeclineExtend()
        {
            ThrowIfNotInitialized();
            if (_run == null) return;

            // Chỉ cho decline khi đang offer
            if (State != RaceEventState.ExtendOffer) return;

            var utcNow = NowUtcSeconds();

            // Mark để FinalizeIfTimeUp không offer nữa
            _run.HasExtended = true; // trick: coi như đã "xử lý extend" để không offer lặp
                                     // hoặc tốt hơn: thêm field _run.HasDeclinedExtend (nếu bạn muốn sạch)

            // Finalize ngay
            _run.EndUtcSeconds = Math.Min(_run.EndUtcSeconds, utcNow);
            _run.IsFinalized = false; // đảm bảo finalize chạy
            FinalizeIfTimeUp(utcNow);
        }

        public bool CanExtend1H()
        {
            ThrowIfNotInitialized();
            if (_run == null) return false;
            if (!ActiveConfigForRunOrCursor().AllowExtend1H) return false;
            if (_run.IsFinalized) return false;
            if (_run.HasExtended) return false;
            if (_run.Player.HasFinished) return false;
            if (State != RaceEventState.ExtendOffer) return false;
            return true;
        }

        public void Extend1H()
        {
            ThrowIfNotInitialized();
            if (!CanExtend1H())
            {
                Log("Extend1H rejected");
                return;
            }

            var utcNow = NowUtcSeconds();
            var addSeconds = (long)TimeSpan.FromHours(ActiveConfigForRunOrCursor().ExtendHours).TotalSeconds;

            // nếu muốn tính từ end cũ: _run.EndUtcSeconds + addSeconds
            // nếu muốn tính từ giờ hiện tại: utcNow + addSeconds
            var newEnd = utcNow + addSeconds;

            if (_run.OriginalEndUtcSeconds <= 0)
                _run.OriginalEndUtcSeconds = _run.EndUtcSeconds;

            _run.HasExtended = true;
            _run.ExtendedEndUtcSeconds = newEnd;
            _run.EndUtcSeconds = newEnd;
            _debugFakeUtcSeconds = 0;
            _save.CurrentRun = _run;

            _sm.SetState(RaceEventState.InRace);
            _save.LastFlowState = RaceEventState.InRace;

            TrySave();
            PublishRunUpdated();

            RequestPopup(new PopupRequest(PopupType.Main));
            Log($"Extend1H accepted. NewEndUtc={newEnd}");
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
            if (State == RaceEventState.Ended || State == RaceEventState.ExtendOffer)
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

            // 2. reset flow/state
            _sm.SetState(RaceEventState.Idle);
            _save.LastFlowState = RaceEventState.Idle;

            // 3. persist
            TrySave();
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
            var cfg = ActiveConfigForRunOrCursor();
            var ctx = BuildEligibilityContext(localNow, cfg);
            return _raceEligibility.IsEligible(ctx);
        }

        /// <summary>
        /// Auto show entry chỉ 1 lần/window: chỉ dùng trong OnEnterMain.
        /// </summary>
        private bool ShouldAutoShowEntryOnEnterMain(DateTime localNow)
        {
            var cfg = ActiveConfigForRunOrCursor();
            var ctx = BuildEligibilityContext(localNow, cfg);

            if (!_raceEligibility.IsEligible(ctx)) return false;

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

            var ctx = new RaceHudContext(
                        localNow: localNow,
                        utcNow: utcNow,
                        currentLevel: CurrentLevel,
                        state: State,
                        cfg: cfg,
                        run: _run,
                        canClaim: canClaim,
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

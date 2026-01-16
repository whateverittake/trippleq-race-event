
#nullable enable
using System;
using System.Collections.Generic;
using static TrippleQ.Event.RaceEvent.Runtime.PopupTypes;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    /// <summary>
    /// Facade/API entry for host games.
    /// - Pure C# service (no MonoBehaviour).
    /// - Orchestrates: StateMachine + Simulation + Persistence + Providers.
    /// - UI listens to events emitted by this service.
    /// </summary>
    public sealed class RaceEventService : IDisposable
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

            SimulateBotsTick();
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



        private void ClearRunAfterClaim()
        {
            _run = null;
            _save.CurrentRun = null;
            _save.LastFlowState = RaceEventState.Idle;
            TrySave();
            PublishRunUpdated();
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
        public void OnEnterMain(bool isInTutorial, DateTime localNow)
        {
            ThrowIfNotInitialized();
            Log("OnEnterMain()");

            RefreshEligibility(isInTutorial, localNow);

            if (ShouldShowEntryPopup(isInTutorial, localNow))
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

            //if (State == RaceEventState.Ended)
            //{
            //    RequestPopup(new PopupRequest(PopupType.Ended));
            //}
        }

        /// <summary>
        /// Host calls this after player wins a level.
        /// </summary>
        public void OnLevelWin(int newLevel, bool isInTutorial, DateTime localNow)
        {
            ThrowIfNotInitialized();

            if (newLevel < 0) newLevel = 0;

            if (newLevel != CurrentLevel)
            {
                CurrentLevel = newLevel;
                Log($"OnLevelWin(): CurrentLevel={CurrentLevel}");
            }

            RefreshEligibility(isInTutorial, localNow);

            //// Optional: show entry right after win if eligible (tweak later)
            //if (ShouldShowEntryPopup(isInTutorial, localNow))
            //{
            //    //RequestPopup(new PopupRequest(PopupType.Entry));
            //}

            if (_run != null && State == RaceEventState.InRace)
            {
                var utcNow = NowUtcSeconds();

                _run.Player.LevelsCompleted += 1;
                _run.Player.LastUpdateUtcSeconds = utcNow;

                if (!_run.Player.HasFinished && _run.Player.LevelsCompleted >= _run.GoalLevels)
                {
                    _run.Player.HasFinished = true;
                    _run.Player.FinishedUtcSeconds = utcNow;

                    Log(
                            $"[RACE][PLAYER FINISH] levels={_run.Player.LevelsCompleted}/{_run.GoalLevels} finishUtc={utcNow}"
                        );
                }

                GhostBotSimulator.SimulateBots(_run, utcNow);

                _save.CurrentRun = _run;
                TrySave();
                PublishRunUpdated();

                //// End condition MVP: player reaches goal -> Ended/Claimable
                //if (_run.HasPlayerReachedGoal())
                //{
                //    _sm.SetState(RaceEventState.Ended);
                //    RequestPopup(new PopupRequest(PopupType.Ended));
                //}

                // NOTE: no early end here
                FinalizeIfTimeUp(utcNow);
            }
        }


        // --------------------
        // Eligibility (B)
        // --------------------
        public bool ShouldShowEntryPopup(bool isInTutorial, DateTime localNow)
        {
            ThrowIfNotInitialized();
            // Do not show when already in race flow
            if (State == RaceEventState.Searching || State == RaceEventState.InRace)
                return false;
            // Feature gating
            if (!ActiveConfigForRunOrCursor().Enabled) return false;
            // Tutorial gating
            if (ActiveConfigForRunOrCursor().BlockDuringTutorial && isInTutorial) return false;
            // Min level gating
            if (CurrentLevel < ActiveConfigForRunOrCursor().MinPlayerLevel) return false;

            //Log("xx 8: " + isInTutorial);
            // Cooldown gating (hours)
            if (IsInCooldown(localNow)) return false;
            //Log("xx 9: " + isInTutorial);
            // Once per day gating (based on resetHourLocal)
            if (HasShownEntryInCurrentWindow(localNow)) return false;

            return true;
        }

        private void RefreshEligibility(bool isInTutorial, DateTime localNow)
        {
            // If currently searching/in race, don't downgrade to Eligible/Idle
            if (State == RaceEventState.Searching ||
                State == RaceEventState.InRace ||
                State == RaceEventState.Ended||
                State == RaceEventState.ExtendOffer)
                return;

            var canShow = ShouldShowEntryPopup(isInTutorial, localNow);
            // We separate 'Eligible' from just 'Idle' to let UI/HUD react later
            _sm.SetState(canShow ? RaceEventState.Eligible : RaceEventState.Idle);
        }

        private bool IsInCooldown(DateTime localNow)
        {
            if (_save.LastJoinLocalUnixSeconds <= 0) return false;

            var lastJoin = DateTimeOffset.FromUnixTimeSeconds(_save.LastJoinLocalUnixSeconds).LocalDateTime;
            var hours = (localNow - lastJoin).TotalHours;
            return hours < ActiveConfigForRunOrCursor().EntryCooldownHours;
        }

        private bool HasShownEntryInCurrentWindow(DateTime localNow)
        {
            // We store the "window id" as the local date of reset boundary.
            // Example: reset at 4AM:
            // - from 04:00 today to 03:59 tomorrow => same window id.

            var windowId = GetWindowId(localNow, ActiveConfigForRunOrCursor().ResetHourLocal);
            return _save.LastEntryShownWindowId == windowId;
        }

        private static int GetWindowId(DateTime localNow, int resetHourLocal)
        {
            // Shift time by reset boundary to make "day window"
            // If resetHourLocal = 4, then 02:00 belongs to previous day window.
            var shifted = localNow.AddHours(-resetHourLocal);
            // int like 20251226
            return shifted.Year * 10000 + shifted.Month * 100 + shifted.Day;
        }

        // --------------------
        // Join flow
        // --------------------
        public bool CanJoinRace(bool isInTutorial, DateTime localNow)
        {
            if (State == RaceEventState.Searching || State == RaceEventState.InRace) return false;
            return ShouldShowEntryPopup(isInTutorial, localNow);
        }

        /// <summary>
        /// Called by Entry popup when player taps "Play/Join".
        /// </summary>
        public void JoinRace(bool isInTutorial, DateTime localNow)
        {
            ThrowIfNotInitialized();
            if (!CanJoinRace(isInTutorial, localNow))
            {
                Log("JoinRace rejected (not eligible)");
                return;
            }

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
            TrySave();

            PublishRunUpdated();
        }

        private void SeedBots(RaceRun run, long utcNow)
        {
            // MVP pool (localization làm sau)
            var pool = _botPool.Bots;
            if (pool == null || pool.Count == 0)
            {
                Log("BotPool is empty. Please export race_bot_pool.json");
                return;
            }
           
            run.Opponents.Clear();
            // Pick first N-1 (later: random w/ RNG provider)
            var need = System.Math.Max(0, ActiveConfigForRunOrCursor().PlayersPerRace - 1);

            // 1) Tính quota theo config
            var comp = ActiveConfigForRunOrCursor().BotComposition;
            // Nếu tổng quota != need => scale / clamp cho đúng need
            NormalizeComposition(ref comp, need);

            var levelFilteredPool = FilterByPlayerLevelOrClosest(pool, CurrentLevel);

            if (levelFilteredPool.Count == 0)
            {
                // fallback: tuyệt đối không được thiếu
                levelFilteredPool = new List<BotProfile>(pool);
            }

            // 1) Add theo quota (ưu tiên đúng level trước)
            AddFromPool(run, levelFilteredPool, BotPersonality.Boss, comp.BossCount, utcNow, allowDuplicate: false);
            AddFromPool(run, levelFilteredPool, BotPersonality.Normal, comp.NormalCount, utcNow, allowDuplicate: false);
            AddFromPool(run, levelFilteredPool, BotPersonality.Noob, comp.NoobCount, utcNow, allowDuplicate: false);

            // 2) Nếu thiếu theo từng type -> bù đúng type từ FULL POOL (bất chấp level)
            EnsurePersonalityCount(run, levelFilteredPool, pool, BotPersonality.Boss, comp.BossCount, utcNow);
            EnsurePersonalityCount(run, levelFilteredPool, pool, BotPersonality.Normal, comp.NormalCount, utcNow);
            EnsurePersonalityCount(run, levelFilteredPool, pool, BotPersonality.Noob, comp.NoobCount, utcNow);

            // 3) Nếu vẫn thiếu tổng -> fill bằng bot gần level yêu cầu nhất (bất kỳ type, allowDuplicate)
            int remaining = need - run.Opponents.Count;
            if (remaining > 0)
                FillRemainingClosestLevel(run, pool, remaining, utcNow, CurrentLevel);

            // Tới đây mà vẫn thiếu thì chỉ có thể là pool rỗng
            if (run.Opponents.Count < need)
            {
                Log($"[ERROR] SeedBots cannot fill need={need}, got={run.Opponents.Count}. BotPool empty?");
            }

            // 4) Shuffle để boss không luôn đứng đầu
            Shuffle(run.Opponents);
        }

        /// <summary>
        /// Called by UI if player closes entry popup without joining.
        /// Still counts as "shown once/day" in this step (common LiveOps behavior).
        /// </summary>
        public void MarkEntryShown(DateTime localNow)
        {
            ThrowIfNotInitialized();
            _save.LastEntryShownWindowId = GetWindowId(localNow, ActiveConfigForRunOrCursor().ResetHourLocal);
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
            var dailyEndLocal = GetNextResetLocal(localNow); // theo config
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

            SeedBots(_run, utcNow);

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

        // --------------------
        // Helpers
        // --------------------
        private void RequestPopup(PopupRequest type) => OnPopupRequested?.Invoke(type);
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

        private void SimulateBotsTick()
        {
            if (_run == null) return;

            // Chỉ simulate khi đang trong race (tuỳ bạn muốn Searching cũng sim hay không)
            if (State != RaceEventState.InRace) return;

            var utcNow = NowUtcSeconds();
            if (utcNow == _lastSimulatedUtc) return; // tránh double tick trong cùng 1 giây
            _lastSimulatedUtc = utcNow;

            // Simulate
            var beforeHash = ComputeProgressHash(_run);
            GhostBotSimulator.SimulateBots(_run, utcNow);
            var afterHash = ComputeProgressHash(_run);

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

        private static int ComputeProgressHash(RaceRun run)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + run.Player.LevelsCompleted;
                for (int i = 0; i < run.Opponents.Count; i++)
                    h = h * 31 + run.Opponents[i].LevelsCompleted;
                return h;
            }
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

        #region HUD Status
        public DateTime GetNextResetLocal(DateTime localNow)
        {
            // reset hour like 4:00
            var resetToday = new DateTime(localNow.Year, localNow.Month, localNow.Day, ActiveConfigForRunOrCursor().ResetHourLocal, 0, 0);

            // if we're already past today's reset => next reset is tomorrow
            if (localNow >= resetToday)
                return resetToday.AddDays(1);

            // else next reset is today at reset hour
            return resetToday;
        }

        public RaceHudStatus GetHudStatus(DateTime localNow)
        {
            ThrowIfNotInitialized();

            var cfg = ActiveConfigForRunOrCursor();
            Log("xx 1");
            // if feature off -> hide
            if (!cfg.Enabled)
                return new RaceHudStatus(false, false, false, TimeSpan.Zero, "Next: ", false);

            ////HUD preview lock window ---
            //const int previewOffset = 5;
            //int unlockLevel = Math.Max(0, cfg.MinPlayerLevel);
            //int showPreviewLevel = Math.Max(0, unlockLevel - previewOffset);

            //// before preview => hide widget
            //if (CurrentLevel < showPreviewLevel)
            //    return new RaceHudStatus(false, false, false, TimeSpan.Zero, "", false);

            //// preview but locked
            //if (CurrentLevel < unlockLevel)
            //{
            //    return new RaceHudStatus(
            //        isVisible: true,
            //        isSleeping: true,          // dùng icon xám/sleeping state
            //        hasClaim: false,
            //        remaining: TimeSpan.Zero,
            //        label: $"Open in Level {unlockLevel}",
            //        showTextCountdown: false,
            //        isLocked: true,
            //        unlockAtLevel: unlockLevel
            //    );
            //}
            Log("xx 2: "+ State);
            // If ended & can claim => show claim attention (not sleeping)
            if (State == RaceEventState.Ended && CanClaim())
                return new RaceHudStatus(true, false, true, TimeSpan.Zero, "Claim now!", false);
            Log("xx 3");
            // If in race -> hide widget (hoặc show active icon)
            if (State == RaceEventState.InRace || State == RaceEventState.Searching)
            {
                // no run? fallback hide
                if (_run == null)
                    return new RaceHudStatus(false, false, false, TimeSpan.Zero, "", false);

                // (A) EXTENDED: show remaining to EndUtc (≈ 1h),
                // and if expired -> hide (your requirement)
                if (_run.HasExtended)
                {
                    var nowUtc = NowUtcSeconds();
                    var remainingSec = _run.EndUtcSeconds - nowUtc;

                    if (remainingSec <= 0)
                        return new RaceHudStatus(false, false, false, TimeSpan.Zero, "", false);

                    var remaining = TimeSpan.FromSeconds(remainingSec);
                    return new RaceHudStatus(true, false, false, remaining, "End in: ", true);
                }

                // (B) NOT EXTENDED: always count down to next 4AM reset
                var nextReset = GetNextResetLocal(localNow); // uses ResetHourLocal
                var remaining2 = nextReset - localNow;
                if (remaining2 < TimeSpan.Zero) remaining2 = TimeSpan.Zero;

                return new RaceHudStatus(true, false, false, remaining2, "End in: ", true);
            }
            Log("xx 4");
            // Otherwise: idle/eligible -> if eligible you may show active icon, if not eligible show sleeping + countdown
            var canShowEntry = ShouldShowEntryPopup(isInTutorial: false, localNow); // HUD không biết tutorial thì bạn có thể truyền vào overload khác
            if (canShowEntry)
            {
                // active state: no countdown
                return new RaceHudStatus(true, false, false, TimeSpan.Zero, "Race now!", false);
            }
            Log("xx 5");
            var nextReset3 = GetNextResetLocal(localNow);
            var remaining3 = nextReset3 - localNow;
            if (remaining3 < TimeSpan.Zero) remaining3 = TimeSpan.Zero;

            return new RaceHudStatus(true, true, false, remaining3, "Next in: ", true);
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
            if (State == RaceEventState.Ended|| State== RaceEventState.ExtendOffer)
                RequestPopup(new PopupRequest(PopupType.Ended));
        }

        public void RequestEntryPopup(bool isInTutorial, DateTime localNow)
        {
            ThrowIfNotInitialized();
            RefreshEligibility(isInTutorial, localNow);
            if (ShouldShowEntryPopup(isInTutorial, localNow))
                RequestPopup(new PopupRequest(PopupType.Entry));
        }

        public void ForceRequestEntryPopup(bool isInTutorial, DateTime localNow)
        {
            ThrowIfNotInitialized();
            RefreshEligibility(isInTutorial, localNow);
            RequestPopup(new PopupRequest(PopupType.Entry));
        }

        public RaceHudClickAction GetHudClickAction(bool isInTutorial, DateTime localNow)
        {
            ThrowIfNotInitialized();

            var hud = GetHudStatus(localNow);
            if (hud.IsLocked) return RaceHudClickAction.None;

            if (State == RaceEventState.Ended && CanClaim())
                return RaceHudClickAction.OpenEnded;

            if (State == RaceEventState.InRace)
                return RaceHudClickAction.OpenInRace;

            if(State == RaceEventState.ExtendOffer)
                return RaceHudClickAction.OpenEnded;

            if (ShouldShowEntryPopup(isInTutorial, localNow))
                return RaceHudClickAction.OpenEntry;

            return RaceHudClickAction.None;
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

        #endregion

        /// <summary>
        /// DEBUG ONLY.
        /// Force end current race immediately (as if time is up).
        /// </summary>
        public void DebugEndEvent()
        {
            ThrowIfNotInitialized();

            Log($"[DEBUG] DebugEndEvent CALLED. State={State}, HasRun={_run != null}");

            if (_run == null)
            {
                Log("DebugEndEvent ignored (no active run)");
                return;
            }

            if (State != RaceEventState.InRace && State != RaceEventState.Ended && State != RaceEventState.ExtendOffer)
            {
                Log($"DebugEndEvent ignored (State={State})");
                return;
            }

            var utcNow = NowUtcSeconds();
            Log($"[DEBUG] Force End. Before: EndUtc={_run.EndUtcSeconds}, utcNow={utcNow}");

            // Force end time to now
            _run.EndUtcSeconds = utcNow;

            FinalizeIfTimeUp(utcNow);

            Log($"[DEBUG] Force End DONE. After: EndUtc={_run.EndUtcSeconds}, State={State}");

            RequestEndedPopup();
        }

        /// <summary>
        /// DEBUG ONLY:
        /// Clear claimed run and reset flow so a new race can be started immediately.
        /// </summary>
        public void Debug_ResetAfterClaimAndAllowNewRun()
        {
            ThrowIfNotInitialized();

            Log("[DEBUG] ResetAfterClaim");

            // Clear run
            _run = null;
            _save.CurrentRun = null;

            // Reset flow
            _sm.SetState(RaceEventState.Idle);
            _save.LastFlowState = RaceEventState.Idle;

            // Remove gating so Entry can show again
            _save.LastEntryShownWindowId = 0;
            _save.LastJoinLocalUnixSeconds = 0;
            _save.SearchingStartUtcSeconds = 0;

            TrySave();

            PublishRunUpdated(); // notify UI to clear standings
        }

        public void Debug_ForceClearAll()
        {
            ThrowIfNotInitialized();

            Log("[DEBUG] FORCE CLEAR ALL RACE EVENT DATA");

            // 1. Clear runtime
            _run = null;

            // 2. Clear save data liên quan run
            _save.CurrentRun = null;
            _save.LastFlowState = RaceEventState.Idle;

            // 3. Reset state machine
            _sm.SetState(RaceEventState.Idle);

            // 4. Clear ALL gating / cooldown / window
            _save.LastEntryShownWindowId = 0;
            _save.LastJoinLocalUnixSeconds = 0;
            _save.SearchingStartUtcSeconds = 0;

            // 6. Persist
            TrySave();

            // 7. Notify UI / presenters
            PublishRunUpdated();               // clear leaderboard, HUD

            Log("[DEBUG] FORCE CLEAR DONE");
        }

        private static void NormalizeComposition(ref RaceBotComposition comp, int need)
        {
            // nếu GD set tổng khác need: ưu tiên giữ Boss/Normal, phần còn lại đổ vào Noob
            var sum = comp.Total;
            if (sum == need) return;

            if (sum < need)
            {
                comp.NoobCount += (need - sum);
                return;
            }

            // sum > need => giảm Noob trước, rồi Normal, cuối cùng Boss
            int extra = sum - need;

            int dec = Math.Min(comp.NoobCount, extra);
            comp.NoobCount -= dec; extra -= dec;

            dec = Math.Min(comp.NormalCount, extra);
            comp.NormalCount -= dec; extra -= dec;

            dec = Math.Min(comp.BossCount, extra);
            comp.BossCount -= dec; extra -= dec;
        }

        private int AddFromPool(
            RaceRun run,
            IReadOnlyList<BotProfile> pool,
            BotPersonality type,
            int count,
            long utcNow,
            bool allowDuplicate = false)
        {
            if (count <= 0) return 0;
            if (pool == null || pool.Count == 0) return 0;

            // lọc candidates theo personality
            var candidates = new List<BotProfile>();
            for (int i = 0; i < pool.Count; i++)
                if (pool[i].Personality == type) candidates.Add(pool[i]);

            if (candidates.Count == 0) return 0;

            int added = 0;

            // pick random
            for (int k = 0; k < count; k++)
            {
                var pick = candidates[UnityEngine.Random.Range(0, candidates.Count)];

                if (!allowDuplicate)
                {
                    bool existed = false;
                    for (int j = 0; j < run.Opponents.Count; j++)
                        if (run.Opponents[j].Id == pick.Id) { existed = true; break; }

                    if (existed) { k--; continue; }
                }

                run.Opponents.Add(pick.ToRaceParticipant(utcNow));
                added++;
            }

            return added;
        }

        private void EnsurePersonalityCount(
                                            RaceRun run,
                                            IReadOnlyList<BotProfile> levelFilteredPool,
                                            IReadOnlyList<BotProfile> fullPool,
                                            BotPersonality type,
                                            int targetCount,
                                            long utcNow)
        {
            if (targetCount <= 0) return;

            int current = 0;
            for (int i = 0; i < run.Opponents.Count; i++)
                if (run.Opponents[i].IsBot && run.Opponents[i].Id.StartsWith(type.ToString().ToLowerInvariant()))
                    current++;

            int need = targetCount - current;
            if (need <= 0) return;

            // Ưu tiên: trong pool đã filter theo level trước
            need -= AddFromPool(run, levelFilteredPool, type, need, utcNow, allowDuplicate: false);

            // Nếu vẫn thiếu: lấy từ full pool (bất chấp level)
            if (need > 0)
                need -= AddFromPool(run, fullPool, type, need, utcNow, allowDuplicate: true);
        }

        private void FillRemainingClosestLevel(
                                                RaceRun run,
                                                IReadOnlyList<BotProfile> fullPool,
                                                int remaining,
                                                long utcNow,
                                                int playerLevel)
        {
            if (remaining <= 0) return;
            if (fullPool == null || fullPool.Count == 0) return;

            // lấy cụm closest (hàm sẵn có)
            var closestPool = FilterByPlayerLevelOrClosest(fullPool, playerLevel);
            if (closestPool == null || closestPool.Count == 0) closestPool = new List<BotProfile>(fullPool);

            // allowDuplicate true để guarantee đủ
            for (int i = 0; i < remaining; i++)
            {
                var pick = closestPool[UnityEngine.Random.Range(0, closestPool.Count)];
                run.Opponents.Add(pick.ToRaceParticipant(utcNow));
            }
        }

        private int AddAnyFromPool(
                                    RaceRun run,
                                    IReadOnlyList<BotProfile> pool,
                                    int count,
                                    long utcNow,
                                    bool allowDuplicate = true)
        {
            if (count <= 0) return 0;
            if (pool == null || pool.Count == 0) return 0;

            int added = 0;

            for (int k = 0; k < count; k++)
            {
                var pick = pool[UnityEngine.Random.Range(0, pool.Count)];

                if (!allowDuplicate)
                {
                    bool existed = false;
                    for (int j = 0; j < run.Opponents.Count; j++)
                        if (run.Opponents[j].Id == pick.Id) { existed = true; break; }
                    if (existed) { k--; continue; }
                }

                run.Opponents.Add(pick.ToRaceParticipant(utcNow));
                added++;
            }

            return added;
        }

        private static List<BotProfile> FilterByPlayerLevel(
            IReadOnlyList<BotProfile> pool,
            int playerLevel)
        {
            var list = new List<BotProfile>();
            for (int i = 0; i < pool.Count; i++)
            {
                var b = pool[i];
                if (playerLevel >= b.MinPlayerLevel &&
                    playerLevel <= b.MaxPlayerLevel)
                {
                    list.Add(b);
                }
            }
            return list;
        }

        private static List<BotProfile> FilterByPlayerLevelOrClosest(
           IReadOnlyList<BotProfile> pool,
           int playerLevel)
        {
            // 1) try exact match
            var exact = new List<BotProfile>();
            for (int i = 0; i < pool.Count; i++)
            {
                var b = pool[i];
                if (playerLevel >= b.MinPlayerLevel && playerLevel <= b.MaxPlayerLevel)
                    exact.Add(b);
            }
            if (exact.Count > 0) return exact;

            // 2) fallback: closest range
            int best = int.MaxValue;
            for (int i = 0; i < pool.Count; i++)
            {
                var b = pool[i];
                int d = DistanceToRange(playerLevel, b.MinPlayerLevel, b.MaxPlayerLevel);
                if (d < best) best = d;
            }

            // collect all bots with best distance (giữ đa dạng)
            var closest = new List<BotProfile>();
            for (int i = 0; i < pool.Count; i++)
            {
                var b = pool[i];
                int d = DistanceToRange(playerLevel, b.MinPlayerLevel, b.MaxPlayerLevel);
                if (d == best) closest.Add(b);
            }

            return closest;
        }

        private static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private static int DistanceToRange(int level, int min, int max)
        {
            if (level < min) return min - level;
            if (level > max) return level - max;
            return 0; // inside range
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


        public LeaderboardSnapshot GetLeaderboardSnapshot(int topN)
        {
            ThrowIfNotInitialized();
            if (_run == null) return LeaderboardSnapshot.Empty();

            // 1) lấy standings theo cùng 1 rule duy nhất
            // nếu RaceStandings.Compute đã sort đúng theo rule finishUtc / progress thì dùng lại luôn
            var standings = RaceStandings.Compute(_run.AllParticipants(), _run.GoalLevels);

            // 2) lấy rank player
            int playerRank = standings.FindIndex(p => p.Id == _run.Player.Id) + 1;
            if (playerRank <= 0) playerRank = standings.Count;

            // 3) topN
            var top = standings.Count <= topN ? standings : standings.GetRange(0, topN);

            return new LeaderboardSnapshot(top, playerRank, _run.Player.HasFinished);
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

        #region DEBUG
        /// <summary>
        /// DEBUG ONLY:
        /// Advance bots by exactly one logical step (≈ +1 level for active bots).
        /// No real time involved.
        /// </summary>
        public void Debug_AdvanceBots()
        {
            ThrowIfNotInitialized();
            if (_run == null) { Log("[DEBUG] AdvanceBots ignored (no run)."); return; }

            var utcNow = NowUtcSeconds();

            // baseUtc = mốc mới nhất trong run (để cộng dồn đúng)
            long baseUtc = utcNow;
            baseUtc = Math.Max(baseUtc, _run.Player.LastUpdateUtcSeconds);
            for (int i = 0; i < _run.Opponents.Count; i++)
                baseUtc = Math.Max(baseUtc, _run.Opponents[i].LastUpdateUtcSeconds);

            // Step: chọn theo BOT CHẬM NHẤT để chắc chắn có ai đó đủ +1
            // (dùng Max thay vì Avg để tránh trường hợp 341s < secPerLevel của bot)
            double maxSecPerLevel = 0;
            int alive = 0;
            foreach (var b in _run.Opponents)
            {
                if (b.HasFinished) continue;
                maxSecPerLevel = Math.Max(maxSecPerLevel, b.AvgSecondsPerLevel);
                alive++;
            }

            if (alive == 0) { Log("[DEBUG] AdvanceBots ignored (all bots finished)."); return; }

            long stepSeconds = (long)Math.Ceiling(maxSecPerLevel) + 1;

            // Retry up to N steps until something changes
            const int maxTries = 60; // 60 * ~6 phút = ~6 giờ (đủ thoát sleep 5h)
            int beforeHash = ComputeProgressHash2(_run);

            long fakeUtc = baseUtc;
            for (int attempt = 1; attempt <= maxTries; attempt++)
            {
                fakeUtc += stepSeconds;

                GhostBotSimulator.SimulateBots(_run, fakeUtc);

                int afterHash = ComputeProgressHash2(_run);
                if (afterHash != beforeHash)
                {
                    _debugFakeUtcSeconds = fakeUtc;
                    _save.CurrentRun = _run;
                    TrySave();
                    PublishRunUpdated();

                    var dt = DateTimeOffset.FromUnixTimeSeconds(fakeUtc).UtcDateTime;
                    Log($"[DEBUG] AdvanceBots => changed=TRUE after {attempt} step(s), step={stepSeconds}s, fakeUtc={fakeUtc} (UTC {dt:HH:mm})");
                    return;
                }
            }

            // Nếu tới đây vẫn false: hoặc bots đang bị rule chặn (sleep/stuck cực lâu) hoặc hash thiếu field
            var dt2 = DateTimeOffset.FromUnixTimeSeconds(fakeUtc).UtcDateTime;
            Log($"[DEBUG] AdvanceBots => changed=FALSE after {maxTries} tries. step={stepSeconds}s fakeUtc={fakeUtc} (UTC {dt2:HH:mm}). Consider logging sleep/stuck/progress remainder.");
        }

        /// <summary>
        /// DEBUG ONLY:
        /// Advance exactly ONE bot by ~1 logical step (≈ +1 level).
        /// botIndex: index in _run.Opponents
        /// </summary>
        public void Debug_AdvanceSingleBot(int botIndex)
        {
            ThrowIfNotInitialized();

            if (_run == null)
            {
                Log("[DEBUG] AdvanceSingleBot ignored (no run).");
                return;
            }

            if (botIndex < 0 || botIndex >= _run.Opponents.Count)
            {
                Log($"[DEBUG] AdvanceSingleBot invalid index={botIndex}");
                return;
            }

            var bot = _run.Opponents[botIndex];

            if (bot.HasFinished)
            {
                Log($"[DEBUG] AdvanceSingleBot ignored (bot {botIndex} already finished).");
                return;
            }

            // baseUtc = mốc mới nhất để tránh time đi lùi
            long baseUtc = NowUtcSeconds();
            baseUtc = Math.Max(baseUtc, bot.LastUpdateUtcSeconds);
            baseUtc = Math.Max(baseUtc, _run.Player.LastUpdateUtcSeconds);

            // stepSeconds = đủ để bot này chắc chắn +1 level
            long stepSeconds = (long)Math.Ceiling(bot.AvgSecondsPerLevel) + 1;

            const int maxTries = 30;
            int beforeLevel = bot.LevelsCompleted;

            long fakeUtc = baseUtc;

            for (int attempt = 1; attempt <= maxTries; attempt++)
            {
                fakeUtc += stepSeconds;

                // ⚠️ chỉ simulate bot này
                GhostBotSimulator.SimulateSingleBot(bot, _run.GoalLevels, fakeUtc);

                if (bot.LevelsCompleted > beforeLevel)
                {
                    bot.LastUpdateUtcSeconds = fakeUtc;

                    _debugFakeUtcSeconds = fakeUtc;
                    _save.CurrentRun = _run;
                    TrySave();
                    PublishRunUpdated();

                    var dt = DateTimeOffset.FromUnixTimeSeconds(fakeUtc).UtcDateTime;
                    Log($"[DEBUG] AdvanceSingleBot index={botIndex} SUCCESS after {attempt} try, level={bot.LevelsCompleted}, fakeUtc={fakeUtc} (UTC {dt:HH:mm})");
                    return;
                }
            }

            Log($"[DEBUG] AdvanceSingleBot index={botIndex} FAILED (no progress after {maxTries} tries)");
        }


        /// <summary>
        /// DEBUG ONLY:
        /// Advance bots from NOW until race end time.
        /// Guarantees no infinite loop.
        /// </summary>
        public void Debug_AdvanceBotsToEnd()
        {
            ThrowIfNotInitialized();
            if (_run == null)
            {
                Log("[DEBUG] AdvanceBotsToEnd ignored (no run).");
                return;
            }

            var nowUtc = NowUtcSeconds();

            // Base time: mốc mới nhất giữa now / player / bots
            long baseUtc = nowUtc;
            baseUtc = Math.Max(baseUtc, _run.Player.LastUpdateUtcSeconds);
            for (int i = 0; i < _run.Opponents.Count; i++)
                baseUtc = Math.Max(baseUtc, _run.Opponents[i].LastUpdateUtcSeconds);

            // Nếu race đã hết giờ → finalize luôn
            if (baseUtc >= _run.EndUtcSeconds)
            {
                FinalizeIfTimeUp(baseUtc);
                Log("[DEBUG] AdvanceBotsToEnd: race already ended.");
                return;
            }

            // Tính stepSeconds dựa trên bot chậm nhất (giống Debug_AdvanceBots)
            double maxSecPerLevel = 0;
            int alive = 0;
            foreach (var b in _run.Opponents)
            {
                if (b.HasFinished) continue;
                maxSecPerLevel = Math.Max(maxSecPerLevel, b.AvgSecondsPerLevel);
                alive++;
            }

            if (alive == 0)
            {
                FinalizeIfTimeUp(baseUtc);
                Log("[DEBUG] AdvanceBotsToEnd: all bots already finished.");
                return;
            }

            long stepSeconds = (long)Math.Ceiling(maxSecPerLevel) + 1;

            // Safety guard: tránh infinite loop
            const int maxSteps = 2000;

            int beforeHash = ComputeProgressHash2(_run);
            long fakeUtc = baseUtc;
            int steps = 0;

            while (fakeUtc < _run.EndUtcSeconds && steps < maxSteps)
            {
                fakeUtc += stepSeconds;
                if (fakeUtc > _run.EndUtcSeconds)
                    fakeUtc = _run.EndUtcSeconds;

                GhostBotSimulator.SimulateBots(_run, fakeUtc);

                int afterHash = ComputeProgressHash2(_run);

                // Nếu không còn thay đổi nữa → break sớm
                if (afterHash == beforeHash)
                {
                    bool anyAlive = false;
                    foreach (var b in _run.Opponents)
                    {
                        if (!b.HasFinished)
                        {
                            anyAlive = true;
                            break;
                        }
                    }

                    if (!anyAlive)
                        break;
                }

                beforeHash = afterHash;
                steps++;
            }

            // Finalize race nếu tới end
            FinalizeIfTimeUp(fakeUtc);

            _save.CurrentRun = _run;
            TrySave();
            PublishRunUpdated();

            var dt = DateTimeOffset.FromUnixTimeSeconds(fakeUtc).UtcDateTime;
            _debugFakeUtcSeconds = fakeUtc;
            Log($"[DEBUG] AdvanceBotsToEnd done. steps={steps}, fakeUtc={fakeUtc} (UTC {dt:HH:mm})");
        }


        private static int ComputeProgressHash2(RaceRun run)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + run.Player.LevelsCompleted;
                h = h * 31 + (run.Player.HasFinished ? 1 : 0);

                for (int i = 0; i < run.Opponents.Count; i++)
                {
                    h = h * 31 + run.Opponents[i].LevelsCompleted;
                    h = h * 31 + (run.Opponents[i].HasFinished ? 1 : 0);
                }
                return h;
            }
        }

        public void Debug_PlayerWinUsingFakeUtc()
        {
            ThrowIfNotInitialized();
            if (_run == null) { Log("[DEBUG] PlayerWin ignored (no run)"); return; }
            if (State != RaceEventState.InRace) { Log($"[DEBUG] PlayerWin ignored (State={State})"); return; }

            // if never used fakeUtc yet, fallback to real utcNow
            long utcNow = (_debugFakeUtcSeconds > 0) ? _debugFakeUtcSeconds
                                                     : NowUtcSeconds();

            _run.Player.LevelsCompleted += 1;
            _run.Player.LastUpdateUtcSeconds = utcNow;

            if (!_run.Player.HasFinished && _run.Player.LevelsCompleted >= _run.GoalLevels)
            {
                _run.Player.HasFinished = true;
                _run.Player.FinishedUtcSeconds = utcNow;
                UnityEngine.Debug.Log($"[RACE][PLAYER FINISH][FAKEUTC] levels={_run.Player.LevelsCompleted}/{_run.GoalLevels} finishUtc={utcNow}");
            }

            GhostBotSimulator.SimulateBots(_run, utcNow);

            _save.CurrentRun = _run;
            TrySave();
            PublishRunUpdated();

            FinalizeIfTimeUp(utcNow);
        }

        #endregion
    }

    public readonly struct LeaderboardSnapshot
    {
        public readonly IReadOnlyList<RaceParticipant> Top;
        public readonly int PlayerRank;      // 1-based
        public readonly bool PlayerFinished;

        public LeaderboardSnapshot(IReadOnlyList<RaceParticipant> top, int playerRank, bool playerFinished)
        {
            Top = top;
            PlayerRank = playerRank;
            PlayerFinished = playerFinished;
        }

        public static LeaderboardSnapshot Empty() => new LeaderboardSnapshot(Array.Empty<RaceParticipant>(), 0, false);
    }

    public readonly struct RaceHudStatus
    {
        public readonly bool IsVisible;
        public readonly bool IsSleeping;     // icon xám + zzz
        public readonly bool HasClaim;       // show "!" rung / claim now
        public readonly TimeSpan Remaining;  // countdown text
        public readonly string Label;        // optional: "NEXT RACE"
        public readonly bool ShowTextCountdown;

        public readonly bool IsLocked;
        public readonly int UnlockAtLevel;

        public RaceHudStatus(bool isVisible, bool isSleeping, bool hasClaim, TimeSpan remaining, string label, bool showTextCountdown)
         : this(isVisible, isSleeping, hasClaim, remaining, label, showTextCountdown, isLocked: false, unlockAtLevel: 0) { }

        public RaceHudStatus(bool isVisible, bool isSleeping, bool hasClaim, TimeSpan remaining, string label, bool showTextCountdown,
                        bool isLocked, int unlockAtLevel)
        {
            IsVisible = isVisible;
            IsSleeping = isSleeping;
            HasClaim = hasClaim;
            Remaining = remaining;
            Label = label;
            ShowTextCountdown = showTextCountdown;

            IsLocked = isLocked;
            UnlockAtLevel = unlockAtLevel;
        }
    }

    public enum RaceHudClickAction
    {
        None,
        OpenEntry,
        OpenInRace,
        OpenEnded
    }
}

using System;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed partial class RaceEventService
    {
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

            if (State != RaceEventState.InRace && State != RaceEventState.Ended)
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

                EndRaceNowAndFinalize();
                return;
            }

            GhostBotSimulator.SimulateBots(_run, utcNow);

            _save.CurrentRun = _run;
            TrySave();

            RefreshUiSnapshot();

            PublishRunUpdated();

            FinalizeIfTimeUp(utcNow);
        }
    }
}

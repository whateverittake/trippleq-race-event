using System;
using static TrippleQ.Event.RaceEvent.Runtime.PopupTypes;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed partial class RaceEventService
    {
        // --------------------
        // Run lifecycle
        // --------------------
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

            Log($"Run cleared: {reason}");

            PersistAndPublish();
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

                PersistAndPublish();

                Log("Bots simulated (tick)");
            }

            FinalizeIfTimeUp(utcNow);
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

        private void FinalizeIfTimeUp(long utcNow)
        {
            if (_run == null) return;
            if (_run.IsFinalized) return;

            if (utcNow < _run.EndUtcSeconds) return;

            //nếu chưa về đích, chưa extend, và config cho phép => offer extend
            if (ActiveConfigForRunOrCursor().AllowExtend1H && !_run.HasExtended && !_run.Player.HasFinished)
            {
                _sm.SetState(RaceEventState.ExtendOffer);
                _save.LastFlowState = RaceEventState.ExtendOffer;

                if (IsPopupActive(PopupType.Main) || IsPopupActive(PopupType.Info))
                {
                    RequestPopup(new PopupRequest(PopupType.Ended)); // dùng Ended popup, đổi nút thành Extend/Claimed tuỳ state
                }

                PersistAndPublish();

                Log("Time up -> ExtendOffer");
                return;
            }

            _run.IsFinalized = true;
            _run.FinalizedUtcSeconds = utcNow;

            var standings = RaceStandings.Compute(_run.AllParticipants(), _run.GoalLevels);

            for (int i = 0; i < standings.Count; i++)
            {
                var standing = standings[i];
                if (standing.HasFinished == false)
                {
                    Log("xx not finish");
                }
            }

            int rank = standings.FindIndex(p => p.Id == _run.Player.Id) + 1;
            _run.FinalPlayerRank = rank <= 0 ? standings.Count : rank;
            _run.WinnerId = standings.Count > 0 ? standings[0].Id : "";

            _save.CurrentRun = _run;

            _sm.SetState(RaceEventState.Ended);
            _save.LastFlowState = RaceEventState.Ended;

            if (IsPopupActive(PopupType.Main) || IsPopupActive(PopupType.Info))
            {
                RequestPopup(new PopupRequest(PopupType.Ended)); // dùng Ended popup, đổi nút thành Extend/Claimed tuỳ state
            }

            PersistAndPublish();

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

            PersistAndPublish();

            RequestPopup(new PopupRequest(PopupType.Main));
            Log($"Extend1H accepted. NewEndUtc={newEnd}");
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
    }
}

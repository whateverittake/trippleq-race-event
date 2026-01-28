namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed partial class RaceEventService
    {

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

            // Notify host to actually grant economy items
            OnRewardGranted?.Invoke(reward);

            PersistAndPublish();

            Log($"Claimed. Rank={_run.FinalPlayerRank}, RewardCoins={reward.Gold}");
        }

    }
}

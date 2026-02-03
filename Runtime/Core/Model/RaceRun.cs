#nullable enable
using System;
using System.Collections.Generic;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    [Serializable]
    public sealed class RaceRun
    {
        public int ConfigIndex;
        public string RunId = "";
        public long StartUtcSeconds;
        public long EndUtcSeconds;

        public int GoalLevels;          // e.g. 20
        public int PlayersCount;        // e.g. 5

        public RaceParticipant Player = new RaceParticipant();
        public List<RaceParticipant> Opponents = new List<RaceParticipant>();

        public bool IsFinalized;
        public long FinalizedUtcSeconds;
        public int FinalPlayerRank;
        public string WinnerId;

        public bool HasClaimed;
        public long ClaimedUtcSeconds;
        public int ClaimedRank;        // rank tại thời điểm claim (thường = FinalPlayerRank)
        public string ClaimedWinnerId; // winner tại thời điểm claim
        public string ClaimedRewardId; // hoặc json mini

        public bool HasExtended;                 // đã gia hạn chưa (chỉ 1 lần)
        public long OriginalEndUtcSeconds;       // để debug / UI
        public long ExtendedEndUtcSeconds;       // 0 nếu chưa extend

        public int WindowId;
        public int RoundIndex;
        public long DayResetUtcSeconds;
        public long NextAllowedStartUtcSeconds;

        // UI snapshot (computed by service/engine, view only reads)
        // NonSerialized để không lưu vào save.
        [NonSerialized] public LeaderboardSnapshot UiSnapshot;

        public IEnumerable<RaceParticipant> AllParticipants()
        {
            yield return Player;
            for (int i = 0; i < Opponents.Count; i++) yield return Opponents[i];
        }

        public bool IsActive(long utcNow) => utcNow >= StartUtcSeconds && utcNow < EndUtcSeconds;

        public bool IsEnded(long utcNow) => utcNow >= EndUtcSeconds;

        public int GetPlayerRank()
        {
            var standings = RaceStandings.Compute(AllParticipants(), GoalLevels);
            for (int i = 0; i < standings.Count; i++)
                if (standings[i].Id == Player.Id) return i + 1;
            return standings.Count;
        }

        public bool HasPlayerReachedGoal() => Player.LevelsCompleted >= GoalLevels;

        public static RaceRun CreateNew(string runId, long startUtc, long endUtc, int goalLevels, int playersCount)
        {
            return new RaceRun
            {
                RunId = runId,
                StartUtcSeconds = startUtc,
                EndUtcSeconds = endUtc,
                GoalLevels = goalLevels,
                PlayersCount = playersCount
            };
        }
    }
}

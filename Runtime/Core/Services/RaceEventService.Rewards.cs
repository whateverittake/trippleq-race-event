namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed partial class RaceEventService
    {
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
    }
}

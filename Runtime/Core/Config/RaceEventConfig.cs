using System;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    [Serializable]
    public struct RaceEventConfig
    {
        public bool Enabled;
        public int MinPlayerLevel;
        public bool BlockDuringTutorial;
        public int ResetHourLocal;
        public int EntryCooldownHours;
        public float SearchingDurationSeconds;
        /// <summary>
        /// How long to keep a claimed, finalized run before clearing it.
        /// Helps UX + prevents edge-case double-claim.
        /// </summary>
        public float KeepClaimedHours;

        public RaceBotComposition BotComposition;

        public int GoalLevels;       // ví dụ 20
        public int PlayersPerRace;   // ví dụ 5
        public int DurationHours;    // ví dụ 24

        public bool AllowExtend1H;
        public int ExtendHours;           // default 1

        public RaceReward Rank1Reward;
        public RaceReward Rank2Reward;
        public RaceReward Rank3Reward;
        public RaceReward Rank4Reward;
        public RaceReward Rank5Reward;

        [Space]
        public ExtendPayType ExtendPayType; // config quyết định pay kiểu gì
        public int ExtendCoinCost;          // dùng khi Coins
        public int ExtendAdsCount;          // dùng khi WatchAds (thường = 1)
    }

    [Serializable]
    public struct RaceBotComposition
    {
        public int BossCount;
        public int NormalCount;
        public int NoobCount;

        public int Total => BossCount + NormalCount + NoobCount;
    }

    [Serializable]
    public enum ExtendPayType
    {
        WatchAds = 0,
        Coins = 1,
    }
}

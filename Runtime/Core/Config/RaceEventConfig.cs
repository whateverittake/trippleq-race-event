using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    [Serializable]
    public struct RaceEventConfig
    {
        [Serializable]
        public struct RoundSettings
        {
            public int GoalLevels;
            public int PlayersPerRace;

            public RaceReward Rank1Reward;
            public RaceReward Rank2Reward;
            public RaceReward Rank3Reward;
            public RaceReward Rank4Reward;
            public RaceReward Rank5Reward;
        }

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

        public int DurationHours;    // ví dụ 24

        public bool AllowExtend1H;
        public int ExtendHours;           // default 1

        [Space]
        public ExtendPayType ExtendPayType; // config quyết định pay kiểu gì
        public int ExtendCoinCost;          // dùng khi Coins
        public int ExtendAdsCount;          // dùng khi WatchAds (thường = 1)

        [Header("Per-round settings (RoundIndex 0..2)")]
        public List<RoundSettings> Rounds;

        public RoundSettings GetRoundSettings(int roundIndex)
        {
            if (Rounds == null || Rounds.Count == 0)
            {
                return new RoundSettings();
            }

            if (Rounds != null && Rounds.Count > 0)
            {
                int idx = Mathf.Clamp(roundIndex, 0, Rounds.Count - 1);
                return Rounds[idx];
            }

            return new RoundSettings();
        }
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

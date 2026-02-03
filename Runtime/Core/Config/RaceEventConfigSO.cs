using System.Collections.Generic;
using UnityEngine;
using static TrippleQ.Event.RaceEvent.Runtime.RaceEventConfig;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    [CreateAssetMenu(
        fileName = "RaceEventConfig",
        menuName = "TrippleQ/Race Event/Config",
        order = 10)]
    public class RaceEventConfigSO : ScriptableObject
    {
        [Header("Feature")]
        public bool Enabled = true;

        [Header("Eligibility")]
        public int MinPlayerLevel = 10;
        public bool BlockDuringTutorial = true;
        public float KeepClaimedHours= 24f;

        [Header("Daily Window")]
        [Tooltip("Local reset hour (e.g. 4 = 4AM)")]
        [Range(0, 23)]
        public int ResetHourLocal = 4;

        [Tooltip("Cooldown (hours) after join. 0 = no cooldown")]
        [Min(0)]
        public int EntryCooldownHours = 0;

        [Header("Searching UI")]
        [Tooltip("Searching popup duration (seconds)")]
        [Min(0.1f)]
        public float SearchingDurationSeconds = 4f;

        [Header("Race Run")]
        [Min(1)] public int GoalLevels = 20;
        [Min(2)] public int PlayersPerRace = 5;
        [Min(1)] public int DurationHours = 24;

        [Header("Race Extend")]
        public bool AllowExtend1H = true;
        public int ExtendHours = 1;           // default 1

        [Header("Race Reward")]
        [Tooltip("2 ways to get reward (use bundleID follow Nhan core or simply int)")]
        public bool UseBundleId = true;

        [Header("Per-round settings (RoundIndex 0..2)")]
        public List<RoundSettings> Rounds;

        [Space]
        [SerializeField]
        private RaceBotComposition _botComposition = new RaceBotComposition
        {
            BossCount = 1,
            NormalCount = 1,
            NoobCount = 2
        };

        [Space]
        public ExtendPayType ExtendPayType; // config quyết định pay kiểu gì
        public int ExtendCoinCost;          // dùng khi Coins
        public int ExtendAdsCount;          // dùng khi WatchAds (thường = 1)

        /// <summary>
        /// Convert ScriptableObject to runtime config struct.
        /// </summary>
        public RaceEventConfig ToConfig()
        {
            return new RaceEventConfig
            {
                Enabled = Enabled,
                MinPlayerLevel = MinPlayerLevel,
                BlockDuringTutorial = BlockDuringTutorial,
                ResetHourLocal = ResetHourLocal,
                EntryCooldownHours = EntryCooldownHours,
                SearchingDurationSeconds = SearchingDurationSeconds,
                KeepClaimedHours = KeepClaimedHours,

                DurationHours = DurationHours,

                Rounds = Rounds,

                BotComposition = _botComposition,

                AllowExtend1H= AllowExtend1H,
                ExtendHours= ExtendHours,

                ExtendPayType= ExtendPayType,
                ExtendCoinCost= ExtendCoinCost,
                ExtendAdsCount= ExtendAdsCount,
            };
        }
    }
}

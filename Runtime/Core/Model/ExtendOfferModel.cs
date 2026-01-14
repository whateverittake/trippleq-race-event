using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public readonly struct ExtendOfferModel
    {
        public readonly ExtendPayType PayType;
        public readonly int CoinCost;
        public readonly int ExtendHours;

        public ExtendOfferModel(ExtendPayType payType, int coinCost, int extendHours)
        {
            PayType = payType;
            CoinCost = coinCost;
            ExtendHours = extendHours;
        }

        public static ExtendOfferModel None()
            => new ExtendOfferModel(ExtendPayType.Coins, 0, 0);
    }
}

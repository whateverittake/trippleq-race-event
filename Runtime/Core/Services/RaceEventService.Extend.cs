using System;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed partial class RaceEventService
    {
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
    }
}

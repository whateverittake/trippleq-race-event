using System;
using TrippleQ.UiKit;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed class RaceEndPopupPresenter : BasePopupPresenter<IRaceEndPopupView>
    {
        private readonly RaceEventService _svc;

        private RaceReward _currentReward;

        public Action NotEnoughCoins;

        public event Action<int, Action<bool>> RequestExtendByCoins;

        public RaceEndPopupPresenter(RaceEventService svc)
        {
            _svc = svc;
        }

        protected override void OnBind()
        {
            View.SetOnClose(Hide);
            View.SetClose(Hide);
            View.SetOnClaim(OnClaim);
            View.SetOnExtend(OnExtend);
            View.SetOnCloseToOpenExtendView(OpenLastChance);
            View.SetOnCloseWithoutExtend(OnCloseWithoutExtend);
            View.SetOnWatchAds(OnWatchAds);
            View.SetOnAcceptNoReward(OnAcceptNoReward);
            View.SetOnAcceptToHideView(OnAcceptToHide);

            _svc.OnStateChanged += OnStateChanged;
            _svc.OnRunUpdated += OnRunUpdated;

            Render();
        }

        protected override void OnUnbind()
        {
            _svc.OnStateChanged -= OnStateChanged;
            _svc.OnRunUpdated -= OnRunUpdated;

            View.SetOnClose(null);
            View.SetClose(null);
            View.SetOnClaim(null);
            View.SetOnExtend(null);
            View.SetOnAcceptNoReward(null);
            View.SetOnCloseToOpenExtendView(null);
            View.SetOnCloseWithoutExtend(null);
            View.SetOnWatchAds(null);
        }

        private void OnStateChanged(RaceEventState a, RaceEventState b) => Render();
        private void OnRunUpdated(RaceRun? run) => Render();

        private void Render()
        {
            if (View == null) return;
            if (_svc == null) return;

            //reward +postions rank

            var run = _svc.CurrentRun;
            if (run == null)
            {
                // không có run thì không hiện gì cả
                return;
            }

            //// đảm bảo đúng assumption: chỉ hiện khi finalized
            //if (!run.IsFinalized) return;

            // Setup base data (rank/opponents/reward)
            SetUpData(run);
            bool canClaim = _svc.CanClaim();
            View.ShowReward(canClaim);
            // Decide which mode this popup is in
            bool canExtend = _svc.CanExtend1H();

            // View state
            bool playerReachGoal = run.Player.HasFinished;

            if (!playerReachGoal)
            {
                if (canExtend)
                {
                    View.SetViewState(RaceEndPopupState.CanExtend, null);
                    View.SetClaimVisible(false);
                    View.SetExtendVisible(true);
                }
                else
                {
                    // Time’s up & not finished: show lose/timeout layout, NO claim
                    View.SetViewState(RaceEndPopupState.NoReward, null);
                    View.SetClaimVisible(false);
                    View.SetExtendVisible(false);
                }
                return;
            }

            View.SetClaimVisible(true);
            View.SetExtendVisible(false);

            if (run.FinalPlayerRank == 1)
            {
                if (_currentReward == null)
                {
                    View.SetViewState(RaceEndPopupState.FirstPlace, null);
                }
                else
                {
                    View.SetViewState(RaceEndPopupState.FirstPlace, new RewardData(_currentReward.Gold, _currentReward.Gems, _currentReward.Booster1, _currentReward.Booster2, _currentReward.Booster3, _currentReward.Booster4));
                }
            }
            else
            {
                if (_currentReward == null)
                {
                    View.SetViewState(RaceEndPopupState.NormalPlace, null);
                }
                else
                {
                    View.SetViewState(RaceEndPopupState.NormalPlace, new RewardData(_currentReward.Gold, _currentReward.Gems, _currentReward.Booster1, _currentReward.Booster2, _currentReward.Booster3, _currentReward.Booster4));
                }
            }
        }

        private void SetUpData(RaceRun run)
        {
            // 1) leaderboard snapshot (source of truth)
            var snap = _svc.BuildLeaderboardSnapshot(5); // hoặc _leaderBoardRanks.Length nếu view expose

            // 2) reward: chỉ dựa vào rank khi canClaim hoặc rank1 special
            RaceReward reward = null;

            bool canClaim = _svc.CanClaim();

            int rank = run.FinalPlayerRank;
            if (_svc.CanClaim())
                reward = _svc.GetRewardForRank(rank);

            _currentReward = reward;

            // 3) push data to view (NO sorting in view)
            View.SetDataLeaderBoard(snap.Top, snap.PlayerRank);
            View.RenderLeaderBoard();
            View.RenderUserReward();
        }

        private void OnClaim()
        {
            _svc.Claim();
            View.OpenChestAnim();

            // _currentReward => anim?

            //add reward
            //sample only in bootstrap
            //_svc.OnRewardGranted += reward =>
            //{
            //    // 1) Economy
            //    economy.AddGold(reward.Gold);
            //    economy.AddGems(reward.Gems);

            //    // 2) Save (nếu economy không tự save)
            //    save.Commit();

            //    // 3) Analytics
            //    analytics.LogRaceReward(reward);

            //    // 4) UI/Anim (nếu muốn)
            //    // ui.ShowRewardFly(reward);
            //};
            //Render(); // service có thể bắn event, nhưng render ngay cho chắc
        }

        private void OnAcceptToHide()
        {
            Hide();
        }

        private void OnExtend()
        {

            var offer = _svc.GetExtendOffer();

            if (offer.PayType == ExtendPayType.Coins)
            {
                int coinNeed = offer.CoinCost;

                if (RequestExtendByCoins == null)
                {
                    NotEnoughCoins?.Invoke();
                    return;
                }

                RequestExtendByCoins.Invoke(coinNeed, approved =>
                {
                    if (!approved)
                    {
                        NotEnoughCoins?.Invoke();
                        return;
                    }

                    _svc.Extend1H();
                    Render();
                });

                return;
            }

            // Ads / Free
            _svc.Extend1H();
            Render();
        }

        private void OnCloseWithoutExtend()
        {
            _svc.DeclineExtend();
        }

        private void OnAcceptNoReward()
        {
            Hide();
        }

        private void OpenLastChance()
        {
            View.SetViewState(RaceEndPopupState.LastChance, null);

            var offer = _svc.GetExtendOffer();

            switch (offer.PayType)
            {
                case ExtendPayType.WatchAds:
                    View.ShowAdsBtn(true);
                    break;

                case ExtendPayType.Coins:
                    View.ShowAdsBtn(false);
                    View.ShowPayCoins(offer.CoinCost);
                    break;
            }
        }

        private void OnWatchAds()
        {
            _svc.RequestWatchAdsToExtend();
        }
    }
}

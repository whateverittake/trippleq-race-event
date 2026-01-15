using Codice.CM.Common;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Analytics;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceEventBootstrap : MonoBehaviour
    {
        [SerializeField] List<RaceEventConfigSO> _configSOs;
        [SerializeField] bool _isInDev=false;

        private RaceEventService _svc;

        public RaceEventService Service => _svc;
        public event Action<RaceEventService> OnServiceReady;

        private JsonRaceStorage _pendingStorage;
        private List<RaceEventConfig> _pendingConfigs;
        private BotPoolJson _pendingPool;

        private Action<RaceReward>? _boundRewardHandler;
        private Action<Action<bool>>? _boundWatchAdsHandler;

        private Action<int, Action<bool>> _boundSpendGoldHandler;
        private Action _boundNotEnoughCoinHandler;

        private void Awake()
        {
            var kv = new FileKeyValueStorage("TrippleQ.RaceEvent.Save");
            var storage = new JsonRaceStorage(kv);

            _svc = new RaceEventService();
            _svc.OnLog += Debug.Log;

            var runtimeConfigs = new List<RaceEventConfig>(_configSOs.Count);
            for (int i = 0; i < _configSOs.Count; i++)
            {
                var so = _configSOs[i];
                if (so == null) continue;
                runtimeConfigs.Add(so.ToConfig()); // snapshot
            }

            if (_isInDev)
            {
                StartCoroutine(JsonBotPoolLoader.LoadOrFallbackAsync(pool =>
                {
                    _svc.Initialize(configs: runtimeConfigs,
                                    storage: storage,
                                    initialLevel: 10,
                                    isInTutorial: false,
                                    pool);

                    OnServiceReady?.Invoke(_svc);
                }));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                // cheat để hàm OnRequestExtend luôn chạy (approve luôn)
                OnRequestExtend((coinNeed, cb) =>
                {
                    Debug.Log($"[CHEAT] RequestSpendGold coinNeed={coinNeed} => APPROVE");
                    cb?.Invoke(false);
                });

                // cheat để OnNotEnoughCoinToExtend debug ra string (khi bị gọi)
                OnNotEnoughCoinToExtend(() =>
                {
                    Debug.Log("[CHEAT] OpenNotEnoughCoinUI called");
                });
#endif
            }
            else
            {
                StartCoroutine(JsonBotPoolLoader.LoadOrFallbackAsync(pool =>
                {
                    _pendingStorage = storage;
                    _pendingConfigs = runtimeConfigs;
                    _pendingPool = pool;

                    OnServiceReady?.Invoke(_svc);
                }));
            }
        }

        public void Initialize(int playerLevel, bool isInTutorial)
        {
            _svc.Initialize(
                configs: _pendingConfigs,
                storage: _pendingStorage,
                initialLevel: playerLevel,
                isInTutorial: isInTutorial,
                botPool: _pendingPool
            );
        }

        public void OnClaimRewardRace(Action<RaceReward> claimAction)
        {
            // chống bind nhiều lần
            if (_boundRewardHandler != null)
                _svc.OnRewardGranted -= _boundRewardHandler;

            _boundRewardHandler = reward => claimAction?.Invoke(reward);
            _svc.OnRewardGranted += _boundRewardHandler;
        }

        public void CheatResetRace()
        {
            _svc.Debug_ResetAfterClaimAndAllowNewRun();
        }

        public void CheatPlusBot()
        {
            _svc.Debug_AdvanceBots();
        }

        public void CheatEndRace()
        {
            _svc.DebugEndEvent();
        }

        public void CheatPlayerWinUsingFakeUtc()
        {
            _svc.Debug_PlayerWinUsingFakeUtc();
        }

        public void CheatBotIndex(int index)
        {
            _svc.Debug_AdvanceSingleBot(index);
        }

        private void Update()
        {
            if(_svc.IsInitialized) _svc.Tick(Time.deltaTime);

            if (Input.GetKeyDown(KeyCode.P)) // debug
            {
                CheatResetRace();
            }

            if (Input.GetKeyDown(KeyCode.O)) // debug
            {
                CheatPlusBot();
            }

            if (Input.GetKeyDown(KeyCode.K)) // debug
            {
                _svc.Debug_AdvanceBotsToEnd();
            }

            if(Input.GetKeyDown(KeyCode.I)) // debug
            {
                //DebugWinLevel();
                CheatPlayerWinUsingFakeUtc();
            }

            if(Input.GetKeyDown(KeyCode.U)) // debug
            {
                CheatEndRace();
            }

            if (Input.GetKeyDown(KeyCode.M))
            {
                DebugWinLevel();
            }
        }

        public void DebugWinLevel()
        {
            _svc.OnLevelWin(
                newLevel: _svc.CurrentLevel + 1,
                isInTutorial: false,
                localNow: DateTime.Now
            );
        }

        private void OnDestroy()
        {
            _svc.Dispose();
        }

        public void NotifyLevelWin(int newLevel, bool isInTutorial, DateTime localNow)
        {
            if (_svc == null) return;              // hoặc throw nếu bạn muốn strict
            if (!_svc.IsInitialized) return;       // nếu service bạn có cờ init thì check

            _svc.OnLevelWin(
                newLevel: newLevel,
                isInTutorial: isInTutorial,
                localNow: localNow
            );
        }

        public void BindWatchAdsToExtend(Action<Action<bool>> watchAdsAction)
        {
            // chống bind nhiều lần
            if (_boundWatchAdsHandler != null)
                _svc.OnExtendAdsRequested -= _boundWatchAdsHandler;

            _boundWatchAdsHandler = onResult =>
            {
                // delegate hết cho framework/project
                watchAdsAction?.Invoke(onResult);
            };

            _svc.OnExtendAdsRequested += _boundWatchAdsHandler;
        }

        public void OnRequestExtend(Action<int, Action<bool>> spendGoldHandler)
        {
            _boundSpendGoldHandler = null;
            _boundSpendGoldHandler = spendGoldHandler;
        }

        public void OnNotEnoughCoinToExtend(Action action)
        {
            _boundNotEnoughCoinHandler = null;
            _boundNotEnoughCoinHandler = action;
        }

        public void RequestSpendGold(int coinNeed, Action<bool> reply)
        {
            // nếu chưa bind handler thì mặc định fail
            if (_boundSpendGoldHandler == null)
            {
                reply?.Invoke(false);
                return;
            }

            bool called = false;

            void Once(bool ok)
            {
                if (called) return;
                called = true;

                //if (!ok) OpenNotEnoughCoinUI();
                reply?.Invoke(ok);
            }

            _boundSpendGoldHandler.Invoke(coinNeed, Once);
        }


        public void OpenNotEnoughCoinUI()
        {
            _boundNotEnoughCoinHandler?.Invoke();
        }
    }
}

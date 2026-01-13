using System;
using System.Collections.Generic;
using UnityEngine;

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

        private void Update()
        {
            if(_svc.IsInitialized) _svc.Tick(Time.deltaTime);

            if (Input.GetKeyDown(KeyCode.R)) // debug
            {
                _svc.Debug_ResetAfterClaimAndAllowNewRun();
            }

            if(Input.GetKeyDown(KeyCode.T)) // debug
            {
                _svc.OnEnterMain(
                    isInTutorial: false,
                    localNow: DateTime.Now
                );
            }

            if (Input.GetKeyDown(KeyCode.L)) // debug
            {
                _svc.Debug_AdvanceBots();
            }

            if (Input.GetKeyDown(KeyCode.K)) // debug
            {
                _svc.Debug_AdvanceBotsToEnd();
            }

            if(Input.GetKeyDown(KeyCode.J)) // debug
            {
                //DebugWinLevel();
                _svc.Debug_PlayerWinUsingFakeUtc();
            }

            if(Input.GetKeyDown(KeyCode.H)) // debug
            {
                _svc.DebugEndEvent();
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
    }
}

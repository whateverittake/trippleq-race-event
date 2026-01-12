using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceEventBootstrap : MonoBehaviour
    {
        [SerializeField] List<RaceEventConfigSO> _configSOs;

        private RaceEventService _svc;

        public RaceEventService Service => _svc;
        public event Action<RaceEventService> OnServiceReady;

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

        private void Update()
        {
            _svc.Tick(Time.deltaTime);

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
                DebugWinLevel();
            }

            if(Input.GetKeyDown(KeyCode.H)) // debug
            {
                _svc.DebugEndEvent();
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
    }
}

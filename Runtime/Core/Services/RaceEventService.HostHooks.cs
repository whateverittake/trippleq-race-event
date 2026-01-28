using System;
using static TrippleQ.Event.RaceEvent.Runtime.PopupTypes;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed partial class RaceEventService
    {
        // --------------------
        // Host hooks
        // --------------------
        public void OnEnterInfo()
        {
            ThrowIfNotInitialized();
            Log("OnEnterInfo()");

            RequestPopup(new PopupRequest(PopupType.Info));
        }

        /// <summary>
        /// Host calls this when entering main screen/home scene.
        /// </summary>
        public void OnEnterMain(bool isInTutorial, DateTime localNow)
        {
            ThrowIfNotInitialized();
            Log("OnEnterMain()");

            RefreshEligibility(isInTutorial, localNow);

            if (ShouldShowEntryPopup(isInTutorial, localNow))
            {
                RequestPopup(new PopupRequest(PopupType.Entry));
            }

            var utcNow = NowUtcSeconds();
            if (_run != null && State == RaceEventState.InRace)
            {
                GhostBotSimulator.SimulateBots(_run, utcNow);
                _save.CurrentRun = _run;

                PersistAndPublish();
            }
        }

        /// <summary>
        /// Host calls this after player wins a level.
        /// </summary>
        public void OnLevelWin(int newLevel, bool isInTutorial, DateTime localNow)
        {
            ThrowIfNotInitialized();

            if (newLevel < 0) newLevel = 0;

            if (newLevel != CurrentLevel)
            {
                CurrentLevel = newLevel;
                Log($"OnLevelWin(): CurrentLevel={CurrentLevel}");
            }

            RefreshEligibility(isInTutorial, localNow);

            var max = ActiveConfigForRunOrCursor().GoalLevels;

            if (_run != null && State == RaceEventState.InRace)
            {
                var utcNow = NowUtcSeconds();

                _run.Player.LevelsCompleted += 1;
                _run.Player.LevelsCompleted = Math.Min(_run.Player.LevelsCompleted, max);
                _run.Player.LastUpdateUtcSeconds = utcNow;

                if (!_run.Player.HasFinished && _run.Player.LevelsCompleted >= _run.GoalLevels)
                {
                    _run.Player.HasFinished = true;
                    _run.Player.FinishedUtcSeconds = utcNow;

                    Log(
                            $"[RACE][PLAYER FINISH] levels={_run.Player.LevelsCompleted}/{_run.GoalLevels} finishUtc={utcNow}"
                        );

                    EndRaceNowAndFinalize();
                    return;
                }

                GhostBotSimulator.SimulateBots(_run, utcNow);

                _save.CurrentRun = _run;

                PersistAndPublish();

                // NOTE: no early end here
                FinalizeIfTimeUp(utcNow);
            }
        }

    }
}

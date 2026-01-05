
using System;
using static UnityEngine.CullingGroup;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public class RaceEventStateMachine
    {
        public event Action<RaceEventState, RaceEventState>? OnStateChanged;

        public RaceEventState State { get; private set; } = RaceEventState.Disabled;

        public void SetState(RaceEventState newState)
        {
            if (State == newState) return;

            var old = State;
            State = newState;
            OnStateChanged?.Invoke(old, newState);
        }
    }
}

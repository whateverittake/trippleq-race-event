using System.Collections.Generic;
using UnityEngine;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    [CreateAssetMenu(menuName = "TrippleQ/Race Event/Bot Pool")]
    public sealed class RaceBotPoolSO : ScriptableObject
    {
        public List<BotProfile> Bots = new();
    }
}
